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

    public Label Reason { get; private set; } = null!;
    public Button Expand { get; private set; } = null!;
    public VBoxContainer RankedList { get; private set; } = null!;

    /// <summary>U23: visible only while <see cref="Refresh"/> is given a tutorial override —
    /// dismisses the first-run chain (<c>TutorialFlow.Dismiss</c>, wired by <c>MainUi</c>) without
    /// exposing this chip to any other tutorial-specific concept.</summary>
    public Button TutorialDismiss { get; private set; } = null!;

    /// <summary>Construct the chip's children. Call once, before the first <see cref="Refresh"/>.</summary>
    public void Build()
    {
        Name = "ObjectiveTracker";
        CustomMinimumSize = new Vector2(DockWidth, 0);

        var body = new VBoxContainer { Name = "ObjectiveTrackerBody" };
        AddChild(body);

        var header = new Label { Name = "ObjectiveHeader", Text = "OBJECTIVE" };
        header.AddThemeColorOverride("font_color", GameTheme.HeaderColor);
        header.AddThemeFontSizeOverride("font_size", GameTheme.LegibilityFloor);
        body.AddChild(header);

        var row = new HBoxContainer { Name = "ObjectiveTrackerRow" };
        body.AddChild(row);

        Reason = new Label
        {
            Name = "ObjectiveReason",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(DockWidth - 24, 0),
        };
        row.AddChild(Reason);

        // Ranked-list expand toggle (U18 approach: "expandable to the ranked list").
        Expand = new Button { Name = "ObjectiveExpand", Text = "More", ToggleMode = true };
        Expand.Pressed += () => RankedList.Visible = Expand.ButtonPressed;
        row.AddChild(Expand);

        TutorialDismiss = new Button { Name = "ObjectiveTutorialDismiss", Text = "Dismiss tutorial", Visible = false };
        row.AddChild(TutorialDismiss);

        RankedList = new VBoxContainer { Name = "ObjectiveRankedList", Visible = false };
        body.AddChild(RankedList);
    }

    /// <summary>
    /// Rebuild the chip from a fresh <see cref="ObjectiveAdvisor.Suggest"/> pass over
    /// <paramref name="state"/>: the top entry's reason renders on the always-visible row (unless
    /// <paramref name="tutorialOverride"/> is given — U23's first-run chain overrides ONLY this
    /// top slot, never the ranked list below, so the live advisor stays reachable via "More"
    /// throughout the tutorial); every entry (including the top one) renders as its own line in
    /// the collapsible ranked list regardless.
    /// </summary>
    public void Refresh(GameState state, string? tutorialOverride = null)
    {
        var suggestions = ObjectiveAdvisor.Suggest(state);
        Reason.Text = tutorialOverride ?? (suggestions.Count > 0 ? suggestions[0].Reason : NoObjectiveText);
        TutorialDismiss.Visible = tutorialOverride is not null;

        foreach (var child in RankedList.GetChildren())
        {
            RankedList.RemoveChild(child);
            child.Free();
        }

        for (var i = 0; i < suggestions.Count; i++)
        {
            RankedList.AddChild(new Label { Name = $"ObjectiveRank_{i}", Text = suggestions[i].Reason });
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
    private static readonly (DayPhase Phase, string Label)[] KernelOrder =
    {
        (DayPhase.Morning, "Morning"),
        (DayPhase.Expedition, "Expedition"),
        (DayPhase.Camp, "Camp"),
        (DayPhase.ExpeditionDeep, "Deep"),
        (DayPhase.Evening, "Evening"),
    };

    private Label[] _phaseLabels = System.Array.Empty<Label>();
    private Label _waiting = null!;

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
        // restyle every HBoxContainer in the app. Without this the 5 phase labels + the
        // waiting indicator sat with zero gap and read as run-on text.
        AddThemeConstantOverride("separation", 12);

        _phaseLabels = new Label[KernelOrder.Length];
        for (var i = 0; i < KernelOrder.Length; i++)
        {
            var (phase, text) = KernelOrder[i];
            var label = new Label { Name = $"TimelinePhase_{phase}", Text = text };
            label.AddThemeColorOverride("font_color", GameTheme.BodyTextColor);
            AddChild(label);
            _phaseLabels[i] = label;
        }

        _waiting = new Label { Name = "TimelineWaiting", Text = "[waiting]", Visible = false };
        _waiting.AddThemeColorOverride("font_color", GameTheme.EmberColor);
        AddChild(_waiting);
    }

    /// <summary>Highlight <paramref name="current"/> among the 5 phase labels and show/hide the
    /// engaged-wait indicator per <paramref name="waiting"/>.</summary>
    public void Refresh(DayPhase current, bool waiting)
    {
        Current = current;
        for (var i = 0; i < KernelOrder.Length; i++)
        {
            var isCurrent = KernelOrder[i].Phase == current;
            _phaseLabels[i].AddThemeColorOverride(
                "font_color", isCurrent ? GameTheme.AccentColor : GameTheme.BodyTextColor);
        }

        _waiting.Visible = waiting;
    }
}
