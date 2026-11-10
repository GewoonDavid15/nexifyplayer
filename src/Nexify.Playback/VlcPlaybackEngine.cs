using LibVLCSharp.Shared;
using Nexify.Core.Media;
using Nexify.Core.Playback;
using Serilog;
using VlcCore = LibVLCSharp.Shared.Core;

namespace Nexify.Playback;

/// <summary>
/// <see cref="IPlaybackEngine"/> on top of LibVLC. One <see cref="LibVLC"/> and one <see cref="MediaPlayer"/>.
/// </summary>
/// <remarks>
/// Threading rules:
/// <list type="bullet">
/// <item>LibVLC raises its events on its own thread. The callbacks here only read the event arguments
/// and queue the real work; LibVLC is never called from inside a LibVLC callback.</item>
/// <item>All public events are raised from one serial queue on the thread pool, in order, never while
/// an internal lock is held. Handlers may call back into the engine (e.g. <see cref="OpenAsync"/>
/// from <see cref="EndReached"/>).</item>
/// <item>Open/stop/dispose operations on the native player are serialized by <see cref="_gate"/>.
/// Every <see cref="OpenAsync"/> takes a new generation number; an open that is superseded before it
/// reaches the player does nothing, and events belonging to an older generation are ignored. So when
/// OpenAsync is called rapidly the last call wins.</item>
/// </list>
/// </remarks>
public sealed class VlcPlaybackEngine : IPlaybackEngine
{
    private static readonly TimeSpan PositionEventInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan DisposeTimeout = TimeSpan.FromSeconds(5);

    private readonly ILogger _log;
    private readonly string[] _extraVlcOptions;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _eventLock = new();
    private readonly object _initLock = new();

    private Task _eventTail = Task.CompletedTask;
    private Task? _initTask;
    private LibVLC? _libVlc;
    private MediaPlayer? _player;
    private Media? _media;
    private Action? _detachMedia;

    // Generation of the most recent OpenAsync call (also bumped by Dispose).
    private long _openGeneration;
    // Generation of the media currently loaded in the native player.
    private long _playerGeneration;

    private volatile bool _ready;
    private volatile bool _disposed;
    private volatile PlaybackState _state = PlaybackState.Idle;
    private volatile MediaItem? _current;
    private volatile string? _title;
    private volatile string? _artist;
    private long _positionMs;
    private long _durationMs;
    private long _lastPositionEventTicks;
    private bool _sawTime;
    private bool _timeReliable;
    private float _lastFraction;
    private int _volume = 100;
    private volatile bool _muted;

    public VlcPlaybackEngine(ILogger log, string[]? extraVlcOptions = null)
    {
        _log = log.ForContext<VlcPlaybackEngine>();
        _extraVlcOptions = extraVlcOptions ?? [];
    }

    public bool IsReady => _ready;
    public PlaybackState State => _state;
    public MediaItem? Current => _current;
    public MediaKind? CurrentKind => _current?.Kind;
    public string? CurrentTitle => _title;
    public string? CurrentArtist => _artist;
    public TimeSpan Position => TimeSpan.FromMilliseconds(Interlocked.Read(ref _positionMs));
    public TimeSpan Duration => TimeSpan.FromMilliseconds(Interlocked.Read(ref _durationMs));
    public object? VideoSurfaceSource => _player;

    public event EventHandler<PlaybackState>? StateChanged;
    public event EventHandler<TimeSpan>? PositionChanged;
    public event EventHandler<TimeSpan>? DurationChanged;
    public event EventHandler? EndReached;
    public event EventHandler<PlaybackError>? PlaybackFailed;
    public event EventHandler? MetadataChanged;

    public int Volume
    {
        get => Volatile.Read(ref _volume);
        set
        {
            var v = Math.Clamp(value, 0, 100);
            Volatile.Write(ref _volume, v);
            TryPlayer(p => ApplyVolume(p, v), "set volume");
        }
    }

    public bool IsMuted
    {
        get => _muted;
        set
        {
            _muted = value;
            TryPlayer(p => p.Mute = value, "set mute");
        }
    }

    /// <summary>The volume most recently pushed to the native player, after it started playing
    /// (for tests: with <c>--aout=dummy</c> LibVLC cannot report its volume back).</summary>
    internal int? AppliedVolumeWhilePlaying { get; private set; }

    private void ApplyVolume(MediaPlayer player, int volume)
    {
        player.Volume = volume;
        if (_state == PlaybackState.Playing) AppliedVolumeWhilePlaying = volume;
    }

    // ---------------------------------------------------------------- initialization

    public Task InitializeAsync()
    {
        lock (_initLock)
            return _initTask ??= Task.Run(InitializeCore);
    }

    private void InitializeCore()
    {
        try
        {
            VlcCore.Initialize();
            string[] options = ["--no-video-title-show", .. _extraVlcOptions];
            var libVlc = new LibVLC(options);
            var player = new MediaPlayer(libVlc);
            player.Playing += OnVlcPlaying;
            player.Paused += OnVlcPaused;
            player.Stopped += OnVlcStopped;
            player.EndReached += OnVlcEndReached;
            player.EncounteredError += OnVlcError;
            player.TimeChanged += OnVlcTimeChanged;
            player.PositionChanged += OnVlcPositionChanged;
            player.LengthChanged += OnVlcLengthChanged;

            lock (_initLock)
            {
                if (_disposed)
                {
                    player.Dispose();
                    libVlc.Dispose();
                    return;
                }
                _libVlc = libVlc;
                _player = player;
                _ready = true;
            }
            _log.Information("LibVLC {Version} loaded (options: {Options})", libVlc.Version, options);
        }
        catch (Exception ex)
        {
            _ready = false;
            _log.Error(ex, "LibVLC could not be loaded");
            Raise(() => PlaybackFailed?.Invoke(this, new PlaybackError(null, PlaybackErrorReason.EngineUnavailable, ex.Message)));
        }
    }

    // ---------------------------------------------------------------- open

    public async Task OpenAsync(MediaItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (_disposed) return;
        var gen = Interlocked.Increment(ref _openGeneration);

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!IsLatest(gen)) return; // superseded by a newer OpenAsync
            // Leave the caller's thread (it may be the UI thread or one of our event handlers).
            await Task.Run(() => OpenCore(gen, item)).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void OpenCore(long gen, MediaItem item)
    {
        _current = item;
        _title = null;
        _artist = null;
        Interlocked.Exchange(ref _positionMs, 0);
        Interlocked.Exchange(ref _durationMs, 0);
        Volatile.Write(ref _sawTime, false);
        Volatile.Write(ref _timeReliable, false);
        Volatile.Write(ref _lastFraction, 0f);
        SetState(gen, PlaybackState.Opening);
        Raise(() =>
        {
            PositionChanged?.Invoke(this, TimeSpan.Zero);
            DurationChanged?.Invoke(this, TimeSpan.Zero);
            MetadataChanged?.Invoke(this, EventArgs.Empty);
        });

        // 1. File-system check first: no LibVLC involved.
        var reason = PlaybackErrorClassifier.ClassifyBeforeOpen(item.Path);
        if (reason != PlaybackErrorReason.Unknown)
        {
            _log.Warning("Cannot open {Path}: {Reason}", item.Path, reason);
            Fail(gen, item, reason, null);
            StopPreviousMedia();
            return;
        }

        var player = _player;
        if (!_ready || player is null || _libVlc is null)
        {
            Fail(gen, item, PlaybackErrorReason.EngineUnavailable, "LibVLC is not loaded");
            return;
        }

        try
        {
            // Stop the previous media synchronously; its events still carry the old generation.
            StopPreviousMedia();
            if (!IsLatest(gen)) return;

            // 2. The URI escapes '#', '%', spaces and non-ASCII characters (see FileUri for why not new Uri(path)).
            var media = new Media(_libVlc, FileUri.FromPath(item.Path));
            // Metadata comes from the playing input (MetaChanged) and, if LibVLC reports it, ParsedChanged.
            // No explicit Media.Parse: a preparse still running while the media is released was a crash risk.
            EventHandler<MediaParsedChangedEventArgs> onParsed = (_, _) => Post(gen, () => ReadMetadata(gen, media));
            EventHandler<MediaMetaChangedEventArgs> onMeta = (_, e) =>
            {
                if (e.MetadataType is MetadataType.Title or MetadataType.Artist) Post(gen, () => ReadMetadata(gen, media));
            };
            media.ParsedChanged += onParsed;
            media.MetaChanged += onMeta;
            _media = media;
            _detachMedia = () =>
            {
                media.ParsedChanged -= onParsed;
                media.MetaChanged -= onMeta;
            };

            Volatile.Write(ref _playerGeneration, gen);
            ApplyVolume(player, Volume);
            player.Mute = IsMuted;

            // 3. Play.
            if (!player.Play(media))
            {
                Fail(gen, item, PlaybackErrorReason.UnsupportedOrCorrupt, "MediaPlayer.Play returned false");
                return;
            }
            _log.Information("Opened {Path}", item.Path);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Opening {Path} failed", item.Path);
            Fail(gen, item, PlaybackErrorReason.UnsupportedOrCorrupt, ex.Message);
        }
    }

    /// <summary>Stops and releases the media currently in the player. Caller holds <see cref="_gate"/>
    /// and runs on a thread-pool thread.</summary>
    private void StopPreviousMedia()
    {
        var old = _media;
        if (old is null || _player is null) return;
        try
        {
            _player.Stop();
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Stopping previous media failed");
        }
        ReleaseMedia();
    }

    /// <summary>Detaches our handlers from the current media, then disposes it. Caller holds <see cref="_gate"/>.</summary>
    private void ReleaseMedia()
    {
        var media = _media;
        _media = null;
        try
        {
            _detachMedia?.Invoke();
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "Detaching media events failed");
        }
        _detachMedia = null;
        media?.Dispose();
    }

    /// <summary>Reads title/artist under <see cref="_gate"/>, so the media cannot be released meanwhile.</summary>
    private void ReadMetadata(long gen, Media? expected) => _ = Task.Run(async () =>
    {
        string? title = null, artist = null;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var media = _media;
            if (!IsLatest(gen) || media is null || (expected is not null && !ReferenceEquals(media, expected))) return;
            title = Clean(media.Meta(MetadataType.Title));
            artist = Clean(media.Meta(MetadataType.Artist));
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "Reading metadata failed");
            return;
        }
        finally
        {
            _gate.Release();
        }

        // Without a tag LibVLC reports the file name as title; that is not real metadata.
        var item = _current;
        if (title is not null && item is not null &&
            (string.Equals(title, Path.GetFileName(item.Path), StringComparison.OrdinalIgnoreCase) ||
             string.Equals(title, item.DisplayName, StringComparison.OrdinalIgnoreCase)))
            title = null;

        Post(gen, () =>
        {
            if (title == _title && artist == _artist) return;
            _title = title;
            _artist = artist;
            MetadataChanged?.Invoke(this, EventArgs.Empty);
        });

        static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    });

    // ---------------------------------------------------------------- transport

    public void Play()
    {
        var gen = Volatile.Read(ref _openGeneration);
        RunOnPlayer(gen, p =>
        {
            if (_media is null) return;
            if (_state == PlaybackState.Paused) p.SetPause(false);
            else if (_state != PlaybackState.Playing)
            {
                // Replay after Ended/Stopped starts from the beginning. LibVLC 3 keeps the ended input
                // around and ignores Play() on it, so stop it first.
                if (_state is PlaybackState.Ended or PlaybackState.Error) p.Stop();
                ResetPosition(gen);
                p.Play();
            }
        }, "play");
    }

    public void Pause()
    {
        var gen = Volatile.Read(ref _openGeneration);
        RunOnPlayer(gen, p =>
        {
            if (_media is not null && _state == PlaybackState.Playing) p.SetPause(true);
        }, "pause");
    }

    public void TogglePlayPause()
    {
        if (_state == PlaybackState.Playing) Pause();
        else Play();
    }

    public void Stop()
    {
        var gen = Volatile.Read(ref _openGeneration);
        RunOnPlayer(gen, p =>
        {
            if (_media is null) return;
            p.Stop();
            SetState(gen, PlaybackState.Stopped);
            ResetPosition(gen);
        }, "stop");
    }

    public void Seek(TimeSpan position)
    {
        var duration = Duration;
        var ms = (long)Math.Max(0, position.TotalMilliseconds);
        if (duration > TimeSpan.Zero) ms = Math.Min(ms, (long)duration.TotalMilliseconds);
        var gen = Volatile.Read(ref _openGeneration);
        var applied = false;
        TryPlayer(p =>
        {
            if (_media is null) return;
            p.Time = ms;
            applied = true;
        }, "seek");
        if (!applied || !IsLatest(gen)) return;

        // LibVLC reports no time while paused, so publish the target right away (unthrottled). Mark the time
        // reliable and align the fraction so the Ogg workaround neither ignores nor overrides what follows.
        Interlocked.Exchange(ref _positionMs, ms);
        var durationMs = Interlocked.Read(ref _durationMs);
        Volatile.Write(ref _lastFraction, durationMs > 0 ? (float)ms / durationMs : 0f);
        Volatile.Write(ref _timeReliable, true);
        Post(gen, () => PositionChanged?.Invoke(this, Position));
    }

    /// <summary>Position back to 0 (stop, replay), with the Ogg-workaround state reset.</summary>
    private void ResetPosition(long gen)
    {
        Interlocked.Exchange(ref _positionMs, 0);
        Volatile.Write(ref _lastFraction, 0f);
        Volatile.Write(ref _timeReliable, false);
        Post(gen, () => PositionChanged?.Invoke(this, TimeSpan.Zero));
    }

    public void SeekRelative(TimeSpan delta) => Seek(Position + delta);

    /// <summary>Runs a state-changing native call serialized with open/stop, off the caller's thread,
    /// unless a newer OpenAsync has been issued since.</summary>
    private void RunOnPlayer(long gen, Action<MediaPlayer> action, string what)
    {
        if (_disposed || _player is null) return;
        _ = Task.Run(async () =>
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!_disposed && IsLatest(gen) && _player is { } p) action(p);
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Player {Action} failed", what);
            }
            finally
            {
                _gate.Release();
            }
        });
    }

    /// <summary>A cheap, non-blocking native call (volume, mute, seek). Never called from a VLC callback.</summary>
    private void TryPlayer(Action<MediaPlayer> action, string what)
    {
        if (_disposed || _player is not { } p) return;
        try
        {
            action(p);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Player {Action} failed", what);
        }
    }

    // ---------------------------------------------------------------- LibVLC callbacks (VLC thread: no LibVLC calls here)

    private long CurrentPlayerGeneration => Volatile.Read(ref _playerGeneration);

    private void OnVlcPlaying(object? sender, EventArgs e)
    {
        var gen = CurrentPlayerGeneration;
        Post(gen, () =>
        {
            SetState(gen, PlaybackState.Playing);
            ReadMetadata(gen, null);
            // Re-apply volume and mute now that the audio output exists (on the thread pool, not the VLC thread).
            RunOnPlayer(gen, p =>
            {
                ApplyVolume(p, Volume);
                p.Mute = IsMuted;
            }, "apply volume");
        });
    }

    private void OnVlcPaused(object? sender, EventArgs e)
    {
        var gen = CurrentPlayerGeneration;
        Post(gen, () => SetState(gen, PlaybackState.Paused));
    }

    private void OnVlcStopped(object? sender, EventArgs e)
    {
        var gen = CurrentPlayerGeneration;
        Post(gen, () =>
        {
            // LibVLC can report Stopped right after EndReached; Ended (and Error) must stay visible.
            if (_state is not (PlaybackState.Error or PlaybackState.Ended)) SetState(gen, PlaybackState.Stopped);
        });
    }

    private void OnVlcEndReached(object? sender, EventArgs e)
    {
        var gen = CurrentPlayerGeneration;
        Post(gen, () =>
        {
            if (_state == PlaybackState.Error) return;
            // A file VLC could not demux or decode sometimes "ends" without an error, having produced
            // neither a length nor any playback time. Treat that as unplayable.
            if (Interlocked.Read(ref _durationMs) <= 0 && !Volatile.Read(ref _sawTime))
            {
                FailFromVlc(gen, "Ended without producing any playable stream");
                return;
            }
            var duration = Interlocked.Read(ref _durationMs);
            if (duration > 0)
            {
                Interlocked.Exchange(ref _positionMs, duration);
                PositionChanged?.Invoke(this, TimeSpan.FromMilliseconds(duration));
            }
            SetState(gen, PlaybackState.Ended);
            EndReached?.Invoke(this, EventArgs.Empty);
        });
    }

    private void OnVlcError(object? sender, EventArgs e)
    {
        var gen = CurrentPlayerGeneration;
        Post(gen, () => FailFromVlc(gen, "LibVLC reported an error"));
    }

    private void OnVlcTimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e)
    {
        var gen = CurrentPlayerGeneration;
        if (!IsLatest(gen)) return;
        var time = Math.Max(0, e.Time);
        if (time > 0)
        {
            Volatile.Write(ref _sawTime, true);
            Volatile.Write(ref _timeReliable, true);
        }
        else if (Volatile.Read(ref _lastFraction) > 0.01f)
        {
            // LibVLC 3.0.21 (seen with Ogg/Vorbis) can report time 0 mid-stream while the position
            // fraction keeps advancing. Ignore it and derive the position from the fraction instead.
            Volatile.Write(ref _timeReliable, false);
            return;
        }
        UpdatePosition(gen, time);
    }

    private void OnVlcPositionChanged(object? sender, MediaPlayerPositionChangedEventArgs e)
    {
        var gen = CurrentPlayerGeneration;
        if (!IsLatest(gen)) return;
        var fraction = Math.Clamp(e.Position, 0f, 1f);
        Volatile.Write(ref _lastFraction, fraction);
        var duration = Interlocked.Read(ref _durationMs);
        if (Volatile.Read(ref _timeReliable) || duration <= 0 || fraction <= 0) return;
        Volatile.Write(ref _sawTime, true);
        UpdatePosition(gen, (long)(fraction * duration));
    }

    private void UpdatePosition(long gen, long ms)
    {
        Interlocked.Exchange(ref _positionMs, ms);

        // Throttle PositionChanged to ~4 per second.
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastPositionEventTicks);
        if (now - last < PositionEventInterval.TotalMilliseconds) return;
        if (Interlocked.CompareExchange(ref _lastPositionEventTicks, now, last) != last) return;
        Post(gen, () => PositionChanged?.Invoke(this, Position));
    }

    private void OnVlcLengthChanged(object? sender, MediaPlayerLengthChangedEventArgs e)
    {
        var gen = CurrentPlayerGeneration;
        if (!IsLatest(gen)) return;
        var length = Math.Max(0, e.Length);
        Interlocked.Exchange(ref _durationMs, length);
        Post(gen, () => DurationChanged?.Invoke(this, TimeSpan.FromMilliseconds(length)));
    }

    // ---------------------------------------------------------------- state and event plumbing

    private bool IsLatest(long gen) => !_disposed && gen == Volatile.Read(ref _openGeneration);

    private void SetState(long gen, PlaybackState state)
    {
        if (!IsLatest(gen) || _state == state) return;
        _log.Debug("State {From} -> {To} (generation {Generation}, position {Position})", _state, state, gen, Position);
        _state = state;
        Raise(() => StateChanged?.Invoke(this, state));
    }

    private void Fail(long gen, MediaItem item, PlaybackErrorReason reason, string? detail)
    {
        if (!IsLatest(gen)) return;
        SetState(gen, PlaybackState.Error);
        Raise(() => PlaybackFailed?.Invoke(this, new PlaybackError(item, reason, detail)));
    }

    private void FailFromVlc(long gen, string detail)
    {
        if (!IsLatest(gen) || _state == PlaybackState.Error || _current is not { } item) return;
        // The file may have disappeared or become unreadable since it was opened.
        var reason = PlaybackErrorClassifier.ClassifyBeforeOpen(item.Path);
        if (reason == PlaybackErrorReason.Unknown) reason = PlaybackErrorReason.UnsupportedOrCorrupt;
        _log.Warning("Playback of {Path} failed: {Reason} ({Detail})", item.Path, reason, detail);
        Fail(gen, item, reason, detail);
    }

    /// <summary>Queues work from a VLC callback onto the serial event queue; dropped if the generation is stale.</summary>
    private void Post(long gen, Action action)
    {
        if (!IsLatest(gen)) return;
        Raise(() =>
        {
            if (IsLatest(gen)) action();
        });
    }

    /// <summary>Runs <paramref name="action"/> on the thread pool after everything queued before it.</summary>
    private void Raise(Action action)
    {
        lock (_eventLock)
        {
            _eventTail = _eventTail.ContinueWith(_ =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    _log.Error(ex, "Playback event handler threw");
                }
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }
    }

    // ---------------------------------------------------------------- dispose

    public void Dispose()
    {
        if (_disposed) return;
        lock (_initLock) _disposed = true;
        Interlocked.Increment(ref _openGeneration); // every pending event is now stale

        var player = _player;
        var libVlc = _libVlc;
        if (player is null && libVlc is null) return;

        // Stop on the thread pool (never on a VLC thread), serialized with any open in progress.
        var cleanup = Task.Run(() =>
        {
            var gotGate = _gate.Wait(DisposeTimeout);
            if (!gotGate)
            {
                // Another thread may still be using the player: leak the native objects rather than
                // disposing them underneath it.
                _log.Warning("Playback engine busy for {Timeout} during Dispose; LibVLC objects are not released", DisposeTimeout);
                return;
            }
            try
            {
                if (player is not null)
                {
                    player.Playing -= OnVlcPlaying;
                    player.Paused -= OnVlcPaused;
                    player.Stopped -= OnVlcStopped;
                    player.EndReached -= OnVlcEndReached;
                    player.EncounteredError -= OnVlcError;
                    player.TimeChanged -= OnVlcTimeChanged;
                    player.PositionChanged -= OnVlcPositionChanged;
                    player.LengthChanged -= OnVlcLengthChanged;
                    player.Stop();
                    player.Dispose();
                }
                ReleaseMedia();
                libVlc?.Dispose();
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Disposing LibVLC failed");
            }
            finally
            {
                _gate.Release();
            }
        });

        if (!cleanup.Wait(DisposeTimeout + DisposeTimeout))
            _log.Warning("Disposing LibVLC did not finish within {Timeout}; continuing", DisposeTimeout + DisposeTimeout);
        _player = null;
        _libVlc = null;
        _ready = false;
    }
}
