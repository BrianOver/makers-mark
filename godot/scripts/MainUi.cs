using System.Globalization;
using System.Linq;
using GameSim.Contracts;
using Godot;
using GodotClient.Panels;
using GodotClient.Town;

namespace GodotClient;

/// <summary>
/// The one UI scene (U11 shell + U12 town layer): a persistent tab bar hosting the
/// living town view plus the six management panels over a top status bar
/// (day/phase/gold + play/pause/fast-forward), with the Evening Ledger as a modal
/// overlay. The Ledger opens through the U12 Return Ritual — a TIME-BASED gate
/// (<see cref="ReturnRitualDelaySeconds"/> after the Evening tick, scaled by clock
/// speed), never blocked by sprite walk-ins, so a zero-survivor day cannot hang the
/// reveal. Owns the single <see cref="SimAdapter"/> and the <see cref="PhaseClock"/>;
/// everything below binds through the adapter (KTD2). Town clicks select tabs (R20).
/// </summary>
public partial class MainUi : Control
{
    /// <summary>
    /// Return Ritual gate (U12 pinned design): fixed reveal delay after the Evening
    /// tick at 1x, scaled by the PhaseClock multiplier. The walk-in is decoration;
    /// this timer is the gate.
    /// </summary>
    public const double ReturnRitualDelaySeconds = 3.0;

    /// <summary>Campaign seed — same seed, same world, everywhere (KTD4).</summary>
    [Export]
    public int Seed { get; set; } = 2026;

    /// <summary>
    /// Scenario injection for engine tests/replays: set BEFORE the node enters the
    /// tree to bind the shell to a prepared campaign instead of a fresh <see cref="Seed"/> one.
    /// </summary>
    public SimAdapter? AdapterOverride { get; set; }

    public SimAdapter Adapter { get; private set; } = null!;
    public PhaseClock Clock { get; private set; } = null!;
    public TabContainer Tabs { get; private set; } = null!;
    public TownScene Town { get; private set; } = null!;
    public ForgePanel Forge { get; private set; } = null!;
    public ShopPanel Shop { get; private set; } = null!;
    public HeroesPanel Heroes { get; private set; } = null!;
    public TavernPanel Tavern { get; private set; } = null!;
    public DepthsPanel Depths { get; private set; } = null!;
    public BountyPanel Bounties { get; private set; } = null!;
    public LedgerModal Ledger { get; private set; } = null!;

    /// <summary>The most recent day whose Evening completed — what the Ledger button reopens.</summary>
    public int LastCompletedDay { get; private set; }

    /// <summary>Seconds left on the Return Ritual gate; 0 when no reveal is pending.</summary>
    public double LedgerDelayRemaining { get; private set; }

    private int _pendingLedgerDay;
    private Label _status = null!;
    private Label _clockLabel = null!;
    private Label _rejections = null!;
    private Button _playPause = null!;
    private Button _speed = null!;
    private bool _resumePlayOnLedgerClose;

    public override void _Ready()
    {
        Adapter = AdapterOverride ?? new SimAdapter((ulong)Seed);
        Clock = new PhaseClock(Adapter);
        BuildUi();

        Adapter.StateChanged += OnPhaseCompleted;
        Town.Bind(Adapter);
        Forge.Bind(Adapter);
        Shop.Bind(Adapter);
        Heroes.Bind(Adapter);
        Tavern.Bind(Adapter);
        Depths.Bind(Adapter);
        Bounties.Bind(Adapter);
        Ledger.Bind(Adapter);

        RefreshStatus();
        UpdateClockLabel();
        GD.Print($"[MainUi] campaign started, seed {Seed}");
    }

    public override void _Process(double delta)
    {
        if (Clock is null)
        {
            return;
        }

        Clock.Update(delta);
        UpdateClockLabel();

        // Return Ritual gate (U12): the reveal lands a fixed, speed-scaled interval
        // after the Evening tick — decoration timer, deliberately independent of
        // play/pause so a paused town still keeps its promised reveal.
        if (LedgerDelayRemaining > 0)
        {
            LedgerDelayRemaining -= delta * Clock.SpeedMultiplier;
            if (LedgerDelayRemaining <= 0)
            {
                LedgerDelayRemaining = 0;
                Ledger.ShowFor(_pendingLedgerDay);
            }
        }
    }

    private void OnPhaseCompleted(DayPhase completedPhase, int completedDay)
    {
        var state = Adapter.CurrentState;
        GD.Print($"[MainUi] tick complete: day {completedDay} {completedPhase} -> day {state.Day} {state.Phase} " +
                 $"({Adapter.LastEvents.Count} events, {Adapter.LastRejections.Count} rejections)");
        foreach (var rejected in Adapter.LastRejections)
        {
            GD.PushWarning($"[MainUi] rejected {rejected.Action.GetType().Name}: {rejected.Reason}");
        }

        RefreshAll();
        Town.OnPhaseCompleted(completedPhase);

        if (completedPhase == DayPhase.Evening)
        {
            // U12 Return Ritual: arm the time-based gate instead of opening the
            // Ledger immediately — _Process fires the reveal when the delay elapses,
            // however many sprites walked back in (zero on a full wipe).
            LastCompletedDay = completedDay;
            _pendingLedgerDay = completedDay;
            LedgerDelayRemaining = ReturnRitualDelaySeconds;
            // The reveal fires from _Process when the gate elapses; the Ledger's
            // visibility handler pauses the clock at that point.
        }
    }

    /// <summary>Re-render the status bar and every panel from CurrentState.</summary>
    public void RefreshAll()
    {
        RefreshStatus();
        Town.Refresh();
        Forge.Refresh();
        Shop.Refresh();
        Heroes.Refresh();
        Tavern.Refresh();
        Depths.Refresh();
        Bounties.Refresh();
        Ledger.Refresh();
    }

    private void RefreshStatus()
    {
        var state = Adapter.CurrentState;
        var alive = state.Heroes.Values.Count(h => h.Alive);
        _status.Text = $"Day {state.Day} — {state.Phase} | Gold {state.Player.Gold}g | Heroes {alive}/{state.Heroes.Count}";
        _rejections.Text = Adapter.LastRejections.IsEmpty
            ? string.Empty
            : "REJECTED: " + string.Join(" | ", Adapter.LastRejections.Select(r => r.Reason));
    }

    private void UpdateClockLabel()
    {
        var remaining = Clock.Remaining.ToString("0", CultureInfo.InvariantCulture);
        var paused = Clock.Playing ? string.Empty : " [paused]";
        _clockLabel.Text = $"next phase in {remaining}s @{Clock.SpeedMultiplier}x{paused}";
        _playPause.Text = Clock.Playing ? "Pause" : "Play";
        _speed.Text = $"{Clock.SpeedMultiplier}x";
    }

    private void BuildUi()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        var layout = new VBoxContainer { Name = "Layout" };
        layout.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(layout);

        // --- status bar -----------------------------------------------------
        var statusBar = new HBoxContainer { Name = "StatusBar" };
        layout.AddChild(statusBar);
        _status = new Label { Name = "StatusLabel" };
        statusBar.AddChild(_status);
        // Gold glyph (U16) sits by the day/phase/gold readout in StatusLabel.
        statusBar.AddChild(new TextureRect
        {
            Name = "GoldIcon",
            Texture = IconRegistry.Glyph("gold"),
            CustomMinimumSize = new Vector2(20, 20),
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        });
        var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        statusBar.AddChild(spacer);
        _clockLabel = new Label { Name = "ClockLabel" };
        statusBar.AddChild(_clockLabel);

        _playPause = new Button { Name = "PlayPause", Text = "Pause" };
        _playPause.Pressed += () =>
        {
            Clock.TogglePlay();
            UpdateClockLabel();
        };
        statusBar.AddChild(_playPause);

        _speed = new Button { Name = "Speed", Text = "1x" };
        _speed.Pressed += () =>
        {
            Clock.CycleSpeed();
            UpdateClockLabel();
        };
        statusBar.AddChild(_speed);

        var ledgerButton = new Button { Name = "OpenLedger", Text = "Ledger" };
        ledgerButton.Pressed += () => Ledger.ShowFor(LastCompletedDay);
        statusBar.AddChild(ledgerButton);

        _rejections = new Label { Name = "Rejections", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _rejections.AddThemeColorOverride("font_color", new Color(1f, 0.5f, 0.5f));
        layout.AddChild(_rejections);

        // --- panel tabs (tab title = scene root node name) -------------------
        Tabs = new TabContainer
        {
            Name = "Tabs",
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        layout.AddChild(Tabs);
        Town = InstantiatePanel<TownScene>("res://scenes/town/town_scene.tscn"); // first tab (U12)
        Town.Clock = Clock;
        Town.HeroClicked += OnTownHeroClicked;
        Town.BuildingClicked += OnTownBuildingClicked;
        Forge = InstantiatePanel<ForgePanel>("res://scenes/panels/forge_panel.tscn");
        Shop = InstantiatePanel<ShopPanel>("res://scenes/panels/shop_panel.tscn");
        Heroes = InstantiatePanel<HeroesPanel>("res://scenes/panels/heroes_panel.tscn");
        Tavern = InstantiatePanel<TavernPanel>("res://scenes/panels/tavern_panel.tscn");
        Depths = InstantiatePanel<DepthsPanel>("res://scenes/panels/depths_panel.tscn");
        Bounties = InstantiatePanel<BountyPanel>("res://scenes/panels/bounty_panel.tscn");

        // --- ledger modal overlay (sibling after the layout = draws on top) --
        Ledger = GD.Load<PackedScene>("res://scenes/panels/ledger_modal.tscn").Instantiate<LedgerModal>();
        AddChild(Ledger);
        Ledger.SetAnchorsPreset(LayoutPreset.FullRect);
        Ledger.VisibilityChanged += OnLedgerVisibilityChanged;
    }

    private T InstantiatePanel<T>(string scenePath) where T : SimPanel
    {
        var panel = GD.Load<PackedScene>(scenePath).Instantiate<T>();
        Tabs.AddChild(panel);
        return panel;
    }

    /// <summary>Town hero click (R20): jump to the Heroes tab with that hero's detail bound.</summary>
    private void OnTownHeroClicked(int heroValue)
    {
        Tabs.CurrentTab = Tabs.GetTabIdxFromControl(Heroes);
        Heroes.SelectHero(heroValue);
    }

    /// <summary>Town building click (R20): jump to the matching management tab.</summary>
    private void OnTownBuildingClicked(string building)
    {
        Control target = building switch
        {
            "Forge" => Forge,
            "Shop" => Shop,
            "Tavern" => Tavern,
            _ => Town,
        };
        Tabs.CurrentTab = Tabs.GetTabIdxFromControl(target);
    }

    /// <summary>Reading the Ledger pauses the town; closing it resumes if it was running.</summary>
    private void OnLedgerVisibilityChanged()
    {
        if (Ledger.Visible)
        {
            _resumePlayOnLedgerClose = Clock.Playing;
            Clock.Pause();
            LedgerDelayRemaining = 0; // a manual open satisfies the pending Return Ritual
        }
        else if (_resumePlayOnLedgerClose)
        {
            Clock.Play();
        }

        UpdateClockLabel();
    }
}
