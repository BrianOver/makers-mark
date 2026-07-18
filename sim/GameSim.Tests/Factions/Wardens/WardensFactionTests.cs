using System.Collections.Immutable;
using GameSim.Factions;
using GameSim.Factions.Wardens;
using GameSim.Kernel;
using Xunit;

namespace GameSim.Tests.Factions.Wardens;

/// <summary>
/// Behavior tests for the Gloomwood Wardens add-on faction (the 3rd town faction, C1). Every test is
/// REGISTRATION-INDEPENDENT: it drives the faction's <see cref="WardensFaction.Definition"/> and the
/// SHARED mechanism through the explicit-set <see cref="FactionRegistry.ByOreKey(string,
/// System.Collections.Generic.IEnumerable{FactionDefinition})"/> overload — never
/// <see cref="FactionRegistry.All"/> — so the suite is green whether or not the orchestrator has
/// applied the registration line (the pack is inert until registered, and these tests prove the DATA
/// and its flow through the shared code path in either state). This mirrors the core's own
/// extensibility proof (<c>FactionConformanceTests.AddOnFaction_FlowsThroughByOreKey_WithoutJoiningRegistry</c>)
/// and the Crownsguard pack's behavior layer.
///
/// Once the orchestrator applies the registration line (with the four Gloomwood materials in the
/// registry), the parameterized <c>FactionConformanceTests</c> + <c>FactionPackTests</c> additionally
/// cover the Wardens' structural and voicing contract automatically — this suite is the pack-owned
/// behavior layer on top.
/// </summary>
public class WardensFactionTests
{
    private static readonly FactionDefinition Wardens = WardensFaction.Definition;

    // ---- Definition data (the pack's identity, C1) -----------------------------------

    [Fact]
    public void Definition_HasExpectedIdentityAndParams()
    {
        Assert.Equal("wardens", Wardens.Id);
        Assert.Equal(WardensFaction.Id, Wardens.Id);
        Assert.False(string.IsNullOrWhiteSpace(Wardens.DisplayName));
        Assert.Equal("Gloomwood Wardens", Wardens.DisplayName);

        Assert.Equal(100, Wardens.StandingCap);
        Assert.Equal(2, Wardens.RiseStep);
        Assert.Equal(1, Wardens.DriftStep);
        Assert.Equal(50, Wardens.MaxAdjustmentPerMille);
    }

    [Fact]
    public void SuppliesOwnGloomwoodMaterials_FourNatureOres()
    {
        Assert.Equal(
            new[] { "amberpitch", "greenheart", "heartwood", "moonresin" },
            Wardens.SuppliesOreKeys.OrderBy(k => k, StringComparer.Ordinal).ToArray());

        // No repeat within the faction.
        Assert.Equal(
            Wardens.SuppliesOreKeys.Length,
            Wardens.SuppliesOreKeys.Distinct(StringComparer.Ordinal).Count());
    }

    // ---- Structural contract (same shape the conformance harness enforces once registered) ----

    [Fact]
    public void StandingParams_ArePositive_AndInSaneBounds()
    {
        Assert.True(Wardens.StandingCap > 0);
        Assert.True(Wardens.RiseStep > 0);
        Assert.True(Wardens.DriftStep > 0);
        Assert.True(Wardens.MaxAdjustmentPerMille > 0);

        Assert.True(Wardens.RiseStep <= Wardens.StandingCap);
        Assert.True(Wardens.DriftStep <= Wardens.StandingCap);

        // Tariff stays a bounded nudge (< 1000 so the player never pays 0; <= 500 by convention).
        Assert.InRange(Wardens.MaxAdjustmentPerMille, 1, 500);
    }

    [Fact]
    public void HysteresisPrecondition_RisePlusDrift_IsBelowFavoredDeadband()
    {
        // The single-buy hysteresis guard the conformance harness enforces: one Morning drift plus one
        // Evening buy must not leap the favored deadband (enter − exit).
        var deadband = FactionStandingThresholds.FavoredEnter(Wardens)
                     - FactionStandingThresholds.FavoredExit(Wardens);
        Assert.True(Wardens.RiseStep + Wardens.DriftStep < deadband,
            $"RiseStep+DriftStep ({Wardens.RiseStep + Wardens.DriftStep}) must be < deadband ({deadband})");
    }

    [Fact]
    public void Thresholds_ScaleFromStandingCap()
    {
        // Named thresholds derive from the cap (cap/2 enter, cap*2/5 exit) — no per-faction data.
        Assert.Equal(50, FactionStandingThresholds.FavoredEnter(Wardens)); // 100/2
        Assert.Equal(40, FactionStandingThresholds.FavoredExit(Wardens));  // 100*2/5
    }

    // ---- Single-supplier invariant vs the other factions (R6/KTD6) -------------------

    [Fact]
    public void SuppliedKeys_AreDisjointFromEveryRegisteredFaction()
    {
        // The pack brings its OWN materials — it never contends for an ore key another faction supplies.
        foreach (var faction in FactionRegistry.All.Values)
        {
            if (faction.Id == Wardens.Id)
            {
                continue; // skip self once the orchestrator has registered the pack
            }

            foreach (var key in Wardens.SuppliesOreKeys)
            {
                Assert.DoesNotContain(key, faction.SuppliesOreKeys);
            }
        }
    }

    [Fact]
    public void CombinedWithRegistry_StillOneSupplierPerOreKey()
    {
        // Wardens alongside every registered faction: still exactly one supplier per ore key. Robust
        // whether or not the pack is registered — dedupe by id so All (which already contains Wardens
        // once registered) plus this Definition never double-counts the pack itself.
        var byId = FactionRegistry.All.Values.ToDictionary(f => f.Id, f => f, StringComparer.Ordinal);
        byId[Wardens.Id] = Wardens;

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
    public void ByOreKey_ResolvesWardens_ForItsOwnMaterials_ThroughExplicitSet()
    {
        var combined = FactionRegistry.All.Values.Append(Wardens).ToImmutableArray();

        Assert.Same(Wardens, FactionRegistry.ByOreKey("greenheart", combined));
        Assert.Same(Wardens, FactionRegistry.ByOreKey("amberpitch", combined));
        Assert.Same(Wardens, FactionRegistry.ByOreKey("moonresin", combined));
        Assert.Same(Wardens, FactionRegistry.ByOreKey("heartwood", combined));

        // The registered factions still resolve to themselves; an unknown ore still resolves to none.
        Assert.Equal(FactionRegistry.DeepveinId, FactionRegistry.ByOreKey("copper", combined)!.Id);
        Assert.Null(FactionRegistry.ByOreKey("no-such-ore", combined));
    }

    [Fact]
    public void Unregistered_WardensMaterials_ResolveToNoneInGlobalRegistry()
    {
        // Until the orchestrator applies the registration line the pack is inert: its materials are
        // supplied by nobody in the live registry. (This assertion flips to Wardens once registered;
        // it documents the inert state.)
        if (!FactionRegistry.IsRegistered("wardens"))
        {
            Assert.Null(FactionRegistry.ByOreKey("greenheart"));
            Assert.Null(FactionRegistry.ByOreKey("heartwood"));
        }
    }

    // ---- Tariff DIRECTION + bound (discount-only, KTD8) reproduced against the params ----

    [Fact]
    public void Tariff_AtPlusCap_DiscountsByMaxAdjustment_NeverBelowZeroCost()
    {
        // Reproduce the handler's aggregate pricing (OreMarketHandlers): at +cap the adjustment is the
        // full MaxAdjustmentPerMille, applied via round-to-nearest MulDiv. Positive standing => the
        // player pays LESS (discount-only direction), bounded at the Wardens' light 5% of base.
        const int baseLineCost = 100;
        var adjPerMille = Math.Clamp(
            IntegerCurves.MulDiv(Wardens.StandingCap, Wardens.MaxAdjustmentPerMille, Wardens.StandingCap),
            -(long)Wardens.MaxAdjustmentPerMille, Wardens.MaxAdjustmentPerMille);
        var playerCost = (int)IntegerCurves.MulDiv(baseLineCost, 1000 - adjPerMille, 1000);

        Assert.Equal(50, adjPerMille);        // full 5% at the cap
        Assert.Equal(95, playerCost);         // 100 - 5% = 95 (discount)
        Assert.True(playerCost < baseLineCost, "positive standing must discount");
        Assert.True(playerCost > 0, "the tariff never drives the price to zero (nudge only)");
    }

    [Fact]
    public void Tariff_AtNeutral_IsExactNoOp()
    {
        const int baseLineCost = 100;
        var adjPerMille = IntegerCurves.MulDiv(0, Wardens.MaxAdjustmentPerMille, Wardens.StandingCap);
        var playerCost = (int)IntegerCurves.MulDiv(baseLineCost, 1000 - adjPerMille, 1000);

        Assert.Equal(0, adjPerMille);
        Assert.Equal(baseLineCost, playerCost); // neutral standing is byte-identical to no tariff
    }

    // ---- Determinism duties: constant data, no RNG/float/clock ------------------------

    [Fact]
    public void Definition_IsConstant_ReferenceStableAcrossReads()
    {
        Assert.Same(WardensFaction.Definition, WardensFaction.Definition);
        Assert.False(Wardens.SuppliesOreKeys.IsDefaultOrEmpty);
    }
}
