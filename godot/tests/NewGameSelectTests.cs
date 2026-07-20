#if GDUNIT_TESTS
using System.Collections.Generic;
using System.Linq;
using GameSim.Contracts;
using GameSim.Kernel;
using GameSim.Professions;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U4/World-Rework-U11 engine-lane scenarios: the new-game front door renders one button per
/// registered profession from the real scene file with a blurb + shared starter-kit note
/// (R9), pressing one reveals the "your first day" primer (5-phase legend, clock note, seed —
/// R11-13) WITHOUT touching <see cref="MainUi.AdapterOverride"/>, Back returns to the picker
/// with nothing committed, and only Begin builds a seeded campaign through the real
/// <see cref="GameSim.GameComposition"/> path into <see cref="MainUi.AdapterOverride"/>. The
/// scene change is stubbed (injectable <see cref="NewGameSelect.SceneChange"/>) so the test
/// tree is never torn down.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class NewGameSelectTests
{
    private static NewGameSelect Mount()
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        var screen = GD.Load<PackedScene>("res://scenes/new_game_select.tscn").Instantiate<NewGameSelect>();
        tree.Root.AddChild(screen); // triggers _Ready: buttons built from ProfessionRegistry
        return screen;
    }

    private static void Unmount(NewGameSelect screen)
    {
        MainUi.AdapterOverride = null; // never leak a picked campaign into later suites
        screen.GetParent()?.RemoveChild(screen);
        screen.Free();
    }

    [TestCase]
    public void RendersOneButtonPerRegisteredProfession_WithBlurbs_AndHiddenPrimer()
    {
        var screen = Mount();
        try
        {
            AssertThat(ProfessionRegistry.All.Count).IsEqual(4);
            foreach (var profession in ProfessionRegistry.All.Values)
            {
                var button = Find<Button>(screen, $"Pick_{profession.Id}");
                AssertThat(button.Text).IsEqual(profession.DisplayName);

                // Per-profession blurb (R9): present and non-empty for every registered craft.
                var blurb = Find<Label>(screen, $"Blurb_{profession.Id}");
                AssertThat(blurb.Text).IsNotEmpty();
            }

            // Exactly the four profession buttons under the picker — no extra "classic" default
            // path (scope pin), and the shared starter-kit note is present.
            var picker = Find<VBoxContainer>(screen, "ProfessionPicker");
            var buttons = picker.GetChildren().OfType<Button>().Count();
            AssertThat(buttons).IsEqual(4);
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
    public void Pick_ShowsPrimer_ListingAllFivePhases_WithClockNoteAndSeed_NeverTouchingAdapter()
    {
        var screen = Mount();
        screen.SeedSource = () => 999UL;
        try
        {
            MainUi.AdapterOverride = null;
            Press(screen, "Pick_blacksmith");

            // Picker hides, primer shows — no campaign built yet (a pick is reversible).
            AssertThat(Find<VBoxContainer>(screen, "ProfessionPicker").Visible).IsFalse();
            AssertThat(Find<VBoxContainer>(screen, "Primer").Visible).IsTrue();
            AssertThat(MainUi.AdapterOverride).IsNull();

            // 5-phase day, one line each, verbatim MainUi.PhaseLegend (R12) — never drifts.
            var phaseLegend = Find<Label>(screen, "PhaseLegend");
            AssertThat(phaseLegend.Text).IsEqual(MainUi.PhaseLegend);
            var lines = phaseLegend.Text.Split('\n');
            AssertThat(lines.Length).IsEqual(5);
            foreach (var phaseName in new[] { "Morning", "Expedition", "Camp", "Deep", "Evening" })
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

    [TestCase]
    public void Back_ReturnsToPicker_WithoutLeakingCampaign_AndPickIsStillUsableAfter()
    {
        var screen = Mount();
        var changedTo = new List<string>();
        screen.SceneChange = changedTo.Add;
        try
        {
            MainUi.AdapterOverride = null;
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
}
#endif
