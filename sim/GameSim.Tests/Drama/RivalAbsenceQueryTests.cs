using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Drama;

namespace GameSim.Tests.Drama;

using static DramaFixtures;

/// <summary>
/// P2-LONG-19: <see cref="RivalAbsenceQuery"/> derives "died carrying nothing the player made"
/// from a real <see cref="HeroDied"/> event (produced by <c>ExpeditionRevealSystem</c>, the same
/// production path <see cref="ExpeditionRevealSystemTests"/> exercises for <c>WornGear</c>) — every
/// case here is state-in/state-out, no tick beyond the one that manufactures the death.
/// </summary>
public class RivalAbsenceQueryTests
{
    private static (GameState State, HeroDied Death) DieWearing(GameState state, params Item[] worn)
    {
        foreach (var item in worn)
        {
            state = Equip(state, heroId: 1, item);
        }

        var result = Result(
            party: [1], survivors: [], deaths: [1],
            targetFloor: 1, deepestCleared: 0,
            floors: [new FloorOutcome(1, false, [Combat(1, 1, "Cave Rat", taken: 30)])]);

        var tick = TickEvening(AtEvening(state, result));
        return (tick.NewState, Assert.Single(tick.Events.OfType<HeroDied>()));
    }

    [Fact]
    public void DiedInUnmarkedGear_AllRivalStock_IsTrue()
    {
        var (state, death) = DieWearing(NewWorld(), RivalItem(20, "Rusty Sword", ItemSlot.Weapon, 3, 0));

        Assert.True(RivalAbsenceQuery.DiedInUnmarkedGear(state, death));
    }

    [Fact]
    public void DiedInUnmarkedGear_BareHanded_IsTrue()
    {
        // No Equip call at all: an empty GearSet carries nothing the player made by definition.
        var (state, death) = DieWearing(NewWorld());

        Assert.True(RivalAbsenceQuery.DiedInUnmarkedGear(state, death));
    }

    [Fact]
    public void DiedInUnmarkedGear_AnyPlayerMarkedSlot_IsFalse()
    {
        // Rival weapon AND a player-marked armor piece — one marked slot is enough to disqualify.
        var (state, death) = DieWearing(
            NewWorld(),
            RivalItem(20, "Rusty Sword", ItemSlot.Weapon, 3, 0),
            PlayerItem(21, "Oathkeeper Plate", ItemSlot.Armor, 0, 6));

        Assert.False(RivalAbsenceQuery.DiedInUnmarkedGear(state, death));
    }

    [Fact]
    public void PendingAbsenceLines_UnmarkedDeath_ProducesExactlyOneLine_ThenNoFurtherLineOnASecondRead()
    {
        var (state, death) = DieWearing(NewWorld(), RivalItem(20, "Rusty Sword", ItemSlot.Weapon, 3, 0));

        var first = RivalAbsenceQuery.PendingAbsenceLines(state, ImmutableHashSet<int>.Empty);
        Assert.Equal([death.Hero], first.Select(d => d.Hero));

        // The once-ness lives in the accumulated "already spoken" set the caller threads through —
        // never a counter the test sets — so a second read of the SAME state, now carrying the
        // hero id the first read returned, must come back empty.
        var spokenFor = first.Select(d => d.Hero.Value).ToImmutableHashSet();
        var second = RivalAbsenceQuery.PendingAbsenceLines(state, spokenFor);
        Assert.Empty(second);
    }

    [Fact]
    public void PendingAbsenceLines_MarkedDeath_ProducesNone()
    {
        // The death card owns this moment instead — protecting it is the whole point of the guard.
        var (state, _) = DieWearing(NewWorld(), PlayerItem(21, "Oathkeeper Plate", ItemSlot.Armor, 0, 6));

        Assert.Empty(RivalAbsenceQuery.PendingAbsenceLines(state, ImmutableHashSet<int>.Empty));
    }
}
