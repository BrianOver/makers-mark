#if GDUNIT_TESTS
using GdUnit4;
using Godot;
using GodotClient.Town2d;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// M1 (2026-07-28 animation plan, Part 1): <see cref="SpriteMotion"/> is a plain, engine-free
/// pose driver (only <see cref="Vector2"/> value structs, no node/scene/runtime), so — like
/// <c>JourneyFeedTests</c>'s coverage of <c>JourneyPlayhead</c> — none of these need
/// <c>[RequireGodotRuntime]</c>.
/// </summary>
[TestSuite]
public class SpriteMotionTests
{
    private const float WalkSpeed = 260f; // HeroActor2D.WalkSpeed scale

    [TestCase]
    public void Advance_SameInputSequence_ProducesIdenticalPoseSequence()
    {
        // Determinism (KTD2/KTD4): identical (delta, velocity, walkSpeed, phaseSeed) sequences
        // must land on identical poses, frame for frame.
        var deltas = new[] { 0.016, 0.016, 0.016, 0.033, 0.016, 0.05, 0.016 };
        var velocities = new[]
        {
            new Vector2(0, 0),
            new Vector2(120, 0),
            new Vector2(150, 30),
            new Vector2(-80, 0),
            new Vector2(0, 0),
            new Vector2(200, -50),
            new Vector2(5, 5),
        };

        var a = new SpriteMotion(phaseSeed: 3.14f);
        var b = new SpriteMotion(phaseSeed: 3.14f);

        for (var i = 0; i < deltas.Length; i++)
        {
            var poseA = a.Advance(deltas[i], velocities[i], WalkSpeed);
            var poseB = b.Advance(deltas[i], velocities[i], WalkSpeed);

            AssertThat(poseA.BobY).IsEqual(poseB.BobY);
            AssertThat(poseA.LeanRadians).IsEqual(poseB.LeanRadians);
            AssertThat(poseA.Scale).IsEqual(poseB.Scale);
            AssertThat(poseA.StepFrameB).IsEqual(poseB.StepFrameB);
        }
    }

    [TestCase]
    public void Advance_VelocityAtOrBelowThreshold_ReadsAsIdle_ZeroBobNoLean()
    {
        // "Walking" = speed > ~20px/s (plan §Part 1) — at/under the threshold must read idle:
        // zero bob, zero lean, no step-frame flip.
        var motion = new SpriteMotion(phaseSeed: 0f);

        var atThreshold = motion.Advance(0.1, new Vector2(20f, 0f), WalkSpeed);
        AssertThat(atThreshold.BobY).IsEqual(0f);
        AssertThat(atThreshold.LeanRadians).IsEqual(0f);
        AssertThat(atThreshold.StepFrameB).IsFalse();

        var belowThreshold = motion.Advance(0.1, new Vector2(5f, 5f), WalkSpeed);
        AssertThat(belowThreshold.BobY).IsEqual(0f);
        AssertThat(belowThreshold.LeanRadians).IsEqual(0f);
    }

    [TestCase]
    public void Advance_VelocityAboveThreshold_Walks_NonZeroBobPossible()
    {
        // Above the threshold, the walk branch is live — over a full step cycle the bob must
        // depart from zero (idle would stay pinned at exactly 0 the whole time).
        var motion = new SpriteMotion(phaseSeed: 0f);
        var sawNonZeroBob = false;

        for (var i = 0; i < 30; i++)
        {
            var pose = motion.Advance(0.05, new Vector2(150f, 0f), WalkSpeed);
            if (pose.BobY != 0f)
            {
                sawNonZeroBob = true;
            }
        }

        AssertThat(sawNonZeroBob).IsTrue();
    }

    [TestCase]
    public void Idle_BobIsAlwaysZero_RegardlessOfElapsedTime()
    {
        var motion = new SpriteMotion(phaseSeed: 1.5f);

        for (var i = 0; i < 20; i++)
        {
            var pose = motion.Advance(0.1, Vector2.Zero, WalkSpeed);
            AssertThat(pose.BobY).IsEqual(0f);
            AssertThat(pose.LeanRadians).IsEqual(0f);
            AssertThat(pose.StepFrameB).IsFalse();
        }
    }

    [TestCase]
    public void Walk_LeanSign_FollowsVelocityX()
    {
        var motion = new SpriteMotion(phaseSeed: 0f);

        var leaningRight = motion.Advance(0.05, new Vector2(100f, 0f), WalkSpeed);
        AssertThat(leaningRight.LeanRadians).IsGreater(0f);

        var leaningLeft = motion.Advance(0.05, new Vector2(-100f, 0f), WalkSpeed);
        AssertThat(leaningLeft.LeanRadians).IsLess(0f);
    }

    [TestCase]
    public void Walk_LeanMagnitude_NeverExceedsMaxAndScalesWithSpeedRatio()
    {
        var slow = new SpriteMotion(phaseSeed: 0f);
        var fast = new SpriteMotion(phaseSeed: 0f);

        var slowPose = slow.Advance(0.05, new Vector2(30f, 0f), WalkSpeed); // low speed ratio
        var fastPose = fast.Advance(0.05, new Vector2(WalkSpeed, 0f), WalkSpeed); // full pace

        AssertThat(Mathf.Abs(slowPose.LeanRadians)).IsLessEqual(SpriteMotion.LeanMaxRadians);
        AssertThat(Mathf.Abs(fastPose.LeanRadians)).IsLessEqual(SpriteMotion.LeanMaxRadians);
        AssertThat(Mathf.Abs(fastPose.LeanRadians)).IsGreater(Mathf.Abs(slowPose.LeanRadians));
    }

    [TestCase]
    public void Idle_Breathing_DesyncsAcrossDifferentPhaseSeeds()
    {
        // Two idle actors with different phaseSeed must NOT breathe in lockstep — the whole
        // point of seeding phase from a deterministic per-actor id (mirrors the existing
        // HeroActor2D lissajous-wander id->motion idiom).
        var motionA = new SpriteMotion(phaseSeed: 0f);
        var motionB = new SpriteMotion(phaseSeed: 1.9f);

        var sawDivergence = false;
        for (var i = 0; i < 10; i++)
        {
            var poseA = motionA.Advance(0.1, Vector2.Zero, WalkSpeed);
            var poseB = motionB.Advance(0.1, Vector2.Zero, WalkSpeed);
            if (Mathf.Abs(poseA.Scale.Y - poseB.Scale.Y) > 0.0001f)
            {
                sawDivergence = true;
            }
        }

        AssertThat(sawDivergence).IsTrue();
    }

    [TestCase]
    public void Idle_Breathing_StaysWithinDocumentedAmplitude()
    {
        var motion = new SpriteMotion(phaseSeed: 0.7f);

        for (var i = 0; i < 40; i++)
        {
            var pose = motion.Advance(0.05, Vector2.Zero, WalkSpeed);
            AssertFloat(pose.Scale.Y).IsBetween(1f - SpriteMotion.BreathAmplitude - 0.0001f, 1f + SpriteMotion.BreathAmplitude + 0.0001f);
            // Scale.X moves inverse to Scale.Y (volume-preserving read) — sums to ~2 (tiny
            // float-rounding tolerance since X and Y are computed via independent +/- ops).
            AssertFloat(pose.Scale.X + pose.Scale.Y).IsBetween(1.999f, 2.001f);
        }
    }

    /// <summary>U-T3-5 (register #141): the correction plus the position it was computed from must
    /// always land on a whole pixel, for a wide range of fractional inputs — including negative
    /// ones (the lissajous wander drifts on both sides of Home) and inputs already exactly on the
    /// grid (the correction must be a true no-op there, never a spurious ±1px nudge).</summary>
    [TestCase]
    public void PixelSnapCorrection_AppliedToItsInput_AlwaysLandsOnAWholePixel()
    {
        Vector2[] inputs =
        {
            new(0f, 0f),
            new(10f, 20f), // already whole
            new(10.49f, -20.49f),
            new(10.51f, -20.51f),
            new(-14.999f, 9.001f),
            new(0.5f, -0.5f), // exact half — Godot's banker's-adjacent Mathf.Round must still land whole
            new(123.456f, -987.654f),
        };

        foreach (var position in inputs)
        {
            var correction = SpriteMotion.PixelSnapCorrection(position);
            var drawn = position + correction;

            AssertFloat(drawn.X).IsEqual(Mathf.Round(drawn.X));
            AssertFloat(drawn.Y).IsEqual(Mathf.Round(drawn.Y));
        }
    }

    /// <summary>An already-whole-pixel position must get a zero correction — a correction that
    /// nudges a position already on the grid would itself reintroduce the exact jitter this unit
    /// exists to remove.</summary>
    [TestCase]
    public void PixelSnapCorrection_OnAlreadyWholePixel_IsZero()
    {
        var correction = SpriteMotion.PixelSnapCorrection(new Vector2(42f, -17f));
        AssertThat(correction).IsEqual(Vector2.Zero);
    }

    [TestCase]
    public void FeetCompensationFormula_KeepsSpriteBottomEdgeAtBobY_RegardlessOfSquashScale()
    {
        // Verify the exact consumer-side contract documented on SpriteMotion.Pose: applying
        // Offset.Y = -h/2 + BobY + h/2*(1 - Scale.Y) makes the sprite's local bottom edge
        // (Offset.Y + h/2*Scale.Y) equal to BobY alone, independent of any squash/breathe scale.
        var motion = new SpriteMotion(phaseSeed: 0f);
        float[] textureHeights = { 24f, 40f, 64f };

        for (var i = 0; i < 25; i++)
        {
            var pose = motion.Advance(0.017, new Vector2(180f, 0f), WalkSpeed);
            foreach (var h in textureHeights)
            {
                var offsetY = -h / 2f + pose.BobY + h / 2f * (1f - pose.Scale.Y);
                var bottomEdge = offsetY + h / 2f * pose.Scale.Y;
                AssertFloat(bottomEdge).IsBetween(pose.BobY - 0.001f, pose.BobY + 0.001f);
            }
        }
    }
}
#endif
