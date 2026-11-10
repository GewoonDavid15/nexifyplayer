using System.Globalization;

namespace Nexify.Core.Localization;

public sealed record LanguageInfo(string Code, string Name);

public interface ILocalizationService
{
    IReadOnlyList<LanguageInfo> Available { get; }
    string CurrentCode { get; }
    void SetLanguage(string code);

    /// <summary>Text for <paramref name="key"/>: current language, then English, then the key itself.</summary>
    string this[string key] { get; }

    string Format(string key, params object[] args);

    event EventHandler? LanguageChanged;
}

public static class LocalizationDefaults
{
    /// <summary>Saved language if any, otherwise "nl" for a Dutch UI culture, otherwise "en".</summary>
    public static string ResolveInitial(string? saved, CultureInfo ui)
    {
        if (!string.IsNullOrWhiteSpace(saved)) return saved;
        return ui.TwoLetterISOLanguageName == "nl" ? "nl" : "en";
    }
}
