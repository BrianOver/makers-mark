#if GDUNIT_TESTS
using System.Linq;
using GameSim.Contracts;
using GameSim.Drama;
using GdUnit4;
using Godot;
using GodotClient.Panels;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U11 engine-lane scenarios: the panels bind real sim state through the ONE adapter
/// and every action goes through real Controls. AE4/AE7 assertions read the rendered
/// Control text, never just the sim value.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class MainUiTests
{
    [TestCase]
    public void MainUi_Instantiates_And_BindsMidGameState()
    {
        var ui = MountMainUi();
        try
        {
            // Tab shell: six panels, pinned order, plus the modal overlay.
            AssertThat(ui.Tabs.GetTabCount()).IsEqual(6);
            var titles = Enumerable.Range(0, 6).Select(i => ui.Tabs.GetTabTitle(i)).ToArray();
            AssertThat(string.Join(",", titles)).IsEqual("Forge,Shop,Heroes,Tavern,Depths,Bounties");
            AssertThat(ui.Ledger.Visible).IsFalse();

            // Drive to a mid-game state: one full day + the next Morning.
            for (var tick = 0; tick < 4; tick++)
            {
                ui.Adapter.AdvancePhase();
            }

            ui.Ledger.CloseModal();
            var state = ui.Adapter.CurrentState;
            AssertThat(state.Day).IsEqual(2);
            AssertThat(state.Phase).IsEqual(DayPhase.Expedition);

            // Status bar reflects the live state.
            var status = Find<Label>(ui, "StatusLabel").Text;
            AssertThat(status).Contains($"Day {state.Day}");
            AssertThat(status).Contains(state.Phase.ToString());
            AssertThat(status).Contains($"Gold {state.Player.Gold}g");

            // Hero roster renders every hero; detail pane renders the selected one.
            var heroesText = RenderedText(ui.Heroes);
            foreach (var hero in state.Heroes.Values)
            {
                AssertThat(heroesText).Contains(hero.Name);
            }

            // Depths board renders each recorded standing.
            var depthsText = RenderedText(ui.Depths);
            foreach (var (heroValue, floor) in state.Drama.DepthsBoard)
            {
                AssertThat(depthsText).Contains($"floor {floor} — {state.Heroes[heroValue].Name}");
            }

            // Tavern renders the newest gossip line (day-2 Morning gossips over day 1).
            var gossip = state.EventLog.OfType<GossipEmitted>().ToList();
            var tavernText = RenderedText(ui.Tavern);
            if (gossip.Count > 0)
            {
                AssertThat(tavernText).Contains(gossip[^1].Line);
            }
            else
            {
                AssertThat(tavernText).Contains("quiet");
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void ForgePanel_CraftRoundTrip_ChangesSimAndRefreshesPanels()
    {
        var ui = MountMainUi();
        try
        {
            DriveToCraftedDagger(ui);
            var state = ui.Adapter.CurrentState;

            // Sim state changed: the dagger exists, carries the maker's mark, copper was consumed.
            var crafted = state.Items.Values.Where(item => item.PlayerCrafted).ToList();
            AssertThat(crafted.Count).IsEqual(1);
            AssertThat(crafted[0].Name).IsEqual("Dagger");
            AssertThat(ui.Adapter.LastRejections.Count).IsEqual(0);

            // Panels refreshed from the new state: the shop lists the unshelved craft,
            // the forge shows the post-craft copper stock.
            AssertThat(RenderedText(ui.Shop)).Contains("Dagger");
            var copperLeft = state.Player.Materials.TryGetValue("copper", out var stock) ? stock : 0;
            AssertThat(RenderedText(ui.Forge)).Contains($"copper x{copperLeft}");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void DayAdvance_FullPhaseCycle_OpensLedgerWithCards()
    {
        var ui = MountMainUi();
        try
        {
            ui.Adapter.AdvancePhase(); // day 1 Morning
            ui.Adapter.AdvancePhase(); // day 1 Expedition
            AssertThat(ui.Ledger.Visible).IsFalse();
            ui.Adapter.AdvancePhase(); // day 1 Evening — the reveal

            AssertThat(ui.Ledger.Visible).IsTrue();
            AssertThat(ui.Ledger.ShownDay).IsEqual(1);
            AssertThat(ui.LastCompletedDay).IsEqual(1);

            // The modal renders the produced return cards: every fate line present.
            var cards = LedgerQuery.ReturnCards(ui.Adapter.CurrentState, 1);
            AssertThat(cards.Count > 0).IsTrue();
            var ledgerText = RenderedText(ui.Ledger);
            foreach (var card in cards)
            {
                AssertThat(ledgerText).Contains(card.HeroName);
                AssertThat(ledgerText).Contains(card.Survived
                    ? $"returned from floor {card.FloorReached}, earned {card.GoldEarned}g"
                    : $"DIED on floor {card.FloorReached}");
            }

            // Reading the ledger holds the town clock; closing releases it.
            AssertThat(ui.Clock.Playing).IsFalse();
            Press(ui.Ledger, "CloseLedger");
            AssertThat(ui.Ledger.Visible).IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void ShopPanel_RendersHeroPassReason_AE4RenderHalf()
    {
        var ui = MountMainUi();
        try
        {
            DriveToCraftedDagger(ui);

            // Day 3 Morning: shelve the dagger at 9999g through the shop panel's controls.
            var itemId = ScriptedSession.CraftedItem(ui.Adapter.CurrentState);
            Find<SpinBox>(ui.Shop, $"StockPrice_{itemId.Value}").Value = ScriptedSession.UnaffordablePrice;
            Press(ui.Shop, $"Stock_{itemId.Value}");
            ui.Adapter.AdvancePhase(); // day 3 Morning: stock applies, then heroes shop and pass

            var state = ui.Adapter.CurrentState;
            var passes = state.EventLog
                .OfType<HeroPassedOnItem>()
                .Where(pass => pass.Day == 3 && pass.Item == itemId)
                .ToList();
            AssertThat(passes.Count > 0).IsTrue();

            // AE4 render half: the reason string is on the rendered shop panel itself.
            var shopText = RenderedText(ui.Shop);
            foreach (var pass in passes)
            {
                AssertThat(shopText).Contains(pass.Reason);
                AssertThat(shopText).Contains($"{state.Heroes[pass.Hero.Value].Name} passed:");
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void BountyPanel_RendersJudgmentReason_AE7RenderHalf()
    {
        var ui = MountMainUi();
        try
        {
            // Post through the panel's form on day 1 Morning.
            Find<SpinBox>(ui.Bounties, "BountyFloor").Value = ScriptedSession.BountyFloor;
            Find<SpinBox>(ui.Bounties, "BountyReward").Value = ScriptedSession.BountyReward;
            Press(ui.Bounties, "PostBounty");
            ui.Adapter.AdvancePhase(); // Morning: the post applies (gold escrowed)

            AssertThat(RenderedText(ui.Bounties))
                .Contains($"clear floor {ScriptedSession.BountyFloor} for {ScriptedSession.BountyReward}g");

            ui.Adapter.AdvancePhase(); // Expedition: every alive hero judges the bounty

            var judged = ui.Adapter.CurrentState.EventLog
                .OfType<BountyJudged>()
                .Where(judgment => judgment.Day == 1)
                .ToList();
            AssertThat(judged.Count > 0).IsTrue();

            // AE7 render half: accept/decline reasons are on the rendered bounty board.
            var bountyText = RenderedText(ui.Bounties);
            foreach (var judgment in judged)
            {
                AssertThat(bountyText).Contains(judgment.Reason);
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// Drives the shared script through the real controls up to the day-2 Evening
    /// tick: bounty posted day 1, day-1 copper offers bought via the reopened Ledger's
    /// Buy buttons, dagger craft queued via the Forge panel's Craft button.
    /// Leaves the sim at day 3 Morning with one unshelved player craft.
    /// </summary>
    private static void DriveToCraftedDagger(MainUi ui)
    {
        for (var tick = 0; tick < 5; tick++)
        {
            ui.Adapter.AdvancePhase(); // day 1 M/X/V, day 2 M/X
        }

        ui.Ledger.CloseModal();
        var state = ui.Adapter.CurrentState;
        AssertThat(state.Day).IsEqual(2);
        AssertThat(state.Phase).IsEqual(DayPhase.Evening);

        // Reopen the day-1 ledger from the status bar and buy copper via its Buy buttons.
        Press(ui, "OpenLedger");
        AssertThat(ui.Ledger.ShownDay).IsEqual(1);
        var buys = ScriptedSession.CopperBuys(state);
        AssertThat(buys.Count > 0).IsTrue();
        foreach (var offer in buys)
        {
            Press(ui.Ledger, $"BuyOre_{offer.From.Value}_{offer.MaterialKey}");
        }

        Press(ui.Ledger, "CloseLedger");

        // Queue the craft through the forge panel's action path (default material = copper).
        Press(ui.Forge, $"Craft_{ScriptedSession.CraftRecipeId}");
        AssertThat(RenderedText(ui.Forge)).Contains($"queued: craft {ScriptedSession.CraftRecipeId}");

        ui.Adapter.AdvancePhase(); // day 2 Evening: buys then craft apply in order
        ui.Ledger.CloseModal();    // the day-2 ledger auto-opened; not under test here
    }
}
#endif
