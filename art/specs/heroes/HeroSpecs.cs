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
    ];
}
