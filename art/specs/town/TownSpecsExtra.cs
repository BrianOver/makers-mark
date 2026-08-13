using System.Collections.Immutable;

namespace GameArt.Specs.Town;

/// <summary>
/// U13 (world-rework plan 2026-07-19-002) art wave. Originally declared seven specs for the
/// world rework's town promotion (a ground layer, the player-smith avatar, and three staged
/// interior backdrops); all seven were deleted in U9 (asset-completion wave, 2026-08-13).
///
/// <para><b>Why deleted, not just left describe-only.</b> None of the seven ever had art, and
/// none ever will: the ground layer and interior-backdrop concepts were superseded by the
/// differently-named <c>town2d-*</c> pixel-art set (ground tiles, atlas, and the per-venue
/// <c>town2d-*-interior-shell</c> rooms — see <see cref="GameArt.Specs.Town.TownSpecs"/>'s sibling
/// module and <c>godot/scripts/town2d/InteriorLayout2D.cs</c>), and <c>player-avatar</c>'s only
/// caller (<c>town.InteriorStage</c>) was deleted in the U4 painted-interiors plan, leaving it with
/// no live path to ever draw. <c>docs/design/ASSETS.md</c> §6 "Unbuilt declarations" is the
/// inventory that flagged all seven for disposition; this file's own U9 removal is that
/// disposition. The four LIVE building specs this module never touched — forge, market, tavern,
/// mine-gate exteriors — stay in <see cref="TownSpecs"/>, untouched by this change.</para>
///
/// <para>The seven removed ids, for anyone tracing history: <c>town-ground-plaza</c>,
/// <c>town-ground-plaza-worn</c>, <c>player-avatar</c>, <c>forge-interior</c>,
/// <c>tavern-interior</c>, <c>gate-interior</c>, <c>town-mine-strip</c>. None ever appeared in
/// <c>godot/assets/art/art-manifest.json</c>; <c>git log -- art/specs/town/TownSpecsExtra.cs</c> is
/// the record of what they were.</para>
/// </summary>
public sealed class TownSpecsExtra : IAssetModule
{
    public ImmutableArray<AssetSpec> Specs { get; } = [];
}
