using System.Collections.Generic;
using GameSim.Contracts;
using Godot;

namespace GodotClient.Town;

/// <summary>
/// THE town (U14 promotion — KTD1: "promote, don't rebuild"). Through U13 this was a purely
/// decorative backdrop (a non-input <see cref="SubViewport"/> painted world sitting behind the
/// real, <c>Control</c>-based, click-driven SVG scaffold in <see cref="TownScene"/>). U14 flips
/// the polarity: this <see cref="SubViewport"/> world is now input-forwarding, Y-sorted
/// (<see cref="_ents"/>), and the ONLY town — the SVG scaffold (invisible since U3) is deleted.
/// World-space pixel constants are published in <c>docs/design/world-scale.md</c>; read that
/// before touching any position/size constant below.
///
/// <para><see cref="TownScene"/> still owns the sim-driven bits (hero lifecycle, speech bubbles,
/// memorial plot, phase choreography) and reaches into this class's <see cref="Ents"/> layer to
/// add/remove them — this class owns the WORLD ITSELF: the ground, the four building facades
/// (feet-anchored per KTD6, each with a <see cref="StaticBody2D"/> base collider and — for the
/// three interactable ones — an <see cref="Area2D"/> click zone), the ambient tint (SOLE town-wide
/// authority since U3), the atmosphere fx layer, and the camera.</para>
///
/// <para>Input: G1 (BOARD) verdict was NO for headless Area2D physics picking under gdUnit4Net —
/// engine-side clicks are proven only by <c>UiTestSupport.ManualSmokeRecipe</c>, never by CI.
/// Tests drive <see cref="BuildingClicked"/> via <c>UiTestSupport.TryClickArea</c> against each
/// building's click-zone <see cref="Area2D"/> directly, bypassing the (unproven-under-CI) picking
/// pass — production code does not need to know the difference.</para>
///
/// <para>Adapter-free: <see cref="ApplyPhase"/> (called by <see cref="TownScene"/>, which owns the
/// adapter) fans the phase out to <see cref="Fx"/>'s discrete per-phase looks; <see cref="Ambient"/>'s
/// own color is the SOLE town-wide tint authority (U3), written by <see cref="TownScene"/>'s LW1
/// crossfade rather than snapped here.</para>
///
/// <para>LW6 camera feel: <see cref="Camera"/> — now the operative camera for the whole Town tab,
/// not merely a decorative SubViewport's own — carries a barely-conscious idle drift
/// (<see cref="DriftOffsetFor"/>), scoped to this SubViewport via <see cref="Camera2D.MakeCurrent"/>.
/// Mouse parallax (<see cref="ApplyParallax"/>) nudges only <see cref="Fx"/> (an idle depth cue) —
/// never <see cref="Ents"/>, whose building colliders/click-zones must never drift out of sync
/// with their own visuals.</para>
/// </summary>
public partial class LitTownOverlay : SubViewportContainer
{
    /// <summary>
    /// A building: its node/asset key, <see cref="IconRegistry.Lit"/> art id, the click-routing
    /// string <see cref="MainUi"/> switches on (null = not clickable, e.g. the mine gate), its
    /// world-space GROUND-LINE anchor point (KTD6 — bottom-center of the facade, NOT its visual
    /// center), and its lantern's offset from that same ground-line point.
    /// </summary>
    public readonly record struct BuildingSpec(
        string Key, string LitId, string? ClickKey, Vector2 Position, Vector2 LightOffset);

    // ── Pilot-approved light params (lit_tavern_pilot.tscn) ───────────────────────────────────
    private static readonly Color LightColor = new(1f, 0.75f, 0.45f); // warm lantern
    private const float LightEnergy = 1.2f;
    private const float LightTextureScale = 2.0f;
    private const float LightHeight = 30.0f;
    private const float FlickerAmplitude = 0.15f; // subtle ember wobble around LightEnergy

    /// <summary>World design space — see <c>docs/design/world-scale.md</c> "World container".</summary>
    public static readonly Vector2I DesignSize = new(1600, 700);

    /// <summary>Shared street baseline every facade's feet anchor sits on (world-scale doc).</summary>
    public const float GroundLine = 480f;

    // Native building art runs much wider than its target read width; scale down to it.
    private const float BuildingTargetWidth = 300f;

    /// <summary>Small physical footprint under each facade (KTD1 item 8) — present regardless of
    /// which building it is; no avatar exists yet to collide with it (U20), but the structure is
    /// load-bearing for that unit rather than invented fresh there.</summary>
    private static readonly Vector2 BaseColliderSize = new(240f, 40f);
    private static readonly Vector2 BaseColliderOffset = new(0f, -20f);

    /// <summary>Click-zone footprint (world-scale doc "Click zones") — big enough to read as the
    /// whole facade, inset enough that neighboring buildings' zones (400+ px pitch, 300px wide
    /// facades) can never touch.</summary>
    private static readonly Vector2 ClickZoneSize = new(260f, 340f);
    private static readonly Vector2 ClickZoneOffset = new(0f, -170f);

    /// <summary>The shipped 4 buildings — world-scale doc's table (feet-anchored, re-spaced so the
    /// 400+ px pitch structurally cannot overlap the ≈300px facade width, unlike U3's 230px-pitch
    /// interim fix).</summary>
    public static readonly BuildingSpec[] DefaultBuildings =
    [
        new("forge", "town-forge", "Forge", new Vector2(260, GroundLine), new Vector2(-30, -260)),
        new("market", "town-market", "Shop", new Vector2(680, GroundLine), new Vector2(30, -260)),
        new("tavern", "town-tavern", "Tavern", new Vector2(1100, GroundLine), new Vector2(-30, -260)),
        new("minegate", "town-mine-gate", null, new Vector2(1440, GroundLine), new Vector2(-20, -260)),
    ];

    // ── LW6: camera drift + Fx-only mouse parallax ────────────────────────────────────────────
    private const float DriftFreqX = 0.10f;
    private const float DriftFreqY = 0.13f;
    private const float DriftAmplitude = 4f; // px

    // U14: the old multi-layer parallax (buildings/decor-heroes/fx, each at its own factor) lost
    // its buildings/decor-heroes targets — buildings now live in the Y-sorted Ents (a parallax
    // nudge would desync their StaticBody2D/Area2D from their own sprite) and the decorative
    // hero figures this used to offset are deleted (real HeroSprites fill that role now). Only
    // the atmosphere fx layer keeps an idle parallax cue.
    private const float ParallaxFactorFx = 0.03f;
    private const float ParallaxLerpSpeed = 6f; // per-second convergence toward the target offset

    private readonly List<PointLight2D> _lights = [];
    private readonly List<Sprite2D> _sprites = [];
    private SubViewport _viewport = null!;
    private Node2D _world = null!;
    private Node2D _ents = null!;
    private Camera2D _camera = null!;
    private CanvasModulate _ambient = null!;
    private GradientTexture2D _lightGradient = null!;
    private AmbientFxLayer _fx = null!;
    private float _time;
    private Vector2 _parallaxTarget;
    private Vector2 _parallaxCurrent;
    private bool _built;

    /// <summary>A building marker was clicked — payload matches <see cref="BuildingSpec.ClickKey"/>
    /// ("Forge" | "Shop" | "Tavern"); the mine gate never raises this (parity with pre-U14).</summary>
    public event System.Action<string>? BuildingClicked;

    /// <summary>The lit world's ambient MULTIPLY tint node (the SubViewport-scoped CanvasModulate).</summary>
    public CanvasModulate Ambient => _ambient;

    /// <summary>The Node2D holding the ground, entities, and fx.</summary>
    public Node2D World => _world;

    /// <summary>
    /// The Y-sorted (<see cref="CanvasItem.YSortEnabled"/>) entity layer — <see cref="TownScene"/>
    /// adds/removes its live <see cref="HeroSprite"/> instances (and their speech bubbles) here
    /// directly, so they draw correctly in front of/behind the building wrappers that are this
    /// layer's other direct children. <see cref="CanvasItem.YSortEnabled"/> lives on the
    /// CanvasItem base (not just Node2D), so a <c>Control</c>-based <see cref="HeroSprite"/> sorts
    /// correctly here too (U19 promotes it to a Node2D <c>HeroActor</c>; U14 does not need to).
    /// </summary>
    public Node2D Ents => _ents;

    /// <summary>Per-building warm lights (for tests / tuning).</summary>
    public IReadOnlyList<PointLight2D> Lights => _lights;

    /// <summary>LW4 atmosphere layer (window glow, forge-coals landmark, particles, props, fog) —
    /// mounted as a child of <see cref="World"/>, drawn ON TOP of <see cref="Ents"/> (never
    /// Y-sorted against actors), phase-driven from <see cref="ApplyPhase"/>.</summary>
    public AmbientFxLayer Fx => _fx;

    /// <summary>The operative Camera2D for the whole Town tab (LW6: idle drift only in U14 — no
    /// follow target until U20's avatar lands).</summary>
    public Camera2D Camera => _camera;

    /// <summary>True once at least one building's art resolved — lets a caller detect a fully
    /// asset-less degrade (every id missing).</summary>
    public bool HasContent => _sprites.Count > 0;

    /// <summary>Build the live town with the shipped 4 buildings.</summary>
    public void Build() => Build(DefaultBuildings);

    /// <summary>
    /// Build the SubViewport world from the given building specs. Injectable so tests can
    /// exercise the graceful-degrade path (a fake id yields no facade/collider/click-zone at
    /// all — same "nothing there" contract this class has always used). Idempotent-guarded.
    /// </summary>
    public void Build(IReadOnlyList<BuildingSpec> buildings)
    {
        if (_built)
        {
            return;
        }

        Name = "LitTownOverlay";
        Stretch = true; // SubViewportContainer tracks this container's pixel rect 1:1

        // U14 KTD1 item 1: flip from decorative (never eats a click) to input-forwarding — this
        // viewport IS the town's input surface now. G1 (BOARD): headless Area2D picking does not
        // fire under gdUnit4Net regardless of this flag; real verification is the manual-smoke
        // recipe (UiTestSupport.ManualSmokeRecipe), never CI.
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsPreset(LayoutPreset.FullRect);

        _viewport = new SubViewport
        {
            Name = "TownViewport",
            Size = DesignSize,
            HandleInputLocally = true, // U14: was false (the decorative-era "SubViewport trap")
            TransparentBg = true,      // only the painted props draw; nothing to show through today
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        AddChild(_viewport);

        _world = new Node2D { Name = "WorldRoot" };
        _viewport.AddChild(_world);

        // LW6: the camera is now load-bearing (not merely scoped to a decorative backdrop) —
        // MakeCurrent() itself is deferred to _Ready (Build() runs before this overlay is added
        // as TownScene's child, so is_inside_tree() is false here).
        _camera = new Camera2D
        {
            Name = "TownCamera",
            AnchorMode = Camera2D.AnchorModeEnum.FixedTopLeft,
            Position = Vector2.Zero,
        };
        _viewport.AddChild(_camera);

        // Phase ambience: a CanvasModulate MULTIPLIES the world only (SubViewport-isolated from
        // the surrounding TabContainer UI). Sole town-wide tint authority since U3.
        _ambient = new CanvasModulate { Name = "AmbientTint", Color = AtmosphereTintFor(DayPhase.Morning) };
        _world.AddChild(_ambient);

        _lightGradient = BuildLightGradient();

        BuildGround();

        // U14 KTD1 item 2: the ONE Y-sort group — building wrappers and (added by TownScene) live
        // HeroSprites are direct children, sorted by world Y at draw time.
        _ents = new Node2D { Name = "Ents", YSortEnabled = true };
        _world.AddChild(_ents);

        foreach (var building in buildings)
        {
            TryAddBuilding(building);
        }

        // LW4 atmosphere layer: window glow, forge-coals landmark, particles, props, fog — added
        // LAST so it always draws on top of Ents, never Y-sorted against actors.
        _fx = new AmbientFxLayer();
        _world.AddChild(_fx);
        _fx.Build(buildings);
        _fx.ApplyPhase(DayPhase.Morning);

        _built = true;
    }

    /// <summary>
    /// Two-temperature retune (LW4): the SOLE town-wide tint authority since U3 (<see
    /// cref="TownScene"/>'s own root Modulate stays pinned white). Morning/Expedition (daylight)
    /// are neutral/warm; Camp/ExpeditionDeep/Evening are deliberately cooler and more desaturated
    /// so the warm window-glow sprites and forge coals (<see cref="AmbientFxLayer"/>) visibly pop
    /// against a colder dusk/night sky. Never below the crush point (the darkest stop still reads
    /// as navy, not black).
    /// </summary>
    public static Color AtmosphereTintFor(DayPhase phase) => phase switch
    {
        DayPhase.Morning => new Color(1.00f, 0.92f, 0.78f),
        DayPhase.Expedition => new Color(1.00f, 1.00f, 1.00f),
        DayPhase.Camp => new Color(0.59f, 0.66f, 0.78f),          // dusk
        DayPhase.ExpeditionDeep => new Color(0.42f, 0.46f, 0.64f), // deepening
        DayPhase.Evening => new Color(0.30f, 0.32f, 0.55f),        // night — above the crush point
        _ => new Color(1.00f, 1.00f, 1.00f),
    };

    /// <summary>
    /// Fan the phase out to the fx layer (window glow / particles / fog — all discrete per-phase,
    /// not tweened). <see cref="Ambient"/>'s color is written by <see cref="TownScene"/>'s LW1
    /// crossfade, never snapped here. Called from <see cref="TownScene.Refresh"/> so the world
    /// tracks the sim.
    /// </summary>
    public void ApplyPhase(DayPhase phase) => _fx?.ApplyPhase(phase);

    /// <summary>LW6: activate the camera once this overlay (and everything <see cref="Build"/>
    /// built into it, camera included) is actually inside the live tree — <see
    /// cref="Camera2D.MakeCurrent"/> requires <c>is_inside_tree()</c>.</summary>
    public override void _Ready() => _camera?.MakeCurrent();

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

        // LW6 idle camera drift — barely-conscious sway.
        if (_camera is not null)
        {
            _camera.Offset = DriftOffsetFor(_time);
        }

        // LW6 mouse parallax — reads the container's own local mouse position (still polled while
        // input-forwarding; a click passes through to the SubViewport, this read never consumes
        // it).
        if (_fx is not null && Size.X > 0 && Size.Y > 0)
        {
            ApplyParallax(GetLocalMousePosition(), Size, (float)delta);
        }
    }

    /// <summary>LW6 idle drift (plan §LW6): a barely-conscious <see cref="Camera2D.Offset"/> sway,
    /// pure function of accumulated time — no wall clock, no RNG (KTD2), testable without a live
    /// SubViewport. Bounded to ±<see cref="DriftAmplitude"/> px on both axes by construction.</summary>
    public static Vector2 DriftOffsetFor(float t) =>
        new(Mathf.Sin(t * DriftFreqX) * DriftAmplitude, Mathf.Cos(t * DriftFreqY) * DriftAmplitude);

    /// <summary>
    /// LW6 mouse parallax (plan §LW6), U14-narrowed to <see cref="Fx"/> only: offsets the
    /// atmosphere layer — NOT the camera, NOT <see cref="Ents"/> — by <c>(mouse - center)</c>
    /// converted from the container's on-screen pixels into the SubViewport's DESIGN-space
    /// pixels, scaled by <see cref="ParallaxFactorFx"/> and lerped toward the target so a mouse
    /// jump reads as a smooth settle, not a snap. Public + delta-parameterized so tests can drive
    /// it with a synthetic mouse position/container size without a real cursor.
    /// </summary>
    public void ApplyParallax(Vector2 mouseLocal, Vector2 containerSize, float delta)
    {
        var designScale = new Vector2(DesignSize.X / containerSize.X, DesignSize.Y / containerSize.Y);
        _parallaxTarget = (mouseLocal - containerSize / 2f) * designScale;

        var lerpAmount = Mathf.Clamp(ParallaxLerpSpeed * delta, 0f, 1f);
        _parallaxCurrent = _parallaxCurrent.Lerp(_parallaxTarget, lerpAmount);

        _fx.Position = _parallaxCurrent * ParallaxFactorFx;
    }

    /// <summary>Tiled ground layer (KTD1 item 9 replacement: the old Control-based tiled
    /// TextureRect lived in <see cref="TownScene"/>, behind this decorative backdrop; promotion
    /// moves ground rendering into the world itself). Graceful-degrade: an unresolved
    /// <c>ground_tile</c> hand-authored SVG (should never happen — it ships in the repo, not the
    /// generated-art pipeline) simply leaves an empty layer, never a crash.</summary>
    private void BuildGround()
    {
        var ground = new Node2D { Name = "GroundLayer" };
        _world.AddChild(ground);

        var texture = IconRegistry.Building("ground_tile");
        if (texture is null)
        {
            return;
        }

        var tileWidth = Mathf.Max(1, texture.GetWidth());
        var tileHeight = Mathf.Max(1, texture.GetHeight());
        var cols = Mathf.CeilToInt(DesignSize.X / (float)tileWidth) + 1;
        var rows = Mathf.CeilToInt(DesignSize.Y / (float)tileHeight) + 1;
        for (var y = 0; y < rows; y++)
        {
            for (var x = 0; x < cols; x++)
            {
                ground.AddChild(new Sprite2D
                {
                    Name = $"Tile_{x}_{y}",
                    Texture = texture,
                    Centered = false,
                    Position = new Vector2(x * tileWidth, y * tileHeight),
                });
            }
        }
    }

    /// <summary>
    /// Build one building: a Node2D wrapper positioned at the ground-line anchor (KTD1 items 3/5/
    /// 6/8), holding a feet-anchored facade <see cref="Sprite2D"/>, its lantern, a
    /// <see cref="StaticBody2D"/> base collider, and — when <see cref="BuildingSpec.ClickKey"/> is
    /// set — an <see cref="Area2D"/> click zone raising <see cref="BuildingClicked"/>. Graceful
    /// degrade preserved exactly: no resolved art means nothing is built at all (no orphan
    /// collider/click-zone/light), the same contract this class has always used.
    /// </summary>
    private void TryAddBuilding(BuildingSpec spec)
    {
        var lit = IconRegistry.Lit(spec.LitId);
        if (lit is null)
        {
            return;
        }

        var wrapper = new Node2D { Name = $"Building_{spec.Key}", Position = spec.Position };
        _ents.AddChild(wrapper);

        var facade = new Sprite2D
        {
            Name = $"LitBuilding_{spec.Key}", // pre-U14 name kept — same discoverable id, new anchor contract
            Texture = lit,
            Centered = false, // KTD6: feet-anchored, not center-anchored
        };
        var scale = ScaleFactorFor(lit, BuildingTargetWidth);
        facade.Scale = Vector2.One * scale;
        var scaledHeight = (lit.DiffuseTexture?.GetHeight() ?? 0) * scale;
        var anchorOffset = AssetCatalog.FeetAnchorOffset(spec.LitId);
        facade.Position = new Vector2(-BuildingTargetWidth / 2f, -scaledHeight) + anchorOffset;
        wrapper.AddChild(facade);
        _sprites.Add(facade);

        var light = new PointLight2D
        {
            Name = "Lantern",
            Position = spec.LightOffset,
            Color = LightColor,
            Energy = LightEnergy,
            Texture = _lightGradient,
            TextureScale = LightTextureScale,
            Height = LightHeight,
        };
        wrapper.AddChild(light);
        _lights.Add(light);

        var body = new StaticBody2D { Name = "Base" };
        body.AddChild(new CollisionShape2D
        {
            Shape = new RectangleShape2D { Size = BaseColliderSize },
            Position = BaseColliderOffset,
        });
        wrapper.AddChild(body);

        if (spec.ClickKey is { } clickKey)
        {
            var area = new Area2D { Name = $"ClickZone_{spec.Key}" };
            area.AddChild(new CollisionShape2D
            {
                Shape = new RectangleShape2D { Size = ClickZoneSize },
                Position = ClickZoneOffset,
            });
            area.InputEvent += (_, @event, _) =>
            {
                if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
                {
                    BuildingClicked?.Invoke(clickKey);
                }
            };
            wrapper.AddChild(area);
        }
    }

    /// <summary>Uniform scale factor so a texture's diffuse renders at <paramref
    /// name="targetWidth"/> px wide (0 when the width can't be read — leaves Scale at the
    /// caller's own default rather than dividing by zero).</summary>
    private static float ScaleFactorFor(CanvasTexture lit, float targetWidth)
    {
        var width = lit.DiffuseTexture?.GetWidth() ?? 0;
        return width > 0 ? targetWidth / width : 1f;
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
