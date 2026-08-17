#if GDUNIT_TESTS
using System.Linq;
using GameSim.Contracts;
using GameSim.Crafting;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// Register #149 ("the legacy jank crafting menu", U-T1-6, §11.14.3): the owner's screenshot showed
/// the forge drawer with three "(none)" dropdowns in a row and nothing saying what any of them were,
/// plus a confirmation row that still occupied a full line of height while blank — both read as
/// leftover dev tooling rather than a finished crafting station. This file pins both fixes:
/// <see cref="ForgePanel.BuildModifierSelect"/>'s three selects now each carry a family label
/// (<see cref="CraftModifiers.FamilyLabel"/>, derived from <see cref="ModifierFamily"/> rather than a
/// hardcoded string), and <see cref="ForgePanel.SetFeedback"/> hides the feedback label — reclaiming
/// its reserved height, since an invisible Godot Control is skipped by its parent Container's own
/// layout — whenever there is nothing to say.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ForgeMenuLegibilityTests
{
    [TestCase]
    public void EachModifierSelect_RendersItsOwnFamilyLabel_DerivedFromTheEnum()
    {
        var ui = MountMainUi();
        try
        {
            ui.OpenPanel("Forge");

            AssertFamilyLabel(ui, "OilSelect", ModifierFamily.QuenchOil);
            AssertFamilyLabel(ui, "RuneSelect", ModifierFamily.Rune);
            AssertFamilyLabel(ui, "FitSelect", ModifierFamily.Fitting);
        }
        finally { Unmount(ui); }
    }

    /// <summary>
    /// Asserts the label is non-blank, matches <see cref="CraftModifiers.FamilyLabel"/>'s own text
    /// (never a copy hand-typed at the test site, which could silently drift the same way the
    /// production bug did) and is a REAL SIBLING of the select it names inside the SAME group — not
    /// merely present somewhere else on the page, which a bare <c>RenderedText().Contains(...)</c>
    /// assertion could not distinguish from an unrelated stray label.
    /// </summary>
    private static void AssertFamilyLabel(MainUi ui, string selectName, ModifierFamily family)
    {
        var select = Find<OptionButton>(ui.Forge, selectName);
        var label = Find<Label>(ui.Forge, $"{selectName}Label");

        AssertThat(label.Text).IsNotEmpty();
        AssertThat(label.Text).IsEqual($"{CraftModifiers.FamilyLabel(family)}:");
        AssertThat(label.GetParent() == select.GetParent()).IsTrue();
    }

    [TestCase]
    public void ThreeModifierSelects_EachCarryADistinctLabel_NeverTheSameTextTwice()
    {
        // The pre-fix bug was not merely "no label" in the abstract — it was three IDENTICAL "(none)"
        // boxes a player had no way to tell apart. Distinctness is the actual player-visible fix, and
        // a weaker "label.Text is non-empty" assertion alone would pass even if all three had been
        // (wrongly) given the SAME non-blank text.
        var ui = MountMainUi();
        try
        {
            ui.OpenPanel("Forge");

            var labels = new[] { "OilSelectLabel", "RuneSelectLabel", "FitSelectLabel" }
                .Select(name => Find<Label>(ui.Forge, name).Text)
                .ToList();

            AssertThat(labels.Distinct().Count()).IsEqual(labels.Count);
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void FreshForgeOpen_NoActionTakenYet_FeedbackRowIsHiddenAndReservesNoSpace()
    {
        var ui = MountMainUi();
        try
        {
            ui.OpenPanel("Forge");

            var feedback = Find<Label>(ui.Forge, "ForgeFeedback");
            AssertThat(feedback.Text).IsEqual(string.Empty);
            // Visible, not just empty text — an INVISIBLE Control is what Godot's own Container
            // layout skips when sizing/positioning siblings, so this is the actual reserved-space
            // claim, not a proxy for it. A test that only checked Text would pass even if the label
            // stayed Visible and kept reserving its blank line.
            AssertThat(feedback.Visible).IsFalse();
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void AfterACraft_FeedbackRowShowsTheConfirmation_AndBecomesVisibleImmediately()
    {
        var ui = MountMainUi();
        try
        {
            ui.Adapter.Queue(new BuyMaterialAction(ScriptedSession.CraftMaterial, ScriptedSession.CopperNeeded));
            ui.Adapter.AdvancePhase();
            ui.OpenPanel("Forge");

            var feedback = Find<Label>(ui.Forge, "ForgeFeedback");
            AssertThat(feedback.Visible).IsFalse(); // sanity: still hidden before the press

            PressEnabled(ui.Forge, $"Craft_{ScriptedSession.CraftRecipeId}");

            AssertThat(feedback.Visible).IsTrue();
            AssertThat(feedback.Text).IsNotEmpty();
            AssertThat(feedback.Text).Contains("Crafted");
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void AfterACraft_ThenReopeningFresh_FeedbackRowHidesAgainOnceMessageIsCleared()
    {
        // SetFeedback is the ONLY writer of _feedback.Text/.Visible (ForgePanel's own doc on the
        // method) — this proves the pairing survives a SECOND write, not just the very first one,
        // so a future call site that bypasses the helper (a direct `_feedback!.Text = ...`) would
        // show up here as a row that stays visible with stale text instead of clearing.
        var ui = MountMainUi();
        try
        {
            ui.Adapter.Queue(new BuyMaterialAction(ScriptedSession.CraftMaterial, ScriptedSession.CopperNeeded));
            ui.Adapter.AdvancePhase();
            ui.OpenPanel("Forge");

            PressEnabled(ui.Forge, $"Craft_{ScriptedSession.CraftRecipeId}");
            var feedback = Find<Label>(ui.Forge, "ForgeFeedback");
            AssertThat(feedback.Visible).IsTrue(); // sanity: the craft did leave a message

            PressEnabled(ui.Forge, "Unlock_keen-eye");

            // Still a real message (Unlock also confirms) — the row must remain visible, proving
            // this is a content-driven toggle, not something that blanks on every unrelated Refresh.
            AssertThat(feedback.Visible).IsTrue();
            AssertThat(feedback.Text).Contains("Unlocked");
        }
        finally { Unmount(ui); }
    }
}
#endif
