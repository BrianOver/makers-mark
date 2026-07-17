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
            "crisp clean stylized game asset, single subject, one structure centered, 3/4 isometric view, "
            + "hand-painted diffuse texture, clear readable silhouette, dark fantasy, low-key moody lighting, "
            + "deep desaturated void-purple shadows, iron-grey, warm ember-orange key glow, subtle arcane-violet "
            + "rim accents, muted somber palette, plain dark neutral background",
        MasterNegative:
            "text, letters, logo, words, title, caption, signature, watermark, multiple buildings, sprite sheet, "
            + "tiled, duplicated, photo, photorealistic, 3d render, blurry, low quality, ui, hud, frame, border, "
            + "oversaturated, neon, bright, cheerful, flat lighting, people, snow, trees, forest background",
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
            "dark fantasy concept art, loose painterly brushwork, dramatic chiaroscuro, oil-painting texture, "
            + "moody atmospheric, deep desaturated purples and iron greys with ember-orange glow, arcane-violet accents",
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

    /// <summary>The full positive prompt for a spec: the track master prompt + subject (+ optional extra).</summary>
    public static string ComposePrompt(AssetSpec spec)
    {
        var profile = For(spec.Track);
        var extra = string.IsNullOrWhiteSpace(spec.PromptExtra) ? string.Empty : ", " + spec.PromptExtra.Trim();
        return $"{profile.MasterPrompt}, {spec.Subject.Trim()}{extra}";
    }

    /// <summary>The full negative for a spec: the track master negative (+ optional additive extra).</summary>
    public static string ComposeNegative(AssetSpec spec)
    {
        var profile = For(spec.Track);
        var extra = string.IsNullOrWhiteSpace(spec.NegativeExtra) ? string.Empty : ", " + spec.NegativeExtra.Trim();
        return $"{profile.MasterNegative}{extra}";
    }
}
