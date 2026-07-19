#if GDUNIT_TESTS
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

            // The status label never sets a local font override — its effective size/color
            // only exist because the cascade from ui.Theme reached it.
            var status = Find<Label>(ui, "StatusLabel");
            AssertThat(status.GetThemeFontSize("font_size") >= GameTheme.LegibilityFloor).IsTrue();
            AssertThat(status.GetThemeColor("font_color") == GameTheme.BodyTextColor).IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }
}
#endif
