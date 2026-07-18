using System.Collections.Immutable;

namespace GameArt.Specs.Items;

/// <summary>
/// Wave-2 of the craftable-item icon set — the visual-sibling recipes the first <see cref="ItemSpecs"/>
/// pass deferred, so that EVERY recipe across all four registered professions
/// (blacksmith + tanning/engineering/alchemy) now has an inventory/shop icon. A pure new-file add-on
/// the reflection registry discovers by presence (a second <see cref="IAssetModule"/> in the assembly),
/// so <see cref="ItemSpecs"/> — a different owner's file — is never touched. Same conventions as the
/// first pass: all <c>Active</c>-track, <c>Item</c>-kind icons at 512×512 with NO normal map (flat menu
/// icons, not Light2D world sprites). Every id is <c>item-&lt;recipeId&gt;</c> for a REAL recipe key in
/// <c>ProfessionRegistry.AllRecipes</c>, and each subject names the recipe's baseline material so the
/// metal/tier reads true. Palette split follows the variety-tone families (§2, 2026-07-18):
/// <c>house</c> for metal/mechanical/arcane gear, <c>hearth</c> for leather and warm consumables.
/// Rendered by name via <c>IconRegistry.Art("&lt;Id&gt;")</c>.
/// </summary>
public sealed class ItemSpecsExtra : IAssetModule
{
    private const int IconSize = 512;

    public ImmutableArray<AssetSpec> Specs { get; } =
    [
        // ---- Blacksmith — weapons (T1 copper / T2 iron two-handed) ------------------------------
        new AssetSpec(
            Id: "item-shortsword",
            Module: "items",
            Track: ArtTrack.Active,
            Kind: AssetKind.Item,
            Subject: "a single copper shortsword, broad tapered blade, leather-wrapped grip and disc pommel",
            PaletteId: "house",
            Width: IconSize, Height: IconSize),
        new AssetSpec(
            Id: "item-greataxe",
            Module: "items",
            Track: ArtTrack.Active,
            Kind: AssetKind.Item,
            Subject: "a massive two-handed iron greataxe, wide crescent blade, long hafted shaft",
            PaletteId: "house",
            Width: IconSize, Height: IconSize),

        // ---- Blacksmith — shields (T1 copper / T2 iron) -----------------------------------------
        new AssetSpec(
            Id: "item-buckler",
            Module: "items",
            Track: ArtTrack.Active,
            Kind: AssetKind.Item,
            Subject: "a small round copper buckler, domed central boss, riveted rim",
            PaletteId: "house",
            Width: IconSize, Height: IconSize),
        new AssetSpec(
            Id: "item-kite-shield",
            Module: "items",
            Track: ArtTrack.Active,
            Kind: AssetKind.Item,
            Subject: "a tall iron kite shield, tapered teardrop face, riveted vertical bands",
            PaletteId: "house",
            Width: IconSize, Height: IconSize),

        // ---- Blacksmith — armor (T1 copper / T2 iron heavy) -------------------------------------
        new AssetSpec(
            Id: "item-scale-mail",
            Module: "items",
            Track: ArtTrack.Active,
            Kind: AssetKind.Item,
            Subject: "a copper scale mail shirt, overlapping metal scales, sleeveless torso piece",
            PaletteId: "house",
            Width: IconSize, Height: IconSize),
        new AssetSpec(
            Id: "item-half-plate",
            Module: "items",
            Track: ArtTrack.Active,
            Kind: AssetKind.Item,
            Subject: "an iron half-plate cuirass, layered breastplate and tassets, riveted straps",
            PaletteId: "house",
            Width: IconSize, Height: IconSize),

        // ---- Tanning — leather line (warm hearth palette) ---------------------------------------
        new AssetSpec(
            Id: "item-tanning-leather-cap",
            Module: "items",
            Track: ArtTrack.Active,
            Kind: AssetKind.Item,
            Subject: "a simple stitched leather cap, riveted brim, buckled chin strap",
            PaletteId: "hearth",
            Width: IconSize, Height: IconSize),
        new AssetSpec(
            Id: "item-tanning-studded-leather",
            Module: "items",
            Track: ArtTrack.Active,
            Kind: AssetKind.Item,
            Subject: "a studded leather cuirass, iron studs over stitched hide panels, buckled straps",
            PaletteId: "hearth",
            Width: IconSize, Height: IconSize),
        new AssetSpec(
            Id: "item-tanning-leather-buckler",
            Module: "items",
            Track: ArtTrack.Active,
            Kind: AssetKind.Item,
            Subject: "a small round leather buckler, stretched hide over a wooden core, stitched rim",
            PaletteId: "hearth",
            Width: IconSize, Height: IconSize),

        // ---- Engineering — mechanical line (house palette) --------------------------------------
        new AssetSpec(
            Id: "item-engineering-deployable-bulwark",
            Module: "items",
            Track: ArtTrack.Active,
            Kind: AssetKind.Item,
            Subject: "a folding mechanical deployable bulwark, hinged copper panels, brace strut",
            PaletteId: "house",
            Width: IconSize, Height: IconSize),
        new AssetSpec(
            Id: "item-engineering-powered-vest",
            Module: "items",
            Track: ArtTrack.Active,
            Kind: AssetKind.Item,
            Subject: "a powered exo-vest, copper plating over a strut frame, small piston pods",
            PaletteId: "house",
            Width: IconSize, Height: IconSize),
        new AssetSpec(
            Id: "item-engineering-utility-multitool",
            Module: "items",
            Track: ArtTrack.Active,
            Kind: AssetKind.Item,
            Subject: "a compact brass utility multitool, folding gear-driven implements, hinged tools",
            PaletteId: "house",
            Width: IconSize, Height: IconSize),

        // ---- Alchemy — healing consumable ladder (warm hearth palette) --------------------------
        new AssetSpec(
            Id: "item-alchemy-minor-elixir",
            Module: "items",
            Track: ArtTrack.Active,
            Kind: AssetKind.Item,
            Subject: "a small glass vial of red minor healing elixir, cork stopper, faintly glowing liquid",
            PaletteId: "hearth",
            Width: IconSize, Height: IconSize),
        new AssetSpec(
            Id: "item-alchemy-transmuters-tonic",
            Module: "items",
            Track: ArtTrack.Active,
            Kind: AssetKind.Item,
            Subject: "a rounded flask of amber transmuter's tonic, wax-sealed cork, swirling restorative liquid",
            PaletteId: "hearth",
            Width: IconSize, Height: IconSize),
        new AssetSpec(
            Id: "item-alchemy-greater-elixir",
            Module: "items",
            Track: ArtTrack.Active,
            Kind: AssetKind.Item,
            Subject: "a large glass vial of crimson greater healing elixir, cork stopper, brightly glowing liquid",
            PaletteId: "hearth",
            Width: IconSize, Height: IconSize),
    ];
}
