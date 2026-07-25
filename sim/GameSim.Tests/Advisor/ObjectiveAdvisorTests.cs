using GameSim;
using GameSim.Advisor;
using GameSim.Contracts;
using GameSim.Economy;
using GameSim.Harness;
using GameSim.Kernel;
using GameSim.Materials;
using GameSim.Professions;

namespace GameSim.Tests.Advisor;

/// <summary>
/// Plan 2026-07-19-002 U10 test scenarios: fresh-game Suggest returns buy-material first when the
/// shelf is empty and gold covers the cheapest quote; Suggest never proposes an illegal action
/// across a driven run; a destitute state's top suggestion names the same material
/// <see cref="DestitutionRecoverySystem"/> is about to buy the player up to.
/// </summary>
public class ObjectiveAdvisorTests
{
    private const ulong Seed = 4242;

    [Fact]
    public void FreshGame_Suggests_BuyMaterialFirst_WhenShelfEmptyAndGoldCoversQuote()
    {
        var state = GameComposition.NewCampaign(Seed);
        Assert.Empty(state.Player.Shelf);
        Assert.Equal(DayPhase.Morning, state.Phase);

        var suggestions = ObjectiveAdvisor.Suggest(state);

        Assert.NotEmpty(suggestions);
        var first = suggestions[0];
        var buy = Assert.IsType<BuyMaterialAction>(first.Action);
        Assert.True(MaterialRegistry.IsPriced(buy.MaterialKey));
        Assert.True(MaterialVendorHandlers.QuoteCost(buy.MaterialKey, buy.Quantity) <= state.Player.Gold);
        Assert.True(ActionLegality.IsLegal(state, buy, state.Phase));
    }

    [Fact]
    public void Suggest_NeverProposesAnIllegalAction_AcrossADrivenRun()
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(Seed);
        var checkedAny = false;

        for (var tick = 0; tick < 30 * 5; tick++)
        {
            foreach (var suggestion in ObjectiveAdvisor.Suggest(state))
            {
                if (suggestion.Action is null)
                {
                    continue;
                }

                checkedAny = true;
                Assert.True(ActionLegality.IsLegal(state, suggestion.Action, state.Phase),
                    $"Day {state.Day} phase {state.Phase}: Suggest proposed an illegal action {suggestion.Action} ({suggestion.Reason}).");
            }

            state = kernel.Tick(state, BaselinePlayer.ActionsFor(state)).NewState;
        }

        Assert.True(checkedAny, "The driven run never produced a single actionable suggestion — the test is vacuous.");
    }

    [Fact]
    public void DestituteState_TopSuggestion_NamesSameMaterial_DestitutionRecoveryWouldBuy()
    {
        // Construct a true dead-end (mirrors DestitutionRecoverySystem's 3 conditions): no gold,
        // no stockable player craft, empty shelf.
        var fresh = GameComposition.NewCampaign(Seed);
        var destitute = fresh with
        {
            Phase = DayPhase.Morning,
            Player = fresh.Player with { Gold = 0, Materials = fresh.Player.Materials.Clear() },
        };

        // Independently compute the same cheapest-path material DestitutionRecoverySystem would
        // top the player up for (its private algorithm, reproduced here read-only for the assertion —
        // NOT calling into Advisor's copy, so this pins against the SYSTEM, not against itself).
        var minQuantity = int.MaxValue;
        foreach (var recipe in ProfessionRegistry.AllRecipes.Values)
        {
            if (recipe.Tier == 1 && destitute.Player.IsSelected(recipe.Profession))
            {
                minQuantity = Math.Min(minQuantity, recipe.MaterialQuantity);
            }
        }

        string? expectedKey = null;
        var expectedCost = int.MaxValue;
        foreach (var key in MaterialRegistry.PricedPool)
        {
            var cost = MaterialVendorHandlers.QuoteCost(key, minQuantity);
            if (cost < expectedCost)
            {
                expectedCost = cost;
                expectedKey = key;
            }
        }

        var suggestions = ObjectiveAdvisor.Suggest(destitute);
        Assert.NotEmpty(suggestions);
        var top = suggestions[0];

        // No legal action exists yet (gold 0 < expectedCost) — Suggest names the material without
        // proposing an action the kernel would reject.
        Assert.Null(top.Action);
        Assert.Contains(expectedKey!, top.Reason);

        // And DestitutionRecoverySystem, run once, tops the purse up to exactly that quote.
        var system = new DestitutionRecoverySystem();
        var afterRecovery = system.Process(destitute, new Pcg32(destitute.Rng), new NullSink());
        Assert.True(afterRecovery.Player.Gold >= expectedCost);
    }

    private sealed class NullSink : IEventSink
    {
        public void Emit(GameEvent gameEvent)
        {
        }
    }

    /// <summary>
    /// U8 (plan 2026-07-25-001): the audit's most durable FR-4 symptom was that
    /// <see cref="ObjectiveAdvisor.Suggest"/>'s TOP pick was frozen at a single distinct value for the
    /// whole 15-day seed-2026 run ("one suggestion verbatim for 15+ days, T4" —
    /// docs/design/2026-07-25-core-interaction-audit.md, FR-4; baseline distinct count PINNED here as
    /// <c>1</c>). U8's demand-driven reordering must move that to >= 3 distinct top suggestions
    /// across the same 15 days, same seed.
    /// </summary>
    [Fact]
    public void Seed2026_15DayRun_TopSuggestion_ChangesAtLeastThreeTimes()
    {
        const int auditBaselineDistinctCount = 1; // FR-4 pin — pre-U8 behavior, cited above
        const int requiredDistinctCount = 3; // R5/U8: ">= 3 distinct suggestions over 15 days"

        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(2026);
        var distinctTopReasons = new HashSet<string>(StringComparer.Ordinal);
        var referencedCommissionOrStall = false;

        for (var tick = 0; tick < 15 * 5; tick++)
        {
            var suggestions = ObjectiveAdvisor.Suggest(state);
            if (suggestions.Count > 0)
            {
                distinctTopReasons.Add(suggestions[0].Reason);
                if (suggestions[0].Action is AcceptCommissionAction || suggestions[0].Reason.Contains("stalled", StringComparison.Ordinal))
                {
                    referencedCommissionOrStall = true;
                }
            }

            state = kernel.Tick(state, BaselinePlayer.ActionsFor(state)).NewState;
        }

        Assert.True(distinctTopReasons.Count >= requiredDistinctCount,
            $"Top suggestion changed only {distinctTopReasons.Count} time(s) across the 15-day seed-2026 " +
            $"run — expected >= {requiredDistinctCount} (audit baseline was {auditBaselineDistinctCount}).");
        Assert.True(referencedCommissionOrStall,
            "The 15-day seed-2026 run never surfaced an open-commission or depth-stall top suggestion — " +
            "R5's demand-aware requirement never actually fired in this run.");
    }

    /// <summary>
    /// U8: six hero deaths must produce a death-adjacent (<see cref="HonorMemorialAction"/>)
    /// suggestion within the phase (Evening) it first becomes legal — the thin bridge to Phase A's
    /// Legend Engine (plan's Roadmap-overlap note).
    /// </summary>
    [Fact]
    public void SixDeaths_ProduceADeathAdjacentSuggestion_WithinOnePhase()
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(Seed);
        var deaths = 0;

        for (var tick = 0; tick < 150 * 5 && deaths < 6; tick++)
        {
            var result = kernel.Tick(state, BaselinePlayer.ActionsFor(state));
            deaths += result.Events.OfType<HeroDied>().Count();
            state = result.NewState;
        }

        Assert.True(deaths >= 6, $"Only {deaths} hero death(s) occurred in 150 days at seed {Seed} — this scenario needs at least 6.");

        // A memorial raised on the death-revealing Evening becomes honorable starting the NEXT
        // Evening tick (FarewellHandlers' own contract) — scan forward through the following two
        // Evenings (one full day cycle) for the death-adjacent suggestion to appear.
        var found = false;
        for (var tick = 0; tick < 10 && !found; tick++)
        {
            if (state.Phase == DayPhase.Evening)
            {
                found = ObjectiveAdvisor.Suggest(state).Any(s => s.Action is HonorMemorialAction);
            }

            if (!found)
            {
                state = kernel.Tick(state, BaselinePlayer.ActionsFor(state)).NewState;
            }
        }

        Assert.True(found, "No HonorMemorial suggestion appeared within the two Evenings following the 6th hero death.");
    }
}
