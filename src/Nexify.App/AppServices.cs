using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using Nexify.App.Localization;
using Nexify.App.Services;
using Nexify.Core.Localization;
using Nexify.Core.Media;
using Nexify.Core.Playback;
using Nexify.Core.Queue;
using Nexify.Core.Settings;
using Nexify.Infrastructure;
using Nexify.Infrastructure.Localization;
using Nexify.Infrastructure.Recent;
using Nexify.Infrastructure.Settings;
using Nexify.Playback;
using Nexify.Presentation;
using Serilog;

namespace Nexify.App;

/// <summary>
/// Composition root without a DI container: creates and owns every long-lived service and view model.
/// Built by <see cref="App.OnStartup"/> in the spec 4.1 order; disposed on exit.
/// </summary>
public sealed class AppServices : IDisposable
{
    private bool _disposed;

    private AppServices(
        string[] args,
        AppPaths paths,
        ILogger log,
        JsonSettingsService settings,
        JsonRecentFilesService recent,
        JsonLocalizationService localization,
        ThemeService theme,
        IUiDispatcher dispatcher,
        VlcPlaybackEngine engine,
        ShellViewModel shell,
        PlayQueue queue,
        NotificationsViewModel notifications,
        PlayerViewModel player)
    {
        Args = args;
        Paths = paths;
        Log = log;
        Settings = settings;
        Recent = recent;
        Localization = localization;
        Theme = theme;
        Dispatcher = dispatcher;
        Engine = engine;
        Shell = shell;
        Queue = queue;
        Notifications = notifications;
        Player = player;
    }

    /// <summary>Command-line arguments (file or folder paths), opened once the engine is ready.</summary>
    public string[] Args { get; }

    public AppPaths Paths { get; }
    public ILogger Log { get; }
    public JsonSettingsService Settings { get; }
    public JsonRecentFilesService Recent { get; }
    public ILocalizationService Localization { get; }
    public ThemeService Theme { get; }
    public IUiDispatcher Dispatcher { get; }
    public IPlaybackEngine Engine { get; }
    public ShellViewModel Shell { get; }
    public PlayQueue Queue { get; }
    public NotificationsViewModel Notifications { get; }
    public PlayerViewModel Player { get; }

    /// <summary>Completes when LibVLC has loaded (or failed to; see <see cref="IPlaybackEngine.InitializeAsync"/>).</summary>
    public Task EngineReady { get; private set; } = Task.CompletedTask;

    public static AppServices Create(string[] args, Application app)
    {
        var paths = AppPaths.Resolve();
        paths.EnsureCreated();
        var log = Logging.Configure(paths);
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        log.Information("Nexify Player {Version} starting (data {DataDir})", version, paths.DataDir);

        var settings = new JsonSettingsService(paths, log);
        settings.Load();
        var recent = new JsonRecentFilesService(paths, log);
        recent.Load();

        var localization = new JsonLocalizationService(Path.Combine(AppContext.BaseDirectory, "Languages"), log);
        localization.SetLanguage(LocalizationDefaults.ResolveInitial(settings.Current.Language, CultureInfo.CurrentUICulture));
        if (!string.Equals(settings.Current.Language, localization.CurrentCode, StringComparison.OrdinalIgnoreCase))
        {
            settings.Update(s => s with { Language = localization.CurrentCode });
        }

        LocProxy.Initialize(localization);

        var theme = new ThemeService(app.Resources, app.Dispatcher);
        theme.Apply(settings.Current.Theme);

        var dispatcher = new WpfDispatcher(app.Dispatcher);
        var engine = new VlcPlaybackEngine(log);
        var shell = new ShellViewModel();
        var queue = new PlayQueue();
        var notifications = new NotificationsViewModel(dispatcher);
        var player = new PlayerViewModel(engine, queue, settings, recent, localization, notifications, dispatcher);

        return new AppServices(args, paths, log, settings, recent, localization, theme, dispatcher, engine, shell, queue, notifications, player);
    }

    /// <summary>Starts loading LibVLC on a background thread without blocking the UI (spec 4.1 step 2).</summary>
    public void StartEngine()
    {
        EngineReady = StartEngineAsync();
    }

    private async Task StartEngineAsync()
    {
        await Player.InitializeEngineAsync().ConfigureAwait(false);
        Dispatcher.Post(OpenStartupArguments);
    }

    /// <summary>Spec 4.1 step 3: files given on the command line play once the engine is ready.</summary>
    // Replaced by MediaOpener in Task 13 (folders, unsupported-file summary, open behaviour).
    private void OpenStartupArguments()
    {
        var items = Args
            .Select(arg => MediaFormats.TryCreate(Path.GetFullPath(arg)))
            .OfType<MediaItem>()
            .ToList();
        if (items.Count == 0)
        {
            return;
        }

        Log.Information("Opening {Count} file(s) from the command line", items.Count);
        _ = Player.PlayItemsAsync(items, OpenBehavior.Replace);
    }

    /// <summary>Flushes settings, stops following the Windows theme, releases LibVLC and flushes the log.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Each step is isolated: a failing step is logged and never skips the later ones; the log is flushed last.
        ShutdownSequence.Run(
            (step, ex) => Log.Error(ex, "Dispose {Step} failed", step),
            ("log-start", () => Log.Information("Nexify Player shutting down")),
            ("settings", Settings.Dispose),
            ("theme", Theme.Dispose),
            ("engine", Engine.Dispose));

        // Final step, always reached because ShutdownSequence never throws.
        Serilog.Log.CloseAndFlush();
    }
}
