---
title: "Wave 4 — Demand-Gated Profession Restructure"
date: 2026-07-27
type: feat
artifact_contract: ce-unified-plan/v1
artifact_readiness: implementation-ready
execution: code
source: docs/design/2026-07-27-five-pillars-design-synthesis.md (Pillar 5, Unified build sequence — Wave 4)
predecessors: docs/design/2026-07-27-how-you-play.md, docs/design/2026-07-27-gameplay-loop-analysis.md
---

# Wave 4 — Demand-Gated Profession Restructure

## Goal Capsule

- **Objective:** Ship the re-axised version of the owner's profession-tree idea: a profession unlocks
  *the moment the world first asks for it, and only then* — not a grind-gated tree that would unlock
  professions "into a demand vacuum." Blacksmith-mandatory single start (the only profession with both
  a real craft-feel — the forge minigame — and day-1 demand); Floor-3 Venom hazard triggers the
  Alchemist-vs-Tanner branch fork (~day 8-11, landing in the measured day-9-11 sag as a timed novelty
  injection, not dead inventory); Floor-5 armored burrowers unlock Engineer EXCLUSIVELY (the floor-5
  stall gate becomes unsatisfiable by any Blacksmith item, denting the measured 75% weapon-commission
  skew by ~25% at floor-5+); a talent layer (Runeforge/Spellsealing) lives INSIDE each profession via
  the existing `UnlockTalentAction`, not as new fifth/sixth professions.
- **DEPENDS ON Wave 2's hazards.** This wave cannot exist without `HazardDefinition`/
  `VenueFloor.HazardTags`/the hazard-counter mechanism (`sim/GameSim/Venues/HazardDefinition.cs`,
  `sim/GameSim/Expedition/ExpeditionResolver.cs`'s gate-penalty check) already shipped and flipped to
  `GameState.DiversifiedDemand = true`. Do not start this wave's unlock-trigger unit before Wave 2 has
  merged and its flag is live on the campaigns this wave's tests run against.
- **Stop conditions:** any reachable state where a profession is unlocked and customer-less (this is an
  explicit, tested invariant — see U-D "unlock-requires-demand"); any migration PR that lands sim rule
  changes and new goldens/`BaselinePlayer` updates in SEPARATE PRs rather than one serial re-baseline
  (Cross-pillar risk 5).

## Scope Boundaries (what this wave does NOT do)

- Does **not** build Enchanter/Healer/Spellcrafter as standalone professions — the synthesis's verdict
  is explicit: cut that breadth for now, mitigated by `ProfessionRegistry` staying data-driven so a
  later floor-7 hazard (curses→Healer, magic-null→Spellcrafter) can slot in with zero rework. Park as
  Phase 2, contingent on this 4-profession version proving the demand-pull loop. Do not speculatively
  scaffold those professions' data now.
- Does **not** implement Wave 5's `Bond`/hero-attribution/rivalry, the ending, or Master Voss — those
  consume this wave's typed unlock moments and hazard-typed commissions, but are not part of it.
- Does **not** retune Wave 2's or Wave 3's numeric constants — this wave's own new constants (the
  unlock-trigger thresholds, the day-14 fallback, the pacing targets) are its own first-draft tuning
  surface, explicitly flagged provisional per unit below, not a re-fit of earlier waves' bands.
- **Alt-start (Alchemist-start variant) is an explicit fast-follow, not this wave's Definition of
  Done** — build the `AltStartUnlocked` flag's DATA shape (so it isn't a later contracts scramble) but
  the actual alternate hazard tables and branch content ship in a follow-up unit, per the synthesis's
  own "ship same phase or first fast-follow" framing. Call this out explicitly in the PR so it isn't
  mistaken for scope-cut silently.
- Does **not** delete `SetProfessionsAction`, `Commission`, or the flat-model code paths Wave 2 left
  intact behind its flag — this wave RESTRICTS `SetProfessionsAction`'s legality and folds
  `Commission`/`DemandLine` together (see Migration), it does not remove the old shapes outright
  (old saves/goldens must still deserialize and replay under `LegacyFlatProfessions`).

## Cross-pillar risks that apply here

- **Risk 2 (Blacksmith-mandatory reads as railroading):** flagged explicitly by the synthesis as
  needing the alt-start fast-follow to not be optional long-term — this wave ships the FLAG and DATA
  shape for it (Scope Boundaries above) so the fast-follow isn't a contracts scramble later, even
  though the content itself is deferred.
- **Risk 4 (day-14 force-activation fallback needs playtest tuning):** this wave's own U-C unit builds
  the fallback mechanism; the EXACT day-14 threshold is explicitly a playtest knob, not a locked
  constant — ship it adjustable and call out in the PR that it is provisional pending real playtest data.
- **Risk 5 (serial re-baseline discipline, the biggest execution risk in this wave):** land sim rules +
  new goldens + `BaselinePlayer` migration in ONE PR, legacy goldens pinned under
  `LegacyFlatProfessions`. This is not a suggestion — it is this wave's single most failure-prone
  step, because it touches the same golden-trace surface every other wave in this program also touches
  (any Wave-2/3 PR merged in the interim needs its own re-pin reconciled here too). Assign this
  migration unit to whichever agent/session has full visibility into all four waves' current merge
  state, not a fan-out worker.

## Key Technical Decisions

- **KTD-D1 — `ProfessionUnlockOffered` state, contracts micro-PR.** New in `Contracts/World.cs` (or a
  dedicated `Contracts/Professions.cs`):
  ```csharp
  public sealed record ProfessionUnlockOffer(
      ImmutableSortedSet<string> Branch,        // e.g. {"alchemist", "tanner"} — the 2-way fork
      HazardAxis TriggeringHazard,
      int OfferedOnDay,
      int ExpiresOnDay);                          // re-offer window (KTD-D2)
  ```
  `GameState.PendingUnlockOffers` — trailing init member (`ImmutableList<ProfessionUnlockOffer> { get;
  init; } = ImmutableList<ProfessionUnlockOffer>.Empty;`), the `Counter`/`Rent`/`Commissions`/
  `Director`/`Assessment`/`Arc`/(Wave 2's `DiversifiedDemand`/`DemandLines`) save-compat precedent —
  every one of those seven-plus existing fields follows exactly this shape, so this is a mechanical,
  low-risk addition once the pattern is recognized. This record and `UnlockProfessionAction` (below)
  land together as ONE dedicated orchestrator-authored micro-PR, merged before this wave's dependent units.
- **KTD-D2 — the unlock-trigger, deterministic, end-of-day.** New system (candidate: a new Evening
  `IPhaseSystem`, e.g. `sim/GameSim/Professions/ProfessionUnlockSystem.cs`, registered analogous to
  `ArcDirectorSystem`'s "AFTER expedition-reveal" placement in `GameComposition.cs`, since it needs the
  day's `HazardCountered`/stall events already resolved). Trigger condition (both must hold,
  mirroring the synthesis's exact "OR floor, not a wall" framing for the readiness half):
  1. **≥2 distinct heroes** have hit the new hazard's stall gate this campaign (count via the new
     `HazardCountered`-adjacent "hazard blocked entry" event Wave 2's KTD-B6/B9 introduces — if Wave 2
     did not stamp a distinct "blocked, not countered" event alongside `HazardCountered`, this wave
     needs a small Wave-2-contract-compatible addition here: `HazardBlocked(HeroId, HazardAxis, int
     Floor)` fired exactly when the gate-penalty check in `ExpeditionResolver.cs` finds the party
     uncountered — check Wave 2's actual shipped shape before assuming it exists; if missing, this is
     a SECOND small contracts touch this wave must make, flagged here so it isn't discovered mid-unit).
  2. **A readiness milestone** — 5 Fine+ items lifetime (count `Item.Quality >= Fine && Item.PlayerCrafted`
     across `state.Items.Values`) **OR** any hero has `DeepestFloorReached >= 3` in player-crafted gear
     (an OR floor, not a wall — either signal alone is sufficient).
  Fires `ProfessionUnlockOffered(Branch, TriggeringHazard, Day, Day + ReofferWindowDays)` and pushes the
  offer onto `GameState.PendingUnlockOffers`. **Zero RNG draws** — pure integer/event-count check, same
  discipline as every other Evening system in `GameComposition.cs`'s existing chain.
- **KTD-D3 — `UnlockProfessionAction` + legality.** New action in `Contracts/Actions.cs` (same micro-PR
  as KTD-D1): `public sealed record UnlockProfessionAction(string Profession) : PlayerAction;`.
  Legality (`ActionLegality.cs`, new `UnlockProfessionLegal`): the named profession must appear in the
  `Branch` set of a LIVE (not expired) entry in `state.PendingUnlockOffers`, and must not already be in
  `state.Player.SelectedProfessions`. Handler (`Professions/ProfessionUnlockHandlers.cs`, new): adds the
  profession to `SelectedProfessions` (reuses `PlayerState`'s existing `SelectedProfessions` set — no
  new storage needed, since Wave 4's unlocked professions ARE just additional selected professions,
  mechanically identical to what `SetProfessionsAction` already does today) and removes the offer
  (both branches of a 2-way fork — the un-chosen one is NOT deleted outright; see KTD-D4) from
  `PendingUnlockOffers`.
- **KTD-D4 — the un-chosen branch re-offers, not lost.** When one branch of a `ProfessionUnlockOffer`
  is taken, the OTHER branch (e.g. player picks Alchemist over Tanner) re-posts as a NEW, single-profession
  offer `+4 days` at "half urgency" (a presentation-only distinction — e.g. the hero-asks copy softens
  from "I need X now" to "I could still use Y eventually" — no new mechanical field required beyond a
  second `ProfessionUnlockOffer` with a later `OfferedOnDay`; do not build a separate "urgency" enum
  unless the Godot copy genuinely needs to branch on it, in which case add a trailing-init
  `bool IsSecondOffer` field to `ProfessionUnlockOffer` rather than a new concept).
- **KTD-D5 — `MaxSelected` must rise from 2.** `ProfessionHandlers.MaxSelected = 2` (P1's original "pick
  1-2" scope) hard-caps `SetProfessionsAction`/now also `UnlockProfessionAction`'s legality at 2
  professions total — but this wave's pacing math reaches Blacksmith + (Alchemist OR Tanner) + Engineer
  = **3** professions by day ~20-24. Raise `MaxSelected` to at least 3 (4 if a full run could
  eventually unlock every current profession — decide based on whether the synthesis's Phase-2-parked
  Enchanter/Healer/Spellcrafter professions are meant to stack on TOP of the existing 4 later, in which
  case leave room; if the 4 current professions are the full roster for the foreseeable future, 4 is
  the safe permanent cap). This is a **contract-adjacent constant change**, not a full contracts touch
  (the constant lives in `Professions/ProfessionHandlers.cs`, not `Contracts/`), but flag it in the PR
  since `ActionLegality.SetProfessionsLegal`/the new `UnlockProfessionLegal` both read it and old tests
  asserting "cannot select more than 2" need a deliberate, narrated update, not a silent break.
- **KTD-D6 — profession/hazard mapping.**
  - Floor 3 Venom → **Alchemist (antidote, consumable/repeat)** *or* **Tanner (hide, durable)** — the
    starved pair gets the FIRST new hazard (teaches both revenue shapes at once, per Wave 2's
    `HazardDefinition.CounterTag` data already tagging both professions' relevant recipes per KTD-B8 in
    the Wave-2 plan).
  - Floor 5 armored burrowers → **Engineer, exclusively.** Concretely: the floor's `HazardAxis`
    (`Structural`, per Wave 2's KTD-B5 floor-5 assignment) has its `CounterTag` satisfied ONLY by
    Engineer recipes (e.g. `PiercingBolt`) — enforce this by simply NOT tagging any Blacksmith/Tanner/
    Alchemy recipe with that `CounterTag` in Wave 2's data (a data-only exclusivity, no special-case
    code needed in the gate-penalty check itself, since KTD-B6's mechanism already treats "any party
    member carries a countering item" generically). Confirm this via a NAMED test (below) rather than
    trusting the data never drifts.
- **KTD-D7 — the unlock moment is not a menu, a hero asks.** Godot-side (out of this sim-focused plan's
  primary file list, but required for Definition of Done per the master plan's "a feature isn't shipped
  until it's in the Godot 3D client" rule): the first hero who hits the new stall (the SAME event
  `ProfessionUnlockSystem` counts toward its trigger, KTD-D2) gets a dialogue beat naming the hazard and
  the two candidate crafts in-fiction ("Venom-beasts on floor 3. Steel doesn't help..."), followed by
  the branch screen (`Study Alchemy` vs `Study Tanning`) that submits `UnlockProfessionAction`. The sim
  side only needs to expose `state.PendingUnlockOffers` for the adapter to read — no new sim-side
  dialogue/copy generation (that's presentation, same boundary as every other Godot-adapter unit in this
  program).
- **KTD-D8 — talent layer, inside professions, via EXISTING `UnlockTalentAction`.** No new action type.
  Extend `ProfessionDefinition.TalentNodes`/`TierGate` data for Blacksmith with a 3-branch structure:
  Anvil (quality — already effectively what the existing talent tree does), Furnace (tier, unlocks
  `MasterworkAttemptAction` — check whether `MasterworkAttemptAction`'s current legality already gates
  on a talent, per `Contracts/Actions.cs`'s doc comment "gated on the workshop's forge tier" — if it
  gates on `ForgeTierHandlers` state rather than a talent node, this wave's Furnace branch needs to
  either ADD a talent gate on top or the doc's language needs reconciling before implementation — flag
  to orchestrator), Runeforge ("Enchanter" — affixes on weapons that already have buyers, a soft
  cross-craft bridge to the venom gate per the synthesis's framing, gating `CommissionLegendaryWorkAction`).
  Alchemist mirrors with Spellsealing / Healer-as-field-tonics branch talent nodes. This is pure DATA
  work in `Professions/*/*.cs` (the `TanningProfession.cs`/`EngineeringProfession.cs`/
  `AlchemyProfession.cs` sibling files already establish the per-profession-file pattern) — no new
  handler, no new contract.
- **KTD-D9 — Migration, serial re-baseline in ONE PR.**
  - **Restrict `SetProfessionsAction` legality** (do not delete the action or its handler): today
    `ActionLegality.SetProfessionsLegal` allows it in EVERY phase with no day/count-beyond-initial
    restriction (`sim/GameSim/Advisor/ActionLegality.cs:509-517` — confirmed by direct read, this is not
    a hypothetical gap). Change `SetProfessionsLegal` to also require `state.Day == 1` (or a
    `LegacyFlatProfessions`-flagged campaign, see below) — post-day-1 profession changes must go through
    `UnlockProfessionAction`'s demand-gated path from here on. This is the concrete code change the
    synthesis's "keep the action, restrict legality to day-0 (+ alt-start)" line refers to.
  - **`LegacyFlatProfessions` ruleset flag** — another `GameState` trailing-init bool (the by-now
    seven-times-precedented pattern), defaulting per how the campaign was created:
    `true` for any save created before this wave (so existing goldens keep replaying under the OLD
    unrestricted `SetProfessionsLegal` rule — a save-compat escape hatch, not a player-facing toggle),
    `false` for any NEW campaign created after this wave ships. Concretely: `GameComposition.NewCampaign`
    stamps `false`; `SaveCodec`/deserialization of a save with no `LegacyFlatProfessions` property in
    its JSON deserializes to... **decide deliberately**: the field's C# default for an ADDITIVE
    trailing-init bool would normally be `false` (matching every other precedent, "absence = the
    feature's fresh-start baseline"), but here the feature being toggled is "does day-1-only
    restriction apply," and the SAFE default for an EXISTING save must be `true` (keep the OLD, more
    permissive rule) to avoid silently breaking an in-progress old save's already-legal action stream.
    This is the one place in this wave's contract design that inverts the usual "absence = fresh
    baseline" precedent — document the inversion explicitly in the field's doc comment so a future
    reader isn't confused by the deviation.
  - **`BaselinePlayer` emits `UnlockProfessionAction`** — extend `sim/GameSim/Harness/BaselinePlayer.cs`
    (a real edit to a shared, non-test-local file — unlike Wave 1/3's test-local policies, this one
    genuinely belongs in `Harness/` since it's the ONE canonical "baseline good play" the Balance gate
    and CLI batch farm both share) to check `state.PendingUnlockOffers` each Morning and submit
    `UnlockProfessionAction` for its FIRST branch option (deterministic, alphabetical by profession id —
    matches the codebase's existing `StringComparer.Ordinal` convention for exactly this kind of "pick
    the first deterministically" tie-break) whenever one is live.
  - **New goldens** — every existing golden/idle-hash trace that runs past day 1 with the default
    (non-legacy) ruleset needs re-pinning once `ProfessionUnlockSystem` starts firing offers (Class 1,
    same discipline as Wave 2's KTD-B7 — one deliberate re-pin, narrated, in this SAME PR).
  - **Land all of the above in ONE PR** — sim rule changes, the `LegacyFlatProfessions` flag, the new
    goldens, and the `BaselinePlayer` update together. Splitting them risks a window where goldens are
    broken against half-migrated rules (Cross-pillar risk 5).
- **KTD-D10 — day-14 hard fallback.** If no `ProfessionUnlockOffer` has fired by day 14 (a player who
  stalls pre-floor-3, per the synthesis's own residual-risk note), `ProfessionUnlockSystem` force-fires
  the Venom-branch offer regardless of the normal trigger conditions. Implement as a simple additional
  OR-branch in the trigger check (KTD-D2): `state.Day >= 14 && no offer yet fired this campaign` bypasses
  both the ≥2-stall and readiness-milestone conditions. Flag the `14` constant as explicitly provisional
  pending real playtest data (Cross-pillar risk 4) — do not treat it as tuned.
- **KTD-D11 — pacing math is measurement, not a lever this wave pulls directly.** The synthesis's
  day-9-11 landing / day-13-15 mirror-offer / day-20-24 Engineer targets fall out of the EXISTING
  hazard-escalation curve Wave 2 already data-authored (floor 3 ~day 8-10, floor 4 ~..., floor 5 ~day
  12-13 per the existing `BalanceSimTests`'s own `NoFloor5BeforeDay = 8` comment history) plus this
  wave's trigger conditions — this wave does not need its own separate pacing constants beyond the
  day-14 fallback. Verify the landing via measurement (U-tests below), not by inventing a second
  schedule to hit a target artificially.

## Implementation Units

### U1. Contracts micro-PR — `ProfessionUnlockOffer`, `UnlockProfessionAction`, `LegacyFlatProfessions`

- **Goal:** Every new data/action shape this wave needs exists and compiles, inert until wired.
- **Files:** `Contracts/World.cs` (or `Contracts/Professions.cs`), `Contracts/Actions.cs`.
- **Approach:** KTD-D1, KTD-D3 (action shape only, not legality/handler yet), KTD-D9's
  `LegacyFlatProfessions` field.
- **Test scenarios:** compiles; every existing save/golden fixture deserializes with
  `LegacyFlatProfessions = true` and `PendingUnlockOffers` empty (explicit inversion test per KTD-D9);
  a NEW campaign (`GameComposition.NewCampaign`) stamps `LegacyFlatProfessions = false`.
- **Verification:** fast lane green.
- **Dependencies:** Wave 2 merged (needs `HazardAxis`).

### U2. `ProfessionUnlockSystem` — the trigger

- **Goal:** Offers fire on the documented conditions, deterministically, including the day-14 fallback.
- **Files:** new `sim/GameSim/Professions/ProfessionUnlockSystem.cs`,
  `sim/GameSim/GameComposition.cs` (register, Evening block, after expedition-reveal).
- **Approach:** KTD-D2, KTD-D10. Confirm (and if missing, add via the SAME U1 micro-PR) whichever
  Wave-2-shipped event distinguishes "hazard blocked entry, uncountered" from "hazard countered."
- **Test scenarios:**
  - Scripted scenario: 2 heroes hit the Venom stall + a hero at floor 3 in player gear → offer fires
    exactly once (not duplicated on subsequent Mornings).
  - Fewer than 2 stalled heroes, readiness milestone unmet → no offer.
  - Day 14 reached with zero heroes stalled → the fallback fires the Venom-branch offer anyway.
  - Zero RNG draws (grep gate, same discipline as Wave 2's KTD-B7).
  - Determinism: identical scripted state fed twice → byte-identical offer.
- **Verification:** fast lane green; grep gate green.
- **Dependencies:** U1.

### U3. `UnlockProfessionAction` legality + handler + `MaxSelected` raise

- **Goal:** The player can accept an offer; the un-chosen branch re-offers; `MaxSelected` accommodates
  3+ professions.
- **Files:** `sim/GameSim/Advisor/ActionLegality.cs` (new `UnlockProfessionLegal`), new
  `sim/GameSim/Professions/ProfessionUnlockHandlers.cs`, `sim/GameSim/Professions/ProfessionHandlers.cs`
  (`MaxSelected` constant), `sim/GameSim/GameComposition.cs` (register the new handler).
- **Approach:** KTD-D3, KTD-D4, KTD-D5.
- **Test scenarios:**
  - `UnlockProfessionAction` for a profession NOT in any live offer → rejected with a typed reason.
  - Accepting one branch adds it to `SelectedProfessions`; the other branch re-offers `+4` days later
    (scripted scenario advancing days, asserting the re-offer's `OfferedOnDay`).
  - Selecting a 3rd profession (Blacksmith + Alchemist + Engineer) succeeds once `MaxSelected` is
    raised; existing "cannot select more than 2" test updated deliberately (not silently broken).
- **Verification:** fast lane green.
- **Dependencies:** U1, U2.

### U4. Profession/hazard mapping data + Engineer exclusivity

- **Goal:** Floor-3 Venom maps to Alchemist/Tanner; floor-5 Structural (armored burrowers) is
  satisfiable ONLY by Engineer recipes.
- **Files:** `sim/GameSim/Professions/*/*.cs` (recipe `HazardCounters` tagging, per Wave 2's KTD-B8
  shape), `sim/GameSim/Venues/VenueRegistry.cs` (confirm/adjust floor-5's `HazardTags`).
- **Approach:** KTD-D6.
- **Test scenarios:**
  - **Named exclusivity test:** iterate `ProfessionRegistry.AllRecipes.Values` for every
    Blacksmith/Tanner/Alchemy recipe and assert NONE carries the floor-5 `Structural` hazard's
    `CounterTag` — a permanent regression guard against future data drift accidentally un-exclusive-ing
    Engineer's niche.
  - A party without any Engineer item fails the floor-5 gate exactly as an uncountered-hazard party
    would (reuses Wave 2's U4 test shape).
- **Verification:** fast lane green.
- **Dependencies:** Wave 2 merged, U1.

### U5. Talent layer (Runeforge/Spellsealing branches)

- **Goal:** The owner's tree fantasy lives inside Blacksmith/Alchemist via existing `UnlockTalentAction`.
- **Files:** `sim/GameSim/Professions/ProfessionRegistry.cs` (Blacksmith's `TalentNodes`/`TierGate`
  extension), `sim/GameSim/Professions/Alchemy/AlchemyProfession.cs` (mirror branch).
- **Approach:** KTD-D8. Reconcile `MasterworkAttemptAction`'s actual current gate (forge-tier vs.
  talent) before assuming the Furnace branch needs a NEW gate — read `ForgeTierHandlers`/
  `MasterworkAttemptHandlers` first.
- **Test scenarios:** new talent nodes unlock in prerequisite order (existing `TalentTree`/
  `CanUnlock` test pattern); Runeforge node gates `CommissionLegendaryWorkAction` exactly as documented.
- **Verification:** fast lane green.
- **Dependencies:** none (can run parallel to U2-U4).

### U6. Migration — serial re-baseline (the highest-risk unit, ONE PR)

- **Goal:** `SetProfessionsAction` restricted to day 1 (or legacy flag); `BaselinePlayer` emits
  `UnlockProfessionAction`; every affected golden re-pinned; nothing left half-migrated.
- **Files:** `sim/GameSim/Advisor/ActionLegality.cs` (`SetProfessionsLegal` restriction),
  `sim/GameSim/Harness/BaselinePlayer.cs`, every golden-trace test file the idle-hash/re-pin touches
  (identify via running the full fast lane and reading which assertions fail first).
- **Approach:** KTD-D9. Do this LAST, after U1-U5 are individually proven, so the re-baseline measures
  the FINAL shape of this wave rather than an intermediate one that would need a second re-pin.
- **Test scenarios:**
  - `SetProfessionsAction` submitted on day 2+ under a NON-legacy campaign → rejected.
  - Same action, same day, under `LegacyFlatProfessions = true` → still legal (old-save compatibility
    proven directly, not just asserted).
  - `BaselinePlayer` accepts the first live unlock offer deterministically (alphabetical tie-break) in
    a scripted multi-day trace.
  - Full 100-day `BalanceSimTests`/new invariant suite re-run and re-pinned exactly once, narrated.
- **Verification:** fast lane green; Balance gate green (re-fit, narrated); NO other PR in this wave's
  set left with a stale golden.
- **Dependencies:** U1-U5.

### U7. Godot: the hero-asks moment + branch screen

- **Goal:** The unlock moment is a hero walking in and asking, then a branch choice — never a bare menu.
- **Files:** `godot/scenes/`, `godot/scripts/` (whichever dialogue/panel surface already exists for
  hero-initiated beats — check the Wave-B/core-loop call-response surfaces first per
  `docs/plans/2026-07-25-001-feat-core-loop-call-response.md`'s precedent before building a new one).
- **Approach:** KTD-D7. Adapter-only, reads `state.PendingUnlockOffers`, submits
  `UnlockProfessionAction`.
- **Test scenarios (gdUnit4Net):** the branch screen renders both options for a scripted offer; picking
  one submits the correct action and the screen closes; **branch determinism** — the SAME offer state
  rendered twice produces the identical two-option layout (no hidden randomness in copy selection).
- **Verification:** engine-lane CI green; manual smoke — trigger an offer in a dev campaign, confirm the
  dialogue + branch screen appear and resolve correctly.
- **Dependencies:** U2, U3.

## Verification Contract

| Gate | Command | Proves |
|---|---|---|
| Fast lane | `dotnet test sim/GameSim.Tests/GameSim.Tests.csproj --filter Category!=Balance` | U1-U6 unit tests; unlock-requires-demand invariant; Engineer exclusivity; branch re-offer; legacy-flag compatibility |
| Balance gate | `dotnet test sim/GameSim.Tests/GameSim.Tests.csproj --filter Category=Balance` | Pacing measurement (unlock lands day 9-11 for ≥80% of seeds, per synthesis); re-fit bands narrated once (U6) |
| Engine lane | gdUnit4Net | U7's hero-asks + branch-screen adapter fidelity |
| Manual smoke | Fresh campaign, play to floor 3 | Hero-asks beat fires, branch screen resolves, second profession's recipes become craftable |

**New Balance test (measurement, not a hard non-negotiable — pacing is explicitly a playtest-tunable
target):** across ≥20 seeds, the first `ProfessionUnlockOffered` for the Venom branch lands on day 8-13
in ≥80% of seeds (synthesis's "day 8-13 for ≥80% seeds" pacing target) — report, don't hard-fail below
that bar on the FIRST measurement (this wave's own numbers are provisional per KTD-D11); do fail if it
regresses in a LATER PR once a first baseline is recorded.

## Definition of Done

- U1-U7 merged; fast lane green; Balance gate green (bands re-fit exactly once, narrated).
- **Unlock-requires-demand invariant, tested and named:** no reachable state exists where a profession
  is selected AND zero heroes have ever stalled on a hazard only it counters (walk the event log of
  every scripted/Balance-sweep trace and assert this structurally, not just by construction).
- Engineer's floor-5 exclusivity is a permanent regression test (U4), not just true by accident today.
- `SetProfessionsAction` restricted to day 1 under non-legacy campaigns; `LegacyFlatProfessions`
  compatibility directly tested.
- The talent layer's Runeforge/Spellsealing branches exist as data and gate the documented actions.
- Godot shows the hero-asks moment and the branch screen — this wave is not "done" on sim tests alone
  (master-plan rule: a feature isn't shipped until it's in the Godot 3D client).
- Alt-start (`AltStartUnlocked`) DATA shape exists; its content is explicitly logged as a fast-follow,
  not silently dropped.
