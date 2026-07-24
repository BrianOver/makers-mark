using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using GameSim.Venues;

namespace GameSim.Heroes;

/// <summary>One floor a party will pass through, with the monster that lairs there.</summary>
public sealed record ForecastThreat(int Floor, string MonsterKind);

/// <summary>
/// A single party's raid-day forecast: who marches, how deep they mean to go, the threats on the
/// way, and where their kit is thin. Pure projection — presentation data only.
/// </summary>
public sealed record ForecastParty(
    ImmutableList<string> HeroNames,
    int TargetFloor,
    string VenueId,
    ImmutableList<ForecastThreat> Threats,
    ImmutableList<string> GearGaps);

/// <summary>
/// Game-Feel Plan G4 ("Tomorrow's Telegraph", docs/design/2026-07-21-game-feel-plan.md §G4): the
/// perfect-information triage board the player reads before ending the day — which parties raid, the
/// floor each targets, the monsters between them and it, and which heroes march with an empty gear
/// slot. Deterministic, RNG-free, no wall clock (KTD2): it layers threat + gear-gap enrichment over
/// <see cref="MusterPlan.Compute"/>'s existing party/floor projection (the SAME prediction the
/// Morning <see cref="PartiesFormed"/> event carries), so the board can never disagree with what the
/// Expedition tick forms. Adapter-consumable; the sim never reads it back.
/// </summary>
public static class RaidForecast
{
    /// <summary>
    /// Forecast every party that will muster from the roster + bounty board as they stand now. One
    /// <see cref="ForecastParty"/> per predicted party, in muster order. <see cref="ForecastParty.Threats"/>
    /// lists floors 1..TargetFloor with each floor's monster; <see cref="ForecastParty.GearGaps"/> names
    /// only heroes carrying at least one empty weapon/shield/armor slot (trinket is optional content,
    /// not a gap).
    /// </summary>
    public static ImmutableList<ForecastParty> ForTomorrow(GameState state)
    {
        var plans = MusterPlan.Compute(state.Heroes, state.Bounties);
        var venue = VenueRegistry.Mine; // the only live venue (mirrors MusterPlan's own hardcode)

        var forecast = ImmutableList.CreateBuilder<ForecastParty>();
        foreach (var plan in plans)
        {
            var names = plan.Roster.Select(id => state.Heroes[id.Value].Name).ToImmutableList();

            var threats = ImmutableList.CreateBuilder<ForecastThreat>();
            for (var floor = 1; floor <= plan.TargetFloor; floor++)
            {
                threats.Add(new ForecastThreat(floor, venue.MonsterKind(floor)));
            }

            var gaps = ImmutableList.CreateBuilder<string>();
            foreach (var id in plan.Roster)
            {
                var hero = state.Heroes[id.Value];
                var missing = MissingItemSlots(hero.Gear);
                if (missing.Count > 0)
                {
                    gaps.Add($"{hero.Name}: {string.Join(", ", missing.Select(SlotLabel))}");
                }
            }

            forecast.Add(new ForecastParty(
                names, plan.TargetFloor, plan.VenueId, threats.ToImmutable(), gaps.ToImmutable()));
        }

        return forecast.ToImmutable();
    }

    /// <summary>
    /// Wave 3 (U13): the per-hero/slot gear-gap query <see cref="Heroes.CommissionSystem"/> needs —
    /// this used to be a private, party-level, prose-string helper (<c>MissingSlots</c>); it is now
    /// PUBLIC and returns typed <see cref="ItemSlot"/> values so a caller can act on the gap, not just
    /// print it. Only weapon/shield/armor count as gaps (trinket is optional content, not a gap — same
    /// rule the old prose helper used). Order is fixed (Weapon, Shield, Armor) so callers that pick
    /// "the first gap" stay deterministic.
    /// </summary>
    public static IReadOnlyList<ItemSlot> MissingItemSlots(GearSet gear)
    {
        var missing = new List<ItemSlot>(3);
        if (gear.Weapon is null)
        {
            missing.Add(ItemSlot.Weapon);
        }

        if (gear.Shield is null)
        {
            missing.Add(ItemSlot.Shield);
        }

        if (gear.Armor is null)
        {
            missing.Add(ItemSlot.Armor);
        }

        return missing;
    }

    private static string SlotLabel(ItemSlot slot) => slot switch
    {
        ItemSlot.Weapon => "no weapon",
        ItemSlot.Shield => "no shield",
        ItemSlot.Armor => "no armor",
        _ => $"no {slot.ToString().ToLowerInvariant()}",
    };
}
