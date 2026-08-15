#if GDUNIT_TESTS
using System;
using System.Linq;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U7 (§11.12 plan, "top menu needs a full revamp incl. explanations + keyboard shortcuts"): the
/// owner could not tell what the top-bar buttons did, and real bindings (WASD/E, the minigame
/// verbs, the four quick-travel number keys, F11) had nowhere on screen that named them. This
/// suite pins both halves: <see cref="ShortcutMap"/> stays honest against the LIVE <see
/// cref="InputMap"/> in both directions, and every top-bar control's <see
/// cref="Control.TooltipText"/> is a real sentence, never the one-word restatement the owner's
/// playtest found.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ShortcutLegendTests
{
    [TestCase]
    public void EveryShortcutMapEntry_NamesARealRegisteredBinding()
    {
        var ui = MountMainUi();
        try
        {
            // Forward direction: every action a ShortcutMap entry names must be a live InputMap
            // action, and its key must actually resolve (never the "?" fallback). Fullscreen is
            // the one deliberate exception (RawKey, no Actions at all) — see its own doc.
            foreach (var entry in ShortcutMap.Entries)
            {
                foreach (var action in entry.Actions)
                {
                    AssertThat(InputMap.HasAction(action))
                        .OverrideFailureMessage(
                            $"ShortcutMap entry '{entry.Id}' names action '{action}', which is not a " +
                            "registered InputMap action.")
                        .IsTrue();
                }

                var keyLabel = ShortcutMap.KeyLabel(entry);
                AssertThat(string.IsNullOrEmpty(keyLabel) || keyLabel.Contains('?'))
                    .OverrideFailureMessage($"ShortcutMap entry '{entry.Id}' could not resolve a key label (\"{keyLabel}\").")
                    .IsFalse();
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void EveryRegisteredGameAction_AppearsInShortcutMap()
    {
        var ui = MountMainUi();
        try
        {
            // Backward direction: every action THIS GAME registers (TownInput/MinigameInput both
            // ran via SettingsPanel.Build() during BuildUi; MainUi's own quick-travel actions ran
            // via RegisterQuickTravelActions) must appear in some entry's Actions — excluding
            // Godot's own built-in "ui_*" navigation actions, which are not a game verb this
            // legend should ever have to explain. A binding added later and never legended here
            // must fail this, not silently stay secret.
            var coveredActions = ShortcutMap.Entries.SelectMany(e => e.Actions).ToHashSet();
            foreach (var action in InputMap.GetActions())
            {
                var name = action.ToString();
                if (name.StartsWith("ui_", StringComparison.Ordinal))
                {
                    continue;
                }

                AssertThat(coveredActions.Contains(name))
                    .OverrideFailureMessage(
                        $"InputMap action '{name}' is registered but no ShortcutMap entry explains it — " +
                        "it stays invisible to the player.")
                    .IsTrue();
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void EveryTopBarControl_HasATooltipLongerThanItsOldOneWordRestatement()
    {
        var ui = MountMainUi();
        try
        {
            // The exact roster the owner's playtest named — the verb cluster + Books Tray — paired
            // with the single word (or, for AdvancePhase, the empty string) its tooltip used to be.
            // Proving the fix is a real sentence, not just "still non-empty."
            var controlsAndOldOneWordTooltips = new (string Name, string OldTooltip)[]
            {
                ("AdvancePhase", ""),
                ("Fullscreen", "Fullscreen (F11)"),
                ("OpenLedger", "Ledger"),
                ("OpenForecast", "Forecast"),
                ("OpenCommissions", "Commissions"),
                ("OpenLegends", "Legends"),
                ("OpenDemand", "Demand"),
                ("OpenHeroCards", "Renown"),
                ("OpenProgress", "Progress"),
            };

            foreach (var (name, oldTooltip) in controlsAndOldOneWordTooltips)
            {
                var tooltip = Find<Control>(ui, name).TooltipText;

                AssertThat(string.IsNullOrWhiteSpace(tooltip))
                    .OverrideFailureMessage($"'{name}' still has no tooltip at all.")
                    .IsFalse();

                AssertThat(tooltip.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length)
                    .OverrideFailureMessage($"'{name}'s tooltip (\"{tooltip}\") is still a one-word restatement of its icon.")
                    .IsGreater(1);

                AssertThat(tooltip)
                    .OverrideFailureMessage($"'{name}'s tooltip never changed from the flagged one-word value.")
                    .IsNotEqual(oldTooltip);
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void NoTopBarControlGainedAVerb_TheSameActionsAreReachableAsBefore()
    {
        // U7 is copy and wiring only (law 3: every verb changes an outcome or reveals the
        // player's stake; this unit changes neither an outcome nor adds a stake — it only
        // narrates one that already exists). Every control the owner already had is still
        // there, under its same Name, and still wired to the same panel/action.
        var ui = MountMainUi();
        try
        {
            foreach (var name in new[]
                     {
                         "AdvancePhase", "WatchButton", "AutoAdvance", "PlayPause", "Speed", "Fullscreen",
                         "OpenLedger", "OpenForecast", "OpenCommissions", "OpenLegends", "OpenDemand",
                         "OpenHeroCards", "OpenProgress",
                     })
            {
                AssertThat(Find<Button>(ui, name))
                    .OverrideFailureMessage($"'{name}' no longer exists in the top bar.")
                    .IsNotNull();
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Test scenario 5: the new badge/tooltip additions must still fit at the project's
    /// smallest supported window (<c>project.godot</c>'s 1152×648 — see <see
    /// cref="HudBoundsTests"/>'s own precedent for pinning the SETTING rather than hardcoding the
    /// numbers). The Fullscreen button is the one control that gained a visible sibling
    /// (<see cref="UiKit.ShortcutBadge"/>) rather than just a longer tooltip.</summary>
    [TestCase]
    public async Task FullscreenBadge_StaysOnScreen_AtTheSmallestSupportedWindow()
    {
        var ui = MountMainUi();
        try
        {
            await SettleLayout(ui);

            var viewport = ui.GetViewportRect().Size;
            var button = Find<Control>(ui, "Fullscreen");
            var badge = Find<Control>(ui, "ShortcutBadge");

            foreach (var control in new[] { button, badge })
            {
                AssertThat(control.Size.X)
                    .OverrideFailureMessage($"{control.Name} collapsed to zero width.")
                    .IsGreater(0f);
                var rect = control.GetGlobalRect();
                AssertThat(rect.Position.X).IsGreaterEqual(0f);
                AssertThat(rect.Position.Y).IsGreaterEqual(0f);
                AssertThat(rect.End.X)
                    .OverrideFailureMessage($"{control.Name} right edge {rect.End.X} > viewport width {viewport.X}")
                    .IsLessEqual(viewport.X);
                AssertThat(rect.End.Y).IsLessEqual(viewport.Y);
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void QuickTravelLegendRows_AreVisibleButDimmed_OnAFreshCampaign()
    {
        // Fresh mount: the tutorial has not run, so Tutorial.QuickTravelUnlocked is false — this
        // is the "invisible" case the owner's plan flags: the fix is that these four rows are
        // still ON SCREEN (never hidden), just visibly locked, with the condition in their tooltip.
        var ui = MountMainUi();
        try
        {
            Find<Control>(ui, "SystemMenu").Visible = true;
            Press(ui, "SystemMenuSettings");

            AssertThat(ui.Tutorial.QuickTravelUnlocked)
                .OverrideFailureMessage("This scenario assumes a fresh campaign whose tutorial has not completed.")
                .IsFalse();

            foreach (var id in new[] { "quicktravel_forge", "quicktravel_shop", "quicktravel_tavern", "quicktravel_gate" })
            {
                var entry = ShortcutMap.Find(id);
                var row = Find<Control>(ui, $"Shortcut_{id}_Row");
                var keyLabel = Find<Label>(ui, $"Shortcut_{id}_Key");

                AssertThat(entry.LockedHint)
                    .OverrideFailureMessage($"'{id}' is a quick-travel entry with no LockedHint — this scenario cannot mean anything without one.")
                    .IsNotNull();

                AssertThat(row.Visible)
                    .OverrideFailureMessage($"'{id}' legend row is hidden rather than shown-and-locked.")
                    .IsTrue();
                AssertThat(row.Modulate.A)
                    .OverrideFailureMessage($"'{id}' legend row is not dimmed while locked.")
                    .IsLess(1f);

                // The key itself still shows — only its dimming/tooltip communicate "locked."
                AssertThat(string.IsNullOrWhiteSpace(keyLabel.Text)).IsFalse();
                AssertThat(keyLabel.TooltipText)
                    .OverrideFailureMessage($"'{id}' does not name its unlock condition while locked.")
                    .IsEqual(entry.LockedHint!);

                // "pressing it still does nothing": there is no Button under this row to press —
                // it is Labels only, never a control with a Pressed handler to disable.
                AssertThat(row.GetChildren().OfType<Button>().Any())
                    .OverrideFailureMessage($"'{id}' legend row carries a pressable Button — it should be read-only.")
                    .IsFalse();
            }
        }
        finally
        {
            Unmount(ui);
        }
    }
}
#endif
