#if GDUNIT_TESTS
using GdUnit4;
using GameSim.Contracts;
using Godot;
using GodotClient;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>Proves the U15 icon pipeline: every themed SVG imports and loads as a texture.</summary>
[TestSuite]
[RequireGodotRuntime]
public class IconRegistryTests
{
    [TestCase]
    public void EverySlotIcon_Loads()
    {
        foreach (ItemSlot slot in System.Enum.GetValues<ItemSlot>())
        {
            AssertThat(IconRegistry.Slot(slot)).IsNotNull();
        }
    }

    [TestCase]
    public void EveryOreIcon_Loads()
    {
        foreach (var mat in new[] { "copper", "iron", "steel", "mithril", "adamant" })
        {
            AssertThat(IconRegistry.Ore(mat)).IsNotNull();
        }
    }

    [TestCase]
    public void EveryGlyph_Loads()
    {
        foreach (var g in new[] { "gold", "bounty", "gossip", "depths", "skull", "rune" })
        {
            AssertThat(IconRegistry.Glyph(g)).IsNotNull();
        }
    }

    [TestCase]
    public void EveryHeroSprite_Loads()
    {
        // P3: iterate the registered classes instead of the removed role enum — every
        // built-in class ships a hero_{id}.svg figure.
        foreach (var classId in GameSim.Classes.ClassRegistry.All.Keys)
        {
            AssertThat(IconRegistry.Sprite(classId)).IsNotNull();
        }
    }

    [TestCase]
    public void EveryBuilding_Loads()
    {
        foreach (var name in new[] { "forge", "shop", "tavern", "mine_gate", "memorial_stone", "ground_tile" })
        {
            AssertThat(IconRegistry.Building(name)).IsNotNull();
        }
    }

    [TestCase]
    public void GeneratedArt_AbsentUntilGenerated_ReturnsNull()
    {
        // Art PNGs are produced by the local ComfyUI pipeline (docs/design/art-pipeline-architecture.md)
        // and committed later; the registry must degrade gracefully until then.
        AssertThat(IconRegistry.Art("does_not_exist_yet")).IsNull();
    }
}
#endif
