#if GDUNIT_TESTS
using GdUnit4;
using Godot;
using GodotClient.Minigames;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// C3 (2026-08-09 shell-and-audio-menu plan): the rebind list on the real, shared
/// <see cref="SettingsPanel"/> — reached here through <c>MainUi</c>'s system menu, the same real
/// Control path <c>SystemMenuTests</c> already exercises for the fullscreen row. Every scenario
/// restores whatever it touched (InputMap is a process-wide singleton; <see cref="Unmount"/> already
/// wipes <c>ui_settings.json</c>, but a live rebind's InputMap mutation would otherwise leak into
/// whichever suite the gdUnit runner happens to run next).
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class SettingsPanelTests
{
    [TestCase]
    public void ShowsExactlyTheEightRebindableActions_ReadingTheLiveInputMapBinding()
    {
        var ui = MountMainUi();
        try
        {
            Find<Control>(ui, "SystemMenu").Visible = true;
            Press(ui, "SystemMenuSettings");

            foreach (var action in new[]
                     {
                         "move_up", "move_down", "move_left", "move_right",
                         "interact", "forge_strike", "bellows", "confirm",
                     })
            {
                AssertThat(Find<Button>(ui, $"Rebind_{action}_Key").Text)
                    .OverrideFailureMessage($"Rebind row for '{action}' does not read the live InputMap binding.")
                    .IsEqual(MinigameInput.KeyLabelFor(action));
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void PressToRebind_UpdatesInputMapAndButtonLabel_AndPersistsTheChoice()
    {
        WithTemporaryBinding("move_up", new InputEventKey { PhysicalKeycode = Key.W }, () =>
        {
            UiSettings.DeleteForTests();
            var ui = MountMainUi();
            try
            {
                Find<Control>(ui, "SystemMenu").Visible = true;
                Press(ui, "SystemMenuSettings");

                Press(ui, "Rebind_move_up_Key");
                var panel = Find<SettingsPanel>(ui, "SettingsPanel");
                panel._Input(new InputEventKey { PhysicalKeycode = Key.I, Pressed = true, Echo = false });

                AssertThat(Find<Button>(ui, "Rebind_move_up_Key").Text).IsEqual("I");
                AssertThat(MinigameInput.KeyLabelFor("move_up")).IsEqual("I");
                AssertThat(UiSettings.LoadKeyBinding("move_up")).IsEqual(Key.I);
            }
            finally
            {
                Unmount(ui);
            }
        });
    }

    [TestCase]
    public void PressToRebind_AKeyAlreadyTaken_IsRejected_AndNamesTheHolder()
    {
        WithTemporaryBinding("move_up", new InputEventKey { PhysicalKeycode = Key.W }, () =>
        {
            UiSettings.DeleteForTests();
            var ui = MountMainUi();
            try
            {
                Find<Control>(ui, "SystemMenu").Visible = true;
                Press(ui, "SystemMenuSettings");

                // "interact" (E, TownInput's own default) is a real, currently-bound action —
                // trying to steal it for "move_up" must be rejected, not silently applied.
                Press(ui, "Rebind_move_up_Key");
                var panel = Find<SettingsPanel>(ui, "SettingsPanel");
                panel._Input(new InputEventKey { PhysicalKeycode = Key.E, Pressed = true, Echo = false });

                AssertThat(Find<Button>(ui, "Rebind_move_up_Key").Text)
                    .OverrideFailureMessage("A rejected rebind must leave the row showing its old key.")
                    .IsEqual("W");
                AssertThat(MinigameInput.KeyLabelFor("move_up"))
                    .OverrideFailureMessage("A rejected rebind must leave the InputMap untouched.")
                    .IsEqual("W");
                AssertThat(UiSettings.LoadKeyBinding("move_up") is null)
                    .OverrideFailureMessage("A rejected rebind must not persist.")
                    .IsTrue();
                AssertThat(Find<Label>(ui, "RebindStatus").Text)
                    .OverrideFailureMessage("Conflict detection must name which action already holds the key.")
                    .Contains("Interact");
                AssertThat(MinigameInput.KeyLabelFor("interact"))
                    .OverrideFailureMessage("The conflicting action's own binding must be untouched too.")
                    .IsEqual("E");
            }
            finally
            {
                Unmount(ui);
            }
        });
    }

    [TestCase]
    public void EscapeWhileArmed_CancelsTheCapture_WithoutRebindingEscapeOrClosingTheMenu()
    {
        WithTemporaryBinding("move_up", new InputEventKey { PhysicalKeycode = Key.W }, () =>
        {
            UiSettings.DeleteForTests();
            var ui = MountMainUi();
            try
            {
                Find<Control>(ui, "SystemMenu").Visible = true;
                Press(ui, "SystemMenuSettings");

                Press(ui, "Rebind_move_up_Key");
                var panel = Find<SettingsPanel>(ui, "SettingsPanel");
                panel._Input(new InputEventKey { PhysicalKeycode = Key.Escape, Pressed = true, Echo = false });

                AssertThat(Find<Button>(ui, "Rebind_move_up_Key").Text).IsEqual("W");
                AssertThat(MinigameInput.KeyLabelFor("move_up")).IsEqual("W");
                AssertThat(UiSettings.LoadKeyBinding("move_up") is null).IsTrue();
                AssertThat(panel.Visible)
                    .OverrideFailureMessage("Escape must cancel the CAPTURE only — the settings menu itself must stay open.")
                    .IsTrue();
            }
            finally
            {
                Unmount(ui);
            }
        });
    }

    [TestCase]
    public void ResetControlsToDefaults_RestoresKeysAndClearsPersistedOverrides()
    {
        WithTemporaryBinding("move_up", new InputEventKey { PhysicalKeycode = Key.W }, () =>
        {
            UiSettings.DeleteForTests();
            var ui = MountMainUi();
            try
            {
                Find<Control>(ui, "SystemMenu").Visible = true;
                Press(ui, "SystemMenuSettings");

                Press(ui, "Rebind_move_up_Key");
                var panel = Find<SettingsPanel>(ui, "SettingsPanel");
                panel._Input(new InputEventKey { PhysicalKeycode = Key.I, Pressed = true, Echo = false });
                AssertThat(MinigameInput.KeyLabelFor("move_up")).IsEqual("I"); // setup: the rebind actually took

                Press(ui, "ResetBindingsToDefaults");

                AssertThat(MinigameInput.KeyLabelFor("move_up")).IsEqual("W");
                AssertThat(Find<Button>(ui, "Rebind_move_up_Key").Text).IsEqual("W");
                AssertThat(UiSettings.LoadKeyBinding("move_up") is null)
                    .OverrideFailureMessage("Reset must clear the PERSISTED override too, or a later restart would silently bring the rebind back.")
                    .IsTrue();
            }
            finally
            {
                Unmount(ui);
            }
        });
    }

    // ---- UI scale (C4) --------------------------------------------------------------------------

    [TestCase]
    public void DraggingTheUiScaleSlider_AppliesToTheRealWindow_AndPersists()
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        var originalScale = tree.Root.ContentScaleFactor;
        UiSettings.DeleteForTests();
        var ui = MountMainUi();
        try
        {
            Find<Control>(ui, "SystemMenu").Visible = true;
            Press(ui, "SystemMenuSettings");

            var slider = Find<HSlider>(ui, "UiScaleSlider");
            AssertThat(slider.Value).IsEqualApprox(100.0, 0.01); // factory default, 100%

            slider.Value = 130.0; // the same SpinBox/Slider.Value-drives-ValueChanged idiom used elsewhere

            AssertThat(tree.Root.ContentScaleFactor).IsEqualApprox(1.3f, 0.001f);
            AssertThat(UiSettings.LoadUiScale()).IsEqualApprox(1.3f, 0.001f);
            AssertThat(Find<Label>(ui, "UiScaleValue").Text).IsEqual("130%");
        }
        finally
        {
            Unmount(ui);
            tree.Root.ContentScaleFactor = originalScale;
        }
    }
}
#endif
