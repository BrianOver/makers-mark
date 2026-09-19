#if GDUNIT_TESTS
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using GameSim;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Harness;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// P2-LONG-34 (MAKERS-MARK.md §11.13's felt-wall paragraph). The earlier masked verbatim-repeat
/// census (§11.13's own throwaway driver, never committed) measured lines the SIM decided, never
/// lines the PLAYER actually saw on screen — and could not see what P2-PROOF-20/21/22 changed at
/// all, because those rewrote how the Evening Ledger RENDERS a beat (<c>TellingPanel</c> headlines,
/// lead-beat reordering), not what the sim recorded. This moves the same measurement onto the real
/// render path: <see cref="GodotClient.Panels.LedgerModal"/> mounted for real via
/// <see cref="MountMainUi"/>, <see cref="GodotClient.Panels.LedgerModal.ShowFor"/> called for real,
/// every <see cref="Label"/> the card tree actually put on screen read back — the same
/// <c>BeatLinesOf</c>-style enumeration <see cref="LedgerModalTests"/> already uses for one row
/// family, generalized here to every label under a card.
///
/// <para><b>Driving the campaign.</b> 25 real days through <see cref="HumanPlayer"/> button clicks
/// would cost minutes per seed — <see cref="HumanPlayer"/>'s whole point is honest INPUT mechanics,
/// not economical day-advance, and <see cref="GodotClient.Tools.FullPlaytest"/> drives only 8 days
/// per run for exactly that cost reason. This instead reuses the exact headless drive
/// <c>GameSim.Cli.FeltWallSweep</c> (P2-LONG-26) already runs — <see cref="GameComposition.BuildKernel"/>
/// plus <see cref="BaselinePlayer"/> — pure sim, zero Godot, already proven deterministic to 100 days.
/// Only the RENDER step needs the engine, so only the render step pays its cost.</para>
///
/// <para><b>One snapshot per census day, not one final state read three times.</b>
/// <see cref="LedgerQuery.ReturnCards"/> reads a day's EVENTS off the append-only log (stable at any
/// later point — <see cref="GodotClient.Panels.LedgerModal"/>'s own doc says so), but a card's
/// purse/gold-on-hand chip reads the hero's CURRENT, query-time gold. Re-querying one day-25 state
/// for days 3 and 12 would silently show day 3's card with day 25's ending purse — masking digits
/// erases that from the comparison either way, but capturing the true state as it stood at the end
/// of each census day is what a player actually saw, and costs nothing extra (immutable records).</para>
///
/// <para><b>Masking</b> mirrors §11.13's own rule: digits and hero/item names are replaced with a
/// mask token before the verbatim-repeat comparison, so a level-up numeral or a freshly recruited
/// hero's name being "new" every single time can't make the census measure the roster/economy
/// turning over instead of the PROSE repeating. Hero names come from <see cref="GameState.Heroes"/>,
/// item names from <see cref="GameState.Items"/>, unioned across all three of one seed's snapshots
/// so a name that first exists on day 12 is still masked in day 25's text.</para>
///
/// <para><b>The definition is literal.</b> A line on day D counts as a repeat only if its masked
/// text appeared on any EARLIER day THIS CENSUS READ — not any earlier day in the campaign. Day 3 is
/// the first day this instrument reads, so day 3's share is 0 for every seed by construction; that
/// is the definition working as stated, not a bug.</para>
///
/// <para><b>Not a gate.</b> Like <c>GameSim.Cli.FeltWallSweep</c>, this produces a measurement and
/// prints a table. The test itself asserts only structural properties (lines were actually read,
/// every share sits in [0,1], the pipeline is deterministic) — no verbatim-repeat BAND exists yet,
/// and booking one is the owner's call (§11.13's own text says so).</para>
///
/// <para><b>Why this is not excluded from the default engine run.</b> This repo has no gdUnit
/// Category/Trait exclusion convention to mirror: every existing slow census in this folder
/// (<see cref="AssetResolutionCensusTests"/>, <see cref="WholeGameSweepTests"/>, the sibling
/// <c>*CensusTests</c> files) runs inside the same unfiltered <c>dotnet test godot/tests</c> CI
/// already runs, because CI's engine-tests step passes no <c>--filter</c> at all for that project
/// (<c>.github/workflows/ci.yml</c>) and <c>.runsettings</c> carries no test-case filter either —
/// both are files this unit may not edit regardless. <c>SeedCount</c> below is kept small enough
/// that the whole class stays well inside the existing suite's budget (measured at the PR), rather
/// than inventing an exclusion path that would need a change to a deny-listed file to take effect.
/// To run only this class: <c>dotnet test godot/tests --settings .runsettings --filter
/// "FullyQualifiedName~FeltWallCensusTests"</c>.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class FeltWallCensusTests
{
    /// <summary>Kept small so the whole class stays well under the ~2-minute budget the unit set for
    /// itself — see the measured runtime quoted in the PR body.</summary>
    private const int SeedCount = 5;

    private const ulong StartSeed = 5001;

    private static readonly int[] CensusDays = { 3, 12, 25 };

    private static readonly Regex CardNamePattern = new(@"^LedgerCard_\d+$", RegexOptions.Compiled);
    private static readonly Regex DigitsPattern = new(@"\d+", RegexOptions.Compiled);

    private const string MaskToken = "‹#›";
    private const string NameMaskToken = "‹NAME›";

    /// <summary>One rendered line, its card-relative kind, and the day it was read on.</summary>
    private readonly record struct RenderedLine(string Kind, string MaskedText);

    /// <summary>Per-day tally for one seed: how many lines were read, how many of those were a
    /// verbatim (masked) repeat of a line read on an EARLIER census day for this same seed, and the
    /// same split by kind.</summary>
    private sealed record DayTally(int TotalLines, int RepeatLines, ImmutableSortedDictionary<string, (int Total, int Repeat)> ByKind)
    {
        public double Share => TotalLines == 0 ? 0.0 : (double)RepeatLines / TotalLines;
    }

    private sealed record SeedResult(ulong Seed, ImmutableSortedDictionary<int, DayTally> Days);

    [TestCase]
    public void MaskedVerbatimRepeatShare_OnDays3_12_25_IsBoundedAndDeterministic()
    {
        var results = RunSeeds(SeedCount);
        AssertThat(results.Count).IsEqual(SeedCount);

        foreach (var result in results)
        {
            foreach (var day in CensusDays)
            {
                var tally = result.Days[day];
                AssertThat(tally.TotalLines)
                    .OverrideFailureMessage($"seed {result.Seed} day {day}: the mounted Ledger rendered zero lines — the census read nothing")
                    .IsGreater(0);
                AssertThat(tally.Share)
                    .OverrideFailureMessage($"seed {result.Seed} day {day}: verbatim-repeat share {tally.Share} is outside [0,1]")
                    .IsBetween(0.0, 1.0);
            }

            AssertThat(result.Days[3].Share)
                .OverrideFailureMessage("day 3 is this census's FIRST read — its share is 0 by the unit's own definition ('any earlier day the census read'), not a measurement of day 3 itself")
                .IsEqual(0.0);
        }

        // Determinism spot-check (rule 5): the full pipeline (headless sim drive + real render +
        // mask + count) for ONE seed, run twice, must produce byte-identical output. One seed is
        // enough to trip a real nondeterminism (an RNG leak, a Dictionary-ordered string, a live
        // clock read reaching render text) — re-running all N would double this class's runtime for
        // no extra coverage of that property.
        var repeat = RunSeeds(1, results[0].Seed).Single();
        AssertThat(FormatSeedRow(repeat)).IsEqual(FormatSeedRow(results[0]));

        var table = FormatTable(results);
        Console.WriteLine(table);
        GD.Print(table);
    }

    /// <summary>Runs the full pipeline for <paramref name="count"/> seeds starting at
    /// <paramref name="startSeed"/> (defaults to <see cref="StartSeed"/>).</summary>
    private static List<SeedResult> RunSeeds(int count, ulong? startSeed = null)
    {
        var start = startSeed ?? StartSeed;
        var results = new List<SeedResult>(count);
        for (var i = 0; i < count; i++)
        {
            results.Add(RunOneSeed(start + (ulong)i));
        }

        return results;
    }

    private static SeedResult RunOneSeed(ulong seed)
    {
        var snapshots = BuildDaySnapshots(seed);

        var nameMask = new HashSet<string>(StringComparer.Ordinal);
        foreach (var state in snapshots.Values)
        {
            foreach (var hero in state.Heroes.Values)
            {
                if (!string.IsNullOrEmpty(hero.Name))
                {
                    nameMask.Add(hero.Name);
                }
            }

            foreach (var item in state.Items.Values)
            {
                if (!string.IsNullOrEmpty(item.Name))
                {
                    nameMask.Add(item.Name);
                }
            }
        }

        var everSeen = new HashSet<string>(StringComparer.Ordinal);
        var everSeenByKind = new Dictionary<string, HashSet<string>>();
        var days = ImmutableSortedDictionary.CreateBuilder<int, DayTally>();

        foreach (var day in CensusDays)
        {
            var lines = RenderDay(snapshots[day], day, nameMask);

            var total = lines.Count;
            var repeat = 0;
            var byKindTotal = new Dictionary<string, int>();
            var byKindRepeat = new Dictionary<string, int>();

            foreach (var line in lines)
            {
                byKindTotal[line.Kind] = byKindTotal.GetValueOrDefault(line.Kind) + 1;
                var seenBefore = everSeen.Contains(line.MaskedText);
                if (seenBefore)
                {
                    repeat++;
                    byKindRepeat[line.Kind] = byKindRepeat.GetValueOrDefault(line.Kind) + 1;
                }
            }

            var byKind = ImmutableSortedDictionary.CreateBuilder<string, (int Total, int Repeat)>();
            foreach (var kind in byKindTotal.Keys)
            {
                byKind[kind] = (byKindTotal[kind], byKindRepeat.GetValueOrDefault(kind));
            }

            days[day] = new DayTally(total, repeat, byKind.ToImmutable());

            // Only AFTER scoring this day against everything read so far does this day's own lines
            // join the read set — so day D never counts as a repeat of itself, and a later day
            // correctly sees D as an earlier read.
            foreach (var line in lines)
            {
                everSeen.Add(line.MaskedText);
                if (!everSeenByKind.TryGetValue(line.Kind, out var set))
                {
                    set = new HashSet<string>(StringComparer.Ordinal);
                    everSeenByKind[line.Kind] = set;
                }

                set.Add(line.MaskedText);
            }
        }

        return new SeedResult(seed, days.ToImmutable());
    }

    /// <summary>Drives one campaign headlessly (no Godot) to day 25 via the same
    /// <see cref="GameComposition.BuildKernel"/> + <see cref="BaselinePlayer"/> machinery
    /// <c>GameSim.Cli.FeltWallSweep</c> (P2-LONG-26) already uses, capturing the state the instant
    /// each of <see cref="CensusDays"/> finishes resolving.</summary>
    private static ImmutableSortedDictionary<int, GameState> BuildDaySnapshots(ulong seed)
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(seed);
        var wanted = new HashSet<int>(CensusDays);
        var maxDay = CensusDays[^1];
        var snapshots = ImmutableSortedDictionary.CreateBuilder<int, GameState>();

        var previousDay = state.Day;
        while (state.Day <= maxDay)
        {
            var chosen = BaselinePlayer.ActionsFor(state);
            var result = kernel.Tick(state, chosen);
            state = result.NewState;

            if (state.Day != previousDay)
            {
                if (wanted.Contains(previousDay))
                {
                    snapshots[previousDay] = state;
                }

                previousDay = state.Day;
            }
        }

        return snapshots.ToImmutable();
    }

    /// <summary>Mounts the real client for <paramref name="state"/>, opens the real Ledger for
    /// <paramref name="day"/>, and reads back every <see cref="Label"/> the card tree put on
    /// screen — per-hero card content (headline beat / other detail / fate status) plus the
    /// once-per-night shared lines (narrator line, followed-item line, gate-streak lines, tutorial
    /// tip) that render outside any one hero's card.</summary>
    private static List<RenderedLine> RenderDay(GameState state, int day, HashSet<string> nameMask)
    {
        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            ui.Ledger.ShowFor(day);

            var lines = new List<RenderedLine>();
            var cards = LedgerQuery.ReturnCards(state, day);
            for (var i = 0; i < cards.Count; i++)
            {
                var cardNode = Find<Control>(ui.Ledger, $"LedgerCard_{i}");
                var labels = new List<(string Name, string Text)>();
                CollectLabels(cardNode, labels);
                foreach (var (name, text) in labels)
                {
                    foreach (var rawLine in SplitLines(text))
                    {
                        lines.Add(new RenderedLine(CardLineKind(name), Mask(rawLine, nameMask)));
                    }
                }
            }

            var sharedRoot = Find<Control>(ui.Ledger, "LedgerCards");
            var sharedLabels = new List<(string Name, string Text)>();
            CollectLabelsExcludingCards(sharedRoot, sharedLabels);
            foreach (var (_, text) in sharedLabels)
            {
                foreach (var rawLine in SplitLines(text))
                {
                    lines.Add(new RenderedLine("shared", Mask(rawLine, nameMask)));
                }
            }

            return lines;
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Every non-empty, trimmed sub-line of a Label's rendered text — most labels are one
    /// logical line, but a few (e.g. multiple warrant saves) join several with an embedded
    /// newline.</summary>
    private static IEnumerable<string> SplitLines(string text) =>
        text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0);

    /// <summary>Best-effort classification off <see cref="LedgerModal"/>'s own Label names: the lead
    /// beat (<c>BeatLine_0</c>, rendered at the fate line's own size) is the card's "headline"; the
    /// survived/fell badge (<c>CardStatus</c>) is its "fate"; everything else named or unnamed under
    /// the card (other beat rows, xp/rank/closest-call/warrant lines, the fate prose itself, the
    /// hero header, ore rows) buckets as "detail" — the card carries far more label shapes than a
    /// four-bucket split can name individually, and this unit's own brief only asks for the split
    /// "if the card structure lets you," not a perfect taxonomy.</summary>
    private static string CardLineKind(string labelName)
    {
        if (labelName == "BeatLine_0")
        {
            return "headline";
        }

        if (labelName == "CardStatus")
        {
            return "fate";
        }

        return "detail";
    }

    private static void CollectLabels(Node node, List<(string Name, string Text)> into)
    {
        if (node is Label label)
        {
            into.Add((label.Name.ToString(), label.Text));
        }

        foreach (var child in node.GetChildren())
        {
            CollectLabels(child, into);
        }
    }

    /// <summary>Same walk as <see cref="CollectLabels"/>, but never descends into a
    /// <c>LedgerCard_&lt;n&gt;</c> subtree — those are read separately, per hero. Everything left
    /// under the ledger's card root renders once per NIGHT rather than once per hero (the narrator
    /// line, the followed-item line, gate-held-streak lines, the once-ever tutorial tip), which is
    /// exactly the "shared lines" bucket the unit brief names.</summary>
    private static void CollectLabelsExcludingCards(Node node, List<(string Name, string Text)> into)
    {
        if (CardNamePattern.IsMatch(node.Name.ToString()))
        {
            return;
        }

        if (node is Label label)
        {
            into.Add((label.Name.ToString(), label.Text));
        }

        foreach (var child in node.GetChildren())
        {
            CollectLabelsExcludingCards(child, into);
        }
    }

    /// <summary>Digits first (a level-up numeral, a gold figure, a floor number), then every known
    /// hero/item name, longest first so "Bram" cannot be swallowed as a substring of "Bramwell"
    /// before its own turn.</summary>
    private static string Mask(string text, HashSet<string> names)
    {
        var masked = DigitsPattern.Replace(text, MaskToken);
        foreach (var name in names.OrderByDescending(n => n.Length))
        {
            if (name.Length == 0)
            {
                continue;
            }

            masked = masked.Replace(name, NameMaskToken, StringComparison.Ordinal);
        }

        return masked;
    }

    private static string FormatSeedRow(SeedResult result)
    {
        var sb = new StringBuilder();
        sb.Append("seed ").Append(result.Seed);
        foreach (var day in CensusDays)
        {
            var t = result.Days[day];
            sb.Append(" | day ").Append(day)
                .Append(": total=").Append(t.TotalLines)
                .Append(" repeat=").Append(t.RepeatLines)
                .Append(" share=").Append(t.Share.ToString("F3"));
            foreach (var (kind, (kindTotal, kindRepeat)) in t.ByKind)
            {
                var kindShare = kindTotal == 0 ? 0.0 : (double)kindRepeat / kindTotal;
                sb.Append(" [").Append(kind).Append(' ').Append(kindShare.ToString("F2")).Append(']');
            }
        }

        return sb.ToString();
    }

    private static string FormatTable(List<SeedResult> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine("P2-LONG-34 — Godot-side masked verbatim-repeat census over the RENDERED Evening Ledger");
        sb.AppendLine($"{results.Count} seed(s), start seed {StartSeed}, days {string.Join(", ", CensusDays)}.");
        sb.AppendLine();
        foreach (var result in results)
        {
            sb.AppendLine(FormatSeedRow(result));
        }

        sb.AppendLine();
        foreach (var day in CensusDays)
        {
            var shares = results.Select(r => r.Days[day].Share).OrderBy(s => s).ToList();
            var mid = shares.Count / 2;
            var median = shares.Count % 2 == 0 ? (shares[mid - 1] + shares[mid]) / 2.0 : shares[mid];
            sb.AppendLine($"day {day}: median share {median:F3} across {shares.Count} seed(s) (min {shares.First():F3}, max {shares.Last():F3})");
        }

        return sb.ToString();
    }
}
#endif
