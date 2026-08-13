#if GDUNIT_TESTS
using System.Linq;
using GdUnit4;
using GameSim.Contracts;
using GodotClient;
using GodotClient.Tools;
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

    /// <summary>
    /// U1 (loud-failures-and-quiet-channels plan): before this unit, <c>IconRegistry.Ore</c> called
    /// <c>GD.Load</c> straight against a missing SVG's path — a native
    /// <c>core/io/resource_loader.cpp</c> ERROR at runtime (one real playtest run logged 260 of
    /// them across 5 distinct missing ids), never a graceful placeholder. This proves the guarded
    /// path: a material key with no committed <c>ore_*.svg</c> resolves to a real, non-null
    /// texture (never null — <c>ForgePanel</c>'s vendor shelf hands this straight into
    /// <c>UiKit.ListRow</c>'s icon slot) and records exactly one <see cref="EngineDistress"/> message
    /// naming the missing key, which is the evidence that the guarded (existence-checked) path was
    /// taken instead of the raw <c>GD.Load</c> call that used to spam the native loader.
    /// </summary>
    [TestCase]
    public void Ore_MissingIcon_ResolvesToAPlaceholder_RecordsOneDistressMessage_NeverNull()
    {
        EngineDistress.ResetForTests();

        var texture = IconRegistry.Ore("does-not-exist-as-an-ore");

        AssertThat(texture)
            .OverrideFailureMessage(
                "IconRegistry.Ore('does-not-exist-as-an-ore') returned null instead of a placeholder — "
                + "a null icon reaching ForgePanel's vendor shelf is exactly the silent-degrade shape "
                + "this unit closes.")
            .IsNotNull();

        AssertThat(EngineDistress.Messages.Count(m => m.Contains("does-not-exist-as-an-ore")))
            .OverrideFailureMessage(
                $"Expected exactly one EngineDistress message naming the missing ore key; got "
                + $"[{string.Join(" | ", EngineDistress.Messages)}]. Zero means the guard never ran "
                + "(or never warned); the guard's whole job is announcing the degrade.")
            .IsEqual(1);
    }

    /// <summary>The healthy path must not change: a real, committed material key still resolves
    /// straight through — no placeholder, no distress message.</summary>
    [TestCase]
    public void Ore_CommittedIcon_ResolvesDirectly_NoDistressMessage()
    {
        EngineDistress.ResetForTests();

        var texture = IconRegistry.Ore("copper");

        AssertThat(texture).IsNotNull();
        AssertThat(EngineDistress.Messages.Any(m => m.Contains("copper")))
            .OverrideFailureMessage("A committed ore icon should never trip the placeholder warning.")
            .IsFalse();
    }

    [TestCase]
    public void GeneratedArt_AbsentUntilGenerated_ReturnsNull()
    {
        // Art PNGs are produced by the local ComfyUI pipeline (docs/design/art-pipeline-architecture.md)
        // and committed later; the registry must degrade gracefully until then.
        AssertThat(IconRegistry.Art("does_not_exist_yet")).IsNull();
    }

    [TestCase]
    public void Lit_AbsentDiffuse_ReturnsNull()
    {
        // V4a (plan 2026-07-17-003 §V4a): the 2.5D lit lookup mirrors Art's null-tolerance —
        // no diffuse PNG means null, so the town falls back to the SVG placeholder.
        AssertThat(IconRegistry.Lit("does_not_exist_yet")).IsNull();
    }

    [TestCase]
    public void Lit_ShippedPair_ReturnsCanvasTextureWithDiffuseAndNormal()
    {
        // V4a: the shipped pilot pair (town-tavern.png + town-tavern_n.png) resolves to a
        // CanvasTexture carrying BOTH the diffuse and the normal — the input a lit Sprite2D
        // needs (proven by lit_tavern_pilot.tscn).
        var lit = IconRegistry.Lit("town-tavern");
        AssertThat(lit).IsNotNull();
        AssertThat(lit!.DiffuseTexture).IsNotNull();
        AssertThat(lit.NormalTexture).IsNotNull();
    }

}
#endif
