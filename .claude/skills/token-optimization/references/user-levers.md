# User-side levers for reducing token usage

Settings and commands only the user can change. Recommend the ones relevant to their situation; don't dump the whole list.

## Commands
- `/context` — see what's loaded at session start (CLAUDE.md, MCP tools, skills). Run in a fresh session.
- `/clear` — between unrelated tasks. `/rename` first to resume later.
- `/compact <instructions>` — after a phase finishes or before a break (while cache is warm). Add a "Compact instructions" section to CLAUDE.md for consistent results.
- `/rewind` — drop recent turns cheaply; earlier turns stay cached.
- `/mcp` — disable servers not needed for the current task.
- `/model`, `/effort`, fast mode — pick at session start; changing mid-session invalidates the prompt cache.
- `/autocompact 200k` — on 1M-context models, compact earlier instead of letting context grow huge.
- `/cost` (API users) or `/usage` — check spend.

## Model and effort
- Smaller model / lower effort for routine edits, renames, boilerplate; bigger model / higher effort for ambiguous design or hard debugging.
- `MAX_THINKING_TOKENS=0` for pure grunt-work sessions.
- Subagent definitions in `.claude/agents/*.md` can set `model: haiku` or `model: sonnet` for repetitive jobs (search, log triage). `CLAUDE_CODE_SUBAGENT_MODEL` sets a default.

## Environment variables
- `ENABLE_PROMPT_CACHING_1H=1` — one-hour cache lifetime (helps long sessions with pauses).
- `BASH_MAX_OUTPUT_LENGTH` — threshold at which command output spills to a file instead of context.
- `CLAUDE_AUTOCOMPACT_PCT_OVERRIDE` — trigger auto-compaction earlier (e.g. `50`).

## CLAUDE.md
- Loaded every turn: keep it to one or two pages of stable facts (build/test commands, conventions, no-touch dirs).
- Move long, task-specific workflows into skills (only name + description load until used).
- List quiet versions of common commands so Claude uses them by default.
- Keep it current; stale instructions cause correction round-trips.

## Hooks
- A PreToolUse hook can rewrite noisy commands (e.g. append `| tail -n 100`, add `--quiet`) so only relevant lines return.

## Habits
- `@`-mention known files so Claude skips searching; don't re-mention a file already in the conversation (attaches a second copy).
- Name specific files, functions, and targets in requests.
- Run recurring `/loop` jobs in a separate session, not the main one.

Sources: Anthropic, "Maximizing the value of your Claude Code sessions" (claude.com/blog); Claude Code docs, Costs (code.claude.com/docs/en/costs); community guides (composio.dev, kdnuggets.com, claudelog.com).
