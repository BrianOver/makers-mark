using System.Collections.Immutable;

namespace GameArt.Specs.Props;

/// <summary>
/// Warm-hub town props — the set dressing that reads the town as lived-in (variety-tone direction row C5).
/// All on the <c>hearth</c> palette family (warm honey-amber daylight, terracotta, aged timber). Each
/// carries a normal map for the 2.5D Light2D path. Two of these are the town's small joys — a napping
/// tavern cat and the forge pet salamander — authored as <see cref="AssetKind.Sprite"/> (loose animate
/// dressing, not fixed architecture); the rest are static <see cref="AssetKind.Prop"/> dressing.
/// Zero sim impact; rendered by name via <c>IconRegistry.Art("&lt;Id&gt;")</c> (null-tolerant, so this
/// describe-only module merges green before any pixel exists).
/// </summary>
public sealed class PropsSpecs : IAssetModule
{
    public ImmutableArray<AssetSpec> Specs { get; } =
    [
        new AssetSpec(
            Id: "props-noticeboard",
            Module: "props",
            Track: ArtTrack.Active,
            Kind: AssetKind.Prop,
            Subject: "a wooden town noticeboard on posts, tacked bounty parchments and scraps, weathered timber frame",
            PaletteId: "hearth",
            NormalMap: true),
        new AssetSpec(
            Id: "props-town-well",
            Module: "props",
            Track: ArtTrack.Active,
            Kind: AssetKind.Prop,
            Subject: "a round stone town well, mossy mortar, wooden roof and crank with a hanging bucket on a rope",
            PaletteId: "hearth",
            NormalMap: true),
        new AssetSpec(
            Id: "props-ore-cart",
            Module: "props",
            Track: ArtTrack.Active,
            Kind: AssetKind.Prop,
            Subject: "a wooden ore cart on iron-rimmed wheels, heaped with raw ore chunks, worn planks and fittings",
            PaletteId: "hearth",
            NormalMap: true),
        new AssetSpec(
            Id: "props-string-lanterns",
            Module: "props",
            Track: ArtTrack.Active,
            Kind: AssetKind.Prop,
            Subject: "a strung garland of paper lanterns between posts, warm glowing amber lights, festival dressing",
            PaletteId: "hearth",
            NormalMap: true),
        new AssetSpec(
            Id: "props-market-crates",
            Module: "props",
            Track: ArtTrack.Active,
            Kind: AssetKind.Prop,
            Subject: "a stack of wooden market crates and barrels, spilling produce and cloth-wrapped goods",
            PaletteId: "hearth",
            NormalMap: true),
        new AssetSpec(
            Id: "props-laundry-line",
            Module: "props",
            Track: ArtTrack.Active,
            Kind: AssetKind.Prop,
            Subject: "a laundry line strung between buildings, hung shirts and linens swaying, wooden clothespins",
            PaletteId: "hearth",
            NormalMap: true),
        new AssetSpec(
            Id: "props-tavern-cat",
            Module: "props",
            Track: ArtTrack.Active,
            Kind: AssetKind.Sprite,
            Subject: "a plump tabby tavern cat curled asleep, content and rounded, big soft ears, cozy",
            PaletteId: "hearth",
            NormalMap: true),
        new AssetSpec(
            Id: "props-forge-salamander",
            Module: "props",
            Track: ArtTrack.Active,
            Kind: AssetKind.Sprite,
            Subject: "a small friendly forge salamander pet, glowing ember-orange belly, big round eyes, curled by warm coals",
            PaletteId: "hearth",
            NormalMap: true),
    ];
}
