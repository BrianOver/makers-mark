#if GDUNIT_TESTS
using System.Linq;
using GdUnit4;
using Godot;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// P007 U1 (R11/KTD1): <see cref="GameTheme.Build"/> is the one programmatic Theme every screen
/// inherits by cascade from the <c>MainUi</c> root. These scenarios prove the Theme itself is
/// well-formed (non-null, legible, styled), that two builds never alias mutable StyleBoxes, and
/// that assigning it at the root actually reaches a descendant Control — the cascade
/// <c>MainUi._Ready</c> relies on.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class GameThemeTests
{
    [TestCase]
    public void Build_ReturnsLegibleTheme_WithPanelAndButtonStyleboxes()
    {
        var theme = GameTheme.Build();

        AssertThat(theme).IsNotNull();
        AssertThat(theme.DefaultFontSize >= GameTheme.LegibilityFloor).IsTrue();

        AssertThat(theme.GetStylebox("panel", "PanelContainer") is StyleBoxFlat).IsTrue();
        AssertThat(theme.GetStylebox("panel", "Panel") is StyleBoxFlat).IsTrue();
        AssertThat(theme.GetStylebox("normal", "Button") is StyleBoxFlat).IsTrue();
        AssertThat(theme.GetStylebox("hover", "Button") is StyleBoxFlat).IsTrue();
        AssertThat(theme.GetStylebox("pressed", "Button") is StyleBoxFlat).IsTrue();
        AssertThat(theme.GetStylebox("disabled", "Button") is StyleBoxFlat).IsTrue();
    }

    [TestCase]
    public void Build_CalledTwice_ReturnsIndependentEquivalentThemes()
    {
        var first = GameTheme.Build();
        var second = GameTheme.Build();

        var firstPanel = first.GetStylebox("panel", "PanelContainer") as StyleBoxFlat;
        var secondPanel = second.GetStylebox("panel", "PanelContainer") as StyleBoxFlat;
        AssertThat(firstPanel).IsNotNull();
        AssertThat(secondPanel).IsNotNull();

        // Equivalent values...
        AssertThat(firstPanel!.BgColor == secondPanel!.BgColor).IsTrue();
        AssertThat(firstPanel.BorderColor == secondPanel.BorderColor).IsTrue();

        // ...but independent instances: mutating one must never bleed into the other or into a
        // fresh third build (the shared-mutable-stylebox aliasing bug KTD1 calls out).
        AssertThat(ReferenceEquals(firstPanel, secondPanel)).IsFalse();
        firstPanel.BgColor = Colors.Red;
        AssertThat(secondPanel.BgColor == Colors.Red).IsFalse();
        AssertThat((GameTheme.Build().GetStylebox("panel", "PanelContainer") as StyleBoxFlat)!.BgColor
            == Colors.Red).IsFalse();
    }

    [TestCase]
    public void MainUi_AssignsThemeAtRoot_AndCascadesToDescendantLabel()
    {
        var ui = MountMainUi();
        try
        {
            AssertThat(ui.Theme).IsNotNull();
            AssertThat(ui.Theme.DefaultFontSize >= GameTheme.LegibilityFloor).IsTrue();

            // The Shop panel's feedback label (SimPanel.AddLabel) never sets a local font
            // override — its effective size/color only exist because the cascade from
            // ui.Theme reached it. (P007 U7 retired the old bare "StatusLabel" in favor of
            // the themed HUD header's StatChips, which DO carry local color overrides by
            // design — see UiKit.StatChip — so they are not a valid cascade-only witness.)
            var feedback = Find<Label>(ui.Shop, "ShopFeedback");
            AssertThat(feedback.GetThemeFontSize("font_size") >= GameTheme.LegibilityFloor).IsTrue();
            AssertThat(feedback.GetThemeColor("font_color") == GameTheme.BodyTextColor).IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// P007 polish (display font): <see cref="GameTheme.HeaderFont"/> resolves to a real,
    /// non-null asset (the committed OFL Cinzel face — never a throw even if the asset were
    /// ever absent, per its null-tolerant contract), and it is registered ONLY on
    /// <see cref="GameTheme.HeaderThemeType"/> — the base "Label"/"Button" theme types never
    /// carry it, so body text keeps the engine default face (R11 layout stability).
    /// </summary>
    [TestCase]
    public void HeaderFont_ResolvesNonNull_AndIsRegisteredOnlyOnTheHeaderVariation()
    {
        AssertThat(GameTheme.HeaderFont).IsNotNull();

        var theme = GameTheme.Build();
        AssertThat(theme.HasFont("font", GameTheme.HeaderThemeType)).IsTrue();
        AssertThat(theme.GetFont("font", GameTheme.HeaderThemeType)).IsEqual(GameTheme.HeaderFont);

        // Body types never carry the display font directly — only the opt-in variation does.
        AssertThat(theme.HasFont("font", "Label")).IsFalse();
        AssertThat(theme.HasFont("font", "Button")).IsFalse();
    }

    /// <summary>
    /// <see cref="GodotClient.Panels.SimPanel.AddHeader"/> and <see cref="UiKit.Section"/>'s
    /// title both opt into <see cref="GameTheme.HeaderThemeType"/>, so a real header rendered
    /// in the live tree resolves <see cref="GameTheme.HeaderFont"/> through the normal theme
    /// cascade — not just in isolation off <see cref="GameTheme.Build"/>.
    /// </summary>
    [TestCase]
    public void RealHeaderLabel_OptsIntoHeaderThemeType_AndResolvesTheDisplayFontFromTheCascade()
    {
        var ui = MountMainUi();
        try
        {
            // ForgePanel's "RECIPES" section header is built via SimPanel.AddHeader.
            var header = ui.Forge.FindChildren("*", "Label", recursive: true, owned: false)
                .Cast<Label>()
                .First(l => l.Text == "RECIPES");

            AssertThat(header.ThemeTypeVariation).IsEqual(GameTheme.HeaderThemeType);
            AssertThat(header.GetThemeFont("font")).IsEqual(GameTheme.HeaderFont);
        }
        finally
        {
            Unmount(ui);
        }
    }
}
#endif
