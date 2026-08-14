namespace GameArt;

/// <summary>
/// The frozen per-track generation profile: the master prompt/negative and the default + legal-range
/// SDXL settings for a track. This is the SINGLE home of the master prompt — asset-style-spec.md and
/// style-bible.md describe it in prose but the authoritative string lives here, so it can't drift
/// across four files.
/// </summary>
/// <param name="Track">The track this profile governs.</param>
/// <param name="MasterPrompt">Positive prefix prepended to every spec's <see cref="AssetSpec.Subject"/>.</param>
/// <param name="MasterNegative">Negative applied to every spec on this track (spec negatives are additive).</param>
/// <param name="Width">Default width.</param>
/// <param name="Height">Default height.</param>
/// <param name="Steps">Default sampling steps.</param>
/// <param name="CfgMilli">Default CFG in per-mille (6500 = 6.5).</param>
/// <param name="SamplerId">Default sampler.</param>
/// <param name="SchedulerId">Default scheduler.</param>
/// <param name="MinSteps">Lowest legal step override.</param>
/// <param name="MaxSteps">Highest legal step override.</param>
/// <param name="MinCfgMilli">Lowest legal CFG override (per-mille).</param>
/// <param name="MaxCfgMilli">Highest legal CFG override (per-mille).</param>
public sealed record ArtTrackProfile(
    ArtTrack Track,
    string MasterPrompt,
    string MasterNegative,
    int Width,
    int Height,
    int Steps,
    int CfgMilli,
    string SamplerId,
    string SchedulerId,
    int MinSteps,
    int MaxSteps,
    int MinCfgMilli,
    int MaxCfgMilli);

/// <summary>The two locked track profiles + prompt composition. See docs/design/asset-style-spec.md.</summary>
public static class ArtTrackProfiles
{
    /// <summary>Gameplay/moving assets: clean, cutout-ready, on-palette. The production workhorse.</summary>
    public static readonly ArtTrackProfile Active = new(
        Track: ArtTrack.Active,
        MasterPrompt:
            // Palette text lives in PaletteRegistry (variety-tone §2) — ComposePrompt splices the
            // spec's family clause here. Geometry/lighting text only.
            "crisp clean stylized game asset, single subject, one structure centered, 3/4 isometric view, "
            + "hand-painted diffuse texture, clear readable silhouette, dark fantasy, low-key moody lighting, "
            + "plain dark neutral background",
        MasterNegative:
            // "bright, cheerful" removed (tone directive 2026-07-18) — warmth is now a legal register.
            "text, letters, logo, words, title, caption, signature, watermark, multiple buildings, sprite sheet, "
            + "tiled, duplicated, photo, photorealistic, 3d render, blurry, low quality, ui, hud, frame, border, "
            + "oversaturated, neon, flat lighting, people, snow, trees, forest background",
        Width: 1024,
        Height: 1024,
        Steps: 28,
        CfgMilli: 6500,
        SamplerId: "dpmpp_2m",
        SchedulerId: "karras",
        MinSteps: 20,
        MaxSteps: 40,
        MinCfgMilli: 4000,
        MaxCfgMilli: 9000);

    /// <summary>Cutscenes/static/key art: soft oil chiaroscuro, atmospheric. Not sprite-clean.</summary>
    public static readonly ArtTrackProfile Painterly = new(
        Track: ArtTrack.Painterly,
        MasterPrompt:
            // Palette text spliced from PaletteRegistry per spec (variety-tone §2).
            "dark fantasy concept art, loose painterly brushwork, dramatic chiaroscuro, oil-painting texture, "
            + "moody atmospheric",
        MasterNegative:
            "photo, photorealistic, 3d render, blurry, low quality, text, watermark, signature, ui, hud, frame, "
            + "border, oversaturated, neon, cartoon, cel shaded, flat lighting",
        Width: 1024,
        Height: 1024,
        Steps: 32,
        CfgMilli: 6500,
        SamplerId: "dpmpp_2m",
        SchedulerId: "karras",
        MinSteps: 24,
        MaxSteps: 50,
        MinCfgMilli: 4000,
        MaxCfgMilli: 9000);

    /// <summary>The profile for a track.</summary>
    public static ArtTrackProfile For(ArtTrack track) => track switch
    {
        ArtTrack.Active => Active,
        ArtTrack.Painterly => Painterly,
        _ => throw new ArgumentOutOfRangeException(nameof(track), track, "Unknown art track"),
    };

    /// <summary>
    /// §11.10 U1 (KTD-A): the positive clause an <see cref="AssetKind.Item"/> spec composes in
    /// place of the master prompt's architecture wording.
    ///
    /// <para>The Active master prompt was authored for BUILDINGS — "one structure centered",
    /// negating "multiple buildings" — and all 48 committed item icons inherit it. *Structure* is
    /// an architecture word, and SDXL reads it as one: an unattended batch on 2026-08-14 returned
    /// a cake stand and a lidded urn for a buckler and a full armoured figure for a hauberk, over
    /// per-item Subject strings that were specific and correct. This names what an item icon
    /// actually is, so the subject stops fighting its own prefix.</para>
    /// </summary>
    public const string ItemClause =
        "a single game item icon, one hand-held object centered on empty space, "
        + "product shot of the object alone, nothing holding it, nothing under it";

    /// <summary>
    /// The negatives an item spec adds. Every entry is a shape a REAL candidate came back as in
    /// the 2026-08-14 batch, not a generic exclusion list — furniture and vessels because the
    /// architecture prefix pulled that way, and the worn/held terms because armour drifted to
    /// "on a figure" (which survives a BiRefNet cutout as a wrong silhouette rather than as a
    /// background, and is therefore worse than a bad background).
    /// </summary>
    public const string ItemNegative =
        "furniture, table, side table, cake stand, vase, urn, jar, bowl, pot, pottery, "
        + "candlestick, lamp, pedestal, plinth, display stand, base plate, "
        + "worn by a character, mannequin, armor stand, held by a hand, figure, person";

    /// <summary>
    /// The kind's own clause, or empty for kinds the master prompt already describes correctly.
    /// Deliberately a switch over <see cref="AssetKind"/> rather than a dictionary: a new kind
    /// added to the enum composes the unchanged master prompt by default, which is the safe
    /// direction — silence, not a wrong clause.
    /// </summary>
    private static string KindClause(AssetKind kind) => kind switch
    {
        AssetKind.Item => ItemClause,
        _ => string.Empty,
    };

    /// <summary>The kind's own negatives, paired with <see cref="KindClause"/>. Kept as its own
    /// switch rather than reusing <see cref="ItemNegative"/> for "any kind with a clause": the two
    /// are only equivalent while Item is the sole clause-bearing kind, and a future kind would
    /// otherwise silently inherit furniture negatives that have nothing to do with it.</summary>
    private static string KindNegative(AssetKind kind) => kind switch
    {
        AssetKind.Item => ItemNegative,
        _ => string.Empty,
    };

    /// <summary>
    /// §11.10 U1: the master prompt's building-specific phrases, removed for kinds that supply
    /// their own clause. Only ever applied when <see cref="KindClause"/> is non-empty, so every
    /// other kind's composed string is byte-identical to what it composed before this existed —
    /// pinned by <c>ArtTrackProfileTests.EveryNonItemKind_ComposesTheUnchangedPreClauseString</c>,
    /// because the alternative (editing the shared master prompt) would silently re-mean roughly
    /// 300 committed assets with nothing in git to trace it to.
    /// </summary>
    private const string ArchitecturePhrase = "one structure centered, ";

    private const string ArchitectureNegativePhrase = "multiple buildings, ";

    /// <summary>The full positive prompt for a spec: track master + the spec's palette-family clause
    /// (variety-tone §2 — <c>house</c> reproduces the pre-family prompt byte-for-byte) + subject.
    /// A spec whose <see cref="AssetSpec.Kind"/> supplies a <see cref="KindClause"/> swaps the
    /// master's architecture phrase for it; every other kind composes exactly as it always did.</summary>
    public static string ComposePrompt(AssetSpec spec)
    {
        var profile = For(spec.Track);
        var palette = PaletteRegistry.Require(spec.PaletteId).Clause;
        var extra = string.IsNullOrWhiteSpace(spec.PromptExtra) ? string.Empty : ", " + spec.PromptExtra.Trim();

        var master = profile.MasterPrompt;
        var kindClause = KindClause(spec.Kind);
        if (kindClause.Length > 0)
        {
            master = master.Replace(ArchitecturePhrase, string.Empty, StringComparison.Ordinal);
            master = $"{master}, {kindClause}";
        }

        return $"{master}, {palette}, {spec.Subject.Trim()}{extra}";
    }

    /// <summary>The full negative for a spec: the track master negative (+ the kind's own negatives
    /// when it has a clause, + optional additive extra).</summary>
    public static string ComposeNegative(AssetSpec spec)
    {
        var profile = For(spec.Track);
        var extra = string.IsNullOrWhiteSpace(spec.NegativeExtra) ? string.Empty : ", " + spec.NegativeExtra.Trim();

        var master = profile.MasterNegative;
        var kindNegative = KindNegative(spec.Kind);
        if (kindNegative.Length > 0)
        {
            master = master.Replace(ArchitectureNegativePhrase, string.Empty, StringComparison.Ordinal);
            master = $"{master}, {kindNegative}";
        }

        return $"{master}{extra}";
    }
}
