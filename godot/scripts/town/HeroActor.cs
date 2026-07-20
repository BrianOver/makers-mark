using System;
using GameSim.Classes;
using GameSim.Contracts;
using Godot;

namespace GodotClient.Town;

/// <summary>
/// One alive hero's town marker (world-rework U19 — replaces the <c>Control</c>-based
/// <c>HeroSprite</c>): a <see cref="Node2D"/>, feet-anchored at <see cref="Position"/> exactly
/// like a <see cref="LitTownOverlay"/> building wrapper (KTD6), with an <see cref="Area2D"/>
/// pick zone for world clicks (G1 fallback — tests drive it via
/// <c>GodotClient.Tests.UiTestSupport.TryClickArea</c>, never real physics picking) instead of
/// the old <see cref="Control.GuiInput"/> seam.
///
/// <para>The motion state machine below is <c>HeroSprite</c>'s LW1 state machine PORTED
/// VERBATIM — Wandering/Rallying/WalkingOut/Away/WalkingIn, <see cref="StepToward"/>, the
/// breath/walk bobs, the arrival squash — only the base type and the figure's own render anchor
/// changed (Control top-left origin → Node2D feet origin); every distance/timing/easing formula
/// is byte-identical to the pre-U19 code, so <see cref="Position"/> (the state machine's own
/// coordinate) never desyncs from the world-scale doc's Home/gate points.</para>
///
/// <para>Visuals switch from the 30x42 SVG figurine to the painted <c>hero-&lt;classId&gt;</c>
/// portrait via <see cref="AssetCatalog.HeroPortrait"/> (untinted — a painted portrait carries
/// its own color), falling back to the hand-authored <see cref="IconRegistry.Sprite"/> SVG
/// (tinted via <see cref="RoleColor"/>, the pre-U19 look) for a class the art pipeline has not
/// painted yet. Name labels render on hover/selected only (<see cref="SetHovered"/>/
/// <see cref="SetSelected"/>) instead of always-on, per the plan's world declutter.</para>
/// </summary>
public partial class HeroActor : Node2D
{
    public enum TownState
    {
        Wandering,
        Rallying,
        WalkingOut,
        Away,
        WalkingIn,
    }

    /// <summary>Gate-walk speed in world-space pixels per second (decoration tuning knob).</summary>
    public const float WalkSpeed = 260f;

    /// <summary>How long a party dwells at the rally point before the first hero peels off
    /// toward the gate (LW1 rally-and-depart).</summary>
    public const float RallyDwellSeconds = 1.0f;

    // world-scale.md: standing figure height ≈96px, width ≈68px. The figure fits inside this
    // box (aspect-preserved, like the old TextureRect's KeepAspectCentered) and is feet-anchored
    // at THIS node's own origin — Position IS the ground-contact point (KTD6), matching the
    // building-wrapper convention exactly, so no separate "rise" offset is needed the way the
    // old Control (whose origin was NOT its own feet) required.
    private const float SpriteWidth = 68f;
    private const float SpriteHeight = 96f;

    private const float WanderAmplitudeX = 14f;
    private const float WanderAmplitudeY = 10f;

    // ── LW1 never-static motion tuning (verbatim) ─────────────────────────────────────────────
    private const float BreathAmplitude = 1.5f;
    private const float BreathFreqHz = 1.2f;
    private const float WalkBobAmplitude = 2.5f;
    private const float WalkBobFreqHz = 2.0f;
    private const float FlipEpsilon = 0.05f;
    private const float SquashDuration = 0.2f;
    private const float OffscreenLeftX = -80f;

    // ── LW1 anchor vignette (verbatim) ────────────────────────────────────────────────────────
    private const float AnchorCyclePeriod = 20f;
    private const float AnchorPauseSeconds = 3f;

    /// <summary>Pick-zone footprint (world-scale convention): big enough to read as the whole
    /// standing figure, centered on the head/torso so a click anywhere on the visible sprite
    /// hits it.</summary>
    private static readonly Vector2 PickZoneSize = new(SpriteWidth + 22f, SpriteHeight + 8f);
    private static readonly Vector2 PickZoneOffset = new(0f, -SpriteHeight / 2f);

    /// <summary>Landmark anchor points a wandering hero occasionally pauses at (world-scale
    /// doc's wander band / building layout) — presentation-only town-square dressing, ported
    /// verbatim from <c>HeroSprite</c>.</summary>
    public static readonly Vector2[] AnchorPoints =
    [
        new(500, 560),  // town well
        new(1380, 500), // noticeboard near the gate
        new(1100, 560), // tavern door
    ];

    private Vector2 _gate;
    private float _phaseX;
    private float _phaseY;
    private float _speedX;
    private float _speedY;
    private int _day;

    private Vector2 _logicalPosition;
    private float _lastBaseX;
    private Sprite2D? _figure;
    private Vector2 _baseScale = Vector2.One; // the figure's fit-to-box scale, pre-squash
    private float _figureTopOffset; // scaled figure's own top edge, local Y (negative)
    private Label? _nameLabel;
    private Area2D? _pickZone;

    private Vector2 _rallyPoint;
    private float _fileDelay;
    private float _rallyElapsed;
    private bool _rallyDwelling;

    private bool _squashing;
    private float _squashElapsed;
    private Vector2 _squashFrom;

    private bool _hovered;
    private bool _selected;

    public int HeroValue { get; private set; }

    public string HeroName { get; private set; } = string.Empty;

    public string ClassId { get; private set; } = string.Empty;

    public TownState State { get; private set; } = TownState.Wandering;

    /// <summary>Anchor point the wander drifts around; deterministic per hero id.</summary>
    public Vector2 Home { get; private set; }

    /// <summary>The <see cref="Area2D"/> pick zone raising <see cref="Clicked"/> — the world
    /// click target tests drive via <c>UiTestSupport.TryClickArea</c> (G1 fallback).</summary>
    public Area2D PickZone => _pickZone!;

    /// <summary>
    /// Point a LW2 speech bubble's tail should touch — the top of the standing figure, tracking
    /// whatever <see cref="Position"/> currently is (walk bob / breath bob included), so a bubble
    /// that repositions here every tick rides along with the hero instead of floating in a fixed
    /// spot. Feet-anchored origin (KTD6) means no horizontal offset is needed (the figure is
    /// already centered on this node), unlike the pre-U19 Control anchor.
    /// </summary>
    public Vector2 HeadAnchor => Position + new Vector2(0, _figureTopOffset);

    /// <summary>The sim day, pushed in by <see cref="TownScene.Refresh"/> every tick — the
    /// second half of the anchor-vignette's (heroId, day) determinism key. Presentation-only;
    /// never read by the sim.</summary>
    public int Day { set => _day = value; }

    /// <summary>A hero actor was clicked (payload: <see cref="HeroValue"/>) — routes to
    /// hero-inspect via <see cref="TownScene.HeroClicked"/>.</summary>
    public event Action<int>? Clicked;

    /// <summary>Class → tint color (P3 pinned palette). Reads <see cref="ClassDefinition.ColorRgb"/>
    /// so an add-on class is self-describing; unknown ids fall back to gray. Applied only to the
    /// SVG-fallback figure — a painted portrait carries its own color and renders untinted.</summary>
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

    /// <summary>Build the figure + pick zone + name label and pin the deterministic wander
    /// parameters.</summary>
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

        // Deterministic per-hero drift parameters — id in, motion out, no RNG. Reused as the
        // breath-bob / walk-bob phase offset too, so every hero's idle rhythm is desynced.
        _phaseX = HeroValue * 1.7f;
        _phaseY = HeroValue * 2.9f;
        _speedX = 0.55f + HeroValue % 3 * 0.2f;
        _speedY = 0.4f + HeroValue % 4 * 0.15f;

        var (texture, tint) = ResolveFigure(hero.ClassId);
        var sprite = new Sprite2D
        {
            Name = "Sprite",
            Texture = texture,
            Modulate = tint,
            Centered = false,
        };
        if (texture is not null)
        {
            var scale = FitScale(texture);
            var scaledWidth = texture.GetWidth() * scale;
            var scaledHeight = texture.GetHeight() * scale;
            sprite.Scale = Vector2.One * scale;
            sprite.Position = new Vector2(-scaledWidth / 2f, -scaledHeight);
            _baseScale = sprite.Scale;
        }

        AddChild(sprite);
        _figure = sprite;
        _figureTopOffset = sprite.Position.Y;

        _pickZone = new Area2D { Name = "PickZone" };
        _pickZone.AddChild(new CollisionShape2D
        {
            Shape = new RectangleShape2D { Size = PickZoneSize },
            Position = PickZoneOffset,
        });
        _pickZone.InputEvent += (_, @event, _) =>
        {
            if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
            {
                Clicked?.Invoke(HeroValue);
            }
        };
        _pickZone.MouseEntered += () => SetHovered(true);
        _pickZone.MouseExited += () => SetHovered(false);
        AddChild(_pickZone);

        _nameLabel = new Label
        {
            Name = "NameLabel",
            Text = hero.Name,
            Visible = false,
            Position = new Vector2(-40f, 4f),
            CustomMinimumSize = new Vector2(80f, 14f),
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _nameLabel.AddThemeFontSizeOverride("font_size", 11);
        AddChild(_nameLabel);
    }

    /// <summary>Resolve this hero's figure: the painted class portrait (untinted — it carries its
    /// own color) when the art pipeline has generated one, otherwise the hand-authored SVG
    /// figurine tinted to the class's role color (the pre-U19 look). Both resolvers are
    /// null-tolerant; an id with neither degrades to no texture, never a throw.</summary>
    private static (Texture2D? Texture, Color Tint) ResolveFigure(string classId)
    {
        var painted = AssetCatalog.HeroPortrait(classId);
        return painted is not null ? (painted, Colors.White) : (IconRegistry.Sprite(classId), RoleColor(classId));
    }

    /// <summary>Uniform scale so the texture fits inside the <see cref="SpriteWidth"/> x
    /// <see cref="SpriteHeight"/> box, aspect preserved (the Node2D analog of the old
    /// TextureRect's <c>KeepAspectCentered</c>). 0-safe (a 0-dimension texture never divides by
    /// zero — falls back to no scaling).</summary>
    private static float FitScale(Texture2D texture)
    {
        var width = Mathf.Max(1, texture.GetWidth());
        var height = Mathf.Max(1, texture.GetHeight());
        return Mathf.Min(SpriteWidth / width, SpriteHeight / height);
    }

    /// <summary>Hover affordance (R2): shows the name label while the cursor is over the pick
    /// zone. Presentation-only; real physics picking is unproven under headless CI (G1), so this
    /// path is exercised by manual smoke, not the automated suite.</summary>
    public void SetHovered(bool hovered)
    {
        _hovered = hovered;
        UpdateLabelVisibility();
    }

    /// <summary>Selection affordance (R2): keeps the name label shown after a click, until
    /// another actor is selected — set/cleared by <see cref="TownScene"/>.</summary>
    public void SetSelected(bool selected)
    {
        _selected = selected;
        UpdateLabelVisibility();
    }

    private void UpdateLabelVisibility()
    {
        if (_nameLabel is not null)
        {
            _nameLabel.Visible = _hovered || _selected;
        }
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

    /// <summary>Squashes relative to <see cref="_baseScale"/> (the figure's own fit-to-box
    /// scale set in <see cref="Setup"/>), never relative to <see cref="Vector2.One"/> — a
    /// painted portrait's fit-scale is rarely 1:1, and squashing toward One would jump its size
    /// the instant a squash starts.</summary>
    private void TriggerArrivalSquash()
    {
        if (_figure is null)
        {
            return;
        }

        _squashFrom = _baseScale * new Vector2(1.2f, 0.8f);
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
        _figure.Scale = EaseOutBack(_squashFrom, _baseScale, t);
        if (t >= 1f)
        {
            _squashing = false;
            _figure.Scale = _baseScale;
        }
    }

    private void ResetSquash()
    {
        _squashing = false;
        if (_figure is not null)
        {
            _figure.Scale = _baseScale;
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
