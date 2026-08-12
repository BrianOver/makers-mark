#if GDUNIT_TESTS
using GameSim.Classes;
using GdUnit4;
using Godot;
using GodotClient.Town2d;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// U6 (docs/plans/2026-08-02-002-feat-playtest-three-plan.md, R7): the permanent pin against the
/// tower-regression this unit exists to fix. Hero bodies grew from 20x36 to 26x44 so
/// sentinel/skirmisher/occultist could get real town art and the other three could carry more
/// drawn detail — but a same-size repaint was invisible (measured 0.07%) and a naive 2x upscale
/// would have made heroes TOWER over the player, which is the one outcome the plan bans outright
/// ("nobody outgrows the player"). This test measures the ACTUAL committed pixels through the
/// SAME resolution ladder the render path uses (<see cref="TownAssets2D.ForHero"/> /
/// <see cref="TownAssets2D.ForPlayer"/>), so a future art pass that quietly grows a hero past the
/// player fails here — at the exact effective (post-<see cref="TownLayout2D.CharacterSpriteScale"/>)
/// size the player actually sees on screen, not the raw PNG dimensions.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class CastProportionTests
{
    [TestCase]
    public void EveryHeroClass_IsShorterOnScreen_ThanThePlayer()
    {
        var playerTexture = TownAssets2D.ForPlayer();
        var playerEffectiveHeight = playerTexture.GetHeight() * TownLayout2D.CharacterSpriteScale;

        foreach (var classId in ClassRegistry.RecruitPool)
        {
            var heroTexture = TownAssets2D.ForHero(classId);
            var heroEffectiveHeight = heroTexture.GetHeight() * TownLayout2D.CharacterSpriteScale;

            AssertThat(heroEffectiveHeight)
                .OverrideFailureMessage(
                    $"'{classId}' resolves a {heroTexture.GetWidth()}x{heroTexture.GetHeight()} " +
                    $"body ({heroEffectiveHeight}px effective at CharacterSpriteScale) — that is " +
                    $"not shorter than the player's {playerTexture.GetWidth()}x" +
                    $"{playerTexture.GetHeight()} body ({playerEffectiveHeight}px effective). The " +
                    "player must stay the tallest figure in town (R7) — this is the tower " +
                    "regression U6 exists to prevent; see docs/plans/2026-08-02-002 KTD-E.")
                .IsLess(playerEffectiveHeight);
        }
    }

    /// <summary>The walk-cycle contract: a class's step frame is the same canvas size as its base
    /// frame. <see cref="TownSpriteArtTests.StepFrames_DifferOnlyBelowTheWaist"/> already proves
    /// the two frames are pixel-identical above the legs/hem; this proves they are not merely
    /// aligned by accident of equal dimensions elsewhere in the pipeline — a mismatched step
    /// frame would either fail to load via the same id-based ladder or silently misalign the
    /// walk cycle's feet-baseline math (<see cref="HeroActor2D"/>'s pose-apply inverts the
    /// sprite's own height, so a size drift between frames is a live hazard, not a cosmetic one).
    /// </summary>
    [TestCase]
    public void EveryHeroClass_BaseAndStepFrames_ShareTheSameDimensions()
    {
        foreach (var classId in ClassRegistry.RecruitPool)
        {
            var baseTexture = TownAssets2D.ForHero(classId);
            var stepTexture = IconRegistry.Art($"town2d-hero-{classId}_step");

            AssertThat(stepTexture)
                .OverrideFailureMessage($"'{classId}' has a base town body but no _step frame")
                .IsNotNull();

            AssertThat(stepTexture!.GetWidth())
                .OverrideFailureMessage(
                    $"'{classId}': base is {baseTexture.GetWidth()}px wide, _step is " +
                    $"{stepTexture.GetWidth()}px wide — a walk frame must not change canvas size.")
                .IsEqual(baseTexture.GetWidth());

            AssertThat(stepTexture.GetHeight())
                .OverrideFailureMessage(
                    $"'{classId}': base is {baseTexture.GetHeight()}px tall, _step is " +
                    $"{stepTexture.GetHeight()}px tall — a walk frame must not change canvas size.")
                .IsEqual(baseTexture.GetHeight());
        }
    }

    /// <summary>
    /// Regression pin for the 2026-08-12 asymmetric-decimation fix
    /// (<see cref="TownLayout2D.CharacterSpriteScale"/>'s own doc has the full story). Character
    /// sprites used to ship at 2x their on-screen pixel size and rely on a runtime Nearest-filtered
    /// GPU sample at <c>CharacterSpriteScale=0.5</c> to halve them — which silently discarded
    /// exactly one column/row out of every mirrored pair, making bilaterally-symmetric art (and any
    /// single-pixel authored accent) come out lopsided on screen. The fix moved the halving OFFLINE
    /// into <c>tools/art/gen_town_sprites.py</c>, so every committed character PNG is ALREADY at its
    /// on-screen size — which only stays true while this constant is exactly 1.0 (a pure
    /// pass-through). Any other value here means either art is being scaled again at runtime (and
    /// this same bug can reproduce) or the on-screen size of every character in town silently
    /// changed, which the task that landed this fix was explicitly forbidden from doing.
    /// </summary>
    [TestCase]
    public void NoRuntimeDecimation_CharacterSpriteScaleStaysOne()
    {
        AssertThat(TownLayout2D.CharacterSpriteScale)
            .OverrideFailureMessage(
                $"CharacterSpriteScale is {TownLayout2D.CharacterSpriteScale}, not 1.0 — character " +
                "art is being scaled at runtime again. Either this reproduces the asymmetric-" +
                "decimation bug (a Nearest-filtered non-1.0 scale on art not already re-baked at " +
                "that size) or the on-screen size of every character in town just changed; neither " +
                "is a silent change. See CharacterSpriteScale's own doc comment.")
            .IsEqual(1.0f);
    }
}
#endif
