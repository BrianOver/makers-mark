using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Expedition;
using GameSim.Kernel;
using GameSim.Venues;
using GameSim.Venues.SunkenCrypt;
using Xunit;

namespace GameSim.Tests.Venues.SunkenCrypt;

/// <summary>
/// Behavior tests for the Sunken Crypt add-on venue (C2). Every test is REGISTRATION-INDEPENDENT: it
/// drives <see cref="SunkenCryptVenue.Definition"/> and the shared pure pipeline
/// (<see cref="ExpeditionResolver"/> / <see cref="AttributionEngine"/>) DIRECTLY — never
/// <see cref="VenueRegistry.All"/> — so the suite is green whether or not the orchestrator has applied
/// the registration line (the pack is inert until registered, and these tests prove the DATA and its
/// flow through the shared code path in either state). This mirrors the core's own extensibility proof
/// (<c>VenueConformanceTests.AddOnVenue_ResolvesEndToEnd_WithoutJoiningLiveRotation</c>).
///
/// Once the orchestrator applies the registration line, the parameterized <c>VenueConformanceTests</c>
/// additionally cover the Sunken Crypt's structural contract automatically — this suite is the
/// pack-owned behavior layer on top.
/// </summary>
public class SunkenCryptVenueTests
{
    private static readonly VenueDefinition Crypt = SunkenCryptVenue.Definition;

    private static readonly ImmutableArray<string> CryptKinds =
        ImmutableArray.Create("Crypt Crab", "Bog-Wight", "Choir of Teeth", "Reliquary Mimic", "The Undertow");

    private static readonly ImmutableArray<string> CryptOres =
        ImmutableArray.Create("verdigris", "saltglass", "bonechalk", "drowned-silver", "abyss-pearl");

    // ---- Definition data (the pack's identity) -----------------------------------------

    [Fact]
    public void Definition_HasExpectedIdentity_AndFiveFloors()
    {
        Assert.Equal("sunken-crypt", Crypt.Id);
        Assert.Equal(SunkenCryptVenue.Id, Crypt.Id);
        Assert.Equal("The Sunken Crypt", Crypt.DisplayName);
        Assert.Equal(5, Crypt.FloorCount);
        Assert.Equal(Crypt.FloorCount, Crypt.Floors.Length);
    }

    [Fact]
    public void Floors_PinTheNamedGatesKindsAndOres()
    {
        var gate = new[] { 0, 15, 35, 60, 100 };
        for (var floor = 1; floor <= 5; floor++)
        {
            Assert.Equal(floor, Crypt.Floors[floor - 1].Floor); // ascending, 1-based, index 0 == floor 1
            Assert.Equal(gate[floor - 1], Crypt.Gate(floor));
            Assert.Equal(CryptKinds[floor - 1], Crypt.MonsterKind(floor));
            Assert.Equal(CryptOres[floor - 1], Crypt.OreKey(floor));
        }
    }

    [Fact]
    public void Floors_AreStructurallyWellFormed()
    {
        for (var floor = 1; floor <= Crypt.FloorCount; floor++)
        {
            Assert.False(string.IsNullOrWhiteSpace(Crypt.MonsterKind(floor)));
            Assert.False(string.IsNullOrWhiteSpace(Crypt.OreKey(floor)));

            Assert.InRange(Crypt.Gate(floor), 0, int.MaxValue);
            Assert.True(Crypt.MonsterHp(floor) > 0);
            Assert.True(Crypt.MonsterAttack(floor) > 0);
            Assert.True(Crypt.MonsterDefense(floor) > 0);
            Assert.True(Crypt.GoldPerKill(floor) > 0);

            // OreFloor inverts OreKey and so guards ore-key uniqueness within the venue.
            Assert.Equal(floor, Crypt.OreFloor(Crypt.OreKey(floor)));
        }
    }

    [Fact]
    public void Gates_AreNonDecreasing_WithDepth()
    {
        for (var floor = 2; floor <= Crypt.FloorCount; floor++)
        {
            Assert.True(
                Crypt.Gate(floor) >= Crypt.Gate(floor - 1),
                $"gate on floor {floor} ({Crypt.Gate(floor)}) drops below floor {floor - 1} ({Crypt.Gate(floor - 1)})");
        }
    }

    [Fact]
    public void OreKeys_AreUnique_AndDisjointFromTheMine()
    {
        Assert.Equal(CryptOres.Length, CryptOres.Distinct(StringComparer.Ordinal).Count());

        // Bring-your-own materials: no crypt ore collides with a Mine ore key.
        foreach (var ore in CryptOres)
        {
            Assert.Equal(0, VenueRegistry.Mine.OreFloor(ore));
        }
    }

    // ---- Fixture-collision guard: sunken-crypt is NOT the test-only sunken-vault fixture ----

    [Fact]
    public void DoesNotCollide_WithTheUnregisteredSunkenVaultFixture()
    {
        // VenueConformanceTests holds an extensibility fixture id "sunken-vault" with ores
        // brine-salt / pearl / abyssal-glass. C2's id and every ore key must stay distinct from it.
        Assert.NotEqual("sunken-vault", Crypt.Id);
        foreach (var vaultOre in new[] { "brine-salt", "pearl", "abyssal-glass" })
        {
            Assert.DoesNotContain(vaultOre, CryptOres);
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
            var hero = Diver(1, weapon.Id);
            var result = ExpeditionResolver.Resolve(
                ImmutableList.Create(hero), items, Crypt, targetFloor: 3, new Pcg32(RngState.FromSeed(seed)));

            // Tagged with THIS venue — not the Mine default.
            Assert.Equal("sunken-crypt", result.VenueId);

            // Clamped to the venue's 5 floors (targetFloor 3 here) — never a Mine kind leaking in.
            Assert.All(result.Floors, f => Assert.InRange(f.Floor, 1, 5));
            foreach (var floor in result.Floors)
            {
                foreach (var combat in floor.Combats)
                {
                    Assert.Contains(combat.MonsterKind, CryptKinds);
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
                Assert.Contains(loot.MaterialKey, CryptOres);
            }

            // Attribution recomputed over the SAME venue is byte-identical (forward + counterfactual
            // passes share one venue, KTD6), and a killing-blow beat names a crypt monster.
            var recomputed = AttributionEngine.ComputeBeats(result.Floors, ImmutableList.Create(hero), items, Crypt);
            Assert.Equal(result.Beats, recomputed);

            foreach (var beat in result.Beats.Where(b => b.Beat == BeatType.KillingBlow))
            {
                sawKillingBlowBeat = true;
                Assert.Contains(CryptKinds, k => beat.Detail.Contains(k, StringComparison.Ordinal));
            }
        }

        Assert.True(clearedAnyFloor, "crypt never cleared a floor — scenario needs retuning");
        Assert.True(sawKillingBlowBeat, "no killing-blow beat on crypt data — attribution not venue-driven");
    }

    // ---- Determinism duties: constant data ---------------------------------------------

    [Fact]
    public void Definition_IsConstant_ReferenceStableAcrossReads()
    {
        Assert.Same(SunkenCryptVenue.Definition, SunkenCryptVenue.Definition);
    }

    private static Item PlayerWeapon(int id, int attack) => new(
        new ItemId(id), "tide-blade", "Tide Blade", ItemSlot.Weapon, QualityGrade.Fine,
        new ItemStats(attack, 0, 4), new MakersMark("You", CraftedOnDay: 1),
        ImmutableList<ItemHistoryEntry>.Empty);

    private static Hero Diver(int id, ItemId weapon) => new(
        new HeroId(id), $"Diver{id}", "vanguard", Level: 8, MaxHp: 300, Gold: 50,
        new GearSet(weapon, null, null), ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 0, DiedOnDay: null);
}
