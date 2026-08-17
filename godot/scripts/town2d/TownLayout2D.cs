using Godot;

namespace GodotClient.Town2d;

/// <summary>
/// U1: static venue→grid table for the 2.5D town — the 2D twin of <c>town3d.Town3D
/// .BuildingLayout</c>'s <c>Vector3</c> placement, keyed to <b>tile</b> coordinates instead. Keys
/// reuse <c>Town3D.Building3D.Key</c>'s exact vocabulary ("forge"/"market"/"tavern"/"minegate"/
/// "noticeboard") so <c>Town2D.FindBuilding</c> stays a drop-in for every existing caller, and
/// <see cref="Building2D.Key"/>'s single flat key space (U3, confirmed in
/// <c>Building2DInteractionTests</c>) means the SAME string is what <c>Town2D.BuildingClicked</c>
/// re-emits — MainUi's <c>OnTownBuildingClicked</c> switch (currently keyed on Town3D's
/// capitalized <c>ClickKey</c> strings, e.g. "Forge"/"Shop") needs its case labels lower-cased to
/// this vocabulary at the U2 cutover; see <c>Town2D</c>'s own class doc for the flagged
/// reconciliation note.
/// </summary>
public static class TownLayout2D
{
    /// <summary>Ground tile edge length, px (pivot plan pixel-discipline: 16×16 tiles).</summary>
    public const int TileSize = 16;

    /// <summary>
    /// Uniform downscale applied to every CHARACTER sprite (player, heroes, townsfolk) — the one
    /// place the cast's world scale is decided.
    ///
    /// <para>Why 1.0 and not some fraction: the world-scale proportion this constant used to
    /// encode (a blacksmith standing 60% the height of his own forge was Brian's "buildings are too
    /// small and the player model is massive" verdict — the buildings were never the wrong dial)
    /// is now baked into the committed PNGs themselves by <c>tools/art/gen_town_sprites.py</c>,
    /// which ships every character sprite already at its on-screen pixel size. This constant stays
    /// as the one place to retune that proportion later (against a screenshot) without touching a
    /// single PNG.</para>
    ///
    /// <para><b>2026-08-12 (asymmetric-decimation fix).</b> This used to be 0.5, applied at RUNTIME
    /// to a double-size source PNG, on the theory that a Nearest-filtered GPU sampler with mipmaps
    /// off would do a "clean 2:1 decimation" at draw time. It didn't: Nearest keeps exactly one
    /// column/row out of every mirrored pair, chosen by pixel-grid alignment, not by the art — a
    /// bilaterally-symmetric silhouette came out visibly lopsided on screen (measured on the real
    /// committed sprites: every mirror-symmetric row broke symmetry, by up to half its width), and a
    /// single-pixel authored accent (visor slit, rune, coolant trace, shield boss) was a coin flip
    /// to survive at all. The fix moved the halving OFFLINE into the generator (see its own
    /// <c>rarity_downsample_2x</c> doc) — the committed PNG already IS the on-screen pixel grid, so
    /// this is 1.0 (a pure pass-through) and there is no runtime decimation left to get wrong. See
    /// <c>CastProportionTests.NoRuntimeDecimation_CharacterSpriteScaleStaysOne</c> for the
    /// regression pin — reintroducing any value other than 1.0 here without ALSO re-baking the
    /// source art at that new scale reproduces this bug.</para>
    ///
    /// <para>Applied via <see cref="CharacterArtRoot"/> — an intermediate node between the actor and
    /// its sprite — rather than by re-generating the PNGs on every retune, so this stays one number
    /// to tune against a screenshot. Deliberately NOT folded into the per-frame <c>Sprite.Scale</c>
    /// assignment: <c>SpriteMotion</c> owns that value for walk squash/breath, and the
    /// feet-compensation offset math in every actor's pose-apply inverts <c>Sprite.Scale</c> to keep
    /// the feet planted — an extra constant factor in there silently breaks that inversion (it made
    /// the player's sprite drift 11px off its own feet line). Keeping the constant on the parent
    /// leaves the pose math, and the invariant tests that check it, exactly as they were.</para>
    /// </summary>
    public const float CharacterSpriteScale = 1.0f;

    /// <summary>Builds the per-actor node that carries <see cref="CharacterSpriteScale"/>: parent the
    /// actor's <c>Sprite2D</c> to this instead of to the actor itself. Its origin coincides with the
    /// actor's, so the sprite's feet-at-origin convention (and therefore the Y-sort baseline) is
    /// unchanged; it only scales what hangs below it. Nothing outside the town2d actors depends on
    /// the sprite's node PATH, so this extra level is invisible to the rest of the client.</summary>
    public static Godot.Node2D CharacterArtRoot() => new()
    {
        Name = "Art",
        Scale = new Godot.Vector2(CharacterSpriteScale, CharacterSpriteScale),
    };

    /// <summary>Town grid extent in tiles. <b>U-T3-2 (register #163, "need to expand the size of
    /// the world"):</b> the world used to be 40×28 (640×448px) — 1.11 screens wide by 1.38 tall on
    /// the 640×360 viewport, so every venue sat inside a single screen and the whole cluster read as
    /// one room ringed by trees. The owner ruled <b>64×44</b> (1024×704px): 1.60 screens wide by
    /// 1.96 tall, a 2.51× area increase over the old grid — enough room that a minimum town (gate,
    /// road, two building rows, a plaza, a tree line) actually fits without every object crowding
    /// the same few tiles. Both dimensions still exceed their viewport axis, so the <see
    /// cref="Camera2D"/> limits keep something to pan across on both axes. Every placement table
    /// below (<see cref="Venues"/>, <see cref="RallyTile"/>, <see cref="PathRects"/>, <see
    /// cref="Props"/>, <see cref="HeroHomeTiles"/>, <see cref="TownsfolkHomeTiles"/>) was re-laid
    /// onto this grid in the same PR, at today's art sizes — <c>godot/tests/TownPlacementTests.cs</c>
    /// pins the proof: its known-bad sprite/nameplate/door-approach overlap sets, sixteen sprite
    /// overlaps and a nameplate stamped on a wheelbarrow among them, all measured off the cramped
    /// 40×28 grid, are now empty exception sets rather than a widened list.</summary>
    public const int GridWidth = 64;

    public const int GridHeight = 44;

    /// <summary>One venue's placement: the tile its Y-sort line (front-door row) sits on, plus the
    /// sprite id <see cref="TownAssets2D"/> resolves through the art manifest/fallback ladder.
    ///
    /// <para><b>U7 (world-and-interiors plan, KTD-3):</b> <see cref="Nametag"/> is a STATIC
    /// default only — the workshop ("forge") venue's REAL nametag follows the player's selected
    /// profession(s) via <see cref="WorkshopVocab.NametagFor"/>, resolved at build/rebuild time in
    /// <c>Town2D.BuildBuildings</c> (which has the live <c>GameState</c> this struct never does).
    /// This table has no GameState access, so its "forge" row keeps reading "Forge" — the
    /// blacksmith default any GameState-free consumer of <see cref="Venues"/> still sees. The
    /// venue KEY itself ("forge") never changes (KTD-3(b): it is load-bearing across <c>MainUi</c>
    /// routing, quick-travel, and the tutorial's <c>StepBuilding</c>).</para>
    /// </summary>
    public readonly record struct VenueLayout(string Key, string Nametag, string SpriteId, Vector2I Tile);

    /// <summary>One static prop's placement: sprite id (resolved via <see
    /// cref="TownAssets2D.ForProp"/>), the tile its feet-origin sits on (same <see
    /// cref="TileToWorld"/> convention buildings use), whether it needs Y-sorting against
    /// heroes/the player (true for anything tall enough to be walked in front of/behind — a well,
    /// lantern post, or tree; a flush ground decal would pass false and mount under <see
    /// cref="Town2D.Ground"/> instead, though this slice has none).
    ///
    /// <para><b>Every prop's art ships at its on-screen pixel size</b> — 8-24px lantern/tree/crate,
    /// 32px well, and (U4, asset-completion wave) the eight <c>props-*</c> warm-hub entries below.
    /// Those eight were rendered at ~800-1024px and committed un-resized, which is why they went
    /// unmounted so long: dropped into this table as-is they draw several SCREENS wide on the
    /// 640×360 world viewport. They were resampled ONCE, offline, to the size the town actually
    /// draws them at (LANCZOS, ~11.87MB → ~31KB) rather than divided down at runtime by a per-entry
    /// scale factor — a 25-30× runtime downscale of a 1MB texture shimmers as the camera pans (the
    /// 2D importer keeps no mipmaps here) and holds ~33MB of VRAM for thumbnails. So there is no
    /// scale knob, and there should never need to be one: if a future prop draws wrong, its art is
    /// the wrong size and that is where to fix it.</para>
    /// </summary>
    public readonly record struct PropLayout(string SpriteId, Vector2I Tile, bool YSorted);

    /// <summary>
    /// The five venues <c>Town3D.BuildingLayout</c> places (forge/market/tavern/minegate/
    /// noticeboard) — the "forge-station"/"counter-station" pair is deliberately OUT for this
    /// slice (scope boundaries: no diegetic in-world minigame stations tonight, see the pivot
    /// plan's "NOT tonight" list); the drawer panels already carry that gameplay verb via the
    /// venue interior route. Tightened into a cozy cluster around a central plaza (~tile 20,15,
    /// see <see cref="PathRects"/>'s plaza rect): forge NW, market NE, tavern SW, noticeboard SE,
    /// each one tile clear of the plaza/road cobble network via a short spur, with the mine gate
    /// north up the road (mirrors <c>Town3D</c>'s "gate departs north, square/market/tavern south"
    /// read, just pulled in tight instead of spread corner-to-corner). Tiles are hand-placed clear
    /// of each other's sprite footprint (see each <c>TownAssets2D.VenuePlaceholders</c> size, which
    /// matches the real generated-art pixel dimensions 1:1).
    ///
    /// <para><b>These draw the pre-pivot SDXL set</b> (2026-08-01 building-exterior receipt —
    /// see <c>runs/receipts/</c>, options A-D, and <c>art/pipeline/recolor-forge-roof.py</c>'s own
    /// doc for the full trace). #316 swapped every venue to the <c>town2d-*</c> pixel set to kill
    /// the Forge's magenta roof (#520051), which was real — but the owner's playtest verdict was
    /// "the buildings look WORSE, we only asked for interior changes": he never asked for an
    /// exterior swap and prefers this SDXL look. Measuring both sets confirms the pixel set's
    /// regression is real too (its shared structural palette runs 0.14-0.35 saturation vs this
    /// set's 0.16-0.41 — see <c>art/pipeline/boost-town2d-palette.py</c>'s own doc for the numbers),
    /// so #316's fix and the owner's complaint are BOTH correct about their own building set. This
    /// keeps the set he prefers and fixes the one thing that was actually broken in it: forge.png's
    /// roof is recoloured terracotta (sampled from tavern.png's own shingles) by
    /// <c>art/pipeline/recolor-forge-roof.py</c>, nothing else about the SDXL set changed.
    /// Footprints use the real PNG dimensions (forge 72×81, market 76×62, tavern 84×88, gate
    /// 48×48, noticeboard 44×50) at the SAME tile coordinates #316 used — verified clear: forge
    /// spans world-Y 119-200, tavern 208-296 (8px gap); market spans Y 138-200, noticeboard
    /// 246-296; the mine gate (Y8-56) shares no row with either.</para>
    ///
    /// <para><b>Switching options</b> (all four were rendered and compared, see the receipt):
    /// back to the <c>town2d-*</c> pixel set (options A/C) — edit the five
    /// <see cref="VenueLayout.SpriteId"/> strings below to their <c>town2d-*</c> equivalents AND mirror the same five keys in
    /// <see cref="TownAssets2D.VenuePlaceholders"/> (that committed set is already boosted —
    /// Option C, <c>art/pipeline/boost-town2d-palette.py</c> — not the original muddy one the
    /// owner rejected). Back to the unrecoloured magenta roof (option B) — overwrite
    /// <c>godot/assets/art/forge.png</c> with the frozen pre-fix copy at
    /// <c>art/pipeline/sources/forge-sdxl-magenta.png</c>.</para>
    ///
    /// <para><b>U-T3-2 (register #163, 64×44 grid):</b> re-laid off the cramped 40×28 tile
    /// coordinates onto the new grid — mine gate north on the road, forge/market as the north
    /// building row, tavern/noticeboard as the south row, the plaza (well, lanterns, string-lights)
    /// sitting in the yard between them. Every footprint below is checked clear of every other
    /// venue/prop/hero/townsfolk sprite, nameplate and door-approach lane at today's real PNG sizes
    /// (forge 72×81, market 76×62, tavern 84×88, mine-gate 48×48, noticeboard 44×50) — see <see
    /// cref="Props"/>'s own doc and <c>TownPlacementTests</c>'s now-empty exception sets.</para>
    /// </summary>
    public static readonly VenueLayout[] Venues =
    {
        new("forge", "Forge", "forge", new Vector2I(18, 26)),
        new("market", "Shop", "market", new Vector2I(46, 26)),
        new("tavern", "Tavern", "tavern", new Vector2I(18, 40)),
        // U2 (playtest-three plan): was "Gate" — bare enough to read as generic scenery next to a
        // "Bounties" building that actually opens a DIFFERENT panel (Depths). Nametag only; the
        // click-routing key ("minegate") and TownAssets2D's sprite id are untouched.
        new("minegate", "Mine Gate", "mine-gate", new Vector2I(32, 8)),
        new("noticeboard", "Bounties", "noticeboard", new Vector2I(46, 40)),
    };

    /// <summary>Where departing heroes rally (dwell as a cluster) before marching to the mine
    /// gate's own <see cref="Building2D.DoorAnchorGlobal"/> — sits ON the north road (see <see
    /// cref="PathRects"/>) between the plaza and the gate, mirroring <c>Town3D.RallySpotFor</c>'s
    /// "near the gate, spread along one axis" read. U-T3-2: moved with the gate from (20,9) to sit
    /// on the new road at column 32.</summary>
    public static readonly Vector2I RallyTile = new(32, 14);

    /// <summary>
    /// Cobble tile rects (<see cref="Town2D.BuildGround"/> paints every cell inside these over the
    /// grass base, using the atlas's existing Cobble coord) forming one connected network: a
    /// central plaza, a north road up to the mine gate, and a short spur from each building's
    /// door-front tile into the plaza/road — replaces the old "one path tile per venue door"
    /// decoration with an actual walkable-reading street layout. Rects deliberately overlap by a
    /// tile at each junction (redundant repaints, not a bug) so the network reads as continuous
    /// rather than as disjoint tile confetti.
    ///
    /// <para><b>U-T3-2 (64×44 grid):</b> re-laid around the new venue positions — the plaza now
    /// sits in the yard between the north row (forge/market) and the south row (tavern/
    /// noticeboard), with the well/lantern cluster (see <see cref="Props"/>) inside it, and the
    /// road running north from the plaza up to the relocated mine gate.</para>
    /// </summary>
    public static readonly Rect2I[] PathRects =
    {
        // Central plaza: x22-41, y28-39 (20×12 tiles), framing the well/lantern props below.
        new(22, 28, 20, 12),
        // North road: the mine gate's door-front row (8) down to the plaza's north edge (28).
        new(31, 8, 2, 21),
        // Forge spur: its door-front row (26) into the plaza's NW corner (22,28).
        new(18, 26, 5, 3),
        // Market spur: its door-front row (26) into the plaza's NE corner (41,28).
        new(41, 26, 5, 3),
        // Tavern spur: plaza's south edge (39) down to its door-front row (40).
        new(18, 39, 5, 2),
        // Noticeboard spur: plaza's south edge (39) down to its door-front row (40).
        new(41, 39, 5, 2),
    };

    /// <summary>
    /// Static decoration: a well anchoring the plaza center, lanterns at the plaza's four corners,
    /// trees framing the map's open edges/corners, and a couple of crates beside the market — all
    /// placed on open grass clear of every venue footprint and the cobble network (props carry no
    /// collision, so this is a legibility/framing choice, not a pathing constraint). <see
    /// cref="Town2D.BuildProps"/> instantiates each entry.
    ///
    /// <para><b>U-T3-2 (register #163, 64×44 grid):</b> every tile re-laid onto the bigger grid at
    /// TODAY's real committed art sizes (well 40×68, lantern 16×44, tree 28×40, crate 16×16 —
    /// measured off the PNG headers, not the placeholder-fallback table in <see
    /// cref="TownAssets2D"/>, which only applies when art is missing). <b>Lanterns dropped from 8
    /// to 4</b> (register #144, "22 light sources in a four-building village") — plaza corners
    /// only now; the road and window glow carry the rest. <c>TownPlacementTests</c>' sprite,
    /// nameplate and door-approach-lane exception sets are all empty against this table — the
    /// sixteen overlaps a four-building 40×28 village had nowhere to avoid are gone, not widened.
    /// </para>
    /// </summary>
    public static readonly PropLayout[] Props =
    {
        new("town2d-well", new Vector2I(32, 33), true),

        // Lanterns: plaza's four corners only (register #144 — was 8, flanking every door too).
        new("town2d-prop-lantern", new Vector2I(24, 30), true),
        new("town2d-prop-lantern", new Vector2I(40, 30), true),
        new("town2d-prop-lantern", new Vector2I(24, 38), true),
        new("town2d-prop-lantern", new Vector2I(40, 38), true),

        // Trees: framing the new 64x44 perimeter, well clear of the central cluster.
        new("town2d-prop-tree", new Vector2I(2, 2), true),
        new("town2d-prop-tree", new Vector2I(16, 2), true),
        new("town2d-prop-tree", new Vector2I(48, 2), true),
        new("town2d-prop-tree", new Vector2I(61, 2), true),
        new("town2d-prop-tree", new Vector2I(2, 12), true),
        new("town2d-prop-tree", new Vector2I(2, 21), true),
        new("town2d-prop-tree", new Vector2I(61, 21), true),
        new("town2d-prop-tree", new Vector2I(61, 33), true),
        new("town2d-prop-tree", new Vector2I(2, 42), true),
        new("town2d-prop-tree", new Vector2I(10, 43), true),
        new("town2d-prop-tree", new Vector2I(51, 43), true),
        new("town2d-prop-tree", new Vector2I(61, 42), true),

        // Crates: a couple stacked just east of the market's footprint.
        new("town2d-prop-crate", new Vector2I(54, 18), true),
        new("town2d-prop-crate", new Vector2I(54, 20), true),

        // U4 (asset-completion wave, docs/design/ASSETS.md "warm-hub town props"): committed,
        // resolution-tested (ArtWiringCoverageTests.TownProps_ResolveWithNormal) props that nothing
        // ever drew until this table. Every tile below is checked clear of every venue footprint
        // (Venues above) and every walkable lane TownLayout2D.PathRects carries INTO a building —
        // the plaza square itself is exempted from that check, matching this file's own
        // established precedent (the well sits dead center of it, corner lanterns flank it): a
        // wide-open square tolerates a decoration, a 1-2-tile spur does not.

        // A market yard, north of its footprint — clear of the market's own spur.
        new("props-market-crates", new Vector2I(54, 22), true),

        // A second, informal flyer board over by the market — NOT the same object as the
        // "noticeboard" VENUE key below (that key is the Bounties building at (46,40), a
        // different system entirely; see this class's own U4 doc note on the name collision).
        new("props-noticeboard", new Vector2I(42, 31), true),

        // Festival garland over the top of the plaza, clear of the north road and every spur.
        new("props-string-lanterns", new Vector2I(32, 28), true),

        // Ore cart parked in the yard behind the forge's west wall.
        new("props-ore-cart", new Vector2I(12, 24), true),

        // The forge's own pet, curled by the coals.
        new("props-forge-salamander", new Vector2I(10, 28), true),

        // Laundry strung in the backyard gap between the forge and the tavern.
        new("props-laundry-line", new Vector2I(9, 33), true),

        // Napping on the tavern's south side.
        new("props-tavern-cat", new Vector2I(24, 41), true),

        // RESOLVED 2026-08-16. A second "props-town-well" used to sit three tiles from the
        // "town2d-well" above — the U4 doc note called that a genuine open question rather than a
        // two-wells design call, and it stayed open long enough that a village of four buildings
        // shipped with two wells in it. The owner ruled: keep the one matching the new town. That is
        // town2d-well, which belongs to the town2d pixel set the whole village is drawn from; the
        // props- one is the older generation. Its asset is deleted rather than orphaned, per the
        // standing no-orphans rule — git history is the archive if it is ever wanted back.
    };

    /// <summary>
    /// U-T3-1 (placement-census unit): the six starting heroes' deterministic wander-home tile,
    /// extracted VERBATIM from <c>Town2D.HomeFor</c>'s own formula — <c>TileToWorld(new
    /// Vector2I(6 + id*3 % 28, 10 + id*2 % 6))</c> evaluated for <c>id</c> 1..6 (index 0 is hero id
    /// 1, ..., index 5 is hero id 6) — so a test can actually see where a hero starts wandering
    /// from; before this it was a private formula buried in a 1,985-line adapter, and nobody could
    /// check it against anything else in the town.
    ///
    /// <para>Covers only the fixed starting six (<c>GameSim.Heroes.HeroRoster.StartingSix</c>).
    /// <c>Town2D.HomeFor</c> keeps evaluating its own formula for any id past 6: a recruit's numeric
    /// id is never reused after a death (see <c>RecruitSystem</c>), so ids climb past 6 in every
    /// campaign that outlives its opening roster, and the formula's period (its X term repeats every
    /// 28 ids, not 6) means this 6-slot table has no value that could stand in for id 7+ without
    /// silently changing where a surviving recruit's home band sits.</para>
    ///
    /// <para><b>U-T3-2 (64×44 grid):</b> re-laid clear of every venue/prop footprint, nameplate and
    /// door-approach lane on the new grid — see <c>TownPlacementTests</c>' now-empty exception
    /// sets.</para>
    /// </summary>
    public static readonly Vector2I[] HeroHomeTiles =
    {
        new(8, 20), new(8, 36), new(58, 20), new(58, 34), new(30, 20), new(34, 20),
    };

    /// <summary>
    /// U-T3-1: <c>Town2D.BuildTownsfolk</c>'s cosmetic-villager wander-home tiles, extracted
    /// VERBATIM from that method's own private table (formerly hand-declared inside
    /// <c>Town2D.cs</c>, where no test could reach it). Two open corners northwest/northeast of the
    /// plaza, two more southwest/southeast of it — clear of every venue footprint and the
    /// <see cref="PathRects"/> cobble network.
    ///
    /// <para><b>U-T3-2 (64×44 grid):</b> re-laid at the new grid's four outer corners, clear of
    /// every venue/prop footprint, nameplate and door-approach lane — see
    /// <c>TownPlacementTests</c>' now-empty exception sets.</para>
    /// </summary>
    public static readonly Vector2I[] TownsfolkHomeTiles =
    {
        new(6, 12), new(58, 12), new(6, 40), new(58, 40),
    };

    /// <summary>U-T3-1: <c>HeroActor2D</c>'s idle lissajous wander-drift half-amplitude in px, X
    /// axis — extracted VERBATIM from that class's own private const (unchanged value) so
    /// <c>TownPlacementTests</c> can inflate a hero's home tile by the SAME band the actor actually
    /// wanders within; the guard and the motion read one number now and can never drift apart.</summary>
    public const float HeroWanderAmplitudeX = 14f;

    /// <summary>U-T3-1: as <see cref="HeroWanderAmplitudeX"/>, Y axis.</summary>
    public const float HeroWanderAmplitudeY = 10f;

    /// <summary>U-T3-1: <c>TownsfolkNpc2D</c>'s own wander-drift half-amplitude in px — deliberately
    /// smaller than <see cref="HeroWanderAmplitudeX"/> (villagers putter close to home rather than
    /// roaming as far as heroes do); same "extracted, unchanged" contract as that constant.</summary>
    public const float TownsfolkWanderAmplitudeX = 9f;

    /// <summary>U-T3-1: as <see cref="TownsfolkWanderAmplitudeX"/>, Y axis.</summary>
    public const float TownsfolkWanderAmplitudeY = 5f;

    /// <summary>Tile coordinate → world-space pixel position of that tile's CENTER. Buildings are
    /// positioned by their Y-sort line (see <see cref="Building2D.Configure"/>'s remarks) at this
    /// same convention — one flat conversion used for every placement (venues, rally point, hero
    /// wander homes, props) so nothing drifts out of the tile grid by a stray pixel offset.</summary>
    public static Vector2 TileToWorld(Vector2I tile) =>
        new(tile.X * TileSize + TileSize / 2f, tile.Y * TileSize + TileSize / 2f);
}
