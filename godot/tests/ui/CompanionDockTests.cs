#if GDUNIT_TESTS
using System.Linq;
using System.Threading.Tasks;
using GameSim;
using GameSim.Contracts;
using GameSim.Drama;
using GdUnit4;
using Godot;
using GodotClient;
using GodotClient.Minigames;
using GodotClient.Panels;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// Register #160, U-T2-3/U-T2-4 ("Tomorrow at the Counter" becomes openable while crafting): the
/// owner's one praised screen was, before this unit, the one thing in the game it was
/// structurally impossible to keep open during a craft — four separate mechanisms conspired to
/// make that so (see <see cref="CompanionDock"/>'s own class doc for all four). This suite pins
/// the fix: a <c>CanvasLayer</c> companion that owns no screen, and the one test the whole unit
/// exists for — opening it mid-craft must never cancel the craft.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class CompanionDockTests
{
    [TestCase]
    public void Dock_IsNotAnOverlaySurface_SoTheTutorialCardStaysVisible()
    {
        var ui = MountMainUi();
        try
        {
            AssertThat(ui.Tutorial.Active)
                .OverrideFailureMessage("setup check: a fresh campaign must start the tutorial chain for this scenario to mean anything.")
                .IsTrue();

            ui.Docket.Open();
            AssertThat(ui.Docket.IsExpanded).IsTrue();

            // Force UpdateEngaged to re-run with the Docket the only thing open (OpenPanel("Town")
            // is a safe no-op here — the drawer is already closed — but it still re-derives
            // AnOverlayOwnsTheScreen/Objective.Visible from scratch at the end of every call).
            ui.OpenPanel("Town");

            AssertThat(ui.Docket.IsExpanded).IsTrue();
            AssertThat(ui.Objective.Visible)
                .OverrideFailureMessage(
                    "Opening the Docket hid the tutorial/objective card — it must never be treated as a " +
                    "screen-owning overlay (MainUi.OverlaySurfaces).")
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void Dock_NeverBlocksTownInput_NorLatchesTheClock()
    {
        var ui = MountMainUi();
        try
        {
            AssertThat(ui.Clock.Engaged).IsFalse();
            AssertThat(ui.Town.WorldInputNode.Enabled).IsTrue();

            ui.Docket.Open();
            ui.OpenPanel("Town"); // re-run UpdateEngaged with the Docket the only thing open

            AssertThat(ui.Docket.IsExpanded).IsTrue();
            AssertThat(ui.Clock.Engaged)
                .OverrideFailureMessage(
                    "Opening the Docket engaged the clock latch — a companion must never hold the clock the " +
                    "way a modal does.")
                .IsFalse();
            AssertThat(ui.Town.WorldInputNode.Enabled)
                .OverrideFailureMessage("Opening the Docket blocked town input — it must behave like a companion, never a modal.")
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void Dock_DrawsAboveTheDrawerVeil_AndItsToggleIsClickableWhileADrawerIsOpen()
    {
        var ui = MountMainUi();
        try
        {
            var layer = Find<CanvasLayer>(ui, "CompanionLayer");
            AssertThat(layer.Layer)
                .OverrideFailureMessage("CompanionLayer must sit above every layer-0 sibling, the drawer's veil included.")
                .IsGreater(0);
            AssertThat(layer.Layer)
                .OverrideFailureMessage("CompanionLayer must sit below TabFade (layer 100) so a tab transition still covers it.")
                .IsLess(100);

            ui.OpenPanel("Forge");
            AssertThat(ui.Drawer.IsOpen).IsTrue();
            AssertThat(ui.Drawer.Veil.Visible)
                .OverrideFailureMessage("setup check: the drawer's own dim veil must actually be up for this test to mean anything.")
                .IsTrue();

            PressEnabled(ui, "DocketToggle");

            AssertThat(ui.Docket.IsExpanded)
                .OverrideFailureMessage("The Docket toggle did not respond while a drawer was open — the veil is eating its click.")
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public async Task Dock_NeverIntersectsTheDrawerCard_AtMinimumWindowWidth()
    {
        var ui = MountMainUi();
        try
        {
            ui.Size = new Vector2(1152, 648); // the project's minimum supported window (LayoutTests' own idiom)

            ui.OpenPanel("Forge");
            ui.Drawer.Tick(DrawerHost.SlideSeconds); // settle the slide fully in one call
            ui.Docket.Open();

            await SettleLayout(ui);

            var dockRect = Find<Control>(ui.Docket, "DocketCard").GetGlobalRect();
            var drawerRect = ui.Drawer.CurrentContent!.GetGlobalRect();

            AssertThat(dockRect.Intersects(drawerRect))
                .OverrideFailureMessage(
                    $"Docket card at {dockRect} overlaps the open drawer at {drawerRect} at the 1152x648 " +
                    "design floor — bottom-left vs right-anchored must never collide.")
                .IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void Docket_AndModalBoard_RenderIdenticalRows_FromOneBuilder()
    {
        var ui = MountMainUi(new SimAdapter(GapWorld(seed: 9101)));
        try
        {
            var state = ui.Adapter.CurrentState;
            AssertThat(CounterForecast.Queue(state).IsEmpty)
                .OverrideFailureMessage("setup check: this fixture must produce a real counter queue.")
                .IsFalse();

            ui.Forecast.ShowForTomorrow(state);
            var modalRows = Find<VBoxContainer>(ui.Forecast, "ForecastBody").GetChildren().OfType<Control>().ToList();

            ui.Docket.Open();
            var docketRows = Find<VBoxContainer>(ui.Docket, "DocketBody").GetChildren().OfType<Control>().ToList();

            AssertThat(docketRows.Count)
                .OverrideFailureMessage("The Docket rendered zero rows for a fixture with a real counter queue.")
                .IsGreater(0);

            // The modal's body renders the counter section FIRST, then its own party rows — so the
            // Docket's rows (counter section ONLY) must match the modal's own leading rows one for one.
            AssertThat(modalRows.Count)
                .OverrideFailureMessage("setup check: the modal must render at least as many rows as the Docket.")
                .IsGreaterEqual(docketRows.Count);

            for (var i = 0; i < docketRows.Count; i++)
            {
                AssertThat(RowText(docketRows[i]))
                    .OverrideFailureMessage(
                        $"Row {i} differs between the two hosts — Docket says \"{RowText(docketRows[i])}\", " +
                        $"modal says \"{RowText(modalRows[i])}\". Both must come from the same CounterSectionBuilder.")
                    .IsEqual(RowText(modalRows[i]));
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>The test this whole unit exists for: opening the Docket mid-craft must never
    /// trip ForgePanel's own <c>NotificationVisibilityChanged</c> cancel path.</summary>
    [TestCase]
    public void OpeningTheDocket_MidForgeMinigame_DoesNotCancelTheCraft()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.WorldViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;

            OpenAnvilMap(ui);
            var overlay = Find<ForgeMinigame>(ui.Forge, "ForgeMinigame");
            AssertThat(overlay.Visible).IsTrue();
            AssertThat(overlay.WasCancelled).IsFalse();

            ui.Docket.Open();

            AssertThat(ui.Docket.IsExpanded).IsTrue();
            AssertThat(ui.Forge.Visible)
                .OverrideFailureMessage("Opening the Docket hid the Forge drawer — it must never touch DrawerHost.")
                .IsTrue();
            AssertThat(overlay.Visible)
                .OverrideFailureMessage("Opening the Docket hid the running craft overlay.")
                .IsTrue();
            AssertThat(overlay.WasCancelled)
                .OverrideFailureMessage(
                    "Opening the Docket cancelled the running craft. This is THE regression the whole unit " +
                    "exists to prevent — a companion layer must never trigger ForgePanel's own " +
                    "NotificationVisibilityChanged cancel path.")
                .IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── fixtures ─────────────────────────────────────────────────────────────────────────────

    private static string RowText(Control c) => c switch
    {
        Label l => l.Text,
        Button b => b.Text,
        _ => c.Name.ToString(),
    };

    /// <summary>A fresh (blacksmith-default) campaign with its lowest-HeroId hero's gear cleared
    /// — mirrors <c>RaidForecastBoardTests.GapWorld</c> (that hero is guaranteed to head the
    /// queue: every starting hero shares the Stranger band, so ties break on HeroId ascending).</summary>
    private static GameState GapWorld(ulong seed)
    {
        var baseState = GameComposition.NewCampaign(seed);
        var hero = baseState.Heroes.Values.First();
        var bare = hero with { Gear = GearSet.Empty };
        return baseState with { Heroes = baseState.Heroes.SetItem(bare.Id.Value, bare) };
    }

    /// <summary>Mirrors <c>MinigameOpensPlayableTests.OpenAnvilMap</c> exactly: buy the dagger's
    /// copper, open the Forge drawer, and press "Work the forge" to open the real craft overlay.</summary>
    private static void OpenAnvilMap(MainUi ui)
    {
        ui.Adapter.Queue(new BuyMaterialAction(ScriptedSession.CraftMaterial, ScriptedSession.CopperNeeded));
        ui.Adapter.AdvancePhase();
        ui.OpenPanel("Forge");
        PressEnabled(ui.Forge, $"WorkForge_{ScriptedSession.CraftRecipeId}");
    }
}
#endif
