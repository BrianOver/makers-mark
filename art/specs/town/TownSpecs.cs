using System.Collections.Immutable;

namespace GameArt.Specs.Town;

/// <summary>
/// The town building set — the first fan-out-owned art module (the dogfood of the pipeline). One file,
/// one owner; adding another module is another file under <c>art/specs/</c> with no edit here or to the
/// GameArt project. All active-track (gameplay) buildings; each carries a normal map for the 2.5D
/// Light2D path. Rendered by name via <c>IconRegistry.Art("&lt;Id&gt;")</c>.
/// </summary>
public sealed class TownSpecs : IAssetModule
{
    public ImmutableArray<AssetSpec> Specs { get; } =
    [
        new AssetSpec(
            Id: "town-forge",
            Module: "town",
            Track: ArtTrack.Active,
            Kind: AssetKind.Building,
            Subject: "a blacksmith's forge building, stone-and-timber, glowing ember forge mouth, anvil out front",
            NormalMap: true),
        new AssetSpec(
            Id: "town-tavern",
            Module: "town",
            Track: ArtTrack.Active,
            Kind: AssetKind.Building,
            Subject: "a cozy tavern building, warm lit windows, hanging sign, timber frame and stone base",
            NormalMap: true),
        new AssetSpec(
            Id: "town-market",
            Module: "town",
            Track: ArtTrack.Active,
            Kind: AssetKind.Building,
            Subject: "a market stall building, cloth awning, crates and barrels of goods, wooden counter",
            NormalMap: true),
        new AssetSpec(
            Id: "town-mine-gate",
            Module: "town",
            Track: ArtTrack.Active,
            Kind: AssetKind.Building,
            Subject: "a mine entrance gate carved into rock, heavy timber supports, dark tunnel mouth, ore cart",
            NormalMap: true),
    ];
}
