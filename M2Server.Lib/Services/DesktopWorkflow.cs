using System.Diagnostics;
using System.Runtime.Versioning;
using M2Server.Lib.Domain;
using M2Server.Lib.Infrastructure;

namespace M2Server.Lib.Services;

/// <summary>Local desktop operations used by the Avalonia shell.</summary>
[SupportedOSPlatform("windows")]
public sealed class DesktopWorkflow(DataStore store, DisplayService displays)
{
    public Guid CreatePreset()
    {
        var names = store.Read(x => x.Presets.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase));
        var index = 1;
        while (names.Contains($"Profile {index}"))
        {
            index++;
        }

        var preset = new Preset
        {
            Name = $"Profile {index}",
            Monitors = displays.GetMonitors()
                .Select((m, i) => new MonitorSetting
                    {
                        Id = m.Id,
                        Name = m.Name,
                        Enabled = m.Enabled,
                        Primary = m.Primary,
                        Order = i,
                        Mode = m.CurrentMode ?? m.Modes.FirstOrDefault() ?? new DisplayMode(1920, 1080, 60),
                        Rotation = m.Rotation,
                        DpiScale = m.CurrentDpiScale,
                        HdrEnabled = m.HdrEnabled
                    }
                )
                .ToList()
        };
        store.Update(x =>
            {
                x.Presets.Add(preset);
                return 0;
            }
        );
        return preset.Id;
    }

    public void RenamePreset(Guid id, string name)
    {
        store.Update(x =>
            {
                x.Presets.First(p => p.Id == id).Name = name;
                return 0;
            }
        );
    }

    public void RemovePreset(Guid id)
    {
        store.Update(x =>
            {
                x.Presets.RemoveAll(p => p.Id == id);
                if (x.ActivePresetId == id)
                {
                    x.ActivePresetId = null;
                }

                return 0;
            }
        );
    }

    public ActivationResult ActivatePreset(Guid id)
    {
        var preset = store.Read(x => x.Presets.First(p => p.Id == id));
        return displays.Activate(preset);
    }

    public static IReadOnlyList<MonitorSetting> EditorMonitors(Preset preset, IReadOnlyList<MonitorInfo> connected)
    {
        var monitors = preset.Monitors.ToList();
        foreach (var live in connected.Where(m
                     => monitors.All(saved => !saved.Id.Equals(m.Id, StringComparison.OrdinalIgnoreCase))
                 ))
        {
            monitors.Add(NewInactiveMonitor(live, monitors.Count));
        }

        return monitors;
    }

    public void UpdateMonitor(Guid presetId, string monitorId, Action<MonitorSetting> change, MonitorSetting? newMonitor = null)
    {
        store.Update(x =>
            {
                var preset = x.Presets.First(p => p.Id == presetId);
                var monitor = preset.Monitors.FirstOrDefault(m => m.Id.Equals(monitorId, StringComparison.OrdinalIgnoreCase));
                if (monitor is null)
                {
                    if (newMonitor is null)
                    {
                        throw new InvalidOperationException("Monitor is not in the profile.");
                    }

                    monitor = CopyMonitor(newMonitor);
                    preset.Monitors.Add(monitor);
                }

                change(monitor);
                if (monitor.Primary)
                {
                    monitor.Enabled = true;
                    foreach (var other in preset.Monitors.Where(m => m.Id != monitorId))
                    {
                        other.Primary = false;
                    }
                }

                return 0;
            }
        );
    }

    public void SetPrimary(Guid presetId, string monitorId, MonitorSetting? newMonitor = null)
    {
        store.Update(x =>
            {
                var preset = x.Presets.First(p => p.Id == presetId);
                if (preset.Monitors.All(m => !m.Id.Equals(monitorId, StringComparison.OrdinalIgnoreCase)))
                {
                    if (newMonitor is null)
                    {
                        throw new InvalidOperationException("Monitor is not in the profile.");
                    }

                    preset.Monitors.Add(CopyMonitor(newMonitor));
                }

                foreach (var monitor in preset.Monitors)
                {
                    monitor.Primary = monitor.Id == monitorId;
                }

                preset.Monitors.First(m => m.Id == monitorId).Enabled = true;
                return 0;
            }
        );
    }

    public void RemoveMonitor(Guid presetId, string monitorId)
    {
        store.Update(x => x.Presets.First(p => p.Id == presetId).Monitors.RemoveAll(m => m.Id == monitorId));
    }

    public void MoveMonitor(Guid presetId, string monitorId, int direction)
    {
        store.Update(x =>
            {
                var monitors = x.Presets.First(p => p.Id == presetId).Monitors.OrderBy(m => m.Order).ToList();
                var index = monitors.FindIndex(m => m.Id == monitorId);
                var target = index + direction;
                if (index >= 0 && target >= 0 && target < monitors.Count)
                {
                    (monitors[index].Order, monitors[target].Order) = (monitors[target].Order, monitors[index].Order);
                }

                return 0;
            }
        );
    }

    private static MonitorSetting NewInactiveMonitor(MonitorInfo live, int order)
    {
        return new MonitorSetting
        {
            Id = live.Id,
            Name = live.Name,
            Enabled = false,
            Order = order,
            Mode = live.CurrentMode ?? live.Modes.FirstOrDefault() ?? new DisplayMode(1920, 1080, 60),
            Rotation = live.Rotation,
            DpiScale = live.CurrentDpiScale,
            HdrEnabled = live.HdrEnabled
        };
    }

    private static MonitorSetting CopyMonitor(MonitorSetting source)
    {
        return new MonitorSetting
        {
            Id = source.Id,
            Name = source.Name,
            Enabled = source.Enabled,
            Primary = source.Primary,
            Order = source.Order,
            Mode = source.Mode,
            Rotation = source.Rotation,
            DpiScale = source.DpiScale,
            HdrEnabled = source.HdrEnabled
        };
    }

    public Guid CreateScript()
    {
        var id = store.Update(x =>
            {
                var draft = x.Scripts.FirstOrDefault(s => string.IsNullOrWhiteSpace(s.Name) || s.Name == "New script");
                if (draft is null)
                {
                    draft = new ScriptEntry();
                    x.Scripts.Add(draft);
                }

                var names = x.Scripts.Where(s => s.Id != draft.Id)
                    .Select(s => s.Name)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var index = 1;
                while (names.Contains($"Script {index}"))
                {
                    index++;
                }

                draft.Name = $"Script {index}";
                return draft.Id;
            }
        );
        return id;
    }

    public void RenameScript(Guid id, string name)
    {
        store.Update(x =>
            {
                x.Scripts.First(s => s.Id == id).Name = name;
                return 0;
            }
        );
    }

    public void UpdateScript(Guid id, string text)
    {
        store.Update(x =>
            {
                x.Scripts.First(s => s.Id == id).Script = text;
                return 0;
            }
        );
    }

    public void RemoveScript(Guid id)
    {
        store.Update(x => x.Scripts.RemoveAll(s => s.Id == id));
    }

    public async Task<string> RunScriptAsync(Guid id)
    {
        if (!store.Read(x => x.Scripts.Any(s => s.Id == id)))
        {
            throw new InvalidOperationException("Script not found.");
        }

        var info = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden
        };
        info.ArgumentList.Add(Constants.RunScriptArgument);
        info.ArgumentList.Add(id.ToString());
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start script.");
        await process.WaitForExitAsync();
        return $"Exit code {process.ExitCode}";
    }
}