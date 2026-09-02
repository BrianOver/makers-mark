using System.Text.RegularExpressions;

namespace Progress;

/// <summary>Parses the plan's unit-index markdown tables and doc references. Pure: takes the
/// document text, returns records. Never touches git, gh, or the filesystem — that stays in
/// GitShell/Program so this class is unit-testable with hand-built fixtures.
///
/// Defensive by design: the plan's tables are hand-written and drift. A line that only *looks*
/// like a unit row (an id-shaped first cell) but doesn't fit its family's expected column count
/// is reported via <see cref="PlanParseResult.Unparseable"/>, never dropped — a parser that
/// quietly skips rows is the same defect class as a hand-listed fixture array that stops
/// covering its family (see CLAUDE.md).</summary>
public static class PlanParser
{
    private static readonly Regex P2IdPattern = new(@"^P2-[A-Z][A-Z0-9]*-\d+[a-zA-Z]?$", RegexOptions.Compiled);
    private static readonly Regex T10IdPattern = new(@"^U\d+[a-zA-Z]?$", RegexOptions.Compiled);
    private static readonly Regex SeparatorCell = new(@"^:?-+:?$", RegexOptions.Compiled);
    private static readonly Regex BacktickSpan = new("`([^`]+)`", RegexOptions.Compiled);
    private static readonly Regex FlagToken = new(@"\[([A-Za-z][A-Za-z0-9]*)\]", RegexOptions.Compiled);
    private static readonly Regex DocMdRef = new(@"docs/[A-Za-z0-9_./\-]+\.md", RegexOptions.Compiled);

    private static readonly Regex DepP2Id = new(@"\bP2-[A-Z][A-Z0-9]*-\d+[a-zA-Z]?\b", RegexOptions.Compiled);
    private static readonly Regex DepT10Id = new(@"\bU\d+[a-zA-Z]?\b", RegexOptions.Compiled);
    private static readonly Regex DepP2Range = new(@"\b(P2-[A-Z][A-Z0-9]*)-(\d+)\.\.(\d+)\b", RegexOptions.Compiled);

    public static PlanParseResult Parse(string text)
    {
        var units = new List<UnitRow>();
        var unparseable = new List<UnparseableRow>();
        var docRefs = new List<DocRef>();

        var lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var lineNumber = i + 1;
            var rawLine = lines[i];
            var trimmed = rawLine.Trim();

            // Doc references are scanned on every line, table or prose — a dangling citation in a
            // sentence is exactly as stale as one in a table cell.
            foreach (Match m in DocMdRef.Matches(rawLine))
            {
                docRefs.Add(new DocRef(m.Value, lineNumber));
            }

            if (!trimmed.StartsWith('|'))
            {
                continue;
            }

            var cells = SplitRow(trimmed);
            if (cells.Count == 0 || IsSeparatorRow(cells))
            {
                continue;
            }

            var idCell = cells[0].Trim();
            // Strip the "carries a body" marker (⚑) some P2 rows lead with.
            idCell = idCell.TrimStart('⚑').Trim();

            UnitTable table;
            int expectedCols;
            if (P2IdPattern.IsMatch(idCell))
            {
                table = UnitTable.P2;
                expectedCols = 5;
            }
            else if (T10IdPattern.IsMatch(idCell))
            {
                table = UnitTable.T10;
                expectedCols = 4;
            }
            else
            {
                // Not a unit-index row (some other table entirely) — not this parser's concern.
                continue;
            }

            if (cells.Count != expectedCols)
            {
                unparseable.Add(new UnparseableRow(lineNumber, rawLine,
                    $"{table} unit id '{idCell}' but row has {cells.Count} cell(s), expected {expectedCols}"));
                continue;
            }

            var title = cells[1].Trim();
            if (title.Length == 0)
            {
                unparseable.Add(new UnparseableRow(lineNumber, rawLine, $"{table} unit '{idCell}' has an empty title cell"));
                continue;
            }

            var filesCell = cells[2];
            var depsCell = cells[3];
            var flags = table == UnitTable.P2 ? ExtractFlags(cells[4]) : Array.Empty<string>();

            var files = ExtractFileRefs(filesCell);
            var deps = ExtractDependsOn(depsCell);

            units.Add(new UnitRow(table, idCell, title, files, deps, depsCell.Trim(), flags, lineNumber));
        }

        return new PlanParseResult(units, unparseable, docRefs);
    }

    private static List<string> SplitRow(string trimmedLine)
    {
        var parts = trimmedLine.Split('|');
        var start = 0;
        var end = parts.Length;
        // A well-formed row starts and ends with '|', which produces an empty leading/trailing
        // element from Split — drop those, but only when they are actually empty (a row missing
        // its closing pipe is exactly the kind of drift this parser must not paper over).
        if (start < end && parts[start].Trim().Length == 0)
        {
            start++;
        }

        if (end > start && parts[end - 1].Trim().Length == 0)
        {
            end--;
        }

        var cells = new List<string>(end - start);
        for (var i = start; i < end; i++)
        {
            cells.Add(parts[i].Trim());
        }

        return cells;
    }

    private static bool IsSeparatorRow(List<string> cells) => cells.All(c => SeparatorCell.IsMatch(c.Trim()));

    private static IReadOnlyList<string> ExtractFlags(string cell)
    {
        var flags = new List<string>();
        foreach (Match m in FlagToken.Matches(cell))
        {
            flags.Add(m.Groups[1].Value);
        }

        return flags;
    }

    /// <summary>Only backtick-quoted spans that contain a '/' are treated as paths — bare
    /// backtick spans like `AddButton` or `ThreadHero` are symbol names, not files, and this
    /// doc uses backticks for both.</summary>
    private static IReadOnlyList<FileRef> ExtractFileRefs(string cell)
    {
        var refs = new List<FileRef>();
        foreach (Match m in BacktickSpan.Matches(cell))
        {
            var path = m.Groups[1].Value.Trim();
            if (!path.Contains('/'))
            {
                continue;
            }

            var before = cell[..m.Index];
            var isNew = Regex.IsMatch(before, @"(?:^|\W)new\s*$", RegexOptions.IgnoreCase);
            refs.Add(new FileRef(path, isNew));
        }

        return refs;
    }

    /// <summary>Extracts unit-id tokens from a "Depends on" cell. Handles the plan's observed
    /// shorthands: an em dash / bare dash for "none", comma lists, and same-domain slash ranges
    /// ("P2-SCREEN-02..10", "P2-SCREEN-07/08"). Text that isn't a recognizable unit id (a ruling
    /// reference like "P2-OQ1", a critical-path item like "P4", a section cite like "§11.5") is
    /// left out of the parsed list — it's not a dependency this tool can check, not a parse
    /// failure of the row itself.</summary>
    private static IReadOnlyList<string> ExtractDependsOn(string cell)
    {
        var ids = new List<string>();

        foreach (Match range in DepP2Range.Matches(cell))
        {
            var domain = range.Groups[1].Value;
            var startText = range.Groups[2].Value;
            var endText = range.Groups[3].Value;
            var width = startText.Length;
            if (int.TryParse(startText, out var start) && int.TryParse(endText, out var end) && start <= end)
            {
                for (var n = start; n <= end; n++)
                {
                    ids.Add($"{domain}-{n.ToString().PadLeft(width, '0')}");
                }
            }
        }

        foreach (Match m in DepP2Id.Matches(cell))
        {
            ids.Add(m.Value);
        }

        foreach (Match m in DepT10Id.Matches(cell))
        {
            ids.Add(m.Value);
        }

        // Slash-combo shorthand within a P2 id run, e.g. "P2-SCREEN-07/08" — the base regex above
        // already captured "P2-SCREEN-07"; pick up the "/08" continuation(s) here.
        foreach (Match m in Regex.Matches(cell, @"\bP2-[A-Z][A-Z0-9]*-\d+((?:/\d+[a-zA-Z]?)+)\b"))
        {
            var anchor = m.Value[..(m.Value.IndexOf('/'))];
            var domain = anchor[..anchor.LastIndexOf('-')];
            foreach (Match part in Regex.Matches(m.Groups[1].Value, @"\d+[a-zA-Z]?"))
            {
                ids.Add($"{domain}-{part.Value}");
            }
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var id in ids)
        {
            if (seen.Add(id))
            {
                result.Add(id);
            }
        }

        return result;
    }
}
