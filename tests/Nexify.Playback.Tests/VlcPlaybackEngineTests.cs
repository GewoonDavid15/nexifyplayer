using System.Collections.Concurrent;
using LibVLCSharp.Shared;
using Nexify.Core.Media;
using Nexify.Core.Playback;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;
using Xunit.Abstractions;

namespace Nexify.Playback.Tests;

public sealed class VlcPlaybackEngineTests : IDisposable
{
    private static readonly TimeSpan PlayingTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SeekTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan EndTimeout = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan Expected = TimeSpan.FromSeconds(3);

    private readonly ITestOutputHelper _output;
    private readonly Logger _log;
    private readonly List<VlcPlaybackEngine> _engines = [];

    public VlcPlaybackEngineTests(ITestOutputHelper output)
    {
        _output = output;
        _log = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(new OutputSink(output)).CreateLogger();
    }

    public void Dispose()
    {
        foreach (var e in _engines) e.Dispose();
        _log.Dispose();
    }

    private VlcPlaybackEngine NewEngine()
    {
        var engine = new VlcPlaybackEngine(_log, ["--aout=dummy", "--vout=dummy"]);
        _engines.Add(engine);
        return engine;
    }

    private async Task<VlcPlaybackEngine> ReadyEngine()
    {
        var engine = NewEngine();
        await engine.InitializeAsync();
        Assert.True(engine.IsReady, "LibVLC failed to load");
        return engine;
    }

    private static MediaItem Item(string name)
    {
        var path = TestMedia.PathOf(name);
        var kind = MediaFormats.Detect(path) ?? throw new InvalidOperationException($"Unknown format: {name}");
        return new MediaItem(path, kind);
    }

    private static async Task WaitUntil(Func<bool> condition, TimeSpan timeout, string what)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"Timed out after {timeout.TotalSeconds:0.#} s waiting for: {what}");
            await Task.Delay(20);
        }
    }

    private static async Task<T> Within<T>(Task<T> task, TimeSpan timeout, string what)
    {
        var done = await Task.WhenAny(task, Task.Delay(timeout));
        if (done != task) throw new TimeoutException($"Timed out after {timeout.TotalSeconds:0.#} s waiting for: {what}");
        return await task;
    }

    [WindowsTheory]
    [InlineData("tone.mp3")]
    [InlineData("tone.wav")]
    [InlineData("tone.flac")]
    [InlineData("tone.ogg")]
    [InlineData("tone-aac.m4a")]
    [InlineData("tone-alac.m4a")]
    [InlineData("tone.wma")]
    [InlineData("clip.mp4")]
    [InlineData("clip.mkv")]
    [InlineData("clip.avi")]
    [InlineData("clip.mov")]
    [InlineData("clip.webm")]
    [InlineData("clip.wmv")]
    [InlineData("clip.mpeg")]
    public async Task PlaysEveryFormat(string file)
    {
        var engine = await ReadyEngine();
        var item = Item(file);
        var playing = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ended = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var failed = new TaskCompletionSource<PlaybackError>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.StateChanged += (_, s) => { if (s == PlaybackState.Playing) playing.TrySetResult(true); };
        engine.EndReached += (_, _) => ended.TrySetResult(true);
        engine.PlaybackFailed += (_, e) => failed.TrySetResult(e);

        await engine.OpenAsync(item);
        var first = await Task.WhenAny(playing.Task, failed.Task, Task.Delay(PlayingTimeout));
        if (first == failed.Task) Assert.Fail($"PlaybackFailed: {failed.Task.Result}");
        Assert.True(first == playing.Task, $"State did not reach Playing within 5 s (state {engine.State})");
        Assert.Same(item, engine.Current);
        Assert.Equal(item.Kind, engine.CurrentKind);

        await WaitUntil(() => Math.Abs((engine.Duration - Expected).TotalSeconds) <= 0.5, TimeSpan.FromSeconds(2),
            $"Duration within 0.5 s of 3 s (was {engine.Duration})");

        // With --aout=dummy LibVLC decodes faster than realtime, so "position >= 1.3 s" alone would also
        // pass without a working seek. Seek while paused (LibVLC reports no time while paused), resume,
        // and require the first new position to be past 1.3 s: without the seek it would start near 0.
        engine.Pause();
        await WaitUntil(() => engine.State == PlaybackState.Paused || ended.Task.IsCompleted, SeekTimeout, $"State Paused (was {engine.State})");
        Assert.False(ended.Task.IsCompleted, "Ended before the seek could be tested");
        var before = engine.Position;
        Assert.True(before < TimeSpan.FromSeconds(1.2), $"Paused too late to test the seek (position {before})");
        engine.Seek(TimeSpan.FromSeconds(1.5));
        await Task.Delay(300);
        engine.Play();

        var deadline = DateTime.UtcNow + SeekTimeout;
        var firstNew = before;
        while (firstNew == before && !ended.Task.IsCompleted && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(1);
            firstNew = engine.Position;
        }
        _output.WriteLine($"Position before seek {before}, first after resume {firstNew}");
        Assert.True(firstNew != before, $"Position did not change within 5 s after Seek(1.5 s) + Play (still {before})");
        Assert.True(firstNew >= TimeSpan.FromSeconds(1.3), $"Position after Seek(1.5 s) was {firstNew}, expected >= 1.3 s");

        await Within(ended.Task, EndTimeout, "EndReached");
        Assert.Equal(PlaybackState.Ended, engine.State);
        Assert.False(failed.Task.IsCompleted, failed.Task.IsCompleted ? $"Unexpected failure: {failed.Task.Result}" : "");
    }

    [WindowsFact]
    public async Task Corrupt_RaisesUnsupportedOrCorrupt()
    {
        var engine = await ReadyEngine();
        var item = Item("corrupt.mp4");
        var failed = new TaskCompletionSource<PlaybackError>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.PlaybackFailed += (_, e) => failed.TrySetResult(e);

        await engine.OpenAsync(item);
        var error = await Within(failed.Task, TimeSpan.FromSeconds(10), "PlaybackFailed");

        Assert.Equal(PlaybackErrorReason.UnsupportedOrCorrupt, error.Reason);
        Assert.Same(item, error.Item);
        await WaitUntil(() => engine.State == PlaybackState.Error, TimeSpan.FromSeconds(2), "State Error");
    }

    [WindowsFact]
    public async Task Missing_RaisesFileNotFound_WithoutVlcCall()
    {
        // Not initialized: any LibVLC call would be impossible, so a correct result proves none was made.
        var engine = NewEngine();
        var item = new MediaItem(Path.Combine(TestMedia.Dir!, "does-not-exist-" + Guid.NewGuid().ToString("N") + ".mp3"), MediaKind.Audio);
        var failed = new TaskCompletionSource<PlaybackError>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.PlaybackFailed += (_, e) => failed.TrySetResult(e);

        await engine.OpenAsync(item);
        var error = await Within(failed.Task, TimeSpan.FromSeconds(2), "PlaybackFailed");

        Assert.Equal(PlaybackErrorReason.FileNotFound, error.Reason);
        Assert.Same(item, error.Item);
        Assert.False(engine.IsReady);
        Assert.Null(engine.VideoSurfaceSource);
        Assert.Equal(PlaybackState.Error, engine.State);
    }

    [WindowsFact]
    public async Task Missing_OnReadyEngine_RaisesFileNotFound()
    {
        var engine = await ReadyEngine();
        var item = new MediaItem(Path.Combine(TestMedia.Dir!, "does-not-exist-" + Guid.NewGuid().ToString("N") + ".mp3"), MediaKind.Audio);
        var failed = new TaskCompletionSource<PlaybackError>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.PlaybackFailed += (_, e) => failed.TrySetResult(e);

        await engine.OpenAsync(item);

        Assert.Equal(PlaybackErrorReason.FileNotFound, (await Within(failed.Task, TimeSpan.FromSeconds(2), "PlaybackFailed")).Reason);
    }

    [WindowsTheory]
    [InlineData("Beyoncé #1 100%.mp3")]
    [InlineData("tone %41.mp3")] // must not be read as "tone A.mp3"
    public async Task Plays_PathWithSpecialCharacters(string file)
    {
        var engine = await ReadyEngine();
        var item = Item(file);
        var playing = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var failed = new TaskCompletionSource<PlaybackError>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.StateChanged += (_, s) => { if (s == PlaybackState.Playing) playing.TrySetResult(true); };
        engine.PlaybackFailed += (_, e) => failed.TrySetResult(e);

        await engine.OpenAsync(item);
        var first = await Task.WhenAny(playing.Task, failed.Task, Task.Delay(PlayingTimeout));

        if (first == failed.Task) Assert.Fail($"PlaybackFailed: {failed.Task.Result}");
        Assert.True(first == playing.Task, "State did not reach Playing within 5 s");
        await WaitUntil(() => Math.Abs((engine.Duration - Expected).TotalSeconds) <= 0.5, TimeSpan.FromSeconds(2),
            $"Duration within 0.5 s of 3 s (was {engine.Duration})");
    }

    [WindowsFact]
    public async Task EndReached_ThenOpenNext_PlaysSecondFile()
    {
        var engine = await ReadyEngine();
        var first = Item("tone.mp3");
        var second = Item("tone.wav");
        var secondPlaying = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ended = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? openSecond = null;
        engine.EndReached += (_, _) =>
        {
            if (ended.TrySetResult(true)) openSecond = engine.OpenAsync(second);
        };
        engine.StateChanged += (_, s) =>
        {
            if (s == PlaybackState.Playing && ReferenceEquals(engine.Current, second)) secondPlaying.TrySetResult(true);
        };

        await engine.OpenAsync(first);
        await Within(ended.Task, TimeSpan.FromSeconds(10), "EndReached of first file");
        await Within(secondPlaying.Task, PlayingTimeout, "Playing of second file");

        Assert.NotNull(openSecond);
        await openSecond!;
        Assert.Same(second, engine.Current);
        Assert.Equal(PlaybackState.Playing, engine.State);
    }

    [WindowsFact]
    public async Task RapidOpen_LastOneWins()
    {
        var engine = await ReadyEngine();
        string[] files = ["tone.mp3", "tone.wav", "tone.flac", "tone.ogg", "tone-aac.m4a", "clip.mp4", "clip.mkv", "clip.avi", "tone.wma", "clip.webm"];
        var items = files.Select(Item).ToArray();
        var failures = new ConcurrentQueue<PlaybackError>();
        engine.PlaybackFailed += (_, e) => failures.Enqueue(e);

        var tasks = items.Select(engine.OpenAsync).ToArray();
        await Within(Task.WhenAll(tasks).ContinueWith(_ => true), TimeSpan.FromSeconds(10), "all OpenAsync calls");

        Assert.Same(items[9], engine.Current);
        await WaitUntil(() => engine.State == PlaybackState.Playing, PlayingTimeout, $"State Playing (was {engine.State})");
        await Task.Delay(500);
        Assert.Same(items[9], engine.Current);
        Assert.Equal(PlaybackState.Playing, engine.State);
        Assert.Empty(failures);
    }

    [WindowsFact]
    public async Task Volume_SetBeforeOpen_IsApplied()
    {
        var engine = await ReadyEngine();
        engine.Volume = 37;
        Assert.Equal(37, engine.Volume);
        var player = Assert.IsType<MediaPlayer>(engine.VideoSurfaceSource);

        await engine.OpenAsync(Item("tone.mp3"));
        await WaitUntil(() => engine.State == PlaybackState.Playing, PlayingTimeout, "State Playing");

        // With --aout=dummy LibVLC cannot report the volume back (reads 0), so check what the engine
        // pushed into the native player once it was playing.
        await WaitUntil(() => engine.AppliedVolumeWhilePlaying == 37, TimeSpan.FromSeconds(2),
            $"volume 37 applied while playing (was {engine.AppliedVolumeWhilePlaying}, native reports {player.Volume})");
        Assert.Equal(37, engine.Volume);
    }

    [WindowsFact]
    public async Task Dispose_WhilePlaying_DoesNotHang()
    {
        var engine = new VlcPlaybackEngine(_log, ["--aout=dummy", "--vout=dummy"]);
        await engine.InitializeAsync();
        await engine.OpenAsync(Item("clip.mp4"));
        await WaitUntil(() => engine.State == PlaybackState.Playing, PlayingTimeout, "State Playing");

        var dispose = Task.Run(engine.Dispose);
        await Within(dispose.ContinueWith(_ => true), TimeSpan.FromSeconds(10), "Dispose");
    }

    [WindowsFact]
    public async Task Initialize_WithInvalidOption_DoesNotThrow()
    {
        // Must never throw out of InitializeAsync; either it loads or reports EngineUnavailable.
        var engine = new VlcPlaybackEngine(_log, ["--this-option-does-not-exist-nexify"]);
        _engines.Add(engine);
        var failed = new TaskCompletionSource<PlaybackError>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.PlaybackFailed += (_, e) => failed.TrySetResult(e);

        await engine.InitializeAsync();

        if (!engine.IsReady)
            Assert.Equal(PlaybackErrorReason.EngineUnavailable, (await Within(failed.Task, TimeSpan.FromSeconds(2), "PlaybackFailed")).Reason);
    }

    private sealed class OutputSink(ITestOutputHelper output) : ILogEventSink
    {
        public void Emit(LogEvent logEvent)
        {
            try
            {
                output.WriteLine($"{logEvent.Timestamp:HH:mm:ss.fff} [{logEvent.Level}] {logEvent.RenderMessage()}{(logEvent.Exception is null ? "" : " " + logEvent.Exception)}");
            }
            catch (InvalidOperationException)
            {
                // Test already finished.
            }
        }
    }
}
