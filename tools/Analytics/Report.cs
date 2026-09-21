using System.Collections.Immutable;
using System.Text;
using GameSim.Chronicle;
using GameSim.Classes;
using GameSim.Contracts;

namespace Analytics;

/// <summary>
/// Pure aggregation over exported chronicles (U14): the NPC-pattern report Brian feeds
/// back for tuning. No IO here — Program.cs owns files.
///
/// P2-HONEST-46: a batch export keeps simulating to <c>--days N</c> long after
/// <see cref="CampaignEnded"/> fires (median day 27 baseline, 31 <c>forgecounter</c>) — measured
/// on the standard corpus, 64-84% of a driven policy's own derived counts land in those dead days.
/// Every total below is therefore reported as TWO columns, pre-Ending and post-Ending, split at
/// each run's own Ending (the whole run counts as pre-Ending when it never reaches one) rather than
/// as one sum that silently blends "the campaign a hero could actually play" with the afterlife a
/// batch sweep never stops simulating.
/// </summary>
public static class Report
{
    public static string Build(IReadOnlyList<ChronicleData> runs)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Maker's Mark — chronicle report");
        sb.AppendLine();
        sb.AppendLine($"Runs: {runs.Count} | Total sim-days: {runs.Sum(r => r.Day - 1)}");
        sb.AppendLine();
        AppendHorizonLine(sb, runs);
        sb.AppendLine();

        // Hero class → its display name for the by-class death table. Resolving the class
        // definition keeps the report reading "Vanguard" (not the raw "vanguard" id); an
        // unregistered id falls back to the raw key so nothing is dropped.
        var roleByHero = new Dictionary<(ulong Seed, int Hero), string>();
        foreach (var run in runs)
        {
            foreach (var hero in run.Heroes)
            {
                // Legacy chronicles (pre-P3 exports) deserialize with a null ClassId — the report
                // tool must tolerate every chronicle ever written, never crash on one.
                roleByHero[(run.Seed, hero.Id.Value)] =
                    hero.ClassId is not null && ClassRegistry.TryGet(hero.ClassId, out var def)
                        ? def!.DisplayName
                        : hero.ClassId ?? "Unknown"; // matches the missing-hero fallback bucket below
            }
        }

        var pre = new Counters();
        var post = new Counters();
        foreach (var run in runs)
        {
            var horizon = Horizon(run);
            foreach (var gameEvent in run.Events)
            {
                var bucket = gameEvent.Day <= horizon ? pre : post;
                Tally(bucket, run, gameEvent, roleByHero);
            }
        }

        AppendColumn(sb, "Pre-Ending", "day ≤ each campaign's own Ending (the whole run, for one that never ends)", pre);
        AppendColumn(sb, "Post-Ending", "the afterlife — a batch sweep keeps simulating here, but no player ever does", post);

        return sb.ToString();
    }

    /// <summary>The day a chronicle's own Ending fell on, or the last full day of the run when it
    /// never reaches one — <see cref="ChronicleData.EndedOnDay"/> is the stamped source of truth
    /// (P2-HONEST-46), with a scan of <see cref="ChronicleData.Events"/> as a fallback for any
    /// chronicle written or hand-built before that field existed (this repo's own pre-existing test
    /// fixtures included).</summary>
    private static int Horizon(ChronicleData run)
    {
        var lastFullDay = Math.Max(0, run.Day - 1);
        var ending = run.EndedOnDay ?? run.Events.OfType<CampaignEnded>().FirstOrDefault()?.Day;
        return ending is int day ? Math.Min(lastFullDay, day) : lastFullDay;
    }

    private static void AppendHorizonLine(StringBuilder sb, IReadOnlyList<ChronicleData> runs)
    {
        var ended = runs.Where(r => r.EndedOnDay is not null || r.Events.OfType<CampaignEnded>().Any()).ToList();
        var neverEnded = runs.Except(ended).Select(r => r.Seed).ToList();

        sb.Append($"Ended: {ended.Count}/{runs.Count} seed(s)");
        if (ended.Count > 0)
        {
            var days = ended.Select(Horizon).OrderBy(d => d).ToList();
            sb.Append($" (day {days[0]}-{days[^1]})");
        }

        sb.AppendLine(neverEnded.Count == 0
            ? " | every seed ended within the run."
            : $" | never ended (counted whole, not silently): {string.Join(", ", neverEnded)}");
    }

    /// <summary>Per-column running totals (P2-HONEST-46) — one instance for events at or before a
    /// run's own Ending, one for events after it.</summary>
    private sealed class Counters
    {
        public readonly SortedDictionary<int, int> DeathsByFloor = new();
        public readonly SortedDictionary<string, int> DeathsByRole = new(StringComparer.Ordinal);
        public readonly SortedDictionary<string, int> BeatsByType = new(StringComparer.Ordinal);
        public readonly Dictionary<string, int> PassReasons = new(StringComparer.Ordinal);
        public int PlayerSales;
        public int RivalSales;
        public int PlayerRevenue;
        public int LootGold;
        public int GossipCount;
        public int BountyAccepts;
        public int BountyDeclines;
        public int Crafts;
        public int PartyNights;
        public int Fulfilments;
    }

    private static void Tally(
        Counters bucket, ChronicleData run, GameEvent gameEvent,
        IReadOnlyDictionary<(ulong Seed, int Hero), string> roleByHero)
    {
        switch (gameEvent)
        {
            case HeroDied died:
                bucket.DeathsByFloor[died.Floor] = bucket.DeathsByFloor.GetValueOrDefault(died.Floor) + 1;
                var role = roleByHero.TryGetValue((run.Seed, died.Hero.Value), out var r) ? r : "Unknown";
                bucket.DeathsByRole[role] = bucket.DeathsByRole.GetValueOrDefault(role) + 1;
                break;
            case AttributionBeatEvent beat:
                bucket.BeatsByType[beat.Beat.ToString()] = bucket.BeatsByType.GetValueOrDefault(beat.Beat.ToString()) + 1;
                break;
            case HeroPassedOnItem pass:
                // Bucket by the reason's shape, not its specifics (names/numbers vary).
                var key = Bucket(pass.Reason);
                bucket.PassReasons[key] = bucket.PassReasons.GetValueOrDefault(key) + 1;
                break;
            case ItemSold sold:
                if (sold.FromPlayerShop)
                {
                    bucket.PlayerSales++;
                    bucket.PlayerRevenue += sold.Price;
                }
                else
                {
                    bucket.RivalSales++;
                }

                break;
            case LootIncomeReceived income:
                bucket.LootGold += income.Gold;
                break;
            case GossipEmitted:
                bucket.GossipCount++;
                break;
            case BountyJudged judged:
                if (judged.Accepted)
                {
                    bucket.BountyAccepts++;
                }
                else
                {
                    bucket.BountyDeclines++;
                }

                break;
            case ItemCrafted:
                bucket.Crafts++;
                break;
            case PartyReturned:
                bucket.PartyNights++;
                break;
            case CommissionFulfilled:
                bucket.Fulfilments++;
                break;
        }
    }

    private static void AppendColumn(StringBuilder sb, string title, string subtitle, Counters b)
    {
        sb.AppendLine($"## {title}");
        sb.AppendLine();
        sb.AppendLine($"_{subtitle}_");
        sb.AppendLine();

        sb.AppendLine("### Deaths");
        sb.AppendLine();
        sb.AppendLine("| Floor | Deaths |  | Role | Deaths |");
        sb.AppendLine("|---|---|---|---|---|");
        var roleRows = b.DeathsByRole.ToList();
        var floorRows = b.DeathsByFloor.ToList();
        for (var i = 0; i < Math.Max(floorRows.Count, roleRows.Count); i++)
        {
            var f = i < floorRows.Count ? $"{floorRows[i].Key} | {floorRows[i].Value}" : " | ";
            var g = i < roleRows.Count ? $"{roleRows[i].Key} | {roleRows[i].Value}" : " | ";
            sb.AppendLine($"| {f} |  | {g} |");
        }

        sb.AppendLine();
        sb.AppendLine("### Attribution beats");
        sb.AppendLine();
        foreach (var (type, count) in b.BeatsByType)
        {
            sb.AppendLine($"- {type}: {count}");
        }

        sb.AppendLine();
        sb.AppendLine("### Crafts, party-nights & commissions");
        sb.AppendLine();
        sb.AppendLine($"- Crafts: {b.Crafts}");
        sb.AppendLine($"- Party-nights: {b.PartyNights}");
        sb.AppendLine($"- Commissions fulfilled: {b.Fulfilments}");
        sb.AppendLine();
        sb.AppendLine("### Economy");
        sb.AppendLine();
        sb.AppendLine($"- Player sales: {b.PlayerSales} ({b.PlayerRevenue}g revenue) | Rival sales: {b.RivalSales}");
        sb.AppendLine($"- Hero loot income: {b.LootGold}g total");
        sb.AppendLine($"- Bounties: {b.BountyAccepts} accepted / {b.BountyDeclines} declined");
        sb.AppendLine($"- Gossip lines: {b.GossipCount}");
        sb.AppendLine();
        sb.AppendLine("### Shopping pass reasons (bucketed)");
        sb.AppendLine();
        foreach (var (reason, count) in b.PassReasons.OrderByDescending(kv => kv.Value).Take(10))
        {
            sb.AppendLine($"- {count}× {reason}");
        }

        sb.AppendLine();
    }

    /// <summary>Collapse specific reasons ("has 30g", item names) into stable buckets.</summary>
    public static string Bucket(string reason)
    {
        if (reason.Contains("afford", StringComparison.OrdinalIgnoreCase))
        {
            return "can't afford";
        }

        if (reason.Contains("too heavy", StringComparison.OrdinalIgnoreCase))
        {
            return "too heavy for role";
        }

        if (reason.Contains("shield", StringComparison.OrdinalIgnoreCase))
        {
            return "role doesn't use shields";
        }

        if (reason.Contains("better", StringComparison.OrdinalIgnoreCase))
        {
            return "current gear is better";
        }

        return "other";
    }
}
