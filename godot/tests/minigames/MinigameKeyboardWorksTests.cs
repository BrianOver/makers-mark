#if GDUNIT_TESTS
using System.Collections.Immutable;
using System.Threading.Tasks;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Professions;
using GdUnit4;
using Godot;
using GodotClient.Minigames;
using GodotClient.Ui;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// The minigames' keyboard controls must actually respond to keys pressed on a keyboard.
///
/// <para><b>Why this exists:</b> all four minigame overlays set
/// <c>FocusMode = FocusModeEnum.All</c> — one of them with the comment "so _GuiInput actually
/// receives keyboard events" — and none of them ever called <c>GrabFocus()</c>. A
/// <see cref="Control"/> only receives key events in <c>_GuiInput</c> while it HAS focus, so every
/// keyboard control in every minigame was dead. Brian's playtest (2026-07-30): "shift and space
/// doesn't work... also doesn't seem possible to complete? the shape keeps resetting to zero".</para>
///
/// <para>In the forge that made the craft <b>unwinnable</b>, not just clumsy: heat drains
/// continuously, and a strike's shape-advance is proportional to current heat. With the bellows
/// (Shift) unreachable, heat floors, strikes stop advancing, and the bellows-pump drag pushes shape
/// back toward zero. One unreachable modifier key read as a broken game.</para>
///
/// <para><b>Why the existing suites could not see it:</b> <c>ForgeMinigameTests</c> and friends drive
/// <c>BellowsStart()</c>/<c>ForgeStrike()</c> directly and never even add the overlay to the tree —
/// deliberately, for determinism. Those seams are correct for scoring math and useless for "can a
/// human operate this", because focus is exactly what they skip. So these tests push REAL
/// <see cref="InputEventKey"/>s through the viewport and assert on game state.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class MinigameKeyboardWorksTests
{
    private static readonly Recipe DaggerRecipe = ProfessionRegistry.AllRecipes[ScriptedSession.CraftRecipeId];

    /// <summary>
    /// The reported bug, end to end: hold Shift as a real key event and the bellows must raise heat.
    /// Heat, not a flag — the flag is what the seam already proved.
    /// </summary>
    [TestCase]
    public async Task HoldingShift_AsARealKeyEvent_RaisesHeat()
    {
        var mg = new ForgeMinigame { Name = "ForgeMinigame" };
        try
        {
            AddNodeToTree(mg);
            mg.Configure(DaggerRecipe, ScriptedSession.CraftMaterial, ProfessionRegistry.Blacksmith, ImmutableSortedSet<string>.Empty, day: 0);

            // Focus is claimed deferred (see UiKit.ClaimKeyboard), so let the build settle first.
            await AwaitFrames(2);

            var before = mg.HeatYPermille;

            PushKey(mg, Key.Shift, pressed: true);
            mg.Advance(0.5);

            AssertThat(mg.HeatYPermille)
                .OverrideFailureMessage(
                    $"Heat went {before} -> {mg.HeatYPermille} while Shift was held as a real key " +
                    "event. The bellows never engaged, so heat only drains — and since a strike's " +
                    "shape-advance is proportional to heat, the craft can never be completed. The " +
                    "overlay almost certainly does not hold keyboard focus (FocusMode alone does not " +
                    "grant it — see UiKit.ClaimKeyboard).")
                .IsGreater(before);
        }
        finally
        {
            PushKey(mg, Key.Shift, pressed: false);
            mg.Free();
        }
    }

    /// <summary>
    /// THE REGRESSION MY FIRST FIX MISSED. Click an on-screen control button, THEN use the keyboard.
    ///
    /// <para>A focused <see cref="Button"/> consumes Space and Enter to press itself. So clicking the
    /// visible "Bellows (hold Shift)" button once moved focus to it permanently: from then on Space
    /// pumped the bellows instead of striking the billet, and Shift reached nothing at all. Brian's
    /// second playtest, verbatim: "shift doesn't do and space seems to actually be the bellows" — after
    /// the previous fix had already made the keyboard work on open.</para>
    ///
    /// <para>The earlier test only ever pressed keys on a freshly opened overlay, which is the one state
    /// where the bug is invisible. Same shape as every other miss this project has had: the check looked
    /// exactly where the defect was not.</para>
    /// </summary>
    [TestCase]
    public async Task AfterClickingAControlButton_TheKeyboardStillBelongsToTheOverlay()
    {
        var mg = new ForgeMinigame { Name = "ForgeMinigame" };
        try
        {
            AddNodeToTree(mg);
            mg.Configure(DaggerRecipe, ScriptedSession.CraftMaterial, ProfessionRegistry.Blacksmith, ImmutableSortedSet<string>.Empty, day: 0);
            await AwaitFrames(2);

            // A REAL mouse press on the button. EmitSignal(Pressed) does NOT move focus — I wrote this
            // test that way first and it passed against the unguarded build, i.e. it was another seam
            // test for the exact seam this whole class of bug hides behind. Focus only moves when Godot
            // actually routes a click to the control.
            var hammer = mg.FindChild("HammerStrike", recursive: true, owned: false) as Button;
            AssertThat(hammer).OverrideFailureMessage("HammerStrike button not found.").IsNotNull();

            var at = hammer!.GetGlobalRect().GetCenter();
            AssertThat(hammer.GetGlobalRect().Size.X)
                .OverrideFailureMessage("The button has no laid-out size, so a click cannot land on it.")
                .IsGreater(1f);

            var viewport = mg.GetViewport();
            viewport.PushInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at,
            });
            viewport.PushInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at,
            });
            await AwaitFrames(2);

            AssertThat(hammer.HasFocus())
                .OverrideFailureMessage(
                    "A control button took keyboard focus. While it holds focus, Space presses THAT " +
                    "button instead of striking, and Shift never reaches the overlay at all — which is " +
                    "exactly the reported bug. These buttons must be mouse-only (UiKit.MakeButtonsMouseOnly).")
                .IsFalse();

            // And prove the keys still work, not merely that focus looks right.
            var before = mg.HeatYPermille;
            PushKey(mg, Key.Shift, pressed: true);
            mg.Advance(0.5);

            AssertThat(mg.HeatYPermille)
                .OverrideFailureMessage(
                    $"After clicking a control button, holding Shift moved heat {before} -> " +
                    $"{mg.HeatYPermille}. The bellows are unreachable from the keyboard, so the craft " +
                    "cannot be completed.")
                .IsGreater(before);
        }
        finally
        {
            PushKey(mg, Key.Shift, pressed: false);
            mg.Free();
        }
    }

    /// <summary>Space is the accessible unaimed strike. Real key event, and shape must advance.</summary>
    [TestCase]
    public async Task PressingSpace_AsARealKeyEvent_StrikesTheBillet()
    {
        var mg = new ForgeMinigame { Name = "ForgeMinigame" };
        try
        {
            AddNodeToTree(mg);
            mg.Configure(DaggerRecipe, ScriptedSession.CraftMaterial, ProfessionRegistry.Blacksmith, ImmutableSortedSet<string>.Empty, day: 0);
            await AwaitFrames(2);

            var before = mg.ShapeXPermille;

            PushKey(mg, Key.Space, pressed: true);
            PushKey(mg, Key.Space, pressed: false);

            AssertThat(mg.ShapeXPermille)
                .OverrideFailureMessage(
                    $"Shape went {before} -> {mg.ShapeXPermille} on a real Space press. Space is the " +
                    "keyboard-accessible strike; if it does nothing, the only way to shape the billet " +
                    "is clicking exactly on it.")
                .IsGreater(before);
        }
        finally
        {
            mg.Free();
        }
    }

    /// <summary>
    /// The generic guard, for the two overlays whose key handler lives on a nested canvas rather than
    /// on the overlay itself — a distinction that already caused a wrong-node fix during this work.
    /// Focus landing ANYWHERE inside the overlay is the precondition; which descendant it is depends
    /// on where that overlay put its handler.
    /// </summary>
    [TestCase]
    public async Task EveryKeyboardDrivenOverlay_HoldsFocusSomewhereInsideItself()
    {
        var bench = new EngineeringBench { Name = "EngineeringBench" };
        var tanning = new TanningFrame { Name = "TanningFrame" };
        try
        {
            AddNodeToTree(bench);
            AddNodeToTree(tanning);
            await AwaitFrames(2);

            // One at a time: focus is global to the viewport, so two mounted overlays cannot both
            // hold it. Re-claim each in turn and check it actually landed.
            foreach (var (overlay, label) in new (Control, string)[] { (bench, "EngineeringBench"), (tanning, "TanningFrame") })
            {
                UiKit.ClaimKeyboard(overlay);
                await AwaitFrames(2);

                var owner = overlay.GetViewport().GuiGetFocusOwner();
                var inside = owner is not null && (owner == overlay || overlay.IsAncestorOf(owner));

                AssertThat(inside)
                    .OverrideFailureMessage(
                        $"{label} does not hold keyboard focus (owner: {owner?.Name.ToString() ?? "<null>"}). " +
                        "Its keys are dead. Note the handler may live on a nested canvas Control — in " +
                        "that case focus must go on the CANVAS, not on the overlay panel.")
                    .IsTrue();
            }
        }
        finally
        {
            bench.Free();
            tanning.Free();
        }
    }

    /// <summary>Pushes a real key event through the viewport, exactly as the OS would deliver it —
    /// both <c>Keycode</c> and <c>PhysicalKeycode</c>, since handlers in this codebase match on
    /// either.</summary>
    private static void PushKey(Node context, Key key, bool pressed) =>
        context.GetViewport().PushInput(new InputEventKey
        {
            Keycode = key,
            PhysicalKeycode = key,
            Pressed = pressed,
            Echo = false,
        });

    private static void AddNodeToTree(Node node) => ((SceneTree)Engine.GetMainLoop()).Root.AddChild(node);

    private static async Task AwaitFrames(int frames)
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        for (var i = 0; i < frames; i++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }
    }
}
#endif
