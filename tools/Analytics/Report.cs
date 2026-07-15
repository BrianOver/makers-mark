using System.Collections.Immutable;
using System.Text;
using GameSim.Chronicle;
using GameSim.Contracts;

namespace Analytics;

/// <summary>
/// Pure aggregation over exported chronicles (U14): the NPC-pattern report Brian feeds
/// back for tuning. No IO here — Program.cs owns files.
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

        var roleByHero = new Dictionary<(ulong Seed, int Hero), HeroRole>();
        foreach (var run in runs)
        {
            foreach (var hero in run.Heroes)
            {
                roleByHero[(run.Seed, hero.Id.Value)] = hero.Role;
            }
        }

        var deathsByFloor = new SortedDictionary<int, int>();
        var deathsByRole = new SortedDictionary<string, int>();
        var beatsByType = new SortedDictionary<string, int>();
        var passReasons = new Dictionary<string, int>(StringComparer.Ordinal);
        var playerSales = 0;
        var rivalSales = 0;
        var playerRevenue = 0;
        var lootGold = 0;
        var gossipCount = 0;
        var bountyAccepts = 0;
        var bountyDeclines = 0;

        foreach (var run in runs)
        {
            foreach (var gameEvent in run.Events)
            {
                switch (gameEvent)
                {
                    case HeroDied died:
                        deathsByFloor[died.Floor] = deathsByFloor.GetValueOrDefault(died.Floor) + 1;
                        var role = roleByHero.TryGetValue((run.Seed, died.Hero.Value), out var r) ? r.ToString() : "Unknown";
                        deathsByRole[role] = deathsByRole.GetValueOrDefault(role) + 1;
                        break;
                    case AttributionBeatEvent beat:
                        beatsByType[beat.Beat.ToString()] = beatsByType.GetValueOrDefault(beat.Beat.ToString()) + 1;
                        break;
                    case HeroPassedOnItem pass:
                        // Bucket by the reason's shape, not its specifics (names/numbers vary).
                        var key = Bucket(pass.Reason);
                        passReasons[key] = passReasons.GetValueOrDefault(key) + 1;
                        break;
                    case ItemSold sold:
                        if (sold.FromPlayerShop)
                        {
                            playerSales++;
                            playerRevenue += sold.Price;
                        }
                        else
                        {
                            rivalSales++;
                        }

                        break;
                    case LootIncomeReceived income:
                        lootGold += income.Gold;
                        break;
                    case GossipEmitted:
                        gossipCount++;
                        break;
                    case BountyJudged judged:
                        if (judged.Accepted)
                        {
                            bountyAccepts++;
                        }
                        else
                        {
                            bountyDeclines++;
                        }

                        break;
                }
            }
        }

        var totalDays = Math.Max(1, runs.Sum(r => r.Day - 1));
        var totalBeats = beatsByType.Values.Sum();

        sb.AppendLine("## Deaths");
        sb.AppendLine();
        sb.AppendLine("| Floor | Deaths |  | Role | Deaths |");
        sb.AppendLine("|---|---|---|---|---|");
        var roleRows = deathsByRole.ToList();
        var floorRows = deathsByFloor.ToList();
        for (var i = 0; i < Math.Max(floorRows.Count, roleRows.Count); i++)
        {
            var f = i < floorRows.Count ? $"{floorRows[i].Key} | {floorRows[i].Value}" : " | ";
            var g = i < roleRows.Count ? $"{roleRows[i].Key} | {roleRows[i].Value}" : " | ";
            sb.AppendLine($"| {f} |  | {g} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Attribution beats");
        sb.AppendLine();
        foreach (var (type, count) in beatsByType)
        {
            sb.AppendLine($"- {type}: {count}");
        }

        sb.AppendLine($"- Rate: {(double)totalBeats * 100 / totalDays / 100.0:0.##} per sim-day");
        sb.AppendLine();
        sb.AppendLine("## Economy");
        sb.AppendLine();
        sb.AppendLine($"- Player sales: {playerSales} ({playerRevenue}g revenue) | Rival sales: {rivalSales}");
        sb.AppendLine($"- Hero loot income: {lootGold}g total ({lootGold / totalDays}g/day)");
        sb.AppendLine($"- Bounties: {bountyAccepts} accepted / {bountyDeclines} declined");
        sb.AppendLine($"- Gossip lines: {gossipCount}");
        sb.AppendLine();
        sb.AppendLine("## Shopping pass reasons (bucketed)");
        sb.AppendLine();
        foreach (var (reason, count) in passReasons.OrderByDescending(kv => kv.Value).Take(10))
        {
            sb.AppendLine($"- {count}× {reason}");
        }

        return sb.ToString();
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
