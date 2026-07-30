#if GDUNIT_TESTS
using System.Linq;
using GdUnit4;
using Godot;
using GodotClient.Town2d;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// The player must be physically detectable, or E-interact is dead.
///
/// <para><b>Why this exists:</b> Brian's human playtest (2026-07-29) found that pressing E at the
/// forge did nothing. <c>PlayerController2D</c> is a <see cref="CharacterBody2D"/> that never added a
/// <see cref="CollisionShape2D"/>, so it was invisible to
/// <see cref="Area2D.GetOverlappingBodies"/>. <c>WorldInput2D.FindNearestOverlapping</c> therefore
/// always returned null, no building ever became the active target, and E was PERMANENTLY dead —
/// never once working, in any build. The same absence meant the player walked through every
/// building's footprint, so the town had no solidity either.</para>
///
/// <para><b>Why nothing caught it:</b> the existing tests drive interaction through
/// <c>WorldInput2D.SetTarget</c> and <c>TriggerInteract()</c> — seams that hand the system a target
/// directly and skip the physics query that decides whether a target can EXIST. They proved the
/// interact plumbing worked downstream of the step that was broken. Same failure shape as
/// <c>Building2D.RaisePick()</c> hiding the dead click path: a seam that skips the broken layer
/// reports success about something it never exercised.</para>
///
/// <para>So these tests assert the physical preconditions — the player has a shape, and a building's
/// interact area actually reports the player as overlapping when they stand in its doorway. That
/// second one is the assertion the seam tests could not make.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class PlayerCanInteractTests
{
    [TestCase]
    public void Player_HasACollisionShape_SoAreasCanDetectIt()
    {
        var town = new Town2D();
        try
        {
            // Must be in the tree: _Ready is where the player builds its sprite and body, and it
            // only fires on entering the tree. Asserting on an out-of-tree node tests nothing.
            AddNodeToTree(town);
            town.Build(new SimAdapter(2026));

            var shapes = town.Player.GetChildren().OfType<CollisionShape2D>().ToList();

            AssertThat(shapes.Count)
                .OverrideFailureMessage(
                    "PlayerController2D has no CollisionShape2D. A shapeless CharacterBody2D is " +
                    "invisible to Area2D.GetOverlappingBodies(), so E-interact can never find a " +
                    "target and the player walks through every building.")
                .IsGreater(0);

            AssertThat(shapes[0].Shape).IsNotNull();
        }
        finally
        {
            town.Free();
        }
    }

    /// <summary>
    /// The assertion the seam tests could not make: stand the player in the forge's doorway and
    /// confirm the interact area actually reports them. This is the exact query
    /// <c>WorldInput2D.FindNearestOverlapping</c> runs, so if this passes, E has a target.
    /// </summary>
    [TestCase]
    public async System.Threading.Tasks.Task StandingInTheDoorway_TheForgeReportsThePlayerOverlapping()
    {
        var town = new Town2D { Name = "Town2D" };
        try
        {
            town.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            AddNodeToTree(town);
            town.Build(new SimAdapter(2026));

            // MUST come before any frame await. This is the only async runtime test that mounts a
            // Town2D, and Town2D owns a live SubViewport — awaiting a tree signal with one rendering
            // is the known gdUnit headless hang this project has been bitten by before. It killed
            // CI on the first push of this file: the Godot runtime runner never reported, so EVERY
            // [RequireGodotRuntime] suite vanished and the engine job went 502 tests -> 68 (the
            // pure-.NET remainder), exit 137. Physics is unaffected by the render target, so
            // disabling it costs this test nothing.
            town.WorldViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;

            var forge = town.FindBuilding("forge");
            town.Player.GlobalPosition = forge.DoorAnchorGlobal;

            // Physics overlap state is only valid after a physics step has run.
            await AwaitPhysicsFrames(4);

            var overlapping = forge.Interact.GetOverlappingBodies();

            AssertThat(overlapping.Contains(town.Player))
                .OverrideFailureMessage(
                    "The forge's Interact area does not report the player even with the player " +
                    "standing on its door anchor. WorldInput2D.FindNearestOverlapping will return " +
                    "null, so E-interact is dead. Check the player's CollisionShape2D and that the " +
                    "area's collision mask includes the player's layer.")
                .IsTrue();
        }
        finally
        {
            town.Free();
        }
    }

    private static void AddNodeToTree(Node node) => ((SceneTree)Engine.GetMainLoop()).Root.AddChild(node);

    private static async System.Threading.Tasks.Task AwaitPhysicsFrames(int frames)
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        for (var i = 0; i < frames; i++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        }
    }
}
#endif
