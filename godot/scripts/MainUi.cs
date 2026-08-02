using System;
using System.Globalization;
using System.Linq;
using GameSim.Contracts;
using Godot;
using GodotClient.Audio;
using GodotClient.Panels;
using GodotClient.Town;
using GodotClient.Town2d;
using GodotClient.Ui;

namespace GodotClient;

/// <summary>
/// The one UI scene (U11 shell + U12 town layer, drawer-reworked U21): the living town view is a
/// PERMANENT full-rect base child — always visible, never hidden by a panel opening — with the seven
/// management panels (Forge/Shop/Heroes/Tavern/Depths/Bounties/Demand) hosted one at a time in the
/// right-anchored <see cref="DrawerHost"/> that slides over it, under a themed HUD header (P007
/// U7 — day/phase/gold/heroes stat chips + Skip/Auto, with play/pause/fast-forward
/// as auto-mode sub-controls), with the Evening Ledger as a modal overlay. The Ledger opens
/// through the U12 Return Ritual — a TIME-BASED gate
/// (<see cref="ReturnRitualDelaySeconds"/> of unscaled wall-clock after the Evening
/// tick), never blocked by sprite walk-ins, so a zero-survivor day cannot hang the
/// reveal. Owns the single <see cref="SimAdapter"/> and the <see cref="PhaseClock"/>;
/// everything below binds through the adapter (KTD2). Town clicks route through <see
/// cref="OpenPanel"/> (R20). U15 (KTD3): the clock flows on its own by default and computes <see
/// cref="PhaseClock.Engaged"/> from drawer/modal state each frame's relevant events — see
/// <see cref="UpdateEngaged"/>.
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

    /// <summary>U18/KTD13: the objective chip's docked width and its margin from the window's
    /// right edge and the header's bottom edge — an overlay sibling (like the Ledger/Camp
    /// modals) rather than a layout child, so it floats above every tab without shifting
    /// panel content down. Menu-sizing fix (gate-b): mirrors
    /// <see cref="Ui.ObjectiveTracker.DockWidth"/> rather than duplicating the literal, so the
    /// chip's own minimum size and its docked offsets can never drift apart.</summary>
    private const float ObjectiveDockWidth = Ui.ObjectiveTracker.DockWidth;
    private const float ObjectiveDockMargin = 16f;

    /// <summary>Height of the HUD's stat-chip row (Day/Phase/Gold/Heroes/rent/slot-pips), measured
    /// from the pre-wrapper layout. Must be explicit — see StatChipsWrap's remark.</summary>
    private const float StatRowHeight = 68f;

    /// <summary>Top offset of the objective chip's top-right dock — must clear the HUD header's
    /// bottom edge. Bumped from 64 to 108 for the two-row header (gate-b playtest, 2026-07-24): the
    /// stat chips moved to their own row, so the header is ~2x tall and the chip would otherwise sit
    /// on the timeline/controls row.</summary>
    private const float ObjectiveDockOffsetTop = 108f;

    /// <summary>Menu-sizing fix (review): the smallest on-screen gap the objective chip's clamp
    /// math will ever collapse OffsetTop/OffsetBottom down to on a very short viewport — keeps
    /// the chip a sliver rather than zero/negative height instead of tuning the normal-case
    /// docking above.</summary>
    private const float ObjectiveDockMinBottomGap = 40f;

    /// <summary>Menu-sizing fix (review): fixed floor for header row 2's PHASE DIAL zone (the
    /// day-timeline) — named here rather than left as an inline literal at its
    /// <c>CustomMinimumSize</c> call site, matching the ObjectiveDock consts above. (The stat-chip
    /// row is row 1 now — its own full width, no floor needed. UI-4's VERB/TRAY zones no longer
    /// need a matching floor: shrunk to a 36px button + three 24px icon buttons and seven 28px
    /// tray icons, their natural minimum is small and stable — HBoxContainer reserves it before
    /// handing the rest to this ExpandFill zone regardless.)</summary>
    private const float TimelineMinWidth = 280f;

    /// <summary>U23: the tutorial-flow overlay docks in the same top-right column, stacked below
    /// the objective chip rather than sharing its box (keeps the chip's own layout untouched).</summary>
    private const float TutorialDockOffsetTop = ObjectiveDockOffsetTop + 90f;

    /// <summary>U23 (R5, KTD4): number-row hotkeys for the quick-travel unlock — runtime <see
    /// cref="InputMap"/> registration only (no <c>project.godot</c> contact), gated on <see
    /// cref="TutorialFlow.QuickTravelUnlocked"/> in <see cref="_Process"/>. Building keys match
    /// <see cref="OnTownBuildingClicked"/>'s own payload vocabulary — the legacy capitalized
    /// names (<see cref="TutorialFlow"/>'s own <c>QuickTravelVenues</c> table uses the same
    /// vocabulary and is out of this unit's edit scope).</summary>
    private static readonly (string Action, Key Key, string Building)[] QuickTravelHotkeys =
    [
        ("quicktravel_forge", Key.Key1, "Forge"),
        ("quicktravel_shop", Key.Key2, "Shop"),
        ("quicktravel_tavern", Key.Key3, "Tavern"),
        ("quicktravel_gate", Key.Key4, "Gate"),
    ];

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
    public DrawerHost Drawer { get; private set; } = null!;
    public Town2D Town { get; private set; } = null!;

    /// <summary>The game's sound. Mounted alongside the town so every panel can find it via
    /// <see cref="AudioDirector.For"/>.</summary>
    public AudioDirector Audio { get; private set; } = null!;
    public ForgePanel Forge { get; private set; } = null!;
    public ShopPanel Shop { get; private set; } = null!;
    public HeroesPanel Heroes { get; private set; } = null!;
    public TavernPanel Tavern { get; private set; } = null!;
    public DepthsPanel Depths { get; private set; } = null!;
    public BountyPanel Bounties { get; private set; } = null!;
    /// <summary>U6 (C2c, plan 2026-07-25-001): the read-only demand telegraph — <see
    /// cref="DemandBoard.Snapshot"/> rendered as pass-reason rollup, open commissions, depth-stall
    /// call-to-action, and the bounty board with each floor's price-floor minimum shown.</summary>
    public DemandPanel Demand { get; private set; } = null!;
    /// <summary>Phase B, B1d (plan 2026-07-25-002): the read-only hero digest — every alive hero
    /// as a card (standing/deepest/XP/rank + summed deeds), distinct from <see cref="Heroes"/>
    /// (the portrait-grid roster + gear/provenance detail pane reached via town clicks). Drawer id
    /// deliberately "HeroCards", not "Heroes" — that id is already taken by the roster panel; the
    /// HUD button below is still labeled "Heroes" per this unit's brief.</summary>
    public HeroPanel HeroCards { get; private set; } = null!;
    public LedgerModal Ledger { get; private set; } = null!;
    /// <summary>U10: the pre-sleep raid-forecast board (RaidForecast.ForTomorrow projection),
    /// chained after the day-end Ledger and re-openable from the HUD "Forecast" button.</summary>
    public RaidForecastBoard Forecast { get; private set; } = null!;
    /// <summary>Gate-b flag 3: the Bestiary gallery (all venues' monsters, a 2D portrait where one
    /// exists), opened from the Tavern's "Bestiary" hotspot.</summary>
    public BestiaryPanel Bestiary { get; private set; } = null!;
    /// <summary>The campaign's ending screen — the reader for <see cref="CampaignEnded"/>, which
    /// carried its own chronicle tallies for exactly this purpose and had no reader until now.
    /// Opens itself on the ending tick; never halts the kernel (the town stays playable after).</summary>
    public ChronicleScroll Chronicle { get; private set; } = null!;
    /// <summary>Wave 3 (U15): the commission board (<see cref="GameState.Commissions"/>) — opened
    /// from the Prepare-phase HUD button next to Forecast.</summary>
    public CommissionBoard Commissions { get; private set; } = null!;
    /// <summary>Wave 4 (U21): the single monument to the spine — memorials, depths records, and
    /// legendary (Signed/high-attribution) gear. Opened from the HUD button or the Tavern's
    /// "Legends" hotspot.</summary>
    public LegendsWall Legends { get; private set; } = null!;
    public CampPanel Camp { get; private set; } = null!;
    /// <summary>U-D4: the multi-axis progression spine — the five ladders + each one's next rung.
    /// Opened from the HUD "Progress" button.</summary>
    public ProgressionPanel Progress { get; private set; } = null!;
    public TabFade TabFade { get; private set; } = null!;
    public AdventureTicker Ticker { get; private set; } = null!;

    /// <summary>U22 (R4/KTD10): the staged-interior framework — opens instead of the drawer on a
    /// venue interact/click-arrival, then routes a hotspot press onto the same drawer id. 2.5D
    /// pivot (U2): this slice's <see cref="OnTownBuildingClicked"/> routes straight to <see
    /// cref="OpenPanel"/> instead, so nothing currently opens this stage — it stays wired
    /// (hotspot/exit handlers intact) for a later reintroduction rather than torn out.</summary>
    public InteriorStage Interior { get; private set; } = null!;

    /// <summary>U18 (R11/KTD13): the top-right objective chip — <c>ObjectiveAdvisor</c>'s top
    /// pick + reason, expandable to the ranked list.</summary>
    public ObjectiveTracker Objective { get; private set; } = null!;

    /// <summary>U23 (R5/R10/R13): the first-run tutorial chain + earn-2nd-profession affordance +
    /// quick-travel unlock — see <see cref="TutorialFlow"/>'s own class doc.</summary>
    public TutorialFlow Tutorial { get; private set; } = null!;

    /// <summary>U18 (R12/KTD13): the top-bar-center day-timeline widget — live phase highlight
    /// + the U15 engaged-wait indicator.</summary>
    public DayTimeline Timeline { get; private set; } = null!;

    /// <summary>U16 (KTD11/KTD13): the expanded scrying-mirror modal.</summary>
    public ScryingMirror Mirror { get; private set; } = null!;

    /// <summary>U16 (KTD13): the bottom-right PiP journey dock.</summary>
    public PipDock Pip { get; private set; } = null!;

    /// <summary>The most recent day whose Evening completed — what the Ledger button reopens.</summary>
    public int LastCompletedDay { get; private set; }

    /// <summary>Seconds left on the Return Ritual gate; 0 when no reveal is pending.</summary>
    public double LedgerDelayRemaining { get; private set; }

    /// <summary>Seconds left on the rejection toast; 0 when no toast is showing (U6).</summary>
    public double ToastRemaining { get; private set; }

    /// <summary>Gate-b bug fix: the two-row HUD header panel — kept so <see
    /// cref="UpdateObjectiveDock"/> can dock the objective chip below its REAL rendered height
    /// instead of a hand-tuned magic offset (<see cref="ObjectiveDockOffsetTop"/> drifted stale
    /// the moment the Books Tray zone made the header taller, which is exactly how the chip ended
    /// up overlapping the tray).</summary>
    private PanelContainer _hudHeader = null!;
    private int _pendingLedgerDay;
    private HBoxContainer _statChips = null!;
    private Label _clockLabel = null!;
    private PanelContainer _toastBanner = null!;
    private Label _toast = null!;
    private Button _advance = null!;
    private Button _auto = null!;
    private Button _playPause = null!;
    private Button _speed = null!;
    private Button _watch = null!;
    private bool _resumePlayOnLedgerClose;
    private bool _resumePlayOnCampClose;
    private bool _resumePlayOnMirrorClose;
    /// <summary>U10: whether resuming play is owed when the forecast board closes — captured the
    /// same way the Ledger's is (from the play state when it opened, or inherited from the Ledger
    /// when the board auto-chains at day end).</summary>
    private bool _resumePlayOnForecastClose;
    /// <summary>U10: armed when the Return-Ritual auto-reveal opens the day-end Ledger, so the
    /// forecast board pops the moment that Ledger is dismissed — the "review results, then read
    /// tomorrow's threats, then sleep" beat. A manual mid-day Ledger peek never sets it, so it
    /// never spuriously chains the board.</summary>
    private bool _showForecastOnLedgerClose;
    /// <summary>U10: true only for the one frame the board is opening as a day-end chain (not a
    /// manual HUD-button open) so its VisibilityChanged handler keeps the inherited resume intent
    /// instead of overwriting it with the (paused) clock state.</summary>
    private bool _forecastChaining;
    /// <summary>Gate-b flag 3: resume-play-on-close intent for the Bestiary modal (same pattern as
    /// the Ledger/Forecast).</summary>
    private bool _resumePlayOnBestiaryClose;
    /// <summary>Wave 3 (U15): mirror of the Bestiary/Forecast latch for the commission board.</summary>
    private bool _resumePlayOnCommissionsClose;
    /// <summary>Wave 4 (U21): mirror of the Bestiary/Forecast latch for the Legends Wall.</summary>
    private bool _resumePlayOnLegendsClose;

    /// <summary>
    /// U1 (playtest-three plan, KTD-A move 2): armed by <see cref="SoundTheTick"/> when the
    /// departure tick lands while a genuine modal (Ledger/Camp/Mirror/Forecast/Bestiary/
    /// Commissions/Legends, or the staged Interior) still owns the screen — the drawer is ALWAYS
    /// closed on departure (move 1), but a modal is a deliberate player choice mid-Morning and
    /// yanking the camera behind it would still be invisible, one layer deeper than the reported
    /// bug. Cleared by <see cref="TryFireDeferredMineGateFocus"/>, called from every modal-close
    /// path, the moment nothing is left covering the town.
    /// </summary>
    private bool _pendingMineGateFocus;

    // ── LW3: gold-chip bounce-scale pop (StatusBar region) ────────────────────────────────────
    // No engine Tween in this codebase (accumulated-delta math only, so the pop is deterministic
    // and headless-testable via direct _Process calls). -1 = not popping.
    private const double GoldPopSeconds = 0.3;
    private Label? _goldValueLabel;
    private double _goldPopElapsed = -1;

    public override void _Ready()
    {
        Adapter = AdapterOverride ?? new SimAdapter((ulong)Seed);
        AdapterOverride = null; // consumed — the handoff is one-shot (see property doc)
        Clock = new PhaseClock(Adapter);
        RegisterQuickTravelActions(); // U23 (KTD4): runtime InputMap only, zero project.godot contact

        // U15 (KTD3 escape hatch): a saved manual-mode preference wins over PhaseClock's
        // ON-by-default so a player who deliberately went manual stays manual next launch.
        // No file yet (fresh install) ⇒ null ⇒ leave the ON default untouched.
        var persistedAutoAdvance = ClockSettings.LoadAutoAdvance();
        if (persistedAutoAdvance.HasValue)
        {
            Clock.SetAutoAdvance(persistedAutoAdvance.Value);
        }

        // P007 U1 (R11/KTD1): assign the shared Theme BEFORE building any child Control so
        // Godot's normal Theme cascade carries it to every panel/tab built below.
        Theme = GameTheme.Build();
        BuildUi();
        UpdateEngaged(); // no drawer open, no modal open — starts disengaged

        Adapter.StateChanged += OnPhaseCompleted;
        Town.Bind(Adapter);
        Forge.Bind(Adapter);
        Shop.Bind(Adapter);
        Heroes.Bind(Adapter);
        Tavern.Bind(Adapter);
        Depths.Bind(Adapter);
        Bounties.Bind(Adapter);
        Demand.Bind(Adapter);
        HeroCards.Bind(Adapter);
        Progress.Bind(Adapter);
        Ledger.Bind(Adapter);
        Chronicle.Bind(Adapter);
        Camp.Bind(Adapter);
        Mirror.Bind(Adapter);
        Pip.Refresh(Adapter.CurrentState, Adapter.LastEvents); // not a SimPanel — no Bind() auto-refresh

        RefreshHud();
        UpdateClockLabel();
        SyncCampModal(); // adopt an injected mid-day (parked) campaign — open the slate if already at Camp
        GD.Print($"[MainUi] campaign started, seed {Seed}");
        MaybeScreenshotAndQuit();
    }

    // Dev tool (no-op in normal play): when TOWN_SHOT=<path> is set, render a few frames then
    // save the whole viewport (3D town + HUD) to that PNG and quit. Lets an agent verify the
    // town visually on a real GPU (headless can't render 3D). Guarded — never fires without the
    // env var, so it has zero effect on a normal launch or playtest.
    private async void MaybeScreenshotAndQuit()
    {
        var shotPath = System.Environment.GetEnvironmentVariable("TOWN_SHOT");
        if (string.IsNullOrEmpty(shotPath))
        {
            return;
        }

        var tree = GetTree();
        for (var i = 0; i < 90; i++) // ~1.5s at 60fps: let 3D, camera, and layout settle
        {
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }

        var image = GetViewport().GetTexture().GetImage();
        image.SavePng(shotPath);
        GD.Print($"[MainUi] TOWN_SHOT saved: {shotPath}");
        tree.Quit();
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
                _showForecastOnLedgerClose = true; // U10: the auto-revealed day-end Ledger chains to the forecast
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

        // LW6: tick the drawer-swap fade veil (no-op unless a dip is in flight).
        TabFade.Tick(delta);

        // U21: tick the drawer's accumulated-delta slide (no-op unless a slide is in flight).
        Drawer.Tick(delta);

        // U22: tick the interior stage's accumulated-delta camera push-in (no-op unless open).
        Interior.Tick(delta);

        // U17: tick the bottom-edge adventure ticker marquee (no-op with no lines yet).
        Ticker.Tick(delta);

        // Tick the ending chronicle's staged line reveal (no-op unless the scroll is open).
        Chronicle.Tick(delta);

        // UI-4: tick the day-timeline's pulsing engaged-wait dot (no-op unless it's visible).
        Timeline.Tick(delta);

        // UI-6: tick the objective note's body fade-in (no-op unless a fresh step just landed).
        Objective.Tick(delta);

        // U23 (R5): quick-travel hotkeys — inert until the tutorial chain completes.
        if (Tutorial.QuickTravelUnlocked)
        {
            foreach (var (action, _, building) in QuickTravelHotkeys)
            {
                if (Input.IsActionJustPressed(action))
                {
                    QuickTravel(building);
                }
            }
        }
    }

    private void OnPhaseCompleted(DayPhase completedPhase, int completedDay)
    {
        var state = Adapter.CurrentState;
        GD.Print($"[MainUi] tick complete: day {completedDay} {completedPhase} -> day {state.Day} {state.Phase} " +
                 $"({Adapter.LastEvents.Count} events, {Adapter.LastRejections.Count} rejections)");

        // One JSONL row per tick when MM_PLAYTEST_LOG is set (the launchers set it; a test run does
        // not, so this is a no-op there). This is the same information as the GD.Print above plus the
        // economy columns, written somewhere a later session can actually analyse — see PlaytestLog
        // for why prose playtest reports kept failing us.
        PlaytestLog.Tick(completedPhase, completedDay, state, Adapter.LastRejections, Adapter.LastEvents.Count);

        // Sound follows the same signal the HUD does, so a cue can never disagree with what is on
        // screen. Phase-keyed rather than event-keyed for the bed: SetPhase ignores an unchanged
        // phase, so calling it on every tick is correct and needs no boundary detection here.
        Audio.SetPhase(state.Phase);
        SoundTheTick(completedPhase, state);
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
            // Nothing the player did wrong this tick, so the banner is free for something the
            // WORLD did. Rejections always win it — the player's own refused action is more urgent
            // feedback than news, and stacking both in one strip would bury the refusal.
            var notice = WorldNotice(Adapter.LastEvents, state);
            if (notice is null)
            {
                ClearToast();
            }
            else
            {
                _toast.Text = notice;
                _toastBanner.Visible = true;
                ToastRemaining = RejectionToastSeconds;
            }
        }
        else
        {
            _toast.Text = string.Join("  ",
                Adapter.LastRejections.Select(r => FriendlyRejection(r.Reason, r.Action)).Distinct());
            _toastBanner.Visible = true; // U7: transient banner, hidden except while a toast is live
            ToastRemaining = RejectionToastSeconds;
        }

        // Autosave on the day turning over. Evening is the natural boundary: the ledger has resolved,
        // the raid is revealed, and nothing is mid-gesture — so a resumed campaign never lands the
        // player inside a half-finished haggle or an open minigame.
        //
        // One rolling slot, written here and nowhere else. That is a design choice, not a shortcut:
        // a per-action or multi-slot save would let a player reload to reroll a craft, and quality
        // rolls are the thing this game asks you to live with.
        if (completedPhase == DayPhase.Evening)
        {
            CampaignSave.Save(state);
        }

        // The campaign's ending. Emitted once, ArcDirectorSystem.EndingDelayDays after the climax.
        // Rendered from the event's own tallies (see ChronicleScroll) rather than re-derived state.
        foreach (var evt in Adapter.LastEvents)
        {
            if (evt is CampaignEnded ended)
            {
                Chronicle.ShowFor(ended);
                break;
            }
        }

        Tutorial.Advance(state, Adapter.LastEvents); // U23: this tick's events only (KTD5-safe)
        RefreshAll();
        Town.OnPhaseCompleted(completedPhase);
        // U25 (c): the drawer's own ShopPanel.OnPhaseCompleted (LW3's lit customer strip) is
        // retired — Interior's own hook below is the ONE ShopStage choreography now.
        Interior.OnPhaseCompleted(completedPhase, state, Adapter.LastEvents); // U22: ported into the shop interior too
        SyncCampModal(); // V7a: raise the winch-house slate the moment a party parks at Camp

        // U17: feed this tick's freshly stamped events to the bottom-edge adventure ticker.
        // EventLog only (Adapter.LastEvents) — never PendingExpeditions — is what keeps it
        // KTD5-safe by construction (see AdventureTicker's class doc).
        Ticker.OnPhaseCompleted(completedPhase, completedDay, state, Adapter.LastEvents);

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

    /// <summary>
    /// Re-render the status bar, the permanent world, and every currently-visible surface from
    /// CurrentState. U21: VISIBILITY-GATED — a load-bearing perf change now that the world always
    /// renders. The five drawer panels NOT currently open never get a Refresh() call here; opening
    /// one via <see cref="OpenPanel"/> refreshes it on the spot, so nothing a player actually looks
    /// at is ever stale. Ledger/Camp/Mirror/Pip are unaffected — they were never tab-gated before
    /// U21 (LedgerModal/CampPanel stay FullRect overlays above the drawer) and stay unconditional.
    /// </summary>
    public void RefreshAll()
    {
        RefreshHud();
        Town.Refresh(); // the world is always visible — always refreshed
        if (Drawer.CurrentPanelId is { } openId)
        {
            PanelFor(openId).Refresh();
        }

        Ledger.Refresh();
        Camp.Refresh();
        Mirror.Refresh();
        Pip.Refresh(Adapter.CurrentState, Adapter.LastEvents); // U16/KTD11: rebuild the PiP's cards once per tick
    }

    /// <summary>
    /// V7a phase hook: raise the camp slate the instant a party parks (Phase == Camp with a
    /// non-empty InFlight), and drop it once the parked run finalizes (InFlight cleared at the
    /// Deep tick). Deliberately does NOT auto-close merely on leaving Camp — the just-completed
    /// Camp tick's rejections must stay legible on the slate through the Deep phase (AE4), and the
    /// player's own Hold is the normal close. A FullRect modal, not a drawer — untouched by U21.
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
    /// U18 (R11/R12): the stat-chip row plus the two new HUD widgets — the objective chip and
    /// the day-timeline — refreshed together on every phase tick (never per frame; see
    /// <see cref="ObjectiveTracker.Refresh"/>/<see cref="DayTimeline.Refresh"/> remarks).
    /// </summary>
    /// <summary>
    /// Re-render just the objective/tutorial card — the ONLY thing that depends on which drawer is open.
    ///
    /// <para>Split out of <see cref="RefreshHud"/> because opening a panel must update the tutorial's copy
    /// (it stops telling you to walk to the room you are standing in) and must NOT do a full HUD refresh.
    /// Calling <c>RefreshHud</c> from <c>OpenPanel</c> made <c>Playtest3dClickThrough</c> — which opens every
    /// panel in every phase across a whole session — run from 27 seconds to past the test runner's timeout,
    /// taking ~200 other tests down with it. Cheap, targeted, and the same reason
    /// <see cref="UpdateObjectiveDock"/> is its own method.</para>
    /// </summary>
    private void RefreshObjectiveLine()
    {
        var state = Adapter.CurrentState;
        // The open drawer is passed in so the tutorial can stop telling the player to walk to a room they
        // are already standing in — see TutorialFlow.GoTo.
        Objective.Refresh(state, Tutorial.TopSlotText(state, Drawer.CurrentPanelId)); // U23: tutorial overrides the top slot only
        UpdateObjectiveDock(); // Refresh can change the reason line's line count — re-dock to it
    }

    private void RefreshHud()
    {
        RefreshStatus();
        var state = Adapter.CurrentState;
        RefreshObjectiveLine();
        Tutorial.RefreshAffordances(state);
        Timeline.Refresh(state.Phase, Waiting);
        UpdateClockLabel(); // U3/U4: bell verb + player-phase banner are state-driven — refresh on every tick, not only per-frame _Process
    }

    /// <summary>
    /// Menu-sizing fix (U2, playtest F1 "objective menu STILL renders off-screen" + the "chip
    /// covers the Buy-copper button" self-test gap): dock the objective chip's OffsetTop/
    /// OffsetBottom to its OWN live content height (<see cref="Control.GetCombinedMinimumSize"/>)
    /// instead of the old fixed 260px dock — a fresh mount's single-line reason and a "More"-
    /// expanded ranked list both get exactly the height they need, never a mostly-empty panel
    /// sized to fit the tallest case. Still clamped so OffsetTop/OffsetBottom can never land past
    /// the viewport's bottom edge on a short window (TopRight anchors both Top/Bottom to the
    /// window's top edge, so these offsets ARE the absolute on-screen Y coordinates) — the same
    /// clamp <see cref="ObjectiveDockMinBottomGap"/> already existed for. Called once at build
    /// time, every <see cref="RefreshHud"/> tick, and on every "More" ranked-list toggle — the
    /// three moments the chip's own content height can change.
    ///
    /// Gate-b bug fix (playtest screenshot, "note overlaps the books tray"): <see
    /// cref="ObjectiveDockOffsetTop"/> is a hand-tuned constant that goes stale every time the HUD
    /// header's own content grows (exactly what happened once the Books Tray zone landed — the
    /// header's real two-row height ended up taller than the 108px the chip was docked at, so the
    /// chip's top-right corner landed a few pixels INSIDE the header's own top-right Books Tray).
    /// Rather than re-tune the magic number again (guaranteed to drift again next HUD change),
    /// dock below <see cref="_hudHeader"/>'s actual measured height — <see
    /// cref="ObjectiveDockOffsetTop"/> stays only as a floor so the chip never docks HIGHER than
    /// the originally-tuned position on a header shorter than expected.
    /// </summary>
    private void UpdateObjectiveDock()
    {
        var viewportHeight = GetViewportRect().Size.Y;
        // GetCombinedMinimumSize (not .Size) for the same reason Objective's own content height is
        // read that way below — a same-frame content change hasn't necessarily flushed into .Size
        // yet, and the header sits flush at the layout's top (y=0), so its minimum height IS its
        // real bottom-edge Y coordinate.
        var headerBottom = _hudHeader.GetCombinedMinimumSize().Y + ObjectiveDockMargin;
        var desiredTop = Mathf.Max(ObjectiveDockOffsetTop, headerBottom);

        // The world sits full-rect BEHIND this opaque header, so the header hides the top ~quarter
        // of it. Centering the camera on the player therefore put the player under, or just below,
        // the header — standing at the forge door meant the forge itself was behind the HUD. Hand
        // the town the header's measured height so it can bias the camera by it (Town2D does the
        // canvas-scale math). Measured, not a constant, for exactly the reason above: the header's
        // height has already drifted twice.
        Town.TopObstructionPx = _hudHeader.GetCombinedMinimumSize().Y;
        Objective.OffsetTop = Mathf.Min(desiredTop, viewportHeight - ObjectiveDockMinBottomGap);
        var maxBottom = Mathf.Max(Objective.OffsetTop + ObjectiveDockMinBottomGap, viewportHeight - ObjectiveDockMargin);
        var contentHeight = Objective.GetCombinedMinimumSize().Y;
        Objective.OffsetBottom = Mathf.Min(Objective.OffsetTop + contentHeight, maxBottom);

        // Stack the tutorial dock BELOW the objective card, measured — never at a magic offset.
        // TutorialDockOffsetTop was `ObjectiveDockOffsetTop + 90f`, a constant that silently assumed
        // both the header's height and the objective card's height. The 2026-07-29 HUD-overflow fix
        // made the header shorter, the objective card moved up, and the tutorial dock stayed put —
        // landing directly on top of the card so the quick-travel row covered the "Today" title and
        // the advisor line. This is the exact drift this method's own remark warns about, so apply
        // the same remedy: derive the position instead of tuning a number that will drift again.
        if (Tutorial is not null)
        {
            // Both edges move together — OffsetBottom is an absolute offset from the same anchor, so
            // shifting only the top would squash or invert the panel's height.
            var tutorialTop = Mathf.Max(TutorialDockOffsetTop, Objective.OffsetBottom + ObjectiveDockMargin);
            Tutorial.OffsetTop = tutorialTop;

            // CLAMP to the window. The previous version sized purely to the content's minimum, which
            // stopped the dock overlapping the objective card but let it run straight off the bottom
            // of the screen — a human playtest found the panel "still cutoff" and its lower rows
            // unreachable. Height is now whatever the content wants OR whatever room is actually
            // left, whichever is smaller; TutorialFlow scrolls internally so nothing is lost when
            // the clamp bites.
            var available = GetViewportRect().Size.Y - tutorialTop - ObjectiveDockMargin;
            var wanted = Tutorial.GetCombinedMinimumSize().Y;
            Tutorial.OffsetBottom = tutorialTop + Mathf.Min(wanted, Mathf.Max(0f, available));
        }
    }

    /// <summary>U18/U15: the day-timeline's engaged-wait indicator mirrors <see cref="
    /// UpdateClockLabel"/>'s own predicate — only worth flagging while the clock is actively
    /// running AND held at a boundary; a manual pause is a different, already-visible state.</summary>
    private bool Waiting => Clock.AutoAdvance && Clock.Playing && Clock.Engaged;

    /// <summary>
    /// P007 U7 (R11/R12/KD1): rebuild the HUD's stat-chip row from CurrentState. Rebuilt (not
    /// mutated in place) each call — mirrors the panels' own Clear-then-compose Refresh pattern
    /// (KTD2) so the chips can never drift from live state between ticks.
    ///
    /// <para>UI-3 (menu-sizing/cozy redesign): the old flat row of 9 loose chips overflowed at the
    /// default window size (<c>ClipContents</c> silently clipped the tail — Rent/Guild/Confidence
    /// were invisible). Regrouped into 3 clusters with a fixed gap (<see cref="GameTheme.Space16"/>):
    /// CALENDAR (Day · Phase, the Act chip demoted into a small badge), WEALTH+HANDS (gold — the
    /// bar's single largest value — heroes, and the unchanged slot pips), and DUES, pushed flush to
    /// the bar's right edge by an <c>ExpandFill</c> spacer. Every node's <see cref="Node.Name"/> is
    /// unchanged from before this pass — only the grouping/visual treatment moved.</para>
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

        _statChips.AddThemeConstantOverride("separation", GameTheme.Space16);

        // ── CALENDAR cluster: Day · Phase, with the campaign Act folded in as a small badge ──────
        var calendar = new HBoxContainer { Name = "CalendarCluster" };
        calendar.AddThemeConstantOverride("separation", GameTheme.Space8);
        _statChips.AddChild(calendar);

        calendar.AddChild(NamedStatChip("DayChip", "Day", $"{state.Day}"));

        var separator = new Label { Text = "·" };
        separator.AddThemeColorOverride("font_color", GameTheme.TextDim);
        calendar.AddChild(separator);

        var phaseChip = NamedStatChip("PhaseChip", "Phase", state.Phase.ToString(), UiKit.ChipTone.Accent);
        phaseChip.TooltipText = PhaseLegend;
        calendar.AddChild(phaseChip);

        // U-D3: which act of the campaign arc the town is in (I → II → III → ending) — demoted
        // (UI-3) from a full peer chip to a small compact badge folded into the Calendar cluster;
        // the detail stays on the tooltip.
        var actChip = NamedStatChipCompact("ActChip", "Act", ArcActRoman(state.Arc.Act), UiKit.ChipTone.Accent);
        actChip.TooltipText =
            $"Campaign arc: {state.Arc.Act}. Advances on the deepest floor your heroes reach; Act III is the climax, then the ending chronicle.";
        calendar.AddChild(actChip);

        // ── WEALTH + HANDS cluster: gold (the bar's biggest value), heroes, action-slot pips ─────
        var wealthHands = new HBoxContainer { Name = "WealthHandsCluster" };
        wealthHands.AddThemeConstantOverride("separation", GameTheme.Space8);
        _statChips.AddChild(wealthHands);

        var goldChip = BuildGoldChip(state.Player.Gold);
        wealthHands.AddChild(goldChip);
        _goldValueLabel = goldChip.GetNode<Label>("Value");

        // Icon stand-in note (UI-3): no dedicated "party/helm" glyph exists in assets/icons yet —
        // reuses "shield" (defense/party) as the closest available fit rather than adding a new
        // asset file (out of this presentation-only unit's file scope). Flagged for a follow-up
        // dedicated HUD icon pass; the word itself still lives in TooltipText.
        var heroesChip = NamedIconChip(
            "HeroesChip", IconRegistry.Glyph("shield"), $"{alive}/{state.Heroes.Count}",
            alive == state.Heroes.Count && state.Heroes.Count > 0 ? UiKit.ChipTone.Positive : UiKit.ChipTone.Neutral,
            "Heroes");
        wealthHands.AddChild(heroesChip);

        // U10 scarcity surfacing: today's remaining real-work action slots as a pip row (UNCHANGED
        // from BuildSlotPips per this unit's brief).
        wealthHands.AddChild(BuildSlotPips(state.ActionSlotsRemaining, ActionBudget.SlotsPerDay));

        // Expanding spacer: pushes the DUES cluster flush to the wood-framed bar's right edge.
        _statChips.AddChild(new Control { Name = "StatChipsSpacer", SizeFlagsHorizontal = SizeFlags.ExpandFill });

        // ── DUES cluster: the three scarcity/heartbeat gauges — icon+value only, the wordy labels
        // ("Rent"/"Guild Assessment"/"Confidence") that ate the most width now live in TooltipText.
        var dues = new HBoxContainer { Name = "DuesCluster" };
        dues.AddThemeConstantOverride("separation", GameTheme.Space8);
        _statChips.AddChild(dues);

        // U10 scarcity surfacing: the guild-rent countdown. Tone escalates as the deadline nears
        // (or once a payment has been missed) so the pressure reads at a glance.
        var rent = state.Rent;
        var rentTone = rent.MissedPayments > 0 || rent.DaysUntilDue <= 1 ? UiKit.ChipTone.Negative
            : rent.DaysUntilDue <= 3 ? UiKit.ChipTone.Accent
            : UiKit.ChipTone.Neutral;
        // Icon stand-in (see HeroesChip's note above): no dedicated "workshop rent" glyph yet —
        // reuses "bounty" (a formal notice/scroll), the closest available fit.
        var rentChip = NamedIconChip(
            "RentChip", IconRegistry.Glyph("bounty"), $"{rent.DaysUntilDue}d·{rent.AmountDueGold}g", rentTone, "Rent");
        rentChip.TooltipText = rent.MissedPayments > 0
            ? $"Rent due in {rent.DaysUntilDue} day(s): {rent.AmountDueGold}g. {rent.MissedPayments} missed payment(s) — the guild is losing patience."
            : $"Rent due in {rent.DaysUntilDue} day(s): {rent.AmountDueGold}g (every {RentState.CadenceDays} days).";
        dues.AddChild(rentChip);

        // U-D2: the Guild Assessment heartbeat — dues on their own cadence, escalating paid or missed.
        var assess = state.Assessment;
        var assessTone = assess.SoftFailed || assess.MissedAssessments > 0 || assess.DaysUntilAssessment <= 1
                ? UiKit.ChipTone.Negative
            : assess.DaysUntilAssessment <= 2 ? UiKit.ChipTone.Accent
            : UiKit.ChipTone.Neutral;
        // Icon stand-in: no dedicated "guild banner" glyph yet — reuses "gossip" (town chatter/
        // reputation), the closest available fit.
        var assessChip = NamedIconChip(
            "AssessmentChip", IconRegistry.Glyph("gossip"), $"{assess.DaysUntilAssessment}d·{assess.DuesGold}g",
            assessTone, "Guild Assessment");
        assessChip.TooltipText = assess.MissedAssessments > 0
            ? $"Guild Assessment due in {assess.DaysUntilAssessment} day(s): {assess.DuesGold}g. {assess.MissedAssessments} missed — dues escalate steeply."
            : $"Guild Assessment due in {assess.DaysUntilAssessment} day(s): {assess.DuesGold}g (every {GuildAssessmentState.CadenceDays} days). Paying it lifts Confidence.";
        dues.AddChild(assessChip);

        // U-D2: the town-Confidence gauge (0-1000 → %) — the soft-deadline morale the Guild
        // Assessment and rival vendor both read. Tone drops as it nears the collapse floor (0).
        var confidence = state.Rent.ConfidencePermille;
        var confidenceTone = confidence <= 200 ? UiKit.ChipTone.Negative
            : confidence <= 500 ? UiKit.ChipTone.Accent
            : UiKit.ChipTone.Positive;
        // Icon stand-in: no dedicated "morale/heart" glyph yet — reuses "rune" (an arcane gauge),
        // the closest available fit.
        var confidenceChip = NamedIconChip(
            "ConfidenceChip", IconRegistry.Glyph("rune"), $"{confidence / 10}%", confidenceTone, "Confidence");
        confidenceChip.TooltipText =
            $"Town confidence {confidence / 10}% — lifts on a paid Guild Assessment, drops on a miss or passive decay. At 0 the era soft-fails (talents + recipes persist).";
        dues.AddChild(confidenceChip);

        // LW3 coin flourish (StatusBar half): a player-shelf sale on THIS tick arms the gold-
        // label pop. ShopStage plays the matching coin-arc off the SAME Adapter.LastEvents batch
        // independently — no cross-panel coupling, the event log is the single source of truth.
        if (Adapter.LastEvents.Any(e => e is ItemSold { FromPlayerShop: true }))
        {
            _goldPopElapsed = 0;
        }
    }

    /// <summary>
    /// The one world-event worth the toast banner this tick, or null.
    ///
    /// <para>Every event below fires correctly in the sim and, before this method existed, reached
    /// no player-visible surface at all: the confidence spiral moved a gauge silently, the campaign
    /// could hit its climax without a word, and the destitution stipend changed the player's gold
    /// with no explanation. The gauges (Confidence/Rent/Assessment chips) show a LEVEL; none of them
    /// marks the MOMENT it crossed a line, which is the part a player needs to react to.</para>
    ///
    /// <para>Deliberately returns ONE line, most consequential first, rather than concatenating.
    /// A four-second strip that tries to say three things says none of them, and this project has
    /// already been burned once by a notification that repeated itself into wallpaper. The quieter
    /// members of this same family (recruits, commissions, the director's incidents) go to the
    /// <see cref="AdventureTicker"/> instead, where they can scroll past without demanding
    /// attention.</para>
    /// </summary>
    private static string? WorldNotice(IEnumerable<GameEvent> events, GameState state)
    {
        string? collapse = null, climax = null, leaving = null, rival = null, stipend = null;

        foreach (var evt in events)
        {
            switch (evt)
            {
                case TownConfidenceCollapsed e:
                    collapse = $"The town has lost faith in your forge — {e.MissedAssessments} assessment(s) missed. " +
                               "Your talents and recipes stay with you.";
                    break;
                case ClimaxReached e:
                    climax = $"Your heroes have reached floor {e.DeepestFloorReached}. Whatever is down there knows it.";
                    break;
                case HeroConsideringLeaving e:
                    leaving = $"{HeroDisplayName(state, e.Hero)} is talking about leaving town.";
                    break;
                case RivalExpansionTriggered e:
                    rival = $"The rival stall is expanding — confidence has slipped to {e.ConfidencePermille / 10}%.";
                    break;
                case RecoveryStipendGranted e:
                    stipend = $"The guild advanced you {e.Amount}g to keep the forge lit.";
                    break;
            }
        }

        return collapse ?? climax ?? leaving ?? rival ?? stipend;
    }

    private static string HeroDisplayName(GameState state, HeroId id) =>
        state.Heroes.TryGetValue(id.Value, out var hero) ? hero.Name : $"Hero #{id.Value}";

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

    /// <summary>UI-3: the demoted-badge twin of <see cref="NamedStatChip"/> — <see
    /// cref="UiKit.StatChipCompact"/> instead of the full <see cref="UiKit.StatChip"/>, for a HUD
    /// element that used to be a full peer chip (e.g. the campaign Act) and is now folded into a
    /// smaller badge alongside its cluster.</summary>
    private static Control NamedStatChipCompact(string name, string label, string value, UiKit.ChipTone tone = UiKit.ChipTone.Neutral)
    {
        var chip = UiKit.StatChipCompact(label, value, tone);
        chip.Name = name;
        return chip;
    }

    /// <summary>UI-3: an <see cref="UiKit.IconChip"/> given a discoverable <see cref="Node.Name"/>
    /// (mirrors <see cref="NamedStatChip"/>'s exact contract) plus a default <paramref
    /// name="tooltip"/> — the word-label a full <see cref="UiKit.StatChip"/> used to render inline
    /// moves here instead, so the on-bar chip stays icon+value only. Callers with a richer,
    /// state-dependent tooltip (e.g. Rent/Guild Assessment) simply overwrite <see
    /// cref="Control.TooltipText"/> on the returned control right after.</summary>
    private static Control NamedIconChip(string name, Texture2D? icon, string value, UiKit.ChipTone tone, string tooltip)
    {
        var chip = UiKit.IconChip(icon, value, tone);
        chip.Name = name;
        chip.TooltipText = tooltip;
        return chip;
    }

    /// <summary>U-D3: the campaign act as a compact roman numeral for the HUD chip.</summary>
    private static string ArcActRoman(CampaignAct act) => act switch
    {
        CampaignAct.ActI => "I",
        CampaignAct.ActII => "II",
        CampaignAct.ActIII => "III",
        CampaignAct.Ended => "Fin",
        _ => act.ToString(),
    };

    /// <summary>
    /// The gold chip pairs the existing gold glyph (U16) with the bar's single largest value
    /// (UI-3: <see cref="GameTheme.HudValueFontSize"/>, <see cref="GameTheme.GoldColor"/> — gold is
    /// the ONE currency, so it gets the ONE outsized read on the bar). The old "Gold" word-label is
    /// gone (moved to <see cref="Control.TooltipText"/>) now that the icon carries that meaning.
    ///
    /// <para>Kept an <see cref="HBoxContainer"/> named "GoldChip" with a direct child <see
    /// cref="Label"/> named "Value" — <c>ShopStageTests</c>/<c>DayAdvanceHudTests</c> locate it by
    /// exactly that shape (<c>Find&lt;HBoxContainer&gt;(ui, "GoldChip")</c> /
    /// <c>Find&lt;Label&gt;(..., "Value")</c>), so this unit's redesign keeps both the node TYPE
    /// and the discoverable "Value" label — only the inner StatChip wrapper (and its extra label)
    /// is gone.</para>
    /// </summary>
    private static Control BuildGoldChip(int gold)
    {
        var wrap = new HBoxContainer { Name = "GoldChip", TooltipText = "Gold" };
        wrap.AddThemeConstantOverride("separation", GameTheme.Space4);
        wrap.AddChild(new TextureRect
        {
            Name = "GoldIcon",
            Texture = IconRegistry.Glyph("gold"),
            CustomMinimumSize = new Vector2(20, 20),
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        });

        var value = new Label { Name = "Value", Text = $"{gold}g" };
        value.AddThemeColorOverride("font_color", GameTheme.GoldColor);
        value.AddThemeFontSizeOverride("font_size", GameTheme.HudValueFontSize);
        wrap.AddChild(value);
        return wrap;
    }

    /// <summary>U10: the action-slot budget (Game-Feel Plan G3) as a row of pips — one filled dot
    /// per remaining slot, one dim dot per spent slot. Filled-ness is carried on a per-dot
    /// <c>filled</c> meta (NOT the node name — Godot renames duplicate sibling names, so a
    /// name-based count would collapse to one); the row carries a spelt-out tooltip.
    /// <paramref name="remaining"/> is clamped into [0, <paramref name="max"/>] defensively so a
    /// transient out-of-range value never spawns a negative/oversized row.</summary>
    private static Control BuildSlotPips(int remaining, int max)
    {
        var filled = Math.Clamp(remaining, 0, max);
        var row = new HBoxContainer { Name = "SlotPips" };
        row.AddThemeConstantOverride("separation", 4);
        row.TooltipText = $"{filled}/{max} action slots left today (craft, restock, negotiate each spend one).";
        row.MouseFilter = MouseFilterEnum.Stop; // let the tooltip surface on hover

        for (var i = 0; i < max; i++)
        {
            var lit = i < filled;
            var pip = new ColorRect
            {
                CustomMinimumSize = new Vector2(12, 12),
                Color = lit ? new Color(0.90f, 0.76f, 0.24f) : new Color(0.28f, 0.28f, 0.34f),
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            pip.SetMeta("filled", lit);
            row.AddChild(pip);
        }

        return row;
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
    /// <remarks>
    /// Public rather than private so <c>RejectionUxTests</c> can pin the mapping directly (same reason
    /// <see cref="RejectionToastSeconds"/> is). That is a seam, and it is the right one here: this is a pure
    /// string-to-string function, so calling it IS the unit — unlike the input-layer seams that hid real
    /// bugs. End-to-end toast rendering stays covered by
    /// <c>ForcedRejection_RendersPlayerPhrasedToast_ThenClears</c>, which drives real actions.
    /// </remarks>
    public static string FriendlyRejection(string reason, PlayerAction? action = null)
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

        // ── Camp / runner refusals. These were all falling through to the shrug below. ──
        if (reason.StartsWith("One runner per party per day", StringComparison.Ordinal))
        {
            return "You've already sent this party a runner today.";
        }

        if (reason.Contains("recall bell has already rung", StringComparison.Ordinal))
        {
            return "You already rang the bell for this party.";
        }

        if (reason.Contains("recall bell has rung", StringComparison.Ordinal)
            || reason.Contains("the runner can't reach them", StringComparison.Ordinal))
        {
            return "They're already on their way up — a runner can't catch them.";
        }

        if (reason.StartsWith("No party is camped with", StringComparison.Ordinal))
        {
            return "That hero isn't camped below.";
        }

        if (reason.Contains("is already in a hero's pack", StringComparison.Ordinal))
        {
            return "A hero is already carrying that.";
        }

        if (reason.Contains("is shelved", StringComparison.Ordinal))
        {
            return "Take it off the shelf first.";
        }

        if (reason.Contains("isn't your craft to send", StringComparison.Ordinal))
        {
            return "You can only send something you made.";
        }

        return LastResort(action);
    }

    /// <summary>
    /// The line shown when no specific mapping matched — named after the ACTION, never a shrug.
    ///
    /// <para>Brian's playtest: 'I hit sent them off and it just said "it didn't work out"'. That was this
    /// fallback, and it is the worst possible thing to tell someone whose action was refused: it confirms
    /// failure and withholds every clue about what to change. A rejection toast is the ONLY feedback the sim
    /// gives when it says no, so a catch-all has to at least name what was refused.</para>
    ///
    /// <para>Keyed on the action type rather than the reason string, deliberately: an unmapped reason is by
    /// definition one nobody anticipated, but the action is always known. The raw kernel reason still never
    /// renders — it goes to the dev log in <see cref="OnPhaseCompleted"/> — so this stays presentation-only.</para>
    /// </summary>
    private static string LastResort(PlayerAction? action) => action switch
    {
        CraftAction => "The forge turned that craft down — check your materials.",
        BuyMaterialAction or BuyOreAction or BuyForgeSupplyAction => "That purchase didn't go through.",
        StockAction or UnstockAction => "That didn't make it onto the shelf.",
        SetPriceAction => "That price wouldn't stick.",
        PostBountyAction => "That bounty wasn't posted.",
        SendSupplyAction => "The runner didn't set out.",
        RecallPartyAction => "The recall bell didn't reach them.",
        null => "That didn't work out.",
        _ => $"The {Humanize(action.GetType().Name)} didn't go through.",
    };

    /// <summary>"AcceptCommissionAction" -> "accept commission", so the catch-all above can name an action it
    /// has no hand-written line for without printing a type name at the player.</summary>
    private static string Humanize(string actionTypeName)
    {
        var trimmed = actionTypeName.EndsWith("Action", StringComparison.Ordinal)
            ? actionTypeName[..^"Action".Length]
            : actionTypeName;

        var words = System.Text.RegularExpressions.Regex.Replace(trimmed, "(?<!^)([A-Z])", " $1");
        return words.ToLowerInvariant();
    }

    private void UpdateClockLabel()
    {
        // UI-4: Auto/Pause/Speed are icon-only 24px buttons now — Text stays a fixed short glyph
        // (or, for Speed, the compact multiplier itself) and the descriptive word/state moves to
        // TooltipText instead. Node NAMEs and the underlying Clock state are untouched.
        _auto.Text = "⏱";
        _auto.TooltipText = Clock.AutoAdvance ? "Auto-advance: ON" : "Auto-advance: OFF";
        _auto.ButtonPressed = Clock.AutoAdvance; // keep the toggle's pressed look in sync (U7)
        // Play/pause + speed are sub-controls of auto mode — hidden while gated (U2).
        _playPause.Visible = Clock.AutoAdvance;
        _speed.Visible = Clock.AutoAdvance;

        var state = Adapter.CurrentState;
        // U1: only meaningful while a party is actually out there — nothing to watch at Dawn/
        // Prepare (nobody has left yet) or Night (everybody is already home).
        _watch.Visible = state.Phase is DayPhase.Expedition or DayPhase.Camp or DayPhase.ExpeditionDeep;
        if (Clock.AutoAdvance)
        {
            _advance.Text = "Skip"; // Innkeeper's Clock (opt-in auto): the bell is the exception
            var remaining = Clock.Remaining.ToString("0", CultureInfo.InvariantCulture);
            var paused = Clock.Playing ? string.Empty : " [paused]";
            // U15/AE1: engaged holds the boundary even while flowing — surface that distinctly
            // from a manual pause so it's legible that the wait is the player's own doing.
            var engaged = !Clock.Playing || !Clock.Engaged ? string.Empty : " [waiting]";
            _clockLabel.Text = $"{PlayerPhaseName(state)} — next in {remaining}s @{Clock.SpeedMultiplier}x{paused}{engaged}";
            _playPause.Text = Clock.Playing ? "⏸" : "▶";
            _playPause.TooltipText = Clock.Playing ? "Pause" : "Play";
            _speed.Text = $"{Clock.SpeedMultiplier}×";
            _speed.TooltipText = $"Speed: {Clock.SpeedMultiplier}x (click to cycle)";
        }
        else
        {
            // U2/U3/U4: player-decided pacing. The bell verb + the phase banner name the player
            // phase (Dawn/Prepare/Quest–Watch/Quest–Vigil/Night, mapped from the kernel phase, U4);
            // an open-items readout replaces the countdown (U3); Quest–Watch shows a departure omen (U4).
            _advance.Text = BellVerb(state);
            var badge = OpenItemsBadge(state);
            var omen = state.Phase == DayPhase.Expedition ? DepartureOmen(state) : string.Empty;
            var tail = !string.IsNullOrEmpty(omen) ? $" — {omen}"
                : !string.IsNullOrEmpty(badge) ? $" — {badge}"
                : string.Empty;
            _clockLabel.Text = $"{PlayerPhaseName(state)}{tail}";
        }
    }

    /// <summary>U4: the player-facing phase banner, mapped from the kernel <see cref="DayPhase"/>
    /// (never a new enum value). Morning splits into Dawn (no counter open) vs Prepare (counter
    /// session open); Camp/ExpeditionDeep both read as the Quest–Vigil beat.</summary>
    private static string PlayerPhaseName(GameState state) => state.Phase switch
    {
        DayPhase.Morning => state.Counter is { Closed: false } ? "Prepare" : "Dawn",
        DayPhase.Expedition => "Quest — Watch",
        DayPhase.Camp => "Quest — Vigil",
        DayPhase.ExpeditionDeep => "Quest — Vigil",
        DayPhase.Evening => "Night",
        _ => state.Phase.ToString(),
    };

    /// <summary>
    /// U3: the contextual bell label — what ringing it does from the current phase.
    ///
    /// <para><b>State-aware, not phase-only, and that is the whole point.</b> The kernel walks every day
    /// through Camp and ExpeditionDeep whether or not anyone is actually below (<c>GameKernel.Advance</c>
    /// is unconditional). A party whose target floor sits inside stage 1 finishes at the Expedition tick
    /// and walks home — so the player then rings two more bells about a mine that is empty. Labelling
    /// those by phase alone produced three separate playtest complaints that were all this one bug:</para>
    /// <list type="bullet">
    /// <item>"hitting 'lower them into the mine' brings them back to the town??" — they came home because
    /// their run was over, which the label denied.</item>
    /// <item>"return bell does nothing but moved it to 'deep' phase??" — Camp's bell advances DEEPER.</item>
    /// <item>"?? not able to see the heroes in the mine" — nobody was in the mine.</item>
    /// </list>
    ///
    /// <para><b>Camp must never say "return bell".</b> That verb belongs to <c>RecallPartyAction</c>, a real
    /// and different Camp action (see <c>CampPanel</c> / <c>CampHandlers.ApplyRecall</c>) which banks the
    /// haul and surfaces the party. The phase bell at Camp does the OPPOSITE — it sends them to the deep
    /// floors. Two controls one click apart cannot share a name while doing opposite things.</para>
    /// </summary>
    private static string BellVerb(GameState state) => state.Phase switch
    {
        DayPhase.Morning => "Send them off",
        // Was "Lower the winch". Brian read it as "lower the wench" and asked what it meant — the
        // winch-house is internal vocabulary (see Expedition/CampHandlers) that leaked onto a button.
        // A button label has to say what pressing it does; flavour is not worth a player not knowing.
        DayPhase.Expedition => "Lower them into the mine",
        DayPhase.Camp => AnyoneBelow(state) ? "Let them press deeper" : "Close the vigil",
        DayPhase.ExpeditionDeep => AnyoneBelow(state) ? "Ring the return bell" : "Close the vigil",
        DayPhase.Evening => "Snuff the lanterns",
        _ => "Advance",
    };

    /// <summary>True while a party is parked below the checkpoint awaiting stage-2 resolution — the only
    /// state in which the Camp/Deep phases have anything to be about. <c>InFlight</c> is populated by the
    /// Expedition tick and cleared by <c>ExpeditionDeepSystem</c>, so it is exactly "is anyone down there
    /// right now".</summary>
    private static bool AnyoneBelow(GameState state) => !state.InFlight.IsEmpty;

    /// <summary>U3: a readout of what is still open this phase (per-type, not one opaque count),
    /// so the player knows what the bell will end. Empty when nothing is pending.</summary>
    private string OpenItemsBadge(GameState state)
    {
        var parts = new System.Collections.Generic.List<string>();
        if (state.Counter is { Closed: false } counter && counter.Queue.Count > 0)
        {
            parts.Add($"{counter.Queue.Count} at the counter");
        }

        if (state.Phase == DayPhase.Morning && state.ActionSlotsRemaining > 0)
        {
            parts.Add($"{state.ActionSlotsRemaining} slots");
        }

        return parts.Count == 0 ? string.Empty : string.Join(" · ", parts);
    }

    /// <summary>U4: the Quest–Watch payoff — a one-line departure omen from the parties that just
    /// mustered (presentation over data the sim already produced; no new sim state).</summary>
    private static string DepartureOmen(GameState state)
    {
        var parties = state.PendingExpeditions.Count;
        return parties > 0
            ? $"{parties} {(parties == 1 ? "party marches" : "parties march")} for the Mine — watch them go"
            : "the gate stands quiet today";
    }

    /// <summary>U5: a transient bell-action notice (reuses the rejection-toast banner).</summary>
    private void ShowBellToast(string message)
    {
        _toast.Text = message;
        _toastBanner.Visible = true;
        ToastRemaining = RejectionToastSeconds;
    }

    private void BuildUi()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // --- U21: TownWorld is now a PERMANENT FullRect base child — added FIRST so every later
        // sibling (the HUD layout, the DrawerHost, the modals) draws on top of it, and it is never
        // hidden by a drawer opening/closing (R1 world permanence). 2.5D pivot (U2): Town2D
        // replaces the grounded 3D town — same permanence contract, same event vocabulary. ---
        // Mounted before the town so a cue fired during the first refresh already has somewhere to go.
        Audio = new AudioDirector();
        AddChild(Audio);
        Audio.SetPhase(Adapter.CurrentState.Phase);

        Town = new Town2D { Name = "Town2D" };
        AddChild(Town);
        Town.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Town.Build(Adapter);
        Town.Clock = Clock;
        Town.HeroClicked += OnTownHeroClicked;
        Town.BuildingClicked += OnTownBuildingClicked;

        var layout = new VBoxContainer { Name = "Layout" };
        layout.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(layout);

        // --- HUD header (P007 U7/R11/R12/KD1, UI-3/UI-4 cozy redesign): a wood-framed panel
        // holding the clustered stat-chip row (row 1) and the PHASE DIAL / PRIMARY VERB / BOOKS
        // TRAY zones (row 2) — the real home for the living day clock (U15). Both the primary
        // verb (Skip/bell) and Auto drive PhaseClock's ONE advance path (AdvanceNow / Update ->
        // SimAdapter.AdvancePhase); nothing here is a second code path (KD1). ------------------
        var header = new PanelContainer { Name = "HudHeader" };
        // UI-3 (menu-sizing/cozy redesign): the wood-framed panel every other cozy surface now
        // shares (falls back to a flat timber-bordered panel on a stripped build — see
        // GameTheme.PanelStyleWood's own null-tolerant contract) instead of the flat Iron rect.
        header.AddThemeStyleboxOverride("panel", GameTheme.PanelStyleWood());
        layout.AddChild(header);
        _hudHeader = header; // gate-b fix: UpdateObjectiveDock docks below this panel's real height

        // Two-row header (gate-b playtest, 2026-07-24): at the default 1152px window a single row
        // could not hold [6 stat chips] + [timeline] + [6 controls] — the stat chips overflowed
        // and Gold/Heroes/Rent/slot-pips clipped off the left region (U10's scarcity widgets were
        // invisible at the shipped size). Splitting into two rows gives the chips their own full-
        // width row so they never compete with the timeline/controls for horizontal space.
        var headerColumn = new VBoxContainer { Name = "HudHeaderColumn" };
        header.AddChild(headerColumn);

        // Row 1: the full-width stat-chip row (Day/Phase/Gold/Heroes/Rent/slot-pips), populated by
        // RefreshStatus. ClipContents is a belt-and-braces cap for a sub-~600px window — there is
        // nothing to its right on this row, so any overflow clips harmlessly at the window edge and
        // can never push a control off-screen (controls live on row 2).
        var statRow = new HBoxContainer { Name = "HudStatRow", ClipContents = true };
        headerColumn.AddChild(statRow);

        // Layout-probe fix (2026-07-29): ClipContents caps DRAWING, not the reported minimum size.
        // An HBoxContainer's minimum is the sum of its children's minimums, so the chip row still
        // demanded 1174px and pushed the ROOT `Layout` container to 1198px inside a 1152px window —
        // which is why "Act I" was clipped, the right-most tray icon was sliced, and the world/ticker
        // sat 46px wider than the screen. Bounding the chips inside a plain (non-Container) Control
        // with a fixed CustomMinimumSize is the same technique TimelineWrap below already uses, and
        // it stops row 1 from ever driving the window wider than itself.
        var statWrap = new Control
        {
            Name = "StatChipsWrap",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,

            // The HEIGHT must be explicit. A plain Control reports no minimum of its own, and its
            // FullRect child cannot give it one, so leaving Y at 0 collapsed the whole row and made
            // Day/Gold/Heroes/rent/slot-pips vanish from the HUD — an information regression far
            // worse than the clipping this wrapper exists to fix. 68px is the height the row measured
            // before it was wrapped.
            CustomMinimumSize = new Vector2(TimelineMinWidth, StatRowHeight),
            ClipContents = true,
        };
        statRow.AddChild(statWrap);
        _statChips = new HBoxContainer { Name = "StatChips" };
        statWrap.AddChild(_statChips);

        // A plain Control does NOT lay out its children — the first attempt at this used a TopLeft
        // preset and the chips collapsed to zero size, so Day/Gold/Heroes/slot-pips disappeared from
        // the HUD entirely. That is strictly worse than the clipping it was meant to fix. FullRect is
        // what TimelineWrap's child uses for exactly this reason: the wrapper bounds the width the
        // PARENT sees, while the child still fills the wrapper and renders normally.
        _statChips.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // Row 2 (UI-4): the day-timeline PHASE DIAL (ExpandFill left) + the PRIMARY VERB cluster
        // (center) + the BOOKS TRAY (right, icon-only, recessed) — 3 zones, 16px apart.
        var headerRow = new HBoxContainer { Name = "HudHeaderRow" };
        headerRow.AddThemeConstantOverride("separation", GameTheme.Space16);
        headerColumn.AddChild(headerRow);

        // Menu-sizing fix (gate-b/HUD clip): the timeline is the row's ExpandFill region — with no
        // cap its own reported minimum (its children's combined minimum) feeds HudHeaderRow's total
        // width demand, and once that exceeds the window the row overflows right (HBoxContainer never
        // shrinks a child below its minimum), pushing HudControls off-screen. Wrapping it in a plain
        // (non-Container) Control with a fixed CustomMinimumSize + ClipContents bounds its width
        // contribution regardless of how many phase labels it holds.
        var timelineWrap = new Control
        {
            Name = "TimelineWrap",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(TimelineMinWidth, 0),
            ClipContents = true,
        };
        headerRow.AddChild(timelineWrap);
        Timeline = new DayTimeline { Alignment = BoxContainer.AlignmentMode.Center };
        Timeline.Build();
        Timeline.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        timelineWrap.AddChild(Timeline);

        // --- UI-4 Zone 2: PRIMARY VERB (center) — the contextual bell verb is now ONE large
        // button carrying the call-to-action weight; Auto/Pause/Speed collapse to small 24px
        // icon-only buttons beside it (full words moved to TooltipText). The clock-label caption
        // (day/phase banner) sits above, small and dim — it used to compete visually with the
        // button it now defers to; its Text-setting logic in UpdateClockLabel is untouched. -----
        var verbCluster = new VBoxContainer { Name = "VerbCluster", Alignment = BoxContainer.AlignmentMode.Center };
        headerRow.AddChild(verbCluster);

        _clockLabel = new Label { Name = "ClockLabel", HorizontalAlignment = HorizontalAlignment.Center };
        _clockLabel.AddThemeColorOverride("font_color", GameTheme.TextDim);
        _clockLabel.AddThemeFontSizeOverride("font_size", GameTheme.LegibilityFloor);
        verbCluster.AddChild(_clockLabel);

        var verbRow = new HBoxContainer { Name = "VerbRow" };
        verbRow.AddThemeConstantOverride("separation", GameTheme.Space8);
        verbCluster.AddChild(verbRow);

        // U15: the living clock flows by default, so the explicit control is now a
        // "Skip" — same underlying advance (AdvanceNow), just relabeled now that it is
        // the exception rather than the primary way forward (player intent always wins,
        // engaged or not). Node NAME stays "AdvancePhase" (existing tests press it by
        // name). The Auto toggle remains the escape hatch back to fully-manual mode.
        _advance = new Button { Name = "AdvancePhase", Text = "Skip", CustomMinimumSize = new Vector2(0, 36) };
        StylePrimaryVerb(_advance);
        _advance.AddThemeFontSizeOverride("font_size", GameTheme.HudValueFontSize);
        _advance.Pressed += () =>
        {
            var state = Adapter.CurrentState;
            // U5: ringing the bell while a counter session is open would otherwise silently fail to
            // advance — GameKernel holds the day at Morning while Counter is { Closed: false }. Close
            // the session first so the day ALWAYS moves, and surface it so it is never a silent
            // abandon of a live haggle/queue (the "never silently discards a live decision" goal).
            if (state.Counter is { Closed: false } counter)
            {
                Adapter.Queue(new CloseCounterAction());
                ShowBellToast(counter.Round > 0
                    ? "Closed the counter mid-haggle — parties depart."
                    : "Closed the counter — parties depart.");
            }

            Clock.AdvanceNow(); // same advance the auto timer fires — player intent wins even engaged
            UpdateClockLabel();
        };
        verbRow.AddChild(_advance);

        // U1 (playtest-three plan, KTD-A move 3): a SECOND, PERSISTENT entry to the Scrying
        // Mirror, next to the bell rather than gated behind it. The owner's loudest playtest line
        // — "clicked send them off... WHERE ARE THE VISUALS OF WHAT THEY ARE DOING??" — traced to
        // the corner PiP dock (Pip.ExpandRequested, wired below) being the ONLY door in, and that
        // dock is suppressed by the very drawer/modal a player has open when the day ends
        // (UpdateEngaged's `engaged` latch). This button sits outside that latch entirely and
        // calls ShowMirror() straight — Mirror content is already phase-ungated (see
        // ScryingMirror), so the one new gate is THIS button's own visibility (UpdateClockLabel),
        // never a second copy of the dock's suppression. The dock stays as the ambient
        // affordance; this is the guaranteed one.
        _watch = new Button
        {
            Name = "WatchButton", Text = "👁 Watch", CustomMinimumSize = new Vector2(0, 36),
            TooltipText = "Watch the raid — opens the Scrying Mirror",
        };
        _watch.Pressed += () => Mirror.ShowMirror();
        verbRow.AddChild(_watch);

        // UI-4: Auto/Pause/Speed collapse to small 24px icon buttons (Text is a short glyph, the
        // descriptive word lives on TooltipText, refreshed every UpdateClockLabel tick). Node
        // NAMEs and underlying behavior are unchanged — tests press these by name and never read
        // their Text/TooltipText.
        _auto = new Button
        {
            Name = "AutoAdvance", Text = "⏱", ToggleMode = true, CustomMinimumSize = new Vector2(24, 24),
        };
        _auto.Pressed += () =>
        {
            Clock.ToggleAuto();
            ClockSettings.SaveAutoAdvance(Clock.AutoAdvance); // U15 escape hatch: sticks across campaigns
            UpdateClockLabel();
            Timeline.Refresh(Adapter.CurrentState.Phase, Waiting); // U18: Auto gates the Waiting predicate too
        };
        verbRow.AddChild(_auto);

        _playPause = new Button { Name = "PlayPause", Text = "⏸", CustomMinimumSize = new Vector2(24, 24) };
        _playPause.Pressed += () =>
        {
            Clock.TogglePlay();
            UpdateClockLabel();
            Timeline.Refresh(Adapter.CurrentState.Phase, Waiting); // U18: Playing gates the Waiting predicate too
        };
        verbRow.AddChild(_playPause);

        _speed = new Button { Name = "Speed", Text = "1×", CustomMinimumSize = new Vector2(24, 24) };
        _speed.Pressed += () =>
        {
            Clock.CycleSpeed();
            UpdateClockLabel();
        };
        verbRow.AddChild(_speed);

        // --- UI-4 Zone 3: BOOKS TRAY (right) — Ledger/Forecast/Commissions/Legends/Demand/Renown/
        // Progress collapse to 28px icon-only buttons on a recessed (SurfaceDeep) tray; the full
        // names move to TooltipText. Icon picks reuse the existing glyph set rather than adding
        // new asset files (out of this presentation-only unit's scope) — some are thematic reuse
        // (Ledger's "skull" already marks fate rows inside LedgerModal itself), the rest are
        // best-available stand-ins flagged for a follow-up dedicated tray-icon pass. -------------
        var tray = new PanelContainer { Name = "BooksTray" };
        var trayStyle = new StyleBoxFlat
        {
            BgColor = GameTheme.SurfaceDeep,
            CornerRadiusBottomLeft = GameTheme.RadiusChip,
            CornerRadiusBottomRight = GameTheme.RadiusChip,
            CornerRadiusTopLeft = GameTheme.RadiusChip,
            CornerRadiusTopRight = GameTheme.RadiusChip,
            ContentMarginLeft = GameTheme.Space4,
            ContentMarginRight = GameTheme.Space4,
            ContentMarginTop = GameTheme.Space4,
            ContentMarginBottom = GameTheme.Space4,
        };
        tray.AddThemeStyleboxOverride("panel", trayStyle);
        headerRow.AddChild(tray);

        var trayRow = new HBoxContainer { Name = "BooksTrayRow" };
        trayRow.AddThemeConstantOverride("separation", GameTheme.Space4);
        tray.AddChild(trayRow);

        var ledgerButton = TrayButton("OpenLedger", IconRegistry.Glyph("skull"), "Ledger");
        ledgerButton.Pressed += () => Ledger.ShowFor(LastCompletedDay);
        trayRow.AddChild(CapTrayIcon(ledgerButton));

        // U10: open the raid-forecast board on demand (day-end auto-open is the chained path in
        // OnLedgerVisibilityChanged). Reads live state so it always reflects the current roster.
        var forecastButton = TrayButton("OpenForecast", IconRegistry.Glyph("depths"), "Forecast");
        forecastButton.Pressed += () => Forecast.ShowForTomorrow(Adapter.CurrentState);
        trayRow.AddChild(CapTrayIcon(forecastButton));

        // Wave 3 (U15): open the commission board on demand — a Prepare-phase surface, same tray
        // as Forecast. Reads live state so it always reflects the current board.
        var commissionsButton = TrayButton("OpenCommissions", IconRegistry.Glyph("bounty"), "Commissions");
        commissionsButton.Pressed += () => Commissions.ShowOpen(Adapter.CurrentState);
        trayRow.AddChild(CapTrayIcon(commissionsButton));

        // Wave 4 (U21): open the Legends Wall on demand — same tray as Forecast/Bestiary/Commissions.
        // Reads live state so it always reflects the current memorials/records/gear.
        var legendsButton = TrayButton("OpenLegends", IconRegistry.Glyph("rune"), "Legends");
        legendsButton.Pressed += () => Legends.ShowWall(Adapter.CurrentState);
        trayRow.AddChild(CapTrayIcon(legendsButton));

        // G1 (plan 2026-07-25-001, Slice 2): the demand telegraph had no player-visible entry —
        // DemandPanel was already registered in the Drawer (U6) and reachable via
        // OpenPanel("Demand"), but nothing ever called it. Same tray, wired straight onto the
        // drawer's own OpenPanel router (mirrors OnTownBuildingClicked's OpenPanel("Bounties")
        // call) rather than inventing a bespoke show method.
        var demandButton = TrayButton("OpenDemand", IconRegistry.Glyph("gossip"), "Demand");
        demandButton.Pressed += () => OpenPanel("Demand");
        trayRow.AddChild(CapTrayIcon(demandButton));

        // Phase B, B1d: the hero digest (standing/deepest/XP-rank/deeds card per alive hero) had
        // no HUD entry — same tray as Demand/Legends above. Opens "HeroCards" (not "Heroes" —
        // that drawer id is already the portrait-grid roster reached via town clicks); the
        // TooltipText stays "Renown" per this unit's brief.
        var heroesButton = TrayButton("OpenHeroCards", IconRegistry.Glyph("shield"), "Renown");
        heroesButton.Pressed += () => OpenPanel("HeroCards");
        trayRow.AddChild(CapTrayIcon(heroesButton));

        // U-D4: the progression spine — same tray. Opens the five-ladder board.
        var progressButton = TrayButton("OpenProgress", IconRegistry.Glyph("weapon"), "Progress");
        progressButton.Pressed += () => OpenPanel("Progress");
        trayRow.AddChild(CapTrayIcon(progressButton));

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

        // U21: the world renders through this gap — a transparent, input-passthrough spacer
        // claiming the exact vertical space the old TabContainer's ExpandFill claimed, so the
        // header stays pinned top and the ticker stays pinned bottom without either drawing over
        // (or blocking clicks into) the permanent world now visible underneath the whole Layout
        // column.
        var worldSlot = new Control
        {
            Name = "WorldSlot",
            SizeFlagsVertical = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        layout.AddChild(worldSlot);

        Forge = InstantiatePanel<ForgePanel>("res://scenes/panels/forge_panel.tscn");
        Shop = InstantiatePanel<ShopPanel>("res://scenes/panels/shop_panel.tscn");
        Heroes = InstantiatePanel<HeroesPanel>("res://scenes/panels/heroes_panel.tscn");
        Tavern = InstantiatePanel<TavernPanel>("res://scenes/panels/tavern_panel.tscn");
        Depths = InstantiatePanel<DepthsPanel>("res://scenes/panels/depths_panel.tscn");
        Depths.Clock = Clock; // U25 (a): MineWatch's journey feed pauses with the clock
        Bounties = InstantiatePanel<BountyPanel>("res://scenes/panels/bounty_panel.tscn");
        Demand = InstantiatePanel<DemandPanel>("res://scenes/panels/demand_panel.tscn");
        HeroCards = InstantiatePanel<HeroPanel>("res://scenes/panels/hero_panel.tscn");
        Progress = new ProgressionPanel(); // U-D4: code-built (no scene deps), like BestiaryPanel

        // U17 (KTD13): the single bottom-edge HUD line — mounted last in the layout so it sits
        // below the world gap, the one region KTD13 reserves for it (PiP docks above it; top bar
        // and the top-right objective chip are untouched by this unit).
        // Menu-sizing fix (U2, playtest F1): AdventureTicker is a PanelContainer whose Label has
        // AutowrapMode.Off (deliberate — a scrolling marquee, never wrapped), so its OWN combined
        // minimum width is the FULL unwrapped width of the joined marquee line — once real events
        // land (first tick) that can be 2000+px. Added straight into `layout` (a VBoxContainer),
        // that minimum propagates upward and inflates the WHOLE layout's width past the viewport,
        // which is what actually pushed Skip/Auto/Pause/1x/Ledger off-screen (not the stat chips —
        // those are already capped by StatChipsWrap/TimelineWrap above). Same fix as those wraps:
        // a plain (non-Container) Control cuts the upward minimum-size propagation at exactly this
        // width (0 — the ticker's real minimum height, 28, still travels up so the world gap keeps
        // reserving the right vertical space); ClipContents keeps the marquee's own scroll/clip
        // rendering inside it exactly as before.
        var tickerWrap = new Control
        {
            Name = "TickerWrap",
            ClipContents = true,
            CustomMinimumSize = new Vector2(0, 28),
        };
        layout.AddChild(tickerWrap);
        Ticker = new AdventureTicker();
        Ticker.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        tickerWrap.AddChild(Ticker);
        Ticker.Build();

        // --- U21: DrawerHost — replaces the TabContainer. A right-anchored ~600px panel that
        // slides over the permanent world; one panel at a time (OpenPanel below REPLACES, never
        // stacks). Dim-under (LedgerModal precedent) + click-out/Esc close; the click-out consumes
        // the input event structurally (the dim veil's default Stop mouse filter), so it never
        // reaches the 3D world's own click-to-move/interact input underneath. -------------------
        Drawer = new DrawerHost();
        AddChild(Drawer);
        Drawer.Build();
        Drawer.Register("Forge", Forge);
        Drawer.Register("Shop", Shop);
        Drawer.Register("Heroes", Heroes);
        Drawer.Register("Tavern", Tavern);
        Drawer.Register("Depths", Depths);
        Drawer.Register("Bounties", Bounties);
        Drawer.Register("Demand", Demand);
        Drawer.Register("HeroCards", HeroCards);
        Drawer.Register("Progress", Progress);
        // LW6: the drawer-swap fade veil (was the tab-switch veil pre-U21) — a purely additive
        // CanvasLayer-100 overlay, triggered from OpenPanel below, and from a click-out/Esc close
        // that bypasses OpenPanel entirely (Drawer.Closed).
        TabFade = new TabFade();
        AddChild(TabFade);
        TabFade.Build();
        Drawer.Closed += () =>
        {
            TabFade.Trigger();
            UpdateEngaged(); // click-out/Esc close the same latch update an OpenPanel("Town") gets
        };

        // --- ledger modal overlay (sibling after the drawer = draws on top) --
        Ledger = GD.Load<PackedScene>("res://scenes/panels/ledger_modal.tscn").Instantiate<LedgerModal>();
        AddChild(Ledger);
        Ledger.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Ledger.VisibilityChanged += OnLedgerVisibilityChanged;

        // --- U10 raid-forecast board: a code-built modal sibling (no scene, no import churn),
        //     drawn above the drawer like the Ledger. Chained after the day-end Ledger (see
        //     OnLedgerVisibilityChanged) and re-openable from the "Forecast" HUD button.
        Forecast = new RaidForecastBoard();
        AddChild(Forecast);
        Forecast.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Forecast.VisibilityChanged += OnForecastVisibilityChanged;

        // --- Bestiary (gate-b flag 3): code-built modal sibling, opened from the Tavern hotspot.
        Bestiary = new BestiaryPanel();
        AddChild(Bestiary);
        Bestiary.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Bestiary.VisibilityChanged += OnBestiaryVisibilityChanged;

        // --- the ending chronicle: code-built modal sibling on the RaidForecastBoard precedent
        //     (no scene, no import churn). Mounted LAST of the overlays so the campaign's closing
        //     beat draws above the Ledger it arrives alongside.
        Chronicle = new ChronicleScroll();
        AddChild(Chronicle);
        Chronicle.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // --- Wave 3 (U15) commission board: code-built modal sibling, mirroring RaidForecastBoard.
        //     Unlike Forecast it submits actions, so it needs the adapter handed in (Depths.Clock
        //     precedent) rather than a SimPanel binding.
        Commissions = new CommissionBoard { Adapter = Adapter };
        AddChild(Commissions);
        Commissions.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Commissions.VisibilityChanged += OnCommissionsVisibilityChanged;

        // --- Wave 4 (U21) Legends Wall: a code-built modal sibling, mirroring RaidForecastBoard.
        //     Wave 4c (U18/U20): now submits actions (Honor/Reforge), so it needs the adapter
        //     handed in too (CommissionBoard precedent) — ShowWall itself still takes the live
        //     GameState explicitly.
        Legends = new LegendsWall { Adapter = Adapter };
        AddChild(Legends);
        Legends.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Legends.VisibilityChanged += OnLegendsVisibilityChanged;

        // --- camp decision slate (V7a): a second modal overlay, code-built (no scene, so no
        //     .tscn/import metadata churn). Camp (phase 3) and the Evening Ledger never show at
        //     once, so the two overlays never contend.
        Camp = new CampPanel { Name = "CampModal" };
        AddChild(Camp);
        Camp.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Camp.VisibilityChanged += OnCampVisibilityChanged;

        // --- objective chip (U18/KTD13): a floating overlay sibling (like the modals above),
        //     anchored top-right at a FIXED width and nudged down by ObjectiveDockOffsetTop to
        //     clear the header row — stays visible over the bare town without shifting any
        //     panel's own layout. Populated by RefreshHud. Menu-sizing fix (gate-b): docked via
        //     explicit OffsetLeft/OffsetRight rather than SetAnchorsAndOffsetsPreset(...,
        //     LayoutPresetMode.Minsize, ...) — Minsize snapshots the CURRENT (collapsed, at
        //     build time) minimum width into a one-time offset, so the chip never grew to
        //     DockWidth even with CustomMinimumSize set, which is exactly the ~1-char-wide
        //     playtest bug.
        Objective = new ObjectiveTracker();
        Objective.Build();
        AddChild(Objective);
        Objective.SetAnchorsPreset(LayoutPreset.TopRight);

        // NOTE (2026-07-29): the layout probe reports this dock's right edge 6px past the window,
        // because the panel's own minimum is DockWidth PLUS its PanelContainer margins. Widening the
        // span to reserve that chrome DOES fix the 6px — and it re-flowed the card's contents so the
        // quick-travel row overlapped the "Today" title, which is far more visible than a 6px
        // overhang. Left as-is deliberately: the 6px is cosmetic, the overlap was not. Fixing it
        // properly means making the dock's internal height calculation width-aware
        // (see UpdateObjectiveDock), which is its own change and not part of a HUD-overflow pass.
        Objective.OffsetLeft = -ObjectiveDockWidth - ObjectiveDockMargin;
        Objective.OffsetRight = -ObjectiveDockMargin;
        UpdateObjectiveDock(); // initial content-height dock (see method doc)
        Objective.Expand.Pressed += UpdateObjectiveDock; // "More" toggles the ranked list's height
        Objective.TutorialDismiss.Pressed += () =>
        {
            Tutorial.Dismiss();
            RefreshHud();
        };

        // --- U23: the tutorial-flow overlay (chain state lives here; its visible chrome is just
        //     the earn-2nd-profession picker + quick-travel row — the chain's OWN top-slot text
        //     renders through the objective chip above, never a second visible HUD element).
        //     Stacked below the objective chip in the same top-right column (KTD13 precedent). --
        Tutorial = new TutorialFlow { CustomMinimumSize = new Vector2(ObjectiveDockWidth, 0) };
        Tutorial.Build();
        AddChild(Tutorial);
        // Dock at the FULL objective width via explicit offsets, NOT LayoutPresetMode.Minsize:
        // Minsize snapshots the collapsed build-time min width into the offset, pinning a sliver-
        // wide panel to the right edge — the same bug already fixed for the Objective chip above
        // (playtest 2026-07-24). The panel self-hides when it has no live affordance (TutorialFlow
        // .RefreshAffordances), so on Day 1 nothing shows here at all.
        Tutorial.SetAnchorsPreset(LayoutPreset.TopRight);
        Tutorial.OffsetLeft = -ObjectiveDockWidth - ObjectiveDockMargin;
        Tutorial.OffsetRight = -ObjectiveDockMargin;
        Tutorial.OffsetTop = TutorialDockOffsetTop;
        Tutorial.OffsetBottom = TutorialDockOffsetTop + Tutorial.GetCombinedMinimumSize().Y;
        Tutorial.SecondProfessionPicked += OnSecondProfessionPicked;
        Tutorial.QuickTravelRequested += QuickTravel;
        Tutorial.Load(); // user:// (KTD2 — never the sim save): adopt a prior dismiss/complete

        // --- U16 (KTD11/KTD13): the scrying mirror (a third same-shaped modal overlay — Camp/
        //     Ledger/Mirror never show at once in practice, but nothing here assumes it) and its
        //     PiP dock, the ONLY new always-on HUD element this unit adds — a small bottom-right
        //     corner Control, independent of the header/Drawer/Ticker/Objective regions U17/U18
        //     touch. -----------------------------------------------------------------------
        Mirror = new ScryingMirror { Name = "ScryingMirror" };
        AddChild(Mirror);
        Mirror.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Mirror.VisibilityChanged += OnMirrorVisibilityChanged;

        Pip = new PipDock();
        AddChild(Pip);
        Pip.Build();
        Pip.ExpandRequested += () => Mirror.ShowMirror();
        Pip.Clock = Clock; // U25 (a): PiP's journey feed pauses with the clock

        // --- U22: InteriorStage — the staged-interior framework (R4/KTD10), mounted LAST so it
        //     draws above the drawer/HUD/every modal. 2.5D pivot (U2): nothing currently opens it
        //     (OnTownBuildingClicked routes straight to OpenPanel), but it stays wired — see
        //     the Interior property's own doc. ---------------------------------------------
        Interior = new InteriorStage();
        AddChild(Interior);
        Interior.Build();
        Interior.HotspotActivated += OnInteriorHotspotActivated;
        Interior.Exited += OnInteriorExited;

        // --- build-provenance stamp (deploy hygiene): a small always-visible corner label naming
        //     this build — mounted last so it draws over everything else. See BuildStamp's own
        //     doc; no other MainUi behavior changes here. ---
        var buildStamp = new BuildStamp();
        AddChild(buildStamp);
        buildStamp.Build();

        // Open the play-session log (no-op unless MM_PLAYTEST_LOG is set — see PlaytestLog). Done
        // here rather than earlier because the build label is the provenance the log header records,
        // and an unattributable log is the problem the stamp itself exists to prevent.
        PlaytestLog.Begin(buildStamp.BuildLabel);
    }

    private static T InstantiatePanel<T>(string scenePath) where T : SimPanel =>
        GD.Load<PackedScene>(scenePath).Instantiate<T>();

    /// <summary>
    /// U21: the one entry point that opens a management surface — replaces the old
    /// <c>Tabs.CurrentTab = ...</c> routing. <paramref name="id"/> is one of "Forge" | "Shop" |
    /// "Heroes" | "Tavern" | "Depths" | "Bounties" | "Demand" | "HeroCards" | "Town" (the last one,
    /// and any drawer already open, both resolve through <see cref="DrawerHost.Close"/> — "Town" IS the bare-world state,
    /// not a drawer). A drawer already open when this is called is REPLACED, never stacked
    /// (<see cref="DrawerHost.Open"/>'s own contract). Opening a panel refreshes it on the spot —
    /// <see cref="RefreshAll"/> is visibility-gated (U21), so this is what guarantees a panel a
    /// player actually opens is never stale from ticks that happened while it was hidden.
    /// </summary>
    public void OpenPanel(string id)
    {
        if (id == "Town")
        {
            Drawer.Close();
            Audio.Play(Cue.PanelClose);
            Audio.SetScene(null); // back above ground
        }
        else
        {
            Drawer.Open(id);
            PanelFor(id).Refresh();
            Audio.Play(EntranceCueFor(id));
            // Watching the raid gets the Mine's own theme; every other panel stays with the day.
            Audio.SetScene(id == "Depths" ? "depths" : null);
        }

        // The tutorial's copy depends on WHICH surface is open (it stops telling you to walk somewhere you
        // are already standing), so opening or closing the drawer has to re-render that card as well as the
        // panel. Without this the acknowledgement only appeared on the next state change — the same
        // stale-instruction bug it exists to fix, just deferred.
        //
        // The CARD only, never the whole HUD: a full RefreshHud here took Playtest3dClickThrough (which opens
        // every panel in every phase across a session) from 27 seconds to past the runner's timeout, which
        // silently took ~200 unrelated tests down with it.
        RefreshObjectiveLine();

        TabFade.Trigger();
        UpdateEngaged();
    }

    /// <summary>
    /// The tick's one sound, chosen by what actually happened. Deliberately ONE cue per tick and not a
    /// cue per event: a busy Evening can carry a dozen sales and a death, and firing a sound for each
    /// turns the most dramatic moment of the day into a burst of noise. Priority order is "worst news
    /// first" — a refusal is the thing the player most needs to notice, then the day's own bell.
    /// </summary>
    private void SoundTheTick(DayPhase completedPhase, GameState state)
    {
        if (!Adapter.LastRejections.IsEmpty)
        {
            Audio.Play(Cue.Rejected);
            return;
        }

        // Morning ending is the send-off: the party is actually leaving, which deserves its own cue
        // rather than the generic bell.
        //
        // `completedPhase == state.Phase` catches the OTHER caller of this event: SimAdapter.Queue's
        // immediate-action branch (buy/craft/stock/reprice — the 2026-07-30 fix) raises StateChanged
        // with the CURRENT, un-advanced phase, because nothing actually completed — see Queue's own
        // doc. Without this guard every accepted craft/buy during Morning read as "the party just
        // departed": the wrong cue today, and — once this unit wired Drawer.Close() below to
        // `departing` — the Forge/Shop drawer slamming shut under the player's own click, which would
        // have been a far worse regression than the bug this method exists to fix. `state` is already
        // the POST-event CurrentState (fetched by the caller, OnPhaseCompleted), so the comparison
        // costs nothing extra.
        var departing = completedPhase == DayPhase.Morning && completedPhase != state.Phase;
        Audio.Play(departing ? Cue.PartyDepart : Cue.Bell);

        if (!departing)
        {
            return;
        }

        // And show it. The HUD has been promising "watch them go" while the gate sat off screen at the
        // north edge and the rally marker appeared outside the view — Brian never once saw a departure.
        //
        // U1 (playtest-three plan, KTD-A): the ORIGINAL gate here was `!Drawer.IsOpen &&
        // !Interior.IsOpen && !Ledger.Visible` — skip the whole beat while a surface owns the screen.
        // That IS the reported bug: a normal Morning ends with a drawer open (craft, then send them
        // off), so the common case skipped the pan every time and the PiP dock stayed suppressed
        // underneath it. "Clicked send them off; not sure what's happening next... WHERE ARE THE
        // VISUALS" was this gate firing correctly, every day, on the one drawer that is open at the
        // exact click that causes it.
        //
        // Fix, per KTD-A: (1) CLOSE the open drawer instead of stepping around it — legitimate,
        // because the player just deliberately ended Morning, and a drawer's verbs are all Morning
        // verbs. DrawerHost.Close() is safe re: #331 (see SimPanel.Clear's doc): it only flips
        // CurrentPanelId and starts the slide-out animation (Tick, driven from _Process) — it frees
        // nothing synchronously, so calling it here, inside OnPhaseCompleted's stack (itself
        // synchronous with whatever pressed the bell), cannot repeat the "freed a node mid-signal"
        // crash. Drawer.Closed already fires UpdateEngaged (see its subscription in BuildUi), so
        // Pip.Suppressed recomputes within this same call — the dock does not wait for a frame that
        // never arrives in a paused/test context.
        Drawer.Close();

        // (2) a genuine MODAL (Ledger/Camp/Mirror/Forecast/Bestiary/Commissions/Legends, or the
        // staged Interior) is a different case from a drawer: the player opened it on purpose
        // mid-Morning, and it is not this method's place to close it out from under them. Defer the
        // beat instead of dropping it — whichever modal-close path finds the screen clear next fires
        // it (TryFireDeferredMineGateFocus).
        if (ModalOwnsTheScreen())
        {
            _pendingMineGateFocus = true;
            return;
        }

        Town.FocusOnMineGate();
    }

    /// <summary>
    /// (3) Fires the departure focus beat <see cref="SoundTheTick"/> deferred because a modal owned
    /// the screen at the actual send-off tick. Called from every modal-close path (each surface's
    /// own <c>VisibilityChanged</c> handler, plus <see cref="OnInteriorExited"/>) so the pan lands
    /// the instant nothing is left covering the town — however many modals the player opens and
    /// closes in between, and regardless of which one happens to be the last to go.
    /// </summary>
    private void TryFireDeferredMineGateFocus()
    {
        if (_pendingMineGateFocus && !ModalOwnsTheScreen())
        {
            _pendingMineGateFocus = false;
            Town.FocusOnMineGate();
        }
    }

    /// <summary>
    /// True while a modal overlay (Ledger/Camp/Mirror/Forecast/Bestiary/Commissions/Legends) or the
    /// staged Interior covers the middle of the screen — the "not a drawer" half of <see
    /// cref="UpdateEngaged"/>'s engaged latch, pulled into its own method so U1's departure-focus
    /// pending beat (above) reads the EXACT same predicate instead of a second hand-copied clause
    /// list that could silently drift from it.
    /// </summary>
    private bool ModalOwnsTheScreen() =>
        Interior.IsOpen || Ledger.Visible || Camp.Visible || Mirror.Visible
        || Forecast.Visible || Bestiary.Visible || Commissions.Visible || Legends.Visible;

    /// <summary>
    /// U-audio-2: which cue plays when <paramref name="id"/> opens. Owner's playtest: "Noises for the
    /// buildings are identical as before... should make noises correlating to their building" — every
    /// panel fired the same generic <see cref="Cue.PanelOpen"/> regardless of which of the five physical
    /// Town2D buildings (or which non-building drawer) it was. Only the five real buildings — Forge,
    /// Shop (the market stall), Tavern, Depths (the mine gate), Bounties (the noticeboard) — get their
    /// own cue; Heroes/Demand/HeroCards/Progress are management surfaces with no building in the world
    /// to correlate to, so they keep the generic knock-and-slide. A DATA TABLE for the same reason
    /// <see cref="PanelFor"/> is one: adding a sixth building's cue later is a one-line edit here.
    /// </summary>
    private static Cue EntranceCueFor(string id) => id switch
    {
        "Forge" => Cue.EnterForge,
        "Shop" => Cue.EnterMarket,
        "Tavern" => Cue.EnterTavern,
        "Depths" => Cue.EnterMineGate,
        "Bounties" => Cue.EnterNoticeboard,
        _ => Cue.PanelOpen,
    };

    /// <summary>The drawer-hosted panel registered under <paramref name="id"/> — "Town" is not a
    /// drawer panel (the world is the permanent base, not routed through here).</summary>
    private SimPanel PanelFor(string id) => id switch
    {
        "Forge" => Forge,
        "Shop" => Shop,
        "Heroes" => Heroes,
        "Tavern" => Tavern,
        "Depths" => Depths,
        "Bounties" => Bounties,
        "Demand" => Demand,
        "HeroCards" => HeroCards,
        "Progress" => Progress,
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "no such drawer panel"),
    };

    /// <summary>
    /// UI-4 (menu-sizing/cozy redesign): the PRIMARY VERB button's Ember-filled surface — now
    /// delegates to <see cref="GameTheme.ButtonStylePrimary"/> (the shared foundation builder that
    /// formalized this exact per-node override) instead of hand-recombining <see
    /// cref="GameTheme.ButtonStyle"/>/<see cref="GameTheme.AccentColor"/> locally. Was named
    /// <c>StylePrimary</c> pre-redesign (Accent/Arcane-tinted); renamed alongside the swap to
    /// Ember so the name matches the look.
    /// </summary>
    private static void StylePrimaryVerb(Button button)
    {
        button.AddThemeStyleboxOverride("normal", GameTheme.ButtonStylePrimary());
        button.AddThemeStyleboxOverride("hover", GameTheme.ButtonStylePrimary(GameTheme.ButtonVisualState.Hover));
        button.AddThemeStyleboxOverride("pressed", GameTheme.ButtonStylePrimary(GameTheme.ButtonVisualState.Pressed));

        button.AddThemeColorOverride("font_color", GameTheme.BoneColor);
        button.AddThemeColorOverride("font_color_hover", GameTheme.BoneColor);
        button.AddThemeColorOverride("font_color_pressed", GameTheme.BoneColor);
    }

    /// <summary>UI-4: a 28px icon-only Books Tray button — the full label moves to <see
    /// cref="Control.TooltipText"/> (mirrors <see cref="UiKit.DrawerHeader"/>'s icon-plus-tooltip
    /// convention for its own Close button).</summary>
    private static Button TrayButton(string name, Texture2D? icon, string tooltip) => new()
    {
        Name = name,
        Icon = icon,
        TooltipText = tooltip,
        CustomMinimumSize = new Vector2(TrayIconSize + 8, TrayIconSize + 8),
    };

    /// <summary>Rendered edge of a Books Tray glyph, in px.</summary>
    private const int TrayIconSize = 22;

    /// <summary>
    /// Cap a tray button's glyph so the TEXTURE stops driving the button's minimum width.
    /// <para>Layout-probe finding (2026-07-29): <see cref="Control.CustomMinimumSize"/> is a FLOOR,
    /// not a cap, so these nominally-28px icon-only buttons actually measured 88x76 — seven of them
    /// made the tray 648px wide and pushed the header row past the window edge, rendering the
    /// right-most icon sliced in half.</para>
    /// <para>The first attempt used <c>ExpandIcon</c>, which fits the glyph to the button's CONTENT
    /// rect — and with the tray's 4px margins plus button padding that rect was tiny, so all seven
    /// icons collapsed to near-invisible dots. Trading a sliced icon for seven blank buttons is not
    /// a fix. The <c>icon_max_width</c> theme constant is the correct lever: it bounds the glyph at a
    /// definite size, which bounds the button's minimum, and the icon still renders legibly.</para>
    /// </summary>
    private static Button CapTrayIcon(Button button)
    {
        button.AddThemeConstantOverride("icon_max_width", TrayIconSize);
        return button;
    }

    /// <summary>Town hero click (R20): open the Heroes drawer with that hero's detail bound.</summary>
    private void OnTownHeroClicked(int heroValue)
    {
        OpenPanel("Heroes");
        Heroes.SelectHero(heroValue);
    }

    /// <summary>
    /// Town building click/interact (R20, T8): 2.5D pivot (U2) — routes straight onto <see
    /// cref="OpenPanel"/>, no staged interior and no camera push-in for this slice. <see
    /// cref="Town2D"/>'s <see cref="Building2D"/> emits its lowercase venue keys
    /// ("forge"/"market"/"tavern"/"minegate"/"noticeboard"); the legacy capitalized names
    /// ("Forge"/"Shop"/"Tavern"/"Gate"/"Bounties") are accepted too since <see
    /// cref="QuickTravel"/> and <c>TutorialFlow</c>'s own quick-travel row (out of this unit's
    /// edit scope) still send them. Any unknown key falls back to the bare-world "Town" id.
    /// </summary>
    private void OnTownBuildingClicked(string building)
    {
        var panelId = building switch
        {
            "forge" or "Forge" => "Forge",
            "market" or "Shop" => "Shop",
            "tavern" or "Tavern" => "Tavern",
            "minegate" or "Gate" => "Depths",
            "noticeboard" or "Bounties" => "Bounties",
            _ => "Town",
        };

        OpenPanel(panelId);
    }

    /// <summary>
    /// U23 (R5): jump straight to <paramref name="building"/>'s interior without walking there —
    /// the shortcut half of the quick-travel unlock, gated on <see
    /// cref="TutorialFlow.QuickTravelUnlocked"/> so both the hotkey path (<see cref="_Process"/>)
    /// and <see cref="Tutorial"/>'s own clickable venue-jump row funnel through the SAME check and
    /// the SAME routing <see cref="OnTownBuildingClicked"/> already uses (content parity —
    /// quick-travel never opens anything a walked arrival could not). Public so a test can call it
    /// directly — a real hotkey press reaches it via <see cref="_Process"/> in production.
    /// </summary>
    public void QuickTravel(string building)
    {
        if (!Tutorial.QuickTravelUnlocked)
        {
            return;
        }

        OnTownBuildingClicked(building);
    }

    /// <summary>U23: register the quick-travel number-row hotkeys at runtime (KTD4) — guarded so
    /// repeated mounts in the same test process never double-add the same action.</summary>
    private static void RegisterQuickTravelActions()
    {
        foreach (var (action, key, _) in QuickTravelHotkeys)
        {
            if (InputMap.HasAction(action))
            {
                continue;
            }

            InputMap.AddAction(action);
            InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
        }
    }

    /// <summary>
    /// U23: earn-2nd-profession affordance — a profession picked from <see
    /// cref="TutorialFlow.ProfessionPicker"/> unions onto the save's current selection (never
    /// replaces it) and queues <see cref="SetProfessionsAction"/> for the next tick (sim already
    /// permits <c>ProfessionHandlers.MaxSelected</c> = 2, no sim change).
    /// </summary>
    private void OnSecondProfessionPicked(string professionId)
    {
        var current = Adapter.CurrentState.Player.SelectedProfessions;
        if (current.Contains(professionId))
        {
            return;
        }

        Adapter.Queue(new SetProfessionsAction(current.Add(professionId)));
    }

    /// <summary>
    /// A content hotspot (never exit) was pressed inside the interior — open the SAME drawer id
    /// the hotspot's action names. 2.5D pivot (U2): <see cref="OnTownBuildingClicked"/> no longer
    /// routes through <see cref="InteriorStage"/> to reach here (nothing currently opens the
    /// stage), but the handler stays live and correct in case a later slice reintroduces it.
    /// </summary>
    private void OnInteriorHotspotActivated(string action)
    {
        // Gate-b flag 3: the Tavern "Bestiary" hotspot opens the code-built modal, not a drawer —
        // route it before OpenPanel (which only knows the drawer ids and would throw).
        if (action == "Bestiary")
        {
            Bestiary.ShowAll();
            return;
        }

        // Wave 4 (U21): the Tavern "Legends" hotspot opens the code-built Legends Wall modal —
        // same routing shape as Bestiary above.
        if (action == "Legends")
        {
            Legends.ShowWall(Adapter.CurrentState);
            return;
        }

        OpenPanel(action);
    }

    /// <summary>The exit hotspot or Esc closed the interior — re-sync the Engaged latch. 2.5D
    /// pivot (U2): no 3D room/avatar-door restore to unwind in this slice (see
    /// <see cref="OnInteriorHotspotActivated"/>'s doc).</summary>
    private void OnInteriorExited()
    {
        UpdateEngaged();
        TryFireDeferredMineGateFocus(); // U1: fires the deferred departure pan if the screen is now clear
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
        else if (_showForecastOnLedgerClose)
        {
            // U10 day-end chain: the Ledger just closed after the Evening reveal — pop the raid
            // forecast next, inheriting the Ledger's own resume intent (the clock stays paused
            // through the board; play resumes only when the board itself closes).
            _showForecastOnLedgerClose = false;
            _forecastChaining = true;
            _resumePlayOnForecastClose = _resumePlayOnLedgerClose;
            Forecast.ShowForTomorrow(Adapter.CurrentState);
        }
        else if (_resumePlayOnLedgerClose)
        {
            Clock.Play();
        }

        UpdateEngaged(); // the Ledger modal engages the latch too
        UpdateClockLabel();
        TryFireDeferredMineGateFocus(); // U1: fires the deferred departure pan if the screen is now clear
    }

    /// <summary>U10: mirror of <see cref="OnLedgerVisibilityChanged"/> for the raid-forecast board —
    /// pause the clock while it owns the screen, resume on close when play was running (or when the
    /// day-end chain inherited that intent). Never resumes if the board opened over a paused clock.</summary>
    private void OnForecastVisibilityChanged()
    {
        if (Forecast.Visible)
        {
            if (!_forecastChaining)
            {
                _resumePlayOnForecastClose = Clock.Playing;
            }

            _forecastChaining = false;
            Clock.Pause();
        }
        else if (_resumePlayOnForecastClose)
        {
            Clock.Play();
        }

        UpdateEngaged();
        UpdateClockLabel();
        TryFireDeferredMineGateFocus(); // U1: fires the deferred departure pan if the screen is now clear
    }

    /// <summary>Gate-b flag 3: mirror of the Forecast/Ledger latch for the Bestiary modal — pause
    /// while it owns the screen, resume on close when play was running.</summary>
    private void OnBestiaryVisibilityChanged()
    {
        if (Bestiary.Visible)
        {
            _resumePlayOnBestiaryClose = Clock.Playing;
            Clock.Pause();
        }
        else if (_resumePlayOnBestiaryClose)
        {
            Clock.Play();
        }

        UpdateEngaged();
        UpdateClockLabel();
        TryFireDeferredMineGateFocus(); // U1: fires the deferred departure pan if the screen is now clear
    }

    /// <summary>Wave 3 (U15): mirror of the Bestiary/Forecast latch for the commission board — pause
    /// while it owns the screen, resume on close when play was running.</summary>
    private void OnCommissionsVisibilityChanged()
    {
        if (Commissions.Visible)
        {
            _resumePlayOnCommissionsClose = Clock.Playing;
            Clock.Pause();
        }
        else if (_resumePlayOnCommissionsClose)
        {
            Clock.Play();
        }

        UpdateEngaged();
        UpdateClockLabel();
        TryFireDeferredMineGateFocus(); // U1: fires the deferred departure pan if the screen is now clear
    }

    /// <summary>Wave 4 (U21): mirror of the Bestiary/Forecast latch for the Legends Wall — pause
    /// while it owns the screen, resume on close when play was running.</summary>
    private void OnLegendsVisibilityChanged()
    {
        if (Legends.Visible)
        {
            _resumePlayOnLegendsClose = Clock.Playing;
            Clock.Pause();
        }
        else if (_resumePlayOnLegendsClose)
        {
            Clock.Play();
        }

        UpdateEngaged();
        UpdateClockLabel();
        TryFireDeferredMineGateFocus(); // U1: fires the deferred departure pan if the screen is now clear
    }

    /// <summary>The scrying mirror holds the town clock while open, same as Ledger/Camp — reading a
    /// live journey feed should not have the day marching on unseen behind it.</summary>
    private void OnMirrorVisibilityChanged()
    {
        if (Mirror.Visible)
        {
            _resumePlayOnMirrorClose = Clock.Playing;
            Clock.Pause();
        }
        else if (_resumePlayOnMirrorClose)
        {
            Clock.Play();
            _resumePlayOnMirrorClose = false;
        }

        UpdateEngaged();
        UpdateClockLabel();
        TryFireDeferredMineGateFocus(); // U1: fires the deferred departure pan if the screen is now clear
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

        UpdateEngaged(); // the Camp modal engages the latch too
        UpdateClockLabel();
        TryFireDeferredMineGateFocus(); // U1: fires the deferred departure pan if the screen is now clear
    }

    /// <summary>
    /// U8 (day-1 attribution pacing): once a <see cref="CraftAction"/> is queued for day 1's
    /// Morning batch but a matching <see cref="StockAction"/> (shelve) is not YET queued, hold
    /// <see cref="PhaseClock.Engaged"/> — even with no drawer/interior/modal open — so the walk
    /// from the Forge to the Shop (the one genuinely unengaged stretch of the tutorial's
    /// Buy→Craft→Shelve chain) cannot let the Morning timer expire mid-walk. An expired Morning
    /// on day 1 applies the queued batch BEFORE the shelve exists, pushing craft+shelve into the
    /// Expedition phase — legal every phase (<see cref="GameSim.Economy.ShopHandlers"/>'s own
    /// class doc), but a day too late for THAT Morning's
    /// <see cref="GameSim.Heroes.HeroShoppingSystem"/> pass: the item cannot sell until day 2's
    /// Morning, which is exactly the day-2 ★ attribution delay this unit closes. Released the
    /// instant a StockAction is ALSO queued — the pending batch now carries both (actions apply
    /// before systems, <see cref="GameSim.Kernel.GameKernel.Tick"/> steps 1/2), so THIS Morning's
    /// hero-shopping pass will see the freshly shelved item. Never engages on any later day (the
    /// "craft during Expedition" steady-state loop ShopHandlers documents for day 2+ is
    /// untouched) and never engages before a craft is queued at all — a fresh, untouched Morning
    /// still ticks exactly as before (<c>MainUiTests.ClosedDrawer_TimerExpiry_...</c>).
    /// </summary>
    /// <summary>
    /// True during the day-1 walk from the Forge to the Shop: the player has made something and not yet
    /// shelved it, and the Morning must not expire out from under them (see <see cref="UpdateEngaged"/>).
    ///
    /// <para>Reads the WORLD, not the action queue. It used to ask whether a <c>CraftAction</c> was
    /// pending and a <c>StockAction</c> was not — which stopped meaning anything the moment workshop
    /// verbs began resolving immediately (see <c>ActionTiming</c>): neither action ever reaches
    /// <c>PendingActions</c> now, so the hold silently evaluated false forever and the timer could expire
    /// mid-walk again. Caught by <c>MainUiTests.Day1CraftQueuedButNotYetShelved...</c>, which is the
    /// suite earning its keep.</para>
    ///
    /// <para>Asking the state instead is also just a better question. "Do I own a finished craft that is
    /// not on a shelf" is the actual condition the pacing guard cares about, it stays true across a tick
    /// or a reload, and it cannot be desynchronised from the queue's timing ever again.</para>
    /// </summary>
    private bool Day1CraftToShelvePacingHold
    {
        get
        {
            var state = Adapter.CurrentState;
            if (state.Day != 1 || state.Phase != DayPhase.Morning)
            {
                return false;
            }

            var shelved = state.Player.Shelf.Select(entry => entry.Item.Value).ToHashSet();
            return state.Items.Values.Any(item => item.PlayerCrafted && !shelved.Contains(item.Id.Value));
        }
    }

    /// <summary>
    /// U15/U21/U22 (KTD3/AE1/R7): real drawer/interior/modal state engages <see
    /// cref="PhaseClock.Engaged"/> — the bare world (no drawer open, no interior staged, no modal
    /// visible) is the only flowing surface; any open drawer (<see cref="DrawerHost.IsOpen"/>),
    /// staged interior (<see cref="InteriorStage.IsOpen"/>), or modal overlay (Ledger/Camp/Mirror)
    /// engages the latch so an expired phase timer holds at the boundary instead of ticking.
    /// </summary>
    /// <summary>
    /// Parks the objective/tutorial dock against the left window edge instead of the right.
    ///
    /// <para>Only used to keep the tutorial card readable while a drawer owns the right-hand ~600px (see
    /// <see cref="UpdateEngaged"/>). Anchors are flipped rather than the card being resized, so its own
    /// content-height docking (<see cref="UpdateObjectiveDock"/>) keeps working untouched — that method owns
    /// the vertical, this one owns the horizontal, and they do not need to know about each other.</para>
    /// </summary>
    private void DockObjectiveHorizontally(bool toLeftEdge)
    {
        if (toLeftEdge)
        {
            Objective.AnchorLeft = 0f;
            Objective.AnchorRight = 0f;
            Objective.OffsetLeft = ObjectiveDockMargin;
            Objective.OffsetRight = ObjectiveDockMargin + ObjectiveDockWidth;
            return;
        }

        Objective.AnchorLeft = 1f;
        Objective.AnchorRight = 1f;
        Objective.OffsetLeft = -ObjectiveDockWidth - ObjectiveDockMargin;
        Objective.OffsetRight = -ObjectiveDockMargin;
    }

    private void UpdateEngaged()
    {
        // Split out from `engaged` because the two cases leave the screen in different shapes: the side
        // drawer occupies a fixed right-hand column, while an interior or a modal covers the middle of the
        // window with no reliable free strip. Only the first one leaves anywhere to put the tutorial card.
        var modalOwnsTheScreen = ModalOwnsTheScreen(); // U1: shared with SoundTheTick's departure gate
        var engaged = Drawer.IsOpen || modalOwnsTheScreen;

        // U8: Clock.Engaged can ALSO be held by the day-1 craft→shelve pacing guard above —
        // deliberately NOT folded into `engaged` itself, which also drives the objective chip's
        // visibility and Town's world-input gate a few lines down: the player must still see the
        // chip and be able to WALK to the Shop during this exact window.
        Clock.Engaged = engaged || Day1CraftToShelvePacingHold;

        // Menu-sizing fix (U2, playtest F1): the objective chip floats over the SAME top-right
        // region a drawer/modal's own action buttons can occupy (e.g. it sat on top of the Forge
        // drawer's "Buy copper" row, and overlapped the Evening Ledger). Reusing this exact
        // "engaged" predicate — already the codebase's one definition of "a drawer/interior/modal
        // owns the screen" — hides the chip for every one of those cases with no new wiring.
        //
        // EXCEPT while the tutorial is running, when hiding it is the bug rather than the fix.
        // Brian's playtest: "The tutorial isn't updating despite entering the forge" — he opened the
        // Forge and the instruction card VANISHED, because this line hid it. An instruction you
        // cannot read while carrying it out is worse than no instruction: the player is left
        // guessing whether they already did the thing.
        //
        // Moved rather than left overlapping, which is what the original fix was right about: the
        // drawer owns the right ~600px, so the card docks to the LEFT edge instead of hiding. The
        // ordinary advisor chip still hides — it is background noise over a modal, and only the
        // tutorial's copy is load-bearing at that moment.
        //
        // DRAWER ONLY. A first pass keyed this on `engaged` and broke
        // HudBoundsTests.LedgerOpen_ObjectiveChip_NeverCoversLedger: an interior or a modal covers the middle
        // of the window, so there is no free column to move to and "keep it readable" just puts it back on
        // top of the thing the player is reading. Those surfaces carry their own copy anyway.
        var keepTutorialReadable = Tutorial.Active && Drawer.IsOpen && !modalOwnsTheScreen;
        Objective.Visible = !engaged || keepTutorialReadable;
        DockObjectiveHorizontally(toLeftEdge: keepTutorialReadable);

        // Same predicate for the journey dock. It was governed by PHASE alone, so during
        // Expedition/Camp/Deep it slid in over whatever the player had opened — a rendered playtest caught it
        // sitting on top of the Depths panel and obscuring the Gloomwood card. Redundant as well as
        // overlapping: the Depths panel is showing that same party in more detail.
        Pip.Suppressed = engaged;

        // T8: a drawer/interior/modal owns input while engaged — the 3D world's own click-to-
        // move/interact must not fight it for the same clicks underneath.
        Town.SetWorldInputEnabled(!engaged);

        // U18: the engaged latch flips on this discrete event (drawer open/close / modal
        // open-close), not only on a phase tick — the waiting indicator must track it here too,
        // still never per frame.
        Timeline.Refresh(Adapter.CurrentState.Phase, Waiting);
    }

    /// <summary>
    /// U15 (KTD3 escape hatch): tiny adapter-side settings store for the living clock's
    /// auto-advance preference — a JSON file at Godot's <c>user://</c>, entirely outside the
    /// sim save (KTD2 — the sim never reads or writes this). Whatever the player last chose
    /// via the Auto toggle survives to the next campaign load, so turning the clock manual
    /// sticks instead of silently reverting to the ON default. Fails soft everywhere (missing
    /// or corrupt file ⇒ null ⇒ callers fall back to <see cref="PhaseClock"/>'s own default)
    /// so a settings-store hiccup can never block boot.
    /// </summary>
    public static class ClockSettings
    {
        private const string Path = "user://clock_settings.json";

        /// <summary>Null when no settings file exists yet — callers keep PhaseClock's own
        /// default (OFF / player-decided, U2); otherwise the persisted auto-advance preference
        /// (opt-in "Innkeeper's Clock").</summary>
        public static bool? LoadAutoAdvance()
        {
            if (!Godot.FileAccess.FileExists(Path))
            {
                return null;
            }

            using var file = Godot.FileAccess.Open(Path, Godot.FileAccess.ModeFlags.Read);
            if (file is null)
            {
                return null; // unreadable — fail soft, never block boot
            }

            try
            {
                var data = System.Text.Json.JsonSerializer.Deserialize<Data>(file.GetAsText());
                return data?.AutoAdvance;
            }
            catch (System.Text.Json.JsonException)
            {
                return null; // corrupt file — fail soft
            }
        }

        public static void SaveAutoAdvance(bool autoAdvance)
        {
            using var file = Godot.FileAccess.Open(Path, Godot.FileAccess.ModeFlags.Write);
            file?.StoreString(System.Text.Json.JsonSerializer.Serialize(new Data { AutoAdvance = autoAdvance }));
        }

        /// <summary>Test-only teardown: delete the file so suites never leak a preference
        /// across runs (this store is adapter-side scaffolding, not sim state — safe to wipe).</summary>
        public static void DeleteForTests()
        {
            if (Godot.FileAccess.FileExists(Path))
            {
                Godot.DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(Path));
            }
        }

        private sealed class Data
        {
            // U2: manual-by-default everywhere — a settings blob missing this field (schema
            // evolution) must not silently resurrect timed mode. (A blob that already persisted
            // AutoAdvance:true keeps it — a one-time carry-over the player can toggle off.)
            public bool AutoAdvance { get; set; } = false;
        }
    }
}
