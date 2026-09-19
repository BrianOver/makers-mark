using System.Collections.Generic;
using System.Linq;
using GameSim.Advisor;
using GameSim.Contracts;
using GameSim.Presentation;

namespace GameSim.Drama;

/// <summary>
/// P2-PEOPLE-06: the fallen's page — a pure, derived read of the three wake verbs so the render can
/// ask "what is still choosable" without re-deriving any legality itself. Never a new judgment: the
/// marker check reuses <see cref="ActionLegality.IsLegal"/> (the same guard chain
/// <see cref="FarewellHandlers"/> enforces), the remembrance default reuses
/// <see cref="PresentationScheduler.StakesFor"/> (the raid's own stakes ladder), and the heirloom
/// check reuses the exact provenance <see cref="ReforgeHeirloomAction"/> requires (worn by the
/// fallen, not already reforged). Pure, no RNG, no clock.
/// </summary>
public static class WakeQuery
{
    /// <summary>Player-crafted pieces that would legally become <paramref name="hero"/>'s grave
    /// marker right now — empty once a marker is set, or if nothing legal exists yet.</summary>
    public static IEnumerable<ItemId> MarkerCandidates(GameState state, HeroId hero)
    {
        if (!HasMemorial(state, hero, out var memorial) || memorial.MarkerItem is not null)
        {
            yield break;
        }

        foreach (var itemValue in state.Items.Keys)
        {
            var item = new ItemId(itemValue);
            if (ActionLegality.IsLegal(state, new PlaceGraveMarkerAction(hero, item), DayPhase.Evening))
            {
                yield return item;
            }
        }
    }

    /// <summary>True while the marker fact is still choosable: unset, and at least one legal piece exists.</summary>
    public static bool MarkerOpen(GameState state, HeroId hero) => MarkerCandidates(state, hero).Any();

    /// <summary>Every event that could stand as <paramref name="hero"/>'s remembrance, in log order —
    /// empty once a remembrance is chosen. The choices a wake offers (mirrors <see cref="RemembranceQuery.Naming"/>).</summary>
    public static IReadOnlyList<GameEvent> RemembranceChoices(GameState state, HeroId hero)
    {
        if (!HasMemorial(state, hero, out var memorial) || memorial.Remembrance is not null)
        {
            return System.Array.Empty<GameEvent>();
        }

        return RemembranceQuery.Naming(state, hero).ToList();
    }

    /// <summary>The default remembrance a wake would pre-select: the highest-stakes naming event by
    /// <see cref="PresentationScheduler.StakesFor"/>, ties broken by log order (stable sort preserves
    /// <see cref="RemembranceChoices"/>'s own order). Null once chosen or if nothing names the hero.</summary>
    public static GameEvent? DefaultRemembrance(GameState state, HeroId hero)
    {
        var choices = RemembranceChoices(state, hero);
        return choices.Count == 0 ? null : choices.OrderByDescending(PresentationScheduler.StakesFor).First();
    }

    /// <summary>True while the heirloom question is still open: a piece the fallen wore at death has
    /// not yet been reforged (the exact provenance <see cref="ReforgeHeirloomAction"/> requires).</summary>
    public static bool HeirloomOpen(GameState state, HeroId hero) =>
        state.EventLog.OfType<HeroDied>()
            .Where(died => died.Hero == hero)
            .SelectMany(died => WornItems(died.WornGear))
            .Any(item => !state.EventLog.OfType<HeirloomReforged>().Any(reforged => reforged.SourceItem == item));

    private static bool HasMemorial(GameState state, HeroId hero, out Memorial memorial)
    {
        var found = state.Drama.Memorials.FirstOrDefault(m => m.Hero == hero);
        memorial = found!;
        return found is not null;
    }

    private static IEnumerable<ItemId> WornItems(GearSet gear)
    {
        if (gear.Weapon is { } weapon)
        {
            yield return weapon;
        }

        if (gear.Shield is { } shield)
        {
            yield return shield;
        }

        if (gear.Armor is { } armor)
        {
            yield return armor;
        }

        if (gear.Trinket is { } trinket)
        {
            yield return trinket;
        }
    }
}
