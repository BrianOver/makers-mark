using System.Collections.Immutable;

namespace GameArt.Specs.Town;

/// <summary>
/// U13 (world-rework plan 2026-07-19-002) art wave — the assets the world rework's town promotion
/// consumes: a real ground surface (today's ground is an SVG tile, <c>godot/assets/sprites/ground_tile.svg</c>),
/// the player-smith avatar figure, and the staged-interior backdrops for the three venues that didn't
/// already have one (<c>shop-interior</c>, <c>art/specs/shop/ShopSpecs.cs</c>, pre-dates this wave). A
/// pure new-file add-on the reflection registry discovers by presence (a second <see cref="IAssetModule"/>
/// in the town directory, mirroring <see cref="GameArt.Specs.Items.ItemSpecsExtra"/>'s "second file, same
/// owner" shape) — <see cref="TownSpecs"/> (a different logical set, the original 4 buildings) is never
/// touched.
///
/// <para><b>SPECS ONLY — no image exists yet.</b> Every consumer (U14 ground layer, U20 avatar, U22
/// interiors) ships with a documented graceful degrade per the plan's risk mitigation (SVG ground /
/// tinted placeholder avatar / drawer fallback), so none of these ids block on art landing.</para>
///
/// <para><b>Kind choices.</b> <see cref="AssetKind"/> has no dedicated "ground"/"tile" value; the two
/// ground specs use <see cref="AssetKind.Prop"/> (closest fit — a static walkable-surface asset, not
/// architecture) rather than inventing a new enum member, the same kind-substitution acknowledgment
/// <see cref="GameArt.Specs.Gloomwood.GloomwoodSpecs"/> calls out explicitly. <c>player-avatar</c> uses
/// <see cref="AssetKind.ClassFigure"/> — same 512×768 full-body silhouette shape as
/// <see cref="GameArt.Specs.Heroes.HeroSpecs"/> ("hero-spec conventions") — but <see cref="AssetSpec.ClassId"/>
/// is deliberately left null: the blacksmith is the player, not a sim <c>ClassRegistry</c> entry, so there
/// is nothing to bind a hint string to (<see cref="AssetSpecRules"/> only forbids the reverse: a non-null
/// <c>ClassId</c> on a non-<c>ClassFigure</c> kind). <c>town-mine-strip</c> uses
/// <see cref="AssetKind.Backdrop"/>, same flat/no-normal contract as <c>mine-backdrop</c>
/// (<see cref="GameArt.Specs.Mine.MineSpecs"/>) — it is a re-authored, EXPLICITLY seamless-tiling sibling
/// of that asset (R16), so <c>MineWatch</c>/<c>ScryingMirror</c> can repeat it edge-to-edge without the
/// <c>FlipH</c> alternate-tile seam-hiding trick U6 needed for the non-tileable original.</para>
///
/// <para><b>Deferred scope (plan Scope Boundaries):</b> walk-cycle/directional animation is explicitly
/// OUT for <c>player-avatar</c> — <see cref="AssetSpec"/> has no frame/animation concept, so v1 glides/bobs
/// like the existing hero figures (KTD6/plan §Scope Boundaries); this is a single static full-body pose,
/// not a sprite sheet.</para>
/// </summary>
public sealed class TownSpecsExtra : IAssetModule
{
    /// <summary>Same neutral-base contract as <see cref="GameArt.Specs.Heroes.HeroSpecs"/> — Modulate
    /// multiplies in-engine, so a desaturated base is what lets a future tint (if the player avatar ever
    /// grows a cosmetic/profession recolor) read true without a re-generation.</summary>
    private const string NeutralBase =
        "desaturated pale bone-grey clothing, neutral monochrome figure, no colored accents";

    public ImmutableArray<AssetSpec> Specs { get; } =
    [
        // --- Ground (R1 — the ground layer today is an SVG tile, no painted ground exists) --------
        new AssetSpec(
            Id: "town-ground-plaza",
            Module: "town",
            Track: ArtTrack.Active,
            Kind: AssetKind.Prop,
            Subject: "a seamless tileable cobblestone plaza ground texture, top-down flat orthographic view, "
                + "worn irregular stone pavers, mortar seams, subtle weathering, edges tile edge-to-edge "
                + "with no visible seam when repeated",
            NormalMap: true),
        new AssetSpec(
            Id: "town-ground-plaza-worn",
            Module: "town",
            Track: ArtTrack.Active,
            Kind: AssetKind.Prop,
            Subject: "a seamless tileable cobblestone plaza ground texture variant, top-down flat orthographic "
                + "view, the same worn stone pavers with added moss patches and a hairline crack for tile "
                + "break-up variety, edges tile edge-to-edge with no visible seam when repeated",
            NormalMap: true),

        // --- Player avatar (R3 — hero-spec conventions: 512x768, neutral base, normal map) ---------
        new AssetSpec(
            Id: "player-avatar",
            Module: "town",
            Track: ArtTrack.Active,
            Kind: AssetKind.ClassFigure,
            Subject: "a single blacksmith figure standing, sturdy leather apron over simple work clothes, "
                + "rolled sleeves, a hammer at the belt, full body, front three-quarter view, "
                + "clear readable silhouette",
            PromptExtra: NeutralBase,
            NeutralBaseTint: true,
            NormalMap: true,
            Width: 512,
            Height: 768),

        // --- Staged interiors (R4 — forge/tavern/gate; shop-interior already exists, ShopSpecs.cs) -
        new AssetSpec(
            Id: "forge-interior",
            Module: "town",
            Track: ArtTrack.Active,
            Kind: AssetKind.Building,
            Subject: "a blacksmith forge interior, glowing ember-lit hearth and anvil station, "
                + "tool racks and quenching trough, stone walls warmed by firelight, "
                + "front-facing wide interior view",
            NormalMap: true),
        new AssetSpec(
            Id: "tavern-interior",
            Module: "town",
            Track: ArtTrack.Active,
            Kind: AssetKind.Building,
            Subject: "a cozy tavern interior, warm hearth fire, long wooden tables and benches, "
                + "a keeper's bar with hanging mugs, gossip-board on the wall, "
                + "front-facing wide interior view",
            PaletteId: "hearth",
            NormalMap: true),
        new AssetSpec(
            Id: "gate-interior",
            Module: "town",
            Track: ArtTrack.Active,
            Kind: AssetKind.Building,
            Subject: "the mine gate's inner threshold, heavy timber support beams, a muster yard just "
                + "inside the entrance, torches lighting the passage down into the dark, "
                + "front-facing wide interior view",
            NormalMap: true),

        // --- Ambient mine strip (R16 — seamless-tileable sibling of mine-backdrop, MineSpecs.cs) ---
        new AssetSpec(
            Id: "town-mine-strip",
            Module: "town",
            Track: ArtTrack.Active,
            Kind: AssetKind.Backdrop,
            Subject: "a torchlit mine tunnel wall strip, rough-hewn rock and timber support beams, "
                + "distant glinting ore veins, composed as a seamless horizontally-tileable band — "
                + "left and right edges match exactly for edge-to-edge repetition with no visible seam"),
    ];
}
