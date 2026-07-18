using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Economy;
using GameSim.Heroes;
using GameSim.Kernel;

namespace GameSim.Tests.Economy;

/// <summary>
/// The economy's bookkeeping law: no U7 transfer creates or destroys gold.
///
/// Ledger of every gold flow in this composition:
///   - player shop sale:  hero → player  (net zero across player+heroes)
///   - ore purchase:      player → hero  (net zero across player+heroes)
///   - RIVAL sale:        hero → rival   (the rival absorbs it — gold LEAVES the
///     player+heroes total BY DESIGN; the rival's purse is deliberately unmodeled,
///     see HeroShoppingSystem.ApplyPurchase)
///   - expedition loot income: creates hero gold — U6/U8 territory, NOT composed here
///     and not this suite's to test.
///
/// So the invariant this property test asserts, tick by tick over a scripted
/// multi-day run: (player + all heroes) changes by EXACTLY minus the rival sale
/// prices of that tick — nothing else, ever.
/// </summary>
public class GoldConservationTests
{
    private static Item PlayerCraft(int id, string recipeId, string name, ItemSlot slot, int attack, int defense, int weight) => new(
        new ItemId(id), recipeId, name, slot, QualityGrade.Masterwork,
        new ItemStats(attack, defense, weight),
        new MakersMark("You", CraftedOnDay: 1), ImmutableList<ItemHistoryEntry>.Empty);

    private static GameKernel EconomyKernel() => new(
        ImmutableList.Create<IPhaseSystem>(new RivalRestockSystem(), new HeroShoppingSystem()),
        ImmutableList.Create<IActionHandler>(new ShopHandlers(), new OreMarketHandlers()));

    private static long TotalGold(GameState state)
    {
        long total = state.Player.Gold;
        foreach (var hero in state.Heroes.Values)
        {
            total += hero.Gold;
        }

        return total;
    }

    /// <summary>Start state: full roster, one player craft to sell, ore offers to buy.</summary>
    internal static GameState ScriptStart()
    {
        var sword = PlayerCraft(100, "longsword", "Longsword", ItemSlot.Weapon, attack: 32, defense: 0, weight: 5);
        return HeroRoster.InstallStartingRoster(GameFactory.NewGame(seed: 11)) with
        {
            Items = ImmutableSortedDictionary<int, Item>.Empty.Add(sword.Id.Value, sword),
            NextItemId = sword.Id.Value + 1,
            OpenOreOffers = ImmutableList.Create(
                new OreOffered(new HeroId(1), "iron", Quantity: 5, UnitPrice: 4),
                new OreOffered(new HeroId(5), "copper", Quantity: 10, UnitPrice: 2)),
        };
    }

    /// <summary>Three scripted days (5-phase: Morning, Expedition, Camp, ExpeditionDeep, Evening):
    /// stock + sell, buy ore across two evenings, keep shopping. Camp/Deep hold empty — ore buys
    /// stay on the real Evening tick (BuyOreAction is Evening-only).</summary>
    internal static ImmutableList<ImmutableList<PlayerAction>> ScriptTicks() => ImmutableList.Create(
        // Day 1
        ImmutableList.Create<PlayerAction>(new StockAction(new ItemId(100), 30)), // Morning
        ImmutableList<PlayerAction>.Empty,                                        // Expedition
        ImmutableList<PlayerAction>.Empty,                                        // Camp
        ImmutableList<PlayerAction>.Empty,                                        // ExpeditionDeep
        ImmutableList.Create<PlayerAction>(                                       // Evening
            new BuyOreAction(new HeroId(1), "iron", 3)),
        // Day 2
        ImmutableList<PlayerAction>.Empty,                                        // Morning
        ImmutableList<PlayerAction>.Empty,                                        // Expedition
        ImmutableList<PlayerAction>.Empty,                                        // Camp
        ImmutableList<PlayerAction>.Empty,                                        // ExpeditionDeep
        ImmutableList.Create<PlayerAction>(                                       // Evening
            new BuyOreAction(new HeroId(1), "iron", 2),
            new BuyOreAction(new HeroId(5), "copper", 10)),
        // Day 3
        ImmutableList<PlayerAction>.Empty,                                        // Morning
        ImmutableList<PlayerAction>.Empty,                                        // Expedition
        ImmutableList<PlayerAction>.Empty,                                        // Camp
        ImmutableList<PlayerAction>.Empty,                                        // ExpeditionDeep
        ImmutableList<PlayerAction>.Empty);                                       // Evening

    [Fact]
    public void TotalGold_ChangesOnlyByRivalSales_AcrossAScriptedMultiDayRun()
    {
        var kernel = EconomyKernel();
        var state = ScriptStart();

        var playerSales = 0;
        var rivalSales = 0;
        var oreBuys = 0;

        foreach (var actions in ScriptTicks())
        {
            var before = TotalGold(state);
            var tick = kernel.Tick(state, actions);
            Assert.Empty(tick.Rejected); // every scripted action must land

            long rivalGoldAbsorbed = 0;
            foreach (var sale in tick.Events.OfType<ItemSold>())
            {
                if (sale.FromPlayerShop)
                {
                    playerSales++;
                }
                else
                {
                    rivalSales++;
                    rivalGoldAbsorbed += sale.Price;
                }
            }

            oreBuys += actions.OfType<BuyOreAction>().Count();

            // P5 U3: the tariff moves gold too — every faction sink/source is a recorded
            // TariffApplied delta (KTD3). delta = playerCost − heroBase: positive burns gold
            // from the town total (surcharge sink), negative mints it (discount source).
            long tariffDelta = 0;
            foreach (var tariff in tick.Events.OfType<TariffApplied>())
            {
                tariffDelta += tariff.Delta;
            }

            // THE conservation law (extended, KTD3): the town total changes by exactly minus the
            // rival sales and minus the summed tariff deltas of that tick — nothing else, ever.
            Assert.Equal(before - rivalGoldAbsorbed - tariffDelta, TotalGold(tick.NewState));

            state = tick.NewState;
        }

        // The run must actually have exercised every transfer type, or the law above
        // proved nothing.
        Assert.True(playerSales >= 1, "script produced no player shop sale");
        Assert.True(rivalSales >= 1, "script produced no rival sale");
        Assert.Equal(3, oreBuys);

        // Ore fully bought out: materials arrived intact, offers consumed.
        Assert.Equal(5, state.Player.Materials["iron"]);
        Assert.Equal(10, state.Player.Materials["copper"]);
        Assert.Empty(state.OpenOreOffers);
    }

    [Fact]
    public void PlayerSaleAndOrePurchase_AreExactMoves_NeverMinting()
    {
        // Focused pair-check on top of the property run: one sale + one ore buy,
        // player and hero deltas mirror each other exactly.
        var kernel = EconomyKernel();
        var state = ScriptStart();

        // Morning: Torvald (H1) buys the 30g masterwork sword (see HeroShopChoiceTests).
        var morning = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new StockAction(new ItemId(100), 30)));
        var sale = Assert.Single(morning.Events.OfType<ItemSold>(), e => e.FromPlayerShop);
        Assert.Equal(new HeroId(1), sale.Buyer);
        Assert.Equal(state.Player.Gold + 30, morning.NewState.Player.Gold);
        Assert.Equal(state.Heroes[1].Gold - 30, morning.NewState.Heroes[1].Gold);

        // Evening: buy 3 iron at 4g from H1 — exact opposite direction. Advance through the
        // empty Expedition/Camp/ExpeditionDeep ticks first (5-phase day).
        var expedition = kernel.Tick(morning.NewState, ImmutableList<PlayerAction>.Empty); // -> Camp
        var camp = kernel.Tick(expedition.NewState, ImmutableList<PlayerAction>.Empty);    // -> ExpeditionDeep
        var deep = kernel.Tick(camp.NewState, ImmutableList<PlayerAction>.Empty);          // -> Evening
        var beforeEvening = deep.NewState;
        var evening = kernel.Tick(beforeEvening, ImmutableList.Create<PlayerAction>(
            new BuyOreAction(new HeroId(1), "iron", 3)));
        Assert.Empty(evening.Rejected);
        Assert.Equal(beforeEvening.Player.Gold - 12, evening.NewState.Player.Gold);
        Assert.Equal(beforeEvening.Heroes[1].Gold + 12, evening.NewState.Heroes[1].Gold);
        Assert.Equal(3, evening.NewState.Player.Materials["iron"]);

        // Per-purchase conservation (KTD3): playerOut == heroBaseIn + Σ tariffDelta. Standing is
        // neutral on this first buy, so delta is 0 and the two mirror exactly — TariffTests pins
        // the nonzero (discount/surcharge) cases.
        long tariffDelta = evening.Events.OfType<TariffApplied>().Sum(t => (long)t.Delta);
        var playerOut = beforeEvening.Player.Gold - evening.NewState.Player.Gold;
        var heroBaseIn = evening.NewState.Heroes[1].Gold - beforeEvening.Heroes[1].Gold;
        Assert.Equal(heroBaseIn + tariffDelta, playerOut);
        Assert.Equal(0, tariffDelta);
    }
}
