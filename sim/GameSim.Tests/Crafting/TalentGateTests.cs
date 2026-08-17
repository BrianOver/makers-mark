using System.Collections.Immutable;
using GameSim.Advisor;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Economy;
using GameSim.Kernel;
using GameSim.Professions;

namespace GameSim.Tests.Crafting;

/// <summary>
/// U-T1-9 (register #157, owner ruling "Forge Tier plus an action slot" — a talent-point economy
/// and a calendar gate were both explicitly rejected): before this unit, 22 blacksmith recipes sat
/// behind two FREE clicks — <see cref="TalentTree.Tier2Smithing"/> had no prerequisites and
/// <see cref="TalentTree.Tier3Smithing"/> needed only tier-2, and <see cref="CraftingHandlers.Apply"/>
/// spent no action slot and asked no Forge Tier question. This file covers the two gates that close
/// that hole: a Forge Tier prerequisite on the two gate nodes (read from
/// <see cref="TalentTree.ForgeTierRequirement"/>, resolved through
/// <see cref="ForgeTierHandlers.CurrentTierIndex"/>), and an action-slot cost on every unlock,
/// checked last like every other real-work handler.
/// </summary>
public class TalentGateTests
{
    private static readonly GameKernel Kernel = new(
        ImmutableList<IPhaseSystem>.Empty,
        ImmutableList.Create<IActionHandler>(new CraftingHandlers()));

    private const string Blacksmith = ProfessionRegistry.BlacksmithId;

    [Fact]
    public void UnlockingATalent_SpendsAnActionSlot()
    {
        var state = GameFactory.NewGame(seed: 1);
        var before = state.ActionSlotsRemaining;

        var result = Kernel.Tick(state, ImmutableList.Create<PlayerAction>(
            new UnlockTalentAction(TalentTree.KeenEye, Blacksmith)));

        Assert.Empty(result.Rejected);
        Assert.Equal(before - 1, result.NewState.ActionSlotsRemaining);
        Assert.Contains(TalentTree.KeenEye, result.NewState.Player.TalentsFor(Blacksmith));
    }

    [Fact]
    public void UnlockingTierTwoSmithing_RequiresForgeTierTwo()
    {
        // Fresh game: Forge Tier I (index 0), no gold/ore spent on an upgrade — the tier-2 gate's
        // only remaining prerequisite (none) is trivially met, so the Forge Tier check is the one
        // thing standing in the way.
        var state = GameFactory.NewGame(seed: 1);

        var result = Kernel.Tick(state, ImmutableList.Create<PlayerAction>(
            new UnlockTalentAction(TalentTree.Tier2Smithing, Blacksmith)));

        var rejected = Assert.Single(result.Rejected);
        Assert.Contains("Forge Tier 2", rejected.Reason); // names the tier, not just "locked"
        Assert.DoesNotContain(TalentTree.Tier2Smithing, result.NewState.Player.TalentsFor(Blacksmith));
        Assert.Equal(state.ActionSlotsRemaining, result.NewState.ActionSlotsRemaining); // rejected before the slot gate too

        // Once the workshop is actually at Forge Tier II, the same unlock succeeds.
        var upgraded = state with
        {
            Player = state.Player with { Materials = state.Player.Materials.SetItem(ForgeTierHandlers.ForgeTierKey, 1) },
        };
        var succeeded = Kernel.Tick(upgraded, ImmutableList.Create<PlayerAction>(
            new UnlockTalentAction(TalentTree.Tier2Smithing, Blacksmith)));

        Assert.Empty(succeeded.Rejected);
        Assert.Contains(TalentTree.Tier2Smithing, succeeded.NewState.Player.TalentsFor(Blacksmith));
    }

    /// <summary>
    /// The bug register #157 named, pinned as a permanent regression test: from a new game, apply
    /// every unlock <see cref="ActionLegality"/> currently reports legal — exactly what mashing
    /// every unlock button once does — and every tier-2/tier-3 recipe must still be rejected
    /// afterward. Iterates the real recipe registry (<see cref="RecipeTable.All"/>), never a
    /// literal id array, so a future recipe addition is covered the day it ships.
    /// </summary>
    [Fact]
    public void TwoFreeClicksOnDayOne_NoLongerUnlockEveryCoreRecipe()
    {
        var state = GameFactory.NewGame(seed: 1);

        // Apply whatever unlock is legal right now, repeatedly, until nothing legal remains for the
        // day — a player mashing every unlock button once, in whatever order the UI offers them.
        // ApplyNow (not Tick): a same-day sequence of individual workshop verbs, no phase advance.
        bool appliedOne;
        do
        {
            appliedOne = false;
            foreach (var node in TalentTree.Nodes.Values)
            {
                var unlock = new UnlockTalentAction(node.NodeId, Blacksmith);
                if (!ActionLegality.IsLegal(state, unlock, state.Phase))
                {
                    continue;
                }

                var result = Kernel.ApplyNow(state, unlock);
                Assert.Empty(result.Rejected);
                state = result.NewState;
                appliedOne = true;
            }
        } while (appliedOne);

        // Every tier-2/tier-3 node must still be locked — the whole point of the gate.
        var talents = state.Player.TalentsFor(Blacksmith);
        Assert.DoesNotContain(TalentTree.Tier2Smithing, talents);
        Assert.DoesNotContain(TalentTree.Tier3Smithing, talents);

        // Stock every material grade generously so a tier-2/3 craft is blocked ONLY by the talent
        // gate, never by a material shortfall.
        var stocked = state.Player.Materials;
        foreach (var key in RecipeTable.MaterialGrades.Keys)
        {
            stocked = stocked.SetItem(key, 1000);
        }

        state = state with { Player = state.Player with { Materials = stocked } };

        var gatedRecipes = RecipeTable.All.Values.Where(r => r.Tier is 2 or 3).ToList();
        Assert.NotEmpty(gatedRecipes); // fixture-assumption guard: a broken filter would pass vacuously

        foreach (var recipe in gatedRecipes)
        {
            var craft = new CraftAction(recipe.RecipeId, recipe.MaterialKey);
            var result = Kernel.ApplyNow(state, craft);
            Assert.NotEmpty(result.Rejected);
            Assert.False(ActionLegality.IsLegal(state, craft, state.Phase));
        }
    }
}
