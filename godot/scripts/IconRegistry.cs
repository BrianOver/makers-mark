using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using GameSim.Contracts;
using GameSim.Economy;
using GodotClient.Tools;

namespace GodotClient;

/// <summary>
/// Maps sim concepts to their themed icon textures (U15). One lookup point so panels
/// and the town scene bind art by concept, not by hardcoded paths. Icons are the
/// hand-authored SVGs under res://assets/icons/ (style bible palette); generated art
/// (portraits, monsters, backdrop) lives under res://assets/art/ and is loaded by name.
///
/// <para>U3 (P006, R10) adds a manifest-backed presence check (<see cref="Has"/>/
/// <see cref="HasNormal"/>) over <c>res://assets/art/art-manifest.json</c> — the generated
/// "what exists" list from <c>art/pipeline/gen-manifest.ps1</c> — so callers (chiefly
/// <see cref="AssetCatalog"/>) can ask "is this id committed?" without a per-call filesystem
/// probe. <see cref="Art"/>/<see cref="Lit"/> remain the single id→path load point and are
/// unchanged: still null-tolerant against the actual resource filesystem, still what
/// <see cref="AssetCatalog"/> delegates to for the real load.</para>
/// </summary>
public static class IconRegistry
{
    private const string IconDir = "res://assets/icons";
    private const string SpriteDir = "res://assets/sprites";
    private const string ArtDir = "res://assets/art";
    private const string ManifestPath = ArtDir + "/art-manifest.json";

    /// <summary>One manifest entry (U3): whether an id has a committed diffuse and/or normal PNG.
    /// Generated from committed pixels by <c>gen-manifest.ps1</c> — never from GameState (R14).</summary>
    public readonly record struct ManifestEntry(bool Diffuse, bool Normal);

    private static Dictionary<string, ManifestEntry>? _manifestCache;

    public static Texture2D Slot(ItemSlot slot) => Load(IconDir, slot switch
    {
        ItemSlot.Weapon => "weapon",
        ItemSlot.Shield => "shield",
        ItemSlot.Armor => "armor",
        _ => "weapon",
    });

    /// <summary>
    /// U7 (proof-the-player-never-sees plan, 2026-08-08): the rival shelf's CATEGORY icon id —
    /// one hand-painted sprite per <see cref="ItemSlot"/> (weapon/shield/armor), not one per
    /// synthetic <c>GameSim.Economy.RivalCatalog</c> entry id. That catalog is fixed data
    /// RivalRestockSystem mints instances from — binding art to the slot instead of the specific
    /// recipe id (<c>"rival-blade-1"</c>, <c>"rival-shield-2"</c>, ...) means a future catalog
    /// addition resolves to real, already-painted art the moment it exists, since every entry
    /// already carries a slot and there are only three. See art/pipeline/gen-rival-icons.py for
    /// the three sprites this composes onto (<c>item-rival-weapon/-shield/-armor</c>).
    /// </summary>
    public static string RivalCategoryArtId(ItemSlot slot) => slot switch
    {
        ItemSlot.Weapon => "item-rival-weapon",
        ItemSlot.Shield => "item-rival-shield",
        ItemSlot.Armor => "item-rival-armor",
        _ => "item-rival-weapon",
    };

    /// <summary>
    /// U1 (loud-failures-and-quiet-channels plan): mirrors <see cref="Art"/>'s
    /// <see cref="ResourceLoader.Exists"/> existence guard, which this lookup never had — before
    /// this unit, <c>Load</c> called <c>GD.Load</c> straight against
    /// <c>res://assets/icons/ore_{materialKey}.svg</c> for every <c>MaterialRegistry.PricedPool</c>
    /// key, and a missing SVG was a native <c>core/io/resource_loader.cpp</c> ERROR at runtime (one
    /// real playtest run logged 260 of them across 5 distinct missing ids) rather than a graceful
    /// fallback — see <c>AssetResolutionCensusTests.PricedOreMaterials_ResolveTheirVendorIcon</c>
    /// (godot/tests, a different assembly — not cref-able from here) for the pinning test. A miss
    /// now degrades to a small placeholder swatch (never null — <c>ForgePanel</c>'s vendor shelf
    /// hands this straight to
    /// <c>UiKit.ListRow</c>'s icon slot, which is null-tolerant, but a swatch reads as "something is
    /// wrong here" the way a blank slot would not) and logs exactly once per missing key via
    /// <see cref="EngineDistress.Warn"/>, the same "announce the degrade" contract
    /// <c>TownAssets2D.Placeholder</c> already established for generated art.
    /// </summary>
    public static Texture2D Ore(string materialKey)
    {
        var path = $"{IconDir}/ore_{materialKey}.svg";
        return ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : OrePlaceholder(materialKey, path);
    }

    private static readonly Dictionary<string, Texture2D> OrePlaceholderCache = new();

    /// <summary>Loud enough to notice, cheap enough to build inline: a flat magenta swatch (the
    /// same "implausible as anything but a warning" color <c>TownAssets2D.LoudMarkerColor</c>
    /// uses) rather than pulling in that class's full bordered/lettered placeholder machinery for
    /// a 24px vendor-row icon, where a hand-drawn 3x5 font would be illegible anyway — the real
    /// diagnostic is the <see cref="EngineDistress"/> message, not the pixels. Cached per key so a
    /// vendor shelf re-rendered every refresh does not rebuild the same swatch every frame.</summary>
    private static Texture2D OrePlaceholder(string materialKey, string path)
    {
        if (OrePlaceholderCache.TryGetValue(materialKey, out var cached))
        {
            return cached;
        }

        var image = Image.CreateEmpty(16, 16, false, Image.Format.Rgba8);
        image.Fill(new Color(1f, 0f, 1f));
        var texture = ImageTexture.CreateFromImage(image);
        OrePlaceholderCache[materialKey] = texture;

        EngineDistress.Warn(
            $"[IconRegistry] no committed ore icon for '{materialKey}' at {path} — showing a "
            + "placeholder swatch instead of letting a missing SVG spam native resource-loader errors.");

        return texture;
    }

    /// <summary>Rival catalog recipe ids, read off the sim's own table rather than hand-listed, so a
    /// future rival line is covered the day it lands. Adapter-side only — no sim edit needed, since
    /// <c>RivalCatalog.Entries</c> is already public data.</summary>
    private static readonly HashSet<string> RivalRecipeIds =
        RivalCatalog.Entries.Select(e => e.RecipeId).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// The art id for an item a player can actually SEE, given its recipe and slot. Per-recipe art
    /// normally (<c>item-&lt;recipeId&gt;</c>); the slot's CATEGORY sprite for a rival catalog line.
    ///
    /// <para><b>Why this exists as well as <see cref="RivalCategoryArtId"/>.</b> U7 made the rival
    /// SHELF use category art, because a synthetic catalog key like <c>"rival-blade-2"</c> has no
    /// committed art and, per that ruling, never will. But rival goods do not stay on the shelf —
    /// heroes buy them, wear them, and die in them, so the same items reappear on the roster card
    /// and the tavern's gear list, and those two surfaces still composed the per-recipe id. Result:
    /// every hero carrying rival kit showed a captioned placeholder box where its icon belongs.
    /// Measured, not theorised: a five-run <c>FullPlaytest</c> reported
    /// <c>item-rival-blade-2</c> and <c>item-rival-shield-1</c> as art misses the moment
    /// <c>UiKit.ArtRect</c> started logging them — the previous run of the same tool, on the same
    /// build minus that log, reported the game clean.</para>
    ///
    /// <para>Deliberately narrow: ONLY rival ids redirect. Any other item whose art is missing keeps
    /// hitting the placeholder and its warning, because that is a real gap someone should fix — the
    /// six forward-ladder recipes were exactly that, and a blanket "fall back to the slot glyph"
    /// rule would have hidden them for another few months.</para>
    /// </summary>
    public static string ItemArtId(string recipeId, ItemSlot slot) =>
        RivalRecipeIds.Contains(recipeId) ? RivalCategoryArtId(slot) : AssetCatalog.ItemIconId(recipeId);

    public static Texture2D Glyph(string name) => Load(IconDir, name); // gold, bounty, gossip, depths, skull, rune

    /// <summary>
    /// Hand-authored hero figure per class (U16, P3). The asset is <c>hero_{classId}.svg</c>
    /// (e.g. hero_vanguard), so a new class ships its figure by naming the file after its id.
    /// Bodies are neutral so the town can tint them to the class color via
    /// <c>TextureRect.Modulate</c>; see the style bible.
    /// </summary>
    public static Texture2D Sprite(string classId) => Load(SpriteDir, "hero_" + classId);

    /// <summary>Generated art by base file name/id (e.g. "hero-mystic", "monster-cave-rat"); null
    /// until the pipeline has generated it. The single id→path load point — <see cref="Lit"/> and
    /// <see cref="AssetCatalog"/>'s resolvers both compose an id string and call through here.</summary>
    public static Texture2D? Art(string name)
    {
        var path = ArtPath(name);
        return ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
    }

    /// <summary>Diffuse+normal CanvasTexture for a generated art id (2.5D path). Null-tolerant:
    /// null when the diffuse is absent (caller falls back to the SVG placeholder); a missing
    /// _n sibling yields a diffuse-only CanvasTexture (lights work, normals just read flat).</summary>
    public static CanvasTexture? Lit(string id)
    {
        var diffuse = Art(id);
        return diffuse is null ? null
            : new CanvasTexture { DiffuseTexture = diffuse, NormalTexture = Art(id + "_n") };
    }

    /// <summary>True iff the generated manifest lists <paramref name="id"/> (any pixels committed
    /// for it). Manifest-backed so repeated presence checks (e.g. Plan #3 enumeration) don't hit
    /// the filesystem per call — the manifest is loaded once and cached for the process lifetime.</summary>
    public static bool Has(string id) => Manifest().ContainsKey(id);

    /// <summary>
    /// P007 U2 art-loader bridge single entry point: <see cref="Has"/>'s manifest fast-path
    /// gates the actual <see cref="Art"/> load, so an id absent from the manifest never even
    /// probes the resource filesystem. True + a non-null texture on hit; false + null on any
    /// miss (unlisted id, or listed but somehow unloadable) — never throws, mirroring every
    /// other lookup on this type.
    /// </summary>
    public static bool TryArt(string id, out Texture2D? texture)
    {
        texture = Has(id) ? Art(id) : null;
        return texture is not null;
    }

    /// <summary>True iff the manifest lists a committed normal map for <paramref name="id"/>;
    /// false for an absent id or a diffuse-only entry (e.g. a flat item icon or backdrop).</summary>
    public static bool HasNormal(string id) => Manifest().TryGetValue(id, out var entry) && entry.Normal;

    /// <summary>Pure parse of the manifest JSON shape — <c>{"&lt;id&gt;": {"diffuse": bool,
    /// "normal": bool}}</c> — with no I/O, so tests can prove manifest fidelity (including a
    /// diffuse-only entry) against a synthetic fixture without touching the committed file or the
    /// Godot resource filesystem. Malformed/missing flags default to <c>false</c>, never throw.</summary>
    public static Dictionary<string, ManifestEntry> ParseManifest(string json)
    {
        var result = new Dictionary<string, ManifestEntry>();
        if (string.IsNullOrWhiteSpace(json)) return result;

        using var doc = JsonDocument.Parse(json);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            var diffuse = prop.Value.TryGetProperty("diffuse", out var d) && d.ValueKind == JsonValueKind.True;
            var normal = prop.Value.TryGetProperty("normal", out var n) && n.ValueKind == JsonValueKind.True;
            result[prop.Name] = new ManifestEntry(diffuse, normal);
        }
        return result;
    }

    /// <summary>
    /// The manifest, loaded once and cached. Both degrade paths below announce themselves — an
    /// absent or corrupt manifest is the single loudest failure this client can suffer quietly,
    /// because <see cref="Has"/> gates <see cref="TryArt"/>, so "nothing present" means EVERY
    /// generated texture in the game silently becomes a placeholder box at once. `ASSETS.md`
    /// carried this as a known open risk ("quieter than the audio equivalent, which warns. Not
    /// closed; not yet bitten") until the ladder-icon batch showed what a quiet art miss actually
    /// costs: six recipes shipped iconless and no playtest run ever reported it.
    /// Warning here (rather than at each caller) fires at most once per process because of the
    /// cache, and `EngineLogAnomalies.Scan` turns it into a real playtest anomaly.
    /// </summary>
    private static Dictionary<string, ManifestEntry> Manifest()
    {
        if (_manifestCache is not null) return _manifestCache;

        try
        {
            if (Godot.FileAccess.FileExists(ManifestPath))
            {
                _manifestCache = ParseManifest(
                    Godot.FileAccess.Open(ManifestPath, Godot.FileAccess.ModeFlags.Read).GetAsText());
            }
            else
            {
                EngineDistress.Warn(
                    $"[IconRegistry] no art manifest at {ManifestPath} — EVERY generated texture will "
                    + "resolve as absent and draw a placeholder. Run art/pipeline/gen-manifest.ps1; a "
                    + "partial checkout or a missed regeneration is the usual cause.");
                _manifestCache = new Dictionary<string, ManifestEntry>();
            }
        }
        catch (JsonException ex)
        {
            // A corrupted manifest degrades to "nothing present" rather than crashing the UI —
            // the same null-tolerant contract Art/Lit already give callers — but it says so now.
            EngineDistress.Warn(
                $"[IconRegistry] art manifest at {ManifestPath} would not parse ({ex.GetType().Name}: "
                + $"{ex.Message}) — degrading to 'nothing present', so EVERY generated texture will "
                + "draw a placeholder. Regenerate it with art/pipeline/gen-manifest.ps1.");
            _manifestCache = new Dictionary<string, ManifestEntry>();
        }
        return _manifestCache;
    }

    private static string ArtPath(string name) => $"{ArtDir}/{name}.png";

    private static Texture2D Load(string dir, string name) => GD.Load<Texture2D>($"{dir}/{name}.svg");
}
