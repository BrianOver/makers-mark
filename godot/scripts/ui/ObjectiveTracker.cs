using System;
using System.Collections.Generic;
using GameSim.Advisor;
using GameSim.Contracts;
using Godot;
using GodotClient.Minigames;

namespace GodotClient.Ui;

/// <summary>
/// World-rework U18 (R11, KTD9/KTD13): the persistent "what do I do now" HUD chip — docks
/// top-right below the header (KTD13). Renders <see cref="ObjectiveAdvisor.Suggest"/>'s top pick
/// plus its reason; the row expands to the full ranked list on demand. <see cref="Refresh"/> is
/// a pure Clear-then-compose pass over a <see cref="GameState"/> snapshot (KTD2 — no sim contact,
/// no mutation) and is called by the owner (<c>MainUi</c>) on every phase tick, never per frame
/// (U18 approach) — <see cref="ObjectiveAdvisor.Suggest"/> only needs to run when the state it
/// projects over could have changed.
///
/// <para>P2-SCREEN-06 (the card diet): this card carries exactly ONE prose block — <see
/// cref="Reason"/> — and nothing else. It used to also carry a title Label, a row of three
/// unlabeled glyph buttons, and (while a tutorial was running) a scrolling checklist of every
/// step's own label/TeachNote/GatingNote — four surfaces wearing one box, in a literal child
/// order that put a button row directly between the instruction and the checklist's own text.
/// A screenshot of that layout reads as one sentence severed by chrome; it is actually two
/// unrelated messages a reader cannot tell apart, which is worse than a wrap bug. The fix moves
/// every non-prose surface OFF this card: the controls become worded buttons in a title bar
/// ABOVE <see cref="Reason"/> (never below it — see <see cref="Build"/>), and the checklist
/// moves out entirely into <c>LessonsPanel</c>, which already renders every row permanently.
/// </para>
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
    /// <see cref="Refresh"/>'s tutorial branch) at <see cref="DockWidth"/>; the label itself is
    /// unclamped, so a longer one still renders — this only guarantees the room is reserved up
    /// front rather than depending on a second layout pass.
    ///
    /// <para>U2 (tutorial-revamp plan, §11.13): dropped from 6 to 3. The teaching that used to be
    /// crammed into this card's own text (the mechanism explanation, the "what this actually is"
    /// paragraph) moved OUT — permanently, into the Lessons book (<c>LessonsPanel</c>) — so the
    /// card itself only ever has to hold the instruction PLUS the live advisor reason appended
    /// (see <c>TutorialFlow.StepText</c>), which fits in three lines at <see cref="DockWidth"/>.
    /// P2-SCREEN-06 finished the job: the per-step checklist that used to sit below this budget
    /// (label, TeachNote, GatingNote, one row per step, in its own 75px scrolling window) is gone
    /// too — moved permanently into <c>LessonsPanel</c>, which is not height-constrained the way a
    /// persistent HUD chip has to be. This card's own budget now only ever has to cover the
    /// instruction itself. The copy itself is held to this budget from the other side:
    /// <c>TutorialCopyIsFollowableTests.NoStepsCopy_OutgrowsTheObjectiveCardsOwnUnclampedLineBudget</c>
    /// fails a step that outgrows three lines, because tutorial text renders UNCLAMPED (see
    /// <see cref="Refresh"/>) and a long enough line therefore grows this chip off the screen rather
    /// than being trimmed — anything that needs more room belongs in the step's TeachNote/the
    /// Lessons book, neither of which costs this card any height at all.</para></summary>
    private const int TutorialMaxLines = 3;

    /// <summary>Height floor for an unclamped tutorial step — same derivation as
    /// <see cref="ReasonMinHeight"/>, just a taller line budget.</summary>
    private const float TutorialMinHeight = TutorialMaxLines * GameTheme.BodyFontSize * 1.3f;

    /// <summary>Fade-in length (UI-6, accumulated-delta only — no engine Tween in this codebase,
    /// same contract as <c>TabFade</c>/the HUD gold-chip pop) for the body's dip-then-settle
    /// whenever a fresh step's text lands.</summary>
    private const double ReasonFadeSeconds = 0.25;

    public Label Reason { get; private set; } = null!;
    public Button Expand { get; private set; } = null!;
    public VBoxContainer RankedList { get; private set; } = null!;

    /// <summary>P2-SCREEN-06: the card's own worded title — "Today", or (while a tutorial is
    /// running) "Today — {Act} · {position} of {total}" (see <see cref="Refresh"/>'s
    /// <c>tutorialTitleSuffix</c> parameter). Sits ABOVE <see cref="Reason"/> in <see
    /// cref="Build"/>'s own child order, never below it — this and <see cref="_controlsRow"/>
    /// together replace the old bare "Today" caption plus the three-glyph button row that used to
    /// sit BELOW the instruction, which is the literal shape of the severed-sentence defect this
    /// unit exists to fix (this class's own doc).</summary>
    private Label _titleLabel = null!;

    /// <summary>U20 (§11.14.14): the on-screen half of the "remind me" re-ask — the matching control the
    /// task's own "Two absences" #2 asks for, so a player who never learns the <c>tutorial_reask</c>
    /// key still has a press that restates the current step and flashes its pointer. <c>MainUi</c>
    /// wires this <see cref="Button.Pressed"/> to the SAME <c>ReaskTutorial</c> handler the keyboard
    /// shortcut calls — one behavior, two ways to ask for it. Visible on the identical <c>isTutorial</c>
    /// gate as <see cref="TutorialDismiss"/> (see <see cref="Refresh"/>): hidden, and therefore inert,
    /// the moment the chain is no longer <see cref="TutorialFlow.Active"/>.
    ///
    /// <para>P2-SCREEN-06: reads "Again" now, not the bare "↻" glyph it used to — one of <see
    /// cref="TutorialDismiss"/>'s two title-bar siblings, both worded, both right-aligned (see
    /// <see cref="Build"/>). This is the one of the two with a keyboard twin, so it alone carries
    /// <see cref="_reaskBadge"/> beside it.</para>
    /// </summary>
    public Button TutorialReask { get; private set; } = null!;

    /// <summary>P2-SCREEN-06: the always-visible "R" chip (<see cref="UiKit.ShortcutBadge"/>) beside
    /// <see cref="TutorialReask"/> — the re-ask has a keyboard twin (<c>tutorial_reask</c>); <see
    /// cref="TutorialDismiss"/> does not, which is why only this button gets one. Visibility tracks
    /// <see cref="TutorialReask"/>'s own (see <see cref="Refresh"/>) so a stray badge never lingers
    /// once the chain is no longer active.</summary>
    private Control _reaskBadge = null!;

    /// <summary>§11.13 amendment (U5): the confirmed-graduation row's "yes, end it" button — the
    /// caller (<c>MainUi</c>) wires the atomic <c>ConcludeApprenticeshipAction</c> submit +
    /// <see cref="TutorialFlow.Dismiss"/> on this <see cref="Button.Pressed"/> event, on top of this
    /// class's own row-hiding handler (both fire; order between them does not matter).</summary>
    public Button TutorialDismissConfirmYes { get; private set; } = null!;

    /// <summary>
    /// U20 (§11.14.14): "there is no way to ask where to go" — see <see cref="TutorialReask"/>'s
    /// own doc. §11.13 amendment (U5, R12 ruled yes): dismissing is graduation, and this now
    /// confirms rather than acting instantly — "no timers on decisions" (law) means this row waits
    /// on the player's own second press, never a countdown; the copy (set by <see
    /// cref="ShowDismissConfirm"/>'s caller, <c>MainUi</c>, from <see
    /// cref="TutorialFlow.DismissConfirmCopy"/>) names the warrant's cost BEFORE the choice is
    /// made, never after.
    ///
    /// <para>P2-SCREEN-06: reads "Skip" now, not the bare "✕" glyph it used to — one of the two
    /// controls the owner's photographed defect named outright ("one of them ends a feature and
    /// forfeits gold, and it is a bare ✕ today"). Naming what it costs, in words, in the title
    /// bar, is the fix — not hiding it behind a glyph and a tooltip nobody hovers. "Skip" rather
    /// than the fuller "Skip the course" (CI catch, same unit: the longer word pushed the title
    /// bar's own controls row 3px past the 1152px-wide viewport floor, <c>HudBoundsTests
    /// .ObjectiveChip_TextNeverOverflowsItsOwnContainer</c>) — the two-press confirm this button
    /// arms (<see cref="ShowDismissConfirm"/>) is where the warrant's cost actually gets named, so
    /// the button itself only has to name the ACTION.</para>
    /// </summary>
    public Button TutorialDismiss { get; private set; } = null!;

    private VBoxContainer _dismissConfirmRow = null!;
    private Label _dismissConfirmLabel = null!;

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

        // P2-SCREEN-06 (the card diet): a worded title bar, ABOVE the single prose line — never
        // below it. The old layout put a row of three unlabeled glyph buttons directly BELOW the
        // instruction and then a scrolling checklist below THAT; a screenshot of it reads as one
        // sentence severed by chrome. Putting every control here, before Reason exists at all,
        // means no button in this card ever again sits between two independent text blocks (see
        // the tripwire, TutorialFlowTests.Card_NoButtonRowSitsBetweenTwoIndependentTextBlocks).
        // Two rows, not one: the title text alone on
        // top (room for "Today — {Act} · {position} of {total}" without fighting three buttons for
        // the same 320px), the controls right-aligned below it.
        var titleBar = new VBoxContainer { Name = "ObjectiveTitleBar" };
        body.AddChild(titleBar);

        _titleLabel = new Label { Name = "ObjectiveTitleLabel", Text = "Today" };
        _titleLabel.AddThemeColorOverride("font_color", GameTheme.HeaderColor);
        _titleLabel.AddThemeFontSizeOverride("font_size", GameTheme.LegibilityFloor);
        titleBar.AddChild(_titleLabel);

        var controlsRow = new HBoxContainer { Name = "ObjectiveControlsRow" };
        controlsRow.AddThemeConstantOverride("separation", GameTheme.Space8);
        titleBar.AddChild(controlsRow);

        // Pushes every control that follows to the right edge of the 320px dock — the title text
        // above already owns the left edge, so nothing needs to claim it here.
        controlsRow.AddChild(new Control { Name = "ObjectiveControlsSpacer", SizeFlagsHorizontal = SizeFlags.ExpandFill });

        // Ranked-list expand toggle (U18 approach: "expandable to the ranked list") — worded now
        // (P2-SCREEN-06: "the controls move into a worded title bar"), not the old icon-only "▾".
        Expand = new Button { Name = "ObjectiveExpand", Text = "More", ToggleMode = true, TooltipText = "Show the full ranked list" };
        Expand.Pressed += () =>
        {
            RankedList.Visible = Expand.ButtonPressed;
            Expand.Text = Expand.ButtonPressed ? "Less" : "More";
        };
        controlsRow.AddChild(Expand);

        // U20: "there is no way to ask where to go" — see this field's own doc. MinigameInput's
        // registry (not TownInput — see its own comment on why) must have already added
        // "tutorial_reask" before ShortcutMap.Tooltip below reads its key label; guarded/idempotent,
        // same precedent CompanionDock.Build already set for "docket_toggle".
        MinigameInput.RegisterActions();
        TutorialReask = new Button
        {
            Name = "ObjectiveTutorialReask", Text = "Again", Visible = false, TooltipText = ShortcutMap.Tooltip("tutorial_reask"),
        };
        controlsRow.AddChild(TutorialReask);
        // P2-SCREEN-06: the re-ask has a keyboard twin; the dismiss beside it does not, so only
        // this one gets a badge — an always-visible fact, not a tooltip nobody hovers to find.
        _reaskBadge = UiKit.ShortcutBadge(ShortcutMap.KeyLabel(ShortcutMap.Find("tutorial_reask")));
        _reaskBadge.Visible = false;
        controlsRow.AddChild(_reaskBadge);

        // "Skip" (not the fuller "Skip the course" this unit shipped with first — see this
        // field's own doc): the two-press confirm it arms is where the warrant's cost actually
        // gets named, so the word here only has to carry the action, and the shorter word keeps
        // the controls row inside the dock's 320px budget at the 1152px viewport floor.
        TutorialDismiss = new Button
        {
            Name = "ObjectiveTutorialDismiss", Text = "Skip", Visible = false,
            TooltipText = "Skip the course — end the apprenticeship early; a warrant cost may still be owed",
        };
        controlsRow.AddChild(TutorialDismiss);

        // §11.13 amendment (U5, R12 ruled yes): dismissing is graduation, and this row confirms
        // rather than acting instantly — "no timers on decisions" (law) means this row waits on the
        // player's own second press, never a countdown; the copy (set by ShowDismissConfirm's
        // caller, MainUi, from TutorialFlow.DismissConfirmCopy) names the warrant's cost BEFORE the
        // choice is made, never after. Sits directly below the title bar that triggers it — still
        // entirely above Reason, so the single prose line below never has to share a sibling with
        // a button (see the tripwire, TutorialFlowTests.Card_NoButtonRowSitsBetweenTwoIndependentTextBlocks).
        _dismissConfirmRow = new VBoxContainer { Name = "ObjectiveTutorialDismissConfirm", Visible = false };
        body.AddChild(_dismissConfirmRow);

        _dismissConfirmLabel = new Label
        {
            Name = "ObjectiveTutorialDismissConfirmLabel",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(DockWidth - 24, 0),
        };
        _dismissConfirmRow.AddChild(_dismissConfirmLabel);

        var confirmButtons = new HBoxContainer { Name = "ObjectiveTutorialDismissConfirmButtons" };
        confirmButtons.AddThemeConstantOverride("separation", GameTheme.Space8);
        _dismissConfirmRow.AddChild(confirmButtons);

        // The caller (MainUi) wires the atomic submit+Dismiss() on this button; hiding the row is
        // this class's own job either way (Yes or No), so it happens here regardless of what the
        // caller's own handler does.
        TutorialDismissConfirmYes = new Button { Name = "ObjectiveTutorialDismissConfirmYes", Text = "End it" };
        TutorialDismissConfirmYes.Pressed += HideDismissConfirm;
        confirmButtons.AddChild(TutorialDismissConfirmYes);

        var confirmNo = new Button { Name = "ObjectiveTutorialDismissConfirmNo", Text = "Keep going" };
        confirmNo.Pressed += HideDismissConfirm;
        confirmButtons.AddChild(confirmNo);

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
    /// <param name="tutorialTitleSuffix">P2-SCREEN-06: the title bar's own "{Act} · {position} of
    /// {total}" fragment (<c>TutorialFlow.CurrentActPositionLabel</c>) while a tutorial is active,
    /// or null to show the bare "Today" caption. Independent of <paramref name="tutorialOverride"/>
    /// so the title and the instruction can never disagree about whether a tutorial is running —
    /// both come from the same caller in the same tick (<c>MainUi.RefreshObjectiveLine</c>).</param>
    public void Refresh(GameState state, string? tutorialOverride = null, string? tutorialTitleSuffix = null)
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
        _titleLabel.Text = tutorialTitleSuffix is { } suffix ? $"Today — {suffix}" : "Today";
        TutorialDismiss.Visible = isTutorial;
        TutorialReask.Visible = isTutorial; // U20: inert once the course completes — hidden, not just disabled
        _reaskBadge.Visible = isTutorial; // the badge tracks the button it sits beside, always
        if (!isTutorial)
        {
            // Defensive: the chain ending some OTHER way (e.g. BackstopDay's own auto-complete)
            // while a confirm sat open must not leave a stray graduation dialog on screen.
            HideDismissConfirm();
        }

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

    /// <summary>
    /// §11.13 amendment (U5): arm the confirmed-graduation row with <paramref name="copy"/> (from
    /// <see cref="TutorialFlow.DismissConfirmCopy"/>, chosen by whether the warrant still holds) —
    /// called by the caller's <see cref="TutorialDismiss"/> press handler. "No timers on decisions"
    /// (law): this row waits on the player's own second press (<see cref="TutorialDismissConfirmYes"/>
    /// or the Keep-going button), never a countdown.
    /// </summary>
    public void ShowDismissConfirm(string copy)
    {
        _dismissConfirmLabel.Text = Plain(copy);
        _dismissConfirmRow.Visible = true;
    }

    /// <summary>Close the confirmed-graduation row without acting — the Keep-going answer, and also
    /// this class's own half of the Yes answer (the caller's Yes handler runs alongside it).</summary>
    public void HideDismissConfirm() => _dismissConfirmRow.Visible = false;

    /// <summary>
    /// Strip the markdown emphasis the sim's advisor/tutorial strings carry. Those strings are
    /// shared with the CLI, where <c>**bold**</c> is meaningful; a Godot <see cref="Label"/> has no
    /// markup parser, so before this fix it rendered the asterisks literally — the first tutorial
    /// step's own bolded building name, back when the card still named one (P2-ONBOARD-06, §11.15,
    /// deleted that WHERE clause entirely). Presentation-side strip rather than a sim change,
    /// because the CLI still wants the emphasis (and the sim must stay the single source of the
    /// wording).
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
        //
        // Trimmed from 12 (tutorial-revamp wave, §11.13): the Books Tray's eighth icon
        // (LessonsPanel, added the same wave) ate into this row's shared ExpandFill budget —
        // see SegmentStyle's own margin trim just below for the other half of that reclaim —
        // and this timeline was the one left short, clipping the "Night" segment
        // (HudBoundsTests.ObjectiveChip_TextNeverOverflowsItsOwnContainer). Still comfortably
        // above the pinned floor.
        AddThemeConstantOverride("separation", 8);

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
                // Trimmed from Space8 (tutorial-revamp wave, §11.13) — see this timeline's own
                // Build()/AddThemeConstantOverride("separation", ...) doc for why: reclaiming
                // width here (5 pills * 2 sides * 4px = 40px) is the other half of what stopped
                // the "Night" segment clipping once the Books Tray grew an eighth icon.
                ContentMarginLeft = GameTheme.Space4,
                ContentMarginRight = GameTheme.Space4,
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
                // Trimmed from Space8 (tutorial-revamp wave, §11.13) — see this timeline's own
                // Build()/AddThemeConstantOverride("separation", ...) doc for why: reclaiming
                // width here (5 pills * 2 sides * 4px = 40px) is the other half of what stopped
                // the "Night" segment clipping once the Books Tray grew an eighth icon.
                ContentMarginLeft = GameTheme.Space4,
                ContentMarginRight = GameTheme.Space4,
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
            // Trimmed from Space8 — see the isCurrent/isPast branches' own doc above.
            ContentMarginLeft = GameTheme.Space4,
            ContentMarginRight = GameTheme.Space4,
            ContentMarginTop = GameTheme.Space4,
            ContentMarginBottom = GameTheme.Space4,
        };
    }
}
