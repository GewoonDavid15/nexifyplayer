namespace Nexify.Core.Media;

public sealed record ScanResult(
    IReadOnlyList<MediaItem> Items,
    int UnsupportedCount,
    IReadOnlyList<string> InaccessibleFolders);
