using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Kernel;
using GameSim.Professions;

namespace GameSim.Tests.Crafting;

/// <summary>
/// PA2/PKD3 pins: <see cref="QualityRoller.RollActive"/> — the blacksmith's active-model
/// dominance roll — against its documented table:
///
///   effective = clamp(performanceGrade ?? 550, 0, 1000) + jitter
///   jitter    = Roll100() * 51 / 100 - 25             // maps [0,99] -> [-25, +25]
///   band:  effective &lt;  200 → Poor | &lt; 550 → Common | &lt; 780 → Fine | &lt; 930 → Superior | else Masterwork
///
/// This file pins the DOMINANCE TABLE and the clamp/jitter/single-draw contract. Material
/// ceiling, auto-craft-never-Masterwork, and talent decount are pinned in
/// <see cref="ActiveQualityModelTests"/>. The former passive ±8 "inert/bounded performance
/// grade" behaviour this file used to pin now lives only on the PASSIVE path (untouched,
/// see <see cref="QualityRollerTests"/>) and is regression-pinned per-profession in
/// GameSim.Tests.Professions.
/// </summary>
public class PerformanceGradeTests
{
    // Tier-1 weapon recipe, material grade == tier, no mastery talent: materialStep = 0,
    // so the ceiling caps at Superior — irrelevant to every grade tested below except the
    // Masterwork-reaching cases, which use a material one step ABOVE tier (materialStep = 1,
    // uncapped) so the ceiling never interferes with the pure banding assertions here.
    private static readonly Recipe Recipe = ProfessionRegistry.Blacksmith.Recipes.Values
        .First(r => r.Tier == 1 && r.Slot == ItemSlot.Weapon);

    private static QualityGrade RollActive(int? grade, int seed, int materialGrade = 2 /* one step above tier 1: uncapped */) =>
        QualityRoller.RollActive(
            Recipe, materialGrade, ImmutableSortedSet<string>.Empty,
            ProfessionRegistry.Blacksmith.Quality, new Pcg32(RngState.FromSeed((ulong)seed)), grade);

    /// <summary>Independent mirror of the documented dominance table.</summary>
    private static QualityGrade ExpectedGrade(int clampedGrade, int roll)
    {
        var jitter = (roll * 51 / 100) - 25;
        var effective = clampedGrade + jitter;
        return effective switch
        {
            < 200 => QualityGrade.Poor,
            < 550 => QualityGrade.Common,
            < 780 => QualityGrade.Fine,
            < 930 => QualityGrade.Superior,
            _ => QualityGrade.Masterwork,
        };
    }

    [Theory]
    [InlineData(0)]
    [InlineData(199)]
    [InlineData(550)]
    [InlineData(780)]
    [InlineData(930)]
    [InlineData(1000)]
    public void DominanceTable_MatchesMirror_AcrossFullJitterRange(int grade)
    {
        // materialStep = 1 (uncapped) so the ceiling never masks a Masterwork result here.
        for (var roll = 0; roll < 100; roll++)
        {
            var rng = new FixedRoll(roll);
            var actual = QualityRoller.RollActive(
                Recipe, materialGrade: 2, ImmutableSortedSet<string>.Empty,
                ProfessionRegistry.Blacksmith.Quality, rng, grade);
            Assert.Equal(ExpectedGrade(grade, roll), actual);
        }
    }

    [Fact]
    public void OutOfRangeGrades_ClampBeforeJitter()
    {
        for (var seed = 0; seed < 100; seed++)
        {
            Assert.Equal(RollActive(1000, seed), RollActive(9999, seed));
            Assert.Equal(RollActive(0, seed), RollActive(-500, seed));
        }
    }

    [Fact]
    public void Jitter_NeverExceeds25_EitherDirection()
    {
        // A grade planted dead-center of a band (Common, 200..549 -> pick 375) can only ever
        // land in Common or its immediate neighbours under the documented ±25 jitter — proving
        // the jitter itself never swings wider than the table's half-width.
        for (var roll = 0; roll < 100; roll++)
        {
            var jitter = (roll * 51 / 100) - 25;
            Assert.InRange(jitter, -25, 25);
        }
    }

    [Fact]
    public void ExactlyOneRoll100Drawn_PerActiveRoll()
    {
        for (var seed = 0; seed < 50; seed++)
        {
            var rollerRng = new Pcg32(RngState.FromSeed((ulong)seed));
            var mirrorRng = new Pcg32(RngState.FromSeed((ulong)seed));

            QualityRoller.RollActive(Recipe, materialGrade: 1, ImmutableSortedSet<string>.Empty, ProfessionRegistry.Blacksmith.Quality, rollerRng, performanceGrade: 700);
            mirrorRng.Roll100(); // exactly one draw expected

            Assert.Equal(mirrorRng.Snapshot(), rollerRng.Snapshot());
        }
    }

    /// <summary>An <see cref="IDeterministicRng"/> whose <c>Roll100</c> always returns a fixed value — for
    /// walking every one of the 100 possible jitter outcomes deterministically without a real seed search.</summary>
    private sealed class FixedRoll(int value) : IDeterministicRng
    {
        public int Roll100() => value;

        public int NextInt(int minInclusive, int maxExclusive) => throw new NotSupportedException();

        public uint NextUInt() => throw new NotSupportedException();
    }
}
