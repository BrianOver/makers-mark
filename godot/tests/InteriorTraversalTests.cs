#if GDUNIT_TESTS
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// Can the player WALK once they are inside a room, and does the tutorial still tell them what to do?
///
/// <para><b>Why this exists.</b> The 2026-08-03 playtest: <i>"I am unable to move around inside the
/// forge"</i>, <i>"Unable to leave the forge via 'E' or moving - escape worked"</i>, <i>"The tutorial
/// is missing?"</i>, <i>"i was unable to post as i couldn't leave the forge so stuck on tutorial 3"</i>.
/// Four reports, one cause: <c>Town.InteriorActive</c> joined <c>MainUi.ModalOwnsTheScreen()</c> in
/// #349, <c>UpdateEngaged</c> fed that to <c>Town.SetWorldInputEnabled(!engaged)</c>, and the player
/// was frozen the instant they stepped inside. The room's exit is a zone you WALK ONTO, so a frozen
/// player could never leave; Escape survived only because it is a UI rung. Station clicks survived via
/// Area2D picking, so the room looked half-alive — menus opened, legs did not work. The tutorial card
/// was not missing either: it was hidden by the same predicate.</para>
///
/// <para><b>Why the whole suite stayed green through it.</b> Every existing interior test either drives
/// a bare <c>Town2D</c> and calls <c>EnterInterior</c> directly (see
/// <c>InteriorEntryExitTests</c>'s own header) — which never runs <c>MainUi.UpdateEngaged</c>, the code
/// that did the freezing — or asserts the OPPOSITE property: that a wall BLOCKS the player. A test
/// proving you cannot walk through walls does not prove you can walk. So these tests deliberately
/// (a) go in through MainUi by CLICKING the building the way a player does, and (b) assert the player
/// actually TRAVERSES the room.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class InteriorTraversalTests
{
    /// <summary>Physics ticks to give the body — movement is <c>_PhysicsProcess</c>-driven, so layout
    /// frames alone prove nothing.</summary>
    private const int PhysicsTicks = 12;

    [TestCase]
    public async Task ClickingIntoTheForge_LeavesThePlayerAbleToWalk()
    {
        var ui = MountMainUi();
        try
        {
            var forge = ui.Town.FindBuilding("forge");

            // The REAL click path: Area2D input picking -> Building2D.Picked -> Town2D.BuildingClicked
            // -> MainUi.OnTownBuildingClicked. Going through MainUi is the whole point; calling
            // Town.EnterInterior directly is what let this regression hide.
            var clicked = TryClickArea(forge.Interact, forge.DoorAnchorGlobal);
            AssertThat(clicked)
                .OverrideFailureMessage("Could not click the forge's interact area — the test cannot prove anything.")
                .IsTrue();
            await SettleLayout(ui);

            AssertThat(ui.Town.InteriorActive)
                .OverrideFailureMessage("Clicking the forge did not put the player inside the room.")
                .IsTrue();

            var before = ui.Town.Player.GlobalPosition;

            // SetDirectInput is the supported seam for headless movement (OS key state is not
            // reliable here), and it still proves the gate: PlayerController2D._PhysicsProcess checks
            // _inputEnabled and returns BEFORE it ever reads _directInput. So if world input is
            // disabled, this moves nothing — which is exactly the bug.
            ui.Town.Player.SetDirectInput(Vector2.Right);
            for (var i = 0; i < PhysicsTicks; i++)
            {
                await SettlePhysics(ui);
            }

            ui.Town.Player.SetDirectInput(null);
            var after = ui.Town.Player.GlobalPosition;

            AssertThat(after.X > before.X + 1.0f)
                .OverrideFailureMessage(
                    $"The player did not move inside the forge: x {before.X} -> {after.X} after "
                    + $"{PhysicsTicks} physics ticks of held right-input. World input is disabled while "
                    + "the room is open. Check MainUi.UpdateEngaged: the input gate must ask "
                    + "'is a drawer or overlay open?' (worldInputBlocked), NOT 'does something cover the "
                    + "screen?' (engaged) — a walkable room answers yes to the second and must still be "
                    + "walkable. This is the bug behind \"I am unable to move around inside the forge\".")
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// The instruction must survive the room. Hiding it here is the worst possible moment: the card
    /// vanishes exactly while the player is inside the building it told them to enter, so they cannot
    /// tell whether they already did the thing. That produced the report "The tutorial is missing?"
    /// while he was in fact stuck on step 3 and knew it.
    /// </summary>
    [TestCase]
    public async Task InsideTheForge_TheTutorialCardStaysReadable()
    {
        var ui = MountMainUi();
        try
        {
            ui.Dev_QueueDay1TutorialLadder();
            await SettleLayout(ui);

            if (!ui.Tutorial.Active)
            {
                // Nothing to assert about a card that is legitimately not running; say so loudly
                // rather than passing silently on a vacuous condition.
                AssertThat(ui.Tutorial.Active)
                    .OverrideFailureMessage(
                        "The tutorial is not active after Dev_QueueDay1TutorialLadder, so this test "
                        + "cannot check that it stays readable. Fix the setup, do not delete the test.")
                    .IsTrue();
            }

            var forge = ui.Town.FindBuilding("forge");
            AssertThat(TryClickArea(forge.Interact, forge.DoorAnchorGlobal)).IsTrue();
            await SettleLayout(ui);

            AssertThat(ui.Town.InteriorActive).IsTrue();
            AssertThat(ui.Objective.Visible)
                .OverrideFailureMessage(
                    "The tutorial card is hidden while the player stands inside the forge. An "
                    + "instruction you cannot read while carrying it out is worse than none. "
                    + "MainUi.UpdateEngaged's keepTutorialReadable must include the room, not only "
                    + "Drawer.IsOpen — a room gives the drawer no chance to be open.")
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }
}
#endif
