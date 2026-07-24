using System;
using System.Globalization;
using System.Linq;
using GameSim.Contracts;
using Godot;
using GodotClient.Panels;
using GodotClient.Town;
using GodotClient.Town3d;
using GodotClient.Ui;

namespace GodotClient;

/// <summary>
/// The one UI scene (U11 shell + U12 town layer, drawer-reworked U21): the living town view is a
/// PERMANENT full-rect base child — always visible, never hidden by a panel opening — with the six
/// management panels (Forge/Shop/Heroes/Tavern/Depths/Bounties) hosted one at a time in the
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
    private const float ObjectiveDockOffsetTop = 64f;

    /// <summary>Menu-sizing fix (review): the smallest on-screen gap the objective chip's clamp
    /// math will ever collapse OffsetTop/OffsetBottom down to on a very short viewport — keeps
    /// the chip a sliver rather than zero/negative height instead of tuning the normal-case
    /// docking above.</summary>
    private const float ObjectiveDockMinBottomGap = 40f;

    /// <summary>Menu-sizing fix (review): fixed floors for the header row's three HUD
    /// sections (stat chips / day timeline / Skip-Auto-Pause-Speed-Ledger controls) — named
    /// here rather than left as inline literals at each <c>CustomMinimumSize</c> call site,
    /// matching the ObjectiveDock consts above.</summary>
    private const float StatChipsMinWidth = 220f;

    private const float TimelineMinWidth = 280f;
    private const float HudControlsMinWidth = 420f;

    /// <summary>U23: the tutorial-flow overlay docks in the same top-right column, stacked below
    /// the objective chip rather than sharing its box (keeps the chip's own layout untouched).</summary>
    private const float TutorialDockOffsetTop = ObjectiveDockOffsetTop + 90f;

    /// <summary>PA8 (spec DB4): the <see cref="CameraRig.PushIn"/> distance for a station
    /// dolly-in — tighter than the town's default follow (<c>CameraRig.Distance</c> = 22) so the
    /// forge/counter focus overlay reads as a deliberate close-up, not a subtle zoom.</summary>
    private const float StationPushInDistance = 6f;

    /// <summary>3D-interiors MVP: the <see cref="CameraRig.PushIn"/> distance for a venue's
    /// <see cref="InteriorRoom3D"/> — slightly wider than <see cref="StationPushInDistance"/> so
    /// the whole 8-unit diorama room (floor + three walls) fits the rig's 45° FOV frame.</summary>
    private const float InteriorRoomPushInDistance = 6f;

    /// <summary>Interior camera pitch (degrees): shallower than the town's -42 top-down follow so
    /// the room is viewed nearer eye level and its walls/depth/props read as a 3D space rather than
    /// a flat floor plan (the "interiors look 2D" fix). Eased by <see cref="CameraRig.PushIn"/>.</summary>
    private const float InteriorRoomPitch = -15f;

    /// <summary>U23 (R5, KTD4): number-row hotkeys for the quick-travel unlock — runtime <see
    /// cref="InputMap"/> registration only (no <c>project.godot</c> contact), gated on <see
    /// cref="TutorialFlow.QuickTravelUnlocked"/> in <see cref="_Process"/>. Building keys match
    /// <see cref="Town3D.BuildingClicked"/>'s own payload vocabulary.</summary>
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
    public Town3D Town { get; private set; } = null!;
    public ForgePanel Forge { get; private set; } = null!;
    public ShopPanel Shop { get; private set; } = null!;
    public HeroesPanel Heroes { get; private set; } = null!;
    public TavernPanel Tavern { get; private set; } = null!;
    public DepthsPanel Depths { get; private set; } = null!;
    public BountyPanel Bounties { get; private set; } = null!;
    public LedgerModal Ledger { get; private set; } = null!;
    public CampPanel Camp { get; private set; } = null!;
    public TabFade TabFade { get; private set; } = null!;
    public AdventureTicker Ticker { get; private set; } = null!;

    /// <summary>U22 (R4/KTD10): the staged-interior framework — opens instead of the drawer on a
    /// venue interact/click-arrival, then routes a hotspot press onto the same drawer id. Since
    /// the 3D-interiors MVP it renders in see-through mode (hotspot/exit overlay only) over the
    /// real 3D room below whenever <see cref="InteriorRoom"/> mounted.</summary>
    public InteriorStage Interior { get; private set; } = null!;

    /// <summary>3D-interiors MVP: the live 3D interior room while a venue interior is open (null
    /// otherwise) — mounted in <see cref="Town3D.World"/> and framed by the shared
    /// <see cref="CameraRig"/> push-in; replaces <see cref="InteriorStage"/>'s painted backdrop,
    /// never its hotspot routing. Test/inspection surface.</summary>
    public InteriorRoom3D? InteriorRoom { get; private set; }

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
    private bool _resumePlayOnMirrorClose;

    /// <summary>U22: the door position the avatar stood at when the currently-open (or just-
    /// closed) interior was opened — restored on exit (R4/AE4 "exit returns avatar to door
    /// position"). Null while no interior has ever been opened this session.</summary>
    private Vector3? _interiorDoorPosition;

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
        Ledger.Bind(Adapter);
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
    private void RefreshHud()
    {
        RefreshStatus();
        var state = Adapter.CurrentState;
        Objective.Refresh(state, Tutorial.TopSlotText(state)); // U23: tutorial overrides the top slot only
        UpdateObjectiveDock(); // Refresh can change the reason line's line count — re-dock to it
        Tutorial.RefreshAffordances(state);
        Timeline.Refresh(state.Phase, Waiting);
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
    /// </summary>
    private void UpdateObjectiveDock()
    {
        var viewportHeight = GetViewportRect().Size.Y;
        Objective.OffsetTop = Mathf.Min(ObjectiveDockOffsetTop, viewportHeight - ObjectiveDockMinBottomGap);
        var maxBottom = Mathf.Max(Objective.OffsetTop + ObjectiveDockMinBottomGap, viewportHeight - ObjectiveDockMargin);
        var contentHeight = Objective.GetCombinedMinimumSize().Y;
        Objective.OffsetBottom = Mathf.Min(Objective.OffsetTop + contentHeight, maxBottom);
    }

    /// <summary>U18/U15: the day-timeline's engaged-wait indicator mirrors <see cref="
    /// UpdateClockLabel"/>'s own predicate — only worth flagging while the clock is actively
    /// running AND held at a boundary; a manual pause is a different, already-visible state.</summary>
    private bool Waiting => Clock.AutoAdvance && Clock.Playing && Clock.Engaged;

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
            // U15/AE1: engaged holds the boundary even while flowing — surface that distinctly
            // from a manual pause so it's legible that the wait is the player's own doing.
            var engaged = !Clock.Playing || !Clock.Engaged ? string.Empty : " [waiting]";
            _clockLabel.Text = $"next phase in {remaining}s @{Clock.SpeedMultiplier}x{paused}{engaged}";
            _playPause.Text = Clock.Playing ? "Pause" : "Play";
            _speed.Text = $"{Clock.SpeedMultiplier}x";
        }
        else
        {
            _clockLabel.Text = "next phase on Skip";
        }
    }

    private void BuildUi()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // --- U21: TownWorld is now a PERMANENT FullRect base child — added FIRST so every later
        // sibling (the HUD layout, the DrawerHost, the modals) draws on top of it, and it is never
        // hidden by a drawer opening/closing (R1 world permanence). T8: the grounded 3D town
        // replaces the 2D SubViewport shell — same permanence contract, same event vocabulary. ---
        Town = new Town3D { Name = "Town3D" };
        AddChild(Town);
        Town.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Town.Build(Adapter);
        Town.Clock = Clock;
        Town.HeroClicked += OnTownHeroClicked;
        Town.BuildingClicked += OnTownBuildingClicked;

        var layout = new VBoxContainer { Name = "Layout" };
        layout.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(layout);

        // --- HUD header (P007 U7/R11/R12/KD1): themed stat-chip row (left) + the
        // Skip/Auto controls cluster (right) — the real home for the living day
        // clock (U15). Both Skip and Auto drive PhaseClock's ONE advance path
        // (AdvanceNow / Update -> SimAdapter.AdvancePhase); nothing here is a second
        // code path (KD1). -----------------------------------------------------
        var header = new PanelContainer { Name = "HudHeader" };
        layout.AddChild(header);
        var headerRow = new HBoxContainer { Name = "HudHeaderRow" };
        header.AddChild(headerRow);

        // Menu-sizing fix (gate-b/HUD clip): StatChips and Timeline are the row's two
        // ExpandFill regions — with no cap, each one's OWN reported minimum size (the sum of
        // its live children's minimums) feeds straight into HudHeaderRow's total width demand,
        // and once that total exceeds the window width the row simply overflows to the right
        // (HBoxContainer never shrinks a child below its minimum) — pushing the rightmost
        // child, HudControls ("Skip"/Auto/Pause/etc.), off-screen. Wrapping each in a plain
        // (non-Container) Control with a fixed CustomMinimumSize + ClipContents=true bounds
        // its contribution to that total regardless of how many stat chips or phase labels it
        // ends up holding — a plain Control's own minimum size is just CustomMinimumSize, so it
        // does NOT propagate its children's combined minimum upward the way a Container would.
        var statChipsWrap = new Control
        {
            Name = "StatChipsWrap",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(StatChipsMinWidth, 0),
            ClipContents = true,
        };
        headerRow.AddChild(statChipsWrap);
        _statChips = new HBoxContainer { Name = "StatChips" };
        _statChips.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        statChipsWrap.AddChild(_statChips); // populated by RefreshStatus (day/phase/gold/heroes)

        // U18/KTD13: the day-timeline widget docks top-bar CENTER, between the stat chips
        // (left) and the Skip/Auto cluster (right) — populated/highlighted by RefreshHud.
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

        // HudControls is the row's rightmost, non-expand child — HBoxContainer already
        // reserves its natural minimum size before handing any leftover space to the two
        // ExpandFill wrappers above, but a fixed floor here keeps it from ever reporting
        // narrower than its real button cluster (Skip/Auto/Pause/Speed/Ledger) needs, which
        // is what actually keeps it fully on-screen once StatChips/Timeline are capped.
        var controls = new HBoxContainer
        {
            Name = "HudControls",
            CustomMinimumSize = new Vector2(HudControlsMinWidth, 0),
        };
        headerRow.AddChild(controls);

        _clockLabel = new Label { Name = "ClockLabel" };
        controls.AddChild(_clockLabel);

        // U15: the living clock flows by default, so the explicit control is now a
        // "Skip" — same underlying advance (AdvanceNow), just relabeled now that it is
        // the exception rather than the primary way forward (player intent always wins,
        // engaged or not). Node NAME stays "AdvancePhase" (existing tests press it by
        // name). The Auto toggle remains the escape hatch back to fully-manual mode.
        _advance = new Button { Name = "AdvancePhase", Text = "Skip" };
        StylePrimary(_advance);
        _advance.Pressed += () =>
        {
            Clock.AdvanceNow(); // same advance the auto timer fires — player intent wins even engaged
            UpdateClockLabel();
        };
        controls.AddChild(_advance);

        _auto = new Button { Name = "AutoAdvance", Text = "Auto: OFF", ToggleMode = true };
        _auto.Pressed += () =>
        {
            Clock.ToggleAuto();
            ClockSettings.SaveAutoAdvance(Clock.AutoAdvance); // U15 escape hatch: sticks across campaigns
            UpdateClockLabel();
            Timeline.Refresh(Adapter.CurrentState.Phase, Waiting); // U18: Auto gates the Waiting predicate too
        };
        controls.AddChild(_auto);

        _playPause = new Button { Name = "PlayPause", Text = "Pause" };
        _playPause.Pressed += () =>
        {
            Clock.TogglePlay();
            UpdateClockLabel();
            Timeline.Refresh(Adapter.CurrentState.Phase, Waiting); // U18: Playing gates the Waiting predicate too
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
            // PA8: release any station dolly-in on every full drawer close — a no-op ease when no
            // PushIn is active (CameraRig.Release's own contract), so this is safe to fire
            // unconditionally rather than tracking "was this station-opened" state here.
            Town.Camera.Release();
        };

        // --- ledger modal overlay (sibling after the drawer = draws on top) --
        Ledger = GD.Load<PackedScene>("res://scenes/panels/ledger_modal.tscn").Instantiate<LedgerModal>();
        AddChild(Ledger);
        Ledger.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Ledger.VisibilityChanged += OnLedgerVisibilityChanged;

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
        Tutorial.SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight, LayoutPresetMode.Minsize, (int)ObjectiveDockMargin);
        Tutorial.OffsetTop += TutorialDockOffsetTop;
        Tutorial.OffsetBottom += TutorialDockOffsetTop;
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
        //     draws above the drawer/HUD/every modal (in practice mutually exclusive with them —
        //     OpenInterior always closes the drawer first — but topmost is the safe default). ---
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
    }

    private static T InstantiatePanel<T>(string scenePath) where T : SimPanel =>
        GD.Load<PackedScene>(scenePath).Instantiate<T>();

    /// <summary>
    /// U21: the one entry point that opens a management surface — replaces the old
    /// <c>Tabs.CurrentTab = ...</c> routing. <paramref name="id"/> is one of "Forge" | "Shop" |
    /// "Heroes" | "Tavern" | "Depths" | "Bounties" | "Town" (the last one, and any drawer already
    /// open, both resolve through <see cref="DrawerHost.Close"/> — "Town" IS the bare-world state,
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
        }
        else
        {
            Drawer.Open(id);
            PanelFor(id).Refresh();
        }

        TabFade.Trigger();
        UpdateEngaged();
    }

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
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "no such drawer panel"),
    };

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

    /// <summary>Town hero click (R20): open the Heroes drawer with that hero's detail bound.</summary>
    private void OnTownHeroClicked(int heroValue)
    {
        OpenPanel("Heroes");
        Heroes.SelectHero(heroValue);
    }

    /// <summary>
    /// Town building click/interact (R20, U22, T8): stage the venue's interior instead of the
    /// drawer — the same <see cref="Town3D.BuildingClicked"/> payload every venue's click-arrival
    /// or E-interact already fires, just routed onto <see cref="InteriorStage.Venues"/> instead of
    /// straight onto <see cref="OpenPanel"/>. A hotspot pressed inside the interior (<see
    /// cref="OnInteriorHotspotActivated"/>) is what actually opens the matching drawer. The
    /// noticeboard (T5/T8) has no staged interior — its "Bounties" payload opens the Bounties
    /// drawer directly, the same one-step routing quick-travel and the interior's own board
    /// hotspot use.
    /// </summary>
    private void OnTownBuildingClicked(string building)
    {
        if (building == "Bounties")
        {
            OpenPanel("Bounties");
            return;
        }

        // PA8 (spec DB4/PKD8): the two active-professions stations open their focus surface
        // DIRECTLY (never through InteriorStage) with a CameraRig dolly-in — Town3D.Build already
        // added these as ordinary Building3D entries, so this same arrival-only payload (walk
        // then interact, KTD12 — never instant) already fired before this switch is reached; the
        // only new behavior is the push-in + which panel opens. Release() is hooked on
        // Drawer.Closed (BuildUi) so it fires regardless of how the panel closes (Esc, click-out,
        // or switching to another drawer).
        if (building == "ForgeStation")
        {
            Town.Camera.PushIn(Town.FindBuilding("forge-station"), StationPushInDistance);
            OpenPanel("Forge");
            return;
        }

        if (building == "CounterStation")
        {
            Town.Camera.PushIn(Town.FindBuilding("counter-station"), StationPushInDistance);
            OpenPanel("Shop");
            return;
        }

        var venueKey = building switch
        {
            "Forge" => "forge",
            "Shop" => "market",
            "Tavern" => "tavern",
            "Gate" => "minegate",
            _ => null,
        };

        if (venueKey is null)
        {
            OpenPanel("Town");
            return;
        }

        OpenInterior(venueKey);
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
    /// Open <paramref name="venueKey"/>'s staged interior (<see cref="InteriorStage.Venues"/>).
    /// The avatar is already standing at the venue's door — <see
    /// cref="Town3D.BuildingClicked"/> only ever fires on arrival/interact — so this records that
    /// exact position for <see cref="ResetAvatarToDoor"/> to restore on exit, closes whichever
    /// drawer was showing (REPLACE semantics, mirrors <see cref="OpenPanel"/>), mounts the venue's
    /// real 3D room (<see cref="MountInteriorRoom"/>, camera dolly included), and opens the stage
    /// in see-through mode over it so the hotspot overlay + its accumulated-delta push-in still
    /// run unchanged.
    /// </summary>
    private void OpenInterior(string venueKey)
    {
        Drawer.Close();
        _interiorDoorPosition = Town.DoorAnchor(venueKey);
        MountInteriorRoom(venueKey);
        Interior.Open(venueKey, Adapter.CurrentState, seeThrough: InteriorRoom is not null);
        UpdateEngaged();
    }

    /// <summary>
    /// 3D-interiors MVP: build <paramref name="venueKey"/>'s real 3D room, mount it on the
    /// <see cref="InteriorRoom3D.MountPosition"/> shelf inside the live town world, and dolly the
    /// shared camera onto it — the SAME <see cref="CameraRig.PushIn"/> path the forge/counter
    /// stations proved (<see cref="OnTownBuildingClicked"/>). The see-through
    /// <see cref="InteriorStage"/> overlay opened right after this keeps every hotspot action /
    /// exit / Esc / Engaged behavior unchanged on top of the room.
    /// </summary>
    private void MountInteriorRoom(string venueKey)
    {
        UnmountInteriorRoom();
        var room = new InteriorRoom3D { Position = InteriorRoom3D.MountPosition };
        room.Build(venueKey);
        Town.World.AddChild(room);
        Town.Camera.PushIn(room.Focus, InteriorRoomPushInDistance, InteriorRoomPitch);
        InteriorRoom = room;
    }

    /// <summary>Tear the 3D interior room down (no-op when none is mounted): release the camera
    /// back to its avatar follow and free the room — a fresh room is built per entry, so venue
    /// state can never leak between visits.</summary>
    private void UnmountInteriorRoom()
    {
        if (InteriorRoom is null)
        {
            return;
        }

        Town.Camera.Release();
        Town.World.RemoveChild(InteriorRoom);
        InteriorRoom.Free();
        InteriorRoom = null;
    }

    /// <summary>A content hotspot (never exit) was pressed inside the interior — close it (room
    /// down, camera released), restore the avatar to the door, and open the SAME drawer id the
    /// hotspot's action names (content parity with the pre-U22 interact-opens-the-drawer
    /// behaviour).</summary>
    private void OnInteriorHotspotActivated(string action)
    {
        UnmountInteriorRoom();
        ResetAvatarToDoor();
        OpenPanel(action);
    }

    /// <summary>The exit hotspot or Esc closed the interior — tear the 3D room down, restore the
    /// avatar to the door position it entered from (R4/AE4) and re-sync the Engaged latch.</summary>
    private void OnInteriorExited()
    {
        UnmountInteriorRoom();
        ResetAvatarToDoor();
        UpdateEngaged();
    }

    private void ResetAvatarToDoor()
    {
        if (_interiorDoorPosition is { } doorPosition)
        {
            Town.Player.GlobalPosition = doorPosition;
        }
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

        UpdateEngaged(); // the Ledger modal engages the latch too
        UpdateClockLabel();
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
    private bool Day1CraftToShelvePacingHold =>
        Adapter.CurrentState.Day == 1
        && Adapter.CurrentState.Phase == DayPhase.Morning
        && Adapter.PendingActions.Any(a => a is CraftAction)
        && !Adapter.PendingActions.Any(a => a is StockAction);

    /// <summary>
    /// U15/U21/U22 (KTD3/AE1/R7): real drawer/interior/modal state engages <see
    /// cref="PhaseClock.Engaged"/> — the bare world (no drawer open, no interior staged, no modal
    /// visible) is the only flowing surface; any open drawer (<see cref="DrawerHost.IsOpen"/>),
    /// staged interior (<see cref="InteriorStage.IsOpen"/>), or modal overlay (Ledger/Camp/Mirror)
    /// engages the latch so an expired phase timer holds at the boundary instead of ticking.
    /// </summary>
    private void UpdateEngaged()
    {
        var engaged = Drawer.IsOpen || Interior.IsOpen || Ledger.Visible || Camp.Visible || Mirror.Visible;

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
        Objective.Visible = !engaged;

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
        /// default (ON); otherwise the persisted auto-advance preference.</summary>
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
            public bool AutoAdvance { get; set; } = true;
        }
    }
}
