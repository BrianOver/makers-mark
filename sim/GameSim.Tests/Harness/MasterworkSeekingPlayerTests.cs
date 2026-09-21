using System.Collections.Immutable;
using GameSim.Advisor;
using GameSim.Classes;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Economy;
using GameSim.Harness;
using GameSim.Kernel;

namespace GameSim.Tests.Harness;

/// <summary>
/// P2-HONEST-44 (docs/design/MAKERS-MARK.md §11.16 measurement 6): <see cref="MasterworkSeekingPlayer"/>
/// kept <see cref="BaselinePlayer"/>'s recipe ORDER but dropped its buyer gate, so its Expedition loop
/// spent every window on whatever recipe sorted first, whether or not any alive hero's gear or the
/// shelf's unsold stock still had a real gap for it — measured: 66% Shortswords, 42% of all crafts
/// unsold at the ending, no armor or shield ever made, LethalSave 0/campaign against a baseline
/// median of 8. The property under test is the RULE ("the masterwork policy does not craft a piece
/// no hero would buy"), not one recipe id — every fixture below covers the family (weapon, shield,
/// armor, consumable), never asserts against "shortsword" alone.
/// </summary>
public class MasterworkSeekingPlayerTests
{
    private const int ForgeTierTwoIndex = MasterworkAttemptHandlers.RequiredForgeTierIndex;

    private static Item Worn(int id, string recipeId, string name, ItemSlot slot, ItemStats stats) =>
        new(new ItemId(id), recipeId, name, slot, QualityGrade.Common, stats,
            new MakersMark("Rival Smith", CraftedOnDay: 1), ImmutableList<ItemHistoryEntry>.Empty);

    /// <summary>
    /// A hero at Forge Tier II with a deep gold reserve, ample copper, and a full coal/flux cupboard
    /// — every precondition <see cref="MasterworkAttemptLegal"/> asks for except the buyer question,
    /// so any recipe this fixture skips is skipped BECAUSE of the gate, not some other illegality.
    /// </summary>
    private static GameState ReadyState(GearSet heroGear, ImmutableSortedDictionary<int, Item> items, int packSize)
    {
        var hero = new Hero(
            new HeroId(1), "Test Hero", ClassRegistry.VanguardId, Level: 1, MaxHp: 20, Gold: 500,
            heroGear, ImmutableList<ItemMemory>.Empty, Alive: true, DeepestFloorReached: 0, DiedOnDay: null)
        {
            // PreparedStockTarget (2) is the highest possible ConsumableStockTargetFor reading —
            // a pack at or above it blocks the Consumable recipe under EVERY trait this hero could
            // draw, without this test needing to know or pin which trait it actually got.
            Pack = Enumerable.Range(900, packSize).Select(i => new ItemId(i)).ToImmutableList(),
        };

        var baseState = GameFactory.NewGame(seed: 42, ImmutableSortedDictionary<int, Hero>.Empty.Add(hero.Id.Value, hero));
        return baseState with
        {
            Day = 20,
            Phase = DayPhase.Expedition,
            NextItemId = 1000,
            ActionSlotsRemaining = 5,
            Items = items,
            Player = baseState.Player with
            {
                Gold = 1_000,
                Materials = ImmutableSortedDictionary<string, int>.Empty
                    .SetItem("copper", 100)
                    .SetItem(ForgeTierHandlers.ForgeTierKey, ForgeTierTwoIndex)
                    .SetItem(ForgeSupplyHandlers.Coal, 10)
                    .SetItem(ForgeSupplyHandlers.Flux, 5),
            },
        };
    }

    [Fact]
    public void ActionsFor_ExpeditionPhase_NoAliveHeroHasAGapInAnySlot_CraftsAndAttemptsNothing()
    {
        // Weapon/Shield/Armor worn at or above every Tier-1 recipe's own BaseStats in that slot, and
        // a pack already at the richest possible stocking target — no recipe in the whole table has
        // a real buyer against this roster.
        var weapon = Worn(1, "shortsword", "Shortsword", ItemSlot.Weapon, new ItemStats(Attack: 10, Defense: 0, Weight: 4));
        var shield = Worn(2, "round-shield", "Round Shield", ItemSlot.Shield, new ItemStats(Attack: 0, Defense: 8, Weight: 4));
        var armor = Worn(3, "scale-mail", "Scale Mail", ItemSlot.Armor, new ItemStats(Attack: 0, Defense: 9, Weight: 7));
        var items = ImmutableSortedDictionary<int, Item>.Empty.Add(1, weapon).Add(2, shield).Add(3, armor);
        var gear = new GearSet(new ItemId(1), new ItemId(2), new ItemId(3));

        var state = ReadyState(gear, items, packSize: 3);

        // Non-vacuous, mirroring MasterworkDominanceBalanceTests' own discipline: prove a masterwork
        // attempt on the recipe this fixture blocks would otherwise be LEGAL, so an empty result below
        // is provably the buyer gate at work and not some other illegality making the reading meaningless.
        Assert.True(
            ActionLegality.IsLegal(state, new MasterworkAttemptAction("shortsword", "copper"), state.Phase),
            "fixture assumption: a masterwork attempt on the blocked recipe must be independently legal");

        var actions = MasterworkSeekingPlayer.ActionsFor(state);

        Assert.Empty(actions);
        // Same reading BaselinePlayer's own Expedition loop already gives this roster — the shared
        // HasBuyer question, not a forked one, is what P2-HONEST-44 wires in.
        Assert.Empty(BaselinePlayer.ActionsFor(state));
    }

    [Fact]
    public void ActionsFor_ExpeditionPhase_OneSlotStillHasARealGap_TargetsOnlyThatGap()
    {
        // Same weapon + shield as above (still no buyer for either), but the Armor slot is empty —
        // the one real gap in the whole roster.
        var weapon = Worn(1, "shortsword", "Shortsword", ItemSlot.Weapon, new ItemStats(Attack: 10, Defense: 0, Weight: 4));
        var shield = Worn(2, "round-shield", "Round Shield", ItemSlot.Shield, new ItemStats(Attack: 0, Defense: 8, Weight: 4));
        var items = ImmutableSortedDictionary<int, Item>.Empty.Add(1, weapon).Add(2, shield);
        var gear = new GearSet(new ItemId(1), new ItemId(2), null);

        var state = ReadyState(gear, items, packSize: 3);

        var actions = MasterworkSeekingPlayer.ActionsFor(state);

        var only = Assert.Single(actions);
        var recipeId = only switch
        {
            MasterworkAttemptAction masterwork => masterwork.RecipeId,
            CraftAction craft => craft.RecipeId,
            _ => throw new Xunit.Sdk.XunitException($"unexpected action type {only.GetType()}"),
        };

        // scale-mail is the best-stat Armor recipe this copper-only economy can legally reach — the
        // gate must aim the one action it takes at the actual gap, not just produce SOME action.
        Assert.Equal("scale-mail", recipeId);
        Assert.Equal(ItemSlot.Armor, RecipeTable.All[recipeId].Slot);
    }
}
