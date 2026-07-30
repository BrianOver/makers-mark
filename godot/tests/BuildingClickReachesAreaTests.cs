#if GDUNIT_TESTS
using GdUnit4;
using Godot;
using GodotClient.Town2d;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// The town's SubViewport must forward real mouse input to the buildings' pick areas.
///
/// <para><b>Why this test exists:</b> a human playtest on 2026-07-29 found that NO building could be
/// entered by clicking, which made the game unplayable past the town — you could walk around and
/// never get into the forge. The cause was <c>SubViewport.HandleInputLocally = true</c>, which makes
/// a SubViewport ignore input forwarded by its parent <see cref="SubViewportContainer"/>, so a click
/// never reached <c>Building2D</c>'s <see cref="Area2D"/> and its <c>InputEvent</c> handler never
/// fired.</para>
///
/// <para><b>Why nothing caught it:</b> every existing playtest and engine test enters a building
/// through <c>Building2D.RaisePick()</c> — the documented test seam — which bypasses viewport input
/// entirely. The seam proved the ROUTING was correct while the only path a player actually has was
/// dead. A seam that skips the failing layer cannot defend that layer, so this test asserts the
/// viewport CONFIGURATION that real picking depends on instead of re-testing the routing.</para>
///
/// <para>Deliberately a configuration assertion rather than a synthesized click: driving a real
/// mouse event through a SubViewportContainer's coordinate transform needs a rendered frame, and
/// pumping frames while a viewport renders is the known gdUnit headless hang this project has
/// already been bitten by. Pinning the two flags is the honest, cheap guard.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class BuildingClickReachesAreaTests
{
    [TestCase]
    public void TownViewport_ForwardsParentInput_SoBuildingClicksCanPick()
    {
        var town = new Town2D();
        try
        {
            town.Build(new SimAdapter(2026));

            AssertThat(town.WorldViewport).IsNotNull();

            // The regression itself. True here means a click can never reach a building.
            AssertThat(town.WorldViewport.HandleInputLocally)
                .OverrideFailureMessage(
                    "SubViewport.HandleInputLocally is true — the viewport will ignore input forwarded " +
                    "by its SubViewportContainer, so clicking a building does nothing and the game is " +
                    "unplayable past the town. It must be false.")
                .IsFalse();

            // Picking also has to be on, or Area2D.InputEvent never fires even with input arriving.
            AssertThat(town.WorldViewport.PhysicsObjectPicking)
                .OverrideFailureMessage("PhysicsObjectPicking is off — Area2D.InputEvent will never fire.")
                .IsTrue();
        }
        finally
        {
            town.Free();
        }
    }

    /// <summary>
    /// The buildings themselves must stay pickable. Both halves are required: the viewport forwards
    /// the event, and the area accepts it.
    /// </summary>
    [TestCase]
    public void EveryBuilding_KeepsAnInputPickableInteractArea()
    {
        var town = new Town2D();
        try
        {
            town.Build(new SimAdapter(2026));

            foreach (var key in new[] { "forge", "market", "tavern", "minegate", "noticeboard" })
            {
                var building = town.FindBuilding(key);
                AssertThat(building.Interact).OverrideFailureMessage($"{key} has no Interact area").IsNotNull();
                AssertThat(building.Interact.InputPickable)
                    .OverrideFailureMessage($"{key}'s Interact area is not InputPickable — it cannot be clicked")
                    .IsTrue();
                AssertThat(building.Interact.Monitoring)
                    .OverrideFailureMessage($"{key}'s Interact area is not Monitoring — E-interact proximity dies")
                    .IsTrue();
            }
        }
        finally
        {
            town.Free();
        }
    }
}
#endif
