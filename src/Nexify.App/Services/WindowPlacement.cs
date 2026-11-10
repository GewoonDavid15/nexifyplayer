using System.Windows;
using Nexify.Core.Settings;
using Nexify.Presentation;
using static Nexify.App.Services.NativeMethods;
using Rect = Nexify.Presentation.Rect;

namespace Nexify.App.Services;

/// <summary>
/// Restores and captures the main window placement (spec 3.3). Screens are enumerated with
/// EnumDisplayMonitors/GetMonitorInfo (no System.Windows.Forms) and converted from pixels to DIP with the
/// system DPI, which is the scale WPF uses for Left/Top/Width/Height in a system-DPI-aware process.
/// </summary>
public static class WindowPlacement
{
    /// <summary>Applies the sanitized placement before the window is shown.</summary>
    public static void Apply(Window window, WindowBounds? saved)
    {
        var (screens, primaryWork) = GetScreens();
        var bounds = WindowPlacementLogic.Sanitize(saved, screens, primaryWork);

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = bounds.Left;
        window.Top = bounds.Top;
        window.Width = bounds.Width;
        window.Height = bounds.Height;
        window.WindowState = bounds.IsMaximized ? WindowState.Maximized : WindowState.Normal;
    }

    /// <summary>The restore bounds plus maximized state, or null when the window has no usable bounds.</summary>
    public static WindowBounds? Capture(Window window)
    {
        var r = window.WindowState == WindowState.Normal
            ? new System.Windows.Rect(window.Left, window.Top, window.ActualWidth, window.ActualHeight)
            : window.RestoreBounds;

        if (r.IsEmpty || !double.IsFinite(r.Left) || !double.IsFinite(r.Top) || r.Width <= 0 || r.Height <= 0)
        {
            return null;
        }

        return new WindowBounds(r.Left, r.Top, r.Width, r.Height, window.WindowState == WindowState.Maximized);
    }

    /// <summary>All monitor rectangles and the primary work area, in DIP.</summary>
    public static (IReadOnlyList<Rect> Screens, Rect PrimaryWork) GetScreens()
    {
        try
        {
            var scale = Math.Max(1, GetDpiForSystem()) / 96.0;
            var screens = new List<Rect>();
            Rect? primaryWork = null;

            bool Callback(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data)
            {
                var info = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
                if (GetMonitorInfo(monitor, ref info))
                {
                    screens.Add(ToDip(info.rcMonitor, scale));
                    if ((info.dwFlags & MONITORINFOF_PRIMARY) != 0)
                    {
                        primaryWork = ToDip(info.rcWork, scale);
                    }
                }

                return true;
            }

            MonitorEnumProc proc = Callback;
            if (EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, proc, IntPtr.Zero) && screens.Count > 0)
            {
                GC.KeepAlive(proc);
                return (screens, primaryWork ?? FallbackWorkArea());
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // fall through to the SystemParameters fallback
        }

        // Fallback: the virtual-screen bounding box and the primary work area as WPF reports them.
        var virtualScreen = new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);
        return ([virtualScreen], FallbackWorkArea());
    }

    private static Rect FallbackWorkArea()
    {
        var work = SystemParameters.WorkArea;
        return new Rect(work.Left, work.Top, work.Width, work.Height);
    }

    private static Rect ToDip(RECT r, double scale)
        => new(r.Left / scale, r.Top / scale, (r.Right - r.Left) / scale, (r.Bottom - r.Top) / scale);
}
