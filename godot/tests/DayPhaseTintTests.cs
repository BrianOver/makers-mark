#if GDUNIT_TESTS
using GameSim.Contracts;
using GdUnit4;
using Godot;
using GodotClient.Town2d;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// Animation-gap fix (#4, "day/night is a lie"): <see cref="DayPhaseTint"/> is a plain, engine-free
/// pure class (only <see cref="Color"/> value structs, no node/scene/runtime dependency) — like
/// <c>SpriteMotionTests</c>'s coverage of <see cref="SpriteMotion"/>, none of these need
/// <c>[RequireGodotRuntime]</c>.
/// </summary>
[TestSuite]
public class DayPhaseTintTests
{
    [TestCase]
    public void TintFor_EveryPhase_ReturnsADistinctTint()
    {
        var morning = DayPhaseTint.TintFor(DayPhase.Morning);
        var expedition = DayPhaseTint.TintFor(DayPhase.Expedition);
        var evening = DayPhaseTint.TintFor(DayPhase.Evening);
        var camp = DayPhaseTint.TintFor(DayPhase.Camp);
        var deep = DayPhaseTint.TintFor(DayPhase.ExpeditionDeep);

        AssertThat(morning).IsNotEqual(expedition);
        AssertThat(expedition).IsNotEqual(evening);
        AssertThat(evening).IsNotEqual(camp);
        AssertThat(camp).IsNotEqual(deep);
        AssertThat(morning).IsNotEqual(deep);
    }

    [TestCase]
    public void TintFor_Evening_MatchesTheOriginalFixedDuskTint()
    {
        // The palette-identity guarantee: Evening must stay the EXACT original dusk color so the
        // established purple-dusk mood is preserved, not replaced by a new scheme.
        AssertThat(DayPhaseTint.TintFor(DayPhase.Evening)).IsEqual(new Color(0.86f, 0.80f, 0.93f));
    }

    [TestCase]
    public void TintFor_EveryPhase_StaysWithinThePurpleDuskFamily()
    {
        // "A shift in tint across phases, not a new color scheme": every stop's Blue channel must
        // stay the dominant-or-tied channel (the hue-family signature of a purple/lavender/violet
        // palette), across the full Morning→ExpeditionDeep brightness range.
        foreach (var phase in new[] { DayPhase.Morning, DayPhase.Expedition, DayPhase.Evening, DayPhase.Camp, DayPhase.ExpeditionDeep })
        {
            var tint = DayPhaseTint.TintFor(phase);
            AssertThat(tint.B >= tint.R)
                .OverrideFailureMessage($"{phase} tint {tint} broke the purple-family hue (Blue should be >= Red)")
                .IsTrue();
            AssertThat(tint.B >= tint.G)
                .OverrideFailureMessage($"{phase} tint {tint} broke the purple-family hue (Blue should be >= Green)")
                .IsTrue();
        }
    }

    [TestCase]
    public void TintFor_DarknessOrdering_MorningBrightestExpeditionDeepDarkest()
    {
        // Legibility contract: brightness must monotonically read Morning (brightest) through
        // ExpeditionDeep (darkest) — the whole point of the fix is that the sky visibly answers
        // "what time is it".
        var morning = DayPhaseTint.TintFor(DayPhase.Morning);
        var expedition = DayPhaseTint.TintFor(DayPhase.Expedition);
        var evening = DayPhaseTint.TintFor(DayPhase.Evening);
        var camp = DayPhaseTint.TintFor(DayPhase.Camp);
        var deep = DayPhaseTint.TintFor(DayPhase.ExpeditionDeep);

        float Brightness(Color c) => c.R + c.G + c.B;

        AssertThat(Brightness(morning)).IsGreater(Brightness(expedition));
        AssertThat(Brightness(expedition)).IsGreater(Brightness(evening));
        AssertThat(Brightness(evening)).IsGreater(Brightness(camp));
        AssertThat(Brightness(camp)).IsGreater(Brightness(deep));
    }

    [TestCase]
    public void Advance_SameInputSequence_ProducesIdenticalTintSequence()
    {
        // Determinism (KTD2/KTD4): identical (initial, phase sequence, delta sequence) must land
        // on identical tints, frame for frame — mirrors SpriteMotionTests' own determinism case.
        var phases = new[] { DayPhase.Morning, DayPhase.Morning, DayPhase.Expedition, DayPhase.Evening, DayPhase.Evening, DayPhase.Camp };
        var deltas = new[] { 0.016, 0.016, 0.033, 0.016, 0.05, 0.016 };

        var a = new DayPhaseTint(DayPhaseTint.TintFor(DayPhase.Morning));
        var b = new DayPhaseTint(DayPhaseTint.TintFor(DayPhase.Morning));

        for (var i = 0; i < phases.Length; i++)
        {
            var tintA = a.Advance(deltas[i], phases[i]);
            var tintB = b.Advance(deltas[i], phases[i]);

            AssertThat(tintA).IsEqual(tintB);
        }
    }

    [TestCase]
    public void Advance_NeverSnaps_FirstFrameStaysBetweenStartAndTarget()
    {
        // "Ease between phases ... do not snap": one moderate-delta frame toward a very different
        // target must land somewhere IN BETWEEN — not jump straight to the target color.
        var tint = new DayPhaseTint(DayPhaseTint.TintFor(DayPhase.Morning));

        var result = tint.Advance(0.1, DayPhase.ExpeditionDeep);

        var start = DayPhaseTint.TintFor(DayPhase.Morning);
        var target = DayPhaseTint.TintFor(DayPhase.ExpeditionDeep);

        AssertThat(result).IsNotEqual(start);
        AssertThat(result).IsNotEqual(target);
        // Every channel must have moved TOWARD the target, not past it.
        AssertFloat(result.R).IsBetween(Mathf.Min(start.R, target.R), Mathf.Max(start.R, target.R));
        AssertFloat(result.G).IsBetween(Mathf.Min(start.G, target.G), Mathf.Max(start.G, target.G));
        AssertFloat(result.B).IsBetween(Mathf.Min(start.B, target.B), Mathf.Max(start.B, target.B));
    }

    [TestCase]
    public void Advance_RepeatedTicksTowardSamePhase_ConvergesCloseToTarget()
    {
        var tint = new DayPhaseTint(DayPhaseTint.TintFor(DayPhase.Morning));
        var target = DayPhaseTint.TintFor(DayPhase.ExpeditionDeep);

        Color last = default;
        for (var i = 0; i < 200; i++)
        {
            last = tint.Advance(0.1, DayPhase.ExpeditionDeep);
        }

        AssertFloat(last.R).IsBetween(target.R - 0.01f, target.R + 0.01f);
        AssertFloat(last.G).IsBetween(target.G - 0.01f, target.G + 0.01f);
        AssertFloat(last.B).IsBetween(target.B - 0.01f, target.B + 0.01f);
    }

    [TestCase]
    public void Advance_HeldOnSamePhase_StaysPinnedAtItsTint_NoDrift()
    {
        var target = DayPhaseTint.TintFor(DayPhase.Evening);
        var tint = new DayPhaseTint(target);

        for (var i = 0; i < 50; i++)
        {
            var result = tint.Advance(0.1, DayPhase.Evening);
            AssertThat(result).IsEqual(target);
        }
    }

    [TestCase]
    public void Constructor_SeedsCurrent_WithNoAdvanceCallNeeded()
    {
        var seed = DayPhaseTint.TintFor(DayPhase.Camp);
        var tint = new DayPhaseTint(seed);

        AssertThat(tint.Current).IsEqual(seed);
    }
}
#endif
