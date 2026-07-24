#if GDUNIT_TESTS
using System.Collections.Immutable;
using System.Linq;
using GameSim;
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

    // U25 (c): StageStrip_IsMountedOutsideTheScrollBody_SoItStaysVisibleWhilScrolling deleted —
    // the drawer's own lit customer strip (ShopPanel.Stage) it pinned is retired as redundant now
    // that InteriorStage hosts the richer choreography for the shop interior (see
    // ShopStageTests.MorningSale_StagesOneBoughtCustomer_AndPopsTheGoldChip, retargeted at
    // ui.Interior.ShopStage, for the equivalent coverage that survives).

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

    [TestCase]
    public void VeteranHero_PassesOnPoorShelfItem_QualityTooLowReasonRendersUnderTheCard()
    {
        // U9 ("quality gets teeth"): a real Morning tick where every hero is a deep-floor
        // veteran and the only shelf item is Poor-grade proves the refusal reason is surfaced —
        // no HeroShoppingSystem/ShoppingAi mocking, the same real sim tick every other ShopPanel
        // scenario in this file drives.
        var ui = MountMainUi(new SimAdapter(VeteranPartyWithPoorShelfItemState()));
        try
        {
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Morning);
            ui.OpenPanel("Shop"); // U21: RefreshAll only refreshes the currently open drawer — open
                                  // it BEFORE the tick so the post-tick pass reasons actually render.

            ui.Adapter.AdvancePhase(); // Morning: every veteran evaluates and refuses the Poor item

            var passes = ui.Adapter.LastEvents.OfType<HeroPassedOnItem>().ToList();
            AssertThat(passes.Count).IsGreater(0);

            var shopText = RenderedText(ui.Shop);
            AssertThat(shopText).Contains("veteran");
            AssertThat(shopText).Contains("bring common or better"); // gate-b retune: gate is Common now (Poor still refused)

            // The item is still on the shelf (every veteran refused it) — proves this is the
            // "passed" render path, not a sale.
            AssertThat(ui.Adapter.CurrentState.Player.Shelf.Any(e => e.Item == PoorItemId)).IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    private static readonly ItemId PoorItemId = new(9101);

    /// <summary>Every default hero bumped to the U9 veteran floor threshold, gear cleared, gold
    /// plentiful (the fixture is about the QUALITY gate, not affordability/gear-score); the only
    /// shelf item is a Poor-grade weapon light enough for every class (mystic included) to
    /// consider; the rival shelf is cleared so nothing else competes for a hero's single buy.</summary>
    private static GameState VeteranPartyWithPoorShelfItemState()
    {
        var baseState = GameComposition.NewCampaign(9101);
        var item = new Item(
            PoorItemId, "test-veteran-quality-gate", "Rusty Test Blade", ItemSlot.Weapon,
            QualityGrade.Poor, new ItemStats(Attack: 5, Defense: 0, Weight: 3),
            new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);

        var heroes = baseState.Heroes.Values
            .Select(h => h with { Gold = 500, Gear = GearSet.Empty, DeepestFloorReached = 3 })
            .ToImmutableSortedDictionary(h => h.Id.Value, h => h);

        return baseState with
        {
            Heroes = heroes,
            RivalShelf = ImmutableList<ShelfEntry>.Empty,
            Items = baseState.Items.Add(item.Id.Value, item),
            Player = baseState.Player with { Shelf = ImmutableList.Create(new ShelfEntry(item.Id, 8)) },
        };
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
