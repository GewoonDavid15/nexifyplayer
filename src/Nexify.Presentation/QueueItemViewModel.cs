using CommunityToolkit.Mvvm.ComponentModel;
using Nexify.Core.Media;

namespace Nexify.Presentation;

/// <summary>One row of the queue list on the Now playing page.</summary>
public sealed partial class QueueItemViewModel : ObservableObject
{
    public QueueItemViewModel(MediaItem item)
    {
        Item = item ?? throw new ArgumentNullException(nameof(item));
        _title = item.DisplayName;
        Folder = MediaText.FolderName(item.Path);
    }

    public MediaItem Item { get; }
    public bool IsVideo => Item.Kind == MediaKind.Video;

    /// <summary>Name of the folder that contains the file.</summary>
    public string Folder { get; }

    /// <summary>File name without extension; the metadata title once known for the playing item.</summary>
    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private bool _isCurrent;
}

internal static class MediaText
{
    /// <summary>File name with extension; handles '\' and '/' on every OS.</summary>
    public static string FileName(string path) => path[(path.LastIndexOfAny(['\\', '/']) + 1)..];

    /// <summary>Name of the containing folder (empty for a bare file name); handles '\' and '/' on every OS.</summary>
    public static string FolderName(string path)
    {
        var dirEnd = path.LastIndexOfAny(['\\', '/']);
        if (dirEnd <= 0) return string.Empty;
        var dir = path[..dirEnd].TrimEnd('\\', '/');
        var name = dir[(dir.LastIndexOfAny(['\\', '/']) + 1)..];
        return name.EndsWith(':') ? dir : name; // "C:" for a file in a drive root
    }
}
