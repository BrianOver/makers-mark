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

namespace GodotClient.Tests;

/// <summary>
/// P2-ONBOARD-10 (docs/design/MAKERS-MARK.md §11.15, plan line ~4640): the seed door — the
/// primer's seed display becomes an enterable <see cref="LineEdit"/> ("SeedField"), validated at
/// Begin time. These guard the property the unit exists to prove (a typed seed is the seed a
/// campaign actually starts on — read off <see cref="GameState"/>, never off label text) plus the
/// two edge doors named in the unit's own scope: an empty field keeps today's wall-clock
/// behavior, and a malformed one refuses to Begin rather than silently starting on a seed the
/// player never typed. <see cref="WarrantSeedTests"/> owns the fourth guard (the Warrant's pin
/// still wins over anything in the field).
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class SeedFieldTests
{
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
    public void EnteringASeed_StartsTheCampaignOnThatSeed_NotTheOneOriginallyDrawn()
    {
        // Force "returning smith" so this pick never qualifies for the Warrant's pinned seed
        // (WarrantSeedTests owns that door) — this test is about SeedField's own override path.
        TutorialFlow.ResetForReturningSmith();

        var screen = Mount();
        screen.SeedSource = () => 111UL; // the draw the player is about to type OVER
        screen.SceneChange = _ => { };
        try
        {
            Press(screen, "NewGame");
            Press(screen, "Pick_alchemy");

            var field = Find<LineEdit>(screen, "SeedField");
            AssertThat(field.Text).IsEqual("111"); // pre-filled with the wall-clock draw

            field.Text = "555555"; // the player types a specific seed to revisit
            Press(screen, "Begin");

            // The proof this unit exists for: read the ACTUAL campaign seed off GameState, never
            // off a label's text.
            var built = MainUi.AdapterOverride!.CurrentState;
            var expected = GameComposition.NewCampaign(555555UL, AlchemyProfession.Id);
            AssertThat(SaveCodec.Serialize(built))
                .OverrideFailureMessage("Typing a seed into SeedField did not start the campaign on that seed.")
                .IsEqual(SaveCodec.Serialize(expected));
        }
        finally
        {
            Unmount(screen);
        }
    }

    [TestCase]
    public void EmptyField_StillStartsACampaign_OnTheWallClockDraw_WithoutThrowing()
    {
        TutorialFlow.ResetForReturningSmith();

        var screen = Mount();
        screen.SeedSource = () => 321UL;
        var changedTo = new List<string>();
        screen.SceneChange = changedTo.Add;
        try
        {
            Press(screen, "NewGame");
            Press(screen, "Pick_alchemy");

            Find<LineEdit>(screen, "SeedField").Text = string.Empty; // "surprise me"
            Press(screen, "Begin"); // must not throw

            var built = MainUi.AdapterOverride!.CurrentState;
            var expected = GameComposition.NewCampaign(321UL, AlchemyProfession.Id);
            AssertThat(SaveCodec.Serialize(built))
                .OverrideFailureMessage("An emptied seed field must keep today's wall-clock behavior.")
                .IsEqual(SaveCodec.Serialize(expected));
            AssertThat(changedTo.Count).IsEqual(1);
        }
        finally
        {
            Unmount(screen);
        }
    }

    [TestCase]
    public void MalformedEntry_RefusesToBegin_AndNamesWhy_NeverFallingBackToARandomSeed()
    {
        TutorialFlow.ResetForReturningSmith();

        var screen = Mount();
        screen.SeedSource = () => 777UL;
        var changedTo = new List<string>();
        screen.SceneChange = changedTo.Add;
        try
        {
            Press(screen, "NewGame");
            Press(screen, "Pick_alchemy");

            Find<LineEdit>(screen, "SeedField").Text = "not-a-seed";
            Press(screen, "Begin");

            // Nothing committed — no silent fallback to a random (or the previously-drawn) seed
            // while the player believes they typed theirs.
            AssertThat(MainUi.AdapterOverride)
                .OverrideFailureMessage("A malformed seed must never silently start a campaign.")
                .IsNull();
            AssertThat(changedTo.Count).IsEqual(0);
            AssertThat(Find<VBoxContainer>(screen, "Primer").Visible).IsTrue();

            // And it says so, on screen, in the player's own words — no "ulong", no "RNG".
            var error = Find<Label>(screen, "SeedError");
            AssertThat(error.Visible).IsTrue();
            AssertThat(error.Text).IsEqual(NewGameSelect.SeedFieldErrorText);
            AssertThat(error.Text).NotContains("ulong");

            // Fixing the entry and pressing Begin again actually works (the refusal is not a dead
            // end) and clears the error along the way.
            Find<LineEdit>(screen, "SeedField").Text = "42";
            Press(screen, "Begin");

            AssertThat(error.Visible).IsFalse();
            var built = MainUi.AdapterOverride!.CurrentState;
            var expected = GameComposition.NewCampaign(42UL, AlchemyProfession.Id);
            AssertThat(SaveCodec.Serialize(built)).IsEqual(SaveCodec.Serialize(expected));
        }
        finally
        {
            Unmount(screen);
        }
    }
}
#endif
