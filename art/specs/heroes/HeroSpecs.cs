using System.Collections.Immutable;

namespace GameArt.Specs.Heroes;

/// <summary>
/// The hero class-figure set — the full-body class sprites the town renders side-by-side (V3). One file,
/// one owner; like the town module this is a pure new-file add-on the reflection registry discovers by
/// presence, with no edit to the GameArt project. All <c>Active</c>-track class figures at 512×768, each
/// carrying a normal map for the 2.5D Light2D path. Authored <b>neutral</b> (<c>NeutralBaseTint</c>): a
/// pale bone-grey base is generated once and MULTIPLIED in-engine by the class tint
/// (<c>ClassDefinition.ColorRgb</c>), so one figure reads true across every class colour. The
/// <c>ClassId</c> is a plain hint string — deliberately NOT resolved against the live ClassRegistry, so
/// sim class churn can never red the art lane. Rendered by name via <c>IconRegistry.SpriteFor(classId)</c>
/// (lit PNG <c>hero-&lt;classId&gt;</c> when present, hand-authored SVG fallback otherwise).
/// </summary>
public sealed class HeroSpecs : IAssetModule
{
    /// <summary>The neutral base every hero figure shares: Modulate multiplies, so a light desaturated
    /// base is what makes the per-class ColorRgb read true. Additive positive only.</summary>
    private const string NeutralBase =
        "desaturated pale bone-grey clothing and armor, neutral monochrome figure, no colored accents";

    /// <summary>Hardens against SDXL's character-sheet/turnaround/cel-shaded attractors that surfaced
    /// during LW-art parity curation (round-1 candidates for occultist/sentinel/skirmisher repeatedly
    /// drew multi-panel reference sheets or flat anime/comic linework instead of the single painterly
    /// portrait the three original hero figures locked in) — additive only, never removes a track
    /// negative.</summary>
    private const string NoConceptSheet =
        "character sheet, turnaround, orthographic views, reference sheet, icon inset, "
        + "decorative border, ornate frame, multiple views, comic book, cel shading, anime, "
        + "vector art, flat cartoon colors, thick outline";

    public ImmutableArray<AssetSpec> Specs { get; } =
    [
        new AssetSpec(
            Id: "hero-vanguard",
            Module: "heroes",
            Track: ArtTrack.Active,
            Kind: AssetKind.ClassFigure,
            Subject: "a single armored warrior figure standing, tower shield and blade, "
                + "full body, front three-quarter view, clear readable silhouette",
            PromptExtra: NeutralBase,
            NeutralBaseTint: true,
            ClassId: "vanguard",
            NormalMap: true,
            Width: 512,
            Height: 768),
        new AssetSpec(
            Id: "hero-striker",
            Module: "heroes",
            Track: ArtTrack.Active,
            Kind: AssetKind.ClassFigure,
            Subject: "a single lean duelist figure standing, twin daggers, poised stance, "
                + "full body, front three-quarter view, clear readable silhouette",
            PromptExtra: NeutralBase,
            NeutralBaseTint: true,
            ClassId: "striker",
            NormalMap: true,
            Width: 512,
            Height: 768),
        new AssetSpec(
            Id: "hero-mystic",
            Module: "heroes",
            Track: ArtTrack.Active,
            Kind: AssetKind.ClassFigure,
            Subject: "a single robed spellcaster figure standing, staff, hood, "
                + "full body, front three-quarter view, clear readable silhouette",
            PromptExtra: NeutralBase,
            NeutralBaseTint: true,
            ClassId: "mystic",
            NormalMap: true,
            Width: 512,
            Height: 768),
        // LW-art parity (living-world plan, 2026-07-19): the 3 sim classes shipped since the
        // original V3 set (Occultist/Sentinel/Skirmisher, sim/GameSim/Classes/*) had no lit figure —
        // AssetCatalog/TownScene fall back to the hand-authored SVG. Same neutral-base contract as
        // the three above; silhouettes deliberately diverge from their nearest sibling class
        // (Occultist vs Mystic: dagger+tome, not staff; Sentinel vs Vanguard: warhammer, bulkier
        // plate; Skirmisher vs Striker: hooked blades + cloak, not twin daggers) so tint alone
        // doesn't have to carry class identity on top of an identical pose.
        new AssetSpec(
            Id: "hero-occultist",
            Module: "heroes",
            Track: ArtTrack.Active,
            Kind: AssetKind.ClassFigure,
            Subject: "a single hooded dark occultist figure standing, curved ritual dagger, tome "
                + "hanging at hip, hollow shadowed hood, full body, front three-quarter view, "
                + "clear readable silhouette",
            PromptExtra: NeutralBase,
            NegativeExtra: NoConceptSheet,
            NeutralBaseTint: true,
            ClassId: "occultist",
            NormalMap: true,
            Width: 512,
            Height: 768),
        new AssetSpec(
            Id: "hero-sentinel",
            Module: "heroes",
            Track: ArtTrack.Active,
            Kind: AssetKind.ClassFigure,
            Subject: "a single bulky heavily-armored figure standing, large kite shield in one hand, "
                + "spiked warhammer in the other, thick layered plate armor, wide braced stance, "
                + "full body, front three-quarter view, clear readable silhouette",
            PromptExtra: NeutralBase,
            NegativeExtra: NoConceptSheet,
            NeutralBaseTint: true,
            ClassId: "sentinel",
            NormalMap: true,
            Width: 512,
            Height: 768),
        new AssetSpec(
            Id: "hero-skirmisher",
            Module: "heroes",
            Track: ArtTrack.Active,
            Kind: AssetKind.ClassFigure,
            Subject: "a single lightly-armored flanking rogue figure standing, dual curved blades "
                + "crossed at the ready, flowing hooded cloak, crouched dynamic stance, full body, "
                + "front three-quarter view, clear readable silhouette",
            PromptExtra: NeutralBase,
            NegativeExtra: NoConceptSheet,
            NeutralBaseTint: true,
            ClassId: "skirmisher",
            NormalMap: true,
            Width: 512,
            Height: 768),
    ];
}
