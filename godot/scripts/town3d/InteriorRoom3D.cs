using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace GodotClient.Town3d;

/// <summary>
/// Real 3D venue interiors (3D-interiors MVP) — a small open-front diorama room built per venue
/// key: a warm-material floor, three primitive walls (open toward the camera, no ceiling, so the
/// fixed <see cref="CameraRig"/> pitch of ~-42° reads the whole room without the near wall
/// clipping the lens), a warm interior light, and the venue-appropriate AI-gen props
/// (<see cref="TownAssets.InstantiateGen"/>, rescaled from each prop's OWN mesh bounds via
/// <see cref="Town3D.MeshHeight"/> — the exact <c>Town3D.AddGenProp</c> pattern).
///
/// <para><b>How it mounts:</b> <c>MainUi.OpenInterior</c> parents one of these into the live
/// town's <see cref="Town3D.World"/> at <see cref="MountPosition"/> (a shelf high above the
/// town, clear of the ground/navmesh/every building) and dollies the shared
/// <see cref="CameraRig"/> onto <see cref="Focus"/> — the same <see cref="CameraRig.PushIn"/>
/// path the forge/counter stations proved. The 2D <see cref="GodotClient.Town.InteriorStage"/>
/// stays mounted on top in see-through mode as the hotspot/exit/Esc overlay: the painted
/// backdrop is what this class replaces, the declarative hotspot routing is deliberately
/// carried forward unchanged (its class doc named that data "the carry-forward asset if
/// walkable interiors happen later" — this is that later).</para>
///
/// <para><b>Deterministic + headless-safe (KTD2/KTD4):</b> every position/scale is a fixed
/// literal, no RNG, no clock; prop sizing is a pure resource read (<c>Mesh.GetAabb</c>), never a
/// render. A missing gen GLB degrades to a tinted primitive block under the SAME node name
/// ("Prop_&lt;file&gt;"), so the room never has a hole and tests never depend on art presence
/// (the <c>InteriorStage.ApplyBackdrop</c> graceful-degrade precedent).</para>
/// </summary>
public partial class InteriorRoom3D : Node3D
{
    /// <summary>One dressed prop inside a venue's room: its gen GLB file, floor position, target
    /// height (rescaled from the prop's own mesh bounds), and yaw.</summary>
    private readonly record struct PropSpec(string File, Vector3 Position, float TargetHeight, float RotationYDeg);

    /// <summary>Where <c>MainUi</c> mounts the room inside <see cref="Town3D.World"/> — a shelf
    /// 60 units above the town center, far above the 60x60 ground plane so the room can never
    /// intersect a building, the navmesh bake (rooms mount AFTER the bake anyway), or a wandering
    /// hero. The camera's exponential ease glides up to it on entry and back down on exit.</summary>
    public static readonly Vector3 MountPosition = new(0f, 60f, 0f);

    /// <summary>Room shell dimensions — an 8x8 floor with 3-unit walls frames comfortably at the
    /// rig's 45° FOV from <c>MainUi.InteriorRoomPushInDistance</c>.</summary>
    private const float RoomSize = 8f;
    private const float WallHeight = 3f;
    private const float WallThickness = 0.3f;

    /// <summary>
    /// The declarative room table (KTD10 — a fresh venue interior is a table row, never a new
    /// code path), keyed by the SAME venue keys <see cref="GodotClient.Town.InteriorStage.Venues"/>
    /// and <see cref="Town3D.DoorAnchor"/> use. Prop positions sit on the floor (y=0), inside the
    /// walls (|x|,|z| &lt; 4), clear of the camera's sight line to the room center.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, PropSpec[]> Rooms = new Dictionary<string, PropSpec[]>
    {
        ["forge"] =
        [
            new PropSpec("anvil.glb", new Vector3(0.4f, 0f, -1.0f), 0.9f, 15f),
            new PropSpec("barrel.glb", new Vector3(-2.4f, 0f, -2.6f), 1.0f, 0f),
            new PropSpec("weapon-rack.glb", new Vector3(2.6f, 0f, -2.7f), 1.7f, -35f),
            new PropSpec("brazier.glb", new Vector3(-2.5f, 0f, -0.9f), 1.0f, 0f),
            new PropSpec("wall-sconce.glb", new Vector3(2.9f, 0f, -1.6f), 0.6f, -90f),
        ],
        ["market"] =
        [
            new PropSpec("market-stall.glb", new Vector3(0f, 0f, -2.0f), 2.4f, 0f),
            new PropSpec("barrel.glb", new Vector3(2.4f, 0f, -2.4f), 1.0f, 30f),
            new PropSpec("crate.glb", new Vector3(-2.5f, 0f, -1.7f), 1.1f, 20f),
            new PropSpec("bookshelf.glb", new Vector3(2.7f, 0f, -2.8f), 2.0f, -30f),
            new PropSpec("potion-shelf.glb", new Vector3(-2.8f, 0f, -2.9f), 1.6f, 20f),
            new PropSpec("apple-barrel.glb", new Vector3(1.4f, 0f, -1.2f), 0.9f, 0f),
        ],
        ["tavern"] =
        [
            new PropSpec("barrel.glb", new Vector3(-2.4f, 0f, -2.2f), 1.0f, 0f),
            new PropSpec("well.glb", new Vector3(1.8f, 0f, -1.6f), 1.6f, -20f),
            new PropSpec("cauldron.glb", new Vector3(2.6f, 0f, -2.8f), 0.9f, 0f),
            new PropSpec("table.glb", new Vector3(-1.4f, 0f, -0.9f), 0.8f, 0f),
            new PropSpec("stool.glb", new Vector3(-0.3f, 0f, -1.3f), 0.6f, 0f),
            new PropSpec("chair.glb", new Vector3(-2.3f, 0f, -1.0f), 0.9f, 90f),
            new PropSpec("bed.glb", new Vector3(2.4f, 0f, -2.4f), 0.7f, -20f),
        ],
        ["minegate"] =
        [
            new PropSpec("ore-cart.glb", new Vector3(0f, 0f, -1.4f), 1.3f, 25f),
            new PropSpec("bounty-board.glb", new Vector3(-2.2f, 0f, -3.0f), 1.8f, 10f),
            new PropSpec("chest.glb", new Vector3(2.3f, 0f, -1.7f), 0.8f, -25f),
            new PropSpec("wall-banner.glb", new Vector3(0.2f, 0f, -3.1f), 1.4f, 0f),
        ],
    };

    /// <summary>Per-venue wall tint — a cheap read of each venue's identity (stone forge, tan
    /// shop, timber tavern, rock gate) without needing interior wall art.</summary>
    private static readonly IReadOnlyDictionary<string, Color> WallTints = new Dictionary<string, Color>
    {
        ["forge"] = new(0.45f, 0.42f, 0.42f),
        ["market"] = new(0.66f, 0.56f, 0.42f),
        ["tavern"] = new(0.52f, 0.38f, 0.24f),
        ["minegate"] = new(0.38f, 0.36f, 0.35f),
    };

    private static readonly Color FloorColor = new(0.36f, 0.26f, 0.18f);

    /// <summary>The venue keys this class can build a room for (test/inspection surface) — must
    /// stay in lockstep with <see cref="GodotClient.Town.InteriorStage.Venues"/>, pinned by
    /// <c>InteriorRoom3DTests</c>.</summary>
    public static IReadOnlyCollection<string> VenueKeys => Rooms.Keys.ToArray();

    /// <summary>The gen GLB files dressed into <paramref name="venueKey"/>'s room, in declared
    /// order (test/inspection surface).</summary>
    public static string[] PropFiles(string venueKey) => Rooms[venueKey].Select(p => p.File).ToArray();

    /// <summary>The built room's venue key ("" before <see cref="Build"/>).</summary>
    public string VenueKey { get; private set; } = "";

    /// <summary>The camera's dolly target — room center at standing-prop height, so the
    /// <see cref="CameraRig.PushIn"/> frame centers the dressed floor, not the wall tops.</summary>
    public Node3D Focus { get; private set; } = null!;

    private bool _built;

    /// <summary>
    /// Build <paramref name="venueKey"/>'s room (must be a key in <see cref="Rooms"/> — throws
    /// the same way <see cref="GodotClient.Town.InteriorStage.Open"/> does for an unknown venue).
    /// One room instance builds once; <c>MainUi</c> mounts a fresh instance per entry.
    /// </summary>
    public void Build(string venueKey)
    {
        if (_built)
        {
            return;
        }

        if (!Rooms.TryGetValue(venueKey, out var props))
        {
            throw new ArgumentOutOfRangeException(nameof(venueKey), venueKey, "no such interior room venue");
        }

        _built = true;
        VenueKey = venueKey;
        Name = "InteriorRoom3D";

        var wallTint = WallTints[venueKey];
        AddChild(Box("Floor", new Vector3(RoomSize + 2 * WallThickness, WallThickness, RoomSize + 2 * WallThickness),
            new Vector3(0f, -WallThickness / 2f, 0f), FloorColor));
        AddChild(Box("WallBack", new Vector3(RoomSize + 2 * WallThickness, WallHeight, WallThickness),
            new Vector3(0f, WallHeight / 2f, -(RoomSize + WallThickness) / 2f), wallTint));
        AddChild(Box("WallLeft", new Vector3(WallThickness, WallHeight, RoomSize + 2 * WallThickness),
            new Vector3(-(RoomSize + WallThickness) / 2f, WallHeight / 2f, 0f), wallTint));
        AddChild(Box("WallRight", new Vector3(WallThickness, WallHeight, RoomSize + 2 * WallThickness),
            new Vector3((RoomSize + WallThickness) / 2f, WallHeight / 2f, 0f), wallTint));

        // Warm interior key light — the room is open-topped, so the town sun contributes fill,
        // but this is what makes the inside read "lit hearth" rather than "gray box".
        AddChild(new OmniLight3D
        {
            Name = "RoomLight",
            Position = new Vector3(0f, WallHeight - 0.6f, 0f),
            LightColor = new Color(1f, 0.82f, 0.55f),
            LightEnergy = 2.3f,
            OmniRange = RoomSize * 1.6f,
        });

        foreach (var prop in props)
        {
            AddChild(BuildProp(prop));
        }

        Focus = new Node3D { Name = "Focus", Position = new Vector3(0f, 1.2f, -1.2f) };
        AddChild(Focus);
    }

    /// <summary>A shell piece — tint on the <see cref="PrimitiveMesh"/>'s own material, never
    /// <see cref="GeometryInstance3D.MaterialOverride"/> (the wedge-fallback bug
    /// <c>Town3D.BuildStationMesh</c>'s doc records).</summary>
    private static MeshInstance3D Box(string name, Vector3 size, Vector3 position, Color color) => new()
    {
        Name = name,
        Mesh = new BoxMesh { Size = size, Material = new StandardMaterial3D { AlbedoColor = color } },
        Position = position,
    };

    /// <summary>The gen prop, rescaled from its own mesh AABB to the spec's target height (the
    /// <c>Town3D.AddGenProp</c> pattern via the shared <see cref="Town3D.MeshHeight"/>); a missing
    /// GLB degrades to a tinted primitive block under the SAME "Prop_&lt;file&gt;" node name so
    /// the room (and any test asserting the room's contents) never depends on art presence.</summary>
    private static Node3D BuildProp(PropSpec spec)
    {
        var name = "Prop_" + System.IO.Path.GetFileNameWithoutExtension(spec.File);
        var piece = TownAssets.InstantiateGen(spec.File);
        if (piece is null)
        {
            piece = new MeshInstance3D
            {
                Mesh = new BoxMesh
                {
                    Size = new Vector3(spec.TargetHeight * 0.8f, spec.TargetHeight, spec.TargetHeight * 0.8f),
                    Material = new StandardMaterial3D { AlbedoColor = new Color(0.5f, 0.42f, 0.32f) },
                },
                Position = new Vector3(0f, spec.TargetHeight / 2f, 0f),
            };
            var wrapper = new Node3D { Name = name, Position = spec.Position, RotationDegrees = new Vector3(0f, spec.RotationYDeg, 0f) };
            wrapper.AddChild(piece);
            return wrapper;
        }

        var height = Town3D.MeshHeight(piece, 1f);
        var scale = height > 0.001f ? spec.TargetHeight / height : 1f;
        piece.Name = name;
        piece.Scale = new Vector3(scale, scale, scale);
        piece.Position = spec.Position;
        piece.RotationDegrees = new Vector3(0f, spec.RotationYDeg, 0f);
        return piece;
    }
}
