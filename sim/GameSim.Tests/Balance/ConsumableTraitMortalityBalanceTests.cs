using System.Collections.Immutable;
using GameSim;
using GameSim.Contracts;
using GameSim.Harness;
using GameSim.Heroes;

namespace GameSim.Tests.Balance;

/// <summary>
/// Phase B (B4, R-B7 close-out — fable-flagged blind spot): the 100-day Balance harness never
/// exercised the CONSUMABLE-STOCKING trait axis (Prepared/Reckless, B2/R-B5) because
/// <see cref="BaselinePlayer"/> never crafts or stocks a Heal-effect item at all — proven by
/// <see cref="SalveProvisioningBalanceTests.Baseline_NeverTouchesConsumables"/> (0 salves ever sold
/// or used on the standard gate). With nothing to buy either way, a Reckless hero's empty pack and
/// a Prepared hero's topped-up pack were mechanically IDENTICAL on every existing Balance test —
/// the Reckless-vs-Prepared survival delta the trait is supposed to model was UNMEASURED.
///
/// This test closes that gap: run the same salve-crafting-and-stocking scripted policy
/// <see cref="SalveProvisioningBalanceTests"/> already uses (so Heal items actually reach the
/// shelf, at an affordable price, every day) across an 11-seed sweep, then compare MORTALITY
/// between every hero who ever existed on that campaign (heroes are NEVER removed from the roster
/// — permadeath only flips <see cref="Hero.Alive"/>, so the starting six + every recruit are all
/// counted) grouped by their derived Consumable Stocking trait
/// (<see cref="TraitEffects.ConsumableStockTargetFor"/>): <see cref="TraitId.Reckless"/> never
/// restocks (target 0) while <see cref="TraitId.Prepared"/> restocks a little early (target 2) —
/// with salves finally available, Reckless heroes should die measurably more often.
/// </summary>
public class ConsumableTraitMortalityBalanceTests
{
    private const int Days = 100;

    /// <summary>Same affordable price <see cref="SalveProvisioningBalanceTests"/> pins.</summary>
    private const int SalvePrice = 8;

    private static readonly ulong[] Seeds =
        { 2026, 2027, 2028, 2029, 2030, 2031, 2032, 2033, 2034, 2035, 2036 };

    private sealed record TraitMortality(int RecklessTotal, int RecklessDied, int PreparedTotal, int PreparedDied);

    /// <summary>Runs the salve-stocking scenario for one seed and classifies every hero who ever
    /// existed on that campaign by their derived Consumable Stocking trait — total seen vs died
    /// (<c>!hero.Alive</c>) per side.</summary>
    private static TraitMortality Run(ulong seed)
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(seed);

        for (var tick = 0; tick < Days * 5; tick++) // 5-phase day (staged resolution)
        {
            state = kernel.Tick(state, SalveActionsFor(state)).NewState;
        }

        var recklessTotal = 0;
        var recklessDied = 0;
        var preparedTotal = 0;
        var preparedDied = 0;

        foreach (var hero in state.Heroes.Values)
        {
            var traits = TraitRegistry.TraitsFor(hero.Id, hero.Name);
            if (traits.Contains(TraitId.Reckless))
            {
                recklessTotal++;
                if (!hero.Alive)
                {
                    recklessDied++;
                }
            }
            else if (traits.Contains(TraitId.Prepared))
            {
                preparedTotal++;
                if (!hero.Alive)
                {
                    preparedDied++;
                }
            }
        }

        return new TraitMortality(recklessTotal, recklessDied, preparedTotal, preparedDied);
    }

    /// <summary>The exact scripted policy <see cref="SalveProvisioningBalanceTests.SalveActionsFor"/>
    /// uses (baseline gear/talent/ore policy, plus crafting two field-salves per Expedition window
    /// and repricing the baseline's generic 1g statless-salve stocking to an affordable price) so
    /// consumables are actually on the shelf for the traits to bite on. Duplicated rather than
    /// shared across test classes on purpose — the two scenarios are allowed to drift independently
    /// (one probes aggregate provisioning value, this one probes the trait axis).</summary>
    private static ImmutableList<PlayerAction> SalveActionsFor(GameState state)
    {
        var actions = BaselinePlayer.ActionsFor(state);

        switch (state.Phase)
        {
            case DayPhase.Morning:
                actions = actions.Select(a =>
                    a is StockAction stock
                    && state.Items.TryGetValue(stock.Item.Value, out var item)
                    && item.Effect is not null
                        ? new StockAction(stock.Item, SalvePrice)
                        : a).ToImmutableList();
                break;

            case DayPhase.Expedition:
                actions = actions
                    .Add(new CraftAction("field-salve", "copper"))
                    .Add(new CraftAction("field-salve", "copper"));
                break;
        }

        return actions;
    }

    [Fact]
    [Trait("Category", "Balance")]
    public void SalvesStocked_TraitPopulationEngagesAcrossSweep()
    {
        // Engagement guard (SalveProvisioningBalanceTests precedent): the comparison below is
        // meaningless if the sweep never produced enough Reckless/Prepared heroes to compare.
        var recklessTotal = 0;
        var preparedTotal = 0;
        foreach (var seed in Seeds)
        {
            var result = Run(seed);
            recklessTotal += result.RecklessTotal;
            preparedTotal += result.PreparedTotal;
        }

        Assert.True(recklessTotal >= 10, $"too few Reckless heroes seen across the sweep: {recklessTotal}");
        Assert.True(preparedTotal >= 10, $"too few Prepared heroes seen across the sweep: {preparedTotal}");
    }

    [Fact]
    [Trait("Category", "Balance")]
    public void SalvesStocked_RecklessHeroes_DieMeasurablyMoreThanPrepared()
    {
        var recklessTotal = 0;
        var recklessDied = 0;
        var preparedTotal = 0;
        var preparedDied = 0;

        foreach (var seed in Seeds)
        {
            var result = Run(seed);
            recklessTotal += result.RecklessTotal;
            recklessDied += result.RecklessDied;
            preparedTotal += result.PreparedTotal;
            preparedDied += result.PreparedDied;
        }

        // Integer-only mortality-rate comparison (KTD2: no floating point in sim-adjacent math) —
        // recklessDied/recklessTotal > preparedDied/preparedTotal, cross-multiplied
        // (both totals are positive per the engagement-guard test above):
        var lhs = (long)recklessDied * preparedTotal;
        var rhs = (long)preparedDied * recklessTotal;

        Assert.True(lhs > rhs,
            $"Reckless mortality ({recklessDied}/{recklessTotal}) is not measurably worse than " +
            $"Prepared ({preparedDied}/{preparedTotal}) even with salves actually stocked — the " +
            "consumable-stocking trait axis shows no survival bite when consumables are available.");
    }

    [Fact]
    [Trait("Category", "Balance")]
    public void SalvesStocked_TraitMortalityScenario_IsDeterministic()
    {
        Assert.Equal(Run(Seeds[0]), Run(Seeds[0]));
    }
}
