---
name: token-optimization
description: Token-efficient working habits for every Claude Code session — scoped reads and searches, quiet commands, subagents for noisy work, short replies, cache-friendly behavior. Use this skill whenever starting non-trivial work (multi-file changes, debugging, exploring an unfamiliar codebase, long-running sessions), whenever context is growing large, and whenever the user mentions tokens, cost, usage limits, context window, /compact, /clear, CLAUDE.md size, or "why is this so slow/expensive".
---

# Token Optimization

Every token in context is re-sent on every turn until the session is cleared or compacted. A 2,000-line file read "just in case" is paid for again on each later turn. So the goal is: **get the same quality answer with the least context**, not to cut corners on correctness. If saving tokens would risk a wrong answer, spend the tokens.

## 1. Reading and searching

- **Search before reading.** Use Grep/Glob to locate the symbol or file, then read only the relevant range with `offset`/`limit`. Read a whole file only when it is small or you truly need all of it.
- **Never re-read what's already in context.** If you read or edited a file this session, work from what you have. Edit/Write would have errored if a change failed, so don't re-read to "verify".
- **Use `files_with_matches` or `count` modes first** for broad searches; switch to `content` with `-C` context only on the hits that matter. Set `head_limit` on searches that could explode.
- **Go straight to named targets.** When the user names a file, function, or line, open that — don't survey the repo first.
- **Skip generated/vendor dirs** (`node_modules`, `bin`, `obj`, `dist`, `Library`, `.git`, lockfiles, minified files) unless the task is about them.

## 2. Commands and tool output

Command output stays in context for the rest of the session, so trim it at the source:

- Use quiet/narrow flags: `git status -s`, `git log --oneline -n 20`, `git diff --stat` before a full diff, `npm test -- --silent`, `dotnet build -v q`, `pytest -q`, `ls` over `ls -la` when sizes don't matter.
- Pipe long output through `| tail -n 50`, `| head`, or `| grep -E "error|fail|warn"` when you only need the failures.
- Run the single relevant test, not the whole suite, while iterating; run the full suite once at the end.
- Avoid dumping large JSON/HTML/logs; extract the field you need (`jq`, `grep`, `Select-String`).

## 3. Delegate noisy work to subagents

A subagent's file dumps and logs stay in *its* context; only its summary comes back. Use one (Explore for read-only sweeps) when the task means reading many files or parsing big logs and you only need the conclusion. Don't spawn a subagent for a single known-file lookup — that costs more than doing it directly. Give the subagent a precise question and ask for a short answer.

## 4. Parallelize, don't serialize

Batch independent tool calls into one message (several Greps, several Reads, a build plus a lint). Fewer round-trips means less repeated context.

## 5. Writing replies and code

- Lead with the answer or the result. No preamble ("Great question…"), no recap of what the user just said, no restating the plan you're about to execute.
- Don't paste back large code blocks you just wrote to a file — reference `file:line` instead.
- Summaries at the end of a task: a few lines on what changed and anything the user must know (failures, skipped steps, decisions). Not a changelog of every tool call.
- Use Edit for targeted changes rather than rewriting whole files with Write.
- Match comment density to the surrounding code; don't add narrating comments.
- If the user explicitly asks for detail, a full explanation, or verbose output, give it — this skill sets defaults, not limits.

## 6. Keep the prompt cache warm

Cached prefix tokens are far cheaper than fresh ones. Things that break the cache: switching model, effort level, or fast mode mid-session; editing CLAUDE.md or MCP config mid-session. Suggest those changes at the start of a new session instead of mid-task.

## 7. Session hygiene (suggest to the user when it fits)

You can't run these slash commands yourself, but you can recommend them at natural breakpoints, briefly:

- `/clear` when switching to an unrelated task (`/rename` first if they may resume).
- `/compact <what to keep>` after finishing a phase of a long task, or before a long break.
- `/context` to see what's preloaded (CLAUDE.md, MCP tool schemas, skills).
- Disabling unused MCP servers via `/mcp` — each one loads its tool schemas every turn.

Don't nag: mention it once when context is clearly bloated or the task has shifted, not every turn.

## 8. Memory and CLAUDE.md

When writing to CLAUDE.md or memory files, keep entries terse and stable (commands, conventions, constraints). Long workflows belong in a skill, which loads only when needed. Flag bloated or stale CLAUDE.md content to the user if you notice it.

## User-side settings

If the user asks how to cut usage further (env vars, model/effort choices, hooks, subagent models), read `references/user-levers.md` and give the relevant items.
