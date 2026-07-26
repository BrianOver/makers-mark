#if GDUNIT_TESTS
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// 3D-client soak: drive a real MainUi deep into a session (many days) and confirm the whole client
/// — including the new Phase C/D surfaces (Confidence/Assessment/Act chips, the progression panel,
/// the per-venue Depths tiles + den threat) — keeps rendering without throwing at LATE-game states
/// (maxed ladders, escalated dens, a possibly-Ended arc) that the per-feature tests don't reach.
/// This is the "rounds of play" pass for the 3D game.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class PhaseD3dSoakTests
{
    [TestCase]
    public void Client_SurvivesADeepSession_AndAllPhaseDSurfacesStillRenderLateGame()
    {
        var ui = MountMainUi();
        try
        {
            // Play deep — enough to escalate dens, advance the arc, and mature every ladder.
            AdvanceDay(ui, 45);

            // The HUD kept its Phase C/D chips through the whole run.
            AssertThat(Find<Control>(ui, "ConfidenceChip")).IsNotNull();
            AssertThat(Find<Control>(ui, "AssessmentChip")).IsNotNull();
            AssertThat(Find<Control>(ui, "ActChip")).IsNotNull();

            // The progression panel opens and renders all five ladders at a matured state.
            Press(ui, "OpenProgress");
            var progress = RenderedText(ui.Progress);
            AssertThat(progress).Contains("Forge");
            AssertThat(progress).Contains("Depth");
            AssertThat(progress).Contains("Roster");
            AssertThat(progress).Contains("Wealth");
            AssertThat(progress).Contains("Chronicle");

            // Both live venues still render, with den state, deep into the session.
            ui.Depths.Refresh();
            var depths = RenderedText(ui.Depths);
            AssertThat(depths).Contains("Mine");
            AssertThat(depths).Contains("Gloomwood");
            AssertThat(depths.ToLowerInvariant()).Contains("den");

            // Sanity: the sim actually advanced deep (not stuck early).
            AssertThat(ui.Adapter.CurrentState.Day >= 40).IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }
}
#endif
