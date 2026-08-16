#if GDUNIT_TESTS
using System;
using System.Collections.Immutable;
using GameSim.Classes;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Heroes;
using GameSim.Kernel;
using GdUnit4;
using GodotClient.Ui;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// U2 (plan 2026-08-03-001-feat-loop-structure-plan.md, KTD-B — "the customer speaks first"):
/// pins <see cref="CustomerVoice"/>'s pure functions in isolation, with no Godot mounting needed —
/// the same "pure logic, gdUnit-decorated for suite consistency" shape as <c>PhaseVocabTests</c>.
/// Every line asserted here is checked against what the sim's OWN evaluators
/// (<see cref="ShoppingAi.EvaluateItem"/>, <see cref="RaidForecast.MissingItemSlots"/>) actually
/// report for the fixture, never a hand-picked expectation that could drift from the sim.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class CustomerVoiceTests
{
    // ── WantLine: the empty/weakest-slot gap query + the hero's real gold ──────────────────────

    [TestCase]
    public void WantLine_HeroWithAGearGap_NamesTheFirstMissingSlotAndTheHerosOwnGold()
    {
        // GearSet.Empty means MissingItemSlots reports Weapon/Shield/Armor, in that fixed order —
        // the want line must name the FIRST one (Weapon), never invent a different slot.
        var hero = MakeHero(1, ClassRegistry.VanguardId, gold: 45, GearSet.Empty);
        var state = GameFactory.NewGame(9101);

        var line = CustomerVoice.WantLine(hero, state);

        AssertThat(RaidForecast.MissingItemSlots(hero.Gear)[0]).IsEqual(ItemSlot.Weapon);
        AssertThat(line).Contains("a weapon");
        AssertThat(line).Contains("45g");
        // U1 (§11.11): the extracted projection must name the exact same slot WantLine spoke.
        AssertThat(CounterForecast.Wants(hero, state)).IsEqual(ItemSlot.Weapon);
    }

    [TestCase]
    public void WantLine_HeroWithNoGaps_NamesTheStrongestActualShelfUpgrade_NeverAnItemTheSimWouldRefuse()
    {
        // A fully-geared vanguard: every slot filled with a weak (0-stat) item, so ANY shelf item
        // with real stats is a genuine gear-score upgrade — ShoppingAi.EvaluateItem must return Buy
        // for it, which is exactly the signal WantLine's full-loadout branch keys on.
        var weakWeapon = MakeItem(1, ItemSlot.Weapon, attack: 0, defense: 0, weight: 1, name: "Rusty Knife");
        var weakShield = MakeItem(2, ItemSlot.Shield, attack: 0, defense: 0, weight: 1, name: "Cracked Buckler");
        var weakArmor = MakeItem(3, ItemSlot.Armor, attack: 0, defense: 0, weight: 1, name: "Ragged Coat");
        var upgrade = MakeItem(4, ItemSlot.Weapon, attack: 8, defense: 0, weight: 2, name: "Fine Blade");

        var hero = MakeHero(1, ClassRegistry.VanguardId, gold: 200,
            new GearSet(weakWeapon.Id, weakShield.Id, weakArmor.Id));

        var state = GameFactory.NewGame(9102) with
        {
            Items = ImmutableSortedDictionary<int, Item>.Empty
                .Add(weakWeapon.Id.Value, weakWeapon)
                .Add(weakShield.Id.Value, weakShield)
                .Add(weakArmor.Id.Value, weakArmor)
                .Add(upgrade.Id.Value, upgrade),
            Player = PlayerState.NewGame(0) with { Shelf = ImmutableList.Create(new ShelfEntry(upgrade.Id, 20)) },
        };

        // Confirm the sim itself calls this a Buy before asserting the spoken line agrees with it —
        // the line must never name an upgrade the sim would actually refuse.
        var verdict = ShoppingAi.EvaluateItem(hero, upgrade, 20, state.Items);
        AssertThat(verdict.Kind).IsEqual(ShoppingVerdictKind.Buy);

        var line = CustomerVoice.WantLine(hero, state);

        AssertThat(RaidForecast.MissingItemSlots(hero.Gear).Count).IsEqual(0); // no gaps — full loadout
        AssertThat(line).Contains("weapon");
        AssertThat(line).Contains("200g");
        // U1 (§11.11): test scenario 3 — a full-loadout hero's projected want is the shelf
        // upgrade slot, never null (the forecast must never under-claim a real want either).
        AssertThat(CounterForecast.Wants(hero, state)).IsEqual(ItemSlot.Weapon);
    }

    [TestCase]
    public void WantLine_HeroWithNoGapsAndNoShelfUpgrade_FallsBackToBrowsing_NeverInventsAWant()
    {
        var bestWeapon = MakeItem(1, ItemSlot.Weapon, attack: 9, defense: 0, weight: 1, name: "Masterwork Blade");
        // Shield/Armor filled with dummy ids (never resolved in state.Items, same as an unremarkable
        // 0-stat item for GearScore purposes) purely so RaidForecast.MissingItemSlots reports ZERO
        // gaps — this test is about the full-loadout FALLBACK branch, not the gap-query branch.
        var hero = MakeHero(1, ClassRegistry.VanguardId, gold: 60,
            new GearSet(bestWeapon.Id, new ItemId(998), new ItemId(999)));

        var worseWeapon = MakeItem(2, ItemSlot.Weapon, attack: 1, defense: 0, weight: 1, name: "Dull Blade");
        var state = GameFactory.NewGame(9103) with
        {
            Items = ImmutableSortedDictionary<int, Item>.Empty
                .Add(bestWeapon.Id.Value, bestWeapon)
                .Add(worseWeapon.Id.Value, worseWeapon),
            Player = PlayerState.NewGame(0) with { Shelf = ImmutableList.Create(new ShelfEntry(worseWeapon.Id, 5)) },
        };

        var verdict = ShoppingAi.EvaluateItem(hero, worseWeapon, 5, state.Items);
        AssertThat(verdict.Kind).IsEqual(ShoppingVerdictKind.Pass); // strictly worse — no gear-score gain

        var line = CustomerVoice.WantLine(hero, state);

        AssertThat(line).Contains("Just browsing");
        AssertThat(line).Contains("60g");
        // U1 (§11.11): test scenario 3 — browsing means the projection is honestly null, never a
        // fabricated slot the sim has no basis for.
        AssertThat(CounterForecast.Wants(hero, state)).IsNull();
    }

    // ── U1 (§11.11): CounterForecast.Wants extracted from WantLine — same slot, one function ───

    /// <summary>Test scenario 2: parameterised over all eight Weapon/Shield/Armor null
    /// combinations. A hero with any gap must project the FIRST missing slot in fixed order; a
    /// full-loadout hero (all three filled) must project the shelf's best genuine upgrade (a real
    /// weapon upgrade is planted on the shelf so that branch is exercised too) — in every case the
    /// slot <see cref="CounterForecast.Wants"/> names must be the one <see cref="CustomerVoice.WantLine"/>
    /// actually speaks, so the counter can never ask for something the forecast board didn't ALSO
    /// show the night before.</summary>
    [TestCase(true, true, true)]
    [TestCase(true, true, false)]
    [TestCase(true, false, true)]
    [TestCase(true, false, false)]
    [TestCase(false, true, true)]
    [TestCase(false, true, false)]
    [TestCase(false, false, true)]
    [TestCase(false, false, false)]
    public void Wants_MatchesCustomerVoiceWantLine_ForEveryGearShape(bool hasWeapon, bool hasShield, bool hasArmor)
    {
        var weakWeapon = MakeItem(21, ItemSlot.Weapon, attack: 0, defense: 0, weight: 1, name: "Rusty Knife");
        var weakShield = MakeItem(22, ItemSlot.Shield, attack: 0, defense: 0, weight: 1, name: "Cracked Buckler");
        var weakArmor = MakeItem(23, ItemSlot.Armor, attack: 0, defense: 0, weight: 1, name: "Ragged Coat");
        var upgrade = MakeItem(24, ItemSlot.Weapon, attack: 8, defense: 0, weight: 2, name: "Fine Blade");

        var gear = new GearSet(
            hasWeapon ? weakWeapon.Id : null,
            hasShield ? weakShield.Id : null,
            hasArmor ? weakArmor.Id : null);
        var hero = MakeHero(1, ClassRegistry.VanguardId, gold: 77, gear);

        var state = GameFactory.NewGame(9210) with
        {
            Items = ImmutableSortedDictionary<int, Item>.Empty
                .Add(weakWeapon.Id.Value, weakWeapon)
                .Add(weakShield.Id.Value, weakShield)
                .Add(weakArmor.Id.Value, weakArmor)
                .Add(upgrade.Id.Value, upgrade),
            Player = PlayerState.NewGame(0) with { Shelf = ImmutableList.Create(new ShelfEntry(upgrade.Id, 20)) },
        };

        var wantSlot = CounterForecast.Wants(hero, state);
        var line = CustomerVoice.WantLine(hero, state);

        var missing = RaidForecast.MissingItemSlots(hero.Gear);
        if (missing.Count > 0)
        {
            AssertThat(wantSlot).IsEqual(missing[0]);
        }

        if (wantSlot is { } slot)
        {
            var word = slot switch
            {
                ItemSlot.Weapon => "weapon",
                ItemSlot.Shield => "shield",
                ItemSlot.Armor => "armor",
                _ => slot.ToString().ToLowerInvariant(),
            };
            AssertThat(line.ToLowerInvariant()).Contains(word);
        }
        else
        {
            AssertThat(line).Contains("Just browsing");
        }
    }

    // ── PresentReply: exhaustive over ShoppingVerdictKind, never an empty bubble ────────────────

    [TestCase]
    public void PresentReply_Buy_NamesTheItemAsSomethingTheyCouldUse()
    {
        var reply = CustomerVoice.PresentReply(ShoppingVerdictKind.Buy, "Iron Sword", passReason: string.Empty);

        AssertThat(reply).Contains("Iron Sword");
        AssertThat(reply).Contains("could use");
    }

    [TestCase]
    public void PresentReply_Pass_ReturnsTheSimsOwnReasonVerbatim_NeverReworded()
    {
        var reply = CustomerVoice.PresentReply(
            ShoppingVerdictKind.Pass, itemName: "ignored for Pass", "shields don't suit a striker");

        AssertThat(reply).IsEqual("shields don't suit a striker");
    }

    [TestCase]
    public void PresentReply_EveryShoppingVerdictKind_RendersANonEmptyReply()
    {
        // Enumerated from the sim's OWN enum (never hand-listed) — a newly added ShoppingVerdictKind
        // that this switch doesn't know about throws inside PresentReply instead of silently
        // rendering an empty bubble, which fails this test loudly.
        foreach (var kind in Enum.GetValues<ShoppingVerdictKind>())
        {
            var reply = CustomerVoice.PresentReply(kind, "Test Blade", "some reason");
            AssertThat(reply).OverrideFailureMessage($"{kind} rendered an empty reply").IsNotEmpty();
        }
    }

    // ── SuggestReply: derived from the OBSERVED interest delta, never a re-derived fit rule ─────

    [TestCase]
    public void SuggestReply_InterestRose_NamesTheItemAndThatTheyLackOne()
    {
        var reply = CustomerVoice.SuggestReply("Iron Shield", interestRose: true);

        AssertThat(reply).Contains("Iron Shield");
        AssertThat(reply).Contains("I do lack one");
    }

    [TestCase]
    public void SuggestReply_InterestHeld_ReturnsNoUseForThat()
    {
        var reply = CustomerVoice.SuggestReply("Iron Shield", interestRose: false);

        AssertThat(reply).IsEqual("No use for that.");
    }

    // ── Fixtures ─────────────────────────────────────────────────────────────────────────────────

    private static Hero MakeHero(int id, string classId, int gold, GearSet gear) => new(
        new HeroId(id), $"Voice{id}", classId, Level: 1, MaxHp: 24, Gold: gold,
        gear, ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 0, DiedOnDay: null);

    private static Item MakeItem(int id, ItemSlot slot, int attack, int defense, int weight, string name) => new(
        new ItemId(id), "test-recipe", name, slot, QualityGrade.Common,
        new ItemStats(attack, defense, weight), Mark: null,
        ImmutableList<ItemHistoryEntry>.Empty);
}
#endif
