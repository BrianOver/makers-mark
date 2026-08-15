using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using GameSim;
using GameSim.Contracts;
using GameSim.Factions;
using GameSim.Presentation;
using GameSim.Venues;
using Godot;
using GodotClient.Audio;
using GodotClient.Panels;
using GodotClient.Tools;
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

    /// <summary>
    /// U2 (shell-and-audio plan, R2/KTD-C): the header's height BUDGET, shared with
    /// <c>HudBoundsTests</c> (<c>godot/tests/HudBoundsTests.cs</c>) so the test and the layout it
    /// polices can never silently drift apart. StatRowHeight/row-2 sizing above are the KNOBS
    /// that must stay under this budget, never the other way around.
    ///
    /// <para><b>Measured, not the plan's proposed 100px.</b> The plan proposed "≤100px at
    /// 1152×648, measured before finalizing" as an open question — a real headless+windowed
    /// probe (<c>GetCombinedMinimumSize().Y</c> after the first tick mounts the full stat-chip
    /// row) measured the CURRENT two-row header at 163px at that same window size, independent of
    /// width or height (a PanelContainer's natural minimum is a function of its children, not the
    /// window). Getting under 100px means either dropping a row or shrinking the Books
    /// Tray/verb cluster measurably — a real visual redesign (moving the tray behind one menu
    /// button, per Open Question 1), not a knob turn, and explicitly out of this structural
    /// unit's scope (see the plan's Scope Boundaries: "Header visual redesign beyond the
    /// budget... taste passes wait for his verdict"). 175px is the real measurement plus ~7%
    /// headroom for font-metric variance — a regression pin on the CURRENT size, not a target to
    /// shrink toward yet.</para>
    /// </summary>
    public const float HeaderBudgetPx = 175f;

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

    /// <summary>
    /// U4 (shell-and-audio plan): scene-change hook for "Save & quit to title" — null = real
    /// <c>GetTree().ChangeSceneToFile</c>. Tests stub this so pressing the button never tears down
    /// the test scene tree (the exact seam <see cref="NewGameSelect.SceneChange"/> already
    /// established for its own Continue/Begin presses).
    /// </summary>
    public Action<string>? SceneChange { get; set; }

    /// <summary>
    /// U4 (KTD-D): test seam for "a real quit was about to happen." Null in production, where
    /// <see cref="SaveAndQuit"/> calls the real <see cref="SceneTree.Quit()"/> — which a test must
    /// never do, since that would tear down the whole test process rather than just this scene.
    /// Non-null in a test observes that the save-then-quit ROUTING fired without actually quitting.
    /// </summary>
    public Action? QuitOverride { get; set; }

    /// <summary>Test-observable count of DISTINCT rejection warnings actually pushed to the dev
    /// log (see the dedup guard in <see cref="OnPhaseCompleted"/>). <see cref="SimAdapter.LastRejections"/>
    /// is deliberately phase-cumulative, so a naive re-warn of the whole list on every immediate
    /// action re-logs already-reported refusals — measured on a real playtest run, that turned 83
    /// genuine rejections into 368 duplicate warnings. This counts what actually got pushed, so a
    /// test can pin the fix rather than trust the console.</summary>
    public int RejectionWarningsEmitted { get; private set; }

    /// <summary>How many entries of <see cref="Adapter"/>'s cumulative <c>LastRejections</c> have
    /// already been warned about this phase — see <see cref="OnPhaseCompleted"/>.</summary>
    private int _rejectionsWarned;

    /// <summary>
    /// The playtest-log "cause" for whichever tick fires next — set immediately before each of the
    /// few calls that can actually trigger <see cref="SimAdapter.AdvancePhase"/> (a button press, or
    /// <see cref="RaidConductor"/>/<see cref="PhaseClock"/>'s own per-frame auto-drivers in <see
    /// cref="_Process"/>), cleared right after. <see cref="OnPhaseCompleted"/> reads it into <see
    /// cref="PlaytestLog.Tick"/> the instant a tick actually happens — see that method's own doc for
    /// why an unattributed real transition is exactly the bug this exists to catch. Left set across a
    /// whole <see cref="RaidConductor.Hurry"/> call on purpose: Hurry can chain several ticks in one
    /// press, and every one of them really was that press, not a mystery.
    /// </summary>
    private string _pendingTickCause = "";

    public SimAdapter Adapter { get; private set; } = null!;
    public PhaseClock Clock { get; private set; } = null!;

    /// <summary>U1 (plan 2026-08-03-001, KTD-A "the two-bell day"): sequences the raid span
    /// (Expedition/Camp/ExpeditionDeep) as a show instead of three player-cranked bells. Constructed
    /// right after <see cref="Clock"/>, BEFORE <c>Adapter.StateChanged += OnPhaseCompleted</c> below —
    /// its own constructor subscribes to the same event, so this ordering guarantees its beat is
    /// already resynced by the time <see cref="OnPhaseCompleted"/> (and the <see
    /// cref="UpdateClockLabel"/> it triggers) reads <see cref="Conductor"/> on any given tick.</summary>
    public RaidConductor Conductor { get; private set; } = null!;

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

    /// <summary>U18 (R11/KTD13): the top-right objective chip — <c>ObjectiveAdvisor</c>'s top
    /// pick + reason, expandable to the ranked list.</summary>
    public ObjectiveTracker Objective { get; private set; } = null!;

    /// <summary>U23 (R5/R10/R13): the first-run tutorial chain + earn-2nd-profession affordance +
    /// quick-travel unlock — see <see cref="TutorialFlow"/>'s own class doc.</summary>
    public TutorialFlow Tutorial { get; private set; } = null!;

    /// <summary>U5 (loop-legibility plan): the tutorial's pointing overlay — see <see
    /// cref="TutorialOverlay"/>'s own class doc.</summary>
    public TutorialOverlay Overlay { get; private set; } = null!;

    /// <summary>U18 (R12/KTD13): the top-bar-center day-timeline widget — live phase highlight
    /// + the U15 engaged-wait indicator.</summary>
    public DayTimeline Timeline { get; private set; } = null!;

    /// <summary>U16 (KTD11/KTD13): the expanded scrying-mirror modal.</summary>
    public ScryingMirror Mirror { get; private set; } = null!;

    /// <summary>U16 (KTD13): the bottom-right PiP journey dock.</summary>
    public PipDock Pip { get; private set; } = null!;

    /// <summary>
    /// U9 (world-and-interiors plan, KTD-4): the ONE live <see cref="MineWatch"/> instance —
    /// constructed once here, refreshed every tick regardless of host (see <see cref="RefreshAll"/>),
    /// and borrowed by <see cref="Depths"/> (its resting host) or <see cref="Mirror"/> (while open)
    /// via each panel's own <c>MountWatch</c>. Never constructed a second time and never a child of
    /// two panels at once — constraint 4's "exactly one live SubViewport, ever".
    /// </summary>
    public MineWatch Watch { get; private set; } = null!;

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

    /// <summary>U2 (shell-and-audio plan): test-observable handle on the header panel — lets
    /// <c>HudBoundsTests</c> assert its rect never intersects <see cref="Town"/>'s and that its
    /// measured height stays inside <see cref="HeaderBudgetPx"/>, without exposing the private
    /// field itself.</summary>
    public PanelContainer HudHeader => _hudHeader;
    private int _pendingLedgerDay;

    /// <summary>Which moment, if any, earned the narrator on the night now waiting to be revealed.
    /// Chosen when the Evening tick resolves and spoken when the Ledger opens — see the capture site
    /// for why it cannot be decided at reveal time.</summary>
    private NarratorVoiceDirector.Trigger? _pendingLedgerVoice;

    /// <summary>Heroes lost on the night <see cref="_pendingLedgerVoice"/> was chosen for, so the
    /// epitaph cannot claim a count the ledger contradicts. Captured with the trigger, at the tick
    /// that resolved the night — see the assignment site for why it cannot be read at reveal time.</summary>
    private int _pendingLedgerLosses;
    private HBoxContainer _statChips = null!;
    private Label _clockLabel = null!;
    private PanelContainer _toastBanner = null!;
    private Label _toast = null!;
    private Button _advance = null!;
    private Button _auto = null!;
    private Button _playPause = null!;
    private Button _speed = null!;
    private Button _watch = null!;
    private Button _fullscreen = null!;

    /// <summary>U3 (loop-legibility plan, KTD-B): the bell tray — one chip per
    /// <see cref="SimAdapter.PendingActions"/> entry, rebuilt (never mutated) by
    /// <see cref="RefreshBellTray"/> so it can never drift from the real queue.</summary>
    private HBoxContainer _bellTray = null!;
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
    /// <summary>U4 (shell-and-audio plan): mirror of the Bestiary/Forecast latch for the in-game
    /// system menu — pause while it owns the screen, resume on close when play was running.</summary>
    private bool _resumePlayOnSystemMenuClose;

    /// <summary>
    /// U4: the in-game system menu (Esc's new bottom rung when nothing else owns the screen, and
    /// its own top rung when it is the thing that's open — see <see cref="_Input"/>). Full-rect
    /// dim + centered wood card, the same shape <see cref="NewGameSelect"/>'s title screen uses, so
    /// pausing reads as "the same kind of screen" rather than a sixth different modal shape.
    /// </summary>
    private Control _systemMenu = null!;

    /// <summary>The system menu's own button list (Resume/Settings/Save &amp; quit/Quit) — hidden
    /// while its nested <see cref="_systemMenuSettings"/> sub-view is showing, exactly the
    /// picker/primer toggle <see cref="NewGameSelect"/> already uses.</summary>
    private VBoxContainer _systemMenuList = null!;

    /// <summary>The system menu's "Settings" sub-view — a SECOND instance of the SAME <see
    /// cref="SettingsPanel"/> class the title screen mounts, never a live-shared Control across
    /// the two scenes (see that class's own doc).</summary>
    private SettingsPanel _systemMenuSettings = null!;

    /// <summary>
    /// U4 (KTD-D): title-screen scene path the system menu's "Save &amp; quit to title" returns
    /// to. Hardcoded here the same way <see cref="NewGameSelect.MainScenePath"/> and every existing
    /// caller of <c>new_game_select.tscn</c> already hardcode their own direction's path — no
    /// shared constant exists yet for either direction.
    /// </summary>
    private const string TitleScenePath = "res://scenes/new_game_select.tscn";

    /// <summary>
    /// U1 (playtest-three plan, KTD-A move 2): armed by <see cref="SoundTheTick"/> when the
    /// departure tick lands while a genuine modal (Ledger/Camp/Mirror/Forecast/Bestiary/
    /// Commissions/Legends, or the walkable interior room) still owns the screen — the drawer is ALWAYS
    /// closed on departure (move 1), but a modal is a deliberate player choice mid-Morning and
    /// yanking the camera behind it would still be invisible, one layer deeper than the reported
    /// bug. Cleared by <see cref="TryFireDeferredMineGateFocus"/>, called from every modal-close
    /// path, the moment nothing is left covering the town.
    ///
    /// <para>U10 (world-and-interiors plan): also armed by <see cref="OnPartyEmerging"/> for the
    /// RETURN half of the same beat — one flag, one deferral rule, regardless of which direction
    /// the party is walking; a modal open at the exact moment either beat wants the camera defers
    /// it identically.</para>
    /// </summary>
    private bool _pendingMineGateFocus;

    // ── LW3: gold-chip bounce-scale pop (StatusBar region) ────────────────────────────────────
    // No engine Tween in this codebase (accumulated-delta math only, so the pop is deterministic
    // and headless-testable via direct _Process calls). -1 = not popping.
    private const double GoldPopSeconds = 0.3;
    private Label? _goldValueLabel;
    private double _goldPopElapsed = -1;

    /// <summary>
    /// The fallback campaign when nothing set <see cref="AdapterOverride"/> — real play always
    /// goes through <c>NewGameSelect</c> (which always sets the override), so this path is a
    /// direct scene launch (a test, or a tool). U7 (world-and-interiors plan) receipt seam:
    /// <c>SHOT_PROFESSION</c> lets <c>tools/receipt.ps1</c>/<c>shot_harness.gd</c> capture a
    /// non-blacksmith start without a real profession pick — set the env var on the process
    /// launching Godot (see <c>shot_harness.gd</c>'s own header), never read anywhere else.
    /// Absent/empty (every normal launch) keeps the pre-U7 seed-only campaign byte-identical.
    /// </summary>
    private SimAdapter BuildDefaultAdapter()
    {
        var professionOverride = System.Environment.GetEnvironmentVariable("SHOT_PROFESSION");
        if (string.IsNullOrEmpty(professionOverride))
        {
            return new SimAdapter((ulong)Seed);
        }

        var state = GameComposition.NewCampaign((ulong)Seed, professionOverride);

        // U7 receipt seam ONLY: a second profession, unioned directly (bypassing the real
        // SetProfessionsAction/day-boundary path a player actually takes — legitimate for a
        // receipt capture, never for real play) so a dual-profession workshop can be rendered
        // without scripting a multi-day drive just to take a screenshot.
        var secondProfession = System.Environment.GetEnvironmentVariable("SHOT_PROFESSION2");
        if (!string.IsNullOrEmpty(secondProfession))
        {
            state = state with { Player = state.Player with { SelectedProfessions = state.Player.SelectedProfessions.Add(secondProfession) } };
        }

        return new SimAdapter(state);
    }

    public override void _Ready()
    {
        Adapter = AdapterOverride ?? BuildDefaultAdapter();
        AdapterOverride = null; // consumed — the handoff is one-shot (see property doc)
        Clock = new PhaseClock(Adapter);

        // U1 (KTD-A): constructed here — BEFORE Adapter.StateChanged += OnPhaseCompleted below — so
        // its own StateChanged subscription (registered in its constructor) runs FIRST on every tick
        // and Current is already resynced by the time OnPhaseCompleted reads it. The delegates close
        // over the Town/Watch PROPERTIES rather than a captured value, so it is safe to construct
        // this before BuildUi() has actually built Town — nothing below reads them until a real
        // frame's Conductor.Update, long after _Ready has returned.
        Conductor = new RaidConductor(Adapter, Clock,
            departureShowDone: () => !Town.AnyDeparturePending,
            homecomingShowDone: () => !Town.AnyReturnPending,
            // The one on-screen ask that lives INSIDE the raid span and nowhere else: the
            // apprenticeship chain's Watch step. It is printed on the Expedition->Camp tick (the
            // PartyDeparted one) and its only affordance — the Watch control below — exists only
            // while a party is out, so before this hold the player had the two EmptyBeatSeconds
            // between those two facts to answer it. Measured: 2.00 seconds. See RaidConductor's own
            // class doc for the owner report this closes. Null-tolerant because Tutorial is built in
            // BuildUi(), after this line runs (nothing calls the delegate until a real frame).
            showHeld: () => Tutorial is { Active: true, Step: Ui.TutorialStep.LookIn });

        // So PlaytestLog rows can report the current beat without PlaytestLog (or SimAdapter, which
        // has no idea RaidConductor exists) taking a hard dependency on this class — see
        // PlaytestLog.BeatProvider's own doc. Set unconditionally: cheap when the log is inactive,
        // and idempotent if a future test mounts a second MainUi in the same process.
        PlaytestLog.BeatProvider = () => Conductor.Current.ToString();

        RegisterQuickTravelActions(); // U23 (KTD4): runtime InputMap only, zero project.godot contact

        // U4 (KTD-D): intercept the OS close request (the window's own X / Alt+F4) so it saves
        // BEFORE quitting instead of the engine tearing the process down out from under an
        // unsaved day. See _Notification/SaveAndQuit. A runtime SceneTree property, never
        // project.godot (deny-listed) — and reset back to true the instant this MainUi hands off
        // to the title screen (SaveAndReturnToTitle), so a window left open on THAT scene still
        // closes the ordinary way with nothing left listening for the notification.
        GetTree().AutoAcceptQuit = false;

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
        // U3 (KTD-B): the ONE subscription that answers every deferred submit, regardless of
        // which panel called Adapter.Queue — see SimAdapter.ActionQueued's own doc for why this
        // beats per-panel wiring.
        Adapter.ActionQueued += OnActionQueued;
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
        // U1 (KTD-A): the vigil's third verb is the only way RaidConductor.Beat.VigilStop ever ends.
        // Wrapped (rather than += Conductor.ResolveVigil directly) so the resulting Camp -> Deep tick
        // carries an honest cause in the playtest log instead of reading as an unattributed auto-tick.
        Camp.SendDeeperRequested += () =>
        {
            _pendingTickCause = "press:SendDeeper";
            Conductor.ResolveVigil();
            _pendingTickCause = "";
        };
        // Hero-facing-day H1 (§3.3 V-2): "Forge something for them" closes the slate (the vigil
        // stays armed — SyncCampModal reopens it the instant a real action lands) and jumps
        // straight to the forge, so the discoverable verb is one click, not "go find it yourself".
        Camp.OpenForgeRequested += () => OpenPanel("Forge");
        Watch.Refresh(Adapter.CurrentState, Adapter.LastEvents); // U9: not a SimPanel — no Bind() auto-refresh
        Mirror.Bind(Adapter);
        Pip.Refresh(Adapter.CurrentState, Adapter.LastEvents); // not a SimPanel — no Bind() auto-refresh

        RefreshHud();
        UpdateClockLabel();
        SyncCampModal(); // adopt an injected mid-day (parked) campaign — open the slate if already at Camp
        GD.Print($"[MainUi] campaign started, seed {Seed}");
        MaybeScreenshotAndQuit();
    }

    /// <summary>
    /// U4 (KTD-D): the OS window's own close request (X button / Alt+F4) — save first, then quit,
    /// same as the system menu's own "Quit game" (<see cref="SaveAndQuit"/>). Requires
    /// <c>AutoAcceptQuit = false</c> (set in <see cref="_Ready"/>) or the engine would already have
    /// torn the process down before this ever ran.
    /// </summary>
    /// <param name="what">
    /// Also drains <see cref="PanelGraveyard"/> on both tree transitions. Every panel rebuild detaches
    /// its old subtree and <c>QueueFree</c>s it (<see cref="SimPanel.Clear"/> — it must, or it would
    /// free a button mid-emission), which self-cleans in the running game because a frame always
    /// arrives, but strands the whole subtree in an engine test that never yields one: the node is
    /// parentless, so <c>Unmount</c>'s <c>ui.Free()</c> cannot reach it either. Mount and unmount are
    /// the two moments where no panel signal can possibly be in flight, so they are the only safe
    /// places to destroy the stragglers by hand. Draining on ENTER as well as EXIT means a test that
    /// mounts without unmounting cannot bequeath its residue to the next one.
    /// </param>
    public override void _Notification(int what)
    {
        base._Notification(what);
        switch (what)
        {
            case (int)NotificationWMCloseRequest:
                SaveAndQuit();
                break;
            case (int)NotificationEnterTree:
            case (int)NotificationExitTree:
                PanelGraveyard.Drain();
                break;
        }
    }

    /// <summary>
    /// Destroy every detached panel subtree NOW — the test host's stand-in for the frame that
    /// flushes the deletion queue in the running game.
    ///
    /// <para><b>Why a test needs this at all.</b> The mount/unmount drains above bound the RESIDUE a
    /// test leaves behind, but not the PEAK it holds while running: a frameless long-lived mount
    /// (<c>Playtest3dClickThrough</c> — 40 in-game days of real button presses, every landed click
    /// refreshing every panel) buried ~375,000 nodes between mount and unmount, ~1.3 GB of Godot RSS,
    /// and under that pressure the SHARED gdUnit runtime dies mid-session (stall or 0xC0000005),
    /// truncating whatever tests happened to come later. Draining between phase ticks bounds the peak
    /// to one tick's rebuilds.</para>
    ///
    /// <para><b>Safety contract — same as <see cref="_Notification"/>'s drains:</b> call ONLY where
    /// no panel signal can be in flight. A test's own loop between two ticks qualifies (every
    /// <c>EmitSignal</c> has returned, <c>SimAdapter.AdvancePhase</c> has unwound); anything
    /// reachable from inside a <c>Pressed</c> handler does NOT — an immediate action's refresh buries
    /// the emitting button itself, and freeing it there is the signal-11 crash
    /// <c>ClearDuringSignalTests</c> pins. That is also why this is a hand-invoked ForTests seam and
    /// not another automatic hook: no production call site below ENTER/EXIT_TREE can prove the
    /// stack is emission-free. The running game never needs it — a frame always arrives.</para>
    /// </summary>
    public static void DrainDetachedPanelsForTests() => PanelGraveyard.Drain();

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

    /// <summary>
    /// Dev/receipt tool only (never called from real play) — queues the same day-1 tutorial
    /// ladder <c>TutorialFlowTests.DriveDay1ToLookIn</c> drives for real in the engine suite
    /// (buy material -&gt; craft -&gt; shelve -&gt; post a bounty, all four immediate lane per
    /// U1), reachable via <c>godot/tools/shot_harness.gd</c>'s source-gen <c>call()</c> bridge
    /// (the same bridge <c>OnTownBuildingClicked</c>/<c>ShowMirror</c> already use for other
    /// receipt states) so <c>SHOT_STATE=TutorialLookIn</c> can reach <see
    /// cref="Ui.TutorialStep.LookIn"/> deterministically after one more real bell press,
    /// without a GDScript caller needing to construct C# <see cref="PlayerAction"/> records.
    /// </summary>
    public void Dev_QueueDay1TutorialLadder()
    {
        var craftedItemId = new ItemId(Adapter.CurrentState.NextItemId);
        Adapter.Queue(new BuyMaterialAction("copper", 2));
        Adapter.Queue(new CraftAction("dagger", "copper"));
        Adapter.Queue(new StockAction(craftedItemId, 50));
        Adapter.Queue(new PostBountyAction(5, 10));
    }

    public override void _Process(double delta)
    {
        if (Clock is null)
        {
            return;
        }

        // U1 (KTD-A): Clock's own opt-in auto-advance timer and Conductor are MUTUALLY EXCLUSIVE
        // within one frame — never both, an if/else, not two unconditional calls in a row. Both
        // ultimately ride the same PhaseClock.AdvanceNow path, and PhaseClock's own contract is "at
        // most one advance per call — a huge delta can never skip phases silently" (see its Update
        // doc). Calling both here would violate that the instant a single large delta straddled BOTH
        // thresholds at once: Clock.Update ticks Morning -> Expedition, RaidConductor.Resync (fired
        // synchronously off that SAME tick) flips Current from Idle to SendOff, and — if the second
        // call still ran with the same oversized delta — Conductor.Update would immediately consume
        // ITS OWN threshold too, landing two phases forward (Expedition straight through to Camp) in
        // one frame. Caught by two engine tests asserting a large auto-advance delta lands EXACTLY
        // at Expedition (DayAdvanceHudTests/MainUiTests): both failed with "'Expedition' but is
        // 'Camp'" until this became an if/else. Checking Current ONCE, before either call, is what
        // makes it exclusive — Conductor.Update only ever runs in the SAME frame Clock.Update did NOT.
        // Named BEFORE either call, cleared right after — whichever one actually ticks the phase
        // (most frames, neither does) picks up the correct unattended cause with no further wiring.
        // See _pendingTickCause's own doc for why this is the one deliberate exception that stays
        // set across a whole multi-tick call (Hurry, not reachable from here) rather than one tick.
        if (Conductor.Current == RaidConductor.Beat.Idle)
        {
            _pendingTickCause = "auto:innkeepers-clock";
            Clock.Update(delta);
        }
        else
        {
            // Independent of Clock.AutoAdvance — the raid span auto-plays regardless of whether the
            // player opted into the (Morning/Evening-only) Innkeeper's Clock. A no-op whenever
            // Conductor.Current is VigilStop (see its own Update doc).
            _pendingTickCause = "auto:conductor-beat-elapsed";
            Conductor.Update(delta);
        }

        _pendingTickCause = "";

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
                // U7 (loop-legibility plan, R10): the ledger's own one-line tutorial explainer,
                // wired ONLY to the automatic Return-Ritual reveal — ConsumeLedgerTip returns
                // non-null exactly once per campaign, so a manual reopen (OpenLedger tray button,
                // OnLedgerVisibilityChanged's forecast chain) never re-asks for it.
                Ledger.ShowFor(_pendingLedgerDay, Tutorial.ConsumeLedgerTip());

                // Only the automatic Return-Ritual reveal speaks. Reopening the ledger from the tray
                // re-reads the same night, and a narrator that recites on every re-read is the
                // repetition that kills the whole feature.
                if (_pendingLedgerVoice is { } trigger)
                {
                    // Rng.Inc is the campaign's identity everywhere flavor is picked (GossipSystem
                    // passes exactly this as campaignId) — one notion of "which campaign", not two.
                    Audio?.SpeakNarrator(
                        trigger, Adapter.CurrentState.Rng.Inc, (ulong)_pendingLedgerDay,
                        Math.Max(1, _pendingLedgerLosses));

                    // U-audio-3: a hero's death gets its own quiet toll, distinct from the ordinary
                    // day's Bell — see DeathNoticeCueFor's own doc. Fires alongside the narrator
                    // line, at the same once-per-reveal moment, never on its own separate gate.
                    if (DeathNoticeCueFor(trigger) is { } cue)
                    {
                        Audio?.Play(cue);
                    }

                    _pendingLedgerVoice = null;
                }
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

        // U17: tick the bottom-edge adventure ticker marquee (no-op with no lines yet).
        Ticker.Tick(delta);

        // Tick the ending chronicle's staged line reveal (no-op unless the scroll is open).
        Chronicle.Tick(delta);

        // UI-4: tick the day-timeline's pulsing engaged-wait dot (no-op unless it's visible).
        Timeline.Tick(delta);

        // UI-6: tick the objective note's body fade-in (no-op unless a fresh step just landed).
        Objective.Tick(delta);

        // U5 (loop-legibility plan): tick the tutorial's pointing pulse/outline (no-op with
        // nothing anchored — i.e. whenever the tutorial is inactive).
        Overlay.Tick(delta);

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

    /// <summary>
    /// Ticks the phase with an explicit playtest-log cause attached — the seam
    /// <c>AgentPlaytestBridge</c>'s <c>advance</c> verb uses (<c>AgentPlaytest.cs</c>'s own doc: there
    /// is no in-game button for it, since real time passage is <see cref="PhaseClock"/>'s own
    /// wall-clock timer, so the bridge calls <see cref="SimAdapter.AdvancePhase"/> directly). Without
    /// this, every automated-harness "advance" turn would tick the phase with <see
    /// cref="_pendingTickCause"/> still empty — reading in the log exactly like the unattributed
    /// auto-tick this whole trail exists to catch, even though a real driver genuinely asked for it.
    /// </summary>
    public void AdvancePhaseWithCause(string cause)
    {
        _pendingTickCause = cause;
        Adapter.AdvancePhase();
        _pendingTickCause = "";
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
        PlaytestLog.Tick(completedPhase, completedDay, state, Adapter.LastRejections, Adapter.LastEvents, _pendingTickCause);

        // Sound follows the same signal the HUD does, so a cue can never disagree with what is on
        // screen. Phase-keyed rather than event-keyed for the bed: SetPhase ignores an unchanged
        // phase, so calling it on every tick is correct and needs no boundary detection here.
        Audio.SetPhase(state.Phase);
        SoundTheTick(completedPhase, state);

        // Adapter.LastRejections ACCUMULATES for the whole phase (SimAdapter's own doc: every
        // immediately-resolved action's refusal is appended, and OnPhaseCompleted fires again on
        // EVERY such action — see SimAdapter.Queue). Warning about the WHOLE list each time
        // re-logs rejections already reported on an earlier call in this same phase. Only the NEW
        // tail is genuinely new news; a list that SHRANK means SimAdapter cleared its accumulator
        // (AdvancePhase truly completed a phase), so start counting fresh from there.
        var rejections = Adapter.LastRejections;
        if (rejections.Count < _rejectionsWarned)
        {
            _rejectionsWarned = 0;
        }

        for (var i = _rejectionsWarned; i < rejections.Count; i++)
        {
            var rejected = rejections[i];
            // Dev log keeps the RAW kernel reason (org logging rule); the player only
            // ever sees the friendly toast below.
            EngineDistress.Warn($"[MainUi] rejected {rejected.Action.GetType().Name}: {rejected.Reason}");
            RejectionWarningsEmitted++;
        }

        _rejectionsWarned = rejections.Count;

        // Milestones speak OUTSIDE the rejection branch below. A player who mistyped an action on
        // the same tick their campaign reached its climax should still hear the climax — the toast
        // strip has to choose one line, but the voice is a different channel and does not.
        SpeakMilestones(Adapter.LastEvents, state);

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

        // U23 (fix): reads GameState.EventLog durably now, not just this tick's LastEvents — see
        // TutorialFlow.Advance's own doc for why a per-tick-only read could dead-end the chain.
        Tutorial.Advance(state);
        RefreshAll();

        // Only run the town's phase choreography when a phase ACTUALLY completed.
        //
        // `StateChanged` has two callers. A real tick advances the phase, so `completedPhase` differs
        // from the post-event `state.Phase`. `SimAdapter.Queue`'s immediate-action branch (buy, craft,
        // stock, reprice — the 2026-07-30 fix) also raises it, with the CURRENT, un-advanced phase,
        // because nothing completed. The audio path below already tells those apart; this call did not,
        // so every immediate action replayed the choreography for whatever phase happened to be current.
        //
        // Owner's playtest, two complaints with one cause: "hitting stock keeps sending the heroes out"
        // (Stock in Morning re-ran DepartWanderingHeroes) and "why did the heroes come back to the town
        // visually?" (any immediate action during Expedition/ExpeditionDeep re-ran ReturnSurvivors,
        // marching the party home mid-raid and contradicting the fiction).
        if (completedPhase != state.Phase)
        {
            Town.OnPhaseCompleted(completedPhase);
        }
        // U25 (c): the drawer's own ShopPanel.OnPhaseCompleted (LW3's lit customer strip) is
        // retired. U4 (painted-interiors plan): its replacement, InteriorStage's embedded
        // ShopStage, was ALSO retired along with the dead InteriorStage host it rode in on.
        // U5 (world-and-interiors plan): ShopStage itself is now deleted — its choreography
        // landed in Town2D.MarketLife2D, hosted by the market room and fed every tick from
        // Town2D.Refresh() (called below via RefreshAll), not from this MainUi tick hook.
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

            // The narrator decides here, not at reveal time: LastEvents is THIS tick's, and by the
            // time the Return-Ritual delay elapses it may have moved on. Which moment earned a voice
            // is a fact about the night that just resolved, so it is captured with the day it belongs
            // to. Null on a quiet night, which is most nights — silence is the default posture.
            _pendingLedgerVoice = NarratorVoiceDirector.SelectForNight(Adapter.LastEvents);

            // How many the night actually took, captured HERE for the same reason the trigger is —
            // it is a fact about the night that resolved, and LastEvents will have moved on by the
            // time the Return-Ritual delay elapses. The owner lost two heroes on 2026-08-14 and heard
            // "One did not come back"; the selector had no way to know it was speaking over two,
            // because nobody had ever counted them. This is that count.
            _pendingLedgerLosses = 0;
            foreach (var e in Adapter.LastEvents)
            {
                if (e is HeroDied)
                {
                    _pendingLedgerLosses++;
                }
            }

            // The reveal fires from _Process when the gate elapses; the Ledger's
            // visibility handler pauses the clock at that point.
        }
    }

    /// <summary>
    /// U3 (loop-legibility plan, KTD-B): the ONE place a deferred submission gets acknowledged,
    /// no matter which panel called <see cref="SimAdapter.Queue"/> — see
    /// <see cref="SimAdapter.ActionQueued"/>'s own doc for why a single subscription replaces
    /// per-panel toast wiring. Immediate actions never raise this event at all (their answer is
    /// the state change itself, plus whatever <see cref="OnPhaseCompleted"/> already surfaces);
    /// a deferred one gets an instant toast naming the bell's promise, and the tray chip.
    /// </summary>
    private void OnActionQueued(PlayerAction action)
    {
        ShowBellToast(PendingVerbVocab.BellPromise(action));
        RefreshBellTray();
    }

    /// <summary>
    /// Re-render the status bar, the permanent world, and every currently-visible surface from
    /// CurrentState. U21: VISIBILITY-GATED — a load-bearing perf change now that the world always
    /// renders. The five drawer panels NOT currently open never get a Refresh() call here; opening
    /// one via <see cref="OpenPanel"/> refreshes it on the spot, so nothing a player actually looks
    /// at is ever stale. Ledger/Camp/Mirror/Pip are unaffected — they were never tab-gated before
    /// U21 (LedgerModal/CampPanel stay FullRect overlays above the drawer) and stay unconditional.
    /// U9 (KTD-4): <see cref="Watch"/> joins that unconditional set — it is a single shared
    /// instance borrowed by whichever of Depths/Mirror is open, so it must stay fresh regardless
    /// of which one that currently is (or neither, between raids).
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
        Watch.Refresh(Adapter.CurrentState, Adapter.LastEvents); // U9: refreshed regardless of host
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
            _spokeVigilForDay = 0; // the party surfaced; the next parking is a new moment
        }
        else if (state.Phase == DayPhase.Camp)
        {
            Camp.ShowModal();

            // The held breath at the winch-house — the other moment the whole day is built to stage.
            // Once per parking, not once per sync: this runs on every phase tick while a party is
            // camped, and a narrator that re-announces the pause every tick is unbearable.
            if (_spokeVigilForDay != state.Day)
            {
                _spokeVigilForDay = state.Day;
                Audio?.SpeakNarrator(
                    NarratorVoiceDirector.Trigger.VigilOpening, state.Rng.Inc, (ulong)state.Day);
            }
        }
    }

    /// <summary>Day whose vigil opening the narrator has already spoken, or 0. Guards the once-per-
    /// parking rule in <see cref="SyncCampModal"/>.</summary>
    private int _spokeVigilForDay;

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
        // The player's current location is passed in so the tutorial can stop telling the player
        // to walk to a room they are already standing in — see TutorialFlow.GoTo. U5: this is no
        // longer just the open drawer id — a walkable INTERIOR (the forge) never touches
        // Drawer.CurrentPanelId at all, which is exactly why "the tutorial isn't updating despite
        // entering the forge" survived a drawer-only check (see CurrentLocationPanelId).
        var locationId = CurrentLocationPanelId();
        Objective.Refresh(
            state,
            Tutorial.TopSlotText(state, locationId), // U23: tutorial overrides the top slot only
            Tutorial.Active ? Tutorial.Checklist(state) : null); // U5: the checklist ticks alongside it
        Overlay.RefreshAnchor(Tutorial.Active ? Tutorial.CurrentAnchor : TutorialAnchor.None, Town, this);
        UpdateObjectiveDock(); // Refresh can change the reason line's line count — re-dock to it
    }

    /// <summary>
    /// U5 (loop-legibility plan): the player's current location in the SAME vocabulary
    /// <c>OnTownBuildingClicked</c>'s own panel-id switch uses ("Forge"/"Shop"/"Tavern"/"Depths"/
    /// "Bounties"), or null when neither a drawer nor a walkable interior is open.
    ///
    /// <para>Before U5 the tutorial's "you're at X" ack read <see cref="DrawerHost.CurrentPanelId"/>
    /// alone — correct for every venue routed through <c>OpenPanel</c>, but a venue with a walkable
    /// <see cref="Town2d.InteriorLayout2D"/> room (the forge) is routed through <see
    /// cref="Town2d.Town2D.EnterInterior"/> instead, which never touches the drawer at all. That is
    /// the exact mechanism behind "the tutorial isn't updating despite entering the forge" — the
    /// drawer-only check could never see it. Falling back to the entered interior's own venue key
    /// (mapped through the same vocabulary) fixes it without adding a second parameter everywhere
    /// this value is threaded through.</para>
    /// </summary>
    private string? CurrentLocationPanelId() =>
        Drawer.CurrentPanelId ?? (Town.InteriorActive ? PanelIdForVenue(Town.InteriorVenueKey!) : null);

    /// <summary>Venue key (<c>Town2D.FindBuilding</c>'s own lowercase vocabulary) -&gt; drawer
    /// panel id — mirrors <see cref="OnTownBuildingClicked"/>'s own panelId switch for the venues
    /// that have one (every real venue does).</summary>
    private static string? PanelIdForVenue(string venueKey) => venueKey switch
    {
        "forge" => "Forge",
        "market" => "Shop",
        "tavern" => "Tavern",
        "minegate" => "Depths",
        "noticeboard" => "Bounties",
        _ => null,
    };

    private void RefreshHud()
    {
        RefreshStatus();
        var state = Adapter.CurrentState;
        RefreshObjectiveLine();
        Tutorial.SetWorkshopVocab(Town.WorkshopNametag, Town.WorkshopStationNoun);
        Tutorial.RefreshAffordances(state);
        Timeline.Refresh(state.Phase, Waiting);
        UpdateClockLabel(); // U3/U4: bell verb + player-phase banner are state-driven — refresh on every tick, not only per-frame _Process
        RefreshBellTray(); // U3 (KTD-B): keep the tray honest on every tick too, not only on submit
    }

    /// <summary>
    /// U3 (loop-legibility plan, KTD-B): rebuild the bell tray straight from
    /// <see cref="SimAdapter.PendingActions"/> — clear-then-compose (the same pattern every other
    /// rebuilt HUD row here uses, e.g. <see cref="RefreshStatus"/>'s <c>_statChips</c>), so the
    /// tray can never lie about what the bell will actually do. Called both from
    /// <see cref="OnActionQueued"/> (the instant a submit defers) and every <see cref="RefreshHud"/>
    /// tick (so a tick that clears <c>_pending</c>, or a withdraw, is reflected too).
    /// </summary>
    private void RefreshBellTray()
    {
        foreach (var child in _bellTray.GetChildren())
        {
            _bellTray.RemoveChild(child);
            child.Free();
        }

        foreach (var pending in Adapter.PendingActions)
        {
            _bellTray.AddChild(BuildBellTrayChip(pending));
        }
    }

    /// <summary>
    /// One bell-tray chip: <see cref="PendingVerbVocab.DisplayName"/> plus a "✕" withdraw
    /// control wired to <see cref="SimAdapter.Withdraw"/>. Closes over the EXACT
    /// <paramref name="action"/> instance from <see cref="SimAdapter.PendingActions"/> so
    /// <see cref="SimAdapter.Withdraw"/>'s reference-based removal takes down this chip's own
    /// entry, never a structurally-equal sibling's. A withdraw that fails (the tick beat the
    /// click) never fails silently — it toasts why, same as every other refused verb in this
    /// class (house rule: never a dead click).
    /// </summary>
    private Control BuildBellTrayChip(PlayerAction action)
    {
        var chip = new PanelContainer { Name = "BellTrayChip" };
        var row = new HBoxContainer { Name = "BellTrayChipRow" };
        row.AddThemeConstantOverride("separation", GameTheme.Space8);
        chip.AddChild(row);

        var verbName = PendingVerbVocab.DisplayName(action);
        var label = new Label { Name = "Verb", Text = verbName };
        label.AddThemeColorOverride("font_color", GameTheme.BodyTextColor);
        label.AddThemeFontSizeOverride("font_size", GameTheme.LegibilityFloor);
        row.AddChild(label);

        var withdraw = new Button
        {
            Name = "Withdraw",
            Text = "✕",
            TooltipText = $"Withdraw \"{verbName}\" — before the bell, it never happens",
            CustomMinimumSize = new Vector2(22, 22),
        };
        withdraw.Pressed += () =>
        {
            if (Adapter.Withdraw(action))
            {
                RefreshBellTray();
            }
            else
            {
                // Not reachable through normal play (single-threaded tick loop), but a stale
                // withdraw must never sit there pretending to work if it ever is.
                ShowBellToast($"Too late — \"{verbName}\" already left with the bell.");
                RefreshBellTray();
            }
        };
        row.AddChild(withdraw);

        return chip;
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

        // U2 (shell-and-audio plan, KTD-C): the world used to sit full-rect BEHIND this opaque
        // header, so the header hid the top ~quarter of it, and Town2D needed its own height fed
        // in to bias the camera away from the hidden band (Town.TopObstructionPx, now deleted).
        // BuildUi now mounts Town2D in LAYOUT FLOW below this header — the world's own rect
        // already excludes the header, so there is nothing left to compensate for here. This
        // chip's own dock math is unaffected: it is still a top-right OVERLAY spanning the full
        // viewport (KTD-C keeps drawers/modals/the objective chip as overlays), so it still needs
        // headerBottom to clear the Books Tray.
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

        // U2 (playtest-three plan): was state.Phase.ToString() — the same raw "Camp"/"ExpeditionDeep"
        // leak the timeline strip and continue screen had, just in a third place. One vocabulary now.
        var phaseChip = NamedStatChip("PhaseChip", "Phase", PhaseVocab.Display(state), UiKit.ChipTone.Accent);
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

        // U5(c) (faction-standing plan): a chip per faction whose standing has actually moved off
        // neutral. Folded into WEALTH+HANDS (not DUES) — standing is a discount the player earned,
        // not a scarcity/heartbeat gauge.
        wealthHands.AddChild(BuildStandingChips(state.Player.Standing));

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
    /// <summary>
    /// The three campaign milestones that earn a voice: the act turn, the climax, and the ending.
    ///
    /// <para>Each is a once-per-campaign event in the sim, but "the sim emits it once" is not the
    /// same claim as "the client sees it once" — events are re-read on reload, and a re-read that
    /// re-speaks would turn the rarest line in the game into a bug. Hence the latch, which is the
    /// client's own memory of what it has already said.</para>
    ///
    /// <para><see cref="CampaignAct"/> advancing had NO presentation of any kind before this —
    /// not a toast, not a ticker line — despite being the moment permadeath starts to bite. The
    /// climax and the ending already had text; they were simply silent.</para>
    /// </summary>
    private void SpeakMilestones(IEnumerable<GameEvent> events, GameState state)
    {
        foreach (var evt in events)
        {
            var trigger = evt switch
            {
                CampaignEnded => NarratorVoiceDirector.Trigger.CampaignEnding,
                ClimaxReached => NarratorVoiceDirector.Trigger.ClimaxReached,
                ActAdvanced => NarratorVoiceDirector.Trigger.ActAdvanced,
                _ => (NarratorVoiceDirector.Trigger?)null,
            };

            if (trigger is not { } t || !_spokenMilestones.Add(t))
            {
                continue;
            }

            // Rng.Inc is the campaign's identity everywhere flavor is picked; the day is the
            // event id, matching how the ledger voice keys its own pick.
            Audio?.SpeakNarrator(t, state.Rng.Inc, (ulong)state.Day);
        }
    }

    /// <summary>What the narrator has already said this session. A campaign milestone that speaks
    /// twice is worse than one that never speaks: the second time teaches the player it was never
    /// a milestone at all.</summary>
    private readonly System.Collections.Generic.HashSet<NarratorVoiceDirector.Trigger> _spokenMilestones = [];

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
    /// all (legal every phase).
    ///
    /// <para>U2 (playtest-three plan, KTD-B): headers are <see cref="PhaseVocab"/>'s words, not
    /// the raw sim phase names — this used to say "Camp"/"Deep" while the HUD banner right below
    /// it said "Vigil", one more split-brain surface. Kept as a literal (not built from
    /// <see cref="PhaseVocab.Display(DayPhase)"/> calls) only because this is a <c>const</c>;
    /// <see cref="GodotClient.Tests.PhaseVocabTests"/> pins the two against drifting apart.</para>
    /// </summary>
    public const string PhaseLegend =
        "Dawn/Prepare — parties muster and recruits arrive. Buy materials from the vendor, post bounties, craft, stock, and price.\n" +
        "Quest — parties descend toward their target floor. Craft, stock, and price; nothing else resolves until they return.\n" +
        "Vigil — a party pauses at its checkpoint before the deep floors. Send supply or recall the party; craft, stock, and price.\n" +
        "Deep Vigil — camped parties push into the deeper floors and the run is decided. Craft, stock, and price; nothing else to do but wait.\n" +
        "Night — heroes return with loot and news. Buy their ore, post bounties, craft, stock, and price.";

    /// <summary>
    /// U7 (§11.12 plan): the Books Tray's "Renown" button's real tooltip — the owner's playtest
    /// found every tray tooltip a one-word restatement of its icon ("Renown" alone, on a button
    /// with no visible text at all). <c>public const</c>, not inlined at the <see
    /// cref="TrayButton"/> call site, because <see
    /// cref="GodotClient.Ui.TutorialFlow.CopyFor"/>'s step 9 line must quote this EXACT sentence
    /// (<see cref="GodotClient.Tests.TutorialCopyIsFollowableTests"/>
    /// .TheTraySteps_QuoteTheTooltipsTheTrayButtonsActuallyCarry_NotTheirPanelTitles pins the
    /// join) — one constant read by both, so the two can never drift apart the way a retyped copy
    /// of "Renown" already proved they could.
    /// </summary>
    public const string RenownTrayTooltip = "Renown — every hero's card: standing, deepest run, and deeds";

    /// <summary>The Books Tray's "Commissions" button's real tooltip — same reasoning and the same
    /// cross-file pin as <see cref="RenownTrayTooltip"/>, for <see
    /// cref="GodotClient.Ui.TutorialFlow.CopyFor"/>'s step 10 line.</summary>
    public const string CommissionsTrayTooltip = "Commissions — the open board of hero requests you can craft against";

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

    /// <summary>
    /// U5(c) (faction-standing plan, R9): one small chip per faction whose standing has moved off
    /// neutral. Standing is a single positive-only lever (buying that faction's ore,
    /// <see cref="GameSim.Economy.OreMarketHandlers"/>) with a single effect (that faction's ore
    /// discount) that decays every Morning (<see cref="FactionDriftSystem"/>) — a faction still at 0 (never
    /// traded, or drifted all the way back) renders no chip at all, so the HUD's footprint never
    /// outgrows the mechanism's footprint into a reputation ladder the sim doesn't run.
    ///
    /// <para>Iterates <see cref="FactionRegistry.All"/>'s own sorted (ordinal) key order rather
    /// than <paramref name="standing"/>'s insertion order, so the row's left-to-right order is
    /// stable across saves/refreshes regardless of the order factions were first traded with. Each
    /// chip borrows the faction's own first supplied ore as its icon — the concrete thing the
    /// standing number is actually about — falling back to the gold glyph for the defensive case
    /// of a faction registered with no ore keys.</para>
    /// </summary>
    private static Control BuildStandingChips(ImmutableSortedDictionary<string, int>? standing)
    {
        var row = new HBoxContainer { Name = "StandingChips" };
        row.AddThemeConstantOverride("separation", GameTheme.Space8);
        if (standing is null)
        {
            return row;
        }

        foreach (var factionId in FactionRegistry.All.Keys)
        {
            var value = standing.TryGetValue(factionId, out var v) ? v : 0;
            if (value == 0 || !FactionRegistry.TryGet(factionId, out var faction) || faction is null)
            {
                continue; // neutral — never traded, or drifted fully back — nothing to show (R9)
            }

            var icon = faction.SuppliesOreKeys.IsEmpty
                ? IconRegistry.Glyph("gold")
                : IconRegistry.Ore(faction.SuppliesOreKeys[0]);
            var chip = NamedIconChip(
                $"StandingChip_{factionId}", icon, $"{value}", UiKit.ChipTone.Positive,
                $"{faction.DisplayName}: standing {value}/{faction.StandingCap} — their ore sells cheaper. " +
                "Buying more raises it; it drifts back toward neutral every Morning you don't.");
            row.AddChild(chip);
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

        // U8a: ProfessionHandlers.ApplySet's own typed reasons — reachable if a stale-enabled
        // Confirm (or a test forcing the click, ProgressionPanel's own doc) submits an
        // out-of-range pick that the client-side mirror should have already caught.
        if (reason.StartsWith("Must select at least one profession", StringComparison.Ordinal))
        {
            return "Pick at least one profession.";
        }

        if (reason.StartsWith("Cannot select more than", StringComparison.Ordinal))
        {
            return "You can only practice up to two professions at once.";
        }

        if (reason.StartsWith("Unknown profession", StringComparison.Ordinal))
        {
            return "That trade isn't one the Guild recognizes.";
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
            // U7: this control had NO tooltip at all before this unit — the one top-bar button the
            // owner's own complaint names, and the sole exception carved out of "23 files set
            // TooltipText" turned out to be a control with none. No key is bound to it (mouse-only
            // by design — see ShortcutMap, which has no entry for it), so this is description
            // only, never a "(key)" suffix.
            _advance.TooltipText = "Jump straight to the next phase, without waiting for the clock to reach it.";
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
            // phase (Dawn/Prepare/Quest/Vigil/Deep Vigil/Night, mapped from the kernel phase via
            // PhaseVocab, U2); an open-items readout replaces the countdown (U3); Expedition shows
            // a departure omen (U4); Morning names who is ready to go (U2 — "the bell tells you
            // who is ready to go").
            //
            // U1 (KTD-A, scope-ruling addendum): the vigil stop holds until answered, and closing
            // its modal (Escape) must never leave the player with no visible way back to it — this
            // control becomes the deliberate reopen affordance whenever the stop is armed and its
            // slate is closed, instead of a Hurry that would (correctly) no-op there and read as
            // dead.
            var vigilStopUnanswered = Conductor.Current == RaidConductor.Beat.VigilStop && !Camp.Visible;
            _advance.Text = vigilStopUnanswered ? "Return to the vigil" : BellVerb(state);
            // U7: same three-way branch the Text above already makes, mirrored for the tooltip so
            // the two can never say different things about what this one press does.
            _advance.TooltipText = vigilStopUnanswered
                ? "Reopens the vigil decision you have not answered yet."
                : Conductor.Current != RaidConductor.Beat.Idle
                    ? "Skips ahead to the next stop in today's raid."
                    : "Ends this phase and moves the day forward.";
            var tailParts = new System.Collections.Generic.List<string>();

            // A stopped day must never be an unexplained one. RaidConductor's shows now hold while
            // the player owes an answer or a surface owns the screen (its own hold doc) — the law
            // permits that only when skipping's cost is NAMED in copy, so the banner says the day is
            // waiting and the bell beside it still reads "Hurry the day along". Both halves, always
            // together: what it is waiting for, and the one press that overrides it.
            if (Conductor.ShowHeld)
            {
                tailParts.Add("the day waits on you");
            }

            if (state.Phase == DayPhase.Expedition)
            {
                tailParts.Add(DepartureOmen(state));
            }

            if (state.Phase == DayPhase.Morning)
            {
                var ready = HeroesReadyAtGateBadge(state);
                if (!string.IsNullOrEmpty(ready))
                {
                    tailParts.Add(ready);
                }
            }

            var badge = OpenItemsBadge(state);
            if (!string.IsNullOrEmpty(badge))
            {
                tailParts.Add(badge);
            }

            var tail = tailParts.Count > 0 ? $" — {string.Join(" · ", tailParts)}" : string.Empty;
            _clockLabel.Text = $"{PlayerPhaseName(state)}{tail}";
        }
    }

    /// <summary>U2 (playtest-three plan, KTD-B): the player-facing phase banner and bell verb now
    /// live in ONE table, <see cref="PhaseVocab"/>, shared with <see
    /// cref="GodotClient.Ui.ObjectiveTracker.DayTimeline"/>'s segment labels and
    /// <c>NewGameSelect</c>'s continue blurb — was <c>MainUi.PlayerPhaseName</c>/<c>BellVerb</c>,
    /// duplicated here and drifting from what the timeline strip printed (raw enum names).
    /// Kept as thin local aliases so every existing call site below reads unchanged.</summary>
    private static string PlayerPhaseName(GameState state) => PhaseVocab.Display(state);

    private static string BellVerb(GameState state) => PhaseVocab.BellVerb(state);

    /// <summary>U2: "who is ready to go" — the bell row names it instead of making the player
    /// guess. Every living hero is ready to march the instant the bell rings (adapter-side read;
    /// away-on-expedition heroes don't exist yet during Morning — <c>InFlight</c>/
    /// <c>PendingExpeditions</c> are both torn down by the time Evening hands off to the next
    /// day's Morning, see <c>ExpeditionDeepSystem</c>/<c>ExpeditionRevealSystem</c> — so a plain
    /// Alive count is exactly the roster the send-off tick will actually muster from).</summary>
    private static string HeroesReadyAtGateBadge(GameState state)
    {
        var ready = state.Heroes.Values.Count(h => h.Alive);
        return ready switch
        {
            0 => string.Empty, // "no heroes" is the destitution floor's own message, not the bell's
            1 => "1 hero ready at the gate",
            _ => $"{ready} heroes ready at the gate",
        };
    }

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

        // Mounted before the town so a cue fired during the first refresh already has somewhere to go.
        Audio = new AudioDirector();
        AddChild(Audio);
        Audio.ApplyPersistedMixer(); // C1: the player's saved mix, before anything plays
        Audio.SetPhase(Adapter.CurrentState.Phase);

        // --- U2 (shell-and-audio plan, R1/KTD-C): Town2D used to mount here as a PERMANENT
        // FullRect base child sitting BEHIND the whole `layout` column — which made the header's
        // opaque top region occlusion of the world a matter of paint order, not layout: the world
        // was always full-rect underneath, the header just drew over the top of it. That is what
        // made the mine "off the screen at the top" (R1) possible in the first place, and it took
        // TWO tuning passes (a camera bias, then that bias's own retune) to paper over it instead
        // of fixing it. The world now mounts INSIDE `layout`'s own `WorldSlot` region (built
        // below, after the header) — the one piece of vertical space the header does not claim —
        // so there is no rect left for the header to occlude. See WorldSlot's own remark. ---
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
            // U1 (KTD-A, scope-ruling addendum): the vigil stop holds until ANSWERED, not until the
            // modal closes — closing it (Escape) is a deliberate "go craft something first" beat,
            // never a silent way to strand the day. If the player closed the slate and comes back to
            // THIS control instead of walking back to it, reopen it here rather than no-op Hurry —
            // an armed-but-unreachable stop is exactly the softlock shape CampPanel has a history of.
            if (Conductor.Current == RaidConductor.Beat.VigilStop)
            {
                if (!Camp.Visible)
                {
                    Camp.ShowModal();
                }

                // No tick happens here — record the press anyway. A press that gets redirected is
                // still a fact about what the player did, and PlaytestLog.Action only ever sees
                // SimAdapter.Queue calls, never a blocked/no-op press like this one.
                PlaytestLog.Note("press AdvancePhase while VigilStop — reopened Camp modal");
                UpdateClockLabel();
                return;
            }

            // U1 (KTD-A): while the conductor owns the rest of the span (Expedition/
            // Camp-with-nobody-parked/ExpeditionDeep), this SAME control renders "Hurry the day
            // along" (PhaseVocab.BellVerb) and does exactly that — skip to the next stop — instead
            // of the Morning/Evening bell logic below, which never applies to those phases any more
            // (no counter to hold, no phase-specific verb to have queued). Idle covers Morning and
            // Evening both.
            if (Conductor.Current != RaidConductor.Beat.Idle)
            {
                // Set for the WHOLE call, not just its first tick: Hurry can chain SendOff -> Camp ->
                // Deep -> Evening in one press (nobody parked — the common day-1 case), and every one
                // of those ticks really was caused by this one press, not a mystery each. This is
                // deliberately the same cascade the owner's "jumped straight to night" report
                // describes — the log should now show N tick rows sharing this exact cause and
                // timestamp instead of looking like N unexplained advances.
                _pendingTickCause = "press:Hurry";
                Conductor.Hurry();
                _pendingTickCause = "";
                UpdateClockLabel();
                return;
            }

            var state = Adapter.CurrentState;
            // U5: ringing the bell while a counter session is open would otherwise silently fail to
            // advance — GameKernel holds the day at Morning while Counter is { Closed: false }. Close
            // the session first so the day ALWAYS moves, and surface it so it is never a silent
            // abandon of a live haggle/queue (the "never silently discards a live decision" goal).
            //
            // U2 (playtest-three plan, KTD-G): GameKernel's Morning-hold (GameKernel.cs, Advance's
            // `DayPhase.Morning when counter is { Closed: false }` branch) has no sim-side timeout —
            // the close queued above always lands before this same tick's Advance runs (CloseCounterAction
            // isn't in ActionTiming's immediate list, so it rides THIS AdvancePhase's batch), which is
            // why the day never actually holds via this button today. The log note is the cheap,
            // permanent detector KTD-G asks for regardless: if a future change ever breaks that
            // ordering, "MORNING-HOLD" is grep-able in the session log the moment it happens instead
            // of reading as a mysteriously stuck day with no evidence anywhere.
            var openCounter = state.Counter is { Closed: false } ? state.Counter : null;
            if (openCounter is not null)
            {
                Adapter.Queue(new CloseCounterAction());
                PlaytestLog.Note("MORNING-HOLD: counter open");
            }

            _pendingTickCause = "press:AdvancePhase";
            Clock.AdvanceNow(); // same advance the auto timer fires — player intent wins even engaged
            _pendingTickCause = "";

            // U2 bug fix: this toast is set AFTER AdvanceNow, not before. AdvanceNow synchronously
            // fires OnPhaseCompleted (the StateChanged subscription below), which unconditionally
            // manages the SAME toast banner (rejection > world notice > ClearToast()) — setting this
            // message before AdvanceNow meant OnPhaseCompleted's own "nothing to report" ClearToast()
            // wiped it out in the same frame, every time, since a plain counter-close+advance tick
            // produces neither a rejection nor a WorldNotice event. That made the "Closed the counter
            // — parties depart" confirmation (U5's whole point: never silently abandon a live haggle)
            // dead code from the moment it shipped — a real playtest gap caught by
            // PhaseVocabTests.RingingBell_AgainstAnOpenCounter..., not a test-timing issue. Setting it
            // last makes THIS message the one that survives to render.
            if (openCounter is not null)
            {
                if (Adapter.CurrentState.Phase == DayPhase.Morning && Adapter.CurrentState.Counter is { Closed: false })
                {
                    // Defense in depth (KTD-G): the close somehow didn't land — not reachable today
                    // (CloseCounterAction always lands in this same tick's batch), but if a future
                    // change ever breaks that ordering, tell the truth instead of a stale bell verb.
                    ShowBellToast("Close the counter first — the day waits on you");
                }
                else
                {
                    ShowBellToast(openCounter.Round > 0
                        ? "Closed the counter mid-haggle — parties depart."
                        : "Closed the counter — parties depart.");
                }
            }

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

        // Fullscreen toggle (owner playtest: "should be able to full screen the game" — there was
        // no way to do it at all). Runtime-only via DisplayServer, never project.godot (deny-listed
        // for agents) — window/stretch defaults stay whatever the project already ships. F11 (see
        // _Input) is the primary path; this button is the discoverable one, same glyph-button
        // convention as Auto/PlayPause/Speed above (no new icon asset).
        _fullscreen = new Button
        {
            Name = "Fullscreen", Text = "⛶", ToggleMode = true, CustomMinimumSize = new Vector2(24, 24),
            ButtonPressed = IsFullscreen(),
        };
        _fullscreen.Pressed += ToggleFullscreen;

        // U7: this was the ONE top-bar control that already named its own key ("Fullscreen
        // (F11)") — the plan's own example of what every other control should look like. Now
        // derived from ShortcutMap rather than a retyped literal, and the key gets a second,
        // always-visible home beside the button (UiKit.ShortcutBadge) instead of only living in
        // the hover tooltip — "controls with a key render the badge inline, not only on hover."
        var fullscreenEntry = ShortcutMap.Find("fullscreen");
        _fullscreen.TooltipText = ShortcutMap.Tooltip(fullscreenEntry.Id);

        var fullscreenRow = new HBoxContainer { Name = "FullscreenRow" };
        fullscreenRow.AddThemeConstantOverride("separation", GameTheme.Space4);
        fullscreenRow.AddChild(_fullscreen);
        fullscreenRow.AddChild(UiKit.ShortcutBadge(ShortcutMap.KeyLabel(fullscreenEntry)));
        verbRow.AddChild(fullscreenRow);

        // U3 (loop-legibility plan, KTD-B): the bell tray sits under the verb row, on the same
        // bell zone as Skip/Watch/Auto — one chip per action still waiting for the bell
        // (UpgradeForge/SetProfessions/CommissionLegendaryWork today; PendingVerbVocab covers any
        // future bell-rider). Built empty; RefreshBellTray (called from RefreshHud and from
        // OnActionQueued the instant a submit defers) populates it straight off
        // Adapter.PendingActions — no shadow list, so the tray can never lie about the queue.
        _bellTray = new HBoxContainer { Name = "BellTray" };
        _bellTray.AddThemeConstantOverride("separation", GameTheme.Space8);
        verbCluster.AddChild(_bellTray);

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

        // U7 (§11.12 plan): every tray tooltip below used to be a one-word restatement of its own
        // icon ("Ledger", "Forecast", ...) — the owner could not tell what these seven buttons did
        // without opening each one to find out. Every tooltip is now a full sentence naming what
        // the panel actually shows; none of these buttons carry a bound key (see ShortcutMap — the
        // tray has no keyboard shortcuts today), so there is no "(key)" suffix to add.
        var ledgerButton = TrayButton(
            "OpenLedger", IconRegistry.Glyph("skull"),
            "Ledger — yesterday's full accounting: what sold, what came in, and who bought it");
        ledgerButton.Pressed += () => Ledger.ShowFor(LastCompletedDay);
        trayRow.AddChild(CapTrayIcon(ledgerButton));

        // U10: open the raid-forecast board on demand (day-end auto-open is the chained path in
        // OnLedgerVisibilityChanged). Reads live state so it always reflects the current roster.
        var forecastButton = TrayButton(
            "OpenForecast", IconRegistry.Glyph("depths"),
            "Forecast — tomorrow's raid board: who's mustering, and how deep they're going");
        forecastButton.Pressed += () => Forecast.ShowForTomorrow(Adapter.CurrentState);
        trayRow.AddChild(CapTrayIcon(forecastButton));

        // Wave 3 (U15): open the commission board on demand — a Prepare-phase surface, same tray
        // as Forecast. Reads live state so it always reflects the current board. Tooltip is the
        // shared CommissionsTrayTooltip constant (see its own doc): TutorialFlow's step 10 line
        // quotes this exact sentence, so the two can never drift apart.
        var commissionsButton = TrayButton("OpenCommissions", IconRegistry.Glyph("bounty"), CommissionsTrayTooltip);
        commissionsButton.Pressed += () => Commissions.ShowOpen(Adapter.CurrentState);
        trayRow.AddChild(CapTrayIcon(commissionsButton));

        // Wave 4 (U21): open the Legends Wall on demand — same tray as Forecast/Bestiary/Commissions.
        // Reads live state so it always reflects the current memorials/records/gear.
        var legendsButton = TrayButton(
            "OpenLegends", IconRegistry.Glyph("rune"),
            "Legends — the wall of fates your work has actually changed");
        legendsButton.Pressed += () => Legends.ShowWall(Adapter.CurrentState);
        trayRow.AddChild(CapTrayIcon(legendsButton));

        // G1 (plan 2026-07-25-001, Slice 2): the demand telegraph had no player-visible entry —
        // DemandPanel was already registered in the Drawer (U6) and reachable via
        // OpenPanel("Demand"), but nothing ever called it. Same tray, wired straight onto the
        // drawer's own OpenPanel router (mirrors OnTownBuildingClicked's OpenPanel("Bounties")
        // call) rather than inventing a bespoke show method.
        var demandButton = TrayButton(
            "OpenDemand", IconRegistry.Glyph("gossip"),
            "Demand — what the town wants right now, and how badly");
        demandButton.Pressed += () => OpenPanel("Demand");
        trayRow.AddChild(CapTrayIcon(demandButton));

        // Phase B, B1d: the hero digest (standing/deepest/XP-rank/deeds card per alive hero) had
        // no HUD entry — same tray as Demand/Legends above. Opens "HeroCards" (not "Heroes" —
        // that drawer id is already the portrait-grid roster reached via town clicks). Tooltip is
        // the shared RenownTrayTooltip constant (see its own doc) — TutorialFlow's step 9 line
        // quotes this exact sentence.
        var heroesButton = TrayButton("OpenHeroCards", IconRegistry.Glyph("shield"), RenownTrayTooltip);
        heroesButton.Pressed += () => OpenPanel("HeroCards");
        trayRow.AddChild(CapTrayIcon(heroesButton));

        // U-D4: the progression spine — same tray. Opens the five-ladder board.
        var progressButton = TrayButton(
            "OpenProgress", IconRegistry.Glyph("weapon"),
            "Progress — the five ladders tracking your climb, and each one's next rung");
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

        // U21/U2: the ExpandFill row between the header and the ticker — claims exactly the
        // vertical space neither of those two fixed-height rows wants, so the header stays
        // pinned top and the ticker stays pinned bottom regardless of window height.
        //
        // U2 (shell-and-audio plan, R1/KTD-C): this USED to be a transparent, input-passthrough
        // spacer over a full-rect Town2D mounted behind the whole Layout column — the header
        // painted over the world's top band and MouseFilter.Ignore let clicks fall through to it.
        // Town2D is now this Control's own child, anchored FullRect WITHIN it (below), so
        // WorldSlot no longer needs to pass anything through to a layer behind it — it IS the
        // world's layout box. Occlusion becomes structural: the header and the ticker each claim
        // their own row in `layout`, WorldSlot gets whatever height is left over, and Town2D can
        // never report a rect outside the box its own parent handed it.
        var worldSlot = new Control
        {
            Name = "WorldSlot",
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        layout.AddChild(worldSlot);

        Town = new Town2D { Name = "Town2D" };
        worldSlot.AddChild(Town);
        Town.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Town.Build(Adapter);
        Town.Clock = Clock;
        Town.HeroClicked += OnTownHeroClicked;
        Town.BuildingClicked += OnTownBuildingClicked;
        // U3 (painted-interiors plan): a station's Picked now carries its WHOLE StationSpec
        // (Action/Focus/HoverLine/FlavorLine), so it routes through its own OnStationActivated
        // rather than straight onto OnInteriorHotspotActivated.
        Town.StationActivated += OnStationActivated;
        // fix/pressing-E-at-nothing-says-something: a dead "interact" press (no station in range) used
        // to produce nothing at all — no sound, no prompt, no screen change (verified-good playtest
        // log, seven of these in one 700-turn run). Reuses the same rejection-toast banner every other
        // transient one-liner in this class shows, exactly like OnStationActivated's flavor toast above.
        Town.NoTargetInteract += ShowBellToast;
        // U4: replaces the deleted InteriorStage.Exited wiring — fires on EITHER room-exit path
        // (Esc or the door), re-syncing the engaged latch/deferred focus beat the same way every
        // other modal-close path already does (see Town2D.InteriorExited's own doc).
        Town.InteriorExited += OnInteriorExited;
        // U10 (world-and-interiors plan, KTD-5): the return half of the departure focus beat —
        // fires once a queued survivor group's show floor elapses and its walk-in actually begins.
        Town.PartyEmerging += OnPartyEmerging;

        Forge = InstantiatePanel<ForgePanel>("res://scenes/panels/forge_panel.tscn");
        Shop = InstantiatePanel<ShopPanel>("res://scenes/panels/shop_panel.tscn");
        Heroes = InstantiatePanel<HeroesPanel>("res://scenes/panels/heroes_panel.tscn");
        Tavern = InstantiatePanel<TavernPanel>("res://scenes/panels/tavern_panel.tscn");
        Depths = InstantiatePanel<DepthsPanel>("res://scenes/panels/depths_panel.tscn");
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

        // --- U9 (world-and-interiors plan, KTD-4): the ONE live MineWatch instance (constraint
        //     4 — pumping frames while any SubViewport renders hangs gdUnit headless, and a
        //     second live instance would double both that hazard and the GPU cost). Depths is
        //     registered above, so its MountWatch slot already exists — this is the strip's
        //     resting home; ScryingMirror steals it for as long as it's open (see
        //     OnMirrorVisibilityChanged) and hands it back on close. -------------------------
        Watch = new MineWatch { Name = "MineWatch" };
        Watch.Build();
        Watch.Clock = Clock;
        Depths.MountWatch(Watch);

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

        // --- U4 (shell-and-audio plan): the in-game system menu — Esc's new rung (see _Input).
        //     Code-built modal sibling on the RaidForecastBoard precedent (no scene, no import
        //     churn), mounted last of the modal overlays so it draws above every one of them —
        //     moot in practice (it only ever opens when nothing else is), but correct regardless.
        _systemMenu = BuildSystemMenu();
        _systemMenu.Visible = false;
        AddChild(_systemMenu);
        _systemMenu.VisibilityChanged += OnSystemMenuVisibilityChanged;

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
        // U7 (world-and-interiors plan, KTD-3): seed the tutorial's workshop vocabulary from the
        // SAME resolution the building/drawer already use (Town.WorkshopNametag/StationNoun) —
        // Town.Build ran above, so this is never stale on the very first frame. RefreshHud keeps
        // it live if a second profession changes it mid-run.
        Tutorial.SetWorkshopVocab(Town.WorkshopNametag, Town.WorkshopStationNoun);
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

        // --- U5 (loop-legibility plan): the tutorial's pointing overlay — a screen-space pulsing
        //     outline for HUD anchors (the world-space building pulse lives on Building2D itself,
        //     ticked through this same overlay — see TutorialOverlay's own class doc). Mounted
        //     late (mirrors Mirror/Pip above) so its outline draws above whatever it points at,
        //     including a modal card (the Vigil step's CampCard). Ignores the mouse entirely — a
        //     pure visual pointer, never a click target and never in the way of one. -------------
        Overlay = new TutorialOverlay();
        AddChild(Overlay);
        Overlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Overlay.Build();

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
            // U7 (world-and-interiors plan, KTD-3): the workshop drawer's title follows the
            // profession (Town.WorkshopNametag) — the registration/routing id ("Forge") never
            // changes, only the header text a player actually reads.
            Drawer.Open(id, id == "Forge" ? Town.WorkshopNametag : null);

            // Station split (owner playtest, 2026-08): a station click narrows the Forge panel to
            // just its own job by calling ForgePanel.FocusSection right after THIS method returns
            // (OnStationActivated, below) — but this method is also the bare, non-station open
            // (Camp's "Forge something for them" shortcut, direct OpenPanel("Forge") calls from
            // playtest tooling), which never calls FocusSection at all. Reset here, on every open,
            // so a bare open always shows the full panel rather than silently inheriting whatever a
            // PREVIOUS room visit last narrowed it to. See ForgePanel.ResetFocus's own doc.
            if (id == "Forge")
            {
                Forge.ResetFocus();
            }

            PanelFor(id).Refresh();
            Audio.Play(EntranceCueFor(id));
            // Watching the raid gets the Mine's own theme; every other panel stays with the day.
            Audio.SetScene(id == "Depths" ? "depths" : null);
            // U7 (tutorial 3-day arc): "meet your heroes" is a UI-only fact (no sim event for
            // opening a panel) — this is the ONE router every real open funnels through (town
            // clicks, quick-travel, tray buttons), so it is the one place to notify from. TutorialFlow
            // itself decides whether id ("Tavern"/"HeroCards") is the one it is waiting on.
            Tutorial.NotifyPanelOpened(id);
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
        // Nothing completed — an immediate action just reported itself. Say nothing.
        //
        // The guard below correctly stopped an immediate action from firing PartyDepart, but then fell
        // straight through to `Cue.Bell` for it: a 1.6s bronze bell on EVERY accepted craft, buy,
        // shelve and reprice. Owner's playtest, two complaints with this one cause — "doing anything in
        // the forge changes the music" (a long tonal bell over a -22 dB bed reads as the music
        // changing) and "shop stock sound was changed... it's now a scary bell instead of the
        // shop/register noise" (Shelve's own cue, then the bell on top of it). Starting a fresh
        // campaign fires a burst of immediate actions, which is the "restarting had a lot of strange
        // noises" report too.
        //
        // The bell belongs to the day advancing. This method's own doc says "the tick's one sound" —
        // an immediate action is not a tick.
        if (completedPhase == state.Phase)
        {
            return;
        }

        // Morning ending is the send-off: the party is actually leaving, which deserves its own cue
        // rather than the generic bell.
        var departing = completedPhase == DayPhase.Morning;
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
        //
        // ORDERING HAZARD found via ShopPanelTests.VeteranHero_PassesOnPoorShelfItem...: this method
        // runs INSIDE OnPhaseCompleted, BEFORE that method's own RefreshAll() call — and RefreshAll's
        // per-drawer refresh (`if (Drawer.CurrentPanelId is { } openId) PanelFor(openId).Refresh();`)
        // only fires while a panel is still registered as open. Calling Drawer.Close() here FIRST
        // would null CurrentPanelId before RefreshAll ever runs, so the panel that was open for the
        // exact tick that just closed it would silently never render that tick's own outcome (e.g. a
        // hero's refusal reason) — stale until the player happens to reopen it. Refresh it explicitly,
        // right here, before closing: the sim's CurrentState/LastEvents are already fully settled by
        // this point (AdvancePhase completed before StateChanged fired), so this renders the true
        // tick outcome; RefreshAll's own gated refresh then no-ops harmlessly once the drawer is shut.
        if (Drawer.CurrentPanelId is { } closingPanelId)
        {
            PanelFor(closingPanelId).Refresh();
        }

        Drawer.Close();

        // (2) a genuine MODAL (Ledger/Camp/Mirror/Forecast/Bestiary/Commissions/Legends, or the
        // walkable interior room) is a different case from a drawer: the player opened it on purpose
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
    /// U10 (world-and-interiors plan, KTD-5): the return half of the departure beat above —
    /// <see cref="Town2D.PartyEmerging"/> fires once a queued survivor group's show floor elapses
    /// and its staggered walk-in actually begins. Reuses the SAME deferred-focus plumbing
    /// <see cref="SoundTheTick"/>'s departure beat already established (<see
    /// cref="_pendingMineGateFocus"/> / <see cref="TryFireDeferredMineGateFocus"/>) — a modal open
    /// at the exact moment the party re-emerges defers the camera exactly the way a modal open at
    /// send-off defers it (#335's own rule, reused rather than re-derived). The narrator toast is
    /// NOT modal-gated (unlike the camera): it uses the same always-visible banner every other
    /// bell/rejection toast renders through, so the player reads it even with a drawer open.
    /// </summary>
    private void OnPartyEmerging(string venueId)
    {
        if (ModalOwnsTheScreen())
        {
            _pendingMineGateFocus = true;
        }
        else
        {
            Town.FocusOnMineGate();
        }

        var venueName = VenueRegistry.All.TryGetValue(venueId, out var venue) ? venue.DisplayName : "the depths";
        ShowBellToast($"The party returns from {venueName}...");
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
    /// walkable interior room covers the middle of the screen — the "not a drawer" half of <see
    /// cref="UpdateEngaged"/>'s engaged latch, pulled into its own method so U1's departure-focus
    /// pending beat (above) reads the EXACT same predicate instead of a second hand-copied clause
    /// list that could silently drift from it.
    ///
    /// <para>U4 (painted-interiors plan): <c>Interior.IsOpen</c> (the deleted, always-false
    /// InteriorStage) is replaced with <see cref="Town2D.InteriorActive"/> — the room genuinely
    /// covers the screen the same way a modal does, so it belongs in this predicate exactly the
    /// way this doc already described "an interior" before U1 ever wired one up.</para>
    /// </summary>
    private bool ModalOwnsTheScreen() =>
        Town.InteriorActive || AnOverlayOwnsTheScreen();

    /// <summary>
    /// <see cref="ModalOwnsTheScreen"/> minus the room itself — "is some overlay covering the screen
    /// *on top of* wherever we are".
    ///
    /// <para><b>Why this split exists.</b> U4 correctly added <see cref="Town2D.InteriorActive"/> to
    /// <see cref="ModalOwnsTheScreen"/>: the room does cover the screen like a modal, and the latch and
    /// the mine-gate focus gate both want that. But the Escape ladder's room-exit rung in
    /// <see cref="_Input"/> only runs <i>when the room is active</i> and then asked
    /// <see cref="ModalOwnsTheScreen"/> whether anything else was open — which, after U4, was
    /// unconditionally true. The last rung became unreachable and Esc stopped exiting the room.
    /// <c>InteriorEntryExitTests.Escape_WithNoDrawerOpen_ExitsTheRoom</c> caught it. A predicate that
    /// includes your own state is the wrong question to ask about everyone else's.</para>
    ///
    /// <para><b>Shell-and-audio plan U4:</b> the system menu (<see cref="_systemMenu"/>) joins this
    /// list — it covers the screen exactly like Ledger/Camp/Mirror do, so <see
    /// cref="UpdateEngaged"/>'s latch (world input suppressed, clock held) and the interior-exit
    /// rung's own defensive re-check both already treat it correctly with no further changes.
    /// <see cref="_Input"/>'s OWN system-menu rung asks <c>_systemMenu.Visible</c> directly, never
    /// through this method — the same lesson the paragraph above already states: a rung must never
    /// learn about its own state by asking the "is anyone ELSE open" question.</para>
    /// </summary>
    private bool AnOverlayOwnsTheScreen() => OverlaySurfaces().Any(o => o.Surface.Visible);

    /// <summary>
    /// The named list <see cref="AnOverlayOwnsTheScreen"/> and <see cref="ActiveOverlayName"/> both
    /// fold over — one source for "is anything covering the screen" and "which one, by name",
    /// so the two can never silently drift apart into two hand-copied lists.
    ///
    /// <para><b>2026-08-12 (coverage-can-see-the-overlays finding A):</b> before this method existed,
    /// <c>AgentPlaytest.cs</c>'s <c>Location()</c> only ever checked <c>Drawer.IsOpen</c> — every one
    /// of these seven overlays bypasses the drawer by design (this file's own "FullRect overlays above
    /// the drawer" comments elsewhere), so a full playthrough that opened the Ledger and the Camp
    /// panel every day reported byte-identical Panel coverage to a run that never opened either. <see
    /// cref="ActiveOverlayName"/> gives the playtest bridge the SAME predicate this class already
    /// trusts for input-blocking, instead of a second hand-maintained name list.</para>
    /// </summary>
    private (string Name, CanvasItem Surface)[] OverlaySurfaces() => new (string, CanvasItem)[]
    {
        ("Ledger", Ledger),
        ("Camp", Camp),
        ("Mirror", Mirror),
        ("Forecast", Forecast),
        ("Bestiary", Bestiary),
        ("Commissions", Commissions),
        ("Legends", Legends),
        ("SystemMenu", _systemMenu),
    };

    /// <summary>The name of whichever <see cref="OverlaySurfaces"/> entry currently owns the screen,
    /// or null if none does. <c>internal</c> so <c>AgentPlaytest.cs</c>'s <c>Location()</c> (same
    /// assembly, GodotClient.Tools) can report an open Ledger/Camp/Mirror/Forecast/Bestiary/
    /// Commissions/Legends/system-menu as a distinct, trackable location instead of it silently
    /// reading as "town" — see <see cref="OverlaySurfaces"/>'s own doc for why this reuses that list
    /// rather than re-deriving the answer.</summary>
    internal string? ActiveOverlayName() => OverlaySurfaces().FirstOrDefault(o => o.Surface.Visible).Name;

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

    /// <summary>
    /// U-audio-3 (verbs that resolved silently): which SFX cue, if any, marks the ledger-reveal
    /// narrator trigger picked by <see cref="NarratorVoiceDirector.SelectForNight"/>. Before this
    /// unit the Evening reveal of a hero who did not come back shared <see cref="Cue.Bell"/> with
    /// every other night's ending — the same generic toll for "the party is home safe" and "one of
    /// them is not." Only <see cref="NarratorVoiceDirector.Trigger.DeathEpitaph"/> earns a distinct
    /// cue: a proven save or a killing blow are good news, and good news does not need a bell of
    /// its own on top of the narrator already speaking.
    ///
    /// <para>Public and pure so a test can pin the mapping directly — mirrors
    /// <see cref="Audio.AudioDirector.LoadComposedTrackForCensus"/>'s own "test entry point into
    /// production's real decision" contract, applied here instead of adding a fragile end-to-end
    /// scenario that would need to script an entire expedition to a death just to prove one
    /// <c>switch</c> arm.</para>
    /// </summary>
    public static Cue? DeathNoticeCueFor(NarratorVoiceDirector.Trigger trigger) =>
        trigger == NarratorVoiceDirector.Trigger.DeathEpitaph ? Cue.DeathToll : null;

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

    /// <summary>
    /// Test-only stand-in for the real OS window mode. U3 (shell-and-audio plan): MOVED to <see
    /// cref="UiSettings.TestWindowMode"/> so the title screen's own F11 copy shares the exact same
    /// seam instead of a second one — this forwarding property keeps every existing test
    /// (<c>MainUi.TestWindowMode = ...</c>) compiling and passing unchanged. See <see
    /// cref="UiSettings"/>'s own doc for why the seam is needed at all (headless
    /// <see cref="DisplayServer.WindowSetMode"/> is a verified no-op).
    /// </summary>
    public static DisplayServer.WindowMode? TestWindowMode
    {
        get => UiSettings.TestWindowMode;
        set => UiSettings.TestWindowMode = value;
    }

    /// <summary>Whether the OS window is currently fullscreen — delegates to <see
    /// cref="UiSettings.IsFullscreen"/> (U3: the one shared implementation both hosts read).</summary>
    private static bool IsFullscreen() => UiSettings.IsFullscreen();

    /// <summary>
    /// F11 / the HUD button: flip the OS window between windowed and fullscreen. U3 moved the
    /// actual <see cref="DisplayServer"/> call + persistence into <see
    /// cref="UiSettings.ToggleFullscreen"/> (shared with the title screen's own F11 handler); this
    /// wrapper's only remaining job is syncing THIS host's discoverable HUD button, since the
    /// title screen has no equivalent button to sync (its own Settings checkbox syncs itself via
    /// <see cref="SettingsPanel.Refresh"/> instead).
    /// </summary>
    private void ToggleFullscreen()
    {
        var isFull = UiSettings.ToggleFullscreen();
        if (_fullscreen is not null)
        {
            _fullscreen.ButtonPressed = isFull;
        }
    }

    /// <summary>Town hero click (R20): open the Heroes drawer with that hero's detail bound.</summary>
    private void OnTownHeroClicked(int heroValue)
    {
        OpenPanel("Heroes");
        Heroes.SelectHero(heroValue);
    }

    /// <summary>
    /// Town building click/interact (R20, T8, U1 painted-interiors plan): <see cref="Town2D"/>'s
    /// <see cref="Building2D"/> emits its lowercase venue keys ("forge"/"market"/"tavern"/
    /// "minegate"/"noticeboard"); the legacy capitalized names ("Forge"/"Shop"/"Tavern"/"Gate"/
    /// "Bounties") are accepted too since <see cref="QuickTravel"/> and <c>TutorialFlow</c>'s own
    /// quick-travel row (out of this unit's edit scope) still send them.
    ///
    /// <para><b>U1 (R1/R9, data-gated):</b> a venue with an <see cref="InteriorLayout2D"/> row
    /// (slice 1: "forge" only) puts the player INSIDE the walkable room instead — the drawer is
    /// never again the direct response to that venue's interact. Every OTHER venue is unaffected:
    /// no row, no room, same <see cref="OpenPanel"/> routing as before this plan. Any unknown key
    /// falls back to the bare-world "Town" id.</para>
    /// </summary>
    private void OnTownBuildingClicked(string building)
    {
        var venueKey = building switch
        {
            "forge" or "Forge" => "forge",
            "market" or "Shop" => "market",
            "tavern" or "Tavern" => "tavern",
            "minegate" or "Gate" => "minegate",
            "noticeboard" or "Bounties" => "noticeboard",
            _ => null,
        };

        if (venueKey is not null && InteriorLayout2D.Rooms.ContainsKey(venueKey))
        {
            Town.EnterInterior(venueKey);
            // U4: the room covers the screen exactly like a modal (ModalOwnsTheScreen now reads
            // Town.InteriorActive) — engage the latch the instant it opens, same as every OpenPanel/
            // modal-open call site below already does. Town.InteriorExited (wired in BuildUi) is
            // the matching release on the way back out.
            UpdateEngaged();

            // U5 (loop-legibility plan): the checklist sub-tick ("Arrived") and the "you're at X"
            // swap must fire on THIS route too — a walkable interior never touches
            // Drawer.CurrentPanelId at all (see CurrentLocationPanelId's own doc), which is exactly
            // why "the tutorial isn't updating despite entering the forge" survived a drawer-only
            // check. Called AFTER EnterInterior/UpdateEngaged so CurrentLocationPanelId already
            // sees the new room — the non-interior route below gets the same call for free at the
            // end of OpenPanel, which runs after Drawer.Open has set CurrentPanelId.
            Tutorial.NotifyEnteredBuilding(venueKey);
            RefreshObjectiveLine();
            return;
        }

        if (venueKey is not null)
        {
            Tutorial.NotifyEnteredBuilding(venueKey);
        }

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
    /// U3 (painted-interiors plan): the walkable room's own station router — a real click/E on one
    /// of <see cref="InteriorLayout2D"/>'s forge stations. A flavor station (<see
    /// cref="InteriorLayout2D.StationSpec.Action"/> null — Quench, and every other building's own
    /// flavor props) never opens a panel: it shows its <see
    /// cref="InteriorLayout2D.StationSpec.FlavorLine"/> as a one-line toast, an honest response
    /// rather than a silent dead click. A real-verb station routes through the EXISTING <see
    /// cref="OnInteriorHotspotActivated"/> (same drawer-id vocabulary as ever), then — if the row
    /// also names a <see cref="InteriorLayout2D.StationSpec.Focus"/> (Forge's materials/craft split)
    /// — scrolls/flashes that section on the panel that just opened.
    ///
    /// <para><b>U5 (verify-by-playing plan, KTD-D):</b> station identity now resolves through the
    /// TABLE, not a switch — every real station also toasts its own <see
    /// cref="InteriorLayout2D.StationSpec.Copy"/> line, the real-verb counterpart to a flavor
    /// station's <c>FlavorLine</c> toast above. This is what makes "pressing the anvil" read
    /// differently from "pressing the furnace" even though both still land on the same Forge panel
    /// — the owner's complaint was never that they open different code paths, it was that nothing
    /// about the press itself told the two apart. A <see
    /// cref="InteriorLayout2D.StationSpec.CombinesWith"/> pair (the forge's anvil+bellows) is NOT
    /// special-cased here: both members already carry the identical <c>Action</c>/<c>Focus</c>, so
    /// pressing either one resolves to the exact same panel/section — one combined session, not two
    /// independent ones — while each still toasts its own <c>Copy</c> line (<see
    /// cref="StationIdentityTests"/> pins both halves of that contract).</para>
    /// </summary>
    private void OnStationActivated(InteriorLayout2D.StationSpec station)
    {
        if (station.Action is null)
        {
            // Honest flavor (U3): no verb here, ever — reuses the same rejection-toast banner
            // every other transient one-liner in this class shows (ShowBellToast's own doc).
            ShowBellToast(station.FlavorLine ?? $"{station.Label}: nothing to do here.");
            return;
        }

        OnInteriorHotspotActivated(station.Action);

        if (station.Focus is { } focus && PanelFor(station.Action) is ForgePanel forge)
        {
            forge.FocusSection(focus);
        }

        // U5 (KTD-D): the real-verb station's own on-screen line — required whenever Action is
        // non-null (InteriorLayout2D.StationSpec's own doc), so the fallback below is defensive only.
        ShowBellToast(station.Copy ?? $"You work the {station.Label}.");
    }

    /// <summary>
    /// A content hotspot (never exit) was pressed — open the SAME drawer id the hotspot's action
    /// names. U3 (painted-interiors plan): <see cref="OnStationActivated"/> is the walkable room's
    /// real caller now (a real-verb station's Action, after routing its optional Focus); kept as
    /// its own method — string in, `OpenPanel`-or-modal out — rather than folded into
    /// <see cref="OnStationActivated"/> directly, since the action-string shape is also what any
    /// future slice-2 hotspot (KTD-3: a table row, never a new code path) will carry.
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

        // U1 (world-and-interiors plan): the gatehouse's "overlook" station — the ONE new action
        // string this unit adds. Same routing shape as Bestiary/Legends above: a code-built modal,
        // not a drawer id, so it must be caught here before OpenPanel (which only knows drawer ids
        // and would throw ArgumentOutOfRangeException for "Watch"). During non-live phases the
        // Mirror already renders its own honest "nobody below" empty state — no extra plumbing
        // needed here for that case.
        if (action == "Watch")
        {
            Mirror.ShowMirror();
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

    /// <summary>
    /// The full #320 Escape-topmost ladder, pinned end to end (<see cref="SystemMenuTests"/>'s
    /// <c>EscapeLadder_*</c> cases prove this exact order):
    ///
    /// <list type="number">
    /// <item>A true overlay (<see cref="DrawerHost"/>, <see cref="ModalEscape"/>'s callers) is a
    /// CHILD of this <see cref="MainUi"/> Control, and Godot dispatches <c>_Input</c> in reverse
    /// tree order — children before parents — so any of them already consumed (and marked handled)
    /// this same Escape press before this method ever runs (<see
    /// cref="EscapeClosesModalsTests"/> proves that for the existing overlays).</item>
    /// <item><b>U4 (shell-and-audio plan) — the system menu's OWN rung, checked FIRST here:</b>
    /// when it is the thing that's open, Esc closes it. Asked about ONLY ITSELF
    /// (<c>_systemMenu.Visible</c>), never "is anything else open too" — the exact lesson <see
    /// cref="AnOverlayOwnsTheScreen"/>'s own doc already states, and the one this rung would get
    /// wrong if it reused that method instead. Nothing below needs to re-check the menu once this
    /// closes it.</item>
    /// <item>The walkable interior room exits (painted-interiors plan U1) — unchanged position and
    /// logic, still gated behind the pre-existing defensive <c>Drawer.IsOpen ||
    /// AnOverlayOwnsTheScreen()</c> re-check (defense in depth, not the only thing stopping a
    /// double-close: Godot's own dispatch order already prevents that — see point 1).</item>
    /// <item><b>U4's new BOTTOM rung:</b> in the bare town, with no drawer/modal/interior open,
    /// Esc opens the system menu — replacing what used to be a silent no-op. Gated on the exact
    /// same <c>!Drawer.IsOpen &amp;&amp; !AnOverlayOwnsTheScreen()</c> check as point 3's guard, so
    /// the menu can never open ON TOP of something else that is (which is also why the menu can
    /// never open while a minigame overlay owns an un-queued gesture — that overlay lives inside
    /// the Forge drawer, so <c>Drawer.IsOpen</c> is already true).</item>
    /// </list>
    /// </summary>
    public override void _Input(InputEvent @event)
    {
        // F11 fullscreen toggle: global, independent of interior/drawer/modal/system-menu state
        // (unlike the Escape ladder below) — there is no reason a player mid-panel, or mid-pause,
        // shouldn't be able to hit it.
        if (@event is InputEventKey { PhysicalKeycode: Key.F11, Pressed: true, Echo: false })
        {
            ToggleFullscreen();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is not InputEventKey { PhysicalKeycode: Key.Escape, Pressed: true })
        {
            return;
        }

        // Ladder step 2 (see class doc): the system menu's own rung, topmost when it is the thing
        // that's open.
        if (_systemMenu.Visible)
        {
            CloseSystemMenu();
            GetViewport()?.SetInputAsHandled();
            return;
        }

        if (!Town.InteriorActive)
        {
            // Ladder step 4: nothing to back out of (no room), and — per the guard below — nothing
            // else open either. Open the system menu. Any drawer/modal that WAS open already
            // consumed this same Escape as a child (step 1) before this parent ever ran, so this
            // re-check is the same defense-in-depth precedent as the interior-exit rung's own guard,
            // not a sign that either one is expected to be true here.
            if (!Drawer.IsOpen && !AnOverlayOwnsTheScreen())
            {
                OpenSystemMenu();
                GetViewport()?.SetInputAsHandled();
            }

            return;
        }

        // Ladder step 3. AnOverlayOwnsTheScreen, NOT ModalOwnsTheScreen: the latter now includes
        // Town.InteriorActive, which this method has already required above — asking it here made the
        // guard unconditionally true and this rung dead. See AnOverlayOwnsTheScreen's doc.
        if (Drawer.IsOpen || AnOverlayOwnsTheScreen())
        {
            return;
        }

        Town.ExitInterior();
        GetViewport()?.SetInputAsHandled();
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
            // U7 (tutorial 3-day arc): the day-1 capstone ("look in on them") is a UI-only fact with
            // no sim event to read durably — this ONE hook covers both real entry points (the
            // persistent Watch button and the PiP dock's expand click), so either door teaches it.
            Tutorial.NotifyMirrorOpened();
            // U9 (KTD-4): borrow the shared strip for as long as the Mirror is open — stealing it
            // from Depths (its resting host) so there is never a second live SubViewport.
            Mirror.MountWatch(Watch);
        }
        else
        {
            // U9 (KTD-4): hand the strip back to its resting host the instant the Mirror closes —
            // before the resume-play branch below, so a re-open a frame later never races an
            // empty slot.
            Depths.MountWatch(Watch);
            if (_resumePlayOnMirrorClose)
            {
                Clock.Play();
                _resumePlayOnMirrorClose = false;
            }
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

    /// <summary>U4: the system menu holds the town clock while open, same as every other modal
    /// here (Ledger/Camp/Mirror/...) — pausing the day while the player reads a pause menu is the
    /// whole point of a pause menu.</summary>
    private void OnSystemMenuVisibilityChanged()
    {
        if (_systemMenu.Visible)
        {
            _resumePlayOnSystemMenuClose = Clock.Playing;
            Clock.Pause();
        }
        else if (_resumePlayOnSystemMenuClose)
        {
            Clock.Play();
            _resumePlayOnSystemMenuClose = false;
        }

        UpdateEngaged(); // the system menu engages the latch too — world input suppressed (R3)
        UpdateClockLabel();
        TryFireDeferredMineGateFocus(); // U1: fires the deferred departure pan if the screen is now clear
    }

    /// <summary>
    /// U4: builds the system menu — a full-rect dim + centered wood card (the same shape <see
    /// cref="NewGameSelect"/>'s title screen uses) holding a button list (<see
    /// cref="_systemMenuList"/>: Resume/Settings/Save &amp; quit to title/Quit game) and a nested
    /// <see cref="SettingsPanel"/> sub-view, toggled the same way the title screen toggles its own
    /// picker/primer/settings views — never more than one visible at once.
    /// </summary>
    private Control BuildSystemMenu()
    {
        var root = new Control { Name = "SystemMenu" };
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // Same job as DrawerHost's own dim veil: darken the screen AND absorb the click before it
        // can reach the world/HUD underneath (Stop filter — Godot skips 2D physics picking once
        // GUI input already consumed the event). No click-out-to-close here, deliberately: a pause
        // menu should not vanish because of a stray click past its edge.
        var dim = new ColorRect
        {
            Name = "SystemMenuDim", Color = new Color(0f, 0f, 0f, 0.55f), MouseFilter = MouseFilterEnum.Stop,
        };
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        root.AddChild(dim);

        var center = new CenterContainer { Name = "SystemMenuCenter" };
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        root.AddChild(center);

        var card = new PanelContainer { Name = "SystemMenuCard", CustomMinimumSize = new Vector2(420f, 0) };
        card.AddThemeStyleboxOverride("panel", GameTheme.PanelStyleWood());
        center.AddChild(card);

        var margin = new MarginContainer { Name = "SystemMenuMargin" };
        margin.AddThemeConstantOverride("margin_left", GameTheme.Space16);
        margin.AddThemeConstantOverride("margin_right", GameTheme.Space16);
        margin.AddThemeConstantOverride("margin_top", GameTheme.Space16);
        margin.AddThemeConstantOverride("margin_bottom", GameTheme.Space16);
        card.AddChild(margin);

        var layout = new VBoxContainer { Name = "SystemMenuLayout" };
        layout.AddThemeConstantOverride("separation", GameTheme.Space16);
        margin.AddChild(layout);

        var title = new Label
        {
            Name = "SystemMenuTitle", Text = "Paused",
            HorizontalAlignment = HorizontalAlignment.Center,
            ThemeTypeVariation = GameTheme.HeaderThemeType,
        };
        title.AddThemeColorOverride("font_color", GameTheme.HeaderColor);
        title.AddThemeFontSizeOverride("font_size", GameTheme.HeaderFontSize);
        layout.AddChild(title);

        _systemMenuList = new VBoxContainer { Name = "SystemMenuList" };
        _systemMenuList.AddThemeConstantOverride("separation", GameTheme.Space12);
        layout.AddChild(_systemMenuList);

        var resume = new Button { Name = "Resume", Text = "Resume", CustomMinimumSize = new Vector2(0, 44) };
        StylePrimaryVerb(resume); // the default verb of a pause menu is to un-pause
        resume.Pressed += CloseSystemMenu;
        _systemMenuList.AddChild(resume);

        var settingsButton = new Button
        {
            Name = "SystemMenuSettings", Text = "Settings", CustomMinimumSize = new Vector2(0, 44),
        };
        settingsButton.Pressed += () =>
        {
            _systemMenuList.Visible = false;
            _systemMenuSettings.Refresh(); // the live window mode may have moved since Build()
            _systemMenuSettings.Visible = true;
        };
        _systemMenuList.AddChild(settingsButton);

        // Never a dead click (repo convention, BountyPanel's own gate precedent): both of these
        // are ALWAYS safe to press the moment the menu is reachable at all — Adapter.CurrentState
        // always exists once MainUi has mounted, and the menu itself can only ever be open when no
        // minigame owns an un-queued gesture (see the class doc on _Input's bottom rung), so
        // neither button needs a disabled state or a reason.
        var saveQuit = new Button
        {
            Name = "SaveQuitToTitle", Text = "Save & quit to title", CustomMinimumSize = new Vector2(0, 44),
        };
        saveQuit.Pressed += SaveAndReturnToTitle;
        _systemMenuList.AddChild(saveQuit);

        var quitGame = new Button { Name = "QuitGame", Text = "Quit game", CustomMinimumSize = new Vector2(0, 44) };
        quitGame.Pressed += SaveAndQuit;
        _systemMenuList.AddChild(quitGame);

        _systemMenuSettings = new SettingsPanel();
        _systemMenuSettings.Build();
        // U7: the in-game host is the only one that can answer "is quick-travel unlocked" — the
        // title screen's own SettingsPanel instance (NewGameSelect) never sets this, and a null
        // probe reads as locked (see the property's own doc), which is correct there: no campaign,
        // no tutorial, no unlock. Tutorial already exists by this point in BuildUi (constructed
        // above, well before the system menu).
        _systemMenuSettings.QuickTravelUnlockedProbe = () => Tutorial.QuickTravelUnlocked;
        _systemMenuSettings.Visible = false;
        _systemMenuSettings.Closed += () =>
        {
            _systemMenuSettings.Visible = false;
            _systemMenuList.Visible = true;
        };
        layout.AddChild(_systemMenuSettings);

        return root;
    }

    /// <summary>Opens the system menu, always resetting to its top-level button list — never
    /// leaves it mid-Settings from whatever it was showing the last time it closed.</summary>
    private void OpenSystemMenu()
    {
        _systemMenuSettings.Visible = false;
        _systemMenuList.Visible = true;
        _systemMenu.Visible = true;
    }

    private void CloseSystemMenu() => _systemMenu.Visible = false;

    /// <summary>
    /// "Save &amp; quit to title" (U4): save the live campaign, then hand off to the title screen
    /// through the exact same seam <see cref="NewGameSelect"/> hands off TO <see cref="MainUi"/>
    /// (<see cref="SceneChange"/>/<c>ChangeSceneToFile</c>). Clearing <see cref="AdapterOverride"/>
    /// is defensive (KTD-E) — <see cref="_Ready"/> already consumes and clears it on the way IN, so
    /// this is normally a no-op — but the title screen builds its own campaign fresh regardless of
    /// anything left over in that static hand-off field, and a defensive clear costs nothing.
    /// </summary>
    private void SaveAndReturnToTitle()
    {
        CampaignSave.Save(Adapter.CurrentState);
        AdapterOverride = null;
        CloseSystemMenu();

        // The window is about to show the title screen, which has no in-flight state that could
        // make a bare OS-close unsafe — hand close-request handling back to the engine default
        // (see _Ready's own comment) so a window left open there still closes the ordinary way.
        GetTree().AutoAcceptQuit = true;

        if (SceneChange is not null)
        {
            SceneChange(TitleScenePath);
        }
        else
        {
            GetTree().ChangeSceneToFile(TitleScenePath);
        }
    }

    /// <summary>
    /// The system menu's "Quit game" AND the OS window's own close request (<see
    /// cref="_Notification"/>) both funnel through here (KTD-D): save first, then quit — so
    /// Continue is never staler than the moment the player actually stopped. <see
    /// cref="QuitOverride"/> lets a test observe that this routing fired without tearing down the
    /// test process via a real <see cref="SceneTree.Quit()"/>.
    /// </summary>
    private void SaveAndQuit()
    {
        CampaignSave.Save(Adapter.CurrentState);

        if (QuitOverride is not null)
        {
            QuitOverride();
            return;
        }

        GetTree().AutoAcceptQuit = true;
        GetTree().Quit();
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

    /// <summary>
    /// U15/U21/U22 (KTD3/AE1/R7): real drawer/interior/modal state engages <see
    /// cref="PhaseClock.Engaged"/> — the bare world (no drawer open, no interior room entered, no
    /// modal visible) is the only flowing surface; any open drawer (<see cref="DrawerHost.IsOpen"/>),
    /// the walkable interior room (<see cref="Town2D.InteriorActive"/>, U4), or modal overlay
    /// (Ledger/Camp/Mirror) engages the latch so an expired phase timer holds at the boundary
    /// instead of ticking.
    ///
    /// <para>Doc note (found while sweeping U4's InteriorStage removal): this summary previously sat
    /// orphaned a few members away, attached to nothing — moved here, onto the method it actually
    /// describes.</para>
    /// </summary>
    private void UpdateEngaged()
    {
        // Split out from `engaged` because the two cases leave the screen in different shapes: the side
        // drawer occupies a fixed right-hand column, while an interior or a modal covers the middle of the
        // window with no reliable free strip. Only the first one leaves anywhere to put the tutorial card.
        var modalOwnsTheScreen = ModalOwnsTheScreen(); // U1: shared with SoundTheTick's departure gate
        var engaged = Drawer.IsOpen || modalOwnsTheScreen;

        // "Does something cover the screen?" and "may the player walk?" are DIFFERENT QUESTIONS, and
        // conflating them froze the player inside every room.
        //
        // A walkable interior answers YES to the first (hold the clock, hide the advisor chip — it does
        // cover the screen) and YES to the second as well (it is a playable space; walking around IS the
        // feature). A drawer or an overlay answers YES and NO. One predicate cannot say both, so there
        // are two below.
        //
        // What this cost, from the 2026-08-03 playtest: "I am unable to move around inside the forge",
        // "Unable to leave the forge via E or moving - escape worked", "i was unable to post as i
        // couldn't leave the forge so stuck on tutorial 3". Introduced by #349 (cb5e7c1) when
        // Town.InteriorActive joined ModalOwnsTheScreen so the Escape rung and the bell's departure gate
        // would treat a room like a modal — correct for both of those, and silently fatal here.
        //
        // The room's exit is a zone you WALK ONTO (InteriorRoom2D's ExitZone sits on the door tile), so a
        // frozen player cannot leave by design; Escape survived only because it is a UI rung in _Input.
        // Station clicks survived too, via Area2D physics picking, which is why the room looked
        // half-alive: menus opened, legs did not work.
        //
        // Note the third occurrence of one lesson: see AnOverlayOwnsTheScreen's own doc, which already
        // records this predicate being wrong for the Escape rung. Fixing it for one consumer and leaving
        // the others is what happened twice. If a fourth consumer appears, ask which of the two questions
        // it is really asking.
        var inRoom = Town.InteriorActive;
        var worldInputBlocked = Drawer.IsOpen || AnOverlayOwnsTheScreen();

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
        // A ROOM keeps the card too, for the same reason the drawer does. The 2026-08-03 playtest reported
        // "The tutorial is missing?" while stuck on step 3 — it was not missing, it was ACTIVE and hidden
        // by this line, because a room made `modalOwnsTheScreen` true and gave `Drawer.IsOpen` no chance
        // to be. Worst case of all: the instruction disappears exactly while the player is inside the
        // building it told them to enter, so they cannot tell whether they already did the thing.
        //
        // Overlays still hide it, which is what the DRAWER ONLY note below was right about — Ledger/Camp/
        // Mirror carry their own copy and fill the middle, so there is nowhere to move the card to.
        // HudBoundsTests.LedgerOpen_ObjectiveChip_NeverCoversLedger pins that and still passes: an overlay
        // is excluded here, a room is not.
        var keepTutorialReadable = Tutorial.Active && (Drawer.IsOpen || inRoom) && !AnOverlayOwnsTheScreen();
        Objective.Visible = !engaged || keepTutorialReadable;
        DockObjectiveHorizontally(toLeftEdge: keepTutorialReadable);

        // Same predicate for the journey dock. It was governed by PHASE alone, so during
        // Expedition/Camp/Deep it slid in over whatever the player had opened — a rendered playtest caught it
        // sitting on top of the Depths panel and obscuring the Gloomwood card. Redundant as well as
        // overlapping: the Depths panel is showing that same party in more detail.
        Pip.Suppressed = engaged;

        // T8: a drawer or an overlay owns input — the world's own click-to-move/interact must not fight
        // it for the same clicks underneath. A ROOM does not: see worldInputBlocked's own note above for
        // what using `engaged` here cost.
        Town.SetWorldInputEnabled(!worldInputBlocked);

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
