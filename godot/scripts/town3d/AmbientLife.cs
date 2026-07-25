using Godot;

namespace GodotClient.Town3d;

/// <summary>
/// Ambient life pass (presentation-only, zero sim impact): the small moving details that make the
/// purple-dusk village read as alive rather than a static diorama — chimney smoke drifting up off a
/// couple of rooftops, and warm firefly/ember motes bobbing slowly around the square and the
/// brazier-lit mine road. Every emitter is a plain <see cref="CpuParticles3D"/> node (same choice as
/// the forge station's <c>ForgeSparks</c>/<c>QuenchSteam</c> in <see cref="Town3D"/>) — headless-safe
/// (nothing here depends on a frame actually rendering, and this file never pumps a live 3D
/// <see cref="SubViewport"/>, so the documented headless-hang trap does not apply). No collider, no
/// nav nodes, no RNG outside the engine's own particle scatter (that's Godot decoration randomness,
/// not <c>sim/GameSim/</c> RNG, so determinism/golden-replay are untouched). A single
/// <see cref="Build"/> returns one <see cref="Node3D"/> the caller adds under <c>Town3D.World</c>.
/// Positions are hand-placed constants keyed to the spread building layout (tavern ~(-12,0,11),
/// forge ~(-12,0,-9), mine road braziers at x≈±3.6, z ∈ {-12,-19,-26}).
/// </summary>
public static class AmbientLife
{
    public static Node3D Build()
    {
        var root = new Node3D { Name = "AmbientLife" };
        root.AddChild(BuildChimneySmoke());
        root.AddChild(BuildFireflies());
        return root;
    }

    /// <summary>Soft grey puffs rising off a couple of rooftops — slow upward drift with a slight
    /// sideways lean, low density so it reads as smoke rather than fog.</summary>
    private static Node3D BuildChimneySmoke()
    {
        var group = new Node3D { Name = "ChimneySmoke" };
        group.AddChild(BuildSmokePuff("TavernChimney", new Vector3(-12f, 7f, 11f)));
        group.AddChild(BuildSmokePuff("ForgeChimney", new Vector3(-12f, 7f, -9f)));
        return group;
    }

    private static CpuParticles3D BuildSmokePuff(string name, Vector3 pos)
    {
        return new CpuParticles3D
        {
            Name = name,
            Position = pos,
            Emitting = true,
            OneShot = false,
            Amount = 10,
            Lifetime = 5.5,
            Explosiveness = 0f,
            Randomness = 0.45f,
            LocalCoords = false, // world-space drift, independent of the (static) emitter transform
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.18f,
            Direction = new Vector3(0.15f, 1f, 0.08f),
            Spread = 16f,
            Gravity = new Vector3(0f, 0.28f, 0f), // gentle buoyant lift, not real gravity
            InitialVelocityMin = 0.22f,
            InitialVelocityMax = 0.5f,
            ScaleAmountMin = 0.55f,
            ScaleAmountMax = 1.35f,
            Color = new Color(0.75f, 0.75f, 0.78f, 0.35f),
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                VertexColorUseAsAlbedo = true,
                AlbedoColor = new Color(0.75f, 0.75f, 0.78f, 0.35f),
            },
        };
    }

    /// <summary>Tiny warm-glowing motes (fireflies / drifting embers) bobbing gently around the
    /// square and the brazier-lit mine road — the only "magic" touch in the cool dusk, additive amber
    /// so they read as a soft glow rather than solid sprites.</summary>
    private static Node3D BuildFireflies()
    {
        var group = new Node3D { Name = "Fireflies" };
        group.AddChild(BuildFireflyCluster("SquareMotes", new Vector3(0f, 1.4f, 0f), 3f));
        group.AddChild(BuildFireflyCluster("RoadBrazierMotes", new Vector3(0f, 1.6f, -14f), 2.5f));
        group.AddChild(BuildFireflyCluster("TavernMotes", new Vector3(-11f, 1.3f, 10f), 2f));
        return group;
    }

    private static CpuParticles3D BuildFireflyCluster(string name, Vector3 pos, float spreadRadius)
    {
        return new CpuParticles3D
        {
            Name = name,
            Position = pos,
            Emitting = true,
            OneShot = false,
            Amount = 24,
            Lifetime = 7.0,
            Explosiveness = 0f,
            Randomness = 0.85f,
            LocalCoords = false, // world-space drift
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = spreadRadius,
            Direction = new Vector3(0f, 1f, 0f),
            Spread = 180f, // near-omnidirectional gentle bob, not a directional stream
            Gravity = Vector3.Zero,
            InitialVelocityMin = 0.05f,
            InitialVelocityMax = 0.22f,
            ScaleAmountMin = 0.05f,
            ScaleAmountMax = 0.12f,
            Color = new Color(1f, 0.8f, 0.4f, 0.9f),
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                VertexColorUseAsAlbedo = true,
                AlbedoColor = new Color(1f, 0.8f, 0.4f, 0.9f),
                EmissionEnabled = true,
                Emission = new Color(1f, 0.8f, 0.4f),
                EmissionEnergyMultiplier = 2.5f,
            },
        };
    }
}
