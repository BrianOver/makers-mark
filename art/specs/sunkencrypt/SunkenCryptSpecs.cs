using System.Collections.Immutable;

namespace GameArt.Specs.SunkenCrypt;

/// <summary>
/// The Sunken Crypt venue art set — the render half of wave-C row C7 (venue art for the C2 sim venue
/// <c>The Sunken Crypt</c>, flooded catacombs under the old chapel). One file, one owner; the reflection
/// registry (<see cref="AssetRegistry"/>) discovers it by presence via the <c>art/specs/**</c> glob, so
/// this is a pure new-file add-on with NO edit to the GameArt project or a shared registration line.
///
/// <para>All <c>crypt</c> palette family (variety-tone §2: bone/parchment + cold-cyan accent, the
/// death-adjacent anchor). A <see cref="AssetKind.Backdrop"/> scene, a <see cref="AssetKind.Building"/>
/// entrance, the five floor monsters (F1 Crypt Crab → F5 The Undertow, mirroring the C2
/// <c>VenueFloor.MonsterKind</c> roster), and one offering <see cref="AssetKind.Prop"/>. Lit world assets
/// (entrance, monsters, prop) carry a normal map for the 2.5D Light2D path; the flat backdrop does not.
/// The mascot-charm rule (Cult-of-the-Lamb citation): rounded shapes + big eyes on grim things — carried
/// hardest by the skull-wearing Crypt Crab. Rendered by name via <c>IconRegistry.Art("&lt;Id&gt;")</c>
/// (null-tolerant), so this describe-PR merges green before any pixel exists.</para>
/// </summary>
public sealed class SunkenCryptSpecs : IAssetModule
{
    public ImmutableArray<AssetSpec> Specs { get; } =
    [
        // ---- Venue scene + gateway -----------------------------------------------------------
        new AssetSpec(
            Id: "sunkencrypt-backdrop",
            Module: "sunkencrypt",
            Track: ArtTrack.Active,
            Kind: AssetKind.Backdrop,
            Subject: "a flooded catacomb interior beneath an old chapel, submerged stone archways and "
                + "toppled sarcophagi, still black water mirroring vaulted funerary crypts, drowned "
                + "pillars receding into gloom, atmospheric wide backdrop scene"),
        new AssetSpec(
            Id: "sunkencrypt-entrance",
            Module: "sunkencrypt",
            Track: ArtTrack.Active,
            Kind: AssetKind.Building,
            Subject: "a sunken crypt entrance, a half-drowned chapel crypt doorway carved into old stone, "
                + "water spilling down worn steps into the dark below, a broken iron gate hanging ajar, "
                + "weathered funerary statues flanking the arch",
            NormalMap: true),

        // ---- Floor monsters F1-F5 (map to C2 VenueFloor.MonsterKind) --------------------------
        new AssetSpec(
            Id: "sunkencrypt-crypt-crab",
            Module: "sunkencrypt",
            Track: ArtTrack.Active,
            Kind: AssetKind.Monster,
            Subject: "a small round crypt crab wearing an oversized borrowed human skull as its shell, "
                + "big expressive eyes peering shyly out of the eye sockets, stubby claws held together "
                + "self-consciously, soft rounded friendly shape, full body, clear readable silhouette",
            NormalMap: true),
        new AssetSpec(
            Id: "sunkencrypt-bog-wight",
            Module: "sunkencrypt",
            Track: ArtTrack.Active,
            Kind: AssetKind.Monster,
            Subject: "a waterlogged bog-wight, tattered grave-shroud draped over a gaunt swollen figure, "
                + "drowned pallid skin, dim lantern-glow eyes, reeds and silt clinging to its limbs, "
                + "full body, clear readable silhouette",
            NormalMap: true),
        new AssetSpec(
            Id: "sunkencrypt-choir-of-teeth",
            Module: "sunkencrypt",
            Track: ArtTrack.Active,
            Kind: AssetKind.Monster,
            Subject: "a floating choir of teeth, a hovering cluster of disembodied jawbones and grinning "
                + "tooth-rows arranged like a chorus opened in silent song, wispy ghostly tendrils "
                + "trailing below, full body, clear readable silhouette",
            NormalMap: true),
        new AssetSpec(
            Id: "sunkencrypt-reliquary-mimic",
            Module: "sunkencrypt",
            Track: ArtTrack.Active,
            Kind: AssetKind.Monster,
            Subject: "a reliquary mimic disguised as an ornate collection chest, hinged lid gaping into a "
                + "toothy maw, votive coins and offerings spilling from its mouth, stubby clawed feet, "
                + "big innocent eyes above the lid, full body, clear readable silhouette",
            NormalMap: true),
        new AssetSpec(
            Id: "sunkencrypt-undertow",
            Module: "sunkencrypt",
            Track: ArtTrack.Active,
            Kind: AssetKind.Monster,
            Subject: "The Undertow, a towering drowned leviathan of coiling black water and bound "
                + "skeletons, a churning vortex maw at its core, trailing chains and grave-silt, looming "
                + "imposing boss form, full body, clear readable silhouette",
            NormalMap: true),

        // ---- Offering prop -------------------------------------------------------------------
        new AssetSpec(
            Id: "sunkencrypt-donation-plate",
            Module: "sunkencrypt",
            Track: ArtTrack.Active,
            Kind: AssetKind.Prop,
            Subject: "a tarnished bronze donation plate on a slender stand, a few votive coins resting on "
                + "it, worn engraved rim, a small chapel offering dish",
            NormalMap: true),
    ];
}
