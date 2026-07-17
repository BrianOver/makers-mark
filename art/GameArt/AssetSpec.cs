namespace GameArt;

/// <summary>The two locked visual tracks (see docs/design/asset-style-spec.md).</summary>
public enum ArtTrack
{
    /// <summary>Soft oil chiaroscuro, atmospheric — cutscenes, static art, key art. Not sprite-clean.</summary>
    Painterly,

    /// <summary>Clean cutout-ready sprite, on-palette (void-purple + ember) — in-game/gameplay assets.</summary>
    Active,
}

/// <summary>What the asset depicts. Drives framing defaults and which track is sensible.</summary>
public enum AssetKind
{
    Building,
    Prop,
    Sprite,
    ClassFigure,
    Portrait,
    Monster,
    Backdrop,
    Item,
}

/// <summary>
/// The request-half of an art asset (the art lane's data record — mirrors <c>FactionDefinition</c>
/// et al). Authored by a task/mod-Claude in its OWN <c>art/specs/&lt;module&gt;/&lt;Module&gt;Specs.cs</c>
/// file; validated by the pure fast-lane <c>AssetConformanceTests</c>; rendered by binding
/// <b>by name</b> (<c>IconRegistry.Art(Id)</c>, null-tolerant) so a describe-PR merges green before any
/// pixel exists. The build-half (resolved seed, model, sha256, uid, provenance) is written LATER, by
/// the single master art-Claude, to <c>art/build/&lt;Id&gt;.build.json</c> — a different file, different
/// owner, so the two writers never contend. See docs/design/art-pipeline-architecture.md.
///
/// <para>Pure constant data: no RNG, no wall-clock, no floats (<see cref="CfgMilli"/> is integer
/// per-mille — 6500 = cfg 6.5). The master prompt/negative are NOT stored here; they live once in
/// <see cref="ArtTrackProfiles"/>.</para>
/// </summary>
/// <param name="Id">Globally-unique, lowercase-kebab, module-prefixed (grammar enforced by
/// <see cref="AssetSpecRules"/>). Also the render binding name and the seed input.</param>
/// <param name="Module">Owner tag = the agent's claimed directory key (per-agent, not a semantic tier).</param>
/// <param name="Track">Which visual track (<see cref="ArtTrack"/>) this asset belongs to.</param>
/// <param name="Kind">What the asset depicts (<see cref="AssetKind"/>).</param>
/// <param name="Subject">The single varying subject phrase (the master prompt is prepended by the track profile).</param>
/// <param name="PromptExtra">Optional extra positive descriptors (material/light/view). Additive only.</param>
/// <param name="NegativeExtra">Optional extra negative descriptors. Additive only — cannot remove a track negative.</param>
/// <param name="PaletteId">Palette-clamp set id; defaults to the house palette.</param>
/// <param name="NeutralBaseTint">Generate neutral, tinted in-engine (e.g. class figures via ClassDefinition.ColorRgb).</param>
/// <param name="ClassId">For <see cref="AssetKind.ClassFigure"/> only — a plain hint string,
/// deliberately NOT resolved against the live ClassRegistry (keeps the art lane decoupled from sim class churn).</param>
/// <param name="NormalMap">True ⇒ a <c>_n</c> normal-map sibling is required when the asset is locked.</param>
/// <param name="Width">Optional width override (multiple of 8); null inherits the track profile.</param>
/// <param name="Height">Optional height override (multiple of 8); null inherits the track profile.</param>
/// <param name="Steps">Optional sampling-steps override; null inherits the track profile.</param>
/// <param name="CfgMilli">Optional CFG override in per-mille (6500 = 6.5), INTEGER; null inherits the profile.</param>
/// <param name="SamplerId">Optional sampler override; null inherits the profile.</param>
/// <param name="SchedulerId">Optional scheduler override; null inherits the profile.</param>
/// <param name="SpecVersion">The asset-style-spec revision this spec was written against; conformance rejects a stale value.</param>
public sealed record AssetSpec(
    string Id,
    string Module,
    ArtTrack Track,
    AssetKind Kind,
    string Subject,
    string PromptExtra = "",
    string NegativeExtra = "",
    string PaletteId = "house",
    bool NeutralBaseTint = false,
    string? ClassId = null,
    bool NormalMap = false,
    int? Width = null,
    int? Height = null,
    int? Steps = null,
    int? CfgMilli = null,
    string? SamplerId = null,
    string? SchedulerId = null,
    int SpecVersion = AssetSpec.CurrentSpecVersion)
{
    /// <summary>The current asset-style-spec revision. Bump when the contract's meaning changes.</summary>
    public const int CurrentSpecVersion = 1;
}
