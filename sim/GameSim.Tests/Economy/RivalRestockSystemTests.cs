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
