---
title: "Wave 1 — Advisor v2: from ranked suggestions to derived plans"
date: 2026-07-27
type: feat
artifact_contract: ce-unified-plan/v1
artifact_readiness: implementation-ready
execution: code
source: docs/design/2026-07-27-five-pillars-design-synthesis.md (Pillar 1, Unified build sequence — Wave 1)
predecessors: docs/design/2026-07-27-how-you-play.md, docs/design/2026-07-27-gameplay-loop-analysis.md
---

# Wave 1 — Advisor v2

## Goal Capsule

- **Objective:** `sim/GameSim/Advisor/ObjectiveAdvisor.cs` stops actively misleading new players.
  Today it (a) forgets an accepted commission the instant it's accepted — `DemandBoard.OpenCommissions`
  filters `commission.Accepted` out, so nothing re-surfaces the promise until it either fulfills itself
  via `HeroShoppingSystem`/`CommissionHandlers.TryFulfillFromShelf` or expires; (b) never checks whether
  a suggested commission is actually completable before nudging the player to accept it (the measured
  ≥9 `CommissionExpired` / net +7g over 18 days finding, `docs/design/2026-07-27-gameplay-loop-analysis.md`
  §10 item 8); (c) never teaches the two hidden openers (`BuyOre`-as-gift, stale-listing haggle) that
  the "how you play" doc found are the answer to the #1 measured frustration ("I made the gear, the
  hero won't/can't buy it"); (d) nags about the same un-honored memorial every single call once Evening
  arrives, with no decay.
- **This wave is INDEPENDENT of Waves 2-5.** It ships alone, today, against the current flat
  weapon-first demand map. Its only forward-looking obligation is to key its gate on
  *recipe-matches-demand* rather than a hardcoded slot set, so Wave 2's demand diversification is a
  data change against this code, not a rewrite.
- **Stop conditions:** any change that would require a `sim/GameSim/Contracts/` edit (there is none
  planned — see KTD-A1); any change that makes a suggestion's reason string non-deterministic
  (violates KTD5); any suggestion that is not independently legal per `ActionLegality.IsLegal`.

## Scope Boundaries (what this wave does NOT do)

- Does **not** touch `Contracts/` — no `DemandLine`, no `HazardDefinition`, no new `PlayerAction` types.
  `Suggestion`'s extension is source-compatible (see KTD-A1).
- Does **not** change any handler's actual legality rules — `ObjectiveAdvisor` and `ActionLegality`
  stay pure *readers*; the fulfillability projection re-derives legality by calling the SAME
  `ActionLegality.IsLegal`/`MaterialVendorHandlers.QuoteCost`/`ProfessionRegistry` surfaces the handlers
  already use (KTD9's "deliberately replicates every guard" pattern), never a new source of truth.
- Does **not** diversify demand — `DemandBoard.OpenCommissions`/`DepthStalls` stay Weapon/Shield/Armor
  only until Wave 2 ships. The gate keys on *recipe-matches-demand* so that when Wave 2 adds
  `DemandLine.Craft`/`Shape`, this file needs a data-shape update, not new branching logic.
- Does **not** pick the hero to invest in, the forge modifier, the sale price, or a talent branch —
  "the brainless line" (synthesis §Pillar 1) stays the design law: describes when players could
  legitimately disagree, prescribes only when every informed player would do the same thing.
- Does **not** add a difficulty/economy mode — Guidance verbosity (Full/Signals/Off) is orthogonal to
  and independent of any future economic difficulty setting (that's Wave 3's "advisor verbosity IS the
  difficulty knob" territory; this wave only ships the three tiers as a presentation filter over the
  same suggestion list).

## Cross-pillar risks that apply here

- **Risk 1 (sequencing, synthesis §Cross-pillar risks):** Wave 2 ships demand diversity *raising*
  effective difficulty on the same 5 daily slots. This wave's fulfillability gate + decline copy is the
  precondition the synthesis calls out as needing to be "pre-landed" before Wave 2 flips its flag —
  ship this wave first, and design its gate keyed on recipe-matches-demand (not hardcoded slots) so
  Wave 2 doesn't need to touch this file's branching structure, only its data.

## Key Technical Decisions

- **KTD-A1 — `Suggestion` extension is source-compatible, no contract micro-PR.** `Suggestion` is
  defined and consumed entirely inside `Advisor/` (`ObjectiveAdvisor.cs`'s own
  `public sealed record Suggestion(PlayerAction? Action, string Reason);`) — it is not in
  `Contracts/`, so it is not deny-listed and not subject to the micro-PR rule. Extend it with
  **trailing init-only members with defaults** (the `Hero.MoodPermille`/`Item.SignedName` pattern
  already used throughout the sim for exactly this kind of additive, save-compat-shaped change):
  ```csharp
  public sealed record Suggestion(PlayerAction? Action, string Reason)
  {
      /// <summary>Null for a one-shot suggestion (unchanged shape). Non-null identifies which open
      /// plan this step belongs to — stable across calls as long as the plan's premise (an accepted
      /// commission, a depth stall) still holds, so the UI can render "step 2 of 3" without the
      /// advisor holding any memory of its own (still a pure per-call projection).</summary>
      public string? PlanKey { get; init; } = null;

      /// <summary>The steps recognized as already-done/still-open for this plan, re-derived every
      /// call from state (an accepted-unfulfilled commission IS an open plan; materials on hand ARE
      /// step-1-done) — never stored, never advanced by the advisor itself.</summary>
      public ImmutableList<PlanStep> Plan { get; init; } = ImmutableList<PlanStep>.Empty;
  }

  public sealed record PlanStep(string Description, bool Done);
  ```
  Every existing call site (`new Suggestion(action, reason)`) keeps compiling unchanged — this is
  purely additive.
- **KTD-A2 — plans are structurally recognized, never advisor-held state.** No new field on
  `GameState`/`PlayerState`/`Hero` — a "plan" is just: is there a `Commission` with `Accepted == true`
  for this hero? Do I hold/can-I-buy the material for its slot/quality? Is the item shelved? Each
  question is answered by re-reading `state` fresh on every `Suggest(state)` call, exactly like every
  other branch in this file today. This is what makes "the same state → byte-identical suggestions"
  hold without any new determinism surface to test.
- **KTD-A3 — fulfillability projection is a pure function over existing lookups**, added as a new
  private static method `FulfillabilityCheck(GameState, Commission, DayPhase)` (or per-slot equivalent
  for a not-yet-posted demand) returning `FulfillabilityVerdict(bool Fulfillable, string? ReasonCode)`
  with `ReasonCode` one of `"NO_RECIPE"`, `"LOCKED_TIER"`, `"CANT_AFFORD_MATERIAL"`, `"NO_TIME"`, or
  `null` when fulfillable. Steps, each reusing an existing surface:
  1. **Recipe path** — cheapest selected-profession recipe for the slot
     (`ProfessionRegistry.AllRecipes.Values.Where(r => r.Slot == slot && state.Player.IsSelected(r.Profession))`,
     the exact query `SuggestSlotCraftOrBuy`/`SuggestQualityUpgrade` already run) at **baseline quality
     only** — never assume the forge minigame's `PerformanceGrade` upside or a modifier roll; if no
     recipe exists for any selected profession → `NO_RECIPE`.
  2. **Tier-gate** — if the cheapest-viable recipe's tier's gate talent
     (`profession.TierGate.TryGetValue(recipe.Tier, out var gate)`) is not in
     `state.Player.TalentsFor(recipe.Profession)` → `LOCKED_TIER` (prepend a free
     `UnlockTalentAction` step to the plan instead of failing outright — unlocking is free and always
     legal once prerequisites hold, mirroring `SuggestQualityUpgrade`'s existing unlock-first branch).
  3. **Material buy** — gated on **`state.Player.Gold` right now**, via
     `MaterialVendorHandlers.QuoteCost(recipe.MaterialKey, needed)` — **never on projected income**
     (no "the hero will sell tomorrow" assumption; KTD5 purity — this is a snapshot check, not a
     forecast). Short of both current stock and current gold → `CANT_AFFORD_MATERIAL`.
  4. **Slot budget** — phase-walked: buys are Morning-only (`ActionLegality`'s existing
     `phase == DayPhase.Morning` gate on `BuyMaterialAction`), so if the commission's
     `DeadlineDay - state.Day` leaves zero remaining Mornings for a still-needed buy+craft+sell
     sequence → `NO_TIME`. (A simple count: at least 1 remaining Morning to buy, at least 1 remaining
     day of any phase to craft and let the shopping pass close the sale — this is intentionally coarse,
     not a full scheduler; over-restrictive is safe, since a false-`NO_TIME` only produces an extra
     Decline suggestion the player can override by accepting anyway.)
  5. **Sale path** — the target hero's **current** `Gold` (not projected) against the item's expected
     list price (`Math.Max(1, (stats.Attack + stats.Defense) * 2)`, the exact formula
     `ObjectiveAdvisor`/`BaselinePlayer`/`ActionLegality` all already use for a stockable candidate,
     since `CommissionHandlers.TryFulfillFromShelf` only requires the hero afford **list price**, the
     premium is opportunistic on top). If the hero can't afford list price now → not an automatic
     `CANT_AFFORD_MATERIAL`-class failure; instead splice in the broke-hero opener (item 3 below) as
     the plan's next step rather than declining, since that opener is a legal fix, not a dead end.
- **KTD-A4 — priority stack replaces the current five-tier ordering**, all within the same
  `Suggest(GameState state)` method body, same early-return shape as today:
  1. Memorial (Evening only) — unchanged from today, but nag-decay gated (KTD-A5).
  2. **NEW: open-plan next step** — scan `state.Commissions.Where(c => c.Accepted)` ordered by
     `DeadlineDay` ascending (nearest-deadline first); for the first one, run the SAME
     craft-now/buy-toward logic `SuggestSlotCraftOrBuy` already has (materials in stock → suggest
     `CraftAction`; else Morning + affordable → suggest `BuyMaterialAction`), tagged with
     `PlanKey = $"commission:{commission.Hero.Value}"`. This is today's biggest behavioral gap: an
     accepted commission currently drops out of every demand read (`DemandBoard.OpenCommissions`
     filters `!commission.Accepted`) the instant it's accepted, so the advisor has nothing pointing
     back at a promise already made. *A demand you already promised beats any new one* — this tier
     runs BEFORE tier 3's new-demand suggestions.
  3. Fulfillable new demand — the existing `demand.OpenCommissions[0]`/quality-stall/slot-stall logic,
     but **gated through `FulfillabilityCheck` first** (KTD-A3). Unfulfillable → do not suggest
     `AcceptCommissionAction` at all; instead suggest `DeclineCommissionAction` with the reason code
     spelled out in plain words (KTD-A6). Never silently skip to the next branch without saying why —
     that reproduces today's "advisor promotes Accept while never promoting the Buy it needs" bug
     (gameplay-loop-analysis §10 item 8) one layer down.
  4. Opportunistic — the two taught openers (KTD-A7) plus the existing fulfillment-match suggestion
     (`SuggestFulfillmentMatch`, unchanged) plus stale-listing haggle.
  5. Cheapest-productive fallback — unchanged (`CheapestProductivePath`/`CheapestTier1Recipe`), plus
     the trailing "stock it" suggestion, both unchanged in position (last).
- **KTD-A5 — nag decay, deterministic and memoryless.** Replace the current
  `state.Drama.Memorials.FirstOrDefault(m => !m.Honored)` unconditional-every-call branch with:
  ```csharp
  var age = state.Day - memorial.Day - 1;
  var show = age >= 0 && age % 3 == 0;
  ```
  computed fresh from `state` every call — no new field, no advisor memory. When multiple memorials
  are un-honored, the FIFO-oldest one is the one the cadence check runs against; the rest are folded
  into the same suggestion's copy as a trailing clause (e.g., "...and 2 others wait too") rather than
  each getting their own cadence slot.
- **KTD-A6 — the fulfillability gate's decline suggestion, exact copy shape:**
  `"Decline {heroName}'s commission — a promise that expires hurts more than a polite no ({reasonCode
  in plain words})."` — reason codes translate to fixed strings (`NO_RECIPE` → "nobody in your
  professions can make that yet", `LOCKED_TIER` → "the recipe's tier isn't unlocked yet",
  `CANT_AFFORD_MATERIAL` → "you can't afford the material even after selling everything on the shelf",
  `NO_TIME` → "the deadline is too close to buy, craft, and sell in time"). The `DeclineCommissionAction`
  suggested is independently re-checked through `ActionLegality.IsLegal` before being returned, per the
  class's existing contract.
- **KTD-A7 — teach the two hidden openers**, each a new opportunistic-tier suggestion:
  - **Broke-hero → `BuyOreAction` gift.** Trigger: the fulfillment-match / sale-path check (KTD-A3
    step 5) finds a target hero whose `Gold` falls short of a shelved/craftable item's list price, AND
    that hero has an open `OreOffered` in `state.OpenOreOffers` (the existing null purse-mismatch
    dead-end `SuggestFulfillmentMatch` already detects and reports as pure information — this reuses
    that same detection point as the trigger, then adds the actionable next step it was missing).
    Copy: `"{heroName} can't quite afford the {slot} yet — buy {quantity} of her {materialKey} ({cost}g).
    You bank ore you'd buy anyway, and the {slot} comes within her reach."`
  - **Stale listing → haggle.** Trigger: a shelved item's age (days since its `StockAction`/last
    `SetPriceAction` — derive from the `EventLog`'s most recent `ItemSold`-adjacent stock event for
    that `ItemId`, per KTD-A8) exceeds a fixed threshold AND a `PassReasonRollup` names a price-shaped
    reason for that slot. Suggests the two-step `OpenCounterAction` → (next call) `PresentItemAction`
    plan, `PlanKey = $"haggle:{itemId}"`. The advisor **never emits the haggle number itself** — no
    `HaggleResponseAction` suggestion ever appears; that number is the player's judgment call (the
    brainless-line boundary, KTD-A_brainless below).
- **KTD-A8 — stale-listing age**, since no field currently timestamps a `Stock`/reprice event: scan
  `state.EventLog` backward for the most recent event touching this `ItemId` (there is no dedicated
  "Stocked" event today — check `Contracts/Events.cs` for the nearest existing stamped event touching
  an `ItemId`, e.g. `ItemSold`/creation-adjacent events, and reuse it; if genuinely nothing stamps a
  shelf event, add ONE new event type `ItemShelved(ItemId, int Price, int Day)` via the standard
  contract micro-PR process — this is the one piece of this wave that may need a `Contracts/Events.cs`
  append, flag it to the orchestrator before starting if `CraftingHandlers`/`ShopHandlers` don't already
  stamp something suitable). Age = `state.Day - shelvedDay`.
- **KTD-A_brainless — the exact line, verbatim, as a doc-comment on `ObjectiveAdvisor`:** *"If two
  skilled players could legitimately choose differently, the advisor describes; if every informed
  player would do the same thing, it prescribes."* Enforced by review, not a test: audit every new
  suggestion added by this wave against it before merging (the haggle number, the forge modifier, which
  hero to prioritize among several equally-fulfillable commissions, and the sale price are all
  descriptive-only; the fulfillability verdict and the two openers' mechanics are prescriptive because
  they are mechanically singular).
- **KTD-A9 — Guidance verbosity tiers**, a pure presentation filter, NOT a new suggestion source: add
  `public enum GuidanceLevel { Full, Signals, Off }` in `Advisor/` (not `Contracts/` — this is a
  player-preference setting the Godot adapter reads/writes, analogous to how `Counter`/`Rent` live as
  save-compat `GameState` init members today, OR simpler: store it as a Godot-side-only preference if
  it never needs to survive a headless CLI session — decide at implementation time based on whether the
  CLI harness needs it; default to Godot-only unless a CLI use case emerges, to avoid an unnecessary
  `GameState` field). `Full` = today's full suggestion list with plan steps. `Signals` = only the
  reason strings / demand telegraph (no concrete `PlayerAction` proposed — mirrors `DemandBoard`'s
  existing read-only surfacing). `Off` = nothing. All three tiers read the SAME
  `ObjectiveAdvisor.Suggest(state)` output; the tiering is a slice/map applied by the caller (CLI/Godot
  adapter), not new logic inside `ObjectiveAdvisor` itself — keeps the sim-purity boundary intact (no
  Godot references in `sim/GameSim/`). Ship; default new saves to `Full`.
- **KTD-A10 — diversification-ready keying.** Every gate in this file that currently branches on
  `ItemSlot.Weapon/Shield/Armor` (the `SubParSlot` scan order, `FindGapSlot`-mirroring logic) is
  **left exactly as is** for this wave (Wave 2 hasn't shipped `DemandLine`/`CraftDiscipline` yet) —
  but the new `FulfillabilityCheck` and priority-stack code MUST be written against
  `ProfessionRegistry.AllRecipes.Values.Where(r => r.Slot == slot && ...)`, i.e. "does a recipe exist
  matching this demand's slot" rather than an inline `slot is ItemSlot.Weapon or ItemSlot.Shield or
  ItemSlot.Armor` literal check anywhere. When Wave 2 lands `DemandLine.Craft`/`Shape`, the query
  becomes "does a recipe exist matching this demand's Craft+Shape" — a data-shape change to the same
  query shape, not new branches.
- **New "repeat-demand" plan shape, stubbed for Wave 2:** add the `PlanStep`/`PlanKey` shape (KTD-A1)
  generic enough to represent "craft 2 antidotes — they burn per raid" even though no consumable
  demand exists yet — do not hardcode it to single-item plans. Concretely: `Plan` is an
  `ImmutableList<PlanStep>`, so a repeat demand is just N steps with the same `Description` — no
  special-casing needed now, just don't design `PlanStep` as inherently singular.

## Implementation Units

### U1. `Suggestion`/`PlanStep` extension + fulfillability projection

- **Goal:** The pure data shapes and the `FulfillabilityCheck` function exist and are independently
  testable before any priority-stack rewiring touches them.
- **Files:** `sim/GameSim/Advisor/ObjectiveAdvisor.cs` (only file touched; add new private
  static methods and the two new record types in the same file, matching the existing single-file
  organization).
- **Approach:** Implement KTD-A1 (`Suggestion`/`PlanStep`) and KTD-A3 (`FulfillabilityCheck` +
  `FulfillabilityVerdict`) exactly as specified above. Do not wire them into `Suggest()` yet — this
  unit is additive-only so it can be tested in isolation first.
- **Test scenarios** (new file `sim/GameSim.Tests/Advisor/FulfillabilityCheckTests.cs`, or add to
  existing `ObjectiveAdvisorTests.cs`):
  - Recipe exists, tier unlocked, material in stock, hero affords list price → `Fulfillable: true`.
  - No recipe for any selected profession matches the slot → `NO_RECIPE`.
  - Recipe exists but tier's gate talent not unlocked → `LOCKED_TIER`, plan includes the free
    `UnlockTalentAction` step.
  - Material short, current gold (not projected) can't cover `QuoteCost` → `CANT_AFFORD_MATERIAL`.
  - Deadline leaves zero remaining Mornings → `NO_TIME`.
  - Same `GameState` input twice → byte-identical `FulfillabilityVerdict` (determinism smoke check).
- **Verification:** `dotnet test sim/GameSim.Tests/GameSim.Tests.csproj --filter Category!=Balance` green.
- **Dependencies:** none.

### U2. Priority-stack rewrite (open-plan tracking + fulfillability gate)

- **Goal:** An accepted commission is answered before any new demand; an unfulfillable commission is
  never suggested for accept — it's suggested for decline, with a reason.
- **Files:** `sim/GameSim/Advisor/ObjectiveAdvisor.cs`.
- **Approach:** Implement KTD-A4's five-tier stack. Reuse `SuggestSlotCraftOrBuy`'s craft-now/buy-toward
  branching for tier 2 (open-plan next step) rather than duplicating it — factor it to accept either a
  `DepthStallEntry` (today's caller) or an accepted `Commission`'s slot/quality (the new caller), since
  both ultimately need the same "craft it now or buy toward it" answer. Wire the fulfillability gate
  (U1) into the existing tier-3 `AcceptCommissionAction` suggestion at `demand.OpenCommissions[0]`.
- **Test scenarios** (extend `sim/GameSim.Tests/Advisor/ObjectiveAdvisorTests.cs`, golden-scenario
  style — assert the ENTIRE suggestion list, reason strings verbatim):
  - Accepted commission near deadline + a new open commission both live → the accepted one's
    craft/buy step is suggested first, tagged with its `PlanKey`.
  - Two accepted commissions, different deadlines → nearest-deadline one answered first.
  - An open commission whose recipe is locked → `Suggest` returns a `DeclineCommissionAction`
    suggestion with the `LOCKED_TIER` copy, never an `AcceptCommissionAction`.
  - Regression: every existing `ObjectiveAdvisorTests` scenario from before this wave still passes
    verbatim, OR is deliberately updated with a narrated reason in the test's own comment (mirrors the
    `AtomicEquivalenceTests` re-pin-note precedent) — this is a Godot-adapter-visible reason-string
    change, so treat every text diff as deliberate, never incidental.
- **Verification:** fast lane green; every suggested action independently legal via `ActionLegality.IsLegal`.
- **Dependencies:** U1.

### U3. Nag decay + the two taught openers

- **Goal:** The memorial nag decays on a fixed cadence; `BuyOre`-as-gift and stale-listing-haggle are
  discoverable through the advisor, not just by an intelligent player's luck.
- **Files:** `sim/GameSim/Advisor/ObjectiveAdvisor.cs`. If KTD-A8 requires it, a one-line
  `Contracts/Events.cs` append (`ItemShelved`) — flag to the orchestrator BEFORE starting if needed;
  this is the only possible contract touch in this wave and should land as its own dedicated micro-PR
  per the standing rule, merged ahead of the rest of this wave's PR.
- **Approach:** KTD-A5 (nag decay), KTD-A7 (both openers), KTD-A8 (stale-listing age source).
- **Test scenarios:**
  - Memorial cadence table: for `age` in `0..8`, assert `show` is exactly `true` at `0, 3, 6` and
    `false` elsewhere (`age % 3 == 0`).
  - Multiple un-honored memorials → oldest FIFO shown as the cadence-gated suggestion; the rest appear
    only as trailing copy, never their own suggestion entry.
  - Broke-hero + open `OreOffered` for that hero → `BuyOreAction` gift suggestion appears with the
    exact copy shape (KTD-A7), and it is NOT suggested when no `OreOffered` exists for that hero (falls
    back to plain informational purse-mismatch, today's behavior).
  - Stale shelf item (age above threshold) + a price-shaped `PassReasonRollup` → `OpenCounterAction`
    suggested; NO suggestion ever proposes a `HaggleResponseAction` with a concrete price (brainless-
    line boundary, asserted by grepping the test's own suggestion list for the absence of that action
    type — a structural, not just behavioral, assertion).
- **Verification:** fast lane green.
- **Dependencies:** U1 (for `PlanKey` on the haggle plan), U2 (ordering must be stable).

### U4. Guidance verbosity tiers

- **Goal:** Full/Signals/Off exist as a presentation filter; default new saves to Full.
- **Files:** `sim/GameSim/Advisor/ObjectiveAdvisor.cs` (the `GuidanceLevel` enum + a pure
  `Filter(ImmutableList<Suggestion>, GuidanceLevel)` helper); Godot-side wiring is presentation-only and
  out of this sim-focused plan's file list (note it as a follow-up task for whichever Godot adapter unit
  picks this up — not blocking Wave 1's sim-side completion).
- **Approach:** KTD-A9.
- **Test scenarios:**
  - `Full` returns the suggestion list unchanged.
  - `Signals` strips `Action` to null on every entry but preserves `Reason` (so the caller can render
    telegraph text with no actionable button).
  - `Off` returns an empty list.
- **Verification:** fast lane green.
- **Dependencies:** U2.

### U5. Test suite: golden scenarios, memorial cadence, property sweep, `AdvisorFollower` Balance policy

- **Goal:** The measured failure this wave fixes (blind advisor-following causing net +7g/18 days with
  ≥9 expiries) is pinned as a regression gate that would have caught it.
- **Files:** `sim/GameSim.Tests/Advisor/ObjectiveAdvisorTests.cs` (golden suite, memorial cadence table —
  both may already partially exist; extend, don't duplicate), a new property test (same file or a
  sibling `ObjectiveAdvisorPropertyTests.cs`), and a new **test-local** scripted policy inside
  `sim/GameSim.Tests/Balance/` (per the `BaselinePlayer.cs` doc comment's own precedent: "the... A/B
  lives in a test-local scripted policy... never [in Harness], BaselinePlayer is never forked" — do
  NOT add this to `sim/GameSim/Harness/`).
- **Approach:**
  - **Golden scenario suite:** hand-authored `GameState` fixtures (mirroring `DramaFixtures.cs`'s
    existing fixture-building pattern) asserting the FULL suggestion list including reason strings
    verbatim — safe because every interpolation is integer/enum-derived (gold amounts, quality grades,
    hero names from fixtures), never float, so cross-OS stability holds (KTD4 spirit).
  - **Memorial cadence table:** parametrized `[Theory]` over `age = 0..8` per U3.
  - **Property test over a 20-seed sweep:** for each seed, run N days of `BaselinePlayer.ActionsFor`
    plus every suggestion `ObjectiveAdvisor.Suggest` currently proposes (or a null-action informational
    one, skip), asserting: (1) every non-null suggested `Action` is legal via `ActionLegality.IsLegal`
    at the moment it was suggested; (2) no `PlanKey` recurs in a LATER suggestion list after its premise
    has died (the commission expired/fulfilled, the memorial honored) — walk the day-by-day suggestion
    stream and assert a dead `PlanKey` never reappears; (3) same `GameState` fed twice → byte-identical
    suggestion list (a direct call-twice check, not just an inference from purity).
  - **`AdvisorFollower` Balance policy:** a new test-local static class inside
    `sim/GameSim.Tests/Balance/` (e.g. `Balance/AdvisorFollowerPolicy.cs` or inlined in a new
    `AdvisorFollowerBalanceTests.cs`) that, each phase, takes the FIRST non-null suggested `Action` from
    `ObjectiveAdvisor.Suggest(state)` and submits exactly that (falling back to a no-op/empty action list
    when every suggestion is informational) — this is deliberately the "trusting player" the
    gameplay-loop-analysis §10 finding describes. Run it over the same `Days`/seed shape as
    `BalanceSimTests.Run` (reuse `GameComposition.BuildKernel`/`NewCampaign`). Assert: **≥95% of
    commissions this policy accepts fulfill before their deadline** (count `CommissionFulfilled` events
    against `CommissionExpired` events restricted to commissions this policy actually accepted — the
    direct regression pin for the fixed bug). Mark `[Trait("Category", "Balance")]`.
- **Verification:** `dotnet test sim/GameSim.Tests/GameSim.Tests.csproj --filter Category!=Balance` green
  for the golden/cadence/property tests; `dotnet test sim/GameSim.Tests/GameSim.Tests.csproj --filter
  Category=Balance` green including the new `AdvisorFollower` ≥95% assertion.
- **Dependencies:** U1-U4.

## Verification Contract

| Gate | Command | Proves |
|---|---|---|
| Fast lane | `dotnet test sim/GameSim.Tests/GameSim.Tests.csproj --filter Category!=Balance` | U1-U5 unit tests, golden suite, memorial cadence, property sweep |
| Balance gate | `dotnet test sim/GameSim.Tests/GameSim.Tests.csproj --filter Category=Balance` | `AdvisorFollower` ≥95% commission-fulfillment-before-deadline; existing `BalanceSimTests` bands unmoved (this wave adds zero RNG draws — see below) |
| Determinism smoke | Same `GameState` fed twice to `ObjectiveAdvisor.Suggest` | Byte-identical suggestion list (U1/U5) |
| Legality tripwire | Every suggested `Action` re-checked via `ActionLegality.IsLegal` | No suggestion the kernel would reject (unchanged existing contract, re-asserted) |

**Golden-safety class:** this wave is **Class 0** in the Phase-B taxonomy sense (see
`docs/plans/2026-07-25-002-feat-phaseB-living-heroes.md` §KTD-B0 for the definitions this plan borrows) —
`ObjectiveAdvisor` draws no RNG, mutates no `GameState`, and is called by nothing that feeds back into
the kernel (the CLI/Godot adapters read its output and submit ordinary `PlayerAction`s the player/AI
already could submit). No idle-hash re-pin is expected; if one occurs, treat it as a signal something in
this wave accidentally touched simulated behavior and stop to investigate before merging.

## Definition of Done

- U1-U5 merged; fast lane green; Balance gate green including the new `AdvisorFollower` assertion.
- Every new suggestion type (open-plan step, decline-with-reason, `BuyOre` gift, haggle opener) has at
  least one golden-scenario test pinning its exact reason string.
- `docs/design/2026-07-27-five-pillars-design-synthesis.md`'s Pillar 1 spec is fully covered: priority
  stack, fulfillability gate, taught openers, nag decay, brainless-line boundary, Guidance tiers,
  diversification-ready keying.
- No `Contracts/` edit landed outside a dedicated, orchestrator-authored micro-PR (KTD-A8's possible
  `ItemShelved` event, if needed at all).
- Nothing in `godot/` required to change for this wave's Definition of Done — Guidance-tier Godot wiring
  (U4's adapter half) is called out as a follow-up, not a blocker, since the sim-side contract (the
  `Filter` helper) is what this plan owns.
