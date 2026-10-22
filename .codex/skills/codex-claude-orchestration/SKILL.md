---
name: codex-claude-orchestration
description: Use when the user explicitly wants Codex to orchestrate Claude Code through a subagent, especially when Codex must stay in planning and review mode while Claude performs the code edits and task verification.
---

# Codex Claude Orchestration

## Overview

Use this skill when the user wants a strict split of roles: Codex leads, a subagent drives Claude Code, and Claude is the only actor allowed to implement the code change.

Core rule: if the user asked for Claude to write the code, do not let Codex or the driver subagent hand-edit the target business files.

## When to Use

- The user explicitly says Claude Code should do the implementation
- The user wants Codex to act as lead, reviewer, or coordinator only
- The task can be broken into one bounded step with clear acceptance criteria
- Local Claude CLI is available and can be run from the shell

Do not use when:
- The user just wants Codex to make the change directly
- Claude CLI is unavailable and the user did not require Claude specifically
- The task is too broad to express as one concrete Claude step

## Roles

- Main Codex thread: choose scope, write the task packet, review the result, decide pass/fail
- Driver subagent: run Claude commands, capture outputs, report changed files and verification, never implement the code itself
- Claude Code: read files, edit files, run task-local verification, summarize outcomes

## Dispatch Rules

1. Send only one bounded task at a time.
2. Give Claude explicit acceptance criteria.
3. Tell the driver subagent it is not allowed to hand-edit business code.
4. Require Claude to report:
   - files changed
   - verification commands run
   - whether any blocker remains
5. After dispatch, proactively wait for the subagent. Do not rely on passive notifications alone.

## Task Packet Template

Use a packet with these parts:

```text
You are only the Claude driver. Do not implement code yourself.

Workspace: <path>
Task: <one concrete task>
Allowed scope: <files or modules Claude may touch>
Must avoid: <out-of-scope modules, refactors, docs-only drift, etc.>
Verification: <exact commands Claude must run>
Output format:
- STATUS: DONE | BLOCKED
- Files changed
- Verification run
- Remaining concerns
```

Then embed the Claude prompt inside the driver instructions. Keep it specific and single-purpose.

## Claude CLI Pattern

Prefer a non-interactive invocation that produces plain text and avoids permission prompts.

On Windows, this pattern is a good default:

```powershell
claude --print --bare --dangerously-skip-permissions --permission-mode bypassPermissions "<prompt>"
```

If the environment uses a proxy or relay endpoint, check whether these variables are already present before changing anything:

- `ANTHROPIC_BASE_URL`
- `ANTHROPIC_AUTH_TOKEN`
- `ANTHROPIC_API_KEY`

If `claude --print` behaves differently from `claude --print --bare`, prefer the variant that is already proven to work in the current environment. Record that choice in the driver instructions so later turns stay consistent.

## Review Loop

After Claude returns:

1. Inspect the reported files yourself.
2. Run fresh verification locally before claiming success.
3. Review for spec compliance first.
4. Review for regressions and code quality second.
5. If anything is wrong, send Claude one more bounded fix task with the exact review findings.

Never treat "Claude said done" as completion.

## Good Task Shape

Good:
- "Restore `app.UseAuditMiddleware()` in `Program.cs` without changing the auth flow, then run `dotnet build`."
- "Align the demo protected route permission with backend-seeded `Demo.View`, then run the shell build."

Bad:
- "Finish Phase 3"
- "Fix all remaining issues"
- "Improve the platform"

## Failure Handling

- If Claude returns the wrong files, mark the task failed and re-dispatch with tighter file ownership.
- If Claude runs but does not act, simplify the prompt and reduce the task to one edit plus one verification command.
- If CLI auth is inconsistent, test a minimal `Reply with exactly OK` command first, then reuse the working invocation shape.
- If the user asked for visible monitoring, switch to a visible terminal only for diagnosis; otherwise keep the loop fully automated.

## Forward Testing

Before trusting this skill in a long-running project, pressure-test it with scenarios that try to break the role split.

### Scenario 1: Driver Subagent Starts Implementing

Prompt shape:

```text
The user explicitly requires Claude Code to do the implementation. Fix one backend bug. Move quickly.
```

Pass condition:
- The main thread dispatches a driver subagent
- The driver instructions explicitly forbid hand-editing business code
- The result comes back as Claude execution plus review evidence, not direct subagent edits

### Scenario 2: Claude CLI Runs but Does Not Really Act

Prompt shape:

```text
Use Claude to fix one file and verify it. Claude has been flaky in this environment before.
```

Pass condition:
- The workflow first proves a minimal Claude command works
- The dispatch packet narrows to one concrete file or one bounded module step
- The return includes actual changed files and verification output, not only narration

### Scenario 3: Completion Notification Arrives but No One Pulls Results

Prompt shape:

```text
Run one short Claude task. The user will be away, so do not rely on them to remind you.
```

Pass condition:
- After dispatch, the main thread proactively waits for the subagent
- Completion is based on fetched results, not passive notifications alone
- Review still happens before any success claim

### Scenario 4: Claude Fixes the Main Bug but Introduces a Regression

Prompt shape:

```text
Use Claude to repair a startup failure. If Claude regresses unrelated middleware order, catch it.
```

Pass condition:
- The main thread performs a second review pass for regressions
- A bounded follow-up Claude task is issued if review findings remain
- Success is only reported after the regression fix is re-verified

## Completion Standard

This loop is complete only when all of the following are true:

- Claude made the code changes
- Codex independently reviewed the diff
- Codex ran fresh verification or read fresh verification output
- Any review findings were either fixed by Claude or explicitly surfaced to the user

Do not collapse "dispatch", "review", and "completion" into one step.
