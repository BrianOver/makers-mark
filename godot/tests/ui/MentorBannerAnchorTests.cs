#if GDUNIT_TESTS
using GdUnit4;
using GodotClient.Ui;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// U10 (§11.14.14): before this unit, <see cref="MentorBanner"/>'s queue carried a line and a rank
/// but no anchor — only <c>ForgePanel</c>'s own separate, one-slot banner could ever point at
/// something while it spoke. This suite pins the fix: a queued beat's anchor now travels with its
/// text through <see cref="MentorBanner.CurrentAnchor"/>, arriving when the line does and clearing
/// when the banner actually closes — never a beat that speaks with a stale pointer left over from
/// whatever showed before it, and never a pointer surviving the banner's own close.
/// </summary>
[TestSuite]
// See MentorBannerQueueTests' own doc for why RequireGodotRuntime is not optional here: Build()
// touches GameTheme.PanelStyleWood()/UiKit.Card, engine resource calls that crash the bare test host.
[RequireGodotRuntime]
public class MentorBannerAnchorTests
{
    private static readonly TutorialAnchor AnchorA = TutorialAnchor.ForHud("ControlA");
    private static readonly TutorialAnchor AnchorB = TutorialAnchor.ForHud("ControlB");

    private static MentorBanner Built()
    {
        var banner = new MentorBanner();
        banner.Build();
        return banner;
    }

    [TestCase]
    public void AFreshBanner_HasNoCurrentAnchor()
    {
        var banner = Built();
        try
        {
            AssertThat(banner.Visible).IsFalse();
            AssertThat(banner.CurrentAnchor).IsNull();
        }
        finally { banner.Free(); }
    }

    [TestCase]
    public void ShowingALine_CarriesItsAnchorTheSameTick()
    {
        var banner = Built();
        try
        {
            banner.Show("first", anchor: AnchorA);

            AssertThat(banner.Visible).IsTrue();
            AssertThat(banner.CurrentAnchor)
                .OverrideFailureMessage("The line went on screen but its own declared anchor did not arrive with it.")
                .IsEqual(AnchorA);
        }
        finally { banner.Free(); }
    }

    [TestCase]
    public void ALineWithNoDeclaredAnchor_SpeaksWithNoPointer_Deliberately()
    {
        var banner = Built();
        try
        {
            banner.Show("a plain text lesson");

            AssertThat(banner.Visible)
                .OverrideFailureMessage("A beat with no declared anchor should still speak.")
                .IsTrue();
            AssertThat(banner.CurrentAnchor)
                .OverrideFailureMessage("A beat that deliberately declares no anchor must read null, not a leftover pointer.")
                .IsNull();
        }
        finally { banner.Free(); }
    }

    [TestCase]
    public void AQueuedBeatsAnchor_BecomesLiveWhenItsLineDoes_AndClearsOnFinalDismiss()
    {
        var banner = Built();
        try
        {
            banner.Show("first", anchor: AnchorA);
            banner.ShowFirstTouch("second", anchor: AnchorB); // queued: banner is busy

            AssertThat(banner.CurrentAnchor)
                .OverrideFailureMessage("The still-showing first line's own anchor should not change just because a second one queued.")
                .IsEqual(AnchorA);

            banner.Dismiss(); // drains "second"

            AssertThat(banner.Visible).IsTrue();
            AssertThat(banner.CurrentAnchor)
                .OverrideFailureMessage("The queued beat's anchor did not become live when its line did.")
                .IsEqual(AnchorB);

            banner.Dismiss(); // queue now empty: closes

            AssertThat(banner.Visible).IsFalse();
            AssertThat(banner.CurrentAnchor)
                .OverrideFailureMessage("A dismissed, closed banner must not still report a pointer nobody can see.")
                .IsNull();
        }
        finally { banner.Free(); }
    }

    [TestCase]
    public void AHigherRankedBeat_Preempts_BothLineAndPointer()
    {
        var banner = Built();
        try
        {
            banner.ShowFirstTouch("a generic note", rank: MentorVoiceRank.Lesson, anchor: AnchorA);

            AssertThat(banner.ShowFirstTouch("the act", preempt: true, rank: MentorVoiceRank.Act, anchor: AnchorB))
                .IsTrue();

            AssertThat(banner.CurrentAnchor)
                .OverrideFailureMessage("Preempting must move the pointer to the NEW beat's own anchor, not leave the old one lit.")
                .IsEqual(AnchorB);

            // The displaced line's own anchor travels with it into the queue — restored, not lost,
            // when "Got it" reaches it.
            banner.Dismiss();
            AssertThat(banner.CurrentAnchor)
                .OverrideFailureMessage("The displaced beat's own anchor should return with its text, not be dropped by being preempted.")
                .IsEqual(AnchorA);
        }
        finally { banner.Free(); }
    }
}
#endif
