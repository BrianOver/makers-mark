#if GDUNIT_TESTS
using System.Collections.Immutable;
using System.Threading.Tasks;
using GameSim.Crafting;
using GameSim.Professions;
using GdUnit4;
using Godot;
using GodotClient.Minigames;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// fix/u-t1-anvil-can-be-finished: the first suite in this repo that ever actually EXECUTES
/// <see cref="ForgeMinigame"/>'s C3 tap-to-toggle latch — <c>_bellowsGesture</c>'s whole state
/// machine — against a real, ticking clock.
///
/// <para><b>Why every existing forge suite misses this entirely.</b> <c>ForgeWinnabilityTests</c>,
/// <c>ForgeTwoActTests</c> and <c>ForgeCraftTests</c> all drive Act 1 through
/// <c>forge.SetProcess(false)</c> and step the clock by hand via <see cref="ForgeMinigame.Advance"/>
/// (<c>ForgePlayer</c>'s own doc explains why: reproducible, millisecond-cheap runs). But
/// <see cref="ForgeMinigame"/>'s own <c>_GuiInput</c> gates the ENTIRE tap-vs-hold measurement on
/// <c>IsProcessing()</c> — "IsProcessing() is false only when a caller has taken the clock away
/// from the engine... in that case this whole gesture-tracking block is skipped." Every prior forge
/// test takes the clock away, so the latch this fix's softlock rides on has never once run in CI.
/// <c>ForgeSoftlockTests</c> proves the FIX on the scripted clock (no gesture machine involved);
/// this suite proves the FEATURE the fix must not break, and the mouse/keyboard desync fix, on the
/// real one.</para>
///
/// <para><b>Real input only.</b> Every action goes through <see cref="HumanPlayer"/> as an actual
/// <see cref="InputEvent"/> at the viewport — never a direct method call — because the keyboard
/// routing itself is exactly where forge bugs have lived before (see <see cref="HumanPlayer"/>'s own
/// remarks). Waits are condition-based (<see cref="HumanPlayer.WaitUntil"/>), never a frame count:
/// CI runs slower on the wall clock but faster per frame (rendering disabled), so a frame count is
/// not a duration.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ForgeGestureTests
{
    private static readonly Recipe DaggerRecipe = ProfessionRegistry.AllRecipes[ScriptedSession.CraftRecipeId];

    /// <summary>
    /// C3's whole reason for existing, proven on the clock it actually runs against: a Shift press
    /// released well inside <see cref="ForgeMinigame.BellowsTapMaxHoldSeconds"/> must leave the
    /// bellows running past release, and the SAME key's next press must stop them.
    /// </summary>
    [TestCase]
    public async Task AQuickShiftTap_LatchesThePump_AndASecondTapStopsIt()
    {
        var forge = MountForge();
        var player = new HumanPlayer(forge);
        try
        {
            await player.Frames(2); // focus is claimed deferred -- see UiKit.ClaimKeyboard

            AssertThat(forge.IsProcessing())
                .OverrideFailureMessage(
                    "Setup check: this overlay must be ticking its OWN real _Process clock -- if " +
                    "IsProcessing() is false, C3's whole gesture-tracking block is skipped and this " +
                    "test would prove nothing about it.")
                .IsTrue();

            player.Tap(Key.Shift); // press immediately followed by release -- a real tap
            await player.Frames(1);

            AssertThat(forge.IsPumping)
                .OverrideFailureMessage(
                    "A quick Shift tap must latch the bellows ON past release (C3's " +
                    "FilterKeys/StickyKeys/one-handed accessibility escape hatch) -- IsPumping is " +
                    "false right after the tap.")
                .IsTrue();

            player.Tap(Key.Shift); // a second quick tap while toggled on
            await player.Frames(1);

            AssertThat(forge.IsPumping)
                .OverrideFailureMessage("A second quick Shift tap while the bellows are toggled on must stop them -- IsPumping is still true.")
                .IsFalse();
        }
        finally
        {
            player.ReleaseAll();
            forge.Free();
        }
    }

    /// <summary>
    /// Red before this fix, green after. Reproduces the exact desync the owner's brief named: the
    /// on-screen Bellows button's <c>ButtonDown</c>/<c>ButtonUp</c> pair calls
    /// <see cref="ForgeMinigame.BellowsStart"/>/<see cref="ForgeMinigame.BellowsStop"/> directly and
    /// never touched <c>_bellowsGesture</c> at all -- so a keyboard tap-latch left ToggledOn behind,
    /// a MOUSE click stopped the pump without clearing it, and the next Shift press read as "tap
    /// while toggled on, stop it" (a no-op, since the pump was already stopped) instead of starting
    /// the pump. <see cref="ForgeMinigame.BellowsStop"/> is now the single place that clears the
    /// gesture back to Idle, so this same sequence must start the pump on the very next press.
    /// </summary>
    [TestCase]
    public async Task ClickingTheBellowsButtonThenPressingShift_StartsThePumpOnTheFirstPress()
    {
        var forge = MountForge();
        var player = new HumanPlayer(forge);
        try
        {
            await player.Frames(2);

            player.Tap(Key.Shift); // latch the pump via the keyboard's own tap-to-toggle
            await player.Frames(1);

            AssertThat(forge.IsPumping)
                .OverrideFailureMessage("setup: a quick Shift tap must latch the pump before the mouse gets involved")
                .IsTrue();

            await player.Click("Bellows"); // the MOUSE stops it -- a real click, not a method call

            AssertThat(forge.IsPumping)
                .OverrideFailureMessage("setup: clicking the Bellows button must stop the pump")
                .IsFalse();

            player.Hold(Key.Shift); // a fresh press -- must start the pump on THIS press
            await player.Frames(1);

            AssertThat(forge.IsPumping)
                .OverrideFailureMessage(
                    "A fresh Shift press after a mouse-driven stop must start the pump on this very " +
                    "press. False here means a stale ToggledOn gesture left over from the earlier " +
                    "keyboard tap ate this press as a 'stop' instead of a 'start' -- Shift reading as " +
                    "dead until pressed twice.")
                .IsTrue();
        }
        finally
        {
            player.ReleaseAll();
            forge.Free();
        }
    }

    /// <summary>
    /// The hold gesture must keep working bit-for-bit for anyone who holds past the tap window --
    /// C3 changed nothing about this path. Real wall-clock, per this file's own remarks: measured
    /// against <see cref="ForgeMinigame.BellowsTapMaxHoldSeconds"/> itself, not a frame count.
    /// </summary>
    [TestCase]
    public async Task AHeldShiftPastTheTapWindow_StopsOnRelease()
    {
        var forge = MountForge();
        var player = new HumanPlayer(forge);
        try
        {
            await player.Frames(2);

            player.Hold(Key.Shift);
            await player.Frames(1);

            AssertThat(forge.IsPumping)
                .OverrideFailureMessage("setup: holding Shift must start the pump")
                .IsTrue();

            var pressedAtMs = Time.GetTicksMsec();
            var crossedTheWindow = await player.WaitUntil(
                () => (Time.GetTicksMsec() - pressedAtMs) / 1000.0 >= ForgeMinigame.BellowsTapMaxHoldSeconds,
                maxFrames: 600);

            AssertThat(crossedTheWindow)
                .OverrideFailureMessage(
                    "Real elapsed time never crossed BellowsTapMaxHoldSeconds while Shift was held -- " +
                    "cannot tell a hold from a tap, so the assertion below would prove nothing.")
                .IsTrue();

            player.Release(Key.Shift);
            await player.Frames(1);

            AssertThat(forge.IsPumping)
                .OverrideFailureMessage(
                    "A Shift press held past BellowsTapMaxHoldSeconds is a HOLD, not a tap -- " +
                    "releasing it must stop the pump immediately, exactly as it always has.")
                .IsFalse();
        }
        finally
        {
            player.ReleaseAll();
            forge.Free();
        }
    }

    private static ForgeMinigame MountForge()
    {
        var forge = new ForgeMinigame { Name = "ForgeMinigame" };
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(forge);
        forge.Configure(
            DaggerRecipe,
            ScriptedSession.CraftMaterial,
            ProfessionRegistry.Blacksmith,
            ImmutableSortedSet<string>.Empty,
            day: 0);
        return forge;
    }
}
#endif
