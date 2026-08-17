#if GDUNIT_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using GameSim.Classes;
using GdUnit4;
using Godot;
using GodotClient.Town2d;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// U-T3-4a (register #143, "bounties building isn't great, improve"): the venue art CONTRACT that
/// #514's own re-render shipped without.
///
/// <para><b>Why this exists.</b> The Bounties building was re-rendered once (PR #514) and the owner
/// rejected it again. Not because the render was rushed — because nothing in the repo ever said what
/// "good" meant for a venue sprite before either set of pixels was drawn. #514's own commit message
/// names two earlier units its render was supposed to be gated behind; neither had landed, and the
/// art shipped anyway, with no guard on colour depth, margin, connectivity or size. This file is
/// that missing guard, measured against TODAY's five committed venue PNGs — iterated off <see
/// cref="TownLayout2D.Venues"/> crossed with the real manifest resolution ladder (<see
/// cref="TownAssets2D.ForVenue"/>), never a hand-listed id array (the mistake that let 128 assets
/// ship untested under a green suite before this repo started iterating tables instead).</para>
///
/// <para><b>This ships GREEN today</b> by pinning every current failure as an EXACT set in <see
/// cref="KnownVenuesFailingTheContract"/> — the same idiom as <c>ArtManifestTests
/// .KnownThreeFrameRobedTownsfolk</c> and <c>TownPlacementTests</c>'s census exception lists.
/// Fixing a venue's art without deleting its row here goes red just as surely as a brand-new
/// regression does; every failure message says so. The re-render unit that follows this one has
/// exactly one job: emptying this table, not widening it.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class VenueArtContractTests
{
    private static readonly (int Dx, int Dy)[] FourNeighbours = { (1, 0), (-1, 0), (0, 1), (0, -1) };

    private const int MinDistinctOpaqueColors = 800;

    private const float MinBboxCoveragePct = 70f;

    /// <summary>Owner ruling on register #142: a venue must stand at least 3.5x (of a 3.5-5.5x band)
    /// the tallest character body actually rendered in town.</summary>
    private const float MinCharacterRatio = 3.5f;

    /// <summary>One venue's alpha-mask measurement — computed once per venue (a single pixel scan
    /// plus one flood fill) and shared across every assertion below, rather than five separate
    /// passes over the same <see cref="Image"/>.</summary>
    private readonly record struct VenueArt(
        string Key,
        int Width,
        int Height,
        int MarginTop,
        int MarginBottom,
        int MarginLeft,
        int MarginRight,
        int DistinctOpaqueColors,
        int ConnectedComponents,
        float BboxCoveragePct);

    private static bool IsOpaque(Image image, int x, int y) => image.GetPixel(x, y).A > 0f;

    private static VenueArt Analyze(string key, Image image)
    {
        var w = image.GetWidth();
        var h = image.GetHeight();
        var mask = new bool[w, h];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                mask[x, y] = IsOpaque(image, x, y);
            }
        }

        bool ColHasOpaque(int x)
        {
            for (var y = 0; y < h; y++)
            {
                if (mask[x, y])
                {
                    return true;
                }
            }

            return false;
        }

        bool RowHasOpaque(int y)
        {
            for (var x = 0; x < w; x++)
            {
                if (mask[x, y])
                {
                    return true;
                }
            }

            return false;
        }

        var left = 0;
        while (left < w && !ColHasOpaque(left))
        {
            left++;
        }

        var right = 0;
        while (right < w && !ColHasOpaque(w - 1 - right))
        {
            right++;
        }

        var top = 0;
        while (top < h && !RowHasOpaque(top))
        {
            top++;
        }

        var bottom = 0;
        while (bottom < h && !RowHasOpaque(h - 1 - bottom))
        {
            bottom++;
        }

        var minX = w;
        var maxX = -1;
        var minY = h;
        var maxY = -1;
        var colors = new HashSet<uint>();
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                if (!mask[x, y])
                {
                    continue;
                }

                if (x < minX)
                {
                    minX = x;
                }

                if (x > maxX)
                {
                    maxX = x;
                }

                if (y < minY)
                {
                    minY = y;
                }

                if (y > maxY)
                {
                    maxY = y;
                }

                colors.Add(image.GetPixel(x, y).ToRgba32());
            }
        }

        var bboxW = maxX >= minX ? maxX - minX + 1 : 0;
        var bboxH = maxY >= minY ? maxY - minY + 1 : 0;
        var bboxPct = 100f * (bboxW * bboxH) / (w * h);

        // 4-neighbour flood fill over the opaque mask — the "one connected subject" rule.
        var visited = new bool[w, h];
        var components = 0;
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                if (!mask[x, y] || visited[x, y])
                {
                    continue;
                }

                components++;
                var queue = new Queue<(int X, int Y)>();
                queue.Enqueue((x, y));
                visited[x, y] = true;
                while (queue.Count > 0)
                {
                    var (cx, cy) = queue.Dequeue();
                    foreach (var (dx, dy) in FourNeighbours)
                    {
                        var nx = cx + dx;
                        var ny = cy + dy;
                        if (nx < 0 || nx >= w || ny < 0 || ny >= h || !mask[nx, ny] || visited[nx, ny])
                        {
                            continue;
                        }

                        visited[nx, ny] = true;
                        queue.Enqueue((nx, ny));
                    }
                }
            }
        }

        return new VenueArt(key, w, h, top, bottom, left, right, colors.Count, components, bboxPct);
    }

    /// <summary>Every venue in <see cref="TownLayout2D.Venues"/>, resolved through the SAME <see
    /// cref="TownAssets2D.ForVenue"/> ladder the town itself draws with — never a second, hand-copied
    /// id list.</summary>
    private static List<VenueArt> AllVenueArt() =>
        TownLayout2D.Venues
            .Select(v => Analyze(v.Key, TownAssets2D.ForVenue(v.SpriteId).GetImage()))
            .ToList();

    /// <summary>Every pooled hero body's height plus the player's, resolved through the SAME ladder
    /// <c>CastProportionTests</c> uses (<see cref="TownAssets2D.ForHero(string)"/> / <see
    /// cref="TownAssets2D.ForPlayer"/>) — read from the actual committed textures, never a hardcoded
    /// literal, so a future cast repaint that changes anyone's height moves this contract's floor
    /// with it instead of silently drifting out of sync with a copy-pasted "32".</summary>
    private static int MaxCharacterBodyHeightPx() =>
        ClassRegistry.RecruitPool
            .Select(classId => TownAssets2D.ForHero(classId).GetHeight())
            .Append(TownAssets2D.ForPlayer().GetHeight())
            .Max();

    /// <summary>
    /// The exact set of (venue, assertion) pairs known to fail TODAY — pinned so this suite ships
    /// green while still catching every regression and every silent non-fix, same contract as <see
    /// cref="ArtManifestTests.KnownThreeFrameRobedTownsfolk"/>: delete a row the same PR that fixes
    /// it; widening this list is never the fix for a NEW failure.
    /// </summary>
    private readonly record struct KnownFailure(string Venue, string Assertion);

    private static readonly KnownFailure[] KnownVenuesFailingTheContract =
    [
        // Assertion 1 — transparent margin >=1px on all four sides. market ships flush to its own
        // canvas on every side (0/0/0/0); noticeboard is flush top/bottom (T0/B0) though its left/
        // right sides already clear the floor (L1/R1).
        new("market", "transparent-margin"),
        new("noticeboard", "transparent-margin"),

        // Assertion 3 — one connected opaque component (4-neighbour flood fill). market's
        // ground/shadow pixels fragment into 59 separate islands instead of one building silhouette.
        new("market", "connected-component"),

        // Assertion 5 — height >= 3.5x the tallest resolved character body (register #142's 3.5-
        // 5.5x ruling). All five venues fail today (forge 81px, tavern 88px, market 62px,
        // noticeboard 50px, mine-gate 48px, all well under 3.5 * 34px = 119px) — this row set IS the
        // regression pin the re-render unit is measured against.
        new("forge", "size-ratio"),
        new("tavern", "size-ratio"),
        new("market", "size-ratio"),
        new("noticeboard", "size-ratio"),
        new("mine-gate", "size-ratio"),
    ];

    private static List<string> Expected(string assertion) =>
        KnownVenuesFailingTheContract
            .Where(k => k.Assertion == assertion)
            .Select(k => k.Venue)
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToList();

    private static void AssertExactFailureSet(
        string assertion, List<string> failingVenueKeys, string measuredTable, string guardDescription)
    {
        failingVenueKeys.Sort(StringComparer.Ordinal);
        var expected = Expected(assertion);

        AssertThat(string.Join(", ", failingVenueKeys))
            .OverrideFailureMessage(
                $"{guardDescription}\n" +
                $"  measured:    {measuredTable}\n" +
                $"  failing now: {string.Join(", ", failingVenueKeys)}\n" +
                $"  pinned:      {string.Join(", ", expected)}\n" +
                $"If you FIXED one, delete its \"{assertion}\" row from KnownVenuesFailingTheContract " +
                "in the same PR. If this is a NEW failure, fix the art in godot/assets/art/ — do not " +
                "widen this list.")
            .IsEqual(string.Join(", ", expected));
    }

    [TestCase]
    public void EveryVenue_HasATransparentMarginOnAllFourSides()
    {
        var art = AllVenueArt();
        var failing = art
            .Where(v => v.MarginTop < 1 || v.MarginBottom < 1 || v.MarginLeft < 1 || v.MarginRight < 1)
            .Select(v => v.Key)
            .ToList();
        var measured = string.Join(", ", art.Select(v => $"{v.Key} T{v.MarginTop}/B{v.MarginBottom}/L{v.MarginLeft}/R{v.MarginRight}"));

        AssertExactFailureSet(
            "transparent-margin", failing, measured,
            "A venue sprite's canvas must carry >=1px of fully-transparent margin on every side — a " +
            "building baked flush to its own canvas edge cannot be repositioned or resized without " +
            "clipping.");
    }

    [TestCase]
    public void EveryVenue_MeetsTheDistinctOpaqueColorFloor()
    {
        var art = AllVenueArt();
        var failing = art
            .Where(v => v.DistinctOpaqueColors < MinDistinctOpaqueColors)
            .Select(v => v.Key)
            .ToList();
        var measured = string.Join(", ", art.Select(v => $"{v.Key} {v.DistinctOpaqueColors}"));

        AssertExactFailureSet(
            "distinct-opaque-color-floor", failing, measured,
            $"A venue sprite must use at least {MinDistinctOpaqueColors} distinct opaque colors — a " +
            "floor against a future programmer-art-flat-box regression, not a re-litigation of " +
            "today's art.");
    }

    [TestCase]
    public void EveryVenue_IsOneConnectedOpaqueComponent()
    {
        var art = AllVenueArt();
        var failing = art
            .Where(v => v.ConnectedComponents != 1)
            .Select(v => v.Key)
            .ToList();
        var measured = string.Join(", ", art.Select(v => $"{v.Key} {v.ConnectedComponents}"));

        AssertExactFailureSet(
            "connected-component", failing, measured,
            "A venue sprite's opaque pixels must form ONE 4-connected component — a single subject, " +
            "not a building plus disconnected shadow/ground speckle floating beside it.");
    }

    [TestCase]
    public void EveryVenue_AlphaBoundingBoxCoversMostOfTheCanvas()
    {
        var art = AllVenueArt();
        var failing = art
            .Where(v => v.BboxCoveragePct < MinBboxCoveragePct)
            .Select(v => v.Key)
            .ToList();
        var measured = string.Join(", ", art.Select(v => $"{v.Key} {v.BboxCoveragePct:F1}%"));

        AssertExactFailureSet(
            "bbox-coverage", failing, measured,
            $"A venue sprite's opaque bounding box must cover >={MinBboxCoveragePct}% of its own " +
            "canvas — no baked ground/vignette spilling all the way to the frame edge around a " +
            "building shrunk small inside it.");
    }

    /// <summary>The actual regression pin for #142 (the owner's 3.5-5.5x buildings-to-character
    /// ruling). Once the re-render lands, this is what stops a future pass shrinking buildings back
    /// down toward today's "single-story dollhouse" scale — read against the resolved cast textures,
    /// never a literal, so a future cast repaint moves the floor instead of silently invalidating
    /// it.</summary>
    [TestCase]
    public void EveryVenue_IsAtLeast3Point5xTheTallestCharacterBody()
    {
        var maxCharacterHeight = MaxCharacterBodyHeightPx();
        var floor = MinCharacterRatio * maxCharacterHeight;

        var art = AllVenueArt();
        var failing = art
            .Where(v => v.Height < floor)
            .Select(v => v.Key)
            .ToList();
        var measured = string.Join(", ", art.Select(v => $"{v.Key} {v.Height}px ({v.Height / (float)maxCharacterHeight:F2}x)"));

        AssertExactFailureSet(
            "size-ratio", failing, measured,
            $"A venue sprite must stand >={MinCharacterRatio}x the tallest resolved character body " +
            $"({maxCharacterHeight}px today) per the owner's #142 3.5-5.5x ruling — this is the " +
            "regression pin a future art pass must clear, not merely today's exception list.");
    }
}
#endif
