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
[JsonDerivedType(typeof(BuyMaterialAction), "buyMaterial")]
[JsonDerivedType(typeof(PostBountyAction), "postBounty")]
[JsonDerivedType(typeof(UnlockTalentAction), "unlockTalent")]
[JsonDerivedType(typeof(SetProfessionsAction), "setProfessions")]
[JsonDerivedType(typeof(SendSupplyAction), "sendSupply")]
[JsonDerivedType(typeof(RecallPartyAction), "recallParty")]
[JsonDerivedType(typeof(OpenCounterAction), "openCounter")]
[JsonDerivedType(typeof(PresentItemAction), "presentItem")]
[JsonDerivedType(typeof(SuggestItemAction), "suggestItem")]
[JsonDerivedType(typeof(HaggleResponseAction), "haggle")]
[JsonDerivedType(typeof(CloseCounterAction), "closeCounter")]
[JsonDerivedType(typeof(AcceptCommissionAction), "acceptCommission")]
[JsonDerivedType(typeof(DeclineCommissionAction), "declineCommission")]
public abstract record PlayerAction;

/// <summary>Craft a recipe using a material grade key from <see cref="PlayerState.Materials"/> (R4).
/// <para><paramref name="PerformanceGrade"/> is the captured-result seam (PA-phase A blacksmith,
/// originally the M3 P4/P11 seam): a per-mille craft-performance grade [0..1000] a PRESENTATION-layer
/// minigame computes and the sim consumes (spec §Determinism model). For a PASSIVE profession 500 is
/// neutral and null is byte-identical to the pre-active roll; for an ACTIVE profession (blacksmith)
/// the grade DOMINATES quality and null means auto-craft at the competent-but-capped baseline (PA2).</para>
/// <para><paramref name="Puzzle"/> is the DUAL-MODE alternative (PKD1): instead of a Godot-computed
/// grade, a profession may submit structured puzzle params the SIM scores itself (alchemist/enchanter,
/// Phase B — strictly better balance-gate coverage). Abstract polymorphic; ZERO derived types in
/// Phase A (always null); Phase B registers derived types via a contracts micro-PR.</para>
/// <para><paramref name="SubScores"/> is the three forge-beat scores (smelt/forge/quench), stored
/// verbatim on the crafted item for Evening ledger flavor ("edge quenched brittle") — DATA, never
/// rules. All three params TRAIL with null defaults, so old saves/actions and existing positional
/// constructors deserialize/compile unchanged (KTD4: the grade rides the ActionLog, replays stay exact).</para></summary>
public sealed record CraftAction(
    string RecipeId,
    string MaterialKey,
    int? PerformanceGrade = null,
    CraftPuzzleInput? Puzzle = null,
    ImmutableList<int>? SubScores = null) : PlayerAction;

/// <summary>
/// Dual-mode craft seam (PKD1): structured puzzle params a profession's crafting logic scores
/// INSIDE the pure sim, as an alternative to a presentation-computed <see cref="CraftAction.PerformanceGrade"/>.
/// Abstract with NO derived types in Phase A — the blacksmith uses the captured grade, never a puzzle,
/// so the property is always null and serializes/deserializes as null. Phase B (alchemist reagent-map,
/// enchanter glyph) adds the <c>[JsonPolymorphic]</c> attribute AND its first derived type in the SAME
/// contracts micro-PR — the attribute must never land alone: System.Text.Json throws at configuration
/// time for a polymorphic base with zero derived types. Rides the ActionLog like any other action data.
/// </summary>
public abstract record CraftPuzzleInput;

/// <summary>Flip the current Morning into stepped counter service (spec §three-layer loop; PKD5):
/// heroes approach the counter one at a time instead of the atomic auto-shopping pass. Morning ONLY.
/// A day that never opens the counter is byte-identical to today — the atomic path stays the default.</summary>
public sealed record OpenCounterAction() : PlayerAction;

/// <summary>Show a shelved item to the active customer at the counter (opener move — a strong
/// role-fit present seeds customer Interest). Counter-session only.</summary>
public sealed record PresentItemAction(ItemId Item) : PlayerAction;

/// <summary>Upsell a complementary-slot item to the active customer (Interest bonus on an empty
/// fitting slot). Counter-session only; never orders the hero — pure influence (PKD7).</summary>
public sealed record SuggestItemAction(ItemId Item) : PlayerAction;

/// <summary>Respond to the active customer's standing offer (PKD6): Accept the offer, HoldFirm
/// (the Recettear band may shift in your favor next round), or Counter at <paramref name="Price"/>.
/// Each response consumes one round of the customer's Patience; ~3-round cap. Counter-session only.</summary>
public sealed record HaggleResponseAction(HaggleResponseKind Kind, int? Price = null) : PlayerAction;

/// <summary>End stepped counter service early. Any hero not yet served falls back to the atomic
/// shopping pass on the closing tick (PKD5 — nobody shops twice, nobody starves).</summary>
public sealed record CloseCounterAction() : PlayerAction;

/// <summary>Move a crafted item onto the shelf at a price (R16).</summary>
public sealed record StockAction(ItemId Item, int Price) : PlayerAction;

/// <summary>Change the price of a shelved item.</summary>
public sealed record SetPriceAction(ItemId Item, int Price) : PlayerAction;

/// <summary>Take a shelved item back off sale.</summary>
public sealed record UnstockAction(ItemId Item) : PlayerAction;

/// <summary>Buy ore offered by a returning hero during Evening (R6).</summary>
public sealed record BuyOreAction(HeroId From, string MaterialKey, int Quantity) : PlayerAction;

/// <summary>Buy base materials from the standing Morning vendor (Playable Core R2/R3, KD2):
/// the always-available supply floor that makes every profession's craft loop reachable on
/// day 1. Morning ONLY; sells the whole <c>MaterialRegistry.PricedPool</c> at unit price plus
/// a fixed markup, so returning heroes' Evening ore offers stay the strictly-cheaper upside.</summary>
public sealed record BuyMaterialAction(string MaterialKey, int Quantity) : PlayerAction;

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

/// <summary>Wave 3: accept a hero's open commission (by hero) — locks it so fulfilling the slot
/// at/above its MinQuality by the deadline pays list + premium.</summary>
public sealed record AcceptCommissionAction(HeroId Hero) : PlayerAction;

/// <summary>Wave 3: decline a hero's open commission — removes it with no obligation.</summary>
public sealed record DeclineCommissionAction(HeroId Hero) : PlayerAction;

/// <summary>An action the kernel refused, with a typed reason — never a silent drop.</summary>
public sealed record RejectedAction(PlayerAction Action, string Reason);
