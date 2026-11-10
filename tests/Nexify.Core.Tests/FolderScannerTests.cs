using Nexify.Core.Media;
using Xunit;

namespace Nexify.Core.Tests;

public sealed class FolderScannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "nexify-scan-" + Guid.NewGuid().ToString("N"));

    public FolderScannerTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { /* best effort */ }
    }

    private string Touch(params string[] parts)
    {
        var path = Path.Combine([_root, .. parts]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
        return path;
    }

    [Fact]
    public async Task Scan_FindsFilesRecursively_FoldersBeforeFiles()
    {
        var b = Touch("b.mp3");
        var x = Touch("A", "x.mp3");
        var deep = Touch("A", "sub", "y.mp4");
        var a2 = Touch("A", "a.mp3");
        var r = await FolderScanner.ScanAsync(_root);
        Assert.Equal([deep, a2, x, b], r.Items.Select(i => i.Path));
        Assert.Equal(MediaKind.Video, r.Items[0].Kind);
    }

    [Fact]
    public async Task Scan_SortsNaturallyWithinFolder()
    {
        var p10 = Touch("10.mp3"); var p2 = Touch("2.mp3"); var p1 = Touch("1.mp3");
        var r = await FolderScanner.ScanAsync(_root);
        Assert.Equal([p1, p2, p10], r.Items.Select(i => i.Path));
    }

    [Fact]
    public async Task Scan_CountsUnsupported()
    {
        Touch("a.mp3"); Touch("b.txt"); Touch("c.srt");
        var r = await FolderScanner.ScanAsync(_root);
        Assert.Single(r.Items);
        Assert.Equal(2, r.UnsupportedCount);
        Assert.Empty(r.InaccessibleFolders);
    }

    [Fact]
    public async Task Scan_IgnoresDotAndHiddenFilesSilently()
    {
        Touch("a.mp3"); Touch(".hidden.mp3"); Touch(".DS_Store");
        var r = await FolderScanner.ScanAsync(_root);
        Assert.Single(r.Items);
        Assert.Equal(0, r.UnsupportedCount);
    }

    [Fact]
    public async Task Scan_SkipsHiddenFolders()
    {
        Touch("visible", "v.mp3");
        Touch(".hidden", "h.mp3");
        var attrDir = Path.Combine(_root, "attr");
        Touch("attr", "a.mp3");
        File.SetAttributes(attrDir, File.GetAttributes(attrDir) | FileAttributes.Hidden);
        // On Linux SetAttributes(Hidden) is a no-op; only assert the attribute case when it stuck.
        var attrStuck = File.GetAttributes(attrDir).HasFlag(FileAttributes.Hidden);

        var r = await FolderScanner.ScanAsync(_root);
        var names = r.Items.Select(i => Path.GetFileName(i.Path)).ToList();
        Assert.Contains("v.mp3", names);
        Assert.DoesNotContain("h.mp3", names);
        if (attrStuck) Assert.DoesNotContain("a.mp3", names);
        else Assert.Contains("a.mp3", names);
    }

    [Fact]
    public async Task Scan_NonexistentFolder_ReportsInaccessible_NoThrow()
    {
        var missing = Path.Combine(_root, "nope");
        var r = await FolderScanner.ScanAsync(missing);
        Assert.Empty(r.Items);
        Assert.Equal([missing], r.InaccessibleFolders);
    }

    [Fact]
    public async Task Scan_IsCancellable_AndRunsOffCallerThread()
    {
        for (var d = 0; d < 20; d++)
            for (var f = 0; f < 100; f++)
                Touch($"d{d}", $"f{f}.mp3");

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => FolderScanner.ScanAsync(_root, cts.Token));

        var task = FolderScanner.ScanAsync(_root);
        Assert.False(task.IsCompleted); // work is queued to the thread pool, not done synchronously
        var r = await task;
        Assert.Equal(2000, r.Items.Count);
    }

    [Fact]
    public void ClassifyPaths_MixedFilesAndFolders_PreservesGivenOrder()
    {
        var f2 = Touch("loose", "file2.mp3");
        var inFolder = Touch("folder", "a.mp3");
        var f1 = Touch("loose", "file1.mp3");
        var txt = Touch("loose", "x.txt");
        var r = FolderScanner.ClassifyPaths([f2, Path.Combine(_root, "folder"), f1, txt]);
        Assert.Equal([f2, inFolder, f1], r.Items.Select(i => i.Path));
        Assert.Equal(1, r.UnsupportedCount);
    }

    [Fact]
    public async Task ClassifyPathsAsync_MatchesSync()
    {
        var a = Touch("folder", "a.mp3");
        var r = await FolderScanner.ClassifyPathsAsync([Path.Combine(_root, "folder")]);
        Assert.Equal([a], r.Items.Select(i => i.Path));
    }
}
