using System.Collections.Immutable;

namespace GameArt.Specs.Town;

/// <summary>
/// U36 (§11, MAKERS-MARK.md "She has a body and a face", R27): Bryn the mentor's own banner
/// portrait — the first real <see cref="AssetKind.Portrait"/> spec this registry carries (the kind
/// existed in the enum, exercised only by <c>ArtTrackProfileTests</c>' synthetic fixtures, until
/// this one).
///
/// <para><b>Portrait, not <see cref="AssetKind.ClassFigure"/>, on purpose.</b>
/// <c>GameArt.Specs.Heroes.HeroSpecs</c>'s six class figures are generated with <see
/// cref="AssetSpec.NeutralBaseTint"/> set so one figure can be multiplied by any of six
/// <c>ClassDefinition.ColorRgb</c> values at runtime — that mechanism exists because a class
/// figure is worn by many interchangeable heroes. Bryn is one fixed, named person with no tint to
/// apply: a plain, fully-coloured portrait is the honest request.</para>
///
/// <para><b>Sibling to <see cref="TownSpecs"/>, not a class figure's twin.</b> One file, one owner,
/// same "new file under <c>art/specs/</c>, no shared edit" discipline the town module's own doc
/// describes — this module owns exactly the one spec U36 adds.</para>
///
/// <para><b>Sized at the exact draw size, not a big render a box happens to shrink.</b>
/// <c>godot/scripts/ui/MentorBanner.cs</c> requests her portrait at its own
/// <c>MentorPortraitSize</c> (56px square — deliberately smaller than the roster's
/// <c>UiKit.PortraitSize</c>; see that constant's own doc), and this spec's <see
/// cref="AssetSpec.Width"/>/<see cref="AssetSpec.Height"/> match it 1:1 — the same "resample to
/// the size it is actually drawn at, never a runtime scale" discipline
/// <c>docs/design/ASSETS.md</c> §5/§7 already established for the town props (11.87MB → 31KB),
/// applied here at the request stage instead of as a later offline fix.</para>
///
/// <para><b>Generation is GPU-gated and owed, not done here.</b> This describe-only spec is the
/// durable artifact (KTD3: the fast lane never touches pixels); the actual SDXL render — one job at
/// a time, ≥14GB VRAM free to start, abort above 14GB used or 83°C (the owner's standing GPU
/// safety floor) — is a separate step. Until it runs, <c>UiKit.ArtRect</c>'s existing null-tolerant
/// ladder draws a loud, captioned placeholder in the banner instead of silently showing nothing.
/// </para>
/// </summary>
public sealed class MentorSpecs : IAssetModule
{
    public ImmutableArray<AssetSpec> Specs { get; } =
    [
        new AssetSpec(
            Id: "mentor-bryn",
            Module: "town",
            Track: ArtTrack.Active,
            Kind: AssetKind.Portrait,
            Subject: "a weathered journeyman blacksmith woman, head-and-shoulders portrait, "
                + "soot-dusted leather apron over a plain forge-scorched tunic, hair tied back, "
                + "calm watchful expression, front-facing, clear readable silhouette",
            Width: 56,
            Height: 56),
    ];
}
