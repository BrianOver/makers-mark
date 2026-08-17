#if GDUNIT_TESTS
using System.Collections.Immutable;
using System.Linq;
using GameSim;
using GameSim.Contracts;
using GdUnit4;
using Godot;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U-T2 Wave C (§11.14.4, Act II, R14.7 "the tutorial names the six dilemmas out loud — one
/// sentence each, both sides, no recommendation"): the two dilemmas this wave teaches — "pricing
/// as a decision" (dilemma #2, fires the first time the player ever reprices a shelf item) and
/// "hold-or-sell" (dilemma #1, fires the first time the player ever accepts a commission) — both
/// routed through the SAME shared <see cref="MentorBanner"/> Wave C introduces rather than a
/// panel-private copy, and the SAME <see cref="TutorialFlow.ConsumeFirstTouch"/> once-ever engine
/// Wave A shipped.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class DilemmaLessonsTests
{
    private static readonly ItemId ShelvedItemId = new(9401);

    private static GameState StateWithOneShelvedItem()
    {
        var baseState = GameComposition.NewCampaign(seed: 9401);
        var item = new Item(
            ShelvedItemId, "test-dilemma-shelf-item", "Test Buckler", ItemSlot.Weapon,
            QualityGrade.Common, new ItemStats(Attack: 4, Defense: 0, Weight: 2),
            new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);

        return baseState with
        {
            Items = baseState.Items.Add(item.Id.Value, item),
            Player = baseState.Player with { Shelf = ImmutableList.Create(new ShelfEntry(item.Id, 10)) },
        };
    }

    /// <summary>Pricing is one of the two dilemmas the census named as wholly untaught before this
    /// unit (plan §11.14.4's own finding). Reprice's own Wave-C wiring must reach the shared
    /// banner regardless of which control drove it (the legacy button, exercised here).</summary>
    [TestCase]
    public void RepricingAShelfItemForTheFirstTime_TeachesThePricingDilemma()
    {
        var ui = MountMainUi(new SimAdapter(StateWithOneShelvedItem()));
        try
        {
            ui.OpenPanel("Shop");

            PressEnabled(ui.Shop, $"Reprice_{ShelvedItemId.Value}");

            AssertThat(ui.Adapter.AppliedThisPhase.OfType<SetPriceAction>().Any(a => a.Item == ShelvedItemId))
                .OverrideFailureMessage("Setup check: the Reprice press did not queue a SetPriceAction.")
                .IsTrue();

            AssertThat(ui.Mentor.Visible)
                .OverrideFailureMessage("The pricing dilemma never showed on the player's first-ever reprice.")
                .IsTrue();
            var text = Find<Label>(ui.Mentor, "MentorBannerText").Text;
            AssertThat(text).Contains(MentorVoice.Name); // attributed to Bryn, never an anonymous tooltip
            AssertThat(text).Contains("relationship");
            AssertThat(text).Contains("sale");

            // R14.7: no recommendation — the lesson must not tell the player which side to take.
            AssertThat(text.Contains("you should", System.StringComparison.OrdinalIgnoreCase))
                .OverrideFailureMessage($"The pricing dilemma reads as a recommendation, not a naming of both sides: \"{text}\"")
                .IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>A second reprice must NOT re-fire the lesson — the same once-ever contract every
    /// other first-touch lesson in this codebase carries (the 1287x memorial nag precedent).</summary>
    [TestCase]
    public void RepricingASecondTime_DoesNotReshowThePricingDilemma()
    {
        var ui = MountMainUi(new SimAdapter(StateWithOneShelvedItem()));
        try
        {
            ui.OpenPanel("Shop");
            PressEnabled(ui.Shop, $"Reprice_{ShelvedItemId.Value}");
            ui.Mentor.Dismiss();

            PressEnabled(ui.Shop, $"Reprice_{ShelvedItemId.Value}");

            AssertThat(ui.Mentor.Visible)
                .OverrideFailureMessage("The pricing dilemma fired a SECOND time — the anti-nag pin failed.")
                .IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Hold-or-sell is the other dilemma named in Wave C's own unit list — accepting a
    /// commission is the moment the player chooses to hold a slot for one named hero rather than
    /// sell freely off the shelf.</summary>
    [TestCase]
    public void AcceptingACommissionForTheFirstTime_TeachesTheHoldOrSellDilemma()
    {
        var commission = new Commission(new HeroId(1), ItemSlot.Weapon, QualityGrade.Fine, DeadlineDay: 12, PremiumGold: 30);
        var state = GameComposition.NewCampaign(seed: 9402) with { Commissions = ImmutableList.Create(commission) };
        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            ui.Commissions.ShowOpen(ui.Adapter.CurrentState);

            PressEnabled(ui.Commissions, "CommissionAccept_1");

            AssertThat(ui.Adapter.AppliedThisPhase.OfType<AcceptCommissionAction>().Any(a => a.Hero == new HeroId(1)))
                .OverrideFailureMessage("Setup check: the Accept press did not queue an AcceptCommissionAction.")
                .IsTrue();

            AssertThat(ui.Mentor.Visible)
                .OverrideFailureMessage("The hold-or-sell dilemma never showed on the player's first-ever commission accept.")
                .IsTrue();
            var text = Find<Label>(ui.Mentor, "MentorBannerText").Text;
            AssertThat(text).Contains(MentorVoice.Name);
            AssertThat(text).Contains("shelf");
            AssertThat(text).Contains("commission");
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Law: no timers on decisions — this banner carries no countdown, same contract as
    /// every other Wave B/C mentor lesson.</summary>
    [TestCase]
    public async System.Threading.Tasks.Task MentorBanner_NeverAutoDismisses_RegardlessOfHowManyFramesPass()
    {
        var ui = MountMainUi(new SimAdapter(StateWithOneShelvedItem()));
        var player = new HumanPlayer(ui);
        try
        {
            ui.OpenPanel("Shop");
            PressEnabled(ui.Shop, $"Reprice_{ShelvedItemId.Value}");
            AssertThat(ui.Mentor.Visible).IsTrue();

            await player.Frames(90);

            AssertThat(ui.Mentor.Visible)
                .OverrideFailureMessage("The shared mentor banner disappeared on its own after real frames passed with no player input.")
                .IsTrue();
        }
        finally
        {
            player.ReleaseAll();
            Unmount(ui);
        }
    }
}
#endif
