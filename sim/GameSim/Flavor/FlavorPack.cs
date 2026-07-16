using System.Collections.Immutable;

namespace GameSim.Flavor;

/// <summary>
/// Dumb data model for a flavor content pack (KTD4): committed C# template data with no
/// behavior — <see cref="FlavorEngine"/> owns picking, substitution, validation, and fallback.
///
/// <para><b>Variant keys.</b> <see cref="Variants"/> is keyed by the full flavor key: event
/// kind, optional beat, then voice, '/'-separated — e.g. <c>"potionLifesave/gruff"</c> or
/// <c>"heroDied/killingBlow/wry"</c>. Each key maps to the authored template variants the
/// engine picks among via the stable hash.</para>
///
/// <para><b>Fallbacks.</b> <see cref="Fallbacks"/> is keyed by the BASE key — the segment
/// before the first '/' (the event kind). Every base key that appears in <see cref="Variants"/>
/// requires exactly one fallback entry: the line rendered when a variant template fails
/// validation or the requested key is unknown. Fallback templates must be simple enough to
/// always pass validation themselves (pack conformance tests assert this).</para>
///
/// <para><b>Templates</b> carry <c>{slot}</c> placeholders (e.g. <c>{hero}</c>, <c>{item}</c>,
/// <c>{floor}</c>) substituted from the structured event's facts (R4). Both dictionaries are
/// ordinal-sorted for deterministic iteration, like every registry in the sim.</para>
/// </summary>
public sealed record FlavorPack(
    ImmutableSortedDictionary<string, ImmutableList<string>> Variants,
    ImmutableSortedDictionary<string, string> Fallbacks)
{
    /// <summary>Builds a pack with ordinal-sorted keys regardless of the input collections' comparers.</summary>
    public static FlavorPack Create(
        IEnumerable<KeyValuePair<string, ImmutableList<string>>> variants,
        IEnumerable<KeyValuePair<string, string>> fallbacks)
        => new(
            variants.ToImmutableSortedDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal),
            fallbacks.ToImmutableSortedDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal));
}
