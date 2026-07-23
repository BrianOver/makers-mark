using System;
using System.Collections.Generic;
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

    /// <summary>Id of the player-blacksmith avatar figure (<c>art/specs/town/TownSpecsExtra.cs</c>,
    /// U13/U20) — a single fixed id, not composed from any parameter, since there is exactly one
    /// avatar. Public so <see cref="GodotClient.Town.InteriorStage"/> and tests can reference the
    /// exact string without duplicating the literal.</summary>
    public const string PlayerAvatarId = "player-avatar";

    /// <summary>Lit avatar figure, or null while the art hasn't landed yet — <see
    /// cref="GodotClient.Town.InteriorStage"/> falls back to a tinted placeholder rect (U20 scope
    /// note: no image exists yet, per <c>TownSpecsExtra</c>).</summary>
    public static CanvasTexture? PlayerAvatar() => IconRegistry.Lit(PlayerAvatarId);

    // ---- 3D gen monster meshes (MonsterView3D spectate stage) ---------------------------------

    /// <summary>
    /// AI-gen monster GLB file names under <c>TownAssets.GenModels</c>, keyed by slugged Mine
    /// monster kind. An EXPLICIT map (not slug composition like <see cref="MonsterPortraitId"/>)
    /// because the gen pipeline shipped shortened file names ("monster-spider.glb", not
    /// "monster-tunnel-spider.glb"). A kind with no entry ("forgeworm" — no gen model yet) resolves
    /// null, which is <c>MonsterView3D</c>'s cue to keep the 2D portrait fallback.
    /// </summary>
    private static readonly Dictionary<string, string> MonsterModelFiles = new()
    {
        ["cave-rat"] = "monster-cave-rat.glb",
        ["tunnel-spider"] = "monster-spider.glb",
        ["deep-ghoul"] = "monster-ghoul.glb",
        ["ore-golem"] = "monster-ore-golem.glb",
    };

    /// <summary>Gen monster GLB file name for <paramref name="kind"/> (display name "Cave Rat" or
    /// slug "cave-rat" — same <see cref="Slugify"/> tolerance as <see cref="MonsterPortraitId"/>),
    /// or null when no 3D model has been generated for that kind yet. Null-tolerant like every
    /// resolver here — never a throw.</summary>
    public static string? MonsterModelFile(string kind) =>
        MonsterModelFiles.TryGetValue(Slugify(kind), out var file) ? file : null;

    // ---- world-rework U14 (KTD6): feet-anchor offset table ------------------------------------

    /// <summary>
    /// KTD6: <c>AssetSpec</c> carries no pivot field and the committed PNGs are content-tight
    /// trimmed cutouts, so "bottom of content ≈ baseline" holds for nearly every world sprite —
    /// the (retired) 2D town feet-anchored buildings by placing <c>Sprite2D</c> art (always
    /// <c>Centered = false</c>) at <c>(-width/2, -height)</c> relative to the ground-line point and
    /// only consults this table for the exceptions (a roof overhang, a few px of untrimmed margin).
    /// Keyed by the same <c>Lit</c> id <see cref="IconRegistry.Lit"/> resolves — never a raw file
    /// path — so a new venue/prop only needs an entry here if its trimmed art does not already sit
    /// flush with its own baseline. No art regeneration required for anchoring (KTD6).
    /// </summary>
    private static readonly Dictionary<string, Vector2> FeetAnchorOffsets = new()
    {
        // The mine-gate art carries a few px of transparent margin below its trimmed silhouette
        // (the gate's stone threshold reads slightly inset) — nudge it down onto the ground line.
        ["town-mine-gate"] = new Vector2(0f, 6f),
    };

    /// <summary>Per-asset feet-anchor correction (KTD6) — <see cref="Vector2.Zero"/> for any id
    /// not listed (the common case: trimmed bottom edge already IS the baseline).</summary>
    public static Vector2 FeetAnchorOffset(string litId) =>
        FeetAnchorOffsets.TryGetValue(litId, out var offset) ? offset : Vector2.Zero;

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
