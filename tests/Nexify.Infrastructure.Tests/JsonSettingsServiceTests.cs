using Microsoft.Extensions.Time.Testing;
using Nexify.Core.Queue;
using Nexify.Core.Settings;
using Nexify.Infrastructure;
using Nexify.Infrastructure.Settings;
using Serilog;
using Xunit;

namespace Nexify.Infrastructure.Tests;

public class JsonSettingsServiceTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly AppPaths _paths;

    public JsonSettingsServiceTests() => _paths = new AppPaths(_dir.Path, _dir.Combine("logs"));

    public void Dispose() => _dir.Dispose();

    private JsonSettingsService Create(TimeProvider? time = null)
        => new(_paths, new LoggerConfiguration().CreateLogger(), time);

    [Fact]
    public void Load_MissingFile_GivesDefaults()
    {
        var s = Create();
        s.Load();
        Assert.Equal(new AppSettings(), s.Current);
        Assert.False(s.LoadedFromCorruptFile);
        Assert.Equal(80, s.Current.DefaultVolume);
        Assert.Null(s.Current.Language);
    }

    [Fact]
    public void Update_ThenNewServiceLoad_RoundTrips()
    {
        var expected = new AppSettings
        {
            Theme = ThemeMode.System,
            Language = "nl",
            Volume = 33,
            DefaultVolume = 55,
            IsMuted = true,
            SeekStepSeconds = 30,
            OpenBehavior = OpenBehavior.Append,
            Shuffle = true,
            Repeat = RepeatMode.All,
            Window = new WindowBounds(10.5, 20, 800, 600, true),
        };
        var a = Create();
        a.Load();
        a.Update(_ => expected);

        var b = Create();
        b.Load();
        Assert.Equal(expected, b.Current);
        var json = File.ReadAllText(_paths.SettingsFile);
        Assert.Contains("\"theme\": \"system\"", json);
        Assert.Contains("\"repeat\": \"all\"", json);
    }

    [Fact]
    public void Load_CorruptJson_CreatesCorruptCopy_AndDefaults()
    {
        File.WriteAllText(_paths.SettingsFile, "{not json");
        var s = Create();
        s.Load();
        Assert.True(s.LoadedFromCorruptFile);
        Assert.Equal(new AppSettings(), s.Current);
        var corrupt = Path.Combine(_dir.Path, "settings.corrupt.json");
        Assert.Equal("{not json", File.ReadAllText(corrupt));
        Assert.False(File.Exists(_paths.SettingsFile));
    }

    [Fact]
    public void Load_UnknownFields_Ignored()
    {
        File.WriteAllText(_paths.SettingsFile, """{"volume":40,"somethingNew":{"a":1}}""");
        var s = Create();
        s.Load();
        Assert.False(s.LoadedFromCorruptFile);
        Assert.Equal(40, s.Current.Volume);
    }

    [Fact]
    public void Load_MissingFields_Defaulted()
    {
        File.WriteAllText(_paths.SettingsFile, """{"volume":30}""");
        var s = Create();
        s.Load();
        Assert.Equal(30, s.Current.Volume);
        Assert.Equal(ThemeMode.Dark, s.Current.Theme);
        Assert.Equal(5, s.Current.SeekStepSeconds);
    }

    [Fact]
    public void Load_OutOfRangeValues_Clamped()
    {
        File.WriteAllText(_paths.SettingsFile, """{"volume":250,"defaultVolume":-4,"seekStepSeconds":7}""");
        var s = Create();
        s.Load();
        Assert.Equal(100, s.Current.Volume);
        Assert.Equal(0, s.Current.DefaultVolume);
        Assert.Equal(5, s.Current.SeekStepSeconds);
    }

    [Fact]
    public void Load_NullJson_TreatedAsCorrupt()
    {
        File.WriteAllText(_paths.SettingsFile, "null");
        var s = Create();
        s.Load();
        Assert.True(s.LoadedFromCorruptFile);
    }

    [Fact]
    public void UpdateDebounced_WritesOnlyAfter500ms()
    {
        var time = new FakeTimeProvider();
        var s = Create(time);
        s.Load();
        s.UpdateDebounced(c => c with { Volume = 12 });
        Assert.Equal(12, s.Current.Volume);
        Assert.False(File.Exists(_paths.SettingsFile));

        time.Advance(TimeSpan.FromMilliseconds(499));
        Assert.False(File.Exists(_paths.SettingsFile));

        time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(File.Exists(_paths.SettingsFile));
        Assert.Contains("\"volume\": 12", File.ReadAllText(_paths.SettingsFile));
    }

    [Fact]
    public void UpdateDebounced_RestartsTimerOnEachCall()
    {
        var time = new FakeTimeProvider();
        var s = Create(time);
        s.Load();
        s.UpdateDebounced(c => c with { Volume = 1 });
        time.Advance(TimeSpan.FromMilliseconds(400));
        s.UpdateDebounced(c => c with { Volume = 2 });
        time.Advance(TimeSpan.FromMilliseconds(400));
        Assert.False(File.Exists(_paths.SettingsFile));
        time.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Contains("\"volume\": 2", File.ReadAllText(_paths.SettingsFile));
    }

    [Fact]
    public void Flush_WritesPendingImmediately()
    {
        var time = new FakeTimeProvider();
        var s = Create(time);
        s.Load();
        s.UpdateDebounced(c => c with { Volume = 7 });
        s.Flush();
        Assert.Contains("\"volume\": 7", File.ReadAllText(_paths.SettingsFile));
    }

    [Fact]
    public void Flush_WithNothingPending_WritesNothing()
    {
        var s = Create(new FakeTimeProvider());
        s.Load();
        s.Flush();
        Assert.False(File.Exists(_paths.SettingsFile));
    }

    [Fact]
    public void Update_RaisesChanged_WithNormalizedValue()
    {
        var s = Create();
        s.Load();
        AppSettings? seen = null;
        s.Changed += (_, v) => seen = v;
        s.Update(c => c with { Volume = 999 });
        Assert.Equal(100, seen?.Volume);
    }
}
