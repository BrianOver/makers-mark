using System.Collections.Immutable;

namespace GameArt.Specs.Items;

/// <summary>
/// The rung-0 craftable-item icon: <c>item-mithril-warblade</c>, the one recipe P2-END-01 added to
/// <c>RecipeTable</c> (owner ruling 2026-09-06, "break the material gate"). It exists as its own
/// module file for the same reason <see cref="ItemSpecsLadder"/> does — a pure new-file add-on the
/// reflection registry discovers by presence, so neither sibling file, each a different owner's,
/// is touched.
///
/// <para><b>Palette follows the base metal set, not a venue.</b> <see cref="ItemSpecsLadder"/>
/// keys its six icons to the venue their ore drops from (<c>gloomwood</c>, <c>den</c>) because
/// those materials belong to a venue with its own colour identity. Mithril is MINE ore — the same
/// ladder as copper, iron and steel — so this icon takes the house palette exactly the way
/// <c>item-dagger</c>, <c>item-longsword</c> and <c>item-greatsword</c> do (they pass no
/// <c>PaletteId</c> at all). A Mithril Warblade should sit on the shelf as the fourth blade in
/// that family, not as a visitor from another wood.</para>
///
/// <para>Conventions otherwise identical to both earlier waves: <c>Active</c> track, <c>Item</c>
/// kind, 512x512, NO normal map (flat menu icon, never drawn under Light2D), id =
/// <c>item-&lt;recipeId&gt;</c> for a REAL key in <c>RecipeTable.All</c>, and a subject that names
/// the recipe's baseline material so the metal and the tier read true.
/// <c>ItemIconCoverageTests</c> pins that mapping in both directions.</para>
/// </summary>
public sealed class ItemSpecsMine : IAssetModule
{
    private const int IconSize = 512;

    /// <summary>
    /// The same measured escalation <see cref="ItemSpecsLadder"/> carries, and for the same reason:
    /// on the master negative alone a first item batch returned 8 of 8 concept sheets — variation
    /// plates, inventory grids, framed plaques on light grounds — because SDXL reads "a single
    /// longsword" as an invitation to draw a design study OF longswords. Restated here rather than
    /// shared from the sibling file so neither module has to be edited to change the other's
    /// prompt (the art lane's whole no-contention rule). Additive only; removes no track negative.
    /// </summary>
    private const string SingleItemOnDark =
        "character sheet, turnaround, orthographic views, reference sheet, icon inset, "
        + "decorative border, ornate frame, multiple views, variations, pair, set of items, "
        + "collection, inventory grid, white background, light background, parchment, "
        + "comic book, cel shading, anime, vector art, flat cartoon colors, thick outline, "
        + "mannequin, armor stand, worn by a character, display plinth, pedestal, base plate, "
        // Measured on this id's own first batch (16 candidates, 2026-09-06): every single-subject
        // survivor came back a DAGGER — a short curved knife on a light ground — because the item
        // track's own clause says "one hand-held object" and SDXL resolves an unqualified blade at
        // dagger scale. A Tier-4 warblade that reads as a knife beside item-greatsword is the
        // "resolves, but to the wrong thing" failure, so the length words are negated here rather
        // than merely asserted in the subject.
        + "dagger, knife, short blade, curved blade, scimitar, kukri, cleaver, "
        + "medallion, coin, amulet, locket, box, chest, gemstone, gem";

    public ImmutableArray<AssetSpec> Specs { get; } =
    [
        // ---- Rung 0 — Mine ore (mithril, grade 4 / Tier 4) --------------------------------------
        new AssetSpec(
            Id: "item-mithril-warblade",
            Module: "items",
            Track: ArtTrack.Active,
            Kind: AssetKind.Item,
            Subject: "a single long two-handed mithril warblade laid flat, very long straight "
                + "double-edged sword blade of pale silver-white metal with a cold blue sheen, blade "
                + "far longer than the hilt, slender fullered spine, dark leather-wrapped grip and "
                + "plain straight steel crossguard",
            NegativeExtra: SingleItemOnDark,
            Width: IconSize, Height: IconSize),
    ];
}
