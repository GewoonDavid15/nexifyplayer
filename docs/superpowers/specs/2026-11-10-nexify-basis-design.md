# Nexify Player — Deelproject 1: Basis — Ontwerp

- **Datum:** 2026-11-10
- **Status:** Ter beoordeling
- **Product:** Nexify Player, een product van Next Solutions (nextsolutionshq.nl)

## 1. Doel en context

Nexify Player wordt een zelfstandige, moderne Windows-mediaspeler voor muziek, video's, films en series, met een eigen installer. Het totale project is opgedeeld in acht deelprojecten die elk een eigen ontwerp, plan en bouw-/testcyclus krijgen:

1. **Basis** (dit document)
2. Audio-engine (equalizer, bass boost, normalisatie, crossfade, snelheid, gapless)
3. Muziekbibliotheek (scannen, artiesten/albums/genres, hoezen, playlists, favorieten, zoeken, metadata bewerken)
4. Videospeler (volledig scherm, Picture-in-Picture, frame-voor-frame, screenshots, zoom/beeldverhouding, audio- en ondertitelsporen)
5. Films en series (herkenning, seizoenen/afleveringen, posters, optionele online metadata)
6. Windows-integratie (SMTC, mediatoetsen, taakbalk, systeemvak, bestandsassociaties, autostart, enkele instantie)
7. Updates (GitHub Releases, veilige verwerking, updategeschiedenis)
8. Installer (NSIS-wizard in eigen huisstijl, x64 + x86, uninstaller, Apps en onderdelen)

**Doel van deelproject 1:** een werkende, geteste basis waarop alle volgende deelprojecten voortbouwen. Na dit deelproject kan de gebruiker audio- en videobestanden en complete mappen openen, afspelen en bedienen in een venster dat er al volledig in de Next Solutions-stijl uitziet, met thema's, twee talen en opgeslagen instellingen.

### Vastgestelde keuzes

| Onderwerp | Keuze |
|---|---|
| Ontwikkelen en testen | GitHub-repository `GewoonDavid15/nexifyplayer`; GitHub Actions bouwt en test op een Windows-runner; de gebruiker test handmatig op Windows |
| Stijl | Huisstijl Next Solutions: zwart met **limoengroen**, modern en strak (niet futuristisch) |
| Technologie | WPF op .NET 8 + LibVLCSharp + (later) SQLite en TagLib# + NSIS |

### Bewust níet in deelproject 1

Muziek-, video-, film- en seriebibliotheek, playlists, favorieten, equalizer en audio-effecten, volledig scherm, Picture-in-Picture, ondertitels, mediatoetsen/SMTC, systeemvak, bestandsassociaties, updates en installer. Deze horen bij de deelprojecten 2–8. Menu-onderdelen daarvoor worden pas zichtbaar wanneer ze echt werken (geen placeholders).

## 2. Architectuur

### 2.1 Doelplatform

- .NET 8, target framework `net8.0-windows10.0.19041.0` (nodig voor de Windows Media-API's in deelproject 6; minimaal Windows 10 versie 2004).
- Architecturen: **x64** en **x86**.
- Publicatie: *self-contained*; de gebruiker hoeft .NET niet te installeren.
- LibVLC via NuGet-pakket `VideoLAN.LibVLC.Windows` (bevat x64- en x86-binaries) en `LibVLCSharp.WPF`. LibVLC is LGPL-2.1: dynamisch gelinkt meegeleverd, licentietekst opgenomen in `THIRD-PARTY-NOTICES.md`.

### 2.2 Projectindeling (`NexifyPlayer.sln`)

```
src/
  Nexify.Core/              Modellen, interfaces, wachtrij, formaatdetectie, mapscan, foutmodellen
  Nexify.Playback/          VlcPlaybackEngine (LibVLCSharp) achter IPlaybackEngine
  Nexify.Infrastructure/    Instellingen, recente bestanden, logging, lokalisatie, atomisch schrijven
  Nexify.App/               WPF-applicatie (MVVM, CommunityToolkit.Mvvm)
tests/
  Nexify.Core.Tests/
  Nexify.Infrastructure.Tests/
  Nexify.Playback.Tests/    Integratietests met echte LibVLC (alleen Windows)
  Nexify.App.UiTests/       FlaUI-smoketest (alleen Windows)
assets/
  branding/                 Logo (SVG), app-icoon (.ico), lettertypen
docs/
.github/workflows/build.yml
```

| Project | Verantwoordelijkheid | Afhankelijk van |
|---|---|---|
| `Nexify.Core` | `MediaItem`, `MediaKind` (Audio/Video), `PlayQueue`, `RepeatMode`, `MediaFormats`, `FolderScanner`, `PlaybackError`/`PlaybackErrorReason`, interfaces `IPlaybackEngine`, `ISettingsService`, `IRecentFilesService`, `ILocalizationService`. Geen UI- of VLC-code. | — |
| `Nexify.Playback` | `VlcPlaybackEngine : IPlaybackEngine`. Beheert één `LibVLC`- en één `MediaPlayer`-instantie. | Core, LibVLCSharp |
| `Nexify.Infrastructure` | `JsonSettingsService`, `JsonRecentFilesService`, `JsonLocalizationService`, `AtomicFileWriter`, `AppPaths`, Serilog-configuratie. | Core |
| `Nexify.App` | Venster, pagina's, viewmodels, thema's, besturingselementen, sneltoetsen, meldingen. | Alle bovenstaande |

**Regel:** de UI praat uitsluitend via `IPlaybackEngine` met de speler. Viewmodels zijn testbaar met een nep-engine, en deelprojecten 2 en 4 breiden de engine uit zonder de UI te verbouwen.

### 2.3 Belangrijkste interface

```csharp
public interface IPlaybackEngine : IDisposable
{
    Task InitializeAsync();                    // LibVLC laden op achtergrondthread
    PlaybackState State { get; }               // Idle, Opening, Playing, Paused, Stopped, Ended, Error
    MediaItem? Current { get; }
    TimeSpan Position { get; }
    TimeSpan Duration { get; }
    int Volume { get; set; }                   // 0–100
    bool IsMuted { get; set; }

    Task OpenAsync(MediaItem item);            // laadt en start afspelen
    void Play(); void Pause(); void TogglePlayPause(); void Stop();
    void Seek(TimeSpan position);
    void SeekRelative(TimeSpan delta);

    object? VideoSurfaceSource { get; }        // MediaPlayer voor de VideoView in de UI

    event EventHandler<PlaybackState> StateChanged;
    event EventHandler<TimeSpan> PositionChanged;
    event EventHandler<TimeSpan> DurationChanged;
    event EventHandler EndReached;
    event EventHandler<PlaybackError> PlaybackFailed;
}
```

Events van LibVLC komen op een VLC-thread binnen. De engine roept nooit LibVLC-methoden aan vanuit die callbacks (vereiste van LibVLC); acties worden via `ThreadPool` uitgevoerd. De UI-laag zet events via de `Dispatcher` door naar de UI-thread.

## 3. Interface en navigatie

### 3.1 Pagina's in deelproject 1

| Pagina | Inhoud |
|---|---|
| **Home** | Knoppen *Bestanden openen* en *Map openen*, sleepzone voor bestanden en mappen, lijst *Recent geopend* (max. 20, met type-icoon, naam, map en datum; klikken speelt af; item verwijderen en lijst wissen mogelijk). |
| **Nu speelt** | Videovlak (LibVLCSharp `VideoView`) of, bij audio, een groot vlak met titel/bestandsnaam en het Nexify-beeldmerk. Daarnaast/eronder de wachtrij: items verslepen, verwijderen, *als volgende afspelen*, wachtrij wissen. Dubbelklik speelt een item af. |
| **Instellingen** | **Weergave:** thema (Donker / Licht / Systeem). **Taal:** Nederlands / English. **Afspelen:** standaardvolume bij opstarten, spoelstap (5/10/30 s), *wachtrij vervangen of aanvullen bij openen*. **Over:** versie, licenties van derden, map met logbestanden openen. |

Muziek, Video's, Films, Series, Playlists en Favorieten worden pas aan de zijbalk toegevoegd in het deelproject waarin ze worden gebouwd.

### 3.2 Huisstijl (afgeleid van nextsolutionshq.nl)

**Kleuren (tokens in `Themes/Dark.xaml` en `Themes/Light.xaml`):**

| Token | Donker | Licht |
|---|---|---|
| `Bg.Base` | `#0A0C10` | `#F6F7F9` |
| `Bg.Surface` | `#11141B` | `#FFFFFF` |
| `Bg.SurfaceHover` | `#FFFFFF` 5% | `#000000` 4% |
| `Border.Subtle` | `#FFFFFF` 10% | `#000000` 10% |
| `Text.Primary` | `#FFFFFF` | `#0A0C10` |
| `Text.Body` | `#D4D4D8` | `#27272A` |
| `Text.Muted` | `#A1A1AA` | `#52525B` |
| `Accent` (vlakken, knoppen) | `#C6F432` | `#C6F432` |
| `Accent.Gradient` | `#E8FC9C → #C6F432 → #A8DB1A` | gelijk |
| `Accent.OnAccent` (tekst op accent) | `#0A0C10` | `#0A0C10` |
| `Accent.Text` (accent als tekst/icoon) | `#C6F432` | `#5F7A0E` |
| `Accent.Soft` (achtergrond actief item) | `#C6F432` 12% | `#C6F432` 25% |
| `Status.Success` | `#34D399` | `#059669` |
| `Status.Error` | `#F87171` | `#DC2626` |

De donkere achtergrond krijgt, net als de website, een subtiele radiale groene gloed linksboven (`#C6F432` 6%).

- **Lettertypen:** *Archivo* SemiBold voor koppen, *Inter* voor tekst en bediening. Beide OFL-1.1, als resource ingebouwd.
- **Vormen:** hoekradius 12 px (knoppen, invoervelden) en 16 px (kaarten, panelen); 1 px randen met `Border.Subtle`; iconen in afgeronde tegels.
- **Iconen:** Segoe Fluent Icons (Windows 11) met terugval op Segoe MDL2 Assets (Windows 10), beide standaard aanwezig.
- **Animaties:** paginawissel 150 ms (fade + 8 px verschuiving), hover/press-overgangen 100 ms. Animaties worden uitgeschakeld als Windows *Animatie-effecten* uit heeft staan.
- **Thema *Systeem*:** leest `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme` en schakelt live mee via `SystemEvents.UserPreferenceChanged`.

### 3.3 Venster

- Eigen titelbalk via `WindowChrome` in de huisstijl: logo, titel, minimaliseren, maximaliseren/herstellen, sluiten.
- Windows 11 *Snap Layouts* op de maximaliseerknop blijven werken (afhandeling van `WM_NCHITTEST` met `HTMAXBUTTON`).
- Donkere/lichte titelbalkrand via `DwmSetWindowAttribute` (`DWMWA_USE_IMMERSIVE_DARK_MODE`).
- Venstergrootte, -positie en gemaximaliseerde staat worden onthouden; een positie buiten de huidige schermen wordt teruggezet naar het primaire scherm.
- Minimale venstergrootte 720 × 480.

### 3.4 Layout

- **Zijbalk links:** 240 px met logo, navigatie-items (icoon + label, actief item met `Accent.Soft`-achtergrond en accentbalkje). Onder 900 px vensterbreedte klapt hij automatisch in tot 72 px (alleen iconen, tooltip met label).
- **Bedieningsbalk onder (altijd zichtbaar, 88 px):**
  - links: hoesje of type-icoon, titel en ondertitel (artiest of map);
  - midden: shuffle, vorige, afspelen/pauzeren (ronde accentknop), volgende, herhalen (uit/alles/één); daaronder een tijdlijn met verstreken en totale tijd;
  - rechts: dempen, volumeschuif, knop *wachtrij* (opent *Nu speelt*).
- De tijdlijn toont tijdens slepen de doeltijd en spoelt bij loslaten; klikken springt direct naar die positie.
- **Titel en artiest:** in deelproject 1 wordt de titel/artiest gelezen die LibVLC uit de metadata haalt. Zonder metadata wordt de bestandsnaam gebruikt.

### 3.5 Logo

Afgeronde tegel in het accentverloop met een afspeeldriehoek die overloopt in een »-vorm (verwijzing naar het Next Solutions-beeldmerk), naast het woordmerk **NEXIFY PLAYER** (Archivo, vet, hoofdletters). Bestanden:

- `assets/branding/nexify-logo.svg`
- `assets/branding/nexify-mark.svg`
- `assets/branding/nexify.ico` met 16, 24, 32, 48, 64, 128 en 256 px, gegenereerd uit de SVG.

### 3.6 Sneltoetsen

| Toets | Actie |
|---|---|
| Spatie | Afspelen / pauzeren |
| ← / → | Terug / vooruit met de ingestelde spoelstap |
| ↑ / ↓ | Volume +5 / −5 |
| M | Dempen aan/uit |
| N / P | Volgende / vorige |
| Ctrl+O | Bestanden openen |
| Ctrl+Shift+O | Map openen |
| Ctrl+, | Instellingen |

Sneltoetsen werken niet terwijl een tekstveld de focus heeft.

**Vorige:** als het huidige item langer dan 3 seconden speelt, begint *Vorige* het item opnieuw; anders gaat het naar het vorige item.

## 4. Gedrag en gegevensstroom

### 4.1 Opstarten

1. `App.OnStartup` configureert Serilog, laadt instellingen en taal en toont direct het hoofdvenster.
2. `IPlaybackEngine.InitializeAsync()` laadt LibVLC op de achtergrond. Totdat dat klaar is, zijn de afspeelknoppen uitgeschakeld en staat er een subtiele laadindicator in de bedieningsbalk.
3. Opstartparameters (bestands- of mappaden) worden geopend zodra de engine klaar is.
4. Er wordt **niet** automatisch verdergegaan met het laatst afgespeelde item.

**Doel opstarttijd:** het venster is binnen 1,5 s zichtbaar op een gemiddelde pc. Dit is een richtwaarde, geen geautomatiseerde test.

### 4.2 Openen van media

- **Formaatdetectie (`MediaFormats`)** op basis van extensie, niet hoofdlettergevoelig:
  - Audio: `.mp3 .wav .flac .aac .m4a .ogg .oga .opus .wma .alac`. ALAC zit meestal in `.m4a` en wordt dan als audio herkend.
  - Video: `.mp4 .m4v .mkv .avi .mov .wmv .webm .mpeg .mpg .ts .m2ts`.
- **Map openen (`FolderScanner`):**
  - doorzoekt ook alle submappen;
  - slaat verborgen en systeemmappen over, en mappen waar geen toegang toe is (die worden gelogd);
  - sorteert per map natuurlijk ("2" vóór "10") en geeft mappen vóór bestanden.
- **Niet-ondersteunde bestanden:** die worden overgeslagen, met één samenvattende melding: *"3 bestanden overgeslagen: formaat niet ondersteund."*
- **Wachtrij vervangen of aanvullen:** volgens de instelling *wachtrij vervangen of aanvullen bij openen* (standaard: vervangen). *Vervangen* start het eerste geopende item.
- **Recent geopend:** elk bestand dat succesvol begint met afspelen, wordt aan *Recent geopend* toegevoegd. Bestaat het al, dan schuift het naar boven.

### 4.3 Wachtrij (`PlayQueue`)

- Operaties:
  - `Add`, `AddRange`, `InsertNext`, `Remove`, `Move`, `Clear`;
  - `MoveTo(index)`, `Next()`, `Previous()`;
  - `Current`, `CurrentIndex`, `Items`;
  - `Shuffle` (bool), `Repeat` (`Off`/`All`/`One`).
- **Shuffle aan:** genereert een willekeurige afspeelvolgorde (Fisher-Yates) met het huidige item vooraan. De zichtbare lijstvolgorde verandert niet.
- **Shuffle uit:** gaat verder vanaf de positie van het huidige item in de normale volgorde.
- **`Next()` aan het einde:**
  - bij `Repeat.All` terug naar het begin (bij shuffle met een nieuwe willekeurige volgorde);
  - bij `Repeat.Off` geeft het `null` en stopt het afspelen.
- **`Repeat.One`:** `EndReached` speelt het huidige item opnieuw. Handmatig *Volgende* gaat wél naar het volgende item.
- **Verwijderen van het huidige item:** het afspelen gaat verder met het volgende item, of stopt als er geen is.
- De wachtrij stuurt `Changed`-events naar de UI. De klasse is niet thread-safe en wordt alleen vanaf de UI-thread gebruikt.

### 4.4 Opslag

Locatie: `%APPDATA%\Nexify Player\` (roamend, gebruikersgegevens) en `%LOCALAPPDATA%\Nexify Player\logs\`.

| Bestand | Inhoud |
|---|---|
| `settings.json` | `schemaVersion`, `theme`, `language`, `volume`, `isMuted`, `seekStepSeconds`, `openBehavior`, `shuffle`, `repeat`, `window` (`left`, `top`, `width`, `height`, `isMaximized`) |
| `recent.json` | `schemaVersion`, lijst van `{ path, kind, openedAtUtc }`, max. 20 |

- **Schrijven via `AtomicFileWriter`:** het bestand wordt eerst als `*.tmp` weggeschreven. Daarna vervangt `File.Replace` het origineel, of `File.Move` als het origineel nog niet bestaat.
- **Instellingen worden opgeslagen:**
  - direct na een wijziging op de instellingenpagina;
  - bij volume- en vensterwijzigingen gegroepeerd met een vertraging van 500 ms;
  - en altijd bij afsluiten.
- **Beschadigd of onleesbaar JSON-bestand:**
  - het bestand wordt hernoemd naar `*.corrupt.json` (een bestaande kopie wordt overschreven);
  - de app gaat verder met standaardwaarden;
  - de gebruiker krijgt de melding *"Je instellingen konden niet worden gelezen en zijn teruggezet. Een kopie is bewaard."*
- **Onbekende velden** worden genegeerd en **ontbrekende velden** krijgen standaardwaarden (voorwaarts compatibel voor updates).
- **Bewaarbeleid:** recente bestanden die niet meer bestaan, worden bij het tonen weggelaten maar niet direct uit het bestand verwijderd. Dan verdwijnen bestanden op een losgekoppelde USB-schijf niet permanent.

### 4.5 Lokalisatie

- **Taalbestanden:** `Languages/nl.json` en `Languages/en.json`, met platte sleutels zoals `"nav.home": "Home"` en placeholders als `{0}`.
- **Gebruik in XAML:** via een markup-extensie `{loc:T nav.home}`. Teksten werken live bij als de taal wordt gewisseld.
- **Nieuwe taal toevoegen:** een bestand toevoegen en de taal verschijnt in de lijst. De naam van de taal komt uit de sleutel `"language.name"`.
- **Ontbrekende sleutel in een taal:** terugval op Engels, en anders wordt de sleutel zelf getoond. Wordt gelogd als waarschuwing.
- **Eerste start:** Nederlands als `CultureInfo.CurrentUICulture` Nederlands is, anders Engels.

## 5. Foutafhandeling

| Situatie | Gedrag |
|---|---|
| Bestand bestaat niet (meer) | Melding *"Bestand niet gevonden: {naam}"*; item wordt overgeslagen. |
| Geen leesrechten | Melding *"Geen toegang tot {naam}"*; overslaan. |
| LibVLC kan het bestand niet openen of decoderen | Melding *"{naam} kan niet worden afgespeeld: het formaat wordt niet ondersteund of het bestand is beschadigd."*; overslaan. |
| Drie opeenvolgende afspeelfouten | Afspelen stopt, met melding *"Meerdere bestanden konden niet worden afgespeeld. Afspelen is gestopt."* Dit voorkomt een eindeloze reeks fouten. |
| LibVLC kan niet worden geladen (ontbrekende/beschadigde DLL's) | Duidelijk foutvenster met uitleg en advies om opnieuw te installeren. De app blijft open; afspeelfuncties zijn uitgeschakeld. |
| Beschadigd instellingen- of recentbestand | Zie 4.4. |
| Onverwachte uitzondering | Handlers op `DispatcherUnhandledException`, `AppDomain.UnhandledException` en `TaskScheduler.UnobservedTaskException` loggen de fout. De gebruiker krijgt een venster met een korte uitleg en een knop *Logmap openen*. Bij UI-thread-fouten blijft de app zo mogelijk draaien. |

- **Meldingen:** in de app als een kaart rechtsonder (toast). Ze verdwijnen na 6 s, zijn eerder te sluiten, en er staan maximaal 3 tegelijk in beeld.
- **Logging:** Serilog schrijft naar `%LOCALAPPDATA%\Nexify Player\logs\nexify-YYYYMMDD.log`, één bestand per dag, 7 bestanden bewaard, standaard op niveau Information.

## 6. Testen

### 6.1 Unittests (xUnit; draaien op Linux en Windows)

- **`PlayQueue`:**
  - toevoegen, invoegen, verwijderen en verplaatsen, ook als het huidige item wordt verplaatst of verwijderd;
  - `Next` en `Previous` aan de randen;
  - alle herhaalmodi;
  - shuffle: een permutatie zonder dubbelen, het huidige item vooraan, en uitzetten behoudt het huidige item;
  - een nieuwe volgorde bij `Repeat.All` met shuffle.
- **`MediaFormats`:** alle extensies, hoofdletters, onbekende extensies en bestanden zonder extensie.
- **`FolderScanner`:**
  - recursief zoeken met natuurlijke sortering;
  - overslaan van verborgen mappen;
  - een niet-toegankelijke map leidt niet tot een crash;
  - de telling van overgeslagen bestanden.
- **`JsonSettingsService`:**
  - opslaan en teruglezen geeft dezelfde instellingen;
  - standaardwaarden als het bestand ontbreekt;
  - een beschadigd bestand leidt tot een `.corrupt.json`-kopie en standaardwaarden;
  - onbekende en ontbrekende velden.
- **`AtomicFileWriter`:** bij een gesimuleerde fout tijdens het schrijven blijft het origineel intact.
- **`JsonRecentFilesService`:**
  - maximaal 20 items;
  - bestaande items schuiven naar boven;
  - verwijderen en wissen;
  - niet-bestaande bestanden worden gefilterd.
- **Lokalisatie:**
  - elk taalbestand heeft exact dezelfde sleutels als `en.json`;
  - de placeholders `{n}` komen overeen;
  - de terugvalvolgorde werkt.
- **Viewmodels** (met een nep-`IPlaybackEngine`):
  - afspelen/pauzeren, volgende en vorige (inclusief de regel van 3 seconden);
  - na `EndReached` volgt het volgende item;
  - na drie fouten stopt het afspelen.

### 6.2 Afspeel-integratietests (alleen Windows, in CI)

- **Testbestanden:** de CI-stap genereert met ffmpeg korte testbestanden van 3 seconden in MP3, WAV, FLAC, OGG, M4A (AAC), M4A (ALAC), MP4 (H.264), MKV, AVI, MOV, WebM (VP9), WMV en MPEG. Ze worden niet in de repository opgenomen.
- **Per bestand wordt gecontroleerd:**
  - de status bereikt `Playing`;
  - `Duration` ligt binnen ±0,5 s van de verwachte duur;
  - `Seek` naar 1,5 s klopt;
  - `EndReached` wordt gemeld.
- **Video:** de tests draaien met LibVLC-optie `--vout=dummy`, zodat ze zonder echt scherm werken.
- **Fouttest:** een bestand met willekeurige bytes en een `.mp4`-extensie geeft `PlaybackFailed`, met reden *formaat niet ondersteund of beschadigd*.

### 6.3 UI-smoketest (alleen Windows, in CI; FlaUI)

- De app start en het hoofdvenster verschijnt binnen 15 s.
- Navigeren naar Instellingen werkt, en het thema op *Licht* zetten wordt in `settings.json` vastgelegd.
- De taal op *English* zetten verandert het navigatielabel *Instellingen* in *Settings*.
- Starten met een testbestand als parameter laat de positie van de tijdlijn binnen 5 s oplopen.
- **Geïsoleerd:** de UI-test draait met de omgevingsvariabele `NEXIFY_DATA_DIR` naar een tijdelijke map, zodat de gebruikersgegevens van de runner niet worden gebruikt. `AppPaths` respecteert deze variabele.

### 6.4 Handmatige testlijst

`docs/TESTING.md` bevat een checklist voor de gebruiker op een echte Windows 10- en Windows 11-pc:

- het uiterlijk in beide thema's;
- het inklappen van de zijbalk;
- Snap Layouts;
- geluid in verschillende formaten;
- 1080p- en 4K-video;
- slepen en neerzetten;
- alle sneltoetsen;
- een beschadigd instellingenbestand;
- een losgekoppelde USB-schijf in *Recent geopend*.

## 7. Build en oplevering

**`.github/workflows/build.yml`** draait op `windows-latest`, bij elke push en pull request:

1. `dotnet restore`, en `dotnet build -c Release`, waarbij waarschuwingen als fout gelden in Core, Playback en Infrastructure;
2. de unittests;
3. ffmpeg installeren (`choco install ffmpeg`), de testbestanden genereren en de afspeel-integratietests draaien;
4. `dotnet publish` voor `win-x64` en `win-x86` (self-contained, `PublishReadyToRun` aan voor een snellere start);
5. de UI-smoketest tegen de x64-publicatie;
6. de publicaties als downloads uploaden (`NexifyPlayer-win-x64.zip`, `NexifyPlayer-win-x86.zip`) en de testresultaten (`.trx`).

**Versienummer:** in `Directory.Build.props`; deelproject 1 levert versie `0.1.0`.

**Lokaal op Linux (ontwikkelsessie):** `dotnet build` met `EnableWindowsTargeting=true` en de unittests, voor snelle terugkoppeling. Draaien en de Windows-tests gebeuren uitsluitend in CI.

**Opgeleverde documentatie:**
- `README.md`: wat het is, de status en het downloaden van de build;
- `docs/BUILD.md`: lokaal bouwen op Windows en in CI;
- `docs/TESTING.md`;
- `THIRD-PARTY-NOTICES.md`: LibVLC (LGPL-2.1), LibVLCSharp (LGPL-2.1), Inter en Archivo (OFL-1.1), CommunityToolkit.Mvvm (MIT), Serilog (Apache-2.0) en FlaUI (MIT).

## 8. Afronding van deelproject 1

Deelproject 1 is klaar als:

1. de CI-build op Windows groen is: build, unittests, afspeeltests, publicatie voor x64 en x86, en de UI-smoketest;
2. alle functies uit hoofdstuk 3 en 4 werken, zonder knoppen of menu-onderdelen die niets doen;
3. de gebruiker de handmatige testlijst op Windows heeft doorlopen en de bevindingen zijn verwerkt.
