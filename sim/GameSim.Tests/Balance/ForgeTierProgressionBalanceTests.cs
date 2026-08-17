using GameSim;
using GameSim.Contracts;
using GameSim.Economy;
using GameSim.Harness;

namespace GameSim.Tests.Balance;

/// <summary>
/// U-T1-11 ("the reference smith climbs the ladder"): the proof this unit owes. Register #157
/// (PR #549, owner ruling R14.3) makes <c>tier-2-smithing</c> require Forge Tier II — but
/// <see cref="BaselinePlayer"/> never once attempted an <see cref="UpgradeForgeAction"/> before
/// this unit, so the balance gate's own reference economy could never reach the rung the feature
/// gates on (measured pre-fix: main seed peak gold 399g against a 400g cost, 0 of 11 balance seeds
/// ever bought Forge Tier II). This is the tripwire that keeps that regression from coming back
/// silently the next time someone touches <see cref="BaselinePlayer"/>'s Morning/Evening loops.
///
/// <para><b>First pass</b> (Morning Forge-Tier attempt + Evening ladder-ore hygiene/reservation) got
/// 9 of 11 seeds to Forge Tier II, main seed on day 41.</para>
///
/// <para><b>Second pass</b>, prompted by composing with #549 on an integration branch (register
/// #157 itself makes <c>UnlockTalentAction</c> spend an action slot and Forge-Tier-gates
/// tier-2/3-smithing): once tier-2/3 recipes are genuinely locked, tier-1 gear demand across the
/// whole roster saturates fast, and two real defects capped income far below 400g —
/// <list type="bullet">
/// <item><description>Evening ore-buying kept spending on iron/steel/etc. that no legal recipe
/// could touch anymore (146g of a 398g total ore spend on dead stock). Fixed: a
/// <c>usableMaterials</c> filter — only buy an ore whose tier-gate is already satisfied by an
/// unlocked talent, or that is the forge ladder's own lock-and-key ore — mirrors
/// <c>ActionLegality.CraftLegal</c>'s own tier check, never re-derived.</description></item>
/// <item><description>The one recipe left with a buyer once gear demand saturates — a Heal
/// consumable — was priced at <c>Math.Max(1, 0*2) = 1g</c> forever (a consumable's ItemStats are
/// always zero, so the gear pricing formula degenerately floors it). Fixed: price a Heal item on
/// its <c>ConsumableEffect.Magnitude</c> instead.</description></item>
/// </list>
/// Measured after both: 10 of 11 seeds reach Forge Tier II standalone (day 25-60; main seed
/// day 31) — every seed except 1 (peak 303g), still short of the composed world's own gate
/// (0 of 11 there — see third pass).</para>
///
/// <para><b>Third pass</b>: composing with #549 on <c>integ/u-t1-compose</c> still left the reference
/// economy stuck (6 of 11 seeds permanently parked in Act II, 0 of 11 reaching Forge Tier II) even
/// with both fixes above, because <see cref="BaselinePlayer"/> never once accepted an open
/// commission — a free action (no slot cost) that pays a guaranteed premium once fulfilled, one of
/// CLAUDE.md's four honest channels, and real, substantial headroom. Wiring it in surfaced two
/// pre-existing correctness bugs the reference player was the first thing to ever actually exercise
/// at volume:
/// <list type="bullet">
/// <item><description><c>Heroes/CommissionSystem.FindGapSlot</c> could post an uncompletable
/// Shield commission for a class that can never equip one — <c>AllowsShield: false</c> classes
/// never populate the Shield gear slot, so the empty-slot branch read that as an unambiguous gap
/// for every class. Fixed: skip the Shield slot for classes with <c>AllowsShield: false</c>, the
/// same fact <c>ShoppingAi.EvaluateItem</c> already gates ordinary shopping on.</description></item>
/// <item><description><c>Heroes/CommissionHandlers.TryFulfillFromShelf</c> matched a shelf item to
/// a commission on Slot+MinQuality only, with no role-fit or weight-cap check — a hard "can this
/// hero physically use it" fact, not a preference gate, and one ordinary shopping already enforces.
/// Fixed: mirror <c>ShoppingAi.EvaluateItem</c>'s role-fit and <c>MaxItemWeight</c> checks before
/// matching.</description></item>
/// </list>
/// <see cref="BaselinePlayer"/> also excludes Consumable-slot commissions from acceptance — not a
/// bug fix, a scope line: overriding a hero's own Consumable Stocking trait
/// (<c>TraitEffects.ConsumableStockTargetFor</c> deliberately returns 0 for Reckless heroes) via a
/// forced commission fulfillment is a separate design question from "does the reference player use
/// an existing honest channel," and is left for whoever next touches that trait's fiction.</para>
///
/// <para>Measured after all three passes, standalone (this branch's own base, the world this PR's
/// own gate actually runs): <b>11 of 11</b> seeds reach Forge Tier II, day 13-20. Pinned here
/// against that full set — a seed that regresses below Forge Tier II is the exact defect this test
/// exists to catch, never a band to widen and never a threshold to nudge.</para>
/// </summary>
public class ForgeTierProgressionBalanceTests
{
    private const int Days = 100;

    [Theory]
    [Trait("Category", "Balance")]
    [InlineData(2026UL)] // main seed
    [InlineData(1UL)]
    [InlineData(7UL)]
    [InlineData(42UL)]
    [InlineData(99UL)]
    [InlineData(1234UL)]
    [InlineData(5678UL)]
    [InlineData(31337UL)]
    [InlineData(777UL)]
    [InlineData(2468UL)]
    [InlineData(13579UL)]
    public void HundredDay_ReachesForgeTierTwo_OnEveryBalanceSeed(ulong seed)
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(seed);

        for (var tick = 0; tick < Days * 5; tick++) // 5-phase day (staged resolution)
        {
            state = kernel.Tick(state, BaselinePlayer.ActionsFor(state)).NewState;
        }

        var tierIndex = ForgeTierHandlers.CurrentTierIndex(state.Player);
        Assert.True(tierIndex >= 1,
            $"seed {seed}: forge tier index is {tierIndex} after {Days} days — BaselinePlayer never "
            + "bought Forge Tier II (index 1), the exact regression this test exists to catch");
    }
}
