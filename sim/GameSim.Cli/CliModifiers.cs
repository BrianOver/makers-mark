using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Crafting;

namespace GameSim.Cli;

/// <summary>
/// Phase C U-C1 slice 2: resolves player-typed craft-modifier tokens (short aliases OR full registry
/// ids) to a registered modifier id of the requested family, for the CLI `craft ... oil:/rune:/fit:`
/// composition syntax. Presentation-only — the sim never sees an alias, only the resolved id (which
/// <c>CraftingHandlers</c> re-validates against grade + material anyway).
/// </summary>
public static class CliModifiers
{
    private static readonly ImmutableDictionary<string, string> Aliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["coward"] = CraftModifiers.CowardsOil,
            ["cowards"] = CraftModifiers.CowardsOil,
            ["brave"] = CraftModifiers.BraveheartOil,
            ["braveheart"] = CraftModifiers.BraveheartOil,
            ["leech"] = CraftModifiers.LeechRune,
            ["lode"] = CraftModifiers.LodestoneFitting,
            ["lodestone"] = CraftModifiers.LodestoneFitting,
        }.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>Resolve a token to a registered modifier id of <paramref name="family"/>, or null if
    /// it names no modifier of that family (unknown alias, unknown id, or wrong family).</summary>
    public static string? Resolve(string token, ModifierFamily family)
    {
        var id = Aliases.TryGetValue(token, out var aliased) ? aliased : token;
        return CraftModifiers.IsFamily(id, family) ? id : null;
    }

    /// <summary>Human-readable listing for the `modifiers` CLI verb, one line per registered modifier.</summary>
    public static ImmutableList<string> ListLines()
    {
        var lines = ImmutableList.CreateBuilder<string>();
        lines.Add("  -- craft modifiers (compose at the forge: craft <recipe> <material> oil:<id> rune:<id> fit:<id>) --");
        foreach (var id in CraftModifiers.All)
        {
            if (CraftModifiers.Definition(id) is { } def)
            {
                var slot = def.Family switch
                {
                    ModifierFamily.QuenchOil => "oil",
                    ModifierFamily.Rune => "rune",
                    ModifierFamily.Fitting => "fit",
                    _ => "?",
                };
                lines.Add($"    {slot}:{id}  — {def.DisplayName}: {def.Tooltip}");
            }
        }

        lines.Add("    (grade decides how many take: Common 1 · Fine 2 · Superior/Masterwork 3; material caps tier)");
        return lines.ToImmutable();
    }
}
