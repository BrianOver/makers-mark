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
/// </summary>
[TestSuite]
public class TutorialAnchorArbiterTests
{
    private static readonly TutorialAnchor ForgeAnchor = TutorialAnchor.ForHud("ForgeSpotlightControl");
    private static readonly TutorialAnchor MentorAnchor = TutorialAnchor.ForHud("MentorBannerControl");
    private static readonly TutorialAnchor ChainAnchor = TutorialAnchor.ForBuilding("forge");
    private static readonly TutorialAnchor LossAnchor = TutorialAnchor.ForHud("OpenLegends");

    [TestCase]
    public void AllFourSourcesNull_ResolvesToNone()
    {
        var result = Resolve(new TutorialAnchorSources(null, null, null, null));
        AssertThat(result).IsEqual(TutorialAnchor.None);
    }

    [TestCase]
    public void ForgeSpotlight_OutranksMentorBanner()
    {
        var result = Resolve(new TutorialAnchorSources(ForgeAnchor, MentorAnchor, null, null));
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
        var result = Resolve(new TutorialAnchorSources(ForgeAnchor, MentorAnchor, ChainAnchor, LossAnchor));
        AssertThat(result).IsEqual(ForgeAnchor);
    }

    [TestCase]
    public void MentorBanner_OutranksChainStep()
    {
        var result = Resolve(new TutorialAnchorSources(null, MentorAnchor, ChainAnchor, null));
        AssertThat(result).IsEqual(MentorAnchor);
    }

    [TestCase]
    public void MentorBanner_OutranksLossRow()
    {
        var result = Resolve(new TutorialAnchorSources(null, MentorAnchor, null, LossAnchor));
        AssertThat(result).IsEqual(MentorAnchor);
    }

    [TestCase]
    public void MentorBanner_OutranksBothLowerSourcesAtOnce()
    {
        var result = Resolve(new TutorialAnchorSources(null, MentorAnchor, ChainAnchor, LossAnchor));
        AssertThat(result).IsEqual(MentorAnchor);
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
