using System.Collections.Immutable;
using System.Linq;
using GameSim;
using GameSim.Cli;
using GameSim.Contracts;

namespace GameSim.Tests.Cli;

/// <summary>
/// Playtest 2026-07-20 finding N1 (P0): a SUCCESSFUL craft narrated nothing on resolution —
/// the CLI's event renderer had no <see cref="ItemCrafted"/> case, so a legal craft looked
/// identical to a no-op (item silently appeared only if the player thought to run 'items').
/// These pin the renderer through the EXACT composition root the CLI drives, so a real
/// <see cref="ItemCrafted"/> off a real tick must produce a visible, item-naming line.
/// </summary>
public class EventNarrationTests
{
    private const ulong Seed = 7;

    [Fact]
    public void Line_ForSuccessfulCraft_IsVisibleAndNamesTheItem()
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(Seed);

        // Morning: buy the copper a tier-1 dagger needs; tick advances to Expedition.
        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new BuyMaterialAction("copper", 2))).NewState;

        // Craft is legal in all phases — this tick emits ItemCrafted on success.
        var result = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new CraftAction("dagger", "copper")));
        var crafted = result.Events.OfType<ItemCrafted>().Single();

        var line = EventNarration.Line(crafted, result.NewState);

        Assert.NotNull(line);
        Assert.Contains("Dagger", line, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Line_ForUnhandledEvent_IsNull()
    {
        // The renderer stays a pure projection: an event with no player-facing beat returns null
        // (the CLI prints nothing), never a crash or a stray blank line. A rival-shop sale
        // (FromPlayerShop == false) is such an event — only YOUR sales narrate.
        var line = EventNarration.Line(new ItemSold(new ItemId(1), new HeroId(1), 10, FromPlayerShop: false), GameComposition.NewCampaign(Seed));
        Assert.Null(line);
    }

    // U1 (C1a, R1/R2): the silent-economy + bounty-lifecycle cluster (playtest audit FR-1/FR-3) —
    // every event the player caused or was charged for must say so. One assertion per new case.
    private static readonly GameState State = GameComposition.NewCampaign(Seed);

    [Fact]
    public void Line_ForBountyPosted_NamesFloorAndReward()
    {
        var line = EventNarration.Line(new BountyPosted(new BountyId(1), TargetFloor: 3, RewardGold: 40), State);
        Assert.NotNull(line);
        Assert.Contains("floor 3", line, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("40g", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Line_ForBountyJudged_SurfacesTheSelfTeachingReasonVerbatim()
    {
        // AE7: the Reason string already names the price floor on a decline — the renderer must
        // pass it through UNCHANGED, never re-word or truncate it.
        const string declineReason = "30g is too thin for floor 3 — Testhero wants 30g";
        var declinedLine = EventNarration.Line(new BountyJudged(new BountyId(1), new HeroId(1), Accepted: false, Reason: declineReason), State);
        Assert.Equal($"  ~ {declineReason}", declinedLine);

        const string acceptReason = "Testhero takes the floor 3 bounty for 40g";
        var acceptedLine = EventNarration.Line(new BountyJudged(new BountyId(1), new HeroId(1), Accepted: true, Reason: acceptReason), State);
        Assert.Equal($"  ⚑ {acceptReason}", acceptedLine);
    }

    [Fact]
    public void Line_ForBountyPaid_NamesHeroAndReward()
    {
        var line = EventNarration.Line(new BountyPaid(new BountyId(1), new HeroId(1), RewardGold: 40), State);
        Assert.NotNull(line);
        Assert.Contains("40g", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Line_ForRentPaid_NamesAmountAndNextDue()
    {
        var line = EventNarration.Line(new RentPaid(AmountGold: 30, NextAmountDueGold: 33), State);
        Assert.NotNull(line);
        Assert.Contains("30g", line, StringComparison.Ordinal);
        Assert.Contains("33g", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Line_ForRentMissed_NamesTheConfidenceHit()
    {
        var line = EventNarration.Line(new RentMissed(AmountDueGold: 30, NextAmountDueGold: 40, MissedPayments: 1, ConfidencePermille: 850), State);
        Assert.NotNull(line);
        Assert.Contains("850", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Line_ForTariffApplied_NamesFactionAndPaidCost()
    {
        var line = EventNarration.Line(new TariffApplied("stonewrights", "iron", BaseLineCost: 100, PlayerCost: 90, Delta: -10), State);
        Assert.NotNull(line);
        Assert.Contains("stonewrights", line, StringComparison.Ordinal);
        Assert.Contains("90g", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Line_ForMarketShareShifted_NamesTheDirection()
    {
        var line = EventNarration.Line(new MarketShareShifted(Permille: 20, RivalGained: true), State);
        Assert.NotNull(line);
        Assert.Contains("rival", line, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Line_ForCommissionFulfilled_NamesThePremium()
    {
        var line = EventNarration.Line(new CommissionFulfilled(new HeroId(1), new ItemId(1), Premium: 25), State);
        Assert.NotNull(line);
        Assert.Contains("25g", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Line_ForCommissionExpired_NamesTheSlot()
    {
        var line = EventNarration.Line(new CommissionExpired(new HeroId(1), ItemSlot.Weapon), State);
        Assert.NotNull(line);
        Assert.Contains("Weapon", line, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Line_ForItemSigned_NamesTheLegend()
    {
        var line = EventNarration.Line(new ItemSigned(new ItemId(1), "the Widowmaker"), State);
        Assert.NotNull(line);
        Assert.Contains("the Widowmaker", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Line_ForMaterialPurchased_NamesQuantityAndCost()
    {
        var line = EventNarration.Line(new MaterialPurchased("copper", Quantity: 3, Cost: 15), State);
        Assert.NotNull(line);
        Assert.Contains("3x copper", line, StringComparison.Ordinal);
        Assert.Contains("15g", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Line_ForRecoveryStipendGranted_NamesTheAmount()
    {
        var line = EventNarration.Line(new RecoveryStipendGranted(Amount: 20), State);
        Assert.NotNull(line);
        Assert.Contains("20g", line, StringComparison.Ordinal);
    }

    // Phase B (B1a/B1c, R-B1/R-B3): the legibility spine's two new event types must render —
    // an undeclared case here would leave them stamped but silently unnarrated (the "dead line"
    // trap the unit explicitly calls out).
    [Fact]
    public void Line_ForHeroDecisionExplained_NamesBothItemsAndTheGap()
    {
        var line = EventNarration.Line(
            new HeroDecisionExplained(new HeroId(1), "Iron Sword", "Bronze Sword", "upgrade: +8 gear score for 20g", GapPermille: 350),
            State);
        Assert.NotNull(line);
        Assert.Contains("Iron Sword", line, StringComparison.Ordinal);
        Assert.Contains("Bronze Sword", line, StringComparison.Ordinal);
        Assert.Contains("350", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Line_ForHeroRankUp_NamesTheHeroAndRank()
    {
        var line = EventNarration.Line(new HeroRankUp(new HeroId(1), "Delver"), State);
        Assert.NotNull(line);
        Assert.Contains("Delver", line, StringComparison.Ordinal);
    }
}
