using System.Collections.Immutable;
using System.Text;
using GameSim;
using GameSim.Advisor;
using GameSim.Contracts;
using GameSim.Harness;

namespace GameSim.Cli;

/// <summary>
/// Decision-surface logger — a documentation tool, not a balance gate. For each seed it ticks a full
/// campaign under <see cref="BaselinePlayer"/> and, at EVERY phase (the exact decision points a human
/// player faces), records the three things that define the choice the game offers:
/// <list type="bullet">
///   <item>the LEGAL option menu (<see cref="ActionLegality.LegalActions"/>) — everything the player COULD do;</item>
///   <item>the ADVISOR's ranked suggestions + reasons (<see cref="ObjectiveAdvisor.Suggest"/>) — what the game recommends;</item>
///   <item>the action the default policy actually CHOSE (<see cref="BaselinePlayer.ActionsFor"/>) — a sample decision.</item>
/// </list>
/// Output is two artifacts under <c>&lt;outDir&gt;</c>: a per-seed <c>decisions-seed{n}.jsonl</c> (one line
/// per tick, machine-readable for downstream analysis) and a cross-seed <c>decision-surface-summary.md</c>
/// that aggregates option frequency by phase, advisor-reason distribution, chosen-action distribution,
/// arc progression, and — most useful for design review — which of the 25 player verbs NEVER surface as
/// legal / advised / chosen (dead options). Pure read of sim state; the tick itself is the only mutation.
/// </summary>
public static class DecisionLogger
{
    /// <summary>The verb key for a player action — its type name minus the "Action" suffix
    /// (e.g. <c>CraftAction</c> → <c>Craft</c>). Stable, human-readable, and aggregation-friendly.</summary>
    private static string Verb(PlayerAction action) => action.GetType().Name.Replace("Action", string.Empty);

    /// <summary>Every player-verb the game defines, so the summary can name the ones that NEVER appear
    /// (a dead option is invisible to frequency counts — it has to be enumerated to be missed).</summary>
    private static readonly ImmutableArray<string> AllVerbs = ImmutableArray.Create(
        "AcceptCommission", "BuyForgeSupply", "BuyMaterial", "BuyOre", "CloseCounter",
        "CommissionLegendaryWork", "Craft", "DeclineCommission", "HaggleResponse", "HonorMemorial",
        "MasterworkAttempt", "OpenCounter", "PostBounty", "PresentItem", "RecallParty",
        "ReforgeHeirloom", "SendSupply", "SetPrice", "SetProfessions", "Stock",
        "SuggestItem", "UnlockTalent", "UnstockItem", "Unstock", "UpgradeForge");

    public static int Run(int seedCount, ulong startSeed, int days, string outDir, TextWriter output, TextWriter error)
    {
        try
        {
            Directory.CreateDirectory(outDir);
        }
        catch (Exception ex)
        {
            error.WriteLine($"decisions: cannot create '{outDir}': {ex.Message}");
            return 1;
        }

        var kernel = GameComposition.BuildKernel();

        // Cross-seed aggregates.
        var legalByPhase = new Dictionary<DayPhase, Dictionary<string, long>>();
        var chosenByPhase = new Dictionary<DayPhase, Dictionary<string, long>>();
        var optionCountByPhase = new Dictionary<DayPhase, (long Sum, long N, int Min, int Max)>();
        var advisorReasons = new Dictionary<string, long>();
        var everLegal = new HashSet<string>();
        var everAdvised = new HashSet<string>();
        var everChosen = new HashSet<string>();
        var endings = new List<string>();
        long totalTicks = 0;

        for (var i = 0; i < seedCount; i++)
        {
            var seed = startSeed + (ulong)i;
            var state = GameComposition.NewCampaign(seed);
            var jsonl = new StringBuilder();

            while (state.Day <= days)
            {
                var phase = state.Phase;
                var legal = ActionLegality.LegalActions(state, phase);
                var advice = ObjectiveAdvisor.Suggest(state);
                var chosen = BaselinePlayer.ActionsFor(state);
                totalTicks++;

                // --- aggregate: legal option menu ---
                if (!legalByPhase.TryGetValue(phase, out var legalCounts))
                {
                    legalByPhase[phase] = legalCounts = new Dictionary<string, long>();
                }

                foreach (var a in legal)
                {
                    var v = Verb(a);
                    everLegal.Add(v);
                    legalCounts[v] = legalCounts.GetValueOrDefault(v) + 1;
                }

                var optCount = legal.Count;
                var agg = optionCountByPhase.GetValueOrDefault(phase, (0, 0, int.MaxValue, 0));
                optionCountByPhase[phase] = (agg.Sum + optCount, agg.N + 1,
                    Math.Min(agg.Min, optCount), Math.Max(agg.Max, optCount));

                // --- aggregate: advisor suggestions ---
                foreach (var s in advice)
                {
                    advisorReasons[s.Reason] = advisorReasons.GetValueOrDefault(s.Reason) + 1;
                    if (s.Action is { } sa)
                    {
                        everAdvised.Add(Verb(sa));
                    }
                }

                // --- aggregate: chosen actions ---
                if (!chosenByPhase.TryGetValue(phase, out var chosenCounts))
                {
                    chosenByPhase[phase] = chosenCounts = new Dictionary<string, long>();
                }

                foreach (var a in chosen)
                {
                    var v = Verb(a);
                    everChosen.Add(v);
                    chosenCounts[v] = chosenCounts.GetValueOrDefault(v) + 1;
                }

                // --- per-tick JSONL row ---
                jsonl.Append("{\"seed\":").Append(seed)
                    .Append(",\"day\":").Append(state.Day)
                    .Append(",\"phase\":\"").Append(phase).Append('"')
                    .Append(",\"act\":\"").Append(state.Arc.Act).Append('"')
                    .Append(",\"legal\":[").Append(JsonVerbList(legal))
                    .Append("],\"advice\":[").Append(JsonAdvice(advice))
                    .Append("],\"chosen\":[").Append(JsonVerbList(chosen))
                    .Append("]}\n");

                state = kernel.Tick(state, chosen).NewState;
            }

            var path = Path.Combine(outDir, $"decisions-seed{seed}-days{days}.jsonl");
            try
            {
                File.WriteAllText(path, jsonl.ToString());
            }
            catch (Exception ex)
            {
                error.WriteLine($"decisions: write failed for '{path}': {ex.Message}");
                return 1;
            }

            var alive = state.Heroes.Values.Count(h => h.Alive);
            var died = state.Heroes.Values.Count(h => !h.Alive);
            endings.Add($"| {seed} | {state.Arc.Act} | {(state.Arc.EndingDay > 0 ? state.Arc.EndingDay.ToString() : "—")} "
                + $"| {state.Player.Gold} | {alive} | {died} | {state.Heroes.Count} |");
            output.WriteLine($"  seed {seed}: {days} days, {state.Heroes.Count} heroes ({alive} alive), arc {state.Arc.Act} -> {path}");
        }

        var summaryPath = Path.Combine(outDir, "decision-surface-summary.md");
        try
        {
            File.WriteAllText(summaryPath, BuildSummary(
                seedCount, startSeed, days, totalTicks, legalByPhase, chosenByPhase,
                optionCountByPhase, advisorReasons, everLegal, everAdvised, everChosen, endings));
        }
        catch (Exception ex)
        {
            error.WriteLine($"decisions: summary write failed: {ex.Message}");
            return 1;
        }

        output.WriteLine($"decision surface: {seedCount} seed(s), {totalTicks} decision points -> {summaryPath}");
        return 0;
    }

    private static string JsonVerbList(ImmutableList<PlayerAction> actions) =>
        string.Join(",", actions.Select(a => $"\"{Verb(a)}\"").Distinct());

    private static string JsonAdvice(ImmutableList<Suggestion> advice) =>
        string.Join(",", advice.Select(s =>
            $"{{\"v\":\"{(s.Action is { } a ? Verb(a) : "-")}\",\"why\":\"{Escape(s.Reason)}\"}}"));

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string BuildSummary(
        int seedCount, ulong startSeed, int days, long totalTicks,
        Dictionary<DayPhase, Dictionary<string, long>> legalByPhase,
        Dictionary<DayPhase, Dictionary<string, long>> chosenByPhase,
        Dictionary<DayPhase, (long Sum, long N, int Min, int Max)> optionCountByPhase,
        Dictionary<string, long> advisorReasons,
        HashSet<string> everLegal, HashSet<string> everAdvised, HashSet<string> everChosen,
        List<string> endings)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Maker's Mark — Decision-Surface Report");
        sb.AppendLine();
        sb.AppendLine($"Auto-generated by `GameSim.Cli decisions`. **{seedCount} seed(s)** "
            + $"(start {startSeed}) × **{days} days** = **{totalTicks} decision points** logged, "
            + "under the default `BaselinePlayer` policy. Each decision point records the legal option "
            + "menu, the advisor's ranked suggestions, and the action chosen. Per-seed tick logs: "
            + "`decisions-seed*.jsonl`.");
        sb.AppendLine();

        // Phase-ordered for readability.
        var phaseOrder = new[] { DayPhase.Morning, DayPhase.Expedition, DayPhase.Camp, DayPhase.ExpeditionDeep, DayPhase.Evening };

        sb.AppendLine("## Option count per phase");
        sb.AppendLine();
        sb.AppendLine("How wide the choice is at each phase (distinct legal verbs offered).");
        sb.AppendLine();
        sb.AppendLine("| Phase | avg options | min | max |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var phase in phaseOrder)
        {
            if (optionCountByPhase.TryGetValue(phase, out var a) && a.N > 0)
            {
                sb.AppendLine($"| {phase} | {(double)a.Sum / a.N:F1} | {a.Min} | {a.Max} |");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Legal options by phase (frequency)");
        sb.AppendLine();
        sb.AppendLine("Which verbs are actually offered in each phase, and how often across all decision points.");
        foreach (var phase in phaseOrder)
        {
            if (!legalByPhase.TryGetValue(phase, out var counts) || counts.Count == 0)
            {
                continue;
            }

            sb.AppendLine();
            sb.AppendLine($"### {phase}");
            sb.AppendLine();
            sb.AppendLine("| verb | times legal |");
            sb.AppendLine("|---|---|");
            foreach (var (verb, count) in counts.OrderByDescending(kv => kv.Value))
            {
                sb.AppendLine($"| {verb} | {count} |");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## What the default policy chose (by phase)");
        sb.AppendLine();
        sb.AppendLine("The sample decision at each point — what `BaselinePlayer` actually does. Reveals the "
            + "*played* loop vs. the *offered* loop above.");
        foreach (var phase in phaseOrder)
        {
            if (!chosenByPhase.TryGetValue(phase, out var counts) || counts.Count == 0)
            {
                continue;
            }

            sb.AppendLine();
            sb.AppendLine($"### {phase}");
            sb.AppendLine();
            sb.AppendLine("| verb | times chosen |");
            sb.AppendLine("|---|---|");
            foreach (var (verb, count) in counts.OrderByDescending(kv => kv.Value))
            {
                sb.AppendLine($"| {verb} | {count} |");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Advisor suggestions (reason frequency)");
        sb.AppendLine();
        sb.AppendLine("Every distinct reason the `ObjectiveAdvisor` surfaced, most frequent first — the "
            + "game's own sense of \"what should I do next,\" and how repetitive that guidance gets.");
        sb.AppendLine();
        sb.AppendLine("| times shown | advisor reason |");
        sb.AppendLine("|---|---|");
        foreach (var (reason, count) in advisorReasons.OrderByDescending(kv => kv.Value).Take(40))
        {
            sb.AppendLine($"| {count} | {reason.Replace("|", "\\|")} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Verb coverage — the dead options");
        sb.AppendLine();
        sb.AppendLine("Every player verb the game defines, and whether it EVER surfaced as legal / advised / "
            + "chosen across the whole sweep. A verb that is defined but never legal (or never advised, or "
            + "never chosen by the default policy) is a candidate dead option — built but not reached.");
        sb.AppendLine();
        sb.AppendLine("| verb | ever legal | ever advised | ever chosen |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var verb in AllVerbs.Distinct().OrderBy(v => v))
        {
            sb.AppendLine($"| {verb} | {Mark(everLegal.Contains(verb))} | {Mark(everAdvised.Contains(verb))} | {Mark(everChosen.Contains(verb))} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Arc / campaign outcome per seed");
        sb.AppendLine();
        sb.AppendLine("| seed | final act | ending day | gold | heroes alive | heroes died | roster ever |");
        sb.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var row in endings)
        {
            sb.AppendLine(row);
        }

        sb.AppendLine();
        sb.AppendLine("*Note: choices are the deterministic `BaselinePlayer` policy, not a human — the value "
            + "here is the OPTION SURFACE (what the game offers + advises at each point), which is "
            + "policy-independent, plus one concrete sample of how a default agent plays it.*");
        return sb.ToString();
    }

    private static string Mark(bool b) => b ? "yes" : "**NO**";
}
