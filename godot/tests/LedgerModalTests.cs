#if GDUNIT_TESTS
using System.Collections.Immutable;
using System.Linq;
using GameSim.Classes;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Kernel;
using GameSim.Materials;
using GdUnit4;
using Godot;
using GodotClient.Panels;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U7 (loop-legibility plan, R10 — "the recap ledger is nice, improve the text boxes and maybe
/// add visuals"): <see cref="LedgerModal"/> stays a pure projection of <see
/// cref="LedgerQuery.ReturnCards"/> (zero sim change) — hand-built <see cref="GameState"/>
/// fixtures driven directly through <see cref="LedgerModal.ShowFor"/>, mirroring the
/// <see cref="LegendsWallTests"/>/<see cref="RaidForecastBoard"/> idiom so a survivor, a death,
/// an attribution beat, and an ore offer can all be exercised in one deterministic day without
/// depending on RNG-driven expedition outcomes.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class LedgerModalTests
{
    private static readonly HeroId SurvivorId = new(1);
    private static readonly HeroId FallenId = new(2);
    private static readonly ItemId BeatItemId = new(500);

    /// <summary>One day: Thistle (vanguard) came home with loot, an ore offer, and a beat on her
    /// dagger; Borin (striker) did not come home at all — the exact "survivors + a death + loot"
    /// shape U7's own test-scenario line asks for.</summary>
    private static GameState DrivenDay()
    {
        var survivor = new Hero(
            SurvivorId, "Thistle", ClassRegistry.VanguardId, Level: 3, MaxHp: 30, Gold: 12,
            Gear: GearSet.Empty, Memories: ImmutableList<ItemMemory>.Empty, Alive: true,
            DeepestFloorReached: 2, DiedOnDay: null);
        var fallen = new Hero(
            FallenId, "Borin", ClassRegistry.StrikerId, Level: 2, MaxHp: 24, Gold: 5,
            Gear: GearSet.Empty, Memories: ImmutableList<ItemMemory>.Empty, Alive: false,
            DeepestFloorReached: 3, DiedOnDay: 1);

        var heroes = ImmutableSortedDictionary<int, Hero>.Empty
            .Add(SurvivorId.Value, survivor)
            .Add(FallenId.Value, fallen);

        var dagger = new Item(
            BeatItemId, "dagger", "Dagger", ItemSlot.Weapon, QualityGrade.Common,
            new ItemStats(8, 0, 2), new MakersMark("Thistle", 1), ImmutableList<ItemHistoryEntry>.Empty);

        var events = ImmutableList.Create<GameEvent>(
            new PartyReturned(ImmutableList.Create(SurvivorId)) { Id = new EventId(1), Day = 1 },
            new HeroDied(FallenId, 3, "a Cave Rat", GearSet.Empty) { Id = new EventId(2), Day = 1 },
            new LootIncomeReceived(SurvivorId, 8) { Id = new EventId(3), Day = 1 },
            new AttributionBeatEvent(
                BeatType.KillingBlow, BeatItemId, SurvivorId, Floor: 2,
                "Dagger landed the killing blow on the Cave Rat") { Id = new EventId(4), Day = 1 },
            new OreOffered(SurvivorId, MaterialRegistry.Copper, Quantity: 3, UnitPrice: 5) { Id = new EventId(5), Day = 1 });

        var baseState = GameFactory.NewGame(9101, heroes);
        return baseState with
        {
            Items = ImmutableSortedDictionary<int, Item>.Empty.Add(BeatItemId.Value, dagger),
            EventLog = events,
        };
    }

    [TestCase]
    public void DrivenDay_RendersEveryCard_WithResolvedIconsAndPortraits_EnumeratedFromReturnCards()
    {
        var ui = MountMainUi(new SimAdapter(DrivenDay()));
        try
        {
            ui.Ledger.ShowFor(1);

            var cards = LedgerQuery.ReturnCards(ui.Adapter.CurrentState, 1);
            AssertThat(cards.Count).IsEqual(2); // Thistle (survivor) + Borin (death)

            var ledgerText = RenderedText(ui.Ledger);

            // Enumerated from ReturnCards' own output — not a hand list (U7 test contract).
            for (var i = 0; i < cards.Count; i++)
            {
                var card = cards[i];
                var cardNode = Find<Control>(ui.Ledger, $"LedgerCard_{i}");

                AssertThat(ledgerText).Contains(card.HeroName);
                AssertThat(ledgerText).Contains(card.Survived ? "Returned safely" : "Did not return");

                // Every icon this card renders (portrait/fallback, beat item, ore) must resolve to
                // a real texture — never a silent blank slot (house rule). The expected COUNT is
                // derived from the card's own data (portrait + one fate-row icon [gold chip or
                // skull] + one per beat + one per ore offer), not a hand list — a mutation that
                // silently drops any one icon (e.g. the beat's item icon, or the gold chip) moves
                // this count and fails here, not just the weaker "at least one" check.
                var textures = cardNode
                    .FindChildren("*", nameof(TextureRect), recursive: true, owned: false)
                    .Cast<TextureRect>()
                    .ToList();
                var expectedIconCount = 1 + 1 + card.Beats.Count + card.OreOffers.Count;
                AssertThat(textures.Count)
                    .OverrideFailureMessage(
                        $"card {i} ('{card.HeroName}'): expected {expectedIconCount} icons "
                        + "(portrait + fate-row icon + one per beat + one per ore offer), found "
                        + $"{textures.Count} — an icon was silently dropped.")
                    .IsEqual(expectedIconCount);
                foreach (var rect in textures)
                {
                    AssertThat(rect.Texture)
                        .OverrideFailureMessage(
                            $"card {i} ('{card.HeroName}'): TextureRect '{rect.Name}' resolved to a "
                            + "null texture — an icon lookup silently went blank.")
                        .IsNotNull();
                }
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void SurvivorCard_AndDeathCard_CarryDistinctAccentBorders()
    {
        var ui = MountMainUi(new SimAdapter(DrivenDay()));
        try
        {
            ui.Ledger.ShowFor(1);

            var cards = LedgerQuery.ReturnCards(ui.Adapter.CurrentState, 1);
            var survivorIndex = cards.FindIndex(c => c.Hero == SurvivorId);
            var deathIndex = cards.FindIndex(c => c.Hero == FallenId);
            AssertThat(survivorIndex >= 0 && deathIndex >= 0).IsTrue();

            var survivorStyle = (StyleBoxFlat)Find<PanelContainer>(ui.Ledger, $"LedgerCard_{survivorIndex}")
                .GetThemeStylebox("panel");
            var deathStyle = (StyleBoxFlat)Find<PanelContainer>(ui.Ledger, $"LedgerCard_{deathIndex}")
                .GetThemeStylebox("panel");

            AssertThat(survivorStyle.BorderColor).IsEqual(GameTheme.CoolantColor);
            AssertThat(deathStyle.BorderColor).IsEqual(GameTheme.BloodColor);
            AssertThat(survivorStyle.BorderColor).IsNotEqual(deathStyle.BorderColor);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void EmptyDay_RendersEmptyState_NotABlankModal()
    {
        var ui = MountMainUi(new SimAdapter(DrivenDay()));
        try
        {
            ui.Ledger.ShowFor(5); // no returns were ever recorded for day 5

            AssertThat(LedgerQuery.ReturnCards(ui.Adapter.CurrentState, 5).IsEmpty).IsTrue();
            AssertThat(RenderedText(ui.Ledger)).Contains("No returns recorded for this day.");

            var icon = Find<TextureRect>(ui.Ledger, "EmptyStateIcon");
            AssertThat(icon.Texture).IsNotNull();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void TutorialTip_ShowsOnce_ThenNeverAgain()
    {
        var ui = MountMainUi(new SimAdapter(DrivenDay()));
        try
        {
            var firstTip = ui.Tutorial.ConsumeLedgerTip();
            AssertThat(firstTip).IsNotNull();

            ui.Ledger.ShowFor(1, firstTip);
            AssertThat(RenderedText(ui.Ledger)).Contains(firstTip!);

            // A manual reopen (or the next day's automatic reveal) asks again — MainUi's own
            // wiring only ever calls ConsumeLedgerTip once, so the second call must return null.
            var secondTip = ui.Tutorial.ConsumeLedgerTip();
            AssertThat(secondTip).IsNull();

            ui.Ledger.ShowFor(1, secondTip);
            AssertThat(RenderedText(ui.Ledger)).NotContains(firstTip!);
        }
        finally
        {
            Unmount(ui);
        }
    }
}
#endif
