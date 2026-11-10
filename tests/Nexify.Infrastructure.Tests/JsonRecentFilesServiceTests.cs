using Microsoft.Extensions.Time.Testing;
using Nexify.Core.Media;
using Nexify.Infrastructure;
using Nexify.Infrastructure.Recent;
using Serilog;
using Xunit;

namespace Nexify.Infrastructure.Tests;

public class JsonRecentFilesServiceTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly AppPaths _paths;
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero));

    public JsonRecentFilesServiceTests() => _paths = new AppPaths(_dir.Path, _dir.Combine("logs"));

    public void Dispose() => _dir.Dispose();

    private JsonRecentFilesService Create()
    {
        var s = new JsonRecentFilesService(_paths, new LoggerConfiguration().CreateLogger(), _time);
        s.Load();
        return s;
    }

    private string Touch(string name)
    {
        var p = _dir.Combine(name);
        File.WriteAllText(p, "x");
        return p;
    }

    private MediaItem Item(string path) => new(path, MediaKind.Audio);

    [Fact]
    public void Add_NewestFirst()
    {
        var s = Create();
        var a = Touch("a.mp3"); var b = Touch("b.mp3");
        s.Add(Item(a));
        _time.Advance(TimeSpan.FromMinutes(1));
        s.Add(Item(b));
        var v = s.GetVisible();
        Assert.Equal([b, a], v.Select(r => r.Path));
        Assert.Equal(_time.GetUtcNow(), v[0].OpenedAtUtc);
    }

    [Fact]
    public void Add_Existing_MovesToTop_NoDuplicate()
    {
        var s = Create();
        var a = Touch("a.mp3"); var b = Touch("b.mp3");
        s.Add(Item(a));
        s.Add(Item(b));
        s.Add(Item(a.ToUpperInvariant()));
        // Stored list is checked directly: on a case-sensitive file system the upper-cased path does not exist.
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(_paths.RecentFile));
        var stored = doc.RootElement.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("path").GetString()).ToList();
        Assert.Equal([a.ToUpperInvariant(), b], stored);
    }

    [Fact]
    public void Add_Beyond20_DropsOldest()
    {
        var s = Create();
        var paths = Enumerable.Range(0, 21).Select(i => Touch($"f{i}.mp3")).ToList();
        foreach (var p in paths) s.Add(Item(p));
        var v = s.GetVisible();
        Assert.Equal(20, v.Count);
        Assert.Equal(paths[20], v[0].Path);
        Assert.DoesNotContain(v, r => r.Path == paths[0]);
    }

    [Fact]
    public void GetVisible_HidesMissingFiles_ButKeepsThemStored()
    {
        var s = Create();
        var a = Touch("a.mp3");
        s.Add(Item(a));
        File.Delete(a);
        Assert.Empty(s.GetVisible());
        Assert.Contains("a.mp3", File.ReadAllText(_paths.RecentFile));
        File.WriteAllText(a, "x");
        Assert.Single(s.GetVisible());
    }

    [Fact]
    public void Remove_And_Clear_Persist()
    {
        var s = Create();
        var a = Touch("a.mp3"); var b = Touch("b.mp3");
        s.Add(Item(a)); s.Add(Item(b));
        s.Remove(a.ToUpperInvariant());
        Assert.Equal([b], Create().GetVisible().Select(r => r.Path));
        s.Clear();
        Assert.Empty(Create().GetVisible());
    }

    [Fact]
    public void Load_Corrupt_GivesEmpty_AndCorruptCopy()
    {
        File.WriteAllText(_paths.RecentFile, "[[ nope");
        var s = Create();
        Assert.Empty(s.GetVisible());
        Assert.Equal("[[ nope", File.ReadAllText(Path.Combine(_dir.Path, "recent.corrupt.json")));
        Assert.False(File.Exists(_paths.RecentFile));
    }

    [Fact]
    public void RoundTrip_PreservesKindAndTime()
    {
        var s = Create();
        var a = Touch("a.mp4");
        s.Add(new MediaItem(a, MediaKind.Video));
        var r = Create().GetVisible().Single();
        Assert.Equal(MediaKind.Video, r.Kind);
        Assert.Equal(_time.GetUtcNow(), r.OpenedAtUtc);
        Assert.Contains("\"schemaVersion\": 1", File.ReadAllText(_paths.RecentFile));
    }

    [Fact]
    public void Changes_RaiseChanged()
    {
        var s = Create();
        var n = 0;
        s.Changed += (_, _) => n++;
        s.Add(Item(Touch("a.mp3")));
        s.Remove("whatever");
        s.Clear();
        Assert.True(n >= 2);
    }
}
