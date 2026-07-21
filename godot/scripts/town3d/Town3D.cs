using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace GodotClient.Town3d;

/// <summary>
/// T3+: standalone scaffold for the grounded 3D town — a <see cref="SubViewportContainer"/>
/// hosting a picking-enabled <see cref="SubViewport"/> whose <see cref="World"/> is a plain
/// <see cref="Node3D"/> with a box-floor ground, a <see cref="CameraRig"/>, ambient light, six
/// interactable <see cref="Buildings"/> (T5), a display-only <see cref="MemorialPlot"/> (T5,
/// populated T7), and <see cref="WorldInputNode"/> driving proximity/highlight/interact (T5). The
/// <see cref="Heroes"/> container stays empty until T7.
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

    /// <summary>Raised when a building is clicked/interacted with (T5+); re-emits into
    /// <c>MainUi</c>'s existing 2D-town vocabulary unchanged (KTD2 — presentation-only).</summary>
    public event System.Action<string>? BuildingClicked;

    /// <summary>Raised when a hero actor is clicked (T7+).</summary>
    public event System.Action<int>? HeroClicked;

    /// <summary>
    /// Builds the whole standalone 3D scaffold: viewport, world, ground, light, camera. Safe to
    /// call once per instance (mirrors the <c>MainUi.Build</c>/panel idiom elsewhere in this
    /// codebase). <paramref name="adapter"/> is accepted now (later tasks reconcile heroes/
    /// buildings from it) but unused by the scaffold itself.
    /// </summary>
    public void Build(GodotClient.SimAdapter adapter)
    {
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

        World.AddChild(BuildGround());
        World.AddChild(BuildLight());
        World.AddChild(BuildEnvironment());

        Buildings = new Node3D { Name = "Buildings" };
        World.AddChild(Buildings);

        Heroes = new Node3D { Name = "Heroes" };
        World.AddChild(Heroes);

        Camera = new CameraRig { Name = "CameraRig" };
        World.AddChild(Camera);

        Player = BuildPlayer();
        World.AddChild(Player);
        Player.Cam = Camera.GetNode<Camera3D>("Camera3D");
        Camera.Target = Player;

        var buildings = BuildBuildings();
        foreach (var building in buildings)
        {
            Buildings.AddChild(building);
        }

        MemorialPlot = BuildMemorialPlot();
        World.AddChild(MemorialPlot);

        WorldInputNode = new WorldInput3D { Name = "WorldInput3D" };
        WorldInputNode.Configure(Player, buildings);
        World.AddChild(WorldInputNode);
        WorldInputNode.Interacted += key => BuildingClicked?.Invoke(key);
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
    /// ground and building footprints, T5) with a feet-anchored capsule collider and a visual
    /// child sourced from <see cref="TownAssets.HeroScene"/> (variant 0) or a primitive capsule
    /// fallback when the Kenney asset is missing.
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
