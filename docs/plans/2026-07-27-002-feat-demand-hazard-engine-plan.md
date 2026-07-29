---
title: "Wave 2 — Demand + Hazard Engine (the keystone)"
date: 2026-07-27
type: feat
artifact_contract: ce-unified-plan/v1
artifact_readiness: implementation-ready
execution: code
source: docs/design/2026-07-27-five-pillars-design-synthesis.md (Pillars 4+5 shared build, Unified build sequence — Wave 2)
predecessors: docs/design/2026-07-27-how-you-play.md, docs/design/2026-07-27-gameplay-loop-analysis.md
---

# Wave 2 — Demand + Hazard Engine

## Goal Capsule

- **Objective:** Fix the root cause every pillar traces back to: `CommissionSystem.FindGapSlot` scans
  only `ItemSlot.Weapon/Shield/Armor` (`sim/GameSim/Heroes/CommissionSystem.cs:221-239`), so
  consumables (Alchemy) and trinkets (Engineering/Alchemy) can never be commissioned, and every
  depth-stall gate (`Drama/DemandBoard.cs`'s `DepthStalls`) is Weapon/Shield/Armor too — weapon-making
  professions carry both the gold and the progression; Alchemist/Tanner have supply verbs with no
  matching demand (`docs/design/2026-07-27-gameplay-loop-analysis.md` §9's "two professions have a
  craft, two have only a customer"). This wave builds `NeedsEngine` + `DemandLine` (generalizing
  `Commission`) with 8 demand shapes, plus `HazardDefinition` with 5 hazard axes that create typed
  demand AND apply a real, deterministic expedition penalty when uncountered — **the single
  highest-leverage change in the whole program**; Waves 3-5 all consume its output.
- **Ship behind a flag, flat profession model fully intact.** This wave proves the demand math alone.
  It does **not** touch profession unlocks (`SetProfessionsAction` stays exactly as legal/illegal as it
  is today) — every hero can still only ever want Weapon/Shield/Armor from a starting Blacksmith save
  until Wave 4 flips the unlock state machine on. The flag exists so the new demand math can be
  reviewed, balance-tested, and reverted independently of the profession-restructure wave that consumes it.
- **Stop conditions:** any hazard penalty implemented as a NEW RNG draw site (see KTD-B6 — this is the
  single biggest technical risk in this wave and must stop work for review, not be quietly shipped);
  any change to `sim/GameSim/Contracts/` landing outside a dedicated orchestrator-authored micro-PR;
  any invariant in the Generator Test / anti-repetition suite failing on the flagged-on path.

## Scope Boundaries (what this wave does NOT do)

- Does **not** flip profession unlocks — that's Wave 4. `ProfessionUnlockOffered`,
  `UnlockProfessionAction`, the hero-asks moment, and the branch screen are explicitly out of scope
  here. This wave's hazards exist and apply penalties/typed demand even though every profession is
  already unlocked from day 1 under the current flat model — Wave 4 is what makes a hazard's *debut*
  coincide with a profession's *unlock*.
- Does **not** retune balance numbers — Wave 3 owns the invariant harness and the "lock the curve
  before diversification can drift it" sequencing note (see Cross-pillar risk 1 below). This wave's
  hazard penalty magnitudes and demand-shape premiums should be conservative defaults, explicitly
  flagged as provisional, not hand-tuned against the 100-day sim.
- Does **not** build the ending, the rival vendor "Master Voss," or `Bond`/hero attribution — those are
  Wave 5, and depend on this wave's diverse demand vocabulary but are not part of it.
- Does **not** change `ObjectiveAdvisor`'s priority-stack structure (Wave 1 ships that independently) —
  it DOES require Wave 1's diversification-ready keying (recipe-matches-demand, not
  `Slot ∈ {Weapon,Shield,Armor}`) to already be in place so this wave's `DemandLine.Craft`/`Shape` data
  slots into Wave 1's existing query shape without a rewrite.
- Does **not** add the profession talent-layer trees (Runeforge/Spellsealing) — Wave 4.

## Cross-pillar risks that apply here

- **Risk 1 (sequencing, biggest, synthesis §Cross-pillar risks):** demand diversity *raises* effective
  difficulty on the same 5 daily action slots (4× the demand shapes competing for the same
  craft/buy/bounty pool). Ship this wave's flag-on state only *with* Wave 1's fulfillability gate and
  Wave 3's warnings/invariants pre-landed, or measured expiry/destitution rates will spike relative to
  today's baseline. **Concretely: do not flip the flag to on-by-default until Wave 1 has merged and
  Wave 3's invariant harness exists to catch the regression it would otherwise cause silently.**
- **Risk 5 (serial re-baseline discipline, applies partially here too):** even flagged off-by-default,
  landing `NeedsEngine` as a new Morning `IPhaseSystem` changes the Morning phase's system list — any
  golden/idle-hash trace that walks the full Morning block will re-pin. Treat this as a Class-1 change
  (see KTD-B7) with ONE deliberate re-pin, narrated, even while the flag keeps the new system's
  behavior a no-op against the flat model.

## Key Technical Decisions

- **KTD-B1 — the flag.** A new bool on `GameState`, trailing init member (the `Counter`/`Rent`/
  `Commissions`/`Director`/`Assessment`/`Arc` save-compat precedent already used seven times over in
  `Contracts/World.cs`): `public bool DiversifiedDemand { get; init; } = false;`. `false` is the
  save-compat default (every existing save, every existing golden trace) — `NeedsEngine` and the new
  hazard-penalty check both no-op entirely when this is false, falling through to the EXISTING
  `CommissionSystem`/gate-check code paths byte-for-byte. This one field IS the flag — no separate
  ruleset/config file needed (mirrors `LegacyFlatProfessions` in Wave 4's own migration note, which is
  the same shaped flag one wave later).
- **KTD-B2 — `DemandLine` generalizes `Commission`, contracts micro-PR.** New record in
  `Contracts/World.cs` (or a new `Contracts/Demand.cs` if the orchestrator prefers a dedicated file —
  either way, `Contracts/` is deny-listed, so this is a dedicated micro-PR merged BEFORE this wave's
  dependent module PRs, per the standing contract-amendment rule):
  ```csharp
  public enum DemandShape { GearGap, ConsumableRestock, HazardCounter, PartyOutfit, RushOrder, BiddingWar, VanityPiece, HeirloomRefit }
  public enum CraftDiscipline { Weapon, Shield, Armor, Consumable, Trinket } // maps existing ItemSlot 1:1 plus the two currently-uncommissionable shapes
  public sealed record DemandLine(
      DemandShape Shape, CraftDiscipline Craft, ItemSlot? Slot, QualityGrade MinQuality,
      int Quantity, int PremiumGold, int DeadlineDay, HeroId? Hero, string? Counter);
  ```
  `Commission` is **not deleted** — it stays as the Weapon/Shield/Armor-only legacy shape
  `CommissionSystem`/`CommissionHandlers`/`ObjectiveAdvisor` (pre-Wave-1-rewire) already read, and
  `GameState.Commissions` stays exactly as-is. Add `GameState.DemandLines` as a NEW trailing init
  member (`ImmutableList<DemandLine> DemandLines { get; init; } = ImmutableList<DemandLine>.Empty;`),
  populated ONLY when `DiversifiedDemand == true`. This means, for one release, there are two parallel
  demand records on `GameState` — deliberate, not an oversight: it is what makes the flag genuinely
  reversible (delete `DemandLines`-writing code and old saves/goldens are untouched). Wave 4's
  migration unit is where `Commission` finally folds into `DemandLine` for good (its own "serial
  re-baseline" unit, not this wave's).
  `Counter` (string?) names the hazard-counter tag a fulfilling item must carry for `Shape ==
  HazardCounter`/`RushOrder`-under-hazard lines — null for shapes that don't need one.
- **KTD-B3 — `NeedsEngine`, a new Morning `IPhaseSystem`.** New file
  `sim/GameSim/Drama/NeedsEngine.cs` (or `Heroes/NeedsEngine.cs` — synthesis places it conceptually
  next to `DemandBoard`/`CommissionSystem`; either is fine, pick based on whether it reads more like a
  read-model producer (`Drama/`) or a hero-need scanner (`Heroes/`) once implemented — flag the choice
  in the PR description). Registers in `GameComposition.cs`'s Morning block **before**
  `CommissionSystem` (mirrors the exact ordering comment already there: `new HeroShoppingSystem(),` then
  `new CommissionSystem(), // ... after shopping, before Muster` — `NeedsEngine` slots between those
  two, since it needs post-shopping hero state exactly like `CommissionSystem` does, and
  `CommissionSystem`'s own legacy-shape posting should be gated to run only when
  `!state.DiversifiedDemand`, with `NeedsEngine` taking over `DemandLine` posting when the flag is on —
  the two are mutually exclusive per campaign, not layered). Pure projection like `MusterPlan`/
  `CommissionSystem` itself: reads each hero's class/traits(none yet — Wave 2 has no trait system;
  see KTD-B4)/target-floor/inventory/recent-events, emits `DemandLine`s. **Zero RNG** (KTD5) — every
  existing Morning system this sits beside is RNG-free by the same contract (`FactionDriftSystem`,
  `RentSystem`, `CommissionSystem` doc comments all say "draws no RNG"); `NeedsEngine` must hold that
  line too, since a new draw site here is out-of-scope stream perturbation this wave cannot afford.
- **KTD-B4 — the 8 demand shapes, each keyed off an existing or trivially-added state read** (no shape
  needs new tracking machinery beyond what's listed):
  1. **GearGap** — today's shape verbatim, generalized: `FindGapSlot`'s exact scan, but for
     `CraftDiscipline.Weapon/Shield/Armor` under the same fixed-order-first-gap rule.
  2. **ConsumableRestock** — recurring, small premium: a hero whose `Pack` (existing
     `ImmutableList<ItemId>` on `Hero`) is empty or below a small threshold gets a repeat `DemandLine`
     for `CraftDiscipline.Consumable`. This is the FIRST shape that makes Alchemy's consumables
     commissionable at all.
  3. **HazardCounter** — prep-ahead: a hero about to face a floor carrying an uncountered
     `HazardDefinition` tag (KTD-B5) gets a `DemandLine` with `Counter` set to that tag, `Craft`
     whichever discipline makes the counter item (data on `HazardDefinition`, KTD-B5).
  4. **PartyOutfit** — batch/queue: when `MusterPlan.Compute` groups ≥2 heroes into one party and more
     than one shares the same gear gap, post ONE `DemandLine` with `Quantity > 1` rather than N
     separate ones (tests the batch-pricing path a single-item shape never exercises).
  5. **RushOrder** — 2× `PremiumGold`, half `DeadlineDay - PostedDay`: triggered off the same
     depth-stall detection `DemandBoard.DepthStalls` already runs, but ONLY for a hero whose stall has
     persisted past a second, longer threshold (escalation, not a new detector).
  6. **BiddingWar** — two heroes, one masterwork: when two heroes independently stall on the SAME slot
     in the SAME Morning, post one `DemandLine` at `QualityGrade.Masterwork` naming neither hero
     individually (`Hero: null`) — whichever hero's commission-fulfillment claims it first wins;
     feeds Wave 5's rivalry system later (not built here, just don't preclude it — keep `Hero`
     nullable).
  7. **VanityPiece** — craft for legend, not depth: a hero at `DeepestFloorReached` already at the
     venue's `FloorCount` (maxed) posts a cosmetic-tier `DemandLine` (`QualityGrade.Fine`+, no gate
     implication) so late-game heroes still generate demand instead of going silent.
  8. **HeirloomRefit** — routes the memorial system into an offer instead of a nag: an un-honored
     `Memorial` older than a threshold becomes a `DemandLine` (`Shape = HeirloomRefit`, `Hero = null`,
     since the original hero is dead) that `ReforgeHeirloomAction` fulfills — fires once per memorial,
     expires like any other line (folds the audited "1287× memorial nag" directly into the generator
     instead of leaving it to Wave 1's nag-decay alone; the two are complementary — nag-decay softens
     the OLD path, HeirloomRefit gives the honored-memorial case a productive NEW path).
  Traits are listed in the synthesis as demand-mix parameters ("Cautious Ranger over-orders antidotes")
  — **no trait system exists yet** (Phase B's B2 traits are shop-teeth only per its own Scope
  Boundaries, and even those are 2/hero fixed via `StableHash`, not consumed by demand generation
  today). Treat trait-weighted demand-mix as a **follow-up hook**: write `NeedsEngine`'s shape-selection
  logic to accept an optional per-hero weighting function/table, defaulted to uniform, so a later wave
  can plug Phase B's trait registry in without restructuring this engine. Do not block this wave on
  building that hook's consumer.
- **KTD-B5 — `HazardDefinition`, 5 axes as data rows.** New file
  `sim/GameSim/Venues/HazardDefinition.cs` (sits beside `VenueDefinition`/`VenueFloor` — the existing
  "venue is just data" precedent, P4 kernel doc comment on `VenueDefinition`):
  ```csharp
  public enum HazardAxis { Venom, Arcane, Structural, FrostHeat, Martial }
  public sealed record HazardDefinition(HazardAxis Axis, string CounterTag, int UncounteredPenalty);
  ```
  Extend `VenueFloor` (trailing init member, save-compat — the exact pattern `Item.SignedName`/
  `Item.HeirloomLineage` already establish for additive data on an existing record):
  ```csharp
  public ImmutableArray<HazardAxis> HazardTags { get; init; } = ImmutableArray<HazardAxis>.Empty;
  ```
  Floors 1-2 carry `[Martial]` only (existing weapon-only gate, unchanged, zero new penalty since
  Blacksmith already counters it by construction); floor 3 (~day 8-10) adds the first non-Martial tag;
  floor 4 adds Arcane/Structural; floor 5 stacks dual-tag; floor 6 "the Heart" (Wave 5's ending beat,
  not built here but the data row should exist so Wave 5 doesn't need a `VenueDefinition` edit) carries
  all five. **This wave builds the DATA and the PENALTY MECHANISM; the actual floor-by-floor escalation
  curve/timing tuning is Wave 3+4's balance-pass concern** — ship a first-draft floor→tag assignment
  matching the synthesis's Act structure, explicitly flagged provisional.
- **KTD-B6 — the penalty is a DETERMINISTIC INPUT SHIFT, never a new RNG draw. This is the wave's
  single highest-risk decision — read it twice.** `ExpeditionResolver.cs:288` gates floor entry with
  `if (CombatMath.PartyAveragePower(fighters, items) < venue.Gate(floor)) { halt = GateHeld; }` — a pure
  integer comparison, zero RNG, computed BEFORE any combat roll for the floor. The hazard penalty must
  land HERE, as an addition to the effective gate (or equivalently a subtraction from party power) —
  e.g. `var effectiveGate = venue.Gate(floor) + UncounteredPenaltyFor(floor, party, items);` — computed
  by checking, for each uncountered `HazardTag` on the floor, whether ANY party member's gear/`Pack`
  carries an item tagged with that hazard's `CounterTag` (KTD-B8), and if not, adding
  `HazardDefinition.UncounteredPenalty` to the gate. **Zero new RNG draws, zero reordering of existing
  draws** — the kernel's `RngState` after resolving a floor is affected only insofar as a harder
  effective gate may cause an EARLIER `GateHeld` halt (fewer floors attempted this expedition = fewer
  draws consumed than before), which is the SAME kind of behavior-shift-without-new-draw-sites Phase B's
  B2 traits already precedented for shop behavior (`docs/plans/2026-07-25-002-feat-phaseB-living-heroes.md`
  §KTD-B1: "no new draw SITES... the gate is 'no new draw sites,' not 'no stream movement'") — this
  wave imports that same Class-1 discipline into the raid side, one tier past where Phase B stopped
  itself (Phase B explicitly withheld raid teeth to "Phase C hardening" in its own Scope Boundaries;
  this synthesis's Wave 2 IS that promotion, made deliberately and reviewed here, not smuggled in).
  **If any implementation of this needs a fresh `rng.NextInt`/`rng.Roll100` call anywhere in the hazard
  path, STOP and escalate to the orchestrator before merging** — that would be a Class-2 stream
  perturbation requiring the full serial-hardening treatment Wave 4's migration section already reserves
  for its OWN changes, and this wave must not need it.
- **KTD-B7 — golden-safety classing for this wave, borrowing Phase B's taxonomy verbatim** (see
  `docs/plans/2026-07-25-002-feat-phaseB-living-heroes.md` for the full Class 0/1/2 definitions this
  plan reuses rather than re-deriving):
  - `DemandLine`/`HazardDefinition`/`VenueFloor.HazardTags`/`GameState.DiversifiedDemand`/
    `GameState.DemandLines` additions, while `DiversifiedDemand == false` and nothing reads/writes them
    yet: **Class 0a** (shape-only, values identical).
  - `NeedsEngine` posting `DemandLine`s once the flag is flipped on in a test/dev campaign: **Class 0b**
    (new events/values, draw-free, decision-free from the KERNEL's perspective — but see below, this
    DOES change hero purchase decisions once fulfilled, which is really Class 1 the moment a
    `DemandLine` gets fulfilled and changes what a hero buys).
  - The hazard-gate penalty (KTD-B6) and any resulting change in which floors get attempted/who dies:
    **Class 1** (behavior-without-draws) — ONE deliberate idle-hash re-pin + ONE Balance re-fit when
    the flag flips on for the first tracked campaign, exactly the U9/B2 playbook.
  - **Nothing in this wave may be Class 2.** The grep gate from Phase B's B2 applies verbatim: confirm
    `rng.(NextInt|Roll100)` stays confined to its existing three sites
    (`ExpeditionResolver`/`QualityRoller`/`HeroRoster.CreateRecruit`) and `NeedsEngine`/
    `HazardDefinition`/the gate-penalty code contain zero `rng.` calls — a code-review/grep gate, run
    it explicitly before merging each unit.
- **KTD-B8 — "counters this hazard" needs a data marker on recipes/items — a second, smaller contracts
  touch.** Add `ImmutableArray<string> HazardCounters { get; init; } = ImmutableArray<string>.Empty;`
  as a trailing init member on `Recipe` (in `sim/GameSim/Crafting/`, NOT `Contracts/` — check whether
  `Recipe` lives in `Contracts/Items.cs` or a `Crafting/`-local file before assuming; if it's
  `Contracts/`-resident, this rides the SAME `DemandLine` micro-PR as KTD-B2 rather than a separate one)
  naming the `HazardAxis.CounterTag` values an item crafted from this recipe satisfies when equipped or
  carried (an antidote's `CounterTag` = "Venom", tanned hide armor's = "Venom" too — the two revenue
  shapes the synthesis calls out teaching side by side at the SAME hazard debut). `Item` inherits the
  tag set at craft time (copy from `Recipe.HazardCounters` onto a new `Item.HazardCounters` trailing
  init member, the `SignedName`/`HeirloomLineage` precedent again) so the KTD-B6 gate check reads the
  ITEM, never re-derives from the recipe id at combat time (keeps `ExpeditionResolver` decoupled from
  `Crafting`/`Professions`).
- **KTD-B9 — hazard→counter-item→chronicle-save.** When an uncountered-tag penalty WOULD have applied
  but the party carries the counter (so it didn't), stamp a new lightweight event (append to
  `Contracts/Events.cs` in the same micro-PR) — e.g.
  `HazardCountered(HeroId Hero, HazardAxis Axis, ItemId Item, int Floor)` — read by the existing
  chronicle/gossip surface (`GossipGenerator`/`LedgerQuery`, whichever already turns `AttributionBeat`-
  adjacent events into prose) to produce the exact "Kara's filter draught held through the gas pocket —
  maker's mark: you" line the synthesis specifies. This event is informational only (no sim rule reads
  it back) — the north-star beat made mechanical without adding a second combat-resolution code path.
- **KTD-B10 — broke-state income floor + memorial-nag fold-in are DemandLine consumers, not new
  systems.** The synthesis's "folds in the two old audit wounds" is satisfied by: (a) an always-on
  low-value `ConsumableRestock`/`GearGap` line for a hero at minimum gold (reuses
  `DestitutionRecoverySystem`'s existing no-softlock arithmetic as the floor reference — do not build a
  second floor-detection mechanism); (b) HeirloomRefit (KTD-B4 shape 8) as the memorial-nag fold. No
  new subsystem beyond the 8 shapes.

## Implementation Units

### U1. Contracts micro-PR — `DemandLine`, `HazardDefinition` data shapes, `GameState` flag

- **Goal:** Every new data shape this wave needs exists, compiles, and is inert (Class 0a) before any
  behavior touches it.
- **Files:** `sim/GameSim/Contracts/World.cs` (or a new `Contracts/Demand.cs`) — `DemandShape`,
  `CraftDiscipline`, `DemandLine`, `GameState.DiversifiedDemand`, `GameState.DemandLines`. Possibly
  `Recipe`/`Item`'s `HazardCounters` field if `Recipe` is contracts-resident (KTD-B8).
- **Approach:** Orchestrator-authored, per the standing rule (KTD9 in the master plan). Merge BEFORE
  any dependent module PR in this wave.
- **Test scenarios:** compiles; `GameState.DiversifiedDemand` defaults false and
  `GameState.DemandLines` defaults empty on every existing golden/save fixture (an explicit
  deserialize-old-save-shape test, mirroring the `Counter`/`Rent` precedent's own test coverage);
  idle-hash trace byte-identical (Class 0a — nothing reads the new fields yet).
- **Verification:** fast lane green; no re-pin needed yet.
- **Dependencies:** none.

### U2. `HazardDefinition` + `VenueFloor.HazardTags` — data only

- **Goal:** The 5 hazard axes exist as data on the Mine's floors, still inert.
- **Files:** `sim/GameSim/Venues/HazardDefinition.cs` (new), `sim/GameSim/Venues/VenueFloor.cs`
  (extend), `sim/GameSim/Venues/VenueRegistry.cs` (populate the Mine's floor rows with first-draft
  `HazardTags` per the Act-structure escalation curve in KTD-B5).
- **Approach:** Pure data. No resolver change yet (that's U4).
- **Test scenarios:** `VenueRegistry.Mine`'s floor 1-2 carry only `Martial`; floor 3+ carry the
  documented first-draft tags; `VenueFloor` round-trips through save/load unchanged for a save with no
  `HazardTags` in its JSON (empty array default).
- **Verification:** fast lane green; Class 0a (nothing reads `HazardTags` yet).
- **Dependencies:** U1 (if `HazardDefinition` needs any contracts-resident type — otherwise independent).

### U3. `NeedsEngine` — the 8 demand shapes, flag-gated

- **Goal:** `NeedsEngine.Process` posts `DemandLine`s that exercise all 8 shapes when
  `state.DiversifiedDemand == true`; no-ops entirely when false.
- **Files:** `sim/GameSim/Drama/NeedsEngine.cs` (or `Heroes/`, per KTD-B3's siting note),
  `sim/GameSim/GameComposition.cs` (register in the Morning block before `CommissionSystem`, gate
  `CommissionSystem`'s legacy posting to `!state.DiversifiedDemand`).
- **Approach:** KTD-B3/B4/B10. Reuse `MusterPlan.Compute`, `DemandBoard.DepthStalls`'s stall-detection
  arithmetic, `DestitutionRecoverySystem`'s floor arithmetic — do not re-derive any of these.
- **Test scenarios:**
  - Flag off → `state.DemandLines` stays empty across a multi-day trace; `Commissions` populate exactly
    as today (regression pin — this is the wave's core reversibility guarantee).
  - Flag on → each of the 8 shapes is exercised by at least one scripted scenario (a fixture per shape,
    mirroring `DramaFixtures.cs`'s pattern), asserting the exact `DemandLine` fields posted.
  - PartyOutfit posts ONE line with `Quantity > 1` for a shared gap, not N separate lines.
  - BiddingWar posts with `Hero: null` when two heroes share a same-Morning stall on the same slot.
  - VanityPiece/HeirloomRefit fire only under their specific trigger conditions and never duplicate an
    existing open line for the same hero/memorial.
  - No RNG draw (grep gate, KTD-B7): `NeedsEngine.cs` contains zero `rng.` calls; kernel `RngState`
    after a flag-off trace is byte-identical to pre-wave.
- **Verification:** fast lane green; grep gate green; Class 0b when flag on (idle-hash moves by values
  only, narrated re-pin).
- **Dependencies:** U1, U2.

### U4. Hazard penalty in `ExpeditionResolver` + counter-item check

- **Goal:** An uncountered hazard tag makes a floor's effective gate harder, deterministically, using
  the party's carried counter-items; a countered hazard stamps `HazardCountered` for the chronicle.
- **Files:** `sim/GameSim/Expedition/ExpeditionResolver.cs` (the `Gate` check at line ~288),
  `sim/GameSim/Contracts/Events.cs` (the `HazardCountered` event, same micro-PR as U1 if not already
  included), `Crafting/` (or wherever `Recipe`/`Item.HazardCounters` lands per KTD-B8).
- **Approach:** KTD-B6, KTD-B8, KTD-B9. This unit carries the wave's biggest risk — implement it with
  the grep-gate check run locally before every commit, not just at PR time.
- **Test scenarios:**
  - A party lacking the floor's hazard counter fails the gate at a LOWER party-power than before
    (regression-style: same party/gear, flag+hazard-on vs flag-off, deterministic power comparison).
  - A party carrying the counter item clears the gate exactly as before (byte-identical to the
    no-hazard path) AND stamps `HazardCountered`.
  - **Zero new RNG draws**: `state.Rng` after resolving an identical scripted expedition is IDENTICAL
    between a countered-hazard run and a no-hazard-tag run (same floors attempted → same draw count) —
    the explicit stream-purity regression test for this unit.
  - Counterfactual purity is unaffected (KTD6's existing `AttributionEngine` tests must still pass
    unchanged — the hazard gate sits strictly before combat, never inside the attribution recompute).
- **Verification:** fast lane green; the zero-new-draws test is a NAMED, permanent regression test, not
  a one-off manual check; ONE deliberate idle-hash re-pin + ONE Balance re-fit when a scripted test
  campaign turns the flag on (Class 1, per KTD-B7).
- **Dependencies:** U1, U2, U3.

### U5. Test suite — anti-repetition invariants (the Generator Test as CI)

- **Goal:** The day-11 repetition finding (a "question shortage, not a content shortage") gets a
  permanent CI tripwire so it can never silently regress once this wave's flag is on by default.
- **Files:** new `sim/GameSim.Tests/Balance/DemandDiversityBalanceTests.cs`, `[Trait("Category",
  "Balance")]`.
- **Approach:** Run a 100-day trace with `DiversifiedDemand = true` (a test-local scripted policy
  extending `BaselinePlayer`'s shape but accepting/fulfilling `DemandLine`s too — per the
  `BaselinePlayer.cs` "test-local scripted policy, never in Harness" precedent) and assert:
  - **No `(Shape, Craft)` pair exceeds 50% of all posted `DemandLine`s over any trailing 5-day window.**
  - **All 4 crafts (`CraftDiscipline.Weapon/Shield/Armor/Consumable/Trinket` mapped to the 4
    professions) receive at least one `DemandLine` by day 10.**
  - **≥5 distinct `DemandShape` values are fulfilled (not just posted) over the 100-day run.**
  - Determinism: the same seed run twice under the flag → byte-identical `DemandLines` sequence.
- **Verification:** `dotnet test sim/GameSim.Tests/GameSim.Tests.csproj --filter Category=Balance` green.
- **Dependencies:** U1-U4.

## Verification Contract

| Gate | Command | Proves |
|---|---|---|
| Fast lane | `dotnet test sim/GameSim.Tests/GameSim.Tests.csproj --filter Category!=Balance` | U1-U4 unit tests; flag-off regression (byte-identical to pre-wave); zero-new-draws test |
| Balance gate | `dotnet test sim/GameSim.Tests/GameSim.Tests.csproj --filter Category=Balance` | Anti-repetition invariants (U5, the Generator Test); existing `BalanceSimTests` bands, re-fit once under the flag (Class 1) |
| Grep gate | manual/CI grep for `rng\.(NextInt|Roll100)` outside the 3 known sites | KTD-B6/B7's no-new-draw-site guarantee |
| Reversibility check | flip `DiversifiedDemand` false → true → false across a save/load cycle | The flag genuinely isolates old and new demand paths (KTD-B1) |

## Definition of Done

- U1-U5 merged; fast lane green; Balance gate green (existing bands re-fit once, narrated; new
  anti-repetition invariants passing).
- Flag ships **off by default** at the end of this wave — flipping it on by default is explicitly a
  Wave 3-gated decision (Cross-pillar risk 1), not this wave's call to make unilaterally.
- Every one of the 8 demand shapes has at least one fixture test pinning its exact posted fields.
- The hazard-penalty mechanism has a named, permanent zero-new-RNG-draws regression test.
- `Contracts/` touches (U1, and `Events.cs`'s `HazardCountered`) landed as dedicated
  orchestrator-authored micro-PRs, merged ahead of U2-U5.
- Wave 1's `ObjectiveAdvisor` is NOT modified by this wave (it already reads `ProfessionRegistry`-shaped
  queries per its own diversification-ready keying; wiring it to actually read `DemandLine`s instead of
  `Commission`s is explicitly deferred — flag it as a fast-follow once this wave's flag flips on by
  default, so Wave 1 doesn't need to re-open).
