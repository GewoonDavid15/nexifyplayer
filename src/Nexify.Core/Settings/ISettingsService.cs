namespace Nexify.Core.Settings;

public interface ISettingsService
{
    AppSettings Current { get; }

    /// <summary>True when the last <see cref="Load"/> found an unreadable file and reset to defaults.</summary>
    bool LoadedFromCorruptFile { get; }

    void Load();

    /// <summary>Applies the change and saves immediately.</summary>
    void Update(Func<AppSettings, AppSettings> change);

    /// <summary>Applies the change now and saves 500 ms after the last call.</summary>
    void UpdateDebounced(Func<AppSettings, AppSettings> change);

    /// <summary>Writes any pending debounced change immediately.</summary>
    void Flush();

    event EventHandler<AppSettings>? Changed;
}
