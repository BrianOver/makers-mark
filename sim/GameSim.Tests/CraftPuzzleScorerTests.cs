using System.Collections.Immutable;
using System.Linq;
using GameSim.Crafting;
using GameSim.Professions;
using Xunit;

namespace GameSim.Tests;

/// <summary>
/// U7/U8 (plan <c>2026-07-28-002</c>): the two new craft scorers that give the previously PASSIVE
/// professions something to actually do. These pin the properties the whole seam design rests on:
/// the scorers are pure, total (no input can throw), integer-only, and RNG-free, and a perfect
/// performance beats a sloppy one which beats an absent one.
///
/// <para>Neither scorer is wired into <c>CraftingHandlers</c> yet — that flip changes attainable
/// quality for two professions and so lands separately behind a balance-gate re-run. These tests
/// therefore describe the scoring rules in isolation, which is exactly what makes the later flip
/// reviewable.</para>
/// </summary>
public class CraftPuzzleScorerTests
{
    private static Recipe TanningRecipe() =>
        ProfessionRegistry.All[TanningProfession.Id].Recipes.Values.OrderBy(r => r.RecipeId, System.StringComparer.Ordinal).First();

    private static Recipe EngineeringRecipe() =>
        ProfessionRegistry.All[EngineeringProfession.Id].Recipes.Values.OrderBy(r => r.RecipeId, System.StringComparer.Ordinal).First();

    private static ProfessionDefinition Tanning() => ProfessionRegistry.All[TanningProfession.Id];

    private static ProfessionDefinition Engineering() => ProfessionRegistry.All[EngineeringProfession.Id];

    // ── tanning ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TanningPatches_AreDeterministic_AndPlaceTheExpectedCounts()
    {
        var a = TanningScrapeScorer.PatchesFor(4242);
        var b = TanningScrapeScorer.PatchesFor(4242);
        var other = TanningScrapeScorer.PatchesFor(4243);

        // NB: ImmutableArray<T>'s own equality is REFERENCE-based, so Assert.Equal/NotEqual on the
        // arrays themselves compares identities and would pass vacuously. Compare CONTENT.
        Assert.True(a.SequenceEqual(b), "same seed must rebuild an identical hide, every time");
        Assert.False(a.SequenceEqual(other), "a different seed must lay out a different hide");
        Assert.Equal(TanningScrapeScorer.CellCount, a.Length);
        Assert.Equal(5, a.Count(k => k == TanningScrapeScorer.CellKind.Flaw));
        Assert.Equal(4, a.Count(k => k == TanningScrapeScorer.CellKind.Thin));
    }

    [Fact]
    public void TanningScore_PerfectlyWorkedHide_ScoresFull_AndRuinsNothing()
    {
        const int seed = 99;
        var kinds = TanningScrapeScorer.PatchesFor(seed);
        var passes = kinds.Select(k => TanningScrapeScorer.IdealPassesFor(k).Min).ToImmutableList();

        var score = TanningScrapeScorer.Score(
            TanningRecipe(), new TanningScrapeInput(passes, seed), ImmutableSortedSet<string>.Empty, Tanning());

        Assert.Equal(1000, score.GradePermille);
        Assert.Equal(1000, score.CoveragePermille);
        Assert.Equal(0, score.RuinPermille);
    }

    [Fact]
    public void TanningScore_OverworkingEveryCell_RuinsTheHide_ButNeverThrows()
    {
        const int seed = 7;
        var passes = Enumerable.Repeat(9, TanningScrapeScorer.CellCount).ToImmutableList();

        var score = TanningScrapeScorer.Score(
            TanningRecipe(), new TanningScrapeInput(passes, seed), ImmutableSortedSet<string>.Empty, Tanning());

        Assert.Equal(0, score.GradePermille);      // scraped through everywhere: no credit at all
        Assert.Equal(1000, score.RuinPermille);
    }

    [Fact]
    public void TanningScore_UntouchedHide_ScoresZero_WithNoRuin()
    {
        var score = TanningScrapeScorer.Score(
            TanningRecipe(),
            new TanningScrapeInput(ImmutableList<int>.Empty, 3),
            ImmutableSortedSet<string>.Empty,
            Tanning());

        Assert.Equal(0, score.GradePermille);
        Assert.Equal(0, score.CoveragePermille);
        Assert.Equal(0, score.RuinPermille);
    }

    [Fact]
    public void TanningScore_CarefulWorkBeatsCarelessWork()
    {
        const int seed = 1234;
        var kinds = TanningScrapeScorer.PatchesFor(seed);
        var careful = kinds.Select(k => TanningScrapeScorer.IdealPassesFor(k).Min).ToImmutableList();
        // Careless: two passes everywhere — fine on plain cells, not enough on flaws, through on thin.
        var careless = Enumerable.Repeat(2, TanningScrapeScorer.CellCount).ToImmutableList();

        var carefulScore = TanningScrapeScorer.Score(
            TanningRecipe(), new TanningScrapeInput(careful, seed), ImmutableSortedSet<string>.Empty, Tanning());
        var carelessScore = TanningScrapeScorer.Score(
            TanningRecipe(), new TanningScrapeInput(careless, seed), ImmutableSortedSet<string>.Empty, Tanning());

        Assert.True(
            carefulScore.GradePermille > carelessScore.GradePermille,
            $"careful {carefulScore.GradePermille} should beat careless {carelessScore.GradePermille}");
    }

    [Fact]
    public void TanningScore_MalformedInput_IsTotal_NeverThrows()
    {
        var recipe = TanningRecipe();
        var nasty = new[]
        {
            new TanningScrapeInput(ImmutableList<int>.Empty, int.MinValue),
            new TanningScrapeInput(ImmutableList.Create(-5, -1, 0), 0),
            new TanningScrapeInput(Enumerable.Repeat(1, TanningScrapeScorer.CellCount * 4).ToImmutableList(), int.MaxValue),
        };

        foreach (var puzzle in nasty)
        {
            var score = TanningScrapeScorer.Score(recipe, puzzle, ImmutableSortedSet<string>.Empty, Tanning());
            Assert.InRange(score.GradePermille, 0, 1000);
            Assert.InRange(score.CoveragePermille, 0, 1000);
            Assert.InRange(score.RuinPermille, 0, 1000);
        }
    }

    // ── engineering ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EngineeringSchematic_IsDeterministic_AndSizedByTier()
    {
        var recipe = EngineeringRecipe();
        var a = EngineeringAssemblyScorer.SchematicFor(recipe);
        var b = EngineeringAssemblyScorer.SchematicFor(recipe);

        Assert.Equal(a, b);
        Assert.Equal(EngineeringAssemblyScorer.SocketCountFor(recipe), a.Count);
        Assert.InRange(a.Count, 3, 5);
        Assert.All(a, part => Assert.InRange(part, 0, EngineeringAssemblyScorer.PartCount - 1));
    }

    [Fact]
    public void EngineeringScore_PerfectAssemblyInOrder_ScoresFull()
    {
        var recipe = EngineeringRecipe();
        var schematic = EngineeringAssemblyScorer.SchematicFor(recipe);

        var flat = ImmutableList.CreateBuilder<int>();
        for (var socket = 0; socket < schematic.Count; socket++)
        {
            flat.Add(socket);
            flat.Add(schematic[socket]);
        }

        var score = EngineeringAssemblyScorer.Score(
            recipe, new EngineeringAssemblyInput(flat.ToImmutable()), ImmutableSortedSet<string>.Empty, Engineering());

        Assert.Equal(1000, score.GradePermille);   // base 1000 + order bonus, clamped
        Assert.Equal(1000, score.ExactPermille);
        Assert.Equal(1000, score.OrderPermille);
    }

    [Fact]
    public void EngineeringScore_RightPartsWrongSockets_EarnPartialCredit_NotZero()
    {
        var recipe = EngineeringRecipe();
        var schematic = EngineeringAssemblyScorer.SchematicFor(recipe);

        // Rotate the schematic by one socket: every part is called for, none is where it belongs.
        var flat = ImmutableList.CreateBuilder<int>();
        for (var socket = 0; socket < schematic.Count; socket++)
        {
            flat.Add(socket);
            flat.Add(schematic[(socket + 1) % schematic.Count]);
        }

        var score = EngineeringAssemblyScorer.Score(
            recipe, new EngineeringAssemblyInput(flat.ToImmutable()), ImmutableSortedSet<string>.Empty, Engineering());

        Assert.InRange(score.GradePermille, 1, 999);
        Assert.True(score.GradePermille > 0, "a right-parts/wrong-sockets build must beat an empty bench");
    }

    [Fact]
    public void EngineeringScore_OrderOnlyEverAdds_SoAScrambledOrderNeverScoresBelowItsPlacements()
    {
        var recipe = EngineeringRecipe();
        var schematic = EngineeringAssemblyScorer.SchematicFor(recipe);

        var inOrder = ImmutableList.CreateBuilder<int>();
        var reversed = ImmutableList.CreateBuilder<int>();
        for (var socket = 0; socket < schematic.Count; socket++)
        {
            inOrder.Add(socket);
            inOrder.Add(schematic[socket]);
        }

        for (var socket = schematic.Count - 1; socket >= 0; socket--)
        {
            reversed.Add(socket);
            reversed.Add(schematic[socket]);
        }

        var ordered = EngineeringAssemblyScorer.Score(
            recipe, new EngineeringAssemblyInput(inOrder.ToImmutable()), ImmutableSortedSet<string>.Empty, Engineering());
        var scrambled = EngineeringAssemblyScorer.Score(
            recipe, new EngineeringAssemblyInput(reversed.ToImmutable()), ImmutableSortedSet<string>.Empty, Engineering());

        Assert.Equal(1000, scrambled.ExactPermille);                         // same parts, same sockets
        Assert.True(ordered.OrderPermille > scrambled.OrderPermille);        // sequence knowledge pays
        Assert.True(scrambled.GradePermille > 0);
    }

    [Fact]
    public void EngineeringScore_ReseatingASocket_KeepsTheFirstPlacement_AndIsNeverPunished()
    {
        var recipe = EngineeringRecipe();
        var schematic = EngineeringAssemblyScorer.SchematicFor(recipe);

        var flat = ImmutableList.CreateBuilder<int>();
        for (var socket = 0; socket < schematic.Count; socket++)
        {
            flat.Add(socket);
            flat.Add(schematic[socket]);
        }

        // Append a bogus reseat of socket 0 — a pre-submit pull-and-replace must cost nothing.
        flat.Add(0);
        flat.Add((schematic[0] + 1) % EngineeringAssemblyScorer.PartCount);

        var score = EngineeringAssemblyScorer.Score(
            recipe, new EngineeringAssemblyInput(flat.ToImmutable()), ImmutableSortedSet<string>.Empty, Engineering());

        Assert.Equal(1000, score.ExactPermille);
    }

    [Fact]
    public void EngineeringScore_MalformedInput_IsTotal_NeverThrows()
    {
        var recipe = EngineeringRecipe();
        var nasty = new[]
        {
            new EngineeringAssemblyInput(ImmutableList<int>.Empty),
            new EngineeringAssemblyInput(ImmutableList.Create(0)),                       // odd length
            new EngineeringAssemblyInput(ImmutableList.Create(-1, -1, 99, 99, 3, 400)),  // out of range
            new EngineeringAssemblyInput(Enumerable.Repeat(0, 200).ToImmutableList()),   // absurdly long
        };

        foreach (var puzzle in nasty)
        {
            var score = EngineeringAssemblyScorer.Score(recipe, puzzle, ImmutableSortedSet<string>.Empty, Engineering());
            Assert.InRange(score.GradePermille, 0, 1000);
            Assert.InRange(score.ExactPermille, 0, 1000);
            Assert.InRange(score.OrderPermille, 0, 1000);
        }
    }
}
