using System.Collections.Generic;
using Godot;

namespace GodotClient.Town2d;

// U3 (make-it-visible plan): the placeholder builder used to be a plain flat-colour box, on the
// theory that "reads as something, not nothing" was enough. It was not — the #316 incident was
// TownLayout2D.Venues pointing at a stale-but-real id, so ForVenue never even reached this class's
// Placeholder() path (IconRegistry.Art found a real, wrong PNG and returned it directly). But the
// SAME silent-degrade shape exists one level down: if an id is simply ABSENT (a typo, an
// unimported asset, a class with no committed body yet), this box is what a person actually sees,
// and a plausible-looking flat-tinted rectangle in a stylised town does not announce itself either
// — it just looks like flat programmer art, not a failure. Every fallback below now bakes in a
// magenta border + the missing id as tiny pixel text, and logs once, so THIS failure mode is loud
// even though the id-table failure mode (#316 itself) needed a different fix (the SET-pinned
// census test, godot/tests/AssetResolutionCensusTests.cs).

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
        // 2026-08-01 building-exterior receipt: reverted to the pre-#316 SDXL ids (see
        // TownLayout2D.Venues for the full trace). Sizes are the real PNG header dimensions
        // (measured, not guessed) -- the #316-era table for these same bare ids had approximate
        // sizes (e.g. tavern 56x72 when tavern.png is actually 84x88), harmless back then only
        // because ForVenue never fell through to this placeholder while the real PNG resolved.
        // Worth getting exactly right now that these ids are live again.
        ["forge"] = (new Vector2(72, 81), new Color(0.45f, 0.27f, 0.16f)),
        ["market"] = (new Vector2(76, 62), new Color(0.30f, 0.42f, 0.38f)),
        ["tavern"] = (new Vector2(84, 88), new Color(0.40f, 0.24f, 0.30f)),
        ["mine-gate"] = (new Vector2(48, 48), new Color(0.18f, 0.16f, 0.22f)),
        ["noticeboard"] = (new Vector2(44, 50), new Color(0.36f, 0.30f, 0.20f)),
    };

    private static readonly Vector2 DefaultVenueSize = new(64, 64);
    private static readonly Color DefaultVenueColor = new(0.4f, 0.4f, 0.42f);

    /// <summary>Per-prop placeholder footprint + tint — sizes match the real generated-art PNG
    /// dimensions 1:1 (verified against <c>godot/assets/art/town2d-*.png</c>'s PNG headers) so
    /// <see cref="TownLayout2D"/>'s hand-placed tile math holds whether or not the real art has
    /// landed yet.</summary>
    private static readonly Dictionary<string, (Vector2 Size, Color Color)> PropPlaceholders = new()
    {
        ["town2d-well"] = (new Vector2(32, 32), new Color(0.35f, 0.35f, 0.40f)),
        ["town2d-prop-lantern"] = (new Vector2(8, 16), new Color(0.85f, 0.65f, 0.25f)),
        ["town2d-prop-tree"] = (new Vector2(24, 32), new Color(0.16f, 0.34f, 0.18f)),
        ["town2d-prop-crate"] = (new Vector2(16, 16), new Color(0.5f, 0.35f, 0.2f)),
    };

    private static readonly Vector2 DefaultPropSize = new(16, 16);
    private static readonly Color DefaultPropColor = new(0.5f, 0.5f, 0.5f);

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
        return Placeholder($"venue:{spriteId}", size, color, spriteId);
    }

    /// <summary>
    /// Resolves a static prop's sprite (well/lantern/tree/crate): generated art first (<see
    /// cref="IconRegistry.Art"/>, same manifest-backed ladder <see cref="ForVenue"/> uses — these
    /// ids are already in the U6 pack, see <see cref="TownLayout2D.Props"/>'s doc), else a small
    /// flat-color placeholder box sized/tinted per <paramref name="spriteId"/> so a fresh checkout
    /// still reads as "a well/lantern/tree/crate", not a blank hole — mirrors <see cref="ForVenue"/>'s
    /// exact fallback ladder, just against the prop table instead of the venue one.
    /// </summary>
    public static Texture2D ForProp(string spriteId)
    {
        var art = IconRegistry.Art(spriteId);
        if (art is not null)
        {
            return art;
        }

        var (size, color) = PropPlaceholders.TryGetValue(spriteId, out var entry)
            ? entry
            : (DefaultPropSize, DefaultPropColor);
        return Placeholder($"prop:{spriteId}", size, color, spriteId);
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

        return Placeholder($"hero:{classId}", HeroPlaceholderSize, HeroPlaceholderColor, $"town2d-hero-{classId}");
    }

    /// <summary>The player smith's neutral body sprite — same ladder as <see cref="ForHero"/>
    /// minus the class tint (the player has no class color).</summary>
    public static Texture2D ForPlayer()
    {
        var art = IconRegistry.Art("player_smith");
        return art ?? Placeholder("player", HeroPlaceholderSize, new Color(0.35f, 0.4f, 0.55f), "player_smith");
    }

    /// <summary>
    /// Builds (and caches) the flat-colour fallback texture for a missing id — and, per the class
    /// doc above, makes sure it never passes for real art: a 1px magenta border plus <paramref
    /// name="missingId"/> baked in as tiny pixel text (see <see cref="DrawLoudMarkers"/>), and one
    /// <see cref="GD.PushWarning"/> + <see cref="PlaytestLog.Note"/> the FIRST time this exact id is
    /// built (the cache check above already gates repeat calls, so this never spams per-frame).
    ///
    /// <para>Deliberately pure CPU pixel-poking (<see cref="Image.SetPixel"/> only) — no
    /// <c>SubViewport</c>, no <c>CanvasItem</c> draw call, no font rasterisation. Anything that
    /// needs a rendered frame to produce a texture is the exact shape of the documented headless
    /// gdUnit hang (pumping frames while ANY <c>SubViewport</c> renders stalls the runner), and this
    /// method runs during ordinary scene construction inside the engine test suite — it must stay
    /// synchronous and frame-independent no matter how tempting a real font would be.</para>
    /// </summary>
    private static Texture2D Placeholder(string cacheKey, Vector2 size, Color color, string missingId)
    {
        if (PlaceholderCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var width = Mathf.Max(1, (int)size.X);
        var height = Mathf.Max(1, (int)size.Y);
        var image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
        image.Fill(color);
        DrawLoudMarkers(image, width, height, missingId);
        var texture = ImageTexture.CreateFromImage(image);
        PlaceholderCache[cacheKey] = texture;

        GD.PushWarning(
            $"[TownAssets2D] no committed art for '{missingId}' — showing a loud placeholder box "
            + "(magenta border + id text). This must never ship; see docs/plans/2026-08-01-001 U3.");
        PlaytestLog.Note($"placeholder-fallback:{missingId}");

        return texture;
    }

    /// <summary>Bright magenta — chosen because no colour in the style bible or any committed
    /// <c>town2d-*</c>/venue/prop/hero asset is anywhere near it (the opposite of the #520051
    /// magenta-roofed Forge, which happened to be a plausible ROOF colour; this one is deliberately
    /// implausible as anything but a warning).</summary>
    private static readonly Color LoudMarkerColor = new(1f, 0f, 1f);

    private const int BorderThicknessPx = 1;

    private const int GlyphWidth = 3;
    private const int GlyphHeight = 5;
    private const int GlyphAdvance = GlyphWidth + 1; // 1px gap between characters
    private const int LineAdvance = GlyphHeight + 1; // 1px gap between rows
    private const int TextMargin = 2; // inset so glyphs never draw over the border itself

    /// <summary>Draws the 1px magenta border + <paramref name="missingId"/> as tiny pixel text onto
    /// an already-filled placeholder image, in place.</summary>
    private static void DrawLoudMarkers(Image image, int width, int height, string missingId)
    {
        for (var x = 0; x < width; x++)
        {
            for (var t = 0; t < BorderThicknessPx; t++)
            {
                image.SetPixel(x, t, LoudMarkerColor);
                image.SetPixel(x, height - 1 - t, LoudMarkerColor);
            }
        }

        for (var y = 0; y < height; y++)
        {
            for (var t = 0; t < BorderThicknessPx; t++)
            {
                image.SetPixel(t, y, LoudMarkerColor);
                image.SetPixel(width - 1 - t, y, LoudMarkerColor);
            }
        }

        DrawTinyText(image, width, height, missingId);
    }

    /// <summary>
    /// Blits <paramref name="text"/> onto <paramref name="image"/> using <see cref="Glyph3X5"/>, a
    /// hand-authored 3×5 pixel font — wrapping to the next line when a glyph would cross the right
    /// margin, and simply stopping once there is no vertical room left. Every write is bounds-
    /// checked against the ACTUAL image dimensions (not just the margin), so even the smallest
    /// placeholder (a 16×16 crate box, say) degrades to a truncated or entirely absent label rather
    /// than an out-of-range <see cref="Image.SetPixel"/> — the border alone still reads as "this is
    /// wrong" at that size, which was always the primary signal; the text is the diagnostic bonus.
    /// An unmapped character (not in <see cref="Glyph3X5"/> — every id actually in play today is
    /// lowercase letters, digits and hyphens/underscores, but this must never throw on a surprise
    /// one) just advances the cursor and draws nothing, i.e. renders as a blank space.
    /// </summary>
    private static void DrawTinyText(Image image, int width, int height, string text)
    {
        var cursorX = TextMargin;
        var cursorY = TextMargin;

        foreach (var raw in text)
        {
            if (cursorX + GlyphWidth > width - TextMargin)
            {
                cursorX = TextMargin;
                cursorY += LineAdvance;
            }

            if (cursorY + GlyphHeight > height - TextMargin)
            {
                return; // out of vertical room entirely — a truncated label is still a label
            }

            if (cursorX + GlyphWidth <= width && cursorY + GlyphHeight <= height
                && Glyph3X5.TryGetValue(char.ToLowerInvariant(raw), out var rows))
            {
                for (var row = 0; row < GlyphHeight; row++)
                {
                    for (var col = 0; col < GlyphWidth; col++)
                    {
                        if (rows[row][col] == '1')
                        {
                            image.SetPixel(cursorX + col, cursorY + row, LoudMarkerColor);
                        }
                    }
                }
            }

            cursorX += GlyphAdvance;
        }
    }

    /// <summary>
    /// A minimal hand-authored 3-wide/5-tall pixel font — just legible enough, at the tiny sizes
    /// these placeholders render at, to read as "there is text here naming something" rather than
    /// decorative noise. Covers every character any committed sprite id actually uses (lowercase
    /// a-z, digits, '-', '_', ':') plus the rest of the alphabet/digits for headroom against a future
    /// id this table hasn't seen yet — an unmapped character just draws as blank space (see <see
    /// cref="DrawTinyText"/>), never a crash. Each entry is 5 rows of 3 characters, '1' = pixel on.
    /// </summary>
    private static readonly Dictionary<char, string[]> Glyph3X5 = new()
    {
        ['0'] = new[] { "111", "101", "101", "101", "111" },
        ['1'] = new[] { "010", "110", "010", "010", "111" },
        ['2'] = new[] { "111", "001", "111", "100", "111" },
        ['3'] = new[] { "111", "001", "111", "001", "111" },
        ['4'] = new[] { "101", "101", "111", "001", "001" },
        ['5'] = new[] { "111", "100", "111", "001", "111" },
        ['6'] = new[] { "111", "100", "111", "101", "111" },
        ['7'] = new[] { "111", "001", "010", "010", "010" },
        ['8'] = new[] { "111", "101", "111", "101", "111" },
        ['9'] = new[] { "111", "101", "111", "001", "111" },
        ['a'] = new[] { "010", "101", "111", "101", "101" },
        ['b'] = new[] { "110", "101", "110", "101", "110" },
        ['c'] = new[] { "011", "100", "100", "100", "011" },
        ['d'] = new[] { "110", "101", "101", "101", "110" },
        ['e'] = new[] { "111", "100", "111", "100", "111" },
        ['f'] = new[] { "111", "100", "111", "100", "100" },
        ['g'] = new[] { "011", "100", "101", "101", "011" },
        ['h'] = new[] { "101", "101", "111", "101", "101" },
        ['i'] = new[] { "111", "010", "010", "010", "111" },
        ['j'] = new[] { "001", "001", "001", "101", "010" },
        ['k'] = new[] { "101", "101", "110", "101", "101" },
        ['l'] = new[] { "100", "100", "100", "100", "111" },
        ['m'] = new[] { "101", "111", "111", "101", "101" },
        ['n'] = new[] { "101", "111", "111", "111", "101" },
        ['o'] = new[] { "010", "101", "101", "101", "010" },
        ['p'] = new[] { "110", "101", "110", "100", "100" },
        ['q'] = new[] { "010", "101", "101", "111", "011" },
        ['r'] = new[] { "110", "101", "110", "101", "101" },
        ['s'] = new[] { "011", "100", "010", "001", "110" },
        ['t'] = new[] { "111", "010", "010", "010", "010" },
        ['u'] = new[] { "101", "101", "101", "101", "011" },
        ['v'] = new[] { "101", "101", "101", "101", "010" },
        ['w'] = new[] { "101", "101", "111", "111", "101" },
        ['x'] = new[] { "101", "101", "010", "101", "101" },
        ['y'] = new[] { "101", "101", "010", "010", "010" },
        ['z'] = new[] { "111", "001", "010", "100", "111" },
        ['-'] = new[] { "000", "000", "111", "000", "000" },
        ['_'] = new[] { "000", "000", "000", "000", "111" },
        [':'] = new[] { "000", "010", "000", "010", "000" },
    };
}
