using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Venues;

namespace GameSim.Drama;

/// <summary>
/// The drama director (Phase C, U-C3): a Morning daily poll that paces the world's incidents and
/// escalates the dens. It is the sim's FOURTH legitimate RNG consumer (after ExpeditionResolver,
/// QualityRoller, HeroRoster). Determinism (KTD4/KTD2):
/// <list type="bullet">
///   <item>Integer-only; no floats, no wall clock, no transcendental <c>Math.*</c> (Min/Max/Clamp OK).</item>
///   <item>It draws from the EXISTING injected kernel stream — never a new <c>Pcg32</c>. Adding draws to
///   the one stream advances its <c>State</c> only; the stream identity <c>RngState.Inc</c> is
///   UNCHANGED (a changed <c>Inc</c> would mean a new stream = a determinism bug).</item>
///   <item>EXACTLY ONE draw per calendar Morning, drawn UNCONDITIONALLY each poll (whether or not the
///   pacing gate lets the picked incident fire) so the draw COUNT is a fixed one-per-day — asserted by
///   <c>DirectorSystemTests</c>.</item>
///   <item>All evolving state lives in <see cref="DirectorState"/> + <see cref="GameState.Venues"/>
///   (serializable), advancing deterministically: same seed + same actions = identical state.</item>
/// </list>
/// <para><b>Escalation inputs are a HARD INVARIANT:</b> incident CATEGORY is gated by the town's
/// PROGRESSION TIER (deepest floor reached) and MAGNITUDE by its SURVIVED-COUNT (delvers returned) —
/// NEVER by shop wealth/gold. Reading gold here would be the RimWorld wealth-spiral (punishing the
/// player for prospering); <c>DirectorSystemTests</c> pins that raising player/hero gold changes
/// nothing the director fires.</para>
/// <para>Den escalation rides this same pass: each live venue carries a <c>ThreatPm</c> meter
/// (<see cref="VenueState.InfectionPerMille"/>) raised by a scheduled daily increment, lowered by
/// yesterday's cleared expeditions, stepped through category tiers at fixed thresholds, and latched
/// into lockdown at the cap. No sim rule reads it back, so it is recorded drama only.</para>
/// </summary>
public sealed class DirectorSystem : IPhaseSystem
{
    public DayPhase Phase => DayPhase.Morning;

    public string Name => "drama-director";

    public GameState Process(GameState state, IDeterministicRng rng, IEventSink events)
    {
        // Held-Morning guard (the RentSystem/GossipSystem precedent): fire once per calendar Morning,
        // not once per stepped-counter tick. CounterQueueSystem runs ahead of this system, so on the
        // closing tick the session reads Closed==true and the director polls exactly once. A run whose
        // Counter stays null (BaselinePlayer / the balance gate — the only paths exercised) skips this
        // guard entirely. This is what keeps "exactly one draw per day" true through a stepped Morning.
        if (state.Counter is { Closed: false })
        {
            return state;
        }

        var day = state.Day;
        var d = state.Director;

        // 1. PACING (pure, no RNG): fixed decay − then yesterday's event tension deltas, run through
        //    the BuildUp/Peak/Relax machine with its min-duration dwell counters.
        var eventDelta = TensionFromYesterday(state, day);
        var (tension, phase, phaseEnteredDay) =
            DirectorPacing.Step(d.Tension, d.Phase, d.PhaseEnteredDay, day, eventDelta);

        // 2. ESCALATION INPUTS — progression tier for CATEGORY, survived-count for MAGNITUDE. NEVER
        //    gold (the wealth-spiral invariant). Both are pure reads of durable roster facts.
        var progressionTier = ProgressionTier(state);
        var survivedCount = SurvivedCount(state);

        // 3. ELIGIBLE SET → integer cumulative-weight table. The tier-0/survived-0 baseline incident is
        //    always eligible, so the table is never empty and the single draw below is always in range.
        var totalWeight = 0;
        foreach (var inc in Catalog)
        {
            if (inc.MinProgressionTier <= progressionTier && inc.MinSurvived <= survivedCount)
            {
                totalWeight += inc.Weight;
            }
        }

        // 4. THE ONE SEEDED DRAW (the legitimate 4th rng. site), on the EXISTING kernel stream. Drawn
        //    unconditionally so the per-day draw count is exactly one; the pacing gate below decides
        //    whether the picked incident actually surfaces.
        var roll = rng.NextInt(0, totalWeight);
        var picked = PickByCumulativeWeight(progressionTier, survivedCount, roll);

        // 5. PACING GATE: fire only at Peak past the refire guard, OR when the drought pity forces it
        //    (pity overrides both the phase and the refire guard — it is the anti-drought floor).
        var refireOk = d.LastFiredDay == 0 || day - d.LastFiredDay >= MinRefireDays;
        var pityForces = d.DroughtDays >= DroughtPityDays;
        var fire = (phase == DirectorPhase.Peak && refireOk) || pityForces;

        var lastFiredDay = d.LastFiredDay;
        var droughtDays = d.DroughtDays;
        string? firedVenueId = null;

        if (fire)
        {
            // Firing releases dramatic tension (the next daily Step then cools Peak → Relax).
            tension = Math.Max(TensionMin, tension - FireRelief);
            lastFiredDay = day;
            droughtDays = 0;
            firedVenueId = picked.VenueId;
            events.Emit(new IncidentFired(picked.Id, picked.Category, picked.Magnitude, picked.VenueId, tension));
        }
        else
        {
            droughtDays += 1;
        }

        // 6. DEN HEARTBEAT (pure, no RNG): every live venue takes a scheduled increment minus
        //    yesterday's clears, plus an incident surge if it was the fired den; tiers/lockdown follow.
        var venues = TickDens(state, day, firedVenueId, events);

        return state with
        {
            Director = new DirectorState(tension, phase, phaseEnteredDay, lastFiredDay, droughtDays),
            Venues = venues,
        };
    }

    // ---- Pacing tuning (the tension machine's knobs — change consciously; balance-sensitive) ----

    /// <summary>Tension accumulator bounds (0..1000, integer).</summary>
    public const int TensionMin = 0;
    public const int TensionMax = 1000;

    /// <summary>Fixed tension bled off every daily poll (the "− fixed daily decay").</summary>
    public const int DailyDecay = 40;

    /// <summary>Tension released when an incident fires (Peak → Relax on the next step).</summary>
    public const int FireRelief = 450;

    /// <summary>Yesterday's-event tension deltas (the "+event deltas"): drama raises the stakes.</summary>
    public const int TensionPerDeath = 220;
    public const int TensionPerFloorRecord = 45;
    public const int TensionPerReturn = 25;

    /// <summary>Refire guard: minimum days between two fired incidents (pity can override).</summary>
    public const int MinRefireDays = 3;

    /// <summary>Drought pity: force-fire once the director has polled this many days without firing.</summary>
    public const int DroughtPityDays = 12;

    // ---- Escalation-input derivation (NEVER reads gold) ----

    /// <summary>
    /// Progression tier (0..3) from the town's deepest floor reached across the WHOLE roster (dead
    /// heroes keep their record, so progress never regresses on a death). Gates incident CATEGORY.
    /// Floor 1 → tier 0, floor 2 → 1, floor 3 → 2, floors 4–5 → 3. Reads no gold.
    /// </summary>
    public static int ProgressionTier(GameState state)
    {
        var deepest = 0;
        foreach (var hero in state.Heroes.Values)
        {
            deepest = Math.Max(deepest, hero.DeepestFloorReached);
        }

        return Math.Clamp(deepest - 1, 0, 3);
    }

    /// <summary>
    /// Survived-count: how many heroes have returned from at least one delve (DeepestFloorReached ≥ 1),
    /// dead or alive — the town's battle-tested depth. Gates incident MAGNITUDE. Reads no gold.
    /// </summary>
    public static int SurvivedCount(GameState state)
    {
        var count = 0;
        foreach (var hero in state.Heroes.Values)
        {
            if (hero.DeepestFloorReached >= 1)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>The catalog ids currently eligible for <paramref name="state"/> (progression + survived
    /// gates, declaration order). The wealth-spiral invariant test asserts this is invariant to gold but
    /// DOES respond to progression.</summary>
    public static ImmutableArray<string> EligibleIds(GameState state)
    {
        var tier = ProgressionTier(state);
        var survived = SurvivedCount(state);
        var builder = ImmutableArray.CreateBuilder<string>();
        foreach (var inc in Catalog)
        {
            if (inc.MinProgressionTier <= tier && inc.MinSurvived <= survived)
            {
                builder.Add(inc.Id);
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>Yesterday's stamped drama, summed into a tension delta. Reverse-scans the day slice the
    /// gossip system uses; day 1 has no yesterday, so it contributes 0.</summary>
    private static int TensionFromYesterday(GameState state, int day)
    {
        var yesterday = day - 1;
        if (yesterday < 1)
        {
            return 0;
        }

        var delta = 0;
        foreach (var gameEvent in DayLog.For(state.EventLog, yesterday))
        {
            delta += gameEvent switch
            {
                HeroDied => TensionPerDeath,
                FloorRecordSet => TensionPerFloorRecord,
                PartyReturned => TensionPerReturn,
                _ => 0,
            };
        }

        return delta;
    }

    // ---- Incident catalog (fixed data; NOT saved state, so it lives in code, not Contracts) ----

    /// <summary>One incident the director can draw. <see cref="MinProgressionTier"/> gates its CATEGORY
    /// to the town's progression, <see cref="MinSurvived"/> gates its MAGNITUDE to survived-count, and
    /// <see cref="Weight"/> is its cumulative-table weight. Order is fixed — the filter walks it in
    /// declaration order, so the cumulative table is deterministic.</summary>
    public sealed record IncidentDef(
        string Id,
        IncidentCategory Category,
        IncidentMagnitude Magnitude,
        int MinProgressionTier,
        int MinSurvived,
        int Weight,
        string VenueId);

    /// <summary>The incident table (fixed order). The first entry (tier 0 / survived 0) is ALWAYS
    /// eligible, guaranteeing a non-empty weight table for the daily draw. Higher categories unlock as
    /// the town delves deeper, higher magnitudes as survivors accumulate — never on wealth.</summary>
    public static readonly ImmutableArray<IncidentDef> Catalog = ImmutableArray.Create(
        new IncidentDef("whispers_in_the_dark", IncidentCategory.Rumor, IncidentMagnitude.Minor, 0, 0, 40, VenueRegistry.MineId),
        new IncidentDef("goblin_probe", IncidentCategory.Skirmish, IncidentMagnitude.Minor, 0, 1, 30, VenueRegistry.MineId),
        new IncidentDef("spider_brood_swells", IncidentCategory.Infestation, IncidentMagnitude.Notable, 1, 2, 20, VenueRegistry.MineId),
        new IncidentDef("ghoul_warren_breaks", IncidentCategory.Breakout, IncidentMagnitude.Notable, 2, 3, 12, VenueRegistry.MineId),
        new IncidentDef("the_forgeworm_stirs", IncidentCategory.Cataclysm, IncidentMagnitude.Severe, 3, 4, 6, VenueRegistry.MineId));

    /// <summary>Pick the incident whose cumulative-weight band contains <paramref name="roll"/>, over
    /// the eligible subset (declaration order). Pure integer selection — the caller drew
    /// <paramref name="roll"/> in [0, totalWeight); this method makes NO RNG draw. Falls back to the
    /// last eligible incident defensively (unreachable given a correct total).</summary>
    public static IncidentDef PickByCumulativeWeight(int progressionTier, int survivedCount, int roll)
    {
        var cumulative = 0;
        var last = Catalog[0];
        foreach (var inc in Catalog)
        {
            if (inc.MinProgressionTier > progressionTier || inc.MinSurvived > survivedCount)
            {
                continue;
            }

            last = inc;
            cumulative += inc.Weight;
            if (roll < cumulative)
            {
                return inc;
            }
        }

        return last;
    }

    // ---- Den escalation (pure, no RNG) ----

    /// <summary>Scheduled per-mille the den's threat rises each day.</summary>
    public const int DenDailyIncrement = 18;

    /// <summary>Per-mille relief per cleared expedition that returned yesterday.</summary>
    public const int DenClearRelief = 30;

    /// <summary>Extra per-mille surge when an incident fires at this den.</summary>
    public const int DenIncidentSurge = 120;

    /// <summary>Threat meter bounds; at the cap the den locks down.</summary>
    public const int DenThreatMin = 0;
    public const int DenThreatCap = 1000;

    /// <summary>Den category tier (0..3) from the threat meter: &lt;250 → 0, &lt;500 → 1, &lt;750 → 2,
    /// ≥750 → 3. The fixed thresholds are the "category shift" boundaries.</summary>
    public static int DenTier(int threatPermille) =>
        threatPermille < 250 ? 0
        : threatPermille < 500 ? 1
        : threatPermille < 750 ? 2
        : 3;

    /// <summary>
    /// Advance one den by a day (pure integer, no RNG): threat = clamp(prev + daily increment −
    /// clears·relief + surge), then re-derive the category <c>ThreatTier</c> and the lockdown latch
    /// (<c>Closed</c> is sticky — once locked, stays locked). <paramref name="Shifted"/> is true when the
    /// tier or the lockdown state changed, i.e. when a <see cref="DenThreatShifted"/> should be emitted.
    /// </summary>
    public static (VenueState Next, bool Shifted) DenStep(VenueState prev, int clears, int surge)
    {
        var threat = Math.Clamp(
            prev.InfectionPerMille + DenDailyIncrement - clears * DenClearRelief + surge,
            DenThreatMin, DenThreatCap);
        var daysUntouched = clears > 0 ? 0 : prev.DaysUntouched + 1;
        var tier = DenTier(threat);
        var locked = prev.Closed || threat >= DenThreatCap;

        var next = new VenueState(daysUntouched, threat, locked, tier);
        var shifted = tier != prev.ThreatTier || locked != prev.Closed;
        return (next, shifted);
    }

    /// <summary>
    /// Escalate every LIVE venue's den one day via <see cref="DenStep"/>, emitting a
    /// <see cref="DenThreatShifted"/> whenever a den's tier or lockdown changes. Only
    /// <see cref="VenueRegistry.LiveRotation"/> venues escalate; a single live den (the Mine) lets
    /// yesterday's returns be attributed to it directly — a multi-den rotation would need per-venue
    /// clear attribution (noted for U-C4).
    /// </summary>
    private static ImmutableSortedDictionary<string, VenueState> TickDens(
        GameState state, int day, string? firedVenueId, IEventSink events)
    {
        var clears = ClearsYesterday(state, day);
        var venues = state.Venues;

        foreach (var venueId in VenueRegistry.LiveRotation)
        {
            var prev = venues.TryGetValue(venueId, out var existing)
                ? existing
                : new VenueState(DaysUntouched: 0, InfectionPerMille: 0, Closed: false, ThreatTier: 0);

            var surge = venueId == firedVenueId ? DenIncidentSurge : 0;
            var (next, shifted) = DenStep(prev, clears, surge);
            venues = venues.SetItem(venueId, next);

            if (shifted)
            {
                events.Emit(new DenThreatShifted(venueId, next.InfectionPerMille, next.ThreatTier, next.Closed));
            }
        }

        return venues;
    }

    /// <summary>Count of parties that returned yesterday (a cleared expedition relieves den pressure).
    /// Reads the same day slice the gossip system uses; day 1 has no yesterday.</summary>
    private static int ClearsYesterday(GameState state, int day)
    {
        var yesterday = day - 1;
        if (yesterday < 1)
        {
            return 0;
        }

        var clears = 0;
        foreach (var gameEvent in DayLog.For(state.EventLog, yesterday))
        {
            if (gameEvent is PartyReturned)
            {
                clears++;
            }
        }

        return clears;
    }
}

/// <summary>
/// The drama director's BuildUp → Peak → Relax pacing machine (Phase C, U-C3), factored out as a pure
/// integer function so it is unit-testable in isolation and provably RNG-free. Every transition is
/// gated by a min-duration dwell (<c>day − phaseEnteredDay</c>) so no phase can flicker faster than its
/// floor. Draws NO RNG, reads no clock — pure integer math (KTD2).
/// </summary>
public static class DirectorPacing
{
    /// <summary>Tension at/above which BuildUp may escalate to Peak.</summary>
    public const int PeakEnterTension = 600;

    /// <summary>Tension below which Peak may cool to Relax (a fire drops it past this).</summary>
    public const int RelaxExitTension = 200;

    /// <summary>Min days in each phase before it may transition (the min-duration counters).</summary>
    public const int MinBuildUpDays = 2;
    public const int MinPeakDays = 1;
    public const int MinRelaxDays = 2;

    /// <summary>Safety cap: Peak cools to Relax after this dwell even if tension stays high, so a
    /// permanently-tense town still cycles and cools instead of locking at Peak.</summary>
    public const int MaxPeakDays = 4;

    /// <summary>
    /// One daily step: apply the fixed decay and the day's event delta (clamped to 0..1000), then run
    /// the phase machine. Returns the new tension, phase, and — when a transition fires — the day it
    /// entered its new phase (the dwell counter reset); otherwise the incoming <paramref name="phaseEnteredDay"/>.
    /// </summary>
    public static (int Tension, DirectorPhase Phase, int PhaseEnteredDay) Step(
        int tension, DirectorPhase phase, int phaseEnteredDay, int day, int eventDelta)
    {
        var t = Math.Clamp(tension - DirectorSystem.DailyDecay + eventDelta,
            DirectorSystem.TensionMin, DirectorSystem.TensionMax);
        var dwell = day - phaseEnteredDay;

        switch (phase)
        {
            case DirectorPhase.BuildUp:
                if (t >= PeakEnterTension && dwell >= MinBuildUpDays)
                {
                    return (t, DirectorPhase.Peak, day);
                }

                break;

            case DirectorPhase.Peak:
                // Cool once tension has bled below the exit band (a fire does this) OR the peak has run
                // its max dwell — either way only after the minimum peak dwell.
                if (dwell >= MinPeakDays && (t < RelaxExitTension || dwell >= MaxPeakDays))
                {
                    return (t, DirectorPhase.Relax, day);
                }

                break;

            case DirectorPhase.Relax:
                if (dwell >= MinRelaxDays)
                {
                    return (t, DirectorPhase.BuildUp, day);
                }

                break;
        }

        return (t, phase, phaseEnteredDay);
    }
}
