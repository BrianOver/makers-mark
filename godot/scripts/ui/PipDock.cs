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
///
/// <para><b>U9 (world-and-interiors plan, R5) — "too small and unsure what its even supposed to
/// show."</b> The dock now NAMES itself (<see cref="_titleLabel"/>, "SCRYING MIRROR" — it never
/// said what it was), states its expand affordance in plain words ("Watch the delve ⤢" instead
/// of the unlabelled "Mirror ⤢"), and shows a row of party HP pips (<see cref="_hpPipsRow"/>,
/// plain <see cref="ColorRect"/>s — never a viewport, this dock stays text/rect-only by design)
/// so the party's state reads at a glance without opening the Mirror at all. <see
/// cref="DockHeight"/> grows 76→96px to fit the extra two rows without truncating anything.</para>
/// </summary>
public partial class PipDock : Control
{
    private const float SlideSeconds = 0.35f; // accumulated-delta slide, not a Tween
    private const float DockWidth = 300f;
    private const float DockHeight = 96f; // U9 (R5): was 76 — "too small" — grew for the title + HP pips rows

    /// <summary>Fixed size of one party-member HP pip (U9, R5) — small enough that three (the v1
    /// party cap, <c>MineWatch.MaxFigures</c>) sit comfortably inside <see cref="DockWidth"/>.</summary>
    private static readonly Vector2 PipSize = new(18f, 12f);

    private PanelContainer _root = null!;
    private Label _titleLabel = null!;
    private Label _feedLabel = null!;
    private Label _partyLabel = null!;
    private Button _cycleButton = null!;
    private Button _expandButton = null!;
    private HBoxContainer _hpPipsRow = null!;

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

        // U9 (R5): "unsure what its even supposed to show" — the dock never named itself. A
        // small dim title, same styling convention as MainUi's own ClockLabel caption.
        _titleLabel = new Label
        {
            Name = "PipTitle",
            Text = "SCRYING MIRROR",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _titleLabel.AddThemeColorOverride("font_color", GameTheme.TextDim);
        _titleLabel.AddThemeFontSizeOverride("font_size", GameTheme.LegibilityFloor);
        body.AddChild(_titleLabel);

        var headerRow = new HBoxContainer();
        body.AddChild(headerRow);

        _partyLabel = new Label { Name = "PipPartyLabel", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        headerRow.AddChild(_partyLabel);

        _cycleButton = new Button { Name = "PipCycle", Text = "▸", Visible = false };
        _cycleButton.Pressed += CycleActiveParty;
        headerRow.AddChild(_cycleButton);

        // U9 (R5): party HP at a glance — plain ColorRects (never a viewport in this dock),
        // populated by UpdateHpPips from the same InFlight/Heroes data MineWatch's own Camp
        // rendering reads. Empty (no children) whenever the active party's live hp isn't known
        // yet (Expedition/Rumored — nothing fabricated).
        _hpPipsRow = new HBoxContainer { Name = "PipHpPips" };
        body.AddChild(_hpPipsRow);

        _expandButton = new Button { Name = "PipExpand", Text = "Watch the delve ⤢" };
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

    /// <summary>
    /// Hold the dock retracted while another surface owns the screen.
    ///
    /// <para>Set by <c>MainUi.UpdateEngaged</c> from the same "a drawer/interior/modal owns the screen"
    /// predicate the objective chip uses. Without it the dock is governed by phase ALONE, so during
    /// Expedition/Camp/Deep it slid in over whatever the player had opened — a rendered playtest caught it
    /// sitting on top of the Depths panel, obscuring the Gloomwood venue card. The Depths panel is showing the
    /// same party in more detail at that moment, so the dock is not merely overlapping, it is redundant.</para>
    /// </summary>
    public bool Suppressed { get; set; }

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
        UpdateHpPips(state);
        UpdateLabels();
    }

    /// <summary>
    /// U9 (R5): rebuild the active party's HP pip row from <see cref="GameState.InFlight"/> — the
    /// same live per-hero hp <c>MineWatch.RenderCamp</c> reads, matched to the dock's active card
    /// by <see cref="JourneyStream.PartyKeyOf"/> (the same stable identity <see cref="_feed"/>
    /// already keys its cards on). Empty whenever the active card has no matching InFlight entry
    /// yet (Expedition/Rumored — hp isn't known, so nothing is fabricated) or once it resolved
    /// (Camp/Deep only — the sim's only live-hp source).
    /// </summary>
    private void UpdateHpPips(GameState state)
    {
        foreach (var child in _hpPipsRow.GetChildren())
        {
            _hpPipsRow.RemoveChild(child);
            child.QueueFree();
        }

        if (_activeIndex >= _feed.Cards.Count)
        {
            return;
        }

        var card = _feed.Cards[_activeIndex];
        var inFlight = state.InFlight.FirstOrDefault(f => JourneyStream.PartyKeyOf(f.Party) == card.PartyKey);
        if (inFlight is null)
        {
            return;
        }

        foreach (var heroId in inFlight.Party)
        {
            var hp = inFlight.Hp.TryGetValue(heroId.Value, out var hpValue) ? hpValue : 0;
            var maxHp = state.Heroes.TryGetValue(heroId.Value, out var hero) ? hero.MaxHp : 0;
            var fraction = maxHp > 0 ? Mathf.Clamp((float)hp / maxHp, 0f, 1f) : 1f;
            var name = state.Heroes.TryGetValue(heroId.Value, out var named) ? named.Name : $"Hero #{heroId.Value}";

            _hpPipsRow.AddChild(new ColorRect
            {
                Name = $"HpPip_{heroId.Value}",
                CustomMinimumSize = PipSize,
                Color = PipColorFor(fraction),
                TooltipText = $"{name} — {hp}/{maxHp} hp",
            });
        }
    }

    /// <summary>Green above 60% hp, amber down to 30%, red below — same three-tier read
    /// <c>MineWatch</c>'s slump pose already uses at the 40% line, widened to a full ramp since a
    /// flat pip has no pose to fall back on.</summary>
    private static Color PipColorFor(float fraction) =>
        fraction >= 0.6f ? GameTheme.GoodColor : fraction >= 0.3f ? GameTheme.WarnColor : GameTheme.DangerColor;

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

        // Suppressed wins over the phase. Read here as well as in Refresh so the dock RETRACTS the moment a
        // surface opens, rather than waiting for the next state tick to tell it.
        var target = _wantsVisible && !Suppressed ? 1f : 0f;
        var step = (float)delta / SlideSeconds;
        _slideProgress = Mathf.MoveToward(_slideProgress, target, step);
        Docked = _wantsVisible && !Suppressed;

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
        // U-EXP1: hero names instead of the generic "Party" label — the corner dock is the ONE
        // surface guaranteed to render every raid tick regardless of which (if any) drawer is
        // open (RefreshAll calls Pip.Refresh unconditionally), so it is the one place "who went"
        // was worth the few extra characters even under this dock's tight width.
        var names = card.PartyNames.IsEmpty ? "Party" : string.Join(", ", card.PartyNames);
        _partyLabel.Text = card.Stage == JourneyStage.Rumored
            ? $"{names} — floor {card.TargetFloor} (rumored)"
            : $"{names} — floor {card.DeepestFloorCleared}/{card.TargetFloor}";

        var revealed = _feed.Revealed(card);
        CurrentBeats = revealed.Select(b => b.Text).ToImmutableList();

        // U-EXP1: before any real beat exists (Rumored stage — see JourneyStream.DepartureLine),
        // lead with "who carries what you made" instead of the bare "a party sets out" rumor —
        // this one line is the emotional payoff the whole premise promises, and it is now known
        // complete the instant the party departs.
        _feedLabel.Text = revealed.Count > 0
            ? revealed[^1].Text
            : card.Stage == JourneyStage.Rumored
                ? JourneyStream.DepartureLine(card)
                : _feed.IdleLine(card.PartyKey);
    }
}
