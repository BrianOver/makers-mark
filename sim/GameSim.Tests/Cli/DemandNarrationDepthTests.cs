using System.Collections.Immutable;
using GameSim.Cli;
using GameSim.Contracts;
using GameSim.Drama;

namespace GameSim.Tests.Cli;

/// <summary>
/// Register #166 said the ledger printed <c>Brunhilde came back from floor 0</c>, and floor 0 does
/// not exist. That was fixed in <c>LedgerQuery</c>, and <see cref="DepthCopy"/> was extracted so
/// every surface turning a raw depth into prose reads the same answer.
///
/// <para><b>The CLI never got the memo.</b> <c>DemandNarration</c> interpolated
/// <c>DeepestFloorReached</c> straight into two sentences, and a hero who has not delved carries 0
/// — which is every hero on day 1. So the same fabricated floor was still reachable, in a sibling
/// surface, under a green suite. It is only the CLI, which this repo's own rule says is not the
/// deployed game; that is a reason to rank it low, not a reason to print a floor that does not
/// exist.</para>
///
/// <para>These tests drive <c>DemandNarration</c>'s real public entry points with a snapshot whose
/// stall sits at depth 0 — the exact day-1 shape — rather than asserting on a helper in isolation,
/// because the defect was never in the helper.</para>
/// </summary>
public class DemandNarrationDepthTests
{
    private static DemandSnapshot SnapshotWithStallAt(int deepestFloorReached) => new(
        PassReasons: ImmutableList<PassReasonRollup>.Empty,
        OpenCommissions: ImmutableList<OpenCommissionEntry>.Empty,
        DepthStalls: ImmutableList.Create(new DepthStallEntry(
            Hero: new HeroId(1),
            HeroName: "Brunhilde",
            DeepestFloorReached: deepestFloorReached,
            TargetFloor: 2,
            BlockingSlot: ItemSlot.Weapon)),
        BountyFloorMinimums: ImmutableList<BountyFloorMinimum>.Empty,
        OpenBounties: ImmutableList<OpenBountyEntry>.Empty);

    [Fact]
    public void DemandVerbLines_ForAHeroWhoHasNeverDelved_NeverNameFloorZero()
    {
        var lines = string.Join("\n", DemandNarration.DemandVerbLines(SnapshotWithStallAt(0)));

        Assert.DoesNotContain("floor 0", lines, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Brunhilde", lines);
        Assert.Contains(DepthCopy.Deepest(0), lines);
    }

    [Fact]
    public void DemandVerbLines_ForAHeroWhoHasDelved_StillNameTheRealFloor()
    {
        var lines = string.Join("\n", DemandNarration.DemandVerbLines(SnapshotWithStallAt(3)));

        Assert.Contains("floor 3", lines);
    }

    /// <summary>
    /// The muster line takes the same stall list through a different sentence
    /// (<c>StallSummary</c>), which is why it gets its own case: the first fix routed one of the two
    /// call sites and the other kept printing the raw int. Two surfaces, two assertions.
    /// </summary>
    [Fact]
    public void MusterLine_ForAHeroWhoHasNeverDelved_NeverNamesFloorZero()
    {
        var line = DemandNarration.MusterLine(
            ImmutableList<PartyPlan>.Empty,
            SnapshotWithStallAt(0),
            ImmutableSortedDictionary<int, Hero>.Empty);

        Assert.DoesNotContain("floor 0", line, System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The next-floor phrase is <c>DeepestFloorReached + 1</c> and is deliberately NOT routed
    /// through <see cref="DepthCopy"/>: a hero at depth 0 is genuinely being measured against floor
    /// 1, which exists. Pinned so a future pass "fixing" it does not turn a correct sentence into
    /// "not yet wants Fine+".
    /// </summary>
    [Fact]
    public void TheNextFloorPhrase_StaysALiteralFloorNumber_BecauseThatFloorIsReal()
    {
        var snapshot = new DemandSnapshot(
            PassReasons: ImmutableList<PassReasonRollup>.Empty,
            OpenCommissions: ImmutableList<OpenCommissionEntry>.Empty,
            DepthStalls: ImmutableList.Create(new DepthStallEntry(
                Hero: new HeroId(1),
                HeroName: "Brunhilde",
                DeepestFloorReached: 0,
                TargetFloor: 2,
                BlockingSlot: null,
                CarriedQuality: QualityGrade.Common,
                RequiredQuality: QualityGrade.Fine)),
            BountyFloorMinimums: ImmutableList<BountyFloorMinimum>.Empty,
            OpenBounties: ImmutableList<OpenBountyEntry>.Empty);

        var lines = string.Join("\n", DemandNarration.DemandVerbLines(snapshot));

        Assert.Contains("floor 1 wants", lines);
    }
}
