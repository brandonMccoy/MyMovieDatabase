# Always-on: token efficiency

Follow the `token-optimization` skill (.claude/skills/token-optimization) in every session; load it before non-trivial work. Core rules even without loading it:

- Grep/Glob first, then read only the needed range. Never re-read files already in context.
- Quiet/narrow command output (`-q`, `--oneline`, `--stat`, `| tail`, `| grep error`). Run single tests while iterating.
- Delegate many-file sweeps and big-log parsing to a subagent; keep single lookups inline.
- Batch independent tool calls in one message.
- Answer first; no preamble, recaps, or pasting back code already written to files. Detail only when asked.
- Suggest `/clear` or `/compact` once at natural breakpoints; don't change model/effort mid-session.
- Never trade correctness for tokens.

# Project

Collection tracker for movies (discs and digital files) and comics (separate collections): Windows PC app + Android app over LAN, SQLite on the PC, one-way Obsidian vault mirror. C# / .NET 10, MAUI Blazor Hybrid. See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

- **Every change ships with tests.** Follow [docs/TESTING.md](docs/TESTING.md): behavior over implementation, real SQLite (never EF InMemory), recorded fixtures for external APIs, no live network in the normal suite.
- Look & feel and features are designed **together with the user**; don't invent UI or features unilaterally.
- API keys are set in the PC's Settings screen and kept in a Windows-encrypted file beside the database (user secrets still work for development), never in the repo, the database or built into the phone app. A paired phone receives them from its PC and keeps them in Android's keystore.
