using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Nexify.App.Localization;

namespace Nexify.App.Shell;

/// <summary>
/// Custom title bar (spec 3.3): drag area plus minimize, maximize/restore and close. The logo sits in the
/// sidebar header, which shares the 40 px caption row. The window chrome itself (WindowChrome,
/// Snap Layouts) is set up by <see cref="MainWindow"/> and <see cref="WindowChromeHelper"/>.
/// </summary>
public partial class TitleBar : UserControl
{
    private const string MaximizeGlyph = "";
    private const string RestoreGlyph = "";

    private Window? _window;

    public TitleBar()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>The maximize/restore button; WindowChromeHelper reports it to Windows as HTMAXBUTTON.</summary>
    public ButtonBase MaximizeButton => MaximizeButtonElement;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _window = Window.GetWindow(this);
        if (_window is not null)
        {
            _window.StateChanged += OnStateChanged;
            UpdateMaximizeButton();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_window is not null)
        {
            _window.StateChanged -= OnStateChanged;
            _window = null;
        }
    }

    private void OnStateChanged(object? sender, EventArgs e) => UpdateMaximizeButton();

    private void UpdateMaximizeButton()
    {
        var maximized = _window?.WindowState == WindowState.Maximized;
        MaximizeButtonElement.Content = maximized ? RestoreGlyph : MaximizeGlyph;
        var key = maximized ? "window.restore" : "window.maximize";
        MaximizeButtonElement.SetBinding(ToolTipProperty, new LocExtension(key).ProvideBinding());
        MaximizeButtonElement.SetBinding(AutomationPropertiesNameProperty, new LocExtension(key).ProvideBinding());
    }

    private static DependencyProperty AutomationPropertiesNameProperty => System.Windows.Automation.AutomationProperties.NameProperty;

    private void OnMinimize(object sender, RoutedEventArgs e)
    {
        if (_window is not null)
        {
            SystemCommands.MinimizeWindow(_window);
        }
    }

    private void OnMaximize(object sender, RoutedEventArgs e)
    {
        // Normally handled via WM_NCLBUTTONUP (HTMAXBUTTON); this covers keyboard/automation invocation.
        if (_window is null)
        {
            return;
        }

        if (_window.WindowState == WindowState.Maximized)
        {
            SystemCommands.RestoreWindow(_window);
        }
        else
        {
            SystemCommands.MaximizeWindow(_window);
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => _window?.Close();
}
