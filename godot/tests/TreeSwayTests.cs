#if GDUNIT_TESTS
using GdUnit4;
using Godot;
using GodotClient.Town2d;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// Animation-gap fix (#2, "trees never move"): <see cref="TreeSway"/> is a plain, engine-free pure
/// class (mirrors <c>SpriteMotionTests</c>'s own convention) — no node/scene/runtime dependency,
/// so none of these need <c>[RequireGodotRuntime]</c>.
/// </summary>
[TestSuite]
public class TreeSwayTests
{
    [TestCase]
    public void Advance_SameInputSequence_ProducesIdenticalRotationSequence()
    {
        // Determinism (KTD2/KTD4): identical (phaseSeed, delta sequence) must land on identical
        // rotations, frame for frame.
        var deltas = new[] { 0.016, 0.016, 0.033, 0.05, 0.016 };

        var a = new TreeSway(phaseSeed: 1.2f);
        var b = new TreeSway(phaseSeed: 1.2f);

        for (var i = 0; i < deltas.Length; i++)
        {
            AssertThat(a.Advance(deltas[i])).IsEqual(b.Advance(deltas[i]));
        }
    }

    [TestCase]
    public void Advance_OverAFullCycle_ActuallyMoves_NotFrozen()
    {
        var sway = new TreeSway(phaseSeed: 0f);
        var min = float.MaxValue;
        var max = float.MinValue;

        for (var i = 0; i < 200; i++)
        {
            var rotation = sway.Advance(0.05);
            min = Mathf.Min(min, rotation);
            max = Mathf.Max(max, rotation);
        }

        AssertThat(max - min > 0.001f)
            .OverrideFailureMessage("a tree prop must actually sway over time, not stay rigid")
            .IsTrue();
    }

    [TestCase]
    public void Advance_NeverExceedsDocumentedAmplitude()
    {
        var sway = new TreeSway(phaseSeed: 0.4f);

        for (var i = 0; i < 300; i++)
        {
            var rotation = sway.Advance(0.033);
            AssertFloat(Mathf.Abs(rotation)).IsLessEqual(TreeSway.SwayAmplitudeRadians + 0.0001f);
        }
    }

    [TestCase]
    public void Advance_DesyncsAcrossDifferentPhaseSeeds()
    {
        // The whole point of per-instance phase (mirrors AmbientLife2D's per-lamppost flicker
        // idiom): two trees with different phaseSeed must NOT sway in lockstep.
        var treeA = new TreeSway(phaseSeed: 0f);
        var treeB = new TreeSway(phaseSeed: 2.4f);

        var sawDivergence = false;
        for (var i = 0; i < 20; i++)
        {
            var rotationA = treeA.Advance(0.1);
            var rotationB = treeB.Advance(0.1);
            if (Mathf.Abs(rotationA - rotationB) > 0.0001f)
            {
                sawDivergence = true;
            }
        }

        AssertThat(sawDivergence).IsTrue();
    }

    [TestCase]
    public void Advance_ZeroDelta_HoldsCurrentRotation()
    {
        var sway = new TreeSway(phaseSeed: 0.8f);
        var first = sway.Advance(0.05);
        var held = sway.Advance(0.0);

        AssertThat(held).IsEqual(first);
    }
}
#endif
