using System.Runtime.InteropServices;

namespace M2Server.Lib.Services;

public static class WinApiProxy
{
    [DllImport("user32.dll")]
    public static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);

    [DllImport("user32.dll")]
    public static extern int QueryDisplayConfig(
        uint flags,
        ref uint paths,
        [Out] PathInfo[] pathArray,
        ref uint modes,
        IntPtr modeArray,
        IntPtr topology);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int DisplayConfigGetDeviceInfo(ref SourceName name);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int DisplayConfigGetDeviceInfo(ref TargetName name);

    [DllImport("user32.dll")]
    public static extern int DisplayConfigGetDeviceInfo(ref DpiScaleGet dpi);

    [DllImport("user32.dll")]
    public static extern int DisplayConfigGetDeviceInfo(ref AdvancedColorInfo color);

    [DllImport("user32.dll")]
    public static extern int DisplayConfigGetDeviceInfo(ref AdvancedColorInfo2 color);

    [DllImport("user32.dll")]
    public static extern int DisplayConfigSetDeviceInfo(ref DpiScaleSet dpi);

    [DllImport("user32.dll")]
    public static extern int DisplayConfigSetDeviceInfo(ref AdvancedColorState color);

    [DllImport("user32.dll")]
    public static extern int SetDisplayConfig(uint pathCount, [In] PathInfo[] paths, uint modeCount, IntPtr modes, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool EnumDisplayDevices(string? device, uint index, ref DisplayDevice result, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool EnumDisplaySettings(string device, int index, ref DevMode result);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int ChangeDisplaySettingsEx(string? device, ref DevMode mode, IntPtr window, uint flags, IntPtr param);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DisplayDevice
    {
        public static readonly int SizeInBytes = Marshal.SizeOf<DisplayDevice>();
        public int Size;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;

        public uint StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PointL
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DevMode
    {
        public static readonly ushort SizeInBytes = (ushort)Marshal.SizeOf<DevMode>();

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        public ushort SpecVersion, DriverVersion, Size, DriverExtra;
        public uint Fields;
        public PointL Position;
        public uint Orientation, FixedOutput;
        public short Color, Duplex, YResolution, TTOption, Collate;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string FormName;

        public ushort LogPixels;
        public uint BitsPerPixel, Width, Height, DisplayFlags, Frequency;
        public uint IcmMethod, IcmIntent, MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Luid
    {
        public uint Low;
        public int High;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SourceInfo
    {
        public Luid Adapter;
        public uint Id, ModeIndex, Status;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Rational
    {
        public uint Numerator, Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TargetInfo
    {
        public Luid Adapter;
        public uint Id, ModeIndex, Technology, Rotation, Scaling;
        public Rational Refresh;
        public uint Scanline, Available, Status;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PathInfo
    {
        public SourceInfo Source;
        public TargetInfo Target;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Header
    {
        public int Type;
        public uint Size;
        public Luid Adapter;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DpiScaleGet
    {
        public Header Header;
        public int MinScaleRel, CurScaleRel, MaxScaleRel;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DpiScaleSet
    {
        public Header Header;
        public int ScaleRel;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AdvancedColorInfo
    {
        public Header Header;
        public uint Values, ColorEncoding;
        public int BitsPerColorChannel;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AdvancedColorInfo2
    {
        public Header Header;
        public uint Values, ColorEncoding;
        public int BitsPerColorChannel;
        public int ActiveColorMode;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AdvancedColorState
    {
        public Header Header;
        public uint Values;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SourceName
    {
        public Header Header;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string GdiName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct TargetName
    {
        public Header Header;
        public uint Flags, Technology;
        public ushort Manufacturer, Product;
        public uint Connector;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string FriendlyName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DevicePath;
    }
}