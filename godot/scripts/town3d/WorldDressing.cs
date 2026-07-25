using Godot;

namespace GodotClient.Town3d;

/// <summary>
/// Visual round (world-connection pass): decoration-only props that dress the village into a lived-in
/// quest hub — a cobbled road out to the mine gate, a central well, warm lantern glow for the purple
/// dusk, market stalls, and scattered crates/barrels. Everything here is pure presentation: NO
/// collider (heroes/player roam the open square unobstructed), NO RNG, NO sim state — a single
/// <see cref="Build"/> returns one <see cref="Node3D"/> the caller adds under <c>Town3D.World</c>.
/// Positions are hand-placed constants keyed to the spread building layout (mine gate at z≈-26).
/// </summary>
public static class WorldDressing
{
    private const string CobblePath = "res://assets/textures/env/cobble.png";
    private const string PalisadePath = "res://assets/textures/env/palisade.png";

    public static Node3D Build()
    {
        var root = new Node3D { Name = "WorldDressing" };

        root.AddChild(BuildRoad());
        root.AddChild(BuildWell(new Vector3(-6f, 0f, 3f)));

        // Warm lanterns ring the square + light the road out — the ONLY warm pools in the cool purple
        // dusk, so they read as a living settlement against the gloom.
        foreach (var spot in LanternSpots)
        {
            root.AddChild(BuildLantern(spot));
        }

        root.AddChild(BuildStall(new Vector3(5f, 0f, 4f), new Color(0.72f, 0.24f, 0.22f)));   // red-striped
        root.AddChild(BuildStall(new Vector3(7f, 0f, -2f), new Color(0.24f, 0.42f, 0.60f)));   // blue-striped

        foreach (var crate in CrateSpots)
        {
            root.AddChild(BuildCrate(crate));
        }

        return root;
    }

    private static readonly Vector3[] LanternSpots =
    {
        new(-9f, 0f, -9f), new(9f, 0f, -9f), new(-9f, 0f, 9f), new(9f, 0f, 9f),
        new(-3.4f, 0f, -13f), new(3.4f, 0f, -20f), new(-3.4f, 0f, -24f),
    };

    private static readonly Vector3[] CrateSpots =
    {
        new(-10.5f, 0f, 2f), new(-10f, 0f, 3.6f), new(10.5f, 0f, 4f),
        new(2.5f, 0f, 9.5f), new(-2f, 0f, -9f),
    };

    /// <summary>The cobbled approach road from the square down to the mine gate (z ≈ -26) — the artery
    /// the heroes march out along, so the world reads as connected village → dungeon.</summary>
    private static Node3D BuildRoad()
    {
        var mat = new StandardMaterial3D { Roughness = 0.95f };
        if (ResourceLoader.Exists(CobblePath))
        {
            mat.AlbedoTexture = ResourceLoader.Load<Texture2D>(CobblePath);
            mat.Uv1Scale = new Vector3(1.4f, 5f, 1f);
            mat.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic;
            mat.AlbedoColor = new Color(0.82f, 0.80f, 0.84f); // slightly cooled toward the dusk
        }
        else
        {
            mat.AlbedoColor = new Color(0.48f, 0.44f, 0.42f);
        }

        return new MeshInstance3D
        {
            Name = "MineRoad",
            Position = new Vector3(0f, 0.05f, -14f),
            Mesh = new PlaneMesh { Size = new Vector2(7f, 30f), Material = mat },
        };
    }

    private static Node3D BuildWell(Vector3 pos)
    {
        var well = new Node3D { Name = "Well", Position = pos };
        var stone = new StandardMaterial3D { AlbedoColor = new Color(0.52f, 0.50f, 0.55f), Roughness = 1f };

        well.AddChild(new MeshInstance3D
        {
            Name = "Rim",
            Position = new Vector3(0f, 0.5f, 0f),
            Mesh = new CylinderMesh { TopRadius = 1.1f, BottomRadius = 1.2f, Height = 1.0f, Material = stone },
        });
        well.AddChild(new MeshInstance3D
        {
            Name = "Water",
            Position = new Vector3(0f, 0.85f, 0f),
            Mesh = new CylinderMesh
            {
                TopRadius = 0.95f, BottomRadius = 0.95f, Height = 0.08f,
                Material = new StandardMaterial3D { AlbedoColor = new Color(0.10f, 0.16f, 0.26f), Metallic = 0.4f, Roughness = 0.15f },
            },
        });

        var postMat = new StandardMaterial3D { AlbedoColor = new Color(0.34f, 0.24f, 0.16f), Roughness = 1f };
        foreach (var x in new[] { -1.0f, 1.0f })
        {
            well.AddChild(new MeshInstance3D
            {
                Name = $"Post{(x < 0 ? "L" : "R")}",
                Position = new Vector3(x, 1.6f, 0f),
                Mesh = new CylinderMesh { TopRadius = 0.08f, BottomRadius = 0.08f, Height = 2.2f, Material = postMat },
            });
        }

        well.AddChild(new MeshInstance3D
        {
            Name = "Roof",
            Position = new Vector3(0f, 2.9f, 0f),
            Mesh = new PrismMesh { Size = new Vector3(2.6f, 0.7f, 1.6f), Material = new StandardMaterial3D { AlbedoColor = new Color(0.40f, 0.22f, 0.16f), Roughness = 1f } },
        });

        return well;
    }

    /// <summary>A lamp post: dark timber pole + a glowing amber lantern head casting a warm
    /// <see cref="OmniLight3D"/> pool. The emissive orb + real light make it read as lit in the dusk.</summary>
    private static Node3D BuildLantern(Vector3 pos)
    {
        var lantern = new Node3D { Name = "Lantern", Position = pos };

        lantern.AddChild(new MeshInstance3D
        {
            Name = "Pole",
            Position = new Vector3(0f, 1.4f, 0f),
            Mesh = new CylinderMesh
            {
                TopRadius = 0.07f, BottomRadius = 0.09f, Height = 2.8f,
                Material = new StandardMaterial3D { AlbedoColor = new Color(0.20f, 0.16f, 0.13f), Roughness = 1f },
            },
        });

        var glow = new StandardMaterial3D
        {
            AlbedoColor = new Color(1.0f, 0.78f, 0.42f),
            EmissionEnabled = true,
            Emission = new Color(1.0f, 0.72f, 0.36f),
            EmissionEnergyMultiplier = 3.2f,
        };
        lantern.AddChild(new MeshInstance3D
        {
            Name = "Flame",
            Position = new Vector3(0f, 2.95f, 0f),
            Mesh = new SphereMesh { Radius = 0.22f, Height = 0.44f, Material = glow },
        });

        lantern.AddChild(new OmniLight3D
        {
            Name = "Light",
            Position = new Vector3(0f, 2.95f, 0f),
            LightColor = new Color(1.0f, 0.74f, 0.42f),
            LightEnergy = 2.6f,
            OmniRange = 8.5f,
            OmniAttenuation = 1.4f,
        });

        return lantern;
    }

    private static Node3D BuildStall(Vector3 pos, Color canopy)
    {
        var stall = new Node3D { Name = "Stall", Position = pos };
        var wood = new StandardMaterial3D { AlbedoColor = new Color(0.40f, 0.28f, 0.18f), Roughness = 1f };

        stall.AddChild(new MeshInstance3D
        {
            Name = "Counter",
            Position = new Vector3(0f, 0.6f, 0f),
            Mesh = new BoxMesh { Size = new Vector3(2.4f, 1.2f, 1.2f), Material = wood },
        });
        stall.AddChild(new MeshInstance3D
        {
            Name = "Canopy",
            Position = new Vector3(0f, 2.4f, 0f),
            Mesh = new BoxMesh
            {
                Size = new Vector3(2.9f, 0.18f, 1.7f),
                Material = new StandardMaterial3D { AlbedoColor = canopy, Roughness = 1f },
            },
        });
        foreach (var (x, z) in new[] { (-1.2f, -0.6f), (1.2f, -0.6f), (-1.2f, 0.6f), (1.2f, 0.6f) })
        {
            stall.AddChild(new MeshInstance3D
            {
                Name = "Leg",
                Position = new Vector3(x, 1.4f, z),
                Mesh = new CylinderMesh { TopRadius = 0.06f, BottomRadius = 0.06f, Height = 2f, Material = wood },
            });
        }

        return stall;
    }

    private static Node3D BuildCrate(Vector3 pos)
    {
        var mat = new StandardMaterial3D { Roughness = 1f };
        if (ResourceLoader.Exists(PalisadePath))
        {
            mat.AlbedoTexture = ResourceLoader.Load<Texture2D>(PalisadePath);
            mat.Uv1Scale = new Vector3(0.5f, 0.5f, 1f);
            mat.AlbedoColor = new Color(0.70f, 0.60f, 0.48f);
        }
        else
        {
            mat.AlbedoColor = new Color(0.42f, 0.30f, 0.18f);
        }

        return new MeshInstance3D
        {
            Name = "Crate",
            Position = new Vector3(pos.X, 0.45f, pos.Z),
            RotationDegrees = new Vector3(0f, (pos.X * 37f) % 45f, 0f), // deterministic slight turn
            Mesh = new BoxMesh { Size = new Vector3(0.9f, 0.9f, 0.9f), Material = mat },
        };
    }
}
