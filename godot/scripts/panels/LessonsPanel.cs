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

        var currentStep = Tutorial is { Active: true } ? Tutorial.Step : (TutorialStep?)null;

        // U-T2-1 (owner ruling): chapters, not one flat countdown — grouped by Act first so the
        // book reads as "The Mark", then "The Hand-Off"'s own four lessons together, then "The
        // Dark", then "The Memory", rather than the registry's own chronological DisplayIndex order
        // (which interleaves acts across the three in-game days). Each row's own numbering is
        // act-scoped too (TutorialFlow.ActPosition), matching the card's own "{Act} · N of M" prefix
        // exactly — the book and the card can never disagree about which chapter a step belongs to.
        foreach (var def in TutorialFlow.Registry
                     .OrderBy(d => (int)d.Act).ThenBy(d => d.DisplayIndex).ThenBy(d => (int)d.Step))
        {
            var isCurrent = currentStep == def.Step;
            var card = Card($"Lesson_{def.DisplayIndex}_{def.Step}");
            _content!.AddChild(card);

            var body = new VBoxContainer();
            card.AddChild(body);

            var titleRow = AddRow(body);
            var marker = isCurrent ? "◆" : "○"; // filled diamond / hollow circle — same glyphs ObjectiveTracker's checklist uses
            var (position, total) = TutorialFlow.ActPosition(def.Step);
            var title = AddLabel(
                titleRow, $"{marker} {TutorialActVocab.DisplayName(def.Act)} · {position} of {total} — {def.ShortLabel}");
            if (isCurrent)
            {
                title.AddThemeColorOverride("font_color", GameTheme.WarnColor);
            }

            AddLabel(body, def.TeachNote);
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
            AddLabel(body, lossLesson);
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
