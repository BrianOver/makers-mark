using GameSim.Contracts;
using GameSim.Crafting;

namespace GameSim.Tests.Crafting;

/// <summary>
/// Rendering follow-up (#714 finding): <see cref="CraftingHandlers.PendingEchoGrade"/> is the
/// pure preview <see cref="CraftingHandlers.ApplyCraft"/> itself now calls to seed its
/// <c>echoGrade</c> local — extracted so the Forge panel can call the SAME function instead of
/// re-typing the decay formula (the exact drift class #712 fixed for <c>HeroPanel</c>). These
/// tests pin the function's own boundaries, deriving every expected number from its own public
/// constants rather than hard-coding today's 4 / 80 / 800 — a test that re-types the constant
/// stops tracking it the moment the constant moves (this repo has shipped that bug before).
/// </summary>
public class PendingEchoGradeTests
{
    private const string RecipeId = "dagger";
    private const int Day = 5;

    [Fact]
    public void NoEcho_ReturnsNull()
    {
        Assert.Null(CraftingHandlers.PendingEchoGrade(null, RecipeId, Day));
    }

    [Fact]
    public void DifferentRecipe_ReturnsNull()
    {
        var echo = new BatchEchoState("buckler", Day, SeedGrade: 1000, Uses: 0);

        Assert.Null(CraftingHandlers.PendingEchoGrade(echo, RecipeId, Day));
    }

    [Fact]
    public void DifferentDay_ReturnsNull()
    {
        var echo = new BatchEchoState(RecipeId, Day - 1, SeedGrade: 1000, Uses: 0);

        Assert.Null(CraftingHandlers.PendingEchoGrade(echo, RecipeId, Day));
    }

    [Fact]
    public void UsesJustBelowTheCap_StillFires()
    {
        var echo = new BatchEchoState(RecipeId, Day, SeedGrade: 1000, Uses: CraftingHandlers.BatchEchoCount - 1);

        Assert.NotNull(CraftingHandlers.PendingEchoGrade(echo, RecipeId, Day));
    }

    [Fact]
    public void UsesAtTheCap_ReturnsNull()
    {
        var echo = new BatchEchoState(RecipeId, Day, SeedGrade: 1000, Uses: CraftingHandlers.BatchEchoCount);

        Assert.Null(CraftingHandlers.PendingEchoGrade(echo, RecipeId, Day));
    }

    [Fact]
    public void UsesPastTheCap_ReturnsNull()
    {
        var echo = new BatchEchoState(RecipeId, Day, SeedGrade: 1000, Uses: CraftingHandlers.BatchEchoCount + 1);

        Assert.Null(CraftingHandlers.PendingEchoGrade(echo, RecipeId, Day));
    }

    [Fact]
    public void DecayAboveTheFloor_IsNotClamped()
    {
        // uses = 0 -> decay = BatchEchoDecayPermille * 1, comfortably above the floor from a
        // max-grade seed.
        var echo = new BatchEchoState(RecipeId, Day, SeedGrade: 1000, Uses: 0);
        var expected = 1000 - (CraftingHandlers.BatchEchoDecayPermille * 1);

        Assert.True(expected > CraftingHandlers.BatchEchoFloor, "fixture assumption: this case must land above the floor");
        Assert.Equal(expected, CraftingHandlers.PendingEchoGrade(echo, RecipeId, Day));
    }

    [Fact]
    public void DecayExactlyAtTheFloor_NeedsNoClampButLandsThere()
    {
        // Choose a seed so seed - decay*(uses+1) == BatchEchoFloor exactly (uses = 0).
        var seed = CraftingHandlers.BatchEchoFloor + CraftingHandlers.BatchEchoDecayPermille;

        var echo = new BatchEchoState(RecipeId, Day, SeedGrade: seed, Uses: 0);

        Assert.Equal(CraftingHandlers.BatchEchoFloor, CraftingHandlers.PendingEchoGrade(echo, RecipeId, Day));
    }

    [Fact]
    public void DecayBelowTheFloor_ClampsUpToTheFloor()
    {
        // A low seed decayed past the floor must never be returned raw.
        var echo = new BatchEchoState(RecipeId, Day, SeedGrade: 500, Uses: 0);
        var raw = 500 - (CraftingHandlers.BatchEchoDecayPermille * 1);

        Assert.True(raw < CraftingHandlers.BatchEchoFloor, "fixture assumption: this case must land below the floor before clamping");
        Assert.Equal(CraftingHandlers.BatchEchoFloor, CraftingHandlers.PendingEchoGrade(echo, RecipeId, Day));
    }
}
