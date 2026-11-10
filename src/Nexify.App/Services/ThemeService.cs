using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Nexify.Core.Settings;
using Nexify.Presentation;

namespace Nexify.App.Services;

/// <summary>
/// Switches the colour-token dictionary (Tokens.Dark.xaml / Tokens.Light.xaml) in the application resources,
/// follows the Windows app theme while in <see cref="ThemeMode.System"/>, and publishes the
/// <c>Anim.Enabled</c> resource from <see cref="SystemParameters.ClientAreaAnimation"/>.
/// </summary>
public sealed class ThemeService : IThemeService, IDisposable
{
    public const string AnimEnabledKey = "Anim.Enabled";

    private const string DarkFile = "Tokens.Dark.xaml";
    private const string LightFile = "Tokens.Light.xaml";
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsUseLightThemeValue = "AppsUseLightTheme";

    private static readonly Uri DarkUri = new($"pack://application:,,,/NexifyPlayer;component/Themes/{DarkFile}", UriKind.Absolute);
    private static readonly Uri LightUri = new($"pack://application:,,,/NexifyPlayer;component/Themes/{LightFile}", UriKind.Absolute);

    private readonly ResourceDictionary _resources;
    private readonly Dispatcher _dispatcher;
    private ResourceDictionary? _dark;
    private ResourceDictionary? _light;
    private bool _disposed;

    public ThemeService()
        : this(Application.Current.Resources, Application.Current.Dispatcher)
    {
    }

    public ThemeService(ResourceDictionary resources, Dispatcher dispatcher)
    {
        _resources = resources;
        _dispatcher = dispatcher;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public ThemeMode Mode { get; private set; } = ThemeMode.Dark;

    public bool IsDarkEffective { get; private set; } = true;

    public event EventHandler? EffectiveThemeChanged;

    public void Apply(ThemeMode mode)
    {
        Mode = mode;
        Refresh(force: true);
    }

    /// <summary>Reads <c>HKCU\...\Themes\Personalize\AppsUseLightTheme</c>; true (light) when absent, as on Windows without dark mode.</summary>
    public static bool ReadSystemUsesLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue(AppsUseLightThemeValue) is not int value || value != 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            return true;
        }
    }

    private void Refresh(bool force)
    {
        _resources[AnimEnabledKey] = SystemParameters.ClientAreaAnimation;

        var dark = ThemeResolution.IsDark(Mode, ReadSystemUsesLight());
        var changed = dark != IsDarkEffective;
        if (changed || force)
        {
            SetTokens(dark);
        }

        IsDarkEffective = dark;
        if (changed)
        {
            EffectiveThemeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Replaces the token dictionary in place (found by its Source), or inserts it first when missing.</summary>
    private void SetTokens(bool dark)
    {
        var wanted = dark
            ? _dark ??= new ResourceDictionary { Source = DarkUri }
            : _light ??= new ResourceDictionary { Source = LightUri };

        var merged = _resources.MergedDictionaries;
        for (var i = 0; i < merged.Count; i++)
        {
            if (IsTokenDictionary(merged[i]))
            {
                if (!ReferenceEquals(merged[i], wanted))
                {
                    merged[i] = wanted;
                }

                return;
            }
        }

        merged.Insert(0, wanted);
    }

    private static bool IsTokenDictionary(ResourceDictionary dictionary)
    {
        var source = dictionary.Source?.OriginalString;
        return source is not null
            && (source.EndsWith(DarkFile, StringComparison.OrdinalIgnoreCase)
                || source.EndsWith(LightFile, StringComparison.OrdinalIgnoreCase));
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        // Theme switches arrive as General ("ImmersiveColorSet"); the animation setting can arrive under other
        // categories, so every notification refreshes Anim.Enabled and only General re-reads the app theme.
        _dispatcher.BeginInvoke(() =>
        {
            if (_disposed)
            {
                return;
            }

            if (e.Category == UserPreferenceCategory.General)
            {
                Refresh(force: false);
            }
            else
            {
                _resources[AnimEnabledKey] = SystemParameters.ClientAreaAnimation;
            }
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }
}
