using System.Collections.Immutable;
using GameSim.Factions;
using GameSim.Materials;
using GameSim.Venues;
using Xunit;

namespace GameSim.Tests.Factions;

/// <summary>
/// The add-on conformance harness for the town-faction core (P5 U1, mirrors
/// <c>VenueConformanceTests</c>/<c>ClassConformanceTests</c>): every faction in
/// <see cref="FactionRegistry.All"/> is validated structurally, so an add-on faction's definition
/// of done is mechanical — register the faction (R1/R2) and make THIS suite green. New factions get
/// covered automatically; no edits needed here.
///
/// Two anchors sit alongside the parameterized checks:
/// <list type="bullet">
/// <item><see cref="ByOreKey_ResolvesTheSupplier_ForEachMineOreKey"/> pins the reference faction's
/// ore-key → supplier lookup (R6/KTD6) the U3 handler will read.</item>
/// <item><see cref="AddOnFaction_FlowsThroughByOreKey_WithoutJoiningRegistry"/> is the extensibility
/// proof (mirrors P4's test-only reference venue and P3's test-only 4th class, R3): a test-only
/// second faction with its own materials and params flows through the SAME
/// <see cref="FactionRegistry.ByOreKey(string, System.Collections.Generic.IEnumerable{FactionDefinition})"/>
/// lookup end-to-end — never registered in <see cref="FactionRegistry.All"/>, never referenced
/// elsewhere.</item>
/// </list>
/// </summary>
public class FactionConformanceTests
{
    /// <summary>The ore keys a registered faction may legitimately supply — every material in
    /// <see cref="MaterialRegistry.All"/> (M1: the single source of truth for price + grade). This is
    /// the G8 flip: the pin now reads the registry, not the frozen Mine floor list, so a registered
    /// faction may supply ANY registered material — the five Mine ores AND add-on materials such as
    /// the Crownsguard's electrum/orichalcum. (Registering the Crownsguard turns this suite green.)</summary>
    private static readonly ImmutableHashSet<string> KnownOreKeys =
        MaterialRegistry.All.Keys.ToImmutableHashSet(StringComparer.Ordinal);

    public static TheoryData<string> AllFactionIds()
    {
        var data = new TheoryData<string>();
        foreach (var id in FactionRegistry.All.Keys)
        {
            data.Add(id);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllFactionIds))]
    public void Identity_IdMatchesKey_AndDisplayNamePresent(string id)
    {
        var faction = FactionRegistry.All[id];
        Assert.Equal(id, faction.Id);
        Assert.False(string.IsNullOrWhiteSpace(faction.DisplayName));
    }

    [Theory]
    [MemberData(nameof(AllFactionIds))]
    public void StandingParams_ArePositive_AndInSaneBounds(string id)
    {
        var faction = FactionRegistry.All[id];

        // Every standing→tariff parameter is a positive integer (R1/KTD1); zero or negative would
        // make standing inert or the tariff run backwards.
        Assert.True(faction.StandingCap > 0, $"{id}: StandingCap must be positive");
        Assert.True(faction.RiseStep > 0, $"{id}: RiseStep must be positive");
        Assert.True(faction.DriftStep > 0, $"{id}: DriftStep must be positive");
        Assert.True(faction.MaxAdjustmentPerMille > 0, $"{id}: MaxAdjustmentPerMille must be positive");

        // A single rise/drift step never leaps the whole range — standing moves gradually.
        Assert.True(faction.RiseStep <= faction.StandingCap, $"{id}: RiseStep exceeds StandingCap");
        Assert.True(faction.DriftStep <= faction.StandingCap, $"{id}: DriftStep exceeds StandingCap");

        // Hysteresis precondition (P5 U4 review): one Morning drift + one Evening buy must not
        // leap the favored deadband (enter − exit), or a SINGLE buy/evening could emit a
        // contradictory cooled+favored pair. Multi-buy Evenings are additionally caught by the
        // batch collapse in GossipGenerator; this guard keeps add-on factions honest for the
        // single-buy case the deadband proof relies on.
        var deadband = FactionStandingThresholds.FavoredEnter(faction) - FactionStandingThresholds.FavoredExit(faction);
        Assert.True(
            faction.RiseStep + faction.DriftStep < deadband,
            $"{id}: RiseStep+DriftStep ({faction.RiseStep + faction.DriftStep}) must be < favored deadband ({deadband}) for hysteresis");

        // The tariff stays a bounded NUDGE (R8/KTD8): the cap adjustment is < 1000 per-mille (< 100%,
        // so the player never pays 0 or negative) and light-touch (<= 500 = <= 50%).
        Assert.InRange(faction.MaxAdjustmentPerMille, 1, 500);
    }

    [Theory]
    [MemberData(nameof(AllFactionIds))]
    public void SuppliesOreKeys_AreKnown_NonEmpty_AndUniqueWithinTheFaction(string id)
    {
        var faction = FactionRegistry.All[id];

        Assert.False(faction.SuppliesOreKeys.IsDefaultOrEmpty, $"{id}: SuppliesOreKeys is empty");

        foreach (var oreKey in faction.SuppliesOreKeys)
        {
            Assert.False(string.IsNullOrWhiteSpace(oreKey), $"{id}: blank ore key");

            // Registered material (M1): a faction may only supply a material the registry knows and
            // prices (R6). Reads the registry directly — so an add-on material (electrum/orichalcum)
            // that OrePricing does not price on the frozen live path is still a valid supply key.
            Assert.Contains(oreKey, KnownOreKeys);
            Assert.True(MaterialRegistry.UnitPrice(oreKey) > 0, $"{id}: ore key '{oreKey}' has no positive price");
        }

        // No ore key repeats within one faction's supply list.
        Assert.Equal(faction.SuppliesOreKeys.Length, faction.SuppliesOreKeys.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void NoTwoFactions_SupplyTheSameOreKey()
    {
        // Single supplier per ore key in this core (R6/KTD6 — the handler resolves ONE faction per
        // ore key). Add-on factions bring their own materials; they never contend for an ore key.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var faction in FactionRegistry.All.Values)
        {
            foreach (var oreKey in faction.SuppliesOreKeys)
            {
                Assert.True(seen.Add(oreKey), $"ore key '{oreKey}' is supplied by more than one faction");
            }
        }
    }

    [Fact]
    public void ByOreKey_ResolvesTheSupplier_ForEachMineOreKey()
    {
        // Every Mine ore key resolves to the reference faction the U3 tariff handler will read.
        foreach (var floor in VenueRegistry.Mine.Floors)
        {
            var supplier = FactionRegistry.ByOreKey(floor.OreKey);
            Assert.NotNull(supplier);
            Assert.Equal(FactionRegistry.DeepveinId, supplier!.Id);
            Assert.Contains(floor.OreKey, supplier.SuppliesOreKeys);
        }
    }

    [Fact]
    public void ByOreKey_ReturnsNull_ForUnknownOreKey()
    {
        Assert.Null(FactionRegistry.ByOreKey("no-such-ore"));
    }

    [Fact]
    public void Registry_TryGet_IsRegistered_AndRequire_Behave()
    {
        Assert.True(FactionRegistry.TryGet(FactionRegistry.DeepveinId, out var found));
        Assert.NotNull(found);
        Assert.Equal(FactionRegistry.DeepveinId, found!.Id);
        Assert.True(FactionRegistry.IsRegistered(FactionRegistry.DeepveinId));

        // "no-such-faction" is the never-registered example (was "crownsguard" until that
        // pack shipped as content — its registration awaits the material-registry core gate).
        Assert.False(FactionRegistry.TryGet("no-such-faction", out var missing));
        Assert.Null(missing);
        Assert.False(FactionRegistry.IsRegistered("no-such-faction"));
        Assert.Throws<KeyNotFoundException>(() => FactionRegistry.Require("no-such-faction"));

        Assert.Same(FactionRegistry.Deepvein, FactionRegistry.Require(FactionRegistry.DeepveinId));
    }

    // ---- Extensibility proof (no live second faction in this core, R3) ------------------------

    /// <summary>
    /// A test-only reference faction with a shape NO built-in has: its own material
    /// (<c>obsidian</c>, not a Mine ore) and its own standing→tariff params. If the lookup reads the
    /// definition — not a hardcoded Deepvein — it resolves against this data alone.
    /// </summary>
    private static FactionDefinition GraniteOrder() => new(
        Id: "granite-order",
        DisplayName: "The Granite Order",
        SuppliesOreKeys: ImmutableArray.Create("obsidian"),
        StandingCap: 60,
        RiseStep: 3,
        DriftStep: 1,
        MaxAdjustmentPerMille: 75);

    [Fact]
    public void AddOnFaction_FlowsThroughByOreKey_WithoutJoiningRegistry()
    {
        var order = GraniteOrder();

        // Defined and used, but NEVER registered — the add-on shape.
        Assert.False(FactionRegistry.IsRegistered(order.Id));
        Assert.DoesNotContain(order.Id, FactionRegistry.All.Keys);

        // Its params satisfy the same structural contract the registered factions do.
        Assert.True(order.StandingCap > 0);
        Assert.True(order.RiseStep > 0 && order.RiseStep <= order.StandingCap);
        Assert.True(order.DriftStep > 0 && order.DriftStep <= order.StandingCap);
        Assert.InRange(order.MaxAdjustmentPerMille, 1, 500);

        // Combined with the registered factions there is still one supplier per ore key: the add-on
        // brings its own material and never contends for a Mine ore key.
        var combined = FactionRegistry.All.Values.Append(order).ToImmutableArray();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var faction in combined)
        {
            foreach (var oreKey in faction.SuppliesOreKeys)
            {
                Assert.True(seen.Add(oreKey), $"ore key '{oreKey}' supplied by more than one faction");
            }
        }

        // The add-on flows through the SAME ByOreKey lookup the handler uses — its own material
        // resolves to it, the registered factions still resolve to themselves, and an unknown ore
        // still resolves to none.
        Assert.Same(order, FactionRegistry.ByOreKey("obsidian", combined));
        Assert.Equal(FactionRegistry.DeepveinId, FactionRegistry.ByOreKey("copper", combined)!.Id);
        Assert.Null(FactionRegistry.ByOreKey("no-such-ore", combined));

        // And it never leaked into the global registry via the lookup.
        Assert.Null(FactionRegistry.ByOreKey("obsidian"));
    }
}
