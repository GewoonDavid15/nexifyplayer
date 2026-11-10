using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Nexify.Core.Localization;
using Nexify.Infrastructure.Localization;
using Serilog;
using Xunit;

namespace Nexify.Infrastructure.Tests;

public class LocalizationTests
{
    private static string LanguagesDir => Path.Combine(AppContext.BaseDirectory, "Languages");

    private static Dictionary<string, string> Load(string code)
        => JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(LanguagesDir, code + ".json")))!;

    private static IEnumerable<string> Codes => Directory.GetFiles(LanguagesDir, "*.json").Select(f => Path.GetFileNameWithoutExtension(f));

    private static JsonLocalizationService Create(string dir) => new(dir, new LoggerConfiguration().CreateLogger());

    [Fact]
    public void AllLanguages_HaveExactlySameKeysAsEnglish()
    {
        var en = Load("en").Keys.OrderBy(k => k).ToList();
        Assert.NotEmpty(en);
        foreach (var code in Codes)
            Assert.Equal(en, Load(code).Keys.OrderBy(k => k).ToList());
    }

    [Fact]
    public void AllLanguages_PlaceholderIndexesMatchEnglish()
    {
        var en = Load("en");
        foreach (var code in Codes)
            foreach (var (key, text) in Load(code))
                Assert.Equal(Indexes(en[key]), Indexes(text));

        static List<string> Indexes(string s)
            => Regex.Matches(s, @"\{(\d+)\}").Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x).ToList();
    }

    [Fact]
    public void AllLanguages_HaveNoEmptyValues()
    {
        foreach (var code in Codes)
            foreach (var (key, text) in Load(code))
                Assert.False(string.IsNullOrWhiteSpace(text), $"{code}:{key}");
    }

    [Fact]
    public void Missing_FallsBackToEnglish_ThenKey()
    {
        using var t = new TempDir();
        File.WriteAllText(t.Combine("en.json"), """{"language.name":"English","a":"A-en","b":"B-en"}""");
        File.WriteAllText(t.Combine("nl.json"), """{"language.name":"Nederlands","a":"A-nl"}""");
        var s = Create(t.Path);
        s.SetLanguage("nl");
        Assert.Equal("A-nl", s["a"]);
        Assert.Equal("B-en", s["b"]);
        Assert.Equal("c", s["c"]);
    }

    [Fact]
    public void Format_AppliesArguments()
    {
        var s = Create(LanguagesDir);
        s.SetLanguage("nl");
        Assert.Equal("3 bestanden overgeslagen: formaat niet ondersteund.", s.Format("error.skippedUnsupported", 3));
        Assert.Equal("Bestand niet gevonden: x", s.Format("error.fileNotFound", "x"));
    }

    [Fact]
    public void SetLanguage_RaisesLanguageChanged()
    {
        var s = Create(LanguagesDir);
        var n = 0;
        s.LanguageChanged += (_, _) => n++;
        s.SetLanguage("nl");
        Assert.Equal("nl", s.CurrentCode);
        Assert.Equal(1, n);
    }

    [Fact]
    public void SetLanguage_Unknown_KeepsCurrent()
    {
        var s = Create(LanguagesDir);
        s.SetLanguage("xx");
        Assert.Equal("en", s.CurrentCode);
    }

    [Fact]
    public void ResolveInitial_DutchCulture_GivesNl()
        => Assert.Equal("nl", LocalizationDefaults.ResolveInitial(null, new CultureInfo("nl-BE")));

    [Fact]
    public void ResolveInitial_German_GivesEn()
        => Assert.Equal("en", LocalizationDefaults.ResolveInitial(null, new CultureInfo("de-DE")));

    [Fact]
    public void ResolveInitial_SavedWins()
        => Assert.Equal("en", LocalizationDefaults.ResolveInitial("en", new CultureInfo("nl-NL")));

    [Fact]
    public void Available_ContainsNlAndEnWithNames()
    {
        var s = Create(LanguagesDir);
        Assert.Contains(s.Available, l => l.Code == "nl" && l.Name == "Nederlands");
        Assert.Contains(s.Available, l => l.Code == "en" && l.Name == "English");
    }
}
