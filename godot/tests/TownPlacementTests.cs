#if GDUNIT_TESTS
using System.Collections.Generic;
using System.Linq;
using GameSim.Heroes;
using GdUnit4;
using Godot;
using GodotClient.Town2d;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// U-T3-1 (placement-census unit, "the town can be checked"): reconstructs every sprite/nameplate
/// rect the CLIENT itself draws — off <see cref="TownLayout2D"/>'s own tables and the SAME
/// <see cref="TownAssets2D"/> resolution ladder <c>Town2D</c> calls, never a second hand-copied
/// number — and proves none of them collide.
///
/// <para><b>Why this couldn't be written before.</b> <c>Town2D.HomeFor</c> was a private
/// <c>Vector2I(6 + id*3 % 28, 10 + id*2 % 6)</c> formula and the cosmetic villagers' home table was
/// a private array, both buried inside a 1,985-line adapter — no test could reach either. The one
/// placement guard that DID exist (<c>Town2DSceneTests.WarmHubProps_NeverSitOnABuildingApproachLane</c>,
/// deleted by this same PR) checked a single anchor TILE against lane RECTS and knew nothing about
/// sprite size or actors. That is exactly how hero id 1's permanent home (tile 9,12) came to sit on
/// top of <c>props-ore-cart</c> at (9,11) — a 20×16px overlap, "Torvald"'s own nameplate stamped
/// inside the cart — under a green suite, for as long as this town has existed.</para>
///
/// <para><b>U-T3-1 itself changed no placement</b> — every number it extracted (<see
/// cref="TownLayout2D.HeroHomeTiles"/>, <see cref="TownLayout2D.TownsfolkHomeTiles"/>, the wander
/// amplitudes) was the SAME value the game already drew, just moved somewhere a test could see it.
/// It landed with sixteen real sprite overlaps, ten nameplate overlaps and eight door-approach
/// overlaps pinned as KNOWN, all a symptom of one thing: the world was 40×28 tiles, 1.11 screens
/// wide, and every venue sat inside a single screen with nowhere else for anything to stand.</para>
///
/// <para><b>U-T3-2 (register #163, "need to expand the size of the world") fixes the room, not the
/// symptom.</b> The owner ruled 64×44; every placement table in <c>TownLayout2D</c> was re-laid
/// onto that grid at today's real art sizes in this PR, and all three exception sets below are now
/// empty — the same census, the same art, just enough room that nothing has to overlap anything
/// else to fit.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TownPlacementTests
{
    private const int GridWidthPx = TownLayout2D.GridWidth * TownLayout2D.TileSize;
    private const int GridHeightPx = TownLayout2D.GridHeight * TownLayout2D.TileSize;

    /// <summary>
    /// <c>Building2D.BuildInteractArea</c>'s own doorway-strip constants, mirrored here rather than
    /// referenced — both are <c>private</c> to that class (per-class-owned tuning, this file's own
    /// established "no cross-class reach-in" convention, see <see cref="Building2D.Tell"/>'s class
    /// doc) — so a change to either number is a red-then-reviewed diff in TWO places, never a silent
    /// drift in one. <see cref="DoorwayStripPx"/> is the walkable strip below a venue's own sprite
    /// bottom edge (Building2D.cs:353-369): local Y 0..+40 relative to the venue's own anchor.
    /// </summary>
    private const float DoorAnchorClearancePx = 16f;

    private const float DoorwayStripPx = DoorAnchorClearancePx + 24f;

    /// <summary>
    /// One drawn sprite: a human-readable label (failure messages only — never compared as data),
    /// the CENTER-X/BOTTOM-Y world position <see cref="TownLayout2D.TileToWorld"/> gives every
    /// placement, and its resolved texture's own width/height. This is the exact trio <see
    /// cref="Building2D.Configure"/> and <c>Town2D.BuildProps</c> use to place a <see
    /// cref="Sprite2D"/> with <c>Centered = true</c> and <c>Offset = (0, -height/2)</c> — bottom
    /// edge on the tile's own center row, never the sprite's own center. <see cref="HasNameplate"/>
    /// marks the objects that ALSO draw a <see cref="Building2D.BuildLabel"/> nametag 10px above
    /// their own sprite top (venues, heroes, townsfolk — never a bare prop).
    /// </summary>
    private readonly record struct Placed(string Label, float CenterX, float Bottom, float Width, float Height, bool HasNameplate)
    {
        public float Left => CenterX - Width / 2f;

        public float Right => CenterX + Width / 2f;

        public float Top => Bottom - Height;

        /// <summary>Nameplate rect per <see cref="Building2D.BuildLabel"/>: local <c>Position =
        /// (-w/2, -h-10)</c>, <c>Size = (w, 8)</c>, relative to the SAME world anchor the sprite
        /// itself is offset from — so the label spans Y <c>Bottom-Height-10 .. Bottom-Height-2</c>,
        /// the same X span as the sprite.</summary>
        public (float Left, float Top, float Right, float Bottom) NameplateRect =>
            (Left, Bottom - Height - 10f, Right, Bottom - Height - 2f);

        public (float Left, float Top, float Right, float Bottom) SpriteRect => (Left, Top, Right, Bottom);
    }

    private static bool Overlap((float Left, float Top, float Right, float Bottom) a, (float Left, float Top, float Right, float Bottom) b) =>
        a.Left < b.Right && b.Left < a.Right && a.Top < b.Bottom && b.Top < a.Bottom;

    private static (int W, int H) IntersectionPx((float Left, float Top, float Right, float Bottom) a, (float Left, float Top, float Right, float Bottom) b) =>
        (Mathf.RoundToInt(Mathf.Min(a.Right, b.Right) - Mathf.Max(a.Left, b.Left)),
         Mathf.RoundToInt(Mathf.Min(a.Bottom, b.Bottom) - Mathf.Max(a.Top, b.Top)));

    /// <summary>Every venue, at its own <see cref="TownLayout2D.Venues"/> tile, sized by the SAME
    /// <see cref="TownAssets2D.ForVenue"/> call <c>Town2D.BuildBuildings</c> resolves through (real
    /// art first, the placeholder ladder on a miss — never a second hardcoded size table).</summary>
    private static IEnumerable<Placed> VenueObjects()
    {
        foreach (var venue in TownLayout2D.Venues)
        {
            var pos = TownLayout2D.TileToWorld(venue.Tile);
            var size = TownAssets2D.ForVenue(venue.SpriteId).GetSize();
            yield return new Placed($"BLD:{venue.Key}", pos.X, pos.Y, size.X, size.Y, HasNameplate: true);
        }
    }

    /// <summary>Every prop, keyed on its PLACEMENT INDEX — not its sprite id — exactly the way
    /// <c>Town2D.BuildProps</c> walks <see cref="TownLayout2D.Props"/> in array order. The lantern
    /// at placement 4 draws a DIFFERENT committed <see cref="ArtVariants"/> variant than the lantern
    /// at placement 0; sizing by id alone would silently collapse every lantern back onto one shared
    /// (and possibly wrong) footprint.</summary>
    private static IEnumerable<Placed> PropObjects()
    {
        var placementIndex = 0;
        foreach (var prop in TownLayout2D.Props)
        {
            var pos = TownLayout2D.TileToWorld(prop.Tile);
            var size = TownAssets2D.ForProp(prop.SpriteId, placementIndex).GetSize();
            yield return new Placed(
                $"PROP:{prop.SpriteId}({prop.Tile.X},{prop.Tile.Y})", pos.X, pos.Y, size.X, size.Y, HasNameplate: false);
            placementIndex++;
        }
    }

    /// <summary>The fixed starting six (<see cref="HeroRoster.StartingSix"/> — no RNG, so this is
    /// stable data rather than one campaign's snapshot) at their own <see
    /// cref="TownLayout2D.HeroHomeTiles"/> tile, sized by the SAME <see cref="TownAssets2D.ForHero"/>
    /// call <c>Town2D.ReconcileHeroes</c> resolves through. A recruit past the starting six (id 7+)
    /// is deliberately OUT OF SCOPE here — <c>Town2D.HomeFor</c> falls through to its own formula
    /// for those ids, which is not fixed layout data this census can pin (see that method's own
    /// U-T3-1 doc note).</summary>
    private static IEnumerable<Placed> HeroObjects()
    {
        foreach (var (id, hero) in HeroRoster.StartingSix())
        {
            var tile = TownLayout2D.HeroHomeTiles[id - 1];
            var pos = TownLayout2D.TileToWorld(tile);
            var size = TownAssets2D.ForHero(hero.ClassId, id).GetSize();
            yield return new Placed($"HERO{id}@({tile.X},{tile.Y})", pos.X, pos.Y, size.X, size.Y, HasNameplate: true);
        }
    }

    /// <summary>The four cosmetic villagers at their own <see
    /// cref="TownLayout2D.TownsfolkHomeTiles"/> tile, sized by the SAME body resolution
    /// <c>Town2D.BuildTownsfolk</c>'s own <c>BodyFor</c> uses: <see cref="TownsfolkNpc2D.BodyIdFor"/>
    /// through <see cref="IconRegistry.Art"/>, falling back to <see
    /// cref="TownsfolkNpc2D.ResolveSprite"/> exactly as that method does on a miss.</summary>
    private static IEnumerable<Placed> TownsfolkObjects()
    {
        for (var i = 0; i < TownLayout2D.TownsfolkHomeTiles.Length; i++)
        {
            var tile = TownLayout2D.TownsfolkHomeTiles[i];
            var pos = TownLayout2D.TileToWorld(tile);
            var civilianId = TownsfolkNpc2D.CivilianIds[i % TownsfolkNpc2D.CivilianIds.Length];
            var bodyId = TownsfolkNpc2D.BodyIdFor(civilianId, i);
            var texture = IconRegistry.Art(bodyId) ?? TownsfolkNpc2D.ResolveSprite(TownsfolkNpc2D.CivilianIds[0]);
            var size = texture.GetSize();
            yield return new Placed($"NPC{i}@({tile.X},{tile.Y})", pos.X, pos.Y, size.X, size.Y, HasNameplate: true);
        }
    }

    private static List<Placed> AllPlacedObjects() =>
        VenueObjects().Concat(PropObjects()).Concat(HeroObjects()).Concat(TownsfolkObjects()).ToList();

    /// <summary>
    /// Pinned as an EXACT SET — the <c>ArtManifestTests.KnownThreeFrameRobedTownsfolk</c> idiom.
    ///
    /// <para><b>U-T3-2 (register #163, "need to expand the size of the world"): EMPTY.</b> The
    /// sixteen rows this list used to carry were measured off a 40×28-tile world — 1.11 screens
    /// wide, every venue crammed inside a single screen with nowhere else for a lantern, a hero's
    /// home, or an ore cart to stand. The owner ruled 64×44; every placement table in
    /// <c>TownLayout2D</c> was re-laid onto that grid at today's real art sizes in the same PR, and
    /// this census — same art, same resolution ladder, just more room to stand in — now finds none.
    /// Emptying this list IS the unit's proof; adding a row back goes red (a NEW overlap shipped).
    /// </para>
    /// </summary>
    private static readonly string[] KnownSpriteOverlaps =
    [
    ];

    /// <summary>
    /// Pinned as an EXACT SET, same contract as <see cref="KnownSpriteOverlaps"/>.
    ///
    /// <para><b>U-T3-2: EMPTY.</b> The ten rows this list used to carry (a building's own nametag
    /// landing on ANOTHER building's sprite, a hero's nametag on the ore cart he was never
    /// touching) were all a symptom of the same 40×28 crowding <see cref="KnownSpriteOverlaps"/>'s
    /// own doc describes — the 64×44 re-lay clears every one of them too.</para>
    /// </summary>
    private static readonly string[] KnownNameplateOverlaps =
    [
    ];

    /// <summary>
    /// Pinned as an EXACT SET, same contract as <see cref="KnownSpriteOverlaps"/>.
    ///
    /// <para><b>U-T3-2: EMPTY.</b> The eight rows this list used to carry (props and heroes
    /// standing in a venue's own door-approach strip) were the same 40×28 crowding — the 64×44
    /// re-lay gives every venue's doorway apron room to stay clear too.</para>
    /// </summary>
    private static readonly string[] KnownDoorApproachOverlaps =
    [
    ];

    /// <summary>The census: every unique pair of placed sprites, in the SAME rect convention the
    /// client itself draws with. This is the check that would have caught all sixteen shipped
    /// overlaps, including the ore-cart/HERO1 one that motivated this whole unit.</summary>
    [TestCase]
    public void NoTwoPlacedObjects_HaveOverlappingSpriteRects()
    {
        var objects = AllPlacedObjects();
        var found = new List<string>();

        for (var i = 0; i < objects.Count; i++)
        {
            for (var j = i + 1; j < objects.Count; j++)
            {
                var a = objects[i];
                var b = objects[j];
                if (!Overlap(a.SpriteRect, b.SpriteRect))
                {
                    continue;
                }

                var (w, h) = IntersectionPx(a.SpriteRect, b.SpriteRect);
                found.Add($"{a.Label} x {b.Label} {w}x{h} px");
            }
        }

        found.Sort(System.StringComparer.Ordinal);
        var expected = KnownSpriteOverlaps.OrderBy(s => s, System.StringComparer.Ordinal).ToList();

        AssertThat(string.Join(", ", found))
            .OverrideFailureMessage(
                "The town's sprite-rect census changed.\n" +
                $"  found now: {string.Join(", ", found)}\n" +
                $"  pinned:    {string.Join(", ", expected)}\n" +
                "If you FIXED an overlap, delete its row from KnownSpriteOverlaps in the same PR. " +
                "If this is a NEW overlap, fix the placement in TownLayout2D — do not widen this list.")
            .IsEqual(string.Join(", ", expected));
    }

    /// <summary>The sprite-rect check alone misses this: <see cref="Building2D.BuildLabel"/> draws a
    /// nametag 10px ABOVE its owner's own sprite top, at a fixed Z-index that pulls it entirely out
    /// of Y-sort (<c>Building2D.NameplateZIndex</c>'s own doc) — so a nameplate can land on some
    /// OTHER object's sprite even when its owner's own sprite does not. This is how "Torvald"'s name
    /// ended up stamped on a wheelbarrow he was never touching.</summary>
    [TestCase]
    public void NoNameplate_LandsOnAnotherObjectsSprite()
    {
        var objects = AllPlacedObjects();
        var found = new List<string>();

        foreach (var owner in objects.Where(o => o.HasNameplate))
        {
            foreach (var other in objects)
            {
                if (other.Equals(owner) || !Overlap(owner.NameplateRect, other.SpriteRect))
                {
                    continue;
                }

                var (w, h) = IntersectionPx(owner.NameplateRect, other.SpriteRect);
                found.Add($"NAMEPLATE:{owner.Label} x {other.Label} {w}x{h} px");
            }
        }

        found.Sort(System.StringComparer.Ordinal);
        var expected = KnownNameplateOverlaps.OrderBy(s => s, System.StringComparer.Ordinal).ToList();

        AssertThat(string.Join(", ", found))
            .OverrideFailureMessage(
                "The town's nameplate-vs-sprite census changed.\n" +
                $"  found now: {string.Join(", ", found)}\n" +
                $"  pinned:    {string.Join(", ", expected)}\n" +
                "If you FIXED one, delete its row from KnownNameplateOverlaps in the same PR. " +
                "If this is a NEW one, fix the placement — do not widen this list.")
            .IsEqual(string.Join(", ", expected));
    }

    /// <summary>Lane clearance, checked as REAL pixels rather than a tile-vs-rect approximation:
    /// every venue's own doorway strip (<see cref="DoorwayStripPx"/>, <c>Building2D
    /// .BuildInteractArea</c>'s own walkable approach below the sprite) must stay clear of every
    /// OTHER prop/hero/townsfolk sprite. Supersedes <c>Town2DSceneTests
    /// .WarmHubProps_NeverSitOnABuildingApproachLane</c> (deleted by this PR), which only ever
    /// checked seven prop ids' TILE against <see cref="TownLayout2D.PathRects"/>'s spur rects —
    /// leaving a stronger guard beside a weaker one is an instruction the next session would have
    /// obeyed.</summary>
    [TestCase]
    public void NoPropOrActor_SitsOnAVenueDoorAnchorOrItsApproach()
    {
        var objects = AllPlacedObjects();
        var found = new List<string>();

        foreach (var venue in TownLayout2D.Venues)
        {
            var pos = TownLayout2D.TileToWorld(venue.Tile);
            var width = TownAssets2D.ForVenue(venue.SpriteId).GetSize().X;
            var lane = (Left: pos.X - width / 2f, Top: pos.Y, Right: pos.X + width / 2f, Bottom: pos.Y + DoorwayStripPx);

            foreach (var other in objects.Where(o => !o.Label.StartsWith("BLD:")))
            {
                if (!Overlap(lane, other.SpriteRect))
                {
                    continue;
                }

                var (w, h) = IntersectionPx(lane, other.SpriteRect);
                found.Add($"LANE:{venue.Key} x {other.Label} {w}x{h} px");
            }
        }

        found.Sort(System.StringComparer.Ordinal);
        var expected = KnownDoorApproachOverlaps.OrderBy(s => s, System.StringComparer.Ordinal).ToList();

        AssertThat(string.Join(", ", found))
            .OverrideFailureMessage(
                "The town's door-approach census changed.\n" +
                $"  found now: {string.Join(", ", found)}\n" +
                $"  pinned:    {string.Join(", ", expected)}\n" +
                "If you FIXED one, delete its row from KnownDoorApproachOverlaps in the same PR. " +
                "If this is a NEW one, fix the placement — do not widen this list.")
            .IsEqual(string.Join(", ", expected));
    }

    /// <summary>No known exceptions: every placement in <see cref="TownLayout2D"/> already sits
    /// inside the <see cref="TownLayout2D.GridWidth"/>x<see cref="TownLayout2D.GridHeight"/>-tile
    /// grid <c>Town2D.Build</c> lays out. A hand-placed table drifting off the edge is exactly the
    /// kind of typo this census exists to catch even though nothing has made it happen yet.</summary>
    [TestCase]
    public void EveryPlacedObject_FitsInsideTheGrid()
    {
        var offenders = AllPlacedObjects()
            .Where(o => o.Left < 0f || o.Top < 0f || o.Right > GridWidthPx || o.Bottom > GridHeightPx)
            .Select(o => $"{o.Label} spans ({o.Left},{o.Top})..({o.Right},{o.Bottom}), grid is 0,0..{GridWidthPx},{GridHeightPx}")
            .ToList();

        AssertThat(string.Join(", ", offenders))
            .OverrideFailureMessage(
                $"{offenders.Count} placed object(s) draw outside the {TownLayout2D.GridWidth}x" +
                $"{TownLayout2D.GridHeight}-tile grid:\n  " + string.Join("\n  ", offenders))
            .IsEqual(string.Empty);
    }
}
#endif
