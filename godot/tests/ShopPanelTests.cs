#if GDUNIT_TESTS
using System.Collections.Immutable;
using System.Linq;
using GameSim;
using GameSim.Contracts;
using GameSim.Kernel;
using GdUnit4;
using Godot;
using GodotClient.Ui;
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

            // Reprice/Unstock controls survive the rethink under their pinned Names. U5: Price_
            // is now a PriceTag (design doc §B6, "a reprice IS a tag flip") — StockPrice_ stays a
            // SpinBox (MainUiTests still drives it as one).
            Find<Button>(ui.Shop, $"Reprice_{itemId.Value}");
            Find<Button>(ui.Shop, $"Unstock_{itemId.Value}");
            Find<PriceTag>(ui.Shop, $"Price_{itemId.Value}");
        }
        finally
        {
            Unmount(ui);
        }
    }

    // U25 (c): StageStrip_IsMountedOutsideTheScrollBody_SoItStaysVisibleWhilScrolling deleted —
    // the drawer's own lit customer strip (ShopPanel.Stage) it pinned is retired as redundant.
    // U4 (painted-interiors plan): the InteriorStage-hosted richer choreography that superseded it
    // was ALSO retired (InteriorStage deleted). U5 (world-and-interiors plan): ShopStage itself is
    // now deleted too — its choreography lives in Town2D.MarketLife2D (MarketLifeTests.cs), hosted
    // by the walkable market room. The gold-chip-pop coverage that survives independently of any of
    // this moved to DayAdvanceHudTests.MorningSale_PopsTheGoldChip.

    [TestCase]
    public void ShelfCard_PriceLabel_ShrinksToContent_InsteadOfStretchingFullPanelWidth()
    {
        var ui = MountMainUi();
        try
        {
            var itemId = CraftDagger(ui);
            Find<SpinBox>(ui.Shop, $"StockPrice_{itemId.Value}").Value = StockPrice;
            PressEnabled(ui.Shop, $"Stock_{itemId.Value}");
            ui.Adapter.AdvancePhase(); // lands the stock

            // UI-5: the shelf row's price now lives in its ListRow's fixed-width "Price" Label
            // (was a standalone StatChip wrapped in AddChip's ShrinkBegin fix) — same underlying
            // R7-class guard (must not stretch to the row's full width), different control.
            var price = Find<Label>(ui.Shop, "Price");
            AssertThat(price.SizeFlagsHorizontal).IsNotEqual(Control.SizeFlags.ExpandFill);
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

            var pending = ui.Adapter.AppliedThisPhase.OfType<StockAction>().ToList();
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

    // ── U5 (plan 2026-07-28-002, design doc §B6): restock as placement + price tags ────────────
    // KTD-A: every gesture (drag/drop, tag flip) terminates in the SAME public seam the pre-U5
    // button already called (PlaceOnShelf/RemoveFromShelf/Reprice), so these scenarios drive the
    // ACTUAL gesture — Control's `_GetDragData`/`_CanDropData`/`_DropData` are public virtuals
    // inherited from `Control`, so a plain `Control`-typed find can invoke the overrides on the
    // (private) DragHandle/DropZone subclasses directly — no mouse, no OS drag required.

    [TestCase]
    public void DropOnEmptyShelfSlot_QueuesTheIdenticalStockAction_TheStockButtonWouldProduce()
    {
        var ui = MountMainUi();
        try
        {
            var itemId = CraftDagger(ui);
            const int price = 77;
            Find<SpinBox>(ui.Shop, $"StockPrice_{itemId.Value}").Value = price;

            // Pick up the unshelved craft's card exactly as a real drag would: _GetDragData reads
            // the SAME live SpinBox the Stock button reads.
            var dragSource = Find<Control>(ui.Shop, $"UnshelvedCard_{itemId.Value}");
            var payload = dragSource._GetDragData(Vector2.Zero);

            var dropZone = Find<Control>(ui.Shop, "EmptyShelfSlot_0");
            AssertThat(dropZone._CanDropData(Vector2.Zero, payload)).IsTrue();
            dropZone._DropData(Vector2.Zero, payload);

            var pending = ui.Adapter.AppliedThisPhase.OfType<StockAction>().ToList();
            AssertThat(pending.Count).IsEqual(1);
            AssertThat(pending[0].Item).IsEqual(itemId);
            AssertThat(pending[0].Price).IsEqual(price);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void DragShelvedItemOffTheShelf_QueuesTheIdenticalUnstockAction_TheUnstockButtonWouldProduce()
    {
        var ui = MountMainUi();
        try
        {
            var itemId = CraftDagger(ui);
            Find<SpinBox>(ui.Shop, $"StockPrice_{itemId.Value}").Value = StockPrice;
            PressEnabled(ui.Shop, $"Stock_{itemId.Value}");
            ui.Adapter.AdvancePhase(); // lands the stock

            var dragSource = Find<Control>(ui.Shop, $"ShelfCard_{itemId.Value}");
            var payload = dragSource._GetDragData(Vector2.Zero);

            var dropZone = Find<Control>(ui.Shop, "BackRoomDropZone");
            AssertThat(dropZone._CanDropData(Vector2.Zero, payload)).IsTrue();
            dropZone._DropData(Vector2.Zero, payload);

            var pending = ui.Adapter.AppliedThisPhase.OfType<UnstockAction>().ToList();
            AssertThat(pending.Count).IsEqual(1);
            AssertThat(pending[0].Item).IsEqual(itemId);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void ShelfSlotDropZone_RejectsAShelvedItemsPayload_AndBackRoomRejectsAnUnshelvedOne()
    {
        // The two drop zones are NOT interchangeable — a shelved-item payload dropped on a shelf
        // slot (or an unshelved-craft payload dropped on the back room) must be refused so the
        // wrong drag can never masquerade as the right one.
        var ui = MountMainUi();
        try
        {
            var itemId = CraftDagger(ui);
            Find<SpinBox>(ui.Shop, $"StockPrice_{itemId.Value}").Value = StockPrice;
            PressEnabled(ui.Shop, $"Stock_{itemId.Value}");
            ui.Adapter.AdvancePhase(); // lands the stock

            var shelfPayload = Find<Control>(ui.Shop, $"ShelfCard_{itemId.Value}")._GetDragData(Vector2.Zero);
            AssertThat(Find<Control>(ui.Shop, "EmptyShelfSlot_0")._CanDropData(Vector2.Zero, shelfPayload)).IsFalse();

            Variant unshelvedPayload =
                new Godot.Collections.Dictionary { ["kind"] = "unshelved", ["itemId"] = itemId.Value, ["price"] = 5 };
            AssertThat(Find<Control>(ui.Shop, "BackRoomDropZone")._CanDropData(Vector2.Zero, unshelvedPayload)).IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void PriceTagEdit_QueuesSetPriceAction_WithExactlyTheShownInteger()
    {
        var ui = MountMainUi();
        try
        {
            var itemId = CraftDagger(ui);
            Find<SpinBox>(ui.Shop, $"StockPrice_{itemId.Value}").Value = StockPrice;
            PressEnabled(ui.Shop, $"Stock_{itemId.Value}");
            ui.Adapter.AdvancePhase(); // lands the stock

            var tag = Find<PriceTag>(ui.Shop, $"Price_{itemId.Value}");
            tag.SetValue(123);

            AssertThat(tag.Value).IsEqual(123);
            var pending = ui.Adapter.AppliedThisPhase.OfType<SetPriceAction>().ToList();
            AssertThat(pending.Count).IsEqual(1);
            AssertThat(pending[0].Item).IsEqual(itemId);
            AssertThat(pending[0].Price).IsEqual(123);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void PriceTag_CanNeverQueueAPriceBelowOne()
    {
        var ui = MountMainUi();
        try
        {
            var itemId = CraftDagger(ui);
            Find<SpinBox>(ui.Shop, $"StockPrice_{itemId.Value}").Value = StockPrice;
            PressEnabled(ui.Shop, $"Stock_{itemId.Value}");
            ui.Adapter.AdvancePhase(); // lands the stock

            var tag = Find<PriceTag>(ui.Shop, $"Price_{itemId.Value}");
            tag.SetValue(-50); // below the floor — the tag itself must clamp, never queue it raw

            AssertThat(tag.Value).IsEqual(1);
            var pending = ui.Adapter.AppliedThisPhase.OfType<SetPriceAction>().ToList();
            AssertThat(pending.Count).IsEqual(1);
            AssertThat(pending[0].Item).IsEqual(itemId);
            AssertThat(pending[0].Price).IsEqual(1);

            // Nudging further down (already at the floor) must not re-queue a duplicate action —
            // PriceTag.SetValue is a no-op once clamped-value == current value.
            tag.Nudge(-10);
            AssertThat(tag.Value).IsEqual(1);
            AssertThat(ui.Adapter.AppliedThisPhase.OfType<SetPriceAction>().Count()).IsEqual(1);
        }
        finally
        {
            Unmount(ui);
        }
    }
}
#endif
