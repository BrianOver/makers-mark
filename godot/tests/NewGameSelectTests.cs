#if GDUNIT_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using GameSim;
using GameSim.Contracts;
using GameSim.Kernel;
using GameSim.Professions;
using GdUnit4;
using Godot;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;
using GodotFileAccess = Godot.FileAccess;

namespace GodotClient.Tests;

/// <summary>
/// U4/World-Rework-U11 engine-lane scenarios, extended by the 2026-08-02 shell-and-audio plan's U3
/// ("the front door: a title screen that is a menu"): the front door now opens on a TITLE MENU
/// (Continue/New Game/Settings/Quit) with the "choose your primary profession" picker and "your
/// first day" primer demoted to the New Game sub-flow behind it. The scene change is stubbed
/// (injectable <see cref="NewGameSelect.SceneChange"/>) so the test tree is never torn down.
///
/// <para><b>Save-state hygiene:</b> <see cref="NewGameSelect.BuildContinue"/> reads
/// <see cref="CampaignSave"/>, which shares <c>user://campaign.json</c> with the real game AND with
/// <c>CampaignSaveTests</c>. Every test that cares whether/what Continue renders backs the file up
/// first and restores it in a <c>finally</c> — the same discipline <c>CampaignSaveTests</c> already
/// uses, duplicated locally rather than shared across files (repo convention: small test helpers
/// are cheap to keep self-contained per suite).</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class NewGameSelectTests
{
    private static NewGameSelect Mount()
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        var screen = GD.Load<PackedScene>("res://scenes/new_game_select.tscn").Instantiate<NewGameSelect>();
        tree.Root.AddChild(screen); // triggers _Ready: title menu + buttons built from ProfessionRegistry
        return screen;
    }

    private static void Unmount(NewGameSelect screen)
    {
        MainUi.AdapterOverride = null; // never leak a picked campaign into later suites
        // U16: several tests below press "Begin" (which sets this true) and never mount MainUi
        // afterward to consume it — without this, a LATER suite's bare MountMainUi() would
        // unexpectedly show the cold-open beat and its exact PendingLessonCount/Visible assertions.
        MainUi.FirstMorningBeatPending = false;
        screen.GetParent()?.RemoveChild(screen);
        screen.Free();

        // U4: leak guard for the shell's fullscreen preference — a test that toggled Settings or
        // F11 must never leave a persisted choice for a later suite (or the developer's own real
        // user:// data) to inherit. Mirrors MainUi's own Unmount guard (UiTestSupport.cs).
        UiSettings.DeleteForTests();
    }

    // ── the title menu itself (U3) ──────────────────────────────────────────────────────────────

    [TestCase]
    public void FreshMount_ShowsOnlyTheTitleMenu_PickerPrimerAndSettingsHidden()
    {
        var backup = Backup();
        try
        {
            CampaignSave.Clear(); // deterministic: no Continue row for this assertion
            var screen = Mount();
            try
            {
                AssertThat(Find<VBoxContainer>(screen, "TitleMenu").Visible).IsTrue();
                AssertThat(Find<VBoxContainer>(screen, "ProfessionPicker").Visible).IsFalse();
                AssertThat(Find<VBoxContainer>(screen, "Primer").Visible).IsFalse();
                AssertThat(Find<SettingsPanel>(screen, "SettingsPanel").Visible).IsFalse();

                // No save exists — no Continue row, but the other three menu entries are always there.
                AssertThat(screen.FindChild("ContinueRow", recursive: true, owned: false)).IsNull();
                AssertThat(Find<Button>(screen, "NewGame")).IsNotNull();
                AssertThat(Find<Button>(screen, "SettingsButton")).IsNotNull();
                AssertThat(Find<Button>(screen, "Quit")).IsNotNull();
            }
            finally
            {
                Unmount(screen);
            }
        }
        finally
        {
            Restore(backup);
        }
    }

    [TestCase]
    public void NewGame_RevealsThePicker_AndHidesTheTitleMenu()
    {
        var screen = Mount();
        try
        {
            Press(screen, "NewGame");

            AssertThat(Find<VBoxContainer>(screen, "TitleMenu").Visible)
                .OverrideFailureMessage("New Game must hide the title menu — only one view is ever visible.")
                .IsFalse();

            AssertThat(ProfessionRegistry.All.Count).IsEqual(4);
            foreach (var profession in ProfessionRegistry.All.Values)
            {
                var button = Find<Button>(screen, $"Pick_{profession.Id}");
                AssertThat(button.Text).IsEqual(profession.DisplayName);

                // Per-profession blurb (R9): present and non-empty for every registered craft.
                var blurb = Find<Label>(screen, $"Blurb_{profession.Id}");
                AssertThat(blurb.Text).IsNotEmpty();
            }

            // The four profession buttons under the picker (no extra "classic" default path,
            // scope pin) PLUS the picker's own "PickerBack" — five, not four. Recursive (not
            // direct-child) count: the cozy restyle groups each pick button+blurb into its own
            // "PickRow_{id}" sub-container, one level deeper than the picker itself, while
            // PickerBack sits as a direct child alongside those rows — this single recursive
            // count catches both shapes.
            var picker = Find<VBoxContainer>(screen, "ProfessionPicker");
            var buttons = picker.FindChildren("*", "Button", recursive: true, owned: false)
                .OfType<Button>()
                .Count();
            AssertThat(buttons).IsEqual(5);
            AssertThat(Find<Label>(screen, "StarterKitNote").Text).IsNotEmpty();

            // The primer never shows before a pick.
            AssertThat(picker.Visible).IsTrue();
            AssertThat(Find<VBoxContainer>(screen, "Primer").Visible).IsFalse();
        }
        finally
        {
            Unmount(screen);
        }
    }

    [TestCase]
    public void PickerBack_ReturnsToTheTitleMenu_WithoutLeakingACampaign()
    {
        var screen = Mount();
        try
        {
            MainUi.AdapterOverride = null;
            Press(screen, "NewGame");
            Press(screen, "PickerBack");

            AssertThat(Find<VBoxContainer>(screen, "TitleMenu").Visible)
                .OverrideFailureMessage("The picker's own Back returns to the title menu.")
                .IsTrue();
            AssertThat(Find<VBoxContainer>(screen, "ProfessionPicker").Visible).IsFalse();
            AssertThat(MainUi.AdapterOverride).IsNull();
        }
        finally
        {
            Unmount(screen);
        }
    }

    [TestCase]
    public void Settings_TogglesFullscreen_AndPersistsViaUiSettings()
    {
        UiSettings.TestWindowMode = DisplayServer.WindowMode.Windowed;
        var screen = Mount();
        try
        {
            Press(screen, "SettingsButton");

            AssertThat(Find<VBoxContainer>(screen, "TitleMenu").Visible)
                .OverrideFailureMessage("Settings must hide the title menu — only one view is ever visible.")
                .IsFalse();
            var settings = Find<SettingsPanel>(screen, "SettingsPanel");
            AssertThat(settings.Visible).IsTrue();

            var toggle = Find<CheckButton>(screen, "FullscreenToggle");
            AssertThat(toggle.ButtonPressed).IsFalse();

            toggle.EmitSignal(CheckButton.SignalName.Toggled, true);

            AssertThat(UiSettings.TestWindowMode)
                .OverrideFailureMessage("The Settings checkbox did not flip the window mode.")
                .IsEqual(DisplayServer.WindowMode.Fullscreen);
            AssertThat(UiSettings.LoadFullscreen() == true)
                .OverrideFailureMessage("Fullscreen must persist (KTD-D) so it survives a restart.")
                .IsTrue();
            AssertThat(toggle.ButtonPressed).IsTrue();

            Press(screen, "SettingsBack");
            AssertThat(settings.Visible).IsFalse();
            AssertThat(Find<VBoxContainer>(screen, "TitleMenu").Visible).IsTrue();
        }
        finally
        {
            Unmount(screen);
            UiSettings.TestWindowMode = null;
        }
    }

    // ── R4: Continue tells the truth ────────────────────────────────────────────────────────────

    [TestCase]
    public void Continue_IsAbsent_WhenNoSaveExists()
    {
        var backup = Backup();
        try
        {
            CampaignSave.Clear();
            var screen = Mount();
            try
            {
                AssertThat(screen.FindChild("ContinueRow", recursive: true, owned: false))
                    .OverrideFailureMessage("No save should mean no Continue row at all, not a disabled one.")
                    .IsNull();
            }
            finally
            {
                Unmount(screen);
            }
        }
        finally
        {
            Restore(backup);
        }
    }

    [TestCase]
    public void Continue_NamesTheProfessionDayAndPhase_AndWhenItWasSaved()
    {
        var backup = Backup();
        try
        {
            CampaignSave.UtcNowSource = () => DateTime.UtcNow; // real "now" — always renders as "today"
            CampaignSave.Save(GameComposition.NewCampaign(11, AlchemyProfession.Id) with { Day = 4 });

            var screen = Mount();
            try
            {
                var button = Find<Button>(screen, "Continue");
                AssertThat(button.Text).Contains(ProfessionRegistry.All[AlchemyProfession.Id].DisplayName);
                AssertThat(button.Text).Contains("Day 4");

                var blurb = Find<Label>(screen, "ContinueBlurb");
                AssertThat(blurb.Text)
                    .OverrideFailureMessage($"Blurb did not name when the save was written: '{blurb.Text}'")
                    .Contains("saved today");
            }
            finally
            {
                Unmount(screen);
            }
        }
        finally
        {
            CampaignSave.UtcNowSource = static () => DateTime.UtcNow;
            Restore(backup);
        }
    }

    /// <summary>KTD-E backward compatibility: a schema-1 envelope written before this unit has
    /// neither <c>ProfessionId</c> nor <c>SavedAtUtc</c> in its JSON at all (not merely null) —
    /// Continue must still appear, just without the profession/saved-at clauses.</summary>
    [TestCase]
    public void Continue_DegradesGracefully_ForAPreU3Save_MissingProfessionAndSavedAt()
    {
        var backup = Backup();
        try
        {
            // "State" only needs to be non-empty — Peek()/BuildContinue never deserialize the world
            // (see CampaignSave.Peek's own doc); a placeholder is honest here because this test is
            // about the ENVELOPE'S missing trailing fields, not save corruption (that is
            // CampaignSaveTests' job).
            Write($"{{\"SchemaVersion\":{CampaignSave.Schema},\"Day\":7,\"Phase\":\"Camp\",\"State\":\"x\"}}");

            var screen = Mount();
            try
            {
                var button = Find<Button>(screen, "Continue");
                AssertThat(button.Text).IsEqual("Continue — Day 7, Vigil");

                var blurb = Find<Label>(screen, "ContinueBlurb");
                AssertThat(blurb.Text)
                    .OverrideFailureMessage($"A pre-U3 save must not claim a saved-at time it never recorded: '{blurb.Text}'")
                    .NotContains("saved");
            }
            finally
            {
                Unmount(screen);
            }
        }
        finally
        {
            Restore(backup);
        }
    }

    // ── the New Game sub-flow (unchanged behavior, now reached via New Game) ───────────────────

    [TestCase]
    public void Pick_ShowsPrimer_ListingAllFivePhases_WithClockNoteAndSeed_NeverTouchingAdapter()
    {
        var screen = Mount();
        screen.SeedSource = () => 999UL;
        try
        {
            MainUi.AdapterOverride = null;
            Press(screen, "NewGame");
            Press(screen, "Pick_blacksmith");

            // Picker hides, primer shows — no campaign built yet (a pick is reversible).
            AssertThat(Find<VBoxContainer>(screen, "ProfessionPicker").Visible).IsFalse();
            AssertThat(Find<VBoxContainer>(screen, "Primer").Visible).IsTrue();
            AssertThat(MainUi.AdapterOverride).IsNull();

            // 5-phase day, one line each, verbatim MainUi.PhaseLegend (R12) — never drifts.
            // U2 (playtest-three plan): headers are PhaseVocab's words now, not the raw sim phases.
            var phaseLegend = Find<Label>(screen, "PhaseLegend");
            AssertThat(phaseLegend.Text).IsEqual(MainUi.PhaseLegend);
            var lines = phaseLegend.Text.Split('\n');
            AssertThat(lines.Length).IsEqual(5);
            foreach (var phaseName in new[] { "Dawn/Prepare", "Quest", "Vigil", "Deep Vigil", "Night" })
            {
                AssertThat(lines.Any(line => line.StartsWith(phaseName))).IsTrue();
            }

            // Clock behavior explainer (R7/R8/KTD3 copy) and the exact seed about to be used.
            AssertThat(Find<Label>(screen, "ClockNote").Text).IsNotEmpty();
            AssertThat(Find<Label>(screen, "SeedLabel").Text).IsEqual("Seed: 999");
        }
        finally
        {
            Unmount(screen);
        }
    }

    /// <summary>U7 (opener fantasy line): the primer must state the fantasy — not just the
    /// clock mechanics — so the first-day view says what the game is about, not only how the
    /// day flows.</summary>
    [TestCase]
    public void Pick_ShowsPrimer_StatesTheFantasy_HeroesCarryYourGearIntoTheMine()
    {
        var screen = Mount();
        try
        {
            MainUi.AdapterOverride = null;
            Press(screen, "NewGame");
            Press(screen, "Pick_blacksmith");

            var fantasy = Find<Label>(screen, "FantasyNote").Text;
            AssertThat(fantasy).IsNotEmpty();
            AssertThat(fantasy).Contains("Mine");
        }
        finally
        {
            Unmount(screen);
        }
    }

    [TestCase]
    public void Back_ReturnsToPicker_WithoutLeakingCampaign_AndPickIsStillUsableAfter()
    {
        var screen = Mount();
        var changedTo = new List<string>();
        screen.SceneChange = changedTo.Add;
        try
        {
            MainUi.AdapterOverride = null;
            Press(screen, "NewGame");
            Press(screen, "Pick_alchemy");
            AssertThat(Find<VBoxContainer>(screen, "Primer").Visible).IsTrue();

            Press(screen, "Back");

            // Nothing committed: no adapter, no scene-change request, picker is back.
            AssertThat(MainUi.AdapterOverride).IsNull();
            AssertThat(changedTo.Count).IsEqual(0);
            AssertThat(Find<VBoxContainer>(screen, "Primer").Visible).IsFalse();
            AssertThat(Find<VBoxContainer>(screen, "ProfessionPicker").Visible).IsTrue();

            // The screen still works after a back-out — pick again and actually begin.
            Press(screen, "Pick_alchemy");
            Press(screen, "Begin");

            AssertThat(MainUi.AdapterOverride).IsNotNull();
            AssertThat(string.Join(",", MainUi.AdapterOverride!.CurrentState.Player.SelectedProfessions))
                .IsEqual("alchemy");
            AssertThat(changedTo.Count).IsEqual(1);
        }
        finally
        {
            Unmount(screen);
        }
    }

    [TestCase("blacksmith")]
    [TestCase("tanning")]
    [TestCase("alchemy")]
    [TestCase("engineering")]
    public void EveryProfession_Pick_Begin_BuildsSeededCampaign_WithMatchingSelection(string professionId)
    {
        var screen = Mount();
        var changedTo = new List<string>();
        screen.SceneChange = changedTo.Add;
        try
        {
            MainUi.AdapterOverride = null;
            Press(screen, "NewGame");
            Press(screen, $"Pick_{professionId}");
            Press(screen, "Begin");

            AssertThat(MainUi.AdapterOverride).IsNotNull();
            var state = MainUi.AdapterOverride!.CurrentState;
            AssertThat(string.Join(",", state.Player.SelectedProfessions)).IsEqual(professionId);
            AssertThat(state.Player.Materials["copper"]).IsEqual(GameFactory.StarterCopper);

            // The press requested exactly one swap, to the main scene.
            AssertThat(changedTo.Count).IsEqual(1);
            AssertThat(changedTo[0]).IsEqual(NewGameSelect.MainScenePath);
        }
        finally
        {
            Unmount(screen);
        }
    }

    [TestCase]
    public void FixedSeed_SameProfession_TwoIndependentScreens_ProduceByteIdenticalCampaigns()
    {
        const ulong fixedSeed = 424242UL;

        // SceneChange MUST be stubbed on every mount that presses Begin — an un-stubbed press
        // fires the REAL GetTree().ChangeSceneToFile, which tears down the test tree out from
        // under Unmount() and leaves orphan nodes behind (caught during this test's authoring).
        var first = Mount();
        first.SeedSource = () => fixedSeed;
        first.SceneChange = _ => { };
        GameState stateA;
        try
        {
            MainUi.AdapterOverride = null;
            Press(first, "NewGame");
            Press(first, "Pick_engineering");
            Press(first, "Begin");
            stateA = MainUi.AdapterOverride!.CurrentState;
        }
        finally
        {
            Unmount(first);
        }

        var second = Mount();
        second.SeedSource = () => fixedSeed;
        second.SceneChange = _ => { };
        GameState stateB;
        try
        {
            MainUi.AdapterOverride = null;
            Press(second, "NewGame");
            Press(second, "Pick_engineering");
            Press(second, "Begin");
            stateB = MainUi.AdapterOverride!.CurrentState;
        }
        finally
        {
            Unmount(second);
        }

        AssertThat(SaveCodec.Serialize(stateA)).IsEqual(SaveCodec.Serialize(stateB));
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

    /// <summary>
    /// U7 (world-and-interiors plan, KTD-3): "rethink the whole start picking" — the pick's world
    /// consequence stated at pick time, one row per registered profession, read straight off the
    /// SAME <see cref="GodotClient.Town2d.WorkshopVocab"/> table the actual workshop building
    /// resolves through (never a second hand-copied name table that could drift from it).
    /// </summary>
    [TestCase]
    public void EveryProfessionRow_StatesItsWorkshopNametag()
    {
        var screen = Mount();
        try
        {
            foreach (var profession in ProfessionRegistry.All.Values)
            {
                var note = Find<Label>(screen, $"WorkshopNote_{profession.Id}");
                var expectedNametag = GodotClient.Town2d.WorkshopVocab.NametagFor(new[] { profession.Id });

                AssertThat(note.Text)
                    .OverrideFailureMessage(
                        $"'{profession.Id}'s pick row must state its actual workshop nametag "
                        + $"('{expectedNametag}'), the SAME one WorkshopVocab/Town2D resolves at play time.")
                    .Contains(expectedNametag);
            }
        }
        finally { Unmount(screen); }
    }
}
#endif
