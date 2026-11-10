namespace Nexify.Core.Media;

public static class FolderScanner
{
    public static Task<ScanResult> ScanAsync(string folder, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.Run(() =>
        {
            var state = new State();
            ScanFolder(folder, state, ct);
            return state.ToResult();
        }, ct);
    }

    public static ScanResult ClassifyPaths(IEnumerable<string> paths) => Classify(paths, CancellationToken.None);

    public static Task<ScanResult> ClassifyPathsAsync(IEnumerable<string> paths, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var list = paths.ToList();
        return Task.Run(() => Classify(list, ct), ct);
    }

    private static ScanResult Classify(IEnumerable<string> paths, CancellationToken ct)
    {
        var state = new State();
        foreach (var path in paths)
        {
            ct.ThrowIfCancellationRequested();
            if (Directory.Exists(path))
                ScanFolder(path, state, ct);
            else if (MediaFormats.TryCreate(path) is { } item)
                state.Items.Add(item);
            else
                state.Unsupported++;
        }
        return state.ToResult();
    }

    private static void ScanFolder(string folder, State state, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        string[] dirs, files;
        try
        {
            dirs = Directory.GetDirectories(folder);
            files = Directory.GetFiles(folder);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or DirectoryNotFoundException)
        {
            state.Inaccessible.Add(folder);
            return;
        }

        Array.Sort(dirs, (a, b) => NaturalStringComparer.Instance.Compare(Path.GetFileName(a), Path.GetFileName(b)));
        Array.Sort(files, (a, b) => NaturalStringComparer.Instance.Compare(Path.GetFileName(a), Path.GetFileName(b)));

        foreach (var dir in dirs)
        {
            if (IsSkipped(dir, FileAttributes.Hidden | FileAttributes.System)) continue;
            ScanFolder(dir, state, ct);
        }

        foreach (var file in files)
        {
            if (IsSkipped(file, FileAttributes.Hidden)) continue;
            if (MediaFormats.TryCreate(file) is { } item) state.Items.Add(item);
            else state.Unsupported++;
        }
    }

    private static bool IsSkipped(string path, FileAttributes mask)
    {
        if (Path.GetFileName(path).StartsWith('.')) return true;
        try { return (File.GetAttributes(path) & mask) != 0; }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException) { return false; }
    }

    private sealed class State
    {
        public List<MediaItem> Items { get; } = [];
        public List<string> Inaccessible { get; } = [];
        public int Unsupported;
        public ScanResult ToResult() => new(Items, Unsupported, Inaccessible);
    }
}
