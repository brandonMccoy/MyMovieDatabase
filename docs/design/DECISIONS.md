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
- **Phone browsing works offline:** the phone stores a full copy of collection + wishlist, refreshed on home Wi-Fi. Offline it is read-only except scans, which queue and sync later (widened in milestone 7: the phone looks things up and saves by itself; see below). Search answers "do I own this?" in a store. Bottom nav: Movies, Comics, Scan, Wishlist, Stats.

- **Wishlist alert on scan:** an amber banner over the camera as soon as a scan matches the wishlist, repeated on the review screen; saving removes it from the wishlist. Also works away from home (marked "likely").
- **Digital format:** the fourth format. Digital copies store the file path; **Play file** opens it on the PC.

- **Digital import:** one movie folder (set in the import screen). Files are matched to TMDB by file name; clear matches are pre-ticked and unclear ones wait for review. Files for a film already owned on disc add Digital to it. **Watch folder** on: new files appear for review automatically. (Built in milestone 6b; see below.)

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
- **Review:** picking a match opens the same editor filled in, with "Best match · TMDB" and "Not it? N other matches" (back to the list; formats switched on are kept). Fields changed from the TMDB data get the amber outline. A scanned UPC switches on the format named in the product, with the UPC; if no format is named, the UPC is not kept (there is no telling which disc it is for; changed 2026-10-10, it used to go to the first disc switched on).
- **After a UPC (scanned or typed):** the search box holds the title read from the product name, or is blank when the barcode database has no name for it. The UPC and its format are held while searching on by title ("UPC … · kept for Blu-ray") and go on the match picked.
- **Already owned:** the match says so; the editor shows the owned formats and "Saving adds 4K UHD to it", and the button reads "Add 4K UHD format". Saving adds the format to the existing film without touching its details. A UPC already in the collection links straight to the film.
- **Find on TMDB (edit):** a button under the poster searches for the film's title and year; picking a match fills in the details and cover, keeps formats, UPCs and file paths, and outlines what changed from the saved film.
- **Covers:** downloaded at w500 and kept on the PC; the phone will get small thumbnails (size decided then, ~60 MB for 4,000 at w185). Films without one keep the gradient placeholder.
- **TMDB key:** user secrets for now; the Settings screen's "Online databases" section comes later (it came on 2026-10-11, see *Online databases*). Until a key is set, the search says how to add one and manual entry still works.

## Milestone 3 (decided 2026-10-09)
- **Vault folder:** set in Settings, which for now has only its Obsidian vault section as mocked (folder + Change, last sync + note count, Rebuild vault). The sidebar footer shows the vault's state ("Vault synced · just now") and the Settings link. A film's page shows its note (`Movies/Heat (1995).md`), which opens it in Obsidian.
- **My notes:** kept. Text under `## My notes` survives saves and rebuilds; a removed film's note goes to the vault's `.trash/` if it has some.
- **Covers:** copied into `Attachments/covers`; notes embed them and `Movies.base` has a cover card view.
- **Links:** directors and cast are `[[links]]` in the note body (people notes aren't created); frontmatter keeps plain names.
- **Status colors:** states are text, not green (green stays reserved for Digital); a failed write is amber.

## Milestone 4 (decided 2026-10-09)
- **Sources:** issues from Metron (API token in user secrets), trades from Open Library by ISBN. GCD and Comic Vine dropped: neither can search by barcode.
- **Add-on scan spike:** moved to milestone 5 with the phone scanner. On the PC the search box takes a 17-digit UPC (USB scanner or typed) or the 12-digit UPC followed by the issue number; add-on decoding is in Domain.
- **Add comic:** one box like Add movie: text ("saga 7", "Daredevil 1964 181"), a UPC or a trade's ISBN. Matches show cover, series, issue, variant, publisher, cover date and "In your collection" with the variants owned; picking one opens the editor filled in. "Not listed? Enter manually".
- **Variants:** each copy is its own entry (`#7 · Cover B`); a series' progress counts the issue once. The same copy saved twice (same UPC, ISBN, or Metron issue + variant) is turned down as already owned.
- **Library:** as mocked. Series groups with "N of M" progress from Metron's issue list and striped tiles for the issues missing between the first and last owned, one tile per gap ("#159–167"; their "+ Wishlist" comes with the wishlist in milestone 6; while searching, gaps aren't shown); chips Series / Issues / Trades & HCs / Key issues; publisher filter; sort Series A–Z or Recently added. Runs Metron doesn't know show "N issues" and no gaps.
- **Trades, HCs, omnibuses:** their own shelf under "Trades & HCs" (cover grid) with an optional series and volume ("Saga Vol. 1"); they don't count toward a series' progress.
- **Key issues:** set by hand in the editor (switch + short note such as "1st Elektra", shown under the issue number). Nothing is tagged automatically.
- **Issue page:** laid out like a film's page: cover · fields (series, publisher, cover date, story title, writers, artists, summary) · panels for the copy owned (format, variant, printing, UPC/ISBN, added) and the series' progress.
- **Vault:** `Comics/<Series> (<Year>)/<Series> <Issue>.md` (no `#`: Obsidian links can't hold it; the heading keeps it) like film notes (writers and artists `[[linked]]`, a link to the series note), `_Series.md` listing owned and missing issues, and `Comics.base` with Issues, Series and Key issues views.

## Milestone 5 (decided 2026-10-09)
- **Phone scope:** browse, open, scan and add. Editing and deleting saved items stays on the PC (a film's or comic's page on the phone has no Edit or Delete). The phone needs the PC on the same Wi-Fi until the offline copy in milestone 7; without it the phone shows "Can't reach <PC>" with Try again.
- **Test device:** emulator only for now. The emulator turned out not to run on the dev PC (no hypervisor driver), so the Android app is built and its screens are tested, but it has not run on a device; the add-on scan spike waits for a phone.
- **"Scan with phone" button on the PC:** left out. Scanning starts on the phone; what the phone saves appears in the PC's open library and counts by itself.
- **Bottom bar:** Movies · Scan · Comics for now, the scan button in the middle. With Wishlist and Stats it becomes the mocked Movies, Comics, Scan, Wishlist, Stats.
- **Browse:** as mocked: title, "Do I own…?" search, format chips (comics: shelf chips), three covers across. No genre, decade, publisher or sort controls on the phone. The "…" opens *This phone* (which PC it is paired with, Forget this PC).
- **Film and comic pages:** the PC's page in one column, read-only, trailer button kept.
- **Scan:** as mocked: Movie / Comic / Auto switch (Auto by default), reticle, "N scanned · review", close and keyboard buttons. A banner at the top shows the last scan (looking up, the title found, not found, already owned, already scanned); tapping it reviews that scan. The wishlist alert takes the banner's place in milestone 6.
- **Review:** the PC's editor in one column, opened on the best match with the scanned barcode and format filled in; "Review · 1 of 3" and Remove in the top bar; the save button at the bottom. After saving, the next scan opens; after the last, the library. A scan that found nothing opens the search step, with "Look it up as a comic" / "as a film" for a code that was taken for the wrong kind. The heart (send to wishlist) comes with the wishlist.
- **Keyboard button:** opens the same Add screen as the PC (movie or comic by the switch), with a link over to the other kind.
- **Pairing on the phone:** "Pair with your PC": Scan the code, or type the PC's address and the six-digit code shown under the QR code.
- **Settings → Phone (PC):** as mocked: QR code, code with countdown, PC address, paired phones with Connected / Last seen and Unpair (no confirmation; pairing again takes seconds). "Connected" is text with a neutral dot, not green.

## Cast and crew (decided 2026-10-10)
- **Vault cast:** film notes name the actors only, not the parts they play (the app still shows the parts).
- **Everything TMDB credits is kept:** the whole cast and the whole crew come with a lookup, whenever a film's details are fetched. No bulk "fetch crew" button; crew isn't edited by hand (the editor says how many credits are kept; directors stay editable).
- **Film page:** the Crew tab lists everyone by department (Directing, Writing, Production, Camera, Editing, Sound, Art, …), each person once with their jobs. Search finds crew names too.
- **Editor:** a long cast shows the first ten rows until "Show all N cast members" is pressed.
- **Vault note:** everyone in the cast and crew is a `[[link]]` (people get no notes of their own). Writers, producers, executive producers, cinematographers, composers and editors have a line each under the directors and a frontmatter list; the rest of the crew is under `## Crew`, a line per job title. Frontmatter `cast` keeps the ten top-billed. Measured on 1,990 films (419,000 links to 176,000 people) in Obsidian 1.13.7: reopening the vault 4.5 s instead of 2.5 s, local graphs smooth, the global graph unusable (about 7 fps). Accepted: the global graph isn't used.
- **Performance rule:** code for performance; don't load, fetch or store what a screen or feature doesn't use. The library list, search and the vault's index read a slim listing of each film without its credits; who is in the credits is searched in the database.

## Milestone 6 (decided 2026-10-10)
- **Wishlist item:** the film or comic, what is wanted and when it was added. No priority, price or notes.
- **One wanted format per film:** a film is on the wishlist once, for one format (the mockup's two dashed badges on one row are dropped).
- **Adding:** "Add to wishlist" uses the same search as Add movie / Add comic; picking a match asks only for the wanted format. Also the heart on the review screen (a scan goes to the wishlist instead of the collection) and "+ Wishlist" on a series' missing tiles.
- **Missing issues:** "+ Wishlist" on a gap tile ("#159–167") adds every issue of the gap, each as an item of its own, so each can be bought, matched on a scan or dropped by itself. (First built as one item per gap; changed 2026-10-10 after trying it.)
- **Got it:** opens the normal review screen with the wanted format switched on; saving moves it to the collection.
- **Match on scan:** as mocked, the amber banner over the camera and on the review screen. A film scanned on another format than the wanted one asks whether to keep it on the wishlist.
- **Phone:** everything the PC does: the Wishlist tab (browse, search), Add to wishlist by search, Got it, remove, the heart on the review screen.
- **Vault:** `Wishlist/` notes and `Wishlist.base`.
- **Later:** the "likely" match away from home comes with the offline copy (milestone 7).

Settled while building it (2026-10-10):
- **Wishlist screen:** rows as mocked, plus an × to remove an item at once (no confirmation: wanting it again takes seconds; changed 2026-10-11, see milestone 9: it asks first). The count reads "N items". "Add to wishlist" adds a film on the Movies tab and a comic on the Comics tab.
- **Comics need no format step:** a comic picked from the search goes straight on the wishlist.
- **The heart:** on the phone it sits left of the save button, as mocked; on the PC it is "To wishlist" in the top bar. For a film it asks for the format (the scanned disc's format is suggested).
- **Another format than wanted:** the banner on the review screen has "Keep it on the wishlist for 4K UHD", switched on; switching it off removes the wish when saving.
- **Missing tiles:** "On wishlist" in place of "+ Wishlist" once wanted; a tile splits where the wishlist starts or stops. The issues added get their covers from Metron's list of the series' issues.
- **Item pages:** a wishlist row's cover and title open the item's own page, laid out like a film's or a comic's page and read-only (the missing tiles in a series don't link anywhere). It shows what TMDB or Metron knows (overview, cast and crew; story, creators, summary), fetched when the page opens. A film's page has a "Format wanted" panel where the format can be changed; formats already owned can't be chosen.
- **Late covers:** each time the PC starts, covers the wishlist is still missing are tried for again.
- **Not built:** the mockup's "Key issue" tag on a wishlist row (key issues are marked by hand on comics owned; a wish has no editor).

## Milestone 6b (decided 2026-10-10)
Screens as built: [m6b-preview](m6b-preview).
- **Getting there:** from Settings only. Settings has a "Digital movies" section (movie folder + Change, the watch switch, "N video files · N new · N missing", **Review files**). The sidebar's footer shows "N new files" while files wait, and that line opens the import screen. No sidebar entry and no button on the Movies screen. The import screen's back button reads "Settings".
- **Import screen:** as mocked (folder row, watch switch, the four counts, chips All / Needs review / Matched, a row per file with tick box, file name, film, state and action; "Add N movies" in the top bar). Each row also has **Skip**.
- **Files you don't want:** Skip takes a file off the list and it stays away on later scans. A "Skipped" chip (shown once something is skipped) lists them, each with "Bring back".
- **Second file for a film:** a film keeps one file. A file for a film that already has one reads "you already have a file for this film", can't be ticked, and offers "Use this file instead" or Skip.
- **Missing files:** the film's page says "File not found" in place of Play file. A "Missing" chip on the import screen (shown when there are any) lists those films, each with "Choose file" and "Remove Digital copy". A file moved inside the folder that keeps its name is linked again without asking.

Settled while building it (2026-10-10):
- **Picking a match:** "Pick match", "Change" and "Search…" open a picker under the row: a search box holding the title read from the file name, and the films found (poster, title, year). Picking one ticks the file.
- **Under the film's title:** the year, then what adding does: "adds Digital to your Blu-ray and DVD", "adds the file to your Digital copy", "you already have a file for this film", "same film as <the other file>". A guess with several candidates reads "N possible matches" instead.
- **Two new files for one film:** the first is ticked; the second says "same film as …" and is left unticked ("Check this").
- **Use this file instead:** the file the film had is skipped at once, so it doesn't come back asking the same question.
- **Remove Digital copy:** removes the Digital format. For a film owned only digitally that deletes the film, so it asks first, like Delete on a film's page.
- **Matched means:** the name's title and year are one film's (a year either side counts: TMDB dates some films by a festival). When several films share the title and year (TMDB lists shorts named like well-known films, e.g. two "Arrival" from 2016), TMDB's first is taken. Found on the first real run.
- **Nothing reviewed twice:** a bulk import doesn't open the editor per film; films come in as TMDB has them and can be edited afterwards.
- **Play file:** a button under the file's path in "Formats owned" (the mockup had it on the badge's row, where it crowds out "Added Oct 2026" in the narrow panel). Only video files are opened. If the PC has no player for the file it says so.
- **TMDB is only asked on the import screen:** the scan at startup and the watcher only count files, so the "N new files" line costs no lookups.
- **Not built:** hiding samples or extras by themselves (they show up once and are skipped by hand); several files per film; a sidebar line for missing files.

## Milestone 8 (decided 2026-10-10)
Built before milestone 7 (the phone's offline copy), which stays last. Screens as built: [m8-preview](m8-preview).
- **Screen:** as mocked: six tiles (Films, Discs, Digital, Comic issues, Series, Wishlist), then the charts. In the sidebar under Wishlist.
- **Films by decade:** added. The whole collection was imported in one month, so "Items added per month" is one tall bar for a year; films by decade of release has something to say from the first day. The two share a row, under "Copies by format"; "Top directors" and "Comics by publisher" share the next.
- **PC only for now:** the phone's Stats tab (the fifth in the mocked bottom bar) comes with the offline copy in milestone 7.
- **Digital tile:** "1.9 TB on this PC" is the size of the films' video files in the movie folder. Sizes are read while the folder is listed anyway (startup, the watcher), never by a pass of their own.
- **Bars open the library:** a format opens Movies on that format, a decade on that decade, a director on that director's films, a publisher opens Comics on that publisher's issues. A month and "Other" open nothing. The hover tip with the exact number is kept.

Settled while building it (2026-10-10):
- **Director filter:** Movies takes a director from the address (`movies?director=…`) and shows a "Directed by …" chip in the filter row; clicking the chip clears it. It matches the director's whole name, so a bar's number and the list it opens agree (the search box would also find the films a director only produced or wrote). There is no control to pick a director in the library itself.
- **What counts:** "Items added" counts copies: each format of a film in the month it was added, and each comic copy. "+N this month" counts films (and single issues) new to the collection this calendar month. "N films on 2+ formats" counts Digital as a format. Months begin by the PC's clock.
- **Comics:** the tile and the publisher chart count single issues, as mocked ("Issues owned"); trades and hardcovers get a line of their own under the tile ("3 trades & HCs"). A complete run is one with every issue Metron lists, the library's "12 of 12". Up to six publishers are listed; with more, the five biggest and "Other" (which also takes issues without a publisher).
- **Top directors:** six, ties A–Z; a film with two directors counts for both.
- **Tips:** drawn in CSS over the bar (the mockup's followed the mouse with script).
- **Axis numbers:** in a margin right of the bars (the mockup had them over the plot, where a tall last bar covers them). The top of a chart is a round, even number at or above the tallest bar, with a second line halfway.
- **Sidebar:** the Movies link no longer lights up on Wishlist and Stats as well (it lit on everything but Comics and Settings).
- **Empty:** with nothing in the collection or on the wishlist the screen says "Nothing to count yet"; a chart with nothing to draw says so in a line.
- **Not built:** the mockup's note that every chart could offer a table view; clickable tiles.

## Milestone 7 (decided 2026-10-10)
The phone becomes an app in its own right, with the PC still the one true copy. This replaces "read-only except scans" above and milestone 5's "the phone needs the PC".
- **Same access as the PC:** the phone looks films and comics up itself (TMDB, UPCitemdb, Metron, Open Library), on Wi-Fi or mobile data. Scan, Add, Add to wishlist and Got it work anywhere there is internet, with the usual search and review screens.
- **Keys:** the PC hands its TMDB key and Metron token to a phone when it pairs and again at each sync. The phone keeps them in Android's keystore and drops them on "Forget this PC" or once the PC says it is unpaired. They are still never in the repo or built into the app. (Changes the rule that keys never reach the phone.)
- **Its own copy:** the phone keeps the whole collection and wishlist, with covers, and its screens always show that copy, at home too.
- **Changes away from home:** what is saved on the phone shows there at once and is sent to the PC, in order, when it is next reachable. Removing a wish and changing a wanted format need no lookup, so they also work with no signal.
- **No signal at all:** browse, search and Stats work; scans wait in the queue (which now survives closing the app) and are looked up when there is internet again; anything else that needs a lookup waits.
- **Still PC only:** editing and deleting saved items. The exception: something added on the phone and not sent yet can be removed there (it exists only on the phone until then).
- **When the PC disagrees:** the PC's rules decide as each change arrives. A film it already has gets the format added. A change that leaves things as they are (the format or copy is already owned, the wish is already gone) is dropped without a word. Anything else is shown once on the phone: "1 change couldn't be sent", with what it was.
- **Syncing:** when the app opens, when it comes back to the front, when the phone's network changes (Wi-Fi comes back while Shelf is on screen; added 2026-10-11 after the first run on the phone, where nothing happened on reconnecting), after each save at home, and "Sync now" under This phone.
- **Covers on the phone:** medium copies (about 340 px wide, roughly 140 MB for 4,000 films), made by the PC. Something saved on the phone fetches its own cover.
- **The grey line** under the title, as mocked, in two wordings: "Away from your PC · collection as of 8:12 AM" and "Away from your PC · **3 changes** to send". Nothing is shown at home with nothing to send.
- **One search:** "Do I own…?" lists films, comics and wishlist items together, as mocked: Owned with format badges, On wishlist, "4 of 12" for a series. The mockup's "Not owned" rows are left out; a search that finds nothing offers "Look it up", which opens Add with the text typed.
- **Stats:** the fifth tab, the PC's screen in one column, without the "on this PC" size line.
- **"Can't reach <PC>":** only before the first sync; after that the phone always has something to show.

Settled while building it (2026-10-10):
- **First time:** after pairing the phone shows "Copying your collection" until it has the films, comics and wishlist; covers follow in the background, and a page that needs one sooner fetches it.
- **The grey line** sits under the title on Movies, Comics, Wishlist and Stats. At home it shows nothing, also while changes are on their way. The time reads "today 8:12 AM", "yesterday 6:40 PM", then the date.
- **What the PC wouldn't take** is shown in the same place, with OK to dismiss it: "1 change couldn't be sent: Dune (2021) on the wishlist".
- **This phone** (the "…" on a tab) gets "Your collection on this phone": as of when, how many changes are waiting, and **Sync now**.
- **Removing something not sent yet:** its page has **Remove** where the PC has Edit and Delete, and asks first. A format added on the phone to a film the PC already has is not the phone's to remove. A wish is taken off with the × as always; if it was never sent, nothing is.
- **One search** is on the Movies and Comics tabs (the Wishlist tab keeps its own). A film or book row opens its page; a series row opens the issues owned that match, where a single issue is answered for. Finding nothing reads "Not on your shelf" with **Look it up**, which opens Add movie or Add comic with the words typed and searches at once.
- **Large covers:** a film, comic or wishlist item on its own page asks the PC for the full cover while the PC is in reach (and keeps it); away, the medium copy is shown.
- **Scans with no signal** read "Couldn't look it up" and are tried again whenever the scan or review screen is opened. A barcode already in the collection is answered without signal.
- **The "likely" wishlist match** by product name was dropped: the phone does the real lookup itself.
- **Forgetting the PC** keeps the copy and what is waiting, so pairing again (the way to follow a PC that got a new address) sends it; pairing with another PC replaces the copy with that PC's at the first sync.
- **Different versions:** if Shelf on the phone and on the PC keep the collection differently (one was updated without the other), they don't sync and the phone says to update both.
- **Not built:** progress for the covers; telling apart "no signal" from "the PC is off" in the grey line (it speaks of the PC only).

## Milestone 9 (decided 2026-10-11)
- **Amazon button:** every wishlist item links to a search of amazon.com for it, opened in the browser (or the Amazon app on the phone), like the trailer. Shelf asks Amazon nothing itself: no prices, no availability.
- **Where:** a small Amazon icon button on each wishlist row, after the trailer and before Got it, and **Find on Amazon** on the item's own page. PC and phone.
- **Films:** title, year and the wanted format, "Heat 1995 4K UHD", in Movies & TV. The year keeps films of the same name apart.
- **Digital:** a film wanted on Digital has no button (there is no disc to look for).

- **Removing asks first** (asked for 2026-10-11, with this milestone): the × on a wishlist row and "Remove from wishlist" on an item's page show "Remove Heat from your wishlist?" with Cancel and Remove, in place, like deleting a film. One question at a time: asking about another row moves it.

Settled while building it:
- **Comics:** an issue is searched for as "Saga #7 comic" across all of Amazon (the word keeps other things named like the series out); a trade or hardcover by its name alone, "Saga Vol. 1". No format in a comic's search.
- **The icon** is Amazon's own mark (the "a" with the smile), taken from Bootstrap Icons (MIT licence): the letter in the text colour, the smile in Amazon's orange. A first version drawn for Shelf was replaced the same day.
- **Not built:** other shops; the link in the vault's wishlist notes; choosing another Amazon site than amazon.com.

## Online databases (decided 2026-10-11)
So that someone else installing Shelf can use their own keys ("set api keys manually").
- **Where:** Settings → Online databases, as mocked: TMDB, UPCitemdb ("Free tier (no key)", nothing to set) and Metron, each key shown as dots and its last four characters.
- **Editing:** **Add key** / **Add token** / **Change** turns the row into a box to paste into, with Save and Cancel (Enter and Esc too) and, under it, where to get one and **Remove key**. One row at a time. The key is never shown again in full.
- **Kept:** in a file beside the database, encrypted by Windows for the account, not in `shelf.db`. Another PC or account means typing the keys in again.
- **Checked:** on Save and every time Settings opens. Turned down on Save: "TMDB turned this key down, so it wasn't saved." and the box stays open. Can't reach the service: kept, "Not checked: couldn't reach TMDB". States are words, not colours; a key turned down is amber.
- **At once:** no restart; a paired phone gets the change on its next sync.

Settled while building it:
- **The line under the heading** says the keys are kept on this PC "and on the phones you pair" (the mockup's "only on this PC" was from before the phone looked things up itself).
- **Metron takes a token**, not the mockup's username and password.
- **"Add your key in Settings"** replaces the `dotnet user-secrets` command in the Add screens' messages: a link on the PC, "Settings on the PC" on the phone.
- **User secrets** still work for a developer until a key is saved in Settings.
- **Not built:** removing a key doesn't ask first (pasting it back undoes it); no key for UPCitemdb's paid tier; changing keys from the phone.

## Search box focus (decided 2026-10-11)
"Add item search input focus": the add screens open ready to type.
- **Where:** the search box of Add movie, Add comic and both Add to wishlist screens (they share the box), each time the search step opens, coming back from the review screen included.
- **PC:** the cursor is always put in the box, over a search already made too.
- **Phone:** the cursor, and with it the keyboard, only when the box is empty. Arriving with something to search for ("Do I own…?" → Add) leaves the keyboard down so the matches show.
- **How:** set from code when the box first appears. The `autofocus` attribute it replaces only works when a page is loaded, not when moving between screens, and the page heading took the focus afterwards. Android's web view brings the keyboard up by itself once the box has the cursor (checked on the S24, 2026-10-11), so there is no native keyboard code.

## Stats: months apart, top actors (decided 2026-10-11)
"Stats page updates (Top actors, separate movies and comics)".
- **Two month charts:** "Items added per month" became "Movies added per month" and "Comics added per month", each with its own scale, so a month of comics no longer flattens the films. Chosen over one chart with two bars a month or stacked bars. Both still count copies: each format of a film, and every comic copy (issues, trades and hardcovers).
- **Top actors:** a seventh chart, six names like "Top directors", counting films owned.
- **Who counts as an actor:** the first 10 names of a film's cast. Every credit is kept, so counting whole casts would fill the list with bit-part and voice actors. Someone a film names twice counts once.
- **An actor's bar opens Movies on that actor:** a "With <name>" chip that clears like "Directed by". It lists exactly the films the bar counted (the same first 10, the whole name, ignoring case and accents). Like the director filter it is only reached from Stats.
- **Rows:** "Copies by format" across the top, then the two month charts, "Films by decade" with "Top directors", "Top actors" with "Comics by publisher".
- **Cost:** the first names are picked out of each cast by SQLite, so the casts are still never loaded: 17 ms for the real collection of 1,204 films (3.2 MB of cast), once per Stats screen and once per list while an actor chip is on.

## Obsidian vault on or off (2026-10-11)
"Obsidian vault use selection" (issue 5).
- **A switch:** the Obsidian vault section of Settings starts with "Use a vault", a switch like "Watch for new files". Off by default; a vault already set up stays on.
- **Off:** the "Vault folder" and "Last sync" rows are hidden, the sidebar has no vault line, and a film's or comic's page has no note link. Nothing more is written.
- **Nothing is deleted:** turning it off leaves the folder and every note in it, and Shelf remembers the folder. Turning it back on brings the same folder up to date with what changed meanwhile.
- **On without a folder:** the "Vault folder" row offers "Choose folder" and the sidebar says "Vault not set up", as before.

## Types in use (2026-10-11)
"Type in use selection" (issue 6): Shelf can be told which types of collection it tracks.
- **Where:** Settings starts with a **Collections** section, one switch per type (Movies, Comics; more types later get a row each). Switches rather than the checkboxes the issue named, to match "Use a vault" and "Watch for new files". All on by default.
- **Off means no trace:** no sidebar link or count, no tab on the phone, its wishlist items, Stats tiles and charts, its "Digital movies" and Online databases rows in Settings, and no link over to it from the other type's Add screen. Its addresses open the first type in use; with Movies off Shelf opens on Comics.
- **Nothing is deleted:** the films, comics and wishlist items of a type that is off stay in the database and come back, unchanged, when it is turned on again.
- **At least one:** the last type in use can't be turned off. Its switch is greyed with "Shelf shows at least one type" beside it.
- **With one type in use:** the wishlist has no Movies/Comics tabs, Stats' Wishlist tile has no "N movie · N comic" line, the scan screen has no Movie/Comic/Auto switch (every barcode is that type's), and the search box of Stats and Settings searches that type.
- **The phone follows the PC:** it has no switches of its own; it learns the choice with each sync and keeps it for when the PC is away. Its bottom bar has four places then: the type, Scan, Wishlist, Stats.
- **"Do I own…?" answers for the page it is typed on:** on Movies it lists films owned and wanted, on Comics comics owned and wanted. This replaces "One search" (milestone 7), which listed both together; the "Movies" and "Comics" headings over the groups went with it, the page already saying which it is.
- **The vault is left as it is:** it keeps mirroring everything in the database, whatever is in use. Chosen over freezing a type's notes or removing them (which would lose anything typed into them).
- **Not built:** a phone with a choice of its own; hiding a type's notes from the vault; stopping the movie folder being watched while Movies is off (the files found meanwhile wait until it is back on).

## Open
- ~~Add-on scan spike~~: done on the phone, 2026-10-10 (see ARCHITECTURE.md, *Barcode scanning*). In Auto and Comic mode a barcode without an add-on is reported 0.6 s later, in case a later frame shows one.
- mDNS discovery: still to do, now that there is a real phone.
- ~~A scan queue that survives closing the app~~: part of milestone 7.
