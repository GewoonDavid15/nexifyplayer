namespace Nexify.Core.Media;

/// <summary>
/// A playable file. Equality is by Path and Kind only; <see cref="Id"/> is unique per instance
/// so the same file can occur more than once in the queue and be removed individually.
/// </summary>
public sealed record MediaItem(string Path, MediaKind Kind)
{
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>File name without extension. Handles both '\' and '/' separators on every OS.</summary>
    public string DisplayName
    {
        get
        {
            var name = Path[(Path.LastIndexOfAny(['\\', '/']) + 1)..];
            var dot = name.LastIndexOf('.');
            return dot > 0 ? name[..dot] : name;
        }
    }

    public bool Equals(MediaItem? other)
        => other is not null && Kind == other.Kind && string.Equals(Path, other.Path, StringComparison.Ordinal);

    public override int GetHashCode() => HashCode.Combine(Path, Kind);
}
