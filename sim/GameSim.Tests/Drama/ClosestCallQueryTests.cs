using System.Collections.Immutable;
using GameSim.Classes;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Expedition;

namespace GameSim.Tests.Drama;

using static DramaFixtures;

/// <summary>
/// P2-PROOF-18: <see cref="ClosestCallQuery"/>'s two load-bearing invariants are (1) it renders
/// for NOBODY who stayed above <see cref="CombatMath.ShouldFlee(int,int)"/>'s own 25% line — most
/// nights, by construction — and (2) its <see cref="ClosestCallQuery.LowHpMoment.MinHp"/> can never
/// disagree with the replay <see cref="AttributionEngine"/> itself performs, because it is built
/// from <see cref="TellingQuery.ReplayHpPerRound"/>, the exact shared method
/// <see cref="TellingQuery.ReplayHp"/>'s own doc comment already pins to AttributionEngine's hp
/// dictionary. Every case here is state-in/record-out: no tick, no RNG draw, no clock.
/// </summary>
public class ClosestCallQueryTests
{
    private const int Hero = 1;
    private const int Other = 2;
    private static readonly ItemId MarkedArmor = new(20);
    private static readonly ItemId RivalArmor = new(21);

    private static HeroAtDeparture Departure(int maxHp, ItemId? armor = null) =>
        new(new HeroId(Hero), "Kael", ClassRegistry.StrikerId, Level: 1, maxHp, Weapon: null, Shield: null, armor);

    private static ImmutableSortedDictionary<int, Item> Items(params Item[] items) =>
        items.ToImmutableSortedDictionary(i => i.Id.Value, i => i);

    private static ExpeditionResult WithDeparture(ExpeditionResult result, HeroAtDeparture departure) =>
        result with { PartyAtDeparture = ImmutableList.Create(departure) };

    [Fact]
    public void For_NonSurvivor_ReturnsNull()
    {
        var result = Result(party: [Hero], survivors: [], deaths: [Hero]);

        Assert.Null(ClosestCallQuery.For(result, new HeroId(Hero), Items()));
    }

    [Fact]
    public void For_HeroNotInThisExpeditionAtAll_ReturnsNull()
    {
        var result = Result(party: [Other], survivors: [Other], deaths: []);

        Assert.Null(ClosestCallQuery.For(result, new HeroId(Hero), Items()));
    }

    [Fact]
    public void For_SurvivorWithNoRecordedDeparture_ReturnsNull()
    {
        // Result() defaults PartyAtDeparture to empty -- an older/synthetic log with no snapshot.
        var result = Result(party: [Hero], survivors: [Hero], deaths: []);

        Assert.Null(ClosestCallQuery.For(result, new HeroId(Hero), Items()));
    }

    [Fact]
    public void For_SurvivorWhoNeverDippedBelowFleeLine_ReturnsNull()
    {
        // MaxHp 20, one round of 5 taken -> floor 15, well above the 25% (5hp) line.
        var floor = new FloorOutcome(1, Cleared: true, ImmutableList.Create(
            Combat(1, Hero, "Cave Rat", dealt: 3, taken: 5)));
        var result = WithDeparture(
            Result(party: [Hero], survivors: [Hero], deaths: [], floors: [floor]),
            Departure(maxHp: 20));

        Assert.Null(ClosestCallQuery.For(result, new HeroId(Hero), Items()));
    }

    [Fact]
    public void For_SurvivorWhoDippedBelowFleeLine_NamesFloorAndMonster()
    {
        // MaxHp 20; round 1 leaves 18 (safe), round 2 leaves 2 (10% -- below the 25%/5hp line),
        // and the resolver would have fled at the top of the next round -- exactly the "came up
        // with 2 HP" shape the felt moment names. Two rounds recorded -- one per floor -- so the
        // low-water mark's own floor/monster are unambiguous.
        var floor1 = new FloorOutcome(1, Cleared: true, ImmutableList.Create(
            Combat(1, Hero, "Cave Rat", dealt: 3, taken: 2)));
        var floor2 = new FloorOutcome(2, Cleared: true, ImmutableList.Create(
            Combat(2, Hero, "Deep Ghoul", dealt: 3, taken: 16)));
        var result = WithDeparture(
            Result(party: [Hero], survivors: [Hero], deaths: [], floors: [floor1, floor2]),
            Departure(maxHp: 20));

        var moment = ClosestCallQuery.For(result, new HeroId(Hero), Items());

        Assert.NotNull(moment);
        Assert.Equal(2, moment!.MinHp);
        Assert.Equal(20, moment.MaxHp);
        Assert.Equal(2, moment.Floor);
        Assert.Equal("Deep Ghoul", moment.MonsterKind);
    }

    [Fact]
    public void For_MinHp_MatchesAttributionEnginesOwnHpReplay_RoundByRound()
    {
        // Pin: ClosestCallQuery is implemented over TellingQuery.ReplayHpPerRound, the exact
        // shared per-round formula TellingQuery.ReplayHp's own doc comment already pins to
        // AttributionEngine's hp dictionary (pre-round heals, damage, modifier delta, post-round
        // heals -- no Uses/ModifierHpDelta fire in this fixture, so the trace is plain subtraction).
        // Independently retracing that SAME sequence here and asserting equality is what stands in
        // for calling AttributionEngine directly: a number this query reports that could disagree
        // with the engine's own replay is worse than no number at all.
        var combats = ImmutableList.Create(
            Combat(1, Hero, "Cave Rat", dealt: 3, taken: 4),
            Combat(1, Hero, "Cave Rat", dealt: 3, taken: 13));
        var floor = new FloorOutcome(1, Cleared: true, combats);
        var result = WithDeparture(
            Result(party: [Hero], survivors: [Hero], deaths: [], floors: [floor]),
            Departure(maxHp: 20));

        var afterRound1 = 20 - combats[0].DamageTaken;
        var afterRound2 = afterRound1 - combats[1].DamageTaken;
        var expectedMinHp = Math.Min(afterRound1, afterRound2);

        var moment = ClosestCallQuery.For(result, new HeroId(Hero), Items());

        Assert.NotNull(moment);
        Assert.Equal(expectedMinHp, moment!.MinHp);
    }

    [Fact]
    public void For_ArmorCarriesTheMark_ReportsTrue()
    {
        var markedArmor = PlayerItem(MarkedArmor.Value, "Iron Plate", ItemSlot.Armor, attack: 0, defense: 3);
        var floor = new FloorOutcome(1, Cleared: true, ImmutableList.Create(
            Combat(1, Hero, "Cave Rat", dealt: 3, taken: 19)));
        var result = WithDeparture(
            Result(party: [Hero], survivors: [Hero], deaths: [], floors: [floor]),
            Departure(maxHp: 20, armor: MarkedArmor));

        var moment = ClosestCallQuery.For(result, new HeroId(Hero), Items(markedArmor));

        Assert.NotNull(moment);
        Assert.True(moment!.ArmorMarked);
    }

    [Fact]
    public void For_ArmorIsRivalStock_ReportsFalse()
    {
        var rivalArmor = RivalItem(RivalArmor.Value, "Store Iron", ItemSlot.Armor, attack: 0, defense: 3);
        var floor = new FloorOutcome(1, Cleared: true, ImmutableList.Create(
            Combat(1, Hero, "Cave Rat", dealt: 3, taken: 19)));
        var result = WithDeparture(
            Result(party: [Hero], survivors: [Hero], deaths: [], floors: [floor]),
            Departure(maxHp: 20, armor: RivalArmor));

        var moment = ClosestCallQuery.For(result, new HeroId(Hero), Items(rivalArmor));

        Assert.NotNull(moment);
        Assert.False(moment!.ArmorMarked);
    }

    [Fact]
    public void For_NoArmorWorn_ReportsFalse()
    {
        var floor = new FloorOutcome(1, Cleared: true, ImmutableList.Create(
            Combat(1, Hero, "Cave Rat", dealt: 3, taken: 19)));
        var result = WithDeparture(
            Result(party: [Hero], survivors: [Hero], deaths: [], floors: [floor]),
            Departure(maxHp: 20, armor: null));

        var moment = ClosestCallQuery.For(result, new HeroId(Hero), Items());

        Assert.NotNull(moment);
        Assert.False(moment!.ArmorMarked);
    }
}
