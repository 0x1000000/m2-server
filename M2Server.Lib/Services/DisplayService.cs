using M2Server.Lib.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace M2Server.Lib.Services;

public sealed class DisplayService
{
    private const string ActivationMutexName = @"Local\M2Server.DisplayActivation";
    private const int MaxAttempts = 6;
    private static readonly TimeSpan ActivationLimit = TimeSpan.FromSeconds(10);
    private readonly ILogger<DisplayService> _logger;
    private readonly IDisplayPlatform _platform;
    private readonly TimeProvider _timeProvider;

    public DisplayService(
        ILogger<DisplayService>? logger = null,
        IDisplayPlatform? platform = null,
        TimeProvider? timeProvider = null)
    {
        this._logger = logger ?? NullLogger<DisplayService>.Instance;
        this._platform = platform ?? new WindowsDisplayPlatform();
        this._timeProvider = timeProvider ?? TimeProvider.System;
    }

    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        return this._platform.GetMonitors();
    }

    public IReadOnlyList<MonitorInfo> GetEditorMonitors()
    {
        var monitors = this.GetMonitors().ToList();
        var knownIds = monitors.Select(monitor => monitor.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var target in this._platform.AvailableTargets().Where(target => knownIds.Add(target.Path)))
        {
            monitors.Add(
                new MonitorInfo(
                    target.Path,
                    target.Name,
                    target.SourceNames.FirstOrDefault() ?? "",
                    false,
                    false,
                    null,
                    [],
                    int.MaxValue,
                    0,
                    [],
                    null,
                    false,
                    null,
                    target.Rotation
                )
            );
        }

        return monitors;
    }

    public IReadOnlyList<string> DiagnoseCurrentModes()
    {
        return this._platform.DiagnoseCurrentModes();
    }

    /// <summary>
    ///     Finds every profile represented by the current Windows display state.
    /// </summary>
    public IReadOnlySet<Guid> DetectActivePresets(IEnumerable<Preset> presets)
    {
        var monitors = this.GetMonitors();
        var activePresetIds = new HashSet<Guid>();
        foreach (var preset in presets)
        {
            if (MatchesCurrentDisplays(preset, monitors))
            {
                activePresetIds.Add(preset.Id);
            }
        }

        return activePresetIds;
    }

    private static bool MatchesCurrentDisplays(Preset preset, IReadOnlyList<MonitorInfo> monitors)
    {
        var settings = preset.Monitors.Where(setting => setting.Enabled).OrderBy(setting => setting.Order).ToList();
        if (settings.Count == 0 || settings.Count(setting => setting.Primary) != 1)
        {
            return false;
        }

        var enabledIds = monitors.Where(monitor => monitor.Enabled)
            .Select(monitor => monitor.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var expectedIds = settings.Select(setting => setting.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!expectedIds.SetEquals(enabledIds))
        {
            return false;
        }

        var expectedX = -settings.TakeWhile(setting => !setting.Primary).Sum(OrientedWidth);
        foreach (var setting in settings)
        {
            var monitor = monitors.FirstOrDefault(candidate => candidate.Id.Equals(setting.Id, StringComparison.OrdinalIgnoreCase)
            );
            if (monitor is null || !monitor.Enabled || monitor.Primary != setting.Primary ||
                monitor.CurrentMode != setting.Mode || monitor.PositionX != expectedX || monitor.PositionY != 0 ||
                monitor.Rotation != setting.Rotation || (setting.DpiScale is { } scale && monitor.CurrentDpiScale != scale) ||
                (setting.HdrEnabled is { } hdr && monitor.HdrEnabled != hdr &&
                 !(!hdr && !monitor.HdrSupported && monitor.HdrEnabled is null)))
            {
                return false;
            }

            expectedX += OrientedWidth(setting);
        }

        return true;
    }

    public ActivationResult Activate(Preset preset, bool dryRun = false)
    {
        var operation = Guid.NewGuid();
        this._logger.LogInformation(
            "Activation {Operation}: starting profile {Profile} ({ProfileId}), dryRun={DryRun}",
            operation,
            preset.Name,
            preset.Id,
            dryRun
        );
        foreach (var setting in preset.Monitors)
        {
            this._logger.LogInformation(
                "Activation {Operation}: requested monitor {Name} ({Id}), enabled={Enabled}, primary={Primary}, order={Order}, mode={Mode}, rotation={Rotation}, dpi={Dpi}, hdr={Hdr}",
                operation,
                setting.Name,
                setting.Id,
                setting.Enabled,
                setting.Primary,
                setting.Order,
                setting.Mode,
                setting.Rotation,
                setting.DpiScale,
                setting.HdrEnabled
            );
        }

        try
        {
            var result = this.ActivateWithRetries(preset, dryRun, operation);
            this._logger.Log(
                result.Success ? LogLevel.Information : LogLevel.Warning,
                "Activation {Operation}: result success={Success}; {Message}",
                operation,
                result.Success,
                result.Message
            );
            foreach (var step in result.Steps ?? [])
            {
                this._logger.Log(
                    step.Success ? LogLevel.Information : LogLevel.Warning,
                    "Activation {Operation}: phase {Phase} success={Success}; {Message}; WindowsError={WindowsError}",
                    operation,
                    step.Phase,
                    step.Success,
                    step.Message,
                    step.WindowsError
                );
            }

            foreach (var monitor in result.Monitors ?? [])
            {
                this._logger.Log(
                    monitor.Warnings.Count == 0 && monitor.WindowsError is null ? LogLevel.Information : LogLevel.Warning,
                    "Activation {Operation}: monitor {Name} ({Id}); topology={Topology}, primary={Primary}, mode={Mode}, position={Position}, dpi={Dpi}, hdr={Hdr}; WindowsError={WindowsError}; warnings={Warnings}",
                    operation,
                    monitor.Name,
                    monitor.Id,
                    monitor.TopologyApplied,
                    monitor.PrimaryApplied,
                    monitor.ModeApplied,
                    monitor.PositionApplied,
                    monitor.DpiApplied,
                    monitor.HdrApplied,
                    monitor.WindowsError,
                    string.Join("; ", monitor.Warnings)
                );
            }

            return result;
        }
        catch (Exception ex)
        {
            this._logger.LogError(
                ex,
                "Activation {Operation}: profile {Profile} ({ProfileId}) threw an exception",
                operation,
                preset.Name,
                preset.Id
            );
            throw;
        }
    }

    private ActivationResult ActivateWithRetries(Preset preset, bool dryRun, Guid operation)
    {
        using var mutex = new Mutex(false, ActivationMutexName);
        var acquired = false;
        var started = this._timeProvider.GetTimestamp();

        TimeSpan Elapsed()
        {
            return this._timeProvider.GetElapsedTime(started);
        }

        try
        {
            try
            {
                var lockBudget = ActivationLimit - Elapsed();
                acquired = lockBudget > TimeSpan.Zero && mutex.WaitOne(lockBudget);
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
            {
                return new ActivationResult(
                    false,
                    "Another display activation did not finish within ten seconds.",
                    [new ActivationStepResult("isolation", false, "Timed out waiting for the display activation lock.")]
                );
            }

            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                if (Elapsed() >= ActivationLimit)
                {
                    return new ActivationResult(
                        false,
                        "Display activation exceeded ten seconds.",
                        [new ActivationStepResult("watchdog", false, "Display activation timed out.")]
                    );
                }

                var result = this.ActivateCore(preset, dryRun, operation);
                var observed = dryRun ? [] : this.GetMonitors();
                var layoutMatches = dryRun || (result.Success && MatchesCurrentLayout(preset, observed));
                var layoutMismatch = result.Success && !dryRun && !layoutMatches
                    ? DescribeLayoutMismatch(preset, observed)
                    : null;
                if (result.Success && layoutMatches)
                {
                    return result;
                }

                var retryable = !dryRun && (result.Success || IsTransientFailure(result));
                this._logger.LogWarning(
                    "Activation {Operation}: attempt {Attempt}/{MaxAttempts}, phase={Phase}, WindowsError={WindowsError}, elapsed={ElapsedMs}ms, layoutMatches={LayoutMatches}, retryable={Retryable}, layoutMismatch={LayoutMismatch}, observed={Observed}; {Message}",
                    operation,
                    attempt,
                    MaxAttempts,
                    result.Steps?.LastOrDefault()?.Phase,
                    result.Steps?.LastOrDefault()?.WindowsError,
                    Elapsed().TotalMilliseconds,
                    layoutMatches,
                    retryable,
                    layoutMismatch,
                    string.Join(
                        ", ",
                        observed.Select(monitor
                            => $"{monitor.Id}:enabled={monitor.Enabled}:primary={monitor.Primary}:mode={FormatMode(monitor.CurrentMode)}:position=({monitor.PositionX},{monitor.PositionY}):rotation={monitor.Rotation}:device={monitor.DeviceName}"
                        )
                    ),
                    result.Message
                );
                if (!retryable || attempt == MaxAttempts)
                {
                    return result.Success
                        ? new ActivationResult(
                            false,
                            $"Windows did not retain the requested display layout: {layoutMismatch ?? "No per-monitor mismatch details were available."}",
                            [
                                new ActivationStepResult(
                                    "verification",
                                    false,
                                    layoutMismatch ?? "Display state did not match the requested profile."
                                )
                            ]
                        )
                        : result;
                }

                var delay = TimeSpan.FromMilliseconds(Math.Min(1000, 250 * (1 << (attempt - 1))));
                if (Elapsed() + delay >= ActivationLimit)
                {
                    return new ActivationResult(
                        false,
                        "Display activation exceeded ten seconds.",
                        [new ActivationStepResult("watchdog", false, "Display activation timed out.")]
                    );
                }

                Thread.Sleep(delay);
            }

            throw new InvalidOperationException("The bounded activation loop exited unexpectedly.");
        }
        finally
        {
            if (acquired)
            {
                mutex.ReleaseMutex();
            }
        }
    }

    private static bool IsTransientFailure(ActivationResult result)
    {
        if (result.Message.StartsWith("No free display path", StringComparison.Ordinal))
        {
            return false;
        }

        var phase = result.Steps?.LastOrDefault()?.Phase;
        return phase is "topology" or "primary" or "layout" or "mode-and-position" ||
               result.Message == "Windows did not activate the requested monitors.";
    }

    private static bool MatchesCurrentLayout(Preset preset, IReadOnlyList<MonitorInfo> monitors)
    {
        var enabled = preset.Monitors.Where(x => x.Enabled).OrderBy(x => x.Order).ToList();
        var active = monitors.Where(x => x.Enabled).ToList();
        if (enabled.Count != active.Count)
        {
            return false;
        }

        var expectedX = -enabled.TakeWhile(x => !x.Primary).Sum(OrientedWidth);
        foreach (var setting in enabled)
        {
            var monitor = active.FirstOrDefault(x => x.Id.Equals(setting.Id, StringComparison.OrdinalIgnoreCase));
            if (monitor is null || monitor.Primary != setting.Primary || monitor.CurrentMode != setting.Mode ||
                monitor.PositionX != expectedX || monitor.PositionY != 0 || monitor.Rotation != setting.Rotation)
            {
                return false;
            }

            expectedX += OrientedWidth(setting);
        }

        return true;
    }

    private static string DescribeLayoutMismatch(Preset preset, IReadOnlyList<MonitorInfo> monitors)
    {
        var expected = preset.Monitors.Where(setting => setting.Enabled).OrderBy(setting => setting.Order).ToList();
        var active = monitors.Where(monitor => monitor.Enabled).ToList();
        var mismatches = new List<string>();
        if (expected.Count != active.Count)
        {
            mismatches.Add($"enabled monitor count expected={expected.Count}, actual={active.Count}");
        }

        var expectedX = -expected.TakeWhile(setting => !setting.Primary).Sum(OrientedWidth);
        foreach (var setting in expected)
        {
            var monitor = active.FirstOrDefault(candidate => candidate.Id.Equals(setting.Id, StringComparison.OrdinalIgnoreCase));
            if (monitor is null)
            {
                mismatches.Add($"{setting.Name} ({setting.Id}) expected active but was not active");
                expectedX += OrientedWidth(setting);
                continue;
            }

            var differences = new List<string>();
            if (monitor.Primary != setting.Primary)
            {
                differences.Add($"primary expected={setting.Primary}, actual={monitor.Primary}");
            }

            if (monitor.CurrentMode != setting.Mode)
            {
                differences.Add($"mode expected={FormatMode(setting.Mode)}, actual={FormatMode(monitor.CurrentMode)}");
            }

            if (monitor.PositionX != expectedX || monitor.PositionY != 0)
            {
                differences.Add($"position expected=({expectedX},0), actual=({monitor.PositionX},{monitor.PositionY})");
            }

            if (monitor.Rotation != setting.Rotation)
            {
                differences.Add($"rotation expected={setting.Rotation}, actual={monitor.Rotation}");
            }

            if (differences.Count > 0)
            {
                mismatches.Add($"{setting.Name} ({setting.Id}): {string.Join(", ", differences)}");
            }

            expectedX += OrientedWidth(setting);
        }

        var expectedIds = expected.Select(setting => setting.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var monitor in active.Where(monitor => !expectedIds.Contains(monitor.Id)))
        {
            mismatches.Add($"unexpected active monitor {monitor.Name} ({monitor.Id})");
        }

        return mismatches.Count == 0 ? "layout verification failed without a field mismatch" : string.Join("; ", mismatches);
    }

    private static string FormatMode(DisplayMode? mode)
    {
        return mode is null ? "unavailable" : $"{mode.Width}x{mode.Height}@{mode.RefreshHz}Hz";
    }

    private static int OrientedWidth(MonitorSetting setting)
    {
        return setting.Mode.Orient(setting.Rotation).Width;
    }

    private ActivationResult ActivateCore(
        Preset preset,
        bool dryRun,
        Guid operation,
        IReadOnlySet<string>? recentlyAttached = null)
    {
        var connected = this.GetMonitors();
        var available = this._platform.AvailableTargets().ToDictionary(target => target.Path, StringComparer.OrdinalIgnoreCase);
        var enabled = preset.Monitors.Where(x => x.Enabled).OrderBy(x => x.Order).ToList();
        var primaries = enabled.Where(x => x.Primary).ToList();
        if (primaries.Count != 1)
        {
            return new ActivationResult(
                false,
                "Exactly one enabled primary monitor is required.",
                [new ActivationStepResult("validation", false, "Exactly one enabled primary monitor is required.")]
            );
        }

        var primary = primaries[0];
        if (!available.ContainsKey(primary.Id))
        {
            return new ActivationResult(
                false,
                "The primary monitor is missing.",
                [new ActivationStepResult("validation", false, "The requested primary monitor is not available.")],
                [
                    new MonitorActivationResult(
                        primary.Id,
                        primary.Name,
                        false,
                        false,
                        false,
                        false,
                        false,
                        false,
                        ["Primary monitor is missing."]
                    )
                ]
            );
        }

        var missing = enabled.FirstOrDefault(setting => !available.ContainsKey(setting.Id));
        if (missing is not null)
        {
            return new ActivationResult(
                false,
                $"Monitor {missing.Name} is unavailable.",
                [new ActivationStepResult("validation", false, $"Requested monitor {missing.Name} is not available.")]
            );
        }

        // A monitor disabled by another profile is absent from the current GDI display list,
        // but remains an available DisplayConfig target and can be restored into the topology.
        var present = enabled.Where(x => connected.Any(m => m.Id.Equals(x.Id, StringComparison.OrdinalIgnoreCase))).ToList();
        var desired = enabled.Where(x => available.ContainsKey(x.Id)).ToList();

        var desiredIds = desired.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var activeIds = connected.Where(x => x.Enabled).Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!desiredIds.SetEquals(activeIds))
        {
            var newlyAttached = desiredIds.Except(activeIds, StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var targets = desired.Select(x =>
                    {
                        var monitor = connected.FirstOrDefault(m => m.Id.Equals(x.Id, StringComparison.OrdinalIgnoreCase));
                        var preferredSource = monitor?.DeviceName ?? available[x.Id].SourceNames.FirstOrDefault() ?? "";
                        return new DisplayTopologyTarget(x.Id, preferredSource, x.Rotation);
                    }
                )
                .ToList();
            var topology = this._platform.SetTopology(targets, !dryRun);
            this._logger.Log(
                topology.Success ? LogLevel.Information : LogLevel.Warning,
                "Activation {Operation}: topology success={Success}, WindowsError={WindowsError}; {Message}",
                operation,
                topology.Success,
                topology.WindowsError,
                topology.Message
            );
            if (!topology.Success)
            {
                return new ActivationResult(
                    false,
                    topology.Message,
                    [new ActivationStepResult("topology", false, topology.Message, topology.WindowsError)]
                );
            }

            if (dryRun)
            {
                return new ActivationResult(
                    true,
                    "Windows accepted the display topology; modes will be applied after activation."
                );
            }

            var changed = this.GetMonitors().Where(m => m.Enabled).Select(m => m.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!changed.SetEquals(desiredIds))
            {
                return new ActivationResult(false, "Windows did not activate the requested monitors.");
            }

            return this.ActivateCore(preset, false, operation, newlyAttached);
        }

        // GDI mode lists for detached monitors can belong to their former source.
        // Validate only after topology has reattached each physical target.
        foreach (var setting in present)
        {
            var monitor = connected.First(m => m.Id.Equals(setting.Id, StringComparison.OrdinalIgnoreCase));
            if (!monitor.Enabled || monitor.CurrentMode is null || string.IsNullOrWhiteSpace(monitor.DeviceName) ||
                monitor.Modes.Count == 0)
            {
                return new ActivationResult(
                    false,
                    $"Windows has not made {monitor.Name} ready.",
                    [new ActivationStepResult("topology", false, $"Waiting for {monitor.Name} to expose its current mode.")]
                );
            }

            // A freshly attached target may still expose the previous source's
            // cached GDI modes. ChangeDisplaySettingsEx(CDS_TEST) validates the
            // requested mode against Windows before any layout is applied.
            if (recentlyAttached?.Contains(setting.Id) != true && !monitor.Modes.Contains(setting.Mode))
            {
                return new ActivationResult(
                    false,
                    $"Unsupported mode for {monitor.Name}.",
                    [new ActivationStepResult("mode", false, $"Unsupported mode for {monitor.Name}.")],
                    [
                        new MonitorActivationResult(
                            setting.Id,
                            monitor.Name,
                            false,
                            false,
                            false,
                            false,
                            false,
                            false,
                            ["Requested mode is unsupported."]
                        )
                    ]
                );
            }
        }

        if (connected.FirstOrDefault(monitor => monitor.Primary)?.Id != primary.Id)
        {
            if (dryRun)
            {
                return new ActivationResult(true, "Primary selection will be applied after topology activation.");
            }

            var primaryResult = this._platform.SetPrimary(primary.Id);
            if (!primaryResult.Success)
            {
                return new ActivationResult(
                    false,
                    primaryResult.Message,
                    [new ActivationStepResult("primary", false, primaryResult.Message, primaryResult.WindowsError)]
                );
            }

            if (this.GetMonitors().FirstOrDefault(monitor => monitor.Primary)?.Id != primary.Id)
            {
                return new ActivationResult(
                    false,
                    "Windows has not selected the requested primary monitor.",
                    [new ActivationStepResult("primary", false, "Requested primary was not observed.")]
                );
            }

            return this.ActivateCore(preset, false, operation, recentlyAttached);
        }

        var plan = new List<(MonitorInfo Monitor, MonitorSetting Setting, DisplayLayoutRequest Request)>();
        var xOffset = -present.TakeWhile(x => !x.Primary).Sum(OrientedWidth);
        foreach (var setting in present)
        {
            var monitor = connected.FirstOrDefault(x => x.Id.Equals(setting.Id, StringComparison.OrdinalIgnoreCase));
            if (monitor is null)
            {
                continue;
            }

            if (setting.Mode.Width <= 0 || setting.Mode.Height <= 0 || setting.Mode.RefreshHz <= 0)
            {
                return new ActivationResult(false, $"Invalid mode for {monitor.Name}.");
            }

            var request = new DisplayLayoutRequest(monitor.DeviceName, setting.Mode, setting.Rotation, xOffset, 0);
            plan.Add((monitor, setting, request));
            xOffset += OrientedWidth(setting);
        }

        foreach (var entry in plan)
        {
            var code = this._platform.TestLayout(entry.Request);
            if (code != 0)
            {
                return new ActivationResult(
                    false,
                    $"Windows rejected {entry.Monitor.Name}: {code}.",
                    [new ActivationStepResult("mode-and-position", false, $"Windows rejected {entry.Monitor.Name}.", code)],
                    [
                        new MonitorActivationResult(
                            entry.Monitor.Id,
                            entry.Monitor.Name,
                            false,
                            false,
                            false,
                            false,
                            false,
                            false,
                            [],
                            code
                        )
                    ]
                );
            }
        }

        if (dryRun)
        {
            return new ActivationResult(true, "Windows accepted the display configuration test.");
        }

        foreach (var entry in plan)
        {
            var code = this._platform.ApplyLayout(entry.Request);

            if (code != 0)
            {
                return new ActivationResult(
                    false,
                    $"Could not apply {entry.Monitor.Name}: {code}.",
                    [new ActivationStepResult("mode-and-position", false, $"Could not apply {entry.Monitor.Name}.", code)],
                    [
                        new MonitorActivationResult(
                            entry.Monitor.Id,
                            entry.Monitor.Name,
                            false,
                            false,
                            false,
                            false,
                            false,
                            false,
                            [],
                            code
                        )
                    ]
                );
            }
        }

        var monitorResults = new List<MonitorActivationResult>();
        foreach (var setting in present)
        {
            var target = available[setting.Id];
            var warnings = new List<string>();
            var dpiApplied = true;
            var hdrApplied = true;
            int? error = null;
            if (setting.DpiScale is int dpiScale)
            {
                var dpiError = this._platform.SetDpiScale(target, dpiScale);
                dpiApplied = dpiError == 0;
                if (!dpiApplied)
                {
                    warnings.Add($"DPI scaling was not applied ({dpiError}).");
                    error = dpiError;
                }
            }

            if (setting.HdrEnabled is bool hdrEnabled)
            {
                var hdr = this._platform.GetHdrInfo(target);
                if (hdr.Supported)
                {
                    var hdrError = this._platform.SetHdr(target, hdrEnabled);
                    hdrApplied = hdrError == 0;
                    if (!hdrApplied)
                    {
                        warnings.Add($"HDR was not applied ({hdrError}).");
                        error ??= hdrError;
                    }
                }
            }

            monitorResults.Add(
                new MonitorActivationResult(
                    setting.Id,
                    setting.Name,
                    true,
                    setting.Primary,
                    true,
                    true,
                    dpiApplied,
                    hdrApplied,
                    warnings,
                    error
                )
            );
        }

        var allApplied = monitorResults.All(result => result.DpiApplied && result.HdrApplied);
        var steps = new List<ActivationStepResult>
        {
            new("topology", true, "Display topology applied."),
            new("primary", true, "Primary display applied."),
            new("mode-and-position", true, "Display modes and positions applied."),
            new("dpi", monitorResults.All(result => result.DpiApplied), "DPI scaling processed."),
            new("hdr", monitorResults.All(result => result.HdrApplied), "HDR settings processed.")
        };
        return new ActivationResult(
            allApplied,
            allApplied ? $"Activated {preset.Name}." : $"Activated {preset.Name} with warnings.",
            steps,
            monitorResults
        );
    }
}