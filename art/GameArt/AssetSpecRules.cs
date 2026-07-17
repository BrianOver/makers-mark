using System.Text.RegularExpressions;

namespace GameArt;

/// <summary>
/// Pure structural validation of a single <see cref="AssetSpec"/> — the art analogue of the checks in
/// <c>FactionConformanceTests</c>, but factored OUT of the test so the conformance suite and the
/// extensibility proof (a test-only, never-registered spec) run through the SAME code path. No IO, no
/// Godot, no registry lookup. Returns the list of problems; empty = valid.
/// </summary>
public static class AssetSpecRules
{
    /// <summary>Lowercase-kebab id grammar (so "forge facade" vs "forge-facade" can't alias).</summary>
    public static readonly Regex IdGrammar = new("^[a-z][a-z0-9]*(-[a-z0-9]+)*$", RegexOptions.Compiled);

    /// <summary>Largest legal pixel dimension (SDXL latent-friendly cap).</summary>
    public const int MaxDimension = 4096;

    /// <summary>Validate one spec structurally. Empty list = valid.</summary>
    public static IReadOnlyList<string> Validate(AssetSpec spec)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(spec.Id) || !IdGrammar.IsMatch(spec.Id))
        {
            errors.Add($"Id '{spec.Id}' must be lowercase-kebab (^[a-z][a-z0-9]*(-[a-z0-9]+)*$)");
        }

        if (string.IsNullOrWhiteSpace(spec.Module))
        {
            errors.Add($"{spec.Id}: Module is blank");
        }

        if (string.IsNullOrWhiteSpace(spec.Subject))
        {
            errors.Add($"{spec.Id}: Subject is blank");
        }

        if (string.IsNullOrWhiteSpace(spec.PaletteId))
        {
            errors.Add($"{spec.Id}: PaletteId is blank");
        }

        if (spec.SpecVersion != AssetSpec.CurrentSpecVersion)
        {
            errors.Add($"{spec.Id}: SpecVersion {spec.SpecVersion} != current {AssetSpec.CurrentSpecVersion}");
        }

        // ClassId is only meaningful for a class figure (a plain hint — deliberately NOT resolved
        // against the live ClassRegistry, so class churn in the sim lane can never red the art lane).
        if (spec.ClassId is not null && spec.Kind != AssetKind.ClassFigure)
        {
            errors.Add($"{spec.Id}: ClassId set but Kind is {spec.Kind}, not ClassFigure");
        }

        // Overrides must sit inside the track's legal ranges (null inherits the profile default).
        var profile = ArtTrackProfiles.For(spec.Track);

        if (spec.Steps is int steps && (steps < profile.MinSteps || steps > profile.MaxSteps))
        {
            errors.Add($"{spec.Id}: Steps {steps} outside {spec.Track} range [{profile.MinSteps},{profile.MaxSteps}]");
        }

        if (spec.CfgMilli is int cfg && (cfg < profile.MinCfgMilli || cfg > profile.MaxCfgMilli))
        {
            errors.Add($"{spec.Id}: CfgMilli {cfg} outside {spec.Track} range [{profile.MinCfgMilli},{profile.MaxCfgMilli}]");
        }

        ValidateDimension(errors, spec.Id, "Width", spec.Width);
        ValidateDimension(errors, spec.Id, "Height", spec.Height);

        // The derived seed must be a valid positive generator seed (sanity of the hash for this id).
        if (AssetSeed.SeedFor(spec.Id) == 0)
        {
            errors.Add($"{spec.Id}: derived seed is zero");
        }

        return errors;
    }

    private static void ValidateDimension(List<string> errors, string id, string name, int? value)
    {
        if (value is int v && (v <= 0 || v > MaxDimension || v % 8 != 0))
        {
            errors.Add($"{id}: {name} {v} must be positive, <= {MaxDimension}, and a multiple of 8");
        }
    }
}
