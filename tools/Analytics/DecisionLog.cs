using System.Text.Json;

namespace Analytics;

/// <summary>
/// U-T6 (register #164, MAKERS-MARK.md §11.14.8): reads the decision channel `PlaytestLog.Decision`
/// writes (`godot/scripts/PlaytestLog.cs`) — one JSONL row per choice the game made on the player's
/// behalf, or reflected back to them, with the reason already attached.
///
/// <para><b>Why this exists.</b> The prior unit (feat/u-t6-every-action-carries-its-reason) took the
/// sim from 1-of-6 known reason-bearing events reaching the session log to 6-of-6 — but nothing read
/// those rows back. A reason only a human grepping raw JSONL can find is barely better than one that
/// scrolled past on screen; the owner's own words are "so you can check later," and "check" means a
/// report, not a text search.</para>
///
/// <para><b>A DIFFERENT file format from the chronicle path.</b> <c>Program.cs</c>'s existing sweep
/// reads <c>*.json</c> chronicle exports (one JSON document per file, <c>ChronicleCodec</c>) written
/// by <c>sim/GameSim.Cli batch</c>. This reads <c>*.jsonl</c> session logs (one JSON object PER LINE,
/// several <c>kind</c>s interleaved — session/tick/action/audio/decision/note) written by the Godot
/// client's <c>PlaytestLog</c>, which only exists when a human or the agent-playtest harness actually
/// plays with <c>MM_PLAYTEST_LOG</c> set. The two are complementary, never overlapping inputs, so
/// this is additive: a corpus with zero <c>.jsonl</c> files reports nothing here and is otherwise
/// unaffected (see <see cref="Report(IReadOnlyList{DecisionRow})"/>'s empty-input contract).</para>
///
/// <para><b>Deliberately tolerant, like the chronicle sweep.</b> A line that fails to parse, or parses
/// but is not <c>"kind":"decision"</c>, is skipped — this file interleaves four other row kinds by
/// design (see <c>PlaytestLog.cs</c>), and a truncated last line (a session log grabbed mid-write) is
/// a normal, not exceptional, shape for this corpus.</para>
/// </summary>
public static class DecisionLog
{
    /// <summary>One decision row, exactly as <c>PlaytestLog.Decision</c> wrote it.</summary>
    /// <param name="What">The decision's subject, e.g. <c>hero-gear-pick:12</c>. See <see cref="Slug"/>
    /// for the stable grouping key this carries (the part before the numeric/id suffix).</param>
    /// <param name="Chose">What was picked, by id or short description.</param>
    /// <param name="Why">The rule or state that produced it, in the game's own vocabulary — the
    /// reason this whole channel exists to preserve.</param>
    /// <param name="Candidates">How many options were on the table, or -1 when the caller could not
    /// say.</param>
    public sealed record DecisionRow(string What, string Chose, string Why, int Candidates);

    /// <summary>
    /// The stable grouping key for a <see cref="DecisionRow.What"/> value: everything before the
    /// first <c>:</c>, e.g. <c>hero-gear-pick:12</c> → <c>hero-gear-pick</c>. Every known emitter
    /// (<c>godot/scripts/DecisionEvents.cs</c>) follows this <c>subject:id</c> shape; a <c>What</c>
    /// with no colon (none exist today, but a future emitter might reasonably omit an id) returns the
    /// whole string unchanged rather than throwing, so a new shape degrades to "one big bucket" instead
    /// of a crash.
    /// </summary>
    public static string Slug(string what)
    {
        var colon = what.IndexOf(':');
        return colon < 0 ? what : what[..colon];
    }

    /// <summary>Parses every <c>"kind":"decision"</c> line in <paramref name="path"/>. Never throws on
    /// a malformed or foreign-kind line — see this class's own doc for why that is normal shape here,
    /// not corruption.</summary>
    public static List<DecisionRow> ParseFile(string path)
    {
        var rows = new List<DecisionRow>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            DecisionRow? row;
            try
            {
                row = ParseLine(line);
            }
            catch (JsonException)
            {
                continue; // one bad line must never abort the corpus (batch-sweep precedent, Program.cs)
            }

            if (row is not null)
            {
                rows.Add(row);
            }
        }

        return rows;
    }

    private static DecisionRow? ParseLine(string line)
    {
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;
        if (!root.TryGetProperty("kind", out var kind) || kind.GetString() != "decision")
        {
            return null;
        }

        var what = root.TryGetProperty("what", out var w) ? w.GetString() ?? "" : "";
        var chose = root.TryGetProperty("chose", out var c) ? c.GetString() ?? "" : "";
        var why = root.TryGetProperty("why", out var y) ? y.GetString() ?? "" : "";
        var candidates = root.TryGetProperty("candidates", out var n) && n.TryGetInt32(out var ni) ? ni : -1;
        return new DecisionRow(what, chose, why, candidates);
    }

    /// <summary>
    /// Groups on the STABLE SLUG only (never on prose — <c>Why</c> varies per hero/item/id even for
    /// the exact same rule, and fuzzy keyword matching over it is the exact mistake
    /// <c>Report.Bucket</c> made once already, see this program's PR body). Within a slug, distinct
    /// <c>Why</c> strings are tallied by EXACT match (no keyword guessing) so "what did heroes
    /// actually refuse, and why" and "which reason dominates" are both answerable without inventing a
    /// category the data does not actually support.
    /// </summary>
    public static string Report(IReadOnlyList<DecisionRow> rows)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("## Decisions (session-log channel)");
        sb.AppendLine();
        if (rows.Count == 0)
        {
            sb.AppendLine("No decision rows found (no `.jsonl` session logs in this corpus, or none "
                + "with `MM_PLAYTEST_LOG` recording turned on).");
            return sb.ToString();
        }

        sb.AppendLine($"Total decision rows: {rows.Count}");
        sb.AppendLine();

        var bySlug = rows.GroupBy(r => Slug(r.What)).OrderByDescending(g => g.Count());
        foreach (var slug in bySlug)
        {
            sb.AppendLine($"### {slug.Key} ({slug.Count()})");
            sb.AppendLine();
            var byReason = slug.GroupBy(r => r.Why, StringComparer.Ordinal)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.Ordinal);
            foreach (var reason in byReason)
            {
                sb.AppendLine($"- {reason.Count()}× {reason.Key}");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }
}
