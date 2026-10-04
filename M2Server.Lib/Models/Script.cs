namespace M2Server.Lib.Models;

/// <summary>
///     Represents a script for profile generation.
/// </summary>
public class Script : IEquatable<Script>
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? Content { get; set; }

    public static IEnumerable<Script> Empty => Enumerable.Empty<Script>();

    public bool Equals(Script? other)
    {
        return ReferenceEquals(this, other) || (other is not null && this.Name == other.Name &&
                                                this.Description == other.Description && this.Content == other.Content);
    }

    public override bool Equals(object? obj)
    {
        return this.Equals(obj as Script);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(this.Name, this.Description);
    }
}