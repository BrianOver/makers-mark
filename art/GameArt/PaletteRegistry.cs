using System.Collections.Immutable;

namespace GameArt;

/// <summary>One palette family: an id assets reference and the prompt clause that carries it.</summary>
public sealed record PaletteDefinition(string Id, string Clause);

/// <summary>
/// The palette families (variety-tone direction §2, 2026-07-18): purple stops being the default
/// and becomes the NIGHT/DEEP anchor. Diffuses carry a per-family base palette; the lighting layer
/// (phase tints + PointLight2D) supplies mood on top. <c>house</c>'s clause is byte-identical to
/// the color text previously baked into <c>ArtTrackProfiles.Active.MasterPrompt</c>, so every
/// pre-family spec composes to the same prompt string and locked assets keep meaningful
/// fixed-seed provenance. Mirrors the sim registries' shape; add families via this file only.
/// Legibility rules for new families live in docs/design/asset-style-spec.md (tint-multiply
/// survival: warm R&gt;G&gt;B always safe; green needs G ≥ 1.6×B; teal is accent-only).
/// </summary>
public static class PaletteRegistry
{
    public static readonly PaletteDefinition House = new(
        "house",
        "deep desaturated void-purple shadows, iron-grey, warm ember-orange key glow, "
        + "subtle arcane-violet rim accents, muted somber palette");

    public static readonly PaletteDefinition Hearth = new(
        "hearth",
        "warm honey-amber daylight, terracotta and aged-timber tones, umber shadows, "
        + "soft golden key light, lived-in warmth, muted painterly palette");

    public static readonly PaletteDefinition Gloomwood = new(
        "gloomwood",
        "mossy green and lichen tones, verdigris-teal accents, damp cool forest shade, "
        + "deep loam shadows, scattered warm firefly glints, muted painterly palette");

    public static readonly PaletteDefinition Crypt = new(
        "crypt",
        "pale bone and parchment tones, cold cyan accent light, dusty grave-grey shadows, "
        + "faded funerary stone, muted painterly palette");

    public static readonly PaletteDefinition Den = new(
        "den",
        "rust-red and charcoal tones, ash-grey dust, hot coal-orange accents, "
        + "scorched iron texture, muted painterly palette");

    /// <summary>All families keyed by id, ordinal-sorted for deterministic iteration.</summary>
    public static readonly System.Collections.Immutable.ImmutableSortedDictionary<string, PaletteDefinition> All =
        new[] { House, Hearth, Gloomwood, Crypt, Den }
            .ToImmutableSortedDictionary(p => p.Id, p => p, System.StringComparer.Ordinal);

    public static bool IsRegistered(string id) => All.ContainsKey(id);

    public static PaletteDefinition Require(string id) =>
        All.TryGetValue(id, out var def)
            ? def
            : throw new System.Collections.Generic.KeyNotFoundException($"Unknown palette family '{id}'.");
}
