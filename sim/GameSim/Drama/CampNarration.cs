using System.Collections.Immutable;
using GameSim.Contracts;

namespace GameSim.Drama;

/// <summary>
/// U3 (C3, R3): the pure "what to print" half of the Camp reframe — extracted the same way
/// <c>GameSim.Cli.EventNarration</c> is (its own doc: "so the mapping is unit-testable"), so these
/// render decisions are pinned directly by a test rather than only reachable by parsing
/// Program.cs's stdout. Both members are pure projections over already-recorded state/events: no
/// mutation, no RNG draw (KTD-2 — the window-closed line and the fate attribution are DERIVED,
/// never a new event type).
/// </summary>
public static class CampNarration
{
    /// <summary>
    /// True when a party carried a live camp slate through this exact Camp tick and neither
    /// 'send' nor 'recall' landed on it — the checkpoint window closed untouched. Reads the
    /// <see cref="InFlightExpedition"/> exactly as it stands AFTER the Camp tick applied any queued
    /// <c>SendSupplyAction</c>/<c>RecallPartyAction</c> (MF-3: the existing InFlight seam is richer
    /// than any event here — no <c>PartyCampReport</c> narration case is added).
    /// </summary>
    public static bool WindowClosedUntouched(InFlightExpedition partyAfterCampTick) =>
        !partyAfterCampTick.Recalled && !partyAfterCampTick.SupplySent;

    /// <summary>
    /// Ties a returning hero's fate to the day's camp choice — but ONLY when that hero's party
    /// actually carried a live camp slate today, gated on <see cref="PartyCampReport"/> (the event
    /// <c>ExpeditionSystem</c> emits when a party parks below the checkpoint, MF-3). Returns null
    /// for a party that resolved in one stage-1 pass — never fabricates a camp story that didn't
    /// happen. <paramref name="dayEvents"/> is the calling day's slice of <c>GameState.EventLog</c>
    /// (<c>Day == day</c>); <paramref name="survived"/> comes straight off the Evening ledger's own
    /// <c>ReturnCard.Survived</c>, so a <see cref="HeroDied"/> lookup here would be redundant.
    /// </summary>
    public static string? Attribution(ImmutableList<GameEvent> dayEvents, HeroId hero, bool survived)
    {
        if (!dayEvents.OfType<PartyCampReport>().Any(r => r.Party.Contains(hero)))
        {
            return null;
        }

        var recalled = dayEvents.OfType<PartyRecalled>().Any(r => r.Party.Contains(hero));
        var supplied = dayEvents.OfType<SupplyDelivered>().Any(sd => sd.To == hero);

        return (recalled, supplied, survived) switch
        {
            (true, _, false) => "you rang the recall bell — it came too late",
            (true, _, true) => "you rang the recall bell — banked safe before it turned ugly",
            (_, true, false) => "you sent a runner with supplies — it wasn't enough",
            (_, true, true) => "the runner's supplies carried them through",
            (_, _, false) => "you held the checkpoint window — the depths took them anyway",
            _ => "you held the checkpoint window — they pushed on and made it",
        };
    }
}
