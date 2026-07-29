---
title: "Wave 5 — Hero Attribution + Ending + Rival"
date: 2026-07-27
type: feat
artifact_contract: ce-unified-plan/v1
artifact_readiness: implementation-ready
execution: code
source: docs/design/2026-07-27-five-pillars-design-synthesis.md (Pillars 2+4 payoff, Unified build sequence — Wave 5)
predecessors: docs/design/2026-07-27-how-you-play.md, docs/design/2026-07-27-gameplay-loop-analysis.md
---

# Wave 5 — Hero Attribution + Ending + Rival

## Goal Capsule

- **Objective:** Legibility (you can *read* a hero — Phase B, shipped) has a missing half: attribution
  — a hero's choices visibly tracing back to a NAMED player action, felt **in-market** (the Morning
  shopping pass), not only in the postmortem chronicle. Ship the `Bond` record (Loyalty/Grief/Gratitude,
  3-entry provenance ring) and its three echoes; the felt-moment Godot surfaces; gossip/reputation/
  rivalry as thin Bond-mark amplifiers; the Ledger-of-Legends ending at a new Floor 6 "the Heart"; and
  rival crafter Master Voss (expired/declined demand routes to him, turning every miss into story
  instead of a silent mood hit). This is the payoff layer — everything else in the program (Wave 2's
  demand diversity most of all) exists so this wave has real vocabulary to attribute against.
- **DEPENDS ON Wave 2.** The synthesis is explicit: "diverse demand = the vocabulary attribution needs."
  Do not start this wave's Bond-echo tuning against the current (pre-Wave-2) weapon-monotone baseline —
  that baseline is the thing being deleted, and magnitudes tuned against it will need re-tuning the
  moment Wave 2's flag flips on. Soft dependency on Wave 4 too: Master Voss's "expired demand routes to
  him" is far more interesting once `DemandLine`'s 8 shapes (Wave 2) and profession-typed commissions
  (Wave 4) exist — build Voss against `DemandLine`, not the legacy `Commission` shape.
- **Stop conditions:** any Bond-touched code path that reads into expedition RESOLUTION (party
  formation, floor choice, combat math, loot) — PKD7 ("influence, never orders") is constitutional here
  above all else, since Bond is the single most temptation-prone surface in the whole program to
  accidentally let cross that line; the guard test (KTD-E4) must be written and green before ANY other
  Bond-consuming code lands, not after.

## Scope Boundaries (what this wave does NOT do)

- Does **not** let Bond touch expedition resolution, party formation, floor-target choice, or any
  combat/loot math — market only (shopping pass, counter, pricing, commission fulfillment). This is not
  a soft preference; it is the one hard constitutional rule this wave answers to (PKD7).
- Does **not** retune Wave 2's demand-shape premiums or Wave 3's balance bands — Bond's magnitudes are
  THIS wave's own new tuning surface, explicitly provisional (see the "don't over-tune" note below),
  layered on top of, never replacing, the existing willingness math.
- Does **not** build a second attribution/counterfactual mechanism — the existing `AttributionEngine`/
  `AttributionBeat` (KTD6, the killing-blow/lethal-save/breakpoint-clear machinery) is untouched;
  `Bond` is a NEW, separate signal (relationship, not combat causality) that happens to READ some of the
  same `ItemMemory`/`AttributionBeat` data as its trigger inputs.
- Does **not** implement NG+, true multi-ending branching, or a scored leaderboard beyond the single
  "Legend Count" cross-run number — the ending is one hard authored beat plus an open scored epilogue,
  not endless content (explicitly bounding scope is itself the synthesis's "strongest anti-treadmill
  move").
- Does **not** build the full Erenshor rivalry system — gossip/reputation/rivalry here are "amplifiers
  on Bond marks, ~3 integers total," not the deferred fuller wave (Phase B's own Scope Boundaries
  already parked "Erenshor wave D (full rivalry)" as its own future plan — this wave stays inside that
  same restraint).

## Cross-pillar risks that apply here

- **Risk 3 (don't over-tune Bond magnitudes against the pre-diversification weapon-monotone,
  synthesis §Cross-pillar risks, verbatim):** every Bond-echo magnitude shipped in this wave
  (`+120‰`/`+60‰` survival loyalty, `+350‰`/`−50‰` death aversion, the gratitude rate-limit) is a
  first-draft number, explicitly re-measured once Wave 2's diversified demand is live and default-on —
  do not treat these as final tuning; flag them exactly as provisional as Wave 4's day-14 fallback is
  flagged in that wave's own plan.

## Key Technical Decisions

- **KTD-E1 — the `Bond` record, contracts micro-PR.** New in `Contracts/Heroes.cs`, following the
  `Hero.MoodPermille`/`ItemMemory` precedent to the letter (verified by direct read of that file):
  ```csharp
  public sealed record BondMark(string Kind, string Detail, int Day); // provenance ring entry

  public sealed record Bond(
      int LoyaltyPermille,    // 0..+1000
      int GriefPermille,      // 0..+1000, decays 50/evening
      int GratitudePermille,  // 0..+600,  decays 30/evening
      ImmutableList<BondMark> Marks) // capped at 3 entries, oldest evicted (the gossip-cap precedent already used elsewhere in Drama/)
  {
      public static readonly Bond None = new(0, 0, 0, ImmutableList<BondMark>.Empty);
  }
  ```
  Add to `Hero` as a trailing init member (the exact `MoodPermille`/`Xp`/`Pack` pattern already three
  times precedented on this same record): `public Bond Bond { get; init; } = Bond.None;`. Old saves
  deserialize to `Bond.None` — zero behavior change until something reads it (Class 0a until wired).
  Channels are SEPARATE (a hero can be loyal to the player AND grieving a friend who died in the
  player's armor) — never collapse them into one scalar.
- **KTD-E2 — the three echoes, each a small, additive write + a small, additive read.**
  - **Survival loyalty:** on a floor-clear/save event where the hero's gear is player-marked
    (`Item.PlayerCrafted`/`Item.Mark`), bump `LoyaltyPermille` by `+120‰` (first-time-this-floor clear)
    or `+60‰` (a save/repeat), clamped to 1000. **Dies → loyalty converts**, does not vanish: on
    `HeroDied` for a hero carrying `LoyaltyPermille >= 300`, grant the item a `ReforgeHeirloom`
    tier-gate WAIVER (a new trailing-init flag on the relevant heirloom-reforge state, e.g.
    `HeirloomWaivesTierGate` read by `Crafting/HeirloomHandlers.cs`'s existing tier-gate check — locate
    that guard before assuming its exact shape) rather than the item's Bond simply being discarded with
    the dead hero — *"he never bought from anyone else."* The item ALSO accrues a deed epithet from the
    EXISTING `ItemMemory`/history mechanism (`Item.History`, `ItemHistoryEntry`) — no new lineage
    system, reuse what's there.
  - **Death aversion (witness-only, not campaign-wide):** on `HeroDied` in player-marked gear, every
    OTHER hero who was in the SAME party (a "witness," per the existing `ExpeditionResult.Party` list —
    do not scan the whole roster) gets `GriefPermille += 350‰`, decaying `−50‰`/Evening (a ~7-day
    shadow). While `GriefPermille >= 150‰`, that witness's shopping AI demands **tier+1 or nothing** in
    the slot the fallen died wearing — a WANT-upgrade (always answerable at the forge — the hero simply
    won't buy below tier+1 in that slot while grieving), never a boycott (the hero still buys freely in
    every OTHER slot). Implement as an additional filter in `Heroes/ShoppingAi.cs`'s existing verdict
    gates (the veteran-quality/gear-score-must-improve precedent Phase B's B2 already established as the
    shape for "a trait/state changes a shopping filter") — not a new shopping subsystem.
  - **Gift memory:** `BuyOreAction` overpay (the existing "tariffed cost vs. full ask, a gold-gift
    lever" mechanic the gameplay-loop-analysis §1/§3 already documents) bumps `GratitudePermille`,
    **rate-limited**: at most one bucket-increment per day, per-hero cap at 600‰, decaying `−30‰`/
    Evening. The `BuyOre` handler already computes the tariffed-vs-ask delta for the gold-conservation
    ledger — reuse that same delta as the gratitude trigger input rather than re-deriving it. Trait-
    flavored voice (Proud hero: *"I repay debts."*; Timid: *"You didn't have to."*) is PRESENTATION only
    (chronicle/gossip line selection keyed off Phase B's existing trait registry, if traits are live by
    the time this ships — if not, ship generic gratitude copy and flag the trait-flavoring as a
    small follow-up, not a blocker).
- **KTD-E3 — feeds the EXISTING PA4 willingness math, additively, clamped — no new pricing pipeline.**
  `Counter/WillingnessModel.cs`'s `TrueWillingness` already takes a `traitPermille` parameter (Phase
  B's precedent, defaulted 0 for byte-identical old callers) alongside `classId`/`interestPermille`/
  `moodPermille`/`quality`. Add a `bondPermille` parameter the SAME way: `factor = ClassPriceFactor +
  interestPermille + moodPermille + QualityBonus + traitPermille + bondPermille`, defaulted `0` so
  every existing caller/test is byte-identical until explicitly threaded. Compute `bondPermille` from
  a hero's `Bond` as a small, clamped, signed combination (e.g. `Loyalty/4 - Grief/4 + Gratitude/6`,
  clamped to some modest band like `±150‰` — the exact weights are this wave's own provisional tuning,
  not derived from anything upstream; pick conservative starting weights and flag them explicitly
  provisional per Risk 3). **O(heroes), never O(history)** — `bondPermille` reads only the hero's
  current `Bond` fields, never replays the event log.
- **KTD-E4 — PKD7 guard test, written and green BEFORE any Bond-consuming shopping/counter code lands.**
  New test (likely `sim/GameSim.Tests/Expedition/BondPurityTests.cs`): run the SAME scripted seed and
  SAME action sequence twice — once with every hero's `Bond` forced to `Bond.None`, once with every
  hero's `Bond` forced to its per-field maximum (`Loyalty=1000, Grief=1000, Gratitude=600`, marks full)
  — and assert the two runs produce **byte-identical floor/kill/death outcomes** in `ExpeditionResult`
  (deepest floor cleared, survivors, deaths, attribution beats). This is the load-bearing regression
  test for the whole wave's constitutional promise; every subsequent unit's PR must keep it green, and
  it should be written FIRST (even trivially green, since nothing reads Bond yet) so later units are
  developed against a live tripwire rather than one added retroactively.
- **KTD-E5 — felt-moment Godot surfaces (adapter-only).**
  - **Hero-card bond glyph:** hammer (loyalty)/black-band (grief)/open-hand (gratitude) icon, whichever
    channel is currently dominant; tap renders the TOP `BondMark` (from the 3-entry ring) as a sentence.
  - **Morning shopping-pass staging:** loyal-first heroes visibly queue/shop first (a presentation
    REORDER of the existing shopping-pass iteration, mirroring how `RelationshipBand` already reorders
    the counter queue — Bond reorders the ATOMIC shopping pass the same way, band-and-Bond both being
    "derived, reorders only" per that file's own doc comment); a griever visibly "pauses at the rack"
    in the slot they're avoiding; a gifted hero's sprite "heads for your stock" first.
  - **Chronicle lines** naming the item, recurring verbatim when the eventual loyalty commission arrives
    (reuses the existing gossip/chronicle event-reference discipline — R14's "no disconnected flavor"
    type constraint, `GossipGenerator`'s existing event-id requirement).
  All three are `godot/scripts/`-only reads of `Hero.Bond` — zero new sim rules in Godot code.
- **KTD-E6 — gossip/reputation/rivalry as thin Bond-mark amplifiers, ~3 integers total.** Do not build
  a new relationship-edge system distinct from Phase B's B3 `RelationshipSystem.cs` (sparse decaying
  signed `RelDelta`, already shipped/shippable per that wave's own plan) — if B3 exists by the time this
  unit starts, ADD Bond-derived amplification as a multiplier/offset on gossip salience weighting and
  on B3's existing relationship-kind set, rather than a parallel system. Rivalry = ONE comparison (which
  of two heroes has the higher combined Bond-with-player score) + a small line table ("two heroes
  competing to be your favorite") — no new mechanical effect beyond flavor lines, this wave's own
  restraint matching Phase B's B3 scope note ("wave D owns full rivalry," this is not wave D).
- **KTD-E7 — the Ledger-of-Legends ending: Floor 6 "the Heart."** `VenueRegistry.Mine` currently has
  `FloorCount = 5` (`sim/GameSim/Venues/VenueRegistry.cs`) — this wave extends it to 6 floors, adding
  `VenueFloor` row 6 tagged with ALL 5 `HazardAxis` values (Wave 2's KTD-B5 explicitly reserves this row
  for exactly this purpose: "Floor 6... carries all five"). **This is a wide-touching change** — every
  piece of code that reads `VenueRegistry.Mine.FloorCount` as "the target depth" (`DemandBoard.DepthStalls`,
  `MusterPlan`, `ArcDirectorSystem`'s Act-transition threshold, any Balance-sim band keyed to "reaches
  the top floor") shifts its meaning the moment floor 6 exists. Treat this as its OWN Class-1
  re-baseline sub-step (mirrors Wave 4's KTD-D9 migration discipline) inside this wave: land the new
  floor + every consumer's threshold update + the re-pinned goldens in ONE PR, not spread across several.
  The ending beat itself: when a party clears floor 6 wearing a full-party multi-craft masterwork outfit
  covering every hazard tag present, the reveal is narratively "you don't fight, you watch whether your
  work holds" — mechanically this is just the EXISTING `ExpeditionResult`/reveal pipeline with a new,
  one-time narrative overlay (Godot-side) keyed off `DeepestFloorCleared == 6`, not a new resolution
  mode. `ArcDirectorSystem` should transition `CampaignAct.Ended` on this same condition (verify/update
  its existing threshold, `sim/GameSim/Arc/ArcDirectorSystem.cs:83`, which already sets `Ended` at SOME
  depth condition today — confirm it currently reads floor 5 and update to floor 6, rather than adding a
  second ending-trigger path).
  **Epilogue — "The Ledger of Legends":** a presentation-layer report (CLI + Godot) walking every hero
  the player ever outfitted and each piece's fate — this is a READ over EXISTING data
  (`ItemHistoryEntry`, `Memorial`, `HeirloomLineage`, `Bond.Marks`) with zero new sim state, analogous to
  `tools/Analytics`'s existing read-model reports. **Legend Count** — one new cross-run integer score
  (e.g. count of `IsSigned`/`IsHeirloom` items plus honored memorials plus Bond marks accrued) computed
  the same way, a pure derived number, not a stored field requiring its own contract. Failing forward is
  on-theme: the ending stays reachable and legends include the fallen — do not gate the epilogue on a
  "clean" run.
- **KTD-E8 — Master Voss (1 portrait + 2 integers).** A rival crafter, DATA-driven the same way the
  existing rival vendor (`Economy/RivalCatalogTests.cs`-adjacent code, `RivalRestockSystem`) already is
  — Voss is a NAMED extension of that existing rival-vendor concept, not a new NPC system. When a
  `DemandLine` (Wave 2) expires or is declined, route it to Voss: stamp an event (e.g.
  `DemandRoutedToRival(DemandLine, HeroId)`) the chronicle turns into a named story beat ("Voss shod
  Kara's party") instead of the current silent mood-hit-only path. Reputation = a demand-mix dial: 2
  integers (`WeaponRepPermille`, `AlchemyRepPermille` or similar, tracked exactly like
  `RivalMarketSharePermille` already is on `GameState` — same trailing-init save-compat shape) that
  bias which `DemandShape`s `NeedsEngine` (Wave 2) is more likely to route the player's way vs. Voss's
  way. High weapon-rep attracts VanityPieces/BiddingWars; low alchemy-rep routes restock lines
  Voss-side, winnable back by fulfilling a few. **This is Wave 2's `NeedsEngine` gaining a second,
  optional bias input — do not fork `NeedsEngine`'s core loop; add the reputation read as one more
  weighting term inside it**, consistent with KTD-B4's own "accept an optional per-hero weighting
  function" hook the Wave-2 plan left in place for exactly this kind of later consumer.

## Implementation Units

### U1. Contracts micro-PR — `Bond`, `BondMark`, `Hero.Bond`

- **Goal:** The data shape exists, inert, save-compat.
- **Files:** `Contracts/Heroes.cs`.
- **Approach:** KTD-E1.
- **Test scenarios:** old saves deserialize to `Bond.None`; idle-hash byte-identical (Class 0a).
- **Verification:** fast lane green.
- **Dependencies:** none (can start immediately, in parallel with Waves 2-4 landing).

### U2. PKD7 guard test (write FIRST, before any echo lands)

- **Goal:** The constitutional tripwire exists and is green before it has anything real to catch.
- **Files:** new `sim/GameSim.Tests/Expedition/BondPurityTests.cs`.
- **Approach:** KTD-E4.
- **Test scenarios:** Bond-zeroed vs Bond-maxed, same seed/actions → byte-identical `ExpeditionResult`
  fields (deepest floor, survivors, deaths, beats). Trivially true today (nothing reads Bond); stays
  the standing gate every later unit must not break.
- **Verification:** fast lane green.
- **Dependencies:** U1.

### U3. The three echoes

- **Goal:** Survival loyalty (+ death→waiver conversion), witness-only death aversion, rate-limited
  gift memory — all writing `Hero.Bond`.
- **Files:** wherever floor-clear/save events resolve (`Expedition/`), `HeroDied` handling
  (`Drama/FarewellHandlers.cs`/wherever `HeroDied` is emitted — locate before assuming), `Crafting/
  HeirloomHandlers.cs` (tier-gate waiver), `Heroes/ShoppingAi.cs` (grief want-upgrade filter),
  `Economy/OreMarketHandlers.cs` (gratitude trigger off the existing tariff-delta computation).
- **Approach:** KTD-E2.
- **Test scenarios:**
  - Loyalty accrues on floor-clear/save in marked gear, clamped at 1000; a second clear doesn't
    double-count past the clamp.
  - A loyal hero's death (`Loyalty >= 300`) grants the reforge tier-gate waiver; a non-loyal hero's
    death does not.
  - Party-mate witnesses gain Grief on a death; heroes NOT in that party do not (campaign-wide leak
    test — the explicit negative case).
  - A griever (`Grief >= 150`) refuses below tier+1 in the fallen's slot, but buys freely in every
    other slot (both the refusal AND the "not a boycott" positive case are asserted).
  - Gratitude increments at most once per day per hero regardless of how many `BuyOre` overpays occur
    that day; decays at the documented rate.
  - **PKD7 guard (U2) stays green** after this unit lands — the single required regression check.
- **Verification:** fast lane green; U2's guard green.
- **Dependencies:** U1, U2.

### U4. Willingness-math integration

- **Goal:** `Bond` feeds pricing/counter behavior additively, clamped, zero new pipeline.
- **Files:** `sim/GameSim/Counter/WillingnessModel.cs`.
- **Approach:** KTD-E3.
- **Test scenarios:** `bondPermille` defaults 0, every pre-existing `TrueWillingness` call/test byte-
  identical; a scripted high-Bond hero shows measurably higher willingness than an identical hero at
  `Bond.None`, within the documented clamp band.
- **Verification:** fast lane green.
- **Dependencies:** U1, U3.

### U5. Godot felt-moment surfaces

- **Goal:** Bond glyph, shopping-pass staging, chronicle lines — all visible in the 3D client.
- **Files:** `godot/scripts/` (hero card, town/shopping-pass staging, chronicle/ticker).
- **Approach:** KTD-E5.
- **Test scenarios (gdUnit4Net):** hero card renders the correct glyph + top `BondMark` sentence for a
  scripted Bond state; a loyal hero's sprite visibly shops before a stranger's in a scripted Morning.
- **Verification:** engine-lane CI green; manual smoke.
- **Dependencies:** U3.

### U6. Gossip/reputation/rivalry amplifiers

- **Goal:** ~3 integers, thin amplifiers on Phase B's B3 edges (if shipped) or a minimal standalone
  weighting if not yet.
- **Files:** wherever B3's `RelationshipSystem.cs` lives (if shipped) or a small standalone module.
- **Approach:** KTD-E6.
- **Test scenarios:** rivalry comparison picks the higher-Bond hero deterministically; gossip salience
  measurably shifts with Bond amplification (a before/after scripted comparison, not a new mechanic
  test).
- **Verification:** fast lane green.
- **Dependencies:** U3; soft dependency on Phase B's B3 (non-blocking — build standalone if B3 isn't live).

### U7. Floor 6 "the Heart" + ending + Ledger-of-Legends epilogue

- **Goal:** A 6th floor carrying all 5 hazard tags; the ending beat; the epilogue report; Legend Count.
- **Files:** `sim/GameSim/Venues/VenueRegistry.cs` (new floor row), every `FloorCount`-reading consumer
  (`DemandBoard.cs`, `MusterPlan`, `ArcDirectorSystem.cs`'s ending threshold — audit and update all in
  this one PR), new epilogue report code (CLI + `godot/scripts/`).
- **Approach:** KTD-E7. Treat the floor-6 addition as its own serial re-baseline sub-step.
- **Test scenarios:**
  - Floor 6 requires all 5 hazard counters simultaneously (a full-party multi-craft outfit) — a
    scripted party missing even one counter fails the gate exactly as any other uncountered-hazard
    case does (reuses Wave 2's mechanism, no new gate logic).
  - `ArcDirectorSystem` transitions to `CampaignAct.Ended` on floor-6 clear, not floor-5 (regression:
    the OLD floor-5 threshold no longer ends the campaign prematurely).
  - Epilogue report lists every outfitted hero + item fate for a scripted multi-death, multi-legend
    campaign; Legend Count computes as a pure function of existing data (no new stored field).
  - Full re-pin of every `FloorCount`-dependent golden/Balance band, narrated in one PR.
- **Verification:** fast lane green; Balance gate green (re-fit, narrated); manual smoke — clear floor
  6, confirm the ending beat and epilogue render in the Godot client.
- **Dependencies:** Wave 2, Wave 3's invariant harness (so the re-baseline is measured against a pinned
  reference), U1-U6 substantially landed (the epilogue reads Bond/loyalty data).

### U8. Master Voss

- **Goal:** Expired/declined `DemandLine`s route to a named rival; 2 reputation integers bias
  `NeedsEngine`'s demand-mix.
- **Files:** wherever `DemandLine` expiry is handled (Wave 2's `NeedsEngine`/successor to
  `CommissionSystem.ExpireCommissions`), `GameState` (2 new reputation integers, trailing-init),
  `Drama/` (the `DemandRoutedToRival` event + chronicle line).
- **Approach:** KTD-E8.
- **Test scenarios:** an expired/declined line stamps the routing event exactly once; the chronicle
  surfaces a named Voss line (event-reference discipline, R14); reputation integers measurably bias
  `NeedsEngine`'s next posted shape-mix in a scripted before/after comparison.
- **Verification:** fast lane green.
- **Dependencies:** Wave 2 (and ideally Wave 4, for typed commissions — soft dependency, build against
  `DemandLine` either way).

## Verification Contract

| Gate | Command | Proves |
|---|---|---|
| Fast lane | `dotnet test sim/GameSim.Tests/GameSim.Tests.csproj --filter Category!=Balance` | U1-U6, U8 unit tests; PKD7 guard green throughout |
| Balance gate | `dotnet test sim/GameSim.Tests/GameSim.Tests.csproj --filter Category=Balance` | Floor-6 re-baseline (U7), re-fit once, narrated |
| PKD7 guard (standing, permanent) | `BondPurityTests` | Bond zeroed vs maxed → byte-identical expedition outcomes, EVERY PR in this wave |
| Engine lane | gdUnit4Net | U5's felt-moment surfaces, U7's ending-beat rendering |
| Manual smoke | Fresh campaign to floor 6 | Ending beat + Ledger-of-Legends epilogue render correctly |

## Definition of Done

- U1-U8 merged; fast lane green; Balance gate green (floor-6 re-baseline re-fit exactly once, narrated).
- `BondPurityTests` has been green on every single PR in this wave, not just the final one — the
  constitutional guarantee was never at risk mid-development, provably.
- Bond magnitudes are explicitly logged as provisional (Risk 3) pending re-measurement once Wave 2's
  diversified demand is default-on.
- The felt moments (bond glyph, shopping-pass staging, chronicle lines, the ending beat, the epilogue)
  are all visible in the Godot 3D client — sim tests alone do not satisfy this wave's Definition of Done.
- Master Voss turns every expired/declined demand line into a named story beat, verified by a
  scripted test asserting the chronicle event fires and names him.
- Legend Count and the Ledger-of-Legends epilogue are pure reads over existing data — no new stored
  aggregate field was added where a projection would do.
