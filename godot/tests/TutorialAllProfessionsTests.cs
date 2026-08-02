#if GDUNIT_TESTS
using System;
using System.Linq;
using GameSim;
using GameSim.Contracts;
using GameSim.Professions;
using GdUnit4;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// The regression this bug fix exists to add: Brian's playtest, verbatim — "The tutorial says
/// buckler but there is none in the forge? cannot continue (think its because i may have picked
/// alchemist)" — had to restart the game entirely. Every OTHER tutorial suite in this repo
/// (<see cref="TutorialFlowTests"/>, <see cref="TutorialKeepsUpTests"/>) drives the chain through
/// <see cref="ScriptedSession"/>'s hardcoded blacksmith "dagger"/"copper" recipe, so a profession
/// mismatch anywhere in the chain's own text or completion logic had nothing to catch it.
///
/// <para>This suite starts a campaign as EACH of the four registered professions
/// (<see cref="ProfessionRegistry.All"/> — add a fifth profession and it is covered here for
/// free), resolves every recipe from THAT profession's own table (never a literal id), and walks
/// the whole chain to <see cref="TutorialFlow.Completed"/>, asserting the card is never left
/// showing a blank/empty instruction while still <see cref="TutorialFlow.Active"/> — the exact
/// shape of "cannot continue".</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TutorialAllProfessionsTests
{
    [TestCase("blacksmith")]
    [TestCase("tanning")]
    [TestCase("engineering")]
    [TestCase("alchemy")]
    public void WalkTheTutorialToCompletion_ResolvingEveryStepFromTheChosenProfessionsOwnRecipes(string professionId)
    {
        var campaign = GameComposition.NewCampaign(ScriptedSession.Seed, professionId);
        var ui = MountMainUi(new SimAdapter(campaign));
        try
        {
            AssertThat(ui.Tutorial.Active).IsTrue();
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.BuyMaterial);
            AssertThat(ui.Adapter.CurrentState.Player.SelectedProfessions).IsEqual(
                System.Collections.Immutable.ImmutableSortedSet.Create(professionId));

            // Never a literal id (the exact bug: "buckler" is the BLACKSMITH's tier-1 shield; an
            // alchemist's own forge never carries it) — resolve THIS profession's own cheapest
            // tier-1 recipe from its own table, mirroring NewCampaignSeedingTests.CheapestTier1.
            var recipe = ProfessionRegistry.All[professionId].Recipes.Values
                .Where(r => r.Tier == 1)
                .OrderBy(r => r.MaterialQuantity)
                .ThenBy(r => r.RecipeId, StringComparer.Ordinal)
                .First();

            var openingText = ui.Tutorial.TopSlotText(ui.Adapter.CurrentState);
            AssertThat(openingText)
                .OverrideFailureMessage($"{professionId}: no tutorial text at all on a fresh campaign.")
                .IsNotNull();

            // Craft straight off the shared starter-copper seeding (GameFactory.StarterCopper
            // covers every profession's cheapest tier-1 recipe — NewCampaignSeedingTests pins this
            // for all four) — the direct reproduction of "picked alchemist, crafted the starter
            // item".
            ui.Adapter.Queue(new CraftAction(recipe.RecipeId, recipe.MaterialKey));
            AssertThat(ui.Adapter.LastRejections.Count)
                .OverrideFailureMessage(
                    $"{professionId}'s own cheapest tier-1 recipe '{recipe.RecipeId}' was rejected on day 1 " +
                    "— the starter kit does not actually cover this profession, which is exactly the dead " +
                    "end this suite exists to catch.")
                .IsEqual(0);
            AssertThat(ui.Tutorial.Step)
                .OverrideFailureMessage($"{professionId}: crafting its own recipe did not move the chain off step 1.")
                .IsNotEqual(TutorialStep.BuyMaterial);

            var craftedItem = ui.Adapter.CurrentState.Items.Values.Single(item => item.PlayerCrafted).Id;
            ui.Adapter.Queue(new StockAction(craftedItem, 10));
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.PostBounty);

            ui.Adapter.Queue(new PostBountyAction(1, 1));

            // Drive the day forward — bounded so a real dead end fails the test instead of hanging
            // it — asserting the card is NEVER left blank while still active (the exact shape of
            // "cannot continue") and that the chain actually finishes rather than stalling behind
            // an unsatisfiable step.
            var maxTicks = MaxPhasesPerDay * 2;
            for (var tick = 0; tick < maxTicks && !ui.Tutorial.Completed; tick++)
            {
                if (ui.Tutorial.Active)
                {
                    var text = ui.Tutorial.TopSlotText(ui.Adapter.CurrentState);
                    AssertThat(string.IsNullOrWhiteSpace(text))
                        .OverrideFailureMessage(
                            $"{professionId}: tutorial still active on tick {tick} but showing no instruction " +
                            "— a player would have nothing readable to act on.")
                        .IsFalse();
                }

                ui.Adapter.AdvancePhase();
            }

            AssertThat(ui.Tutorial.Completed)
                .OverrideFailureMessage(
                    $"{professionId}: the tutorial never completed within {maxTicks} phase ticks — the chain " +
                    "dead-ended, the exact failure this suite exists to catch.")
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }
}
#endif
