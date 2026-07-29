using Godot;

namespace GodotClient.Town2d;

/// <summary>
/// Animation-gap fix (#2, "trees never move"): a gentle wind-sway rotation for static
/// <c>"town2d-prop-tree"</c> props — they were placed once by <see cref="Town2D.BuildProps"/> and
/// stayed perfectly rigid while lampposts flicker (<see cref="AmbientLife2D"/>) and fireflies
/// drift around them. A plain, engine-free pure class (mirrors <see cref="SpriteMotion"/>'s
/// convention: only accumulates a delta and returns a value, no node/scene/runtime dependency) so
/// it is unit-testable without <c>[RequireGodotRuntime]</c>.
///
/// <para><b>Per-instance <see cref="_phaseSeed"/></b> desyncs a whole grove so trees don't sway in
/// lockstep — the exact same id-&gt;phase idiom <see cref="AmbientLife2D"/>'s per-lamppost
/// flicker already uses (<c>i * 0.9f</c> there; <see cref="SwayingTreeSprite2D"/> seeds this the
/// same way per tree index).</para>
///
/// <para>Applied to a CHILD <see cref="Sprite2D"/>'s own <see cref="Node2D.Rotation"/> (see
/// <see cref="SwayingTreeSprite2D"/>) whose <see cref="Sprite2D.Offset"/> already lifts the art up
/// by half its texture height (the shared prop feet-origin convention) — rotating that sprite
/// rotates around the FEET line (the trunk base), never the crown, exactly like a tree actually
/// sways in wind.</para>
/// </summary>
public sealed class TreeSway
{
    /// <summary>Wind-gust cadence (Hz) — slow and lazy, not a flag-in-a-storm flutter.</summary>
    public const float SwayHz = 0.18f;

    /// <summary>Max sway angle (~2.5 degrees) — gentle, restrained, never cartoonish.</summary>
    public static readonly float SwayAmplitudeRadians = 2.5f * Mathf.Pi / 180f;

    private readonly float _phaseSeed;
    private double _time;

    public TreeSway(float phaseSeed)
    {
        _phaseSeed = phaseSeed;
    }

    /// <summary>
    /// Accumulates <paramref name="delta"/> and returns this frame's sway rotation (radians).
    /// Pure function of accumulated time + the phase seed: no RNG, no wall-clock (KTD4/KTD5) —
    /// same phaseSeed + delta sequence always yields the same rotation sequence.
    /// </summary>
    public float Advance(double delta)
    {
        _time += delta;
        return SwayAmplitudeRadians * Mathf.Sin((float)(_time * SwayHz * Mathf.Tau) + _phaseSeed);
    }
}
