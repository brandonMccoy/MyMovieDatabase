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
| Phone ↔ PC | **HTTPS REST over LAN**, mDNS discovery, QR-code pairing | Works without internet; pairing QR doubles as trust setup. |
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
│   ├─ MMDb.Client (typed HTTP client)                               │
│   └─ Local cache + offline queue (SQLite)                          │
└────────────────────────────────────────────────────────────────────┘
```

The PC owns all data and does all metadata lookups (API keys live only on the PC). The phone is a client: it scans, shows results, lets you edit, and sends the result to the PC.

## Solution layout

```
MyMovieDatabase.slnx
src/
  MMDb.Domain/          Entities (Movie, Comic, WishlistItem), value objects
                        (Upc, ComicBarcode, Isbn), validation. No dependencies.
  MMDb.Application/     Use cases (LookupBarcode, SearchMetadata, AddMovie,
                        AddComic, UpdateX, Wishlist ops, Query). Defines ports:
                        IMovieRepository, IComicRepository, IMetadataProvider<T>,
                        IVaultWriter, ICoverStore. Uses TimeProvider for time.
  MMDb.Infrastructure/  EF Core + SQLite, metadata provider adapters, vault
                        writer, cover store, HTTP resilience (retry, rate limit).
  MMDb.Contracts/       Request/response DTOs shared by server and client.
  MMDb.Server/          ASP.NET Core minimal API, auth (device tokens), mDNS
                        advertisement, pairing endpoint.
  MMDb.Client/          Typed HttpClient for the phone, offline queue, sync.
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
  MMDb.Server.Tests/          HTTP API via WebApplicationFactory
  MMDb.Client.Tests/          client + offline queue against in-memory server
  MMDb.UI.Tests/              bUnit component tests
  MMDb.E2E.Tests/             Playwright over WebView2 (few critical journeys)
```

Dependency rule: `Domain ← Application ← Infrastructure/Server`; `UI` depends only on `Contracts` and an `ICollectionApi` interface (declared in `MMDb.Contracts`). On Windows that interface is `MMDb.Server.InProcessCollectionApi`, which calls the use cases directly (one DI scope per call, like a request); on Android it will be `MMDb.Client`. The same screens therefore run in both places.

**Data on the PC:** `%LOCALAPPDATA%\Shelf\shelf.db`, backed up on startup to `backups\` (SQLite online backup, newest 10 kept), then migrated. Set `SHELF_DATA_DIR` to run against another folder (scratch data, E2E). Schema changes: `dotnet ef migrations add <Name> --project src/MMDb.Infrastructure --output-dir Persistence/Migrations` (local tool, `dotnet tool restore`); a test fails if the model has changes without a migration.

## Data model (initial; fields to be finalized in feature planning)

Movies and comics are **separate tables and separate screens**; they share only common columns via a base type.

- **ProductBase**: `Id (Guid)`, `CreatedAt`, `UpdatedAt`, `Version` (optimistic concurrency + sync), `CoverImage`. No per-copy details (condition, price, location): the user decided these are clutter.
- **Movie** (one row per film): `Title`, `Year`, `TmdbId`, `Directors`, `Cast` (top N), `Genres`, `RuntimeMinutes`, `Rating`, `Overview`, `TrailerYouTubeKey`.
- **MovieFormat** (1..n per movie): `MovieId`, `Format` (DVD / Blu-ray / 4K UHD / Digital), `Upc?`, `FilePath?` (Digital only: the video file on the PC), `AddedAt`. Unique on (`MovieId`, `Format`). Owning a film on several formats = one Movie with several MovieFormats.
- **Comic**: `Series`, `Volume`, `IssueNumber`, `Variant`, `Printing`, `Publisher`, `CoverDate`, `Writers`, `Artists`, `Format` (single issue / TPB / HC / omnibus), `Upc`, `UpcSupplement`, `Isbn`, `MetronId`, `IsKeyIssue`, `KeyNote` (e.g. "1st Elektra").
- **Series** (comics): `Name`, `Volume`, `Publisher`, `MetronId`, `TotalIssues` — powers progress bars and "missing issues".
- **WishlistItem**: `ProductType`, the same descriptive fields (nullable), `Priority`, `MaxPrice`, `Notes`, `AcquiredAt` (moving an item to the collection links the two).
- **LookupCache**: provider + key → raw JSON + fetched-at, to avoid repeat API calls and rate-limit trouble.

## Adding a product: scan → review → accept

1. Phone camera decodes a barcode (see *Barcode scanning* below) and sends `POST /lookup { code, supplement?, productType? }`.
2. Server classifies the code:
   - EAN-13 starting `978`/`979` → **ISBN** (comic trade/graphic novel).
   - UPC-A with 5-digit add-on → **comic single issue** (add-on = issue/variant/printing).
   - Otherwise → **movie** (unless the user chose a type first).
3. Server runs the provider chain for that type, merges results into **candidates**, and returns them. Nothing is saved yet.
4. User picks a candidate, **edits any field**, and taps Accept → `POST /movies` or `/comics`.
5. Server saves to SQLite, downloads the cover, and writes the vault note.
   - **Movies:** if the TMDB id already exists, the review screen says so, and Accept adds the new format to the existing film. If that format is already owned, it reports a duplicate.
   - **Comics:** same UPC + supplement (or Metron id) warns as a duplicate.

**Wishlist match:** every lookup also checks the wishlist and returns `wishlistMatch` with the candidates. The phone shows an alert over the camera as soon as it arrives, and the review screen repeats it. Accepting removes the matched wishlist item, or for a partial match asks first.
- Movies match by `TmdbId`. *Full match* = the wanted format was scanned. *Partial* = same film, different format (e.g. wanted 4K, scanned DVD).
- Comics match by `MetronId`, else series + volume + issue (+ variant if set).
- **Away from home:** the phone can't reach the PC, so it matches the scanned barcode against barcodes in its cached collection and wishlist. If that finds nothing, it calls UPCitemdb directly over mobile data (free tier, no key) and fuzzy-matches the product title against cached wishlist titles. The result is marked "likely" and the scan joins the queue for a proper lookup at home.

**Scan queue:** the phone can scan several barcodes in a row. Each one is looked up in the background and waits in a queue for review, one by one.

**Manual entry** uses the same review screen: the user types a title (movies) or series + issue (comics), the server searches the same providers by text, and the user picks or ignores the suggestions.

## Metadata providers

Each provider implements `IMetadataProvider<TCandidate>` with `LookupByCodeAsync` and/or `SearchAsync`. They are run as a chain with fallbacks, and each is cached and rate-limited.

| Type | By barcode | By text search | Notes |
|---|---|---|---|
| Movie | UPCitemdb → product title | **TMDB** (rich metadata, posters) | No API maps UPC straight to TMDB. Take the UPC title, strip noise ("[Blu-ray]", "Widescreen"), detect format, then search TMDB. |
| Comic issue | **Metron** (UPC filter, *to verify*) → GCD barcode search | Metron / Comic Vine by series + issue | Metron limit: 20 req/min. |
| Comic trade | Open Library / Google Books by ISBN | same | |

API keys are kept in the PC's user secrets/config, never in the phone app or the repo.

**Trailers:** TMDB `GET /movie/{id}/videos` lists YouTube videos. Pick `site=YouTube`, `type=Trailer`, preferring `official=true`, then the newest. Store the key; the Trailer button opens `https://www.youtube.com/watch?v={key}` in the system browser/YouTube app (no in-app player). If TMDB has no trailer, fall back to a YouTube search link (`<title> <year> trailer`). The vault note includes the trailer link too.

## Digital movies (PC only)

- **Folder import:** the user picks a single movie folder (stored in settings), and the PC enumerates video files (`.mkv .mp4 .m4v .avi .mov .wmv`, recursive).
- **Parsing:** `ReleaseNameParser` (Domain, pure) turns a file name into title + year by stripping quality, source, codec, language and group tags (`2160p`, `UHD`, `WEB-DL`, `x265`, `KOREAN`…) and turning dots and underscores into spaces.
- **Matching:** TMDB search by title + year, scored into *Matched* (one confident hit), *Check this* (several hits, or no year) and *No match*. Files already linked to a movie are skipped.
- **Saving:** accepting creates the Movie if needed and adds a `Digital` MovieFormat with `FilePath`.
- **Watch folder (confirmed):** a `FileSystemWatcher` on the movie folder queues new files for review, and a full rescan runs on startup to catch changes made while the app was closed. Missing files (moved or deleted) are flagged, never auto-removed.
- **Play file:** opens the file with the OS default player (`Process.Start` with `UseShellExecute`). PC only; the phone shows the format but no play button.

## Barcode scanning

- Library: `BarcodeScanning.Native.Maui` (Google ML Kit on Android). It's fast, but **ML Kit may not decode UPC 5-digit add-ons**.
- **Spike #1 (risk):** test add-on decoding on real comics. Fallback order: ZXing.Net with `UPC_EAN_EXTENSION` enabled, then a second targeted scan of the add-on, then let the user type the issue number.
- Scanner sits behind `IBarcodeScanner`, so UI and logic are testable with a fake.

## PC ↔ phone communication

- **Discovery:** PC advertises `_mmdb._tcp` via mDNS. The phone browses for it, with manual IP entry as a fallback.
- **Pairing:** the PC shows a QR code containing `host:port`, a one-time pairing code, and the **SHA-256 fingerprint of the PC's self-signed TLS cert**. The phone scans it, pins the cert, and exchanges the code for a long-lived device token. Devices can be revoked on the PC. This avoids Android's cleartext-HTTP block without needing a real certificate.
- **API:** REST + JSON. Endpoints are versioned (`/api/v1`). DTOs live in `MMDb.Contracts`, so client and server can't drift.
- **Offline phone (confirmed):** the phone keeps a read-only cached copy of the collection and wishlist (metadata + small cover thumbnails), so "do I already own this?" works in a store. Scans made away from home are queued and sent when the PC is reachable. Sync uses `UpdatedAt`/`Version` (`GET /changes?since=`).
- **Windows firewall:** the app adds an inbound rule for its port on first run. This needs the user's consent and is limited to the private network profile only.

## Obsidian vault (one-way mirror)

```
<Vault>/
  CLAUDE.md                     schema + how to query; read by Claude
  _Index.md                     compact summary: totals + one line per film/issue/wishlist item.
                                Claude reads this first instead of opening every note.
  Movies/<Title> (<Year>).md
  Comics/<Series> (<Volume>)/<Series> #<Issue>.md
  Comics/<Series> (<Volume>)/_Series.md   links all issues
  Wishlist/<…>.md
  Attachments/covers/<id>.jpg
  Movies.base  Comics.base  Wishlist.base  (Obsidian Bases views)
```

- Each note has **YAML frontmatter** with every structured field, plus a readable body (overview, credits, cover embed, `[[links]]` to series/people). Frontmatter keys are stable and documented in the vault's `CLAUDE.md`.
- Every note includes `mmdb_id` and `managed_by: mmdb`. The app only touches notes it owns.
- Writes are atomic (temp file → rename). A "Rebuild vault" command regenerates everything from the database.
- *Feature question:* keep a `## My notes` section that the app never overwrites, so hand-written notes survive.

## Cross-cutting

- **Time:** `TimeProvider` everywhere (testable).
- **Logging:** `Microsoft.Extensions.Logging` + rolling file on the PC.
- **Config:** `appsettings.json` + user secrets for API keys; the vault path and port are set in the app's Settings screen.
- **Backups:** the database file is copied on startup, keeping the last N copies.
- **Static analysis:** nullable enabled, analyzers on, `TreatWarningsAsErrors` in `Directory.Build.props`.

## Prerequisites to install

- .NET 10 SDK (pinned by `global.json`, roll-forward to latest feature band)
- `dotnet workload install maui` (includes Android)
- Android SDK/emulator (via Visual Studio installer or Android Studio); USB debugging on the phone
- API keys: TMDB, UPCitemdb (free tier), Metron account, optionally Comic Vine

## Build order (milestones)

0. Solution skeleton, CI-style `dotnet test` script (`scripts/test.ps1`), design system tokens (`MMDb.UI/wwwroot/css/tokens.css`).
1. Domain + SQLite + movies CRUD on PC (manual entry, no lookup).
2. Metadata: TMDB search, UPC lookup; review-before-accept screen.
3. Vault writer + `CLAUDE.md` + Bases.
4. Comics (Metron/GCD/ISBN) — includes scanner add-on spike.
5. Server API + pairing + Android app with scanner.
6. Wishlist + wishlist match on scan.
6b. Digital folder import + watch folder.
7. Offline cache/queue on phone.
8. Stats screen.

Each milestone ships with its tests (see [TESTING.md](TESTING.md)).

## Open risks

1. Comic add-on barcode decoding on Android (spike early).
2. Whether Metron supports UPC lookup (check the API docs during milestone 4; fall back to GCD).
3. UPC coverage for older DVDs is spotty, so manual search must be first-class.
4. API terms: TMDB and Comic Vine are non-commercial/attribution-required. Fine for personal use; show attribution in About.
