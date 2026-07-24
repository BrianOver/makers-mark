using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Heroes;

namespace GameSim.Tests.Drama;

using static DramaFixtures;

/// <summary>
/// Wave 4 (U22, "kin-of-the-dead"): a recruit generated while a famous-dead legend exists in
/// <see cref="DramaState.Memorials"/> starts with a small <see cref="Hero.MoodPermille"/> bump
/// above neutral (<see cref="RecruitSystem.KinOfDeadMoodBonus"/>); one generated with no such
/// legend starts neutral, exactly as before U22.
/// </summary>
public class RecruitOpinionSeedTests
{
    private static Item SignedItem(int id, int slotArmor = 0) => new(
        new ItemId(id), "recipe-signed", "Signed Blade", ItemSlot.Weapon, QualityGrade.Masterwork,
        new ItemStats(20, 0, Weight: 4), new MakersMark("You", CraftedOnDay: 1),
        ImmutableList<ItemHistoryEntry>.Empty)
    {
        SignedName = "Emberfall",
    };

    // ── LegendQuery unit coverage ──────────────────────────────────────────────────────────────

    [Fact]
    public void LegendQuery_NoMemorials_NoFamousLegend()
    {
        var state = NewWorld();

        Assert.False(LegendQuery.HasFamousDeadLegend(state));
    }

    [Fact]
    public void LegendQuery_MemorialWithFewBeats_NotFamous()
    {
        var state = NewWorld();
        var result = Result(
            party: [1], survivors: [], deaths: [1],
            floors: [new FloorOutcome(1, false, [Combat(1, 1, "Cave Rat", taken: 40)])],
            beats: [new AttributionBeat(BeatType.KillingBlow, new ItemId(90), new HeroId(1), 1, "one beat only")]);

        var evening = TickEvening(AtEvening(state, result));
        Assert.Single(evening.NewState.Drama.Memorials);

        Assert.False(LegendQuery.HasFamousDeadLegend(evening.NewState));
    }

    [Fact]
    public void LegendQuery_MemorialWithThreeOrMoreBeats_IsFamous()
    {
        var state = NewWorld();
        var beats = new[]
        {
            new AttributionBeat(BeatType.KillingBlow, new ItemId(90), new HeroId(1), 1, "beat one"),
            new AttributionBeat(BeatType.LethalSave, new ItemId(90), new HeroId(1), 1, "beat two"),
            new AttributionBeat(BeatType.BreakpointClear, new ItemId(90), new HeroId(1), 1, "beat three"),
        };
        var result = Result(
            party: [1], survivors: [], deaths: [1],
            floors: [new FloorOutcome(1, true, [Combat(1, 1, "Cave Rat", taken: 40)])],
            beats: beats);

        var evening = TickEvening(AtEvening(state, result));

        Assert.True(LegendQuery.HasFamousDeadLegend(evening.NewState));
        Assert.True(LegendQuery.IsFamousDead(evening.NewState, new HeroId(1)));
    }

    [Fact]
    public void LegendQuery_DiedBearingSignedWork_IsFamous_EvenWithNoAttributionBeats()
    {
        var state = Equip(NewWorld(), 1, SignedItem(90));
        var result = Result(
            party: [1], survivors: [], deaths: [1],
            floors: [new FloorOutcome(1, false, [Combat(1, 1, "Cave Rat", taken: 40)])]);

        var evening = TickEvening(AtEvening(state, result));

        Assert.True(LegendQuery.DiedBearingSignedWork(evening.NewState, new HeroId(1)));
        Assert.True(LegendQuery.HasFamousDeadLegend(evening.NewState));
    }

    // ── RecruitSystem seeding ───────────────────────────────────────────────────────────────────

    [Fact]
    public void RecruitSystem_NoFamousDeadLegend_SeedsNeutralMood()
    {
        var state = NewWorld() with
        {
            Heroes = NewWorld().Heroes.SetItem(1, NewWorld().Heroes[1] with { Alive = false, DiedOnDay = 1 }),
        };

        var tick = Tick(state, new RecruitSystem());

        var recruit = Assert.Single(tick.Events.OfType<RecruitArrived>());
        Assert.Equal(0, tick.NewState.Heroes[recruit.Hero.Value].MoodPermille);
    }

    [Fact]
    public void RecruitSystem_FamousDeadLegendExists_SeedsMoodAboveNeutral()
    {
        var world = NewWorld();
        var beats = new[]
        {
            new AttributionBeat(BeatType.KillingBlow, new ItemId(90), new HeroId(1), 1, "beat one"),
            new AttributionBeat(BeatType.LethalSave, new ItemId(90), new HeroId(1), 1, "beat two"),
            new AttributionBeat(BeatType.BreakpointClear, new ItemId(90), new HeroId(1), 1, "beat three"),
        };
        var result = Result(
            party: [1], survivors: [], deaths: [1],
            floors: [new FloorOutcome(1, true, [Combat(1, 1, "Cave Rat", taken: 40)])],
            beats: beats);

        var evening = TickEvening(AtEvening(world, result));
        Assert.True(LegendQuery.HasFamousDeadLegend(evening.NewState));

        var tick = Tick(evening.NewState, new RecruitSystem());
        var recruit = Assert.Single(tick.Events.OfType<RecruitArrived>());

        Assert.Equal(RecruitSystem.KinOfDeadMoodBonus, tick.NewState.Heroes[recruit.Hero.Value].MoodPermille);
    }

    [Fact]
    public void RecruitSystem_MoodSeed_IsDeterministic_AcrossIdenticalRuns()
    {
        GameState Setup()
        {
            var world = NewWorld(seed: 11);
            var beats = new[]
            {
                new AttributionBeat(BeatType.KillingBlow, new ItemId(90), new HeroId(1), 1, "one"),
                new AttributionBeat(BeatType.LethalSave, new ItemId(90), new HeroId(1), 1, "two"),
                new AttributionBeat(BeatType.BreakpointClear, new ItemId(90), new HeroId(1), 1, "three"),
            };
            var result = Result(
                party: [1], survivors: [], deaths: [1],
                floors: [new FloorOutcome(1, true, [Combat(1, 1, "Cave Rat", taken: 40)])],
                beats: beats);
            return TickEvening(AtEvening(world, result)).NewState;
        }

        var a = Tick(Setup(), new RecruitSystem());
        var b = Tick(Setup(), new RecruitSystem());

        var recruitA = Assert.Single(a.Events.OfType<RecruitArrived>());
        var recruitB = Assert.Single(b.Events.OfType<RecruitArrived>());
        Assert.Equal(a.NewState.Heroes[recruitA.Hero.Value], b.NewState.Heroes[recruitB.Hero.Value]);
    }

    // ── PKD7: mood seeding never touches muster/floor/expedition ──────────────────────────────

    [Fact]
    public void MoodSeed_NeverAffectsMusterPlan_ByteMatch()
    {
        var baseline = HeroRoster.StartingSix();
        var elevated = baseline.SetItem(1, baseline[1] with { MoodPermille = RecruitSystem.KinOfDeadMoodBonus });

        var planBaseline = MusterPlan.Compute(baseline, ImmutableList<Bounty>.Empty);
        var planElevated = MusterPlan.Compute(elevated, ImmutableList<Bounty>.Empty);

        // ImmutableList<T> compares by reference, not value — flatten to value tuples so this
        // asserts the actual roster/floor/venue CONTENT is identical, not list identity.
        object Flatten(PartyPlan p) => (Roster: string.Join(",", p.Roster.Select(h => h.Value)), p.TargetFloor, p.VenueId);
        Assert.Equal(planBaseline.Select(Flatten), planElevated.Select(Flatten));
    }
}
