using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using Godot;
using GodotClient.Town2d;
using GodotClient.Ui;

namespace GodotClient.Panels;

/// <summary>
/// LW5 — the depths watch: a lit <see cref="SubViewport"/> strip (SubViewport trap,
/// SubViewport-scoped <see cref="CanvasModulate"/>, null-tolerant lit sprites). Live ONLY while a
/// party is underground — <see cref="DayPhase.Expedition"/>/<see cref="DayPhase.Camp"/>/
/// <see cref="DayPhase.ExpeditionDeep"/> — collapsed to zero height otherwise, so whichever host
/// panel sits beneath it renders exactly as it always has when nobody is raiding. Zero sim/Contracts
/// writes (KTD2): <see cref="Refresh"/> only ever READS <see cref="GameState"/> and the tick's
/// <see cref="GameEvent"/> batch; every animation below is driven by accumulated frame delta
/// (<see cref="_time"/>), never wall-clock, never engine RNG.
///
/// <para><b>U9 (world-and-interiors plan, KTD-4) — one shared instance, two borrowing hosts.</b>
/// This class used to be constructed and owned by <c>DepthsPanel</c> alone; it is now a SINGLE
/// instance <c>MainUi</c> constructs once and <see cref="Refresh"/>es unconditionally every tick
/// (regardless of which host currently shows it), while <c>DepthsPanel</c> and <c>ScryingMirror</c>
/// each borrow it via their own <c>MountWatch</c> — always by stealing it from wherever it
/// currently sits (<c>Node.RemoveChild</c> before <c>AddChild</c>), so there is never a moment
/// with two parents, let alone two live <see cref="SubViewport"/>s (constraint 4's central
/// hazard). <see cref="ForceRevealWhilePaused"/> exists because the two hosts differ in one load-
/// bearing way: <c>DepthsPanel</c> is a drawer that never touches <see cref="PhaseClock"/>, while
/// <c>ScryingMirror</c> is a modal that always force-pauses it on open.</para>
///
/// <para><b>State model.</b> The marching party is the most recent <see cref="PartyDeparted"/>
/// party, cached across ticks — a <see cref="GameEvent"/> batch is momentary (only live in
/// <c>Adapter.LastEvents</c> for the one <see cref="Refresh"/> call right after its tick), so a
/// party that departed at the Expedition tick must still be remembered through the Camp/
/// ExpeditionDeep ticks that follow it, which emit no <see cref="PartyDeparted"/> of their own.
/// Once a party parks (<see cref="GameState.InFlight"/> non-empty) that persistent record — the
/// same decision facts <see cref="PartyCampReport"/> and <c>CampPanel</c> read — takes over as the
/// authoritative party/hp source, because it (unlike the cache) survives a save/load and reflects
/// live camp deliveries.</para>
///
/// <para><b>Floor-milestone flash — the plan's own flagged risk, confirmed.</b>
/// <see cref="FloorRecordSet"/>/<see cref="AttributionBeatEvent"/> are emitted ONLY by
/// <c>GameSim.Drama.ExpeditionRevealSystem</c> at the <see cref="DayPhase.Evening"/> tick (verified
/// against that system's source before wiring this) — by the time <see cref="Refresh"/> sees one,
/// <c>GameState.Phase</c> has already rolled to next-day <see cref="DayPhase.Morning"/>, outside the
/// live-phase gate above. Rather than silently drop the beat the plan asked for, the milestone
/// flash (monster silhouette slide + record bark) is the one deliberate exception to that gate: it
/// force-shows the strip for <see cref="MilestoneSeconds"/> regardless of phase, then restores
/// whatever the phase gate says. The silhouette's monster kind has no event field to read (a
/// <see cref="FloorRecordSet"/> carries no monster at all; an <see cref="AttributionBeatEvent"/>'s
/// <c>Detail</c> only sometimes names one, in free text) — deterministically picked from the floor
/// number over the committed roster instead (flavor, not a specific-encounter claim). A
/// <see cref="DenThreatShifted"/> for the Mine rides the SAME flash mechanism (chore/kill-3d-residue):
/// it is the only client-side reader of that event, giving the daily den-escalation pass (<c>
/// DirectorSystem.TickDens</c>) an actual moment-in-time callout instead of a silent write — legible
/// without a new panel, and complementary (not redundant) to <c>DepthsPanel</c>'s always-on tier
/// line beneath this strip, which shows the CURRENT state rather than the moment it changed.</para>
///
/// <para><b>Graceful degrade:</b> a missing "mine-backdrop" makes
/// <see cref="HasContent"/> false and collapses the WHOLE strip forever, whatever the phase —
/// DepthsPanel behaves exactly as it did before this unit. A missing hero-class or monster art id
/// degrades that ONE figure only (no sprite, no light, never a crash) — see <see
/// cref="ResolveWalkFrames"/> for the current two-tier fallback (4-frame pixel walk, then the
/// single-frame portrait, then nothing) every registered class already clears today.</para>
/// </summary>
public partial class MineWatch : SubViewportContainer
{
    public enum WatchState
    {
        Hidden,
        Marching,
        Camped,
    }

    private const string MineVenueId = "mine";
    private static readonly Vector2I DesignSize = new(1024, 260);
    private const float StripHeight = 260f;
    private const float HeroTargetWidth = 64f;
    private const float FigureSpacing = 86f;
    private const float MonsterTargetWidth = 160f;
    private const float PropTargetWidth = 80f; // smaller than a monster, bigger than a hero — background scenery
    private const float MilestoneSeconds = 2.6f;
    private const float LowHpFraction = 0.4f; // below this, a camped hero's pose slumps
    private const float SlumpOffsetY = 14f;
    private const float SlumpRotationDegrees = 8f;
    private const int MaxFigures = 3; // PartyFormation ships parties of <=3 (v1)
    private const float BackdropSpeed = 14f; // design px/s — deliberately slow ("never-static", not a scroller)

    /// <summary>Fixed "pretend" marching velocity fed to every figure's <see cref="SpriteMotion"/>
    /// (real-walk-cycle unit, owner playtest: "very basic animations - make detailed etc") —
    /// figures never actually translate (the backdrop scrolls to sell forward motion instead; see
    /// <see cref="AnimateBackdrop"/>), so this exists purely to pin <see
    /// cref="SpriteMotion.Advance"/>'s speed ratio at a constant 1 (its own magnitude equals <see
    /// cref="MarchSpeed"/>) for a steady, deterministic full-pace gait — no RNG, no wall clock.</summary>
    private static readonly Vector2 MarchVelocity = new(140f, 0f);

    private const float MarchSpeed = 140f;

    /// <summary>How hard a heavy beat (<see cref="DelveStage.ImpactPulse"/>) punches the torch/
    /// campfire light energy — secondary motion (weight cue), decays with the same pulse.</summary>
    private const float ImpactLightPunch = 0.9f;

    /// <summary>World-nudge amplitude (px) on a heavy beat — a small camera/world shake, scaled by
    /// <see cref="DelveStage.ImpactPulse"/> so it's sharp on impact and settles with it.</summary>
    private const float WorldShakeAmplitude = 2.5f;

    private const float ShakeFrequency = 40f; // rad/s — fast enough to read as a jolt, not a sway

    /// <summary>Logical width of one backdrop tile, world/px units (the backdrop art is scaled to
    /// this width — see <see cref="RebuildBackdropTiles"/>). <c>SubViewportContainer.Stretch</c>
    /// resizes the child <see cref="SubViewport"/> to match this container's REAL on-screen width
    /// (Godot's stretch contract — the viewport is not pinned to <see cref="DesignSize"/>), so a
    /// fixed 2-tile strip stops covering the window on anything wider than 2×this. Tile count is
    /// recomputed from the container's live width every time it changes (see <see cref="_Process"/>).</summary>
    public const float BackdropTileWidth = 1024f;

    /// <summary>U25 follow-up (a): wired by <see cref="DepthsPanel"/> so the feed pauses with the
    /// clock (paused ≠ engaged — an engaged surface keeps the feed flowing per KTD3). Null in every
    /// test that never wires a <see cref="PhaseClock"/> — treated as "always playing" (the
    /// pre-U25 behavior), never a crash.</summary>
    public PhaseClock? Clock { get; set; }

    /// <summary>
    /// U9 (world-and-interiors plan, KTD-4): the shared strip is now borrowed by two hosts —
    /// <c>DepthsPanel</c> (a drawer, which never touches <see cref="Clock"/>) and
    /// <c>ScryingMirror</c> (a modal that unconditionally force-pauses <see cref="PhaseClock"/>
    /// while it is open, see <c>MainUi.OnMirrorVisibilityChanged</c>). Gating this strip's own
    /// feed/beat reveal on <c>Clock.Playing</c> — correct while <c>DepthsPanel</c> owns it, so a
    /// genuine player pause still freezes the story — would ALSO freeze it the instant a player
    /// opened the Mirror to watch it, since that open is what paused the clock in the first
    /// place. That is the exact bug <c>ScryingMirror</c>'s own feed was already fixed for (see its
    /// <c>_Process</c> remarks: "gating this feed on Playing would freeze it the instant it
    /// opens"). Each host sets this on <c>MountWatch</c> — false for <c>DepthsPanel</c> (restore
    /// the normal pause-respecting contract), true for <c>ScryingMirror</c> (always keep
    /// revealing while the player is looking at it) — so the same instance behaves correctly in
    /// both homes.
    /// </summary>
    public bool ForceRevealWhilePaused { get; set; }

    private static readonly Color AmbientTint = new(0.30f, 0.33f, 0.52f); // dark-cool — contrast for the warm torch/fire
    private static readonly Color TorchColor = new(1f, 0.72f, 0.42f);
    private static readonly Color CampfireColor = new(1f, 0.55f, 0.24f);
    private static readonly Color MonsterTint = new(0.22f, 0.20f, 0.26f, 0.92f); // dark-modulated silhouette

    /// <summary>Committed Mine monster ids (art wave, `art-manifest.json`) — the milestone flash's
    /// silhouette picks deterministically from this roster by floor number (see type remarks: no
    /// event field names the actual monster). APPEND as the roster grows; never reorder existing
    /// entries (keeps the floor->id mapping stable for anyone who screenshots it).</summary>
    private static readonly string[] MonsterRoster =
    [
        "cave-rat", "tunnel-spider", "deep-ghoul", "ore-golem", "forgeworm",
    ];

    /// <summary>Fixed screen-space anchor slots for venue-specific static Prop decoration (never
    /// random — determinism/testability, repo convention): index 0 holds a venue's first prop id,
    /// index 1 its second. Extend this before any venue needs a 3rd.</summary>
    private static readonly Vector2[] VenuePropSlots =
    [
        new(70f, StripHeight - 30f),
        new(954f, StripHeight - 50f),
    ];

    /// <summary>Committed Prop ids (art wave, <c>GloomwoodSpecs.cs</c>/<c>SunkenCryptSpecs.cs</c>)
    /// per RAIDED (sim) venue id — three generated, normal-mapped props that resolved through
    /// <c>ArtWiringCoverageTests.VenueProps_ResolveWithNormal</c> but nothing ever drew them until
    /// this table (repo <c>fix/u8-mount-the-unmounted-props</c>): "an id resolves to a texture" and
    /// "something draws it" had silently drifted apart. Keyed by the SAME sim venue id <see
    /// cref="ResolveVenueId"/>/<see cref="BackdropVenueId"/> already report ("sunken-crypt", not
    /// "sunkencrypt" — see <see cref="AssetCatalog.VenueArtId"/>'s own split); each prop id's own
    /// art-side prefix is already baked into the literal, so no further mapping is needed here.</summary>
    private static readonly ImmutableDictionary<string, string[]> VenuePropIdsByVenueId =
        ImmutableDictionary.CreateRange(new[]
        {
            KeyValuePair.Create("gloomwood", new[] { "gloomwood-mushroom-cluster", "gloomwood-toll-booth" }),
            KeyValuePair.Create("sunken-crypt", new[] { "sunkencrypt-donation-plate" }),
        });

    /// <summary>Resolved animation-frame set for one hero figure — the SAME <c>town2d-hero-*</c>
    /// pixel family <see cref="GodotClient.Town2d.HeroActor2D"/> walks the town with (base +
    /// _walk2/_step/_walk4, <c>tools/art/gen_town_sprites.py</c>'s U3 gait), preferred because it
    /// already bakes a per-class garment colour into the art (see that script's COLOUR + MATERIAL
    /// PASS section) — <see cref="BakedColor"/> true, so the sprite's own modulate stays white.
    /// Falls back to the single-frame lit <see cref="AssetCatalog.HeroPortrait"/> (still
    /// neutral-tinted, <see cref="BakedColor"/> false) for any class the pixel set hasn't shipped —
    /// same graceful-degrade contract as everything else in this file, just one frame instead of
    /// four instead of zero.</summary>
    private readonly record struct WalkFrames(Texture2D Base, Texture2D? Walk2, Texture2D? Step, Texture2D? Walk4, bool BakedColor);

    /// <summary>One marching/camped hero figure. <see cref="Motion"/> is the SAME
    /// <see cref="SpriteMotion"/> pose driver the town uses for walking/idle NPCs — reused
    /// verbatim (<see cref="AnimateFigures"/>) rather than a second animation system. <see
    /// cref="BaseRotationDegrees"/> carries the camp low-hp slump baseline set once at construction
    /// (<see cref="RenderCamp"/>); <see cref="AnimateFigures"/> adds the pose's own lean on top of
    /// it every frame, never replacing it, so a slumped figure stays visibly slumped through every
    /// walk/breath frame. <see cref="SizeScale"/> is the fixed <see cref="HeroTargetWidth"/> scale
    /// (<see cref="ScaleToWidth"/>) the pose's own squash/breathe multiplies on top of.</summary>
    private sealed class Figure
    {
        public required Sprite2D Sprite;
        public required Vector2 BasePosition;
        public required HeroId HeroId;
        public required SpriteMotion Motion;
        public required Vector2 SizeScale;
        public required WalkFrames Frames;
        public float BaseRotationDegrees;
    }

    private SubViewport _viewport = null!;
    private Node2D _world = null!;
    private CanvasModulate _ambient = null!;
    private Texture2D? _backdropTexture;
    private readonly List<Sprite2D> _backdropTiles = [];
    private float _backdropContainerWidth = -1f; // -1 forces the first RebuildBackdropTiles call

    /// <summary>Static Prop decoration mounted for the strip's current raided venue (see
    /// <see cref="VenuePropIdsByVenueId"/>/<see cref="ApplyVenueProps"/>) — empty for the Mine or
    /// any venue with no entry there.</summary>
    private readonly List<Sprite2D> _venueProps = [];

    /// <summary>U9 (KTD-4): which venue's art the backdrop currently shows — starts at the Mine
    /// (matches <see cref="Build()"/>'s own default) and swaps via <see cref="ApplyVenueBackdrop"/>
    /// the first time <see cref="Refresh"/> can resolve a REAL raided venue off <see
    /// cref="GameState.InFlight"/>/<see cref="GameState.PendingExpeditions"/>. Never reset back to
    /// Mine on its own — between raids (both empty) the strip is Hidden anyway, so the stale value
    /// is invisible and the next departure's own Refresh resolves the real venue again.</summary>
    private string _backdropVenueId = MineVenueId;
    private PointLight2D _torch = null!;
    private PointLight2D _campfireLight = null!;
    private CpuParticles2D _embers = null!;
    private Sprite2D _monsterSlide = null!;
    private Label _recordBark = null!;
    private GradientTexture2D _lightGradient = null!;

    private readonly List<Figure> _figures = [];
    private ImmutableList<HeroId> _currentParty = ImmutableList<HeroId>.Empty;
    private float _time;
    private float _milestoneRemaining;
    private bool _built;

    /// <summary>U16 (KTD11): the in-panel journey feed — one <see cref="JourneyFeed"/> cache
    /// driving a text line under the marching/camped figures above. MineWatch shows exactly ONE
    /// party's feed (the same party its figures already track); multi-party support (PARTY TABS)
    /// lives on the bigger <c>ScryingMirror</c> surface this strip's click expands to.</summary>
    private readonly JourneyFeed _feed = new();
    private Label _feedLabel = null!;

    /// <summary>
    /// U2 (the send-off unit): the departure slate — every player-crafted item the tracked party
    /// carries, named ONCE and legibly, instead of sharing a few lines of scroll budget with
    /// roll-call text and (once combat starts) getting swept away by beats before a player could
    /// read it. See <see cref="UpdateDepartureSlate"/> for why this is a direct child of
    /// <c>this</c> (the <see cref="SubViewportContainer"/>) rather than of <see cref="_viewport"/>
    /// (the <see cref="SubViewport"/> itself).
    /// </summary>
    private PanelContainer _departureSlate = null!;
    private VBoxContainer _departureSlateBody = null!;

    /// <summary>The departure slate's currently rendered lines, in manifest order — either every
    /// <see cref="JourneyManifestLine.Text"/> the tracked party carries, or a single honest
    /// empty-state sentence when nobody does (test/tuning hook, U2).</summary>
    public ImmutableList<string> DepartureSlateLines { get; private set; } = ImmutableList<string>.Empty;

    /// <summary>A2 (+A3 FX), plan <c>2026-07-28-001</c> Part 2: the beat-driven combat overlay
    /// (floor chip, current-floor monster + HP bar, hit/quaff/death-cloud FX) layered over the
    /// figures built above. Mounted as a sibling of <see cref="_world"/> (never a descendant) —
    /// same "never dark-tinted" reasoning as <see cref="_recordBark"/>/<see cref="_feedLabel"/>.
    /// Reads the SAME <see cref="DelveBeats"/> projection <see cref="_feed"/>'s caption line
    /// reads, via its OWN <see cref="JourneyPlayhead"/> (<see cref="_delveHead"/>) bound on the
    /// <see cref="DelveBeat"/> count — a second, independent time-stretch of the same underlying
    /// story, exactly the "delve stage = 3rd renderer of the same feed" the plan calls for.</summary>
    private DelveStage _delveStage = null!;

    private ImmutableList<DelveBeat> _delveBeats = ImmutableList<DelveBeat>.Empty;
    private readonly JourneyPlayhead _delveHead = new();
    private int _delvePartyKey = int.MinValue;
    private int _delveRendered;
    private ImmutableSortedDictionary<int, Hero> _delveHeroes = ImmutableSortedDictionary<int, Hero>.Empty;

    /// <summary>The beat-driven overlay (test/tuning hook).</summary>
    public DelveStage Delve => _delveStage;

    /// <summary>The currently revealed feed lines for the tracked party, in recorded order — the
    /// test hook AE2/KTD5 scenarios assert against (never contains a death round's real text).</summary>
    public ImmutableList<string> CurrentBeats { get; private set; } = ImmutableList<string>.Empty;

    private const int FeedVisibleLines = 3;

    /// <summary>The strip's current choreography state (test/tuning hook).</summary>
    public WatchState State { get; private set; } = WatchState.Hidden;

    /// <summary>
    /// 2026-08-04 (repo task #67, owner playtest: "Lower into the mine has them return??? what
    /// logic is that lol"): true during Camp/ExpeditionDeep when the tracked party is NOT actually
    /// camped (<see cref="GameState.InFlight"/> empty) — every hero's trip already resolved whole
    /// at the Expedition tick (floor 1 is structurally unstaged, or stage 1 ended badly) and is
    /// sitting in <see cref="GameState.PendingExpeditions"/> waiting to be paced out here. Without
    /// this flag the strip kept animating the marching figures and playing back the SAME beats a
    /// live descent would show, with nothing telling the player the outcome was already decided —
    /// exactly the "why did they just come back" confusion <see cref="CampPanel"/>'s own "ALREADY
    /// BACK TODAY" section exists to prevent, except that section only renders when
    /// <see cref="CampPanel.ShowModal"/> actually opens (non-empty InFlight, <c>MainUi.SyncCampModal</c>)
    /// — precisely the one case that does NOT need the reassurance. This is that same honesty,
    /// moved onto the always-visible strip for the case that DOES need it (test/tuning hook).
    /// </summary>
    public bool AlreadyBackThisCycle { get; private set; }

    /// <summary>True once "mine-backdrop" resolved — false degrades the WHOLE strip forever,
    /// whatever the phase (see type remarks).</summary>
    public bool HasContent { get; private set; }

    /// <summary>The venue id (<c>VenueRegistry</c> key, e.g. "mine"/"gloomwood"/"sunken-crypt")
    /// the currently-shown backdrop was resolved from (test/tuning hook, U9 KTD-4).</summary>
    public string BackdropVenueId => _backdropVenueId;

    /// <summary>The lit world's dark-cool ambient tint (test/tuning hook).</summary>
    public CanvasModulate Ambient => _ambient;

    /// <summary>Party figures currently drawn (test hook) — 0 while Hidden or while the current
    /// party is not yet known (live phase, no <see cref="PartyDeparted"/>/<see
    /// cref="InFlightExpedition"/> seen yet this day).</summary>
    public int FigureCount => _figures.Count;

    /// <summary>Live backdrop tile count (test hook) — <c>ceil(containerWidth/BackdropTileWidth)+1</c>.</summary>
    public int BackdropTileCount => _backdropTiles.Count;

    /// <summary>True while the milestone flash is sliding the monster silhouette (test hook).</summary>
    public bool MonsterSlideVisible => _monsterSlide.Visible;

    /// <summary>Current left-edge X of every backdrop tile, world/px units (test hook) — each tile
    /// spans <c>[X, X+BackdropTileWidth)</c>; used to assert full-width coverage through a scroll cycle.</summary>
    public IReadOnlyList<float> BackdropTileX => _backdropTiles.Select(t => t.Position.X).ToList();

    /// <summary>Build with the real committed backdrop id.</summary>
    public void Build() => Build(AssetCatalog.VenueBackdropId(MineVenueId));

    /// <summary>
    /// Build the SubViewport world. Injectable backdrop id (tests exercise the graceful-degrade
    /// path with a fake one). Idempotent-guarded.
    /// </summary>
    public void Build(string backdropId)
    {
        if (_built)
        {
            return;
        }

        Name = "MineWatch";
        Stretch = true;
        MouseFilter = MouseFilterEnum.Ignore; // decoration only — never eats a click
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        CustomMinimumSize = Vector2.Zero; // starts collapsed; Refresh grows it once live

        _viewport = new SubViewport
        {
            Name = "MineViewport",
            Size = DesignSize,
            HandleInputLocally = false,
            TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        AddChild(_viewport);

        _world = new Node2D { Name = "MineWorld" };
        _viewport.AddChild(_world);

        _ambient = new CanvasModulate { Name = "MineAmbient", Color = AmbientTint };
        _world.AddChild(_ambient);

        _lightGradient = BuildLightGradient();

        _backdropTexture = IconRegistry.Art(backdropId);
        HasContent = _backdropTexture is not null;
        if (HasContent)
        {
            RebuildBackdropTiles(CurrentContainerWidth());
        }

        _torch = new PointLight2D
        {
            Name = "MineTorch",
            Color = TorchColor,
            Energy = 1.2f,
            Texture = _lightGradient,
            TextureScale = 1.4f,
            Height = 24f,
            Enabled = false,
        };
        _world.AddChild(_torch);

        _campfireLight = new PointLight2D
        {
            Name = "CampfireLight",
            Color = CampfireColor,
            Energy = 1.1f,
            Texture = _lightGradient,
            TextureScale = 1.7f,
            Height = 20f,
            Enabled = false,
        };
        _world.AddChild(_campfireLight);

        _embers = new CpuParticles2D
        {
            Name = "CampfireEmbers",
            Amount = 20,
            Lifetime = 1.3,
            Emitting = false,
            OneShot = false,
            Direction = new Vector2(0, -1), // 2D node — Direction/Gravity are Vector2 (verified against GodotSharp 4.6.3; the Vector3 gotcha is CPUParticles3D's, not this one)
            Spread = 18f,
            Gravity = new Vector2(0, -26f), // embers rise on their own heat, not fall
            InitialVelocityMin = 12f,
            InitialVelocityMax = 26f,
            ScaleAmountMin = 1.2f,
            ScaleAmountMax = 2.4f,
            Color = new Color(1f, 0.55f, 0.2f),
        };
        _world.AddChild(_embers);

        _monsterSlide = new Sprite2D { Name = "MonsterSlide", Visible = false, Modulate = MonsterTint };
        _world.AddChild(_monsterSlide);

        // repo fix/u8-mount-the-unmounted-props: mount whatever venue props this strip's current
        // backdrop venue owns — "mine" (the default here) has none, so this is a no-op until
        // ApplyVenueBackdrop later swaps to a venue that does.
        ApplyVenueProps(_backdropVenueId);

        _recordBark = new Label
        {
            Name = "RecordBark",
            Visible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            Position = new Vector2(0, 10),
            Size = new Vector2(DesignSize.X, 28),
        };
        _viewport.AddChild(_recordBark); // sibling of _world — never dark-tinted by MineAmbient

        // U16: the journey feed line, a sibling of _world for the same reason _recordBark is —
        // never dark-tinted, and drawn on top of the marching/camped figures below it.
        _feedLabel = new Label
        {
            Name = "JourneyFeedLabel",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            VerticalAlignment = VerticalAlignment.Bottom,
            Position = new Vector2(12, StripHeight - 58),
            Size = new Vector2(DesignSize.X - 24, 54),
        };
        _viewport.AddChild(_feedLabel);

        // A2: sibling of _world (never dark-tinted), drawn after the feed label so its floor
        // chip/monster/FX composite on top of everything else in the strip.
        _delveStage = new DelveStage();
        _delveStage.Build();
        _viewport.AddChild(_delveStage);

        // U2 (the send-off unit): the departure slate is added to `this` — the SubViewportContainer
        // itself — NOT to _viewport like every label above. That is deliberate: ScreenObservation
        // .Descendants (the tree-walk AgentPlaytest's digest and HumanPlayer.Screen() both share)
        // stops at a SubViewport boundary and never looks inside one, so _recordBark/_feedLabel/
        // DelveStage are all invisible to that harness today. A sibling of _viewport still draws
        // on top of the SubViewport's own rendered texture (normal CanvasItem draw order — later-
        // added siblings draw over earlier ones) while staying reachable by that walk, which is the
        // one property this particular content needs: a player (or an agent playing the game)
        // actually being able to read it, not just this strip's own decorative chrome.
        _departureSlate = UiKit.Card("DepartureSlate");
        _departureSlate.MouseFilter = MouseFilterEnum.Ignore; // decoration only — never eats a click
        _departureSlate.Position = new Vector2(10f, 6f);
        _departureSlate.CustomMinimumSize = new Vector2(260f, 0f); // real wrap width — see AddLabel's own R7 remarks
        _departureSlate.Visible = false; // Refresh's first UpdateDepartureSlate call decides
        AddChild(_departureSlate);

        _departureSlateBody = new VBoxContainer { Name = "DepartureSlateBody" };
        _departureSlate.AddChild(_departureSlateBody);

        var slateHeader = new Label { Name = "DepartureSlateHeader", Text = "THE SEND-OFF" };
        slateHeader.AddThemeColorOverride("font_color", GameTheme.HeaderColor);
        slateHeader.AddThemeFontSizeOverride("font_size", GameTheme.LegibilityFloor);
        _departureSlateBody.AddChild(slateHeader);

        _built = true;
    }

    /// <summary>
    /// Rebuild the strip's choreography from the live world. Called once per tick, regardless of
    /// which host currently mounts the strip (U9, KTD-4: <c>MainUi.RefreshAll</c> — MineWatch is a
    /// single shared instance now, not something only its current host refreshes) — reads
    /// <paramref name="state"/>/<paramref name="lastEvents"/> only, never writes sim/Contracts.
    /// </summary>
    public void Refresh(GameState state, ImmutableList<GameEvent> lastEvents)
    {
        Build();

        // U9 (KTD-4): the backdrop follows the party's ACTUAL raided venue instead of always
        // showing the Mine — resolved off the live/resolved party record, which already carries a
        // VenueId (falls back to whatever was last known, "mine" on a fresh strip, whenever
        // neither source has a party to read yet).
        if (ResolveVenueId(state) is { } venueId && venueId != _backdropVenueId)
        {
            ApplyVenueBackdrop(venueId);
        }

        if (!HasContent)
        {
            ApplyHidden();
            return;
        }

        // U16 (KTD11): rebuild this tick's journey cards once per Refresh call (never per frame —
        // matches every other adapter cache in this codebase). Collapses whatever the outgoing
        // phase hadn't finished revealing first (JourneyFeed.Refresh's own contract).
        _feed.Refresh(state, lastEvents);

        foreach (var departed in lastEvents.OfType<PartyDeparted>())
        {
            _currentParty = departed.Party; // last one wins if somehow more than one party departs a tick
        }

        var live = state.Phase is DayPhase.Expedition or DayPhase.Camp or DayPhase.ExpeditionDeep;
        if (!live)
        {
            _currentParty = ImmutableList<HeroId>.Empty; // never let a stale party carry into next day
        }

        // repo task #67: Camp/ExpeditionDeep with nobody actually camped — the party's whole trip
        // already finished at the Expedition tick (see AlreadyBackThisCycle's own doc). Read BEFORE
        // the State/RenderMarch branch below so UpdateFeedLabel (called at the end of this method,
        // and every frame after from _Process) always sees this tick's real answer.
        AlreadyBackThisCycle = live && state.Phase is DayPhase.Camp or DayPhase.ExpeditionDeep && state.InFlight.IsEmpty;

        if (live && state.Phase == DayPhase.Camp && !state.InFlight.IsEmpty)
        {
            State = WatchState.Camped;
            RenderCamp(state, state.InFlight[0]);
        }
        else if (live)
        {
            State = WatchState.Marching;
            RenderMarch(state, state.InFlight.IsEmpty ? _currentParty : state.InFlight[0].Party);
        }
        else
        {
            State = WatchState.Hidden;
            ClearFigures();
            _torch.Enabled = false;
            _campfireLight.Enabled = false;
            _embers.Emitting = false;
        }

        var milestone = lastEvents.FirstOrDefault(e =>
            e is FloorRecordSet or AttributionBeatEvent or DenThreatShifted { VenueId: MineVenueId });
        if (milestone is not null)
        {
            QueueMilestone(state, milestone);
        }

        Visible = live || _milestoneRemaining > 0f;
        CustomMinimumSize = Visible ? new Vector2(0, StripHeight) : Vector2.Zero;

        UpdateFeedLabel();
        RefreshDelveBeats(state, live);
        UpdateDepartureSlate(_feed.Cards.Count > 0 ? _feed.Cards[0] : null);
    }

    /// <summary>
    /// A2: (re)source this tick's <see cref="DelveBeat"/> timeline for the beat-driven overlay —
    /// a staged party's stage-1 floors (<see cref="GameState.InFlight"/>) if one is parked, else
    /// the tracked party's finalized result (<see cref="GameState.PendingExpeditions"/>) if it
    /// resolved whole without ever staging (the ONLY source that can ever carry a <see
    /// cref="DelveBeatKind.SwallowedByDark"/> beat — <see cref="InFlightExpedition.Dead"/> is
    /// always empty in v1, so a staged party's beats never contain one; only a finalized <see
    /// cref="ExpeditionResult"/> can). Rebinds <see cref="_delveHead"/> on the SAME stable party
    /// key <see cref="JourneyStream.PartyKeyOf"/> gives <see cref="_feed"/>'s own cards, and resets
    /// the whole overlay (<see cref="DelveStage.ResetState"/>) whenever that key changes — a new
    /// tracked party, or the day rolling over to nothing tracked at all.
    /// </summary>
    private void RefreshDelveBeats(GameState state, bool live)
    {
        var staged = live && !state.InFlight.IsEmpty ? state.InFlight[0] : null;
        var resolved = live && staged is null && !state.PendingExpeditions.IsEmpty ? state.PendingExpeditions[0] : null;

        var beats = staged is not null
            ? DelveBeats.Build(staged, state.Heroes)
            : resolved is not null
                ? DelveBeats.Build(resolved, state.Heroes)
                : ImmutableList<DelveBeat>.Empty;
        var party = staged?.Party ?? resolved?.Party ?? ImmutableList<HeroId>.Empty;
        var partyKey = party.IsEmpty ? int.MinValue : JourneyStream.PartyKeyOf(party);

        if (partyKey != _delvePartyKey)
        {
            _delvePartyKey = partyKey;
            _delveRendered = 0;
            _delveStage.ResetState();
        }

        _delveBeats = beats;
        _delveHeroes = state.Heroes;
        _delveHead.Bind(partyKey, beats.Count, PhaseClock.DurationOf(state.Phase));
    }

    public override void _Process(double delta)
    {
        if (!_built)
        {
            return;
        }

        _time += (float)delta;

        // Godot's SubViewportContainer.Stretch contract resizes the child SubViewport to this
        // container's REAL on-screen width every layout pass — there is no resize signal wired
        // (repo convention: accumulated-delta polling, not events; see TabFade/gold-pop), so a
        // width change is caught here, same as every other per-frame check in this method.
        var width = CurrentContainerWidth();
        if (HasContent && !Mathf.IsEqualApprox(width, _backdropContainerWidth))
        {
            RebuildBackdropTiles(width);
        }

        AnimateBackdrop((float)delta);

        if (State != WatchState.Hidden)
        {
            AnimateFigures((float)delta);
        }

        if (_milestoneRemaining > 0f)
        {
            AnimateMilestone((float)delta);
        }

        // U16 (KTD11): accumulated-delta only, no engine Tween, no RNG — same contract as every
        // other animator in this file. U25 (a): feed pauses with the clock (paused ≠ engaged — an
        // engaged surface keeps the feed flowing per KTD3), wired via Clock/DepthsPanel. U9
        // (KTD-4): ForceRevealWhilePaused overrides this while ScryingMirror borrows the strip —
        // see that property's doc for why (Mirror force-pauses the clock on open; without the
        // override, opening it to watch the show would freeze the show).
        var feedPaused = Clock is not null && !Clock.Playing && !ForceRevealWhilePaused;
        _feed.Advance(delta, paused: feedPaused);
        UpdateFeedLabel();

        // A2 (+A3 FX): the beat-driven overlay's own playhead, same pause contract as _feed above.
        // SyncHeroSprites runs BEFORE any beat renders and BEFORE DelveStage.Process, so FX always
        // target this frame's already-bobbed figure (AnimateFigures ran earlier this same call).
        _delveStage.SyncHeroSprites(BuildHeroSpriteMap());
        _delveHead.Advance(delta, paused: feedPaused);
        var revealTarget = Math.Min(_delveHead.Revealed, _delveBeats.Count);
        for (; _delveRendered < revealTarget; _delveRendered++)
        {
            _delveStage.RenderBeat(_delveBeats[_delveRendered], _delveHeroes);
        }

        _delveStage.Process((float)delta);

        // Read AFTER DelveStage.Process so a beat rendered THIS frame (RenderBeat, just above)
        // shows up at (very nearly) full strength here instead of lagging a frame behind —
        // DelveStage.ImpactPulse is a test/tuning hook; MineWatch is its only production reader.
        // Unconditional (not gated on State): ImpactPulse decays every frame in DelveStage.Process
        // regardless of visibility, so this must run every frame too or a shake mid-decay could
        // freeze at a nonzero _world.Position the moment the strip goes Hidden mid-fight.
        AnimateWorldShake();
        if (State != WatchState.Hidden)
        {
            AnimateLightFlicker();
        }
    }

    /// <summary>Hero→sprite map for <see cref="DelveStage.SyncHeroSprites"/> — whichever figure
    /// <see cref="RenderMarch"/>/<see cref="RenderCamp"/> already built for each hero this tick,
    /// so the overlay's FX always land on the SAME body the player has been watching, never a
    /// duplicate (see <see cref="DelveStage"/> type remarks).</summary>
    private Dictionary<int, Sprite2D> BuildHeroSpriteMap() =>
        _figures.ToDictionary(f => f.HeroId.Value, f => f.Sprite);

    /// <summary>repo task #67: the honest lead line prepended whenever <see
    /// cref="AlreadyBackThisCycle"/> is true — same vocabulary as <c>CampPanel</c>'s "ALREADY BACK
    /// TODAY" section ("back from the mine; the full story awaits tonight's Ledger") so a player who
    /// has seen one recognizes the other, without naming survivors/floor/death (KTD5/AE2: the
    /// resolved beats replayed below already self-censor deaths, but this lead line must not leak
    /// anything the beats themselves don't).</summary>
    private const string AlreadyBackCaption = "Already back — the tale continues below.";

    /// <summary>Renders the tracked party's revealed beats (KTD11 time-stretch) as up to
    /// <see cref="FeedVisibleLines"/> lines, falling back to the rumor line (Expedition phase, no
    /// beats yet) or the censored idle loop (stream exhaustion) when there is nothing to show.
    /// Reserves one line for <see cref="AlreadyBackCaption"/> when <see cref="AlreadyBackThisCycle"/>
    /// (repo task #67) — the strip is replaying an already-decided run, not watching a live one, and
    /// nothing else on this always-visible surface said so.</summary>
    private void UpdateFeedLabel()
    {
        if (_feed.Cards.IsEmpty)
        {
            CurrentBeats = ImmutableList<string>.Empty;
            _feedLabel.Visible = false;
            return;
        }

        var card = _feed.Cards[0]; // one party here — ScryingMirror owns multi-party PARTY TABS
        var revealed = _feed.Revealed(card);
        CurrentBeats = revealed.Select(b => b.Text).ToImmutableList();

        var budget = AlreadyBackThisCycle ? FeedVisibleLines - 1 : FeedVisibleLines;
        var lines = CurrentBeats.TakeLast(budget).ToList();
        if (lines.Count == 0)
        {
            // U-EXP1 (Expedition-watchable — owner-flagged twice: "the player just sits there"):
            // Rumored cards carry zero JourneyBeat by design (JourneyStage's own doc; pinned by
            // JourneyStreamTests) — this branch used to be the ONLY thing the strip ever showed
            // for the entire Expedition phase, and it was a content-free "rumor has it" line with
            // no roster at all. RumoredLines below reads the SAME state this strip already has
            // (card.PartyNames, resolved once in JourneyStream) to show WHO went — the "what they
            // carry" half of the payoff moved to UpdateDepartureSlate (U2, the send-off unit):
            // RumoredLines used to also append up to FeedVisibleLines-1 manifest lines here,
            // silently dropping a party's 3rd carried item and sharing this shrinking scroll
            // budget with roll-call text and (once combat starts) combat beats — "burial, not
            // ceremony". The departure slate shows EVERY manifest line, uncapped, as its own
            // moment instead.
            // (AlreadyBackThisCycle is never true alongside a Rumored card — that stage only exists
            // at the Expedition phase, this flag only at Camp/ExpeditionDeep — so the budget above
            // never fights RumoredLines for the same line.)
            lines.AddRange(card.Stage == JourneyStage.Rumored
                ? RumoredLines(card)
                : [_feed.IdleLine(card.PartyKey)]);
        }
        else if (_feed.IsIdle(card))
        {
            lines.Add(_feed.IdleLine(card.PartyKey));
        }

        if (AlreadyBackThisCycle)
        {
            lines.Insert(0, AlreadyBackCaption);
        }

        _feedLabel.Text = string.Join("\n", lines);
        _feedLabel.Visible = Visible;
    }

    /// <summary>U-EXP1: the strip's roll-call line for the Expedition phase — who went, and for
    /// which floor. U2 (the send-off unit) moved the per-item "X carries your Y" lines this used
    /// to append (capped to <see cref="FeedVisibleLines"/>-1, silently dropping a party's 3rd
    /// carried item) OFF this scrolling label and onto <see cref="_departureSlate"/> — a moment a
    /// player can actually read, uncapped, instead of a few seconds sharing scroll budget with
    /// combat beats. <c>PipDock</c>'s own single-line <see cref="JourneyStream.DepartureLine"/> is
    /// untouched by this unit — that dock has room for exactly one line and keeps preferring the
    /// first manifest line there.</summary>
    private static IReadOnlyList<string> RumoredLines(JourneyCard card)
    {
        var names = card.PartyNames.IsEmpty ? "A party" : string.Join(", ", card.PartyNames);
        return [$"{names} set out for floor {card.TargetFloor}."];
    }

    /// <summary>
    /// U2 (the send-off unit): rebuild the departure slate from <paramref name="card"/>'s <see
    /// cref="JourneyCard.Manifest"/> — the SAME manifest <see cref="JourneyStream.BuildManifest"/>
    /// already produces for every other spectate surface (never a second builder). Every line
    /// renders, never capped — the cap this unit's plan flagged lived in the old
    /// <see cref="RumoredLines"/>, not here. An honest empty state (icon + sentence, same shape as
    /// <c>LedgerModal.AddEmptyState</c>) replaces the bare placeholder whenever nobody in the
    /// tracked party carries anything player-crafted — <see cref="JourneyStream.DepartureLine"/>'s
    /// own "A party sets out…" fallback stays exactly as-is for <c>PipDock</c>, which has no room
    /// for this ceremony; this slate does.
    ///
    /// <para>Shown for as long as this party is tracked (Rumored through Held/Resolved, mirroring
    /// <see cref="Visible"/>) rather than only at the instant of departure — gear is a roster fact
    /// that does not change mid-raid (<see cref="JourneyStream.BuildManifest"/>'s own doc), so
    /// there is nothing dishonest about it staying legible for the whole trip. Hidden together
    /// with the rest of the strip's chrome once the day exits the live window (<paramref
    /// name="card"/> is null whenever <see cref="JourneyFeed.Cards"/> has nothing to show).</para>
    /// </summary>
    private void UpdateDepartureSlate(JourneyCard? card)
    {
        // Keep the header (index 0); drop everything else and rebuild — same detach-then-defer
        // shape as every other panel rebuild in this codebase (PanelGraveyard's own doc: a
        // rebuild that never reaches a frame boundary must not leak the previous subtree).
        foreach (var child in _departureSlateBody.GetChildren().Skip(1))
        {
            _departureSlateBody.RemoveChild(child);
            PanelGraveyard.Bury(child);
        }

        if (card is null)
        {
            _departureSlate.Visible = false;
            DepartureSlateLines = ImmutableList<string>.Empty;
            return;
        }

        _departureSlate.Visible = Visible;

        if (card.Manifest.IsEmpty)
        {
            const string emptyText = "Nobody in this party carries anything you forged.";
            var row = new HBoxContainer { Name = "DepartureSlateEmptyRow" };
            _departureSlateBody.AddChild(row);
            row.AddChild(new TextureRect
            {
                Name = "DepartureSlateEmptyIcon",
                Texture = IconRegistry.Glyph("rune"),
                CustomMinimumSize = new Vector2(16f, 16f),
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = MouseFilterEnum.Ignore,
            });
            row.AddChild(new Label
            {
                Name = "DepartureSlateEmptyLabel",
                Text = emptyText,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            });
            DepartureSlateLines = ImmutableList.Create(emptyText);
            return;
        }

        foreach (var line in card.Manifest)
        {
            _departureSlateBody.AddChild(new Label
            {
                Name = $"DepartureSlateLine_{line.Item.Value}",
                Text = line.Text,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            });
        }

        DepartureSlateLines = card.Manifest.Select(m => m.Text).ToImmutableList();
    }

    // ── phase rendering ──────────────────────────────────────────────────────────────────────

    private void RenderMarch(GameState state, ImmutableList<HeroId> party)
    {
        ClearFigures();
        _campfireLight.Enabled = false;
        _embers.Emitting = false;

        var groundY = StripHeight - 70f;
        var placed = 0;
        for (var i = 0; i < party.Count && placed < MaxFigures; i++)
        {
            var built = BuildFigureSprite(state, party[i], new Vector2(120f + placed * FigureSpacing, groundY), rotation: 0f);
            if (built is not { } b)
            {
                continue; // per-figure graceful degrade — unshipped class art
            }

            _figures.Add(new Figure
            {
                Sprite = b.Sprite,
                BasePosition = b.Sprite.Position,
                HeroId = party[i],
                Motion = new SpriteMotion(party[i].Value * 1.7f), // id->phase idiom, verbatim from HeroActor2D
                SizeScale = b.Sprite.Scale,
                Frames = b.Frames,
            });
            if (placed == 0)
            {
                _torch.Position = b.Sprite.Position + new Vector2(20, -46);
                _torch.Enabled = true;
            }

            placed++;
        }

        if (placed == 0)
        {
            _torch.Enabled = false; // no known party yet (live phase, PartyDeparted not seen) — ambient only
        }
    }

    private void RenderCamp(GameState state, InFlightExpedition camp)
    {
        ClearFigures();
        _torch.Enabled = false;

        var centerX = DesignSize.X / 2f;
        var groundY = StripHeight - 60f;
        var placed = 0;
        for (var i = 0; i < camp.Party.Count && placed < MaxFigures; i++)
        {
            var heroId = camp.Party[i];
            var hp = camp.Hp.TryGetValue(heroId.Value, out var hpValue) ? hpValue : 0;
            var maxHp = state.Heroes.TryGetValue(heroId.Value, out var hero) ? hero.MaxHp : 0;
            var fraction = maxHp > 0 ? (float)hp / maxHp : 1f;
            var slumped = fraction < LowHpFraction;

            var angle = (placed - (Math.Min(camp.Party.Count, MaxFigures) - 1) / 2f) * 0.6f;
            var basePos = new Vector2(centerX + Mathf.Sin(angle) * 90f, groundY + (slumped ? SlumpOffsetY : 0f));
            var rotation = slumped ? SlumpRotationDegrees : 0f;
            var built = BuildFigureSprite(state, heroId, basePos, rotation);
            if (built is not { } b)
            {
                continue;
            }

            _figures.Add(new Figure
            {
                Sprite = b.Sprite,
                BasePosition = basePos,
                HeroId = heroId,
                Motion = new SpriteMotion(heroId.Value * 1.7f),
                SizeScale = b.Sprite.Scale,
                Frames = b.Frames,
                BaseRotationDegrees = rotation,
            });
            placed++;
        }

        _campfireLight.Position = new Vector2(centerX, groundY - 10f);
        _campfireLight.Enabled = true;
        _embers.Position = _campfireLight.Position;
        _embers.Emitting = true;
    }

    /// <summary>
    /// Resolves a hero class's walk-cycle frame set: the SAME <c>town2d-hero-{classId}</c> pixel
    /// family (base + <c>_walk2</c>/<c>_step</c>/<c>_walk4</c>) <see
    /// cref="GodotClient.Town2d.HeroActor2D"/> already walks the town with — every currently
    /// registered class (vanguard/striker/mystic/sentinel/skirmisher/occultist) ships the full
    /// 4-frame set as of the 2026-08-04 redraw, so this is the live path for every real hero
    /// today. <see cref="WalkFrames.BakedColor"/> true there tells <see cref="BuildFigureSprite"/>
    /// to leave the sprite untinted (the art already bakes a per-class garment colour — multiplying
    /// it by <see cref="ClassColors.RoleColor"/> would wash that back out, the exact bug
    /// <c>HeroActor2D</c>'s own U3 comment documents).
    ///
    /// <para>Falls back to the single-frame lit <see cref="AssetCatalog.HeroPortrait"/> (still
    /// normal-mapped, so it keeps responding to this strip's torch/campfire <see
    /// cref="PointLight2D"/>s) for any FUTURE class whose pixel set hasn't shipped — <see
    /// cref="WalkFrames.BakedColor"/> false there, so the runtime tint stays. Null only when
    /// NEITHER exists for this class — the per-figure graceful degrade <see cref="RenderMarch"/>/
    /// <see cref="RenderCamp"/> already handle.</para>
    /// </summary>
    private static WalkFrames? ResolveWalkFrames(string classId)
    {
        var baseTex = IconRegistry.Art($"town2d-hero-{classId}");
        if (baseTex is not null)
        {
            return new WalkFrames(
                baseTex,
                IconRegistry.Art($"town2d-hero-{classId}_walk2"),
                IconRegistry.Art($"town2d-hero-{classId}_step"),
                IconRegistry.Art($"town2d-hero-{classId}_walk4"),
                BakedColor: true);
        }

        var portrait = AssetCatalog.HeroPortrait(classId);
        return portrait is null ? null : new WalkFrames(portrait, null, null, null, BakedColor: false);
    }

    private (Sprite2D Sprite, WalkFrames Frames)? BuildFigureSprite(GameState state, HeroId heroId, Vector2 position, float rotation)
    {
        if (!state.Heroes.TryGetValue(heroId.Value, out var hero))
        {
            return null;
        }

        if (ResolveWalkFrames(hero.ClassId) is not { } wf)
        {
            return null; // graceful degrade — no diffuse anywhere for this class, no sprite, no crash
        }

        var sprite = new Sprite2D
        {
            Name = $"MineHero_{_figures.Count}",
            Texture = wf.Base,
            Position = position,
            RotationDegrees = rotation,
            Modulate = wf.BakedColor ? Colors.White : ClassColors.RoleColor(hero.ClassId),
        };
        ScaleToWidth(sprite, wf.Base, HeroTargetWidth);
        _world.AddChild(sprite);
        return (sprite, wf);
    }

    private void ClearFigures()
    {
        foreach (var figure in _figures)
        {
            _world.RemoveChild(figure.Sprite);
            figure.Sprite.Free();
        }

        _figures.Clear();
    }

    // ── milestone flash (floor record / attribution beat) ───────────────────────────────────────

    private void QueueMilestone(GameState state, GameEvent evt)
    {
        var floor = FloorOf(evt);
        var monsterId = MonsterRoster[Math.Abs(floor) % MonsterRoster.Length];

        _milestoneRemaining = MilestoneSeconds;
        _recordBark.Text = BarkFor(state, evt);
        _recordBark.Visible = true;

        var monsterArt = AssetCatalog.MonsterPortrait(monsterId);
        if (monsterArt is not null)
        {
            ScaleToWidth(_monsterSlide, monsterArt, MonsterTargetWidth);
            _monsterSlide.Texture = monsterArt;
            _monsterSlide.Position = new Vector2(-MonsterTargetWidth, StripHeight - 90f);
            _monsterSlide.Visible = true;
        }
    }

    private void AnimateMilestone(float delta)
    {
        _milestoneRemaining -= delta;
        var progress = 1f - Mathf.Clamp(_milestoneRemaining / MilestoneSeconds, 0f, 1f);
        var slideX = Mathf.Lerp(-MonsterTargetWidth, DesignSize.X + MonsterTargetWidth, progress);
        _monsterSlide.Position = new Vector2(slideX, _monsterSlide.Position.Y);

        if (_milestoneRemaining > 0f)
        {
            return;
        }

        _milestoneRemaining = 0f;
        _monsterSlide.Visible = false;
        _recordBark.Visible = false;
        if (State == WatchState.Hidden)
        {
            Visible = false;
            CustomMinimumSize = Vector2.Zero;
        }
    }

    private static int FloorOf(GameEvent evt) => evt switch
    {
        FloorRecordSet r => r.Floor,
        AttributionBeatEvent b => b.Floor,
        // No floor rides a den-threat shift — pick the roster entry by tier so a worse den shows a
        // correspondingly nastier silhouette (flavor only; DenTier is already 0..3, MonsterRoster 0..4).
        DenThreatShifted d => Math.Clamp(d.ThreatTier + 1, 1, MonsterRoster.Length),
        _ => 1,
    };

    private static string BarkFor(GameState state, GameEvent evt) => evt switch
    {
        FloorRecordSet r => $"{HeroLabel(state, r.Hero)} sets a new depth record — floor {r.Floor}!",
        AttributionBeatEvent b => $"{HeroLabel(state, b.Hero)} — {BeatVerb(b.Beat)} (floor {b.Floor})",
        DenThreatShifted { Lockdown: true } => "The Mine has been overrun — the routes here are locked down!",
        DenThreatShifted d => $"The Mine's depths grow restless — den threat tier {d.ThreatTier} ({d.ThreatPermille / 10}%).",
        _ => string.Empty,
    };

    private static string HeroLabel(GameState state, HeroId id) =>
        state.Heroes.TryGetValue(id.Value, out var hero) ? hero.Name : $"Hero #{id.Value}";

    private static string BeatVerb(BeatType beat) => beat switch
    {
        BeatType.KillingBlow => "killing blow",
        BeatType.LethalSave => "lethal save",
        BeatType.BreakpointClear => "breakpoint clear",
        BeatType.Provisioned => "provisioned",
        BeatType.PotionLifesave => "potion lifesave",
        BeatType.ToolAssist => "tool assist",
        _ => "notable beat",
    };

    private void ApplyHidden()
    {
        State = WatchState.Hidden;
        Visible = false;
        CustomMinimumSize = Vector2.Zero;
    }

    // ── per-frame animation (accumulated delta only — no wall clock, no RNG) ────────────────────

    private void AnimateBackdrop(float delta)
    {
        if (_backdropTiles.Count == 0)
        {
            return;
        }

        var shift = -BackdropSpeed * delta;
        var wrapSpan = _backdropTiles.Count * BackdropTileWidth; // generalized N-tile wrap
        for (var i = 0; i < _backdropTiles.Count; i++)
        {
            var tile = _backdropTiles[i];
            var x = tile.Position.X + shift;
            if (x <= -BackdropTileWidth)
            {
                x += wrapSpan;
            }

            tile.Position = new Vector2(x, 0);
        }
    }

    /// <summary>
    /// The walk cycle (link3 — "the watch becomes a fight"): every figure's <see
    /// cref="Figure.Motion"/> is the SAME <see cref="SpriteMotion"/> pose driver the town's own
    /// walking NPCs use, fed a fixed full-pace <see cref="MarchVelocity"/> while <see
    /// cref="State"/> is <see cref="WatchState.Marching"/> (figures never actually translate — the
    /// backdrop scrolls to sell forward motion, see <see cref="AnimateBackdrop"/> — this velocity
    /// exists only to pin the pose driver's speed ratio at 1, a steady full-stride gait) and zero
    /// velocity while <see cref="WatchState.Camped"/> (idle breathing, distinct from the walk bob —
    /// see <see cref="SpriteMotion.IdlePose"/>). <see cref="Figure.BaseRotationDegrees"/> (the camp
    /// low-hp slump baseline) is never replaced, only added to every frame, so a slumped figure
    /// stays visibly slumped through every walk/breath frame it plays.
    ///
    /// <para>A hero <see cref="DelveStage"/> has clouded (<see cref="DelveStage.IsClouded"/>) is
    /// skipped entirely — that figure fell (<c>DelveStage.AdvanceCloudFx</c>'s own fall-and-freeze
    /// curve) and DelveStage is now the sole authority over its Position/Rotation. Without this
    /// check, this method would re-plant a fallen figure upright and breathing every single frame,
    /// since it runs BEFORE <see cref="DelveStage.Process"/> in <see cref="_Process"/> and touches
    /// every figure unconditionally otherwise — the fall would never visibly stick.</para>
    /// </summary>
    private void AnimateFigures(float delta)
    {
        var velocity = State == WatchState.Marching ? MarchVelocity : Vector2.Zero;
        foreach (var figure in _figures)
        {
            if (_delveStage.IsClouded(figure.HeroId.Value))
            {
                continue;
            }

            var pose = figure.Motion.Advance(delta, velocity, MarchSpeed);
            figure.Sprite.Position = figure.BasePosition + new Vector2(0, pose.BobY);
            figure.Sprite.RotationDegrees = figure.BaseRotationDegrees + Mathf.RadToDeg(pose.LeanRadians);
            figure.Sprite.Scale = figure.SizeScale * pose.Scale;
            figure.Sprite.Texture = ResolveWalkFrameTexture(figure.Frames, pose.WalkFrame);
        }
    }

    /// <summary>U3's real 4-frame gait, mirrored verbatim from <see
    /// cref="GodotClient.Town2d.HeroActor2D.ResolveWalkFrameTexture"/>: maps <see
    /// cref="SpriteMotion.Pose.WalkFrame"/> (0-3) to whichever of the four resolved textures
    /// exists, falling back to the base texture for any frame this checkout is missing (a partial
    /// art drop degrades to fewer visible poses, never a crash or a null texture) — and always for
    /// the single-frame portrait fallback (<see cref="WalkFrames.Walk2"/>/<see
    /// cref="WalkFrames.Step"/>/<see cref="WalkFrames.Walk4"/> all null there).</summary>
    private static Texture2D ResolveWalkFrameTexture(WalkFrames frames, int walkFrame) => walkFrame switch
    {
        1 when frames.Walk2 is not null => frames.Walk2,
        2 when frames.Step is not null => frames.Step,
        3 when frames.Walk4 is not null => frames.Walk4,
        _ => frames.Base,
    };

    /// <summary>Torch/campfire flicker, punched by <see cref="DelveStage.ImpactPulse"/> on a
    /// landed/taken blow (<see cref="ImpactLightPunch"/>) — weight cue, decays with the pulse
    /// itself (<c>DelveStage.Process</c> owns that decay; this method only reads the current
    /// value).</summary>
    private void AnimateLightFlicker()
    {
        var punch = _delveStage.ImpactPulse * ImpactLightPunch;
        if (_torch.Enabled)
        {
            _torch.Energy = 1.2f + 0.12f * Mathf.Sin(_time * 9f) * Mathf.Sin(_time * 2.1f) + punch;
        }

        if (_campfireLight.Enabled)
        {
            _campfireLight.Energy = 1.1f + 0.18f * Mathf.Sin(_time * 11f) * Mathf.Sin(_time * 1.7f) + punch;
        }
    }

    /// <summary>A short world-nudge on a heavy beat (<see cref="WorldShakeAmplitude"/>/<see
    /// cref="ShakeFrequency"/>), scaled by <see cref="DelveStage.ImpactPulse"/> so it is sharp on
    /// impact and settles with it — a camera-shake read achieved by nudging <see cref="_world"/>
    /// itself (every tile/figure/light is one of its children, so this moves the whole scene
    /// uniformly without touching any individual figure's own <c>Position</c>, which stays the
    /// walk/breathe/combat-pose baseline those systems already own). Always resolves back to
    /// exactly <see cref="Vector2.Zero"/> once the pulse decays — <see cref="_world"/>'s own
    /// Position has no other writer in this file, so this is safe to set unconditionally.</summary>
    private void AnimateWorldShake()
    {
        var pulse = _delveStage.ImpactPulse;
        _world.Position = pulse > 0.001f
            ? new Vector2(WorldShakeAmplitude * pulse * Mathf.Sin(_time * ShakeFrequency), 0f)
            : Vector2.Zero;
    }

    // ── U9 (KTD-4): venue-true backdrop ──────────────────────────────────────────────────────

    /// <summary>The raided venue's id, read off whichever of the two live sources currently has a
    /// party (in that order — a party can never be in both at once): a parked/camped run's <see
    /// cref="InFlightExpedition.VenueId"/>, or a fully-resolved (never staged) run's <see
    /// cref="ExpeditionResult.VenueId"/>. Null when neither has a party to read yet (no departure
    /// this session, or the day has fully rolled over) — the caller leaves the backdrop exactly
    /// where it was rather than resetting to the Mine on every quiet tick.</summary>
    private static string? ResolveVenueId(GameState state) =>
        !state.InFlight.IsEmpty ? state.InFlight[0].VenueId :
        !state.PendingExpeditions.IsEmpty ? state.PendingExpeditions[0].VenueId :
        null;

    /// <summary>Swap the backdrop texture/tiles to <paramref name="venueId"/>'s art, tracking it so
    /// <see cref="Refresh"/> only pays for a rebuild when the raided venue actually changes.
    /// Graceful-degrades exactly like <see cref="Build(string)"/>'s own missing-art path: a venue
    /// with no committed backdrop collapses the WHOLE strip (<see cref="HasContent"/> false) rather
    /// than leaving stale tiles from the PREVIOUS venue on screen.</summary>
    private void ApplyVenueBackdrop(string venueId)
    {
        _backdropVenueId = venueId;
        _backdropTexture = IconRegistry.Art(AssetCatalog.VenueBackdropId(venueId));
        HasContent = _backdropTexture is not null;
        ApplyVenueProps(venueId);

        if (HasContent)
        {
            RebuildBackdropTiles(CurrentContainerWidth());
            return;
        }

        foreach (var tile in _backdropTiles)
        {
            _world.RemoveChild(tile);
            tile.Free();
        }

        _backdropTiles.Clear();
        _backdropContainerWidth = -1f; // force a rebuild if a later venue's art resolves
    }

    /// <summary>
    /// (Re)builds <see cref="_venueProps"/> for <paramref name="venueId"/> — clears whatever the
    /// PREVIOUS venue mounted (a venue absent from <see cref="VenuePropIdsByVenueId"/>, like the
    /// Mine, ends up with zero), then mounts each of its committed prop ids at its own fixed
    /// <see cref="VenuePropSlots"/> anchor. Lit exactly like a hero/monster figure — the resolved
    /// <see cref="CanvasTexture"/> assigned straight to <see cref="Sprite2D.Texture"/>, so the
    /// strip's own Light2D/CanvasModulate world picks up the committed normal map automatically
    /// (same technique as <see cref="BuildFigureSprite"/>; no special normal-map handling needed
    /// beyond that one assignment). Added to <see cref="_world"/> AFTER the backdrop/lights/FX
    /// nodes <see cref="Build(string)"/> already built and BEFORE this same <see cref="Refresh"/>
    /// call rebuilds this tick's hero figures (<see cref="RenderMarch"/>/<see cref="RenderCamp"/>
    /// always run after <see cref="ApplyVenueBackdrop"/>) — so a prop always draws in front of the
    /// scrolling backdrop and behind whichever heroes are currently marching past it. A missing
    /// prop id degrades that ONE slot only (skipped, never a crash), the same graceful-degrade
    /// contract every other resolver in this file follows.
    /// </summary>
    private void ApplyVenueProps(string venueId)
    {
        foreach (var sprite in _venueProps)
        {
            _world.RemoveChild(sprite);
            sprite.Free();
        }

        _venueProps.Clear();

        if (!VenuePropIdsByVenueId.TryGetValue(venueId, out var propIds))
        {
            return;
        }

        for (var i = 0; i < propIds.Length && i < VenuePropSlots.Length; i++)
        {
            var lit = IconRegistry.Lit(propIds[i]);
            if (lit is null)
            {
                continue; // graceful degrade — an ungenerated prop id skips its slot, never a crash
            }

            var sprite = new Sprite2D
            {
                Name = $"VenueProp_{propIds[i]}",
                Texture = lit,
                Position = VenuePropSlots[i],
            };
            ScaleToWidth(sprite, lit, PropTargetWidth);
            _world.AddChild(sprite);
            _venueProps.Add(sprite);
        }
    }

    // ── build helpers ────────────────────────────────────────────────────────────────────────

    /// <summary>The container's live width, in world/px units — <see cref="DesignSize"/> until the
    /// engine's first <c>NOTIFICATION_RESIZED</c> layout pass sizes this
    /// <see cref="SubViewportContainer"/> for real (Stretch contract; see <see cref="_Process"/>).</summary>
    private float CurrentContainerWidth() => Size.X > 0f ? Size.X : DesignSize.X;

    /// <summary>
    /// (Re)builds the backdrop as <c>ceil(containerWidth / <see cref="BackdropTileWidth"/>) + 1</c>
    /// tiles, laid edge-to-edge from x=0 — enough that, combined with the wrap in
    /// <see cref="AnimateBackdrop"/>, the strip has no seam-free gap at ANY scroll offset for a
    /// container of this width (the "+1" covers the one tile mid-wrap off the left edge). Odd tiles
    /// are <see cref="Sprite2D.FlipH"/>'d — the art isn't tileable, so alternating the flip breaks
    /// the repeating mirror-seam read instead of hard-wrapping the same edge into itself every tile.
    /// </summary>
    private void RebuildBackdropTiles(float containerWidth)
    {
        foreach (var tile in _backdropTiles)
        {
            _world.RemoveChild(tile);
            tile.Free();
        }

        _backdropTiles.Clear();
        _backdropContainerWidth = containerWidth;

        if (_backdropTexture is null)
        {
            return;
        }

        var scale = new Vector2(BackdropTileWidth / _backdropTexture.GetWidth(), StripHeight / _backdropTexture.GetHeight());
        var tileCount = (int)Mathf.Ceil(containerWidth / BackdropTileWidth) + 1;
        for (var i = 0; i < tileCount; i++)
        {
            var tile = new Sprite2D
            {
                Name = $"MineBackdrop_{i}",
                Texture = _backdropTexture,
                Centered = false, // (x,0) is the top-left corner — maps 1:1 onto pixel space
                Scale = scale,
                Position = new Vector2(i * BackdropTileWidth, 0),
                FlipH = i % 2 == 1,
            };
            _world.AddChild(tile);
            _backdropTiles.Add(tile);
        }
    }

    /// <summary>Scale a Sprite2D so its texture renders at <paramref name="targetWidth"/> px. Takes
    /// the plain <see cref="Texture2D"/> base type so ONE method covers both callers: a lit <see
    /// cref="CanvasTexture"/> (portrait/prop/monster art — width read off <see
    /// cref="CanvasTexture.DiffuseTexture"/>, since <c>CanvasTexture.GetWidth()</c> itself is not
    /// reliable) and a plain pixel <see cref="Texture2D"/> (<see cref="WalkFrames.Base"/> and
    /// friends, loaded via <see cref="IconRegistry.Art"/> — no CanvasTexture wrapper, so its own
    /// <c>GetWidth()</c> is correct as-is). Not shared across lanes — same call CampPanel's
    /// mirrored SupplyFee constant makes.</summary>
    private static void ScaleToWidth(Sprite2D sprite, Texture2D texture, float targetWidth)
    {
        var width = texture is CanvasTexture canvas ? canvas.DiffuseTexture?.GetWidth() ?? 0 : texture.GetWidth();
        if (width > 0)
        {
            sprite.Scale = Vector2.One * (targetWidth / width);
        }
    }

    /// <summary>The pilot's radial falloff recipe: white core → 0.45 alpha at 0.55 → transparent
    /// edge, radial fill. Duplicated for the same cross-lane reason as <see
    /// cref="ScaleToWidth"/>.</summary>
    private static GradientTexture2D BuildLightGradient()
    {
        var gradient = new Gradient
        {
            Colors = [new Color(1, 1, 1, 1), new Color(1, 1, 1, 0.45f), new Color(1, 1, 1, 0)],
            Offsets = [0f, 0.55f, 1f],
        };
        return new GradientTexture2D
        {
            Gradient = gradient,
            Width = 512,
            Height = 512,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(1f, 0.5f),
        };
    }
}
