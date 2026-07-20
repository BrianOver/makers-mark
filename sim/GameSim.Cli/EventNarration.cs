using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Narrative;

namespace GameSim.Cli;

/// <summary>
/// Renders a resolved <see cref="GameEvent"/> into the one player-facing beat the interactive CLI
/// prints for it, or <c>null</c> for an event with no beat (a pure projection — no state mutation,
/// no RNG draw; deterministic flavor variation reads the snapshot counter <c>state.Rng.Inc</c>).
///
/// Playtest 2026-07-20 finding N1 (P0): a SUCCESSFUL craft narrated nothing — the switch had no
/// <see cref="ItemCrafted"/> case, so a legal craft looked identical to a silent no-op. The
/// <c>⚒ forged …</c> line closes that: every resolution the player caused now says so out loud.
/// Extracted from Program.cs's former inline <c>Narrate</c> so the mapping is unit-testable.
/// </summary>
public static class EventNarration
{
    public static string? Line(GameEvent gameEvent, GameState state) => gameEvent switch
    {
        ItemCrafted crafted =>
            $"  ⚒ forged {ItemName(state, crafted.Item)} [{crafted.Quality}]",
        ItemSold sold when sold.FromPlayerShop =>
            $"  $ {HeroName(state, sold.Buyer)} bought {ItemName(state, sold.Item)} for {sold.Price}g from YOUR shop",
        HeroPassedOnItem pass =>
            $"  ~ {HeroName(state, pass.Hero)} passed on {ItemName(state, pass.Item)}: {pass.Reason}",
        PartyDeparted dep =>
            "  → " + ExpeditionNarrator.Departure(PartyHeroes(state, dep.Party), dep.TargetFloor, NarratorPack.Pack, state.Rng.Inc, dep.Day),
        AttributionBeatEvent beat =>
            $"  ★ {beat.Beat}: {beat.Detail} (floor {beat.Floor})",
        HeroDied died =>
            $"  † {HeroName(state, died.Hero)} died on floor {died.Floor} — {died.Cause}",
        SupplyDelivered supply =>
            $"  ⛏ runner delivered {ItemName(state, supply.Item)} to {HeroName(state, supply.To)} at camp — {supply.Fee}g",
        PartyRecalled recalled =>
            $"  ⤺ recall bell — [{string.Join(", ", recalled.Party.Select(h => HeroName(state, h)))}] bank and surface",
        RecruitArrived recruit =>
            $"  + recruit {HeroName(state, recruit.Hero)} arrives in town",
        GossipEmitted gossip =>
            $"  🍺 \"{gossip.Line}\"",
        _ => null,
    };

    private static string HeroName(GameState s, HeroId id) => s.Heroes.TryGetValue(id.Value, out var h) ? h.Name : id.ToString();

    private static string ItemName(GameState s, ItemId id) => s.Items.TryGetValue(id.Value, out var i) ? i.Name : id.ToString();

    private static ImmutableList<Hero> PartyHeroes(GameState s, ImmutableList<HeroId> ids)
    {
        var heroes = ImmutableList.CreateBuilder<Hero>();
        foreach (var id in ids)
        {
            if (s.Heroes.TryGetValue(id.Value, out var hero))
            {
                heroes.Add(hero);
            }
        }

        return heroes.ToImmutable();
    }
}
