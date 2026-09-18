using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using GameSim.Advisor;
using GameSim.Contracts;

namespace GodotClient.Ui;

/// <summary>
/// P2-MEMORY-05 ("the idle line varies"): what Bryn says when the apprenticeship is done and the
/// player presses her anyway. Before this unit, <c>MainUi.OnStationActivated</c> answered every
/// such press with the exact same <see cref="MentorVoice.RestingLine"/>, forever — the same
/// "permanent fact re-presented as if it were news" shape <see cref="ObjectiveAdvisor"/>'s own U8
/// class doc names by count (1,287 fires for the memorial nag before that unit fixed it).
///
/// <para><b>Not a new pool of hand-authored near-synonyms.</b> The richest source of "what's
/// actually different right now" is <see cref="ObjectiveAdvisor.Suggest"/> — the SAME sim-decided,
/// state-derived reason <c>ObjectiveTracker</c>'s persistent HUD chip already renders, and the same
/// "attribute an existing sim reason to her, never re-derive one" idiom <c>MainUi.ReaskTutorial</c>
/// already uses for the tutorial's own top-slot text. Reusing it means Bryn's idle line varies
/// exactly when there is something to say — an unsold shelf piece, a hero stalled on a slot, an
/// open commission — and never invents a fact of its own.</para>
///
/// <para><b>Deterministic by construction.</b> <see cref="ObjectiveAdvisor.Suggest"/> draws no RNG
/// and reads no wall clock — same <see cref="GameState"/> in, same ranked list out — so this needs
/// no seed of its own: same state renders the same line, always.</para>
///
/// <para><b>U34 (§11, R25): a second fallback, ahead of the resting line.</b> When the advisor has
/// nothing live to say, this used to fall straight to <see cref="MentorVoice.RestingLine"/> — the
/// exact "nothing ever changes" gap R25 exists to close. <see cref="Line"/> now tries <see
/// cref="MentorVoice.NextObservation"/> first: same log, same rules, same "never an invented fact"
/// contract. <paramref name="alreadyTold"/> defaults to empty so every call site (and this class's
/// own tests) that predates this unit keeps behaving identically whenever the log is empty — the
/// caller passes the campaign's own persisted "told" ids to make a spoken observation retire for
/// real (<c>MainUi.DismissedIdleLine</c>'s own doc names exactly which table).</para>
/// </summary>
public static class MentorIdleVoice
{
    /// <summary>Bryn's line for a press while no tutorial step is active — the live top objective,
    /// spoken in her voice; failing that, a logged observation (U34, R25); failing that too, <see
    /// cref="MentorVoice.RestingLine"/> (the same honest-empty-state <see
    /// cref="GodotClient.Ui.ObjectiveTracker.NoObjectiveText"/> answers for its own HUD chip).
    /// </summary>
    public static string Line(GameState state, IReadOnlySet<string>? alreadyTold = null)
    {
        if (TopLiveObjective(state) is { } top)
        {
            return MentorVoice.Speak(ObjectiveTracker.Plain(top.Reason));
        }

        if (MentorVoice.NextObservation(state, alreadyTold ?? ImmutableHashSet<string>.Empty) is { } observation)
        {
            return MentorVoice.Speak(observation.Text);
        }

        return MentorVoice.CurrentLesson(null);
    }

    /// <summary>The live top objective <see cref="Line"/> would speak, or null when the advisor
    /// has nothing — exposed (deliberately typed <see cref="Suggestion"/>, never <c>bool</c>, so a
    /// text-scanning census elsewhere in this tree that discovers every bare
    /// <c>bool Name(GameState)</c> predicate does not also have to explain this delegating one-liner)
    /// so a caller that also needs to know WHETHER an observation was the thing actually spoken (to
    /// mark it told, and only then) never has to re-derive the advisor's own branch order by hand.
    /// </summary>
    public static Suggestion? TopLiveObjective(GameState state) => ObjectiveAdvisor.Suggest(state).FirstOrDefault();
}
