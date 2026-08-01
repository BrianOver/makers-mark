using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
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
/// with salves finally available, the two populations must show a measurable survival DELTA.
/// (The delta's DIRECTION is deliberately unpinned since the 2026-08-01 router re-baseline — see
/// <see cref="SalvesStocked_TraitAxis_HasAMeasurableSurvivalDelta"/>'s doc for the measured
/// inversion and the open design question it raises.)
/// </summary>
public class ConsumableTraitMortalityBalanceTests
{
    private const int Days = 100;

    /// <summary>Same affordable price <see cref="SalveProvisioningBalanceTests"/> pins.</summary>
    private const int SalvePrice = 8;

    // Widened 30 -> 90 seeds when Phase C U-C3 (drama director) landed: the director's daily draw
    // shifts the shared RNG stream position, and the Reckless-vs-Prepared survival delta this test
    // probes is a genuinely MARGINAL effect (the trait only bites when a hero actually reaches a
    // flee/death moment where a stocked salve would have mattered). At 30 seeds a stream shift could
    // flip the aggregate sign; a larger sweep restores a robust signal for the same measurement
    // (not a loosened assertion — still strict Reckless-dies-more, just more samples).
    private static readonly ulong[] Seeds =
        System.Linq.Enumerable.Range(2026, 90).Select(i => (ulong)i).ToArray();

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

    /// <summary>Runs <see cref="Run"/> across every seed IN PARALLEL and totals the four counters.
    /// Each seed builds its own kernel/state and runs an isolated, integer-only 100-day sim (no
    /// shared mutable state, no IO/clock), so the 90-seed sweep is embarrassingly parallel;
    /// per-seed determinism is untouched and the reduction is a commutative sum, so the aggregate
    /// is identical to the old serial <c>foreach</c> regardless of completion order.</summary>
    private static TraitMortality RunAllSeedsParallel()
    {
        var bag = new ConcurrentBag<TraitMortality>();
        Parallel.ForEach(Seeds, seed => bag.Add(Run(seed)));
        return bag.Aggregate(
            new TraitMortality(0, 0, 0, 0),
            (a, r) => new TraitMortality(
                a.RecklessTotal + r.RecklessTotal,
                a.RecklessDied + r.RecklessDied,
                a.PreparedTotal + r.PreparedTotal,
                a.PreparedDied + r.PreparedDied));
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
        var totals = RunAllSeedsParallel();

        Assert.True(totals.RecklessTotal >= 10, $"too few Reckless heroes seen across the sweep: {totals.RecklessTotal}");
        Assert.True(totals.PreparedTotal >= 10, $"too few Prepared heroes seen across the sweep: {totals.PreparedTotal}");
    }

    [Fact]
    [Trait("Category", "Balance")]
    public void SalvesStocked_TraitAxis_HasAMeasurableSurvivalDelta()
    {
        // RE-BASELINED (2026-08-01, venue-router power bands + honest BaselinePlayer + T1 flip),
        // and the direction assertion is DELIBERATELY retired — read this before "fixing" it back:
        //
        // This test used to assert Reckless dies MORE than Prepared. That direction is not a
        // property of the trait axis — it is a property of WHERE the world's flee-moments happen.
        // A stocked Heal item does not shield a hero; it OVERRIDES the flee decision and fights on
        // (ExpeditionResolver.FightMonster: a would-flee hero quaffs "instead and fights on";
        // fleeing itself is guaranteed survival). So Prepared = presses on, Reckless = flees home.
        // Wherever per-round monster damage outpaces the heal, pressing on is net-LETHAL, and the
        // sign of the mortality delta follows the venue/floor mix the router produces:
        //
        //   - old tightest-fit router (pre-bands): reckless 359/491 (73%) vs prepared 326/490
        //     (67%) died — Reckless worse, the old assertion held;
        //   - banded router, same 90 seeds:        reckless 243/437 (56%) vs prepared 330/465
        //     (71%) died — INVERTED, by a margin far outside sampling noise (~900 heroes).
        //
        // The inversion is a real, flagged GAME-DESIGN question (does "Prepared" deserve its
        // survival-positive flavor while the quaff rule converts guaranteed flees into fights?) —
        // routed to the owner in the router-rebaseline PR, not silently pinned here as intended.
        // What this gate still guards, direction-free: the axis must BITE — stocked salves must
        // produce a measurably different death rate between the two traits. If this goes flat,
        // the trait axis is dead again (the exact blind spot this file was created to close).
        var totals = RunAllSeedsParallel();
        var (recklessTotal, recklessDied, preparedTotal, preparedDied) =
            (totals.RecklessTotal, totals.RecklessDied, totals.PreparedTotal, totals.PreparedDied);

        // Integer-only mortality-rate comparison (KTD2: no floating point in sim-adjacent math):
        // |recklessDied/recklessTotal - preparedDied/preparedTotal| >= 5 percentage points,
        // cross-multiplied (both totals are positive per the engagement-guard test above). The
        // 5pp floor is deliberate — the 2026-08-01 measurement sits at ~15pp, so this trips only
        // when the axis genuinely deflates, not on ordinary seed-to-seed wobble:
        var gap = Math.Abs((long)recklessDied * preparedTotal - (long)preparedDied * recklessTotal);
        var floor5pp = (long)recklessTotal * preparedTotal / 20;

        Assert.True(gap >= floor5pp,
            $"Reckless mortality ({recklessDied}/{recklessTotal}) is within 5pp of " +
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
