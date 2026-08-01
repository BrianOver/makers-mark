#if GDUNIT_TESTS
using System.Collections.Immutable;
using GameSim.Classes;
using GameSim.Contracts;
using GameSim.Kernel;
using GdUnit4;
using Godot;
using GodotClient.Panels;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// P007 polish (R14/KTD2/KTD3): the tavern gossip feed rebuilt around one
/// <c>UiKit.Section</c> ("TAVERN GOSSIP") holding a themed <c>Card</c> per line — every
/// scenario proves the same sim read (<see cref="GossipEmitted"/> off <c>state.EventLog</c>,
/// newest-first) the pre-polish panel used still renders, through the real themed Controls,
/// and that the panel is never a blank void on either edge: no gossip yet, or a line whose
/// glyph has no generated art (gossip has no per-line art concept at all).
///
/// <para>Brian-playtest fix ("one apologetic line of text and ~370px of blank panel"): the
/// suite below covers the two sections that give the Tavern real content beyond the gossip
/// feed — IN THE COMMON ROOM (who's here, what they're carrying, who's grumbling) and OUT AT
/// THE MINE (who's away and their committed target floor, never the outcome). Every scenario
/// is a pure state fixture (the <c>ProvenanceCardTests.GearedHeroWorld</c> precedent), not a
/// scripted playthrough — these are unit-level projections, same as the gossip cases above.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TavernPanelTests
{
    [TestCase]
    public void GossipLine_Renders_UnderThemedSection_WithDayAndQuoteText()
    {
        var ui = MountMainUi(new SimAdapter(WorldWithGossip()));
        try
        {
            var tavernText = RenderedText(ui.Tavern);
            AssertThat(tavernText).Contains("TAVERN GOSSIP");
            AssertThat(tavernText).Contains("[day 3]");
            AssertThat(tavernText).Contains("A dagger sold for a fortune.");

            // The section itself renders (themed panel), never a blank void.
            AssertThat(ui.Tavern.FindChildren("*", "PanelContainer", recursive: true, owned: false).Count > 0)
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void FreshCampaign_NoGossipYet_RendersThemedEmptyState_NotBlankPanel()
    {
        var ui = MountMainUi();
        try
        {
            AssertThat(ui.Adapter.CurrentState.EventLog.IsEmpty).IsTrue();

            var tavernText = RenderedText(ui.Tavern);
            AssertThat(tavernText).Contains("TAVERN GOSSIP");
            AssertThat(tavernText).Contains("quiet");

            AssertThat(ui.Tavern.FindChildren("*", "PanelContainer", recursive: true, owned: false).Count > 0)
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void GossipLine_HasNoGeneratedArt_RendersArtRectFallback()
    {
        // KTD3 fallback path: a gossip line has no per-line art concept, so ArtRect always
        // misses the manifest and renders the themed placeholder — never a blank hole.
        var ui = MountMainUi(new SimAdapter(WorldWithGossip()));
        try
        {
            var placeholders =
                ui.Tavern.FindChildren("ArtRectFallback", "PanelContainer", recursive: true, owned: false);
            AssertThat(placeholders.Count > 0).IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    private static GameState WorldWithGossip()
    {
        var baseState = GameFactory.NewGame(9100);
        var gossip = new GossipEmitted(new EventId(1), "A dagger sold for a fortune.")
        {
            Id = new EventId(2),
            Day = 3,
        };
        return baseState with { EventLog = ImmutableList.Create<GameEvent>(gossip) };
    }

    // ── IN THE COMMON ROOM / OUT AT THE MINE (this unit) ──────────────────────────────────────

    [TestCase]
    public void FreshCampaign_ListsTheStartingSix_InTheCommonRoom_BareHanded()
    {
        // A fresh campaign (GameComposition.NewCampaign, what MountMainUi() with no override
        // builds) seeds the six starting heroes with GearSet.Empty (HeroRoster) and nobody has
        // departed yet — the day-1 case the original bug report actually hit.
        var ui = MountMainUi();
        try
        {
            var tavernText = RenderedText(ui.Tavern);
            AssertThat(tavernText).Contains("IN THE COMMON ROOM");
            AssertThat(tavernText).Contains("Torvald"); // HeroRoster's fixed starting cast
            AssertThat(tavernText).Contains("bare-handed"); // GearSet.Empty — no forge output yet
            AssertThat(tavernText).NotContains("OUT AT THE MINE"); // nobody's away — section omitted
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void HeroAwayInFlight_ListedOutAtTheMine_ExcludedFromTheCommonRoom()
    {
        var ui = MountMainUi(new SimAdapter(AwayHeroWorld()));
        try
        {
            var tavernText = RenderedText(ui.Tavern);
            AssertThat(tavernText).Contains("OUT AT THE MINE");
            AssertThat(tavernText).Contains("Wanderer");
            AssertThat(tavernText).Contains("floor 3"); // InFlightExpedition.TargetFloor, committed at muster

            // The away hero gets no patron card at all — the common-room roster reads live off
            // InFlight party membership, not just Hero.Alive.
            AssertThat(ui.Tavern.FindChild("Patron_1", recursive: true, owned: false)).IsNull();
            Find<PanelContainer>(ui.Tavern, "Patron_2"); // throws (fails the test) if missing
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void GossipCitingAHero_RendersAsTheirCommonRoomTopicLine()
    {
        // The Topic priority's second rung: a gossip line resolved back to the FloorRecordSet
        // it grew from (HeroGossipTopics) — proves the Source-EventId walk, not just that SOME
        // text renders.
        var ui = MountMainUi(new SimAdapter(GossipAboutHeroWorld()));
        try
        {
            var tavernText = RenderedText(ui.Tavern);
            AssertThat(tavernText).Contains("still talking about it");
            AssertThat(tavernText).Contains("Renny's gone deeper than ever before — floor 4!");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void WornWeapon_RendersInCarryingRow_AndHistoryButtonOpensItsProvenance()
    {
        var ui = MountMainUi(new SimAdapter(WornGearWorld()));
        try
        {
            var tavernText = RenderedText(ui.Tavern);
            AssertThat(tavernText).Contains("Bar Brawl Special");
            AssertThat(tavernText).Contains("[Fine]");

            // The Tavern's own gear-row History button — a second, independent wiring of the
            // same ProvenanceCard popup HeroesPanel already opens (ProvenanceCardTests covers
            // that one); this proves THIS panel's click reaches THIS panel's own popup instance.
            PressEnabled(ui.Tavern, $"TavernHistory_1_{ItemSlot.Weapon}");

            var card = Find<ProvenanceCard>(ui.Tavern, "ProvenanceCard");
            AssertThat(card.Visible).IsTrue();
            AssertThat(card.ShownItemId).IsEqual(WornWeaponId);
        }
        finally
        {
            Unmount(ui);
        }
    }

    private static GameState AwayHeroWorld()
    {
        var away = new Hero(
            new HeroId(1), "Wanderer", ClassRegistry.VanguardId, Level: 2, MaxHp: 30, Gold: 10,
            GearSet.Empty, ImmutableList<ItemMemory>.Empty, Alive: true, DeepestFloorReached: 1, DiedOnDay: null);
        var patron = new Hero(
            new HeroId(2), "Homebody", ClassRegistry.StrikerId, Level: 1, MaxHp: 25, Gold: 20,
            GearSet.Empty, ImmutableList<ItemMemory>.Empty, Alive: true, DeepestFloorReached: 0, DiedOnDay: null);

        var inFlight = new InFlightExpedition(
            Party: ImmutableList.Create(away.Id), TargetFloor: 3, CheckpointFloor: 1, VenueId: "mine",
            Hp: ImmutableSortedDictionary<int, int>.Empty,
            Packs: ImmutableSortedDictionary<int, ImmutableList<ItemId>>.Empty,
            Gold: ImmutableSortedDictionary<int, int>.Empty, Dead: ImmutableSortedSet<int>.Empty,
            Floors: ImmutableList<FloorOutcome>.Empty, Loot: ImmutableList<OreLoot>.Empty, DeepestFloorCleared: 0);

        var baseState = GameFactory.NewGame(6601);
        return baseState with
        {
            Heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(away.Id.Value, away).Add(patron.Id.Value, patron),
            InFlight = ImmutableList.Create(inFlight),
        };
    }

    private static GameState GossipAboutHeroWorld()
    {
        var hero = new Hero(
            new HeroId(1), "Renny", ClassRegistry.MysticId, Level: 3, MaxHp: 22, Gold: 15,
            GearSet.Empty, ImmutableList<ItemMemory>.Empty, Alive: true, DeepestFloorReached: 4, DiedOnDay: null);

        var record = new FloorRecordSet(hero.Id, Floor: 4) { Id = new EventId(1), Day = 5 };
        var gossip = new GossipEmitted(new EventId(1), "Renny's gone deeper than ever before — floor 4!")
        {
            Id = new EventId(2),
            Day = 5,
        };

        var baseState = GameFactory.NewGame(6602);
        return baseState with
        {
            Heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(hero.Id.Value, hero),
            EventLog = ImmutableList.Create<GameEvent>(record, gossip),
        };
    }

    private static readonly ItemId WornWeaponId = new(801);

    private static Item WornWeapon() => new(
        WornWeaponId, "no-such-recipe", "Bar Brawl Special", ItemSlot.Weapon, QualityGrade.Fine,
        new ItemStats(9, 0, 3), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);

    private static GameState WornGearWorld()
    {
        var hero = new Hero(
            new HeroId(1), "Geared", ClassRegistry.VanguardId, Level: 2, MaxHp: 30, Gold: 10,
            new GearSet(WornWeaponId, null, null), ImmutableList<ItemMemory>.Empty,
            Alive: true, DeepestFloorReached: 0, DiedOnDay: null);

        var baseState = GameFactory.NewGame(6603);
        return baseState with
        {
            Heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(hero.Id.Value, hero),
            Items = ImmutableSortedDictionary<int, Item>.Empty.Add(WornWeaponId.Value, WornWeapon()),
        };
    }
}
#endif
