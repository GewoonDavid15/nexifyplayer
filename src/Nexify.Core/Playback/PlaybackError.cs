using Nexify.Core.Media;

namespace Nexify.Core.Playback;

public enum PlaybackErrorReason { FileNotFound, AccessDenied, UnsupportedOrCorrupt, EngineUnavailable, Unknown }

/// <summary>A playback failure. <paramref name="Item"/> is null when no media item is involved (e.g. engine load failure).</summary>
public sealed record PlaybackError(MediaItem? Item, PlaybackErrorReason Reason, string? Detail);
