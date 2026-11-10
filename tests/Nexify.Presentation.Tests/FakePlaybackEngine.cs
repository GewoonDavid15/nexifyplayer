using Nexify.Core.Localization;
using Nexify.Core.Media;
using Nexify.Core.Playback;
using Nexify.Core.Recent;
using Nexify.Core.Settings;
using Nexify.Presentation;

namespace Nexify.Presentation.Tests;

/// <summary>Records calls and lets tests raise the engine events. Always ready.</summary>
public sealed class FakePlaybackEngine : IPlaybackEngine
{
    public List<MediaItem> Opened { get; } = [];
    public List<TimeSpan> Seeks { get; } = [];
    public List<TimeSpan> RelativeSeeks { get; } = [];
    public int StopCalls { get; private set; }
    public int ToggleCalls { get; private set; }

    public Task InitializeAsync() => Task.CompletedTask;
    public bool IsReady { get; set; } = true;
    public PlaybackState State { get; set; } = PlaybackState.Idle;
    public MediaItem? Current { get; private set; }
    public MediaKind? CurrentKind => Current?.Kind;
    public string? CurrentTitle { get; set; }
    public string? CurrentArtist { get; set; }
    public TimeSpan Position { get; set; }
    public TimeSpan Duration { get; set; }
    public int Volume { get; set; }
    public bool IsMuted { get; set; }
    public object? VideoSurfaceSource { get; set; }

    public Task OpenAsync(MediaItem item)
    {
        Opened.Add(item);
        Current = item;
        State = PlaybackState.Opening;
        Position = TimeSpan.Zero;
        return Task.CompletedTask;
    }

    public void Play() => State = PlaybackState.Playing;
    public void Pause() => State = PlaybackState.Paused;
    public void TogglePlayPause() => ToggleCalls++;

    public void Stop()
    {
        StopCalls++;
        State = PlaybackState.Stopped;
    }

    public void Seek(TimeSpan position)
    {
        Seeks.Add(position);
        Position = position;
    }

    public void SeekRelative(TimeSpan delta) => RelativeSeeks.Add(delta);

    public event EventHandler<PlaybackState>? StateChanged;
    public event EventHandler<TimeSpan>? PositionChanged;
    public event EventHandler<TimeSpan>? DurationChanged;
    public event EventHandler? EndReached;
    public event EventHandler<PlaybackError>? PlaybackFailed;
    public event EventHandler? MetadataChanged;

    public void RaiseState(PlaybackState state)
    {
        State = state;
        StateChanged?.Invoke(this, state);
    }

    public void RaisePosition(TimeSpan position)
    {
        Position = position;
        PositionChanged?.Invoke(this, position);
    }

    public void RaiseDuration(TimeSpan duration)
    {
        Duration = duration;
        DurationChanged?.Invoke(this, duration);
    }

    public void RaiseEndReached()
    {
        State = PlaybackState.Ended;
        EndReached?.Invoke(this, EventArgs.Empty);
    }

    public void RaiseFailed(PlaybackErrorReason reason)
    {
        State = PlaybackState.Error;
        PlaybackFailed?.Invoke(this, new PlaybackError(Current, reason, null));
    }

    public void RaiseMetadata() => MetadataChanged?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
    }
}

public sealed class SyncDispatcher : IUiDispatcher
{
    public void Post(Action action) => action();
}

public sealed class FakeSettingsService : ISettingsService
{
    public AppSettings Current { get; private set; } = new();
    public bool LoadedFromCorruptFile => false;
    public int ImmediateUpdates { get; private set; }
    public int DebouncedUpdates { get; private set; }

    public FakeSettingsService(AppSettings? initial = null) => Current = initial ?? new AppSettings();

    public void Load()
    {
    }

    public void Update(Func<AppSettings, AppSettings> change)
    {
        ImmediateUpdates++;
        Current = change(Current);
        Changed?.Invoke(this, Current);
    }

    public void UpdateDebounced(Func<AppSettings, AppSettings> change)
    {
        DebouncedUpdates++;
        Current = change(Current);
        Changed?.Invoke(this, Current);
    }

    public void Flush()
    {
    }

    public event EventHandler<AppSettings>? Changed;
}

public sealed class FakeRecentFiles : IRecentFilesService
{
    public List<MediaItem> Added { get; } = [];

    public void Load()
    {
    }

    public IReadOnlyList<RecentFile> GetVisible() => [];
    public void Add(MediaItem item) => Added.Add(item);

    public void Remove(string path)
    {
    }

    public void Clear()
    {
    }

    public event EventHandler? Changed
    {
        add { }
        remove { }
    }
}

/// <summary>Returns "key" or "key|arg1|arg2", so tests can check which text was used.</summary>
public sealed class FakeLocalization : ILocalizationService
{
    public IReadOnlyList<LanguageInfo> Available { get; } = [new("en", "English")];
    public string CurrentCode => "en";

    public void SetLanguage(string code)
    {
    }

    public string this[string key] => key;
    public string Format(string key, params object[] args) => string.Join("|", [key, .. args.Select(a => a.ToString())]);

    public event EventHandler? LanguageChanged
    {
        add { }
        remove { }
    }
}

public sealed class RecordingNotifications : INotificationService
{
    public List<(string Message, NotificationKind Kind)> Shown { get; } = [];
    public void Show(string message, NotificationKind kind = NotificationKind.Info) => Shown.Add((message, kind));
}
