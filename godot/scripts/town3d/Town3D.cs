using System;
using System.Collections.Generic;
using System.Linq;
using GameSim.Contracts;
using Godot;

namespace GodotClient.Town3d;

/// <summary>
/// T3+: standalone scaffold for the grounded 3D town — a <see cref="SubViewportContainer"/>
/// hosting a picking-enabled <see cref="SubViewport"/> whose <see cref="World"/> is a plain
/// <see cref="Node3D"/> with a box-floor ground, a <see cref="CameraRig"/>, ambient light, six
/// interactable <see cref="Buildings"/> (T5), a display-only <see cref="MemorialPlot"/> (T5,
/// populated T7), a synchronously-baked <see cref="NavRegion"/> (T6) driving the player's
/// navmesh click-to-move, and <see cref="WorldInputNode"/> driving proximity/highlight/interact
/// plus camera-ray click resolution (T5/T6). The <see cref="Heroes"/> container stays empty
/// until T7.
///
/// <para>Deliberately standalone: this task mounts <see cref="Town3D"/> directly in a
/// code-built test rig (see <c>Town3DSceneTests</c>) rather than through <c>MainUi</c> — the
/// <c>MainUi</c> cutover is a single atomic task (T8) at the end of the plan.</para>
/// </summary>
public partial class Town3D : SubViewportContainer
{
    public SubViewport Viewport { get; private set; } = null!;
    public Node3D World { get; private set; } = null!;
    public CameraRig Camera { get; private set; } = null!;
    public Node3D Buildings { get; private set; } = null!;
    public Node3D Heroes { get; private set; } = null!;

    /// <summary>T4: the player avatar; spawned in <see cref="Build"/> and followed by
    /// <see cref="Camera"/>.</summary>
    public PlayerController Player { get; private set; } = null!;

    /// <summary>T5: display-only landmark (mirrors <c>TownScene</c>'s memorial corner) — no
    /// collider, no interact zone. Per-hero stones are added by <c>ReconcileHeroes</c> (T7); this
    /// task only places the container so the plot has a fixed home in the world.</summary>
    public Node3D MemorialPlot { get; private set; } = null!;

    /// <summary>T5: proximity target + prompt + interact — scans <see cref="Buildings"/> against
    /// <see cref="Player"/> every physics frame.</summary>
    public WorldInput3D WorldInputNode { get; private set; } = null!;

    /// <summary>T6: baked once in <see cref="Build"/> after <see cref="Buildings"/> (and the
    /// ground) exist as its descendants — <see cref="Godot.NavigationMesh.SourceGeometryMode"/>
    /// defaults to scanning the region's own subtree, so both must be reparented under here
    /// rather than left as <see cref="World"/> siblings for the bake to see them at all.</summary>
    public NavigationRegion3D NavRegion { get; private set; } = null!;

    /// <summary>Raised when a building is clicked/interacted with (T5+); re-emits into
    /// <c>MainUi</c>'s existing 2D-town vocabulary unchanged (KTD2 — presentation-only).</summary>
    public event System.Action<string>? BuildingClicked;

    /// <summary>Raised when a hero actor is clicked (T7+).</summary>
    public event System.Action<int>? HeroClicked;

    /// <summary>T6: gates <see cref="WorldInputNode"/> off entirely (proximity scan, camera-ray
    /// clicks, "interact") — T8 drives this from <c>MainUi</c>'s engaged-state so a drawer/
    /// interior/modal owns input instead of the world underneath it.</summary>
    public void SetWorldInputEnabled(bool enabled) => WorldInputNode.Enabled = enabled;

    /// <summary>
    /// T8: drop-in replacement for the old <c>TownScene.Bind(SimAdapter)</c> call site — <see
    /// cref="Build"/> already assigned <see cref="Adapter"/> and ran one reconcile by the time
    /// <c>MainUi._Ready</c> calls this, so re-storing it here is a harmless no-op re-assignment;
    /// the follow-up <see cref="ReconcileHeroes"/> call keeps behavior identical to the pre-cutover
    /// two-call sequence (<c>Build</c> then <c>Bind</c>) either way.
    /// </summary>
    public void Bind(GodotClient.SimAdapter adapter)
    {
        Adapter = adapter;
        ReconcileHeroes();
    }

    /// <summary>T8: drop-in replacement for <c>TownScene.Refresh()</c> — <c>MainUi.RefreshAll</c>
    /// calls this every tick the world is visible (which, per U21, is always).</summary>
    public void Refresh() => ReconcileHeroes();

    /// <summary>
    /// T8: interface parity with the old <c>TownScene.Clock</c> setter <c>MainUi.BuildUi</c> already
    /// assigns — a no-op here. The 2D town read the live clock to pause its own ambient-light
    /// crossfade tween; this 3D scaffold has no such per-frame decoration keyed off clock state, so
    /// there's nothing for the value to drive yet.
    /// </summary>
    public GodotClient.PhaseClock? Clock { set { } }

    /// <summary>
    /// T8: venue → door-anchor lookup for <c>MainUi.OpenInterior</c>/<c>ResetAvatarToDoor</c> — the
    /// world-space point <see cref="Player"/> should stand at while (and be restored to, on interior
    /// exit) <paramref name="venueKey"/>'s interior is staged. Null for an unrecognized key
    /// (defensive only — every venue key MainUi passes is one of <see cref="BuildingLayout"/>'s own
    /// keys).
    /// </summary>
    public Vector3? DoorAnchor(string venueKey) =>
        Buildings.GetChildren().OfType<Building3D>().FirstOrDefault(b => b.Key == venueKey)?.DoorAnchorGlobal;

    /// <summary>T7: the adapter <see cref="Build"/> was given — <see cref="ReconcileHeroes"/> and
    /// the phase-choreography helpers below read <see cref="GodotClient.SimAdapter.CurrentState"/>
    /// off this. Null only before <see cref="Build"/> has run.</summary>
    public GodotClient.SimAdapter? Adapter { get; private set; }

    /// <summary>T7: live hero actors keyed by <c>HeroId.Value</c> — alive heroes only (mirrors
    /// <c>TownScene.Sprites</c>).</summary>
    private readonly Dictionary<int, HeroActor3D> _heroActors = new();

    /// <summary>LW1 file-exit stagger, ported from <c>TownScene</c> — how far apart (in seconds)
    /// successive party members peel off the rally point toward the gate.</summary>
    private const float FileExitStaggerSeconds = 0.35f;

    /// <summary>T7: live hero-actor count (test/inspection surface).</summary>
    public int HeroActorCount() => _heroActors.Count;

    /// <summary>T7: the lowest-HeroId live actor (test/inspection surface) — deterministic even
    /// though <see cref="Dictionary{TKey,TValue}"/> enumeration order is an implementation detail.
    /// Throws if <see cref="ReconcileHeroes"/> has never produced any actor.</summary>
    public HeroActor3D FirstHeroActor() => _heroActors.Values.OrderBy(a => a.HeroIdValue).First();

    /// <summary>
    /// T7: reconcile <see cref="Heroes"/> against <c>Adapter.CurrentState.Heroes</c> — adds a
    /// <see cref="HeroActor3D"/> for every ALIVE hero that doesn't already have one, and removes
    /// (frees) the actor for any hero that's now dead or gone entirely (mirrors
    /// <c>GodotClient.Town.TownScene.ReconcileSprites</c>'s intent). Also rebuilds the memorial
    /// plot from the current <c>DramaState.Memorials</c>. Called once at the end of <see
    /// cref="Build"/> for this standalone scaffold; the T8 <c>Refresh()</c> surface calls it again
    /// on every tick.
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

            var actor = new HeroActor3D();
            actor.Configure(hero.Id.Value, hero.Name, hero.Id.Value % 12, HomeFor(hero.Id.Value), hero.ClassId);
            actor.Picked += id => HeroClicked?.Invoke(id);
            Heroes.AddChild(actor);
            _heroActors[hero.Id.Value] = actor;
        }

        // Permadeath (R7) / roster-absent: free the actor for anyone no longer alive-and-present.
        foreach (var heroId in _heroActors.Keys
                     .Where(id => !state.Heroes.TryGetValue(id, out var hero) || !hero.Alive)
                     .ToList())
        {
            var actor = _heroActors[heroId];
            _heroActors.Remove(heroId);
            Heroes.RemoveChild(actor);
            actor.QueueFree();
        }

        RebuildMemorialPlot(state);
    }

    /// <summary>Deterministic home spot per hero id — spread across an open band of the ground
    /// plane clear of every building footprint (world-scale doc's 2D "wander band" analog).
    /// </summary>
    private static Vector3 HomeFor(int heroValue) => new(-18f + heroValue * 7 % 36, 0f, 2f + heroValue * 5 % 10);

    /// <summary>T7: one stone per dead hero (mirrors <c>TownScene.RebuildMemorials</c>) — clears
    /// and rebuilds <see cref="MemorialPlot"/> every reconcile so it never drifts from
    /// <c>DramaState.Memorials</c>.</summary>
    private void RebuildMemorialPlot(GameState state)
    {
        foreach (var child in MemorialPlot.GetChildren().ToList())
        {
            MemorialPlot.RemoveChild(child);
            child.QueueFree();
        }

        var index = 0;
        foreach (var memorial in state.Drama.Memorials)
        {
            MemorialPlot.AddChild(BuildMemorialStone(memorial, index));
            index++;
        }

        MemorialStoneCount = index;
    }

    /// <summary>Live memorial-stone count (test/inspection surface).</summary>
    public int MemorialStoneCount { get; private set; }

    private static Node3D BuildMemorialStone(Memorial memorial, int index)
    {
        var stone = new Node3D { Name = $"Memorial_{memorial.Hero.Value}", Position = new Vector3(index * 1.6f, 0f, 0f) };
        stone.AddChild(new MeshInstance3D
        {
            Name = "Stone",
            Mesh = new BoxMesh { Size = new Vector3(0.6f, 1.0f, 0.3f) },
            Position = new Vector3(0, 0.5f, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.55f, 0.58f) },
        });
        stone.AddChild(new Label3D
        {
            Name = "Label3D",
            Text = memorial.HeroName,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Position = new Vector3(0, 1.3f, 0),
            FontSize = 28,
            OutlineSize = 6,
        });
        return stone;
    }

    /// <summary>
    /// T7 stub: phase-transition choreography for hero actors, called (like <c>TownScene</c>'s own
    /// <c>OnPhaseCompleted</c>) after every tick. Morning done → every non-Away, non-WalkingIn
    /// actor rallies and files out the gate. Expedition/ExpeditionDeep done → survivors of any
    /// FINALIZED run (<c>PendingExpeditions</c>) who are still Away walk home (the <c>Away</c>
    /// guard makes calling this at both arms idempotent, same as the 2D original). Evening done →
    /// every remaining actor whose hero is confirmed alive snaps home for the new day (see
    /// <see cref="SnapRemainingHeroesHome"/> for why that's an alive-check rather than a blanket
    /// snap). Camp/unknown → no-op (never snap an away/dead hero home mid-day before the Evening
    /// reveal).
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
    /// Evening choreography: every actor not already Wandering snaps home for the new day —
    /// EXCEPT one whose hero is dead or gone from the roster right now. Review finding: a hero
    /// that died mid-expedition may not have been reconciled away yet (this task's
    /// <see cref="ReconcileHeroes"/> only runs at <see cref="Build"/>; T8 wires the per-tick
    /// <c>Refresh()</c> call that keeps it current), so its actor could still be sitting in
    /// <see cref="_heroActors"/> in a non-Wandering state (e.g. <c>Away</c>) when Evening fires —
    /// a blanket snap would resurrect it (visible, <c>Wandering</c>) until the next reconcile
    /// caught up. Fixing this with a per-actor alive-check (rather than calling
    /// <see cref="ReconcileHeroes"/> first, here, to remove the dead actor before this loop runs)
    /// keeps the ordering dependency gone entirely instead of just narrowing the window: it's
    /// correct regardless of whether/when reconcile has last run, and it avoids forcing a full
    /// heroes diff + memorial-plot rebuild on every Evening completion just to cover this one
    /// case. Dead/absent heroes are left exactly as they are; <see cref="ReconcileHeroes"/>
    /// (called elsewhere) is what actually frees their actor.
    /// </summary>
    private void SnapRemainingHeroesHome()
    {
        var heroes = Adapter!.CurrentState.Heroes;
        foreach (var actor in _heroActors.Values.Where(a =>
                     a.State != HeroActor3D.ActorState.Wandering &&
                     heroes.TryGetValue(a.HeroIdValue, out var hero) && hero.Alive))
        {
            actor.SnapHome();
        }
    }

    private void DepartWanderingHeroes()
    {
        var departing = _heroActors.Values
            .Where(a => a.State != HeroActor3D.ActorState.Away && a.State != HeroActor3D.ActorState.WalkingIn)
            .OrderBy(a => a.HeroIdValue)
            .ToList();
        for (var i = 0; i < departing.Count; i++)
        {
            departing[i].BeginDeparture(RallySpotFor(i, departing.Count), i * FileExitStaggerSeconds);
        }
    }

    /// <summary>Party-file rally slot near the gate, spread along X so the group reads as a
    /// cluster rather than a stack (LW1, ported to 3D).</summary>
    private static Vector3 RallySpotFor(int index, int count) =>
        new((index - (count - 1) / 2f) * 0.8f, 0f, -15f);

    private void ReturnSurvivors()
    {
        var survivors = Adapter!.CurrentState.PendingExpeditions
            .SelectMany(expedition => expedition.Survivors)
            .Select(id => id.Value)
            .ToHashSet();
        foreach (var actor in _heroActors.Values
                     .Where(a => a.State == HeroActor3D.ActorState.Away && survivors.Contains(a.HeroIdValue)))
        {
            actor.BeginReturn();
        }
    }

    public override void _Process(double delta)
    {
        foreach (var actor in _heroActors.Values)
        {
            actor.Advance(delta);
        }
    }

    /// <summary>
    /// Builds the whole standalone 3D scaffold: viewport, world, ground, light, camera. Safe to
    /// call once per instance (mirrors the <c>MainUi.Build</c>/panel idiom elsewhere in this
    /// codebase). <paramref name="adapter"/> is accepted now (later tasks reconcile heroes/
    /// buildings from it) but unused by the scaffold itself.
    /// </summary>
    public void Build(GodotClient.SimAdapter adapter)
    {
        Adapter = adapter;
        TownInput.RegisterActions();

        Stretch = true;

        Viewport = new SubViewport
        {
            Name = "Viewport",
            PhysicsObjectPicking = true,
            HandleInputLocally = true,
            OwnWorld3D = true,
        };
        AddChild(Viewport);

        World = new Node3D { Name = "World" };
        Viewport.AddChild(World);

        World.AddChild(BuildLight());
        World.AddChild(BuildEnvironment());

        Buildings = new Node3D { Name = "Buildings" };
        var buildings = BuildBuildings();
        foreach (var building in buildings)
        {
            Buildings.AddChild(building);
        }

        // T6: NavRegion must be built AFTER the ground + buildings it reparents exist, and its
        // bake must run AFTER it (and them) are actually in the live tree — see the NavRegion
        // property doc for why they become its children rather than World siblings.
        NavRegion = new NavigationRegion3D
        {
            Name = "NavRegion",
            NavigationMesh = new NavigationMesh
            {
                GeometryParsedGeometryType = NavigationMesh.ParsedGeometryType.StaticColliders,
                GeometryCollisionMask = 1 | 2,
            },
        };
        World.AddChild(NavRegion);
        NavRegion.AddChild(BuildGround());
        NavRegion.AddChild(Buildings);
        NavRegion.BakeNavigationMesh(onThread: false); // synchronous — deterministic headless

        Heroes = new Node3D { Name = "Heroes" };
        World.AddChild(Heroes);

        Camera = new CameraRig { Name = "CameraRig" };
        World.AddChild(Camera);

        Player = BuildPlayer();
        World.AddChild(Player);
        Player.Cam = Camera.GetNode<Camera3D>("Camera3D");
        Camera.Target = Player;
        Player.ArrivedAtBuilding += key => BuildingClicked?.Invoke(key);

        MemorialPlot = BuildMemorialPlot();
        World.AddChild(MemorialPlot);

        WorldInputNode = new WorldInput3D { Name = "WorldInput3D" };
        WorldInputNode.Configure(Player, buildings, Player.Cam);
        World.AddChild(WorldInputNode);
        WorldInputNode.Interacted += key => BuildingClicked?.Invoke(key);

        // T7: populate Heroes/MemorialPlot from the adapter's initial state. T8's Refresh()
        // surface calls this again on every tick; this standalone scaffold only needs it once.
        ReconcileHeroes();
    }

    /// <summary>
    /// T5 world-feature layout: the four staged-interior venues (forge/market/tavern/minegate —
    /// the exact click-keys <c>MainUi.OnTownBuildingClicked</c>'s switch already handles) plus the
    /// noticeboard (payload "Bounties" — the existing bounty-panel id used elsewhere in <c>MainUi</c>
    /// (<c>Drawer.Register("Bounties", ...)</c>, the interior "board" hotspot's own "Bounties"
    /// payload); the 2D noticeboard zone never routed through <c>BuildingClicked</c> at all — T8
    /// adds the MainUi-side case). Positions are spread around the box-floor ground (60×60, so
    /// ±30 in each horizontal axis) well clear of each other's interact zones.
    /// </summary>
    private static readonly (string Key, string Label, string ClickKey, Vector3 Position)[] BuildingLayout =
    {
        ("forge", "Forge", "Forge", new Vector3(-9f, 0f, -7f)),
        ("market", "Shop", "Shop", new Vector3(9f, 0f, -7f)),
        ("tavern", "Tavern", "Tavern", new Vector3(-9f, 0f, 8f)),
        ("minegate", "Gate", "Gate", new Vector3(0f, 0f, -20f)),
        ("noticeboard", "Bounties", "Bounties", new Vector3(11f, 0f, 9f)),
    };

    private static List<Building3D> BuildBuildings()
    {
        var buildings = new List<Building3D>();
        foreach (var (key, label, clickKey, position) in BuildingLayout)
        {
            var building = new Building3D();
            building.Configure(key, label, clickKey, position, TownAssets.BuildingScene(key));
            buildings.Add(building);
        }

        return buildings;
    }

    /// <summary>T5: the memorial corner plot's container — a non-interactive display-only
    /// landmark (no collider, no interact zone; see <see cref="MemorialPlot"/>'s own doc). One
    /// stone per dead hero, rebuilt on reconcile, lands in T7.</summary>
    private static Node3D BuildMemorialPlot() => new() { Name = "MemorialPlot", Position = new Vector3(-14f, 0f, 14f) };

    /// <summary>Look up one of the six placed buildings by its <see cref="Building3D.Key"/> (e.g.
    /// "forge") — throws if <see cref="Build"/> hasn't run or the key is unknown, since every
    /// caller (tests, T6/T8 routing) expects the full layout to already exist.</summary>
    public Building3D FindBuilding(string key) =>
        Buildings.GetChildren().OfType<Building3D>().FirstOrDefault(b => b.Key == key)
        ?? throw new InvalidOperationException($"No building named '{key}' in Town3D.Buildings.");

    /// <summary>
    /// Builds the standalone player body: a layer-4 <see cref="CharacterBody3D"/> (mask 1|2 — the
    /// ground and building footprints, T5) with a feet-anchored capsule collider, a visual
    /// child sourced from <see cref="TownAssets.HeroScene"/> (variant 0) or a primitive capsule
    /// fallback when the Kenney asset is missing, and (T6) a <see cref="NavigationAgent3D"/> that
    /// drives click-to-move.
    /// </summary>
    private static PlayerController BuildPlayer()
    {
        var player = new PlayerController { Name = "Player", CollisionLayer = 4, CollisionMask = 1 | 2 };

        var shape = new CollisionShape3D
        {
            Name = "CollisionShape3D",
            Shape = new CapsuleShape3D { Radius = 0.35f, Height = 1.6f },
            Position = new Vector3(0, 0.8f, 0),
        };
        player.AddChild(shape);

        var mesh = SpawnCharacterMesh(0);
        player.AddChild(mesh);
        player.Mesh = mesh;

        var agent = new NavigationAgent3D
        {
            Name = "NavigationAgent3D",
            AvoidanceEnabled = false,
            PathDesiredDistance = 0.5f,
            TargetDesiredDistance = 1.0f,
        };
        player.AddChild(agent);
        player.Agent = agent;

        return player;
    }

    /// <summary>Instantiates the Kenney hero GLB for <paramref name="variant"/> when the asset
    /// exists, else falls back to <see cref="PrimitiveCapsule"/> so the standalone scaffold never
    /// depends on art having landed.</summary>
    private static Node3D SpawnCharacterMesh(int variant)
    {
        var scene = TownAssets.HeroScene(variant);
        if (scene == null)
        {
            return PrimitiveCapsule(new Color(0.85f, 0.78f, 0.55f));
        }

        var mesh = scene.Instantiate<Node3D>();
        mesh.Name = "Mesh";
        return mesh;
    }

    /// <summary>Primitive placeholder body — a capsule sitting on the ground plane (origin at
    /// feet, matching the collider) tinted <paramref name="color"/>.</summary>
    private static Node3D PrimitiveCapsule(Color color)
    {
        var mesh = new MeshInstance3D
        {
            Name = "Mesh",
            Mesh = new CapsuleMesh { Radius = 0.35f, Height = 1.6f },
            Position = new Vector3(0, 0.8f, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = color },
        };
        return mesh;
    }

    // BuildingClicked/HeroClicked are wired to real in-world sources starting in T5/T7
    // (WorldInput3D.Interacted / HeroActor3D.RaisePick forwarding straight into these events);
    // this scaffold only declares the vocabulary so downstream tasks compile against it.

    /// <summary>
    /// Box-floor ground: a 60x60 <see cref="PlaneMesh"/> for the visible surface, paired with a
    /// layer-1 <see cref="StaticBody3D"/> carrying a <see cref="BoxShape3D"/> (NOT a
    /// <see cref="WorldBoundaryShape3D"/> — an infinite plane shape can't be baked into a
    /// navmesh later, T6). The box is centered at y=-0.5 with height 1 so its TOP face sits
    /// exactly at y=0, flush with the visible plane mesh.
    /// </summary>
    private static Node3D BuildGround()
    {
        var ground = new Node3D { Name = "Ground" };

        var mesh = new MeshInstance3D
        {
            Name = "GroundMesh",
            Mesh = new PlaneMesh { Size = new Vector2(60, 60) },
        };
        ground.AddChild(mesh);

        var body = new StaticBody3D { Name = "GroundBody", CollisionLayer = 1, CollisionMask = 0 };
        var shape = new CollisionShape3D
        {
            Name = "GroundShape",
            Shape = new BoxShape3D { Size = new Vector3(60, 1, 60) },
            Position = new Vector3(0, -0.5f, 0),
        };
        body.AddChild(shape);
        ground.AddChild(body);

        return ground;
    }

    private static DirectionalLight3D BuildLight() => new()
    {
        Name = "SunLight",
        RotationDegrees = new Vector3(-55, -30, 0),
        LightEnergy = 1.1f,
    };

    private static WorldEnvironment BuildEnvironment()
    {
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = new ProceduralSkyMaterial() },
        };
        return new WorldEnvironment { Name = "WorldEnvironment", Environment = env };
    }
}
