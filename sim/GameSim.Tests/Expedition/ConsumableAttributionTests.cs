using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Expedition;
using GameSim.Kernel;
using GameSim.Venues;

namespace GameSim.Tests.Expedition;

/// <summary>
/// P2 consumable beats (R11, KTD6): Provisioned and PotionLifesave are computed facts
/// over RECORDED <see cref="ConsumableUse"/> data — never a fresh RNG draw. One beat
/// per hero per expedition, for the first player-marked use; it upgrades to
/// PotionLifesave when replaying the same fight's subsequent recorded DamageTaken
/// from the use's HpBefore would have reached hp &lt;= 0 while the hero survived.
/// </summary>
public class ConsumableAttributionTests
{
    private static Item Salve(int id, bool marked = true) => new(
        new ItemId(id), "field-salve", "Field Salve", ItemSlot.Consumable, QualityGrade.Common,
        new ItemStats(0, 0, 0), marked ? new MakersMark("You", 1) : null,
        ImmutableList<ItemHistoryEntry>.Empty, new ConsumableEffect(ConsumableKind.Heal, 6));

    private static Hero Torvald(int maxHp = 30) => new(
        new HeroId(1), "Torvald", "vanguard", Level: 1, MaxHp: maxHp, Gold: 30,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty, Alive: true, DeepestFloorReached: 0, DiedOnDay: null);

    /// <summary>One recorded round; Uses ride on the round the quaff preceded.</summary>
    private static CombatEvent Round(int taken, bool killed = false, params ConsumableUse[] uses) => new(
        1, new HeroId(1), "Cave Rat", ImmutableList.Create(2, 4), DamageDealt: 5, taken, killed, KillingItem: null)
    {
        Uses = uses.ToImmutableList(),
    };

    private static ImmutableList<AttributionBeat> Beats(Item salve, params CombatEvent[] fight) =>
        AttributionEngine.ComputeBeats(
            ImmutableList.Create(new FloorOutcome(1, Cleared: false, fight.ToImmutableList())),
            ImmutableList.Create(Torvald()),
            ImmutableSortedDictionary<int, Item>.Empty.Add(salve.Id.Value, salve),
            VenueRegistry.Mine);

    [Fact]
    public void FirstUse_EmitsProvisioned_NamingHeroAndFloor()
    {
        // Round 2 quaff at 5 hp; only 3 more damage lands — no lethal replay, so the
        // salve "kept them fighting" and nothing more.
        var salve = Salve(10);
        var beats = Beats(
            salve,
            Round(taken: 8),
            Round(taken: 3, killed: false, new ConsumableUse(salve.Id, Round: 2, HpBefore: 5, HpAfter: 11)),
            Round(taken: 0, killed: true));

        var beat = Assert.Single(beats, b => b.Beat is BeatType.Provisioned or BeatType.PotionLifesave);
        Assert.Equal(BeatType.Provisioned, beat.Beat);
        Assert.Equal(salve.Id, beat.Item);
        Assert.Equal(new HeroId(1), beat.Hero);
        Assert.Equal(1, beat.Floor);
        Assert.Contains("kept Torvald fighting on floor 1", beat.Detail);
    }

    [Fact]
    public void LethalReplay_UpgradesToPotionLifesave()
    {
        // Round 2 quaff at 5 hp; 8 damage follows. Without the heal: 5 - 8 <= 0 — dead.
        // With it: 11 - 8 = 3, and the hero finishes the fight alive. Lifesave, proven.
        var salve = Salve(10);
        var beats = Beats(
            salve,
            Round(taken: 8),
            Round(taken: 8, killed: false, new ConsumableUse(salve.Id, Round: 2, HpBefore: 5, HpAfter: 11)),
            Round(taken: 0, killed: true));

        var beat = Assert.Single(beats, b => b.Beat is BeatType.Provisioned or BeatType.PotionLifesave);
        Assert.Equal(BeatType.PotionLifesave, beat.Beat);
        Assert.Contains("saved Torvald's life", beat.Detail);
    }

    [Fact]
    public void HeroDiedAnyway_NoLifesaveUpgrade()
    {
        // The quaff bought a round but the hero still died in the fight: the drink is
        // Provisioned (it kept them fighting), never a lifesave.
        var salve = Salve(10);
        var beats = Beats(
            salve,
            Round(taken: 8),
            Round(taken: 12, killed: false, new ConsumableUse(salve.Id, Round: 2, HpBefore: 5, HpAfter: 11)));

        var beat = Assert.Single(beats, b => b.Beat is BeatType.Provisioned or BeatType.PotionLifesave);
        Assert.Equal(BeatType.Provisioned, beat.Beat);
    }

    [Fact]
    public void PostFloorUse_IsNeverALifesave()
    {
        // A post-floor quaff (Round past the fight's rounds) has no subsequent damage
        // to replay — it can only be Provisioned.
        var salve = Salve(10);
        var beats = Beats(
            salve,
            Round(taken: 8),
            Round(taken: 0, killed: true, new ConsumableUse(salve.Id, Round: 3, HpBefore: 4, HpAfter: 10)));

        var beat = Assert.Single(beats, b => b.Beat is BeatType.Provisioned or BeatType.PotionLifesave);
        Assert.Equal(BeatType.Provisioned, beat.Beat);
    }

    [Fact]
    public void UnmarkedConsumable_EarnsNoBeat_EvenWhenDecisive()
    {
        // The maker's-mark gate (existing rule): rival salves stay untold.
        var rivalSalve = Salve(10, marked: false);
        var beats = Beats(
            rivalSalve,
            Round(taken: 8),
            Round(taken: 8, killed: false, new ConsumableUse(rivalSalve.Id, Round: 2, HpBefore: 5, HpAfter: 11)),
            Round(taken: 0, killed: true));

        Assert.DoesNotContain(beats, b => b.Beat is BeatType.Provisioned or BeatType.PotionLifesave);
    }

    [Fact]
    public void OnlyFirstUse_OneBeatPerHeroPerExpedition()
    {
        // Two quaffs in one expedition: exactly one beat, for the first use.
        var salve = Salve(10);
        var secondSalve = Salve(11);
        var items = ImmutableSortedDictionary<int, Item>.Empty
            .Add(salve.Id.Value, salve)
            .Add(secondSalve.Id.Value, secondSalve);

        var beats = AttributionEngine.ComputeBeats(
            ImmutableList.Create(
                new FloorOutcome(1, Cleared: true, ImmutableList.Create(
                    Round(taken: 8),
                    Round(taken: 3, killed: false, new ConsumableUse(salve.Id, Round: 2, HpBefore: 5, HpAfter: 11)),
                    Round(taken: 0, killed: true))),
                new FloorOutcome(2, Cleared: false, ImmutableList.Create(
                    Round(taken: 3, killed: false, new ConsumableUse(secondSalve.Id, Round: 1, HpBefore: 6, HpAfter: 12)) with { Floor = 2 }))),
            ImmutableList.Create(Torvald()),
            items,
            VenueRegistry.Mine);

        var beat = Assert.Single(beats, b => b.Beat is BeatType.Provisioned or BeatType.PotionLifesave);
        Assert.Equal(salve.Id, beat.Item);
        Assert.Equal(1, beat.Floor);
    }

    [Fact]
    public void ResolverIntegration_QuaffProducesBeat_EndToEnd()
    {
        // Through the real resolver: a marked salve that fires must surface as exactly
        // one Provisioned-or-lifesave beat for the hero.
        var salve = Salve(10);
        var items = ImmutableSortedDictionary<int, Item>.Empty.Add(salve.Id.Value, salve);

        for (ulong seed = 0; seed < 200; seed++)
        {
            var hero = Torvald() with { Pack = ImmutableList.Create(salve.Id) };
            var result = ExpeditionResolver.Resolve(
                ImmutableList.Create(hero), items, VenueRegistry.Mine, targetFloor: 2, new Pcg32(RngState.FromSeed(seed)));

            if (!result.Floors.SelectMany(f => f.Combats).SelectMany(c => c.Uses).Any())
            {
                continue;
            }

            var beat = Assert.Single(result.Beats, b => b.Beat is BeatType.Provisioned or BeatType.PotionLifesave);
            Assert.Equal(salve.Id, beat.Item);
            Assert.Equal(hero.Id, beat.Hero);
            return; // proven
        }

        Assert.Fail("No quaff across 200 seeds — end-to-end beat unproven.");
    }

    [Fact]
    public void ConsumableAttribution_ConsumesZeroRngState()
    {
        // Recomputing beats over a result with recorded uses touches no RNG and
        // reproduces identical beats — recorded data only (KTD6).
        var salve = Salve(10);
        var items = ImmutableSortedDictionary<int, Item>.Empty.Add(salve.Id.Value, salve);
        var hero = Torvald() with { Pack = ImmutableList.Create(salve.Id) };

        var rng = new Pcg32(RngState.FromSeed(11));
        var result = ExpeditionResolver.Resolve(ImmutableList.Create(hero), items, VenueRegistry.Mine, 3, rng);
        var streamAfterResolve = rng.Snapshot();

        var beatsAgain = AttributionEngine.ComputeBeats(result.Floors, ImmutableList.Create(hero), items, VenueRegistry.Mine);
        Assert.Equal(streamAfterResolve, rng.Snapshot());
        Assert.Equal(result.Beats, beatsAgain);
    }
}
