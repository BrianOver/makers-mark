# The overnight loop

One Claude Code session, kept going by the built-in `/goal` command, taking units off the plan
until it runs out of work or hits something only the owner can rule on.

It exists for one reason, and it is not throughput. The owner's standing complaint across this
project's whole history is that **feedback gets heard, half-built, and then forgotten when the
session ends**. A loop that reads the plan instead of a conversation cannot forget, because it
never remembered in the first place — every turn re-derives where things stand from `origin/main`.

## Kick it off

```
/goal Work the Maker's Mark frontier against origin/main, following tools/loop/README.md. Each
turn: run `dotnet run --project tools/Progress -- --frontier` and read it fresh — never trust what
an earlier turn said was runnable. Take the topmost RUNNABLE unit, build it via the
mm-unit-builder agent, gate its PR via the mm-ci-gate agent, and confirm the merge against
`gh pr view <n> --json state` before moving on. End every turn with exactly one of these as its LAST line: `@@LOOP
WORKING - <unit> next`, `@@LOOP DONE`, `@@LOOP HALTED: <unit>, <reason>`, or `@@LOOP BLOCKED:
<reason>`. The goal condition is: a line beginning `@@LOOP DONE`, `@@LOOP HALTED` or `@@LOOP
BLOCKED` has been stated, or stop after 40 turns.
```

**The `@@LOOP` prefix is load-bearing, not decoration.** `/goal`'s evaluator reads the conversation
transcript, and the bare words *done*, *halted* and *blocked* occur constantly in ordinary engineering
prose — a commit message saying "done", a test named `Blocked`, a sentence explaining why some other
thing halted. An unprefixed condition ends the night on the first one. The prefix has no other
occurrence in this repo, so it cannot fire by accident. The trailing turn cap is cheap insurance
against the opposite failure: an evaluator repeatedly reading a stuck turn as "still working".

Set the output style to caveman ultra before pasting it. Output styles do **not** inherit to
subagents, which is why each `.claude/agents/mm-*.md` carries its own caveman instruction in its
body.

## What one turn does

1. **Read the frontier, never memory.** `tools/Progress -- --frontier` derives every unit's state
   from `origin/main` and the plan's own `Depends on` column. Nothing is stored between turns, so a
   turn that starts after a crash behaves identically to one that starts after a success.
2. **Check the kill switch.** If `tools/loop/HALT` exists, stop and say so. Create it from anywhere
   with `New-Item tools/loop/HALT` — it is gitignored, so it never travels in a commit.
3. **Take the topmost RUNNABLE unit.** The frontier has already refused anything gated on an owner
   ruling, anything whose dependencies have not landed, anything carrying a `[C]`/`[GOLD]`/`[BAL]`
   ceremony flag, and anything whose id already appears in tracked source.
4. **Dispatch `mm-unit-builder`** with the unit id, its base ref, and the symbol its first command
   must grep for. It builds in its own worktree, runs the fast lane, opens the PR and arms
   auto-merge.
5. **Dispatch `mm-ci-gate`** with the PR number. It blocks on `gh pr checks --watch` and answers
   only against the checks the branch ruleset actually requires, read live.
6. **Confirm the merge against the repo**, never against the builder's own report: `gh pr view <n>
   --json state` must say `MERGED`. A self-reported success is not evidence.
7. **End the turn with its `@@LOOP` line**, because that line is the whole of what `/goal`'s
   evaluator reads. There is no separate status file to keep in sync — and therefore none to stop
   being written, which is how the system this design borrowed from lost 67 PRs' worth of ledger
   without noticing.

## The four terminal states

| State | Means | What happens next |
|---|---|---|
| `@@LOOP DONE` | The frontier has no RUNNABLE unit left | Stop. The morning report is the frontier's own REFUSED list — usually "the owner's evening is the gate". |
| `@@LOOP HALTED: <unit>, <reason>` | A specific unit cannot proceed: red CI, a wrong spec, a golden or balance band that would have to move | Stop on that unit. Do not skip to another one — a loop that routes around a halt is a loop that hides it. |
| `@@LOOP BLOCKED: <reason>` | The session cannot continue: rate limit, context budget, an unreadable input | Stop. A human re-issues the same kickoff; step 1 makes that safe whenever it happens. |
| `@@LOOP WORKING - <unit> next` | A unit merged and another is runnable | Next turn. |

A **degraded frontier is `@@LOOP BLOCKED`, never a short night.** `tools/Progress -- --frontier`
exits 2 when it read incomplete data, and refuses to render rows at all rather than handing back a
shorter list — because both of its tolerant reads push units toward looking *unbuilt*, so a degraded
frontier is systematically over-full of work that is already done.

## What it never does

Each of these is prevented by something that fails closed, not by a sentence here.

- **Merge anything CI has not passed.** The branch ruleset requires `sim-tests`, `balance-sim` and
  `engine-tests` server-side, with no bypass actors. `--auto` cannot outrun it and neither can the
  loop.
- **Take an owner-gated unit.** `--frontier` refuses any row whose `Depends on` cell names a token
  the parser could not resolve to a unit — `P4` (the owner's own evening), an open ruling, a
  section cite. Two real units sit behind `P4` today.
- **Re-record a golden or re-pin a balance band.** The `[GOLD]`/`[BAL]` flags refuse at the
  frontier, and the builder agent is told to treat a red golden as a finding rather than a chore.
- **Edit a deny-listed path.** `tools/loop/settings.json` denies them at the harness, so the tool
  call fails rather than the model choosing to be good.
- **Run the engine suite in two places at once.** The builder agents are forbidden it outright; the
  orchestrator runs it, one at a time. Two concurrent gdUnit runs each report success while losing
  hundreds of tests, which is the worst shape a green signal can have.

## What it is NOT

**No sandbox trunk.** The system this borrows from cuts an `agent-main` branch and promotes it by
hand — because that repo's `main` has no server-side protection at all, so its "human gate" is a
client-side hook plus prose. This repo's `main` is genuinely protected, review happens on `main`,
and `git revert` is the undo (rule 11). A second trunk would move the truth away from the thing
`play.bat` actually launches.

**No DAG file, no run ledger, no progress block.** All three are second copies of facts git already
owns, and all three went stale in the source system — its ledger stopped mid-run while 67 PRs
merged, and its state file accumulated 69 blocks nobody removed. The merged-PR list is the ledger
because it cannot stop being written.

**No external supervisor and no self-scheduling.** The source system built a launchd watchdog, it
never worked (exit 126), the ADR retired it — and a later PR added it back with no ADR at all. If
the process dies, a human re-issues the kickoff. Step 1 is what makes that cost nothing.
