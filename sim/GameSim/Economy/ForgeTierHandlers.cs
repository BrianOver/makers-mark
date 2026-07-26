using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Materials;

namespace GameSim.Economy;

/// <summary>
/// Phase D (U-D1, gold sink 1 — "forge tier"): the player's <see cref="UpgradeForgeAction"/>
/// handler. Sales income is investment-shaped (deeper floors -&gt; richer heroes -&gt; higher
/// prices), so the plan calls for the primary sink to be EXPONENTIAL — "always one purchase from
/// the next threshold." Forge I is the free starting baseline; each of the four upgrades to
/// Forge II..V costs a fixed, steeply-escalating gold price PLUS a flat quantity of the ore the
/// corresponding Mine floor actually yields (lock-and-key — gold alone can never buy past what the
/// Mine has given up).
///
/// <para><b>State, without a Contracts change (KTD2 minimalism):</b> this unit's Contracts budget
/// is "additive new <see cref="PlayerAction"/> records only." Forge tier progress therefore rides
/// the EXISTING generic <see cref="PlayerState.Materials"/> dictionary under a reserved,
/// never-craftable key (<see cref="ForgeTierKey"/>) instead of a new field — exactly the shape
/// <see cref="Crafting.BatchEchoState"/> and every other counter in this codebase would otherwise
/// need a trailing member for, except here the generic bag already exists and needs no schema
/// change at all. <see cref="CurrentTierIndex"/> is the number of upgrades bought so far (0 = Forge
/// I, 4 = Forge V, the max); absent means 0, matching the "have = TryGetValue(...) ?? 0" convention
/// every other handler in this module already uses for <see cref="PlayerState.Materials"/> reads.</para>
///
/// <para><b>Determinism:</b> integer-only, no RNG, no wall clock, no transcendental math — the four
/// upgrade costs are literal fixed constants (never <c>Math.Pow</c>), per the Phase D plan note
/// ("all integers... zero new RNG").</para>
///
/// <para>Emits no event: matches the existing <see cref="Crafting.CraftingHandlers"/>'s
/// <c>UnlockTalentAction</c> precedent (another permanent, no-roll progression purchase) — no
/// existing <see cref="GameEvent"/> shape fits "spend gold + consume ore to raise a tier" without
/// being stretched, and this unit's Contracts budget doesn't extend to a new event type.</para>
/// </summary>
public sealed class ForgeTierHandlers : IActionHandler
{
    /// <summary>Reserved <see cref="PlayerState.Materials"/> key for forge-tier progress (never a
    /// craftable material; see class doc). Number of upgrades bought so far, 0..4.</summary>
    public const string ForgeTierKey = "forge-tier-progress";

    /// <summary>Forge I (the free starting baseline) through Forge V. Display tier = index + 1.</summary>
    public const int MaxUpgradeIndex = 3; // four upgrades: 0->1, 1->2, 2->3, 3->4

    /// <summary>Fixed exponential gold cost per upgrade (index = current tier index before the buy):
    /// Forge II 400g, III 1600g, IV 6400g, V 25600g (x4 each step, per the plan).</summary>
    public static readonly ImmutableArray<int> GoldCost = ImmutableArray.Create(400, 1600, 6400, 25600);

    /// <summary>The Mine-floor ore each upgrade consumes (lock-and-key): floor 1..4 ore for the four
    /// upgrades — floor-5 adamant is never required since Forge V is the ceiling.</summary>
    public static readonly ImmutableArray<string> OreKey = ImmutableArray.Create(
        MaterialRegistry.Copper, MaterialRegistry.Iron, MaterialRegistry.Steel, MaterialRegistry.Mithril);

    /// <summary>Flat ore quantity every upgrade consumes.</summary>
    public const int OreQuantity = 25;

    /// <summary>Current tier index (0..4) read off <see cref="PlayerState.Materials"/>; absent = 0
    /// (Forge I, the untouched baseline every fresh save and BaselinePlayer trace starts at).</summary>
    public static int CurrentTierIndex(PlayerState player) =>
        player.Materials.TryGetValue(ForgeTierKey, out var v) ? v : 0;

    public bool CanHandle(PlayerAction action, DayPhase phase) =>
        action is UpgradeForgeAction && phase == DayPhase.Morning;

    public (GameState State, RejectedAction? Rejected) Apply(
        GameState state, PlayerAction action, IDeterministicRng rng, IEventSink events)
    {
        if (action is not UpgradeForgeAction)
        {
            return (state, new RejectedAction(action, $"ForgeTierHandlers cannot apply {action.GetType().Name}."));
        }

        var tierIndex = CurrentTierIndex(state.Player);

        // 1. Already at the ceiling (Forge V) — nothing left to buy.
        if (tierIndex > MaxUpgradeIndex)
        {
            return (state, new RejectedAction(action, "The forge is already at Tier V — the maximum."));
        }

        var oreKey = OreKey[tierIndex];
        var cost = GoldCost[tierIndex];

        // 2. Lock-and-key: must have the floor's ore in hand — gold alone never buys past the Mine.
        var oreHave = state.Player.Materials.TryGetValue(oreKey, out var oreStock) ? oreStock : 0;
        if (oreHave < OreQuantity)
        {
            return (state, new RejectedAction(action,
                $"Not enough {oreKey} for Forge Tier {tierIndex + 2}: need {OreQuantity}, have {oreHave}."));
        }

        // 3. Gold.
        if (state.Player.Gold < cost)
        {
            return (state, new RejectedAction(action,
                $"Not enough gold for Forge Tier {tierIndex + 2}: need {cost}, have {state.Player.Gold}."));
        }

        // 4. Day action-budget gate — checked LAST, like every other real-work handler.
        if (state.ActionSlotsRemaining <= 0)
        {
            return (state, new RejectedAction(action, $"No action slots left today (0/{ActionBudget.SlotsPerDay}) — 'next' to advance."));
        }

        var newState = state with
        {
            Player = state.Player with
            {
                Gold = state.Player.Gold - cost,
                Materials = state.Player.Materials
                    .SetItem(oreKey, oreHave - OreQuantity)
                    .SetItem(ForgeTierKey, tierIndex + 1),
            },
            ActionSlotsRemaining = state.ActionSlotsRemaining - 1,
        };

        return (newState, null);
    }
}
