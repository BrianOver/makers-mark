#if GDUNIT_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using GdUnit4;
using GameSim.Contracts;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// Automated 3D-CLIENT playtest recorder — drives the REAL <c>MainUi</c> (the shipped Godot client,
/// not the CLI) through a full session and records what the 3D surface actually presents to the
/// player: which action controls each panel exposes (the 3D verb-reachability map — the 3D analogue
/// of the CLI decision-surface's dead-verb table), how the HUD arc chip progresses (does the
/// campaign ever reach Act III on screen?), and the advisor objective chip. The decisive question it
/// answers, which no CLI run can: <b>does the 3D client even have a button for the on-theme verbs</b>
/// (reforge a fallen hero's blade, haggle at the counter, send a salve, honor the stone), or are they
/// sim-and-CLI-only with no 3D surface?
///
/// <para>Writes a markdown report to <c>PLAYTEST_3D_OUT</c> (env) if set, else <c>GD.Print</c>s it.
/// Property/tree inspection only — opens panels and enumerates their <see cref="Button"/> descendants;
/// no viewport frame-pump (3D-render-hang rule). A documentation tool, not a balance gate.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class Playtest3dRecorder
{
    private const int Days = 90;

    /// <summary>The drawer panels (opened via <c>Drawer.Open</c> to populate before enumeration).</summary>
    private static readonly string[] DrawerPanels =
    {
        "Forge", "Shop", "Heroes", "Tavern", "Depths", "Bounties", "Demand", "HeroCards", "Progress",
    };

    /// <summary>The HUD buttons that open surfaces / drive the clock — the always-visible verb rail.</summary>
    private static readonly string[] HudButtons =
    {
        "OpenLedger", "OpenForecast", "OpenCommissions", "OpenLegends", "OpenDemand",
        "OpenHeroCards", "OpenProgress",
    };

    [TestCase]
    public void DriveRealMainUi_FullRun_RecordThe3dChoiceSurface()
    {
        var report = new StringBuilder();
        report.AppendLine("# Maker's Mark — 3D-Client Playtest Recording");
        report.AppendLine();
        report.AppendLine($"Drives the REAL `MainUi` (shipped Godot client) for {Days} days, seed 2026, "
            + "and records the actual 3D action surface. Property/tree inspection, no render pump.");
        report.AppendLine();

        var ui = MountMainUi();
        var maxAct = "ActI";
        var actAtDay = new List<string>();
        string? advisorChip = null;
        try
        {
            // Snapshot the HUD arc chip as the run progresses (does the campaign reach Act III on screen?).
            for (var chunk = 0; chunk < Days / 10; chunk++)
            {
                AdvanceDay(ui, 10);
                var act = ui.Adapter.CurrentState.Arc.Act.ToString();
                actAtDay.Add($"day {ui.Adapter.CurrentState.Day}: {act}");
                if (string.CompareOrdinal(act, maxAct) > 0)
                {
                    maxAct = act;
                }
            }

            // The advisor objective chip — the 3D client's single guidance surface. Best-effort find.
            advisorChip = TryReadObjectiveChip(ui);

            // --- HUD verb rail: which top-level buttons exist + are enabled ---
            report.AppendLine("## HUD button rail (always-visible verbs)");
            report.AppendLine();
            report.AppendLine("| button | present | enabled |");
            report.AppendLine("|---|---|---|");
            foreach (var name in HudButtons)
            {
                var btn = TryFind<Button>(ui, name);
                report.AppendLine($"| {name} | {(btn is null ? "**NO**" : "yes")} | {(btn is null ? "—" : (btn.Disabled ? "disabled" : "yes"))} |");
            }

            // Populate the drawer panels (Open triggers their bind) before enumerating controls.
            foreach (var name in DrawerPanels)
            {
                try
                {
                    ui.Drawer.Open(name);
                }
                catch
                {
                    // best-effort populate; enumeration below still reports the static scaffold
                }
            }

            // --- Per-panel action surface: enumerate the action buttons EVERY panel exposes ---
            // Uses the MainUi panel PROPERTIES directly (no node-name guessing), incl. the non-drawer
            // surfaces where the on-theme verbs live (Camp = send/recall, Commissions = accept/decline,
            // Legends = honor-memorial/reforge). A verb with no button anywhere below has no 3D surface.
            report.AppendLine();
            report.AppendLine("## Per-panel action controls (the 3D verb surface)");
            report.AppendLine();
            var panelHosts = new (string Name, Control Host)[]
            {
                ("Forge", ui.Forge), ("Shop", ui.Shop), ("Heroes", ui.Heroes), ("Tavern", ui.Tavern),
                ("Depths", ui.Depths), ("Bounties", ui.Bounties), ("Demand", ui.Demand), ("HeroCards", ui.HeroCards),
                ("Ledger", ui.Ledger), ("Forecast", ui.Forecast), ("Bestiary", ui.Bestiary),
                ("Commissions", ui.Commissions), ("Legends", ui.Legends), ("Camp", ui.Camp), ("Progress", ui.Progress),
            };
            foreach (var (name, host) in panelHosts)
            {
                report.AppendLine();
                report.AppendLine($"### {name}");
                if (host is null)
                {
                    report.AppendLine("- (panel property is null)");
                    continue;
                }

                var buttons = CollectButtons(host);
                if (buttons.Count == 0)
                {
                    report.AppendLine("- (no action buttons — display-only panel)");
                }
                else
                {
                    foreach (var b in buttons)
                    {
                        report.AppendLine($"- `{b.Name}`{(string.IsNullOrEmpty(b.Text) ? "" : $" — \"{b.Text}\"")}{(b.Disabled ? " *(disabled)*" : "")}");
                    }
                }
            }

            report.AppendLine();
            report.AppendLine("> Note: there is **no Counter/Haggle panel property** on `MainUi` — the stepped "
                + "counter-service verbs (OpenCounter/Present/Suggest/Close/Haggle) have no 3D surface at all.");

            // --- Arc progression on the HUD ---
            report.AppendLine();
            report.AppendLine("## Arc chip progression (HUD)");
            report.AppendLine();
            report.AppendLine($"Highest act reached on screen across {Days} days: **{maxAct}**.");
            report.AppendLine();
            foreach (var line in actAtDay)
            {
                report.AppendLine($"- {line}");
            }

            report.AppendLine();
            report.AppendLine("## Advisor objective chip (the 3D guidance surface)");
            report.AppendLine();
            report.AppendLine(advisorChip is null
                ? "- (objective chip not found by name — needs a node-name check)"
                : $"- End-of-run chip text: \"{advisorChip}\"");

            WriteReport(report.ToString());

            // Sanity: the real client actually ran deep and kept its HUD.
            AssertThat(ui.Adapter.CurrentState.Day >= Days).IsTrue();
            AssertThat(TryFind<Control>(ui, "ActChip")).IsNotNull();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Second pass: drive the SAME real client but with the default player policy actively
    /// queued each phase (craft/stock/buy-ore/talent), so the state-gated surface (craft buttons,
    /// Phase-D sinks, memorials from deaths) lights up — separating "gated in a broke passive run"
    /// from "genuinely has no 3D control." Also the decisive arc test: does active crafting push a
    /// hero to floor 5 and flip the Act chip to III, or does the arc stall even when the player works?</summary>
    [TestCase]
    public void DriveRealMainUi_ActivePolicy_LightsGatedSurface_AndTestsArc()
    {
        var report = new StringBuilder();
        report.AppendLine("# Maker's Mark — 3D-Client Recording (ACTIVE policy)");
        report.AppendLine();
        report.AppendLine($"Drives the REAL `MainUi` for {Days} days, seed 2026, with the default "
            + "`BaselinePlayer` policy queued each phase — so state-gated 3D controls light up.");
        report.AppendLine();

        var ui = MountMainUi();
        var maxAct = "ActI";
        try
        {
            for (var day = 0; day < Days; day++)
            {
                var ticks = 0;
                do
                {
                    foreach (var action in GameSim.Harness.BaselinePlayer.ActionsFor(ui.Adapter.CurrentState))
                    {
                        ui.Adapter.Queue(action);
                    }

                    ui.Adapter.AdvancePhase();
                    var act = ui.Adapter.CurrentState.Arc.Act.ToString();
                    if (string.CompareOrdinal(act, maxAct) > 0)
                    {
                        maxAct = act;
                    }

                    if (++ticks > MaxPhasesPerDay)
                    {
                        break;
                    }
                }
                while (ui.Adapter.CurrentState.Phase != DayPhase.Morning);
            }

            var state = ui.Adapter.CurrentState;
            var deepest = state.Heroes.Values.Any() ? state.Heroes.Values.Max(h => h.DeepestFloorReached) : 0;
            var memorials = state.Drama.Memorials.Count;
            var itemsCrafted = state.Items.Values.Count(i => i.PlayerCrafted);

            report.AppendLine("## Outcome of an ACTIVELY-PLAYED 90-day 3D run");
            report.AppendLine();
            report.AppendLine($"- Highest arc act reached on the HUD: **{maxAct}**");
            report.AppendLine($"- Deepest floor any hero reached: **{deepest}** (Act III needs floor 5)");
            report.AppendLine($"- Player-crafted items over the run: **{itemsCrafted}**");
            report.AppendLine($"- Memorials raised (fallen heroes): **{memorials}**");
            report.AppendLine($"- Final gold: **{state.Player.Gold}**");
            report.AppendLine();

            // Did the gated Forge surface light up? Count enabled craft buttons now.
            ui.Drawer.Open("Forge");
            var craftButtons = CollectButtons(ui.Forge).Where(b => b.Name.ToString().StartsWith("Craft_")).ToList();
            var enabledCraft = craftButtons.Count(b => !b.Disabled);
            report.AppendLine($"- Forge craft buttons enabled (active run): **{enabledCraft} / {craftButtons.Count}**");

            // Did any Phase-D sink control surface?
            var sinkNames = new[] { "UpgradeForge", "BuySupply", "Masterwork", "CommissionLegendary", "BuyForgeSupply" };
            var sinksFound = sinkNames.Where(n => TryFind<Button>(ui, n) is not null).ToList();
            report.AppendLine($"- Phase-D sink buttons present: {(sinksFound.Count == 0 ? "**NONE**" : string.Join(", ", sinksFound))}");

            WriteReport(report.ToString(), "PLAYTEST_3D_ACTIVE_OUT");

            AssertThat(state.Day >= Days).IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    private static string? TryReadObjectiveChip(MainUi ui)
    {
        foreach (var name in new[] { "ObjectiveChip", "ObjectiveTracker", "AdvisorChip", "ObjectiveLabel" })
        {
            var node = ui.FindChild(name, recursive: true, owned: false);
            if (node is not null)
            {
                var text = RenderedText(node).Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text.Length > 200 ? text[..200] : text;
                }
            }
        }

        return null;
    }

    private static List<Button> CollectButtons(Node root)
    {
        var found = new List<Button>();
        var stack = new Stack<Node>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node is Button b && !string.IsNullOrEmpty(b.Name))
            {
                found.Add(b);
            }

            foreach (var child in node.GetChildren())
            {
                stack.Push(child);
            }
        }

        return found
            .GroupBy(b => b.Name.ToString())
            .Select(g => g.First())
            .OrderBy(b => b.Name.ToString(), StringComparer.Ordinal)
            .ToList();
    }

    private static T? TryFind<T>(Node root, string name) where T : Node =>
        root.FindChild(name, recursive: true, owned: false) as T;

    private static void WriteReport(string content, string envVar = "PLAYTEST_3D_OUT")
    {
        var path = System.Environment.GetEnvironmentVariable(envVar);
        if (string.IsNullOrWhiteSpace(path))
        {
            GD.Print("=== 3D PLAYTEST REPORT (set PLAYTEST_3D_OUT to write to a file) ===");
            GD.Print(content);
            return;
        }

        try
        {
            System.IO.File.WriteAllText(path, content);
            GD.Print($"3D playtest report written: {path}");
        }
        catch (Exception ex)
        {
            GD.Print($"3D playtest report write failed ({ex.Message}); dumping to stdout:");
            GD.Print(content);
        }
    }
}
#endif
