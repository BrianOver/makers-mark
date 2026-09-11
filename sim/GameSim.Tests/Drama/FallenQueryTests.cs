using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Heroes;

namespace GameSim.Tests.Drama;

using static DramaFixtures;

/// <summary>
/// P2-MEMORY-02: the death card's two pure reads. <see cref="FallenQuery"/> derives both lines from
/// facts the sim already recorded and nothing read — the fallen's undepleted pack and
/// <see cref="CombatEvent.KillingItem"/> — so every case here is a state-in/string-out assertion
/// with no tick, no RNG draw and no clock.
///
/// <para>Half these cases assert <see cref="string.Empty"/>. That is the point rather than padding:
/// the whole unit's discipline is that a line the record cannot prove is not softened into a vaguer
/// line, it is not printed at all (the <see cref="ProvenanceQuery.Clause"/> contract). The sharpest
/// of them is <see cref="PackLine_RecklessButDrankOnTheNight_RendersNothing"/> — an empty pack alone
/// reads identically whether the hero carried nothing or drank everything, and only the recorded
/// <see cref="CombatEvent.Uses"/> separate the two.</para>
/// </summary>
public class FallenQueryTests
{
    private const int Fallen = 1;

    /// <summary>A world where hero <see cref="Fallen"/> died last night, with the given recorded
    /// floors retained (P2-PROOF-01's <see cref="GameState.LastNightExpeditions"/>).</summary>
    private static GameState AfterDeath(params FloorOutcome[] floors)
    {
        var state = NewWorld();
        var hero = state.Heroes[Fallen];
        return state with
        {
            Heroes = state.Heroes.SetItem(Fallen, hero with { Alive = false, DiedOnDay = 3 }),
            LastNightExpeditions = ImmutableList.Create(
                Result(party: [Fallen], survivors: [], deaths: [Fallen], floors: floors)),
        };
    }

    private static GameState WithPack(GameState state, params int[] itemIds) => state with
    {
        Heroes = state.Heroes.SetItem(
            Fallen,
            state.Heroes[Fallen] with { Pack = itemIds.Select(id => new ItemId(id)).ToImmutableList() }),
    };

    /// <summary>Rename the fallen hero until the derived trait ladder gives (or withholds)
    /// <see cref="TraitId.Reckless"/>. Traits are a pure function of (id, name) with no stored
    /// field, so a name IS the fixture — no seed hunting, no reflection.</summary>
    private static GameState WithRecklessness(GameState state, bool reckless)
    {
        foreach (var candidate in new[] { "Ansa", "Bekk", "Cyra", "Dorn", "Esk", "Fain", "Gild", "Hest" })
        {
            if (TraitRegistry.Has(new HeroId(Fallen), candidate, TraitId.Reckless) == reckless)
            {
                return state with
                {
                    Heroes = state.Heroes.SetItem(Fallen, state.Heroes[Fallen] with { Name = candidate }),
                };
            }
        }

        // Fixture guard, never a skip: eight names that cannot produce both sides of a two-in-ten
        // trait draw would mean the ladder stopped deriving, and this suite must go red for it.
        throw new InvalidOperationException($"No candidate name derives Reckless={reckless} for hero {Fallen}.");
    }

    private static string NameOf(GameState state) => state.Heroes[Fallen].Name;

    [Fact]
    public void PackLine_LivingHero_RendersNothing()
    {
        var state = WithItem(NewWorld(), PlayerItem(10, "Field Salve", ItemSlot.Consumable, 0, 0));
        state = state with
        {
            Heroes = state.Heroes.SetItem(
                Fallen, state.Heroes[Fallen] with { Pack = ImmutableList.Create(new ItemId(10)) }),
        };

        Assert.Equal(string.Empty, FallenQuery.PackLine(state, new HeroId(Fallen)));
    }

    [Fact]
    public void PackLine_PlayerWorkLeftUnopened_NamesIt()
    {
        var state = WithItem(AfterDeath(), PlayerItem(10, "Field Salve", ItemSlot.Consumable, 0, 0));
        state = WithPack(state, 10);

        Assert.Equal(
            $"The Field Salve you sent was still in {NameOf(state)}'s pack, unopened.",
            FallenQuery.PackLine(state, new HeroId(Fallen)));
    }

    [Fact]
    public void PackLine_NamesTheItemTheResolverWouldHaveReachedForNext()
    {
        // Pack order IS quaff order (Hero.Pack's determinism contract). Naming the second item
        // would name something the fight never got to; naming the first names the drink that was
        // one round away.
        var state = AfterDeath();
        state = WithItem(state, PlayerItem(10, "Field Salve", ItemSlot.Consumable, 0, 0));
        state = WithItem(state, PlayerItem(11, "Deep Draught", ItemSlot.Consumable, 0, 0));
        state = WithPack(state, 10, 11);

        Assert.Contains("Field Salve", FallenQuery.PackLine(state, new HeroId(Fallen)));
        Assert.DoesNotContain("Deep Draught", FallenQuery.PackLine(state, new HeroId(Fallen)));
    }

    [Fact]
    public void PackLine_SkipsRivalGoodsToFindThePlayersOwn()
    {
        var state = AfterDeath();
        state = WithItem(state, RivalItem(20, "Cheap Tonic", ItemSlot.Consumable, 0, 0));
        state = WithItem(state, PlayerItem(10, "Field Salve", ItemSlot.Consumable, 0, 0));
        state = WithPack(state, 20, 10);

        Assert.Contains("Field Salve", FallenQuery.PackLine(state, new HeroId(Fallen)));
    }

    [Fact]
    public void PackLine_RivalGoodsOnly_RendersNothing()
    {
        // The town's memory is about the player's hand. A vendor tonic in a dead hero's pack is
        // not the player's grief, and inventing a line for it would be participation credit.
        var state = WithItem(AfterDeath(), RivalItem(20, "Cheap Tonic", ItemSlot.Consumable, 0, 0));
        state = WithPack(state, 20);

        Assert.Equal(string.Empty, FallenQuery.PackLine(state, new HeroId(Fallen)));
    }

    [Fact]
    public void PackLine_RecklessAndCarriedNothing_SpeaksTheTrait()
    {
        var state = WithRecklessness(AfterDeath(Floor(Combat(1, Fallen, "Rat"))), reckless: true);

        Assert.Equal(
            $"{NameOf(state)} carried no salve — and never did.",
            FallenQuery.PackLine(state, new HeroId(Fallen)));
    }

    [Fact]
    public void PackLine_RecklessButDrankOnTheNight_RendersNothing()
    {
        // The honesty guard this unit exists for. An empty pack reads identically whether the hero
        // carried nothing or drank everything they had; only the recorded Uses separate them, and
        // "never did" over a hero who emptied a pack of salves would be a lie living in the ledger.
        var drank = Combat(1, Fallen, "Rat") with
        {
            Uses = ImmutableList.Create(new ConsumableUse(new ItemId(10), Round: 1, HpBefore: 4, HpAfter: 12)),
        };
        var state = WithRecklessness(AfterDeath(Floor(drank)), reckless: true);

        Assert.Equal(string.Empty, FallenQuery.PackLine(state, new HeroId(Fallen)));
    }

    [Fact]
    public void PackLine_EmptyPackButNotReckless_RendersNothing()
    {
        var state = WithRecklessness(AfterDeath(Floor(Combat(1, Fallen, "Rat"))), reckless: false);

        Assert.Equal(string.Empty, FallenQuery.PackLine(state, new HeroId(Fallen)));
    }

    [Fact]
    public void PackLine_EmptyPackWithNoRetainedNight_RendersNothing()
    {
        // Without the night, "drank nothing" is unprovable — so the trait line goes silent rather
        // than asserting something the record no longer holds.
        var state = WithRecklessness(AfterDeath(Floor(Combat(1, Fallen, "Rat"))), reckless: true);
        state = state with { LastNightExpeditions = ImmutableList<ExpeditionResult>.Empty };

        Assert.Equal(string.Empty, FallenQuery.PackLine(state, new HeroId(Fallen)));
    }

    [Fact]
    public void LastBlowLine_BladeWasNotYours_NamesTheKill()
    {
        var state = WithItem(
            AfterDeath(Floor(Combat(2, Fallen, "Cave Lurker", monsterKilled: true, killingItem: 20))),
            RivalItem(20, "Notched Axe", ItemSlot.Weapon, 3, 0));

        Assert.Equal(
            $"{NameOf(state)}'s last blow felled the Cave Lurker. The blade was not yours. "
                + $"The arm was {NameOf(state)}'s.",
            FallenQuery.LastBlowLine(state, new HeroId(Fallen)));
    }

    [Fact]
    public void LastBlowLine_TakesTheLastKillNotTheFirst()
    {
        var state = WithItem(
            AfterDeath(
                Floor(Combat(1, Fallen, "Rat", monsterKilled: true, killingItem: 20)),
                Floor(Combat(2, Fallen, "Cave Lurker", monsterKilled: true, killingItem: 20))),
            RivalItem(20, "Notched Axe", ItemSlot.Weapon, 3, 0));

        Assert.Contains("Cave Lurker", FallenQuery.LastBlowLine(state, new HeroId(Fallen)));
    }

    [Fact]
    public void LastBlowLine_IgnoresOtherHerosKills()
    {
        var state = WithItem(
            AfterDeath(
                Floor(
                    Combat(1, Fallen, "Rat", monsterKilled: true, killingItem: 20),
                    Combat(1, heroId: 2, "Cave Lurker", monsterKilled: true, killingItem: 20))),
            RivalItem(20, "Notched Axe", ItemSlot.Weapon, 3, 0));

        Assert.Contains("Rat", FallenQuery.LastBlowLine(state, new HeroId(Fallen)));
        Assert.DoesNotContain("Cave Lurker", FallenQuery.LastBlowLine(state, new HeroId(Fallen)));
    }

    [Fact]
    public void LastBlowLine_PlayerMarkedBlade_RendersNothing()
    {
        // The beat rows above this line already own a player-marked killing blow — either as an
        // earned KillingBlow beat or as a replay that found it did not matter. A second sentence
        // here would be the participation credit this game refuses to give.
        var state = WithItem(
            AfterDeath(Floor(Combat(2, Fallen, "Cave Lurker", monsterKilled: true, killingItem: 10))),
            PlayerItem(10, "Emberbite", ItemSlot.Weapon, 5, 0));

        Assert.Equal(string.Empty, FallenQuery.LastBlowLine(state, new HeroId(Fallen)));
    }

    [Fact]
    public void LastBlowLine_NamedBossTakesNoArticle()
    {
        // Mirrors ExpeditionRevealSystem.DeathReport's own rule rather than re-deciding it.
        var state = WithItem(
            AfterDeath(Floor(Combat(5, Fallen, "The Warden", monsterKilled: true, killingItem: 20))),
            RivalItem(20, "Notched Axe", ItemSlot.Weapon, 3, 0));

        Assert.Contains("felled The Warden.", FallenQuery.LastBlowLine(state, new HeroId(Fallen)));
    }

    [Fact]
    public void LastBlowLine_FellNothing_RendersNothing()
    {
        var state = AfterDeath(Floor(Combat(1, Fallen, "Rat")));

        Assert.Equal(string.Empty, FallenQuery.LastBlowLine(state, new HeroId(Fallen)));
    }

    [Fact]
    public void LastBlowLine_KillWithNoRecordedBlade_RendersNothing()
    {
        var state = AfterDeath(Floor(Combat(1, Fallen, "Rat", monsterKilled: true)));

        Assert.Equal(string.Empty, FallenQuery.LastBlowLine(state, new HeroId(Fallen)));
    }

    [Fact]
    public void LastBlowLine_NightRolledOutOfRetention_RendersNothing()
    {
        var state = WithItem(
            AfterDeath(Floor(Combat(2, Fallen, "Cave Lurker", monsterKilled: true, killingItem: 20))),
            RivalItem(20, "Notched Axe", ItemSlot.Weapon, 3, 0));
        state = state with { LastNightExpeditions = ImmutableList<ExpeditionResult>.Empty };

        Assert.Equal(string.Empty, FallenQuery.LastBlowLine(state, new HeroId(Fallen)));
    }

    [Fact]
    public void LastBlowLine_LivingHero_RendersNothing()
    {
        var state = WithItem(
            AfterDeath(Floor(Combat(2, Fallen, "Cave Lurker", monsterKilled: true, killingItem: 20))),
            RivalItem(20, "Notched Axe", ItemSlot.Weapon, 3, 0));
        state = state with
        {
            Heroes = state.Heroes.SetItem(Fallen, state.Heroes[Fallen] with { Alive = true, DiedOnDay = null }),
        };

        Assert.Equal(string.Empty, FallenQuery.LastBlowLine(state, new HeroId(Fallen)));
    }

    private static FloorOutcome Floor(params CombatEvent[] combats) =>
        new(combats[0].Floor, Cleared: false, combats.ToImmutableList());
}
