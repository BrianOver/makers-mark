using System.Collections.Generic;
using System.Text.Json;
using Godot;
using GameSim.Contracts;

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

    public static Texture2D Ore(string materialKey) => Load(IconDir, $"ore_{materialKey}");

    public static Texture2D Glyph(string name) => Load(IconDir, name); // gold, bounty, gossip, depths, skull, rune

    /// <summary>
    /// Hand-authored hero figure per class (U16, P3). The asset is <c>hero_{classId}.svg</c>
    /// (e.g. hero_vanguard), so a new class ships its figure by naming the file after its id.
    /// Bodies are neutral so the town can tint them to the class color via
    /// <c>TextureRect.Modulate</c>; see the style bible.
    /// </summary>
    public static Texture2D Sprite(string classId) => Load(SpriteDir, "hero_" + classId);

    /// <summary>
    /// Hand-authored town facade / prop by key (U16): forge, shop, tavern, mine_gate,
    /// memorial_stone, ground_tile.
    /// </summary>
    public static Texture2D Building(string name) => Load(SpriteDir, name);

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

    private static Dictionary<string, ManifestEntry> Manifest()
    {
        if (_manifestCache is not null) return _manifestCache;

        try
        {
            _manifestCache = Godot.FileAccess.FileExists(ManifestPath)
                ? ParseManifest(Godot.FileAccess.Open(ManifestPath, Godot.FileAccess.ModeFlags.Read).GetAsText())
                : new Dictionary<string, ManifestEntry>();
        }
        catch (JsonException)
        {
            // A corrupted manifest degrades to "nothing present" rather than crashing the UI —
            // the same null-tolerant contract Art/Lit already give callers.
            _manifestCache = new Dictionary<string, ManifestEntry>();
        }
        return _manifestCache;
    }

    private static string ArtPath(string name) => $"{ArtDir}/{name}.png";

    private static Texture2D Load(string dir, string name) => GD.Load<Texture2D>($"{dir}/{name}.svg");
}
