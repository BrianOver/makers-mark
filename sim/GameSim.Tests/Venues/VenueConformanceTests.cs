using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Expedition;
using GameSim.Kernel;
using GameSim.Venues;
using Xunit;

namespace GameSim.Tests.Venues;

/// <summary>
/// The add-on conformance harness (P4, mirrors <c>ClassConformanceTests</c>): every venue in
/// <see cref="VenueRegistry.All"/> is validated structurally, so an add-on Claude's definition of
/// done is mechanical — register the venue and make THIS suite green. New venues get covered
/// automatically; no edits needed here.
///
/// Two anchors sit alongside the parameterized checks:
/// <list type="bullet">
/// <item><see cref="Mine_IsByteIdenticalTo_TheOldMonsterTable"/> pins the Mine's data to the exact
/// literal values the old static <c>MonsterTable</c> held — the byte-identical guarantee that keeps
/// every seed's world (and the Balance bands) from moving.</item>
/// <item><see cref="AddOnVenue_ResolvesEndToEnd_WithoutJoiningLiveRotation"/> is the extensibility
/// proof (mirrors P3's test-only 4th class): a test-only reference venue with a different floor
/// count, gate curve, monster kinds, and ore keys flows through the real
/// <see cref="ExpeditionResolver"/> + <see cref="AttributionEngine"/> end-to-end — never registered,
/// never added to <see cref="VenueRegistry.LiveRotation"/>.</item>
/// </list>
/// </summary>
public class VenueConformanceTests
{
    public static TheoryData<string> AllVenueIds()
    {
        var data = new TheoryData<string>();
        foreach (var id in VenueRegistry.All.Keys)
        {
            data.Add(id);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllVenueIds))]
    public void Identity_IdMatchesKey_AndDisplayNamePresent(string id)
    {
        var venue = VenueRegistry.All[id];
        Assert.Equal(id, venue.Id);
        Assert.False(string.IsNullOrWhiteSpace(venue.DisplayName));
    }

    [Theory]
    [MemberData(nameof(AllVenueIds))]
    public void FloorData_IsComplete_AndWellFormed(string id)
    {
        var venue = VenueRegistry.All[id];
        Assert.True(venue.FloorCount >= 1, $"{id}: FloorCount must be >= 1");
        Assert.Equal(venue.FloorCount, venue.Floors.Length);

        for (var floor = 1; floor <= venue.FloorCount; floor++)
        {
            var data = venue.Floors[floor - 1];
            Assert.Equal(floor, data.Floor); // ascending, 1-based, index 0 == floor 1
            Assert.False(string.IsNullOrWhiteSpace(venue.MonsterKind(floor)), $"{id} floor {floor}: blank MonsterKind");
            Assert.False(string.IsNullOrWhiteSpace(venue.OreKey(floor)), $"{id} floor {floor}: blank OreKey");

            // Structural gate is a non-negative power threshold; monster stats + reward are positive.
            Assert.InRange(venue.Gate(floor), 0, int.MaxValue);
            Assert.True(venue.MonsterHp(floor) > 0, $"{id} floor {floor}: MonsterHp must be positive");
            Assert.True(venue.MonsterAttack(floor) > 0, $"{id} floor {floor}: MonsterAttack must be positive");
            Assert.True(venue.MonsterDefense(floor) > 0, $"{id} floor {floor}: MonsterDefense must be positive");
            Assert.True(venue.GoldPerKill(floor) > 0, $"{id} floor {floor}: GoldPerKill must be positive");

            // OreFloor inverts OreKey (and so guards ore-key uniqueness within the venue).
            Assert.Equal(floor, venue.OreFloor(venue.OreKey(floor)));
        }
    }

    [Theory]
    [MemberData(nameof(AllVenueIds))]
    public void Gates_AreNonDecreasing_WithDepth(string id)
    {
        var venue = VenueRegistry.All[id];
        for (var floor = 2; floor <= venue.FloorCount; floor++)
        {
            Assert.True(
                venue.Gate(floor) >= venue.Gate(floor - 1),
                $"{id}: gate on floor {floor} ({venue.Gate(floor)}) drops below floor {floor - 1} ({venue.Gate(floor - 1)})");
        }
    }

    [Theory]
    [MemberData(nameof(AllVenueIds))]
    public void EntryPower_IsNonNegative(string id)
    {
        // The router's progression band: negative would make a venue "reached" by every party AND
        // sort below the EntryPower-0 early venues — a contradiction the comparator can't express.
        Assert.InRange(VenueRegistry.All[id].EntryPower, 0, int.MaxValue);
    }

    [Fact]
    public void EntryPowerBands_AreTheTunedRecord()
    {
        // The routing tuning record (2026-08-01, measured against the banded-world power curve:
        // router-side party power ramps 30..54 (p25..p75) in days 1-10 and saturates at ~70-76).
        // Mine + Sunken Crypt are the EntryPower-0 early peers (queue-split). Gloomwood sits at
        // 72; Emberfall sits at 79 — NOT a tie (see below). Change these consciously — they move
        // every seed's venue distribution (and therefore the balance bands), and MEASURE with the
        // batch farm, never reason it out: Gloomwood placements 35 and 55 both looked plausible
        // and measured 65%+ and 86% venue share; the full sweep 55/65/68/70/71/72 measured
        // 86/75/74/70/72/68% — the threshold lever saturates near ~68-70% in a three-venue world
        // because the economy feeds back (Mine traffic sells the smith copper, gear improves, the
        // fleet levels back over any bar below the ~81 power ceiling). Shipping row at these
        // values: gloomwood 68% / mine 25% / crypt 7%, all three on 20/20 seeds.
        //
        // Emberfall go-live (2026-08-02, P3/task #45): first shipped tied at 72 (go-live sanity:
        // ember 42% / mine 26% / gloomwood 24% / crypt 6%), but a full 20-seed x 100-day sweep on
        // the real branch measured the tie handing Emberfall 41.4% of ALL routed parties and
        // collapsing Gloomwood from a same-codebase pre-flip 64.3% down to 19.5% — the ordinal
        // tie-break ("emberfall" < "gloomwood") wins every close call, so a "peer" band in practice
        // starved Gloomwood. Re-tuned to 79 (search at 72/76/78/79/80 measured gloomwood/emberfall
        // shares of 19.5/41.4, 29.1/35.3, 40.7/23.4, 50.5/14.6, 57.2/7.3 — full search recorded in
        // `EmberfallFoundryVenue.Build`'s doc): Gloomwood back to a 50.5% majority (a substantial
        // mid-game destination again), Emberfall a real-but-secondary 14.6% endgame share (clearly
        // above dormant Sunken Crypt's flat 8%, clearly below Gloomwood) — later-game content, not
        // co-primary. Reversible in one line; re-measure before moving it.
        Assert.Equal(0, VenueRegistry.Mine.EntryPower);
        Assert.Equal(0, VenueRegistry.All["sunken-crypt"].EntryPower);
        Assert.Equal(72, VenueRegistry.All["gloomwood"].EntryPower);
        Assert.Equal(79, VenueRegistry.All["emberfall"].EntryPower);
    }

    [Fact]
    public void OreFloor_ReturnsZero_ForUnknownOre()
    {
        Assert.Equal(0, VenueRegistry.Mine.OreFloor("no-such-ore"));
    }

    [Fact]
    public void LiveRotation_IsNonEmpty_AndSubsetOfAll()
    {
        Assert.NotEmpty(VenueRegistry.LiveRotation);
        foreach (var id in VenueRegistry.LiveRotation)
        {
            Assert.True(VenueRegistry.IsRegistered(id), $"live-rotation id '{id}' is not a registered venue");
        }
    }

    [Fact]
    public void LiveRotation_IsExactlyMineGloomwoodCryptAndEmberfall()
    {
        // The live-venue contract (P4): a registered venue is NOT automatically live. T1 content
        // flip (relands PR #242, landed #328): the Sunken Crypt joined the Mine and Gloomwood, in
        // this exact order — callers seed VenueRouter's queue dictionary from this array, and every
        // golden replay that reads LiveRotation's order depends on it. P3/task #45 unlock: Emberfall
        // is APPENDED here (built, banded EntryPower 79 as of the go-live re-tune — see
        // `EntryPowerBands_AreTheTunedRecord` — its own determinism-gated re-baseline) — it was
        // held dormant by #328 solely for missing art (see VenueRegistry.LiveRotation's doc); this
        // PR ships as a draft until `feat/emberfall-art-set` lands the backdrop/portraits.
        Assert.Equal(
            new[] { VenueRegistry.MineId, "gloomwood", "sunken-crypt", "emberfall" },
            VenueRegistry.LiveRotation);
    }

    [Fact]
    public void Require_Throws_ForUnregisteredVenue()
    {
        Assert.True(VenueRegistry.TryGet(VenueRegistry.MineId, out _));
        Assert.False(VenueRegistry.TryGet("sunken-vault", out var missing));
        Assert.Null(missing);
        Assert.Throws<KeyNotFoundException>(() => VenueRegistry.Require("sunken-vault"));
    }

    // ---- Byte-identical pin: the Mine as data reproduces the old MonsterTable exactly ----------

    [Fact]
    public void Mine_IsByteIdenticalTo_TheOldMonsterTable()
    {
        var mine = VenueRegistry.Mine;

        // Golden literals — the exact values the pre-P4 static MonsterTable held, reproduced here
        // independently so this is a real cross-check, not a tautology.
        Assert.Equal("mine", mine.Id);
        Assert.Equal(5, mine.FloorCount);

        var gate = new[] { 0, 15, 35, 60, 100 };
        var kind = new[] { "Cave Rat", "Tunnel Spider", "Deep Ghoul", "Ore Golem", "The Forgeworm" };
        var ore = new[] { "copper", "iron", "steel", "mithril", "adamant" };

        for (var floor = 1; floor <= 5; floor++)
        {
            Assert.Equal(gate[floor - 1], mine.Gate(floor));
            Assert.Equal(kind[floor - 1], mine.MonsterKind(floor));
            Assert.Equal(ore[floor - 1], mine.OreKey(floor));
            Assert.Equal(12 + 10 * floor, mine.MonsterHp(floor));
            Assert.Equal(5 + 6 * floor, mine.MonsterAttack(floor));
            Assert.Equal(2 + 2 * floor, mine.MonsterDefense(floor));
            Assert.Equal(5 + 3 * floor, mine.GoldPerKill(floor));

            // And the compat shim delegates to the Mine correctly (same source of truth).
            Assert.Equal(mine.Gate(floor), MonsterTable.Gate(floor));
            Assert.Equal(mine.MonsterKind(floor), MonsterTable.MonsterKind(floor));
            Assert.Equal(mine.MonsterHp(floor), MonsterTable.MonsterHp(floor));
            Assert.Equal(mine.MonsterAttack(floor), MonsterTable.MonsterAttack(floor));
            Assert.Equal(mine.MonsterDefense(floor), MonsterTable.MonsterDefense(floor));
            Assert.Equal(mine.GoldPerKill(floor), MonsterTable.GoldPerKill(floor));
            Assert.Equal(mine.OreKey(floor), MonsterTable.OreKey(floor));
        }

        Assert.Equal(5, MonsterTable.FloorCount);
    }

    // ---- Extensibility proof (no live second venue in this core) ------------------------------

    private static readonly ImmutableArray<string> SunkenVaultKinds =
        ImmutableArray.Create("Brine Lurker", "Drowned Sentinel", "The Maw");

    private static readonly ImmutableArray<string> SunkenVaultOres =
        ImmutableArray.Create("brine-salt", "pearl", "abyssal-glass");

    /// <summary>
    /// A test-only reference venue with a shape NO built-in has: THREE floors (not five), a
    /// different gate curve, different monster kinds, and different ore keys. If the resolver and
    /// attribution engine read the definition — not a hardcoded Mine — a full expedition resolves
    /// against this data alone.
    /// </summary>
    private static VenueDefinition SunkenVault()
    {
        var floors = ImmutableArray.CreateBuilder<VenueFloor>(3);
        var gate = new[] { 0, 8, 20 };
        for (var floor = 1; floor <= 3; floor++)
        {
            floors.Add(new VenueFloor(
                Floor: floor,
                Gate: gate[floor - 1],
                MonsterKind: SunkenVaultKinds[floor - 1],
                MonsterHp: 10 + 8 * floor,
                MonsterAttack: 4 + 5 * floor,
                MonsterDefense: 1 + 2 * floor,
                GoldPerKill: 4 + 4 * floor,
                OreKey: SunkenVaultOres[floor - 1]));
        }

        return new VenueDefinition("sunken-vault", "The Sunken Vault", floors.ToImmutable());
    }

    private static Item PlayerWeapon(int id, int attack) => new(
        new ItemId(id), "tide-blade", "Tide Blade", ItemSlot.Weapon, QualityGrade.Fine,
        new ItemStats(attack, 0, 4), new MakersMark("You", CraftedOnDay: 1),
        ImmutableList<ItemHistoryEntry>.Empty);

    private static Hero Diver(int id, ItemId weapon) => new(
        new HeroId(id), $"Diver{id}", "vanguard", Level: 8, MaxHp: 300, Gold: 50,
        new GearSet(weapon, null, null), ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 0, DiedOnDay: null);

    [Fact]
    public void AddOnVenue_ResolvesEndToEnd_WithoutJoiningLiveRotation()
    {
        var vault = SunkenVault();

        // Defined and used, but NEVER registered or live — the add-on shape.
        Assert.False(VenueRegistry.IsRegistered(vault.Id));
        Assert.DoesNotContain(vault.Id, VenueRegistry.LiveRotation);

        // Sanity: the reference venue itself satisfies the structural contract used above.
        Assert.Equal(3, vault.FloorCount);
        for (var floor = 1; floor <= 3; floor++)
        {
            Assert.Equal(floor, vault.OreFloor(vault.OreKey(floor)));
        }

        var weapon = PlayerWeapon(1, attack: 40);
        var items = ImmutableSortedDictionary<int, Item>.Empty.Add(weapon.Id.Value, weapon);

        var sawKillingBlowBeat = false;
        var clearedAnyFloor = false;
        for (ulong seed = 0; seed < 200; seed++)
        {
            var hero = Diver(1, weapon.Id);
            var result = ExpeditionResolver.Resolve(
                ImmutableList.Create(hero), items, vault, targetFloor: 3, new Pcg32(RngState.FromSeed(seed)));

            // The result is tagged with the venue it was raided in — not the Mine default.
            Assert.Equal("sunken-vault", result.VenueId);

            // Clamped to the venue's 3 floors — never the Mine's 5.
            Assert.All(result.Floors, f => Assert.InRange(f.Floor, 1, 3));

            foreach (var floor in result.Floors)
            {
                foreach (var combat in floor.Combats)
                {
                    // Combat read the venue's monster kinds — never a Mine kind.
                    Assert.Contains(combat.MonsterKind, SunkenVaultKinds);
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
                Assert.Contains(loot.MaterialKey, SunkenVaultOres);
            }

            // Attribution (run inside Resolve) read the venue: a player-weapon killing blow names
            // the venue's monster. Recomputing over the SAME venue reproduces identical beats,
            // proving forward + counterfactual passes share one venue (KTD6).
            var recomputed = AttributionEngine.ComputeBeats(result.Floors, ImmutableList.Create(hero), items, vault);
            Assert.Equal(result.Beats, recomputed);

            foreach (var beat in result.Beats.Where(b => b.Beat == BeatType.KillingBlow))
            {
                sawKillingBlowBeat = true;
                Assert.Contains(SunkenVaultKinds, k => beat.Detail.Contains(k, StringComparison.Ordinal));
            }
        }

        Assert.True(clearedAnyFloor, "reference venue never cleared a floor — scenario needs retuning");
        Assert.True(sawKillingBlowBeat, "no killing-blow beat on non-Mine data — attribution not venue-driven");
    }
}
