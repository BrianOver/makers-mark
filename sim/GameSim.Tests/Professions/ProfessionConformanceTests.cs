using System.Collections.Immutable;
using GameSim.Crafting;
using GameSim.Professions;
using Xunit;

namespace GameSim.Tests.Professions;

/// <summary>
/// The add-on conformance harness (P1): every profession in <see cref="ProfessionRegistry.All"/>
/// is validated structurally, so an add-on Claude's definition of done is mechanical — register
/// the profession and make THIS suite green (see docs/addon-guide.md). New professions get
/// covered automatically; no test edits needed here.
/// </summary>
public class ProfessionConformanceTests
{
    public static TheoryData<string> AllProfessionIds()
    {
        var data = new TheoryData<string>();
        foreach (var id in ProfessionRegistry.All.Keys)
        {
            data.Add(id);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllProfessionIds))]
    public void Identity_IdMatchesKey_AndDisplayNamePresent(string id)
    {
        var profession = ProfessionRegistry.All[id];
        Assert.Equal(id, profession.Id);
        Assert.False(string.IsNullOrWhiteSpace(profession.DisplayName));
    }

    [Theory]
    [MemberData(nameof(AllProfessionIds))]
    public void Recipes_TaggedToOwner_KnownMaterials_SaneNumbers(string id)
    {
        var profession = ProfessionRegistry.All[id];
        Assert.NotEmpty(profession.Recipes);
        foreach (var (key, recipe) in profession.Recipes)
        {
            Assert.Equal(key, recipe.RecipeId);
            Assert.Equal(id, recipe.Profession);
            Assert.True(RecipeTable.MaterialGrades.ContainsKey(recipe.MaterialKey),
                $"{id}/{recipe.RecipeId}: unknown material '{recipe.MaterialKey}'");
            Assert.True(recipe.MaterialQuantity >= 1, $"{id}/{recipe.RecipeId}: quantity < 1");
            Assert.True(recipe.Tier >= 1, $"{id}/{recipe.RecipeId}: tier < 1");
            Assert.True(recipe.BaseStats.Attack >= 0 && recipe.BaseStats.Defense >= 0 && recipe.BaseStats.Weight >= 0,
                $"{id}/{recipe.RecipeId}: negative base stat");

            // Consumable slot and ConsumableEffect must agree, and magnitudes stay sane (P2).
            Assert.True((recipe.Slot == GameSim.Contracts.ItemSlot.Consumable) == (recipe.Effect is not null),
                $"{id}/{recipe.RecipeId}: Consumable slot and Effect must be set together");
            if (recipe.Effect is { } effect)
            {
                Assert.InRange(effect.Magnitude, 1, 100);
            }
        }
    }

    [Theory]
    [MemberData(nameof(AllProfessionIds))]
    public void TalentGraph_PrereqsExist_AndAcyclic(string id)
    {
        var profession = ProfessionRegistry.All[id];
        foreach (var (key, node) in profession.TalentNodes)
        {
            Assert.Equal(key, node.NodeId);
            foreach (var prereq in node.Prerequisites)
            {
                Assert.True(profession.TalentNodes.ContainsKey(prereq),
                    $"{id}/{node.NodeId}: prerequisite '{prereq}' is not a node of this profession");
            }
        }

        // Every node must be reachable by repeatedly unlocking satisfiable nodes — this
        // proves the prerequisite graph is acyclic AND nothing is permanently locked.
        var unlocked = ImmutableSortedSet<string>.Empty;
        var progressed = true;
        while (progressed && unlocked.Count < profession.TalentNodes.Count)
        {
            progressed = false;
            foreach (var nodeId in profession.TalentNodes.Keys)
            {
                if (!unlocked.Contains(nodeId) && profession.CanUnlock(nodeId, unlocked))
                {
                    unlocked = unlocked.Add(nodeId);
                    progressed = true;
                }
            }
        }

        Assert.Equal(profession.TalentNodes.Count, unlocked.Count);
    }

    [Theory]
    [MemberData(nameof(AllProfessionIds))]
    public void ReferencedNodes_TierGates_Efficiency_QualityShifts_AllExist(string id)
    {
        var profession = ProfessionRegistry.All[id];
        foreach (var (tier, gateNode) in profession.TierGate)
        {
            Assert.True(tier >= 2, $"{id}: tier gate on tier {tier} — tier 1 must be ungated");
            Assert.True(profession.TalentNodes.ContainsKey(gateNode),
                $"{id}: tier-{tier} gate '{gateNode}' is not a node");
        }

        if (profession.MaterialEfficiencyNode is { } efficiency)
        {
            Assert.True(profession.TalentNodes.ContainsKey(efficiency),
                $"{id}: material-efficiency node '{efficiency}' is not a node");
        }

        foreach (var (nodeId, shift) in profession.Quality.FlatShifts)
        {
            Assert.True(profession.TalentNodes.ContainsKey(nodeId), $"{id}: flat-shift node '{nodeId}' is not a node");
            Assert.InRange(shift, 1, 25);
        }

        foreach (var (nodeId, slotShift) in profession.Quality.SlotShifts)
        {
            Assert.True(profession.TalentNodes.ContainsKey(nodeId), $"{id}: slot-shift node '{nodeId}' is not a node");
            Assert.InRange(slotShift.Shift, 1, 25);
        }

        if (profession.Quality.MaterialMasteryNode is { } mastery)
        {
            Assert.True(profession.TalentNodes.ContainsKey(mastery),
                $"{id}: material-mastery node '{mastery}' is not a node");
        }
    }

    [Fact]
    public void RecipeIds_GloballyUnique_AcrossAllProfessions()
    {
        // AllRecipes construction throws on duplicates at static init; this makes the
        // contract explicit and readable in a test failure instead of a type initializer error.
        var total = ProfessionRegistry.All.Values.Sum(p => p.Recipes.Count);
        Assert.Equal(total, ProfessionRegistry.AllRecipes.Count);
    }
}
