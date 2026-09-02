#if GDUNIT_TESTS
using GdUnit4;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Ui.TutorialAnchorArbiter;

namespace GodotClient.Tests;

/// <summary>
/// U10 (§11.14.14): <see cref="TutorialAnchorArbiter"/> replaced the hardcoded conditional chain
/// that used to live inline in <c>MainUi.RefreshObjectiveLine</c> — "the forge spotlight, else the
/// current chain step, else the loss row." This suite asserts its precedence PAIR BY PAIR (every
/// higher-ranked source wins even while a lower one is ALSO set, never merely "whichever happens to
/// be non-null"), rather than trusting the order of an <c>if</c>/<c>?:</c> chain the way the code it
/// replaced did.
///
/// <para><b>P2-ONBOARD-01 (§11.15): the teaching lease.</b> <see
/// cref="TutorialAnchorSources.MentorBanner"/> is no longer a bare anchor — it is an anchor paired
/// with the <see cref="MentorVoiceRank"/> the line is showing at, and <see cref="Resolve"/> now
/// gates on that rank (U46's rule). <see cref="MentorBanner_ActRank_OutranksChainStep"/> and its
/// siblings below pin the SAME precedence the original suite pinned, now spelled with an explicit
/// <see cref="MentorVoiceRank.Act"/> voice; <see cref="MentorBanner_LessonRank_NeverDisplacesChainStep"/>
/// is the regression this unit exists to close — see its own doc for the before/after this pins.
/// </para>
/// </summary>
[TestSuite]
public class TutorialAnchorArbiterTests
{
    private static readonly TutorialAnchor ForgeAnchor = TutorialAnchor.ForHud("ForgeSpotlightControl");
    private static readonly TutorialAnchor MentorAnchor = TutorialAnchor.ForHud("MentorBannerControl");
    private static readonly TutorialAnchor ChainAnchor = TutorialAnchor.ForBuilding("forge");
    private static readonly TutorialAnchor LossAnchor = TutorialAnchor.ForHud("OpenLegends");

    private static readonly MentorBannerVoice ActVoice = new(MentorAnchor, MentorVoiceRank.Act);
    private static readonly MentorBannerVoice LessonVoice = new(MentorAnchor, MentorVoiceRank.Lesson);

    [TestCase]
    public void AllFourSourcesNull_ResolvesToNone()
    {
        var result = Resolve(new TutorialAnchorSources(null, null, null, null));
        AssertThat(result).IsEqual(TutorialAnchor.None);
    }

    [TestCase]
    public void ForgeSpotlight_OutranksMentorBanner()
    {
        var result = Resolve(new TutorialAnchorSources(ForgeAnchor, ActVoice, null, null));
        AssertThat(result).IsEqual(ForgeAnchor);
    }

    [TestCase]
    public void ForgeSpotlight_OutranksChainStep()
    {
        var result = Resolve(new TutorialAnchorSources(ForgeAnchor, null, ChainAnchor, null));
        AssertThat(result).IsEqual(ForgeAnchor);
    }

    [TestCase]
    public void ForgeSpotlight_OutranksLossRow()
    {
        var result = Resolve(new TutorialAnchorSources(ForgeAnchor, null, null, LossAnchor));
        AssertThat(result).IsEqual(ForgeAnchor);
    }

    [TestCase]
    public void ForgeSpotlight_OutranksEveryOtherSourceAtOnce()
    {
        var result = Resolve(new TutorialAnchorSources(ForgeAnchor, ActVoice, ChainAnchor, LossAnchor));
        AssertThat(result).IsEqual(ForgeAnchor);
    }

    [TestCase]
    public void MentorBanner_ActRank_OutranksChainStep()
    {
        var result = Resolve(new TutorialAnchorSources(null, ActVoice, ChainAnchor, null));
        AssertThat(result).IsEqual(MentorAnchor);
    }

    [TestCase]
    public void MentorBanner_ActRank_OutranksLossRow()
    {
        var result = Resolve(new TutorialAnchorSources(null, ActVoice, null, LossAnchor));
        AssertThat(result).IsEqual(MentorAnchor);
    }

    [TestCase]
    public void MentorBanner_ActRank_OutranksBothLowerSourcesAtOnce()
    {
        var result = Resolve(new TutorialAnchorSources(null, ActVoice, ChainAnchor, LossAnchor));
        AssertThat(result).IsEqual(MentorAnchor);
    }

    /// <summary>
    /// U46's regression, pinned. <b>Before P2-ONBOARD-01, this failed:</b> the predecessor
    /// <c>Resolve</c> read <c>sources.ForgeSpotlight ?? sources.MentorBannerAnchor ?? sources.ChainStep
    /// ?? sources.LossRow ?? TutorialAnchor.None</c> — a bare <c>TutorialAnchor?</c> with no rank
    /// attached, so ANY MentorBanner anchor beat <c>ChainStep</c> unconditionally (see
    /// <see cref="MentorBanner_ActRank_OutranksChainStep"/> immediately above, which already pinned
    /// exactly that outcome). Since <see cref="MentorBanner.Show"/>'s own default is <see
    /// cref="MentorVoiceRank.Lesson"/>, an ordinary lesson with any declared anchor silently stole the
    /// pulse from the chain's own current step — U46's own finding, worked around twice at the call
    /// site rather than fixed here. <b>After:</b> <see cref="Resolve"/> only reads a MentorBanner
    /// voice's anchor when its rank is <see cref="MentorVoiceRank.Act"/> (<c>ActRankAnchor</c>), so a
    /// lesson-rank voice's anchor is never a candidate — this test asserts the chain's own step wins
    /// instead, which is false against the predecessor implementation and true against this one.
    /// </summary>
    [TestCase]
    public void MentorBanner_LessonRank_NeverDisplacesChainStep()
    {
        var result = Resolve(new TutorialAnchorSources(null, LessonVoice, ChainAnchor, null));
        AssertThat(result)
            .OverrideFailureMessage(
                "An ordinary lesson's own anchor displaced the chain's current step — U46's regression "
                    + "is back. Only an Act-rank voice may steer the pulse.")
            .IsEqual(ChainAnchor);
    }

    /// <summary>The same rule holds even with nothing else competing: a lesson's anchor is simply
    /// never a candidate, so an inactive chain (null <see cref="TutorialAnchorSources.ChainStep"/>)
    /// falls all the way through to the loss row rather than surfacing the lesson's own pointer.
    /// </summary>
    [TestCase]
    public void MentorBanner_LessonRank_FallsThroughToLossRowWhenChainIsInactive()
    {
        var result = Resolve(new TutorialAnchorSources(null, LessonVoice, null, LossAnchor));
        AssertThat(result).IsEqual(LossAnchor);
    }

    [TestCase]
    public void ChainStep_OutranksLossRow()
    {
        var result = Resolve(new TutorialAnchorSources(null, null, ChainAnchor, LossAnchor));
        AssertThat(result).IsEqual(ChainAnchor);
    }

    [TestCase]
    public void LossRow_WinsOnlyWhenNothingElseIsSet()
    {
        var result = Resolve(new TutorialAnchorSources(null, null, null, LossAnchor));
        AssertThat(result).IsEqual(LossAnchor);
    }
}
#endif
