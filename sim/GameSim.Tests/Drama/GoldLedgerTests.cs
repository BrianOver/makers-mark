using System.Collections.Immutable;
using GameSim;
using GameSim.Bounties;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Harness;
using GameSim.Kernel;

namespace GameSim.Tests.Drama;

/// <summary>
/// U2 (C1b, R1): the Evening "why did my gold change" ledger must account for EVERY player-gold
/// delta, not just the evented ones. MF-2 proved a neutral-standing ore buy emits NO event at all
/// (<see cref="GameSim.Economy.OreMarketHandlers"/> records only the tariff DELTA when standing
/// moves the price) and MF-4 proved the bounty escrow refund is likewise silent
/// (<c>BountySystems.cs:62-78</c>) — so <see cref="GoldLedger.DayDeltas"/> takes both as caller-fed
/// rows rather than trying (and failing) to reconstruct them from the log alone. These pin the
/// reconstruction invariant end-to-end against a real, scripted kernel run: for every day, the
/// itemized rows must sum to the ACTUAL purse change that day.
/// </summary>
public class GoldLedgerTests
{
    [Fact]
    public void DayDeltas_ReconstructsObservedPurseChange_EveryDayOfABaselineRun()
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(seed: 2026);

        const int days = 15;
        var goldAtDayStart = state.Player.Gold;
        var day = state.Day;

        for (var i = 0; i < days; i++)
        {
            var oreSpend = ImmutableList<GoldLedgerEntry>.Empty;
            var forgeUpgrades = ImmutableList<GoldLedgerEntry>.Empty;

            while (true)
            {
                var before = state;
                var actions = BaselinePlayer.ActionsFor(before);
                var result = kernel.Tick(before, actions);
                state = result.NewState;

                if (before.Phase == DayPhase.Evening)
                {
                    // MF-2: the SAME derivation Program.cs's Advance() feeds GoldLedger with — an
                    // accepted BuyOreAction's actual cost is the matching TariffApplied.PlayerCost
                    // when standing moved the price, else the pre-tick offer's base ask (no event
                    // fires at all for a neutral-standing buy). BaselinePlayer buys ore every
                    // Evening it can afford (Harness/BaselinePlayer.cs), so this hole is exercised
                    // for real on most days of this run, not left an empty caller input.
                    oreSpend = ComputeOreSpend(before, actions, result);
                }

                if (before.Phase == DayPhase.Morning)
                {
                    // U-T1-11: a third silent flow, same shape as MF-2 above — UpgradeForgeAction
                    // emits no event at all (ForgeTierHandlers' own class doc), so an accepted one's
                    // cost is derived the same way: the pre-tick tier index looked up against the
                    // ladder's own fixed cost table, only when the action was actually accepted
                    // (never rejected — a rejected action changes nothing, needs no row).
                    forgeUpgrades = ComputeForgeUpgrade(before, actions, result);
                }

                if (state.Phase == DayPhase.Morning)
                {
                    break; // the day just rolled over
                }
            }

            var (_, total) = GoldLedger.DayDeltas(state, day, oreSpend, ImmutableList<GoldLedgerEntry>.Empty, forgeUpgrades);
            var observedChange = state.Player.Gold - goldAtDayStart;
            Assert.Equal(observedChange, total);

            goldAtDayStart = state.Player.Gold;
            day = state.Day;
        }
    }

    [Fact]
    public void DayDeltas_ReconstructsThePurseChange_OnADayWithABountyEscrowAndItsRefund()
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(seed: 3);

        // Day 1 Morning: escrow a floor-5 bounty — unreachable by any starting hero (Judge declines
        // whenever TargetFloor > DeepestFloorReached + 1), so it is GUARANTEED to lapse and refund
        // at expiry, never accepted (mirrors BountyRefundTests.UnacceptedBounty_StillRefunds). 50g
        // (not the fresh 100g purse) keeps the destitution floor (10g target) comfortably out of
        // reach of tripping and adding an unplanned RecoveryStipendGranted to this day's total.
        var goldAtDayStart = state.Player.Gold;
        var day = state.Day;
        var postResult = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new PostBountyAction(TargetFloor: 5, RewardGold: 50)));
        state = postResult.NewState;
        Assert.Equal(goldAtDayStart - 50, state.Player.Gold);

        Bounty? refunded = null;

        // 5-phase days, empty actions from here — walk until the expiry refund lands.
        for (var i = 0; i < 5 * (BountyRules.ExpiryDays + 2) && refunded is null; i++)
        {
            var before = state;
            var result = kernel.Tick(before, ImmutableList<PlayerAction>.Empty);
            state = result.NewState;

            if (state.Phase != DayPhase.Morning)
            {
                continue; // still mid-day
            }

            // Day just rolled over (before.Phase was Evening) — check U1's MF-4 cross-tick diff.
            var paidIds = result.Events.OfType<BountyPaid>().Select(p => p.Bounty.Value).ToHashSet();
            var afterIds = state.Bounties.Select(b => b.Id.Value).ToHashSet();
            foreach (var bounty in before.Bounties)
            {
                if (!afterIds.Contains(bounty.Id.Value) && !paidIds.Contains(bounty.Id.Value))
                {
                    refunded = bounty;
                }
            }

            if (refunded is null)
            {
                // No refund yet this day — still assert the invariant holds with an empty caller
                // feed (no ore was ever bought in this all-empty-actions run).
                var (_, noopTotal) = GoldLedger.DayDeltas(state, day, ImmutableList<GoldLedgerEntry>.Empty, ImmutableList<GoldLedgerEntry>.Empty);
                Assert.Equal(state.Player.Gold - goldAtDayStart, noopTotal);
                goldAtDayStart = state.Player.Gold;
                day = state.Day;
            }
        }

        Assert.NotNull(refunded);
        var refundRows = ImmutableList.Create(
            new GoldLedgerEntry("bounty refund", refunded!.RewardGold, $"{refunded.Id} (floor {refunded.TargetFloor}) lapsed"));

        var (rows, total) = GoldLedger.DayDeltas(state, day, ImmutableList<GoldLedgerEntry>.Empty, refundRows);
        Assert.Contains(rows, r => r.Source == "bounty refund" && r.Delta == 50);
        Assert.Equal(state.Player.Gold - goldAtDayStart, total);
    }

    /// <summary>MF-2, replicated from Program.cs's Advance() so this test exercises the SAME
    /// derivation the CLI feeds GoldLedger with, against a real ore-buying policy. Matches a
    /// TariffApplied event on (MaterialKey, BaseLineCost) — not MaterialKey alone — because two
    /// same-material buys in one tick can carry DIFFERENT baselines (different quantities), and one
    /// can legitimately round to a zero delta (no event) while another doesn't; matching by
    /// MaterialKey alone can steal the wrong buy's tariff record (caught by this very test).</summary>
    private static ImmutableList<GoldLedgerEntry> ComputeOreSpend(
        GameState before, ImmutableList<PlayerAction> actions, TickResult result)
    {
        var rejected = result.Rejected.Select(r => r.Action).ToHashSet();
        var tariffs = result.Events.OfType<TariffApplied>().ToList();
        var rows = ImmutableList.CreateBuilder<GoldLedgerEntry>();

        foreach (var ore in actions)
        {
            if (ore is not BuyOreAction buy || rejected.Contains(ore))
            {
                continue;
            }

            var offer = before.OpenOreOffers.FirstOrDefault(o => o.From == buy.From && o.MaterialKey == buy.MaterialKey);
            var baseLineCost = offer is null ? 0 : buy.Quantity * offer.UnitPrice;

            var tariffIndex = tariffs.FindIndex(t => t.MaterialKey == buy.MaterialKey && t.BaseLineCost == baseLineCost);
            if (tariffIndex >= 0)
            {
                var tariff = tariffs[tariffIndex];
                tariffs.RemoveAt(tariffIndex);
                rows.Add(new GoldLedgerEntry("ore", -tariff.PlayerCost, $"{buy.Quantity}x {buy.MaterialKey}"));
                continue;
            }

            rows.Add(new GoldLedgerEntry("ore", -baseLineCost, $"{buy.Quantity}x {buy.MaterialKey}"));
        }

        return rows.ToImmutable();
    }

    /// <summary>U-T1-11: same shape as <see cref="ComputeOreSpend"/> for the other silent flow —
    /// <c>UpgradeForgeAction</c> emits no event, so an ACCEPTED one's cost is derived from the
    /// pre-tick tier index against the ladder's own fixed <see cref="GameSim.Economy.ForgeTierHandlers.GoldCost"/>
    /// table (the SAME lookup <c>ForgeTierHandlers.Apply</c> itself uses), never re-derived.</summary>
    private static ImmutableList<GoldLedgerEntry> ComputeForgeUpgrade(
        GameState before, ImmutableList<PlayerAction> actions, TickResult result)
    {
        var rejected = result.Rejected.Select(r => r.Action).ToHashSet();
        var rows = ImmutableList.CreateBuilder<GoldLedgerEntry>();

        foreach (var action in actions)
        {
            if (action is not UpgradeForgeAction || rejected.Contains(action))
            {
                continue;
            }

            var tierIndex = GameSim.Economy.ForgeTierHandlers.CurrentTierIndex(before.Player);
            var cost = GameSim.Economy.ForgeTierHandlers.GoldCost[tierIndex];
            rows.Add(new GoldLedgerEntry("forge tier", -cost, $"upgraded to Forge Tier {tierIndex + 2}"));
        }

        return rows.ToImmutable();
    }
}
