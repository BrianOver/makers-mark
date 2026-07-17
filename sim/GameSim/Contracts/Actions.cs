using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace GameSim.Contracts;

/// <summary>
/// Everything the player can do, as data (KTD4): the UI and the console runner submit
/// these to <c>Tick</c>; the action log is the replay record. Polymorphic-serializable.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$action")]
[JsonDerivedType(typeof(CraftAction), "craft")]
[JsonDerivedType(typeof(StockAction), "stock")]
[JsonDerivedType(typeof(SetPriceAction), "setPrice")]
[JsonDerivedType(typeof(UnstockAction), "unstock")]
[JsonDerivedType(typeof(BuyOreAction), "buyOre")]
[JsonDerivedType(typeof(PostBountyAction), "postBounty")]
[JsonDerivedType(typeof(UnlockTalentAction), "unlockTalent")]
[JsonDerivedType(typeof(SetProfessionsAction), "setProfessions")]
[JsonDerivedType(typeof(SendSupplyAction), "sendSupply")]
[JsonDerivedType(typeof(RecallPartyAction), "recallParty")]
public abstract record PlayerAction;

/// <summary>Craft a recipe using a material grade key from <see cref="PlayerState.Materials"/> (R4).</summary>
public sealed record CraftAction(string RecipeId, string MaterialKey) : PlayerAction;

/// <summary>Move a crafted item onto the shelf at a price (R16).</summary>
public sealed record StockAction(ItemId Item, int Price) : PlayerAction;

/// <summary>Change the price of a shelved item.</summary>
public sealed record SetPriceAction(ItemId Item, int Price) : PlayerAction;

/// <summary>Take a shelved item back off sale.</summary>
public sealed record UnstockAction(ItemId Item) : PlayerAction;

/// <summary>Buy ore offered by a returning hero during Evening (R6).</summary>
public sealed record BuyOreAction(HeroId From, string MaterialKey, int Quantity) : PlayerAction;

/// <summary>Post a subsidized objective heroes weigh but may decline (R18).</summary>
public sealed record PostBountyAction(int TargetFloor, int RewardGold) : PlayerAction;

/// <summary>Spend a talent point unlocking a node in a profession's talent mini-tree (R4/P1).
/// <paramref name="Profession"/> scopes the node lookup and the resulting unlocked set.</summary>
public sealed record UnlockTalentAction(string NodeId, string Profession) : PlayerAction;

/// <summary>Choose which professions this save practises (P1): pick 1–2 registered professions.
/// Only recipes whose profession is selected may be crafted.</summary>
public sealed record SetProfessionsAction(ImmutableSortedSet<string> Professions) : PlayerAction;

/// <summary>Pay the camp runner to deliver ONE held consumable to a camped hero (Camp phase only).
/// The item goes to the FRONT of the hero's pack — the resolver quaffs front-first, so
/// your delivery drinks before anything the hero bought (P2 pack-order contract).</summary>
public sealed record SendSupplyAction(HeroId To, ItemId Item) : PlayerAction;

/// <summary>Ring the recall bell: the party containing <paramref name="Member"/> banks its
/// stage-1 clears/ore and surfaces at the Deep tick without rolling deeper floors (v1).</summary>
public sealed record RecallPartyAction(HeroId Member) : PlayerAction;

/// <summary>An action the kernel refused, with a typed reason — never a silent drop.</summary>
public sealed record RejectedAction(PlayerAction Action, string Reason);
