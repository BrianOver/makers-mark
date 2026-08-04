using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using GameSim.Professions;
using Godot;
using GodotClient.Panels;
using GodotClient.Ui;

namespace GodotClient.Town2d;

/// <summary>
/// U1: the 2.5D Stardew-style town — same public contract <c>town3d.Town3D</c> exposes (the pivot
/// plan's "THE Town2D CONTRACT"), so <c>MainUi</c>'s U2 cutover is a single constructor-line swap.
/// Presentation-only (KTD2): every method here reads <see cref="Adapter"/>'s already-computed
/// <see cref="GameState"/> and writes ONLY render-facing node properties — no sim rule lives in
/// this file, no RNG, no wall-clock (KTD4/KTD5).
///
/// <para><b>Node graph</b> (pivot plan §"Node architecture"): this <see cref="Control"/> hosts one
/// <see cref="SubViewportContainer"/> (nearest-filtered) wrapping a fixed 640×360 <see
/// cref="SubViewport"/> (pixel-snapped) whose <see cref="World"/> holds a non-sorted <see
/// cref="Ground"/> tile layer, a <see cref="YSort"/> container (buildings + player + heroes — the
/// whole 2.5D depth illusion), an <see cref="Fx"/> layer for forge VFX, a dusk <see
/// cref="CanvasModulate"/>, and the follow <see cref="Cam"/>. Every pixel/filter/snap flag is set
/// HERE at runtime in <see cref="Build"/> — <c>project.godot</c> is deny-listed and (per the plan)
/// carries no [rendering]/[display] section for this to lean on.</para>
///
/// <para><b>Reconciliation note for U2/the orchestrator</b> — MainUi's <c>OnTownBuildingClicked</c>
/// switch (MainUi.cs:1364-1371) currently matches Town3D's CAPITALIZED <c>ClickKey</c> strings
/// ("Forge"/"Shop"/"Tavern"/"Gate"/"Bounties"). <see cref="Building2D"/> (U3) emits its single flat
/// <c>Key</c> string verbatim (confirmed by <c>Building2DInteractionTests.RaisePick_FiresPickedWithKey</c>,
/// which asserts the LOWERCASE key round-trips), and this class's <see cref="TownLayout2D"/> table
/// uses Town3D's lowercase <c>Building3D.Key</c> vocabulary ("forge"/"market"/"tavern"/"minegate"/
/// "noticeboard") for both <see cref="FindBuilding"/> and the re-emitted <see cref="BuildingClicked"/>
/// event — there is no separate capitalized click-key in the 2D town. U2 must lower-case (and
/// rename Shop/Gate/Bounties→market/minegate/noticeboard) MainUi's switch cases, not the other way
/// around — changing <see cref="TownLayout2D"/>'s keys would break <see cref="FindBuilding"/>
/// parity with Town3D's own lookup vocabulary that the plan explicitly asks this class to mirror.</para>
///
/// <para><b>HeroActor2D state</b> — unlike <c>HeroActor3D</c> (driven by <c>Town3D</c> calling
/// <c>Advance(delta)</c> every frame from <em>this</em> class), <see cref="HeroActor2D"/> owns its
/// own <c>_Process</c> and exposes a live <see cref="HeroActor2D.State"/> getter, so <see
/// cref="OnPhaseCompleted"/>'s choreography reads it directly (no shadow bookkeeping needed here)
/// — <c>WalkingOut</c>→<c>Away</c> and <c>WalkingIn</c>→<c>Wandering</c> transitions happen
/// autonomously inside <see cref="HeroActor2D"/> on arrival.</para>
/// </summary>
public partial class Town2D : Control
{
    // Nominal SubViewport size. The live size is owned by the SubViewportContainer (Stretch=true
    // with StretchShrink=2 — see Build), which sets it to window/2, so at the default 1152x648
    // window the real canvas is 576x324, close to these numbers but not equal to them. Kept as the
    // reference framing for the overview-capture math + tests.
    public const int ViewportWidth = 640;
    public const int ViewportHeight = 360;

    /// <summary>
    /// Camera zoom. 1, deliberately: the Stardew-close framing is produced by the container's
    /// <c>StretchShrink</c> (see <see cref="Build"/>), which is a TRUE low-res canvas upscaled by an
    /// integer. Magnifying with camera zoom instead renders the world at full window resolution and
    /// merely draws it big — smooth-edged sprites in a pixel-art game — and stacking the two
    /// multiplied to 4x and pushed buildings off the top of the screen. One magnification dial only.
    /// </summary>
    private const float CameraZoom = 1f;

    /// <summary>
    /// How much of the world, in PIXELS OF WIDTH, should be on screen at once. 576px is 36 tiles.
    ///
    /// <para>Was 384 (24 tiles), tuned against a single screenshot. The owner played it and said "world is
    /// a little... too zoomed in now" — which is the same complaint as the earlier "buildings are too
    /// small, world limited" seen from the other side: that fix corrected the SIZE of things and
    /// overshot into standing too close to them.</para>
    ///
    /// <para>576 rather than something between: <see cref="ShrinkFor"/> must stay an INTEGER (pixel-art
    /// textures under a Nearest filter shimmer on a fractional resample), so at a 1152px-wide window the
    /// only choices either side of 3 are 2 and 4. 576 lands on shrink 2 — the single available step
    /// outward. There is no finer dial here without giving up the crisp-pixel rule.</para>
    ///
    /// <para>This, not the shrink factor, is the real design intent. Holding it fixed is what makes the
    /// framing resolution-independent. STILL NEEDS A HUMAN EYE: nobody has looked at 36 tiles yet.</para>
    /// </summary>
    public const int TargetVisibleWorldWidth = 576;

    /// <summary>
    /// The container's live <c>StretchShrink</c> — how many screen pixels one world pixel is drawn as.
    /// <see cref="FollowPlayer"/> needs it to convert a screen-space measurement back to world space.
    ///
    /// <para><b>Was a hardcoded 3, which made the framing depend on the monitor.</b> The canvas is
    /// window-size / shrink, so a fixed shrink means a bigger window shows MORE WORLD rather than the
    /// same world larger: 24 tiles across at 1152x648, 40 at 1080p, 53 at 1440p. Everything would look
    /// progressively tinier the better your display, which is very likely why "buildings are too small,
    /// world limited" survived the first framing fix — that fix was verified at 1152x648 and nowhere
    /// else.</para>
    ///
    /// <para>Derived from the viewport now, so tiles-on-screen stays put. Kept an INTEGER because these
    /// are pixel-art textures under a Nearest filter: a fractional shrink resamples and shimmers, which
    /// is worse than being a tile or two off the ideal framing. Floored at 2 so a small window cannot
    /// end up at 1 (no upscale at all, hairline pixels).</para>
    /// </summary>
    /// Initialised from <see cref="ShrinkFor"/> at the project's default window width rather than a literal:
    /// a hardcoded default silently disagreed with <see cref="TargetVisibleWorldWidth"/> the moment that
    /// constant moved, which is the exact drift <see cref="ShrinkFor"/> exists to prevent. Only a fallback —
    /// <c>ApplyCanvasShrink</c> replaces it from the live viewport on the first resize.
    public int CanvasShrink { get; private set; } = ShrinkFor(DefaultWindowWidth);

    /// <summary>The window width the project boots at (<c>project.godot</c>'s viewport width), used only to
    /// seed <see cref="CanvasShrink"/> before the first resize reports a real one.</summary>
    private const float DefaultWindowWidth = 1152f;

    /// <summary>The integer shrink that puts <see cref="TargetVisibleWorldWidth"/> closest to on screen
    /// for a window <paramref name="screenWidth"/> px wide. Pure, so a test can check the ladder without
    /// resizing anything.</summary>
    public static int ShrinkFor(float screenWidth) =>
        Math.Max(2, (int)MathF.Round(screenWidth / TargetVisibleWorldWidth));

    private const int TileSize = TownLayout2D.TileSize;

    /// <summary>Party-file rally spacing (px) along X — mirrors <c>Town3D.RallySpotFor</c>'s spread
    /// so a departing party reads as a cluster, not a stack.</summary>
    private const float RallySpacingPx = 14f;

    /// <summary>How long the camera lingers on the gate when a party leaves. Long enough to read the
    /// moment, short enough that it never feels like the game took the controls away — the player can
    /// walk throughout, and the camera returns to wherever they actually got to.</summary>
    private const float MineGateFocusSeconds = 3.2f;

    /// <summary>Where a focus beat is pointing, or null when the camera belongs to the player.</summary>
    private Vector2? _focusTarget;

    /// <summary>Seconds left on the current focus beat; 0 or less means the player has the camera.
    /// Accumulated-delta, matching every other timer in this file — no engine Tween anywhere here.</summary>
    private float _focusRemaining;

    public SubViewportContainer ViewportContainer { get; private set; } = null!;
    /// <summary>Named <c>WorldViewport</c> rather than <c>Viewport</c> to avoid shadowing the
    /// Godot <see cref="Godot.Viewport"/> TYPE (needed unqualified below for <see
    /// cref="Godot.Viewport.DefaultCanvasItemTextureFilter"/>) with an instance property of the
    /// same name within this class's scope.</summary>
    public SubViewport WorldViewport { get; private set; } = null!;
    public Node2D World { get; private set; } = null!;
    public TileMapLayer Ground { get; private set; } = null!;
    public Node2D YSort { get; private set; } = null!;
    public Node2D BuildingsRoot { get; private set; } = null!;
    public Node2D HeroesRoot { get; private set; } = null!;
    public Node2D TownsfolkRoot { get; private set; } = null!;
    public Node2D Fx { get; private set; } = null!;
    public CanvasModulate DuskModulate { get; private set; } = null!;
    public Camera2D Cam { get; private set; } = null!;
    public PlayerController2D Player { get; private set; } = null!;
    public WorldInput2D WorldInputNode { get; private set; } = null!;

    /// <summary>Re-emits <see cref="Building2D.Picked"/> — the click-key vocabulary (see this
    /// class's own doc for the U2 lower-case reconciliation note).</summary>
    public event Action<string>? BuildingClicked;

    /// <summary>Re-emits <see cref="HeroActor2D.Picked"/> — hero id.</summary>
    public event Action<int>? HeroClicked;

    /// <summary>U1 (painted-interiors plan): re-emits <see cref="InteriorRoom2D.StationActivated"/>
    /// — the WHOLE <see cref="InteriorLayout2D.StationSpec"/> (U3: Action/Focus/HoverLine/FlavorLine
    /// together), mirroring how <see cref="BuildingClicked"/> re-emits <see cref="Building2D.Picked"/>
    /// for town buildings. <c>MainUi</c> subscribes this onto its own <c>OnStationActivated</c>,
    /// which routes a real verb (with its optional Focus) through the existing
    /// <c>OnInteriorHotspotActivated</c> or shows an honest flavor toast.</summary>
    public event Action<InteriorLayout2D.StationSpec>? StationActivated;

    /// <summary>U4 (painted-interiors plan): raised at the END of <see cref="ExitInterior"/> — the
    /// ONE method both room-exit paths (Esc, and the door <see cref="InteriorRoom2D.ExitZone"/>)
    /// already funnel through, so this fires regardless of which one the player used. Replaces the
    /// deleted <c>InteriorStage.Exited</c>'s wiring: <c>MainUi</c> subscribes this straight onto its
    /// existing <c>OnInteriorExited</c> (re-syncs the engaged latch, fires any deferred departure
    /// focus beat) — without it, entering the room engaging <see cref="MainUi"/>'s modal latch
    /// (via <see cref="InteriorActive"/>) would have nothing to disengage it again on the way out.</summary>
    public event Action? InteriorExited;

    /// <summary>U10 (world-and-interiors plan, KTD-5): raised the instant a queued survivor
    /// group's show floor (<see cref="MinDelveShowSeconds"/>) elapses and its staggered walk-in
    /// actually begins — never at the moment <see cref="ReturnSurvivors"/> first queues it. The
    /// venue id is the SAME one <see cref="ExpeditionResult.VenueId"/> carries (KTD-4's
    /// venue-true precedent), so a subscriber can name where the party is coming FROM.
    /// <c>MainUi</c> subscribes this onto the exact deferred-focus plumbing its own departure beat
    /// (<see cref="SoundTheTick"/>/<c>_pendingMineGateFocus</c>) already established, plus the
    /// narrator toast — see that subscription's own doc for why reusing rather than duplicating
    /// the modal-deferral rule is the correct move (#335).</summary>
    public event Action<string>? PartyEmerging;

    /// <summary>U1: true while the player is inside a walkable interior room (KTD-1, island
    /// placement) rather than the bare town. Gates the mine-gate departure focus beat (<see
    /// cref="FocusOnMineGate"/>) — everything else (drawer/modal engagement, the objective chip)
    /// is unaffected: the room IS the world, not an overlay (KTD-4).</summary>
    public bool InteriorActive { get; private set; }

    /// <summary>The venue key of the currently-entered room, or null outside one (test/MainUi
    /// visibility).</summary>
    public string? InteriorVenueKey { get; private set; }

    /// <summary>Warm constant modulate for the room's interior (U1 slice-1 answer to "the room
    /// would otherwise read purple" — <see cref="DuskModulate"/> tints the WHOLE viewport, and the
    /// room/town are never on screen at once, so overriding it while <see cref="InteriorActive"/>
    /// reads identically to a per-subtree tint would without needing a second <see
    /// cref="CanvasModulate"/> — Godot supports at most one per canvas). Flagged for the owner's
    /// eye at the U1 receipt (plan Open Question 1); U2/a later pass may make this phase-aware.</summary>
    private static readonly Color InteriorWarmTint = new(1.05f, 0.92f, 0.78f);

    private readonly Dictionary<string, InteriorRoom2D> _interiorRooms = new();

    /// <summary>The adapter <see cref="Build"/> was given. Null only before <see cref="Build"/> has
    /// run.</summary>
    public SimAdapter? Adapter { get; private set; }

    /// <summary>T8-parity no-op setter (the deleted <c>Town3D</c>'s own <c>Clock</c> doc applied
    /// verbatim here — no per-frame decoration in this slice keys off clock state).</summary>
    public PhaseClock? Clock { set { } }

    private readonly Dictionary<string, Building2D> _buildingsByKey = new();
    private readonly Dictionary<int, HeroActor2D> _heroActors = new();

    /// <summary>Actors mid-rally-dwell, each with the seconds remaining until <see cref="_Process"/>
    /// fires their <see cref="HeroActor2D.MarchOutTo"/> — see <see cref="DepartWanderingHeroes"/>'s
    /// doc for why this cascade lives HERE rather than inside <see cref="HeroActor2D"/> itself.</summary>
    private readonly List<(HeroActor2D Actor, float RemainingSeconds)> _pendingMarchOut = new();

    /// <summary>How long a party dwells at the rally point before peeling off toward the gate
    /// (<c>HeroActor3D.RallyDwellSeconds</c>, ported verbatim).</summary>
    private const float RallyDwellSeconds = 1.0f;

    /// <summary>How far apart (seconds) successive party members peel off toward the gate
    /// (<c>Town3D.FileExitStaggerSeconds</c>, ported verbatim).</summary>
    private const float FileExitStaggerSeconds = 0.35f;

    /// <summary>
    /// U10 (world-and-interiors plan, KTD-5): the minimum real seconds a survivor must have stood
    /// at <see cref="HeroActor2D.HeroTownState.Away"/> before <see cref="ReturnSurvivors"/>'s
    /// queued group may begin its emergence walk-in — the show floor that stops "why did the
    /// heroes come back to the town visually?" from recurring.
    ///
    /// <para>An unstaged (target floor 1) expedition resolves and reveals
    /// <c>PendingExpeditions</c> on the SAME <c>AdvancePhase</c> tick that ends the Expedition
    /// phase (<c>ExpeditionSystem.cs:84-106</c>) — often before the party has even finished
    /// marching to the gate the PREVIOUS tick queued. Without a floor, <see
    /// cref="ReturnSurvivors"/> firing <see cref="HeroActor2D.ReturnTo"/> in that same call reads
    /// as a teleport: the departure animation had zero seconds to register. This constant is
    /// measured against <em>each actor's own</em> accumulated Away time (<see
    /// cref="_awaySeconds"/>), not against when the tick fired — a staged (Camp) return has
    /// already accumulated far more than this by the time it resolves days later, so the floor is
    /// invisible there by construction (it can only ever hold up the fast, same-day path).</para>
    ///
    /// <para>8s: long enough that the PiP dock/Mine Watch strip the party mustered into is
    /// actually readable for a beat before they reappear, short enough it never reads as the game
    /// stalling on an empty town.</para>
    /// </summary>
    public const float MinDelveShowSeconds = 8f;

    /// <summary>How long (real seconds, accumulated delta — KTD4/KTD5, no wall-clock reads) each
    /// hero id has continuously held <see cref="HeroActor2D.HeroTownState.Away"/> — what <see
    /// cref="MinDelveShowSeconds"/> is measured against. <see cref="_Process"/> accumulates this
    /// every frame for actors currently Away and drops the entry the instant an actor leaves Away
    /// (arrival home, a forced <see cref="SnapRemainingHeroesHome"/>), so a hero's NEXT expedition
    /// later in the campaign starts this clock fresh rather than inheriting today's total.</summary>
    private readonly Dictionary<int, float> _awaySeconds = new();

    /// <summary>One <see cref="ReturnSurvivors"/> batch — one per finalized <c>ExpeditionResult</c>
    /// with survivors — awaiting its emergence beat (KTD-5's show floor). Grouped per expedition
    /// (not merged into one town-wide list) so the narrator line and staggered walk-in only ever
    /// describe ONE venue at a time (KTD-4's venue-true precedent). Deliberately NOT persisted
    /// anywhere (KTD-5: "the floor only stretches presentation inside a live session, never
    /// state") — a reload rebuilds every actor fresh via <see cref="ReconcileHeroes"/>'s ordinary
    /// Wandering default (see that method's own doc), so a hold that never resolved before a
    /// reload cannot leave anyone stuck invisible; it simply never existed for the new session.</summary>
    private sealed class PendingReturn
    {
        public required List<int> HeroIds;
        public required string VenueId;
    }

    private readonly List<PendingReturn> _pendingReturns = new();

    /// <summary>Hero ids already queued in <see cref="_pendingReturns"/> — <see
    /// cref="ReturnSurvivors"/> is re-invoked at BOTH the Expedition and ExpeditionDeep phase
    /// completions (both read the same durable <c>GameState.PendingExpeditions</c>, cleared only
    /// by the Evening reveal), so without this a survivor already queued — or already walking
    /// home — would be queued a second time by the later call.</summary>
    private readonly HashSet<int> _queuedReturnHeroIds = new();

    /// <summary>Staggered emergence walk-ins actually in flight — the return-side twin of <see
    /// cref="_pendingMarchOut"/>, firing <see cref="HeroActor2D.ReturnTo"/> (not
    /// <see cref="HeroActor2D.MarchOutTo"/>) once each entry's delay elapses. A separate queue
    /// rather than reusing <see cref="_pendingMarchOut"/> because the two run in opposite
    /// directions and a departure mid-file-out overlapping a return mid-file-in is a real
    /// (if rare) scenario this file must not conflate.</summary>
    private readonly List<(HeroActor2D Actor, float RemainingSeconds, Vector2 GateAnchor)> _pendingReturnWalk = new();

    private Building2D? _forgeBuilding;
    private Sprite2D? _forgeGlowOverlay;
    private CpuParticles2D? _forgeSparks;
    private CpuParticles2D? _forgeSteam;
    private AmbientLife2D? _ambientLife;

    /// <summary>U5 (world-and-interiors plan, KTD-8): the market room's customer choreography —
    /// null only if the "market" row is ever removed from <see cref="InteriorLayout2D.Rooms"/>
    /// (defensive; every shipped build has it). Mounted directly under <see cref="YSort"/> in
    /// <see cref="BuildInteriorRooms"/> (the SAME flat, non-Y-sort-enabled wrapper role
    /// <see cref="TownsfolkRoot"/> plays), fed this tick's events every <see cref="Refresh"/> —
    /// see <see cref="MarketLife2D.QueueDay"/>'s own doc for why it must be THIS tick's events
    /// only, never the whole log.</summary>
    private MarketLife2D? _marketLife;
    /// <summary>U7 (world-and-interiors plan, KTD-3): the venue key the shared workshop shell
    /// always answers to (deliberately never renamed — <c>MainUi</c> routing, quick-travel, and
    /// the tutorial's <c>StepBuilding</c> all key off this string; only the vocabulary/dressing
    /// swap by profession).</summary>
    private const string WorkshopVenueKey = "forge";

    /// <summary>
    /// PRIMARY-FIRST profession order for the workshop's vocabulary (<see cref="WorkshopVocab"/>).
    /// Captured once in <see cref="Build"/> from <c>Adapter.CurrentState.Player.SelectedProfessions</c>
    /// — a sorted SET with no chronological memory of its own (KTD2: nothing in <c>Contracts</c>
    /// tracks "which was picked first") — and then only ever APPENDED to (see <see
    /// cref="RebuildWorkshopIfStale"/>), never recomputed from scratch, so the profession a
    /// campaign actually started with stays primary for the rest of the session even after a
    /// second one is added mid-run.
    ///
    /// <para>A fresh campaign's <see cref="Build"/> call always sees exactly one selected
    /// profession (the pick <c>NewGameSelect</c> made), so this is exact in the common case. A
    /// RESUMED save that already held two professions before this <see cref="Town2D"/> instance
    /// ever existed has no historical order to recover — see <see
    /// cref="ResolveInitialWorkshopOrder"/>'s own doc for that fallback.</para>
    /// </summary>
    private IReadOnlyList<string> _workshopProfessionOrder = Array.Empty<string>();

    /// <summary>The exact profession set the currently-mounted workshop room/building dressing
    /// reflects — compared against the live sim state in <see cref="RebuildWorkshopIfStale"/> so a
    /// profession added mid-run rebuilds the room on the player's NEXT entry, per this unit's own
    /// "rooms are built once at startup" structural fix.</summary>
    private ImmutableSortedSet<string> _workshopBuiltFor = ImmutableSortedSet<string>.Empty;

    /// <summary>The workshop's current player-facing nametag (<see cref="WorkshopVocab.NametagFor"/>
    /// over <see cref="_workshopProfessionOrder"/>) — read by <c>MainUi</c> for the drawer title and
    /// pushed into <c>TutorialFlow</c> so neither surface ever derives it independently (the
    /// vocabulary-seam risk #339 already fixed once).</summary>
    public string WorkshopNametag => WorkshopVocab.NametagFor(_workshopProfessionOrder);

    /// <summary>The workshop's current tutorial station noun ("anvil"/"cauldron"/...) — same
    /// single-source rule as <see cref="WorkshopNametag"/>.</summary>
    public string WorkshopStationNoun => WorkshopVocab.StationNounFor(_workshopProfessionOrder);

    /// <summary>U6 (world-and-interiors plan): every spawned villager, so <see cref="_Process"/>
    /// can feed each one the current <see cref="DayPhase"/> (mirrors <see
    /// cref="_ambientLife"/>'s own <c>SetPhase</c> cadence) — kept alongside <see
    /// cref="TownsfolkRoot"/> rather than re-querying its children every frame.</summary>
    private readonly List<TownsfolkNpc2D> _townsfolk = new();

    /// <summary>U6: patron seating inside the tavern room — null only if the tavern has no
    /// <see cref="InteriorLayout2D"/> row (defensive; every real build has one) or its "Patron
    /// Table" stations were renamed out from under <see cref="WireTavernLife"/>.</summary>
    private TavernLife2D? _tavernLife;

    /// <summary>Gap #4 fix ("day/night is a lie"): eases <see cref="DuskModulate"/>'s tint toward
    /// whatever <see cref="Adapter"/>'s current <see cref="DayPhase"/> calls for, every frame (see
    /// <see cref="_Process"/>) — seeded in <see cref="Build"/> at the campaign's actual starting
    /// phase tint so there is no snap-then-ease on the very first frame.</summary>
    private DayPhaseTint? _dayTint;

    private const float ForgeGlowMaxAlpha = 0.85f;

    private static GradientTexture2D? _glowTextureCache;

    /// <summary>
    /// Builds the whole town: ground tiles, buildings (from <see cref="TownLayout2D"/>), the
    /// player (spawned at the forge's door), heroes (<see cref="ReconcileHeroes"/>), and forge FX
    /// hookup. Mirrors <c>Town3D.Build</c>'s one-shot-per-instance contract.
    /// </summary>
    public void Build(SimAdapter adapter)
    {
        Adapter = adapter;
        TownInput.RegisterActions();

        // U7 (world-and-interiors plan): capture the workshop's primary-first profession order
        // ONCE, before BuildBuildings/BuildInteriorRooms read it (see _workshopProfessionOrder's
        // own doc for why this must never be recomputed from scratch later).
        var startingProfessions = adapter.CurrentState.Player.SelectedProfessions;
        _workshopProfessionOrder = ResolveInitialWorkshopOrder(startingProfessions);
        _workshopBuiltFor = startingProfessions;

        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        ViewportContainer = new SubViewportContainer
        {
            Name = "ViewportContainer",
            Stretch = true,
            // The load-bearing half of Stretch. Stretch=true means the CONTAINER owns the child
            // viewport's size — it overwrites WorldViewport.Size with (container size /
            // StretchShrink) every layout pass. StretchShrink defaults to 1, so the world was being
            // rendered at the full window resolution and then drawn 1:1: the entire "render a
            // 640x360 pixel-art canvas and upscale it" plan was silently dead, and every number
            // downstream of it (camera zoom, the Limit rect, TownLayout2D's "width deliberately
            // equals the 640px viewport so no horizontal pan is needed") had been reasoned against
            // a canvas that does not exist at runtime. That is why the town sat in the corner of a
            // sea of grass at half the intended magnification.
            //
            // 3 gives a 384x216 canvas upscaled 3x into a 1152x648 window: a real low-res canvas,
            // integer-upscaled, crisp under Nearest, showing 24x13.5 tiles of town. Tuned against a
            // screenshot — 2 (576x324) framed the town too wide to read as a place you stand in,
            // and combining 2 with the camera's old 2x zoom pushed in so far that buildings ran off
            // the top of the screen. This is the ONLY magnification dial now: CameraZoom is 1.
            StretchShrink = CanvasShrink, // recomputed for the real window size just below
            TextureFilter = TextureFilterEnum.Nearest,
        };
        ViewportContainer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(ViewportContainer);

        WorldViewport = new SubViewport
        {
            Name = "Viewport",
            Size = new Vector2I(ViewportWidth, ViewportHeight),
            // false is the idiomatic value for a container-wrapped SubViewport: let the parent
            // SubViewportContainer translate a click into viewport space and forward it, which is
            // what PhysicsObjectPicking below needs to pick anything.
            //
            // CORRECTION (2026-07-30). This comment used to declare the false "load-bearing" and
            // blame HandleInputLocally=true for the 2026-07-29 playtest finding that no building
            // could be clicked. That causal story is NOT TRUE, and it was never tested — it was
            // reasoned from the docs and then written down as fact. RealClickReachesBuildingTests
            // now pushes a real click at a real screen position through this whole chain, and it
            // passes with this flag set EITHER way. So whatever actually broke clicking that day,
            // it was not this line.
            //
            // Left as false regardless (it is correct for this node layout), but the claim is gone.
            // The guarantee now comes from a test that can fail, not from a confident comment —
            // which is the same lesson as Building2D.RaisePick(): the old coverage entered buildings
            // through a seam that skipped viewport input entirely, so it proved the routing worked
            // while the only path a player has was dead.
            HandleInputLocally = false,
            PhysicsObjectPicking = true,
            Snap2DTransformsToPixel = true,
            Snap2DVerticesToPixel = true,
            CanvasItemDefaultTextureFilter = Viewport.DefaultCanvasItemTextureFilter.Nearest,
        };
        ViewportContainer.AddChild(WorldViewport);
        ApplyCanvasShrink();
        Resized += ApplyCanvasShrink; // a resized window must re-derive, or the framing drifts again

        World = new Node2D { Name = "World" };
        WorldViewport.AddChild(World);

        Ground = BuildGround();
        World.AddChild(Ground);

        YSort = new Node2D { Name = "YSort", YSortEnabled = true };
        World.AddChild(YSort);

        BuildingsRoot = new Node2D { Name = "Buildings" };
        YSort.AddChild(BuildingsRoot);
        BuildBuildings();
        BuildProps();
        BuildInteriorRooms(); // U1: island rooms exist off-frame from the start, same as the town

        Player = new PlayerController2D { Name = "Player" };
        YSort.AddChild(Player);
        var forgeDoor = FindBuilding("forge").DoorAnchorGlobal;
        Player.SpawnAt(forgeDoor);

        HeroesRoot = new Node2D { Name = "Heroes" };
        YSort.AddChild(HeroesRoot);

        TownsfolkRoot = new Node2D { Name = "Townsfolk" };
        YSort.AddChild(TownsfolkRoot);
        BuildTownsfolk();
        WireTavernLife(); // U6: needs BuildInteriorRooms' tavern row + stations, already built above

        Fx = new Node2D { Name = "Fx" };
        World.AddChild(Fx);

        // Gap #4 fix: seed the modulate (and the eased driver) at the CURRENT phase's own tint —
        // a campaign resumed mid-Evening starts already purple-dusk, not snapping from some
        // unrelated default.
        var startingTint = DayPhaseTint.TintFor(adapter.CurrentState.Phase);
        DuskModulate = new CanvasModulate { Name = "DuskModulate", Color = startingTint };
        World.AddChild(DuskModulate);
        _dayTint = new DayPhaseTint(startingTint);

        Cam = new Camera2D
        {
            Name = "Cam",
            Zoom = new Vector2(CameraZoom, CameraZoom), // INTEGER zoom only — never fractional (pixel shimmer)
            PositionSmoothingEnabled = true,
            PositionSmoothingSpeed = 8f,
            LimitLeft = 0,
            LimitTop = 0,
            LimitRight = TownLayout2D.GridWidth * TileSize,
            LimitBottom = TownLayout2D.GridHeight * TileSize,
        };
        World.AddChild(Cam);
        Cam.MakeCurrent();
        Cam.GlobalPosition = forgeDoor;
        Cam.ResetSmoothing();

        WorldInputNode = new WorldInput2D { Name = "WorldInput2D" };
        World.AddChild(WorldInputNode);
        WorldInputNode.Configure(Player, _buildingsByKey.Values.ToList());

        WireForgeFx();
        WireAmbientLife();

        // T8-parity: populate Heroes from the adapter's initial state now; Refresh() re-runs this
        // every tick once MainUi wires it up (U2).
        Bind(adapter);
        RefreshTavernLife(); // U6: seat present heroes immediately — don't wait for the first tick's Refresh()
    }

    /// <summary>T8-parity drop-in for the old <c>Bind(SimAdapter)</c> call site (see
    /// <c>Town3D.Bind</c>'s own doc — <see cref="Build"/> already assigned <see cref="Adapter"/>
    /// and reconciled once, so this is a harmless re-assignment either way).</summary>
    public void Bind(SimAdapter adapter)
    {
        Adapter = adapter;
        ReconcileHeroes();
    }

    /// <summary>T8-parity drop-in for <c>Refresh()</c> — called every tick the world is visible.
    /// U5: also feeds <see cref="_marketLife"/> this tick's events — the SAME
    /// <c>Adapter.CurrentState</c>/<c>Adapter.LastEvents</c> pair <c>MainUi</c> already hands
    /// <c>Watch</c>/<c>Pip</c> every tick (see <c>MainUi.RefreshAll</c>), so a shop event can never
    /// disagree between what the HUD reports and what the market room stages. Harmless on every
    /// non-shop tick — <see cref="MarketLife2D.QueueDay"/> just stages nothing when the batch has
    /// no matching event. U6: also reseats tavern patrons off the same reconciled hero states (see
    /// <see cref="RefreshTavernLife"/> — hero presence only changes on a tick, not every render
    /// frame).</summary>
    public void Refresh()
    {
        ReconcileHeroes();
        if (Adapter is not null)
        {
            _marketLife?.QueueDay(Adapter.CurrentState, Adapter.LastEvents);
        }

        RefreshTavernLife();
    }

    /// <summary>Gates BOTH proximity/interact (<see cref="WorldInputNode"/>) and the player's own
    /// WASD/seek (<see cref="PlayerController2D.SetInputEnabled"/>) — <see
    /// cref="PlayerController2D"/> deliberately adds the latter beyond Town3D's own
    /// <c>SetWorldInputEnabled</c> (which never gated <c>PlayerController</c> movement), per its own
    /// doc comment ("matching the 3D town's veil-guard convention" — this is the 2D town actually
    /// keeping that promise).</summary>
    public void SetWorldInputEnabled(bool enabled)
    {
        WorldInputNode.Enabled = enabled;
        Player.SetInputEnabled(enabled);
    }

    /// <summary>Brightens a dedicated glow overlay near the forge (NEVER the building's own <see
    /// cref="Building2D.Sprite"/> modulate — that's <see cref="Building2D.SetHighlighted"/>'s
    /// property alone; fighting over the same <see cref="CanvasItem.Modulate"/> would be the exact
    /// "two states, one property" bug <c>Town3D</c>'s own forge-station doc warns against) in step
    /// with the live Smelt heat gauge (0-1000 permille). No-op if the station was never wired.</summary>
    public void ForgeGlow(int heatPermille)
    {
        if (_forgeGlowOverlay is null)
        {
            return;
        }

        var t = Mathf.Clamp(heatPermille / 1000f, 0f, 1f);
        var color = _forgeGlowOverlay.Modulate;
        color.A = Mathf.Lerp(0f, ForgeGlowMaxAlpha, t);
        _forgeGlowOverlay.Modulate = color;
    }

    /// <summary>Resets the glow overlay to fully transparent — called the instant the Smelt stage
    /// ends/cancels so a half-finished smelt never leaves the forge stuck glowing.</summary>
    public void ForgeGlowReset()
    {
        if (_forgeGlowOverlay is null)
        {
            return;
        }

        var color = _forgeGlowOverlay.Modulate;
        color.A = 0f;
        _forgeGlowOverlay.Modulate = color;
    }

    /// <summary>A one-shot spark burst at the forge — the on-beat forge-hit cue.</summary>
    public void ForgeSparkBurst()
    {
        if (_forgeSparks is null)
        {
            return;
        }

        _forgeSparks.Restart();
        _forgeSparks.Emitting = true;
    }

    /// <summary>A one-shot steam plume at the forge — the quench-lock cue.</summary>
    public void ForgeSteamPlume()
    {
        if (_forgeSteam is null)
        {
            return;
        }

        _forgeSteam.Restart();
        _forgeSteam.Emitting = true;
    }

    /// <summary>Look up a placed venue by <see cref="Building2D.Key"/> (e.g. "forge") — throws if
    /// <see cref="Build"/> hasn't run or the key is unknown (every real caller expects the full
    /// layout to already exist, mirrors <c>Town3D.FindBuilding</c>).</summary>
    public Building2D FindBuilding(string key) =>
        _buildingsByKey.TryGetValue(key, out var building)
            ? building
            : throw new InvalidOperationException($"No building named '{key}' in Town2D.");

    /// <summary>Look up a built interior room by venue key (test/inspection surface, mirrors <see
    /// cref="FindBuilding"/>'s own contract) — throws if <see cref="Build"/> hasn't run or the
    /// venue has no <see cref="InteriorLayout2D"/> row.</summary>
    public InteriorRoom2D FindInteriorRoom(string venueKey) =>
        _interiorRooms.TryGetValue(venueKey, out var room)
            ? room
            : throw new InvalidOperationException($"No interior room for venue '{venueKey}' in Town2D.");

    /// <summary>U6: test/inspection surface for the tavern's patron seating — null only if <see
    /// cref="WireTavernLife"/> found no tavern row/table stations (never expected on a real
    /// build).</summary>
    public TavernLife2D? TavernLife => _tavernLife;

    /// <summary>U6: test/inspection surface for the live villager list <see cref="_Process"/>
    /// feeds phase updates to (mirrors <see cref="FirstHeroActor"/>'s shape).</summary>
    public IReadOnlyList<TownsfolkNpc2D> Townsfolk => _townsfolk;

    /// <summary>
    /// U1 (KTD-1, island placement): teleports the player into <paramref name="venueKey"/>'s
    /// walkable interior room and clamps the camera to it — no town hide/show, no reparenting, no
    /// new SubViewport; the town keeps existing off-frame exactly as it does off-camera today.
    /// No-op if already inside a room, or if <paramref name="venueKey"/> has no <see
    /// cref="InteriorLayout2D"/> row (<c>MainUi.OnTownBuildingClicked</c> checks that table before
    /// calling this, so the second case is defensive only, mirroring <see cref="FindBuilding"/>'s
    /// throw-on-unknown-key contract would be too strict here — a stale/legacy building key with no
    /// room row must fall through to the drawer, not throw).
    /// </summary>
    public void EnterInterior(string venueKey)
    {
        if (InteriorActive)
        {
            return;
        }

        // U7 (world-and-interiors plan): a profession picked mid-run rebuilds the workshop room on
        // the player's NEXT entry (rooms are built once at startup — see RebuildWorkshopIfStale's
        // own doc) — checked before the room lookup below so a stale dictionary entry is never
        // teleported into.
        if (venueKey == WorkshopVenueKey)
        {
            RebuildWorkshopIfStale();
        }

        if (!_interiorRooms.TryGetValue(venueKey, out var room))
        {
            return;
        }

        InteriorActive = true;
        InteriorVenueKey = venueKey;

        Player.SpawnAt(room.DoorAnchorGlobal);

        Cam.LimitLeft = (int)room.RoomRect.Position.X;
        Cam.LimitTop = (int)room.RoomRect.Position.Y;
        Cam.LimitRight = (int)(room.RoomRect.Position.X + room.RoomRect.Size.X);
        Cam.LimitBottom = (int)(room.RoomRect.Position.Y + room.RoomRect.Size.Y);
        Cam.GlobalPosition = room.DoorAnchorGlobal;
        Cam.ResetSmoothing(); // avoid a long glide-in from wherever the camera sat in town (mirrors Build()'s own post-snap ResetSmoothing)

        WorldInputNode.Configure(Player, room.Stations); // re-point interact/highlight scanning at the room's stations
    }

    /// <summary>
    /// Reverses <see cref="EnterInterior"/>: teleports the player back to <see
    /// cref="InteriorVenueKey"/>'s building door anchor, unclamps the camera to the town's own
    /// bounds, and re-points <see cref="WorldInputNode"/> at the town's buildings. No-op outside a
    /// room (double-Esc / walking back over the exit zone twice is safe).
    /// </summary>
    public void ExitInterior()
    {
        if (!InteriorActive)
        {
            return;
        }

        var doorAnchor = FindBuilding(InteriorVenueKey!).DoorAnchorGlobal;
        InteriorActive = false;
        InteriorVenueKey = null;

        Player.SpawnAt(doorAnchor);

        Cam.LimitLeft = 0;
        Cam.LimitTop = 0;
        Cam.LimitRight = TownLayout2D.GridWidth * TileSize;
        Cam.LimitBottom = TownLayout2D.GridHeight * TileSize;
        Cam.GlobalPosition = doorAnchor;
        Cam.ResetSmoothing();

        WorldInputNode.Configure(Player, _buildingsByKey.Values.ToList());
        InteriorExited?.Invoke();
    }

    /// <summary>Live hero-actor count (test/inspection surface).</summary>
    public int HeroActorCount() => _heroActors.Count;

    /// <summary>Live cosmetic-villager count (test/inspection surface) — mirrors <see
    /// cref="HeroActorCount"/>'s shape for <see cref="TownsfolkNpc2D"/>.</summary>
    public int TownsfolkCount() => TownsfolkRoot.GetChildCount();

    /// <summary>Test/inspection surface: the market room's live customer choreography, or null
    /// before <see cref="Build"/> has run (mirrors <see cref="FindInteriorRoom"/>'s own
    /// null-before-Build contract).</summary>
    public MarketLife2D? MarketLife => _marketLife;

    /// <summary>The lowest-HeroId live actor (test/inspection surface) — deterministic even though
    /// dictionary enumeration order is an implementation detail. Throws if <see
    /// cref="ReconcileHeroes"/> has never produced any actor.</summary>
    public HeroActor2D FirstHeroActor() => _heroActors.Values.OrderBy(a => a.HeroIdValue).First();

    /// <summary>The live actor for a specific hero id (test/inspection surface), or null if that
    /// hero has no actor (never alive-and-present, or freed on death/roster removal) — the
    /// by-id twin of <see cref="FindBuilding"/> for heroes rather than venues.</summary>
    public HeroActor2D? FindHeroActor(int heroId) => _heroActors.GetValueOrDefault(heroId);

    /// <summary>U10 test/inspection surface: true while any survivor group is still waiting on the
    /// KTD-5 show floor (queued in <see cref="_pendingReturns"/>, not yet handed to <see
    /// cref="_pendingReturnWalk"/>) — i.e. <see cref="ReturnSurvivors"/> has run but nobody in the
    /// batch has begun their emergence walk-in yet.</summary>
    public bool AnyReturnPending => _pendingReturns.Count > 0;

    /// <summary>
    /// U1 (plan 2026-08-03-001, KTD-A): true while the Morning-end departure choreography is still
    /// mid-cascade — <see cref="DepartWanderingHeroes"/> has scheduled a dwell-then-file-peel-off
    /// timer in <see cref="_pendingMarchOut"/> for at least one actor that has not yet reached its
    /// own <see cref="HeroActor2D.MarchOutTo"/> call (see <see cref="TickPendingMarchOut"/>). This is
    /// <see cref="GodotClient.RaidConductor"/>'s SendOff completion condition — the departure show it
    /// waits on before performing the stage-1 tick.
    /// </summary>
    public bool AnyDeparturePending => _pendingMarchOut.Count > 0;

    /// <summary>U10 test/inspection surface: real seconds <paramref name="heroId"/> has
    /// continuously held <see cref="HeroActor2D.HeroTownState.Away"/>, or 0 if they are not
    /// currently Away — what <see cref="MinDelveShowSeconds"/> is measured against.</summary>
    public float AwaySecondsFor(int heroId) => _awaySeconds.GetValueOrDefault(heroId);

    /// <summary>
    /// Reconciles <see cref="HeroesRoot"/> against <c>Adapter.CurrentState.Heroes</c> — adds a
    /// <see cref="HeroActor2D"/> for every ALIVE hero without one yet, and removes the actor for
    /// any hero now dead or gone from the roster (mirrors <c>Town3D.ReconcileHeroes</c>, minus the
    /// memorial-plot rebuild — no memorial landmark in this slice, see the pivot plan's scope
    /// boundaries).
    /// </summary>
    public void ReconcileHeroes()
    {
        if (Adapter is null)
        {
            return;
        }

        var state = Adapter.CurrentState;

        foreach (var hero in state.Heroes.Values.Where(h => h.Alive))
        {
            if (_heroActors.ContainsKey(hero.Id.Value))
            {
                continue;
            }

            var actor = new HeroActor2D();
            var color = ClassColors.RoleColor(hero.ClassId);
            var sprite = TownAssets2D.ForHero(hero.ClassId);
            actor.Init(hero.Id.Value, hero.ClassId, color, sprite, HomeFor(hero.Id.Value));
            actor.Picked += id => HeroClicked?.Invoke(id);
            HeroesRoot.AddChild(actor);
            _heroActors[hero.Id.Value] = actor;
        }

        // Permadeath (R7) / roster-absent: free the actor for anyone no longer alive-and-present.
        foreach (var heroId in _heroActors.Keys
                     .Where(id => !state.Heroes.TryGetValue(id, out var hero) || !hero.Alive)
                     .ToList())
        {
            var actor = _heroActors[heroId];
            _heroActors.Remove(heroId);
            HeroesRoot.RemoveChild(actor);
            PanelGraveyard.Bury(actor); // detached => parentless => nothing else would ever free it
        }
    }

    /// <summary>
    /// Phase-transition choreography (mirrors <c>Town3D.OnPhaseCompleted</c>): Morning done → every
    /// wandering actor rallies then marches out the gate. Expedition/ExpeditionDeep done →
    /// survivors of any FINALIZED run walk home. Evening done → every remaining non-wandering
    /// actor whose hero is confirmed alive snaps home for the new day. Camp/unknown → no-op.
    /// </summary>
    /// <summary>
    /// Test/inspection surface: how many times the phase choreography has actually run.
    ///
    /// <para>Every immediate action used to reach this method (<c>MainUi</c> called it on the shared
    /// <c>StateChanged</c> event without checking whether a phase had completed), so Stock in Morning
    /// re-marched the party out and any action mid-raid re-walked them home. Nothing observed the
    /// difference, so nothing could pin it. This counter is what
    /// <c>ImmediateActionsDoNotReplayThePhaseTests</c> watches.</para>
    /// </summary>
    public int PhaseChoreographyRuns { get; private set; }

    public void OnPhaseCompleted(DayPhase completedPhase)
    {
        if (Adapter is null)
        {
            return;
        }

        PhaseChoreographyRuns++;

        switch (completedPhase)
        {
            case DayPhase.Morning:
                DepartWanderingHeroes();
                break;
            case DayPhase.Expedition:
            case DayPhase.ExpeditionDeep:
                ReturnSurvivors();
                break;
            case DayPhase.Evening:
                SnapRemainingHeroesHome();
                break;
            case DayPhase.Camp:
            default:
                break;
        }
    }

    /// <summary>
    /// Mirrors <c>Town3D.DepartWanderingHeroes</c>: every wandering actor walks to a spread rally
    /// spot near the gate. <see cref="HeroActor2D"/> has no internal dwell/file-stagger timer (its
    /// own class doc: "the fileDelay cascade moved out of this type"), so THIS method schedules
    /// each hero's follow-up <see cref="HeroActor2D.MarchOutTo"/> into <see cref="_pendingMarchOut"/>
    /// — <see cref="_Process"/> fires it once <see cref="RallyDwellSeconds"/> plus that hero's file
    /// position has elapsed, reproducing <c>HeroActor3D.BeginDeparture</c>'s
    /// dwell-then-peel-off-in-file cascade one level up.
    /// </summary>
    private void DepartWanderingHeroes()
    {
        var departing = _heroActors.Values
            .Where(a => a.State == HeroActor2D.HeroTownState.Wandering)
            .OrderBy(a => a.HeroIdValue)
            .ToList();

        for (var i = 0; i < departing.Count; i++)
        {
            var actor = departing[i];
            actor.RallyTo(RallySpotFor(i, departing.Count));
            _pendingMarchOut.Add((actor, RallyDwellSeconds + i * FileExitStaggerSeconds));
        }
    }

    /// <summary>Ticks <see cref="_pendingMarchOut"/>'s dwell timers (see <see
    /// cref="DepartWanderingHeroes"/>'s doc) — off the sim path, pure accumulated delta (KTD4/KTD5).
    /// Guards against firing <see cref="HeroActor2D.MarchOutTo"/> on an actor <see
    /// cref="ReconcileHeroes"/> already freed (e.g. the hero died the same tick it was mustering).
    /// Also advances <see cref="_dayTint"/> (gap #4) every frame regardless of <see
    /// cref="_pendingMarchOut"/>'s state — the sky must keep answering "what time is it" whether or
    /// not a party happens to be departing this tick.</summary>
    public override void _Process(double delta)
    {
        if (_focusRemaining > 0f)
        {
            _focusRemaining -= (float)delta;
            if (_focusRemaining <= 0f)
            {
                _focusTarget = null;
            }
        }

        FollowPlayer();

        if (Adapter is not null && _dayTint is not null)
        {
            // U1: the eased driver keeps advancing even while inside a room (so exiting resumes
            // exactly where dusk should be, no jump) — only the RENDERED color is overridden to a
            // warm constant while InteriorActive. See InteriorWarmTint's own doc for why a global
            // override reads correctly here despite DuskModulate covering the whole viewport.
            var eased = _dayTint.Advance(delta, Adapter.CurrentState.Phase);
            DuskModulate.Color = InteriorActive ? InteriorWarmTint : eased;

            // U11: lamp/window glow reads the CURRENT phase directly (no easing — a hard phase
            // change flips the lamps, only the sky color drifts) every tick, same cadence as the
            // tint driver above.
            _ambientLife?.SetPhase(Adapter.CurrentState.Phase);

            // U6: townsfolk gate NEW errands off the same phase (see TownsfolkNpc2D.IsErrandHours)
            // — a handful of nodes, cheap regardless of the per-frame cadence.
            foreach (var npc in _townsfolk)
            {
                npc.SetPhase(Adapter.CurrentState.Phase);
            }
        }

        // U10 (KTD-5): accumulate/reset each actor's Away timer every frame, regardless of
        // whether anything is currently queued for return — so a group that queues LATER already
        // carries an accurate elapsed total instead of starting the show floor's clock from zero
        // at enqueue time (see MinDelveShowSeconds' own doc for why that distinction is the fix).
        TickAwayTimers(delta);

        TickPendingMarchOut(delta);
        TickPendingReturns(delta);
        TickPendingReturnWalk(delta);
    }

    /// <summary>U10 (KTD-5): every frame, every actor currently <see
    /// cref="HeroActor2D.HeroTownState.Away"/> accrues one more tick of <see cref="_awaySeconds"/>;
    /// everyone else's entry is dropped so a hero's NEXT expedition starts the clock fresh rather
    /// than inheriting today's total (see the field's own doc).</summary>
    private void TickAwayTimers(double delta)
    {
        foreach (var actor in _heroActors.Values)
        {
            if (actor.State == HeroActor2D.HeroTownState.Away)
            {
                _awaySeconds[actor.HeroIdValue] = _awaySeconds.GetValueOrDefault(actor.HeroIdValue) + (float)delta;
            }
            else
            {
                _awaySeconds.Remove(actor.HeroIdValue);
            }
        }
    }

    private void TickPendingMarchOut(double delta)
    {
        if (_pendingMarchOut.Count == 0)
        {
            return;
        }

        var mineDoor = FindBuilding("minegate").DoorAnchorGlobal;
        for (var i = _pendingMarchOut.Count - 1; i >= 0; i--)
        {
            var (actor, remaining) = _pendingMarchOut[i];
            remaining -= (float)delta;
            if (remaining > 0f)
            {
                _pendingMarchOut[i] = (actor, remaining);
                continue;
            }

            _pendingMarchOut.RemoveAt(i);
            if (_heroActors.ContainsValue(actor))
            {
                actor.MarchOutTo(mineDoor);
            }
        }
    }

    /// <summary>
    /// U10 (KTD-5): the show-floor check — moves a queued group from <see
    /// cref="_pendingReturns"/> into <see cref="_pendingReturnWalk"/> (and fires <see
    /// cref="PartyEmerging"/>) the instant EVERY surviving member has cleared <see
    /// cref="MinDelveShowSeconds"/> of accumulated Away time (<see cref="_awaySeconds"/>).
    ///
    /// <para>Condition (a) from KTD-5 ("the phase vocabulary has left Quest") needs no runtime
    /// check here: <see cref="ReturnSurvivors"/> is only ever called from <see
    /// cref="OnPhaseCompleted"/> for the Expedition/ExpeditionDeep case, and by the time that
    /// fires <c>Adapter.CurrentState.Phase</c> has already advanced past whichever of those it
    /// was — true by construction of the ONE call site, not something this method re-derives.</para>
    ///
    /// <para>A group with nobody left to bring home (every member died or was otherwise freed
    /// before their own floor elapsed) is simply dropped — nothing to animate, and <see
    /// cref="SnapRemainingHeroesHome"/> already owns the safety net for anyone who genuinely
    /// cannot resolve here.</para>
    /// </summary>
    private void TickPendingReturns(double delta)
    {
        if (_pendingReturns.Count == 0)
        {
            return;
        }

        var mineDoor = FindBuilding("minegate").DoorAnchorGlobal;
        for (var i = _pendingReturns.Count - 1; i >= 0; i--)
        {
            var pending = _pendingReturns[i];
            var actors = pending.HeroIds
                .Select(id => _heroActors.TryGetValue(id, out var a) ? a : null)
                .Where(a => a is not null)
                .Select(a => a!)
                .ToList();

            if (actors.Count == 0)
            {
                _pendingReturns.RemoveAt(i);
                foreach (var id in pending.HeroIds)
                {
                    _queuedReturnHeroIds.Remove(id);
                }

                continue;
            }

            if (actors.Any(a => !_awaySeconds.TryGetValue(a.HeroIdValue, out var t) || t < MinDelveShowSeconds))
            {
                continue; // at least one survivor hasn't cleared the show floor yet
            }

            _pendingReturns.RemoveAt(i);
            foreach (var id in pending.HeroIds)
            {
                _queuedReturnHeroIds.Remove(id);
            }

            for (var h = 0; h < actors.Count; h++)
            {
                _pendingReturnWalk.Add((actors[h], h * FileExitStaggerSeconds, mineDoor));
            }

            PartyEmerging?.Invoke(pending.VenueId);
        }
    }

    /// <summary>U10: fires each queued survivor's <see cref="HeroActor2D.ReturnTo"/> — FROM the
    /// gate door anchor (KTD-5), staggered by <see cref="FileExitStaggerSeconds"/> exactly like
    /// <see cref="TickPendingMarchOut"/> staggers the opposite direction.</summary>
    private void TickPendingReturnWalk(double delta)
    {
        if (_pendingReturnWalk.Count == 0)
        {
            return;
        }

        for (var i = _pendingReturnWalk.Count - 1; i >= 0; i--)
        {
            var (actor, remaining, gateAnchor) = _pendingReturnWalk[i];
            remaining -= (float)delta;
            if (remaining > 0f)
            {
                _pendingReturnWalk[i] = (actor, remaining, gateAnchor);
                continue;
            }

            _pendingReturnWalk.RemoveAt(i);
            if (_heroActors.ContainsValue(actor))
            {
                actor.ReturnTo(gateAnchor);
            }
        }
    }

    /// <summary>
    /// Keeps the camera on the player, every frame.
    ///
    /// <para>THE CAMERA NEVER FOLLOWED. <see cref="Build"/> set <c>Cam.GlobalPosition = forgeDoor</c>
    /// once and nothing ever moved it again, so walking away from the forge walked the player
    /// straight off the edge of a frozen view — in a top-down game whose only control is WASD, that
    /// is the whole game broken. It looked survivable in screenshots because the spawn point is in
    /// frame; it is only wrong once you MOVE, which no automated check ever did (the playtest tools
    /// teleport via <see cref="PlayerController2D.SpawnAt"/> and read sim state, they never watch the
    /// view). <see cref="CameraFollowTests"/> now drives real movement and asserts the camera
    /// tracked it.</para>
    ///
    /// <para>Assigning <see cref="Node2D.GlobalPosition"/> is the follow: the camera's own
    /// <see cref="Camera2D.PositionSmoothingEnabled"/> (set in <see cref="Build"/>) eases the drawn
    /// position toward it, and its <c>Limit*</c> rect clamps the view to the town, so this stays a
    /// one-line target assignment rather than hand-rolled lerp + clamp. Null-guarded for the window
    /// between <see cref="Node._Process"/> starting and <see cref="Build"/> having run.</para>
    /// </summary>
    /// <summary>Re-derives <see cref="CanvasShrink"/> from the current viewport width and pushes it to
    /// the container. Safe to call repeatedly — assigning the same value is a no-op, so wiring this to
    /// <see cref="Control.Resized"/> costs nothing while the window is still.</summary>
    private void ApplyCanvasShrink()
    {
        if (ViewportContainer is null)
        {
            return;
        }

        // The control's own width, not the OS window's: the town is full-rect inside MainUi, so these
        // agree in the real game, and in a test that mounts it small the framing still follows the box
        // it was actually given.
        var width = Size.X > 1f ? Size.X : GetViewportRect().Size.X;
        var shrink = ShrinkFor(width);
        if (shrink == CanvasShrink && ViewportContainer.StretchShrink == shrink)
        {
            return;
        }

        CanvasShrink = shrink;
        ViewportContainer.StretchShrink = shrink;
    }

    private void FollowPlayer()
    {
        if (Cam is null || Player is null)
        {
            return;
        }

        // U2 (shell-and-audio plan, KTD-C): used to lift the camera target by half of
        // MainUi's opaque HUD header height, because the header painted OVER the top of this
        // full-rect viewport and hid whatever the player stood under. The header no longer
        // overlaps the world at all — MainUi.BuildUi now mounts this control in LAYOUT FLOW
        // below the header, so every pixel Town2D reports is already on-screen. Centering on
        // the player centers them in the visible strip because the visible strip is now the
        // whole viewport; no compensation term is needed, or correct, here.
        //
        // A focus beat borrows the camera for a moment — see FocusOn. The player keeps walking
        // underneath; the camera simply looks elsewhere and then glides back, since Camera2D's own
        // position smoothing eases both directions for free.
        var anchor = _focusRemaining > 0f && _focusTarget is { } focus ? focus : Player.GlobalPosition;
        Cam.GlobalPosition = anchor;
    }

    /// <summary>
    /// Points the camera at <paramref name="worldTarget"/> for <paramref name="seconds"/>, then hands it
    /// back to the player.
    ///
    /// <para><b>Why:</b> the HUD promises "watch them go" when a party marches for the Mine, and you
    /// could not — the camera stays glued to the player, the gate is at the far north edge of the town,
    /// and the rally marker is off screen the moment it appears. Brian's playtest: "after sending off the
    /// party, the little floor thing is off the screen — where are the visuals to follow their
    /// adventure??" The departure is the most cinematic thing that happens in a day and nobody had ever
    /// seen it.</para>
    ///
    /// <para>Deliberately a TIMED BORROW rather than a mode: no state to get stuck in, no way to leave
    /// the player uncontrollable, and a second call simply retargets. Input is untouched throughout —
    /// walking away during the beat is allowed, and the camera returning to wherever the player actually
    /// got to is the correct behaviour rather than a snap-back.</para>
    /// </summary>
    public void FocusOn(Vector2 worldTarget, float seconds)
    {
        if (seconds <= 0f)
        {
            return;
        }

        _focusTarget = worldTarget;
        _focusRemaining = seconds;
    }

    /// <summary>Convenience for the commonest focus beat: look at the mine gate as a party departs.
    /// U1: suppressed while <see cref="InteriorActive"/> — the camera is clamped to the room rect,
    /// and a departure pan would fight that clamp and rip the player's view out of the room mid-day.
    /// Simply dropped, not deferred (contrast <c>MainUi</c>'s modal-owns-the-screen pending-beat
    /// path): a beat that would have fired while the player happened to be inside the forge is an
    /// acceptable, deliberately cheap simplification for this slice.</summary>
    public void FocusOnMineGate(float seconds = MineGateFocusSeconds)
    {
        if (InteriorActive)
        {
            return;
        }

        if (_buildingsByKey.TryGetValue("minegate", out var gate))
        {
            FocusOn(gate.DoorAnchorGlobal, seconds);
        }
    }

    /// <summary>
    /// U10 (world-and-interiors plan, KTD-5): no longer snaps a survivor home on the spot. This
    /// only QUEUES the group into <see cref="_pendingReturns"/> — <see cref="TickPendingReturns"/>
    /// (run every frame from <see cref="_Process"/>) is what actually fires <see
    /// cref="HeroActor2D.ReturnTo"/>, once the show floor clears. Queuing rather than acting here
    /// is the fix for "why did the heroes come back to the town visually?": this method is called
    /// from <see cref="OnPhaseCompleted"/> for BOTH the Expedition and ExpeditionDeep completions,
    /// and for an unstaged (target floor 1) run the FIRST of those calls can land on the very same
    /// tick a departing actor is still mid-march — snapping <see cref="HeroActor2D.ReturnTo"/> then
    /// would have shown a hero who never even finished leaving suddenly walking back in.
    ///
    /// <para>Grouped per <c>ExpeditionResult</c> (one <see cref="PendingReturn"/> per venue, not one
    /// town-wide bag) and de-duplicated via <see cref="_queuedReturnHeroIds"/> — <c>PendingExpeditions</c>
    /// is durable sim state (cleared only by the Evening reveal), so the SAME finalized result is read
    /// again by the ExpeditionDeep-completion call for an unstaged run, and would otherwise be
    /// queued twice.</para>
    /// </summary>
    private void ReturnSurvivors()
    {
        foreach (var expedition in Adapter!.CurrentState.PendingExpeditions)
        {
            var ids = expedition.Survivors
                .Select(id => id.Value)
                .Where(id => !_queuedReturnHeroIds.Contains(id) &&
                             _heroActors.TryGetValue(id, out var actor) &&
                             actor.State != HeroActor2D.HeroTownState.Wandering)
                .ToList();

            if (ids.Count == 0)
            {
                continue;
            }

            _pendingReturns.Add(new PendingReturn { HeroIds = ids, VenueId = expedition.VenueId });
            foreach (var id in ids)
            {
                _queuedReturnHeroIds.Add(id);
            }
        }
    }

    /// <summary>
    /// U10 addendum: also clears every piece of the show-floor's live-session bookkeeping
    /// (<see cref="_pendingReturns"/>/<see cref="_pendingReturnWalk"/>/<see
    /// cref="_queuedReturnHeroIds"/>/<see cref="_awaySeconds"/>). This is the edge-case safety
    /// valve KTD-5 names explicitly: if a hold somehow has not resolved by the moment the NEW day
    /// begins (every actor below is about to be forced to <c>Wandering</c> regardless), leaving
    /// stale entries around would have <see cref="TickPendingReturns"/> keep checking a group
    /// against actors that are already home — at best a no-op, at worst a confusing second
    /// walk-in animation replayed over an actor that never left <c>Wandering</c>. Clearing here
    /// keeps this method's existing role as the unconditional fallback honest.
    /// </summary>
    private void SnapRemainingHeroesHome()
    {
        var heroes = Adapter!.CurrentState.Heroes;
        foreach (var actor in _heroActors.Values.Where(a =>
                     a.State != HeroActor2D.HeroTownState.Wandering &&
                     heroes.TryGetValue(a.HeroIdValue, out var hero) && hero.Alive))
        {
            actor.SetState(HeroActor2D.HeroTownState.Wandering);
        }

        _pendingReturns.Clear();
        _pendingReturnWalk.Clear();
        _queuedReturnHeroIds.Clear();
        _awaySeconds.Clear();
    }

    /// <summary>Deterministic wander-band home per hero id (no RNG, KTD2/KTD4) — a 2D twin of
    /// <c>Town3D.HomeFor</c>, spread across an open tile band clear of every venue footprint.</summary>
    private static Vector2 HomeFor(int heroValue) =>
        TownLayout2D.TileToWorld(new Vector2I(6 + heroValue * 3 % 28, 10 + heroValue * 2 % 6));

    /// <summary>Party-file rally slot near the town square, spread along X (mirrors
    /// <c>Town3D.RallySpotFor</c>).</summary>
    private static Vector2 RallySpotFor(int index, int count) =>
        TownLayout2D.TileToWorld(TownLayout2D.RallyTile) + new Vector2((index - (count - 1) / 2f) * RallySpacingPx, 0f);

    private void BuildBuildings()
    {
        foreach (var venue in TownLayout2D.Venues)
        {
            var building = new Building2D();
            var sprite = TownAssets2D.ForVenue(venue.SpriteId);
            // U7 (world-and-interiors plan, KTD-3): the workshop's nametag follows the profession
            // (WorkshopNametag) instead of TownLayout2D's static default; every other venue is
            // unaffected (TownLayout2D.Venues's own doc: the static "Forge" entry stays the
            // fallback any GameState-free reader still sees).
            var nametag = venue.Key == WorkshopVenueKey ? WorkshopNametag : venue.Nametag;
            building.Configure(venue.Key, nametag, sprite, TownLayout2D.TileToWorld(venue.Tile));
            building.Picked += key => BuildingClicked?.Invoke(key);
            BuildingsRoot.AddChild(building);
            _buildingsByKey[venue.Key] = building;

            if (venue.Key == WorkshopVenueKey)
            {
                MountWorkshopSignboard(building);
            }
        }
    }

    private Sprite2D? _workshopSignboard;

    /// <summary>U7 (world-and-interiors plan): the exterior signboard overlay — a small sprite
    /// hung above the nametag, swapped per profession (<c>town2d-sign-{professionId}</c>, pinned
    /// for U8's art). The cheap, honest exterior answer KTD-3 asks for THIS unit (full
    /// per-profession exterior art is an owner-gated follow-up, plan Open Question 3).</summary>
    private void MountWorkshopSignboard(Building2D building)
    {
        var texture = TownAssets2D.ForProp(WorkshopVocab.SignboardSpriteIdFor(_workshopProfessionOrder));

        if (_workshopSignboard is not null)
        {
            _workshopSignboard.QueueFree();
        }

        var buildingSize = building.Sprite.Texture?.GetSize() ?? new Vector2(64f, 80f);
        _workshopSignboard = new Sprite2D
        {
            Name = "WorkshopSignboard",
            Texture = texture,
            Centered = true,
            // Hangs clear above the nametag (which itself sits at -size.Y - 10, see
            // Building2D.BuildLabel) — cosmetic overlay only, no collision of its own.
            Position = new Vector2(0f, -buildingSize.Y - 26f),
        };
        building.AddChild(_workshopSignboard);
    }

    /// <summary>Re-renders the workshop building's nametag + signboard for the CURRENT <see
    /// cref="_workshopProfessionOrder"/> — called by <see cref="RebuildWorkshopIfStale"/> whenever
    /// a profession changes mid-run.</summary>
    private void UpdateWorkshopBuildingDressing()
    {
        if (!_buildingsByKey.TryGetValue(WorkshopVenueKey, out var building))
        {
            return;
        }

        building.NameLabel.Text = WorkshopNametag;
        MountWorkshopSignboard(building);
    }

    /// <summary>Fallback footprint for a prop whose resolved texture reports a zero/negative size
    /// (mirrors <see cref="Building2D"/>'s own <c>FallbackSize</c> guard) — small enough to stay
    /// unobtrusive if it's ever hit.</summary>
    private static readonly Vector2 PropFallbackSize = new(16f, 16f);

    /// <summary>Sprite id gap #2 ("trees never move") keys off — every prop with this id gets a
    /// <see cref="SwayingTreeSprite2D"/> instead of a bare <see cref="Sprite2D"/>.</summary>
    private const string TreePropSpriteId = "town2d-prop-tree";

    /// <summary>
    /// Instantiates every <see cref="TownLayout2D.Props"/> entry (well, lanterns, trees, crates) —
    /// called once from <see cref="Build"/>, right after <see cref="BuildBuildings"/> so <see
    /// cref="YSort"/> already exists. Each prop is positioned via the SAME feet-origin convention
    /// <see cref="Building2D.Configure"/> uses for buildings (<see cref="Sprite2D.Offset"/>
    /// shifted up by half the sprite's height so its BOTTOM edge lands on <see
    /// cref="TownLayout2D.TileToWorld"/>'s tile-center position) — required for <see
    /// cref="YSort"/>'s <c>YSortEnabled</c> parent to sort heroes/the player correctly in front of
    /// or behind a tall prop like a tree or the well, exactly as it already does for buildings.
    /// Y-sorted props mount under <see cref="YSort"/>; a flat (non-Y-sorted) prop would mount under
    /// <see cref="Ground"/> instead, but <see cref="TownLayout2D.Props"/> has none of those yet.
    ///
    /// <para>Gap #2 fix ("trees never move"): every <see cref="TreePropSpriteId"/> entry is a <see
    /// cref="SwayingTreeSprite2D"/> rather than a bare <see cref="Sprite2D"/> — a per-instance
    /// index seeds its <see cref="TreeSway"/> phase so a grove doesn't sway in lockstep (mirrors
    /// <see cref="AmbientLife2D"/>'s per-lamppost flicker-phase idiom). Every other prop is
    /// unaffected — a bare <see cref="Sprite2D"/>, exactly as before.</para>
    /// </summary>
    private void BuildProps()
    {
        var treeIndex = 0;
        foreach (var prop in TownLayout2D.Props)
        {
            var sprite = TownAssets2D.ForProp(prop.SpriteId);
            var size = sprite?.GetSize() ?? PropFallbackSize;
            if (size.X <= 0f || size.Y <= 0f)
            {
                size = PropFallbackSize;
            }

            Sprite2D node;
            if (prop.SpriteId == TreePropSpriteId)
            {
                var swaying = new SwayingTreeSprite2D();
                swaying.Init(treeIndex * 0.9f); // same per-instance phase spread AmbientLife2D's lamp flicker uses
                treeIndex++;
                node = swaying;
            }
            else
            {
                node = new Sprite2D();
            }

            node.Name = $"Prop_{prop.SpriteId}_{prop.Tile.X}_{prop.Tile.Y}";
            node.Texture = sprite;
            node.Centered = true;
            node.Offset = new Vector2(0f, -size.Y / 2f); // bottom edge lands on the tile's center (feet-origin)
            node.Position = TownLayout2D.TileToWorld(prop.Tile);

            if (prop.YSorted)
            {
                YSort.AddChild(node);
            }
            else
            {
                Ground.AddChild(node);
            }
        }
    }

    /// <summary>
    /// U1 (painted-interiors plan): instantiates every <see cref="InteriorLayout2D.Rooms"/> entry —
    /// called once from <see cref="Build"/>, right after <see cref="BuildProps"/> so <see
    /// cref="YSort"/> already exists. Each room's shell/walls/exit-zone mount under the room's own
    /// node (built off-frame at its island offset — KTD-1); each room's STATIONS mount as direct
    /// children of <see cref="YSort"/> itself (see <see cref="InteriorRoom2D"/>'s own class doc for
    /// why: they must share the town's flat Y-sort scope with the player, not a nested one).
    /// </summary>
    private void BuildInteriorRooms()
    {
        foreach (var spec in InteriorLayout2D.Rooms.Values)
        {
            // U7 (world-and-interiors plan, KTD-3): the workshop's ACTUAL room is composed from
            // the current profession selection, never the static "forge" table row directly (that
            // row stays the blacksmith-only default other readers rely on — see
            // InteriorLayout2D's own doc on its "forge" entry).
            var effectiveSpec = spec.VenueKey == WorkshopVenueKey
                ? InteriorLayout2D.WorkshopRoomFor(_workshopProfessionOrder)
                : spec;
            MountInteriorRoom(effectiveSpec);
        }
    }

    /// <summary>Builds and mounts one interior room from <paramref name="spec"/> — factored out of
    /// <see cref="BuildInteriorRooms"/> (U7) so <see cref="RebuildWorkshopRoom"/> can remount just
    /// the workshop without repeating the wiring (shell/walls/exit-zone via <see
    /// cref="InteriorRoom2D.Build"/>, stations onto the flat <see cref="YSort"/> scope, the
    /// exit-zone's <c>BodyEntered</c> → <see cref="ExitInterior"/>, and the room's own
    /// <c>StationActivated</c> re-emit).</summary>
    private void MountInteriorRoom(InteriorLayout2D.RoomSpec spec)
    {
        var room = new InteriorRoom2D();
        World.AddChild(room);
        room.Build(spec);
        room.StationActivated += stationSpec => StationActivated?.Invoke(stationSpec);

        foreach (var station in room.Stations)
        {
            YSort.AddChild(station);
        }

        room.ExitZone.BodyEntered += body =>
        {
            if (body == Player && InteriorActive && InteriorVenueKey == spec.VenueKey)
            {
                ExitInterior();
            }
        };

        _interiorRooms[spec.VenueKey] = room;

        // U5 (world-and-interiors plan, KTD-8): the market room additionally gets its customer
        // choreography — a plain (non-Y-sort-enabled) wrapper under YSort, mirroring TownsfolkRoot's
        // own precedent, so each customer Y-sorts individually against the player rather than as one
        // blob (see MarketLife2D's own class doc).
        if (spec.VenueKey == "market")
        {
            _marketLife = new MarketLife2D();
            YSort.AddChild(_marketLife);
            _marketLife.Build(room);
        }
    }

    /// <summary>
    /// U7 (world-and-interiors plan): the unit's one structural change — rooms are built ONCE at
    /// startup (<see cref="BuildInteriorRooms"/>), so a profession added mid-run (<c>MainUi
    /// .OnSecondProfessionPicked</c>) needs its own remount path. Tears down the currently-mounted
    /// workshop room (stations + the room node itself) and rebuilds it from <see
    /// cref="_workshopProfessionOrder"/> via the SAME <see cref="MountInteriorRoom"/> the initial
    /// build uses, then re-renders the building's nametag/signboard to match.
    /// </summary>
    private void RebuildWorkshopRoom()
    {
        if (_interiorRooms.TryGetValue(WorkshopVenueKey, out var old))
        {
            foreach (var station in old.Stations)
            {
                YSort.RemoveChild(station);
                PanelGraveyard.Bury(station);
            }

            World.RemoveChild(old);
            PanelGraveyard.Bury(old);
        }

        MountInteriorRoom(InteriorLayout2D.WorkshopRoomFor(_workshopProfessionOrder));
        UpdateWorkshopBuildingDressing();
    }

    /// <summary>
    /// Checked at the top of <see cref="EnterInterior"/> for the workshop venue only: if a
    /// profession has been confirmed by the sim since the room was last built/rebuilt (<see
    /// cref="_workshopBuiltFor"/>), extend <see cref="_workshopProfessionOrder"/> (never dropping
    /// the existing primary — new ids are only ever APPENDED) and rebuild the room in place before
    /// the player walks in. A no-op the overwhelming majority of calls (nothing changed since the
    /// last entry), so this never adds a per-entry cost beyond one set-equality check.
    /// </summary>
    private void RebuildWorkshopIfStale()
    {
        var current = Adapter!.CurrentState.Player.SelectedProfessions;
        if (current.SetEquals(_workshopBuiltFor))
        {
            return;
        }

        var order = _workshopProfessionOrder.Where(current.Contains).ToList();
        foreach (var id in current)
        {
            if (!order.Contains(id))
            {
                order.Add(id);
            }
        }

        _workshopProfessionOrder = order.Count > 0 ? order : current.ToArray();
        _workshopBuiltFor = current;
        RebuildWorkshopRoom();
    }

    /// <summary>
    /// The workshop's PRIMARY-first profession order at boot (see <see
    /// cref="_workshopProfessionOrder"/>'s own doc). A fresh campaign's <see cref="Build"/> always
    /// sees exactly one selected profession, which is trivially exact. A RESUMED save that already
    /// held two before this instance existed has no historical "which was picked first" to recover
    /// (KTD2: <c>Contracts</c> stores an unordered set) — this falls back to keeping blacksmith
    /// primary if present (the long-standing pre-U7 default, least likely to surprise a returning
    /// player), else the set's own alphabetical order. Cosmetic-only approximation, named here
    /// rather than silently guessed.
    /// </summary>
    private static IReadOnlyList<string> ResolveInitialWorkshopOrder(ImmutableSortedSet<string> selected)
    {
        if (selected.Count <= 1)
        {
            return selected.ToArray();
        }

        return selected.Contains(ProfessionRegistry.BlacksmithId)
            ? new[] { ProfessionRegistry.BlacksmithId }.Concat(selected.Where(p => p != ProfessionRegistry.BlacksmithId)).ToArray()
            : selected.ToArray();
    }

    /// <summary>Deterministic wander-home tiles for the cosmetic <see cref="TownsfolkNpc2D"/>
    /// villagers — hand-picked clear of every venue footprint, the plaza/road cobble network, and
    /// hero <see cref="HomeFor"/> bands (see <see cref="TownLayout2D.Venues"/>/<see
    /// cref="TownLayout2D.PathRects"/> for the occupied regions this avoids): two open corners
    /// northwest/northeast of the plaza, two more southwest/southeast of it.</summary>
    private static readonly Vector2I[] TownsfolkHomeTiles =
    {
        new(6, 8),
        new(34, 8),
        new(6, 20),
        new(34, 20),
    };

    /// <summary>Test/inspection surface: how many cosmetic villagers <see cref="BuildTownsfolk"/>
    /// spawns (mirrors <see cref="TownsfolkHomeTiles"/>'s length without exposing the private table
    /// itself).</summary>
    public static int TownsfolkHomeTileCount => TownsfolkHomeTiles.Length;

    /// <summary>
    /// Spawns a small bounded set of purely cosmetic wandering villagers (<see
    /// cref="TownsfolkNpc2D"/>) into <see cref="TownsfolkRoot"/> — called once from <see
    /// cref="Build"/>, right after <see cref="HeroesRoot"/> exists. These are NOT heroes: never
    /// added to <see cref="_heroActors"/>, never reconciled against sim state, never clickable —
    /// pure ambience (KTD2). Art/tint resolution lives on <see cref="TownsfolkNpc2D"/> itself (see
    /// its class doc) so this method is just the deterministic placement loop.
    /// </summary>
    private void BuildTownsfolk()
    {
        // Gap #3 / U3 fix: resolve the step-B/walk2/walk4 textures once (shared by every
        // villager — they all reuse the vanguard body) and hand them to each Init call;
        // null-tolerant if any is ever absent.
        var stepSprite = TownsfolkNpc2D.ResolveStepSprite();
        var walk2Sprite = TownsfolkNpc2D.ResolveWalk2Sprite();
        var walk4Sprite = TownsfolkNpc2D.ResolveWalk4Sprite();

        // U6: every venue's own door anchor, in TownLayout2D.Venues' fixed array order (a stable,
        // deterministic sequence — a Dictionary's enumeration order is an implementation detail,
        // this array's is not) — the pool an errand's id-seeded rotation picks from.
        var errandTargets = TownLayout2D.Venues
            .Select(v => _buildingsByKey[v.Key].DoorAnchorGlobal)
            .ToList();

        _townsfolk.Clear();
        for (var i = 0; i < TownsfolkHomeTiles.Length; i++)
        {
            var npc = new TownsfolkNpc2D();
            npc.Init(
                i,
                TownsfolkNpc2D.ResolveSprite(),
                TownsfolkNpc2D.CivilianTint(i),
                TownLayout2D.TileToWorld(TownsfolkHomeTiles[i]),
                stepSprite,
                walk2Sprite,
                walk4Sprite);
            npc.SetErrandTargets(errandTargets);
            TownsfolkRoot.AddChild(npc);
            _townsfolk.Add(npc);
        }
    }

    /// <summary>U6: mounts <see cref="TavernLife2D"/> at the tavern room's own "Patron Table"
    /// station positions (<c>InteriorLayout2D</c>'s <c>"table-a"</c>/<c>"table-b"</c> rows, U1-
    /// pinned as seating anchors) — a plain wrapper directly under <see cref="YSort"/> (mirrors
    /// <see cref="HeroesRoot"/>/<see cref="TownsfolkRoot"/>'s own "flat Y-sort scope" precedent,
    /// see <see cref="InteriorRoom2D"/>'s class doc for why a nested Y-sort container would sort
    /// as one blob instead). Called once from <see cref="Build"/>, after <see
    /// cref="BuildInteriorRooms"/> so the tavern's stations already exist with resolved positions.
    /// No-op (leaves <see cref="_tavernLife"/> null) if the tavern row or its table stations are
    /// ever renamed out from under this — defensive, never expected on a real build.</summary>
    private void WireTavernLife()
    {
        if (!_interiorRooms.TryGetValue("tavern", out var tavernRoom))
        {
            return;
        }

        var seatAnchors = tavernRoom.Stations
            .Where(s => s.Key is "table-a" or "table-b")
            .Select(s => s.Position)
            .ToList();

        if (seatAnchors.Count == 0)
        {
            return;
        }

        _tavernLife = new TavernLife2D { Name = "TavernLife2D" };
        YSort.AddChild(_tavernLife);
        _tavernLife.Build(seatAnchors);
    }

    /// <summary>U6: assigns present (<see cref="HeroActor2D.HeroTownState.Wandering"/>) heroes to
    /// tavern seats — called from <see cref="Refresh"/> (once per sim tick, not once per render
    /// frame: hero presence only ever changes on a tick, see <see cref="TavernLife2D"/>'s own class
    /// doc). A hero mid-rally/march/away is excluded by the state filter alone — the guard against
    /// the same hero reading as both wandering the square and seated in the tavern.</summary>
    private void RefreshTavernLife()
    {
        if (_tavernLife is null || Adapter is null)
        {
            return;
        }

        var present = _heroActors.Values
            .Where(a => a.State == HeroActor2D.HeroTownState.Wandering)
            .OrderBy(a => a.HeroIdValue)
            .Select(a => (a.HeroIdValue, a.ClassId, MoodPermilleFor(a.HeroIdValue)))
            .ToList();

        _tavernLife.Refresh(present);
    }

    /// <summary>Reads the live sim <c>MoodPermille</c> for a hero id — the same field
    /// <c>TavernPanel</c>/<c>CounterPanel</c> already read; 0 (neutral) if the hero has somehow
    /// left the roster the same tick its actor is still being torn down.</summary>
    private int MoodPermilleFor(int heroId) =>
        Adapter!.CurrentState.Heroes.TryGetValue(heroId, out var hero) ? hero.MoodPermille : 0;

    /// <summary>Locates the forge building and wires a glow overlay + spark/steam particles near
    /// its door — called once at the tail of <see cref="Build"/>, after <see cref="BuildBuildings"/>
    /// has configured every <see cref="Building2D"/> (mirrors <c>Town3D.WireForgeStationVfx</c>'s
    /// "after Configure, not inside" ordering).</summary>
    private void WireForgeFx()
    {
        _forgeBuilding = FindBuilding("forge");
        var pos = _forgeBuilding.GlobalPosition + new Vector2(0f, -24f);

        _forgeGlowOverlay = new Sprite2D
        {
            Name = "ForgeGlow",
            Texture = GlowTexture(),
            Position = pos,
            Modulate = new Color(1f, 0.6f, 0.2f, 0f),
            ZIndex = 5,
        };
        Fx.AddChild(_forgeGlowOverlay);

        _forgeSparks = BuildParticles("ForgeSparks", pos, new Color(1f, 0.8f, 0.3f), amount: 14, lifetime: 0.35);
        Fx.AddChild(_forgeSparks);

        _forgeSteam = BuildParticles("QuenchSteam", pos, new Color(0.9f, 0.9f, 0.95f, 0.5f), amount: 8, lifetime: 0.7);
        Fx.AddChild(_forgeSteam);
    }

    /// <summary>Builds the cozy ambient-life layer (<see cref="AmbientLife2D"/>: chimney smoke,
    /// dusk fireflies, flickering lamp glow) — mounted under <see cref="World"/> as the LAST child
    /// added (after <see cref="Fx"/>, <see cref="DuskModulate"/>, <see cref="Cam"/>), so painter's-
    /// order draws it ABOVE the Y-sorted buildings/heroes/player and above the forge FX, without
    /// ever joining <see cref="YSort"/> itself — nothing here participates in, or needs, Y-sorting.
    /// <see cref="DuskModulate"/>'s tint applies to the whole canvas regardless of sibling order, so
    /// mounting after it does not exempt this layer from the dusk mood. Called once at the tail of
    /// <see cref="Build"/>, after <see cref="WireForgeFx"/> so <see cref="_forgeBuilding"/> is
    /// already resolved.</summary>
    private void WireAmbientLife()
    {
        _ambientLife = new AmbientLife2D { Name = "AmbientLife2D" };
        World.AddChild(_ambientLife);

        var tavernBuilding = FindBuilding("tavern");
        var forgeChimneyPos = _forgeBuilding!.GlobalPosition + new Vector2(18f, -70f);
        var tavernChimneyPos = tavernBuilding.GlobalPosition + new Vector2(14f, -58f);
        var townRect = new Rect2(0f, 0f, TownLayout2D.GridWidth * TileSize, TownLayout2D.GridHeight * TileSize);
        var lanternPositions = TownLayout2D.Props
            .Where(prop => prop.SpriteId == "town2d-prop-lantern")
            .Select(prop => TownLayout2D.TileToWorld(prop.Tile))
            .ToList();

        // Gap #1 fix ("Market, Mine-gate and Noticeboard buildings are completely dead"): position
        // each venue's ambient cue off that BUILDING's own resolved sprite height (not a hardcoded
        // placeholder number) so it stays proportionate whether real generated art or the flat-color
        // fallback is what actually loaded.
        var marketBuilding = FindBuilding("market");
        var marketHeight = (float)(marketBuilding.Sprite.Texture?.GetHeight() ?? 64);
        var marketAwningPos = marketBuilding.GlobalPosition + new Vector2(0f, -marketHeight * 0.75f); // eave/door lintel

        var mineBuilding = FindBuilding("minegate");
        var mineHeight = (float)(mineBuilding.Sprite.Texture?.GetHeight() ?? 48);
        var mineDustPos = mineBuilding.GlobalPosition + new Vector2(0f, -mineHeight * 0.3f); // the dark mouth, near ground level

        var noticeboardBuilding = FindBuilding("noticeboard");
        var noticeboardHeight = (float)(noticeboardBuilding.Sprite.Texture?.GetHeight() ?? 48);
        var noticeboardPaperPos = noticeboardBuilding.GlobalPosition + new Vector2(0f, -noticeboardHeight * 0.6f); // the board's face

        // U11 ("lamps glow at a fixed alpha all day, no window light, no darkness"): one warm
        // glow anchor per venue, hand-placed against that building's OWN resolved sprite size
        // (same "fraction of the real PNG, never a hardcoded pixel number" idiom as the gap #1
        // cues above) — a window pane for the forge/market/tavern facades, and a torch/lantern
        // stand-in for the mine gate's windowless cave mouth and the noticeboard's own small
        // hanging lamp.
        var windowGlowPositions = new List<Vector2>
        {
            WindowAnchor(_forgeBuilding, -0.22f, -0.60f, 72f, 81f),
            WindowAnchor(marketBuilding, -0.05f, -0.55f, 76f, 62f),
            WindowAnchor(tavernBuilding, -0.28f, -0.62f, 84f, 88f),
            WindowAnchor(mineBuilding, 0f, -0.45f, 48f, 48f),
            WindowAnchor(noticeboardBuilding, -0.30f, -0.55f, 44f, 50f),
        };

        _ambientLife.Build(
            forgeChimneyPos,
            tavernChimneyPos,
            townRect,
            lanternPositions,
            marketAwningPos,
            mineDustPos,
            noticeboardPaperPos,
            windowGlowPositions);

        // Seed the correct phase immediately (mirrors DayPhaseTint's constructor-seeding
        // discipline: never start wrong for even one frame) — _Process re-drives this every tick.
        _ambientLife.SetPhase(Adapter!.CurrentState.Phase);
    }

    /// <summary>U11: a hand-placed window/light-source glow anchor for <see
    /// cref="WireAmbientLife"/> — <paramref name="xFrac"/>/<paramref name="yFrac"/> are fractions
    /// of that building's OWN resolved sprite size (negative Y is up, matching every other offset
    /// in this file), falling back to <paramref name="fallbackWidth"/>/<paramref
    /// name="fallbackHeight"/> (the venue's real committed PNG dimensions) if the texture somehow
    /// failed to resolve.</summary>
    private static Vector2 WindowAnchor(Building2D building, float xFrac, float yFrac, float fallbackWidth, float fallbackHeight)
    {
        var width = (float)(building.Sprite.Texture?.GetWidth() ?? fallbackWidth);
        var height = (float)(building.Sprite.Texture?.GetHeight() ?? fallbackHeight);
        return building.GlobalPosition + new Vector2(xFrac * width, yFrac * height);
    }

    private static CpuParticles2D BuildParticles(string name, Vector2 position, Color color, int amount, double lifetime) => new()
    {
        Name = name,
        Position = position,
        Emitting = false,
        OneShot = true,
        Amount = amount,
        Lifetime = lifetime,
        Explosiveness = 1f,
        Direction = new Vector2(0, -1),
        Spread = 45f,
        Gravity = new Vector2(0, 60f),
        InitialVelocityMin = 20f,
        InitialVelocityMax = 45f,
        ScaleAmountMin = 1.2f,
        ScaleAmountMax = 2.2f,
        Color = color,
    };

    /// <summary>Small radial white→transparent falloff (same recipe as <c>MineWatch.BuildLightGradient</c>,
    /// scaled down for a hand-pixel-sized glow) — cached process-wide, tinted at draw time via <see
    /// cref="Sprite2D.Modulate"/>.</summary>
    private static GradientTexture2D GlowTexture() => _glowTextureCache ??= new GradientTexture2D
    {
        Gradient = new Gradient
        {
            Colors = [new Color(1, 1, 1, 1), new Color(1, 1, 1, 0.4f), new Color(1, 1, 1, 0)],
            Offsets = [0f, 0.5f, 1f],
        },
        Width = 48,
        Height = 48,
        Fill = GradientTexture2D.FillEnum.Radial,
        FillFrom = new Vector2(0.5f, 0.5f),
        FillTo = new Vector2(1f, 0.5f),
    };

    /// <summary>
    /// Paints the whole grid with grass, then paints every tile inside <see
    /// cref="TownLayout2D.PathRects"/> with cobble — the plaza/road/spur network reading as one
    /// continuous cozy-village street, purely for legibility (decoration only — <see
    /// cref="Building2D"/> carries its own blocking <c>Footprint</c>, so nothing here needs
    /// collision).
    ///
    /// <para>When the real pixel-art ground atlas (<c>town2d-ground-atlas</c>: grass base + two
    /// detail variants + textured cobble) is present, grass is broken up with a deterministic
    /// scatter of the two detail tiles (a stable spatial hash, never RNG — keeps the field identical
    /// every run and headless-test-safe) and grass cells that border the cobble network bias toward
    /// the blade variant so the plaza/road edge dithers into the grass instead of reading as a hard
    /// rectangle seam. When the atlas is missing, everything degrades to the original 2-tile
    /// flat-color build (single grass coord, single cobble coord) — identical to the pre-art slice,
    /// so no import dependency can break the town rendering.</para>
    /// </summary>
    private static TileMapLayer BuildGround()
    {
        var (tileSet, rich) = BuildTileSet();
        var layer = new TileMapLayer { Name = "Ground", TileSet = tileSet };

        var cobble = rich ? RichCobbleCoord : FlatCobbleCoord;
        var cobbleCells = new HashSet<Vector2I>();
        foreach (var rect in TownLayout2D.PathRects)
        {
            for (var y = rect.Position.Y; y < rect.Position.Y + rect.Size.Y; y++)
            {
                for (var x = rect.Position.X; x < rect.Position.X + rect.Size.X; x++)
                {
                    cobbleCells.Add(new Vector2I(x, y));
                }
            }
        }

        for (var y = 0; y < TownLayout2D.GridHeight; y++)
        {
            for (var x = 0; x < TownLayout2D.GridWidth; x++)
            {
                var cell = new Vector2I(x, y);
                if (cobbleCells.Contains(cell))
                {
                    layer.SetCell(cell, 0, cobble);
                }
                else
                {
                    layer.SetCell(cell, 0, rich ? GrassVariantFor(x, y, cobbleCells) : GrassBaseCoord);
                }
            }
        }

        return layer;
    }

    /// <summary>Deterministic grass-tile pick (stable spatial hash, no RNG): grass cells touching the
    /// cobble network bias hard toward the blade variant (edge dither), open field gets a sparse
    /// scatter of both detail variants over the base. Pure — same grid every run.</summary>
    private static Vector2I GrassVariantFor(int x, int y, HashSet<Vector2I> cobbleCells)
    {
        var bordersCobble =
            cobbleCells.Contains(new Vector2I(x - 1, y)) || cobbleCells.Contains(new Vector2I(x + 1, y)) ||
            cobbleCells.Contains(new Vector2I(x, y - 1)) || cobbleCells.Contains(new Vector2I(x, y + 1));

        var h = unchecked((x * 73856093) ^ (y * 19349663));
        var bucket = ((h % 100) + 100) % 100; // 0..99, sign-safe

        if (bordersCobble)
        {
            // A path's edge is worn, not flowering: blades and pebbles only. Clover deliberately never
            // appears here — a flower growing in the middle of a footpath reads as a mistake.
            return bucket < 45 ? GrassBladeCoord : bucket < 62 ? GrassPebbleCoord : GrassBaseCoord;
        }

        // Open field. Detail stays sparse on purpose: at 3x upscale a busy ground fights the buildings
        // and the characters for attention, and this is the surface the eye should rest on.
        return bucket switch
        {
            < 14 => GrassBladeCoord,
            < 22 => GrassFleckCoord,
            < 29 => GrassCloverCoord,
            < 33 => GrassPebbleCoord,
            _ => GrassBaseCoord,
        };
    }

    // Rich atlas layout (town2d-ground-atlas.png, 96x16):
    //   base | blades | flecks | cobble | clover | pebbles
    // Tiles 4 and 5 are generated by art/pipeline/gen-ground-tiles.py, which samples its greens from
    // tiles 0-1 so the palette cannot drift.
    private static readonly Vector2I GrassBaseCoord = new(0, 0);
    private static readonly Vector2I GrassBladeCoord = new(1, 0);
    private static readonly Vector2I GrassFleckCoord = new(2, 0);
    private static readonly Vector2I RichCobbleCoord = new(3, 0);
    private static readonly Vector2I GrassCloverCoord = new(4, 0);
    private static readonly Vector2I GrassPebbleCoord = new(5, 0);

    /// <summary>Tiles in the atlas beyond the original four. Kept as a count rather than a hardcoded
    /// width so a future tile only has to be created below and given a bucket in
    /// <see cref="GrassVariantFor"/>.</summary>
    private const int ExtraGroundTiles = 2;

    // Flat-fallback atlas layout (2 tiles): grass | cobble. GrassBaseCoord (0,0) doubles as the flat
    // grass coord; the flat cobble sits at (1,0).
    private static readonly Vector2I FlatCobbleCoord = new(1, 0);

    private static readonly Color GrassColor = new(0.30f, 0.45f, 0.22f);
    private static readonly Color CobbleColor = new(0.42f, 0.40f, 0.38f);

    /// <summary>
    /// Prefers the imported pixel-art ground atlas (<c>town2d-ground-atlas</c>, 4×1 tiles of 16px);
    /// falls back to the original 2-tile flat-color image built in code when the art is missing so
    /// the town always renders (headless tests, unimported checkouts). Returns the tile set plus
    /// whether the rich atlas was used, so <see cref="BuildGround"/> knows which coord vocabulary and
    /// scatter path to use.
    /// </summary>
    private static (TileSet TileSet, bool Rich) BuildTileSet()
    {
        var art = GodotClient.IconRegistry.Art("town2d-ground-atlas");
        if (art is not null && art.GetWidth() >= TileSize * 4 && art.GetHeight() >= TileSize)
        {
            var richAtlas = new TileSetAtlasSource { Texture = art, TextureRegionSize = new Vector2I(TileSize, TileSize) };
            richAtlas.CreateTile(GrassBaseCoord);
            richAtlas.CreateTile(GrassBladeCoord);
            richAtlas.CreateTile(GrassFleckCoord);
            richAtlas.CreateTile(RichCobbleCoord);
            if (art.GetWidth() >= TileSize * (4 + ExtraGroundTiles))
            {
                // Guarded so an older 4-tile atlas still loads: creating a tile outside the texture's
                // bounds is an error, and the fallback for missing art is meant to be graceful.
                richAtlas.CreateTile(GrassCloverCoord);
                richAtlas.CreateTile(GrassPebbleCoord);
            }

            var richSet = new TileSet { TileSize = new Vector2I(TileSize, TileSize) };
            richSet.AddSource(richAtlas, 0);
            return (richSet, true);
        }

        var image = Image.CreateEmpty(TileSize * 2, TileSize, false, Image.Format.Rgba8);
        image.FillRect(new Rect2I(0, 0, TileSize, TileSize), GrassColor);
        image.FillRect(new Rect2I(TileSize, 0, TileSize, TileSize), CobbleColor);
        var texture = ImageTexture.CreateFromImage(image);

        var atlas = new TileSetAtlasSource { Texture = texture, TextureRegionSize = new Vector2I(TileSize, TileSize) };
        atlas.CreateTile(GrassBaseCoord);
        atlas.CreateTile(FlatCobbleCoord);

        var tileSet = new TileSet { TileSize = new Vector2I(TileSize, TileSize) };
        tileSet.AddSource(atlas, 0);
        return (tileSet, false);
    }
}
