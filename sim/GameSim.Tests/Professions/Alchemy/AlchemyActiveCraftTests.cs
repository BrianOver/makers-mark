using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Kernel;
using GameSim.Professions;

namespace GameSim.Tests.Professions.Alchemy;

/// <summary>
/// Phase B end-to-end: the alchemist's reagent puzzle rides <see cref="CraftAction.Puzzle"/>
/// through the REAL kernel pipeline (<see cref="CraftingHandlers"/>), the sim scores it, and the
/// grade dominates quality via <see cref="QualityRoller.RollActive"/> exactly the way the
/// blacksmith's captured grade does. Covers the routing branch, the puzzle-mismatch rejections
/// (before any RNG draw), replay determinism with a puzzle in the batch, and the save/ActionLog
/// round-trip through <see cref="SaveCodec"/>'s runtime polymorphism registration.
/// </summary>
public class AlchemyActiveCraftTests
{
    private static readonly GameKernel Kernel = new(
        ImmutableList<IPhaseSystem>.Empty,
        ImmutableList.Create<IActionHandler>(new CraftingHandlers()));

    private static GameState NewAlchemyState(ulong seed, string material = "copper", int qty = 4, string profession = "alchemy")
    {
        var state = GameFactory.NewGame(seed);
        return state with
        {
            Player = state.Player with
            {
                SelectedProfessions = ImmutableSortedSet.Create(profession),
                Materials = state.Player.Materials.SetItem(material, qty),
            },
        };
    }

    private static AlchemyReagentPuzzle PerfectBrew(string recipeId) =>
        new(AlchemyPuzzleScorer.IdealSequenceFor(AlchemyProfession.Definition.Recipes[recipeId]));

    [Fact]
    public void PerfectBrew_ThroughKernel_GradeDominates_ReachesTopBands()
    {
        // A perfect puzzle scores 1000; with material grade == tier the ceiling is Superior —
        // so a perfect brew always lands Superior (jittered 975..1025, ceiling-capped), never
        // the Common/Fine the 550 auto-craft baseline hovers at.
        var state = NewAlchemyState(seed: 4242);
        var action = new CraftAction("alchemy-minor-elixir", "copper", Puzzle: PerfectBrew("alchemy-minor-elixir"));

        var result = Kernel.Tick(state, ImmutableList.Create<PlayerAction>(action));

        Assert.Empty(result.Rejected);
        var item = Assert.Single(result.NewState.Items).Value;
        Assert.Equal(QualityGrade.Superior, item.Quality);
    }

    [Fact]
    public void PerfectBrew_WithAboveTierMaterial_IsMasterworkReachable_AutoCraftNever()
    {
        // Iron (grade 2) on a tier-1 recipe lifts the ceiling: a perfect brew's 975+ effective
        // value is Masterwork every time; the puzzle is the only road to the top (PKD4) — the
        // same seed's auto-craft never gets there.
        var brewed = Kernel.Tick(
            NewAlchemyState(seed: 7, material: "iron"),
            ImmutableList.Create<PlayerAction>(new CraftAction("alchemy-minor-elixir", "iron", Puzzle: PerfectBrew("alchemy-minor-elixir"))));
        Assert.Empty(brewed.Rejected);
        Assert.Equal(QualityGrade.Masterwork, Assert.Single(brewed.NewState.Items).Value.Quality);

        var auto = Kernel.Tick(
            NewAlchemyState(seed: 7, material: "iron"),
            ImmutableList.Create<PlayerAction>(new CraftAction("alchemy-minor-elixir", "iron")));
        Assert.Empty(auto.Rejected);
        Assert.NotEqual(QualityGrade.Masterwork, Assert.Single(auto.NewState.Items).Value.Quality);
    }

    [Fact]
    public void GarbageBrew_ThroughKernel_LandsPoor_WorseThanAutoCraft()
    {
        // An actively-botched brew (grade 0, jitter can only reach +25) is Poor — visibly worse
        // than auto-craft's competent 550 baseline. Skill dominates in both directions.
        var state = NewAlchemyState(seed: 4242);
        var garbage = new AlchemyReagentPuzzle(ImmutableList.Create(
            AlchemyReagents.Voidsalt, AlchemyReagents.Voidsalt, AlchemyReagents.Voidsalt));

        var result = Kernel.Tick(state, ImmutableList.Create<PlayerAction>(
            new CraftAction("alchemy-minor-elixir", "copper", Puzzle: garbage)));

        Assert.Empty(result.Rejected);
        Assert.Equal(QualityGrade.Poor, Assert.Single(result.NewState.Items).Value.Quality);
    }

    [Fact]
    public void ReagentPuzzle_OnABlacksmithRecipe_IsRejected_BeforeAnyRngDraw()
    {
        var state = NewAlchemyState(seed: 1, profession: "blacksmith");

        var result = Kernel.Tick(state, ImmutableList.Create<PlayerAction>(
            new CraftAction("dagger", "copper", Puzzle: PerfectBrew("alchemy-minor-elixir"))));

        var rejected = Assert.Single(result.Rejected);
        Assert.Contains("does not take a reagent puzzle", rejected.Reason);
        Assert.Empty(result.NewState.Items);
        // Rejection precedes the RNG draw: materials untouched proves the craft never ran.
        Assert.Equal(4, result.NewState.Player.Materials["copper"]);
    }

    [Fact]
    public void SameSeedSameBrew_ByteIdenticalStates_ReplayHolds()
    {
        GameState Run()
        {
            var state = NewAlchemyState(seed: 99);
            var batch = ImmutableList.Create<PlayerAction>(
                new CraftAction("alchemy-minor-elixir", "copper", Puzzle: PerfectBrew("alchemy-minor-elixir")));
            return Kernel.Tick(state, batch).NewState;
        }

        Assert.Equal(SaveCodec.Serialize(Run()), SaveCodec.Serialize(Run()));
    }

    [Fact]
    public void BrewInTheActionLog_RoundTripsThroughSaveCodec()
    {
        // The puzzle rides the ActionLog like any other action data (KTD4). SaveCodec's runtime
        // polymorphism registration ("$puzzle" discriminator) must round-trip the derived type
        // with its reagent sequence intact — and a puzzle-less log stays byte-identical (the
        // null case is covered by every pre-existing save/replay test).
        var state = NewAlchemyState(seed: 5);
        var brew = PerfectBrew("alchemy-minor-elixir");
        var after = Kernel.Tick(state, ImmutableList.Create<PlayerAction>(
            new CraftAction("alchemy-minor-elixir", "copper", Puzzle: brew))).NewState;

        var json = SaveCodec.Serialize(after);
        var reloaded = SaveCodec.Deserialize(json);

        var craft = Assert.IsType<CraftAction>(Assert.Single(reloaded.ActionLog).Actions[0]);
        var puzzle = Assert.IsType<AlchemyReagentPuzzle>(craft.Puzzle);
        Assert.Equal(brew.Reagents, puzzle.Reagents);

        // And the reloaded state re-serializes byte-stable.
        Assert.Equal(json, SaveCodec.Serialize(reloaded));
    }

    [Fact]
    public void AutoCraft_NullPuzzleNullGrade_ConsumesExactlyOneRoll_SameAsBefore()
    {
        // Draw-count pin (KTD4): an alchemy craft — puzzle or not — draws exactly one Roll100,
        // identical to the passive path it replaced, so no other module's stream shifts.
        var withPuzzle = Kernel.Tick(
            NewAlchemyState(seed: 321),
            ImmutableList.Create<PlayerAction>(new CraftAction("alchemy-minor-elixir", "copper", Puzzle: PerfectBrew("alchemy-minor-elixir")))).NewState;
        var withoutPuzzle = Kernel.Tick(
            NewAlchemyState(seed: 321),
            ImmutableList.Create<PlayerAction>(new CraftAction("alchemy-minor-elixir", "copper"))).NewState;

        // Same seed, same single draw → the RNG stream position after the tick is identical,
        // which the serialized RngState pins (states differ only in the crafted item + log).
        Assert.Equal(withPuzzle.Rng, withoutPuzzle.Rng);
    }
}
