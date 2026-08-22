#if GDUNIT_TESTS
using GdUnit4;
using Godot;
using GodotClient.Ui;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// U-T9-12 (§11.14.13): a teacher may not blank the thing she is pointing at.
///
/// <para><b>The defect, measured in pixels.</b> <c>MentorBanner</c> is a FullRect
/// <see cref="PanelContainer"/> and carried <c>GameTheme.PanelStyleWood()</c> as its own panel
/// override. <c>ui-frame-wood.png</c> is fully opaque at its centre — sampled at
/// <c>(42, 36, 54, 255)</c> — so every lesson Bryn has ever spoken covered the entire screen with a
/// solid sheet until the player pressed "Got it": the docket lesson, pricing, hold-or-sell,
/// read-only-surfaces, quick-travel, the four craft lessons, and worst of all <b>the proof</b>, where
/// the line explaining "that flash" hid the flash it was explaining.</para>
///
/// <para><b>Why one override was not the whole fix.</b> Moving the wood style onto the card and
/// simply dropping the root's override leaves a <see cref="PanelContainer"/> drawing the THEME's
/// default panel — also opaque. A rendered frame proved it: the world was still a flat sheet with the
/// card correctly framed on top of it. Only an explicit <see cref="StyleBoxEmpty"/> makes a FullRect
/// PanelContainer draw nothing, which is why this test pins the empty box by TYPE rather than merely
/// asserting the wood one is gone.</para>
///
/// <para>The root stays FullRect (the <c>CenterContainer</c> centres against it) and stays
/// <see cref="Control.MouseFilterEnum.Ignore"/> — which was always the intent and is only now honest,
/// because a transparent root means the controls a click passes through to are controls the player
/// can actually see.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class MentorBannerDoesNotBlankTheGameTests
{
    private static MentorBanner Built()
    {
        var banner = new MentorBanner();
        banner.Build();
        return banner;
    }

    [TestCase]
    public void TheFullRectRoot_PaintsNothing()
    {
        var banner = Built();
        try
        {
            AssertThat(banner.GetThemeStylebox("panel") is StyleBoxEmpty)
                .OverrideFailureMessage(
                    "The banner's own FullRect root is painting a stylebox again. Every lesson would "
                    + "cover the whole screen — including the proof lesson, which would hide the very "
                    + "flash it exists to explain. It must be StyleBoxEmpty, not merely un-overridden: "
                    + "a PanelContainer with no override falls back to the theme's own opaque panel.")
                .IsTrue();
        }
        finally { banner.Free(); }
    }

    /// <summary>The frame did not disappear — it moved to the card, where it was always meant to be.
    /// Without this half the fix would read as "delete the styling" rather than "put it on the thing
    /// that should have carried it".</summary>
    [TestCase]
    public void TheCard_StillCarriesTheWoodFrame()
    {
        var banner = Built();
        try
        {
            var card = banner.FindChild("MentorBannerCard", recursive: true, owned: false) as PanelContainer;

            AssertThat(card).IsNotNull();
            AssertThat(card!.GetThemeStylebox("panel") is StyleBoxEmpty)
                .OverrideFailureMessage("The lesson card itself must still be a framed card, not bare text on the world.")
                .IsFalse();
        }
        finally { banner.Free(); }
    }

    /// <summary>The root must stay click-through. It covers the whole window by construction, so if it
    /// ever started accepting input it would swallow every press in the game while a lesson was
    /// up — the same defect in the other direction.</summary>
    [TestCase]
    public void TheRoot_NeverSwallowsInput()
    {
        var banner = Built();
        try
        {
            AssertThat(banner.MouseFilter).IsEqual(Control.MouseFilterEnum.Ignore);
        }
        finally { banner.Free(); }
    }
}
#endif
