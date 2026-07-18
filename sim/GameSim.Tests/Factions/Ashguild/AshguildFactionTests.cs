using System.Collections.Immutable;
using GameSim.Factions;
using GameSim.Factions.Ashguild;
using GameSim.Kernel;
using Xunit;

namespace GameSim.Tests.Factions.Ashguild;

/// <summary>
/// Behavior tests for the Ashguild add-on faction (the Emberfall Foundry's supplier, R2). Every test is
/// REGISTRATION-INDEPENDENT: it drives the faction's <see cref="AshguildFaction.Definition"/> and the
/// SHARED mechanism through the explicit-set <see cref="FactionRegistry.ByOreKey(string,
/// System.Collections.Generic.IEnumerable{FactionDefinition})"/> overload — never
/// <see cref="FactionRegistry.All"/> — so the suite is green whether or not the orchestrator has
/// applied the registration line (the pack is inert until registered, and these tests prove the DATA
/// and its flow through the shared code path in either state). This mirrors the core's own
/// extensibility proof (<c>FactionConformanceTests.AddOnFaction_FlowsThroughByOreKey_WithoutJoiningRegistry</c>).
///
/// Once the orchestrator applies the registration line, the parameterized
/// <c>FactionConformanceTests</c> + <c>FactionPackTests</c> additionally cover the Ashguild's structural
/// and voicing contract automatically — this suite is the pack-owned behavior layer on top.
/// </summary>
public class AshguildFactionTests
{
    private static readonly FactionDefinition Ashguild = AshguildFaction.Definition;

    // ---- Definition data (the pack's identity, R1/R2) --------------------------------

    [Fact]
    public void Definition_HasExpectedIdentityAndParams()
    {
        Assert.Equal("ashguild", Ashguild.Id);
        Assert.Equal(AshguildFaction.Id, Ashguild.Id);
        Assert.False(string.IsNullOrWhiteSpace(Ashguild.DisplayName));
        Assert.Equal("The Ashguild", Ashguild.DisplayName);

        Assert.Equal(100, Ashguild.StandingCap);
        Assert.Equal(6, Ashguild.RiseStep);
        Assert.Equal(3, Ashguild.DriftStep);
        Assert.Equal(100, Ashguild.MaxAdjustmentPerMille);
    }

    [Fact]
    public void SuppliesOwnFoundryMaterials_FirebrickThroughHeartcoal()
    {
        Assert.Equal(
            new[] { "emberglass", "firebrick", "heartcoal", "quench-salt", "slagiron" },
            Ashguild.SuppliesOreKeys.OrderBy(k => k, StringComparer.Ordinal).ToArray());

        // No repeat within the faction.
        Assert.Equal(
            Ashguild.SuppliesOreKeys.Length,
            Ashguild.SuppliesOreKeys.Distinct(StringComparer.Ordinal).Count());
    }

    // ---- Structural contract (same shape the conformance harness enforces once registered) ----

    [Fact]
    public void StandingParams_ArePositive_AndInSaneBounds()
    {
        Assert.True(Ashguild.StandingCap > 0);
        Assert.True(Ashguild.RiseStep > 0);
        Assert.True(Ashguild.DriftStep > 0);
        Assert.True(Ashguild.MaxAdjustmentPerMille > 0);

        Assert.True(Ashguild.RiseStep <= Ashguild.StandingCap);
        Assert.True(Ashguild.DriftStep <= Ashguild.StandingCap);

        // Tariff stays a bounded nudge (< 1000 so the player never pays 0; <= 500 by convention).
        Assert.InRange(Ashguild.MaxAdjustmentPerMille, 1, 500);
    }

    [Fact]
    public void HysteresisPrecondition_RisePlusDrift_IsBelowFavoredDeadband()
    {
        // The single-buy hysteresis guard the conformance harness enforces: one Morning drift plus one
        // Evening buy must not leap the favored deadband (enter − exit), or a single day-cycle could
        // emit a contradictory cooled+favored pair.
        var deadband = FactionStandingThresholds.FavoredEnter(Ashguild)
                     - FactionStandingThresholds.FavoredExit(Ashguild);
        Assert.True(Ashguild.RiseStep + Ashguild.DriftStep < deadband,
            $"RiseStep+DriftStep ({Ashguild.RiseStep + Ashguild.DriftStep}) must be < deadband ({deadband})");
    }

    [Fact]
    public void Thresholds_ScaleFromStandingCap()
    {
        // Named thresholds derive from the cap (cap/2 enter, cap*2/5 exit) — no per-faction data.
        Assert.Equal(50, FactionStandingThresholds.FavoredEnter(Ashguild)); // 100/2
        Assert.Equal(40, FactionStandingThresholds.FavoredExit(Ashguild));  // 100*2/5
    }

    // ---- Single-supplier invariant vs the registered factions (R6/KTD6) ---------------

    [Fact]
    public void SuppliedKeys_AreDisjointFromDeepvein()
    {
        // The pack brings its OWN materials — it never contends for a Mine ore key Deepvein supplies.
        foreach (var key in Ashguild.SuppliesOreKeys)
        {
            Assert.DoesNotContain(key, FactionRegistry.Deepvein.SuppliesOreKeys);
        }
    }

    [Fact]
    public void CombinedWithRegistry_StillOneSupplierPerOreKey()
    {
        // Ashguild alongside every registered faction: still exactly one supplier per ore key.
        // Robust whether or not the pack is registered — dedupe by id so All (which already contains
        // Ashguild once registered) plus this Definition never double-counts the pack itself.
        var byId = FactionRegistry.All.Values.ToDictionary(f => f.Id, f => f, StringComparer.Ordinal);
        byId[Ashguild.Id] = Ashguild;

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
    public void ByOreKey_ResolvesAshguild_ForItsOwnMaterials_ThroughExplicitSet()
    {
        var combined = FactionRegistry.All.Values.Append(Ashguild).ToImmutableArray();

        Assert.Same(Ashguild, FactionRegistry.ByOreKey("firebrick", combined));
        Assert.Same(Ashguild, FactionRegistry.ByOreKey("heartcoal", combined));

        // The registered factions still resolve to themselves; an unknown ore still resolves to none.
        Assert.Equal(FactionRegistry.DeepveinId, FactionRegistry.ByOreKey("copper", combined)!.Id);
        Assert.Null(FactionRegistry.ByOreKey("no-such-ore", combined));
    }

    [Fact]
    public void Unregistered_AshguildMaterials_ResolveToNoneInGlobalRegistry()
    {
        // Until the orchestrator applies the registration line the pack is inert: its materials are
        // supplied by nobody in the live registry, so the handler leaves standing untouched for them.
        // (This assertion flips to Ashguild once registered; it documents the inert state.)
        if (!FactionRegistry.IsRegistered("ashguild"))
        {
            Assert.Null(FactionRegistry.ByOreKey("firebrick"));
            Assert.Null(FactionRegistry.ByOreKey("heartcoal"));
        }
    }

    // ---- Tariff DIRECTION + bound (discount-only, KTD8) reproduced against the params ----

    [Fact]
    public void Tariff_AtPlusCap_DiscountsByMaxAdjustment_NeverBelowZeroCost()
    {
        // Reproduce the handler's aggregate pricing (OreMarketHandlers): at +cap the adjustment is the
        // full MaxAdjustmentPerMille, applied to the line cost via round-to-nearest MulDiv. Positive
        // standing => the player pays LESS (discount-only direction), bounded at 10% of base.
        const int baseLineCost = 100;
        var adjPerMille = Math.Clamp(
            IntegerCurves.MulDiv(Ashguild.StandingCap, Ashguild.MaxAdjustmentPerMille, Ashguild.StandingCap),
            -(long)Ashguild.MaxAdjustmentPerMille, Ashguild.MaxAdjustmentPerMille);
        var playerCost = (int)IntegerCurves.MulDiv(baseLineCost, 1000 - adjPerMille, 1000);

        Assert.Equal(100, adjPerMille);       // full 10% at the cap
        Assert.Equal(90, playerCost);         // 100 - 10% = 90 (discount)
        Assert.True(playerCost < baseLineCost, "positive standing must discount");
        Assert.True(playerCost > 0, "the tariff never drives the price to zero (nudge only)");
    }

    [Fact]
    public void Tariff_AtNeutral_IsExactNoOp()
    {
        const int baseLineCost = 100;
        var adjPerMille = IntegerCurves.MulDiv(0, Ashguild.MaxAdjustmentPerMille, Ashguild.StandingCap);
        var playerCost = (int)IntegerCurves.MulDiv(baseLineCost, 1000 - adjPerMille, 1000);

        Assert.Equal(0, adjPerMille);
        Assert.Equal(baseLineCost, playerCost); // neutral standing is byte-identical to no tariff
    }

    // ---- Determinism duties: constant data, no RNG/float/clock ------------------------

    [Fact]
    public void Definition_IsConstant_ReferenceStableAcrossReads()
    {
        Assert.Same(AshguildFaction.Definition, AshguildFaction.Definition);
        Assert.False(Ashguild.SuppliesOreKeys.IsDefaultOrEmpty);
    }
}
