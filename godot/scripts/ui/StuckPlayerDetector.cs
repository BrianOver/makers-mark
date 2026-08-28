using System.Collections.Generic;

namespace GodotClient.Ui;

/// <summary>
/// U19 (§11.14.14, R32): "a player who is stuck, idle, or repeatedly refused is offered help once,
/// without a nag." Every aid this game offered before this unit keyed off GAME STATE — the
/// checklist's GatingNote, the overlay's pulse, the toast's friendly wording — and none of it
/// observed the PLAYER: stand still and the same pulsing outline pulses forever; bounce off the same
/// legality gate three times and the toast repeats the identical line a third time with nothing
/// escalated (the raw reason WAS already being recorded, but only to the dev log —
/// <c>MainUi.OnPhaseCompleted</c>'s own <c>EngineDistress.Warn</c> call, which the player never
/// sees). This class is the bookkeeping half of the fix — deliberately tiny and Godot-free (KTD2: no
/// <see cref="Godot.Node"/>, no clock read of its own; <c>MainUi._Process</c> hands it the delta), the
/// same "pure bookkeeping, the caller decides what the facts mean" shape <see
/// cref="FirstTouchLessons"/> already established.
///
/// <para><b>Two independent detectors, one class</b> — they answer the same question ("has the
/// player been doing anything?") from opposite directions: nothing happening for a while (idle) versus
/// the same wrong thing happening repeatedly (refused). <c>MainUi</c> already owns exactly one
/// <c>_Process</c> heartbeat to drive both from, so one small class beside it beats two.</para>
///
/// <para><b>Law 2 — no timers on decisions.</b> <see cref="TickIdle"/> IS a clock, and that is legal:
/// it decides only WHEN an unprompted offer of help appears, never what the player may do, whether a
/// choice stays open, or what waiting costs. The offer itself always lands on <c>MentorBanner</c> —
/// the same no-timer toast that stays up until the player's own "Got it" press, never a countdown of
/// its own (that class's own doc). Nothing here ever shortens a window, blocks an input, or makes a
/// decision more expensive for having waited on it.</para>
///
/// <para><b>The anti-nag PIN is not this class's job.</b> Both <see cref="TickIdle"/> (edge-triggered
/// — fires <see langword="true"/> at most once per <see cref="ResetIdle"/>) and <see
/// cref="RegisterRefusal"/> (a plain running count) are honest but forgetful: restart the process and
/// both start counting over. The PERMANENT "never repeats this exact offer again, for the life of the
/// campaign" guarantee is <c>TutorialFlow.ConsumeFirstTouch</c>'s — the repo's own scarred-in,
/// proven-not-promised 1287x-memorial-nag engine — which <c>MainUi</c> gates every promotion through
/// before it ever reaches <c>MentorBanner</c>. That is deliberate layering, not a gap: this class only
/// ever needs to be right for the CURRENT session (has it been quiet in front of THIS step; has THIS
/// refusal come up three times), so the once-ever contract keeps living in the one place that already
/// owns it, never duplicated into a second copy here.</para>
/// </summary>
public sealed class StuckPlayerDetector
{
    private double _idleSeconds;
    private bool _idleOffered;

    /// <summary>
    /// Advance the idle clock by <paramref name="delta"/> unscaled wall-clock seconds (<c>MainUi.
    /// _Process</c>'s own units — KTD6: wall-clock lives in the adapter, never the sim) and report
    /// whether idleness JUST crossed <paramref name="thresholdSeconds"/> — <see langword="true"/>
    /// exactly once per <see cref="ResetIdle"/>, mirroring the edge-triggered "fire once when a
    /// counting gate crosses its line" idiom <c>MainUi.LedgerDelayRemaining</c> already uses for the
    /// Return Ritual (counting down to zero there; counting up to a floor here). Calling this every
    /// frame past the threshold is cheap and safe — it keeps returning <see langword="false"/> until
    /// the next reset, so a caller never has to remember to stop asking.
    /// </summary>
    public bool TickIdle(double delta, double thresholdSeconds)
    {
        if (_idleOffered)
        {
            return false;
        }

        _idleSeconds += delta;
        if (_idleSeconds < thresholdSeconds)
        {
            return false;
        }

        _idleOffered = true;
        return true;
    }

    /// <summary>
    /// The player did something real. <c>MainUi</c> calls this from the ONE choke point every
    /// submitted <c>PlayerAction</c> reaches (<c>SimAdapter.Queue</c>'s own doc), immediate or
    /// bell-deferred alike, and again whenever <c>TutorialFlow.Step</c> itself moves — the day-1
    /// muster and the two UI-navigation-only steps (LookIn/MeetHeroes) can advance the chain with no
    /// <c>PlayerAction</c> submitted at all, and a step that just changed is proof of progress by
    /// itself. Clears both the accumulated idle time and the one-shot <see cref="TickIdle"/> latch, so
    /// the next stretch of genuine idleness gets its own fair shot at the threshold.
    /// </summary>
    public void ResetIdle()
    {
        _idleSeconds = 0;
        _idleOffered = false;
    }

    /// <summary>Every distinct friendly-refusal text seen so far this session, and how many times.
    /// Keyed by <c>MainUi.FriendlyRejection</c>'s own OUTPUT — never the raw kernel reason — so this
    /// counts "the same wrong thing happening" the way the PLAYER experiences it on screen, not the
    /// way the kernel's internal reason string happens to be spelled today.</summary>
    private readonly Dictionary<string, int> _refusalCounts = new();

    /// <summary>
    /// Records one more occurrence of <paramref name="friendlyText"/> and returns the new running
    /// count. Never resets on its own (unlike <see cref="ResetIdle"/>): a refusal that recurs after
    /// other, unrelated actions in between is still the SAME refusal recurring, and R32 asks for help
    /// on the third OCCURRENCE, not the third CONSECUTIVE one. The caller (<c>MainUi.
    /// OnPhaseCompleted</c>) reads the returned count directly rather than keeping a duplicate counter
    /// of its own, and decides what "three" means (promote to the banner) — this class only counts.
    /// </summary>
    public int RegisterRefusal(string friendlyText)
    {
        _refusalCounts.TryGetValue(friendlyText, out var count);
        count++;
        _refusalCounts[friendlyText] = count;
        return count;
    }
}
