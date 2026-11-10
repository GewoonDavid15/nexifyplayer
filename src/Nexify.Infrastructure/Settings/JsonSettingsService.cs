using System.Text.Json;
using System.Text.Json.Serialization;
using Nexify.Core.Settings;
using Serilog;

namespace Nexify.Infrastructure.Settings;

public sealed class JsonSettingsService : ISettingsService, IDisposable
{
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(500);

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
    };

    private readonly AppPaths _paths;
    private readonly ILogger _log;
    private readonly object _gate = new();
    private readonly ITimer _timer;
    private AppSettings _current = new();
    private bool _pending;

    public JsonSettingsService(AppPaths paths, ILogger log, TimeProvider? time = null)
    {
        _paths = paths;
        _log = log;
        _timer = (time ?? TimeProvider.System).CreateTimer(OnTimer, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public AppSettings Current { get { lock (_gate) return _current; } }

    public bool LoadedFromCorruptFile { get; private set; }

    public event EventHandler<AppSettings>? Changed;

    public void Load()
    {
        var path = _paths.SettingsFile;
        LoadedFromCorruptFile = false;
        AppSettings loaded = new();

        if (File.Exists(path))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions);
                if (parsed is null) throw new JsonException("settings file contained null");
                loaded = parsed.Normalized();
            }
            catch (JsonException ex)
            {
                _log.Warning(ex, "Settings file is corrupt, resetting to defaults");
                try { CorruptFile.Quarantine(path); }
                catch (Exception ioEx) { _log.Error(ioEx, "Could not preserve corrupt settings file"); }
                LoadedFromCorruptFile = true;
            }
            catch (IOException ex)
            {
                _log.Error(ex, "Could not read settings file, using defaults");
            }
            catch (UnauthorizedAccessException ex)
            {
                _log.Error(ex, "No access to settings file, using defaults");
            }
        }

        lock (_gate)
        {
            _pending = false;
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _current = loaded;
        }
    }

    public void Update(Func<AppSettings, AppSettings> change)
    {
        var updated = Apply(change);
        _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        lock (_gate) _pending = false;
        Save(updated);
        Changed?.Invoke(this, updated);
    }

    public void UpdateDebounced(Func<AppSettings, AppSettings> change)
    {
        AppSettings updated;
        lock (_gate)
        {
            updated = _current = change(_current).Normalized();
            _pending = true;
        }
        _timer.Change(DebounceDelay, Timeout.InfiniteTimeSpan);
        Changed?.Invoke(this, updated);
    }

    public void Flush()
    {
        AppSettings toSave;
        lock (_gate)
        {
            if (!_pending) return;
            _pending = false;
            toSave = _current;
        }
        _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        Save(toSave);
    }

    public void Dispose() => _timer.Dispose();

    private AppSettings Apply(Func<AppSettings, AppSettings> change)
    {
        lock (_gate) return _current = change(_current).Normalized();
    }

    private void OnTimer(object? state) => Flush();

    private void Save(AppSettings settings)
    {
        try
        {
            AtomicFileWriter.WriteAllText(_paths.SettingsFile, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Error(ex, "Could not save settings");
        }
    }
}
