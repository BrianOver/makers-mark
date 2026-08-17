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
    /// (2026-08-04 verify-by-playing plan, R3) authored the canvas at 26x44 -> 40x64 — "26x44 is
    /// too few pixels to carry detail at gameplay distance" per the plan's own words.
    ///
    /// <para><b>2026-08-12 (asymmetric-decimation fix):</b> the 40x64 canvas above is now an
    /// AUTHORING resolution only. <c>gen_town_sprites.py</c>'s <c>rarity_downsample_2x()</c> halves
    /// it to 20x32 before committing — the SHIPPED, on-screen pixel grid — and
    /// <c>TownLayout2D.CharacterSpriteScale</c> went from 0.5 to 1.0 to match (no runtime scaling,
    /// hence no runtime decimation, left at all). These pins are the shipped dimensions, not the
    /// authoring canvas.</para></summary>
    private const int BodyWidth = 20;
    private const int BodyHeight = 32;

    /// <summary>First row of the legs/hem in the SHIPPED (post-halving) image —
    /// <c>gen_town_sprites.py</c>'s own <c>LEGS_TOP_ROW</c> (45, at the 40x64 authoring
    /// resolution) integer-divided by 2, kept in sync by hand since this test intentionally reads
    /// committed pixels rather than importing the generator. A walk frame may differ at or below
    /// this row and nowhere above it — that separation is what makes the frames read as a stride,
    /// not a whole-body swap.
    ///
    /// <para><b>2026-08-15 (folk-cast wave):</b> no committed civilian pool uses this value any
    /// longer — see <see cref="TownsfolkLegsTopRow"/>/<see cref="RobedTownsfolkExtensionLegsTopRow"/>
    /// below, which supersede it the same way <see cref="BaseClassLegsTopRow"/> superseded it for
    /// the hero base ids. Kept (unused) rather than deleted: it still documents the arithmetic that
    /// produced 22, in case a future hand-drawn body ever needs it again.</para></summary>
    private const int LegsTopRow = 22;

    /// <summary>First row of the legs/hem for the townsfolk civilian pool's own broad/slight
    /// AI-composite bodies (2026-08-15 folk-cast wave: the 'folk' art job redrew broad/slight as an
    /// SDXL composite matching the hero cast's own style, and slotted two new non-robed
    /// silhouettes — belder (broad-v6..-v10) and steen (slight-v6..-v10) — into the SAME two pools
    /// right after them). Measured off each body's own "&lt;id&gt; - torso proof.txt" from the art
    /// job, cross-checked against the committed PNG bytes, same discipline as
    /// <see cref="BaseClassLegsTopRow"/>. Supersedes the old shared <see cref="LegsTopRow"/> (22),
    /// which was correct only for the pre-2026-08-15 hand-ASCII civilian bodies no committed pixel
    /// is any longer.</summary>
    private const int TownsfolkLegsTopRow = 21;

    /// <summary>Hem row for the two ROBED townsfolk extensions the same wave added — bmatron
    /// (broad-v11..-v15) and selder (slight-v11..-v15), long-dress silhouettes with no visible legs
    /// (the hero mystic/occultist's own "hem sways instead of legs" idiom). Applies from
    /// <see cref="RobedTownsfolkExtensionStartIndex"/> onward in either family's pool, regardless of
    /// which family.</summary>
    private const int RobedTownsfolkExtensionLegsTopRow = 24;

    /// <summary><see cref="ArtVariants.PoolFor"/> index (0-based — 0 is the base id, 1 is -v2, ...)
    /// at which the townsfolk pools hand off from the belder/steen extension (leg_row 21, same as
    /// the family's own base) to the ROBED bmatron/selder extension (hem_row 24): pool slot 11
    /// (index 10) is each family's -v11, the first robed id.</summary>
    private const int RobedTownsfolkExtensionStartIndex = 10;

    /// <summary>The robed bmatron/selder extension's own disclosed gap (tmp/folk/MANIFEST.txt's
    /// KNOWN MISSES: "robed bodies: 2 distinct sway frames vs the hand pipeline's 4", and every
    /// affected id's own art/build/*.build.json <c>aiDisclosure</c>): the art job's PIL hem-sway
    /// helper (assemble_folk.py's robed branch) only swayed the two CONTACT frames (base/_step);
    /// the two PASSING frames (_walk2/_walk4) both reuse the literal unswayed base render, so they
    /// are byte-identical to EACH OTHER for every one of these 10 ids' 5 palette variants each.
    /// Measured directly against the committed pixels before this carve-out was written, not
    /// assumed. This is a real, disclosed limitation of the shipped art (the two CONTACT poses
    /// still alternate correctly) — not a reason to weaken <see
    /// cref="EveryVariantBody_ObeysTheSameGaitInvariantsAsItsBase"/>'s 4-distinct floor for anything
    /// else in the cast, which is why the carve-out below is scoped to exactly this id range rather
    /// than lowering the shared assertion.</summary>
    private const int RobedTownsfolkExtensionMinDistinctFrames = 3;

    /// <summary>Per-class first-divergent-row floor for <see cref="StepFrames_DifferOnlyBelowTheWaist"/>,
    /// superseding the single shared <see cref="LegsTopRow"/> for the SIX BASE CLASS ids only
    /// (2026-08-15 six-hero-cast ship wave). The base body swapped from the hand ASCII-grid render
    /// to an SDXL composite cast (art/build/town2d-hero-&lt;class&gt;*.build.json) with its own
    /// per-class figure crop, so the torso/leg boundary is no longer a single shared constant —
    /// measured directly off each class's own committed pixels (each hero's own
    /// "&lt;class&gt; - torso proof.txt" from the art job, cross-checked here against the actual
    /// PNG bytes): Vanguard 20, Sentinel 18, Striker 23, Skirmisher 22, Mystic 24, Occultist 24.
    /// <see cref="LegsTopRow"/> itself is UNCHANGED and still correct for
    /// <see cref="EveryVariantBody_ObeysTheSameGaitInvariantsAsItsBase"/> — the -v2.. variant pool
    /// bodies are still the untouched hand-drawn render, whose torso/leg boundary never moved.</summary>
    private static readonly Dictionary<string, int> BaseClassLegsTopRow = new()
    {
        ["vanguard"] = 20,
        ["sentinel"] = 18,
        ["striker"] = 23,
        ["skirmisher"] = 22,
        ["mystic"] = 24,
        ["occultist"] = 24,
    };

    /// <summary>A flat placeholder box is 2-3 colours. Real shading needs an outline, at least two
    /// body tones and a highlight, so anything under this is a regression to programmer art.
    /// Measured (2026-08-04, post-COLOUR+MATERIAL pass, pre-halving): every class had 12-15
    /// distinct opaque colours. <b>2026-08-12 (asymmetric-decimation fix):</b> re-measured post
    /// halving at 23-29 (the rarity-priority downsample keeps existing palette colours rather than
    /// inventing blended ones, and a smaller canvas concentrates them) — the floor still sits well
    /// below either measurement, so a genuine regression trips it without making the test flaky
    /// against a future one-tone tweak.</summary>
    private const int MinDistinctColors = 10;

    /// <summary>Minimum warm-hue-range pixels for <see cref="EveryClass_CarriesASkinToneRegion"/> —
    /// see that test's own doc for the full measurement and why this replaced an exact-RGB match.
    /// </summary>
    private const int MinSkinPixels = 4;

    /// <summary>Hue band (degrees, matching Godot <c>Color.H</c>'s 0-1-of-360 convention once
    /// divided by 360) for a warm, continuous-tone "skin-adjacent" pixel — centred on the fantasy-
    /// tan family every hero's face/visor-slit/hood-shadow renders into, regardless of exact shade.
    /// See <see cref="EveryClass_CarriesASkinToneRegion"/>'s doc for the measurement.</summary>
    private const float SkinHueMinDegrees = 5f;
    private const float SkinHueMaxDegrees = 45f;

    /// <summary>Saturation floor/ceiling for the same warm-hue test — excludes near-neutral greys
    /// (steel plate, shadow, white rim-light, all sit under ~0.12 saturation in the committed art)
    /// at the low end, and would exclude a fully-saturated dyed cloth at the high end (none of the
    /// six classes' garment ramps land inside this hue band in the first place, but the ceiling
    /// keeps the intent explicit rather than accidental).</summary>
    private const float SkinSaturationMin = 0.15f;
    private const float SkinSaturationMax = 0.70f;

    /// <summary>Value floor for the same test — excludes near-black shadow pixels, which can carry
    /// a numerically "warm" hue by HSV maths despite reading as pure darkness, not skin.</summary>
    private const float SkinValueMin = 0.25f;

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
            var legsTopRow = BaseClassLegsTopRow[classId];

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
                            $"legs row {legsTopRow}. Only the legs/hem may move between frames.")
                        .IsGreaterEqual(legsTopRow);
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
    /// palette" — the plan's own words. Measures the fraction of the shipped canvas where one
    /// class's opaque/transparent status (its silhouette) disagrees with another's — a symmetric-
    /// difference-over-union (Jaccard distance) over the ALPHA channel only, so a colour-only
    /// difference (e.g. two classes sharing a body shape but different accent tints) scores zero
    /// here even though it would matter to <see cref="HeroBodies_StayNeutral_SoEngineTintDoesNotDoubleUp"/>'s
    /// concerns — this test is deliberately blind to colour, on purpose, because the plan
    /// explicitly calls out outline as the thing palette differences do NOT substitute for.
    ///
    /// <para><b>Threshold.</b> Measured (2026-08-04, this pass's actual committed art, pre-halving,
    /// all 15 class pairs): the closest pair was striker/skirmisher at 0.115.
    /// <b>2026-08-12 (asymmetric-decimation fix):</b> re-measured post halving at
    /// striker/skirmisher = 0.098, mystic/occultist = 0.103 (every other pair 0.13 or higher) — a
    /// smaller canvas means a single opaque-vs-transparent pixel flip at a shared silhouette edge
    /// is a bigger fraction of the union, so this margin narrowed (0.08 floor, ~0.018 headroom
    /// versus ~0.035 before). <see cref="MinSilhouetteDistance"/> (0.08) still clears it, but with
    /// less room than before — a future accent tweak to striker or skirmisher is more likely to
    /// make this flaky than it used to be, and should re-measure rather than assume.</para>
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
    /// <para><b>Threshold.</b> Measured (2026-08-04, this pass's committed art, pre-halving): the
    /// closest pair was Vanguard/Skirmisher at 46.4. <b>2026-08-12 (asymmetric-decimation fix):</b>
    /// re-measured post halving at Vanguard/Sentinel = 64.0 (every other pair 87 or higher) — the
    /// rarity-priority downsample keeps a block's colour exactly rather than blending it toward a
    /// neighbour, which is why this margin held (in fact widened) instead of collapsing the way
    /// <see cref="MinSkinPixels"/>'s did. <see cref="MinGarmentColorDistance"/> (30) sits
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
    /// 'that's a person' cue". Every class carries at least <see cref="MinSkinPixels"/> pixels in
    /// the warm skin-adjacent hue/chroma band (<see cref="SkinHueMinDegrees"/>-<see
    /// cref="SkinHueMaxDegrees"/>, see <see cref="CountSkinTonePixels"/>) — a full face for the two
    /// open-faced classes (Striker, Skirmisher), a visor-slit/jaw peek for the two full-helm tanks
    /// (Vanguard, Sentinel — no room for more without contradicting their own closed-helm
    /// silhouette), and a dim hint at the shadow-hood's edge for the two cowled casters
    /// (Mystic/Occultist's faces are deliberately shadowed BY DESIGN so a FULL face would
    /// contradict the class's own established look, not just add detail).
    ///
    /// <para><b>2026-08-15 (AI-composite cast, hue/chroma rewrite):</b> the six hero bases moved
    /// from the hand ASCII-grid pipeline to an SDXL composite render (art/build/town2d-hero-
    /// &lt;class&gt;*.build.json) — continuous tone, not a fixed indexed palette. Measuring against
    /// the OLD exact skin RGB (196,148,110, ±10/255 — the hand pipeline's single shared skin letter,
    /// <c>tools/art/gen_town_sprites.py</c>'s <c>'f'</c>) — what this test did before — returns 0-1
    /// pixels on every class (measured directly against the committed art: vanguard 0, sentinel 0,
    /// striker 0, skirmisher 1, mystic 0, occultist 0), even though every class
    /// plainly DOES carry a warm skin-toned region by eye (see the pool sheet) — continuous-tone
    /// rendering plus PNG/import rounding essentially never lands back on one hand-picked triple, so
    /// the old metric was measuring the WRONG thing for this art, not catching a real defect.</para>
    ///
    /// <para><b>What replaced it, and the real numbers.</b> A hue/saturation/value RANGE instead of
    /// a point match, measured directly against the actual committed pixels (same discipline as
    /// <see cref="BaseClassLegsTopRow"/>): vanguard 9, sentinel 269, striker 18, skirmisher 70,
    /// mystic 18, occultist 4. <see cref="MinSkinPixels"/> (4) sits AT the true floor (occultist) —
    /// zero margin, same precedent this file already accepted for the pre-08-15 metric ("now exactly
    /// AT this floor... a future change must re-measure this, not assume it still holds").</para>
    ///
    /// <para><b>Known imprecision, accepted on purpose:</b> Sentinel's own bronze <c>CLASS_HUE</c>
    /// (176,141,87) sits in the SAME warm hue/chroma neighbourhood as skin, so its count (269) is
    /// dominated by armour, not by its actual jaw-peek. Unlike the old exact-RGB match, this test
    /// cannot cleanly isolate "sliver of skin" from "lots of similarly-hued armour" for that ONE
    /// class. That is an acceptable trade, not a blind spot: Sentinel's design intent already grants
    /// it only a tiny sliver of skin ("no room for more without contradicting their own closed-helm
    /// silhouette"), so a coarser guard for that one class does not weaken the other five, and the
    /// test still catches what it exists to catch — a class shipping with NO warm-hued region at
    /// all (a broken palette, a flat placeholder, a colour-space bug).</para>
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
                    $"town2d-hero-{classId} has only {skinPixels} skin-hue-range pixels (floor " +
                    $"{MinSkinPixels}) — every class needs a real, visible skin-toned region (R3).")
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

    /// <summary>Counts pixels in the warm skin-adjacent hue/chroma band — see
    /// <see cref="EveryClass_CarriesASkinToneRegion"/>'s doc for the range and why it replaced an
    /// exact-RGB match. <c>pixel.H</c> is Godot's own hue, a 0-1 fraction of 360°.</summary>
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

                var hueDegrees = pixel.H * 360f;
                if (hueDegrees >= SkinHueMinDegrees && hueDegrees <= SkinHueMaxDegrees
                    && pixel.S >= SkinSaturationMin && pixel.S <= SkinSaturationMax
                    && pixel.V >= SkinValueMin)
                {
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>
    /// The gait invariants above, applied to every VARIATION-POOL body rather than just the six
    /// base ids — because the pool is where they were about to silently stop holding.
    ///
    /// <para>Measured, not theorised: Godot generated the 128 new variant <c>.import</c> sidecars
    /// with its default <c>process/fix_alpha_border=true</c>, which is exactly the setting whose
    /// removal <see cref="StepFrames_DifferOnlyBelowTheWaist"/> exists to protect ("Any FUTURE
    /// <c>town2d-hero-*_step</c> pair must ship the same setting, or this reproduces"). A full
    /// engine run went green over all 128 of them anyway, because that test iterates six
    /// hand-listed class ids and a variant is not one of them. The pixels were fine; the coverage
    /// was not.</para>
    ///
    /// <para>Deliberately does NOT re-assert the cross-class tests (distinct silhouettes, distinct
    /// garment colours): variants of one class are SUPPOSED to share a silhouette, and their
    /// garment hues are near-neighbours of one shared class hue by design.</para>
    ///
    /// <para><b>2026-08-15 (hero variant pool ships the AI-composite cast too):</b> the six hero
    /// classes' <c>-v2..-v5</c> pools were regenerated as deterministic PIL recolours of the SAME
    /// AI-composite base as their class (not the hand-drawn ASCII-grid render), so they inherit
    /// that base's own torso/leg boundary — <see cref="BaseClassLegsTopRow"/>, NOT the single
    /// shared <see cref="LegsTopRow"/> that was correct back when every hero variant was still
    /// hand-drawn.</para>
    ///
    /// <para><b>2026-08-15 (folk-cast wave, same day): townsfolk join it too.</b> broad/slight
    /// themselves were redrawn as the same kind of AI composite, and two new non-robed silhouettes
    /// (belder, steen) were slotted into their pools right after (pool index &lt;
    /// <see cref="RobedTownsfolkExtensionStartIndex"/>, row <see cref="TownsfolkLegsTopRow"/>),
    /// followed by two ROBED silhouettes (bmatron, selder) at
    /// <see cref="RobedTownsfolkExtensionStartIndex"/> onward (row
    /// <see cref="RobedTownsfolkExtensionLegsTopRow"/>). Every committed civilian pool id is one of
    /// these three buckets now — none still uses the old shared <see cref="LegsTopRow"/>. The robed
    /// pair also carries a disclosed 3-not-4-distinct-frames gap (<see
    /// cref="RobedTownsfolkExtensionMinDistinctFrames"/>) that the hero pools do not.</para>
    /// </summary>
    [TestCase]
    public void EveryVariantBody_ObeysTheSameGaitInvariantsAsItsBase()
    {
        var bodiesChecked = 0;

        var poolBases = Classes.Select(c => $"town2d-hero-{c}")
            .Concat(Town2d.TownsfolkNpc2D.CivilianIds.Select(c => $"town2d-townsfolk-{c}"));

        foreach (var baseId in poolBases)
        {
            var classId = baseId.StartsWith("town2d-hero-") ? baseId["town2d-hero-".Length..] : null;
            var pool = ArtVariants.PoolFor(baseId);

            for (var poolIndex = 1; poolIndex < pool.Count; poolIndex++)
            {
                var bodyId = pool[poolIndex];
                var isRobedTownsfolkExtension = classId is null && poolIndex >= RobedTownsfolkExtensionStartIndex;
                var legsTopRow = classId is not null && BaseClassLegsTopRow.TryGetValue(classId, out var row)
                    ? row
                    : isRobedTownsfolkExtension ? RobedTownsfolkExtensionLegsTopRow : TownsfolkLegsTopRow;

                var frames = GaitSuffixes.Select(s => Load($"{bodyId}{s}")).ToList();

                foreach (var frame in frames)
                {
                    AssertThat(frame.GetWidth()).IsEqual(BodyWidth);
                    AssertThat(frame.GetHeight()).IsEqual(BodyHeight);
                }

                // Every frame must agree with the base frame above the hem — the whole-body-swap
                // flicker guard, and the fix_alpha_border regression detector.
                for (var f = 1; f < frames.Count; f++)
                {
                    for (var y = 0; y < legsTopRow; y++)
                    {
                        for (var x = 0; x < BodyWidth; x++)
                        {
                            AssertThat(frames[f].GetPixel(x, y) == frames[0].GetPixel(x, y))
                                .OverrideFailureMessage(
                                    $"{bodyId}{GaitSuffixes[f]} differs from its own base frame at "
                                    + $"({x},{y}), above the legs row {legsTopRow}. If the PNGs are "
                                    + "byte-identical up there, the .import sidecar is the culprit: "
                                    + "process/fix_alpha_border must be false, as it is on every base body.")
                                .IsTrue();
                        }
                    }
                }

                // …and the four frames must actually be (at least) four distinct poses — except the
                // robed townsfolk extension's own disclosed gap (see
                // RobedTownsfolkExtensionMinDistinctFrames's doc): its two PASSING frames are
                // byte-identical to each other by construction, so only 3 of its 4 frames differ.
                var distinct = frames.Select(FingerprintOf).Distinct().Count();
                var minDistinct = isRobedTownsfolkExtension ? RobedTownsfolkExtensionMinDistinctFrames : 4;
                AssertThat(distinct)
                    .OverrideFailureMessage($"{bodyId} ships {distinct} distinct gait frames, fewer than "
                        + $"the {minDistinct} even its own documented limitation allows")
                    .IsGreaterEqual(minDistinct);

                bodiesChecked++;
            }
        }

        // Vacuous-green guard: this test's entire value is that it iterates the POOL, so a pool
        // that enumerates empty must fail here rather than pass by checking nothing.
        AssertThat(bodiesChecked)
            .OverrideFailureMessage("no variant bodies were checked — the pool enumerated empty")
            .IsGreaterEqual(Classes.Length);
    }

    /// <summary>Cheap content fingerprint for frame-distinctness — the full pixel run, joined.
    /// 20x32 is small enough that exactness costs nothing and beats any sampling heuristic.</summary>
    private static string FingerprintOf(Image image) => FingerprintOf(image, BodyWidth, BodyHeight);

    private static string FingerprintOf(Image image, int width, int height)
    {
        var sb = new System.Text.StringBuilder(width * height * 4);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                sb.Append(image.GetPixel(x, y).ToRgba32()).Append(';');
            }
        }
        return sb.ToString();
    }

    private static Image Load(string id)
    {
        var texture = GD.Load<Texture2D>($"res://assets/art/{id}.png");
        AssertThat(texture).OverrideFailureMessage($"{id}.png did not load").IsNotNull();
        return texture.GetImage();
    }

    // ── Player smith (2026-08-15 owner playtest: "main character looks awful — the generic
    // shopkeeper sprite was better") ────────────────────────────────────────────────────────────
    // The player carries no ClassDefinition and joins no ArtVariants pool (gen_town_sprites.py's
    // own PLAYER SMITH section doc — "the player does NOT join a variant pool, he is one person"),
    // so he needs his own small, explicit id list rather than folding into Classes/GaitSuffixes
    // above, which are keyed on the six-class hero family specifically.

    private const int PlayerWidth = 22;
    private const int PlayerHeight = 34;

    /// <summary>First row of the legs in the SHIPPED (post-halving) player image — measured directly
    /// against the committed pixels (the first row at which ANY of the three walk frames differs
    /// from the base).
    ///
    /// <para><b>2026-08-15 (six-hero-cast ship wave):</b> re-measured at 23, not 24, after
    /// <c>player_smith*</c> swapped from the hand ASCII-grid render to its own SDXL composite
    /// (art/build/player_smith*.build.json) — a different figure crop than the hand pipeline's,
    /// so the arithmetic-derived 24 (68-row authoring canvas vs a hero's 64) no longer applies; 23
    /// is the smith's own torso proof measurement, cross-checked against the committed PNG bytes,
    /// same discipline as <see cref="BaseClassLegsTopRow"/>.</para></summary>
    private const int PlayerLegsTopRow = 23;

    private static readonly string[] PlayerGaitIds =
        ["player_smith", "player_smith_walk2", "player_smith_step", "player_smith_walk4"];

    /// <summary>Hue band (degrees) for a "warm PLAYER_HUE-family" pixel — <c>PLAYER_HUE</c>
    /// (110,74,42) itself sits at hue≈28°, and <c>cloth_ramp()</c>'s light/mid/dark/deepest stops
    /// all preserve that hue by construction (the ramp lerps toward white/black, which cannot move
    /// hue), so the whole ramp — indexed-palette or continuous-tone alike — lives in this band.
    /// See <see cref="PlayerSmith_WarmGarmentTonesAreTheMajorityOfItsOpaqueArea"/>'s doc for why
    /// this replaced an exact 4-tone RGB match.</summary>
    private const float PlayerWarmHueMinDegrees = 0f;
    private const float PlayerWarmHueMaxDegrees = 45f;

    /// <summary>Saturation floor for the same test — excludes near-neutral pixels (white rim-light,
    /// black shadow, silvery tool/anvil greys all sit under ~0.1 saturation), whose hue is
    /// numerically noisy at low chroma and would otherwise false-match by chance.</summary>
    private const float PlayerWarmSaturationMin = 0.25f;

    /// <summary>Value floor for the same test — excludes near-black shadow pixels, which (like the
    /// hero skin-hue check above) can carry a "warm" hue by HSV maths despite reading as pure
    /// darkness rather than a warm garment tone.</summary>
    private const float PlayerWarmValueMin = 0.12f;

    /// <summary>The minimum fraction of <c>player_smith.png</c>'s own opaque area that must fall in
    /// the warm hue band above — the regression pin for the 2026-08-15 fix.
    ///
    /// <para><b>2026-08-15 (AI-composite cast, hue/chroma rewrite):</b> <c>player_smith*</c> moved
    /// to an SDXL composite render (art/build/player_smith*.build.json) the same day this floor was
    /// first written against the hand-authored fix, so the ORIGINAL version of this test (exact
    /// match against the four hand-picked cloth_ramp RGB triples, ±4/255) never actually ran against
    /// continuous-tone output. Measured directly against the committed art: the exact-RGB version
    /// scores 0/257 opaque pixels (0.0%) even though the sprite plainly reads as a warm leather-and-
    /// apron smith, not the neutral steel-violet look the fix replaced — same failure mode as
    /// <see cref="EveryClass_CarriesASkinToneRegion"/>'s old metric, and the same fix: a hue/chroma
    /// RANGE instead of a point match. Measured with the range above: 166/257 opaque pixels (64.6%)
    /// — comfortably clear of this floor, with roughly the same margin the original exact-match
    /// version had over ITS floor (61.8% measured vs 45% floor) before the art changed under it.
    /// </para>
    /// </summary>
    private const float MinWarmFraction = 0.45f;

    [TestCase]
    public void PlayerSmith_IsThePinnedSize_ForEveryGaitFrame()
    {
        foreach (var id in PlayerGaitIds)
        {
            var image = Load(id);
            AssertThat(image.GetWidth()).OverrideFailureMessage($"{id} width").IsEqual(PlayerWidth);
            AssertThat(image.GetHeight()).OverrideFailureMessage($"{id} height").IsEqual(PlayerHeight);
        }
    }

    [TestCase]
    public void PlayerSmith_WarmGarmentTonesAreTheMajorityOfItsOpaqueArea()
    {
        var image = Load("player_smith");
        var total = 0;
        var warm = 0;

        for (var y = 0; y < image.GetHeight(); y++)
        {
            for (var x = 0; x < image.GetWidth(); x++)
            {
                var pixel = image.GetPixel(x, y);
                if (pixel.A == 0)
                {
                    continue;
                }

                total++;
                var hueDegrees = pixel.H * 360f;
                if (hueDegrees >= PlayerWarmHueMinDegrees && hueDegrees <= PlayerWarmHueMaxDegrees
                    && pixel.S >= PlayerWarmSaturationMin && pixel.V >= PlayerWarmValueMin)
                {
                    warm++;
                }
            }
        }

        AssertThat(total).OverrideFailureMessage("player_smith.png has no opaque pixels at all").IsGreater(0);

        var fraction = (float)warm / total;
        AssertThat(fraction)
            .OverrideFailureMessage(
                $"player_smith.png is {fraction:P1} warm PLAYER_HUE-range tones ({warm}/{total} opaque " +
                $"px) — floor {MinWarmFraction:P0}. The owner's 2026-08-15 complaint (\"main character " +
                "looks awful\") measured as the warm hue confined to the apron bib while the shirt/" +
                "collar/waist/trousers stayed neutral steel; this pins the fix so it cannot regress.")
            .IsGreaterEqual(MinWarmFraction);
    }

    /// <summary>The same two gait invariants <see cref="StepFrames_DifferOnlyBelowTheWaist"/> and
    /// <see cref="AllFourGaitFrames_ArePairwiseDistinct"/> pin for the six hero classes, applied to
    /// the player's own four frames — a colour-only palette fix must not touch the leg SHAPE the
    /// walk-cycle relies on.</summary>
    [TestCase]
    public void PlayerSmith_GaitFrames_DifferOnlyBelowTheWaist_AndAreAllFourDistinct()
    {
        var frames = PlayerGaitIds.Select(Load).ToArray();

        for (var f = 1; f < frames.Length; f++)
        {
            var differencesBelow = 0;
            for (var y = 0; y < PlayerHeight; y++)
            {
                for (var x = 0; x < PlayerWidth; x++)
                {
                    if (frames[0].GetPixel(x, y) == frames[f].GetPixel(x, y))
                    {
                        continue;
                    }

                    AssertThat(y)
                        .OverrideFailureMessage(
                            $"{PlayerGaitIds[f]} differs from player_smith at ({x},{y}), above the " +
                            $"legs row {PlayerLegsTopRow}. Only the legs may move between the " +
                            "player's gait frames.")
                        .IsGreaterEqual(PlayerLegsTopRow);
                    differencesBelow++;
                }
            }

            AssertThat(differencesBelow)
                .OverrideFailureMessage($"{PlayerGaitIds[f]} is identical to player_smith — the walk needs a real stride")
                .IsGreater(0);
        }

        var distinct = frames.Select(f => FingerprintOf(f, PlayerWidth, PlayerHeight)).Distinct().Count();
        AssertThat(distinct)
            .OverrideFailureMessage($"player_smith ships {distinct} distinct gait frames, not 4")
            .IsEqual(4);
    }

    // ── U-T3-7 (register #141, R14.11 "the 244-PNG silhouette pass") ─────────────────────────

    /// <summary>
    /// Every AI-cast body's alpha channel must be BINARY (0 or 1, Godot's normalized 0/255) —
    /// no partial/soft alpha anywhere. <c>art/pipeline/harden-cast-silhouette.py</c>'s own doc has
    /// the full root-cause trace: <c>cutout.py</c>'s BiRefNet segmentation mask is a continuous
    /// sigmoid probability field, never thresholded before being saved, so every
    /// <c>town2d-hero-*</c>/<c>player_smith*</c> PNG shipped with 15-18% of its pixels at a soft
    /// alpha ramp — a translucent halo that reads as a leg dissolving into whatever sits behind it
    /// (register #141, "the character's legs 'clip' with the grass").
    ///
    /// <para>Iterates the REAL registry — <see cref="ArtVariants.PoolFor"/>'s own manifest probe
    /// and <see cref="Town2d.TownsfolkNpc2D.CivilianIds"/>, not a hand-listed id array — the exact
    /// same enumeration <see cref="EveryVariantBody_ObeysTheSameGaitInvariantsAsItsBase"/> already
    /// uses, so a future class/variant/civilian added without going through
    /// <c>harden-cast-silhouette.py</c> is caught immediately rather than silently shipping
    /// soft-edged, the way the original 124 files did. Townsfolk civilians are included even
    /// though today's committed art already ships crisp (measured directly: 0.0% partial alpha
    /// across all 120 <c>town2d-townsfolk-*</c> files — they never went through the AI cutout
    /// path) — the invariant this test states is "the whole cast reads as a hard silhouette," not
    /// "only the half that happened to need fixing today does," so a future civilian regenerated
    /// through the same AI-cutout pipeline would trip this exactly like a hero would.</para>
    /// </summary>
    [TestCase]
    public void EveryCastBody_HasABinaryAlphaChannel_NoSoftCutoutEdges()
    {
        var checkedCount = 0;

        var heroBases = Classes.Select(c => $"town2d-hero-{c}");
        var townsfolkBases = Town2d.TownsfolkNpc2D.CivilianIds.Select(c => $"town2d-townsfolk-{c}");

        foreach (var baseId in heroBases.Concat(townsfolkBases))
        {
            foreach (var bodyId in ArtVariants.PoolFor(baseId))
            {
                foreach (var suffix in GaitSuffixes)
                {
                    AssertAlphaIsBinary(Load($"{bodyId}{suffix}"), $"{bodyId}{suffix}");
                    checkedCount++;
                }
            }
        }

        foreach (var id in PlayerGaitIds)
        {
            AssertAlphaIsBinary(Load(id), id);
            checkedCount++;
        }

        // Vacuous-green guard (mirrors EveryVariantBody_...'s own): this test's entire value is
        // that it iterates the real registry, so an enumeration that collapses to (near-)nothing
        // must fail here rather than pass by checking almost nothing. 244 is the exact census
        // measured directly against this checkout's committed files at the time this test was
        // written (120 hero + 120 townsfolk + 4 player) — a floor, not a ceiling: future art
        // growing the roster should only ever push this number up.
        AssertThat(checkedCount)
            .OverrideFailureMessage($"only {checkedCount} cast bodies were checked — the registry enumerated (nearly) empty")
            .IsGreaterEqual(244);
    }

    private static void AssertAlphaIsBinary(Image image, string id)
    {
        for (var y = 0; y < image.GetHeight(); y++)
        {
            for (var x = 0; x < image.GetWidth(); x++)
            {
                var a = image.GetPixel(x, y).A;
                AssertThat(a == 0f || a == 1f)
                    .OverrideFailureMessage(
                        $"{id}.png has a partial-alpha pixel at ({x},{y}) (alpha={a}) — a soft " +
                        "cutout edge that reads as the character dissolving into whatever is behind " +
                        "it (register #141). Run art/pipeline/harden-cast-silhouette.py over it.")
                    .IsTrue();
            }
        }
    }
}
#endif
