using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Nexify.Core.Localization;
using Serilog;

namespace Nexify.Infrastructure.Localization;

public sealed class JsonLocalizationService : ILocalizationService
{
    private const string Fallback = "en";
    private const string NameKey = "language.name";

    private readonly ILogger _log;
    private readonly Dictionary<string, Dictionary<string, string>> _languages = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _warned = new();
    private string _current;

    public JsonLocalizationService(string languagesDir, ILogger log)
    {
        _log = log;
        if (Directory.Exists(languagesDir))
        {
            foreach (var file in Directory.EnumerateFiles(languagesDir, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var code = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                try
                {
                    var map = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file));
                    if (map is not null) _languages[code] = map;
                }
                catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
                {
                    _log.Error(ex, "Could not load language file {File}", file);
                }
            }
        }
        else
        {
            _log.Error("Languages directory not found: {Dir}", languagesDir);
        }

        Available = _languages
            .Select(l => new LanguageInfo(l.Key, l.Value.TryGetValue(NameKey, out var n) ? n : l.Key))
            .OrderBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        _current = _languages.ContainsKey(Fallback) ? Fallback : _languages.Keys.FirstOrDefault() ?? Fallback;
    }

    public IReadOnlyList<LanguageInfo> Available { get; }

    public string CurrentCode => _current;

    public event EventHandler? LanguageChanged;

    public void SetLanguage(string code)
    {
        code = code.ToLowerInvariant();
        if (!_languages.ContainsKey(code))
        {
            _log.Warning("Unknown language {Code}, keeping {Current}", code, _current);
            return;
        }
        _current = code;
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public string this[string key]
    {
        get
        {
            if (_languages.TryGetValue(_current, out var cur) && cur.TryGetValue(key, out var text)) return text;
            if (_current != Fallback && _languages.TryGetValue(Fallback, out var en) && en.TryGetValue(key, out text))
            {
                WarnOnce(key);
                return text;
            }
            WarnOnce(key);
            return key;
        }
    }

    public string Format(string key, params object[] args)
    {
        var template = this[key];
        try { return string.Format(CultureInfo.CurrentCulture, template, args); }
        catch (FormatException)
        {
            _log.Warning("Bad format string for key {Key}", key);
            return template;
        }
    }

    private void WarnOnce(string key)
    {
        if (_warned.TryAdd($"{_current}|{key}", 0))
            _log.Warning("Missing translation for {Key} in {Language}", key, _current);
    }
}
