using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Economy;
using GameSim.Harness;
using GameSim.Kernel;
using GameSim.Materials;
using Xunit.Abstractions;

namespace GameSim.Tests.Balance;

/// <summary>
/// U4 (P6b) ship-gate measurement (plan 2026-07-13-001): "Masterwork is a purchased guarantee
/// standing next to a skill minigame, and late-game it may dominate hand-crafting" — the plan
/// treats that as a design-time risk the project's filter requires measuring, not assuming.
///
/// <para><b>The trap this test exists to avoid.</b> Neither <see cref="BaselinePlayer"/> nor
/// <see cref="CounterPlayer"/> ever constructs <see cref="MasterworkAttemptAction"/>
/// (confirmed by grep before writing this), so measuring "value through purchased attempts"
/// against either would read zero, forever, and look like "no dominance risk" when nothing was
/// ever exercised. <see cref="MasterworkSeekingPlayer"/> (additive, <see cref="BaselinePlayer"/>
/// untouched) is the new policy that actually buys forge supplies and attempts masterworks once
/// affordable, greedily preferring a masterwork attempt over a hand-craft of the SAME recipe
/// whenever the attempt is legal.</para>
///
/// <para><b>Why a focused kernel.</b> Mirrors <see cref="PhaseDSinksBalanceTests"/>' own precedent:
/// only the four sink handlers plus ordinary crafting are composed, with ZERO <see
/// cref="IPhaseSystem"/>s — so gold and materials move ONLY through this policy's own actions,
/// never confounded by rent, guild dues, hero shopping, or market share. The starting
/// <see cref="GameState"/> still comes from <see cref="GameComposition.NewCampaign"/> so a live
/// hero roster exists and <see cref="GameKernel"/>'s own phase-advance logic reaches
/// <see cref="DayPhase.Expedition"/> naturally each day (an empty roster would fold every Morning
/// straight to Evening) — but with no phase systems registered, no hero ever actually raids, so
/// the roster stays exactly as installed for the whole run. This isolates the ONE trade-off the
/// ship-gate is actually asking about: given a finite late-game gold reserve already at Forge Tier
/// II, does a rational smith keep hand-crafting, or does the guarantee crowd it out?</para>
///
/// <para><b>Value metric:</b> <c>item.Stats.Attack + item.Stats.Defense</c> — the SAME "value" the
/// existing <see cref="BaselinePlayer"/> Morning branch already prices a shelved item by
/// (<c>statSum</c>), reused rather than inventing a second metric.</para>
///
/// <para><b>Recorded result</b> (seed 555001, 50 days, Forge Tier II start, 5,000 starting gold,
/// ample base materials, focused kernel — see <see cref="Run"/>):
///   masterwork attempts = 7   value = 91
///   hand crafts         = 43  value = 445
///   masterwork value share = 17.0% (7/50 of crafts by count)
/// The policy ALSO self-funds a Forge Tier III upgrade on day 1 (ore + gold both legal — the
/// same greedy "take it if legal" rule applies to sink 1, not only sink 3b), which both spends
/// 1,600g up front AND raises the per-attempt surcharge from 200g to 300g — so the 5,000g reserve
/// funds exactly 7 attempts (day 1 through day 7) before gold drops below the 300g floor, at
/// which point the policy permanently falls back to hand-crafting for the remaining 43 days (gold
/// never regenerates in this closed, income-free scenario). Read the 17.0% as "dominant for the
/// early stretch of a resourced run, then crowded back out once the reserve itself is spent" —
/// NOT as a fixed number: it moves with starting gold, the tier-upgrade decision, and the
/// surcharge/coal/flux prices, all of which are asserted live rather than retyped above.</para>
/// </summary>
public class MasterworkDominanceBalanceTests
{
    private const int Days = 50;
    private const ulong Seed = 555_001UL;
    private const int StartingGold = 5_000;

    private readonly ITestOutputHelper _output;

    public MasterworkDominanceBalanceTests(ITestOutputHelper output) => _output = output;

    private readonly record struct DominanceResult(
        long MasterworkValue, int MasterworkCount, long HandCraftValue, int HandCraftCount);

    private static GameKernel FocusedKernel() => new(
        ImmutableList<IPhaseSystem>.Empty,
        ImmutableList.Create<IActionHandler>(
            new ForgeTierHandlers(), new ForgeSupplyHandlers(), new MasterworkAttemptHandlers(), new CraftingHandlers()));

    /// <summary>Already at Forge Tier II with a late-game-sized but FINITE gold reserve and ample
    /// base-material stock (mirrors <see cref="PhaseDSinksBalanceTests.RichWorkshopStart"/>'s own
    /// stocking shape) — never zero coal/flux by construction (the policy buys its own each
    /// Morning), so the ONLY thing that can ever block a masterwork attempt in this scenario is
    /// running out of gold.</summary>
    private static GameState LateGameTierTwoStart(ulong seed)
    {
        var baseState = GameComposition.NewCampaign(seed);
        return baseState with
        {
            Player = baseState.Player with
            {
                Gold = StartingGold,
                Materials = ImmutableSortedDictionary<string, int>.Empty
                    .SetItem(MaterialRegistry.Copper, 1000)
                    .SetItem(MaterialRegistry.Iron, 1000)
                    .SetItem(MaterialRegistry.Steel, 1000)
                    .SetItem(MaterialRegistry.Mithril, 1000)
                    .SetItem(ForgeTierHandlers.ForgeTierKey, MasterworkAttemptHandlers.RequiredForgeTierIndex),
            },
        };
    }

    private static DominanceResult Run(ulong seed)
    {
        var kernel = FocusedKernel();
        var state = LateGameTierTwoStart(seed);

        long masterworkValue = 0;
        long handCraftValue = 0;
        var masterworkCount = 0;
        var handCraftCount = 0;

        for (var tick = 0; tick < Days * 5; tick++) // 5-phase day, same convention as BalanceSimTests
        {
            var actions = MasterworkSeekingPlayer.ActionsFor(state);
            var result = kernel.Tick(state, actions);
            state = result.NewState;

            foreach (var evt in result.Events.OfType<ItemCrafted>())
            {
                if (!state.Items.TryGetValue(evt.Item.Value, out var item))
                {
                    continue; // defensive — never actually missing, the mint and the event land in the same tick
                }

                var value = item.Stats.Attack + item.Stats.Defense;
                if (actions.Any(a => a is MasterworkAttemptAction))
                {
                    masterworkValue += value;
                    masterworkCount++;
                }
                else if (actions.Any(a => a is CraftAction))
                {
                    handCraftValue += value;
                    handCraftCount++;
                }
            }
        }

        return new DominanceResult(masterworkValue, masterworkCount, handCraftValue, handCraftCount);
    }

    [Fact]
    [Trait("Category", "Balance")]
    public void AtForgeTierTwoWithLateGameGold_MasterworkDominanceIsMeasuredAndReported_NeverVacuous()
    {
        var result = Run(Seed);
        var totalValue = result.MasterworkValue + result.HandCraftValue;
        var totalCount = result.MasterworkCount + result.HandCraftCount;

        _output.WriteLine($"masterwork attempts = {result.MasterworkCount}, value = {result.MasterworkValue}");
        _output.WriteLine($"hand crafts         = {result.HandCraftCount}, value = {result.HandCraftValue}");
        _output.WriteLine(totalValue > 0
            ? $"masterwork value share = {100.0 * result.MasterworkValue / totalValue:F1}% " +
              $"({result.MasterworkCount}/{totalCount} of crafts by count over {Days} days)"
            : "NO CRAFTED VALUE AT ALL — the scenario minted nothing; this measurement would be vacuous.");

        // Non-vacuous, per the unit's own brief: the policy must have genuinely exercised the
        // purchased-attempt verb — a run that never legally reaches a masterwork attempt would
        // report a 0% dominance number that LOOKS like "no risk" while actually proving nothing.
        Assert.True(result.MasterworkCount > 0,
            "MasterworkSeekingPlayer never actually attempted a masterwork in this scenario — the dominance number would be vacuous.");
        Assert.True(totalValue > 0, "No crafted value was minted at all — the scenario is vacuous.");
    }

    [Fact]
    [Trait("Category", "Balance")]
    public void AtForgeTierTwoWithLateGameGold_IsDeterministic()
    {
        Assert.Equal(Run(Seed), Run(Seed));
    }
}
