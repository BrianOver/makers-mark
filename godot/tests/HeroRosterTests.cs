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
/// P007 U4 (R12/R11/R15): the hero roster rebuilt as a <see cref="GridContainer"/> of
/// <c>UiKit.PortraitFrame</c> cards (each a themed toggle <see cref="Button"/>) replacing the
/// old <c>ItemList</c>. Every scenario proves the pre-rethink contract survives: every hero
/// name discoverable, <see cref="HeroesPanel.SelectHero"/> town-click routing (R20), dead-hero
/// state, and the KTD3 art-fallback guarantee — plus the new detail-pane gear
/// <c>ArtRect</c>/<c>StatChip</c> rows and the preserved <see cref="LedgerQuery.MarkTally"/> read.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class HeroRosterTests
{
    [TestCase]
    public void Roster_RendersCardPerHero_NamesDiscoverable_SelectingRendersGearAndMarkTally()
    {
        var ui = MountMainUi(new SimAdapter(RosterWorld()));
        try
        {
            var heroesText = RenderedText(ui.Heroes);
            AssertThat(heroesText).Contains("Geared1");
            AssertThat(heroesText).Contains("Fallen2");

            Find<Button>(ui.Heroes, "HeroCard_1");
            Find<Button>(ui.Heroes, "HeroCard_2");

            // Card click drives the same RenderDetail path SelectHero/the old ItemList did.
            PressEnabled(ui.Heroes, "HeroCard_1");

            var detailText = RenderedText(Find<VBoxContainer>(ui.Heroes, "HeroDetail"));
            AssertThat(detailText).Contains("Geared1");
            AssertThat(detailText).Contains("Trusty Blade");
            AssertThat(detailText).Contains("mark of You: 2 kills, 1 saves");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void DeadHero_RendersDiedState_WithoutCollapsingGrid()
    {
        var ui = MountMainUi(new SimAdapter(RosterWorld()));
        try
        {
            var heroesText = RenderedText(ui.Heroes);
            AssertThat(heroesText).Contains("DIED day 3");

            // Both cards still stand side by side — the dead hero didn't collapse the grid.
            var dead = Find<Button>(ui.Heroes, "HeroCard_2");
            var alive = Find<Button>(ui.Heroes, "HeroCard_1");
            AssertThat(dead.Visible).IsTrue();
            AssertThat(alive.Visible).IsTrue();
            AssertThat(dead.CustomMinimumSize.X > 0 && dead.CustomMinimumSize.Y > 0).IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void SelectHero_RoutesToRequestedHero_AndHighlightsItsCard()
    {
        var ui = MountMainUi(new SimAdapter(RosterWorld()));
        try
        {
            // Default selection lands on hero 1 (id order); explicitly route to hero 2 the way
            // MainUi's town-click handler does (R20).
            ui.Heroes.SelectHero(2);

            var detailText = RenderedText(Find<VBoxContainer>(ui.Heroes, "HeroDetail"));
            AssertThat(detailText).Contains("Fallen2");
            AssertThat(detailText).Contains("DIED day 3");

            AssertThat(Find<Button>(ui.Heroes, "HeroCard_2").ButtonPressed).IsTrue();
            AssertThat(Find<Button>(ui.Heroes, "HeroCard_1").ButtonPressed).IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void UnregisteredClassHero_RendersPlaceholderPortrait_StillShowingNameOnCard()
    {
        // KTD3 fallback: an id no art pipeline (or, here, no class registration) ever produced.
        // Hero 1 keeps a real registered class so the default auto-selection never asks
        // ClassRegistry to resolve the fake one — only the ROSTER CARD is under test here.
        var ui = MountMainUi(new SimAdapter(FallbackPortraitWorld()));
        try
        {
            var card = Find<Button>(ui.Heroes, "HeroCard_2");
            var cardText = RenderedText(card);
            AssertThat(cardText).Contains("Mystery2");
            AssertThat(cardText).Contains("Lv");

            var placeholders = card.FindChildren("ArtRectFallback", "PanelContainer", recursive: true, owned: false);
            AssertThat(placeholders.Count > 0).IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── Fixtures ──────────────────────────────────────────────────────────────────────────────

    private static Hero GearedHero(int id) => new(
        new HeroId(id), $"Geared{id}", "vanguard", Level: 4, MaxHp: 50, Gold: 20,
        new GearSet(new ItemId(90), null, null), ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 1, DiedOnDay: null);

    private static Hero DeadHero(int id) => new(
        new HeroId(id), $"Fallen{id}", "mystic", Level: 3, MaxHp: 40, Gold: 5,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty,
        Alive: false, DeepestFloorReached: 2, DiedOnDay: 3);

    private static Hero UnknownClassHero(int id) => new(
        new HeroId(id), $"Mystery{id}", "no-such-class", Level: 2, MaxHp: 30, Gold: 5,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 0, DiedOnDay: null);

    private static Item MarkedWeapon() => new(
        new ItemId(90), "no-such-recipe-either", "Trusty Blade", ItemSlot.Weapon, QualityGrade.Fine,
        new ItemStats(12, 0, 4), new MakersMark("You", 1),
        ImmutableList.Create(
            new ItemHistoryEntry(1, "kill", "cave rat"),
            new ItemHistoryEntry(1, "kill", "cave rat"),
            new ItemHistoryEntry(2, "save", "ally")));

    private static GameState RosterWorld()
    {
        var heroes = new[] { GearedHero(1), DeadHero(2) }
            .ToImmutableSortedDictionary(h => h.Id.Value, h => h);
        var items = new[] { MarkedWeapon() }.ToImmutableSortedDictionary(i => i.Id.Value, i => i);
        return GameFactory.NewGame(4242) with { Heroes = heroes, Items = items };
    }

    private static GameState FallbackPortraitWorld()
    {
        var heroes = new[] { GearedHero(1), UnknownClassHero(2) }
            .ToImmutableSortedDictionary(h => h.Id.Value, h => h);
        var items = new[] { MarkedWeapon() }.ToImmutableSortedDictionary(i => i.Id.Value, i => i);
        return GameFactory.NewGame(4243) with { Heroes = heroes, Items = items };
    }
}
#endif
