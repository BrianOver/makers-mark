using System.Collections.Generic;
using System.Linq;
using Godot;
using GodotClient.Ui;

namespace GodotClient.Panels;

/// <summary>
/// U2 (tutorial-revamp plan, §11.13): the Lessons book — all ten <see
/// cref="TutorialFlow.Registry"/> rows' <see cref="TutorialStepDef.ShortLabel"/> +
/// <see cref="TutorialStepDef.TeachNote"/>, rendered at full height, PERMANENTLY, surviving both
/// <see cref="TutorialFlow.Dismiss"/> and <see cref="TutorialFlow.Completed"/>. Before this unit
/// the ten teaching paragraphs existed only inside <see cref="ObjectiveTracker"/>'s own checklist —
/// a "peek-and-scroll sliver" that showed exactly one TeachNote (the current step's) and vanished
/// the instant the chain was dismissed or finished. The teaching moves OUT of that card and into
/// this book; the card's own job narrows to pointing.
///
/// <para>Mirrors <see cref="DemandPanel"/>'s read-only <see cref="SimPanel"/>/Section/Card idiom —
/// no posting form, no queued action, pure presentation over <see cref="TutorialFlow.Registry"/>'s
/// own static data (KTD2: no sim change, no RNG, no mutation). <see cref="Tutorial"/> is the one
/// piece of live state this panel reads beyond the registry itself — which row counts as "current"
/// — set once by <c>MainUi</c> right after <see cref="TutorialFlow"/> is built (mirrors <see
/// cref="CommissionBoard"/>/<see cref="LegendsWall"/>'s own "needs more than just the adapter"
/// precedent).</para>
/// </summary>
public partial class LessonsPanel : SimPanel
{
    /// <summary>
    /// U5 (§11.14.14, "teaching surfaces render their own copy"): a human title for every
    /// first-touch id the game actually fires. <see cref="TutorialFlow.FirstTouch"/>'s own <see
    /// cref="FirstTouchLessons.Fired"/> table carries only the id (its bookkeeping key) and the
    /// fired lesson text — deliberately: <see cref="FirstTouchLessons"/>'s class doc says outright
    /// "this class has no idea what 'reachable' means for any given action... pure bookkeeping
    /// only", and a display title is exactly the presentation knowledge it disclaims. Before this
    /// table, the book headed every fired first-touch card with the raw id string verbatim ("◆
    /// the-proof-taught") — the internal kebab-case bookkeeping key, never a word a person wrote,
    /// in the one surface a player who never reads source is guaranteed to see it.
    ///
    /// <para>An id missing here falls back to itself (<see cref="FirstTouchTitle"/>) rather than
    /// throwing — a test fixture's own throwaway id (e.g. <c>LessonsPanelTests</c>' "test-overwrite")
    /// has no real title and was never meant to, so a hard failure belongs to the GUARD, not the
    /// render path: <c>LessonsPanelFirstTouchTitleTests</c> source-scans every <c>.cs</c> file under
    /// <c>res://scripts</c> (the same idiom <c>TeachingCoverageCensusTests.FirstTouchIdIsWiredInSource</c>
    /// already uses) for every id ANY live <c>ConsumeFirstTouch</c>/<c>ShowMentorFirstTouch</c> call
    /// site names, and fails BY ID for any one missing from this table — so a new Wave-E-style
    /// long-tail lesson that forgets its title is caught the day it ships, never silently, the
    /// "128 untested assets" shape this repo has already been bitten by once.</para>
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> FirstTouchTitles = new Dictionary<string, string>
    {
        // U30 (§11.14.14): "the-proof-taught" moved off this ConsumeFirstTouch-backed table — the
        // Proof act is now a dormant act with its own arm-day field (TutorialFlow.ConsumeProofBeat),
        // the same shape the loss act already uses, and this book's own trailing "Lesson_Proof"
        // card (below) is its permanent record instead. A stale entry here would fail
        // LessonsPanelTests.EveryLiveFirstTouchId_HasANonSlugTitleInTheCatalog — no live call site
        // names this id anymore.
        ["read-only-surfaces"] = "Nothing here is a button",
        ["tomorrow-at-the-counter"] = "Tomorrow's counter",
        ["quick-travel-unlocked"] = "Quick travel unlocked",
        ["second-profession-picked"] = "A second profession",
        ["hold-or-sell"] = "Hold it, or sell it",
        ["the-mark-read"] = "The mark, read",
        ["legends-wall-taught"] = "The town's memory",
        ["honor-memorial"] = "The farewell rite",
        ["reforge-heirloom"] = "Reforging an heirloom",
        ["forecast-board-taught"] = "Tomorrow's forecast",
        ["the-muster-speaks"] = "What the muster shows",
        ["pricing-as-a-decision"] = "Pricing is a decision",
        ["material-ceiling-hand-band"] = "The material sets the ceiling",
        ["forge-act1-shaping"] = "The forge, shaping",
        ["forge-act2-quench"] = "The forge, the quench",
        ["alchemy-brew"] = "Brewing the reagents",
        ["engineering-assembly"] = "Assembling the parts",
        ["tanning-frame"] = "Working the tanning frame",
        ["first-talent-unlock"] = "Unlocking a talent",
        ["foundry-four-verbs"] = "The Foundry's four verbs",
        ["the-tariff-fork"] = "Whose ore you buy",
        // P2-SCREEN-07: the three lessons split back out of BuyMaterial's own bolted-on TeachNote
        // paragraph — see TutorialFlow's own SlotBudgetLessonId/StationPressLessonId/
        // LeavingARoomLessonId doc.
        [TutorialFlow.SlotBudgetLessonId] = "The day's action slots",
        [TutorialFlow.StationPressLessonId] = "Stations, and pressing E",
        [TutorialFlow.LeavingARoomLessonId] = "Leaving a room",
    };

    /// <summary>Copy for <paramref name="id"/>'s card heading — see <see cref="FirstTouchTitles"/>'s
    /// own doc for why an unrecognized id falls back to itself instead of throwing.</summary>
    private static string FirstTouchTitle(string id) =>
        FirstTouchTitles.TryGetValue(id, out var title) ? title : id;

    private VBoxContainer? _content;

    /// <summary>The live chain, for marking the current row — null only before <c>MainUi</c> wires
    /// it (defensive; every real mount sets this immediately after <see cref="TutorialFlow.Build"/>).
    /// Reading <see cref="TutorialFlow.Step"/> here is safe regardless of <see
    /// cref="TutorialFlow.Active"/> — the book shows ALL ten rows either way; only the "current"
    /// marker depends on the chain still being active.</summary>
    public TutorialFlow? Tutorial { get; set; }

    public override void _Ready() => EnsureBuilt();

    public override void Refresh()
    {
        EnsureBuilt();
        Clear(_content!);

        AddHeader(_content!, "The Lessons Book");
        AddLabel(
            _content!,
            "Every lesson this campaign has to teach, in order. It stays here whether the tutorial "
            + "is running, dismissed, or already finished — nothing taught is ever taken back.");

        // P2-SCREEN-06 (the card diet): the four-state row (done/current/skipped/upcoming) that
        // used to live in ObjectiveTracker's own 75px checklist scroll moves here, permanently —
        // keyed by DISPLAYED slot (TutorialFlow.Checklist's own contract: BuyMaterial/Craft share
        // slot 1, so both cards below read the identical state). Null only when the panel is
        // mounted with no live Adapter/Tutorial at all (defensive; every real mount has both) —
        // every registry row then falls back to the "upcoming" glyph rather than throwing.
        var rowsByDisplayIndex = Adapter is not null && Tutorial is not null
            ? Tutorial.Checklist(Adapter.CurrentState).ToDictionary(r => r.DisplayIndex)
            : null;

        // U-T2-1 (owner ruling): chapters, not one flat countdown — grouped by Act first so the
        // book reads as "The Mark", then "The Hand-Off"'s own four lessons together, then "The
        // Dark", then "The Memory", rather than the registry's own chronological DisplayIndex order
        // (which interleaves acts across the three in-game days). Each row's own numbering is
        // act-scoped too (TutorialFlow.ActPosition), matching the card's own "{Act} · N of M" prefix
        // exactly — the book and the card can never disagree about which chapter a step belongs to.
        //
        // P2-ONBOARD-06 (§11.15), deletion #5: grouped by DISPLAYED slot, one card per slot — not
        // one card per raw TutorialStep. Before this unit, BuyMaterial and Craft (which share
        // DisplayIndex 1 — TutorialStepDef's own doc) each got their own card, so the book showed
        // two "◆ The Mark · 1 of 1" cards in a row for what the player experiences as ONE step.
        // TutorialFlow.Checklist already dedupes this exact way (its own `seen.Add(def.DisplayIndex)`
        // guard) for the checklist's card-diet reading; this loop now matches it. The slot's header
        // reads the FIRST member's own Act/ShortLabel (BuyMaterial's — Registry declaration order —
        // whose own ShortLabel, "Buy material, then craft your first item," already names both
        // halves), and the body renders every member's TeachNote as its own paragraph, so no lesson
        // this book used to hold two cards' worth of text for is lost to the merge.
        foreach (var group in TutorialFlow.Registry
                     .OrderBy(d => (int)d.Act).ThenBy(d => d.DisplayIndex).ThenBy(d => (int)d.Step)
                     .GroupBy(d => d.DisplayIndex))
        {
            var def = group.First();
            var row = rowsByDisplayIndex?.GetValueOrDefault(def.DisplayIndex);
            var isCurrent = row?.Current ?? false;
            var isDone = row?.Done ?? false;
            // The honest third state (ChecklistRow.Skipped's own doc): a row the chain carried the
            // player PAST without it ever being genuinely answered. The only place the game admits
            // a step never came up — losing this in the move off the card would be a regression in
            // honesty, not just layout, so it is checked BEFORE Done/Current, same order the old
            // checklist used.
            var isSkipped = row?.Skipped ?? false;
            var card = Card($"Lesson_{def.DisplayIndex}_{def.Step}");
            _content!.AddChild(card);

            var body = new VBoxContainer();
            card.AddChild(body);

            var titleRow = AddRow(body);
            // ✓ done / ◆ current / — skipped / ○ still upcoming — same four glyphs and the same
            // "didn't come up this time" suffix ObjectiveTracker's own (now-deleted) checklist used.
            var marker = isSkipped ? "—" : isDone ? "✓" : isCurrent ? "◆" : "○";
            var suffix = isSkipped ? "  — didn't come up this time" : string.Empty;
            var (position, total) = TutorialFlow.ActPosition(def.Step);
            var title = AddLabel(
                titleRow,
                ObjectiveTracker.Plain(
                    $"{marker} {TutorialActVocab.DisplayName(def.Act)} · {position} of {total} — {def.ShortLabel}{suffix}"));
            if (isCurrent)
            {
                title.AddThemeColorOverride("font_color", GameTheme.WarnColor);
            }
            else if (isDone || isSkipped)
            {
                title.AddThemeColorOverride("font_color", GameTheme.TextDim);
            }

            // U5 (§11.14.14): this copy is shared with the CLI, where **bold** is meaningful — a
            // Godot Label has no markup parser, so before this fix a TeachNote carrying emphasis
            // (e.g. OpenCounter's own "**Present** a shelved item...") rendered the asterisks
            // literally in this permanent record. ObjectiveTracker.Plain is the SAME strip the
            // tutorial card and checklist already apply to the identical strings; this panel had
            // simply never called it. One label per group member, so a shared slot (BuyMaterial +
            // Craft) still carries both TeachNotes, each its own paragraph.
            foreach (var member in group)
            {
                AddLabel(body, ObjectiveTracker.Plain(member.TeachNote));
            }
        }

        // §11.13 amendment (U6): the loss lesson — not one of the ten registry rows (it is dormant
        // until the campaign's first HeroDied, which can land well after the chain itself finished),
        // so it gets its own trailing card, appended only once TutorialFlow.LossLessonText has
        // something to show. "Re-reading beats re-running" (U2's own answer): this card outlives the
        // checklist row's own two-day visible window (TutorialFlow.LossActRow) forever.
        if (Tutorial?.LossLessonText is { } lossLesson)
        {
            var card = Card("Lesson_Loss");
            _content!.AddChild(card);

            var body = new VBoxContainer();
            card.AddChild(body);

            AddLabel(body, "◆ The first loss").AddThemeColorOverride("font_color", GameTheme.WarnColor);
            AddLabel(body, ObjectiveTracker.Plain(lossLesson));
        }

        // U30 (§11.14.14): the proof lesson — the Proof act's own trailing card, same "not one of
        // the ten registry rows, dormant until its own fact lands" shape as the loss lesson just
        // above (it is dormant until the campaign's first AttributionBeatEvent, which can land
        // before OR after the numbered chain finishes). ProofLessonText is RAW/unattributed (see
        // its own doc); this book wraps it in MentorVoice.Speak so it reads exactly like every
        // other Bryn-voiced first-touch card below, since it used to BE one before this unit moved
        // it onto the dormant-act mechanism.
        if (Tutorial?.ProofLessonText is { } proofLesson)
        {
            var card = Card("Lesson_Proof");
            _content!.AddChild(card);

            var body = new VBoxContainer();
            card.AddChild(body);

            AddLabel(body, "◆ The proof, explained").AddThemeColorOverride("font_color", GameTheme.WarnColor);
            AddLabel(body, ObjectiveTracker.Plain(MentorVoice.Speak(proofLesson)));
        }

        // U-T2-7 (Wave A substrate, §11.14.4): the first-touch tier's own permanent record — every
        // id TutorialFlow.ConsumeFirstTouch has ever fired, rendered forever ("re-reading beats
        // re-running", same precedent as the loss lesson above and every registry row before it).
        // Empty today (Wave A ships the engine; Wave E's own units are its first real callers), so
        // this loop renders nothing yet on a fresh campaign — LessonsPanelTests proves it renders
        // once something HAS fired.
        if (Tutorial is { } firstTouchOwner)
        {
            foreach (var (id, lessonText) in firstTouchOwner.FirstTouch.Fired)
            {
                var card = Card($"Lesson_FirstTouch_{id}");
                _content!.AddChild(card);

                var body = new VBoxContainer();
                card.AddChild(body);

                // U5 (§11.14.14): the card used to head itself with the raw bookkeeping id
                // ("◆ the-proof-taught") — see FirstTouchTitles' own doc for why the title is
                // looked up from copy instead. The text gets the same bold-strip every other card
                // in this book applies (see the registry loop above) — first-touch lessonText is
                // authored the same way TeachNote is and can carry the same markup.
                AddLabel(body, $"◆ {FirstTouchTitle(id)}").AddThemeColorOverride("font_color", GameTheme.WarnColor);
                AddLabel(body, ObjectiveTracker.Plain(lessonText));
            }
        }
    }

    private void EnsureBuilt()
    {
        if (_content is not null)
        {
            return;
        }

        var body = BuildScrollBody();
        _content = new VBoxContainer { Name = "LessonsContent" };
        body.AddChild(_content);
    }
}
