using System.Collections.Immutable;

namespace GameArt.Specs.Mine;

/// <summary>
/// The Mine's own venue art — the default/built-in venue (<c>VenueRegistry.MineId</c> = "mine", the
/// sim's one live venue). One file, one owner; a pure new-file add-on the reflection registry
/// discovers by presence (glob-compiled from <c>art/specs/</c>), no edit to the GameArt project or any
/// shared registration line — mirrors <see cref="GameArt.Specs.Gloomwood.GloomwoodSpecs"/>'s shape.
///
/// <para>Just the hub-tile backdrop today: <c>town-mine-gate</c> (<c>art/specs/town/TownSpecs.cs</c>)
/// already covers the lit foreground entrance, so this module fills the one gap flagged in the art
/// pipeline health record (<c>docs/design/art-pipeline-health-2026-07-18.md</c>) — the Mine's venue-map
/// hub tile (<c>DepthsPanel.BuildMineTile</c>, via <c>AssetCatalog.VenueBackdrop("mine")</c>) rendering
/// a themed fallback because no <c>mine-backdrop</c> spec existed. <c>PaletteId: "house"</c> — the
/// Mine is the house palette's own anchor (variety-tone direction §2: "Mine gate, mine F5, memorials,
/// arcane items, key art default, night-state building variants"; <c>town-mine-gate</c> stays
/// <c>house</c> too, per the same doc's edit list). Flat far plane, no normal map — same
/// <c>AssetKind.Backdrop</c> contract as <c>GloomwoodSpecs.GloomwoodBackdrop</c> and
/// <c>SunkenCryptSpecs</c>' backdrop (both skip the normal-map chain; the far backdrop plane never
/// carries one). Rendered by name via <c>IconRegistry.Art("mine-backdrop")</c>.</para>
/// </summary>
public sealed class MineSpecs : IAssetModule
{
    public ImmutableArray<AssetSpec> Specs { get; } =
    [
        new AssetSpec(
            Id: "mine-backdrop",
            Module: "mine",
            Track: ArtTrack.Active,
            Kind: AssetKind.Backdrop,
            Subject: "a deep mine tunnel backdrop, torchlit rough-hewn rock walls receding into shadow, "
                + "distant glinting ore veins, timber support beams, drifting dust motes, "
                + "deep atmospheric depth into the dark descending passage",
            PaletteId: "house"),
    ];
}
