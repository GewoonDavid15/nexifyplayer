using Nexify.Core.Settings;
using Nexify.Presentation;
using Xunit;

namespace Nexify.Presentation.Tests;

public class ThemeResolutionTests
{
    [Theory]
    [InlineData(ThemeMode.Dark, false, true)]
    [InlineData(ThemeMode.Dark, true, true)]
    [InlineData(ThemeMode.Light, false, false)]
    [InlineData(ThemeMode.Light, true, false)]
    [InlineData(ThemeMode.System, true, false)]
    [InlineData(ThemeMode.System, false, true)]
    public void IsDark_Table(ThemeMode mode, bool systemUsesLight, bool expected)
    {
        Assert.Equal(expected, ThemeResolution.IsDark(mode, systemUsesLight));
    }
}
