#if GDUNIT_TESTS
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using GameSim;
using GameSim.Advisor;
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
    public void StockingWithNoPriceInteraction_AutoPricesAtTheSuggestion_AndShowsWhereItCameFrom()
    {
        // Owner playtest note, verbatim: "The store pricing should be auto tbh - focus less on
        // this for now." The player must be able to sell without ever touching a price control —
        // this drives Stock exactly as-is, with no SpinBox edit, and proves the landed price is
        // the Advisor's own SuggestedPrice, shown on screen with its provenance.
        var ui = MountMainUi();
        try
        {
            var itemId = CraftDagger(ui);

            var suggested = SuggestedPrice.For(ui.Adapter.CurrentState.Items[itemId.Value]);
            var spin = Find<SpinBox>(ui.Shop, $"StockPrice_{itemId.Value}");
            AssertThat((int)spin.Value).IsEqual(suggested); // pre-filled — zero interaction needed

            // No blocking price prompt anywhere under the panel: a real modal would be an
            // AcceptDialog (ConfirmationDialog derives from it too), and none exists.
            AssertThat(ui.Shop.FindChildren("*", "AcceptDialog", recursive: true, owned: false).Count)
                .IsEqual(0);

            PressEnabled(ui.Shop, $"Stock_{itemId.Value}");
            ui.Adapter.AdvancePhase(); // lands the stock

            var shelf = ui.Adapter.CurrentState.Player.Shelf;
            AssertThat(shelf.Count).IsEqual(1);
            AssertThat(shelf[0].Item).IsEqual(itemId);
            AssertThat(shelf[0].Price).IsEqual(suggested);

            // The UI shows the chosen price AND names it as a suggestion — never a silent guess.
            var shopText = RenderedText(ui.Shop);
            AssertThat(shopText).Contains($"{suggested}g");
            AssertThat(shopText).Contains("suggested");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void OverridingTheSuggestedPrice_BeforeStocking_LandsTheCustomPrice_LabeledCustom()
    {
        // The suggestion must stay a default, not a floor or a lock — an override is still one
        // SpinBox edit away, and once it lands the UI must stop calling it "suggested".
        var ui = MountMainUi();
        try
        {
            var itemId = CraftDagger(ui);
            var suggested = SuggestedPrice.For(ui.Adapter.CurrentState.Items[itemId.Value]);
            var overridden = suggested + 37;

            Find<SpinBox>(ui.Shop, $"StockPrice_{itemId.Value}").Value = overridden;
            PressEnabled(ui.Shop, $"Stock_{itemId.Value}");
            ui.Adapter.AdvancePhase(); // lands the stock

            var shelf = ui.Adapter.CurrentState.Player.Shelf;
            AssertThat(shelf[0].Price).IsEqual(overridden);

            var shopText = RenderedText(ui.Shop);
            AssertThat(shopText).Contains($"{overridden}g");
            AssertThat(shopText).Contains("custom");
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

    // ── U2 (plan "who would buy this"): HeroForecast.ForShelfAsItStands surfaced read-only ─────
    // (sim/GameSim/Advisor/HeroForecast.cs) — was called by the CLI's hero card and by nothing in
    // the Godot client, so the player was choosing sell-or-hold blind while the sim already knew
    // the answer. Same forecast-exactness guarantee the sim tests pin (HeroForecastTests.cs):
    // this only proves the PRESENTATION reads it correctly, never re-derives the verdict itself.

    [TestCase]
    public void ForecastSection_KnownHeroAndKnownItem_RendersAForecastRowNamingBoth()
    {
        var ui = MountMainUi(new SimAdapter(KnownBuyerAndItemState()));
        try
        {
            var shopText = RenderedText(ui.Shop);
            AssertThat(shopText).Contains("Who Would Buy This");
            AssertThat(shopText).Contains(KnownForecastHeroName);
            AssertThat(shopText).Contains(KnownForecastItemName);
            AssertThat(shopText).Contains($"as the shelf stands: would buy {KnownForecastItemName}");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void FreshCampaign_EmptyShelf_ForecastSection_RendersHonestEmptyState_NotBlank()
    {
        var ui = MountMainUi();
        try
        {
            AssertThat(ui.Adapter.CurrentState.Player.Shelf.IsEmpty).IsTrue();

            var shopText = RenderedText(ui.Shop);
            AssertThat(shopText).Contains("Who Would Buy This");
            AssertThat(shopText).Contains("Nothing on the shelf to forecast");
        }
        finally
        {
            Unmount(ui);
        }
    }

    private const string KnownForecastHeroName = "Torvald the Forecast Buyer";
    private const string KnownForecastItemName = "Sunfire Blade";
    private static readonly ItemId KnownForecastItemId = new(93011);

    /// <summary>One hero (empty gear, plentiful gold, weapon-eligible class), one shelf item
    /// (an affordable strict upgrade), rival shelf cleared so nothing else competes for the buy —
    /// same shape as <c>HeroForecastTests.Forecast_PredictsTheExactItemTheRealSystemThenBuys</c>
    /// (sim/GameSim.Tests/Heroes/HeroForecastTests.cs), so the forecast is guaranteed a Buy verdict
    /// rather than hoping the default roster happens to want this item.</summary>
    private static GameState KnownBuyerAndItemState()
    {
        var baseState = GameComposition.NewCampaign(9301);
        var item = new Item(
            KnownForecastItemId, "test-forecast-sword", KnownForecastItemName, ItemSlot.Weapon,
            QualityGrade.Common, new ItemStats(Attack: 8, Defense: 0, Weight: 3),
            new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);

        var buyer = baseState.Heroes.Values.First();
        var heroes = baseState.Heroes.SetItem(
            buyer.Id.Value,
            buyer with
            {
                Name = KnownForecastHeroName,
                ClassId = "vanguard",
                Gold = 100,
                Gear = GearSet.Empty,
                DeepestFloorReached = 0,
            });

        return baseState with
        {
            Heroes = heroes,
            RivalShelf = ImmutableList<ShelfEntry>.Empty,
            Items = baseState.Items.Add(item.Id.Value, item),
            Player = baseState.Player with { Shelf = ImmutableList.Create(new ShelfEntry(item.Id, 20)) },
        };
    }

    // ── U7 (proof-the-player-never-sees plan): rival card art ─────────────────────────────────

    /// <summary>Same fixture shape as <c>LayoutTests.RivalShelfWorld</c> (a fresh campaign's
    /// RivalShelf starts empty — RivalRestockSystem only fills it over several real days), seeded
    /// here directly with one entry per ItemSlot so all three rival category icons are exercised
    /// in a single mount.</summary>
    private static GameState RivalShelfAllCategoriesWorld()
    {
        var weapon = GameSim.Economy.RivalCatalog.Entries.First(e => e.Slot == ItemSlot.Weapon);
        var shield = GameSim.Economy.RivalCatalog.Entries.First(e => e.Slot == ItemSlot.Shield);
        var armor = GameSim.Economy.RivalCatalog.Entries.First(e => e.Slot == ItemSlot.Armor);
        var items = new[]
            {
                GameSim.Economy.RivalCatalog.Mint(new ItemId(900), weapon),
                GameSim.Economy.RivalCatalog.Mint(new ItemId(901), shield),
                GameSim.Economy.RivalCatalog.Mint(new ItemId(902), armor),
            }
            .ToImmutableSortedDictionary(i => i.Id.Value, i => i);
        var shelf = ImmutableList.Create(
            new ShelfEntry(new ItemId(900), weapon.Price),
            new ShelfEntry(new ItemId(901), shield.Price),
            new ShelfEntry(new ItemId(902), armor.Price));

        return GameComposition.NewCampaign(9302) with { Items = items, RivalShelf = shelf };
    }

    /// <summary>
    /// U7 (proof-the-player-never-sees plan): before this unit, every rival card's ArtRect hit
    /// UiKit's no-manifest-hit placeholder branch (ArtRectFallback — a bordered box with the raw
    /// id as its caption) because AssetCatalog.ItemIconId(item.RecipeId) composed a synthetic id
    /// ("item-rival-blade-2") no PNG has ever existed for. IconRegistry.RivalCategoryArtId now
    /// resolves each rival item to one of three committed category icons instead. This proves the
    /// real-art branch (ArtRectCaptioned) renders for all three slots, and the placeholder never
    /// does — a card-level assertion, not just the id-resolution census in
    /// AssetResolutionCensusTests/ArtWiringCoverageTests.
    /// </summary>
    [TestCase]
    public async Task RivalCards_EveryCategory_RenderRealArt_NeverThePlaceholderBox()
    {
        var ui = MountMainUi(new SimAdapter(RivalShelfAllCategoriesWorld()));
        try
        {
            ui.OpenPanel("Shop");
            await SettleLayout(ui);

            foreach (var itemId in new[] { 900, 901, 902 })
            {
                var card = Find<PanelContainer>(ui.Shop, $"RivalCard_{itemId}");
                AssertThat(card.FindChild("ArtRectFallback", recursive: true, owned: false))
                    .OverrideFailureMessage(
                        $"RivalCard_{itemId} rendered the generic slot placeholder instead of its "
                        + "category icon — see IconRegistry.RivalCategoryArtId.")
                    .IsNull();
                AssertThat(card.FindChild("ArtRectCaptioned", recursive: true, owned: false))
                    .IsNotNull();
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── U8 (§11.12 plan, "shop counters identical and redundant — condense") ────────────────────

    private static readonly ItemId CondensedShelfItemId = new(9401);

    /// <summary>One shelved item, one hero already promoted to Active at the counter — the exact
    /// state under which Present/Suggest must appear ON the shelf card (see <see
    /// cref="ShelvedItem_AppearsInExactlyOneRow_NotTwo"/>/<see
    /// cref="PresentAndSuggest_RenderOnlyWhileACustomerIsActive"/>).</summary>
    private static GameState ShelfWithActiveCustomerState()
    {
        var baseState = GameComposition.NewCampaign(9401);
        var item = new Item(
            CondensedShelfItemId, "test-recipe", "Test Blade", ItemSlot.Weapon, QualityGrade.Common,
            new ItemStats(Attack: 5, Defense: 0, Weight: 2), new MakersMark("You", 1),
            ImmutableList<ItemHistoryEntry>.Empty);
        var heroId = baseState.Heroes.Values.First().Id;

        return baseState with
        {
            Items = baseState.Items.Add(item.Id.Value, item),
            Player = baseState.Player with { Shelf = ImmutableList.Create(new ShelfEntry(item.Id, 10)) },
            Counter = new CounterState(
                Queue: ImmutableList.Create(heroId),
                Active: heroId,
                Round: 0,
                InterestPermille: 0,
                PatienceRounds: 3,
                GoodwillPermille: 0,
                Presented: null,
                StandingOfferGold: null,
                Served: ImmutableSortedSet<int>.Empty,
                Closed: false),
        };
    }

    /// <summary>Same shelf, but the counter has never been opened (<c>Counter</c> is null) — the
    /// "no active customer" half of the same scenario.</summary>
    private static GameState ShelfWithNoActiveCustomerState()
    {
        var baseState = GameComposition.NewCampaign(9401);
        var item = new Item(
            CondensedShelfItemId, "test-recipe", "Test Blade", ItemSlot.Weapon, QualityGrade.Common,
            new ItemStats(Attack: 5, Defense: 0, Weight: 2), new MakersMark("You", 1),
            ImmutableList<ItemHistoryEntry>.Empty);

        return baseState with
        {
            Items = baseState.Items.Add(item.Id.Value, item),
            Player = baseState.Player with { Shelf = ImmutableList.Create(new ShelfEntry(item.Id, 10)) },
        };
    }

    /// <summary>Recursive descendant count by <see cref="Node.Name"/> — <see
    /// cref="UiTestSupport.Find{T}"/> only ever returns the FIRST match, which cannot by itself
    /// prove a name is unique. Local to this suite: no other test file needs a duplicate-detector.</summary>
    private static int CountDescendantsNamed(Node root, string name)
    {
        var count = 0;
        foreach (var child in root.GetChildren())
        {
            if (child.Name == name)
            {
                count++;
            }

            count += CountDescendantsNamed(child, name);
        }

        return count;
    }

    /// <summary>
    /// The pin for the whole unit (test scenario 1 of the plan): before this unit,
    /// <c>CounterPanel.BuildShelfActions</c> ("Present / Suggest") rendered a SECOND full card for
    /// every shelved item directly above this same list, in the same scroll — same item, same
    /// icon, same name/quality/price shape. That section is deleted outright, not merely emptied.
    /// </summary>
    [TestCase]
    public void ShelvedItem_AppearsInExactlyOneRow_NotTwo()
    {
        var ui = MountMainUi(new SimAdapter(ShelfWithActiveCustomerState()));
        try
        {
            ui.OpenPanel("Shop");

            AssertThat(CountDescendantsNamed(ui.Shop, $"ShelfCard_{CondensedShelfItemId.Value}"))
                .OverrideFailureMessage(
                    "A shelved item must render in exactly one card — the counter's old duplicate " +
                    "shelf list must be gone, not just hidden.")
                .IsEqual(1);

            AssertThat(RenderedText(ui.Shop).Contains("Present / Suggest"))
                .OverrideFailureMessage("The old 'Present / Suggest' section header still renders somewhere.")
                .IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Test scenario 3: Present/Suggest are genuinely ABSENT with no active customer —
    /// never a disabled-and-present button (which would still cost a look-then-discard on every
    /// shelf item) — and present, enabled, on the SAME card once a customer is active.</summary>
    [TestCase]
    public void PresentAndSuggest_RenderOnlyWhileACustomerIsActive()
    {
        var uiNoCustomer = MountMainUi(new SimAdapter(ShelfWithNoActiveCustomerState()));
        try
        {
            uiNoCustomer.OpenPanel("Shop");

            AssertThat(uiNoCustomer.Shop.FindChild($"Present_{CondensedShelfItemId.Value}", recursive: true, owned: false))
                .OverrideFailureMessage("Present must be ABSENT with no active customer, not merely disabled.")
                .IsNull();
            AssertThat(uiNoCustomer.Shop.FindChild($"Suggest_{CondensedShelfItemId.Value}", recursive: true, owned: false))
                .OverrideFailureMessage("Suggest must be ABSENT with no active customer, not merely disabled.")
                .IsNull();
        }
        finally
        {
            Unmount(uiNoCustomer);
        }

        var uiWithCustomer = MountMainUi(new SimAdapter(ShelfWithActiveCustomerState()));
        try
        {
            uiWithCustomer.OpenPanel("Shop");

            var present = Find<Button>(uiWithCustomer.Shop, $"Present_{CondensedShelfItemId.Value}");
            var suggest = Find<Button>(uiWithCustomer.Shop, $"Suggest_{CondensedShelfItemId.Value}");
            AssertThat(present.Disabled).IsFalse();
            AssertThat(suggest.Disabled).IsFalse();

            PressEnabled(uiWithCustomer.Shop, $"Present_{CondensedShelfItemId.Value}");
            AssertThat(uiWithCustomer.Adapter.AppliedThisPhase.OfType<PresentItemAction>().Single().Item)
                .IsEqual(CondensedShelfItemId);
        }
        finally
        {
            Unmount(uiWithCustomer);
        }
    }

    /// <summary>Test scenario 2, enumerated (not sampled): every shelf/counter verb reachable
    /// before this unit is still reachable after it, under the same <see cref="Node.Name"/>s
    /// existing tests already press by.</summary>
    [TestCase]
    public void EveryShelfAndCounterVerb_IsStillReachable_ByTheSameName()
    {
        var ui = MountMainUi(new SimAdapter(ShelfWithActiveCustomerState()));
        try
        {
            ui.OpenPanel("Shop");

            foreach (var name in new[]
                     {
                         $"Unstock_{CondensedShelfItemId.Value}", $"Reprice_{CondensedShelfItemId.Value}",
                         $"Provenance_{CondensedShelfItemId.Value}", $"Present_{CondensedShelfItemId.Value}",
                         $"Suggest_{CondensedShelfItemId.Value}", "Accept", "HoldFirm", "Counter", "CloseCounter",
                     })
            {
                AssertThat(Find<Button>(ui.Shop, name))
                    .OverrideFailureMessage($"'{name}' is no longer reachable after condensing the shelf lists.")
                    .IsNotNull();
            }
        }
        finally
        {
            Unmount(ui);
        }

        var uiClosed = MountMainUi();
        try
        {
            uiClosed.OpenPanel("Shop");
            AssertThat(Find<Button>(uiClosed.Shop, "OpenCounter")).IsNotNull();
        }
        finally
        {
            Unmount(uiClosed);
        }
    }

    /// <summary>Test scenario 7: a reflective orphan guard over the WHOLE <c>godot/scripts</c> and
    /// <c>godot/tests</c> trees — walks the real filesystem (mirrors <c>AudioTests
    /// .EveryCue_HasAtLeastOneProductionReference</c>'s <c>ProjectSettings.GlobalizePath</c> +
    /// <c>Directory.GetFiles</c> idiom) rather than a hand-listed set of known-good ids, so the
    /// NEXT deletion (a script/test file removed without its Godot-generated <c>.cs.uid</c>
    /// sidecar) cannot leave the same class of litter this unit found and deleted
    /// (<c>ShopStage.cs.uid</c>/<c>ShopStageTests.cs.uid</c>/<c>MonsterView3D.cs.uid</c>).</summary>
    [TestCase]
    public void NoUidSidecar_ExistsWithoutItsMatchingCsFile()
    {
        foreach (var resRoot in new[] { "res://scripts", "res://tests" })
        {
            var dir = ProjectSettings.GlobalizePath(resRoot);
            var uidFiles = System.IO.Directory.GetFiles(dir, "*.cs.uid", System.IO.SearchOption.AllDirectories);

            AssertThat(uidFiles.Length)
                .OverrideFailureMessage($"Found no .cs.uid files under {dir} — did the folder move?")
                .IsGreater(0);

            foreach (var uidFile in uidFiles)
            {
                var csFile = uidFile[..^".uid".Length];
                AssertThat(System.IO.File.Exists(csFile))
                    .OverrideFailureMessage($"'{uidFile}' has no matching '{csFile}' — an orphaned Godot sidecar left behind by a deleted script.")
                    .IsTrue();
            }
        }
    }
}
#endif
