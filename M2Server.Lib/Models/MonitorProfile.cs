namespace M2Server.Lib.Models;

/// <summary>
///     Represents a monitor profile definition.
/// </summary>
public class MonitorProfile : IEquatable<MonitorProfile>
{
    public string Name { get; set; } = string.Empty;

    public string DeviceName { get; set; } = string.Empty;

    public string Resolution { get; set; } = string.Empty;

    public int RefreshRate { get; set; }

    public bool IsPrimary { get; set; }

    public bool IsHidden { get; set; }

    public int Position { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public static IEnumerable<MonitorProfile> Empty => Enumerable.Empty<MonitorProfile>();

    public bool Equals(MonitorProfile? other)
    {
        return ReferenceEquals(this, other) || (other is not null && this.Name == other.Name &&
                                                this.DeviceName == other.DeviceName && this.Resolution == other.Resolution &&
                                                this.RefreshRate == other.RefreshRate && this.IsPrimary == other.IsPrimary &&
                                                this.IsHidden == other.IsHidden && this.Position == other.Position &&
                                                this.Width == other.Width && this.Height == other.Height);
    }

    public override bool Equals(object? obj)
    {
        return this.Equals(obj as MonitorProfile);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(this.Name, this.DeviceName, this.Resolution);
    }
}