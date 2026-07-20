using System;
using System.Globalization;
using System.Linq;
using GameSim.Contracts;
using Godot;
using GodotClient.Panels;
using GodotClient.Town;
using GodotClient.Ui;

namespace GodotClient;

/// <summary>
/// The one UI scene (U11 shell + U12 town layer): a persistent tab bar hosting the
/// living town view plus the six management panels under a themed HUD header (P007
/// U7 — day/phase/gold/heroes stat chips + Advance/Auto, with play/pause/fast-forward
/// as auto-mode sub-controls), with the Evening Ledger as a modal overlay. The Ledger opens
/// through the U12 Return Ritual — a TIME-BASED gate
/// (<see cref="ReturnRitualDelaySeconds"/> of unscaled wall-clock after the Evening
/// tick), never blocked by sprite walk-ins, so a zero-survivor day cannot hang the
/// reveal. Owns the single <see cref="SimAdapter"/> and the <see cref="PhaseClock"/>;
/// everything below binds through the adapter (KTD2). Town clicks select tabs (R20).
/// </summary>
public partial class MainUi : Control
{
    /// <summary>
    /// Return Ritual gate (U12 pinned design, U2 revision): fixed reveal delay of
    /// UNSCALED wall-clock seconds after the Evening tick — independent of the
    /// auto-advance flag, Playing state, and speed multiplier, so the gated (auto
    /// OFF) clock still delivers its promised reveal. The walk-in is decoration;
    /// this timer is the gate.
    /// </summary>
    public const double ReturnRitualDelaySeconds = 3.0;

    /// <summary>
    /// U6 (R6) toast lifetime: a surfaced rejection renders as a short player-phrased
    /// line for this many UNSCALED wall-clock seconds, then clears (or earlier, on the
    /// next clean tick). The raw kernel reason never renders — it goes to the dev log.
    /// </summary>
    public const double RejectionToastSeconds = 4.0;

    /// <summary>Campaign seed — same seed, same world, everywhere (KTD4).</summary>
    [Export]
    public int Seed { get; set; } = 2026;

    /// <summary>
    /// Scenario/campaign injection: set BEFORE the node enters the tree to bind the shell
    /// to a prepared campaign instead of a fresh <see cref="Seed"/> one. STATIC (U4) so the
    /// new-game profession select can hand a freshly seeded campaign across
    /// <c>ChangeSceneToFile</c> (a new MainUi instance exists only after the swap).
    /// Consumed — cleared — by <see cref="_Ready"/>, so a stale override never leaks into
    /// a later mount.
    /// </summary>
    public static SimAdapter? AdapterOverride { get; set; }

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
    public CampPanel Camp { get; private set; } = null!;
    public TabFade TabFade { get; private set; } = null!;

    /// <summary>The most recent day whose Evening completed — what the Ledger button reopens.</summary>
    public int LastCompletedDay { get; private set; }

    /// <summary>Seconds left on the Return Ritual gate; 0 when no reveal is pending.</summary>
    public double LedgerDelayRemaining { get; private set; }

    /// <summary>Seconds left on the rejection toast; 0 when no toast is showing (U6).</summary>
    public double ToastRemaining { get; private set; }

    private int _pendingLedgerDay;
    private HBoxContainer _statChips = null!;
    private Label _clockLabel = null!;
    private PanelContainer _toastBanner = null!;
    private Label _toast = null!;
    private Button _advance = null!;
    private Button _auto = null!;
    private Button _playPause = null!;
    private Button _speed = null!;
    private bool _resumePlayOnLedgerClose;
    private bool _resumePlayOnCampClose;

    // ── LW3: gold-chip bounce-scale pop (StatusBar region) ────────────────────────────────────
    // No engine Tween in this codebase (LitTownOverlay/HeroSprite precedent: accumulated-delta
    // math only, so the pop is deterministic and headless-testable via direct _Process calls,
    // same as TownScene.Animate). -1 = not popping.
    private const double GoldPopSeconds = 0.3;
    private Label? _goldValueLabel;
    private double _goldPopElapsed = -1;

    public override void _Ready()
    {
        Adapter = AdapterOverride ?? new SimAdapter((ulong)Seed);
        AdapterOverride = null; // consumed — the handoff is one-shot (see property doc)
        Clock = new PhaseClock(Adapter);

        // P007 U1 (R11/KTD1): assign the shared Theme BEFORE building any child Control so
        // Godot's normal Theme cascade carries it to every panel/tab built below.
        Theme = GameTheme.Build();
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
        Camp.Bind(Adapter);

        RefreshStatus();
        UpdateClockLabel();
        SyncCampModal(); // adopt an injected mid-day (parked) campaign — open the slate if already at Camp
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

        // Return Ritual gate (U12, U2 revision): the reveal lands a fixed UNSCALED
        // wall-clock interval after the Evening tick — decoration timer, deliberately
        // independent of the auto flag, play/pause, and speed, so the gated (auto OFF)
        // or paused town still keeps its promised reveal.
        if (LedgerDelayRemaining > 0)
        {
            LedgerDelayRemaining -= delta;
            if (LedgerDelayRemaining <= 0)
            {
                LedgerDelayRemaining = 0;
                Ledger.ShowFor(_pendingLedgerDay);
            }
        }

        // U6 rejection toast: transient by design — it fades on unscaled wall-clock
        // (same _Process pattern as the Return Ritual gate) or on the next clean tick.
        if (ToastRemaining > 0)
        {
            ToastRemaining -= delta;
            if (ToastRemaining <= 0)
            {
                ClearToast();
            }
        }

        // LW3: the gold-chip bounce-scale pop (1.0→1.25→1.0), armed by RefreshStatus whenever the
        // just-completed tick's LastEvents carried a player-shelf sale.
        if (_goldPopElapsed >= 0 && _goldValueLabel is not null)
        {
            _goldPopElapsed += delta;
            var t = Mathf.Clamp((float)(_goldPopElapsed / GoldPopSeconds), 0f, 1f);
            _goldValueLabel.Scale = Vector2.One * GoldPopScale(t);
            if (t >= 1f)
            {
                _goldPopElapsed = -1;
            }
        }

        // LW6: tick the tab-switch fade veil (no-op unless a dip is in flight).
        TabFade.Tick(delta);
    }

    private void OnPhaseCompleted(DayPhase completedPhase, int completedDay)
    {
        var state = Adapter.CurrentState;
        GD.Print($"[MainUi] tick complete: day {completedDay} {completedPhase} -> day {state.Day} {state.Phase} " +
                 $"({Adapter.LastEvents.Count} events, {Adapter.LastRejections.Count} rejections)");
        foreach (var rejected in Adapter.LastRejections)
        {
            // Dev log keeps the RAW kernel reason (org logging rule); the player only
            // ever sees the friendly toast below.
            GD.PushWarning($"[MainUi] rejected {rejected.Action.GetType().Name}: {rejected.Reason}");
        }

        // U6 (R6) toast half: surfaced refusals render as a short player-phrased line
        // that auto-clears (wall-clock in _Process, or here on the next clean tick).
        // The raw kernel string never reaches a rendered control.
        if (Adapter.LastRejections.IsEmpty)
        {
            ClearToast();
        }
        else
        {
            _toast.Text = string.Join("  ",
                Adapter.LastRejections.Select(r => FriendlyRejection(r.Reason)).Distinct());
            _toastBanner.Visible = true; // U7: transient banner, hidden except while a toast is live
            ToastRemaining = RejectionToastSeconds;
        }

        RefreshAll();
        Town.OnPhaseCompleted(completedPhase);
        Shop.OnPhaseCompleted(completedPhase); // LW3: stage the day's shop customers/coin flourish
        SyncCampModal(); // V7a: raise the winch-house slate the moment a party parks at Camp

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
        Camp.Refresh();
    }

    /// <summary>
    /// V7a phase hook: raise the camp slate the instant a party parks (Phase == Camp with a
    /// non-empty InFlight), and drop it once the parked run finalizes (InFlight cleared at the
    /// Deep tick). Deliberately does NOT auto-close merely on leaving Camp — the just-completed
    /// Camp tick's rejections must stay legible on the slate through the Deep phase (AE4), and the
    /// player's own Hold is the normal close. No new tab, so the MainUiTests tab-title pin is untouched.
    /// </summary>
    private void SyncCampModal()
    {
        var state = Adapter.CurrentState;
        if (state.InFlight.IsEmpty)
        {
            Camp.CloseModal();
        }
        else if (state.Phase == DayPhase.Camp)
        {
            Camp.ShowModal();
        }
    }

    /// <summary>
    /// P007 U7 (R11/R12/KD1): rebuild the HUD's stat-chip row from CurrentState. Rebuilt (not
    /// mutated in place) each call — mirrors the panels' own Clear-then-compose Refresh pattern
    /// (KTD2) so the chips can never drift from live state between ticks.
    /// </summary>
    private void RefreshStatus()
    {
        var state = Adapter.CurrentState;
        var alive = state.Heroes.Values.Count(h => h.Alive);

        foreach (var child in _statChips.GetChildren())
        {
            _statChips.RemoveChild(child);
            child.Free();
        }

        _statChips.AddChild(NamedStatChip("DayChip", "Day", $"{state.Day}"));
        var phaseChip = NamedStatChip("PhaseChip", "Phase", state.Phase.ToString(), UiKit.ChipTone.Accent);
        phaseChip.TooltipText = PhaseLegend;
        _statChips.AddChild(phaseChip);

        var goldChip = BuildGoldChip(state.Player.Gold);
        _statChips.AddChild(goldChip);
        _goldValueLabel = goldChip.GetNode<Label>("StatChip/StatChipRow/Value");

        _statChips.AddChild(NamedStatChip(
            "HeroesChip", "Heroes", $"{alive}/{state.Heroes.Count}",
            alive == state.Heroes.Count && state.Heroes.Count > 0 ? UiKit.ChipTone.Positive : UiKit.ChipTone.Neutral));

        // LW3 coin flourish (StatusBar half): a player-shelf sale on THIS tick arms the gold-
        // label pop. ShopStage plays the matching coin-arc off the SAME Adapter.LastEvents batch
        // independently — no cross-panel coupling, the event log is the single source of truth.
        if (Adapter.LastEvents.Any(e => e is ItemSold { FromPlayerShop: true }))
        {
            _goldPopElapsed = 0;
        }
    }

    /// <summary>1.0→1.25→1.0 bounce over the pop's duration — a symmetric sine hump standing in
    /// for the plan's "Trans.Elastic" (no engine Tween in this codebase; accumulated-delta only,
    /// the same determinism contract every other decoration on this project holds).</summary>
    private static float GoldPopScale(float t) => 1f + 0.25f * Mathf.Sin(Mathf.Pi * t);

    /// <summary>P007 U7 (R12/R14): the PhaseChip's legend flyout, one line per phase in the
    /// kernel's own tick order (<see cref="GameSim.Kernel.GameKernel"/>'s Morning→Expedition→
    /// Camp→ExpeditionDeep→Evening transition table — NOT the <see cref="DayPhase"/> enum's
    /// declaration order, which lists Evening before Camp/ExpeditionDeep). Each line names what
    /// happens that phase and what the player can do, mirrored against the handlers' own
    /// <c>CanHandle</c> phase gates so it never drifts from what's actually legal: BuyMaterial is
    /// Morning-only (<see cref="GameSim.Economy.MaterialVendorHandlers"/>); PostBounty is
    /// Morning+Evening (<see cref="GameSim.Bounties.BountyHandlers"/>); BuyOre is Evening-only
    /// (<see cref="GameSim.Economy.OreMarketHandlers"/>); SendSupply/RecallParty are Camp-only
    /// (<see cref="GameSim.Expedition.CampHandlers"/>); craft/stock/price have no phase term at
    /// all (legal every phase).</summary>
    public const string PhaseLegend =
        "Morning — parties muster and recruits arrive. Buy materials from the vendor, post bounties, craft, stock, and price.\n" +
        "Expedition — parties descend toward their target floor. Craft, stock, and price; nothing else resolves until they return.\n" +
        "Camp — a party pauses at its checkpoint before the deep floors. Send supply or recall the party; craft, stock, and price.\n" +
        "Deep — camped parties push into the deeper floors and the run is decided. Craft, stock, and price; nothing else to do but wait.\n" +
        "Evening — heroes return with loot and news. Buy their ore, post bounties, craft, stock, and price.";

    /// <summary>A <see cref="UiKit.StatChip"/> given a discoverable <see cref="Node.Name"/> so
    /// tests can locate the exact chip instead of scanning the whole HUD's rendered text.</summary>
    private static Control NamedStatChip(string name, string label, string value, UiKit.ChipTone tone = UiKit.ChipTone.Neutral)
    {
        var chip = UiKit.StatChip(label, value, tone);
        chip.Name = name;
        return chip;
    }

    /// <summary>The gold chip pairs the existing gold glyph (U16) with a themed StatChip value —
    /// the one place the U16 icon and the P007 U2 widget kit meet.</summary>
    private static Control BuildGoldChip(int gold)
    {
        var wrap = new HBoxContainer { Name = "GoldChip" };
        wrap.AddChild(new TextureRect
        {
            Name = "GoldIcon",
            Texture = IconRegistry.Glyph("gold"),
            CustomMinimumSize = new Vector2(20, 20),
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        });
        wrap.AddChild(UiKit.StatChip("Gold", $"{gold}g", UiKit.ChipTone.Accent));
        return wrap;
    }

    private void ClearToast()
    {
        ToastRemaining = 0;
        _toast.Text = string.Empty;
        _toastBanner.Visible = false; // U7: hide the whole banner, not just the text
    }

    /// <summary>
    /// U6 (R6): map a kernel rejection reason to a short player-phrased toast line.
    /// Presentation only — no rule lives here, and the RAW reason never renders (it
    /// goes to the dev log in <see cref="OnPhaseCompleted"/>). Ordered most-specific
    /// first; unknown reasons fall through to a generic friendly line.
    /// </summary>
    private static string FriendlyRejection(string reason)
    {
        if (reason.StartsWith("Not enough gold", StringComparison.Ordinal)
            || reason.StartsWith("Can't pay the", StringComparison.Ordinal))
        {
            return "You can't afford that yet.";
        }

        if (reason.StartsWith("No handler accepts", StringComparison.Ordinal))
        {
            return "Can't do that right now.";
        }

        if (reason.StartsWith("Not enough ", StringComparison.Ordinal))
        {
            return "You don't have the materials for that.";
        }

        if (reason.StartsWith("No open ore offer", StringComparison.Ordinal)
            || reason.StartsWith("Only ", StringComparison.Ordinal))
        {
            return "That offer is gone.";
        }

        if (reason.Contains("is no longer alive", StringComparison.Ordinal))
        {
            return "That seller never made it home.";
        }

        if (reason.Contains("was already sold", StringComparison.Ordinal))
        {
            return "Sold consumables don't come back.";
        }

        return "That didn't work out.";
    }

    private void UpdateClockLabel()
    {
        _auto.Text = Clock.AutoAdvance ? "Auto: ON" : "Auto: OFF";
        _auto.ButtonPressed = Clock.AutoAdvance; // keep the toggle's pressed look in sync (U7)
        // Play/pause + speed are sub-controls of auto mode — hidden while gated (U2).
        _playPause.Visible = Clock.AutoAdvance;
        _speed.Visible = Clock.AutoAdvance;

        if (Clock.AutoAdvance)
        {
            var remaining = Clock.Remaining.ToString("0", CultureInfo.InvariantCulture);
            var paused = Clock.Playing ? string.Empty : " [paused]";
            _clockLabel.Text = $"next phase in {remaining}s @{Clock.SpeedMultiplier}x{paused}";
            _playPause.Text = Clock.Playing ? "Pause" : "Play";
            _speed.Text = $"{Clock.SpeedMultiplier}x";
        }
        else
        {
            _clockLabel.Text = "next phase on Advance";
        }
    }

    private void BuildUi()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        var layout = new VBoxContainer { Name = "Layout" };
        layout.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(layout);

        // --- HUD header (P007 U7/R11/R12/KD1): themed stat-chip row (left) + the
        // Advance/Auto controls cluster (right) — the real home for the hybrid day
        // clock. Both Advance and Auto drive PhaseClock's ONE gated advance path
        // (AdvanceNow / Update -> SimAdapter.AdvancePhase); nothing here is a second
        // code path (KD1). -----------------------------------------------------
        var header = new PanelContainer { Name = "HudHeader" };
        layout.AddChild(header);
        var headerRow = new HBoxContainer { Name = "HudHeaderRow" };
        header.AddChild(headerRow);

        _statChips = new HBoxContainer { Name = "StatChips", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        headerRow.AddChild(_statChips); // populated by RefreshStatus (day/phase/gold/heroes)

        var controls = new HBoxContainer { Name = "HudControls" };
        headerRow.AddChild(controls);

        _clockLabel = new Label { Name = "ClockLabel" };
        controls.AddChild(_clockLabel);

        // U2 hybrid clock controls: explicit Advance is the primary control (styled as
        // such — StylePrimary — so it reads as THE control); the Auto toggle opts into
        // the timed cadence, where play/pause + speed apply.
        _advance = new Button { Name = "AdvancePhase", Text = "Advance" };
        StylePrimary(_advance);
        _advance.Pressed += () =>
        {
            Clock.AdvanceNow(); // same advance the auto timer fires (R1)
            UpdateClockLabel();
        };
        controls.AddChild(_advance);

        _auto = new Button { Name = "AutoAdvance", Text = "Auto: OFF", ToggleMode = true };
        _auto.Pressed += () =>
        {
            Clock.ToggleAuto();
            UpdateClockLabel();
        };
        controls.AddChild(_auto);

        _playPause = new Button { Name = "PlayPause", Text = "Pause" };
        _playPause.Pressed += () =>
        {
            Clock.TogglePlay();
            UpdateClockLabel();
        };
        controls.AddChild(_playPause);

        _speed = new Button { Name = "Speed", Text = "1x" };
        _speed.Pressed += () =>
        {
            Clock.CycleSpeed();
            UpdateClockLabel();
        };
        controls.AddChild(_speed);

        var ledgerButton = new Button { Name = "OpenLedger", Text = "Ledger" };
        ledgerButton.Pressed += () => Ledger.ShowFor(LastCompletedDay);
        controls.AddChild(ledgerButton);

        // U6/U7 rejection banner: a transient, themed, player-phrased line — hidden
        // except while a toast is live (OnPhaseCompleted shows it, ClearToast/_Process
        // hide it). NOT a persistent status readout, and never the raw kernel string.
        _toastBanner = new PanelContainer { Name = "ToastBanner", Visible = false };
        layout.AddChild(_toastBanner);
        _toast = new Label
        {
            Name = "RejectionToast",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _toast.AddThemeColorOverride("font_color", GameTheme.RejectionColor);
        _toastBanner.AddChild(_toast);

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

        // LW6: tab-switch fade — a purely additive CanvasLayer-100 veil, never touches the
        // TabContainer itself. TabChanged (not TabSelected) so a programmatic jump (hero/building
        // click routing, R20) dips it too, not just a manual click.
        TabFade = new TabFade();
        AddChild(TabFade);
        TabFade.Build();
        Tabs.TabChanged += _ => TabFade.Trigger();

        // --- ledger modal overlay (sibling after the layout = draws on top) --
        Ledger = GD.Load<PackedScene>("res://scenes/panels/ledger_modal.tscn").Instantiate<LedgerModal>();
        AddChild(Ledger);
        Ledger.SetAnchorsPreset(LayoutPreset.FullRect);
        Ledger.VisibilityChanged += OnLedgerVisibilityChanged;

        // --- camp decision slate (V7a): a second modal overlay, code-built (no scene, so no
        //     .tscn/import metadata churn). Camp (phase 3) and the Evening Ledger never show at
        //     once, so the two overlays never contend.
        Camp = new CampPanel { Name = "CampModal" };
        AddChild(Camp);
        Camp.SetAnchorsPreset(LayoutPreset.FullRect);
        Camp.VisibilityChanged += OnCampVisibilityChanged;
    }

    private T InstantiatePanel<T>(string scenePath) where T : SimPanel
    {
        var panel = GD.Load<PackedScene>(scenePath).Instantiate<T>();
        Tabs.AddChild(panel);
        return panel;
    }

    /// <summary>
    /// P007 U7: an Accent-forward per-node override marking the ONE primary HUD action
    /// (Advance). A deliberate, narrow exception to the "no local color literals" rule
    /// (R11/KTD1) — every color/shape still comes from <see cref="GameTheme"/>'s public
    /// surface (<see cref="GameTheme.ButtonStyle"/>, <see cref="GameTheme.AccentColor"/>,
    /// <see cref="GameTheme.BoneColor"/>), just recombined for this single distinguished
    /// control rather than registered as a shared theme type.
    /// </summary>
    private static void StylePrimary(Button button)
    {
        var normal = GameTheme.ButtonStyle(GameTheme.ButtonVisualState.Pressed);
        normal.BgColor = GameTheme.AccentColor;
        button.AddThemeStyleboxOverride("normal", normal);

        var hover = GameTheme.ButtonStyle(GameTheme.ButtonVisualState.Pressed);
        hover.BgColor = GameTheme.AccentColor.Lightened(0.15f);
        button.AddThemeStyleboxOverride("hover", hover);

        var pressed = GameTheme.ButtonStyle(GameTheme.ButtonVisualState.Pressed);
        pressed.BgColor = GameTheme.AccentColor.Darkened(0.15f);
        button.AddThemeStyleboxOverride("pressed", pressed);

        button.AddThemeColorOverride("font_color", GameTheme.BoneColor);
        button.AddThemeColorOverride("font_color_hover", GameTheme.BoneColor);
        button.AddThemeColorOverride("font_color_pressed", GameTheme.BoneColor);
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

    /// <summary>The camp decision window holds the town clock; Hold (close) resumes it if it was running.</summary>
    private void OnCampVisibilityChanged()
    {
        if (Camp.Visible)
        {
            _resumePlayOnCampClose = Clock.Playing;
            Clock.Pause();
        }
        else if (_resumePlayOnCampClose)
        {
            Clock.Play();
            _resumePlayOnCampClose = false;
        }

        UpdateClockLabel();
    }
}
