using Nexify.Core.Media;
using Nexify.Core.Playback;
using Nexify.Core.Queue;
using Nexify.Core.Settings;
using Xunit;

namespace Nexify.Presentation.Tests;

public class PlayerViewModelTests
{
    private readonly FakePlaybackEngine _engine = new();
    private readonly PlayQueue _queue = new(new Random(1));
    private readonly FakeSettingsService _settings;
    private readonly FakeRecentFiles _recent = new();
    private readonly RecordingNotifications _notifications = new();
    private readonly PlayerViewModel _vm;

    public PlayerViewModelTests() : this(new AppSettings())
    {
    }

    private PlayerViewModelTests(AppSettings settings)
    {
        _settings = new FakeSettingsService(settings);
        _vm = Create();
    }

    private PlayerViewModel Create()
        => new(_engine, _queue, _settings, _recent, new FakeLocalization(), _notifications, new SyncDispatcher());

    private static MediaItem Audio(string name) => new($@"C:\Music\Album\{name}.mp3", MediaKind.Audio);
    private static MediaItem Video(string name) => new($@"C:\Videos\{name}.mp4", MediaKind.Video);

    private static readonly MediaItem A = Audio("a"), B = Audio("b"), C = Audio("c");

    private async Task StartPlaying(params MediaItem[] items)
    {
        await _vm.PlayItemsAsync(items, OpenBehavior.Replace);
        _engine.RaiseState(PlaybackState.Playing);
    }

    [Fact]
    public async Task PlayItems_Replace_StartsFirst()
    {
        _queue.AddRange([Audio("old")]);
        await _vm.PlayItemsAsync([A, B, C], OpenBehavior.Replace);

        Assert.Equal([A, B, C], _queue.Items);
        Assert.Same(A, _queue.Current);
        Assert.Same(A, Assert.Single(_engine.Opened));
        Assert.Equal(3, _vm.QueueItems.Count);
        Assert.True(_vm.QueueItems[0].IsCurrent);
        Assert.Equal("a", _vm.CurrentTitle);
        Assert.Equal("Album", _vm.CurrentSubtitle);
    }

    [Fact]
    public async Task PlayItems_Append_WhilePlaying_DoesNotInterrupt()
    {
        await StartPlaying(A);
        await _vm.PlayItemsAsync([B, C], OpenBehavior.Append);

        Assert.Equal([A, B, C], _queue.Items);
        Assert.Same(A, _queue.Current);
        Assert.Same(A, Assert.Single(_engine.Opened));
    }

    [Fact]
    public async Task PlayItems_Append_WhenNothingPlays_StartsFirstAppended()
    {
        await _vm.PlayItemsAsync([A, B], OpenBehavior.Append);
        Assert.Same(A, Assert.Single(_engine.Opened));

        _engine.RaiseEndReached(); // -> B
        _engine.RaiseEndReached(); // last item, repeat off -> stops
        await _vm.PlayItemsAsync([C], OpenBehavior.Append);
        Assert.Same(C, _engine.Opened[^1]);
        Assert.Same(C, _queue.Current);
    }

    [Fact]
    public async Task Previous_After3s_RestartsCurrent()
    {
        await StartPlaying(A, B);
        _vm.NextCommand.Execute(null);
        _engine.RaiseState(PlaybackState.Playing);
        _engine.RaisePosition(TimeSpan.FromSeconds(3.5));

        _vm.PreviousCommand.Execute(null);

        Assert.Equal(TimeSpan.Zero, Assert.Single(_engine.Seeks));
        Assert.Same(B, _queue.Current);
        Assert.Equal(2, _engine.Opened.Count);
    }

    [Fact]
    public async Task Previous_Within3s_GoesToPrevious()
    {
        await StartPlaying(A, B);
        _vm.NextCommand.Execute(null);
        _engine.RaisePosition(TimeSpan.FromSeconds(2));

        _vm.PreviousCommand.Execute(null);

        Assert.Same(A, _queue.Current);
        Assert.Same(A, _engine.Opened[^1]);
        Assert.Empty(_engine.Seeks);
    }

    [Fact]
    public async Task Previous_AtFirstItem_RestartsCurrent()
    {
        await StartPlaying(A, B);
        _engine.RaisePosition(TimeSpan.FromSeconds(1));

        _vm.PreviousCommand.Execute(null);

        Assert.Same(A, _queue.Current);
        Assert.Equal(TimeSpan.Zero, Assert.Single(_engine.Seeks));
    }

    [Fact]
    public async Task EndReached_PlaysNext()
    {
        await StartPlaying(A, B);
        _engine.RaiseEndReached();

        Assert.Same(B, _queue.Current);
        Assert.Same(B, _engine.Opened[^1]);
        Assert.Equal("b", _vm.CurrentTitle);
    }

    [Fact]
    public async Task EndReached_LastItemRepeatOff_Stops()
    {
        await StartPlaying(A, B);
        _engine.RaiseEndReached();
        _engine.RaiseEndReached();

        Assert.Equal(2, _engine.Opened.Count);
        Assert.Equal(1, _engine.StopCalls);
        Assert.Same(B, _queue.Current);
        Assert.Equal("b", _vm.CurrentTitle);
    }

    [Fact]
    public async Task EndReached_RepeatOne_ReplaysSame()
    {
        await StartPlaying(A, B);
        _vm.CycleRepeatCommand.Execute(null); // All
        _vm.CycleRepeatCommand.Execute(null); // One
        _engine.RaiseEndReached();

        Assert.Same(A, _queue.Current);
        Assert.Equal([A, A], _engine.Opened);
    }

    [Fact]
    public async Task PlaybackFailed_ShowsMessage_AndSkips()
    {
        await _vm.PlayItemsAsync([A, B], OpenBehavior.Replace);
        _engine.RaiseFailed(PlaybackErrorReason.FileNotFound);

        var (message, kind) = Assert.Single(_notifications.Shown);
        Assert.Equal("error.fileNotFound|a.mp3", message);
        Assert.Equal(NotificationKind.Error, kind);
        Assert.Same(B, _queue.Current);
        Assert.Same(B, _engine.Opened[^1]);
    }

    [Theory]
    [InlineData(PlaybackErrorReason.AccessDenied, "error.accessDenied|a.mp3")]
    [InlineData(PlaybackErrorReason.UnsupportedOrCorrupt, "error.unsupported|a.mp3")]
    [InlineData(PlaybackErrorReason.Unknown, "error.unsupported|a.mp3")]
    public async Task PlaybackFailed_MessagePerReason(PlaybackErrorReason reason, string expected)
    {
        await _vm.PlayItemsAsync([A, B], OpenBehavior.Replace);
        _engine.RaiseFailed(reason);
        Assert.Equal(expected, Assert.Single(_notifications.Shown).Message);
    }

    [Fact]
    public async Task EngineUnavailable_ShowsVlcMissing_AndDoesNotSkip()
    {
        await _vm.PlayItemsAsync([A, B], OpenBehavior.Replace);
        _engine.RaiseFailed(PlaybackErrorReason.EngineUnavailable);

        Assert.StartsWith("error.vlcMissing", Assert.Single(_notifications.Shown).Message);
        Assert.Same(A, Assert.Single(_engine.Opened));
    }

    [Fact]
    public async Task ThreeConsecutiveFailures_StopsWithMultipleFailedMessage()
    {
        var d = Audio("d");
        await _vm.PlayItemsAsync([A, B, C, d], OpenBehavior.Replace);
        _engine.RaiseFailed(PlaybackErrorReason.FileNotFound); // A -> B
        _engine.RaiseFailed(PlaybackErrorReason.FileNotFound); // B -> C
        _engine.RaiseFailed(PlaybackErrorReason.FileNotFound); // C -> stop

        Assert.Equal([A, B, C], _engine.Opened);
        Assert.Equal(1, _engine.StopCalls);
        Assert.Equal("error.multipleFailed", _notifications.Shown[^1].Message);
        Assert.Equal(3, _notifications.Shown.Count);
    }

    [Fact]
    public async Task SuccessfulPlay_ResetsFailureCounter()
    {
        var d = Audio("d");
        var e = Audio("e");
        await _vm.PlayItemsAsync([A, B, C, d, e], OpenBehavior.Replace);
        _engine.RaiseFailed(PlaybackErrorReason.FileNotFound); // A -> B
        _engine.RaiseFailed(PlaybackErrorReason.FileNotFound); // B -> C
        _engine.RaiseState(PlaybackState.Playing);             // C plays
        _engine.RaiseEndReached();                             // C -> d
        _engine.RaiseFailed(PlaybackErrorReason.FileNotFound); // d -> e

        Assert.Same(e, _engine.Opened[^1]);
        Assert.Equal(0, _engine.StopCalls);
        Assert.DoesNotContain(_notifications.Shown, n => n.Message == "error.multipleFailed");
    }

    [Fact]
    public async Task Playing_AddsToRecent()
    {
        await StartPlaying(A, B);
        Assert.Same(A, Assert.Single(_recent.Added));

        _engine.RaiseState(PlaybackState.Paused);
        _engine.RaiseState(PlaybackState.Playing); // same item resumed: not added again
        Assert.Single(_recent.Added);

        _engine.RaiseEndReached();
        _engine.RaiseState(PlaybackState.Playing);
        Assert.Equal([A, B], _recent.Added);
    }

    [Fact]
    public void VolumeChange_PersistsDebounced()
    {
        _vm.Volume = 42;

        Assert.Equal(42, _engine.Volume);
        Assert.Equal(42, _settings.Current.Volume);
        Assert.True(_settings.DebouncedUpdates >= 1);
        Assert.Equal(0, _settings.ImmediateUpdates);

        _vm.ToggleMuteCommand.Execute(null);
        Assert.True(_engine.IsMuted);
        Assert.True(_settings.Current.IsMuted);

        _vm.ToggleShuffleCommand.Execute(null);
        Assert.True(_queue.Shuffle);
        Assert.True(_settings.Current.Shuffle);
        Assert.Equal(0, _settings.ImmediateUpdates);
    }

    [Fact]
    public void Startup_UsesDefaultVolume_AndRestoresMuteShuffleRepeat()
    {
        _settings.Update(_ => new AppSettings { Volume = 30, DefaultVolume = 65, IsMuted = true, Shuffle = true, Repeat = RepeatMode.All });
        var updatesBefore = _settings.DebouncedUpdates + _settings.ImmediateUpdates;

        var vm = Create();

        Assert.Equal(65, vm.Volume);
        Assert.Equal(65, _engine.Volume);
        Assert.True(vm.IsMuted);
        Assert.True(_engine.IsMuted);
        Assert.True(vm.Shuffle);
        Assert.True(_queue.Shuffle);
        Assert.Equal(RepeatMode.All, vm.Repeat);
        Assert.Equal(RepeatMode.All, _queue.Repeat);
        Assert.Equal(updatesBefore, _settings.DebouncedUpdates + _settings.ImmediateUpdates);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(30)]
    public void SeekRelative_UsesSettingsStep(int step)
    {
        _settings.Update(s => s with { SeekStepSeconds = step });

        _vm.SeekRelativeCommand.Execute(1);
        _vm.SeekRelativeCommand.Execute(-1);

        Assert.Equal([TimeSpan.FromSeconds(step), TimeSpan.FromSeconds(-step)], _engine.RelativeSeeks);
    }

    [Fact]
    public void SeekTo_SeeksEngine()
    {
        _vm.SeekToCommand.Execute(12.5);
        Assert.Equal(TimeSpan.FromSeconds(12.5), Assert.Single(_engine.Seeks));
    }

    [Fact]
    public void CycleRepeat_OffAllOneOff()
    {
        Assert.Equal(RepeatMode.Off, _vm.Repeat);
        _vm.CycleRepeatCommand.Execute(null);
        Assert.Equal(RepeatMode.All, _vm.Repeat);
        Assert.Equal(RepeatMode.All, _queue.Repeat);
        _vm.CycleRepeatCommand.Execute(null);
        Assert.Equal(RepeatMode.One, _vm.Repeat);
        _vm.CycleRepeatCommand.Execute(null);
        Assert.Equal(RepeatMode.Off, _vm.Repeat);
        Assert.Equal(RepeatMode.Off, _settings.Current.Repeat);
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(59, "0:59")]
    [InlineData(61.9, "1:01")]
    [InlineData(3599, "59:59")]
    [InlineData(3725, "1:02:05")]
    public void TimeFormatting(double seconds, string expected)
    {
        Assert.Equal(expected, PlayerViewModel.FormatTime(seconds));
    }

    [Fact]
    public async Task PositionAndDuration_UpdateTexts()
    {
        await StartPlaying(A);
        _engine.RaiseDuration(TimeSpan.FromSeconds(3725));
        _engine.RaisePosition(TimeSpan.FromSeconds(59));

        Assert.Equal(3725, _vm.DurationSeconds);
        Assert.Equal("1:02:05", _vm.DurationText);
        Assert.Equal(59, _vm.PositionSeconds);
        Assert.Equal("0:59", _vm.PositionText);
        Assert.True(_vm.IsPlaying);
    }

    [Fact]
    public async Task RemoveCurrentWhilePlaying_PlaysFollowing()
    {
        await StartPlaying(A, B, C);

        _vm.RemoveQueueItemCommand.Execute(_vm.QueueItems[0]);

        Assert.Same(B, _queue.Current);
        Assert.Same(B, _engine.Opened[^1]);
        Assert.Equal(2, _vm.QueueItems.Count);
    }

    [Fact]
    public async Task RemoveLastRemainingWhilePlaying_Stops()
    {
        await StartPlaying(A);
        _vm.RemoveQueueItemCommand.Execute(_vm.QueueItems[0]);

        Assert.Empty(_queue.Items);
        Assert.Equal(1, _engine.StopCalls);
        Assert.False(_vm.HasCurrent);
    }

    [Fact]
    public async Task RemoveOtherItem_DoesNotInterrupt()
    {
        await StartPlaying(A, B, C);
        _vm.RemoveQueueItemCommand.Execute(_vm.QueueItems[2]);

        Assert.Same(A, Assert.Single(_engine.Opened));
        Assert.Equal([A, B], _queue.Items);
    }

    [Fact]
    public async Task QueueCommands_PlayItem_PlayNext_Move_Clear()
    {
        await StartPlaying(A, B, C);

        _vm.PlayQueueItemCommand.Execute(_vm.QueueItems[2]);
        Assert.Same(C, _queue.Current);
        Assert.Same(C, _engine.Opened[^1]);
        Assert.True(_vm.QueueItems[2].IsCurrent);
        Assert.False(_vm.QueueItems[0].IsCurrent);

        _vm.MoveQueueItem(2, 0);
        Assert.Equal([C, A, B], _queue.Items);
        Assert.Equal([C, A, B], _vm.QueueItems.Select(q => q.Item));

        _vm.PlayNextQueueItemCommand.Execute(_vm.QueueItems[2]); // B after C
        Assert.Equal([C, B, A], _queue.Items);
        Assert.Equal([C, B, A], _vm.QueueItems.Select(q => q.Item));

        _vm.ClearQueueCommand.Execute(null);
        Assert.Empty(_vm.QueueItems);
        Assert.Equal(1, _engine.StopCalls);
        Assert.False(_vm.HasCurrent);
    }

    [Fact]
    public async Task Metadata_TitleAndArtist_OverrideFileNames()
    {
        await StartPlaying(A);
        _engine.CurrentTitle = "Song";
        _engine.CurrentArtist = "Artist";
        _engine.RaiseMetadata();

        Assert.Equal("Song", _vm.CurrentTitle);
        Assert.Equal("Artist", _vm.CurrentSubtitle);
        Assert.Equal("Song", _vm.QueueItems[0].Title);
    }

    [Fact]
    public async Task HasVideo_FollowsCurrentKind()
    {
        var v = Video("clip");
        await StartPlaying(v, A);
        Assert.True(_vm.HasVideo);
        _engine.RaiseEndReached();
        Assert.False(_vm.HasVideo);
    }

    [Fact]
    public async Task PlayPause_TogglesEngine()
    {
        await StartPlaying(A);
        _vm.PlayPauseCommand.Execute(null);
        Assert.Equal(1, _engine.ToggleCalls);
    }

    [Fact]
    public void VolumeStep_Clamps()
    {
        _vm.Volume = 98;
        _vm.VolumeStepCommand.Execute(5);
        Assert.Equal(100, _vm.Volume);
        _vm.Volume = 3;
        _vm.VolumeStepCommand.Execute(-5);
        Assert.Equal(0, _vm.Volume);
    }

    [Fact]
    public async Task InitializeEngine_RefreshesReadyAndVideoSource()
    {
        _engine.IsReady = false;
        var vm = Create();
        Assert.False(vm.IsReady);

        _engine.IsReady = true;
        _engine.VideoSurfaceSource = new object();
        await vm.InitializeEngineAsync();

        Assert.True(vm.IsReady);
        Assert.False(vm.IsEngineLoading);
        Assert.Same(_engine.VideoSurfaceSource, vm.VideoSource);
    }
}
