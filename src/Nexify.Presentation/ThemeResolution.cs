using Nexify.Core.Settings;

namespace Nexify.Presentation;

public static class ThemeResolution
{
    /// <summary>Resolves a theme mode to dark (true) or light (false).</summary>
    /// <param name="systemUsesLight">Windows "AppsUseLightTheme"; only consulted for <see cref="ThemeMode.System"/>.</param>
    public static bool IsDark(ThemeMode mode, bool systemUsesLight) => mode switch
    {
        ThemeMode.Dark => true,
        ThemeMode.Light => false,
        _ => !systemUsesLight,
    };
}
