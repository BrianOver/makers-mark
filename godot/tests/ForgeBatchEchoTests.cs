#if GDUNIT_TESTS
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Kernel;
using GdUnit4;
using Godot;
using GodotClient;
using GodotClient.Minigames;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// #714 finding: batch echo (Wave 5 U23e, <see cref="CraftingHandlers"/>'s class doc) computes a
/// decaying grade for the next few auto-crafts of a hand-forged recipe, but nothing in
/// <c>godot/</c> ever showed it — the player had no way to see an echo was live, which recipe it
/// was on, what remained of it, or that it expires. This pins the Forge recipe card's "Echo" chip
/// (built in <see cref="GodotClient.Panels.ForgePanel"/>'s recipe-card loop), wired through
/// <see cref="CraftingHandlers.PendingEchoGrade"/> — the SAME pure function
/// <c>CraftingHandlers.ApplyCraft</c> itself calls for the real craft. Every assertion below
/// checks the render against THAT function's own output, never a hand-recomputed number — the
/// exact drift class #712 fixed for <c>HeroPanel</c>'s hand-typed threshold copy.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ForgeBatchEchoTests
{
    private static GameState StateWithEcho(BatchEchoState? echo)
    {
        var state = ScriptedSession.StartState();
        return state with { Player = state.Player with { BatchEcho = echo } };
    }

    private static string DaggerCardText(MainUi ui) =>
        RenderedText(Find<PanelContainer>(ui.Forge, $"RecipeCard_{ScriptedSession.CraftRecipeId}"));

    [TestCase]
    public void FreshEcho_RendersFullUsesLeftAndTheSimsOwnTrendingGrade()
    {
        var day = ScriptedSession.StartState().Day;
        var echo = new BatchEchoState(ScriptedSession.CraftRecipeId, day, SeedGrade: 1000, Uses: 0);
        var ui = MountMainUi(new SimAdapter(StateWithEcho(echo)));
        try
        {
            ui.OpenPanel("Forge");

            var expectedGrade = CraftingHandlers.PendingEchoGrade(echo, ScriptedSession.CraftRecipeId, day);
            AssertThat(expectedGrade.HasValue).IsTrue();
            var expectedBand = ForgeMinigame.PreviewGrade(expectedGrade!.Value);
            var expectedLeft = CraftingHandlers.BatchEchoCount - echo.Uses;

            AssertThat(DaggerCardText(ui)).Contains($"{expectedLeft} left today, trending {expectedBand}");
        }
        finally { Unmount(ui); }
    }

    /// <summary>The Uses-vs-BatchEchoCount boundary this whole chip depends on: one use short of
    /// the cap must still fire (and read "1 left"), never silently drop a use early.</summary>
    [TestCase]
    public void EchoAtItsLastValidUse_StillRendersOneLeft()
    {
        var day = ScriptedSession.StartState().Day;
        var echo = new BatchEchoState(ScriptedSession.CraftRecipeId, day, SeedGrade: 1000, Uses: CraftingHandlers.BatchEchoCount - 1);
        var ui = MountMainUi(new SimAdapter(StateWithEcho(echo)));
        try
        {
            ui.OpenPanel("Forge");

            var expectedGrade = CraftingHandlers.PendingEchoGrade(echo, ScriptedSession.CraftRecipeId, day);
            AssertThat(expectedGrade.HasValue).IsTrue(); // still live — the boundary this test pins
            var expectedBand = ForgeMinigame.PreviewGrade(expectedGrade!.Value);

            AssertThat(DaggerCardText(ui)).Contains($"1 left today, trending {expectedBand}");
        }
        finally { Unmount(ui); }
    }

    /// <summary>The other side of the same boundary: at the cap, the sim's own function returns
    /// null (see <see cref="EchoExhausts_MatchesTheSimsOwnFunction"/>), so the card must render no
    /// chip at all — not a "0 left" chip, which would be a live-looking dead echo.</summary>
    [TestCase]
    public void EchoExhausted_AtTheCap_RendersNoEchoChip()
    {
        var day = ScriptedSession.StartState().Day;
        var echo = new BatchEchoState(ScriptedSession.CraftRecipeId, day, SeedGrade: 1000, Uses: CraftingHandlers.BatchEchoCount);
        var ui = MountMainUi(new SimAdapter(StateWithEcho(echo)));
        try
        {
            ui.OpenPanel("Forge");

            AssertThat(DaggerCardText(ui)).NotContains("Echo");
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void EchoExhausts_MatchesTheSimsOwnFunction()
    {
        var day = ScriptedSession.StartState().Day;
        var echo = new BatchEchoState(ScriptedSession.CraftRecipeId, day, SeedGrade: 1000, Uses: CraftingHandlers.BatchEchoCount);

        AssertThat(CraftingHandlers.PendingEchoGrade(echo, ScriptedSession.CraftRecipeId, day)).IsNull();
    }

    /// <summary>A recipe that isn't the one the echo was seeded on must never inherit it — the
    /// card only reads its OWN <see cref="Recipe.RecipeId"/> against the echo's.</summary>
    [TestCase]
    public void EchoForADifferentRecipe_RendersNoEchoChipOnThisCard()
    {
        var day = ScriptedSession.StartState().Day;
        var echo = new BatchEchoState("buckler", day, SeedGrade: 1000, Uses: 0); // any OTHER recipe id
        var ui = MountMainUi(new SimAdapter(StateWithEcho(echo)));
        try
        {
            ui.OpenPanel("Forge");

            AssertThat(CraftingHandlers.PendingEchoGrade(echo, ScriptedSession.CraftRecipeId, day)).IsNull();
            AssertThat(DaggerCardText(ui)).NotContains("Echo");
        }
        finally { Unmount(ui); }
    }

    /// <summary>Yesterday's echo is stale today — the same match check <c>CraftingHandlers</c>
    /// itself applies, so the card can't show a memory that would no longer fire.</summary>
    [TestCase]
    public void EchoFromAPriorDay_RendersNoEchoChipOnThisCard()
    {
        var day = ScriptedSession.StartState().Day;
        var echo = new BatchEchoState(ScriptedSession.CraftRecipeId, day - 1, SeedGrade: 1000, Uses: 0);
        var ui = MountMainUi(new SimAdapter(StateWithEcho(echo)));
        try
        {
            ui.OpenPanel("Forge");

            AssertThat(CraftingHandlers.PendingEchoGrade(echo, ScriptedSession.CraftRecipeId, day)).IsNull();
            AssertThat(DaggerCardText(ui)).NotContains("Echo");
        }
        finally { Unmount(ui); }
    }

    /// <summary>The chip is a READ of state.Player.BatchEcho — rendering it must never itself
    /// write anything (whole-state fingerprint, not a hand-listed field set that could silently
    /// miss a mutation elsewhere in the tree).</summary>
    [TestCase]
    public void RenderingTheEchoChip_WritesNoSimState()
    {
        var day = ScriptedSession.StartState().Day;
        var echo = new BatchEchoState(ScriptedSession.CraftRecipeId, day, SeedGrade: 1000, Uses: 0);
        var ui = MountMainUi(new SimAdapter(StateWithEcho(echo)));
        try
        {
            var before = SaveCodec.Serialize(ui.Adapter.CurrentState);
            ui.OpenPanel("Forge");
            var after = SaveCodec.Serialize(ui.Adapter.CurrentState);

            AssertThat(after).IsEqual(before);
        }
        finally { Unmount(ui); }
    }
}
#endif
