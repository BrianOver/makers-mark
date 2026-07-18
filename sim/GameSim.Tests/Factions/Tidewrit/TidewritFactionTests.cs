using System.Collections.Immutable;
using GameSim.Factions;
using GameSim.Factions.Tidewrit;
using GameSim.Kernel;
using Xunit;

namespace GameSim.Tests.Factions.Tidewrit;

/// <summary>
/// Behavior tests for the Tidewrit Salvors add-on faction (C2, the 3rd town faction). Every test is
/// REGISTRATION-INDEPENDENT: it drives <see cref="TidewritFaction.Definition"/> and the SHARED
/// mechanism through the explicit-set <see cref="FactionRegistry.ByOreKey(string,
/// System.Collections.Generic.IEnumerable{FactionDefinition})"/> overload — never
/// <see cref="FactionRegistry.All"/> — so the suite is green whether or not the orchestrator has
/// applied the registration line (the pack is inert until registered, and these tests prove the DATA
/// and its flow through the shared code path in either state). This mirrors the core's own
/// extensibility proof (<c>FactionConformanceTests.AddOnFaction_FlowsThroughByOreKey_WithoutJoiningRegistry</c>)
/// and the Crownsguard pack's suite.
///
/// The crypt ore keys the Salvors supply are registered in the material registry ALONGSIDE this faction
/// (the C2 material registration lines); these tests deliberately do NOT read the material registry, so
/// they stay green in the committed (unregistered) state where those materials are not yet in the pool.
/// Once the orchestrator applies the registration lines, the parameterized <c>FactionConformanceTests</c>
/// + <c>FactionPackTests</c> additionally cover Tidewrit's structural and voicing contract automatically.
/// </summary>
public class TidewritFactionTests
{
    private static readonly FactionDefinition Tidewrit = TidewritFaction.Definition;

    private static readonly ImmutableArray<string> CryptOres =
        ImmutableArray.Create("verdigris", "saltglass", "bonechalk", "drowned-silver", "abyss-pearl");

    // ---- Definition data (the pack's identity, R1/R2) --------------------------------

    [Fact]
    public void Definition_HasExpectedIdentityAndParams()
    {
        Assert.Equal("tidewrit", Tidewrit.Id);
        Assert.Equal(TidewritFaction.Id, Tidewrit.Id);
        Assert.Equal("Tidewrit Salvors", Tidewrit.DisplayName);

        Assert.Equal(90, Tidewrit.StandingCap);
        Assert.Equal(4, Tidewrit.RiseStep);
        Assert.Equal(2, Tidewrit.DriftStep);
        Assert.Equal(90, Tidewrit.MaxAdjustmentPerMille);
    }

    [Fact]
    public void SuppliesOwnCryptOres_FloorOneThroughFive()
    {
        Assert.Equal(
            CryptOres.OrderBy(k => k, StringComparer.Ordinal).ToArray(),
            Tidewrit.SuppliesOreKeys.OrderBy(k => k, StringComparer.Ordinal).ToArray());

        // No repeat within the faction.
        Assert.Equal(
            Tidewrit.SuppliesOreKeys.Length,
            Tidewrit.SuppliesOreKeys.Distinct(StringComparer.Ordinal).Count());
    }

    // ---- Structural contract (same shape the conformance harness enforces once registered) ----

    [Fact]
    public void StandingParams_ArePositive_AndInSaneBounds()
    {
        Assert.True(Tidewrit.StandingCap > 0);
        Assert.True(Tidewrit.RiseStep > 0);
        Assert.True(Tidewrit.DriftStep > 0);
        Assert.True(Tidewrit.MaxAdjustmentPerMille > 0);

        Assert.True(Tidewrit.RiseStep <= Tidewrit.StandingCap);
        Assert.True(Tidewrit.DriftStep <= Tidewrit.StandingCap);

        // Tariff stays a bounded nudge (< 1000 so the player never pays 0; <= 500 by convention).
        Assert.InRange(Tidewrit.MaxAdjustmentPerMille, 1, 500);
    }

    [Fact]
    public void HysteresisPrecondition_RisePlusDrift_IsBelowFavoredDeadband()
    {
        // One Morning drift + one Evening buy must not leap the favored deadband (enter − exit), or a
        // single day-cycle could emit a contradictory cooled+favored pair.
        var deadband = FactionStandingThresholds.FavoredEnter(Tidewrit)
                     - FactionStandingThresholds.FavoredExit(Tidewrit);
        Assert.True(Tidewrit.RiseStep + Tidewrit.DriftStep < deadband,
            $"RiseStep+DriftStep ({Tidewrit.RiseStep + Tidewrit.DriftStep}) must be < deadband ({deadband})");
    }

    [Fact]
    public void Thresholds_ScaleFromStandingCap()
    {
        // Named thresholds derive from the cap (cap/2 enter, cap*2/5 exit) — no per-faction data.
        Assert.Equal(45, FactionStandingThresholds.FavoredEnter(Tidewrit)); // 90/2
        Assert.Equal(36, FactionStandingThresholds.FavoredExit(Tidewrit));  // 90*2/5
    }

    // ---- Single-supplier invariant vs the registered factions (R6/KTD6) --------------

    [Fact]
    public void SuppliedKeys_AreDisjointFromEveryRegisteredFaction()
    {
        // The pack brings its OWN crypt materials — it never contends for an ore key another faction
        // supplies (Deepvein's copper…adamant, the Crownsguard's electrum/orichalcum, etc.). Skip the
        // pack's own entry so the check is correct whether or not Tidewrit is registered in All.
        foreach (var faction in FactionRegistry.All.Values)
        {
            if (faction.Id == Tidewrit.Id)
            {
                continue;
            }

            foreach (var key in Tidewrit.SuppliesOreKeys)
            {
                Assert.DoesNotContain(key, faction.SuppliesOreKeys);
            }
        }
    }

    [Fact]
    public void CombinedWithRegistry_StillOneSupplierPerOreKey()
    {
        // Tidewrit alongside every registered faction: still exactly one supplier per ore key. Robust
        // whether or not the pack is registered — dedupe by id so All (which already contains Tidewrit
        // once registered) plus this Definition never double-counts the pack itself.
        var byId = FactionRegistry.All.Values.ToDictionary(f => f.Id, f => f, StringComparer.Ordinal);
        byId[Tidewrit.Id] = Tidewrit;

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
    public void ByOreKey_ResolvesTidewrit_ForEachCryptOre_ThroughExplicitSet()
    {
        var combined = FactionRegistry.All.Values.Append(Tidewrit).ToImmutableArray();

        foreach (var ore in CryptOres)
        {
            Assert.Same(Tidewrit, FactionRegistry.ByOreKey(ore, combined));
        }

        // The registered factions still resolve to themselves; an unknown ore still resolves to none.
        Assert.Equal(FactionRegistry.DeepveinId, FactionRegistry.ByOreKey("copper", combined)!.Id);
        Assert.Null(FactionRegistry.ByOreKey("no-such-ore", combined));
    }

    [Fact]
    public void Unregistered_TidewritOres_ResolveToNoneInGlobalRegistry()
    {
        // Until the orchestrator applies the registration line the pack is inert: its materials are
        // supplied by nobody in the live registry, so the handler leaves standing untouched for them.
        // (This assertion flips to Tidewrit once registered; it documents the inert state.)
        if (!FactionRegistry.IsRegistered("tidewrit"))
        {
            foreach (var ore in CryptOres)
            {
                Assert.Null(FactionRegistry.ByOreKey(ore));
            }
        }
    }

    // ---- Tariff DIRECTION + bound (discount-only, KTD8) reproduced against the params ----

    [Fact]
    public void Tariff_AtPlusCap_DiscountsByMaxAdjustment_NeverBelowZeroCost()
    {
        // Reproduce the handler's aggregate pricing (OreMarketHandlers): at +cap the adjustment is the
        // full MaxAdjustmentPerMille, applied via round-to-nearest MulDiv. Positive standing => the
        // player pays LESS (discount-only direction), bounded at 9% of base.
        const int baseLineCost = 100;
        var adjPerMille = Math.Clamp(
            IntegerCurves.MulDiv(Tidewrit.StandingCap, Tidewrit.MaxAdjustmentPerMille, Tidewrit.StandingCap),
            -(long)Tidewrit.MaxAdjustmentPerMille, Tidewrit.MaxAdjustmentPerMille);
        var playerCost = (int)IntegerCurves.MulDiv(baseLineCost, 1000 - adjPerMille, 1000);

        Assert.Equal(90, adjPerMille);        // full 9% at the cap
        Assert.Equal(91, playerCost);         // 100 - 9% = 91 (discount)
        Assert.True(playerCost < baseLineCost, "positive standing must discount");
        Assert.True(playerCost > 0, "the tariff never drives the price to zero (nudge only)");
    }

    [Fact]
    public void Tariff_AtNeutral_IsExactNoOp()
    {
        const int baseLineCost = 100;
        var adjPerMille = IntegerCurves.MulDiv(0, Tidewrit.MaxAdjustmentPerMille, Tidewrit.StandingCap);
        var playerCost = (int)IntegerCurves.MulDiv(baseLineCost, 1000 - adjPerMille, 1000);

        Assert.Equal(0, adjPerMille);
        Assert.Equal(baseLineCost, playerCost); // neutral standing is byte-identical to no tariff
    }

    // ---- Determinism duties: constant data ---------------------------------------------

    [Fact]
    public void Definition_IsConstant_ReferenceStableAcrossReads()
    {
        Assert.Same(TidewritFaction.Definition, TidewritFaction.Definition);
        Assert.False(Tidewrit.SuppliesOreKeys.IsDefaultOrEmpty);
    }
}
