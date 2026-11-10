# Nexify Player — Deelproject 1: Basis — Implementatieplan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Een werkende, geteste WPF-basis voor Nexify Player: bestanden en mappen openen, audio en video afspelen met wachtrij en bedieningsbalk, in de Next Solutions-huisstijl, met thema's, twee talen, opgeslagen instellingen en een Windows-CI-build voor x64 en x86.

**Architecture:** Gelaagde .NET 8-oplossing.
- `Nexify.Core` bevat modellen en logica zonder UI.
- `Nexify.Infrastructure` regelt opslag, logging en lokalisatie.
- `Nexify.Playback` verpakt LibVLCSharp achter `IPlaybackEngine`.
- `Nexify.Presentation` bevat UI-loze viewmodels (CommunityToolkit.Mvvm) die op Linux testbaar zijn.
- `Nexify.App` is de WPF-schil.
- GitHub Actions op `windows-latest` bouwt, test en publiceert.

**Tech Stack:** .NET 8 (`net8.0` / `net8.0-windows10.0.19041.0`), WPF, LibVLCSharp.WPF 3.9.4, VideoLAN.LibVLC.Windows 3.0.21, CommunityToolkit.Mvvm 8.x, Serilog (File-sink), xUnit, FlaUI.UIA3, ffmpeg (alleen in CI).

**Spec:** `docs/superpowers/specs/2026-11-10-nexify-basis-design.md`

## Global Constraints

- **Target frameworks:**
  - Core, Infrastructure, Presentation en de bijbehorende tests: `net8.0`.
  - Playback en App: `net8.0-windows10.0.19041.0`.
  - Alle projecten krijgen `EnableWindowsTargeting=true`, zodat ze op Linux compileren.
- **LibVLCSharp 3.9.x trekt `Microsoft.WindowsAppSDK` mee.** Dat breekt de build (onbekende `win10-*` RID's). Elk project dat LibVLCSharp gebruikt, krijgt daarom:
  `<PackageReference Include="Microsoft.WindowsAppSDK" Version="1.5.240627000" ExcludeAssets="all" PrivateAssets="all" />`.
  Dit is lokaal geverifieerd.
- **Publiceren:** `win-x64` en `win-x86`, self-contained, `PublishReadyToRun=true`.
  - Bij `win-x86`: `VlcWindowsX64Enabled=false`.
  - Bij `win-x64`: `VlcWindowsX86Enabled=false`.
  - Zo bevat elke publicatie alleen de LibVLC-versie die bij de architectuur past.
- Versie `0.1.0` in `Directory.Build.props`. `Nullable=enable`, `TreatWarningsAsErrors=true` voor Core, Infrastructure, Presentation en Playback.
- **Gegevensmap:** `%APPDATA%\Nexify Player\`; logs in `%LOCALAPPDATA%\Nexify Player\logs\`. De omgevingsvariabele `NEXIFY_DATA_DIR` vervangt beide (logs komen dan in `<dir>\logs`).
- **Kleuren, lettertypen, maten en teksten** exact volgens spec §3.2–3.6 en §5. Gebruikersteksten staan in `nl.json`/`en.json` en nooit hardcoded in XAML of C#.
- **Geen menu-onderdelen of knoppen zonder werkende functie.** De zijbalk bevat in dit deelproject alleen Home, Nu speelt en Instellingen.
- Niet automatisch verdergaan met het laatst afgespeelde item.
- **Commits:** elk commitbericht eindigt met de twee attributieregels uit de sessie (`Co-Authored-By: …` en `Claude-Session: …`).

**Afwijking van de spec (bewust):** viewmodels staan in een apart project `Nexify.Presentation` (`net8.0`, geen WPF-referenties), zodat de viewmodel-tests uit spec §6.1 ook op Linux draaien. UI-thread-marshalling gaat via een `IUiDispatcher`-interface.

## Review Focus

1. **LibVLC-events komen op een andere thread binnen.** Een engine die vanuit een VLC-callback weer LibVLC aanroept (bijvoorbeeld `Play` vanuit `EndReached`), loopt vast. De gebruiker verwacht dat het volgende nummer gewoon start. *Test:* Task 9, `EndReached_ThenOpenNext_PlaysSecondFile`.
2. **Snel achter elkaar op *Volgende* klikken.** Na 10× klikken verwacht de gebruiker het 10e volgende item, zonder vastloper of verkeerde titel. *Test:* Task 9, `RapidOpen_LastOneWins`.
3. **Paden met unicode, spaties en `#` of `%`** (zoals `C:\Muziek\Beyoncé #1 100%.mp3`) moeten afspelen. LibVLC verwerkt paden als URI. *Test:* Task 9, `Plays_PathWithSpecialCharacters`.
4. **Venster wordt geopend op een scherm dat er niet meer is**, bijvoorbeeld na het loskoppelen van een tweede monitor. De gebruiker verwacht het venster zichtbaar op het hoofdscherm. *Test:* Task 11, `WindowPlacement_OffscreenBounds_ResetToPrimary`.
5. **Een map met duizenden bestanden of een onbereikbare netwerkmap** mag de UI niet bevriezen. *Test:* Task 4, `Scan_IsCancellable_AndRunsOffCallerThread`; Task 13 laat het scannen op de achtergrond draaien met een laadindicator.

---

## Bestandsstructuur

```
Directory.Build.props            Gedeelde eigenschappen, versie
global.json                      SDK 8.0.x
NexifyPlayer.sln
.gitignore / .gitattributes
src/Nexify.Core/
  Media/MediaKind.cs, MediaItem.cs, MediaFormats.cs
  Media/FolderScanner.cs, ScanResult.cs, NaturalStringComparer.cs
  Playback/IPlaybackEngine.cs, PlaybackState.cs, PlaybackError.cs, PlaybackErrorClassifier.cs
  Queue/PlayQueue.cs, RepeatMode.cs
  Settings/AppSettings.cs, ISettingsService.cs
  Recent/RecentFile.cs, IRecentFilesService.cs
  Localization/ILocalizationService.cs
src/Nexify.Infrastructure/
  AppPaths.cs, AtomicFileWriter.cs, Logging.cs
  Settings/JsonSettingsService.cs
  Recent/JsonRecentFilesService.cs
  Localization/JsonLocalizationService.cs
src/Nexify.Playback/
  VlcPlaybackEngine.cs
src/Nexify.Presentation/
  IUiDispatcher.cs, INotificationService.cs, IFileDialogService.cs, IThemeService.cs
  PlayerViewModel.cs, QueueItemViewModel.cs
  HomeViewModel.cs, SettingsViewModel.cs, ShellViewModel.cs, NotificationsViewModel.cs
  MediaOpener.cs
src/Nexify.App/
  App.xaml(.cs), Program.cs (Main + opstartparameters)
  Languages/nl.json, en.json
  Themes/Tokens.Dark.xaml, Tokens.Light.xaml, Controls.xaml, Typography.xaml
  Assets/Fonts/ (Inter, Archivo .ttf), Assets/nexify.ico, Assets/nexify-mark.png
  Services/ThemeService.cs, WpfDispatcher.cs, FileDialogService.cs, WindowPlacement.cs
  Localization/LocExtension.cs
  Shell/MainWindow.xaml(.cs), TitleBar.xaml(.cs), Sidebar.xaml(.cs), WindowChromeHelper.cs
  Player/TransportBar.xaml(.cs), SeekSlider.cs
  Pages/HomePage.xaml, NowPlayingPage.xaml, SettingsPage.xaml
  Notifications/ToastHost.xaml
tests/Nexify.Core.Tests/, Nexify.Infrastructure.Tests/, Nexify.Presentation.Tests/
tests/Nexify.Playback.Tests/      (Windows; [WindowsFact])
tests/Nexify.App.UiTests/         (Windows; FlaUI)
tests/media/generate-test-media.ps1
assets/branding/nexify-logo.svg, nexify-mark.svg, nexify.ico
tools/make-icon.py               SVG → .ico/.png (cairosvg + Pillow)
.github/workflows/build.yml
docs/BUILD.md, docs/TESTING.md, README.md, THIRD-PARTY-NOTICES.md
```

**Lokale verificatie (Linux):** `export PATH=/root/.dotnet:$PATH` en daarna `dotnet build NexifyPlayer.sln` en `dotnet test tests/Nexify.Core.Tests tests/Nexify.Infrastructure.Tests tests/Nexify.Presentation.Tests`. Playback- en UI-tests draaien alleen in CI.

---

### Task 1: Solution-skelet en CI-basis

**Files:**
- Create: `Directory.Build.props`, `global.json`, `NexifyPlayer.sln`, `.gitignore`, `.gitattributes`.
- Create: alle `*.csproj` uit de bestandsstructuur, nog zonder code (lege projecten compileren). Code volgt in de eigen taak.
- Create: `.github/workflows/build.yml`. Eerste versie: restore, build en de drie Linux-veilige testprojecten.

**Interfaces:**
- Produces: projectnamen en -referenties:
  - Core ← Infrastructure, Playback, Presentation;
  - Presentation → Core;
  - App → alle;
  - testprojecten → hun doelproject.
- `Nexify.App` heeft `OutputType=WinExe`, `UseWPF=true`, `AssemblyName=NexifyPlayer`, `ApplicationIcon=Assets\nexify.ico` (icoon volgt in Task 10; voeg de property dan pas toe).

- [ ] **Step 1:** Maak de bestanden aan. `Directory.Build.props` bevat `Version=0.1.0`, `Company=Next Solutions`, `Product=Nexify Player`, `LangVersion=latest`, `Nullable=enable`, `ImplicitUsings=enable` en `EnableWindowsTargeting=true`. `global.json` zet `"sdk": { "version": "8.0.400", "rollForward": "latestFeature" }`.
- [ ] **Step 2:** Voeg in Playback en App de LibVLC-pakketten en de WindowsAppSDK-uitsluiting uit de Global Constraints toe. App krijgt ook `CommunityToolkit.Mvvm`, Presentation krijgt `CommunityToolkit.Mvvm`, en Infrastructure krijgt `Serilog` en `Serilog.Sinks.File`.
- [ ] **Step 3:** Run `dotnet build NexifyPlayer.sln`. Verwacht: `0 Error(s)`.
- [ ] **Step 4:** Commit: `chore: solution-skelet en CI-basis`.

---

### Task 2: Mediamodellen en formaatdetectie

**Files:**
- Create: `src/Nexify.Core/Media/MediaKind.cs`, `MediaItem.cs`, `MediaFormats.cs`
- Test: `tests/Nexify.Core.Tests/MediaFormatsTests.cs`

**Interfaces:**
- Produces:
  - `enum MediaKind { Audio, Video }`
  - `sealed record MediaItem(string Path, MediaKind Kind) { string DisplayName { get; } }`. `DisplayName` is de bestandsnaam zonder extensie. Er komt ook een `Guid Id`, uniek per instantie, zodat hetzelfde bestand twee keer in de wachtrij kan staan; `Id` wordt in de constructor gezet en telt niet mee in de gelijkheid.
  - `static class MediaFormats { IReadOnlySet<string> AudioExtensions; IReadOnlySet<string> VideoExtensions; MediaKind? Detect(string path); bool IsSupported(string path); MediaItem? TryCreate(string path); }`
  - Extensielijsten exact volgens spec §4.2, met punt en in kleine letters.

- [ ] **Step 1: Schrijf de tests**
  - `[Theory]` `Detect_KnownAudio_ReturnsAudio`, met `.mp3 .wav .flac .aac .m4a .ogg .oga .opus .wma .alac`;
  - `Detect_KnownVideo_ReturnsVideo`, met `.mp4 .m4v .mkv .avi .mov .wmv .webm .mpeg .mpg .ts .m2ts`;
  - `Detect_IsCaseInsensitive` (`"A.MP3"` → Audio);
  - `Detect_Unknown_ReturnsNull` (`.txt`, `.srt`, `""`, `"noext"`, `"map."`);
  - `TryCreate_SetsDisplayName` (`@"C:\a\Song One.flac"` → `"Song One"`);
  - `TwoItemsSamePath_HaveDifferentIds`.
- [ ] **Step 2:** Run `dotnet test tests/Nexify.Core.Tests`. Verwacht: compileerfout of FAIL.
- [ ] **Step 3:** Implementeer. Gebruik `Path.GetExtension` met `StringComparer.OrdinalIgnoreCase`-sets.
- [ ] **Step 4:** Run opnieuw. Verwacht: PASS.
- [ ] **Step 5:** Commit: `feat(core): mediamodellen en formaatdetectie`.

---

### Task 3: Wachtrij (`PlayQueue`)

**Files:**
- Create: `src/Nexify.Core/Queue/PlayQueue.cs`, `RepeatMode.cs`
- Test: `tests/Nexify.Core.Tests/PlayQueueTests.cs`

**Interfaces:**
- Produces:
  - `enum RepeatMode { Off, All, One }`
  - `sealed class PlayQueue`, met de constructor `PlayQueue(Random? random = null)`. De `Random` is injecteerbaar zodat tests deterministisch zijn.
  - Leden:
    - `IReadOnlyList<MediaItem> Items`, `int CurrentIndex` (−1 = geen), `MediaItem? Current`;
    - `bool Shuffle { get; set; }`, `RepeatMode Repeat { get; set; }`;
    - `void Add(MediaItem)`, `void AddRange(IEnumerable<MediaItem>)`, `void InsertNext(MediaItem)`;
    - `void Remove(MediaItem)` (op basis van `Id`), `void Move(int from, int to)`, `void Clear()`;
    - `void ReplaceWith(IEnumerable<MediaItem>)` (wist de wachtrij, vult hem en zet `CurrentIndex` op 0, of −1 als hij leeg is);
    - `MediaItem? MoveTo(int index)`, `MediaItem? Next()`, `MediaItem? Previous()`;
    - `MediaItem? NextForEndReached()`;
    - `bool HasNext`, `bool HasPrevious`;
    - `event EventHandler? Changed`.
  - **`Next()`** volgt shuffle en repeat. Met `Repeat.All` gaat hij aan het einde terug naar het begin; met shuffle krijgt hij dan een nieuwe volgorde waarvan het eerste item niet gelijk is aan het laatst gespeelde, als er meer dan één item is. Met `Repeat.Off` geeft hij aan het einde `null` en blijft `CurrentIndex` staan.
  - **`NextForEndReached()`** geeft bij `Repeat.One` het huidige item terug, en gedraagt zich anders als `Next()`.
  - **`Previous()`** aan het begin: geeft bij `Repeat.All` het laatste item, anders `null`. De regel van 3 seconden hoort bij de PlayerViewModel, niet hier.
  - **Shuffle** werkt via een interne lijst `_order` met indexen (Fisher-Yates). Het huidige item staat bij het aanzetten vooraan, en `Items` behoudt de zichtbare volgorde.
  - **`Remove` van het huidige item:** `Current` wordt het item dat erna komt in de afspeelvolgorde, of het vorige als het laatste werd verwijderd. Een lege wachtrij geeft `CurrentIndex = −1`. De speler bepaalt zelf of hij doorspeelt (Task 12).
  - **`Move`** houdt `Current` op hetzelfde item.

- [ ] **Step 1: Schrijf de tests**
  - `Add_FirstItem_DoesNotSetCurrent_UntilMoveTo`
  - `ReplaceWith_SetsCurrentToFirst`
  - `Next_AtEnd_RepeatOff_ReturnsNull`
  - `Next_AtEnd_RepeatAll_WrapsToFirst`
  - `NextForEndReached_RepeatOne_ReturnsSameItem`
  - `Next_RepeatOne_ManualNextAdvances`
  - `Previous_AtStart_RepeatOff_ReturnsNull`
  - `Previous_AtStart_RepeatAll_ReturnsLast`
  - `InsertNext_PlacesAfterCurrent` (ook met shuffle aan: het ingevoegde item is het volgende in de afspeelvolgorde)
  - `Move_KeepsCurrentItem`
  - `Remove_Current_SelectsFollowingItem`
  - `Remove_LastRemaining_SetsIndexMinusOne`
  - `Shuffle_On_VisitsEveryItemExactlyOnce` (20 items, `new Random(42)`, 19× `Next()`, de set van bezochte `Id`'s heeft precies 20 elementen)
  - `Shuffle_On_KeepsCurrentFirst`
  - `Shuffle_Off_ContinuesFromCurrentInListOrder`
  - `Shuffle_RepeatAll_Wrap_DoesNotRepeatLastImmediately`
  - `SameFileTwice_RemoveRemovesOnlyOne`
  - `Changed_RaisedOnEveryMutation`
- [ ] **Step 2:** Run de tests. Verwacht: FAIL.
- [ ] **Step 3:** Implementeer `PlayQueue`.
- [ ] **Step 4:** Run de tests. Verwacht: PASS.
- [ ] **Step 5:** Commit: `feat(core): wachtrij met shuffle en herhalen`.

---

### Task 4: Mappen scannen

**Files:**
- Create: `src/Nexify.Core/Media/FolderScanner.cs`, `ScanResult.cs`, `NaturalStringComparer.cs`
- Test: `tests/Nexify.Core.Tests/FolderScannerTests.cs`, `NaturalStringComparerTests.cs`

**Interfaces:**
- Produces:
  - `sealed class NaturalStringComparer : IComparer<string>` met `static Instance`. Getallenreeksen worden numeriek vergeleken, de rest ordinaal en hoofdletterongevoelig.
  - `sealed record ScanResult(IReadOnlyList<MediaItem> Items, int UnsupportedCount, IReadOnlyList<string> InaccessibleFolders)`
  - `static class FolderScanner`, met
    - `Task<ScanResult> ScanAsync(string folder, CancellationToken ct = default)`;
    - `ScanResult ClassifyPaths(IEnumerable<string> paths)`, voor bestanden en mappen samen (een losgelaten selectie bij slepen of opstartparameters). Een map daarin wordt synchroon gescand; voor de UI-thread gebruik je `ClassifyPathsAsync`;
    - `Task<ScanResult> ClassifyPathsAsync(IEnumerable<string> paths, CancellationToken ct = default)`.
  - **Gedrag:**
    - per map eerst de submappen (natuurlijk gesorteerd, recursief), daarna de bestanden (natuurlijk gesorteerd);
    - mappen met `FileAttributes.Hidden` of `FileAttributes.System` worden overgeslagen;
    - `UnauthorizedAccessException`, `IOException` en `DirectoryNotFoundException` op een map voegen die map toe aan `InaccessibleFolders` en het scannen gaat door;
    - `ScanAsync` draait via `Task.Run` en controleert `ct` per map;
    - bestanden die niet ondersteund worden, tellen mee in `UnsupportedCount`. Bestanden die met een punt beginnen of verborgen zijn, worden stil overgeslagen en tellen niet mee.

- [ ] **Step 1: Schrijf de tests** (met een echte tijdelijke map per test)
  - `Natural_SortsNumbersNumerically` (`["10.mp3","2.mp3","1.mp3"]` → `["1.mp3","2.mp3","10.mp3"]`)
  - `Natural_IsCaseInsensitive`
  - `Scan_FindsFilesRecursively_FoldersBeforeFiles` (`root/b.mp3`, `root/A/x.mp3` → `A/x.mp3` komt eerst)
  - `Scan_CountsUnsupported` (`a.mp3`, `b.txt`, `c.srt` → 1 item en `UnsupportedCount` 2)
  - `Scan_SkipsHiddenFolders` (map `.hidden` en een map met het Hidden-attribuut via `File.SetAttributes`)
  - `Scan_NonexistentFolder_ReportsInaccessible_NoThrow`
  - `Scan_IsCancellable_AndRunsOffCallerThread` (al geannuleerde token → `OperationCanceledException`; Thread-id in een callback is niet nodig; controleer dat `ScanAsync` een niet-voltooide `Task` teruggeeft bij een map met 2000 bestanden)
  - `ClassifyPaths_MixedFilesAndFolders_PreservesGivenOrder`
- [ ] **Step 2:** Run de tests. Verwacht: FAIL.
- [ ] **Step 3:** Implementeer.
- [ ] **Step 4:** Run de tests. Verwacht: PASS.
- [ ] **Step 5:** Commit: `feat(core): recursief mappen scannen met natuurlijke sortering`.

---

### Task 5: AppPaths, atomisch schrijven en logging

**Files:**
- Create: `src/Nexify.Infrastructure/AppPaths.cs`, `AtomicFileWriter.cs`, `Logging.cs`
- Test: `tests/Nexify.Infrastructure.Tests/AppPathsTests.cs`, `AtomicFileWriterTests.cs`

**Interfaces:**
- Produces:
  - `sealed class AppPaths`, met
    - `AppPaths(string dataDir, string logDir)`;
    - `static AppPaths Resolve()` (leest `NEXIFY_DATA_DIR`, anders `Environment.SpecialFolder.ApplicationData`/`LocalApplicationData` + `"Nexify Player"`);
    - `string DataDir`, `string LogDir`, `string SettingsFile` (`settings.json`), `string RecentFile` (`recent.json`);
    - `void EnsureCreated()`.
  - `static class AtomicFileWriter`, met
    - `void WriteAllText(string path, string contents)`: schrijft naar `path + ".tmp"`, flusht naar schijf en doet daarna `File.Replace(tmp, path, null)` als `path` bestaat, anders `File.Move(tmp, path)`;
    - `internal static Action<string>? FailAfterTempWriteForTests` als testhaak.
  - `static class Logging { static ILogger Configure(AppPaths paths); }`: Serilog File-sink naar `nexify-.log`, `rollingInterval: Day`, `retainedFileCountLimit: 7`, minimumniveau Information. Geeft `Serilog.ILogger` terug en zet `Log.Logger`.

- [ ] **Step 1: Schrijf de tests**
  - `Resolve_UsesEnvOverride` (env `NEXIFY_DATA_DIR=/tmp/x` → `DataDir=/tmp/x`, `LogDir=/tmp/x/logs`)
  - `Write_NewFile_CreatesIt`
  - `Write_Existing_ReplacesContents`
  - `Write_FailureAfterTemp_LeavesOriginalIntact` (de testhaak gooit een uitzondering; het origineel bevat nog de oude inhoud)
  - `Write_LeavesNoTempFileOnSuccess`
- [ ] **Step 2:** Run. Verwacht: FAIL.
- [ ] **Step 3:** Implementeer. Gebruik `[assembly: InternalsVisibleTo("Nexify.Infrastructure.Tests")]`.
- [ ] **Step 4:** Run. Verwacht: PASS.
- [ ] **Step 5:** Commit: `feat(infra): gegevenspaden, atomisch schrijven en logging`.

---

### Task 6: Instellingen

**Files:**
- Create: `src/Nexify.Core/Settings/AppSettings.cs`, `ISettingsService.cs`
- Create: `src/Nexify.Infrastructure/Settings/JsonSettingsService.cs`
- Test: `tests/Nexify.Infrastructure.Tests/JsonSettingsServiceTests.cs`

**Interfaces:**
- Produces (Core):
  - `enum ThemeMode { Dark, Light, System }`, `enum OpenBehavior { Replace, Append }`
  - `sealed record WindowBounds(double Left, double Top, double Width, double Height, bool IsMaximized)`
  - `sealed record AppSettings`, met de standaardwaarden:
    - `SchemaVersion = 1`, `Theme = Dark`, `Language = null` (null = automatisch bij de eerste start);
    - `Volume = 80`, `DefaultVolume = 80` (volume bij opstarten), `IsMuted = false`, `SeekStepSeconds = 5` (toegestaan: 5, 10 of 30; een andere waarde wordt 5), `OpenBehavior = Replace`;
    - `Shuffle = false`, `Repeat = RepeatMode.Off`, `Window = null`.
    - `Volume` en `DefaultVolume` worden bij het laden begrensd op 0–100.
  - `interface ISettingsService`, met
    - `AppSettings Current { get; }`, `bool LoadedFromCorruptFile { get; }`;
    - `void Load()`, `void Update(Func<AppSettings, AppSettings> change)` (slaat direct op);
    - `void UpdateDebounced(Func<AppSettings, AppSettings> change)` (wacht 500 ms), `void Flush()`;
    - `event EventHandler<AppSettings>? Changed`.
- Produces (Infrastructure): `sealed class JsonSettingsService(AppPaths paths, ILogger log, TimeProvider? time = null) : ISettingsService`.
  - `System.Text.Json` met `JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = CamelCase, Converters = { JsonStringEnumConverter(CamelCase) } }` en `UnmappedMemberHandling` op `Skip`.
  - **Beschadigd bestand:** bij een `JsonException` of een `null` resultaat doet de service `File.Copy(settings, settings.corrupt.json, overwrite: true)`, verwijdert het origineel, zet `LoadedFromCorruptFile = true` en gebruikt de standaardwaarden.
  - **Debounce:** via `TimeProvider.CreateTimer`; `Flush()` schrijft wijzigingen die nog wachten direct weg.

- [ ] **Step 1: Schrijf de tests**
  - `Load_MissingFile_GivesDefaults`
  - `Update_ThenNewServiceLoad_RoundTrips` (alle velden)
  - `Load_CorruptJson_CreatesCorruptCopy_AndDefaults` (inhoud `"{not json"`)
  - `Load_UnknownFields_Ignored`
  - `Load_MissingFields_Defaulted` (`{"volume":30}` → `Volume` 30 en `Theme` Dark)
  - `Load_OutOfRangeValues_Clamped` (`volume: 250` → 100; `seekStepSeconds: 7` → 5)
  - `UpdateDebounced_WritesOnlyAfter500ms` (`FakeTimeProvider` uit `Microsoft.Extensions.TimeProvider.Testing`)
  - `Flush_WritesPendingImmediately`
- [ ] **Step 2:** Run. Verwacht: FAIL.
- [ ] **Step 3:** Implementeer.
- [ ] **Step 4:** Run. Verwacht: PASS.
- [ ] **Step 5:** Commit: `feat(infra): instellingen opslaan met herstel bij beschadiging`.

---

### Task 7: Recent geopende bestanden

**Files:**
- Create: `src/Nexify.Core/Recent/RecentFile.cs`, `IRecentFilesService.cs`
- Create: `src/Nexify.Infrastructure/Recent/JsonRecentFilesService.cs`
- Test: `tests/Nexify.Infrastructure.Tests/JsonRecentFilesServiceTests.cs`

**Interfaces:**
- Produces:
  - `sealed record RecentFile(string Path, MediaKind Kind, DateTimeOffset OpenedAtUtc)`
  - `interface IRecentFilesService`, met
    - `void Load()`;
    - `IReadOnlyList<RecentFile> GetVisible()` (filtert op `File.Exists`; het opgeslagen bestand blijft ongewijzigd);
    - `void Add(MediaItem item)`, `void Remove(string path)`, `void Clear()`;
    - `event EventHandler? Changed`.
  - `JsonRecentFilesService(AppPaths, ILogger, TimeProvider? time = null)`. Het bestand heeft het formaat `{ "schemaVersion": 1, "items": [...] }`, met maximaal 20 items, de nieuwste eerst. Paden worden hoofdletterongevoelig vergeleken (Windows). Een beschadigd bestand wordt hetzelfde afgehandeld als bij de instellingen (een `recent.corrupt.json`-kopie en een lege lijst).

- [ ] **Step 1: Schrijf de tests**
  - `Add_NewestFirst`
  - `Add_Existing_MovesToTop_NoDuplicate` (ook bij een ander hoofdlettergebruik)
  - `Add_Beyond20_DropsOldest`
  - `GetVisible_HidesMissingFiles_ButKeepsThemStored` (het bestand verwijderen, daarna `GetVisible` → weg; het bestand opnieuw aanmaken → weer zichtbaar)
  - `Remove_And_Clear_Persist`
  - `Load_Corrupt_GivesEmpty_AndCorruptCopy`
- [ ] **Step 2:** Run. Verwacht: FAIL.
- [ ] **Step 3:** Implementeer.
- [ ] **Step 4:** Run. Verwacht: PASS.
- [ ] **Step 5:** Commit: `feat(infra): lijst met recent geopende bestanden`.

---

### Task 8: Lokalisatie en taalbestanden

**Files:**
- Create: `src/Nexify.Core/Localization/ILocalizationService.cs`
- Create: `src/Nexify.Infrastructure/Localization/JsonLocalizationService.cs`
- Create: `src/Nexify.App/Languages/en.json`, `nl.json` (in de App-csproj als `Content`, `CopyToOutputDirectory=PreserveNewest`)
- Test: `tests/Nexify.Infrastructure.Tests/LocalizationTests.cs` (koppelt `..\..\src\Nexify.App\Languages\*.json` als Content in het testproject)

**Interfaces:**
- Produces:
  - `sealed record LanguageInfo(string Code, string Name)`
  - `interface ILocalizationService`, met
    - `IReadOnlyList<LanguageInfo> Available`, `string CurrentCode`;
    - `void SetLanguage(string code)`;
    - `string this[string key]`, `string Format(string key, params object[] args)`;
    - `event EventHandler? LanguageChanged`;
    - `static string ResolveInitial(string? saved, CultureInfo ui)`: geeft `saved` terug als die bestaat, anders `"nl"` als `ui.TwoLetterISOLanguageName == "nl"`, anders `"en"`.
  - `JsonLocalizationService(string languagesDir, ILogger log)`. Laadt alle `*.json` uit die map; `Code` is de bestandsnaam en `Name` de sleutel `"language.name"`. **Terugval:** eerst de huidige taal, dan `en`, en anders de sleutel zelf. Een ontbrekende sleutel wordt één keer per sleutel als waarschuwing gelogd.
  - **Sleutelset** (dekt alle teksten uit de spec): `language.name`, `app.title`, `nav.*` (home, nowPlaying, settings), `home.*` (openFiles, openFolder, dropHint, recent, recentEmpty, clearRecent, removeRecent), `player.*` (play, pause, next, previous, shuffle, repeatOff, repeatAll, repeatOne, mute, unmute, volume, queue, loading, nothingPlaying), `queue.*` (title, empty, clear, playNext, remove), `settings.*` (appearance, theme, themeDark, themeLight, themeSystem, language, playback, defaultVolume, seekStep, openBehavior, openReplace, openAppend, about, version, licenses, openLogFolder), `error.*` (fileNotFound, accessDenied, unsupported, multipleFailed, settingsReset, vlcMissing, unexpected, skippedUnsupported, inaccessibleFolders), `common.*` (close, ok, seconds). De Nederlandse teksten komen letterlijk uit spec §5, bijvoorbeeld `error.unsupported` = `"{0} kan niet worden afgespeeld: het formaat wordt niet ondersteund of het bestand is beschadigd."` en `error.skippedUnsupported` = `"{0} bestanden overgeslagen: formaat niet ondersteund."`.

- [ ] **Step 1: Schrijf de tests**
  - `AllLanguages_HaveExactlySameKeysAsEnglish`
  - `AllLanguages_PlaceholderIndexesMatchEnglish` (regex `\{(\d+)\}`)
  - `Missing_FallsBackToEnglish_ThenKey`
  - `SetLanguage_RaisesLanguageChanged`
  - `ResolveInitial_DutchCulture_GivesNl`, `ResolveInitial_German_GivesEn`, `ResolveInitial_SavedWins`
  - `Available_ContainsNlAndEnWithNames`
- [ ] **Step 2:** Run. Verwacht: FAIL.
- [ ] **Step 3:** Implementeer de service en schrijf beide taalbestanden volledig uit.
- [ ] **Step 4:** Run. Verwacht: PASS.
- [ ] **Step 5:** Commit: `feat(infra): lokalisatie met Nederlands en Engels`.

---

### Task 9: Afspeel-engine (LibVLC)

**Files:**
- Create: `src/Nexify.Core/Playback/IPlaybackEngine.cs`, `PlaybackState.cs`, `PlaybackError.cs`, `PlaybackErrorClassifier.cs` (pure functie in Core, zodat hij op Linux testbaar is)
- Create: `src/Nexify.Playback/VlcPlaybackEngine.cs`
- Create: `tests/media/generate-test-media.ps1`
- Test: `tests/Nexify.Core.Tests/PlaybackErrorClassifierTests.cs`
- Test: `tests/Nexify.Playback.Tests/VlcPlaybackEngineTests.cs`, `WindowsFactAttribute.cs`

**Interfaces:**
- Produces (Core):
  - `enum PlaybackState { Idle, Opening, Playing, Paused, Stopped, Ended, Error }`
  - `enum PlaybackErrorReason { FileNotFound, AccessDenied, UnsupportedOrCorrupt, EngineUnavailable, Unknown }`
  - `sealed record PlaybackError(MediaItem? Item, PlaybackErrorReason Reason, string? Detail)`
  - `static class PlaybackErrorClassifier { static PlaybackErrorReason ClassifyBeforeOpen(string path); }`: geeft `FileNotFound` als het bestand niet bestaat, `AccessDenied` als het openen met `FileShare.ReadWrite` een `UnauthorizedAccessException` geeft, anders `Unknown` (= mag geopend worden). Een VLC-fout na het openen wordt `UnsupportedOrCorrupt`.
  - `IPlaybackEngine` exact zoals spec §2.3, plus `bool IsReady { get; }`, `MediaKind? CurrentKind`, `string? CurrentTitle`, `string? CurrentArtist` (uit de LibVLC-metadata na het parsen, anders null), en het event `MetadataChanged`.
- Produces (Playback): `sealed class VlcPlaybackEngine(ILogger log, string[]? extraVlcOptions = null) : IPlaybackEngine`.
  - **`InitializeAsync`:** `Core.Initialize()` en `new LibVLC(...)` via `Task.Run`, met de opties `--no-video-title-show` en `extraVlcOptions`. Faalt het laden, dan gooit hij geen uitzondering naar buiten: `IsReady=false` en `PlaybackFailed(EngineUnavailable)`.
  - **`OpenAsync`:**
    1. eerst `ClassifyBeforeOpen`; bij een fout `PlaybackFailed`, zonder VLC-aanroep;
    2. daarna `new Media(libVlc, new Uri(path))`. `Uri` doet de escaping van `#`, `%`, spaties en unicode;
    3. dan `MediaPlayer.Play(media)`.
    - Een open-generatieteller (`_openGeneration`) zorgt dat events van een vorig medium worden genegeerd, zodat bij snel achter elkaar openen het laatste wint.
  - **VLC-events** (`Playing`, `Paused`, `Stopped`, `EndReached`, `EncounteredError`, `TimeChanged` (afgeknepen tot ~4×/s), `LengthChanged`): de engine zet ze door via `ThreadPool.QueueUserWorkItem` en roept nooit LibVLC aan op de VLC-thread.
  - **Metadata:** `MediaParsedChanged` leest `Media.Meta(MetadataType.Title)` en `Media.Meta(MetadataType.Artist)`.
  - **Volume:** de property onthoudt de waarde ook als er nog geen medium is en past die toe bij `Playing`.
  - `VideoSurfaceSource` geeft de `MediaPlayer` terug.

- [ ] **Step 1: Schrijf `generate-test-media.ps1`.** Het script maakt met ffmpeg een sinustoon of `testsrc2` van 3 seconden naar `$OutDir` voor:
  - `tone.mp3`, `tone.wav`, `tone.flac`, `tone.ogg`, `tone-aac.m4a`, `tone-alac.m4a`, `tone.wma`;
  - `clip.mp4` (libx264), `clip.mkv`, `clip.avi` (mpeg4), `clip.mov`, `clip.webm` (libvpx-vp9), `clip.wmv` (wmv2), `clip.mpeg` (mpeg2video);
  - `Beyoncé #1 100%.mp3`, een kopie van `tone.mp3`;
  - `corrupt.mp4`, 64 KB willekeurige bytes.
- [ ] **Step 2: Schrijf de core-tests** (`PlaybackErrorClassifierTests`): `Missing_IsFileNotFound` en `Existing_IsUnknown`. Run ze. Verwacht: eerst FAIL, na de implementatie PASS.
- [ ] **Step 3: Schrijf de integratietests.** Gebruik `[WindowsFact]`: dat slaat de test over als het niet Windows is of `NEXIFY_TEST_MEDIA` niet gezet is. De engine wordt aangemaakt met `--aout=dummy` en `--vout=dummy`.
  - `[Theory]` `PlaysEveryFormat` (alle bestanden uit Step 1 behalve `corrupt` en het bijzondere pad): de status bereikt `Playing` binnen 5 s, `Duration` ligt binnen ±0,5 s van 3 s, `Seek(1.5s)` zet de `Position` binnen 5 s op ≥ 1,3 s, en `EndReached` komt binnen 6 s;
  - `Corrupt_RaisesUnsupportedOrCorrupt`;
  - `Missing_RaisesFileNotFound_WithoutVlcCall`;
  - `Plays_PathWithSpecialCharacters`;
  - `EndReached_ThenOpenNext_PlaysSecondFile` (de handler van `EndReached` roept `OpenAsync(second)` aan en `Playing` komt binnen 5 s);
  - `RapidOpen_LastOneWins` (10× `OpenAsync` zonder te wachten; het uiteindelijke `Current` is het 10e en de status is `Playing`);
  - `Volume_SetBeforeOpen_IsApplied`.
- [ ] **Step 4:** Implementeer de engine. Run lokaal `dotnet build`. Verwacht: 0 fouten. De integratietests draaien in CI (Task 14).
- [ ] **Step 5:** Commit: `feat(playback): LibVLC-engine met foutclassificatie en integratietests`.

---

### Task 10: Huisstijl: tokens, lettertypen, stijlen en logo

**Files:**
- Create: `assets/branding/nexify-logo.svg`, `nexify-mark.svg`, `tools/make-icon.py`, `assets/branding/nexify.ico`, `src/Nexify.App/Assets/nexify.ico`, `nexify-mark.png` (256 px)
- Create: `src/Nexify.App/Assets/Fonts/Inter-*.ttf` en `Archivo-*.ttf` (Regular/Medium/SemiBold/Bold, van de officiële OFL-releases op GitHub en Google Fonts), plus `OFL.txt` per familie.
- Create: `src/Nexify.App/Themes/Tokens.Dark.xaml`, `Tokens.Light.xaml`, `Typography.xaml`, `Controls.xaml`
- Create: `src/Nexify.App/Services/ThemeService.cs`, `src/Nexify.Presentation/IThemeService.cs`
- Test: `tests/Nexify.Presentation.Tests/ThemeResolutionTests.cs`

**Interfaces:**
- Produces:
  - **Resource-sleutels:** `Bg.Base`, `Bg.Surface`, `Bg.SurfaceHover`, `Border.Subtle`, `Text.Primary`, `Text.Body`, `Text.Muted`, `Accent`, `Accent.Gradient`, `Accent.OnAccent`, `Accent.Text`, `Accent.Soft`, `Status.Success`, `Status.Error`. Allemaal als `SolidColorBrush`, behalve `Accent.Gradient` (een `LinearGradientBrush`). De waarden exact volgens spec §3.2.
  - **Lettertypen:** `Font.Heading` (Archivo SemiBold) en `Font.Body` (Inter), via `pack://application:,,,/Assets/Fonts/#Inter`.
  - **Groottes** (`Typography.xaml`): `Text.Display` 28, `Text.Title` 20, `Text.Subtitle` 15, `Text.Body` 14 en `Text.Caption` 12. Dit zijn `double`-resources met de prefix `Size.`.
  - **Stijlen** (`Controls.xaml`): `Button.Accent` (accentverloop, `Accent.OnAccent`-tekst, radius 12), `Button.Subtle` (transparant met rand `Border.Subtle`, hover `Bg.SurfaceHover`), `Button.Icon` (40×40, radius 12, Segoe Fluent Icons), `Button.PlayRound` (56×56 rond, accent), `Card` (Border-stijl met `Bg.Surface`, radius 16, rand 1), `Slider.Nexify` (spoor 4 px, gevuld deel accent, duim 14 px), `ListBoxItem.Nav`, `RadioButton.Segmented`, `ComboBox.Nexify` en `ScrollBar.Thin`. Hover- en press-animaties duren 100 ms.
  - **Iconen:** `IconFont` gebruikt `Segoe Fluent Icons, Segoe MDL2 Assets`.
  - `interface IThemeService { ThemeMode Mode { get; } bool IsDarkEffective { get; } void Apply(ThemeMode mode); event EventHandler? EffectiveThemeChanged; }`
  - `static bool ThemeResolution.IsDark(ThemeMode mode, bool systemUsesLight)` in Presentation.
  - **`ThemeService`** (App):
    - wisselt de `MergedDictionaries[0]` tussen Dark en Light;
    - leest de registerwaarde `AppsUseLightTheme`;
    - luistert naar `SystemEvents.UserPreferenceChanged` (categorie `General`);
    - zet `RenderOptions`/animaties uit als `SystemParameters.ClientAreaAnimation == false` (via de resource `Anim.Enabled`, een `bool`).
  - **Logo:** de tegel heeft radius 22% en het accentverloop van linksboven (`#E8FC9C`) naar rechtsonder (`#A8DB1A`). In de tegel staat de afspeeldriehoek, gevolgd door een smalle »-chevron, in `#0A0C10`. `nexify-logo.svg` zet daarnaast het woordmerk "NEXIFY PLAYER" in Archivo Bold. Dat is tekst omgezet naar paden; maak die met `fonttools`/`cairosvg` in `make-icon.py` of teken de letters handmatig.
  - **`make-icon.py`:** `python3 tools/make-icon.py` rendert `nexify-mark.svg` met cairosvg naar PNG's van 16, 24, 32, 48, 64, 128 en 256 px. Pillow schrijft die als één `.ico` (`sizes=[...]`) en als `nexify-mark.png`. De gegenereerde bestanden worden gecommit, zodat CI geen Python nodig heeft.

- [ ] **Step 1: Schrijf de test** `IsDark_Table`: (Dark, *) → true; (Light, *) → false; (System, true) → false; (System, false) → true.
- [ ] **Step 2:** Run en zie de test falen. Implementeer `ThemeResolution`. Run en zie hem slagen.
- [ ] **Step 3:** Maak de SVG's en draai `make-icon.py`. Controleer met `python3 -c "from PIL import Image; im=Image.open('assets/branding/nexify.ico'); print(im.info['sizes'])"`. Verwacht: 7 formaten. Bekijk de 256 px-PNG visueel met de Read-tool.
- [ ] **Step 4:** Voeg de lettertypen, XAML-dictionaries en `ThemeService` toe, en `ApplicationIcon` aan de App-csproj. Run `dotnet build`. Verwacht: 0 fouten.
- [ ] **Step 5:** Commit: `feat(app): huisstijl, thema's, lettertypen en logo`.

---

### Task 11: Hoofdvenster, titelbalk, zijbalk en navigatie

**Files:**
- Create: `src/Nexify.App/Program.cs`, `App.xaml(.cs)`, `Services/WpfDispatcher.cs`, `Services/WindowPlacement.cs`, `Localization/LocExtension.cs`
- Create: `src/Nexify.App/Shell/MainWindow.xaml(.cs)`, `TitleBar.xaml(.cs)`, `Sidebar.xaml(.cs)`, `WindowChromeHelper.cs`
- Create: `src/Nexify.Presentation/IUiDispatcher.cs`, `ShellViewModel.cs`, `WindowPlacementLogic.cs`
- Test: `tests/Nexify.Presentation.Tests/ShellViewModelTests.cs`, `WindowPlacementLogicTests.cs`

**Interfaces:**
- Produces:
  - `interface IUiDispatcher { void Post(Action action); }`. In tests is dit een synchrone implementatie.
  - `enum Page { Home, NowPlaying, Settings }`
  - `ShellViewModel : ObservableObject`, met
    - `Page CurrentPage`, `RelayCommand<Page> NavigateCommand`;
    - `bool IsSidebarCompact` en `void UpdateForWidth(double width)` (compact als de breedte kleiner is dan 900).
  - `static WindowBounds WindowPlacementLogic.Sanitize(WindowBounds? saved, IReadOnlyList<Rect> screens, Rect primaryWorkArea)`:
    - zonder opgeslagen waarde of als minder dan 64×64 px van de titelbalk op een scherm valt: gecentreerd op het primaire scherm, 1280×800, maximaal 90% van de werkruimte;
    - de breedte en hoogte zijn minimaal 720×480.
    - `Rect` is een eigen `readonly record struct Rect(double X, double Y, double W, double H)` in Presentation.
  - **`LocExtension`:** `{loc:T Key}` levert een `Binding` naar een `LocProxy`-indexer, die `PropertyChanged("Item[]")` afvuurt bij `LanguageChanged`. Teksten worden zo live bijgewerkt.
  - **`MainWindow`:**
    - `WindowChrome` met `CaptionHeight=40`, `ResizeBorderThickness=6`, `GlassFrameThickness=0`, `CornerRadius=0`;
    - een titelbalk met het logo en de knoppen min, max/herstel en sluiten;
    - `WindowChromeHelper` zet `DwmSetWindowAttribute(DWMWA_USE_IMMERSIVE_DARK_MODE=20)` op basis van het thema, en handelt `WM_NCHITTEST` af door `HTMAXBUTTON` (9) terug te geven boven de maximaliseerknop, zodat Snap Layouts werken;
    - de achtergrond is `Bg.Base` met een radiale gloed (`RadialGradientBrush`, centrum 10%/−10%, `#0FC6F432` naar transparant);
    - de inhoud wordt met een `ContentControl` gewisseld met een fade + 8 px `TranslateTransform` in 150 ms (als `Anim.Enabled`).
  - **`Sidebar`:** de items Home, Nu speelt en Instellingen met iconen `E80F`, `E8D6` en `E713`. Het actieve item krijgt de achtergrond `Accent.Soft` en een accentbalkje van 3 px. De zijbalk is 240 of 72 px breed; in compacte stand toont hij tooltips.
  - **`Program.Main`:** `[STAThread]`. Het maakt `App` aan, geeft `args` door, configureert de services handmatig (zonder DI-container: een `AppServices`-klasse met de instanties) en roept `app.Run()` aan.
  - **Globale foutafhandelaars** komen in Task 13.

- [ ] **Step 1: Schrijf de tests**
  - `Navigate_ChangesCurrentPage`
  - `UpdateForWidth_Below900_Compact_AtOrAbove900_Full`
  - `WindowPlacement_Null_CentersOnPrimary`
  - `WindowPlacement_OffscreenBounds_ResetToPrimary` (opgeslagen op x=5000 met één scherm van 1920×1080)
  - `WindowPlacement_ValidBounds_Kept`
  - `WindowPlacement_TooSmall_ClampedTo720x480`
- [ ] **Step 2:** Run. Verwacht: FAIL. Implementeer de Presentation-code. Run. Verwacht: PASS.
- [ ] **Step 3:** Implementeer de WPF-schil. Run `dotnet build`. Verwacht: 0 fouten.
- [ ] **Step 4:** Commit: `feat(app): hoofdvenster met eigen titelbalk, zijbalk en navigatie`.

---

### Task 12: Speler: viewmodel, bedieningsbalk en Nu speelt

**Files:**
- Create: `src/Nexify.Presentation/PlayerViewModel.cs`, `QueueItemViewModel.cs`, `INotificationService.cs`, `NotificationsViewModel.cs`
- Create: `src/Nexify.App/Player/TransportBar.xaml(.cs)`, `SeekSlider.cs`, `Pages/NowPlayingPage.xaml(.cs)`, `Notifications/ToastHost.xaml(.cs)`
- Test: `tests/Nexify.Presentation.Tests/PlayerViewModelTests.cs`, `NotificationsViewModelTests.cs`, `FakePlaybackEngine.cs`

**Interfaces:**
- Consumes: `IPlaybackEngine`, `PlayQueue`, `ISettingsService`, `IRecentFilesService`, `ILocalizationService` en `IUiDispatcher`.
- Produces:
  - `interface INotificationService { void Show(string message, NotificationKind kind = NotificationKind.Info); }`, met `enum NotificationKind { Info, Success, Error }`.
  - `NotificationsViewModel : INotificationService`, met
    - `ObservableCollection<NotificationItem> Visible`: maximaal 3; een 4e duwt de oudste weg;
    - `NotificationItem(string Message, NotificationKind Kind)`, `RelayCommand<NotificationItem> DismissCommand`;
    - automatisch verwijderen na 6 s via een geïnjecteerde `TimeProvider`.
  - `PlayerViewModel : ObservableObject`:
    - eigenschappen: `QueueItems` (`ObservableCollection<QueueItemViewModel>`), `CurrentTitle`, `CurrentSubtitle`, `IsPlaying`, `IsReady`, `HasVideo`, `PositionSeconds`, `DurationSeconds`, `PositionText` en `DurationText` (`m:ss`, of `h:mm:ss` vanaf 1 uur), `Volume`, `IsMuted`, `Shuffle`, `Repeat` en `object? VideoSource`;
    - commando's: `PlayPauseCommand`, `NextCommand`, `PreviousCommand`, `ToggleShuffleCommand`, `CycleRepeatCommand` (Off → All → One → Off), `ToggleMuteCommand`, `SeekToCommand(double seconds)`, `SeekRelativeCommand(int direction)` (± `SeekStepSeconds`), `VolumeStepCommand(int delta)`, `PlayQueueItemCommand(QueueItemViewModel)`, `RemoveQueueItemCommand`, `PlayNextQueueItemCommand`, `ClearQueueCommand` en `MoveQueueItem(int from, int to)`;
    - `Task PlayItemsAsync(IReadOnlyList<MediaItem> items, OpenBehavior behavior)`: bij `Replace` gebruikt hij `ReplaceWith` en speelt hij het eerste item; bij `Append` doet hij `AddRange` en start hij alleen als er niets speelt;
    - `CurrentTitle` is de titel uit de metadata, anders `DisplayName`. `CurrentSubtitle` is de artiest, anders de mapnaam.
  - **Gedrag:**
    - **Vorige:** als de positie groter is dan 3 s, `Seek(0)`; anders `queue.Previous()`. Geeft die `null`, dan ook `Seek(0)`.
    - **`EndReached`:** speelt `NextForEndReached()` af. Is die `null`, dan stopt hij, waarbij `Current` blijft staan.
    - **`PlaybackFailed`:** toont een melding met de vertaalde tekst per reden (`error.fileNotFound`, `error.accessDenied`, `error.unsupported`, `error.vlcMissing` met de bestandsnaam) en speelt daarna het volgende item. Er telt een teller van opeenvolgende fouten mee; bij 3 fouten stopt hij met `error.multipleFailed`. `Playing` zet de teller terug op 0.
    - **Recent geopend:** bij `Playing` van een nieuw item wordt `recent.Add(item)` aangeroepen.
    - **Instellingen:** volume, dempen, shuffle en herhalen worden via `UpdateDebounced` opgeslagen. Bij het opstarten komen ze uit de instellingen.
    - **Verwijderen van het huidige item:** speelt het nieuwe `Current` af als het afspelen bezig was, en stopt bij een lege wachtrij.
  - **`TransportBar.xaml`:** de layout uit spec §3.4, 88 px hoog, met de stijlen uit Task 10. `SeekSlider` werkt de positie tijdens het slepen niet bij vanuit de engine, toont de doeltijd in een tooltip en voert bij loslaten `SeekToCommand` uit. Klikken op het spoor springt direct naar die positie (`IsMoveToPointEnabled`).
  - **`NowPlayingPage`:** een `VideoView` (`MediaPlayer` gebonden aan `VideoSource`), zichtbaar als `HasVideo`. Anders een `Card` met `nexify-mark.png`, de titel en de ondertitel. Rechts staat een wachtrijlijst van 360 px met slepen om te herordenen (`GongSolutions.WPF.DragDrop` of eigen drag-handlers; kies de eigen handlers om afhankelijkheden te beperken), een contextmenu (*Als volgende afspelen*, *Verwijderen*) en een knop *Wachtrij wissen*. Dubbelklik speelt een item af.
  - **`ToastHost`:** rechtsonder boven de bedieningsbalk, met `Card`-stijl en een gekleurd icoon per `Kind`.

- [ ] **Step 1: Schrijf de tests**
  - **`FakePlaybackEngine`:** registreert aanroepen, laat tests events afvuren, en zet `IsReady=true`.
  - `PlayItems_Replace_StartsFirst`
  - `PlayItems_Append_WhilePlaying_DoesNotInterrupt`
  - `Previous_After3s_RestartsCurrent`
  - `Previous_Within3s_GoesToPrevious`
  - `EndReached_PlaysNext`
  - `EndReached_LastItemRepeatOff_Stops`
  - `EndReached_RepeatOne_ReplaysSame`
  - `PlaybackFailed_ShowsMessage_AndSkips`
  - `ThreeConsecutiveFailures_StopsWithMultipleFailedMessage`
  - `SuccessfulPlay_ResetsFailureCounter`
  - `Playing_AddsToRecent`
  - `VolumeChange_PersistsDebounced`
  - `SeekRelative_UsesSettingsStep`
  - `CycleRepeat_OffAllOneOff`
  - `TimeFormatting` (59 s → `0:59`, 3725 s → `1:02:05`)
  - `RemoveCurrentWhilePlaying_PlaysFollowing`
  - `Notifications_Max3_AutoDismissAfter6s`
- [ ] **Step 2:** Run. Verwacht: FAIL. Implementeer de Presentation-code. Run. Verwacht: PASS.
- [ ] **Step 3:** Implementeer de XAML-onderdelen. Run `dotnet build`. Verwacht: 0 fouten.
- [ ] **Step 4:** Commit: `feat(app): spelerbediening, Nu speelt en wachtrij`.

---

### Task 13: Home, Instellingen, openen, sneltoetsen en foutafhandeling

**Files:**
- Create: `src/Nexify.Presentation/HomeViewModel.cs`, `SettingsViewModel.cs`, `MediaOpener.cs`, `IFileDialogService.cs`
- Create: `src/Nexify.App/Pages/HomePage.xaml(.cs)`, `SettingsPage.xaml(.cs)`, `Services/FileDialogService.cs`
- Modify: `src/Nexify.App/App.xaml.cs` (opstartvolgorde §4.1 en globale foutafhandelaars) en `Shell/MainWindow.xaml(.cs)` (sneltoetsen, slepen en neerzetten, venster opslaan)
- Create: `THIRD-PARTY-NOTICES.md` (getoond via *Licenties* op de instellingenpagina, met de lijst uit spec §7)
- Test: `tests/Nexify.Presentation.Tests/MediaOpenerTests.cs`, `HomeViewModelTests.cs`, `SettingsViewModelTests.cs`

**Interfaces:**
- Produces:
  - `interface IFileDialogService`, met
    - `IReadOnlyList<string> PickFiles()`. Het filter gebruikt `MediaFormats`: "Alle media", "Audio", "Video" en "Alle bestanden";
    - `string? PickFolder()`, via `Microsoft.Win32.OpenFolderDialog`, beschikbaar in .NET 8;
    - `void OpenInExplorer(string path)` en `void OpenFile(string path)`.
  - `sealed class MediaOpener(PlayerViewModel player, ISettingsService settings, INotificationService notify, ILocalizationService loc)`, met
    - `Task OpenPathsAsync(IEnumerable<string> paths, CancellationToken ct = default)`: roept `FolderScanner.ClassifyPathsAsync` aan, daarna `player.PlayItemsAsync(items, settings.Current.OpenBehavior)`;
    - meldingen: `error.skippedUnsupported` als `UnsupportedCount > 0`, en `error.inaccessibleFolders` als die lijst niet leeg is;
    - `bool IsBusy`, gebonden aan een laadindicator op Home.
  - `HomeViewModel`: `OpenFilesCommand`, `OpenFolderCommand`, `ObservableCollection<RecentFile> Recent` (`GetVisible`), `PlayRecentCommand`, `RemoveRecentCommand` en `ClearRecentCommand`. Het viewmodel ververst bij `IRecentFilesService.Changed`.
  - `SettingsViewModel`: `Theme`, `LanguageCode` (met de lijst `Languages`), `DefaultVolume`, `SeekStepSeconds` (5/10/30) en `OpenBehavior`. Elke setter roept `settings.Update` aan en past het effect direct toe (`themeService.Apply`, `loc.SetLanguage`). Daarnaast `VersionText` (assemblyversie), `OpenLogFolderCommand` en `OpenLicensesCommand`.
  - **Standaardvolume (spec §3.1):** bij het opstarten wordt `Volume = DefaultVolume` (veld uit Task 6). Volumewijzigingen tijdens het gebruik passen alleen `Volume` aan en gelden dus voor de sessie.
  - **Opstarten (`App.OnStartup`):**
    1. `AppPaths.Resolve().EnsureCreated()`, Logging, settings en recent laden, en lokalisatie (`ResolveInitial`, daarna opslaan);
    2. thema toepassen;
    3. `MainWindow.Show()`;
    4. `await engine.InitializeAsync()`;
    5. `MediaOpener.OpenPathsAsync(args)` als er argumenten zijn;
    6. als `LoadedFromCorruptFile`, de melding `error.settingsReset`.
  - **Afsluiten:** de vensterpositie opslaan, `settings.Flush()`, `engine.Dispose()` en `Log.CloseAndFlush()`.
  - **Globale fouten:**
    - `DispatcherUnhandledException`: loggen, een dialoog (eigen `Card`-dialoogvenster) met `error.unexpected` en de knop *Logmap openen*, daarna `e.Handled = true`;
    - `AppDomain.UnhandledException`: loggen en flushen;
    - `TaskScheduler.UnobservedTaskException`: loggen en `SetObserved()`.
  - **Sneltoetsen** (spec §3.6): `InputBindings` op `MainWindow`, plus een `PreviewKeyDown`-filter dat Spatie, pijltjes, M, N en P negeert als `Keyboard.FocusedElement` een `TextBoxBase`, `PasswordBox` of `ComboBox` met de bewerkmodus aan is.
  - **Slepen en neerzetten:** `AllowDrop` op het hele venster; `DataFormats.FileDrop` gaat naar `MediaOpener`. De sleepzone op Home licht op tijdens het slepen.

- [ ] **Step 1: Schrijf de tests**
  - `OpenPaths_ReplaceBehavior_ReplacesQueue`
  - `OpenPaths_AppendBehavior_Appends`
  - `OpenPaths_Unsupported_ShowsSkippedCount` (de tekst bevat `"2"`)
  - `OpenPaths_InaccessibleFolder_ShowsMessage`
  - `OpenPaths_NothingPlayable_DoesNotTouchQueue`
  - `Home_RecentRefreshesOnChanged`
  - `Home_ClearRecent_ClearsService`
  - `Settings_ThemeChange_AppliesAndPersists`
  - `Settings_LanguageChange_SetsLanguage`
  - `Settings_SeekStep_OnlyAllows5_10_30`
- [ ] **Step 2:** Run. Verwacht: FAIL. Implementeer. Run. Verwacht: PASS.
- [ ] **Step 3:** Implementeer de pagina's, de opstartvolgorde, de foutafhandelaars, de sneltoetsen en het slepen. Run `dotnet build`. Verwacht: 0 fouten.
- [ ] **Step 4:** Commit: `feat(app): home, instellingen, openen, sneltoetsen en foutafhandeling`.

---

### Task 14: Volledige CI en UI-smoketest

**Files:**
- Modify: `.github/workflows/build.yml`
- Create: `tests/Nexify.App.UiTests/SmokeTests.cs`, `AppFixture.cs`
- Modify: XAML. `AutomationProperties.AutomationId` op: `Nav.Home`, `Nav.NowPlaying`, `Nav.Settings`, `Settings.ThemeLight`, `Settings.Language`, `Player.Position`, `Player.PlayPause` en `MainWindow`.

**Interfaces:**
- Consumes: de gepubliceerde `NexifyPlayer.exe` in het pad uit de omgevingsvariabele `NEXIFY_APP_EXE`, en `NEXIFY_TEST_MEDIA`.
- **Workflow-stappen** (één job, `windows-latest`):
  1. checkout en `actions/setup-dotnet@v4` (8.0.x);
  2. `dotnet restore` en `dotnet build -c Release --no-restore`;
  3. `dotnet test` met de drie Linux-veilige testprojecten, met `--logger trx`;
  4. `choco install ffmpeg -y`, daarna `pwsh tests/media/generate-test-media.ps1 -OutDir $env:RUNNER_TEMP\media`, en `NEXIFY_TEST_MEDIA` zetten;
  5. `dotnet test tests/Nexify.Playback.Tests -c Release`;
  6. `dotnet publish src/Nexify.App -c Release -r win-x64 --self-contained -p:PublishReadyToRun=true -p:VlcWindowsX86Enabled=false -o out/x64`, en hetzelfde voor x86 met `-p:VlcWindowsX64Enabled=false`;
  7. `dotnet test tests/Nexify.App.UiTests`, met `NEXIFY_APP_EXE=out/x64/NexifyPlayer.exe`;
  8. de downloads `NexifyPlayer-win-x64` en `NexifyPlayer-win-x86` (zip van de out-mappen) en `test-results` (`**/*.trx`, `if: always()`) uploaden.
- **UI-tests** (`[WindowsFact]`): elke test start de app met `NEXIFY_DATA_DIR` naar een nieuwe tijdelijke map en sluit hem af in `Dispose`.
  - `Starts_MainWindowVisibleWithin15s`;
  - `Settings_ThemeLight_PersistsToSettingsJson` (wacht tot `settings.json` `"theme": "light"` bevat, maximaal 5 s);
  - `Settings_LanguageEnglish_ChangesNavLabel` (de naam van `Nav.Settings` wordt `"Settings"`);
  - `StartWithFile_PositionAdvances` (start met `tone.mp3`; de waarde van `Player.Position` is binnen 5 s groter dan 0). Dit is een audiobestand, zodat de test geen GPU nodig heeft.

- [ ] **Step 1:** Schrijf de UI-tests en voeg de AutomationId's toe. Run lokaal `dotnet build`. Verwacht: 0 fouten.
- [ ] **Step 2:** Breid de workflow uit. Commit, push en volg de run met `gh run watch`. Verwacht: alle stappen groen.
- [ ] **Step 3:** Faalt er iets, dan onderzoek je de logs via `gh run view --log-failed` en los je het op met de systematic-debugging-skill. Herhaal tot alles groen is.
- [ ] **Step 4:** Commit: `ci: Windows-build met afspeel- en UI-tests en x64/x86-publicatie`.

---

### Task 15: Documentatie

**Files:**
- Create: `README.md`: wat Nexify Player is, de status (deelproject 1 van 8), schermafbeeldingen (volgen in een later deelproject; hier geen afbeeldingslink), downloaden via de Actions-artefacten, en de sneltoetsen.
- Create: `docs/BUILD.md`: vereisten (.NET 8 SDK, Windows 10/11, eventueel Visual Studio 2022 17.8+), `dotnet build`, `dotnet test`, de publish-commando's uit Task 14, het genereren van testmedia, en het bouwen op Linux (alleen compileren en unittests).
- Create: `docs/TESTING.md`: de handmatige checklist uit spec §6.4 met afvinkvakjes, per punt de verwachte uitkomst, en een plek voor de bevindingen.

- [ ] **Step 1:** Schrijf de drie bestanden in het Nederlands. Controleer dat elk commando in `BUILD.md` overeenkomt met `build.yml`.
- [ ] **Step 2:** Commit: `docs: README, buildinstructies en handmatige testlijst`.
