using System.Collections.Immutable;
using System.Text.RegularExpressions;
using GameSim.Contracts;
using GameSim.Expedition;
using GameSim.Kernel;
using GameSim.Venues;

namespace GameSim.Tests.Expedition;

/// <summary>
/// P2-PROOF-14: <see cref="AttributionEngine"/> computes the entire counterfactual for every beat
/// (the recorded roll, the hero's defense with and without the player's piece, the damage that
/// would have landed) and used to throw all of it away, writing only a bare claim into
/// <see cref="AttributionBeat.Detail"/> — the ledger surface the player actually reads asserted
/// "your item mattered" with no arithmetic behind it. This file pins that every <c>Detail</c> now
/// carries the SAME numbers the engine itself computed, in the voice <see cref="TellingQuery"/>'s
/// "Ask how it happened" modal already settled on for the identical facts.
///
/// <para><b>Two disciplines this file exists to hold the line on</b> (both named in CLAUDE.md's own
/// unit brief): no ratio, percentage, share or score EVER stands in for a counterfactual (a beat
/// gives the raw "with it" / "without it" numbers, never a rating of how much it mattered) — see
/// <see cref="NoDetail_EverContainsARatioPercentageShareOrScore_AcrossASeedSweep"/>. And every
/// <see cref="BeatType"/> the engine can emit is proven here BY REFLECTION over the enum, not by a
/// hand-list that quietly stops growing — see <see cref="EveryBeatType_IsEitherArithmeticCovered_OrACitedException"/>.</para>
/// </summary>
public class AttributionArithmeticTests
{
    // ── Coverage census ──────────────────────────────────────────────────────────────────────────
    //
    // Enum.GetValues<BeatType>() is read fresh every run — a SEVENTH beat type added to the
    // contract fails ExceptionCount_IsPinned or EveryBeatType_IsEitherArithmeticCovered_OrACitedException
    // the day it appears, rather than silently shipping a bare claim the way KillingBlow/LethalSave/
    // BreakpointClear/Provisioned/PotionLifesave all used to.

    /// <summary>BeatType -> the test method proving its Detail carries the engine's own arithmetic.</summary>
    private static readonly Dictionary<BeatType, string> Covered = new()
    {
        [BeatType.KillingBlow] = nameof(KillingBlow_DetailCarries_TheRecordedRoll_AndDamageWithoutTheWeapon),
        [BeatType.LethalSave] = nameof(LethalSave_DetailCarries_TheRawBlow_TheDefenseDrunk_AndWhereTheHeroStood),
        [BeatType.BreakpointClear] = nameof(BreakpointClear_DetailCarries_ThePartyPowerAndTheGate),
        [BeatType.Provisioned] = nameof(Provisioned_DetailCarries_TheNaiveMarginThatProvesItDidNotMatter),
        [BeatType.PotionLifesave] = nameof(PotionLifesave_DetailCarries_TheMarginThatWouldHaveKilled_AndWhereTheFightActuallyEnded),
    };

    /// <summary>BeatType -> reason citing why it is exempt from arithmetic coverage here.</summary>
    private static readonly Dictionary<BeatType, string> Exceptions = new()
    {
        [BeatType.ToolAssist] =
            "P2-PROOF-14: reserved for the Engineering add-on. BeatType's own doc comment and " +
            "TellingQuery.Build's ArgumentOutOfRangeException both agree AttributionEngine has no " +
            "emitter for it yet -- there is no Detail string to give arithmetic to until an emitter " +
            "exists. Remove this exception the day ToolAssist gets one.",
    };

    private const int ExpectedExceptionCount = 1;

    [Fact]
    public void ExceptionCount_IsPinned_SoANewGrantIsAVisibleDiff()
        => Assert.True(Exceptions.Count == ExpectedExceptionCount,
            $"Pinned at {ExpectedExceptionCount}; the table now holds {Exceptions.Count}.");

    [Fact]
    public void EveryPinnedException_CitesTheRulingThatGrantedIt()
    {
        var citation = new Regex(@"§11\.7|\bP\d+\b|P2-[A-Z]+-\d+");
        var uncited = Exceptions
            .Where(e => !citation.IsMatch(e.Value))
            .Select(e => e.Key.ToString())
            .ToList();

        Assert.True(uncited.Count == 0,
            "An exception with no ruling behind it is drift wearing a reason:\n  " + string.Join("\n  ", uncited));
    }

    [Fact]
    public void EveryBeatType_IsEitherArithmeticCovered_OrACitedException()
    {
        var uncovered = Enum.GetValues<BeatType>()
            .Where(bt => !Covered.ContainsKey(bt) && !Exceptions.ContainsKey(bt))
            .ToList();

        Assert.True(uncovered.Count == 0,
            "A BeatType has no arithmetic-coverage test and no cited exception -- a beat can ship a "
            + "bare claim again (P2-PROOF-14): " + string.Join(", ", uncovered));
    }

    // ── Fixtures (mirrors AttributionTests.cs / ConsumableAttributionTests.cs) ─────────────────────

    private static Item PlayerWeapon(int id, int attack) => new(
        new ItemId(id), "shortsword", "Fine Shortsword", ItemSlot.Weapon, QualityGrade.Fine,
        new ItemStats(attack, 0, 4), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);

    private static Item PlayerArmor(int id, int defense) => new(
        new ItemId(id), "chain-vest", "Fine Chain Vest", ItemSlot.Armor, QualityGrade.Fine,
        new ItemStats(0, defense, 4), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);

    private static Item Salve(int id) => new(
        new ItemId(id), "field-salve", "Field Salve", ItemSlot.Consumable, QualityGrade.Common,
        new ItemStats(0, 0, 0), new MakersMark("You", 1),
        ImmutableList<ItemHistoryEntry>.Empty, new ConsumableEffect(ConsumableKind.Heal, 6));

    private static Hero HeroWith(int id, GearSet gear, int hp = 30, int level = 3, string name = "Torvald") => new(
        new HeroId(id), name, "vanguard", level, hp, Gold: 50,
        gear, ImmutableList<ItemMemory>.Empty, Alive: true, DeepestFloorReached: 4, DiedOnDay: null);

    private static VenueDefinition SingleFloorVenue(
        int gate = 0, int monsterHp = 999, int monsterAttack = 10, int monsterDefense = 0,
        string monsterKind = "Cave Rat") =>
        new("test-venue", "Test Venue", ImmutableArray.Create(
            new VenueFloor(1, gate, monsterKind, monsterHp, monsterAttack, monsterDefense, GoldPerKill: 5, OreKey: "iron")));

    /// <summary>One recorded round; Uses ride on the round the quaff preceded (ConsumableAttributionTests' own shape).</summary>
    private static CombatEvent Round(int taken, bool killed = false, params ConsumableUse[] uses) => new(
        1, new HeroId(1), "Cave Rat", ImmutableList.Create(2, 4), DamageDealt: 5, taken, killed, KillingItem: null)
    {
        Uses = uses.ToImmutableList(),
    };

    private static ImmutableList<AttributionBeat> ConsumableBeats(Item salve, params CombatEvent[] fight) =>
        AttributionEngine.ComputeBeats(
            ImmutableList.Create(new FloorOutcome(1, Cleared: false, fight.ToImmutableList())),
            ImmutableList.Create(HeroWith(1, GearSet.Empty)),
            ImmutableSortedDictionary<int, Item>.Empty.Add(salve.Id.Value, salve),
            VenueRegistry.Mine);

    // ── KillingBlow ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void KillingBlow_DetailCarries_TheRecordedRoll_AndDamageWithoutTheWeapon()
    {
        var weapon = PlayerWeapon(1, attack: 40);
        var hero = HeroWith(1, new GearSet(weapon.Id, null, null));
        var items = ImmutableSortedDictionary<int, Item>.Empty.Add(1, weapon);

        var killRound = new CombatEvent(
            1, hero.Id, "Cave Rat", ImmutableList.Create(4), DamageDealt: 50, DamageTaken: 0,
            MonsterKilled: true, KillingItem: weapon.Id);
        var floor = new FloorOutcome(1, Cleared: true, ImmutableList.Create(killRound));

        var beats = AttributionEngine.ComputeBeats(
            ImmutableList.Create(floor), ImmutableList.Create(hero), items, VenueRegistry.Mine);
        var beat = Assert.Single(beats, b => b.Beat == BeatType.KillingBlow);

        // Ground truth via the SAME CombatMath calls the engine itself makes for this branch --
        // never a hand-typed constant that could silently drift from a class-balance change.
        var attackWithoutItem = CombatMath.HeroAttack(hero, items.Remove(weapon.Id.Value));
        var expectedDealtWithoutItem = CombatMath.HeroDamage(attackWithoutItem, roll: 4, VenueRegistry.Mine.MonsterDefense(1));

        Assert.Contains("read 4", beat.Detail);
        Assert.Contains($"deals {expectedDealtWithoutItem}, not 50", beat.Detail);
        Assert.True(expectedDealtWithoutItem < 50,
            "fixture must make the weapon's Attack stat actually decisive to the number, or this proves nothing");
    }

    // ── LethalSave ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void LethalSave_DetailCarries_TheRawBlow_TheDefenseDrunk_AndWhereTheHeroStood()
    {
        var armor = PlayerArmor(1, defense: 12);
        var hero = HeroWith(1, new GearSet(null, null, armor.Id), hp: 14);
        var items = ImmutableSortedDictionary<int, Item>.Empty.Add(1, armor);
        var venue = SingleFloorVenue(monsterAttack: 20, monsterDefense: 0);

        // Hero rolls 3 (deals 3, irrelevant here), monster rolls 5. With the armor's 12 defense the
        // hit reads 12 and the hero stands at 2; without it the SAME roll reads 25 and the hero falls.
        var round = new CombatEvent(
            1, hero.Id, "Cave Rat", ImmutableList.Create(3, 5), DamageDealt: 3, DamageTaken: 12,
            MonsterKilled: false, KillingItem: null);
        var floor = new FloorOutcome(1, Cleared: false, ImmutableList.Create(round));

        var beats = AttributionEngine.ComputeBeats(
            ImmutableList.Create(floor), ImmutableList.Create(hero), items, venue);
        var beat = Assert.Single(beats, b => b.Beat == BeatType.LethalSave);

        Assert.Contains("read 25", beat.Detail); // monsterAttack(20) + monsterRoll(5)
        Assert.Contains("drank 12 of it", beat.Detail);
        Assert.Contains("stood at 2", beat.Detail); // 14 - 12
        Assert.Contains("falls", beat.Detail);
    }

    // ── BreakpointClear ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BreakpointClear_DetailCarries_ThePartyPowerAndTheGate()
    {
        var weapon = PlayerWeapon(1, attack: 50);
        var items = ImmutableSortedDictionary<int, Item>.Empty.Add(1, weapon);
        var hero1 = HeroWith(1, new GearSet(weapon.Id, null, null), level: 1);
        var hero2 = HeroWith(2, GearSet.Empty, level: 1, name: "Halvar");

        var avgWithItem = CombatMath.PartyAveragePower(new[] { hero1, hero2 }, items);
        var withoutItem = items.Remove(weapon.Id.Value);
        var avgWithoutItem = CombatMath.PartyAveragePower(new[] { hero1, hero2 }, withoutItem);
        var gate = avgWithoutItem + (avgWithItem - avgWithoutItem) / 2;
        Assert.True(gate > avgWithoutItem && gate <= avgWithItem); // fixture sanity: gate strictly between

        var floor = new FloorOutcome(1, Cleared: true, ImmutableList<CombatEvent>.Empty);
        var venue = SingleFloorVenue(gate: gate);

        var beats = AttributionEngine.ComputeBeats(
            ImmutableList.Create(floor), ImmutableList.Create(hero1, hero2), items, venue);
        var beat = Assert.Single(beats, b => b.Beat == BeatType.BreakpointClear);

        Assert.Contains($"read {avgWithItem} against the gate at {gate}", beat.Detail);
        Assert.Contains($"Without it, {avgWithoutItem}", beat.Detail);
        Assert.Contains("under the gate", beat.Detail);
    }

    // ── Provisioned ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Provisioned_DetailCarries_TheNaiveMarginThatProvesItDidNotMatter()
    {
        // Round 2 quaff at 5 hp; only 3 more damage lands — the fight's own numbers leave the hero
        // standing even without the heal (naiveHpWithoutHeal = 5 - (3 + 0) = 2, positive).
        var salve = Salve(10);
        var beats = ConsumableBeats(
            salve,
            Round(taken: 8),
            Round(taken: 3, killed: false, new ConsumableUse(salve.Id, Round: 2, HpBefore: 5, HpAfter: 11)),
            Round(taken: 0, killed: true));

        var beat = Assert.Single(beats, b => b.Beat is BeatType.Provisioned or BeatType.PotionLifesave);
        Assert.Equal(BeatType.Provisioned, beat.Beat); // fixture-authenticity check

        Assert.Contains("drank it at round 2, 5 to 11", beat.Detail);
        Assert.Contains("read 2 from there", beat.Detail);
        Assert.Contains("No credit taken", beat.Detail);
    }

    [Fact]
    public void Provisioned_HeroDiedAnyway_ReportsTheHonestNegativeMargin_NeverClaimsSurvival()
    {
        // The quaff bought a round but the hero died in the fight regardless (only 2 rounds, no
        // kill) — naiveHpWithoutHeal = 5 - 12 = -7. Still Provisioned (never upgraded, since the
        // hero did not actually survive), and the Detail must not spin a negative margin into a
        // survival claim it did not earn.
        var salve = Salve(10);
        var beats = ConsumableBeats(
            salve,
            Round(taken: 8),
            Round(taken: 12, killed: false, new ConsumableUse(salve.Id, Round: 2, HpBefore: 5, HpAfter: 11)));

        var beat = Assert.Single(beats, b => b.Beat is BeatType.Provisioned or BeatType.PotionLifesave);
        Assert.Equal(BeatType.Provisioned, beat.Beat); // fixture-authenticity check

        Assert.Contains("read -7 from there", beat.Detail);
        Assert.DoesNotContain("still standing", beat.Detail);
    }

    // ── PotionLifesave ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PotionLifesave_DetailCarries_TheMarginThatWouldHaveKilled_AndWhereTheFightActuallyEnded()
    {
        // Round 2 quaff at 5 hp; 8 damage follows. Without the heal: 5 - 8 = -3 (dead). With it:
        // 11 - 8 = 3, and the hero finishes the fight alive.
        var salve = Salve(10);
        var beats = ConsumableBeats(
            salve,
            Round(taken: 8),
            Round(taken: 8, killed: false, new ConsumableUse(salve.Id, Round: 2, HpBefore: 5, HpAfter: 11)),
            Round(taken: 0, killed: true));

        var beat = Assert.Single(beats, b => b.Beat is BeatType.Provisioned or BeatType.PotionLifesave);
        Assert.Equal(BeatType.PotionLifesave, beat.Beat); // fixture-authenticity check

        Assert.Contains("saved Torvald's life", beat.Detail);
        Assert.Contains("reads -3 without it", beat.Detail);
        Assert.Contains("drank it at 5 to 11", beat.Detail);
        Assert.Contains("closed the fight at 3", beat.Detail);
    }

    // ── No participation credit, nowhere ─────────────────────────────────────────────────────────

    /// <summary>
    /// A counterfactual is what would have happened without this piece -- never a share, a
    /// percentage, a total, a rating, or a contribution score (CLAUDE.md, link4). Swept over a real
    /// resolver run (every beat type this venue's structure can realistically produce), never just
    /// the hand-built fixtures above.
    /// </summary>
    private static readonly Regex ParticipationCreditPattern = new(
        @"%|\bpercent\b|\bpercentage\b|\bshare\b|\bscore\b|\bratio\b|\brating\b|\d+\s*/\s*\d+|\d+\s*out of\s*\d+",
        RegexOptions.IgnoreCase);

    [Fact]
    public void NoDetail_EverContainsARatioPercentageShareOrScore_AcrossASeedSweep()
    {
        var weapon = PlayerWeapon(1, attack: 30);
        var armor = PlayerArmor(2, defense: 10);
        var salve = Salve(3);
        var items = ImmutableSortedDictionary<int, Item>.Empty.Add(1, weapon).Add(2, armor).Add(3, salve);

        var beatTypesSeen = new HashSet<BeatType>();
        var checkedDetails = 0;

        for (ulong seed = 0; seed < 300; seed++)
        {
            var hero = HeroWith(1, new GearSet(weapon.Id, null, armor.Id), hp: 24, level: 2) with
            {
                Pack = ImmutableList.Create(salve.Id),
            };

            var result = ExpeditionResolver.Resolve(
                ImmutableList.Create(hero), items, VenueRegistry.Mine, targetFloor: 5, new Pcg32(RngState.FromSeed(seed)));

            foreach (var beat in result.Beats)
            {
                checkedDetails++;
                beatTypesSeen.Add(beat.Beat);
                Assert.False(ParticipationCreditPattern.IsMatch(beat.Detail),
                    $"Detail reads like a participation credit, not a counterfactual: \"{beat.Detail}\"");
            }
        }

        Assert.True(checkedDetails > 20, $"Only {checkedDetails} beats produced across 300 seeds -- too few to prove the property.");
        Assert.True(beatTypesSeen.Count >= 3,
            $"Only {beatTypesSeen.Count} distinct beat type(s) seen ({string.Join(", ", beatTypesSeen)}) -- widen the fixture.");
    }

    // ── Golden-safety note ──────────────────────────────────────────────────────────────────────
    //
    // AttributionBeat.Detail is serialized state (StateFieldReachCensusTests pins it RENDERED), so
    // this unit's wording change moves the golden replay hash in AtomicEquivalenceTests. That is
    // expected -- P2-PROOF-14's own brief reserves re-recording the golden to the orchestrator, so
    // this file does not touch it.
}
