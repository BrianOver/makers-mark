#if GDUNIT_TESTS
using GdUnit4;
using Godot;
using GodotClient.Town;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// LW2 direct unit coverage for <see cref="SpeechBubble"/>'s pop-in/hold/fade-out lifecycle,
/// isolated from the full gossip/bark routing pipeline (covered separately in TownSceneTests via
/// the real event-batch/cap/cooldown/dedupe path).
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class SpeechBubbleTests
{
    [TestCase]
    public void Setup_StartsInvisibleAndPoppingIn()
    {
        var bubble = new SpeechBubble();
        try
        {
            bubble.Setup("A steal, that was.");
            AssertThat(bubble.State).IsEqual(SpeechBubble.BubbleState.PoppingIn);
            AssertThat(bubble.Modulate.A).IsEqual(0f);
            AssertThat(bubble.Scale).IsEqual(new Vector2(0.85f, 0.85f));
            AssertThat(bubble.Line).IsEqual("A steal, that was.");
            AssertThat(bubble.IsReaction).IsFalse();
            AssertThat(bubble.IsDone).IsFalse();
        }
        finally
        {
            bubble.Free();
        }
    }

    [TestCase]
    public void Advance_PopsInThenSettlesToFullyVisible()
    {
        var bubble = new SpeechBubble();
        try
        {
            bubble.Setup("Nice find!");

            bubble.Advance(SpeechBubble.PopInSeconds / 2);
            AssertThat(bubble.State).IsEqual(SpeechBubble.BubbleState.PoppingIn);
            AssertThat(bubble.Modulate.A > 0f && bubble.Modulate.A < 1f).IsTrue();

            bubble.Advance(SpeechBubble.PopInSeconds); // more than enough to finish popping in
            AssertThat(bubble.State).IsEqual(SpeechBubble.BubbleState.Holding);
            AssertThat(bubble.Modulate.A).IsEqual(1f);
            AssertThat(bubble.Scale).IsEqual(Vector2.One);
        }
        finally
        {
            bubble.Free();
        }
    }

    [TestCase]
    public void Advance_HoldsForFullDurationBeforeFading()
    {
        var bubble = new SpeechBubble();
        try
        {
            bubble.Setup("Worth every coin.");
            bubble.Advance(SpeechBubble.PopInSeconds); // through pop-in, into Holding

            bubble.Advance(SpeechBubble.HoldSeconds - 0.1); // just short of the hold window
            AssertThat(bubble.State).IsEqual(SpeechBubble.BubbleState.Holding);
            AssertThat(bubble.Modulate.A).IsEqual(1f);

            bubble.Advance(0.2); // crosses into FadingOut
            AssertThat(bubble.State).IsEqual(SpeechBubble.BubbleState.FadingOut);
            AssertThat(bubble.Modulate.A < 1f).IsTrue();
        }
        finally
        {
            bubble.Free();
        }
    }

    [TestCase]
    public void Advance_BigFastForward_CarriesAllTheWayToDone()
    {
        // Same big-Advance-call contract HeroSprite/TownScene already honor: a single large
        // delta resolves pop-in + hold + fade-out in one call (cascading leftover time forward),
        // never needing thousands of small engine-frame ticks.
        var bubble = new SpeechBubble();
        try
        {
            bubble.Setup("This'll do.");
            bubble.Advance(SpeechBubble.PopInSeconds + SpeechBubble.HoldSeconds + SpeechBubble.FadeOutSeconds + 1.0);
            AssertThat(bubble.State).IsEqual(SpeechBubble.BubbleState.Done);
            AssertThat(bubble.IsDone).IsTrue();
            AssertThat(bubble.Modulate.A).IsEqual(0f);
        }
        finally
        {
            bubble.Free();
        }
    }

    [TestCase]
    public void Advance_PastDone_IsANoOp()
    {
        var bubble = new SpeechBubble();
        try
        {
            bubble.Setup("Exactly what I needed.");
            bubble.Advance(100); // done well within one call
            AssertThat(bubble.IsDone).IsTrue();

            bubble.Advance(5); // must not throw or resurrect the bubble
            AssertThat(bubble.IsDone).IsTrue();
        }
        finally
        {
            bubble.Free();
        }
    }

    [TestCase]
    public void PositionAbove_PlacesTailTipAtTheGivenPoint()
    {
        var bubble = new SpeechBubble();
        try
        {
            bubble.Setup("…!", reaction: true);
            var head = new Vector2(500, 300);
            bubble.PositionAbove(head);

            // The bubble sits fully above `head` (bottom edge, including the tail, at or above it).
            AssertThat(bubble.Position.Y + bubble.Size.Y < head.Y).IsTrue();
            // Horizontally centered on the head point.
            AssertThat(Mathf.Abs(bubble.Position.X + bubble.Size.X / 2f - head.X) < 0.5f).IsTrue();
        }
        finally
        {
            bubble.Free();
        }
    }

    [TestCase]
    public void Setup_ReactionBubble_IsSmallAndUnwrapped()
    {
        var bubble = new SpeechBubble();
        try
        {
            bubble.Setup("…!", reaction: true);
            AssertThat(bubble.IsReaction).IsTrue();
            AssertThat(bubble.Size.X < 60f).IsTrue(); // compact, not a full gossip-line bubble
        }
        finally
        {
            bubble.Free();
        }
    }

    [TestCase]
    public void Setup_LongerLine_ProducesWiderBubble_ButNeverPastMaxWidth()
    {
        var shortBubble = new SpeechBubble();
        var longBubble = new SpeechBubble();
        try
        {
            shortBubble.Setup("Hm.");
            longBubble.Setup("The forge ran hot all night — someone's due a raise, surely.");

            AssertThat(longBubble.Size.X > shortBubble.Size.X).IsTrue();
            AssertThat(longBubble.Size.X <= 200f).IsTrue(); // MaxWidth
        }
        finally
        {
            shortBubble.Free();
            longBubble.Free();
        }
    }
}
#endif
