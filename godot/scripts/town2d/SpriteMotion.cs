using Godot;

namespace GodotClient.Town2d;

/// <summary>
/// M1 (2026-07-28 animation plan, Part 1): a plain, engine-free pose driver for a walking/idle
/// 2D character sprite. Follows the repo's accumulated-delta pure-class convention (mirrors
/// <c>JourneyPlayhead</c>: no scene/node/runtime dependency, only <see cref="Godot.Vector2"/>
/// value structs) — a caller (<c>HeroActor2D._Process</c> for M2, <c>PlayerController2D._Process</c>
/// for M3) feeds per-frame delta + velocity and applies the returned <see cref="Pose"/> to the
/// CHILD <c>Sprite2D</c>'s <c>Offset</c>/<c>Rotation</c>/<c>Scale</c> — never the actor's own
/// <c>Node2D.Position</c> (the feet/Y-sort baseline, per the plan's hard invariant).
///
/// <para>Deterministic by construction: same (phaseSeed, delta sequence, velocity sequence,
/// walkSpeed) always yields the same <see cref="Pose"/> sequence. No RNG, no wall-clock, no
/// <c>Godot.Time</c> — only the accumulated <c>delta</c> passed into <see cref="Advance"/>.</para>
/// </summary>
public sealed class SpriteMotion
{
    // ── Tuning constants (16px scale — plan §Part 1) ──────────────────────────────────────

    /// <summary>Below this speed (px/s), motion reads as idle regardless of nonzero velocity
    /// (hero wander drift stays under this, so it reads as an idle shuffle, not a walk).</summary>
    public const float WalkSpeedThreshold = 20f;

    /// <summary>Base footfall cadence at full pace (steps/sec) — scaled down by
    /// <c>speed/walkSpeed</c> so a slow-moving actor takes a slower, not just smaller, step.</summary>
    public const float StepHz = 3.2f;

    /// <summary>Walk bob amplitude in px.</summary>
    public const float BobAmplitude = 1.5f;

    /// <summary>Footfall squash/stretch scale applied briefly at each bob bottom.</summary>
    public static readonly Vector2 FootfallSquashScale = new(1.06f, 0.94f);

    /// <summary>How long (seconds) the footfall squash holds around each bob bottom — a "brief"
    /// snap, not a sustained squash. Expressed as an angular half-width on the bob's own sine
    /// argument (see <see cref="Advance"/>) so it scales correctly with cadence instead of
    /// staying a fixed wall-clock window regardless of step speed.</summary>
    public const float FootfallSquashWindowSeconds = 0.06f;

    /// <summary>Max lean at full pace, radians (~4 degrees).</summary>
    public const float LeanMaxRadians = 4f * Mathf.Pi / 180f;

    /// <summary>Idle breathing cadence (Hz).</summary>
    public const float BreathHz = 0.8f;

    /// <summary>Idle breathing amplitude, fraction of scale (±1.5%).</summary>
    public const float BreathAmplitude = 0.015f;

    /// <summary>
    /// One frame's computed sprite pose.
    ///
    /// <para><b>Feet-compensation contract — the CONSUMER's job, not this class's</b> (M2/M3
    /// must implement this when applying a <see cref="Pose"/> to a <c>Sprite2D</c>): the
    /// sprite's resting vertical offset is <c>-h/2</c> (h = the resolved texture's own height,
    /// see <c>HeroActor2D.BuildSprite</c>/<c>PlayerController2D.BuildSprite</c>).
    /// <list type="bullet">
    /// <item><description><c>Sprite.Offset.Y = -h/2 + BobY</c> — <see cref="BobY"/> is already
    /// expressed in the sprite-local "up is negative" convention, so it adds directly onto the
    /// existing feet-offset with no sign flip.</description></item>
    /// <item><description><c>Sprite.Rotation = LeanRadians</c>.</description></item>
    /// <item><description><c>Sprite.Scale = Scale</c> — but Godot scales a <c>Sprite2D</c>
    /// around its own center, so any frame where <see cref="Scale"/>.Y != 1 (footfall squash,
    /// idle breathing) shifts the visual feet line unless compensated. The consumer MUST also
    /// add <c>h/2 * (1 - Scale.Y)</c> to <c>Sprite.Offset.Y</c>:
    /// <c>Sprite.Offset.Y = -h/2 + BobY + h/2 * (1 - Scale.Y)</c>. Skipping this makes the
    /// character appear to sink or float whenever it squashes or breathes.</description></item>
    /// <item><description><see cref="StepFrameB"/> selects the M4-derived step-B texture
    /// instead of the base texture; this class only reports the flag — it never swaps
    /// textures itself.</description></item>
    /// <item><description><see cref="WalkFrame"/> (U3, 2026-08-04 verify-by-playing plan, R3):
    /// which of FOUR gait textures to show — 0 = base, 1 = "_walk2", 2 = "_step", 3 =
    /// "_walk4" (ids per <c>tools/art/gen_town_sprites.py</c>'s U3 section). Supersedes
    /// <see cref="StepFrameB"/> for any consumer that has all four frames; kept alongside it
    /// (rather than replacing it) so an already-working 2-frame consumer (<c>PlayerController2D</c>,
    /// which only ever got a base/step pair) needs no change. Always 0 while idle — a caller that
    /// only reads <see cref="WalkFrame"/> and ignores <see cref="StepFrameB"/> still shows the
    /// base texture at rest, exactly like the 2-frame path always did.</description></item>
    /// </list></para>
    /// </summary>
    public readonly record struct Pose(float BobY, float LeanRadians, Vector2 Scale, bool StepFrameB, int WalkFrame);

    /// <summary>Per-actor idle-breath phase offset (radians), typically derived from a
    /// deterministic id (e.g. <c>HeroActor2D.HeroIdValue</c>, mirroring the existing
    /// id-&gt;motion lissajous-wander idiom at <c>HeroActor2D.Init</c>) so a town full of idle
    /// actors doesn't breathe in lockstep.</summary>
    private readonly float _phaseSeed;

    private double _time;

    public SpriteMotion(float phaseSeed)
    {
        _phaseSeed = phaseSeed;
    }

    /// <summary>
    /// Accumulate <paramref name="delta"/> and compute this frame's <see cref="Pose"/> from
    /// <paramref name="velocity"/> (px/s) and <paramref name="walkSpeed"/> (the actor's own
    /// full-pace speed, e.g. <c>HeroActor2D.WalkSpeed</c>/<c>PlayerController2D.Speed</c> — used
    /// only to normalize cadence/lean to the actor's own pace, never to move anything itself).
    /// Pure function of accumulated time + the two arguments: no RNG, no wall-clock reads.
    /// </summary>
    public Pose Advance(double delta, Vector2 velocity, float walkSpeed)
    {
        _time += delta;

        var speed = velocity.Length();
        if (speed <= WalkSpeedThreshold)
        {
            return IdlePose();
        }

        return WalkPose(speed, velocity.X, walkSpeed);
    }

    private Pose IdlePose()
    {
        var breath = BreathAmplitude * Mathf.Sin((float)(_time * BreathHz * Mathf.Tau) + _phaseSeed);

        // Scale.X moves inverse to Scale.Y — a cheap "volume preserving" squash/stretch read
        // rather than a flat one-axis pulse.
        var scale = new Vector2(1f - breath, 1f + breath);
        return new Pose(BobY: 0f, LeanRadians: 0f, Scale: scale, StepFrameB: false, WalkFrame: 0);
    }

    private Pose WalkPose(float speed, float velocityX, float walkSpeed)
    {
        var speedRatio = Mathf.Clamp(speed / Mathf.Max(walkSpeed, 0.0001f), 0f, 1f);
        var stepHz = StepHz * speedRatio;

        // sinArg is monotonically non-decreasing (time only accumulates forward, stepHz >= 0),
        // so a plain modulo (no Mathf.PosMod needed) is safe here.
        var sinArg = (float)(_time * stepHz) * Mathf.Pi;
        var rawSin = Mathf.Sin(sinArg);
        var bobY = -BobAmplitude * Mathf.Abs(rawSin);

        // Footfall squash: a brief flatten at each bob BOTTOM, i.e. where |sin| peaks at 1
        // (sinArg mod PI == PI/2). The half-width is expressed in the same angular units as
        // sinArg (angular velocity = stepHz * PI rad/sec) so the WALL-CLOCK window stays
        // ~FootfallSquashWindowSeconds regardless of cadence, instead of narrowing/widening
        // with speed.
        var phaseIntoHalfCycle = sinArg % Mathf.Pi; // sinArg >= 0, so plain % is in [0, PI)
        var distanceFromBottom = Mathf.Abs(phaseIntoHalfCycle - Mathf.Pi / 2f);
        var angularHalfWidth = 0.5f * FootfallSquashWindowSeconds * stepHz * Mathf.Pi;
        var squashed = distanceFromBottom < angularHalfWidth;
        var scale = squashed ? FootfallSquashScale : Vector2.One;

        var leanSign = velocityX > 0f ? 1f : velocityX < 0f ? -1f : 0f;
        var lean = leanSign * LeanMaxRadians * speedRatio;

        var stepFrameB = rawSin >= 0f;

        // U3 (2026-08-04 verify-by-playing plan, R3): a REAL 4-frame alternating gait, replacing
        // the 2-frame symmetric pose swap. sinArg's full period (Tau in sinArg-space) is one
        // complete stride — both feet back to where they started — so quartering it gives four
        // equal phases: 0 (base, matches the idle/rest texture so a walk-start never pops), 1
        // ("_walk2", a passing pose), 2 ("_step", the mirrored contact pose), 3 ("_walk4", the
        // other passing pose). Quartering (not the StepFrameB half-period above) is deliberate:
        // StepFrameB only ever needed two states, WalkFrame needs four evenly-spaced ones.
        var strideArg = sinArg % Mathf.Tau;
        var walkFrame = Mathf.Clamp((int)(strideArg / (Mathf.Tau / 4f)), 0, 3);

        return new Pose(bobY, lean, scale, stepFrameB, walkFrame);
    }

    /// <summary>
    /// U-T3-5 (register #141, "character legs clip with the grass"): the per-frame correction to
    /// apply to the ART ROOT — the child <see cref="Node2D"/> <c>TownLayout2D.CharacterArtRoot()</c>
    /// sits between an actor and its <see cref="Sprite2D"/> — so the actor keeps drawing on a whole
    /// screen pixel every frame.
    ///
    /// <para><b>Root cause.</b> <c>HeroActor2D</c>/<c>TownsfolkNpc2D</c>'s idle lissajous wander and
    /// <c>PlayerController2D</c>'s seek both hold the actor's own <see cref="Node2D.Position"/> (the
    /// Y-sort/feet baseline) at a continuously-varying float, and — independent of that — this
    /// class's own idle-breathe/walk-bob <see cref="Pose"/> holds <c>Sprite2D.Offset</c>/<c>Scale</c>
    /// continuously fractional too. <c>Town2D.Build</c> already turns on the world
    /// <c>SubViewport</c>'s <c>Snap2DTransformsToPixel</c>/<c>Snap2DVerticesToPixel</c>, and every
    /// character texture is drawn <c>Nearest</c>-filtered — but <c>tools/receipt.ps1</c>'s own
    /// measured noise floor (see its header) still isolates a nonzero frame-to-frame pixel diff to
    /// "one idle actor's breath-cycle animation" even with every OTHER animated layer suppressed:
    /// the engine-level snap does not fully absorb a continuously-varying SCALE the way it does a
    /// continuously-varying position. That residual is what reads as a leg dissolving into the
    /// grass — the sprite's own bottom edge samples a slightly different sub-pixel position every
    /// single frame, even while the character stands still.</para>
    ///
    /// <para><b>Why a correction on a separate node, not <c>Mathf.Round</c> on <see
    /// cref="Node2D.Position"/> itself.</b> Every actor's own <c>_Process</c> computes this frame's
    /// velocity as <c>(basePos - Position) / delta</c> BEFORE overwriting <c>Position</c> — that
    /// feeds <see cref="Advance"/>'s walk/idle threshold and lean. Rounding <c>Position</c> directly
    /// would inject up to ±0.5px of spurious velocity noise into that division (at a 60fps delta of
    /// ~0.017s, ±0.5px is ±29px/s — enough to spuriously cross <see cref="WalkSpeedThreshold"/>
    /// (20) near a lissajous zero-crossing and flash a walk pose on an idle actor). Returning the
    /// correction as its own delta, applied only to the art root's <see cref="Node2D.Position"/>,
    /// changes what is DRAWN without changing what is MEASURED — every existing exact-position
    /// assertion (<c>HeroActor2DTests</c>, <c>TownsfolkNpc2DTests</c>, <c>PlayerController2DTests</c>)
    /// stays correct unchanged.</para>
    /// </summary>
    public static Vector2 PixelSnapCorrection(Vector2 position) =>
        new(Mathf.Round(position.X) - position.X, Mathf.Round(position.Y) - position.Y);
}
