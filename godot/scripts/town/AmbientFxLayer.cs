using System.Collections.Generic;
using GameSim.Contracts;
using Godot;

namespace GodotClient.Town;

/// <summary>
/// LW4 atmosphere layer: window glow, the forge-coals landmark, ambient particles, props, and
/// fog wisps — everything that makes the lit town feel inhabited in the negative space around
/// the buildings and heroes. Mounted as a child of <see cref="LitTownOverlay"/>'s <c>WorldRoot</c>
/// (same SubViewport, same CanvasModulate scope), built entirely in code (no .tscn authoring),
/// CpuParticles2D-only (headless-simulatable — GPUParticles2D needs a compute shader that never
/// runs under <c>--headless</c>), and null-tolerant against <see cref="IconRegistry.Lit"/> so a
/// missing prop id degrades to "no sprite" rather than a crash — same contract as
/// <see cref="LitTownOverlay"/>'s buildings/heroes.
///
/// <para>All motion is accumulated-delta (never wall clock, never engine RNG) per CLAUDE.md rule
/// 5 / KTD2 — sway and fog drift are pure <c>Mathf.Sin</c> / linear-pan presentation, exactly the
/// pattern <see cref="LitTownOverlay"/>'s ember flicker already uses. CpuParticles2D's own
/// internal emission jitter is the engine's cosmetic particle RNG, not the sim's injected
/// stream — it never touches game state. Note: <c>Direction</c>/<c>Gravity</c> on the 2D node are
/// <see cref="Vector2"/> (the Vector3 gotcha belongs to CPUParticles3D, not this one — verified
/// against GodotSharp 4.6.3, same finding LW3/LW5 made independently).</para>
/// </summary>
public partial class AmbientFxLayer : Node2D
{
    /// <summary>A placeable prop: its <see cref="IconRegistry.Lit"/> id, world position, and
    /// whether it gets the lantern/laundry sway (<c>Rotation = sin(t)*0.05</c>).</summary>
    public readonly record struct PropSpec(string Id, Vector2 Position, bool Sways);

    // ── Tunables (recipes from plan §LW4) ─────────────────────────────────────────────────────
    private const float PropTargetWidth = 56f;
    private const float SwayFrequencyHz = 0.4f;
    private const float SwayAmplitudeRad = 0.05f;

    private const float WindowGlowWidth = 96f;
    private static readonly Color WindowGlowColor = new(1f, 0.78f, 0.45f);

    // U14: LitTownOverlay.BuildingSpec.Position is now the GROUND-LINE anchor (feet, KTD6), not
    // the facade's visual center it was pre-U14 — every offset below is retuned relative to that
    // new reference point (all negative-Y "up from the ground" instead of "up from mid-facade").
    private static readonly Vector2 WindowGlowOffset = new(0f, -190f); // mid-upper facade band

    // The forge-coals landmark: the ONE strongest light town-wide (LitTownOverlay's per-building
    // lantern lights run at LightEnergy 1.2 — this beats them so the coals read as the hottest
    // spot in town), sitting low and close to the forge's own ground line (a hearth, not a hung
    // lantern).
    private const float ForgeCoalsEnergy = 1.9f;
    private const float ForgeCoalsTextureScale = 1.4f;
    private const float ForgeCoalsHeight = 14f;
    private static readonly Color ForgeCoalsColor = new(1f, 0.42f, 0.12f);
    private static readonly Vector2 ForgeCoalsOffset = new(30f, -20f);

    private static readonly Vector2 ChimneyOffset = new(60f, -300f); // roofline

    private const float FogAlpha = 0.12f;
    private const float FogPanSpeed = 6f; // design px/s
    private const float FogWrapWidth = 1700f; // wider than the 1600px canvas so the wrap never pops

    /// <summary>The 8 committed props (LW-art), placed around the world-scale doc's wander band
    /// (world X [300,1300], Y [460,600]) in front of the four re-spaced facades. Only the string
    /// lanterns and laundry line sway — everything else is a static ground prop.</summary>
    public static readonly PropSpec[] DefaultProps =
    [
        new("props-town-well", new Vector2(420, 560), false),
        new("props-noticeboard", new Vector2(1380, 500), false),
        new("props-ore-cart", new Vector2(1200, 560), false),
        new("props-market-crates", new Vector2(760, 460), false),
        new("props-string-lanterns", new Vector2(1100, 320), true),
        new("props-laundry-line", new Vector2(600, 340), true),
        new("props-tavern-cat", new Vector2(1150, 580), false),
        new("props-forge-salamander", new Vector2(300, 460), false),
    ];

    private readonly List<Sprite2D> _windowGlows = [];
    private readonly List<CpuParticles2D> _chimneySmokes = [];
    private readonly List<Sprite2D> _swaying = [];
    private readonly List<Sprite2D> _props = [];
    private readonly List<Sprite2D> _fogWisps = [];
    private readonly List<PointLight2D> _lights = [];

    private GradientTexture2D _glowTexture = null!;
    private GradientTexture2D _fogTexture = null!;
    private CpuParticles2D? _forgeEmbers;
    private CpuParticles2D? _fireflies;
    private CpuParticles2D? _dustMotes;
    private float _time;
    private bool _built;

    /// <summary>Per-building window-glow additive sprites (for tests / tuning).</summary>
    public IReadOnlyList<Sprite2D> WindowGlows => _windowGlows;

    /// <summary>Extra lights this layer adds (today: just the forge-coals landmark) — combine
    /// with <see cref="LitTownOverlay.Lights"/> for the town-wide light-budget check.</summary>
    public IReadOnlyList<PointLight2D> Lights => _lights;

    /// <summary>Per-building chimney smoke emitters.</summary>
    public IReadOnlyList<CpuParticles2D> ChimneySmokes => _chimneySmokes;

    /// <summary>Resolved prop sprites (missing ids are simply absent — graceful degrade).</summary>
    public IReadOnlyList<Sprite2D> Props => _props;

    /// <summary>The 2 fog wisp sprites.</summary>
    public IReadOnlyList<Sprite2D> FogWisps => _fogWisps;

    public CpuParticles2D? ForgeEmbers => _forgeEmbers;
    public CpuParticles2D? Fireflies => _fireflies;
    public CpuParticles2D? DustMotes => _dustMotes;

    /// <summary>Build with the shipped 4 buildings + 8 props.</summary>
    public void Build(IReadOnlyList<LitTownOverlay.BuildingSpec> buildings) => Build(buildings, DefaultProps);

    /// <summary>
    /// Build the fx set from the given buildings/props. Injectable so tests can exercise the
    /// graceful-degrade path (a fake prop id yields no sprite). Idempotent-guarded.
    /// </summary>
    public void Build(IReadOnlyList<LitTownOverlay.BuildingSpec> buildings, IReadOnlyList<PropSpec> props)
    {
        if (_built)
        {
            return;
        }

        Name = "AmbientFxLayer";
        _glowTexture = BuildGlowGradient();
        _fogTexture = BuildFogGradient();

        foreach (var building in buildings)
        {
            AddWindowGlow(building);
            AddChimneySmoke(building);
            if (building.Key == "forge")
            {
                AddForgeLandmark(building);
            }
        }

        AddFireflies();
        AddDustMotes();

        foreach (var prop in props)
        {
            AddProp(prop);
        }

        AddFogWisps();

        _built = true;
    }

    /// <summary>Push the phase onto every phase-gated fx: window-glow alpha ramp, fireflies
    /// (dusk/night only), dust motes (daytime only), fog wisps (dusk/night only). Chimney smoke
    /// and the forge embers run always — the forge never goes cold.</summary>
    public void ApplyPhase(DayPhase phase)
    {
        var glowAlpha = GlowAlphaFor(phase);
        foreach (var glow in _windowGlows)
        {
            glow.Modulate = new Color(1f, 1f, 1f, glowAlpha);
            glow.Visible = glowAlpha > 0f;
        }

        var isNight = phase is DayPhase.Camp or DayPhase.ExpeditionDeep or DayPhase.Evening;
        var isDay = phase is DayPhase.Morning or DayPhase.Expedition;

        if (_fireflies is not null)
        {
            _fireflies.Emitting = isNight;
        }

        if (_dustMotes is not null)
        {
            _dustMotes.Emitting = isDay;
        }

        foreach (var fog in _fogWisps)
        {
            fog.Visible = isNight;
        }
    }

    /// <summary>Window-glow alpha ramp: off in daylight, rising as the ambient cools toward
    /// night (so the warm glow pops harder against <see cref="LitTownOverlay.AtmosphereTintFor"/>'s
    /// cooler dusk/night stops).</summary>
    private static float GlowAlphaFor(DayPhase phase) => phase switch
    {
        DayPhase.Morning => 0f,
        DayPhase.Expedition => 0f,
        DayPhase.Camp => 0.55f,
        DayPhase.ExpeditionDeep => 0.80f,
        DayPhase.Evening => 1.00f,
        _ => 0f,
    };

    public override void _Process(double delta)
    {
        _time += (float)delta;

        for (var i = 0; i < _swaying.Count; i++)
        {
            var phaseOffset = i * 0.9f; // per-prop offset so lanterns/laundry don't sway in lockstep
            _swaying[i].Rotation = Mathf.Sin((_time + phaseOffset) * SwayFrequencyHz * Mathf.Tau) * SwayAmplitudeRad;
        }

        for (var i = 0; i < _fogWisps.Count; i++)
        {
            var wisp = _fogWisps[i];
            var nextX = wisp.Position.X + FogPanSpeed * (float)delta;
            if (nextX > FogWrapWidth)
            {
                nextX -= FogWrapWidth;
            }

            wisp.Position = new Vector2(nextX, wisp.Position.Y);
        }
    }

    private void AddWindowGlow(LitTownOverlay.BuildingSpec building)
    {
        var sprite = new Sprite2D
        {
            Name = $"WindowGlow_{building.Key}",
            Texture = _glowTexture,
            Position = building.Position + WindowGlowOffset,
            Scale = Vector2.One * (WindowGlowWidth / _glowTexture.Width),
            Modulate = new Color(1f, 1f, 1f, 0f), // starts off — ApplyPhase(Morning) ramps it
            Visible = false,
            Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add },
        };
        AddChild(sprite);
        _windowGlows.Add(sprite);
    }

    private void AddChimneySmoke(LitTownOverlay.BuildingSpec building)
    {
        var smoke = new CpuParticles2D
        {
            Name = $"ChimneySmoke_{building.Key}",
            Position = building.Position + ChimneyOffset,
            Emitting = true,
            Amount = 10,
            Lifetime = 4.5,
            Direction = new Vector2(0f, -1f), // 2D node — Direction/Gravity are Vector2 (Vector3 gotcha is CPUParticles3D's, not this)
            Spread = 12f,
            Gravity = new Vector2(0f, -14f), // smoke rises
            InitialVelocityMin = 10f,
            InitialVelocityMax = 20f,
            ScaleAmountMin = 0.6f,
            ScaleAmountMax = 1.4f,
            Color = new Color(0.85f, 0.85f, 0.85f, 0.35f),
            ColorRamp = FadeRamp(new Color(0.85f, 0.85f, 0.85f, 0.4f), new Color(0.85f, 0.85f, 0.85f, 0f)),
        };
        AddChild(smoke);
        _chimneySmokes.Add(smoke);
    }

    private void AddForgeLandmark(LitTownOverlay.BuildingSpec forge)
    {
        var position = forge.Position + ForgeCoalsOffset;

        var light = new PointLight2D
        {
            Name = "ForgeCoalsLight",
            Position = position,
            Color = ForgeCoalsColor,
            Energy = ForgeCoalsEnergy,
            Texture = _glowTexture,
            TextureScale = ForgeCoalsTextureScale,
            Height = ForgeCoalsHeight,
        };
        AddChild(light);
        _lights.Add(light);

        _forgeEmbers = new CpuParticles2D
        {
            Name = "ForgeEmberBurst",
            Position = position,
            Emitting = true,
            Amount = 20,
            Lifetime = 0.6,
            Explosiveness = 0.8f,
            Randomness = 0.4f,
            Direction = new Vector2(0f, -1f),
            Spread = 35f,
            Gravity = new Vector2(0f, 200f), // sparks pop up, then fall
            InitialVelocityMin = 40f,
            InitialVelocityMax = 90f,
            ScaleAmountMin = 0.3f,
            ScaleAmountMax = 0.7f,
            Color = new Color(1f, 0.55f, 0.15f),
            ColorRamp = FadeRamp(new Color(1f, 0.75f, 0.25f), new Color(0.6f, 0.15f, 0.05f, 0f)),
        };
        AddChild(_forgeEmbers);
    }

    private void AddFireflies()
    {
        _fireflies = new CpuParticles2D
        {
            Name = "Fireflies",
            Position = new Vector2(800f, 460f), // world-scale doc: middle of the wander band
            Emitting = false, // ApplyPhase(Camp/ExpeditionDeep/Evening) turns them on
            Amount = 10,
            Lifetime = 3.0,
            Direction = new Vector2(0f, -1f),
            Spread = 180f, // wander every direction, not a jet
            Gravity = Vector2.Zero,
            InitialVelocityMin = 4f,
            InitialVelocityMax = 14f,
            ScaleAmountMin = 0.4f,
            ScaleAmountMax = 0.8f,
            Color = new Color(0.85f, 1f, 0.55f),
            ColorRamp = FadeRamp(new Color(0.85f, 1f, 0.55f, 0f), new Color(0.85f, 1f, 0.55f, 0.9f)), // alpha-hump blink
            Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add },
        };
        AddChild(_fireflies);
    }

    private void AddDustMotes()
    {
        _dustMotes = new CpuParticles2D
        {
            Name = "DustMotes",
            Position = new Vector2(800f, 380f), // world-scale doc: middle of the wander band
            Emitting = false, // ApplyPhase(Morning/Expedition) turns them on
            Amount = 15,
            Lifetime = 6.0,
            Direction = new Vector2(0f, 1f),
            Spread = 180f,
            Gravity = new Vector2(0f, 2f), // near-zero — motes barely fall
            InitialVelocityMin = 2f,
            InitialVelocityMax = 6f,
            ScaleAmountMin = 0.2f,
            ScaleAmountMax = 0.4f,
            Color = new Color(1f, 0.96f, 0.85f, 0.4f), // alpha ≤0.4 per the recipe
        };
        AddChild(_dustMotes);
    }

    private void AddProp(PropSpec spec)
    {
        var lit = IconRegistry.Lit(spec.Id);
        if (lit is null)
        {
            return; // graceful degrade — same contract as LitTownOverlay's buildings/heroes
        }

        var sprite = new Sprite2D
        {
            Name = $"Prop_{spec.Id}",
            Texture = lit,
            Position = spec.Position,
        };
        var width = lit.DiffuseTexture?.GetWidth() ?? 0;
        if (width > 0)
        {
            sprite.Scale = Vector2.One * (PropTargetWidth / width);
        }

        AddChild(sprite);
        _props.Add(sprite);

        if (spec.Sways)
        {
            _swaying.Add(sprite);
        }
    }

    private void AddFogWisps()
    {
        AddFogWisp("FogWisp_0", new Vector2(0f, 420f));
        AddFogWisp("FogWisp_1", new Vector2(850f, 520f));
    }

    private void AddFogWisp(string name, Vector2 position)
    {
        var wisp = new Sprite2D
        {
            Name = name,
            Texture = _fogTexture,
            Position = position,
            Scale = new Vector2(2.5f, 1.4f),
            Modulate = new Color(1f, 1f, 1f, FogAlpha),
            Visible = false, // ApplyPhase(Camp/ExpeditionDeep/Evening) turns them on
        };
        AddChild(wisp);
        _fogWisps.Add(wisp);
    }

    private static Gradient FadeRamp(Color start, Color end) => new() { Colors = [start, end], Offsets = [0f, 1f] };

    /// <summary>Soft warm radial falloff for window glow / the forge-coals light — same recipe
    /// as <see cref="LitTownOverlay"/>'s pilot gradient, tinted warm-amber for the glow sprite.</summary>
    private static GradientTexture2D BuildGlowGradient()
    {
        var gradient = new Gradient
        {
            Colors =
            [
                WindowGlowColor,
                new Color(WindowGlowColor.R, WindowGlowColor.G, WindowGlowColor.B, 0.35f),
                new Color(WindowGlowColor.R, WindowGlowColor.G, WindowGlowColor.B, 0f),
            ],
            Offsets = [0f, 0.5f, 1f],
        };
        return new GradientTexture2D
        {
            Gradient = gradient,
            Width = 256,
            Height = 256,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(1f, 0.5f),
        };
    }

    /// <summary>Large, soft, neutral radial falloff for the fog wisps — <see cref="FogAlpha"/> on
    /// the sprite's Modulate is the ONLY opacity control (the gradient itself peaks at full alpha)
    /// so the ~0.12 figure in the plan is the actual on-screen peak, not a compounded guess.</summary>
    private static GradientTexture2D BuildFogGradient()
    {
        var gradient = new Gradient
        {
            Colors = [new Color(1f, 1f, 1f, 1f), new Color(1f, 1f, 1f, 0f)],
            Offsets = [0f, 1f],
        };
        return new GradientTexture2D
        {
            Gradient = gradient,
            Width = 512,
            Height = 256,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(1f, 0.5f),
        };
    }
}
