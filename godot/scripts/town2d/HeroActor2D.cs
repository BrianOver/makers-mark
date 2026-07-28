using System;
using Godot;

namespace GodotClient.Town2d;

/// <summary>
/// U4: one alive hero's 2D town marker — a <see cref="Node2D"/> ground-anchored at
/// <see cref="Position"/> (feet baseline, for the <c>YSort</c> node it lives under), with a
/// real-click <see cref="Area2D"/> "Pick" zone (<c>CollisionLayer</c> 2, <c>InputPickable</c>)
/// plus <see cref="RaisePick"/>, a test seam raising the same event a real click would.
///
/// <para>This is <c>GodotClient.Town3d.HeroActor3D</c> PORTED BACK to 2D — that type is itself
/// documented as a Vector2→Vector3 port of an older 2D actor, so this is a mechanical reversal:
/// Vector3 becomes <see cref="Vector2"/>, the ground X/Z plane becomes X/Y, and the yaw-facing
/// mesh rotation becomes a <see cref="Sprite2D.FlipH"/> flip. The state machine —
/// Wandering/Rallying/WalkingOut/Away/WalkingIn, the lissajous wander drift, the
/// distance/<see cref="WalkSpeed"/> step-toward-target motion — is the 3D file's logic verbatim,
/// minus the vertical axis. Unlike <c>HeroActor3D</c> (driven by <c>Town3D</c> calling
/// <c>Advance(delta)</c> every frame), all timing here lives in <see cref="_Process"/> per the
/// 2.5D plan's rule that tween/animation timing stays off the sim path — nothing here reads the
/// sim clock or wall-clock, so it is still a pure function of accumulated per-frame delta
/// (KTD2/KTD4/KTD5: presentation-only, deterministic, zero sim coupling).</para>
///
/// <para>Public surface intentionally differs in shape from <c>HeroActor3D</c>'s
/// (<c>Init</c>/<c>SetState</c>/<c>RallyTo</c>/<c>MarchOutTo</c>/<c>ReturnTo</c> replace
/// <c>Configure</c>/<c>BeginDeparture</c>/<c>BeginReturn</c>/<c>SetAway</c>/<c>SnapHome</c>) to
/// match the exact contract <c>Town2D</c> (U1) is built against — the party-file rally-dwell
/// stagger that lived inside <c>HeroActor3D.BeginDeparture</c> is expected to live in
/// <c>Town2D</c>'s own choreography instead (it calls <see cref="RallyTo"/> then, after its own
/// dwell, <see cref="MarchOutTo"/>), since this type's <see cref="RallyTo"/> has no fileDelay
/// parameter to carry it.</para>
/// </summary>
public partial class HeroActor2D : Node2D
{
    public enum HeroTownState
    {
        Wandering,
        Rallying,
        WalkingOut,
        Away,
        WalkingIn,
    }

    /// <summary>Walk speed in px/sec — the actual 2D-original value; <c>HeroActor3D.WalkSpeed</c>'s
    /// own doc comment notes its 2.6 units/sec 3D figure "has no meaningful 3D equivalent" to
    /// this 260px/sec 2D figure, so this port restores the original number rather than rescaling
    /// the 3D one.</summary>
    public const float WalkSpeed = 260f;

    /// <summary>Wander-drift amplitude in px (2D-scale decoration tuning knob — the 3D port's
    /// 1.4/1.0 world-unit amplitudes have no fixed pixel equivalent; picked to read as a gentle
    /// idle bob at 16px-tile scale).</summary>
    private const float WanderAmplitudeX = 14f;

    private const float WanderAmplitudeY = 10f;

    /// <summary>Half this = the <see cref="Sprite2D.Offset"/> lift, so <see cref="Position"/> is
    /// the sprite's FEET line — matches the vertical-slice manifest's 16×24 hero sprite and is
    /// the Y-sort contract every <c>YSortEnabled</c> sibling (buildings, player) must share.</summary>
    private const float HeroSpriteHeight = 24f;

    public int HeroIdValue { get; private set; }

    public string ClassId { get; private set; } = string.Empty;

    public HeroTownState State { get; private set; } = HeroTownState.Wandering;

    /// <summary>Anchor point the wander drifts around; deterministic per hero id (set by
    /// <see cref="Init"/>, resumed by <see cref="SetState"/>'s Wandering case).</summary>
    public Vector2 Home { get; private set; }

    public Sprite2D Sprite { get; private set; } = null!;

    public Area2D Pick { get; private set; } = null!;

    /// <summary>Raised by <see cref="RaisePick"/> (test seam) or a real click on <see
    /// cref="Pick"/> — <c>Town2D</c> forwards this into its own <c>HeroClicked</c> event,
    /// unchanged (KTD2: presentation-only).</summary>
    public event Action<int>? Picked;

    private double _townTime;
    private float _phaseX;
    private float _phaseY;
    private float _speedX;
    private float _speedY;

    private Vector2 _logicalPosition;
    private Vector2 _walkTarget;

    /// <summary>
    /// Build the sprite + pick zone and pin the deterministic wander parameters. Mirrors
    /// <c>HeroActor3D.Configure</c> — <paramref name="spawn"/> becomes both <see cref="Home"/>
    /// and the initial <see cref="Position"/>.
    /// </summary>
    public void Init(int heroId, string classId, Color classColor, Texture2D sprite, Vector2 spawn)
    {
        HeroIdValue = heroId;
        ClassId = classId;
        Home = spawn;
        Name = $"Hero_{heroId}";
        Position = spawn;
        _logicalPosition = spawn;
        _walkTarget = spawn;

        // Deterministic per-hero drift parameters — id in, motion out, no RNG (HeroActor3D's own
        // formula, verbatim).
        _phaseX = heroId * 1.7f;
        _phaseY = heroId * 2.9f;
        _speedX = 0.55f + heroId % 3 * 0.2f;
        _speedY = 0.4f + heroId % 4 * 0.15f;

        Sprite = BuildSprite(sprite, classColor);
        AddChild(Sprite);

        Pick = BuildPick();
        AddChild(Pick);

        State = HeroTownState.Wandering;
        Visible = true;
    }

    /// <summary>Test seam raising the same event a real <see cref="Pick"/> click would.</summary>
    public void RaisePick() => Picked?.Invoke(HeroIdValue);

    /// <summary>
    /// Blunt direct state assignment — used for the Evening/new-day "snap home" and Away
    /// transitions that don't need a travelled path (<c>HeroActor3D.SnapHome</c>/<c>SetAway</c>
    /// collapsed into one setter here). Setting to Wandering snaps to <see cref="Home"/> and
    /// shows; setting to Away hides. Setting directly into an in-transit state (Rallying/
    /// WalkingOut/WalkingIn) without a travel target holds the actor in place until <see
    /// cref="RallyTo"/>/<see cref="MarchOutTo"/>/<see cref="ReturnTo"/> supplies one.
    /// </summary>
    public void SetState(HeroTownState s)
    {
        State = s;
        switch (s)
        {
            case HeroTownState.Wandering:
                _logicalPosition = Home;
                Position = Home;
                _walkTarget = Home;
                Visible = true;
                break;
            case HeroTownState.Away:
                Visible = false;
                _walkTarget = _logicalPosition;
                break;
            default:
                Visible = true;
                _walkTarget = _logicalPosition;
                break;
        }
    }

    /// <summary>Expedition departs: walk to the rally point near the gate and idle there —
    /// <c>Town2D</c> is expected to call <see cref="MarchOutTo"/> once its own dwell/file-stagger
    /// timer elapses (see class doc: the fileDelay cascade moved out of this type).</summary>
    public void RallyTo(Vector2 target)
    {
        _walkTarget = target;
        State = HeroTownState.Rallying;
    }

    /// <summary>Walk to the mine door; arriving sets <see cref="HeroTownState.Away"/> and hides
    /// (despawn-ready — mirrors <c>HeroActor3D.AdvanceWalkingOut</c>/<c>SetAway</c>).</summary>
    public void MarchOutTo(Vector2 mineDoor)
    {
        _walkTarget = mineDoor;
        State = HeroTownState.WalkingOut;
    }

    /// <summary>Survivor re-enters at <paramref name="townPoint"/> (the gate edge) and walks home
    /// — arriving resumes <see cref="HeroTownState.Wandering"/> (mirrors
    /// <c>HeroActor3D.BeginReturn</c> + <c>AdvanceWalkingIn</c>).</summary>
    public void ReturnTo(Vector2 townPoint)
    {
        _logicalPosition = townPoint;
        Position = townPoint;
        Visible = true;
        _walkTarget = Home;
        State = HeroTownState.WalkingIn;
    }

    /// <summary>
    /// Per-frame state advance — off the sim path (2.5D plan rule), pure function of accumulated
    /// delta (no RNG, no wall-clock, KTD2/KTD4): same id/home plus the same delta sequence always
    /// lands at the same <see cref="Position"/>.
    /// </summary>
    public override void _Process(double delta)
    {
        _townTime += delta;

        var basePos = State switch
        {
            HeroTownState.Wandering => WanderingBasePosition(),
            HeroTownState.Rallying => AdvanceRallying(delta),
            HeroTownState.WalkingOut => AdvanceWalkingOut(delta),
            HeroTownState.WalkingIn => AdvanceWalkingIn(delta),
            _ => _logicalPosition, // Away: frozen, invisible anyway
        };

        Face(basePos - Position);
        Position = basePos;
    }

    /// <summary>Deterministic lissajous drift for the current accumulated time (pure function of
    /// id + t, no RNG) — <c>HeroActor3D.WanderingBasePosition</c>, X/Z ground axes replaced by
    /// X/Y screen axes.</summary>
    private Vector2 WanderingBasePosition()
    {
        _logicalPosition = Home + new Vector2(
            WanderAmplitudeX * Mathf.Sin((float)(_townTime * _speedX) + _phaseX),
            WanderAmplitudeY * Mathf.Sin((float)(_townTime * _speedY) + _phaseY));
        return _logicalPosition;
    }

    /// <summary>Walk to the rally target then idle there (dwell/peel-off staggering lives in
    /// <c>Town2D</c> now — see class doc) — mirrors the travel half of
    /// <c>HeroActor3D.AdvanceRallying</c>.</summary>
    private Vector2 AdvanceRallying(double delta)
    {
        StepToward(_walkTarget, delta, out _);
        return _logicalPosition;
    }

    private Vector2 AdvanceWalkingOut(double delta)
    {
        StepToward(_walkTarget, delta, out var arrived);
        if (arrived)
        {
            State = HeroTownState.Away;
            Visible = false;
        }

        return _logicalPosition;
    }

    private Vector2 AdvanceWalkingIn(double delta)
    {
        StepToward(_walkTarget, delta, out var arrived);
        if (arrived)
        {
            State = HeroTownState.Wandering;
        }

        return _logicalPosition;
    }

    /// <summary>
    /// Move <see cref="_logicalPosition"/> toward <paramref name="target"/> at <see
    /// cref="WalkSpeed"/>, consuming only the slice of <paramref name="delta"/> the remaining
    /// distance needs; <paramref name="arrived"/> is true once there (<c>HeroActor3D.StepToward</c>
    /// verbatim, Vector3 → Vector2).
    /// </summary>
    private double StepToward(Vector2 target, double delta, out bool arrived)
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

    /// <summary>Flips the sprite to face the travel direction — the 2D analog of
    /// <c>HeroActor3D.Face</c>'s yaw-lerp (a binary flip has no meaningful lerp); a no-op with
    /// near-zero horizontal movement so the sprite keeps facing whichever way it last moved.</summary>
    private void Face(Vector2 moved)
    {
        if (Mathf.Abs(moved.X) < 0.01f)
        {
            return;
        }

        Sprite.FlipH = moved.X < 0f;
    }

    /// <summary>Class-colored sprite (modulate = classColor, mirroring
    /// <c>HeroActor3D</c>'s capsule-tint fallback contract) — <paramref name="offset"/>s its
    /// origin up by half its height so <see cref="Position"/> stays the feet/Y-sort line.</summary>
    private static Sprite2D BuildSprite(Texture2D sprite, Color classColor) => new()
    {
        Name = "Sprite",
        Texture = sprite,
        Modulate = classColor,
        Offset = new Vector2(0, -HeroSpriteHeight / 2f),
    };

    /// <summary>Real-click pick zone — layer 2 (mirrors <c>HeroActor3D.BuildPick</c>'s layer
    /// convention), <c>InputPickable</c> so a real click on the sprite delivers "input_event"
    /// here; <see cref="RaisePick"/> is the seam tests use instead of simulating OS input.</summary>
    private Area2D BuildPick()
    {
        var area = new Area2D
        {
            Name = "Pick",
            CollisionLayer = 2,
            CollisionMask = 0,
            Monitoring = false,
            Monitorable = false,
            InputPickable = true,
        };
        area.AddChild(new CollisionShape2D
        {
            Name = "PickShape",
            Shape = new CircleShape2D { Radius = HeroSpriteHeight / 2f },
            Position = new Vector2(0, -HeroSpriteHeight / 2f),
        });
        area.InputEvent += (Node _, InputEvent @event, long _) =>
        {
            if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
            {
                RaisePick();
            }
        };
        return area;
    }
}
