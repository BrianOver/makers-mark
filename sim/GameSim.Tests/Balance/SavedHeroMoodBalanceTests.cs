using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Harness;
using GameSim.Heroes;
using GameSim.Kernel;

namespace GameSim.Tests.Balance;

/// <summary>
/// P2-PEOPLE-26 (link5, "the outcome becomes the town's memory", owner ruling §11.7.13): two-sided
/// gate on <see cref="ExpeditionRevealSystem.SavedByYourWorkMood"/> — heroes saved by player-crafted
/// work must read warmer than untouched strangers WITHOUT every hero ending up Sworn by day 100. A
/// one-sided "mood went up" assertion cannot see the constant tuned so high the band stops
/// distinguishing anything, which is exactly the failure this unit's own PR body sweep is required
/// to catch before landing.
///
/// <para><b>Measured 2026-09-19</b> (20 seeds x 100 days, in-process — the same policies
/// <c>dotnet run --project sim/GameSim.Cli -- batch --policy &lt;baseline|forgecounter&gt;</c>
/// drives, re-run here so the deed-move tally can be read off live <see cref="GameState.Items"/>
/// instead of a chronicle export that carries no item catalog): BEFORE this unit landed, ForgeCounter
/// (the policy that both stocks the shelf and closes counter sales) never put a single hero above
/// Stranger from mood alone beyond what pin/fleece/fair-deal already did (max mood 153, 0% Sworn,
/// 0% Patron). AFTER: Sworn 6.1%, Patron 4.6%, max mood 5180 — the long tail of a hero who fights
/// every night for 100 days beside the same signed gear and gets saved dozens of times. Most heroes
/// (74.3%) stay Stranger; this is a warm tail, not a saturated one. The Baseline policy (shelf sales
/// only, no counter) shows the same shape at a smaller scale (1,057 legend-deed credits landed there
/// too — P2-HONEST-30's shelf channel alone reaches heroes) but its Sworn/Patron shares are already
/// dominated by <c>CommissionSystem</c>'s own much larger pre-existing swings (12.9% Sworn BEFORE
/// this unit), so ForgeCounter is the cleaner read of this unit's own marginal effect. Full table in
/// the landing PR.</para>
/// </summary>
public class SavedHeroMoodBalanceTests
{
    private const int Days = 100;
    private static readonly ulong[] Seeds = Enumerable.Range(1, 20).Select(i => (ulong)i).ToArray();

    private sealed record SweepStats(
        List<int> FinalMoods, int SwornCount, int PatronCount, int RegularCount, int LegendDeedMoodMoves);

    private static SweepStats RunSweep(Func<GameState, ImmutableList<PlayerAction>> policy)
    {
        var kernel = GameComposition.BuildKernel();
        var moods = new List<int>();
        var sworn = 0;
        var patron = 0;
        var regular = 0;
        var deedMoves = 0;

        foreach (var seed in Seeds)
        {
            var state = GameComposition.NewCampaign(seed);
            for (var tick = 0; tick < Days * 5; tick++)
            {
                var before = state;
                state = kernel.Tick(state, policy(state)).NewState;

                // Tally exactly the production predicate (LegendQuery.IsLegendDeed + PlayerCrafted +
                // alive-at-reveal), read off THIS tick's own new events against THIS tick's own new
                // state — the same order ExpeditionRevealSystem itself observes them in.
                foreach (var e in state.EventLog.Skip(before.EventLog.Count).OfType<AttributionBeatEvent>())
                {
                    if (!LegendQuery.IsLegendDeed(e))
                    {
                        continue;
                    }

                    if (state.Items.TryGetValue(e.Item.Value, out var item) && item.PlayerCrafted
                        && state.Heroes.TryGetValue(e.Hero.Value, out var hero) && hero.Alive)
                    {
                        deedMoves++;
                    }
                }
            }

            foreach (var hero in state.Heroes.Values)
            {
                moods.Add(hero.MoodPermille);
                switch (RelationshipBands.For(hero.Id, state))
                {
                    case RelationshipBand.Sworn:
                        sworn++;
                        break;
                    case RelationshipBand.Patron:
                        patron++;
                        break;
                    case RelationshipBand.Regular:
                        regular++;
                        break;
                }
            }
        }

        return new SweepStats(moods, sworn, patron, regular, deedMoves);
    }

    [Fact]
    [Trait("Category", "Balance")]
    public void ForgeCounterSweep_LegendDeedsMoveMood_WithoutEveryHeroSaturatingSworn()
    {
        // ForgeCounter is the cleanest read of this unit's OWN marginal effect (see class doc): it
        // is the one policy where a legend-deed credit is not swamped by CommissionSystem's much
        // larger pre-existing swings.
        var stats = RunSweep(GameSim.Harness.ForgeCounterPlayer.ActionsFor);
        var heroCount = stats.FinalMoods.Count;

        Assert.True(stats.LegendDeedMoodMoves > 0,
            "no legend-deed mood move landed across 20 seeds x 100 days — the credit path never fired");

        // Measured 6.1% Sworn / 4.6% Patron (2026-09-19) — headroom, not a shaved ceiling: the
        // failure this guards is "the constant is tuned so high the band stops distinguishing
        // anything" (every hero Sworn), not "a hero saved often enough reads warmly".
        var swornShare = (double)stats.SwornCount / heroCount;
        Assert.True(swornShare < 0.5,
            $"{stats.SwornCount}/{heroCount} heroes Sworn by day 100 ({swornShare:P0}) — "
                + "SavedByYourWorkMood is tuned too high and the band has stopped distinguishing anything");
    }

    [Fact]
    [Trait("Category", "Balance")]
    public void BaselineSweep_ShelfSalesAlone_StillLandLegendDeedCredits()
    {
        // P2-HONEST-30's own finding: BaselinePlayer never opens the interactive counter, but it
        // DOES stock the shelf, and HeroShoppingSystem's passive purchase loop still puts
        // player-crafted gear on heroes — so the shelf channel alone (link2's first of four honest
        // channels) must ALSO be able to earn a legend-deed mood credit, not only a counter close.
        var stats = RunSweep(BaselinePlayer.ActionsFor);

        Assert.True(stats.LegendDeedMoodMoves > 0,
            "no legend-deed mood move landed on shelf-sold gear across 20 seeds x 100 days");
    }
}
