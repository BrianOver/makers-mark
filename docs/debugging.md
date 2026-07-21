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
| Sim fast lane | `dotnet test sim/GameSim.Tests/GameSim.Tests.csproj --filter Category!=Balance` | nothing |
| Balance gate | `dotnet test sim/GameSim.Tests/GameSim.Tests.csproj --filter Category=Balance` | nothing |
| Art conformance | `dotnet test art/GameArt.Tests/GameArt.Tests.csproj` | nothing |
| Engine tests | `dotnet test godot/tests --settings .runsettings` | Godot 4.6.3 via GODOT_BIN; a display (CI uses xvfb) |

Run the fast lane before reporting ANY work done (CLAUDE.md rule 1).

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
| `dotnet test godot/tests` prints "Godot compilation TIMEOUT" and reports far FEWER tests than usual (e.g. 33 instead of the full ~267), still "Passed!" | gdUnit4Net's `CompileProcessTimeout` (default 20s) is too short for a cold/first-in-session build; the run that hits it appears to complete against a partial/stale discovery set rather than failing outright | Don't trust a run whose console shows a TIMEOUT banner, even if it says `Passed!` — the low test count is the tell. Re-run the exact same command once with nothing else changed; a warm second run completes cleanly (confirmed PA9: identical invocation went 33/33 → 267/267 pass with no code change between runs). If a full-count run itself fails, that's real — investigate normally. Recurs every cold run → raise `CompileProcessTimeout` in `.runsettings`'s `<GdUnit4>` block |
| gdUnit engine suite hangs (never returns) on a 3D scene | Pumping physics frames while a 3D `SubViewport` keeps rendering — memory: godot-3d-headless-test-hang | Disable the `SubViewport`'s render-target update before pumping frames in the test (see `Town3DSceneTests`/`CameraRig` test setup for the pattern) |

## 6. The telemetry loop

`docs/telemetry-loop.md` — batch → analytics → anomalies → Claude proposes data-tuning PR →
gates → Brian approves. Anomaly entries carry their own repro pointers (section 1 applies).
