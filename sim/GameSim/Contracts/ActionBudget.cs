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
    /// Whether <paramref name="action"/> is "real work" that spends a slot.
    ///
    /// <para>This list is the TEN action types whose handlers actually decrement
    /// <c>ActionSlotsRemaining</c> — verified by grep, not by intent. It named only four until
    /// 2026-08-14, while nine handlers spent slots, so any surface built on it would have
    /// under-reported the day's real cost. Nothing called it at runtime, which is the only reason
    /// the lie was harmless: it was a trap armed for the next caller, not a live bug.</para>
    ///
    /// <para><b><see cref="UnlockTalentAction"/> is the tenth, added by U-T1-9 (register #157, owner
    /// ruling R14.3).</b> Twenty-two blacksmith recipes used to open on two free clicks; a talent
    /// unlock now requires a Forge Tier AND costs a slot, so opening the ladder competes with the
    /// day's crafting instead of being a freebie taken on the way past. This predicate and
    /// <c>CraftingHandlers.ApplyUnlock</c> had to move in the same PR: the handler spending a slot
    /// while this file still said "free" is the exact fiction the 2026-08-14 correction above was
    /// about, and shipping the two halves apart would have re-armed the same trap in the opposite
    /// direction.</para>
    ///
    /// <para>Shelf-arranging (stock/price/unstock), profession picks, counter-session moves
    /// (open/close/present/suggest/haggle), commission answers, the farewell rite, and Camp verbs
    /// (send/recall) stay free — they don't compete for the day's attention budget. That half of the
    /// original comment was always true; it was the "real work" half that was wrong. Note that
    /// "talent picks" used to be listed here and no longer is.</para>
    ///
    /// <para><c>ActionBudgetTests</c> pins this by REFLECTION over every concrete
    /// <see cref="PlayerAction"/> subtype: each must be explicitly consuming or explicitly free, so
    /// a tenth action type fails the suite by name rather than defaulting silently to free. That is
    /// the drift the old "exactly the four" test could not catch, and it mirrors the advisor's own
    /// legality-parity test.</para>
    /// </summary>
    public static bool ConsumesSlot(PlayerAction action) =>
        action is CraftAction
            or BuyOreAction
            or BuyMaterialAction
            or PostBountyAction
            or ReforgeHeirloomAction
            or BuyForgeSupplyAction
            or UpgradeForgeAction
            or MasterworkAttemptAction
            or CommissionLegendaryWorkAction
            or UnlockTalentAction;
}
