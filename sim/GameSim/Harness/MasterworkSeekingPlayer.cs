using System.Collections.Immutable;
using GameSim.Advisor;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Economy;

namespace GameSim.Harness;

/// <summary>
/// U4 (P6b ship-gate measurement, plan 2026-07-13-001): the ONLY scripted policy in this codebase
/// that ever constructs <see cref="UpgradeForgeAction"/>, <see cref="BuyForgeSupplyAction"/>, or
/// <see cref="MasterworkAttemptAction"/> — <see cref="BaselinePlayer"/> and <see cref="CounterPlayer"/>
/// never do (grepped and confirmed before writing this policy). Measuring the plan's "Masterwork
/// may dominate hand-crafting once the forge is resourced" ship-gate risk against either existing
/// policy would read zero crafted value through the purchased path forever, and a zero would look
/// exactly like "no dominance risk" even though nothing was ever exercised.
///
/// <para><b>The policy is deliberately greedy</b> — the shape of a rational, informed player, not a
/// balanced 50/50 script: once a masterwork attempt on the best-affordable recipe is legal
/// (<see cref="ActionLegality.IsLegal"/>, never re-derived by hand here), take it over an ordinary
/// hand-craft of the SAME recipe, every time. Hand-crafting only happens when a masterwork attempt
/// is not (yet) legal — too low a forge tier, an empty coal/flux cupboard, or not enough gold for
/// the surcharge. That greedy preference IS the dominance question the ship-gate asks; softening it
/// into an artificial split would answer a different, easier question than the one that matters.</para>
///
/// Same purity contract as every other <c>Harness/</c> policy: a pure function of
/// <see cref="GameState"/>, no IO, no RNG of its own, no wall clock, no transcendental
/// <c>Math.*</c> (house rule — cross-OS float drift). <see cref="BaselinePlayer"/> is UNTOUCHED and
/// never forked — this lives beside it, the same precedent <see cref="CounterPlayer"/> already set.
/// </summary>
public static class MasterworkSeekingPlayer
{
    /// <summary>Morning restock floor for coal/flux — top back up to this many units whenever a
    /// Morning finds either below it and the supplier's price is affordable. Comfortably above one
    /// attempt's cost (3 coal + 1 flux) so a short run of attempts between two Mornings does not
    /// itself become the limiter this policy exists to measure around.</summary>
    private const int SupplyRestockFloor = 12;

    private const int SupplyRestockBatch = 12;

    /// <summary>
    /// P2-HONEST-40: the entry point a <b>whole-campaign sweep</b> drives (<c>--policy masterwork</c>),
    /// as against <see cref="ActionsFor"/>, which <c>MasterworkDominanceBalanceTests</c> drives from a
    /// hand-built late-game fixture (Forge Tier II, 5,000 gold, a thousand of each base material).
    ///
    /// <para><b>Why the split, measured.</b> <see cref="ActionsFor"/> spends: it upgrades, restocks
    /// coal and flux, and forges. It has no acquisition half at all — no Evening ore purchase, no
    /// shelf stocking, so no revenue — which is invisible against a fixture that starts rich and
    /// stays rich. Pointed at <see cref="GameComposition.NewCampaign(ulong)"/> it made <b>zero crafts
    /// of any grade across 8 seeds x 30 days</b>, because the first hand-craft never had materials:
    /// the sweep would have read the same zero the census already read, for a new reason. This
    /// method supplies exactly the missing half by delegating every phase the masterwork loop does
    /// not own to <see cref="BaselinePlayer"/> — the campaign that funds the forge — and keeps the
    /// Expedition window, where the greedy attempt-over-hand-craft preference IS the measurement.
    /// <see cref="ActionsFor"/> is left byte-for-byte alone so the recorded dominance numbers in
    /// <c>MasterworkDominanceBalanceTests</c> still mean what they say.</para>
    /// </summary>
    public static ImmutableList<PlayerAction> SweepActionsFor(GameState state) =>
        state.Phase == DayPhase.Expedition
            ? ActionsFor(state)
            // Morning: baseline stocks and prices the shelf first (the income), then this policy's
            // own upgrade and coal/flux restock spend it. Every other phase is baseline's alone —
            // ActionsFor contributes nothing outside Morning and Expedition, so the AddRange is a
            // no-op there rather than a special case that could drift out of sync with the switch.
            : BaselinePlayer.ActionsFor(state).AddRange(ActionsFor(state));

    public static ImmutableList<PlayerAction> ActionsFor(GameState state)
    {
        var actions = ImmutableList.CreateBuilder<PlayerAction>();

        switch (state.Phase)
        {
            case DayPhase.Morning:
                // Sink 1: climb the forge tier whenever the next upgrade is legal (ore + gold met).
                var upgrade = new UpgradeForgeAction();
                if (ActionLegality.IsLegal(state, upgrade, state.Phase))
                {
                    actions.Add(upgrade);
                }

                // Sink 3a: keep coal/flux stocked so a masterwork attempt is never blocked by an
                // empty supply cupboard rather than by gold — that would understate the dominance
                // risk this policy exists to measure honestly.
                foreach (var supplyKey in new[] { ForgeSupplyHandlers.Coal, ForgeSupplyHandlers.Flux })
                {
                    var have = state.Player.Materials.TryGetValue(supplyKey, out var stock) ? stock : 0;
                    if (have >= SupplyRestockFloor)
                    {
                        continue;
                    }

                    var restock = new BuyForgeSupplyAction(supplyKey, SupplyRestockBatch);
                    if (ActionLegality.IsLegal(state, restock, state.Phase))
                    {
                        actions.Add(restock);
                    }
                }

                break;

            case DayPhase.Expedition:
                // G3: a craft/attempt spends an action slot — skip once the day's budget is spent
                // (mirrors BaselinePlayer's own guard, so a doomed action is never queued).
                if (state.ActionSlotsRemaining <= 0)
                {
                    break;
                }

                // Same recipe ordering BaselinePlayer's Expedition branch uses (tier desc, then
                // stat-sum desc) — best recipe first, one action per window. For THAT recipe: a
                // masterwork attempt whenever legal, else an ordinary hand-craft, else fall through
                // to the next-best recipe.
                foreach (var recipe in RecipeTable.All.Values
                             .OrderByDescending(r => r.Tier)
                             .ThenByDescending(r => r.BaseStats.Attack + r.BaseStats.Defense))
                {
                    var masterwork = new MasterworkAttemptAction(recipe.RecipeId, recipe.MaterialKey);
                    if (ActionLegality.IsLegal(state, masterwork, state.Phase))
                    {
                        actions.Add(masterwork);
                        break;
                    }

                    var craft = new CraftAction(recipe.RecipeId, recipe.MaterialKey);
                    if (ActionLegality.IsLegal(state, craft, state.Phase))
                    {
                        actions.Add(craft);
                        break;
                    }
                }

                break;
        }

        return actions.ToImmutable();
    }
}
