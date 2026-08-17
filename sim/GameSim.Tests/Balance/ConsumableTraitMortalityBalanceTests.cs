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
/// with salves finally available, Prepared heroes must die measurably LESS than Reckless ones.
/// (That direction was inverted until the 2026-08-01 quaff-ordering fix — see
/// <see cref="SalvesStocked_PreparedHeroes_SurviveMeasurablyBetterThanReckless"/>'s doc for the
/// full measurement history and the structural cause.)
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
    public void SalvesStocked_PreparedHeroes_SurviveMeasurablyBetterThanReckless()
    {
        // THE DIRECTION IS THE POINT (owner ruling 2026-08-01: "prefer more prepared heroes").
        // Preparation must read as insurance: a Prepared hero should die LESS than a Reckless
        // one. That was not true before this window, and the history is worth keeping because
        // it explains what the assertion is actually protecting:
        //
        //   - old tightest-fit router:              reckless 359/491 (73%) vs prepared 326/490
        //     (67%) — accidentally correct, and the original test pinned that accident;
        //   - banded router, gloomwood 55, 4-venue: reckless 243/437 (56%) vs prepared 330/465
        //     (71%) — INVERTED by ~15pp: preparation was actively LETHAL;
        //   - banded router, gloomwood 72, 3-venue: reckless 553/684 (81%) vs prepared 551/699
        //     (79%) — inversion gone but the axis ~inert, ~2pp, inside noise.
        //
        // The cause was structural, not tuning: a stocked Heal item OVERRODE the flee decision
        // and fought on, and fleeing is guaranteed survival — so carrying a salve swapped a safe
        // exit for a fight the hero could lose, and the trait's sign followed wherever the
        // router put the world's flee-moments. Fixed in ExpeditionResolver.FightMonster: flee is
        // checked FIRST and is never cancelled; a salve is drunk while the hero is still ABOVE
        // the flee line — when merely wounded (CombatMath.ShouldDrink) or, decisively, when the
        // monster's worst-case next blow could kill (CombatMath.CouldDieNextRound). A plain
        // wounded-% line alone measured just 0.9pp on an independent seed block; the
        // lethal-risk clause is what makes preparation actually protective.
        //
        // Measured after the fix, two INDEPENDENT 90-seed blocks (the second is not this test's
        // sweep — a fluke on one block cannot produce both):
        //   seeds 2026..2115: reckless 376/470 (80.0%) vs prepared 292/463 (63.1%) — 16.9pp
        //   seeds 5000..5089: reckless 355/463 (76.7%) vs prepared 328/496 (66.1%) — 10.6pp
        // Prepared heroes are better off, not immortal: they still die ~2/3 of the time across
        // 100 days, so the trait buys real insurance, not invulnerability.
        //
        // The gate below asserts the DIRECTION with a >=5pp margin, deliberately well under the
        // measured 10.6pp so ordinary seed wobble never trips it — but any change that flattens
        // the axis or flips it lethal again fails here, loudly.
        //
        // RE-BASELINE HISTORY (2026-08-17, link2 fix, "consumables get drunk before the hero
        // dies"): register #157's own branch (U-T1-9, not yet merged) composed with U-T1-11
        // pushed this 2.7pp BELOW zero — Prepared 62.2% (234/376) vs Reckless 59.5% (206/346),
        // the fiction genuinely backwards, not just a narrower correct-direction gap (an
        // apparent-margin reading that fooled a first pass at this exact number — which is
        // exactly why the direction is asserted SEPARATELY from the margin below). Root cause,
        // found by instrumenting every death carrying an unused-or-just-drunk Heal item across a
        // 90-seed sweep: 6 of 6 recorded deaths were a hero who quaffed correctly (wounded check
        // fired, TryQuaff fired) but whose single field-salve's Magnitude wasn't enough to clear
        // that same round's worst-case hit — CouldDieNextRound never asked "would drinking
        // actually get me clear," only "am I in danger," so the salve got burned on a fight it
        // could not secure. Fixed in ExpeditionResolver.FightMonster: when a hero is at risk NOW
        // and a Heal item is available, simulate the post-heal HP against the SAME worst-case
        // check — if that's still in the danger zone, flee instead of drinking-and-fighting (the
        // item cannot save this fight, so don't spend it losing one; a hero with no Heal item, or
        // whose heal WOULD clear the risk, is unaffected). Re-measured after, same two 90-seed
        // blocks: seeds 2026..2115 reckless 231/342 (67.5%) vs prepared 170/316 (53.8%) — 13.7pp;
        // seeds 5000..5089 reckless 253/359 (70.5%) vs prepared 181/324 (55.9%) — 14.6pp. (Total
        // populations are smaller than the two blocks quoted above them because U-T1-11's
        // healthier reference economy shipped between those measurements and this one — an
        // unrelated, already-landed change, not an effect of this fix.) This fix targets the
        // un-merged #549 branch's regression but stands on its own: it closes a real gap on
        // PLAIN `main` too (the fast lane and this file's own margin assertion were already
        // green before this fix — the 90-seed sweep just never happened to sample the exact
        // insufficient-heal shape until instrumented directly), and this file's own gate
        // (fast lane 1644/1644, balance 70/70) did not move a single OTHER pinned band.
        var totals = RunAllSeedsParallel();
        var (recklessTotal, recklessDied, preparedTotal, preparedDied) =
            (totals.RecklessTotal, totals.RecklessDied, totals.PreparedTotal, totals.PreparedDied);

        // DIRECTION IS THE LOAD-BEARING CLAIM — asserted on its own, separately from the margin
        // below. A single combined "margin >= 5pp" assertion cannot distinguish "correct
        // direction, not enough margin" from "backwards" in its failure message, and that
        // ambiguity is exactly what let a 2.7pp INVERSION (Prepared WORSE than Reckless) read at
        // a glance as "the right way round with a narrower gap" during this fix's own review.
        // Strict inequality (no margin, no tie): Prepared must die at a LOWER rate than Reckless,
        // full stop, before any question of by how much.
        Assert.True((long)preparedDied * recklessTotal < (long)recklessDied * preparedTotal,
            $"Prepared mortality ({preparedDied}/{preparedTotal}) is not even LOWER than Reckless " +
            $"({recklessDied}/{recklessTotal}) — direction INVERTED, not just short of margin. " +
            "Preparation must read as insurance, never as a liability, whatever the margin turns " +
            "out to be.");

        // Integer-only rate comparison (KTD2: no floating point in sim-adjacent math):
        // preparedDied/preparedTotal + 5pp <= recklessDied/recklessTotal, cross-multiplied
        // (both totals are positive per the engagement-guard test above).
        var preparedScaled = (long)preparedDied * recklessTotal;
        var recklessScaled = (long)recklessDied * preparedTotal;
        var margin5pp = (long)recklessTotal * preparedTotal / 20;

        Assert.True(preparedScaled + margin5pp <= recklessScaled,
            $"Prepared mortality ({preparedDied}/{preparedTotal}) is not at least 5pp BELOW " +
            $"Reckless ({recklessDied}/{recklessTotal}). Preparation must read as insurance — " +
            "if a salve is again cancelling a survivable flee (ExpeditionResolver.FightMonster), " +
            "the trait is lethal and backwards from its own fiction.");
    }

    [Fact]
    [Trait("Category", "Balance")]
    public void SalvesStocked_TraitMortalityScenario_IsDeterministic()
    {
        Assert.Equal(Run(Seeds[0]), Run(Seeds[0]));
    }
}
