using Nexify.Core.Settings;

namespace Nexify.Presentation;

/// <summary>Applies the light/dark colour tokens and follows the Windows app theme in <see cref="ThemeMode.System"/>.</summary>
public interface IThemeService
{
    /// <summary>The mode last passed to <see cref="Apply"/>.</summary>
    ThemeMode Mode { get; }

    /// <summary>True when the dark tokens are currently active.</summary>
    bool IsDarkEffective { get; }

    void Apply(ThemeMode mode);

    /// <summary>Raised when the effective theme (dark/light) changes, also when Windows switches while in System mode.</summary>
    event EventHandler? EffectiveThemeChanged;
}
