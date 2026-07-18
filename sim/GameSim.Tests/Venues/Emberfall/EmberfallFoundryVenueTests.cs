using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Expedition;
using GameSim.Kernel;
using GameSim.Venues;
using GameSim.Venues.Emberfall;
using Xunit;

namespace GameSim.Tests.Venues.Emberfall;

/// <summary>
/// Behavior tests for the Emberfall Foundry add-on venue. Every test is REGISTRATION-INDEPENDENT: it
/// drives <see cref="EmberfallFoundryVenue.Definition"/> and the shared pure pipeline
/// (<see cref="ExpeditionResolver"/> / <see cref="AttributionEngine"/>) DIRECTLY — never
/// <see cref="VenueRegistry.All"/> — so the suite is green whether or not the orchestrator has applied
/// the registration line (the pack is inert until registered, and these tests prove the DATA and its
/// flow through the shared code path in either state). This mirrors the core's own extensibility proof
/// (<c>VenueConformanceTests.AddOnVenue_ResolvesEndToEnd_WithoutJoiningLiveRotation</c>).
///
/// Once the orchestrator applies the registration line, the parameterized <c>VenueConformanceTests</c>
/// additionally cover the Foundry's structural contract automatically — this suite is the pack-owned
/// behavior layer on top.
/// </summary>
public class EmberfallFoundryVenueTests
{
    private static readonly VenueDefinition Foundry = EmberfallFoundryVenue.Definition;

    private static readonly ImmutableArray<string> FoundryKinds =
        ImmutableArray.Create("Cinder Imp", "Slag Hound", "The Bellows-Mad", "Molten Archivist", "The Undying Forge-Heart");

    private static readonly ImmutableArray<string> FoundryOres =
        ImmutableArray.Create("firebrick", "slagiron", "quench-salt", "emberglass", "heartcoal");

    // ---- Definition data (the pack's identity) -----------------------------------------

    [Fact]
    public void Definition_HasExpectedIdentity_AndFiveFloors()
    {
        Assert.Equal("emberfall", Foundry.Id);
        Assert.Equal(EmberfallFoundryVenue.Id, Foundry.Id);
        Assert.Equal("The Emberfall Foundry", Foundry.DisplayName);
        Assert.Equal(5, Foundry.FloorCount);
        Assert.Equal(Foundry.FloorCount, Foundry.Floors.Length);
    }

    [Fact]
    public void Floors_PinTheNamedGatesKindsAndOres()
    {
        var gate = new[] { 0, 15, 35, 60, 100 };
        for (var floor = 1; floor <= 5; floor++)
        {
            Assert.Equal(floor, Foundry.Floors[floor - 1].Floor); // ascending, 1-based, index 0 == floor 1
            Assert.Equal(gate[floor - 1], Foundry.Gate(floor));
            Assert.Equal(FoundryKinds[floor - 1], Foundry.MonsterKind(floor));
            Assert.Equal(FoundryOres[floor - 1], Foundry.OreKey(floor));
        }
    }

    [Fact]
    public void Floors_AreStructurallyWellFormed()
    {
        for (var floor = 1; floor <= Foundry.FloorCount; floor++)
        {
            Assert.False(string.IsNullOrWhiteSpace(Foundry.MonsterKind(floor)));
            Assert.False(string.IsNullOrWhiteSpace(Foundry.OreKey(floor)));

            Assert.InRange(Foundry.Gate(floor), 0, int.MaxValue);
            Assert.True(Foundry.MonsterHp(floor) > 0);
            Assert.True(Foundry.MonsterAttack(floor) > 0);
            Assert.True(Foundry.MonsterDefense(floor) > 0);
            Assert.True(Foundry.GoldPerKill(floor) > 0);

            // OreFloor inverts OreKey and so guards ore-key uniqueness within the venue.
            Assert.Equal(floor, Foundry.OreFloor(Foundry.OreKey(floor)));
        }
    }

    [Fact]
    public void Gates_AreNonDecreasing_WithDepth()
    {
        for (var floor = 2; floor <= Foundry.FloorCount; floor++)
        {
            Assert.True(
                Foundry.Gate(floor) >= Foundry.Gate(floor - 1),
                $"gate on floor {floor} ({Foundry.Gate(floor)}) drops below floor {floor - 1} ({Foundry.Gate(floor - 1)})");
        }
    }

    [Fact]
    public void OreKeys_AreUnique_AndDisjointFromEveryOtherVenue()
    {
        Assert.Equal(FoundryOres.Length, FoundryOres.Distinct(StringComparer.Ordinal).Count());

        // Bring-your-own materials: no Foundry ore collides with a Mine ore key …
        foreach (var ore in FoundryOres)
        {
            Assert.Equal(0, VenueRegistry.Mine.OreFloor(ore));
        }

        // … nor with any OTHER registered venue's ore ladder (Gloomwood, Sunken Crypt, …).
        foreach (var venue in VenueRegistry.All.Values.Where(v => v.Id != Foundry.Id))
        {
            foreach (var ore in FoundryOres)
            {
                Assert.Equal(0, venue.OreFloor(ore));
            }
        }
    }

    // ---- Resolver + attribution read THIS venue's data end-to-end (registration-independent) ----

    [Fact]
    public void ResolvesEndToEnd_ThroughTheSharedPipeline_OnItsOwnData()
    {
        var weapon = PlayerWeapon(1, attack: 40);
        var items = ImmutableSortedDictionary<int, Item>.Empty.Add(weapon.Id.Value, weapon);

        var sawKillingBlowBeat = false;
        var clearedAnyFloor = false;
        for (ulong seed = 0; seed < 200; seed++)
        {
            var hero = Smith(1, weapon.Id);
            var result = ExpeditionResolver.Resolve(
                ImmutableList.Create(hero), items, Foundry, targetFloor: 3, new Pcg32(RngState.FromSeed(seed)));

            // Tagged with THIS venue — not the Mine default.
            Assert.Equal("emberfall", result.VenueId);

            // Clamped to the venue's 5 floors (targetFloor 3 here) — never a Mine kind leaking in.
            Assert.All(result.Floors, f => Assert.InRange(f.Floor, 1, 5));
            foreach (var floor in result.Floors)
            {
                foreach (var combat in floor.Combats)
                {
                    Assert.Contains(combat.MonsterKind, FoundryKinds);
                    Assert.DoesNotContain(combat.MonsterKind, VenueRegistry.Mine.Floors.Select(fl => fl.MonsterKind));
                }

                if (floor.Cleared)
                {
                    clearedAnyFloor = true;
                }
            }

            // Loot rarity came from THIS venue's ore ladder.
            foreach (var loot in result.Loot)
            {
                Assert.Contains(loot.MaterialKey, FoundryOres);
            }

            // Attribution recomputed over the SAME venue is byte-identical (forward + counterfactual
            // passes share one venue, KTD6), and a killing-blow beat names a Foundry monster.
            var recomputed = AttributionEngine.ComputeBeats(result.Floors, ImmutableList.Create(hero), items, Foundry);
            Assert.Equal(result.Beats, recomputed);

            foreach (var beat in result.Beats.Where(b => b.Beat == BeatType.KillingBlow))
            {
                sawKillingBlowBeat = true;
                Assert.Contains(FoundryKinds, k => beat.Detail.Contains(k, StringComparison.Ordinal));
            }
        }

        Assert.True(clearedAnyFloor, "foundry never cleared a floor — scenario needs retuning");
        Assert.True(sawKillingBlowBeat, "no killing-blow beat on foundry data — attribution not venue-driven");
    }

    // ---- Determinism duties: constant data ---------------------------------------------

    [Fact]
    public void Definition_IsConstant_ReferenceStableAcrossReads()
    {
        Assert.Same(EmberfallFoundryVenue.Definition, EmberfallFoundryVenue.Definition);
    }

    private static Item PlayerWeapon(int id, int attack) => new(
        new ItemId(id), "forge-hammer", "Forge Hammer", ItemSlot.Weapon, QualityGrade.Fine,
        new ItemStats(attack, 0, 4), new MakersMark("You", CraftedOnDay: 1),
        ImmutableList<ItemHistoryEntry>.Empty);

    private static Hero Smith(int id, ItemId weapon) => new(
        new HeroId(id), $"Smith{id}", "vanguard", Level: 8, MaxHp: 300, Gold: 50,
        new GearSet(weapon, null, null), ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 0, DiedOnDay: null);
}
