#if GDUNIT_TESTS
using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using GameSim.Kernel;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// P007 U3 (R12/R11/R15): the storefront rebuilt around <c>UiKit.Section</c>/<c>Card</c>/
/// <c>StatChip</c>/<c>ArtRect</c> — every scenario drives the SAME sim reads (<c>state.Player.
/// Shelf</c>, unshelved player crafts, <c>state.RivalShelf</c>) and action queues
/// (<see cref="StockAction"/>/<see cref="SetPriceAction"/>/<see cref="UnstockAction"/>) the
/// pre-rethink panel used, through the real Controls (<see cref="Press"/>/
/// <see cref="PressEnabled"/>), proving only the visual composition changed.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ShopPanelTests
{
    private const int StockPrice = 42;

    [TestCase]
    public void CraftedAndStockedItem_RendersCard_WithNameQualityPriceAndControls()
    {
        var ui = MountMainUi();
        try
        {
            var itemId = CraftDagger(ui);

            Find<SpinBox>(ui.Shop, $"StockPrice_{itemId.Value}").Value = StockPrice;
            PressEnabled(ui.Shop, $"Stock_{itemId.Value}");
            ui.Adapter.AdvancePhase(); // lands the stock

            var item = ui.Adapter.CurrentState.Items[itemId.Value];
            var shopText = RenderedText(ui.Shop);
            AssertThat(shopText).Contains("Your Shelf");
            AssertThat(shopText).Contains(item.Name);
            AssertThat(shopText).Contains(item.Quality.ToString());
            AssertThat(shopText).Contains($"{StockPrice}g");

            // Reprice/Unstock controls survive the rethink under their pinned Names.
            Find<Button>(ui.Shop, $"Reprice_{itemId.Value}");
            Find<Button>(ui.Shop, $"Unstock_{itemId.Value}");
            Find<SpinBox>(ui.Shop, $"Price_{itemId.Value}");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void StageStrip_IsMountedOutsideTheScrollBody_SoItStaysVisibleWhilScrolling()
    {
        var ui = MountMainUi();
        try
        {
            AssertThat(ui.Shop.Stage).IsNotNull();

            // U5 fix: the lit customer strip must never live inside "Scroll" — if it did, it
            // would scroll away with the shelf list instead of staying fixed above it.
            var scroll = ui.Shop.FindChild("Scroll", recursive: true, owned: false);
            AssertThat(scroll).IsNotNull();
            AssertThat(IsDescendantOf(ui.Shop.Stage!, (Node)scroll!)).IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void ShelfCard_PriceChip_ShrinksToContent_InsteadOfStretchingFullPanelWidth()
    {
        var ui = MountMainUi();
        try
        {
            var itemId = CraftDagger(ui);
            Find<SpinBox>(ui.Shop, $"StockPrice_{itemId.Value}").Value = StockPrice;
            PressEnabled(ui.Shop, $"Stock_{itemId.Value}");
            ui.Adapter.AdvancePhase(); // lands the stock

            var chip = ui.Shop.FindChild("StatChip", recursive: true, owned: false) as Control;
            AssertThat(chip).IsNotNull();
            AssertThat(chip!.SizeFlagsHorizontal).IsNotEqual(Control.SizeFlags.ExpandFill);
        }
        finally
        {
            Unmount(ui);
        }
    }

    private static bool IsDescendantOf(Node node, Node ancestor)
    {
        for (var current = node.GetParent(); current is not null; current = current.GetParent())
        {
            if (current == ancestor)
            {
                return true;
            }
        }

        return false;
    }

    [TestCase]
    public void FreshCampaign_EmptyShelf_RendersThemedEmptyState_NotBlankPanel()
    {
        var ui = MountMainUi();
        try
        {
            AssertThat(ui.Adapter.CurrentState.Player.Shelf.IsEmpty).IsTrue();

            var shopText = RenderedText(ui.Shop);
            AssertThat(shopText).Contains("Your Shelf");
            AssertThat(shopText).Contains("craft at the forge");

            // The section itself still renders (themed panel + header), never a blank void.
            AssertThat(ui.Shop.FindChildren("*", "PanelContainer", recursive: true, owned: false).Count > 0)
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void PressingStockButton_QueuesStockAction_InPendingActions()
    {
        var ui = MountMainUi();
        try
        {
            var itemId = CraftDagger(ui);

            Find<SpinBox>(ui.Shop, $"StockPrice_{itemId.Value}").Value = StockPrice;
            PressEnabled(ui.Shop, $"Stock_{itemId.Value}");

            var pending = ui.Adapter.PendingActions.OfType<StockAction>().ToList();
            AssertThat(pending.Count).IsEqual(1);
            AssertThat(pending[0].Item).IsEqual(itemId);
            AssertThat(pending[0].Price).IsEqual(StockPrice);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void ShelfItem_WithUncommittedArt_RendersSlotIconFallback_StillShowingNameAndPrice()
    {
        // KTD3 fallback path: a RecipeId no art pipeline ever generated, so ArtRect must miss
        // the manifest and render the slot-icon placeholder — never a blank hole, never a crash.
        var ui = MountMainUi(new SimAdapter(ShelfWithUncommittedArt()));
        try
        {
            var shopText = RenderedText(ui.Shop);
            AssertThat(shopText).Contains("Mystery Blade");
            AssertThat(shopText).Contains("15g");

            var placeholders = ui.Shop.FindChildren("ArtRectFallback", "PanelContainer", recursive: true, owned: false);
            AssertThat(placeholders.Count > 0).IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Buy 2x copper then craft the scripted dagger, driven directly through the
    /// adapter (setup only — Forge interaction is exercised by MainUiTests/PlayableLoopTests).
    /// Leaves the sim with one unshelved player craft.</summary>
    private static ItemId CraftDagger(MainUi ui)
    {
        ui.Adapter.Queue(new BuyMaterialAction(ScriptedSession.CraftMaterial, ScriptedSession.CopperNeeded));
        ui.Adapter.AdvancePhase(); // Morning: buy lands
        ui.Adapter.Queue(new CraftAction(ScriptedSession.CraftRecipeId, ScriptedSession.CraftMaterial));
        ui.Adapter.AdvancePhase(); // Expedition: craft lands
        AssertThat(ui.Adapter.LastRejections.Count).IsEqual(0);
        ui.OpenPanel("Shop"); // U21: RefreshAll is visibility-gated — open it so the new unshelved
                              // craft's row (SpinBox/Stock button) actually exists to find/press
        return ScriptedSession.CraftedItem(ui.Adapter.CurrentState);
    }

    private static readonly ItemId MysteryItemId = new(501);

    private static GameState ShelfWithUncommittedArt()
    {
        var baseState = GameFactory.NewGame(9001);
        var item = new Item(
            MysteryItemId, "no-such-recipe-in-any-manifest", "Mystery Blade", ItemSlot.Weapon,
            QualityGrade.Common, new ItemStats(5, 0, 3), new MakersMark("You", 1),
            ImmutableList<ItemHistoryEntry>.Empty);

        return baseState with
        {
            Items = ImmutableSortedDictionary<int, Item>.Empty.Add(item.Id.Value, item),
            Player = baseState.Player with { Shelf = ImmutableList.Create(new ShelfEntry(item.Id, 15)) },
        };
    }
}
#endif
