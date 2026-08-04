using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Professions;
using Xunit;

namespace GameSim.Tests.Crafting;

/// <summary>
/// Wave 5 (U23b): <see cref="ForgeScorer"/> — the blacksmith's pure in-sim puzzle scorer.
/// Covers the deviation-to-subscore rule, the forge-zone strike fold, the fixed 300/400/300
/// weight table, the <see cref="ForgeMoment"/> truth table, talent-assist forgiveness (mirroring
/// the exact <see cref="ProfessionRegistry.Blacksmith"/> assist semantics), and total-function
/// robustness (null/empty/odd-length traces never throw).
/// </summary>
public class ForgeScorerTests
{
    private static readonly ProfessionDefinition Blacksmith = ProfessionRegistry.Blacksmith;
    private static readonly ImmutableSortedSet<string> NoTalents = ImmutableSortedSet<string>.Empty;

    private static Recipe Dagger => RecipeTable.All["dagger"];       // tier 1, Weapon
    private static Recipe Buckler => RecipeTable.All["buckler"];     // tier 1, Shield

    /// <summary>Zero-tempo-error strikes at three forge-zone x's — full marks on the strike axis.</summary>
    private static ImmutableList<int> PerfectStrikes => ImmutableList.Create(400, 0, 500, 0, 600, 0);

    private static ForgeScore Score(Recipe recipe, ImmutableList<int> samples, ImmutableList<int> strikes, int pathSeed, ImmutableSortedSet<string>? talents = null) =>
        ForgeScorer.Score(recipe, new ForgeTraceInput(samples, strikes, pathSeed), talents ?? NoTalents, Blacksmith);

    // =====================================================================================
    // Perfect / worst / monotonicity
    // =====================================================================================

    [Fact]
    public void PerfectTrace_Scores1000AllAxes_WithCleanMoments()
    {
        var path = ForgePath.Generate(Dagger.Tier, Dagger.Slot, Dagger.BaseStats.Weight, pathSeed: 100);
        var score = Score(Dagger, path, PerfectStrikes, pathSeed: 100);

        Assert.Equal(1000, score.GradePermille);
        Assert.Equal(ImmutableList.Create(1000, 1000, 1000), score.SubScores);

        var expectedMoments = (int)(ForgeMoment.ForgedInOneHeat | ForgeMoment.NeverScorched | ForgeMoment.PerfectQuench);
        Assert.Equal(expectedMoments, score.Moments);
        Assert.Equal(0, score.Moments & (int)ForgeMoment.RecoveredFromTheBrink);
    }

    [Fact]
    public void NearPerfectTrace_QualifiesForSigning_AllThreeSubScoresAtLeast950()
    {
        // Every sample nudged +10 permille off the target — small enough to stay under the
        // ArtifactSigning.SubScoreThreshold gap (dev 10 * DevScale 4 = 40 -> subscore 960).
        var path = ForgePath.Generate(Dagger.Tier, Dagger.Slot, Dagger.BaseStats.Weight, pathSeed: 101);
        var vertexCount = path.Count / 2;
        var builder = path.ToBuilder();
        for (var i = 0; i < vertexCount; i++)
        {
            var yIndex = i * 2 + 1;
            builder[yIndex] = Math.Min(1000, builder[yIndex] + 10);
        }

        var score = Score(Dagger, builder.ToImmutable(), PerfectStrikes, pathSeed: 101);

        Assert.Equal(3, score.SubScores.Count);
        Assert.All(score.SubScores, s => Assert.True(s >= ArtifactSigning.SubScoreThreshold, $"subscore {s} below signing threshold"));
    }

    [Fact]
    public void WorstTrace_ScoresFloor_AllAxesZero()
    {
        var path = ForgePath.Generate(Dagger.Tier, Dagger.Slot, Dagger.BaseStats.Weight, pathSeed: 5);
        var vertexCount = path.Count / 2;
        var samples = ImmutableList.CreateBuilder<int>();
        for (var i = 0; i < vertexCount; i++)
        {
            var x = path[i * 2];
            var target = ForgePath.HeatAt(path, x);
            samples.Add(x);
            samples.Add(target >= 500 ? 0 : 1000); // maximally opposite the target everywhere
        }

        var worstStrikes = ImmutableList.Create(400, 1000, 500, 1000);
        var score = Score(Dagger, samples.ToImmutable(), worstStrikes, pathSeed: 5);

        Assert.Equal(0, score.GradePermille);
        Assert.All(score.SubScores, s => Assert.Equal(0, s));
    }

    [Fact]
    public void DeviationMonotonicity_StrictlyWorseTrace_NeverScoresHigher()
    {
        var path = ForgePath.Generate(Dagger.Tier, Dagger.Slot, Dagger.BaseStats.Weight, pathSeed: 200);
        var g0 = Score(Dagger, path, PerfectStrikes, pathSeed: 200).GradePermille;

        var climbY = path[3];
        var slightlyWorse = path.SetItem(3, Math.Min(1000, climbY + 50));
        var g1 = Score(Dagger, slightlyWorse, PerfectStrikes, pathSeed: 200).GradePermille;

        var muchWorse = path.SetItem(3, Math.Min(1000, climbY + 300));
        var g2 = Score(Dagger, muchWorse, PerfectStrikes, pathSeed: 200).GradePermille;

        Assert.True(g0 >= g1, $"{g0} should be >= {g1}");
        Assert.True(g1 >= g2, $"{g1} should be >= {g2}");
        Assert.True(g0 > g2, $"{g0} should be strictly greater than {g2}");
    }

    // =====================================================================================
    // Fold-weight pin (300 / 400 / 300)
    // =====================================================================================

    [Fact]
    public void FoldWeights_QuenchZeroed_PinsExpectedGrade()
    {
        var path = ForgePath.Generate(Dagger.Tier, Dagger.Slot, Dagger.BaseStats.Weight, pathSeed: 300);
        var samples = path.SetItem(path.Count - 3, 1000).SetItem(path.Count - 1, 1000); // trough + final y -> worst
        var score = Score(Dagger, samples, PerfectStrikes, pathSeed: 300);

        Assert.Equal(700, score.GradePermille); // (1000*300 + 1000*400 + 0*300) / 1000
        Assert.Equal(ImmutableList.Create(1000, 1000, 0), score.SubScores);
    }

    [Fact]
    public void FoldWeights_SmeltZeroed_PinsExpectedGrade()
    {
        var path = ForgePath.Generate(Dagger.Tier, Dagger.Slot, Dagger.BaseStats.Weight, pathSeed: 301);
        var samples = path.SetItem(1, 1000).SetItem(3, 0); // start + climb y -> worst
        var score = Score(Dagger, samples, PerfectStrikes, pathSeed: 301);

        Assert.Equal(700, score.GradePermille); // (0*300 + 1000*400 + 1000*300) / 1000
        Assert.Equal(0, score.SubScores[0]);
        Assert.Equal(1000, score.SubScores[2]);
    }

    [Fact]
    public void FoldWeights_ForgeZeroed_PinsExpectedGrade()
    {
        // Forge needs BOTH the sample axis and the strike axis at floor to hit an exact 0 —
        // proves the forge sub-score really is the (sample, strike) average, not sample-only.
        var path = ForgePath.Generate(Dagger.Tier, Dagger.Slot, Dagger.BaseStats.Weight, pathSeed: 302);
        var samplesBuilder = path.ToBuilder();
        for (var i = 0; i < samplesBuilder.Count; i += 2)
        {
            var x = samplesBuilder[i];
            if (x > ForgePath.SmeltZoneEnd && x <= ForgePath.ForgeZoneEnd)
            {
                samplesBuilder[i + 1] = 0; // forge-zone samples driven far from target
            }
        }

        var worstForgeStrikes = ImmutableList.Create(400, 1000, 500, 1000, 600, 1000);
        var score = Score(Dagger, samplesBuilder.ToImmutable(), worstForgeStrikes, pathSeed: 302);

        Assert.Equal(600, score.GradePermille); // (1000*300 + 0*400 + 1000*300) / 1000
        Assert.Equal(0, score.SubScores[1]);
    }

    // =====================================================================================
    // Moments truth table
    // =====================================================================================

    [Fact]
    public void ForgedInOneHeat_False_WhenHeatReClimbsMultipleTimes()
    {
        // Cools back down below the working-band entry threshold twice after first heating —
        // three separate rising edges, well inside the smelt/forge x range (<= 666).
        var reheating = ImmutableList.Create(
            0, 200,
            50, 700,
            100, 300,
            150, 700,
            200, 300,
            250, 700);

        var score = Score(Dagger, reheating, ImmutableList<int>.Empty, pathSeed: 9);
        Assert.Equal(0, score.Moments & (int)ForgeMoment.ForgedInOneHeat);
    }

    [Fact]
    public void NeverScorched_False_AndRecoveredFromTheBrink_True_WhenTraceSpikesButFinishesFine()
    {
        var path = ForgePath.Generate(Dagger.Tier, Dagger.Slot, Dagger.BaseStats.Weight, pathSeed: 21);
        var samples = path.Add(500).Add(950); // x=500 always lands in the fixed forge bucket [334,666]

        var score = Score(Dagger, samples, PerfectStrikes, pathSeed: 21);

        Assert.Equal(0, score.Moments & (int)ForgeMoment.NeverScorched);
        Assert.True(score.GradePermille >= 550, $"expected Fine-equivalent grade, got {score.GradePermille}");
        Assert.Equal((int)ForgeMoment.RecoveredFromTheBrink, score.Moments & (int)ForgeMoment.RecoveredFromTheBrink);
    }

    [Fact]
    public void PerfectQuench_False_WhenNoQuenchZoneSamplesExist()
    {
        var path = ForgePath.Generate(Dagger.Tier, Dagger.Slot, Dagger.BaseStats.Weight, pathSeed: 33);
        var withoutQuenchTail = path.GetRange(0, path.Count - 4); // drop the trough + final vertices

        var score = Score(Dagger, withoutQuenchTail, PerfectStrikes, pathSeed: 33);
        Assert.Equal(0, score.Moments & (int)ForgeMoment.PerfectQuench);
    }

    [Fact]
    public void PerfectQuench_False_WhenTailDeviatesFarFromTarget()
    {
        var path = ForgePath.Generate(Dagger.Tier, Dagger.Slot, Dagger.BaseStats.Weight, pathSeed: 34);
        var farFromTail = path.SetItem(path.Count - 1, 900); // final vertex driven far off target

        var score = Score(Dagger, farFromTail, PerfectStrikes, pathSeed: 34);
        Assert.Equal(0, score.Moments & (int)ForgeMoment.PerfectQuench);
    }

    // =====================================================================================
    // Talent assists (mirrors ProfessionRegistry.Blacksmith's MinigameAssist semantics)
    // =====================================================================================

    [Fact]
    public void KeenEye_WidensSmeltQuenchTolerance_ScoresAtLeastAsHigh()
    {
        var path = ForgePath.Generate(Dagger.Tier, Dagger.Slot, Dagger.BaseStats.Weight, pathSeed: 400);
        var climbY = path[3];
        var slightlyOff = path.SetItem(3, Math.Min(1000, climbY + 40));

        var withoutTalent = Score(Dagger, slightlyOff, PerfectStrikes, pathSeed: 400).GradePermille;
        var withKeenEye = Score(Dagger, slightlyOff, PerfectStrikes, pathSeed: 400, ImmutableSortedSet.Create(TalentTree.KeenEye)).GradePermille;

        Assert.True(withKeenEye >= withoutTalent, $"{withKeenEye} should be >= {withoutTalent}");
        Assert.True(withKeenEye > withoutTalent, "Keen Eye should measurably forgive a smelt-zone deviation");
    }

    [Fact]
    public void MasterTouch_ReducesDrift_AppliesAcrossAllZones()
    {
        var path = ForgePath.Generate(Dagger.Tier, Dagger.Slot, Dagger.BaseStats.Weight, pathSeed: 401);
        var forgeEndYIndex = path.Count - 5; // the forgeEnd vertex sits just before the quench pair
        var forgeEndY = path[forgeEndYIndex];
        var slightlyOff = path.SetItem(forgeEndYIndex, Math.Min(1000, forgeEndY + 40));

        var withoutTalent = Score(Dagger, slightlyOff, PerfectStrikes, pathSeed: 401).GradePermille;
        var withMasterTouch = Score(Dagger, slightlyOff, PerfectStrikes, pathSeed: 401, ImmutableSortedSet.Create(TalentTree.MasterTouch)).GradePermille;

        Assert.True(withMasterTouch > withoutTalent, $"{withMasterTouch} should be > {withoutTalent}");
    }

    [Fact]
    public void LegendaryCraft_ForgivesOffBeatForgeStrikes()
    {
        var path = ForgePath.Generate(Dagger.Tier, Dagger.Slot, Dagger.BaseStats.Weight, pathSeed: 402);
        var offBeatStrikes = ImmutableList.Create(400, 60, 500, 60, 600, 60);

        var withoutTalent = Score(Dagger, path, offBeatStrikes, pathSeed: 402).GradePermille;
        var withLegendaryCraft = Score(Dagger, path, offBeatStrikes, pathSeed: 402, ImmutableSortedSet.Create(TalentTree.LegendaryCraft)).GradePermille;

        Assert.True(withLegendaryCraft > withoutTalent, $"{withLegendaryCraft} should be > {withoutTalent}");
    }

    [Fact]
    public void WeaponSpecialist_OnlyAppliesToWeaponRecipes()
    {
        var path = ForgePath.Generate(Dagger.Tier, Dagger.Slot, Dagger.BaseStats.Weight, pathSeed: 403);
        var climbY = path[3];
        var slightlyOff = path.SetItem(3, Math.Min(1000, climbY + 40));

        var withoutTalent = Score(Dagger, slightlyOff, PerfectStrikes, pathSeed: 403).GradePermille;
        var withWeaponSpecialist = Score(Dagger, slightlyOff, PerfectStrikes, pathSeed: 403, ImmutableSortedSet.Create(TalentTree.WeaponSpecialist)).GradePermille;

        Assert.True(withWeaponSpecialist > withoutTalent, "Weapon Specialist should widen tolerance on a Weapon recipe");
    }

    [Fact]
    public void WeaponSpecialist_ContributesNothing_OnNonWeaponRecipe()
    {
        var path = ForgePath.Generate(Buckler.Tier, Buckler.Slot, Buckler.BaseStats.Weight, pathSeed: 404);
        var climbY = path[3];
        var slightlyOff = path.SetItem(3, Math.Min(1000, climbY + 40));

        var withoutTalent = Score(Buckler, slightlyOff, PerfectStrikes, pathSeed: 404).GradePermille;
        var withWeaponSpecialist = Score(Buckler, slightlyOff, PerfectStrikes, pathSeed: 404, ImmutableSortedSet.Create(TalentTree.WeaponSpecialist)).GradePermille;

        Assert.Equal(withoutTalent, withWeaponSpecialist);
    }

    [Fact]
    public void LockedTalents_ContributeNothing()
    {
        var path = ForgePath.Generate(Dagger.Tier, Dagger.Slot, Dagger.BaseStats.Weight, pathSeed: 405);
        var climbY = path[3];
        var slightlyOff = path.SetItem(3, Math.Min(1000, climbY + 40));

        Assert.Equal(
            Score(Dagger, slightlyOff, PerfectStrikes, pathSeed: 405).GradePermille,
            Score(Dagger, slightlyOff, PerfectStrikes, pathSeed: 405, NoTalents).GradePermille);
    }

    // =====================================================================================
    // Defensive / total-function robustness
    // =====================================================================================

    [Fact]
    public void NullSamplesAndStrikes_ScoreFloor_NeverThrow()
    {
        var trace = new ForgeTraceInput(null!, null!, PathSeed: 1);
        var score = ForgeScorer.Score(Dagger, trace, NoTalents, Blacksmith);

        Assert.Equal(0, score.GradePermille);
        Assert.Equal(3, score.SubScores.Count);
        Assert.All(score.SubScores, s => Assert.Equal(0, s));
    }

    [Fact]
    public void EmptySamplesAndStrikes_ScoreFloor_NeverThrow()
    {
        var trace = new ForgeTraceInput(ImmutableList<int>.Empty, ImmutableList<int>.Empty, PathSeed: 2);
        var score = ForgeScorer.Score(Dagger, trace, NoTalents, Blacksmith);

        Assert.Equal(0, score.GradePermille);
        Assert.All(score.SubScores, s => Assert.Equal(0, s));
    }

    [Fact]
    public void OddLengthSamplesAndStrikes_DefensivelyIgnoreTrailingInt_NeverThrow()
    {
        var oddSamples = ImmutableList.Create(0, 100, 500); // trailing 500 has no paired y
        var oddStrikes = ImmutableList.Create(400, 0, 500); // trailing 500 has no paired tempo error

        var trace = new ForgeTraceInput(oddSamples, oddStrikes, PathSeed: 3);
        var score = ForgeScorer.Score(Dagger, trace, NoTalents, Blacksmith);

        Assert.InRange(score.GradePermille, 0, 1000);
        Assert.Equal(3, score.SubScores.Count);
    }

    [Fact]
    public void SameTraceTwice_SameScore_PureFunction()
    {
        var path = ForgePath.Generate(Dagger.Tier, Dagger.Slot, Dagger.BaseStats.Weight, pathSeed: 500);
        var first = Score(Dagger, path, PerfectStrikes, pathSeed: 500);
        var second = Score(Dagger, path, PerfectStrikes, pathSeed: 500);

        // ForgeScore.SubScores is an ImmutableList<int>, which uses reference (not structural)
        // equality — compare fields individually rather than the whole record struct.
        Assert.Equal(first.GradePermille, second.GradePermille);
        Assert.Equal(first.Moments, second.Moments);
        Assert.Equal(first.SubScores, second.SubScores);
    }

    // =====================================================================================
    // U6 — strike-count decoupling (docs/plans/2026-08-04-001-feat-verify-by-playing-plan.md)
    //
    // Characterized before touching ForgeScorer.cs, using this same 40-seed setup (see the PR
    // body for the full before/after table):
    //   BEFORE (x-window-gated strikes): N=20 wins 37/40, N=40 wins 40/40, N=50 wins 40/40.
    //   AFTER  (every strike counted):   N=20 wins 40/40, N=40 wins 40/40, N=50 wins 40/40.
    // N=20 is where the old scorer's forgeStrikeCount shrinks to ~6 (only strikes whose x fell
    // in (333,666] survived), and that undersized sample let 3/40 seeds' sloppy players win on
    // luck alone. Scoring every strike removes the weak point without touching the strong ones.
    // =====================================================================================

    /// <summary>A player with a fixed skill LEVEL but bimodal per-strike outcomes (mostly close
    /// to the beat, occasionally a whiff) — more realistic than a tight uniform band, and it is
    /// exactly this heavy tail that made the old x-windowed sample noisy at low strike counts.
    /// x is spread across the FULL [0,1000] shape axis (not confined to the forge third) — under
    /// the fixed scorer x no longer matters at all; under the old scorer this is what shrank
    /// the counted sample as <paramref name="count"/> fell.</summary>
    private static ImmutableList<int> ScriptedStrikes(int seed, int count, int whiffPct, int floor, int range)
    {
        var rng = new Random(seed);
        var builder = ImmutableList.CreateBuilder<int>();
        for (var i = 0; i < count; i++)
        {
            var x = count > 1 ? i * 1000 / (count - 1) : 500;
            var tempoError = rng.Next(100) < whiffPct ? 999 : floor + rng.Next(range);
            builder.Add(x);
            builder.Add(tempoError);
        }

        return builder.ToImmutable();
    }

    private static ImmutableList<int> OnTempoStrikes(int seed, int count) =>
        ScriptedStrikes(seed, count, whiffPct: 25, floor: 0, range: 150);

    // A distinct RNG stream (seed transformed, not reused) so the sloppy draw is independent of
    // the on-tempo draw for the same seed index.
    private static ImmutableList<int> SloppyStrikes(int seed, int count) =>
        ScriptedStrikes(seed * 7919 + 1, count, whiffPct: 40, floor: 60, range: 220);

    private static int GradeFor(int seed, int count, bool onTempo)
    {
        var path = ForgePath.Generate(Dagger.Tier, Dagger.Slot, Dagger.BaseStats.Weight, pathSeed: seed);
        var strikes = onTempo ? OnTempoStrikes(seed, count) : SloppyStrikes(seed, count);
        return Score(Dagger, path, strikes, pathSeed: seed).GradePermille;
    }

    [Theory]
    [InlineData(20)] // shorter candidate length (U7 territory)
    [InlineData(40)] // roughly today's run length
    [InlineData(50)] // the length that measurably broke the old scorer's win rate (37/40)
    public void OnTempoBeatsSloppy_AcrossSeedSet_AtStrikeCount(int strikeCount)
    {
        const int SeedCount = 40;
        var wins = 0;
        for (var seed = 0; seed < SeedCount; seed++)
        {
            if (GradeFor(seed, strikeCount, onTempo: true) > GradeFor(seed, strikeCount, onTempo: false))
            {
                wins++;
            }
        }

        Assert.True(wins == SeedCount, $"on-tempo should beat sloppy on every seed at N={strikeCount}; won {wins}/{SeedCount}");
    }

    /// <summary>The core property U7 is blocked on: an identically-accurate player scores the
    /// same whether the craft asks for 20 strikes or 50. Uses a period-10 repeating tempo-error
    /// cycle — 10 divides both 20 and 50, so each count completes whole cycles and the exact
    /// same per-strike distribution is sampled either way; x is spread across the full run so a
    /// window-gated scorer would NOT be invariant here (it would only ever see a positional
    /// slice of the cycle, and that slice's size and phase both change with the count).</summary>
    [Fact]
    public void ScoreInvariantToStrikeCount_ForIdenticallyAccuratePlayer()
    {
        int[] cycle = { 10, 220, 40, 180, 30, 200, 20, 190, 5, 210 }; // period 10, mean 110.5
        static ImmutableList<int> CyclicStrikes(int count, int[] cycle)
        {
            var builder = ImmutableList.CreateBuilder<int>();
            for (var i = 0; i < count; i++)
            {
                var x = count > 1 ? i * 1000 / (count - 1) : 500;
                builder.Add(x);
                builder.Add(cycle[i % cycle.Length]);
            }

            return builder.ToImmutable();
        }

        var path = ForgePath.Generate(Dagger.Tier, Dagger.Slot, Dagger.BaseStats.Weight, pathSeed: 700);
        var score20 = Score(Dagger, path, CyclicStrikes(20, cycle), pathSeed: 700);
        var score50 = Score(Dagger, path, CyclicStrikes(50, cycle), pathSeed: 700);

        Assert.Equal(score20.GradePermille, score50.GradePermille);
        Assert.Equal(score20.SubScores, score50.SubScores);
    }

    /// <summary>Grade-boundary regression guard (QualityRoller.RollActive's active-model bands:
    /// &lt;200 Poor, &lt;550 Common, &lt;780 Fine, &lt;930 Superior, else Masterwork). Pins that
    /// the characterized on-tempo/sloppy runs still land in the buckets they landed in before
    /// this change — the fix must not quietly re-baseline anyone's craft outcome.</summary>
    [Theory]
    [InlineData(20)]
    [InlineData(40)]
    [InlineData(50)]
    public void GradeBoundaries_UnchangedBuckets_ForCharacterizedRuns(int strikeCount)
    {
        var onGrade = GradeFor(seed: 1, strikeCount, onTempo: true);
        var slopGrade = GradeFor(seed: 1, strikeCount, onTempo: false);

        Assert.True(onGrade is >= 780 and < 930, $"on-tempo grade {onGrade} should stay in the Superior band (perfect heat control caps out below Masterwork here)");
        Assert.True(slopGrade is >= 780 and < 930, $"sloppy grade {slopGrade} should stay in the Superior band (perfect heat control still carries it)");
    }
}
