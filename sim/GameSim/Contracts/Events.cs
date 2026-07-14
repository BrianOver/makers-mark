using System.Text.Json.Serialization;

namespace GameSim.Contracts;

/// <summary>
/// Everything that happened, as data. The Evening Ledger, gossip generator, and item
/// histories are all derived from these — gossip must reference a real <see cref="EventId"/> (R14).
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$event")]
[JsonDerivedType(typeof(ItemCrafted), "itemCrafted")]
[JsonDerivedType(typeof(ItemSold), "itemSold")]
[JsonDerivedType(typeof(HeroPassedOnItem), "heroPassed")]
[JsonDerivedType(typeof(PartyDeparted), "partyDeparted")]
[JsonDerivedType(typeof(PartyReturned), "partyReturned")]
[JsonDerivedType(typeof(AttributionBeatEvent), "attributionBeat")]
[JsonDerivedType(typeof(HeroDied), "heroDied")]
[JsonDerivedType(typeof(RecruitArrived), "recruitArrived")]
[JsonDerivedType(typeof(LootIncomeReceived), "lootIncome")]
[JsonDerivedType(typeof(OreOffered), "oreOffered")]
[JsonDerivedType(typeof(BountyPosted), "bountyPosted")]
[JsonDerivedType(typeof(BountyJudged), "bountyJudged")]
[JsonDerivedType(typeof(BountyPaid), "bountyPaid")]
[JsonDerivedType(typeof(GossipEmitted), "gossip")]
[JsonDerivedType(typeof(FloorRecordSet), "floorRecord")]
public abstract record GameEvent
{
    public EventId Id { get; init; }
    public int Day { get; init; }
}

public sealed record ItemCrafted(ItemId Item, QualityGrade Quality) : GameEvent;

public sealed record ItemSold(ItemId Item, HeroId Buyer, int Price, bool FromPlayerShop) : GameEvent;

/// <summary>A hero evaluated an item and passed — with the legible reason (R8/AE4).</summary>
public sealed record HeroPassedOnItem(HeroId Hero, ItemId Item, string Reason) : GameEvent;

public sealed record PartyDeparted(System.Collections.Immutable.ImmutableList<HeroId> Party, int TargetFloor) : GameEvent;

public sealed record PartyReturned(System.Collections.Immutable.ImmutableList<HeroId> Survivors) : GameEvent;

/// <summary>A proven item-attributable beat (R11/AE1/AE2). The spine of the game.</summary>
public sealed record AttributionBeatEvent(BeatType Beat, ItemId Item, HeroId Hero, int Floor, string Detail) : GameEvent;

/// <summary>Permadeath record naming the worn gear (R13/AE6).</summary>
public sealed record HeroDied(HeroId Hero, int Floor, string Cause, GearSet WornGear) : GameEvent;

public sealed record RecruitArrived(HeroId Hero) : GameEvent;

/// <summary>Expedition gold credited to a surviving hero at the Evening reveal (R12/R17).</summary>
public sealed record LootIncomeReceived(HeroId Hero, int Gold) : GameEvent;

/// <summary>A returning hero offers floor-scaled ore for sale (R6).</summary>
public sealed record OreOffered(HeroId From, string MaterialKey, int Quantity, int UnitPrice) : GameEvent;

public sealed record BountyPosted(BountyId Bounty, int TargetFloor, int RewardGold) : GameEvent;

/// <summary>A hero weighed a bounty — accepted or declined with a visible reason (R18/AE7).</summary>
public sealed record BountyJudged(BountyId Bounty, HeroId Hero, bool Accepted, string Reason) : GameEvent;

public sealed record BountyPaid(BountyId Bounty, HeroId To, int RewardGold) : GameEvent;

/// <summary>A templated tavern line — must cite the event it grew from (R14).</summary>
public sealed record GossipEmitted(EventId Source, string Line) : GameEvent;

/// <summary>A new personal deepest-floor record for the Depths Progress board (R15).</summary>
public sealed record FloorRecordSet(HeroId Hero, int Floor) : GameEvent;
