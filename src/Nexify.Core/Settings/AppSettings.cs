using Nexify.Core.Queue;

namespace Nexify.Core.Settings;

public enum ThemeMode { Dark, Light, System }

public enum OpenBehavior { Replace, Append }

public sealed record WindowBounds(double Left, double Top, double Width, double Height, bool IsMaximized);

/// <summary>Persisted user settings. Missing fields in the JSON file fall back to these defaults.</summary>
public sealed record AppSettings
{
    public static readonly int[] AllowedSeekSteps = [5, 10, 30];

    public int SchemaVersion { get; init; } = 1;
    public ThemeMode Theme { get; init; } = ThemeMode.Dark;

    /// <summary>Language code; null means "decide automatically on first start".</summary>
    public string? Language { get; init; }

    /// <summary>Last used volume, 0-100.</summary>
    public int Volume { get; init; } = 80;

    /// <summary>Volume applied at startup, 0-100.</summary>
    public int DefaultVolume { get; init; } = 80;

    public bool IsMuted { get; init; }
    public int SeekStepSeconds { get; init; } = 5;
    public OpenBehavior OpenBehavior { get; init; } = OpenBehavior.Replace;
    public bool Shuffle { get; init; }
    public RepeatMode Repeat { get; init; } = RepeatMode.Off;
    public WindowBounds? Window { get; init; }

    /// <summary>Returns a copy with Volume/DefaultVolume clamped to 0-100 and SeekStepSeconds limited to 5, 10 or 30.</summary>
    public AppSettings Normalized() => this with
    {
        Volume = Math.Clamp(Volume, 0, 100),
        DefaultVolume = Math.Clamp(DefaultVolume, 0, 100),
        SeekStepSeconds = Array.IndexOf(AllowedSeekSteps, SeekStepSeconds) >= 0 ? SeekStepSeconds : 5,
    };
}
