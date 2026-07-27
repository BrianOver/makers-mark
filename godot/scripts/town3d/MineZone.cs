using Godot;

namespace GodotClient.Town3d;

/// <summary>
/// The mine zone: the dark cave mouth the village's cobbled road (<see cref="WorldDressing"/>'s
/// <c>MineRoad</c>) actually leads to. Today the road runs past the mine gate (z ≈ -26) and the
/// prowling-enemy band (z ≈ -30..-43, see <c>TownsfolkNpcs</c>/monster placement) and simply fades
/// into the purple dusk — nothing marks journey's end. This class builds that destination: an
/// ominous rock mound with a recessed black tunnel bore, timber framing, scattered boulders, a
/// minecart-rail hint running back toward the village, and a faint cold glow bleeding out of the
/// blackness — so the world reads as connected village → dungeon-descent.
///
/// <para>PRESENTATION ONLY: no collider, no <see cref="NavigationRegion3D"/>, zero sim coupling —
/// a single <see cref="Build"/> returns one orphaned <see cref="Node3D"/> for the caller to add
/// under <c>Town3D.World</c> (the caller wires it in; this file does not touch <c>Town3D</c>).
/// Every position below is a hand-placed constant (no RNG) so placement is fully deterministic.</para>
/// </summary>
public static class MineZone
{
    /// <summary>How far down the road (-Z) the zone sits — well beyond the mine gate (z≈-26) and
    /// the prowling-enemy band (z≈-30..-43), so it reads as the far destination, not another
    /// obstacle along the way.</summary>
    private const float ZonePosition = -46f;

    private static readonly Color RockColor = new(0.14f, 0.13f, 0.16f);
    private static readonly Color TimberColor = new(0.12f, 0.09f, 0.07f);
    private static readonly Color RailColor = new(0.10f, 0.10f, 0.11f);
    private static readonly Color VoidColor = new(0.015f, 0.015f, 0.018f);

    public static Node3D Build()
    {
        var root = new Node3D { Name = "MineZone", Position = new Vector3(0f, 0f, ZonePosition) };

        root.AddChild(BuildRockMound());
        root.AddChild(BuildTunnelOpening());
        root.AddChild(BuildTimbers());
        root.AddChild(BuildBoulders());
        root.AddChild(BuildRailHint());
        root.AddChild(BuildGroundMist());

        // Optional accent: the generated minegate mesh, if present — smaller/scaled down and
        // tucked at the threshold so the BUILT mound stays the dominant, darker cave mouth per the
        // brief. Graceful-degrade to nothing when the asset is missing (never a crash).
        var accent = TownAssets.InstantiateGen("minegate.glb");
        if (accent is not null)
        {
            accent.Name = "GenAccent";
            accent.Position = new Vector3(0f, 0f, 2.6f);
            accent.Scale = new Vector3(0.7f, 0.7f, 0.7f);
            root.AddChild(accent);
        }

        // Gen'd 2026-07-26: distinctive mine-mouth dressing (a timber support, a glowing ore vein, a
        // rubble heap) spread to the sides of the tunnel bore, clear of its z≈0 axis — replaces the
        // blocky primitive read with real props. Additive; graceful-degrade when a GLB is missing.
        AddGenDressing(root, "mine-timber.glb", new Vector3(3.6f, 0f, 3.0f), 205f);
        AddGenDressing(root, "mine-ore-vein.glb", new Vector3(-3.8f, 0f, 4.2f), 30f);
        AddGenDressing(root, "mine-rubble.glb", new Vector3(2.4f, 0f, 5.4f), 120f);

        return root;
    }

    /// <summary>Places one normalized gen dressing GLB (feet-pivoted, pre-scaled) at
    /// <paramref name="position"/>. Null-guarded — a missing asset is silently skipped, never a crash.</summary>
    private static void AddGenDressing(Node3D root, string fileName, Vector3 position, float rotationYDeg)
    {
        var piece = TownAssets.InstantiateGen(fileName);
        if (piece is null)
        {
            return;
        }

        piece.Position = position;
        piece.RotationDegrees = new Vector3(0f, rotationYDeg, 0f);
        root.AddChild(piece);
    }

    /// <summary>A big dark, rough rock mass framing the tunnel bore — several oversized primitive
    /// chunks (boxes + a prism cap) offset and rotated so the mound reads as an irregular hillside
    /// rather than a single obvious box. Roughly 10-12 units wide, 6-8 tall, dominating the end of
    /// the road. Deliberately leaves a gap at the front-centre for <see cref="BuildTunnelOpening"/>
    /// to occupy.</summary>
    private static Node3D BuildRockMound()
    {
        var mound = new Node3D { Name = "RockMound" };
        var rock = new StandardMaterial3D { AlbedoColor = RockColor, Roughness = 1f };

        (string Name, Vector3 Size, Vector3 Pos, float RotYDeg, bool Prism)[] chunks =
        {
            ("ChunkLeft", new Vector3(4.6f, 6.2f, 6.4f), new Vector3(-4.6f, 3.0f, 0f), 8f, false),
            ("ChunkRight", new Vector3(4.6f, 6.0f, 6.4f), new Vector3(4.6f, 2.9f, 0f), -6f, false),
            ("ChunkBack", new Vector3(11f, 5.2f, 4.2f), new Vector3(0f, 2.5f, -3.2f), 0f, false),
            ("ChunkLeftUpper", new Vector3(3.2f, 3.0f, 4.2f), new Vector3(-3.4f, 6.1f, -1f), 15f, false),
            ("ChunkRightUpper", new Vector3(3.2f, 2.8f, 4.2f), new Vector3(3.4f, 5.9f, -1f), -12f, false),
            ("ChunkTop", new Vector3(9.4f, 3.0f, 7f), new Vector3(0f, 7.2f, -0.5f), 0f, true),
        };

        foreach (var chunk in chunks)
        {
            var mesh = chunk.Prism
                ? new PrismMesh { Size = chunk.Size, Material = rock }
                : (Mesh)new BoxMesh { Size = chunk.Size, Material = rock };

            mound.AddChild(new MeshInstance3D
            {
                Name = chunk.Name,
                Position = chunk.Pos,
                RotationDegrees = new Vector3(0f, chunk.RotYDeg, 0f),
                Mesh = mesh,
            });
        }

        return mound;
    }

    /// <summary>The recessed black tunnel: a dark, unlit (non-emissive) cylinder bored into the
    /// mound facing +Z (toward the village), plus a faint sickly glow seated deep inside so the
    /// blackness isn't perfectly flat — a small emissive orb and a matching low-energy cold
    /// <see cref="OmniLight3D"/>, cyan/green per the mood brief.</summary>
    private static Node3D BuildTunnelOpening()
    {
        var opening = new Node3D { Name = "TunnelOpening" };

        opening.AddChild(new MeshInstance3D
        {
            Name = "Mouth",
            Position = new Vector3(0f, 2.2f, 0f),
            RotationDegrees = new Vector3(90f, 0f, 0f), // cylinder axis along Z, facing the village
            Mesh = new CylinderMesh
            {
                TopRadius = 2.4f, BottomRadius = 2.6f, Height = 7f,
                Material = new StandardMaterial3D { AlbedoColor = VoidColor, Roughness = 1f },
            },
        });

        var glowMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.3f, 0.5f, 0.5f),
            EmissionEnabled = true,
            Emission = new Color(0.3f, 0.5f, 0.5f),
            EmissionEnergyMultiplier = 1.4f,
        };
        opening.AddChild(new MeshInstance3D
        {
            Name = "DepthGlow",
            Position = new Vector3(0f, 1.9f, -3.2f), // deep in the throat, away from the mouth
            Mesh = new SphereMesh { Radius = 0.5f, Height = 1f, Material = glowMat },
        });

        opening.AddChild(new OmniLight3D
        {
            Name = "DepthLight",
            Position = new Vector3(0f, 1.9f, -3.2f),
            LightColor = new Color(0.3f, 0.5f, 0.5f),
            LightEnergy = 0.6f,
            OmniRange = 4f,
            OmniAttenuation = 1.8f,
        });

        return opening;
    }

    /// <summary>Two dark vertical support beams + a lintel spanning them at the mouth's threshold —
    /// the mineshaft-entrance framing that reads as "built", not just natural rock.</summary>
    private static Node3D BuildTimbers()
    {
        var timbers = new Node3D { Name = "Timbers" };
        var wood = new StandardMaterial3D { AlbedoColor = TimberColor, Roughness = 1f };

        foreach (var x in new[] { -3.2f, 3.2f })
        {
            timbers.AddChild(new MeshInstance3D
            {
                Name = x < 0 ? "TimberLeft" : "TimberRight",
                Position = new Vector3(x, 2.5f, 3.2f),
                Mesh = new CylinderMesh { TopRadius = 0.32f, BottomRadius = 0.4f, Height = 5f, Material = wood },
            });
        }

        timbers.AddChild(new MeshInstance3D
        {
            Name = "Lintel",
            Position = new Vector3(0f, 5.1f, 3.2f),
            Mesh = new BoxMesh { Size = new Vector3(7.2f, 0.6f, 0.7f), Material = wood },
        });

        return timbers;
    }

    /// <summary>A handful of scattered dark boulders around the mouth's apron — deterministic
    /// hand-placed positions, no RNG.</summary>
    private static Node3D BuildBoulders()
    {
        var boulders = new Node3D { Name = "Boulders" };
        var rock = new StandardMaterial3D { AlbedoColor = RockColor, Roughness = 1f };

        (string Name, Vector3 Pos, float Radius)[] spots =
        {
            ("Boulder0", new Vector3(-5.6f, 0.45f, 5.2f), 0.6f),
            ("Boulder1", new Vector3(5.9f, 0.4f, 4.6f), 0.55f),
            ("Boulder2", new Vector3(-2.1f, 0.35f, 6.8f), 0.45f),
        };

        foreach (var spot in spots)
        {
            boulders.AddChild(new MeshInstance3D
            {
                Name = spot.Name,
                Position = spot.Pos,
                Mesh = new SphereMesh { Radius = spot.Radius, Height = spot.Radius * 2f, Material = rock },
            });
        }

        return boulders;
    }

    /// <summary>Minecart-rail hint: two thin dark strips running out from the mouth along +Z, back
    /// toward the village road — just enough geometry to suggest a rail line without a real cart.</summary>
    private static Node3D BuildRailHint()
    {
        var rails = new Node3D { Name = "RailHint" };
        var metal = new StandardMaterial3D { AlbedoColor = RailColor, Metallic = 0.6f, Roughness = 0.5f };

        foreach (var x in new[] { -0.9f, 0.9f })
        {
            rails.AddChild(new MeshInstance3D
            {
                Name = x < 0 ? "RailLeft" : "RailRight",
                Position = new Vector3(x, 0.05f, 8f),
                Mesh = new BoxMesh { Size = new Vector3(0.16f, 0.06f, 14f), Material = metal },
            });
        }

        return rails;
    }

    /// <summary>Low ground mist at the mouth's apron: a wide, very-translucent flat quad (unshaded,
    /// alpha-blended) — cheap menace without particles.</summary>
    private static Node3D BuildGroundMist()
    {
        return new MeshInstance3D
        {
            Name = "GroundMist",
            Position = new Vector3(0f, 0.18f, 4f),
            Mesh = new PlaneMesh
            {
                Size = new Vector2(14f, 10f),
                Material = new StandardMaterial3D
                {
                    AlbedoColor = new Color(0.55f, 0.58f, 0.62f, 0.16f),
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                },
            },
        };
    }
}
