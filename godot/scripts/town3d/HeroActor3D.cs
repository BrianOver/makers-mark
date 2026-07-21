using System;
using Godot;
using GodotClient.Ui;

namespace GodotClient.Town3d;

/// <summary>
/// T7: one alive hero's 3D town marker — a <see cref="Node3D"/>, ground-anchored at
/// <see cref="Position"/> (the state machine's own coordinate, KTD6, same convention as
/// <c>Building3D</c>'s footprint), with a real-click <see cref="Area3D"/> "Pick" zone
/// (<c>CollisionLayer</c> 2, <c>InputRayPickable</c>) plus <see cref="RaisePick"/>, a test seam
/// raising the same event a real click would — headless 3D physics picking is unproven (T3/T5
/// precedent: <c>WorldInput3D</c>'s own camera-ray click resolution is manual-smoke-only), so
/// tests drive through the seam instead.
///
/// <para>The motion state machine below is <c>GodotClient.Town.HeroActor</c>'s LW1 state machine
/// PORTED to 3D (X/Z ground plane replaces X/Y screen plane; <see cref="Vector2"/> becomes
/// <see cref="Vector3"/>) — Wandering/Rallying/WalkingOut/Away/WalkingIn, <see cref="StepToward"/>,
/// the rally-dwell-then-peel-off cascade: every formula is the 2D original with the vertical
/// axis dropped in favor of Z. <see cref="Advance"/> is a pure function of accumulated delta (no
/// RNG, no wall-clock, KTD2/KTD4) — same seed/home plus the same delta sequence always lands at
/// the same <see cref="Node3D.GlobalPosition"/>.</para>
///
/// <para>Visuals: the real Kenney "mini-characters" GLB for <paramref name="variant"/>-select via
/// <see cref="TownAssets.HeroScene"/> when it resolves (T1 committed all 12 variants), else a
/// primitive capsule tinted <see cref="ClassColors.RoleColor"/> (moved verbatim off
/// <c>HeroActor.RoleColor</c> in this same task) so a missing/renamed asset degrades gracefully,
/// same contract as <c>Building3D</c>'s wedge fallback.</para>
/// </summary>
public partial class HeroActor3D : Node3D
{
    public enum ActorState
    {
        Wandering,
        Rallying,
        WalkingOut,
        Away,
        WalkingIn,
    }

    /// <summary>Gate-walk speed in world units/sec (3D-scale decoration tuning knob — the 2D
    /// original's 260px/sec has no meaningful 3D equivalent).</summary>
    public const float WalkSpeed = 2.6f;

    /// <summary>How long a party dwells at the rally point before the first hero peels off
    /// toward the gate (LW1 rally-and-depart, ported verbatim).</summary>
    public const float RallyDwellSeconds = 1.0f;

    private const float WanderAmplitudeX = 1.4f;
    private const float WanderAmplitudeZ = 1.0f;

    private static readonly Color FallbackTint = new(0.85f, 0.78f, 0.55f);

    /// <summary>Default walk-out/return target — just outside <c>Town3D</c>'s minegate door
    /// anchor (building at (0,0,-20), door anchor ~1.8 units in front of it). Settable per-actor
    /// so <c>Town3D</c> can re-point it if the layout ever moves without touching this type.
    /// </summary>
    private static readonly Vector3 DefaultGate = new(0f, 0f, -18.2f);

    public int HeroIdValue { get; private set; }

    public string HeroName { get; private set; } = string.Empty;

    public ActorState State { get; private set; } = ActorState.Wandering;

    /// <summary>Anchor point the wander drifts around; deterministic per hero id.</summary>
    public Vector3 Home { get; private set; }

    /// <summary>Walk-out/return target (see <see cref="DefaultGate"/>'s doc).</summary>
    public Vector3 Gate { get; set; } = DefaultGate;

    public Node3D Mesh { get; private set; } = null!;

    public Area3D Pick { get; private set; } = null!;

    public Label3D Label { get; private set; } = null!;

    /// <summary>Raised by <see cref="RaisePick"/> (test seam) or a real click on <see
    /// cref="Pick"/> — <c>Town3D.ReconcileHeroes</c> forwards this into its own
    /// <c>HeroClicked</c> event, unchanged (KTD2: presentation-only).</summary>
    public event Action<int>? Picked;

    private double _townTime;
    private float _phaseX;
    private float _phaseZ;
    private float _speedX;
    private float _speedZ;

    private Vector3 _logicalPosition;

    private Vector3 _rallyPoint;
    private float _fileDelay;
    private float _rallyElapsed;
    private bool _rallyDwelling;

    /// <summary>
    /// Build the visual + pick zone + name label and pin the deterministic wander parameters.
    /// <paramref name="classId"/> only matters for the primitive-capsule fallback tint (a real
    /// GLB carries its own color, same as <c>Building3D</c>); pass empty when unknown.
    /// </summary>
    public void Configure(int heroIdValue, string name, int variant, Vector3 home, string classId = "")
    {
        HeroIdValue = heroIdValue;
        HeroName = name;
        Home = home;
        Name = $"Hero_{heroIdValue}";
        Position = home;
        _logicalPosition = home;

        // Deterministic per-hero drift parameters — id in, motion out, no RNG (HeroActor's own
        // formula, verbatim).
        _phaseX = heroIdValue * 1.7f;
        _phaseZ = heroIdValue * 2.9f;
        _speedX = 0.55f + heroIdValue % 3 * 0.2f;
        _speedZ = 0.4f + heroIdValue % 4 * 0.15f;

        Mesh = BuildMesh(variant, classId);
        AddChild(Mesh);

        Pick = BuildPick();
        AddChild(Pick);

        Label = BuildLabel(name);
        AddChild(Label);
    }

    /// <summary>Test seam raising the same event a real <see cref="Pick"/> click would — headless
    /// 3D physics picking is unproven (T3/T5 precedent), so tests drive through here instead of
    /// simulating OS input.</summary>
    public void RaisePick() => Picked?.Invoke(HeroIdValue);

    /// <summary>
    /// Advance the state machine by <paramref name="delta"/> seconds, accumulated (never
    /// wall-clock) — pure function of (id, accumulated time, state script), so the same delta
    /// sequence from the same <see cref="Configure"/> call always lands at the same position.
    /// </summary>
    public void Advance(double delta)
    {
        _townTime += delta;

        var basePos = State switch
        {
            ActorState.Wandering => WanderingBasePosition(),
            ActorState.Rallying => AdvanceRallying(delta),
            ActorState.WalkingOut => AdvanceWalkingOut(delta),
            ActorState.WalkingIn => AdvanceWalkingIn(delta),
            _ => _logicalPosition, // Away: frozen, invisible anyway
        };

        Face(basePos - Position, delta);
        Position = basePos;
    }

    /// <summary>
    /// Expedition departs: rally near the gate first (spaced via <paramref name="rallyPoint"/>),
    /// dwell together, then peel off toward the real gate — <paramref name="fileDelaySeconds"/>
    /// staggers the peel-off so the party exits in file (LW1 rally-and-depart, ported verbatim).
    /// </summary>
    public void BeginDeparture(Vector3 rallyPoint, float fileDelaySeconds)
    {
        _rallyPoint = rallyPoint;
        _fileDelay = Mathf.Max(0f, fileDelaySeconds);
        _rallyElapsed = 0f;
        _rallyDwelling = false;
        State = ActorState.Rallying;
    }

    /// <summary>Survivor re-enters through the gate and walks home.</summary>
    public void BeginReturn()
    {
        _logicalPosition = Gate;
        Position = Gate;
        Visible = true;
        State = ActorState.WalkingIn;
    }

    /// <summary>In the Mine — not visible in town.</summary>
    public void SetAway()
    {
        Visible = false;
        State = ActorState.Away;
    }

    /// <summary>Safety snap: place the hero at home, wandering (new-day rollover).</summary>
    public void SnapHome()
    {
        _logicalPosition = Home;
        Position = Home;
        Visible = true;
        State = ActorState.Wandering;
    }

    /// <summary>Deterministic lissajous drift for the current accumulated time (pure function of
    /// id + t, no RNG) — the 2D original's <c>WanderOffset</c>, X/Y screen axes replaced by X/Z
    /// ground axes.</summary>
    private Vector3 WanderingBasePosition()
    {
        _logicalPosition = Home + new Vector3(
            WanderAmplitudeX * Mathf.Sin((float)(_townTime * _speedX) + _phaseX),
            0f,
            WanderAmplitudeZ * Mathf.Sin((float)(_townTime * _speedZ) + _phaseZ));
        return _logicalPosition;
    }

    /// <summary>
    /// Rally → dwell → peel-off, all inside one call when <paramref name="delta"/> is large
    /// enough (test fast-forward convention) — each sub-stage spends only the time it actually
    /// needs and hands the remainder to the next (2D original, ported verbatim).
    /// </summary>
    private Vector3 AdvanceRallying(double delta)
    {
        if (!_rallyDwelling)
        {
            var leftover = StepToward(_rallyPoint, delta, out var arrived);
            if (!arrived)
            {
                return _logicalPosition; // still travelling — this call's time is fully spent
            }

            _rallyDwelling = true;
            _rallyElapsed = 0f;
            delta = leftover;
        }

        var dwellTarget = RallyDwellSeconds + _fileDelay;
        if (_rallyElapsed < dwellTarget)
        {
            var remaining = dwellTarget - _rallyElapsed;
            if (delta < remaining)
            {
                _rallyElapsed += (float)delta;
                return _logicalPosition;
            }

            delta -= remaining;
            _rallyElapsed = dwellTarget;
        }

        State = ActorState.WalkingOut;
        return AdvanceWalkingOut(delta);
    }

    private Vector3 AdvanceWalkingOut(double delta)
    {
        StepToward(Gate, delta, out var arrived);
        if (arrived)
        {
            SetAway();
        }

        return _logicalPosition;
    }

    private Vector3 AdvanceWalkingIn(double delta)
    {
        StepToward(Home, delta, out var arrived);
        if (arrived)
        {
            State = ActorState.Wandering;
        }

        return _logicalPosition;
    }

    /// <summary>
    /// Move <see cref="_logicalPosition"/> toward <paramref name="target"/> at <see
    /// cref="WalkSpeed"/>, consuming only the slice of <paramref name="delta"/> the remaining
    /// distance needs; <paramref name="arrived"/> is true once there, and the return value is
    /// whatever of <paramref name="delta"/> was left unspent (0 mid-walk) — letting a rally's
    /// dwell/peel-off cascade spend it in the same call (2D original, ported verbatim).
    /// </summary>
    private double StepToward(Vector3 target, double delta, out bool arrived)
    {
        var distance = _logicalPosition.DistanceTo(target);
        var timeToArrive = distance / WalkSpeed;
        if (delta >= timeToArrive)
        {
            _logicalPosition = target;
            arrived = true;
            return delta - timeToArrive;
        }

        var step = WalkSpeed * (float)delta;
        _logicalPosition = _logicalPosition.MoveToward(target, step);
        arrived = false;
        return 0.0;
    }

    /// <summary>Turns <see cref="Mesh"/> toward the travel direction — a no-op with (near-)zero
    /// movement so the mesh keeps facing whichever way it last moved instead of snapping back to
    /// a default heading. Driven off the ACTUAL position delta (accumulated, never wall-clock),
    /// same determinism contract as the rest of this state machine.</summary>
    private void Face(Vector3 moved, double delta)
    {
        var flat = new Vector3(moved.X, 0, moved.Z);
        if (flat.LengthSquared() < 0.0001f)
        {
            return;
        }

        var targetYaw = Mathf.Atan2(-flat.X, -flat.Z);
        var rotation = Mesh.Rotation;
        rotation.Y = Mathf.LerpAngle(rotation.Y, targetYaw, (float)delta * 12f);
        Mesh.Rotation = rotation;
    }

    /// <summary>Instantiates the Kenney hero GLB for <paramref name="variant"/> when it resolves,
    /// else a tinted primitive capsule fallback (same degrade contract as <c>Building3D</c>'s
    /// wedge and <c>Town3D.SpawnCharacterMesh</c>).</summary>
    private static Node3D BuildMesh(int variant, string classId)
    {
        var scene = TownAssets.HeroScene(variant);
        if (scene != null)
        {
            var mesh = scene.Instantiate<Node3D>();
            mesh.Name = "Mesh";
            return mesh;
        }

        var tint = string.IsNullOrEmpty(classId) ? FallbackTint : ClassColors.RoleColor(classId);
        return new MeshInstance3D
        {
            Name = "Mesh",
            Mesh = new CapsuleMesh { Radius = 0.35f, Height = 1.6f },
            Position = new Vector3(0, 0.8f, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = tint },
        };
    }

    /// <summary>Real-click pick zone — layer 2 (mirrors <c>Building3D</c>'s own footprint/interact
    /// layer), <c>InputRayPickable</c> so the <c>SubViewport</c>'s physics-object-picking (T3,
    /// already ON in <c>Town3D.Build</c>) delivers "input_event" here on a real click. Headless
    /// picking is unproven (T3/T5 precedent) — <see cref="RaisePick"/> is the seam tests use
    /// instead.</summary>
    private Area3D BuildPick()
    {
        var area = new Area3D
        {
            Name = "Pick",
            CollisionLayer = 2,
            CollisionMask = 0,
            Monitoring = false,
            Monitorable = false,
            InputRayPickable = true,
        };
        area.AddChild(new CollisionShape3D
        {
            Name = "PickShape",
            Shape = new CapsuleShape3D { Radius = 0.45f, Height = 1.8f },
            Position = new Vector3(0, 0.9f, 0),
        });
        area.InputEvent += (_, @event, _, _, _) =>
        {
            if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
            {
                RaisePick();
            }
        };
        return area;
    }

    private static Label3D BuildLabel(string text) => new()
    {
        Name = "Label3D",
        Text = text,
        Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
        Position = new Vector3(0, 2.0f, 0),
        FontSize = 32,
        OutlineSize = 6,
    };
}
