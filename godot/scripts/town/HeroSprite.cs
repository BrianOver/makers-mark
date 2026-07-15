using GameSim.Contracts;
using Godot;

namespace GodotClient.Town;

/// <summary>
/// One alive hero's town marker (U12 placeholder art): a role-colored square plus a
/// name label. Movement is a four-state machine driven by <see cref="TownScene"/>
/// (never by its own _Process): Wandering in town, WalkingOut through the gate when
/// the expedition departs, Away while in the Mine, WalkingIn when the survivor
/// returns. The wander is a deterministic lissajous drift derived from the hero id —
/// presentation only, NO RNG, so it can never touch the sim's stream (KTD2/KTD4).
/// </summary>
public partial class HeroSprite : Control
{
    public enum TownState
    {
        Wandering,
        WalkingOut,
        Away,
        WalkingIn,
    }

    /// <summary>Gate-walk speed in design-space pixels per second (decoration tuning knob).</summary>
    public const float WalkSpeed = 260f;

    private const float MarkerSize = 14f;
    private const float WanderAmplitudeX = 14f;
    private const float WanderAmplitudeY = 10f;

    private Vector2 _gate;
    private float _phaseX;
    private float _phaseY;
    private float _speedX;
    private float _speedY;

    public int HeroValue { get; private set; }

    public string HeroName { get; private set; } = string.Empty;

    public HeroRole Role { get; private set; }

    public TownState State { get; private set; } = TownState.Wandering;

    /// <summary>Anchor point the wander drifts around; deterministic per hero id.</summary>
    public Vector2 Home { get; private set; }

    /// <summary>Role → placeholder color (U12 pinned palette).</summary>
    public static Color RoleColor(HeroRole role) => role switch
    {
        HeroRole.Vanguard => new Color(0.27f, 0.51f, 0.71f), // steel blue
        HeroRole.Striker => new Color(0.86f, 0.08f, 0.24f),  // crimson
        HeroRole.Mystic => new Color(0.54f, 0.17f, 0.89f),   // violet
        _ => new Color(0.8f, 0.8f, 0.8f),
    };

    /// <summary>Build the marker + label and pin the deterministic wander parameters.</summary>
    public void Setup(Hero hero, Vector2 home, Vector2 gate)
    {
        HeroValue = hero.Id.Value;
        HeroName = hero.Name;
        Role = hero.Role;
        Home = home;
        _gate = gate;
        Name = $"Hero_{HeroValue}";
        Position = home;
        Size = new Vector2(64, 34);
        MouseFilter = MouseFilterEnum.Stop;

        // Deterministic per-hero drift parameters — id in, motion out, no RNG.
        _phaseX = HeroValue * 1.7f;
        _phaseY = HeroValue * 2.9f;
        _speedX = 0.55f + HeroValue % 3 * 0.2f;
        _speedY = 0.4f + HeroValue % 4 * 0.15f;

        var marker = new ColorRect
        {
            Name = "Marker",
            Color = RoleColor(hero.Role),
            Position = new Vector2((Size.X - MarkerSize) / 2f, 0),
            Size = new Vector2(MarkerSize, MarkerSize),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(marker);

        var label = new Label
        {
            Name = "NameLabel",
            Text = hero.Name,
            Position = new Vector2(0, MarkerSize + 2),
            CustomMinimumSize = new Vector2(Size.X, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", 11);
        AddChild(label);
    }

    /// <summary>Expedition departs: walk toward the gate, then vanish into the Mine.</summary>
    public void BeginDeparture() => State = TownState.WalkingOut;

    /// <summary>Survivor re-enters through the gate and walks home.</summary>
    public void BeginReturn()
    {
        Position = _gate;
        Visible = true;
        State = TownState.WalkingIn;
    }

    /// <summary>In the Mine — not visible in town.</summary>
    public void SetAway()
    {
        Visible = false;
        State = TownState.Away;
    }

    /// <summary>Safety snap: place the hero at home, wandering (used on new-day rollover).</summary>
    public void SnapHome()
    {
        Position = Home;
        Visible = true;
        State = TownState.Wandering;
    }

    /// <summary>
    /// Advance the decoration by <paramref name="delta"/> seconds at town time
    /// <paramref name="townTime"/>. Called by <see cref="TownScene.Animate"/> only,
    /// so tests can fast-forward the town deterministically without engine frames.
    /// </summary>
    public void Advance(double delta, double townTime)
    {
        switch (State)
        {
            case TownState.Wandering:
                Position = Home + WanderOffset(townTime);
                break;
            case TownState.WalkingOut:
                if (StepToward(_gate, delta))
                {
                    SetAway();
                }

                break;
            case TownState.WalkingIn:
                if (StepToward(Home, delta))
                {
                    State = TownState.Wandering;
                }

                break;
            case TownState.Away:
            default:
                break;
        }
    }

    /// <summary>Deterministic lissajous drift for the given town time (pure function of id + t).</summary>
    public Vector2 WanderOffset(double townTime) => new(
        WanderAmplitudeX * Mathf.Sin((float)(townTime * _speedX) + _phaseX),
        WanderAmplitudeY * Mathf.Sin((float)(townTime * _speedY) + _phaseY));

    /// <summary>Move toward the target at <see cref="WalkSpeed"/>; true when arrived.</summary>
    private bool StepToward(Vector2 target, double delta)
    {
        var step = WalkSpeed * (float)delta;
        Position = Position.MoveToward(target, step);
        return Position.DistanceTo(target) < 0.5f;
    }
}
