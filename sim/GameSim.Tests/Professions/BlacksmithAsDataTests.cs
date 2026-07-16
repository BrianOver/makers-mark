using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Kernel;
using GameSim.Professions;

namespace GameSim.Tests.Professions;

/// <summary>
/// Proves the P1 kernel generalizes WITHOUT changing blacksmith behaviour: the blacksmith,
/// now expressed purely as a <see cref="ProfessionDefinition"/> and driven through the
/// profession-agnostic pipeline, reproduces the exact previously-hardcoded constants, the
/// exact documented quality distribution, and mints an identical item.
/// </summary>
public class BlacksmithAsDataTests
{
    private static readonly ProfessionDefinition Bs = ProfessionRegistry.Blacksmith;

    [Fact]
    public void Definition_ReproducesHardcodedConstants_AsData()
    {
        Assert.Equal(ProfessionRegistry.BlacksmithId, Bs.Id);

        // Recipes + talent nodes are the existing tables, all tagged blacksmith.
        Assert.Equal(RecipeTable.All.Count, Bs.Recipes.Count);
        Assert.All(Bs.Recipes.Values, r => Assert.Equal(ProfessionRegistry.BlacksmithId, r.Profession));
        Assert.Equal(TalentTree.Nodes.Count, Bs.TalentNodes.Count);

        // Tier gates copied from the old CraftingHandlers switch: t2 -> tier-2-smithing,
        // t3 -> tier-3-smithing, tier 1 ungated.
        Assert.Equal(TalentTree.Tier2Smithing, Bs.TierGate[2]);
        Assert.Equal(TalentTree.Tier3Smithing, Bs.TierGate[3]);
        Assert.False(Bs.TierGate.ContainsKey(1));

        // Material-efficiency node.
        Assert.Equal(TalentTree.MaterialEfficiency, Bs.MaterialEfficiencyNode);

        // Quality shift model — the exact numbers the old QualityRoller hardcoded.
        Assert.Equal(5, Bs.Quality.FlatShifts[TalentTree.KeenEye]);
        Assert.Equal(7, Bs.Quality.FlatShifts[TalentTree.MasterTouch]);
        Assert.Equal(8, Bs.Quality.FlatShifts[TalentTree.LegendaryCraft]);
        var weaponSpecialist = Bs.Quality.SlotShifts[TalentTree.WeaponSpecialist];
        Assert.Equal(ItemSlot.Weapon, weaponSpecialist.Slot);
        Assert.Equal(5, weaponSpecialist.Shift);
        Assert.Equal(TalentTree.MaterialMastery, Bs.Quality.MaterialMasteryNode);

        // Non-quality nodes never appear in the quality model.
        Assert.False(Bs.Quality.FlatShifts.ContainsKey(TalentTree.MaterialEfficiency));
        Assert.False(Bs.Quality.FlatShifts.ContainsKey(TalentTree.Tier2Smithing));
        Assert.False(Bs.Quality.SlotShifts.ContainsKey(TalentTree.MaterialEfficiency));
    }

    [Fact]
    public void Definition_CanUnlock_MirrorsTalentTreeValidation()
    {
        // The definition's profession-scoped CanUnlock is the generalized replacement the
        // presentation layer should use instead of TalentTree.CanUnlock; prove they agree for
        // every node against an empty set, plus the key prerequisite/duplicate/unknown cases.
        var none = ImmutableSortedSet<string>.Empty;

        Assert.True(Bs.CanUnlock(TalentTree.KeenEye, none));
        Assert.False(Bs.CanUnlock(TalentTree.MasterTouch, none));
        Assert.True(Bs.CanUnlock(TalentTree.MasterTouch, none.Add(TalentTree.KeenEye)));
        Assert.False(Bs.CanUnlock(TalentTree.KeenEye, none.Add(TalentTree.KeenEye)));
        Assert.False(Bs.CanUnlock("not-a-node", none));

        foreach (var node in TalentTree.Nodes.Keys)
        {
            Assert.Equal(TalentTree.CanUnlock(node, none), Bs.CanUnlock(node, none));
        }
    }

    [Fact]
    public void QualityDistribution_ThroughDefinition_MatchesGoldenCounts()
    {
        // Identical golden case to QualityRollerTests.BaseDistribution: tier-1 dagger + copper
        // (grade 1), no talents, seed 1234, 1000 rolls. Routing through the profession quality
        // model must reproduce the same byte-identical distribution.
        var rng = new Pcg32(RngState.FromSeed(1234));
        var counts = new int[5];
        for (var i = 0; i < 1000; i++)
        {
            var grade = QualityRoller.Roll(RecipeTable.All["dagger"], materialGrade: 1, ImmutableSortedSet<string>.Empty, Bs.Quality, rng);
            counts[(int)grade]++;
        }

        Assert.Equal("146,513,247,85,9", string.Join(",", counts));
        Assert.Equal(1000, counts.Sum());
    }

    [Fact]
    public void GeneralizedCraft_ThroughKernel_MintsBlacksmithItem()
    {
        var kernel = new GameKernel(ImmutableList<IPhaseSystem>.Empty, ImmutableList.Create<IActionHandler>(new CraftingHandlers()));
        var state = GameFactory.NewGame(seed: 42);
        state = state with { Player = state.Player with { Materials = state.Player.Materials.SetItem("copper", 5) } };

        var result = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new CraftAction("dagger", "copper")));

        Assert.Empty(result.Rejected);
        var item = Assert.Single(result.NewState.Items).Value;
        Assert.Equal("dagger", item.RecipeId);
        Assert.Equal(ItemSlot.Weapon, item.Slot);
        Assert.Equal("You", item.Mark!.CrafterName);
        Assert.Equal(3, result.NewState.Player.Materials["copper"]); // dagger consumes 2

        // Stats scale by the quality multiplier exactly as before (dagger base attack 8).
        var pct = ItemForge.QualityPercent(item.Quality);
        Assert.Equal(8 * pct / 100, item.Stats.Attack);
    }

    [Fact]
    public void SaveRoundTrip_PerProfessionTalents_AndSelectedProfessions_ByteIdentical()
    {
        var kernel = new GameKernel(
            ImmutableList<IPhaseSystem>.Empty,
            ImmutableList.Create<IActionHandler>(new CraftingHandlers(), new ProfessionHandlers()));

        var state = GameFactory.NewGame(seed: 7);
        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(
            new UnlockTalentAction(TalentTree.KeenEye, ProfessionRegistry.BlacksmithId),
            new SetProfessionsAction(ImmutableSortedSet.Create(ProfessionRegistry.BlacksmithId)))).NewState;
        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(
            new UnlockTalentAction(TalentTree.MasterTouch, ProfessionRegistry.BlacksmithId))).NewState;

        var json = SaveCodec.Serialize(state);
        var roundTripped = SaveCodec.Deserialize(json);

        // Serialize → deserialize → serialize is byte-identical (KTD4).
        Assert.Equal(json, SaveCodec.Serialize(roundTripped));

        var talents = roundTripped.Player.TalentsFor(ProfessionRegistry.BlacksmithId);
        Assert.Contains(TalentTree.KeenEye, talents);
        Assert.Contains(TalentTree.MasterTouch, talents);
        Assert.True(roundTripped.Player.IsSelected(ProfessionRegistry.BlacksmithId));
    }
}
