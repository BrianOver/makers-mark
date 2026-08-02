using System.Collections.Generic;
using System.Linq;
using GameSim.Contracts;
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
/// future venue rename) never crashes town construction. The same null-tolerance covers the
/// gap #1 venue cues below (market awning / mine-gate dust / noticeboard paper): any missing
/// position just skips that one cue.</para>
///
/// <para><b>Gap #1 fix ("Market, Mine-gate and Noticeboard buildings are completely dead")</b>:
/// three restrained, per-venue ambient cues, each appropriate to what the building IS rather than
/// a copy of the forge's own heat/spark/steam repertoire (the forge stays the liveliest thing in
/// town, per the fix's own restraint note): a market awning that sways in the wind (<see
/// cref="BuildMarketAwning"/>), a slow drift of dust from the mine gate's dark mouth (<see
/// cref="BuildMineDust"/>), and a noticeboard notice that flutters at its pinned corner (<see
/// cref="BuildNoticeboardPaper"/>). All three are procedural (flat-color textures/particles, same
/// idiom as this file's existing lamp-glow/smoke-puff recipes) — no new art asset is invented.</para>
///
/// <para><b>U11 fix ("lamps glow at a fixed alpha all day, no window light, no darkness")</b>:
/// <see cref="SetPhase"/> feeds this node the sim's current <see cref="DayPhase"/> every frame
/// (mirrors how <see cref="DayPhaseTint"/> gets its phase from <c>Town2D._Process</c>) — lamp
/// glow now ramps from nearly invisible at Morning/"Dawn" up to a strong warm glow at
/// Evening/Camp/ExpeditionDeep ("Night"/"Vigil"/"Deep Vigil"), instead of sitting at one constant
/// alpha regardless of the clock. The new "WindowGlow" group <see cref="Build"/> creates applies
/// the exact same curve to a warm quad hand-placed at each of the five venues' own window/light
/// anchor (<see cref="LampAlphaFor"/> is the single source of truth both groups read), so the
/// town's darkened canvas reads as buildings with the lamps LIT rather than a uniformly dim
/// scene.</para>
/// </summary>
public partial class AmbientLife2D : Node2D
{
    private const int SmokeAmount = 9;
    private const double SmokeLifetime = 5.5;

    /// <summary>Pre-U11 constant alpha — kept only as the transient seed value <see cref="Build"/>
    /// paints on lamp/window sprites before the very first <see cref="SetPhase"/>/<see
    /// cref="_Process"/> call recomputes it off the real phase (mirrors <see
    /// cref="DayPhaseTint"/>'s "never start wrong for a frame" discipline in spirit, if not in
    /// mechanism — the caller is expected to call <see cref="SetPhase"/> right after <see
    /// cref="Build"/>, same as <c>Town2D.WireAmbientLife</c> does).</summary>
    private const float LampBaseAlpha = 0.55f;

    private const float LampFlickerAmplitude = 0.12f;
    private const float LampFlickerSpeed = 1.7f; // rad/sec

    /// <summary>U11 (KTD-6b) lamp/window glow curve stops — Morning/"Dawn" nearly dark (lamps were
    /// snuffed at first light), Expedition/"Quest" a faint daytime pilot glow, and
    /// Evening/Camp/ExpeditionDeep ("Night"/"Vigil"/"Deep Vigil") all share the same strong
    /// night-time band so every genuinely dark phase reads as genuinely lit.</summary>
    private const float MorningLampAlpha = 0.06f;

    private const float ExpeditionLampAlpha = 0.25f;
    private const float NightLampAlpha = 0.78f; // Evening/Camp/ExpeditionDeep — mid of the 0.7-0.85 band

    /// <summary>Market awning sway cadence/amplitude — a slow, gentle cloth-in-the-breeze read.</summary>
    private const float AwningSwayHz = 0.5f;

    private static readonly float AwningSwayAmplitudeRadians = 6f * Mathf.Pi / 180f;

    /// <summary>Noticeboard paper flutter — TWO layered sine frequencies (still fully
    /// deterministic, no RNG) so a pinned notice's corner reads as an irregular flutter rather than
    /// a metronome-smooth wobble.</summary>
    private const float PaperFlutterHzPrimary = 0.9f;

    private const float PaperFlutterHzSecondary = 2.3f;
    private static readonly float PaperFlutterAmplitudeRadians = 4f * Mathf.Pi / 180f;

    private const int MineDustAmount = 5;
    private const double MineDustLifetime = 4.5;

    private static GradientTexture2D? _lampGlowTextureCache;
    private static ImageTexture? _awningTextureCache;
    private static ImageTexture? _paperTextureCache;
    private static ImageTexture? _windowGlowTextureCache;

    private readonly List<(Sprite2D Sprite, float Phase)> _lampSprites = new();
    private readonly List<Sprite2D> _windowSprites = new();
    private float _elapsed;

    /// <summary>U11: the sim phase this frame — <see cref="SetPhase"/> is the only writer (called
    /// by <c>Town2D</c> every <c>_Process</c> tick, same cadence as <see
    /// cref="DayPhaseTint.Advance"/>). Defaults to <see cref="DayPhase.Expedition"/> (the
    /// pre-U11 lamp alpha's closest daytime neighbor) so a <see cref="Build"/> call with no
    /// follow-up <see cref="SetPhase"/> yet (e.g. a test that only wants to check flicker motion,
    /// not phase response) still flickers within a safe, non-zero, sub-1.0 range.</summary>
    private DayPhase _phase = DayPhase.Expedition;

    private Sprite2D? _marketAwning;
    private Sprite2D? _noticeboardPaper;

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
    /// <param name="marketAwningPos">Gap #1: world position for the market's swaying awning cue.
    /// Null skips it (e.g. a layout without a market).</param>
    /// <param name="mineDustPos">Gap #1: world position for the mine gate's drifting-dust cue at
    /// its mouth. Null skips it.</param>
    /// <param name="noticeboardPaperPos">Gap #1: world position for the noticeboard's fluttering-
    /// paper cue. Null skips it.</param>
    /// <param name="windowGlowPositions">U11: world positions for the warm window/light-source
    /// glow quad at each venue (hand-placed anchor per venue, computed by the caller off that
    /// building's own resolved sprite size — mirrors <paramref name="lanternPositions"/>'s "flat
    /// list of world positions" shape). May be null or empty — degrades to no window glows, no
    /// crash, same null-tolerance as every other optional group here.</param>
    public void Build(
        Vector2 forgeChimneyPos,
        Vector2? tavernChimneyPos,
        Rect2 townRect,
        IReadOnlyList<Vector2>? lanternPositions,
        Vector2? marketAwningPos = null,
        Vector2? mineDustPos = null,
        Vector2? noticeboardPaperPos = null,
        IReadOnlyList<Vector2>? windowGlowPositions = null)
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

        var windowGroup = new Node2D { Name = "WindowGlow" };
        AddChild(windowGroup);
        _windowSprites.Clear();
        if (windowGlowPositions is { Count: > 0 })
        {
            for (var i = 0; i < windowGlowPositions.Count; i++)
            {
                var sprite = new Sprite2D
                {
                    Name = $"WindowGlow_{i}",
                    Texture = WindowGlowTexture(),
                    Centered = true,
                    Position = windowGlowPositions[i],
                    Modulate = new Color(1f, 0.72f, 0.38f, LampBaseAlpha),
                    Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add },
                };
                windowGroup.AddChild(sprite);
                _windowSprites.Add(sprite);
            }
        }

        var cueGroup = new Node2D { Name = "VenueCues" };
        AddChild(cueGroup);

        _marketAwning = null;
        if (marketAwningPos is { } awningPos)
        {
            _marketAwning = BuildMarketAwning(awningPos);
            cueGroup.AddChild(_marketAwning);
        }

        if (mineDustPos is { } dustPos)
        {
            cueGroup.AddChild(BuildMineDust(dustPos));
        }

        _noticeboardPaper = null;
        if (noticeboardPaperPos is { } paperPos)
        {
            _noticeboardPaper = BuildNoticeboardPaper(paperPos);
            cueGroup.AddChild(_noticeboardPaper);
        }
    }

    /// <summary>Sine-flickers each lamp glow's alpha around the current phase's baseline (<see
    /// cref="LampAlphaFor"/>), does the exact same for every window glow, and (gap #1) sways the
    /// market awning / flutters the noticeboard paper — all pure accumulated-delta cosmetics (no
    /// wall-clock read, KTD4/KTD5). A no-op for whichever group has nothing built (e.g. zero
    /// lamps, or a layout that skipped a venue cue).</summary>
    public override void _Process(double delta)
    {
        _elapsed += (float)delta;

        var baseAlpha = LampAlphaFor(_phase);

        foreach (var (sprite, phase) in _lampSprites)
        {
            var alpha = baseAlpha + LampFlickerAmplitude * Mathf.Sin(_elapsed * LampFlickerSpeed + phase);
            var color = sprite.Modulate;
            color.A = alpha;
            sprite.Modulate = color;
        }

        // U11: windows follow the SAME curve as lamps (no independent flicker phase-offset needed
        // — a steady warm glow, unlike the lamp's per-post flicker, reads as light spilling through
        // glass rather than an open flame).
        foreach (var sprite in _windowSprites)
        {
            var color = sprite.Modulate;
            color.A = baseAlpha;
            sprite.Modulate = color;
        }

        if (_marketAwning is not null)
        {
            _marketAwning.Rotation = AwningSwayAmplitudeRadians * Mathf.Sin(_elapsed * AwningSwayHz * Mathf.Tau);
        }

        if (_noticeboardPaper is not null)
        {
            var flutter =
                Mathf.Sin(_elapsed * PaperFlutterHzPrimary * Mathf.Tau) +
                0.5f * Mathf.Sin(_elapsed * PaperFlutterHzSecondary * Mathf.Tau);
            _noticeboardPaper.Rotation = PaperFlutterAmplitudeRadians * (flutter / 1.5f); // normalize the 2-term sum back to [-1,1]
        }
    }

    /// <summary>U11: the ONLY writer of <see cref="_phase"/> — call every frame (mirrors <see
    /// cref="DayPhaseTint.Advance"/>'s per-tick contract) so lamp/window glow always answers the
    /// current sim phase, not a stale one from whenever <see cref="Build"/> ran.</summary>
    public void SetPhase(DayPhase phase) => _phase = phase;

    /// <summary>U11 (KTD-6b): the single source of truth both the lamp and window glow groups read
    /// — Morning/"Dawn" nearly snuffed, Expedition/"Quest" a faint daytime pilot glow,
    /// Evening/Camp/ExpeditionDeep ("Night"/"Vigil"/"Deep Vigil") all share the same strong
    /// night-time band. Pure function of the phase alone, so it is directly testable with no live
    /// node/scene tree (see <c>PhaseLightTests</c>).</summary>
    public static float LampAlphaFor(DayPhase phase) => phase switch
    {
        DayPhase.Morning => MorningLampAlpha,
        DayPhase.Expedition => ExpeditionLampAlpha,
        DayPhase.Evening or DayPhase.Camp or DayPhase.ExpeditionDeep => NightLampAlpha,
        _ => LampBaseAlpha,
    };

    /// <summary>Test/inspection surface: live lamp-glow count (mirrors the count of positions
    /// <see cref="Build"/> was given).</summary>
    public int LampGlowCount() => _lampSprites.Count;

    /// <summary>Test/inspection surface: live window-glow count (mirrors the count of positions
    /// <see cref="Build"/> was given via <c>windowGlowPositions</c>).</summary>
    public int WindowGlowCount() => _windowSprites.Count;

    /// <summary>Test/inspection surface: true once <see cref="Build"/> was given a non-null market
    /// awning position (gap #1).</summary>
    public bool HasMarketAwning => _marketAwning is not null;

    /// <summary>Test/inspection surface: true once <see cref="Build"/> was given a non-null
    /// noticeboard paper position (gap #1).</summary>
    public bool HasNoticeboardPaper => _noticeboardPaper is not null;

    /// <summary>Restrained "cloth stirring in the wind" cue for the market — a small rectangular
    /// swatch above the door, sine-swaying in <see cref="_Process"/> around its own top edge (like
    /// a banner pinned at the eave). Procedural flat-color texture, same idiom as <see
    /// cref="LampGlowTexture"/> — no new art asset invented.</summary>
    private static Sprite2D BuildMarketAwning(Vector2 pos) => new()
    {
        Name = "MarketAwning",
        Texture = AwningTexture(),
        Centered = true,
        Offset = new Vector2(0f, -4f), // pivot near the swatch's own top edge, not its center
        Position = pos,
        Modulate = new Color(0.74f, 0.34f, 0.24f, 0.95f), // warm terra-cotta cloth
    };

    /// <summary>Restrained "dust drifting from the mouth" cue for the mine gate — a slow, sparse
    /// particle trickle (same <see cref="CpuParticles2D"/> recipe family as <see
    /// cref="BuildSmokePuff"/>, muted dust color, gentle downward-and-out drift rather than a
    /// chimney's upward lift) so the gate reads as a dark opening breathing faint dust, not a
    /// second forge.</summary>
    private static CpuParticles2D BuildMineDust(Vector2 pos) => new()
    {
        Name = "MineDust",
        Position = pos,
        Emitting = true,
        OneShot = false,
        Amount = MineDustAmount,
        Lifetime = MineDustLifetime,
        Explosiveness = 0f,
        Randomness = 0.5f,
        EmissionShape = CpuParticles2D.EmissionShapeEnum.Sphere,
        EmissionSphereRadius = 10f, // spread across the gate's mouth width, not a single point
        Direction = new Vector2(0f, 1f), // settles/drifts down and out of the opening
        Spread = 30f,
        Gravity = new Vector2(0f, 2f),
        InitialVelocityMin = 1.5f,
        InitialVelocityMax = 4f,
        ScaleAmountMin = 0.5f,
        ScaleAmountMax = 1.1f,
        Color = new Color(0.42f, 0.38f, 0.34f, 0.35f), // muted dust brown-grey
    };

    /// <summary>Restrained "paper flutter" cue for the noticeboard — a small pale parchment
    /// swatch, rotation-flutters in <see cref="_Process"/> around its own pinned top corner (as if
    /// tacked up and catching a breeze). Procedural flat-color texture, same idiom as <see
    /// cref="LampGlowTexture"/>/<see cref="BuildMarketAwning"/> — no new art asset invented.</summary>
    private static Sprite2D BuildNoticeboardPaper(Vector2 pos) => new()
    {
        Name = "NoticeboardPaper",
        Texture = PaperTexture(),
        Centered = true,
        Offset = new Vector2(3f, -5f), // pivot near the pinned top-left corner, not the swatch's own center
        Position = pos,
        Modulate = new Color(0.88f, 0.83f, 0.68f, 0.95f), // pale parchment
    };

    private static ImageTexture AwningTexture() => _awningTextureCache ??= SolidRectTexture(20, 8);

    private static ImageTexture PaperTexture() => _paperTextureCache ??= SolidRectTexture(10, 14);

    /// <summary>U11: a small warm rectangle standing in for a lit window pane — same "flat
    /// procedural swatch, tinted at draw time" idiom as <see cref="AwningTexture"/>/<see
    /// cref="PaperTexture"/>, no new art asset invented. Sized to read as a modest window-glow
    /// patch rather than a floodlight, additive-blended (see <see cref="Build"/>) so it lights the
    /// painted wall around it instead of covering it.</summary>
    private static ImageTexture WindowGlowTexture() => _windowGlowTextureCache ??= SolidRectTexture(9, 7);

    /// <summary>A plain opaque-white rectangle — tinted at draw time via each sprite's own <see
    /// cref="Sprite2D.Modulate"/> (mirrors <see cref="LampGlowTexture"/>'s "cached, tinted at draw
    /// time" contract, just a flat swatch instead of a radial falloff).</summary>
    private static ImageTexture SolidRectTexture(int width, int height)
    {
        var image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
        image.Fill(Colors.White);
        return ImageTexture.CreateFromImage(image);
    }

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
