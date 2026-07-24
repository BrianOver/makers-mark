using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using GameSim.Kernel;

namespace GameSim.Heroes;

/// <summary>
/// Wave 3 "Commissions" (plan 2026-07-24-003, U13): a Morning system that asks the player, on the
/// hero's behalf, to forge a specific gear slot. WIDENED design (2026-07-24 walkthrough choice): ANY
/// alive hero with an empty or clearly sub-par weapon/shield/armor slot may get a commission posted —
/// this is NOT gated on <see cref="RelationshipBand"/> (bands only scale the ask's MinQuality/premium,
/// per KTD4: depth signals terminate in price/verdict/queue/prose, never in who gets served). To keep
/// the board (and the guaranteed-premium income channel — U14) from flooding, at most
/// <see cref="MaxOpenCommissions"/> UN-ACCEPTED commissions may exist at once; this system tops the
/// board back up toward that cap each calendar Morning, never exceeding it.
///
/// <para>Also owns deadline expiry (U14's other half): an ACCEPTED commission that passes its
/// <see cref="Commission.DeadlineDay"/> unfulfilled emits <see cref="CommissionExpired"/> and a mood
/// hit. A commission that was POSTED but never accepted expires SILENTLY when its deadline passes —
/// no event, no mood change — so a player (or <c>BaselinePlayer</c>) who never looks at the board is
/// never penalized; only an accepted-then-missed promise stings.</para>
///
/// <para>Determinism: pure integer projection over <see cref="MusterPlan.Compute"/> (the same
/// zero-RNG prediction <see cref="RaidForecast"/> and <see cref="MusterSystem"/> already use) — no
/// RNG draw, no wall clock, no transcendental math. U1 held-Morning guard: fires once per calendar
/// Morning even while a stepped counter session holds the day at Morning across many ticks.</para>
///
/// <para>PKD7: commissions never touch party formation, floor choice, or expedition resolution — they
/// only add gold-priced entries to <see cref="GameState.Commissions"/> and (via U14) nudge
/// <see cref="Hero.MoodPermille"/>. Registered AFTER <see cref="HeroShoppingSystem"/> and BEFORE
/// <see cref="MusterSystem"/> in <c>GameComposition</c>'s Morning block (see that file's ordering
/// comment): gaps are read post-shopping so a hero who just bought their own fix this same Morning
/// isn't also offered a redundant commission for it, and MusterSystem's documented "must register
/// LAST" contract stays intact.</para>
/// </summary>
public sealed class CommissionSystem : IPhaseSystem
{
    /// <summary>Cap on concurrently OPEN (posted, not-yet-accepted) commissions — keeps the board and
    /// the guaranteed-premium income channel from flooding (U13 explicit requirement).</summary>
    public const int MaxOpenCommissions = 3;

    /// <summary>Mornings a hero gives the smith to deliver, from the day the commission is posted.</summary>
    public const int DeadlineWindowDays = 5;

    /// <summary>Base premium (gold) over list, before floor/band scaling.</summary>
    public const int BasePremiumGold = 15;

    /// <summary>Extra premium gold per target floor the hero is marching toward.</summary>
    public const int PremiumPerFloor = 10;

    /// <summary>Mood lost when an ACCEPTED commission passes its deadline unfulfilled (U14).</summary>
    public const int ExpireMoodPenalty = 100;

    public DayPhase Phase => DayPhase.Morning;

    public string Name => "commissions";

    public GameState Process(GameState state, IDeterministicRng rng, IEventSink events)
    {
        // U1 held-Morning guard (identical to RentSystem/HeroShoppingSystem): a stepped counter
        // session holds the day at Morning across many ticks, and GameKernel re-runs every Morning
        // system on every tick, so a once-per-Morning effect must skip until the session's final,
        // closing tick (Closed==true) — CounterQueueSystem registers ahead of this block and flips
        // Closed on either close path before this system ever sees Closed==true.
        if (state.Counter is { Closed: false })
        {
            return state;
        }

        state = ExpireCommissions(state, events);
        state = PostCommissions(state, events);
        return state;
    }

    /// <summary>Drops every commission whose deadline has passed. An ACCEPTED one emits
    /// <see cref="CommissionExpired"/> + a mood hit (a broken promise stings); a merely POSTED one is
    /// removed with no event and no mood change (ignoring the board is always safe).</summary>
    private static GameState ExpireCommissions(GameState state, IEventSink events)
    {
        if (state.Commissions.IsEmpty)
        {
            return state;
        }

        var kept = ImmutableList.CreateBuilder<Commission>();
        foreach (var commission in state.Commissions)
        {
            if (state.Day <= commission.DeadlineDay)
            {
                kept.Add(commission);
                continue;
            }

            if (commission.Accepted)
            {
                events.Emit(new CommissionExpired(commission.Hero, commission.Slot));
                state = BumpMood(state, commission.Hero, -ExpireMoodPenalty);
            }

            // Posted-but-never-accepted: silently dropped — no event, no mood change (U14 rule).
        }

        return state with { Commissions = kept.ToImmutable() };
    }

    /// <summary>Tops the open (un-accepted) board back up toward <see cref="MaxOpenCommissions"/>,
    /// scanning heroes in HeroId order (deterministic, matches every other Morning system) for the
    /// first gear gap that doesn't already have a live commission (open or accepted).</summary>
    private static GameState PostCommissions(GameState state, IEventSink events)
    {
        var openCount = state.Commissions.Count(c => !c.Accepted);
        if (openCount >= MaxOpenCommissions)
        {
            return state;
        }

        var heroesWithCommission = new HashSet<int>(state.Commissions.Select(c => c.Hero.Value));

        var plans = MusterPlan.Compute(state.Heroes, state.Bounties);
        var targetFloorByHero = new Dictionary<int, int>();
        foreach (var plan in plans)
        {
            foreach (var id in plan.Roster)
            {
                targetFloorByHero[id.Value] = plan.TargetFloor;
            }
        }

        foreach (var heroId in state.Heroes.Keys) // ascending HeroId.Value order
        {
            if (openCount >= MaxOpenCommissions)
            {
                break;
            }

            var hero = state.Heroes[heroId];
            if (!hero.Alive || heroesWithCommission.Contains(heroId))
            {
                continue;
            }

            // Only heroes actually mustering tomorrow have a target floor to scale the ask against —
            // a hero sitting out (e.g. an odd leftover, or simply not enough live heroes to party)
            // gets no commission this Morning rather than a floor-less guess.
            if (!targetFloorByHero.TryGetValue(heroId, out var targetFloor))
            {
                continue;
            }

            var slot = FindGapSlot(hero.Gear, state.Items, targetFloor);
            if (slot is null)
            {
                continue; // fully and adequately kitted for the trip
            }

            var band = RelationshipBands.For(hero.Id, state);
            var minQuality = MinQualityFor(targetFloor, band);
            var premium = PremiumFor(targetFloor, band);
            var deadline = state.Day + DeadlineWindowDays;

            events.Emit(new CommissionPosted(hero.Id, slot.Value, minQuality, deadline, premium));
            state = state with
            {
                Commissions = state.Commissions.Add(
                    new Commission(hero.Id, slot.Value, minQuality, deadline, premium)),
            };

            openCount++;
            heroesWithCommission.Add(heroId);
        }

        return state;
    }

    /// <summary>The floor-implied quality bar: what a hero marching to this depth expects their kit
    /// to clear. Doubles as the "sub-par" bar (a worn item below this grade is as good as empty for
    /// commission purposes — the plan's own wording) and as the FLOOR half of the posted MinQuality.</summary>
    private static QualityGrade FloorMinQuality(int targetFloor) => targetFloor switch
    {
        >= 5 => QualityGrade.Superior,
        >= 3 => QualityGrade.Fine,
        _ => QualityGrade.Common,
    };

    /// <summary>The BAND half of the posted MinQuality — a Sworn regular knows the smith's best work
    /// and won't ask for less; a Stranger/Regular is happy with whatever the floor demands.</summary>
    private static QualityGrade BandMinQuality(RelationshipBand band) => band switch
    {
        RelationshipBand.Sworn => QualityGrade.Superior,
        RelationshipBand.Patron => QualityGrade.Fine,
        _ => QualityGrade.Common,
    };

    private static QualityGrade MinQualityFor(int targetFloor, RelationshipBand band)
    {
        var floorBar = (int)FloorMinQuality(targetFloor);
        var bandBar = (int)BandMinQuality(band);
        return (QualityGrade)Math.Max(floorBar, bandBar);
    }

    private static int PremiumBonusFor(RelationshipBand band) => band switch
    {
        RelationshipBand.Sworn => 50,
        RelationshipBand.Patron => 25,
        RelationshipBand.Regular => 10,
        _ => 0,
    };

    private static int PremiumFor(int targetFloor, RelationshipBand band) =>
        BasePremiumGold + (PremiumPerFloor * targetFloor) + PremiumBonusFor(band);

    /// <summary>The first slot (fixed Weapon/Shield/Armor order — deterministic) that is either empty
    /// or worn below <see cref="FloorMinQuality"/> for the hero's target floor. Null when every worn
    /// slot is filled at or above the bar (nothing to commission).</summary>
    private static ItemSlot? FindGapSlot(GearSet gear, ImmutableSortedDictionary<int, Item> items, int targetFloor)
    {
        var bar = FloorMinQuality(targetFloor);
        foreach (var slot in new[] { ItemSlot.Weapon, ItemSlot.Shield, ItemSlot.Armor })
        {
            var worn = gear.Slot(slot);
            if (worn is null)
            {
                return slot; // empty — an unambiguous gap
            }

            if (items.TryGetValue(worn.Value.Value, out var item) && item.Quality < bar)
            {
                return slot; // sub-par: below what this depth demands
            }
        }

        return null;
    }

    /// <summary>Applies a signed mood delta to one hero (mirrors the counter-haggle mood-bump idiom in
    /// <c>HaggleResolver.CloseSale</c> — unclamped, since <see cref="Hero.MoodPermille"/> is
    /// documented as a signed, unbounded opinion score). A no-op if the hero is no longer in the
    /// roster (defensive; never throws).</summary>
    internal static GameState BumpMood(GameState state, HeroId hero, int delta)
    {
        if (!state.Heroes.TryGetValue(hero.Value, out var h))
        {
            return state;
        }

        return state with
        {
            Heroes = state.Heroes.SetItem(hero.Value, h with { MoodPermille = h.MoodPermille + delta }),
        };
    }
}
