# HTTP fixtures

Responses the provider tests replay through a fake `HttpMessageHandler` (see docs/TESTING.md).

| File | Where it came from |
|---|---|
| `upcitemdb/*.json` | Recorded from the live free tier (`/prod/trial/lookup`) on 2026-10-09. `too-fast.json` is the real 429 after six lookups in a minute. |
| `tmdb/invalid-key.json` | Recorded from the live API (401 without a key) on 2026-10-09. |
| `tmdb/*.json` (the rest) | Written by hand in the shape of TMDB's v3 responses (`/search/movie`, `/movie/{id}?append_to_response=credits,videos,release_dates`). Not yet recorded: no key was available. Re-record them once a key is set up and check them with the live contract tests (`dotnet test --project tests/MMDb.Infrastructure.Tests -- --explicit only`). |
