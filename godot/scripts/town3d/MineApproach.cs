using System.Collections.Generic;
using Godot;

namespace GodotClient.Town3d;

/// <summary>
/// Ambient danger dressing for the dark road beyond the mine gate (<c>Town3D</c>'s "minegate"
/// building anchors the descent; this unit's brief places the guard further out, z below -26 —
/// "in the mine mouth", never back in the village). A handful of <see cref="MineCreature"/> posts
/// slow-patrol near their spawn point so the descent reads as visibly dangerous before a hero ever
/// sets out. PRESENTATION ONLY (KTD2): no sim reads, no RNG, no nav-participating collider — pure
/// decoration <c>Town3D</c> (or whichever orchestrator wires this in) can add/remove at will.
/// </summary>
public static class MineApproach
{
    /// <summary>Cycled per creature (index % Length) so the guard reads as a mixed pack rather
    /// than one species repeated.</summary>
    private static readonly string[] CreatureFiles =
    {
        "monster-bramble-boar.glb",
        "monster-cave-rat.glb",
        "monster-crypt-crab.glb",
        "monster-ghoul.glb",
        "monster-lantern-moth.glb",
        "monster-ore-golem.glb",
        "monster-spider.glb",
    };

    /// <summary>Per-species target height (world units; heroes read ~1.6 units tall) that <see
    /// cref="MineCreature.Configure"/> height-fits its mesh's OWN AABB to — same fit-by-own-bounds
    /// convention as <c>MonsterView3D.ShowMonster</c> / <c>Town3D.AddGenProp</c>, since gen monster
    /// GLBs are not baked to a shared scale and a flat multiplier would read inconsistently across
    /// species. Small/medium beasts land 1.2-1.6; the ghoul and ore-golem loom taller, 2.5-3.</summary>
    private static readonly Dictionary<string, float> TargetHeight = new()
    {
        ["monster-bramble-boar.glb"] = 1.4f,
        ["monster-cave-rat.glb"] = 1.2f,
        ["monster-crypt-crab.glb"] = 1.3f,
        ["monster-ghoul.glb"] = 2.6f,
        ["monster-lantern-moth.glb"] = 1.5f,
        ["monster-ore-golem.glb"] = 2.9f,
        ["monster-spider.glb"] = 1.6f,
    };

    /// <summary>Deterministic posts beyond the mine gate (brief's convention: gate ≈ z=-26,
    /// negative z toward the mine) — x ∈ [-10,10], z ∈ [-30,-44], clustered like a guard blocking
    /// the descent rather than scattered at random.</summary>
    private static readonly Vector3[] Posts =
    {
        new(-7f, 0f, -30f),
        new(6f, 0f, -32f),
        new(-3f, 0f, -35f),
        new(9f, 0f, -37f),
        new(-9f, 0f, -40f),
        new(2f, 0f, -43f),
    };

    /// <summary>Builds the whole ambient guard: one <see cref="MineCreature"/> per <see
    /// cref="Posts"/> entry, species cycled from <see cref="CreatureFiles"/>. Deterministic —
    /// same call always yields the same layout, no <see cref="System.Random"/> anywhere.</summary>
    public static Node3D Build()
    {
        var root = new Node3D { Name = "MineApproach" };
        for (var i = 0; i < Posts.Length; i++)
        {
            var file = CreatureFiles[i % CreatureFiles.Length];
            var creature = new MineCreature();
            root.AddChild(creature);
            creature.Configure(i, Posts[i], file, TargetHeight.GetValueOrDefault(file, 1.6f));
        }

        return root;
    }
}

/// <summary>
/// One ambient monster guarding the mine approach: a gen GLB (height-fit to <see
/// cref="MineApproach"/>'s per-species target) or a dark angular primitive fallback when the
/// asset is missing/renamed, slow-patrolling a short deterministic loop around its spawn post.
/// The ghoul and lantern-moth also get a faint colored <see cref="OmniLight3D"/> — subtle,
/// fits the purple-dusk mood rather than announcing itself. No nav-participating collider: this
/// is pure decoration, never a body on nav layers 1/2.
/// </summary>
public partial class MineCreature : Node3D
{
    /// <summary>Patrol drift speed in world units/sec — slow and menacing, not a chase.</summary>
    public const float PatrolSpeed = 0.6f;

    /// <summary>Half-width of the left-right patrol swing around <see cref="Post"/>.</summary>
    private const float PatrolRadius = 1.6f;

    private const float BobAmplitude = 0.06f;

    /// <summary>Which mesh/light get the faint menace glow (subtle — see class doc).</summary>
    private static readonly HashSet<string> GlowFiles = new() { "monster-ghoul.glb", "monster-lantern-moth.glb" };

    public int Index { get; private set; }

    /// <summary>The spawn anchor this creature patrols around — never itself mutated after
    /// <see cref="Configure"/>, so <see cref="_Process"/> always has a stable center.</summary>
    public Vector3 Post { get; private set; }

    public Node3D Mesh { get; private set; } = null!;

    private float _phase;
    private float _bobPhase;
    private double _time;

    /// <summary>Builds the mesh (or fallback), positions at <paramref name="post"/>, and pins the
    /// deterministic per-creature patrol phase — index in, motion out, no <see
    /// cref="System.Random"/> (same convention as <c>HeroActor3D.Configure</c>'s wander phases).
    /// </summary>
    public void Configure(int index, Vector3 post, string fileName, float targetHeight)
    {
        Index = index;
        Post = post;
        Name = $"Creature_{index}";
        Position = post;

        _phase = index * 1.3f;
        _bobPhase = index * 0.7f;

        Mesh = BuildMesh(fileName, targetHeight);
        AddChild(Mesh);

        if (GlowFiles.Contains(fileName))
        {
            AddChild(BuildMenaceGlow(fileName));
        }
    }

    /// <summary>Slow left-right patrol swing plus a faint vertical bob around <see cref="Post"/> —
    /// wall-clock <paramref name="delta"/> is fine here (Godot-only presentation motion, not sim
    /// state, KTD2); no <see cref="System.Random"/>, every creature's path is a pure function of
    /// its own index and accumulated time, so it never strays from its post.</summary>
    public override void _Process(double delta)
    {
        _time += delta;
        var swing = PatrolRadius * Mathf.Sin((float)_time * PatrolSpeed + _phase);
        var bob = BobAmplitude * Mathf.Sin((float)_time * 0.9f + _bobPhase);
        Position = Post + new Vector3(swing, bob, 0f);
    }

    /// <summary>Real gen GLB height-fit to <paramref name="targetHeight"/> from its OWN AABB, or a
    /// dark angular primitive (a tinted <see cref="PrismMesh"/>, faintly emissive so it still
    /// reads as "something menacing" rather than an untextured crate) when the asset is missing —
    /// same graceful-degrade contract as <c>Building3D</c>'s wedge and <c>HeroActor3D</c>'s
    /// capsule fallback.</summary>
    private static Node3D BuildMesh(string fileName, float targetHeight)
    {
        var mesh = TownAssets.InstantiateGen(fileName);
        if (mesh != null)
        {
            mesh.Name = "Mesh";
            var height = MeshHeight(mesh, 1f);
            var scale = height > 0.001f ? targetHeight / height : 1f;
            mesh.Scale = new Vector3(scale, scale, scale);
            return mesh;
        }

        return new MeshInstance3D
        {
            Name = "Mesh",
            Mesh = new PrismMesh
            {
                Size = new Vector3(targetHeight * 0.6f, targetHeight, targetHeight * 0.6f),
                Material = new StandardMaterial3D
                {
                    AlbedoColor = new Color(0.12f, 0.08f, 0.14f),
                    EmissionEnabled = true,
                    Emission = new Color(0.25f, 0.05f, 0.3f),
                    EmissionEnergyMultiplier = 0.3f,
                },
            },
            Position = new Vector3(0f, targetHeight * 0.5f, 0f),
        };
    }

    private static OmniLight3D BuildMenaceGlow(string fileName) => new()
    {
        Name = "MenaceGlow",
        Position = new Vector3(0f, 1.2f, 0f),
        LightColor = fileName == "monster-ghoul.glb"
            ? new Color(0.55f, 0.15f, 0.65f)
            : new Color(0.75f, 0.2f, 0.25f),
        LightEnergy = 0.35f,
        OmniRange = 2.0f,
    };

    /// <summary>Tallest descendant <see cref="MeshInstance3D"/> AABB height, folding each node's Y
    /// scale in on the way down. Local copy of the same fit-by-own-bounds helper duplicated in
    /// <c>MonsterView3D.MeshHeight</c> / <c>Town3D.MeshHeight</c> (both private in files this unit
    /// does not own — cross-lane duplication rule, same precedent <c>MonsterView3D</c> itself
    /// used rather than widening a parallel-owned file). Pure resource read, never a render.
    /// </summary>
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
