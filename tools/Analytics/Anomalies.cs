using System.Text;
using GameSim.Chronicle;
using GameSim.Contracts;

namespace Analytics;

/// <summary>How loudly an anomaly should be treated.</summary>
public enum AnomalySeverity
{
    Low,
    Medium,
    High,
}

/// <summary>
/// One rule hit: what drifted, where, and how to reproduce it. The repro pointer is the load-bearing
/// field (observability plan R1/R3) — a Claude must be able to re-create the window from it alone.
/// </summary>
public sealed record Anomaly(
    AnomalySeverity Severity,
    string Rule,
    ulong Seed,
    int DayFrom,
    int DayTo,
    string Detail)
{
    public string ReproCommand =>
        $"dotnet run --project sim/GameSim.Cli -- batch --seeds 1 --seed {Seed} --days {DayTo}";
}

/// <summary>
/// The heavy-event detector (observability plan U3): pure rules over exported chronicles that flag
/// when the game drifts unhealthy. Lives in Analytics — the sim never observes itself (KTD4).
/// Thresholds are consts, deliberately blunt for v1; tune with evidence, not vibes.
///
/// Rules:
/// <list type="bullet">
/// <item><b>beat-starvation</b> (HIGH) — no attribution beats in the trailing window while heroes
/// live. The fun thesis (attribution pride) is failing; the single most important gauge.</item>
/// <item><b>death-spike</b> (MEDIUM) — a floor kills far above the corpus baseline.</item>
/// <item><b>gold-mint-spike</b> (MEDIUM) — trailing mint rate (loot + bounty payouts) far above the
/// campaign's opening rate: inflation runaway.</item>
/// <item><b>dead-shop</b> (MEDIUM) — the player crafts but nothing sells from the player shop:
/// crafting into the void.</item>
/// <item><b>tariff-saturation</b> (LOW) — tariff repeatedly at/near the cap: standing pegged, the
/// lever stopped mattering.</item>
/// <item><b>bounty-monoculture</b> (LOW) — bounty judgments almost all one direction: the decision
/// degenerated. (Shopping/floor-target monoculture activates with the decision-trace events, U4.)</item>
/// </list>
/// </summary>
public static class Anomalies
{
    // Windows and thresholds (v1 consts — see doc comment).
    public const int TrailingWindowDays = 10;
    public const int BeatStarvationMinDay = 12;         // don't fire on a campaign too young to have beats
    public const int DeathSpikeMinDeaths = 3;
    public const int DeathSpikeFactor = 3;              // run's floor deaths > factor × corpus mean
    public const int MintSpikeFactor = 3;               // trailing rate > factor × opening rate
    public const int MintSpikeMinGold = 500;            // and the trailing window minted at least this
    public const int DeadShopMinCrafts = 5;
    public const int DeadShopMinDay = 15;
    public const int TariffSaturationPerMille = 90;     // |delta| ≥ 9% of base ≈ at the 10% cap
    public const int TariffSaturationMinEvents = 8;
    public const int TariffSaturationMinSpanDays = 5;
    public const int MonocultureMinJudgments = 20;
    public const int MonoculturePerCentEither = 95;

    /// <summary>Run every rule over the corpus. Deterministic order: by seed, then rule name.</summary>
    public static IReadOnlyList<Anomaly> Detect(IReadOnlyList<ChronicleData> runs)
    {
        var found = new List<Anomaly>();

        // Corpus baseline for the death-spike rule: mean deaths per floor per run.
        var corpusDeathsByFloor = new Dictionary<int, int>();
        foreach (var run in runs)
        {
            foreach (var death in run.Events.OfType<HeroDied>())
            {
                corpusDeathsByFloor[death.Floor] = corpusDeathsByFloor.GetValueOrDefault(death.Floor) + 1;
            }
        }

        foreach (var run in runs)
        {
            var lastFullDay = run.Day - 1; // run.Day is the in-progress day
            if (lastFullDay < 1)
            {
                continue;
            }

            BeatStarvation(run, lastFullDay, found);
            DeathSpike(run, runs.Count, corpusDeathsByFloor, found);
            GoldMintSpike(run, lastFullDay, found);
            DeadShop(run, lastFullDay, found);
            TariffSaturation(run, found);
            BountyMonoculture(run, lastFullDay, found);
        }

        return found
            .OrderByDescending(a => a.Severity)
            .ThenBy(a => a.Seed)
            .ThenBy(a => a.Rule, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Render the severity-ranked markdown report (empty corpus → honest empty report).</summary>
    public static string Render(IReadOnlyList<Anomaly> anomalies, int runCount)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Anomalies");
        sb.AppendLine();
        sb.AppendLine($"Runs analyzed: {runCount} | Hits: {anomalies.Count}");
        sb.AppendLine();
        if (anomalies.Count == 0)
        {
            sb.AppendLine("No anomalies detected.");
            return sb.ToString();
        }

        foreach (var a in anomalies)
        {
            sb.AppendLine($"## [{a.Severity.ToString().ToUpperInvariant()}] {a.Rule} — seed {a.Seed}, days {a.DayFrom}-{a.DayTo}");
            sb.AppendLine();
            sb.AppendLine(a.Detail);
            sb.AppendLine();
            sb.AppendLine($"Repro: `{a.ReproCommand}` then replay per docs/debugging.md §1.");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static void BeatStarvation(ChronicleData run, int lastFullDay, List<Anomaly> found)
    {
        if (lastFullDay < BeatStarvationMinDay || !run.Heroes.Any(h => h.Alive))
        {
            return;
        }

        var from = lastFullDay - TrailingWindowDays + 1;
        var beats = run.Events.OfType<AttributionBeatEvent>().Count(e => e.Day >= from && e.Day <= lastFullDay);
        if (beats == 0)
        {
            found.Add(new Anomaly(
                AnomalySeverity.High, "beat-starvation", run.Seed, from, lastFullDay,
                $"0 attribution beats in the trailing {TrailingWindowDays} days with living heroes — the attribution-pride loop is starving."));
        }
    }

    private static void DeathSpike(
        ChronicleData run, int runCount, Dictionary<int, int> corpusDeathsByFloor, List<Anomaly> found)
    {
        var byFloor = new Dictionary<int, List<HeroDied>>();
        foreach (var death in run.Events.OfType<HeroDied>())
        {
            (byFloor.TryGetValue(death.Floor, out var list) ? list : byFloor[death.Floor] = []).Add(death);
        }

        foreach (var (floor, deaths) in byFloor.OrderBy(kv => kv.Key))
        {
            // Baseline = the OTHER runs (excluding self, else a lone spike dilutes its own baseline):
            // run's count > factor × others' mean  ⇔  count × (runCount−1) > factor × othersTotal.
            // A single-run corpus has no baseline and stays silent by construction.
            var othersTotal = corpusDeathsByFloor.GetValueOrDefault(floor) - deaths.Count;
            if (deaths.Count >= DeathSpikeMinDeaths && deaths.Count * (runCount - 1) > DeathSpikeFactor * othersTotal)
            {
                found.Add(new Anomaly(
                    AnomalySeverity.Medium, "death-spike", run.Seed,
                    deaths.Min(d => d.Day), deaths.Max(d => d.Day),
                    $"floor {floor}: {deaths.Count} deaths vs {othersTotal} across the other {runCount - 1} run(s)."));
            }
        }
    }

    private static void GoldMintSpike(ChronicleData run, int lastFullDay, List<Anomaly> found)
    {
        if (lastFullDay < TrailingWindowDays * 2)
        {
            return; // need two disjoint full windows
        }

        var opening = MintedIn(run, 1, TrailingWindowDays);
        var trailingFrom = lastFullDay - TrailingWindowDays + 1;
        var trailing = MintedIn(run, trailingFrom, lastFullDay);
        if (trailing >= MintSpikeMinGold && trailing > MintSpikeFactor * Math.Max(1, opening))
        {
            found.Add(new Anomaly(
                AnomalySeverity.Medium, "gold-mint-spike", run.Seed, trailingFrom, lastFullDay,
                $"gold minted {trailing}g in trailing window vs {opening}g in days 1-{TrailingWindowDays} — inflation runaway."));
        }
    }

    private static int MintedIn(ChronicleData run, int from, int to) =>
        run.Events.OfType<LootIncomeReceived>().Where(e => e.Day >= from && e.Day <= to).Sum(e => e.Gold)
        + run.Events.OfType<BountyPaid>().Where(e => e.Day >= from && e.Day <= to).Sum(e => e.RewardGold);

    private static void DeadShop(ChronicleData run, int lastFullDay, List<Anomaly> found)
    {
        if (lastFullDay < DeadShopMinDay)
        {
            return;
        }

        var crafted = run.Events.OfType<ItemCrafted>().Count();
        var playerSales = run.Events.OfType<ItemSold>().Count(e => e.FromPlayerShop);
        if (crafted >= DeadShopMinCrafts && playerSales == 0)
        {
            found.Add(new Anomaly(
                AnomalySeverity.Medium, "dead-shop", run.Seed, 1, lastFullDay,
                $"{crafted} items crafted, 0 player-shop sales — crafting into the void."));
        }
    }

    private static void TariffSaturation(ChronicleData run, List<Anomaly> found)
    {
        var atCap = run.Events.OfType<TariffApplied>()
            .Where(t => t.BaseLineCost > 0
                        && Math.Abs(t.Delta) * 1000 >= t.BaseLineCost * TariffSaturationPerMille)
            .ToList();
        if (atCap.Count < TariffSaturationMinEvents)
        {
            return;
        }

        var from = atCap.Min(t => t.Day);
        var to = atCap.Max(t => t.Day);
        if (to - from + 1 >= TariffSaturationMinSpanDays)
        {
            found.Add(new Anomaly(
                AnomalySeverity.Low, "tariff-saturation", run.Seed, from, to,
                $"{atCap.Count} tariff applications at/near the cap over {to - from + 1} days — standing pegged; the lever stopped mattering."));
        }
    }

    private static void BountyMonoculture(ChronicleData run, int lastFullDay, List<Anomaly> found)
    {
        var judged = run.Events.OfType<BountyJudged>().ToList();
        if (judged.Count < MonocultureMinJudgments)
        {
            return;
        }

        var accepted = judged.Count(j => j.Accepted);
        var acceptedPerCent = accepted * 100 / judged.Count;
        if (acceptedPerCent >= MonoculturePerCentEither || acceptedPerCent <= 100 - MonoculturePerCentEither)
        {
            found.Add(new Anomaly(
                AnomalySeverity.Low, "bounty-monoculture", run.Seed, 1, lastFullDay,
                $"{judged.Count} bounty judgments, {acceptedPerCent}% accepted — the decision has degenerated to one direction."));
        }
    }
}
