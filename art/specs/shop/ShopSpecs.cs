using System.Collections.Immutable;

namespace GameArt.Specs.Shop;

/// <summary>
/// The shop-stage backdrop set (LW-art parity, living-world plan 2026-07-19, unit LW3's art
/// dependency). One file, one owner; a pure new-file add-on the reflection registry discovers by
/// presence — no edit to the GameArt project or any shared registration line, mirrors
/// <see cref="GameArt.Specs.Mine.MineSpecs"/>'s shape.
///
/// <para>Just the shelf-wall strip LW3's <c>ShopStage</c> mounts at the top of <c>ShopPanel</c> (a
/// slim ~1024x220 lit strip, SubViewport pattern cloned from <c>LitTownOverlay</c>): a readable wall
/// of empty display shelving the customer-walk choreography plays out in front of. Unlike the venue
/// backdrops (<c>mine-backdrop</c>, <c>gloomwood-backdrop</c>) this is a near, lit, structured surface
/// — not a receding atmospheric plane — so it keeps the normal-map + BiRefNet-cutout chain the
/// buildings/props use rather than skipping it. <c>PaletteId: "hearth"</c> (warm honey-amber,
/// terracotta/aged-timber) — the shop is a lived-in warm-town space, not the house palette's
/// night/arcane anchor. Rendered by name via <c>IconRegistry.Art("shop-interior")</c>; LW3 ships
/// green with a themed-gradient fallback before this asset lands (same null-tolerant rule the
/// overlay already follows) and swaps to the lit art once committed.</para>
/// </summary>
public sealed class ShopSpecs : IAssetModule
{
    public ImmutableArray<AssetSpec> Specs { get; } =
    [
        new AssetSpec(
            Id: "shop-interior",
            Module: "shop",
            Track: ArtTrack.Active,
            Kind: AssetKind.Building,
            Subject: "a front-facing blacksmith shop interior wall, sturdy warm timber shelving lined "
                + "with rows of empty display slots, wood-paneled back wall, warm lantern-lit interior, "
                + "wide horizontal composition, flat front view not an isometric angle",
            PaletteId: "hearth",
            NormalMap: true,
            Width: 1024,
            Height: 224),
    ];
}
