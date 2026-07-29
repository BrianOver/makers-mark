using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Kernel;
using GameSim.Professions;

namespace GameSim.Tests.Professions;

/// <summary>
/// PA2/PKD2 byte-identical regression pin, amended by Phase B and by U3b (plan
/// <c>2026-07-28-004</c>): ALL FOUR professions — blacksmith (PA2), alchemy (Phase B), and now
/// tanning + engineering (U3b) — are ACTIVE. There are no passive professions left. Each
/// fixed-seed case below pins its auto-craft golden (null grade + null puzzle → the
/// competent-but-capped <see cref="QualityRoller.RollActive"/> path). Tanning and engineering's
/// goldens moved at the U3b flip: both dropped from Superior (the old passive
/// <see cref="QualityRoller.Roll"/> result) to Fine — the deliberate PKD4 auto-craft
/// consequence (auto-craft hard-caps at Superior, and here the tier-1/copper material ceiling
/// caps at Fine before that even applies), exactly the same drop alchemy's golden took at its
/// own flip. Each case crafts through the real, full pipeline (<see cref="CraftingHandlers"/>
/// via the kernel, exactly as the game does) with a fixed seed and pins the resulting item's
/// quality, stats, and effect magnitude against golden values captured from this exact code
/// path. If a future change to the roll, the shared quality math, or these professions' data
/// ever moves these numbers, this test fails.
/// </summary>
public class PassiveQualityRegressionTests
{
    private static readonly GameKernel Kernel = new(
        ImmutableList<IPhaseSystem>.Empty,
        ImmutableList.Create<IActionHandler>(new CraftingHandlers()));

    private static Item CraftOne(string profession, string recipeId, string materialKey, int qty, ulong seed)
    {
        var state = GameFactory.NewGame(seed);
        state = state with
        {
            Player = state.Player with
            {
                SelectedProfessions = ImmutableSortedSet.Create(profession),
                Materials = state.Player.Materials.SetItem(materialKey, qty),
            },
        };

        var result = Kernel.Tick(state, ImmutableList.Create<PlayerAction>(new CraftAction(recipeId, materialKey)));
        Assert.Empty(result.Rejected);
        return Assert.Single(result.NewState.Items).Value;
    }

    [Fact]
    public void Alchemy_FixedSeedAutoCraft_RoutesActive_MatchesGoldenItem()
    {
        // Phase B flipped alchemy ACTIVE: a puzzle-less craft is now the auto-craft path
        // (RollActive, grade 550 + jittered by the same single Roll100 draw the passive roll
        // used — the draw COUNT is unchanged, so no other module's stream moves). Golden
        // captured from this exact code path at the flip.
        var item = CraftOne("alchemy", "alchemy-minor-elixir", "copper", qty: 2, seed: 4242);

        Assert.Equal(QualityGrade.Fine, item.Quality);
        Assert.Equal(new ItemStats(Attack: 0, Defense: 0, Weight: 0), item.Stats);
        Assert.NotNull(item.Effect);
        Assert.Equal(ConsumableKind.Heal, item.Effect!.Kind);
        Assert.Equal(6, item.Effect.Magnitude); // base 6 * ItemForge's Fine percent = 6 (integer division)
        Assert.Empty(item.CraftSubScores); // auto-craft carries no puzzle sub-scores
    }

    [Fact]
    public void Engineering_FixedSeedAutoCraft_RoutesActive_MatchesGoldenItem()
    {
        // U3b flipped engineering ACTIVE: a puzzle-less craft is now the auto-craft path
        // (RollActive, grade 550 + jittered by the same single Roll100 draw the passive roll
        // used — the draw COUNT is unchanged). Golden captured from this exact code path at
        // the flip: Superior (the old passive golden) drops to Fine (the tier-1/copper material
        // ceiling caps at Fine, and auto-craft additionally hard-caps at Superior — PKD4).
        var item = CraftOne("engineering", "engineering-bolt-thrower", "copper", qty: 2, seed: 4242);

        Assert.Equal(QualityGrade.Fine, item.Quality);
        Assert.Equal(new ItemStats(Attack: 9, Defense: 0, Weight: 2), item.Stats); // base 8 attack * Fine 115% = 9
        Assert.Null(item.Effect);
        Assert.Empty(item.CraftSubScores); // auto-craft carries no puzzle sub-scores
    }

    [Fact]
    public void Tanning_FixedSeedAutoCraft_RoutesActive_MatchesGoldenItem()
    {
        // U3b flipped tanning ACTIVE — same auto-craft path and the same Superior→Fine drop as
        // engineering and alchemy took at their own flips.
        var item = CraftOne("tanning", "tanning-hide-jerkin", "copper", qty: 3, seed: 4242);

        Assert.Equal(QualityGrade.Fine, item.Quality);
        Assert.Equal(new ItemStats(Attack: 0, Defense: 8, Weight: 3), item.Stats); // base 7 defense * Fine 115% = 8
        Assert.Null(item.Effect);
        Assert.Empty(item.CraftSubScores); // auto-craft carries no puzzle sub-scores
    }

    [Fact]
    public void ActiveProfessions_AllRouteThrough_TheDominanceRoll_AndRetiredTheirShifts()
    {
        // Structural half of the pin, U3b edition: EVERY profession is now active — assert the
        // exact set so no future profession flips by accident (half a), and that every active
        // profession retired its FlatShifts/SlotShifts into MinigameAssists (half b) — the guard
        // that would have caught the dead-talent bug the U1 correction found (a profession
        // shipping ActiveCraft: true while a talent still only writes to FlatShifts/SlotShifts is
        // dead data, because RollActive never reads those fields).
        var expectedActiveIds = ImmutableSortedSet.Create(
            StringComparer.Ordinal,
            ProfessionRegistry.BlacksmithId,
            AlchemyProfession.Id,
            TanningProfession.Id,
            EngineeringProfession.Id);

        // Exactly the registered set of professions, so a fifth profession appearing here
        // without also appearing in ProfessionRegistry.All would fail loudly, not silently.
        Assert.Equal(expectedActiveIds, ImmutableSortedSet.CreateRange(StringComparer.Ordinal, ProfessionRegistry.All.Keys));

        foreach (var profession in ProfessionRegistry.All.Values)
        {
            Assert.Contains(profession.Id, expectedActiveIds);
            Assert.True(profession.ActiveCraft, $"{profession.Id} must be ActiveCraft.");
            Assert.NotEmpty(profession.MinigameAssists); // every active profession has assist data
            Assert.Empty(profession.Quality.FlatShifts);  // PKD3 double-count fix
            Assert.Empty(profession.Quality.SlotShifts);
        }
    }
}
