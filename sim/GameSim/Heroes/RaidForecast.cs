using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using GameSim.Venues;

namespace GameSim.Heroes;

/// <summary>One floor a party will pass through, with the monster that lairs there.</summary>
public sealed record ForecastThreat(int Floor, string MonsterKind);

/// <summary>
/// P2-SCREEN-18: one filled gear slot a marching hero actually wears — the upgrade arm of decision 3
/// ("fill the empty slot, or upgrade the full one"), which before this record had only its gap arm
/// visible (<see cref="ForecastParty.GearGaps"/>). A party in three Common copper daggers used to
/// read identically to a party in full Masterwork; this is the fact that tells them apart.
///
/// <para>FACTS only, per the muster board's own law (<see cref="ForecastParty"/>'s doc): what the
/// slot holds, its quality tier, and whose hands made it (<see cref="WornSlot.PlayerCrafted"/> —
/// link 1, the fact that makes the upgrade decision the PLAYER's, not the sim's). Never combat
/// stats, never a survival estimate, never a power score: this is worn-gear bookkeeping, not a
/// verdict.</para>
/// </summary>
public sealed record WornSlot(
    string HeroName,
    ItemSlot Slot,
    string ItemName,
    QualityGrade Quality,
    bool PlayerCrafted,
    int? CraftedOnDay);

/// <summary>
/// P2-SCREEN-36 ("the marcher's empty slot names the commission that would have filled it",
/// decision 1 — sell the good one, or hold it for the hero who needs it): a hero marching with an
/// empty gear slot that ALSO has a live commission open for that exact slot — the smith already
/// knows what this hero wants, and they are walking into the Mine without it.
///
/// <para>FACTS only: which hero, which slot, and the day the ask falls due — never the premium,
/// never the minimum quality, never a recommendation to fill it before they leave (law 12,
/// "influence never orders"); a caller who wants those reads the <see cref="Commission"/> record
/// itself off <see cref="GameState.Commissions"/>.</para>
/// </summary>
public sealed record GapCommission(string HeroName, ItemSlot Slot, int DeadlineDay);

/// <summary>
/// A single party's raid-day forecast: who marches, how deep they mean to go, the threats on the
/// way, where their kit is thin, and what their filled slots actually hold. Pure projection —
/// presentation data only.
///
/// <para><b>The law this record stays inside of (P2-SCREEN-18):</b> the forecast does not tell you
/// who will survive. It carries only facts of worn gear — what a slot holds, its quality tier, whose
/// hands made it — never a survival estimate, a power score, a "this party needs X" verdict, or a
/// ranking of one party against another. The advisor never orders: naming what a slot holds is a
/// fact, "you should upgrade Kael's dagger" is an order, and this type has no field for the second
/// one.</para>
///
/// <para>P2-LONG-28 ("the muster names the record the party is pressing past"): <see
/// cref="BestRecordedFloor"/> and <see cref="RecordHolderName"/> carry the SAME fact <see
/// cref="GameSim.Expedition.ExpeditionSystem.TargetFloorFor"/> already computed to pick <see
/// cref="TargetFloor"/> in the first place — <c>party.Max(h => h.DeepestFloorReached)</c> — recomputed
/// here rather than threaded through as a new field on <c>PartyPlan</c>, since a Godot board has no
/// other honest way to say WHY floor 4 is the target instead of merely that it is. Still a fact, never
/// a verdict (link 3, "the hero carries it into the dark on their own judgment") — the record a party
/// is pressing past, or the ground it is walking again, never an opinion about whether it should.</para>
/// </summary>
public sealed record ForecastParty(
    ImmutableList<string> HeroNames,
    int TargetFloor,
    string VenueId,
    ImmutableList<ForecastThreat> Threats,
    ImmutableList<string> GearGaps,
    ImmutableList<WornSlot> WornGear,
    int BestRecordedFloor,
    string RecordHolderName,
    ImmutableList<GapCommission> GapCommissions);

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
    /// not a gap); <see cref="ForecastParty.WornGear"/> (P2-SCREEN-18) names what every OTHER weapon/
    /// shield/armor slot holds — the two lists partition the same three tracked slots, so together
    /// they account for every hero's kit, gap or filled.
    /// </summary>
    public static ImmutableList<ForecastParty> ForTomorrow(GameState state)
    {
        var plans = MusterPlan.Compute(state.Heroes, state.Bounties, state.Items, state.Day);

        var forecast = ImmutableList.CreateBuilder<ForecastParty>();
        foreach (var plan in plans)
        {
            // Phase C U-C4: read the PLAN'S OWN venue (routing may send a party to any live venue),
            // not always the Mine — otherwise a Gloomwood-routed party's threat list would name Mine
            // monsters it never actually faces.
            var venue = VenueRegistry.Require(plan.VenueId);
            var partyHeroes = plan.Roster.Select(id => state.Heroes[id.Value]).ToImmutableList();
            var names = partyHeroes.Select(h => h.Name).ToImmutableList();

            // P2-LONG-28: the exact fact ExpeditionSystem.TargetFloorFor's own default rule reads
            // off this same party — "one past the party's best recorded floor" — so the board can
            // name whose record floor 4 presses past instead of showing a bare, arbitrary-looking
            // number. First-in-roster-order on a tie, matching every other deterministic-pick idiom
            // in this file (roster order is already HeroId order, so this can never reshuffle
            // between two identical builds).
            var bestRecordedFloor = partyHeroes.Max(h => h.DeepestFloorReached);
            var recordHolderName = partyHeroes.First(h => h.DeepestFloorReached == bestRecordedFloor).Name;

            var threats = ImmutableList.CreateBuilder<ForecastThreat>();
            for (var floor = 1; floor <= plan.TargetFloor; floor++)
            {
                threats.Add(new ForecastThreat(floor, venue.MonsterKind(floor)));
            }

            var gaps = ImmutableList.CreateBuilder<string>();
            var worn = ImmutableList.CreateBuilder<WornSlot>();
            var gapCommissions = ImmutableList.CreateBuilder<GapCommission>();
            foreach (var hero in partyHeroes)
            {
                var missing = MissingItemSlots(hero.Gear);
                if (missing.Count > 0)
                {
                    gaps.Add($"{hero.Name}: {string.Join(", ", missing.Select(SlotLabel))}");

                    // P2-SCREEN-36: does a still-open commission (posted or accepted, but not yet
                    // fulfilled or expired — both of THOSE remove the entry from state.Commissions
                    // outright, so anything still here is still live) name one of THIS hero's actual
                    // gaps? CommissionSystem posts at most one live commission per hero at a time
                    // (PostCommissions' own heroesWithCommission gate), but this walks every missing
                    // slot rather than assuming that invariant, so a future relaxation of it could
                    // never silently under-report here.
                    foreach (var slot in missing)
                    {
                        if (state.Commissions.FirstOrDefault(c => c.Hero == hero.Id && c.Slot == slot)
                            is { } commission)
                        {
                            gapCommissions.Add(new GapCommission(hero.Name, slot, commission.DeadlineDay));
                        }
                    }
                }

                // Same three tracked slots MissingItemSlots gaps on (trinket is optional content,
                // not part of decision 3's fill-or-upgrade surface) — every slot a gap did NOT claim
                // is filled, and what it holds is this loop's whole job.
                foreach (var slot in TrackedSlots)
                {
                    if (hero.Gear.Slot(slot) is { } itemId
                        && state.Items.TryGetValue(itemId.Value, out var item))
                    {
                        worn.Add(new WornSlot(
                            hero.Name, slot, item.Name, item.Quality, item.PlayerCrafted, item.Mark?.CraftedOnDay));
                    }
                }
            }

            forecast.Add(new ForecastParty(
                names, plan.TargetFloor, plan.VenueId, threats.ToImmutable(), gaps.ToImmutable(), worn.ToImmutable(),
                bestRecordedFloor, recordHolderName, gapCommissions.ToImmutable()));
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

    /// <summary>The three slots decision 3 ("fill the empty slot, or upgrade the full one") is
    /// actually about — the same set <see cref="MissingItemSlots"/> gaps on. Trinket stays excluded
    /// (optional content, not a gap and not an upgrade decision — same rule <see
    /// cref="MissingItemSlots"/>'s own doc states).</summary>
    private static readonly ItemSlot[] TrackedSlots = { ItemSlot.Weapon, ItemSlot.Shield, ItemSlot.Armor };
}
