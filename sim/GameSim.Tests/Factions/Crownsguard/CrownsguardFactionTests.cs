using System.Collections.Immutable;
using GameSim.Factions;
using GameSim.Factions.Crownsguard;
using GameSim.Kernel;
using Xunit;

namespace GameSim.Tests.Factions.Crownsguard;

/// <summary>
/// Behavior tests for the Crownsguard Armory add-on faction (the 2nd town faction, R2). Every test is
/// REGISTRATION-INDEPENDENT: it drives the faction's <see cref="CrownsguardFaction.Definition"/> and
/// the SHARED mechanism through the explicit-set <see cref="FactionRegistry.ByOreKey(string,
/// System.Collections.Generic.IEnumerable{FactionDefinition})"/> overload — never
/// <see cref="FactionRegistry.All"/> — so the suite is green whether or not the orchestrator has
/// applied the registration line (the pack is inert until registered, and these tests prove the DATA
/// and its flow through the shared code path in either state). This mirrors the core's own
/// extensibility proof (<c>FactionConformanceTests.AddOnFaction_FlowsThroughByOreKey_WithoutJoiningRegistry</c>).
///
/// Once the orchestrator applies the registration line, the parameterized
/// <c>FactionConformanceTests</c> + <c>FactionPackTests</c> additionally cover Crownsguard's structural
/// and voicing contract automatically — this suite is the pack-owned behavior layer on top.
/// </summary>
public class CrownsguardFactionTests
{
    private static readonly FactionDefinition Crownsguard = CrownsguardFaction.Definition;

    // ---- Definition data (the pack's identity, R1/R2) --------------------------------

    [Fact]
    public void Definition_HasExpectedIdentityAndParams()
    {
        Assert.Equal("crownsguard", Crownsguard.Id);
        Assert.Equal(CrownsguardFaction.Id, Crownsguard.Id);
        Assert.False(string.IsNullOrWhiteSpace(Crownsguard.DisplayName));
        Assert.Equal("Crownsguard Armory", Crownsguard.DisplayName);

        Assert.Equal(120, Crownsguard.StandingCap);
        Assert.Equal(4, Crownsguard.RiseStep);
        Assert.Equal(3, Crownsguard.DriftStep);
        Assert.Equal(80, Crownsguard.MaxAdjustmentPerMille);
    }

    [Fact]
    public void SuppliesOwnRegalMaterials_ElectrumAndOrichalcum()
    {
        Assert.Equal(
            new[] { "electrum", "orichalcum" },
            Crownsguard.SuppliesOreKeys.OrderBy(k => k, StringComparer.Ordinal).ToArray());

        // No repeat within the faction.
        Assert.Equal(
            Crownsguard.SuppliesOreKeys.Length,
            Crownsguard.SuppliesOreKeys.Distinct(StringComparer.Ordinal).Count());
    }

    // ---- Structural contract (same shape the conformance harness enforces once registered) ----

    [Fact]
    public void StandingParams_ArePositive_AndInSaneBounds()
    {
        Assert.True(Crownsguard.StandingCap > 0);
        Assert.True(Crownsguard.RiseStep > 0);
        Assert.True(Crownsguard.DriftStep > 0);
        Assert.True(Crownsguard.MaxAdjustmentPerMille > 0);

        Assert.True(Crownsguard.RiseStep <= Crownsguard.StandingCap);
        Assert.True(Crownsguard.DriftStep <= Crownsguard.StandingCap);

        // Tariff stays a bounded nudge (< 1000 so the player never pays 0; <= 500 by convention).
        Assert.InRange(Crownsguard.MaxAdjustmentPerMille, 1, 500);
    }

    [Fact]
    public void HysteresisPrecondition_RisePlusDrift_IsBelowFavoredDeadband()
    {
        // The single-buy hysteresis guard the conformance harness enforces: one Morning drift plus one
        // Evening buy must not leap the favored deadband (enter − exit), or a single day-cycle could
        // emit a contradictory cooled+favored pair.
        var deadband = FactionStandingThresholds.FavoredEnter(Crownsguard)
                     - FactionStandingThresholds.FavoredExit(Crownsguard);
        Assert.True(Crownsguard.RiseStep + Crownsguard.DriftStep < deadband,
            $"RiseStep+DriftStep ({Crownsguard.RiseStep + Crownsguard.DriftStep}) must be < deadband ({deadband})");
    }

    [Fact]
    public void Thresholds_ScaleFromStandingCap()
    {
        // Named thresholds derive from the cap (cap/2 enter, cap*2/5 exit) — no per-faction data.
        Assert.Equal(60, FactionStandingThresholds.FavoredEnter(Crownsguard)); // 120/2
        Assert.Equal(48, FactionStandingThresholds.FavoredExit(Crownsguard));  // 120*2/5
    }

    // ---- Single-supplier invariant vs Deepvein (R6/KTD6) -----------------------------

    [Fact]
    public void SuppliedKeys_AreDisjointFromDeepvein()
    {
        // The pack brings its OWN materials — it never contends for a Mine ore key Deepvein supplies.
        foreach (var key in Crownsguard.SuppliesOreKeys)
        {
            Assert.DoesNotContain(key, FactionRegistry.Deepvein.SuppliesOreKeys);
        }
    }

    [Fact]
    public void CombinedWithRegistry_StillOneSupplierPerOreKey()
    {
        // Crownsguard alongside every registered faction: still exactly one supplier per ore key.
        // Robust whether or not the pack is registered — dedupe by id so All (which already contains
        // Crownsguard once registered) plus this Definition never double-counts the pack itself.
        var byId = FactionRegistry.All.Values.ToDictionary(f => f.Id, f => f, StringComparer.Ordinal);
        byId[Crownsguard.Id] = Crownsguard;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var faction in byId.Values)
        {
            foreach (var key in faction.SuppliesOreKeys)
            {
                Assert.True(seen.Add(key), $"ore key '{key}' supplied by more than one faction");
            }
        }
    }

    // ---- Ore-key -> supplier resolution through the SAME lookup the handler uses ------

    [Fact]
    public void ByOreKey_ResolvesCrownsguard_ForItsOwnMaterials_ThroughExplicitSet()
    {
        var combined = FactionRegistry.All.Values.Append(Crownsguard).ToImmutableArray();

        Assert.Same(Crownsguard, FactionRegistry.ByOreKey("electrum", combined));
        Assert.Same(Crownsguard, FactionRegistry.ByOreKey("orichalcum", combined));

        // The registered factions still resolve to themselves; an unknown ore still resolves to none.
        Assert.Equal(FactionRegistry.DeepveinId, FactionRegistry.ByOreKey("copper", combined)!.Id);
        Assert.Null(FactionRegistry.ByOreKey("no-such-ore", combined));
    }

    [Fact]
    public void Unregistered_CrownsguardMaterials_ResolveToNoneInGlobalRegistry()
    {
        // Until the orchestrator applies the registration line the pack is inert: its materials are
        // supplied by nobody in the live registry, so the handler leaves standing untouched for them.
        // (This assertion flips to Crownsguard once registered; it documents the inert state.)
        if (!FactionRegistry.IsRegistered("crownsguard"))
        {
            Assert.Null(FactionRegistry.ByOreKey("electrum"));
            Assert.Null(FactionRegistry.ByOreKey("orichalcum"));
        }
    }

    // ---- Tariff DIRECTION + bound (discount-only, KTD8) reproduced against the params ----

    [Fact]
    public void Tariff_AtPlusCap_DiscountsByMaxAdjustment_NeverBelowZeroCost()
    {
        // Reproduce the handler's aggregate pricing (OreMarketHandlers): at +cap the adjustment is the
        // full MaxAdjustmentPerMille, applied to the line cost via round-to-nearest MulDiv. Positive
        // standing => the player pays LESS (discount-only direction), bounded at 8% of base.
        const int baseLineCost = 100;
        var adjPerMille = Math.Clamp(
            IntegerCurves.MulDiv(Crownsguard.StandingCap, Crownsguard.MaxAdjustmentPerMille, Crownsguard.StandingCap),
            -(long)Crownsguard.MaxAdjustmentPerMille, Crownsguard.MaxAdjustmentPerMille);
        var playerCost = (int)IntegerCurves.MulDiv(baseLineCost, 1000 - adjPerMille, 1000);

        Assert.Equal(80, adjPerMille);        // full 8% at the cap
        Assert.Equal(92, playerCost);         // 100 - 8% = 92 (discount)
        Assert.True(playerCost < baseLineCost, "positive standing must discount");
        Assert.True(playerCost > 0, "the tariff never drives the price to zero (nudge only)");
    }

    [Fact]
    public void Tariff_AtNeutral_IsExactNoOp()
    {
        const int baseLineCost = 100;
        var adjPerMille = IntegerCurves.MulDiv(0, Crownsguard.MaxAdjustmentPerMille, Crownsguard.StandingCap);
        var playerCost = (int)IntegerCurves.MulDiv(baseLineCost, 1000 - adjPerMille, 1000);

        Assert.Equal(0, adjPerMille);
        Assert.Equal(baseLineCost, playerCost); // neutral standing is byte-identical to no tariff
    }

    // ---- Determinism duties: constant data, no RNG/float/clock ------------------------

    [Fact]
    public void Definition_IsConstant_ReferenceStableAcrossReads()
    {
        Assert.Same(CrownsguardFaction.Definition, CrownsguardFaction.Definition);
        Assert.False(Crownsguard.SuppliesOreKeys.IsDefaultOrEmpty);
    }
}
