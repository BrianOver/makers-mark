#if GDUNIT_TESTS
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using GameSim;
using GameSim.Contracts;
using GameSim.Kernel;
using GameSim.Professions;
using GdUnit4;
using Godot;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U3 (loop-legibility plan, KTD-B): the three bell-riders (<see cref="UpgradeForgeAction"/>,
/// <see cref="SetProfessionsAction"/>, <see cref="CommissionLegendaryWorkAction"/>) — and any
/// future one <see cref="ActionTiming"/> defers — are never silent: submit raises an instant
/// acknowledgment toast naming the bell (<see cref="PendingVerbVocab.BellPromise"/>), a chip
/// lands on the bell tray (<c>MainUi._bellTray</c>, rendered straight off
/// <see cref="SimAdapter.PendingActions"/>), and the chip's withdraw control pulls the action
/// off the queue before it ever reaches the kernel (<see cref="SimAdapter.Withdraw"/>).
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class BellTrayTests
{
    private static HBoxContainer Tray(MainUi ui) => Find<HBoxContainer>(ui, "BellTray");
    private static Label RejectionToast(MainUi ui) => Find<Label>(ui, "RejectionToast");

    /// <summary>
    /// One instance-factory per concrete <see cref="PlayerAction"/> type — mirrors
    /// <c>ActionTimingConformanceTests.ExpectedLane</c>'s idiom exactly (sim, U1): reflection
    /// finds the SET of types, a hand-written factory per type builds an instance to probe with
    /// (the field values are arbitrary — only the TYPE and <see cref="ActionTiming.ResolvesImmediately"/>'s
    /// verdict on it matter here). Duplicated rather than shared because that dictionary is
    /// private to a different assembly (<c>sim/GameSim.Tests</c>), and this unit's sim diff must
    /// stay at zero (KTD-F) — this file may not touch <c>sim/</c> at all.
    /// </summary>
    private static readonly Dictionary<Type, Func<PlayerAction>> Factory = new()
    {
        [typeof(BuyMaterialAction)] = () => new BuyMaterialAction("copper", 1),
        [typeof(BuyOreAction)] = () => new BuyOreAction(new HeroId(1), "copper", 1),
        [typeof(BuyForgeSupplyAction)] = () => new BuyForgeSupplyAction("coal", 1),
        [typeof(CraftAction)] = () => new CraftAction("dagger", "copper"),
        [typeof(ReforgeHeirloomAction)] = () => new ReforgeHeirloomAction(new ItemId(1), "dagger", "copper"),
        [typeof(MasterworkAttemptAction)] = () => new MasterworkAttemptAction("dagger", "copper"),
        [typeof(StockAction)] = () => new StockAction(new ItemId(1), 10),
        [typeof(UnstockAction)] = () => new UnstockAction(new ItemId(1)),
        [typeof(SetPriceAction)] = () => new SetPriceAction(new ItemId(1), 10),
        [typeof(OpenCounterAction)] = () => new OpenCounterAction(),
        [typeof(PresentItemAction)] = () => new PresentItemAction(new ItemId(1)),
        [typeof(SuggestItemAction)] = () => new SuggestItemAction(new ItemId(1)),
        [typeof(HaggleResponseAction)] = () => new HaggleResponseAction(HaggleResponseKind.Accept),
        [typeof(CloseCounterAction)] = () => new CloseCounterAction(),
        [typeof(AcceptCommissionAction)] = () => new AcceptCommissionAction(new HeroId(1)),
        [typeof(DeclineCommissionAction)] = () => new DeclineCommissionAction(new HeroId(1)),
        [typeof(PostBountyAction)] = () => new PostBountyAction(1, 25),
        [typeof(SendSupplyAction)] = () => new SendSupplyAction(new HeroId(1), new ItemId(1)),
        [typeof(RecallPartyAction)] = () => new RecallPartyAction(new HeroId(1)),
        [typeof(UnlockTalentAction)] = () => new UnlockTalentAction("node", "blacksmith"),
        [typeof(HonorMemorialAction)] = () => new HonorMemorialAction(new HeroId(1)),
        [typeof(UpgradeForgeAction)] = () => new UpgradeForgeAction(),
        [typeof(SetProfessionsAction)] = () => new SetProfessionsAction(ImmutableSortedSet.Create("blacksmith")),
        [typeof(CommissionLegendaryWorkAction)] = () => new CommissionLegendaryWorkAction("dagger", "copper"),
    };

    private static IEnumerable<Type> ConcretePlayerActionTypesInAssembly() =>
        typeof(PlayerAction).Assembly.GetTypes()
            .Where(t => typeof(PlayerAction).IsAssignableFrom(t) && !t.IsAbstract && t.IsClass);

    /// <summary>The set <see cref="PendingVerbVocab"/> must cover, DERIVED — never hand-listed —
    /// from reflection over every concrete <see cref="PlayerAction"/> type plus
    /// <see cref="ActionTiming.ResolvesImmediately"/>'s own verdict on each (the same source U1's
    /// <c>ActionTimingConformanceTests</c> reflects over). A fourth bell-rider added to
    /// <see cref="ActionTiming"/> later shows up here automatically; only
    /// <see cref="PendingVerbVocab"/> itself would need updating, and this test is what tells you so.</summary>
    private static IReadOnlyList<Type> DeferredTypes()
    {
        var actual = ConcretePlayerActionTypesInAssembly().ToImmutableHashSet();
        var known = Factory.Keys.ToImmutableHashSet();

        var unfactoried = actual.Except(known);
        AssertThat(unfactoried.Count)
            .OverrideFailureMessage($"New PlayerAction type(s) with no Factory entry in BellTrayTests: {string.Join(", ", unfactoried.Select(t => t.Name))}")
            .IsEqual(0);

        return actual.Where(t => !ActionTiming.ResolvesImmediately(Factory[t]())).ToList();
    }

    [TestCase]
    public void DeferredSet_IsExactlyTheThreeBellRiders_AndPendingVerbVocabCoversAllOfIt()
    {
        var deferred = DeferredTypes();
        var expected = new HashSet<Type>
        {
            typeof(UpgradeForgeAction), typeof(SetProfessionsAction), typeof(CommissionLegendaryWorkAction),
        };

        AssertThat(deferred.Count).IsEqual(3);
        AssertThat(deferred.ToHashSet().SetEquals(expected))
            .OverrideFailureMessage($"Deferred set was [{string.Join(", ", deferred.Select(t => t.Name))}], expected exactly the three bell-riders.")
            .IsTrue();

        // The vocab conformance half: every deferred type must render a real display name and a
        // real bell-promise — PendingVerbVocab throws (never blank-fallback) for anything it
        // doesn't cover, so this loop fails BY NAME the moment a new bell-rider ships unnamed.
        foreach (var type in deferred)
        {
            var instance = Factory[type]();
            var displayName = PendingVerbVocab.DisplayName(instance);
            var bellPromise = PendingVerbVocab.BellPromise(instance);
            AssertThat(string.IsNullOrWhiteSpace(displayName))
                .OverrideFailureMessage($"{type.Name}: PendingVerbVocab.DisplayName must not be blank.")
                .IsFalse();
            AssertThat(string.IsNullOrWhiteSpace(bellPromise))
                .OverrideFailureMessage($"{type.Name}: PendingVerbVocab.BellPromise must not be blank.")
                .IsFalse();
        }
    }

    /// <summary>An immediate verb handed to <see cref="PendingVerbVocab"/> is a programming error
    /// (it should never reach the tray at all) — proves the table is deny-by-default, the same
    /// shape <see cref="ActionTiming"/> itself uses, rather than silently accepting every type.</summary>
    [TestCase]
    public void PendingVerbVocab_ThrowsForAnImmediateAction_RatherThanRenderingSomethingBlank()
    {
        var immediateInstance = Factory[typeof(StockAction)]();
        AssertThat(ActionTiming.ResolvesImmediately(immediateInstance)).IsTrue();

        var threw = false;
        try
        {
            PendingVerbVocab.DisplayName(immediateInstance);
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }

        AssertThat(threw)
            .OverrideFailureMessage("PendingVerbVocab.DisplayName must throw (never render blank/silent) for a type it doesn't cover.")
            .IsTrue();
    }

    [TestCase]
    public void Withdraw_MakesTheActionNeverReachTheKernel_DeterminismFree()
    {
        const ulong seed = 20260802UL;

        // Control: the tick with nothing extra ever queued.
        var control = new SimAdapter(seed);
        control.AdvancePhase();
        var controlState = SaveCodec.Serialize(control.CurrentState);

        // Pick a profession the fresh campaign did NOT start with, so applying the action would
        // provably change something (see the positive control below).
        var starting = new SimAdapter(seed).CurrentState.Player.SelectedProfessions;
        var alternate = ProfessionRegistry.All.Keys.First(id => !starting.Contains(id));
        var action = new SetProfessionsAction(ImmutableSortedSet.Create(alternate));

        // Withdrawn before the tick.
        var withdrawnRun = new SimAdapter(seed);
        withdrawnRun.Queue(action);
        AssertThat(withdrawnRun.PendingActions.Count).IsEqual(1);
        var removed = withdrawnRun.Withdraw(action);
        AssertThat(removed).IsTrue();
        AssertThat(withdrawnRun.PendingActions.Count).IsEqual(0);
        withdrawnRun.AdvancePhase();
        var withdrawnState = SaveCodec.Serialize(withdrawnRun.CurrentState);

        // Determinism-free proof: a withdrawn action never reached the kernel, so the tick behaves
        // exactly as if it had never been submitted — byte-identical to the control.
        AssertThat(withdrawnState)
            .OverrideFailureMessage("A withdrawn action must be byte-identical to never having queued it at all.")
            .IsEqual(controlState);

        // A second withdraw of the same (now-gone) action must say so, never silently no-op.
        AssertThat(withdrawnRun.Withdraw(action)).IsFalse();

        // Positive control: the SAME action, submitted and NOT withdrawn, DOES change the state —
        // proving the withdrawal above suppressed something real, not a no-op that changes nothing
        // either way.
        var appliedRun = new SimAdapter(seed);
        appliedRun.Queue(action);
        appliedRun.AdvancePhase();
        AssertThat(appliedRun.CurrentState.Player.SelectedProfessions)
            .OverrideFailureMessage("The non-withdrawn action should have changed SelectedProfessions — otherwise this proves nothing.")
            .IsEqual(ImmutableSortedSet.Create(alternate));
    }

    [TestCase]
    public void SubmittingADeferredVerb_AddsATrayChip_AndShowsTheAckToast_WithNoManualRefresh()
    {
        var ui = MountMainUi();
        try
        {
            AssertThat(Tray(ui).GetChildCount()).IsEqual(0);

            ui.Adapter.Queue(new UpgradeForgeAction()); // no ui.RefreshAll() call — proves the event wiring alone answers this

            AssertThat(Tray(ui).GetChildCount()).IsEqual(1);
            var chip = Tray(ui).GetChild(0);
            AssertThat(Find<Label>(chip, "Verb").Text).IsEqual(PendingVerbVocab.DisplayName(new UpgradeForgeAction()));

            AssertThat(ui.ToastRemaining > 0).IsTrue();
            AssertThat(RejectionToast(ui).Text).IsEqual(PendingVerbVocab.BellPromise(new UpgradeForgeAction()));
        }
        finally { Unmount(ui); }
    }

    /// <summary>Every deferred type's ack toast text comes from <see cref="PendingVerbVocab.BellPromise"/>
    /// exactly — enumerated from <see cref="DeferredTypes"/>, not a hand list.</summary>
    [TestCase]
    public void AckToast_MatchesPendingVerbVocab_ForEveryDeferredType()
    {
        foreach (var type in DeferredTypes())
        {
            var ui = MountMainUi();
            try
            {
                var action = Factory[type]();
                ui.Adapter.Queue(action);
                AssertThat(RejectionToast(ui).Text)
                    .OverrideFailureMessage($"{type.Name}'s ack toast did not match PendingVerbVocab.BellPromise.")
                    .IsEqual(PendingVerbVocab.BellPromise(action));
            }
            finally { Unmount(ui); }
        }
    }

    [TestCase]
    public void WithdrawButton_RemovesTheChip_AndThePendingEntry()
    {
        var ui = MountMainUi();
        try
        {
            ui.Adapter.Queue(new UpgradeForgeAction());
            AssertThat(Tray(ui).GetChildCount()).IsEqual(1);
            AssertThat(ui.Adapter.PendingActions.Count).IsEqual(1);

            Press(Tray(ui).GetChild(0), "Withdraw");

            AssertThat(Tray(ui).GetChildCount()).IsEqual(0);
            AssertThat(ui.Adapter.PendingActions.Count).IsEqual(0);
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void TheTick_ClearsTheTray()
    {
        var ui = MountMainUi();
        try
        {
            ui.Adapter.Queue(new UpgradeForgeAction());
            AssertThat(Tray(ui).GetChildCount()).IsEqual(1);

            PressEnabled(ui, "AdvancePhase"); // rings the bell — the tick consumes _pending either way

            AssertThat(Tray(ui).GetChildCount()).IsEqual(0);
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void ImmediateVerbs_NeverTouchTheTray()
    {
        var ui = MountMainUi();
        try
        {
            AssertThat(ActionTiming.ResolvesImmediately(new BuyMaterialAction("copper", 1))).IsTrue();

            ui.Adapter.Queue(new BuyMaterialAction("copper", 1)); // Morning-legal workshop buy

            AssertThat(Tray(ui).GetChildCount()).IsEqual(0);
        }
        finally { Unmount(ui); }
    }
}
#endif
