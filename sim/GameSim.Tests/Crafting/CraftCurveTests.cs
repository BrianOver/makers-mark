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

        // 100 per-mille clear of the Fine seam, so the CURVE's own output for an indifferent hand
        // cannot be lifted out of Common by RollActive's +/-25 jitter alone. Post-curve bonuses
        // (talent assists, Engineering's order bonus) deliberately can and do lift it — see
        // IndifferentAnchorPermille's own doc, and NoCraftIsPunishing_* below, which asserts the
        // talent case as a requirement rather than tolerating it.
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

    [Fact]
    public void EveryPuzzle_AsksForDistinctThings_SoAWrongGuessCannotBeAccidentallyRight()
    {
        // The defect this pins, found by the 20-seed sweep and not by this file's first cut:
        // EngineeringAssemblyScorer.SchematicFor stepped by `Tier + 2`, which at tier 1 is 3 and
        // shares a factor with PartCount (6) — so every tier-1 schematic wanted the same part in two
        // sockets. With a repeated part, NO derangement of the called-for multiset exists, so a hand
        // that deliberately puts every part in the wrong socket still lands a free exact match. That
        // is 183 per-mille of unearned credit, and it is what let an indifferent assembly take
        // Masterwork 26.7% of the time while a strictly better hand took it 1.0% of the time.
        foreach (var recipe in EngineeringProfession.Definition.Recipes.Values)
        {
            var schematic = EngineeringAssemblyScorer.SchematicFor(recipe);
            Assert.Equal(schematic.Count, schematic.Distinct().Count());
            Assert.All(schematic, part => Assert.InRange(part, 0, EngineeringAssemblyScorer.PartCount - 1));
        }
    }

    // ================================================================================
    // One coherent curve: the same described hand lands in the same band in all four crafts
    // ================================================================================

    [Fact]
    public void IndifferentHand_NeverGradesAboveFine_AndUsuallyGradesCommon()
    {
        // The curve puts an indifferent hand at mid-Common by construction, and most recipes land
        // exactly there. Two things legitimately lift a few above it, and both are named rather than
        // averaged away:
        //
        //  - A recipe whose puzzle calls for the SAME component twice cannot be fully deranged, so a
        //    hand that puts every component in the wrong place still lands one accidental exact
        //    match. Alchemy has two such brews on purpose (greater-elixir and philosophers-stone
        //    reuse a reagent), and AlchemyPuzzleScorer's own doc already called that credit correct
        //    rather than a bug: a brew that wants Sunpetal at both ends genuinely does tolerate a
        //    rotation better. That is authored content, unlike Engineering's version of the same
        //    obstruction, which was an accident of a derived formula and is fixed
        //    (EveryPuzzle_AsksForDistinctThings_* above).
        //  - Engineering's build-order bonus is added after the curve and an indifferent hand can
        //    still build in order.
        //
        // What must hold everywhere is the ceiling: an indifferent hand never reads as skilled work.
        foreach (var (name, grade) in AllCraftsAt(CraftHand.Indifferent, NoTalents))
        {
            Assert.True(
                Band(grade) <= QualityGrade.Fine,
                $"{name}: an indifferent hand graded {grade} ({Band(grade)}) — that reads as skill it did not show");
            Assert.True(grade > 0, $"{name}: an indifferent hand still tried — it must not score 0");
        }

        // ... and it must still be the COMMON case, or the anchor has stopped meaning anything.
        var hands = AllCraftsAt(CraftHand.Indifferent, NoTalents).ToList();
        var common = hands.Count(h => Band(h.Grade) == QualityGrade.Common);
        Assert.True(
            common * 2 > hands.Count,
            $"only {common} of {hands.Count} indifferent crafts graded Common — the anchor is not holding");
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
        // "Reachable, not routine": a skilled hand is one mistake short of flawless. Everywhere it
        // must clear Fine untalented, and a master with the same hand must reach Masterwork (so the
        // talent tree pays for itself).
        //
        // Why Fine and not Superior everywhere: on the SMALLEST puzzles the least mistake a player
        // can physically make is a big fraction of the whole craft. A tier-1 brew is three pours, and
        // a pour order cannot be wrong in exactly one place — the smallest possible error is a
        // transposition, which costs 2 of the 6 points. So a tier-1 brew has essentially three
        // outcomes (indifferent, one mistake, flawless) and no room for a fourth. That is a property
        // of a 3-slot memory puzzle, not of the curve, and it is not punishing: getting three pours
        // right is well within a skilled player's reach, and doing so pays Masterwork. Where a puzzle
        // HAS the resolution to express the difference — the widest of each craft, checked
        // immediately below — a skilled hand does clear Superior on its own merits.
        foreach (var (name, grade) in AllCraftsAt(CraftHand.Skilled, NoTalents))
        {
            Assert.True(
                Band(grade) >= QualityGrade.Fine,
                $"{name}: a skilled hand graded {grade} ({Band(grade)}) — the top of the scale is out of reach");
        }

        foreach (var (name, grade) in WidestPuzzleOfEachCraft(CraftHand.Skilled, NoTalents))
        {
            Assert.True(
                Band(grade) >= QualityGrade.Superior,
                $"{name}: a skilled hand on this craft's widest puzzle graded {grade} ({Band(grade)}) "
                + "— Superior must be reachable without talents where the puzzle can express it");
        }

        // Mastered, the same hand clears Superior everywhere...
        foreach (var (name, grade) in AllCraftsAt(CraftHand.Skilled, AllTalents()))
        {
            Assert.True(
                Band(grade) >= QualityGrade.Superior,
                $"{name}: a mastered skilled hand graded {grade} ({Band(grade)}) — mastery did not pay");
        }

        // ...and takes the top grade on the widest puzzle of every craft, which is what makes
        // Masterwork reachable-by-skill rather than reachable-only-by-perfection. On the narrow
        // recipes it stops at Superior and the top stays behind a flawless craft — three pours is
        // small enough that getting them all right IS the skilled outcome.
        foreach (var (name, grade) in WidestPuzzleOfEachCraft(CraftHand.Skilled, AllTalents()))
        {
            Assert.True(
                Band(grade) == QualityGrade.Masterwork,
                $"{name}: a mastered skilled hand on this craft's widest puzzle graded {grade} "
                + $"({Band(grade)}) — the top grade is not reachable by skill in this craft");
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
        //
        // The ordering owed is over PERFORMANCES, not over the names of these three hands. On the
        // smallest puzzles two adjacent rungs describe the identical submitted input — a 3-pour
        // brew's "first half remembered, tail guessed" IS its "last two transposed" — and scoring an
        // identical input identically is determinism, not a flattened curve. Those pairs are skipped
        // by comparing the inputs, never by comparing the grades, so a genuinely flat curve can never
        // slip through this as if it were a small puzzle.
        var inputs = new[]
        {
            HandInputs(CraftHand.Indifferent).ToList(),
            HandInputs(CraftHand.Average).ToList(),
            HandInputs(CraftHand.Skilled).ToList(),
        };

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

                    // Two named hands that submit the IDENTICAL input are the same performance, and
                    // must score the same (see this test's own comment). Only the three scripted
                    // rungs have inputs to compare; the flawless rung is always a distinct input.
                    if (step < inputs.Length
                        && inputs[step - 1][craft] == inputs[step][craft])
                    {
                        Assert.Equal(worse.Grade, better.Grade);
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

    /// <summary>
    /// EVERY recipe of all four crafts at one <see cref="CraftHand"/>, each graded by its own
    /// scorer on its own puzzle shape.
    ///
    /// <para><b>Every recipe, not one per craft — this is the whole reason these fixtures are
    /// shaped this way.</b> The first cut of this file sampled a single tier-3 recipe per
    /// profession, and every contract below passed. It was wrong: Engineering's tier-1 schematics
    /// had period 2 (see <c>EngineeringAssemblyScorer.SchematicFor</c>'s own comment), which handed
    /// an indifferent hand a free exact match and let it take Masterwork on 26.7% of a 20-seed
    /// sweep. A tier-3-only fixture cannot see that, and the sweep found what the test should have.
    /// Sampling one recipe per craft is how a per-tier defect hides under a green suite.</para>
    /// </summary>
    private static IEnumerable<(string Name, int Grade)> AllCraftsAt(
        CraftHand hand, ImmutableSortedSet<string> talents)
    {
        foreach (var recipe in AlchemyProfession.Definition.Recipes.Values)
        {
            yield return ($"Alchemy/{recipe.RecipeId}",
                ScoreAlchemy(recipe, AlchemyPuzzlePlayer.BuildPuzzle(recipe, hand), talents));
        }

        foreach (var recipe in TanningProfession.Definition.Recipes.Values)
        {
            yield return ($"Tanning/{recipe.RecipeId}",
                ScoreTanning(recipe, TanningPuzzlePlayer.BuildPuzzle(recipe, hand), talents));
        }

        foreach (var recipe in EngineeringProfession.Definition.Recipes.Values)
        {
            yield return ($"Engineering/{recipe.RecipeId}",
                ScoreEngineering(recipe, EngineeringPuzzlePlayer.BuildPuzzle(recipe, hand), talents));
        }

        foreach (var recipe in ProfessionRegistry.Blacksmith.Recipes.Values)
        {
            yield return ($"Blacksmith/{recipe.RecipeId}",
                ScoreForge(recipe, HandForgePlayer.BuildTrace(recipe, hand), talents));
        }
    }

    /// <summary>Every recipe of all four crafts done perfectly — each craft's own idea of perfect.
    /// Enumerated in the SAME order as <see cref="AllCraftsAt"/>, which the ladder test relies on to
    /// compare rungs recipe by recipe.</summary>
    private static IEnumerable<(string Name, int Grade)> AllCraftsFlawless(ImmutableSortedSet<string> talents)
    {
        foreach (var recipe in AlchemyProfession.Definition.Recipes.Values)
        {
            yield return ($"Alchemy/{recipe.RecipeId}", ScoreAlchemy(
                recipe, new AlchemyReagentPuzzle(AlchemyPuzzleScorer.IdealSequenceFor(recipe)), talents));
        }

        var kinds = TanningScrapeScorer.PatchesFor(1);
        var passes = ImmutableList.CreateBuilder<int>();
        for (var i = 0; i < TanningScrapeScorer.CellCount; i++)
        {
            passes.Add(TanningScrapeScorer.IdealPassesFor(kinds[i]).Min);
        }

        var perfectHide = new TanningScrapeInput(passes.ToImmutable(), 1);
        foreach (var recipe in TanningProfession.Definition.Recipes.Values)
        {
            yield return ($"Tanning/{recipe.RecipeId}", ScoreTanning(recipe, perfectHide, talents));
        }

        foreach (var recipe in EngineeringProfession.Definition.Recipes.Values)
        {
            var schematic = EngineeringAssemblyScorer.SchematicFor(recipe);
            var placements = ImmutableList.CreateBuilder<int>();
            for (var socket = 0; socket < schematic.Count; socket++)
            {
                placements.Add(socket);
                placements.Add(schematic[socket]);
            }

            yield return ($"Engineering/{recipe.RecipeId}",
                ScoreEngineering(recipe, new EngineeringAssemblyInput(placements.ToImmutable()), talents));
        }

        // A flawless forge is the target line itself, tracked exactly, with on-beat strikes.
        foreach (var recipe in ProfessionRegistry.Blacksmith.Recipes.Values)
        {
            var path = ForgePath.Generate(recipe.Tier, recipe.Slot, recipe.BaseStats.Weight, 1);
            yield return ($"Blacksmith/{recipe.RecipeId}", ScoreForge(
                recipe, new ForgeTraceInput(path, ImmutableList.Create(400, 0, 500, 0, 600, 0), 1), talents));
        }
    }

    /// <summary>One tier-3 recipe per craft — the widest puzzle each has (5 pours, 5 sockets, the
    /// full hide, the longest heat line), and so the only place a craft has the resolution to place a
    /// near-flawless hand distinctly below a flawless one.</summary>
    private static IEnumerable<(string Name, int Grade)> WidestPuzzleOfEachCraft(
        CraftHand hand, ImmutableSortedSet<string> talents)
    {
        var alchemy = AlchemyProfession.Definition.Recipes.Values.First(r => r.Tier == 3);
        yield return ("Alchemy", ScoreAlchemy(alchemy, AlchemyPuzzlePlayer.BuildPuzzle(alchemy, hand), talents));

        var tanning = TanningProfession.Definition.Recipes.Values.First(r => r.Tier == 3);
        yield return ("Tanning", ScoreTanning(tanning, TanningPuzzlePlayer.BuildPuzzle(tanning, hand), talents));

        var engineering = EngineeringProfession.Definition.Recipes.Values.First(r => r.Tier == 3);
        yield return ("Engineering",
            ScoreEngineering(engineering, EngineeringPuzzlePlayer.BuildPuzzle(engineering, hand), talents));

        var blacksmith = ProfessionRegistry.Blacksmith.Recipes.Values.First(r => r.Tier == 3);
        yield return ("Blacksmith", ScoreForge(blacksmith, HandForgePlayer.BuildTrace(blacksmith, hand), talents));
    }

    /// <summary>A STRUCTURAL key for the puzzle input each craft's policy submits at
    /// <paramref name="hand"/>, in the SAME recipe order as <see cref="AllCraftsAt"/> — so the ladder
    /// test can tell "these two rungs scored the same because the curve is flat" from "they scored
    /// the same because the puzzle is too small for them to be different hands at all".
    ///
    /// <para>Rendered to a string rather than compared as records on purpose: every puzzle input
    /// type holds an <see cref="ImmutableList{T}"/>, which has no structural equality, so the
    /// compiler-generated record <c>Equals</c> compares those by REFERENCE and reports two
    /// identical hands as different. Using it would have silently disabled the skip this key
    /// exists to drive.</para></summary>
    private static IEnumerable<string> HandInputs(CraftHand hand)
    {
        foreach (var recipe in AlchemyProfession.Definition.Recipes.Values)
        {
            var puzzle = (AlchemyReagentPuzzle)AlchemyPuzzlePlayer.BuildPuzzle(recipe, hand);
            yield return string.Join(",", puzzle.Reagents);
        }

        foreach (var recipe in TanningProfession.Definition.Recipes.Values)
        {
            var puzzle = (TanningScrapeInput)TanningPuzzlePlayer.BuildPuzzle(recipe, hand);
            yield return $"{puzzle.PatchSeed}:{string.Join(",", puzzle.CellPasses)}";
        }

        foreach (var recipe in EngineeringProfession.Definition.Recipes.Values)
        {
            var puzzle = (EngineeringAssemblyInput)EngineeringPuzzlePlayer.BuildPuzzle(recipe, hand);
            yield return string.Join(",", puzzle.Placements);
        }

        foreach (var recipe in ProfessionRegistry.Blacksmith.Recipes.Values)
        {
            var trace = HandForgePlayer.BuildTrace(recipe, hand);
            yield return $"{trace.PathSeed}:{string.Join(",", trace.Samples)}:{string.Join(",", trace.Strikes)}";
        }
    }

    private static int ScoreAlchemy(Recipe recipe, CraftPuzzleInput puzzle, ImmutableSortedSet<string> talents) =>
        AlchemyPuzzleScorer.Score(
            recipe, (AlchemyReagentPuzzle)puzzle, talents, AlchemyProfession.Definition).GradePermille;

    private static int ScoreTanning(Recipe recipe, CraftPuzzleInput puzzle, ImmutableSortedSet<string> talents) =>
        TanningScrapeScorer.Score(
            recipe, (TanningScrapeInput)puzzle, talents, TanningProfession.Definition).GradePermille;

    private static int ScoreEngineering(Recipe recipe, CraftPuzzleInput puzzle, ImmutableSortedSet<string> talents) =>
        EngineeringAssemblyScorer.Score(
            recipe, (EngineeringAssemblyInput)puzzle, talents, EngineeringProfession.Definition).GradePermille;

    private static int ScoreForge(Recipe recipe, ForgeTraceInput trace, ImmutableSortedSet<string> talents) =>
        ForgeScorer.Score(recipe, trace, talents, ProfessionRegistry.Blacksmith).GradePermille;

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
