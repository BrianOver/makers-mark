using System.Text;

namespace Progress;

/// <summary>Formats a <see cref="ReconciliationResult"/> to plain text for stdout. Pure: takes
/// the result (plus an optional ref label for the header), returns a string.</summary>
public static class Report
{
    public static string Build(ReconciliationResult result, string? refLabel = null)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# Progress reconciliation" + (refLabel is null ? "" : $" — {refLabel}"));
        sb.AppendLine();
        sb.AppendLine("Derived fresh from git/gh on every run. Nothing here is stored — re-run to re-check.");
        sb.AppendLine();

        AppendDomains(sb, result.Domains);
        AppendMissingFiles(sb, result.MissingFiles);
        AppendOrderingViolations(sb, result.OrderingViolations);
        AppendDanglingDocs(sb, result.DanglingDocs);
        AppendCollisions(sb, result.Collisions);
        AppendUnparseable(sb, result.Unparseable);
        AppendSummary(sb, result);

        return sb.ToString();
    }

    private static void AppendDomains(StringBuilder sb, IReadOnlyList<DomainStatus> domains)
    {
        sb.AppendLine("## 1. Per-domain status");
        sb.AppendLine();

        if (domains.Count == 0)
        {
            sb.AppendLine("(no unit-index rows parsed)");
            sb.AppendLine();
            return;
        }

        foreach (var domain in domains)
        {
            var landed = domain.Rows.Count(r => r.Status == UnitStatus.Landed);
            var open = domain.Rows.Count(r => r.Status == UnitStatus.Open);
            var unbuilt = domain.Rows.Count(r => r.Status == UnitStatus.Unbuilt);

            sb.AppendLine($"### {domain.Domain} — {landed} landed / {open} open / {unbuilt} unbuilt (of {domain.Rows.Count})");
            sb.AppendLine();

            foreach (var row in domain.Rows)
            {
                var status = row.Status switch
                {
                    UnitStatus.Landed => $"LANDED  {ShortSha(row.Sha)}" + (row.PrNumber is { } pr ? $" #{pr}" : ""),
                    UnitStatus.Open => "OPEN    " + (row.PrNumber is { } pr2 ? $"#{pr2}" : "(untagged)"),
                    UnitStatus.Unbuilt => "UNBUILT",
                    _ => "?",
                };

                sb.AppendLine($"- [{status}] {row.Unit.Id} — {row.Unit.Title}");
            }

            sb.AppendLine();
        }
    }

    private static string ShortSha(string? sha) => sha is null ? "" : (sha.Length > 9 ? sha[..9] : sha);

    private static void AppendMissingFiles(StringBuilder sb, IReadOnlyList<MissingFileFinding> findings)
    {
        sb.AppendLine("## 2. Units citing a path that does not exist on origin/main");
        sb.AppendLine();

        if (findings.Count == 0)
        {
            sb.AppendLine("None.");
        }
        else
        {
            foreach (var f in findings)
            {
                sb.AppendLine($"- {f.UnitId} (line {f.LineNumber}): `{f.Path}`");
            }
        }

        sb.AppendLine();
    }

    private static void AppendOrderingViolations(StringBuilder sb, IReadOnlyList<OrderingViolation> violations)
    {
        sb.AppendLine("## 3. Ordering violations (landed unit depends on an unlanded unit)");
        sb.AppendLine();

        if (violations.Count == 0)
        {
            sb.AppendLine("None.");
        }
        else
        {
            foreach (var v in violations)
            {
                sb.AppendLine($"- {v.UnitId} ({ShortSha(v.UnitSha)}{(v.UnitPr is { } pr ? $" #{pr}" : "")}) depends on {v.DepId}, which is not landed");
            }
        }

        sb.AppendLine();
    }

    private static void AppendDanglingDocs(StringBuilder sb, IReadOnlyList<DocRef> dangling)
    {
        sb.AppendLine("## 4. Dangling doc references (docs/**.md cited but not on origin/main)");
        sb.AppendLine();

        if (dangling.Count == 0)
        {
            sb.AppendLine("None.");
        }
        else
        {
            foreach (var d in dangling)
            {
                sb.AppendLine($"- `{d.Path}` (first cited line {d.LineNumber})");
            }
        }

        sb.AppendLine();
    }

    private static void AppendCollisions(StringBuilder sb, IReadOnlyList<IdCollision> collisions)
    {
        sb.AppendLine("## 5. Unit-id collisions");
        sb.AppendLine();

        if (collisions.Count == 0)
        {
            sb.AppendLine("None.");
        }
        else
        {
            foreach (var c in collisions)
            {
                var lines = string.Join(", ", c.LineNumbers);
                var tables = string.Join(", ", c.Tables);
                sb.AppendLine($"- {c.Id} used at lines {lines} (table(s): {tables})");
            }
        }

        sb.AppendLine();
    }

    private static void AppendUnparseable(StringBuilder sb, IReadOnlyList<UnparseableRow> unparseable)
    {
        sb.AppendLine("## Unparseable rows (id-shaped but malformed — not a failure, but reported, never dropped)");
        sb.AppendLine();

        if (unparseable.Count == 0)
        {
            sb.AppendLine("None.");
        }
        else
        {
            foreach (var u in unparseable)
            {
                sb.AppendLine($"- line {u.LineNumber}: {u.Reason}");
                sb.AppendLine($"  `{u.RawLine.Trim()}`");
            }
        }

        sb.AppendLine();
    }

    private static void AppendSummary(StringBuilder sb, ReconciliationResult result)
    {
        var totalUnits = result.Domains.Sum(d => d.Rows.Count);
        var landed = result.Domains.Sum(d => d.Rows.Count(r => r.Status == UnitStatus.Landed));
        var open = result.Domains.Sum(d => d.Rows.Count(r => r.Status == UnitStatus.Open));
        var unbuilt = result.Domains.Sum(d => d.Rows.Count(r => r.Status == UnitStatus.Unbuilt));

        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine($"- Units parsed: {totalUnits} ({landed} landed, {open} open, {unbuilt} unbuilt)");
        sb.AppendLine($"- Unparseable rows: {result.Unparseable.Count}");
        sb.AppendLine($"- Missing-file findings: {result.MissingFiles.Count}");
        sb.AppendLine($"- Ordering violations: {result.OrderingViolations.Count}");
        sb.AppendLine($"- Dangling doc references: {result.DanglingDocs.Count}");
        sb.AppendLine($"- Unit-id collisions: {result.Collisions.Count}");

        var failing = result.MissingFiles.Count > 0 || result.OrderingViolations.Count > 0 || result.Collisions.Count > 0;
        sb.AppendLine();
        sb.AppendLine(failing
            ? "EXIT NON-ZERO: missing-file, ordering, or collision findings above are defects, not information."
            : "EXIT ZERO: no misfiled paths, ordering violations, or id collisions.");
    }
}
