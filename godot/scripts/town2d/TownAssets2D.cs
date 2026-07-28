using System.Collections.Generic;
using Godot;

namespace GodotClient.Town2d;

/// <summary>
/// U1: venue-key/hero-class → <see cref="Texture2D"/> for the 2.5D town — the 2D twin of
/// <c>town3d.TownAssets</c>, but resolving through the SAME generated-art pipeline every other
/// panel already uses (<see cref="IconRegistry.Art"/>, manifest-backed) rather than a separate
/// Kenney-kit loader, since the pivot plan's asset manifest ships flat PNGs under
/// <c>res://assets/art/</c> (U6 programmer-art pack, U7 gen batch) keyed by these SAME ids.
///
/// <para><b>Never null-crashes</b> (the whole point of this class existing before U6/U7 land any
/// pixels): every lookup falls back to a small procedurally-built flat-color <see
/// cref="ImageTexture"/>, sized/tinted per venue so the vertical slice still reads as distinct
/// buildings — not a blank hole — on a fresh checkout. Cached per id so repeat calls (multiple
/// <see cref="Town2D.ReconcileHeroes"/> passes, e.g.) don't rebuild the same placeholder image.</para>
/// </summary>
public static class TownAssets2D
{
    private static readonly Dictionary<string, Texture2D> PlaceholderCache = new();

    /// <summary>Per-venue placeholder footprint + tint — deliberately distinct colors so the five
    /// buildings read apart from each other even with zero real art (KTD2/goal-4 "not bare gray").</summary>
    private static readonly Dictionary<string, (Vector2 Size, Color Color)> VenuePlaceholders = new()
    {
        ["forge"] = (new Vector2(64, 80), new Color(0.45f, 0.27f, 0.16f)),
        ["market"] = (new Vector2(64, 64), new Color(0.30f, 0.42f, 0.38f)),
        ["tavern"] = (new Vector2(56, 72), new Color(0.40f, 0.24f, 0.30f)),
        ["mine-gate"] = (new Vector2(48, 48), new Color(0.18f, 0.16f, 0.22f)),
        ["noticeboard"] = (new Vector2(32, 48), new Color(0.36f, 0.30f, 0.20f)),
    };

    private static readonly Vector2 DefaultVenueSize = new(64, 64);
    private static readonly Color DefaultVenueColor = new(0.4f, 0.4f, 0.42f);

    /// <summary>Neutral body-sprite size for heroes/player (pivot plan asset-manifest row: 16×24) —
    /// bodies are drawn neutral-tinted so <see cref="HeroActor2D"/> can multiply in the class color
    /// via modulate (mirrors <see cref="IconRegistry.Sprite"/>'s own "bodies are neutral" contract).</summary>
    private static readonly Vector2 HeroPlaceholderSize = new(16, 24);

    private static readonly Color HeroPlaceholderColor = new(0.82f, 0.78f, 0.68f);

    /// <summary>
    /// Resolves a venue building's sprite: generated art first (<see cref="IconRegistry.Art"/>,
    /// manifest-backed — null until U6/U7 land it), else a flat-color placeholder box sized/tinted
    /// per <paramref name="spriteId"/> (falls back to a generic gray box for an unlisted id, e.g. a
    /// future venue added to <see cref="TownLayout2D"/> before its placeholder entry is).
    /// </summary>
    public static Texture2D ForVenue(string spriteId)
    {
        var art = IconRegistry.Art(spriteId);
        if (art is not null)
        {
            return art;
        }

        var (size, color) = VenuePlaceholders.TryGetValue(spriteId, out var entry)
            ? entry
            : (DefaultVenueSize, DefaultVenueColor);
        return Placeholder($"venue:{spriteId}", size, color);
    }

    /// <summary>
    /// Resolves a hero's neutral body sprite: a class-distinctive generated hero sprite
    /// ("hero-{classId}") first, then the hand-authored <see cref="IconRegistry.Sprite"/> SVG
    /// (already committed per-class art, used elsewhere e.g. <c>HeroesPanel</c>), then a flat
    /// neutral placeholder — the class tint itself is applied by the caller (<see
    /// cref="Town2D.ReconcileHeroes"/> passes <see cref="GodotClient.Ui.ClassColors.RoleColor"/>
    /// into <c>HeroActor2D.Init</c>'s own modulate), never baked in here.
    /// </summary>
    public static Texture2D ForHero(string classId)
    {
        // Prefer the 16×24 town body sprite (U6 pack): the bare "hero-{classId}" id resolves to the
        // 512×768 roster PORTRAIT used by HeroesPanel — mounting that in the 640×360 town SubViewport
        // renders a hero as a screen-filling portrait, not a walking body. Town bodies win here.
        var body = IconRegistry.Art($"town2d-hero-{classId}");
        if (body is not null)
        {
            return body;
        }

        var svg = IconRegistry.Sprite(classId);
        if (svg is not null)
        {
            return svg;
        }

        return Placeholder($"hero:{classId}", HeroPlaceholderSize, HeroPlaceholderColor);
    }

    /// <summary>The player smith's neutral body sprite — same ladder as <see cref="ForHero"/>
    /// minus the class tint (the player has no class color).</summary>
    public static Texture2D ForPlayer()
    {
        var art = IconRegistry.Art("player_smith");
        return art ?? Placeholder("player", HeroPlaceholderSize, new Color(0.35f, 0.4f, 0.55f));
    }

    private static Texture2D Placeholder(string cacheKey, Vector2 size, Color color)
    {
        if (PlaceholderCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var width = Mathf.Max(1, (int)size.X);
        var height = Mathf.Max(1, (int)size.Y);
        var image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
        image.Fill(color);
        var texture = ImageTexture.CreateFromImage(image);
        PlaceholderCache[cacheKey] = texture;
        return texture;
    }
}
