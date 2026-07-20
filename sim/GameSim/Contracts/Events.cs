using System.Collections.Immutable;
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
[JsonDerivedType(typeof(TariffApplied), "tariffApplied")]
[JsonDerivedType(typeof(MaterialPurchased), "materialPurchased")]
[JsonDerivedType(typeof(RecoveryStipendGranted), "recoveryStipend")]
[JsonDerivedType(typeof(FactionStandingShifted), "factionStandingShifted")]
[JsonDerivedType(typeof(PartyCampReport), "partyCampReport")]
[JsonDerivedType(typeof(SupplyDelivered), "supplyDelivered")]
[JsonDerivedType(typeof(PartyRecalled), "partyRecalled")]
[JsonDerivedType(typeof(PartiesFormed), "partiesFormed")]
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

/// <summary>
/// A faction ore tariff moved the price the player paid away from the base ask (P5 U3, R7/R8/KTD3).
/// The hero always receives <paramref name="BaseLineCost"/> (the base ask); the player pays
/// <paramref name="PlayerCost"/>; <paramref name="Delta"/> = <c>PlayerCost − BaseLineCost</c> is the
/// signed faction sink/source — positive burns gold from the town total (surcharge sink), negative
/// mints it (discount source, the only reachable direction in this discount-only core, KTD8). The
/// MANDATORY recorded delta (KTD3) is what the gold-conservation invariant reconciles against; it is
/// emitted only when a faction supplies the ore AND the tariff actually moved the price (delta != 0),
/// keeping the log clean at neutral standing.
/// </summary>
public sealed record TariffApplied(
    string FactionId, string MaterialKey, int BaseLineCost, int PlayerCost, int Delta) : GameEvent;

/// <summary>
/// A faction's standing crossed a voicing threshold (P5 U4, R9/KTD7): the drama the flavor engine
/// renders into a gossip line. Carries the faction's <paramref name="FactionId"/> (sim identity) and
/// its <paramref name="FactionName"/> — the DISPLAY name as a ready slot VALUE, so
/// <c>GossipGenerator</c> voices the line with no <c>FactionRegistry</c> lookup (KTD7: the renderer
/// receives only heroes + items, never the registry). <paramref name="Direction"/> is the band
/// crossing (warmed/cooled). Emitted at most once per faction per direction per day-cycle — the ore
/// purchase (rise) crosses the favored ENTER boundary, the Morning drift crosses the EXIT boundary,
/// and the ENTER/EXIT deadband (<see cref="GameSim.Factions.FactionStandingThresholds"/>) is the
/// HYSTERESIS that stops a same-day drift-then-buy oscillation from emitting a contradictory pair.
/// </summary>
public sealed record FactionStandingShifted(
    string FactionId, string FactionName, StandingShiftDirection Direction) : GameEvent;

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

/// <summary>A Morning vendor purchase landed (Playable Core R3/KD2). <paramref name="Cost"/> is the
/// marked-up gold the player handed over — a recorded gold SINK the conservation invariant
/// reconciles against (KTD3; TariffApplied / SupplyDelivered precedent): the vendor's purse is
/// unmodelled, so the cost leaves the modelled town total.</summary>
public sealed record MaterialPurchased(string MaterialKey, int Quantity, int Cost) : GameEvent;

/// <summary>The destitution floor fired (Playable Core R5/KD3): the player hit a true dead-end
/// (cannot buy, craft, stock, or sell anything) and the Morning recovery topped gold up by
/// <paramref name="Amount"/>. A recorded gold SOURCE the conservation invariant reconciles
/// against (mirror of the discount-tariff source term). Never fires on a solvent trace.</summary>
public sealed record RecoveryStipendGranted(int Amount) : GameEvent;

/// <summary>One planned party from the Morning muster: roster (HeroId order), the provisional
/// target floor (bounty acceptance PREDICTED via the same pure rule BountyJudgingSystem applies
/// at the Expedition tick), and the destination venue's <c>VenueRegistry</c> key.</summary>
public sealed record PartyPlan(ImmutableList<HeroId> Roster, int TargetFloor, string VenueId);

/// <summary>Morning-phase muster (plan 2026-07-19-002 U9/KTD8): the parties that will depart at
/// the Expedition tick. Emitted by MusterSystem with ZERO RNG draws; consumed by the adapter for
/// roster-true gate departures, ticker lines, and spectate feeds. A zero-hero morning emits this
/// event with an empty <paramref name="Parties"/> list (pinned shape — never omitted).</summary>
public sealed record PartiesFormed(ImmutableList<PartyPlan> Parties) : GameEvent;
