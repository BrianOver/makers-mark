---
title: "feat: staged expedition resolution — completion (verdict steps 2-6)"
date: 2026-07-17
artifact_contract: ce-unified-plan/v1
artifact_readiness: implementation-ready
origin: docs/design/expedition-tension-verdict.md (§5); step 1 merged as PR #31
execution: code — AI/NPC-lane Claude (U2-U5 bodies), orchestrator (U1 + registration-line review/merge), VISUALS lane (V5a prerequisite — see docs/plans/2026-07-17-003)
related: docs/plans/2026-07-17-003-feat-town-2p5d-migration-plan.md (V5a/V5b), docs/design/lane-operating-model.md (gates G1-G6)
---

## Goal Capsule

Implement architecture **B (staged resolution) + A's narrator graft** per the verdict in `docs/design/expedition-tension-verdict.md`: the day becomes 5 ticks (Morning → Expedition → **Camp** → **ExpeditionDeep** → Evening); expeditions resolve floors `[1..checkpoint]` at the Expedition tick, park an `InFlightExpedition`, open a real decision window at Camp (`SendSupply` / `Recall` / hold), and finalize `[checkpoint+1..target]` at ExpeditionDeep — stage-2 rolls are provably undrawn at decision time. Step 1 (the `ResolveFloors` seam, `sim/GameSim/Expedition/ExpeditionResolver.cs:76-173`) is already merged. This plan lands steps 2–6 as five units. Authority: the verdict doc; plan of record `docs/plans/2026-07-13-001`.

**Cross-lane hard gate (BOARD G2):** CI runs the engine-tests lane on EVERY PR (`.github/workflows/ci.yml:4-6,42` — no path filters), and `godot/tests` hard-codes 3-tick days (20 `AdvancePhase()` call sites: TownSceneTests 8 / MainUiTests 9 / SimAdapterTests 3; `TownSceneTests.cs:34-41` asserts `Day==2` after 3 ticks) plus `TownScene.OnPhaseCompleted`'s `case DayPhase.Evening: default:` snap-home (`TownScene.cs:131-139`). Therefore **U2 cannot merge until the VISUALS lane's V5a (5-phase tolerance, town plan `2026-07-17-003`) is on main.** U1 is engine-green without V5a: it changes no runtime phase sequence and no godot test enumerates `DayPhase` values (V5a's `AdvanceDay` helper is specified as loop-until-Morning for exactly this reason). U2's dependency row and verification contract below carry this gate.

---

## Grounding facts (verified against working tree)

- **RNG staging question — answered definitively: `InFlightExpedition` carries NO RNG snapshot.** `GameKernel.Tick` constructs the stream from `state.Rng` (`Kernel/GameKernel.cs:24`) and persists `rng.Snapshot()` back into `GameState` every tick (`GameKernel.cs:70`). RNG position is therefore *already* save-persisted between ticks, and `SaveLoadTests.SaveAtDayN_LoadAndContinue_EqualsUninterruptedRun` (`sim/GameSim.Tests/Kernel/SaveLoadTests.cs:25-52`) proves mid-stream save/load ≡ uninterrupted run. Stage 2 **continues on the live kernel stream** at the Deep tick. Putting an `RngState` inside `InFlightExpedition` would create a second authority over the one stream (KTD4 violation) — explicitly forbidden in the contract doc comment (U1). Draw contiguity also holds structurally: within the Expedition tick, `BountyJudgingSystem` runs before `ExpeditionSystem` (`GameComposition.cs:36-37`) and nothing draws after it; all resolver draws are contiguous inside the floor loop (`ExpeditionResolver.cs:162,222,231`); the Camp tick has no systems and `CampHandlers` (U4) draws nothing.
- **Draw-order consequence:** for a single-party day, staged draws are the *same sequence* as unstaged (floor loop draws don't depend on the stage boundary; nothing else draws in the empty Camp tick). Divergence comes only from **multi-party interleave** (was A₁A₂B₁B₂, becomes A₁B₁A₂B₂) — and 6 alive heroes = 2 parties every day (`Heroes/PartyFormation.cs:28`), so the balance re-fit in U3 is certain, per the verdict.
- **DayPhase is serialized as an int** (`Kernel/SaveCodec.cs:32-37` has no string-enum converter; `GameState.Phase`, `LoggedBatch.Phase`; corpus JSON writes `"Phase": 0`). Current values `Morning=0, Expedition=1, Evening=2` (`Contracts/Enums.cs:4-9`). Append-only is safe: appending `Camp=3, ExpeditionDeep=4` leaves every existing save's 0/1/2 meaning intact; **numeric order ≠ day order** — the day cycle is defined solely by `GameKernel.Advance` (`GameKernel.cs:81-87`). Grep of all `.cs` found **zero relational comparisons** on `DayPhase` — only equality/switch (full audit table in U2).
- **Stage-1 deaths cannot leak via the camp report.** In `ResolveFloors`, any death sets `floorCleared = false` (`ExpeditionResolver.cs:115`); the fighter loop finishes the floor, then the *floor* loop breaks on `!floorCleared` (`:152-154`) — so a party is parked in-flight **only when every stage-1 floor cleared and nobody died or is too hurt**. Parked ⇒ `Dead` empty ⇒ `PartyCampReport` never reveals a death early (KTD5 reveal timing preserved). The "postmortem window" failure mode (verdict kill-risk 2) manifests as *no window at all* (immediate finalize), which is what the checkpoint decision below minimizes.
- **Attribution needs zero changes:** `AttributionEngine.ComputeBeats(floors, party, items, venue)` replays from recorded data only (`ExpeditionResolver.cs:51`); it runs at finalize time over the merged floor list. A camp-delivered salve is an ordinary front-of-pack item drunk by `TryQuaff`'s first-Heal-in-pack-order scan (`ExpeditionResolver.cs:278-292`) — the `PotionLifesave` path is already end-to-end.
- **Reveal untouched:** `ExpeditionRevealSystem` consumes `PendingExpeditions` only (`Drama/ExpeditionRevealSystem.cs:39`); staged finalize just appends to that list one tick later. Pack depletion reconciles at `:156-173`.
- **Bounty refund path for Recall v1 verified:** an accepted-but-unreached bounty refunds via the expiry branch (`Bounties/BountySystems.cs:81-89`, pinned by `BountyRefundTests`) — "bank and surface" needs no new bounty mechanics.
- **Gold-conservation law is event-reconciled** (`Economy/GoldConservationTests.cs:120-122`: Δtown = −rivalSales −Σ`TariffApplied.Delta`). The supply-runner fee is a new sink and must ride a recorded event delta, KTD3-style.
- `VenueRegistry.Require(venueId)` exists (`Venues/VenueRegistry.cs:59`) — stage 2 rehydrates the venue from `InFlightExpedition.VenueId` with no registry changes.
- Mine floor count = 5 (`Expedition/MonsterTable.cs:19`); flee threshold = 25% MaxHp (`Expedition/CombatMath.cs:15`).

---

## Key decisions (locked here so PRs are mechanical)

### D1 — Checkpoint formula: **after floor 1**, single tuning const

Step-0 telemetry, recounted directly from the committed corpus this session (20 seeds × 100 days, `runs/batch-seed*.json`, `heroDied` events): deaths by floor 1/2/3/4 = **59 / 182 / 191 / 25** (total **457**). Cumulative: ≤1 = 12.9%, ≤2 = 52.7%, ≤3 = 94.5%. The verdict's `(target+1)/2` strawman puts the checkpoint at 2–3 for typical targets → 53–95% of deaths happen in stage 1 → the window never opens on the days that matter (kill-risk 2 criterion ">50% before checkpoint" is blown). Decision:

```csharp
// ExpeditionSystem — THE tuning knob (kill-risk 2). Step-0 histogram (20 seeds × 100 days,
// recounted from runs/ corpus 2026-07-17): deaths by floor 1/2/3/4 = 59/182/191/25 (n=457).
// 87.1% of deaths happen ABOVE floor 1, i.e. in stage 2, after the camp window. Deepening
// this to 2 puts the modal death floor (3, with 2 close behind) partly before the window;
// do that only if post-staging telemetry shows deaths migrating deeper. Depth-scaling
// (e.g. target-3 late game) is a v2 data-tuning PR.
internal const int CampCheckpointDepth = 1;
internal static int CheckpointFor(int targetFloor) => Math.Min(CampCheckpointDepth, targetFloor - 1);
```

`targetFloor == 1` → checkpoint 0 → **unstaged** (full `Resolve` at the Expedition tick, result parked in `PendingExpeditions` as today). Checkpoint can never equal target. Known cost: a floor-1 camp report carries thinner HP signal — accepted; the knob is one const and the post-U3 batch farm re-histograms.

### D2 — `BountyHandlers` phase whitelist

`phase != DayPhase.Expedition` (`Bounties/BountyHandlers.cs:12-13`) would silently legalize `PostBountyAction` during Camp/Deep. Change to an explicit whitelist preserving today's exact semantics: `action is PostBountyAction && phase is DayPhase.Morning or DayPhase.Evening`. Mid-expedition posting has no judging until next day anyway; conservative deny + test.

### D3 — All-phase handlers stay all-phase

`CraftingHandlers` (`Crafting/CraftingHandlers.cs:20-21`) and `ShopHandlers` (`Economy/ShopHandlers.cs:25-26`) accept every phase by design; under 5 phases that now includes Camp/Deep. **Keep** — crafting at the forge while the party is below is the verdict vignette's core rationing loop ("a shelved salve is a salve you can't send" requires unstock→send to be queueable in one Camp batch, which action-order application `GameKernel.cs:29-47` already supports). `ProfessionHandlers`/`OreMarketHandlers` unchanged.

### D4 — `ExpeditionResult` gains a trailing `Halt` field, with an explicit precedence rule

The verdict's cross-examination proved gate-retreat vs too-hurt endings are **indistinguishable** from `Floors` alone (no `FloorOutcome` is emitted for either). The narrator (U5), telemetry, and the camp-report path all need the cause. Since U1 is already a contracts micro-PR, add an APPEND-ONLY enum + trailing default param (exact `VenueId` precedent, `SaveCodec.cs:18-21` + `SaveLoadTests.PreP4Save…`): old saves deserialize to the default. Resolver populates it in U3.

**Precedence rule (normative, fixes the range-boundary misclassification):** the too-hurt exit is a `break` (`ExpeditionResolver.cs:166-169`) that fires **even when `floor == toFloor`** — i.e. a party that clears its target floor and only *then* trips the too-hurt check has fully succeeded. Halt classification therefore evaluates success first:

> `DeepestCleared == targetFloor` ⇒ `TargetReached`, **regardless of which exit path ended the loop.** Only when `DeepestCleared < targetFloor` do the exit-path mappings apply (wipe / gate / floor-lost / too-hurt / recalled).

Without this rule the narrator voices a limp-home closer over a complete success. Pinned by a dedicated U3 test.

### D5 — Baseline player holds at Camp

`BaselinePlayer` (`Harness/BaselinePlayer.cs`) gets explicit empty Camp/Deep arms — the balance gate keeps measuring the same policy. The kill-risk-1 A/B (send-when-hurt vs never-send) lives in a **test-local scripted policy** exactly like `SalveProvisioningBalanceTests.cs:77-99` already models (BaselinePlayer is "one policy, never forked" per its own header), landing with U4.

### D6 — `GameComposition.cs` procedure (unified with the lane operating model)

Registration order IS the determinism contract (`GameComposition.cs:15-18`). Unified mechanism (this supersedes both "orchestrator sign-off" and "orchestrator authors" phrasings): **the AI-NPC lane's unit PR may contain the single registration line its composed-kernel tests need, the PR description flags it under `Registration lines:`, and the orchestrator personally reviews that line and performs the merge — such PRs are never auto-merged.** Swarm/addon PRs never touch the file at all (line rides in the PR description; orchestrator applies). Recorded identically in `docs/design/lane-operating-model.md` §4/§7.

---

## U1 — Contracts micro-PR (**orchestrator-owned**, per CLAUDE.md deny-list)

**Branch:** `chore/contracts-staged-resolution`. **Files:** `sim/GameSim/Contracts/{Enums,Actions,Expedition,World,Events}.cs` + `sim/GameSim.Tests/Kernel/SaveLoadTests.cs`. All additive; nothing reads the new members yet; green trivially — including the engine lane (no runtime phase change; no godot test enumerates `DayPhase`). Merged **before** U2; in-flight agents rebase.

**`Enums.cs`** — append to `DayPhase` (and add the APPEND ONLY warning the enum currently lacks; update the "three phases" doc comment):

```csharp
public enum DayPhase
{
    Morning,
    Expedition,
    Evening,
    Camp,           // = 3 — decision window while the party camps below the checkpoint (staged resolution)
    ExpeditionDeep, // = 4 — stage-2 floors resolve; APPEND ONLY, values frozen in saves (KTD4).
}                   // Day ORDER is defined by GameKernel.Advance, never by numeric value.
```

New enum (D4):

```csharp
/// <summary>Why an expedition's floor progression stopped. APPEND ONLY — serialized in
/// ExpeditionResult (KTD4). TargetReached is the default old saves deserialize to.
/// Precedence: DeepestCleared == TargetFloor is ALWAYS TargetReached, whatever exit path
/// ended the loop (a too-hurt break after clearing the target is a success, not a limp).</summary>
public enum ExpeditionHalt
{
    TargetReached, // cleared through the target floor
    GateHeld,      // structural gate turned the party back (no roll)
    FloorLost,     // a flee or death left a floor uncleared
    PartyWiped,    // nobody left standing
    TooHurt,       // cleared the floor but too hurt to continue (short of target)
    Recalled,      // the player rang the recall bell at Camp (v1 bank-and-surface)
}
```

**`Actions.cs`** — two records + registrations on the `PlayerAction` polymorphic block (`Actions.cs:10-18`):

```csharp
[JsonDerivedType(typeof(SendSupplyAction), "sendSupply")]
[JsonDerivedType(typeof(RecallPartyAction), "recallParty")]
```

```csharp
/// <summary>Pay the camp runner to deliver ONE held consumable to a camped hero (Camp phase only).
/// The item goes to the FRONT of the hero's pack — the resolver quaffs front-first, so
/// your delivery drinks before anything the hero bought (P2 pack-order contract).</summary>
public sealed record SendSupplyAction(HeroId To, ItemId Item) : PlayerAction;

/// <summary>Ring the recall bell: the party containing <paramref name="Member"/> banks its
/// stage-1 clears/ore and surfaces at the Deep tick without rolling deeper floors (v1).</summary>
public sealed record RecallPartyAction(HeroId Member) : PlayerAction;
```

**`Expedition.cs`** — `InFlightExpedition` (fields map 1:1 onto the `ResolveFloors` parameters/locals, `ExpeditionResolver.cs:76-90`, converted to serializable immutable shapes) and the trailing `Halt` on `ExpeditionResult`:

```csharp
/// <summary>
/// A staged expedition parked between the Expedition tick (stage 1, floors [1..CheckpointFloor])
/// and the ExpeditionDeep tick (stage 2, floors [CheckpointFloor+1..TargetFloor]). Every field is
/// a serializable image of a ResolveFloors working local, so stage 2 resumes the loop verbatim.
/// Deliberately carries NO RngState: the kernel stream (GameState.Rng) is the single RNG
/// authority — it is snapshotted per tick by GameKernel, so stage-2 rolls are UNDRAWN while this
/// record exists, and mid-day save/load correctness is inherited from the kernel (KTD4).
/// v1 invariant: parked only when all stage-1 floors cleared with no deaths and nobody too hurt
/// (any other stage-1 ending finalizes immediately at the Expedition tick), so Dead is always
/// empty today — kept for the verbatim stage-2 call and for v2 rules that fight past deaths.
/// </summary>
public sealed record InFlightExpedition(
    ImmutableList<HeroId> Party,                                  // formation order (id-sorted)
    int TargetFloor,
    int CheckpointFloor,                                          // camp sits below this floor
    string VenueId,                                               // VenueRegistry key (P4)
    ImmutableSortedDictionary<int, int> Hp,                       // HeroId.Value -> hp after stage 1
    ImmutableSortedDictionary<int, ImmutableList<ItemId>> Packs,  // working packs, stage-1-depleted; camp deliveries front-insert here AND on Hero.Pack
    ImmutableSortedDictionary<int, int> Gold,                     // per-hero expedition gold so far
    ImmutableSortedSet<int> Dead,                                 // HeroId.Values dead in stage 1 (empty in v1 — see invariant)
    ImmutableList<FloorOutcome> Floors,                           // stage-1 outcomes (KTD6 record)
    ImmutableList<OreLoot> Loot,                                  // stage-1 ore
    int DeepestFloorCleared)                                      // stage-1 deepest (== CheckpointFloor under the v1 invariant)
{
    /// <summary>One delivery per party per day (Camp rule). Non-positional init member —
    /// absent in older JSON defaults false (CombatEvent.Uses pattern).</summary>
    public bool SupplySent { get; init; }

    /// <summary>Recall bell rung this Camp: the Deep tick banks and surfaces (v1).</summary>
    public bool Recalled { get; init; }
}
```

`ExpeditionResult` (`Expedition.cs:49-59`): append trailing `ExpeditionHalt Halt = ExpeditionHalt.TargetReached` after `VenueId` (exact `VenueId = "mine"` precedent — old saves lacking the property deserialize to the default; document in `SaveCodec` doc block as the P6 save-shape note).

**`World.cs`** — non-positional init member on `GameState` (`Hero.Pack` precedent, `Contracts/Heroes.cs:58`; `GameFactory` needs no change — positional ctor, `Kernel/GameFactory.cs:11-28`):

```csharp
/// <summary>Staged expeditions between the Expedition and ExpeditionDeep ticks (KTD5 staged).
/// Non-positional init member: pre-staging saves (no property) deserialize to empty.</summary>
public ImmutableList<InFlightExpedition> InFlight { get; init; } = ImmutableList<InFlightExpedition>.Empty;
```

**`Events.cs`** — three events + registrations:

```csharp
[JsonDerivedType(typeof(PartyCampReport), "partyCampReport")]
[JsonDerivedType(typeof(SupplyDelivered), "supplyDelivered")]
[JsonDerivedType(typeof(PartyRecalled), "partyRecalled")]
```

```csharp
/// <summary>The winch-house slate (staged resolution): a party camped below the checkpoint,
/// stage 2 unresolved. HpByHero/HealsLeftByHero are the decision facts (current hp; count of
/// Heal consumables left in the working pack). Never lists a dead hero — a stage-1 death
/// finalizes immediately and no report is emitted (KTD5: deaths reveal only at Evening).</summary>
public sealed record PartyCampReport(
    ImmutableList<HeroId> Party,
    int CampedBelowFloor,
    int TargetFloor,
    ImmutableSortedDictionary<int, int> HpByHero,
    ImmutableSortedDictionary<int, int> HealsLeftByHero) : GameEvent;

/// <summary>A camp delivery landed (front of pack). Fee is the runner's charge — a recorded
/// gold SINK the conservation invariant reconciles against (KTD3, TariffApplied precedent).</summary>
public sealed record SupplyDelivered(HeroId To, ItemId Item, int Fee) : GameEvent;

/// <summary>The recall bell: the party will bank and surface at the Deep tick (v1).</summary>
public sealed record PartyRecalled(ImmutableList<HeroId> Party) : GameEvent;
```

**Save round-trip pins** (added to `SaveLoadTests`, mirroring `PreP4Save`/`PreP5Save` regex-strip pattern at `SaveLoadTests.cs:55-95`):
1. `PreStagedSave_WithoutInFlight_LoadsEmpty` — strip `"InFlight":[]`, assert loads `Empty` (and, populated case, values survive byte-identical re-save).
2. `CampPhase_RoundTripsAsInt` — `state with { Phase = DayPhase.Camp }` round-trips byte-identical (pins int-3 serialization).
3. `PreP6Result_WithoutHalt_LoadsTargetReached` — strip the `Halt` property from a serialized `ExpeditionResult`, assert default.
4. `RoundTrip_PreservesCampActions` — tick a handler-less kernel with `SendSupplyAction`/`RecallPartyAction` (batch logs even when rejected, `GameKernel.cs:73`), round-trip, `Assert.IsType` both.
5. `RoundTrip_PreservesCampEvents` — state with the three new events in `EventLog` round-trips polymorphically.

**Test scenarios:** the five pins above; plus full-solution fast lane green with zero behavior change (nothing reads the members); all three CI lanes green (engine lane unaffected by construction).

---

## U2 — Kernel: the 5-phase day (**AI/NPC lane**)

**Branch:** `feat/u2-five-phase-kernel`. **Claim (`.claude/tasks/U2-five-phase-kernel.md`):** owns `sim/GameSim/Kernel/GameKernel.cs`, `sim/GameSim/Harness/BaselinePlayer.cs`, `sim/GameSim/Bounties/BountyHandlers.cs`, and the listed test files. **Depends:** U1 merged **AND V5a merged** (town plan `2026-07-17-003` — godot 5-phase tolerance; engine tests run on every PR and would otherwise go red on the 3-tick day assumptions and the `OnPhaseCompleted` default snap-home). Confirm gate G2 cleared on `.claude/tasks/BOARD.md` before cutting the branch.

**`GameKernel.Advance` (`GameKernel.cs:81-87`) becomes:**

```csharp
private static (int Day, DayPhase Phase) Advance(int day, DayPhase phase) => phase switch
{
    DayPhase.Morning        => (day, DayPhase.Expedition),
    DayPhase.Expedition     => (day, DayPhase.Camp),
    DayPhase.Camp           => (day, DayPhase.ExpeditionDeep),
    DayPhase.ExpeditionDeep => (day, DayPhase.Evening),
    DayPhase.Evening        => (day + 1, DayPhase.Morning),
    _ => throw new InvalidOperationException($"Unknown phase {phase}"),
};
```

At this point Camp/Deep are empty ticks: no registered systems, no draws → `Pcg32(state.Rng).Snapshot() == state.Rng` → **every seed's world is unchanged except tick counts and ActionLog length**. Balance bands hold (verdict step 3 claim, verified: the only per-tick state changes with no systems are log/day/phase fields).

**Full `DayPhase` comparison audit** (complete grep of `sim/` + `godot/` — the PR description must carry this table):

| Site | Verdict |
|---|---|
| `Kernel/GameKernel.cs:81-87` Advance | rewrite (above) |
| `Bounties/BountyHandlers.cs:13` `!= Expedition` | **change** to `is Morning or Evening` whitelist (D2) + rejection test for Camp |
| `Harness/BaselinePlayer.cs:23-68` 3-arm switch | add explicit empty `Camp`/`ExpeditionDeep` arms with D5 comment |
| `Kernel/GameFactory.cs:13`; system `Phase` properties (`BountySystems.cs:14,50`, `ExpeditionSystem.cs:16`, `RecruitSystem.cs:29`, `GossipSystem.cs:22`, `RivalRestockSystem.cs:23`, `FactionDriftSystem.cs:25`, `HeroShoppingSystem.cs:25`, `ExpeditionRevealSystem.cs:31`); `OreMarketHandlers.cs:35-36`; `Cli/Program.cs:142,247` | no change needed (equality on unchanged phases; CLI `day` loop terminates on Morning regardless of cycle length) |
| `Crafting/CraftingHandlers.cs:21`, `Economy/ShopHandlers.cs:26`, `Professions/ProfessionHandlers.cs:25` (all-phase) | keep, per D3 — note in PR |
| `godot/scripts/PhaseClock.cs:40-46`, `TownScene.cs:93,101-135,163,221-227,275`, `MainUi.cs:110-134` | **already handled by V5a** (hard dependency of this unit) — tolerance is on main before this PR opens; the real 5-phase ambience/choreography follows as V5b |

**Mechanical test updates (grep-verified complete list of `* 3` day-loop sites):**
- `Kernel/DeterminismTests.cs:31` `days * 3` → `days * 5` (`EmptyWorld` assert `Day == 4` still holds — 3 full days; `DeterminismTests.cs:69-72`)
- `Kernel/PhaseMachineTests.cs:14-27` — insert Camp + ExpeditionDeep assertions; day 2 Morning arrives after 5 ticks. `:52-53` positional ActionLog pins unchanged (ticks 1–2 are still Morning, Expedition)
- `Kernel/SaveLoadTests.cs:31-49` — 30/15+15 ticks → 50/25+25 (keeps the "10 days / 5+5" comments true)
- `Chronicle/ChronicleTests.cs:15` `days * 3` → `* 5`
- `Balance/BalanceSimTests.cs:49` `Days * 3` → `* 5`; `:139` TenDayRun 30 → 50 ticks
- `Balance/SalveProvisioningBalanceTests.cs:34` → `* 5` (its scripted switch needs no new arms — unmatched phases produce no actions)
- `Balance/FactionTariffBalanceTests.cs:102` → `* 5`
- `tools/Analytics`: audited — no phase/tick math (grep clean)
- `godot/tests`: **no edits needed** — V5a's `AdvanceDay` (loop-until-Morning) is day-length-agnostic by design

**New tests:** `EmptyCampAndDeepTicks_DrawNoRng` (Rng state byte-equal across the two new ticks on a composed kernel); `PostBounty_DuringCamp_IsRejected`.

**Verification:** fast lane green; **balance gate green with untouched band constants** (this is the proof the stream didn't move); **engine-tests CI lane green** (V5a tolerance carrying it); CLI smoke: `next` five times traverses the day.

---

## U3 — Staging: park + finalize (**AI/NPC lane**; the conscious divergence point)

**Branch:** `feat/u3-staged-expedition`. **Claim:** owns `sim/GameSim/Expedition/` + the one-line `GameComposition.cs` registration (procedure per D6: line in the PR, flagged in the description, orchestrator reviews the line and merges — never auto-merge), `sim/GameSim.Tests/Expedition/`, `Balance/`. **Depends:** U2.

**Resolver (`ExpeditionResolver.cs`) — minimal signature change for the early-termination cause:** `ResolveFloors` returns `(int DeepestCleared, ExpeditionHalt Halt)` instead of `int`. Halt derivation inside the existing loop (no draw changes), **applied only after the D4 precedence check** (`DeepestCleared == toFloor` when `toFloor` is the expedition target ⇒ `TargetReached`): `fighters.Count == 0` → `PartyWiped`; gate `break` (`:101`) → `GateHeld`; `!floorCleared` → `anyoneStanding ? FloorLost : PartyWiped`; too-hurt `break` (`:166-169`) short of target → `TooHurt`; loop completes → `TargetReached` (meaning "range complete"). Public `Resolve` stamps the halt into the result, mapping range-complete-at-target to `TargetReached` per D4. Two new public entries, both thin compositions over the seam:

```csharp
// Stage 1: init locals exactly as Resolve does (ExpeditionResolver.cs:31-39), run
// ResolveFloors(1..checkpoint). Halt != TargetReached => finalize now (attribution over
// stage-1 floors; ExpeditionResult with that Halt — D4 precedence does NOT apply at the
// checkpoint boundary: clearing the checkpoint is not clearing the target). Otherwise build
// InFlightExpedition from the locals (dicts -> ImmutableSorted*, packs -> ImmutableList).
public static (ExpeditionResult? Completed, InFlightExpedition? InFlight) ResolveStage1(
    ImmutableList<Hero> party, ImmutableSortedDictionary<int, Item> items,
    VenueDefinition venue, int targetFloor, int checkpointFloor, IDeterministicRng rng);

// Stage 2: rehydrate mutable working state from InFlight, run
// ResolveFloors(CheckpointFloor+1 .. TargetFloor) on the LIVE stream, merge floors/loot
// (InFlight.Floors.AddRange(stage2)), deepest = stage2 > 0 ? stage2 : InFlight.DeepestFloorCleared,
// attribution over the MERGED floors with the same venue (KTD6), Halt from stage 2 with the
// D4 precedence rule (deepest == TargetFloor => TargetReached even on a too-hurt exit).
// Recalled short-circuit: no loop, no draws — result from stage-1 state, Halt = Recalled.
public static ExpeditionResult ResolveStage2(
    InFlightExpedition inFlight, ImmutableList<Hero> party,
    ImmutableSortedDictionary<int, Item> items, VenueDefinition venue, IDeterministicRng rng);
```

**`ExpeditionSystem.Process` (`ExpeditionSystem.cs:20-49`):** per party, `checkpoint = CheckpointFor(targetFloor)` (D1). `checkpoint < 1` → existing `Resolve`, park in `PendingExpeditions` (unchanged path). Else `ResolveStage1`: Completed → `PendingExpeditions` (wipe/gate/too-hurt finalize immediately — no camp report); InFlight → `state.InFlight.Add(...)` + emit `PartyCampReport` (Hp from the working dict, HealsLeft = count of pack items whose `Item.Effect is { Kind: ConsumableKind.Heal }`). `PartyDeparted` emission unchanged.

**New `Expedition/ExpeditionDeepSystem.cs`** (`Phase => DayPhase.ExpeditionDeep`, `Name => "expedition-deep"`): for each `InFlight` in list order — fetch party heroes by stored id order from `state.Heroes` (safe: nothing mutates gear/MaxHp/Alive between the ticks; Camp touches `Pack` only, and the working pack lives in `InFlight.Packs`), `venue = VenueRegistry.Require(VenueId)`, `ResolveStage2`, append result to `PendingExpeditions`. Ends with `state with { InFlight = Empty }`. Emits nothing (the town learns at Evening; the narrator reads data).

**`GameComposition.BuildKernel`:** register `new ExpeditionDeepSystem()` between `ExpeditionSystem` and `ExpeditionRevealSystem` (day order; cross-phase order is documentation, phase filter does the work). D6 procedure applies.

**Test scenarios (`Expedition/StagedResolutionTests.cs` + updates):**
- **Differential parity (the keystone):** same party/items/venue/seed — `Resolve(target)` vs `ResolveStage1(checkpoint)`+`ResolveStage2` → serialized `ExpeditionResult`s byte-equal (single-party staging is draw-identical by construction; this pins it).
- **Halt precedence at the range boundary (D4):** scripted party that clears the target floor and then trips the too-hurt threshold → `Halt == TargetReached`, `DeepestCleared == target`.
- Halt paths: under-geared party → `GateHeld` result in `PendingExpeditions` at the Expedition tick, `InFlight` empty, no `PartyCampReport`; scripted wipe → `PartyWiped`, deaths still reveal only at Evening; too-hurt-at-checkpoint → `TooHurt` immediate finalize (v1 keeps old-behavior parity — a camp-refill rescue for too-hurt parties is a noted v2 tuning option, not built).
- Parked path: healthy party → `InFlight` populated with correct Hp/Packs/Gold/Floors/Loot, report facts match, `PendingExpeditions` gains the result only after the Deep tick, Evening reveal output equals the same day's pre-staging reveal shape.
- `target == 1` → unstaged; mixed multi-party day (one halts, one parks); `Recalled` flag honored by `ResolveStage2` (set manually — verbs arrive in U4): floors count == stage-1 count, `Halt == Recalled`, ore banked.
- Save/load between Camp and Deep mid-InFlight equals uninterrupted (composed-kernel version of `SaveAtDayN`).
- Composed-kernel 10-day run-twice byte-equal (existing `TenDayRun_IsDeterministic` now exercises staging).

**Balance re-fit protocol (mandatory, in-PR):** (1) run the full Balance suite + a 20-seed × 100-day batch (`GameSim.Cli batch`) on the branch and on main; (2) diff per-seed `FirstFloor3Day` / `FirstFloor5Day` / `minGold` / grin-rate; (3) re-fit `BalanceSimTests` band constants **consciously**, documenting the re-fit in the constants' comment block exactly as the day-8 re-fit precedent (`BalanceSimTests.cs:22-26`) and in the PR description; (4) tripwires per verdict kill-risk 3: grin-rate collapse, any seed insolvent, or >30% band shift → stop and re-examine `CampCheckpointDepth` before retuning.

**Post-merge broadcast:** BOARD.md line "U3 merged — VISUALS may start V5b (survivors-return moves to Deep-complete; the current Expedition-complete query would strand staged parties' sprites Away until Evening — visible bug, not crash, `TownScene.cs:118-130`)".

---

## U4 — Camp verbs (**AI/NPC lane**)

**Branch:** `feat/u4-camp-verbs`. **Claim:** owns `sim/GameSim/Expedition/CampHandlers.cs` (new), `GameComposition.cs` handler registration (D6 procedure), `sim/GameSim.Cli/Program.cs`, tests. **Depends:** U3.

**`CampHandlers : IActionHandler`** — `CanHandle: action is SendSupplyAction or RecallPartyAction && phase == DayPhase.Camp`. Draws no RNG (fee is a formula; insertion deterministic).

Fee (integer, tuning consts): `internal const int SupplyFeeBase = 6; internal const int SupplyFeePerFloor = 3;` → `Fee = SupplyFeeBase + SupplyFeePerFloor * inFlight.CheckpointFloor` (9g at the v1 floor-1 camp — deliberately priced just **above** the pinned 8g salve sale price, `SalveProvisioningBalanceTests.cs:19`, so sending always costs more than selling: the rationing tension; both consts are kill-risk-1 knobs).

**SendSupply validation order (each a distinct typed `RejectedAction` reason, evaluated in this fixed order):**
1. no `InFlight` party whose `Party` contains `To` → "no party is camped with H{n}"
2. `inFlight.Dead.Contains(To.Value)` → defensive (unreachable v1 invariant)
3. `inFlight.Recalled` → "the recall bell has rung — the runner won't chase them"
4. `inFlight.SupplySent` → "one runner per party per day"
5. item not in `state.Items` → unknown item
6. `item.Effect is null` → "the runner carries consumables only"
7. **ownership:** reject if `!item.PlayerCrafted`, or on `Player.Shelf` ("shelved — unstock it first"; unstock+send queue in one Camp batch works, `GameKernel` applies actions in order), or on `RivalShelf`, or in any hero's `Pack`
8. `Player.Gold < fee` → "can't pay the {fee}g runner"

**Apply:** `Player.Gold -= fee` (sink); `Hero.Pack = Pack.Insert(0, item)` (so the Evening `Pack.Remove(use.Item)` depletion at `ExpeditionRevealSystem.cs:156-173` reconciles, and an undrunk delivery stays with the hero); `InFlight.Packs[To]` front-insert (what stage 2 actually quaffs from); `SupplySent = true`; emit `SupplyDelivered(To, Item, Fee)`.

**Recall:** find party by `Member`; reject if none / already recalled; set `Recalled = true`; emit `PartyRecalled(Party)`. v1 = bank-and-surface (verdict: forfeit/resentment economics deliberately not promised); the unfulfilled bounty refunds via the verified expiry path.

**Economy note (KTD3):** the fee is a town-gold sink recorded on `SupplyDelivered.Fee`. Extend the conservation ledger comment (`GoldConservationTests.cs:12-24`) and add a focused camp-composition test: Δ(player+heroes) == −fee, event delta matches.

**CLI:** `send <heroId> <itemId>` / `recall <heroId>` commands + help text; camp slate rendering at the Camp prompt (read `state.InFlight` + latest `PartyCampReport`: per hero "Kess 18/22", heals-left, "N floors to target/bounty"); `Narrate` arms for `SupplyDelivered`/`PartyRecalled`.

**Test scenarios:**
- **Marquee end-to-end:** craft salve → party parks → `SendSupply` → stage 2 quaffs it (front-of-pack beats a rival salve already in the pack — pins `TryQuaff` order) → recorded `ConsumableUse` → `PotionLifesave` beat at Evening **with zero `AttributionEngine` edits** (assert no file diff there; it's the architecture's core claim).
- Every rejection reason above, one test each; one-per-party across two parties (each may receive one); Morning-phase send → kernel-level "no handler" rejection.
- Recall: deep floors never rolled (`Floors.Count` == stage-1), survivors surface with ore, bounty unfulfilled → escrow refunds at expiry (extends `BountyRefundTests`).
- Gold conservation focused test; determinism: same seed + same camp actions run twice byte-equal.
- **Kill-risk-1 A/B (Balance category, test-local policy per D5):** hold-policy vs send-when-`hp*100 < 40*MaxHp` policy over the seed sweep — assert sends produce ≥1 delivered-item `PotionLifesave` across the sweep and don't increase deaths; record the deltas in the test comment as the tuning baseline. If deltas are noise, retune fee/checkpoint consts **before** U5 builds presentation on a dead verb.

---

## U5 — Narrator graft (**dedicated agent, addon-style**; zero sim-state changes)

**Branch:** `feat/u5-expedition-narrator`. **Claim:** `U5-expedition-narrator.md` — owns `sim/GameSim/Narrative/` (new dir: `ExpeditionNarrator.cs`, `NarratorPack.cs`), `sim/GameSim.Cli/Program.cs` (drip wiring — **explicit carve-out from the AI-NPC lane's CLI grant for the life of this claim**, per `docs/design/lane-operating-model.md` §2), `sim/GameSim.Tests/Narrative/`. **Depends:** U4 (CLI file contention + camp slate interplay). Godot drip is VISUALS-lane later.

**`ExpeditionNarrator`** — pure static over recorded data (KTD6): input `(ImmutableList<FloorOutcome> slice, ImmutableList<AttributionBeat> beats, heroes, items, ExpeditionHalt halt, FlavorPack pack, ulong campaignSeed)`; output `ImmutableList<string>`. Per combat event renders kill/hurt/quaff/fled/died lines; floor headers; a halt closer disambiguated by the U1 `Halt` field (`GateHeld` vs `TooHurt` — the ambiguity undecidable from `Floors` alone; and thanks to the D4 precedence rule, a target-cleared run never voices a limp-home closer). Variant picks via the existing `FlavorEngine.Render` stable-hash contract (`Flavor/FlavorEngine.cs:60-78`) with a deterministic pseudo-event-id `(day, floorIndex, combatIndex)` mix — same save, same line, forever. Renderer receives heroes + items only (KTD7 convention).

**`NarratorPack`** — `FlavorPack.Create` data (`Flavor/FlavorPack.cs`): base keys `floorEnter, combatKill, combatHurt, combatQuaff, combatFled, combatDied, gateHeld, tooHurt, recallSurface, campReport`; slots `{hero} {monster} {item} {floor} {dmg} {hp}`; one fallback per base key; conformance test mirroring `TavernPackTests`/`LedgerPackTests` (fallbacks always validate).

**CLI drip:** after the Expedition tick — narrate each `InFlight.Floors` (stage-1 slice; **no beats exist yet** — attribution runs at finalize, so stage-1 beats surface at the Evening ledger as today, a documented v1 choice) then the camp slate. After the Deep tick — for each result: slice floors `> checkpoint` (the CLI's `Advance` already holds both `current` and `next` states, `Program.cs:230-253`; `current.InFlight` supplies each party's checkpoint) and interleave that result's beats at their proving floor — A's one great idea, at A's S-cost.

**Test scenarios:** narrator determinism (same inputs → identical lines, twice); slicing (stage-2 drip contains exactly floors > checkpoint); beat interleave position; halt closers per `ExpeditionHalt` value **including the target-cleared-then-too-hurt case rendering as success**; pack conformance; purity (no RNG interface in any signature — structural assert like FlavorEngine's); CLI smoke via scripted stdin.

---

## U6 — Godot adapter follow-through (**VISUALS lane** — executed as V5a/V5b of the town plan, NOT a separate unit here)

This plan does not own any `godot/` work. The adapter's staged-resolution follow-through is fully specified in `docs/plans/2026-07-17-003-feat-town-2p5d-migration-plan.md`:
- **V5a (5-phase tolerance)** — a **prerequisite** of U2 (BOARD gate G2), not a post-U3 cleanup: unknown-phase no-op in `OnPhaseCompleted`, loop-until-Morning `AdvanceDay` test helper, tolerance defaults.
- **V5b (real ambience + choreography)** — after U3 (and V4b): Camp/Deep tint rows, `PhaseClock.DurationOf` arms (Camp long — it's the decision window), and the survivors-return move from Expedition-complete to Deep-complete (`TownScene.cs:118-130`).

The AI-NPC lane's only obligation: the U3 post-merge BOARD broadcast above.

## Sequencing & lane assignment

| Unit | Verdict step | Lane | Branch | Depends on |
|---|---|---|---|---|
| U1 contracts | 2 | **Orchestrator** (deny-list) | `chore/contracts-staged-resolution` | — |
| V5a godot tolerance | — | VISUALS (town plan) | `feat/v5a-phase-tolerance` | — (land ASAP) |
| U2 kernel 5-phase | 3 | AI/NPC Claude | `feat/u2-five-phase-kernel` | U1 **+ V5a** |
| U3 staging | 4 | AI/NPC Claude | `feat/u3-staged-expedition` | U2 |
| U4 camp verbs | 5 | AI/NPC Claude | `feat/u4-camp-verbs` | U3 |
| U5 narrator | 6 | Dedicated agent (addon-style) | `feat/u5-expedition-narrator` | U4 |
| V5b godot ambience | — | VISUALS (town plan) | `feat/v5b-phase-ambience` | U3 (+ V4b) |

- Strictly serial U1→U4 (same directories / determinism-bearing files). One unit = one claim file in `.claude/tasks/` (README + operating-model §5 format) = one small PR; conventional commits; no `git add .`.
- **vs telemetry plan (`docs/plans/2026-07-17-001`) U4 decision-trace:** its `FloorTargetScored` emitter edits `Expedition/` — it starts only after **this plan's U4** merges (directory ownership; also lets its deliberate golden/band re-record happen once, after the U3 re-fit settles). Telemetry U1–U3/U5 are unaffected and may run in parallel. Staged-U5 (narrator) ∥ telemetry-U4 is fine (disjoint dirs; CLI contention is the only watch item — narrator claims `Program.cs` first).

## Verification contract / Definition of done

- Fast lane (`dotnet test sim/GameSim.Tests/GameSim.Tests.csproj --filter Category!=Balance`) green after every unit; **all three CI lanes** green on every PR (hard rule 1 — engine lane included, which is what makes V5a a hard gate for U2).
- Balance gate: green with **unchanged** bands after U2 (proof of stream preservation); consciously re-fit and documented in U3; kill-risk-1 A/B recorded in U4.
- Sim purity holds throughout: no Godot refs, no wall clock, integer math only, all RNG via the injected stream; `InFlightExpedition` carries no RNG state.
- Save compat: all five U1 pins green; every new member is trailing-optional/non-positional per the P4/P5 precedents; `SaveCodec` doc block gains the P6 note.
- Done = U1–U5 merged green, V5b confirmed scheduled on BOARD.md (VISUALS lane), verdict doc §5 annotated "implemented — see plan", memory/CLAUDE.md pointers updated by the orchestrator.
