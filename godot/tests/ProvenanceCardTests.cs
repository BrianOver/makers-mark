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
/// U5 ("your craft writes the legends" made touchable): <see cref="ProvenanceCard"/> is a pure
/// projection of <see cref="Item.History"/>/<see cref="Item.Mark"/>/<see cref="Item.CraftSubScores"/>
/// — zero sim change. Every scenario drives it through a REAL host panel's click (the shelf's
/// <c>ShopPanel</c>, the gear row's <c>HeroesPanel</c>) so the "opens from wherever items are
/// listed" contract is proven through the same <c>Pressed</c>-signal path every other panel in
/// this codebase uses (KTD5), not by calling <see cref="ProvenanceCard.ShowFor"/> directly.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ProvenanceCardTests
{
    private static readonly ItemId HistoryItemId = new(701);
    private static readonly ItemId FreshItemId = new(702);
    private static readonly ItemId ScoredItemId = new(703);
    private static readonly ItemId SignedItemId = new(704);

    [TestCase]
    public void MultiEntryHistory_RendersEachEntry_InDayAscendingOrder()
    {
        var ui = MountMainUi(new SimAdapter(ShelfWorld()));
        try
        {
            PressEnabled(ui.Shop, $"Provenance_{HistoryItemId.Value}");

            var card = Find<ProvenanceCard>(ui.Shop, "ProvenanceCard");
            AssertThat(card.Visible).IsTrue();
            AssertThat(card.ShownItemId).IsEqual(HistoryItemId);

            var text = RenderedText(card);
            // Day-ascending: day 1's two entries both precede day 2's, regardless of the
            // fixture's own (already day-ordered) append order — this proves the render,
            // not just an accidental pass-through.
            var day1First = text.IndexOf("Day 1 — kill: cave rat");
            var day1Second = text.IndexOf("Day 1 — kill: cave rat", day1First + 1);
            var day2 = text.IndexOf("Day 2 — save: ally");
            AssertThat(day1First).IsGreaterEqual(0);
            AssertThat(day1Second).IsGreater(day1First);
            AssertThat(day2).IsGreater(day1Second);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void FreshlyForgedItem_NoHistory_RendersMinimalCard_NotAnError()
    {
        var ui = MountMainUi(new SimAdapter(ShelfWorld()));
        try
        {
            PressEnabled(ui.Shop, $"Provenance_{FreshItemId.Value}");

            var card = Find<ProvenanceCard>(ui.Shop, "ProvenanceCard");
            AssertThat(card.Visible).IsTrue();
            AssertThat(card.ShownItemId).IsEqual(FreshItemId);

            var text = RenderedText(card);
            AssertThat(text).Contains("Fresh off the forge — no history yet.");
            AssertThat(text).Contains("Fresh Dagger"); // the item's own name still renders
            AssertThat(text).Contains("Forged by You on day 1"); // mark still renders
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void SaleEvents_InterleaveWithRecordedHistory_ByDay_NotAsATwoStackedLists()
    {
        // P2-MEMORY-06: a counter sale (day 0, before any recorded entry) and a commission (day
        // 3, after every recorded entry) both derive from the event log, not Item.History — this
        // proves they land in the SAME day-ascending list as the recorded kill/save entries, at
        // their own day, rather than appended as a separate block.
        var ui = MountMainUi(new SimAdapter(ShelfWorldWithSaleEvents()));
        try
        {
            PressEnabled(ui.Shop, $"Provenance_{HistoryItemId.Value}");

            var text = RenderedText(Find<ProvenanceCard>(ui.Shop, "ProvenanceCard"));
            var day0Sale = text.IndexOf("Day 0 — sold: Haggled off your counter.");
            var day1Kill = text.IndexOf("Day 1 — kill: cave rat");
            var day3Commission = text.IndexOf("Day 3 — commissioned: Delivered on commission.");

            AssertThat(day0Sale).IsGreaterEqual(0);
            AssertThat(day1Kill).IsGreater(day0Sale);
            AssertThat(day3Commission).IsGreater(day1Kill);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void ItemWithNoRecordedHistory_ButSold_RendersTheSaleLine_NotTheFreshMessage()
    {
        // FreshItemId carries an empty Item.History — without a derived line it would render
        // "Fresh off the forge", which would be a lie the moment the item has actually been sold.
        var ui = MountMainUi(new SimAdapter(ShelfWorldWithSaleEvents()));
        try
        {
            PressEnabled(ui.Shop, $"Provenance_{FreshItemId.Value}");

            var text = RenderedText(Find<ProvenanceCard>(ui.Shop, "ProvenanceCard"));
            AssertThat(text).Contains("Day 5 — sold: Left your shelf — bought sight-unseen.");
            AssertThat(text).NotContains("Fresh off the forge");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void ItemNeverSoldAndNoRecordedHistory_StillRendersTheHonestFreshMessage()
    {
        // ScoredItemId has empty Item.History and no channel event anywhere in this fixture's
        // log — the honest-empty-state contract must still hold once a sibling item's sale is on
        // the log (AllChannels filters by item id, so ScoredItemId's card must stay untouched).
        var ui = MountMainUi(new SimAdapter(ShelfWorldWithSaleEvents()));
        try
        {
            PressEnabled(ui.Shop, $"Provenance_{ScoredItemId.Value}");

            var text = RenderedText(Find<ProvenanceCard>(ui.Shop, "ProvenanceCard"));
            AssertThat(text).Contains("Fresh off the forge — no history yet.");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void MakersMarkAndThreeSubScores_Display()
    {
        var ui = MountMainUi(new SimAdapter(ShelfWorld()));
        try
        {
            PressEnabled(ui.Shop, $"Provenance_{ScoredItemId.Value}");

            var text = RenderedText(Find<ProvenanceCard>(ui.Shop, "ProvenanceCard"));
            AssertThat(text).Contains("Forged by You on day 2");
            AssertThat(text).Contains("Smelt");
            AssertThat(text).Contains("700‰");
            AssertThat(text).Contains("Forge");
            AssertThat(text).Contains("650‰");
            AssertThat(text).Contains("Quench");
            AssertThat(text).Contains("800‰");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void SignedWork_RendersMarkerAndLegendName()
    {
        var ui = MountMainUi(new SimAdapter(ShelfWorld()));
        try
        {
            PressEnabled(ui.Shop, $"Provenance_{SignedItemId.Value}");

            var card = Find<ProvenanceCard>(ui.Shop, "ProvenanceCard");
            var text = RenderedText(card);
            AssertThat(text).Contains("SIGNED WORK");
            AssertThat(text).Contains("Emberfall");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void UnsignedItem_NeverRendersSignedWorkMarker()
    {
        var ui = MountMainUi(new SimAdapter(ShelfWorld()));
        try
        {
            PressEnabled(ui.Shop, $"Provenance_{FreshItemId.Value}");

            var text = RenderedText(Find<ProvenanceCard>(ui.Shop, "ProvenanceCard"));
            AssertThat(text).NotContains("SIGNED WORK");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void OpeningFromShelf_ResolvesTheClickedItem_NotAPreviouslyOpenedOne()
    {
        var ui = MountMainUi(new SimAdapter(ShelfWorld()));
        try
        {
            // Open the history item first...
            PressEnabled(ui.Shop, $"Provenance_{HistoryItemId.Value}");
            var card = Find<ProvenanceCard>(ui.Shop, "ProvenanceCard");
            AssertThat(card.ShownItemId).IsEqual(HistoryItemId);
            AssertThat(RenderedText(card)).Contains("cave rat");

            // ...then a DIFFERENT shelf item — the same reused card instance must resolve to
            // the newly clicked item, not keep showing (or blend in) the previous one's data.
            PressEnabled(ui.Shop, $"Provenance_{ScoredItemId.Value}");
            AssertThat(card.ShownItemId).IsEqual(ScoredItemId);
            var scoredText = RenderedText(card);
            AssertThat(scoredText).Contains("Smelt");
            AssertThat(scoredText).NotContains("cave rat");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void HeroGear_HistoryButton_OpensTheWornItemsOwnProvenance()
    {
        var ui = MountMainUi(new SimAdapter(GearedHeroWorld()));
        try
        {
            ui.Heroes.SelectHero(1);
            PressEnabled(ui.Heroes, $"Provenance_{HistoryItemId.Value}");

            var card = Find<ProvenanceCard>(ui.Heroes, "ProvenanceCard");
            AssertThat(card.Visible).IsTrue();
            AssertThat(card.ShownItemId).IsEqual(HistoryItemId);
            AssertThat(RenderedText(card)).Contains("cave rat");
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── Fixtures ──────────────────────────────────────────────────────────────────────────────

    private static Item HistoryItem() => new(
        HistoryItemId, "no-such-recipe", "Storied Blade", ItemSlot.Weapon, QualityGrade.Fine,
        new ItemStats(10, 0, 3), new MakersMark("You", 1),
        ImmutableList.Create(
            new ItemHistoryEntry(1, "kill", "cave rat"),
            new ItemHistoryEntry(1, "kill", "cave rat"),
            new ItemHistoryEntry(2, "save", "ally")));

    private static Item FreshItem() => new(
        FreshItemId, "no-such-recipe", "Fresh Dagger", ItemSlot.Weapon, QualityGrade.Common,
        new ItemStats(6, 0, 2), new MakersMark("You", 1),
        ImmutableList<ItemHistoryEntry>.Empty);

    private static Item ScoredItem() => new(
        ScoredItemId, "no-such-recipe", "Tempered Axe", ItemSlot.Weapon, QualityGrade.Superior,
        new ItemStats(14, 0, 5), new MakersMark("You", 2),
        ImmutableList<ItemHistoryEntry>.Empty)
    {
        CraftSubScores = ImmutableList.Create(700, 650, 800),
    };

    /// <summary>Wave 4 (U19, "Signed Works"): a named artifact — the ONE new render this unit adds.</summary>
    private static Item SignedItem() => new(
        SignedItemId, "no-such-recipe", "Masterwork Longsword", ItemSlot.Weapon, QualityGrade.Masterwork,
        new ItemStats(24, 0, 5), new MakersMark("You", 3),
        ImmutableList<ItemHistoryEntry>.Empty)
    {
        SignedName = "Emberfall",
    };

    private static GameState ShelfWorld()
    {
        var baseState = GameFactory.NewGame(5501);
        var items = new[] { HistoryItem(), FreshItem(), ScoredItem(), SignedItem() }
            .ToImmutableSortedDictionary(i => i.Id.Value, i => i);
        var shelf = ImmutableList.Create(
            new ShelfEntry(HistoryItemId, 20),
            new ShelfEntry(FreshItemId, 10),
            new ShelfEntry(ScoredItemId, 40),
            new ShelfEntry(SignedItemId, 90));

        return baseState with
        {
            Items = items,
            Player = baseState.Player with { Shelf = shelf },
        };
    }

    /// <summary>
    /// P2-MEMORY-06 fixture: <see cref="ShelfWorld"/> plus an event log naming two of its items —
    /// a pre-history counter sale and a post-history commission for <see cref="HistoryItemId"/>,
    /// and a shelf sale for the otherwise-history-empty <see cref="FreshItemId"/>. <see
    /// cref="ScoredItemId"/> is deliberately left unnamed by any event, proving the honest-empty
    /// state survives a log that is non-empty for a SIBLING item.
    /// </summary>
    private static GameState ShelfWorldWithSaleEvents() => ShelfWorld() with
    {
        EventLog = ImmutableList.Create<GameEvent>(
            new CounterSaleClosed(new HeroId(1), HistoryItemId, Price: 10, Pinned: false)
                with { Id = new EventId(1), Day = 0 },
            new CommissionFulfilled(new HeroId(2), HistoryItemId, Premium: 5)
                with { Id = new EventId(2), Day = 3 },
            new ItemSold(FreshItemId, new HeroId(3), Price: 8, FromPlayerShop: true)
                with { Id = new EventId(3), Day = 5 }),
    };

    private static GameState GearedHeroWorld()
    {
        var hero = new Hero(
            new HeroId(1), "Wearer", "vanguard", Level: 4, MaxHp: 50, Gold: 20,
            new GearSet(HistoryItemId, null, null), ImmutableList<ItemMemory>.Empty,
            Alive: true, DeepestFloorReached: 1, DiedOnDay: null);

        var baseState = GameFactory.NewGame(5502);
        return baseState with
        {
            Heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(hero.Id.Value, hero),
            Items = ImmutableSortedDictionary<int, Item>.Empty.Add(HistoryItemId.Value, HistoryItem()),
        };
    }
}
#endif
