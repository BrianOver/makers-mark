#if GDUNIT_TESTS
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
/// P2-ONBOARD-05 (docs/design/MAKERS-MARK.md §11.15, "The Warrant ships"): the ONE distinction
/// this unit exists to prove — a fresh profile's first blacksmith Begin builds its campaign from
/// <see cref="NewGameSelect.WarrantSeed"/> (== <c>OpeningCampaignPinTests.ChosenSeed</c>), and
/// every other door (a different profession, or any pick once
/// <see cref="TutorialFlow.HasPriorProgress"/> is true) draws wall-clock exactly as before this
/// unit — see <see cref="NewGameSelect.WarrantSeed"/>'s own doc for why both conditions are
/// required and why a non-blacksmith pick is not owed the pin.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class WarrantSeedTests
{
    private static NewGameSelect Mount()
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        var screen = GD.Load<PackedScene>("res://scenes/new_game_select.tscn").Instantiate<NewGameSelect>();
        tree.Root.AddChild(screen);
        return screen;
    }

    private static void UnmountScreen(NewGameSelect screen)
    {
        MainUi.AdapterOverride = null;
        MainUi.FirstMorningBeatPending = false;
        screen.GetParent()?.RemoveChild(screen);
        screen.Free();
    }

    [TestCase]
    public void FreshProfile_FirstBlacksmithBegin_BuildsTheCampaignFromThePinnedWarrantSeed()
    {
        TutorialFlow.DeleteForTests(); // guarantee HasPriorProgress reads false, same as a true first-timer

        var screen = Mount();
        screen.SeedSource = () => 999999UL; // proves the pin wins over SeedSource, not merely a coincidence
        screen.SceneChange = _ => { };
        try
        {
            Press(screen, "NewGame");
            Press(screen, "Pick_blacksmith");

            // The primer prints the Warrant's name where a raw seed number would otherwise print —
            // never the seed itself (P2-HONEST's own "no raw ... number" rule read for a seed).
            var seedLabel = Find<Label>(screen, "SeedLabel");
            AssertThat(seedLabel.Text)
                .OverrideFailureMessage($"Expected the Warrant's fiction name, got '{seedLabel.Text}'.")
                .Contains("The Warrant");
            AssertThat(seedLabel.Text).NotContains("999999");

            Press(screen, "Begin");

            var built = MainUi.AdapterOverride!.CurrentState;
            var expected = GameComposition.NewCampaign(NewGameSelect.WarrantSeed, ProfessionRegistry.BlacksmithId);
            AssertThat(SaveCodec.Serialize(built))
                .OverrideFailureMessage("A fresh profile's first blacksmith Begin did not build from the pinned Warrant seed.")
                .IsEqual(SaveCodec.Serialize(expected));
        }
        finally
        {
            UnmountScreen(screen);
            TutorialFlow.DeleteForTests();
        }
    }

    [TestCase]
    public void FreshProfile_NonBlacksmithPick_DrawsWallClock_NeverThePin()
    {
        TutorialFlow.DeleteForTests();

        var screen = Mount();
        screen.SeedSource = () => 424242UL;
        screen.SceneChange = _ => { };
        try
        {
            Press(screen, "NewGame");
            Press(screen, "Pick_alchemy");

            // No pin outside blacksmith (ApprenticePlayer's one gear recipe, "dagger", is a
            // blacksmith recipe — see WarrantSeed's own doc) — the ordinary seed display stands.
            AssertThat(Find<Label>(screen, "SeedLabel").Text).IsEqual("Seed: 424242");

            Press(screen, "Begin");

            var built = MainUi.AdapterOverride!.CurrentState;
            var expected = GameComposition.NewCampaign(424242UL, AlchemyProfession.Id);
            AssertThat(SaveCodec.Serialize(built)).IsEqual(SaveCodec.Serialize(expected));
        }
        finally
        {
            UnmountScreen(screen);
            TutorialFlow.DeleteForTests();
        }
    }

    [TestCase]
    public void SecondCampaign_BlacksmithPick_DrawsWallClock_NeverThePin()
    {
        // Simulates "a second campaign" the same way ReturningSmithTests does: seed a prior-
        // campaign fact so HasPriorProgress reads true, then pick again — matching NewGameSelect's
        // own class doc: "a second campaign ... draw[s] wall-clock exactly as now."
        TutorialFlow.ResetForReturningSmith();

        var screen = Mount();
        screen.SeedSource = () => 777777UL;
        screen.SceneChange = _ => { };
        try
        {
            Press(screen, "NewGame");
            Press(screen, "Pick_blacksmith");

            AssertThat(Find<VBoxContainer>(screen, "ReturningSmithChoice").Visible).IsTrue();
            AssertThat(Find<Label>(screen, "SeedLabel").Text)
                .OverrideFailureMessage("A returning smith's pick must never be pinned to the Warrant seed.")
                .IsEqual("Seed: 777777");

            // "Run the course" stays the default (never pressed SkipCourse) — the pin is gated on
            // HasPriorProgress alone, independent of which returning-smith choice gets made.
            Press(screen, "Begin");

            var built = MainUi.AdapterOverride!.CurrentState;
            var expected = GameComposition.NewCampaign(777777UL, ProfessionRegistry.BlacksmithId);
            AssertThat(SaveCodec.Serialize(built)).IsEqual(SaveCodec.Serialize(expected));
        }
        finally
        {
            UnmountScreen(screen);
            TutorialFlow.DeleteForTests();
        }
    }
}
#endif
