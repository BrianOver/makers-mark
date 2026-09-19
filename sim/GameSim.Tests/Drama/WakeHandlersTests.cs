using System.Collections.Immutable;
using GameSim.Advisor;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Kernel;

namespace GameSim.Tests.Drama;

using static DramaFixtures;

/// <summary>
/// P2-PEOPLE-05 (wake contracts): the two wake verbs on a memorial — set your own craft as the grave-marker,
/// choose the logged event the fallen is remembered by — and the predicate that keeps the remembrance
/// honest (it must truly name the hero). The fallen's page and the death-night staging are P2-PEOPLE-06.
/// </summary>
public class WakeHandlersTests
{
    private static readonly GameKernel Kernel = new(
        ImmutableList<IPhaseSystem>.Empty,
        ImmutableList.Create<IActionHandler>(new FarewellHandlers()));

    private static readonly HeroId Torvald = new(1);
    private static readonly HeroId Kess = new(2);

    private static GameState World()
    {
        var marker = PlayerItem(10, "Iron Cairn", ItemSlot.Trinket, attack: 0, defense: 0);
        var rival = RivalItem(11, "Traveler's Sword", ItemSlot.Weapon, attack: 5, defense: 0);
        var worn = PlayerItem(12, "Oathkeeper Aegis", ItemSlot.Shield, attack: 0, defense: 7);
        var state = NewWorld() with
        {
            Phase = DayPhase.Evening,
            Drama = DramaState.Empty with
            {
                Memorials = ImmutableList.Create(new Memorial(Torvald, "Torvald", 3, "a rusty sword")),
            },
            Items = ImmutableSortedDictionary<int, Item>.Empty
                .Add(marker.Id.Value, marker).Add(rival.Id.Value, rival).Add(worn.Id.Value, worn),
            EventLog = ImmutableList.Create<GameEvent>(
                new HeroDied(Torvald, 3, "slain by a Tunnel Spider", GearSet.Empty) { Id = new EventId(1), Day = 3 },
                new AttributionBeatEvent(BeatType.LethalSave, new ItemId(12), Kess, 2, "the Aegis held", Decisive: true) { Id = new EventId(2), Day = 2 },
                new GossipEmitted(new EventId(1), "the tavern talks") { Id = new EventId(3), Day = 3 }),
        };
        var kess = state.Heroes[Kess.Value] with { Gear = GearSet.Empty.WithSlot(ItemSlot.Shield, worn.Id) };
        return state with { Heroes = state.Heroes.SetItem(Kess.Value, kess) };
    }

    private static TickResult Tick(GameState state, PlayerAction action) =>
        Kernel.Tick(state, ImmutableList.Create(action));

    [Fact]
    public void PlaceMarker_SetsMarkerItem_EmitsGraveMarkerPlaced_WithHeroName()
    {
        var result = Tick(World(), new PlaceGraveMarkerAction(Torvald, new ItemId(10)));

        Assert.Empty(result.Rejected);
        Assert.Equal(new ItemId(10), Assert.Single(result.NewState.Drama.Memorials).MarkerItem);
        var placed = Assert.Single(result.Events.OfType<GraveMarkerPlaced>());
        Assert.Equal(("Torvald", new ItemId(10)), (placed.HeroName, placed.Item));
        Assert.True(result.NewState.Items.ContainsKey(10)); // the item's history is permanent (R5)
    }

    [Fact]
    public void PlaceMarker_Rejects_NoMemorial_RivalWork_WornPiece_SecondMarker_AndAnotherGravesMarker()
    {
        var state = World();
        Assert.NotEmpty(Tick(state, new PlaceGraveMarkerAction(Kess, new ItemId(10))).Rejected); // Kess is alive: no memorial
        Assert.NotEmpty(Tick(state, new PlaceGraveMarkerAction(Torvald, new ItemId(11))).Rejected); // not your mark
        Assert.NotEmpty(Tick(state, new PlaceGraveMarkerAction(Torvald, new ItemId(12))).Rejected); // on Kess's back
        Assert.NotEmpty(Tick(state, new PlaceGraveMarkerAction(Torvald, new ItemId(99))).Rejected); // no such item

        var placed = Tick(state, new PlaceGraveMarkerAction(Torvald, new ItemId(10))).NewState with { Phase = DayPhase.Evening };
        Assert.NotEmpty(Tick(placed, new PlaceGraveMarkerAction(Torvald, new ItemId(10))).Rejected); // one marker, ever

        var twoGraves = placed with
        {
            Drama = placed.Drama with { Memorials = placed.Drama.Memorials.Add(new Memorial(Kess, "Kess", 4, "a shield")) },
        };
        Assert.NotEmpty(Tick(twoGraves, new PlaceGraveMarkerAction(Kess, new ItemId(10))).Rejected); // already marks Torvald's
    }

    [Fact]
    public void PlaceMarker_RejectsAShelvedPiece_UntilItIsTakenDown()
    {
        var state = World();
        var shelved = state with { Player = state.Player with { Shelf = ImmutableList.Create(new ShelfEntry(new ItemId(10), 20)) } };
        Assert.NotEmpty(Tick(shelved, new PlaceGraveMarkerAction(Torvald, new ItemId(10))).Rejected);
        Assert.Empty(Tick(state, new PlaceGraveMarkerAction(Torvald, new ItemId(10))).Rejected);
    }

    [Fact]
    public void ChooseRemembrance_TakesOnlyAnEventThatNamesTheFallen()
    {
        var state = World();

        var chosen = Tick(state, new ChooseRemembranceAction(Torvald, new EventId(1)));
        Assert.Empty(chosen.Rejected);
        Assert.Equal(new EventId(1), Assert.Single(chosen.NewState.Drama.Memorials).Remembrance);
        var remembered = Assert.Single(chosen.Events.OfType<RemembranceChosen>());
        Assert.Equal(("Torvald", new EventId(1)), (remembered.HeroName, remembered.Source));

        Assert.NotEmpty(Tick(state, new ChooseRemembranceAction(Torvald, new EventId(2))).Rejected); // Kess's save, not Torvald's
        Assert.NotEmpty(Tick(state, new ChooseRemembranceAction(Torvald, new EventId(3))).Rejected); // tavern talk names nobody
        Assert.NotEmpty(Tick(state, new ChooseRemembranceAction(Torvald, new EventId(42))).Rejected); // not in the record
        Assert.NotEmpty(Tick(chosen.NewState with { Phase = DayPhase.Evening }, new ChooseRemembranceAction(Torvald, new EventId(1))).Rejected); // one remembrance
    }

    [Fact]
    public void WakeVerbs_AreEveningOnly_LikeHonor()
    {
        var morning = World() with { Phase = DayPhase.Morning };
        Assert.False(ActionLegality.IsLegal(morning, new PlaceGraveMarkerAction(Torvald, new ItemId(10)), DayPhase.Morning));
        Assert.False(ActionLegality.IsLegal(morning, new ChooseRemembranceAction(Torvald, new EventId(1)), DayPhase.Morning));
        Assert.True(ActionLegality.IsLegal(World(), new PlaceGraveMarkerAction(Torvald, new ItemId(10)), DayPhase.Evening));
        Assert.True(ActionLegality.IsLegal(World(), new ChooseRemembranceAction(Torvald, new EventId(1)), DayPhase.Evening));
    }

    [Fact]
    public void Legality_MirrorsTheHandler_ForEveryWakeShape()
    {
        var state = World();
        var placed = Tick(state, new PlaceGraveMarkerAction(Torvald, new ItemId(10))).NewState with { Phase = DayPhase.Evening };
        var shelved = state with { Player = state.Player with { Shelf = ImmutableList.Create(new ShelfEntry(new ItemId(10), 20)) } };
        var remembered = Tick(state, new ChooseRemembranceAction(Torvald, new EventId(1))).NewState with { Phase = DayPhase.Evening };

        var cases = new (GameState State, PlayerAction Action)[]
        {
            (state, new PlaceGraveMarkerAction(Torvald, new ItemId(10))),
            (state, new PlaceGraveMarkerAction(Torvald, new ItemId(11))),
            (state, new PlaceGraveMarkerAction(Torvald, new ItemId(12))),
            (state, new PlaceGraveMarkerAction(Torvald, new ItemId(99))),
            (state, new PlaceGraveMarkerAction(Kess, new ItemId(10))),
            (placed, new PlaceGraveMarkerAction(Torvald, new ItemId(10))),
            (shelved, new PlaceGraveMarkerAction(Torvald, new ItemId(10))),
            (state, new ChooseRemembranceAction(Torvald, new EventId(1))),
            (state, new ChooseRemembranceAction(Torvald, new EventId(2))),
            (state, new ChooseRemembranceAction(Torvald, new EventId(3))),
            (state, new ChooseRemembranceAction(Torvald, new EventId(42))),
            (state, new ChooseRemembranceAction(Kess, new EventId(2))),
            (remembered, new ChooseRemembranceAction(Torvald, new EventId(1))),
        };

        foreach (var (s, action) in cases)
        {
            var legal = ActionLegality.IsLegal(s, action, DayPhase.Evening);
            var accepted = Tick(s, action).Rejected.Count == 0;
            Assert.True(legal == accepted, $"{action}: legal={legal} accepted={accepted}");
        }
    }

    [Fact]
    public void NamesHero_IsTheHerosOwnStory_NeverAnothersOrNobodys()
    {
        var state = World();
        var names = RemembranceQuery.Naming(state, Torvald).Select(e => e.Id.Value).ToList();
        Assert.Equal([1], names);
        Assert.Equal([2], RemembranceQuery.Naming(state, Kess).Select(e => e.Id.Value).ToList());
        Assert.False(RemembranceQuery.NamesHero(new GossipEmitted(new EventId(1), "the tavern talks"), Torvald));
        Assert.True(RemembranceQuery.NamesHero(new ItemSold(new ItemId(5), Torvald, 20, FromPlayerShop: true), Torvald));
        Assert.True(RemembranceQuery.NamesHero(new BountyPaid(new BountyId(1), Torvald, 40), Torvald));
        Assert.False(RemembranceQuery.NamesHero(new BountyPaid(new BountyId(1), Kess, 40), Torvald));
    }
}
