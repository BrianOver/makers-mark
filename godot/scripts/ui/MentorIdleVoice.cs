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
/// </summary>
public static class MentorIdleVoice
{
    /// <summary>Bryn's line for a press while no tutorial step is active — the live top objective,
    /// spoken in her voice, or <see cref="MentorVoice.RestingLine"/> when nothing is actually
    /// happening (the same honest-empty-state <see cref="GodotClient.Ui.ObjectiveTracker.NoObjectiveText"/>
    /// answers for its own HUD chip).</summary>
    public static string Line(GameState state)
    {
        var top = ObjectiveAdvisor.Suggest(state).FirstOrDefault();
        return top is null
            ? MentorVoice.CurrentLesson(null)
            : MentorVoice.Speak(ObjectiveTracker.Plain(top.Reason));
    }
}
