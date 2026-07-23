using Godot;
using GodotClient.Town3d;

namespace GodotClient.Panels;

/// <summary>
/// A tiny self-contained 3D stage (<see cref="SubViewport"/> + <see cref="Camera3D"/> + light)
/// that frames ONE AI-gen monster GLB so a real 3D mesh can appear inside the 2D-ish spectate
/// panels — first consumer is <see cref="MineWatch"/>'s milestone flash, which draws this
/// viewport's texture on a Sprite2D where the 2D silhouette used to slide. Kind→GLB resolution
/// lives in <see cref="AssetCatalog.MonsterModelFile"/>; a kind with no gen model (e.g. "The
/// Forgeworm") makes <see cref="ShowMonster"/> return false, which is the caller's signal to keep
/// the existing 2D portrait fallback — never a crash (KTD graceful-degrade convention).
///
/// <para><b>3D-render-hang rule.</b> A rendering 3D SubViewport hangs the headless gdUnit runner
/// the moment anything pumps frames, so this viewport is <see cref="SubViewport.UpdateMode.Disabled"/>
/// from birth and only ever switches to Always while a monster is actually shown AND the display
/// server is not headless (see <see cref="SetRendering"/>) — headless test runs can build, show,
/// and assert everything here property-only without a single render being scheduled.</para>
///
/// <para>Adapter-only (KTD2): pure render state, reads no sim types, no RNG, no wall clock — the
/// optional idle spin below is accumulated frame delta, same contract as every
/// <see cref="MineWatch"/> animator.</para>
/// </summary>
public partial class MonsterView3D : SubViewport
{
    /// <summary>World-unit height every monster mesh is uniformly rescaled to from its OWN AABB
    /// (gen assets ship at varying baked scales — same fit-by-own-bounds rule as
    /// <c>Town3D.AddGenProp</c>), chosen so the camera below frames it with headroom.</summary>
    public const float FramedHeight = 1.6f;

    /// <summary>Render-target pixel size — small on purpose (a strip inset, not a hero shot).</summary>
    public static readonly Vector2I ViewSize = new(256, 256);

    private const float IdleSpinDegreesPerSecond = 35f;

    private Node3D _stage = null!;
    private Node3D? _monster;
    private float _time;
    private bool _built;

    /// <summary>The kind (as passed by the caller) currently on stage, null when empty (test hook).</summary>
    public string? CurrentKind { get; private set; }

    /// <summary>True while a gen monster mesh is instantiated on the stage (test hook).</summary>
    public bool HasMonster => _monster is not null;

    /// <summary>The shown mesh's AABB height AFTER rescaling, world units — ≈<see cref="FramedHeight"/>
    /// for any non-degenerate mesh (test hook for the height-fit contract). 0 when empty.</summary>
    public float FittedHeight { get; private set; }

    /// <summary>Build the stage: camera, key light, soft ambient, empty monster root. Idempotent.</summary>
    public void Build()
    {
        if (_built)
        {
            return;
        }

        Name = "MonsterView3D";
        Size = ViewSize;
        OwnWorld3D = true; // isolated World3D — this viewport lives inside a 2D panel chain
        TransparentBg = true; // composites over the strip's backdrop, no opaque void
        HandleInputLocally = false;
        RenderTargetUpdateMode = UpdateMode.Disabled; // 3D-render-hang rule — see type remarks

        // Rotation set directly (never LookAt — that needs a live global transform, and tests
        // build this node orphaned). Slightly above mid-height, gently pitched down.
        AddChild(new Camera3D
        {
            Name = "MonsterCamera",
            Position = new Vector3(0f, FramedHeight * 0.62f, FramedHeight * 1.75f),
            RotationDegrees = new Vector3(-6f, 0f, 0f),
            Current = true,
        });

        AddChild(new DirectionalLight3D
        {
            Name = "MonsterKeyLight",
            RotationDegrees = new Vector3(-45f, -30f, 0f),
            LightEnergy = 1.3f,
        });

        // Soft ambient so the unlit side never reads as a black cutout against the strip.
        AddChild(new WorldEnvironment
        {
            Name = "MonsterEnvironment",
            Environment = new Godot.Environment
            {
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.55f, 0.55f, 0.62f),
                AmbientLightEnergy = 0.7f,
            },
        });

        _stage = new Node3D { Name = "MonsterStage" };
        AddChild(_stage);

        _built = true;
    }

    /// <summary>
    /// Put <paramref name="kind"/>'s gen GLB on the stage, height-fitted to <see cref="FramedHeight"/>
    /// from its own mesh AABB. Returns false — stage cleared, rendering off — when the kind has no
    /// gen model yet or the file failed to instantiate: the caller's cue to fall back to 2D art.
    /// </summary>
    public bool ShowMonster(string kind)
    {
        Build();
        ClearMonster();

        var file = AssetCatalog.MonsterModelFile(kind);
        var mesh = file is null ? null : TownAssets.InstantiateGen(file);
        if (mesh is null)
        {
            return false;
        }

        var height = MeshHeight(mesh, 1f);
        var scale = height > 0.001f ? FramedHeight / height : 1f;
        mesh.Scale = new Vector3(scale, scale, scale);
        mesh.Name = "Monster";
        _stage.AddChild(mesh);

        _monster = mesh;
        CurrentKind = kind;
        FittedHeight = height * scale;
        SetRendering(true);
        return true;
    }

    /// <summary>Empty the stage and stop scheduling renders (called by <see cref="ShowMonster"/>
    /// and by the owner when its flash ends).</summary>
    public void ClearMonster()
    {
        if (_monster is not null)
        {
            _stage.RemoveChild(_monster);
            _monster.Free();
            _monster = null;
        }

        CurrentKind = null;
        FittedHeight = 0f;
        _stage.Rotation = Vector3.Zero;
        _time = 0f;
        SetRendering(false);
    }

    /// <summary>Slow idle Y-spin while a monster is on stage — accumulated delta only.</summary>
    public override void _Process(double delta)
    {
        if (_monster is null)
        {
            return;
        }

        _time += (float)delta;
        _stage.RotationDegrees = new Vector3(0f, _time * IdleSpinDegreesPerSecond, 0f);
    }

    /// <summary>The one place the update mode changes: Always only while showing AND not headless
    /// (a headless run must never have a 3D render scheduled — see type remarks).</summary>
    private void SetRendering(bool showing) =>
        RenderTargetUpdateMode = showing && DisplayServer.GetName() != "headless"
            ? UpdateMode.Always
            : UpdateMode.Disabled;

    /// <summary>Tallest descendant <see cref="MeshInstance3D"/> AABB height, folding each node's Y
    /// scale in on the way down. Local copy of <c>Town3D.MeshHeight</c> (private there; duplicated
    /// rather than widening a parallel-PR-owned file — same cross-lane duplication rule as
    /// <c>MineWatch.ScaleToWidth</c>). Pure resource read (<c>Mesh.GetAabb</c>), never a render,
    /// headless-test safe.</summary>
    private static float MeshHeight(Node node, float scaleY)
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
}
