#if GDUNIT_TESTS
using System.Collections.Immutable;
using GameSim.Crafting;
using GameSim.Professions;
using GdUnit4;
using GodotClient.Minigames;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// fix/u-t1-anvil-can-be-finished: the owner's own reported state, reproduced exactly —
/// <c>Strike 24/21 — Heat 1000 — pumping — the billet is yielding, keep going</c>. This is the
/// class of test that never ran before this fix: every existing winnability harness
/// (<see cref="ForgePlayer"/>, and this suite's sibling <c>ForgeWinnabilityTests</c>) plays
/// SENSIBLY — it pumps, then stops, then strikes — so none of them can ever wander into "the
/// bellows are latched AND the hammer is pressed," which is exactly the state a player reaches by
/// following the OLD readout's own advice ("keep going") after a quick Shift tap latched the pump
/// (C3's tap-to-toggle) at full heat.
///
/// <para><b>STRIKE IMPLIES RELEASE (owner ruling).</b> A hammer strike that arrives mid-pump now
/// stops the pump and lands — see <see cref="ForgeMinigame.ForgeStrike"/>'s own doc. These tests
/// pin that rule directly against the sim clock (no mounted overlay, no keyboard — see
/// <c>ForgeGestureTests</c> for the real-input half of this fix), the same "construct, Configure,
/// drive the clock by hand" style <c>ForgeWinnabilityTests</c> already uses for its own
/// clock-free cases.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ForgeSoftlockTests
{
    private static readonly Recipe DaggerRecipe = ProfessionRegistry.AllRecipes[ScriptedSession.CraftRecipeId];

    /// <summary>
    /// Brian's literal reported line, reproduced and then closed out. Also pins the two mechanical
    /// halves of the fix in one place: at the heat clamp the pump costs the shape nothing (A3), and
    /// a strike that arrives mid-pump stops the pump before it lands (A1).
    /// </summary>
    [TestCase]
    public void AWhiteHotPumpingBillet_CanStillBeStruckToCompletion()
    {
        var act1 = new ForgeMinigame();
        act1.Configure(DaggerRecipe, ScriptedSession.CraftMaterial, ProfessionRegistry.Blacksmith,
            ImmutableSortedSet<string>.Empty, day: 0, demonstratedAccuracyPermille: 500);

        act1.BellowsStart();

        // 50 steps of 0.1s is 5s of simulated pumping at BellowsRaisePermillePerSecond=260 —
        // enough to clamp heat to 1000 from ANY starting point on the path, well inside the loop
        // bound (worst case, starting at heat 0, needs roughly 1000/260/0.1 ~= 38 steps).
        for (var i = 0; i < 50 && act1.HeatYPermille < 1000; i++)
        {
            act1.Advance(0.1);
        }

        AssertThat(act1.HeatYPermille)
            .OverrideFailureMessage("could not drive heat to the clamp; the test no longer reproduces Brian's state")
            .IsEqual(1000);
        AssertThat(act1.IsPumping)
            .OverrideFailureMessage("setup check: the bellows must still be latched going into the clamp")
            .IsTrue();

        // At the clamp the pump raises heat by zero, so it must cost the shape nothing either —
        // the mechanism behind the reported softlock: heat pinned at 1000 forever, shape draining
        // to 0 with no strike able to land because ForgeStrike() used to no-op while pumping.
        var shapeAtClamp = act1.ShapeXPermille;
        act1.Advance(10.0);

        AssertThat(act1.ShapeXPermille)
            .OverrideFailureMessage(
                $"At full heat the pump does no work, so it must cost nothing: shape moved from " +
                $"{shapeAtClamp} to {act1.ShapeXPermille} across 10s of pumping at the clamp.")
            .IsEqual(shapeAtClamp);
        AssertThat(act1.IsPumping)
            .OverrideFailureMessage("the pump must still be running -- nothing in this test has stopped it yet")
            .IsTrue();

        // Brian's literal reported state: "Strike 24/21 -- Heat 1000 -- pumping -- the billet is
        // yielding, keep going." The strike cap here is deliberately generous rather than tuned to
        // a minimum: this test's own frozen tempo phase (whatever _elapsed happened to land on
        // during the ramp above) is not controlled for, so a strike sequence landing entirely
        // off-beat needs materially more strikes than an on-beat one to close the same distance.
        // The claim under test is "this terminates at all," not "in exactly N strikes" — that is
        // ForgeWinnabilityTests' job, on a policy that actually manages heat.
        var struckOnce = false;
        for (var i = 0; i < 150 && !act1.Completed; i++)
        {
            act1.ForgeStrike();

            if (!struckOnce)
            {
                struckOnce = true;
                AssertThat(act1.IsPumping)
                    .OverrideFailureMessage(
                        "STRIKE IMPLIES RELEASE (owner ruling): the first strike after a latched, " +
                        "full-heat pump must stop the bellows immediately, not leave them running.")
                    .IsFalse();
            }
        }

        AssertThat(act1.Completed)
            .OverrideFailureMessage(
                $"A white-hot billet with the pump latched could not be struck to completion " +
                $"(shape stuck at {act1.ShapeXPermille}/{ForgeMinigame.ShapingFinishPermille}, " +
                $"heat {act1.HeatYPermille}). This is Brian's literal reported softlock: " +
                "\"Strike 24/21 -- Heat 1000 -- pumping -- the billet is yielding, keep going.\"")
            .IsTrue();
    }

    /// <summary>
    /// The other half of A3, isolated from striking entirely: real banked progress must survive an
    /// arbitrarily long stay at the heat clamp. Before the fix, 90s at the clamp would have drained
    /// <c>BellowsDriftBackPermillePerSecond * 90</c> = 720 permille — more than the ENTIRE act's
    /// finish line (666) — silently unwinding a fully-shaped billet back to zero while the player
    /// did nothing wrong.
    /// </summary>
    [TestCase]
    public void PumpingAtFullHeat_NeverUnwindsBankedShape()
    {
        var act1 = new ForgeMinigame();
        act1.Configure(DaggerRecipe, ScriptedSession.CraftMaterial, ProfessionRegistry.Blacksmith,
            ImmutableSortedSet<string>.Empty, day: 0, demonstratedAccuracyPermille: 1000);

        // Bank real shape via ordinary strikes, topping up heat between them so a strike is never
        // forced down to the cold-strike floor -- this test is about the PUMP's behaviour at the
        // clamp, not the striking economy, so getting to ~500 permille should be quick and clean.
        for (var i = 0; i < 40 && act1.ShapeXPermille < 500 && !act1.Completed; i++)
        {
            if (act1.HeatYPermille < 900)
            {
                act1.BellowsStart();
                act1.Advance(5.0); // clamps to 1000 well before this returns
                act1.BellowsStop();
            }

            act1.ForgeStrike();
        }

        AssertThat(act1.Completed)
            .OverrideFailureMessage("the craft finished before banking ~500 permille of shape -- the setup loop needs a smaller target or fewer iterations")
            .IsFalse();
        AssertThat(act1.ShapeXPermille)
            .OverrideFailureMessage($"could not bank at least 500 permille of shape before the clamp test; only reached {act1.ShapeXPermille}")
            .IsGreaterEqual(500);

        // Climb to the clamp FIRST, and only then snapshot. The strikes above spent heat, so this
        // ramp runs below 1000 — where the drift-back is real work honestly paid for, and the trade
        // this test is not about. Snapshotting before the ramp charged that legitimate cost to the
        // ninety-second hold and read as a 40 permille leak at full heat that was never there.
        act1.BellowsStart();
        act1.Advance(5.0);

        AssertThat(act1.HeatYPermille)
            .OverrideFailureMessage("setup check: heat must be at the clamp before the 90s hold below means anything")
            .IsEqual(1000);

        var banked = act1.ShapeXPermille;

        act1.Advance(90.0);

        AssertThat(act1.ShapeXPermille)
            .OverrideFailureMessage(
                $"Banked shape unwound from {banked} to {act1.ShapeXPermille} across 90s of pumping " +
                "at the heat clamp. At full heat the bellows do no work and must cost the shape " +
                "nothing -- a fully-shaped billet must not silently decay while the player holds " +
                "the bellows at max heat.")
            .IsEqual(banked);
    }

    /// <summary>
    /// The copy half of the fix, mirrors <c>ForgeWinnabilityTests.AColdBillet_TellsThePlayerToWorkTheBellows</c>:
    /// the readout must never send the player back to the one input that cannot help. At the heat
    /// clamp that input is the bellows, not the hammer — the exact inverse of the cold-billet case.
    /// </summary>
    [TestCase]
    public void AWhiteHotPumpingBillet_TellsThePlayerToStrike_NeverToKeepPumping()
    {
        var act1 = new ForgeMinigame();
        act1.Configure(DaggerRecipe, ScriptedSession.CraftMaterial, ProfessionRegistry.Blacksmith,
            ImmutableSortedSet<string>.Empty, day: 0, demonstratedAccuracyPermille: 500);

        // Drive well past the strike budget without ever pumping, so AssistEngaged (overrun) goes
        // true well before the shape can possibly close -- mirrors
        // ForgeWinnabilityTests.AColdBilletStillMoves_SoTheActCanAlwaysClose's own setup.
        for (var i = 0; i < 40 && !act1.AssistEngaged && !act1.Completed; i++)
        {
            act1.ForgeStrike();
        }

        AssertThat(act1.Completed)
            .OverrideFailureMessage("the craft finished before the strike budget overran -- the test no longer reaches AssistEngaged")
            .IsFalse();
        AssertThat(act1.AssistEngaged)
            .OverrideFailureMessage($"could not overrun the strike budget (RequiredStrikes {act1.RequiredStrikes}) in 40 strikes -- StrikesLanded={act1.StrikesLanded}")
            .IsTrue();

        // Now latch the pump to the heat clamp -- Brian's literal reported combination: overrun
        // AND pumping AND full heat, all at once.
        act1.BellowsStart();
        for (var i = 0; i < 50 && act1.HeatYPermille < 1000; i++)
        {
            act1.Advance(0.1);
        }

        AssertThat(act1.HeatYPermille)
            .OverrideFailureMessage("could not drive heat to the clamp while pumping")
            .IsEqual(1000);

        var readout = act1.ReadoutText.ToLowerInvariant();

        AssertThat(readout)
            .OverrideFailureMessage(
                $"At heat {act1.HeatYPermille} with the strike budget overrun, the readout says " +
                $"\"{act1.ReadoutText}\" -- it must tell the player to swing/strike, never to keep " +
                "pumping or \"keep going\" (the one thing that no longer helps at the clamp).")
            .NotContains("keep going");

        AssertThat(readout.Contains("swing") || readout.Contains("strike"))
            .OverrideFailureMessage($"Readout \"{act1.ReadoutText}\" never names swinging or striking even though the billet is white-hot and the strike budget is overrun.")
            .IsTrue();
    }
}
#endif
