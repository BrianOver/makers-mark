using System.Collections.Immutable;

namespace GameArt.Specs.Monsters;

/// <summary>
/// The Mine monster set — the five floor bosses hero parties fight in <c>VenueRegistry.Mine</c>
/// (Cave Rat → Tunnel Spider → Deep Ghoul → Ore Golem → The Forgeworm, floors 1–5). One file, one owner;
/// a pure new-file add-on the reflection registry discovers by presence, no edit to the GameArt project.
/// All <c>Active</c>-track, <c>Monster</c>-kind world sprites; unlike the UI item icons these are lit
/// creatures standing in the Mine, so each carries a normal map for the 2.5D Light2D path (the same
/// treatment the town buildings and hero figures get) and inherits the track's 1024×1024 canvas. Ids
/// (<c>monster-&lt;kind&gt;</c>) map to the real <c>VenueFloor.MonsterKind</c> display names; subjects are a
/// single centered creature with a clean readable silhouette. Rendered by name via
/// <c>IconRegistry.Art("&lt;Id&gt;")</c>.
/// </summary>
public sealed class MonsterSpecs : IAssetModule
{
    public ImmutableArray<AssetSpec> Specs { get; } =
    [
        new AssetSpec(
            Id: "monster-cave-rat",
            Module: "monsters",
            Track: ArtTrack.Active,
            Kind: AssetKind.Monster,
            Subject: "a single mangy giant cave rat, matted fur, gleaming red eyes, bared teeth, "
                + "full body, clear readable silhouette",
            NormalMap: true),
        new AssetSpec(
            Id: "monster-tunnel-spider",
            Module: "monsters",
            Track: ArtTrack.Active,
            Kind: AssetKind.Monster,
            Subject: "a single large tunnel spider, bristled jointed legs, gleaming fangs, dark chitin carapace, "
                + "full body, clear readable silhouette",
            NormalMap: true),
        new AssetSpec(
            Id: "monster-deep-ghoul",
            Module: "monsters",
            Track: ArtTrack.Active,
            Kind: AssetKind.Monster,
            Subject: "a single gaunt deep ghoul, pallid stretched flesh, hollow eyes, long clawed hands, "
                + "full body, clear readable silhouette",
            NormalMap: true),
        new AssetSpec(
            Id: "monster-ore-golem",
            Module: "monsters",
            Track: ArtTrack.Active,
            Kind: AssetKind.Monster,
            Subject: "a single hulking ore golem, jagged rock body, glowing ore-veined limbs, molten core, "
                + "full body, clear readable silhouette",
            NormalMap: true),
        new AssetSpec(
            Id: "monster-forgeworm",
            Module: "monsters",
            Track: ArtTrack.Active,
            Kind: AssetKind.Monster,
            Subject: "a single colossal forgeworm, segmented armored body, molten gaping maw, glowing ember cracks, "
                + "full body, clear readable silhouette",
            NormalMap: true),
    ];
}
