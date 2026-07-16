using System.Text;

namespace GameSim.Flavor;

/// <summary>
/// Deterministic flavor template renderer (KTD1/KTD2/KTD5). Pure static: output depends only
/// on the arguments — no kernel RNG (the API takes none, so drawing it is impossible), no wall
/// clock, integer-only math, ordinal string operations only. Flavor lines are sim state, so
/// this is sim code held to the determinism gate.
///
/// <para><b>Variant pick.</b> <c>Finalize(StableHash.Mix(campaignId, eventId,
/// StableHash.HashString(key))) % variantCount</c>. The finalizer is a SplitMix64-style
/// avalanche: raw FNV-1a low bits cycle with sequential event ids (index would be nearly
/// campaign-independent), and the pick must vary by campaign for R3's cross-seed variety.
/// Same save, same line, forever.</para>
///
/// <para><b>Validation (KTD5/R4)</b> is structural, via a single-pass parse of the TEMPLATE:
/// every <c>{name}</c> placeholder must resolve to a provided slot (an unknown placeholder or
/// an unclosed '<c>{</c>' fails); after substitution every provided slot value must appear
/// verbatim (ordinal) in the output (a template that omits a provided fact fails). Braces
/// arriving inside slot VALUES are data, not placeholders — the parse consumes template
/// placeholders only, so values containing braces pass validation and survive verbatim.
/// A bare '<c>}</c>' in a template is literal text.</para>
///
/// <para><b>Fallback (committed semantics).</b> Any failure — unknown full key, empty variant
/// list, or a variant failing validation — renders the BASE key's fallback (base key = segment
/// before the first '/'), substituted and validated the same way. No other variant is tried:
/// deterministic and simple. If the fallback itself fails validation (a pack authoring bug;
/// pack tests forbid it), its lenient substitution is returned — known slots substituted,
/// unknown placeholders left literal — with no further recursion. If the base key has no
/// fallback entry at all, the base key string itself is returned: deterministic, greppable,
/// and it signals the missing pack entry without throwing mid-sim.</para>
///
/// <para><b>Caller contract:</b> slot dictionaries must use ordinal key comparison.</para>
/// </summary>
public static class FlavorEngine
{
    /// <summary>Separator between key segments (event kind / optional beat / voice).</summary>
    public const char KeySeparator = '/';

    /// <summary>
    /// Render the flavor line for <paramref name="key"/> from <paramref name="pack"/>,
    /// substituting <paramref name="slots"/>; <paramref name="campaignId"/> and
    /// <paramref name="eventId"/> drive the stable variant pick.
    /// </summary>
    public static string Render(
        FlavorPack pack,
        string key,
        IReadOnlyDictionary<string, string> slots,
        ulong campaignId,
        ulong eventId)
    {
        if (pack.Variants.TryGetValue(key, out var variants) && variants.Count > 0)
        {
            var pick = Finalize(StableHash.Mix(campaignId, eventId, StableHash.HashString(key)));
            var index = (int)(pick % (ulong)variants.Count);
            if (TryRenderTemplate(variants[index], slots, out var line))
            {
                return line;
            }
        }

        return RenderFallback(pack, key, slots);
    }

    /// <summary>
    /// SplitMix64-style avalanche finalizer: spreads campaign-id entropy into the low bits
    /// the modulo reads. Without it, FNV-1a's low bits cycle with sequential event ids and
    /// the variant index barely varies by campaign (see class doc). Constants are the
    /// canonical SplitMix64 finalizer constants.
    /// </summary>
    private static ulong Finalize(ulong hash)
    {
        hash ^= hash >> 30;
        hash *= 0xBF58476D1CE4E5B9UL;
        hash ^= hash >> 27;
        hash *= 0x94D049BB133111EBUL;
        hash ^= hash >> 31;
        return hash;
    }

    /// <summary>The base key: the segment before the first '/', or the whole key if none.</summary>
    public static string BaseKey(string key)
    {
        var separator = key.IndexOf(KeySeparator); // char overload: always ordinal
        return separator < 0 ? key : key[..separator];
    }

    /// <summary>
    /// Substitute and validate a single template. Returns true with the rendered line when the
    /// template parses cleanly, every placeholder resolves, and every provided slot value
    /// appears verbatim in the output; false (with <paramref name="rendered"/> empty) otherwise.
    /// Exposed for pack conformance tests (fallbacks must always pass).
    /// </summary>
    public static bool TryRenderTemplate(
        string template,
        IReadOnlyDictionary<string, string> slots,
        out string rendered)
    {
        var builder = new StringBuilder(template.Length + 16);
        var i = 0;
        while (i < template.Length)
        {
            var ch = template[i];
            if (ch != '{')
            {
                builder.Append(ch);
                i++;
                continue;
            }

            var close = template.IndexOf('}', i + 1);
            if (close < 0)
            {
                rendered = string.Empty; // unclosed '{': malformed template
                return false;
            }

            var name = template.Substring(i + 1, close - i - 1);
            if (!slots.TryGetValue(name, out var value))
            {
                rendered = string.Empty; // placeholder no slot provides: cannot be consumed
                return false;
            }

            builder.Append(value);
            i = close + 1;
        }

        var line = builder.ToString();
        foreach (var slot in slots)
        {
            if (!line.Contains(slot.Value, StringComparison.Ordinal))
            {
                rendered = string.Empty; // a provided fact would be lost (R4)
                return false;
            }
        }

        rendered = line;
        return true;
    }

    private static string RenderFallback(FlavorPack pack, string key, IReadOnlyDictionary<string, string> slots)
    {
        var baseKey = BaseKey(key);
        if (!pack.Fallbacks.TryGetValue(baseKey, out var fallback))
        {
            return baseKey; // committed: missing pack entry surfaces as the base key itself
        }

        if (TryRenderTemplate(fallback, slots, out var line))
        {
            return line;
        }

        // Pack authoring bug (conformance tests forbid it): last resort, no recursion.
        return SubstituteLenient(fallback, slots);
    }

    /// <summary>
    /// Best-effort substitution with no validation: known slots substituted, everything else
    /// (unclosed braces, unknown placeholders) kept literally. Only reachable when a fallback
    /// template itself fails validation.
    /// </summary>
    private static string SubstituteLenient(string template, IReadOnlyDictionary<string, string> slots)
    {
        var builder = new StringBuilder(template.Length + 16);
        var i = 0;
        while (i < template.Length)
        {
            var ch = template[i];
            if (ch != '{')
            {
                builder.Append(ch);
                i++;
                continue;
            }

            var close = template.IndexOf('}', i + 1);
            if (close < 0)
            {
                builder.Append(template, i, template.Length - i);
                break;
            }

            var name = template.Substring(i + 1, close - i - 1);
            if (slots.TryGetValue(name, out var value))
            {
                builder.Append(value);
            }
            else
            {
                builder.Append(template, i, close - i + 1);
            }

            i = close + 1;
        }

        return builder.ToString();
    }
}
