using GameSim.Contracts;

namespace GodotClient.Ui;

/// <summary>
/// U2 (playtest-three plan, KTD-B): the ONE place a <see cref="DayPhase"/> becomes player-facing
/// text. Before this table existed, three surfaces each decided independently what to call the
/// same sim moment: <c>MainUi</c> named it "Quest — Vigil" on the HUD banner, <c>ObjectiveTracker</c>
/// 's day timeline printed the raw enum name ("Camp", "Deep") right above it, and
/// <c>NewGameSelect</c>'s continue blurb lowercased <c>state.Phase.ToString()</c> verbatim ("camp
/// of day 5" / "expeditiondeep of day 12"). A player reading all three at once was reading a
/// split-brained game. Every renderer of a phase now goes through here instead.
///
/// <para>The sim enum itself (<c>Contracts/</c>, deny-listed) is never renamed — "Camp" the
/// MECHANIC keeps its internal name and the save envelope keeps storing
/// <c>Phase.ToString()</c> verbatim (format untouched); only the rendered word changes, here.</para>
/// </summary>
public static class PhaseVocab
{
    /// <summary>
    /// The context-free word for a phase — used wherever there is no live <see cref="GameState"/>
    /// to ask about a sub-state: the day timeline's segment labels (<c>ObjectiveTracker</c>) and a
    /// resumed save's summary line (<c>NewGameSelect</c>, via <see cref="Display(string)"/>) both
    /// read this table. Morning's live Dawn/Prepare split needs the counter's own state (see <see
    /// cref="Display(GameState)"/>), so out of context it collapses to its resting value, Dawn.
    /// </summary>
    public static string Display(DayPhase phase) => phase switch
    {
        DayPhase.Morning => "Dawn",
        DayPhase.Expedition => "Quest",
        DayPhase.Camp => "Vigil",
        DayPhase.ExpeditionDeep => "Deep Vigil",
        DayPhase.Evening => "Night",
        _ => phase.ToString(),
    };

    /// <summary>
    /// The live HUD banner's word (was <c>MainUi.PlayerPhaseName</c>): the same table as <see
    /// cref="Display(DayPhase)"/>, except Morning splits into "Prepare" (a counter session is open
    /// — <see cref="CounterState.Closed"/> false) vs "Dawn" (no session yet, or already closed).
    /// </summary>
    public static string Display(GameState state) => state.Phase switch
    {
        DayPhase.Morning => state.Counter is { Closed: false } ? "Prepare" : "Dawn",
        _ => Display(state.Phase),
    };

    /// <summary>
    /// The save envelope's stored form (<c>CampaignSave.Envelope.Phase</c>, always
    /// <c>DayPhase.ToString()</c> — KTD-B: format untouched) rendered back into the same
    /// vocabulary for <c>NewGameSelect</c>'s continue blurb. A string that doesn't parse as a <see
    /// cref="DayPhase"/> (a foreign or corrupted save) degrades to itself rather than throwing —
    /// losing the pretty word is better than losing the continue screen.
    /// </summary>
    public static string Display(string phaseName) =>
        System.Enum.TryParse<DayPhase>(phaseName, out var phase) ? Display(phase) : phaseName;

    /// <summary>
    /// U3 (moved verbatim from <c>MainUi.BellVerb</c>): the contextual bell label — what ringing it
    /// does from the current phase, state-aware rather than phase-only.
    ///
    /// <para><b>Camp must never say "return bell".</b> That verb belongs to
    /// <c>RecallPartyAction</c>, a real and different Camp action (<c>CampPanel</c> /
    /// <c>CampHandlers.ApplyRecall</c>) which banks the haul and surfaces the party. The phase bell
    /// at Camp does the OPPOSITE — it sends the party to the deep floors. Two controls one click
    /// apart cannot share a name while doing opposite things (three playtest complaints traced to
    /// exactly this: "hitting 'lower them into the mine' brings them back to the town??", "return
    /// bell does nothing but moved it to 'deep' phase??", "not able to see the heroes in the
    /// mine").</para>
    /// </summary>
    public static string BellVerb(GameState state) => state.Phase switch
    {
        DayPhase.Morning => "Send them off",
        // Was "Lower the winch" — internal winch-house vocabulary that leaked onto a button and
        // read as "lower the wench" at a glance. A label has to say what pressing it does.
        DayPhase.Expedition => "Lower them into the mine",
        DayPhase.Camp => AnyoneBelow(state) ? "Let them press deeper" : "Close the vigil",
        DayPhase.ExpeditionDeep => AnyoneBelow(state) ? "Ring the return bell" : "Close the vigil",
        DayPhase.Evening => "Snuff the lanterns",
        _ => "Advance",
    };

    /// <summary>True while a party is parked below the checkpoint awaiting stage-2 resolution —
    /// the only state in which the Camp/Deep phases have anything to be about. <c>InFlight</c> is
    /// populated by the Expedition tick and cleared by <c>ExpeditionDeepSystem</c>, so it is
    /// exactly "is anyone down there right now".</summary>
    private static bool AnyoneBelow(GameState state) => !state.InFlight.IsEmpty;
}
