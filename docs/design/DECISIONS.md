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

## Open
- None. Design phase complete (2026-10-08).
