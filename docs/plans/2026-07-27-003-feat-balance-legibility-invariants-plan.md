---
title: "Wave 3 — Balance Legibility + Invariants"
date: 2026-07-27
type: feat
artifact_contract: ce-unified-plan/v1
artifact_readiness: implementation-ready
execution: code
source: docs/design/2026-07-27-five-pillars-design-synthesis.md (Pillar 3, Unified build sequence — Wave 3)
predecessors: docs/design/2026-07-27-how-you-play.md, docs/design/2026-07-27-gameplay-loop-analysis.md
---

# Wave 3 — Balance Legibility + Invariants

## Goal Capsule

- **Objective:** The measured curve is right (5.13×/5.78× gold under skilled play; floor 4 beatable by
  skill; the sim's own `BalanceSimTests.cs` bands already hold) — **the problem is legibility, not
  tuning.** The quality wall at floor 5 (`Superior+` required, but auto-craft only reaches Superior by
  RNG per `QualityRoller.RollActive`'s hard auto-craft cap) is invisible to the player as a LEVER
  ("the minigame/Masterwork path is the intended route") rather than a wall, so players grind
  auto-crafts hoping for a lucky Superior roll instead of engaging the forge minigame. Fix this with
  **information** — a deterministic quality-forecast band on the craft screen — never a rate buff (a
  rate buff would make the *wrong* strategy, grinding, more viable, tuning toward the failure mode).
  Also land the **10 balance-sim invariants** as permanent CI gates (three policies — Skilled/Naive/
  Greedy — over ≥20 seeds) so the curve this wave documents as "right" gets pinned before Wave 2's
  demand diversification can silently drift it.
- **NO number retuning in this wave.** Every existing constant in `CombatMath`, `QualityRoller`,
  `VenueFloor`, `CommissionSystem`'s premium/deadline tables, and `BalanceSimTests`'s band constants
  stays exactly as measured. This wave adds legibility (deterministic forecast, invariant tests,
  destitution pre-warning) and one narrow softening (an early, player-chosen low-pay income
  affordance — see KTD-C7) — it does not change what the sim actually rolls or gates.
- **Ordering nuance (explicit — read before sequencing this wave against Wave 2):** this wave's
  invariant harness should land and pass **against the CURRENT (pre-Wave-2, `DiversifiedDemand =
  false`) curve first**, pinning it as the baseline the invariants protect, **before** Wave 2's flag
  gets flipped on by default. Wave 2 can build and merge its own PRs in parallel (its flag defaults
  false, so it doesn't perturb this wave's baseline measurement) — but the SEQUENCE that matters is:
  Wave 3's invariants exist and pass on the flat-demand curve BEFORE anyone flips `DiversifiedDemand`
  to true by default, so that flip is measured against a pinned reference rather than tuned blind.
- **Stop conditions:** any PR in this wave that changes a `BalanceSimTests` band constant's VALUE
  without an explicit, narrated tuning justification (there should be none — this wave adds tests, it
  doesn't re-fit existing ones); any invariant implementation that requires a new RNG draw site.

## Scope Boundaries (what this wave does NOT do)

- Does **not** retune combat, quality-roll, commission-premium, or venue-gate constants. If an
  invariant FAILS against the current curve, the correct response is to inspect whether the invariant
  itself is mis-specified relative to the synthesis's own measured findings (§Pillar 3 diagnosis) —
  not to reach for a constant change without flagging it to the orchestrator first.
- Does **not** add a 6th forge slot — the synthesis explicitly defers that to a later forge-upgrade
  unit, gated on Wave 2's demand diversification already having shipped (so the slot relieves the NEW
  4-craft pressure, not today's 1-craft-dominant pressure). Out of scope here entirely.
- Does **not** build `NeedsEngine`/`DemandLine`/hazards (Wave 2) or profession unlocks (Wave 4) — this
  wave's invariants are written against BOTH the flat model (must pass today) and, once Wave 2 merges,
  re-run against the diversified-flag-on state as a **separate, explicitly-labeled** measurement (not
  a silent replacement of the flat-model baseline).
- Does **not** implement "difficulty modes" as a menu of separate rulesets — advisor verbosity (Wave 1's
  Guidance tiers) IS the difficulty knob; this wave only adds the mapping from verbosity tier to which
  legibility surfaces render (full plans vs. warnings-only vs. cliff-warnings-only), a presentation
  concern layered on Wave 1's existing `GuidanceLevel`, not a new sim-side mode.

## Cross-pillar risks that apply here

- **Risk 1 (sequencing, synthesis §Cross-pillar risks):** this wave IS one of the two things (along
  with Wave 1's fulfillability gate) the synthesis says must be "pre-landed" before Wave 2's demand
  diversity ships by default, specifically so the invariant harness catches an expiry/destitution-rate
  spike instead of it going unmeasured. This wave's invariants are therefore written to be RE-RUNNABLE
  against `DiversifiedDemand = true` once Wave 2 exists, not single-use against the flat model only.
- **Risk 4 (day-14 hazard force-activation fallback):** not this wave's mechanism to build (Wave 4), but
  this wave's invariant #7-style "nothing surfaced expires" discipline is the general pattern Wave 4's
  fallback tuning should be measured against later — noted here so Wave 4's plan doesn't need to
  re-derive the invariant-test shape from scratch.

## Key Technical Decisions

- **KTD-C1 — the quality forecast is a deterministic BAND, computed without drawing RNG, reusing
  `QualityRoller`'s own threshold table.** `QualityRoller.Roll` (passive professions) computes
  `effective = rng.Roll100() + shift`, where `shift` is entirely computed from material grade, talents,
  and (optionally) a captured `performanceGrade` — i.e. everything except the roll is already
  deterministic and known BEFORE crafting. The forecast is: compute `shift` exactly as `Roll` does,
  then map the two ends of the roll's range (`Roll100()` ∈ [0, 100)) through the SAME threshold table
  (`<=14 Poor, 15-64 Common, 65-89 Fine, 90-98 Superior, >=99 Masterwork`) to get `BandFor(shift + 0)`
  through `BandFor(shift + 99)` — the forecast is the closed interval of grades that range covers (e.g.
  shift=0 → "Poor–Fine (Superior rare, 9%; Masterwork ~1%)"; the synthesis's exact "auto-craft iron →
  Common–Fine (Superior rare)" phrasing is this band read off the real numbers). For the active-model
  profession (blacksmith), do the same over `RollActive`'s jitter range (`clamp(performanceGrade ??
  550, 0, 1000) ± 25`, mapped through `BandFor`/`MaterialCeiling`/the auto-craft Superior cap) — this is
  where "forge it (Great) → Fine–Superior" comes from: a `performanceGrade` estimate (e.g. the
  minigame's historical average, or simply present both the AUTO-CRAFT band at `performanceGrade =
  null` and a BEST-CASE band at `performanceGrade = 1000` side by side, since the player hasn't played
  the minigame yet at forecast time — this is a genuine implementation choice, flagged for the
  implementer to resolve against what the Godot craft screen can actually show before the minigame
  starts). **Zero RNG draws** — this is a pure projection function, callable any number of times,
  living in `sim/GameSim/Crafting/` beside `QualityRoller` (a new `QualityForecast.cs` or a static
  method added to `QualityRoller` itself — prefer a new file to keep `QualityRoller`'s own doc-comment
  "the threshold table" contract undisturbed).
- **KTD-C2 — show slot-cost alongside the forecast**, per the synthesis's "the quality tax stays
  legible" requirement: the forecast surface should also report the material grade / talent-tier
  difference this recipe is asking for relative to what's already unlocked — reuses the exact fields
  `SuggestQualityUpgrade` (Wave 1, `sim/GameSim/Advisor/ObjectiveAdvisor.cs`) already computes when
  naming a locked tier gate. Do not duplicate that computation; factor a shared helper if both Wave 1
  and this wave's forecast need it (check whether Wave 1 has already landed by the time this unit
  starts — if so, extract `SuggestQualityUpgrade`'s tier-gate-lookup into a small shared static method
  both call).
- **KTD-C3 — this is a sim projection + Godot adapter pair, not sim-only.** The pure forecast function
  lives in `sim/GameSim/Crafting/`; a `godot/scripts/` adapter renders it on the craft screen (forge
  panel) as text/a colored band — per the master plan's KTD2 boundary (`godot/` renders state, submits
  actions, holds no game rules). This wave's Definition of Done requires BOTH halves; a sim-only
  forecast function with no Godot surface does not satisfy "the craft screen shows it."
  Non-goal within this wave: the forge minigame's own live in-progress feedback (already exists per the
  `HeatBandForge`/active-craft surface) — this forecast is the PRE-craft band shown before committing
  materials, not a replacement for minigame feedback during it.
- **KTD-C4 — depth-gate callout + advisor escalation copy, fire-once per the nag lesson.** When a hero
  is stalled on a QUALITY gate (`DepthStallEntry.BlockingSlot == null`, the exact shape
  `Drama/DemandBoard.cs`'s `DepthStalls` already detects and `ObjectiveAdvisor.SuggestQualityUpgrade`
  already answers, per Wave 1), the callout should fire ONCE per stall (not every Morning) — mirror
  Wave 1's nag-decay cadence pattern (KTD-A5 in the Wave-1 plan: `age % 3 == 0`-style gating) rather
  than inventing a second cadence mechanism. This is presentation copy layered on data Wave 1 already
  computes, not a new detector.
- **KTD-C5 — the 10 balance-sim invariants, three canonical policies, ≥20 seeds.** New file
  `sim/GameSim.Tests/Balance/BalanceInvariantsTests.cs`. Three test-local scripted policies (the
  `BaselinePlayer.cs` doc-comment's own precedent — "the A/B lives in a test-local scripted policy...
  never [in Harness]"):
  - **Skilled** — `BaselinePlayer.ActionsFor` AS-IS (it already IS the skilled policy the existing
    `BalanceSimTests` measures — reuse it directly, do not fork it).
  - **Naive** — a new, deliberately worse test-local policy: crafts the CHEAPEST recipe regardless of
    what's needed (no gear-gap targeting), never checks commissions, buys ore/materials in raw
    listing order rather than by need. Must never accidentally match `BaselinePlayer`'s behavior —
    assert the two policies diverge on at least one scripted day as a policy-sanity check.
  - **Greedy** — a new test-local policy: maximizes personal gold accumulation (prices high, crafts
    whatever sells regardless of hero need, never touches `BuyOre`-as-gift, never accepts a commission
    it can't obviously fulfill) — the "shopkeeper, not patron" archetype the "how you play" doc's
    merchant run already exemplifies in prose; codify it as a policy.
  Run each over the same ≥20-seed sweep (extend `BalanceSimTests.SeedSweep_CoreBands_Hold`'s existing
  10-seed `[InlineData]` list to ≥20, or add a fresh seed set for this test class — pick whichever
  keeps the two seed lists from silently diverging in intent; document the choice in the test file's
  own header comment). The 10 invariants (numbered per the synthesis, non-negotiables marked):
  1. Skilled reaches floor 5 by day 30 in ≥90% of seeds.
  2. **(non-negotiable) Naive NEVER reaches floor 5 by day 100**, any seed — protects the minigame's
     reason to exist; if this passes for Naive, quality RNG alone regressed toward too generous.
  3. Naive reaches floor 3 by day 20 in most seeds, but not before day-15 destitution in any (Naive
     should struggle, not softlock outright — the existing `DestitutionRecoverySystem` floor already
     structurally prevents a true softlock; this invariant is about PACE, not survival).
  4. Skilled day-15 gold falls in `[3.5×, 7×]` of starting gold (bracketing the measured 5.13×/5.78×
     range with headroom either side).
  5. Greedy accumulates MORE gold than Skilled by day 100 in most seeds (confirms "shopkeeper wins at
     gold, patron wins at depth" is a real, measurable trade-off, not just prose).
  6. Greedy reaches a SHALLOWER deepest floor than Skilled in most seeds (the other half of the same
     trade-off).
  7. **(non-negotiable) Zero advisor-surfaced commissions expire** under a policy that accepts
     everything the advisor surfaces — this is Wave 1's `AdvisorFollower` ≥95% invariant, generalized
     to exactly 0 for anything the advisor ACTIVELY suggested accepting post-Wave-1's fulfillability
     gate (Wave 1 ships its own ≥95% version pre-gate-fix; this wave's version should be measurably
     tighter now that the gate exists — if Wave 1 has landed, assert 100% here, not 95%; if this wave
     runs before Wave 1 merges, keep the looser bound and flag the TODO to tighten it once Wave 1
     lands).
  8. Once `DiversifiedDemand = true` (a SEPARATE, explicitly-labeled test class or `[Theory]` case run
     only once Wave 2 exists — see below): no single craft discipline exceeds 55% of total gold earned,
     and every craft discipline earns at least 10%, over a 100-day Skilled run.
  9. No hero-death rate exceeds a sanity ceiling under Naive (a defensive invariant against a future
     balance change accidentally making Naive a death machine rather than merely slow — pick a
     concrete ceiling by first RUNNING Naive and observing its actual death rate, then setting the
     ceiling with headroom, exactly like `BalanceSimTests`'s own existing band-setting methodology).
  10. **(non-negotiable) No event class fires more than 20×/100-day run**, any policy, any seed — the
      generalized, permanent version of the audited 1287× memorial-nag finding. Implement as a single
      parametrized check over `state.EventLog` grouped by event TYPE (not per-instance content) — this
      is the cheapest, most mechanical invariant to write and should be done FIRST as a smoke test
      before the other 9, since it would have caught the original bug outright.
- **KTD-C6 — invariant #8 is Wave-2-conditional, written now, activated later.** Write the test as a
  `[Fact(Skip = "requires Wave 2's DiversifiedDemand flag")]` or an `#if`-style conditional guard if
  `GameState.DiversifiedDemand` doesn't exist yet when this wave's PR lands — do NOT block this wave's
  merge on Wave 2 existing. Un-skip it in whichever wave merges second. Document the skip reason
  directly in the test so a future reader isn't confused about why it's inert.
- **KTD-C7 — destitution floor + pre-warning.** The existing `DestitutionRecoverySystem`
  (`sim/GameSim/Economy/DestitutionRecoverySystem.cs`) already guarantees "impossible to dead-end" via
  a direct gold stipend (`RecoveryStipendGranted`) once all three of its dead-end conditions hold. The
  synthesis's language ("one always-on low-pay income path... never loans/gifts") describes something
  narrower than what exists today — **reconcile explicitly, do not silently reinterpret:**
  - The EXISTING hard floor (`RecoveryStipendGranted`) stays exactly as-is — it is the true last-resort
    guarantee (R5/KD3) and this wave does not touch its three conditions or its target formula. This
    wave does NOT replace it with an "income path" mechanic; that would be retuning the safety net,
    out of scope.
  - What this wave DOES add: a **pre-warning event** fired 1-2 Mornings before the destitution floor
    would trigger (a cheap forward-check: re-run the SAME three-condition test one day early against
    projected end-of-day gold, WITHOUT mutating state — a read-only preview, not a second system) —
    surfaced to the player as a legible "you're about to hit the floor" signal, closing the synthesis's
    "2-3 painful recovery days" visibility gap (today the stipend just silently appears with an event
    the player may never notice in the log).
  - If, after implementing the pre-warning, the orchestrator judges the synthesis's "low-pay income
    path" language calls for something genuinely new (e.g. a always-available minor sell/chore action
    distinct from the hard floor), flag it as a follow-up decision rather than building it speculatively
    inside this wave — it would be new mechanic surface, and this wave's mandate is legibility, not new
    verbs.
- **KTD-C8 — Guidance-tier mapping (Wave 1 dependency).** Once Wave 1's `GuidanceLevel` enum exists,
  this wave's craft-screen forecast and depth-gate callout should be gated: `Full` shows the forecast
  band + slot-cost + the depth-gate callout; `Signals` shows only the depth-gate callout text (no band
  numbers); `Off` shows neither. If Wave 1 has not yet merged when this wave starts, build the forecast
  UNGATED (always visible) and add the `GuidanceLevel` filter as a small follow-up once Wave 1 lands —
  do not block this wave on Wave 1's merge order, since the synthesis marks Wave 1 "independent of
  everything else" and this wave only has a soft, additive dependency on it (advisor-honesty invariant
  #7 and the verbosity mapping), not a hard build-order one.

## Implementation Units

### U1. Quality forecast — sim projection

- **Goal:** A pure, deterministic function reporting the quality band a given recipe+material+method
  combination WOULD produce, without drawing RNG.
- **Files:** new `sim/GameSim/Crafting/QualityForecast.cs`.
- **Approach:** KTD-C1, KTD-C2.
- **Test scenarios:**
  - Passive profession, shift = 0 → forecast band spans exactly `Poor..Fine` with `Superior`/
    `Masterwork` flagged as the tail-probability cases (9%/1% per `QualityRoller`'s own documented base
    odds) — assert against the EXACT documented base-odds comment in `QualityRoller.cs`, not a
    re-derivation.
  - Higher material grade shifts the forecast band up by exactly one grade per +8 shift, matching
    `QualityRoller.Roll`'s own formula — a direct parity test against `QualityRoller`'s threshold table
    (call both and confirm the forecast's reported range brackets every quality `QualityRoller.Roll`
    actually produces over a full 0-99 roll sweep, for a fixed shift — this is the strongest possible
    correctness proof: an exhaustive sweep, not a sampled one).
  - Active-model (blacksmith) forecast at `performanceGrade = null` (auto-craft) hard-caps at
    `Superior`, matching `RollActive`'s own auto-craft cap.
  - Zero RNG draws: calling the forecast function does not advance `IDeterministicRng` (pass a
    spy/counting RNG in the test, or simply assert the function's signature takes no RNG parameter at
    all — the strongest possible zero-draw guarantee is a signature that structurally cannot draw).
- **Verification:** fast lane green.
- **Dependencies:** none.

### U2. Craft-screen Godot adapter

- **Goal:** The forecast renders on the actual craft screen a player sees.
- **Files:** `godot/scripts/` (whichever script backs the forge/craft panel — locate via
  `godot/scenes/` panel wiring), no new scene structure required if the panel already has a quality/
  stat display area to extend.
- **Approach:** KTD-C3. Adapter-only — calls `QualityForecast` (U1), renders the band + slot-cost text.
  No game rule in Godot code.
- **Test scenarios (gdUnit4Net):** the craft panel renders the forecast text for a scripted mid-game
  state without error; changing the selected material grade updates the rendered band.
- **Verification:** engine-lane CI green; manual smoke — open the forge panel, confirm the band renders
  and changes with material choice.
- **Dependencies:** U1.

### U3. Depth-gate callout + destitution pre-warning

- **Goal:** The quality-stall wall and an approaching destitution floor are both telegraphed once,
  legibly, without new nag spam.
- **Files:** `sim/GameSim/Economy/DestitutionRecoverySystem.cs` (read-only preview helper — a new
  method, e.g. `PreviewsDestitutionNextMorning(GameState)`, that runs the SAME three-condition check
  one day forward WITHOUT mutating state), a small new event or reuse of an existing telegraphing
  surface for the depth-gate callout (check whether Wave 1's advisor copy already covers this — if so,
  this unit may be presentation-only wiring rather than new sim logic).
- **Approach:** KTD-C4, KTD-C7.
- **Test scenarios:**
  - Pre-warning fires exactly 1-2 Mornings before `RecoveryStipendGranted` would (scripted scenario
    driving a player toward the floor); never fires on a solvent trace (regression: the existing
    `NoSoftlockTests` suite should remain green and unmodified in its assertions).
  - Depth-gate callout fires once per stall, not every Morning it persists (cadence test, mirrors
    Wave 1's memorial cadence table shape).
- **Verification:** fast lane green; `NoSoftlockTests` unchanged.
- **Dependencies:** none (soft dependency on Wave 1's `GuidanceLevel` per KTD-C8, non-blocking).

### U4. The 10 balance-sim invariants

- **Goal:** Skilled/Naive/Greedy over ≥20 seeds, all 10 invariants, non-negotiables #2/#7/#10 enforced.
- **Files:** new `sim/GameSim.Tests/Balance/BalanceInvariantsTests.cs`.
- **Approach:** KTD-C5, KTD-C6. Reuse `BalanceSimTests.Run`'s harness shape (the `RunStats` record, the
  `kernel.Tick` loop over `Days * 5` ticks) rather than re-deriving it — extract a shared runner if
  `BalanceSimTests.cs`'s `Run` method needs to accept a policy parameter instead of hardcoding
  `BaselinePlayer.ActionsFor` (a small, safe refactor: change `Run(ulong seed)` to
  `Run(ulong seed, Func<GameState, ImmutableList<PlayerAction>> policy)` defaulted to
  `BaselinePlayer.ActionsFor` so existing call sites need no changes).
- **Test scenarios:** exactly the 10 invariants listed in KTD-C5, plus the policy-divergence sanity
  check (Naive/Greedy must each differ from Skilled on at least one scripted day).
- **Verification:** `dotnet test sim/GameSim.Tests/GameSim.Tests.csproj --filter Category=Balance`
  green, including invariant #10 as a fast, mechanical first check.
- **Dependencies:** none (invariant #8 is written but skipped until Wave 2 exists, per KTD-C6).

## Verification Contract

| Gate | Command | Proves |
|---|---|---|
| Fast lane | `dotnet test sim/GameSim.Tests/GameSim.Tests.csproj --filter Category!=Balance` | U1-U3 unit tests; forecast correctness (exhaustive sweep); zero-RNG-draw guarantee |
| Balance gate | `dotnet test sim/GameSim.Tests/GameSim.Tests.csproj --filter Category=Balance` | U4's 10 invariants, ≥20 seeds; existing `BalanceSimTests` bands UNCHANGED (no retuning) |
| Engine lane | gdUnit4Net (craft panel forecast render) | U2's adapter fidelity |
| Manual smoke | Open forge panel, vary material | Forecast band visibly updates |

## Definition of Done

- U1-U4 merged; fast lane green; Balance gate green with all 10 invariants passing (or #8 explicitly
  skipped, narrated, pending Wave 2).
- Zero `BalanceSimTests` band constants changed in VALUE by this wave's PRs.
- The craft screen (Godot) visibly shows the deterministic quality forecast band and slot-cost before
  the player commits materials.
- The destitution pre-warning is observable in a scripted test at least 1 Morning before the existing
  hard floor fires.
- This wave's invariant harness is explicitly re-runnable (not single-use) against Wave 2's
  `DiversifiedDemand = true` state once that wave merges — confirmed by KTD-C6's skip-and-un-skip plan
  being followed through, not abandoned.
