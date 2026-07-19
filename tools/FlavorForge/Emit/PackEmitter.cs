using System.Text;

namespace FlavorForge.Emit;

/// <summary>
/// Splices accepted variants into an EXISTING pack file's <c>ImmutableList.Create(...)</c>
/// blocks, in place (KTD-D). Anchored on the pack's own
/// <c>[$"{ConstName}/{voice}"] = ImmutableList.Create(</c> entry line — the constant identifier
/// is the base key with its first character upper-cased (the convention every shipped pack
/// follows: <c>heroDied</c> → <c>HeroDied</c>, <c>killingBlow</c> → <c>KillingBlow</c>, etc.), so
/// the anchor is derived mechanically from the accepted key, never hand-mapped per surface.
///
/// <para>Never adds a key, never touches <c>Fallbacks</c>, never invents a new pack file. All
/// edits happen against an in-memory string; a missing anchor throws
/// <see cref="PackEmitAnchorNotFoundException"/> BEFORE any accepted key is applied, so a caller
/// that only writes the returned text on success can never produce a partially-spliced file.</para>
/// </summary>
public static class PackEmitter
{
    /// <summary>
    /// Returns <paramref name="packSource"/> with every key in <paramref name="acceptedByKey"/>
    /// that has at least one accepted line spliced into its existing block. Keys with zero
    /// accepted lines are skipped entirely (no-op for that key). Throws
    /// <see cref="PackEmitAnchorNotFoundException"/> — with the source UNCHANGED from the
    /// caller's point of view (nothing is returned) — if any such key's anchor can't be found.
    /// </summary>
    public static string Splice(string packSource, IReadOnlyDictionary<string, IReadOnlyList<string>> acceptedByKey)
    {
        var text = packSource;
        foreach (var (key, lines) in acceptedByKey)
        {
            if (lines.Count == 0)
            {
                continue;
            }

            var separator = key.IndexOf('/');
            if (separator < 0)
            {
                throw new ArgumentException($"key '{key}' is not in '<baseKey>/<voice>' form", nameof(acceptedByKey));
            }

            var baseKey = key[..separator];
            var voice = key[(separator + 1)..];
            var constName = char.ToUpperInvariant(baseKey[0]) + baseKey[1..];
            var anchor = $"[$\"{{{constName}}}/{voice}\"] = ImmutableList.Create(";

            var anchorIndex = text.IndexOf(anchor, StringComparison.Ordinal);
            if (anchorIndex < 0)
            {
                throw new PackEmitAnchorNotFoundException(
                    $"no anchor found for key '{key}' (looked for `{anchor}`) — pack-shape mismatch, aborting with no write");
            }

            var openParenIndex = anchorIndex + anchor.Length - 1;
            var closeParenIndex = FindMatchingClose(text, openParenIndex);
            var indent = LeadingIndentOfLineContaining(text, closeParenIndex);

            var insertion = new StringBuilder();
            insertion.Append(',').Append('\n');
            for (var i = 0; i < lines.Count; i++)
            {
                insertion.Append(indent).Append(EscapeLiteral(lines[i]));
                insertion.Append(i == lines.Count - 1 ? string.Empty : ",\n");
            }

            text = text[..closeParenIndex] + insertion.ToString() + text[closeParenIndex..];
        }

        return text;
    }

    /// <summary>File-IO wrapper: reads, splices, and writes back ONLY if the content actually
    /// changed (zero accepted lines everywhere ⇒ the file is byte-unchanged, never rewritten).</summary>
    public static void SpliceFile(string filePath, IReadOnlyDictionary<string, IReadOnlyList<string>> acceptedByKey)
    {
        var original = File.ReadAllText(filePath);
        var updated = Splice(original, acceptedByKey);
        if (!string.Equals(original, updated, StringComparison.Ordinal))
        {
            File.WriteAllText(filePath, updated);
        }
    }

    private static string EscapeLiteral(string raw) =>
        "\"" + raw.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static string LeadingIndentOfLineContaining(string text, int index)
    {
        var lineStart = text.LastIndexOf('\n', index) + 1;
        var end = lineStart;
        while (end < index && (text[end] == ' ' || text[end] == '\t'))
        {
            end++;
        }

        return text[lineStart..end];
    }

    /// <summary>Walks forward from an opening '(' to its matching ')', skipping over the contents
    /// of any string literal encountered (so parens inside quoted prose never miscount depth).</summary>
    private static int FindMatchingClose(string text, int openParenIndex)
    {
        var depth = 0;
        var i = openParenIndex;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '"')
            {
                i = SkipStringLiteral(text, i);
                continue;
            }

            if (c == '(')
            {
                depth++;
            }
            else if (c == ')')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }

            i++;
        }

        throw new FormatException("unbalanced parentheses while scanning an ImmutableList.Create(...) block");
    }

    /// <summary>Skips a `"..."` string literal (respecting `\"` and `\\` escapes) and returns the
    /// index just past its closing quote.</summary>
    private static int SkipStringLiteral(string text, int quoteIndex)
    {
        var i = quoteIndex + 1;
        while (i < text.Length)
        {
            if (text[i] == '\\')
            {
                i += 2;
                continue;
            }

            if (text[i] == '"')
            {
                return i + 1;
            }

            i++;
        }

        throw new FormatException("unterminated string literal while scanning pack source");
    }
}
