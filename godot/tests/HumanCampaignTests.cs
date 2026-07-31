#if GDUNIT_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GameSim.Contracts;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// Plays a real multi-day campaign through <see cref="HumanPlayer"/> and asserts the LOOP ACTUALLY TURNS.
///
/// <para><b>Why this is different from every other suite.</b> The panel sweeps prove each surface is
/// readable and its buttons respond. <c>Playtest3dClickThrough</c> proves clicking everything never throws.
/// Neither asks the only question that matters: <b>does playing the game get you anywhere?</b> A build where
/// every screen is pretty, every button fires, nothing crashes, and the player still cannot craft, sell,
/// earn, or reach day 3 would pass all of them. Owner: "you need to test the whole game lol".</para>
///
/// <para><b>What it drives.</b> Real mouse clicks on real buttons, panel by panel, phase by phase, across
/// several days — the same <see cref="HumanPlayer"/> contract as everything else: no
/// <c>EmitSignal(Pressed)</c>, no <c>Adapter.Queue</c>, no reaching past the UI — including the bell, which
/// is clicked on the HUD with the drawer closed. An earlier draft ticked the phase through
/// <c>Adapter.AdvancePhase</c> instead, on the reasoning that the bell was covered elsewhere; that froze the
/// campaign on day 1 because the real bell handler closes an open counter session and the kernel holds the day
/// at Morning until it does. Every step here is a click, and that is why.</para>
///
/// <para><b>What it asserts.</b> Progress, not polish: the day advances, materials get bought, something
/// gets crafted, something reaches the shelf, gold moves, and the player is never left with no legal verb.
/// Each is stated as its own claim so a failure names which part of the loop died rather than "the
/// playthrough failed".</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class HumanCampaignTests
{
    /// <summary>Days to play. Three is the shortest run that exercises a full cycle plus the day-2 and day-3
    /// consequences (ore offers land on day 2, the first Ledger auto-reveals during day 3), and it keeps the
    /// test inside a CI job that already runs close to its deadline.</summary>
    private const int Days = 3;

    /// <summary>Panels worth visiting each phase. The Forge and Shop carry the craft/sell loop; the Gate's
    /// Bounties panel is the third verb the tutorial teaches.</summary>
    private static readonly string[] WorkPanels = ["Forge", "Shop", "Bounties"];

    /// <summary>Scroll depths visited per panel per phase. Three reaches the Forge's recipe cards (the vendor
    /// rows fill the first page) without making a three-day run unaffordable in CI.</summary>
    private const int PagesPerPanel = 3;

    [TestCase]
    public async Task PlayingThreeDaysWithRealClicks_TurnsTheWholeLoop()
    {
        // The INTENDED new-player start (profession + starter copper), not the bare seed adapter — this
        // reflects what a player actually boots into, which is the campaign the real front door injects.
        var ui = MountMainUi(new SimAdapter(
            GameSim.GameComposition.NewCampaign(2026UL, GameSim.Professions.ProfessionRegistry.BlacksmithId)));
        try
        {
            var player = new HumanPlayer(ui);
            await player.WaitForLayout(ui);

            var log = new List<string>();
            var clicks = 0;
            var startGold = ui.Adapter.CurrentState.Player.Gold;
            var deadEnds = new List<string>();

            for (var day = 0; day < Days; day++)
            {
                var ticks = 0;
                do
                {
                    var phase = ui.Adapter.CurrentState.Phase;

                    foreach (var panel in WorkPanels)
                    {
                        clicks += await WorkThePanel(ui, player, panel, log);
                    }

                    // A phase must offer SOMETHING beyond ending itself. Checked with the drawer closed,
                    // which is the state the player is in when deciding what to do next.
                    ui.OpenPanel("Town");
                    await player.Frames(4);
                    if (!player.ClickableLabels().Any(IsRealVerb))
                    {
                        deadEnds.Add($"day {ui.Adapter.CurrentState.Day} {phase}");
                    }

                    // RING THE BELL FOR REAL — a click on the HUD's own button, with the drawer closed so the
                    // veil is not over it.
                    //
                    // The first version called Adapter.AdvancePhase() directly, reasoning that "can you ring
                    // the bell" was already covered elsewhere. That was wrong, and the test caught it: MainUi's
                    // bell handler CLOSES an open counter session before ticking, and the kernel holds the day
                    // at Morning while a session is open. So clicking "Open Counter" in the Shop and then
                    // bypassing the real bell froze the campaign on day 1 forever — 28 clicks, four material
                    // buys, and time never moved. Skipping the "trivial" step skipped load-bearing logic,
                    // which is the same lesson as every seam bug in this project.
                    await RingTheBell(ui, player, log);
                }
                while (ui.Adapter.CurrentState.Phase != DayPhase.Morning && ++ticks <= MaxPhasesPerDay);
            }

            var end = ui.Adapter.CurrentState;
            var story = $"{clicks} real clicks over {Days} days.\n  " + string.Join("\n  ", log);

            // ── 1. Time passed. If this fails nothing below means anything. ──
            AssertThat(end.Day)
                .OverrideFailureMessage($"Played {Days} days of phases but the sim is still on day {end.Day}.\n{story}")
                .IsGreaterEqual(Days);

            // ── 2. The player could actually spend money on materials. ──
            AssertThat(log.Any(l => l.Contains("bought")))
                .OverrideFailureMessage(
                    $"Three days of clicking every enabled Forge button never bought a single material, so " +
                    $"the loop cannot even start.\n{story}")
                .IsTrue();

            // ── 3. Something got made. This is the game's whole premise. ──
            AssertThat(end.Items.Values.Any(i => i.PlayerCrafted))
                .OverrideFailureMessage(
                    $"Three days of play produced NO player-crafted item. The player is a blacksmith who " +
                    $"cannot forge anything.\n{story}")
                .IsTrue();

            // ── 4. Something reached the shelf, where heroes can buy it. ──
            //
            // Asserted from the play log, not from the END state: the shelf DRAINS when a hero buys, so a
            // final snapshot of zero is equally consistent with "never shelved anything" and with "shelved it
            // and sold it", which are opposite outcomes. What happened during the run is the honest record.
            AssertThat(log.Any(l => l.Contains("shelved")) || end.Player.Shelf.Count > 0)
                .OverrideFailureMessage(
                    $"Nothing the player made ever reached the shelf. Crafting with no route to a customer is " +
                    $"a dead end.\n{story}")
                .IsTrue();

            // ── 5. Gold moved. Direction is deliberately unasserted — early days SHOULD run at a loss —
            //      but a purse frozen at its starting value across three days means no economy is running. ──
            AssertThat(end.Player.Gold)
                .OverrideFailureMessage(
                    $"Gold never moved from {startGold} across three days of buying and selling.\n{story}")
                .IsNotEqual(startGold);

            // ── 6. No phase ever left the player with nothing to do. ──
            AssertThat(deadEnds)
                .OverrideFailureMessage(
                    $"These phases offered no verb at all besides ending them: [{string.Join(", ", deadEnds)}]\n{story}")
                .IsEmpty();

            // ── Anti-fakery: the run has to have actually done things. ──
            AssertThat(clicks)
                .OverrideFailureMessage($"Only {clicks} buttons were clicked across the whole run.\n{story}")
                .IsGreater(15);
        }
        finally { Unmount(ui); }
    }


    /// <summary>
    /// Click the HUD's bell — the real one, with its own session-closing behaviour — and let the tick settle.
    /// </summary>

    /// <summary>Open <paramref name="panel"/> and scroll to <paramref name="page"/>, re-establishing the whole
    /// position from scratch — a click can close the drawer, rebuild the content, and reset the scroll.</summary>
    private static async Task OpenAt(MainUi ui, HumanPlayer player, string panel, int page)
    {
        ui.OpenPanel(panel);

        // A short settle budget on purpose: the drawer's slide finishes in ~20 frames and this runs once per
        // page per panel per phase per day. A panel that has not settled in 60 frames is measured
        // approximately, which is the right trade here — this test asks whether the loop turns, not whether a
        // rect is pixel-exact.
        //
        // Measured cost: ~61s locally for three days, ~44s for two (so ~17s per day). Three is kept because
        // CI's overhead is NOT a linear multiple of local time — a 28s local sweep cost ~49s in CI, not 5x —
        // so trimming coverage on a predicted CI cost would be guessing, which is the mistake this project
        // keeps paying for.
        await player.TrySettleLayout(ui.Drawer.CurrentContent!, maxFrames: 60);

        var content = ui.Drawer.CurrentContent!;
        for (var scroll = 0; scroll < page; scroll++)
        {
            await player.ScrollDown(content.GetGlobalRect().GetCenter());
        }
    }

    /// <summary>
    /// Close any modal the GAME raised on its own, the way a player has to before the day can go on.
    ///
    /// <para>Several open themselves: the Ledger at the Evening return ritual, the Camp slate while a party is
    /// below, the Chronicle at a campaign ending. Each puts a click-catching veil over the HUD, so the bell is
    /// unreachable until it is dismissed — which is correct, and which stalled this playthrough until it was
    /// handled. Reading and closing the Ledger is part of playing, not an obstacle to it.</para>
    /// </summary>
    private static async Task ClearModals(MainUi ui, HumanPlayer player, List<string> log)
    {
        Control[] modals =
        [
            ui.Ledger, ui.Camp, ui.Chronicle, ui.Mirror,
            ui.Forecast, ui.Bestiary, ui.Commissions, ui.Legends,
        ];

        // Loop until nothing is left, closing whatever is actually REACHABLE this pass.
        //
        // Modals stack: the Camp slate and the Ledger can both be up at once, with Camp on top. A single
        // fixed-order pass then tries to close the covered one and the click is swallowed by the modal above
        // it — correct veil behaviour, and it stalled this playthrough. Retrying until no progress handles any
        // stacking order without having to know the z-order.
        for (var pass = 0; pass < modals.Length; pass++)
        {
            var closedSomething = false;

            foreach (var modal in modals.Where(m => m.IsVisibleInTree()))
            {
                var closer = player.ClickableButtons(modal).FirstOrDefault(b =>
                    b.Name.ToString().Contains("Close", StringComparison.OrdinalIgnoreCase) ||
                    b.Text.Contains("Close", StringComparison.OrdinalIgnoreCase) ||
                    b.Text.Contains("Hold", StringComparison.OrdinalIgnoreCase) ||
                    b.Text.Contains('✕') || b.Text.Contains('×'));

                if (closer is null)
                {
                    // Not fatal here — WholeGameSweepTests owns the "every modal is dismissible" claim, and it
                    // found a real softlock that way. Recorded so a stall downstream is explicable.
                    log.Add($"[modal] {modal.Name} is up with no reachable close control");
                    continue;
                }

                try
                {
                    await player.ClickControl(closer, $"close {modal.Name}");
                    log.Add($"[modal] closed {modal.Name}");
                    closedSomething = true;
                    await player.Frames(4);
                }
                catch (InvalidOperationException)
                {
                    // Covered by another modal — a later pass gets it once that one is gone.
                }
            }

            if (!closedSomething || !modals.Any(m => m.IsVisibleInTree()))
            {
                return;
            }
        }
    }

    private static async Task RingTheBell(MainUi ui, HumanPlayer player, List<string> log)
    {
        // Wait for the drawer's veil to actually GO, not merely for Close() to have been called. The drawer
        // slides shut over DrawerHost.SlideSeconds and its click-catching ColorRect stays up for the whole
        // slide — so a bell click a few frames after closing a panel lands on the veil and is swallowed. Four
        // frames was not enough and the click silently did nothing.
        for (var frame = 0; frame < 120 && ui.Drawer.Veil.IsVisibleInTree(); frame++)
        {
            await player.Frames(1);
        }

        var bell = ui.FindChild("AdvancePhase", recursive: true, owned: false) as Button
            ?? throw new InvalidOperationException("The HUD has no AdvancePhase bell button.");

        var dayBefore = ui.Adapter.CurrentState.Day;
        var phaseBefore = ui.Adapter.CurrentState.Phase;

        // Up to two presses, because ONE press legitimately may not move the day.
        //
        // Clicking "Open Counter" only QUEUES the action; the bell applies it, and the kernel then holds the
        // day at Morning for as long as a counter session is open (PA3/PKD5 — that hold is the whole point of
        // a stepped counter). So the first bell after opening the counter opens the session instead of ending
        // the phase, and the second closes it and advances. A player experiences this as "opening the shop
        // costs me a bell". Treating the first hold as a stall was wrong, and the test said so.
        for (var press = 0; press < 2; press++)
        {
            // Clear modals before EVERY press, not once before the loop. The Return Ritual opens the Ledger on
            // a wall-clock delay inside _Process, so it can appear during the frames this method itself pumps —
            // clearing beforehand left the second press landing on a veil that did not exist yet when we looked.
            await ClearModals(ui, player, log);

            await player.ClickControl(bell, $"bell \"{bell.Text}\"");
            await player.Frames(4);

            var now = ui.Adapter.CurrentState;
            if (now.Day != dayBefore || now.Phase != phaseBefore)
            {
                return;
            }

            if (now.Counter is not { Closed: false })
            {
                break; // held for some reason OTHER than an open session — that is a genuine stall
            }
        }

        var after = ui.Adapter.CurrentState;
        throw new InvalidOperationException(
            $"Two bell presses at day {dayBefore} {phaseBefore} advanced nothing (counter session: " +
            $"{(after.Counter is { Closed: false } ? "open" : "none")}). The day is stuck, so the campaign " +
            $"cannot progress.{player.TraceTail()}");
    }

    /// <summary>
    /// Open <paramref name="panel"/> and click every enabled button in it, recording what the sim did in
    /// response. Returns how many clicks landed.
    ///
    /// <para>Re-derives the clickable list before each click — panels rebuild themselves on refresh, so both
    /// node names and instances go stale (see <c>HumanPlaytestTests</c> for the full account).</para>
    /// </summary>
    private static async Task<int> WorkThePanel(MainUi ui, HumanPlayer player, string panel, List<string> log)
    {
        var clicked = 0;

        // PAGE DOWN through the panel. Without scrolling this only ever reached the top of the Forge — the
        // vendor rows — and never the recipe cards further down, so a three-day run bought eight materials and
        // never crafted a single thing. The assertion said "the player is a blacksmith who cannot forge
        // anything", which was true of the TEST, not the game.
        for (var page = 0; page < PagesPerPanel; page++)
        {
            clicked += await WorkPage(ui, player, panel, page, log);
        }

        return clicked;
    }

    /// <summary>Click everything reachable at one scroll depth of <paramref name="panel"/>.</summary>
    private static async Task<int> WorkPage(MainUi ui, HumanPlayer player, string panel, int page, List<string> log)
    {
        var clicked = 0;

        await OpenAt(ui, player, panel, page);

        var available = player.ClickableButtons(ui.Drawer.CurrentContent).Count;
        for (var i = 0; i < available; i++)
        {
            if (ui.Drawer.CurrentPanelId != panel)
            {
                await OpenAt(ui, player, panel, page);
            }

            var live = player.ClickableButtons(ui.Drawer.CurrentContent);
            if (i >= live.Count)
            {
                break;
            }

            var button = live[i];
            var label = string.IsNullOrEmpty(button.Text) ? button.Name.ToString() : button.Text;
            var before = ui.Adapter.CurrentState;

            try
            {
                await player.ClickControl(button, $"[{panel}] {label}");
                clicked++;
            }
            catch (ObjectDisposedException)
            {
                clicked++; // the click worked so well it freed its own button
            }
            catch (InvalidOperationException)
            {
                continue; // unreachable — the sweeps own that claim, not this one
            }

            Describe(before, ui.Adapter.CurrentState, panel, label, log);
        }

        return clicked;
    }

    /// <summary>Record what actually CHANGED, so a failure reads as a play-by-play instead of a click list.
    /// State diffing rather than event sniffing: it catches an effect no matter which event carried it.</summary>
    private static void Describe(GameState before, GameState after, string panel, string label, List<string> log)
    {
        var boughtMaterial = after.Player.Materials.Sum(kv => kv.Value) > before.Player.Materials.Sum(kv => kv.Value);
        var crafted = after.Items.Values.Count(i => i.PlayerCrafted) > before.Items.Values.Count(i => i.PlayerCrafted);
        var shelved = after.Player.Shelf.Count > before.Player.Shelf.Count;
        var goldMoved = after.Player.Gold != before.Player.Gold;

        if (boughtMaterial)
        {
            log.Add($"[{panel}] {label} -> bought material");
        }

        if (crafted)
        {
            log.Add($"[{panel}] {label} -> CRAFTED an item");
        }

        if (shelved)
        {
            log.Add($"[{panel}] {label} -> shelved an item");
        }

        if (goldMoved && !boughtMaterial)
        {
            log.Add($"[{panel}] {label} -> gold {before.Player.Gold} -> {after.Player.Gold}");
        }
    }

    /// <summary>Is this on-screen label a real thing to DO, rather than a way to end the turn or open a
    /// reference screen? Used only for the dead-end check, so the bar is "would a player call this a move".</summary>
    private static bool IsRealVerb(string label) =>
        !label.Contains("bell", StringComparison.OrdinalIgnoreCase) &&
        !label.Contains("Send them off", StringComparison.OrdinalIgnoreCase) &&
        !label.Contains("Snuff", StringComparison.OrdinalIgnoreCase) &&
        !label.Contains("press deeper", StringComparison.OrdinalIgnoreCase) &&
        !label.Contains("Close the vigil", StringComparison.OrdinalIgnoreCase) &&
        !label.Contains("Advance", StringComparison.OrdinalIgnoreCase) &&
        !label.Contains("Lower them", StringComparison.OrdinalIgnoreCase);
}
#endif
