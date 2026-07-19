using GameSim.Classes;
using GameSim.Contracts;
using Godot;

namespace GodotClient.Town;

/// <summary>
/// One alive hero's town marker: a hand-authored role figure (U16 SVG, tinted to the
/// role color via Modulate) over a small role-color footing, plus a name label.
/// Movement is a state machine driven by <see cref="TownScene"/> (never by its own
/// _Process): Wandering in town, Rallying at the gate before an expedition departs,
/// WalkingOut through the gate, Away while in the Mine, WalkingIn when a survivor
/// returns (or a fresh recruit walks in from off-screen). The wander/breath/walk motion
/// is a deterministic function of the hero id + accumulated town time — presentation
/// only, NO RNG, so it can never touch the sim's stream (KTD2/KTD4).
///
/// <para>LW1 (2026-07-19 living-world plan): every state now reads a "base" position
/// (<see cref="_logicalPosition"/> for the walking/rally states, the wander/anchor
/// formula for Wandering) and layers a small vertical bob on top for rendering only —
/// the base position is what state-machine distance checks and left/right facing key
/// off, so the bob can never desync arrival detection or cause facing jitter.</para>
/// </summary>
public partial class HeroSprite : Control
{
    public enum TownState
    {
        Wandering,
        Rallying,
        WalkingOut,
        Away,
        WalkingIn,
    }

    /// <summary>Gate-walk speed in design-space pixels per second (decoration tuning knob).</summary>
    public const float WalkSpeed = 260f;

    /// <summary>How long a party dwells at the rally point before the first hero peels off
    /// toward the gate (LW1 rally-and-depart).</summary>
    public const float RallyDwellSeconds = 1.0f;

    private const float SpriteWidth = 30f;
    private const float SpriteHeight = 42f;
    private const float SpriteRise = 14f;   // the figure rises above the control's top so it reads as standing
    private const float FootingWidth = 18f; // role-color base bar under the feet
    private const float FeetY = SpriteHeight - SpriteRise;
    private const float WanderAmplitudeX = 14f;
    private const float WanderAmplitudeY = 10f;

    // ── LW1 never-static motion tuning ────────────────────────────────────────────────────
    private const float BreathAmplitude = 1.5f;  // idle micro-bob, px
    private const float BreathFreqHz = 1.2f;
    private const float WalkBobAmplitude = 2.5f; // walk-cycle bob, px
    private const float WalkBobFreqHz = 2.0f;    // ≈ 2 steps/sec
    private const float FlipEpsilon = 0.05f;     // px of horizontal travel before flipping facing
    private const float SquashDuration = 0.2f;   // arrival squash settle time (Trans.Back feel)
    private const float OffscreenLeftX = -80f;   // recruit spawn point, left of the design-space edge

    // ── LW1 anchor vignette: deterministic (heroId, day) idle-target bias ─────────────────
    private const float AnchorCyclePeriod = 20f; // seconds of town-time between anchor windows
    private const float AnchorPauseSeconds = 3f; // how long the pause at the anchor lasts

    /// <summary>Landmark anchor points a wandering hero occasionally pauses at (well /
    /// noticeboard near the gate / tavern door) — presentation-only town-square dressing.</summary>
    public static readonly Vector2[] AnchorPoints =
    [
        new(200, 320), // town well
        new(850, 260), // noticeboard near the gate
        new(748, 175), // tavern door
    ];

    private Vector2 _gate;
    private float _phaseX;
    private float _phaseY;
    private float _speedX;
    private float _speedY;
    private int _day;

    private Vector2 _logicalPosition; // the "real" walk-state position; bob is layered on top for render only
    private float _lastBaseX;
    private TextureRect? _figure;

    private Vector2 _rallyPoint;
    private float _fileDelay;
    private float _rallyElapsed;
    private bool _rallyDwelling;

    private bool _squashing;
    private float _squashElapsed;
    private Vector2 _squashFrom;

    public int HeroValue { get; private set; }

    public string HeroName { get; private set; } = string.Empty;

    public string ClassId { get; private set; } = string.Empty;

    public TownState State { get; private set; } = TownState.Wandering;

    /// <summary>Anchor point the wander drifts around; deterministic per hero id.</summary>
    public Vector2 Home { get; private set; }

    /// <summary>The sim day, pushed in by <see cref="TownScene.Refresh"/> every tick — the
    /// second half of the anchor-vignette's (heroId, day) determinism key. Presentation-only;
    /// never read by the sim.</summary>
    public int Day { set => _day = value; }

    /// <summary>Class → tint color (U12 pinned palette; U16 tints the figure + footing with it).
    /// Reads <see cref="ClassDefinition.ColorRgb"/> (P3), so an add-on class is self-describing;
    /// unknown ids fall back to gray (the old default arm).</summary>
    public static Color RoleColor(string classId)
    {
        if (ClassRegistry.TryGet(classId, out var def))
        {
            var (r, g, b) = def!.ColorRgb;
            return new Color(r / 255f, g / 255f, b / 255f);
        }

        return new Color(0.8f, 0.8f, 0.8f);
    }

    /// <summary>Deterministic (no RNG) day check: does this hero visit a landmark anchor
    /// today? Roughly one day in three, varying by hero so the town square never lock-steps.</summary>
    public static bool VisitsAnchorOn(int heroValue, int day) => (heroValue * 13 + day * 7) % 3 == 0;

    /// <summary>Which anchor this hero visits on the given day (deterministic).</summary>
    public static Vector2 AnchorFor(int heroValue, int day) =>
        AnchorPoints[(heroValue + day) % AnchorPoints.Length];

    /// <summary>Build the marker + label and pin the deterministic wander parameters.</summary>
    public void Setup(Hero hero, Vector2 home, Vector2 gate)
    {
        HeroValue = hero.Id.Value;
        HeroName = hero.Name;
        ClassId = hero.ClassId;
        Home = home;
        _gate = gate;
        Name = $"Hero_{HeroValue}";
        Position = home;
        _logicalPosition = home;
        _lastBaseX = home.X;
        Size = new Vector2(64, 34);
        MouseFilter = MouseFilterEnum.Stop;

        // Deterministic per-hero drift parameters — id in, motion out, no RNG. Reused as the
        // breath-bob / walk-bob phase offset too, so every hero's idle rhythm is desynced.
        _phaseX = HeroValue * 1.7f;
        _phaseY = HeroValue * 2.9f;
        _speedX = 0.55f + HeroValue % 3 * 0.2f;
        _speedY = 0.4f + HeroValue % 4 * 0.15f;

        // U16: the hand-authored role figure, tinted to the role color via Modulate.
        // The figure rises above the control's top so it reads as standing; the
        // control's hit rect (Size) is unchanged, so click routing is identical.
        var sprite = new TextureRect
        {
            Name = "Sprite",
            Texture = IconRegistry.Sprite(hero.ClassId),
            Modulate = RoleColor(hero.ClassId),
            Position = new Vector2((Size.X - SpriteWidth) / 2f, -SpriteRise),
            Size = new Vector2(SpriteWidth, SpriteHeight),
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            PivotOffset = new Vector2(SpriteWidth / 2f, SpriteHeight / 2f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(sprite);
        _figure = sprite;

        // Small role-color footing bar under the feet. Retained (U12 pinned) so the
        // role→color contract stays visible and asserted at a glance.
        var marker = new ColorRect
        {
            Name = "Marker",
            Color = RoleColor(hero.ClassId),
            Position = new Vector2((Size.X - FootingWidth) / 2f, FeetY - 4),
            Size = new Vector2(FootingWidth, 4),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(marker);

        var label = new Label
        {
            Name = "NameLabel",
            Text = hero.Name,
            Position = new Vector2(0, FeetY),
            CustomMinimumSize = new Vector2(Size.X, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", 11);
        AddChild(label);
    }

    /// <summary>
    /// Expedition departs: rally near the gate first (spaced from other party members via
    /// <paramref name="rallyPoint"/>), dwell together, then peel off toward the real gate —
    /// <paramref name="fileDelaySeconds"/> staggers that peel-off so the party exits in file
    /// instead of all at once (LW1 rally-and-depart).
    /// </summary>
    public void BeginDeparture(Vector2 rallyPoint, float fileDelaySeconds)
    {
        _rallyPoint = rallyPoint;
        _fileDelay = Mathf.Max(0f, fileDelaySeconds);
        _rallyElapsed = 0f;
        _rallyDwelling = false;
        State = TownState.Rallying;
    }

    /// <summary>Survivor re-enters through the gate and walks home.</summary>
    public void BeginReturn()
    {
        _logicalPosition = _gate;
        Position = _gate;
        Visible = true;
        State = TownState.WalkingIn;
    }

    /// <summary>
    /// A fresh recruit (<c>RecruitArrived</c>) walks into town from off-screen left, straight
    /// to Home — reuses the WalkingIn state machine so arrival squash/facing come for free.
    /// </summary>
    public void BeginRecruitWalkIn()
    {
        var offscreen = new Vector2(OffscreenLeftX, Home.Y);
        _logicalPosition = offscreen;
        Position = offscreen;
        _lastBaseX = offscreen.X;
        Visible = true;
        State = TownState.WalkingIn;
    }

    /// <summary>In the Mine — not visible in town.</summary>
    public void SetAway()
    {
        Visible = false;
        State = TownState.Away;
        ResetSquash();
    }

    /// <summary>Safety snap: place the hero at home, wandering (used on new-day rollover).</summary>
    public void SnapHome()
    {
        _logicalPosition = Home;
        Position = Home;
        _lastBaseX = Home.X;
        Visible = true;
        State = TownState.Wandering;
        ResetSquash();
    }

    /// <summary>
    /// Advance the decoration by <paramref name="delta"/> seconds at town time
    /// <paramref name="townTime"/>. Called by <see cref="TownScene.Animate"/> only,
    /// so tests can fast-forward the town deterministically without engine frames.
    /// </summary>
    public void Advance(double delta, double townTime)
    {
        var basePos = State switch
        {
            TownState.Wandering => WanderingBasePosition(townTime),
            TownState.Rallying => AdvanceRallying(delta),
            TownState.WalkingOut => AdvanceWalkingOut(delta),
            TownState.WalkingIn => AdvanceWalkingIn(delta),
            _ => _logicalPosition, // Away: frozen, invisible anyway
        };

        UpdateFacing(basePos.X - _lastBaseX);
        _lastBaseX = basePos.X;
        AdvanceSquash(delta);

        var stepping = State == TownState.WalkingOut || State == TownState.WalkingIn ||
                       (State == TownState.Rallying && !_rallyDwelling);
        var idling = State == TownState.Wandering || (State == TownState.Rallying && _rallyDwelling);
        var bobY = stepping ? WalkBob(townTime) : idling ? BreathBob(townTime) : 0f;

        Position = basePos + new Vector2(0, bobY);
    }

    /// <summary>Deterministic lissajous drift for the given town time (pure function of id + t).</summary>
    public Vector2 WanderOffset(double townTime) => new(
        WanderAmplitudeX * Mathf.Sin((float)(townTime * _speedX) + _phaseX),
        WanderAmplitudeY * Mathf.Sin((float)(townTime * _speedY) + _phaseY));

    private Vector2 WanderingBasePosition(double townTime)
    {
        if (VisitsAnchorOn(HeroValue, _day))
        {
            var cyclePos = (float)((townTime + _phaseX) % AnchorCyclePeriod);
            if (cyclePos < AnchorPauseSeconds)
            {
                _logicalPosition = AnchorFor(HeroValue, _day);
                return _logicalPosition;
            }
        }

        _logicalPosition = Home + WanderOffset(townTime);
        return _logicalPosition;
    }

    /// <summary>
    /// Rally → dwell → peel-off, all inside one call when <paramref name="delta"/> is large
    /// enough (test fast-forward convention, e.g. <c>Animate(10)</c>) — each sub-stage spends
    /// only the time it actually needs and hands the remainder to the next, so a single big
    /// Advance call can carry a sprite all the way from Rallying to Away exactly like
    /// WalkingOut always could on its own.
    /// </summary>
    private Vector2 AdvanceRallying(double delta)
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
            TriggerArrivalSquash();
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

        // Dwell (+ file stagger) elapsed — peel off toward the real gate with whatever time
        // is left over from this call.
        State = TownState.WalkingOut;
        return AdvanceWalkingOut(delta);
    }

    private Vector2 AdvanceWalkingOut(double delta)
    {
        StepToward(_gate, delta, out var arrived);
        if (arrived)
        {
            SetAway();
        }

        return _logicalPosition;
    }

    private Vector2 AdvanceWalkingIn(double delta)
    {
        StepToward(Home, delta, out var arrived);
        if (arrived)
        {
            State = TownState.Wandering;
            TriggerArrivalSquash();
        }

        return _logicalPosition;
    }

    /// <summary>
    /// Move <see cref="_logicalPosition"/> toward <paramref name="target"/> at
    /// <see cref="WalkSpeed"/>, consuming only the slice of <paramref name="delta"/> the
    /// remaining distance needs; <paramref name="arrived"/> is true once there, and the
    /// return value is whatever of <paramref name="delta"/> was left unspent (0 mid-walk) —
    /// letting a rally's dwell/peel-off cascade spend it in the same call. Facing follows the
    /// STEP direction, never the rendered (bobbed) position, so the walk bob can never cause
    /// facing jitter.
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

    private void UpdateFacing(float dx)
    {
        if (_figure is null)
        {
            return;
        }

        if (dx > FlipEpsilon)
        {
            _figure.FlipH = false;
        }
        else if (dx < -FlipEpsilon)
        {
            _figure.FlipH = true;
        }
    }

    private void TriggerArrivalSquash()
    {
        if (_figure is null)
        {
            return;
        }

        _squashFrom = new Vector2(1.2f, 0.8f);
        _figure.Scale = _squashFrom;
        _squashElapsed = 0f;
        _squashing = true;
    }

    private void AdvanceSquash(double delta)
    {
        if (!_squashing || _figure is null)
        {
            return;
        }

        _squashElapsed += (float)delta;
        var t = Mathf.Clamp(_squashElapsed / SquashDuration, 0f, 1f);
        _figure.Scale = EaseOutBack(_squashFrom, Vector2.One, t);
        if (t >= 1f)
        {
            _squashing = false;
            _figure.Scale = Vector2.One;
        }
    }

    private void ResetSquash()
    {
        _squashing = false;
        if (_figure is not null)
        {
            _figure.Scale = Vector2.One;
        }
    }

    /// <summary>Ease-out-back curve (the Trans.Back/Ease.Out shape a Tween would use) computed
    /// from accumulated delta instead of an engine Tween node, so it stays exactly as
    /// fast-forwardable/deterministic as the rest of this state machine (Animate-driven, no
    /// wall clock, no per-frame engine callback the test harness would need to pump).</summary>
    private static Vector2 EaseOutBack(Vector2 from, Vector2 to, float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        var tm1 = t - 1f;
        var eased = 1f + c3 * tm1 * tm1 * tm1 + c1 * tm1 * tm1;
        return from.Lerp(to, eased);
    }

    private float BreathBob(double townTime) =>
        BreathAmplitude * Mathf.Sin((float)(townTime * BreathFreqHz) * Mathf.Tau + _phaseX);

    private float WalkBob(double townTime) =>
        WalkBobAmplitude * Mathf.Abs(Mathf.Sin((float)(townTime * WalkBobFreqHz) * Mathf.Tau + _phaseX));
}
