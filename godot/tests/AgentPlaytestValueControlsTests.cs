#if GDUNIT_TESTS
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using GameSim.Classes;
using GameSim.Contracts;
using GameSim.Kernel;
using GdUnit4;
using GodotClient.Panels;
using GodotClient.Tools;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// fix/the-three-unreachable-decisions: an 8-lens adversarial audit (CLAUDE.md) found that the
/// haggle counter-price (<see cref="CoinStack"/>), the shop reprice tag (<see cref="PriceTag"/>),
/// and the entire bounty post form — floor AND reward (<see cref="MineCrossSection"/> +
/// <see cref="CoinStack"/>, both in <c>BountyPanel.cs</c>) — are drawn with <c>_Draw()</c> on plain
/// <c>Control</c>s, not <c>Button</c>s. <see cref="ScreenObservation.ObservedControls"/> only ever
/// enumerated Button/Label/RichTextLabel/ItemList, so none of these three ever reached
/// <c>state.json</c>, and <see cref="AgentPlaytestBridge"/>'s press/move/key/advance/stop
/// vocabulary had no way to change one even if it had — every Counter/Reprice/PostBounty press any
/// playtest ever made resubmitted the widget's own compiled-in default, and every one of those
/// presses logged as an ordinary success (confirmed against 85 sampled turnlog.md files: zero
/// Bounty/Reprice/Haggle/Counter presses in any of them).
///
/// <para>This suite pins the three-part fix: <see cref="IHarnessValueControl"/> makes the widgets
/// OBSERVABLE (<see cref="ScreenObservation.ObservedValueControls"/> / <c>ValueControlDigest</c>)
/// and DRIVABLE (<see cref="AgentPlaytestBridge"/>'s new <c>set</c> command, through the exact seam
/// a click/drag/keypress already uses); and the bridge's new guard reports — inside the outcome
/// string itself — whenever a Counter/Reprice/PostBounty press still submits the value it opened
/// with, so an unexercised decision can never again read as a real one.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class AgentPlaytestValueControlsTests
{
    private static readonly ItemId ShelvedItemId = new(9701);
    private const int ShelvedItemOpeningPrice = 40;
    private static readonly ItemId CounterShopItemId = new(9702);

    // ── observation: the three surfaces now reach the digest with their real values ──────────────

    [TestCase]
    public void Digest_Bounty_ExposesFloorAndReward_WithTheirRealOpeningValues()
    {
        var ui = MountMainUi();
        try
        {
            ui.OpenPanel("Bounties");
            var bridge = new AgentPlaytestBridge(ui);

            var digest = bridge.BuildDigest(ui, 1, "(start)");
            var floor = digest.ValueControls.SingleOrDefault(v => v.Name == "BountyFloor");
            var reward = digest.ValueControls.SingleOrDefault(v => v.Name == "BountyReward");

            AssertThat(floor)
                .OverrideFailureMessage(
                    "BountyFloor never reached the digest. ValueControls seen: " +
                    $"[{string.Join(", ", digest.ValueControls.Select(v => v.Name))}].")
                .IsNotNull();
            AssertThat(floor!.Kind).IsEqual(nameof(MineCrossSection));
            AssertThat(floor.Value).IsEqual(1); // BountyPanel's own opening selection
            AssertThat(floor.Settable).IsTrue();

            AssertThat(reward)
                .OverrideFailureMessage(
                    "BountyReward never reached the digest. ValueControls seen: " +
                    $"[{string.Join(", ", digest.ValueControls.Select(v => v.Name))}].")
                .IsNotNull();
            AssertThat(reward!.Kind).IsEqual(nameof(CoinStack));
            AssertThat(reward.Value).IsEqual(25); // BountyPanel.DefaultReward
            AssertThat(reward.Settable).IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void Digest_Shop_ExposesThePriceTag_WithTheShelvedItemsRealPrice()
    {
        var ui = MountMainUi(new SimAdapter(ShopWorldWithShelvedItem(ShelvedItemOpeningPrice)));
        try
        {
            ui.OpenPanel("Shop");
            var bridge = new AgentPlaytestBridge(ui);

            var digest = bridge.BuildDigest(ui, 1, "(start)");
            var tag = digest.ValueControls.SingleOrDefault(v => v.Name == $"Price_{ShelvedItemId.Value}");

            AssertThat(tag)
                .OverrideFailureMessage(
                    $"Price_{ShelvedItemId.Value} never reached the digest. ValueControls seen: " +
                    $"[{string.Join(", ", digest.ValueControls.Select(v => v.Name))}].")
                .IsNotNull();
            AssertThat(tag!.Kind).IsEqual(nameof(PriceTag));
            AssertThat(tag.Value).IsEqual(ShelvedItemOpeningPrice);
            AssertThat(tag.Settable).IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void Digest_Counter_ExposesTheHagglePrice_WithTheLiveStandingOffer()
    {
        const int standingOffer = 57;
        var ui = MountMainUi(new SimAdapter(CounterFixture(standingOffer)));
        try
        {
            ui.OpenPanel("Shop"); // CounterPanel is embedded inside ShopPanel
            var bridge = new AgentPlaytestBridge(ui);

            var digest = bridge.BuildDigest(ui, 1, "(start)");
            var price = digest.ValueControls.SingleOrDefault(v => v.Name == "CounterPrice");

            AssertThat(price)
                .OverrideFailureMessage(
                    "CounterPrice never reached the digest. ValueControls seen: " +
                    $"[{string.Join(", ", digest.ValueControls.Select(v => v.Name))}].")
                .IsNotNull();
            AssertThat(price!.Kind).IsEqual(nameof(CoinStack));
            AssertThat(price.Value).IsEqual(standingOffer);
            AssertThat(price.Settable).IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── actuation: 'set' changes the SUBMITTED value, not just the widget ────────────────────────

    [TestCase]
    public async Task SetCommand_OnBountyControls_ChangesWhatPostBountySubmits()
    {
        var ui = MountMainUi();
        try
        {
            ui.OpenPanel("Bounties");
            var bridge = new AgentPlaytestBridge(ui);

            // 60g, not 777g: a fresh campaign starts with 100g on hand (PostBounty escrows the
            // reward immediately — GatePostButton disables the button once the reward exceeds the
            // player's own gold, per BountyPanel.GatePostButton), so the value must stay
            // affordable while still differing from BountyPanel.DefaultReward (25).
            var setFloor = await bridge.Apply(ui, new AgentCommand("set", "BountyFloor", Value: 5));
            AssertThat(setFloor).StartsWith("set BountyFloor -> 1 to 5");
            var setReward = await bridge.Apply(ui, new AgentCommand("set", "BountyReward", Value: 60));
            AssertThat(setReward).StartsWith("set BountyReward -> 25 to 60");

            var outcome = await bridge.Apply(ui, new AgentCommand("press", "PostBounty"));
            AssertThat(outcome)
                .OverrideFailureMessage($"PostBounty was refused or wrongly guarded after real 'set' commands: '{outcome}'.")
                .NotContains("GUARD");

            var posted = ui.Adapter.AppliedThisPhase.OfType<PostBountyAction>().ToList();
            AssertThat(posted.Count).IsEqual(1);
            AssertThat(posted[0].TargetFloor).IsEqual(5);
            AssertThat(posted[0].RewardGold).IsEqual(60);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public async Task SetCommand_OnPriceTag_QueuesTheRealSetPriceAction_ThroughTheSameSeamAClickUses()
    {
        var ui = MountMainUi(new SimAdapter(ShopWorldWithShelvedItem(ShelvedItemOpeningPrice)));
        try
        {
            ui.OpenPanel("Shop");
            var bridge = new AgentPlaytestBridge(ui);

            var outcome = await bridge.Apply(ui, new AgentCommand("set", $"Price_{ShelvedItemId.Value}", Value: 123));
            AssertThat(outcome).StartsWith($"set Price_{ShelvedItemId.Value} -> {ShelvedItemOpeningPrice} to 123");

            // PriceTag.ValueChanged queues SetPriceAction immediately (ShopPanel.EnsureBuilt) — the
            // same thing a real scroll-wheel/keyboard edit does (see ShopPanelTests'
            // PriceTagEdit_QueuesSetPriceAction_WithExactlyTheShownInteger).
            var queued = ui.Adapter.AppliedThisPhase.OfType<SetPriceAction>().ToList();
            AssertThat(queued.Count).IsEqual(1);
            AssertThat(queued[0].Item).IsEqual(ShelvedItemId);
            AssertThat(queued[0].Price).IsEqual(123);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public async Task SetCommand_OnCounterPrice_ChangesWhatCounterSubmits()
    {
        var ui = MountMainUi(new SimAdapter(CounterFixture(standingOffer: 50)));
        try
        {
            ui.OpenPanel("Shop");
            var bridge = new AgentPlaytestBridge(ui);

            var setOutcome = await bridge.Apply(ui, new AgentCommand("set", "CounterPrice", Value: 80));
            AssertThat(setOutcome).StartsWith("set CounterPrice -> 50 to 80");

            var outcome = await bridge.Apply(ui, new AgentCommand("press", "Counter"));
            AssertThat(outcome).NotContains("GUARD");

            var haggle = ui.Adapter.AppliedThisPhase.OfType<HaggleResponseAction>().Single();
            AssertThat(haggle.Kind).IsEqual(HaggleResponseKind.Counter);
            AssertThat(haggle.Price!.Value).IsEqual(80);
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── the loud guard: an unexercised decision must be reported, never silently accepted ────────

    [TestCase]
    public async Task PressPostBounty_WithoutEverSettingFloorOrReward_IsGuarded_ButStillSubmits()
    {
        var ui = MountMainUi();
        try
        {
            ui.OpenPanel("Bounties");
            var bridge = new AgentPlaytestBridge(ui);

            var digest = bridge.BuildDigest(ui, 1, "(start)"); // the driver's own first observation
            AssertThat(digest.ValueControls.Any(v => v.Name == "BountyFloor")).IsTrue();

            var outcome = await bridge.Apply(ui, new AgentCommand("press", "PostBounty"));

            AssertThat(outcome)
                .OverrideFailureMessage($"Expected a GUARD marker in the outcome, got: '{outcome}'.")
                .Contains("GUARD");
            AssertThat(outcome).Contains("BountyFloor still at its opening default (1)");
            AssertThat(outcome).Contains("BountyReward still at its opening default (25)");

            // Reported, not silently accepted — and still SUBMITTED: a real player who never
            // touched either control would still post at the defaults, so the guard must never
            // block the press, only make its emptiness impossible to miss.
            var posted = ui.Adapter.AppliedThisPhase.OfType<PostBountyAction>().Single();
            AssertThat(posted.TargetFloor).IsEqual(1);
            AssertThat(posted.RewardGold).IsEqual(25);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public async Task PressPostBounty_AfterSettingOnlyReward_GuardsOnlyTheFloor()
    {
        var ui = MountMainUi();
        try
        {
            ui.OpenPanel("Bounties");
            var bridge = new AgentPlaytestBridge(ui);
            bridge.BuildDigest(ui, 1, "(start)");

            // 60g, not 999g — stays affordable against a fresh campaign's 100g on hand (see the
            // sibling test's own note on GatePostButton), so this press is refused for the FLOOR
            // guard reason under test, never for insufficient gold.
            await bridge.Apply(ui, new AgentCommand("set", "BountyReward", Value: 60));
            var outcome = await bridge.Apply(ui, new AgentCommand("press", "PostBounty"));

            AssertThat(outcome).Contains("GUARD");
            AssertThat(outcome).Contains("BountyFloor still at its opening default (1)");
            AssertThat(outcome).NotContains("BountyReward still at its opening default");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public async Task PressReprice_WithoutEverSettingThePriceTag_IsGuarded()
    {
        var ui = MountMainUi(new SimAdapter(ShopWorldWithShelvedItem(ShelvedItemOpeningPrice)));
        try
        {
            ui.OpenPanel("Shop");
            var bridge = new AgentPlaytestBridge(ui);
            bridge.BuildDigest(ui, 1, "(start)");

            var outcome = await bridge.Apply(ui, new AgentCommand("press", $"Reprice_{ShelvedItemId.Value}"));

            AssertThat(outcome)
                .OverrideFailureMessage($"Expected a GUARD marker in the outcome, got: '{outcome}'.")
                .Contains("GUARD");
            AssertThat(outcome)
                .Contains($"Price_{ShelvedItemId.Value} still at its opening default ({ShelvedItemOpeningPrice})");
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── regression: the four PRE-EXISTING observable node types are untouched ────────────────────

    [TestCase]
    public void ExistingObservedControls_StillOnlyEverEnumerateButtons_ValueControlsAreASeparateList()
    {
        var ui = MountMainUi();
        try
        {
            ui.OpenPanel("Bounties");

            var buttons = ScreenObservation.ObservedControls(ui);
            AssertThat(buttons.Any(b => b.Name == "PostBounty"))
                .OverrideFailureMessage("PostBounty (a real Button) must still enumerate via ObservedControls.")
                .IsTrue();
            AssertThat(buttons.Any(b => b.Name == "BountyFloor" || b.Name == "BountyReward"))
                .OverrideFailureMessage(
                    "BountyFloor/BountyReward leaked into ObservedControls (Button-only) — they must " +
                    "only ever appear in ObservedValueControls.")
                .IsFalse();

            var values = ScreenObservation.ObservedValueControls(ui);
            AssertThat(values.Any(v => v.Node.Name == "BountyFloor")).IsTrue();
            AssertThat(values.Any(v => v.Node.Name == "PostBounty"))
                .OverrideFailureMessage("PostBounty (a Button) must never appear in ObservedValueControls.")
                .IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void FreshDigest_PreExistingFieldsAreUnaffectedByTheNewValueControlsField()
    {
        // Same assertion shape as AgentPlaytestBridgeTests.FreshDigest_ListsDayPhaseAndAtLeastOneEnabledControl
        // — proves the pre-existing digest fields did not shrink or change shape now that a new field
        // (valueControls) sits alongside them.
        var ui = MountMainUi();
        try
        {
            var bridge = new AgentPlaytestBridge(ui);
            var digest = bridge.BuildDigest(ui, turn: 1, lastOutcome: "(start)");

            AssertThat(digest.Controls.Count)
                .OverrideFailureMessage("The pre-existing Button-only Controls digest is now empty.")
                .IsGreater(0);
            AssertThat(digest.ScreenText.Count)
                .OverrideFailureMessage("The pre-existing screenText digest is now empty.")
                .IsGreater(0);
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── fixtures ──────────────────────────────────────────────────────────────────────────────────

    private static GameState ShopWorldWithShelvedItem(int price)
    {
        var baseState = GameFactory.NewGame(97001);
        var item = new Item(
            ShelvedItemId, "test-recipe", "Test Dagger", ItemSlot.Weapon, QualityGrade.Common,
            new ItemStats(Attack: 3, Defense: 0, Weight: 1), new MakersMark("You", 1),
            ImmutableList<ItemHistoryEntry>.Empty);

        return baseState with
        {
            Items = ImmutableSortedDictionary<int, Item>.Empty.Add(ShelvedItemId.Value, item),
            Player = baseState.Player with { Shelf = ImmutableList.Create(new ShelfEntry(ShelvedItemId, price)) },
        };
    }

    /// <summary>A world with a live, mid-haggle <see cref="CounterState"/> already installed, at a
    /// caller-chosen standing offer — mirrors <c>CounterPanelTests.CounterFixture</c>'s shape.</summary>
    private static GameState CounterFixture(int standingOffer)
    {
        var hero = new Hero(
            new HeroId(1), "Buyer1", ClassRegistry.StrikerId, Level: 1, MaxHp: 24, Gold: 500,
            GearSet.Empty, ImmutableList<ItemMemory>.Empty,
            Alive: true, DeepestFloorReached: 0, DiedOnDay: null);
        var heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(hero.Id.Value, hero);
        var baseState = GameFactory.NewGame(97002, heroes);

        var counter = new CounterState(
            Queue: ImmutableList.Create(hero.Id),
            Active: hero.Id,
            Round: 1,
            InterestPermille: 100,
            PatienceRounds: 3,
            GoodwillPermille: 0,
            Presented: CounterShopItemId,
            StandingOfferGold: standingOffer,
            Served: ImmutableSortedSet<int>.Empty,
            Closed: false);

        var item = new Item(
            CounterShopItemId, "test-recipe", "Test Blade", ItemSlot.Weapon, QualityGrade.Common,
            new ItemStats(Attack: 5, Defense: 0, Weight: 2), new MakersMark("You", 1),
            ImmutableList<ItemHistoryEntry>.Empty);

        return baseState with
        {
            Items = ImmutableSortedDictionary<int, Item>.Empty.Add(CounterShopItemId.Value, item),
            Player = baseState.Player with { Shelf = ImmutableList.Create(new ShelfEntry(CounterShopItemId, 8)) },
            Counter = counter,
        };
    }
}
#endif
