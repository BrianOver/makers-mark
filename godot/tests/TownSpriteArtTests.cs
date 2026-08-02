#if GDUNIT_TESTS
using System.Collections.Generic;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// Pixel-level conformance for the town's hand-authored hero sprites
/// (<c>tools/art/gen_town_sprites.py</c>).
///
/// <para>These assets do not go through the SDXL pipeline and so are not covered by
/// <c>art/GameArt.Tests/AssetConformanceTests</c>, whose contract is spec/seed/prompt-shaped. They
/// still need a guard, because the failure modes here are silent: art can regress to a flat
/// placeholder rectangle without breaking a single compile, and a step frame can drift so that the
/// whole body changes between frames instead of just the legs — which the eye reads as flicker, not
/// walking. Both are caught below.</para>
///
/// <para>These read the COMMITTED pixels rather than re-running the generator, so they also catch
/// the case where someone edits a PNG by hand and the script silently disagrees with what ships.
/// <c>gen_town_sprites.py --check</c> is the other half of that pair.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TownSpriteArtTests
{
    /// <summary>What <c>TownAssets2D</c>/<c>TownLayout2D</c> lay the town out against. U6
    /// (docs/plans/2026-08-02-002) resized the canvas 20x36 -> 26x44 (13x22 on screen at the fixed
    /// 0.5 <c>CharacterSpriteScale</c>, still under the player's 15x23 — see
    /// <c>CastProportionTests</c> for the permanent proportion pin) — a canvas resize, not a layout
    /// change, since neither <c>TownLayout2D</c>'s tile coordinates nor this census pin size.</summary>
    private const int BodyWidth = 26;
    private const int BodyHeight = 44;

    /// <summary>First row of the legs/hem (U6: rows 0-1 empty margin, 2-12 head, 13-30 torso, then
    /// legs/hem). A step frame may differ at or below this row and nowhere above it — that
    /// separation is what makes two frames read as a stride.</summary>
    private const int LegsTopRow = 31;

    /// <summary>A flat placeholder box is 2-3 colours. Real shading needs an outline, at least two
    /// body tones and a highlight, so anything under this is a regression to programmer art.</summary>
    private const int MinDistinctColors = 6;

    /// <summary>U6: all six hero classes now have a hand-authored town body (sentinel/skirmisher/
    /// occultist previously fell back to <c>IconRegistry.Sprite</c>'s roster SVG — see
    /// <c>AssetResolutionCensusTests.KnownPendingIds</c>, now empty).</summary>
    private static readonly string[] Classes =
        ["vanguard", "sentinel", "striker", "skirmisher", "mystic", "occultist"];

    [TestCase]
    public void HeroBodies_AreThePinnedSize_AndCarryRealShading()
    {
        foreach (var classId in Classes)
        {
            foreach (var suffix in new[] { string.Empty, "_step" })
            {
                var id = $"town2d-hero-{classId}{suffix}";
                var image = Load(id);

                AssertThat(image.GetWidth()).OverrideFailureMessage($"{id} width").IsEqual(BodyWidth);
                AssertThat(image.GetHeight()).OverrideFailureMessage($"{id} height").IsEqual(BodyHeight);

                var colors = new HashSet<Color>();
                for (var y = 0; y < image.GetHeight(); y++)
                {
                    for (var x = 0; x < image.GetWidth(); x++)
                    {
                        var pixel = image.GetPixel(x, y);
                        if (pixel.A > 0)
                        {
                            colors.Add(pixel);
                        }
                    }
                }

                AssertThat(colors.Count)
                    .OverrideFailureMessage($"{id} has {colors.Count} opaque colours — flat placeholder?")
                    .IsGreaterEqual(MinDistinctColors);
            }
        }
    }

    /// <summary>
    /// The invariant that makes the 2-frame walk work. If a step frame differs above the hem, the
    /// swap reads as the sprite being replaced rather than taking a step.
    /// </summary>
    [TestCase]
    public void StepFrames_DifferOnlyBelowTheWaist()
    {
        foreach (var classId in Classes)
        {
            var basis = Load($"town2d-hero-{classId}");
            var step = Load($"town2d-hero-{classId}_step");

            var differencesBelow = 0;
            for (var y = 0; y < BodyHeight; y++)
            {
                for (var x = 0; x < BodyWidth; x++)
                {
                    if (basis.GetPixel(x, y) == step.GetPixel(x, y))
                    {
                        continue;
                    }

                    AssertThat(y)
                        .OverrideFailureMessage(
                            $"town2d-hero-{classId}_step differs from its base at ({x},{y}), above the " +
                            $"legs row {LegsTopRow}. Only the legs/hem may move between frames.")
                        .IsGreaterEqual(LegsTopRow);
                    differencesBelow++;
                }
            }

            // …and it must actually differ, or the "walk" is a still image.
            AssertThat(differencesBelow)
                .OverrideFailureMessage($"town2d-hero-{classId}_step is identical to its base frame")
                .IsGreater(0);
        }
    }

    /// <summary>
    /// <c>TownAssets2D.ForHero</c>'s documented contract: bodies ship neutral so
    /// <c>HeroActor2D</c> can multiply the class colour in via modulate. A saturated body would
    /// double-tint the moment <c>ClassColors.RoleColor</c> lands on it.
    ///
    /// <para>Measured over the LIT body only, and that qualifier is load-bearing. The style bible's
    /// darks are deliberately purple-blacks — Void <c>#140f1f</c> reads at 0.52 saturation and Iron
    /// <c>#2a2438</c> at 0.36 — so a naive "dominant opaque pixel" check fails on a slim sprite
    /// where the outline is simply the most common colour. It did, on the Striker, the first time
    /// this test ran. The outline is correct and the test was wrong: what a modulate visibly
    /// multiplies is the lit surface, so that is what gets measured. The accents (ember rim, teal
    /// circuit, arcane rune) are intentionally saturated and are a handful of pixels each, so
    /// checking the dominant tone rather than the maximum is what keeps them legal.</para>
    /// </summary>
    [TestCase]
    public void HeroBodies_StayNeutral_SoEngineTintDoesNotDoubleUp()
    {
        // Above the style bible's Iron/Void darks (V 0.22 and below) — i.e. the mid, light and
        // highlight tones that make up the readable surface of the sprite.
        const float LitValueFloor = 0.35f;

        foreach (var classId in Classes)
        {
            var image = Load($"town2d-hero-{classId}");
            var counts = new Dictionary<Color, int>();

            for (var y = 0; y < image.GetHeight(); y++)
            {
                for (var x = 0; x < image.GetWidth(); x++)
                {
                    var pixel = image.GetPixel(x, y);
                    if (pixel.A == 0 || pixel.V <= LitValueFloor)
                    {
                        continue;
                    }

                    counts[pixel] = counts.GetValueOrDefault(pixel) + 1;
                }
            }

            AssertThat(counts.Count)
                .OverrideFailureMessage($"town2d-hero-{classId} has no lit body tones at all")
                .IsGreater(0);

            var dominant = new Color(0, 0, 0);
            var best = 0;
            foreach (var (color, count) in counts)
            {
                if (count > best)
                {
                    best = count;
                    dominant = color;
                }
            }

            AssertThat(dominant.S)
                .OverrideFailureMessage(
                    $"town2d-hero-{classId}'s dominant LIT body colour has saturation {dominant.S} — " +
                    "bodies must stay neutral; the class colour arrives via modulate.")
                .IsLessEqual(0.3f);
        }
    }

    private static Image Load(string id)
    {
        var texture = GD.Load<Texture2D>($"res://assets/art/{id}.png");
        AssertThat(texture).OverrideFailureMessage($"{id}.png did not load").IsNotNull();
        return texture.GetImage();
    }
}
#endif
