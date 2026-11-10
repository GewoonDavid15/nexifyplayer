using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexify.Core.Localization;
using Nexify.Core.Media;
using Nexify.Core.Playback;
using Nexify.Core.Queue;
using Nexify.Core.Recent;
using Nexify.Core.Settings;

namespace Nexify.Presentation;

/// <summary>
/// The player: transport bar, Now playing page and queue (spec 3.4, 4.3, 5).
/// </summary>
/// <remarks>
/// Threading: the engine raises its events on a background thread; every handler is posted to the UI thread via
/// <see cref="IUiDispatcher"/> first. The <see cref="PlayQueue"/> and all properties are only touched on the UI thread.
/// </remarks>
public sealed partial class PlayerViewModel : ObservableObject
{
    /// <summary>Spec 3.6: "Previous" restarts the current item once it has played longer than this.</summary>
    public static readonly TimeSpan RestartThreshold = TimeSpan.FromSeconds(3);

    /// <summary>After this many playback failures in a row, playback stops (spec 5).</summary>
    public const int MaxConsecutiveFailures = 3;

    private readonly IPlaybackEngine _engine;
    private readonly PlayQueue _queue;
    private readonly ISettingsService _settings;
    private readonly IRecentFilesService _recent;
    private readonly ILocalizationService _loc;
    private readonly INotificationService _notifications;
    private readonly IUiDispatcher _dispatcher;

    private PlaybackState _state = PlaybackState.Idle;
    private MediaItem? _opened;
    private Guid? _recordedRecent;
    private int _failures;
    private bool _restoring;

    public PlayerViewModel(
        IPlaybackEngine engine,
        PlayQueue queue,
        ISettingsService settings,
        IRecentFilesService recent,
        ILocalizationService localization,
        INotificationService notifications,
        IUiDispatcher dispatcher)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _recent = recent ?? throw new ArgumentNullException(nameof(recent));
        _loc = localization ?? throw new ArgumentNullException(nameof(localization));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

        PlayPauseCommand = new RelayCommand(PlayPause);
        NextCommand = new RelayCommand(Next);
        PreviousCommand = new RelayCommand(Previous);
        ToggleShuffleCommand = new RelayCommand(() => Shuffle = !Shuffle);
        CycleRepeatCommand = new RelayCommand(() => Repeat = Repeat switch
        {
            RepeatMode.Off => RepeatMode.All,
            RepeatMode.All => RepeatMode.One,
            _ => RepeatMode.Off,
        });
        ToggleMuteCommand = new RelayCommand(() => IsMuted = !IsMuted);
        SeekToCommand = new RelayCommand<double>(SeekTo);
        SeekRelativeCommand = new RelayCommand<int>(direction =>
            _engine.SeekRelative(TimeSpan.FromSeconds(Math.Sign(direction) * _settings.Current.SeekStepSeconds)));
        VolumeStepCommand = new RelayCommand<int>(delta => Volume = Math.Clamp(Volume + delta, 0, 100));
        PlayQueueItemCommand = new RelayCommand<QueueItemViewModel>(PlayQueueItem);
        RemoveQueueItemCommand = new RelayCommand<QueueItemViewModel>(RemoveQueueItem);
        PlayNextQueueItemCommand = new RelayCommand<QueueItemViewModel>(PlayNextQueueItem);
        ClearQueueCommand = new RelayCommand(ClearQueue);

        // Ruling R1: the startup volume is the "default volume" setting; mute, shuffle and repeat are restored.
        var s = _settings.Current;
        _restoring = true;
        Volume = Math.Clamp(s.DefaultVolume, 0, 100);
        IsMuted = s.IsMuted;
        Shuffle = s.Shuffle;
        Repeat = s.Repeat;
        _restoring = false;
        _engine.Volume = Volume;
        _engine.IsMuted = IsMuted;
        _queue.Shuffle = Shuffle;
        _queue.Repeat = Repeat;

        _isReady = _engine.IsReady;
        _videoSource = _engine.VideoSurfaceSource;

        _queue.Changed += (_, _) => SyncQueue();
        // The engine drops events of superseded opens, but they can still be waiting in the UI queue when the user
        // opens something else. So each event remembers the item it belongs to and is ignored if that is no longer
        // the item this view model opened.
        _engine.StateChanged += (_, state) =>
        {
            var id = _engine.Current?.Id;
            _dispatcher.Post(() =>
            {
                if (IsCurrentOpen(id)) OnStateChanged(state);
            });
        };
        _engine.PositionChanged += (_, position) => _dispatcher.Post(() => SetPosition(position.TotalSeconds));
        _engine.DurationChanged += (_, duration) => _dispatcher.Post(() => SetDuration(duration.TotalSeconds));
        _engine.EndReached += (_, _) =>
        {
            var id = _engine.Current?.Id;
            _dispatcher.Post(() =>
            {
                if (IsCurrentOpen(id)) OnEndReached();
            });
        };
        _engine.PlaybackFailed += (_, error) => _dispatcher.Post(() =>
        {
            if (error.Item is null || IsCurrentOpen(error.Item.Id)) OnPlaybackFailed(error);
        });
        _engine.MetadataChanged += (_, _) => _dispatcher.Post(UpdateCurrent);

        SyncQueue();
    }

    // ------------------------------------------------------------------ properties

    public ObservableCollection<QueueItemViewModel> QueueItems { get; } = [];

    [ObservableProperty]
    private bool _hasCurrent;

    [ObservableProperty]
    private string _currentTitle = string.Empty;

    [ObservableProperty]
    private string _currentSubtitle = string.Empty;

    /// <summary>True while playing or opening (the play button then shows "pause").</summary>
    [ObservableProperty]
    private bool _isPlaying;

    /// <summary>False until LibVLC has loaded; the transport controls are disabled meanwhile (spec 4.1).</summary>
    [ObservableProperty]
    private bool _isReady;

    /// <summary>True while LibVLC is loading (subtle loading indicator in the transport bar).</summary>
    [ObservableProperty]
    private bool _isEngineLoading;

    [ObservableProperty]
    private bool _hasVideo;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionText))]
    private double _positionSeconds;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DurationText))]
    private double _durationSeconds;

    public string PositionText => FormatTime(PositionSeconds);
    public string DurationText => FormatTime(DurationSeconds);

    /// <summary>0-100.</summary>
    [ObservableProperty]
    private int _volume;

    [ObservableProperty]
    private bool _isMuted;

    [ObservableProperty]
    private bool _shuffle;

    [ObservableProperty]
    private RepeatMode _repeat;

    /// <summary>The engine's native player for the video view; null until the engine is ready.</summary>
    [ObservableProperty]
    private object? _videoSource;

    // ------------------------------------------------------------------ commands

    public RelayCommand PlayPauseCommand { get; }
    public RelayCommand NextCommand { get; }
    public RelayCommand PreviousCommand { get; }
    public RelayCommand ToggleShuffleCommand { get; }
    public RelayCommand CycleRepeatCommand { get; }
    public RelayCommand ToggleMuteCommand { get; }
    public RelayCommand<double> SeekToCommand { get; }

    /// <summary>Parameter: +1 forward, -1 back, by the seek step from the settings.</summary>
    public RelayCommand<int> SeekRelativeCommand { get; }

    /// <summary>Parameter: volume change in points, e.g. +5 / -5.</summary>
    public RelayCommand<int> VolumeStepCommand { get; }

    public RelayCommand<QueueItemViewModel> PlayQueueItemCommand { get; }
    public RelayCommand<QueueItemViewModel> RemoveQueueItemCommand { get; }
    public RelayCommand<QueueItemViewModel> PlayNextQueueItemCommand { get; }
    public RelayCommand ClearQueueCommand { get; }

    // ------------------------------------------------------------------ public operations

    /// <summary><c>m:ss</c>, or <c>h:mm:ss</c> from one hour.</summary>
    public static string FormatTime(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) seconds = 0;
        var t = TimeSpan.FromSeconds(Math.Floor(seconds));
        return t.TotalHours >= 1
            ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds)
            : string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", t.Minutes, t.Seconds);
    }

    /// <summary>Loads the engine in the background, then enables the controls (spec 4.1 step 2).</summary>
    public async Task InitializeEngineAsync()
    {
        IsEngineLoading = !_engine.IsReady;
        await _engine.InitializeAsync().ConfigureAwait(false);
        _dispatcher.Post(() =>
        {
            IsEngineLoading = false;
            IsReady = _engine.IsReady;
            VideoSource = _engine.VideoSurfaceSource;
            _engine.Volume = Volume;
            _engine.IsMuted = IsMuted;
        });
    }

    /// <summary>
    /// Replace: the queue becomes <paramref name="items"/> and the first one plays.
    /// Append: the items are added; playback only starts (at the first added item) when nothing is playing.
    /// </summary>
    public Task PlayItemsAsync(IReadOnlyList<MediaItem> items, OpenBehavior behavior)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0) return Task.CompletedTask;
        _failures = 0;

        if (behavior == OpenBehavior.Replace)
        {
            _queue.ReplaceWith(items);
            return _queue.Current is { } first ? OpenAsync(first) : Task.CompletedTask;
        }

        var busy = IsBusy && _queue.Current is not null;
        var firstIndex = _queue.Items.Count;
        _queue.AddRange(items);
        if (busy) return Task.CompletedTask;
        return _queue.MoveTo(firstIndex) is { } item ? OpenAsync(item) : Task.CompletedTask;
    }

    /// <summary>Moves a queue row (drag and drop); indexes in the visible list.</summary>
    public void MoveQueueItem(int from, int to)
    {
        if (from < 0 || from >= _queue.Items.Count || to < 0 || to >= _queue.Items.Count || from == to) return;
        _queue.Move(from, to);
    }

    // ------------------------------------------------------------------ commands implementation

    /// <summary>Opening, playing or paused: an item is loaded and has not ended, stopped or failed.</summary>
    private bool IsBusy => _state is PlaybackState.Opening or PlaybackState.Playing or PlaybackState.Paused;

    private void PlayPause()
    {
        var current = _queue.Current ?? _queue.MoveTo(0);
        if (current is null) return;

        // Re-open when the engine holds something else (a removed item), nothing, or a failed item.
        if (_opened?.Id != current.Id || _engine.Current?.Id != current.Id || _state == PlaybackState.Error)
        {
            _failures = 0;
            Open(current);
            return;
        }

        _engine.TogglePlayPause();
    }

    private void Next()
    {
        _failures = 0;
        if (_queue.Next() is { } item) Open(item);
    }

    private void Previous()
    {
        if (_queue.Current is null) return;
        if (_engine.Position > RestartThreshold)
        {
            SeekTo(0);
            return;
        }

        _failures = 0;
        if (_queue.Previous() is { } item) Open(item);
        else SeekTo(0);
    }

    private void SeekTo(double seconds)
    {
        if (!double.IsFinite(seconds)) return;
        seconds = Math.Max(0, seconds);
        _engine.Seek(TimeSpan.FromSeconds(seconds));
        PositionSeconds = seconds;
    }

    private void PlayQueueItem(QueueItemViewModel? row)
    {
        if (row is null) return;
        var index = IndexOf(row.Item);
        if (index < 0) return;
        _failures = 0;
        if (_queue.MoveTo(index) is { } item) Open(item);
    }

    private void RemoveQueueItem(QueueItemViewModel? row)
    {
        if (row is null || IndexOf(row.Item) < 0) return;
        var wasCurrent = _queue.Current?.Id == row.Item.Id;
        var busy = IsBusy;
        // Spec 4.3: continue with the item that followed in play order. When the removed item was the last one,
        // that is the first item with Repeat.All, and nothing (stop) otherwise.
        var wasLast = _queue.IsLastInOrder;
        var wrap = wasLast && _queue.Repeat == RepeatMode.All;

        _queue.Remove(row.Item);
        if (!wasCurrent) return;

        // The engine still holds the removed file: never resume it.
        _opened = null;
        var next = _queue.Items.Count == 0 ? null : wrap ? _queue.Next() : wasLast ? null : _queue.Current;
        if (next is null)
        {
            StopPlayback();
            UpdateCurrent();
        }
        else if (busy)
        {
            Open(next);
        }
    }

    private void PlayNextQueueItem(QueueItemViewModel? row)
    {
        if (row is null || IndexOf(row.Item) < 0 || _queue.Current?.Id == row.Item.Id) return;
        if (_queue.Current is null)
        {
            // Nothing current: "next" is the first position.
            MoveQueueItem(IndexOf(row.Item), 0);
            return;
        }

        // Remove + InsertNext also puts it next in the play order when shuffle is on.
        _queue.Remove(row.Item);
        _queue.InsertNext(row.Item);
    }

    private void ClearQueue()
    {
        _queue.Clear();
        StopPlayback();
        _opened = null;
        UpdateCurrent();
    }

    // ------------------------------------------------------------------ engine events (UI thread)

    private void OnStateChanged(PlaybackState state)
    {
        _state = state;
        IsPlaying = state is PlaybackState.Playing or PlaybackState.Opening;
        if (state != PlaybackState.Playing) return;

        _failures = 0;
        if (_opened is { } item && _recordedRecent != item.Id)
        {
            _recordedRecent = item.Id;
            _recent.Add(item);
        }
    }

    private void OnEndReached()
    {
        // Only an item that is actually playing can end (not one that was just re-opened or stopped).
        if (_state is not (PlaybackState.Playing or PlaybackState.Paused or PlaybackState.Ended)) return;
        _state = PlaybackState.Ended;
        if (_queue.NextForEndReached() is { } item)
        {
            Open(item);
        }
        else
        {
            // Repeat off at the end of the queue: stop, the last item stays current.
            StopPlayback();
        }
    }

    private void OnPlaybackFailed(PlaybackError error)
    {
        if (error.Reason == PlaybackErrorReason.EngineUnavailable)
        {
            // No point in trying the next item: every one would fail the same way.
            _state = PlaybackState.Error;
            IsPlaying = false;
            IsReady = _engine.IsReady;
            _notifications.Show(_loc.Format("error.vlcMissing", error.Item is { } i ? MediaText.FileName(i.Path) : string.Empty), NotificationKind.Error);
            return;
        }

        _state = PlaybackState.Error;
        IsPlaying = false;
        _failures++;
        if (_failures >= MaxConsecutiveFailures)
        {
            _failures = 0;
            StopAfterFailure();
            _notifications.Show(_loc["error.multipleFailed"], NotificationKind.Error);
            return;
        }

        var name = error.Item is { } item ? MediaText.FileName(item.Path) : string.Empty;
        var key = error.Reason switch
        {
            PlaybackErrorReason.FileNotFound => "error.fileNotFound",
            PlaybackErrorReason.AccessDenied => "error.accessDenied",
            _ => "error.unsupported",
        };
        _notifications.Show(_loc.Format(key, name), NotificationKind.Error);

        // Skip to the next item (not Repeat.One's replay: that would fail again).
        if (_queue.Next() is { } next) Open(next);
        else StopAfterFailure();
    }

    /// <summary>Stops after a failure; the state stays Error so Play opens the current item again.</summary>
    private void StopAfterFailure()
    {
        StopPlayback();
        _state = PlaybackState.Error;
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>True when an engine event for item <paramref name="id"/> belongs to the item opened last.</summary>
    private bool IsCurrentOpen(Guid? id) => id is not null && id == _opened?.Id;

    private void Open(MediaItem item)
    {
        _ = OpenAsync(item);
    }

    private async Task OpenAsync(MediaItem item)
    {
        _opened = item;
        _state = PlaybackState.Opening;
        IsPlaying = true;
        PositionSeconds = 0;
        DurationSeconds = 0;
        UpdateCurrent();
        try
        {
            await _engine.OpenAsync(item).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The engine reports open failures through PlaybackFailed; anything else is unexpected.
            _notifications.Show(_loc["error.unexpected"], NotificationKind.Error);
        }
    }

    private void StopPlayback()
    {
        _engine.Stop();
        _state = PlaybackState.Stopped;
        IsPlaying = false;
        PositionSeconds = 0;
    }

    private void SetPosition(double seconds) => PositionSeconds = Math.Max(0, seconds);

    private void SetDuration(double seconds) => DurationSeconds = Math.Max(0, seconds);

    private int IndexOf(MediaItem item)
    {
        var items = _queue.Items;
        for (var i = 0; i < items.Count; i++)
            if (items[i].Id == item.Id) return i;
        return -1;
    }

    /// <summary>Brings <see cref="QueueItems"/> in line with the queue with as few collection changes as possible
    /// (rows are reused by item id, so selection and scroll position survive a reorder).</summary>
    private void SyncQueue()
    {
        var items = _queue.Items;
        var existing = QueueItems.ToDictionary(q => q.Item.Id);
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (i < QueueItems.Count && QueueItems[i].Item.Id == item.Id) continue;

            var at = -1;
            for (var j = i + 1; j < QueueItems.Count; j++)
            {
                if (QueueItems[j].Item.Id == item.Id)
                {
                    at = j;
                    break;
                }
            }

            if (at >= 0) QueueItems.Move(at, i);
            else QueueItems.Insert(i, existing.TryGetValue(item.Id, out var row) ? row : new QueueItemViewModel(item));
        }

        while (QueueItems.Count > items.Count) QueueItems.RemoveAt(QueueItems.Count - 1);
        UpdateCurrent();
    }

    /// <summary>Title, subtitle, video flag and the current row from the queue's current item and the metadata.</summary>
    private void UpdateCurrent()
    {
        var current = _queue.Current;
        var currentId = current?.Id;
        foreach (var row in QueueItems) row.IsCurrent = row.Item.Id == currentId;

        HasCurrent = current is not null;
        HasVideo = current?.Kind == MediaKind.Video;
        if (current is null)
        {
            CurrentTitle = string.Empty;
            CurrentSubtitle = string.Empty;
            return;
        }

        var hasMetadata = _engine.Current?.Id == current.Id;
        var title = hasMetadata && !string.IsNullOrWhiteSpace(_engine.CurrentTitle) ? _engine.CurrentTitle! : current.DisplayName;
        var artist = hasMetadata && !string.IsNullOrWhiteSpace(_engine.CurrentArtist) ? _engine.CurrentArtist! : MediaText.FolderName(current.Path);
        CurrentTitle = title;
        CurrentSubtitle = artist;
        if (QueueItems.FirstOrDefault(q => q.Item.Id == currentId) is { } currentRow) currentRow.Title = title;
    }

    // ------------------------------------------------------------------ persisted settings

    partial void OnVolumeChanged(int value)
    {
        var v = Math.Clamp(value, 0, 100);
        if (v != value)
        {
            Volume = v;
            return;
        }

        if (_restoring) return;
        _engine.Volume = v;
        _settings.UpdateDebounced(s => s with { Volume = v });
    }

    partial void OnIsMutedChanged(bool value)
    {
        if (_restoring) return;
        _engine.IsMuted = value;
        _settings.UpdateDebounced(s => s with { IsMuted = value });
    }

    partial void OnShuffleChanged(bool value)
    {
        if (_restoring) return;
        _queue.Shuffle = value;
        _settings.UpdateDebounced(s => s with { Shuffle = value });
    }

    partial void OnRepeatChanged(RepeatMode value)
    {
        if (_restoring) return;
        _queue.Repeat = value;
        _settings.UpdateDebounced(s => s with { Repeat = value });
    }
}
