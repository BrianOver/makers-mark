using System.Collections.Immutable;
using GameSim.Classes;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Heroes;

namespace GameSim.Tests.Drama;

using static DramaFixtures;

/// <summary>
/// P2-MEMORY-02 / P2-PROOF-11: the death card's three pure reads. <see cref="FallenQuery"/> derives
/// every line from facts the sim already recorded and nothing read — the fallen's undepleted pack,
/// <see cref="CombatEvent.KillingItem"/>, and the fatal round's own roll/gear/hp — so every case
/// here is a state-in/string-out assertion with no tick, no RNG draw and no clock.
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

    /// <summary>Overlays the raid-time snapshot <see cref="MarginLine"/> reads
    /// (<see cref="ExpeditionResult.PartyAtDeparture"/>) onto an already-built <see cref="AfterDeath"/>
    /// world — a separate helper rather than a new <see cref="AfterDeath"/> parameter because
    /// <c>floors</c> is a <c>params</c> array and must stay the last parameter.</summary>
    private static GameState WithDeparture(GameState state, HeroAtDeparture departure)
    {
        var night = state.LastNightExpeditions[0] with
        {
            PartyAtDeparture = ImmutableList.Create(departure),
        };
        return state with { LastNightExpeditions = ImmutableList.Create(night) };
    }

    private static HeroAtDeparture Departure(int maxHp, ItemId? shield = null, ItemId? armor = null) =>
        new(new HeroId(Fallen), "Fallen", ClassRegistry.StrikerId, Level: 1, maxHp, Weapon: null, shield, armor);

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

    // ---- MarginLine (P2-PROOF-11) ------------------------------------------------------------

    /// <summary>Builds a world where <see cref="Fallen"/> died to a genuine fatal blow on
    /// <paramref name="floor"/>: the monster's own roll recorded (it survived the hero's swing),
    /// positive damage taken, and the raid-time gear/hp snapshot <see cref="FallenQuery.MarginLine"/>
    /// reads. Varying <paramref name="floor"/>/<paramref name="monsterRoll"/>/gear/<paramref
    /// name="maxHp"/> across callers is the property test: every case is a DIFFERENT shape, not one
    /// instance re-asserted.</summary>
    private static GameState FatalBlow(int floor, int monsterRoll, int shieldDefense, int armorDefense, int maxHp)
    {
        var state = AfterDeath(Floor(
            Combat(floor, Fallen, "Test Monster", monsterKilled: false, taken: 1) with
            {
                RecordedRolls = ImmutableList.Create(3, monsterRoll),
            }));

        ItemId? shield = null;
        ItemId? armor = null;
        if (shieldDefense > 0)
        {
            state = WithItem(state, PlayerItem(30, "Ward", ItemSlot.Shield, 0, shieldDefense));
            shield = new ItemId(30);
        }

        if (armorDefense > 0)
        {
            state = WithItem(state, PlayerItem(31, "Mail", ItemSlot.Armor, 0, armorDefense));
            armor = new ItemId(31);
        }

        return WithDeparture(state, Departure(maxHp, shield, armor));
    }

    [Theory]
    [InlineData(1, 4, 2, 0, 9)]   // the plan's own worked example: blow 15, gear drank 2, stood at 9
    [InlineData(2, 1, 0, 3, 20)]  // armor only, a different floor and roll
    [InlineData(3, 5, 0, 0, 12)]  // no gear at all -- the clause must simply omit, never read "0"
    public void MarginLine_EveryNumberEqualsTheRecordedFact(
        int floor, int monsterRoll, int shieldDefense, int armorDefense, int maxHp)
    {
        var state = FatalBlow(floor, monsterRoll, shieldDefense, armorDefense, maxHp);
        var name = NameOf(state);

        // Independently re-derived from the SAME recorded facts MarginLine reads (the venue's own
        // fixed attack curve, the item stats, and the departure snapshot) -- a test whose expected
        // values are computed a second, independent way is exactly what would catch the card
        // drifting from the replay.
        var expectedAttack = floor == 5 ? 26 : 5 + 6 * floor;
        var expectedBlow = expectedAttack + monsterRoll;
        var expectedGear = shieldDefense + armorDefense;
        var expectedGearClause = expectedGear > 0 ? $"{name}'s gear drank {expectedGear} of it. " : string.Empty;
        var expected = $"The blow read {expectedBlow}. {expectedGearClause}{name} stood at {maxHp}.";

        Assert.Equal(expected, FallenQuery.MarginLine(state, new HeroId(Fallen)));
    }

    [Theory]
    [InlineData(1, 4, 2, 0, 9)]
    [InlineData(2, 1, 0, 3, 20)]
    [InlineData(3, 5, 0, 0, 12)]
    public void MarginLine_NeverReadsAsAShareRatioOrPercentage(
        int floor, int monsterRoll, int shieldDefense, int armorDefense, int maxHp)
    {
        // Law 4, guarded as a PATTERN over the generated line rather than one literal: whatever the
        // numbers are, the line must never carry a "/" ratio shape, a "%" figure, or the vocabulary
        // of a share. A margin is a magnitude, never a slice of one.
        var state = FatalBlow(floor, monsterRoll, shieldDefense, armorDefense, maxHp);
        var line = FallenQuery.MarginLine(state, new HeroId(Fallen));

        Assert.NotEqual(string.Empty, line);
        Assert.DoesNotContain("%", line);
        Assert.DoesNotMatch(@"\d+\s*/\s*\d+", line);
        Assert.DoesNotContain("percent", line, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("share", line, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("contribution", line, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ratio", line, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MarginLine_ReplaysHpThroughAPriorRoundOnTheSameFloor_RatherThanAlwaysMaxHp()
    {
        // The important shape MarginLine_EveryNumberEqualsTheRecordedFact's single-round fixtures
        // cannot catch: a hero who took an EARLIER hit on the same floor and survived it, THEN took
        // the fatal one. "Stood at" must read the hp AFTER that first hit, never MaxHp verbatim --
        // proving the replay actually walks prior rounds instead of reporting the starting number.
        var earlierRound = Combat(1, Fallen, "Test Monster", monsterKilled: false, taken: 6)
            with { RecordedRolls = ImmutableList.Create(1, 2) };
        var fatalRound = Combat(1, Fallen, "Test Monster", monsterKilled: false, taken: 1)
            with { RecordedRolls = ImmutableList.Create(3, 4) };
        var state = AfterDeath(Floor(earlierRound, fatalRound));
        state = WithDeparture(state, Departure(maxHp: 20));

        // MaxHp 20, minus the earlier round's recorded 6 damage, leaves 14 entering the fatal round
        // -- not 20, which is what a bug that skipped the prior-round replay would report instead.
        Assert.Equal(
            $"The blow read 15. {NameOf(state)} stood at 14.",
            FallenQuery.MarginLine(state, new HeroId(Fallen)));
    }

    [Fact]
    public void MarginLine_NoRecordedCombatAtAll_RendersNothing()
    {
        // DeathReport's own "lost to the Mine" shape: a synthetic result with no combats behind the
        // death at all. There is no round to replay, so there is no margin -- silence, not a guess.
        var state = AfterDeath();
        state = WithDeparture(state, Departure(maxHp: 10));

        Assert.Equal(string.Empty, FallenQuery.MarginLine(state, new HeroId(Fallen)));
    }

    [Fact]
    public void MarginLine_LastRecordedRoundWasAKillNotADeath_RendersNothing()
    {
        // The important test: a round where the monster died cannot be the round that killed the
        // hero. The guard checks MonsterKilled directly rather than trusting that "last recorded
        // round" always means "fatal round" -- never invent a margin from the wrong round.
        var state = AfterDeath(Floor(Combat(1, Fallen, "Rat", monsterKilled: true)));
        state = WithDeparture(state, Departure(maxHp: 10));

        Assert.Equal(string.Empty, FallenQuery.MarginLine(state, new HeroId(Fallen)));
    }

    [Fact]
    public void MarginLine_NoDamageRecordedOnTheLastRound_RendersNothing()
    {
        var state = AfterDeath(Floor(Combat(1, Fallen, "Rat", monsterKilled: false, taken: 0)));
        state = WithDeparture(state, Departure(maxHp: 10));

        Assert.Equal(string.Empty, FallenQuery.MarginLine(state, new HeroId(Fallen)));
    }

    [Fact]
    public void MarginLine_MonsterRollWasNeverRecorded_RendersNothing()
    {
        // The monster's own roll is only ever recorded when it survived the hero's swing (the same
        // contract TellingRound's own doc comment pins). A round missing it cannot be replayed into
        // a raw blow, so this must not invent one.
        var state = AfterDeath(Floor(
            Combat(1, Fallen, "Rat", monsterKilled: false, taken: 4) with
            {
                RecordedRolls = ImmutableList.Create(3),
            }));
        state = WithDeparture(state, Departure(maxHp: 10));

        Assert.Equal(string.Empty, FallenQuery.MarginLine(state, new HeroId(Fallen)));
    }

    [Fact]
    public void MarginLine_NoPartyAtDepartureSnapshot_RendersNothing()
    {
        // Pre-KTD5 save shape: the fight is real, but nothing recorded the raid-time gear/MaxHp
        // snapshot to read a gear or hp number from. Silence, not a guess.
        var state = AfterDeath(Floor(Combat(1, Fallen, "Rat", monsterKilled: false, taken: 5)));

        Assert.Equal(string.Empty, FallenQuery.MarginLine(state, new HeroId(Fallen)));
    }

    [Fact]
    public void MarginLine_LivingHero_RendersNothing()
    {
        var state = FatalBlow(floor: 1, monsterRoll: 4, shieldDefense: 2, armorDefense: 0, maxHp: 9);
        state = state with
        {
            Heroes = state.Heroes.SetItem(Fallen, state.Heroes[Fallen] with { Alive = true, DiedOnDay = null }),
        };

        Assert.Equal(string.Empty, FallenQuery.MarginLine(state, new HeroId(Fallen)));
    }

    [Fact]
    public void MarginLine_NightRolledOutOfRetention_RendersNothing()
    {
        var state = FatalBlow(floor: 1, monsterRoll: 4, shieldDefense: 2, armorDefense: 0, maxHp: 9);
        state = state with { LastNightExpeditions = ImmutableList<ExpeditionResult>.Empty };

        Assert.Equal(string.Empty, FallenQuery.MarginLine(state, new HeroId(Fallen)));
    }

    private static FloorOutcome Floor(params CombatEvent[] combats) =>
        new(combats[0].Floor, Cleared: false, combats.ToImmutableList());
}
