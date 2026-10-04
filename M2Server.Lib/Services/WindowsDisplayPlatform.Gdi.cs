using M2Server.Lib.Domain;
using static M2Server.Lib.Services.WinApiProxy;

namespace M2Server.Lib.Services;

public sealed partial class WindowsDisplayPlatform
{
    private const int Current = -1;
    private const uint Attached = 1, Primary = 4;
    private const uint DmPosition = 0x20, DmOrientation = 0x80, DmWidth = 0x80000, DmHeight = 0x100000, DmFrequency = 0x400000;
    private const uint CdsUpdateRegistry = 1, CdsTest = 2;

    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var result = new List<MonitorInfo>();
        var connectedTargets = this.ConnectedTargets();
        var allTargets = this.AvailableTargets();
        var assigned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (uint index = 0;; index++)
        {
            var display = NewDevice();
            if (!EnumDisplayDevices(null, index, ref display, 0))
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(display.DeviceName) || (display.StateFlags & 0x8) != 0)
            {
                continue;
            }

            var attached = (display.StateFlags & Attached) != 0;
            if (!attached || !connectedTargets.TryGetValue(display.DeviceName, out var path))
            {
                continue;
            }

            var monitor = NewDevice();
            EnumDisplayDevices(display.DeviceName, 0, ref monitor, 0);

            // CCD owns the live source-to-target mapping. After a registry reset,
            // EnumDisplayDevices can report a previous monitor interface path for
            // an active source even while CCD has the correct physical target.
            var target = allTargets.FirstOrDefault(item => item.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
            if (target is null || !assigned.Add(target.Path))
            {
                continue;
            }

            var modes = new List<DisplayMode>();
            for (var m = 0;; m++)
            {
                var dm = NewMode();
                if (!EnumDisplaySettings(display.DeviceName, m, ref dm))
                {
                    break;
                }

                var mode = new DisplayMode((int)dm.Width, (int)dm.Height, (int)dm.Frequency).Orient(target.Rotation);
                if (mode.Width > 0 && mode.Height > 0 && mode.RefreshHz > 0 && !modes.Contains(mode))
                {
                    modes.Add(mode);
                }
            }

            var active = NewMode();
            var current = attached && EnumDisplaySettings(display.DeviceName, Current, ref active)
                ? new DisplayMode((int)active.Width, (int)active.Height, (int)active.Frequency).Orient(target.Rotation)
                : null;
            var stableId = target.Path;
            var displayName = target.Name;

            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = string.IsNullOrWhiteSpace(monitor.DeviceString) ? target.Model : monitor.DeviceString;
            }

            var dpi = this.GetDpiScaleInfo(target);
            var hdr = this.GetHdrInfo(target);
            result.Add(
                new MonitorInfo(
                    stableId,
                    displayName + " (" + display.DeviceName + ")",
                    display.DeviceName,
                    attached,
                    (display.StateFlags & Primary) != 0,
                    current,
                    modes.OrderByDescending(x => x.Width * x.Height).ThenByDescending(x => x.RefreshHz).ToList(),
                    current is null ? int.MaxValue : active.Position.X,
                    current is null ? 0 : active.Position.Y,
                    dpi.Supported,
                    dpi.Current,
                    hdr.Item1,
                    hdr.Item1 ? hdr.Item2 : null,
                    target.Rotation
                )
            );
        }

        return result.OrderBy(x => x.PositionX).ThenBy(x => x.DeviceName).ToList();
    }

    public IReadOnlyList<string> DiagnoseCurrentModes()
    {
        var lines = new List<string>();
        foreach (var monitor in this.GetMonitors().Where(m => m.Enabled))
        {
            var dm = NewMode();
            if (!EnumDisplaySettings(monitor.DeviceName, Current, ref dm))
            {
                continue;
            }

            var unchanged = ChangeDisplaySettingsEx(monitor.DeviceName, ref dm, IntPtr.Zero, CdsTest, IntPtr.Zero);
            dm.Fields = DmPosition | DmWidth | DmHeight | DmFrequency;
            var selected = ChangeDisplaySettingsEx(monitor.DeviceName, ref dm, IntPtr.Zero, CdsTest, IntPtr.Zero);
            lines.Add(
                $"{monitor.DeviceName}: current {dm.Width}x{dm.Height}@{dm.Frequency} position {dm.Position.X},{dm.Position.Y} fields {dm.Fields:X} unchanged={unchanged} selected={selected}"
            );
        }

        return lines;
    }

    public int TestLayout(DisplayLayoutRequest request)
    {
        var mode = CreateLayoutMode(request);
        return ChangeDisplaySettingsEx(request.DeviceName, ref mode, IntPtr.Zero, CdsTest, IntPtr.Zero);
    }

    public int ApplyLayout(DisplayLayoutRequest request)
    {
        var mode = CreateLayoutMode(request);
        var code = ChangeDisplaySettingsEx(request.DeviceName, ref mode, IntPtr.Zero, CdsUpdateRegistry, IntPtr.Zero);
        if (code == 0)
        {
            return 0;
        }

        var current = NewMode();
        if (!EnumDisplaySettings(request.DeviceName, Current, ref current) || current.Width != mode.Width ||
            current.Height != mode.Height || current.Position.X != mode.Position.X || current.Position.Y != mode.Position.Y ||
            current.Orientation != mode.Orientation)
        {
            return code;
        }

        current.Fields = DmFrequency;
        current.Frequency = mode.Frequency;
        return ChangeDisplaySettingsEx(request.DeviceName, ref current, IntPtr.Zero, CdsUpdateRegistry, IntPtr.Zero);
    }

    private static DevMode CreateLayoutMode(DisplayLayoutRequest request)
    {
        var mode = NewMode();
        if (!EnumDisplaySettings(request.DeviceName, Current, ref mode))
        {
            mode = NewMode();
        }

        var orientedMode = request.Mode.Orient(request.Rotation);
        mode.Fields = DmPosition | DmOrientation | DmWidth | DmHeight | DmFrequency;
        mode.Position = new PointL { X = request.PositionX, Y = request.PositionY };
        mode.Orientation = (uint)request.Rotation - 1;
        mode.Width = (uint)orientedMode.Width;
        mode.Height = (uint)orientedMode.Height;
        mode.Frequency = (uint)orientedMode.RefreshHz;
        return mode;
    }

    private static DisplayDevice NewDevice()
    {
        return new DisplayDevice { Size = DisplayDevice.SizeInBytes };
    }

    private static DevMode NewMode()
    {
        return new DevMode { Size = DevMode.SizeInBytes };
    }
}