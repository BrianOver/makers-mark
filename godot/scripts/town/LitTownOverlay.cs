using System.Collections.Generic;
using GameSim.Contracts;
using Godot;

namespace GodotClient.Town;

/// <summary>
/// The 2.5D lit town backdrop (V-lit-overlay, plan 2026-07-17-003 CP-1 option (c): an ADDITIVE
/// overlay — no <c>town_scene.tscn</c> surgery, no editor). Built entirely in code (CLAUDE.md
/// rule 2: never author .tscn metadata with a non-pinned editor), this is the V4a "SubViewport
/// trap" made live: a <see cref="SubViewport"/> world whose <see cref="CanvasModulate"/> tints
/// ONLY the lit world (not the surrounding TabContainer UI), holding the normal-mapped building /
/// hero <see cref="CanvasTexture"/>s, one warm <see cref="PointLight2D"/> per building, and the
/// approved <see cref="LitTavernPilot"/> look (gradient/height/energy/warm color + ember flicker).
///
/// <para>Mounted by <see cref="TownScene"/> as its backmost visual layer (behind the SVG facades /
/// labels / hero markers, on top of the cobble ground). <see cref="MouseFilterEnum.Ignore"/> + a
/// non-input SubViewport means it never intercepts a click, so every TownScene routing/label test
/// stays green. Graceful degrade: any asset whose <see cref="IconRegistry.Lit"/> is null simply
/// does not appear — no sprite, no light, never a crash — so the SVG town survives on its own.</para>
///
/// <para>Adapter-free: the phase tint is pushed in via <see cref="ApplyPhase"/> by TownScene (which
/// owns the adapter), and the flicker is pure presentation (accumulated frame time → Mathf.Sin, no
/// sim contact, no RNG — same contract as the pilot and <see cref="HeroSprite"/>).</para>
/// </summary>
public partial class LitTownOverlay : SubViewportContainer
{
    /// <summary>A lit building: node key + its <see cref="IconRegistry.Lit"/> art id, world
    /// position (echoing the Control town's building layout), and its light's offset.</summary>
    public readonly record struct BuildingSpec(string Key, string LitId, Vector2 Position, Vector2 LightOffset);

    /// <summary>A lit hero figure: class id (its lit art id is <c>hero-{ClassId}</c>) and world
    /// position. The Sprite2D is MULTIPLY-tinted by the class ColorRgb (neutral-base design).</summary>
    public readonly record struct HeroSpec(string ClassId, string LitId, Vector2 Position);

    // ── Pilot-approved light params (lit_tavern_pilot.tscn) ───────────────────────────────────
    private static readonly Color LightColor = new(1f, 0.75f, 0.45f); // warm lantern
    private const float LightEnergy = 1.2f;
    private const float LightTextureScale = 2.0f;
    private const float LightHeight = 30.0f;
    private const float FlickerAmplitude = 0.15f; // subtle ember wobble around LightEnergy

    private static readonly Vector2I DesignSize = new(1024, 600);
    private const float BuildingTargetWidth = 220f; // native art is ~1024px+; scale to read as a facade
    private const float HeroTargetWidth = 90f;

    /// <summary>Building layout echoing <see cref="TownScene"/>'s SVG anchors (Forge/Shop/Tavern/gate),
    /// dropped a little lower so the lit facades read as a street BEHIND the crisp SVG markers.</summary>
    public static readonly BuildingSpec[] DefaultBuildings =
    [
        new("forge", "town-forge", new Vector2(468, 230), new Vector2(-24, -110)),
        new("market", "town-market", new Vector2(608, 230), new Vector2(24, -110)),
        new("tavern", "town-tavern", new Vector2(748, 230), new Vector2(-24, -110)),
        new("minegate", "town-mine-gate", new Vector2(914, 320), new Vector2(-16, -120)),
    ];

    /// <summary>Three hero figures in the town square, one per built-in class.</summary>
    public static readonly HeroSpec[] DefaultHeroes =
    [
        new("vanguard", "hero-vanguard", new Vector2(470, 380)),
        new("striker", "hero-striker", new Vector2(600, 400)),
        new("mystic", "hero-mystic", new Vector2(720, 380)),
    ];

    private readonly List<PointLight2D> _lights = [];
    private readonly List<Sprite2D> _sprites = [];
    private SubViewport _viewport = null!;
    private Node2D _world = null!;
    private CanvasModulate _ambient = null!;
    private GradientTexture2D _lightGradient = null!;
    private float _time;
    private bool _built;

    /// <summary>The lit world's ambient MULTIPLY tint node (the SubViewport-scoped CanvasModulate).</summary>
    public CanvasModulate Ambient => _ambient;

    /// <summary>The Node2D holding the lit buildings, heroes, and lights.</summary>
    public Node2D World => _world;

    /// <summary>Per-building warm lights (for tests / tuning).</summary>
    public IReadOnlyList<PointLight2D> Lights => _lights;

    /// <summary>True once at least one lit sprite resolved — lets TownScene skip the backdrop
    /// entirely (and any veil) when every asset is absent, so the SVG town is untouched.</summary>
    public bool HasContent => _sprites.Count > 0;

    /// <summary>Build the live backdrop with the shipped 4 buildings + 3 hero figures.</summary>
    public void Build() => Build(DefaultBuildings, DefaultHeroes);

    /// <summary>
    /// Build the SubViewport world from the given specs. Injectable so tests can exercise the
    /// graceful-degrade path (a fake id yields no sprite). Idempotent-guarded.
    /// </summary>
    public void Build(IReadOnlyList<BuildingSpec> buildings, IReadOnlyList<HeroSpec> heroes)
    {
        if (_built)
        {
            return;
        }

        Name = "LitTownOverlay";
        Stretch = true;                        // SubViewport tracks this container's pixel rect 1:1
        MouseFilter = MouseFilterEnum.Ignore;  // never eat a click — the SVG town on top owns input
        SetAnchorsPreset(LayoutPreset.FullRect);

        _viewport = new SubViewport
        {
            Name = "LitViewport",
            Size = DesignSize,
            HandleInputLocally = false,        // the "SubViewport trap": no input handling here
            TransparentBg = true,              // only the lit props draw; the cobble ground shows through gaps
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        AddChild(_viewport);

        _world = new Node2D { Name = "LitWorld" };
        _viewport.AddChild(_world);

        // Phase ambience: a CanvasModulate MULTIPLIES the lit world only (the SubViewport isolates
        // it from the TabContainer UI). Starts warm (Morning) to match TownScene's own initial tint.
        _ambient = new CanvasModulate { Name = "AmbientTint", Color = TownScene.TintFor(DayPhase.Morning) };
        _world.AddChild(_ambient);

        _lightGradient = BuildLightGradient();

        foreach (var building in buildings)
        {
            TryAddBuilding(building);
        }

        foreach (var hero in heroes)
        {
            TryAddHero(hero);
        }

        _built = true;
    }

    /// <summary>Push the phase tint onto the lit world's CanvasModulate (same 5-phase table as
    /// TownScene). Called from <see cref="TownScene.Refresh"/> so the lit backdrop tracks the sim.</summary>
    public void ApplyPhase(DayPhase phase)
    {
        if (_ambient is not null)
        {
            _ambient.Color = TownScene.TintFor(phase);
        }
    }

    public override void _Process(double delta)
    {
        // Ember flicker: subtle deterministic wobble (presentation only — no sim contact, no RNG),
        // per the pilot. A tiny per-light phase offset stops the lanterns pulsing in unison.
        _time += (float)delta;
        for (var i = 0; i < _lights.Count; i++)
        {
            var phase = _time * 9f + i * 1.7f;
            _lights[i].Energy = LightEnergy + FlickerAmplitude * Mathf.Sin(phase) * Mathf.Sin(_time * 2.3f);
        }
    }

    private void TryAddBuilding(BuildingSpec spec)
    {
        var lit = IconRegistry.Lit(spec.LitId);
        if (lit is null)
        {
            return; // graceful degrade — no diffuse means no sprite AND no orphan light
        }

        var sprite = new Sprite2D
        {
            Name = $"LitBuilding_{spec.Key}",
            Texture = lit,
            Position = spec.Position,
        };
        ScaleToWidth(sprite, lit, BuildingTargetWidth);
        _world.AddChild(sprite);
        _sprites.Add(sprite);

        var light = new PointLight2D
        {
            Name = $"LitLight_{spec.Key}",
            Position = spec.Position + spec.LightOffset,
            Color = LightColor,
            Energy = LightEnergy,
            Texture = _lightGradient,
            TextureScale = LightTextureScale,
            Height = LightHeight,
        };
        _world.AddChild(light);
        _lights.Add(light);
    }

    private void TryAddHero(HeroSpec spec)
    {
        var lit = IconRegistry.Lit(spec.LitId);
        if (lit is null)
        {
            return; // graceful degrade
        }

        var sprite = new Sprite2D
        {
            Name = $"LitHero_{spec.ClassId}",
            Texture = lit,
            Position = spec.Position,
            // Neutral-base multiply: tint the figure to its class ColorRgb (ClassRegistry via
            // HeroSprite.RoleColor — the same contract the SVG hero marker asserts).
            Modulate = HeroSprite.RoleColor(spec.ClassId),
        };
        ScaleToWidth(sprite, lit, HeroTargetWidth);
        _world.AddChild(sprite);
        _sprites.Add(sprite);
    }

    /// <summary>Scale a lit Sprite2D so its diffuse renders at <paramref name="targetWidth"/> px.</summary>
    private static void ScaleToWidth(Sprite2D sprite, CanvasTexture lit, float targetWidth)
    {
        var width = lit.DiffuseTexture?.GetWidth() ?? 0;
        if (width > 0)
        {
            sprite.Scale = Vector2.One * (targetWidth / width);
        }
    }

    /// <summary>The pilot's radial falloff (Gradient_light + GradientTexture2D_light in
    /// lit_tavern_pilot.tscn): white core → 0.45 alpha at 0.55 → transparent edge, radial fill.</summary>
    private static GradientTexture2D BuildLightGradient()
    {
        var gradient = new Gradient
        {
            Colors = [new Color(1, 1, 1, 1), new Color(1, 1, 1, 0.45f), new Color(1, 1, 1, 0)],
            Offsets = [0f, 0.55f, 1f],
        };
        return new GradientTexture2D
        {
            Gradient = gradient,
            Width = 512,
            Height = 512,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(1f, 0.5f),
        };
    }
}
