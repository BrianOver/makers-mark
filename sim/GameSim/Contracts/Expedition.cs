using System.Collections.Immutable;

namespace GameSim.Contracts;

/// <summary>
/// One consumable quaffed during a fight (P2), recorded for attribution (KTD6):
/// <see cref="Round"/> is the 1-based round within the hero's fight the drink preceded
/// (a value past the fight's last round marks the post-floor "too hurt to continue"
/// quaff, which heals AFTER the fight's damage). Hp values are the actual before/after
/// (heal capped at MaxHp), so attribution replays from data alone — never re-draws.
/// </summary>
public sealed record ConsumableUse(ItemId Item, int Round, int HpBefore, int HpAfter);

/// <summary>
/// One combat exchange inside an expedition, with its resolved rolls recorded (KTD6).
/// Counterfactual attribution recomputes over <see cref="RecordedRolls"/> — it never draws RNG.
/// <see cref="Uses"/> holds any consumables quaffed this round (P2); non-positional init
/// member so old saves and existing constructors default to empty.
/// </summary>
public sealed record CombatEvent(
    int Floor,
    HeroId Hero,
    string MonsterKind,
    ImmutableList<int> RecordedRolls,
    int DamageDealt,
    int DamageTaken,
    bool MonsterKilled,
    ItemId? KillingItem)
{
    public ImmutableList<ConsumableUse> Uses { get; init; } = ImmutableList<ConsumableUse>.Empty;
}

/// <summary>Outcome for one floor attempt within an expedition.</summary>
public sealed record FloorOutcome(int Floor, bool Cleared, ImmutableList<CombatEvent> Combats);

/// <summary>An attribution beat proven by the resolver (R11), pre-event-log form.</summary>
public sealed record AttributionBeat(BeatType Beat, ItemId Item, HeroId Hero, int Floor, string Detail);

/// <summary>Ore looted by a hero on an expedition, priced at Evening (R6).</summary>
public sealed record OreLoot(HeroId Hero, string MaterialKey, int Quantity);

/// <summary>
/// The pure-function output of an expedition (KTD5): computed at departure, revealed on return.
/// Everything the Evening reveal needs is in here — no other source of truth exists.
/// <see cref="VenueId"/> is the <c>VenueRegistry</c> key of the venue raided (P4); it is TRAILING
/// with a Mine default so the reveal/records are venue-aware and old saves (no venue in the JSON)
/// deserialize to the Mine — a byte-identical round-trip while the Mine is the only live venue.
/// <see cref="Halt"/> is TRAILING with the TargetReached default on the same precedent (P6
/// save-shape): pre-staging saves lacking the property deserialize to the old implicit meaning.
/// </summary>
public sealed record ExpeditionResult(
    ImmutableList<HeroId> Party,
    int TargetFloor,
    int DeepestFloorCleared,
    ImmutableList<FloorOutcome> Floors,
    ImmutableList<HeroId> Survivors,
    ImmutableList<HeroId> Deaths,
    ImmutableList<AttributionBeat> Beats,
    ImmutableList<OreLoot> Loot,
    ImmutableSortedDictionary<int, int> GoldEarnedByHero,
    string VenueId = "mine",
    ExpeditionHalt Halt = ExpeditionHalt.TargetReached);

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

/// <summary>A bounty on the board (R18).</summary>
public sealed record Bounty(BountyId Id, int TargetFloor, int RewardGold, int PostedOnDay, HeroId? AcceptedBy, bool Paid);
