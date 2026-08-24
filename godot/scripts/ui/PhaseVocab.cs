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
    /// U3 (moved verbatim from <c>MainUi.BellVerb</c>), U1 (plan 2026-08-03-001, KTD-A "the two-bell
    /// day"): the contextual bell label — what pressing the HUD's one advance control does from the
    /// given phase.
    ///
    /// <para><b>Only Morning and Evening keep a real bell.</b> Expedition/Camp/ExpeditionDeep are the
    /// <see cref="RaidConductor"/>'s span now — the player has no phase-specific verb in any of the
    /// three (KTD-A's presentation contract: "a phase is player-operated iff the player has
    /// phase-specific verbs in it"), so the control reads as a skip-ahead, never a bell. The three
    /// retired labels — "Lower them into the mine", "Let them press deeper" / "Ring the return bell",
    /// "Close the vigil" — traced to three separate playtest complaints ("hitting 'lower them into
    /// the mine' brings them back to the town??", "return bell does nothing but moved it to 'deep'
    /// phase??", "not able to see the heroes in the mine") whose real cause was the STRUCTURE (a
    /// player-cranked bell over a span with nothing to decide), not any one label's wording — U1
    /// retires the structure, so no replacement label is owed for any of the three.</para>
    /// </summary>
    public static string BellVerb(DayPhase phase) => phase switch
    {
        DayPhase.Morning => "Send them off",
        DayPhase.Expedition or DayPhase.Camp or DayPhase.ExpeditionDeep => "Hurry the day along",
        DayPhase.Evening => "Snuff the lanterns",
        _ => "Advance",
    };

    /// <summary>
    /// State-aware overload: reads <paramref name="state"/>'s live phase. Every caller with a real
    /// <see cref="GameState"/> in hand goes through here (<c>MainUi</c>, <c>TutorialFlow</c>); the
    /// phase-only overload above exists for callers with no live state to ask — U2 (§11.14.14)
    /// added it so the pre-campaign "your first day" primer (<c>NewGameSelect</c>) can quote the
    /// SAME bell words the HUD will actually print, without fabricating a <see cref="GameState"/>
    /// just to read one field off it.
    /// </summary>
    public static string BellVerb(GameState state) => BellVerb(state.Phase);
}
