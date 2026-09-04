using System.Text.RegularExpressions;

namespace Progress;

/// <summary>Parses CLAUDE.md rule 12 / §11.6 rule 3's `Serves:` receipt line out of a PR body.
/// Pure: string in, record out — no IO. The *search* for the line stays lenient (any case, any
/// leading whitespace) because the census exists specifically to catch drift in how the line was
/// written; only the *value* after the colon is held to the rule's literal forms
/// (`P&lt;n&gt;` / `link&lt;1-5&gt;` / `substrate` / `overhead — booked`), plus this repo's own
/// observed extension of citing a tracked unit id directly (`P2-HONEST-01`, `U27`).</summary>
public static class ServesReceipts
{
    private static readonly Regex ServesLine = new(@"(?im)^[ \t]*Serves:[ \t]*(.*)$", RegexOptions.Compiled);

    // Same shape PlanParser/CommitTags use for a tracked unit-index row id.
    private static readonly Regex UnitId = new(
        @"^(?:P2-[A-Z][A-Z0-9]*-\d+[a-zA-Z]?|U\d+[a-zA-Z]?)$", RegexOptions.Compiled);

    private static readonly Regex LinkId = new(@"^link[1-5]$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // §11.4's older critical-path items: "P1", "P5(a)" — a bare number, optionally with a single
    // lettered sub-item. Deliberately excludes the P2-DOMAIN-NN shape above (that always has a
    // dash before its trailing digits; this never does), so the two never collide.
    private static readonly Regex PlanItem = new(@"^P\d+(\([a-zA-Z]\))?$", RegexOptions.Compiled);

    private static readonly Regex Overhead = new(@"^overhead\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static ServesReceipt Parse(string? body)
    {
        var m = ServesLine.Match(body ?? string.Empty);
        if (!m.Success)
        {
            return new ServesReceipt(ServesKind.Missing, null, null);
        }

        var value = m.Groups[1].Value.Trim();

        if (LinkId.IsMatch(value))
        {
            return new ServesReceipt(ServesKind.Link, value, null);
        }

        if (string.Equals(value, "substrate", StringComparison.OrdinalIgnoreCase))
        {
            return new ServesReceipt(ServesKind.Substrate, value, null);
        }

        if (Overhead.IsMatch(value))
        {
            return new ServesReceipt(ServesKind.Overhead, value, null);
        }

        if (UnitId.IsMatch(value))
        {
            return new ServesReceipt(ServesKind.Unit, value, value);
        }

        if (PlanItem.IsMatch(value))
        {
            return new ServesReceipt(ServesKind.PlanItem, value, null);
        }

        // Present, non-empty, but fits none of the rule's forms — e.g. a bare domain like
        // "P2-ONBOARD" (missing its unit number) or free text. Reported, not silently accepted:
        // a malformed receipt breaks the one-grep audit rule 12 exists to enable.
        return new ServesReceipt(ServesKind.Malformed, value.Length == 0 ? null : value, null);
    }
}
