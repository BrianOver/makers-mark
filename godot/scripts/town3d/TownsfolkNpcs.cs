using Godot;

namespace GodotClient.Town3d;

/// <summary>
/// Presentation-only non-hero villager NPCs decorating the market square — pure Godot-side
/// flavor with ZERO sim/determinism footprint (no <c>GameSim</c> reference anywhere in this
/// file, no <c>System.Random</c>; the per-NPC wander uses wall-clock <c>delta</c> straight off
/// <see cref="Node3D._Process"/>, which is fine here because these are decoration, not sim
/// state — see <c>HeroActor3D</c>'s doc for the contrast with the real, deterministic hero
/// state machine this deliberately does NOT reuse).
///
/// <para>Each <see cref="TownsfolkNpc"/> ambles between a small fixed loop of waypoints around
/// the square (origin-centered, radius roughly 11), pausing a beat at each stop before picking
/// the next — a simple 2-state machine (<c>Walking</c>/<c>Pausing</c>), varied per-NPC only by
/// an integer id (loop rotation, speed jitter, pause length) so motion reads as lively without
/// any randomness.</para>
/// </summary>
public static class TownsfolkNpcs
{
    /// <summary>Villager name + subtitle pairs — the "· townsfolk" hint plus a visibly cooler
    /// tint (vs. <c>HeroActor3D.FallbackTint</c>'s warm tan) are what read as NON-hero at a
    /// glance; heroes carry combat stats, these are flavor only.</summary>
    private static readonly (string Name, int Variant)[] Roster =
    {
        ("Marta the Baker", 0),
        ("Old Corwin", 6),
        ("Little Pib", 3),
        ("Bess the Weaver", 1),
        ("Fenn the Tanner", 8),
    };

    /// <summary>Distinct start spots around the square, clear of the exact centre where the
    /// player stands (~origin) and clear of the mine gate (far at z≈-26, out of this range
    /// entirely).</summary>
    private static readonly Vector3[] StartSpots =
    {
        new(6f, 0f, 3f),
        new(-5f, 0f, 6f),
        new(4f, 0f, -5f),
        new(-7f, 0f, -2f),
        new(2f, 0f, 8f),
    };

    /// <summary>Builds the "TownsfolkNpcs" group node with the full roster wandering the
    /// square. Never null, never touches the sim.</summary>
    public static Node3D Build()
    {
        var group = new Node3D { Name = "TownsfolkNpcs" };

        for (var i = 0; i < Roster.Length; i++)
        {
            var (name, variant) = Roster[i];
            var npc = new TownsfolkNpc();
            group.AddChild(npc);
            npc.Configure(i, name, variant, StartSpots[i % StartSpots.Length]);
        }

        return group;
    }
}

/// <summary>
/// One wandering, non-hero villager: a visual (Kenney mesh or capsule fallback) plus a
/// billboard <see cref="Label3D"/> name tag, easing between a handful of deterministic
/// waypoints around the square. Presentation-only — see <see cref="TownsfolkNpcs"/>'s class doc
/// for the determinism boundary this stays inside of.
/// </summary>
public partial class TownsfolkNpc : Node3D
{
    private enum WanderState
    {
        Walking,
        Pausing,
    }

    /// <summary>Wander speed in world units/sec — slower than a hero's gate-walk
    /// (<c>HeroActor3D.WalkSpeed</c> = 2.6) since this is idle village life, not travel.</summary>
    public const float WanderSpeed = 1.2f;

    /// <summary>A small fixed loop around the market square (roughly radius ~11, well inside the
    /// ±13 cobbled square and clear of the ±12-14 building line) — every NPC shares this loop but
    /// starts at a different index and pause length so they don't move in lockstep.</summary>
    private static readonly Vector3[] Waypoints =
    {
        new(7f, 0f, 5f),
        new(-6f, 0f, 7f),
        new(-8f, 0f, -3f),
        new(3f, 0f, -7f),
        new(9f, 0f, -1f),
        new(1f, 0f, 9f),
    };

    private static readonly Color FallbackTint = new(0.55f, 0.62f, 0.68f); // cool grey-blue — reads as "not a hero"

    public int NpcId { get; private set; }

    public string NpcName { get; private set; } = string.Empty;

    public Node3D Mesh { get; private set; } = null!;

    public Label3D Label { get; private set; } = null!;

    private WanderState _state = WanderState.Pausing;
    private int _waypointIndex;
    private float _pauseRemaining;

    /// <summary>Per-id pause length so NPCs don't all start/stop in sync — deterministic
    /// (id-derived), not random.</summary>
    private float _pauseSeconds;

    /// <summary>
    /// Builds the visual + name label and pins this NPC's starting waypoint index (derived from
    /// <paramref name="npcId"/>, so distinct NPCs take distinct routes through the same loop).
    /// </summary>
    public void Configure(int npcId, string name, int variant, Vector3 startPosition)
    {
        NpcId = npcId;
        NpcName = name;
        Name = $"Townsfolk_{npcId}";
        Position = startPosition;

        // id-derived, not random: spreads NPCs across the waypoint loop and varies pause timing.
        _waypointIndex = npcId % Waypoints.Length;
        _pauseSeconds = 0.8f + npcId % 3 * 0.6f;
        _pauseRemaining = _pauseSeconds;
        _state = WanderState.Pausing;

        Mesh = BuildMesh(variant);
        AddChild(Mesh);

        Label = BuildLabel(name);
        AddChild(Label);
    }

    /// <summary>Wall-clock <paramref name="delta"/> is fine here — this is Godot-side decoration,
    /// not sim state (see class doc). Eases toward the current waypoint; on arrival, pauses a
    /// beat, then advances to the next waypoint in the loop.</summary>
    public override void _Process(double delta)
    {
        base._Process(delta);

        if (_state == WanderState.Pausing)
        {
            _pauseRemaining -= (float)delta;
            if (_pauseRemaining <= 0f)
            {
                _state = WanderState.Walking;
            }

            return;
        }

        var target = Waypoints[_waypointIndex];
        var step = WanderSpeed * (float)delta;
        Position = Position.MoveToward(target, step);

        if (Position.DistanceTo(target) < 0.01f)
        {
            Position = target;
            _waypointIndex = (_waypointIndex + 1) % Waypoints.Length;
            _pauseRemaining = _pauseSeconds;
            _state = WanderState.Pausing;
        }
    }

    /// <summary>Kenney hero mesh reused for variety (townsfolk are just people too) when it
    /// resolves, else a tinted primitive capsule — same graceful-degrade contract as
    /// <c>HeroActor3D.BuildMesh</c>, but with a cooler fallback tint so a missing asset still
    /// reads as "not a hero".</summary>
    private static Node3D BuildMesh(int variant)
    {
        var mesh = TownAssets.InstantiateHero(variant);
        if (mesh != null)
        {
            mesh.Name = "Mesh";
            return mesh;
        }

        return new MeshInstance3D
        {
            Name = "Mesh",
            Mesh = new CapsuleMesh { Radius = 0.32f, Height = 1.5f },
            Position = new Vector3(0, 0.75f, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = FallbackTint },
        };
    }

    private static Label3D BuildLabel(string name) => new()
    {
        Name = "Label3D",
        Text = $"{name} · townsfolk",
        Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
        Position = new Vector3(0, 1.9f, 0),
        FontSize = 28,
        OutlineSize = 6,
        Modulate = new Color(0.85f, 0.9f, 0.95f),
    };
}
