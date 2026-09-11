using System.Text;

namespace Progress;

/// <summary>One unit an unattended run may take right now, or one it must not.</summary>
public sealed record FrontierRow(
    string UnitId,
    string Title,
    IReadOnlyList<string> Flags,
    IReadOnlyList<string> Files,
    string? RefusalReason);

/// <summary>
/// The machine-readable half of this tool: which units an unattended session may take, derived
/// fresh from `origin/main` and the plan on every call, stored nowhere.
///
/// <para><b>Why this is not a DAG file.</b> The system this idea came from keeps a committed JSON
/// graph with a hand-flipped <c>status</c> per node, and its own run ledger stopped being written
/// across 67 merged PRs without anything noticing. A second copy of a fact git already owns is a
/// copy that can go stale, and CLAUDE.md rule 8 exists because a stale doc is an instruction the
/// next session obeys. So the frontier is computed, never stored: the dependency edges come from
/// the plan's own <c>Depends on</c> column and landedness from <see cref="Reconciler"/>, which
/// reads git.</para>
///
/// <para><b>Deny by default, and the refusals are the point.</b> A row comes back RUNNABLE only
/// when every one of its dependencies is Landed AND every token in its Depends-on cell was one
/// this tool could resolve. That second condition is the one worth the file: a cell can name a
/// gate that is not a unit at all — <c>P4</c> (the owner's own evening), an open ruling like
/// <c>P2-OQ1</c>, a section cite — and the parser used to drop those silently, which is correct
/// for a human reading the report beside the raw cell and catastrophic for anything computing what
/// to build tonight. The source system shipped exactly this defect as a <c>type: HITL</c> field
/// that gated nothing; two of its committed kickoffs would have walked straight into it.</para>
///
/// <para>Also refused: a unit whose id is already written into tracked source (section 9's
/// warning — it may have shipped inside a PR whose subject carried no per-unit tag), and one
/// carrying a ceremony flag an unattended run must not perform alone.</para>
/// </summary>
public static class Frontier
{
    /// <summary>Flags an unattended run may not take on its own. <c>[C]</c> is a Contracts
    /// micro-PR, which CLAUDE.md's multi-agent rules reserve to the orchestrating session and
    /// require to land alone; <c>[GOLD]</c> re-records a golden replay and <c>[BAL]</c> re-pins a
    /// balance band — both are the shape where "make the test match the code" is indistinguishable
    /// from "soften the test", and rule 12 says the fix is never softening the test.</summary>
    private static readonly HashSet<string> CeremonyFlags = new(StringComparer.Ordinal) { "C", "GOLD", "BAL" };

    public static IReadOnlyList<FrontierRow> Compute(ReconciliationResult result)
    {
        var status = new Dictionary<string, UnitStatus>(StringComparer.Ordinal);
        foreach (var row in result.Domains.SelectMany(d => d.Rows))
        {
            status[row.Unit.Id] = row.Status;
        }

        var sourceTagged = new HashSet<string>(
            result.SourceTaggedUnbuilts.Select(s => s.UnitId), StringComparer.Ordinal);

        var rows = new List<FrontierRow>();
        foreach (var row in result.Domains.SelectMany(d => d.Rows))
        {
            if (row.Status != UnitStatus.Unbuilt)
            {
                continue;
            }

            rows.Add(new FrontierRow(
                row.Unit.Id,
                row.Unit.Title,
                row.Unit.Flags,
                row.Unit.Files.Select(f => f.Path).ToList(),
                Refusal(row.Unit, status, sourceTagged)));
        }

        return rows
            .OrderBy(r => r.RefusalReason is null ? 0 : 1)
            .ThenBy(r => r.UnitId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? Refusal(
        UnitRow unit,
        IReadOnlyDictionary<string, UnitStatus> status,
        IReadOnlySet<string> sourceTagged)
    {
        if (unit.UnparsedDependsOn.Count > 0)
        {
            return $"its Depends-on cell names {string.Join(", ", unit.UnparsedDependsOn.Select(t => $"\"{t}\""))}, "
                + "which is not a unit row this tool can resolve — an owner gate, a ruling or a section cite. "
                + "Never auto-runnable; the owner rules on it first.";
        }

        var unmet = unit.DependsOn
            .Where(d => !status.TryGetValue(d, out var s) || s != UnitStatus.Landed)
            .ToList();
        if (unmet.Count > 0)
        {
            return $"depends on {string.Join(", ", unmet)}, which {(unmet.Count == 1 ? "has" : "have")} not landed.";
        }

        var ceremony = unit.Flags.Where(CeremonyFlags.Contains).ToList();
        if (ceremony.Count > 0)
        {
            return $"carries {string.Join(", ", ceremony.Select(f => $"[{f}]"))} — a ceremony an unattended run "
                + "does not perform alone (Contracts micro-PR, golden re-record, balance re-baseline).";
        }

        if (sourceTagged.Contains(unit.Id))
        {
            return "its id is already written into tracked source on origin/main (section 9) — it may have "
                + "shipped inside a PR whose subject carried no per-unit tag. Read the cited file first.";
        }

        return null;
    }

    /// <summary>Plain-text render. Deliberately grep-shaped rather than JSON: the consumer is a
    /// model reading a terminal, and a line it can quote back verbatim in a PR body is worth more
    /// than a structure it has to summarise.</summary>
    public static string Render(IReadOnlyList<FrontierRow> rows, IReadOnlyList<string>? degradations = null)
    {
        var sb = new StringBuilder();

        // Fail closed, and say why in the first line so a caller that reads nothing else still
        // stops. Both degradations this can carry push units the SAME way -- toward looking
        // unbuilt -- so a degraded frontier is not merely incomplete, it is systematically
        // over-full of work that is already done or already in flight.
        if (degradations is { Count: > 0 })
        {
            sb.AppendLine("# Frontier REFUSED -- this run read incomplete data, so every row below would be a guess.");
            sb.AppendLine();
            foreach (var degradation in degradations)
            {
                sb.AppendLine($"DEGRADED  {degradation}");
            }

            sb.AppendLine();
            sb.AppendLine("RUNNABLE none -- fix the read and re-run. Never build off a degraded frontier.");
            return sb.ToString();
        }

        var runnable = rows.Where(r => r.RefusalReason is null).ToList();
        var refused = rows.Where(r => r.RefusalReason is not null).ToList();

        sb.AppendLine($"# Frontier — {runnable.Count} runnable, {refused.Count} refused, derived from origin/main");
        sb.AppendLine();

        if (runnable.Count == 0)
        {
            sb.AppendLine("RUNNABLE none — every unbuilt unit is blocked, gated or already in source. Stop and report.");
        }

        foreach (var row in runnable)
        {
            var flags = row.Flags.Count > 0 ? " [" + string.Join("][", row.Flags) + "]" : "";
            sb.AppendLine($"RUNNABLE  {row.UnitId}{flags}  {row.Title}");
            foreach (var file in row.Files)
            {
                sb.AppendLine($"    file  {file}");
            }
        }

        sb.AppendLine();
        foreach (var row in refused)
        {
            sb.AppendLine($"REFUSED   {row.UnitId}  {row.RefusalReason}");
        }

        return sb.ToString();
    }
}
