using System.Collections.Generic;
using System.Linq;
using Godot;

namespace GodotClient.Town2d;

/// <summary>
/// Presentation-only ambient life for the 2.5D town (zero sim impact, KTD2/KTD4/KTD5): chimney
/// smoke drifting up off the forge (and, fainter, the tavern), a wide field of warm ember/teal
/// firefly motes drifting across the town at dusk, and a subtle per-lamppost warm-glow flicker so
/// the lanterns <see cref="TownLayout2D.Props"/> already places read as genuinely lit. This is the
/// 2D twin of <c>town3d.AmbientLife</c> — same intent (chimney smoke + fireflies), ported to
/// <see cref="CpuParticles2D"/>/<see cref="Sprite2D"/> and widened with the lamp-flicker pass the
/// 3D version didn't have. Every emitter/sprite here is decoration only: no collider, no sim read,
/// no RNG outside Godot's own particle scatter and the <see cref="_Process"/> sine flicker (both
/// pure cosmetic per the pivot plan's "particles + <c>_Process</c> flicker are fine" carve-out).
///
/// <para><see cref="Build"/> is null-tolerant on every position argument — an absent tavern, or an
/// empty lantern list, simply skips that group rather than throwing, so a thinner layout (or a
/// future venue rename) never crashes town construction.</para>
/// </summary>
public partial class AmbientLife2D : Node2D
{
    private const int SmokeAmount = 9;
    private const double SmokeLifetime = 5.5;

    private const float LampBaseAlpha = 0.55f;
    private const float LampFlickerAmplitude = 0.12f;
    private const float LampFlickerSpeed = 1.7f; // rad/sec

    private static GradientTexture2D? _lampGlowTextureCache;

    private readonly List<(Sprite2D Sprite, float Phase)> _lampSprites = new();
    private float _elapsed;

    /// <summary>
    /// Builds the three ambient groups (ChimneySmoke / Fireflies / LampGlow) as children of this
    /// node. Caller (<c>Town2D.Build</c>) is expected to add this node under <c>World</c> — ABOVE
    /// the Y-sorted buildings/heroes layer in child order (or at a high enough <see
    /// cref="CanvasItem.ZIndex"/>) so smoke/fireflies drift in front, and never under <c>YSort</c>
    /// itself (this layer has no actor to sort against; mixing it into <c>YSort</c> would just risk
    /// fighting the building/hero depth illusion for no benefit).
    /// </summary>
    /// <param name="forgeChimneyPos">World position for the forge's chimney smoke emitter (Town2D
    /// already computes the forge building's world position for its own FX wiring — reuse it).</param>
    /// <param name="tavernChimneyPos">Optional fainter second chimney (tavern). Null skips it.</param>
    /// <param name="townRect">The town's world-space extent (grid width/height × tile size) the
    /// firefly field drifts across.</param>
    /// <param name="lanternPositions">World positions of every lamppost prop to flicker-glow. May
    /// be null or empty — degrades to no lamp glows, no crash.</param>
    public void Build(
        Vector2 forgeChimneyPos,
        Vector2? tavernChimneyPos,
        Rect2 townRect,
        IReadOnlyList<Vector2>? lanternPositions)
    {
        var smokeGroup = new Node2D { Name = "ChimneySmoke" };
        AddChild(smokeGroup);
        smokeGroup.AddChild(BuildSmokePuff("ForgeChimney", forgeChimneyPos, faint: false));
        if (tavernChimneyPos is { } tavernPos)
        {
            smokeGroup.AddChild(BuildSmokePuff("TavernChimney", tavernPos, faint: true));
        }

        var fireflyGroup = new Node2D { Name = "Fireflies" };
        AddChild(fireflyGroup);
        fireflyGroup.AddChild(BuildFireflyField("EmberMotes", townRect, new Color(1f, 0.55f, 0.2f, 0.85f)));
        fireflyGroup.AddChild(BuildFireflyField("TealMotes", townRect, new Color(0.35f, 0.9f, 0.85f, 0.7f)));

        var lampGroup = new Node2D { Name = "LampGlow" };
        AddChild(lampGroup);
        _lampSprites.Clear();
        if (lanternPositions is { Count: > 0 })
        {
            for (var i = 0; i < lanternPositions.Count; i++)
            {
                var sprite = new Sprite2D
                {
                    Name = $"Lamp_{i}",
                    Texture = LampGlowTexture(),
                    Centered = true,
                    Position = lanternPositions[i] + new Vector2(0f, -14f), // near the post's lamp head
                    Modulate = new Color(1f, 0.78f, 0.4f, LampBaseAlpha),
                    Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add },
                };
                lampGroup.AddChild(sprite);
                _lampSprites.Add((sprite, i * 0.9f)); // phase-offset so lamps don't pulse in lockstep
            }
        }
    }

    /// <summary>Sine-flickers each lamp glow's alpha around <see cref="LampBaseAlpha"/> — pure
    /// accumulated-delta cosmetic (no wall-clock read, KTD4/KTD5), a no-op with zero lamps.</summary>
    public override void _Process(double delta)
    {
        if (_lampSprites.Count == 0)
        {
            return;
        }

        _elapsed += (float)delta;
        foreach (var (sprite, phase) in _lampSprites)
        {
            var alpha = LampBaseAlpha + LampFlickerAmplitude * Mathf.Sin(_elapsed * LampFlickerSpeed + phase);
            var color = sprite.Modulate;
            color.A = alpha;
            sprite.Modulate = color;
        }
    }

    /// <summary>Test/inspection surface: live lamp-glow count (mirrors the count of positions
    /// <see cref="Build"/> was given).</summary>
    public int LampGlowCount() => _lampSprites.Count;

    private static CpuParticles2D BuildSmokePuff(string name, Vector2 pos, bool faint)
    {
        var color = faint
            ? new Color(0.75f, 0.75f, 0.78f, 0.18f)
            : new Color(0.75f, 0.75f, 0.78f, 0.3f);

        return new CpuParticles2D
        {
            Name = name,
            Position = pos,
            Emitting = true,
            OneShot = false,
            Amount = faint ? 6 : SmokeAmount,
            Lifetime = SmokeLifetime,
            Explosiveness = 0f,
            Randomness = 0.45f,
            EmissionShape = CpuParticles2D.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 2f,
            Direction = new Vector2(0.12f, -1f), // up, slight sideways lean
            Spread = 14f,
            Gravity = new Vector2(0f, -4f), // gentle continued lift (2D: negative Y is up)
            InitialVelocityMin = 3f,
            InitialVelocityMax = 7f,
            ScaleAmountMin = 0.8f,
            ScaleAmountMax = 1.6f,
            Color = color,
        };
    }

    /// <summary>A wide, continuously-emitting field of tiny warm motes spread across
    /// <paramref name="rect"/> — omnidirectional gentle bob (no directional stream), additive blend
    /// so they read as a soft glow rather than solid dots. Modest <c>Amount</c> per the plan's
    /// "readability over spectacle" note.</summary>
    private static CpuParticles2D BuildFireflyField(string name, Rect2 rect, Color color) => new()
    {
        Name = name,
        Position = rect.GetCenter(),
        Emitting = true,
        OneShot = false,
        Amount = 18,
        Lifetime = 8.0,
        Explosiveness = 0f,
        Randomness = 0.85f,
        EmissionShape = CpuParticles2D.EmissionShapeEnum.Rectangle,
        EmissionRectExtents = rect.Size / 2f,
        Direction = Vector2.Zero,
        Spread = 180f,
        Gravity = Vector2.Zero,
        InitialVelocityMin = 2f,
        InitialVelocityMax = 8f,
        ScaleAmountMin = 0.35f,
        ScaleAmountMax = 0.7f,
        Color = color,
        Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add },
    };

    /// <summary>Small radial white→transparent falloff for the lamp glow sprites — same recipe as
    /// <c>Town2D.GlowTexture</c> (kept as an independent copy rather than a cross-class reach-in,
    /// since this file owns its own presentation asset), cached process-wide, tinted at draw time
    /// via each <see cref="Sprite2D.Modulate"/>.</summary>
    private static GradientTexture2D LampGlowTexture() => _lampGlowTextureCache ??= new GradientTexture2D
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
