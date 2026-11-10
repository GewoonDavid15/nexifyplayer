using Nexify.Core.Media;

namespace Nexify.Core.Playback;

/// <summary>
/// The only way the UI talks to the player. Events may be raised on any thread (never on the
/// engine's native callback thread); the UI layer marshals them to its own thread.
/// </summary>
public interface IPlaybackEngine : IDisposable
{
    /// <summary>Loads the engine on a background thread. Never throws; on failure <see cref="IsReady"/>
    /// stays false and <see cref="PlaybackFailed"/> is raised with <see cref="PlaybackErrorReason.EngineUnavailable"/>.</summary>
    Task InitializeAsync();

    bool IsReady { get; }
    PlaybackState State { get; }
    MediaItem? Current { get; }
    MediaKind? CurrentKind { get; }

    /// <summary>Title from the media metadata after parsing, otherwise null.</summary>
    string? CurrentTitle { get; }

    /// <summary>Artist from the media metadata after parsing, otherwise null.</summary>
    string? CurrentArtist { get; }

    TimeSpan Position { get; }
    TimeSpan Duration { get; }

    /// <summary>0–100.</summary>
    int Volume { get; set; }
    bool IsMuted { get; set; }

    /// <summary>Loads and starts playing. Safe to call from any thread, including from an event handler.</summary>
    Task OpenAsync(MediaItem item);
    void Play();
    void Pause();
    void TogglePlayPause();
    void Stop();
    void Seek(TimeSpan position);
    void SeekRelative(TimeSpan delta);

    /// <summary>The underlying player object for the UI's video view.</summary>
    object? VideoSurfaceSource { get; }

    event EventHandler<PlaybackState> StateChanged;
    event EventHandler<TimeSpan> PositionChanged;
    event EventHandler<TimeSpan> DurationChanged;
    event EventHandler EndReached;
    event EventHandler<PlaybackError> PlaybackFailed;
    event EventHandler MetadataChanged;
}
