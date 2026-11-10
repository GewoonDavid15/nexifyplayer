using Nexify.Core.Settings;
using Nexify.Presentation;
using Xunit;

namespace Nexify.Presentation.Tests;

public class WindowPlacementLogicTests
{
    private static readonly Rect Screen1080 = new(0, 0, 1920, 1080);
    private static readonly Rect Work1080 = new(0, 0, 1920, 1040);

    [Fact]
    public void WindowPlacement_Null_CentersOnPrimary()
    {
        var b = WindowPlacementLogic.Sanitize(null, [Screen1080], Work1080);

        Assert.Equal(1280, b.Width);
        Assert.Equal(800, b.Height);
        Assert.Equal((1920 - 1280) / 2.0, b.Left);
        Assert.Equal((1040 - 800) / 2.0, b.Top);
        Assert.False(b.IsMaximized);
    }

    [Fact]
    public void WindowPlacement_Null_SmallWorkArea_AtMost90Percent()
    {
        var work = new Rect(0, 0, 1280, 720);
        var b = WindowPlacementLogic.Sanitize(null, [new Rect(0, 0, 1280, 760)], work);

        Assert.Equal(1152, b.Width);
        Assert.Equal(648, b.Height);
        Assert.Equal((1280 - 1152) / 2.0, b.Left);
        Assert.Equal((720 - 648) / 2.0, b.Top);
    }

    [Fact]
    public void WindowPlacement_Null_CentersOnPrimaryWithOffset()
    {
        var work = new Rect(-1920, 100, 1920, 1040);
        var b = WindowPlacementLogic.Sanitize(null, [new Rect(-1920, 100, 1920, 1080)], work);

        Assert.Equal(-1920 + 320, b.Left);
        Assert.Equal(100 + 120, b.Top);
    }

    [Fact]
    public void WindowPlacement_OffscreenBounds_ResetToPrimary()
    {
        var saved = new WindowBounds(5000, 100, 1000, 700, false);
        var b = WindowPlacementLogic.Sanitize(saved, [Screen1080], Work1080);

        Assert.Equal(new WindowBounds(320, 120, 1280, 800, false), b);
    }

    [Fact]
    public void WindowPlacement_OnlySlightlyVisibleTitleBar_ResetToPrimary()
    {
        // only 40 px of the title bar width is on the screen (needs 64 x 64)
        var saved = new WindowBounds(1880, 100, 1000, 700, false);
        var b = WindowPlacementLogic.Sanitize(saved, [Screen1080], Work1080);

        Assert.Equal(320, b.Left);
        Assert.Equal(120, b.Top);
    }

    [Fact]
    public void WindowPlacement_TitleBarAboveScreen_ResetToPrimary()
    {
        var saved = new WindowBounds(100, -300, 1000, 700, false);
        var b = WindowPlacementLogic.Sanitize(saved, [Screen1080], Work1080);

        Assert.Equal(320, b.Left);
        Assert.Equal(120, b.Top);
    }

    [Fact]
    public void WindowPlacement_ValidBounds_Kept()
    {
        var saved = new WindowBounds(200, 150, 1100, 750, true);
        var b = WindowPlacementLogic.Sanitize(saved, [Screen1080], Work1080);

        Assert.Equal(saved, b);
    }

    [Fact]
    public void WindowPlacement_ValidOnSecondScreen_Kept()
    {
        var second = new Rect(1920, -200, 2560, 1440);
        var saved = new WindowBounds(2100, -100, 1200, 800, false);
        var b = WindowPlacementLogic.Sanitize(saved, [Screen1080, second], Work1080);

        Assert.Equal(saved, b);
    }

    [Fact]
    public void WindowPlacement_TooSmall_ClampedTo720x480()
    {
        var saved = new WindowBounds(100, 100, 300, 200, false);
        var b = WindowPlacementLogic.Sanitize(saved, [Screen1080], Work1080);

        Assert.Equal(new WindowBounds(100, 100, 720, 480, false), b);
    }

    [Fact]
    public void WindowPlacement_InvalidNumbers_ResetToPrimary()
    {
        var saved = new WindowBounds(double.NaN, 100, double.PositiveInfinity, 700, false);
        var b = WindowPlacementLogic.Sanitize(saved, [Screen1080], Work1080);

        Assert.Equal(new WindowBounds(320, 120, 1280, 800, false), b);
    }

    [Fact]
    public void WindowPlacement_NoScreens_ResetToPrimary()
    {
        var saved = new WindowBounds(200, 150, 1100, 750, false);
        var b = WindowPlacementLogic.Sanitize(saved, [], Work1080);

        Assert.Equal(320, b.Left);
    }
}
