using System.Collections.Generic;

namespace GodotClient.Ui;

/// <summary>
/// U-T2-7 (Wave A substrate, §11.14.4): "a first-touch lesson fires the FIRST time an action
/// becomes reachable, once ever, and then lives in the Lessons book." This is the whole engine —
/// deliberately tiny, deliberately generic, and deliberately Godot-free (KTD2: plain data, no <see
/// cref="Godot.Node"/>, no clock, no RNG) so <see cref="TutorialFlowTests"/> and a bare unit test can
/// both exercise it without mounting a scene.
///
/// <para><b>Why generic, not a fourth hand-rolled flag.</b> <see cref="TutorialFlow"/> already has
/// three copies of this exact "once ever, never again" shape (<c>HasSeenLedgerTip</c>,
/// <c>_hasSeenWarrantEndBeat</c>, <c>_firstLossDay</c>), each its own bespoke bool/int field plus its
/// own persistence lines plus its own consume method. Wave E's long tail names AT LEAST five more
/// actions that need the identical shape (the Foundry's four verbs, reforge, the read-only surfaces,
/// the HUD chips including quick travel). Copying the pattern a fourth, fifth, sixth... time is
/// exactly the sprawl this repo's own standing complaint is about; one keyed engine instead.</para>
///
/// <para><b>The anti-nag pin — this is the whole point of the class.</b> This repo has already
/// shipped a 1287x memorial nag to the owner (KTD-H): a surface that was SUPPOSED to fire once and
/// in practice fired on every tick, because nothing durable ever recorded that it already had.
/// <see cref="Consume"/>'s contract is therefore load-bearing, not cosmetic: calling it twice with
/// the SAME <paramref name="id"/> — in the same session, or a thousand sessions apart via <see
/// cref="Fired"/> being reloaded through the constructor — returns non-null exactly once, ever.
/// <see cref="FirstTouchLessonsTests"/> proves this by calling one id a four-digit number of times
/// and counting the non-null answers, rather than trusting a comment.</para>
///
/// <para><b>Pure bookkeeping only.</b> This class has no idea what "reachable" means for any given
/// action — the caller decides that (e.g. "the second-profession button just turned visible") and
/// is expected to call <see cref="Consume"/> only once it already has. A caller that polled this
/// every frame regardless of reachability would still only ever get one non-null answer per id, but
/// the game's own no-nag law is enforced by the CALLER gating on reachability first, not by this
/// class guessing at timing.</para>
/// </summary>
public sealed class FirstTouchLessons
{
    private readonly Dictionary<string, string> _fired;

    /// <summary>
    /// <paramref name="seed"/> replays a prior campaign's already-fired ids (<see
    /// cref="TutorialFlow.Load"/>'s own call) — every id in it counts as already consumed, so <see
    /// cref="Consume"/> can never re-fire one across a reload. <see langword="null"/> (a save from
    /// before this class existed, or a fresh campaign) starts with nothing fired — safe, never a
    /// false fire.
    /// </summary>
    public FirstTouchLessons(IReadOnlyDictionary<string, string>? seed = null)
    {
        _fired = seed is null ? new Dictionary<string, string>() : new Dictionary<string, string>(seed);
    }

    /// <summary>Every id that has fired at least once, paired with the exact text it fired with —
    /// <c>LessonsPanel</c>'s own permanent record (KTD2: plain data — no <see cref="Godot.Node"/>
    /// reachable through this dictionary), and what <see cref="TutorialFlow.Save"/> persists
    /// verbatim.</summary>
    public IReadOnlyDictionary<string, string> Fired => _fired;

    /// <summary>
    /// Records <paramref name="lessonText"/> against <paramref name="id"/> and returns it — but
    /// ONLY the first time this exact id is ever passed here (class doc's anti-nag pin). Every call
    /// after that returns <see langword="null"/>, and the ORIGINAL text is what <see cref="Fired"/>
    /// keeps forever — a second call with different text for an id that already fired is silently
    /// ignored, never overwrites (the book's own record of what was actually taught must not shift
    /// under it after the fact).
    /// </summary>
    public string? Consume(string id, string lessonText)
    {
        if (_fired.ContainsKey(id))
        {
            return null;
        }

        _fired[id] = lessonText;
        return lessonText;
    }

    /// <summary>Whether <paramref name="id"/> has already fired — read-only inspection, never
    /// consumes (use <see cref="Consume"/> to actually fire one).</summary>
    public bool HasFired(string id) => _fired.ContainsKey(id);
}
