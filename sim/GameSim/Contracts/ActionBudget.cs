namespace GameSim.Contracts;

/// <summary>
/// The day's action-slot scarcity budget (Game-Feel Plan G3, docs/design/2026-07-21-game-feel-plan.md
/// §G3): a fixed number of "real work" actions per calendar day, so crafting/restocking/negotiating
/// each mean NOT doing the others.
///
/// Lives in Contracts (not a module) so three independent layers share the same constant/predicate
/// with zero cross-module coupling: the kernel's day-boundary reset (<c>GameKernel.Tick</c>), the
/// handlers that gate on it (Crafting/Economy/Bounties), and the CLI/UI surface that displays
/// "actions left". Pure data + a pure predicate over <see cref="PlayerAction"/> already defined in
/// this same file's neighbor (Actions.cs) — no RNG, no state, no wall clock (KTD2).
/// </summary>
public static class ActionBudget
{
    /// <summary>Slots granted at the start of each calendar day. The ONE tuning knob (start N≈5
    /// per the game-feel plan); data-driven so a later balance pass changes just this line.</summary>
    public const int SlotsPerDay = 5;

    /// <summary>
    /// Whether <paramref name="action"/> is "real work" that spends a slot: craft, restock/buy
    /// (the Morning material vendor and the Evening ore market), or negotiate (post a bounty).
    /// Shelf-arranging (stock/price/unstock), profession/talent picks, counter-session moves, and
    /// Camp verbs (send/recall) stay free — they don't compete for the day's attention budget.
    /// </summary>
    public static bool ConsumesSlot(PlayerAction action) =>
        action is CraftAction or BuyOreAction or BuyMaterialAction or PostBountyAction;
}
