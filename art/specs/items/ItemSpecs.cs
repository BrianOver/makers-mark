using System.Collections.Immutable;

namespace GameArt.Specs.Items;

/// <summary>
/// The craftable-item icon set — inventory/shop icons for the recipes the four registered professions
/// forge (blacksmith + the tanning/engineering/alchemy add-on packs). One file, one owner; a pure new-file
/// add-on the reflection registry discovers by presence, no edit to the GameArt project. All
/// <c>Active</c>-track, <c>Item</c>-kind icons at 512×512 (small square UI icons, not world sprites) with
/// NO normal map — icons render flat in menus, not under Light2D like the town/hero world sprites.
/// Every id maps to a REAL recipe key from <c>ProfessionRegistry.AllRecipes</c> (<c>item-&lt;recipeId&gt;</c>);
/// subjects name the recipe's baseline material so the metal/tier reads true. A representative spread —
/// all four professions, all five slots (weapon/shield/armor/consumable/trinket), tiers 1–3 — is authored
/// here; the visual-sibling recipes in each slot family (same material + form) are wave-2 deferrals on the
/// identical pattern. Rendered by name via <c>IconRegistry.Art("&lt;Id&gt;")</c>.
/// </summary>
public sealed class ItemSpecs : IAssetModule
{
    private const int IconSize = 512;

    public ImmutableArray<AssetSpec> Specs { get; } =
    [
        // ---- Blacksmith — weapons (T1/T2/T3, incl. two-handed) ----------------------------------
        new AssetSpec(
            Id: "item-dagger",
            Module: "items",
            Track: ArtTrack.Active,
            Kind: AssetKind.Item,
            Subject: "a single copper dagger, short tapered blade, leather-wrapped grip",
            Width: IconSize, Height: IconSize),
        new AssetSpec(
            Id: "item-longsword",
            Module: "items",
            Track: ArtTrack.Active,
            Kind: AssetKind.Item,
            Subject: "a single iron longsword, straight cruciform blade, wrapped hilt and pommel",
            Width: IconSize, Height: IconSize),
        new AssetSpec(
            Id: "item-greatsword",
            Module: "items",
            Track: ArtTrack.Active,
            Kind: AssetKind.Item,
            Subject: "a massive two-handed steel greatsword, long broad blade, heavy crossguard",
            Width: IconSize, Height: IconSize),

        // ---- Blacksmith — shields (T1/T2 heavy/T3) ----------------------------------------------
        new AssetSpec(
            Id: "item-round-shield",
            Module: "items",
            Track: ArtTrack.Active,
            Kind: AssetKind.Item,
            Subject: "a single round wooden shield, copper rim and central boss, banded face",
            Width: IconSize, Height: IconSize),
        new AssetSpec(
            Id: "item-tower-shield",
            Module: "items",
            Track: ArtTrack.Active,
            Kind: AssetKind.Item,
            Subject: "a tall heavy iron tower shield, riveted vertical bands, angular top edge",
            Width: IconSize, Height: IconSize),
        new AssetSpec(
            Id: "item-bulwark",
            Module: "items",
            Track: ArtTrack.Active,
            Kind: AssetKind.Item,
            Subject: "a massive steel bulwark shield, thick reinforced plates, imposing broad face",
            Width: IconSize, Height: IconSize),

        // ---- Blacksmith — armor (T1 light/T2/T3 plate) ------------------------------------------
        new AssetSpec(
            Id: "item-chain-vest",
            Module: "items",
            Track: ArtTrack.Active,
            Kind: AssetKind.Item,
            Subject: "a light copper chainmail vest, fine riveted rings, sleeveless torso piece",
            Width: IconSize, Height: IconSize),
        new AssetSpec(
            Id: "item-hauberk",
            Module: "items",
            Track: ArtTrack.Active,
            Kind: AssetKind.Item,
            Subject: "an iron chainmail hauberk, long riveted rings, layered shoulders",
            Width: IconSize, Height: IconSize),
        new AssetSpec(
            Id: "item-full-plate",
            Module: "items",
            Track: ArtTrack.Active,
            Kind: AssetKind.Item,
            Subject: "a full suit of steel plate armor, articulated plates, knightly cuirass and pauldrons",
            Width: IconSize, Height: IconSize),

        // ---- Blacksmith — consumable ------------------------------------------------------------
        new AssetSpec(
            Id: "item-field-salve",
            Module: "items",
            Track: ArtTrack.Active,
            Kind: AssetKind.Item,
            Subject: "a small tin of healing field salve, cloth-wrapped body, waxed lid",
            Width: IconSize, Height: IconSize),

        // ---- Alchemy — potions, wearable, trinkets ----------------------------------------------
        new AssetSpec(
            Id: "item-alchemy-healing-draught",
            Module: "items",
            Track: ArtTrack.Active,
            Kind: AssetKind.Item,
            Subject: "a glass vial of red healing draught, cork stopper, softly glowing liquid",
            Width: IconSize, Height: IconSize),
        new AssetSpec(
            Id: "item-alchemy-panacea",
            Module: "items",
            Track: ArtTrack.Active,
            Kind: AssetKind.Item,
            Subject: "an ornate crystal flask of shimmering panacea, gold filigree, radiant cure-all elixir",
            Width: IconSize, Height: IconSize),
        new AssetSpec(
            Id: "item-alchemy-alchemical-robe",
            Module: "items",
            Track: ArtTrack.Active,
            Kind: AssetKind.Item,
            Subject: "an alchemist's robe, layered cloth, vial-lined belt, deep hood",
            Width: IconSize, Height: IconSize),
        new AssetSpec(
            Id: "item-alchemy-quicksilver-charm",
            Module: "items",
            Track: ArtTrack.Active,
            Kind: AssetKind.Item,
            Subject: "a small quicksilver charm pendant, suspended liquid-metal bead, silver chain",
            Width: IconSize, Height: IconSize),
        new AssetSpec(
            Id: "item-alchemy-philosophers-stone",
            Module: "items",
            Track: ArtTrack.Active,
            Kind: AssetKind.Item,
            Subject: "a faceted crimson philosopher's stone, glowing arcane gem, sharp facets",
            Width: IconSize, Height: IconSize),

        // ---- Tanning — leather line -------------------------------------------------------------
        new AssetSpec(
            Id: "item-tanning-hide-jerkin",
            Module: "items",
            Track: ArtTrack.Active,
            Kind: AssetKind.Item,
            Subject: "a rugged leather hide jerkin, stitched panels, buckled side straps",
            Width: IconSize, Height: IconSize),
        new AssetSpec(
            Id: "item-tanning-dragonhide-armor",
            Module: "items",
            Track: ArtTrack.Active,
            Kind: AssetKind.Item,
            Subject: "scaled dragonhide armor, overlapping hardened scales, ridged spine",
            Width: IconSize, Height: IconSize),
        new AssetSpec(
            Id: "item-tanning-hide-shield",
            Module: "items",
            Track: ArtTrack.Active,
            Kind: AssetKind.Item,
            Subject: "a round hide-covered wooden shield, stretched leather face, stitched rim",
            Width: IconSize, Height: IconSize),
        new AssetSpec(
            Id: "item-tanning-field-poultice",
            Module: "items",
            Track: ArtTrack.Active,
            Kind: AssetKind.Item,
            Subject: "a bundled herbal field poultice, cloth wrap, twine tie",
            Width: IconSize, Height: IconSize),

        // ---- Engineering — mechanical line ------------------------------------------------------
        new AssetSpec(
            Id: "item-engineering-bolt-thrower",
            Module: "items",
            Track: ArtTrack.Active,
            Kind: AssetKind.Item,
            Subject: "a compact mechanical bolt-thrower crossbow, geared mechanism, loaded bolt",
            Width: IconSize, Height: IconSize),
        new AssetSpec(
            Id: "item-engineering-clockwork-glaive",
            Module: "items",
            Track: ArtTrack.Active,
            Kind: AssetKind.Item,
            Subject: "a clockwork glaive polearm, geared blade housing, brass fittings",
            Width: IconSize, Height: IconSize),
        new AssetSpec(
            Id: "item-engineering-exo-frame",
            Module: "items",
            Track: ArtTrack.Active,
            Kind: AssetKind.Item,
            Subject: "a powered exo-frame armor rig, articulated struts, pistons and steel plating",
            Width: IconSize, Height: IconSize),
        new AssetSpec(
            Id: "item-engineering-targeting-monocle",
            Module: "items",
            Track: ArtTrack.Active,
            Kind: AssetKind.Item,
            Subject: "a brass targeting monocle, stacked geared lens rings, adjustment dials",
            Width: IconSize, Height: IconSize),
        new AssetSpec(
            Id: "item-engineering-field-repair-kit",
            Module: "items",
            Track: ArtTrack.Active,
            Kind: AssetKind.Item,
            Subject: "a compact field repair kit case, tools and spare gears, hinged lid",
            Width: IconSize, Height: IconSize),
    ];
}
