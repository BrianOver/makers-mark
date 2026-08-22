using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace GodotClient.Ui;

/// <summary>
/// U-T2 Wave C (§11.14.4, Act II): a reusable "Bryn speaks a first-touch lesson" banner — the SAME
/// no-timer, non-gating toast contract <c>Panels.ForgePanel</c>'s own private mentor banner
/// established in Wave B, lifted into a standalone class so a SECOND caller (Wave C's two
/// dilemma lessons, "pricing as a decision" and "hold-or-sell") does not paste a second copy.
/// Owns no <see cref="Ui.TutorialFlow"/> reference itself (KTD2-adjacent: this is presentation
/// only) — a caller drives it entirely through <see cref="ShowFirstTouch"/>, which already carries
/// the <see cref="TutorialFlow.ConsumeFirstTouch"/> result.
///
/// <para><b>ForgePanel keeps its own Wave-B-era private copy for now</b> — de-duplicating it onto
/// this shared class is a follow-up, not a Wave C blocker (Wave B's own PR was in flight when this
/// was written; touching it again here would be scope creep on a unit that does not need it).</para>
///
/// <para><b>No timer, ever (law).</b> This banner carries NO countdown of its own — it stays up
/// until the player presses "Got it," however long that takes. The root and its inner containers
/// stay <see cref="Control.MouseFilterEnum.Ignore"/> (never blocks a click meant for whatever is
/// underneath); only the dismiss button itself accepts input — a toast, never a gating modal.</para>
/// </summary>
/// <summary>
/// U-T9-0 (§11.14.13): how much a line's moment is worth, so a loud night drops the right one.
///
/// <para>Measured over twelve seeds and ten days of <c>BaselinePlayer</c>: <b>day 4 lands four
/// course voices on eight of twelve seeds and five on the other four</b> — Act II, the first
/// attribution beat, the first fulfilled commission, the warrant ending at dawn, and on a third of
/// seeds the first hero death too. The banner's backlog caps at four. Before this rank existed the
/// queue dropped whatever arrived LAST, which on that night is most likely the proof — the one
/// sentence the whole course exists to deliver.</para>
///
/// <para>Two values, deliberately. A finer scale invites arguing about the middle; the only
/// distinction that has ever mattered here is "a tool explained itself" versus "the game just did
/// the thing it is about".</para>
/// </summary>
public enum MentorVoiceRank
{
    /// <summary>A tool, surface or mechanic explaining itself once — the first-touch tier. Valuable,
    /// re-readable in the Lessons book forever, and the right thing to lose on a crowded night.</summary>
    Lesson = 0,

    /// <summary>A beat on the sentence itself: the proof fired, a promise was kept, somebody died.
    /// These are the course, not its footnotes, and they outrank a tool tip for the screen.</summary>
    Act = 1,
}

public partial class MentorBanner : PanelContainer
{
    private Label _label = null!;

    /// <summary>Build the banner chrome once, hidden. Idempotent-guarded like every other
    /// code-built node on this project.</summary>
    public void Build()
    {
        if (_label is not null)
        {
            return;
        }

        Name = "MentorBanner";
        Visible = false;
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        // This class IS a PanelContainer, so dropping the wood override alone is not enough — it
        // would simply fall back to the theme's own panel style, which is opaque too. Measured by
        // capturing a frame with a lesson up: the world was still a flat sheet. An explicit empty box
        // is the only thing that makes a FullRect PanelContainer draw nothing.
        AddThemeStyleboxOverride("panel", new StyleBoxEmpty());

        var center = new CenterContainer { Name = "MentorBannerCenter", MouseFilter = MouseFilterEnum.Ignore };
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(center);

        // U-T9-12 (§11.14.13): the wood frame belongs on the CARD, not on this FullRect root.
        //
        // It used to be an override on the root itself, and `ui-frame-wood.png` is fully opaque at its
        // centre — measured (42, 36, 54, 255) — so every single lesson Bryn has ever spoken covered
        // the whole screen with a solid sheet until the player pressed "Got it". The docket lesson,
        // the pricing lesson, hold-or-sell, read-only-surfaces, quick-travel, the craft lessons — and
        // worst of all the PROOF, where the line explaining "that flash" hid the flash it was
        // explaining. A teacher who blanks the thing she is pointing at is not teaching.
        //
        // The root stays FullRect (the CenterContainer needs it to centre against the window) and
        // stays MouseFilter.Ignore, which was always the intent and is only now honest: a transparent
        // root means the controls a click passes through to are controls the player can actually see.
        var card = UiKit.Card("MentorBannerCard");
        card.AddThemeStyleboxOverride("panel", GameTheme.PanelStyleWood());
        center.AddChild(card);

        // U-T2 Wave D fix (WholeGameSweepTests): an unconstrained Label under a CenterContainer
        // sizes to its own UNWRAPPED natural width before AutowrapMode ever gets a width to wrap
        // against — on the smallest content window (1152x648, the sweep's own floor) a full-
        // sentence lesson pushed the card far wider than the window, and CenterContainer happily
        // centers a child larger than itself, overflowing both edges and (via the resulting
        // mis-sized VBoxContainer) shoving the Dismiss button below the visible window entirely —
        // a dead click on the one control that gets the player OUT of the lesson. Same fixed-width
        // idiom CommissionBoard/RaidForecastBoard already use for their own modal cards.
        var body = new VBoxContainer { Name = "MentorBannerBody", CustomMinimumSize = new Vector2(440, 0) };
        card.AddChild(body);

        _label = AddLabel(body, string.Empty);
        _label.Name = "MentorBannerText";
        _label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _label.HorizontalAlignment = HorizontalAlignment.Center;
        _label.CustomMinimumSize = new Vector2(440, 0);

        var dismiss = AddButton(body, "MentorBannerDismiss", "Got it", Dismiss);
        dismiss.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
    }

    /// <summary>
    /// Shows <paramref name="fired"/> (already-wrapped in Bryn's own voice, already-gated through
    /// <see cref="TutorialFlow.ConsumeFirstTouch"/> by the caller) — or does nothing when
    /// <paramref name="fired"/> is <see langword="null"/> (the lesson did not fire this call, per
    /// that engine's own once-ever contract).
    ///
    /// <para><b>The busy-guard used to DROP the second lesson, and this doc used to claim it
    /// "waits for a later call once the banner is free again". That was false, and the correction is
    /// the reason this method now holds a queue.</b> <see cref="TutorialFlow.ConsumeFirstTouch"/>
    /// marks an id fired and persists that BEFORE handing the copy here, so there is no later call:
    /// the id never fires again. What actually happened was that the second lesson lost its
    /// teachable moment — the one where the player has just done the thing it explains. (It was not
    /// lost from the game: <c>LessonsPanel</c> renders every id <c>FirstTouch.Fired</c> holds,
    /// forever. A lesson buried in a book the player has to go and open is worth a fraction of the
    /// same words arriving the instant they press the button, which is the whole premise of the
    /// first-touch tier existing at all.) Measured when this was found: of TWELVE call sites in
    /// <c>godot/scripts</c>, zero passed <paramref name="preempt"/>, and <c>ForgeMentorLessonsTests</c>
    /// carried a workaround comment for it ("free the banner slot for the mark-read lesson").</para>
    ///
    /// <para>So a lesson that arrives while the banner is busy is now QUEUED, and
    /// <see cref="Dismiss"/> drains it: the player's own "Got it" advances to the next one. Still no
    /// timer anywhere (law) — nothing appears or disappears except on a press.</para>
    ///
    /// <para><paramref name="preempt"/> (Wave D, generalized from the same insight ForgePanel's
    /// mark-read lesson needed in Wave B): lifts the busy-guard for a lesson whose own value
    /// outranks whatever generic orientation note happens to still be on screen. A currently-
    /// showing banner is ALWAYS an already-consumed lesson by construction — preempting it costs
    /// nothing the Lessons book has not already recorded permanently, it only reorders which of two
    /// lessons gets the screen first. Use for a SPECIFIC, actionable lesson (e.g. a live dilemma, or
    /// the lesson belonging to an act the player just performed) that can collide with a more
    /// generic "here is what this screen is" note fired moments earlier — not a default. With the
    /// queue in place the displaced note is not discarded either: it goes to the FRONT of the queue
    /// and is the next thing "Got it" shows.</para>
    ///
    /// <para>Returns whether <paramref name="fired"/> went on screen right now. A <see
    /// langword="false"/> return with a non-null <paramref name="fired"/> means queued, not dropped —
    /// callers that branch on it (<c>ForgePanel</c>'s material-ceiling/mark-read pair) still read
    /// correctly, since "did this one get the screen" is exactly the question they ask.</para>
    /// </summary>
    public bool ShowFirstTouch(string? fired, bool preempt = false, MentorVoiceRank rank = MentorVoiceRank.Lesson)
    {
        if (fired is null)
        {
            return false;
        }

        if (Visible && !preempt)
        {
            Enqueue(fired, rank);
            return false;
        }

        if (Visible)
        {
            // Preempting: the note being displaced has already been consumed and would otherwise be
            // dropped, so it takes the front of its own rank band rather than the bin. Front, not
            // back: it was fired first and it is the more general of the two, so it reads better
            // after the specific one than buried behind whatever arrives later.
            Enqueue(_label.Text, _currentRank, front: true);
        }

        _label.Text = fired;
        _currentRank = rank;
        Visible = true;
        return true;
    }

    /// <summary>The banner's own "Got it" — never a timer, always the player's own press (law: no
    /// timers on decisions). Drains one queued lesson if any is waiting, so the press advances
    /// through the backlog instead of discarding it; only an empty queue closes the banner.</summary>
    public void Dismiss()
    {
        if (_pending.Count > 0)
        {
            (_label.Text, _currentRank) = _pending[0];
            _pending.RemoveAt(0);
            return;
        }

        Visible = false;
    }

    /// <summary>How many consumed-but-not-yet-shown lessons are waiting behind the current one.
    /// Test/inspection surface, the same idiom as <c>ForgePanel.LastFocusedSection</c>.</summary>
    public int PendingLessonCount => _pending.Count;

    /// <summary>Lessons consumed while the banner was busy, in the order they will be shown —
    /// highest <see cref="MentorVoiceRank"/> first, insertion order within a rank.</summary>
    private readonly List<(string Text, MentorVoiceRank Rank)> _pending = new();

    /// <summary>The rank of whatever is on screen right now, so a preempted line re-enters the queue
    /// at its own rank rather than being demoted by the act of being displaced.</summary>
    private MentorVoiceRank _currentRank = MentorVoiceRank.Lesson;

    /// <summary>Ceiling on the backlog. Four is a deliberate, low number: a player facing a fifth
    /// stacked lesson is being lectured, not taught, and every one of them is still readable in the
    /// Lessons book — so past this point dropping is the kinder failure. What CHANGED in U-T9-0 is
    /// not the number but which line gets dropped when it is reached (see <see cref="Enqueue"/>).
    ///
    /// <para><b>The cap is reachable, and that is measured, not theoretical.</b> A twelve-seed,
    /// ten-day <c>BaselinePlayer</c> census counted the course voices that land on the same in-game
    /// day: <b>day 4 carries four on eight of twelve seeds and five on the other four</b> — Act II,
    /// the first attribution beat, the first fulfilled commission, the warrant's end at dawn, and on
    /// a third of seeds the first hero death as well. So the T9 course cannot treat the cap as an
    /// impossible edge, and a caller firing "in a batch" is not always a bug: sometimes the day
    /// genuinely is that loud. The other half of the answer belongs to the acts themselves — a beat
    /// whose night is already full arms for tomorrow instead of queueing (§11.14.13's priority rule,
    /// landing with U-T9-1) — because the honest fix for a loud night is fewer voices, not a longer
    /// queue.</para></summary>
    private const int MaxPendingLessons = 4;

    private void Enqueue(string lesson, MentorVoiceRank rank, bool front = false)
    {
        // Same text twice would read as a stutter on consecutive presses. Cannot normally happen
        // (ConsumeFirstTouch is once-ever per id) but the queue should not be the thing that makes
        // it possible if two ids ever share copy.
        if (lesson == string.Empty || _pending.Any(p => p.Text == lesson))
        {
            return;
        }

        // U-T9-0: at the cap, drop the LOWEST-RANKED waiting line rather than refusing the newest
        // arrival. Before this, a full queue silently dropped whatever came last — so on the
        // measured five-voice day 4 the line most likely to be lost was the one that arrived last,
        // and the course's most important sentence (the proof) is a late-evening beat. Refusing the
        // incoming line is only correct when the incoming line IS the least important one.
        if (_pending.Count >= MaxPendingLessons)
        {
            var weakest = _pending.Count - 1;
            for (var i = _pending.Count - 1; i >= 0; i--)
            {
                if (_pending[i].Rank <= _pending[weakest].Rank)
                {
                    weakest = i;
                }
            }

            if (_pending[weakest].Rank >= rank)
            {
                return; // nothing waiting is weaker than what just arrived — the arrival yields
            }

            _pending.RemoveAt(weakest);
        }

        // Ordered by rank, insertion order preserved inside a rank. `front` means the front of this
        // line's OWN band, never ahead of a higher-ranked one waiting behind it.
        var at = 0;
        while (at < _pending.Count && _pending[at].Rank > rank)
        {
            at++;
        }

        if (!front)
        {
            while (at < _pending.Count && _pending[at].Rank == rank)
            {
                at++;
            }
        }

        _pending.Insert(at, (lesson, rank));
    }

    /// <summary>Same small local widget-builders every other code-built panel on this project
    /// carries (<c>SimPanel</c>/<c>BestiaryPanel</c>/<c>CommissionBoard</c> precedent) — this class
    /// is a bare <see cref="PanelContainer"/>, not a <c>SimPanel</c>, so it does not inherit theirs.</summary>
    private static Label AddLabel(Node parent, string text)
    {
        var label = new Label { Text = text };
        parent.AddChild(label);
        return label;
    }

    private static Button AddButton(Node parent, string name, string text, Action onPressed)
    {
        var button = new Button { Name = name, Text = text };
        button.Pressed += onPressed;
        parent.AddChild(button);
        return button;
    }
}
