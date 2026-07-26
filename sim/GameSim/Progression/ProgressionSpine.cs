using System.Collections.Immutable;
using System.Linq;

namespace GameSim.Progression;

/// <summary>The five progression ladders the campaign advances along (U-D4). Four are finite and
/// cross-feed each other; <see cref="Chronicle"/> is the unbounded axis that outlives them, so the
/// progression tree can't "end before the systems do" (the Travellers-Rest failure the plan calls
/// out).</summary>
public enum ProgressionAxis
{
    Forge,
    Depth,
    Roster,
    Wealth,
    Chronicle,
}

/// <summary>One ladder's visible state: where the player is, the concrete NEXT rung to aim at, an
/// optional 0-1000 closeness meter (<c>null</c> when a rung isn't meaningfully a fraction — e.g. a
/// countdown or an unbounded tally), whether the axis is <see cref="Unbounded"/> (never completes),
/// and a one-line <see cref="Feeds"/> note naming which other ladder(s) this one powers. Pure data
/// (no Godot, integer-only) — a READ-ONLY derivation over <see cref="GameSim.Contracts.GameState"/>,
/// so it draws no RNG and changes no rule (golden-neutral).</summary>
public sealed record ProgressionRung(
    ProgressionAxis Axis,
    string Current,
    string NextRung,
    int? ProgressPermille,
    bool Unbounded,
    string Feeds);

/// <summary>The whole spine — one <see cref="ProgressionRung"/> per axis, always all five, in
/// <see cref="ProgressionAxis"/> order. Indexable by axis for callers (HUD, CLI) that want one
/// ladder.</summary>
public sealed record ProgressionSpine(ImmutableList<ProgressionRung> Rungs)
{
    /// <summary>The rung for <paramref name="axis"/> (always present — Compute emits all five).</summary>
    public ProgressionRung this[ProgressionAxis axis] => Rungs.First(r => r.Axis == axis);
}
