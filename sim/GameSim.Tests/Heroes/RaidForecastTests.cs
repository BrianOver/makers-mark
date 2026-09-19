using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Heroes;
using GameSim.Kernel; // GameFactory

namespace GameSim.Tests.Heroes;

/// <summary>
/// Game-Feel Plan G4: <see cref="RaidForecast"/> is the pre-sleep triage board. These pin that it
/// (a) never disagrees with <see cref="MusterPlan.Compute"/>'s party/floor projection — the same
/// prediction the Expedition tick makes real — and (b) correctly enriches it with per-floor threats
/// and empty-gear-slot gaps. Pure, deterministic, RNG-free.
/// </summary>
public class RaidForecastTests
{
    [Fact]
    public void ForTomorrow_RosterAndFloor_MatchMusterPlan()
    {
        var state = HeroRoster.InstallStartingRoster(GameFactory.NewGame(seed: 4242));

        var plans = MusterPlan.Compute(state.Heroes, state.Bounties, state.Items, state.Day);
        var forecast = RaidForecast.ForTomorrow(state);

        Assert.Equal(plans.Count, forecast.Count);
        for (var i = 0; i < plans.Count; i++)
        {
            Assert.Equal(plans[i].TargetFloor, forecast[i].TargetFloor);
            Assert.Equal(plans[i].VenueId, forecast[i].VenueId);
            var expectedNames = plans[i].Roster.Select(id => state.Heroes[id.Value].Name).ToList();
            Assert.Equal(expectedNames, forecast[i].HeroNames);
        }
    }

    [Fact]
    public void Threats_CoverFloorsOneThroughTarget_InOrder()
    {
        var state = HeroRoster.InstallStartingRoster(GameFactory.NewGame(seed: 7));

        var forecast = RaidForecast.ForTomorrow(state);
        Assert.NotEmpty(forecast);

        foreach (var party in forecast)
        {
            Assert.Equal(party.TargetFloor, party.Threats.Count);
            for (var i = 0; i < party.Threats.Count; i++)
            {
                Assert.Equal(i + 1, party.Threats[i].Floor); // floors 1..TargetFloor, in order
                Assert.False(string.IsNullOrWhiteSpace(party.Threats[i].MonsterKind));
            }
        }
    }

    [Fact]
    public void GearGaps_NameOnlyHeroesWithEmptySlots_AndListEachMissingSlot()
    {
        var state = HeroRoster.InstallStartingRoster(GameFactory.NewGame(seed: 3));

        // Pin two heroes explicitly (don't assume the starting roster's default gear): one fully
        // equipped, one bare — so the assertions hold regardless of starting kit.
        var heroes = state.Heroes.Values.ToList();
        var full = heroes[0] with { Gear = new GearSet(new ItemId(1), new ItemId(2), new ItemId(3)) };
        var bare = heroes[1] with { Gear = GearSet.Empty };
        state = state with { Heroes = state.Heroes.SetItem(full.Id.Value, full).SetItem(bare.Id.Value, bare) };

        var allGaps = RaidForecast.ForTomorrow(state).SelectMany(p => p.GearGaps).ToList();

        Assert.DoesNotContain(allGaps, g => g.StartsWith($"{full.Name}:")); // fully geared => no gap line
        Assert.Contains($"{bare.Name}: no weapon, no shield, no armor", allGaps);
    }

    /// <summary>
    /// P2-SCREEN-36: a hero marching with an empty slot the smith already PROMISED to fill is a
    /// different, worse fact than an ordinary gap, and before this the muster board and the
    /// commission board never joined — the only match for "commission" in either render file was an
    /// unrelated doc comment. These cases pin the join in both directions, because the expensive
    /// failure is the false positive: a line claiming the player owes something they do not.
    /// </summary>
    [Fact]
    public void GapCommissions_NameOnlyACommissionMatchingThisHeroAndThisEmptySlot()
    {
        var state = HeroRoster.InstallStartingRoster(GameFactory.NewGame(seed: 7));
        var heroes = state.Heroes.Values.ToList();

        // One hero bare in every tracked slot, one fully geared — pinned explicitly rather than
        // assumed off the starting roster, same discipline as the gap test above.
        var bare = heroes[0] with { Gear = GearSet.Empty };
        var geared = heroes[1] with { Gear = new GearSet(new ItemId(1), new ItemId(2), new ItemId(3)) };
        state = state with
        {
            Heroes = state.Heroes.SetItem(bare.Id.Value, bare).SetItem(geared.Id.Value, geared),
            Commissions =
            [
                // Matches: this hero, a slot they are actually missing.
                new Commission(bare.Id, ItemSlot.Shield, QualityGrade.Common, DeadlineDay: 9, PremiumGold: 25),
                // Same hero, but a slot the geared hero holds — must not appear against anyone.
                new Commission(geared.Id, ItemSlot.Weapon, QualityGrade.Common, DeadlineDay: 4, PremiumGold: 10),
            ],
        };

        var owed = RaidForecast.ForTomorrow(state).SelectMany(p => p.GapCommissions).ToList();

        var mine = Assert.Single(owed);
        Assert.Equal(bare.Name, mine.HeroName);
        Assert.Equal(ItemSlot.Shield, mine.Slot);
        // The day comes off the commission the sim recorded, never recomputed from today + a window.
        Assert.Equal(9, mine.DeadlineDay);
    }

    /// <summary>A commission for a slot this hero already fills is not an unmet promise, and a
    /// commission belonging to a different hero is not this hero's. Both would be false positives,
    /// which is the failure mode worth paying for a test.</summary>
    [Fact]
    public void GapCommissions_IgnoreAFilledSlot_AndAnotherHerosCommission()
    {
        var state = HeroRoster.InstallStartingRoster(GameFactory.NewGame(seed: 12));
        var heroes = state.Heroes.Values.ToList();

        // Missing the shield only; weapon and armor are held.
        var partial = heroes[0] with { Gear = new GearSet(new ItemId(1), null, new ItemId(3)) };
        var other = heroes[1] with { Gear = GearSet.Empty };
        state = state with
        {
            Heroes = state.Heroes.SetItem(partial.Id.Value, partial).SetItem(other.Id.Value, other),
            Commissions =
            [
                // A slot this hero already fills.
                new Commission(partial.Id, ItemSlot.Weapon, QualityGrade.Common, DeadlineDay: 6, PremiumGold: 15),
            ],
        };

        var owed = RaidForecast.ForTomorrow(state).SelectMany(p => p.GapCommissions).ToList();

        Assert.DoesNotContain(owed, o => o.Slot == ItemSlot.Weapon);
        // 'other' is bare in every slot but has no commission of their own, so nothing is owed them.
        Assert.DoesNotContain(owed, o => o.HeroName == other.Name);
    }

    /// <summary>Fulfilled and expired commissions are REMOVED from <c>GameState.Commissions</c> by
    /// <c>CommissionSystem</c> rather than flagged, so an empty list is the whole of "nothing is
    /// owed" — and the forecast must render nothing rather than a stale promise.</summary>
    [Fact]
    public void GapCommissions_AreEmpty_WhenNoCommissionIsLive()
    {
        var state = HeroRoster.InstallStartingRoster(GameFactory.NewGame(seed: 21));
        var heroes = state.Heroes.Values.ToList();
        var bare = heroes[0] with { Gear = GearSet.Empty };
        state = state with
        {
            Heroes = state.Heroes.SetItem(bare.Id.Value, bare),
            Commissions = [],
        };

        Assert.Empty(RaidForecast.ForTomorrow(state).SelectMany(p => p.GapCommissions));
    }

    [Fact]
    public void ZeroHeroes_ProducesEmptyForecast()
    {
        var state = GameFactory.NewGame(seed: 11); // no roster installed
        Assert.Empty(RaidForecast.ForTomorrow(state));
    }

    /// <summary>
    /// P2-SCREEN-18 (decision 3, "fill the empty slot, or upgrade the full one"): before this unit
    /// <see cref="ForecastParty.GearGaps"/> was the only visible arm — a party in three Common
    /// copper daggers read identically to a party in full Masterwork. Iterates every one of the 8
    /// possible (weapon, shield, armor) fill patterns a hero's kit can take, one hero at a time, so
    /// the property holds for the whole family rather than one hand-picked shape. For every shape,
    /// <see cref="ForecastParty.WornGear"/> and <see cref="ForecastParty.GearGaps"/> must PARTITION
    /// the same three tracked slots: a filled slot appears in the former exactly once and never in
    /// the latter; an empty one appears in the latter's line and never in the former. The gap half
    /// of this property is a NEGATIVE CONTROL — it already passed before this unit and must keep
    /// passing unchanged now that WornGear rides alongside it.
    /// </summary>
    [Fact]
    public void WornGear_AndGearGaps_Partition_TheThreeTrackedSlots_AcrossEveryFillPattern()
    {
        var shapes = new List<(bool Weapon, bool Shield, bool Armor)>();
        for (var w = 0; w < 2; w++)
        {
            for (var s = 0; s < 2; s++)
            {
                for (var a = 0; a < 2; a++)
                {
                    shapes.Add((w == 1, s == 1, a == 1));
                }
            }
        }

        var nextItemId = 1;
        foreach (var shape in shapes)
        {
            var state = HeroRoster.InstallStartingRoster(GameFactory.NewGame(seed: 6000 + (ulong)nextItemId));
            var hero = state.Heroes.Values.First();

            ItemId? Fill(bool present, ItemSlot slot)
            {
                if (!present)
                {
                    return null;
                }

                var id = new ItemId(nextItemId++);
                var item = new Item(
                    id, "recipe", $"Test {slot}", slot, QualityGrade.Common,
                    new ItemStats(1, 1, 1), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);
                state = state with { Items = state.Items.Add(id.Value, item) };
                return id;
            }

            var weapon = Fill(shape.Weapon, ItemSlot.Weapon);
            var shield = Fill(shape.Shield, ItemSlot.Shield);
            var armor = Fill(shape.Armor, ItemSlot.Armor);
            var geared = hero with { Gear = new GearSet(weapon, shield, armor) };
            state = state with { Heroes = state.Heroes.SetItem(geared.Id.Value, geared) };

            var forecast = RaidForecast.ForTomorrow(state);
            var worn = forecast.SelectMany(p => p.WornGear).Where(w => w.HeroName == geared.Name).ToList();
            var gaps = forecast.SelectMany(p => p.GearGaps).Where(g => g.StartsWith($"{geared.Name}:")).ToList();

            Assert.Equal(shape.Weapon, worn.Any(w => w.Slot == ItemSlot.Weapon));
            Assert.Equal(shape.Shield, worn.Any(w => w.Slot == ItemSlot.Shield));
            Assert.Equal(shape.Armor, worn.Any(w => w.Slot == ItemSlot.Armor));

            var missing = new[] { shape.Weapon, shape.Shield, shape.Armor }.Count(filled => !filled);
            if (missing == 0)
            {
                Assert.Empty(gaps); // negative control: an all-filled hero regresses to no gap line
            }
            else
            {
                var gapLine = Assert.Single(gaps);
                if (!shape.Weapon)
                {
                    Assert.Contains("no weapon", gapLine);
                }

                if (!shape.Shield)
                {
                    Assert.Contains("no shield", gapLine);
                }

                if (!shape.Armor)
                {
                    Assert.Contains("no armor", gapLine);
                }
            }
        }
    }

    /// <summary>
    /// Link 1 ("you make a thing, and it is provably yours"): a piece stamped with the player's own
    /// <see cref="MakersMark"/> must read as theirs; a piece with none (rival-vendor stock, R5) must
    /// not. Iterates all three tracked slots — the property must hold no matter which slot the
    /// marked/unmarked piece happens to occupy, not only a hand-picked one.
    /// </summary>
    [Fact]
    public void WornGear_MarksThePlayersOwnCraft_AsTheirs_AndRivalStock_AsNot()
    {
        var trackedSlots = new[] { ItemSlot.Weapon, ItemSlot.Shield, ItemSlot.Armor };
        var nextItemId = 1;

        foreach (var markedSlot in trackedSlots)
        {
            var state = HeroRoster.InstallStartingRoster(GameFactory.NewGame(seed: 7000 + (ulong)nextItemId));
            var hero = state.Heroes.Values.First();

            var slotIds = new Dictionary<ItemSlot, ItemId>();
            foreach (var slot in trackedSlots)
            {
                var id = new ItemId(nextItemId++);
                var mark = slot == markedSlot ? new MakersMark("You", 2) : null;
                var item = new Item(
                    id, "recipe", $"Test {slot}", slot, QualityGrade.Common,
                    new ItemStats(1, 1, 1), mark, ImmutableList<ItemHistoryEntry>.Empty);
                state = state with { Items = state.Items.Add(id.Value, item) };
                slotIds[slot] = id;
            }

            var geared = hero with { Gear = new GearSet(slotIds[ItemSlot.Weapon], slotIds[ItemSlot.Shield], slotIds[ItemSlot.Armor]) };
            state = state with { Heroes = state.Heroes.SetItem(geared.Id.Value, geared) };

            var worn = RaidForecast.ForTomorrow(state).SelectMany(p => p.WornGear)
                .Where(w => w.HeroName == geared.Name).ToDictionary(w => w.Slot);

            foreach (var slot in trackedSlots)
            {
                var entry = worn[slot];
                if (slot == markedSlot)
                {
                    Assert.True(entry.PlayerCrafted, $"{slot} carries the player's own MakersMark and must read PlayerCrafted.");
                    Assert.Equal(2, entry.CraftedOnDay);
                }
                else
                {
                    Assert.False(entry.PlayerCrafted, $"{slot} carries no MakersMark (rival stock) and must not read PlayerCrafted.");
                    Assert.Null(entry.CraftedOnDay);
                }
            }
        }
    }

    /// <summary>
    /// The law this unit is closest to breaking ("the forecast does not tell you who will
    /// survive"): pins <see cref="WornSlot"/>'s field set at the TYPE level so a future edit cannot
    /// silently smuggle in a combat stat, a survival estimate, or a power score — it would show up
    /// here as an unexpected property name, not as a passing test that quietly stopped meaning
    /// anything.
    /// </summary>
    [Fact]
    public void WornSlot_CarriesOnlyTheDeclaredFactFields_NeverAStatOrEstimate()
    {
        var names = typeof(WornSlot).GetProperties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
        var expected = new[] { "CraftedOnDay", "HeroName", "ItemName", "PlayerCrafted", "Quality", "Slot" }
            .OrderBy(n => n, StringComparer.Ordinal).ToList();

        Assert.Equal(expected, names);
    }

    /// <summary>
    /// P2-LONG-28 ("the muster names the record the party is pressing past", link 3 — "the hero
    /// carries it into the dark on their own judgment"): <see cref="ForecastParty.BestRecordedFloor"/>
    /// and <see cref="ForecastParty.RecordHolderName"/> exist so the Godot muster board can name
    /// WHOSE record a target floor presses past instead of showing a bare, arbitrary-looking number.
    /// Phrased against the property, never one hand-picked shape (this repo has paid for that four
    /// times) — iterates several <see cref="Hero.DeepestFloorReached"/> distributions across the
    /// fixed starting six, including a full tie at the party's own maximum, and checks every FORMED
    /// party (not just the first) against a fact re-derived independently from <c>state.Heroes</c>
    /// rather than the production code under test.
    /// </summary>
    [Fact]
    public void BestRecordedFloor_AndRecordHolderName_MatchThePartysOwnHeroes_AcrossVariedDepthShapes()
    {
        // Values stay small (max 3) so no shape can bump a target floor past ANY live venue's
        // FloorCount and trip the clamp — that clamp is a real branch, but it belongs to a
        // different unit, not this one.
        var shapes = new List<Dictionary<int, int>>
        {
            new(), // every starting hero still at 0 — the "never delved" edge DepthCopy names "not yet"
            new() { [1] = 0, [2] = 1, [3] = 2, [4] = 3, [5] = 1, [6] = 2 }, // varied, no two parties alike
            new() { [1] = 3, [2] = 2, [3] = 1, [4] = 0, [5] = 3, [6] = 1 }, // the mirror shape
            new() { [1] = 2, [2] = 2, [3] = 2, [4] = 2, [5] = 2, [6] = 2 }, // every hero tied at the max
            new() { [4] = 3 }, // one spike, everyone else stays at 0
        };

        for (var i = 0; i < shapes.Count; i++)
        {
            var state = HeroRoster.InstallStartingRoster(GameFactory.NewGame(seed: 5000 + (ulong)i));
            foreach (var (heroId, depth) in shapes[i])
            {
                var hero = state.Heroes[heroId] with { DeepestFloorReached = depth };
                state = state with { Heroes = state.Heroes.SetItem(heroId, hero) };
            }

            var forecast = RaidForecast.ForTomorrow(state);
            Assert.NotEmpty(forecast); // setup check: the fixed starting six always musters someone

            foreach (var party in forecast)
            {
                var members = party.HeroNames.Select(name => state.Heroes.Values.First(h => h.Name == name)).ToList();
                var expectedBest = members.Max(h => h.DeepestFloorReached);
                var expectedHolder = members.First(h => h.DeepestFloorReached == expectedBest).Name;

                Assert.Equal(expectedBest, party.BestRecordedFloor);
                Assert.Equal(expectedHolder, party.RecordHolderName);

                // No bounty in any of these fixtures, so the default rule alone decides the target —
                // the identical arithmetic ExpeditionSystem.TargetFloorFor uses.
                Assert.Equal(expectedBest + 1, party.TargetFloor);
            }
        }
    }

    /// <summary>
    /// P2-LONG-28's OTHER branch: a bounty can send a party back to a floor at or below its own
    /// best recorded depth ("known ground"), never past it — <see
    /// cref="GameSim.Bounties.BountyRules.Judge"/> only ever accepts a bounty at or below
    /// <c>hero.DeepestFloorReached + 1</c>, so <see cref="ForecastParty.TargetFloor"/> can never
    /// exceed <see cref="ForecastParty.BestRecordedFloor"/> by more than the default rule's own one
    /// floor. <see cref="ForecastParty.BestRecordedFloor"/> itself must stay the party's actual
    /// record regardless — the override changes the TARGET, never the record it is measured
    /// against.
    /// </summary>
    [Fact]
    public void BestRecordedFloor_StaysThePartysActualRecord_WhenABountyOverridesTheTargetToKnownGround()
    {
        var state = HeroRoster.InstallStartingRoster(GameFactory.NewGame(seed: 6001));
        var torvald = state.Heroes[1] with { DeepestFloorReached = 3 };
        state = state with
        {
            Heroes = state.Heroes.SetItem(1, torvald),
            Bounties = ImmutableList.Create(new Bounty(
                new BountyId(1), TargetFloor: 2, RewardGold: 500, PostedOnDay: 1, AcceptedBy: new HeroId(1), Paid: false)),
        };

        var party = RaidForecast.ForTomorrow(state).Single(p => p.HeroNames.Contains("Torvald"));

        Assert.Equal(3, party.BestRecordedFloor); // the record itself is untouched by the override
        Assert.Equal("Torvald", party.RecordHolderName);
        Assert.Equal(2, party.TargetFloor); // the bounty's own floor, not best + 1
        Assert.True(party.TargetFloor <= party.BestRecordedFloor); // the "known ground" branch's own gate
    }

    [Fact]
    public void ForTomorrow_IsDeterministic()
    {
        var state = HeroRoster.InstallStartingRoster(GameFactory.NewGame(seed: 99));

        // Give one hero real worn gear too — a build with an always-empty WornGear list would let
        // this test pass without ever proving THAT field renders deterministically.
        var hero = state.Heroes.Values.First();
        var item = new Item(
            new ItemId(1), "recipe", "Test Weapon", ItemSlot.Weapon, QualityGrade.Fine,
            new ItemStats(1, 1, 1), new MakersMark("You", 3), ImmutableList<ItemHistoryEntry>.Empty);
        state = state with { Items = state.Items.Add(1, item) };
        var geared = hero with { Gear = new GearSet(new ItemId(1), null, null) };
        state = state with { Heroes = state.Heroes.SetItem(geared.Id.Value, geared) };

        // ForecastParty is a record but its members are ImmutableLists (reference equality), so
        // compare a stable flattened render rather than the objects themselves.
        static string Render(System.Collections.Immutable.ImmutableList<ForecastParty> f) =>
            string.Join("|", f.Select(p =>
                $"{string.Join(",", p.HeroNames)};{p.TargetFloor};{p.VenueId};" +
                $"{string.Join(",", p.Threats.Select(t => $"{t.Floor}:{t.MonsterKind}"))};" +
                $"{string.Join(",", p.GearGaps)};" +
                $"{string.Join(",", p.WornGear.Select(w => $"{w.HeroName}:{w.Slot}:{w.ItemName}:{w.Quality}:{w.PlayerCrafted}:{w.CraftedOnDay}"))}"));

        Assert.Equal(Render(RaidForecast.ForTomorrow(state)), Render(RaidForecast.ForTomorrow(state)));
    }
}
