using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Nexify.App.Pages;
using Nexify.App.Services;
using Nexify.Presentation;
using NavPage = Nexify.Presentation.Page;

namespace Nexify.App.Shell;

/// <summary>
/// The shell (spec 3.3/3.4): custom title bar, sidebar navigation, page area and the transport-bar row.
/// Pages are created once and kept in the tree, so their state (and the video surface) survives navigation.
/// </summary>
public partial class MainWindow : Window
{
    private static readonly Duration TransitionDuration = new(TimeSpan.FromMilliseconds(150));
    private const double TransitionOffset = 8;

    private readonly ShellViewModel _shell;
    private readonly IThemeService _theme;
    private readonly PlayerViewModel _player;
    private readonly WindowChromeHelper _chrome;
    private readonly Dictionary<NavPage, FrameworkElement> _pages = [];
    private FrameworkElement? _activePage;

    public MainWindow(ShellViewModel shell, IThemeService theme, PlayerViewModel player, NotificationsViewModel notifications)
    {
        _shell = shell;
        _theme = theme;
        _player = player;
        InitializeComponent();
        DataContext = shell;
        Transport.DataContext = player;
        Transport.QueueRequested += (_, _) => _shell.NavigateCommand.Execute(NavPage.NowPlaying);
        Toasts.DataContext = notifications;
        foreach (var page in Enum.GetValues<NavPage>())
        {
            GetPage(page);
        }

        _chrome = new WindowChromeHelper(this, TitleBarPanel.MaximizeButton);

        SourceInitialized += OnSourceInitialized;
        SizeChanged += (_, _) => OnSizeOrStateChanged();
        StateChanged += (_, _) =>
        {
            if (WindowState != WindowState.Minimized)
            {
                LastNonMinimizedState = WindowState;
            }

            OnSizeOrStateChanged();
        };
        Closed += OnClosed;
        _theme.EffectiveThemeChanged += OnEffectiveThemeChanged;
        _shell.PropertyChanged += OnShellPropertyChanged;

        _shell.UpdateForWidth(Width);
        Loaded += (_, _) => LastNonMinimizedState = WindowState == WindowState.Minimized ? LastNonMinimizedState : WindowState;
        ShowPage(_shell.CurrentPage, animate: false);
    }

    /// <summary>Normal or Maximized: the state to restore when the window is closed while minimized.</summary>
    public WindowState LastNonMinimizedState { get; private set; } = WindowState.Normal;

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        // ThemeService raises no event for its first Apply, so read the current state once here.
        _chrome.SetDarkMode(_theme.IsDarkEffective);
        OnSizeOrStateChanged();
    }

    private void OnEffectiveThemeChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess())
        {
            _chrome.SetDarkMode(_theme.IsDarkEffective);
        }
        else
        {
            Dispatcher.BeginInvoke(() => _chrome.SetDarkMode(_theme.IsDarkEffective));
        }
    }

    private void OnSizeOrStateChanged()
    {
        _shell.UpdateForWidth(ActualWidth > 0 ? ActualWidth : Width);
        Root.Margin = _chrome.GetMaximizedOverhang();
    }

    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.CurrentPage))
        {
            ShowPage(_shell.CurrentPage, animate: true);
        }
    }

    private void ShowPage(NavPage page, bool animate)
    {
        var view = GetPage(page);
        if (ReferenceEquals(_activePage, view))
        {
            return;
        }

        _activePage = view;
        foreach (var other in _pages.Values)
        {
            other.Visibility = ReferenceEquals(other, view) ? Visibility.Visible : Visibility.Hidden;
        }

        if (!animate || !AnimationsEnabled)
        {
            PageHost.BeginAnimation(OpacityProperty, null);
            PageTranslate.BeginAnimation(TranslateTransform.YProperty, null);
            PageHost.Opacity = 1;
            PageTranslate.Y = 0;
            return;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        PageHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TransitionDuration) { EasingFunction = ease });
        PageTranslate.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(TransitionOffset, 0, TransitionDuration) { EasingFunction = ease });
    }

    private bool AnimationsEnabled => TryFindResource(ThemeService.AnimEnabledKey) is not bool enabled || enabled;

    private FrameworkElement GetPage(NavPage page)
    {
        if (!_pages.TryGetValue(page, out var view))
        {
            view = page switch
            {
                NavPage.Home => new HomePage(),
                NavPage.NowPlaying => new NowPlayingPage { DataContext = _player },
                NavPage.Settings => new SettingsPage(),
                _ => throw new ArgumentOutOfRangeException(nameof(page), page, null),
            };
            view.Visibility = Visibility.Hidden;
            _pages[page] = view;
            PageHost.Children.Add(view);
        }

        return view;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _theme.EffectiveThemeChanged -= OnEffectiveThemeChanged;
        _shell.PropertyChanged -= OnShellPropertyChanged;
        _chrome.Dispose();
    }
}
