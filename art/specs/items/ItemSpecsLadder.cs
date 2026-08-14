using System.Collections.Immutable;

namespace GameArt.Specs.Items;

/// <summary>
/// Wave-3 of the craftable-item icon set — the six FORWARD-LADDER recipes (plan 2026-08-10-003 L3/L4)
/// that landed in <c>RecipeTable</c> after <see cref="ItemSpecs"/> and <see cref="ItemSpecsExtra"/> were
/// authored, and so had no icon at all: they rendered as a generic slot glyph plus the recipe name. Not
/// broken; generic — and generic on exactly the six items a party earns by graduating a venue, which is
/// the moment the ladder is supposed to feel like a reward.
///
/// <para>A pure new-file add-on the reflection registry discovers by presence (a third
/// <see cref="IAssetModule"/> in the assembly), so neither sibling file — a different owner's — is
/// touched. Same conventions as both earlier passes: <c>Active</c> track, <c>Item</c> kind, 512×512, NO
/// normal map (flat menu icons, not Light2D world sprites), id = <c>item-&lt;recipeId&gt;</c> for a REAL
/// key in <c>RecipeTable.All</c>, subject naming the recipe's baseline material so the metal and tier
/// read true. <c>ItemIconCoverageTests</c> pins that mapping in both directions, so a seventh ladder
/// recipe cannot ship iconless the way these six did.</para>
///
/// <para><b>Palette follows the VENUE the material drops from</b>, not the item's slot — which is where
/// this file departs from <see cref="ItemSpecsExtra"/>'s metal/leather split. Greenheart, amberpitch and
/// moonresin are Gloomwood loot and take <c>gloomwood</c>; firebrick, slagiron and emberglass are
/// Emberfall loot and take <c>den</c>, the same family <c>EmberfallSpecs</c> already renders its whole
/// venue in. A hero looking at a Cinderforge Blade should see where it came from. The two draughts
/// therefore break the earlier waves' consumables-are-<c>hearth</c> habit on purpose: moonresin is a cold
/// silver-blue and would fight honey-amber, and emberglass belongs to the fire venue.</para>
/// </summary>
public sealed class ItemSpecsLadder : IAssetModule
{
    private const int IconSize = 512;

    /// <summary>
    /// The escalation <c>HeroSpecs.NoConceptSheet</c> already carries, widened for items. Measured, not
    /// guessed: a first candidate batch on the master negative alone returned 8 of 8 concept sheets —
    /// three-blade variation plates, inventory grids, ornate framed plaques, light parchment grounds —
    /// even though the master negative already says <c>sprite sheet</c>, <c>tiled</c>, <c>duplicated</c>,
    /// <c>frame</c> and <c>border</c>. SDXL reads a bare "a single longsword" as an invitation to draw a
    /// design study of longswords. The item-only additions beyond the hero string are the plural and
    /// ground terms — <c>pair</c>, <c>set of items</c>, <c>collection</c>, <c>inventory grid</c>,
    /// <c>white background</c>, <c>parchment</c> — because a light ground defeats the BiRefNet cutout as
    /// surely as a second sword defeats the silhouette. Additive only; removes no track negative.
    /// </summary>
    private const string SingleItemOnDark =
        "character sheet, turnaround, orthographic views, reference sheet, icon inset, "
        + "decorative border, ornate frame, multiple views, variations, pair, set of items, "
        + "collection, inventory grid, white background, light background, parchment, "
        + "comic book, cel shading, anime, vector art, flat cartoon colors, thick outline, "
        // Second measured round: armour drifted to "worn by a figure" and vessels to "standing on a
        // carved plinth". Both survive a cutout as a wrong silhouette rather than as a background,
        // which is worse — BiRefNet keeps the body and the base because they ARE the subject.
        + "mannequin, armor stand, worn by a character, display plinth, pedestal, base plate";

    public ImmutableArray<AssetSpec> Specs { get; } =
    [
        // ---- Rung 1 — Gloomwood ore (greenheart T8 / amberpitch T9 / moonresin T9) --------------
        new AssetSpec(
            Id: "item-gloomsteel-blade",
            Module: "items",
            Track: ArtTrack.Active,
            Kind: AssetKind.Item,
            Subject: "a single gloomsteel longsword, dark green-black blade with mossy verdigris veining, "
                + "greenheart-wood grip and lichen-tarnished crossguard",
            NegativeExtra: SingleItemOnDark,
            PaletteId: "gloomwood",
            Width: IconSize, Height: IconSize),
        new AssetSpec(
            Id: "item-wardenweave-mail",
            Module: "items",
            Track: ArtTrack.Active,
            Kind: AssetKind.Item,
            Subject: "a single empty sleeveless mail hauberk laid out alone, dark woven rings "
                + "lacquered with amber pitch, honey-gold resin sheen across the shoulders",
            NegativeExtra: SingleItemOnDark,
            PaletteId: "gloomwood",
            Width: IconSize, Height: IconSize),
        new AssetSpec(
            Id: "item-moonresin-draught",
            Module: "items",
            Track: ArtTrack.Active,
            Kind: AssetKind.Item,
            Subject: "a single tall corked glass phial standing alone, luminous pale silver-blue "
                + "moonresin liquid inside, waxed cork stopper bound with sinew cord",
            NegativeExtra: SingleItemOnDark,
            PaletteId: "gloomwood",
            Width: IconSize, Height: IconSize),

        // ---- Rung 2 — Emberfall ore (firebrick T12 / slagiron T13 / emberglass T14) -------------
        new AssetSpec(
            Id: "item-cinderforge-blade",
            Module: "items",
            Track: ArtTrack.Active,
            Kind: AssetKind.Item,
            Subject: "a single cinderforge greatblade, scorched dark iron blade with a glowing "
                + "ember-orange fuller, soot-blackened crossguard and wrapped hilt",
            NegativeExtra: SingleItemOnDark,
            PaletteId: "den",
            Width: IconSize, Height: IconSize),
        new AssetSpec(
            Id: "item-ashguild-plate",
            Module: "items",
            Track: ArtTrack.Active,
            Kind: AssetKind.Item,
            Subject: "an ashguild plate cuirass, heavy slag-iron breastplate, soot-blackened pitted "
                + "surface, riveted straps and layered tassets",
            NegativeExtra: SingleItemOnDark,
            PaletteId: "den",
            Width: IconSize, Height: IconSize),
        new AssetSpec(
            Id: "item-emberglass-draught",
            Module: "items",
            Track: ArtTrack.Active,
            Kind: AssetKind.Item,
            Subject: "a faceted emberglass flask of draught, molten orange glowing liquid behind "
                + "smoked glass, blackened iron collar and stopper",
            NegativeExtra: SingleItemOnDark,
            PaletteId: "den",
            Width: IconSize, Height: IconSize),
    ];
}
