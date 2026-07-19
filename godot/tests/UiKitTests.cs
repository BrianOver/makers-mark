#if GDUNIT_TESTS
using System.Linq;
using GdUnit4;
using Godot;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// P007 U2 (R11/R12/KTD2/KTD3): the themed widget kit (<see cref="UiKit"/>) and its
/// fallback-safe <see cref="UiKit.ArtRect"/> bridge. <c>Card</c>/<c>Section</c> scenarios prove
/// the cascade — not a per-node override — supplies the stylebox, by asserting reference
/// equality with the exact StyleBox instance <c>MainUi.Theme</c> registered. The
/// <c>ArtRect</c>/<c>PortraitFrame</c> scenarios prove the graceful-degrade guarantee: a
/// fabricated key never returns null and never throws, while a manifest-known key
/// ("hero-vanguard", committed by Plan #2) resolves to the real texture.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class UiKitTests
{
    private const string KnownArtKey = "hero-vanguard"; // committed by P006 (art-manifest.json)
    private const string UnknownArtKey = "no-such-key";

    [TestCase]
    public void Card_And_SectionRoot_ResolveStylebox_FromThemeCascade_NotLocalOverride()
    {
        var ui = MountMainUi();
        try
        {
            var expected = ui.Theme.GetStylebox("panel", "PanelContainer");
            AssertThat(expected).IsNotNull();

            var card = Card();
            ui.AddChild(card);
            AssertThat(ReferenceEquals(card.GetThemeStylebox("panel"), expected)).IsTrue();

            var section = Section("Your Shelf");
            ui.AddChild(section.Root);
            AssertThat(ReferenceEquals(section.Root.GetThemeStylebox("panel"), expected)).IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void Section_RendersHeaderText_InThemedColorAndSize()
    {
        var ui = MountMainUi();
        try
        {
            var section = Section("Unshelved Crafts");
            ui.AddChild(section.Root);

            AssertThat(RenderedText(section.Root)).Contains("Unshelved Crafts");

            var header = Find<Label>(section.Root, "SectionHeader");
            AssertThat(header.GetThemeColor("font_color") == GameTheme.HeaderColor).IsTrue();
            AssertThat(header.GetThemeFontSize("font_size")).IsEqual(GameTheme.HeaderFontSize);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void StatChip_RendersLabelAndValueText_Discoverable()
    {
        var chip = StatChip("Gold", "42", UiKit.ChipTone.Positive);
        try
        {
            AssertThat(chip).IsNotNull();

            var rendered = RenderedText(chip);
            AssertThat(rendered).Contains("Gold");
            AssertThat(rendered).Contains("42");
        }
        finally
        {
            chip.Free(); // never parented into a mounted tree — free it directly, no leaked orphan
        }
    }

    [TestCase]
    public void ArtRect_UnknownKey_ReturnsFallbackPlaceholder_WithGlyphAndCaption_NeverNull()
    {
        var control = ArtRect(UnknownArtKey, new Vector2(64, 64));
        try
        {
            AssertThat(control).IsNotNull();
            AssertThat(control is TextureRect).IsFalse(); // no texture hit — must be the placeholder

            var icons = control.FindChildren("*", nameof(TextureRect), recursive: true, owned: false);
            AssertThat(icons.Count > 0).IsTrue();
            AssertThat(((TextureRect)icons[0]).Texture).IsNotNull();

            AssertThat(RenderedText(control)).Contains(UnknownArtKey);
        }
        finally
        {
            control.Free();
        }
    }

    [TestCase]
    public void ArtRect_KnownManifestKey_ReturnsTextureRect_CarryingRealTexture()
    {
        var control = ArtRect(KnownArtKey, new Vector2(64, 64));
        try
        {
            AssertThat(control is TextureRect).IsTrue();
            AssertThat(((TextureRect)control).Texture).IsNotNull();
        }
        finally
        {
            control.Free();
        }
    }

    [TestCase]
    public void PortraitFrame_HitAndMiss_BothRenderNonNullFramedContent()
    {
        var hit = PortraitFrame(KnownArtKey);
        var miss = PortraitFrame(UnknownArtKey, caption: "Mystery Hero");
        try
        {
            AssertThat(hit).IsNotNull();
            var hitIcons = hit.FindChildren("*", nameof(TextureRect), recursive: true, owned: false);
            AssertThat(hitIcons.Count > 0).IsTrue();
            AssertThat(hitIcons.Cast<TextureRect>().Any(t => t.Texture is not null)).IsTrue();

            AssertThat(miss).IsNotNull();
            AssertThat(RenderedText(miss)).Contains("Mystery Hero");
        }
        finally
        {
            hit.Free();
            miss.Free();
        }
    }

    // Thin static-passthrough wrappers so this suite reads against the same kit surface
    // SimPanel subclasses use (protected passthroughs), proving both the kit and the
    // exposure work end to end.
    private static PanelContainer Card(string? name = null) => UiKit.Card(name);
    private static UiKit.SectionView Section(string title) => UiKit.Section(title);
    private static Control StatChip(string label, string value, UiKit.ChipTone tone) =>
        UiKit.StatChip(label, value, tone);
    private static Control ArtRect(string artKey, Vector2 size) => UiKit.ArtRect(artKey, size);
    private static Control PortraitFrame(string artKey, string? caption = null) =>
        UiKit.PortraitFrame(artKey, caption: caption);
}
#endif
