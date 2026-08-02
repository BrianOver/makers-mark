using System.Collections.Immutable;

namespace GameArt.Specs.Emberfall;

/// <summary>
/// The Emberfall Foundry venue art set — task #80, the last thing blocking the venue from being
/// FLIPPED LIVE (a separate, not-yet-made balance decision; see <c>GameSim.Venues.VenueRegistry
/// .LiveRotation</c>'s own remarks). Sim venue is <c>GameSim.Venues.Emberfall.EmberfallFoundryVenue</c>
/// (abandoned dwarven smelting halls, Mine-peer gate ladder 0/15/35/60/100). One file, one owner; a
/// pure new-file add-on the reflection registry discovers by presence (glob-compiled from
/// <c>art/specs/</c>), exactly like <c>GloomwoodSpecs</c>/<c>SunkenCryptSpecs</c> — no edit to the
/// GameArt project or any shared registration line.
///
/// <para>All <c>PaletteId: "den"</c> (rust-red + charcoal + hot coal-orange, <see
/// cref="PaletteRegistry"/> — the warm register that sets Emberfall apart from the Mine/Gloomwood/
/// Crypt's purple-green-blue family, per <c>EmberfallFoundryVenue</c>'s own "warm register" remarks).
/// The five floor monsters (Cinder Imp F1 → Slag Hound F2 → The Bellows-Mad F3 → Molten Archivist F4
/// → The Undying Forge-Heart F5 boss) are the EXACT five <c>VenueFloor.MonsterKind</c> values
/// <c>EmberfallFoundryVenue.Build()</c> registers — never invented — following the same
/// Cult-of-the-Lamb cute-over-grim rule the direction doc set for Gloomwood/Sunken Crypt (rounded
/// shapes, big eyes, sympathetic flavor) and already baked into this venue's own doc comment
/// ("apologetic", "resents any withdrawal"). No entrance/props spec — task #80's scope is the
/// backdrop + five portraits only; an entrance is not required by any current render path
/// (<c>DepthsPanel</c> never mounts a dormant venue's tile, and <c>BestiaryPanel</c> only ever asks
/// for a monster portrait, never an entrance).</para>
///
/// <para>Diffuse+normal pairs are hand-authored Python pixel grids (<c>art/pipeline/
/// gen_emberfall_venue.py</c>), not SDXL/ComfyUI — see that script's own header for why. The
/// <see cref="AssetSpec.NormalMap"/> contract still matches the SDXL-family siblings exactly
/// (Backdrop = diffuse-only flat far plane; Monster = lit foreground figure, normal map required)
/// so <c>ArtWiringCoverageTests</c>' shape applies unchanged. Rendered by name via
/// <c>IconRegistry.Art("&lt;Id&gt;")</c> / <c>IconRegistry.Lit("&lt;Id&gt;")</c>, null-tolerant, so
/// this describe-half merges green before any pixel exists (same as every other venue module).</para>
/// </summary>
public sealed class EmberfallSpecs : IAssetModule
{
    public ImmutableArray<AssetSpec> Specs { get; } =
    [
        // --- Environment -------------------------------------------------------------------
        new AssetSpec(
            Id: "emberfall-backdrop",
            Module: "emberfall",
            Track: ArtTrack.Active,
            Kind: AssetKind.Backdrop,
            Subject: "an abandoned dwarven foundry hall backdrop, molten channels of glowing slag "
                + "threading the floor, dark furnace archways and cold anvils receding into ash-grey "
                + "gloom, drifting embers, a forge whose fires never fully went out",
            PaletteId: "den"),

        // --- Floor monsters (Cult-of-the-Lamb rule: rounded shapes + big eyes) --------------
        // Roster verbatim from GameSim.Venues.Emberfall.EmberfallFoundryVenue.Build()'s floor
        // switch (sim/GameSim/Venues/Emberfall/EmberfallFoundryVenue.cs) — the five monsters this
        // venue actually spawns, one per floor, never invented.
        new AssetSpec(
            Id: "emberfall-cinder-imp",
            Module: "emberfall",
            Track: ArtTrack.Active,
            Kind: AssetKind.Monster,
            Subject: "a single small round cinder imp, plump ash-grey body glowing at the seams with "
                + "hot coal-orange cracks, big soft apologetic eyes, clutching a stolen glowing coal "
                + "to its chest, stubby horns, full body, clear readable silhouette",
            PaletteId: "den",
            NormalMap: true),
        new AssetSpec(
            Id: "emberfall-slag-hound",
            Module: "emberfall",
            Track: ArtTrack.Active,
            Kind: AssetKind.Monster,
            Subject: "a single molten-slag hound, rounded charcoal body with dripping coal-orange "
                + "seams like cooling lava, big loyal watchful eyes, stubby glowing paws, guarding a "
                + "cooling channel, full body, clear readable silhouette",
            PaletteId: "den",
            NormalMap: true),
        new AssetSpec(
            Id: "emberfall-bellows-mad",
            Module: "emberfall",
            Track: ArtTrack.Active,
            Kind: AssetKind.Monster,
            Subject: "a single hulking forge-golem, blocky charcoal bellows-and-anvil body, a wide "
                + "glowing furnace-mouth grin that never closes, big obsessive round eyes, stubby "
                + "riveted arms, convinced the fires must never die, full body, clear readable "
                + "silhouette",
            PaletteId: "den",
            NormalMap: true),
        new AssetSpec(
            Id: "emberfall-molten-archivist",
            Module: "emberfall",
            Track: ArtTrack.Active,
            Kind: AssetKind.Monster,
            Subject: "a single robed archivist golem, scorched charcoal robe with hot coal-orange "
                + "trim, hugging a fireproof ledger possessively to its chest, big wary eyes peeking "
                + "from a low hood, resents any withdrawal, full body, clear readable silhouette",
            PaletteId: "den",
            NormalMap: true),
        new AssetSpec(
            Id: "emberfall-undying-forge-heart",
            Module: "emberfall",
            Track: ArtTrack.Active,
            Kind: AssetKind.Monster,
            Subject: "The Undying Forge-Heart, a towering furnace-core venue boss, a great rounded "
                + "iron-banded heart-shaped furnace veined with hot coal-orange cracks that never cool, "
                + "looming imposing boss form, full body, clear readable silhouette",
            PaletteId: "den",
            NormalMap: true),
    ];
}
