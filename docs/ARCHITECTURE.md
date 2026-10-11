# MyMovieDatabase — Architecture

Status: **draft v1** (2026-10-08). Look & feel and the detailed feature list are being planned separately with the user; this doc covers structure only.

## Goals

- Track a personal collection of **physical movies** and **comic books**, kept as separate collections.
- Add items by **barcode scan** (phone camera) or **manual entry**; both paths can pull metadata from online databases, and every new item can be edited before it's accepted.
- **Wishlist** for both product types.
- **PC app** (Windows) and **phone app** (Android) that talk over the home Wi-Fi.
- Mirror the collection into an **Obsidian vault** so Claude can answer questions about it.
- Professional, deliberate UI (design to be decided together).

## Decisions

| Area | Decision | Why |
|---|---|---|
| Language/runtime | C# on **.NET 10 (LTS)** | One language for server, PC and phone; user knows C#. |
| App shell | **.NET MAUI Blazor Hybrid** (Windows + Android) | One UI codebase in Razor/HTML/CSS. CSS gives full control over a polished look; MAUI gives native camera access. |
| Source of truth | **SQLite on the PC** via EF Core | Single file, zero admin, easy backup. |
| Vault | **One-way mirror**: app writes markdown, never reads it back | Simple and reliable; no conflict resolution. |
| Phone ↔ PC | **HTTPS REST over LAN**, QR-code pairing (mDNS discovery later) | Works without internet; pairing QR doubles as trust setup. |
| Phone OS | **Android only** | Easy install from the PC (no store, no Mac). |

## System overview

```
┌──────────────────────────── Windows PC ────────────────────────────┐
│  MMDb.App (MAUI, Windows)                                          │
│   ├─ Blazor UI (MMDb.UI)                                           │
│   └─ hosts in-process ─► MMDb.Server (ASP.NET Core, Kestrel :5243) │
│                           ├─ Application use cases                 │
│                           ├─ SQLite (EF Core)        ← truth       │
│                           ├─ Metadata providers ───► internet APIs │
│                           ├─ Cover image store                     │
│                           └─ Vault writer ─────────► Obsidian vault│
└──────────────────────────────▲─────────────────────────────────────┘
                               │ HTTPS (pinned self-signed cert), LAN
┌──────────────────────────────┴───── Android phone ─────────────────┐
│  MMDb.App (MAUI, Android)                                          │
│   ├─ Blazor UI (MMDb.UI, same components)                          │
│   ├─ Camera barcode scanner                                        │
│   ├─ MMDb.Local over its own SQLite: a copy of the PC's collection │
│   ├─ Metadata providers ───► internet APIs (keys from the PC)      │
│   └─ MMDb.Client: pairing, sync, changes waiting to be sent        │
└────────────────────────────────────────────────────────────────────┘
```

The PC owns the data: its database is the one true copy. The phone keeps a copy of it and runs the same code over that copy, so it browses, searches, looks things up and saves wherever it is; what it saves is sent to the PC when that is next in reach, and the PC's answer replaces the phone's (see *The phone's copy*).

## Solution layout

```
MyMovieDatabase.slnx
src/
  MMDb.Domain/          Entities (Movie, Comic, WishlistMovie, WishlistComic), value objects
                        (Upc, ComicBarcode, Isbn), validation. No dependencies.
  MMDb.Application/     Use cases (LookupBarcode, SearchMetadata, AddMovie,
                        AddComic, UpdateX, Wishlist ops, Query, Stats). Defines ports:
                        IMovieRepository, IComicRepository, IMovieMetadata,
                        IBarcodeLookup, IPosterSource,
                        IVaultWriter, ICoverStore. Uses TimeProvider for time.
  MMDb.Infrastructure/  EF Core + SQLite, metadata provider adapters, vault
                        writer, cover store, HTTP resilience (retry, rate limit).
  MMDb.Contracts/       Request/response DTOs shared by server and client.
  MMDb.Local/           A collection on this device, for whichever app has a database:
                        InProcessCollectionApi and InProcessStatsApi over the use
                        cases, and the mapping to the contracts. No ASP.NET Core,
                        so it runs on Android too.
  MMDb.Server/          PC only. Library hosted inside the PC app (PhoneHost):
                        ASP.NET Core minimal API over ICollectionApi, pairing,
                        device tokens, the PC's certificate, rows and smaller
                        covers for phones; vault, digital and phone APIs for the
                        PC's screens.
  MMDb.Client/          The phone's side: PcLink (pairing, certificate pin, token,
                        the PC's keys), ShelfSync and Outbox (the copy and what
                        waits to be sent), PhoneCollectionApi, HttpCollectionApi
                        (straight to the PC), covers kept on the phone.
  MMDb.UI/              Razor class library: pages, components, design system
                        (CSS tokens). Shared by both platforms.
  MMDb.App/             MAUI Blazor Hybrid host. Windows: starts the server
                        in-process. Android: scanner + client.
tests/                      (each added when its layer first has code)
  MMDb.Architecture.Tests/    layer dependency rules (reads src/*.csproj)
  MMDb.Domain.Tests/          pure logic (barcode parsing, validation)
  MMDb.Application.Tests/     use cases against real SQLite + fake providers
  MMDb.Infrastructure.Tests/  EF mappings/migrations, providers (recorded HTTP),
                              vault output (snapshot)
  MMDb.Server.Tests/          HTTP API on an in-memory TestServer
  MMDb.Client.Tests/          the phone as the app builds it (own SQLite, faked online databases)
                              against the in-memory server; real HTTPS on loopback
  MMDb.UI.Tests/              bUnit component tests
  MMDb.E2E.Tests/             Playwright over WebView2 (few critical journeys)
```

Dependency rule: `Domain ← Application ← Infrastructure ← Local ← Server/Client`; `UI` depends only on `Contracts` and an `ICollectionApi` interface (declared in `MMDb.Contracts`). On Windows that interface is `MMDb.Local.InProcessCollectionApi`, which calls the use cases directly (one DI scope per call, like a request); on Android it is `MMDb.Client.PhoneCollectionApi`, which does the same over the phone's own database and queues what is saved for the PC. The same screens therefore run in both places; `ShelfDevice.IsPhone` switches the frame (bottom bar instead of sidebar) and hides what the phone doesn't do (editing and deleting what the PC has). `ICollectionApi.Changed` tells open screens on the PC that something was saved, so a phone's additions appear in the PC's library and counts by themselves.

**Data on the PC:** `%LOCALAPPDATA%\Shelf\shelf.db`, backed up on startup to `backups\` (SQLite online backup, newest 10 kept), then migrated. Downloaded covers live in `covers\` beside it (one file per film and poster, `<movie id>-<TMDB poster file>`); the WebView serves that folder as `https://covers.shelf.example/`. Set `SHELF_DATA_DIR` to run against another folder (scratch data, E2E). Schema changes: `dotnet ef migrations add <Name> --project src/MMDb.Infrastructure --output-dir Persistence/Migrations` (local tool, `dotnet tool restore`); a test fails if the model has changes without a migration.

## Data model (initial; fields to be finalized in feature planning)

Movies and comics are **separate tables and separate screens**; they share only common columns via a base type.

- **ProductBase**: `Id (Guid)`, `CreatedAt`, `UpdatedAt`, `Version` (optimistic concurrency + sync), `CoverImage`. No per-copy details (condition, price, location): the user decided these are clutter.
- **Movie** (one row per film): `Title`, `Year`, `TmdbId`, `Directors`, `Cast` (everyone TMDB bills, with the part played), `Crew` (everyone else TMDB credits: name, job, department; kept in credit order, never edited by hand), `People` (the cast's and crew's names folded to lower case without accents, kept in step on every save, so search can find a film by its credits in SQL), `Genres`, `RuntimeMinutes`, `Rating`, `Overview`, `TrailerYouTubeKey`.
- **MovieFormat** (1..n per movie): `MovieId`, `Format` (DVD / Blu-ray / 4K UHD / Digital), `Upc?`, `FilePath?` (Digital only: the video file on the PC), `AddedAt`. Unique on (`MovieId`, `Format`). Owning a film on several formats = one Movie with several MovieFormats.
- **Comic** (one row per copy: each variant or printing owned is its own row): `Format` (single issue / trade paperback / hardcover / omnibus), `Series`, `SeriesYear` (the year it began, which tells volumes apart: Daredevil (1964)), `Number` (text: "181", "½", "1.MU"; a trade's volume number), `Title` (a trade's title or an issue's story title), `Variant`, `Printing`, `Publisher`, `CoverDate`, `Writers`, `Artists`, `Summary`, `Upc` + `UpcSupplement` (the 5-digit add-on), `Isbn`, `MetronId`, `MetronSeriesId`, `IsKeyIssue`, `KeyNote` (e.g. "1st Elektra"), `CoverUrl` (where the cover was downloaded from).
- **ComicSeries**: `MetronId`, `Name`, `YearBegan`, `Publisher`, `IssueNumbers` (every issue Metron lists), `FetchedAt` — powers "N of M" and missing issues. Fetched when an issue of the series is saved and the copy kept is missing or a week old.
- **PairedDevice**: `Id`, `Name`, `TokenHash` (SHA-256 of the phone's token), `PairedAt`, `LastSeenAt`.
- **WishlistMovie** (one row per film wanted): `Title`, `Year`, `Directors`, `TmdbId?`, `TrailerYouTubeKey`, `PosterPath`, `Format` (the one format wanted), cover. Only what the list shows and what finds the film again; "Got it" fetches the rest from TMDB. No priority, price or notes.
- **WishlistComic** (one row per issue or book wanted): `Format`, `Series`, `SeriesYear`, `Number`, `Title`, `Publisher`, `Isbn`, `MetronId?`, `MetronSeriesId?`, `CoverUrl`, `FromMissing`. Which cover or printing doesn't matter.
- **SkippedFile**: `Path` (the key, compared without case), `SkippedAt`. A video file in the movie folder the user never wants offered for import.
- **LookupCache**: provider + request (API key stripped) → raw JSON + fetched-at, to avoid repeat API calls and rate-limit trouble. Kept 30 days (TMDB search results: 1 day). Only successful answers are kept; for UPCitemdb that includes "unknown barcode".

## Adding a product: scan → review → accept

1. Phone camera decodes a barcode (see *Barcode scanning* below) and sends `POST /api/v1/scans { code, kind? }` (the add-on digits follow the UPC in `code`).
2. Server classifies the code (`ScannedCode.KindOf`), unless the user chose Movie or Comic on the scan screen:
   - EAN-13 starting `978`/`979` → **ISBN** (comic trade/graphic novel).
   - UPC-A with an add-on → **comic single issue** (add-on = issue/variant/printing).
   - Otherwise → **movie**. A comic whose add-on wasn't read looks like a film; the review screen offers "Look it up as a comic".
3. Server runs the provider chain for that type, merges results into **candidates**, and returns them. Nothing is saved yet.
4. User picks a candidate, **edits any field**, and taps Accept → `POST /movies` or `/comics`.
5. Server saves to SQLite, downloads the cover, and writes the vault note.
   - **Movies:** if the TMDB id already exists, the review screen says so, and Accept adds the new format to the existing film. If that format is already owned, it reports a duplicate.
   - **Comics:** the same copy (same UPC and add-on, same ISBN, or the same Metron issue, variant and printing) is turned down as already owned; another variant or printing is a copy of its own.

**Wishlist match:** every lookup also checks the wishlist: each candidate carries `wish` if it is wanted (`MovieMatch.Wanted`, `ComicMatch.Wanted`). The phone shows an alert over the camera as soon as it arrives, and the review screen repeats it.
- Movies match by `TmdbId`; a wish entered without one matches by title and year (`WishlistMovie.IsFor`). Saving a film on the wanted format removes the wish, in the same database write as the film (`MovieCatalog`). Saved on another format, the wish stays unless the review screen's "Keep it on the wishlist" is switched off.
- Comics match by Metron series (else series name and starting year) and issue number, a book by ISBN (else title); any cover or printing counts (`WishlistComic.IsFor`). Saving a copy takes the issue or book off the wishlist (`ComicCatalog`).
- **Got it:** `LookupWishlistMovieAsync` / `LookupWishlistComicAsync` return the wish's TMDB or Metron entry as an ordinary lookup, so the review screen opens on it. A wishlist item's own page uses the same lookup for what the wishlist doesn't keep (overview, credits, summary); the lookup cache makes reopening it free.
- **Amazon:** each item carries `AmazonUrl`, a search link made by `AmazonSearch` (Domain) beside the trailer link: title, year and wanted disc for a film (none for Digital), the display name for a comic. The row and the item's page show it as a button; nothing is fetched from Amazon.
- **Missing issues:** the library's missing tiles ("#159–167") have "+ Wishlist"; `WantMissingIssuesAsync` takes the numbers from the series' issue list and adds each issue as an item of its own (they have no Metron entry or cover until bought). Tiles say "On wishlist" where they are (`RunEntry.Wanted`); wanted and unwanted issues never share a tile.
- **Away from home:** nothing special. The phone runs the same lookup against its own copy with its own access to the online databases, so the match is the real one (the "likely" match once planned, by product name alone, was never built).

**Scan queue:** the phone can scan several barcodes in a row. Each one is looked up in the background and waits in a queue (`ScanInbox`) for review, one by one, in the same editor the PC uses. The queue is kept in the phone's database (`IScanQueueStore`), so it is still there after the app was closed; what was found before comes from the lookup cache. With no signal a scan waits ("Couldn't look it up") and is tried again whenever the queue is next on screen; a barcode the phone's copy already has is answered without any signal.

**Manual entry** uses the same review screen: the user types a title (movies) or series + issue (comics), the server searches the same providers by text, and the user picks or ignores the suggestions.

## Metadata providers

Movies use three ports: `IMovieMetadata` (TMDB: search by title + year, details by id), `IBarcodeLookup` (UPCitemdb: barcode → product names) and `IPosterSource` (TMDB's image server). `MovieLookup` chains them; each HTTP client has the lookup cache and the standard resilience handler (retries, timeouts; UPCitemdb's 429s are not retried). Comics use `IComicMetadata` (Metron), `IBookMetadata` (Open Library) and `ICoverSource` (their image servers only); `ComicLookup` chains them.

- **Lookup by text:** a trailing year narrows the search ("heat 1995"); if that finds nothing the whole text is tried, for titles like "Blade Runner 2049". Up to 8 matches are fetched in full (credits, videos and US release dates in one request each).
- **Lookup by barcode:** a UPC already in the collection is answered from the database without any API call. Otherwise UPCitemdb's product name is cleaned (`RetailTitle`: packaging words, label prefixes like "Mill Creek -", bracketed parts; a bracketed year is kept) and searched on TMDB; the format comes from the name (combo packs count as the best disc).
- **From TMDB:** directors from the crew, the top 10 billed actors, genres, runtime, US theatrical certification as the rating, overview, trailer (see below) and poster path.
- **Covers:** saved at TMDB's `w500` (about 60–90 KB, ~300 MB for 4,000 films) when the film is saved; the poster path is kept so the phone can get smaller thumbnails later. A failed download leaves the film without a cover until its next save.
- **Keys:** the TMDB key is read from the app's user secrets: `dotnet user-secrets set Tmdb:ApiKey <key> --project src/MMDb.App`. Either TMDB's API Read Access Token or the older API Key works. UPCitemdb's free tier needs no key (100 lookups a day, 6 a minute).

| Type | By barcode | By text search | Notes |
|---|---|---|---|
| Movie | UPCitemdb → product title | **TMDB** (rich metadata, posters) | No API maps UPC straight to TMDB. Take the UPC title, strip noise ("[Blu-ray]", "Widescreen"), detect format, then search TMDB. |
| Comic issue | **Metron** `upc` (all 17 digits); if not listed, `upc_starts_with` the UPC + the issue number from the add-on | Metron by series words, year began, number | Token in user secrets (`Metron:Token`). 20 req/min, 5,000/day: a search fetches at most 6 issues; issue details cached 30 days, searches 1 day. GCD and Comic Vine have no barcode search, so they aren't used. |
| Comic trade | **Open Library** Books API by ISBN (no key) | (enter by hand) | Cover from Open Library; format defaults to trade paperback. |

API keys are kept in the PC's user secrets/config, never in the repo or built into the phone app. A paired phone is handed them (`GET /api/v1/keys`) so it can ask the same databases itself; see *The phone's copy*.

**Trailers:** TMDB `GET /movie/{id}/videos` lists YouTube videos. Pick `site=YouTube`, `type=Trailer`, preferring `official=true`, then the newest. Store the key; the Trailer button opens `https://www.youtube.com/watch?v={key}` in the system browser/YouTube app (no in-app player). If TMDB has no trailer, fall back to a YouTube search link (`<title> <year> trailer`). The vault note includes the trailer link too.

## Digital movies (PC only)

- **Movie folder:** the user picks a single folder (Settings or the import screen; kept in the `Settings` table as `digital.folder`). A scan lists its video files (`.mkv .mp4 .m4v .avi .mov .wmv`, recursive) and sorts them into: linked (a film's digital copy already), waiting for review, skipped, and missing (a film's file that is no longer there). **A scan only reads the folder and the database; it asks TMDB nothing**, so it is cheap enough to run at startup and after every change the watcher reports.
- **Parsing:** `ReleaseNameParser` (Domain, pure) turns a file name into title + year. The title runs up to the year or the first thing that can only be a tag (`2160p`, `WEB-DL`, `x265`, `S01E02`…); dots and underscores become spaces. Words that are tags but also title words (`KOREAN`, `Uncut`, `Complete`) only end a name that has no year, so "The English Patient" survives. The year is the last one before the tags unless the name starts with it ("1917.mkv", "2001.A.Space.Odyssey.mkv"). A file whose own name has no year takes its folder's when that has one ("Heat (1995)/movie.mkv").
- **Matching:** done when the import screen is open, four files at a time (`DigitalImport.MatchAsync`). `IMovieMetadata.FindAsync` lists a search's films as TMDB's search itself gives them (id, title, original title, year, poster): one request per file, no details fetched. A number taken for the year that finds nothing is searched again as part of the title ("Blade Runner 2049.mkv"). Rated *Matched* (one film has the title and the year, ignoring case, accents and punctuation; a year either side counts when no film has the year itself; of several with the title and year, TMDB's first if it is one of them), *Check this* (anything else that found films: no year, a title that is only close) or *No match*. Of two new files that turn out to be one film, the second is left unticked.
- **Saving:** adding fetches each new film's full details (four at a time) and saves it through `MovieCatalog.AddAsync` with a `Digital` MovieFormat and `FilePath`. A film already owned keeps its details and gets the file (`MovieCatalog.LinkFileAsync`). From ten films up, the vault is written once at the end (`VaultMirror.Hold` / `ReleaseAsync`) instead of after every film.
- **One file per film:** a second file for a film that has one is never added by itself; "Use this file instead" swaps the film's file and skips the old one.
- **Skipped files:** kept in the `SkippedFiles` table (path, case-insensitive), so later scans leave them out until they are brought back.
- **Watch folder:** `IFolderWatcher` (Infrastructure: a `FileSystemWatcher` on file and folder names, recursive) tells the PC once things have been quiet for two seconds; `InProcessDigitalApi` then scans again. A scan also runs at startup to catch what changed while the app was closed. Missing files are flagged, never removed automatically; a file that turns up elsewhere in the folder under the same name (and is the only one) is linked again. If the folder itself is gone (a drive unplugged), nothing is called missing.
- **State:** `ImportDesk` (Application, one per app) holds what the last scan found and what was looked up, as an immutable status the screens read; `DigitalImport` (scoped) has the use cases; `IDigitalApi` / `InProcessDigitalApi` hand it to the UI. PC only: the phone has no `IDigitalApi`.
- **Play file:** opens the film's file with the OS default player (`IFileOpener`: `Process.Start` with `UseShellExecute`), only if it is a video file that exists: a path can be typed by hand or come from a phone, and opening runs whatever it names. PC only; the phone shows the format but no play button.

## Stats

- `StatsSource` (Application) counts the whole collection in one go: `IMovieRepository.ListTalliesAsync` (year, directors, when it was added, and each format with when it was added), `IComicRepository.ListTalliesAsync` (format, series, number, publisher, when it was added), the series Metron described and two counts of the wishlist. No credits, summaries or covers are read; the adding up is in memory.
- Months are calendar months in the PC's time zone (`TimeProvider.LocalTimeZone`). "Items added per month" counts copies (a film's formats by `AddedAt`, comics by `CreatedAt`) for this month and the eleven before.
- Runs and complete runs are told as the comics library tells them (`ComicFilter.RunKey`, `ComicFilter.SeriesOf`), so the tile agrees with "N of M" there.
- `IStatsApi` / `InProcessStatsApi` hand the screen a `StatsDto`. The phone has them too, counting its own copy: Stats is the fifth tab of its bottom bar, the same screen in one column (no "on this PC" size, which needs `IDigitalApi`).
- **Size on disk:** not part of the stats read. The movie-folder scan notes each video file's size as it lists the folder (`ImportStatus.LinkedBytes`: the files that are a film's digital copy), and the Digital tile shows `IDigitalApi.Folder.LinkedBytes`.
- **Bars are links** into the libraries' own addresses: `movies?format=`, `movies?decade=`, `movies?director=` (`MovieQuery.Director`: the whole name, ignoring case and accents) and `comics?shelf=Issues&publisher=`.

## Barcode scanning

- Library: `BarcodeScanning.Native.Maui` (Google ML Kit on Android). It's fast, but **ML Kit does not read UPC add-ons** (checked on a phone, 2026-10-10: the main barcode is read, the add-on never).
- The camera is a native view under the web view (`MainPage`); the scan screen keeps its background see-through and draws the reticle, switch and queue over it in HTML.
- **Add-ons (spike #1, done 2026-10-10):** Shelf reads them itself. When ML Kit finds a UPC or EAN, the frame it was read in is handed over as well (`CaptureNextFrame`), and `AddOnReader` (`MMDb.UI/Scanning`, plain C#, no camera needed to test it) looks just past either end of the barcode for a 2- or 5-digit add-on:
  - ML Kit's box is only roughly on the barcode and often takes in part of the add-on, so each of 15 lines across the bars finds the barcode's own first and last bar by its build (59 runs, 95 bar widths, guards at the ends and in the middle).
  - From there it reads outward: a blank gap, a start guard, the digits, a blank margin. The digits must fit the check that the choice of bar sets encodes; several lines have to agree (two for five digits, four for two digits, which are only loosely checked).
  - Brightness is the red channel, as in a shop's scanner: on a red or yellow cover the bars would hardly stand out in green.
  - `ProductReadings` turns frames into scans: a UPC with its add-on is one code at once; a UPC without one waits 0.6 s for a frame that shows it; a two-digit add-on counts once two frames agree; frames that miss the add-on after it was read aren't another product; a book's ISBN leaves its add-on (the price) out. In Movie mode none of this runs.
  - ZXing.Net was tried first and did not read the add-on from a clear photo, so it is not used.
  - Older comics: Metron often has no barcode for them (a 1980s DC newsstand UPC gave nothing at all), so they open on the search by series and issue; the barcode is kept for the copy.
- Scanner sits behind `IBarcodeScanner` (in `MMDb.UI`), so UI and logic are testable with a fake; `CameraScanner` in the Android app implements it.

## PC ↔ phone communication

- **Finding the PC:** the pairing QR code lists the PC's addresses (Wi-Fi and wired adapters before VPN and virtual ones). The phone remembers them all, tries them in turn and puts the one that answered first. mDNS discovery (`_mmdb._tcp`), for when the router gives the PC a new address, waits until it can be tested on a real phone; until then the phone pairs again.
- **Pairing:** Settings on the PC shows a QR code (`shelf://pair?h=…&p=5243&c=…&f=…&n=…`) with the PC's addresses and port, a one-time six-digit code, and the **SHA-256 fingerprint of the PC's self-signed TLS certificate**. The phone scans it, pins the certificate (nothing else about it is checked) and exchanges the code for a long-lived device token. The address and code can be typed instead; the phone then pins whichever certificate that address shows (trust on first use). Devices can be unpaired on the PC. This avoids Android's cleartext-HTTP block without needing a real certificate.
  - The code exists only while Settings shows it, works once and for five minutes; five wrong guesses throw it away and close pairing for a minute.
  - The token is 32 random bytes; the PC stores only its SHA-256 (`PairedDevices` table) and when the phone was last seen.
  - The certificate (`phone.pfx` beside the database, ECDSA P-256, 20 years) is made on first run and kept, so paired phones keep recognizing the PC.
- **API:** REST + JSON under `/api/v1`, one endpoint per `ICollectionApi` call plus `pair`, `session`, `keys`, `sync`, `sync/rows`, `scans` and `covers/{name}`; everything but `health` and `pair` needs `Authorization: Bearer <token>` (401 otherwise). A save that doesn't go through answers 400/404/409 with the same response body the PC's screens get. DTOs live in `MMDb.Contracts` (`ShelfJson`: camelCase, enums by name), so client and server can't drift.
- **Covers:** pages ask for stored covers under `https://covers.shelf.example/` on both devices. On the PC the web view maps that host to the cover folder; on the phone the app intercepts the request and answers from its own cover folder (`CoverShelf`), fetching from the PC what isn't there yet. A page that shows one cover large (`Poster` with `Large`) asks with `?full=1` and gets the PC's own file while the PC is connected, kept under `covers/full`; otherwise the medium copy.
- **PC away:** the phone carries on with its copy. Only a phone that never synced shows "Can't reach <PC>" with Try again.
- **Windows firewall:** the first time the PC app listens, Windows asks whether to allow it; allowing it on private networks is enough. Shelf adds no rule itself. A VPN on the PC can block devices on the home network until its local-network access setting is on.

## The phone's copy

The phone has its own SQLite database (`shelf.db` in the app's files, never backed up by Android) with the PC's schema, created by the same migrations, and its own cover folder beside it. `PhoneServices.AddShelfPhone` puts it together: `MMDb.Local` over that database, plus the link, the sync and the outbox.

- **Reading:** every screen reads the copy through `PhoneCollectionApi` → `InProcessCollectionApi`, at home too. Search, runs and missing issues, wishlist matches and Stats are the PC's own code, so they can't differ.
- **Looking up:** the phone asks TMDB, UPCitemdb, Metron and Open Library itself, with the PC's keys. `PcLink` gets them from `GET /keys` when it pairs and at every sync, keeps them with the pairing in Android's keystore (`SecureLinkStore`), and drops them when the PC answers 401 (unpaired) or the PC is forgotten. The phone has a lookup cache of its own. Metron's limits are per account, so PC and phone share them.
- **Saving:** a save is made in the copy at once and added to the `Outbox` (a JSON list in the copy's `Settings` table: the request as it was made, the row it made or changed, and a few words for the user). Editing and deleting what the PC has throws `NotSupportedException`: the screens don't offer it.
  - Something the outbox made exists only on the phone until it is sent (`IPhoneSync.IsUnsent`), so it can still be removed there, which drops its changes from the outbox.
  - A wish made on the phone and changed or taken off again before it was sent rewrites or drops the change that made it, so nothing in the outbox ever points at a row the PC doesn't know.
  - Missing issues wanted as a run are queued issue by issue, for the same reason.
- **Syncing** (`ShelfSync`, one at a time): when the app opens or comes back to the front, after every save, "Sync now", and when the phone's network changes (`Connectivity.ConnectivityChanged` → `NetworkChangedAsync`, which tries after 1, 5 and 15 seconds until the PC answers: Android reports Wi-Fi a little before the PC can be reached over it).
  1. `GET /keys`: finds out whether the PC is there before anything waits on it.
  2. **Push:** each change in the outbox is asked of the PC through `HttpCollectionApi`, in order. Saved, already owned, or (for a wish) not there any more all count as done; anything else is noted for the user ("1 change couldn't be sent: …") and dropped. A PC that stops answering leaves the rest in the outbox.
  3. **Pull:** `GET /sync` lists every row's key and stamp (`Version` and cover name; for a comic series when it was fetched) for Movies, Comics, ComicSeries, WishlistMovies and WishlistComics. The phone removes the rows the PC no longer has and asks for the ones whose stamp differs (`POST /sync/rows`, 100 at a time), which arrive as SQLite holds them and are written over its own (`CollectionCopy`; a film's formats travel with it). Rows the phone made itself are replaced by the PC's this way.
  4. **Covers**, in the background: the phone fetches every cover the copy shows that it doesn't have (`covers/{name}?size=medium`, about 342 px wide, scaled by the PC with SkiaSharp in `CoverSizes`), and clears out files no row shows any more once they are an hour old.
- **Nothing else writes while it matters:** saves and the writes of a pull take turns. After a push the copy is held until the pull is through; without one (the first, long pull) saves go on between requests, and a pull that finds something newly saved stops and starts over with a push.
- **Same schema only:** rows travel as they are, so the manifest names the PC's newest migration; if the phone's differs nothing is written and the phone says to update both.
- **What the PC doesn't get:** the phone's lookup cache and scan queue.

## Obsidian vault (one-way mirror)

```
<Vault>/
  CLAUDE.md                     schema + how to query; read by Claude
  _Index.md                     compact summary: totals + one line per film/issue/wishlist item.
                                Claude reads this first instead of opening every note.
  Movies/<Title> (<Year>).md
  Comics/<Series> (<Year>)/<Series> <Issue>.md   one per copy: "Saga 7 (Cover B).md"; no # (links can't hold it)
  Comics/<Series> (<Year>)/_Series.md   issues owned (linked) and missing
  Comics/Trades & HCs/<Title>.md
  Wishlist/<Title> (<Year>) (wanted).md, <Series> <Issue> (wanted).md   one per wishlist item; "(wanted)" keeps the name
                                apart from the note of the same film owned on another format
  Attachments/covers/<id>-<file>   copies of the covers
  .trash/                       removed notes that had text under "My notes"
  Movies.base  Comics.base  Wishlist.base  (Obsidian Bases views)
```

- Each note has **YAML frontmatter** with every structured field, plus a readable body (overview, credits, cover embed, `[[links]]` to series/people). Frontmatter keys are stable and documented in the vault's `CLAUDE.md`.
- Every note includes `mmdb_id` and `managed_by: mmdb`. The app only touches notes it owns.
- Writes are atomic (temp file → rename). A "Rebuild vault" command regenerates everything from the database.
- **My notes:** everything under a note's `## My notes` heading is kept when Shelf rewrites the note (the only part Shelf ever reads back). A removed film's note is deleted, or moved to the vault's `.trash/` if it had text there.
- **When it is written:** each save or delete rewrites that film's or comic's note and cover, then the series notes, `_Index.md`, `CLAUDE.md`, `Movies.base` and `Comics.base` (files whose text hasn't changed are left untouched). At startup, on choosing a folder and on **Rebuild vault**, everything is brought up to date and notes of films no longer owned are removed. After a failed write (folder gone, file in use) the next change does a full catch-up. A failure never fails the save; it shows in the sidebar and in Settings.
- **Names:** `Title (Year).md`; characters Windows or Obsidian links can't hold are dropped (`:` becomes ` -`). Comics: `Saga 7.md`, with the variant or printing in brackets. A name already used by another note or by one of the user's own files gets ` (2)`. A series folder whose last issue goes loses its `_Series.md`, and the folder too if nothing of the user's is in it. When a title changes, the note moves with its My notes.
- **Shared files:** `CLAUDE.md` starts with a `managed_by: mmdb` comment and `Movies.base` with a `# managed_by: mmdb` line; without it (the user's own file, or a base whose views were changed in Obsidian) Shelf leaves the file alone.
- **Folder:** chosen in Settings and kept in the database's `Settings` table. Ports: `IVaultWriter` (files, in Infrastructure) and `ISettingStore`; `VaultMirror` takes one write at a time and holds the status.

## Cross-cutting

- **Time:** `TimeProvider` everywhere (testable).
- **Logging:** `Microsoft.Extensions.Logging` + rolling file on the PC.
- **Config:** user secrets for API keys (`Tmdb:ApiKey`, `Metron:Token`); the vault path is set in the app's Settings screen; phones are served on port 5243.
- **Backups:** the database file is copied on startup, keeping the last N copies.
- **Static analysis:** nullable enabled, analyzers on, `TreatWarningsAsErrors` in `Directory.Build.props`.

## Prerequisites to install

- .NET 10 SDK (pinned by `global.json`, roll-forward to latest feature band)
- `dotnet workload install maui` (includes Android)
- Android SDK/emulator (via Visual Studio installer or Android Studio); USB debugging on the phone
- API keys: TMDB, UPCitemdb (free tier, no key), a Metron API token; Open Library needs none

## Build order (milestones)

0. Solution skeleton, CI-style `dotnet test` script (`scripts/test.ps1`), design system tokens (`MMDb.UI/wwwroot/css/tokens.css`).
1. Domain + SQLite + movies CRUD on PC (manual entry, no lookup).
2. Metadata: TMDB search, UPC lookup; review-before-accept screen.
3. Vault writer + `CLAUDE.md` + Bases.
4. Comics (Metron, Open Library by ISBN), on the PC.
5. Server API + pairing + Android app with scanner. (The add-on scan spike and mDNS wait for a real phone.)
6. Wishlist + wishlist match on scan. (Done 2026-10-10. Away from home the phone now does the real lookup itself; see 7.)
6b. Digital folder import + watch folder, Play file. (Done 2026-10-10.)
7. The phone's own copy: it knows the collection and wishlist without the PC, looks things up and saves by itself, sends what it saved when the PC is next in reach, and has its Stats tab. Built last, after 8. (Built 2026-10-10; not yet run on a phone, see TESTING.md, *Phone hardware*.)
8. Stats screen. (Done 2026-10-10, on the PC.)
9. Amazon button on wishlist items: a search for the title, and the wanted format for a film. (Done 2026-10-11.)

Each milestone ships with its tests (see [TESTING.md](TESTING.md)).

## Open risks

1. ~~Comic add-on barcode decoding on Android~~: done, see *Barcode scanning*. The app has run on a phone since 2026-10-10 (Galaxy S24 Ultra, Android 16); what is left of the first run is in TESTING.md, *Phone hardware*.
2. ~~Whether Metron supports UPC lookup~~: it does (`upc`, `upc_starts_with`), checked in its source in milestone 4. Its fixtures are hand-written until a token is set up.
3. UPC coverage for older DVDs is spotty, so manual search must be first-class.
4. API terms: TMDB and Comic Vine are non-commercial/attribution-required. Fine for personal use; show attribution in About.
