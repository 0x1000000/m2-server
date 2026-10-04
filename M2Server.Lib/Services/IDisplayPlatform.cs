using M2Server.Lib.Domain;

namespace M2Server.Lib.Services;

public sealed record DisplayTargetIdentity(
    string Path,
    string Name,
    string Model,
    IReadOnlySet<string> SourceNames,
    bool Active,
    DisplayRotation Rotation)
{
    internal WinApiProxy.Luid SourceAdapter { get; init; }

    internal uint SourceId { get; init; }

    internal WinApiProxy.Luid TargetAdapter { get; init; }

    internal uint TargetId { get; init; }
}

public sealed record DisplayTopologyTarget(
    string Path,
    string PreferredSource,
    DisplayRotation Rotation = DisplayRotation.Identity);

public sealed record DisplayDpiScaleInfo(IReadOnlyList<int> Supported, int? Current, int? Recommended);

public sealed record DisplayLayoutRequest(
    string DeviceName,
    DisplayMode Mode,
    DisplayRotation Rotation,
    int PositionX,
    int PositionY);

public interface IDisplayPlatform
{
    IReadOnlyList<MonitorInfo> GetMonitors();

    IReadOnlyList<string> DiagnoseCurrentModes();

    IReadOnlyList<DisplayTargetIdentity> AvailableTargets();

    (bool Success, string Message, int? WindowsError) SetTopology(IReadOnlyList<DisplayTopologyTarget> targets, bool apply);

    (bool Success, string Message, int? WindowsError) SetPrimary(string targetPath);

    int TestLayout(DisplayLayoutRequest request);

    int ApplyLayout(DisplayLayoutRequest request);

    int SetDpiScale(DisplayTargetIdentity target, int desiredPercent);

    (bool Supported, bool Enabled) GetHdrInfo(DisplayTargetIdentity target);

    int SetHdr(DisplayTargetIdentity target, bool enabled);
}