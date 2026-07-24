using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Kernel;
using GameSim.Professions;

namespace GameSim.Tests.Crafting;

using static GameSim.Tests.Drama.DramaFixtures;

/// <summary>
/// Wave 4c (U20, heirloom reforge): <see cref="HeirloomHandlers"/> processes
/// <see cref="ReforgeHeirloomAction"/>. Covers the happy path (lineage-carrying new item + both
/// events), invalid-source rejections (unknown item, never worn by a fallen hero), the
/// no-double-reforge guard, phase legality (all three phases, like <see cref="CraftAction"/>),
/// and the determinism pin — the SAME single roll a normal <see cref="CraftAction"/> draws.
/// </summary>
public class HeirloomHandlersTests
{
    private static readonly GameKernel Kernel = new(
        ImmutableList<IPhaseSystem>.Empty,
        ImmutableList.Create<IActionHandler>(new HeirloomHandlers()));

    private const string Blacksmith = ProfessionRegistry.BlacksmithId;

    /// <summary>A day-1 world with a dead hero (id 1) whose worn dagger (item 10) is reforgeable,
    /// plus enough copper stocked to reforge it.</summary>
    private static GameState FallenHeroWorld(ulong seed = 42, int copper = 5)
    {
        var state = GameFactory.NewGame(seed);
        var wornGear = new GearSet(new ItemId(10), null, null);
        var dagger = new Item(
            new ItemId(10), "dagger", "Dagger", ItemSlot.Weapon, QualityGrade.Common,
            new ItemStats(8, 0, 2), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);

        var hero = new Hero(
            new HeroId(1), "Sera", "vanguard", Level: 4, MaxHp: 40, Gold: 0,
            wornGear, ImmutableList<ItemMemory>.Empty, Alive: false, DeepestFloorReached: 3, DiedOnDay: 1);

        var died = new HeroDied(new HeroId(1), 3, "slain by a Tunnel Spider", wornGear) { Id = new EventId(1), Day = 1 };

        return state with
        {
            Heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(1, hero),
            Items = ImmutableSortedDictionary<int, Item>.Empty.Add(10, dagger),
            EventLog = ImmutableList.Create<GameEvent>(died),
            Player = state.Player with { Materials = state.Player.Materials.SetItem("copper", copper) },
        };
    }

    // ---- Happy path ---------------------------------------------------------------------

    [Fact]
    public void Reforge_ValidSource_MintsNewItem_WithLineage_EmitsBothEvents_ConsumesMaterial()
    {
        var state = FallenHeroWorld();
        var action = new ReforgeHeirloomAction(new ItemId(10), "shortsword", "copper");
        var result = Kernel.Tick(state, ImmutableList.Create<PlayerAction>(action));

        Assert.Empty(result.Rejected);

        // NextItemId starts at 1 in a fresh GameFactory.NewGame world, independent of the hand-
        // seeded source item's id (10).
        Assert.Equal(new ItemId(1), result.NewState.Items.Values.Single(i => i.Id.Value != 10).Id);
        var reforged = Assert.Single(result.NewState.Items.Values, i => i.HeirloomLineage is not null);
        Assert.Equal("forged from the Dagger of Sera", reforged.HeirloomLineage);
        Assert.True(reforged.IsHeirloom);
        Assert.Equal("shortsword", reforged.RecipeId);

        var craftedEvent = Assert.Single(result.Events.OfType<ItemCrafted>());
        Assert.Equal(reforged.Id, craftedEvent.Item);

        var reforgedEvent = Assert.Single(result.Events.OfType<HeirloomReforged>());
        Assert.Equal(reforged.Id, reforgedEvent.NewItem);
        Assert.Equal(new ItemId(10), reforgedEvent.SourceItem);
        Assert.Equal("forged from the Dagger of Sera", reforgedEvent.Lineage);

        // Shortsword costs 3 copper; started with 5.
        Assert.Equal(2, result.NewState.Player.Materials["copper"]);
    }

    [Fact]
    public void Reforge_ConsumesOneActionSlot()
    {
        var state = FallenHeroWorld();
        var before = state.ActionSlotsRemaining;
        var action = new ReforgeHeirloomAction(new ItemId(10), "shortsword", "copper");
        var result = Kernel.Tick(state, ImmutableList.Create<PlayerAction>(action));

        Assert.Empty(result.Rejected);
        Assert.Equal(before - 1, result.NewState.ActionSlotsRemaining);
    }

    // ---- Rejections: invalid source ------------------------------------------------------

    [Fact]
    public void Reforge_UnknownSourceItem_TypedRejection()
    {
        var state = FallenHeroWorld();
        var action = new ReforgeHeirloomAction(new ItemId(999), "shortsword", "copper");
        var result = Kernel.Tick(state, ImmutableList.Create<PlayerAction>(action));

        var rejection = Assert.Single(result.Rejected);
        Assert.Contains("Unknown source item", rejection.Reason);
        Assert.DoesNotContain(result.NewState.Items.Values, i => i.HeirloomLineage is not null);
    }

    [Fact]
    public void Reforge_ItemNeverWornByAFallenHero_TypedRejection()
    {
        var state = FallenHeroWorld();
        // Item 20 exists but no HeroDied event ever names it in WornGear.
        var untouched = new Item(
            new ItemId(20), "buckler", "Buckler", ItemSlot.Shield, QualityGrade.Common,
            new ItemStats(0, 6, 2), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);
        state = state with { Items = state.Items.Add(20, untouched) };

        var action = new ReforgeHeirloomAction(new ItemId(20), "shortsword", "copper");
        var result = Kernel.Tick(state, ImmutableList.Create<PlayerAction>(action));

        var rejection = Assert.Single(result.Rejected);
        Assert.Contains("was never worn by a fallen hero", rejection.Reason);
    }

    [Fact]
    public void Reforge_ItemWornByAStillLivingHero_TypedRejection()
    {
        // A living hero's equipped gear is a real item, but never rides a HeroDied event.
        var state = GameFactory.NewGame(7);
        var weapon = new Item(
            new ItemId(30), "dagger", "Dagger", ItemSlot.Weapon, QualityGrade.Common,
            new ItemStats(8, 0, 2), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);
        var livingHero = new Hero(
            new HeroId(2), "Kess", "vanguard", Level: 2, MaxHp: 30, Gold: 0,
            new GearSet(new ItemId(30), null, null), ImmutableList<ItemMemory>.Empty,
            Alive: true, DeepestFloorReached: 1, DiedOnDay: null);
        state = state with
        {
            Heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(2, livingHero),
            Items = state.Items.Add(30, weapon),
            Player = state.Player with { Materials = state.Player.Materials.SetItem("copper", 5) },
        };

        var action = new ReforgeHeirloomAction(new ItemId(30), "shortsword", "copper");
        var result = Kernel.Tick(state, ImmutableList.Create<PlayerAction>(action));

        var rejection = Assert.Single(result.Rejected);
        Assert.Contains("was never worn by a fallen hero", rejection.Reason);
    }

    // ---- No double reforge ----------------------------------------------------------------

    [Fact]
    public void Reforge_SameSourceTwice_SecondAttemptRejected_AlreadyReforged()
    {
        var state = FallenHeroWorld(copper: 10);
        var action = new ReforgeHeirloomAction(new ItemId(10), "shortsword", "copper");

        var first = Kernel.Tick(state, ImmutableList.Create<PlayerAction>(action));
        Assert.Empty(first.Rejected);

        var second = Kernel.Tick(first.NewState, ImmutableList.Create<PlayerAction>(action));
        var rejection = Assert.Single(second.Rejected);
        Assert.Contains("has already been reforged", rejection.Reason);
        // No second lineage-carrying item was minted.
        Assert.Single(second.NewState.Items.Values, i => i.HeirloomLineage is not null);
    }

    // ---- Standard craft guards still apply (reused chain) ---------------------------------

    [Fact]
    public void Reforge_UnknownRecipe_TypedRejection()
    {
        var state = FallenHeroWorld();
        var action = new ReforgeHeirloomAction(new ItemId(10), "excalibur", "copper");
        var result = Kernel.Tick(state, ImmutableList.Create<PlayerAction>(action));

        Assert.Contains("Unknown recipe", Assert.Single(result.Rejected).Reason);
    }

    [Fact]
    public void Reforge_InsufficientMaterial_TypedRejection()
    {
        var state = FallenHeroWorld(copper: 1); // shortsword needs 3
        var action = new ReforgeHeirloomAction(new ItemId(10), "shortsword", "copper");
        var result = Kernel.Tick(state, ImmutableList.Create<PlayerAction>(action));

        Assert.Contains("Not enough copper", Assert.Single(result.Rejected).Reason);
    }

    // ---- Phase legality: all three phases, like CraftAction --------------------------------

    [Theory]
    [InlineData(DayPhase.Morning)]
    [InlineData(DayPhase.Expedition)]
    [InlineData(DayPhase.Evening)]
    public void Reforge_IsLegal_InAllThreePhases(DayPhase phase)
    {
        var handler = new HeirloomHandlers();
        Assert.True(handler.CanHandle(new ReforgeHeirloomAction(new ItemId(10), "shortsword", "copper"), phase));
    }

    // ---- Determinism: SAME single roll a normal craft draws ---------------------------------

    [Fact]
    public void Reforge_DrawsExactlyOneRoll_SameAsNormalCraft_IdenticalQualityGivenSameSeed()
    {
        // Same recipe, material, talents (none), profession (blacksmith, selected by default) —
        // the ONLY difference between a bare CraftAction and this reforge is the source-provenance
        // guard, so a fresh rng at the same seed must land on the exact same QualityGrade and the
        // exact same post-draw stream position for both paths.
        var craftState = GameFactory.NewGame(2026) with
        {
            Player = GameFactory.NewGame(2026).Player with
            {
                Materials = GameFactory.NewGame(2026).Player.Materials.SetItem("copper", 5),
            },
        };
        var craftRng = new Pcg32(craftState.Rng);
        var craftSink = new NullSink();
        var craftHandlers = new CraftingHandlers();
        var (afterCraft, craftRejected) = craftHandlers.Apply(
            craftState, new CraftAction("shortsword", "copper"), craftRng, craftSink);
        Assert.Null(craftRejected);
        var craftedItem = afterCraft.Items.Values.Single();

        var reforgeState = FallenHeroWorld(seed: 2026, copper: 5);
        var reforgeRng = new Pcg32(reforgeState.Rng);
        var reforgeSink = new NullSink();
        var heirloomHandlers = new HeirloomHandlers();
        var (afterReforge, reforgeRejected) = heirloomHandlers.Apply(
            reforgeState, new ReforgeHeirloomAction(new ItemId(10), "shortsword", "copper"), reforgeRng, reforgeSink);
        Assert.Null(reforgeRejected);
        var reforgedItem = afterReforge.Items.Values.Single(i => i.Id.Value != 10);

        Assert.Equal(craftRng.Snapshot(), reforgeRng.Snapshot()); // both drew exactly one Roll100
        Assert.Equal(craftedItem.Quality, reforgedItem.Quality); // same shift, same single roll -> same band
        Assert.Equal(craftedItem.Stats, reforgedItem.Stats);
    }

    [Fact]
    public void SameState_SameReforgeAction_ByteIdenticalReplay()
    {
        var state = FallenHeroWorld();
        var actions = ImmutableList.Create<PlayerAction>(new ReforgeHeirloomAction(new ItemId(10), "shortsword", "copper"));

        var a = Kernel.Tick(state, actions);
        var b = Kernel.Tick(state, actions);

        Assert.Equal(SaveCodec.Serialize(a.NewState), SaveCodec.Serialize(b.NewState));
    }

    // ---- Save-compat: HeirloomLineage trailing init member ----------------------------------

    [Fact]
    public void Item_WithoutHeirloomLineageProperty_DeserializesToNull_NotAnHeirloom()
    {
        var plainItem = new Item(
            new ItemId(1), "dagger", "Dagger", ItemSlot.Weapon, QualityGrade.Common,
            new ItemStats(8, 0, 2), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);

        Assert.Null(plainItem.HeirloomLineage);
        Assert.False(plainItem.IsHeirloom);

        var state = GameFactory.NewGame(1) with { Items = ImmutableSortedDictionary<int, Item>.Empty.Add(1, plainItem) };
        var json = SaveCodec.Serialize(state);
        var reloaded = SaveCodec.Deserialize(json);

        var reloadedItem = reloaded.Items[1];
        Assert.Null(reloadedItem.HeirloomLineage);
        Assert.False(reloadedItem.IsHeirloom);
    }

    private sealed class NullSink : IEventSink
    {
        public void Emit(GameEvent gameEvent)
        {
        }
    }
}
