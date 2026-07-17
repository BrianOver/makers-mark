using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Kernel;
using GameSim.Professions;

namespace GameSim.Tests.Professions;

/// <summary>
/// Covers profession SELECTION and how it gates crafting and scopes talents (P1):
/// <see cref="SetProfessionsAction"/> validation, the "recipe's profession must be selected"
/// craft gate, and per-profession talent scoping.
/// </summary>
public class ProfessionSelectionTests
{
    private static readonly GameKernel Selection = new(
        ImmutableList<IPhaseSystem>.Empty,
        ImmutableList.Create<IActionHandler>(new ProfessionHandlers()));

    private static readonly GameKernel Crafting = new(
        ImmutableList<IPhaseSystem>.Empty,
        ImmutableList.Create<IActionHandler>(new CraftingHandlers()));

    private static SetProfessionsAction Select(params string[] ids) =>
        new(ImmutableSortedSet.CreateRange(ids));

    // ---- SetProfessions validation ------------------------------------------

    [Fact]
    public void SelectOne_Registered_Works()
    {
        var result = Selection.Tick(GameFactory.NewGame(seed: 1),
            ImmutableList.Create<PlayerAction>(Select(ProfessionRegistry.BlacksmithId)));

        Assert.Empty(result.Rejected);
        Assert.True(result.NewState.Player.IsSelected(ProfessionRegistry.BlacksmithId));
        Assert.Single(result.NewState.Player.SelectedProfessions);
    }

    [Fact]
    public void SelectTwo_IsWithinTheLimit()
    {
        // "weaving" is the still-unregistered placeholder (was "tanning" until the Tanning
        // add-on registered it). The limit is two: a two-element set must be rejected (if at
        // all) for its UNKNOWN member, never for exceeding the count — proving two is allowed.
        Assert.Equal(2, ProfessionHandlers.MaxSelected);

        var result = Selection.Tick(GameFactory.NewGame(seed: 1),
            ImmutableList.Create<PlayerAction>(Select(ProfessionRegistry.BlacksmithId, "weaving")));

        var rejected = Assert.Single(result.Rejected);
        Assert.Contains("weaving", rejected.Reason);
        Assert.DoesNotContain("more than", rejected.Reason);
    }

    [Fact]
    public void SelectThree_Rejected_ForCount()
    {
        var result = Selection.Tick(GameFactory.NewGame(seed: 1),
            ImmutableList.Create<PlayerAction>(Select("a", "b", "c")));

        var rejected = Assert.Single(result.Rejected);
        Assert.Contains("more than", rejected.Reason);

        // Rejection leaves the NewGame default selection intact.
        Assert.True(result.NewState.Player.IsSelected(ProfessionRegistry.BlacksmithId));
    }

    [Fact]
    public void SelectUnknownProfession_Rejected()
    {
        var result = Selection.Tick(GameFactory.NewGame(seed: 1),
            ImmutableList.Create<PlayerAction>(Select("weaving")));

        var rejected = Assert.Single(result.Rejected);
        Assert.Contains("weaving", rejected.Reason);
    }

    [Fact]
    public void SelectNone_Rejected()
    {
        var result = Selection.Tick(GameFactory.NewGame(seed: 1),
            ImmutableList.Create<PlayerAction>(Select()));

        Assert.Single(result.Rejected);
    }

    // ---- Craft gated by selection -------------------------------------------

    [Fact]
    public void Craft_Rejected_WhenRecipeProfessionNotSelected()
    {
        var state = GameFactory.NewGame(seed: 42);
        state = state with
        {
            Player = state.Player with
            {
                Materials = state.Player.Materials.SetItem("copper", 5),
                SelectedProfessions = ImmutableSortedSet<string>.Empty, // deselect blacksmith
            },
        };

        var result = Crafting.Tick(state, ImmutableList.Create<PlayerAction>(new CraftAction("dagger", "copper")));

        var rejected = Assert.Single(result.Rejected);
        Assert.Contains("blacksmith", rejected.Reason);
        Assert.Empty(result.NewState.Items);
        Assert.Equal(5, result.NewState.Player.Materials["copper"]); // nothing consumed
    }

    [Fact]
    public void Craft_Allowed_AfterSelectingProfession()
    {
        var state = GameFactory.NewGame(seed: 42);
        state = state with
        {
            Player = state.Player with
            {
                Materials = state.Player.Materials.SetItem("copper", 5),
                SelectedProfessions = ImmutableSortedSet<string>.Empty,
            },
        };

        state = Selection.Tick(state, ImmutableList.Create<PlayerAction>(Select(ProfessionRegistry.BlacksmithId))).NewState;
        var result = Crafting.Tick(state, ImmutableList.Create<PlayerAction>(new CraftAction("dagger", "copper")));

        Assert.Empty(result.Rejected);
        Assert.Single(result.NewState.Items);
    }

    // ---- Talent unlock is profession-scoped ---------------------------------

    [Fact]
    public void UnlockTalent_OnlyAffectsThatProfessionsSet()
    {
        var result = Crafting.Tick(GameFactory.NewGame(seed: 1), ImmutableList.Create<PlayerAction>(
            new UnlockTalentAction(TalentTree.KeenEye, ProfessionRegistry.BlacksmithId)));

        Assert.Empty(result.Rejected);
        var player = result.NewState.Player;

        Assert.Contains(TalentTree.KeenEye, player.TalentsFor(ProfessionRegistry.BlacksmithId));

        // A different profession key sees an empty, independent set — scoping holds.
        Assert.Empty(player.TalentsFor("tanning"));
        Assert.DoesNotContain(TalentTree.KeenEye, player.TalentsFor("tanning"));

        // The outer Talents map carries exactly the one profession that was touched.
        Assert.Equal(new[] { ProfessionRegistry.BlacksmithId }, player.Talents.Keys);
    }

    [Fact]
    public void UnlockTalent_UnknownProfession_Rejected()
    {
        var result = Crafting.Tick(GameFactory.NewGame(seed: 1), ImmutableList.Create<PlayerAction>(
            new UnlockTalentAction(TalentTree.KeenEye, "tanning")));

        var rejected = Assert.Single(result.Rejected);
        Assert.Contains("tanning", rejected.Reason);
    }
}
