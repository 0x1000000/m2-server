using System.Buffers.Binary;
using System.Runtime.InteropServices;
using M2Server.Lib.Domain;

namespace M2Server.Lib.Services;

// CCD device paths identify physical targets more reliably than the GDI DISPLAY number.
public sealed partial class WindowsDisplayPlatform : IDisplayPlatform
{
    private const uint SdcUseSuppliedDisplayConfig = 0x20;
    private const uint SdcValidate = 0x40;
    private const uint SdcApply = 0x80;
    private const uint SdcSaveToDatabase = 0x200;
    private const uint SdcForceModeEnumeration = 0x1000;

    public (bool Success, string Message, int? WindowsError) SetPrimary(string targetPath)
    {
        // DISPLAYCONFIG_MODE_INFO is 64 bytes; source-mode position is at bytes 28-35.
        const int modeInfoSize = 64;
        var error = WinApiProxy.GetDisplayConfigBufferSizes(2, out var pathCount, out var modeCount);
        if (error != 0)
        {
            return (false, $"Windows could not enumerate active display paths: {error}.", error);
        }

        var paths = new WinApiProxy.PathInfo[pathCount];
        var modeBuffer = Marshal.AllocHGlobal(checked((int)Math.Max(modeCount, 1) * modeInfoSize));
        try
        {
            error = WinApiProxy.QueryDisplayConfig(
                2,
                ref pathCount,
                paths,
                ref modeCount,
                modeBuffer,
                IntPtr.Zero
            );
            if (error != 0)
            {
                return (false, $"Windows could not query active display paths: {error}.", error);
            }

            var modes = new byte[checked((int)modeCount * modeInfoSize)];
            Marshal.Copy(modeBuffer, modes, 0, modes.Length);
            uint? primaryModeIndex = null;
            foreach (var path in paths.Take((int)pathCount))
            {
                var target = new WinApiProxy.TargetName
                {
                    Header = new WinApiProxy.Header
                    {
                        Type = 2,
                        Size = (uint)Marshal.SizeOf<WinApiProxy.TargetName>(),
                        Adapter = path.Target.Adapter,
                        Id = path.Target.Id
                    }
                };
                if (WinApiProxy.DisplayConfigGetDeviceInfo(ref target) != 0 ||
                    !target.DevicePath.Equals(targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                primaryModeIndex = path.Source.ModeIndex;
                break;
            }

            if (primaryModeIndex is null || primaryModeIndex >= modeCount)
            {
                return (false, "The requested primary display has no active source mode.", null);
            }

            var primaryOffset = checked((int)primaryModeIndex.Value * modeInfoSize);
            if (BinaryPrimitives.ReadUInt32LittleEndian(modes.AsSpan(primaryOffset, 4)) != 1)
            {
                return (false, "The requested primary display has an invalid source mode.", null);
            }

            var originX = BinaryPrimitives.ReadInt32LittleEndian(modes.AsSpan(primaryOffset + 28, 4));
            var originY = BinaryPrimitives.ReadInt32LittleEndian(modes.AsSpan(primaryOffset + 32, 4));
            for (var index = 0; index < modeCount; index++)
            {
                var offset = checked(index * modeInfoSize);
                if (BinaryPrimitives.ReadUInt32LittleEndian(modes.AsSpan(offset, 4)) != 1)
                {
                    continue;
                }

                var x = BinaryPrimitives.ReadInt32LittleEndian(modes.AsSpan(offset + 28, 4));
                var y = BinaryPrimitives.ReadInt32LittleEndian(modes.AsSpan(offset + 32, 4));
                BinaryPrimitives.WriteInt32LittleEndian(modes.AsSpan(offset + 28, 4), checked(x - originX));
                BinaryPrimitives.WriteInt32LittleEndian(modes.AsSpan(offset + 32, 4), checked(y - originY));
            }

            Marshal.Copy(modes, 0, modeBuffer, modes.Length);
            const uint flags = SdcApply | SdcUseSuppliedDisplayConfig | SdcSaveToDatabase;
            error = WinApiProxy.SetDisplayConfig(pathCount, paths, modeCount, modeBuffer, flags);
            return error == 0
                ? (true, "Primary display selected.", null)
                : (false, $"Windows could not select the primary display: {error}.", error);
        }
        finally
        {
            Marshal.FreeHGlobal(modeBuffer);
        }
    }

    public (bool Success, string Message, int? WindowsError) SetTopology(IReadOnlyList<DisplayTopologyTarget> targets, bool apply)
    {
        if (WinApiProxy.GetDisplayConfigBufferSizes(1, out var pathCount, out var modeCount) != 0)
        {
            return (false, "Windows could not enumerate display paths.", null);
        }

        var allPaths = new WinApiProxy.PathInfo[pathCount];
        var modeBuffer = Marshal.AllocHGlobal(checked((int)Math.Max(modeCount, 1) * 128));
        try
        {
            var error = WinApiProxy.QueryDisplayConfig(
                1,
                ref pathCount,
                allPaths,
                ref modeCount,
                modeBuffer,
                IntPtr.Zero
            );
            if (error != 0)
            {
                return (false, $"Windows could not query display paths: {error}.", error);
            }

            var candidates = new List<(WinApiProxy.PathInfo Path, string TargetPath, string SourceName)>();
            foreach (var path in allPaths.Take((int)pathCount).Where(p => p.Target.Available != 0))
            {
                var target = new WinApiProxy.TargetName
                {
                    Header = new WinApiProxy.Header
                    {
                        Type = 2,
                        Size = (uint)Marshal.SizeOf<WinApiProxy.TargetName>(),
                        Adapter = path.Target.Adapter,
                        Id = path.Target.Id
                    }
                };
                var source = new WinApiProxy.SourceName
                {
                    Header = new WinApiProxy.Header
                    {
                        Type = 1,
                        Size = (uint)Marshal.SizeOf<WinApiProxy.SourceName>(),
                        Adapter = path.Source.Adapter,
                        Id = path.Source.Id
                    }
                };
                if (WinApiProxy.DisplayConfigGetDeviceInfo(ref target) == 0 &&
                    WinApiProxy.DisplayConfigGetDeviceInfo(ref source) == 0)
                {
                    candidates.Add((path, target.DevicePath, source.GdiName));
                }
            }

            var selected = new List<WinApiProxy.PathInfo>();
            var used = new HashSet<(uint Low, int High, uint Source)>();
            foreach (var target in targets)
            {
                var choice = candidates.Where(c => c.TargetPath.Equals(target.Path, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(c => (c.Path.Flags & 1) != 0)
                    .ThenByDescending(c => c.SourceName.Equals(target.PreferredSource, StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault(c => !used.Contains((c.Path.Source.Adapter.Low, c.Path.Source.Adapter.High, c.Path.Source.Id))
                    );
                if (choice.TargetPath is null)
                {
                    return (false, $"No free display path for {target.Path}.", null);
                }

                var path = choice.Path;
                used.Add((path.Source.Adapter.Low, path.Source.Adapter.High, path.Source.Id));
                path.Source.ModeIndex = uint.MaxValue;
                path.Target.ModeIndex = uint.MaxValue;
                path.Target.Refresh = default;
                path.Target.Scanline = 0;
                path.Target.Rotation = (uint)target.Rotation;
                path.Flags |= 1;
                selected.Add(path);
            }

            // Do not let Windows replace a requested physical path with another
            // target. A rejected topology is safer than activating the wrong monitor.
            var flags = (apply ? SdcApply | SdcSaveToDatabase | SdcForceModeEnumeration : SdcValidate) |
                        SdcUseSuppliedDisplayConfig;
            var status = WinApiProxy.SetDisplayConfig((uint)selected.Count, selected.ToArray(), 0, IntPtr.Zero, flags);
            if (status != 0)
            {
                return (false, $"Windows rejected display topology: {status}.", status);
            }

            if (apply)
            {
                var active = this.ActiveTargetPaths();
                var requested = targets.Select(target => target.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (active is null || !requested.SetEquals(active))
                {
                    return (false,
                        $"Windows activated different display targets. Requested: {string.Join(", ", requested)}; active: {(active is null ? "unavailable" : string.Join(", ", active))}.",
                        null);
                }
            }

            return (true, apply ? "Display topology applied." : "Display topology accepted.", null);
        }
        finally
        {
            Marshal.FreeHGlobal(modeBuffer);
        }
    }

    public IReadOnlyList<DisplayTargetIdentity> AvailableTargets()
    {
        var result
            = new List<(string Path, string Name, string Source, bool Active, WinApiProxy.Luid SourceAdapter, uint SourceId,
                WinApiProxy.Luid TargetAdapter, uint TargetId, DisplayRotation Rotation)>();
        if (WinApiProxy.GetDisplayConfigBufferSizes(1, out var pathsNeeded, out var modesNeeded) != 0)
        {
            return [];
        }

        var paths = new WinApiProxy.PathInfo[pathsNeeded];
        var modeBuffer = Marshal.AllocHGlobal(checked((int)Math.Max(modesNeeded, 1) * 128));
        try
        {
            if (WinApiProxy.QueryDisplayConfig(
                    1,
                    ref pathsNeeded,
                    paths,
                    ref modesNeeded,
                    modeBuffer,
                    IntPtr.Zero
                ) != 0)
            {
                return [];
            }

            foreach (var path in paths.Take((int)pathsNeeded))
            {
                if (path.Target.Available == 0)
                {
                    continue;
                }

                var target = new WinApiProxy.TargetName
                {
                    Header = new WinApiProxy.Header
                    {
                        Type = 2,
                        Size = (uint)Marshal.SizeOf<WinApiProxy.TargetName>(),
                        Adapter = path.Target.Adapter,
                        Id = path.Target.Id
                    }
                };
                var source = new WinApiProxy.SourceName
                {
                    Header = new WinApiProxy.Header
                    {
                        Type = 1,
                        Size = (uint)Marshal.SizeOf<WinApiProxy.SourceName>(),
                        Adapter = path.Source.Adapter,
                        Id = path.Source.Id
                    }
                };
                if (WinApiProxy.DisplayConfigGetDeviceInfo(ref target) != 0 ||
                    WinApiProxy.DisplayConfigGetDeviceInfo(ref source) != 0)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(target.DevicePath))
                {
                    result.Add(
                        (target.DevicePath, target.FriendlyName, source.GdiName, (path.Flags & 1) != 0, path.Source.Adapter,
                            path.Source.Id, path.Target.Adapter, path.Target.Id, (DisplayRotation)path.Target.Rotation)
                    );
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(modeBuffer);
        }

        return result.GroupBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
                {
                    var path = group.Key;
                    var marker = "DISPLAY#";
                    var start = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                    var model = start < 0 ? "" : path[(start + marker.Length)..].Split('#')[0];
                    return new DisplayTargetIdentity(
                        path,
                        group.First().Name,
                        model,
                        group.Select(x => x.Source).ToHashSet(StringComparer.OrdinalIgnoreCase),
                        group.Any(x => x.Active),
                        group.First().Rotation
                    )
                    {
                        SourceAdapter = group.First().SourceAdapter,
                        SourceId = group.First().SourceId,
                        TargetAdapter = group.First().TargetAdapter,
                        TargetId = group.First().TargetId
                    };
                }
            )
            .ToList();
    }

    public (bool Supported, bool Enabled) GetHdrInfo(DisplayTargetIdentity target)
    {
        if (Environment.OSVersion.Version.Build >= 26100)
        {
            var hdr = new WinApiProxy.AdvancedColorInfo2
            {
                Header = new WinApiProxy.Header
                {
                    Type = 15,
                    Size = (uint)Marshal.SizeOf<WinApiProxy.AdvancedColorInfo2>(),
                    Adapter = target.TargetAdapter,
                    Id = target.TargetId
                }
            };
            if (WinApiProxy.DisplayConfigGetDeviceInfo(ref hdr) != 0)
            {
                return (false, false);
            }

            return ((hdr.Values & 0x10) != 0, (hdr.Values & 0x20) != 0);
        }

        var color = new WinApiProxy.AdvancedColorInfo
        {
            Header = new WinApiProxy.Header
            {
                Type = 9,
                Size = (uint)Marshal.SizeOf<WinApiProxy.AdvancedColorInfo>(),
                Adapter = target.TargetAdapter,
                Id = target.TargetId
            }
        };
        if (WinApiProxy.DisplayConfigGetDeviceInfo(ref color) != 0)
        {
            return (false, false);
        }

        return ((color.Values & 1) != 0, (color.Values & 2) != 0);
    }

    public int SetDpiScale(DisplayTargetIdentity target, int desiredPercent)
    {
        var info = this.GetDpiScaleInfo(target);
        if (info.Recommended is null || info.Supported.Count == 0 || !info.Supported.Contains(desiredPercent))
        {
            return 87;
        }

        var recommendedIndex = Array.IndexOf(
            new[] { 100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500 },
            info.Recommended.Value
        );
        var desiredIndex = Array.IndexOf(new[] { 100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500 }, desiredPercent);
        var dpi = new WinApiProxy.DpiScaleSet
        {
            Header = new WinApiProxy.Header
            {
                Type = -4,
                Size = (uint)Marshal.SizeOf<WinApiProxy.DpiScaleSet>(),
                Adapter = target.SourceAdapter,
                Id = target.SourceId
            },
            ScaleRel = desiredIndex - recommendedIndex
        };
        return WinApiProxy.DisplayConfigSetDeviceInfo(ref dpi);
    }

    public int SetHdr(DisplayTargetIdentity target, bool enabled)
    {
        var color = new WinApiProxy.AdvancedColorState
        {
            Header = new WinApiProxy.Header
            {
                Type = Environment.OSVersion.Version.Build >= 26100 ? 16 : 10,
                Size = (uint)Marshal.SizeOf<WinApiProxy.AdvancedColorState>(),
                Adapter = target.TargetAdapter,
                Id = target.TargetId
            },
            Values = enabled ? 1u : 0u
        };
        return WinApiProxy.DisplayConfigSetDeviceInfo(ref color);
    }

    private IReadOnlySet<string>? ActiveTargetPaths()
    {
        if (WinApiProxy.GetDisplayConfigBufferSizes(2, out var pathCount, out var modeCount) != 0)
        {
            return null;
        }

        var paths = new WinApiProxy.PathInfo[pathCount];
        var modes = Marshal.AllocHGlobal(checked((int)Math.Max(modeCount, 1) * 128));
        try
        {
            if (WinApiProxy.QueryDisplayConfig(
                    2,
                    ref pathCount,
                    paths,
                    ref modeCount,
                    modes,
                    IntPtr.Zero
                ) != 0)
            {
                return null;
            }

            var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in paths.Take((int)pathCount))
            {
                var target = new WinApiProxy.TargetName
                {
                    Header = new WinApiProxy.Header
                    {
                        Type = 2,
                        Size = (uint)Marshal.SizeOf<WinApiProxy.TargetName>(),
                        Adapter = path.Target.Adapter,
                        Id = path.Target.Id
                    }
                };
                if (WinApiProxy.DisplayConfigGetDeviceInfo(ref target) != 0 || string.IsNullOrWhiteSpace(target.DevicePath))
                {
                    return null;
                }

                active.Add(target.DevicePath);
            }

            return active;
        }
        finally
        {
            Marshal.FreeHGlobal(modes);
        }
    }

    private IReadOnlyDictionary<string, string> ConnectedTargets()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (WinApiProxy.GetDisplayConfigBufferSizes(2, out var pathCount, out var modeCount) != 0)
        {
            return result;
        }

        var paths = new WinApiProxy.PathInfo[pathCount];
        var modes = Marshal.AllocHGlobal(checked((int)Math.Max(modeCount, 1) * 128));
        try
        {
            if (WinApiProxy.QueryDisplayConfig(
                    2,
                    ref pathCount,
                    paths,
                    ref modeCount,
                    modes,
                    IntPtr.Zero
                ) != 0)
            {
                return result;
            }

            foreach (var path in paths.Take((int)pathCount))
            {
                var target = new WinApiProxy.TargetName
                {
                    Header = new WinApiProxy.Header
                    {
                        Type = 2,
                        Size = (uint)Marshal.SizeOf<WinApiProxy.TargetName>(),
                        Adapter = path.Target.Adapter,
                        Id = path.Target.Id
                    }
                };
                var source = new WinApiProxy.SourceName
                {
                    Header = new WinApiProxy.Header
                    {
                        Type = 1,
                        Size = (uint)Marshal.SizeOf<WinApiProxy.SourceName>(),
                        Adapter = path.Source.Adapter,
                        Id = path.Source.Id
                    }
                };
                if (WinApiProxy.DisplayConfigGetDeviceInfo(ref target) == 0 &&
                    WinApiProxy.DisplayConfigGetDeviceInfo(ref source) == 0 && !string.IsNullOrWhiteSpace(target.DevicePath) &&
                    !string.IsNullOrWhiteSpace(source.GdiName))
                {
                    result[source.GdiName] = target.DevicePath;
                }
            }

            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(modes);
        }
    }

    private DisplayDpiScaleInfo GetDpiScaleInfo(DisplayTargetIdentity target)
    {
        var dpi = new WinApiProxy.DpiScaleGet
        {
            Header = new WinApiProxy.Header
            {
                Type = -3,
                Size = (uint)Marshal.SizeOf<WinApiProxy.DpiScaleGet>(),
                Adapter = target.SourceAdapter,
                Id = target.SourceId
            }
        };
        if (WinApiProxy.DisplayConfigGetDeviceInfo(ref dpi) != 0)
        {
            return new DisplayDpiScaleInfo([], null, null);
        }

        var values = new[] { 100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500 };
        var recommendedIndex = Math.Abs(dpi.MinScaleRel);
        var indexes = Enumerable.Range(dpi.MinScaleRel, dpi.MaxScaleRel - dpi.MinScaleRel + 1)
            .Select(relative => recommendedIndex + relative)
            .Where(index => index >= 0 && index < values.Length)
            .ToArray();
        var currentIndex = recommendedIndex + Math.Clamp(dpi.CurScaleRel, dpi.MinScaleRel, dpi.MaxScaleRel);
        return new DisplayDpiScaleInfo(
            indexes.Select(index => values[index]).ToArray(),
            currentIndex >= 0 && currentIndex < values.Length ? values[currentIndex] : null,
            recommendedIndex >= 0 && recommendedIndex < values.Length ? values[recommendedIndex] : null
        );
    }
}