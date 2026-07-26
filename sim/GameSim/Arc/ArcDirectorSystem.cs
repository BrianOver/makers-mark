using System.Linq;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Expedition;

namespace GameSim.Arc;

/// <summary>
/// Phase D (U-D3): the campaign's 3-act arc director. Reads progression signals already in
/// <see cref="GameState"/> — deepest floor reached (<see cref="DramaState.DepthsBoard"/>) and days
/// elapsed (<see cref="GameState.Day"/>) — to advance Act I -&gt; Act II -&gt; Act III (never
/// regresses), fires the Climax beat at the Act III threshold, and fires the Ending a fixed number
/// of days later with an assembled final-chronicle summary.
///
/// This unit deliberately does NOT reach for U-D1 (forge tiers), U-D2 (guild dues/Confidence), or
/// a Floor 6 venue — none is merged yet. The plan's richer climax (forge the Final Commission at
/// Forge V, choose the recipient, watch the scripted Warden-of-the-Heart expedition) and the
/// Floor-6 unseal are cross-unit hooks the orchestrator wires onto <see cref="ClimaxReached"/> at
/// batch-integration; this skeleton fires that event off the ONE signal that already exists —
/// reaching the Mine's current deepest floor (<see cref="MonsterTable.FloorCount"/>), "the wall."
///
/// COMPOSITION ORDER: registered in Evening, between BountyPayoutSystem and MarketShareSystem —
/// AFTER ExpeditionRevealSystem (which updates <see cref="DramaState.DepthsBoard"/> earlier this
/// same Evening tick, so today's new floor record is visible here) and BEFORE MarketShareSystem,
/// which must stay LAST in Evening BY CONTRACT (see that class's comment) — this insertion changes
/// no existing system's relative order.
///
/// Determinism (KTD2): pure integer threshold comparisons over already-derived state; draws ZERO
/// RNG (the <paramref name="rng"/> parameter of <see cref="Process"/> is never touched). Every
/// act/climax/ending transition is guarded by <see cref="ArcState"/>'s own monotonic day-stamps, so
/// it fires EXACTLY ONCE regardless of how many Evening ticks land after the threshold is crossed.
/// </summary>
public sealed class ArcDirectorSystem : IPhaseSystem
{
    /// <summary>Deepest floor reached (any hero, ever) that closes Act I and opens Act II — the
    /// plan's "Act 2 (F3-4)" starting point.</summary>
    public const int ActIIFloorThreshold = 3;

    /// <summary>Deepest floor reached that closes Act II, opens Act III, and fires the Climax in
    /// the same tick — the Mine's current deepest floor, "the wall" the plan's Act 3 describes.</summary>
    public static readonly int ActIIIFloorThreshold = MonsterTable.FloorCount;

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

        if (arc.Act == CampaignAct.ActI && deepestFloor >= ActIIFloorThreshold)
        {
            arc = arc with { Act = CampaignAct.ActII, ActIIStartDay = state.Day };
            events.Emit(new ActAdvanced(CampaignAct.ActII, deepestFloor));
        }

        if (arc.Act == CampaignAct.ActII && deepestFloor >= ActIIIFloorThreshold)
        {
            arc = arc with { Act = CampaignAct.ActIII, ActIIIStartDay = state.Day };
            events.Emit(new ActAdvanced(CampaignAct.ActIII, deepestFloor));
            events.Emit(new ClimaxReached(deepestFloor));
        }

        if (arc.Act == CampaignAct.ActIII && state.Day >= arc.ActIIIStartDay + EndingDelayDays)
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
