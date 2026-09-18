#if GDUNIT_TESTS
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using GameSim.Classes;
using GameSim.Contracts;
using GameSim.Heroes;
using GameSim.Kernel;
using GdUnit4;
using GodotClient.Ui;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// P2-MEMORY-20 ("the forecast gets a face"): pins <see cref="MusterVoice"/>'s pure functions in
/// isolation, no Godot mounting needed — the same "pure logic, gdUnit-decorated for suite
/// consistency" shape <see cref="PartyVoice"/>'s own <c>PartyVoiceTests</c> and
/// <see cref="CustomerVoice"/>'s own <c>CustomerVoiceTests</c> already use. Every property below is
/// checked against a TABLE of <see cref="ForecastParty"/> SHAPES (party size, target floor, which
/// heroes carry a gear gap and in which slots), not one hand-picked instance — the repo's own
/// "a hand-listed fixture stops covering its family" lesson, applied here the same way
/// <c>PartyVoiceTests</c> applies it to the camp's voice.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class MusterVoiceTests
{
    // ── A table of forecast-party SHAPES — every test below iterates the whole table ──────────────

    private sealed record Scenario(
        ImmutableList<string> HeroNames,
        int TargetFloor,
        ImmutableList<string> GearGaps);

    private static IEnumerable<Scenario> Scenarios()
    {
        // Solo party, no gaps at all — the "Just me" / full-kit branch.
        yield return new Scenario(Names("Torvald"), TargetFloor: 2, GearGaps: Gaps());
        // Two heroes, one gap of a single slot.
        yield return new Scenario(Names("Kael", "Sable"), TargetFloor: 3, GearGaps: Gaps("Kael: no shield"));
        // Three heroes, one hero missing TWO slots — the "and" join.
        yield return new Scenario(
            Names("Torvald", "Brunhilde", "Moss"), TargetFloor: 4, GearGaps: Gaps("Moss: no weapon, no armor"));
        // Four heroes, TWO different heroes each with a gap — every gap must get its own sentence.
        yield return new Scenario(
            Names("Torvald", "Kael", "Sable", "Elowen"), TargetFloor: 5,
            GearGaps: Gaps("Kael: no weapon", "Elowen: no armor"));
        // The whole living roster, one hero missing all three tracked slots — the Oxford-comma join.
        yield return new Scenario(
            Names("Torvald", "Brunhilde", "Kael", "Sable", "Elowen", "Moss"), TargetFloor: 6,
            GearGaps: Gaps("Elowen: no weapon, no shield, no armor"));
        // Five heroes, no gaps — the full-kit branch again, at a party size that is not the solo case.
        yield return new Scenario(
            Names("Torvald", "Brunhilde", "Kael", "Sable", "Elowen"), TargetFloor: 3, GearGaps: Gaps());
    }

    private static ImmutableList<string> Names(params string[] names) => names.ToImmutableList();
    private static ImmutableList<string> Gaps(params string[] gaps) => gaps.ToImmutableList();

    // ── 1. Every clause traces to the read-model, and the numbers/names match it ───────────────────

    [TestCase]
    public void AnchorLine_EveryScenario_EveryClauseTracesToTheReadModel()
    {
        foreach (var scenario in Scenarios())
        {
            var party = Build(scenario);
            var line = MusterVoice.AnchorLine(party);
            var context = $"size={scenario.HeroNames.Count} floor={scenario.TargetFloor}";

            // Party size + target floor — the SAME two facts the board's own header and Target
            // line already carry.
            AssertThat(line.Contains($"floor {scenario.TargetFloor}", StringComparison.Ordinal))
                .OverrideFailureMessage($"[{context}] target floor must appear verbatim. Line: \"{line}\"").IsTrue();

            if (scenario.HeroNames.Count == 1)
            {
                AssertThat(line.Contains("Just me", StringComparison.Ordinal))
                    .OverrideFailureMessage($"[{context}] a solo party must speak for itself. Line: \"{line}\"").IsTrue();
            }
            else
            {
                var expectedCount = CountWords[scenario.HeroNames.Count];
                AssertThat(line.Contains($"{expectedCount} of us", StringComparison.Ordinal))
                    .OverrideFailureMessage(
                        $"[{context}] party size ({scenario.HeroNames.Count}) must appear as \"{expectedCount} of us\". Line: \"{line}\"")
                    .IsTrue();
            }

            // Gear gaps — every hero named in the read-model's own GearGaps list must be named here
            // too, and the slot(s) they lack must appear, never a fabricated fuller kit.
            if (scenario.GearGaps.IsEmpty)
            {
                AssertThat(line.Contains("whole", StringComparison.Ordinal))
                    .OverrideFailureMessage($"[{context}] a party with no gear gaps must say so. Line: \"{line}\"").IsTrue();
            }
            else
            {
                foreach (var gap in scenario.GearGaps)
                {
                    var (heroName, slots) = ParseGap(gap);
                    AssertThat(line.Contains($"{heroName}'s going without", StringComparison.Ordinal))
                        .OverrideFailureMessage(
                            $"[{context}] {heroName}'s own gap must be spoken by name. Line: \"{line}\"").IsTrue();

                    foreach (var slot in slots)
                    {
                        var phrase = slot switch
                        {
                            "weapon" => "a weapon",
                            "shield" => "a shield",
                            "armor" => "some armor",
                            _ => slot,
                        };
                        AssertThat(line.Contains(phrase, StringComparison.Ordinal))
                            .OverrideFailureMessage(
                                $"[{context}] {heroName}'s missing {slot} must be named (\"{phrase}\"). Line: \"{line}\"")
                            .IsTrue();
                    }
                }

                AssertThat(line.Contains("whole, all round", StringComparison.Ordinal))
                    .OverrideFailureMessage(
                        $"[{context}] a party with a real gap must never also claim a full kit. Line: \"{line}\"")
                    .IsFalse();
            }
        }
    }

    // ── 2. Determinism: the same party renders the identical line twice ────────────────────────────

    [TestCase]
    public void AnchorLine_EveryScenario_SameStateRendersTheIdenticalLineTwice()
    {
        foreach (var scenario in Scenarios())
        {
            var party = Build(scenario);
            AssertThat(MusterVoice.AnchorLine(party))
                .OverrideFailureMessage($"Re-rendering the SAME party produced a different line for size={scenario.HeroNames.Count}.")
                .IsEqual(MusterVoice.AnchorLine(party));
        }
    }

    // ── 3. The forecast does not tell you who will survive (law 3) — no odds, risk or danger ──────

    /// <summary>Same deny-pattern <c>RaidForecastBoardTests</c> already checks the Target line
    /// against — one canonical regex, reused rather than reinvented for a third rendered surface.</summary>
    private static readonly Regex SurvivalOrRiskLanguage = new(
        @"\d+%|\bsurvive[sd]?\b|\bchance\b|\bpower\b|\bwill (win|lose|die)\b|\brisk(y)?\b|\bodds\b|\bdanger",
        RegexOptions.IgnoreCase);

    [TestCase]
    public void AnchorLine_EveryScenario_NeverNamesASurvivalOrOddsOrRiskEstimate()
    {
        foreach (var scenario in Scenarios())
        {
            var line = MusterVoice.AnchorLine(Build(scenario));
            AssertThat(SurvivalOrRiskLanguage.IsMatch(line))
                .OverrideFailureMessage($"the anchor must state facts only, never a survival/odds/risk estimate: \"{line}\"")
                .IsFalse();
        }
    }

    // ── 4. Influence never orders (law 1): no imperative anywhere, guarded as a PROPERTY ────────────

    /// <summary>Same deny-list shape <c>PartyVoiceTests</c> already applies to <see cref="PartyVoice"/>
    /// — a bare command verb opening a sentence would read as an order directed at the player.</summary>
    private static readonly string[] ImperativeSentenceStarts =
    {
        "send", "give", "bring", "recall", "go", "use", "buy", "sell", "forge", "craft",
        "pay", "wait", "hurry", "keep", "take", "leave", "hold", "ring", "answer", "spend", "trust",
    };

    private static readonly string[] DirectivePhrases =
    {
        "you should", "you must", "you need to", "you have to", "make sure to", "don't forget to",
    };

    [TestCase]
    public void AnchorLine_EveryScenario_NeverPhrasesAnOrder()
    {
        foreach (var scenario in Scenarios())
        {
            var line = MusterVoice.AnchorLine(Build(scenario));
            var lower = line.ToLowerInvariant();

            foreach (var phrase in DirectivePhrases)
            {
                AssertThat(lower.Contains(phrase, StringComparison.Ordinal))
                    .OverrideFailureMessage($"Line phrases a direct order (\"{phrase}\"): \"{line}\"")
                    .IsFalse();
            }

            var sentences = line.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var sentence in sentences)
            {
                var trimmed = sentence.TrimStart(' ', '—');
                var firstWord = (trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty)
                    .Trim(',').ToLowerInvariant();

                AssertThat(ImperativeSentenceStarts.Contains(firstWord))
                    .OverrideFailureMessage(
                        $"Sentence \"{trimmed}\" opens with a bare command verb (\"{firstWord}\") — that reads as an "
                        + $"order, and influence never orders (Law 1). Full line: \"{line}\"")
                    .IsFalse();
            }
        }
    }

    // ── fixture builder + a test-local parse of the SAME gap shape RaidForecast produces ───────────

    private static readonly ImmutableArray<string> CountWords =
        ["zero", "One", "Two", "Three", "Four", "Five", "Six"];

    private static ForecastParty Build(Scenario scenario) => new(
        HeroNames: scenario.HeroNames,
        TargetFloor: scenario.TargetFloor,
        VenueId: "mine",
        Threats: ImmutableList<ForecastThreat>.Empty,
        GearGaps: scenario.GearGaps,
        WornGear: ImmutableList<WornSlot>.Empty,
        BestRecordedFloor: 0,
        RecordHolderName: scenario.HeroNames[0],
        // P2-SCREEN-36: MusterVoice speaks the gap, never the commission owed against it -- that
        // line belongs to RaidForecastBoard. Empty here keeps these cases about what this class
        // actually says.
        GapCommissions: ImmutableList<GapCommission>.Empty);

    /// <summary>Independent test-side parse of a <c>RaidForecast</c>-shaped gap string
    /// ("Kael: no shield", "Moss: no weapon, no armor") into (hero name, bare slot words) — mirrors
    /// the shape <see cref="MusterVoice"/>'s own <c>GapSentence</c> parses, kept separate so this
    /// test cannot pass merely by agreeing with itself.</summary>
    private static (string HeroName, string[] Slots) ParseGap(string gap)
    {
        var parts = gap.Split(": ", 2);
        var slots = parts[1].Split(", ").Select(label => label.Replace("no ", string.Empty)).ToArray();
        return (parts[0], slots);
    }

    // ── P2-SCREEN-35 ("follow one piece"): FollowedSendOffLine / FollowedNightLine ──────────────────
    //
    // Pure functions over GameSim.Contracts state (no Adapter, no mounting) — same "isolated logic,
    // gdUnit-decorated for suite consistency" idiom as everything above. Every test resets
    // FollowedItem before AND after itself (TutorialFlow.DeleteForTests()'s own idiom, applied to
    // this class's own client-side static) so no test can leak its pick into a sibling.

    private static readonly HeroId FollowHeroId = new(41);
    private static readonly ItemId FollowedWeaponId = new(4100);
    private static readonly ItemId FollowedArmorId = new(4101);
    private static readonly ItemId UnfollowedId = new(4102);

    private static Item WeaponItem(ItemId id) => new(
        id, "dagger", "Emberbite", ItemSlot.Weapon, QualityGrade.Common,
        new ItemStats(8, 0, 2), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);

    private static Item ArmorItem(ItemId id) => new(
        id, "plate", "Steadfast Plate", ItemSlot.Armor, QualityGrade.Common,
        new ItemStats(0, 6, 8), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);

    private static GameState StateWithHolder(ItemId itemId, Item item, ItemSlot slot)
    {
        var gear = slot switch
        {
            ItemSlot.Weapon => GearSet.Empty with { Weapon = itemId },
            ItemSlot.Armor => GearSet.Empty with { Armor = itemId },
            _ => GearSet.Empty,
        };
        var hero = new Hero(
            FollowHeroId, "Torvald", ClassRegistry.VanguardId, Level: 2, MaxHp: 30, Gold: 0,
            Gear: gear, Memories: ImmutableList<ItemMemory>.Empty, Alive: true,
            DeepestFloorReached: 1, DiedOnDay: null);

        return GameFactory.NewGame(7001, ImmutableSortedDictionary<int, Hero>.Empty.Add(FollowHeroId.Value, hero))
            with { Items = ImmutableSortedDictionary<int, Item>.Empty.Add(itemId.Value, item) };
    }

    // ── FollowedSendOffLine: not-followed and stale-pick absence ────────────────────────────────────

    [TestCase]
    public void FollowedSendOffLine_NothingFollowed_RendersNothing()
    {
        FollowedItem.DeleteForTests();
        try
        {
            var state = GameFactory.NewGame(7002);
            AssertThat(MusterVoice.FollowedSendOffLine(state, ImmutableList<ForecastParty>.Empty)).IsNull();
        }
        finally
        {
            FollowedItem.DeleteForTests();
        }
    }

    [TestCase]
    public void FollowedSendOffLine_FollowedItemNoLongerInState_RendersNothing()
    {
        FollowedItem.DeleteForTests();
        try
        {
            var state = GameFactory.NewGame(7003); // Items empty — the followed id resolves to nothing
            FollowedItem.Set(UnfollowedId);
            AssertThat(MusterVoice.FollowedSendOffLine(state, ImmutableList<ForecastParty>.Empty)).IsNull();
        }
        finally
        {
            FollowedItem.DeleteForTests();
        }
    }

    // ── FollowedSendOffLine: the two honest outcomes, never a third ─────────────────────────────────

    [TestCase]
    public void FollowedSendOffLine_HolderMustersTomorrow_NamesTheHeroAndTheFloor()
    {
        FollowedItem.DeleteForTests();
        try
        {
            var item = WeaponItem(FollowedWeaponId);
            var state = StateWithHolder(FollowedWeaponId, item, ItemSlot.Weapon);
            FollowedItem.Set(FollowedWeaponId);

            var party = new ForecastParty(
                HeroNames: ImmutableList.Create("Torvald"), TargetFloor: 4, VenueId: "mine",
                Threats: ImmutableList<ForecastThreat>.Empty, GearGaps: ImmutableList<string>.Empty,
                WornGear: ImmutableList<WornSlot>.Empty, BestRecordedFloor: 0, RecordHolderName: "Torvald",
                GapCommissions: ImmutableList<GapCommission>.Empty);

            var line = MusterVoice.FollowedSendOffLine(state, ImmutableList.Create(party));

            AssertThat(line).IsNotNull();
            AssertThat(line!.Contains(item.Name, StringComparison.Ordinal)).IsTrue();
            AssertThat(line.Contains("Torvald", StringComparison.Ordinal)).IsTrue();
            AssertThat(line.Contains("floor 4", StringComparison.Ordinal)).IsTrue();
        }
        finally
        {
            FollowedItem.DeleteForTests();
        }
    }

    [TestCase]
    public void FollowedSendOffLine_HolderIsNotMusteringTomorrow_SaysStaysBehind_NeverAFabricatedMarch()
    {
        FollowedItem.DeleteForTests();
        try
        {
            var item = WeaponItem(FollowedWeaponId);
            var state = StateWithHolder(FollowedWeaponId, item, ItemSlot.Weapon);
            FollowedItem.Set(FollowedWeaponId);

            // No party names "Torvald" — the item's holder is not mustering tomorrow.
            var otherParty = new ForecastParty(
                HeroNames: ImmutableList.Create("Sable"), TargetFloor: 2, VenueId: "mine",
                Threats: ImmutableList<ForecastThreat>.Empty, GearGaps: ImmutableList<string>.Empty,
                WornGear: ImmutableList<WornSlot>.Empty, BestRecordedFloor: 0, RecordHolderName: "Sable",
                GapCommissions: ImmutableList<GapCommission>.Empty);

            var line = MusterVoice.FollowedSendOffLine(state, ImmutableList.Create(otherParty));

            AssertThat(line).IsNotNull();
            AssertThat(line!.Contains(item.Name, StringComparison.Ordinal)).IsTrue();
            AssertThat(line.Contains("stays behind", StringComparison.Ordinal)).IsTrue();
            AssertThat(line.Contains("floor", StringComparison.Ordinal))
                .OverrideFailureMessage($"a piece staying behind must never also claim a march: \"{line}\"").IsFalse();
        }
        finally
        {
            FollowedItem.DeleteForTests();
        }
    }

    // ── FollowedNightLine: not-followed, and a followed piece that stayed on the shelf ──────────────

    [TestCase]
    public void FollowedNightLine_NothingFollowed_RendersNothing()
    {
        FollowedItem.DeleteForTests();
        try
        {
            var state = GameFactory.NewGame(7004);
            AssertThat(MusterVoice.FollowedNightLine(state, ImmutableList<ExpeditionResult>.Empty)).IsNull();
        }
        finally
        {
            FollowedItem.DeleteForTests();
        }
    }

    [TestCase]
    public void FollowedNightLine_FollowedItemInNoRevealedExpedition_HonestShelfLine_NeverAFabricatedNight()
    {
        FollowedItem.DeleteForTests();
        try
        {
            var item = WeaponItem(FollowedWeaponId);
            var state = StateWithHolder(FollowedWeaponId, item, ItemSlot.Weapon);
            FollowedItem.Set(FollowedWeaponId);

            var line = MusterVoice.FollowedNightLine(state, ImmutableList<ExpeditionResult>.Empty);

            AssertThat(line).IsNotNull();
            AssertThat(line!.Contains(item.Name, StringComparison.Ordinal)).IsTrue();
            AssertThat(line.Contains("stayed on the shelf", StringComparison.Ordinal)).IsTrue();
        }
        finally
        {
            FollowedItem.DeleteForTests();
        }
    }

    // ── FollowedNightLine: the followed weapon's own night, across every shape it can take ─────────

    private static HeroAtDeparture Departure(ItemId? weapon = null, ItemId? armor = null) => new(
        FollowHeroId, "Torvald", ClassRegistry.VanguardId, Level: 2, MaxHp: 30, weapon, Shield: null, armor);

    [TestCase]
    public void FollowedNightLine_WeaponNeverSwung_SaysSoRatherThanClaimingATurnedNothingNight()
    {
        FollowedItem.DeleteForTests();
        try
        {
            var item = WeaponItem(FollowedWeaponId);
            var state = StateWithHolder(FollowedWeaponId, item, ItemSlot.Weapon);
            FollowedItem.Set(FollowedWeaponId);

            var result = new ExpeditionResult(
                Party: ImmutableList.Create(FollowHeroId), TargetFloor: 2, DeepestFloorCleared: 1,
                Floors: ImmutableList<FloorOutcome>.Empty, Survivors: ImmutableList.Create(FollowHeroId),
                Deaths: ImmutableList<HeroId>.Empty, Beats: ImmutableList<AttributionBeat>.Empty,
                Loot: ImmutableList<OreLoot>.Empty, GoldEarnedByHero: ImmutableSortedDictionary<int, int>.Empty)
            {
                PartyAtDeparture = ImmutableList.Create(Departure(weapon: FollowedWeaponId)),
            };

            var line = MusterVoice.FollowedNightLine(state, ImmutableList.Create(result));

            AssertThat(line).IsNotNull();
            AssertThat(line!.Contains("never swung", StringComparison.Ordinal)).IsTrue();
        }
        finally
        {
            FollowedItem.DeleteForTests();
        }
    }

    [TestCase]
    public void FollowedNightLine_WeaponSwungAndLandedTheRecordedKill_NamesTheFloorAndWhichSwing()
    {
        FollowedItem.DeleteForTests();
        try
        {
            var item = WeaponItem(FollowedWeaponId);
            var state = StateWithHolder(FollowedWeaponId, item, ItemSlot.Weapon);
            FollowedItem.Set(FollowedWeaponId);

            var miss = new CombatEvent(
                Floor: 1, Hero: FollowHeroId, MonsterKind: "cave-rat", RecordedRolls: ImmutableList<int>.Empty,
                DamageDealt: 0, DamageTaken: 0, MonsterKilled: false, KillingItem: null);
            var kill = new CombatEvent(
                Floor: 2, Hero: FollowHeroId, MonsterKind: "cave-rat", RecordedRolls: ImmutableList<int>.Empty,
                DamageDealt: 10, DamageTaken: 0, MonsterKilled: true, KillingItem: FollowedWeaponId);

            var result = new ExpeditionResult(
                Party: ImmutableList.Create(FollowHeroId), TargetFloor: 2, DeepestFloorCleared: 2,
                Floors: ImmutableList.Create(
                    new FloorOutcome(1, Cleared: true, Combats: ImmutableList.Create(miss)),
                    new FloorOutcome(2, Cleared: true, Combats: ImmutableList.Create(kill))),
                Survivors: ImmutableList.Create(FollowHeroId), Deaths: ImmutableList<HeroId>.Empty,
                Beats: ImmutableList<AttributionBeat>.Empty, Loot: ImmutableList<OreLoot>.Empty,
                GoldEarnedByHero: ImmutableSortedDictionary<int, int>.Empty)
            {
                PartyAtDeparture = ImmutableList.Create(Departure(weapon: FollowedWeaponId)),
            };

            var line = MusterVoice.FollowedNightLine(state, ImmutableList.Create(result));

            AssertThat(line).IsNotNull();
            AssertThat(line!.Contains("floor 2", StringComparison.Ordinal)).IsTrue();
            AssertThat(line.Contains("two times", StringComparison.Ordinal)).IsTrue();
            AssertThat(line.Contains("died", StringComparison.Ordinal)).IsTrue();
            AssertThat(line.Contains("second", StringComparison.Ordinal)).IsTrue();
        }
        finally
        {
            FollowedItem.DeleteForTests();
        }
    }

    [TestCase]
    public void FollowedNightLine_WeaponSwungButLandedNoRecordedKill_TurnedNothingLine_NeverAFabricatedKill()
    {
        FollowedItem.DeleteForTests();
        try
        {
            var item = WeaponItem(FollowedWeaponId);
            var state = StateWithHolder(FollowedWeaponId, item, ItemSlot.Weapon);
            FollowedItem.Set(FollowedWeaponId);

            var swing = new CombatEvent(
                Floor: 1, Hero: FollowHeroId, MonsterKind: "cave-rat", RecordedRolls: ImmutableList<int>.Empty,
                DamageDealt: 3, DamageTaken: 0, MonsterKilled: false, KillingItem: null);

            var result = new ExpeditionResult(
                Party: ImmutableList.Create(FollowHeroId), TargetFloor: 1, DeepestFloorCleared: 1,
                Floors: ImmutableList.Create(new FloorOutcome(1, Cleared: false, Combats: ImmutableList.Create(swing))),
                Survivors: ImmutableList.Create(FollowHeroId), Deaths: ImmutableList<HeroId>.Empty,
                Beats: ImmutableList<AttributionBeat>.Empty, Loot: ImmutableList<OreLoot>.Empty,
                GoldEarnedByHero: ImmutableSortedDictionary<int, int>.Empty)
            {
                PartyAtDeparture = ImmutableList.Create(Departure(weapon: FollowedWeaponId)),
            };

            var line = MusterVoice.FollowedNightLine(state, ImmutableList.Create(result));

            AssertThat(line).IsNotNull();
            AssertThat(line!.Contains("It turned nothing.", StringComparison.Ordinal)).IsTrue();
            AssertThat(line.Contains("died", StringComparison.Ordinal))
                .OverrideFailureMessage($"a swing with no recorded kill must never claim one: \"{line}\"").IsFalse();
        }
        finally
        {
            FollowedItem.DeleteForTests();
        }
    }

    // ── FollowedNightLine: the followed defensive piece's own night ────────────────────────────────

    [TestCase]
    public void FollowedNightLine_DefensivePieceTookNoDamage_NothingTouchedItLine()
    {
        FollowedItem.DeleteForTests();
        try
        {
            var item = ArmorItem(FollowedArmorId);
            var state = StateWithHolder(FollowedArmorId, item, ItemSlot.Armor);
            FollowedItem.Set(FollowedArmorId);

            var swing = new CombatEvent(
                Floor: 1, Hero: FollowHeroId, MonsterKind: "cave-rat", RecordedRolls: ImmutableList<int>.Empty,
                DamageDealt: 5, DamageTaken: 0, MonsterKilled: true, KillingItem: null);

            var result = new ExpeditionResult(
                Party: ImmutableList.Create(FollowHeroId), TargetFloor: 1, DeepestFloorCleared: 1,
                Floors: ImmutableList.Create(new FloorOutcome(1, Cleared: true, Combats: ImmutableList.Create(swing))),
                Survivors: ImmutableList.Create(FollowHeroId), Deaths: ImmutableList<HeroId>.Empty,
                Beats: ImmutableList<AttributionBeat>.Empty, Loot: ImmutableList<OreLoot>.Empty,
                GoldEarnedByHero: ImmutableSortedDictionary<int, int>.Empty)
            {
                PartyAtDeparture = ImmutableList.Create(Departure(armor: FollowedArmorId)),
            };

            var line = MusterVoice.FollowedNightLine(state, ImmutableList.Create(result));

            AssertThat(line).IsNotNull();
            AssertThat(line!.Contains("Nothing touched it.", StringComparison.Ordinal)).IsTrue();
        }
        finally
        {
            FollowedItem.DeleteForTests();
        }
    }

    [TestCase]
    public void FollowedNightLine_DefensivePieceTookHits_NamesTheWorstOne_NeverASurvivalVerdict()
    {
        FollowedItem.DeleteForTests();
        try
        {
            var item = ArmorItem(FollowedArmorId);
            var state = StateWithHolder(FollowedArmorId, item, ItemSlot.Armor);
            FollowedItem.Set(FollowedArmorId);

            var lightHit = new CombatEvent(
                Floor: 1, Hero: FollowHeroId, MonsterKind: "cave-rat", RecordedRolls: ImmutableList<int>.Empty,
                DamageDealt: 0, DamageTaken: 4, MonsterKilled: false, KillingItem: null);
            var worstHit = new CombatEvent(
                Floor: 2, Hero: FollowHeroId, MonsterKind: "cave-troll", RecordedRolls: ImmutableList<int>.Empty,
                DamageDealt: 0, DamageTaken: 9, MonsterKilled: false, KillingItem: null);

            var result = new ExpeditionResult(
                Party: ImmutableList.Create(FollowHeroId), TargetFloor: 2, DeepestFloorCleared: 2,
                Floors: ImmutableList.Create(
                    new FloorOutcome(1, Cleared: true, Combats: ImmutableList.Create(lightHit)),
                    new FloorOutcome(2, Cleared: true, Combats: ImmutableList.Create(worstHit))),
                Survivors: ImmutableList.Create(FollowHeroId), Deaths: ImmutableList<HeroId>.Empty,
                Beats: ImmutableList<AttributionBeat>.Empty, Loot: ImmutableList<OreLoot>.Empty,
                GoldEarnedByHero: ImmutableSortedDictionary<int, int>.Empty)
            {
                PartyAtDeparture = ImmutableList.Create(Departure(armor: FollowedArmorId)),
            };

            var line = MusterVoice.FollowedNightLine(state, ImmutableList.Create(result));

            AssertThat(line).IsNotNull();
            AssertThat(line!.Contains("floor 2", StringComparison.Ordinal)).IsTrue();
            AssertThat(line.Contains("9", StringComparison.Ordinal)).IsTrue();
            AssertThat(line.Contains("cave-troll".Replace("-", " "), StringComparison.OrdinalIgnoreCase)).IsTrue();
        }
        finally
        {
            FollowedItem.DeleteForTests();
        }
    }

    // ── FollowedItem: campaign-envelope snapshot/restore round-trip ────────────────────────────────

    [TestCase]
    public void FollowedItem_SnapshotThenRestore_RoundTripsToTheSameItem()
    {
        FollowedItem.DeleteForTests();
        try
        {
            FollowedItem.Set(FollowedWeaponId);
            var snapshot = FollowedItem.Snapshot();

            FollowedItem.Clear();
            AssertThat(FollowedItem.Current).IsNull();

            FollowedItem.Restore(snapshot);
            AssertThat(FollowedItem.Current).IsEqual(FollowedWeaponId);
        }
        finally
        {
            FollowedItem.DeleteForTests();
        }
    }

    [TestCase]
    public void FollowedItem_RestoreNullOrGarbage_AlwaysLandsOnNothingFollowed()
    {
        FollowedItem.DeleteForTests();
        try
        {
            FollowedItem.Set(FollowedWeaponId);
            FollowedItem.Restore(null);
            AssertThat(FollowedItem.Current).IsNull();

            FollowedItem.Set(FollowedWeaponId);
            FollowedItem.Restore("not-an-int");
            AssertThat(FollowedItem.Current).IsNull();

            FollowedItem.Set(FollowedWeaponId);
            FollowedItem.Restore(string.Empty);
            AssertThat(FollowedItem.Current).IsNull();
        }
        finally
        {
            FollowedItem.DeleteForTests();
        }
    }
}
#endif
