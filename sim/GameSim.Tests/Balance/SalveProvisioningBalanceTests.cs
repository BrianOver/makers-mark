using System.Collections.Immutable;
using GameSim;
using GameSim.Contracts;
using GameSim.Harness;

namespace GameSim.Tests.Balance;

/// <summary>
/// The P2 provisioning scenario: a scripted player who crafts and stocks field-salves
/// daily, on top of the baseline policy, must move survival the right way — a deeper
/// average cleared floor OR fewer deaths than the salveless baseline (tolerant, the
/// OR is the band). Engagement asserts guard against a vacuous pass, and the baseline
/// leg doubles as proof that BaselinePlayer never touches consumables.
/// </summary>
public class SalveProvisioningBalanceTests
{
    private const int Days = 100;
    private const ulong Seed = 2026; // the main balance seed
    private const int SalvePrice = 8; // affordable for the whole starting cast (30g+)

    private sealed record ProvisionStats(int Deaths, int Expeditions, int FloorSum, int SalvesSold, int SalveUses);

    private static ProvisionStats Run(bool withSalves)
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(Seed);

        var deaths = 0;
        var expeditions = 0;
        var floorSum = 0;
        var salvesSold = 0;
        var salveUses = 0;

        for (var tick = 0; tick < Days * 3; tick++)
        {
            var actions = withSalves ? SalveActionsFor(state) : BaselinePlayer.ActionsFor(state);
            var result = kernel.Tick(state, actions);
            state = result.NewState;

            foreach (var gameEvent in result.Events)
            {
                switch (gameEvent)
                {
                    case HeroDied:
                        deaths++;
                        break;
                    case ItemSold sold when state.Items.TryGetValue(sold.Item.Value, out var item)
                                            && item.Effect is not null:
                        salvesSold++;
                        break;
                }
            }

            // The Expedition tick just ran: its results sit in PendingExpeditions
            // until the Evening reveal — the one window to read achieved depths.
            if (state.Phase == DayPhase.Evening)
            {
                foreach (var expedition in state.PendingExpeditions)
                {
                    expeditions++;
                    floorSum += expedition.DeepestFloorCleared;
                    salveUses += expedition.Floors.Sum(f => f.Combats.Sum(c => c.Uses.Count));
                }
            }
        }

        return new ProvisionStats(deaths, expeditions, floorSum, salvesSold, salveUses);
    }

    /// <summary>
    /// Baseline policy + salves: craft up to two field-salves per Expedition window
    /// (AFTER the baseline's gear craft, so gear priority is untouched; short copper
    /// just rejects the extra craft — typed, no RNG, no state change) and reprice the
    /// baseline's generic 1g stocking of statless salves to the scenario price.
    /// Re-stock attempts for sold salves are refused by the P2 ShopHandlers rule.
    /// </summary>
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
    public void Baseline_NeverTouchesConsumables()
    {
        var baseline = Run(withSalves: false);

        Assert.Equal(0, baseline.SalvesSold);
        Assert.Equal(0, baseline.SalveUses);
    }

    [Fact]
    [Trait("Category", "Balance")]
    public void SalveEconomy_ActuallyEngages()
    {
        var salves = Run(withSalves: true);

        Assert.True(salves.SalvesSold > 0, "no salve ever sold — the shopping pass never engaged");
        Assert.True(salves.SalveUses > 0, "no salve ever drunk — the quaff rule never engaged");
    }

    [Fact]
    [Trait("Category", "Balance")]
    public void Provisioning_MovesSurvivalTheRightWay()
    {
        var baseline = Run(withSalves: false);
        var salves = Run(withSalves: true);

        // Directional, tolerant: deeper average cleared floor (cross-multiplied
        // integer comparison — no floats in the band math) OR fewer deaths.
        var deeperAverageFloor =
            (long)salves.FloorSum * baseline.Expeditions > (long)baseline.FloorSum * salves.Expeditions;
        var fewerDeaths = salves.Deaths < baseline.Deaths;

        Assert.True(deeperAverageFloor || fewerDeaths,
            $"salves helped nobody: baseline {baseline.FloorSum}/{baseline.Expeditions} floors, " +
            $"{baseline.Deaths} deaths vs salves {salves.FloorSum}/{salves.Expeditions} floors, " +
            $"{salves.Deaths} deaths ({salves.SalvesSold} sold, {salves.SalveUses} drunk)");
    }

    [Fact]
    [Trait("Category", "Balance")]
    public void SalveScenario_IsDeterministic()
    {
        Assert.Equal(Run(withSalves: true), Run(withSalves: true));
    }
}
