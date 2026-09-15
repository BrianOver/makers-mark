#if GDUNIT_TESTS
using System;
using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using GdUnit4;
using GodotClient.Ui;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// P2-MEMORY-01: pins <see cref="BeatVocab"/> as the ONE short-label vocabulary for a <see
/// cref="BeatType"/>, and makes a new beat type without a label a red build.
///
/// <para><b>Reflective, deny-by-default.</b> This sweeps <see cref="Enum.GetValues{TEnum}"/>
/// rather than a hand-listed array of members — a hand-listed array stops covering the family
/// the moment someone adds a member (this repo has shipped that exact bug before); iterating the
/// enum itself means a new <see cref="BeatType"/> with no matching arm in <see
/// cref="BeatVocab.Label"/> throws at the switch's runtime default the instant this test asks
/// for its label, failing the suite rather than silently rendering blank or raw.</para>
/// </summary>
[TestSuite]
public class BeatVocabTests
{
    [TestCase]
    public void EveryBeatType_HasALabel_NeverTheRawEnumSpelling()
    {
        foreach (var beat in Enum.GetValues<BeatType>())
        {
            string label;
            try
            {
                label = BeatVocab.Label(beat);
            }
            catch (Exception ex)
            {
                throw new Exception($"{beat} has no BeatVocab label (deny-by-default tripped): {ex.Message}", ex);
            }

            AssertThat(label).OverrideFailureMessage($"{beat} has an empty BeatVocab label").IsNotEmpty();
            AssertThat(label)
                .OverrideFailureMessage($"{beat} still renders its own raw enum spelling")
                .IsNotEqual(beat.ToString());
        }
    }

    // ── P2-PROOF-15: the rank. Same deny-by-default sweep as the label above, and — the part that
    // matters — the ORDER is pinned as a PROPERTY over the whole BeatType family, never as a
    // hand-listed pair of members. A guard that walks a literal array stops covering the family the
    // moment the family grows, and this repo has shipped that exact bug (128 assets, green suite).
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    private static AttributionBeatEvent Beat(BeatType type, int floor, int id = 1) =>
        new(type, new ItemId(500), new HeroId(1), floor, $"{type} on floor {floor}")
        { Id = new EventId(id), Day = 1 };

    [TestCase]
    public void EveryBeatType_HasARank_DenyByDefault()
    {
        foreach (var beat in Enum.GetValues<BeatType>())
        {
            int rank;
            try
            {
                rank = BeatVocab.Rank(beat);
            }
            catch (Exception ex)
            {
                throw new Exception($"{beat} has no BeatVocab rank (deny-by-default tripped): {ex.Message}", ex);
            }

            AssertThat(rank)
                .OverrideFailureMessage(
                    $"{beat} ranks {rank}, below the killing blow ({BeatVocab.KillingBlowRank}) — nothing may " +
                    "rank under the family floor, or a beatless card could outrank a beat-bearing one")
                .IsGreaterEqual(BeatVocab.KillingBlowRank);
        }
    }

    /// <summary>
    /// The structural claim P2-PROOF-15 exists to make, stated over the family rather than over a
    /// chosen pair: a <see cref="BeatType.KillingBlow"/> is the LEAST evidentiary thing the sim can
    /// prove — it fires on every player-crafted kill, decisive or not, and <c>TellingQuery</c> can
    /// give it no counterfactual second pass ("there the record ends") — so no beat type may ever
    /// rank below it, and a night must never open on one while anything else is on the card. A new
    /// <see cref="BeatType"/> ranked under a killing blow fails HERE, whoever adds it.
    /// </summary>
    [TestCase]
    public void KillingBlow_IsTheFamilyFloor_AndTheFamilyIsNotFlat()
    {
        var killingBlow = BeatVocab.Rank(BeatType.KillingBlow);
        AssertThat(killingBlow).IsEqual(BeatVocab.KillingBlowRank);

        foreach (var beat in Enum.GetValues<BeatType>())
        {
            AssertThat(BeatVocab.Rank(beat))
                .OverrideFailureMessage($"{beat} ranks below a killing blow, which is the family floor")
                .IsGreaterEqual(killingBlow);
        }

        AssertThat(Enum.GetValues<BeatType>().Any(beat => BeatVocab.Rank(beat) > killingBlow))
            .OverrideFailureMessage(
                "every beat type ranks the same, so the ordering does nothing at all and the night " +
                "still opens on whatever the resolver emitted first")
            .IsTrue();
    }

    /// <summary>
    /// The ordering itself, swept over every ORDERED pair in the family and both emission orders:
    /// whenever one beat type outranks another, <see cref="BeatVocab.LeadFirst"/> leads with it no
    /// matter which one the sim happened to emit first. This is what makes the sort a real
    /// comparison rather than a stable pass-through of the event log.
    /// </summary>
    [TestCase]
    public void LeadFirst_LeadsWithTheHigherRankedBeat_ForEveryOrderedPair_InEitherEmissionOrder()
    {
        var family = Enum.GetValues<BeatType>();
        foreach (var first in family)
        {
            foreach (var second in family)
            {
                if (BeatVocab.Rank(first) <= BeatVocab.Rank(second))
                {
                    continue;
                }

                // Same floor on both, so RANK is the only thing that can decide the lead.
                foreach (var emitted in new[]
                {
                    ImmutableList.Create(Beat(first, 2, 1), Beat(second, 2, 2)),
                    ImmutableList.Create(Beat(second, 2, 1), Beat(first, 2, 2)),
                })
                {
                    AssertThat(BeatVocab.LeadFirst(emitted)[0].Beat)
                        .OverrideFailureMessage(
                            $"{first} (rank {BeatVocab.Rank(first)}) did not lead {second} " +
                            $"(rank {BeatVocab.Rank(second)}) when the sim emitted " +
                            $"{string.Join(", ", emitted.Select(b => b.Beat))}")
                        .IsEqual(first);
                }
            }
        }
    }

    /// <summary>Depth is the tiebreak, swept over the whole family: two beats of the SAME type lead
    /// with the deeper floor whichever order they were emitted in. (This is also what subsumes the
    /// "bottom floor of the venue" sub-tier an earlier draft wanted — see
    /// <see cref="BeatVocab.LeadFirst"/>'s own note.)</summary>
    [TestCase]
    public void LeadFirst_OnEqualRank_LeadsWithTheDeeperFloor_InEitherEmissionOrder()
    {
        foreach (var type in Enum.GetValues<BeatType>())
        {
            foreach (var emitted in new[]
            {
                ImmutableList.Create(Beat(type, 1, 1), Beat(type, 5, 2)),
                ImmutableList.Create(Beat(type, 5, 1), Beat(type, 1, 2)),
            })
            {
                AssertThat(BeatVocab.LeadFirst(emitted)[0].Floor)
                    .OverrideFailureMessage($"{type} did not lead with the deeper floor")
                    .IsEqual(5);
            }
        }
    }

    /// <summary>
    /// LAW 4 (show only what the sim decided), pinned against this unit's own temptation: reordering
    /// the beats may not DROP, MERGE, or SUMMARISE any of them. Every beat the sim emitted comes back
    /// out exactly once, with its <c>Detail</c> byte-identical — the ledger's beat rows are a
    /// permutation of the event log and nothing else.
    /// </summary>
    [TestCase]
    public void LeadFirst_IsAPermutation_DroppingMergingAndSummarisingNothing()
    {
        var emitted = Enum.GetValues<BeatType>()
            .SelectMany((type, i) => new[] { Beat(type, 1, (i * 2) + 1), Beat(type, 4, (i * 2) + 2) })
            .ToImmutableList();

        var ordered = BeatVocab.LeadFirst(emitted);

        AssertThat(ordered.Count).IsEqual(emitted.Count);
        foreach (var beat in emitted)
        {
            AssertThat(ordered.Count(o => o.Id == beat.Id && o.Detail == beat.Detail))
                .OverrideFailureMessage($"{beat.Beat} on floor {beat.Floor} was dropped or duplicated by the reorder")
                .IsEqual(1);
        }
    }

    /// <summary>A stable sort, so a night that ties all the way down still reads in the order the
    /// sim decided — the ledger never invents an order the record does not have.</summary>
    [TestCase]
    public void LeadFirst_OnAFullTie_KeepsTheSimsOwnEmissionOrder()
    {
        var emitted = ImmutableList.Create(
            Beat(BeatType.KillingBlow, 2, 7),
            Beat(BeatType.KillingBlow, 2, 8),
            Beat(BeatType.KillingBlow, 2, 9));

        AssertThat(BeatVocab.LeadFirst(emitted).Select(b => b.Id.Value).ToArray())
            .IsEqual(new[] { 7, 8, 9 });
    }
}
#endif
