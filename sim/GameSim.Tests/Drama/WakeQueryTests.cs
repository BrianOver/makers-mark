using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Drama;

namespace GameSim.Tests.Drama;

using static DramaFixtures;

/// <summary>
/// P2-PEOPLE-06: <see cref="WakeQuery"/> is the fallen's page's ONLY source of "what is still
/// choosable" — every case here is phrased against the property (a memorial's marker/remembrance/
/// heirloom fact stays open exactly until something sets it, or until nothing legal remains), not
/// against one named hero or item, so the page keeps working as the roster and item catalog grow.
/// </summary>
public class WakeQueryTests
{
    private static readonly HeroId Fallen = new(1);

    private static GameState WithMemorial(GameState state, Memorial memorial) => state with
    {
        Drama = state.Drama with { Memorials = ImmutableList.Create(memorial) },
    };

    // ---- MarkerCandidates / MarkerOpen -------------------------------------------------

    [Fact]
    public void MarkerCandidates_PlayerCraftedUnwornUnshelvedItem_IsACandidate()
    {
        var state = WithMemorial(NewWorld(), new Memorial(Fallen, "Torvald", 3, "a rusty sword"));
        state = WithItem(state, PlayerItem(50, "Emberbite", ItemSlot.Weapon, 5, 0));

        Assert.Contains(new ItemId(50), WakeQuery.MarkerCandidates(state, Fallen));
        Assert.True(WakeQuery.MarkerOpen(state, Fallen));
    }

    [Fact]
    public void MarkerCandidates_ExcludesNotPlayerCrafted()
    {
        var state = WithMemorial(NewWorld(), new Memorial(Fallen, "Torvald", 3, "a rusty sword"));
        state = WithItem(state, RivalItem(50, "Bought Blade", ItemSlot.Weapon, 5, 0));

        Assert.Empty(WakeQuery.MarkerCandidates(state, Fallen));
        Assert.False(WakeQuery.MarkerOpen(state, Fallen));
    }

    [Fact]
    public void MarkerCandidates_ExcludesWornGear()
    {
        var state = WithMemorial(NewWorld(), new Memorial(Fallen, "Torvald", 3, "a rusty sword"));
        var item = PlayerItem(50, "Emberbite", ItemSlot.Weapon, 5, 0);
        state = Equip(state, heroId: 2, item); // worn by a DIFFERENT (living) hero

        Assert.Empty(WakeQuery.MarkerCandidates(state, Fallen));
    }

    [Fact]
    public void MarkerCandidates_ExcludesShelvedItem()
    {
        var state = WithMemorial(NewWorld(), new Memorial(Fallen, "Torvald", 3, "a rusty sword"));
        var item = PlayerItem(50, "Emberbite", ItemSlot.Weapon, 5, 0);
        state = WithItem(state, item) with
        {
            Player = NewWorld().Player with
            {
                Shelf = ImmutableList.Create(new ShelfEntry(item.Id, 100)),
            },
        };

        Assert.Empty(WakeQuery.MarkerCandidates(state, Fallen));
    }

    [Fact]
    public void MarkerCandidates_ExcludesItemAlreadyMarkingAnotherGrave()
    {
        var other = new HeroId(2);
        var item = PlayerItem(50, "Emberbite", ItemSlot.Weapon, 5, 0);
        var state = NewWorld() with
        {
            Drama = DramaState.Empty with
            {
                Memorials = ImmutableList.Create(
                    new Memorial(Fallen, "Torvald", 3, "a rusty sword"),
                    new Memorial(other, "Brynn", 2, "a leather cap", MarkerItem: item.Id)),
            },
        };
        state = WithItem(state, item);

        Assert.Empty(WakeQuery.MarkerCandidates(state, Fallen));
    }

    [Fact]
    public void MarkerCandidates_EmptyOnceMarkerAlreadySet()
    {
        var item = PlayerItem(50, "Emberbite", ItemSlot.Weapon, 5, 0);
        var other = PlayerItem(51, "Guardwall", ItemSlot.Armor, 0, 5);
        var state = WithMemorial(NewWorld(), new Memorial(Fallen, "Torvald", 3, "a rusty sword", MarkerItem: item.Id));
        state = WithItem(WithItem(state, item), other);

        // even though `other` would otherwise be a legal candidate, the grave already has ITS marker
        Assert.Empty(WakeQuery.MarkerCandidates(state, Fallen));
        Assert.False(WakeQuery.MarkerOpen(state, Fallen));
    }

    [Fact]
    public void MarkerCandidates_NoMemorial_Empty()
    {
        var state = NewWorld();
        Assert.Empty(WakeQuery.MarkerCandidates(state, Fallen));
    }

    // ---- RemembranceChoices / DefaultRemembrance ---------------------------------------

    [Fact]
    public void RemembranceChoices_ReturnsOnlyEventsNamingTheHero_InLogOrder()
    {
        var died = new HeroDied(Fallen, 3, "goblin", new GearSet(null, null, null)) { Id = new EventId(1) };
        var rivalsSale = new ItemSold(new ItemId(9), new HeroId(2), 10, true) { Id = new EventId(2) };
        var rankUp = new HeroRankUp(Fallen, "Veteran") { Id = new EventId(3) };
        var state = WithMemorial(NewWorld(), new Memorial(Fallen, "Torvald", 3, "a rusty sword")) with
        {
            EventLog = ImmutableList.Create<GameEvent>(died, rivalsSale, rankUp),
        };

        var choices = WakeQuery.RemembranceChoices(state, Fallen);

        Assert.Equal(new GameEvent[] { died, rankUp }, choices);
    }

    [Fact]
    public void RemembranceChoices_EmptyOnceRemembranceAlreadySet()
    {
        var died = new HeroDied(Fallen, 3, "goblin", new GearSet(null, null, null)) { Id = new EventId(1) };
        var state = WithMemorial(NewWorld(), new Memorial(Fallen, "Torvald", 3, "a rusty sword", Remembrance: died.Id)) with
        {
            EventLog = ImmutableList.Create<GameEvent>(died),
        };

        Assert.Empty(WakeQuery.RemembranceChoices(state, Fallen));
        Assert.Null(WakeQuery.DefaultRemembrance(state, Fallen));
    }

    [Fact]
    public void DefaultRemembrance_PicksHighestStakes_DeathOverAnOrdinaryFact()
    {
        var rankUp = new HeroRankUp(Fallen, "Veteran") { Id = new EventId(1) };
        var died = new HeroDied(Fallen, 3, "goblin", new GearSet(null, null, null)) { Id = new EventId(2) };
        var state = WithMemorial(NewWorld(), new Memorial(Fallen, "Torvald", 3, "a rusty sword")) with
        {
            // deliberately logged BEFORE the death, so a naive "first choice" default would be wrong
            EventLog = ImmutableList.Create<GameEvent>(rankUp, died),
        };

        Assert.Equal(died, WakeQuery.DefaultRemembrance(state, Fallen));
    }

    [Fact]
    public void DefaultRemembrance_PicksProvenSaveOverAnOrdinaryKillingBlow()
    {
        var kill = new AttributionBeatEvent(BeatType.KillingBlow, new ItemId(5), Fallen, 2, "goblin")
            { Id = new EventId(1) };
        var save = new AttributionBeatEvent(BeatType.LethalSave, new ItemId(6), Fallen, 2, "would have died")
            { Id = new EventId(2) };
        var state = WithMemorial(NewWorld(), new Memorial(Fallen, "Torvald", 3, "a rusty sword")) with
        {
            EventLog = ImmutableList.Create<GameEvent>(kill, save),
        };

        Assert.Equal(save, WakeQuery.DefaultRemembrance(state, Fallen));
    }

    [Fact]
    public void DefaultRemembrance_TiedStakes_BreaksToEarlierLogOrder()
    {
        var rankUp = new HeroRankUp(Fallen, "Veteran") { Id = new EventId(1) };
        var recruited = new RecruitArrived(Fallen) { Id = new EventId(2) };
        var state = WithMemorial(NewWorld(), new Memorial(Fallen, "Torvald", 3, "a rusty sword")) with
        {
            // neither is in the raid stakes grammar (both score 0) — the earlier logged one wins
            EventLog = ImmutableList.Create<GameEvent>(rankUp, recruited),
        };

        Assert.Equal(rankUp, WakeQuery.DefaultRemembrance(state, Fallen));
    }

    [Fact]
    public void DefaultRemembrance_NoNamingEvents_Null()
    {
        var state = WithMemorial(NewWorld(), new Memorial(Fallen, "Torvald", 3, "a rusty sword"));
        Assert.Null(WakeQuery.DefaultRemembrance(state, Fallen));
    }

    // ---- HeirloomOpen -------------------------------------------------------------------

    [Fact]
    public void HeirloomOpen_WornPieceNotYetReforged_True()
    {
        var weapon = new ItemId(50);
        var died = new HeroDied(Fallen, 3, "goblin", new GearSet(weapon, null, null)) { Id = new EventId(1) };
        var state = NewWorld() with { EventLog = ImmutableList.Create<GameEvent>(died) };

        Assert.True(WakeQuery.HeirloomOpen(state, Fallen));
    }

    [Fact]
    public void HeirloomOpen_AlreadyReforged_False()
    {
        var weapon = new ItemId(50);
        var died = new HeroDied(Fallen, 3, "goblin", new GearSet(weapon, null, null)) { Id = new EventId(1) };
        var reforged = new HeirloomReforged(new ItemId(51), weapon, "lineage") { Id = new EventId(2) };
        var state = NewWorld() with { EventLog = ImmutableList.Create<GameEvent>(died, reforged) };

        Assert.False(WakeQuery.HeirloomOpen(state, Fallen));
    }

    [Fact]
    public void HeirloomOpen_NoDeathRecorded_False()
    {
        var state = NewWorld();
        Assert.False(WakeQuery.HeirloomOpen(state, Fallen));
    }
}
