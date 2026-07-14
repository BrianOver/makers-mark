using System.Collections.Immutable;

namespace GameSim.Contracts;

/// <summary>
/// One combat exchange inside an expedition, with its resolved rolls recorded (KTD6).
/// Counterfactual attribution recomputes over <see cref="RecordedRolls"/> — it never draws RNG.
/// </summary>
public sealed record CombatEvent(
    int Floor,
    HeroId Hero,
    string MonsterKind,
    ImmutableList<int> RecordedRolls,
    int DamageDealt,
    int DamageTaken,
    bool MonsterKilled,
    ItemId? KillingItem);

/// <summary>Outcome for one floor attempt within an expedition.</summary>
public sealed record FloorOutcome(int Floor, bool Cleared, ImmutableList<CombatEvent> Combats);

/// <summary>An attribution beat proven by the resolver (R11), pre-event-log form.</summary>
public sealed record AttributionBeat(BeatType Beat, ItemId Item, HeroId Hero, int Floor, string Detail);

/// <summary>Ore looted by a hero on an expedition, priced at Evening (R6).</summary>
public sealed record OreLoot(HeroId Hero, string MaterialKey, int Quantity);

/// <summary>
/// The pure-function output of an expedition (KTD5): computed at departure, revealed on return.
/// Everything the Evening reveal needs is in here — no other source of truth exists.
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
    ImmutableSortedDictionary<int, int> GoldEarnedByHero);

/// <summary>A bounty on the board (R18).</summary>
public sealed record Bounty(BountyId Id, int TargetFloor, int RewardGold, int PostedOnDay, HeroId? AcceptedBy, bool Paid);
