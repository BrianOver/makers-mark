using System;
using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using Godot;

namespace GodotClient.Ui;

/// <summary>
/// U16 (KTD13 HUD layout spec): the picture-in-picture journey dock — "PiP docks bottom-right
/// above the ticker (expedition phases only)". A small always-on-top corner widget (code-built,
/// mounted directly on <c>MainUi</c>, never inside the Tabs) showing the active live party's most
/// recent revealed beat off the same <see cref="JourneyFeed"/>/<see cref="JourneyStream"/> every
/// other spectate surface reads. Visible only during <see cref="DayPhase.Expedition"/>/
/// <see cref="DayPhase.Camp"/>/<see cref="DayPhase.ExpeditionDeep"/> — hidden at
/// <see cref="DayPhase.Morning"/>/<see cref="DayPhase.Evening"/>, sliding out/in on the transition
/// (accumulated-delta only, no Tween — repo convention). Click the arrow to cycle the active party
/// among however many are live; click the body to raise <see cref="ExpandRequested"/>, which
/// <c>MainUi</c> wires to <c>ScryingMirror.ShowMirror</c>.
/// </summary>
public partial class PipDock : Control
{
    private const float SlideSeconds = 0.35f; // accumulated-delta slide, not a Tween
    private const float DockWidth = 300f;
    private const float DockHeight = 76f;

    private PanelContainer _root = null!;
    private Label _feedLabel = null!;
    private Label _partyLabel = null!;
    private Button _cycleButton = null!;
    private Button _expandButton = null!;

    private readonly JourneyFeed _feed = new();
    private int _activeIndex;
    private bool _built;
    private bool _wantsVisible;
    private float _slideProgress; // 0 = fully hidden (slid out), 1 = fully docked

    /// <summary>Raised when the player clicks the dock's body to expand to the full mirror.</summary>
    public event Action? ExpandRequested;

    /// <summary>U25 follow-up (a): wired by <c>MainUi</c> so the feed pauses with the clock
    /// (paused ≠ engaged — an engaged surface keeps the feed flowing per KTD3). Null in every
    /// test that never wires a <see cref="PhaseClock"/> — treated as "always playing" (the
    /// pre-U25 behavior), never a crash.</summary>
    public PhaseClock? Clock { get; set; }

    /// <summary>The active party's currently revealed beat lines (test hook — same KTD5/AE2
    /// self-censor guarantee every other spectate surface carries).</summary>
    public ImmutableList<string> CurrentBeats { get; private set; } = ImmutableList<string>.Empty;

    /// <summary>How many parties currently have a live card (test hook).</summary>
    public int PartyCount => _feed.Cards.Count;

    /// <summary>The active party's index within the live cards (test hook).</summary>
    public int ActiveIndex => _activeIndex;

    /// <summary>True while the dock should be showing (expedition phases) — distinct from
    /// <see cref="CanvasItem.Visible"/>, which stays true through the slide-out animation so the
    /// tail end of the slide still renders (test/tuning hook).</summary>
    public bool Docked { get; private set; }

    public void Build()
    {
        if (_built)
        {
            return;
        }

        Name = "PipDock";
        MouseFilter = MouseFilterEnum.Pass;
        CustomMinimumSize = new Vector2(DockWidth, DockHeight);
        Size = new Vector2(DockWidth, DockHeight);
        Visible = false; // _Process's slide-in owns visibility from here — never flash at (0,0)

        // Anchored to the bottom-right corner (KTD13: "PiP docks bottom-right"); OffsetLeft/Top/
        // Right/Bottom (set every frame in _Process) are then relative to that corner.
        AnchorLeft = 1f;
        AnchorTop = 1f;
        AnchorRight = 1f;
        AnchorBottom = 1f;

        _root = new PanelContainer { Name = "PipDockPanel" };
        _root.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_root);
        var body = new VBoxContainer();
        _root.AddChild(body);

        var headerRow = new HBoxContainer();
        body.AddChild(headerRow);

        _partyLabel = new Label { Name = "PipPartyLabel", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        headerRow.AddChild(_partyLabel);

        _cycleButton = new Button { Name = "PipCycle", Text = "▸", Visible = false };
        _cycleButton.Pressed += CycleActiveParty;
        headerRow.AddChild(_cycleButton);

        _expandButton = new Button { Name = "PipExpand", Text = "Mirror ⤢" };
        _expandButton.Pressed += () => ExpandRequested?.Invoke();
        body.AddChild(_expandButton);

        _feedLabel = new Label
        {
            Name = "PipFeedLabel",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        body.AddChild(_feedLabel);

        _built = true;
    }

    /// <summary>Rebuild this tick's cards and recompute the dock's show/hide intent (KTD13). Call
    /// once per completed tick, same contract as every other panel's <c>Refresh</c>.</summary>
    public void Refresh(GameState state, ImmutableList<GameEvent> lastEvents)
    {
        Build();
        _feed.Refresh(state, lastEvents);
        if (_activeIndex >= _feed.Cards.Count)
        {
            _activeIndex = 0;
        }

        _wantsVisible = state.Phase is DayPhase.Expedition or DayPhase.Camp or DayPhase.ExpeditionDeep;
        UpdateLabels();
    }

    /// <summary>Cycle the active party among however many are live (test hook + button handler).</summary>
    public void CycleActiveParty()
    {
        if (_feed.Cards.Count == 0)
        {
            return;
        }

        _activeIndex = (_activeIndex + 1) % _feed.Cards.Count;
        UpdateLabels();
    }

    public override void _Process(double delta)
    {
        if (!_built)
        {
            return;
        }

        // U25 (a): feed pauses with the clock (paused ≠ engaged — see MineWatch's matching wiring).
        _feed.Advance(delta, paused: Clock is not null && !Clock.Playing);
        UpdateLabels();

        var target = _wantsVisible ? 1f : 0f;
        var step = (float)delta / SlideSeconds;
        _slideProgress = Mathf.MoveToward(_slideProgress, target, step);
        Docked = _wantsVisible;

        // Slide from fully off-screen-right to docked, accumulated-delta only (no Tween).
        var hiddenOffsetX = DockWidth + 24f;
        OffsetRight = -24f + hiddenOffsetX * (1f - _slideProgress);
        OffsetLeft = OffsetRight - DockWidth;
        OffsetBottom = -24f;
        OffsetTop = OffsetBottom - DockHeight;
        Visible = _slideProgress > 0f;
    }

    private void UpdateLabels()
    {
        _cycleButton.Visible = _feed.Cards.Count > 1;

        if (_feed.Cards.IsEmpty)
        {
            CurrentBeats = ImmutableList<string>.Empty;
            _partyLabel.Text = string.Empty;
            _feedLabel.Text = string.Empty;
            return;
        }

        var card = _feed.Cards[_activeIndex];
        _partyLabel.Text = card.Stage == JourneyStage.Rumored
            ? $"Party — floor {card.TargetFloor} (rumored)"
            : $"Party — floor {card.DeepestFloorCleared}/{card.TargetFloor}";

        var revealed = _feed.Revealed(card);
        CurrentBeats = revealed.Select(b => b.Text).ToImmutableList();

        _feedLabel.Text = revealed.Count > 0
            ? revealed[^1].Text
            : card.Stage == JourneyStage.Rumored
                ? $"A party sets out for floor {card.TargetFloor}…"
                : _feed.IdleLine(card.PartyKey);
    }
}
