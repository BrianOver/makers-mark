using Godot;

namespace GodotClient.Town2d;

/// <summary>
/// U7 (asset-completion wave, "the workshop is not switched off"): a heat-glow pulse for the
/// forge's own furnace and anvil. Before this unit both stations were flat, static sprites —
/// the only motion either one carried was <see cref="Building2D.Tell"/>, the U12 "you can click
/// this" affordance pulse every real-verb station in the game gets identically (market counter,
/// tavern bar, gate muster board, ...). That is a UI cue, not a property of what a FURNACE is:
/// it never made the furnace itself read as an active heat source. This adds that, restrained to
/// exactly the two stations that plausibly radiate heat.
///
/// <para><b>A pure, engine-free accumulator drives it</b> — <see cref="EmberPulse"/> mirrors
/// <c>TreeSway</c>/<c>SpriteMotion</c>'s own convention exactly: only accumulates a delta and
/// returns a value, no node/scene/runtime dependency, so it is unit-testable without
/// <c>[RequireGodotRuntime]</c>. <see cref="ForgeEmberGlowSprite2D"/> is the thin node wrapper
/// (mirrors <c>SwayingTreeSprite2D</c>'s "the node IS the state" shape) that owns one and paints
/// its alpha onto a small additive-blended overlay sprite — never the station's OWN <see
/// cref="Building2D.Sprite"/>.Modulate, which <see cref="Building2D.SetHighlighted"/> and
/// <see cref="Building2D.SetTutorialPulsing"/> already own; writing this pulse there too would
/// silently fight both on every frame either is active.</para>
///
/// <para><b>Why the anvil gets the SAME idiom as the furnace, not a spark tied to a hammer
/// strike.</b> The richer version this packet asked for FIRST — a spark burst timed to a landed
/// hit — needs a signal from <c>ForgeMinigame.ForgeStrike</c> (and its own private nested strike-FX
/// class, "a spark burst from the billet + a small shake") reaching this presentation-only WORLD
/// layer. <c>ForgeMinigame</c> is a <c>Control</c> minigame mounted by <c>ForgePanel</c> — a UI
/// overlay <c>MainUi</c> opens on top of the town, never a child of <see cref="InteriorRoom2D"/>'s
/// world scene — with no event that survives past its own panel's lifetime and no reference this
/// presentation-only class has any legitimate way to hold. Reaching it would mean a new cross-tree
/// signal plumbed through <c>MainUi</c>/<c>ForgePanel</c>, both outside this unit's file-ownership
/// boundary this round. So the anvil gets the same restrained pulse the furnace gets — dimmer,
/// since nothing is actively burning there — read as "the furnace's own heat catching the nearby
/// metal," rather than a fake, untied spark with nothing driving it.</para>
///
/// <para><b>Accumulated delta only, no pause gate — matches every sibling cosmetic animator's own
/// contract.</b> <c>AmbientLife2D</c>'s lamp flicker/awning sway, <c>Building2D.Tell</c>'s
/// affordance pulse, <c>TavernLife2D</c>'s seated-patron breath, <c>MarketLife2D</c>'s whole
/// choreography, and <c>SwayingTreeSprite2D</c>'s wind-sway all advance unconditionally on every
/// <c>_Process</c> tick regardless of <c>PhaseClock.Playing</c> — none of them read the clock at
/// all. That is a deliberate, documented carve-out (KTD4/KTD5: "particles + <c>_Process</c>
/// flicker are fine" cosmetic exception, not gameplay state), not an oversight this unit should
/// "fix" by adding a pause check nothing else in this family has. This class follows the identical
/// shape on purpose.</para>
/// </summary>
public sealed class EmberPulse
{
    private readonly float _phaseSeed;
    private readonly float _baseAlpha;
    private readonly float _amplitude;
    private readonly float _hz;
    private double _time;

    public EmberPulse(float phaseSeed, float baseAlpha, float amplitude, float hz)
    {
        _phaseSeed = phaseSeed;
        _baseAlpha = baseAlpha;
        _amplitude = amplitude;
        _hz = hz;
    }

    /// <summary>
    /// Accumulates <paramref name="delta"/> and returns this frame's alpha. Pure function of
    /// accumulated time plus the constructor's own tuning: no RNG, no wall-clock, no
    /// <c>Godot.Time</c> (KTD4/KTD5) — identical tuning + delta sequence always yields an
    /// identical alpha sequence.
    /// </summary>
    public float Advance(double delta)
    {
        _time += delta;
        return _baseAlpha + _amplitude * Mathf.Sin((float)(_time * _hz * Mathf.Tau) + _phaseSeed);
    }
}

/// <summary>One station's ember-glow tuning — see <see cref="ForgeEmberGlowSprite2D"/>'s class
/// doc for why the furnace and anvil share this idiom with different numbers rather than the
/// anvil getting a distinct spark effect.</summary>
public readonly record struct EmberTuning(Color Color, float BaseAlpha, float Amplitude, float Hz);

/// <summary>The heat-glow overlay itself — a small additive-blended <see cref="Sprite2D"/> mounted
/// as a CHILD of the furnace/anvil <see cref="Building2D"/> station (see <see
/// cref="InteriorRoom2D"/>'s <c>BuildStations</c>, the only mount site), so it Y-sorts and tears
/// down with its station for free: <c>Building2D</c> owns no logic that reaches into this node,
/// and freeing the station (<c>PanelGraveyard.Bury</c>'s <c>QueueFree</c> cascade, or a direct
/// <c>Free()</c> in a test) frees this along with every other child it already has (Sprite/Tell/
/// Interact/Footprint/NameLabel/DoorAnchor) — no separate teardown path to get wrong.</summary>
public partial class ForgeEmberGlowSprite2D : Sprite2D
{
    /// <summary>Fraction of the station's WIDER dimension the glow's diameter fills — CONTAINED
    /// within the sprite's own silhouette (unlike <c>Building2D.Tell</c>'s deliberately-poking-out
    /// 1.35 halo, which reads as "look here"). This wants to read as the object's own material
    /// glowing, not a light source floating beside it.</summary>
    private const float EmberDiameterFraction = 0.85f;

    /// <summary>Furnace: a slow, deep-orange heat-breathing pulse — bright enough to read as an
    /// active heat source across the room, slow enough (0.35 Hz, ~2.9s per cycle) to read as
    /// warmth rather than a strobe.</summary>
    public static readonly EmberTuning FurnaceTuning = new(new Color(1f, 0.42f, 0.12f), 0.42f, 0.22f, 0.35f);

    /// <summary>Anvil: the SAME pulse idiom, dimmer/cooler and phase-offset (see <see
    /// cref="TuningFor"/>'s caller) — no strike-tied spark was reachable from this
    /// presentation-only layer (see the class doc); this reads as ambient heat off the nearby
    /// furnace, never the anvil's own fire.</summary>
    public static readonly EmberTuning AnvilTuning = new(new Color(1f, 0.55f, 0.25f), 0.24f, 0.12f, 0.35f);

    private static GradientTexture2D? _glowTextureCache;

    private EmberPulse _pulse = null!;

    /// <summary>Station id -&gt; tuning, or <see langword="null"/> for every station that gets no
    /// ember glow (every non-forge station, and the forge's own bellows/quench/shelf/rack). The
    /// ONLY two ids this unit's scope covers.</summary>
    public static EmberTuning? TuningFor(string stationId) => stationId switch
    {
        "furnace" => FurnaceTuning,
        "anvil" => AnvilTuning,
        _ => null,
    };

    /// <summary>
    /// Must be called once right after construction, before adding this node as a child (mirrors
    /// every other code-built node's <c>Init</c>/<c>Configure</c> convention in this namespace).
    /// <paramref name="stationSize"/> is the station's OWN sprite size (<see
    /// cref="Building2D.Sprite"/>'s texture) — this centers/scales the glow the exact same way
    /// <c>Building2D.BuildTell</c> centers its own halo on that sprite's centroid.
    /// </summary>
    public void Init(EmberTuning tuning, float phaseSeed, Vector2 stationSize)
    {
        _pulse = new EmberPulse(phaseSeed, tuning.BaseAlpha, tuning.Amplitude, tuning.Hz);

        Name = "EmberGlow";
        Texture = GlowTexture();
        Centered = true;
        Position = new Vector2(0f, -stationSize.Y / 2f);
        var diameter = Mathf.Max(stationSize.X, stationSize.Y) * EmberDiameterFraction;
        Scale = new Vector2(diameter, diameter) / 32f; // GlowTexture is a fixed 32x32 canvas
        Modulate = new Color(tuning.Color.R, tuning.Color.G, tuning.Color.B, tuning.BaseAlpha);
        Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
    }

    public override void _Process(double delta)
    {
        var color = Modulate;
        color.A = _pulse.Advance(delta);
        Modulate = color;
    }

    /// <summary>Same radial white-to-transparent falloff recipe as <c>AmbientLife2D
    /// .LampGlowTexture</c>/<c>Building2D.TellGlowTexture</c> — this file owns its own copy per
    /// those classes' own documented "no cross-class reach-in" precedent for this exact shape.</summary>
    private static GradientTexture2D GlowTexture() => _glowTextureCache ??= new GradientTexture2D
    {
        Gradient = new Gradient
        {
            Colors = [new Color(1, 1, 1, 1), new Color(1, 1, 1, 0.4f), new Color(1, 1, 1, 0)],
            Offsets = [0f, 0.5f, 1f],
        },
        Width = 32,
        Height = 32,
        Fill = GradientTexture2D.FillEnum.Radial,
        FillFrom = new Vector2(0.5f, 0.5f),
        FillTo = new Vector2(1f, 0.5f),
    };
}
