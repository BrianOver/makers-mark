using System;
using System.Collections.Immutable;
using System.Linq;
using GameSim;
using GameSim.Contracts;
using GameSim.Kernel;
using GameSim.Professions;
using GameSim.Progression;
using Xunit;

namespace GameSim.Tests.Progression;

/// <summary>
/// U-D4: the multi-axis progression spine. The plan's bar — "each ladder exposes a next rung" — plus
/// the Travellers-Rest fix (the Chronicle axis is unbounded, so the tree never ends), the cross-feed
/// shape, and the purity guarantee (a read-only derivation that never moves state).
/// </summary>
public class ProgressionSpineTests
{
    private static GameState Fresh() => GameComposition.NewCampaign(9001);

    [Fact]
    public void Compute_EmitsAllFiveAxes_InOrder()
    {
        var spine = ProgressionSpineSystem.Compute(Fresh());

        Assert.Equal(
            new[]
            {
                ProgressionAxis.Forge, ProgressionAxis.Depth, ProgressionAxis.Roster,
                ProgressionAxis.Wealth, ProgressionAxis.Chronicle,
            },
            spine.Rungs.Select(r => r.Axis));
    }

    [Fact]
    public void EveryLadder_ExposesANextRung()
    {
        // The plan's headline requirement: no ladder is ever a dead end — each shows the player
        // something concrete to aim at, on a fresh campaign and after progress alike.
        var fresh = ProgressionSpineSystem.Compute(Fresh());
        Assert.All(fresh.Rungs, r =>
        {
            Assert.False(string.IsNullOrWhiteSpace(r.Current));
            Assert.False(string.IsNullOrWhiteSpace(r.NextRung));
            Assert.False(string.IsNullOrWhiteSpace(r.Feeds));
        });
    }

    [Fact]
    public void Chronicle_IsUnbounded_AndAlwaysHasANextRung_EvenAtFiniteCeilings()
    {
        // Push every finite ladder to its ceiling: all forge gates unlocked, a hero at the wall.
        var state = Fresh();
        var bs = ProfessionRegistry.Blacksmith;
        var player = state.Player;
        foreach (var node in bs.TierGate.Values)
        {
            player = player.WithTalent(bs.Id, node);
        }

        var firstHero = state.Heroes.Values.First();
        var maxedHeroes = state.Heroes.SetItem(
            firstHero.Id.Value,
            firstHero with { DeepestFloorReached = GameSim.Expedition.MonsterTable.FloorCount });

        var ceiling = state with { Player = player, Heroes = maxedHeroes };
        var spine = ProgressionSpineSystem.Compute(ceiling);

        Assert.Equal(1000, spine[ProgressionAxis.Forge].ProgressPermille);
        Assert.Equal(1000, spine[ProgressionAxis.Depth].ProgressPermille);
        Assert.False(spine[ProgressionAxis.Forge].Unbounded);

        var chronicle = spine[ProgressionAxis.Chronicle];
        Assert.True(chronicle.Unbounded);
        Assert.False(string.IsNullOrWhiteSpace(chronicle.NextRung));
    }

    [Fact]
    public void Forge_NextRung_NamesTheNextLockedTier_WhenTalentsEmpty()
    {
        var spine = ProgressionSpineSystem.Compute(Fresh());
        var forge = spine[ProgressionAxis.Forge];

        // Fresh blacksmith has no talents → the lowest locked gate (tier 2) is the next rung.
        var lowestGate = ProfessionRegistry.Blacksmith.TierGate.First();
        Assert.Contains($"tier {lowestGate.Key}", forge.NextRung);
        Assert.Contains(lowestGate.Value, forge.NextRung);
    }

    [Fact]
    public void Forge_Advances_WhenAGateIsUnlocked()
    {
        var state = Fresh();
        var bs = ProfessionRegistry.Blacksmith;
        var before = ProgressionSpineSystem.Compute(state)[ProgressionAxis.Forge];

        var firstGate = bs.TierGate.First().Value;
        var after = ProgressionSpineSystem.Compute(
            state with { Player = state.Player.WithTalent(bs.Id, firstGate) })[ProgressionAxis.Forge];

        // Unlocking a gate must move the ladder forward: strictly more progress than before.
        Assert.True((after.ProgressPermille ?? 0) > (before.ProgressPermille ?? 0));
    }

    [Fact]
    public void Depth_NextRung_IsFloorOne_OnAFreshCampaign()
    {
        var depth = ProgressionSpineSystem.Compute(Fresh())[ProgressionAxis.Depth];
        Assert.Contains("Floor 1", depth.NextRung);
    }

    [Fact]
    public void Wealth_NextRung_ReferencesTheGuildAssessment()
    {
        var wealth = ProgressionSpineSystem.Compute(Fresh())[ProgressionAxis.Wealth];
        Assert.Contains("assessment", wealth.NextRung, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"{Fresh().Assessment.DuesGold}g", wealth.NextRung);
    }

    [Fact]
    public void Compute_IsPure_LeavesStateUntouched_AndIsDeterministic()
    {
        var state = Fresh();
        var snapshot = SaveCodec.Serialize(state);

        var a = ProgressionSpineSystem.Compute(state);
        var b = ProgressionSpineSystem.Compute(state);

        // Read-only: the derivation never mutates the state it reads (golden-neutral) ...
        Assert.Equal(snapshot, SaveCodec.Serialize(state));
        // ... and is deterministic: same state in, identical spine out.
        Assert.Equal(a.Rungs, b.Rungs);
    }
}
