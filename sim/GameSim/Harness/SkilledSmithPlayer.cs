using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;

namespace GameSim.Harness;

/// <summary>
/// U2 (plan 2026-08-13-002): a skilled baseline that DELEGATES ENTIRELY to
/// <see cref="BaselinePlayer.ActionsFor"/> and re-stamps each returned <see cref="CraftAction"/>
/// with a <see cref="SmithSkill"/> performance grade — KTD1's "compose, never fork." Every other
/// action (talent unlocks, stocking, ore buys) rides through byte-identical: this type never asks
/// which recipe to craft, never re-derives legality, never touches anything but the one field a
/// real player's minigame result would have set.
///
/// <para><b>First-of-its-kind in this codebase</b> (KTD1's correction): <see
/// cref="MasterworkSeekingPlayer"/> looks like a precedent but is not — it never calls
/// <see cref="BaselinePlayer.ActionsFor"/>, it independently reimplements the same
/// tier-descending recipe loop. This type is the first policy that wraps another policy's output
/// instead of re-deriving it, which is why its delegation tests carry the real burden of proof.</para>
///
/// <para>Same purity contract as every other <c>Harness/</c> policy: a pure function of
/// <see cref="GameState"/> plus a <see cref="SmithSkill"/> profile, no IO, no RNG of its own
/// (the grade is derived from state, not drawn — see <see cref="SmithSkill"/>), no wall clock.</para>
/// </summary>
public static class SkilledSmithPlayer
{
    /// <summary>
    /// <see cref="BaselinePlayer.ActionsFor"/>'s output for <paramref name="state"/>, with every
    /// <see cref="CraftAction"/> re-stamped to carry <paramref name="skill"/>'s
    /// <see cref="SmithSkill.Grade"/> as its <see cref="CraftAction.PerformanceGrade"/>. Every
    /// other action is the exact same record <see cref="BaselinePlayer"/> emitted — same order,
    /// same values, same recipe/material choice.
    /// </summary>
    public static ImmutableList<PlayerAction> ActionsFor(GameState state, SmithSkill skill) =>
        BaselinePlayer.ActionsFor(state)
            .Select(action => action is CraftAction craft
                ? craft with { PerformanceGrade = skill.Grade(state) }
                : action)
            .ToImmutableList();
}
