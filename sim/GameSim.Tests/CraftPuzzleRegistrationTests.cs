using System.Collections.Immutable;
using System.Reflection;
using GameSim.Contracts;
using GameSim.Kernel;
using GameSim.Professions;

namespace GameSim.Tests;

/// <summary>
/// The save codec can only serialize <see cref="CraftPuzzleInput"/> subtypes that someone
/// remembered to register in <see cref="SaveCodec"/>. Forgetting one is not a compile error and
/// not a test failure anywhere else — it throws <c>NotSupportedException</c> at runtime, only on
/// the save path, only after a player actually crafts with that profession. The Godot client
/// catches that and pushes a warning, so the visible symptom is: the campaign silently stops
/// saving.
///
/// <para>That is exactly what shipped. Task #30 wired the tanning and engineering puzzles and
/// registered neither, so every autosave after an engineer's or tanner's craft failed — found by
/// reading a playtest log, not by a test. This file is the pin that makes the next omission a red
/// build instead of a lost campaign.</para>
///
/// <para><b>The census pins the SET, not a sample.</b> It enumerates the assembly for concrete
/// subtypes rather than checking the four we happen to know about, so a fifth profession added
/// tomorrow fails here on the day it lands.</para>
/// </summary>
public class CraftPuzzleRegistrationTests
{
    private static IReadOnlyList<Type> AllConcretePuzzleTypes =>
        typeof(CraftPuzzleInput).Assembly
            .GetTypes()
            .Where(t => t is { IsAbstract: false, IsClass: true } && t.IsSubclassOf(typeof(CraftPuzzleInput)))
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToList();

    [Fact]
    public void EveryPuzzleTypeInTheAssembly_IsRegisteredWithTheSaveCodec()
    {
        var registered = SaveCodec.RegisteredPuzzleTypes.ToHashSet();
        var missing = AllConcretePuzzleTypes.Where(t => !registered.Contains(t)).ToList();

        Assert.True(
            missing.Count == 0,
            $"CraftPuzzleInput subtype(s) not registered in SaveCodec.AddCraftPuzzlePolymorphism: "
            + $"{string.Join(", ", missing.Select(t => t.FullName))}. "
            + "An unregistered subtype makes autosave throw the moment a player crafts with it, "
            + "and the client only logs a warning — the campaign stops saving with no visible error. "
            + "Add a DerivedTypes.Add line for each.");
    }

    [Fact]
    public void TheRegistrationIsNotStale_NoRegisteredTypeHasBeenDeleted()
    {
        var all = AllConcretePuzzleTypes.ToHashSet();
        var orphans = SaveCodec.RegisteredPuzzleTypes.Where(t => !all.Contains(t)).ToList();

        Assert.True(
            orphans.Count == 0,
            $"SaveCodec registers type(s) that are no longer CraftPuzzleInput subtypes: "
            + $"{string.Join(", ", orphans.Select(t => t.FullName))}.");
    }

    [Fact]
    public void DiscriminatorsAreUnique()
    {
        var duplicates = SaveCodec.RegisteredPuzzleTypes
            .GroupBy(t => t)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key.FullName)
            .ToList();

        Assert.True(duplicates.Count == 0, $"Duplicate registration(s): {string.Join(", ", duplicates)}");
    }

    /// <summary>
    /// The census proves registration; this proves the registration actually works end-to-end for
    /// the two types the bug hit. A round-trip through the real codec is the thing the playtest was
    /// doing when it broke.
    /// </summary>
    [Theory]
    [MemberData(nameof(PuzzleSamples))]
    public void EachPuzzleType_SurvivesASaveRoundTrip(string label, CraftPuzzleInput puzzle)
    {
        var state = GameFactory.NewGame(seed: 12345UL);
        var action = new CraftAction("dagger", "copper", Puzzle: puzzle);
        var batch = new LoggedBatch(state.Day, state.Phase, ImmutableList.Create<PlayerAction>(action));
        var withPuzzle = state with { ActionLog = state.ActionLog.Add(batch) };

        var json = SaveCodec.Serialize(withPuzzle);
        var back = SaveCodec.Deserialize(json);

        var recovered = Assert.IsType<CraftAction>(back.ActionLog[^1].Actions[^1]);
        Assert.Equal(puzzle, recovered.Puzzle);
        Assert.NotNull(label);
    }

    public static TheoryData<string, CraftPuzzleInput> PuzzleSamples() => new()
    {
        { "alchemy", new AlchemyReagentPuzzle(ImmutableList.Create(1, 2, 3)) },
        { "engineering", new EngineeringAssemblyInput(ImmutableList.Create(0, 3, 1)) },
        { "tanning", new TanningScrapeInput(ImmutableList.Create(2, 2, 1), PatchSeed: 77) },
    };
}
