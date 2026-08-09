using System.Collections.Generic;
using GameSim.Advisor;
using GameSim.Contracts;
using Godot;

namespace GodotClient.Ui;

/// <summary>
/// World-rework U18 (R11, KTD9/KTD13): the persistent "what do I do now" HUD chip — docks
/// top-right below the header (KTD13). Renders <see cref="ObjectiveAdvisor.Suggest"/>'s top pick
/// plus its reason; the row expands to the full ranked list on demand. <see cref="Refresh"/> is
/// a pure Clear-then-compose pass over a <see cref="GameState"/> snapshot (KTD2 — no sim contact,
/// no mutation) and is called by the owner (<c>MainUi</c>) on every phase tick, never per frame
/// (U18 approach) — <see cref="ObjectiveAdvisor.Suggest"/> only needs to run when the state it
/// projects over could have changed.
/// </summary>
public sealed partial class ObjectiveTracker : PanelContainer
{
    /// <summary>Shown when <see cref="ObjectiveAdvisor.Suggest"/> returns nothing productive —
    /// the destitution floor (<c>DestitutionRecoverySystem</c>) resolves it next Morning without
    /// player input, so this is a calm line, not an error.</summary>
    public const string NoObjectiveText = "Nothing urgent right now — the town runs itself.";

    /// <summary>Menu-sizing fix (gate-b): the chip's fixed docked width — set as
    /// <see cref="Control.CustomMinimumSize"/> on both this panel and its autowrap
    /// <see cref="Reason"/> label so the WordSmart label can never collapse the row to its
    /// ~1px natural minimum (the R7 layout-collapse class <c>LayoutTests</c> hunts elsewhere).
    /// <c>MainUi</c> reads this same constant to dock the chip's offsets — one source of truth.</summary>
    public const float DockWidth = 320f;

    /// <summary>UI-6 (menu-sizing/cozy redesign): the body clamps to this many WordSmart lines
    /// (<see cref="Label.MaxLinesVisible"/> + <see cref="TextServer.OverrunBehavior.TrimEllipsis"/>)
    /// so a verbose advisor reason can never grow the "quest note" past two lines — <see
    /// cref="Reason"/>'s own <see cref="Label.Text"/> still carries the FULL untruncated string
    /// (only the rendered glyphs clip), so <c>RenderedText</c>-based tests reading the raw text
    /// are unaffected.</summary>
    private const int ReasonMaxLines = 2;

    /// <summary>Bug fix (gate-b playtest screenshot, "body text does not render"): <see
    /// cref="Label.ClipText"/> is required so a verbose reason can never blow out the panel, but it
    /// also makes Godot report the Label's OWN natural minimum height as effectively zero — without
    /// an explicit floor here, <c>reasonRow</c> (an <see cref="HBoxContainer"/>) sizes to its
    /// tallest child, which becomes <c>ObjectiveStepGlyph</c>'s single-line height, and the
    /// WordSmart-wrapped body then has zero pixels of vertical room to render into: invisible, even
    /// though <see cref="Reason"/>'s <see cref="Label.Text"/> is still fully populated (which is why
    /// the existing <c>RenderedText</c>-based tests never caught this — they read <c>.Text</c>, not
    /// rendered pixels). Sized for <see cref="ReasonMaxLines"/> lines at <see
    /// cref="GameTheme.BodyFontSize"/> (the Label's own default font size — Reason sets no
    /// font-size override), at a ~1.3x line-height multiplier.</summary>
    private const float ReasonMinHeight = ReasonMaxLines * GameTheme.BodyFontSize * 1.3f;

    /// <summary>Line budget the height floor reserves for an UNCLAMPED tutorial step (see
    /// <see cref="Refresh"/>'s tutorial branch). Four lines holds the longest step in
    /// <c>TutorialFlow</c>'s chain at <see cref="DockWidth"/>; the label itself is unclamped, so a
    /// longer one still renders — this only guarantees the room is reserved up front rather than
    /// depending on a second layout pass.
    ///
    /// <para>Six, because a step's text is the instruction PLUS the live advisor reason appended
    /// (see <c>TutorialFlow.StepText</c>) — e.g. "Walk to the Forge (WASD, or click the ground to
    /// move) and press E, or click it — Buy 2 copper…". That concatenation is why this card needs so
    /// many lines. The copy itself is now held to this budget from the other side:
    /// <c>TutorialCopyIsFollowableTests.NoStepsCopy_OutgrowsTheObjectiveCardsOwnUnclampedLineBudget</c>
    /// fails a step that outgrows six lines, because tutorial text renders UNCLAMPED (see
    /// <see cref="Refresh"/>) and a long enough line therefore grows this chip off the screen rather
    /// than being trimmed — anything that needs more room belongs in the step's TeachNote, which
    /// renders inside the scrolling checklist and costs no height at all.</para></summary>
    private const int TutorialMaxLines = 6;

    /// <summary>Height floor for an unclamped tutorial step — same derivation as
    /// <see cref="ReasonMinHeight"/>, just a taller line budget.</summary>
    private const float TutorialMinHeight = TutorialMaxLines * GameTheme.BodyFontSize * 1.3f;

    /// <summary>Fade-in length (UI-6, accumulated-delta only — no engine Tween in this codebase,
    /// same contract as <c>TabFade</c>/the HUD gold-chip pop) for the body's dip-then-settle
    /// whenever a fresh step's text lands.</summary>
    private const double ReasonFadeSeconds = 0.25;

    /// <summary>U5: the checklist's own height ceiling — up to ten rows (plus an occasional gating
    /// note) would otherwise grow this chip past the window on a short viewport, the exact "still
    /// cutoff" class of bug <c>TutorialFlow</c>'s own dock already learned this lesson from (see
    /// its <c>Build</c> doc). Scrolls internally past this height rather than growing the whole
    /// dock or clipping content with no way to reach it.
    ///
    /// <para>Sized against the REST of this same chip's own pre-existing budget
    /// (<c>HudBoundsTests.ObjectiveChip_HeightTracksContent_NotFixedEmptyPanel</c>'s 260px pin,
    /// which predates this checklist and is never relaxed): a fresh Day-1 mount measures header
    /// (~23px) + the unclamped 6-line tutorial reason (<see cref="TutorialMinHeight"/>, ~127px) +
    /// the actions row (~35px) + this panel's own wood-frame margins (24px) + body separations
    /// (~12px) = ~221px before the checklist adds anything, leaving under 40px of the 260px
    /// budget for it. A ceiling any taller reopens the exact bug the 260px pin exists to catch —
    /// the ten-row checklist is why a peek-and-scroll sliver, not a several-row window, is what
    /// fits; the full list is always one scroll away.</para></summary>
    private const float ChecklistMaxHeight = 32f;

    public Label Reason { get; private set; } = null!;
    public Button Expand { get; private set; } = null!;
    public VBoxContainer RankedList { get; private set; } = null!;

    /// <summary>U5 (loop-legibility plan, R7): the tutorial's own checklist — every displayed
    /// step, ticked as it completes, rendered from <see cref="TutorialFlow.Checklist"/>. Visible
    /// only while <see cref="Refresh"/> is given a non-null <c>checklist</c> (i.e. only while the
    /// tutorial is <see cref="TutorialFlow.Active"/>) — never shown once the chain is dismissed or
    /// completed, same gate <see cref="TutorialDismiss"/> already uses.</summary>
    public VBoxContainer TutorialChecklist { get; private set; } = null!;

    /// <summary>The scrollable wrapper around <see cref="TutorialChecklist"/> (see <see
    /// cref="ChecklistMaxHeight"/>'s own doc) — this, not <see cref="TutorialChecklist"/> itself,
    /// is what <see cref="RefreshTutorialChecklist"/> shows/hides, so the scrollbar chrome never
    /// lingers empty.</summary>
    private ScrollContainer _checklistScroll = null!;

    /// <summary>U23: visible only while <see cref="Refresh"/> is given a tutorial override —
    /// dismisses the first-run chain (<c>TutorialFlow.Dismiss</c>, wired by <c>MainUi</c>) without
    /// exposing this chip to any other tutorial-specific concept.</summary>
    public Button TutorialDismiss { get; private set; } = null!;

    /// <summary>The step text last rendered — compared on every <see cref="Refresh"/> to decide
    /// whether a fresh <see cref="Tick"/> fade-in is owed (a same-text re-render, e.g. a tick that
    /// didn't change the advisor's top pick, never restarts the dip). <c>null</c> means "never
    /// rendered yet" (bug fix, gate-b): distinct from any real (including empty) reason string, so
    /// the very FIRST render is never mistaken for a "change" that arms the dip-then-settle fade —
    /// the first paint is the tutorial's opening line and must never risk landing on a dim frame.</summary>
    private string? _lastReasonText;

    /// <summary>-1 while no fade is in flight (mirrors <c>TabFade</c>'s own <c>_elapsed</c> idiom).</summary>
    private double _fadeElapsed = -1;

    /// <summary>Construct the chip's children. Call once, before the first <see cref="Refresh"/>.</summary>
    public void Build()
    {
        Name = "ObjectiveTracker";
        CustomMinimumSize = new Vector2(DockWidth, 0);

        // UI-6: a cozy "quest note" — the wood-framed panel every other cozy-redesign surface now
        // shares (falls back to a flat timber-bordered panel on a stripped build; see
        // GameTheme.PanelStyleWood's own null-tolerant contract), replacing the flat Iron rect.
        AddThemeStyleboxOverride("panel", GameTheme.PanelStyleWood());

        var body = new VBoxContainer { Name = "ObjectiveTrackerBody" };
        AddChild(body);

        // "Today" — a quiet caption, not the old shouting "OBJECTIVE" — still the smallest
        // legible size (LegibilityFloor) so it reads as a header, not a peer of the reason line.
        var header = new Label { Name = "ObjectiveHeader", Text = "Today" };
        header.AddThemeColorOverride("font_color", GameTheme.HeaderColor);
        header.AddThemeFontSizeOverride("font_size", GameTheme.LegibilityFloor);
        body.AddChild(header);

        var reasonRow = new HBoxContainer { Name = "ObjectiveTrackerRow" };
        reasonRow.AddThemeConstantOverride("separation", GameTheme.Space8);
        body.AddChild(reasonRow);

        // A filled step glyph marks the single live step (the ranked list below dims its own
        // lower-priority entries with the hollow twin — see the Refresh loop).
        var stepGlyph = new Label { Name = "ObjectiveStepGlyph", Text = "◆" };
        stepGlyph.AddThemeColorOverride("font_color", GameTheme.WarnColor);
        reasonRow.AddChild(stepGlyph);

        Reason = new Label
        {
            Name = "ObjectiveReason",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(DockWidth - 24, ReasonMinHeight),
            MaxLinesVisible = ReasonMaxLines,
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            Modulate = Colors.White, // belt-and-braces: never rely on the engine default alone
        };
        reasonRow.AddChild(Reason);

        var actionsRow = new HBoxContainer { Name = "ObjectiveActionsRow" };
        body.AddChild(actionsRow);

        // Ranked-list expand toggle (U18 approach: "expandable to the ranked list") — an icon-only
        // chevron (UI-6), full word moved to TooltipText.
        Expand = new Button { Name = "ObjectiveExpand", Text = "▾", ToggleMode = true, TooltipText = "More" };
        Expand.Pressed += () =>
        {
            RankedList.Visible = Expand.ButtonPressed;
            Expand.Text = Expand.ButtonPressed ? "▴" : "▾";
            Expand.TooltipText = Expand.ButtonPressed ? "Less" : "More";
        };
        actionsRow.AddChild(Expand);

        TutorialDismiss = new Button
        {
            Name = "ObjectiveTutorialDismiss", Text = "✕", Visible = false, TooltipText = "Dismiss tutorial",
        };
        actionsRow.AddChild(TutorialDismiss);

        // U5: the tutorial checklist — sits below the actions row, above the (unrelated) live
        // advisor ranked list, and is Clear-then-composed by Refresh() exactly like RankedList
        // below (same "no checklist yet" contract: hidden until Refresh hands it real rows).
        // Scrolls internally past ChecklistMaxHeight (see that const's own doc) rather than
        // growing this whole chip past the window on a short viewport.
        _checklistScroll = new ScrollContainer
        {
            Name = "ObjectiveTutorialChecklistScroll",
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            CustomMinimumSize = new Vector2(0, ChecklistMaxHeight),
            Visible = false,
        };
        body.AddChild(_checklistScroll);

        TutorialChecklist = new VBoxContainer { Name = "ObjectiveTutorialChecklist" };
        _checklistScroll.AddChild(TutorialChecklist);

        RankedList = new VBoxContainer { Name = "ObjectiveRankedList", Visible = false };
        body.AddChild(RankedList);
    }

    /// <summary>
    /// Rebuild the chip from a fresh <see cref="ObjectiveAdvisor.Suggest"/> pass over
    /// <paramref name="state"/>: the top entry's reason renders on the always-visible row (unless
    /// <paramref name="tutorialOverride"/> is given — U23's first-run chain overrides ONLY this
    /// top slot, never the ranked list below, so the live advisor stays reachable via "More"
    /// throughout the tutorial); every entry (including the top one) renders as its own line in
    /// the collapsible ranked list regardless. UI-6: arms the body's fade-in (see <see
    /// cref="Tick"/>) whenever the rendered text actually changed — a same-text re-render (a tick
    /// that didn't move the advisor's top pick) never restarts the dip.
    /// </summary>
    /// <param name="checklist">U5 (loop-legibility plan, R7): <see cref="TutorialFlow.Checklist"/>'s
    /// own projection, or null while the tutorial is not <see cref="TutorialFlow.Active"/>. Renders
    /// as a tick-list in <see cref="TutorialChecklist"/>, independent of <paramref
    /// name="tutorialOverride"/> (the top-slot line) so either can be reasoned about on its own.</param>
    public void Refresh(GameState state, string? tutorialOverride = null, IReadOnlyList<ChecklistRow>? checklist = null)
    {
        var suggestions = ObjectiveAdvisor.Suggest(state);
        var text = tutorialOverride ?? (suggestions.Count > 0 ? suggestions[0].Reason : NoObjectiveText);
        if (_lastReasonText is null)
        {
            // Bug fix (gate-b, "body text does not render"): the very first render must land at
            // full opacity immediately, never on the fade's dimmer starting frames — Tick's dip is
            // a nice-to-have for LATER changes only, once something is already on-screen to dip
            // FROM. Without this guard the initial empty-to-real-text transition armed the same
            // dip-then-settle as any later change, so a screenshot/paused-frame capture taken
            // before Tick finished its 0.25s ramp could catch the tutorial's opening line at its
            // dimmest.
            Reason.Modulate = Colors.White;
            _fadeElapsed = -1;
        }
        else if (text != _lastReasonText)
        {
            _fadeElapsed = 0;
        }

        _lastReasonText = text;
        Reason.Text = Plain(text);

        var isTutorial = tutorialOverride is not null;
        TutorialDismiss.Visible = isTutorial;

        // A TUTORIAL STEP IS NEVER ELLIPSIZED. The two-line clamp exists to stop a verbose advisor
        // reason from growing this chip without bound, and for advisor text losing the tail is fine —
        // it is a suggestion, and the full ranked list is one click away. A tutorial step is an
        // INSTRUCTION, and the clamp was eating the half that says what to do: Brian's playtest read
        // "Tutorial 1/5: Walk to the Forge (walk…" and reported it cut off, three times over three
        // sessions. Unclamped for tutorial text, clamped for everything else; the height floor grows
        // to match so the extra lines have somewhere to render (see ReasonMinHeight's own doc for why
        // ClipText makes an explicit floor mandatory).
        Reason.MaxLinesVisible = isTutorial ? -1 : ReasonMaxLines;
        Reason.ClipText = !isTutorial;
        // Unclamping the line count is not enough on its own — TrimEllipsis still ellipsizes at the
        // rect's bottom edge, which is what left "…Buy 2 copper…" on screen after the first attempt
        // at this fix. The trim behaviour has to go too.
        Reason.TextOverrunBehavior = isTutorial
            ? TextServer.OverrunBehavior.NoTrimming
            : TextServer.OverrunBehavior.TrimEllipsis;
        Reason.CustomMinimumSize = new Vector2(
            DockWidth - 24,
            isTutorial ? TutorialMinHeight : ReasonMinHeight);

        RefreshTutorialChecklist(checklist);

        foreach (var child in RankedList.GetChildren())
        {
            RankedList.RemoveChild(child);
            child.Free();
        }

        for (var i = 0; i < suggestions.Count; i++)
        {
            // Hollow glyph for every ranked-list entry (including the top one, shown again here
            // for the full-list view) — visually subordinate to the filled step glyph above.
            // Layout-probe fix (2026-07-29): these were bare Labels with no wrap, no clip and no
            // width cap, so a verbose advisor reason ran up to 82px PAST the window edge — the
            // ranked list was the one text surface in this panel that didn't get the same treatment
            // the main Reason label (see Build) already had.
            RankedList.AddChild(new Label
            {
                Name = $"ObjectiveRank_{i}",
                Text = Plain($"◇ {suggestions[i].Reason}"),
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                CustomMinimumSize = new Vector2(DockWidth - 24, 0),
                ClipText = true,
            });
        }
    }

    /// <summary>The checklist rows rendered by the LAST <see cref="RefreshTutorialChecklist"/>
    /// call that actually rebuilt the tree — <c>null</c> means "never rendered" (distinct from an
    /// empty/inactive render, mirrors <see cref="_lastReasonText"/>'s own null-means-unrendered
    /// contract). Compared on every call so a re-render carrying the IDENTICAL ten rows (the
    /// common case — most calls land mid-step, where nothing in the checklist changed) skips the
    /// clear-then-compose entirely.</summary>
    private IReadOnlyList<ChecklistRow>? _lastChecklistRows;

    /// <summary>
    /// U5 (loop-legibility plan, R7): Clear-then-compose the checklist from <paramref
    /// name="rows"/> (<see cref="TutorialFlow.Checklist"/>'s own projection) — hidden entirely
    /// when null/empty (tutorial inactive, or a caller that never passes one — <see
    /// cref="Refresh"/>'s own default is null, so every existing non-tutorial call site is
    /// unaffected). A done row dims; the current row carries the filled glyph plus (R7) its own
    /// gating note when the step is not currently actionable ("a Morning task — rest until dawn"
    /// rather than the old, confusing "press Next/Advance") and a small "Arrived" mark once <see
    /// cref="TutorialFlow.NotifyEnteredBuilding"/>'s ratchet has fired for it.
    ///
    /// <para><b>Skips the rebuild when <paramref name="rows"/> is unchanged from last time</b> —
    /// the same "a same-text re-render never restarts the dip" idiom <see cref="Refresh"/> already
    /// uses for <see cref="Reason"/> (compare against <see cref="_lastReasonText"/>), applied here
    /// because <c>MainUi.RefreshHud</c>/<c>RefreshObjectiveLine</c> calls this on EVERY phase tick
    /// AND every immediate action — dozens of times within a single tutorial step that never
    /// actually changes the ten-row checklist. Rebuilding ~30 Controls on every one of those calls
    /// (a full <c>Playtest3dClickThrough</c> session drives this hundreds of times before the
    /// chain completes) measurably destabilized the engine under that load — confirmed by bisection:
    /// removing just this rebuild's redundant repetition is what stops
    /// <c>Playtest3dClickThrough</c>'s two test cases from crashing the Godot process when run
    /// back to back, even though each rebuild individually frees every node it creates.</para>
    /// </summary>
    private void RefreshTutorialChecklist(IReadOnlyList<ChecklistRow>? rows)
    {
        if (ChecklistUnchanged(rows))
        {
            return;
        }

        _lastChecklistRows = rows;

        foreach (var child in TutorialChecklist.GetChildren())
        {
            TutorialChecklist.RemoveChild(child);
            child.Free();
        }

        _checklistScroll.Visible = rows is { Count: > 0 };
        if (rows is null)
        {
            return;
        }

        foreach (var row in rows)
        {
            var glyph = row.Done ? "✓" : row.Current ? "◆" : "○";
            var glyphColor = row.Done ? GameTheme.GoodColor : row.Current ? GameTheme.WarnColor : GameTheme.TextDim;

            var line = new HBoxContainer { Name = $"TutorialChecklistRow_{row.DisplayIndex}" };
            line.AddThemeConstantOverride("separation", GameTheme.Space8);
            TutorialChecklist.AddChild(line);

            var glyphLabel = new Label { Text = glyph };
            glyphLabel.AddThemeColorOverride("font_color", glyphColor);
            line.AddChild(glyphLabel);

            var suffix = row.VisitedAnchor ? "  ✓ Arrived" : string.Empty;
            var textLabel = new Label
            {
                Name = "TutorialChecklistLabel",
                Text = Plain(row.Label) + suffix,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                CustomMinimumSize = new Vector2(DockWidth - 40, 0),
                ClipText = true,
            };
            if (row.Done)
            {
                textLabel.AddThemeColorOverride("font_color", GameTheme.TextDim);
            }

            line.AddChild(textLabel);

            // The current step's "what this mechanism is" line. Until this shipped, every one of
            // TutorialFlow.Registry's ten TeachNotes was written, reviewed, and rendered by nobody
            // — a step's instruction told the player what to press and nothing ever told them what
            // the thing they were pressing DOES. The owner asking to "explain how the bounties work
            // further" was asking for a paragraph the game already had and never showed.
            if (row.Current && row.TeachNote is { } teach)
            {
                var teachLabel = new Label
                {
                    Name = "TutorialChecklistTeachNote",
                    Text = Plain(teach),
                    AutowrapMode = TextServer.AutowrapMode.WordSmart,
                    CustomMinimumSize = new Vector2(DockWidth - 24, 0),
                };
                teachLabel.AddThemeColorOverride("font_color", GameTheme.TextDim);
                TutorialChecklist.AddChild(teachLabel);
            }

            if (row.Current && row.GatingNote is { } note)
            {
                var noteLabel = new Label
                {
                    Name = "TutorialChecklistGatingNote",
                    Text = Plain(note),
                    AutowrapMode = TextServer.AutowrapMode.WordSmart,
                    CustomMinimumSize = new Vector2(DockWidth - 24, 0),
                };
                noteLabel.AddThemeColorOverride("font_color", GameTheme.WarnColor);
                TutorialChecklist.AddChild(noteLabel);
            }
        }
    }

    /// <summary>Value-equality check for <see cref="RefreshTutorialChecklist"/>'s skip-if-unchanged
    /// guard — <see cref="ChecklistRow"/> is a <c>readonly record struct</c>, so element comparison
    /// is a real field-by-field check, not a reference comparison; two DIFFERENT <see
    /// cref="TutorialFlow.Checklist"/> calls that happen to return the same content (the common
    /// case between sim-state changes) compare equal here.</summary>
    private bool ChecklistUnchanged(IReadOnlyList<ChecklistRow>? rows)
    {
        if (rows is null || _lastChecklistRows is null)
        {
            return rows is null && _lastChecklistRows is null;
        }

        if (rows.Count != _lastChecklistRows.Count)
        {
            return false;
        }

        for (var i = 0; i < rows.Count; i++)
        {
            if (!rows[i].Equals(_lastChecklistRows[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Strip the markdown emphasis the sim's advisor/tutorial strings carry. Those strings are
    /// shared with the CLI, where <c>**bold**</c> is meaningful; a Godot <see cref="Label"/> has no
    /// markup parser, so it rendered the asterisks literally — the first tutorial step read
    /// "Walk to the **Forge**". Presentation-side strip rather than a sim change, because the CLI
    /// still wants the emphasis (and the sim must stay the single source of the wording).
    /// </summary>
    public static string Plain(string text) => text.Replace("**", string.Empty);

    /// <summary>UI-6: advance the body's dip-then-settle fade by one frame's delta — called from
    /// <c>MainUi._Process</c>, the same accumulated-delta place <c>TabFade</c>/the gold-chip pop
    /// tick (no engine Tween in this codebase). No-op unless <see cref="Refresh"/> just changed
    /// the rendered text.</summary>
    public void Tick(double delta)
    {
        if (_fadeElapsed < 0)
        {
            return;
        }

        _fadeElapsed += delta;
        var t = Mathf.Clamp((float)(_fadeElapsed / ReasonFadeSeconds), 0f, 1f);
        Reason.Modulate = new Color(1f, 1f, 1f, 0.25f + 0.75f * t);
        if (t >= 1f)
        {
            _fadeElapsed = -1;
            Reason.Modulate = Colors.White;
        }
    }
}

/// <summary>
/// World-rework U18 (R12, KTD13): the day-timeline widget — docks top-bar center (KTD13).
/// Renders the 5 phases in the kernel's own tick order (Morning → Expedition → Camp →
/// ExpeditionDeep → Evening — NOT <see cref="DayPhase"/>'s declaration order, which lists
/// Evening before Camp/ExpeditionDeep; mirrors <c>MainUi.PhaseLegend</c>'s own ordering), with
/// the live phase highlighted, plus the U15 engaged-wait indicator
/// (<see cref="GodotClient.PhaseClock.Engaged"/>) so a player can see the clock is holding at a
/// boundary for them rather than assume it stalled. <see cref="Refresh"/> is called by the owner
/// on every phase tick and on every discrete engaged-state change (tab switch, modal open/close)
/// — never per frame.
/// </summary>
public sealed partial class DayTimeline : HBoxContainer
{
    /// <summary>U2 (playtest-three plan, KTD-B): labels come from <see cref="PhaseVocab"/> now,
    /// not the raw sim phase names — this used to print "Camp"/"Deep" (<c>DayPhase.ToString()</c>
    /// verbatim) directly above a HUD banner that already said "Vigil"/"Deep Vigil" for the exact
    /// same moment, the split-brain vocabulary the owner's playtest flagged.</summary>
    private static readonly (DayPhase Phase, string Label)[] KernelOrder =
    {
        (DayPhase.Morning, PhaseVocab.Display(DayPhase.Morning)),
        (DayPhase.Expedition, PhaseVocab.Display(DayPhase.Expedition)),
        (DayPhase.Camp, PhaseVocab.Display(DayPhase.Camp)),
        (DayPhase.ExpeditionDeep, PhaseVocab.Display(DayPhase.ExpeditionDeep)),
        (DayPhase.Evening, PhaseVocab.Display(DayPhase.Evening)),
    };

    /// <summary>Segment-pill underline thickness (px) — the "current phase" marker (UI-4).</summary>
    private const float UnderlineHeight = 2f;

    /// <summary>Pulsing-dot period (seconds) — accumulated-delta only (no engine Tween in this
    /// codebase; mirrors <c>TabFade</c>/the HUD gold-chip pop).</summary>
    private const double PulsePeriodSeconds = 1.2;

    private const float PulseMinAlpha = 0.35f;

    private PanelContainer[] _segmentPills = System.Array.Empty<PanelContainer>();
    private Label[] _phaseLabels = System.Array.Empty<Label>();
    private ColorRect[] _underlines = System.Array.Empty<ColorRect>();
    private ColorRect _waiting = null!;
    private double _pulseElapsed;

    /// <summary>The phase last highlighted by <see cref="Refresh"/> — a discoverable pin for
    /// tests (mirrors <c>TabFade.IsFading</c>'s own testability shape) so a scripted-day test
    /// can assert the live phase without scanning theme-color overrides.</summary>
    public DayPhase Current { get; private set; }

    /// <summary>Construct the timeline's children. Call once, before the first <see cref="Refresh"/>.</summary>
    public void Build()
    {
        Name = "DayTimeline";

        // Menu-sizing fix (gate-b): LOCAL override only (this node's own theme-constant
        // stack) — never theme.SetConstant("separation", "HBoxContainer", ...), which would
        // restyle every HBoxContainer in the app. Without this the 5 phase segments + the
        // waiting dot sat with zero gap and read as run-on text. Kept >= 6 (MenuSizingTests).
        AddThemeConstantOverride("separation", 12);

        // UI-4 (menu-sizing/cozy redesign): a connected segment strip — past dim, current a
        // filled Arcane pill with an Ember underline, future outlined — replacing the 5 loose
        // plain-text labels that used to read as run-on text at a glance.
        _segmentPills = new PanelContainer[KernelOrder.Length];
        _phaseLabels = new Label[KernelOrder.Length];
        _underlines = new ColorRect[KernelOrder.Length];
        for (var i = 0; i < KernelOrder.Length; i++)
        {
            var (phase, text) = KernelOrder[i];

            // The per-phase Name is kept even though it's no longer a bare Label — nothing reads
            // it today, but it stays a stable, discoverable-by-phase anchor for the segment.
            var segment = new VBoxContainer { Name = $"TimelinePhase_{phase}" };
            segment.AddThemeConstantOverride("separation", 2);
            AddChild(segment);

            var pill = new PanelContainer { Name = "TimelineSegmentPill" };
            segment.AddChild(pill);

            var label = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center };
            label.AddThemeFontSizeOverride("font_size", GameTheme.LegibilityFloor);
            pill.AddChild(label);

            var underline = new ColorRect
            {
                Name = "TimelineUnderline",
                Color = GameTheme.EmberColor,
                CustomMinimumSize = new Vector2(0, UnderlineHeight),
                Visible = false,
            };
            segment.AddChild(underline);

            _segmentPills[i] = pill;
            _phaseLabels[i] = label;
            _underlines[i] = underline;
        }

        // The old "[waiting]" text becomes a small pulsing Ember dot (UI-4) — ticked in Tick(),
        // called every frame from MainUi._Process (accumulated-delta, no engine Tween). Node NAME
        // is kept ("TimelineWaiting") and it stays a plain Control so existing Visible-toggle
        // assertions (MainUiTests) keep working unchanged.
        _waiting = new ColorRect
        {
            Name = "TimelineWaiting",
            Color = GameTheme.EmberColor,
            CustomMinimumSize = new Vector2(10, 10),
            Visible = false,
        };
        AddChild(_waiting);
    }

    /// <summary>Highlight <paramref name="current"/> among the 5 phase segments (past dim,
    /// current filled+underlined, future outlined) and show/hide the pulsing engaged-wait dot per
    /// <paramref name="waiting"/>.</summary>
    public void Refresh(DayPhase current, bool waiting)
    {
        Current = current;
        var currentIndex = 0;
        for (var i = 0; i < KernelOrder.Length; i++)
        {
            if (KernelOrder[i].Phase == current)
            {
                currentIndex = i;
                break;
            }
        }

        for (var i = 0; i < KernelOrder.Length; i++)
        {
            var isCurrent = i == currentIndex;
            var isPast = i < currentIndex;
            _segmentPills[i].AddThemeStyleboxOverride("panel", SegmentStyle(isCurrent, isPast));
            _phaseLabels[i].AddThemeColorOverride(
                "font_color", isCurrent ? GameTheme.BoneColor : isPast ? GameTheme.TextDim : GameTheme.BodyTextColor);
            _underlines[i].Visible = isCurrent;
        }

        _waiting.Visible = waiting;
        if (!waiting)
        {
            // Reset so the NEXT time it shows always starts at the dip's brightest point rather
            // than resuming mid-cycle from a stale elapsed value.
            _pulseElapsed = 0;
            _waiting.Color = GameTheme.EmberColor;
        }
    }

    /// <summary>Advance the waiting-dot pulse by one frame's delta — called from
    /// <c>MainUi._Process</c> alongside every other accumulated-delta decoration (TabFade, the
    /// gold-chip pop, ObjectiveTracker's own fade). No-op while the dot is hidden.</summary>
    public void Tick(double delta)
    {
        if (!_waiting.Visible)
        {
            return;
        }

        _pulseElapsed += delta;
        var phase = (float)((_pulseElapsed % PulsePeriodSeconds) / PulsePeriodSeconds);
        var alpha = PulseMinAlpha + (1f - PulseMinAlpha) * (0.5f + 0.5f * Mathf.Sin(Mathf.Tau * phase));
        _waiting.Color = new Color(GameTheme.EmberColor, alpha);
    }

    /// <summary>Fresh <see cref="StyleBoxFlat"/> per call (StyleBox is a mutable Resource — never
    /// share one instance across segments/calls, same rule <c>GameTheme</c>'s own builders
    /// follow): filled Arcane for the current phase, a faint Arcane outline for a future phase,
    /// and a dim, borderless fill for a past one.</summary>
    private static StyleBoxFlat SegmentStyle(bool isCurrent, bool isPast)
    {
        if (isCurrent)
        {
            return new StyleBoxFlat
            {
                BgColor = GameTheme.AccentColor,
                CornerRadiusBottomLeft = GameTheme.RadiusChip,
                CornerRadiusBottomRight = GameTheme.RadiusChip,
                CornerRadiusTopLeft = GameTheme.RadiusChip,
                CornerRadiusTopRight = GameTheme.RadiusChip,
                ContentMarginLeft = GameTheme.Space8,
                ContentMarginRight = GameTheme.Space8,
                ContentMarginTop = GameTheme.Space4,
                ContentMarginBottom = GameTheme.Space4,
            };
        }

        if (isPast)
        {
            return new StyleBoxFlat
            {
                BgColor = new Color(GameTheme.Surface, 0.4f),
                CornerRadiusBottomLeft = GameTheme.RadiusChip,
                CornerRadiusBottomRight = GameTheme.RadiusChip,
                CornerRadiusTopLeft = GameTheme.RadiusChip,
                CornerRadiusTopRight = GameTheme.RadiusChip,
                ContentMarginLeft = GameTheme.Space8,
                ContentMarginRight = GameTheme.Space8,
                ContentMarginTop = GameTheme.Space4,
                ContentMarginBottom = GameTheme.Space4,
            };
        }

        // Future: outlined only, transparent fill.
        return new StyleBoxFlat
        {
            BgColor = Colors.Transparent,
            BorderColor = new Color(GameTheme.AccentColor, 0.5f),
            BorderWidthBottom = 1,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            CornerRadiusBottomLeft = GameTheme.RadiusChip,
            CornerRadiusBottomRight = GameTheme.RadiusChip,
            CornerRadiusTopLeft = GameTheme.RadiusChip,
            CornerRadiusTopRight = GameTheme.RadiusChip,
            ContentMarginLeft = GameTheme.Space8,
            ContentMarginRight = GameTheme.Space8,
            ContentMarginTop = GameTheme.Space4,
            ContentMarginBottom = GameTheme.Space4,
        };
    }
}
