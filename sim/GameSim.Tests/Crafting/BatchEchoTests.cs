using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Kernel;
using GameSim.Professions;

namespace GameSim.Tests.Crafting;

/// <summary>
/// Wave 5 (U23e, "batch echo"): after a hand-forge, the next few IDENTICAL auto-crafts (same recipe,
/// same day) inherit a decaying echo of that grade, so the player doesn't re-forge identical copies
/// by hand. Deterministic, RNG-draw-count-preserving; a different recipe or day makes the memory
/// stale. These tests exercise the echo state machine directly (constructing <see cref="BatchEchoState"/>
/// where useful) so they stay deterministic without long multi-day sequences.
/// </summary>
public class BatchEchoTests
{
    private static readonly Recipe Tier1Weapon = ProfessionRegistry.Blacksmith.Recipes.Values
        .First(r => r.Tier == 1 && r.Slot == ItemSlot.Weapon);

    private static readonly ImmutableList<int> PerfectStrikes = ImmutableList.Create(400, 0, 500, 0, 600, 0);

    private static readonly GameKernel Kernel = new(
        ImmutableList<IPhaseSystem>.Empty,
        ImmutableList.Create<IActionHandler>(new CraftingHandlers()));

    private static GameState StateWithIron(ulong seed = 42)
    {
        var state = GameFactory.NewGame(seed);
        return state with
        {
            ActionSlotsRemaining = 12, // headroom for multi-craft sequences
            Player = state.Player with { Materials = state.Player.Materials.SetItem("iron", 30) },
        };
    }

    private static ForgeTraceInput PerfectTrace(int pathSeed)
    {
        var path = ForgePath.Generate(Tier1Weapon.Tier, Tier1Weapon.Slot, Tier1Weapon.BaseStats.Weight, pathSeed);
        return new ForgeTraceInput(path, PerfectStrikes, pathSeed);
    }

    [Fact]
    public void HandForge_SeedsBatchEcho_AtItsGrade_ForThisRecipeAndDay()
    {
        var action = new CraftAction(Tier1Weapon.RecipeId, "iron", Puzzle: PerfectTrace(100));

        var after = Kernel.Tick(StateWithIron(), ImmutableList.Create<PlayerAction>(action)).NewState;

        var echo = after.Player.BatchEcho;
        Assert.NotNull(echo);
        Assert.Equal(Tier1Weapon.RecipeId, echo!.RecipeId);
        Assert.Equal(after.Day, echo.Day);
        Assert.Equal(0, echo.Uses);
        Assert.InRange(echo.SeedGrade, 1, 1000);
    }

    [Fact]
    public void EchoedAutoCraft_OutperformsColdAutoCraft_AndAdvancesUseCount()
    {
        // Cold reference: a bare auto-craft from a fresh (no-echo) state.
        var coldAction = new CraftAction(Tier1Weapon.RecipeId, "iron");
        var cold = Kernel.Tick(StateWithIron(), ImmutableList.Create<PlayerAction>(coldAction))
            .NewState.Items.Values.Single();

        // Echoed: hand-forge (seeds the echo) then an identical auto-craft, same tick/day.
        var handForge = new CraftAction(Tier1Weapon.RecipeId, "iron", Puzzle: PerfectTrace(100));
        var autoCopy = new CraftAction(Tier1Weapon.RecipeId, "iron");
        var result = Kernel.Tick(StateWithIron(), ImmutableList.Create<PlayerAction>(handForge, autoCopy)).NewState;

        // Two items minted; the second (the echoed copy) outranks a cold auto-craft's band.
        Assert.Equal(2, result.Items.Count);
        var echoedCopy = result.Items[result.Items.Keys.Max()]; // highest id = the second craft
        Assert.True((int)echoedCopy.Quality > (int)cold.Quality,
            $"echoed copy {echoedCopy.Quality} should outrank cold {cold.Quality}");
        Assert.Equal(1, result.Player.BatchEcho!.Uses);
    }

    [Fact]
    public void EchoDoesNotFire_ForADifferentRecipe_AndLeavesTheMemoryUnconsumed()
    {
        var otherWeapon = ProfessionRegistry.Blacksmith.Recipes.Values
            .FirstOrDefault(r => r.Slot == ItemSlot.Weapon && r.RecipeId != Tier1Weapon.RecipeId);
        Assert.NotNull(otherWeapon); // fixture assumption

        var seeded = StateWithIron();
        seeded = seeded with
        {
            Player = seeded.Player with { BatchEcho = new BatchEchoState(Tier1Weapon.RecipeId, seeded.Day, 990, 0) },
        };

        var autoOther = new CraftAction(otherWeapon!.RecipeId, "iron");
        var after = Kernel.Tick(seeded, ImmutableList.Create<PlayerAction>(autoOther)).NewState;

        // The echo was for a different recipe: not consumed, still Uses 0.
        Assert.Equal(0, after.Player.BatchEcho!.Uses);
    }

    [Fact]
    public void EchoExhausts_AfterTheCap_NextCopyIsCold()
    {
        // Seed the echo already at the cap (Uses == BatchEchoCount = 4).
        var seeded = StateWithIron();
        seeded = seeded with
        {
            Player = seeded.Player with { BatchEcho = new BatchEchoState(Tier1Weapon.RecipeId, seeded.Day, 990, 4) },
        };

        var autoCopy = new CraftAction(Tier1Weapon.RecipeId, "iron");
        var after = Kernel.Tick(seeded, ImmutableList.Create<PlayerAction>(autoCopy)).NewState;

        // At the cap the echo no longer fires, so Uses is not advanced past 4.
        Assert.Equal(4, after.Player.BatchEcho!.Uses);
    }

    [Fact]
    public void EchoedSequence_IsDeterministic_SameSeedSameResult()
    {
        var handForge = new CraftAction(Tier1Weapon.RecipeId, "iron", Puzzle: PerfectTrace(100));
        var autoCopy = new CraftAction(Tier1Weapon.RecipeId, "iron");
        var batch = ImmutableList.Create<PlayerAction>(handForge, autoCopy);

        var a = Kernel.Tick(StateWithIron(seed: 9), batch).NewState;
        var b = Kernel.Tick(StateWithIron(seed: 9), batch).NewState;

        Assert.Equal(SaveCodec.Serialize(a), SaveCodec.Serialize(b));
    }
}
