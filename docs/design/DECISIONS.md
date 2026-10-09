# Design decisions

Decided with the user. Reference mockup: [mockup-v1.html](mockup-v1.html).

## Look
- **Name:** Shelf.
- **Style:** dark media library (Letterboxd-like), cover-art grid.
- **Accent:** amber `#e8b04b`. Fonts: Instrument Serif (titles), Instrument Sans (UI), JetBrains Mono (codes), bundled in `MMDb.UI/wwwroot/fonts` so they work offline. Tokens live in `MMDb.UI/wwwroot/css/tokens.css` (from the mockup's `:root`).
- Format badges: 4K UHD (amber), Blu-ray (blue), DVD (magenta), Digital (green). Charts use darker steps of the same hues (`--c-uhd #bf8a2c`, `--c-br #3a78e0`, `--c-dvd #c05aa8`, `--c-digital #3fa36b`), which pass the dark-surface colorblind-safety check. Green is reserved for Digital, so "owned" and other states use text, not green.

## Features
- **Trailer:** a single button that opens YouTube. No in-app player.
- **No per-copy details:** no condition, price, purchase date, shelf location or edition.
- **One film, many formats:** a film owned on several formats is listed once, with a badge per format. Scanning a new format of an owned film adds the format instead of creating a duplicate.
- **Comics extras:** grouped by series with completion progress, missing issues shown (one click to wishlist), key-issue tags.
- **Scan queue** on the phone: scan many items, then review them one by one. Every field is editable before accepting.

- **Wishlist:** separate Movies and Comics tabs. Wanted format shown as a dashed badge; "Upgrade" tag when the film is owned on another format. "Got it" → review screen → moves to collection.
- **Stats:** kept. Headline tiles, discs by format, items added per month, top directors, comics by publisher.
- **Phone browsing works offline:** the phone stores a full copy of collection + wishlist, refreshed on home Wi-Fi. Offline it is read-only except scans, which queue and sync later. Search answers "do I own this?" in a store. Bottom nav: Movies, Comics, Scan, Wishlist, Stats.

- **Wishlist alert on scan:** an amber banner over the camera as soon as a scan matches the wishlist, repeated on the review screen; saving removes it from the wishlist. Also works away from home (marked "likely").
- **Digital format:** the fourth format. Digital copies store the file path; **Play file** opens it on the PC.

- **Digital import:** one movie folder (set in the import screen). Files are matched to TMDB by file name; clear matches are pre-ticked and unclear ones wait for review. Files for a film already owned on disc add Digital to it. **Watch folder** on: new files appear for review automatically.

- **Settings:** approved as mocked: phone pairing (QR + code, paired devices), vault folder + rebuild, online-database status. The movie folder for digital import lives here too.
- **Claude + vault:** Shelf itself uses no AI. The user asks questions by opening Claude Code in the vault (their subscription). The app may later pass questions to the local Claude Code program.

## Milestone 1 (decided 2026-10-09)
- **Add / edit movie (PC):** the detail page's three columns made editable (poster · fields · formats panel). One page for Add and Edit; Cancel/Save in the top bar. Save stays disabled until there is a title and at least one owned format. Edited fields get the amber outline (edit only); rejected fields a red one with the reason under it.
- **Formats panel (edit):** a row per format to switch on or off; an owned disc takes an optional UPC, a digital copy an optional file path.
- **Covers:** placeholder gradient posters (title + director) until TMDB covers arrive in milestone 2.
- **Library:** grid only for now (list view to be designed later). Sidebar shows only screens that exist; Comics, Wishlist, Stats and the phone/vault status appear in their milestones.
- **Delete:** from the detail page's "…" menu, with an inline confirmation.

## Milestone 2 (decided 2026-10-09)
- **Add movie (PC):** opens on a search step: one box takes a title (a trailing year narrows it) or a UPC, typed or from a USB barcode scanner. Matches show poster, title, year, director and runtime, and "In your collection" with the formats owned. "Not listed? Enter manually" opens the empty editor (with the typed title).
- **Review:** picking a match opens the same editor filled in, with "Best match · TMDB" and "Not it? N other matches" (back to the list; formats switched on are kept). Fields changed from the TMDB data get the amber outline. A scanned UPC switches on the format named in the product, with the UPC; if no format is named, the UPC goes to the first disc switched on.
- **Already owned:** the match says so; the editor shows the owned formats and "Saving adds 4K UHD to it", and the button reads "Add 4K UHD format". Saving adds the format to the existing film without touching its details. A UPC already in the collection links straight to the film.
- **Find on TMDB (edit):** a button under the poster searches for the film's title and year; picking a match fills in the details and cover, keeps formats, UPCs and file paths, and outlines what changed from the saved film.
- **Covers:** downloaded at w500 and kept on the PC; the phone will get small thumbnails (size decided then, ~60 MB for 4,000 at w185). Films without one keep the gradient placeholder.
- **TMDB key:** user secrets for now; the Settings screen's "Online databases" section comes later. Until a key is set, the search says how to add one and manual entry still works.

## Open
- None. Design phase complete (2026-10-08).
