using System.Collections.Generic;
using Godot;

namespace GodotClient.Town3d;

/// <summary>
/// Kenney CC0 GLB lookup + runtime material fix for the 3D town — maps abstract building/hero
/// keys to <c>res://assets/models/kenney/...</c> scenes committed in T1, and repairs a texture
/// bug in every affected GLB along the way (see <see cref="ApplyColormapFallback"/>).
///
/// <para><b>The white-model bug:</b> every fantasy-town-kit/castle-kit/mini-characters GLB ships
/// a single <c>StandardMaterial3D</c> whose color comes ENTIRELY from an external
/// <c>Textures/colormap.png</c> atlas referenced by URI (no <c>baseColorFactor</c> — see each
/// kit's own glTF JSON). Godot 4.6.3's glTF importer silently fails to resolve that external-URI
/// texture reference for these particular files (confirmed by dumping the imported material at
/// runtime: <c>AlbedoTexture</c> is null, <c>AlbedoColor</c> is the untouched default (1,1,1,1) —
/// not an environment/tonemap issue, the material genuinely has no texture attached), so every
/// surface renders flat white. <c>nature-kit</c> is unaffected — its materials bake color via
/// <c>baseColorFactor</c> directly, no texture involved.
///
/// <para>Fix: load each kit's <c>Textures/colormap.png</c> ourselves and assign it as
/// <c>AlbedoTexture</c> on any surface material that's missing one, right after instantiation —
/// the mesh's own UVs are intact (only the importer's texture link is broken), so this recovers
/// the GENUINE Kenney per-part colors from the atlas rather than falling back to a flat tint.</para>
/// </summary>
public static class TownAssets
{
    public const string FantasyTownKit = "res://assets/models/kenney/fantasy-town-kit/";
    public const string CastleKit = "res://assets/models/kenney/castle-kit/";
    private const string MiniCharacters = "res://assets/models/kenney/mini-characters/";

    /// <summary>Kenney mini-characters GLBs are ~0.67 units tall in native mesh space;
    /// <c>Town3D.BuildPlayer</c>'s hand-picked capsule collider is 1.6 units tall (meant to read as
    /// human-height next to <see cref="BuildingScale"/>-sized buildings) — scaling the visual mesh
    /// by this factor brings it in line with that collider instead of standing as a tiny doll
    /// inside a much bigger hitbox.</summary>
    public const float CharacterScale = 2.4f;

    /// <summary>fantasy-town-kit wall/roof pieces sit on a 1-unit grid (a 1x1 room assembled from
    /// them is ~1.1 units across, wall thickness included); scaling an assembled building by this
    /// factor brings it to roughly <c>Building3D</c>'s 2.4-unit footprint box, so the visible walls
    /// line up with the invisible collision rather than floating tiny inside it.</summary>
    public const float BuildingScale = 2.2f;

    private static readonly string[] HeroVariantFiles =
    {
        "character-female-a.glb", "character-female-b.glb", "character-female-c.glb",
        "character-female-d.glb", "character-female-e.glb", "character-female-f.glb",
        "character-male-a.glb", "character-male-b.glb", "character-male-c.glb",
        "character-male-d.glb", "character-male-e.glb", "character-male-f.glb",
    };

    private static readonly Dictionary<string, Texture2D?> ColormapCache = new();

    /// <summary>Instantiates hero <paramref name="variant"/>'s Kenney mesh, colormap-fixed and
    /// pre-scaled to <see cref="CharacterScale"/> — null only when the asset itself is missing
    /// (callers fall back to a tinted primitive capsule).</summary>
    public static Node3D? InstantiateHero(int variant)
    {
        var count = HeroVariantFiles.Length;
        var index = ((variant % count) + count) % count;
        var node = Instantiate(MiniCharacters, HeroVariantFiles[index]);
        if (node == null)
        {
            return null;
        }

        node.Scale = new Vector3(CharacterScale, CharacterScale, CharacterScale);
        return node;
    }

    /// <summary>The assembled, colormap-fixed, pre-scaled building mesh for a
    /// <c>Building3D</c> key — see <see cref="BuildingKit"/> for the per-venue assemblies. Never
    /// null (every venue has a deterministic fallback all the way down to a single primitive
    /// piece); <c>Building3D</c>'s own wedge fallback is reserved for the "no scene passed at all"
    /// test seam.</summary>
    public static Node3D BuildBuilding(string key) => BuildingKit.Build(key);

    /// <summary>
    /// Instantiates one Kenney GLB piece from <paramref name="kitFolder"/> and applies the
    /// colormap-fallback fix (see class doc) — the single entry point <see cref="BuildingKit"/>
    /// and <see cref="InstantiateHero"/> both go through, so every Kenney mesh in the town gets
    /// the same texture repair. Returns null only when the asset itself is missing/renamed.
    /// </summary>
    public static Node3D? Instantiate(string kitFolder, string fileName)
    {
        var path = kitFolder + fileName;
        if (!ResourceLoader.Exists(path))
        {
            return null;
        }

        var scene = ResourceLoader.Load<PackedScene>(path);
        var node = scene.Instantiate<Node3D>();
        ApplyColormapFallback(node, ColormapFor(kitFolder));
        return node;
    }

    /// <summary>Adds a small warm point light near the top of a "lantern.glb" instance — the mesh
    /// alone (now correctly colour-textured, see class doc) still reads as a grey/plain post from
    /// a distance without an actual glow, so every placed lantern (street props in
    /// <c>Town3D.BuildProps</c>, the door lantern in <see cref="BuildingKit"/>'s cottages) gets one
    /// of these as a child so it lines up with the mesh regardless of its parent's scale.</summary>
    public static void AttachLanternGlow(Node3D lanternPiece)
    {
        lanternPiece.AddChild(new OmniLight3D
        {
            Name = "Glow",
            Position = new Vector3(0f, 1.3f, 0f),
            LightColor = new Color(1f, 0.82f, 0.45f),
            LightEnergy = 0.9f,
            OmniRange = 1.6f,
        });
    }

    private static Texture2D? ColormapFor(string kitFolder)
    {
        if (ColormapCache.TryGetValue(kitFolder, out var cached))
        {
            return cached;
        }

        var path = kitFolder + "Textures/colormap.png";
        var texture = ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;
        ColormapCache[kitFolder] = texture;
        return texture;
    }

    /// <summary>Recursively walks <paramref name="root"/>'s mesh surfaces, assigning
    /// <paramref name="colormap"/> as <c>AlbedoTexture</c> wherever the imported material is
    /// missing one — a no-op on any surface that already resolved a texture correctly (never
    /// clobbers a working material), and a total no-op when <paramref name="colormap"/> itself
    /// couldn't load.</summary>
    private static void ApplyColormapFallback(Node root, Texture2D? colormap)
    {
        if (colormap == null)
        {
            return;
        }

        if (root is MeshInstance3D mesh && mesh.Mesh != null)
        {
            var surfaceCount = mesh.Mesh.GetSurfaceCount();
            for (var i = 0; i < surfaceCount; i++)
            {
                if (mesh.GetActiveMaterial(i) is StandardMaterial3D std && std.AlbedoTexture == null)
                {
                    std.AlbedoTexture = colormap;
                }
            }
        }

        foreach (var child in root.GetChildren())
        {
            ApplyColormapFallback(child, colormap);
        }
    }
}
