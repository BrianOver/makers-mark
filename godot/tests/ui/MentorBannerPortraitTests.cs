#if GDUNIT_TESTS
using GdUnit4;
using Godot;
using GodotClient.Ui;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// U36 (§11, MAKERS-MARK.md "She has a body and a face", R27): Bryn's banner now carries a
/// portrait alongside her lines — the hero-portrait mechanism (<see cref="UiKit.PortraitFrame"/>/
/// <see cref="UiKit.ArtRect"/>), never a second one invented for her.
///
/// <para><b>Why the missing-art path is what this checkout actually exercises.</b> Her portrait
/// (<c>GameArt.Specs.Town.MentorSpecs</c>'s <c>"mentor-bryn"</c>) is a describe-only spec as of
/// this unit — generation is GPU-gated and owed, not run here — so on every checkout that has not
/// separately generated it, <c>ArtRect</c> takes its OWN no-committed-art branch for real, not
/// hypothetically. This suite pins that branch specifically: a loud, captioned placeholder, plus
/// the one <see cref="GodotClient.Tools.EngineDistress"/> warning that branch always fires, never a
/// silent gap (the class doc's own "graceful fallback with no message is indistinguishable from
/// working" warning, applied to this exact new call site).</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class MentorBannerPortraitTests
{
    private static MentorBanner Built()
    {
        UiKit.ResetArtMissWarningsForTests();
        GodotClient.Tools.EngineDistress.ResetForTests();

        var banner = new MentorBanner();
        banner.Build();
        return banner;
    }

    [TestCase]
    public void Banner_CarriesAPortraitFrame_ForHerOwnPortraitId()
    {
        var banner = Built();
        try
        {
            var portrait = banner.FindChild("MentorBannerPortrait", recursive: true, owned: false);
            AssertThat(portrait)
                .OverrideFailureMessage("MentorBanner.Build() no longer builds a portrait child — she has lost her face again.")
                .IsNotNull();
        }
        finally { banner.Free(); }
    }

    /// <summary>"At draw size, no runtime scale" — the #471/#487 defect shape, checked directly: no
    /// <see cref="CanvasItem.Scale"/> multiplier anywhere in the portrait's own node chain. Layout
    /// goes through <see cref="Control.CustomMinimumSize"/> + <see
    /// cref="TextureRect.ExpandModeEnum.IgnoreSize"/> alone (the same mechanism every hero portrait
    /// card already uses) — if real art for "mentor-bryn" has landed on this checkout, its own tile
    /// is pinned to exactly <c>MentorPortraitSize</c> (56px) square, whatever the source PNG's
    /// native pixel dimensions are.</summary>
    [TestCase]
    public void Portrait_HasNoRuntimeScaleKnob_AndSizesTheRealArtTileToHerFixedDrawSize()
    {
        var banner = Built();
        try
        {
            var portrait = banner.FindChild("MentorBannerPortrait", recursive: true, owned: false) as Control;
            AssertThat(portrait).IsNotNull();
            AssertThat(portrait!.Scale)
                .OverrideFailureMessage("Her portrait carries a non-identity Scale — that is the exact runtime-scale-knob defect shape (#471/#487), not a size request.")
                .IsEqual(Vector2.One);

            // Only meaningful once real art exists for "mentor-bryn" (today's checkout has none —
            // see MissingPortraitArt_RendersALoudFallback_AndWarnsOnce for that branch instead).
            if (portrait.FindChild("ArtRect", recursive: true, owned: false) is TextureRect artRect)
            {
                AssertThat(artRect.CustomMinimumSize).IsEqual(new Vector2(56f, 56f));
                AssertThat(artRect.Scale).IsEqual(Vector2.One);
                AssertThat(artRect.ExpandMode).IsEqual(TextureRect.ExpandModeEnum.IgnoreSize);
            }
        }
        finally { banner.Free(); }
    }

    /// <summary>The missing-asset path must be LOUD, at the draw site — never a silent flat box.
    /// This id genuinely has no committed art on any checkout that has not separately run the
    /// GPU-gated generation this unit leaves owed, so this is the real path executing today, not a
    /// forced/bogus id standing in for one.</summary>
    [TestCase]
    public void MissingPortraitArt_RendersALoudFallback_AndWarnsOnce()
    {
        var banner = Built();
        try
        {
            var portrait = banner.FindChild("MentorBannerPortrait", recursive: true, owned: false);
            AssertThat(portrait).IsNotNull();

            var fallback = portrait!.FindChild("ArtRectFallback", recursive: true, owned: false);
            AssertThat(fallback)
                .OverrideFailureMessage(
                    "Her portrait id resolved to REAL committed art on this checkout — if that is "
                    + "genuinely true now, this test needs a bogus-id variant instead of relying on "
                    + "the real gap; do not delete the coverage.")
                .IsNotNull();

            var warned = false;
            foreach (var message in GodotClient.Tools.EngineDistress.Messages)
            {
                if (message.Contains(MentorVoice.PortraitId))
                {
                    warned = true;
                    break;
                }
            }

            AssertThat(warned)
                .OverrideFailureMessage(
                    $"No EngineDistress warning named '{MentorVoice.PortraitId}' — a missing portrait "
                    + "degraded silently instead of announcing itself at the draw site.")
                .IsTrue();
        }
        finally { banner.Free(); }
    }
}
#endif
