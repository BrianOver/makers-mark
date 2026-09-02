# Debugging Maker's Mark — the self-serve manual

For any Claude (or human) diagnosing a bug, test failure, or behavior anomaly. The sim is
deterministic — that is the superpower: **any state reproduces byte-identically from seed +
action log.** Use it before reading resolver code or guessing.

## 1. The deterministic repro recipe

1. **Capture** — from a live CLI session: `export [path]` writes the campaign chronicle
   (seed, day, roster, full event log) to `runs/`. From a batch: chronicles are already there.
2. **Replay** — a fresh campaign with the same seed + the same actions IS the same world:
   `GameComposition.NewCampaign(seed)` + `kernel.Tick(state, actions)` per phase. The golden-replay
   test (`sim/GameSim.Tests`) is the executable spec of this guarantee.
3. **Bisect by day** — the anomaly names a day window. Re-run to day N-1, then step one phase at a
   time (`next` in the CLI) watching the event log. Events are stamped and ordered; the first
   surprising event names the module that emitted it.
4. **Isolate** — write a focused xUnit test that builds the minimal state and calls the one
   system/handler directly (see any `*ConformanceTests` for the pattern). Fix, then re-run lanes.

Batch repro of a reported anomaly: run the exact `Repro:` command from `anomalies.md` (it targets
`runs-repro/` so the truncated repro chronicle never pollutes the analytics corpus), then inspect
that chronicle. **Determinism is seed + ACTIONS**: a batch chronicle replays only under the batch
command (BaselinePlayer actions); an interactively-exported run was driven by YOUR actions — replay
it by re-entering the same commands with `--seed <s>`, or debug from its exported event log directly.

## 2. Test lanes (what to run, when)

| Lane | Command | Needs |
|---|---|---|
| Sim fast lane | `dotnet test sim/GameSim.Tests/GameSim.Tests.csproj -c Release --filter Category!=Balance` | nothing |
| Balance gate | `dotnet test sim/GameSim.Tests/GameSim.Tests.csproj --filter Category=Balance` | nothing |
| Art conformance | `dotnet test art/GameArt.Tests/GameArt.Tests.csproj` | nothing |
| Engine tests | **`.\tools\engine-test.ps1`** (NOT `dotnet test godot/tests` by hand) | Godot 4.6.3 via GODOT_BIN; a display (CI uses xvfb) |

Run the fast lane before reporting ANY work done (CLAUDE.md rule 1).

### The standard local set (pick by what the PR touches, never guess a smaller filter)

Two PRs failed CI overnight on `LayoutTests.EveningLedger_CardLabels_RenderAtReadableWidth` and
`HudBoundsTests.ObjectiveChip_TextNeverOverflowsItsOwnContainer` — cross-cutting layout tests that
live in files neither PR touched — after both had a large, green, **filtered** local run. No filter
either session would plausibly have chosen selects those two tests. The lesson is not "pick a better
filter": **the engine suite has no safe filter.** `tools/engine-test.ps1` already says this in its
own `.PARAMETER Filter` doc; the fix here is to actually follow it, every time:

1. **Always:** `dotnet test sim/GameSim.Tests/GameSim.Tests.csproj -c Release --filter Category!=Balance`
   — 1754 cases, ~135 duration-seconds, under a minute wall-clock. **Never filter further than
   `Category!=Balance`.** `-c Release` matters: it matches what CI runs, and Debug runs the sim
   1.5–3x slower.
2. **If the PR touches `godot/` at all:** `.\tools\engine-test.ps1` with **no `-Filter`, ever** — see
   §2a for why a filtered engine run cannot prove anything.
3. **If the PR touches `sim/GameSim/`:** the balance gate too. Be honest that more cores mostly don't
   help here — xUnit runs one test class serially, so the job's floor is whichever single class's
   tests sum the highest, not the sum across cores. Measured wall-clock: ~30 min before the
   `Lazy<T>` sweep memoization landed (2026-08-29 CI audit, run 33582674334), ~17 min after (the
   memoized class alone was 1796.8s of a 1945s job; see the `Lazy<T>` fields and their doc comments
   in `sim/GameSim.Tests/Balance/`).
4. **If the PR touches `art/` or `tools/`:** run the art-conformance lane, and any dedicated
   `tools/` test project that covers the changed file.

### 2a. The engine lane lies to you, so it has a wrapper

**Always `.\tools\engine-test.ps1`. Never `dotnet test godot/tests` directly.** Every trap below has
been hit for real, most more than once, and each produced a *confident wrong answer* rather than an
error. The script fails on all of them.

| Trap | What you see | Why it fools you |
|---|---|---|
| Two concurrent runs | `Passed: 87 ... Duration: 579 ms` | Not a fast suite — a runtime that never connected. gdUnit DROPS every `[RequireGodotRuntime]` test and still prints `Passed!` |
| Piping to `tail`/`head` | success | A bash pipeline returns the LAST command's exit code, hiding a failed run |
| Runtime dies mid-session | `Passed!` for a partial suite | The summary counts what finished. `ENGINE_MIN_PASSED=900` (`.github/workflows/ci.yml`) against a suite that runs 1696 means a run that drops well over half the suite can still clear CI's floor — check the current floor in `ci.yml` before trusting any number written here, since this exact table row has gone stale once already |
| Testing `C:\Code\Game` | green | That root is a coordination checkout nobody updates — it was ~130 PRs stale on 2026-08-03. You measured old code |
| **The wrapper itself, pre-2026-08-04** | `dotnet.exe : Expecting be equal:` and a 31-line log | The wrapper was hiding real failures. See below — this cost an hour of looking in the wrong place |
| **The wrapper itself, pre-2026-08-07** | `PASS - 945 tests, runtime healthy.` + a receipt | The run was `Failed: 5, Passed: 940`. The runner exited **0 with five failures** and the wrapper's only failure check was the exit code. See below |

**The count is the check, not the word "Passed".** The full suite runs 1696 (2026-08-29 CI audit,
run 33582674334) — up from ~880 on 2026-08-04, so treat any specific count in this doc as a snapshot,
not a target: it already went stale once. If the printed `Total` is dramatically below the current
`ENGINE_MIN_PASSED` in `.github/workflows/ci.yml`, the runtime died; read §5 before theorising.

**If a run log ends right after the build output with no summary, suspect the SHELL, not the test
framework.** In Windows PowerShell 5.1, redirecting a native process's stderr (`2>&1`) wraps each line
in an ErrorRecord, and with `$ErrorActionPreference = 'Stop'` the *first* stderr line becomes a
script-terminating error. A genuine assertion failure writes to stderr — so the wrapper died at its
own run line and skipped every line of its own failure reporting. Fixed by going through
`Start-Process` with OS-level redirects (PR #387). Recorded here because the symptom points squarely
at the test runner while the cause is the shell.

A second trap in the same fix: `WaitForExit(ms)` returning true does **not** guarantee `ExitCode` is
populated. It read as empty, and `'' -ne 0` is **true** in PowerShell — which would have reported a
*green* run as a failure. Read the code after an argument-less `WaitForExit()` and default to a
nonzero sentinel: an unreadable exit code must never become a claimed success.

**A third lie, found 2026-08-07: the runner can report failures AND exit 0.** A real wave produced
`Failed: 5, Passed: 940, Total: 945` with process exit code **0**. The wrapper's only failure check
was `$testExit -ne 0`, so it printed `PASS - 945 tests, runtime healthy`, wrote a receipt vouching
for the commit, and the failures were reported to the owner as green *twice* before CI caught them.
Note the shape: this is the mirror image of the trap two rows above — that one was a floor with no
failure check, this one was a failure check with no floor on its own honesty.

Fixed by summing every `Failed:\s+(\d+)` in the log and refusing on nonzero **regardless of exit
code**, printing an explicit note when the two disagree. **The count is authoritative; the exit code
is advisory.** The fix earned itself immediately: on the very next run it caught two more failures
the exit code would have hidden again.

**So a green claim needs three independent facts, and any one alone is forgeable:** the suite ran
(`Total` vs the floor), nothing failed (the parsed `Failed` count, *not* exit status), and the
runtime survived (death signatures). Before believing any harness's verdict — including this one's —
run `grep -E "Failed:\s+[0-9]+" .claude/engine-test/last-run.log` yourself. It costs nothing and has
now caught two different lies from two different layers.

**The wrapper now waits for the machine instead of telling you to.** `-MaxWaitMinutes` (default 10)
waits, names which process holds the machine, then refuses with a message stating it is a final
answer. **A refusal is not a hint to retry** — sessions used to idle for hours against a run they
could not see. `-RunTimeoutMinutes` (default 20) kills a stalled run and stops its Godot strays, so
one stall cannot make every other caller's wait expire for nothing. A healthy full run is 4–7 minutes
here; a stall was measured at ~9m48s.

### 2b. Six rules that came from losing a day to this

1. **Verify the precondition before theorising about the symptom.** On 2026-08-03 a stale one-line
   guard disabled *one* SubViewport while `MineWatch`'s constructor built a second — so tests
   rendered while the code above them said they didn't. Three confident diagnoses were built on that
   false premise (a "fixed wall-clock cap", a "random flaky test", a memory-pressure theory) before
   anyone checked whether rendering was actually off. `MountMainUi` now disables all rendering by
   default; opting in is visible in a diff, forgetting to opt out was invisible.
2. **CI is a gate, not a test loop.** Get local green first. `engine-test.ps1` writes a receipt
   naming the commit it verified — a push should match it. Three red pushes of one branch in a day
   is three notifications to the owner and zero information you couldn't have had locally.
3. **Serialize engine runs, and prove it.** One gdUnit run at a time, machine-wide. The script now
   waits up to 10 minutes and then refuses; if you bypass it, check `Get-Process *Godot*` yourself
   first. **Never poll for a slot in a loop** — that is how a background session spends 300k tokens
   and pushes nothing.
4. **Change a measured number only with a new measurement.** A day-count on the click-through sweep
   was cut, reverted on reasoning, then re-applied when the run disagreed — two full suite runs to
   learn what one would have said.
5. **A tool that checks for lies can lie.** Both defects in §2a's last row were in
   `engine-test.ps1` itself — the thing built to stop the suite from lying. When the wrapper's own
   output is your only evidence, run the underlying command once, raw, to confirm the wrapper is
   telling you the truth. That one step would have saved an hour.
6. **Same commit, different result means the bug is timing-dependent — not that the suite is flaky.**
   On 2026-08-04 one commit gave 7 failures and then 879/879 on consecutive runs. The cause was real:
   `MainUi._Process` ticked both the clock and the raid conductor in one frame, so an oversized frame
   delta could satisfy two phase thresholds at once. "Flaky test" was the comfortable read and the
   wrong one — a player would hit it on a slow frame.

## 3. Where logs & artifacts live

- `runs/*.json` — exported chronicles (CLI `export`, batch runner). Input to Analytics.
- `dotnet run --project tools/Analytics -- runs` — tuning report to STDOUT; `runs/anomalies.md` written to disk.
- CI: trx artifacts per lane (`sim-test-results`, `balance-test-results`, `engine-test-results`);
  engine-lane failure step dumps recent Godot logs.
- Godot user logs (local): `%APPDATA%\Godot\app_userdata\` / `~/.local/share/godot/`.
- ComfyUI (art gen, local): `C:\Tools\comfy_boot*.log` + server console.

## 4. Counter service + forge minigame log map (Phase A / PA1–PA9)

Both loops are sim events narrated at two layers — CLI text (headless repro) and Godot faces/prose
(render-only). Trace a haggle or a craft by following an event kind through both layers:

| Sim event (`Contracts/Events.cs`) | Emitted from | CLI narration (`GameSim.Cli/EventNarration.cs`) | Godot render (`godot/scripts/panels/ShopStage.cs`) |
|---|---|---|---|
| `CustomerApproached(Hero)` | `CounterHandlers.ApplyOpen` / `CounterQueueSystem` dequeue-next | `→ {hero} steps up to the counter` | customer walk-in choreography |
| `CustomerCountered(Hero, OfferGold)` | `HaggleResolver` (round resolution) | `↔ {hero} offers {gold}g` | standing-offer chip on `CounterPanel` |
| `CounterSaleClosed(Hero, Item, Price, Pinned)` | `HaggleResolver.ResolveHaggleResponse` (Accept / in-band Counter) | `$ {hero} buys {item} for {gold}g at the counter` (+"— dead on the money" flavor if `Pinned`) | `ShopStage.ClassifyCounterSale` → Heart (pinned) / Smile emote |
| `CustomerWalked(Hero, Item?, Reason)` | `CounterQueueSystem` (afford/role-fit fail) or `HaggleResolver` (Patience 0) | `~ {hero} walks away from the counter: {reason}` | `ShopStage.ClassifyCounterWalk` → Frown ("patience" in reason) / Shrug |

Forge minigame (PA6, Godot-only — the sim never sees the beats, only the folded result):

| What to check | Where | Notes |
|---|---|---|
| Sub-score → grade fold | `ForgeMinigame.FoldGrade` (static, testable in isolation) | Smelt 0.30 / Forge 0.40 / Quench 0.30 — weights are named constants, not magic numbers |
| Beat scoring | `SmeltBeat`/`ForgeBeat`/`QuenchBeat.Advance(delta)` | Pure accumulated-clock — no wall-clock, no engine RNG; replay a scripted `Advance` sequence to get an identical grade every time |
| Carry-forward flaw | `ForgeMinigame.EnterForge` passes `Smelt.Impurity` into the new `ForgeBeat` | A bad Smelt caps the Forge sub-score even before a single strike — `ForgeMinigameDross` label renders it |
| The one emitted action | `ForgeMinigame.Finish` → `Finished?.Invoke(action)` | Exactly one `CraftAction(recipeId, material, grade, Puzzle: null, subScores)`; `Cancel()` raises `Cancelled` instead and queues nothing — assert whichever event fired, never both |
| Quality math from the grade | `QualityRoller.RollActive` (sim-side) | Bands read off `effective = grade + jitter`; `ForgeMinigame` computes ONLY the grade, never the band — if quality looks wrong, bisect here first, not in the overlay |

Repro a haggle from a live/exported chronicle: filter the event log for `customerApproached` /
`customerCountered` / `counterSaleClosed` / `customerWalked` (the JSON discriminators in
`Events.cs`) in HeroId + Round order — the round sequence tells the whole negotiation.

Repro a graded craft: find the `CraftAction` in the action log; `PerformanceGrade` and
`SubScores` (in beat order — smelt, forge, quench) are stored verbatim, so the grade a player saw
in the overlay is the exact integer the sim rolled against — no recompute needed to check it.

## 5. Known failure shapes (check these FIRST)

| Symptom | Cause | Fix |
|---|---|---|
| Golden-replay mismatch | An RNG draw was added/removed/reordered — every seed's world shifted | Find the new/moved draw; if intentional, re-record goldens as a deliberate reviewed commit |
| `net8.0` in a csproj diff | Godot editor injected a TFM downgrade | `git checkout -- godot/GodotClient.csproj`; TFM lives in `Directory.Build.props` only (CLAUDE.md rule 3) |
| Engine tests "Connection timeout" | gdUnit4Net launches Godot WITHOUT --headless; no display | Run under xvfb (CI does); locally ensure a desktop session |
| Save fails to load after a contract change | New member not trailing-optional | Follow the `PreP4Save`/`PreP5Save` pin pattern in `SaveLoadTests` — absent member must default sanely |
| Flavor/gossip text changed unexpectedly | StableHash input or variant list changed | Voice/variant lists are FROZEN append-only; check `FlavorEngine`/`VoiceProfile` doc comments |
| Determinism breaks cross-OS only | float / transcendental `Math.*` / `string.GetHashCode` snuck into sim | Integer-only in `sim/GameSim`; use `IntegerCurves` / `StableHash` |
| Balance gate red after tuning | Bands moved | Re-tune or re-baseline deliberately — never loosen the assertion to pass |
| `HaggleResponseAction` rejected "No standing offer to respond to" | `PresentItemAction` and `HaggleResponseAction` queued in the SAME batch/tick | Submit them in SEPARATE ticks — `CounterHandlers.ApplyHaggle` resolves immediately (no spare deferred field survives the Contracts freeze, see its doc remarks) and reads the standing offer `CounterQueueSystem` set up on a PRIOR tick's Present. Natural UX anyway: see the offer, then respond |
| Counter session "stuck" — Morning never advances | `GameKernel.Advance`'s Morning-hold (PKD5) is working as designed | An open, un-`Closed` `CounterState` intentionally holds the phase at Morning. Queue `CloseCounterAction` (unserved heroes fall back to the atomic pass same-tick) or serve the queue empty |
| `dotnet test godot/tests` prints "Godot compilation TIMEOUT" and reports far FEWER tests than usual, still "Passed!" | gdUnit4Net's `CompileProcessTimeout` (default 20s) is too short for a cold/first-in-session build; the run that hits it appears to complete against a partial/stale discovery set rather than failing outright | Don't trust a run whose console shows a TIMEOUT banner, even if it says `Passed!` — a low test count relative to the CURRENT full suite (check `ENGINE_MIN_PASSED` in `.github/workflows/ci.yml` — the 267 this row's PA9 evidence used is from 2026-mid and long stale; the suite runs 1696 as of the 2026-08-29 audit) is the tell. Re-run the exact same command once with nothing else changed; a warm second run completes cleanly (PA9's own confirmation: identical invocation went 33/33 → 267/267 with no code change between runs — that 267 is that incident's historical count, not today's target). If a full-count run itself fails, that's real — investigate normally. Recurs every cold run → raise `CompileProcessTimeout` in `.runsettings`'s `<GdUnit4>` block |
| CI `engine-tests` red with `Connection interrupted by cancellation requested` and a duration of **~9m48s**, at any test count | The gdUnit Godot runtime **stalls** and the session is cancelled at a fixed ~9m48s. **Not** a headroom problem and not the blamed test's fault. Two measurements from 2026-08-01 settle it (the suite was ~550 tests then; it is 1696 now — the counts below are that incident's historical evidence, not a current target): (a) PR #311's commit aborted at 9m48s/266-of-550, then passed **unchanged** on re-run at 3m21s/550; (b) PR #314's job *passed* the full suite 550/550 in 3m13s and then its **8-test** silent-skip-guard step was cut at the same 9m48s. A cap that lands identically on 8 tests and on 550 is a fixed deadline, and an 8-test run reaching it means the runtime stalled at/near connect rather than running slowly. Healthy run ~3m30s = ~2.8x headroom; the job's ~14 min is checkout + setup + build + import, not the test run | `gh run rerun <id> --failed`. The blamed test is whichever one was executing when the axe fell and differs every time (HumanCampaignTests, Playtest3dClickThrough, MainUiTests.ShopPanel_…, MainUiTests.ForgePanel_CraftRoundTrip all observed with identical error text) — do not "fix" that test, and do not trim the suite to buy time you already have. If it reproduces on re-run, that is new information: look for a genuinely slow call on a hot path |
| gdUnit engine suite hangs (never returns) on a 3D scene | Pumping physics frames while a 3D `SubViewport` keeps rendering — memory: godot-3d-headless-test-hang | Disable the `SubViewport`'s render-target update before pumping frames in the test (see `Town3DSceneTests`/`CameraRig` test setup for the pattern) |

## 6. The telemetry loop

`docs/telemetry-loop.md` — batch → analytics → anomalies → Claude proposes data-tuning PR →
gates → Brian approves. Anomaly entries carry their own repro pointers (section 1 applies).
