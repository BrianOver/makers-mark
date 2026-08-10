using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Expedition;
using GameSim.Kernel;
using GameSim.Venues;
using GameSim.Venues.Gloomwood;
using Xunit;

namespace GameSim.Tests.Venues.Gloomwood;

/// <summary>
/// Behavior tests for the Gloomwood venue (the 2nd raid venue, C1 content / Phase C U-C4 go-live).
/// Most tests drive the venue's <see cref="GloomwoodVenue.Definition"/> through the real
/// <see cref="ExpeditionResolver"/> + <see cref="AttributionEngine"/> directly — never through
/// <see cref="VenueRegistry.All"/> — so they stay green regardless of registration state. The
/// registration/live-state test below confirms the U-C4 go-live: the Gloomwood is now registered AND
/// in <see cref="VenueRegistry.LiveRotation"/> alongside the Mine. This mirrors the core's own
/// extensibility proof (<c>VenueConformanceTests.AddOnVenue_ResolvesEndToEnd_WithoutJoiningLiveRotation</c>),
/// which still uses a THIRD, never-registered reference venue to prove the add-on shape independently
/// of whether any given venue happens to be live.
///
/// The parameterized <c>VenueConformanceTests</c> additionally covers the Gloomwood's structural
/// contract automatically (any registered venue) — this suite is the pack-owned behavior layer on top.
/// </summary>
public class GloomwoodVenueTests
{
    private static readonly VenueDefinition Gloomwood = GloomwoodVenue.Definition;

    private static readonly ImmutableArray<string> GloomwoodKinds =
        ImmutableArray.Create("Bramble Boar", "Lantern Moth", "The Wicker Shepherd", "Old Mossjaw");

    private static readonly ImmutableArray<string> GloomwoodOres =
        ImmutableArray.Create("greenheart", "amberpitch", "moonresin", "heartwood");

    // ---- Definition data (the pack's identity, C1) -----------------------------------

    [Fact]
    public void Definition_HasExpectedIdentity_AndFourFloors()
    {
        Assert.Equal("gloomwood", Gloomwood.Id);
        Assert.Equal(GloomwoodVenue.Id, Gloomwood.Id);
        Assert.Equal("The Gloomwood", Gloomwood.DisplayName);
        Assert.Equal(4, Gloomwood.FloorCount);
        Assert.Equal(Gloomwood.FloorCount, Gloomwood.Floors.Length);
    }

    [Fact]
    public void Gates_Are_0_20_45_73_AndNonDecreasing()
    {
        // Floor 4 (the boss) re-gated 75 -> 73 (forward-ladder plan 2026-08-10-003 L3): Gloomwood
        // is now rung 1, entered only by graduating the Mine/Sunken Crypt (rank 0), and a graduating
        // party's global DeepestFloorReached (5) means their FIRST Gloomwood trip targets floor 4
        // (the boss) directly — see GloomwoodVenue.Build's own comment for the full measurement
        // (73 sits inside the measured 70-74 graduation-power band on all 11 characterized seeds,
        // clearing the boss 1-4 days after graduation with zero deaths, where 75-90 stranded 2-6
        // of those seeds for 100 days).
        var expected = new[] { 0, 20, 45, 73 };
        for (var floor = 1; floor <= 4; floor++)
        {
            Assert.Equal(expected[floor - 1], Gloomwood.Gate(floor));
        }

        for (var floor = 2; floor <= 4; floor++)
        {
            Assert.True(Gloomwood.Gate(floor) >= Gloomwood.Gate(floor - 1));
        }
    }

    [Fact]
    public void MonsterKinds_AreTheNamedGloomwoodCreatures()
    {
        for (var floor = 1; floor <= 4; floor++)
        {
            Assert.Equal(GloomwoodKinds[floor - 1], Gloomwood.MonsterKind(floor));
        }
    }

    [Fact]
    public void FloorStats_AreAllPositive()
    {
        for (var floor = 1; floor <= 4; floor++)
        {
            Assert.True(Gloomwood.MonsterHp(floor) > 0, $"floor {floor}: MonsterHp");
            Assert.True(Gloomwood.MonsterAttack(floor) > 0, $"floor {floor}: MonsterAttack");
            Assert.True(Gloomwood.MonsterDefense(floor) > 0, $"floor {floor}: MonsterDefense");
            Assert.True(Gloomwood.GoldPerKill(floor) > 0, $"floor {floor}: GoldPerKill");
        }
    }

    [Fact]
    public void OreKeys_AreUniqueWithinVenue_AndInvertViaOreFloor()
    {
        for (var floor = 1; floor <= 4; floor++)
        {
            Assert.Equal(GloomwoodOres[floor - 1], Gloomwood.OreKey(floor));
            // OreFloor inverts OreKey — and so guards ore-key uniqueness within the venue.
            Assert.Equal(floor, Gloomwood.OreFloor(Gloomwood.OreKey(floor)));
        }

        Assert.Equal(0, Gloomwood.OreFloor("no-such-ore"));
    }

    [Fact]
    public void OreKeys_AreDisjointFromTheMine()
    {
        // The Gloomwood mints its OWN nature-ores — it never mints a Mine ore key.
        var mineOres = VenueRegistry.Mine.Floors.Select(f => f.OreKey).ToImmutableHashSet(StringComparer.Ordinal);
        foreach (var ore in GloomwoodOres)
        {
            Assert.DoesNotContain(ore, mineOres);
        }
    }

    // ---- Registration/live state ------------------------------------------------------

    [Fact]
    public void Gloomwood_IsRegistered_AndLive()
    {
        // Phase C U-C4: the Gloomwood is now the second live venue alongside the Mine —
        // VenueRouter distributes bounty-free parties across both (see VenueRouterTests /
        // VenueConformanceTests.LiveRotation_IsExactlyTheFourBuiltVenues).
        Assert.True(VenueRegistry.IsRegistered("gloomwood"));
        Assert.Contains("gloomwood", VenueRegistry.LiveRotation);
    }

    // ---- End-to-end through the REAL resolver + attribution (the add-on shape) --------

    private static Item Blade(int id, int attack) => new(
        new ItemId(id), "moss-cleaver", "Moss Cleaver", ItemSlot.Weapon, QualityGrade.Fine,
        new ItemStats(attack, 0, 4), new MakersMark("You", CraftedOnDay: 1),
        ImmutableList<ItemHistoryEntry>.Empty);

    private static Hero Warden(int id, ItemId weapon) => new(
        new HeroId(id), $"Ranger{id}", "vanguard", Level: 8, MaxHp: 300, Gold: 50,
        new GearSet(weapon, null, null), ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 0, DiedOnDay: null);

    [Fact]
    public void ResolvesEndToEnd_ReadingTheGloomwoodData_WithoutJoiningLiveRotation()
    {
        var weapon = Blade(1, attack: 70);
        var items = ImmutableSortedDictionary<int, Item>.Empty.Add(weapon.Id.Value, weapon);

        var clearedAnyFloor = false;
        var sawKillingBlowBeat = false;

        for (ulong seed = 0; seed < 200; seed++)
        {
            var hero = Warden(1, weapon.Id);
            var result = ExpeditionResolver.Resolve(
                ImmutableList.Create(hero), items, Gloomwood, targetFloor: 4, new Pcg32(RngState.FromSeed(seed)));

            // Tagged with the venue it was raided in — not the Mine default.
            Assert.Equal("gloomwood", result.VenueId);

            // Clamped to the venue's 4 floors — never the Mine's 5.
            Assert.All(result.Floors, f => Assert.InRange(f.Floor, 1, 4));

            foreach (var floor in result.Floors)
            {
                foreach (var combat in floor.Combats)
                {
                    // Combat read the venue's monster kinds — never a Mine kind.
                    Assert.Contains(combat.MonsterKind, GloomwoodKinds);
                    Assert.DoesNotContain(combat.MonsterKind, VenueRegistry.Mine.Floors.Select(fl => fl.MonsterKind));
                }

                if (floor.Cleared)
                {
                    clearedAnyFloor = true;
                }
            }

            // Loot rarity came from the venue's ore table.
            foreach (var loot in result.Loot)
            {
                Assert.Contains(loot.MaterialKey, GloomwoodOres);
            }

            // Attribution (run inside Resolve) read the venue: recomputing over the SAME venue
            // reproduces identical beats, proving forward + counterfactual passes share one venue (KTD6).
            var recomputed = AttributionEngine.ComputeBeats(result.Floors, ImmutableList.Create(hero), items, Gloomwood);
            Assert.Equal(result.Beats, recomputed);

            foreach (var beat in result.Beats.Where(b => b.Beat == BeatType.KillingBlow))
            {
                sawKillingBlowBeat = true;
                Assert.Contains(GloomwoodKinds, k => beat.Detail.Contains(k, StringComparison.Ordinal));
            }
        }

        Assert.True(clearedAnyFloor, "Gloomwood never cleared a floor — scenario needs retuning");
        Assert.True(sawKillingBlowBeat, "no killing-blow beat on Gloomwood data — attribution not venue-driven");
    }
}
