using Nexify.Core.Settings;

namespace Nexify.Presentation;

/// <summary>A rectangle in device-independent pixels (WPF units).</summary>
public readonly record struct Rect(double X, double Y, double W, double H)
{
    public double Right => X + W;
    public double Bottom => Y + H;

    /// <summary>The overlapping part of two rectangles; width/height 0 when they do not overlap.</summary>
    public Rect Intersect(Rect other)
    {
        var x = Math.Max(X, other.X);
        var y = Math.Max(Y, other.Y);
        var w = Math.Max(0, Math.Min(Right, other.Right) - x);
        var h = Math.Max(0, Math.Min(Bottom, other.Bottom) - y);
        return new Rect(x, y, w, h);
    }
}

/// <summary>Restores the saved window placement safely (spec 3.3).</summary>
public static class WindowPlacementLogic
{
    public const double MinWidth = 720;
    public const double MinHeight = 480;
    public const double DefaultWidth = 1280;
    public const double DefaultHeight = 800;

    /// <summary>A grip of at least this many DIP (width and height) of the window's top strip must lie on one screen.</summary>
    public const double MinVisibleGrip = 64;

    private const double DefaultMaxFraction = 0.9;

    /// <summary>
    /// Returns <paramref name="saved"/> with its size clamped to at least 720 x 480, or, when there is no saved value
    /// or less than 64 x 64 DIP of its title-bar strip lies on any screen, a 1280 x 800 window (at most 90% of the
    /// work area) centred on the primary work area. The maximized flag is kept.
    /// </summary>
    public static WindowBounds Sanitize(WindowBounds? saved, IReadOnlyList<Rect> screens, Rect primaryWorkArea)
    {
        if (saved is null || !IsFinite(saved))
        {
            return Default(primaryWorkArea, isMaximized: false);
        }

        var width = Math.Max(saved.Width, MinWidth);
        var height = Math.Max(saved.Height, MinHeight);
        var titleStrip = new Rect(saved.Left, saved.Top, width, MinVisibleGrip);
        var reachable = screens.Any(screen =>
        {
            var visible = titleStrip.Intersect(screen);
            return visible.W >= MinVisibleGrip && visible.H >= MinVisibleGrip;
        });

        return reachable
            ? saved with { Width = width, Height = height }
            : Default(primaryWorkArea, saved.IsMaximized);
    }

    private static WindowBounds Default(Rect work, bool isMaximized)
    {
        var width = Math.Max(MinWidth, Math.Min(DefaultWidth, Math.Floor(work.W * DefaultMaxFraction)));
        var height = Math.Max(MinHeight, Math.Min(DefaultHeight, Math.Floor(work.H * DefaultMaxFraction)));
        return new WindowBounds(
            work.X + (work.W - width) / 2,
            work.Y + (work.H - height) / 2,
            width,
            height,
            isMaximized);
    }

    private static bool IsFinite(WindowBounds b)
        => double.IsFinite(b.Left) && double.IsFinite(b.Top) && double.IsFinite(b.Width) && double.IsFinite(b.Height);
}
