using Godot;

namespace GodotClient.Town3d;

/// <summary>
/// T3: standalone scaffold for the grounded 3D town — a <see cref="SubViewportContainer"/>
/// hosting a picking-enabled <see cref="SubViewport"/> whose <see cref="World"/> is a plain
/// <see cref="Node3D"/> with a box-floor ground, a <see cref="CameraRig"/>, ambient light, and
/// empty <see cref="Buildings"/>/<see cref="Heroes"/> containers for later tasks to populate.
///
/// <para>Deliberately standalone: this task mounts <see cref="Town3D"/> directly in a
/// code-built test rig (see <c>Town3DSceneTests</c>) rather than through <c>MainUi</c> — the
/// <c>MainUi</c> cutover is a single atomic task (T8) at the end of the plan, so this type has
/// no <c>Player</c> property yet (T4) and no building/hero population yet (T5/T7).</para>
/// </summary>
public partial class Town3D : SubViewportContainer
{
    public SubViewport Viewport { get; private set; } = null!;
    public Node3D World { get; private set; } = null!;
    public CameraRig Camera { get; private set; } = null!;
    public Node3D Buildings { get; private set; } = null!;
    public Node3D Heroes { get; private set; } = null!;

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
