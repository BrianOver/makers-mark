#if GDUNIT_TESTS
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using GameSim;
using GameSim.Contracts;
using GameSim.Kernel;
using GdUnit4;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// Adapter fidelity (KTD2): the SimAdapter is pure delegation — a scripted session
/// through Queue/AdvancePhase must produce the byte-identical world that the same
/// batches produce on a raw <see cref="GameComposition"/> kernel. Pure C#, no
/// Godot runtime needed.
/// </summary>
[TestSuite]
public class SimAdapterTests
{
    [TestCase]
    public void ScriptedThreeDaySession_MatchesDirectKernel()
    {
        // Through the adapter: 3 full days, actions chosen from live state. Loop-until-Morning
        // is day-length agnostic — 9 ticks today, 15 after the 5-phase kernel (staged-plan
        // U2). Camp/Deep ticks yield empty batches (ScriptedSession is append-tolerant), so
        // the same batch list drives the raw kernel below identically.
        var adapter = new SimAdapter(ScriptedSession.Seed);
        var batches = new List<ImmutableList<PlayerAction>>();
        for (var day = 0; day < 3; day++)
        {
            var ticks = 0;
            do
            {
                var batch = ScriptedSession.ChooseActions(adapter.CurrentState);
                batches.Add(batch);
                foreach (var action in batch)
                {
                    adapter.Queue(action);
                }

                var result = adapter.AdvancePhase();
                AssertThat(result.Rejected.Count).IsEqual(0);
                if (++ticks > UiTestSupport.MaxPhasesPerDay)
                {
                    throw new InvalidOperationException("day exceeded max phases without returning to Morning");
                }
            }
            while (adapter.CurrentState.Phase != DayPhase.Morning);
        }

        // The same batches applied directly to a GameComposition kernel.
        var kernel = GameComposition.BuildKernel();
        var direct = GameComposition.NewCampaign(ScriptedSession.Seed);
        foreach (var batch in batches)
        {
            direct = kernel.Tick(direct, batch).NewState;
        }

        AssertThat(SaveCodec.Serialize(adapter.CurrentState)).IsEqual(SaveCodec.Serialize(direct));
        AssertThat(adapter.CurrentState.Day).IsEqual(4);
        AssertThat(adapter.CurrentState.Phase).IsEqual(DayPhase.Morning);
    }

    [TestCase]
    public void AdvancePhase_ExposesTickOutcome_AndRaisesStateChanged()
    {
        var adapter = new SimAdapter(ScriptedSession.Seed);
        var observed = new List<(DayPhase Phase, int Day)>();
        adapter.StateChanged += (phase, day) => observed.Add((phase, day));

        // An action no handler accepts during Expedition surfaces as a typed rejection.
        adapter.AdvancePhase(); // day 1 Morning
        adapter.Queue(new BuyOreAction(new HeroId(1), "copper", 1));
        adapter.AdvancePhase(); // day 1 Expedition — BuyOre is Evening-only

        AssertThat(observed.Count).IsEqual(2);
        AssertThat(observed[0]).IsEqual((DayPhase.Morning, 1));
        AssertThat(observed[1]).IsEqual((DayPhase.Expedition, 1));
        AssertThat(adapter.LastRejections.Count).IsEqual(1);
        AssertThat(adapter.LastRejections[0].Reason).Contains("Expedition");
        AssertThat(adapter.PendingActions.Count).IsEqual(0); // queue consumed either way
        AssertThat(adapter.LastEvents.Count > 0).IsTrue();   // expedition departure happened

        // Advanced OFF Expedition, still day 1 (Evening today; Camp once the 5-phase kernel
        // lands, staged-plan U2). The point is the tick advanced and the Evening-only BuyOre
        // was rejected mid-expedition — not the exact post-Expedition phase (kernel contract,
        // pinned by PhaseMachineTests in the sim lane).
        AssertThat(adapter.CurrentState.Day).IsEqual(1);
        AssertThat(adapter.CurrentState.Phase).IsNotEqual(DayPhase.Expedition);
        AssertThat(adapter.CurrentState.Phase).IsNotEqual(DayPhase.Morning);
    }
}
#endif
