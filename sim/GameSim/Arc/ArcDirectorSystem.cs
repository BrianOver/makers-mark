using System.Linq;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Venues;

namespace GameSim.Arc;

/// <summary>
/// Phase D (U-D3): the campaign's 3-act arc director. Reads progression signals already in
/// <see cref="GameState"/> — deepest floor reached (<see cref="DramaState.DepthsBoard"/>), the
/// forward ladder's own <see cref="Hero.LadderRank"/> (forward-ladder plan 2026-08-10-003, L5), and
/// days elapsed (<see cref="GameState.Day"/>) — to advance Act I -&gt; Act II -&gt; Act III (never
/// regresses), fires the Climax beat once the ladder's top is beaten, and fires the Ending a fixed
/// number of days later with an assembled final-chronicle summary.
///
/// <para><b>L5 re-anchor.</b> Act II -&gt; Act III used to fire on the SAME signal as the Climax
/// (both read the Mine's own floor 5, "the wall") — but that closed the campaign the moment the
/// Mine's floor 5 fell, one to two rungs before the real endgame the forward ladder built
/// (Gloomwood, then Emberfall, were never reached before the old Ending already fired — measured
/// day 16-24 on every L4 seed). Act III now opens when a hero reaches <see cref="TerminalRank"/> —
/// the ladder's last dungeon admits them — and the Climax is a LATER, separate moment: that same
/// dungeon's own bottom floor falling, promoting a hero to <see cref="ClimaxRank"/>. The two used to
/// share <c>ArcState.ActIIIStartDay</c>; they now use <c>ActIIIStartDay</c> and
/// <see cref="GameSim.Contracts.ArcState.ClimaxDay"/> respectively, because they can land days apart.</para>
///
/// COMPOSITION ORDER: registered in Evening, between BountyPayoutSystem and MarketShareSystem —
/// AFTER ExpeditionRevealSystem (which updates <see cref="DramaState.DepthsBoard"/> and every
/// graduating hero's <see cref="Hero.LadderRank"/> earlier this same Evening tick, so today's new
/// floor record AND today's graduation are both visible here) and BEFORE MarketShareSystem, which
/// must stay LAST in Evening BY CONTRACT (see that class's comment) — this insertion changes no
/// existing system's relative order.
///
/// Determinism (KTD2): pure integer threshold comparisons over already-derived state; draws ZERO
/// RNG (the <paramref name="rng"/> parameter of <see cref="Process"/> is never touched). Every
/// act/climax/ending transition is guarded by <see cref="ArcState"/>'s own monotonic day-stamps, so
/// it fires EXACTLY ONCE regardless of how many Evening ticks land after the threshold is crossed.
/// <see cref="Hero.LadderRank"/> is itself monotonic by construction (graduation-only increment,
/// dead heroes keep their earned rank) so the population-wide max read below can never regress
/// either, exactly like the pre-L5 max-floor read it replaces for Act III/Climax.
/// </summary>
public sealed class ArcDirectorSystem : IPhaseSystem
{
    /// <summary>Deepest floor reached (any hero, ever) that closes Act I and opens Act II — the
    /// plan's "Act 2 (F3-4)" starting point. Unchanged by L5 (measured: fires day 3-4 on every
    /// seed, zero re-baseline risk — the plan's own words).</summary>
    public const int ActIIFloorThreshold = 3;

    /// <summary>Forward-ladder plan L5: the rank of the ladder's own top rung — today, Emberfall
    /// Foundry at <see cref="VenueDefinition.LadderRank"/> 2. A hero reaching this rank has had the
    /// LAST dungeon open for them, closing Act II and opening Act III. Derived from the registry
    /// (never hand-pinned) so a future rung added to the ladder moves this automatically.</summary>
    public static readonly int TerminalRank = VenueRegistry.All.Values.Max(v => v.LadderRank);

    /// <summary>One past <see cref="TerminalRank"/> — reached only by clearing the TERMINAL venue's
    /// own bottom floor (Emberfall's floor 5 falls today). Names no venue; it is the proof the
    /// ladder's top is BEATEN, not merely opened, and it is what fires the Climax.</summary>
    public static readonly int ClimaxRank = TerminalRank + 1;

    /// <summary>Days after the Climax before the Ending/credits fire automatically — the scripted
    /// final-expedition beat the plan describes, compressed to a fixed integer delay in this
    /// skeleton (no Final Commission content exists yet to gate on).</summary>
    public const int EndingDelayDays = 5;

    /// <summary>Proven attribution beats crediting a LIVING hero that count them as a legend for
    /// the final chronicle tally (mirrors <see cref="LegendQuery.FamousBeatThreshold"/>, which only
    /// covers the famous DEAD).</summary>
    public const int LegendBeatThreshold = LegendQuery.FamousBeatThreshold;

    public DayPhase Phase => DayPhase.Evening;

    public string Name => "arc-director";

    public GameState Process(GameState state, IDeterministicRng rng, IEventSink events)
    {
        var arc = state.Arc;

        if (arc.Act == CampaignAct.Ended)
        {
            return state; // the Ending already fired — nothing left for the arc director to do.
        }

        var deepestFloor = state.Drama.DepthsBoard.Values.DefaultIfEmpty(0).Max();
        var maxRank = state.Heroes.Values.Select(h => h.LadderRank).DefaultIfEmpty(0).Max();

        if (arc.Act == CampaignAct.ActI && deepestFloor >= ActIIFloorThreshold)
        {
            arc = arc with { Act = CampaignAct.ActII, ActIIStartDay = state.Day };
            events.Emit(new ActAdvanced(CampaignAct.ActII, deepestFloor));
        }

        // L5: Act III opens on the LADDER signal (a hero reached the top rung, so the last dungeon
        // now admits them) instead of the old Mine-floor-5 wall. This can no longer coincide with
        // the Climax below by construction on any realistic trace (LadderRank increments by exactly
        // one graduation per hero per Evening, so the population max cannot skip past TerminalRank
        // straight to ClimaxRank in a single tick) — but a same-tick cascade is still handled
        // correctly (and stays covered by ArcDirectorSystemTests' synthetic same-tick case) because
        // `arc` is re-read fresh below.
        if (arc.Act == CampaignAct.ActII && maxRank >= TerminalRank)
        {
            arc = arc with { Act = CampaignAct.ActIII, ActIIIStartDay = state.Day };
            events.Emit(new ActAdvanced(CampaignAct.ActIII, deepestFloor));
        }

        // L5: the Climax is now its own later moment — the terminal venue's own bottom floor
        // falling (ClimaxRank), not Act III's own entry. Guarded on ArcState.ClimaxDay (not merely
        // Act == ActIII) so it still fires exactly once even though it no longer shares a tick with
        // the Act III transition above.
        if (arc.Act == CampaignAct.ActIII && arc.ClimaxDay == 0 && maxRank >= ClimaxRank)
        {
            arc = arc with { ClimaxDay = state.Day };
            events.Emit(new ClimaxReached(deepestFloor));
        }

        // L5: Ending is scheduled off ClimaxDay, never ActIIIStartDay — the two now land days apart.
        if (arc.Act == CampaignAct.ActIII && arc.ClimaxDay > 0 && state.Day >= arc.ClimaxDay + EndingDelayDays)
        {
            arc = arc with { Act = CampaignAct.Ended, EndingDay = state.Day };
            events.Emit(BuildEnding(state, deepestFloor));
        }

        return ReferenceEquals(arc, state.Arc) ? state : state with { Arc = arc };
    }

    /// <summary>Assembles the final-chronicle tallies straight off existing state/EventLog — the
    /// "summary of the run's legends" the plan's Credits beat calls for. No new running totals were
    /// added to <see cref="GameState"/> for this; everything here is a pure derivation.</summary>
    private static CampaignEnded BuildEnding(GameState state, int deepestFloor)
    {
        var memorials = state.Drama.Memorials;
        var honored = memorials.Count(m => m.Honored);
        var beats = state.EventLog.OfType<AttributionBeatEvent>().Count();
        var gossip = state.EventLog.OfType<GossipEmitted>().Count();
        var legendaryDead = memorials.Count(m => LegendQuery.IsFamousDead(state, m.Hero));
        var legendaryLiving = state.Heroes.Values.Count(h =>
            h.Alive && LegendQuery.AttributionBeatCount(state, h.Id) >= LegendBeatThreshold);

        return new CampaignEnded(
            DeepestFloorReached: deepestFloor,
            MemorialCount: memorials.Count,
            HonoredMemorialCount: honored,
            AttributionBeatCount: beats,
            GossipHighlightCount: gossip,
            LegendaryHeroCount: legendaryDead + legendaryLiving);
    }
}
