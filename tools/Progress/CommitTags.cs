using System.Text.RegularExpressions;

namespace Progress;

/// <summary>Extracts unit-id tags and PR numbers from commit subjects / PR titles. Pure: takes a
/// string, returns tokens. The repo's commit-tag convention drifts (parenthetical single id,
/// parenthetical slash-combo, colon-prefixed slash-combo, comma lists, or no tag at all on a
/// "sweep" commit) — this reads the shape rather than trusting one fixed convention, and treats
/// a commit that carries no recognizable tag as carrying no tag, not an error: not every commit
/// closes a unit.</summary>
public static class CommitTags
{
    private static readonly Regex PrNumberSuffix = new(@"\(#(\d+)\)\s*$", RegexOptions.Compiled);

    // An anchor id, optionally followed by one or more "/NN" continuations that share its domain
    // (the plan's "P2-SCREEN-07/08" / "P2-SCREEN-01/02" shorthand). The continuation group is
    // bare digits only — "U46, U47" does not need it, since each full id already matches the
    // base alternation on its own pass.
    private static readonly Regex TagPattern = new(
        @"\b(?<full>P2-[A-Z][A-Z0-9]*-\d+[a-zA-Z]?|U\d+[a-zA-Z]?)(?<cont>(?:/\d+[a-zA-Z]?)*)\b",
        RegexOptions.Compiled);

    public static int? ExtractPrNumber(string commitSubject)
    {
        var m = PrNumberSuffix.Match(commitSubject);
        return m.Success ? int.Parse(m.Groups[1].Value) : null;
    }

    public static IReadOnlyList<string> ExtractUnitIds(string subjectOrTitle)
    {
        var withoutPr = PrNumberSuffix.Replace(subjectOrTitle, string.Empty);

        var ids = new List<string>();
        foreach (Match m in TagPattern.Matches(withoutPr))
        {
            var full = m.Groups["full"].Value;
            ids.Add(full);

            var cont = m.Groups["cont"].Value;
            if (cont.Length == 0)
            {
                continue;
            }

            var isP2 = full.StartsWith("P2-", StringComparison.Ordinal);
            var domain = isP2 ? full[..full.LastIndexOf('-')] : null;

            foreach (Match part in Regex.Matches(cont, @"\d+[a-zA-Z]?"))
            {
                ids.Add(isP2 ? $"{domain}-{part.Value}" : $"U{part.Value}");
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
