using Godot;
using GameSim.Contracts;

namespace GodotClient;

/// <summary>
/// Maps sim concepts to their themed icon textures (U15). One lookup point so panels
/// and the town scene bind art by concept, not by hardcoded paths. Icons are the
/// hand-authored SVGs under res://assets/icons/ (style bible palette); generated art
/// (portraits, monsters, backdrop) lives under res://assets/art/ and is loaded by name.
/// </summary>
public static class IconRegistry
{
    private const string IconDir = "res://assets/icons";
    private const string ArtDir = "res://assets/art";

    public static Texture2D Slot(ItemSlot slot) => Load(IconDir, slot switch
    {
        ItemSlot.Weapon => "weapon",
        ItemSlot.Shield => "shield",
        ItemSlot.Armor => "armor",
        _ => "weapon",
    });

    public static Texture2D Ore(string materialKey) => Load(IconDir, $"ore_{materialKey}");

    public static Texture2D Glyph(string name) => Load(IconDir, name); // gold, bounty, gossip, depths, skull

    /// <summary>Generated art by base file name (e.g. "hero_mystic", "monster_floor5"); null until U15's generator has run.</summary>
    public static Texture2D? Art(string name)
    {
        var path = $"{ArtDir}/{name}.png";
        return ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
    }

    private static Texture2D Load(string dir, string name) => GD.Load<Texture2D>($"{dir}/{name}.svg");
}
