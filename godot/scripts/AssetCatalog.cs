using System;
using System.Text;
using Godot;

namespace GodotClient;

/// <summary>
/// The R10 by-id art seam (U3, P006): typed, id-composing resolvers over the existing
/// null-tolerant <see cref="IconRegistry"/> loader, so gameplay screens ask for art by sim
/// concept (a recipe id, a monster kind, a venue id, a class id) instead of hand-wiring a
/// texture path. The id-composition below *is* the AssetId→file mapping documented in the
/// <c>art/specs/&lt;module&gt;</c> headers (<c>item-&lt;recipeId&gt;</c>, <c>monster-&lt;kind&gt;</c>,
/// <c>&lt;venue&gt;-backdrop</c>/<c>-entrance</c>, <c>hero-&lt;classId&gt;</c>).
///
/// <para><b>KTD-C decoupling:</b> this type must never reference <c>GameSim</c> types — callers
/// pass primitive strings (a recipe id, a class id, a venue id, a monster kind/slug) and this
/// class composes an id string from them, the same "bind by name" rule <c>HeroSpecs.ClassId</c>
/// already uses to keep the art lane decoupled from sim registries. Every resolver is
/// null-tolerant: an unknown/ungenerated concept degrades to <c>null</c>, never a throw.</para>
/// </summary>
public static class AssetCatalog
{
    private const string MonsterPrefix = "monster";

    // ---- id composition (public so callers/tests can assert the exact string) -----------------

    /// <summary>Item icon id for a <c>ProfessionRegistry.AllRecipes</c> key.</summary>
    public static string ItemIconId(string recipeId) => $"item-{recipeId}";

    /// <summary>Monster portrait id. <paramref name="kind"/> may be a display name (e.g. "Cave
    /// Rat", "The Forgeworm" — <c>VenueFloor.MonsterKind</c>) or an already-slugged id fragment
    /// (e.g. "cave-rat"); both slugify to the same id. <paramref name="venuePrefix"/> selects the
    /// venue-prefixed variant non-Mine monsters use (e.g. "gloomwood" → "gloomwood-bramble-boar");
    /// omitted/blank defaults to the Mine's plain "monster-" prefix.</summary>
    public static string MonsterPortraitId(string kind, string? venuePrefix = null)
    {
        var prefix = string.IsNullOrWhiteSpace(venuePrefix) ? MonsterPrefix : venuePrefix.Trim();
        return $"{prefix}-{Slugify(kind)}";
    }

    /// <summary>Venue backdrop id, e.g. "gloomwood" → "gloomwood-backdrop".</summary>
    public static string VenueBackdropId(string venueId) => $"{venueId}-backdrop";

    /// <summary>Venue entrance id, e.g. "sunkencrypt" → "sunkencrypt-entrance".</summary>
    public static string VenueEntranceId(string venueId) => $"{venueId}-entrance";

    /// <summary>Hero portrait id for a <c>ClassRegistry</c> class id, e.g. "vanguard" → "hero-vanguard".</summary>
    public static string HeroPortraitId(string classId) => $"hero-{classId}";

    // ---- resolvers (null-tolerant, delegate to IconRegistry.Art/Lit) --------------------------

    /// <summary>Flat menu icon for a craftable recipe (no normal map — see <c>ItemSpecs</c>).</summary>
    public static Texture2D? ItemIcon(string recipeId) => IconRegistry.Art(ItemIconId(recipeId));

    /// <summary>Lit world sprite for a Mine (or venue-prefixed) monster kind.</summary>
    public static CanvasTexture? MonsterPortrait(string kind, string? venuePrefix = null) =>
        IconRegistry.Lit(MonsterPortraitId(kind, venuePrefix));

    /// <summary>Flat far-plane backdrop for a venue (no normal map — see <c>GloomwoodSpecs</c>/
    /// <c>SunkenCryptSpecs</c>).</summary>
    public static Texture2D? VenueBackdrop(string venueId) => IconRegistry.Art(VenueBackdropId(venueId));

    /// <summary>Lit foreground entrance building for a venue.</summary>
    public static CanvasTexture? VenueEntrance(string venueId) => IconRegistry.Lit(VenueEntranceId(venueId));

    /// <summary>Lit class figure for a hero, neutral-tinted (caller applies <c>Modulate</c>).</summary>
    public static CanvasTexture? HeroPortrait(string classId) => IconRegistry.Lit(HeroPortraitId(classId));

    // ---- presence -------------------------------------------------------------------------------

    /// <summary>True iff the generated manifest lists <paramref name="id"/> — the authoritative
    /// "what exists" check (no filesystem probe), backed by <see cref="IconRegistry.Has"/>'s
    /// cached, once-loaded manifest.</summary>
    public static bool Has(string id) => IconRegistry.Has(id);

    /// <summary>True iff the manifest lists a committed normal map for <paramref name="id"/>;
    /// false for an absent id or a diffuse-only entry (flat icon/backdrop).</summary>
    public static bool HasNormal(string id) => IconRegistry.HasNormal(id);

    // ---- helpers --------------------------------------------------------------------------------

    /// <summary>Lowercase, hyphen-separated slug: strips a leading "The " (so "The Forgeworm" →
    /// "forgeworm", matching the committed <c>monster-forgeworm</c> id), collapses any run of
    /// non-alphanumeric characters to a single hyphen, and trims leading/trailing hyphens.
    /// Idempotent — an already-slugged input ("cave-rat") passes through unchanged.</summary>
    private static string Slugify(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var trimmed = value.Trim();
        if (trimmed.StartsWith("The ", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[4..];
        }

        var sb = new StringBuilder(trimmed.Length);
        var lastWasHyphen = false;
        foreach (var ch in trimmed)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
                lastWasHyphen = false;
            }
            else if (sb.Length > 0 && !lastWasHyphen)
            {
                sb.Append('-');
                lastWasHyphen = true;
            }
        }
        if (sb.Length > 0 && sb[^1] == '-')
        {
            sb.Length--;
        }
        return sb.ToString();
    }
}
