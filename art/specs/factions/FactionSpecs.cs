using System.Collections.Immutable;

namespace GameArt.Specs.Factions;

/// <summary>
/// The town faction emblem set — heraldic crests for the two standing/tariff factions: the built-in
/// <c>FactionRegistry.Deepvein</c> miners' guild (R2) and the <c>CrownsguardFaction</c> royal armory
/// add-on. One file, one owner; a pure new-file add-on the reflection registry discovers by presence,
/// no edit to the GameArt project. Emblems are flat front-facing UI crests, so they use the
/// <c>Item</c> kind (icon framing — the closest fit; see the WISH note below) at 512×512 with NO normal
/// map, like the other UI icons. Ids (<c>faction-&lt;id&gt;-emblem</c>) map to the real faction registry keys.
/// Rendered by name via <c>IconRegistry.Art("&lt;Id&gt;")</c>.
///
/// WISH: <c>AssetKind</c> has no dedicated Emblem/Sigil/Crest value; a heraldic device is neither a
/// gameplay item nor a prop. <c>Item</c> is mapped here because it drives flat centered icon framing,
/// which is what a crest needs — but a first-class emblem kind would let the pipeline give crests a
/// bespoke framing default. Noted for a future contract amendment, not forced into the current enum.
/// </summary>
public sealed class FactionSpecs : IAssetModule
{
    private const int IconSize = 512;

    public ImmutableArray<AssetSpec> Specs { get; } =
    [
        new AssetSpec(
            Id: "faction-deepvein-emblem",
            Module: "factions",
            Track: ArtTrack.Active,
            Kind: AssetKind.Item,
            Subject: "the Deepvein Consortium heraldic emblem, crossed miner's pickaxes over a glowing ore vein, "
                + "engraved guild crest, flat front-facing, clean bold silhouette",
            Width: IconSize, Height: IconSize),
        new AssetSpec(
            Id: "faction-crownsguard-emblem",
            Module: "factions",
            Track: ArtTrack.Active,
            Kind: AssetKind.Item,
            Subject: "the Crownsguard Armory heraldic emblem, crowned anvil with electrum trim, "
                + "royal armory crest, flat front-facing, clean bold silhouette",
            Width: IconSize, Height: IconSize),
    ];
}
