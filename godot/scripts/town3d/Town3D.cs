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
            Text = $"{memorial.HeroName} — Day {memorial.Day}",
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Position = new Vector3(0, 1.3f, 0),
            FontSize = 28,
            OutlineSize = 6,
        });
        stone.AddChild(new Label3D
        {
            Name = "EpitaphLabel3D",
            Text = $"died wearing {memorial.GearNamed}",
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Position = new Vector3(0, 1.0f, 0),
            FontSize = 18,
            OutlineSize = 5,
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
        new((index - (count - 1) / 2f) * 0.8f, 0f, -11f);

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

        // G1 forge-station VFX: decay the on-beat flash pulse ForgeSparkBurst armed — accumulated
        // delta, no engine Tween (the established idiom elsewhere in this codebase, e.g. MainUi's
        // gold-chip pop). No-op whenever no flash is in flight (-1 sentinel) or the station was
        // never wired (headless-safe: nothing here depends on a frame actually rendering).
        if (_forgeFlashElapsed >= 0 && _forgeFlashLight is not null)
        {
            _forgeFlashElapsed += delta;
            var t = Mathf.Clamp((float)(_forgeFlashElapsed / ForgeFlashSeconds), 0f, 1f);
            _forgeFlashLight.LightEnergy = Mathf.Lerp(ForgeFlashPeakEnergy, 0f, t);
            if (t >= 1f)
            {
                _forgeFlashElapsed = -1;
            }
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
        World.AddChild(BuildFillLight());
        World.AddChild(BuildEnvironment());
        World.AddChild(BuildSun());

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

        // Decoration only — never parented under NavRegion (goal 4: no collider means nothing
        // here can distort the bake above, matching the plan's "props are decoration only" rule).
        World.AddChild(BuildProps());
        World.AddChild(BuildBoundary()); // visual round: perimeter treeline (decoration, no collider)

        Heroes = new Node3D { Name = "Heroes" };
        World.AddChild(Heroes);

        // Visual round: pull the follow-camera back a touch (28 vs the class default 22) so the
        // town reads as a sizable village in a forest clearing rather than a tight cluster — reveals
        // more of the square + the perimeter treeline. Instance-level only; the class default (and
        // the CameraRig tests that pin 22) are untouched, and PushIn station dollies still override.
        Camera = new CameraRig { Name = "CameraRig", Distance = 28f };
        World.AddChild(Camera);

        Player = BuildPlayer();
        World.AddChild(Player);
        Player.Cam = Camera.GetNode<Camera3D>("Camera3D");
        Camera.Target = Player;
        Player.ArrivedAtBuilding += key => BuildingClicked?.Invoke(key);

        // U7 (opener fantasy line + frame the mine gate on day 1): the mine gate — the whole
        // point of the game — sits far ahead of the player's day-1 spawn (z ≈ -16 vs the
        // player's z = 0), close to the edge of (or entirely outside) the rig's forward view
        // when it simply snaps onto the player alone. Framing the rig's OPENING position at the
        // midpoint between the player and the gate — instead of directly on the player — pulls
        // the gate well inside the camera's initial frustum while the player, much nearer to the
        // rig than the gate is, stays comfortably in view too. This only nudges the first frame:
        // CameraRig's own per-frame follow-ease (_Process) takes back over immediately after,
        // gliding the rig from this opening establishing shot onto the player as normal.
        var gate = FindBuilding("minegate");
        Camera.SnapTo(new Vector3(
            Player.GlobalPosition.X,
            Player.GlobalPosition.Y,
            (Player.GlobalPosition.Z + gate.GlobalPosition.Z) / 2f));

        MemorialPlot = BuildMemorialPlot();
        World.AddChild(MemorialPlot);

        WorldInputNode = new WorldInput3D { Name = "WorldInput3D" };
        WorldInputNode.Configure(Player, buildings, Player.Cam);
        World.AddChild(WorldInputNode);
        WorldInputNode.Interacted += key => BuildingClicked?.Invoke(key);

        // T7: populate Heroes/MemorialPlot from the adapter's initial state. T8's Refresh()
        // surface calls this again on every tick; this standalone scaffold only needs it once.
        ReconcileHeroes();

        // G1: locate the forge station's VFX nodes now that every Building3D (including the
        // station cluster above) is configured — see WireForgeStationVfx's own doc for why this
        // must run after Configure, not inside BuildAnvilFurnaceCluster itself.
        WireForgeStationVfx();
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
        ("forge", "Forge", "Forge", new Vector3(-8f, 0f, -6f)),
        ("market", "Shop", "Shop", new Vector3(8f, 0f, -6f)),
        ("tavern", "Tavern", "Tavern", new Vector3(-8f, 0f, 7f)),
        ("minegate", "Gate", "Gate", new Vector3(0f, 0f, -16f)),
        ("noticeboard", "Bounties", "Bounties", new Vector3(9f, 0f, 8f)),
    };

    /// <summary>
    /// PA8 (spec DB4/PKD8): the two active-professions stations — the forge anvil/furnace
    /// cluster and the shop counter — placed a clear gap south of their respective venue
    /// buildings (5+ units, clear of every existing footprint/interact zone and the gate
    /// departure lane at x≈0). Distinct from <see cref="BuildingLayout"/>'s venue doorways
    /// (which stage the full 2.5D interior): arriving here opens the focus overlay/panel
    /// DIRECTLY with a <see cref="CameraRig.PushIn"/> dolly (<c>MainUi.OnTownBuildingClicked</c>
    /// routes "ForgeStation"/"CounterStation" before the venue-interior switch) — the outdoor
    /// station is reused precedent for Phase C's fuller diegetic-3D swap. Built as plain <see
    /// cref="Building3D"/> instances (proximity/highlight/interact/click-picking are already
    /// fully generic over the shared <see cref="Buildings"/> list — no new plumbing needed in
    /// <see cref="PlayerController"/>/<see cref="WorldInput3D"/>), so they reuse
    /// <see cref="PlayerController.ArrivedAtBuilding"/> and <see cref="WorldInput3D.Interacted"/>
    /// unchanged.
    /// </summary>
    private static readonly (string Key, string Label, string ClickKey, Vector3 Position)[] StationLayout =
    {
        ("forge-station", "Anvil", "ForgeStation", new Vector3(-8f, 0f, -12f)),
        ("counter-station", "Counter", "CounterStation", new Vector3(8f, 0f, -12f)),
    };

    private static List<Building3D> BuildBuildings()
    {
        var buildings = new List<Building3D>();
        foreach (var (key, label, clickKey, position) in BuildingLayout)
        {
            var building = new Building3D();
            building.Configure(key, label, clickKey, position, TownAssets.BuildBuilding(key));
            buildings.Add(building);
        }

        foreach (var (key, label, clickKey, position) in StationLayout)
        {
            var station = new Building3D();
            station.Configure(key, label, clickKey, position, BuildStationMesh(key));
            buildings.Add(station);
        }

        return buildings;
    }

    /// <summary>
    /// PA8 placeholder station props (asset-manifest logged: "Forge station (3D world prop)" /
    /// "Shop counter station" — both `primitive`, no Kenney asset earmarked for either). Built the
    /// SAME way <see cref="BuildGround"/>/<see cref="BuildPrimitiveWedge"/> build their fallback
    /// geometry — the tint lives on the <see cref="PrimitiveMesh"/> resource's own <see
    /// cref="PrimitiveMesh.Material"/>, NEVER <see cref="GeometryInstance3D.MaterialOverride"/>
    /// (the wedge-fallback bug this deliberately avoids: <see cref="MaterialOverride"/> renders
    /// ahead of the per-surface override <see cref="Building3D.SetHighlighted"/> installs, so
    /// setting it here would silently swallow the proximity highlight glow).
    /// </summary>
    private static Node3D BuildStationMesh(string key) => key switch
    {
        "forge-station" => BuildAnvilFurnaceCluster(),
        "counter-station" => BuildCounterCluster(),
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "no station mesh for this key"),
    };

    private static Node3D BuildAnvilFurnaceCluster()
    {
        var cluster = new Node3D { Name = "AnvilFurnaceCluster" };

        cluster.AddChild(new MeshInstance3D
        {
            Name = "Furnace",
            Mesh = new BoxMesh
            {
                Size = new Vector3(1.1f, 1.4f, 1.1f),
                Material = new StandardMaterial3D
                {
                    AlbedoColor = new Color(0.32f, 0.28f, 0.27f),
                    EmissionEnabled = true,
                    Emission = new Color(0.95f, 0.4f, 0.05f),
                    EmissionEnergyMultiplier = ForgeGlowBaseline,
                },
            },
            Position = new Vector3(-0.7f, 0.7f, 0f),
        });

        // Gen-first (mirrors BuildingKit): the AI-gen anvil GLB becomes the station's anvil, rescaled
        // to ~0.6u from its own bounds and feet-pivoted at the same spot; the primitive box is the
        // graceful fallback when the asset is absent. The ForgeSparks/flash VFX below sit above this
        // anchor either way.
        var genAnvil = TownAssets.InstantiateGen("anvil.glb");
        if (genAnvil != null)
        {
            var anvilHeight = MeshHeight(genAnvil, 1f);
            var anvilScale = anvilHeight > 0.001f ? 0.6f / anvilHeight : 1f;
            genAnvil.Name = "Anvil";
            genAnvil.Scale = new Vector3(anvilScale, anvilScale, anvilScale);
            genAnvil.Position = new Vector3(0.7f, 0f, 0f);
            cluster.AddChild(genAnvil);
        }
        else
        {
            cluster.AddChild(new MeshInstance3D
            {
                Name = "Anvil",
                Mesh = new BoxMesh
                {
                    Size = new Vector3(0.9f, 0.6f, 0.5f),
                    Material = new StandardMaterial3D { AlbedoColor = new Color(0.2f, 0.2f, 0.22f) },
                },
                Position = new Vector3(0.7f, 0.3f, 0f),
            });
        }

        // G1 (game-feel plan §"World VFX keyed to beat state"): the station's own VFX props —
        // headless-safe (CpuParticles3D/OmniLight3D are plain scene nodes; nothing here depends on
        // a frame actually rendering, and none of it is read back by anything else, so a headless
        // engine test that never pumps a rendering SubViewport is unaffected). Driven by
        // ForgeGlow/ForgeSparkBurst/ForgeSteamPlume below, wired to the anvil after Configure runs
        // (see WireForgeStationVfx).
        cluster.AddChild(new CpuParticles3D
        {
            Name = "ForgeSparks",
            Emitting = false,
            OneShot = true,
            Amount = 14,
            Lifetime = 0.4,
            Explosiveness = 1f,
            Position = new Vector3(0.7f, 0.55f, 0f),
            Direction = new Vector3(0, 1, 0),
            Spread = 60f,
            Gravity = new Vector3(0, -9.8f, 0),
            InitialVelocityMin = 1.5f,
            InitialVelocityMax = 3.5f,
            ScaleAmountMin = 0.04f,
            ScaleAmountMax = 0.08f,
            Color = new Color(1f, 0.75f, 0.25f),
        });

        cluster.AddChild(new CpuParticles3D
        {
            Name = "QuenchSteam",
            Emitting = false,
            OneShot = true,
            Amount = 10,
            Lifetime = 0.9,
            Explosiveness = 0.6f,
            Position = new Vector3(0.7f, 0.35f, 0f),
            Direction = new Vector3(0, 1, 0),
            Spread = 25f,
            Gravity = Vector3.Zero,
            InitialVelocityMin = 0.4f,
            InitialVelocityMax = 0.9f,
            ScaleAmountMin = 0.3f,
            ScaleAmountMax = 0.6f,
            Color = new Color(0.9f, 0.9f, 0.95f, 0.5f),
        });

        cluster.AddChild(new OmniLight3D
        {
            Name = "ForgeFlash",
            LightColor = new Color(1f, 0.7f, 0.3f),
            LightEnergy = 0f, // off by default — a brief pulse driven by ForgeSparkBurst/_Process
            OmniRange = 4f,
            Position = new Vector3(0.7f, 0.6f, 0f),
        });

        return cluster;
    }

    // ── G1 forge-station world VFX (game-feel plan §"World VFX keyed to beat state") ──────────
    // Presentation-only: every method below reads an already-computed permille/bool cue from the
    // minigame and writes ONLY render-facing node properties (emission energy, particle Emitting,
    // light energy). No game logic lives here, nothing here is read back by the sim or by
    // ForgeMinigame, and every accessor degrades to a silent no-op when the station hasn't been
    // wired (e.g. a headless test that builds a bare Town3D without ever opening the forge).

    private const float ForgeGlowBaseline = 0.8f;
    private const float ForgeGlowPeak = 2.6f;
    private const double ForgeFlashSeconds = 0.15;
    private const float ForgeFlashPeakEnergy = 3.5f;

    private StandardMaterial3D? _forgeFurnaceMaterial;
    private CpuParticles3D? _forgeSparks;
    private CpuParticles3D? _forgeSteam;
    private OmniLight3D? _forgeFlashLight;
    private double _forgeFlashElapsed = -1;

    /// <summary>Locates the forge station's VFX nodes once <see cref="Build"/> has configured every
    /// <see cref="Building3D"/> — called at the tail of <see cref="Build"/>. Reads <see
    /// cref="MeshInstance3D.GetActiveMaterial"/> (not the mesh resource's own <c>Material</c>)
    /// because <see cref="Building3D.Configure"/> already duplicated every surface material into a
    /// per-instance override for its own proximity-highlight glow (<see
    /// cref="Building3D.SetHighlighted"/>) — this is the SAME material actually on screen, so
    /// brightening it here and the highlight toggle never fight over two different resources.</summary>
    private void WireForgeStationVfx()
    {
        var station = FindBuilding("forge-station");
        var furnaceMesh = station.Mesh.GetNode<MeshInstance3D>("Furnace");
        _forgeFurnaceMaterial = furnaceMesh.GetActiveMaterial(0) as StandardMaterial3D;
        _forgeSparks = station.Mesh.GetNodeOrNull<CpuParticles3D>("ForgeSparks");
        _forgeSteam = station.Mesh.GetNodeOrNull<CpuParticles3D>("QuenchSteam");
        _forgeFlashLight = station.Mesh.GetNodeOrNull<OmniLight3D>("ForgeFlash");
    }

    /// <summary>Brightens the furnace glow in step with the live Smelt heat gauge (0-1000 permille)
    /// — call every frame while the minigame's Smelt beat is active. No-op if the station was never
    /// wired.</summary>
    public void ForgeGlow(int heatPermille)
    {
        if (_forgeFurnaceMaterial is null)
        {
            return;
        }

        var t = Mathf.Clamp(heatPermille / 1000f, 0f, 1f);
        _forgeFurnaceMaterial.EmissionEnergyMultiplier = Mathf.Lerp(ForgeGlowBaseline, ForgeGlowPeak, t);
    }

    /// <summary>Resets the furnace glow to its resting baseline — called the instant the Smelt
    /// stage ends (or the minigame cancels/closes) so a half-finished smelt never leaves the
    /// furnace stuck bright.</summary>
    public void ForgeGlowReset()
    {
        if (_forgeFurnaceMaterial is not null)
        {
            _forgeFurnaceMaterial.EmissionEnergyMultiplier = ForgeGlowBaseline;
        }
    }

    /// <summary>A one-shot spark burst + brief flash at the anvil — the on-beat forge-hit cue.</summary>
    public void ForgeSparkBurst()
    {
        if (_forgeSparks is not null)
        {
            _forgeSparks.Restart();
            _forgeSparks.Emitting = true;
        }

        _forgeFlashElapsed = 0; // decayed in _Process
    }

    /// <summary>A one-shot steam plume at the anvil — the quench-lock cue.</summary>
    public void ForgeSteamPlume()
    {
        if (_forgeSteam is null)
        {
            return;
        }

        _forgeSteam.Restart();
        _forgeSteam.Emitting = true;
    }

    private static Node3D BuildCounterCluster()
    {
        var cluster = new Node3D { Name = "CounterCluster" };

        cluster.AddChild(new MeshInstance3D
        {
            Name = "CounterTop",
            Mesh = new BoxMesh
            {
                Size = new Vector3(2.0f, 0.9f, 0.7f),
                Material = new StandardMaterial3D { AlbedoColor = new Color(0.5f, 0.36f, 0.22f) },
            },
            Position = new Vector3(0f, 0.45f, 0f),
        });

        cluster.AddChild(new MeshInstance3D
        {
            Name = "Shelf",
            Mesh = new BoxMesh
            {
                Size = new Vector3(1.6f, 0.15f, 0.4f),
                Material = new StandardMaterial3D { AlbedoColor = new Color(0.42f, 0.3f, 0.18f) },
            },
            Position = new Vector3(0f, 1.0f, -0.3f),
        });

        return cluster;
    }

    /// <summary>T5: the memorial corner plot's container — a non-interactive display-only
    /// landmark (no collider, no interact zone; see <see cref="MemorialPlot"/>'s own doc). One
    /// stone per dead hero, rebuilt on reconcile, lands in T7.</summary>
    private static Node3D BuildMemorialPlot() => new() { Name = "MemorialPlot", Position = new Vector3(-11f, 0f, 11f) };

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

    /// <summary>Instantiates the Kenney hero GLB for <paramref name="variant"/> (colormap-fixed,
    /// pre-scaled — see <see cref="TownAssets.InstantiateHero"/>) when the asset exists, else falls
    /// back to <see cref="PrimitiveCapsule"/> so the standalone scaffold never depends on art
    /// having landed.</summary>
    private static Node3D SpawnCharacterMesh(int variant)
    {
        var mesh = TownAssets.InstantiateHero(variant);
        if (mesh == null)
        {
            return PrimitiveCapsule(new Color(0.85f, 0.78f, 0.55f));
        }

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
    /// exactly at y=0, flush with the visible plane mesh. Tinted an earthy grass green (goal 4:
    /// "not bare gray") via a flat <see cref="StandardMaterial3D"/> — no texture needed for a flat
    /// field of color.
    /// </summary>
    /// <summary>A ring of stylized pines around the village perimeter (visual round): encloses the
    /// open meadow so the town reads as a real settlement in a clearing rather than a few models on
    /// a bare tile, and the distance haze (<see cref="BuildEnvironment"/>) fades the far trees to
    /// sell scale. Pure decoration — no collider, never under the NavRegion, so it can't distort the
    /// hero navigation bake. Placement is deterministic (index-derived angle + hash jitter, no RNG).</summary>
    private static Node3D BuildBoundary()
    {
        var ring = new Node3D { Name = "Boundary" };
        const int count = 40;
        const float radius = 24f;
        for (var i = 0; i < count; i++)
        {
            var angle = Mathf.Tau * i / count;
            var jitter = ((i * 2654435761u) % 1000u) / 1000f; // deterministic 0..1
            var r = radius + (jitter - 0.5f) * 7f;
            var pos = new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r);
            ring.AddChild(BuildPine(pos, 1.4f + jitter * 0.9f, i));
        }

        return ring;
    }

    /// <summary>A single low-poly stylized pine (trunk + three stacked foliage cones), matching the
    /// flat-shaded town look — the generated foliage texture can dress these later.</summary>
    private static Node3D BuildPine(Vector3 pos, float scale, int i)
    {
        var tree = new Node3D { Name = $"Pine{i}", Position = pos, Scale = Vector3.One * scale };

        tree.AddChild(new MeshInstance3D
        {
            Name = "Trunk",
            Position = new Vector3(0f, 0.7f, 0f),
            Mesh = new CylinderMesh
            {
                TopRadius = 0.18f,
                BottomRadius = 0.26f,
                Height = 1.4f,
                Material = new StandardMaterial3D { AlbedoColor = new Color(0.34f, 0.24f, 0.16f), Roughness = 1f },
            },
        });

        var foliage = new StandardMaterial3D { AlbedoColor = new Color(0.18f, 0.40f, 0.23f), Roughness = 1f };
        for (var t = 0; t < 3; t++)
        {
            tree.AddChild(new MeshInstance3D
            {
                Name = $"Foliage{t}",
                Position = new Vector3(0f, 1.7f + t * 1.05f, 0f),
                Mesh = new CylinderMesh
                {
                    TopRadius = 0.02f,
                    BottomRadius = 1.35f - t * 0.34f,
                    Height = 1.6f,
                    Material = foliage,
                },
            });
        }

        return tree;
    }

    private static Node3D BuildGround()
    {
        var ground = new Node3D { Name = "Ground" };

        // Visual round: a larger meadow (90×90 vs the old 60) so the village sits in open country
        // rather than on a cramped tile, and a warmer, slightly brighter grass tone. The generated
        // ground texture (grass/dirt/cobble) drops onto this same material's AlbedoTexture later.
        var mesh = new MeshInstance3D
        {
            Name = "GroundMesh",
            Mesh = new PlaneMesh
            {
                Size = new Vector2(90, 90),
                Material = new StandardMaterial3D
                {
                    AlbedoColor = new Color(0.42f, 0.52f, 0.28f),
                    Roughness = 0.95f,
                },
            },
        };
        ground.AddChild(mesh);

        var body = new StaticBody3D { Name = "GroundBody", CollisionLayer = 1, CollisionMask = 0 };
        var shape = new CollisionShape3D
        {
            Name = "GroundShape",
            Shape = new BoxShape3D { Size = new Vector3(90, 1, 90) },
            Position = new Vector3(0, -0.5f, 0),
        };
        body.AddChild(shape);
        ground.AddChild(body);

        return ground;
    }

    /// <summary>
    /// Goal 4 scene dressing: a plaza fountain, a handful of trees, a fenced paddock corner, a
    /// cart, and scattered rocks — every position is a fixed literal (no RNG, KTD2/KTD4) chosen to
    /// sit clear of every <c>Building3D</c>'s 4.4-unit interact zone and the gate departure lane
    /// (x≈0, z from -20 to -15). Pure decoration: no colliders, so nothing here can block the
    /// player, a hero, or the bake this container is deliberately kept OUT of (see <see
    /// cref="Build"/>'s own comment on why it's a <see cref="World"/> sibling of
    /// <see cref="NavRegion"/> rather than a child).
    /// </summary>
    private static Node3D BuildProps()
    {
        var props = new Node3D { Name = "Props" };

        AddProp(props, TownAssets.FantasyTownKit, "fountain-round.glb", new Vector3(0f, 0f, 1f), scale: 1.3f);

        AddProp(props, TownAssets.FantasyTownKit, "tree.glb", new Vector3(-16f, 0f, -2f));
        AddProp(props, TownAssets.FantasyTownKit, "tree-high.glb", new Vector3(15f, 0f, -4f));
        AddProp(props, TownAssets.FantasyTownKit, "tree-crooked.glb", new Vector3(-4f, 0f, 16f));
        AddProp(props, TownAssets.FantasyTownKit, "tree.glb", new Vector3(6f, 0f, 17f));
        AddProp(props, TownAssets.FantasyTownKit, "tree-high.glb", new Vector3(-17f, 0f, 12f));

        AddProp(props, TownAssets.FantasyTownKit, "fence.glb", new Vector3(-7f, 0f, 1.5f), rotationYDeg: 0f);
        AddProp(props, TownAssets.FantasyTownKit, "fence.glb", new Vector3(-7f, 0f, 2.5f), rotationYDeg: 0f);
        AddProp(props, TownAssets.FantasyTownKit, "fence-gate.glb", new Vector3(-7f, 0f, 3.5f), rotationYDeg: 0f);

        AddProp(props, TownAssets.FantasyTownKit, "cart.glb", new Vector3(6f, 0f, -3f), rotationYDeg: 35f);
        AddProp(props, TownAssets.FantasyTownKit, "rock-large.glb", new Vector3(-3f, 0f, -17f));
        AddProp(props, TownAssets.FantasyTownKit, "rock-small.glb", new Vector3(4f, 0f, -18f), rotationYDeg: 60f);
        AddProp(props, TownAssets.FantasyTownKit, "rock-small.glb", new Vector3(16f, 0f, 3f), rotationYDeg: 200f);

        AddProp(props, TownAssets.FantasyTownKit, "lantern.glb", new Vector3(1.5f, 0f, 4.5f), lanternGlow: true);
        AddProp(props, TownAssets.FantasyTownKit, "lantern.glb", new Vector3(-1.5f, 0f, 4.5f), lanternGlow: true);

        // Wire the AI-gen prop GLBs (previously orphaned — see GenAssetCoverageTests): each is
        // rescaled to a sane prop height from its OWN mesh bounds (gen assets ship at varying baked
        // scales, so we never trust the file's raw size), placed clear of every Building3D interact
        // zone and the gate departure lane (x≈0, z -20..-15). Pure decoration, no colliders.
        AddGenProp(props, "well.glb", new Vector3(-3f, 0f, 5.5f), targetHeight: 1.3f);
        AddGenProp(props, "barrel.glb", new Vector3(-11f, 0f, 4.5f), targetHeight: 0.8f);
        AddGenProp(props, "barrel.glb", new Vector3(-10.2f, 0f, 9f), targetHeight: 0.8f, rotationYDeg: 40f);
        AddGenProp(props, "ore-cart.glb", new Vector3(4f, 0f, -15f), targetHeight: 1.0f, rotationYDeg: 20f);
        AddGenProp(props, "market-stall.glb", new Vector3(12.5f, 0f, -4f), targetHeight: 2.2f, rotationYDeg: -30f);
        AddGenProp(props, "bounty-board.glb", new Vector3(12f, 0f, 9f), targetHeight: 1.6f, rotationYDeg: -20f);
        AddGenProp(props, "signpost.glb", new Vector3(2.5f, 0f, 3.5f), targetHeight: 2.2f, rotationYDeg: 30f);
        AddGenProp(props, "haybale.glb", new Vector3(-6f, 0f, 12f), targetHeight: 0.8f, rotationYDeg: 0f);
        AddGenProp(props, "haybale.glb", new Vector3(-5.1f, 0f, 12.6f), targetHeight: 0.7f, rotationYDeg: 55f);
        AddGenProp(props, "statue.glb", new Vector3(-4f, 0f, 9f), targetHeight: 1.9f, rotationYDeg: 200f);
        AddGenProp(props, "lamp-post.glb", new Vector3(5f, 0f, 5.5f), targetHeight: 2.4f, rotationYDeg: 0f, lampGlow: true);
        AddGenProp(props, "tree-stump.glb", new Vector3(-14f, 0f, -6f), targetHeight: 0.7f, rotationYDeg: 20f);
        AddGenProp(props, "trough.glb", new Vector3(-11.5f, 0f, 6.5f), targetHeight: 0.7f, rotationYDeg: 10f);
        AddGenProp(props, "standing-lantern.glb", new Vector3(-5f, 0f, 5.5f), targetHeight: 1.6f, rotationYDeg: 0f, lampGlow: true);
        AddGenProp(props, "grain-sack.glb", new Vector3(11f, 0f, -2f), targetHeight: 0.7f, rotationYDeg: 10f);
        AddGenProp(props, "shop-sign.glb", new Vector3(10f, 0f, -6.5f), targetHeight: 1.0f, rotationYDeg: -20f);
        AddGenProp(props, "bucket.glb", new Vector3(-2f, 0f, 6f), targetHeight: 0.5f, rotationYDeg: 0f);
        AddGenProp(props, "scarecrow.glb", new Vector3(-7.5f, 0f, 13.5f), targetHeight: 1.9f, rotationYDeg: 210f);
        AddGenProp(props, "flower-planter.glb", new Vector3(1f, 0f, 6.5f), targetHeight: 0.5f, rotationYDeg: 15f);

        return props;
    }

    private static void AddProp(Node3D parent, string kitFolder, string asset, Vector3 position, float rotationYDeg = 0f, float scale = 1f, bool lanternGlow = false)
    {
        var piece = TownAssets.Instantiate(kitFolder, asset);
        if (piece == null)
        {
            return;
        }

        piece.Position = position;
        piece.RotationDegrees = new Vector3(0f, rotationYDeg, 0f);
        piece.Scale = new Vector3(scale, scale, scale);
        parent.AddChild(piece);

        if (lanternGlow)
        {
            TownAssets.AttachLanternGlow(piece);
        }
    }

    /// <summary>Places a normalized AI-gen prop GLB (<see cref="TownAssets.InstantiateGen"/>) as pure
    /// decoration, uniformly rescaled from its own mesh bounds to <paramref name="targetHeight"/>
    /// units — gen assets ship at varying baked scales, so we fit each to a sane prop height rather
    /// than trusting the file. No collider (mirrors <see cref="AddProp"/>); a missing file is a silent
    /// skip (<c>GenAssetCoverageTests</c> guards presence + wiring separately).</summary>
    private static void AddGenProp(Node3D parent, string fileName, Vector3 position, float targetHeight, float rotationYDeg = 0f, bool lampGlow = false)
    {
        var piece = TownAssets.InstantiateGen(fileName);
        if (piece == null)
        {
            return;
        }

        var height = MeshHeight(piece, 1f);
        var scale = height > 0.001f ? targetHeight / height : 1f;
        piece.Scale = new Vector3(scale, scale, scale);
        piece.Position = position;
        piece.RotationDegrees = new Vector3(0f, rotationYDeg, 0f);
        // Recognizable node name (e.g. "Gen_well") so scene inspection + the placement smoke test
        // can find gen props among the Kenney dressing; Godot auto-suffixes the duplicate barrel.
        piece.Name = "Gen_" + System.IO.Path.GetFileNameWithoutExtension(fileName);

        // A warm point light near the prop's top (child local Y ≈ 0.85·natural height, so after the
        // uniform scale above it lands at ~0.85·targetHeight world) — for the lamp-post so it reads
        // as a lit street lamp, not a grey pole. Also contributes to the town's night ambience.
        if (lampGlow)
        {
            piece.AddChild(new OmniLight3D
            {
                Name = "LampGlow",
                Position = new Vector3(0f, height * 0.85f, 0f),
                LightColor = new Color(1f, 0.82f, 0.45f),
                LightEnergy = 1.6f,
                OmniRange = 6f,
            });
        }

        parent.AddChild(piece);
    }

    /// <summary>Tallest descendant <see cref="MeshInstance3D"/> AABB height, folding in each node's Y
    /// scale on the way down — enough to read a gen asset's natural height for uniform rescaling.
    /// Pure resource read (<c>Mesh.GetAabb</c>), never a render, so it is headless-test safe.
    /// Internal (not private) so <see cref="InteriorRoom3D"/> reuses the SAME sizing read for its
    /// own gen-prop dressing instead of forking the pattern.</summary>
    internal static float MeshHeight(Node node, float scaleY)
    {
        if (node is Node3D n3)
        {
            scaleY *= n3.Scale.Y;
        }

        var height = 0f;
        if (node is MeshInstance3D mesh && mesh.Mesh != null)
        {
            height = mesh.Mesh.GetAabb().Size.Y * scaleY;
        }

        foreach (var child in node.GetChildren())
        {
            height = Mathf.Max(height, MeshHeight(child, scaleY));
        }

        return height;
    }

    /// <summary>Key light (the sun): warm, from the upper front-left. Slightly brighter than the
    /// original 1.1 so lit faces of the PBR gen assets read with punch.</summary>
    private static DirectionalLight3D BuildLight() => new()
    {
        Name = "SunLight",
        RotationDegrees = new Vector3(-55, -30, 0),
        LightColor = new Color(1f, 0.96f, 0.9f),
        LightEnergy = 1.2f,
    };

    /// <summary>Fill light: a dim, cool, shadowless light from the OPPOSITE side that lifts the
    /// faces the sun leaves dark. This is the main fix for "everything looks dark/flat" — the town
    /// (and the baked-PBR gen assets, which go near-black on unlit faces under low ambient) had only
    /// a single key light + modest ambient, so back/side faces read almost black. A classic
    /// key+fill pair opens them up without washing the scene out.</summary>
    private static DirectionalLight3D BuildFillLight() => new()
    {
        Name = "FillLight",
        RotationDegrees = new Vector3(-25, 150, 0),
        LightColor = new Color(0.75f, 0.83f, 1f),
        LightEnergy = 0.45f,
        ShadowEnabled = false,
    };

    /// <summary>Sky + ambient. Ambient (and the procedural sky feeding it) were dim enough that
    /// shadowed geometry crushed to black; raised so the whole town sits in a readable daylight
    /// ambient. Filmic tonemap kept (safe, no colour surprises).</summary>
    private static WorldEnvironment BuildEnvironment()
    {
        // A real daytime sky (not the near-black void the low-energy default read as): a warm,
        // slightly hazy blue dome over a soft green-tinted horizon, so the world beyond the village
        // reads as open countryside. Visual round 2026-07-24 (stylized-3D direction): these are
        // placeholder gradient colors a generated skybox texture can later replace.
        var sky = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color(0.35f, 0.55f, 0.85f),      // clear upper blue
            SkyHorizonColor = new Color(0.78f, 0.82f, 0.80f),  // pale hazy horizon
            SkyEnergyMultiplier = 1.0f,
            GroundHorizonColor = new Color(0.62f, 0.66f, 0.55f), // meadow haze meeting the sky
            GroundBottomColor = new Color(0.40f, 0.45f, 0.34f),
            GroundEnergyMultiplier = 0.9f,
            SunAngleMax = 30f,
            SunCurve = 0.15f,
        };

        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = sky },
            BackgroundEnergyMultiplier = 1.0f,
            AmbientLightSource = Godot.Environment.AmbientSource.Bg,
            AmbientLightEnergy = 1.1f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            // A touch of distance haze so the far treeline/walls fade into the horizon — sells scale.
            FogEnabled = true,
            FogLightColor = new Color(0.74f, 0.80f, 0.82f),
            FogDensity = 0.006f,
            FogSkyAffect = 0.2f,
        };
        return new WorldEnvironment { Name = "WorldEnvironment", Environment = env };
    }

    /// <summary>A warm key sun that casts the village's shadows — gives the flat-shaded 3D town depth
    /// and time-of-day warmth (visual round). Paired with the sky's own ambient fill in
    /// <see cref="BuildEnvironment"/>.</summary>
    private static DirectionalLight3D BuildSun()
    {
        var sun = new DirectionalLight3D
        {
            Name = "Sun",
            LightColor = new Color(1.0f, 0.94f, 0.82f), // warm late-morning
            LightEnergy = 1.15f,
            ShadowEnabled = true,
        };
        // Angled low from the south-west for long, readable shadows across the square.
        sun.RotationDegrees = new Vector3(-52f, -130f, 0f);
        return sun;
    }
}
