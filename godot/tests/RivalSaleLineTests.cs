#if GDUNIT_TESTS
using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Kernel;
using GdUnit4;
using Godot;
using GodotClient.Panels;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// P2-MEMORY-26 ("the rival takes a name"): the night card names the hero, the rival's price, and
/// the player's own price when a rival sale beat a still-shelved piece of the player's in the same
/// slot, stocked before today. <see cref="RivalSaleQueryTests"/> (sim side) pins the match rule
/// itself; this only pins that <see cref="LedgerModal"/> renders (or, on a miss, does not render)
/// the one line it produces.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class RivalSaleLineTests
{
    private static readonly ItemId RivalItemId = new(90);
    private static readonly ItemId PlayerItemId = new(10);
    private static readonly HeroId BuyerId = new(1);

    private static Item PlayerShelfItem() => new(
        PlayerItemId, "shortsword", "Emberbite", ItemSlot.Weapon, QualityGrade.Common,
        new ItemStats(6, 0, 1), new MakersMark("You", CraftedOnDay: 1), ImmutableList<ItemHistoryEntry>.Empty);

    private static Item RivalShopItem() => new(
        RivalItemId, "shortsword", "Rusty Shortsword", ItemSlot.Weapon, QualityGrade.Common,
        new ItemStats(6, 0, 1), Mark: null, ImmutableList<ItemHistoryEntry>.Empty);

    /// <summary>Day 5: a rival sale of a weapon, with the player's own weapon stocked on day 3
    /// (before today) at 40g.</summary>
    private static GameState NightWithRivalSale(int stockedDay)
    {
        var sale = new ItemSold(RivalItemId, BuyerId, Price: 22, FromPlayerShop: false)
        {
            Id = new EventId(1),
            Day = 5,
        };

        var state = GameFactory.NewGame(seed: 424242) with
        {
            Day = 5,
            Items = ImmutableSortedDictionary<int, Item>.Empty
                .Add(RivalItemId.Value, RivalShopItem())
                .Add(PlayerItemId.Value, PlayerShelfItem()),
            EventLog = ImmutableList.Create<GameEvent>(sale),
        };

        return state with
        {
            Player = state.Player with
            {
                Shelf = ImmutableList.Create(new ShelfEntry(PlayerItemId, 40, stockedDay)),
            },
        };
    }

    [TestCase]
    public void RivalSaleBeatingAStockedPiece_RendersTheNamedLine()
    {
        var ui = MountMainUi(new SimAdapter(NightWithRivalSale(stockedDay: 3)));
        try
        {
            ui.Ledger.ShowFor(5);

            var line = ui.Ledger.FindChild("RivalSaleLine_1_90", recursive: true, owned: false) as Label;

            AssertThat(line).IsNotNull();
            AssertThat(line!.Text).Contains("bought a rival Rusty Shortsword for 22g");
            AssertThat(line.Text).Contains("yours sat at 40g");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void RivalSaleAgainstAPieceStockedTheSameDay_RendersNoLine()
    {
        // The honest predicate: same-day is silence, never a guess about tick order.
        var ui = MountMainUi(new SimAdapter(NightWithRivalSale(stockedDay: 5)));
        try
        {
            ui.Ledger.ShowFor(5);

            AssertThat(ui.Ledger.FindChild("RivalSaleLine_1_90", recursive: true, owned: false)).IsNull();
        }
        finally
        {
            Unmount(ui);
        }
    }
}
#endif
