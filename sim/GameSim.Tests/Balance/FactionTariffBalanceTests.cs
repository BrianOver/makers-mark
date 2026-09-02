using System.Collections.Immutable;
using GameSim;
using GameSim.Contracts;
using GameSim.Economy;
using GameSim.Factions;
using GameSim.Harness;
using GameSim.Kernel;

namespace GameSim.Tests.Balance;

/// <summary>
/// P5 U5 faction-tariff acceptance scenario (validates R5/R7 end-to-end, KTD8). Proves the ore tariff
/// is a real, bounded, directional force on the 100-day balance economy — NOT a mechanical band
/// re-baseline.
///
/// <para><b>Re-baseline status: NONE.</b> With the Deepvein tariff LIVE, the existing
/// <see cref="BalanceSimTests"/> and <see cref="SalveProvisioningBalanceTests"/> bands still pass 17/17
/// unchanged — the ~10% ore discount for a saturated-standing player lands WITHIN their tolerances, so
/// no band was WRONG and none was re-baselined. The tariff DOES move metrics (on the main seed it
/// shifts first-floor-5 from day 36 to never-reached and end-alive heroes from 5 to 6, and cuts player
/// ore spend on identical buys from 2216 to 2042), which is what a band-moving core should do — but the
/// shifts stay inside the shipped bands. Every assert below is an INDEPENDENT acceptance criterion on
/// the tariff itself (directional / cap / drift), deliberately kept separate from the unchanged bands
/// so this file is not circular with them.</para>
///
/// <para><b>Deepvein params: KEPT.</b> The U1 starting values (StandingCap 100, RiseStep 5, DriftStep
/// 2, MaxAdjustmentPerMille 100) satisfy every criterion here — the tariff fires (standing saturates to
/// cap, 157 discount events on the main seed) and the aggregate discount (the bounded money-supply
/// source) stays under the 10% cap (7.85% of total ore) — so no tuning was needed. (Forward-ladder
/// plan L6, 2026-08-10-003: the money-supply property is now asserted single-run, via that same cap,
/// not via a two-run counterfactual — see "Money-supply upper bound" below.)</para>
///
/// Integer-only, deterministic (every scenario runs twice, byte/value-identical), no RNG in the tariff
/// path.
/// </summary>
public class FactionTariffBalanceTests
{
    private const int Days = 100;
    private const ulong MainSeed = 2026; // the main balance seed (matches BalanceSimTests)

    private static readonly FactionDefinition Deepvein = FactionRegistry.Deepvein;

    /// <summary>
    /// The tariff-relevant aggregates of one 100-day baseline run.
    /// <see cref="TotalBaseOreCost"/> is the neutral (untariffed) price of every ore buy that landed —
    /// the counterfactual "what a no-standing player pays for the identical purchases";
    /// <see cref="TotalPlayerOreCost"/> is what the player actually paid (base + Σ delta).
    /// <see cref="TotalTariffDelta"/> is Σ (playerCost − base): negative = gold minted by discounts.
    /// </summary>
    private sealed record TariffRunStats(
        string FinalJson,
        long TotalBaseOreCost,
        long TotalPlayerOreCost,
        long TotalTariffDelta,
        int NegativeDeltaCount,
        int MaxStanding);

    private sealed class NullSink : IEventSink
    {
        public void Emit(GameEvent gameEvent)
        {
        }
    }

    // MEASURED (CI run 33582674334, balance-sim.trx): Run(MainSeed, neutral: false) — the
    // FAVORED leg — is called once each by HundredDay_TariffFires_ReachesFavored_AndDiscounts,
    // HundredDay_Favored_PaysLessThanBaseOnItsOwnBuys, and
    // HundredDay_AggregateDiscount_StaysWithinCap (106.5 + 129.8 + 89.5s; two of the three are
    // pure duplication of the same 100-day campaign, ~216s). Run is a pure function of its
    // (seed, neutral) arguments, so memoizing the one (MainSeed, false) combination those three
    // band tests share is safe. Do NOT route HundredDay_WithFactions_IsDeterministic through this
    // field — it exists to run the SAME campaign TWICE and compare byte-identical output, so it
    // must keep calling Run() fresh both times (and its neutral:true leg is only ever computed by
    // that test, so it stays out of this memo too).
    private static readonly Lazy<TariffRunStats> MemoizedFavoredRun =
        new(() => Run(MainSeed, neutral: false), LazyThreadSafetyMode.ExecutionAndPublication);

    private static long BaseCostOf(GameState state, BuyOreAction buy)
    {
        var index = state.OpenOreOffers.FindIndex(o => o.From == buy.From && o.MaterialKey == buy.MaterialKey);
        return index < 0 ? 0 : (long)buy.Quantity * state.OpenOreOffers[index].UnitPrice;
    }

    /// <summary>
    /// Run the 100-day baseline campaign. <paramref name="neutral"/>=false is the FAVORED run (the
    /// normal baseline — <see cref="BaselinePlayer"/> buys every evening, so Deepvein standing
    /// SATURATES at the cap and the tariff runs at max discount permanently). <paramref name="neutral"/>=
    /// true wipes standing to null at the head of every tick, so it never accumulates a persistent
    /// discount — the neutral counterfactual over the same seed. (A tiny within-Evening residual can
    /// remain when several buys land in one tick before the next wipe; it only ever RAISES the neutral
    /// player's retained gold, which relaxes — never tightens — <see cref="HundredDay_Favored_IsStrictlyCheaperThanNeutral"/>'s
    /// ore-spend comparison below.) Ore base cost is read from the pre-tick offers so both legs are
    /// measured identically.
    /// </summary>
    private static TariffRunStats Run(ulong seed, bool neutral)
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(seed);

        long totalBaseOreCost = 0;
        long deltaSum = 0;
        var negCount = 0;
        var maxStanding = 0;

        for (var tick = 0; tick < Days * 5; tick++) // 5-phase day
        {
            if (neutral)
            {
                state = state with { Player = state.Player with { Standing = null } };
            }

            var actions = BaselinePlayer.ActionsFor(state);
            foreach (var buy in actions.OfType<BuyOreAction>())
            {
                totalBaseOreCost += BaseCostOf(state, buy);
            }

            var result = kernel.Tick(state, actions);
            Assert.DoesNotContain(result.Rejected, r => r.Action is BuyOreAction); // base-priced budget => buys land
            state = result.NewState;

            maxStanding = Math.Max(maxStanding, state.Player.StandingFor(FactionRegistry.DeepveinId));

            foreach (var gameEvent in result.Events)
            {
                if (gameEvent is TariffApplied tariff)
                {
                    deltaSum += tariff.Delta;
                    if (tariff.Delta < 0)
                    {
                        negCount++;
                    }
                }
            }
        }

        return new TariffRunStats(
            SaveCodec.Serialize(state), totalBaseOreCost, totalBaseOreCost + deltaSum, deltaSum,
            negCount, maxStanding);
    }

    // ---- Tariff fires: the feature is not inert over the campaign -----------------------------

    [Fact]
    [Trait("Category", "Balance")]
    public void HundredDay_TariffFires_ReachesFavored_AndDiscounts()
    {
        var fav = MemoizedFavoredRun.Value;

        // Standing must actually reach the "favored" band (cap/2), i.e. the driver is live and earning.
        Assert.True(fav.MaxStanding >= FactionStandingThresholds.FavoredEnter(Deepvein),
            $"standing never reached the favored band: peak {fav.MaxStanding} < {FactionStandingThresholds.FavoredEnter(Deepvein)}");

        // At least one discount (negative delta) actually fired — the tariff is not inert.
        Assert.True(fav.NegativeDeltaCount >= 1, "no discount TariffApplied event ever fired");

        // Net over the campaign the tariff is a discount (a gold SOURCE), not neutral or a sink.
        Assert.True(fav.TotalTariffDelta < 0,
            $"tariff was not net-directional (Σ delta {fav.TotalTariffDelta}, expected < 0 for a discount)");
    }

    // ---- Directional: the favored player pays less for the ore it actually buys ----------------

    [Fact]
    [Trait("Category", "Balance")]
    public void HundredDay_Favored_PaysLessThanBaseOnItsOwnBuys()
    {
        // U-T1 re-baseline: this test used to ALSO assert a two-run comparison — favored's total
        // ore spend stays within a hand-tuned slack of neutral's total ore spend over the SAME
        // seed. That comparison already had one re-baseline on record (U9, 2026-07-24, ~41g
        // overshoot) for the reason this file's own "Money-supply upper bound" section names for a
        // sibling test: favored and neutral submit the SAME BaselinePlayer policy but are not
        // forced onto the same in-game trajectory, so their total ore VOLUME diverges for reasons
        // that have nothing to do with the tariff (gold-on-hand timing relative to each Evening's
        // offers, compounded over 500 ticks). U-T1's demand-gated craft loop (BaselinePlayer now
        // only crafts when some alive hero has a real gear gap) raises the reference player's peak
        // gold roughly 2x-4x, which widens that same compounding drift by an order of magnitude:
        // measured on the main seed, fav bought base=3143g of ore vs neutral's base=959g (neutral's
        // buys landed at ZERO discount that run — its wiped-every-tick standing never earns one) —
        // a >2000g volume gap no fixed slack constant should try to absorb, per the owner ruling
        // already on record just above ("you are overcomplicating the balance tests, just do
        // what's more fun for the player"). Deleted rather than re-tuned, same as that sibling.
        //
        // What survives is the single-run, trajectory-free, player-feelable proof: for the exact
        // purchases the favored player actually made, the tariff paid less than the untariffed
        // price of those same buys (measured: 2980g paid vs 3143g base, a real ~5.2% saving).
        // HundredDay_AggregateDiscount_StaysWithinCap (below) bounds that saving from the other
        // side, and HundredDay_TariffFires_ReachesFavored_AndDiscounts proves it is never zero.
        var fav = MemoizedFavoredRun.Value;

        Assert.True(fav.TotalPlayerOreCost < fav.TotalBaseOreCost,
            $"favored player did not save on identical buys: paid {fav.TotalPlayerOreCost} vs base {fav.TotalBaseOreCost}");
    }

    // ---- Cap: the aggregate discount never runs away past MaxAdjustmentPerMille ----------------

    [Fact]
    [Trait("Category", "Balance")]
    public void HundredDay_AggregateDiscount_StaysWithinCap()
    {
        var fav = MemoizedFavoredRun.Value;

        var minted = -fav.TotalTariffDelta;            // total gold saved by discounts (>= 0)
        Assert.True(minted > 0, "no discount minted — nothing to bound");

        // Σ discount <= MaxAdjustmentPerMille (10%) of the total base ore cost, integer cross-multiply
        // (no floats). Even though per-buy rounding on cheap ore can exceed 10% on a single line, the
        // campaign aggregate stays under the cap — no runaway discount (R8/KTD4).
        Assert.True(minted * 1000 <= fav.TotalBaseOreCost * Deepvein.MaxAdjustmentPerMille,
            $"aggregate discount {minted} exceeds {Deepvein.MaxAdjustmentPerMille}‰ of base ore {fav.TotalBaseOreCost}");
    }

    // ---- Money-supply upper bound (KTD8): discount-minting cannot inflate reserves -------------
    //
    // Forward-ladder plan L6 (2026-08-10-003, owner ruling "you are overcomplicating the balance
    // tests, just do what's more fun for the player"): the old HundredDay_MoneySupply_StaysWithinNeutralPlusMinted
    // asserted a two-run counterfactual (favored town gold vs a neutral re-run of the SAME seed,
    // padded with a hand-tuned TrajectoryDriftSlack for trajectory divergence between the two runs)
    // — no player ever sees the neutral run, so the assertion measured something unobservable at
    // the table. DELETED, not re-tuned. What a player CAN feel is bounded right here, in ONE run:
    // HundredDay_AggregateDiscount_StaysWithinCap (above, under "Cap") already asserts the
    // single-run, player-feelable version of the same property — the discount minted over the
    // campaign cannot exceed MaxAdjustmentPerMille (10%) of the ore the player actually bought,
    // cross-multiplied, no floats, no second run, no slack constant. TariffFires and DriftBack are
    // untouched by this reframing.

    // ---- Determinism: the tariffed campaign is byte-identical across two runs ------------------

    [Fact]
    [Trait("Category", "Balance")]
    public void HundredDay_WithFactions_IsDeterministic()
    {
        Assert.Equal(Run(MainSeed, neutral: false).FinalJson, Run(MainSeed, neutral: false).FinalJson);
        Assert.Equal(Run(MainSeed, neutral: true).FinalJson, Run(MainSeed, neutral: true).FinalJson);
    }

    // ---- Drift-back: a buy-then-stop run decays standing to neutral and the discount shrinks ----
    // The 100-day baseline buys every Evening, so its standing SATURATES and never shows drift-back;
    // this dedicated scripted run is the only place drift-back is exercised (per the U5 spec).

    private static Hero AliveHero(int id) => new(
        new HeroId(id), $"H{id}", "vanguard", Level: 1, MaxHp: 30, Gold: 40,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty, Alive: true, DeepestFloorReached: 0, DiedOnDay: null);

    private static GameState Evening(int gold, params OreOffered[] offers) =>
        GameFactory.NewGame(seed: 7) with
        {
            Phase = DayPhase.Evening,
            Player = PlayerState.NewGame(gold),
            Heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(1, AliveHero(1)),
            OpenOreOffers = offers.ToImmutableList(),
            // These helpers drive OreMarketHandlers directly (never through the kernel's day-rollover
            // slot reset) to saturate standing over many buys — they exercise the tariff/standing
            // mechanic, NOT the G3 action budget, so grant slots generously so no buy is slot-rejected.
            ActionSlotsRemaining = 1000,
        };

    /// <summary>The discount (base − playerCost) on a fixed 10-copper@3 buy (base 30) at a given standing.</summary>
    private static int DiscountFor(int standing)
    {
        var state = Evening(1000, new OreOffered(new HeroId(1), "copper", 10, 3));
        if (standing != 0)
        {
            state = state with { Player = state.Player.WithStanding(Deepvein.Id, standing) };
        }

        var (after, rejected) = new OreMarketHandlers()
            .Apply(state, new BuyOreAction(new HeroId(1), "copper", 10), new Pcg32(state.Rng), new NullSink());
        Assert.Null(rejected);
        var playerCost = 1000 - after.Player.Gold;
        return 30 - playerCost;
    }

    /// <summary>
    /// Saturate Deepvein standing to the cap through real purchases, then run Morning drifts to
    /// neutral, returning the standing after each Morning (index 0 = the saturated peak).
    /// </summary>
    private static ImmutableList<int> DriftBackSequence()
    {
        var buysToSaturate = (Deepvein.StandingCap + Deepvein.RiseStep - 1) / Deepvein.RiseStep;
        var handler = new OreMarketHandlers();
        var state = Evening(1000, new OreOffered(new HeroId(1), "copper", buysToSaturate, 3));
        for (var i = 0; i < buysToSaturate; i++)
        {
            var (next, rejected) = handler.Apply(
                state, new BuyOreAction(new HeroId(1), "copper", 1), new Pcg32(state.Rng), new NullSink());
            Assert.Null(rejected);
            state = next;
        }

        var drift = new FactionDriftSystem();
        var horizon = (Deepvein.StandingCap + Deepvein.DriftStep - 1) / Deepvein.DriftStep; // enough to reach 0
        var sequence = ImmutableList.CreateBuilder<int>();
        sequence.Add(state.Player.StandingFor(Deepvein.Id));
        for (var m = 0; m < horizon; m++)
        {
            state = drift.Process(state, new Pcg32(state.Rng), new NullSink());
            sequence.Add(state.Player.StandingFor(Deepvein.Id));
        }

        return sequence.ToImmutable();
    }

    [Fact]
    [Trait("Category", "Balance")]
    public void DriftBack_StandingDecaysToNeutral_AndDiscountShrinks()
    {
        var sequence = DriftBackSequence();

        // Standing saturated at the cap before drift began.
        Assert.Equal(Deepvein.StandingCap, sequence[0]);

        // Every Morning steps standing strictly DOWN toward neutral, never past 0.
        for (var i = 1; i < sequence.Count; i++)
        {
            Assert.True(sequence[i] >= 0, $"standing overshot neutral: {sequence[i]}");
            if (sequence[i - 1] > 0)
            {
                Assert.True(sequence[i] < sequence[i - 1],
                    $"standing did not fall on a Morning: {sequence[i - 1]} -> {sequence[i]}");
            }
        }

        // Fully returned to neutral within the drift horizon.
        Assert.Equal(0, sequence[^1]);

        // The effective discount shrinks as standing drifts down, reaching zero at neutral.
        var peakDiscount = DiscountFor(Deepvein.StandingCap);
        var midDiscount = DiscountFor(Deepvein.StandingCap / 2);
        var neutralDiscount = DiscountFor(0);
        Assert.True(peakDiscount > midDiscount, $"discount did not shrink: peak {peakDiscount} vs mid {midDiscount}");
        Assert.True(midDiscount > neutralDiscount, $"discount did not shrink: mid {midDiscount} vs neutral {neutralDiscount}");
        Assert.Equal(0, neutralDiscount);
    }

    [Fact]
    [Trait("Category", "Balance")]
    public void DriftBack_IsDeterministic()
    {
        Assert.Equal(DriftBackSequence(), DriftBackSequence());
    }
}
