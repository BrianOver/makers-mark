#if GDUNIT_TESTS
using System.Collections.Immutable;
using System.Linq;
using GameSim;
using GameSim.Contracts;
using GameSim.Kernel;
using GdUnit4;
using Godot;
using GodotClient.Panels;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// LW3 (living-world plan, 2026-07-19-001): the shop customer choreography + coin flourish.
/// Headless-safe assertions only — properties/states/queued-run data, never pixels (the emote
/// glyph's <c>_Draw()</c> is decoration and stays untested here, same contract PointLight2D/
/// CPUParticles2D assertions hold elsewhere on this project).
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ShopStageTests
{
    [TestCase]
    public void Build_ShopInteriorArtShipped_UsesLitBackdrop()
    {
        // LW-art parity (2026-07-19) shipped "shop-interior" — see art/build/shop-interior.build.json.
        // This was "Build_NoShopInteriorArtYet_DegradesToGeneratedGradient" pre-ship; the graceful-
        // degrade branch it exercised is still live code (ShopStage.BuildBackdrop's else arm) but is
        // no longer reachable through this real, always-registered asset id.
        AssertThat(IconRegistry.Lit("shop-interior")).IsNotNull();

        var stage = new ShopStage();
        try
        {
            stage.Build();

            AssertThat(stage.HasBackdropArt).IsTrue();
            AssertThat(stage.World.FindChild("ShopBackdrop", true, false)).IsNotNull();
            AssertThat(stage.World.FindChild("ShopBackdropFallback", true, false)).IsNull();

            // Decoration only — never intercepts a click (same contract as LitTownOverlay).
            AssertThat(stage.MouseFilter).IsEqual(Control.MouseFilterEnum.Ignore);
        }
        finally
        {
            stage.Free();
        }
    }

    [TestCase]
    public void Build_WideWindow_NeverShowsOpaqueVoidRightOfDesignWidth()
    {
        // U5 fix: the SubViewport must be transparent (a wider host window than the 1024px design
        // space must never paint an opaque gray rect past x=1024) AND the strip's own footprint
        // must stay pinned at the fixed design size (ShrinkCenter, not ExpandFill) — otherwise
        // Godot's SubViewportContainer.Stretch auto-resizes the SubViewport itself to match
        // whatever width the container is handed, blowing the design space back open.
        var stage = new ShopStage();
        try
        {
            stage.Build();

            AssertThat(stage.Viewport.TransparentBg).IsTrue();
            AssertThat(stage.SizeFlagsHorizontal).IsEqual(Control.SizeFlags.ShrinkCenter);
            AssertThat(stage.CustomMinimumSize).IsEqual(new Vector2(1024, 220));
        }
        finally
        {
            stage.Free();
        }
    }

    [TestCase]
    public void QueueDay_SoldAndPassedEvents_QueuesOneStaggeredRunPerRelevantEvent()
    {
        var state = TwoHeroTwoItemWorld();
        var stage = new ShopStage();
        try
        {
            var dayEvents = new GameEvent[]
            {
                new ItemSold(new ItemId(101), new HeroId(1), 8, FromPlayerShop: true),
                new HeroPassedOnItem(new HeroId(2), new ItemId(101), "can't afford at 50g — has 10g"),
                new ItemSold(new ItemId(102), new HeroId(2), 40, FromPlayerShop: false), // rival sale — ignored
                new RecruitArrived(new HeroId(3)), // unrelated event type — ignored
            };

            stage.QueueDay(state, dayEvents);

            AssertThat(stage.QueuedRuns.Count).IsEqual(2);

            var bought = stage.QueuedRuns[0];
            AssertThat(bought.Bought).IsTrue();
            AssertThat(bought.Hero).IsEqual(new HeroId(1));
            AssertThat(bought.StartDelay).IsEqual(0.0);

            var passed = stage.QueuedRuns[1];
            AssertThat(passed.Bought).IsFalse();
            AssertThat(passed.Hero).IsEqual(new HeroId(2));
            AssertThat(passed.StartDelay).IsGreater(bought.StartDelay); // staggered, not simultaneous
        }
        finally
        {
            stage.Free();
        }
    }

    [TestCase]
    public void QueueDay_UnresolvableIds_DegradesToNoRunNoCrash()
    {
        var state = TwoHeroTwoItemWorld();
        var stage = new ShopStage();
        try
        {
            var dayEvents = new GameEvent[]
            {
                new ItemSold(new ItemId(999), new HeroId(1), 8, FromPlayerShop: true), // no such item
                new HeroPassedOnItem(new HeroId(999), new ItemId(101), "can't afford"), // no such hero
            };

            stage.QueueDay(state, dayEvents);

            AssertThat(stage.QueuedRuns.Count).IsEqual(0);
        }
        finally
        {
            stage.Free();
        }
    }

    [TestCase]
    public void ClassifySale_UndercutsRivalBaseline_IsHeart_AtOrAboveIsSmile()
    {
        // Baseline mirrors RivalCatalog's own fixed shelf-price formula: (Attack + Defense) * 2.
        var item = TestWeapon(attack: 10, defense: 0); // baseline 20

        AssertThat(ShopStage.ClassifySale(item, 15)).IsEqual(ShopStage.EmoteKind.Heart);
        AssertThat(ShopStage.ClassifySale(item, 20)).IsEqual(ShopStage.EmoteKind.Smile); // boundary: not a bargain
        AssertThat(ShopStage.ClassifySale(item, 25)).IsEqual(ShopStage.EmoteKind.Smile);
    }

    [TestCase]
    public void ClassifyPass_PinnedReasonMapping_UnaffordableIsFrown_EveryOtherReasonIsShrug()
    {
        AssertThat(ShopStage.ClassifyPass("can't afford at 45g — has 30g"))
            .IsEqual(ShopStage.EmoteKind.Frown);

        // Every other R8 pass-reason shape (role mismatch, too heavy, not-an-upgrade, "picked X
        // instead") maps to the same catch-all shrug — the LW3-pinned four-way table.
        AssertThat(ShopStage.ClassifyPass("shields don't suit a striker"))
            .IsEqual(ShopStage.EmoteKind.Shrug);
        AssertThat(ShopStage.ClassifyPass("too heavy for a mystic — 5 weight, carries at most 4"))
            .IsEqual(ShopStage.EmoteKind.Shrug);
        AssertThat(ShopStage.ClassifyPass("current Iron Sword is better"))
            .IsEqual(ShopStage.EmoteKind.Shrug);
        AssertThat(ShopStage.ClassifyPass("no gear-score improvement"))
            .IsEqual(ShopStage.EmoteKind.Shrug);
        AssertThat(ShopStage.ClassifyPass("picked Iron Sword instead — better gear score per gold"))
            .IsEqual(ShopStage.EmoteKind.Shrug);
    }

    [TestCase]
    public void Advance_QueuedRun_WalksInJudgesAndWalksOutThenIsFreed()
    {
        var state = TwoHeroTwoItemWorld();
        var stage = new ShopStage();
        try
        {
            stage.QueueDay(state, new GameEvent[]
            {
                new ItemSold(new ItemId(101), new HeroId(1), 8, FromPlayerShop: true),
            });

            AssertThat(stage.ActiveCustomerCount).IsEqual(0); // still pending, not yet started

            stage.Advance(0.01); // crosses the (zero) start delay — spawns the customer
            AssertThat(stage.ActiveCustomerCount).IsEqual(1);
            AssertThat(stage.World.FindChild("ShopCustomer_1", true, false)).IsNotNull();

            // Drive real per-frame-sized steps (mirrors TownScene.Animate/HeroSprite.Advance)
            // until the customer walks in, judges, and walks fully back out — one state
            // transition can complete per Advance call by design, so a single huge delta (unlike
            // real frame deltas) would only cross ONE boundary; capped so a stuck machine fails
            // the test instead of looping forever.
            for (var i = 0; i < 200 && stage.ActiveCustomerCount > 0; i++)
            {
                stage.Advance(0.1);
            }

            AssertThat(stage.ActiveCustomerCount).IsEqual(0);
            AssertThat(stage.World.FindChild("ShopCustomer_1", true, false)).IsNull();
        }
        finally
        {
            stage.Free();
        }
    }

    [TestCase]
    public void MorningSale_StagesOneBoughtCustomer_AndPopsTheGoldChip()
    {
        var ui = MountMainUi(new SimAdapter(GuaranteedSaleState()));
        try
        {
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Morning);
            var goldBefore = ui.Adapter.CurrentState.Player.Gold;

            ui.Adapter.AdvancePhase(); // Morning: the sale lands (OnPhaseCompleted stages the run)

            var sale = ui.Adapter.LastEvents.OfType<ItemSold>().FirstOrDefault(s => s.FromPlayerShop);
            AssertThat(sale).IsNotNull();
            AssertThat(ui.Adapter.CurrentState.Player.Gold).IsGreater(goldBefore);

            // U25 (c): the drawer's own ShopPanel.Stage strip is retired — InteriorStage's own
            // embedded ShopStage is the ONE choreography now, and it queues regardless of whether
            // the shop interior is currently staged (InteriorStage.OnPhaseCompleted's own contract).
            AssertThat(ui.Interior.ShopStage).IsNotNull();
            AssertThat(ui.Interior.ShopStage.QueuedRuns.Count).IsEqual(1);
            AssertThat(ui.Interior.ShopStage.QueuedRuns[0].Bought).IsTrue();

            // Gold-pop tween property assertions (accumulated-delta, no engine Tween): the
            // StatusBar's gold VALUE label bounces 1.0 -> ~1.25 -> 1.0 over 0.3s.
            var goldValue = Find<Label>(Find<HBoxContainer>(ui, "GoldChip"), "Value");
            AssertThat(goldValue.Scale).IsEqual(Vector2.One);

            ui._Process(0.15); // mid-pop
            AssertThat(goldValue.Scale.X).IsGreater(1.05f);

            ui._Process(0.15); // pop completes
            AssertThat(goldValue.Scale).IsEqual(Vector2.One);
            AssertThat(goldValue.Text).Contains($"{ui.Adapter.CurrentState.Player.Gold}g");
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── Fixtures ──────────────────────────────────────────────────────────────────────────────

    private static Item TestWeapon(int attack, int defense) => new(
        new ItemId(1), "test-recipe", "Test Blade", ItemSlot.Weapon, QualityGrade.Common,
        new ItemStats(attack, defense, 2), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);

    private static Hero Buyer(int id) => new(
        new HeroId(id), $"Buyer{id}", "striker", Level: 1, MaxHp: 24, Gold: 100,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 0, DiedOnDay: null);

    private static GameState TwoHeroTwoItemWorld()
    {
        var heroes = new[] { Buyer(1), Buyer(2) }.ToImmutableSortedDictionary(h => h.Id.Value, h => h);
        var items = new[]
        {
            new Item(new ItemId(101), "test-recipe-a", "Test Dagger", ItemSlot.Weapon, QualityGrade.Common,
                new ItemStats(5, 0, 2), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty),
            new Item(new ItemId(102), "test-recipe-b", "Test Shield", ItemSlot.Shield, QualityGrade.Common,
                new ItemStats(0, 4, 2), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty),
        }.ToImmutableSortedDictionary(i => i.Id.Value, i => i);

        return GameFactory.NewGame(5150) with { Heroes = heroes, Items = items };
    }

    /// <summary>
    /// A real starting campaign (full composition — faction drift, restock, recruit, gossip all
    /// run) with every starting hero's Gear cleared and Gold bumped so the first shopper in
    /// HeroId order provably buys the shelved item: its value ratio (gain/price = 5/8 = 0.625)
    /// strictly beats every RivalCatalog line's constant 0.5 ratio ((Atk+Def)*2 pricing against a
    /// zero starting gear score), so no rival-shelf item can win the "single best across both
    /// shelves" comparison regardless of which class shops first.
    /// </summary>
    private static GameState GuaranteedSaleState()
    {
        var baseState = GameComposition.NewCampaign(9002);
        var item = new Item(
            new ItemId(9001), "test-guaranteed-sale", "Test Blade", ItemSlot.Weapon, QualityGrade.Common,
            new ItemStats(Attack: 5, Defense: 0, Weight: 2), new MakersMark("You", 1),
            ImmutableList<ItemHistoryEntry>.Empty);

        var heroes = baseState.Heroes.Values
            .Select(h => h with { Gold = 500, Gear = GearSet.Empty })
            .ToImmutableSortedDictionary(h => h.Id.Value, h => h);

        return baseState with
        {
            Heroes = heroes,
            RivalShelf = ImmutableList<ShelfEntry>.Empty,
            Items = baseState.Items.Add(item.Id.Value, item),
            Player = baseState.Player with { Shelf = ImmutableList.Create(new ShelfEntry(item.Id, 8)) },
        };
    }
}
#endif
