using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Harness;
using GameSim.Kernel;
using GameSim.Professions;

namespace GameSim.Tests.Crafting;

/// <summary>
/// P2-OQ11 (owner ruling 2026-09-04, <c>MAKERS-MARK.md</c> §11.7.12): the ONE quality curve, pinned
/// as a contract over all FOUR professions rather than as four separate tables. The ruling's target
/// is "top grade earned by accuracy, reachable in every craft, automatic in none", and every
/// paragraph of it is a test here — deliberately written as loops over the professions, so that a
/// fifth craft or a retuned scorer cannot quietly opt out of the contract the way the first four
/// each drifted into their own curve.
///
/// <para><b>Why this file compares the crafts and never equates them.</b> <c>THE-GAME.md</c> §4.1
/// requires the professions to feel different in the hands — the forge kinetic, the brew and the
/// hide unhurried. So nothing here asserts that two crafts produce the same grade for "the same
/// input": there is no such thing as the same input across a heat trace, a pour order, a scraped
/// hide and a socket assembly. What is asserted is that the same DESCRIBED hand — indifferent,
/// skilled, flawless, as defined once in <see cref="CraftHand"/> — lands in the same band whichever
/// craft made it. That is the coherence the ruling asked for, and it is the most this file may
/// assert without destroying the thing the ruling explicitly protects.</para>
/// </summary>
public class CraftCurveTests
{
    private static readonly ImmutableSortedSet<string> NoTalents = ImmutableSortedSet<string>.Empty;

    /// <summary>
    /// <see cref="QualityRoller.RollActive"/>'s documented band table, restated here because the
    /// roller's own <c>BandFor</c> is private. <see cref="TestBandTable_AgreesWithTheRealRoller"/>
    /// proves this copy is not allowed to drift from the real one — without that test this helper
    /// would be exactly the kind of hand-maintained mirror that goes stale and then lies (rule 8).
    /// </summary>
    private static QualityGrade Band(int permille) => permille switch
    {
        < 200 => QualityGrade.Poor,
        < 550 => QualityGrade.Common,
        < 780 => QualityGrade.Fine,
        < 930 => QualityGrade.Superior,
        _ => QualityGrade.Masterwork,
    };

    // ================================================================================
    // The curve itself
    // ================================================================================

    [Fact]
    public void TheCurve_AnchorsTheIndifferentHandToMidCommon_AndFlawlessToMasterwork()
    {
        Assert.Equal(QualityGrade.Common, Band(CraftCurve.IndifferentAnchorPermille));

        // 100 per-mille clear of the Fine seam, so RollActive's +/-25 jitter can never lift an
        // indifferent craft out of Common. "Automatic in none" is arithmetic, not luck.
        Assert.Equal(QualityGrade.Common, Band(CraftCurve.IndifferentAnchorPermille + 25));
        Assert.Equal(QualityGrade.Common, Band(CraftCurve.IndifferentAnchorPermille - 25));

        Assert.Equal(CraftCurve.IndifferentAnchorPermille, CraftCurve.GradeFor(5, 5, 10));
        Assert.Equal(1000, CraftCurve.GradeFor(10, 5, 10));
        Assert.Equal(QualityGrade.Masterwork, Band(CraftCurve.GradeFor(10, 5, 10)));
    }

    [Fact]
    public void TheCurve_IsStrictlyIncreasing_AcrossEveryCalibrationThisRepoUses()
    {
        // Alchemy/Engineering: indifferent = n, flawless = 2n, for n in 3..5 (pours or sockets).
        // Tanning: 75 of 80. The property must hold point by point, with no ties anywhere.
        var calibrations = new List<(int Indifferent, int Flawless)>
        {
            (3, 6), (4, 8), (5, 10), (75, 80),
        };

        foreach (var (indifferent, flawless) in calibrations)
        {
            var previous = int.MinValue;
            for (var points = 0; points <= flawless; points++)
            {
                var grade = CraftCurve.GradeFor(points, indifferent, flawless);
                Assert.True(
                    grade > previous,
                    $"calibration {indifferent}/{flawless}: {points} pts graded {grade}, "
                    + $"not strictly above the {points - 1}-pt grade {previous}");
                previous = grade;
            }
        }
    }

    [Fact]
    public void TheCurve_IsTotal_ForHostileInput()
    {
        // Pure and total is a contract, not a hope (KTD2): no input throws, everything lands in range.
        int[] points = [int.MinValue, -7, 0, 1, 5, 10, 4_000, int.MaxValue];
        int[] indifferents = [int.MinValue, -1, 0, 1, 5, 10, 99, int.MaxValue];
        int[] flawlesses = [int.MinValue, -1, 0, 1, 6, 80, int.MaxValue];

        foreach (var p in points)
        {
            foreach (var i in indifferents)
            {
                foreach (var f in flawlesses)
                {
                    var grade = CraftCurve.GradeFor(p, i, f);
                    Assert.InRange(grade, 0, 1000);
                }
            }
        }
    }

    [Fact]
    public void TestBandTable_AgreesWithTheRealRoller()
    {
        // Roll100 = 49 maps to jitter 0 ((49 * 51 / 100) - 25 == 24 - 25 == -1; 50 gives 0), so
        // pick the roll that actually zeroes the jitter and compare bands straight across.
        // Material is deliberately two grades above tier so no ceiling can clip the top band.
        var recipe = ProfessionRegistry.Blacksmith.Recipes.Values.First(r => r.Tier == 1);

        for (var permille = 0; permille <= 1000; permille += 5)
        {
            var rolled = QualityRoller.RollActive(
                recipe, materialGrade: recipe.Tier + 2, NoTalents, ProfessionRegistry.Blacksmith.Quality,
                new ZeroJitter(), performanceGrade: permille);
            Assert.Equal(Band(permille), rolled);
        }
    }

    // ================================================================================
    // One coherent curve: the same described hand lands in the same band in all four crafts
    // ================================================================================

    [Fact]
    public void IndifferentHand_GradesCommon_InAllFourCrafts()
    {
        foreach (var (name, grade) in AllCraftsAt(CraftHand.Indifferent, NoTalents))
        {
            Assert.Equal(QualityGrade.Common, Band(grade));
            Assert.True(grade > 0, $"{name}: an indifferent hand still tried — it must not score 0");
        }
    }

    [Fact]
    public void FlawlessHand_EarnsMasterwork_InAllFourCrafts()
    {
        foreach (var (name, grade) in AllCraftsFlawless(NoTalents))
        {
            Assert.True(grade == 1000, $"{name}: a flawless craft graded {grade}, not the full 1000");
            Assert.Equal(QualityGrade.Masterwork, Band(grade));
        }
    }

    [Fact]
    public void SkilledHand_ReachesSuperiorUntalented_AndMasterworkOnceMastered_InAllFourCrafts()
    {
        // "Reachable, not routine": a skilled hand is one mistake short of flawless. Untalented it
        // must clear Superior in every craft (so the top of the scale is genuinely in reach), and a
        // master with the same hand must reach Masterwork (so the talent tree pays for itself).
        foreach (var (name, grade) in AllCraftsAt(CraftHand.Skilled, NoTalents))
        {
            Assert.True(
                Band(grade) >= QualityGrade.Superior,
                $"{name}: a skilled hand graded {grade} ({Band(grade)}) — the top of the scale is out of reach");
        }

        foreach (var (name, grade) in AllCraftsAt(CraftHand.Skilled, AllTalents()))
        {
            Assert.True(
                Band(grade) == QualityGrade.Masterwork,
                $"{name}: a mastered skilled hand graded {grade} ({Band(grade)}) — mastery did not pay");
        }
    }

    [Fact]
    public void TopGrade_IsNeverAutomatic_EvenForAFullyMasteredIndifferentHand()
    {
        // The other half of the ruling: an indifferent hand must NOT reach the top grade in any
        // craft, however complete the talent tree. This is the property Tanning failed outright
        // before P2-OQ11 (87.3% Masterwork from day 6 under a hand that never looked at the hide).
        foreach (var (name, grade) in AllCraftsAt(CraftHand.Indifferent, AllTalents()))
        {
            Assert.True(
                Band(grade) < QualityGrade.Masterwork,
                $"{name}: an indifferent hand graded {grade} ({Band(grade)}) with a full tree — the top grade is automatic");
        }
    }

    [Fact]
    public void NoCraftIsPunishing_AMastersIndifferentDayStillGradesFineOrBetter()
    {
        // "The goal is that skill matters, not that the game gets harder." A fully talented crafter
        // having an off day should still make something decent.
        foreach (var (name, grade) in AllCraftsAt(CraftHand.Indifferent, AllTalents()))
        {
            Assert.True(
                Band(grade) >= QualityGrade.Fine,
                $"{name}: a master's indifferent day graded {grade} ({Band(grade)}) — that craft is punishing");
        }
    }

    [Fact]
    public void TalentsAreClearlyWorthUnlocking_AMasterOutscoresANovice_OnTheSameHand()
    {
        foreach (var hand in new[] { CraftHand.Indifferent, CraftHand.Average, CraftHand.Skilled })
        {
            var novice = AllCraftsAt(hand, NoTalents).ToList();
            var master = AllCraftsAt(hand, AllTalents()).ToList();

            for (var i = 0; i < novice.Count; i++)
            {
                Assert.True(
                    master[i].Grade > novice[i].Grade,
                    $"{novice[i].Name} at {hand}: master {master[i].Grade} did not beat novice {novice[i].Grade}");
            }
        }
    }

    [Fact]
    public void BetterHands_GradeStrictlyBetter_InEveryCraft_AtEveryTalentLevel()
    {
        // Strict ordering, end to end through the real scorers rather than through CraftCurve alone:
        // Indifferent < Average < Skilled < flawless, untalented AND fully mastered. The mastered
        // pass is the one that matters — it is the property #705's dead zone destroyed for the forge
        // (§11.7.11), and the reason the assist is added after the curve rather than to the points.
        foreach (var talents in new[] { NoTalents, AllTalents() })
        {
            var ladders = new[]
            {
                AllCraftsAt(CraftHand.Indifferent, talents).ToList(),
                AllCraftsAt(CraftHand.Average, talents).ToList(),
                AllCraftsAt(CraftHand.Skilled, talents).ToList(),
                AllCraftsFlawless(talents).ToList(),
            };

            for (var craft = 0; craft < ladders[0].Count; craft++)
            {
                for (var step = 1; step < ladders.Length; step++)
                {
                    var worse = ladders[step - 1][craft];
                    var better = ladders[step][craft];

                    // A mastered hand can legitimately be clamped at 1000 by two adjacent rungs, so
                    // the strict comparison is only owed below the ceiling.
                    if (worse.Grade >= 1000)
                    {
                        continue;
                    }

                    Assert.True(
                        better.Grade > worse.Grade,
                        $"{worse.Name}: rung {step} graded {better.Grade}, not strictly above {worse.Grade}");
                }
            }
        }
    }

    // ================================================================================
    // Fixtures: the four crafts, each scored through its OWN scorer and its OWN puzzle shape
    // ================================================================================

    /// <summary>The four crafts at one <see cref="CraftHand"/>, each graded by its own scorer on a
    /// tier-3 recipe (the widest puzzle each craft has: 5 pours, 5 sockets, the full hide).</summary>
    private static IEnumerable<(string Name, int Grade)> AllCraftsAt(
        CraftHand hand, ImmutableSortedSet<string> talents)
    {
        yield return ("Alchemy", ScoreAlchemy(AlchemyPuzzlePlayer.BuildPuzzle(AlchemyRecipe, hand), talents));
        yield return ("Tanning", ScoreTanning(TanningPuzzlePlayer.BuildPuzzle(TanningRecipe, hand), talents));
        yield return ("Engineering", ScoreEngineering(EngineeringPuzzlePlayer.BuildPuzzle(EngineeringRecipe, hand), talents));
        yield return ("Blacksmith", ScoreForge(HandForgePlayer.BuildTrace(BlacksmithRecipe, hand), talents));
    }

    /// <summary>The four crafts done perfectly — each craft's own idea of perfect.</summary>
    private static IEnumerable<(string Name, int Grade)> AllCraftsFlawless(ImmutableSortedSet<string> talents)
    {
        yield return ("Alchemy", ScoreAlchemy(
            new AlchemyReagentPuzzle(AlchemyPuzzleScorer.IdealSequenceFor(AlchemyRecipe)), talents));

        var kinds = TanningScrapeScorer.PatchesFor(1);
        var passes = ImmutableList.CreateBuilder<int>();
        for (var i = 0; i < TanningScrapeScorer.CellCount; i++)
        {
            passes.Add(TanningScrapeScorer.IdealPassesFor(kinds[i]).Min);
        }

        yield return ("Tanning", ScoreTanning(new TanningScrapeInput(passes.ToImmutable(), 1), talents));

        var schematic = EngineeringAssemblyScorer.SchematicFor(EngineeringRecipe);
        var placements = ImmutableList.CreateBuilder<int>();
        for (var socket = 0; socket < schematic.Count; socket++)
        {
            placements.Add(socket);
            placements.Add(schematic[socket]);
        }

        yield return ("Engineering", ScoreEngineering(new EngineeringAssemblyInput(placements.ToImmutable()), talents));

        // A flawless forge is the target line itself, tracked exactly, with on-beat strikes.
        var path = ForgePath.Generate(
            BlacksmithRecipe.Tier, BlacksmithRecipe.Slot, BlacksmithRecipe.BaseStats.Weight, 1);
        yield return ("Blacksmith", ScoreForge(
            new ForgeTraceInput(path, ImmutableList.Create(400, 0, 500, 0, 600, 0), 1), talents));
    }

    private static readonly Recipe AlchemyRecipe =
        AlchemyProfession.Definition.Recipes.Values.First(r => r.Tier == 3);

    private static readonly Recipe TanningRecipe =
        TanningProfession.Definition.Recipes.Values.First(r => r.Tier == 3);

    private static readonly Recipe EngineeringRecipe =
        EngineeringProfession.Definition.Recipes.Values.First(r => r.Tier == 3);

    private static readonly Recipe BlacksmithRecipe =
        ProfessionRegistry.Blacksmith.Recipes.Values.First(r => r.Tier == 3);

    private static int ScoreAlchemy(CraftPuzzleInput puzzle, ImmutableSortedSet<string> talents) =>
        AlchemyPuzzleScorer.Score(
            AlchemyRecipe, (AlchemyReagentPuzzle)puzzle, talents, AlchemyProfession.Definition).GradePermille;

    private static int ScoreTanning(CraftPuzzleInput puzzle, ImmutableSortedSet<string> talents) =>
        TanningScrapeScorer.Score(
            TanningRecipe, (TanningScrapeInput)puzzle, talents, TanningProfession.Definition).GradePermille;

    private static int ScoreEngineering(CraftPuzzleInput puzzle, ImmutableSortedSet<string> talents) =>
        EngineeringAssemblyScorer.Score(
            EngineeringRecipe, (EngineeringAssemblyInput)puzzle, talents, EngineeringProfession.Definition).GradePermille;

    private static int ScoreForge(ForgeTraceInput trace, ImmutableSortedSet<string> talents) =>
        ForgeScorer.Score(BlacksmithRecipe, trace, talents, ProfessionRegistry.Blacksmith).GradePermille;

    /// <summary>Every assist node in every profession — read off the definitions rather than listed,
    /// so a new talent joins this contract automatically instead of quietly escaping it.</summary>
    private static ImmutableSortedSet<string> AllTalents()
    {
        var builder = ImmutableSortedSet.CreateBuilder<string>(StringComparer.Ordinal);
        foreach (var profession in new[]
                 {
                     AlchemyProfession.Definition, TanningProfession.Definition,
                     EngineeringProfession.Definition, ProfessionRegistry.Blacksmith,
                 })
        {
            foreach (var nodeId in profession.MinigameAssists.Keys)
            {
                builder.Add(nodeId);
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>The <see cref="IDeterministicRng.Roll100"/> value that zeroes
    /// <see cref="QualityRoller.RollActive"/>'s jitter: <c>(50 * 51 / 100) - 25 == 0</c>.</summary>
    private sealed class ZeroJitter : IDeterministicRng
    {
        public int Roll100() => 50;

        public int NextInt(int minInclusive, int maxExclusive) => throw new NotSupportedException();

        public uint NextUInt() => throw new NotSupportedException();
    }
}
