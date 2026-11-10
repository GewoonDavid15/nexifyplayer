using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using static Nexify.App.Services.NativeMethods;

namespace Nexify.App.Shell;

/// <summary>
/// Native window integration for the custom title bar:
/// <list type="bullet">
/// <item>Windows 11 Snap Layouts: WM_NCHITTEST returns HTMAXBUTTON over our maximize button, so Windows shows the
/// snap flyout on hover. Because the button then receives non-client messages instead of WPF mouse input, the
/// hover/pressed look is driven through <see cref="IsNcHoverProperty"/>/<see cref="IsNcPressedProperty"/> and the
/// click is handled from WM_NCLBUTTONDOWN/UP.</item>
/// <item>Dark/light window frame via DWMWA_USE_IMMERSIVE_DARK_MODE, and rounded corners on Windows 11.</item>
/// <item>The amount a maximized window overhangs its monitor work area, so the content can be inset.</item>
/// </list>
/// </summary>
public sealed class WindowChromeHelper : IDisposable
{
    public static readonly DependencyProperty IsNcHoverProperty = DependencyProperty.RegisterAttached(
        "IsNcHover", typeof(bool), typeof(WindowChromeHelper), new PropertyMetadata(false));

    public static readonly DependencyProperty IsNcPressedProperty = DependencyProperty.RegisterAttached(
        "IsNcPressed", typeof(bool), typeof(WindowChromeHelper), new PropertyMetadata(false));

    private readonly Window _window;
    private readonly ButtonBase _maximizeButton;
    private HwndSource? _source;
    private bool _tracking;

    public WindowChromeHelper(Window window, ButtonBase maximizeButton)
    {
        _window = window;
        _maximizeButton = maximizeButton;
        _window.SourceInitialized += OnSourceInitialized;
    }

    public static bool GetIsNcHover(DependencyObject element) => (bool)element.GetValue(IsNcHoverProperty);

    public static void SetIsNcHover(DependencyObject element, bool value) => element.SetValue(IsNcHoverProperty, value);

    public static bool GetIsNcPressed(DependencyObject element) => (bool)element.GetValue(IsNcPressedProperty);

    public static void SetIsNcPressed(DependencyObject element, bool value) => element.SetValue(IsNcPressedProperty, value);

    private IntPtr Handle => _source?.Handle ?? IntPtr.Zero;

    /// <summary>Sets the DWM dark-mode frame attribute. Safe to call before the window has a handle (no-op).</summary>
    public void SetDarkMode(bool dark)
    {
        var hwnd = Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var value = dark ? 1 : 0;
        if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int)) != 0)
        {
            // Windows 10 before 20H1 used attribute 19.
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_PRE_20H1, ref value, sizeof(int));
        }
    }

    /// <summary>How far (DIP) the maximized window extends past the monitor work area on each side; zero otherwise.</summary>
    public Thickness GetMaximizedOverhang()
    {
        var hwnd = Handle;
        if (hwnd == IntPtr.Zero || _window.WindowState != WindowState.Maximized || !GetWindowRect(hwnd, out var rect))
        {
            return default;
        }

        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
        {
            return default;
        }

        var dpi = VisualTreeHelper.GetDpi(_window);
        var work = info.rcWork;
        return new Thickness(
            Math.Max(0, work.Left - rect.Left) / dpi.DpiScaleX,
            Math.Max(0, work.Top - rect.Top) / dpi.DpiScaleY,
            Math.Max(0, rect.Right - work.Right) / dpi.DpiScaleX,
            Math.Max(0, rect.Bottom - work.Bottom) / dpi.DpiScaleY);
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _source = (HwndSource?)PresentationSource.FromVisual(_window);
        if (_source is null)
        {
            return;
        }

        // Added after WindowChrome's own hook, so this one runs first.
        _source.AddHook(WndProc);

        var round = DWMWCP_ROUND;
        DwmSetWindowAttribute(_source.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_NCHITTEST:
                if (IsOverMaximizeButton(lParam))
                {
                    handled = true;
                    return new IntPtr(HTMAXBUTTON);
                }

                break;

            case WM_NCMOUSEMOVE:
                SetHover(wParam.ToInt32() == HTMAXBUTTON);
                break;

            case WM_NCMOUSELEAVE:
                _tracking = false;
                SetHover(false);
                SetPressed(false);
                break;

            case WM_MOUSEMOVE:
                SetHover(false);
                break;

            case WM_NCLBUTTONDOWN:
            case WM_NCLBUTTONDBLCLK:
                // A quick second click arrives as a double-click; treat it as a press so the next button-up toggles again.
                if (wParam.ToInt32() == HTMAXBUTTON)
                {
                    SetPressed(true);
                    handled = true;
                    return IntPtr.Zero;
                }

                break;

            case WM_NCLBUTTONUP:
                if (wParam.ToInt32() == HTMAXBUTTON)
                {
                    var wasPressed = GetIsNcPressed(_maximizeButton);
                    SetPressed(false);
                    handled = true;
                    if (wasPressed && _maximizeButton.IsEnabled)
                    {
                        ToggleMaximize();
                    }

                    return IntPtr.Zero;
                }

                SetPressed(false);
                break;
        }

        return IntPtr.Zero;
    }

    /// <summary>Maximizes or restores the window, as the maximize button does.</summary>
    public void ToggleMaximize()
    {
        if (_window.WindowState == WindowState.Maximized)
        {
            SystemCommands.RestoreWindow(_window);
        }
        else
        {
            SystemCommands.MaximizeWindow(_window);
        }
    }

    private bool IsOverMaximizeButton(IntPtr lParam)
    {
        if (!_maximizeButton.IsVisible || PresentationSource.FromVisual(_maximizeButton) is null)
        {
            return false;
        }

        var value = lParam.ToInt64();
        var x = unchecked((short)(value & 0xFFFF));
        var y = unchecked((short)((value >> 16) & 0xFFFF));
        var point = _maximizeButton.PointFromScreen(new Point(x, y));
        return point.X >= 0 && point.Y >= 0
            && point.X < _maximizeButton.ActualWidth && point.Y < _maximizeButton.ActualHeight;
    }

    private void SetHover(bool hover)
    {
        if (hover && !_tracking && Handle != IntPtr.Zero)
        {
            // Ask for WM_NCMOUSELEAVE so the hover look is cleared when the pointer leaves the window.
            var track = new TRACKMOUSEEVENT
            {
                cbSize = Marshal.SizeOf<TRACKMOUSEEVENT>(),
                dwFlags = TME_LEAVE | TME_NONCLIENT,
                hwndTrack = Handle,
            };
            _tracking = TrackMouseEvent(ref track);
        }

        if (GetIsNcHover(_maximizeButton) != hover)
        {
            SetIsNcHover(_maximizeButton, hover);
        }

        if (!hover)
        {
            SetPressed(false);
        }
    }

    private void SetPressed(bool pressed)
    {
        if (GetIsNcPressed(_maximizeButton) != pressed)
        {
            SetIsNcPressed(_maximizeButton, pressed);
        }
    }

    public void Dispose()
    {
        _window.SourceInitialized -= OnSourceInitialized;
        _source?.RemoveHook(WndProc);
        _source = null;
    }
}
