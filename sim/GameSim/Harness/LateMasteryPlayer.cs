using System.Collections.Immutable;
using System.Linq;
using GameSim.Advisor;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Professions;

namespace GameSim.Harness;

/// <summary>
/// The second talent-pacing policy P2-OQ9 asked for (owner ruling 2026-09-03,
/// <c>docs/design/MAKERS-MARK.md</c> §11.15): everything <see cref="BaselinePlayer"/> already
/// decides, INCLUDING the hand-forge itself (<see cref="HandForgePlayer.HandForgeOver"/>), except
/// which talent the Morning phase's one-node-per-day unlock loop reaches for.
///
/// <para><b>The question this exists to answer.</b> <see cref="BaselinePlayer"/>'s own talent order
/// is prereq-order-then-alphabetical, unconditional: keen-eye day 1, master-touch day 2,
/// legendary-craft day 3, every seed — and P2-OQ9 measured that this greedy order makes Masterwork
/// the MODAL hand-forge grade (51.7% of 1,522 items) for the rest of every 100-day campaign, with
/// the ceiling locking in before the very first hand-forge fires. That finding rests entirely on
/// ONE scripted order. This type is the other end of the range the owner ruling asked to be bounded:
/// an order that spends every other point it can before either mastery talent, so the sweep can
/// finally say whether the ceiling is a genuine progression (locks late, or never, under a
/// differently-paced but still-plausible player) or a structural property of the tree (locks early
/// regardless of order).</para>
///
/// <para><b>Why "late-mastery" and not the other two candidates the ruling named.</b>
/// <list type="bullet">
/// <item><description><b>Never-mastery</b> (never buy either mastery talent, spend everything
/// else) cannot answer THIS question — by construction it never reaches Masterwork and never
/// locks a ceiling, so it has no "which day" to report. It is also not new information: P2-OQ9
/// already measured its shape exactly, via the test-local talent-strip wrapper the doc's own
/// "Is it the talents or the policy?" section describes (0% Masterwork, 1,458 items, zero
/// exceptions). Shipping a second policy to re-derive an answer already on record would be the
/// "four policies nobody asked for" this ruling explicitly warns against.</description></item>
/// <item><description><b>An economically-motivated order</b> (whatever a gold- or
/// survivability-optimizing player would buy) is not a distinct choice YET: <see cref="TalentTree"/>'s
/// own class doc says so directly — "unlocking costs nothing beyond prerequisites — the
/// talent-point economy (earn rate, costs) is deliberately deferred; only prerequisite edges gate
/// progression for now." With no talent-point cost or budget to trade off, there is no economic
/// axis to script against; every order spends the SAME one-node-per-morning cadence, so
/// "economically motivated" collapses to a preference ordering indistinguishable from any other
/// hand-picked one, with nothing in the sim yet to justify preferring it over late-mastery for this
/// specific question.</description></item>
/// </list>
/// So late-mastery is the one order that is both genuinely different from the greedy baseline (it
/// can produce an observably later, or absent, ceiling-lock day) and cheap to build honestly
/// (composition only — see below), which is why it is the only new policy this PR ships.</para>
///
/// <para><b>Composition, not a fork (KTD1) — twice over, the <see cref="HandForgePlayer"/>
/// precedent.</b> This type never re-derives <see cref="BaselinePlayer"/>'s non-talent decisions
/// (forge-tier purchase, commission acceptance, shelf stocking, ore buying) and never re-derives
/// which recipe/material to craft or how to hand-forge it — both ride through
/// <see cref="BaselinePlayer.ActionsFor"/> and <see cref="HandForgePlayer.HandForgeOver"/>
/// unchanged. The ONLY thing this type ever computes is which single <see cref="UnlockTalentAction"/>
/// (if any) to substitute for the one <see cref="BaselinePlayer"/>'s Morning branch already picked —
/// same eligibility rule (<see cref="TalentTree.CanUnlock"/>'s prerequisite check, inlined the same
/// way <see cref="BaselinePlayer"/> inlines it), same legality gate
/// (<see cref="ActionLegality.IsLegal"/>, asked, never re-derived), different tie-break order.</para>
///
/// <para><b>The ordering rule.</b> Among nodes whose prerequisites are already satisfied and that
/// are not yet unlocked, this policy always prefers a non-mastery node (<see cref="TalentTree.KeenEye"/>,
/// <see cref="TalentTree.WeaponSpecialist"/>, <see cref="TalentTree.MaterialEfficiency"/>,
/// <see cref="TalentTree.MaterialMastery"/>, <see cref="TalentTree.Tier2Smithing"/>,
/// <see cref="TalentTree.Tier3Smithing"/>) over <see cref="TalentTree.MasterTouch"/> or
/// <see cref="TalentTree.LegendaryCraft"/>, walking a legal candidate exactly the way
/// <see cref="BaselinePlayer"/> already does (a prereq-eligible-but-Forge-Tier-locked candidate —
/// <see cref="TalentTree.Tier2Smithing"/>/<see cref="TalentTree.Tier3Smithing"/> before the matching
/// Forge Tier is reached — is skipped rather than blocking the walk forever). Within a rank, ties
/// break the SAME way <see cref="BaselinePlayer"/> breaks them (ordinal node id) purely so the order
/// stays deterministic and reviewable; it carries no design meaning of its own. Master's Touch can
/// only be reached once every other reachable node is either already unlocked or currently
/// Forge-Tier-locked, and Legendary Craft only once Master's Touch itself is unlocked (its own
/// prerequisite) — "as late as the tree allows" is therefore an emergent property of this one rule,
/// not a hand-picked day number.</para>
///
/// <para>Pure: no RNG of its own, no IO, no wall clock — the same composition-only guarantee
/// <see cref="HandForgePlayer"/> and <see cref="ApprenticePlayer"/> already carry.</para>
/// </summary>
public static class LateMasteryPlayer
{
    public static ImmutableList<PlayerAction> ActionsFor(GameState state) =>
        HandForgePlayer.HandForgeOver(state, ReorderedTalentBaseline(state));

    /// <summary>
    /// <see cref="BaselinePlayer.ActionsFor"/>'s own action list, with its one Morning
    /// <see cref="UnlockTalentAction"/> (if it emitted one) swapped for the pick this policy's own
    /// order — see class doc — would make instead. Everything else in the list (forge-tier
    /// purchase, commission accepts, shelf stocking on Morning; the craft loop on Expedition; ore
    /// buying on Evening) is <see cref="BaselinePlayer"/>'s own, untouched.
    /// </summary>
    private static ImmutableList<PlayerAction> ReorderedTalentBaseline(GameState state)
    {
        var baseline = BaselinePlayer.ActionsFor(state);
        if (state.Phase != DayPhase.Morning)
        {
            return baseline; // BaselinePlayer only ever unlocks talents in Morning — nothing to reorder.
        }

        var talentIndex = baseline.FindIndex(a => a is UnlockTalentAction);
        if (talentIndex < 0)
        {
            return baseline; // no eligible-and-legal node this morning under EITHER order — nothing to swap.
        }

        var smithTalents = state.Player.TalentsFor(ProfessionRegistry.BlacksmithId);
        var pick = TalentTree.Nodes.Values
            .Where(n => !smithTalents.Contains(n.NodeId) && n.Prerequisites.All(smithTalents.Contains))
            .OrderBy(n => IsMastery(n.NodeId) ? 1 : 0)
            .ThenBy(n => n.NodeId, StringComparer.Ordinal)
            .Select(n => new UnlockTalentAction(n.NodeId, ProfessionRegistry.BlacksmithId))
            .FirstOrDefault(candidate => ActionLegality.IsLegal(state, candidate, state.Phase));

        // `pick` cannot be null here: it walks the exact same candidate SET BaselinePlayer's own
        // Where(...) produced (same eligibility, same ActionLegality.IsLegal predicate) — only the
        // ORDER differs — and baseline already proved that set contains a legal candidate by
        // emitting one (talentIndex >= 0 above). Re-ordering a non-empty matching set cannot make
        // the match disappear; the throw below is an assertion, not a reachable path.
        return pick is null
            ? throw new InvalidOperationException(
                "LateMasteryPlayer found no legal talent candidate where BaselinePlayer found one — " +
                "the two policies' eligibility/legality predicates have diverged.")
            : baseline.SetItem(talentIndex, pick);
    }

    /// <summary>The two talents this order defers past every other reachable node — see class doc.</summary>
    private static bool IsMastery(string nodeId) =>
        nodeId == TalentTree.MasterTouch || nodeId == TalentTree.LegendaryCraft;
}
