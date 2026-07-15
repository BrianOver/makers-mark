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
        foreach (var g in new[] { "gold", "bounty", "gossip", "depths", "skull" })
        {
            AssertThat(IconRegistry.Glyph(g)).IsNotNull();
        }
    }

    [TestCase]
    public void GeneratedArt_AbsentUntilGenerated_ReturnsNull()
    {
        // Art PNGs are produced by tools/AssetGen (needs GEMINI_API_KEY) and committed
        // later; the registry must degrade gracefully until then.
        AssertThat(IconRegistry.Art("does_not_exist_yet")).IsNull();
    }
}
#endif
