# SOW Broadcast – uudelleenrakennusspesifikaatio

> Tämä tiedosto on tarkoitettu uuden projektin juureen (`CLAUDE.md`), jotta Claude (tai ihminen) voi rakentaa
> SOW Broadcast -sovelluksen uudelleen valitulla kielellä/työkalulla. Se kuvaa **mitä** nykyinen sovellus tekee,
> **missä muodossa** se puhuu ympäristönsä (OBS, overlayt, SOWDraft) kanssa, **miltä** uuden version pitää näyttää
> (Suomi OW ry:n graafinen ohjeistus) ja **mitä kannattaa tehdä toisin**.
>
> Lähde: nykyinen Python/PyQt5-toteutus (`SOWBroadcast.py`, `server.py`, `draft_link.py`, `tournament_link.py`,
> `HTML/*.html`, `Scoreboard/**`). Rivinumerot viittaavat tilaan 2026-10-15. Merkintä **[EHD-n]** = ehdotus, joka
> odottaa omistajan hyväksyntää (ks. luku 14). Merkintä **[KYS-n]** = avoin kysymys omistajalle (luku 15).

---

## 0. Työskentelyohjeet Claudelle

1. **Päätetty (omistaja, 2026-10-15): C# + Avalonia UI, vain Windows, koko käyttöliittymä englanniksi.**
   Kontrolliohjelma on natiivi työpöytäsovellus. **Ei web-pohjaista käyttöliittymää** (ei Electron, ei Tauri,
   ei WebView-shelliä). Tekninen pino: luku 2b. Kaikki päätökset: luku 15.
2. **Overlayt pysyvät HTML/CSS/JS:nä.** Ne ovat OBS Browser Source -lähteitä, eikä OBS osaa muuta. Ne eivät ole
   "sovelluksen UI", vaan sovelluksen tuottama tuote. Sovelluksen sisälle ei rakenneta web-käyttöliittymää.
   **Overlayjen visuaalista ilmettä ei päivitetä brändiohjeistuksen mukaiseksi** (KYS-4); vain niiden *datankäsittely* siirretään
   uuteen kontraktiin (KYS-6, ks. luku 9).
3. **Siirrytään uuteen kontraktiin (KYS-6).** Ei pysyvää taaksepäin yhteensopivuutta: uusi sovellus, uusi tilarajapinta (EHD-2),
   päivitetyt OBS-scenet ja päivitetyt overlayt (datan haku uudesta rajapinnasta, ulkoasu ennallaan) julkaistaan **yhtenä pakettina**.
   Silti **vaiheessa M1 uusi sovellus tuottaa vielä vanhan tiedostokontraktin** (luku 6–7), jotta vanhat overlayt toimivat oraakkelina ja
   golden-testit (luku 13) ovat mahdollisia. Vanha kontrakti poistetaan vasta kun uudet overlayt on otettu käyttöön.
4. **UI-kieli on yksinomaan englanti** (KYS-5): kaikki tekstit, virheilmoitukset ja tilarivit. Luvun 8 suomenkieliset merkkijonot
   korvataan englanninkielisillä (ks. 8.0). Lokalisointirunkoa ei tarvita (resurssitiedosto kuitenkin suositeltava, jotta tekstit ovat yhdessä paikassa).
4. **Vanha sovellus on testioraakkeli.** Aja sama tila molemmilla ja vertaa `Scoreboard/`-puun sisältöä
   (ks. luku 13, hyväksyntätestit).
5. **Älä toista bugeja.** Luku 12 listaa vanhan sovelluksen virheet ja oudot kohdat. Ne on tarkoituksella
   *ei*-kopioitava lista.
6. **Ulkoasu = yhdistyksen graafinen ohjeistus** (luku 11). Lähde: `suomiow nettisivut/docs/brand/graafinen-ohjeistus.pdf`
   (v1.0, lokakuu 2026). PDF on totuus; tämän tiedoston luku 11 on tiivistelmä.
7. Käyttäjäkieli omistajalle: suomi. Koodi, tunnisteet ja commit-viestit: englanti (ellei toisin sovita).
8. Älä kovakoodaa polkuja (`C:\SOWBroadcast`, `C:\Suomi OW koodiprojektit\...`), porttia (8324) tai versionumeroa
   useaan paikkaan. Konfiguraatio yhteen paikkaan.
9. Henkilötietoa (pelaajanimet, joukkueet) on `autosave.json`/`match.json` -tiedostoissa. Älä kopioi oikeaa dataa
   testeihin tai committeihin; käytä synteettistä.

---

## 1. Mikä järjestelmä on

**SOW Broadcast** on Suomi OW ry:n (suomalainen Overwatch-yhteisö) Overwatch 2 -esportslähetysten ohjausohjelmisto.
Tuottaja syöttää ottelutiedot (joukkueet, pelaajat, mapit, pisteet, bannit, tulostaulukot, pudotuspelikaavio,
odotusruudun tekstit ja ajastin) työpöytäsovellukseen. Sovellus kirjoittaa tilan levylle ja ilmoittaa paikalliselle
palvelimelle, joka työntää muutosviestin OBS:n Browser Source -overlayille. Overlayt hakevat päivittyneet tiedostot ja
animoivat muutokset ruudulle.

### Komponentit

| # | Komponentti | Nykyinen toteutus | Rooli |
|---|---|---|---|
| 1 | **Kontrollisovellus** | `SOWBroadcast.py` (PyQt5, ~5460 riviä) | Kaikki muokkaus, tilan omistus, vienti levylle |
| 2 | **Paikallinen HTTP-palvelin** | `server.py` (stdlib `ThreadingHTTPServer`), portti 8324 | Staattiset tiedostot + SSE `/events` + `POST /notify` |
| 3 | **Overlayt** | `HTML/*.html` (17 tiedostoa) | OBS Browser Source, 1920×1080, lukevat `Scoreboard/**` |
| 4 | **SOWDraft-linkki** | `draft_link.py` (Socket.IO-asiakas) | Täyttää mapit, pickit, bannit, voittajat live-draftista |
| 5 | **Turnauslinkki** | `tournament_link.py` (REST + Socket.IO-trigger) | Täyttää standings- ja bracket-datan SOWDraftin turnausjärjestelmästä |
| 6 | **OBS-scenekokoelmat** | `sowbroadcastscenes.json`, `SOWBROADCAST.json` | Tuotavat OBS-scenet (18 kpl) |
| 7 | **Käynnistin & asennin** | `launch.bat`, PyInstaller-specit, Inno Setup (`setup.iss`) | Jakelu Windowsille |

### Tietovirta

```
 Käyttäjä muokkaa UI:ta ──► Update-painike / linkkitapahtuma
        │
        ▼
  _collect_state()  ──►  _diff_for_scoreboard(vanha, uusi) ──► changed[] -avaimet
        │                                                         │
        ├─► kirjoita Scoreboard/** (txt/json/png)                 │
        ├─► match.json, assets.json, autosave.json (juuri)        │
        └─► POST http://127.0.0.1:8324/notify {"changed":[...]} ◄─┘
                                   │
                      server.py: SSE /events  ──►  overlay (EventSource)
                                                      │
                                       fetch(tiedosto?_=Date.now()) → renderöi + animoi
```

Ulkoiset palvelut: ainoastaan **sowdraft.fi** (Socket.IO + REST + logokuvat). OBS tarjoaa selaimen, tekstilähteet,
NDI:n, VLC-musiikin, pelikaappauksen. Sovellus ei puhu OBS:lle suoraan (ks. **[EHD-6]**).

---

## 2. Suositeltu uusi arkkitehtuuri

Pidä rajapinnat (luvut 6–7) ennallaan, mutta siisti sisus:

```
┌──────────────────────────── yksi prosessi ────────────────────────────┐
│  UI (natiivi)  ◄──►  Tila (yksi totuus, versioitu)  ◄──►  Palvelut     │
│                              │                          · DraftLink     │
│                              ▼                          · TournamentLink│
│                       Exporter (txt/json/png)           · ReplayWatcher │
│                              │                          · Timer         │
│                              ▼                                          │
│                  HTTP-palvelin + SSE-hub (sisäänrakennettu)             │
└────────────────────────────────────────────────────────────────────────┘
```

- **Yksi prosessi, yksi palvelin.** Nykyinen sovellus käynnistää palvelimen kahdesti samaan porttiin (`SOWServer.exe`
  + säie GUI:ssa), ja SSE-tila on moduulikohtainen, joten käyttäytyminen on epädeterminististä (luku 12, #1).
  Sisäänrakennettu palvelin riittää; erillinen `SOWServer.exe` ja `launch.bat`-koreografia poistuvat. **[EHD-1]**
- **Tila = yksi puhdas, serialisoitava objekti** (luku 3) + `schemaVersion`. UI on tilan näkymä; palvelut
  (draft/tournament/timer) muokkaavat tilaa saman rajapinnan kautta kuin UI.
- **Exporter on puhdas funktio** `exportState(prev, next) → (tiedostomuutokset, changedKeys)`. Helppo testata
  golden-tiedostoilla.
- **UI-säie ei tee verkkoa eikä levy-I/O:ta raskaasti.** Vanha sovellus lataa turnauslogot synkronisesti UI-säikeessä
  (jäädyttää ikkunan) ja POSTaa `/notify`:n UI-säikeestä. Uudessa: taustatehtävät + viestijono UI:lle.
- **Atomiset kirjoitukset** (kirjoita temp-tiedostoon, vaihda nimeksi) kaikkeen JSON-tilaan. Vanha voi jättää
  autosaven rikkinäiseksi kesken kirjoituksen.
- **Ei sivuvaikutuksia latauksessa.** Vanha `_apply_state` laukaisee `updated`-signaaleja kesken latauksen, mikä
  kirjoittaa puolivalmiin tilan autosaveen yli (luku 12, #9). Lataus = "aseta tila kerralla", sitten yksi päivitys.

---

## 2b. Tekninen pino: C# + Avalonia (vain Windows)

Suositukset (omistaja on päättänyt vain C# + Avalonia + Windows; alla olevat kirjastovalinnat ovat ehdotuksia ja ne saa korvata perustellusti):

| Alue | Suositus | Huom |
|---|---|---|
| Runtime | **.NET 8 LTS** (tai uudempi LTS) | `PublishSingleFile` + self-contained → ei erillistä runtime-asennusta |
| UI | **Avalonia 11** + oma teema `SowTheme.axaml` (ei Fluent sellaisenaan) | design-tokenit luvusta 11.5 `ResourceDictionary`ssä; `ControlTheme`/`Styles` per komponentti – **ei globaalia tyylikaskadia ja ylikirjoituksia vanhemmilla** (ks. 11.6) |
| Malli | **MVVM** (CommunityToolkit.Mvvm tai ReactiveUI) | tila = `AppState` (immutable record tai `INotifyPropertyChanged`-puu) + `ViewModel`it; UI ei kosketa levyä/verkkoa |
| HTTP + SSE | **ASP.NET Core Kestrel** (minimal API) prosessin sisällä | `/events` SSE (`Results.Stream`/`IAsyncEnumerable`), `/api/state`, `/notify`; staattiset kansiot erikseen `UseStaticFiles` sallituilla `PhysicalFileProvider`eilla |
| JSON | **System.Text.Json** (source generators) | `JsonUnmappedMemberHandling.Skip`, `schemaVersion`-migraatiot |
| Socket.IO | **SocketIOClient** (NuGet, Socket.IO v4) | tarkista yhteensopivuus sowdraft.fi:n palvelinversioon (v3/v4) ennen sitoutumista |
| REST | `HttpClient` (+ `User-Agent: Mozilla/5.0 (compatible; SOWBroadcast)`) | |
| Kuvat | **SkiaSharp** / `Avalonia.Media.Imaging` | logo → PNG-uudelleenkoodaus; vältä `System.Drawing` |
| Fontit | upotettu `AvaloniaResource` (Saira, Inter, Russo One TTF:t) | `FontFamily="avares://…#Saira Semi Condensed"` |
| Tiedostovahti | `FileSystemWatcher` replay-vahtijalle (+ vakautusviive) | korvaa 0,5 s pollauksen |
| Ajastin | `PeriodicTimer` + `Stopwatch`/`endsAt` (EHD-9) | |
| Asennus & päivitys | **Inno Setup** (valittava kansio, oletus `C:\SOW Broadcast`; toteutettu, Velopack hylättiin) | päivitystarkistus lataa uuden asentimen GitHubista (EHD-15/16) |
| Testit | **xUnit** + golden-tiedostot; Avalonia.Headless UI-savutesteihin | |
| Lokitus | Serilog (tiedosto) | |

Projektirakenne (ehdotus): `SowBroadcast.Core` (tila, säännöt, exporter – ei UI-riippuvuuksia; 100 % testattavissa) ·
`SowBroadcast.Server` (Kestrel+SSE) · `SowBroadcast.Integrations` (DraftLink, TournamentLink, ReplayWatcher) ·
`SowBroadcast.App` (Avalonia UI, teema, ViewModelit) · `SowBroadcast.Tests`.

Avalonia-erityishuomiot (opittu PyQt-kokeilusta, luku 11.6):
- Pitkät välilehdet `ScrollViewer`in sisään. Vähimmäisikkuna 1200×750.
- Haettava combo: `AutoCompleteBox` ei riitä (vaatimus: substring-haku popupissa, nuolinäppäimet, Esc). Tee oma `SearchableComboBox` (`TemplatedControl`) heti M2:ssa.
- Värivalitsin: Avalonia `ColorPicker`/`ColorView` teemattuna (EHD-12), ei OS-dialogia. Tiedostovalinnat: `StorageProvider` (OS-natiivi, hyväksytty).
- Tee `FocusAdorner`/`:focus-visible` -tyyli: 2 px `#23C5D7`.
- High-DPI toimii oletuksena; tarkista Saira-fontin renderöinti 125/150/200 % skaalauksella.

---

### 2c. Toteutustila (päivitetään vaiheittain)

**M0 valmis (2026-10-15, haara `v3`)** – `src/SowBroadcast.sln`: `SowBroadcast.Core` (tilamalli + `StateStore`), `SowBroadcast.App` (Avalonia, assembly `SOWBroadcast.exe`, versio **3.0.0**), `SowBroadcast.Tests` (xUnit, 18 testiä).
Toteutuksessa tehdyt päätökset, jotka poikkeavat luvun 2b ehdotuksista:
- **Avalonia 11.3.22 (ei 12.x).** Avalonia 12.x:n lähdegeneraattori vaatii Roslyn 4.14:n, mutta asennettu .NET SDK 8.0.426 sisältää 4.11:n (`InitializeComponent` ei generoidu). 12.x:ään voi siirtyä kun SDK päivitetään (.NET 9/10 SDK voi kääntää net8.0-kohteen). Kohde säilyy `net8.0`.
- **Teema** = `Theme/SowResources.axaml` (palettitokenit + Fluent-resurssien ylikirjoitukset, tumma variantti) + `Theme/SowStyles.axaml` (tyyliluokat: `h1`, `h2`, `label`, `muted`, `result`, `card`, `cardTitle`, `header`, `pill`, `primary`, `statusbar`). Fluent on pohjana mutta kaikki värit tulevat brändipaletista; **yksi `Button.primary` (teräsyaani) per näkymä**, muut napit leijonansinisiä. Fokus = 2 px teräsyaani (varmistettu kuvakaappauksella).
- **Fontit** upotettuina `Assets/Fonts` (Saira SemiCondensed Bold/ExtraBold, Inter 18pt Regular/SemiBold, Russo One + OFL-lisenssit); fonttiperheet avaimilla `SowHeadingFont`, `SowBodyFont`, `SowDisplayFont`. Täydet fonttisetit omistajan kansiossa `Logos + fonts/` (ei versionhallinnassa).
- **Logot**: `Logos + fonts/suomiow_rintakuva.png` = **Tunnus** (kruunattu leijonan pää; sovellusikoni ja otsikkopalkki), `suomiow_.png` = Päälogo, `suomiow_suomioverwatch.png` = Pitkä nimi. UI:hin skaalatut PNG:t ja monikokoinen `app.ico` ovat `src/SowBroadcast.App/`.
- **Tallennus**: `StateStore` (System.Text.Json, `SnakeCaseLower` → vanhat avaimet; tuntemattomat avaimet ohitetaan; `winner` johdetaan pisteistä; pelaajat/mapit täytetään 8/7:ään; atominen kirjoitus + `.bak` + palautus korruptoituneesta). Datan juuri: `%LOCALAPPDATA%\SOWBroadcast` tai `SOWB_ROOT`. Autosave latautuu käynnistyksessä ja tallentuu sulkiessa sekä Save/Load-toimintojen yhteydessä.
- **Dev-apu (vain Debug)**: `SOWBroadcast.exe --screenshot <tiedosto.png> [--tab <n>] [--size WxH] [--focus]` renderöi ikkunan PNG:ksi ja sulkeutuu – käytetään teeman visuaaliseen tarkistukseen ilman OS-tason kuvakaappausta. THEME-välilehti (kontrollien galleria) näkyy vain Debug-käännöksessä.
**M1 valmis (2026-10-16)** – palvelin, SSE, viejä ja golden-testit (61 testiä). Havainnot ja päätökset:
- **Golden-oraakkeli**: `tools/golden/generate_legacy_golden.py` ajaa vanhan Python-sovelluksen ilman ikkunaa (Qt offscreen, ikkuna `show()`-tilassa, muuten `current_map` on aina null) synteettisellä tilalla
  (`src/SowBroadcast.Tests/Fixtures`) ja tallentaa viennit (`Golden/default`, `Golden/full`) sekä diff-avaimet (`Golden/diff_keys.json`). Koneen polut tokenisoidaan (`{FIXTURES}`). Ajetaan uudelleen vain kun vanha kontrakti tallennetaan tarkoituksella uudelleen.
- **Viejä** (`Core/Export/ScoreboardExporter`): tuottaa vanhan sisällön tavutarkasti `.txt`-tiedostoille (CRLF kuten vanha Windows-teksti­tila) ja JSON:ille semanttisesti (järjestys/sisennys ei merkitse); PNG-logot vain mittojen osalta (kooderi eroaa).
  Ei toteuteta (jäänteet): `maps.txt`, `map_pool.txt`, `seriestext.txt`, `__last_gui_touch.txt`. Korjattu vs. vanha: tyhjennetty logo poistaa vanhentuneen PNG:n; `winner`/`current_map` johdetaan aina (`StateDerivations`); `match.json` = vanha muoto (`LegacyMatchJson`: `map_pool: []`, `format_type`, `division`, `first_to = maps_count`).
- **Diff** (`StateDiff`): vanha avainsanasto ja ehdot; duplikaatit poistettu (vanha saattoi lähettää `maps` kahdesti). Assettien (`assets.*`) oikea vertailu tulee M2:ssa; ensimmäinen päivitys raportoi kaikki avaimet.
- **Palvelin** (`SowBroadcast.Server`, Kestrel prosessin sisällä): `127.0.0.1:8324` (varaportit 8325… jos varattu), SSE `/events` (jono per asiakas, `Last-Event-ID`-toisto, 15 s keepalive, edellisen ajon id:lle yksi täysi päivitys; uusi asiakas ei saa vanhaa viestiä), `POST /notify` vaatii `X-SOWB-Token` (401/400/413/204),
  vain sallitut kansiot (`/HTML`, `/Scoreboard`, `/Music`, `/Videos`, `/Highlights`; `autosave.json` ja `..`-polut → 404), no-cache-otsakkeet kaikkialla. **Kansiorakenne**: generoitu `Scoreboard/` on käyttäjäkohtainen (`%LOCALAPPDATA%\SOWBroadcast\Scoreboard`, tai `SOWB_ROOT`), ja vain bundlatut kuvakansiot
  (`Heroes`, `HeroesBody`, `Maps`, `Gametypes`, `Roles`, `General/Icons`) palvellaan lisäksi asennuskansiosta (käyttäjän kansio ensin). Näin vanhat generoidut tiedostot asennuskansiossa eivät vuoda overlayihin. Palvelin palvelee vain kansiot jotka ovat olemassa käynnistyksessä → `AppServices` luo ne.
- **Livetodennus**: vanha `scoreboard.html` (muuttamaton) renderöi uuden sovelluksen viennin ja päivittyi SSE:llä (`--demo-live`-kytkin Debug-käännöksessä nosti pisteet 1 → 6 ilman sivun latausta). Käynnistys julkaisee tallennetun tilan (KYS-11d). Julkaisu on toistaiseksi vain Debug → *Publish current state*; oikeat Update-napit tulevat välilehtien mukana (M3–M5).
- **Dev-aputyökalut (vain Debug)**: Debug-valikko (*Publish current state*, *Load sample match*, *Open scoreboard overlay*), `--demo-live`.
- **Valikot**: File (Load/Save/Save As/Quit), Customize (assetit, M2) ja Teams (Export/Import Home/Away, M3) toimivat. Kaikki viisi välilehteä ovat toteutettu (M3–M5).

**M2 valmis (2026-10-16)** – assetit (128 testiä yhteensä). Havainnot ja päätökset:
- **Katalogi kerroksittain** (`Core/Assets/AssetService`): käyttäjän `Scoreboard/<Kat>/index.json` (datakansio) → bundlattu `Scoreboard/<Kat>/index.json` (asennuskansio) → kuvakansion skannaus. Bundlatut `index.json`-tiedostot (Heroes, Maps, Gametypes: 54 heroa, 31 mappia, 5 pelitilaa) ovat nyt versionhallinnassa; käyttäjän lisäykset kirjoitetaan vain datakansioon, eikä bundlattuja kuvia kopioida turhaan.
  Hero→rooli luetaan indeksistä (`HERO_ROLES`-taulukkoa ei toteuteta); `Slug.Make` käyttää ei-latinalaisille nimille hajautetta, joten ne eivät enää törmää `"item"`-arvoon (luku 12 #16). `Slug.HeroBodyFile` säilyttää vanhat ylikirjoitukset (EHD-10:n manifesti korvaa ne myöhemmin).
- **Julkaisusääntö (KYS-9/EHD-3)**: assetin lisäys/muutos/poisto kirjoitetaan levylle heti, mutta `assets.<kategoria>` ilmoitetaan overlayille vasta seuraavassa Updatessa (`IOverlayPublisher.MarkAssetsChanged` kerää avaimet; Update liittää ne `changed`-listaan). Muutos nostaa "Unsent changes" -merkin. Vanhan diffin vertailuaukko (rooli/body/kuva ei laukaise) poistuu, koska avain tulee suoraan muutoksesta.
- **Haettava combo** (`Controls/SearchableComboBox`, `SearchableListModel` testattavana ilman UI:ta): substring-haku kirjainkoosta riippumatta, ylös/alas/Enter/Esc, rulla vaihtaa arvoa, leveys max(combo, 220), korkeus clamp(40+22·rivit, 120..320). Win32:ssa `OverlayPopups = true` (`Program.cs`), jotta popup piirtyy ikkunan sisällä ja kuvakaappaus (RenderTargetBitmap) näkee sen.
- **Dialogit**: `AssetManagerWindow` (Heroes/Maps/Game Modes, vanhan käytöksen mukaiset virheilmoitukset `Missing name` / `Missing body image`), `BulkImportWindow` (`Imported X heroes and Y maps.`). Näkymämallit (`AssetManagerViewModel`, `BulkImportViewModel`) testataan feikatulla `IImagePicker`/`IMessages`-parilla.
- **Dev-apu**: `--screenshot <png> --dialog heroes|maps|modes|bulk [--select <nimi>]` renderöi dialogin PNG:ksi.
- **Tunnettu dataongelma**: `Scoreboard/Heroes/d-va.png` puuttuu vaikka indeksi listaa sen (vanhan sovelluksen virhe, luku 12 #16) – D.Va:n logo-esikatselu on tyhjä kunnes kuva toimitetaan.

**M3 valmis (2026-10-16)** – MATCH-välilehti (159 testiä yhteensä). Havainnot ja päätökset:
- **Rakenne**: `MatchViewModel` (muokattava puoli: `TeamPanelViewModel` ×2 + `PlayerRowViewModel` ×8, `MapCardViewModel` ×7, formaattipalkki) peilaa `AppState`n ottelun osan (`Load` / `ApplyTo`) ja ilmoittaa käyttäjän muokkauksista (`Edited`, ei latauksen aikana).
  `MainWindowViewModel` omistaa tilan: `CollectMatch()` ottaa ruudun sisällön tilaan ennen julkaisua, tallennusta ja autosavea; ulkoa asetettu tila (Load, palautus, Swap, Reset, Import) näytetään ruudulla. Puhtaat toiminnot Coressa: `MatchOperations.Swap/Reset`, `TeamFile.Export/Import`.
- **Julkaisusääntö**: vain **Update** lähettää (kirjoittaa kaikki viennit + SSE + autosave). Muokkaus, Reset, Swap Teams ja Team Import vain autosaavat ja nostavat "Unsent changes" -merkin (KYS-9). Swap/Reset/Import eivät kysy vahvistusta (kuten vanha).
- **Säännöt vanhasta säilytetty**: Swap vaihtaa koko joukkueen (pisteet, pelaajat), väri kulkee mukana vain jos poikkeaa slotin oletuksesta, mapeissa vaihtuvat pisteet/pick/banit mutta `completed` ei; Reset nollaa joukkueet, värit oletuksiin ja mapit mutta ei formaattipalkkia, general-asetuksia eikä linkkejä;
  heron valinta asettaa roolin katalogista (`HERO_ROLES`-taulukkoa ei ole), roolia ei koskaan tyhjennetä, ja lataus ei ylikirjoita tallennettua roolia (vanhan bugi #7 korjattu); pick-listan T1/T2 näyttävät joukkueen nimen, tallennusarvo pysyy `—`/`T1`/`T2`. Formaattipalkki piilottaa kortit/rivit mutta data säilyy; `general.first_to` = `maps_count` (kuten vanha).
- **Parittomuustesti**: legacy-golden `full` kulkee ruudun läpi (Load → ApplyTo) ja vienti on tavutarkasti sama kuin suoraan viedessä.
- **Värivalitsin**: `Controls/ColorField` (väripaletti + heksakenttä, teemattu flyout) on EHD-12:n ensimmäinen versio; "viimeksi käytetyt" ja kontrastivaroitus tulevat M9:ssä. General-välilehti (M4) käyttää samaa kontrollia.
- **Linkitys**: *SOWDraft Live Link* toteutettu M6:ssa (alla). Joukkueen logon valinta tukee png/jpg/jpeg/webp (vanhan `.svg` jää pois, koska SkiaSharp ei pura SVG:tä ilman lisäpakettia).
- **Dev-apu**: `--sample` (lataa esimerkkiottelun), `--open-colour` (avaa värivalitsimen) `--screenshot`-kytkimen kanssa.

**M4 valmis (2026-10-16)** – GENERAL- ja WAITING SCREEN -välilehdet (203 testiä yhteensä). Havainnot ja päätökset:
- **Rakenne**: `GeneralViewModel` (host/casterit, kaksi logoa, status text, 8 overlay-väriä `ColorField`illä) ja `WaitingViewModel` (videokansio, ajastin, tekstit, ticker override, 6 somekenttää) peilaavat tilan osiaan kuten Match. `MainWindowViewModel.CollectTabs()` kerää kaikki kolme välilehteä tilaan; `ShowState` lataa kokonaisen tilan kaikille (Load, autosave-palautus, sample), mutta Swap/Reset/Import päivittävät vain Matchin, joten käynnissä oleva ajastin ei keskeydy.
- **Osittainen julkaisu**: `IOverlayPublisher.PublishPart(state, PublishScope)` – `General` (general-tiedostot + status.txt), `Waiting` (waiting-tiedostot + status.txt), `Timer` (ajastintiedostot, `waiting.timer` lähtee aina heti), `TimerTick` (vain tiedostot, ei SSE:tä). Osittainen julkaisu päivittää vain oman osansa viimeksi julkaistuun tilaan, joten seuraava täysi Update raportoi muiden osien muutokset oikein (vanhan bugi #9 korjattu) ja vain oman osansa avaimet lähtevät.
- **"Unsent changes" lähteittäin**: merkki seuraa mitkä lähteet (match, general, waiting, assets, file) ovat lähettämättä; Update (General) tyhjentää vain `general`-lähteen, täysi Update kaikki. Ajastinkomennot ja kello eivät ole lähettämättömiä muutoksia.
- **Ajastin** (`Core/Waiting/CountdownTimer`): laskee monotonista kelloa vasten loppuaikaan (EHD-9:n `endsAt`-periaate), joten ei ajaudu eikä tarvitse sekuntikirjanpitoa; vanhan säännöt säilyvät (säätö pysäytettynä siirtää presetin, Reset palaa presetiin, Start nollasta aloittaa presetistä, lataus palauttaa pysäytettynä). Ikkunan 250 ms `DispatcherTimer` vain päivittää näytön ja kirjoittaa `timer_seconds.txt` kun sekunti vaihtuu. **Vanhat overlayt pollaavat `timer_seconds.txt`:tä sekunnin välein**, joten tiedostokontrakti pidetään M8:aan asti; `endsAt`-laskenta overlayssa (ei tiedostokirjoituksia sekunnissa) tulee uuden kontrakin mukana M8:ssa.
- **Säännöt vanhasta**: somekenttien normalisointi (`SocialHandles`, testattu tapaukset luvun 13 mukaan); tyhjä teksti → oletusteksti; "Reset this tab" (Waiting) palauttaa oletusvideot, tekstit (lopputeksti yhtenäisesti ilman huutomerkkiä), ajastimen ja tickerin mutta ei somekenttiä; ei lähetä, paitsi että pysäytetty käynnissä ollut ajastin pysäytetään myös overlaylle. Videokansion valinta: oletuskansio (`Highlights` datakansiossa) tai oma; oletuksen valinta tyhjentää polun, selaus poistaa oletuksen valinnan. Muutokset vanhaan: videokansio/ajastimen/vain-Update-säännön mukaan kentät eivät lähetä itsestään (vanha lähetti oletusvalinnan vaihdosta).
- **Värit**: Start/Pause/Reset-napit käyttävät hyväksyttyjä toiminnallisia värejä (KYS-3; Start tummennettu `#1E8449`, jotta valkoinen kuvake täyttää kontrastin); ajastimen numero Russo One teräsyaanilla (ainoa iso teräsyaanipinta).

**M5 valmis (2026-10-16)** – STANDINGS- ja BRACKET-välilehdet (248 testiä yhteensä). Havainnot ja päätökset:
- **Rakenne**: `StandingsViewModel` (ryhmät → `StandingsGroupViewModel` → `StandingsRowViewModel`, "Show in standings.html" -valinta) ja `BracketViewModel` (otsikko/stage, pohja, 16 joukkuerivin lista, kierrokset ja ottelut `BracketMatchViewModel`) peilaavat tilaa kuten muutkin välilehdet; yhteinen kantaluokka `EditableObject` (Edited-tapahtuma, ei latauksen aikana). Puhtaat säännöt Coressa: `StandingsOperations` (tyhjien rivien poisto, Update + sort, joukkueet kaaviota varten), `BracketTemplates` (6 pohjaa, `MatchIdSource`, double-elim-näkymän ja pronssiottelun säännöt).
- **Update-painikkeet ovat välilehtikohtaisia**: `PublishScope.Standings` ja `PublishScope.Bracket` kirjoittavat vain oman tiedostonsa ja lähettävät vain oman avaimensa (`standings` / `bracket`), eivätkä vie lähettämättömiä Match-muokkauksia mukanaan (vanha lähetti täyden `_update()`:n). Reset this tab ei lähetä kummassakaan (vanhan Bracket-reset lähetti; KYS-9). Muokkaus ei lähetä (vanhan bugi #6: jokainen Title/Stage-näppäily päivitti kaiken).
- **Standings**: Update + sort poistaa tyhjät rivit, lajittelee `(−points, −map_diff, nimi)` ja antaa **kaikille** riveille sijan 1..n (korvaa käsin kirjoitetut; kuten vanha koodi, vaikka spesifikaation 4.5 sanamuoto antoi ymmärtää toisin). Muulloin (vienti, tallennus) vain rank=0-rivit saavat sijan `ApplyRanks`illa **ruudun rivejä muuttamatta**, joten sijat päivittyvät pisteiden mukana kunnes Update + sort lukitsee ne (vanha kirjoitti sijat takaisin ruudulle jo keräyksessä, jolloin ne jäivät jumiin). `played/draws/sf/sa` säilyvät läpi muokkauksen. "Remove Selected" on korvattu rivikohtaisella ✕-napilla.
- **Bracket**: pohjan lataus korvaa kaavion (kuten vanha, ei vahvistusta); double-elim-näkymän valinta näkyy kierroskokojen perusteella (6- ja 8-tiimin), pronssiottelu vain single-elimillä ja sen lisäys/poisto säilyttää muut ottelut. Joukkueen valinta kopioi nimen, logon ja (tilasta ladatulle) lyhenteen listasta; kun listan joukkueen nimi/logo muuttuu, sen valinneet ottelut päivittyvät (vanhassa kopio jäi vanhaksi). Listan ulkopuolinen nimi (esim. turnauslinkistä) säilyy sellaisenaan, samoin ottelun `id/bo_label/status/scheduled_at`. "Import from Standings" avaa valintaikkunan (`TeamImportWindow`, kaikki valittuina); tyhjällä standingsilla ilmoitus `No teams found in standings.`
- **Pariteetti**: legacy-golden `full` kulkee Standings- ja Bracket-välilehtien läpi ja `standings.json` / `bracket.json` ovat tavutarkasti samat kuin suoraan viedessä.
- **Linkitys**: toteutettu M6:ssa (ks. alla). Logon valinta png/jpg/jpeg/webp (ei `.svg`).


**M6 valmis (2026-10-16)** – SOWDraft-linkitys luvun 8.4 mukaan (342 testiä yhteensä; ajettu myös oikeaa SOWDraftia vastaan). Havainnot ja päätökset:
- **Rakenne**: uusi projekti `SowBroadcast.Integrations` (`LinkService`, `LogoCache`, `HttpFetcher`, `SocketIoConnection` SocketIOClient 4.0.5:llä; Socket.IO v4). `SowBroadcast.Core/Link` on verkoton: `TournamentSnapshot` (yksi sisäinen malli molemmille lähteille), `SnapshotParser`, `LiveBuilder` (SOWDraftin `liveState.js` C#:lla), `MatchLinkMapper`, `TournamentMapper`, `LinkNaming`. Sovelluspuolella `LinkViewModel` (+ `LinkPanel`/`LinkBar`) ja `ILinkHost`-rajapinta `MainWindowViewModel`ssä.
- **Kaksi reittiä, yksi malli**: `LinkService` kokeilee ensin `GET …/broadcast`; 404 → vanha julkinen `GET /api/tournament-system/:id` + `join-match`/`update-state` (draft-huoneen tila → `LiveBuilder`) + globaali `tournament-update` (suodatus UUID/slug). **sowdraft.fi palauttaa tällä hetkellä 404 `/broadcast`ille (PR #22 on mergattu mainiin mutta ei julkaistu)**, joten käytännössä ajetaan vanhaa reittiä kunnes SOWDraft päivitetään; sovellus siirtyy uuteen reittiin itse. Uusi reitti: `join-tournament`, `tournament-event` (vain-live-tapahtuma jonka `version` jatkaa edellistä sovelletaan ilman hakua, muuten haku; muun ottelun live ohitetaan). Eheys: koko haku jokaisen (uudelleen)yhteyden jälkeen, 30 s välein ja versiohypyssä; yksi uusinta 4 s päästä virheestä; tapahtumapurskeet yhdistetään yhdeksi hauksi.
- **Ottelu valitaan aina käsin (KYS-13)**: linkityksen jälkeen `No match selected` eikä mitään täytetä; valinta tallentuu `AppState.Link`iin ja palautuu käynnistyksessä (EHD-5: `RestoreLinkAsync`). Ottelulista: käynnissä olevat draft-ottelut (●) ensin, sitten tulevat, valmiit viimeisinä; byet pois.
- **Täyttösäännöt** (`MatchLinkMapper`): nimi, lyhenne (puuttuessa ehdotus nimen 3 ensimmäisestä kirjaimesta), väri (vain jos SOWDraftissa), logo (ladataan `Scoreboard/TeamLogos/` – yksi kansio sekä joukkueille että standings/bracketille, ei erillistä `TournamentLogos`), pelaajat (battletag ennen `#`, max 12 merkkiä; rooli pää + toissijaiset, Flex jos kattavat kaikki kolme), ottelun pisteet, mapit/pickit/bannit/tulokset kortteihin 1..N kronologisessa järjestyksessä (tasapeli omana korttinaan, Push 1–0/0–1, automaattinen pick → TBA, bannit **jokaiselle** mapille). **Kenttä kirjoitetaan vain jos SOWDraftin arvo on muuttunut viimeksi vastaanotetusta** (`Link.Received` tallentuu mukana), joten käsin tehdyt korjaukset säilyvät; tyhjä arvo ei pyyhi käsin kirjoitettua ennen kuin sille on tullut ei-tyhjä arvo; pelaajarivin vaihtuessa toiseksi pelaajaksi heron valinta nollataan. Tuntematon map jää nimellään näkyviin ja tilariville tulee varoitus; sarja jossa on enemmän mappeja kuin formaattipalkissa varoittaa (`maps_count`ia ei koskaan muuteta). Ottelun vaihto, Swap sides ja Reset kirjoittavat kaiken uudelleen.
- **Sivut**: `Swap sides` -kytkin (`Link.SidesSwapped`, tallentuu); **Swap Teams -nappi linkitettynä = sama kytkin** (muuten linkitetyt arvot menisivät väärälle joukkueelle). Reset linkitettynä täytetään heti uudelleen.
- **Lähetys (KYS-11a)**: linkistä tuleva data kirjoittuu ja lähtee overlayihin heti täydellä Updatella (vanhan tapaan); `Unlink` nollaa mapkortit ja lähettää, mutta jättää joukkueet sekä standings/bracket-datan paikoilleen. Standings- ja Bracket-välilehdet täytetään turnauksesta (`TournamentMapper`: vanhan kuvaus, mutta pisteet **vaiheen omilla** voitto-/tasapeli-/tappiopisteillä, ei kovakoodattuja 3/0) ja lukitaan (vain luku) kunnes Unlink; Match-välilehti ei lukitu.
- **Varakeino**: *Advanced → link a draft room manually* (`…/stream?matchId=…`, ei turnaustietoa; joukkueet draft-huoneen nimistä, sivut käsin, standings/bracket-välilehdet eivät lukitu).
- **UI-tekstit** englanniksi luvun 8.0 mukaan; tilarivi: `Not linked` / `Connecting…` / `Linked – <turnaus> – <T1> vs <T2>` / `No match selected` / `Waiting for draft to start…` / `Match complete` / `Error: …` + `last update hh:mm:ss`. `User-Agent: SOWBroadcast/<versio> (+https://suomiow.fi)`; tiimitokeneita ei käytetä (vain julkinen `stream`-rooli).
- **Varmistus oikeaa vastaan**: paikallinen SOWDraft (sowdraft-repon oma testiharness, väliaikainen tietokanta) ajettiin sovelluksen `LinkService`llä: `/broadcast`, `join-tournament`, `tournament-event` live-päivitykset (map, banni, tulos, pisteet) toimivat; vanha reitti ajettiin sowdraft.fi:tä vastaan (vain luku): turnaus, 27 ottelua, standings, bracket-kierrokset ja pelaajaroolit tulkittiin oikein, myös päättyneen ottelun draft-huone oli vielä liitettävissä.
- **Ei vielä mukana**: kenttäkohtainen "linkitetty"-merkki käyttöliittymässä (linkitetty data näkyy vain tilarivillä ja täyttyneinä kenttinä); SVG-logo ei muunnu overlayn `T{n}Logo.png`:ksi (Skia ei pura SVG:tä). SOWDraftin `S5`-sallintasääntö on edelleen käyttäjän tehtävä (valinnainen).
- **Dev-apu**: `--link <turnausosoite> [--select-live]` `--screenshot`-kytkimen kanssa.

**M7 valmis (2026-10-16)** – replay-vahtija, loki, About ja asennin (351 testiä yhteensä). Havainnot ja päätökset:
- **Replay-vahtija** (`Core/Replay/ReplayWatcher`): seuraa `Scoreboard/Replay/Replay Replay.mp4` (`FileSystemWatcher` herättää 0,5 s tarkistukset, joita jatketaan kunnes leike on otettu). Leike kopioidaan soittolistaan (`<n>-Replay-Replay.mp4`, n = suurin numero + 1) vasta kun `(mtime, koko)` on ollut sama 3 tarkistusta peräkkäin, ei koskaan kahdesti, ja käynnistyksessä jo olemassa oleva leike merkitään nähdyksi (vanhan bugi #14: kopioi joka käynnistyksessä). OBS:n vielä kirjoittama (lukittu) tiedosto yritetään myöhemmin uudelleen. `replaypath.txt` = vain tiedostonimi; `replay`-avain lähtee overlayille heti (`IOverlayPublisher.AnnounceReplay`). Soittolistasta pidetään uusimmat 25 leikettä (vanha ei siivonnut koskaan); käytössä olevaa ei poisteta.
- **Loki**: Serilog, `Logs/sowbroadcast-YYYYMMDD.log` datakansiossa (päiväkohtainen, 14 päivää). Lokiin: käynnistys (versio, polut), overlay-palvelin, linkin tilat, tallennus-/kirjoitusvirheet, replay-leikkeet; ei pelaajanimiä tai muuta ottelutietoa. Käsittelemättömät poikkeukset lokitetaan (`AppDomain`, `TaskScheduler`) ja UI-säikeen virhe lokitetaan ja ohjelma jatkaa (lähetyksen ei pidä pysähtyä); Help → Open log folder / Open data folder.
- **Versio**: yksi lähde, `<Version>` projektitiedostossa → `AppInfo.Version` (tilarivi, About, User-Agent, loki, asennin). **About** (Help-valikko): Päälogo gradientilla, versio, datakansio ja lokikansio.
- **Asennin** (M7:ssä Velopack, korvattu Inno Setupilla – ks. "Asennin: Inno Setup" M9:n jälkeen): `tools/build-installer.ps1` julkaisee itsenäisen win-x64-version, lisää viereen `HTML/` ja toimitettavat kuvakansiot (`Heroes`, `HeroesBody`, `Maps`, `Gametypes`, `Roles`, `General/Icons`; vain `index.json`-luettelot) ja pakkaa asentimen. Version nosto: `<Version>` → ajo.
- **Havainto**: Release-käännös oli rikki M1:stä asti (Debug-valikon `LoadSampleCommand` ja `OnOpenScoreboardOverlay` olivat `#if DEBUG` -lohkoissa mutta XAML viittasi niihin) – korjattu; asentimen skripti pysähtyy nyt virheeseen. Julkaistu ohjelma ajettiin ja sen overlay-palvelin tarjoaa `HTML/`- ja `Scoreboard/`-sisällön ohjelman vierestä.
- **Ei vielä**: päivitystarkistus GitHub Releasesista (EHD-15; toteutettu myöhemmin, ks. Inno Setup -kappale), asentimen koon pienentäminen (EHD-16: Maps 117 Mt ja HeroesBody 41 Mt PNG-kuvia; ks. "Asennin pienemmäksi") sekä oikea asennus puhtaalle Windows 11 -koneelle OBS:n kanssa.

**M8 valmis (2026-10-16)** – overlayjen datakerros uuteen kontraktiin ja OBS-scenet (370 testiä yhteensä). Havainnot ja päätökset:
- **Kontrakti** (luku 7): `GET /api/state` = `{schema_version, version, server_time, match, matchtext, videos[], timer{seconds,running,ends_at}, standings, bracket}`; `match` on vanhan `match.json`in muoto ilman standings/bracketia ja ilman logopolkuja. SSE-viesti on `{"changed":[…],"version":n}` (`version` = tapahtuman id). Tila asetetaan ennen kuin tapahtuma julkaistaan, joten overlay joka hakee tilan vihjeestä saa aina vähintään sen version. Viejä tuottaa sovelluksessa vain sen mitä OBS tarvitsee tiedostona (`legacyFiles: false`: logo-PNG:t, `caster1|2`/`host.txt` OBS:n tekstilähteille, odotusruudun soittolistan videot). Golden-testit ajavat edelleen vanhan kontraktin (`legacyFiles: true`, oletus), joten oraakkelivertailu säilyy.
- **Ajastin (EHD-9)**: `WaitingSettings.TimerEndsAtMs` + `timer.ends_at`; overlay laskee itse (kellosiirtymä `server_time`sta), sekuntitikkejä ei julkaista. Loppuminen ilmoitetaan kerran (`waiting.timer`).
- **Overlayjen datakerros**: `HTML/sow-data.js` (`window.SOW`) lataa `/api/state`in, pitää sen tuoreena omalla EventSourcellaan ja vastaa overlayjen vanhoihin `read/readJSON/ok`-kutsuihin tiloista (`virtualFile()`); kuvat, fontit, indeksit ja videot haetaan edelleen tiedostoina. `SOW.events()` kutsuu sivun `onmessage`a vasta kun tila on päivittynyt. Valinta: ulkoasu- ja animaatiokoodiin ei kosketa (KYS-4), vain datan lähde vaihtuu; jaetun moduulin uudelleenkirjoitus natiiviksi voi tulla myöhemmin. `tools/overlay-data.test.mjs` varmistaa että 84 vanhaa datatiedostoa kummastakin golden-tilasta tulee `SOW.read/readJSON`ista täsmälleen samana (xUnit ajaa sen kun Node on asennettu).
- **Yhdistetyt overlayt (EHD-11)**: `waiting.html?mode=start|brb|end` (korvaa startingsoon/berightback/thankyouforwatching; loppunäytöllä ei ajastinta ja logo on tekstin alla), `team.html?side=1|2` (korvaa team1/team2), `MapDraft.html` säilyy ja saa valinnaisen video-taustan (`Maps/<slug>.mp4` jos on, muuten PNG – KYS-14). Poistettu: `draft.html`, `MapDraft_v2.html`, `SOWBROADCAST.json` (draft-scenet). Rajdhani on paikallinen kaikissa (Google Fonts -linkit poistettu).
- **Visuaalinen regressio**: `tools/overlay-shots.mjs` (headless Edge CDP, 1920×1080) + `tools/overlay-compare.py` (ennen/jälkeen). Samalla esimerkkitilalla (`--serve-sample <s>`, Debug) 14 overlayta on pikselintarkasti identtisiä lukuun ottamatta rullaavaa tikkeriä (odotusruudut, 0,1–0,3 % pikseleistä, vain tikkerin alue) ja `maps`in yhtä keskeneräistä animaatiokehystä.
- **OBS-scenet**: (M8:ssa Python-työkalu, **M9c:ssä siirretty ohjelmaan**: Control → OBS panel → *Export scene collection…*, `Core/Obs/ObsSceneCollection`, pohja upotettuna `Core/Obs/scene-template.json`) rakentaa vanhasta kokoelmasta uuden "SOW Broadcast" -kokoelman: 18 scenea, uudet osoitteet (`waiting.html?mode=…`, `team.html?side=…`, `MapDraft.html`), portti/asennus-/datakansio parametreina (`--base-url`, `--install-dir`, `--data-dir`), NDI-lähettäjien nimet tyhjennetty, web-kamera ja `modules`-asetukset pois, kuolleet kohteet (puuttuvat lähteet) pois, `localhost:899`-osoite korjattu, uudet vakaat tunnisteet, 1089 px korkea Team 2 -lähde 1080:ksi. Skripti validoi itsensä (kaikki sivut olemassa, ei `C:/SOWBroadcast`ia tai laitetunnisteita); xUnit-testi ajaa `--check-only`n kun Python on asennettu. Käyttäjä valitsee NDI-lähettäjät ja kameran OBS:ssä tuonnin jälkeen.
- **Ei vielä**: vanhan Python-sovelluksen ja sen tiedostokontraktin poisto repositoriosta (`SOWBroadcast.py`, `server.py` ym. pidetään golden-oraakkelina kunnes M9 on valmis), scenegeneraattorin liittäminen sovellukseen (EHD-6: OBS WebSocket), koko paketin kokeilu OBS:ssä (Browser Source -latausta ei testattu OBS:llä, vain Edge-selaimella), `replay.html` lukee edelleen oikeita tiedostoja (soittolista + `replaypath.txt`, joita sovellus kirjoittaa).
**M9a / EHD-7 valmis (2026-10-16)** – pikanäppäimet ja paikallinen ohjaus. Havainnot ja päätökset:
- **Komentoluettelo** (`Core/Control/ControlCatalog`): `team1|team2.score.up|down`, `map.team1|team2.up|down` (nykyisen mapin pisteet), `map.next` (nykyinen map valmiiksi; nykyinen map = ensimmäinen näkyvä keskeneräinen kortti), `swap`, `timer.toggle|start|pause|reset|plus1m|minus1m`, `update`. Kaikki kulkevat yhden reitin `MainWindowViewModel.RunControl(id)` kautta UI-säikeessä, joten pikanäppäin ja HTTP tekevät aina saman. **KYS-9 pätee**: pisteet/swap/map muuttavat vain ruutua + autosavea ja nostavat "Unsent changes" -merkin; overlayhin ne lähtevät vasta `update`-komennolla (tai HTTP:n `&publish=1`, joka on Update heti komennon perään). Ajastinkomennot lähtevät heti kuten napit (KYS-11b).
- **Paikallinen HTTP** (`OverlayServer`, `ControlGateway`): `GET|POST /api/control/<komento>?token=…` (token myös `X-SOWB-Token`-otsakkeena), `GET /api/control` listaa komennot ja tilan. Vastaus JSON `{ok, command, message, team1{score}, team2{score}, current_map, timer{text,running}, unsent}`; 200 tehty, **409 ei voinut tehdä** (pistemäärä jo 0, ei avointa mapia – Stream Deckin näppäin näyttää virheen), 404 tuntematon komento, 401 väärä/puuttuva token, 503 kunnes ikkuna on valmis. GET sallitaan, koska Stream Deck/Companion on helpoin säätää pelkällä URL:lla; token pitää muut sivut ulkona. Token on **pysyvä** (`control.json` datakansiossa, ei ottelutiedostossa) ja vaihdettavissa ikkunassa (voimaan heti).
- **Pikanäppäimet** (`App/Services/GlobalHotkeys`): Windowsin `RegisterHotKey` omalla säikeellä (+`MOD_NOREPEAT`), toimii myös kun OBS tai peli on aktiivinen; ikkunassa on lisäksi oma näppäinkäsittelijä, joten Windowsin hylkäämä yhdistelmä (toinen ohjelma käyttää) toimii ainakin ikkunan ollessa aktiivinen ja tilariville tulee varoitus. Oletukset Ctrl+Alt+F1…F9 (pisteet ±, seuraava map, swap, ajastin start/pause, reset, Update); muut komennot ilman oletusta. Asetukset: Control → *Hotkeys and remote control…* (näppäinyhdistelmä napataan painamalla, päällekkäisyydet ja turvattomat globaalit yhdistelmät – pelkkä kirjain – estetään). Kytkin "Work in every program" pois → vain ikkunassa.
- **Varmistus**: yksikkötestit (gesti-jäsennys, asetukset, komennot, HTTP-isäntä, päätepiste, token) sekä livenä: ajettu ohjelma vastasi curlille (200/409/404/401) ja simuloitu Ctrl+Alt+F3/F4-painallus (`keybd_event`) muutti pisteitä ohjelman ollessa taustalla.
- **Ei vielä**: WebSocket-ohjaus (EHD-7:n maininta; HTTP riittää Stream Deckille ja Companionille – lisätään jos tarvitaan), Companion-moduulin/Stream Deck -profiilin esimerkkitiedostot, komentojen laajennus (esim. pelaajat, tekstit) tarpeen mukaan.
**M9a / EHD-4 valmis (2026-10-16)** – automaatiot. Havainnot ja päätökset:
- **Kolme kytkintä, kaikki oletuksena pois** (asetukset: Control → *Hotkeys, remote control and automation…*, tallennus `control.json`, kone-/operaattorikohtainen, ei ottelutiedostossa): (1) *Team score = maps won* – joukkueen pisteet = voitetut **valmiit** mapit (näkyvät kortit; tasapeli ei kenellekään); (2) *Finish a map when a team has the points that win it* – map merkitään valmiiksi kun yksi joukkue saavuttaa pelitilan voittopisteet (Control 2, Flashpoint 3, Push 1; Escort ja Hybrid 0 = ei päätetä pisteillä, koska ne riippuvat molemmista hyökkäyksistä; luvut muokattavissa), jolloin seuraava map on automaattisesti nykyinen (`current_map` johdetaan ensimmäisestä keskeneräisestä – erillistä "seuraava map" -kytkintä ei tarvita; käsin voi päättää `map.next`-komennolla); (3) *Bracket: move the winner to the next round*. Pelitila luetaan assetin `Mode`-kentästä, joten tuntematon/tyhjä map ei valmistu itsestään. Voitto lasketaan vain kun yksi joukkue on pisteissä edellä; "valmis"-ruksin poisto ottaa voiton pois pisteistä.
- **Säännöt** ovat puhtaita funktioita Coressa (`Core/Automation/MapAutomation`, `Core/Bracket/BracketAdvance`) ja VM kutsuu niitä vain käyttäjän muokkauksista (lataus, Reset, Swap ja linkki eivät laukaise). Automaatio ei vie overlayihin mitään (KYS-9): muutos näkyy ruudulla ja autosavessa + "Unsent changes", overlay saa sen Updatella.
- **Bracket**: voittoon tarvittavat erävoitot BO-labelista (`BO5` → 3), muuten oletus-best-of (asetus, oletus 3 → 2 voittoa) – käsin tehdyissä kaavioissa BO-labelia ei ole. Single elimination täysi: voittaja seuraavan kierroksen paikkaan (parillinen/pariton → slotti 1/2; 6 joukkueen kierros 1 → ensimmäinen vapaa paikka, jotta käsin asetetut sijoitetut joukkueet säilyvät), semifinaalien häviäjät pronssiotteluun. Double elimination: voittajat etenevät omalla puolellaan ja Upper/Lower Final voittajat Grand Finaliin (1/2); **häviäjien putoaminen upper → lower jätetään käsin** (mallipohjan Lower-kierrokset eivät kerro mistä häviäjät tulevat). **Ei koskaan ylikirjoita käsin asetettua joukkuetta**; korjattu tulos vaihtaa vain automaattisesti viedyn (häviäjän) tilalle voittajan; voittajan logo ja lyhenne säilyvät.
- **Linkki**: automaatiot väistävät kun SOWDraft täyttää (Match: `Link.IsLinked`, Bracket: `Link.IsTournamentLinked`), koska linkitetty data on totuus.
- **Ei vielä**: Match-välilehden oma näkyvä merkintä "automaation asettama" (kenttä muuttuu hiljaa; tilarivi ei kerro), ajastimen automaatiot, EHD-4:n "seuraava map" -erillisautomaatio (katso yllä, johdettu).
**M9a / EHD-17 valmis (2026-10-16)** – undo/redo ja edellisen ottelun palautus. Havainnot ja päätökset:
- **Undo/redo** (`Core/History/UndoHistory`, `MainWindowViewModel.History`): askel = tilan tekstimuotoinen kuva (match, general, waiting-tekstit, standings, bracket); 100 askelta. Muokkaukset yhdistyvät yhdeksi askeleeksi kun kirjoittaminen tauolla (700 ms ajastin ikkunassa), ja **jokainen toiminto (Swap, Reset, tuonti, lataus, pikanäppäinkomento) on aina oma askeleensa** (tilanne tallennetaan ennen toimintoa). Edit-valikko (Ctrl+Z / Ctrl+Y; tekstikentän sisällä Ctrl+Z on kentän oma) ja pikakomennot `undo` / `redo` (oletus Ctrl+Alt+Z / Ctrl+Alt+Y, myös HTTP). **Ei kosketa**: ajastinta (käynnissä oleva lasku jatkuu, tikit eivät luo askelia), SOWDraft-linkkiä ja linkin osoitteita, overlayjä – undo vaihtaa vain ruudun tilan, joten se on kuten muukin muokkaus ("Unsent changes"; overlay saa sen Updatella, KYS-9). Linkin täyttämä data kirjautuu tavallisena askeleena (undo palauttaa ruudun ennen linkitystä; SOWDraftin seuraava muutos täyttää sen taas). Autosave-palautus asettaa historian alun.
- **Edellisen ottelun palautus** (`Core/History/MatchArchive`): File → *Restore previous match…* listaa arkistoidut ottelut (`History/match-<aika>.json` datakansiossa, uusin ensin, 30 kpl). Ottelu arkistoidaan automaattisesti ennen **Resetiä**, **File→Loadia**, **palautusta**, **linkitetyn ottelun vaihtoa**, **ohjelman sulkemista** ja **Updatea** (korkeintaan kerran 10 min välein); tyhjää ottelua tai täsmälleen samaa tilaa kuin uusin arkistoitu ei arkistoida (ajastin ja linkin kirjanpito eivät kuulu vertailuun). Palautus tuo ottelun ruudulle kuten Load (ei lähetä), mutta **säilyttää käynnissä olevan ajastimen ja linkin**; nykyinen ottelu arkistoidaan ensin ja palautus on itse undo-askel. Tiedostonimet kasvavat aina (sama sekunti → `-02`), joten karsinta ei koskaan poista uusinta.
- **Varmistus**: yksikkötestit (historia, arkisto, VM) ja livenä: ajettu ohjelma, undo/redo HTTP-komennoilla (409 kun ei ole mitä perua), swap erillisenä askeleena, sulkeminen arkistoi tilan; palautusikkuna renderöity.
- **Ei vielä**: undo-historian säilytys uudelleenkäynnistyksen yli (arkisto kattaa tämän karkeasti), yksittäisen kentän vanha arvo -korostus, avattu pääikkunan Edit-valikon visuaalinen tarkistus (valikon `HotKey` toimii Avaloniassa mutta ei kokeiltu näppäimellä).
**M9b / EHD-10 valmis (2026-10-16)** – asset-manifesti. Havainnot ja päätökset:
- **Manifesti** (`Scoreboard/manifest.json`, `Core/Assets/AssetManifest`): yksi tiedosto jossa jokaisen sankarin, mapin ja pelitilan nimi, slug, rooli / pelitila, kuvatiedostot (`image`, sankareilla `body_image`) ja **aliakset**; kentät `schema`, `revision` (suurin voittaa), `updated`, valinnaiset `image_url` / `body_image_url` (vain https). Toimitettu manifesti on tehty nykyisistä `index.json`-tiedostoista (`tools/make-manifest.py [--bump]`: aliakset säännöillä – aksentit pois, välimerkit pois / välilyönniksi, kaareva heittomerkki – ja `EXTRA`-taulukolla, esim. Hammond, McCree, Torbjörn). 54 sankaria, 31 mappia, 5 pelitilaa, jokaisella sankarilla rooli ja vartalokuva (testattu).
- **Poistuneet kovakoodaukset**: `Slug.HeroBodyOverrides` (vartalokuvan poikkeustaulu) ja overlayjen JS-kopiot (`heroBodyOverrides`/`heroBodyFile` MapDraftissa ja team.html:ssä) – vartalokuvan tiedosto luetaan nyt manifestista (`Heroes/index.json` → `body_image`, `SOW.heroBodyPath(nimi)` löytää nimellä tai aliaksella); vain manifestin ulkopuolinen uusi sankari saa sääntönimen (`Slug.HeroBodyFile`: sanat Title Case + `_`, aksentit ja välimerkit pois: "Soldier: 76" → `Soldier_76`). Roolitaulu oli jo indeksissä (M2). **Sumea nimihaku poistettu**: `LinkNaming.ResolveName` ei enää arvaa välimerkkejä; SOWDraftin map- ja **bannattujen sankarien** nimet haetaan `AssetService.Resolve` -kutsulla (nimi tai alias, vain kirjainkoko ja ylimääräiset välit sallitaan eroksi). Tuntematon nimi → varoitus tilariville, nimi säilyy (ei hiljaista ohitusta).
- **AssetService**: käynnistyksessä käytetään korkeimman `revision`in manifestia (käyttäjän kansion haettu vs. asennuksen mukana tullut); tunnetut asiat saavat manifestista roolin, pelitilan, kuvatiedostot ja aliakset (**manifesti on auktoriteetti näille kentille** – käsin Asset Managerissa muutettu rooli palautuu manifestin arvoon seuraavalla käynnistyksellä), puuttuvat sankarit/mapit **lisätään itsestään**, käyttäjän omat lisäykset säilyvät ja **käyttäjän poistamaa ei tuoda takaisin** (`assets.local.json`, uudelleenlisäys kumoaa poiston). Muuttuneet kategoriat kirjoitetaan käyttäjän `index.json`-tiedostoihin, jotka overlayt lukevat.
- **Päivitys GitHub Releasesista** (`Integrations/Assets/ManifestUpdater`, valikko Customize → *Update hero and map list…*): vain käyttäjän pyynnöstä (ohjelma ei tee itsenäisesti verkkokutsuja). Hakee `AlexNB01/SOWBroadcast`in viimeisimmän julkaisun `manifest.json`-liitteen (osoite vain github.com / githubusercontent.com, ≤1 Mt, validointi `AssetManifest.Parse`), ohittaa jos revision ei ole uudempi, lataa puuttuvat kuvat (https, ≤5 Mt, PNG/JPEG/WebP-tunniste tarkistetaan, polku ei saa karata Scoreboard-kansiosta) ennen kuin uusi lista kirjoitetaan, ja kertoo mitä lisättiin. **Julkaisu**: aja `tools/make-manifest.py --bump`, liitä `Scoreboard/manifest.json` GitHub-julkaisun liitteeksi (uusille hahmoille kuvat joko asennuspakettiin tai `image_url`/`body_image_url`-osoitteisiin). Asennin kopioi manifestin mukaan (`build-installer.ps1`).
- **Varmistus**: 21 uutta testiä (jäsennys, toimitettu manifesti vs. katalogi, yhdistäminen, poistot, asennus, päivittäjä feikatulla HTTP:llä, overlayn hakufunktio Nodella); MapDraft ja team-overlayt pikselintarkasti ennallaan (sankarikuvat näkyvät).
- **Tunnetut jäänteet**: `Heroes/d-va.png` puuttuu edelleen (manifesti viittaa siihen; vanha dataongelma). Päivittäjää ei ole ajettu oikeaa GitHubia vastaan (julkaisussa ei ole vielä manifestia).
**M9b / EHD-8 valmis (2026-10-16)** – joukkuekirjasto. Havainnot ja päätökset:
- **Kirjasto** (`Core/Teams/TeamLibrary`, datakansion `Scoreboard/TeamLibrary/`): `library.json` + yksi PNG per joukkue kansiossa `logos/<id>.png`. Tallennettu: nimi, lyhenne, väri, logo, 8 pelaajaa (nimi, hero, rooli), lähde (`manual` / `file` / `sowdraft`), aika; **ei pisteitä eikä bannia**. Teams-valikko: *Team library…* (ikkuna), *Keep Home / Away in the library*, sekä alivalikko *Team files* (vanhat `*.sowteam.json`-vienti/-tuonti säilyvät jaettavaksi koneiden välillä; tiedosto voidaan myös tuoda suoraan kirjastoon).
- **Logot kuuluvat kirjastolle**: logo kopioidaan kirjaston omaksi PNG:ksi (ei absoluuttisia polkuja kirjastotiedostossa) ja korvataan/poistetaan joukkueen mukana, joten vanhentunutta PNG:tä ei jää; jos paneelin logoa ei voi lukea, kirjaston vanha säilyy ja tulee varoitus; paneelin ollessa jo kirjaston oma logo ei kopioida mitään.
- **Haku** (`Search`): joukkueen nimi, lyhenne tai pelaajan nimi sisältää kaikki hakusanat (kirjainkoolla ei väliä). Ikkunassa kaksoisklikkaus lataa Homeksi; *Load as Home/Away* säilyttää kohdepaneelin pisteet (kuten vanha import), laittaa kirjaston logon paneeliin ja on **oma undo-askel**; ei lähetä overlayihin (KYS-9). Sama nimi = sama joukkue: tallennus päivittää (nimen muutos luo uuden).
- **SOWDraft-joukkueiden tuonti**: *Import SOWDraft teams* (aktiivinen kun turnaus on linkitetty) tuo turnauksen kaikki joukkueet: lyhenne SOWDraftista tai ehdotus (3 kirjainta), väri, logo (ladataan `LogoCache`en), pelaajat (battletag ennen `#`, rooli kuten linkissä, Flex kun roolit kattavat kolme). SOWDraftin arvo korvaa saman nimisen merkinnän, paitsi lyhenne/väri jota SOWDraftilla ei ole säilyy.
- **Varmistus**: 17 uutta testiä (tallennus/päivitys/poisto, logon korvaus ja siivous, haku, vanha tiedosto, rikkinäinen kirjasto → `.bad`, SOWDraft-tuonti, VM ja ikkuna); ikkuna renderöity.
- **Ei vielä**: logon vaihto suoraan paneelin napista kirjastosta, kirjaston joukkueiden muokkaus ikkunassa (muokkaus tehdään paneelissa ja tallennetaan uudelleen), kirjaston yhdistäminen koneiden välillä (kansio on kopioitavissa sellaisenaan).
**M9b / EHD-18 valmis (2026-10-16)** – otteluprofiilit. Havainnot ja päätökset:
- **Profiili** (`Core/Profiles/ProfileStore`, datakansion `Profiles/<id>/profile.json` + `logos/`): nimetty koko lähetysasetelman kuva yhdelle turnaukselle tai ottelulle – General (status, värit, logot, casterit, formaatti), odotusruudun tekstit/somet/tickeri/videokansio, standings, bracket, **turnauksen osoite** ja ottelu (joukkueet, mapit, pisteet). **Ei kuulu**: ajastin ja käynnissä oleva SOWDraft-yhteys (vain osoite). Logot (joukkueet, overlay, transition) kopioidaan profiilin omaan kansioon, joten profiili ei riipu tiedostoista jotka voivat siirtyä; uudelleentallennus siivoaa vanhat logot. Sama nimi (kirjainkoolla ei väliä) = sama profiili, tallennus korvaa. Viimeksi käytetty/tallennettu on "aktiivinen" (`index.json`) ja näkyy otsikon pillissä *Profile: <nimi>*.
- **Vaihto** (File → *Profiles…*, `ProfilesViewModel`): *Apply settings only* (oletus, kaksoisklikkaus) vaihtaa vain tapahtuman asetukset ja jättää ruudulla olevat joukkueet, mapit ja pisteet; *Apply whole match* tuo myös tallennetun ottelun ja **arkistoi nykyisen ensin** (Restore previous match). Kumpikin on **undo-askel**, ei lähetä overlayihin (Update, KYS-9), ja jättää käynnissä olevan ajastimen sekä linkin rauhaan; turnauksen osoite täytetään linkkikenttään kun linkkiä ei ole (yhdistäminen jää käyttäjälle), linkitettynä jätetään ennalleen ja tilariville tulee huomautus.
- **Rajaus**: tallennettavaa ottelua varten profiilit ja arkisto (EHD-17) kattavat yhdessä "useita otteluita": profiili = nimetty ottelu/turnaus, arkisto = automaattinen historia. Profiilin muokkaus tehdään ruudulla ja tallennetaan samalla nimellä; uudelleennimeäminen = tallenna uudella nimellä + poista vanha.
- **Varmistus**: 12 uutta testiä (säilytys, logojen omistus ja siivous, korvaus, poisto, rikkinäinen profiili, VM-vaihto, undo, ajastin, osoite, ikkuna); ikkuna renderöity.
**M9c / EHD-6 valmis (2026-10-16)** – OBS WebSocket. Havainnot ja päätökset:
- **Asiakas** (`Integrations/Obs/ObsClient`): OBS WebSocket v5 (OBS 28+ sisäänrakennettu palvelin) ilman lisäkirjastoja (`ClientWebSocket`): Hello → salasanahaaste (`base64(sha256(base64(sha256(salasana+salt))+challenge))`) → Identify (tilaukset Scenes + Outputs) → pyynnöt/vastaukset requestId:llä (5 s aikakatkaisu, virheet `ObsException` statuskoodilla) ja tapahtumat; yhteyden katkeaminen kaataa odottavat pyynnöt ja ilmoitetaan. Vastaukset sanoin: OBS ei vastaa → "Is OBS running with the WebSocket server switched on…", väärä salasana → "OBS refused the password", salasana puuttuu → "OBS asks for a password".
- **Ohjain** (`ObsController`): seuraa sceneä (lista OBS:n järjestyksessä, nykyinen scene, tapahtumat `CurrentProgramSceneChanged`/`SceneListChanged`), replay bufferia, striimiä ja tallennusta; toiminnot: scenen vaihto, replay bufferin käynnistys/pysäytys, **Save replay** (vain kun buffer käy; muuten selkeä virhe) ja `ReplaySaved`-tapahtuma. Clip kopioituu soittolistaan edelleen replay-vahtijan kautta (`Scoreboard/Replay`).
- **Go-live-tarkistuslista**: yhteys, overlay-palvelin käynnissä, kaikki 18 scenea löytyvät OBS:stä, **kaikki overlay-browser sourcet osoittavat tähän palvelimeen (osoite+portti) ja olemassa oleviin sivuihin** (vanha `startingsoon.html` jne. → ongelma), replay buffer päällä/käynnissä, OBS:n tallennuskansio = kansio jota vahti seuraa (varoitus jos ei), striimi. Tulos ✔ / ! / ✘ riveinä.
- **Paneeli** (Control → *OBS panel…*, ei-modaalinen ikkuna): yhteys (host, portti, salasana, *Connect automatically when the program starts*), scenepainikkeet (nykyinen korostettu), replay-napit, tarkistus, scenekokoelman vienti. Asetukset `obs.json` datakansiossa; **salasana tallennetaan Windows DPAPI:lla (käyttäjäkohtainen), ei koskaan selkotekstinä**, eikä näytetä uudelleen (kentässä paikkamerkki). Automaattinen yhteys oletuksena pois; epäonnistunut tausta-yhteys ei häiritse.
- **Komennot** (hotkeyt + HTTP, samat kuin EHD-7): `obs.replay.save` (oletus **Ctrl+Alt+R**), `obs.replay.toggle`, `obs.scene` (HTTP: `?name=Scene name`; ei pikanäppäintä). OBS-komennot ovat asynkronisia (`RunControlAsync`) ja HTTP-vastaus odottaa OBS:n vastausta (200/409). Muut komennot kulkevat saman oven kautta.
- **Scenekokoelma ohjelmassa**: kokoelma generoidaan ohjelman omista osoitteista/kansioista, ilman laitekohtaisia arvoja; Python-työkalu ja sen testi poistettu (yksi totuus). C#-versio tuottaa semanttisesti saman JSONin kuin Python-versio (verrattu).
- **Varmistus**: 37 uutta testiä (protokolla feikatulla OBS:llä: sisäänkirjautuminen, haaste, virheet, aikakatkaisu, tapahtumat, yhteyden katkeaminen; ohjain, tarkistuslista, asetukset, VM, komennot, kokoelma) sekä oikea WebSocket-kuljetus paikallista OBS-jäljitelmää vastaan. **Ei kokeiltu oikealla OBS:llä** (tässä koneessa ei OBS:ää ajettu): protokollan nimet ja kentät on kirjoitettu OBS WebSocket 5.x -dokumentaation mukaan, joten ensimmäinen oikea yhteys on se varsinainen todennus.
- **Ei vielä**: scenekohtaiset pikanäppäimet (vain HTTP), OBS-asetusten korjaus napista (esim. tallennuskansion asetus), ReplayBufferSaved-tiedoston suora käyttö vahdin sijaan, lähteiden luonti WebSocketilla (kokoelma tuodaan OBS:n omalla tuonnilla).
**M9c / EHD-13 ja EHD-12 valmis (2026-10-16)** – overlay-esikatselu ja värivalitsimen viimeistely. Havainnot ja päätökset:
- **Esikatselu** (valikko *Preview*, `Core/Overlays/OverlayCatalog`): ei WebViewiä sovelluksessa – valinta avaa **oletusselaimeen** osoitteen `…/HTML/<sivu>?preview` (`BrowserOpener`). `?preview` (lisätty `HTML/sow-data.js`:ään) näyttää yhden overlayn shakkiruudukon päällä ja skaalattuna selainikkunaan (1920×1080-suhde säilyy, keskitetty); OBS ei koskaan lisää `?preview`ta, joten OBS:ssä mikään ei muutu. *All overlays at once* avaa `HTML/preview.html`:n: kaikki 15 overlayta kerralla livenä pieninä ruutuina (iframe 1920×1080 skaalattuna, ruudun koko säädettävissä, klikkaus avaa suuren `?preview`-näkymän). Luettelo on yhdessä paikassa (`OverlayCatalog`); testit varmistavat että jokainen sivu on olemassa, `preview.html` listaa täsmälleen samat sivut ja OBS-scenekokoelma käyttää vain luettelon sivuja. Kuvattu: yleisnäkymä ja yksittäinen `?preview` näyttävät oikein esimerkkitilalla.
- **Värivalitsin** (`Controls/ColorField`): palettipikavalinnat + vapaa heksa (EHD-12:n ensimmäinen versio M3:sta) sai **Recent**-rivin: viimeksi valitut 8 väriä uusin ensin, yksi lista kaikille värikentille (`Core/Colour/RecentColours`, `recent-colours.json` datakansiossa; vain käyttäjän valinta lisää, ei sidonnan asettama arvo; sama väri nousee kärkeen; rikkinäinen tiedosto → tyhjä lista).
- **Kontrastivaroitus poistettu (2026-10-17)**: joukkuevärin alla ollut huomautus (General-välilehden *Secondary*-väri vs. joukkueen väri) poistettiin omistajan pyynnöstä, koska se harhautti (`scoreboard.html` ei lue General-värejä). `ColourContrast` ja sen testit on poistettu.
- **Varmistus**: 32 uutta testiä; värivalitsin renderöity (Recent-rivi ja varoitus näkyvät).
- **Ei vielä**: HSV-liukusäädin/värikenttä vapaaseen värin valintaan (paletti + heksa kattaa nykyisen käytön), varoitus muista overlay-väreistä (tertiary jne.), esikatselun sulautus sovellukseen (päätös: selain).
**M9c / EHD-15 ja EHD-16 valmis (2026-10-16)** – perusinfra, päivitystarkistus ja asennin. Havainnot ja päätökset:
- **Päivitystarkistus** (`App/Services/UpdateService` + `InstallerUpdater`, ks. "Asennin: Inno Setup"): Help → *Check for updates…*; vain käyttäjän pyynnöstä. Tulokset sanoin: ei asennettu (kehityskansio/kopio → "Updates work only in the installed program"), ajan tasalla, uusi versio + koko, virhe (vain syyn ensimmäinen rivi, ei stack traceä). Uuden version asennus kysyy vahvistuksen ("the program closes and starts again… do not update during a broadcast"), tallentaa autosaven ja arkistoi ottelun ennen latausta (edistyminen tilarivillä). Valinnainen *Check for updates when the program starts* (`app-settings.json`, oletus **pois**): hiljainen tarkistus, vain rivi tilapalkkiin, ei dialogia. Testattu feikatulla HTTP:llä; **oikeaa GitHub-julkaisua vastaan ei ajettu** (julkaisuja ei vielä ole): ensimmäinen julkaisu (tagi `v3.0.0`) ja sitä seuraava testaavat ketjun.
- **Kaatumisraportti** (`Core/Diagnostics/CrashReports`): käsittelemätön päättävä virhe kirjoittaa `Logs/crash-<aika>.txt` (versio, järjestelmä, virhe; ei ottelutietoa; uusimmat 10 säilyvät) ja merkinnän, jonka seuraava käynnistys näyttää kerran ("The program closed unexpectedly last time… report saved: <polku>"). Aiempi lokitus, About ja versionäyttö (M7) ovat jo olemassa.
- **CI** (`.github/workflows/ci.yml`): Windows-ajo joka pushilla/PR:llä: Debug- ja Release-käännös + kaikki testit (Node asennettuna overlayjen datakerroksen testeille). **Julkaisu** (`release.yml`, tagi `v*`): tarkistaa että tagi = ohjelman versio, ajaa testit, asentaa Inno Setupin (choco) ja rakentaa asentimen (`tools/build-installer.ps1`, Python+Pillow kuvien pienennykseen), ja liittää julkaisuun asentimen, sen `.sha256`-tiedoston **ja `Scoreboard/manifest.json`in** (hero/map-listan päivitys lukee sen). **Työnkulkuja ei ole ajettu GitHubissa** – ne on kirjoitettu ja luettu, ei testattu.
- **Asennin pienemmäksi** (`tools/optimize-assets.py`, kutsutaan `build-installer.ps1`stä, `-NoOptimize` ohittaa): asennuspaketin *kopioista* poistetaan manifestissa listaamattomat kuvat (vanhat kirjoitusasut/duplikaatit, 11,7 Mt), yli 1500 px korkeat vartalokuvat pienennetään (1440p-skaalauksessakin riittää; 9 kuvaa) ja kaikki PNG:t pakataan uudelleen häviöttä. Repon omiin kuviin ei kosketa. **Setup.exe 228 → 182 Mt** (Maps 122 → 68 Mt, HeroesBody 43 → 37 Mt); tarkistettu: kaikki manifestin viittaamat kuvat löytyvät, 134 kuvaa pikselintarkasti identtisiä, 9 pienennettyä; julkaistu ohjelma ajettu ja se palvelee overlayt, kuvat ja `/api/state`n. Karttakuvien värien vähennys (256 väriä) pienentäisi paljon enemmän mutta näkyy raitoina, joten sitä ei tehdä; seuraava iso askel olisi karttakuvien siirto JPEG/WebP-muotoon (vaatii viejän ja polkujen muutoksen: nyt `.png` on kovakoodattu johdetuissa poluissa). Loput koosta (~70 Mt) on itsenäinen .NET-ajoympäristö + Avalonia/Skia.
- **Varmistus**: 13 uutta testiä (päivityspalvelu, kaatumisraportti, asetukset).
**M9 valmis (2026-10-16)** – kaikki hyväksytyt EHD-parannukset on toteutettu (588 testiä). **Todentamatta oikeassa ympäristössä** (koodi ja testit valmiit, mutta ulkoista järjestelmää vastaan ei ajettu): (1) OBS WebSocket oikealla OBS:llä (EHD-6), (2) päivitystarkistus ja manifestin haku oikeaa GitHub-julkaisua vastaan (EHD-10, EHD-15), (3) CI- ja release-työnkulut GitHubissa, (4) puhdas asennus toiselle Windows 11 -koneelle OBS:n kanssa (M7:stä), (5) SOWDraftin `/broadcast`-rajapinta (odottaa SOWDraftin julkaisua; vanha reitti toimii). **Tietoisesti rajattu pois**: WebSocket-ohjaus (HTTP riittää, EHD-7), double elimination -häviäjien putoaminen (EHD-4), karttakuvien JPEG/WebP (EHD-16), HSV-värinvalinta (EHD-12).
**Vanha Python-sovellus poistettu (2026-10-17)** – `SOWBroadcast.py`, `server.py`, `draft_link.py`, `tournament_link.py`, `launch.bat`, PyInstaller-specit, vanhat Inno-skriptit, `requirements.txt` ja vanhat ikonit on poistettu työpuusta; viimeinen tila on git-tagissa `legacy-python-app`. Golden-tiedostot ovat repossa, joten testit eivät tarvitse vanhaa sovellusta; `tools/golden/generate_legacy_golden.py` ajetaan tarvittaessa tagista otetussa worktreessä. README kirjoitettu uudelleen.
**Asennin: Inno Setup (2026-10-17, korvaa Velopackin)** – omistaja halusi ohjelman helposti löydettävään, itse valittavaan kansioon eikä %LOCALAPPDATA%:an (Velopack asentaa aina sinne). Havainnot ja päätökset:
- **Asennin** (`tools/installer/SOWBroadcast.iss`, ajetaan `tools/build-installer.ps1`:llä, tarvitsee Inno Setup 6:n): oletuskansio **`C:\SOW Broadcast`**, käyttäjä voi valita toisen; ei järjestelmänvalvojan oikeuksia (`PrivilegesRequired=lowest`, valintaikkuna tarjoaa "kaikille käyttäjille"). Uusi AppId (vanhan Python-asentimen oma on eri), työpöytäkuvake valinnaisena, Käynnistä-valikon kuvake ja valinnainen kuvake data-kansioon. **Musiikki (22 Mt) on valinnainen komponentti** (tyyppi "Program and music library" / "Program only"); `-NoMusic` jättää sen pois. Tulos: `artifacts/installer/SOWBroadcast-Setup-<versio>.exe` + `.sha256` (171 Mt).
- **Data** on ohjelman vieressä: `<ohjelmakansio>\Data` (autosave, generoidut overlay-tiedostot, lokit, asetukset, kirjastot). `AppPaths.ResolveDataRoot`: `SOWB_ROOT` voittaa; muuten `Data`-kansio ohjelman vieressä jos se on olemassa (asennin luo sen); muuten vanha `%LOCALAPPDATA%\SOWBroadcast` (kehityskopio, ei menetä dataa). **Edellisestä (Velopack-)asennuksesta ei siirretä dataa** – vain testattu, ei tuotannossa. Poistaja jättää `Data`-kansion paikoilleen (varmistettu). Help → *Open program folder* ja About → PROGRAM FOLDER avaavat kansion.
- **Päivitys** (`App/Services/InstallerUpdater`): Check for updates hakee GitHubin viimeisimmän julkaisun (`api.github.com/repos/AlexNB01/SOWBroadcast/releases/latest`), vertaa tagia (`v3.1.0`) ohjelman versioon, lataa liitteen `SOWBroadcast-Setup-<versio>.exe` väliaikaiskansioon, tarkistaa koon ja (jos liitteenä) `.sha256`-summan, sallii vain `github.com`/`githubusercontent.com`-osoitteet, ja käynnistää asentimen hiljaa (`/SILENT /SUPPRESSMSGBOXES /NOCANCEL /CLOSEAPPLICATIONS`); ohjelma sulkeutuu ja asennin käynnistää sen uudelleen (`[Run]` + `Check: WizardSilent`). "Asennettu" = `unins000.exe` ohjelman vieressä. Ei delta-päivityksiä: jokainen päivitys on koko asennin.
- **Overlayjen tapahtumayhteys on WebSocket (2026-10-17, SSE varalla)**: selain (ja OBS:n CEF) sallii vain 6 HTTP/1.1-yhteyttä samaan osoitteeseen, ja jokainen overlay-sivu piti `/events`-SSE-virran auki, joten kun eläviä lähteitä oli 6 tai enemmän (sisältö- ja transition-sivut, Studio Mode), kaikki muut sivujen pyynnöt (tila, kuvat) jäivät jumiin – todennettu selaimessa (6 SSE → `fetch` ei vastaa). Palvelin tarjoaa nyt `GET /ws` (samat viestit `{"changed":[…],"version":n}` tekstikehyksinä, `?lastEventId=` toistaa puuttuneet, edellisen ajon id → yksi täysi päivitys; vain oma alkuperä sallittu: `Origin` = sama portti ja `127.0.0.1`/`localhost`, muuten 403). `HTML/sow-data.js` avaa yhden WebSocketin per sivu, yhdistää uudelleen (1 s) ja siirtyy `/events`-SSE:hen jos WebSocket ei avaudu kahdella yrityksellä peräkkäin. `/events` ja `POST /notify` ovat ennallaan. Testit: 7 uutta palvelintestiä (järjestys, toisto, täysi päivitys, 12 socketia ei näännytä pyyntöjä, alkuperätarkistus, sulkeminen, selkeä virhe tavalliselle pyynnölle); selaimessa varmistettu: 11 socketia + tavallinen pyyntö toimii, sivu päivittyy, SSE-varapolku toimii. **Ei todennettu oikealla OBS:llä** – että tämä oli Team 2 -päivitysongelman syy on todennäköinen mutta ei varmistettu.
- **Kameroiden OBS-asettelu (2026-10-17)**: scenekokoelman kamerasceneissä (`=> Host/Caster 1/Caster 2 NDI Framing`) on vain NDI-lähde, venytettynä koko kankaalle (2560×1440, rajauksella `bounds_type 3`); kehys-ryhmät, nimitekstit ja kameramaski (`Scale`) on poistettu, joten kamera on aina täysi 16:9-kuva ja sellaisenaan käytettävissä muissa sceneissä. Vanhasta kokoelmasta puuttui Caster 2:n NDI-lähde kokonaan (siksi duo-scenen toinen ruutu oli tyhjä): generaattori tekee `Caster2NDI`:n Caster 1:n kopiona. Single/Duo/Trio-sceneissä kamerat sijoitetaan overlay-sivujen kamera-ikkunoihin (`ObsSceneCollection.ApplyCameraLayout`): single = koko kuva; duo = kaksi 848×477 CSS-pikselin ikkunaa (`duorow.html`: kehys 848×549 miinus 72 px nimilaatta, väli 48, keskitetty alueelle top 220 / korkeus 750), ei rajausta; trio = kolme 596×441 ikkunaa (`triorow.html`: kehys 596×505 miinus 64 px laatta, väli 36), järjestys vasemmalta **Caster 1, Host, Caster 2** (vanha scene oli Host oikealla ja Caster 2 keskellä, vastoin sivua), kuva peittää ikkunan ja rajataan sivuilta (306 px/puoli). Sivut ovat 1920×1080 CSS-pikseliä ja näkyvät kankaalla 4/3-kertaisina (OBS-profiilien perusresoluutio on 2560×1440 kaikissa, tarkistettu `basic.ini`-tiedostoista), siksi kaikki mitat kerrotaan 4/3:lla. Testit lukevat kehysten mitat suoraan sivujen CSS:stä, joten sivun muutos ilman päivitystä tänne kaatuu testiin. **Kokoelma on tuotava OBS:ään uudelleen** (Control → OBS panel → Export scene collection… → Scene Collection → Import). **Ei kokeiltu oikealla OBS:llä.**
- **Välilehtien tiivistys Full HD:lle (2026-10-17)**: Match (linkki yhdelle riville, joukkuepaneelit tiiviimmiksi, mapit riveiksi; ~1350 → ~930 px), General (kolme korttia rinnakkain, värit kahdessa sarakkeessa; ~965 → ~500), Waiting Screen (videot + ticker, countdown + tekstit rinnakkain, somet kahdessa sarakkeessa; ~1045 → ~560), Standings (linkki yhdelle riville, ryhmän lisäys + näyttö + otsikko + alaotsikko yhdelle riville, ryhmät rinnakkain kun ikkuna ≥ 1700 px ja ryhmiä ≥ 2 – `StandingsViewModel.WideLayout/GroupColumns`, ei muokkaus) ja Bracket (joukkuelista 4×4-ruudukkona, otsikko/stage/template yhdellä rivillä, Upper/Lower/Grand Final rinnakkain; ~1250 → ~760). Kaikki mahtuvat ~900 px:n sisältöalueeseen, ja pienin ikkuna 1200×750 on tarkistettu kuvakaappauksin. Match-valitsimen lista: pelattavat ottelut ensin, sitten seuraavat (aikaisin ensin), päättyneet viimeisinä uusin ensin; kun jokin ottelu on käynnissä, päättyneet piilotetaan (valittu ottelu jää näkyviin).
- **Pelaajan hero SOWDraftista (2026-10-17)**: SOWDraft (haara `claude/player-heroes`) tallentaa organisaattorin asettaman heron `players[].hero` (admin: joukkueen muokkaus → pelaajan välilehti → *Hero*, tai `PATCH …/teams/:teamId/player-heroes`), piilottaa sen turnaussivujen julkisista joukkuelistoista ja palauttaa sen `/broadcast`issa kentässä `teams[].players[].hero` (lisäys, `schemaVersion` pysyy 1:ssä). SOWBroadcast lukee sen (`LinkPlayer.Hero`) ja täyttää Match-välilehden pelaajarivin heron katalogin kirjoitusasulla (nimi tai alias); tuntematon hero jätetään tyhjäksi ja tilariville tulee varoitus `Hero "X" is not in the hero list.`; kuten muutkin linkitetyt kentät hero kirjoitetaan vain kun SOWDraftin arvo muuttuu (käsin tehty korjaus säilyy), ja vaihtuessa pelaajarivin pelaajaksi toiseksi heron nollaus toimii kuten ennenkin. Joukkuekirjaston *Import SOWDraft teams* tuo myös herot. Roolia ei johdeta herosta (rooli tulee edelleen SOWDraftin rooleista). Vanha julkinen reitti (`GET /api/tournament-system/:id`) ei kanna herot.
- **SOWDraftin ottelun ajat, kierroksen nimi, statusteksti ja jatkoon-tila (2026-10-17)**: `/broadcast` (SOWDraft-haara `claude/broadcast-rounds-status`) kantaa nyt `matches[].startedAt/completedAt/roundName/statusText` ja `advancement[]` (`stageId, group, teamId, status`: `qualified`/`eliminated`). SOWBroadcast: (1) Match-valitsimen lista: käynnissä olevat ensin (viimeksi alkanut ylimpänä, `startedAt`), sitten tulevat (aikaisin ensin), päättyneet viimeisinä uusin ensin (`completedAt`); kun jokin ottelu on käynnissä päättyneet piilotetaan; rivin teksti käyttää kierroksen nimeä (`Alpha vs Bravo – Semifinals / completed`). (2) Ottelun `statusText` täytetään General-välilehden Status text -kenttään kuten muutkin linkitetyt kentät (kirjoitetaan vain kun SOWDraftin arvo muuttuu, käsin kirjoitettu säilyy; ottelun vaihto kirjoittaa uudelleen); `ApplyLinked` lataa General-välilehden uudelleen. (3) Standings-rivin `Status` tulee `advancement`ista (ryhmävaiheen vaihe + ryhmä + joukkue; sekä vaiheistetussa polussa että palvelimen omassa taulukossa); standings.html värittää qualified/eliminated. SOWDraft-puoli: `started_at` (uusi sarake, leimataan draft-huoneen käynnistyessä ja tyhjennetään peruutuksessa), `completed_at` leimataan nyt myös kun draft kirjaa tuloksen itse (aiemmin vain käsin kirjattaessa); `roundName` (Final/Semifinals/Quarterfinals, Upper/Lower, Grand Final, Round N); `advancement`: päättynyt ryhmä ratkaistaan taulukosta, kesken olevassa ryhmässä joukkue on jatkossa kun enintään `advance − 1` muuta voi vielä yltää sen pisteisiin ja putoaa kun `advance` muuta on jo sen maksimin yläpuolella (tasapisteet jäävät auki). **Ei vielä julkaistu**: sowdraft.fi palvelee vanhaa versiota kunnes SOWDraft päivitetään.
- **standings.html: joukkueen nimet ja logot katosivat (2026-10-17)**: kun taulukossa oli enintään 4 riviä ja laajat sarakkeet (Played/Drawn/For/Against, ne tulevat linkitetystä turnauksesta), rivien skaala nousi 2:een ja kiinteät sarakkeet (910 px × 2 + välit) veivät koko 1688 px:n levyn, jolloin joukkuesarake (`1fr`) kutistui nollaan. Nyt sarakkeiden levyt ja välit lasketaan (`fixedWidth`), skaala rajataan niin että joukkueelle jää vähintään 340 px (`computeRowScale`), ja laajat sarakkeet jätetään pois kun ne eivät mahdu (kaksi taulukkoa rinnakkain: `fitsExtended`). Ulkoasun muu osa ennallaan. Vika oli alkuperäisessä overlayssa, ei uudessa datakerroksessa.
- **Kaavio: vapaakierrokset (2026-10-18)**: SOWDraft luo bye-ottelut (esim. 5–7 joukkuetta 8:n kaaviossa) ja sovellus jättää ne pois, mutta yläkaavion kierros, josta niitä jätettiin pois, kantaa nyt paikkatiedon: `BracketRound.Slots` (kierroksen paikkojen määrä) ja `BracketMatch.Slot` (ottelun paikka, = SOWDraftin `match_number`); ne näkyvät `bracket.json`issa kentinä `slots` / `slot` vain silloin kun kierroksesta puuttuu ottelu. `bracket.html` sijoittaa ottelun paikkaansa ja vetää yhdysviivan seuraavan kierroksen paikkaan `ceil(paikka / 2)`, joten viivat osuvat oikeaan otteluun. Ilman paikkatietoa (käsin tehty kaavio, mallipohjat) toimitaan kuten ennen. SOWDraftin single elimination seedaa nyt vakiojärjestyksessä (8: 1v8, 4v5, 2v7, 3v6), joten seed 1 ja 2 kohtaavat vasta finaalissa. Huom: `bracket.html?preview` piirtää yhdysviivat väärään kohtaan kun esikatselu on skaalattu (mittaus ja piirto eri mittakaavassa); OBS:ssä (mittakaava 1) viivat ovat oikein.
- **Näkyvät käyttäjäkansiot**: logojen ja joukkuetiedostojen valintaikkunat avautuvat ohjelman viereen: `<ohjelmakansio>\Logos\Team Logos`, `<ohjelmakansio>\Logos\Broadcast Logos` ja `<ohjelmakansio>\Team Files` (asennettuna `C:\SOW Broadcast\...`; `AppPaths.UserFilesRoot`, kehityskopiolla datakansion sisällä). Asennin ja ohjelma luovat ne; ne eivät ole piilossa `Data\Scoreboard\Temp`-polussa kuten ennen. Standings-logot käyttävät `Team Logos`-kansiota.
- **Todennettu**: asennin rakennettu, asennettu hiljaa väliaikaiskansioon (HTML, Scoreboard-kuvat, Music, `Data`, poistaja), asennettu ohjelma käynnistyi, loi tiedostonsa `Data`-kansioon ja tarjosi overlayt + `/api/state`n portissa 8324; poisto jätti `Data`:n. 15 uutta testiä (data-kansion valinta, julkaisun luku, päivityksen tarkistus/lataus/summa/koko/vieras palvelin/käynnistys).
- **Ei todennettu**: päivitysketju oikeaa GitHub-julkaisua vastaan (hiljainen päivitys vanhan version päälle ja uudelleenkäynnistys), `release.yml`:n Inno Setup -asennus GitHubissa (kirjoitettu, ei ajettu), asennus puhtaalle koneelle.
- **Koodiallekirjoitusta ei tehdä (2026-10-29)**: omistaja päätti olla hakematta allekirjoitusta (SignPath Foundation tms.), koska ohjelman asentaa korkeintaan muutama ihminen vuodessa. Allekirjoitukseen liittyvät asiat (SignPath-vaiheet ja PFX-polku `release.yml`:stä, `-SignTool` asennusskriptistä, `SignTool` Inno-skriptistä) on poistettu; asennin on allekirjoittamaton ja Windows SmartScreen voi varoittaa asennettaessa ("More info → Run anyway").
- **Odotusruudun ottelulista (2026-10-29)**: `waiting.html` näyttää videon päällä vaihtuvan kortin: tulevat ottelut (käynnissä oleva ensin `LIVE`-merkillä, muut aikajärjestyksessä, aika paikallisena `18:00` / `Sat 18:00`) ja viimeisimmät tulokset. **Starting Soon** (`?mode=start`) kiertää vain tulevat → pelkkä video; **Be Right Back** (`?mode=brb`) tulevat → tulokset → pelkkä video; päätösruutu ei näytä listaa. Kukin sivu on näkyvillä "Seconds per page" sekuntia.
  - **Data**: linkitetyn turnauksen ottelut (`AppState.Schedule`, `ScheduleMapper`; kaikki ei-bye-ottelut myös alkulohkosta) → `GET /api/state` → `schedule`-osio (`ScheduleOverlay.BuildJson`: `enabled, rows, seconds, upcoming[], results[]`, valmiiksi valittuna ja järjestettynä; striimilinkit eivät lähde overlaylle). Uusi SSE-avain `schedule` lähtee vain kun *listattu sisältö* tai asetus muuttuu (ei kaukaisen ottelun muutoksesta). Linkistä tuleva data lähtee heti (KYS-11a); asetukset (Waiting-välilehden *Match schedule* -kortti: päällä, otteluita sivulla 1–8, sekuntia sivulla 5–120, striimilinkin suodatin) Update (Waiting) -napilla. Unlink tyhjentää listan. Ei linkkiä → ei listaa → paneeli ei näy.
  - **Vain striimattavat ottelut**: tulevissa otteluissa näytetään vain ottelut, joille SOWDraftissa on asetettu **Stream-linkki** (ottelun admin-kenttä `stream_url`); suodatinteksti ("Stream link contains", esim. `twitch.tv/suomiow`, kirjainkoolla ei väliä, tyhjä = mikä tahansa linkki) rajaa vielä kanavaan. Tulokset näytetään kaikista päättyneistä otteluista. SOWDraft (`lib/broadcast.js`): `/broadcast` kantaa nyt `matches[].streamUrl` (lisäys, `schemaVersion` 1; README ja testi päivitetty); vanha julkinen reitti kantaa `stream_url`:n jo, ja sieltä luetaan myös `started_at`/`completed_at` jos ne ovat mukana. Tulosten järjestys käyttää `completedAt`ia; ilman sitä (SOWDraftin vanha versio) kierros + ottelunumero.
  - **Järjestys ja sisältö (2026-10-29)**: tulevat ottelut lajitellaan alkamisajan mukaan (`ScheduleOverlay.StartsAt`: `scheduled_at`, muuten ASAP-ottelun toivottu aika `target_at`, muuten ei aikaa → viimeisiksi); ilman aikaa ratkaisevat ASAP ensin, sitten **vaihe** (`ScheduleEntry.StageOrder`: vaiheen järjestys, alkulohko ennen pudotuspelejä), kierros ja ottelunumero (aiemmin kierrosta verrattiin vaiheiden yli, joten pudotuspelien Round 1 meni alkulohkon Round 3:n eteen). **ASAP-ottelu näyttää `ASAP`** (ei `TBA`) kun sille ei ole vielä kellonaikaa; toivottu aika vain järjestää ja valitsee päiväotsikon. Ottelu, jossa kumpikaan joukkue ei ole vielä tiedossa, jätetään pois, ellei Waiting-välilehden valinta *Also list matches in which neither team is known yet* (`WaitingSettings.ScheduleShowUnknown`, oletus pois) ole päällä. SOWDraft (`lib/broadcast.js`): `/broadcast` kantaa nyt `matches[].scheduleAsap` ja `targetAt` (lisäys, README ja testi päivitetty; **vaatii SOWDraftin julkaisun**, vanha julkinen reitti kantaa `schedule_asap`/`target_at` jo).
  - **Kierto (2026-10-29)**: kierto alkaa aina pelkällä videolla, joka pyörii keskeytyksettä *Video alone (seconds)* -asetuksen verran (`WaitingSettings.ScheduleVideoSeconds`, oletus 60, 10–600; JSONissa `schedule.video_seconds`); sen jälkeen sivut (Starting Soon: tulevat, Be Right Back: tulevat → tulokset) kukin *Seconds per page* sekuntia, ja sitten taas video. Tulokset näytetään kaikista päättyneistä otteluista (ei striimilinkkirajausta). **Komento `schedule.show`** (oletus **Ctrl+Alt+L**, HTTP `/api/control/schedule.show`, 409 jos lista on pois päältä tai ei ole mitään näytettävää): overlay lopettaa videon heti ja näyttää listasivut (SSE-avain `schedule.show`, ei kuulu tilaan eikä `StateDiff`iin); kierto jatkuu siitä normaalisti. Todennettu ohjelmalla ja selaimella (HTTP → SSE → overlay).
  - **Ulkoasu (2026-10-29)**: tumma paneeli taustasumennuksella, otsikon alla aksenttiviiva; päiväotsikot (Today / Tomorrow / viikonpäivä, `Later` ajattomille); rivi = vasemmalla aika (tai punainen sykkivä `LIVE`, `ASAP`, `TBA`) ja sen alla kierros, oikealla joukkueet peilattuna kiinteän keskikohdan ympärille logoineen (`team1_logo`/`team2_logo` JSONissa, `TeamLogos/…`-polku suhteessa Scoreboardiin; joukkueella jolla ei ole logoa, jonka logo ei lataudu tai joka on `TBD`, näytetään Overwatch-logo `HTML/assets/ow_logo.png`); seuraava ottelu korostettu ja isompi (`NEXT`); pitkät nimet pienenevät (ei `…`) ja rivien koko skaalautuu (`--k`) kunnes kaikki mahtuvat; tuloksissa isompi tulos ja häviäjä himmennetty; rivit tulevat porrastetulla animaatiolla. Katsottu selaimessa 1920×1080 (Starting Soon ja Be Right Back, tulevat ja tulokset).
  - **Varmistus**: 13 uutta testiä (kartoitus, striimilinkki molemmista reiteistä, suodatus, järjestys, rivimäärä, JSON, diff-avain, vanha tallennus, tallennus) + järjestys/ASAP/piilotus/logot (yhteensä 636 testiä); `GoldenTests.First_update_reports_every_key_in_the_legacy_order` odottaa nyt vanhan avainlistan perään uuden avaimen `schedule`. Overlay katsottu selaimessa oikealla ohjelmalla ja esimerkkidatalla (LIVE, aika, huomisen päivä, suodatus, kiertojärjestys kolmessa tilassa); Waiting-välilehden kortti renderöity. **Ei kokeiltu oikealla SOWDraft-turnauksella eikä OBS:llä.** Live-päivitys (linkki → `schedule`-avain → overlay) on katettu yksikkötesteillä, ei ajettu päästä päähän.
---

## 3. Tietomalli

Kaikki JSON UTF-8, `ensure_ascii=false`, sisennys 2. Kentät on lueteltu nykyisessä muodossa; uuden toteutuksen on
osattava **lukea** vanhat tallennukset (`autosave.json`, `*.sowbroadcast.json`) ja **kirjoittaa** `match.json`
vanhassa muodossa (overlayt lukevat sitä).

### 3.1 Top-level tila (`_collect_state`, SOWBroadcast.py:5044)

```jsonc
{
  "team1": Team, "team2": Team,
  "maps": [ MapEntry x7 ],            // aina 7, myös piilotetut kortit
  "current_map": 1..7 | null,         // johdettu: ensimmäinen näkyvä, ei-completed kortti
  "map_pool": [],                     // aina tyhjä (jäänne) – ei toteuteta
  "assets": { "heroes": {...}, "maps": {...}, "modes": {...} },  // vain autosavessa/diffissä; EI lueta latauksessa
  "general": General, "waiting": Waiting, "standings": Standings, "bracket": Bracket,
  "draft_link_url": "", "tournament_link_url": ""
}
```

### 3.2 Entiteetit

**Team**: `name`, `abbr` (max 6 merkkiä, ei kirjainkoon pakotusta), `logo_path` (absoluuttinen polku), `score` int 0..200
(syötetään käsin, **ei** johdeta mapeista), `color_hex` `#rrggbb` (oletus T1 `#55aaff`, T2 `#ff557f`), `players` (8 kpl,
aina kaikki), `banned_hero` (ei UI:ta, aina `""` – jäänne).

**Player**: `name`, `hero` (`""` = ei valittu), `role` ∈ `Tank|Damage|Support|Flex|""`.

**MapEntry**: `index` 1..7, `map` (nimi tai `""`), `t1`/`t2` int 0..200, `completed` bool,
`pick` ∈ `"—"` (em dash = TBA) | `"T1"` | `"T2"`, `t1_ban`/`t2_ban` (herojen nimet tai `""`),
`winner` ∈ `"t1"|"t2"|""` (**johdettu** pelkistä pisteistä, ei `completed`:sta).

**General**: `first_to` (oletus 2; todellisuudessa kopio `maps_count`:sta, ks. luku 12), `maps_count` 1..7 (oletus 7),
`num_players` 1..8 (oletus 5; Match-välilehden "Players"-spinneri), `format_type` & `division` (käyttämättömiä
jäänteitä – `division` luetaan MapDraft-overlayssa, mutta UI ei aseta sitä), `host`, `caster1`, `caster2`,
`status_text`, `overlay_logo_path`, `transition_logo_path`, `colors{primary…octonary}`.

Overlay-värien oletukset: `primary #FFFFFF`, `secondary #000000`, `tertiary #55aaff`, `quaternary #006ea1`,
`quinary #FFFFFF`, `senary #FFFFFF`, `septenary #FFFFFF`, `octonary #006ea1`. Merkitykset:

| avain | merkitys |
|---|---|
| primary | tekstien takana oleva taustaväri |
| secondary | useimpien tekstien väri |
| tertiary | aksentit, pisteet, "vs" ei-pelissä-sceneissä |
| quaternary | pisteiden tausta ei-pelissä-sceneissä |
| quinary | bracket-matchup-labelit, "Playoffs"-teksti, Away Screen -labelit, somefontti (**ei käytössä** missään overlayssa) |
| senary | Away Screen / Bracket -viestin fontti (**ei käytössä**) |
| septenary | stinger-siirtymän ensisijainen tausta |
| octonary | stinger-siirtymän jälkimmäinen tausta |

**Waiting**: `videos_dir`, `timer_seconds` (= *jäljellä oleva* aika sekunteina), `text_starting` ("STARTING SOON!"),
`text_brb` ("BE RIGHT BACK!"), `text_end` ("THANK YOU FOR WATCHING"), `timer_running`, `socials{}` (vain ei-tyhjät avaimet;
mahdolliset: twitch, twitter, youtube, instagram, discord, web), `ticker_override`, `ticker_override_enabled`.

**Standings**: `title`, `subtitle`, `columns{"mode":"map_diff"}` (kiinteä), `rows` (näytetyn/ensimmäisen ryhmän peili),
`groups[{key,name,rows}]`, `display_group` (`"__all__"` tai ryhmän key).
**StandingsRow**: `team_name`, `abbr`, `logo_path`, `wins`, `losses`, `map_diff`, `points`, `status` ∈ `""|qualified|eliminated`,
`rank`, valinnaiset `played`, `draws`, `sf`, `sa` (vain turnauslinkistä).

**Bracket**: `title`, `stage`, `rounds[{name, side:"upper"|"lower"|"grand", matches[]}]`, `double_elim_view` ∈ `""|upper|lower`,
`teams[16 × TeamRef]`. **BracketMatch**: `id`, `bo_label` ("BO5"), `team1`/`team2` (TeamRef), `score1`, `score2`, `status`,
`scheduled_at` (epoch ms, 0 = ei). **TeamRef**: `name`, `abbr`, `logo_path`.

**Asset** (hero/map/mode): `name`, `image_path` (suhteellinen), `mode` (vain mapit: Control|Escort|Hybrid|Push|Flashpoint),
`source_path` (absoluuttinen lähdetiedosto), `body_image_path`/`body_source_path` (vain heroes), `role` (vain heroes: Tank|Damage|Support).

### 3.3 Tallennusmuodot

| Tiedosto | Sisältö | Huomio |
|---|---|---|
| `autosave.json` (juuri) | koko tila (+ `assets`) | ladataan automaattisesti käynnistyksessä |
| `match.json` (juuri) ja `Scoreboard/Match/match.json` | tila ilman `assets` | overlayt (MapDraft, draft) lukevat |
| `assets.json` (juuri) | `{heroes,maps,modes}` | johdannainen dumppi; totuus on `Scoreboard/<Kat>/index.json` |
| `*.sowbroadcast.json` | sama kuin autosave | File → Save/Save As/Load |
| `*.sowteam.json` | `{version:1,name,abbr,color_hex,logo_png,players[8]}` + `<slug>.png` | pisteitä/bannia ei viedä |

Latausvaatimus: tuntemattomat avaimet **ei** saa kaataa latausta (vanha heittää TypeErrorin; luku 12).

---

## 4. Käyttöliittymä – toiminnallinen spesifikaatio

Ikkuna: otsikko "SOW Broadcast", oletuskoko 1400×860, vähimmäiskoko 1200×750. Valikkopalkki + 5 välilehteä:
**MATCH · GENERAL · WAITING SCREEN · STANDINGS · BRACKET** (isoilla kirjaimilla, Saira; ikonit generisiä).
Pitkät välilehdet (General, Waiting) **vierityskehykseen** – vanhassa ne leikkautuivat/menivät päällekkäin
pienemmässä ikkunassa (opittu kantapään kautta).

### 4.1 Valikot

- **File**: Load…, Save, Save As…, —, Quit. (Save: jos `current_save_path` asetettu ja ≠ autosave → kirjoita siihen, muuten Save As.
  Aina myös autosave. Load ei kutsu Updatea → overlayt päivittyvät vasta seuraavalla Updatella. **[EHD-3]** korjaa.)
- **Customize**: Manage Heroes…, Manage Maps…, Manage Game Modes…, —, Bulk Import from Folders….
- **Teams**: Export Home…, Import Home…, —, Export Away…, Import Away…

### 4.2 MATCH-välilehti

- **Format bar**: "Maps" 1..7 (näyttää/piilottaa N ensimmäistä mapkorttia), "Players" 1..8 (näyttää/piilottaa pelaajarivit
  molemmissa paneeleissa). Piilotettu data säilyy ja viedään.
- **TeamPanel ×2** (jaettu splitterillä): Name · Abbreviation (max 6) · Score (0..200) · Team Color (värinvalitsin) ·
  Load Logo… (png/jpg/jpeg/webp/svg; lähtökansio `Scoreboard/Temp/Team Logos`; esikatselu 120×120) · 8 × PlayerRow.
  - **PlayerRow**: nimi, hero-combo ("— Hero —" + aakkosjärjestys), rooli-combo ("- Role -", Tank/Damage/Support/Flex).
    Heron valinta asettaa roolin automaattisesti (`asset.role` tai oletuskartta); roolia ei koskaan tyhjennetä
    eikä Flexiä päätellä. Käyttäjän käsin valitsemaa roolia ei saa ylikirjoittaa listan uudelleenrakennuksessa (vanha tekee niin).
- **MapCard ×7** (4 sarakkeen ruudukko): `M{n}`, pick-combo (TBA/T1/T2 – T1/T2 näyttävät *joukkueen nimen*, tallennusarvo
  `—`/`T1`/`T2`, identiteetti indeksillä), mapin kuva 176×99 (cover), map-combo (tyhjä + aakkosjärjestys), kaksi pistespinneriä 0..200,
  "Completed"-valintaruutu, "T1 Ban:"/"T2 Ban:" hero-comboja.
- **SOWDraft Live Link** -ryhmä: URL-kenttä, Link, Unlink, tilateksti. Tyhjä URL → varoitus. Linkitettynä: URL ja Link
  lukittu, Unlink aktiivinen. Unlink katkaisee yhteyden, nollaa **kaikki** mapkortit ja kutsuu Updatea.
- **Napit**: **Reset** (nollaa molemmat paneelit + värit + mapkortit; ei muuta format baria/general/linkkejä; vain autosave,
  **ei overlay-pushia – päätetty, KYS-9: vain ja ainoastaan Update päivittää overlayn**; operaattori painaa itse Update. Reset muuttaa vain UI-tilaa ja autosavea;
  suositus: näytä "unsent changes" -merkki kunnes Update painetaan), **Swap Teams** (vaihtaa paneelien koko sisällön; väri kulkee mukana vain jos poikkeaa slotin oletuksesta;
  mapeissa vaihtuu t1↔t2, pick T1↔T2, banit; `completed` ei vaihdu; vain autosave), **Update** (→ luku 5.1).
- Joukkueen nimen muutos päivittää pick-comboiden labelit heti.

### 4.3 GENERAL-välilehti

Casters & Host (host, caster1, caster2) · Logos (overlay-logo ja transition-logo, esikatselu 200×80, lähtökansio
`Scoreboard/Temp/Broadcast Logos`) · Status text (placeholder "Tournament name here") · Overlay colours (8 riviä, selite + värinvalitsin) ·
**Reset this tab** (palauttaa oletukset, ei lähetä) · **Update (General)** (kirjoittaa vain general-exportit + status.txt).

### 4.4 WAITING SCREEN -välilehti

- **Videos**: kansiopolku (vain luku) + Browse + "Use default (SOWBroadcast\Highlights)". Oletuskansio:
  `<base>/SOWBroadcast/Highlights` jos on, muuten `<base>/Highlights`. Valinta tyhjentää polun; selaaminen poistaa valinnan.
- **Countdown**: näyttö `MM:SS` (minuutit rajaamattomat), napit −30s −1m −3m −5m / +30s +1m +3m +5m, ▶ start, ■ pause, ↺ reset.
  Semantiikka (säilytä, ks. myös **[EHD-9]**):
  - `adjust(d)`: remaining = max(0, remaining + d); jos ei käynnissä → preset = remaining.
  - `start`: jos remaining ≤ 0 → remaining = preset; käynnistä 1 s tick.
  - `pause`: vain jos käynnissä.
  - `reset`: pysäytä, remaining = preset.
  - `tick`: remaining = max(0, remaining − 1); pysäytä nollassa; kirjoita **vain** `timer_seconds.txt` + `timer_running.txt` ja
    ilmoita `waiting.timer` (kerran sekunnissa). Ei autosavea.
  - Latauksessa timeria ei käynnistetä; remaining = preset = tallennettu arvo; `timer_running` ohitetaan.
- **On-screen texts**: Starting Soon / Be Right Back / End screen. Tyhjä → oletusteksti.
- **Ticker override**: tekstikenttä (aktiivinen vain jos ruksi) + "Overwrite default ticker text".
- **Socials**: Twitch, Twitter/X, YouTube, Instagram, Discord, Website. Normalisointi `_normalize_handle`
  (SOWBroadcast.py:2018): poista `https?://`, poista alustan domain-etuliite (twitch, twitter/x.com, youtube/youtu.be,
  instagram, discord.gg, discord.com/invite), poista alkuun `@` ja `/`, katkaise ensimmäisestä `/ ? #`. Tyhjät pois.
- **Reset this tab**: oletusvideot päälle, tekstit oletuksiin (lopputeksti "…WATCHING!" huutomerkillä – epäjohdonmukaisuus, yhtenäistä),
  timer reset, ticker tyhjä; **socials ei tyhjene**. **Update (Waiting)**: vain waiting-exportit + status + autosave.

### 4.5 STANDINGS-välilehti

- Turnauslinkki-ryhmä (jaettu Bracketin kanssa): URL, Link, Unlink, tila. Linkitettynä koko välilehti **vain luku**.
- **Groups**: Add Group; "Show in standings.html" (`__all__` = kaikki ryhmät, muuten ryhmän key).
  Ryhmälaatikko: nimi, Remove (ei kun vain yksi), taulukko **Rank · Team · Abbr · W · L · +/- · Points · Status · Logo**
  (+ Add Row / Remove Selected). Rank 0..99, W/L 0..99, +/- −99..99, Points 0..999, Status ""/qualified/eliminated.
  Logo = tekstikenttä + Browse (lähtö `Scoreboard/Teams`). Uusi ryhmä: key `group_{n}`, nimi `Group {len+1}`.
- Title, Subtitle/Group.
- **Update + sort**: tyhjät rivit pois (ei nimeä/lyhennettä/logoa eikä W/L/diff/points); lajittelu
  `(−points, −map_diff, lowercase(nimi))`; ranks 1..n uudelleen (vain rank=0-rivit saavat uuden sijan, käsin asetettu >0 säilyy);
  emit → Update. Lisätilastot (`played/draws/sf/sa`) säilyvät edit-kierroksen yli.
- **Reset this tab**: yksi tyhjä ryhmä; ei lähetä.

### 4.6 BRACKET-välilehti

- Turnauslinkki-ryhmä kuten yllä (lukitsee välilehden).
- Title, Stage (jokainen näppäily laukaisee vanhassa koko Updaten – **älä**, käytä debouncea/Update-nappia).
- **Controls**: Template-combo + Load Template; double-elim view (Upper/Lower; näkyy vain 6- ja 8-tiimin double-elimillä);
  "Bronze match" (vain single-elim: lisää/poistaa kierroksen "Bronze Match", side `lower`).
- **Templatet** (match.id = `m{epoch_s}-{counter}`):

| template | upper | lower | muu |
|---|---|---|---|
| 4 single | Semifinals(2), Final(1) | – | – |
| 6 single (top 2 alkaa kierroksella 2) | Round 1(2), Semifinals(2), Final(1) | – | – |
| 8 single | Quarterfinals(4), Semifinals(2), Final(1) | – | – |
| 4 double | Upper Semifinals(2), Upper Final(1) | Lower Semifinal(1), Lower Final(1) | Grand Final(1, side `grand`) |
| 6 double | Upper Round 1(2), Upper Semifinals(2), Upper Final(1) | Lower Round 1(2), Lower Semifinal(1), Lower Final(1) | Grand Final(1) |
| 8 double | Upper Round 1(4), Upper Semifinals(2), Upper Final(1) | Lower Round 1(2), Lower Round 2(2), Lower Final(1) | Grand Final(1) |

  Template-lataus nollaa double_elim_viewn `upper`ksi ja bronzen pois. Double-view-kontrolli näytetään kierrosmäärien
  signatuurista (upper [4,2,1]+lower [2,2,1] tai upper [2,2,1]+lower [2,1,1]); 4-double ei saa kontrollia.
- **Teams-paneeli**: 16 riviä (nimi + logo "…"; abbr aina ""), "Import from Standings" (valintalista, kaikki valittuina;
  järjestys rank (0→9999) sitten nimi) ja Clear.
- **BracketMatchWidget**: kaksi haettavaa joukkue-comboa (tyhjä + tiimilistan nimet) + kaksi pistespinneriä 0..99.
  Joukkueen valinta kopioi TeamRefin (nimi, abbr, logo).
- **Ei voittajan eteenpäinvientiä** vanhassa – kaikki käsin. **[EHD-4]**
- Update (Bracket), Reset this tab (tyhjentää, lähettää).

### 4.7 Dialogit

- **AssetManagerDialog** (Heroes 700×560, Maps/Game Modes 700×420): vasemmalla aakkosjärjestetty lista; oikealla Name, Mode (vain Maps),
  Role (vain Heroes: "- Role -"+ROLES), Logo (Browse, png/jpg/jpeg/webp, esikatselu 180 px), Heroes: myös Full Body Image (140 px).
  - *Add/Update*: tyhjä nimi → "Missing name". `slug = slugify(name)`; `image_path = Scoreboard/<Heroes|Gametypes|Maps>/<slug>.png`.
    Heroes: `body_image_path = Scoreboard/HeroesBody/<heroBodyFile(name)>.png`; jos kuvaa ei valittu → käytä olemassa olevaa tiedostoa
    tai aiempaa `body_source_path`, muuten "Missing body image" ja keskeytä. Ei viennin heti – vienti seuraavalla Updatella diffin kautta
    (**[EHD-3]**: vie heti).
  - *Delete*: poistaa assetin ja `Scoreboard/<kat>/<slug>.png`:n (HeroesBody-kuvaa **ei** poisteta), vie heti, tyhjentää lomakkeen.
- **BulkImportDialog**: skannaa `Scoreboard/Heroes` ja `Scoreboard/Maps` (png/jpg/jpeg/webp), nimiarvaus tiedostonimestä
  (`-`/`_` → väli, Title Case), rivillä ruksi/nimi/(mapeille Mode). Olemassa olevat nimet ohitetaan. Heroille ei body-kuvaa.
- **BracketTeamsImportDialog**: valintalista TeamRefeistä (kaikki valittuina), Ok/Cancel.
- **Haettava combo** (`make_combo_searchable`, SOWBroadcast.py:22): kaikki comboboxit avaavat natiivin dropdownin sijaan oman
  popupin: suodatuskenttä ("Search…") + lista. Substring-haku kirjainkoosta riippumatta; Up/Down liikkuu näkyvissä riveissä,
  Enter valitsee, Esc sulkee, klikkaus valitsee; leveys = max(combo, 220), korkeus = clamp(40+22·rivit, 120..320). Hiiren
  rulla vaihtaa edelleen arvoa. Tämä on sovelluksen tärkein käytettävyysominaisuus (54 heroa, 31 mappia) – toteuta natiivisti.

---

## 5. Liiketoimintalogiikka

### 5.1 Update (`_update`, SOWBroadcast.py:5232)

**Vanha käytös** – laukeaa: Match-Update, Standings/Bracket-muutos, draft-linkin tila, turnauslinkin data, Unlink, käynnistys.
**Uusi sääntö (KYS-9 + KYS-11): käyttäjän tekemät muokkaukset lähtevät overlayihin vain Update-toiminnoilla** (Match **Update**, **Update (General)**,
**Update (Waiting)**, **Update + sort**, **Update (Bracket)**). Standings/Bracket-kenttien muokkaus, Reset, Swap, Import, Load eivät vie eivätkä lähetä.
**Nimetyt poikkeukset (lähettävät itse):**
1. SOWDraft-/turnauslinkin saapuva data (+ Unlink): koko `_update()` kuten vanhassa (täyttää ja lähettää).
2. Ajastimen komennot (Start/Pause/Reset/±aika): kirjoita `Waiting/timer_*` ja lähetä `waiting.timer` heti. Ei autosavea per komento ei tarvita; ei sekuntitikkejä (EHD-9).
3. Replay-vahtija: kopio soittolistaan + `replaypath.txt` + `replay` heti.
4. Käynnistys: lataa `autosave.json` ja suorita täysi vienti + lähetys (all keys).
Kaikki muu tilan muutos: vain autosave + "unsent changes" -merkki.
1. `state = collect()`; `changed = diff(old, state)`.
2. Vie Heroes/Maps/Gametypes **vain jos** `assets.*` on diffissä.
3. Vie general, match, waiting, standings, bracket, status.txt (aina).
4. `notify(changed)`.
5. Kirjoita juuren `match.json` (tila ilman assets), `assets.json`, autosave.
6. Tallenna `lastState`.
`_update_general_only` / `_update_waiting_only` vievät vain oman osansa (+ status). Ne **eivät saa** korvata `lastState`a
kehyksellä (vanha tekee niin ja aiheuttaa vääriä diffejä).

### 5.2 Diff-avaimet (`_diff_for_scoreboard`, :4262)

Ei vanhaa tilaa → emittoi kaikki. Muuten:
`assets.heroes|maps|modes` (vertaa vain nimiä, image_path, mode – **ei** roolia/bodya/source_pathia, bugi),
`general.colors|first_to|logo|status|caster1|caster2|host`, `waiting.texts|timer|videos|socials`,
`t1.*`/`t2.*` ∈ `name|score|color|logo|ban|abbr|players`, `maps`, `standings`, `bracket`,
`matchtext` (kun maps/nimi/score/ban/ticker-override muuttuu), `replay` (replay-vahtija).
`general.first_to` laukeaa `maps_count`in muutoksesta.

### 5.3 Pienet algoritmit

- **slugify** (:4507): NFKD → pienet kirjaimet → poista yhdistävät merkit → `[^a-z0-9]+` → `-` → trimmaa `-` → tyhjä = `"item"`.
  `Lúcio→lucio`, `D.Va→d-va`. (Ei-latinalaiset nimet törmäävät `"item"`-arvoon – korjaa.)
- **heroBodyFile** (:260, peilattava MapDraft.html:254-268 JS:ssä): ylikirjoitukset `d.va|dva→Dva`, `lúcio→Lucio`, `soldier: 76→Soldier_76`,
  `wrecking ball→Wrecking_Ball`, `junker queen→Junker_Queen`, `jetpack cat→Jetpack_Cat`, `torbjörn→Torbjörn`;
  muuten sanat isolla alkukirjaimella, `_`-liitettynä; tyhjä → `Hero`. **[EHD-10]**: siirrä manifestiin (`body_image`-kenttä on jo index.json:ssa).
- **hero → rooli**: `asset.role` tai `HERO_ROLES[nimi]` (:170). Hero-roolit löytyvät valmiina `Scoreboard/Heroes/index.json`:sta (54 kpl) – käytä sitä.
- **matchtext** (:4037): `"{T1} ({s1} - {s2}) {T2}"` + jokaiselle mapille jolla on nimi `"{map} ({m1} - {m2})"`, erotin `"      •      "`
  (6 väliä, •, 6 väliä), perään **30 välilyöntiä**.
- **current_map**: ensimmäinen *näkyvä* kortti jossa `completed=false`, muuten null.
- **Joukkueen väri swapissa**: väri kulkee vain jos eroaa slotin oletuksesta (#55aaff / #ff557f).
- **Standings-lajittelu** ja **turnauslinkin standings**: pisteet 3/voitto; draw = valmis ottelu ilman `winner_team_id`;
  lajittelu pisteet → (sf−sa) → sf; `map_diff = sf − sa`.
- **Logopolun normalisointi** (:3688): Scoreboard-juuren sisällä → suhteellinen, `/`-erottimin; muuten absoluuttinen `/`-erottimin.
- **Team export** (:4072): oletus `<Scoreboard/Teams>/<slug>.sowteam.json`; logo → `<slug>.png` (ohita jos kohde uudempi). **Import** (:4118):
  säilyttää nykyisen pistemäärän, ban = ""; väri = tiedoston tai paneelin oletus; logohaku: `logo_png` → `<jsonbasename>.png` → `Logo.png`;
  pelaajat täytetään 8:aan.

---

## 6. Tiedostokontrakti (`Scoreboard/**`) – **pidä täsmälleen**

Juuri: `base = $SOWB_ROOT || sovelluksen kansio`. Kaikki `.txt` UTF-8; kirjoita vain jos sisältö muuttui. Yksiarvoiset tiedostot ilman
loppurivinvaihtoa; `key=value`-tiedostot CRLF.

| Polku (`Scoreboard/`) | Muoto / esimerkki | Lukija |
|---|---|---|
| `Match/T1Name.txt`, `T2Name.txt` | teksti | lähes kaikki overlayt |
| `Match/T1Score.txt`, `T2Score.txt` | kokonaisluku | scoreboard, MapDraft, draft, maps, duo/trio/single (piilottavat boxin kun 0–0) |
| `Match/T1Color.txt`, `T2Color.txt` | `#rrggbb` | scorebar-overlayt, team1/2 |
| `Match/T1Abbr.txt`, `T2Abbr.txt` | teksti | ei kukaan (draft lukee abbrin match.jsonista) |
| `Match/T1Logo.png`, `T2Logo.png` | PNG (uudelleenkoodattu valitusta kuvasta, force) | scoreboard, MapDraft, draft, maps, duo/trio/single, team1/2 |
| `Match/T{n}P{1..8}Name.txt` / `Role.txt` / `Hero.txt` | teksti; aina 8 riviä, tyhjä jos ei pelaajaa | team1.html, team2.html |
| `Match/T1Players.txt`, `T2Players.txt` | `idx\tname\thero\trole` + CRLF | ei kukaan (OBS-tekstilähde / legacy) |
| `Match/Map1.txt … Map7.txt` | `Name=`, `T1=`, `T2=`, `Completed=0\|1`, `Pick=—\|T1\|T2`, `T1Ban=`, `T2Ban=` | scoreboard, maps, draft |
| `Match/CurrentMap.txt` | 1..7 tai tyhjä | scoreboard, maps, draft |
| `Match/maps.txt` | yksi `slug.png` per rivi (mappipooli) | vain `draft.html` → **poistuu uudesta versiosta (KYS-10)**; ei toteuteta |
| `Match/map_pool.txt` | mapin nimi per rivi | ei kukaan |
| `Match/matchtext.txt` | ks. 5.3 | tickerit (startingsoon, berightback, thankyou) |
| `Match/status.txt` | teksti | scoreboard, draft, duorow, triorow |
| `Match/match.json` | koko tila ilman assets | MapDraft*, draft |
| `Match/seriestext.txt` | `ESM 2025` (jäänne) | ei kukaan – **ei toteuteta** |
| `General/host.txt`, `caster1.txt`, `caster2.txt` | teksti; muoto `Nimi` tai `Nimi @handle` | duorow, triorow, singlecam **ja OBS text_gdiplus -lähteet** |
| `General/first_to.txt` | **maps_count** (1..7; harhaanjohtava nimi, säilytä) | scoreboard, maps |
| `General/colors.txt` | `key=#hex` CRLF, 8 avainta | kaikki paitsi scoreboard |
| `General/OverlayLogo.png`, `TransitionLogo.png` | PNG | maps, waiting, replay, transition |
| `General/Icons/*.svg` | discord, instagram, twitch, twitter, web, youtube | waiting-overlayt |
| `Waiting/text_starting.txt`, `text_brb.txt`, `text_end.txt` | teksti | vastaava scene |
| `Waiting/timer_seconds.txt`, `timer_running.txt` | kokonaisluku; `0\|1` | startingsoon, berightback |
| `Waiting/ticker_override.txt`, `ticker_use_override.txt` | teksti; `1` vain jos päällä **ja** ei-tyhjä | waiting-overlayt |
| `Waiting/videos.txt` | tiedostonimet, yksi per rivi, järjestetty | waiting-overlayt |
| `Waiting/videos_dir.txt` | aina tyhjä (overlayt ohittavat) | – |
| `Waiting/socials.json` | `{"twitch":"..","twitter":".."}` | waiting-overlayt |
| `Waiting/Playlist/` | kopio videokansiosta (`.mp4 .mov .webm .mkv`; kopioi jos puuttuu/uudempi; poista muut) | waiting-overlayt |
| `Replay/replaypath.txt` | yksi tiedostonimi ilman polkua | replay.html |
| `Replay/Playlist/<n>-Replay-Replay.mp4` | n = suurin etuliite + 1 | replay.html |
| `Standings/standings.json` | `{title,subtitle,columns,rows,groups[{key,name,rows[{team,abbr,logo,wins,losses,map_diff,points,status,rank,played?,draws?,sf?,sa?}]}],display_group}` | standings.html |
| `Bracket/bracket.json` | `{title,stage,rounds[{name,side,matches[{id,bo_label,team1{name,abbr,logo},team2,score1,score2,status,scheduled_at}]}],double_elim_view}` (ei `teams`) | bracket.html |
| `Standings/updated_at.txt`, `Bracket/updated_at.txt` | `YYYY-MM-DD HH:MM:SS`, vain jos sisältö muuttui | – |
| `Heroes/<slug>.png`, `Heroes/index.json` (+`index.txt`) | `{"heroes":[{name,slug,image:"Heroes/ana.png",role,body_image?}]}` | scoreboard, draft, maps (portretit) |
| `HeroesBody/<heroBodyFile>.png` | koko vartalo | MapDraft*, team1/2 |
| `Maps/<slug>.png`, `Maps/index.json` | `{"maps":[{name,slug,image,mode}]}` | scoreboard, MapDraft*, draft, maps |
| `Gametypes/<slug>.png`, `Gametypes/index.json` | `{"modes":[{name,slug,image}]}` | scoreboard, draft, maps |
| `Roles/Tank\|Damage\|Support\|Flex.png` | roolikuvakkeet | team1/2 |
| `TournamentLogos/<sha1(url)[:16]><ext>` | ladatut turnauslogot | standings, bracket |
| `Teams/`, `Temp/{Team Logos,Broadcast Logos,Maps}/` | käyttäjän työkansiot | – |
| `__last_gui_touch.txt` | `ok` | ei kukaan (jäänne, älä toteuta) |

Muut tiedostot: `Highlights/` (odotusruudun videot), `Videos/` (tyhjä, ei käytössä), `Music/` (6 mp3, OBS:n VLC-lähde; ei sovelluksen koodissa).

**Assettien käynnistys**: (1) jos `index.json` puuttuu → kopioi puuttuvat kuvat paketoidusta `Scoreboard/<alikansio>`:sta (ei ylikirjoita);
(2) lataa `index.json`; epäonnistuessa skannaa kuvakansio (nimi = tiedostonimi Title Casena); (3) vie kaikki kolme kategoriaa joka käynnistyksessä.
Assettivienti = `shutil.copy2` (ei uudelleenkoodausta; ohita jos koko sama ja kohde ei vanhempi), `index.txt` + `index.json`.

---

## 7. HTTP-palvelin ja SSE-protokolla

Nykyinen `server.py` (120 riviä): `GET /events` (SSE), `POST /notify`, kaikki muu staattisena tiedostopalvelimena juuresta.
Overlayt käyttävät juuri-relatiivisia URL:eja (`/Scoreboard/Match/T1Name.txt`), OBS käyttää `http://127.0.0.1:8324/HTML/<sivu>.html`.

| | Vaatimus (yhteensopivuus) | Parannus |
|---|---|---|
| Portti/osoite | `127.0.0.1:8324` oletus | konfiguroitava; vapaa portti jos varattu; **[EHD-1]** |
| Otsakkeet | `Cache-Control: no-cache, no-store, must-revalidate`, `Pragma: no-cache`, `Expires: 0` joka vastauksessa | – |
| `GET /events` | `text/event-stream`; ensin `: connected\n\n`; `id: <n>\ndata: {"changed":[...]}\n\n`; keepalive `: keepalive\n\n` 15 s välein | jono + `Last-Event-ID`-uudelleentoisto; ei vanhan viestin toistoa uudelle asiakkaalle |
| `POST /notify` | runko `{"changed":[string]}` → **204**; virhe → **400** | rajoita rungon koko; tarkista `Content-Length`; vain localhost; tunniste-token **[EHD-1]** |
| Staattiset | koko juuri | **vain** `HTML/`, `Scoreboard/`, `Music/`, `Videos/`, `Highlights/` (ei `autosave.json`) |
| CORS | ei | ei tarvita (same-origin) |

**changed-avainten sanasto**: luku 5.2. Overlayt reagoivat vain tunnettuihin avaimiin ja hakevat sitten tiedostot uudelleen
(`fetch(polku + "?_=" + Date.now())`, olemassaolo `HEAD`-pyynnöllä).

Sovelluksen `notify` on synkroninen 0,5 s timeoutilla, virheet nielty. Uudessa: asynkroninen.

---

## 8. Integraatiot

### 8.0 UI-merkkijonot englanniksi (KYS-5)

Vanhan sovelluksen suomenkieliset tekstit korvataan:

| Vanha | Uusi (EN) |
|---|---|
| Ei linkitetty | Not linked |
| Yhdistetään… | Connecting… |
| Linkitetty | Linked |
| Yhdistetty, haetaan dataa… | Connected, fetching data… |
| Virhe: … | Error: … |
| Linkki ei ole kelvollinen URL. | The link is not a valid URL. |
| Linkistä ei löytynyt matchId-parametria. | The link has no matchId parameter. |
| Turnausta ei löytynyt. | Tournament not found. |
| Liitä SOWDraftin stream-linkki… (placeholder) | Paste the SOWDraft stream link (e.g. https://…/stream?matchId=…) |
| Liitä ensin SOWDraftin …-linkki. | Paste a SOWDraft link first. |
| Valitse tiimin logo / overlay-logo / transition-logo / väri | Select team logo / overlay logo / transition logo / colour |

Muut tekstit (valikot, napit, `Missing name`, `Imported X heroes and Y maps.`) ovat jo englantia – säilytä. Käytä brittiläistä `colour`ia
siellä missä vanha sovellus käyttää sitä ("Overlay colours", "Select colour"), ja pidä käytös yhtenäisenä. Tekstit yhteen resurssitiedostoon.

> **Huom:** luvut 8.1 ja 8.2 kuvaavat **vanhan sovelluksen** linkitystä. Uusi sovellus toteuttaa luvun **8.4** mallin (turnaus ensin); 8.1/8.2 ovat
> `LegacySource`-varatien ja siirrettävien sääntöjen (logojen lataus, standings/bracket-kuvaus, hero-/mappihaku) viitekuvauksia.

### 8.1 SOWDraft-linkki (`draft_link.py`, 93 riviä)

- Asiakas: Socket.IO (`websocket` + `polling`, auto-reconnect, `wait_timeout=10`). Palvelin: `https://sowdraft.fi` (origin otetaan liitetystä URL:sta).
- URL: mikä tahansa absoluuttinen URL jossa kyselyparametri `matchId`. Esim. `https://sowdraft.fi/stream?matchId=<id>&role=stream`.
  Virheet: "Linkki ei ole kelvollinen URL." / "Linkistä ei löytynyt matchId-parametria."
- Yhteyden avautuessa (myös jokaisen reconnectin jälkeen): emit `join-match {matchId, role:"stream"}` (julkinen, ei tokenia).
  Kuuntele `update-state` (payload dict), `error`, `connect_error`, `disconnect`.
- Tilat UI:lle: `connecting`, `connected`, `disconnected`, `error:<viesti>` → "Yhdistetään…", "Linkitetty", "Ei linkitetty", "Virhe: …".
- **Payload-kentät**: `team1Name`, `team2Name`, `previousMaps[{map,winner}]`, `selectedMap`, `selectedMapTeam`, `heroBans[{team,hero}]`,
  `summary[{pickedBy, entries[{type:"result", winner, team1Score, team2Score}]}]`.
- **Kuvaus tilaan** (`_on_draft_link_state`, :4955):
  - Joukkueiden nimiä/logoja/värejä **ei** tuoda. Puolet päätellään vertaamalla trimmattuja, kirjainkoosta riippumattomia nimiä
    GUI:n joukkueisiin (suora tai vaihdettu järjestys → `sides_swapped`). Jos ei täsmää: pick/ban/pisteet jätetään asettamatta
    (mapit ja completed asetetaan silti). **[EHD-8]**: näytä varoitus tästä.
  - `ordered_maps = previousMaps[].map + selectedMap`. Korteille `i < len(previousMaps)`: `completed=true`; viimeinen = nykyinen (ei completed).
  - Mapin nimi etsitään combosta täsmälleen tai välimerkit/kirjainkoko ohittaen (`King's Row` ↔ `King S Row`).
  - Pick = `summary`-kierroksen `pickedBy` (päätetyt) tai `selectedMapTeam` (nykyinen); tuntematon → TBA.
  - Pisteet päätetylle mapille: summaryn `team1Score`/`team2Score` (vaihdetaan jos sides_swapped), muuten `winner` → 1–0 / 0–1. Tasapelit eivät ole `previousMaps`issa.
  - Hero-bannit vain nykyiselle, päättämättömälle mapille (`heroBans[].team` nimellä → `t1_ban`/`t2_ban`); vanhoja ei tyhjennetä.
  - Lopuksi `_update()`.
- Linkin URL tallentuu tilaan mutta **ei yhdistä uudelleen käynnistyksessä** – **[EHD-5]**.

### 8.2 Turnauslinkki (`tournament_link.py`, 145 riviä + kuvaus `SOWBroadcast.py:462-715`)

- URL: `/tournament/<id-tai-slug>` tai `?id=`. REST: `GET {origin}/api/tournament-system/{id}`, julkinen, headerit
  `User-Agent: Mozilla/5.0 (compatible; SOWBroadcast)` (**pakollinen** – CDN palauttaa 403 oletus-UA:lle), `Accept: application/json`, timeout 10 s.
  Vastauksen on oltava dict jossa `tournament` (tai "Turnausta ei löytynyt."). `tournament` ja `stages` voivat tulla JSON-merkkijonona.
- Ensimmäisen haun jälkeen slug korvataan oikealla UUID:lla (`tournament-update` lähetetään vain UUID:lla). `data["_origin"]` lisätään logojen resoluutiota varten.
- Live: Socket.IO samaan originiin, kuuntele `tournament-update` (arg = tournament id); jos täsmää → **koko REST-haku uudelleen** taustasäikeessä.
  Ei pollausta. Virheessä yksi uudelleenyritys 4 s päästä. Tilat kuten draftissa; "Yhdistetty, haetaan dataa…" kunnes data saapuu, sitten "Linkitetty".
- Vastauskentät: `tournament{id,name,format,best_of,stages}`, `teams[{id,name,logo_url}]`,
  `matches[{id,stage_id,bracket,group_name,round,match_number,team1_id,team2_id,score1,score2,winner_team_id,status,scheduled_at}]`,
  `standings` (vain ei-vaiheistetuissa), stage `{id,name,format,best_of}`.
- **Logot**: `urljoin(origin, logo_url)` → **ladataan** `Scoreboard/TournamentLogos/<sha1(url)[:16]><ext|.png>` (SOWDraft lähettää
  `Cross-Origin-Resource-Policy: same-origin`, joten hotlinkkaus OBS:ssä estyy). Välimuisti per sessio, virheet välimuistiin `None`. Lataa **taustasäikeessä**.
- **Standings**: vain vaiheet joiden `format` ∈ `round_robin, swiss, league, group_stage`. `group_stage` → ryhmä per `group_name`
  (matchit joissa `bracket=="group"`), key `"{stageId}_{gname}"`, nimi `"{stage} - Group {g}"`. Rivit lasketaan paikallisesti (5.3).
  Ei-vaiheistettu → palvelimen `standings` (dict ryhmittäin tai lista; kentät `wins,losses,mapDiff,points,name,id`).
  Tulos: `columns{"mode":"map_diff"}`, `rows` = ensimmäinen ryhmä, `display_group` = ensimmäisen ryhmän key.
- **Bracket**: vaiheet `single_elimination, double_elimination, group_stage`. `bye`-matchit pudotetaan; säilytä vain `bracket` ∈
  `main, winners, losers, grand_final, playoff`. Sivu: `winners→upper`, `losers→lower`, `grand_final→grand`, muu→`upper`.
  Kierrosnimi etäisyydestä viimeiseen: `Final` / `Semifinals` / `Quarterfinals` / `Round N`, etuliite `Upper `/`Lower `; grand → `Grand Final`;
  useampi vaihe → ei enää etuliitettä (uusi sovellus jättää vaiheen nimen pois kierrosotsikoista, 2026-10-18). `bo_label = "BO{best_of}"`. Tiimilista max 16; `double_elim_view="upper"`; `stage=""`.
- Linkin jälkeen Standings- ja Bracket-välilehdet lukitaan kunnes Unlink (data jää paikalleen).

### 8.4 Linkityksen uusi malli: turnaus ensin (PÄÄTETTY 2026-10-15, korvaa 8.1:n ja 8.2:n käyttötavan)

Lähtökohta: SOWDraft-koodin lukeminen (`sowdraft/`: `sockets/index.js`, `lib/gameState.js`, `routes/tournamentSystem.js`) osoitti, että turnauksen ottelu tuntee oman draft-huoneensa
(`tournament_matches.draft_match_id`), ja draft-tilassa on kaikki tarvittava ilman nimivertailua. Nykyinen "liitä draft-huoneen stream-linkki" -tapa on varakeino, ei pääreitti.

**Käyttäjän näkökulmasta**
1. Käyttäjä liittää **vain turnauksen osoitteen** (`https://sowdraft.fi/tournament/<slug-tai-id>`) ja painaa **Link**. Kenttiä on yksi.
2. Sovellus hakee turnauksen (REST) ja näyttää **Match**-valitsimen, jossa on ottelut listana ("Team A vs Team B – round / status"; käynnissä olevat draft-ottelut korostettuina ja listan kärjessä).
   **Ottelun valitsee aina käyttäjä (KYS-13). Automaattista valintaa ei ole**: ei "follow live match" -tilaa, ei tuoreimman oletusvalintaa. Ennen valintaa mitään ei täytetä (tila `No match selected`).
   Valinta tallennetaan ja palautuu käynnistyksessä (tämä ei ole automaattista valintaa – käyttäjä valitsi sen); kun ottelu päättyy, sovellus **ei** vaihda seuraavaan vaan ilmoittaa `Match complete` ja odottaa uutta valintaa.
3. Valitun ottelun joukkueet **täytetään** Team 1/Team 2 -paneeleihin (nimi, lyhenne, logo, väri jos SOWDraftissa on, pelaajat) ja draft-huoneen tila täyttää mapit, pickit, bannit ja pisteet. Tieto **lähetetään overlayihin heti** (KYS-11a).
4. **Sivut**: oletus SOWDraft team1 → Team 1 -paneeli. Käyttäjä voi vaihtaa "Swap sides" -kytkimellä; valinta tallennetaan tilaan (`link.sidesSwapped`), ei johdeta nimistä.
5. **Käsin tehdyt muutokset**: linkitetty kenttä saa linkki-merkin. Käyttäjä voi muokata sitä; SOWDraftin arvo ylikirjoittaa käsin muokatun **vain jos SOWDraftin arvo itse muuttuu** (verrataan viimeksi vastaanotettuun arvoon). Näin SOWDraft ei taistele operaattorin korjauksia vastaan.
6. **Käynnistys**: tallennettu linkki (turnaus-id, valittu ottelu, auto/manuaali, swap) yhdistyy uudelleen automaattisesti (EHD-5 ✔ hyväksytty).
7. **Varakeino**: "Advanced → Link draft room manually" hyväksyy vanhan `…/stream?matchId=…` -linkin (sama käsittely, ilman turnaustietoa; joukkueet täytetään draft-tilan nimistä/logoista, sivut valitaan käsin).
8. Tilarivi (EN): `Not linked` / `Connecting…` / `Linked – <tournament> – <Team 1> vs <Team 2>` / `No match selected` / `Waiting for draft to start…` (valittu ottelu ilman draft-huonetta) / `Match complete` / `Error: …` + "last update hh:mm:ss"; varoitus jos draft-huoneen joukkue ei vastaa valittua ottelua.

**Tekninen toteutus (SOWBroadcast-puoli)**
- Abstraktio `IDraftSource` kahdella toteutuksella: **`BroadcastApiSource`** (uusi, ks. 8.5) ja **`LegacySource`** (nykyiset julkiset rajapinnat). Jos `GET …/broadcast` palauttaa 404 → käytä LegacySourcea automaattisesti. Yhteinen sisäinen malli `LinkedState` (alla), josta tilaan kirjoitus on yksi funktio.
- **Reitti 1 (uusi)**: REST `GET /api/tournament-system/:id/broadcast[?matchId=]` + Socket.IO `join-tournament` → `tournament-event` (liveMatch-payload mukana, ei lisähakua).
- **Reitti 2 (legacy)**: REST `GET /api/tournament-system/:id` (julkinen) + Socket.IO `join-match {matchId, role:"stream"}` → `update-state` (draft-huone) + `tournament-update` (id-merkkijono, **tulee kaikista turnauksista** → suodata tunnuksella, myös slug→UUID).
- **Eheyden varmistus** (SOWDraftin tapahtumat eivät kanna järjestysnumeroa legacy-reitillä): haku uudelleen (a) jokaisen Socket.IO-reconnectin jälkeen, (b) 30 s välein hiljaisesti, (c) kun `version` hyppää yli yhden (uusi reitti). Koko tila haetaan kerralla, joten toistot ovat idempotentteja.
- **WAF/CDN**: sowdraft.fi:n edessä oleva suoja palautti 403 Python-urllibin User-Agentille. Käytä `User-Agent: SOWBroadcast/<versio> (+https://suomiow.fi)` ja pyydä SOWDraftin ylläpitoa sallimaan se (S5 alla). Älä imitoi selainta.
- Tokenit: tiimitokeneita (`team1`/`team2`-roolit) **ei koskaan** käytetä, tallenneta eikä lokiteta. Vain `stream`-rooli.
- Logot: kuten 8.2 (lataus taustasäikeessä `Scoreboard/TeamLogos/` – huom. uusi kansio joukkueiden logoille, `TournamentLogos` säilyy standings/bracketille); koko- ja tyyppitarkistus (PNG/JPEG/WebP/SVG, max ~2 Mt).

**`LinkedState` → SOWBroadcast-tila (kuvaus)**

| Lähde (SOWDraft) | Kohde (SOWBroadcast) | Sääntö |
|---|---|---|
| `teams[].name` | `team{n}.name` | suoraan |
| `teams[].abbr` | `team{n}.abbr` | jos puuttuu (KYS-12 ✔): **ehdota automaattisesti** nimen 3 ensimmäistä kirjainta/numeroa isoina (vain kirjaimet/numerot, välit ohitetaan); merkitse "suggested" -merkillä ja anna muokata; SOWDraftin oma arvo voittaa heti kun se ilmestyy |
| `teams[].color` | `team{n}.color_hex` | jos puuttuu: säilytä paneelin nykyinen väri |
| `teams[].logoUrl` | `team{n}.logo_path` | lataa paikalliseksi (8.2) |
| `teams[].players[]` | `team{n}.players[0..7]` | max 8 ensimmäistä SOWDraftin järjestyksessä; `name` = battletagin osa ennen `#` (max 12 merkkiä), muuten `name`, muuten Discord-nimi |
| pelaajan `role` + `secondaryRoles` | `players[].role` | `Tank→Tank`; `Hitscan`, `Flex DPS→Damage`; `Main Support`, `Flex Support→Support`; jos pää+toissijaiset kattavat kaikki kolme kategoriaa (tank/dps/support) → `Flex` (sama sääntö kuin SOWDraftin `dsPlaysAllRoles`) |
| `live.score.team1/team2` | `team{n}.score` | **automaattisesti** (EHD-4 ✔ tähän tapaukseen): ottelun kokonaispisteet |
| `live.maps[i].map` | `maps[i].map` | etsi comboista täsmälleen, sitten välimerkit/kirjainkoko ohittaen (`King's Row`); löytymättömästä varoitus tilariville, ei hiljaista ohitusta |
| `live.maps[i].pickedBy` (`team1`/`team2`/`null`) | `maps[i].pick` | slotti→T1/T2 (swap huomioiden); `null`/auto → `TBA` (`—`) |
| `live.maps[i].bans[]` (`team`,`hero`) | `maps[i].t1_ban`/`t2_ban` | **jokaiselle mapille**, ei vain nykyiselle (vanha ei tehnyt näin) |
| `live.maps[i].result` | `maps[i].t1/t2`, `completed` | `team1Score/team2Score` jos on (Control/Escort/Flashpoint/Hybrid); muuten `winner` → 1–0 / 0–1 (Push); `draw` → annetut pisteet (tai 0–0), `completed=true`; swap vaihtaa t1↔t2 |
| `live.maps[i].status == "current"` | `completed=false`, `current_map` | viimeinen käynnissä oleva kortti |
| `live.maps.length` | `general.maps_count` | **ei muuteta** automaattisesti (format-palkki on operaattorin); varoita jos `mapCount`-sarja ei mahdu |
| `live.phase == "complete"` | tilarivi | näytä "Match complete" |

Mapit täytetään korteille 1..N kronologisessa järjestyksessä (`order`). Tasapelit näkyvät omana korttinaan (vanhassa ne katosivat, koska SOWDraft ei kirjannut niitä `previousMaps`iin).

**Standings ja bracket**: kuten 8.2 (turnauslinkin kuvaus), mutta jos `GET …/broadcast` palauttaa palvelinlaskemat `standings`/`stages`, käytä niitä, älä laske pisteitä paikallisesti uudelleen (poistaa "3 pistettä/voitto" -kovakoodauksen, sillä SOWDraftin stage-asetuksissa `pts_win/pts_draw/pts_loss/tiebreakers` ovat säädettävissä).

**Testit (luku 13:een lisättävät)**: fikstuurit joukkueen nimen kirjoitusasun eroista, vaihdetuista sivuista, tasapelistä, kahdesta samasta mapista, Push-mapista (ei yksityiskohtaisia pisteitä), auto-pickistä (`pickedBy:null`), katkenneesta yhteydestä kesken drafin, kahdesta samanaikaisesta in_progress-ottelusta, slug→UUID-vaihdosta, tuntemattomasta mapista/herosta.

### 8.5 Pyynnöt SOWDraft-puolelle (päätetty: kaikki neljä + yksi; toteutus erillisenä SOWDraft-työnä)

SOWDraft on omistajan oma palvelu (`C:\Suomi OW koodiprojektit\sowdraft`, Node.js 20 + Express 4 + Socket.IO 4 + SQLite). Muutokset ovat **taaksepäin yhteensopivia**: nykyisiä päätepisteitä, tapahtumia ja kenttiä ei muuteta tai poisteta.

**S1 – `GET /api/tournament-system/:id/broadcast[?matchId=<tournament_match_id>]`** (julkinen; `:id` = UUID tai slug, kuten muut reitit)
Palauttaa yhden dokumentoidun, versioidun JSON:n. Palvelin tekee nimi→slotti-resoluution, joten asiakas ei vertaa nimiä.

```jsonc
{
  "schemaVersion": 1,
  "version": 1234,                    // kasvava laskuri per turnaus (muistissa riittää; nollautuu uudelleenkäynnistyksessä → asiakas hakee silloin koko tilan)
  "updatedAt": 1760000000000,
  "tournament": { "id", "slug", "name", "format", "status", "bestOf", "logoUrl" },
  "stages": [ { "id", "name", "format", "bestOf", "ptsWin", "ptsDraw", "ptsLoss", "tiebreakers": [] } ],
  "teams": [ { "id", "name", "abbr": null|"EE", "color": null|"#rrggbb", "logoUrl": null|"https://…" (absoluuttinen),
               "players": [ { "name", "battletag", "role", "secondaryRoles": [], "country" } ] } ],   // ei discord_user_id, ei yhteystietoja
  "matches": [ { "id", "stageId", "bracket", "groupName", "round", "matchNumber", "team1Id", "team2Id",
                 "score1", "score2", "winnerTeamId", "status", "scheduledAt", "draftMatchId", "bestOf" } ],
  "standings": { /* kuten nykyinen GET /api/tournament-system/:id */ },
  "live": null | {                    // VAIN kun ?matchId= annettu ja ottelulla on draft_match_id; muuten null (ei automaattista "uusin in_progress" -valintaa, KYS-13)
    "tournamentMatchId", "draftMatchId", "team1Id", "team2Id",
    "phase": "ready-check|map|side-selection|hero-ban|result|complete",
    "mapCount": 2,                    // voitettavien mappien määrä (first to)
    "score": { "team1": 0, "team2": 0 },
    "currentTurn": "team1|team2|null", "countdownEnd": null,
    "maps": [ { "order": 1, "map": "Lijiang Tower", "gamemode": "control",
                "pickedBy": "team1|team2|null", "auto": false,
                "side": null | { "chosenBy": "team2", "side": "Attack|Defense" },
                "bans": [ { "team": "team1", "hero": "Ana" } ],
                "status": "current|decided",
                "result": null | { "winner": "team1|team2|draw", "team1Score": 2, "team2Score": 1 } } ]
  }
}
```
Johto: `live.maps` rakennetaan draft-tilan `summary`-listasta (`type:"map"` -merkinnät kronologisesti, mukaan lukien tasapelit), bannit `entries[type:"ban"]`:sta + nykyisen kierroksen `heroBans`ista, tulokset `entries[type:"result"]`:sta (`winnerSlot`, `team1Score`, `team2Score`), sivut `sideSelections`:sta. Vastaus välimuistiin 1–2 s muistissa (julkinen, kuormitus). Ei salaisuuksia: ei `roleTokens`ia, ei yhteystietoja.

**S2 – Turnaustapahtuma huoneeseen** (lisäys, ei korvaa)
- Uusi socket-viesti `join-tournament {tournamentId}` (julkinen; slug→UUID) → `socket.join("tournament:" + uuid)`.
- Uusi tapahtuma `tournament-event` huoneeseen: `{ tournamentId, version, updatedAt, changed: ["live"|"matches"|"teams"|"standings"], live? }` – `live` mukana kun `changed` sisältää `"live"` (≤ muutama kt), muuten asiakas hakee `/broadcast`in.
- **Vanha `tournament-update` (merkkijono, `io.emit` kaikille) säilyy ennallaan** nettisivuja varten. Toteutus: yksi apufunktio `notifyTournament(id, changed)` joka emittoi molemmat; korvaa/täydentää ~40 `io.emit('tournament-update', …)` -kutsua ([gameState.js:93](../sowdraft/lib/gameState.js:93), `tournamentSystem.js` useita) – oletus `changed = ["matches","teams","standings"]`, `broadcastState` lisää `"live"`.

**S3 – Joukkueen lyhenne ja väri**
- `tournament_teams`: uudet sarakkeet `abbr TEXT`, `color TEXT` (migraatio `initDB.js`: `ALTER TABLE … ADD COLUMN` jos puuttuu).
- Validointi: `abbr` 1–6 merkkiä (trimmattu), `color` `^#[0-9a-fA-F]{6}$` tai tyhjä. Lisää `PATCH /api/tournament-system/:id/teams/:teamId` sallittuihin kenttiin ([tournamentSystem.js:583](../sowdraft/routes/tournamentSystem.js:583)), admin-UI:hin (`tournament-admin.js`) ja valinnaisena ilmoittautumislomakkeelle/kapteenin muokkaukseen.
- Julkinen joukkuedata palauttaa kentät (ei yksityisiin kuuluvia).

**S4 – Tasapelin kirjaus**
- `resolveAndApplyMapResult` ([gameState.js:334](../sowdraft/lib/gameState.js:334)) draw-haara: lisää `match.previousMaps.push({ map, winner: 'draw' })` ennen `selectedMap = null`.
- **Ennen muutosta auditoi `previousMaps`in kuluttajat** (`public/match.js`, `public/stream.html`, `routes/matches.js` admin-muokkaus ~:231–257, `routes/stats.js`, `lib/discord.js`) – `winner: 'draw'` ei saa rikkoa voittolaskentaa tai tilastoja. Jos auditointi paljastaa riskin, jätä S4 pois: uusi `/broadcast` käyttää `summary`a eikä tarvitse `previousMaps`ia; kirjaa päätös tähän.

**S4 – tulos (2026-10-15, SOWDraft-haara `claude/broadcast-api`, commit 4b22831):** auditoinnin perusteella **S4:ää ei sovellettu**. Admin-editori (`public/admin.js`, `PATCH /admin/matches/:matchId`)
lukee `previousMaps`ia `summary`n kanssa indeksi-linjassa ja tulkitsisi `{winner:'draw'}`-rivin joukkue 1:n voitoksi ja laskisi sarjan uudelleen tallennuksessa. `/broadcast` ei tarvitse sitä, koska `live.maps` rakennetaan
`summary`sta, jossa tasapelimappi on aina mukana. Päätös dokumentoitu SOWDraftin README:ssä.

**S5 – tulos (testattu 2026-10-15):** sowdraft.fi on **Cloudflaren** takana. `curl` pyynnöllä `GET /api/public-tournaments`: User-Agent `Python-urllib/3.13` → **403**, mutta `SOWBroadcast/3.0 (+https://suomiow.fi)` ja `Mozilla/5.0 (compatible; SOWBroadcast)` → **200**. Eli pelkkä oma, ei-kirjastomainen UA riittää nykyisellään; erillinen sallintasääntö on **valinnainen varmistus** (Cloudflaren Browser Integrity Check voi tiukentua tai haastaa tietyistä IP-osoitteista). Käytä aina `SOWBroadcast/<versio>` -UA:ta, älä HttpClientin/kirjaston oletusta.

**S5 – CDN/WAF-sallinta**: sallita `User-Agent: SOWBroadcast/*` (konfiguraatio sowdraft.fi:n edessä, ei koodimuutos).

**SOWDraft-puolen testit**: tasapelin jälkeinen `/broadcast` (`maps` sisältää tasapelikortin), Push-mappi (ei pisteitä), auto-pick (`pickedBy:null`), kaksi samaa mappia, `join-tournament` slugilla, `tournament-event` vain oikeaan huoneeseen, vanha `tournament-update` toimii ennallaan nettisivuilla, `/broadcast` ei vuoda tokeneita/yhteystietoja, `version` kasvaa jokaisesta muutoksesta.

### 8.6 OBS

Scenekokoelma: 18 scenea (`In Game`, `InstantReplay`, `Bracket`, `Standings`, `MapDraft`, `Maps`, `Duo SingleCam`, `DuoCam Row`, `TrioCam Row`,
`Team1`, `Team2`, `Starting Soon`, `Be Right Back`, `Thank You for Watching`, kaksi casteria + host NDI-kehykset, erotin).
Jokaisessa scenessä värilähde + overlay + `transition.html` päällimmäisenä; OBS-siirtymä "BGG Wipe". 29 browser sourcea 1920×1080 (Team 2 Roster 1920×1089),
3 `text_gdiplus` joka lukee `C:/SOWBroadcast/Scoreboard/General/{caster1,caster2,host}.txt`, 2 NDI-lähdettä, game capture, webcam, 2 VLC (Music), kuvamaski
`HTML/assets/imagemask/scalecamera.png`. Laitekohtaiset arvot (NDI-koneen nimi, BRIO-polku) on kovakoodattu – uusi tuotanto: tuotettava generoitu/siivottu kokoelma. Kaksi tiedostoa
eroavat vain MapDraft-lähteessä (`draft.html` vs `mapdraft.html` – jälkimmäinen toimii vain Windowsin kirjainkoko-välinpitämättömyydellä).

---

## 9. Overlayt (HTML) – mitä ne tekevät

Kaikki 1920×1080, läpinäkyvä tausta (paitsi MapDraft: musta), lukevat `ROOT=".."`-suhteellisesti. **Yhteinen "stinger"-koukku**:
`?stinger=` tai `?delay=` (ms, oletus 1800) viivästää `body.visible`-luokkaa; `visibilitychange` vaihtaa `visible`/`leaving` (OBS "shutdown when not visible"
käynnistää sisääntuloanimaation uudelleen). Päivitys: SSE-vihje → `fetch`.

| Tiedosto | Scene | Lukee | Päivitys | Huomio |
|---|---|---|---|---|
| `scoreboard.html` | In Game | `Match/T{n}Name\|Score\|Color\|Logo`, `Map1..N.txt`, `CurrentMap`, `status.txt`, `General/first_to`, `Maps/index.json`, `Gametypes/index.json` | SSE | uusi tumma/kulta-ulkoasu; **ei** lue `colors.txt`:ää; "First to N" = `ceil(maps_count/2)`; ban-portretti `Heroes/<slug>.png`; kuolleet koukut `overlay.scoreboard.show/hide` |
| `MapDraft.html` ✔ **säilyy (KYS-10)** | MapDraft (uusi scene osoittaa tähän; kirjainkoko täsmälleen `MapDraft.html`) | `match.json`, `T*`, `colors.txt`, `HeroesBody`, `Maps/<slug>.png` | pollaus 1 s (signatuuri) | nykyisen mapin paljastus + bannattu hero; ei animaatioita |
| `MapDraft_v2.html` | ei käytössä | kuten yllä + `Maps/<slug>.mp4` jos on | pollaus | testifork: video taustana; mp4:ää ei ole |
| ~~`draft.html`~~ | (vanha `SOWBROADCAST.json`) | `maps.txt`, `match.json`, `Map<N>.txt`, `T*` | pollaus 1 s | **EI TUODA uuteen versioon (KYS-10)** – ei käytössä; vain historiaa: sarake per pelitila, lukee olematonta `state.current`, voittajat avaimella mapin nimi |
| `maps.html` | Maps | `Map1..N`, `CurrentMap`, indexit, `OverlayLogo`, `T*Logo` | pollaus 1 s | voittaja päätellään pisteistä; "NEXT MAP" -tabi |
| `team1.html`, `team2.html` | Team1/Team2 | `T{n}P{1..8}*`, `T{n}Name\|Color\|Logo`, `colors.txt`, `HeroesBody`, `Roles` | SSE | identtiset kuuden rivin eroin → yksi parametrisoitu sivu |
| `duorow.html`, `triorow.html`, `singlecam.html` | kamerarivit | `T*`, `caster1\|2`, `host`, `status`, `colors` | SSE | 6 px `--tertiary`-kehykset; `Nimi @handle` jäsennetään; singlecamin `upFooter` kuollutta koodia |
| `bracket.html` | Bracket | `Bracket/bracket.json`, `colors.txt` | SSE | CSS-grid, elbow-liittimet DOM:sta, automaattinen skaalaus 0,65–1,4, flash muuttuneille; värit käänteisesti (primary=kortti, secondary=teksti) |
| `standings.html` | Standings | `Standings/standings.json`, `colors.txt` | SSE | skaalaa viewportiin; sarakkeet vaihtuvat `played`-kentän mukaan; vyöhykevärit qualified/eliminated |
| `replay.html` | InstantReplay | `Replay/replaypath.txt` → `Replay/Playlist/<tiedosto>`, logo | pollaus 1 s (SSE-`replay` ohitetaan) | kaksoispuskuri toimii huonosti; video `loop` |
| `startingsoon.html`, `berightback.html`, `thankyouforwatching.html` | odotusscenet | `Waiting/*`, `matchtext`, `socials.json`, `OverlayLogo`, `Icons/*.svg`, `colors` | SSE + 1 s `upTimer` | ~95 % identtiset → yksi mallipohja; Rajdhani Google Fontsista (vaatii netin) |
| `transition.html` | stinger | `colors.txt` (septenary/octonary), `TransitionLogo.png` | vain lataus | kaksi vinoa wipe-kerrosta + logo; ajoitus viritetty 1800 ms oletukselle |

**Yhteiset JS-apurit** (kopioitu ~15 tiedostoon): `read/readJSON/ok`, `applyColors` (regex `^\s*([^=#]+)\s*=\s*(.+?)\s*$`), `slug/nameToSlug`, `hex/hexToRgb`,
`fitTeamName/fitCaptionText/fitBigText` (odottavat `document.fonts.load`), `startSSE`, signatuuri → renderöi vain muutoksesta.
CSS-muuttujat: `--primary #0f1114`, `--secondary #fff`, `--tertiary #3a3a90`, `--quaternary #006ea1`, `--t1`/`--t2` (oletus `#27AAE1`/`#C80013` – **eri** kuin sovelluksen oletukset).

**Linjaus (päätetty: KYS-4 ja KYS-6)**:
- **Ulkoasua ei päivitetä.** Overlayjen värit, fontit (Rajdhani), asettelut ja animaatiot pysyvät ennallaan, myös `scoreboard.html`:n kulta/tumma-ilme.
  Ne eivät ole brändiohjeistuksen alaisia. Älä "korjaa" niitä brändin mukaisiksi.
- **Datankäsittely siirretään uuteen kontraktiin.** Koska siirrytään uuteen tilarajapintaan (EHD-2) ja scenet päivitetään samalla, overlayjen datankäsittely
  (kopioidut `read/ok/readJSON`-apurit, SSE-avainsanasto, pollaus) korvataan yhdellä jaetulla moduulilla joka hakee `GET /api/state` ja kuuntelee uutta SSE:tä.
  Tämä on **sallittu ja odotettu** refaktorointi (EHD-11, rajattuna: ei ulkoasumuutoksia). Samalla yhdistettävät kaksoiskappaleet: waiting ×3 → 1,
  team1/2 → 1. **Draft-overlay (KYS-10, päätetty): vain `MapDraft.html` säilyy.** `draft.html` on eri tiedosto eikä ole enää käytössä → **ei tuoda uuteen versioon**
  (ei migraatiota, ei OBS-scenessä; poista myös sen yksinomaan käyttämä `Match/maps.txt` tiedostokontraktista). `MapDraft_v2.html` on käyttämätön testifork: sitä ei tuoda sellaisenaan, mutta sen **video-taustatuki yhdistetään valinnaisena ominaisuutena `MapDraft.html`:ään** (KYS-14, luku 15).
- Fontit paikallisina (waiting-overlayt hakevat nyt Rajdhanin Google Fontsista → ei toimi ilman nettiä).
- Visuaalinen regressiotesti: ennen/jälkeen-kuvakaappaus jokaisesta overlaysta (1920×1080) samalla tilalla; ero sallittu vain jos animaatiotiming-poikkeama.
- Kontrolliohjelma riippuu overlayjen sisäisestä toteutuksesta vain `/api/state`-skeeman ja SSE:n kautta (luku 7).

---

## 10. Taustaprosessit

- **Replay-vahtija** (`_replay_watcher`, :4220): säie, pollaa 0,5 s `Scoreboard/Replay/Replay Replay.mp4` (OBS:n replay buffer tallentaa tähän –
  OBS-konfiguraatio, ei repossa). Vakaa kun `(mtime,size)` sama 3 kertaa ja eri kuin viimeksi kopioitu → kopioi `Replay/Playlist/{n}-Replay-Replay.mp4`
  (n = suurin numeroetuliite + 1), kirjoita `replaypath.txt` (vain tiedostonimi), notify `["replay"]`. Käynnistyksessä olemassa oleva tiedosto kopioituu uudelleen
  (bugi – kopioi vain jos uusi). Playlistia ei koskaan siivota.
- **Waiting-videoiden synkka** (`_export_waiting`, :3629): ks. luku 6.
- **Timer**: luku 4.4.
- **Autosave**: joka Update + sulkeminen (`closeEvent` katkaisee linkit ja tallentaa).

---

## 11. Ulkoasu – Suomi OW ry:n graafinen ohjeistus

Lähde: `suomiow nettisivut/docs/brand/graafinen-ohjeistus.pdf` (25 sivua, v1.0, lokakuu 2026). **Sovellus on sisäinen työkalu**, joten
ohjeistusta sovelletaan sen hengessä; kohdat joissa tulkinta on omani on merkitty **(tulkinta)**.

### 11.1 Väripaletti (s. 10, 23)

| Token | Hex | Nimi | Rooli |
|---|---|---|---|
| `--sow-night` | `#001445` | Yönsininen | tumma pohja (pääväri, tumma) |
| `--sow-lion` | `#134380` | Leijonansininen | pääväri; rakenne, valinnat, ensisijainen täyttö |
| `--sow-crown` | `#1E8AB9` | Kruununsininen | tukiväri; hover, gradientin yläpää |
| `--sow-shadow` | `#0F356B` | Varjonsininen | tukiväri; kortit/paneelit, gradientin alapää |
| `--sow-blade` | `#23C5D7` | Teräsyaani | **korostus, säästeliäästi** |
| `--sow-frost` | `#A9B8CC` | Huurre | toissijainen teksti tummalla |
| `--sow-ice` | `#F2F6FA` | Jäänvalkoinen | vaalea pohja |
| – | `#FFFFFF` | Valkoinen | ensisijainen teksti tummalla |
| – | `#5A6B82` | Harmaansininen | toissijainen teksti vaalealla |
| Liukuväri | `#1E8AB9 → #0F356B` | | **aina ylhäältä alas**, ei vaakaan/säteittäin/käänteisenä |

**Käyttösuhteet (s. 11)**: 55 % pohja (valkoinen/jäänvalkoinen/yönsininen) · 25 % Leijonansininen · 15 % tukivärit · **5 % Teräsyaani**:
vain viivat, LIVE-merkki, tärkein numero tai linkki. "Se erottuu, koska sitä on vähän." Teräsyaani ei koskaan pohjaväriksi.
Tarkistuslistan kohta: *"Vain paletin värit"*, *"Teräsyaania alle 5 %"*, *"Enintään kaksi elementtiä"* (s. 25).

**Kontrastit** (WCAG AA; laskettu uudelleen, täsmää ohjeistuksen s. 12 lukuihin):

| teksti → tausta | suhde | käyttö |
|---|---|---|
| valkoinen / yönsininen | 17,7:1 | kaikki tekstit |
| valkoinen / varjonsininen | 12,1:1 | tekstit korteilla |
| valkoinen / leijonansininen | 9,8:1 | napit |
| huurre / yönsininen | 8,8:1 | toissijainen |
| huurre / varjonsininen | 6,0:1 | toissijainen korteilla |
| teräsyaani / yönsininen | 8,5:1 | linkit, aksentit |
| teräsyaani / varjonsininen | 5,8:1 | linkit korteilla |
| yönsininen / teräsyaani | 8,5:1 | **painiketeksti teräsyaanilla** |
| harmaansininen / valkoinen | 5,4:1 | toissijainen vaalealla |
| valkoinen / kruununsininen | **3,9:1** | **vain iso teksti (24 px+)** |
| teräsyaani / valkoinen | **2,1:1** | **kielletty tekstinä** |
| leijonansininen / yönsininen | **1,8:1** | **kielletty** (ei tekstiä leijonansinisellä tummalla pohjalla) |

Ohjeistuksen oma tumman teeman resepti (s. 13): **pohja `#001445` · kortit `#0F356B` · teksti `#FFFFFF` · toissijainen `#A9B8CC` · painike ja linkki `#23C5D7`**.

### 11.2 Typografia (s. 14–15)

| Rooli | Fontti | Painot | Huom |
|---|---|---|---|
| Otsikot | **Saira Semi Condensed** | Bold 700 / ExtraBold 800 | H1 versaalein, yksi per julkaisu |
| Leipäteksti | **Inter** | Regular 400 / SemiBold 600 | tekstin tasaus vasemmalle, riviväli 1,5 |
| Tulokset ja nostot | **Russo One** | vain yksi paino | **max 4 sanaa**, esim. `LIVE`, `FINAALI`, `3–1` |
| Varafontit | Arial Narrow Bold / Arial | | jos fontteja ei ole; "ei muita fontteja" |

Ohjeistuksen kokoasteikko (verkko / some / A3): H1 Saira 800 = 48 / 110 / 100 pt · H2 Saira 700 = 32 / 64 / 48 · H3 Saira 700 = 24 / 44 / 32 ·
pikkuotsikko 15 / 28 / 18 · leipä Inter 400 = 17 / 32 / 13 · kuvateksti 14 / 24 / 9 · nosto Russo One = 64 / 120 / 120.
**Sovellusasteikko (tulkinta, tiheä työkalu-UI)**: sivuotsikko Saira 800 versaalein 24–28 px · osio-otsikko Saira 700 versaalein 15–16 px ·
leipä Inter 400 13–14 px · kenttälabel Inter 600 12–13 px · numerot/ajastin Russo One 36–64 px.
**Fonttitiedostot**: repossa on nyt vain *latin*-osajoukko WOFF2:sta muunnettuna (`HTML/fonts/{Saira,Inter,RussoOne}/*.ttf`). Uuteen projektiin
hae **täydet** TTF:t (Google Fonts, OFL) – ä/ö/å tarvitaan, ja `latin-ext` kattaa erikoisnimet (Torbjörn, Lúcio, Esperança). Lisenssit mukaan.

### 11.3 Logo ja ikoni (s. 3–9)

Viisi versiota: **Päälogo** (oletus), **Pitkä nimi** (SM-kisat, kumppanit, media), **CUPS** (vain kuukausiturnaukset), **Tunnus** (pienet tilat, striimin kulma, sovellusikoni), **Maskotti** (vain vaalealla).
Lähdetiedostot: `suomiow nettisivut/src/assets/brand/suomiow_{paalogo,pitka-nimi,cups,tunnus,maskotti}.png` (3875×3875 RGBA) + `suomiow_profiili_*.png`.
Säännöt: logo on **aina sininen** (ei tapahtuma-/kumppaniväriä), aina suorassa, ei venytystä (Shift pohjassa), ei varjoa/hehkua/3D:tä/lisäreunuksia,
ei himmennystä (100 % peittävyys), vain PNG (ei JPG – ei valkoista laatikkoa). **Suoja-alue X = 10 % logon näkyvästä leveydestä**
(ympyräversiot 5 % halkaisijasta; PNG:ssä on jo 6–10 % läpinäkyvää reunaa). **Minimikoot** (100 % näkymä): päälogo 120 px, pitkä nimi 180 px, CUPS 120 px,
tunnus 32 px, maskotti 160 px. Ei tilaa → vaihda **tunnukseen**, älä pienennä päälogoa. Oikeat taustat: valkoinen, jäänvalkoinen, yönsininen, liukuväri.
Väärät: teräsyaani, kirkkaat värit, kirjava kuva, maskotti sinisellä.
**Sovelluksessa**: ikkuna-/tehtäväpalkki-ikoni = **Tunnus** (ikoni 32 ja 180 px; tee ICO usealla koolla – 16 px sivustoikoni puuttuu ohjeistuksen s. 26 mukaan, tilattava logon tekijältä);
yläpalkki/About/splash = Päälogo yönsinisellä pohjalla; pieni tila → Tunnus.

### 11.4 Elementit (s. 16–17)

- **Teräviiva** (hammastettu viiva): korkeus 8–26 px 1080 px leveässä kuvassa, hampaan leveys = 2 × viivan korkeus; otsikon alla/erottimena;
  ei valkoisella pohjalla, ei kehyksenä. → Sovelluksessa: ohut erotinviiva sivun otsikon alla, **säästeliäästi** (se on Teräsyaanin 5 % -budjettia).
- **Liukuväripinta**: aina ylhäältä alas `#1E8AB9→#0F356B`; isot otsikkopalkit, splash, About; iso teksti ylös, pieni alas.
- **Tarrareuna** (valkoinen 1 % + yönsininen 1,3 %) ja **harjareuna** (4–7 piikkiä, syvyys 10–20 %) ovat markkinointigrafiikkaa (julisteet, nimilaput).
  Työkalu-UI:ssa ei käytetä; mahdollisesti splash/About, **enintään yksi** per näkymä. **(tulkinta)**

### 11.5 Design-tokenit sovellukselle **(tulkinta ohjeistuksesta)**

| Käyttö | Arvo |
|---|---|
| Ikkunan tausta | `#001445` |
| Kortti / ryhmälaatikko / paneeli / syötekenttä | `#0F356B` |
| Teksti | `#FFFFFF`; toissijainen `#A9B8CC` |
| Reunaviiva | `#A9B8CC` ~22–35 % alfa (sama väri, ei uutta) |
| Ensisijainen toiminto (yksi per näkymä, esim. *Update*) | täyttö `#23C5D7`, teksti `#001445` (8,5:1) |
| Toissijainen painike | täyttö `#134380`, teksti `#FFFFFF` (9,8:1), hover `#1E8AB9` + **lihavoitu/iso** teksti tai kirkastettu `#134380` (valk./`#1E8AB9` on vain 3,9:1) |
| Fokus | **2 px `#23C5D7`** (ohjeistus s. 23: "Kohdistus: 2 px Teräsyaani") |
| Valittu välilehti / otsikkoliuska | `#134380` |
| Disabled | teksti `#A9B8CC` ~45 % alfa, tausta `#0F356B` |
| Ajastin / tärkein numero | Russo One, teräsyaani-täyttö + yönsininen teksti (ainoa isompi teräsyaani-pinta) |
| Tooltip | tausta `#0F356B`, 1 px reuna `#23C5D7` |
| Kulmapyöristys | 4–6 px (ohjeistus ei määrää) |
| Skrollipalkki | raide `#001445`, kahva `#A9B8CC` ~35 %, hover `#1E8AB9` |

**Toiminnalliset poikkeukset (HYVÄKSYTTY, KYS-3)**: ohjeistuksessa ei ole onnistumis-/virheväriä. Kolme operaattorille kriittistä tilaa
tarvitsevat universaalin värin: **Start = vihreä, Pause = tumma liuskekivi, Reset = neutraali harmaa** (vanhan sovelluksen ratkaisu),
sekä virheilmoitus (punainen). Standings-overlayn qualified/eliminated-värit (vihreä/punainen) ovat vastaava poikkeus.

### 11.6 Opit PyQt-version teemoituksesta (älä toista)

Vanhaan sovellukseen tehtiin keskeneräinen (committaamaton, `git status`) teema globaalilla QSS:llä. Havainnot, jotka koskevat mitä tahansa toolkitia:
1. Hajanaiset paikalliset tyylit (18 `setStyleSheet`-kutsua kovakoodatuilla heksoilla) → epäyhtenäinen ulkoasu. Käytä **yhtä token-taulukkoa** ja tyylitä komponenttitasolla.
2. Pitkät välilehdet **tarvitsevat vierityskehyksen** – ilman sitä rivit puristuvat ja napit menevät päällekkäin (General/Waiting).
3. Paikallisen tyylin asettaminen *esi-isä*-widgetille rikkoi lapsinappien perityn tyylin (napit muuttuivat läpinäkyviksi). Vältä "tyylin overridea" vanhemmilla; käytä scope-rajattuja resursseja/teemaa (juuri syy suositella XAML/QML-mallia).
4. Vierityskehyksen viewport maalaa oletuspohjavärin (valkoinen raidan sliveri) – aseta pohja eksplisiittisesti.
5. Haettava combo ja natiivit tiedosto-/värivalintadialogit eivät perineet teemaa (Windows-OS-dialogit) – hyväksyttävä rajoitus, mutta oma värivalitsin (palettiin sidottu + vapaa heksa) olisi parempi **[EHD-12]**.

---

## 12. Vanhan sovelluksen virheet ja oudot – **älä kopioi**

1. Kaksi palvelinta samaan porttiin (SOWServer.exe + säie GUI:ssa); SSE-hub moduulikohtainen → epädeterministinen. Jos portti varattu, GUI kaatuu käynnistyksessä (ei try/exceptiä).
2. SSE toimittaa vain **viimeisen** payloadin (ei jonoa) → nopeat päivitykset hukkaavat avaimia; uusi asiakas saa heti vanhan viestin. `Content-Length`-int-parsinta ilman suojaa, rungolla ei kokorajaa.
3. Koko juurikansio tarjolla (myös `autosave.json` henkilötietoineen); `POST /notify` ilman autentikointia (mikä tahansa selainsivu voi lähettää).
4. `first_to.txt` sisältää `maps_count`in; `general.first_to` laahaa yhden `collect`in jäljessä; `first_to`/`maps_count`/`format_type` menevät sekaisin; `current_map` johdettu, `winner` johdettu vain pisteistä.
5. Match-välilehden muutokset eivät ole liveinä; **Reset, Swap Teams, Team import, File→Load** vain autosaavat eivätkä päivitä overlayita.
   **Päätös (KYS-9, vahvistettu): vain ja ainoastaan Update päivittää overlayn.** Reset, Swap Teams, Team import ja File→Load eivät lähetä – tämä on tarkoituksellinen
   ominaisuus, ei bugi. Se *koskee myös* Bracketin ja Standingsin jokaista näppäilyä/muutosta, jotka vanhassa laukaisevat koko Updaten (bugi #6): uudessa ne eivät lähetä mitään
   ennen Update-painiketta. Avoimet poikkeukset (linkit, ajastin, replay) luvussa 15.
6. Bracketin jokainen näppäily (Title/Stage/tiimin nimi) laukaisee koko Updaten (kaikki viennit + autosave).
7. `refresh_heroes` laukaisee hero-changed ja **ylikirjoittaa käsin valitun roolin** oletuksella.
8. `_apply_state` käynnistää signaaleja kesken latauksen → `_update_waiting_only` autosaa puolivalmiin tilan; tuntemattomat avaimet (`Team(**d)`, `WaitingSettings(**d)`) kaatavat latauksen → tallennukset versioherkkiä.
9. `_update_general_only`/`_update_waiting_only` korvaavat `lastState`n kehyksellä → seuraava täysi Update raportoi vääriä muutoksia.
10. Asset-muutosten diff ei havaitse roolin/body-kuvan/kuvan korvaamisen muutosta (vain nimi/image_path/mode); vienti vasta seuraavalla Updatella (paitsi Delete).
11. Vanhentuneet tuotokset: `T1Logo.png`/`T2Logo.png`/General-logot jäävät kun logo tyhjennetään.
12. Turnauslogojen lataus synkronisesti UI-säikeessä (jäädyttää), ei kokorajaa/sisältötarkistusta, välimuistia ei siivota; yksi uudelleenyritys 4 s; koko turnaus haetaan joka `tournament-update`ssa.
13. Draft-linkki: puolet vain nimivertailulla – ei täsmää → hiljainen ohitus; `disconnect` vs. auto-reconnect -kilpa; linkki ei yhdistä uudelleen käynnistyksessä.
14. Replay-vahtija kopioi olemassa olevan tiedoston joka käynnistyksessä; playlist ei siivoudu.
15. Kovakoodatut Windows-polut: `C:\Suomi OW koodiprojektit\...` (DEV_ASSET_DIRS, :189), `C:\SOWBroadcast` (scene-JSONit, asennin), `Replay\\Playlist` kirjaimellinen backslash, tallennettu absoluuttisia `C:\...` logo/source-polkuja JSONissa; `taskkill /IM SOWServer.exe` tappaa kaikki samannimiset.
16. `slugify` palauttaa `"item"` ei-latinalaisille nimille; kaksoistiedostot (`antartic-peninsula.png`, `lucio.png`/`l-cio.png`…); `Heroes/d-va.png` puuttuu vaikka indexissä; hero-kuvat Title_Case vs. slug -epäyhtenäisyys.
17. Ajastin 1 s `QTimer` ilman ajautumiskorjausta; "adjust" pysäytettynä muuttaa presetin; lopputeksti oletuksessa ilman `!`, resetissä `!`:llä; "Reset this tab" ei tyhjennä sosiaalisia.
18. Kuollut koodi/kentät: `Team.banned_hero`, `GeneralSettings.format_type/division`, `map_pool`, `Scoreboard/Roles` (itse asiassa käytössä overlayissa), `_export_scoreboard`, `seriestext.txt`, `__last_gui_touch.txt`, `videos_dir.txt`, `T*Abbr/Players.txt`, `quinary/senary`.
19. Suomi/englanti sekaisin UI:ssa ja virheviesteissä; ei lokitusta (`print` + lokit mykistetty), ei testejä, ei CI:tä, ei konfiguraatiotiedostoa.
20. Asennin: versio vain `setup.iss`:ssä (2.3.0), ei näy sovelluksessa; `install.iss` vanhentunut (2.1, eri AppId, kovakoodatut polut); 266 Mt (kuvat+musiikki; musiikin lisenssi epäselvä); `PrivilegesRequired=lowest` + `C:\SOWBroadcast` voi epäonnistua.
22. **Draft-linkki katoaa tasapelin kohdalla**: SOWDraft ei kirjaa tasapeliä `previousMaps`iin (`gameState.js:358`), ja vanha koodi rakentaa mapit `previousMaps`ista + `selectedMap`ista → tasapelimappi ja sen bannit puuttuvat. Uusi rakentaa `summary`sta (luku 8.4).
23. Draft-linkki tuo bannit vain nykyiselle mapille eikä tyhjennä vanhoja; puolet päätellään nimestä (8.4: slotit ID/swap-kytkimellä).
21. `D.Va` / `Soldier: 76` -tiedostonimet vaativat ylikirjoitustaulun; `Torbjörn.png`-tiedostonimi sisältää ei-ASCII:ta.

---

## 13. Rakennusvaiheet ja hyväksyntätestit

### Vaiheet

| Vaihe | Sisältö | Valmis kun |
|---|---|---|
| M0 | Projektirunko, token-teema (luku 11.5), ikkuna, valikot, fontit, ikoni, `schemaVersion`-tila + autosave/load (atomiset) | tyhjä ikkuna näyttää brändin mukaiselta; vanhat `autosave.json` latautuvat |
| M1 | Palvelin + SSE + notify-jono; Exporter; golden-testit | vanhat overlayt toimivat uudella palvelimella ilman muutoksia |
| M2 | Assetit: index.json-lataus, haettava combo, AssetManager, BulkImport | kaikki 54 heroa/31 mappia/5 pelitilaa listautuvat, haku toimii |
| M3 | MATCH-välilehti kokonaan (TeamPanel, MapCard, Swap/Reset/Update, team import/export) | luvun 4.2 käyttäytyminen + `Scoreboard/Match/**` vastaa oraakkelia |
| M4 | GENERAL + WAITING (timer, socials, playlist-synkka) | exportit vastaavat, ajastin overlayssa oikein |
| M5 | STANDINGS + BRACKET (templatet, tiimilista, lukitus) | `standings.json`/`bracket.json` vastaavat |
| M6 | SOWDraft-linkitys luvun **8.4** mukaan (`IDraftSource`: `BroadcastApiSource` + `LegacySource`), turnaus ensin, Match-valitsin, swap-kytkin, ristiriitasääntö | mock-palvelimella ja oikealla `esm26`-turnauksella; toimii sekä uudella `/broadcast`-rajapinnalla että vanhalla SOWDraftilla |
| M6b | **SOWDraft-muutokset S1–S5** (luku 8.5; erillinen työ `sowdraft`-repossa, tehtävä ennen kuin `BroadcastApiSource` voidaan testata livenä) | SOWDraft-puolen testit vihreinä; vanhat nettisivut toimivat ennallaan |
| M7 | Replay-vahtija, asennin (per-käyttäjä), versionäyttö, lokitus | puhdas asennus Windows 11:lle toimii OBS:n kanssa |
| M8 | **Siirtymä uuteen kontraktiin (KYS-6)**: overlayjen datakerros → `/api/state`+SSE (ulkoasu ennallaan, visuaalinen regressiotesti), uudet/siivotut OBS-scenet, vanhan tiedostokontraktin poisto | overlayt näyttävät ennallaan uudella sovelluksella; julkaistaan yhtenä pakettina |
| M9 | Muut hyväksytyt **[EHD]**-parannukset omistajan järjestyksessä | erikseen sovittu |

### Hyväksyntätestit (golden)

1. **Export-pariteetti**: syötä vanhaan ja uuteen sama synteettinen tila (kaksi joukkuetta, 8 pelaajaa, 7 mappia vaihtelevilla tiloilla,
   3 ryhmän standings, 8-tiimin double-elim bracket, odotusruudun tekstit/socials). Vertaa `Scoreboard/**` tiedosto tiedostolta (ero sallittu vain `updated_at.txt`).
2. **Diff-avaimet**: tee sarja muutoksia (pistemuutos, pick, ban, väri, logo, roolimuutos…) ja vertaa `changed`-listoja.
3. **SSE**: 100 nopeaa notifyä → asiakas saa kaikki avaimet (uusi) / vähintään viimeisen (vanha); keepalive 15 s; reconnect Last-Event-ID:llä.
4. **Slug/heroBodyFile**: taulukkotesti (`Lúcio, D.Va, Soldier: 76, Torbjörn, Wrecking Ball, Junker Queen, Jetpack Cat, King's Row`).
5. **Socials-normalisointi**: `https://www.twitch.tv/SuomiOW?x=1`, `@handle`, `//a/b`, `discord.gg/abc`.
6. **Timer**: adjust/start/pause/reset/tick-sekvenssit (luku 4.4).
7. **Standings-lajittelu** ja turnauslinkin kuvaus: kiinteä JSON-fikstuuri → odotettu `standings.json`/`bracket.json`.
8. **Draft-kuvaus**: fikstuurit (suora/vaihdettu puoli, nimi ei täsmää, tasapeli, kaksi samaa mappia).
9. **Lataus**: vanha `autosave.json` + tuntemattomia avaimia ei kaada; `autosave.json` kirjoitus ei jää rikki kesken (kill -9 -testi).
10. **UI**: pienin ikkuna 1200×750 – mikään välilehti ei leikkaudu; fokus-rengas 2 px `#23C5D7`; tab-navigointi; kontrastit luvun 11.1 mukaan.

---

## 14. Ehdotetut parannukset ja uudet ominaisuudet (odottavat omistajan hyväksyntää)

Prioriteetti: **A** = korjaa todellisen ongelman, **B** = selvä hyöty, **C** = mukava lisä.

### Päätöstila (omistaja, 2026-10-15)

| Tila | EHD |
|---|---|
| ✔ **Hyväksytty** | **1** (yksi prosessi/palvelin, token), **2** (uusi tilarajapinta), **4** (automaatiot), **5** (linkit yhdistyvät uudelleen), **6** (OBS WebSocket), **7** (pikanäppäimet + paikallinen ohjaus), **8** (joukkuekirjasto), **9** (ajastin `endsAt`), **11** (overlayjen datakerros + duplikaatit; ei ulkoasumuutoksia), **12** (oma värivalitsin), **13** (overlay-esikatselu), **14** (UI englanniksi), **15** (perusinfra + päivitystarkistus), **16** (asennin), **17** (undo/redo), **18** (otteluprofiilit) |
| ✘ **Hylätty** | **19** (saavutettavuuspaketti: ei erillistä työtä; brändin 2 px fokus-rengas ja perus-näppäimistökäyttö kuuluvat silti tavalliseen laatuun) |
| ◐ **Ehdollinen** | **20** (monialustaisuus: vain jos ei vaadi merkittävästi resursseja → *best effort*: Avalonia/.NET-koodi pidetään siirrettävänä missä se on ilmaista (`Path.Combine`, ei turhia Win32-kutsuja), mutta **vain Windows on tuettu ja testattu**; ei macOS/Linux-buildeja, -asentimia eikä -testejä) |
| ✔ **Hyväksytty (rajattuna)** | **3** (vain "unsent changes" -merkki, ei Auto-updatea; KYS-9:n mukainen), **10** (asset-manifesti: yksi tiedosto jossa jokaisen sankarin/mapin/pelitilan nimi, slug, rooli/pelitila, kuvatiedostot ja aliakset; poistaa kovakoodatut `HERO_ROLES`, `_HERO_BODY_OVERRIDES` ja nimi-fuzzy-matchit; valinnainen päivitys GitHub Releasesista) |

**Kaikki EHD-ehdotukset on nyt ratkaistu.** Toteutusjärjestys M-vaiheissa (luku 13); M9 kattaa hyväksytyt parannukset omistajan priorisoimassa järjestyksessä.

| # | Ehdotus | Pri | Miksi |
|---|---|---|---|
| **EHD-1** ✔ | **Yksi prosessi, yksi sisäänrakennettu palvelin**; konfiguroitava portti + varaportti; vain localhost; `/notify` tokenilla; staattiset vain sallituista kansioista | A | luku 12 #1–3. Poistaa `SOWServer.exe`, `launch.bat`-koreografian ja `taskkill`-kikat |
| **EHD-2** ✔ | **Yksi tilarajapinta** (**HYVÄKSYTTY, KYS-6**): `GET /api/state` (versioitu) + SSE joka kantaa **tilapäivityksen** (versio + muutosavaimet) ja tukee `Last-Event-ID`; litteät txt-tiedostot jäävät vain OBS-tekstilähteille (caster/host) ja taaksepäin | A/B | nyt sama tila vientinä 4 muodossa; pollaavat overlayt tekevät kymmeniä pyyntöjä/s |
| **EHD-3** ✔ (uudelleenrajattu KYS-9:llä) | **"Unsent changes" -merkki**: näkyvä merkki (ja muuttuneet kentät korostettuna) kunnes Update painetaan; sääntö "vain Update päivittää overlayn" säilyy. **Ei** Auto-update-kytkintä, ei Reset/Swap/Load/Import-pushia. Asset-muutokset (lisäys/poisto/rooli/kuva) kirjoitetaan levylle heti, mutta overlayihin ne näkyvät vasta Updatella (ei SSE:tä ennen sitä) | A | luku 12 #10; tuottaja ei tiedä mikä on lähetetty |
| **EHD-4** ✔ | **Automaatiot (valinnaisia kytkimiä)**: joukkueen pistemäärä mapeista; `completed` kun pisteet asetettu; **bracketin voittajan eteenpäinvienti**; "seuraava map" automaattisesti | B | nyt kaikki käsin → virheitä live-tilanteessa |
| **EHD-5** ✔ (osa luvun 8.4 mallia) | Linkit **yhdistyvät uudelleen käynnistyksessä** (valinnainen); tilarivi + "viimeksi päivitetty" -aikaleima; varoitus jos nimet eivät täsmää | B | nyt hiljainen ohitus |
| **EHD-6** ✔ | **OBS WebSocket (v5) -integraatio**: scenenvaihto sovelluksesta, replay buffer -tallennus napista, "Go live" -tarkistuslista (palvelin, scenet, lähteet), scenekokoelman generointi ilman laitekohtaisia kovakoodauksia | B | korvaa käsin-konfiguroinnin (replay-polku, NDI/BRIO-nimet) |
| **EHD-7** ✔ | **Pikanäppäimet + paikallinen HTTP/WebSocket-ohjaus** (Stream Deck / Companion): pisteet ±1, swap, seuraava map, timer start/pause | B | live-tuotannossa hiiri on hidas |
| **EHD-8** ✔ | **Joukkuekirjasto** tiedostoviennin/-tuonnin tilalle (nimi, abbr, väri, logo, pelaajat; haku; SOWDraftin joukkueiden tuonti) + logot kopioidaan sovelluksen omaan kirjastoon (ei absoluuttisia polkuja, ei vanhentuneita PNG:itä) | B | nyt `*.sowteam.json` + käsin valittu logo joka kerta |
| **EHD-9** ✔ | **Ajastin loppuaikaleimalla** (`endsAt`, monotoninen kello) ja overlay laskee itse; ei kirjoituksia/notifyja kerran sekunnissa; ei ajautumista | A/B | luku 12 #17; sama tapahtumamäärä pienenee ~60× |
| **EHD-10** ✔ | **Asset-manifesti** (hero: slug, rooli, vartalokuva, aliakset; map: slug, mode, aliakset kuten `King's Row`; mode): poistaa kovakoodatut `HERO_ROLES`, `_HERO_BODY_OVERRIDES`, nimi-fuzzy-matchit ja kaksoistiedostot; uusien herojen/mappien päivitys yhdellä manifestilla (esim. julkaistu GitHub Releasesissa) | B | uusia heroja tulee jatkuvasti (Doctorine ym.) |
| **EHD-11** ✔ | **Overlay-refaktorointi**: yksi jaettu JS-moduuli (fetch, värit, fit-text, stinger, SSE + pollausvaraus), parametrisoidut mallipohjat (waiting ×3 → 1, roster ×2 → 1, draft ×3 → 1 `?mode=`), paikalliset fontit (ei Google Fontsia). **Rajaus (KYS-4/6): vain datakerros ja duplikaatit; ulkoasua EI muuteta, ei brändiä** | B (osin päätetty) | ~15 tiedostoa kopioitua koodia |
| **EHD-12** ✔ | Oma **värivalitsin** (brändipaletti pikavalintoina + vapaa heksa + "viimeksi käytetyt"); joukkuevärin kontrastivaroitus overlayn tekstiä vasten | C | natiivi OS-dialogi ei teemaudu eikä tunne brändiä |
| **EHD-13** ✔ | **Overlay-esikatselu** sovelluksessa tai yhdellä napilla selaimeen (ei WebView:tä UI:ssa: avaa oletusselaimeen `?preview`) | C | nyt ei näe mitä lähtee ulos ilman OBS:ää |
| **EHD-14** ✔ | **Kieli**: UI yksinomaan englanniksi, ei sekakieltä; tekstit yhdessä resurssitiedostossa | B | **PÄÄTETTY (KYS-5)**, luku 8.0 |
| **EHD-15** ✔ | **Perusinfra**: lokitus (tiedosto + näkymä), yksikkötestit, CI, `schemaVersion` + migraatiot, tuntemattomien avainten sietäminen, kaatumisraportti, About-ikkuna (versio), **päivitystarkistus** GitHub Releasesista | A/B | nyt ei testejä, ei lokeja, versio vain asentimessa |
| **EHD-16** ✔ | **Asennin**: per-käyttäjä asennus + data `%LOCALAPPDATA%`/käyttäjän valitsemaan kansioon (ei `C:\SOWBroadcast` kovakoodattuna), musiikki erilliseksi valinnaiseksi paketiksi (lisenssi!), pienempi koko | B | 266 Mt; oikeusongelmat |
| **EHD-17** ✔ | **Undo/Redo** ja "palauta edellinen ottelu" (autosave-historia) | C | live-virheen korjaus |
| **EHD-18** ✔ | **Moniottelu-/turnausprofiili**: useita tallennettuja otteluita/turnausasetuksia (nimi, värit, status, logot) ja nopea vaihto | C | nyt yksi autosave + manuaaliset tallennukset |
| **EHD-19** ✘ | **Saavutettavuus & käytettävyys**: täysi näppäimistönavigointi, fokusjärjestys, korkea kontrasti, skaalaus 100–200 %, high-DPI | B | "moderni sovellus" |
| EHD-20 ◐ | Monialustaisuus – **vain best effort** (ks. päätöstila) | – | KYS-2: vain Windows tuettu; EHD-20 hyväksytty ehdolla "jos ei vaadi liikaa resursseja" |

---

## 15. Omistajan päätökset ja avoimet kysymykset

### Päätetty (2026-10-15)

| # | Päätös | Seuraus |
|---|---|---|
| KYS-1 | **C# + Avalonia UI** | luku 2b; PySide6/QML hylätty |
| KYS-2 | **Vain Windows** | EHD-20 (monialustaisuus) **pudotettu**; Windows-spesifit (`FileSystemWatcher`, Velopack/Inno, `%LOCALAPPDATA%`) sallittu. Silti ei kovakoodattuja polkuja |
| KYS-3 | **Toiminnalliset värit hyväksytty** | Start vihreä, Pause liuskekivi, Reset harmaa, virhe punainen, qualified/eliminated: sallitut brändin ulkopuoliset poikkeukset (11.5) |
| KYS-4 | **Overlayjen ulkoasua ei päivitetä** | kulta/tumma `scoreboard.html` ja muut ennallaan; EHD-11:n brändiosa **hylätty** |
| KYS-5 | **UI kokonaan englanniksi** | EHD-14 ratkaistu; luku 8.0 |
| KYS-6 | **Siirrytään uuteen kontraktiin** | EHD-2 **hyväksytty** (uusi `/api/state` + SSE); OBS-scenet ja overlayjen datakerros päivitetään yhtenä pakettina (luku 0 kohta 3, luku 9) |
| KYS-7 | **`Music/` jaettavissa asentimessa** | EHD-16:n musiikkiosa ei pakollinen; pidä mukana (voi silti olla valinnainen komponentti koon vuoksi) |
| KYS-9 + KYS-11 | **Käyttäjän muokkaus päivittää overlayn vain Updatella** (vahvistettu 2026-10-15). Neljä nimettyä poikkeusta (KYS-11) | Reset, Swap Teams, joukkueen tuonti, File→Load, kenttien muokkaus, assetti-muutokset: **ei overlay-pushia**. Match **Update**, **Update (General)**, **Update (Waiting)**, **Update + sort**, **Update (Bracket)** kirjoittavat viennit ja lähettävät SSE:n. **Poikkeukset, jotka lähettävät itse**: SOWDraft-/turnauslinkin saapuva data, odotusruudun ajastimen komennot, valmis replay-leike, käynnistys (viimeisin tallennettu tila). Katso luku 5.1 |
| KYS-11a | **Linkit täyttävät ja lähettävät** | SOWDraft- ja turnauslinkin saapuva data kirjoittuu tilaan ja viedään/lähetetään heti (kuten vanhassa). Unlink: nollaa ja lähettää (kuten vanhassa) |
| KYS-11b | **Ajastin vaikuttaa heti** | Start/Pause/Reset ja ±aika lähettävät `waiting.timer` heti (EHD-9: `endsAt`, ei sekuntitikkejä) |
| KYS-11c | **Replay automaattisesti** | valmis leike kopioidaan soittolistaan ja `replay` lähetetään heti |
| KYS-11d | **Käynnistys lähettää viimeisimmän tilan** | `autosave.json` ladataan ja viedään/lähetetään overlayihin käynnistyksessä (oletettu "Kyllä" – vastaus oli kirjoitettu "Kylä"; vahvistettava) |

> Tulkintoja jotka vaativat vahvistuksen: (a) KYS-4 + KYS-6 yhdessä = overlayjen *datakerros* muuttuu, *ulkoasu* ei. (b) KYS-9 koskee vain Resetiä; Swap/Import/Load jäävät avoimeksi kunnes EHD-3 ratkaistaan.

### Edelleen avoinna

- ~~KYS-8~~ **RATKAISTU 2026-10-15: vaihtoehto (b)** – sovitaan virallinen, dokumentoitu tietomuoto ja muutetaan SOWDraftia (luvut 8.4–8.5; S1–S5 hyväksytty, linkitys "turnaus ensin", pelaajat/roolit täytetään automaattisesti ja lähetetään live). Alkuperäinen kysymys: SOWDraft on toinen sovellus, jonka omistatte itse (`sowdraft`-kansio). SOWBroadcast *arvaa* SOWDraftin lähettämän datan muodon (kentät kuten `team1Name`, `previousMaps`)
  koodista, eikä muotoa ole kirjattu mihinkään. Tästä seuraa mm. se, että joukkueet tunnistetaan **nimen perusteella** – jos nimi on kirjoitettu eri tavalla, pick/ban/pisteet jäävät hiljaa tuomatta.
  Kysymys: Haluatteko (a) jatkaa nykyisellä "arvatulla" muodolla, vai (b) sopia SOWDraftin kanssa **virallisen, dokumentoidun tietomuodon** ja tarvittaessa muuttaa myös SOWDraftia
  (esim. joukkueille tunnisteet/värit/logot mukaan)? Jos (b), tarkistetaan ensin SOWDraftin lähdekoodista mitä se oikeasti lähettää ja kirjataan se tähän tiedostoon.
- ~~KYS-12~~ **Ratkaistu: kyllä** – puuttuva lyhenne ehdotetaan automaattisesti (3 ensimmäistä kirjainta isoina), muokattavissa, SOWDraftin arvo voittaa kun ilmestyy (luku 8.4).
- ~~KYS-13~~ **Ratkaistu: ei automaattista valintaa** – käyttäjä valitsee ottelun aina itse; ei Auto-tilaa; S1:n `live` palautuu vain `matchId`-parametrilla (luku 8.4–8.5).
- ~~KYS-11~~ **Ratkaistu** (a: linkit täyttävät ja lähettävät, b: ajastin heti, c: replay automaattisesti, d: käynnistys lähettää – oletettu "kyllä", vahvistettava; ks. päätöstaulukko).
- ~~KYS-10~~ **Ratkaistu: `MapDraft.html`** säilyy. `draft.html` ei ole käytössä eikä tule mukaan uuteen versioon.
- ~~KYS-14~~ **Ratkaistu (tulkinta: säilytetään mahdollisuus – vahvistettava):** `MapDraft_v2.html`:n **video-taustatuki yhdistetään valinnaisena ominaisuutena `MapDraft.html`:ään**: jos `Maps/<slug>.mp4` on olemassa, näytetään se mykistettynä, silmukoituna taustavideona map-kuvan sijaan, muuten `Maps/<slug>.png` (nykyinen käytös). Olemassaolo tarkistetaan kerran per slug ja välimuistitetaan. `MapDraft_v2.html` itsenäisenä tiedostona ei tule mukaan. Videoita ei ole vielä – ominaisuus ei saa vaikuttaa ilman niitä. (Jos tarkoitit "jätetään pois", ominaisuus poistetaan tästä.)
- **EHD-ehdotukset**: kaikki ratkaistu, päätöstila luvun 14 alussa (EHD-3 ja EHD-10 hyväksytty 2026-10-15).

---

## 16. Viitetiedostot vanhassa repossa

| Aihe | Paikka |
|---|---|
| Tietomallit (dataclassit) | `SOWBroadcast.py:228-455`; `HERO_ROLES` :170; `_hero_body_filename` :260 |
| Haettava combo | `SOWBroadcast.py:22-160` |
| BracketMatchWidget / TeamRow / dialogit | :718-899; AssetManager :899–1151; BulkImport :3024 |
| PlayerRow, TeamPanel, MapCard | :1151–1508 |
| General/Waiting/Standings/Bracket-välilehdet | :1509 / :1735 / :2023 / :2492 |
| Turnauslinkin kuvaus standings/bracketiksi | :462–715 |
| Päävaikkuna, `_collect_state`, `_apply_state`, `_update` | `TournamentApp` ~:3361; :5044 / :5107 / :5232 |
| Viennit (`_export_*`), diff, notify, replay-vahtija | :4014–4700; :4262; :4479; :4220 |
| Draft-tila → UI | :4955–5039 |
| Palvelin | `server.py` |
| Linkit | `draft_link.py`, `tournament_link.py` |
| Overlayt | `HTML/*.html`; assetit `HTML/assets`, `HTML/fonts` |
| Asennus | `setup.iss` (ajantasainen), `install.iss` (vanhentunut), `SOWBroadcast.spec`, `SOWServer.spec`, `launch.bat` |
| Brändi | `suomiow nettisivut/docs/brand/graafinen-ohjeistus.pdf`, `…/src/assets/brand/*.png`, fontit `sowdraft/public/fonts/*.woff2` (+ lisenssit) |
