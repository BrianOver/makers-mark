using System.Collections.Immutable;

namespace GameArt.Specs.Gloomwood;

/// <summary>
/// The Gloomwood venue art set — the describe-half of the art for the <c>gloomwood</c> venue (wave-C row
/// C6; sim venue is C1's <c>VenueRegistry</c> "gloomwood", a moonlit fungal forest, first non-purple
/// family). One file, one owner; a pure new-file add-on the reflection registry discovers by presence
/// (glob-compiled from <c>art/specs/</c>), no edit to the GameArt project or any shared registration line.
///
/// <para>All <c>PaletteId: "gloomwood"</c> (moss/lichen/verdigris family, <see cref="PaletteRegistry"/>).
/// The four floor monsters (Bramble Boar F1 → Lantern Moth F2 → The Wicker Shepherd F3 → Old Mossjaw F4
/// boss) follow the Cult-of-the-Lamb cute-over-grim rule from the direction doc: rounded shapes and big
/// expressive eyes are baked into each Subject so grim creatures still read charming. <c>Backdrop</c> and
/// <c>Prop</c> are real <see cref="AssetKind"/> values, so no Kind substitution was needed. Lit
/// foreground pieces (entrance, monsters, props) carry a normal map for the 2.5D Light2D path; the
/// far backdrop plane does not. Rendered by name via <c>IconRegistry.Art("&lt;Id&gt;")</c>.</para>
/// </summary>
public sealed class GloomwoodSpecs : IAssetModule
{
    public ImmutableArray<AssetSpec> Specs { get; } =
    [
        // --- Environment -----------------------------------------------------------------------
        new AssetSpec(
            Id: "gloomwood-backdrop",
            Module: "gloomwood",
            Track: ArtTrack.Active,
            Kind: AssetKind.Backdrop,
            Subject: "a moonlit fungal forest backdrop, towering mushroom canopies, drifting spore motes, "
                + "damp mossy loam floor, faint firefly glints in the mist, deep atmospheric depth",
            PaletteId: "gloomwood"),
        new AssetSpec(
            Id: "gloomwood-entrance",
            Module: "gloomwood",
            Track: ArtTrack.Active,
            Kind: AssetKind.Building,
            Subject: "a Gloomwood forest entrance archway, gnarled root-woven timber gate, lantern-lit path "
                + "into the fungal wood, moss and lichen creeping over old stone posts",
            PaletteId: "gloomwood",
            NormalMap: true),

        // --- Floor monsters (Cult-of-the-Lamb rule: rounded shapes + big eyes) ------------------
        new AssetSpec(
            Id: "gloomwood-bramble-boar",
            Module: "gloomwood",
            Track: ArtTrack.Active,
            Kind: AssetKind.Monster,
            Subject: "a single round chubby bramble boar, plump rounded body wrapped in thorny bramble vines, "
                + "big soft expressive eyes, tiny tusks, snuffling snout, endearingly gluttonous, "
                + "full body, clear readable silhouette",
            PaletteId: "gloomwood",
            NormalMap: true),
        new AssetSpec(
            Id: "gloomwood-lantern-moth",
            Module: "gloomwood",
            Track: ArtTrack.Active,
            Kind: AssetKind.Monster,
            Subject: "a single fluffy lantern moth, rounded velvety body, big gentle glowing eyes, "
                + "soft plush wings, a warm paper lantern clutched politely in its legs, "
                + "full body, clear readable silhouette",
            PaletteId: "gloomwood",
            NormalMap: true),
        new AssetSpec(
            Id: "gloomwood-wicker-shepherd",
            Module: "gloomwood",
            Track: ArtTrack.Active,
            Kind: AssetKind.Monster,
            Subject: "a single walking wicker scarecrow shepherd, rounded stitched burlap head with big "
                + "kind button eyes, woven straw body, a gentle herding crook, trailing moss and lantern, "
                + "full body, clear readable silhouette",
            PaletteId: "gloomwood",
            NormalMap: true),
        new AssetSpec(
            Id: "gloomwood-old-mossjaw",
            Module: "gloomwood",
            Track: ArtTrack.Active,
            Kind: AssetKind.Monster,
            Subject: "a single hulking Old Mossjaw venue boss, huge rounded moss-and-lichen-covered beast, "
                + "big slow soulful eyes, broad blunt jaw, mushrooms sprouting on its back, gentle giant menace, "
                + "full body, clear readable silhouette",
            PaletteId: "gloomwood",
            NormalMap: true),

        // --- Props -----------------------------------------------------------------------------
        new AssetSpec(
            Id: "gloomwood-mushroom-cluster",
            Module: "gloomwood",
            Track: ArtTrack.Active,
            Kind: AssetKind.Prop,
            Subject: "a glowing mushroom cluster prop, clustered bioluminescent fungi caps casting soft light, "
                + "mossy base, drifting spores, clean readable silhouette",
            PaletteId: "gloomwood",
            NormalMap: true),
        new AssetSpec(
            Id: "gloomwood-toll-booth",
            Module: "gloomwood",
            Track: ArtTrack.Active,
            Kind: AssetKind.Prop,
            Subject: "a Gloomwood Wardens toll booth prop, small mossy timber permit-office kiosk, "
                + "hanging Form-7 notice board, shuttered window, lantern on the counter, clean readable silhouette",
            PaletteId: "gloomwood",
            NormalMap: true),
    ];
}
