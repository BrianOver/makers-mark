#if GDUNIT_TESTS
using System.Collections.Generic;
using System.Linq;
using GameSim.Classes;
using GameSim.Venues;
using GdUnit4;
using Godot;
using GodotClient.Town2d;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// U3 (make-it-visible plan, docs/plans/2026-08-01-001): a census over every art id the SHIPPED
/// 2.5D render path can actually ask for, asserting each one resolves to real committed art —
/// extending the reasoning behind <see cref="Town2DSceneTests"/>'s
/// <c>EveryVenueSpriteId_IsInThePixelSet_AndResolvesToCommittedArt</c> (landed with #316) to every
/// OTHER family the render path touches, rather than re-deriving it a second time under a second
/// name. Venues are deliberately NOT re-asserted here — that test already pins them, reading
/// straight from <see cref="TownLayout2D.Venues"/>; asserting the same fact twice under two names
/// is the "hand-maintained list" anti-pattern in miniature (two places to update, one to rot).
/// Between the two files, every id family below IS the full census.
///
/// <para><b>Why this exists.</b> The town drew the pre-pivot SDXL buildings for WEEKS while the
/// complete <c>town2d-*</c> pixel set sat committed and unreferenced: <c>TownLayout2D.Venues</c>
/// still named the old bare ids, and <c>TownAssets2D.ForVenue</c> is deliberately null-tolerant
/// (crash-safety on a fresh, unimported checkout matters more than a loud failure), so the wrong id
/// quietly resolved to a REAL image — the stale PNG is real, committed art, not a placeholder box —
/// and nothing ever failed. The owner found it because the Forge roof happened to be magenta. This
/// census enumerates every id the render path can ask for TODAY, reading it from the SAME
/// tables/consts the render path itself reads (never a hand-copied string list, which rots exactly
/// like the state-fingerprint lesson: docs memory "State fingerprint must be complete"), and fails
/// at PR time the moment one stops resolving to committed art.</para>
///
/// <para><b>"Resolves" is not "resolves to the RIGHT art" — the actual #316 lesson.</b> A bare
/// non-null assertion would have sailed straight through the Forge bug, because the stale SDXL
/// PNG is ALSO committed and ALSO resolves. Every family below where an old/new pair of assets
/// exists side by side on disk today gets a SET-pinned assertion — the specific <c>town2d-</c> (or
/// <c>town2d-monster-</c>) id must be the one that resolves, not merely "something eventually
/// does". Families with no such stale twin (props, the player, the ground atlas — none of these
/// ever had a prior art generation under a different name) are resolution-only, since there is no
/// second asset for them to silently regress onto.</para>
///
/// <para><b>Coverage table</b> (id source → family pinned):
/// <list type="bullet">
/// <item><see cref="ClassRegistry.RecruitPool"/> → <c>town2d-hero-{classId}</c> / <c>_step</c>
/// (SET: the alternative is <c>IconRegistry.Sprite</c>'s hand-authored roster SVG, a different art
/// style/size meant for panels, not the town — see <see cref="TownAssets2D.ForHero"/>'s own "town
/// bodies win here" comment). Also covers <see cref="TownsfolkNpc2D"/>, which reuses the vanguard
/// entry verbatim for its civilian body.</item>
/// <item><see cref="VenueRegistry.Mine"/>'s five <c>VenueFloor.MonsterKind</c> values →
/// <c>town2d-monster-{slug}</c> (SET: every one of these five has an old, non-<c>town2d-</c>
/// portrait committed too — <c>monster-cave-rat.png</c> et al — so this is the exact #316 shape:
/// a wrong/missing new id would silently draw the old portrait via <c>DelveStage.ShowMonster</c>'s
/// own fallback, and nothing would fail).</item>
/// <item><see cref="TownLayout2D.Props"/> → each distinct <c>SpriteId</c> (resolution-only).</item>
/// <item><see cref="PlayerController2D.PlayerSpriteId"/> / <c>PlayerStepSpriteId</c>
/// (resolution-only).</item>
/// <item>The ground tile atlas — resolution-only; see <see cref="GroundAtlasId"/>'s own doc for why
/// this one id is hand-copied rather than read off a table.</item>
/// </list></para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class AssetResolutionCensusTests
{
    /// <summary>
    /// The single tile id the render path ever asks for (<c>Town2D.BuildTileSet</c>,
    /// <c>godot/scripts/town2d/Town2D.cs</c>, the <c>IconRegistry.Art("town2d-ground-atlas")</c>
    /// call). Not a public constant anywhere, so — unlike every other entry in this file — there is
    /// no table to read it from without widening <c>Town2D</c>'s surface for one string; this is the
    /// one deliberately hand-copied id in this census, flagged here instead of silently left out.
    /// Godot's ground atlas has a graceful, ALREADY-working in-code fallback (a flat 2-tile TileSet
    /// built in <c>BuildTileSet</c> itself) if this id ever stops resolving, so an id-check here is
    /// a coverage improvement, not closing a crash risk.
    /// </summary>
    private const string GroundAtlasId = "town2d-ground-atlas";

    /// <summary>
    /// Ids known to be intentionally absent right now — checked before every resolve assertion so
    /// a genuinely-pending id (e.g. a second live venue's pixel set, or a registered-but-not-yet-
    /// recruitable class getting promoted into <see cref="ClassRegistry.RecruitPool"/> before its
    /// art lands) is a one-line, reviewable addition instead of commenting out an assertion.
    ///
    /// <para>U6 (docs/plans/2026-08-02-002) closed the last entries this set ever held: T1's
    /// content flip (relands PR #242) promoted sentinel/skirmisher/occultist into <see
    /// cref="ClassRegistry.RecruitPool"/> before their town pixel bodies were drawn, so their
    /// <c>town2d-hero-*</c> ids resolved via <c>TownAssets2D.ForHero</c>'s next rung (the roster
    /// SVG) instead — wrong art style, never a crash, never a magenta box, but not what ships.
    /// <c>tools/art/gen_town_sprites.py</c> now authors all six classes at 26x44, so every id below
    /// resolves to its real town body and the census enforces it forever. Empty, not deleted: the
    /// shape stays ready for the next promoted-before-art class.</para>
    /// </summary>
    private static readonly HashSet<string> KnownPendingIds = new()
    {
        // U1 (world-and-interiors plan, docs/plans/2026-08-02-004): market/tavern/gatehouse room
        // shells + station art — framework lands here as loud placeholders (TownAssets2D's
        // magenta-bordered box, not silence); U2/U3/U4 paint the real pixels and remove these
        // lines one room at a time (red→green shown in each art unit's own PR body).
        "town2d-market-interior-shell",
        "town2d-station-market-counter",
        "town2d-station-market-shelf",
        "town2d-station-market-ledger",
        "town2d-station-market-crates",
        "town2d-tavern-interior-shell",
        "town2d-station-tavern-bar",
        "town2d-station-tavern-table",
        "town2d-station-tavern-hearth",
        "town2d-station-tavern-storywall",
        "town2d-gatehouse-interior-shell",
        "town2d-station-gate-muster",
        "town2d-station-gate-bounty",
        "town2d-station-gate-overlook",
        "town2d-station-gate-winch",
    };

    [TestCase]
    public void RecruitableHeroClasses_ResolveTheTownPixelBody_NotJustAnyFallback()
    {
        foreach (var classId in ClassRegistry.RecruitPool)
        {
            AssertResolves(
                $"town2d-hero-{classId}",
                $"'{classId}' is in ClassRegistry.RecruitPool, so a live Hero can carry this class "
                + "any day the game runs, and TownAssets2D.ForHero draws this exact id for it. "
                + "Without it, ForHero's next rung is IconRegistry.Sprite's hand-authored roster "
                + "SVG — real, committed art, sized for a panel portrait, not a walking town body — "
                + "which is precisely the 'resolves, but to the wrong thing' shape #316 was.");

            AssertResolves(
                $"town2d-hero-{classId}_step",
                $"the walk-cycle step frame for '{classId}' — HeroActor2D swaps to this on "
                + "alternating footfalls. Missing it doesn't crash (SpriteMotion just holds the "
                + "base frame forever), it just means this class never visibly strides.");
        }
    }

    [TestCase]
    public void MineMonsterKinds_ResolveTheirTownPixelPortrait_NotTheOldSdxlOne()
    {
        foreach (var floor in VenueRegistry.Mine.Floors)
        {
            // AssetCatalog.MonsterPortraitId(kind) with no venue prefix returns "monster-{slug}" —
            // the OLD SDXL family DelveStage.ShowMonster falls back to. Prefixing "town2d-" onto
            // that gives the exact id ShowMonster tries FIRST, without this file re-deriving
            // AssetCatalog's private Slugify algorithm by hand (a second, driftable copy of it).
            var oldSdxlId = AssetCatalog.MonsterPortraitId(floor.MonsterKind);
            var townPixelId = "town2d-" + oldSdxlId;

            AssertResolves(
                townPixelId,
                $"floor {floor.Floor}'s monster ('{floor.MonsterKind}') has an old, non-town2d "
                + $"portrait committed too ({oldSdxlId}.png and friends) — DelveStage.ShowMonster "
                + "tries the town2d- id first and silently falls back to that old one if it is "
                + "missing, so a broken new id here shows the OLD art with nothing failing. "
                + "Exactly the #316 shape, one panel over.");
        }
    }

    [TestCase]
    public void EveryConfiguredPropSpriteId_ResolvesToCommittedArt()
    {
        foreach (var spriteId in TownLayout2D.Props.Select(p => p.SpriteId).Distinct())
        {
            AssertResolves(
                spriteId,
                $"'{spriteId}' is a TownLayout2D.Props entry — Town2D.BuildProps mounts one for "
                + "every placement in that table via TownAssets2D.ForProp.");
        }
    }

    [TestCase]
    public void PlayerSpriteIds_ResolveToCommittedArt()
    {
        AssertResolves(
            PlayerController2D.PlayerSpriteId,
            "the player-smith's body — PlayerController2D.ResolvePlayerTexture's first rung.");

        AssertResolves(
            PlayerController2D.PlayerStepSpriteId,
            "the player-smith's walk-cycle step frame — PlayerController2D.ResolveStepTexture's "
            + "first rung (missing it just holds the base frame; see that method's own doc).");
    }

    [TestCase]
    public void GroundAtlas_ResolvesToCommittedArt()
    {
        AssertResolves(
            GroundAtlasId,
            "Town2D.BuildTileSet's preferred ground tile atlas — every grass/cobble tile the town "
            + "paints comes from this one texture's atlas coords.");
    }

    /// <summary>
    /// U2 (painted-interiors plan, docs/plans/2026-08-02-001) — the seven Forge-interior art ids
    /// (one room shell + six station props) authored by <c>art/pipeline/gen-forge-interior.py</c>
    /// and mounted by <c>InteriorRoom2D</c> (U1, a parallel branch). Hardcoded here rather than
    /// read off <c>InteriorLayout2D</c>'s table (the pattern every other test in this file
    /// follows): U1 had not merged onto this branch when this test was authored, so there was no
    /// live table to enumerate yet. Asserted directly against <see cref="IconRegistry.Art"/>
    /// rather than through <see cref="AssertResolves"/>'s <see cref="KnownPendingIds"/> escape
    /// hatch on purpose: once this unit's PNGs are committed there is no legitimate "pending"
    /// state left for these ids, so this must fail loudly even if a stale KnownPendingIds entry
    /// for one of them survives a merge with U1 (which ships these same ids as placeholders).
    /// </summary>
    private static readonly string[] ForgeInteriorArtIds =
    {
        "town2d-forge-interior-shell",
        "town2d-station-anvil",
        "town2d-station-furnace",
        "town2d-station-bellows",
        "town2d-station-quench",
        "town2d-station-shelf",
        "town2d-station-rack",
    };

    [TestCase]
    public void ForgeInteriorArtIds_ResolveToCommittedArt_NeverAPlaceholder()
    {
        foreach (var id in ForgeInteriorArtIds)
        {
            AssertThat(IconRegistry.Art(id))
                .OverrideFailureMessage(
                    $"census: '{id}' (U2, painted-interiors plan) does not resolve to committed "
                    + "art. InteriorRoom2D mounts this id for the Forge room shell/stations; a "
                    + "miss renders TownAssets2D's loud magenta placeholder, which this unit "
                    + "exists to retire.")
                .IsNotNull();
        }
    }

    /// <summary>
    /// Missing-ore-icon fix: <see cref="GameSim.Materials.MaterialRegistry.PricedPool"/> (the sim's
    /// own registry, not a hand-written list — a new ore added tomorrow is enumerated here for free)
    /// is the exact set <c>ForgePanel</c>'s vendor shelf iterates, calling <c>IconRegistry.Ore(key)</c>
    /// for every entry on every draw. Unlike <see cref="IconRegistry.Art"/>, <c>IconRegistry.Ore</c>
    /// (<c>IconRegistry.Load</c>) has NO null-tolerant rung and no manifest gate — a missing
    /// <c>res://assets/icons/ore_{key}.svg</c> is a native <c>core/io/resource_loader.cpp</c> ERROR at
    /// runtime (260 of them, 5 distinct ids, one real playtest run), not a graceful placeholder. This
    /// is the gap <see cref="AssertResolves"/> above cannot see, because it only ever reads the
    /// generated-art manifest under <c>res://assets/art/</c>, a different loader for a different
    /// directory.
    /// </summary>
    [TestCase]
    public void PricedOreMaterials_ResolveTheirVendorIcon()
    {
        foreach (var key in GameSim.Materials.MaterialRegistry.PricedPool)
        {
            AssertThat(IconRegistry.Ore(key))
                .OverrideFailureMessage(
                    $"census: IconRegistry.Ore('{key}') does not resolve to a committed icon. "
                    + $"'{key}' is in MaterialRegistry.PricedPool, so ForgePanel's vendor shelf draws "
                    + "it every time the panel opens. IconRegistry.Ore has no null-tolerant fallback: "
                    + "a missing SVG for it is a native engine ERROR at runtime, not a placeholder.")
                .IsNotNull();
        }
    }

    /// <summary>
    /// U1's OWN table-driven twin of <see cref="ForgeInteriorArtIds_ResolveToCommittedArt_NeverAPlaceholder"/>
    /// above (U2, landed first onto main) — reads ids straight from <see
    /// cref="InteriorLayout2D.Rooms"/> rather than a hand-copied array, the pattern every other
    /// case in this file follows, so a future slice-2 room (market/tavern/minegate) is covered the
    /// moment its row lands here with zero edits to this test. Deliberately kept alongside the
    /// hardcoded U2 case above rather than deleting one: U2 already merged and is not this unit's
    /// file to rewrite, and the two are cheap, harmless duplication rather than a real conflict —
    /// both must stay green together.
    /// </summary>
    [TestCase]
    public void InteriorRoomSpriteIds_ResolveToCommittedArt()
    {
        foreach (var room in InteriorLayout2D.Rooms.Values)
        {
            AssertResolves(
                room.ShellSpriteId,
                $"'{room.ShellSpriteId}' is the '{room.VenueKey}' room's shell (floor/walls) — "
                + "InteriorRoom2D.Build draws it for every player who enters.");

            foreach (var station in room.Stations)
            {
                AssertResolves(
                    station.SpriteId,
                    $"'{station.SpriteId}' is the '{station.Id}' station in the '{room.VenueKey}' "
                    + "room — InteriorRoom2D.BuildStations draws it for every player who enters.");
            }
        }
    }

    /// <summary>
    /// U3 (painted-interiors plan, "also owed"): U2 authored the six forge station sprites BEFORE
    /// U1 pushed the room/footprint code, judging each only on a standalone composite — never
    /// against the actual tile spacing. <c>Building2D.Configure</c> derives a station's COLLISION
    /// footprint directly from its texture's own pixel size (no separate declared-size field
    /// anywhere), so an oversized sprite silently becomes an oversized hitbox that can reach into a
    /// neighboring station or the room wall. This pins each shipped PNG to the KTD-5 authoring
    /// brief's own size (the plan text's one written record of what each station was SUPPOSED to
    /// be) so a future re-paint that quietly grows a sprite fails here — at table-validation time —
    /// instead of shifting collision the next time someone stands at the shelf.
    /// </summary>
    [TestCase]
    public void ForgeStationArt_MatchesItsKtd5DeclaredFootprint_NeverShiftsCollision()
    {
        var expectedSizes = new Dictionary<string, Vector2>
        {
            ["town2d-station-anvil"] = new(24, 20),
            ["town2d-station-furnace"] = new(32, 40),
            ["town2d-station-bellows"] = new(20, 14),
            ["town2d-station-quench"] = new(24, 14),
            ["town2d-station-shelf"] = new(28, 32),
            ["town2d-station-rack"] = new(28, 32),
        };

        foreach (var room in InteriorLayout2D.Rooms.Values)
        {
            foreach (var station in room.Stations)
            {
                if (!expectedSizes.TryGetValue(station.SpriteId, out var expected))
                {
                    continue; // a future slice-2 station this test predates — not this unit's table to guess at
                }

                var art = IconRegistry.Art(station.SpriteId);
                if (art is null)
                {
                    continue; // the census test above already fails loudly for a missing id
                }

                AssertThat(art.GetSize())
                    .OverrideFailureMessage(
                        $"'{station.SpriteId}' (station '{station.Id}' in room '{room.VenueKey}') is "
                        + $"{art.GetSize()}px, but its declared KTD-5 footprint is {expected}px. "
                        + "Building2D.Configure derives collision straight from texture size, so this "
                        + "mismatch SHIFTS COLLISION at the station's tile — never a cosmetic-only diff.")
                    .IsEqual(expected);
            }
        }

        // Room shell art must match InteriorLayout2D's own computed room size the same way —
        // TownAssets2D.ForShell is handed that exact size, so a mismatch here would mean the shell
        // PNG and the room's actual walkable rect have silently drifted apart.
        foreach (var room in InteriorLayout2D.Rooms.Values)
        {
            var shellArt = IconRegistry.Art(room.ShellSpriteId);
            if (shellArt is null)
            {
                continue;
            }

            var expectedShellSize = new Vector2(
                room.SizeTiles.X * TownLayout2D.TileSize, room.SizeTiles.Y * TownLayout2D.TileSize);
            AssertThat(shellArt.GetSize())
                .OverrideFailureMessage(
                    $"'{room.ShellSpriteId}' (the '{room.VenueKey}' room's shell) is {shellArt.GetSize()}px, "
                    + $"but the room's own declared size is {expectedShellSize}px.")
                .IsEqual(expectedShellSize);
        }
    }

    // ── shared assertion ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The one assertion every census entry above funnels through, so every failure message says
    /// the same thing in the same shape: the offending id, why it matters, and — the whole point of
    /// U3's other half (<see cref="TownAssets2D"/>'s loud placeholder) — what a person actually sees
    /// when this fails (a magenta-bordered box with the id stamped on it, not a crash, not a blank
    /// hole, and not the plausible-looking wrong art #316 slipped through as).
    /// </summary>
    private static void AssertResolves(string id, string why)
    {
        if (KnownPendingIds.Contains(id))
        {
            return;
        }

        AssertThat(IconRegistry.Art(id))
            .OverrideFailureMessage(
                $"census: '{id}' does not resolve to committed art. {why} On screen, that means a "
                + "loud magenta-bordered placeholder box with this id stamped on it (TownAssets2D's "
                + "placeholder builder, U3) — never a crash, but never something this repo ships "
                + "either.")
            .IsNotNull();
    }
}
#endif
