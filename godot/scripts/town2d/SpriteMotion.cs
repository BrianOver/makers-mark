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
    /// </list></para>
    /// </summary>
    public readonly record struct Pose(float BobY, float LeanRadians, Vector2 Scale, bool StepFrameB);

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
        return new Pose(BobY: 0f, LeanRadians: 0f, Scale: scale, StepFrameB: false);
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

        return new Pose(bobY, lean, scale, stepFrameB);
    }
}
