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

    /// <summary>
    /// Player screenshots with real media (NEXIFY_SHOWCASE_MEDIA, generated in CI): video on Now playing with a queue,
    /// audio playing on Home, error toasts, the queue context menu and the light theme.
    /// </summary>
    [WindowsOnlyFact]
    public void Player_Screenshots()
    {
        if (Environment.GetEnvironmentVariable("NEXIFY_SHOWCASE_MEDIA") is not { Length: > 0 } media || !Directory.Exists(media))
        {
            output.WriteLine("NEXIFY_SHOWCASE_MEDIA not set; skipping player screenshots");
            return;
        }

        var dir = ScreenshotDir();
        string M(string name) => Path.Combine(media, name);
        string[] videoQueue = [M("Nexify Demo.mp4"), M("Evening Drive.mp3"), M("Morning Light.flac"), M("City Lights.mp4"), M("Night Walk.ogg")];
        string[] audioQueue = [M("Evening Drive.mp3"), M("Morning Light.flac"), M("Nexify Demo.mp4"), M("Night Walk.ogg")];

        using (var session = AppSession.Start(output, theme: "dark", language: "en", width: 1280, height: 800, args: videoQueue))
        {
            session.WaitForPlayback("Nexify Demo");
            session.Select("Nav.NowPlaying");
            Thread.Sleep(1500);
            session.Shot(dir, "09-dark-nowplaying-video");

            session.RightClickQueueRow(2);
            session.Shot(dir, "10-dark-queue-contextmenu", parkPointer: false);
            Keyboard.Type(FlaUI.Core.WindowsAPI.VirtualKeyShort.ESCAPE); // Type = down + up (Press leaves it held)
            Thread.Sleep(300);

            // Drag "City Lights" (row 4) above "Evening Drive" (row 2).
            session.DragQueueRow(3, 1, dir);
            var order = session.QueueRowNames();
            output.WriteLine("Queue after drag: " + string.Join(", ", order));
            Assert.Equal(["Nexify Demo", "City Lights", "Evening Drive", "Morning Light", "Night Walk"], order);

            // Double-click plays a row.
            session.DoubleClickQueueRow(2);
            session.WaitForPlayback("Evening Drive");
            session.Shot(dir, "10b-dark-queue-after-drag-and-doubleclick");
        }

        using (var session = AppSession.Start(output, theme: "dark", language: "en", width: 1280, height: 800, args: audioQueue))
        {
            session.WaitForPlayback("Evening Drive");
            Thread.Sleep(3000); // let the time progress
            session.Shot(dir, "11-dark-home-audio-playing");
            session.Select("Nav.NowPlaying");
            session.Shot(dir, "12-dark-nowplaying-audio");
        }

        using (var session = AppSession.Start(output, theme: "dark", language: "en", width: 1280, height: 800,
                   args: [M("Missing Song.mp3"), M("Old Recording.wav"), M("Evening Drive.mp3")]))
        {
            Assert.True(
                WaitUntil(() => session.Window.FindAllDescendants(cf => cf.ByAutomationId("Toast.Message")).Length >= 2, TimeSpan.FromSeconds(20)),
                "expected two error toasts for the missing files");
            session.WaitForPlayback("Evening Drive");
            session.Shot(dir, "13-dark-toasts");
        }

        using (var session = AppSession.Start(output, theme: "light", language: "nl", width: 1280, height: 800, args: videoQueue))
        {
            session.WaitForPlayback("Nexify Demo");
            session.Select("Nav.NowPlaying");
            Thread.Sleep(1500);
            session.Shot(dir, "14-light-nowplaying-video-nl");
        }

        using (var session = AppSession.Start(output, theme: "light", language: "en", width: 860, height: 600, args: audioQueue))
        {
            session.WaitForPlayback("Evening Drive");
            session.Select("Nav.NowPlaying");
            session.Shot(dir, "15-light-nowplaying-audio-compact");
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

        public static AppSession Start(ITestOutputHelper output, string theme, string language, int width, int height, bool maximized = false, string[]? args = null)
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
            foreach (var arg in args ?? [])
            {
                psi.ArgumentList.Add(arg);
            }

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

        /// <summary>Waits until the transport bar shows <paramref name="title"/> and the time has started to run.</summary>
        public void WaitForPlayback(string title)
        {
            string Text(string id) => Window.FindFirstDescendant(cf => cf.ByAutomationId(id))?.Name ?? string.Empty;
            var playing = WaitUntil(
                () => Text("Transport.Title") == title && Text("Transport.Position") is { Length: > 0 } p && p != "0:00",
                TimeSpan.FromSeconds(25));
            _output.WriteLine($"Transport: title '{Text("Transport.Title")}', position {Text("Transport.Position")} / {Text("Transport.Duration")} (playing: {playing})");
            Assert.True(playing, $"'{title}' did not start playing");
        }

        private AutomationElement[] QueueRows()
        {
            var list = Window.FindFirstDescendant(cf => cf.ByAutomationId("Queue.List"))
                ?? throw new InvalidOperationException("Queue.List not found");
            return list.FindAllChildren(cf => cf.ByControlType(ControlType.ListItem));
        }

        private Point QueueRowCenter(int index)
        {
            var rows = QueueRows();
            Assert.True(rows.Length > index, $"queue has {rows.Length} rows");
            return Center(rows[index].BoundingRectangle);
        }

        public string[] QueueRowNames() => QueueRows().Select(r => r.Name).ToArray();

        public void RightClickQueueRow(int index)
        {
            Mouse.MoveTo(QueueRowCenter(index));
            Thread.Sleep(300);
            Mouse.RightClick();
            Thread.Sleep(600);
        }

        public void DoubleClickQueueRow(int index)
        {
            Mouse.MoveTo(QueueRowCenter(index));
            Thread.Sleep(200);
            Mouse.DoubleClick();
            Thread.Sleep(300);
        }

        /// <summary>
        /// Drags row <paramref name="from"/> onto the upper half of row <paramref name="to"/> (inserting before it).
        /// Uses SendInput for every step: OLE drag and drop (WPF DoDragDrop) cancels on the cursor jumps of SetCursorPos.
        /// </summary>
        public void DragQueueRow(int from, int to, string? shotDir = null)
        {
            var start = QueueRowCenter(from);
            var target = QueueRows()[to].BoundingRectangle;
            var end = new Point(target.Left + target.Width / 2, target.Top + target.Height / 4);
            Input.Move(start);
            Thread.Sleep(200);
            Input.Button(down: true);
            Thread.Sleep(100);
            const int steps = 20;
            for (var i = 1; i <= steps; i++)
            {
                Input.Move(new Point(start.X, start.Y + (end.Y - start.Y) * i / steps));
                Thread.Sleep(30);
            }

            for (var i = 0; i < 4; i++)
            {
                Input.Move(new Point(end.X + (i % 2 == 0 ? 2 : -2), end.Y));
                Thread.Sleep(50);
            }

            Input.Move(end);
            Thread.Sleep(250);
            if (shotDir is not null) Shot(shotDir, "10a-dark-queue-dragging", parkPointer: false);
            Input.Button(down: false);
            Thread.Sleep(400);
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

    /// <summary>Raw mouse input through SendInput (absolute moves, left button).</summary>
    private static class Input
    {
        private const uint InputMouse = 0;
        private const uint MouseMove = 0x0001, LeftDown = 0x0002, LeftUp = 0x0004, Absolute = 0x8000, VirtualDesk = 0x4000;

        public static void Move(Point p)
        {
            // Absolute coordinates are normalised to 0..65535 over the virtual desktop.
            int vx = GetSystemMetrics(76), vy = GetSystemMetrics(77), vw = GetSystemMetrics(78), vh = GetSystemMetrics(79);
            var nx = (int)Math.Round((p.X - vx) * 65535.0 / Math.Max(1, vw - 1));
            var ny = (int)Math.Round((p.Y - vy) * 65535.0 / Math.Max(1, vh - 1));
            Send(MouseMove | Absolute | VirtualDesk, nx, ny);
        }

        public static void Button(bool down) => Send(down ? LeftDown : LeftUp, 0, 0);

        private static void Send(uint flags, int x, int y)
        {
            var input = new INPUT { Type = InputMouse, Mouse = new MOUSEINPUT { Dx = x, Dy = y, Flags = flags } };
            if (SendInput(1, [input], Marshal.SizeOf<INPUT>()) != 1)
                throw new InvalidOperationException($"SendInput failed ({Marshal.GetLastWin32Error()})");
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int Dx;
            public int Dy;
            public uint MouseData;
            public uint Flags;
            public uint Time;
            public IntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint Type;
            public MOUSEINPUT Mouse;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint count, INPUT[] inputs, int size);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);
    }
}
