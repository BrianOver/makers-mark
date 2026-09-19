using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Heroes;
using GameSim.Kernel;

namespace GameSim.Tests.Heroes;

/// <summary>
/// P2-PEOPLE-27 ("one like the one that held"): a commission names the player-crafted piece a
/// PARTY-MATE's legend deed was earned by in the asked slot — the most recent such deed — and
/// nothing when no such proof exists. Kills never prove anything; the asker's own deeds do not
/// count; strangers' deeds do not count; rival iron does not count.
/// </summary>
public class CommissionProofTests
{
    private static readonly HeroId Torvald = new(1);
    private static readonly HeroId Kess = new(2);
    private static readonly HeroId Stranger = new(3);

    private static Item Mine(int id, ItemSlot slot, string name) => new(
        new ItemId(id), $"recipe-{id}", name, slot, QualityGrade.Fine,
        new ItemStats(5, 5, 3), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);

    private static Item Rival(int id, ItemSlot slot) => new(
        new ItemId(id), $"rival-{id}", "Traveler's Kit", slot, QualityGrade.Common,
        new ItemStats(3, 3, 3), Mark: null, ImmutableList<ItemHistoryEntry>.Empty);

    private static AttributionBeatEvent Deed(int id, BeatType kind, ItemId item, HeroId hero, bool decisive = true) =>
        new(kind, item, hero, 2, $"beat {id}", Decisive: decisive) { Id = new EventId(id), Day = 2 };

    private static GameState World(params GameEvent[] log)
    {
        var shield = Mine(10, ItemSlot.Shield, "Oathkeeper Aegis");
        var sword = Mine(11, ItemSlot.Weapon, "Emberbite");
        var rivalShield = Rival(12, ItemSlot.Shield);
        return GameFactory.NewGame(seed: 7) with
        {
            Items = ImmutableSortedDictionary<int, Item>.Empty
                .Add(10, shield).Add(11, sword).Add(12, rivalShield),
            EventLog = ImmutableList.CreateRange<GameEvent>([
                new PartyDeparted(ImmutableList.Create(Torvald, Kess), 3) { Id = new EventId(1), Day = 1 },
                .. log,
            ]),
        };
    }

    [Fact]
    public void APartyMatesLethalSave_InTheAskedSlot_IsTheProof()
    {
        var state = World(Deed(2, BeatType.LethalSave, new ItemId(10), Kess));

        var proof = CommissionProof.For(state, Torvald, ItemSlot.Shield);

        Assert.NotNull(proof);
        Assert.Equal((new ItemId(10), Kess), (proof!.Item, proof.ProvedFor));
    }

    [Fact]
    public void TheMostRecentProvingDeed_Wins()
    {
        var older = Mine(13, ItemSlot.Shield, "Old Buckler");
        var state = World(
            Deed(2, BeatType.LethalSave, new ItemId(13), Kess),
            Deed(3, BeatType.BreakpointClear, new ItemId(10), Kess));
        state = state with { Items = state.Items.Add(13, older) };

        Assert.Equal(new ItemId(10), CommissionProof.For(state, Torvald, ItemSlot.Shield)!.Item);
    }

    [Fact]
    public void NoProof_ForAKill_AStranger_TheAskerThemself_RivalIron_OrTheWrongSlot()
    {
        Assert.Null(CommissionProof.For(World(Deed(2, BeatType.KillingBlow, new ItemId(10), Kess)), Torvald, ItemSlot.Shield)); // 22 a night is the job
        Assert.Null(CommissionProof.For(World(Deed(2, BeatType.LethalSave, new ItemId(10), Stranger)), Torvald, ItemSlot.Shield)); // never raided together
        Assert.Null(CommissionProof.For(World(Deed(2, BeatType.LethalSave, new ItemId(10), Torvald)), Torvald, ItemSlot.Shield)); // their own save is not what they watched
        Assert.Null(CommissionProof.For(World(Deed(2, BeatType.LethalSave, new ItemId(12), Kess)), Torvald, ItemSlot.Shield)); // rival iron carries no mark
        Assert.Null(CommissionProof.For(World(Deed(2, BeatType.LethalSave, new ItemId(10), Kess)), Torvald, ItemSlot.Weapon)); // a shield proves nothing about swords
        Assert.Null(CommissionProof.For(World(Deed(2, BeatType.LethalSave, new ItemId(10), Kess, decisive: false)), Torvald, ItemSlot.Shield)); // unproven
        Assert.Null(CommissionProof.For(World(), Torvald, ItemSlot.Shield));
    }

    [Fact]
    public void PartyMates_AreEveryoneWhoEverDepartedBesideYou()
    {
        var state = World(new PartyDeparted(ImmutableList.Create(Kess, Stranger), 2) { Id = new EventId(2), Day = 2 });

        Assert.Equal([Kess], CommissionProof.PartyMates(state, Torvald).OrderBy(h => h.Value).ToList());
        Assert.Equal([Torvald, Stranger], CommissionProof.PartyMates(state, Kess).OrderBy(h => h.Value).ToList());
        Assert.Empty(CommissionProof.PartyMates(state, new HeroId(9)));
    }
}
