using System.Collections.Generic;
using Godot;

namespace GodotClient.Town3d;

/// <summary>
/// T3: Kenney CC0 GLB lookup for the 3D town — maps abstract building/hero keys to
/// <c>res://assets/models/kenney/...</c> scene paths committed in T1. Every lookup is guarded
/// by <see cref="ResourceLoader.Exists"/>, so a missing or renamed asset degrades to a null
/// return rather than an <see cref="ResourceLoader.Load"/> exception at runtime — callers (T4+
/// <c>Town3D</c>/<c>Building3D</c>/<c>HeroActor3D</c>) fall back to a primitive placeholder
/// mesh when this returns null.
///
/// <para>Building keys below are provisional: the Fantasy Town Kit is a modular wall/roof kit
/// with no single "forge"/"tavern" prefab, so each key currently points at one representative
/// piece as a stand-in footprint mesh. T5 (Buildings + WorldInput3D) finalizes the real
/// per-building assemblies/lookups once <c>Building3D</c> lands.</para>
/// </summary>
public static class TownAssets
{
    private const string FantasyTownKit = "res://assets/models/kenney/fantasy-town-kit/";
    private const string MiniCharacters = "res://assets/models/kenney/mini-characters/";

    private static readonly Dictionary<string, string> BuildingPaths = new()
    {
        ["market"] = FantasyTownKit + "stall-red.glb",
        ["tavern"] = FantasyTownKit + "stall-green.glb",
        ["forge"] = FantasyTownKit + "stall.glb",
        ["minegate"] = FantasyTownKit + "wall-arch.glb",
        ["noticeboard"] = FantasyTownKit + "wall-detail-cross.glb",
    };

    // 6 female + 6 male Kenney "mini-characters" meshes, indexed 0-11.
    private static readonly string[] HeroVariantFiles =
    {
        "character-female-a.glb", "character-female-b.glb", "character-female-c.glb",
        "character-female-d.glb", "character-female-e.glb", "character-female-f.glb",
        "character-male-a.glb", "character-male-b.glb", "character-male-c.glb",
        "character-male-d.glb", "character-male-e.glb", "character-male-f.glb",
    };

    public static PackedScene? BuildingScene(string key) =>
        BuildingPaths.TryGetValue(key, out var path) ? LoadIfExists(path) : null;

    public static PackedScene? HeroScene(int variant)
    {
        var count = HeroVariantFiles.Length;
        var index = ((variant % count) + count) % count;
        return LoadIfExists(MiniCharacters + HeroVariantFiles[index]);
    }

    private static PackedScene? LoadIfExists(string path) =>
        ResourceLoader.Exists(path) ? ResourceLoader.Load<PackedScene>(path) : null;
}
