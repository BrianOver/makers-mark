---
title: Active Professions & Shop Management — Phase A (Blacksmith Vertical Slice) - Plan
type: feat
date: 2026-07-21
topic: active-professions-shop
design_authority: docs/design/2026-07-21-active-professions-shop-design.md
plan_of_record: docs/plans/2026-07-13-001-feat-inverted-mmo-game-plan.md
artifact_contract: ce-unified-plan/v1
artifact_readiness: implementation-ready
execution: code
---

# Active Professions & Shop Management — Phase A (Blacksmith Vertical Slice) - Plan

## Goal Capsule

- **Objective:** Replace passive button-click crafting and atomic auto-shopping with an *active* blacksmith loop — a three-beat forge minigame whose captured `PerformanceGrade` DOMINATES quality, and a stepped Morning counter-service haggle — built as one deep vertical slice that becomes the reusable template for the Phase B profession fan-out.
- **Product authority:** the approved design spec `docs/design/2026-07-21-active-professions-shop-design.md` (design bets DB1–DB7 confirmed with Brian, 2026-07-21). This plan implements Phase A of that spec only.
- **Non-negotiables carried from the plan of record:** sim purity (KTD2), determinism (KTD4/KTD5), no runtime LLM, influence-never-orders, tests-green-before-done, contract amendments as orchestrator-authored micro-PRs (KTD9).
- **The crux invariant (spec §Determinism model):** real-time is a Godot concern; the sim consumes only captured results. The minigame's outcome is a per-mille integer riding `CraftAction.PerformanceGrade`; counter service is discrete actions the sim resolves; the headless BaselinePlayer never runs a minigame and keeps the 100-day balance gate trivially reproducible.
- **Open blockers:** None. Tuning constants (grade bands, haggle band widths, meter deltas) are pinned by tests inside their owning units, mirroring how U10 owned combat constants.
- **Stop conditions:** Surface (don't guess) anything that would break determinism, change the phase-machine semantics beyond the pinned Morning hold, add a runtime LLM/network dependency, or let counter outcomes alter a hero's autonomous expedition choices.

---

## Planning Contract

### Key Technical Decisions

- **PKD1. Captured-result seam, dual-mode from day one.** `CraftAction` carries EITHER a captured `PerformanceGrade` (blacksmith — Godot computes it, sim consumes it) OR a structured `CraftPuzzleInput` the sim scores itself (alchemist/enchanter, Phase B — strictly better balance-gate coverage). Phase A lands the contract shape (abstract polymorphic record, always null in Phase A, derived types registered via Phase B micro-PRs) and builds ONLY the blacksmith path. Both are action-log data, so replays stay exact.
- **PKD2. Active vs passive quality models, per profession.** `ProfessionDefinition` gains an active-craft flag + minigame-assist data. Professions with an active model (blacksmith only in Phase A) use the new dominance roll (PKD3); passive professions (alchemy, engineering, tanning) keep the existing ±8-shift threshold table **byte-identical** — a pinned regression, so nothing outside the blacksmith moves.
- **PKD3. PerformanceGrade dominates; RNG shrinks to a floor jitter.** For active professions the roll inverts (spec DB5): quality bands are read off the per-mille grade itself; the single `Roll100` draw (draw count unchanged — KTD4 contract) maps to a small ± jitter that can never cross a band on its own; material grade (± material-mastery) sets a hard grade **ceiling**; talents stop shifting the roll entirely (double-count resolved: blacksmith `FlatShifts`/`SlotShifts` retire from the roller and remap to minigame-assist data the overlay reads — mastery makes the *act* easier, visibly).
- **PKD4. Auto-craft = null grade, competent and capped.** For active professions, `PerformanceGrade: null` (and null puzzle) means auto-craft at the fixed competent grade, hard-capped below Masterwork (Sandrock lesson: the minigame is the only road to the top). `BaselinePlayer` already emits null-grade `CraftAction`s — the balance gate and CLI grind path get the deterministic competent baseline with zero harness change.
- **PKD5. Stepped Morning by phase-hold, atomic path untouched.** `OpenCounterAction` (Morning-only) switches the day's Morning into a stepped customer queue: `GameKernel.Advance` holds at Morning while a counter session is open, and each Tick resolves one player action batch (one haggle step). A run that never opens the counter is **byte-identical** to today — the atomic `HeroShoppingSystem` path is the default, which is what keeps BaselinePlayer, the 100-day gate, and every existing determinism test green. HIGH-RISK unit: the hold + the atomic-equivalence regression tests land before any haggle economics.
- **PKD6. Counter session state lives on `GameState` as a save-compat init member.** `CounterState?` (null = atomic morning) follows the exact `InFlight`/`Venues` pattern: non-positional init property, old saves deserialize to null. Meters are Potionomics-shaped (Interest raises the price band, Patience counts in **rounds** not seconds, Goodwill is the fleece-memory that feeds mood/gossip); the price band is Recettear-shaped (per-class factor, band shifts per round, ~3-round cap, "pin" mood bonus for countering near true willingness).
- **PKD7. Influence, never orders — extended to the counter.** Haggle outcomes move gold, hero mood, and future willingness bands. They NEVER touch party formation, floor choice, or expedition resolution. Pinned by test in PA4.
- **PKD8. Overlay is presentation, station is presentation, faces are presentation.** The forge minigame is a self-contained overlay scene that receives recipe/material/assist context and emits exactly one `CraftAction`; station proximity + CameraRig dolly-in only choose *when* the overlay opens; reaction faces render the sim's computed verdicts with zero new action params (Moonlighter). Phase C's diegetic-3D forge swaps the overlay without a single sim change.
- **PKD9. Rules-change honesty on determinism.** The golden replay is an in-process run-twice comparison, so it re-baselines with the new rules automatically. But replaying a PRE-Phase-A action log through the new kernel produces different quality outcomes — an intentional rules change, not a determinism defect. Saves are state snapshots (KTD4) and still load; the action log stays a diagnostics rider. Documented in the PA2/PA3 PR bodies.

### Contract additions (PA1 micro-PR — the exact shape, orchestrator-only)

All in `sim/GameSim/Contracts/`, deny-listed, landing FIRST as one micro-PR:

- **Actions** (`Actions.cs`, all `[JsonDerivedType]`-registered):
  - `OpenCounterAction()` — Morning-only; flips the day into stepped counter service.
  - `PresentItemAction(ItemId Item)` — show a shelved item to the active customer.
  - `SuggestItemAction(ItemId Item)` — upsell a complementary slot.
  - `HaggleResponseAction(HaggleResponseKind Kind, int? Price = null)` — Accept | HoldFirm | Counter(price).
  - `CloseCounterAction()` — end stepped service; unserved heroes fall back to the atomic pass.
  - `CraftAction` gains TWO trailing optional params (old logs/saves deserialize fine): `CraftPuzzleInput? Puzzle = null` (PKD1 seam, abstract polymorphic record, no derived types in Phase A) and `ImmutableList<int>? SubScores = null` (the three beat scores, stored on the item verbatim for ledger flavor — data, never rules).
- **Enums** (`Enums.cs`): `HaggleResponseKind { Accept, HoldFirm, Counter }`.
- **State** (`World.cs`): `CounterState? Counter { get; init; } = null` on `GameState` (PKD6). `CounterState` record: `ImmutableList<HeroId> Queue`, `HeroId? Active`, `int Round`, `int InterestPermille`, `int PatienceRounds`, `int GoodwillPermille`, `ItemId? Presented`, `int? StandingOfferGold`, `ImmutableSortedSet<int> Served`, `bool Closed`.
- **Heroes** (`Heroes.cs`): `int MoodPermille { get; init; } = 0` on `Hero` (non-positional, save-compat) — the pin-bonus / fleece-memory target. Read by willingness math and gossip only (PKD7).
- **Items** (`Items.cs`): `ImmutableList<int> CraftSubScores { get; init; } = []` on `Item` — beat sub-scores for Evening flavor ("edge quenched brittle").
- **Events** (`Events.cs`): `CustomerApproached(HeroId Hero)`, `CustomerCountered(HeroId Hero, int OfferGold)`, `CounterSaleClosed(HeroId Hero, ItemId Item, int Price, bool Pinned)`, `CustomerWalked(HeroId Hero, ItemId? Item, string Reason)` (reuses R8 legible-reason prose rules).

### Output structure (new/changed paths only)

```text
sim/GameSim/
  Contracts/                       # PA1 micro-PR ONLY (deny-list)
  Crafting/QualityRoller.cs        # PA2: active-model dominance roll
  Crafting/CraftingHandlers.cs     # PA2: auto-craft + sub-score storage
  Professions/ProfessionDefinition.cs  # PA2: active flag + MinigameAssist data
  Counter/                         # PA3+PA4: NEW module dir (one-agent-owned)
    CounterHandlers.cs             # open/present/suggest/haggle/close
    CounterQueueSystem.cs          # Morning stepped queue + atomic fallback
    WillingnessModel.cs            # PA4: bands, class factors, meters
    HaggleResolver.cs              # PA4: round resolution, pin detection
  Kernel/GameKernel.cs             # PA3: Morning phase-hold (HIGH-RISK)
  Heroes/HeroShoppingSystem.cs     # PA3: skip-served gating + fallback pass
  Harness/CounterPlayer.cs         # PA5: scripted stepped policy (pure)
sim/GameSim.Cli/                   # PA5: counter + grade verbs
godot/
  scenes/minigames/                # PA6: NEW — blacksmith forge overlay
  scripts/minigames/               # PA6: beat logic, Advance(delta) test seam
  scripts/panels/CounterPanel.cs   # PA7: NEW — stepped counter service UI
  scripts/panels/ShopStage.cs      # PA7: counter reaction faces (render-only)
  scripts/town3d/                  # PA8: stations, proximity, dolly-in
docs/design/asset-manifest.md      # LIVING — every unit adds its placeholders
docs/design/active-professions-template.md  # PA9: the Phase B template doc
```

### Risks

- **Stepped Morning touches the kernel's phase machine — the plan's highest-risk seam.** Mitigation is structural: PA3 lands the hold + the atomic-equivalence regression (no-counter run byte-identical to pre-PA3) BEFORE any haggle economics exist, and every later unit re-runs that regression. Marked HIGH-RISK-land-tests-first.
- **Quality inversion shifts the 100-day balance bands.** Auto-craft's competent grade replaces the old distribution for every BaselinePlayer craft. PA2 owns re-running (and if needed re-tuning) `Category=Balance` — the gate is green in PA2's own PR, never deferred.
- **Talent remap could silently double-count or zero out mastery.** Resolved by decision (PKD3) and pinned both ways in PA2 tests: unlocked quality talents shift the active roll by exactly 0; assist data is exposed to the adapter and asserted present.
- **Minigame feel is subjective and unprovable in CI.** Vertical-slice-first order (spec DB2); auto-craft de-risks tedium; beat tuning constants are exported data so playtest iteration never touches sim code.
- **Solo review bottleneck.** Max 2–3 parallel subclaudes per wave (plan-of-record research consensus); the decomposition below respects it.
- **Godot editor pin.** All Godot units: 4.6.3-stable .NET ONLY (`.godot-version`); prefer code-built scenes (existing house pattern) to minimize `.tscn` churn.
- **gdUnit 3D headless hang.** PA8 pumps physics near a 3D SubViewport — known trap: disable the viewport's render target update before pumping frames (memory: godot-3d-headless-test-hang).

---

## Scope Boundaries

**Deferred to Phase B (fan-out):**

- Alchemist reagent-puzzle minigame (in-sim scored via the `CraftPuzzleInput` seam PA1 lands) and the enchanter glyph-pattern profession (roster decision deferred to Phase B).
- Shelf-slot arrangement model + browse murmurs (TCG Card Shop Sim), shop-appeal scalar (Winkeltje), overnight racking/aging and station-tier caps (Travellers Rest), calendar-gated craft bonuses (Moonlight Peaks) — adopted mechanics, spec-approved, but not needed to prove the template.
- Ledger flavor lines generated from `CraftSubScores` (the data is stored in Phase A; the prose surface is Phase B polish — PA9 may land a minimal version if free).

**Deferred to Phase C:**

- Fuller diegetic-3D manipulation replacing the focus overlay (the PKD8 seam exists precisely so this swaps in without sim changes).

**Outside this phase's identity:**

- New professions beyond the registered four; any change to hero autonomy, party formation, expedition resolution, or the attribution engine beyond surfacing craft performance as flavor; bespoke themed art (tracked in the manifest, executed in the art-gen phase); timing/twitch pressure inside the SIM (all timing lives in Godot; the sim sees rounds and captured integers only).

---

## Implementation Units

Unit index — dependency order; one unit = one branch (`feat/<slug>`) = one small PR; sim units first, Godot units after their sim seams are merged:

| U-ID | Title | Kind | Key paths | Depends on |
|---|---|---|---|---|
| PA1 | Contracts micro-PR: counter actions, dual-mode craft seam, CounterState | sim (orchestrator-only) | `sim/GameSim/Contracts/` | — |
| PA2 | Quality inversion + talent remap + auto-craft (blacksmith active model) | sim | `sim/GameSim/Crafting/`, `sim/GameSim/Professions/` | PA1 |
| PA3 | Stepped Morning: counter queue, phase-hold, atomic fallback | sim (HIGH-RISK) | `sim/GameSim/Counter/`, `sim/GameSim/Kernel/`, `sim/GameSim/Heroes/` | PA1 |
| PA4 | Haggle economics: willingness bands, meters, pin bonus, conservation | sim | `sim/GameSim/Counter/` | PA3 |
| PA5 | Harness + CLI: CounterPlayer policy, console verbs, replay coverage | sim | `sim/GameSim/Harness/`, `sim/GameSim.Cli/` | PA2, PA4 |
| PA6 | Forge minigame overlay: smelt / forge / quench | godot | `godot/scenes/minigames/`, `godot/scripts/minigames/` | PA2 |
| PA7 | Counter service UI: stepped loop, meters, reaction faces | godot | `godot/scripts/panels/` | PA4 |
| PA8 | Stations: proximity, interact prompt, CameraRig dolly-in | godot | `godot/scripts/town3d/` | PA6 |
| PA9 | Slice integration: playable day, asset manifest sweep, template doc | godot + docs | `docs/design/`, `godot/` | PA5–PA8 |

Parallelization: PA1 lands alone, first. PA2 ∥ PA3 (disjoint directories). PA4 follows PA3; PA6 follows PA2 and runs ∥ PA4. PA5, PA7, PA8 run after their deps as the third wave. PA9 is the sequential closer. Max 2–3 in flight at once (Risks: single reviewer).

### PA1. Contracts micro-PR — counter actions, dual-mode craft seam, CounterState

- **Goal:** Every shared type Phase A needs, landed once, first, by the orchestrating session (KTD9) — dependents build against a frozen contract; in-flight agents rebase.
- **Covers:** spec DB3/DB5/DB6 contract halves; PKD1/PKD6; spec §Sim contract additions.
- **Dependencies:** —. **Branch:** `feat/pa1-counter-contracts`.
- **Files:** `sim/GameSim/Contracts/Actions.cs`, `Enums.cs`, `Events.cs`, `World.cs`, `Heroes.cs`, `Items.cs`; `sim/GameSim.Tests/Contracts/CounterContractTests.cs`.
- **Approach:** Exactly the shapes pinned in §Contract additions above — no handler, no system, no behavior. New actions have no registered handler yet, so the kernel's existing "No handler accepts" rejection covers them safely until PA3. All new state is non-positional init members with save-compat defaults (the `InFlight`/`Venues` pattern). `CraftPuzzleInput` ships as an abstract `[JsonPolymorphic]` record with zero derived types and a doc note that Phase B registers derived types via micro-PR.
- **Test scenarios:**
  - Polymorphic round-trip: every new action serializes → deserializes to an equal record inside a `LoggedBatch`.
  - Save-compat: a pre-PA1 `GameState` JSON (no `Counter`, heroes without `MoodPermille`, items without `CraftSubScores`) deserializes with defaults (null / 0 / empty).
  - Old-log compat: a `CraftAction` serialized without `Puzzle`/`SubScores` deserializes to nulls; a null-everything `CraftAction` round-trips byte-identically.
  - Kernel safety: submitting `PresentItemAction` today → typed `RejectedAction` ("No handler accepts"), state unchanged, zero RNG drawn.
  - Determinism: full existing fast lane passes untouched (contracts additions must be behavior-neutral).
- **Verification:** `dotnet test sim/GameSim.Tests/GameSim.Tests.csproj --filter Category!=Balance` green; balance gate green (must be trivially unaffected); merged BEFORE PA2/PA3 branch.

### PA2. Quality inversion + talent remap + auto-craft (blacksmith active model)

- **Goal:** Skill drives quality (spec DB5): for the blacksmith, `PerformanceGrade` dominates, RNG shrinks to a floor jitter, material sets the ceiling, talents become minigame-assist data, and auto-craft is competent-but-capped (spec DB6) — while every passive profession stays byte-identical.
- **Covers:** DB5, DB6; PKD2/PKD3/PKD4; anti-tedium grade cap.
- **Dependencies:** PA1. **Branch:** `feat/pa2-quality-inversion`.
- **Files:** `sim/GameSim/Crafting/QualityRoller.cs`, `sim/GameSim/Crafting/CraftingHandlers.cs`, `sim/GameSim/Crafting/ItemForge.cs` (sub-score stamping), `sim/GameSim/Professions/ProfessionDefinition.cs` + the blacksmith definition data; `sim/GameSim.Tests/Crafting/` (rewrite `QualityRollerTests`/`PerformanceGradeTests` threshold tables, new `ActiveQualityModelTests`), `sim/GameSim.Tests/Professions/` (passive regression pins).
- **Approach:** `ProfessionDefinition` gains `bool ActiveCraft` and `ImmutableSortedDictionary<string, MinigameAssist> MinigameAssists` (per-node: sweet-zone width bonus, drift-rate reduction, off-beat forgiveness — per-mille integers, pure data the adapter reads; sim never interprets them beyond exposure). Blacksmith flips `ActiveCraft: true`, empties its `FlatShifts`/`SlotShifts` (double-count resolved — PKD3), keeps `MaterialMasteryNode` (material axis, no overlap), and maps the retired quality nodes (keen-eye, master-touch, legendary-craft, weapon-specialist) to assist data. `QualityRoller` branches on the flag: **active path** — `effective = clamp(grade, 0..1000) + jitter` where jitter maps the single `Roll100` draw to `[-25..+25]` per-mille (one draw either path — draw-count contract holds); grade bands read off `effective` (starting table, tests pin the exact final numbers: Poor <200, Common 200–549, Fine 550–779, Superior 780–929, Masterwork ≥930 — band width ≥121 ≫ jitter 50, so RNG can never cross a band alone); ceiling from `materialGrade + mastery − recipe.Tier` (≤ −1 → Fine cap, 0 → Superior cap, ≥ +1 → uncapped). Auto-craft: null grade + null puzzle on an active profession resolves at the competent constant (550) AND hard-caps at Superior regardless of jitter or future constants (never Masterwork — belt and braces). **Passive path:** the existing ±8 threshold-table code, untouched. `CraftingHandlers` stamps `SubScores` onto the item verbatim. `BaselinePlayer` needs no edit (already null-grade) — but this unit owns re-running and if necessary re-tuning the balance gate, since blacksmith craft quality distribution changes under it.
- **Test scenarios:**
  - Dominance table: grades {0, 199, 550, 780, 930, 1000} at neutral material → exact expected bands across the full jitter range (property over all 100 roll outcomes) — RNG can never move a result across a band boundary by itself.
  - Ceiling: grade 1000 on below-tier material never exceeds Fine; on-tier never exceeds Superior; material-mastery lifts the ceiling exactly one step; above-tier reaches Masterwork.
  - Auto-craft: null grade on blacksmith → competent band, and can NEVER produce Masterwork (property over all rolls and all material grades).
  - Talent decount: blacksmith with every quality talent unlocked vs none → identical distribution for identical grade (talents shift the active roll by exactly 0); assist data for each remapped node is present and non-degenerate.
  - Passive regression (byte-identical pin): alchemy/engineering/tanning crafts with a fixed seed produce the exact same items as before this PR (golden values inlined in the test).
  - Draw count: exactly one `Roll100` per successful craft on both paths; rejects draw zero.
  - Sub-scores: `CraftAction` with `SubScores: [812, 640, 905]` → item carries them verbatim; null → empty.
  - Determinism/golden-replay impact: run-twice replay stays green by construction (both runs use new rules); PR body documents the PKD9 rules-change note for old logs.
- **Verification:** fast lane green; `--filter Category=Balance` green (re-tuned in this PR if bands moved — never handed downstream).

### PA3. Stepped Morning — counter queue, phase-hold, atomic fallback (HIGH-RISK)

- **Goal:** The Morning phase becomes a stepped customer queue behind `OpenCounterAction`, holding the phase machine at Morning per haggle step — while a run that never opens the counter stays byte-identical to today (spec DB3; PKD5).
- **Covers:** DB3; PKD5/PKD6; spec §three-layer day loop Morning row.
- **Dependencies:** PA1. **Branch:** `feat/pa3-stepped-morning`. **HIGH-RISK-land-tests-first:** the phase-hold and atomic-equivalence tests are the first commit; behavior follows.
- **Files:** `sim/GameSim/Counter/CounterHandlers.cs`, `sim/GameSim/Counter/CounterQueueSystem.cs` (new module dir — this agent owns it), `sim/GameSim/Kernel/GameKernel.cs` (Advance hold), `sim/GameSim/Kernel/GameFactory.cs` (registration), `sim/GameSim/Heroes/HeroShoppingSystem.cs` (skip-served gate + fallback); `sim/GameSim.Tests/Counter/`, `sim/GameSim.Tests/Kernel/` (phase tests).
- **Approach:** `OpenCounterAction` (Morning-only `CanHandle`) initializes `CounterState`: queue = alive heroes in HeroId order (the existing deterministic shopping order), first customer becomes `Active`, `CustomerApproached` emitted. `GameKernel.Advance` becomes state-aware for exactly one case: `Morning` with an open, unfinished `CounterState` → stays `(day, Morning)`; every other transition is the verbatim existing switch. Each subsequent Morning Tick applies the player's counter actions (handled by `CounterHandlers`) and `CounterQueueSystem` advances the session: resolve the active customer (bought / walked / round consumed — resolution math is a PA4 seam, PA3 ships a minimal deterministic placeholder: present → buy iff the existing `ShoppingAi.EvaluateItem` says Buy at list price, else walk with its reason), dequeue to the next customer, and when the queue empties or `CloseCounterAction` lands, mark `Closed`. On the closing tick, `HeroShoppingSystem` runs its atomic pass for UNSERVED heroes only (`CounterState.Served` gate) — nobody shops twice, nobody starves; consumable pass unchanged. `CounterState` resets to null when Morning finally advances. "No active customer" (queue empty, player arranging) is a valid open state. Wrong-phase/wrong-state counter actions → typed rejections before any RNG.
- **Test scenarios:**
  - **Atomic equivalence (the pin, lands first):** a 30-day run submitting no counter actions is byte-identical to the same run on the pre-PA3 kernel (golden values inlined) — the fast lane's existing determinism suite must also pass unmodified.
  - Phase-hold: `OpenCounter` → N ticks stay Morning; `CloseCounter` (or queue exhaustion) → next Advance goes to Expedition; day counter never double-advances.
  - Queue order: customers approach in HeroId order; dead heroes never queue; recruit added mid-save queues next morning.
  - Fallback: open counter, serve 2 of 6, close → exactly the 4 unserved heroes run the atomic pass that same tick, in HeroId order; `Served` heroes excluded.
  - Placeholder resolution: presenting a strict-upgrade affordable item → `CounterSaleClosed` at list price, gold moves exactly (conservation); presenting a role-mismatch item → `CustomerWalked` with the R8 reason prose.
  - Rejections: `PresentItem` with no session / not Morning / unshelved item / no active customer → typed reasons, no RNG drawn, state unchanged.
  - Determinism: same seed + same stepped action sequence run twice → byte-identical; save mid-session → load → continue ≡ uninterrupted (CounterState round-trips through SaveCodec).
  - Balance gate: green untouched (BaselinePlayer never opens the counter — asserted by the atomic-equivalence pin).
- **Verification:** fast lane green; `--filter Category=Balance` green; PR body carries the PKD9 note and names the atomic-equivalence test.

### PA4. Haggle economics — willingness bands, meters, pin bonus, conservation

- **Goal:** The deterministic Potionomics/Recettear haggle: per-class willingness bands that shift per round, Interest/Patience/Goodwill meters, a ~3-round cap, the "pin" mood bonus, and upsell — all integer math, all influence-never-orders.
- **Covers:** DB3 economics half; spec §Counter service; PKD6/PKD7; extends U7's gold conservation.
- **Dependencies:** PA3 (same module dir — sequential, ideally the same agent continues). **Branch:** `feat/pa4-haggle-economics`.
- **Files:** `sim/GameSim/Counter/WillingnessModel.cs`, `sim/GameSim/Counter/HaggleResolver.cs`, `CounterHandlers.cs`/`CounterQueueSystem.cs` (replace PA3's placeholder resolution); `sim/GameSim.Tests/Counter/`.
- **Approach:** Willingness-to-pay derives from the EXISTING utility (no second AI system): base = `ShoppingAi.EvaluateItem`'s gear-score gain and the hero's gold, times a per-class price-factor table (Recettear: Vanguard overpays for a fitting shield, Skirmisher stingy — data in `Counter/`, keyed by class id, integer per-mille). The band `[floor..ceiling]` around list price widens/shifts per surviving round (+N per-mille per round) so HoldFirm can genuinely win round 2 — never a trap. Meters: `PresentItem` with strong role-fit = opener Interest bonus; `SuggestItem` on a complementary empty slot = upsell Interest bonus; each `HaggleResponse` consumes one `PatienceRounds` (init ~3, data); Patience 0 → `CustomerWalked` with a legible reason; `Counter` above the round's ceiling costs Goodwill (fleece memory → `MoodPermille` down, feeds future bands and gossip); `Counter` landing within the pin window of true willingness → sale plus `Pinned: true` and a `MoodPermille` bonus (reading the hero IS the counter skill). Anti-solved-meta: role-fit and mood move the band enough that one global markup leaves money/loyalty behind (pinned by a two-scenario test, not a vibe). All resolution is pure integer functions of (state, action) — zero RNG in the haggle (spec: no timing, no dice; a slow player and a fast player converge).
- **Test scenarios:**
  - Band math table: fixed hero/item/class fixtures → exact willingness, floor, ceiling per round for rounds 1–3 (tests pin the table; changing constants changes both together).
  - HoldFirm value: a scripted customer whose round-2 band accepts what round-1 refused (the Recettear shift is real).
  - Patience: 3 responses → walk with reason; Accept on round 1 → sale at offer; no fourth round exists.
  - Pin: counter within the pin window → `Pinned: true`, mood bonus applied; outside → normal sale, no bonus; fleece (over-ceiling counter) → Goodwill/mood penalty recorded.
  - Per-class factor: same item, Vanguard vs Skirmisher → distinct bands per the data table; role-fit + mood shifts beat any single global markup (anti-107% pin).
  - Conservation: property test over a stepped morning — every gold movement conserves (extends U7's test to counter sales).
  - **Influence-never-orders (PKD7 pin):** two identical runs differing only in counter mood outcomes → party formation, floor choice, and `ExpeditionResult` identical; mood touches shopping/gossip surfaces only.
  - Determinism: full stepped-morning action script run twice → byte-identical; zero RNG draws in the entire haggle path (stream position asserted unchanged).
- **Verification:** fast lane green; `--filter Category=Balance` green (atomic path still untouched); conservation property named in the PR.

### PA5. Harness + CLI — CounterPlayer policy, console verbs, replay coverage

- **Goal:** The stepped loop is playable in text and exercised by the determinism suite end-to-end: golden-replay coverage for a minigame-graded craft AND a counter sale (spec success criterion 2).
- **Covers:** spec SC2; R21 tradition (console = permanent debug harness).
- **Dependencies:** PA2, PA4. **Branch:** `feat/pa5-counter-cli-harness`.
- **Files:** `sim/GameSim/Harness/CounterPlayer.cs` (new — pure scripted stepped policy: open counter, present best role-fit shelf item, counter at band-center, close; no IO/RNG/clock, `BaselinePlayer` untouched and never forked), `sim/GameSim.Cli/` (verbs: `counter open|present <id>|suggest <id>|haggle accept|hold|counter <g>|close`, `craft <recipe> [grade <0-1000>]` for grade-in-hand testing), `sim/GameSim.Tests/Kernel/` (replay extension).
- **Approach:** CLI verbs are thin action constructors over the existing loop — zero rules. `CounterPlayer` lives beside `BaselinePlayer` in `Harness/` under the same purity contract and exists so tests (and the batch farm, optionally) can drive stepped mornings deterministically. Extend the determinism suite: a scripted 20-day run that includes at least one `CraftAction` with an explicit `PerformanceGrade` + `SubScores` and one full haggled counter sale, run twice → byte-identical; save/load mid-haggle round-trips.
- **Test scenarios:**
  - Replay coverage: the 20-day stepped script × 2 runs → byte-identical serialized state (the spec's "golden-replay covers a minigame craft and a counter sale" — named test).
  - CounterPlayer purity: same state → same actions, every call; policy handles empty-shelf and no-customer mornings without error.
  - CLI: verb parsing round-trips to the exact action records (unit-level, no interactive test).
  - Balance gate: green, and a one-line assertion that `BaselinePlayer` output is unchanged from pre-Phase-A for a fixed state fixture (never forked — pinned).
- **Verification:** fast lane + balance gate green; manual: `dotnet run --project sim/GameSim.Cli` — play one full day with a graded craft and a haggled sale in text.

### PA6. Forge minigame overlay — smelt / forge / quench (Godot)

- **Goal:** The three-beat blacksmith minigame as a self-contained focus overlay: readable heat-and-timing skill (never twitch), carry-forward flaw, talent assists visible — emitting exactly one `CraftAction` with the folded `PerformanceGrade` + `SubScores`.
- **Covers:** DB1/DB4/DB5 render halves; spec §Blacksmith minigame; PKD8. Adapter-only (KTD2): no game rules in Godot.
- **Dependencies:** PA2 (assist data + grade semantics merged). **Branch:** `feat/pa6-forge-minigame`.
- **Files:** `godot/scenes/minigames/` + `godot/scripts/minigames/` (new: `ForgeMinigame.cs` host + `SmeltBeat.cs`/`ForgeBeat.cs`/`QuenchBeat.cs`), `godot/scripts/panels/ForgePanel.cs` (a "Work the forge" button per recipe beside the existing Craft button — which becomes the explicit auto-craft path, relabeled "Auto-craft (competent)"), `godot/tests/minigames/`, `docs/design/asset-manifest.md` (overlay art placeholders).
- **Approach:** Beats per spec: **Smelt** — heat gauge rises and drifts; stop-in-band hold (Spiritfarer); sub-score = distance-from-band-center per-mille; over/under-heat records an impurity. **Forge** — a shaping-progress budget to fill before the glow cools (Fantasy Life); on-beat strikes fill more; off-beat mars; the smelt impurity renders as visible dross here (Jacksmith carry-forward flaw — causal chain, and it debits the forge sub-score cap). **Quench** — stop-the-needle on the color/temperature readout. Difficulty (drift speed, band width, cooling rate) scales with recipe tier + material grade; talent assists (from `ProfessionDefinition.MinigameAssists` via the adapter's render state) widen bands / slow drift / forgive off-beats — mastery visible in the act. Folding weights (3 sub-scores → one grade) are exported data. House testability pattern (ShopStage precedent): a public `Advance(double delta)` accumulated-clock drive plus an input seam (`SetDirectInput`-style), so gdUnit tests score scripted runs without wall-clock or engine RNG. On completion the overlay queues exactly one `CraftAction(recipeId, material, grade, subScores)` through `SimAdapter` and closes; cancel queues nothing. Standalone-openable from `ForgePanel` in this unit — PA8 adds the station entrance.
- **Test scenarios (gdUnit4Net, `[RequireGodotRuntime]`):**
  - Deterministic scoring: a scripted perfect input sequence via `Advance` → grade ≥ the Masterwork-reachable threshold; a scripted sloppy run → mid grade; same script twice → identical grade (no hidden randomness).
  - Carry-forward: scripted smelt impurity → forge beat exposes the dross state and its sub-score cap; sub-scores land in the emitted action in beat order.
  - Single-action contract: one completed run → exactly one queued `CraftAction`; cancel mid-beat → zero actions queued.
  - Assist wiring: assist data present → band-width/drift parameters differ from no-talent baseline (asserted on the beat's exported parameters, not pixels).
  - Adapter fidelity: the emitted action applied to the sim produces an item whose quality matches PA2's table for that grade.
- **Verification:** `dotnet build Game.sln` + engine suite (`dotnet test godot/tests --settings .runsettings`) green locally and on CI; manifest updated; manual smoke — forge one item via the minigame from the panel.

### PA7. Counter service UI — stepped loop, meters, reaction faces (Godot)

- **Goal:** The Morning counter played through real UI: customer approaches, present/suggest/haggle controls bound to the new actions, meters readable, verdicts rendered as reaction faces — evolving `ShopPanel`/`ShopStage`, adapter-only.
- **Covers:** DB3 render half; Moonlighter faces (pure render, zero new action params); PKD8.
- **Dependencies:** PA4. **Branch:** `feat/pa7-counter-service-ui`.
- **Files:** `godot/scripts/panels/CounterPanel.cs` (new), `godot/scripts/panels/ShopStage.cs` (counter choreography + face mapping extension), `godot/scripts/panels/ShopPanel.cs` (open-counter entry + shelf reuse), `MainUi`/`InteriorStage` wiring, `godot/tests/panels/`, `docs/design/asset-manifest.md`.
- **Approach:** `CounterPanel` binds `state.Counter`: active customer card (class, mood hint), Interest/Patience/Goodwill meter chips (render of sim integers — no local math), the presented item, the hero's standing offer, and buttons `Present_{id}` / `Suggest_{id}` / `Accept` / `HoldFirm` / `Counter` (+price SpinBox) / `CloseCounter` queueing the PA1 actions verbatim through `SimAdapter`. Follow the house gate pattern (`GateButton` mirrors the sim's legality checks visually; the kernel stays the real gate). `ShopStage` gains counter choreography: customer walks to the counter instead of a shelf slot, and the emote-glyph face maps off the sim's resolution events (`CounterSaleClosed` pinned → Heart; sale → Smile; walked-patience → Frown; walked-other → Shrug) — computed-verdict render only, no new params (Moonlighter). Async prep stays available: between customers the existing shelf/reprice/unstock controls remain live (spec: "no active customer" is a valid state). Keep every existing control `Name` stable (house rule — tests drive signals by name).
- **Test scenarios (gdUnit4Net):**
  - Bind: mid-haggle `CounterState` fixture renders customer, meters, round, and offer without error; null `Counter` renders the arrange-only layout.
  - Action fidelity: each button press queues exactly the intended action record (asserted on the adapter queue); a scripted stepped morning through UI signals only ≡ the same actions applied directly to the sim (adapter fidelity — the U11 pattern).
  - Faces: each resolution event kind maps to its pinned `EmoteKind`; walk reason prose renders on the card (R8 render half).
  - Meters: sim meter integers render 1:1 (no UI-side arithmetic — asserted by fixture).
- **Verification:** engine suite green; manifest updated (counter/customer art placeholders); manual smoke — serve a 3-customer morning through the panel, one haggled sale, one walk with visible reason.

### PA8. Stations — proximity, interact prompt, CameraRig dolly-in (Godot)

- **Goal:** The 3D town hosts the loop: walk to the forge or counter station, get an interact prompt, camera pushes in, the focus overlay opens (spec DB4) — presentation only, and the Phase C swap seam stays clean.
- **Covers:** DB4; PKD8.
- **Dependencies:** PA6 (overlay entry seam); runs ∥ PA7. **Branch:** `feat/pa8-town-stations`.
- **Files:** `godot/scripts/town3d/Town3D.cs` (station volumes + props), `PlayerController.cs` (proximity set + interact input), `CameraRig.cs` (dolly-in/out API: target + distance override with the existing exponential ease), `WorldInput3D.cs`/`TownInput.cs` (interact action), `godot/tests/town3d/`, `docs/design/asset-manifest.md`.
- **Approach:** Two station interaction volumes (Area3D): forge (anvil/furnace prop cluster) and shop counter — Kenney/primitive placeholders, logged in the manifest. `PlayerController` tracks the nearest in-range station and raises an event on interact (mirror of the existing `ArrivedAtBuilding` pattern — click-to-move to a station reuses `MoveToAndInteract`, WASD proximity adds the prompt path); never opens instantly mid-walk (KTD12 precedent). `CameraRig` gains `PushIn(Node3D focus, float distance)` / `Release()` using the existing frame-rate-independent ease; the town host opens the PA6 forge overlay or the PA7 counter panel on arrival and releases the camera on close. Zero sim contact in this unit beyond opening surfaces that already speak to the adapter.
- **Test scenarios (gdUnit4Net — heed the 3D headless trap: disable the SubViewport's render-target update before pumping physics frames):**
  - Proximity: player scripted into the forge volume → prompt state set; leaving clears it; two stations → nearest wins deterministically.
  - Interact: interact inside the volume raises the station event with the right station key; outside → no-op; mid-click-move → only fires on arrival.
  - Dolly: `PushIn` converges toward the override target/distance (asserted over pumped frames); `Release` restores follow behavior; no NaNs at zero delta.
  - Wiring: station event → correct overlay/panel opened; overlay close → camera released (order asserted).
- **Verification:** engine suite green (headless, no hang); manifest updated (station props); manual smoke — walk to the forge, dolly-in, run one minigame craft, walk to the counter, serve a customer.

### PA9. Slice integration — playable day, asset manifest sweep, template doc

- **Goal:** The spec's success criteria proven whole: a human plays a full active day; the manifest is current; the blacksmith slice is documented as the Phase B template — no tribal knowledge.
- **Covers:** spec SC1–SC4; org docs rule.
- **Dependencies:** PA5–PA8. **Branch:** `feat/pa9-slice-integration`.
- **Files:** `docs/design/active-professions-template.md` (new), `docs/design/asset-manifest.md` (sweep), `docs/debugging.md` (counter/minigame log map entries), `README.md`/`CLAUDE.md`-adjacent docs if commands changed (CLAUDE.md itself is deny-listed — any needed edit is a CONTRACT-REQUEST to the orchestrator), small integration glue only (no new features).
- **Approach:** Full-day manual smoke on the assembled slice (the spec's SC1 script: counter → haggled sale → forge minigame → quality + ledger reflection) with fixes limited to integration seams. Template doc captures, for a fresh Phase B subclaude: the dual-mode craft seam and how an in-sim-scored puzzle plugs in (`CraftPuzzleInput` derived type via micro-PR), the counter/queue extension points, the minigame overlay skeleton (Advance-driven testing pattern, assist-data wiring, single-action contract), the station/dolly hookup, and the per-unit manifest duty. Manifest sweep: verify every PA6–PA8 placeholder is listed with its intended bespoke replacement. Stretch (only if free): minimal Evening ledger flavor line from `CraftSubScores`.
- **Test scenarios:** Test expectation: no new sim behavior — full fast lane + balance gate + engine suite re-run green at head; one added engine-level integration test if the smoke exposed an untested seam (else none).
- **Verification:** All three suites green at the integrated head; manual full-day smoke per SC1 recorded in the PR body; template doc reviewed by the orchestrator; manifest current.

---

## Subclaude decomposition

Single-reviewer ceiling: **max 2–3 implementation agents in flight** (plan-of-record Risks). Sim first, Godot after its seams merge. Every agent: claim the unit in `.claude/tasks/` before starting, read `BOARD.md` at session start and after any rebase failure, stage only the unit's files (no `git add .`), conventional commits, one branch = one PR, rebase-and-rerun when stale (auto-merge is on). Shared root is READ-ONLY for subagents — all work happens in each unit's own worktree/branch. Contracts, `Game.sln`, `project.godot`, `.github/`, `CLAUDE.md`, `global.json`, `Directory.Build.props`, `.godot-version` are deny-listed for everyone but the orchestrator; a needed contract change mid-unit is a CONTRACT-REQUEST escalation, never a local edit.

| Wave | Units | Mode | Model / effort | Notes |
|---|---|---|---|---|
| 0 | PA1 | sequential, orchestrating session itself | (orchestrator) | KTD9: contracts micro-PR, merged before anything branches |
| 1 | PA2 ∥ PA3 | 2 parallel agents | sonnet / high effort both | Hard-reasoning pair; disjoint dirs (Crafting+Professions vs Counter+Kernel+Heroes). PA3 is HIGH-RISK-land-tests-first |
| 2 | PA4 ∥ PA6 | 2 parallel agents | PA4: sonnet / high (ideally the PA3 agent continued via SendMessage — same module dir, context intact). PA6: sonnet / medium | PA4 replaces PA3's placeholder resolution; PA6 builds against PA2's merged seam |
| 3 | PA5 ∥ PA7 ∥ PA8 | 3 parallel agents (the cap) | PA5: sonnet / low (mechanical). PA7: sonnet / medium. PA8: sonnet / medium | Disjoint: Harness+Cli vs panels vs town3d. PA8 prompt MUST carry the 3D-headless-hang guardrail |
| 4 | PA9 | sequential, 1 agent + orchestrator review | sonnet / medium | Integration closer; template doc is the Phase B unlock |

Model policy: sonnet fleets per the standing token-economy rule; escalate a single unit to a stronger model only with Brian's approval — the one candidate worth asking about is PA3 if the phase-hold proves gnarlier than planned. No unit needs web access; no unit runs `rm`/watch/interactive commands.

**Self-verify before reporting done (non-negotiable, per unit):**

- PA1–PA5 (sim): `dotnet test sim/GameSim.Tests/GameSim.Tests.csproj --filter Category!=Balance` AND `dotnet test sim/GameSim.Tests/GameSim.Tests.csproj --filter Category=Balance` — both green locally, then CI green on the PR.
- PA6–PA8 (Godot): `dotnet build Game.sln` + `dotnet test godot/tests --settings .runsettings` (GODOT_BIN per `.runsettings`; if the local engine is unavailable, the CI engine lane must be green before the unit is reportable) — plus the fast lane (cheap, catches accidental sim touches, which are forbidden for these units anyway).
- PA9: all three suites at the integrated head + the SC1 manual smoke, evidence in the PR body.

No unit is reportable as complete until its self-verify passed and CI is green on the PR (hard rule 1).

---

## Verification Contract

| Gate | Command | When | Proves |
|---|---|---|---|
| Sim suite | `dotnet test sim/GameSim.Tests/GameSim.Tests.csproj --filter Category!=Balance` | Every PR (fast lane) | PA1–PA5 scenarios; atomic-equivalence pin; passive-profession byte-identical pin; zero-RNG haggle; run-twice determinism incl. graded craft + counter sale |
| Balance sim | `dotnet test sim/GameSim.Tests/GameSim.Tests.csproj --filter Category=Balance` | Every PR (required step) | 100-day bands under auto-craft competent baseline; BaselinePlayer never forked; stepped code paths dormant under baseline play |
| Engine suite | gdUnit4Net via `dotnet test godot/tests --settings .runsettings` (CI engine lane) | Every PR touching `godot/` | Minigame deterministic scoring + single-action contract; counter UI adapter fidelity; station/dolly behavior |
| Merge gate | GitHub ruleset: PR + required checks + branch up-to-date + auto-merge | Every merge | Nothing lands red; merges serialized at 2–3-agent scale |
| Console smoke | `dotnet run --project sim/GameSim.Cli` — one day with a graded craft + a haggled sale in text | PA5 completion | Stepped loop playable headless, pre-UI |
| Playable smoke | Manual: full day — counter (≥1 haggled sale) → forge minigame (≥1 item) → quality + ledger reflection | PA9 completion | Spec success criterion SC1 for real humans |

The atomic-equivalence test (PA3) and the passive-profession pin (PA2) are the two regressions every subsequent PR must keep green — they are what make this an additive slice instead of a rules rewrite.

---

## Definition of Done

- All nine units merged to `main` through the protected ruleset with green required checks; PA1 landed first as the orchestrator-authored contracts micro-PR (KTD9).
- Spec success criteria met: a human plays a full day (counter haggled sale + minigame-forged item + quality/ledger reflection); fast lane + balance gate green; the determinism suite's run-twice replay covers a minigame-graded craft and a counter sale by name.
- The two structural pins hold at head: a no-counter run is byte-identical to pre-Phase-A, and passive professions' quality math is byte-identical to pre-Phase-A.
- Auto-craft can never produce Masterwork (named property test); the minigame is the only road to the top.
- Counter outcomes provably never alter hero expedition choices (named PKD7 test) — influence, never orders.
- `docs/design/asset-manifest.md` lists every placeholder this slice shipped (forge overlay, counter/customer art, station props) with intended replacements; no orphans — no dead placeholder code, no unused constants from abandoned tuning.
- `docs/design/active-professions-template.md` exists and is sufficient for a fresh subclaude to build the Phase B alchemist loop without tribal knowledge; `docs/debugging.md` carries the counter/minigame log map entries.
- Deferred items (Phase B fan-out, Phase C diegetic-3D, arrangement/racking/station-tier/calendar mechanics, sub-score ledger prose) recorded here in Scope Boundaries — nothing else left implicit.

---

## Appendix — Seam verification notes (2026-07-21, against worktree head)

- `QualityRoller.PerformanceShiftMax = 8` confirmed (`sim/GameSim/Crafting/QualityRoller.cs`) — the "never dominating" clamp PA2 inverts; threshold-table doc comment explicitly says tests assert the exact table, change both together.
- `CraftAction.PerformanceGrade` already threaded end-to-end (`Contracts/Actions.cs` → `CraftingHandlers.ApplyCraft` → roller); null = neutral today, becomes auto-craft-competent for active professions in PA2 (passive professions keep null = neutral).
- Golden replay is an in-process run-twice comparison (`GameSim.Tests/Kernel/DeterminismTests.cs`), not a committed golden file — quality-rule changes re-baseline automatically; PKD9 covers the old-log honesty note.
- `GameKernel.Advance` is a pure 5-phase switch (Morning→Expedition→Camp→ExpeditionDeep→Evening); the Morning hold (PKD5) is its only planned change.
- `GameState` already carries two non-positional save-compat init members (`InFlight`, `Venues`) — `CounterState` follows the identical pattern.
- `Hero` has no mood/loyalty field today — hence the PA1 `MoodPermille` addition; `ItemMemory` (kills/saves) is unrelated and untouched.
- `HeroShoppingSystem` shops in HeroId order, gear pass then consumable pass — the counter queue reuses that exact order; the fallback pass slots in per-hero via the `Served` gate.
- `BaselinePlayer` emits `CraftAction(recipeId, materialKey)` with null grade already — zero harness change for PKD4.
- `ShopStage` already demonstrates the deterministic-presentation house pattern PA6/PA7 reuse: public `Advance(double)` accumulated clock, no wall-clock, no engine RNG, remove-then-Free; its `EmoteKind` mapping extends for counter verdicts.
- `PlayerController.MoveToAndInteract`/`ArrivedAtBuilding` and `CameraRig`'s exponential-ease follow are the exact hooks PA8 extends; `ForgePanel`'s `Craft_{recipeId}` buttons become the relabeled auto-craft path beside PA6's minigame entry.
