using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Economy;
using GameSim.Factions;
using GameSim.Kernel;

namespace GameSim.Tests.Factions;

/// <summary>
/// P5 U3 ore tariff with faction sink/source (R7/R8/KTD3/KTD4/KTD6/KTD8): the supplying faction's
/// standing-AT-START scales the ore price the PLAYER pays, applied to the AGGREGATE line cost
/// (per-mille, bounded, round-to-nearest via <see cref="IntegerCurves.MulDiv"/>) — never per-unit
/// (KTD4 rounds a cheap-ore nudge to zero). The hero ALWAYS receives the base ask (KTD3); the signed
/// delta (playerCost − base) is a MANDATORY recorded <see cref="TariffApplied"/> faction sink/source.
/// Neutral standing is an exact no-op. This core is discount-only (KTD8); the surcharge branch is
/// built and proven here by white-box negative-standing injection, though gameplay cannot reach it.
/// Pure integer, no float, no RNG.
/// </summary>
public class TariffTests
{
    private static readonly FactionDefinition Deepvein = FactionRegistry.Deepvein;

    private sealed class TestSink : IEventSink
    {
        public List<GameEvent> Events { get; } = [];
        public void Emit(GameEvent gameEvent) => Events.Add(gameEvent);
    }

    private static Hero MakeHero(int id, int gold = 40) => new(
        new HeroId(id), $"Hero{id}", "vanguard", Level: 1, MaxHp: 30, Gold: gold,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 0, DiedOnDay: null);

    /// <summary>Fresh Evening state: one hero (H1), player at <paramref name="playerGold"/>, the given
    /// standing seeded (0 = untouched, i.e. a truly fresh neutral map), and open ore offers.</summary>
    private static GameState EveningState(int playerGold, int standing, params OreOffered[] offers)
    {
        var player = PlayerState.NewGame(playerGold);
        if (standing != 0)
        {
            // White-box seam: WithStanding takes any int, so a NEGATIVE standing (unreachable by
            // gameplay in this discount-only core, KTD8) can be injected to exercise the surcharge branch.
            player = player.WithStanding(FactionRegistry.DeepveinId, standing);
        }

        return GameFactory.NewGame(seed: 42) with
        {
            Phase = DayPhase.Evening,
            Player = player,
            Heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(1, MakeHero(1)),
            OpenOreOffers = offers.ToImmutableList(),
        };
    }

    private static OreOffered Offer(int heroId, string material, int quantity, int unitPrice) =>
        new(new HeroId(heroId), material, quantity, unitPrice);

    private static (GameState State, List<GameEvent> Events) Buy(GameState state, string material, int quantity)
    {
        var sink = new TestSink();
        var (next, rejected) = new OreMarketHandlers()
            .Apply(state, new BuyOreAction(new HeroId(1), material, quantity), new Pcg32(state.Rng), sink);
        Assert.Null(rejected);
        return (next, sink.Events);
    }

    private static long TotalGold(GameState state)
    {
        long total = state.Player.Gold;
        foreach (var hero in state.Heroes.Values)
        {
            total += hero.Gold;
        }

        return total;
    }

    // ---- Neutral: exact no-op, byte-identical to a pre-tariff purchase (KTD4) -----------

    [Fact]
    public void NeutralStanding_PlayerPaysExactlyBase_HeroGetsBase_NoDelta()
    {
        // Fresh state, neutral standing, a faction DOES supply copper — so this proves the no-op is
        // driven by adj==0, not by "no faction". 5 copper at 3g = 15 base.
        var state = EveningState(playerGold: 1000, standing: 0, Offer(1, "copper", 10, 3));
        var heroBefore = state.Heroes[1].Gold;
        const int baseCost = 5 * 3;

        var (after, events) = Buy(state, "copper", 5);

        Assert.Equal(1000 - baseCost, after.Player.Gold);        // player pays EXACTLY base
        Assert.Equal(heroBefore + baseCost, after.Heroes[1].Gold); // hero receives base
        Assert.Equal(5, after.Player.Materials["copper"]);       // materials arrive intact
        Assert.Empty(events.OfType<TariffApplied>());            // no delta event at neutral (clean log)
    }

    // ---- High standing: aggregate discount, hero still gets base, delta recorded (KTD3/KTD4) ---

    [Fact]
    public void HighStanding_PlayerPaysAggregateDiscount_HeroGetsBase_DeltaRecorded()
    {
        // 10 copper at 3g = 30 base; standing at +cap → 10% adj → MulDiv(30, 900, 1000) = 27.
        var state = EveningState(playerGold: 1000, standing: Deepvein.StandingCap, Offer(1, "copper", 10, 3));
        var heroBefore = state.Heroes[1].Gold;

        var (after, events) = Buy(state, "copper", 10);

        Assert.Equal(1000 - 27, after.Player.Gold);      // player pays the AGGREGATE-discounted 27
        Assert.Equal(heroBefore + 30, after.Heroes[1].Gold); // hero STILL receives the 30 base (KTD3)

        var tariff = Assert.Single(events.OfType<TariffApplied>());
        Assert.Equal(FactionRegistry.DeepveinId, tariff.FactionId);
        Assert.Equal("copper", tariff.MaterialKey);
        Assert.Equal(30, tariff.BaseLineCost);
        Assert.Equal(27, tariff.PlayerCost);
        Assert.Equal(-3, tariff.Delta);                  // negative = source (discount minted)
    }

    // ---- Bounded: even beyond the cap the move is at most MaxAdjustmentPerMille (R8) ----

    [Fact]
    public void TariffBounded_StandingBeyondCap_ClampsToMaxAdjustment()
    {
        // Inject standing at 2× cap: adjPerMille must CLAMP to MaxAdjustmentPerMille, so the aggregate
        // cost moves by at most the cap percentage — no runaway discount (R8/KTD4).
        var state = EveningState(playerGold: 1000, standing: 2 * Deepvein.StandingCap, Offer(1, "copper", 10, 3));

        var (after, events) = Buy(state, "copper", 10);

        // Identical to the at-cap case: 10% of 30 = 3, so playerCost is 27, not more.
        Assert.Equal(1000 - 27, after.Player.Gold);
        var tariff = Assert.Single(events.OfType<TariffApplied>());
        Assert.Equal(-3, tariff.Delta);
        Assert.True(
            Math.Abs(tariff.Delta) <= IntegerCurves.MulDiv(30, Deepvein.MaxAdjustmentPerMille, 1000),
            "aggregate move must not exceed MaxAdjustmentPerMille of base");
    }

    // ---- Surcharge branch (dormant, KTD8): white-box negative standing → player OVERPAYS ---

    [Fact]
    public void SurchargeBranch_NegativeStanding_PlayerPaysMore_DeltaPositiveSink()
    {
        // Gameplay can't drive standing negative here (discount-only, KTD8), but the symmetric branch
        // must still compute — inject −cap directly. adj = −100 → MulDiv(30, 1100, 1000) = 33.
        var state = EveningState(playerGold: 1000, standing: -Deepvein.StandingCap, Offer(1, "copper", 10, 3));
        var heroBefore = state.Heroes[1].Gold;

        var (after, events) = Buy(state, "copper", 10);

        Assert.Equal(1000 - 33, after.Player.Gold);      // player OVERPAYS (surcharge)
        Assert.Equal(heroBefore + 30, after.Heroes[1].Gold); // hero STILL receives the 30 base (KTD3)

        var tariff = Assert.Single(events.OfType<TariffApplied>());
        Assert.Equal(30, tariff.BaseLineCost);
        Assert.Equal(33, tariff.PlayerCost);
        Assert.Equal(3, tariff.Delta);                   // positive = sink (gold burned)
    }

    // ---- Gold conservation over a multi-purchase sequence: extended invariant + per-purchase ---

    [Fact]
    public void GoldConservation_MultiPurchase_ExtendedInvariantAndPerPurchaseHold()
    {
        // Standing at +cap; three buys of 10 copper at 3g. Each: base 30, playerCost 27, delta −3.
        var state = EveningState(playerGold: 1000, standing: Deepvein.StandingCap, Offer(1, "copper", 30, 3));

        long deltaSum = 0;
        for (var i = 0; i < 3; i++)
        {
            var before = TotalGold(state);
            var playerBefore = state.Player.Gold;
            var heroBefore = state.Heroes[1].Gold;

            var (after, events) = Buy(state, "copper", 10);

            var tariff = Assert.Single(events.OfType<TariffApplied>());

            // Per-purchase: playerOut == heroBaseIn + tariffDelta.
            var playerOut = playerBefore - after.Player.Gold;
            var heroBaseIn = after.Heroes[1].Gold - heroBefore;
            Assert.Equal(heroBaseIn + tariff.Delta, playerOut);

            // Aggregate (no rival sales in this fixture): TotalGold(after) == TotalGold(before) − Σ delta.
            deltaSum += tariff.Delta;
            Assert.Equal(before - tariff.Delta, TotalGold(after));

            state = after;
        }

        Assert.Equal(-9, deltaSum); // three −3 discounts minted (a bounded source, KTD8)
    }

    // ---- Rounding: aggregate MulDiv round-to-nearest, ties away from zero, pinned ---

    [Fact]
    public void AggregateRounding_FractionalTie_RoundsAwayFromZero()
    {
        // 7 iron at 5g = 35 base (odd); +cap → 10% → MulDiv(35, 900, 1000) = 31.5 → 32 (ties away).
        var state = EveningState(playerGold: 1000, standing: Deepvein.StandingCap, Offer(1, "iron", 7, 5));

        var (after, events) = Buy(state, "iron", 7);

        Assert.Equal(1000 - 32, after.Player.Gold);
        var tariff = Assert.Single(events.OfType<TariffApplied>());
        Assert.Equal(35, tariff.BaseLineCost);
        Assert.Equal(32, tariff.PlayerCost);
        Assert.Equal(-3, tariff.Delta);
    }
}
