using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Kernel;
using GameSim.Professions;

namespace GameSim.Tests.Crafting;

/// <summary>
/// M3 seam pins: the performance grade is a PRESENTATION-fed modifier that must be provably
/// inert when null (pre-M3 byte-parity) and bounded when present (one material-grade step max).
/// </summary>
public class PerformanceGradeTests
{
    private static readonly Recipe Recipe = ProfessionRegistry.Blacksmith.Recipes.Values
        .First(r => r.Tier == 1);

    private static QualityGrade RollWith(int? grade, int seed)
    {
        var rng = new Pcg32(RngState.FromSeed((ulong)seed));
        return QualityRoller.Roll(
            Recipe, materialGrade: 1, ImmutableSortedSet<string>.Empty,
            ProfessionRegistry.Blacksmith.Quality, rng, grade);
    }

    [Fact]
    public void NullGrade_IsByteIdentical_ToOmittingTheParameter()
    {
        for (var seed = 0; seed < 200; seed++)
        {
            var rng = new Pcg32(RngState.FromSeed((ulong)seed));
            var baseline = QualityRoller.Roll(
                Recipe, materialGrade: 1, ImmutableSortedSet<string>.Empty,
                ProfessionRegistry.Blacksmith.Quality, rng);
            Assert.Equal(baseline, RollWith(null, seed));
        }
    }

    [Fact]
    public void NeutralGrade500_EqualsNull()
    {
        for (var seed = 0; seed < 200; seed++)
        {
            Assert.Equal(RollWith(null, seed), RollWith(500, seed));
        }
    }

    [Fact]
    public void GradeExtremes_ShiftLikeOneMaterialGradeStep()
    {
        // grade 1000 must equal a +8 shift and grade 0 a −8 shift: compare against rolling
        // with materialGrade one step up/down (the documented equivalence).
        for (var seed = 0; seed < 200; seed++)
        {
            var rngUp = new Pcg32(RngState.FromSeed((ulong)seed));
            var oneGradeUp = QualityRoller.Roll(
                Recipe, materialGrade: 2, ImmutableSortedSet<string>.Empty,
                ProfessionRegistry.Blacksmith.Quality, rngUp);
            Assert.Equal(oneGradeUp, RollWith(1000, seed));

            var rngDown = new Pcg32(RngState.FromSeed((ulong)seed));
            var oneGradeDown = QualityRoller.Roll(
                Recipe, materialGrade: 0, ImmutableSortedSet<string>.Empty,
                ProfessionRegistry.Blacksmith.Quality, rngDown);
            Assert.Equal(oneGradeDown, RollWith(0, seed));
        }
    }

    [Fact]
    public void OutOfRangeGrades_AreClamped()
    {
        for (var seed = 0; seed < 100; seed++)
        {
            Assert.Equal(RollWith(1000, seed), RollWith(9999, seed));
            Assert.Equal(RollWith(0, seed), RollWith(-500, seed));
        }
    }
}
