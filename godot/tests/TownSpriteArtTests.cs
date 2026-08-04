#if GDUNIT_TESTS
using System.Collections.Generic;
using System.Linq;
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
/// placeholder rectangle without breaking a single compile, a step frame can drift so that the
/// whole body changes between frames instead of just the legs — which the eye reads as flicker, not
/// walking — a "4-frame gait" can silently be four copies of a 2-frame swap, two classes'
/// outlines can silently converge, and (2026-08-04 COLOUR + MATERIAL pass) two classes' garment
/// colours can silently converge or a class can lose its skin-tone region. All six are caught
/// below.</para>
///
/// <para>These read the COMMITTED pixels rather than re-running the generator, so they also catch
/// the case where someone edits a PNG by hand and the script silently disagrees with what ships.
/// <c>gen_town_sprites.py --check</c> is the other half of that pair.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TownSpriteArtTests
{
    /// <summary>What <c>TownAssets2D</c>/<c>TownLayout2D</c> lay the town out against. U3
    /// (2026-08-04 verify-by-playing plan, R3) resized the canvas 26x44 -> 40x64 — "26x44 is too
    /// few pixels to carry detail at gameplay distance" per the plan's own words — a canvas
    /// resize, not a layout change, since neither <c>TownLayout2D</c>'s tile coordinates nor this
    /// census pin world scale (that retune is deliberately deferred to U4, see the plan's KTD-F).</summary>
    private const int BodyWidth = 40;
    private const int BodyHeight = 64;

    /// <summary>First row of the legs/hem (U3: rows 0-2 empty margin, 3-18 head, 19-44 torso, then
    /// legs/hem — <c>gen_town_sprites.py</c>'s own <c>LEGS_TOP_ROW</c> constant, kept in sync by
    /// hand since this test intentionally reads committed pixels rather than importing the
    /// generator). A walk frame may differ at or below this row and nowhere above it — that
    /// separation is what makes the frames read as a stride, not a whole-body swap.</summary>
    private const int LegsTopRow = 45;

    /// <summary>A flat placeholder box is 2-3 colours. Real shading needs an outline, at least two
    /// body tones and a highlight, so anything under this is a regression to programmer art.
    /// Measured (2026-08-04, post-COLOUR+MATERIAL pass): every class has 12-15 distinct opaque
    /// colours (up from 8-9 pre-pass, now that armour/cloth/skin/hair are all separate ramps);
    /// this floor sits two below that measured minimum so a genuine regression trips it without
    /// making the test flaky against a future one-tone tweak.</summary>
    private const int MinDistinctColors = 10;

    /// <summary>The shared skin tone every class's face/skin-peek region uses
    /// (<c>tools/art/gen_town_sprites.py</c>'s <c>'f'</c> letter) — one fantasy-tan RGB, reused
    /// everywhere rather than picked per class, matching the file's own "never a colour invented
    /// per class" discipline for shared tones.</summary>
    private static readonly Color SkinTone = Color.Color8(196, 148, 110);

    /// <summary>Per-channel tolerance for matching <see cref="SkinTone"/> against a loaded pixel —
    /// generous enough to survive PNG/Godot import rounding, tight enough that it could never
    /// match a class's garment or steel tones (the closest neighbour, Sentinel's bronze cloth
    /// light stop, is well over 40 per channel away).</summary>
    private const float SkinToneTolerance = 10f / 255f;

    /// <summary>Minimum skin-tone pixels for <see cref="EveryClass_CarriesASkinToneRegion"/>.
    /// Measured (2026-08-04): Mystic/Occultist (the two cowled casters, deliberately just a
    /// shadow-edge hint per their own established "shadowed face" silhouette) are the thinnest at
    /// 6px; every other class is 8-40px. This floor sits two below that measured minimum.</summary>
    private const int MinSkinPixels = 4;

    /// <summary>Minimum Euclidean RGB distance (0-255 scale per channel) between any two classes'
    /// dominant garment colour — see <see cref="EveryClass_HasADistinctDominantGarmentHue"/>'s own
    /// doc for the metric and the measured numbers this is chosen against.</summary>
    private const double MinGarmentColorDistance = 30.0;

    /// <summary>U3: the real 4-frame alternating gait (see <c>SpriteMotion.Pose.WalkFrame</c>) —
    /// base/"_step" are frames 1/3 (the two mirrored CONTACT poses, kept under their pre-U3 ids so
    /// every existing null-tolerant consumer keeps working), "_walk2"/"_walk4" are frames 2/4 (the
    /// two PASSING poses). Order here matches <c>SpriteMotion.Pose.WalkFrame</c>'s 0-3 mapping.</summary>
    private static readonly string[] GaitSuffixes = [string.Empty, "_walk2", "_step", "_walk4"];

    /// <summary>Silhouette-distinctness floor for <see
    /// cref="AnyTwoClasses_HaveDistinctSilhouettes"/> — see that test's own doc for the metric and
    /// the measured numbers this is chosen against.</summary>
    private const double MinSilhouetteDistance = 0.08;

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
            foreach (var suffix in GaitSuffixes)
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
    ///
    /// <para>U6 tripped this in CI on all six classes despite the committed PNGs being
    /// byte-identical above row 31 (verified directly against the on-disk bytes, bypassing Godot
    /// entirely). The actual cause: every <c>town2d-hero-*.png(.import)</c> inherited Godot's
    /// default <c>process/fix_alpha_border=true</c>, which bakes a filler RGB into fully
    /// transparent (alpha 0) pixels near an opaque edge — a mitigation for bilinear/mipmap
    /// sampling bleeding into an edge, which is irrelevant here (this pipeline is Nearest
    /// filtering, mipmaps off, by design). Because the opaque legs legitimately diverge between
    /// base/step starting at row 31+, the border-fix picked a different filler colour for a
    /// same-coordinate transparent pixel a couple of rows ABOVE the divergence (e.g. row 29) in
    /// each imported texture — same alpha (0, invisible either way), different RGB, which
    /// <c>Image.GetPixel</c> equality below still catches. Fix landed in the 12 <c>.import</c>
    /// sidecars (<c>process/fix_alpha_border=false</c>), not the PNGs or the generator: the
    /// authored pixel grids were already correct by construction (see
    /// <c>tools/art/gen_town_sprites.py</c>'s own doc — base/step share every row above the
    /// legs/hem by construction). Any FUTURE <c>town2d-hero-*_step</c> pair must ship the same
    /// setting, or this reproduces.</para>
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
    /// U3: the fourth explicit gait requirement — "all four gait frames are pairwise distinct".
    /// <see cref="StepFrames_DifferOnlyBelowTheWaist"/> already proves base/"_step" differ from
    /// each other; this closes the remaining five pairs (base/"_walk2", base/"_walk4",
    /// "_walk2"/"_step", "_walk2"/"_walk4", "_step"/"_walk4") so a lazy generator that ships four
    /// copies of a 2-frame swap (e.g. "_walk2" == "_walk4") cannot pass silently.
    /// </summary>
    [TestCase]
    public void AllFourGaitFrames_ArePairwiseDistinct()
    {
        foreach (var classId in Classes)
        {
            var images = GaitSuffixes.Select(suffix => Load($"town2d-hero-{classId}{suffix}")).ToArray();

            for (var i = 0; i < images.Length; i++)
            {
                for (var j = i + 1; j < images.Length; j++)
                {
                    var identical = true;
                    for (var y = 0; identical && y < BodyHeight; y++)
                    {
                        for (var x = 0; x < BodyWidth; x++)
                        {
                            if (images[i].GetPixel(x, y) != images[j].GetPixel(x, y))
                            {
                                identical = false;
                                break;
                            }
                        }
                    }

                    AssertThat(identical)
                        .OverrideFailureMessage(
                            $"town2d-hero-{classId}{GaitSuffixes[i]} and {GaitSuffixes[j]} are " +
                            "pixel-identical — the 4-frame gait needs four DISTINCT poses, not " +
                            "duplicates of each other.")
                        .IsFalse();
                }
            }
        }
    }

    /// <summary>
    /// U3 (R3): "a Vanguard must read differently from a Mystic at a glance, by OUTLINE, not just
    /// palette" — the plan's own words. Measures the fraction of the 40x64 canvas where one
    /// class's opaque/transparent status (its silhouette) disagrees with another's — a symmetric-
    /// difference-over-union (Jaccard distance) over the ALPHA channel only, so a colour-only
    /// difference (e.g. two classes sharing a body shape but different accent tints) scores zero
    /// here even though it would matter to <see cref="HeroBodies_StayNeutral_SoEngineTintDoesNotDoubleUp"/>'s
    /// concerns — this test is deliberately blind to colour, on purpose, because the plan
    /// explicitly calls out outline as the thing palette differences do NOT substitute for.
    ///
    /// <para><b>Threshold.</b> Measured (2026-08-04, this pass's actual committed art, all 15
    /// class pairs): the closest pair is striker/skirmisher at 0.115; every other pair is 0.12 or
    /// higher, several above 0.30. <see cref="MinSilhouetteDistance"/> (0.08) sits comfortably
    /// below that measured floor — enough margin that a future accent tweak to any one class
    /// doesn't make this flaky — while still being far above what two classes sharing (or nearly
    /// sharing) a body shape would score.</para>
    /// </summary>
    [TestCase]
    public void AnyTwoClasses_HaveDistinctSilhouettes()
    {
        var masks = Classes.ToDictionary(classId => classId, classId => OpacityMask(Load($"town2d-hero-{classId}")));

        for (var i = 0; i < Classes.Length; i++)
        {
            for (var j = i + 1; j < Classes.Length; j++)
            {
                var (classA, classB) = (Classes[i], Classes[j]);
                var (maskA, maskB) = (masks[classA], masks[classB]);

                var xor = 0;
                var union = 0;
                for (var k = 0; k < maskA.Length; k++)
                {
                    if (maskA[k] != maskB[k])
                    {
                        xor++;
                    }

                    if (maskA[k] || maskB[k])
                    {
                        union++;
                    }
                }

                var distance = union == 0 ? 0.0 : (double)xor / union;

                AssertThat(distance)
                    .OverrideFailureMessage(
                        $"'{classA}' and '{classB}' silhouettes differ by only {distance:F3} of " +
                        $"their opaque footprint (floor {MinSilhouetteDistance:F2}) — they read as " +
                        "the same shape at gameplay distance; a class must be distinguishable by " +
                        "OUTLINE, not just palette (R3).")
                    .IsGreaterEqual(MinSilhouetteDistance);
            }
        }
    }

    private static bool[] OpacityMask(Image image)
    {
        var mask = new bool[BodyWidth * BodyHeight];
        for (var y = 0; y < BodyHeight; y++)
        {
            for (var x = 0; x < BodyWidth; x++)
            {
                mask[y * BodyWidth + x] = image.GetPixel(x, y).A > 0;
            }
        }

        return mask;
    }

    /// <summary>
    /// SUPERSEDES the pre-2026-08-04 <c>HeroBodies_StayNeutral_SoEngineTintDoesNotDoubleUp</c>
    /// test, which asserted the OPPOSITE of what this now checks: that bodies stayed
    /// desaturated so <c>HeroActor2D</c> could multiply a class colour in via <c>Modulate</c>.
    /// That was the right invariant for a neutral-grey sprite; it is the WRONG one now that the
    /// art itself carries a real per-class garment colour sourced from
    /// <c>ClassDefinition.ColorRgb</c> (see <c>gen_town_sprites.py</c>'s own COLOUR + MATERIAL
    /// PASS doc) — which is exactly why <c>HeroActor2D.BuildSprite</c>'s <c>Modulate</c> changed
    /// from <c>classColor</c> to <c>Colors.White</c> in the same pass (multiplying an
    /// already-coloured, material-differentiated sprite by an unrelated runtime tint would wash
    /// the neutral STEEL back into whatever hue the tint happens to be, undoing the material
    /// contrast this pass exists to add).
    ///
    /// <para><b>Metric.</b> Among pixels with saturation &gt; 0.3 AND value &gt; 0.3 (excludes the
    /// style bible's purple-black darks, which read at S≈0.36-0.52 despite being intended as
    /// neutral shading, not garment colour — the exact trap the OLD test's own doc already
    /// recorded), the most-common-by-count colour is each class's "dominant garment colour".
    /// Compared pairwise by Euclidean RGB distance (0-255 per channel).</para>
    ///
    /// <para><b>Threshold.</b> Measured (2026-08-04, this pass's committed art): the closest pair
    /// is Vanguard/Skirmisher at 46.4; every other pair is 48 or higher, several above 200 (e.g.
    /// anything against Mystic's bright violet). <see cref="MinGarmentColorDistance"/> (30) sits
    /// comfortably below that floor. Deliberately NOT a hue-angle-only check: Mystic (bright
    /// violet) and Occultist (dark violet) are only 12 degrees apart in hue BY DESIGN — the
    /// sim's own <c>ClassDefinition.ColorRgb</c> comment calls Occultist's "deeper and less
    /// saturated than the Mystic's bright violet" — so a hue-only metric would wrongly fail a
    /// pair the sim's own data model treats as intentionally close in hue but far apart in
    /// value/saturation; a full RGB distance correctly credits that separation.</para>
    /// </summary>
    [TestCase]
    public void EveryClass_HasADistinctDominantGarmentHue()
    {
        var dominant = Classes.ToDictionary(classId => classId, classId => DominantGarmentColor(Load($"town2d-hero-{classId}")));

        for (var i = 0; i < Classes.Length; i++)
        {
            for (var j = i + 1; j < Classes.Length; j++)
            {
                var (classA, classB) = (Classes[i], Classes[j]);
                var distance = RgbDistance255(dominant[classA], dominant[classB]);

                AssertThat(distance)
                    .OverrideFailureMessage(
                        $"'{classA}' ({dominant[classA]}) and '{classB}' ({dominant[classB]})'s dominant " +
                        $"garment colours are only {distance:F1} apart (floor {MinGarmentColorDistance:F0}) " +
                        "— they read as the same colour across the room, not two distinct classes (R3).")
                    .IsGreaterEqual(MinGarmentColorDistance);
            }
        }
    }

    /// <summary>
    /// R3, the review's explicit ask: "a visible face area with a skin tone... the single biggest
    /// 'that's a person' cue". Every class carries at least <see cref="MinSkinPixels"/> pixels of
    /// the shared <see cref="SkinTone"/> — a full face for the two open-faced classes (Striker,
    /// Skirmisher), a visor-slit/jaw peek for the two full-helm tanks (Vanguard, Sentinel — no
    /// room for more without contradicting their own closed-helm silhouette), and a dim hint at
    /// the shadow-hood's edge for the two cowled casters (Mystic/Occultist's faces are
    /// deliberately shadowed BY DESIGN — see their own head-row comments — so a FULL face here
    /// would contradict the class's own established look, not just add detail).
    /// </summary>
    [TestCase]
    public void EveryClass_CarriesASkinToneRegion()
    {
        foreach (var classId in Classes)
        {
            var image = Load($"town2d-hero-{classId}");
            var skinPixels = CountSkinTonePixels(image);

            AssertThat(skinPixels)
                .OverrideFailureMessage(
                    $"town2d-hero-{classId} has only {skinPixels} skin-tone pixels (floor " +
                    $"{MinSkinPixels}) — every class needs a real, visible skin-tone region (R3).")
                .IsGreaterEqual(MinSkinPixels);
        }
    }

    /// <summary>Pixels with saturation &gt; 0.3 AND value &gt; 0.3 (see
    /// <see cref="EveryClass_HasADistinctDominantGarmentHue"/>'s doc for why), the most common by
    /// count.</summary>
    private static Color DominantGarmentColor(Image image)
    {
        const float SaturationFloor = 0.3f;
        const float ValueFloor = 0.3f;

        var counts = new Dictionary<Color, int>();
        for (var y = 0; y < image.GetHeight(); y++)
        {
            for (var x = 0; x < image.GetWidth(); x++)
            {
                var pixel = image.GetPixel(x, y);
                if (pixel.A == 0 || pixel.S <= SaturationFloor || pixel.V <= ValueFloor)
                {
                    continue;
                }

                counts[pixel] = counts.GetValueOrDefault(pixel) + 1;
            }
        }

        AssertThat(counts.Count).OverrideFailureMessage("no saturated garment pixels found at all").IsGreater(0);

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

        return dominant;
    }

    private static double RgbDistance255(Color a, Color b)
    {
        double dr = (a.R - b.R) * 255.0;
        double dg = (a.G - b.G) * 255.0;
        double db = (a.B - b.B) * 255.0;
        return System.Math.Sqrt(dr * dr + dg * dg + db * db);
    }

    private static int CountSkinTonePixels(Image image)
    {
        var count = 0;
        for (var y = 0; y < image.GetHeight(); y++)
        {
            for (var x = 0; x < image.GetWidth(); x++)
            {
                var pixel = image.GetPixel(x, y);
                if (pixel.A == 0)
                {
                    continue;
                }

                if (System.Math.Abs(pixel.R - SkinTone.R) <= SkinToneTolerance
                    && System.Math.Abs(pixel.G - SkinTone.G) <= SkinToneTolerance
                    && System.Math.Abs(pixel.B - SkinTone.B) <= SkinToneTolerance)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static Image Load(string id)
    {
        var texture = GD.Load<Texture2D>($"res://assets/art/{id}.png");
        AssertThat(texture).OverrideFailureMessage($"{id}.png did not load").IsNotNull();
        return texture.GetImage();
    }
}
#endif
