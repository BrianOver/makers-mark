#if GDUNIT_TESTS
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using GameSim.Contracts;
using GameSim.Kernel;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// The same readability + reachability claims <c>HumanPlaytestTests</c> makes about the nine drawer panels,
/// applied to every OTHER surface in the game.
///
/// <para><b>Why this exists.</b> The drawer sweep found three real bugs on its first run — the Depths panel
/// 124px too wide, the Demand panel's growing chip row, and the Shop's completely dead "Open Counter"
/// button. It covered nine surfaces. The game has eight more that nothing was sweeping at all: the Ledger,
/// Forecast, Commissions and Legends modals, the Bestiary, the Scrying Mirror, the Chronicle, and the
/// building interiors. Owner, bluntly: "you need to test the whole game lol".</para>
///
/// <para><b>Opened the way a player opens them.</b> Where the HUD has a real button (Ledger, Forecast,
/// Commissions, Legends) the sweep clicks it through <see cref="HumanPlayer"/>. The contextual surfaces
/// (Bestiary, Mirror, Chronicle, Interior) have no HUD button — a hero click or a phase beat raises them —
/// so those are shown via the same public method their real trigger calls. That is a seam, and a defensible
/// one: the claim under test is "once this surface is up, is it usable", not "does its trigger fire", which
/// belongs to whatever raises it.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class WholeGameSweepTests
{
    /// <summary>A surface, how to raise it, and how to read its rect. Data rather than one test each, so a
    /// new modal is one line here instead of a test somebody forgets to write.</summary>
    private sealed record Surface(string Name, Func<MainUi, Task> Open, Func<MainUi, Control> Node);

    private static IReadOnlyList<Surface> Surfaces =>
    [
        // ── Raised by a real HUD tray button: clicked for real. ──
        new("Ledger", async ui => await ClickTray(ui, "OpenLedger"), ui => ui.Ledger),
        new("Forecast", async ui => await ClickTray(ui, "OpenForecast"), ui => ui.Forecast),
        new("Commissions", async ui => await ClickTray(ui, "OpenCommissions"), ui => ui.Commissions),
        new("Legends", async ui => await ClickTray(ui, "OpenLegends"), ui => ui.Legends),

        // ── Contextual: no HUD button exists, so raised through the same call their trigger makes. ──
        new("Bestiary", ui => { ui.Bestiary.ShowAll(); return Task.CompletedTask; }, ui => ui.Bestiary),
        new("Mirror", ui => { ui.Mirror.ShowMirror(); return Task.CompletedTask; }, ui => ui.Mirror),
    ];

    /// <summary>
    /// Dismiss <paramref name="node"/> the way a player would: Escape first, then hunt for a close control.
    /// Returns whether it actually went away.
    ///
    /// <para>Escape first because it is the one affordance that ought to work on every modal in the game.
    /// It currently does not — the sweep found that Ledger, Forecast, Commissions and Legends all survive
    /// Escape and only close via their own ✕ — which is worth knowing and is recorded rather than asserted,
    /// since "every modal closes on Escape" is a design call, not a bug I should decide alone.</para>
    /// </summary>
    private static async Task<bool> Dismiss(MainUi ui, HumanPlayer player, Control node)
    {
        player.Tap(Key.Escape);
        await player.Frames(6);

        if (!node.IsVisibleInTree())
        {
            return true;
        }

        var closer = player.ClickableButtons(node).FirstOrDefault(b =>
            b.Name.ToString().Contains("Close", StringComparison.OrdinalIgnoreCase) ||
            b.Text.Contains("Close", StringComparison.OrdinalIgnoreCase) ||
            b.Text.Contains('✕') || b.Text.Contains('×'));

        if (closer is not null)
        {
            await player.ClickControl(closer, $"close {node.Name}");
            await player.Frames(6);
        }

        return !node.IsVisibleInTree();
    }

    private static async Task ClickTray(MainUi ui, string buttonName)
    {
        var player = new HumanPlayer(ui);
        var button = ui.FindChild(buttonName, recursive: true, owned: false) as Button
            ?? throw new InvalidOperationException($"No HUD tray button named {buttonName}.");

        await player.ClickControl(button, $"HUD tray \"{buttonName}\"");
    }

    /// <summary>
    /// Every surface must fit on screen, keep its controls apart, and stay inside the window.
    ///
    /// <para>One test over all of them rather than one test each: the failure message names the surface, and
    /// a single mount-and-sweep is far cheaper in CI than eight mounts — which matters, because the engine
    /// job already runs close to its deadline.</para>
    /// </summary>
    /// <summary>U3 (tutorial-revamp plan, §11.13): the four HUD-tray-routed surfaces in
    /// <see cref="Surfaces"/> are now gated tray books — a totally fresh mount would fail all four
    /// of them closed, dropping this sweep to 2-of-6 (Bestiary + Mirror only) and tripping its own
    /// <c>swept &gt;= 4</c> floor for a reason unrelated to what this sweep actually claims (every
    /// surface is readable and non-overlapping once raised). Mounted with each gate's own fact
    /// already true so the sweep still measures readability, not gating — <c>SurfaceUnlocksTests</c>
    /// owns the gate claims themselves.</summary>
    private static GameState AllTraySurfacesUnlockedWorld() =>
        GameFactory.NewGame(2026) with
        {
            Phase = DayPhase.Evening,
            EventLog = ImmutableList.Create<GameEvent>(
                new PartyDeparted(ImmutableList.Create(new HeroId(1)), TargetFloor: 1),
                new CommissionPosted(new HeroId(1), ItemSlot.Weapon, QualityGrade.Common, DeadlineDay: 5, PremiumGold: 10),
                new AttributionBeatEvent(BeatType.KillingBlow, new ItemId(1), new HeroId(1), Floor: 1, Detail: "sweep fixture")),
        };

    [TestCase]
    public async Task EverySurface_IsReadableAndDoesNotOverlapItself()
    {
        var ui = MountMainUi(new SimAdapter(AllTraySurfacesUnlockedWorld()));
        try
        {
            var player = new HumanPlayer(ui);

            // Settle the HUD before clicking anything. Without this the tray buttons are still at their
            // pre-layout rects and a click lands on whatever happens to be at the top-left corner — the
            // first run of this sweep "found" the Ledger button blocked by the DayChip at (23,17), which was
            // simply two mid-layout rectangles being compared.
            await player.WaitForLayout(ui);

            var problems = new List<string>();
            var swept = 0;

            foreach (var surface in Surfaces)
            {
                await surface.Open(ui);
                var node = surface.Node(ui);

                // TrySettle, not WaitForLayout: BestiaryPanel animates forever by design (an idle breath in
                // its own _Process), so demanding a settled layout aborted the whole sweep on it. Its
                // geometry is then approximate, which is the right trade for measuring it at all.
                await player.TrySettleLayout(node);

                if (!node.IsVisibleInTree())
                {
                    // Not a failure of THIS claim: several surfaces legitimately decline to open with
                    // nothing to show (an empty Legends wall on day 1). Recorded so a surface that never
                    // opens cannot masquerade as one that opened cleanly.
                    problems.Add($"[{surface.Name}] did not become visible when raised — nothing was swept");
                    continue;
                }

                swept++;
                problems.AddRange(player.ClippedText().Select(p => $"[{surface.Name}] {p}"));
                problems.AddRange(player.OverlappingSiblings(node).Select(p => $"[{surface.Name}] {p}"));
                problems.AddRange(player
                    .TooWideFor(node, ui.GetViewport().GetVisibleRect().Size.X + 1f)
                    .Select(p => $"[{surface.Name}] {p}"));

                // Close it before raising the next one. A modal's own veil covers the HUD tray, so leaving it
                // up makes every subsequent tray click fail — which is the veil working, not a bug, and it
                // made the first run of this sweep report the Forecast button as unreachable.
                await Dismiss(ui, player, node);
            }

            AssertThat(swept)
                .OverrideFailureMessage(
                    $"Only {swept} of {Surfaces.Count} surfaces actually opened, so this proved almost " +
                    $"nothing:\n  {string.Join("\n  ", problems)}")
                .IsGreaterEqual(4);

            AssertThat(problems)
                .OverrideFailureMessage(
                    $"Swept {swept} surfaces; these are unreadable or self-overlapping:\n  " +
                    string.Join("\n  ", problems))
                .IsEmpty();
        }
        finally { Unmount(ui); }
    }

    /// <summary>
    /// No always-on-top widget may float over an open panel.
    ///
    /// <para>Found by rendering the game and looking at it: the journey dock (<c>PipDock</c>) is governed by
    /// PHASE alone, so during Expedition/Camp/Deep it slid in over whatever the player had opened — the
    /// screenshot showed it sitting on the Depths panel, covering the Gloomwood venue card. Redundant as well
    /// as overlapping, since the Depths panel shows that same party in more detail.</para>
    ///
    /// <para>Checked during an expedition phase specifically, because that is the only time the dock wants to
    /// be visible — running this in Morning would pass while proving nothing.</para>
    /// </summary>
    /// <summary>
    /// Pumps frames until the journey dock has stopped moving, instead of waiting a fixed frame count.
    /// <para>
    /// The dock slides over <c>SlideSeconds</c> — a duration in SECONDS — and this test used to wait 30
    /// FRAMES for it. That only outlasts the slide below a certain frame rate, and this suite disables
    /// SubViewport rendering, so frames can come much faster than real time; a fast run would sample the
    /// dock mid-slide and report an overlap that resolves a moment later. The sibling bug in
    /// <c>CameraFocusBeatTests</c> failed CI for exactly this reason.
    /// </para>
    /// </summary>
    private static async Task SettleDock(HumanPlayer player, MainUi ui, int maxFrames = 600)
    {
        var previous = ui.Pip.GetGlobalRect();
        var still = 0;

        for (var frame = 0; frame < maxFrames && still < 2; frame++)
        {
            await player.Frames(1);
            var now = ui.Pip.GetGlobalRect();
            still = now == previous ? still + 1 : 0;
            previous = now;
        }
    }

    [TestCase]
    public async Task NoFloatingWidget_CoversAnOpenPanel()
    {
        var ui = MountMainUi();
        try
        {
            var player = new HumanPlayer(ui);
            await player.WaitForLayout(ui);

            // Into an expedition phase, where the dock wants the corner.
            AdvanceToPhase(ui, DayPhase.Expedition);
            ui.RefreshAll();
            await SettleDock(player, ui);

            AssertThat(ui.Adapter.CurrentState.Phase)
                .OverrideFailureMessage("Never reached an expedition phase, so the dock was never asked to show.")
                .IsEqual(DayPhase.Expedition);

            var overlaps = new List<string>();
            foreach (var panel in new[] { "Depths", "Forge", "Shop" })
            {
                ui.OpenPanel(panel);
                await player.TrySettleLayout(ui.Drawer.CurrentContent!);
                await SettleDock(player, ui);

                var content = ui.Drawer.CurrentContent!;
                if (ui.Pip.Visible && ui.Pip.GetGlobalRect().Intersects(content.GetGlobalRect()))
                {
                    overlaps.Add(
                        $"[{panel}] PipDock at {ui.Pip.GetGlobalRect()} covers the open panel at " +
                        $"{content.GetGlobalRect()} (Docked={ui.Pip.Docked}, Suppressed={ui.Pip.Suppressed})");
                }
            }

            AssertThat(overlaps)
                .OverrideFailureMessage(
                    "An always-on-top widget is drawn over an open panel's content:\n  " +
                    string.Join("\n  ", overlaps) +
                    "\n\nGate it on the same \"a surface owns the screen\" predicate the objective chip uses " +
                    "(MainUi.UpdateEngaged -> PipDock.Suppressed).")
                .IsEmpty();
        }
        finally { Unmount(ui); }
    }

    /// <summary>
    /// Every surface must be dismissible, and dismissing it must give the game back.
    ///
    /// <para>A modal you cannot close is a softlock, and it is the single worst bug a surface can have —
    /// worse than being ugly or cut off, because the run is over. Nothing tested this for any of them.</para>
    /// </summary>
    [TestCase]
    public async Task EverySurface_CanBeDismissed_AndGivesTheGameBack()
    {
        // U3 (tutorial-revamp plan, §11.13): same reason EverySurface_IsReadableAndDoesNotOverlapItself
        // above mounts AllTraySurfacesUnlockedWorld() rather than a bare campaign — Ledger/Forecast/
        // Commissions/Legends are now SurfaceUnlocks-gated tray books, so a fresh mount's four HUD-tray
        // buttons are Disabled and a real click on them (ClickTray, via HumanPlayer) correctly refuses,
        // same as it would for an actual player. This test needs every gate already earned so it can
        // measure dismissal, not gating (SurfaceUnlocksTests owns the gate claims themselves).
        var ui = MountMainUi(new SimAdapter(AllTraySurfacesUnlockedWorld()));
        try
        {
            var player = new HumanPlayer(ui);
            await player.WaitForLayout(ui); // see the sweep above — never click a pre-layout rect
            var stuck = new List<string>();

            foreach (var surface in Surfaces)
            {
                await surface.Open(ui);
                var node = surface.Node(ui);
                await player.TrySettleLayout(node);

                if (!node.IsVisibleInTree())
                {
                    continue; // declined to open (nothing to show) — covered by the sweep above
                }

                if (!await Dismiss(ui, player, node))
                {
                    stuck.Add(
                        $"[{surface.Name}] survived Escape AND had no close control that dismissed it. " +
                        $"On screen: [{string.Join(" | ", player.ClickableLabels(node))}]");
                }
            }

            AssertThat(stuck)
                .OverrideFailureMessage(
                    "These surfaces cannot be dismissed — each one is a softlock:\n  " +
                    string.Join("\n  ", stuck))
                .IsEmpty();
        }
        finally { Unmount(ui); }
    }
}
#endif
