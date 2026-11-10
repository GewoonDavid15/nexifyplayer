using System.Windows;
using Nexify.App.Services;
using Nexify.App.Shell;
using Nexify.Presentation;

namespace Nexify.App;

public partial class App : Application
{
    private readonly string[] _args;
    private AppServices? _services;

    public App(string[] args)
    {
        _args = args;
    }

    /// <summary>The composition root; null before <see cref="OnStartup"/>.</summary>
    public AppServices? Services => _services;

    /// <summary>Spec 4.1: logging, settings, language and theme, then show the window immediately; LibVLC loads in the background.</summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = AppServices.Create(_args, this);
        _services = services;

        var window = new MainWindow(services.Shell, services.Theme);
        WindowPlacement.Apply(window, services.Settings.Current.Window);
        window.Closing += (_, _) => SaveWindowPlacement(window);
        MainWindow = window;
        window.Show();
        services.Log.Information("Main window shown");

        services.StartEngine();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        _services = null;
        base.OnExit(e);
    }

    /// <summary>Saves the placement; a failure is logged and never blocks closing the window.</summary>
    private void SaveWindowPlacement(MainWindow window)
    {
        if (_services is not { } services)
        {
            return;
        }

        ShutdownSequence.Run(
            (step, ex) => services.Log.Error(ex, "Saving {Step} failed", step),
            ("window placement", () =>
            {
                if (WindowPlacement.Capture(window, window.LastNonMinimizedState) is { } bounds)
                {
                    services.Settings.Update(s => s with { Window = bounds });
                }
            }));
    }
}
