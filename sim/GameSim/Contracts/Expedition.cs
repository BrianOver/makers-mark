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
    string VenueId = "mine");

/// <summary>A bounty on the board (R18).</summary>
public sealed record Bounty(BountyId Id, int TargetFloor, int RewardGold, int PostedOnDay, HeroId? AcceptedBy, bool Paid);
