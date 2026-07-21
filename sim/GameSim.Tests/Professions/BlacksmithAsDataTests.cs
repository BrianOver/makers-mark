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

        // PA2/PKD2/PKD3: blacksmith flips to the ACTIVE dominance model — the retired
        // quality-shift nodes no longer touch the roll at all (double-count resolved),
        // so FlatShifts/SlotShifts are EMPTY. Material-mastery is KEPT (no overlap with
        // the minigame — it still raises the roll's ceiling).
        Assert.True(Bs.ActiveCraft);
        Assert.Empty(Bs.Quality.FlatShifts);
        Assert.Empty(Bs.Quality.SlotShifts);
        Assert.Equal(TalentTree.MaterialMastery, Bs.Quality.MaterialMasteryNode);

        // The four retired quality nodes remap 1:1 to non-degenerate minigame-assist data
        // for the PA6 forge-overlay adapter to read (pure data — the sim never interprets it).
        Assert.Equal(4, Bs.MinigameAssists.Count);
        foreach (var nodeId in new[] { TalentTree.KeenEye, TalentTree.MasterTouch, TalentTree.LegendaryCraft, TalentTree.WeaponSpecialist })
        {
            var assist = Bs.MinigameAssists[nodeId];
            Assert.True(assist.SweetZoneWidthBonus > 0 || assist.DriftRateReduction > 0 || assist.OffBeatForgiveness > 0,
                $"{nodeId}: minigame-assist data is degenerate (all zero)");
        }
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
        // PA2: blacksmith is ACTIVE now, so the real production path is RollActive, not the
        // passive Roll(). Tier-1 dagger + copper (grade 1, materialStep = 0), no PerformanceGrade
        // submitted (auto-craft, PKD4), seed 1234, 1000 rolls. Auto-craft resolves at the
        // competent constant (550) ± jitter, which straddles only the Common/Fine seam at
        // materialStep 0 — never Poor, Superior, or Masterwork.
        var rng = new Pcg32(RngState.FromSeed(1234));
        var counts = new int[5];
        for (var i = 0; i < 1000; i++)
        {
            var grade = QualityRoller.RollActive(RecipeTable.All["dagger"], materialGrade: 1, ImmutableSortedSet<string>.Empty, Bs.Quality, rng, performanceGrade: null);
            counts[(int)grade]++;
        }

        Assert.Equal("0,487,513,0,0", string.Join(",", counts));
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
