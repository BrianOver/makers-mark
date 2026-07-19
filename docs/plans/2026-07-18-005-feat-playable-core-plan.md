---
artifact_contract: ce-unified-plan/v1
artifact_readiness: implementation-ready
execution: code
product_contract_source: ce-brainstorm
origin: docs/plans/2026-07-18-004-feat-next-phase-scope-plan.md
title: Playable Core — Plan
date: 2026-07-18
---

# Playable Core — Plan

> **Plan 1 of 4** in the next-phase wave (see the origin doc's plan table). Turns a
> complete-but-unreachable sim into a game a player can sit down and play: a reachable
> material→craft→stock→sell loop for every profession, a player-gated day clock, a materials
> vendor + starting-profession on-ramp, a hard no-softlock floor, and the legibility/rejection
> fixes the live playtest demanded. Art (Plan 2) and the full UI rethink (Plan 3) sit downstream;
> this plan lands playability with *functional* styling only (KD4).

---

## Goal Capsule

**Objective.** Make the craft loop reachable and un-loseable, and kill the timing-trap class of
rejections, so a fresh campaign is playable from day 1 in the Godot client.

**Product authority.** `docs/plans/2026-07-18-004-feat-next-phase-scope-plan.md` (the origin doc).
This plan enriches, it does not redefine, that scope. A substantive product-scope change loops back
there first.

**Requirements owned.** R1, R2, R3, R4, R5, R6, R7 (playability) plus the cross-cutting R14
(determinism/purity guardrail) and R15 (regression coverage). The housekeeping fold-in (BOARD.md
conflict markers + stale tables) rides in as U1.

**Open blockers.** None. The two Plan-1 open questions (OQ1 no-softlock mechanism, OQ2 vendor key
scope) are resolved in Key Technical Decisions below.

---

## Summary

Six behavioural fixes plus one housekeeping chore, dependency-ordered:

1. **Reconcile `.claude/tasks/BOARD.md`** — remove committed git conflict markers, re-baseline the
   stale gate/claim tables against merged history (dev-only, no code).
2. **Hybrid day clock (R1)** — nothing advances without an explicit *Advance*; an auto-advance
   toggle fires the same advance on a timer. Realigns the modal flow so a modal opens on the
   correct side of its tick (Camp is already correct; the Ledger reveal is decoupled from the
   auto-clock). Root fix for the REJECTED-spam class. `godot/` only.
3. **Direct materials vendor (R2/R3)** — a new always-Morning-legal `BuyMaterialAction` +
   `MaterialVendorHandlers`, so any selected profession can buy its base materials at a standard
   price. Hero Evening ore offers stay as the exotic/cheaper upside. A **sim rule** — new handler,
   new action, gold-conservation-tracked.
4. **Starting-profession selection + starter stock (R4)** — a Godot new-game selection screen and a
   `GameComposition.NewCampaign` seeding overload that selects the chosen profession and seeds a
   little base stock. A **sim seeding change** plus a `godot/` screen.
5. **No-softlock floor (R5)** — a deterministic destitution-recovery system so the player can never
   dead-end, proven by an un-losability test. A **sim rule**.
6. **Rejection UX + legality-gated controls (R6)** — gate buttons on legality (disable illegal /
   unaffordable), replace the persistent raw-kernel red label with a transient player-phrased toast.
   `godot/` only.
7. **Ledger/Camp vertical-text fix (R7)** — the `ScrollContainer` horizontal-scroll collapse that
   renders autowrap labels one character wide. `godot/` layout only.

Then a regression unit wires the gdUnit UI loop (vendor→craft→stock→sell) and the label-width assert
(R15). Every sim-touching unit carries the R14 determinism/purity guardrail.

---

## Problem Frame

A live playtest of the complete sim surfaced four blockers, all in the presentation/on-ramp layer:

- **The craft loop is dead on day 1.** A fresh campaign seeds zero materials and the only material
  source is returning heroes' Evening offers (`OreMarketHandlers`, Evening-only, hero-offer-only), so
  the player cannot buy → cannot craft → cannot stock → cannot sell. Observed:
  `REJECTED: No handler accepts BuyOreAction during Morning | Not enough iron: need 2, have 0`.
- **Timing traps.** The real-time `PhaseClock` auto-advances; the Evening Ledger opens *after* the
  Evening tick has advanced the phase, so a `BuyOreAction` queued from it lands on the wrong phase
  and is correctly rejected. A *class* of bug, not one instance.
- **Rejection UX is raw.** `MainUi.RefreshStatus` dumps the kernel's typed reason into a persistent
  red `Rejections` label (`"REJECTED: " + string.Join(...)`), and illegal/unaffordable actions are
  submitted-then-rejected rather than prevented.
- **Vertical text.** Modal bodies wrap each autowrap `Label` to one character per line because their
  `ScrollContainer` leaves horizontal scroll enabled (four sites: `SimPanel.BuildScrollBody`,
  `LedgerModal`, `CampPanel`, `HeroesPanel`).

The sim is sound: `GameKernel.Tick` applies queued actions then runs the phase's systems in fixed
registration order; determinism is byte-exact (`DeterminismTests`, `SaveCodec`); gold is conserved
to a tracked invariant (`GoldConservationTests`). Every fix here must preserve those.

---

## Requirements

Traced to the origin doc's R-IDs. This plan **satisfies** R1–R7, R14, R15.

| R# | Requirement (abbrev.) | Owning unit(s) |
|----|-----------------------|----------------|
| R1 | Player-gated day advance + optional auto-advance toggle | U2 |
| R2 | Reachable material→craft→stock→sell loop for every selected profession | U3, U4 |
| R3 | Direct materials vendor, fixed always-legal Morning phase | U3 |
| R4 | New-game starting-profession choice + starter stock | U4 |
| R5 | Impossible to hard-lose or soft-lock (economy floor) | U5 |
| R6 | Illegal/unaffordable actions prevented at UI; transient player-phrased rejection | U6 |
| R7 | Ledger/Camp render normally (no one-char-per-line collapse) | U7 |
| R14 | Sim-purity + determinism invariants hold; presentation/dev-tooling never writes GameState/saves/chronicles | U3, U4, U5 (guardrail); all |
| R15 | Fast sim lane stays green; new Godot behaviour gains gdUnit coverage | U8 (+ per-unit sim tests) |

---

## Key Technical Decisions

- **KTD-A — Advance is presentation-only; no new sim "advance" action (KD1).** A day advance is
  `SimAdapter.AdvancePhase()` → `GameKernel.Tick` — there is no `PlayerAction` for advancing, and one
  is not needed. The gated model is entirely `godot/`: the *Advance* button calls
  `Adapter.AdvancePhase()`; the auto toggle fires the **same** call on the `PhaseClock` timer. The sim
  stays untouched by U2, so determinism/golden-replay cannot regress from the clock rework.

- **KTD-B — Direct vendor as a new sim handler; hero offers unchanged (KD2).** Generalise material
  acquisition beyond `OreMarketHandlers` (Evening-only, hero-offer-only) with a new
  `BuyMaterialAction` + `MaterialVendorHandlers` legal in **Morning only**. Hero Evening offers keep
  their exact current behaviour as the exotic/cheaper upside layer. The vendor is a **gold sink**
  (player gold leaves the modelled town total, the vendor's purse is unmodelled — the same modelling
  choice as rival sales and the camp runner fee), reconciled through a stamped `MaterialPurchased`
  event so `GoldConservationTests`' invariant extends cleanly.

- **KTD-C — OQ2 resolved: vendor sells the whole priced pool (all-keys).** The vendor sells every
  `MaterialRegistry.PricedPool` key (copper, iron, steel, mithril, adamant) at
  `MaterialRegistry.UnitPrice`, not just the selected profession's. **Justification:** all four
  professions draw tier-1 from copper and higher tiers from iron/steel (`RecipeTable`,
  `TanningProfession`, etc.), and a save may select two professions; all-keys lets any selected
  profession buy any tier's base material and enables the intended multi-profession dabbling. Buying a
  high grade does **not** bypass progression — the tier-gate talent check in `CraftingHandlers` still
  blocks crafting a tier-2/3 recipe without its unlock node. A tunable `VendorMarkupPermille`
  (default recommended so hero offers stay strictly cheaper — see U3) keeps the vendor the *floor*,
  never the *bargain*.

- **KTD-D — OQ1 resolved: no-softlock = always-affordable base recipe + a destitution stipend
  (KD3).** The guarantee (R5) is met by a **combination**: (1) the Morning vendor makes a base
  material always purchasable (copper 3g; a fresh save holds 100g and a tier-1 craft costs 2 copper),
  and (2) a deterministic Morning `DestitutionRecoverySystem` that fires **only** at a true dead-end
  (gold below the cheapest priced-pool unit price **and** no materials **and** no unsold player craft
  **and** empty shelf), topping gold up to a fixed `DestitutionFloorGold`. It draws **no RNG** and is
  a tracked gold **source** (`RecoveryStipendGranted` event), so — exactly like `FactionDriftSystem`
  — inserting it into the composition leaves every existing seed's RNG draw order and world
  byte-identical, and it never fires on a normal trace so golden-replay and the balance bands are
  unchanged. Vendor sell-back is **deferred** (nice-to-have, not needed for the guarantee).

- **KTD-E — Modal-before-tick alignment (KD1).** In the gated model a phase's player input is queued
  while the sim sits *at* that phase, then the *Advance* click runs the tick that applies it. The Camp
  slate already follows this (`MainUi.SyncCampModal` opens it when `state.Phase == Camp`); the Evening
  Ledger's *reveal* is a post-Evening-tick retelling (read-only) and must not be driven by the
  auto-clock. Ore-buying from the Ledger is gated by phase legality in the UI (R6/U6), so a buy can
  only be queued when it will land on an Evening tick — the timing trap cannot recur by construction.

- **KTD-F — Contract + composition edits land as orchestrator micro-PRs.** `sim/GameSim/Contracts/`
  and `sim/GameSim/GameComposition.cs` are on the multi-agent deny-list. The new `BuyMaterialAction`
  / `MaterialPurchased` / `RecoveryStipendGranted` contract additions and the handler/system
  registration land as dedicated micro-PRs authored by the orchestrating session and merged before the
  dependent module PRs, per CLAUDE.md. Each unit below marks which edits are deny-list micro-PRs.

---

## High-Level Technical Design

The one behavioural shape prose won't carry is the gated day loop and where each modal sits relative
to its tick. Today the `PhaseClock` auto-fires `AdvancePhase` and the Ledger opens *after* the Evening
tick; the target flow makes advance explicit and keeps every input modal on the pre-tick side.

```mermaid
sequenceDiagram
    participant P as Player
    participant UI as MainUi / Panels (godot)
    participant CK as PhaseClock (godot)
    participant AD as SimAdapter (godot)
    participant K as GameKernel (sim, pure)

    Note over CK: Gated is source of truth (R1)
    P->>UI: queue actions for current phase (Craft, BuyMaterial@Morning, Send@Camp...)
    UI->>AD: Adapter.Queue(action)   %% no tick yet
    alt manual advance
        P->>UI: press "Advance"
        UI->>AD: AdvancePhase()
    else auto-advance toggle ON
        CK-->>AD: timer elapsed → AdvancePhase()  %% same call path
    end
    AD->>K: Tick(state, queued batch)
    K-->>AD: NewState + Events + Rejected
    AD-->>UI: StateChanged(completedPhase, day)
    UI->>UI: RefreshAll(); toast any rejection (R6)
    opt completedPhase == Evening
        UI->>UI: reveal Ledger retelling (read-only); ore Buy enabled only at Evening (R6)
    end
    opt state.Phase == Camp && InFlight not empty
        UI->>UI: open Camp slate BEFORE the Camp tick (already correct)
    end
```

**Sim-vs-godot split at a glance:** U2, U6, U7 are `godot/` adapter/layout only. U3, U5 are `sim/`
rules (determinism + gold-conservation gated). U4 is `sim/` seeding + a `godot/` screen. U1 is
dev-only docs. U8 is `godot/` tests. No unit lets presentation or dev-tooling write into `GameState`,
saves, or chronicles (R14).

---

## Implementation Units

### U1. Housekeeping — reconcile BOARD.md conflict markers + stale tables

**Goal.** Remove the committed git conflict markers in `.claude/tasks/BOARD.md` and re-baseline the
stale gate/claim tables against merged history, so the coordination board is trustworthy again.

**Requirements.** None directly (origin "Housekeeping" fold-in); unblocks clean multi-agent work on
the rest of the plan.

**Dependencies.** None (do first).

**Files.**
- Modify: `.claude/tasks/BOARD.md`

**Approach.** Lines 25/29/33 carry raw `<<<<<<< HEAD` / `=======` / `>>>>>>> 2ac2033 ...` markers
straddling the G3/G4/G5 gate rows. Resolve by taking the **merged reality** from `git log`/merged PRs:
G3 (U2 five-phase kernel) merged (#43), G4 (U3 staging) merged (#51), G5 (U4 camp verbs) merged — and
the plan of record has advanced well past these (recent commits run through #77). Collapse the two
conflicting halves into one correct table, mark the resolved gates `MERGED (#..)` per history, and add
a one-line note that the gate/claim tables are historical (the current plan of record is
`docs/plans/2026-07-13-001` and this next-phase wave). Reconcile the Open-claims table the same way —
any claim whose PR is merged is `done`. Do **not** invent gate status you cannot confirm from history;
where uncertain, mark `historical — see git log`.

**Execution note.** Read-and-reconcile against `git log --oneline` and `gh pr list --state merged`
before editing; this is mechanical bookkeeping, not a design change. BOARD.md is not on the deny-list,
but it is orchestrator-owned by convention — land it as a small standalone commit.

**Patterns to follow.** Mirror the existing "Seam changes (dated)" log style already in the file for
any note you add.

**Test scenarios.** Test expectation: none — documentation/coordination file with no build or runtime
surface. Verify by `grep -n '<<<<<<<\|=======\|>>>>>>>' .claude/tasks/BOARD.md` returning nothing.

**Verification.** No conflict markers remain; the gate and claim tables read consistently against
merged history; the file parses as valid Markdown.

---

### U2. Hybrid day clock — gated advance + optional auto-advance toggle

**Goal.** Nothing advances a phase without an explicit player *Advance*; an optional auto-advance
toggle fires the identical advance on the `PhaseClock` timer. Realign the modal flow so the auto-clock
never advances past a phase whose input modal is still open.

**Requirements.** R1. (Root fix for the REJECTED-spam class; foundation for R6.)

**Dependencies.** U1 (sequence only).

**Sim-vs-godot.** `godot/` adapter only. No `sim/` change (KTD-A: advance is `Adapter.AdvancePhase()`,
not a `PlayerAction`). Determinism cannot regress from this unit.

**Files.**
- Modify: `godot/scripts/PhaseClock.cs` — add a gated/auto mode; gated is the default and source of truth.
- Modify: `godot/scripts/MainUi.cs` — add an *Advance* button and an *Auto* toggle to the status bar; route both through `Adapter.AdvancePhase()`; keep the Ledger reveal decoupled from the auto-clock.
- Modify/Add test: `godot/tests/PhaseClockTests.cs` — gated + toggle scenarios.

**Approach.** `PhaseClock` today auto-fires `AdvancePhase` whenever `Elapsed >= PhaseDuration` and is
`Playing` by default. Rework so the clock has an explicit **auto-advance** flag (default OFF): when
OFF, `Update(delta)` never calls `AdvancePhase` — the player advances via the new *Advance* button;
when ON, it accrues time and fires the same `_adapter.AdvancePhase()` at the phase duration (its
existing behaviour, now opt-in). Preserve the speed multiplier and the "at most one advance per call"
guard. In `MainUi.BuildUi`, add an *Advance* `Button` (calls `Adapter.AdvancePhase()`) and an *Auto*
toggle button (flips the clock's auto flag); the existing Play/Pause and Speed controls become
sub-controls of auto mode (pause/speed are meaningless while gated — hide or disable them when auto is
OFF, mirroring how `UpdateClockLabel` already reflects clock state). Keep the Camp modal path
untouched (`SyncCampModal` already opens the slate at `state.Phase == Camp`, pre-tick). The Evening
Ledger's timed Return-Ritual reveal (`LedgerDelayRemaining`) stays, but it is a **read-only**
retelling; ore-buying legality is handled in U6 so a buy can only queue at Evening.

**Execution note.** Prove the gated invariant directly: with auto OFF, a large `Update(delta)` must
leave `Day`/`Phase` unchanged; only an explicit `AdvancePhase()` moves the sim. This is the property
that dissolves the timing-trap class. Two verified seams to handle deliberately: (1) the Ledger
reveal countdown (`MainUi._Process` scales `LedgerDelayRemaining` by `Clock.SpeedMultiplier`,
MainUi.cs:105; Ledger open/close pauses/resumes the clock, 306–333) — in gated mode the reveal must
elapse on wall-clock (unscaled) or reveal immediately, never wait on the auto clock, or the Ledger
never reveals with auto OFF; (2) `PhaseClock.DurationOf` covers only Morning/Expedition/Evening and
falls back to `MorningSeconds` for Camp/ExpeditionDeep (PhaseClock.cs:40–46) — keep or change that
default deliberately for auto mode and state it in the PR.

**Patterns to follow.** `PhaseClockTests` (existing pure-C# clock tests, no Godot runtime);
`MainUi.UpdateClockLabel` / the existing Play/Pause/Speed button wiring for the new controls.

**Test scenarios.**
- Happy (gated): auto OFF, `Update(PhaseClock.MorningSeconds * 10)` → `Phase` still Morning, `Day` still 1; one `AdvancePhase()` → Expedition.
- Happy (auto): auto ON, `Update(MorningSeconds)` → advances one phase (existing behaviour preserved); auto ON at 2x, `Update(MorningSeconds/2)` → advances.
- Edge: toggling auto ON then OFF mid-phase leaves accrued time harmless — a subsequent gated period never advances.
- Integration (gdUnit, in U8): mount `MainUi`, press *Advance* → sim ticks exactly once; toggle *Auto* → `_Process` drives ticks.

**Verification.** With auto OFF no wall-clock delta advances the sim; the *Advance* button ticks
exactly one phase; the *Auto* toggle reproduces the old timed cadence. Engine tests green.

---

### U3. Direct materials vendor (sim rule) — BuyMaterialAction + MaterialVendorHandlers

**Goal.** Any selected profession can buy its base materials from a standard vendor every Morning,
making the craft loop reachable on day 1 without waiting on hero raids.

**Requirements.** R2, R3, R14.

**Dependencies.** U1.

**Sim-vs-godot.** **`sim/` game rule** (new handler + action + event; must preserve determinism and
gold conservation) plus a `godot/` buy surface. Contract + composition edits are deny-list micro-PRs
(KTD-F).

**Files.**
- Add (contract micro-PR): `sim/GameSim/Contracts/Actions.cs` — `BuyMaterialAction(string MaterialKey, int Quantity)` + its `[JsonDerivedType(..., "buyMaterial")]` line.
- Add (contract micro-PR): `sim/GameSim/Contracts/Events.cs` — `MaterialPurchased(string MaterialKey, int Quantity, int Cost)` stamped event.
- Create: `sim/GameSim/Economy/MaterialVendorHandlers.cs` — the Morning-only handler.
- Modify (composition micro-PR): `sim/GameSim/GameComposition.cs` — register `new MaterialVendorHandlers()` in the handler list.
- Create test: `sim/GameSim.Tests/Economy/MaterialVendorHandlersTests.cs`.
- Modify test: `sim/GameSim.Tests/Economy/GoldConservationTests.cs` — extend the invariant to reconcile vendor spend (`MaterialPurchased`) as a tracked sink.
- Modify: `godot/scripts/panels/ForgePanel.cs` (or a small vendor sub-panel within it) — a per-material Buy row that queues `BuyMaterialAction`.

**Approach.** Model `MaterialVendorHandlers` on `OreMarketHandlers` exactly (fixed check order for
stable rejection reasons; every rejection before any state change; **no RNG**). `CanHandle` returns
true for `BuyMaterialAction` when `phase == DayPhase.Morning`. Apply, in order: (1) quantity positive;
(2) `MaterialRegistry.IsPriced(key)` — the vendor sells only the priced pool (KTD-C), unknown/inert
keys rejected; (3) compute cost with the exact integer formula
`cost = ceilDiv(quantity * MaterialRegistry.UnitPrice(key) * (1000 + VendorMarkupPermille), 1000)`
(ceiling division so a single unit still carries the markup: 1 copper at +250‰ = ceil(3.75) = 4g),
with `VendorMarkupPermille = 250` so hero offers stay strictly cheaper — pin the formula and value
with a test and flag for balance confirmation; (4) player
can pay. On success: player gold down by cost, `Player.Materials[key] += quantity`, emit
`MaterialPurchased(key, quantity, cost)`. Gold leaves the modelled town total (vendor purse
unmodelled) — a **sink**, so extend `GoldConservationTests` to subtract `Σ MaterialPurchased.Cost`
alongside the existing rival-sale and tariff-delta terms. The `ForgePanel` gains a Buy control per
priced key (queues the action, feedback "applies next phase"); it renders `MaterialRegistry`
prices/keys — adapter-only, no rule logic.

**Execution note.** Golden-replay guard: the golden trace submits no `BuyMaterialAction`, the handler
draws no RNG, and adding a handler does not reorder any RNG-drawing **system**, so `DeterminismTests`
and the balance gate stay byte-identical. State this in the PR body. Note `GoldConservationTests`
composes its own focused `EconomyKernel` with a subset of systems/handlers
(GoldConservationTests.cs:37) — the vendor handler must be registered in that focused kernel too,
not only in `GameComposition`, or the extended invariant never exercises it.

**Patterns to follow.** `OreMarketHandlers` (check-order discipline, rejection-before-mutation, event
emission, gold accounting); `ShopHandlers` (all-phase-legal handler shape for comparison);
`GoldConservationTests` (how `TariffApplied` deltas are reconciled — extend the same way).

**Test scenarios.**
- Happy: Morning, 100g, buy 4 copper → cost = ceilDiv(4·3·1250, 1000) = 15 → gold −15, `Materials["copper"] == 4`, one `MaterialPurchased(copper,4,15)` event; buy 1 copper → cost 4 (markup survives rounding).
- Happy (multi-profession): a save with two selected professions buys iron and copper in one Morning batch; both land.
- Edge: buy exactly to zero gold succeeds; buy the last affordable unit succeeds.
- Error (phase): `BuyMaterialAction` during Expedition/Evening → rejected `No handler accepts BuyMaterialAction during {phase}` (the original playtest failure, now impossible in Morning).
- Error (unaffordable): buy 100 copper with 10g → rejected `Not enough gold: need N, have 10`, no state change.
- Error (inert/unknown key): buy `electrum` (registered, not priced) or `"nonsense"` → rejected, no state change.
- Error (non-positive qty): `Quantity <= 0` → rejected.
- Conservation: `GoldConservationTests` extended run — town total moves by exactly minus rival sales minus tariff deltas minus vendor spend, across the multi-day script including a vendor buy.
- Determinism: `DeterminismTests` (Category!=Balance) unchanged and green; balance gate green.

**Verification.** A fresh campaign can buy copper on day 1 Morning and craft; conservation invariant
holds with the vendor term; determinism + balance gates green.

---

### U4. Starting-profession selection + starter stock

**Goal.** New game lets the player pick a starting profession and seeds a small base-material stock
for it, so day 1 is immediately playable for the chosen profession.

**Requirements.** R4, R2 (with U3), R14.

**Dependencies.** U3 (vendor exists so any profession's loop is reachable; starter stock is the
head-start on top of it).

**Sim-vs-godot.** **`sim/` seeding change** (deterministic new-game state) plus a `godot/` selection
screen. `GameComposition` edit is a deny-list micro-PR (KTD-F).

**Files.**
- Modify (composition micro-PR): `sim/GameSim/GameComposition.cs` — add a `NewCampaign(ulong seed, string startingProfession)` overload that selects the chosen profession and seeds starter materials; keep the existing `NewCampaign(ulong seed)` as blacksmith-default for the CLI/tests/replays.
- Modify: `sim/GameSim/Kernel/GameFactory.cs` — a `NewGame` seeding path (or param) that sets `SelectedProfessions` and seeds `Player.Materials` deterministically; leave the default `NewGame(seed)` byte-identical.
- Modify (contract micro-PR): `sim/GameSim/Contracts/Player.cs` — optionally a `PlayerState.NewGame(gold, profession, starterMaterials)` factory sibling to the existing blacksmith-default `NewGame(gold)` (keep the old one unchanged for save/determinism compatibility). Deny-list file — orchestrator-authored per KTD-F.
- Create: `godot/scenes/new_game_select.tscn` + `godot/scripts/NewGameSelect.cs` — a functional profession-pick screen (four buttons from `ProfessionRegistry.All`) that constructs the campaign via the new overload and hands it to `MainUi.AdapterOverride`.
- Modify: `godot/scripts/SimAdapter.cs` — allow constructing from a chosen profession (reuse the existing `SimAdapter(GameState)` injection ctor; the select screen builds the state and injects it).
- Create test: `sim/GameSim.Tests/Kernel/NewCampaignSeedingTests.cs`.
- Create/Modify test: `godot/tests/NewGameSelectTests.cs` (gdUnit) — selection yields a campaign whose player has the chosen profession + starter stock.

**Approach.** Add a seeding overload that (a) sets `SelectedProfessions` to the single chosen
registered profession (validated via `ProfessionRegistry.IsRegistered`), and (b) seeds
`Player.Materials` with a small deterministic starter stock keyed to that profession's tier-1 base
material — every profession's tier-1 recipes use `copper` (see `RecipeTable`/`TanningProfession`), so a
fixed `StarterCopper` (e.g., 6, enough for ~3 tier-1 crafts) works uniformly and keeps seeding
trivial. Pure data, no RNG, integer-only (KTD2). Route reuse through `HeroRoster.InstallStartingRoster`
exactly as `NewCampaign(seed)` does today, so the starting cast and id counters are unchanged. The
Godot select screen is functional-only (KD4): four labelled buttons; on click it builds the state via
the overload, sets `MainUi.AdapterOverride`, and enters the main scene. The existing single-arg
`NewCampaign`/`NewGame`/`PlayerState.NewGame` paths stay byte-identical so the CLI, replays, and every
existing test are untouched.

**Execution note.** Determinism guard: the new overload only *adds* a seeding path; the default paths
that `DeterminismTests`, the balance gate, and golden replay exercise must serialize byte-identically
to today. Assert that in `NewCampaignSeedingTests` (default overload == pre-change bytes for a fixed
seed).

**Patterns to follow.** `GameComposition.NewCampaign` + `HeroRoster.InstallStartingRoster` (the
existing seed→roster pipeline); `PlayerState.NewGame` (the blacksmith-default factory, kept as the
compatibility baseline); `SimAdapter(GameState)` injection ctor + `MainUi.AdapterOverride` (the
existing scenario-injection seam used by engine tests).

**Test scenarios.**
- Happy: `NewCampaign(seed, "tanning")` → `Player.SelectedProfessions == {tanning}`, `Materials["copper"] == StarterCopper`, roster + id counters identical to `NewCampaign(seed)`.
- Happy (each profession): blacksmith/tanning/alchemy/engineering each seed correctly and their tier-1 recipe is immediately craftable from starter stock.
- Edge: choosing blacksmith via the overload produces the same selection as the default (no double-seed drift).
- Error: an unregistered profession key → the overload rejects/throws (screen never passes one; guarded anyway).
- Determinism: default `NewCampaign(seed)` serializes byte-identical to pre-change (regression pin).
- Integration (gdUnit): select "tanning" on the screen → mounted `MainUi` shows tanning recipes in the Forge and copper in materials; can craft turn 1.

**Verification.** New game offers four professions; picking one yields a campaign that can craft on
day 1; default seeding is byte-identical; determinism + balance gates green.

---

### U5. No-softlock economy floor — destitution recovery (sim rule) + un-losability proof

**Goal.** Guarantee the player can never dead-end: from any state there is always an affordable path
back to a productive loop.

**Requirements.** R5, R14.

**Dependencies.** U3 (vendor is the affordable base-material path), U4 (starter stock is the day-1
head start). Resolves OQ1.

**Sim-vs-godot.** **`sim/` game rule** (new deterministic Morning system + event). Composition +
contract edits are deny-list micro-PRs (KTD-F).

**Files.**
- Add (contract micro-PR): `sim/GameSim/Contracts/Events.cs` — `RecoveryStipendGranted(int Amount)` stamped event.
- Create: `sim/GameSim/Economy/DestitutionRecoverySystem.cs` — Morning `IPhaseSystem`, draws no RNG.
- Modify (composition micro-PR): `sim/GameSim/GameComposition.cs` — register the system in the Morning group (order-neutral because it draws no RNG — see execution note).
- Modify test: `sim/GameSim.Tests/Economy/GoldConservationTests.cs` — reconcile the stipend as a tracked source.
- Create test: `sim/GameSim.Tests/Economy/NoSoftlockTests.cs` — the un-losability proof.

**Approach.** `DestitutionRecoverySystem.Process` detects the true dead-end and only then acts:
`Player.Gold < min(MaterialRegistry priced-pool unit prices)` **AND** `Player.Materials.IsEmpty`
**AND** no unsold player-crafted `Items` (nothing to stock/sell) **AND** `Player.Shelf.IsEmpty` (no
pending sale income). When all hold, top gold up to a fixed `DestitutionFloorGold` (enough to buy a
few copper and craft — e.g., 10g) and emit `RecoveryStipendGranted(delta)`. Otherwise return state
unchanged. Pure integer, no RNG, no wall clock, no transcendental math (KTD2). The stipend is a gold
**source**, so extend `GoldConservationTests` to add `Σ RecoveryStipendGranted.Amount` on the source
side of the invariant (mirroring how discount tariff deltas mint gold).

The proof (`NoSoftlockTests`): construct a destitute `GameState` (0 gold, empty materials/items/shelf,
roster present), run one Morning tick through the composed kernel, assert the stipend fired and the
player can now afford **and** craft the cheapest tier-1 recipe of their selected profession — i.e., a
legal productive action provably exists. Add a small sweep over low-gold / low-material states
asserting that after one Morning tick at least one legal productive action is always available (buy
cheapest priced material, or craft from stock, or stock an existing craft).

**Execution note.** Golden-replay + band guard: like `FactionDriftSystem`, this system **draws no
RNG**, so its insertion does not shift the kernel stream — every existing seed's world and the balance
bands stay byte-identical, and it never fires on a solvent trace. Verify `DeterminismTests`
(Category!=Balance) and the balance gate are unchanged. State this in the PR body. Two composition
cautions: (1) `FactionDriftSystem` documents a contract that it runs **FIRST** in the Morning group
(FactionDriftSystem.cs:13–15, GameComposition.cs:31) — insert `DestitutionRecoverySystem` after it,
never before, and state the chosen position in the micro-PR; (2) `GoldConservationTests`' focused
`EconomyKernel` (GoldConservationTests.cs:37) must also register this system or the stipend source
term is never exercised by the invariant.

**Patterns to follow.** `FactionDriftSystem` (a Morning system that mutates player state, draws no
RNG, is order-neutral for determinism — the exact precedent this relies on); `OreMarketHandlers` /
`GoldConservationTests` (source/sink reconciliation via a stamped event).

**Test scenarios.**
- Happy (fires): destitute state → one Morning tick → gold == `DestitutionFloorGold`, one `RecoveryStipendGranted`, and a cheapest-recipe craft is now legal.
- Happy (does not fire): solvent state (any gold ≥ cheapest price, or any material, or any craft/shelf entry) → no stipend, state byte-identical.
- Edge (boundary): gold exactly at the cheapest priced-pool unit price and no materials → does **not** fire (player can already buy one unit); gold one below → fires.
- Edge (has unsold craft but 0 gold): does **not** fire (player can stock+sell) — proves the floor is a last resort, not a handout.
- Un-losability sweep: over a range of near-zero states, after one Morning tick a legal productive action always exists.
- Conservation: extended `GoldConservationTests` — town total reconciles with the stipend source term.
- Determinism: `DeterminismTests` unchanged; balance gate green (stipend never fires on the trace).

**Verification.** No constructible state leaves the player unable to reach a productive action;
conservation + determinism + balance gates green.

---

### U6. Rejection UX + legality-gated controls

**Goal.** Prevent illegal/unaffordable actions at the UI, and replace the persistent raw-kernel red
label with a transient, player-phrased message.

**Requirements.** R6.

**Dependencies.** U2 (gated flow — the modal/tick timing the gating relies on), U3 (vendor buttons to
gate), and it touches every panel that queues actions.

**Sim-vs-godot.** `godot/` adapter/presentation only. No sim change; legality is still the kernel's
call — the UI only *mirrors* it to disable doomed controls (the same "mirror, never replace" discipline
`ShopPanel`/`CampPanel` already document).

**Files.**
- Modify: `godot/scripts/MainUi.cs` — replace the persistent `_rejections` red `Label` with a transient toast (auto-clearing label/timer); stop dumping `Adapter.LastRejections` verbatim.
- Modify: `godot/scripts/panels/SimPanel.cs` — a shared helper to disable a button when its action would be illegal/unaffordable, and a shared toast helper.
- Modify: `godot/scripts/panels/ForgePanel.cs` — disable Craft when materials insufficient; disable vendor Buy when unaffordable.
- Modify: `godot/scripts/panels/LedgerModal.cs` — enable ore Buy only when `state.Phase == Evening` and affordable (kills the timing trap at the surface); phrase the queued/blocked message for a player.
- Modify: `godot/scripts/panels/ShopPanel.cs`, `CampPanel.cs` — gate Stock/Reprice/Unstock and Send/Recall on legality already known to the panel; render any surfaced rejection as player-phrased text.

**Approach.** Two moves. (1) **Prevention:** each panel already computes the facts a legality check
needs (materials on hand, gold, phase, shelf membership). Disable the button when the action is
provably illegal or unaffordable, reusing the sim's own predicates where exposed
(`ProfessionDefinition.CanUnlock` is already used this way in `ForgePanel`) — mirror, never
re-implement, the rule. The Ledger's ore Buy becomes enabled only at Evening + affordable, which is
the surface-level half of the timing-trap fix. (2) **Transient, player-phrased rejection:** replace
`MainUi`'s persistent `"REJECTED: " + kernel string` label with a toast that shows a friendly line
(e.g., "You can't afford that yet.") and clears after a short delay or the next action; never surface
the raw handler string. Keep the underlying `GD.PushWarning` developer logging (org logging rule) but
off the player surface.

**Execution note.** Non-default proof: assert on **rendered Control state** (button `Disabled`, toast
text), not just sim values — the AE-style "render half" the existing `MainUiTests` already assert. A
disabled control that never submits is the observable contract. Gate each control on **its own
handler's actual legality**, not a shared phase assumption: `CraftingHandlers` is legal in ALL phases
(CraftingHandlers.cs:8,21) while the vendor is Morning-only and ore offers Evening-only — a blanket
"wrong phase = disable everything" rule would wrongly lock crafting.

**Patterns to follow.** `ForgePanel` talent-unlock button (`button.Disabled = !profession.CanUnlock(...)`
— the exact disable-on-illegality pattern to generalise); `CampPanel` Send button
(`Disabled = party.SupplySent || held.IsEmpty`); the "mirror, never replace" doc notes in
`ShopPanel`/`CampPanel`.

**Test scenarios.**
- Happy: with 0 copper, the dagger Craft button renders `Disabled`; with ≥2 copper it enables.
- Happy: vendor Buy for a material the player can't afford renders `Disabled`; affordable enables.
- Edge (phase): the Ledger ore Buy is `Disabled` at Morning/Expedition, `Enabled` at Evening — the exact playtest trap, now unreachable from the UI.
- Error/UX: force a rejection (queue a doomed action programmatically) → a transient player-phrased toast appears and clears; the persistent raw red `"REJECTED: No handler accepts..."` string is **absent** from rendered text.
- Integration (gdUnit, U8): the full vendor→craft→stock→sell loop drives only enabled controls and produces zero rejections.

**Verification.** No illegal/unaffordable action can be submitted from the UI; rejections that do
surface are transient and player-phrased; the raw kernel string never renders. Engine tests green.

---

### U7. Ledger/Camp vertical-text fix — ScrollContainer horizontal collapse

**Goal.** Modal and panel bodies render normally instead of collapsing each autowrap label to one
character per line.

**Requirements.** R7.

**Dependencies.** None (independent; small).

**Sim-vs-godot.** `godot/` layout only.

**Files.**
- Modify: `godot/scripts/panels/SimPanel.cs` — `BuildScrollBody` (the standard panel body used by Forge/Shop) `ScrollContainer` at line ~98.
- Modify: `godot/scripts/panels/LedgerModal.cs` — the `ScrollContainer` at line ~243.
- Modify: `godot/scripts/panels/CampPanel.cs` — the `ScrollContainer` at line ~199.
- Modify: `godot/scripts/panels/HeroesPanel.cs` — the `ScrollContainer` at line ~160.
- Add/Modify test: `godot/tests/` — a gdUnit label-width assertion (see U8).

**Approach.** A `ScrollContainer` with horizontal scroll enabled gives its child unbounded horizontal
space, so an autowrap `Label` (`AutowrapMode.WordSmart`, used by `SimPanel.AddLabel`) wraps at width ~1.
At each site, disable horizontal scrolling
(`HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled`) and/or give the inner `VBoxContainer` a
sensible `CustomMinimumSize`/`SizeFlags.ExpandFill` so the label has a real width to wrap within. The
modals already set the box `CustomMinimumSize` to 640×420; ensure the scroll's content follows that
width. Wide content (if any) scrolls inside its own container, but text bodies wrap normally.

**Execution note.** Default proof (behavioural) — assert rendered label bounding-box width is well
above one character (e.g., `> 100px`) after mounting a populated modal; a width-collapse regression
fails the assert.

**Patterns to follow.** The existing `BuildScrollBody` structure (keep the ScrollContainer + VBox
shape; only fix the scroll mode/width); the modal box `CustomMinimumSize` already set in
`LedgerModal`/`CampPanel`.

**Test scenarios.**
- Happy: mount a populated Ledger → its longest card label renders with width `> 100px` (not `~1 char`).
- Happy: mount the Camp slate with a parked party → labels render at readable width.
- Happy: Forge/Shop bodies (via `BuildScrollBody`) render multi-word labels on one wrapped line each.
- Regression: the assert fails if any of the four sites reverts to default horizontal scroll.

**Verification.** All four scroll sites render autowrapped text at readable width; the label-width
gdUnit assert passes.

---

### U8. Regression tests — gdUnit UI loop + label-width; wire the whole thing

**Goal.** Lock the playable loop and the legibility fix behind engine tests so neither playtest
blocker can ship green again.

**Requirements.** R15 (and it exercises R1–R7 end-to-end).

**Dependencies.** U2, U3, U4, U5, U6, U7.

**Sim-vs-godot.** `godot/` gdUnit tests (drive real Controls through the one adapter). The sim-side
tests for U3/U4/U5 live in those units.

**Files.**
- Create: `godot/tests/PlayableLoopTests.cs` — the full vendor→craft→stock→sell loop through the UI.
- Modify: `godot/tests/MainUiTests.cs` — gated-advance + Auto-toggle scenarios; rejection-toast + disabled-control assertions (R6).
- Create/Modify: `godot/tests/LayoutTests.cs` (or fold into `MainUiTests`) — the label-width assert (R7).
- Modify: `godot/tests/ScriptedSession.cs` / `UiTestSupport.cs` — helpers for a vendor-buy driven session and for reading a Control's rendered width.

**Approach.** Drive a fresh seed-2026 campaign through the real Controls (the `MountMainUi` /
`Press` / `RenderedText` harness the suite already uses): on day 1 Morning press the vendor Buy for
copper, press Craft on a tier-1 recipe, advance one gated phase, stock the craft via the Shop panel,
advance, and assert an `ItemSold` (or at minimum a shelved, priced, hero-visible item) with zero
rejections — the loop the playtest could not complete. Add gated-clock assertions (auto OFF → no
`_Process` advance; press *Advance* → exactly one tick) and R6 assertions (a disabled Craft with no
materials; the Ledger Buy disabled off-Evening; a transient toast on a forced rejection; the raw
`"REJECTED:"` string absent from rendered text). Add the label-width assert from U7.

**Execution note.** These are the two tests the origin doc calls out as missing (R15: "both playtest
blockers shipped CI-green because no test drives the UI loop or asserts label layout"). Assert on
**rendered Control state**, never only sim values.

**Patterns to follow.** `MainUiTests.ForgePanel_CraftRoundTrip...` and `...DriveToCraftedDagger` (the
existing UI-loop driving style); `UiTestSupport` (`MountMainUi`, `Press`, `RenderedText`, `AdvanceDay`);
`ScriptedSession` (deterministic action chooser).

**Test scenarios.**
- Integration (loop): day-1 vendor buy → craft → advance → stock → advance → item is priced/shelved/sold with zero `LastRejections`.
- Integration (gated clock): auto OFF, `_Process(large delta)` → sim unchanged; press *Advance* → one tick.
- Integration (R6): no-material Craft button `Disabled`; off-Evening Ledger Buy `Disabled`; forced rejection → transient toast, raw `"REJECTED:"` absent.
- Integration (R7): populated Ledger/Camp labels render width `> 100px`.
- Determinism: same seed twice → identical rendered loop outcome (mirrors the existing `...IsDeterministic_AcrossRuns` test).

**Verification.** The engine suite drives the full loop and asserts layout; both former blockers are
covered. `dotnet test godot/tests --settings .runsettings` green.

---

## Verification Contract

Gate commands (a unit is not done until its lane is green):

```bash
# Fast sim lane — MUST pass before reporting any sim-touching unit (U3, U4, U5) done
dotnet test sim/GameSim.Tests/GameSim.Tests.csproj --filter Category!=Balance

# Balance gate — MUST stay green for the RNG-order-sensitive sim units (U3, U4, U5)
dotnet test sim/GameSim.Tests/GameSim.Tests.csproj --filter Category=Balance

# Engine tests — needs Godot (GODOT_BIN via .runsettings/env); gates the godot units (U2, U6, U7, U8)
dotnet test godot/tests --settings .runsettings

# Whole build
dotnet build Game.sln
```

**Determinism gates that must remain byte-identical:** `DeterminismTests.SameSeed_SameActions_ByteIdenticalAfter200Ticks`
(fast lane) **and the golden replay `BalanceSimTests.Ae5_HundredDay_ByteIdenticalReplay` — which lives
in `Category=Balance`, so the fast lane alone does NOT run it; sim-touching units (U3/U4/U5) must run
the balance gate locally before "done"**, the balance gate bands, and
the default `NewCampaign(seed)` serialization. Each sim unit's PR body states *why* it cannot perturb
the RNG stream (no RNG drawn; handlers don't reorder systems; new systems draw no RNG — the
`FactionDriftSystem` precedent).

**Gold-conservation gate:** `GoldConservationTests` extended to reconcile the vendor sink
(`MaterialPurchased`) and the stipend source (`RecoveryStipendGranted`) alongside rival sales and
tariff deltas; the invariant must hold tick-by-tick.

## Definition of Done

- BOARD.md carries no conflict markers; gate/claim tables reconcile with merged history (U1).
- With auto-advance OFF, no wall-clock delta advances the sim; the *Advance* button ticks exactly one
  phase; the *Auto* toggle reproduces the timed cadence (U2, R1).
- A fresh campaign can, on day 1 Morning: pick a profession, hold starter stock, buy base material at
  the vendor, craft, stock, and sell — for every profession (U3+U4, R2/R3/R4).
- No constructible state dead-ends the player; the un-losability proof passes (U5, R5).
- No illegal/unaffordable action can be submitted from the UI; surfaced rejections are transient and
  player-phrased; the raw kernel string never renders (U6, R6).
- Ledger/Camp/Forge/Shop/Heroes bodies render text at readable width (U7, R7).
- The gdUnit suite drives the full vendor→craft→stock→sell loop and asserts label layout; both former
  blockers are covered (U8, R15).
- Fast sim lane, balance gate, engine tests, and full build all green; determinism + gold-conservation
  invariants hold (R14).

---

## Scope Boundaries

**In scope:** the player-gated hybrid clock, the Morning materials vendor, starting-profession
selection + starter stock, the no-softlock floor, the rejection-UX/legality gating, the ScrollContainer
legibility fix, and the regression tests — with *functional* (unstyled) presentation only (KD4).

**Out of scope (owned by sibling plans):** art generation/wiring (Plan 2, R8–R10); the full UI rethink
— shared Theme, storefront dashboard, portrait roster, drag-to-craft, venue-map hub (Plan 3, R11–R12);
the dev-time FlavorForge generator (Plan 4, R13).

### Deferred to Follow-Up Work

- **Vendor sell-back.** The no-softlock guarantee (R5) is met without it (KTD-D); a "sell materials/
  crafts back to the vendor for gold" path is a nice-to-have for a later economy pass, and it would
  need its own gold-conservation source-term treatment.
- **Vendor markup tuning.** `VendorMarkupPermille` ships at a recommended default that keeps hero
  offers strictly cheaper; confirming the exact value against the standing gold-inflation anomaly
  (13/20 seeds, `runs/anomalies.md`) is a balance-pass task, not a blocker for playability.
- **Save/load wiring in Godot.** The sim has `SaveCodec`; whether the Godot layer wires quit/resume is
  a fast-follow to verify (origin Scope Boundaries), not committed here.
- **CLI parity for the vendor + profession pick.** The CLI (`sim/GameSim.Cli/Program.cs`) can gain a
  `buymat` command and a `--profession` flag later; this plan targets the Godot playable surface. The
  CLI keeps its blacksmith-default `NewCampaign(seed)` unchanged.
- **New gameplay mechanisms** (venue routing, abilities/leveling, monster dens, disasters, etc.) — the
  origin doc's explicit non-goals for this phase.
