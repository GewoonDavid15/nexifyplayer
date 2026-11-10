namespace Nexify.Core.Media;

public static class MediaFormats
{
    public static IReadOnlySet<string> AudioExtensions { get; } = new HashSet<string>(
        [".mp3", ".wav", ".flac", ".aac", ".m4a", ".ogg", ".oga", ".opus", ".wma", ".alac"],
        StringComparer.OrdinalIgnoreCase);

    public static IReadOnlySet<string> VideoExtensions { get; } = new HashSet<string>(
        [".mp4", ".m4v", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".mpeg", ".mpg", ".ts", ".m2ts"],
        StringComparer.OrdinalIgnoreCase);

    public static MediaKind? Detect(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var ext = System.IO.Path.GetExtension(path);
        if (AudioExtensions.Contains(ext)) return MediaKind.Audio;
        if (VideoExtensions.Contains(ext)) return MediaKind.Video;
        return null;
    }

    public static bool IsSupported(string path) => Detect(path) is not null;

    public static MediaItem? TryCreate(string path)
        => Detect(path) is { } kind ? new MediaItem(path, kind) : null;
}
