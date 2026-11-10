using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.UIA3;
using Xunit;
using Xunit.Abstractions;

namespace Nexify.App.UiTests;

/// <summary>
/// Launches the built app (NEXIFY_APP_EXE, or the Nexify.App build output next to this test project) with an
/// isolated NEXIFY_DATA_DIR and saves screenshots of the shell for visual review (CI artifact "screenshots").
/// Screenshots go to NEXIFY_SCREENSHOT_DIR (default: %TEMP%\nexify-screenshots).
/// </summary>
public sealed class ShellScreenshotTests(ITestOutputHelper output)
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(20);

    [WindowsOnlyFact]
    public void Shell_Screenshots()
    {
        var dir = ScreenshotDir();
        output.WriteLine($"Screenshots: {dir}");
        output.WriteLine($"Primary screen: {System.Windows.SystemParameters.PrimaryScreenWidth} x {System.Windows.SystemParameters.PrimaryScreenHeight} DIP");

        using (var session = AppSession.Start(output, theme: "dark", language: "en", width: 1280, height: 800))
        {
            session.Shot(dir, "01-dark-home-1280x800");
            session.Select("Nav.NowPlaying");
            session.Shot(dir, "02-dark-nowplaying-1280x800");
            session.Select("Nav.Settings");
            session.Shot(dir, "03-dark-settings-1280x800");
        }

        using (var session = AppSession.Start(output, theme: "dark", language: "en", width: 860, height: 600))
        {
            session.Shot(dir, "04-dark-compact-860x600");
            session.Hover("Nav.NowPlaying", TimeSpan.FromMilliseconds(1200));
            session.Shot(dir, "07-dark-compact-tooltip", parkPointer: false);
            session.Hover("TitleBar.Maximize", TimeSpan.FromMilliseconds(400));
            session.Shot(dir, "08-dark-maximize-hover", parkPointer: false);
        }

        using (var session = AppSession.Start(output, theme: "light", language: "nl", width: 1280, height: 800))
        {
            session.Shot(dir, "05-light-home-nl-1280x800");
        }

        using (var session = AppSession.Start(output, theme: "dark", language: "en", width: 1280, height: 800, maximized: true))
        {
            session.Shot(dir, "06-dark-maximized");
        }
    }

    [WindowsOnlyFact]
    public void Shell_MaximizeButton_ReportsHtMaxButton_AndClickMaximizes()
    {
        using var session = AppSession.Start(output, theme: "dark", language: "en", width: 1000, height: 700);
        var button = session.Window.FindFirstDescendant(cf => cf.ByAutomationId("TitleBar.Maximize"))
            ?? throw new InvalidOperationException("TitleBar.Maximize not found");
        var center = Center(button.BoundingRectangle);
        var hwnd = session.Window.Properties.NativeWindowHandle.Value;

        // Snap Layouts: Windows asks WM_NCHITTEST; the maximize button must answer HTMAXBUTTON (9).
        var hit = SendMessage(hwnd, 0x0084, IntPtr.Zero, MakeLParam(center.X, center.Y)).ToInt64();
        Assert.Equal(9, hit);

        // Elsewhere in the caption the WindowChrome answers HTCAPTION (2).
        var caption = session.Window.BoundingRectangle;
        var captionHit = SendMessage(hwnd, 0x0084, IntPtr.Zero, MakeLParam(caption.Left + caption.Width / 2, caption.Top + 20)).ToInt64();
        Assert.Equal(2, captionHit);

        // A real click on the button still maximizes (handled via WM_NCLBUTTONDOWN/UP).
        Mouse.Click(center);
        Assert.True(
            WaitUntil(() => session.Window.Patterns.Window.Pattern.WindowVisualState.Value == WindowVisualState.Maximized, TimeSpan.FromSeconds(5)),
            "window did not maximize after clicking the maximize button");

        var restore = Center(session.Window.FindFirstDescendant(cf => cf.ByAutomationId("TitleBar.Maximize"))!.BoundingRectangle);
        Mouse.Click(restore);
        Assert.True(
            WaitUntil(() => session.Window.Patterns.Window.Pattern.WindowVisualState.Value == WindowVisualState.Normal, TimeSpan.FromSeconds(5)),
            "window did not restore after clicking the restore button");
    }

    private static Point Center(Rectangle r) => new(r.Left + r.Width / 2, r.Top + r.Height / 2);

    private static IntPtr MakeLParam(int x, int y) => new((y << 16) | (x & 0xFFFF));

    private static bool WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (condition()) return true;
            Thread.Sleep(100);
        }

        return condition();
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    private static string ScreenshotDir()
    {
        var dir = Environment.GetEnvironmentVariable("NEXIFY_SCREENSHOT_DIR") is { Length: > 0 } d
            ? d
            : Path.Combine(Path.GetTempPath(), "nexify-screenshots");
        Directory.CreateDirectory(dir);
        return dir;
    }

    internal static string LocateAppExe()
    {
        if (Environment.GetEnvironmentVariable("NEXIFY_APP_EXE") is { Length: > 0 } exe)
        {
            return Path.GetFullPath(exe);
        }

        var baseDir = AppContext.BaseDirectory;
        var configs = baseDir.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            ? new[] { "Release", "Debug" }
            : new[] { "Debug", "Release" };
        for (var dir = new DirectoryInfo(baseDir); dir is not null; dir = dir.Parent)
        {
            if (!File.Exists(Path.Combine(dir.FullName, "NexifyPlayer.sln")))
            {
                continue;
            }

            foreach (var config in configs)
            {
                var candidate = Path.Combine(dir.FullName, "src", "Nexify.App", "bin", config, "net8.0-windows10.0.19041.0", "NexifyPlayer.exe");
                if (File.Exists(candidate)) return candidate;
            }
        }

        throw new FileNotFoundException("NexifyPlayer.exe not found; set NEXIFY_APP_EXE or build src/Nexify.App.");
    }

    /// <summary>One app process with its own temporary data directory.</summary>
    private sealed class AppSession : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly Application _app;
        private readonly UIA3Automation _automation;
        private readonly string _dataDir;

        private AppSession(ITestOutputHelper output, Application app, UIA3Automation automation, Window window, string dataDir)
        {
            _output = output;
            _app = app;
            _automation = automation;
            Window = window;
            _dataDir = dataDir;
        }

        public Window Window { get; }

        public static AppSession Start(ITestOutputHelper output, string theme, string language, int width, int height, bool maximized = false)
        {
            var dataDir = Path.Combine(Path.GetTempPath(), "nexify-ui-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dataDir);
            File.WriteAllText(Path.Combine(dataDir, "settings.json"), $$"""
                {
                  "theme": "{{theme}}",
                  "language": "{{language}}",
                  "window": { "left": 0, "top": 0, "width": {{width}}, "height": {{height}}, "isMaximized": {{(maximized ? "true" : "false")}} }
                }
                """);

            var exe = LocateAppExe();
            output.WriteLine($"Launching {exe} (data {dataDir}, {theme}/{language}, {width}x{height}{(maximized ? ", maximized" : "")})");
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! };
            psi.Environment["NEXIFY_DATA_DIR"] = dataDir;

            var app = Application.Launch(psi);
            var automation = new UIA3Automation();
            try
            {
                var window = app.GetMainWindow(automation, StartTimeout)
                    ?? throw new InvalidOperationException("main window did not appear");
                Assert.Equal("MainWindow", window.AutomationId);
                window.SetForeground();
                Thread.Sleep(1500); // first render, fonts, theme
                return new AppSession(output, app, automation, window, dataDir);
            }
            catch
            {
                DumpLogs(output, dataDir);
                automation.Dispose();
                app.Kill();
                app.Dispose();
                throw;
            }
        }

        public void Select(string automationId)
        {
            var item = Window.FindFirstDescendant(cf => cf.ByAutomationId(automationId))
                ?? throw new InvalidOperationException($"{automationId} not found");
            item.Patterns.SelectionItem.Pattern.Select();
            Thread.Sleep(600); // 150 ms transition + render
        }

        public void Hover(string automationId, TimeSpan wait)
        {
            var element = Window.FindFirstDescendant(cf => cf.ByAutomationId(automationId))
                ?? throw new InvalidOperationException($"{automationId} not found");
            Mouse.MoveTo(Center(element.BoundingRectangle));
            Thread.Sleep(wait);
        }

        public void Shot(string dir, string name, bool parkPointer = true)
        {
            var r = Window.BoundingRectangle;
            if (parkPointer)
            {
                // keep the pointer over the (non-interactive) logo so no hover state is captured
                Mouse.Position = new Point(r.Left + 30, r.Top + 20);
                Thread.Sleep(250);
            }

            var path = Path.Combine(dir, name + ".png");
            Capture.Element(Window).ToFile(path);
            _output.WriteLine($"Saved {path} ({r.Width} x {r.Height} px at {r.Left},{r.Top})");
        }

        public void Dispose()
        {
            try
            {
                _app.Close();
                if (!_app.HasExited)
                {
                    _app.WaitWhileMainHandleIsMissing(TimeSpan.FromSeconds(1));
                }

                var sw = Stopwatch.StartNew();
                while (!_app.HasExited && sw.Elapsed < TimeSpan.FromSeconds(10)) Thread.Sleep(100);
                if (!_app.HasExited)
                {
                    _output.WriteLine("App did not exit within 10 s; killing it");
                    _app.Kill();
                }
            }
            finally
            {
                _automation.Dispose();
                _app.Dispose();
                var settings = Path.Combine(_dataDir, "settings.json");
                if (File.Exists(settings)) _output.WriteLine("settings.json after exit:\n" + File.ReadAllText(settings));
                DumpLogs(_output, _dataDir);
            }
        }

        private static void DumpLogs(ITestOutputHelper output, string dataDir)
        {
            var logs = Path.Combine(dataDir, "logs");
            if (!Directory.Exists(logs)) return;
            foreach (var file in Directory.GetFiles(logs))
            {
                try
                {
                    using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var reader = new StreamReader(stream);
                    output.WriteLine($"--- {Path.GetFileName(file)}\n{reader.ReadToEnd()}");
                }
                catch (IOException ex)
                {
                    output.WriteLine($"--- {Path.GetFileName(file)}: {ex.Message}");
                }
            }
        }
    }
}
