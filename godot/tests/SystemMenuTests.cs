#if GDUNIT_TESTS
using System.Collections.Generic;
using System.Threading.Tasks;
using GameSim.Kernel;
using GdUnit4;
using Godot;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;
using GodotFileAccess = Godot.FileAccess;

namespace GodotClient.Tests;

/// <summary>
/// U4 (2026-08-02 shell-and-audio plan): the in-game system menu (Resume/Settings/Save &amp; quit
/// to title/Quit game) and its place in the #320 Escape-topmost ladder.
///
/// <para><b>The ladder, pinned end to end</b> (see <see cref="MainUi._Input"/>'s own class doc for
/// the same list with the code alongside it):</para>
/// <list type="number">
/// <item>An open drawer or true modal overlay closes first (pre-existing, <see
/// cref="EscapeClosesModalsTests"/>/<see cref="InteriorEntryExitTests"/> already pin this).</item>
/// <item>The system menu's OWN rung: when it is the thing that's open, Esc closes it.</item>
/// <item>The walkable interior room exits (pre-existing, unchanged position).</item>
/// <item>The system menu's NEW bottom rung: in the bare town with nothing else open, Esc opens it.</item>
/// </list>
///
/// <para><b>Save-state hygiene:</b> the Save-and-quit/Quit-game tests write the real
/// <c>user://campaign.json</c> (shared with <c>CampaignSaveTests</c>/<c>NewGameSelectTests</c>) —
/// backed up and restored exactly like those suites already do.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class SystemMenuTests
{
    // ── the ladder, pinned in one walkthrough ───────────────────────────────────────────────────

    [TestCase]
    public async Task EscapeLadder_ClosesTopmostFirst_ThenExitsTheRoom_ThenOpensTheSystemMenu_ThenClosesIt()
    {
        var ui = MountMainUi();
        try
        {
            var player = new HumanPlayer(ui);
            var menu = Find<Control>(ui, "SystemMenu");

            // Rung 1: enter the forge room, then open its drawer OVER the room. Esc must close
            // the drawer FIRST — the room stays active, the menu never opens.
            ui.Town.FindBuilding("forge").RaisePick();
            AssertThat(ui.Town.InteriorActive).IsTrue();
            ui.OpenPanel("Forge");
            AssertThat(ui.Drawer.IsOpen).IsTrue();

            player.Tap(Key.Escape);
            var drawerClosed = await player.WaitUntil(() => !ui.Drawer.IsOpen);
            AssertThat(drawerClosed).OverrideFailureMessage("Rung 1: the drawer over the room must close first.").IsTrue();
            AssertThat(ui.Town.InteriorActive).OverrideFailureMessage("The same press must not also exit the room.").IsTrue();
            AssertThat(menu.Visible).OverrideFailureMessage("The same press must not also open the system menu.").IsFalse();

            // Rung 2 (pre-existing, unchanged): nothing left open but the room — Esc exits it.
            player.Tap(Key.Escape);
            var roomExited = await player.WaitUntil(() => !ui.Town.InteriorActive);
            AssertThat(roomExited).OverrideFailureMessage("Rung 2: Esc with nothing else open must exit the room.").IsTrue();
            AssertThat(menu.Visible).OverrideFailureMessage("Exiting the room must not also open the system menu.").IsFalse();

            // Rung 3 (U4, NEW): bare town, nothing open — Esc opens the system menu.
            player.Tap(Key.Escape);
            var menuOpened = await player.WaitUntil(() => menu.Visible);
            AssertThat(menuOpened).OverrideFailureMessage("Rung 3: Esc in the bare town must open the system menu.").IsTrue();

            // Rung 4 (U4, NEW): the menu is the topmost thing — Esc closes IT, asked about only
            // itself, never "is anything else open too".
            player.Tap(Key.Escape);
            var menuClosed = await player.WaitUntil(() => !menu.Visible);
            AssertThat(menuClosed).OverrideFailureMessage("Rung 4: Esc with the system menu open must close it.").IsTrue();
            AssertThat(ui.Town.InteriorActive).OverrideFailureMessage("Closing the menu must not re-enter the room.").IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public async Task Escape_WithAModalOpen_ClosesTheModal_NeverOpensTheSystemMenu()
    {
        var ui = MountMainUi();
        try
        {
            ui.Camp.ShowModal();
            AssertThat(ui.Camp.Visible).IsTrue();

            var player = new HumanPlayer(ui);
            player.Tap(Key.Escape);

            var closed = await player.WaitUntil(() => !ui.Camp.Visible);
            AssertThat(closed).OverrideFailureMessage("The open modal must close first.").IsTrue();
            AssertThat(Find<Control>(ui, "SystemMenu").Visible)
                .OverrideFailureMessage("The same press must not also open the system menu.")
                .IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── what being open actually does ───────────────────────────────────────────────────────────

    [TestCase]
    public async Task SystemMenu_WhileOpen_SuppressesWorldInput_AndPausesTheClock()
    {
        var ui = MountMainUi();
        try
        {
            ui.Clock.Play();
            AssertThat(ui.Town.WorldInputNode.Enabled).IsTrue();

            var player = new HumanPlayer(ui);
            player.Tap(Key.Escape);
            var opened = await player.WaitUntil(() => Find<Control>(ui, "SystemMenu").Visible);
            AssertThat(opened).IsTrue();

            AssertThat(ui.Town.WorldInputNode.Enabled)
                .OverrideFailureMessage("The system menu must suppress world input while open (R3).")
                .IsFalse();
            AssertThat(ui.Clock.Playing)
                .OverrideFailureMessage("The day must pause while the pause menu is up.")
                .IsFalse();

            player.Tap(Key.Escape);
            var closed = await player.WaitUntil(() => !Find<Control>(ui, "SystemMenu").Visible);
            AssertThat(closed).IsTrue();

            AssertThat(ui.Town.WorldInputNode.Enabled)
                .OverrideFailureMessage("Closing the menu must restore world input.")
                .IsTrue();
            AssertThat(ui.Clock.Playing)
                .OverrideFailureMessage("Closing the menu must resume play, since it was playing when opened.")
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void SystemMenu_Settings_IsTheSharedSettingsPanel_AndTogglesFullscreen()
    {
        UiSettings.TestWindowMode = DisplayServer.WindowMode.Windowed;
        var ui = MountMainUi();
        try
        {
            var menu = Find<Control>(ui, "SystemMenu");
            menu.Visible = true; // open directly — this test is about the SETTINGS SUB-VIEW, not Esc routing

            Press(ui, "SystemMenuSettings");
            AssertThat(Find<VBoxContainer>(ui, "SystemMenuList").Visible)
                .OverrideFailureMessage("Settings must hide the button list — only one sub-view at a time.")
                .IsFalse();

            var toggle = Find<CheckButton>(ui, "FullscreenToggle");
            AssertThat(toggle.ButtonPressed).IsFalse();

            toggle.EmitSignal(CheckButton.SignalName.Toggled, true);

            AssertThat(UiSettings.TestWindowMode)
                .OverrideFailureMessage("The system menu's Settings checkbox reuses UiSettings — same seam as the title screen.")
                .IsEqual(DisplayServer.WindowMode.Fullscreen);

            Press(ui, "SettingsBack");
            AssertThat(Find<VBoxContainer>(ui, "SystemMenuList").Visible).IsTrue();
        }
        finally
        {
            Unmount(ui);
            UiSettings.TestWindowMode = null;
        }
    }

    // ── save-on-quit (KTD-D) ────────────────────────────────────────────────────────────────────

    [TestCase]
    public void SaveAndQuitToTitle_WritesALoadableSave_MatchingCurrentState_AndRequestsTheTitleScene()
    {
        var backup = Backup();
        try
        {
            var ui = MountMainUi();
            var changedTo = new List<string>();
            ui.SceneChange = changedTo.Add;
            try
            {
                AdvanceDay(ui); // land mid-campaign (day 2, Morning) — never staler than "day 1 untouched"
                var beforeState = ui.Adapter.CurrentState;

                Press(ui, "SaveQuitToTitle");

                AssertThat(changedTo.Count).IsEqual(1);
                AssertThat(changedTo[0]).IsEqual("res://scenes/new_game_select.tscn");

                var summary = CampaignSave.Peek();
                AssertThat(summary).IsNotNull();
                AssertThat(summary!.Day).IsEqual(beforeState.Day);
                AssertThat(summary.Phase).IsEqual(beforeState.Phase.ToString());

                var loaded = CampaignSave.TryLoad();
                AssertThat(loaded).IsNotNull();
                AssertThat(SaveCodec.Serialize(loaded!))
                    .OverrideFailureMessage("Save & quit to title must persist the EXACT live state, not a stale copy.")
                    .IsEqual(SaveCodec.Serialize(beforeState));
            }
            finally
            {
                Unmount(ui);
            }
        }
        finally
        {
            Restore(backup);
        }
    }

    [TestCase]
    public void QuitGame_SavesFirst_ThenRequestsQuit_ViaTheOverrideSeam()
    {
        var backup = Backup();
        try
        {
            var ui = MountMainUi();
            try
            {
                AdvanceDay(ui);
                var beforeState = ui.Adapter.CurrentState;

                var quit = false;
                ui.QuitOverride = () => quit = true;

                Press(ui, "QuitGame");

                AssertThat(quit)
                    .OverrideFailureMessage("Quit game must route through QuitOverride, never a real SceneTree.Quit() in a test.")
                    .IsTrue();

                var loaded = CampaignSave.TryLoad();
                AssertThat(loaded).IsNotNull();
                AssertThat(SaveCodec.Serialize(loaded!)).IsEqual(SaveCodec.Serialize(beforeState));
            }
            finally
            {
                Unmount(ui);
            }
        }
        finally
        {
            Restore(backup);
        }
    }

    /// <summary>KTD-D: the OS window's own close request funnels through the exact same
    /// save-then-quit routing as "Quit game" — proven here by invoking <c>_Notification</c>
    /// directly with the real notification constant, never a real OS event (headless CI has none)
    /// and never a real <c>GetTree().Quit()</c> (would tear down the test process).</summary>
    [TestCase]
    public void WmCloseRequest_SavesFirst_ThenRequestsQuit_ViaTheOverrideSeam()
    {
        var backup = Backup();
        try
        {
            var ui = MountMainUi();
            try
            {
                AdvanceDay(ui);
                var beforeState = ui.Adapter.CurrentState;

                var quit = false;
                ui.QuitOverride = () => quit = true;

                ui._Notification((int)Node.NotificationWMCloseRequest);

                AssertThat(quit).IsTrue();
                var loaded = CampaignSave.TryLoad();
                AssertThat(loaded).IsNotNull();
                AssertThat(SaveCodec.Serialize(loaded!)).IsEqual(SaveCodec.Serialize(beforeState));
            }
            finally
            {
                Unmount(ui);
            }
        }
        finally
        {
            Restore(backup);
        }
    }

    // ── helpers: never clobber a real campaign (CampaignSaveTests precedent, duplicated) ────────

    private static string? Backup() => GodotFileAccess.FileExists(CampaignSave.SavePath) ? Read() : null;

    private static void Restore(string? backup)
    {
        if (backup is null)
        {
            CampaignSave.Clear();
            return;
        }

        Write(backup);
    }

    private static string Read()
    {
        using var file = GodotFileAccess.Open(CampaignSave.SavePath, GodotFileAccess.ModeFlags.Read);
        return file.GetAsText();
    }

    private static void Write(string contents)
    {
        using var file = GodotFileAccess.Open(CampaignSave.SavePath, GodotFileAccess.ModeFlags.Write);
        file.StoreString(contents);
    }
}
#endif
