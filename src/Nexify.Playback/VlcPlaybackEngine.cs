using Nexify.Core.Media;
using Nexify.Core.Playback;
using Serilog;

namespace Nexify.Playback;

public sealed class VlcPlaybackEngine(ILogger log, string[]? extraVlcOptions = null) : IPlaybackEngine
{
    private readonly ILogger _log = log;
    private readonly string[] _extra = extraVlcOptions ?? [];
    public Task InitializeAsync() => throw new NotImplementedException();
    public bool IsReady => throw new NotImplementedException();
    public PlaybackState State => throw new NotImplementedException();
    public MediaItem? Current => throw new NotImplementedException();
    public MediaKind? CurrentKind => throw new NotImplementedException();
    public string? CurrentTitle => throw new NotImplementedException();
    public string? CurrentArtist => throw new NotImplementedException();
    public TimeSpan Position => throw new NotImplementedException();
    public TimeSpan Duration => throw new NotImplementedException();
    public int Volume { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public bool IsMuted { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public object? VideoSurfaceSource => throw new NotImplementedException();
    public event EventHandler<PlaybackState>? StateChanged { add { } remove { } }
    public event EventHandler<TimeSpan>? PositionChanged { add { } remove { } }
    public event EventHandler<TimeSpan>? DurationChanged { add { } remove { } }
    public event EventHandler? EndReached { add { } remove { } }
    public event EventHandler<PlaybackError>? PlaybackFailed { add { } remove { } }
    public event EventHandler? MetadataChanged { add { } remove { } }
    public Task OpenAsync(MediaItem item) => throw new NotImplementedException();
    public void Play() => throw new NotImplementedException();
    public void Pause() => throw new NotImplementedException();
    public void TogglePlayPause() => throw new NotImplementedException();
    public void Stop() => throw new NotImplementedException();
    public void Seek(TimeSpan position) => throw new NotImplementedException();
    public void SeekRelative(TimeSpan delta) => throw new NotImplementedException();
    public void Dispose() { }
}
