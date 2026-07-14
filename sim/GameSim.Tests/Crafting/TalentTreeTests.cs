using System.Collections.Immutable;
using GameSim.Crafting;

namespace GameSim.Tests.Crafting;

public class TalentTreeTests
{
    private static readonly ImmutableSortedSet<string> None = ImmutableSortedSet<string>.Empty;

    [Fact]
    public void Tree_HasExactlyEightNodes_WithStableIds()
    {
        Assert.Equal(8, TalentTree.Nodes.Count);
        Assert.Equal(
            new[]
            {
                TalentTree.KeenEye,
                TalentTree.LegendaryCraft,
                TalentTree.MasterTouch,
                TalentTree.MaterialEfficiency,
                TalentTree.MaterialMastery,
                TalentTree.Tier2Smithing,
                TalentTree.Tier3Smithing,
                TalentTree.WeaponSpecialist,
            }.OrderBy(x => x, StringComparer.Ordinal),
            TalentTree.Nodes.Keys);
    }

    [Fact]
    public void EveryPrerequisite_ReferencesAnExistingNode()
    {
        foreach (var node in TalentTree.Nodes.Values)
        {
            foreach (var prereq in node.Prerequisites)
            {
                Assert.True(TalentTree.Nodes.ContainsKey(prereq), $"{node.NodeId} requires unknown node '{prereq}'");
            }
        }
    }

    [Fact]
    public void EveryNode_HasNameAndDescription()
    {
        foreach (var node in TalentTree.Nodes.Values)
        {
            Assert.False(string.IsNullOrWhiteSpace(node.Name));
            Assert.False(string.IsNullOrWhiteSpace(node.Description));
        }
    }

    [Fact]
    public void CanUnlock_RootNodes_WithEmptySet()
    {
        Assert.True(TalentTree.CanUnlock(TalentTree.KeenEye, None));
        Assert.True(TalentTree.CanUnlock(TalentTree.MaterialEfficiency, None));
        Assert.True(TalentTree.CanUnlock(TalentTree.Tier2Smithing, None));
    }

    [Fact]
    public void CanUnlock_EnforcesPrerequisites()
    {
        // master-touch requires keen-eye.
        Assert.False(TalentTree.CanUnlock(TalentTree.MasterTouch, None));
        Assert.True(TalentTree.CanUnlock(TalentTree.MasterTouch, None.Add(TalentTree.KeenEye)));

        // legendary-craft requires master-touch (deep chain).
        Assert.False(TalentTree.CanUnlock(TalentTree.LegendaryCraft, None.Add(TalentTree.KeenEye)));
        Assert.True(TalentTree.CanUnlock(TalentTree.LegendaryCraft, None.Add(TalentTree.KeenEye).Add(TalentTree.MasterTouch)));

        // tier-3-smithing requires tier-2-smithing.
        Assert.False(TalentTree.CanUnlock(TalentTree.Tier3Smithing, None));
        Assert.True(TalentTree.CanUnlock(TalentTree.Tier3Smithing, None.Add(TalentTree.Tier2Smithing)));

        // material-mastery requires material-efficiency.
        Assert.False(TalentTree.CanUnlock(TalentTree.MaterialMastery, None));
        Assert.True(TalentTree.CanUnlock(TalentTree.MaterialMastery, None.Add(TalentTree.MaterialEfficiency)));

        // weapon-specialist requires keen-eye.
        Assert.False(TalentTree.CanUnlock(TalentTree.WeaponSpecialist, None));
        Assert.True(TalentTree.CanUnlock(TalentTree.WeaponSpecialist, None.Add(TalentTree.KeenEye)));
    }

    [Fact]
    public void CanUnlock_False_ForUnknownNode()
    {
        Assert.False(TalentTree.CanUnlock("not-a-node", None));
    }

    [Fact]
    public void CanUnlock_False_WhenAlreadyUnlocked()
    {
        Assert.False(TalentTree.CanUnlock(TalentTree.KeenEye, None.Add(TalentTree.KeenEye)));
    }
}
