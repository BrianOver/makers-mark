---
artifact_contract: ce-unified-plan/v1
artifact_readiness: implementation-ready
execution: code
product_contract_source: ce-plan-bootstrap
type: feat
created: 2026-08-10
origin: owner direction 2026-08-10 — "the runs should be capturing all details not just run count ... the playtests should cover EVERYTHING/EVERY aspect using the three primary playtest modes ... launch the playtests with different claudes who have varying understanding, knowledge, goals ... That is also why we setup detailed backend logs"
---

# The playtest keeps what it saw

Thirty runs finished overnight and produced a table with six columns: run, scope, exit,
verdict, model-driven turns, last day. Every one of those is a **count**. The owner's
answer to that table is the whole scope of this wave:

> "the runs should be capturing all details not just run count ... the playtests should
> cover EVERYTHING/EVERY aspect using the three primary playtest 'modes'. The idea was to
> launch the playtests with different claudes who have variying undertstanding, knowledge,
> goals etc ... That is also why we setup detailed backend logs"

So this wave is not another honesty fix. The instrument is honest now (#419, #430, #433,
#436 — it reports degraded runs, fell-back runs, and runs that stopped early). What it is
not yet is **an instrument that keeps evidence**. It throws away most of what it sees while
it is looking straight at it.

## Goal Capsule

- **Every artifact a run produces is kept and readable afterwards** — frames, backend log,
  the model's reasoning, the phase timeline — not just the aggregate line.
- **"Cover everything" gets a denominator.** A coverage census enumerates the game's
  surfaces and reports which were exercised *and which were never touched*, per run and
  across a sweep. An untouched list is the only form in which "we tested everything" can be
  either true or false.
- **The three modes are played by different players.** Persona files give each run a
  different amount of knowledge and a different goal, so N runs measure N players rather
  than one player N times.
- **The sweep is a repo tool, not a temp script.** The overnight sweeps have been ad-hoc
  PowerShell in a scratch directory each time, which is why no two of them are comparable.

## What this serves

`Serves: substrate.` Not one unit here is a §2 link item. The wave earns a slot on one
argument, the same one §11.6 rule 4 granted the last harness wave: the playtest is the only
mechanism in the repo that observes the *whole assembled game* the way a person does, and a
finding it cannot evidence is a finding nobody can act on. The scout judge's standing
verdict — *"nothing in the log names the player's work — every outcome is read as generic"* —
is a link-4/link-5 claim that currently rests on one model's prose, with no backend record
attached to confirm or refute it. That is the gap this closes.

## Scope Boundaries (non-goals)

- **No new judgement model.** Same local `llava:7b` through the same ollama path. Personas
  are prompt content, not new inference infrastructure. Owner constraint still binding:
  nothing that costs money.
- **No cloud/API players.** "Different claudes" is honoured as *different personas with
  different knowledge*, driven by the local model. A real multi-model fleet is a separate
  decision with a bill attached, and this wave must not smuggle it in.
- **No new coverage requirement in CI.** The census reports; it does not gate. A coverage
  floor is a policy decision for the owner after he has seen the first real numbers.
- **The day-11 boredom wall is not answered here.** Measured rate is ~11–16 turns per
  in-game day, so reaching day 11 needs ~150–200 turns per run. This wave makes the
  instrument able to *see* it; whether to spend that many turns is the owner's call.

## Verification Contract

| Claim | How it is proven |
|---|---|
| Frames are kept per turn | A `-Scripted` run leaves one PNG per turn under `frames/`, and `turnlog.md` names each |
| The backend log is read | `findings.md` contains a Backend section whose numbers are derived from `playtest-log.jsonl`, asserted against a fixture log |
| Coverage has a denominator | `coverage.json` lists total surfaces and untouched surfaces; the untouched list is non-empty on a short run and shrinks on a long one |
| Personas differ | Two runs with different personas produce different act-prompt hashes, recorded in each `findings.md` header |
| The sweep is reproducible | `tools/playtest-sweep.ps1 -DryRun` prints the exact run matrix without launching Godot |
| The timeout mismatch is closed | The client's command timeout is ≥ the driver's per-turn model budget, pinned by a test that reads both constants |

---

## Implementation Units

### U1. Every frame the model saw is kept

**Goal.** `frame.png` is overwritten every turn, so a 80-turn run leaves exactly one
screenshot — of the last turn, which is usually the least interesting one. The owner asked
for screen grabs specifically; they exist, and then they are deleted by the next turn.

**Files.** Modify `tools/agent-playtest.ps1`. Modify `godot/scripts/tools/AgentPlaytest.cs`
only if the frame path is chosen client-side.

**Approach.** After each turn's frame is consumed, copy it to
`<OutDir>/frames/turn-<NNN>.png` (zero-padded, so a directory listing is chronological).
Add `-FrameEvery <n>` (default 1) for long runs. Record the kept-frame count in the
findings header, and reference the file beside each turn in `turnlog.md` so a reader can
open the exact frame a note is about. A turn whose frame was missing must say so in the
log rather than silently having no line — that asymmetry is what hid the imageless-turn
defect until #420.

**Test scenarios.** Scripted run leaves N frames for N turns; `-FrameEvery 5` leaves
⌈N/5⌉; a deliberately deleted frame produces a "frame missing" line rather than a gap.

**Verification.** `tools/agent-playtest.ps1 -Scripted` then count `frames/*.png`.

### U2. The backend log becomes evidence

**Goal.** `playtest-log.jsonl` is written every run and **read by nothing**. The judge sees
only the model's own turnlog — the model's account of the game, with no independent record
beside it. The owner's line names exactly this: *"That is also why we setup detailed backend
logs."*

**Files.** Create `tools/agent-playtest/backend.ps1`. Modify `tools/agent-playtest.ps1`.
Create `tools/agent-playtest/tests/backend-fixture.jsonl`.

**Approach.** A pure function `Get-BackendSummary -LogPath <p>` (no Godot, no ollama, so it
is testable in isolation like `completion.ps1` and `scope-map.ps1` already are) that emits:

- the phase/day timeline, with each advance's recorded **cause** (`press:AdvancePhase`,
  `press:Hurry`, `auto:innkeepers-clock`, `auto:conductor-beat-elapsed` — the tags #425
  added and nothing has consumed since);
- every rejected action with its reason, counted by reason;
- sim events by type, and specifically whether any event **named the player's work**
  (an attribution/ledger/gossip line carrying a `MakersMark`) — the standing scout claim,
  finally measurable instead of narrated;
- narrator lines fired, and autosave writes;
- **contradiction checks**: a turn the driver recorded as accepted while the backend logged
  a rejection, and any phase advance with an `auto:` cause during a turn the model was
  mid-decision.

Write it as a `## Backend record` section of `findings.md` *above* the model's prose, and as
`backend.json` beside it. The order matters: the recorded facts should be the first thing a
reader meets, and the model's account second.

**Test scenarios.** Fixture log with a known tag mix produces exact counts; a log with an
`auto:` advance and an accepted turn produces the contradiction line; an empty/absent log
produces an explicit "no backend log" line, never a silent zero.

**Verification.** Dot-source and run against the fixture; then a scripted run end to end.

### U3. Coverage census — "everything" gets a denominator

**Goal.** No run can currently say what it did *not* reach. "Cover EVERY aspect" is
unfalsifiable without the untouched list.

**Files.** Create `tools/agent-playtest/coverage.ps1`. Modify `tools/agent-playtest.ps1`.

**Approach.** Enumerate the denominator from the game rather than a hand-typed list
(a hand-listed field set is the exact defect the state-fingerprint work already hit): panel
ids, interior stations, town buildings, HUD control names, `DayPhase` values, and the action
types the advisor's legality mirror already enumerates. Per turn, record what the state
showed and what the model pressed; at the end emit `coverage.md` + `coverage.json` with
touched, untouched, and the percentage — **and print the untouched list in full**, never
truncated to a top-N, because the tail is the part nobody has ever seen.

Where a denominator cannot be derived from code, that is itself reported ("3 surfaces could
not be enumerated"), never quietly dropped.

**Test scenarios.** A stub turn history covering two panels of a known set reports the exact
complement; an unenumerable surface appears in the caveat line; the union across two runs is
the union, not the last run's set.

**Verification.** Pure-logic checks, no Godot needed, plus one scripted run.

### U4. Five players, not one player five times

**Goal.** One `act.md` persona ("curious, slightly impatient") drives every run in every
mode. Thirty runs measured that same person thirty times. The owner asked for *"different
claudes who have varying understanding, knowledge, goals"*.

**Files.** Create `tools/agent-playtest/prompts/personas/{first-timer,veteran,speedrunner,
completionist,sceptic}.md`. Modify `tools/agent-playtest.ps1` (a `-Persona` parameter,
`random` allowed). Modify `tools/agent-playtest/turn-prompt.ps1` if the persona alters the
per-turn framing.

**Approach.** `act.md` keeps everything that is *protocol* — the JSON contract, the movement
rules, the control-legality rule. A persona file carries only **knowledge and goal**, and
they differ deliberately:

- **first-timer** — told nothing about the game beyond what the screen says; goal is to
  follow the tutorial and find out what this is. The only persona that can find a copy
  defect, because it is the only one that does not already know the answer.
- **veteran** — given the loop and the six decisions; goal is gold and hero survival, and it
  is told to skip the tutorial. Finds the mid-game, which no first-timer run ever reaches.
- **speedrunner** — goal is to reach the deepest floor in the fewest days, pressing advance
  hard. This persona is the §11.7.8 probe: if skipping is ever engineered against rather
  than merely costly, this is the run that hits it.
- **completionist** — goal is to touch every surface at least once; explicitly paired with
  U3, and the persona whose coverage number is worth reading.
- **sceptic** — given the seven laws and the five links; goal is to find a verb that changes
  nothing. Pairs with the Scout judge.

Persona choice, and a short hash of the assembled act prompt, go in the `findings.md`
header and the sweep summary — so two runs claiming to be different players can be checked.

**Test scenarios.** Each persona file assembles into a prompt containing the protocol
section and its own goal; two personas produce different hashes; an unknown `-Persona`
value fails loudly rather than falling back to the default (a silent fallback here would
reproduce the exact class of defect this harness was just fixed for).

**Verification.** Pure-logic assembly checks; then one live run per persona at low turn
count.

### U5. The sweep becomes a repo tool

**Goal.** Every overnight sweep so far has been a fresh ad-hoc script in a scratch
directory, which is why runs from different nights cannot be compared and why the summary
was a six-column CSV.

**Files.** Create `tools/playtest-sweep.ps1`. Modify `.gitignore` if `runs/playtest/` needs
it (it does — outputs are evidence, not source).

**Approach.** Parameters: `-Runs`, `-Scopes`, `-Personas`, `-Turns`, `-DryRun`. Serial by
construction (each run holds the GPU model and its own Godot client). Writes
`runs/playtest/<stamp>/` with per-run subdirectories and two aggregates:

- `SUMMARY.csv` — one row per run, now including persona, prompt hash, completion ratio,
  coverage percentage, untouched count, backend contradiction count.
- `REPORT.md` — the detail the owner asked for: the union coverage across the sweep and the
  **surfaces no run in the entire sweep ever touched**; findings that recur across runs
  (the same complaint from three personas is a different fact from one run's opinion);
  per-persona differences; every INCOMPLETE/DEGRADED run named with its cause.

`-DryRun` prints the matrix and exits, so a 30-run night can be checked in one second
before it is spent.

**Test scenarios.** `-DryRun` prints the exact matrix; aggregation over two fixture run
directories produces the union and the recurrence counts; a run directory missing
`findings.md` is reported as missing rather than skipped silently.

**Verification.** `-DryRun`, then a two-run live sweep at low turns.

### U6. The 30-second client against the 300-second model

**Goal.** Named and left unfixed in #436: `AgentPlaytest.cs` hardcodes
`DefaultCommandTimeoutMs = 30_000`, while the driver allows the model up to **300 s across
three attempts per turn**. When a turn runs long the client calls `GetTree().Quit(0)` — a
clean, silent self-exit that writes no further state. `Scout-5` died that way on turn 1 and
reported `ok`.

**Files.** Modify `godot/scripts/tools/AgentPlaytest.cs`. Modify `godot/tests/
AgentPlaytestBridgeTests.cs`.

**Approach.** Raise the client's wait past the driver's worst-case turn budget, and make the
relationship explicit rather than two unrelated numbers in two languages: the driver passes
its budget in (env var, already how `AGENT_PLAYTEST_DIR` travels) and the client uses it,
falling back to a constant that is documented as *"must exceed the driver's per-turn
budget"*. And a timeout exit must **not** be exit 0 — it is an abandoned run, and #436's
completion floor should not be the only thing that notices.

**Test scenarios.** Bridge test asserting the effective timeout exceeds the driver's budget;
a timeout produces a non-zero exit and a named reason in the log.

**Verification.** Engine suite, then a live run that deliberately stalls one turn.

## Dependencies and sequencing

- **U1, U2, U3, U4, U6 are independent** — different files, no shared edits except
  `agent-playtest.ps1`, which each touches in a distinct region (frames, backend section,
  coverage section, persona parameter). Parallel-safe with a merge cost that is real but
  small; serialise U1→U2→U3→U4 if a worker reports conflict churn.
- **U5 depends on U1–U4** — it aggregates fields those units create. It is the last unit and
  the one whose PR deletes this wave doc (rule 7).
- **U6 is the only unit needing a live Godot run** to verify, and engine tests serialise
  globally, so it should not run concurrently with another engine suite.

## Definition of Done

1. A single run's output directory contains: per-turn frames, `turnlog.md` referencing them,
   `backend.json` + a Backend section, `coverage.json` + `coverage.md`, `findings.md` with
   persona and prompt hash in its header, and `driver.log`.
2. `tools/playtest-sweep.ps1 -DryRun` prints a matrix crossing all three scopes with at
   least three personas.
3. A real sweep runs and its `REPORT.md` names at least one surface no run touched.
4. Fast lane green, engine suite green, both quoted from the runner's own
   `Failed: N, Passed: N` line.
5. This doc is deleted by the PR landing U5.
