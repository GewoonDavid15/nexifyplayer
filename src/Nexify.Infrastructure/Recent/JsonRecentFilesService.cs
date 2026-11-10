using System.Text.Json;
using Nexify.Core.Media;
using Nexify.Core.Recent;
using Nexify.Infrastructure.Settings;
using Serilog;

namespace Nexify.Infrastructure.Recent;

public sealed class JsonRecentFilesService : IRecentFilesService
{
    public const int MaxItems = 20;

    private sealed record Store(int SchemaVersion, List<RecentFile> Items);

    private readonly AppPaths _paths;
    private readonly ILogger _log;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private List<RecentFile> _items = [];

    public JsonRecentFilesService(AppPaths paths, ILogger log, TimeProvider? time = null)
    {
        _paths = paths;
        _log = log;
        _time = time ?? TimeProvider.System;
    }

    public event EventHandler? Changed;

    public void Load()
    {
        var path = _paths.RecentFile;
        var loaded = new List<RecentFile>();

        if (File.Exists(path))
        {
            try
            {
                var store = JsonSerializer.Deserialize<Store>(File.ReadAllText(path), JsonSettingsService.JsonOptions);
                if (store is null) throw new JsonException("recent file contained null");
                loaded = (store.Items ?? [])
                    .Where(i => i is not null && !string.IsNullOrEmpty(i.Path))
                    .Take(MaxItems)
                    .ToList();
            }
            catch (JsonException ex)
            {
                _log.Warning(ex, "Recent files list is corrupt, starting empty");
                try { CorruptFile.Quarantine(path); }
                catch (Exception ioEx) { _log.Error(ioEx, "Could not preserve corrupt recent file"); }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.Error(ex, "Could not read recent files list, starting empty");
            }
        }

        lock (_gate) _items = loaded;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<RecentFile> GetVisible()
    {
        List<RecentFile> snapshot;
        lock (_gate) snapshot = [.. _items];
        return snapshot.Where(i => File.Exists(i.Path)).ToList();
    }

    public void Add(MediaItem item)
    {
        lock (_gate)
        {
            _items.RemoveAll(i => SamePath(i.Path, item.Path));
            _items.Insert(0, new RecentFile(item.Path, item.Kind, _time.GetUtcNow()));
            if (_items.Count > MaxItems) _items.RemoveRange(MaxItems, _items.Count - MaxItems);
            Save();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Remove(string path)
    {
        bool removed;
        lock (_gate)
        {
            removed = _items.RemoveAll(i => SamePath(i.Path, path)) > 0;
            if (removed) Save();
        }
        if (removed) Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        lock (_gate)
        {
            _items.Clear();
            Save();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static bool SamePath(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(new Store(1, _items), JsonSettingsService.JsonOptions);
            AtomicFileWriter.WriteAllText(_paths.RecentFile, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Error(ex, "Could not save recent files list");
        }
    }
}
