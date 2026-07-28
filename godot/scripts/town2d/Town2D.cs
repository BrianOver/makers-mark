using System;
using System.Collections.Generic;
using System.Linq;
using GameSim.Contracts;
using Godot;
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
    // Nominal SubViewport size. NOTE: the SubViewportContainer has Stretch=true, which overrides the
    // live viewport size to the container's pixel size — so the actual zoom is set by the Camera2D's
    // integer Zoom (see Build), not this constant. Kept for the overview-capture math + tests.
    public const int ViewportWidth = 640;
    public const int ViewportHeight = 360;

    /// <summary>Integer camera zoom for the Stardew-close framing (2x = crisp, no fractional shimmer).</summary>
    private const float CameraZoom = 2f;

    private const int TileSize = TownLayout2D.TileSize;

    /// <summary>Party-file rally spacing (px) along X — mirrors <c>Town3D.RallySpotFor</c>'s spread
    /// so a departing party reads as a cluster, not a stack.</summary>
    private const float RallySpacingPx = 14f;

    /// <summary>Dusk-purple ambient tint (pivot plan §"Node architecture": "~#b9a3d0", i.e.
    /// RGB 185/163/208 normalized) — a literal float triple rather than a hex-string parse to
    /// avoid depending on a specific <see cref="Color"/> parsing overload being present.</summary>
    private static readonly Color DuskTint = new(0.86f, 0.80f, 0.93f);

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

    /// <summary>The adapter <see cref="Build"/> was given. Null only before <see cref="Build"/> has
    /// run.</summary>
    public SimAdapter? Adapter { get; private set; }

    /// <summary>T8-parity no-op setter (Town3D's own <see cref="GodotClient.Town3d.Town3D.Clock"/>
    /// doc applies verbatim here — no per-frame decoration in this slice keys off clock state).</summary>
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

    private Building2D? _forgeBuilding;
    private Sprite2D? _forgeGlowOverlay;
    private CpuParticles2D? _forgeSparks;
    private CpuParticles2D? _forgeSteam;
    private AmbientLife2D? _ambientLife;

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

        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        ViewportContainer = new SubViewportContainer
        {
            Name = "ViewportContainer",
            Stretch = true,
            TextureFilter = TextureFilterEnum.Nearest,
        };
        ViewportContainer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(ViewportContainer);

        WorldViewport = new SubViewport
        {
            Name = "Viewport",
            Size = new Vector2I(ViewportWidth, ViewportHeight),
            HandleInputLocally = true,
            PhysicsObjectPicking = true,
            Snap2DTransformsToPixel = true,
            Snap2DVerticesToPixel = true,
            CanvasItemDefaultTextureFilter = Viewport.DefaultCanvasItemTextureFilter.Nearest,
        };
        ViewportContainer.AddChild(WorldViewport);

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

        Player = new PlayerController2D { Name = "Player" };
        YSort.AddChild(Player);
        var forgeDoor = FindBuilding("forge").DoorAnchorGlobal;
        Player.SpawnAt(forgeDoor);

        HeroesRoot = new Node2D { Name = "Heroes" };
        YSort.AddChild(HeroesRoot);

        TownsfolkRoot = new Node2D { Name = "Townsfolk" };
        YSort.AddChild(TownsfolkRoot);
        BuildTownsfolk();

        Fx = new Node2D { Name = "Fx" };
        World.AddChild(Fx);

        DuskModulate = new CanvasModulate { Name = "DuskModulate", Color = DuskTint };
        World.AddChild(DuskModulate);

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
    }

    /// <summary>T8-parity drop-in for the old <c>Bind(SimAdapter)</c> call site (see
    /// <c>Town3D.Bind</c>'s own doc — <see cref="Build"/> already assigned <see cref="Adapter"/>
    /// and reconciled once, so this is a harmless re-assignment either way).</summary>
    public void Bind(SimAdapter adapter)
    {
        Adapter = adapter;
        ReconcileHeroes();
    }

    /// <summary>T8-parity drop-in for <c>Refresh()</c> — called every tick the world is visible.</summary>
    public void Refresh() => ReconcileHeroes();

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

    /// <summary>Live hero-actor count (test/inspection surface).</summary>
    public int HeroActorCount() => _heroActors.Count;

    /// <summary>Live cosmetic-villager count (test/inspection surface) — mirrors <see
    /// cref="HeroActorCount"/>'s shape for <see cref="TownsfolkNpc2D"/>.</summary>
    public int TownsfolkCount() => TownsfolkRoot.GetChildCount();

    /// <summary>The lowest-HeroId live actor (test/inspection surface) — deterministic even though
    /// dictionary enumeration order is an implementation detail. Throws if <see
    /// cref="ReconcileHeroes"/> has never produced any actor.</summary>
    public HeroActor2D FirstHeroActor() => _heroActors.Values.OrderBy(a => a.HeroIdValue).First();

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
            actor.QueueFree();
        }
    }

    /// <summary>
    /// Phase-transition choreography (mirrors <c>Town3D.OnPhaseCompleted</c>): Morning done → every
    /// wandering actor rallies then marches out the gate. Expedition/ExpeditionDeep done →
    /// survivors of any FINALIZED run walk home. Evening done → every remaining non-wandering
    /// actor whose hero is confirmed alive snaps home for the new day. Camp/unknown → no-op.
    /// </summary>
    public void OnPhaseCompleted(DayPhase completedPhase)
    {
        if (Adapter is null)
        {
            return;
        }

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
    /// cref="ReconcileHeroes"/> already freed (e.g. the hero died the same tick it was mustering).</summary>
    public override void _Process(double delta)
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

    private void ReturnSurvivors()
    {
        var survivors = Adapter!.CurrentState.PendingExpeditions
            .SelectMany(expedition => expedition.Survivors)
            .Select(id => id.Value)
            .ToHashSet();

        foreach (var actor in _heroActors.Values
                     .Where(a => a.State == HeroActor2D.HeroTownState.Away && survivors.Contains(a.HeroIdValue)))
        {
            actor.ReturnTo(HomeFor(actor.HeroIdValue));
        }
    }

    private void SnapRemainingHeroesHome()
    {
        var heroes = Adapter!.CurrentState.Heroes;
        foreach (var actor in _heroActors.Values.Where(a =>
                     a.State != HeroActor2D.HeroTownState.Wandering &&
                     heroes.TryGetValue(a.HeroIdValue, out var hero) && hero.Alive))
        {
            actor.SetState(HeroActor2D.HeroTownState.Wandering);
        }
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
            building.Configure(venue.Key, venue.Nametag, sprite, TownLayout2D.TileToWorld(venue.Tile));
            building.Picked += key => BuildingClicked?.Invoke(key);
            BuildingsRoot.AddChild(building);
            _buildingsByKey[venue.Key] = building;
        }
    }

    /// <summary>Fallback footprint for a prop whose resolved texture reports a zero/negative size
    /// (mirrors <see cref="Building2D"/>'s own <c>FallbackSize</c> guard) — small enough to stay
    /// unobtrusive if it's ever hit.</summary>
    private static readonly Vector2 PropFallbackSize = new(16f, 16f);

    /// <summary>
    /// Instantiates every <see cref="TownLayout2D.Props"/> entry (well, lanterns, trees, crates) —
    /// called once from <see cref="Build"/>, right after <see cref="BuildBuildings"/> so <see
    /// cref="YSort"/> already exists. Each prop is a bare <see cref="Sprite2D"/> positioned via the
    /// SAME feet-origin convention <see cref="Building2D.Configure"/> uses for buildings (<see
    /// cref="Sprite2D.Offset"/> shifted up by half the sprite's height so its BOTTOM edge lands on
    /// <see cref="TownLayout2D.TileToWorld"/>'s tile-center position) — required for <see
    /// cref="YSort"/>'s <c>YSortEnabled</c> parent to sort heroes/the player correctly in front of
    /// or behind a tall prop like a tree or the well, exactly as it already does for buildings.
    /// Y-sorted props mount under <see cref="YSort"/>; a flat (non-Y-sorted) prop would mount under
    /// <see cref="Ground"/> instead, but <see cref="TownLayout2D.Props"/> has none of those yet.
    /// </summary>
    private void BuildProps()
    {
        foreach (var prop in TownLayout2D.Props)
        {
            var sprite = TownAssets2D.ForProp(prop.SpriteId);
            var size = sprite?.GetSize() ?? PropFallbackSize;
            if (size.X <= 0f || size.Y <= 0f)
            {
                size = PropFallbackSize;
            }

            var node = new Sprite2D
            {
                Name = $"Prop_{prop.SpriteId}_{prop.Tile.X}_{prop.Tile.Y}",
                Texture = sprite,
                Centered = true,
                Offset = new Vector2(0f, -size.Y / 2f), // bottom edge lands on the tile's center (feet-origin)
                Position = TownLayout2D.TileToWorld(prop.Tile),
            };

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
        for (var i = 0; i < TownsfolkHomeTiles.Length; i++)
        {
            var npc = new TownsfolkNpc2D();
            npc.Init(i, TownsfolkNpc2D.ResolveSprite(), TownsfolkNpc2D.CivilianTint(i), TownLayout2D.TileToWorld(TownsfolkHomeTiles[i]));
            TownsfolkRoot.AddChild(npc);
        }
    }

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

        var forgeChimneyPos = _forgeBuilding!.GlobalPosition + new Vector2(18f, -70f);
        var tavernChimneyPos = FindBuilding("tavern").GlobalPosition + new Vector2(14f, -58f);
        var townRect = new Rect2(0f, 0f, TownLayout2D.GridWidth * TileSize, TownLayout2D.GridHeight * TileSize);
        var lanternPositions = TownLayout2D.Props
            .Where(prop => prop.SpriteId == "town2d-prop-lantern")
            .Select(prop => TownLayout2D.TileToWorld(prop.Tile))
            .ToList();

        _ambientLife.Build(forgeChimneyPos, tavernChimneyPos, townRect, lanternPositions);
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
    /// Paints the whole grid with a flat grass tile (a one-tone <see cref="TileMapLayer"/> — no
    /// autotile logic in this slice, U6/U7 swap in a real ground atlas by replacing <see
    /// cref="BuildTileSet"/>'s source image), then paints every tile inside <see
    /// cref="TownLayout2D.PathRects"/> with the cobble tile — the plaza/road/spur network reading
    /// as one continuous cozy-village street, purely for legibility (decoration only — <see
    /// cref="Building2D"/> carries its own blocking <c>Footprint</c>, so nothing here needs
    /// collision).
    /// </summary>
    private static TileMapLayer BuildGround()
    {
        var layer = new TileMapLayer { Name = "Ground", TileSet = BuildTileSet() };

        for (var y = 0; y < TownLayout2D.GridHeight; y++)
        {
            for (var x = 0; x < TownLayout2D.GridWidth; x++)
            {
                layer.SetCell(new Vector2I(x, y), 0, GrassAtlasCoord);
            }
        }

        foreach (var rect in TownLayout2D.PathRects)
        {
            for (var y = rect.Position.Y; y < rect.Position.Y + rect.Size.Y; y++)
            {
                for (var x = rect.Position.X; x < rect.Position.X + rect.Size.X; x++)
                {
                    layer.SetCell(new Vector2I(x, y), 0, CobbleAtlasCoord);
                }
            }
        }

        return layer;
    }

    private static readonly Vector2I GrassAtlasCoord = new(0, 0);
    private static readonly Vector2I CobbleAtlasCoord = new(1, 0);

    private static readonly Color GrassColor = new(0.30f, 0.45f, 0.22f);
    private static readonly Color CobbleColor = new(0.42f, 0.40f, 0.38f);

    /// <summary>
    /// A minimal 2-tile flat-color atlas (grass/cobble) built entirely in code — no <c>.tres</c>
    /// churn, no imported ground art required for the slice to run. U6/U7 replace <see
    /// cref="GrassColor"/>/etc. (or the whole method) with a real imported atlas texture without
    /// touching <see cref="BuildGround"/>'s cell-painting logic.
    /// </summary>
    private static TileSet BuildTileSet()
    {
        var image = Image.CreateEmpty(TileSize * 2, TileSize, false, Image.Format.Rgba8);
        image.FillRect(new Rect2I(0, 0, TileSize, TileSize), GrassColor);
        image.FillRect(new Rect2I(TileSize, 0, TileSize, TileSize), CobbleColor);
        var texture = ImageTexture.CreateFromImage(image);

        var atlas = new TileSetAtlasSource { Texture = texture, TextureRegionSize = new Vector2I(TileSize, TileSize) };
        atlas.CreateTile(GrassAtlasCoord);
        atlas.CreateTile(CobbleAtlasCoord);

        var tileSet = new TileSet { TileSize = new Vector2I(TileSize, TileSize) };
        tileSet.AddSource(atlas, 0);
        return tileSet;
    }
}
