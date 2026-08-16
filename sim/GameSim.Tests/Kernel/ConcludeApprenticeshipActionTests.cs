using System.Collections.Immutable;
using GameSim;
using GameSim.Advisor;
using GameSim.Contracts;
using GameSim.Kernel;

namespace GameSim.Tests.Kernel;

/// <summary>
/// §11.13 amendment (U4a, R12 ruled yes): <see cref="ConcludeApprenticeshipAction"/>'s own three
/// unit-brief scenarios (docs/design/MAKERS-MARK.md's U4a "Test scenarios" list) — legality/timing/
/// idempotence, kept in one small file deliberately DECOUPLED from
/// <c>GameSim.Expedition.ApprenticeWarrant</c> (a U4 type) so this file builds and passes on the
/// U4a diff alone, before U4 lands. The fourth scenario (the reachability census entry) lives in
/// <c>godot/tests/ActionReachabilityCensusTests.cs</c>, not here.
/// </summary>
public class ConcludeApprenticeshipActionTests
{
    private const ulong Seed = 1;

    [Fact]
    public void ConcludeApprenticeship_SpendsNoActionSlot()
    {
        Assert.False(ActionBudget.ConsumesSlot(new ConcludeApprenticeshipAction()));

        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(Seed);
        var before = state.ActionSlotsRemaining;

        var result = kernel.ApplyNow(state, new ConcludeApprenticeshipAction());

        Assert.True(result.Rejected.IsEmpty);
        Assert.Equal(before, result.NewState.ActionSlotsRemaining);
    }

    [Fact]
    public void ConcludeApprenticeship_IsIdempotent_SecondSubmitChangesNothing()
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(Seed);

        var once = kernel.ApplyNow(state, new ConcludeApprenticeshipAction());
        Assert.True(once.Rejected.IsEmpty);

        var twice = kernel.ApplyNow(once.NewState, new ConcludeApprenticeshipAction());
        Assert.True(twice.Rejected.IsEmpty);

        // Nothing but the two ActionLog entries themselves differs — every other field (Player,
        // Heroes, Items, Day, Phase, Rng, ...) is untouched, since the handler mutates nothing.
        var beforeLog = once.NewState with { ActionLog = ImmutableList<LoggedBatch>.Empty };
        var afterLog = twice.NewState with { ActionLog = ImmutableList<LoggedBatch>.Empty };
        Assert.Equal(beforeLog, afterLog);
        Assert.Equal(2, twice.NewState.ActionLog.Sum(b => b.Actions.Count) - state.ActionLog.Sum(b => b.Actions.Count));
    }

    /// <summary>
    /// "After LastGraceDay" is illustrative here (day 10 — well past any plausible 3-4 day warrant
    /// window) rather than a reference to <c>ApprenticeWarrant.LastGraceDay</c>, deliberately: the
    /// action's OWN legality never reads the calendar at all (see
    /// <c>ActionLegality.ConcludeApprenticeshipLegal</c>'s doc) — a late submit is a legal no-op
    /// because the WARRANT has nothing left to end, not because this predicate blocks it. That
    /// stays true whatever day it is, so this test does not need U4's type to exist.
    /// </summary>
    [Fact]
    public void ConcludeApprenticeship_AfterLastGraceDay_IsALegalNoOp()
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(Seed) with { Day = 10 };

        var action = new ConcludeApprenticeshipAction();
        Assert.True(ActionLegality.IsLegal(state, action, state.Phase));

        var result = kernel.ApplyNow(state, action);
        Assert.True(result.Rejected.IsEmpty);
        Assert.Equal(state.Player, result.NewState.Player);
        Assert.Equal(state.Heroes, result.NewState.Heroes);
    }

    [Fact]
    public void ConcludeApprenticeship_LegalInEveryPhase()
    {
        var state = GameComposition.NewCampaign(Seed);
        foreach (var phase in Enum.GetValues<DayPhase>())
        {
            Assert.True(ActionLegality.IsLegal(state, new ConcludeApprenticeshipAction(), phase));
        }
    }
}
