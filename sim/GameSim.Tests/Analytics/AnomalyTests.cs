using System.Collections.Immutable;
using Analytics;
using GameSim.Chronicle;
using GameSim.Contracts;

namespace GameSim.Tests.Analytics;

/// <summary>
/// The heavy-event detector (observability plan U3): each rule fires on a synthetic chronicle
/// shaped to violate it, and a healthy corpus stays silent (no false positives).
/// </summary>
public class AnomalyTests
{
    private static Hero LivingHero(int id = 1) => new(
        new HeroId(id), $"H{id}", "vanguard", Level: 1, MaxHp: 20, Gold: 10,
        new GearSet(null, null, null), ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 1, DiedOnDay: null);

    private static ChronicleData Run(ulong seed, int day, IEnumerable<GameEvent> events, params Hero[] heroes) =>
        new(seed, day, DayPhase.Morning,
            (heroes.Length == 0 ? [LivingHero()] : heroes).ToImmutableList(),
            events.ToImmutableList());

    private static ImmutableList<GameEvent> HealthyBeats(int throughDay)
    {
        var events = ImmutableList.CreateBuilder<GameEvent>();
        for (var day = 1; day <= throughDay; day++)
        {
            events.Add(new AttributionBeatEvent(
                BeatType.KillingBlow, new ItemId(1), new HeroId(1), Floor: 1, "beat") with
            { Id = new EventId(day), Day = day });
        }

        return events.ToImmutable();
    }

    [Fact]
    public void HealthyRun_ProducesNoAnomalies()
    {
        // Beats every day, no deaths, no economy motion — nothing should fire.
        var run = Run(seed: 7, day: 31, HealthyBeats(30));

        Assert.Empty(Anomalies.Detect([run]));
    }

    [Fact]
    public void BeatStarvation_Fires_WhenTrailingWindowHasNoBeats_AndHeroesLive()
    {
        // Beats stop on day 15; day 30 trailing window (21-30) is empty.
        var run = Run(seed: 7, day: 31, HealthyBeats(15));

        var hit = Assert.Single(Anomalies.Detect([run]), a => a.Rule == "beat-starvation");
        Assert.Equal(AnomalySeverity.High, hit.Severity);
        Assert.Equal(7UL, hit.Seed);
        Assert.Contains("--seed 7", hit.ReproCommand, StringComparison.Ordinal);
    }

    [Fact]
    public void BeatStarvation_Silent_WhenTownIsEmpty()
    {
        // Everyone dead long before the window (day 10 < window start 21) and no recruits:
        // no town, no gauge. (Living recruits who never depart DO fire — deliberately loud.)
        var dead = LivingHero() with { Alive = false, DiedOnDay = 10 };
        var run = Run(seed: 7, day: 31, HealthyBeats(15), dead);

        Assert.DoesNotContain(Anomalies.Detect([run]), a => a.Rule == "beat-starvation");
    }

    [Fact]
    public void BeatStarvation_Fires_WhenWipeHappensInsideTheWindow()
    {
        // Beat-less window 21-30 with the party dying on day 30: starvation preceded the wipe —
        // the gauge must not be silenced by end-of-run roster liveness.
        var dead = LivingHero() with { Alive = false, DiedOnDay = 30 };
        var run = Run(seed: 7, day: 31, HealthyBeats(15), dead);

        Assert.Contains(Anomalies.Detect([run]), a => a.Rule == "beat-starvation");
    }

    private static ImmutableList<GameEvent> WithFloorDeaths(ImmutableList<GameEvent> events, int count, int idBase)
    {
        var builder = events.ToBuilder();
        for (var i = 0; i < count; i++)
        {
            builder.Add(new HeroDied(new HeroId(i + 1), Floor: 2, "ambush", new GearSet(null, null, null)) with
            { Id = new EventId(idBase + i), Day = i + 5 });
        }

        return builder.ToImmutable();
    }

    [Fact]
    public void DeathSpike_Fires_AgainstCorpusBaseline()
    {
        // Spiky run: 6 deaths on floor 2 vs 1 each in the others: 6×2 > 3×2 → fires.
        var spiky = Run(seed: 1, day: 31, WithFloorDeaths(HealthyBeats(30), 6, 40));
        var calm1 = Run(seed: 2, day: 31, WithFloorDeaths(HealthyBeats(30), 1, 60));
        var calm2 = Run(seed: 3, day: 31, WithFloorDeaths(HealthyBeats(30), 1, 70));

        var hit = Assert.Single(Anomalies.Detect([spiky, calm1, calm2]), a => a.Rule == "death-spike");
        Assert.Equal(1UL, hit.Seed);
        Assert.Contains("floor 2", hit.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void DeathSpike_Silent_OnFrontierRun_WithNoBaseline()
    {
        // Only one run reached floor 2 (othersTotal == 0): progress, not drift — stays silent.
        var frontier = Run(seed: 1, day: 31, WithFloorDeaths(HealthyBeats(30), 4, 40));
        var calm1 = Run(seed: 2, day: 31, HealthyBeats(30));
        var calm2 = Run(seed: 3, day: 31, HealthyBeats(30));

        Assert.DoesNotContain(Anomalies.Detect([frontier, calm1, calm2]), a => a.Rule == "death-spike");
    }

    [Fact]
    public void GoldMintSpike_Fires_OnRunawayTrailingWindow()
    {
        // Opening window mints 10g; trailing window mints 600g (> 3× and ≥ 500g).
        var events = HealthyBeats(30).ToBuilder();
        events.Add(new LootIncomeReceived(new HeroId(1), Gold: 10) with { Id = new EventId(100), Day = 2 });
        events.Add(new LootIncomeReceived(new HeroId(1), Gold: 600) with { Id = new EventId(101), Day = 28 });
        var run = Run(seed: 4, day: 31, events.ToImmutable());

        var hit = Assert.Single(Anomalies.Detect([run]), a => a.Rule == "gold-mint-spike");
        Assert.Equal(AnomalySeverity.Medium, hit.Severity);
    }

    [Fact]
    public void DeadShop_Fires_WhenCraftsNeverSell()
    {
        var events = HealthyBeats(20).ToBuilder();
        for (var i = 0; i < 6; i++)
        {
            events.Add(new ItemCrafted(new ItemId(i + 10), QualityGrade.Fine) with
            { Id = new EventId(200 + i), Day = i + 1 });
        }

        var run = Run(seed: 5, day: 21, events.ToImmutable());

        Assert.Single(Anomalies.Detect([run]), a => a.Rule == "dead-shop");
    }

    [Fact]
    public void DeadShop_Silent_WhenAnythingSellsFromPlayerShop()
    {
        var events = HealthyBeats(20).ToBuilder();
        for (var i = 0; i < 6; i++)
        {
            events.Add(new ItemCrafted(new ItemId(i + 10), QualityGrade.Fine) with
            { Id = new EventId(200 + i), Day = i + 1 });
        }

        events.Add(new ItemSold(new ItemId(10), new HeroId(1), Price: 20, FromPlayerShop: true) with
        { Id = new EventId(300), Day = 8 });
        var run = Run(seed: 5, day: 21, events.ToImmutable());

        Assert.DoesNotContain(Anomalies.Detect([run]), a => a.Rule == "dead-shop");
    }

    [Fact]
    public void TariffSaturation_Fires_OnRepeatedAtCapDeltas()
    {
        // 8 tariffs at 9%+ of base across 8 days (cap-adjacent), interleaved with healthy beats.
        var events = HealthyBeats(30).ToBuilder();
        for (var i = 0; i < 8; i++)
        {
            events.Add(new TariffApplied("deepvein", "copper", BaseLineCost: 100, PlayerCost: 91, Delta: -9) with
            { Id = new EventId(400 + i), Day = i + 10 });
        }

        var run = Run(seed: 6, day: 31, events.ToImmutable());

        var hit = Assert.Single(Anomalies.Detect([run]), a => a.Rule == "tariff-saturation");
        Assert.Equal(10, hit.DayFrom);
        Assert.Equal(17, hit.DayTo);
    }

    [Fact]
    public void BountyMonoculture_Fires_WhenJudgmentsDegenerate()
    {
        var events = HealthyBeats(30).ToBuilder();
        for (var i = 0; i < 21; i++)
        {
            events.Add(new BountyJudged(new BountyId(i + 1), new HeroId(1), Accepted: false, "too deep") with
            { Id = new EventId(500 + i), Day = (i % 30) + 1 });
        }

        var run = Run(seed: 8, day: 31, events.ToImmutable());

        var hit = Assert.Single(Anomalies.Detect([run]), a => a.Rule == "bounty-monoculture");
        Assert.Contains("0 accepted", hit.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void BountyMonoculture_Silent_JustInsideBothBoundaries()
    {
        // 11/200 accepted = 5.5% — above the 5% reject threshold; integer floor must not fire it
        // (cross-multiplied compare, not floored percent).
        var events = HealthyBeats(30).ToBuilder();
        for (var i = 0; i < 200; i++)
        {
            events.Add(new BountyJudged(new BountyId(i + 1), new HeroId(1), Accepted: i < 11, "r") with
            { Id = new EventId(700 + i), Day = (i % 30) + 1 });
        }

        var run = Run(seed: 12, day: 31, events.ToImmutable());

        Assert.DoesNotContain(Anomalies.Detect([run]), a => a.Rule == "bounty-monoculture");
    }

    [Fact]
    public void SingleRunCorpus_DoesNotDivideByZero_AndRenderHandlesEmpty()
    {
        var run = Run(seed: 9, day: 3, HealthyBeats(2));

        var anomalies = Anomalies.Detect([run]);
        var rendered = Anomalies.Render(anomalies, runCount: 1);

        Assert.Contains("Runs analyzed: 1", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_OrdersBySeverity_AndCarriesReproPointer()
    {
        // Starvation (HIGH) + monoculture (LOW) in one run: HIGH renders first, both carry repro.
        var events = HealthyBeats(15).ToBuilder();
        for (var i = 0; i < 21; i++)
        {
            events.Add(new BountyJudged(new BountyId(i + 1), new HeroId(1), Accepted: true, "gold") with
            { Id = new EventId(600 + i), Day = (i % 30) + 1 });
        }

        var run = Run(seed: 10, day: 31, events.ToImmutable());

        var anomalies = Anomalies.Detect([run]);
        Assert.True(anomalies.Count >= 2);
        Assert.Equal(AnomalySeverity.High, anomalies[0].Severity);
        var rendered = Anomalies.Render(anomalies, 1);
        Assert.Contains("batch --seeds 1 --seed 10", rendered, StringComparison.Ordinal);
        Assert.Contains("docs/debugging.md", rendered, StringComparison.Ordinal);
    }
}
