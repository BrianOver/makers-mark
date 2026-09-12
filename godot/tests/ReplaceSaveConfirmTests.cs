#if GDUNIT_TESTS
using System.Collections.Generic;
using GameSim;
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
/// P2-SCREEN-17 (docs/design/MAKERS-MARK.md plan line ~4636, "the save-replace press names the day
/// it destroys"): <see cref="NewGameSelect.OnBeginPressed"/>'s own <see cref="CampaignSave.Clear"/>
/// call was already correct where it sits — the only defect was that the sole cost-naming anywhere
/// on this screen was a dim line three clicks earlier. These guard the confirm row this unit adds:
/// shown only when there is an actual save to lose (never <see cref="Ui.TutorialFlow.HasPriorProgress"/>
/// — a different file, a different question), quoting THAT save's own day, and resolving only on the
/// player's own next press.
///
/// <para><b>Save-state hygiene:</b> mirrors <c>NewGameSelectTests</c>'s own discipline — every test
/// here backs up whatever <c>user://campaign.json</c> holds before it runs and restores it in a
/// <c>finally</c>, since this screen shares that file with the real game and with
/// <c>CampaignSaveTests</c>.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ReplaceSaveConfirmTests
{
    /// <summary>The fixture's own day — every assertion below reads THIS constant rather than a
    /// literal "day 31", so changing the fixture changes what the assertion looks for.</summary>
    private const int FixtureDay = 31;

    private static NewGameSelect Mount()
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        var screen = GD.Load<PackedScene>("res://scenes/new_game_select.tscn").Instantiate<NewGameSelect>();
        tree.Root.AddChild(screen);
        return screen;
    }

    private static void Unmount(NewGameSelect screen)
    {
        MainUi.AdapterOverride = null;
        MainUi.FirstMorningBeatPending = false;
        screen.GetParent()?.RemoveChild(screen);
        screen.Free();
        TutorialFlow.DeleteForTests();
    }

    [TestCase]
    public void NoPriorSave_BeginProceedsStraightThrough_ConfirmRowNeverShown()
    {
        var backup = Backup();
        try
        {
            CampaignSave.Clear(); // the property under test: nothing to destroy

            var screen = Mount();
            var changedTo = new List<string>();
            screen.SceneChange = changedTo.Add;
            try
            {
                Press(screen, "NewGame");
                Press(screen, "Pick_alchemy");
                Press(screen, "Begin");

                AssertThat(Find<VBoxContainer>(screen, "ReplaceSaveConfirm").Visible)
                    .OverrideFailureMessage("A fresh profile with no prior campaign must never be asked to confirm destroying nothing.")
                    .IsFalse();
                AssertThat(MainUi.AdapterOverride)
                    .OverrideFailureMessage("With nothing to destroy, Begin must proceed straight through.")
                    .IsNotNull();
                AssertThat(changedTo.Count).IsEqual(1);
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
    public void PriorSave_BeginShowsConfirm_NamingTheSavesOwnDay_WithoutCommittingYet()
    {
        var backup = Backup();
        try
        {
            CampaignSave.Save(GameComposition.NewCampaign(1UL) with { Day = FixtureDay });

            var screen = Mount();
            var changedTo = new List<string>();
            screen.SceneChange = changedTo.Add;
            try
            {
                Press(screen, "NewGame");
                Press(screen, "Pick_alchemy");
                Press(screen, "Begin");

                var confirm = Find<VBoxContainer>(screen, "ReplaceSaveConfirm");
                AssertThat(confirm.Visible)
                    .OverrideFailureMessage("An existing save must be confirmed before Begin destroys it.")
                    .IsTrue();

                var label = Find<Label>(screen, "ReplaceSaveConfirmLabel").Text;
                AssertThat(label)
                    .OverrideFailureMessage($"Confirm copy did not quote the doomed save's own day ({FixtureDay}): '{label}'")
                    .Contains($"day {FixtureDay}");
                AssertThat(label).Contains("current campaign");
                // Player-facing vocabulary only — no enum member, registry id, or raw citation.
                AssertThat(label).NotContains("P2-SCREEN-17");
                AssertThat(label).NotContains("Envelope");

                // Nothing committed yet — the press only opened the confirm.
                AssertThat(MainUi.AdapterOverride).IsNull();
                AssertThat(changedTo.Count).IsEqual(0);

                // Begin/Back are unreachable while the confirm is up — the primer is in exactly
                // one decision at a time.
                AssertThat(Find<HBoxContainer>(screen, "PrimerActions").Visible).IsFalse();
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
    public void Cancel_DestroysNothing_SaveStillExists_NoCampaignStarted_ScreenReturnedAsIs()
    {
        var backup = Backup();
        try
        {
            CampaignSave.Save(GameComposition.NewCampaign(1UL) with { Day = FixtureDay });

            var screen = Mount();
            var changedTo = new List<string>();
            screen.SceneChange = changedTo.Add;
            try
            {
                Press(screen, "NewGame");
                Press(screen, "Pick_alchemy");
                Press(screen, "Begin");
                AssertThat(Find<VBoxContainer>(screen, "ReplaceSaveConfirm").Visible).IsTrue();

                Press(screen, "ReplaceSaveConfirmNo"); // Cancel

                // Nothing touched: no campaign, no scene change, and the save itself is untouched —
                // same day, still there.
                AssertThat(MainUi.AdapterOverride)
                    .OverrideFailureMessage("Cancel must never start a campaign.")
                    .IsNull();
                AssertThat(changedTo.Count).IsEqual(0);
                var stillThere = CampaignSave.Peek();
                AssertThat(stillThere)
                    .OverrideFailureMessage("Cancel must never touch the existing save.")
                    .IsNotNull();
                AssertThat(stillThere!.Day).IsEqual(FixtureDay);

                // The player is returned exactly where they were: primer up, Begin/Back back,
                // confirm gone.
                AssertThat(Find<VBoxContainer>(screen, "Primer").Visible).IsTrue();
                AssertThat(Find<HBoxContainer>(screen, "PrimerActions").Visible).IsTrue();
                AssertThat(Find<VBoxContainer>(screen, "ReplaceSaveConfirm").Visible).IsFalse();

                // Not a dead end: Begin still works afterward (re-opens the same confirm).
                Press(screen, "Begin");
                AssertThat(Find<VBoxContainer>(screen, "ReplaceSaveConfirm").Visible).IsTrue();
                AssertThat(MainUi.AdapterOverride).IsNull();
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
    public void Confirmed_ProceedsExactlyAsBeginAlwaysDid_ReplacingTheOldSave()
    {
        var backup = Backup();
        try
        {
            CampaignSave.Save(GameComposition.NewCampaign(1UL) with { Day = FixtureDay });

            var screen = Mount();
            screen.SeedSource = () => 555UL;
            var changedTo = new List<string>();
            screen.SceneChange = changedTo.Add;
            try
            {
                Press(screen, "NewGame");
                Press(screen, "Pick_alchemy");
                Press(screen, "Begin");
                Press(screen, "ReplaceSaveConfirmYes"); // Replace it

                AssertThat(MainUi.AdapterOverride).IsNotNull();
                var built = MainUi.AdapterOverride!.CurrentState;
                var expected = GameComposition.NewCampaign(555UL, AlchemyProfession.Id);
                AssertThat(SaveCodec.Serialize(built))
                    .OverrideFailureMessage("Confirming must start exactly the campaign Begin always built.")
                    .IsEqual(SaveCodec.Serialize(expected));
                AssertThat(changedTo.Count).IsEqual(1);

                // The old save is gone — CommitNewCampaign's own CampaignSave.Clear() still runs.
                AssertThat(CampaignSave.Peek()).IsNull();
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
    public void MalformedSeed_RefusesBeforeConfirmIsEverOffered_ExistingSaveUntouched()
    {
        var backup = Backup();
        try
        {
            CampaignSave.Save(GameComposition.NewCampaign(1UL) with { Day = FixtureDay });

            var screen = Mount();
            var changedTo = new List<string>();
            screen.SceneChange = changedTo.Add;
            try
            {
                Press(screen, "NewGame");
                Press(screen, "Pick_alchemy");
                Find<LineEdit>(screen, "SeedField").Text = "not-a-seed";
                Press(screen, "Begin");

                // The seed refusal wins — the confirm row must never have appeared.
                AssertThat(Find<Label>(screen, "SeedError").Visible)
                    .OverrideFailureMessage("A malformed seed must refuse before any confirm is offered.")
                    .IsTrue();
                AssertThat(Find<VBoxContainer>(screen, "ReplaceSaveConfirm").Visible).IsFalse();
                AssertThat(Find<HBoxContainer>(screen, "PrimerActions").Visible).IsTrue();
                AssertThat(MainUi.AdapterOverride).IsNull();
                AssertThat(changedTo.Count).IsEqual(0);

                // And the save the confirm would have named is still exactly as it was.
                var stillThere = CampaignSave.Peek();
                AssertThat(stillThere).IsNotNull();
                AssertThat(stillThere!.Day).IsEqual(FixtureDay);
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

    // ── helpers: never clobber a real campaign (CampaignSaveTests/NewGameSelectTests precedent) ──

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
