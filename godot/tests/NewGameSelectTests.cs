#if GDUNIT_TESTS
using System.Collections.Generic;
using System.Linq;
using GameSim.Kernel;
using GameSim.Professions;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U4 module engine-lane scenarios: the new-game profession select renders one button per
/// registered profession from the real scene file, and pressing one builds a seeded campaign
/// through the real <see cref="GameSim.GameComposition"/> path into
/// <see cref="MainUi.AdapterOverride"/>. The scene change is stubbed (injectable
/// <see cref="NewGameSelect.SceneChange"/>) so the test tree is never torn down.
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
    public void RendersOneButtonPerRegisteredProfession()
    {
        var screen = Mount();
        try
        {
            AssertThat(ProfessionRegistry.All.Count).IsEqual(4);
            foreach (var profession in ProfessionRegistry.All.Values)
            {
                var button = Find<Button>(screen, $"Pick_{profession.Id}");
                AssertThat(button.Text).IsEqual(profession.DisplayName);
            }

            // Exactly the four profession buttons — no extra "classic" default path (scope pin).
            var layout = Find<VBoxContainer>(screen, "Layout");
            var buttons = layout.GetChildren().OfType<Button>().Count();
            AssertThat(buttons).IsEqual(4);
        }
        finally
        {
            Unmount(screen);
        }
    }

    [TestCase]
    public void PickTanning_BuildsSeededCampaign_IntoAdapterOverride_AndRequestsMainScene()
    {
        var screen = Mount();
        var changedTo = new List<string>();
        screen.SceneChange = changedTo.Add; // stub — no real scene swap in the test tree
        try
        {
            MainUi.AdapterOverride = null;
            Press(screen, "Pick_tanning");

            AssertThat(MainUi.AdapterOverride).IsNotNull();
            var state = MainUi.AdapterOverride!.CurrentState;
            AssertThat(string.Join(",", state.Player.SelectedProfessions)).IsEqual("tanning");
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
}
#endif
