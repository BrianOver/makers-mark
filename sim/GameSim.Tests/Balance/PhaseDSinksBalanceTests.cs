using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Economy;
using GameSim.Kernel;

namespace GameSim.Tests.Balance;

/// <summary>
/// Phase D (U-D1) acceptance scenario: proves the five power-matched sinks never let gold run
/// negative and that the one CAPPED sink (<see cref="CommissionLegendaryWorkAction"/>) genuinely
/// bounds itself rather than draining an unbounded treasury. <see cref="BaselinePlayer"/> never
/// submits any of these new actions (golden-neutral, per this unit's brief), so this is a
/// dedicated scripted scenario rather than a 100-day <see cref="BaselinePlayer"/> run — the
/// existing <see cref="OreMarketHandlersTests"/>/<see cref="RentSystemTests"/> suites already cover
/// sinks 2 (ore market) and 4 (guild dues), which this unit reuses unchanged.
///
/// Determinism: integer-only, zero RNG (every handler here draws nothing — see each handler's
/// class doc); the whole script runs twice, byte-identical.
/// </summary>
public class PhaseDSinksBalanceTests
{
    private static GameState RichWorkshopStart() =>
        GameFactory.NewGame(seed: 2026) with
        {
            Player = PlayerState.NewGame(startingGold: 500_000) with
            {
                Materials = ImmutableSortedDictionary<string, int>.Empty
                    .SetItem("copper", 1000).SetItem("iron", 1000).SetItem("steel", 1000).SetItem("mithril", 1000),
            },
            ActionSlotsRemaining = 10_000, // isolate the sink math from the unrelated G3 slot budget
        };

    private static GameKernel SinksKernel() => new(
        ImmutableList<IPhaseSystem>.Empty,
        ImmutableList.Create<IActionHandler>(
            new ForgeTierHandlers(), new ForgeSupplyHandlers(), new MasterworkAttemptHandlers(), new LegendaryCommissionHandlers()));

    private static string Script(GameState state) => SaveCodec.Serialize(state);

    /// <summary>Runs the full script once: 4 forge-tier upgrades, a stock of coal/flux, several
    /// masterwork attempts, then legendary commissions until the cap bites. Returns the final
    /// state plus every intermediate player-gold snapshot (for the never-negative assertion).</summary>
    private static (GameState Final, ImmutableList<int> GoldTrace) RunScript()
    {
        var kernel = SinksKernel();
        var state = RichWorkshopStart();
        var trace = ImmutableList.CreateBuilder<int>();

        void Step(PlayerAction action, bool morningOnly = false)
        {
            // ForgeTierHandlers/ForgeSupplyHandlers are Morning-only; the kernel advances
            // state.Phase every tick (no phase systems are composed to cycle it back around in
            // this focused kernel), so pin it back to Morning immediately before each such action.
            if (morningOnly)
            {
                state = state with { Phase = DayPhase.Morning };
            }

            var tick = kernel.Tick(state, ImmutableList.Create(action));
            Assert.Empty(tick.Rejected);
            state = tick.NewState;
            trace.Add(state.Player.Gold);
        }

        // Sink 1: buy all four forge-tier upgrades.
        for (var i = 0; i < 4; i++)
        {
            Step(new UpgradeForgeAction(), morningOnly: true);
        }

        // Sink 3a: stock up on coal + flux.
        Step(new BuyForgeSupplyAction("coal", 50), morningOnly: true);
        Step(new BuyForgeSupplyAction("flux", 20), morningOnly: true);

        // Sink 3b: several masterwork attempts (workshop is at Forge Tier V, well past the gate).
        for (var i = 0; i < 5; i++)
        {
            Step(new MasterworkAttemptAction("dagger", "copper"));
        }

        // Sink 5: exhaust the capped legendary-commission slots, then confirm the cap actually bites.
        for (var i = 0; i < LegendaryCommissionHandlers.MaxPerCampaign; i++)
        {
            Step(new CommissionLegendaryWorkAction("dagger", "copper"));
        }

        var beforeCappedAttempt = state.Player.Gold;
        var capped = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new CommissionLegendaryWorkAction("dagger", "copper")));
        Assert.Single(capped.Rejected);
        Assert.Equal(beforeCappedAttempt, capped.NewState.Player.Gold); // the cap — not the wallet — stops the sink

        return (state, trace.ToImmutable());
    }

    [Fact]
    [Trait("Category", "Balance")]
    public void FullSinkScript_GoldNeverGoesNegative()
    {
        var (_, trace) = RunScript();

        Assert.NotEmpty(trace);
        Assert.All(trace, g => Assert.True(g >= 0, $"gold went negative mid-script: {g}"));
    }

    [Fact]
    [Trait("Category", "Balance")]
    public void FullSinkScript_ForgeTierReachesMaxAndCommissionCapBites()
    {
        var (final, _) = RunScript();

        Assert.Equal(ForgeTierHandlers.MaxUpgradeIndex + 1, ForgeTierHandlers.CurrentTierIndex(final.Player));
        Assert.Equal(LegendaryCommissionHandlers.MaxPerCampaign,
            final.Player.Materials[LegendaryCommissionHandlers.CommissionsUsedKey]);

        // 5 masterwork attempts + 4 commissions = 9 minted items, every one at least Superior.
        Assert.Equal(9, final.Items.Count);
        Assert.All(final.Items.Values, item => Assert.True(item.Quality >= QualityGrade.Superior));
    }

    [Fact]
    [Trait("Category", "Balance")]
    public void FullSinkScript_IsDeterministic()
    {
        var (finalA, _) = RunScript();
        var (finalB, _) = RunScript();

        Assert.Equal(Script(finalA), Script(finalB));
    }
}
