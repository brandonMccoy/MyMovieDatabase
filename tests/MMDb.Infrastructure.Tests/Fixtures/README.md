# HTTP fixtures

Responses the provider tests replay through a fake `HttpMessageHandler` (see docs/TESTING.md).

| File | Where it came from |
|---|---|
| `upcitemdb/*.json` | Recorded from the live free tier (`/prod/trial/lookup`) on 2026-10-09. `too-fast.json` is the real 429 after six lookups in a minute. |
| `tmdb/invalid-key.json` | Recorded from the live API (401 without a key) on 2026-10-09. |
| `tmdb/search-parasite-2019.json` | Recorded from the live API (`/search/movie?query=parasite&year=2019`) on 2026-10-10, cut down to its first three results (of four). The third has no release date and no poster. |
| `tmdb/*.json` (the rest) | Written by hand in the shape of TMDB's v3 responses (`/search/movie`, `/movie/{id}?append_to_response=credits,videos,release_dates`). Not yet recorded: no key was available. Re-record them once a key is set up and check them with the live contract tests (`dotnet test --project tests/MMDb.Infrastructure.Tests -- --explicit only`). |
| `openlibrary/*.json` | Recorded from the live Books API (`/api/books?bibkeys=ISBN:…&jscmd=data&format=json`) on 2026-10-09. `unknown.json` is the real answer for an ISBN it doesn't know. |
| `metron/no-token.json` | Recorded from the live API (401 without a token) on 2026-10-09. |
| `metron/issue-8003-saga-7.json`, `metron/issue-52779-saga-tpb-7.json`, `metron/search-saga-2012-7.json` | Recorded from the live API (signed in, through the browser) on 2026-10-09. |
| `metron/*.json` (the rest) | Written by hand in the shape of Metron's responses (`/api/issue/?…`, `/api/issue/{id}/`, `/api/series/{id}/`), following the Metron server and Mokkari client source. Not yet recorded: no token was available. Re-record them once a token is set up (`dotnet user-secrets set Metron:Token <token> --project src/MMDb.App`) and check them with the live contract tests. |
