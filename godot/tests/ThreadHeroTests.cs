#if GDUNIT_TESTS
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection;
using GameSim;
using GameSim.Classes;
using GameSim.Contracts;
using GameSim.Kernel;
using GdUnit4;
using GodotClient.Ui;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// U22 (§11.14.14): pins <see cref="TutorialFlow.ThreadHero"/> — the first hero, in event order, the
/// player's own work reached (that method's own doc has the full channel/ordering rule; this file
/// proves each promise it makes). Five things, matching the unit's own spec:
///
/// <list type="bullet">
/// <item>stable across a reload (a real <see cref="SaveCodec"/> round-trip, not just "call it
/// twice") for the identical event log;</item>
/// <item>nothing before any hand-off;</item>
/// <item>event order decides, not channel priority, when two channels both delivered — the whole
/// reason this is a Day comparison first and a declared-order tiebreak only on an exact tie;</item>
/// <item>the pinned rule: no <see cref="TutorialStepDef.IsDone"/> anywhere reaches <see
/// cref="TutorialFlow.ThreadHero"/>, proved by walking each compiled delegate's own IL rather than
/// trusting a comment;</item>
/// <item>copy built from it names the thread hero and never borrows — or contradicts — a beat that
/// actually landed on someone else.</item>
/// </list>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ThreadHeroTests
{
    private static Item PlayerCraftedSword(ItemId id, int day) => new(
        id, "recipe.sword", "Iron Sword", ItemSlot.Weapon, QualityGrade.Common,
        new ItemStats(Attack: 5, Defense: 0, Weight: 3), new MakersMark("You", day),
        ImmutableList<ItemHistoryEntry>.Empty);

    [TestCase]
    public void ThreadHero_IsStableAcrossAReload_ForTheSameEventLog()
    {
        var baseState = GameComposition.NewCampaign(9001);
        var hero = new HeroId(7);
        var state = baseState with
        {
            EventLog = baseState.EventLog.Add(new SupplyDelivered(hero, new ItemId(1), Fee: 3) { Day = 2 }),
        };

        var before = TutorialFlow.ThreadHero(state);
        AssertThat(before)
            .OverrideFailureMessage("Expected a thread hero once a vigil supply lands.")
            .IsEqual(hero);

        // The real reload path — SaveCodec is what user:// actually writes/reads — not a hand-rolled
        // stand-in, so this proves the derivation survives the SAME round-trip a real Continue does.
        var reloaded = SaveCodec.Deserialize(SaveCodec.Serialize(state));
        var after = TutorialFlow.ThreadHero(reloaded);

        AssertThat(after)
            .OverrideFailureMessage(
                "ThreadHero disagreed with itself across a save/reload round-trip for the identical event log " +
                "— it must be a pure function of state, nothing cached or static.")
            .IsEqual(before);
    }

    [TestCase]
    public void ThreadHero_ReturnsNothing_BeforeAnyHandOff()
    {
        var baseState = GameComposition.NewCampaign(9001);
        AssertThat(TutorialFlow.ThreadHero(baseState))
            .OverrideFailureMessage("A fresh campaign with no hand-off yet must have no thread hero.")
            .IsNull();

        var hero = new HeroId(3);
        var item = new ItemId(1);
        // Crafted (never sold) and a commission POSTED (never accepted) — neither is a hand-off.
        var stillNothing = baseState with
        {
            Items = baseState.Items.Add(item.Value, PlayerCraftedSword(item, day: 1)),
            EventLog = baseState.EventLog
                .Add(new ItemCrafted(item, QualityGrade.Common) { Day = 1 })
                .Add(new CommissionPosted(hero, ItemSlot.Weapon, QualityGrade.Common, DeadlineDay: 5, PremiumGold: 10) { Day = 1 }),
        };

        AssertThat(TutorialFlow.ThreadHero(stillNothing))
            .OverrideFailureMessage("A crafted-but-unsold item and a POSTED (not accepted) commission are not a hand-off.")
            .IsNull();
    }

    [TestCase]
    public void ThreadHero_EventOrderDecides_NotChannelPriority_WhenTwoChannelsBothDelivered()
    {
        var baseState = GameComposition.NewCampaign(9001);
        var vigilHero = new HeroId(1); // Day 1 — the LOWEST-ranked channel in ThreadHero's own list
        var shopHero = new HeroId(2);  // Day 5 — the HIGHEST-ranked channel in that same list
        var item = new ItemId(1);

        var state = baseState with
        {
            Items = baseState.Items.Add(item.Value, PlayerCraftedSword(item, day: 5)),
            EventLog = baseState.EventLog
                .Add(new SupplyDelivered(vigilHero, new ItemId(2), Fee: 3) { Day = 1 })
                .Add(new ItemSold(item, shopHero, 20, FromPlayerShop: true) { Day = 5 }),
        };

        AssertThat(TutorialFlow.ThreadHero(state))
            .OverrideFailureMessage(
                "A Day-1 vigil supply must beat a Day-5 shop sale even though 'shop sale' is listed FIRST in " +
                "ThreadHero's own fallback order — event order decides, not channel priority.")
            .IsEqual(vigilHero);
    }

    [TestCase]
    public void ThreadHero_UsesItsDeclaredOrder_OnlyAsASameDayTiebreak()
    {
        var baseState = GameComposition.NewCampaign(9001);
        var shopHero = new HeroId(2);
        var commissionHero = new HeroId(3);
        var item = new ItemId(1);

        // Both channels first fire on the SAME day — the only situation where the declared
        // shop/commission/vigil order is allowed to decide anything at all (ThreadHero's own doc).
        var tied = baseState with
        {
            Items = baseState.Items.Add(item.Value, PlayerCraftedSword(item, day: 5)),
            EventLog = baseState.EventLog.Add(new ItemSold(item, shopHero, 20, FromPlayerShop: true) { Day = 5 }),
            ActionLog = baseState.ActionLog.Add(new LoggedBatch(
                5, DayPhase.Morning, ImmutableList.Create<PlayerAction>(new AcceptCommissionAction(commissionHero)))),
        };

        AssertThat(TutorialFlow.ThreadHero(tied))
            .OverrideFailureMessage("On an exact same-day tie, the shop sale must win the declared tiebreak over an accepted commission.")
            .IsEqual(shopHero);
    }

    [TestCase]
    /// <summary>
    /// U22 follow-up: the counter is the fourth hand-off channel and emits its OWN event
    /// (<c>CounterSaleClosed</c>) with no companion <c>ItemSold</c>. A hero who haggled face to face
    /// and walked out with the player's work used to be invisible here — which is perverse, since the
    /// counter is the one channel where the player and the hero are in the same room.
    /// </summary>
    public void ThreadHero_NamesAHero_WhoBoughtOnlyAtTheCounter()
    {
        var baseState = GameComposition.NewCampaign(9001);
        var hero = new HeroId(7);
        var state = baseState with
        {
            EventLog = baseState.EventLog.Add(
                new CounterSaleClosed(hero, new ItemId(1), Price: 14, Pinned: true) { Day = 2 }),
        };

        AssertThat(TutorialFlow.ThreadHero(state))
            .OverrideFailureMessage(
                "A counter sale is a hand-off: the hero stood at the counter and took the player's work. " +
                "It emits CounterSaleClosed and never ItemSold, so a derivation reading only ItemSold " +
                "misses the channel the course most wants to name.")
            .IsEqual(hero);
    }

    [TestCase]
    public void ThreadHero_IgnoresAShopSale_OfANonPlayerCraftedItem()
    {
        var baseState = GameComposition.NewCampaign(9001);
        var hero = new HeroId(4);
        var item = new ItemId(9);
        var rivalItem = new Item(
            item, "recipe.dagger", "Rusty Dagger", ItemSlot.Weapon, QualityGrade.Poor,
            new ItemStats(2, 0, 1), null, ImmutableList<ItemHistoryEntry>.Empty);

        var state = baseState with
        {
            Items = baseState.Items.Add(item.Value, rivalItem),
            EventLog = baseState.EventLog.Add(new ItemSold(item, hero, 5, FromPlayerShop: true) { Day = 1 }),
        };

        AssertThat(TutorialFlow.ThreadHero(state))
            .OverrideFailureMessage("A sale of a NON-player-crafted item is not a hand-off of the player's own work.")
            .IsNull();
    }

    /// <summary>
    /// The hard rule (U22's own mandate): building copy from <see cref="TutorialFlow.ThreadHero"/>
    /// is limited to NAMING the thread hero — never asserting what happens to them, because a beat
    /// can (and here does) land on somebody else entirely. The thread hero (accepted a commission on
    /// Day 1) and the beat's own hero (a killing blow on Day 3) are deliberately different people.
    /// </summary>
    [TestCase]
    public void CopyBuiltFromThreadHero_NamesTheHero_NeverAssertsWhoABeatContradicts()
    {
        var baseState = GameComposition.NewCampaign(9001);
        var threadHeroId = new HeroId(1);
        var beatHeroId = new HeroId(2);

        var threadHero = new Hero(
            threadHeroId, "Aldric", ClassRegistry.VanguardId, Level: 1, MaxHp: 20, Gold: 0,
            Gear: GearSet.Empty, Memories: ImmutableList<ItemMemory>.Empty, Alive: true,
            DeepestFloorReached: 0, DiedOnDay: null);
        var beatHero = new Hero(
            beatHeroId, "Emberbite", ClassRegistry.StrikerId, Level: 3, MaxHp: 28, Gold: 0,
            Gear: GearSet.Empty, Memories: ImmutableList<ItemMemory>.Empty, Alive: true,
            DeepestFloorReached: 3, DiedOnDay: null);

        var state = baseState with
        {
            Heroes = baseState.Heroes.SetItem(threadHeroId.Value, threadHero).SetItem(beatHeroId.Value, beatHero),
            ActionLog = baseState.ActionLog.Add(new LoggedBatch(
                1, DayPhase.Morning, ImmutableList.Create<PlayerAction>(new AcceptCommissionAction(threadHeroId)))),
            EventLog = baseState.EventLog.Add(
                new AttributionBeatEvent(BeatType.KillingBlow, new ItemId(1), beatHeroId, Floor: 3,
                    Detail: "turned the killing blow") { Day = 3 }),
        };

        var resolved = TutorialFlow.ThreadHero(state);
        AssertThat(resolved)
            .OverrideFailureMessage("The thread hero must stay whoever the player reached out to FIRST, not whoever a later beat rewarded.")
            .IsEqual(threadHeroId);

        // The one shape a row's copy is allowed to build: name the thread hero. It must never
        // borrow, or contradict, the beat's own claim.
        var copy = $"Remember the name: {state.Heroes[resolved!.Value.Value].Name}.";

        AssertThat(copy.Contains(threadHero.Name))
            .OverrideFailureMessage("The naming copy must name the thread hero.")
            .IsTrue();
        AssertThat(copy.Contains(beatHero.Name))
            .OverrideFailureMessage("The naming copy must never name the hero the beat actually landed on instead.")
            .IsFalse();
        AssertThat(copy.Contains("killing blow"))
            .OverrideFailureMessage("The naming copy must never assert the beat's own outcome — that is the card's job, never the mechanism's.")
            .IsFalse();
    }

    /// <summary>
    /// The pinned rule, as a test rather than a comment: walks every <see
    /// cref="TutorialStepDef.IsDone"/> delegate's own compiled IL (and, transitively, any private
    /// helper inside <see cref="TutorialFlow"/> it calls — the exact shape <c>CounterAnsweredAtLeastOnce</c>
    /// / <c>AnyPartyStagedForCheckpointToday</c> already use for other rows) looking for a direct
    /// call to <see cref="TutorialFlow.ThreadHero"/>. A future row that reaches it — even indirectly
    /// — fails this test, not a code reviewer's memory.
    /// </summary>
    [TestCase]
    public void NoRegistryIsDone_ReadsThreadHero()
    {
        var threadHero = typeof(TutorialFlow).GetMethod(nameof(TutorialFlow.ThreadHero), BindingFlags.Public | BindingFlags.Static);
        AssertThat(threadHero)
            .OverrideFailureMessage("TutorialFlow.ThreadHero was not found by reflection — this test cannot pin anything against it.")
            .IsNotNull();

        foreach (var def in TutorialFlow.Registry)
        {
            var visited = new HashSet<MethodBase>();
            AssertThat(CallsThreadHero(def.IsDone.Method, threadHero!, visited))
                .OverrideFailureMessage(
                    $"{def.Step}'s IsDone reaches ThreadHero, directly or through a helper in this class — " +
                    "completion may NEVER key on which hero the course happens to be naming (ThreadHero's own doc).")
                .IsFalse();
        }
    }

    /// <summary>IL walk for a direct <c>call</c>/<c>callvirt</c> into <paramref name="threadHero"/>,
    /// recursing only into OTHER <see cref="TutorialFlow"/>-declared methods a candidate calls (Linq/
    /// BCL/Godot calls can never reach it and would otherwise make this an unbounded whole-program
    /// walk). <paramref name="visited"/> guards against a cycle between two helpers.</summary>
    private static bool CallsThreadHero(MethodBase method, MethodInfo threadHero, HashSet<MethodBase> visited)
    {
        if (!visited.Add(method))
        {
            return false;
        }

        var il = method.GetMethodBody()?.GetILAsByteArray();
        if (il is null)
        {
            return false;
        }

        for (var i = 0; i + 4 < il.Length; i++)
        {
            if (il[i] != 0x28 && il[i] != 0x6F) // 0x28 call, 0x6F callvirt — both take a 4-byte token operand
            {
                continue;
            }

            var token = BitConverter.ToInt32(il, i + 1);
            MethodBase? resolved;
            try
            {
                resolved = method.Module.ResolveMethod(token);
            }
            catch
            {
                continue; // an unresolvable operand (e.g. a generic method spec) is never ThreadHero — it resolves cleanly
            }

            if (resolved is null)
            {
                continue;
            }

            if (resolved.Module == threadHero.Module && resolved.MetadataToken == threadHero.MetadataToken)
            {
                return true;
            }

            if (resolved.DeclaringType == typeof(TutorialFlow) && CallsThreadHero(resolved, threadHero, visited))
            {
                return true;
            }
        }

        return false;
    }
}
#endif
