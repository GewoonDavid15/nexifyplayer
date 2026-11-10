using Nexify.Core.Media;

namespace Nexify.Core.Recent;

public interface IRecentFilesService
{
    void Load();

    /// <summary>Newest first; omits files that no longer exist (they stay in the stored list).</summary>
    IReadOnlyList<RecentFile> GetVisible();

    void Add(MediaItem item);
    void Remove(string path);
    void Clear();

    event EventHandler? Changed;
}
