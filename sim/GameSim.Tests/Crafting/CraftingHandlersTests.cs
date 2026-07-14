using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Kernel;

namespace GameSim.Tests.Crafting;

public class CraftingHandlersTests
{
    private static readonly GameKernel Kernel = new(
        ImmutableList<IPhaseSystem>.Empty,
        ImmutableList.Create<IActionHandler>(new CraftingHandlers()));

    /// <summary>Day-1 state with the given raw materials in the player's stores.</summary>
    private static GameState StateWith(params (string Key, int Qty)[] materials)
    {
        var state = GameFactory.NewGame(seed: 42);
        var stores = state.Player.Materials;
        foreach (var (key, qty) in materials)
        {
            stores = stores.SetItem(key, qty);
        }

        return state with { Player = state.Player with { Materials = stores } };
    }

    private static GameState WithTalents(GameState state, params string[] talents) =>
        state with { Player = state.Player with { Talents = state.Player.Talents.Union(talents) } };

    private sealed class TestSink : IEventSink
    {
        public List<GameEvent> Events { get; } = [];

        public void Emit(GameEvent gameEvent) => Events.Add(gameEvent);
    }

    // ---- CraftAction: happy path -------------------------------------------------

    [Fact]
    public void Craft_MintsItem_WithMark_EmptyHistory_AndEvent()
    {
        var state = StateWith(("copper", 5));
        var result = Kernel.Tick(state, ImmutableList.Create<PlayerAction>(new CraftAction("dagger", "copper")));

        Assert.Empty(result.Rejected);

        var item = Assert.Single(result.NewState.Items).Value;
        Assert.Equal(new ItemId(1), item.Id);
        Assert.Equal(2, result.NewState.NextItemId);
        Assert.Equal("dagger", item.RecipeId);
        Assert.Equal(ItemSlot.Weapon, item.Slot);
        Assert.NotNull(item.Mark);
        Assert.Equal("You", item.Mark!.CrafterName);
        Assert.Equal(1, item.Mark.CraftedOnDay);
        Assert.Empty(item.History);

        // Quality-scaled integer stats: dagger base attack 8 through the grade multiplier.
        var expectedPct = item.Quality switch
        {
            QualityGrade.Poor => 80,
            QualityGrade.Common => 100,
            QualityGrade.Fine => 115,
            QualityGrade.Superior => 135,
            QualityGrade.Masterwork => 160,
            _ => throw new InvalidOperationException(),
        };
        Assert.Equal(8 * expectedPct / 100, item.Stats.Attack);

        // Materials consumed: dagger costs 2 copper.
        Assert.Equal(3, result.NewState.Player.Materials["copper"]);

        var crafted = Assert.IsType<ItemCrafted>(Assert.Single(result.NewState.EventLog));
        Assert.Equal(item.Id, crafted.Item);
        Assert.Equal(item.Quality, crafted.Quality);
        Assert.Equal(1, crafted.Day);
    }

    [Fact]
    public void Craft_IsLegal_InAllThreePhases()
    {
        var handlers = new CraftingHandlers();
        foreach (var phase in new[] { DayPhase.Morning, DayPhase.Expedition, DayPhase.Evening })
        {
            Assert.True(handlers.CanHandle(new CraftAction("dagger", "copper"), phase));
            Assert.True(handlers.CanHandle(new UnlockTalentAction(TalentTree.KeenEye), phase));
        }
    }

    [Fact]
    public void Craft_WithAnyMaterialGrade_ConsumesThatMaterial()
    {
        // Better material than the recipe's baseline is allowed; the chosen key is consumed.
        var state = StateWith(("mithril", 4));
        var result = Kernel.Tick(state, ImmutableList.Create<PlayerAction>(new CraftAction("dagger", "mithril")));

        Assert.Empty(result.Rejected);
        Assert.Equal(2, result.NewState.Player.Materials["mithril"]);
        Assert.Single(result.NewState.Items);
    }

    // ---- CraftAction: rejections -------------------------------------------------

    [Fact]
    public void Craft_UnknownRecipe_TypedRejection()
    {
        var state = StateWith(("copper", 5));
        var action = new CraftAction("excalibur", "copper");
        var result = Kernel.Tick(state, ImmutableList.Create<PlayerAction>(action));

        var rejected = Assert.Single(result.Rejected);
        Assert.Same(action, rejected.Action);
        Assert.Contains("excalibur", rejected.Reason);
        Assert.Empty(result.NewState.Items);
        Assert.Equal(1, result.NewState.NextItemId);
    }

    [Fact]
    public void Craft_UnknownMaterial_TypedRejection()
    {
        var state = StateWith(("copper", 5));
        var result = Kernel.Tick(state, ImmutableList.Create<PlayerAction>(new CraftAction("dagger", "unobtainium")));

        var rejected = Assert.Single(result.Rejected);
        Assert.Contains("unobtainium", rejected.Reason);
        Assert.Empty(result.NewState.Items);
    }

    [Fact]
    public void Craft_InsufficientMaterials_RejectionNamesMaterial_StateUnchanged_NoRngDrawn()
    {
        var state = StateWith(("copper", 1)); // dagger needs 2
        var handlers = new CraftingHandlers();
        var rng = new Pcg32(RngState.FromSeed(9));
        var before = rng.Snapshot();
        var sink = new TestSink();

        var (newState, rejected) = handlers.Apply(state, new CraftAction("dagger", "copper"), rng, sink);

        Assert.NotNull(rejected);
        Assert.Contains("copper", rejected!.Reason);
        Assert.Equal(SaveCodec.Serialize(state), SaveCodec.Serialize(newState)); // state untouched
        Assert.Equal(before, rng.Snapshot()); // rejection must not advance the RNG stream
        Assert.Empty(sink.Events);
    }

    [Fact]
    public void Craft_TierGated_RequiresUnlockTalent()
    {
        var state = StateWith(("iron", 10), ("steel", 10));

        // Tier 2 without tier-2-smithing → rejected.
        var locked = Kernel.Tick(state, ImmutableList.Create<PlayerAction>(new CraftAction("longsword", "iron")));
        var rejected = Assert.Single(locked.Rejected);
        Assert.Contains(TalentTree.Tier2Smithing, rejected.Reason);
        Assert.Empty(locked.NewState.Items);

        // Tier 2 with the talent → crafted.
        var unlocked = Kernel.Tick(
            WithTalents(state, TalentTree.Tier2Smithing),
            ImmutableList.Create<PlayerAction>(new CraftAction("longsword", "iron")));
        Assert.Empty(unlocked.Rejected);
        Assert.Single(unlocked.NewState.Items);

        // Tier 3 needs tier-3-smithing even when tier 2 is unlocked.
        var tier3 = Kernel.Tick(
            WithTalents(state, TalentTree.Tier2Smithing),
            ImmutableList.Create<PlayerAction>(new CraftAction("greatsword", "steel")));
        var rejected3 = Assert.Single(tier3.Rejected);
        Assert.Contains(TalentTree.Tier3Smithing, rejected3.Reason);
    }

    [Fact]
    public void MaterialEfficiency_ReducesConsumptionByOne_FloorOfOne()
    {
        // Dagger costs 2 copper; material-efficiency drops it to 1.
        var state = WithTalents(StateWith(("copper", 1)), TalentTree.MaterialEfficiency);
        var result = Kernel.Tick(state, ImmutableList.Create<PlayerAction>(new CraftAction("dagger", "copper")));

        Assert.Empty(result.Rejected);
        Assert.Equal(0, result.NewState.Player.Materials["copper"]);
        Assert.Single(result.NewState.Items);
    }

    // ---- UnlockTalentAction --------------------------------------------------------

    [Fact]
    public void UnlockTalent_AddsNode_WhenPrereqsMet()
    {
        var state = StateWith();
        var result = Kernel.Tick(state, ImmutableList.Create<PlayerAction>(new UnlockTalentAction(TalentTree.KeenEye)));

        Assert.Empty(result.Rejected);
        Assert.Contains(TalentTree.KeenEye, result.NewState.Player.Talents);
    }

    [Fact]
    public void UnlockTalent_RejectsMissingPrereq_UnknownNode_AndDuplicate()
    {
        var state = StateWith();

        var missingPrereq = Kernel.Tick(state, ImmutableList.Create<PlayerAction>(new UnlockTalentAction(TalentTree.MasterTouch)));
        var r1 = Assert.Single(missingPrereq.Rejected);
        Assert.Contains(TalentTree.KeenEye, r1.Reason);

        var unknown = Kernel.Tick(state, ImmutableList.Create<PlayerAction>(new UnlockTalentAction("not-a-node")));
        var r2 = Assert.Single(unknown.Rejected);
        Assert.Contains("not-a-node", r2.Reason);

        var duplicate = Kernel.Tick(
            WithTalents(state, TalentTree.KeenEye),
            ImmutableList.Create<PlayerAction>(new UnlockTalentAction(TalentTree.KeenEye)));
        var r3 = Assert.Single(duplicate.Rejected);
        Assert.Contains(TalentTree.KeenEye, r3.Reason);
    }

    [Fact]
    public void UnlockThenCraft_SameTick_TalentApplies()
    {
        // Actions in one batch apply in order: unlock tier-2, then craft tier-2.
        var state = StateWith(("iron", 5));
        var result = Kernel.Tick(state, ImmutableList.Create<PlayerAction>(
            new UnlockTalentAction(TalentTree.Tier2Smithing),
            new CraftAction("longsword", "iron")));

        Assert.Empty(result.Rejected);
        Assert.Single(result.NewState.Items);
    }

    // ---- Determinism ----------------------------------------------------------------

    [Fact]
    public void SameState_SameCraftAction_IdenticalItem_ByteIdenticalState()
    {
        var state = StateWith(("iron", 10));
        var actions = ImmutableList.Create<PlayerAction>(
            new UnlockTalentAction(TalentTree.Tier2Smithing),
            new CraftAction("longsword", "iron"));

        var a = Kernel.Tick(state, actions);
        var b = Kernel.Tick(state, actions);

        Assert.Equal(SaveCodec.Serialize(a.NewState), SaveCodec.Serialize(b.NewState));

        var itemA = a.NewState.Items[1];
        var itemB = b.NewState.Items[1];
        Assert.Equal(itemA.Id, itemB.Id);
        Assert.Equal(itemA.Quality, itemB.Quality);
        Assert.Equal(itemA.Stats, itemB.Stats);
    }
}
