namespace Nexify.Core.Playback;

/// <summary>
/// Classifies a file before it is handed to the playback engine. Pure file-system check, no VLC.
/// A failure reported by VLC after opening is <see cref="PlaybackErrorReason.UnsupportedOrCorrupt"/>.
/// </summary>
public static class PlaybackErrorClassifier
{
    /// <returns>
    /// <see cref="PlaybackErrorReason.FileNotFound"/> if the file does not exist,
    /// <see cref="PlaybackErrorReason.AccessDenied"/> if opening it for reading is denied,
    /// otherwise <see cref="PlaybackErrorReason.Unknown"/> (meaning: may be opened).
    /// </returns>
    public static PlaybackErrorReason ClassifyBeforeOpen(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return PlaybackErrorReason.FileNotFound;
        try
        {
            using var _ = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return PlaybackErrorReason.Unknown;
        }
        catch (UnauthorizedAccessException)
        {
            return PlaybackErrorReason.AccessDenied;
        }
        catch (FileNotFoundException)
        {
            return PlaybackErrorReason.FileNotFound;
        }
        catch (DirectoryNotFoundException)
        {
            return PlaybackErrorReason.FileNotFound;
        }
        catch (IOException)
        {
            // e.g. exclusively locked by another process: let VLC try.
            return PlaybackErrorReason.Unknown;
        }
    }
}
