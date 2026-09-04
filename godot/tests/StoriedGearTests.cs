#if GDUNIT_TESTS
using System;
using System.Collections.Immutable;
using System.Linq;
using GameSim.Classes;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Heroes;
using GameSim.Kernel;
using GdUnit4;
using Godot;
using GodotClient.Panels;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// M2b — the storied-gear promotion, rendered. <see cref="ShoppingAi"/> has been promoting the
/// player's old work to heirloom status for months (once a worn item's Kills + Saves reach the
/// bearer's own trait-shifted threshold, that hero refuses to trade it for a marginal upgrade) and
/// no screen said so. This suite pins the three surfaces that now do: the item's card, the Legends
/// Wall, and the counter's spoken refusal.
///
/// <para>The load-bearing condition is that all three read as RECORDED FACTS, never credit — no
/// total across the player's work, no ratio, no medal, no percentage of party contribution, no
/// ranking against other gear, no score (CLAUDE.md law 4: the game shows only what the sim decided,
/// and there is no participation credit). The copy tripwires below fail on that vocabulary.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class StoriedGearTests
{
    private const string FixtureNamePrefix = "Sto";
    private static readonly ItemId WornId = new(901);
    private static readonly ItemId UpgradeId = new(902);
    private static readonly ItemId RivalWornId = new(903);

    /// <summary>Vocabulary that would turn a recorded fact into participation credit. Shared by
    /// every copy tripwire in this suite.</summary>
    private static readonly string[] CreditVocabulary =
    {
        "%", "total", "score", "rank", "best", "points", "credit", "contribution",
        "rating", "tier", "out of", "MVP", "award", "medal",
    };

    // ── the counter voices the refusal, and ONLY on a genuine Sentimental pass ──────────────────

    [TestCase]
    public void EveryPassReasonKind_EitherSpeaks_OrIsNamedInThePinnedExemptionList()
    {
        // Deny-by-default over the WHOLE enum by reflection — never a hand-listed array, which is
        // the guard shape that has silently stopped covering its family in this repo before. A new
        // PassReasonKind must either get a line here or be exempted on the record; adding one and
        // saying nothing turns this red.
        var exemptions = CustomerVoice.UnvoicedPassReasons;
        AssertThat(exemptions.Count)
            .OverrideFailureMessage(
                "CustomerVoice.UnvoicedPassReasons is pinned at 1 — a new exemption must be a reviewed diff, "
                + "with the reason that member can never reach a walk-away bubble.")
            .IsEqual(1);

        foreach (var kind in Enum.GetValues<PassReasonKind>())
        {
            if (exemptions.TryGetValue(kind, out var why))
            {
                AssertThat(why).IsNotEmpty();
                // Exempt means genuinely unrenderable, not a quiet fallthrough to an empty bubble.
                AssertThrown(() => CustomerVoice.PassReply(kind, "sim reason", "Emberfang"))
                    .IsInstanceOf<ArgumentOutOfRangeException>();
                continue;
            }

            var reply = CustomerVoice.PassReply(kind, "sim reason", "Emberfang");
            AssertThat(reply)
                .OverrideFailureMessage($"PassReasonKind.{kind} renders nothing and is not exempted.")
                .IsNotEmpty();
            AssertThat(reply).IsNotEqual(kind.ToString()); // an enum name is not a spoken line
        }
    }

    [TestCase]
    public void PassReply_Sentimental_SpeaksTheRefusalInTheHerosOwnVoice_AndNamesTheGearTheyAreKeeping()
    {
        var reply = CustomerVoice.PassReply(PassReasonKind.Sentimental, "sim reason", "Emberfang");

        AssertThat(reply).Contains("Emberfang");
        AssertThat(reply).IsNotEqual("sim reason");
        foreach (var banned in CreditVocabulary)
        {
            AssertThat(reply.ToLowerInvariant()).NotContains(banned.ToLowerInvariant());
        }
    }

    [TestCase]
    public void PassReply_EveryOtherReason_HandsBackTheSimsOwnProseVerbatim_NeverReworded()
    {
        foreach (var kind in Enum.GetValues<PassReasonKind>())
        {
            if (kind is PassReasonKind.Sentimental || CustomerVoice.UnvoicedPassReasons.ContainsKey(kind))
            {
                continue;
            }

            AssertThat(CustomerVoice.PassReply(kind, "shields don't suit a striker", "Emberfang"))
                .IsEqual("shields don't suit a striker");
        }
    }

    [TestCase]
    public void WalkReply_OnARealSentimentalRefusal_SpeaksIt()
    {
        var state = SentimentalRefusalWorld(out var hero, out var recordedReason);

        // The precondition is proven, not assumed: the sim itself must call this a Sentimental pass.
        var verdict = ShoppingAi.EvaluateItem(hero, Upgrade(), price: 5, state.Items);
        AssertThat(verdict.PassReason).IsEqual(PassReasonKind.Sentimental);

        var spoken = CustomerVoice.WalkReply(state, hero.Id, UpgradeId, recordedReason);

        AssertThat(spoken).IsNotEqual(recordedReason);
        AssertThat(spoken).Contains("Emberfang"); // the piece they are keeping, by name
    }

    [TestCase]
    public void WalkReply_OnAnyOtherRefusal_IsTheRecordedReasonVerbatim_NeverDecoration()
    {
        // Same world, same hero, an item they pass on for a DIFFERENT reason (priced past their
        // purse). Nothing storied is spoken, because nothing storied was decided.
        const int unaffordable = 9999;
        var state = SentimentalRefusalWorld(out var hero, out _);
        var shield = new Item(
            new ItemId(910), "recipe-shield", "Kite Shield", ItemSlot.Shield, QualityGrade.Fine,
            new ItemStats(0, 9, 6), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);
        state = state with
        {
            Items = state.Items.Add(shield.Id.Value, shield),
            Player = state.Player with { Shelf = state.Player.Shelf.Add(new ShelfEntry(shield.Id, unaffordable)) },
        };

        var verdict = ShoppingAi.EvaluateItem(hero, shield, unaffordable, state.Items);
        AssertThat(verdict.Kind).IsEqual(ShoppingVerdictKind.Pass);
        AssertThat(verdict.PassReason).IsNotEqual(PassReasonKind.Sentimental);

        AssertThat(CustomerVoice.WalkReply(state, hero.Id, shield.Id, verdict.Reason)).IsEqual(verdict.Reason);
    }

    [TestCase]
    public void WalkReply_WhenTheRecordedReasonCannotBeReproduced_FallsBackToItVerbatim()
    {
        // The equality guard: a reason this state's evaluator does not produce is never re-voiced,
        // so the storied line can never appear as decoration over a refusal that was something else.
        var state = SentimentalRefusalWorld(out var hero, out _);

        AssertThat(CustomerVoice.WalkReply(state, hero.Id, UpgradeId, "a reason from some other day"))
            .IsEqual("a reason from some other day");
        AssertThat(CustomerVoice.WalkReply(state, hero.Id, presented: null, "no item recorded"))
            .IsEqual("no item recorded");
        AssertThat(CustomerVoice.WalkReply(state, new HeroId(4242), UpgradeId, "no such hero"))
            .IsEqual("no such hero");
    }

    [TestCase]
    public void WalkReply_WhenTheItemHasLeftTheShelf_FallsBackToTheRecordedReason()
    {
        var state = SentimentalRefusalWorld(out var hero, out var recordedReason);
        var unstocked = state with { Player = state.Player with { Shelf = ImmutableList<ShelfEntry>.Empty } };

        AssertThat(CustomerVoice.WalkReply(unstocked, hero.Id, UpgradeId, recordedReason)).IsEqual(recordedReason);
    }

    // ── the item's card reads storied ──────────────────────────────────────────────────────────

    [TestCase]
    public void ProvenanceCard_StoriedWornGear_ReadsStoried_WithTheRecordedFactsAndNoCredit()
    {
        var ui = MountMainUi(new SimAdapter(SentimentalRefusalWorld(out var hero, out _)));
        try
        {
            ui.Heroes.SelectHero(hero.Id.Value);
            PressEnabled(ui.Heroes, $"Provenance_{WornId.Value}");

            var card = Find<ProvenanceCard>(ui.Heroes, "ProvenanceCard");
            AssertThat(card.ShownItemId).IsEqual(WornId);

            var line = Find<Label>(card, "ProvenanceStoriedLine").Text;
            AssertThat(line).Contains("Storied");
            AssertThat(line).Contains(hero.Name);
            AssertThat(line).Contains("4 fights");
            AssertThat(RenderedText(card)).Contains(line); // it really is on the visible card

            foreach (var banned in CreditVocabulary)
            {
                AssertThat(line.ToLowerInvariant())
                    .OverrideFailureMessage($"The storied card line must never read as credit; found \"{banned}\".")
                    .NotContains(banned.ToLowerInvariant());
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void ProvenanceCard_GearBelowTheBearersThreshold_SaysNothing()
    {
        // One deed short: the gate does not fire, so the card renders no storied line at all —
        // an honest empty state, never a "not storied yet" consolation row.
        var world = SentimentalRefusalWorld(out var hero, out _);
        var quieter = hero with
        {
            Memories = ImmutableList.Create(new ItemMemory(WornId, Kills: StoriedGear.ThresholdFor(hero) - 1, Saves: 0)),
        };
        world = world with { Heroes = world.Heroes.SetItem(quieter.Id.Value, quieter) };

        var ui = MountMainUi(new SimAdapter(world));
        try
        {
            ui.Heroes.SelectHero(hero.Id.Value);
            PressEnabled(ui.Heroes, $"Provenance_{WornId.Value}");

            AssertThat(RenderedText(Find<ProvenanceCard>(ui.Heroes, "ProvenanceCard"))).NotContains("Storied");
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── the legends wall lists objects, not only people ────────────────────────────────────────

    [TestCase]
    public void LegendsWall_ListsStoriedGear_NamingItsBearerAndItsDeeds()
    {
        var ui = MountMainUi();
        try
        {
            ui.Legends.ShowWall(SentimentalRefusalWorld(out var hero, out _));

            AssertThat(ui.Legends.ShowedEmptyState).IsFalse();
            AssertThat(ui.Legends.StoriedItemCount).IsEqual(1);

            var text = RenderedText(ui.Legends);
            AssertThat(text).Contains("STORIED GEAR");
            AssertThat(text).Contains("Emberfang");
            AssertThat(text).Contains(hero.Name);
            AssertThat(text).Contains("4 fights");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void LegendsWall_StoriedSection_NeverListsGearThatCarriesNoMakersMark()
    {
        // A rival's blade can cross the same threshold — the sim's gate does not care who forged
        // it — but this wall is the town's memory of the PLAYER's work. A row here for someone
        // else's stock is the participation-credit inversion link 4 forbids.
        var world = SentimentalRefusalWorld(out var hero, out _);
        var rivalWorn = new Item(
            RivalWornId, "recipe-rival", "Rival Cleaver", ItemSlot.Armor, QualityGrade.Fine,
            new ItemStats(0, 5, 4), Mark: null, ImmutableList<ItemHistoryEntry>.Empty);
        var wearingBoth = world.Heroes[hero.Id.Value] with
        {
            Gear = world.Heroes[hero.Id.Value].Gear.WithSlot(ItemSlot.Armor, RivalWornId),
            Memories = world.Heroes[hero.Id.Value].Memories.Add(new ItemMemory(RivalWornId, Kills: 4, Saves: 0)),
        };
        world = world with
        {
            Items = world.Items.Add(RivalWornId.Value, rivalWorn),
            Heroes = world.Heroes.SetItem(hero.Id.Value, wearingBoth),
        };

        // The QUERY still sees both — it is an honest read of the rule, not a filtered one.
        AssertThat(StoriedGear.All(world).Count).IsEqual(2);

        var ui = MountMainUi();
        try
        {
            ui.Legends.ShowWall(world);

            AssertThat(ui.Legends.StoriedItemCount).IsEqual(1);
            AssertThat(RenderedText(ui.Legends)).NotContains("Rival Cleaver");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void LegendsWall_StoriedRows_CarryNoScoreRatioOrRanking()
    {
        var ui = MountMainUi();
        try
        {
            ui.Legends.ShowWall(SentimentalRefusalWorld(out _, out _));

            // Scoped to the storied section's own rows — the rest of the wall has its own register.
            var text = RenderedText(Find<VBoxContainer>(ui.Legends, "StoriedItemsSection")).ToLowerInvariant();
            AssertThat(text).Contains("emberfang");
            foreach (var banned in CreditVocabulary)
            {
                AssertThat(text)
                    .OverrideFailureMessage($"A storied row must read as a recorded fact; found \"{banned}\".")
                    .NotContains(banned.ToLowerInvariant());
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void LegendsWall_WithNoStoriedGear_SaysSoWithoutInventingARow()
    {
        // A wall that has SOMETHING on it (one memorial) but no storied gear — an all-empty wall
        // takes its own invitational early return and never reaches this section.
        var world = SentimentalRefusalWorld(out var hero, out _);
        var forgetful = world.Heroes[hero.Id.Value] with { Memories = ImmutableList<ItemMemory>.Empty };
        world = world with
        {
            Heroes = world.Heroes.SetItem(hero.Id.Value, forgetful),
            Drama = world.Drama with
            {
                Memorials = ImmutableList.Create(new Memorial(new HeroId(99), "Sera", Day: 4, GearNamed: "a plain blade")),
            },
        };

        var ui = MountMainUi();
        try
        {
            ui.Legends.ShowWall(world);

            AssertThat(ui.Legends.ShowedEmptyState).IsFalse();
            AssertThat(ui.Legends.StoriedItemCount).IsEqual(0);
            AssertThat(RenderedText(ui.Legends)).Contains("No storied gear yet");
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── fixtures ───────────────────────────────────────────────────────────────────────────────

    private static Item Worn() => new(
        WornId, "recipe-worn", "Emberfang", ItemSlot.Weapon, QualityGrade.Fine,
        new ItemStats(6, 0, 3), new MakersMark("You", 1),
        ImmutableList.Create(new ItemHistoryEntry(2, "kill", "cave rat")));

    private static Item Upgrade() => new(
        UpgradeId, "recipe-upgrade", "Plain Blade", ItemSlot.Weapon, QualityGrade.Fine,
        new ItemStats(8, 0, 3), new MakersMark("You", 3), ImmutableList<ItemHistoryEntry>.Empty);

    /// <summary>
    /// A world where the sim really does refuse: one hero wearing <see cref="Worn"/> with four
    /// recorded deeds, and a +2 blade on the shelf — a gain below
    /// <see cref="ShoppingAi.SentimentalMinDisplacementGain"/>, so the storied gate fires. The
    /// hero is FOUND by scanning ids for one who is not Practical (traits are derived from id and
    /// name, never stored), so the fixture can never silently land on the hero this gate skips.
    /// </summary>
    private static GameState SentimentalRefusalWorld(out Hero hero, out string recordedReason)
    {
        var (id, name) = FindNonPracticalHero();
        hero = new Hero(
            id, name, ClassRegistry.VanguardId, Level: 3, MaxHp: 40, Gold: 500,
            GearSet.Empty.WithSlot(ItemSlot.Weapon, WornId),
            ImmutableList.Create(new ItemMemory(WornId, Kills: 2, Saves: 2)), // 4 deeds
            Alive: true, DeepestFloorReached: 1, DiedOnDay: null);

        var baseState = GameFactory.NewGame(6400);
        var state = baseState with
        {
            Heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(hero.Id.Value, hero),
            Items = ImmutableSortedDictionary<int, Item>.Empty
                .Add(WornId.Value, Worn())
                .Add(UpgradeId.Value, Upgrade()),
            Player = baseState.Player with { Shelf = ImmutableList.Create(new ShelfEntry(UpgradeId, 5)) },
        };

        recordedReason = ShoppingAi.EvaluateItem(hero, Upgrade(), price: 5, state.Items).Reason;
        return state;
    }

    private static (HeroId Id, string Name) FindNonPracticalHero(int maxId = 2000)
    {
        for (var id = 1; id <= maxId; id++)
        {
            var heroId = new HeroId(id);
            var name = $"{FixtureNamePrefix}{id}";
            if (!TraitRegistry.TraitsFor(heroId, name).Contains(TraitId.Practical))
            {
                return (heroId, name);
            }
        }

        throw new InvalidOperationException($"No hero id in 1..{maxId} is non-Practical.");
    }
}
#endif
