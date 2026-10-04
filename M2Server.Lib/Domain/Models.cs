namespace M2Server.Lib.Domain;

public sealed record DisplayMode(int Width, int Height, int RefreshHz);

public enum DisplayRotation
{
    Identity = 1,
    Rotate90 = 2,
    Rotate180 = 3,
    Rotate270 = 4
}

public static class DisplayRotationGeometry
{
    // Profiles keep a monitor's native width and height. Windows swaps them in
    // DEVMODE when a display is rotated by a quarter turn.
    public static bool SwapsDimensions(this DisplayRotation rotation)
    {
        return rotation is DisplayRotation.Rotate90 or DisplayRotation.Rotate270;
    }

    public static DisplayMode Orient(this DisplayMode mode, DisplayRotation rotation)
    {
        return rotation.SwapsDimensions() ? new DisplayMode(mode.Height, mode.Width, mode.RefreshHz) : mode;
    }
}

public sealed record MonitorInfo(
    string Id,
    string Name,
    string DeviceName,
    bool Enabled,
    bool Primary,
    DisplayMode? CurrentMode,
    IReadOnlyList<DisplayMode> Modes,
    int PositionX,
    int PositionY,
    IReadOnlyList<int> DpiScales,
    int? CurrentDpiScale,
    bool HdrSupported,
    bool? HdrEnabled,
    DisplayRotation Rotation);

public sealed class MonitorSetting
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public bool Enabled { get; set; } = true;

    public bool Primary { get; set; }

    public int Order { get; set; }

    public DisplayMode Mode { get; set; } = new(1920, 1080, 60);

    public DisplayRotation Rotation { get; set; } = DisplayRotation.Identity;

    public int? DpiScale { get; set; }

    public bool? HdrEnabled { get; set; }
}

public sealed class Preset
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    public List<MonitorSetting> Monitors { get; set; } = [];
}

public sealed class ScriptEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    public string Script { get; set; } = "";
}

public sealed class AppSettings
{
    public string? PasswordHash { get; set; }

    // Null preserves the behavior of settings saved before this switch existed.
    public bool? WebEnabled { get; set; }

    public bool AllowLanAccess { get; set; }

    public bool AllowPublicLanAccess { get; set; }

    public string StartupPath { get; set; } = "";

    public bool AutoStartup { get; set; }

    public int HttpsPort { get; set; }

    public string? CertificateThumbprint { get; set; }
}

public sealed class AppDocument
{
    public List<Preset> Presets { get; set; } = [];

    public List<ScriptEntry> Scripts { get; set; } = [];

    public AppSettings Settings { get; set; } = new();

    public Dictionary<string, DateTimeOffset> Tokens { get; set; } = [];

    public Guid? ActivePresetId { get; set; }
}

public sealed record ActivationStepResult(string Phase, bool Success, string Message, int? WindowsError = null);

public sealed record MonitorActivationResult(
    string Id,
    string Name,
    bool TopologyApplied,
    bool PrimaryApplied,
    bool ModeApplied,
    bool PositionApplied,
    bool DpiApplied,
    bool HdrApplied,
    IReadOnlyList<string> Warnings,
    int? WindowsError = null);

public sealed record ActivationResult(
    bool Success,
    string Message,
    IReadOnlyList<ActivationStepResult>? Steps = null,
    IReadOnlyList<MonitorActivationResult>? Monitors = null);