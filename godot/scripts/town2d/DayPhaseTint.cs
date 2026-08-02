using GameSim.Contracts;
using Godot;

namespace GodotClient.Town2d;

/// <summary>
/// Animation-gap fix (#4, "day/night is a lie"): drives the town's <see
/// cref="Town2D.DuskModulate"/> tint off the sim's real <see cref="DayPhase"/> clock instead of a
/// single fixed dusk tint applied once at <see cref="Town2D.Build"/>. A plain, engine-free pure
/// class (mirrors <see cref="SpriteMotion"/>'s convention: only <see cref="Godot.Color"/> value
/// structs, no node/scene/runtime dependency) so it is unit-testable without
/// <c>[RequireGodotRuntime]</c>.
///
/// <para><b>Keeps the established purple-dusk palette identity</b>: every phase's tint (see <see
/// cref="TintFor"/>) is a variation on the SAME purple hue family — from a pale lavender daybreak
/// through the near-night violet of the deep mine floors — never a new color scheme. A day cycle
/// should read as one shift of light across a single moody world, not a jump to a different
/// game's sky.</para>
///
/// <para>Deterministic by construction: same (initial tint, target-phase sequence, delta
/// sequence) always yields the same eased-tint sequence. No RNG, no wall-clock — only the
/// accumulated <c>delta</c> passed to <see cref="Advance"/>. Eases exponentially toward the
/// current phase's target every call (never a hard snap) so a phase change reads as a gradual
/// wash across the sky rather than a jump-cut.</para>
/// </summary>
public sealed class DayPhaseTint
{
    /// <summary>Fraction of the remaining gap to the target tint closed per second — an
    /// exponential ease, so convergence speed stays consistent regardless of frame-delta size
    /// (unlike a flat per-frame lerp factor, which would converge slower at low framerates).</summary>
    public const float EaseRatePerSecond = 0.6f;

    /// <summary>Pale lavender daybreak — the brightest stop, town is calm and just waking.</summary>
    public static readonly Color MorningTint = new(0.97f, 0.95f, 0.98f);

    /// <summary>Soft lavender daylight — heroes are out raiding, town is quiet but lit.</summary>
    public static readonly Color ExpeditionTint = new(0.92f, 0.88f, 0.97f);

    /// <summary>U11 retune ("'night' phase is day" / "Night -&gt; Dawn - no visual difference
    /// really"): was the original fixed <c>Town2D.DuskTint</c> value verbatim, (0.86, 0.80, 0.93)
    /// — near-white, indistinguishable from <see cref="MorningTint"/> at a glance. Dropped to a
    /// genuinely dark violet so the phase the UI labels "Night" (<see
    /// cref="GameSim.Contracts.DayPhase.Evening"/>, via <c>PhaseVocab</c>) actually reads as
    /// night — sitting between <see cref="CampTint"/> and <see cref="ExpeditionDeepTint"/> (KTD-6:
    /// <see cref="ExpeditionDeepTint"/> keeps its own "darkest, most tense hour" narrative meaning
    /// as the stage-2 floors resolve; this stop is dark, not darkest). The Night&lt;-&gt;Dawn gap
    /// is still the single largest jump in the whole palette (still same purple-dusk hue family —
    /// Blue stays the dominant channel).</summary>
    public static readonly Color EveningTint = new(0.42f, 0.36f, 0.58f);

    /// <summary>Deeper twilight purple — the party is camped below the checkpoint, town itself
    /// has gone fully dark for the night.</summary>
    public static readonly Color CampTint = new(0.55f, 0.48f, 0.68f);

    /// <summary>Near-night violet — the darkest stop. Stage-2 floors are resolving far below;
    /// the town above reads as its darkest, most tense hour.</summary>
    public static readonly Color ExpeditionDeepTint = new(0.30f, 0.26f, 0.42f);

    /// <summary>Maps a <see cref="DayPhase"/> to its target tint — pure, no state. The unmatched
    /// default (future kernel phases) falls back to <see cref="EveningTint"/>, the palette's own
    /// historical default, rather than an arbitrary color.</summary>
    public static Color TintFor(DayPhase phase) => phase switch
    {
        DayPhase.Morning => MorningTint,
        DayPhase.Expedition => ExpeditionTint,
        DayPhase.Evening => EveningTint,
        DayPhase.Camp => CampTint,
        DayPhase.ExpeditionDeep => ExpeditionDeepTint,
        _ => EveningTint,
    };

    private Color _current;

    /// <summary>Seeds the eased tint at exactly <paramref name="initial"/> — callers should pass
    /// <see cref="TintFor"/> of the adapter's phase at construction time so the town starts
    /// already correct for whatever phase the campaign resumed in, with no snap-then-ease on the
    /// very first frame.</summary>
    public DayPhaseTint(Color initial)
    {
        _current = initial;
    }

    /// <summary>The last tint <see cref="Advance"/> computed (or the constructor's seed if
    /// <see cref="Advance"/> has never been called).</summary>
    public Color Current => _current;

    /// <summary>
    /// Eases <see cref="Current"/> toward <see cref="TintFor"/>(<paramref name="phase"/>) by this
    /// frame's exponential fraction of <see cref="EaseRatePerSecond"/> — pure function of
    /// accumulated delta, no wall-clock read (KTD4/KTD5).
    /// </summary>
    public Color Advance(double delta, DayPhase phase)
    {
        var target = TintFor(phase);
        var t = 1f - Mathf.Exp(-EaseRatePerSecond * (float)delta);
        _current = _current.Lerp(target, Mathf.Clamp(t, 0f, 1f));
        return _current;
    }
}
