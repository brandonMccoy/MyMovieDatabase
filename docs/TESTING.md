# Testing strategy

Every change ships with tests. This doc says which kind of test goes where and what makes a test good.

## Principles

1. **Test behavior, not implementation.** Assert on what a caller can observe (return values, HTTP responses, rows saved, files written, rendered markup), not on private methods or which internal calls happened. Tests should survive refactoring.
2. **Trophy-shaped suite.** Static analysis is the free base. **Integration tests are the bulk**, because most bugs live at the seams: DB mapping, HTTP, JSON, files. Unit tests cover pure logic with many edge cases. A handful of E2E tests cover critical journeys.
3. **Real where cheap, fake at the edges.** Use real SQLite, a real file system (temp dir) and a real ASP.NET pipeline. Fake only what is slow, flaky or external: internet APIs, camera, clock. Prefer hand-written **fakes** over mocking frameworks; use mocks only to verify an outbound call that *is* the behavior (e.g. "the vault writer was asked to delete this note").
4. **Deterministic.** No `Thread.Sleep`, no real clock (`TimeProvider` / `FakeTimeProvider`), no network, no shared mutable state between tests. Tests can run in parallel and in any order.
5. **One behavior per test, readable as a spec.** Structure each test as Arrange / Act / Assert. Name it after the behavior: `Scanning_a_comic_with_addon_returns_issue_number`. A failing test name should tell you what broke.
6. **Test data close to the test.** Use builders (`AMovie().WithTitle("Heat").Build()`) with sensible defaults; set only the fields the test cares about. No giant shared fixtures.
7. **Bug → failing test first.** Reproduce every bug with a test before fixing it.
8. **Coverage is a signal, not a target.** Check coverage to find untested areas. Periodically run **mutation testing** (Stryker.NET) on Domain/Application to find tests that don't actually assert anything.

## Tools

| Purpose | Tool |
|---|---|
| Test framework | **xUnit v3** on Microsoft.Testing.Platform (`global.json` sets the `dotnet test` runner) |
| Assertions | **Shouldly** (FluentAssertions v8+ requires a commercial license) |
| Snapshot/approval | In-repo `Snapshot.Match` (vault markdown): compares with `Snapshots/*.verified.*`, leaves a `.received` file on a change. Verify was the plan, but its current versions stop the build until a project declares a sponsorship or fee exemption. |
| Database | **SQLite** (in-memory with a kept-open connection, or temp file). Never the EF InMemory provider (Microsoft advises against it). |
| HTTP API | **WebApplicationFactory** (`Microsoft.AspNetCore.Mvc.Testing`) |
| External APIs | Fake `HttpMessageHandler` serving **recorded JSON fixtures** captured from the real APIs |
| Time | `Microsoft.Extensions.TimeProvider.Testing` (`FakeTimeProvider`) |
| Blazor components | **bUnit** |
| E2E | **Playwright** connected to the Windows app's WebView2 via CDP |
| Mutation | **Stryker.NET** (periodic, not every build) |
| Coverage | `coverlet` / `dotnet-coverage` |

## What gets tested where

| Layer | Test type | Examples |
|---|---|---|
| Domain | Unit (many `[Theory]` cases) | UPC/EAN check digits; ISBN detection; comic add-on parsing (issue/variant/printing); title normalization ("Heat [Blu-ray] (Widescreen)" → "Heat", format = Blu-ray); validation rules; release-name parsing for digital files (`Blade.Runner.2049.2017.2160p.UHD.mkv` → "Blade Runner 2049", 2017; names with no year; titles containing numbers, like "2001: A Space Odyssey"); wishlist match rules (full / partial / none). |
| Application | Integration (real SQLite, fake providers, fake vault/cover store) | Add movie → saved + vault note requested; duplicate detection; wishlist → owned; lookup returns wishlist match and accepting clears it; digital import over a temp folder of empty fake video files (matched, ambiguous, already-linked, missing-file cases); provider fallback chain order; lookup cache hit skips provider. |
| Infrastructure | Integration | EF migrations apply on an empty DB; round-trip every entity; each provider maps recorded JSON → candidates and handles 404, 429 (rate limit) and malformed responses; vault writer output **snapshot**; atomic write; "rebuild vault" removes orphaned managed notes but leaves unmanaged ones alone. |
| Server | Integration (WebApplicationFactory + SQLite) | Endpoints: status codes, validation errors (ProblemDetails), JSON shape snapshots; auth: unpaired device → 401; revoked token → 401; pairing code single-use and expiring. |
| Client | Integration (client against in-memory server) | Offline queue replays in order once reachable; conflict on stale `Version` surfaces to the user. |
| UI | bUnit | Review screen pre-fills candidate and allows edits before Accept; Accept disabled while required fields are invalid; empty and error states render; movies and comics lists stay separate. |
| E2E | Playwright (few) | Manual add movie with TMDB suggestion → appears in list; scan flow with fake scanner → review → accept; wishlist item → mark acquired. |
| Phone hardware | Manual checklist per release | Real camera scan of DVD, Blu-ray, comic with add-on, trade paperback ISBN; pairing via QR; offline queue. |

## Live API contract tests

Recorded fixtures can go stale if a provider changes its API. Keep a small set of `[Fact(Explicit = true)]` tests that hit the **real** APIs with real keys and compare the response shape to the fixture. Run them manually (`dotnet test --explicit only`) when a provider seems broken; they never run in the normal suite.

## Conventions

- Test project per source project: `tests/<Project>.Tests`, created when that project first has code. Shared test settings and packages live in `tests/Directory.Build.props`.
- Fixture files: `tests/<Project>.Tests/Fixtures/<provider>/<case>.json`.
- Verify snapshots are committed (`*.verified.*`); never commit `*.received.*`.
- E2E tests carry `[Trait("Category", "E2E")]`.
- CI-style check (restore, build with warnings as errors, fast suite): `./scripts/test.ps1`. Add `-SkipApp` to skip the slow MAUI build while iterating, `-Full` to include E2E.
- Raw commands: fast suite `dotnet test --filter-not-trait Category=E2E`; full `dotnet test`; one project `dotnet test --project tests/<Project>.Tests`.
- A change is done when the tests covering it pass and the whole fast suite is green.

## Sources

- Kent C. Dodds, Testing Trophy; Microsoft Learn, *Testing without your production database* (EF Core)
- xunit.net v3 docs; bunit.dev docs; ASP.NET Core integration tests (WebApplicationFactory)
- startdebugging.net, *WebApplicationFactory vs Testcontainers* (2026) on SQLite divergences
