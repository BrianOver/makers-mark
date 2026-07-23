using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Economy;
using GameSim.Kernel;

namespace GameSim.Tests.Economy;

/// <summary>
/// Covers the rival vendor's restock loop (A3, R16-baseline-half): every Morning the
/// rival shelf is topped back up to one item per catalog line, deterministically and
/// with no RNG, BEFORE heroes shop (composition order pinned in HeroShopChoiceTests).
/// </summary>
public class RivalRestockSystemTests
{
    private sealed class TestSink : IEventSink
    {
        public List<GameEvent> Events { get; } = [];
        public void Emit(GameEvent gameEvent) => Events.Add(gameEvent);
    }

    private static (GameState State, List<GameEvent> Events) Run(GameState state)
    {
        var system = new RivalRestockSystem();
        var sink = new TestSink();
        var after = system.Process(state, new Pcg32(state.Rng), sink);
        return (after, sink.Events);
    }

    [Fact]
    public void SystemContract_MorningPhase_StableName()
    {
        var system = new RivalRestockSystem();
        Assert.Equal(DayPhase.Morning, system.Phase);
        Assert.Equal("rival-restock", system.Name);
    }

    [Fact]
    public void EmptyShelf_IsFullyStockedOnFirstMorning()
    {
        var start = GameFactory.NewGame(seed: 3);

        var (after, events) = Run(start);

        Assert.Equal(RivalCatalog.Entries.Count, after.RivalShelf.Count);
        Assert.Equal(start.NextItemId + RivalCatalog.Entries.Count, after.NextItemId);
        Assert.Empty(events); // restocking is not a sale — no events

        // Every catalog line is on the shelf at its fixed price, minted unmarked.
        foreach (var line in RivalCatalog.Entries)
        {
            var entry = Assert.Single(after.RivalShelf,
                e => after.Items[e.Item.Value].RecipeId == line.RecipeId);
            Assert.Equal(line.Price, entry.Price);
            var item = after.Items[entry.Item.Value];
            Assert.Null(item.Mark);
            Assert.Equal(QualityGrade.Common, item.Quality);
            Assert.Equal(line.Stats, item.Stats);
        }
    }

    [Fact]
    public void SoldSlot_IsRestockedNextMorning_OthersUntouched()
    {
        var (stocked, _) = Run(GameFactory.NewGame(seed: 3));

        // Simulate a sale: the first shelf entry leaves the shelf (item stays in the
        // world, as it does when a hero buys and equips it).
        var soldEntry = stocked.RivalShelf[0];
        var afterSale = stocked with { RivalShelf = stocked.RivalShelf.RemoveAt(0) };

        var (restocked, _) = Run(afterSale);

        Assert.Equal(RivalCatalog.Entries.Count, restocked.RivalShelf.Count);

        // The sold line is back — as a NEW item, not the sold one.
        var soldRecipe = stocked.Items[soldEntry.Item.Value].RecipeId;
        var replacement = Assert.Single(restocked.RivalShelf,
            e => restocked.Items[e.Item.Value].RecipeId == soldRecipe);
        Assert.NotEqual(soldEntry.Item, replacement.Item);

        // Untouched slots keep their exact item ids — no duplicate minting.
        foreach (var entry in afterSale.RivalShelf)
        {
            Assert.Contains(restocked.RivalShelf, e => e.Item == entry.Item);
        }

        Assert.Equal(afterSale.NextItemId + 1, restocked.NextItemId);
    }

    [Fact]
    public void FullShelf_RestockIsANoOp()
    {
        var (stocked, _) = Run(GameFactory.NewGame(seed: 3));

        var (again, events) = Run(stocked);

        Assert.Empty(events);
        Assert.Equal(SaveCodec.Serialize(stocked), SaveCodec.Serialize(again));
    }

    [Fact]
    public void ZeroMarketShare_ShelvesAtFixedCatalogPrice()
    {
        // Default/pre-G3 RivalMarketSharePermille == 0 must stay byte-identical to the fixed
        // catalog price — the identity-function guarantee DiscountedPrice's doc promises.
        var (after, _) = Run(GameFactory.NewGame(seed: 3) with { RivalMarketSharePermille = 0 });

        foreach (var line in RivalCatalog.Entries)
        {
            var entry = Assert.Single(after.RivalShelf, e => after.Items[e.Item.Value].RecipeId == line.RecipeId);
            Assert.Equal(line.Price, entry.Price);
        }
    }

    [Fact]
    public void FullMarketShare_DiscountsNewlyMintedStock_ButNeverBelowOneGold()
    {
        // Game-Feel Plan G3: at the 1000‰ ceiling the rival undercuts by up to
        // RivalRestockSystem.MaxDiscountPermille (40%) — the visible consequence of a fully
        // idle campaign, floored at 1 gold so a line is never free.
        var (after, _) = Run(GameFactory.NewGame(seed: 3) with { RivalMarketSharePermille = 1000 });

        foreach (var line in RivalCatalog.Entries)
        {
            var entry = Assert.Single(after.RivalShelf, e => after.Items[e.Item.Value].RecipeId == line.RecipeId);
            var discountPermille = (int)IntegerCurves.MulDiv(1000, RivalRestockSystem.MaxDiscountPermille, 1000);
            var expected = Math.Max(1, line.Price - (int)IntegerCurves.MulDiv(line.Price, discountPermille, 1000));
            Assert.Equal(expected, entry.Price);
            Assert.True(entry.Price <= line.Price, "discounted price must never exceed the base catalog price");
        }
    }

    [Fact]
    public void PartialMarketShare_DiscountsProportionally()
    {
        // Half the ceiling (500‰) should land at half the max discount — pins the linear scaling
        // (IntegerCurves.MulDiv round-to-nearest) rather than an all-or-nothing toggle.
        var (after, _) = Run(GameFactory.NewGame(seed: 3) with { RivalMarketSharePermille = 500 });

        var line = RivalCatalog.Entries.First(l => l.RecipeId == "rival-blade-2"); // Attack 20 -> price 40
        var entry = Assert.Single(after.RivalShelf, e => after.Items[e.Item.Value].RecipeId == line.RecipeId);
        Assert.True(entry.Price < line.Price);

        var maxDiscounted = Math.Max(1, line.Price - (int)IntegerCurves.MulDiv(line.Price, RivalRestockSystem.MaxDiscountPermille, 1000));
        Assert.True(entry.Price > maxDiscounted);
    }

    [Fact]
    public void AlreadyShelvedLines_KeepTheirOriginalPrice_OnlyFreshMintsRepricePrice()
    {
        // A discount is only applied at MINT time (the class doc's "fresh mints re-price" note) —
        // an untouched shelf entry from a previous, cheaper (or costlier) market-share morning must
        // not silently retag its price when the meter moves on a later morning.
        var (stocked, _) = Run(GameFactory.NewGame(seed: 3) with { RivalMarketSharePermille = 0 });
        var untouchedPrice = stocked.RivalShelf[0].Price;

        var (again, _) = Run(stocked with { RivalMarketSharePermille = 1000 }); // full shelf -> restock is a no-op anyway

        Assert.Equal(untouchedPrice, again.RivalShelf[0].Price);
    }

    [Fact]
    public void Restock_DrawsNoRng_TwoRunsIdentical()
    {
        var start = GameFactory.NewGame(seed: 3);
        var system = new RivalRestockSystem();

        var rngA = new Pcg32(start.Rng);
        var a = system.Process(start, rngA, new TestSink());
        var rngB = new Pcg32(start.Rng);
        var b = system.Process(start, rngB, new TestSink());

        Assert.Equal(SaveCodec.Serialize(a), SaveCodec.Serialize(b));
        Assert.Equal(start.Rng, rngA.Snapshot()); // stream untouched — fixed catalog needs no dice
    }
}
