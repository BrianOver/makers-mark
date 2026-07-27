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
/// Automated 3D CLICK-THROUGH playtest — plays the real Godot client the way a PLAYER does: it
/// opens each panel and PRESSES the actual action buttons (through their <c>Pressed</c> signal, the
/// exact path a mouse click emits), NOT by queuing sim actions on the adapter. Over a full session it
/// clicks every economic / craft / commission / legend verb button it can find, day after day, and
/// records for each: did the click land an action (the adapter's pending queue grew), was the button
/// disabled, or did the click THROW (a player-facing crash — the single most valuable thing this can
/// find). This is "test what a player would actually play," end to end, on the shipped 3D UI.
///
/// <para>Excludes the real-time minigame widgets (hammer/bellows/plunge/brew, reagent picks) — those
/// need frame-driven input and a separate interactive test; clicking them blind would pump the
/// 3D-render-hang path. Everything else a player clicks in the daily loop is exercised here.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class Playtest3dClickThrough
{
    private const int Days = 40;

    /// <summary>Panels a player opens and acts in each day (drawer + the commission/legend surfaces).</summary>
    private static readonly string[] DrawerPanels =
    {
        "Forge", "Shop", "Bounties", "Demand", "Heroes", "Depths", "Tavern", "Progress",
    };

    /// <summary>Button-name prefixes that represent a PLAYER VERB worth clicking (queues an action /
    /// opens a service). Everything else — Close/Cancel/Skip/Undo/Hold, clock controls, and the
    /// real-time minigame widgets — is deliberately skipped.</summary>
    // NOTE: OpenCounter / Present / Suggest are deliberately excluded — the stepped counter service
    // is a Morning SUB-FLOW that holds the phase open until CloseCounter; blind-clicking Open without
    // running present→haggle→close stalls the day. The counter needs a dedicated flow test.
    private static readonly string[] ClickablePrefixes =
    {
        "BuyMat_", "Craft_", "Unlock_", "PostBounty", "Stock", "Price",
        "Accept", "Decline", "Honor", "Reforge", "Send", "Recall", "HeroCard_",
    };

    private static readonly string[] SkipExact =
    {
        "ProvenanceClose", "CloseLedger", "ForecastClose", "BestiaryClose", "CommissionClose",
        "LegendsWallClose", "CampHold", "ForgeCeremonySkip", "ForgeMinigameCancel", "BrewCancel",
        "BrewSubmit", "BrewUndo", "HammerStrike", "Bellows", "Plunge",
    };

    private sealed record ClickOutcome(string Panel, string Button, string Result);

    [TestCase]
    public void PlayTheClient_ByClicking_EveryVerbButton_AcrossAFullSession()
    {
        var ui = MountMainUi();
        var outcomes = new List<ClickOutcome>();
        var crashes = new List<string>();
        var rejections = new Dictionary<string, int>();
        var verbsClickedOk = new HashSet<string>();
        var itemsCraftedClicks = 0;

        try
        {
            for (var day = 0; day < Days; day++)
            {
                AdvanceToPhase(ui, DayPhase.Morning);

                foreach (var panel in DrawerPanels)
                {
                    try
                    {
                        ui.Drawer.Open(panel);
                    }
                    catch (Exception ex)
                    {
                        crashes.Add($"OPEN {panel}: {ex.GetType().Name}: {Trim(ex.Message)}");
                        continue;
                    }

                    var host = HostFor(ui, panel);
                    if (host is null)
                    {
                        continue;
                    }

                    // Snapshot the actionable, ENABLED buttons up front — pressing mutates the tree,
                    // so we resolve the list before clicking any (and re-find each by name at press
                    // time in case a rebuild invalidated the node).
                    var names = EnabledClickableButtonNames(host);
                    foreach (var name in names)
                    {
                        var before = ui.Adapter.PendingActions.Count;
                        try
                        {
                            var btn = host.FindChild(name, recursive: true, owned: false) as Button;
                            if (btn is null || btn.Disabled)
                            {
                                continue; // gated off after a prior click this pass
                            }

                            btn.EmitSignal(BaseButton.SignalName.Pressed);
                        }
                        catch (Exception ex)
                        {
                            var msg = $"{panel}/{name}: {ex.GetType().Name}: {Trim(ex.Message)}";
                            crashes.Add(msg);
                            outcomes.Add(new ClickOutcome(panel, name, "THREW"));
                            continue;
                        }

                        var landed = ui.Adapter.PendingActions.Count > before;
                        outcomes.Add(new ClickOutcome(panel, name, landed ? "queued action" : "no-op"));
                        if (landed)
                        {
                            verbsClickedOk.Add(VerbOf(name));
                            if (name.StartsWith("Craft_", StringComparison.Ordinal))
                            {
                                itemsCraftedClicks++;
                            }
                        }
                    }
                }

                // Apply everything the day's clicks queued, phase by phase, collecting the sim's
                // REJECTIONS — the proof of WHY a click did or didn't do anything (e.g. a Buy click
                // that bounces for lack of gold, which is why craft never enables).
                try
                {
                    var ticks = 0;
                    do
                    {
                        ui.Adapter.AdvancePhase();
                        foreach (var r in ui.Adapter.LastRejections)
                        {
                            var key = $"{r.Action.GetType().Name.Replace("Action", string.Empty)}: {Trim(r.Reason)}";
                            rejections[key] = rejections.GetValueOrDefault(key) + 1;
                        }

                        if (++ticks > MaxPhasesPerDay)
                        {
                            break;
                        }
                    }
                    while (ui.Adapter.CurrentState.Phase != DayPhase.Morning);
                }
                catch (Exception ex)
                {
                    crashes.Add($"ADVANCE day {day}: {ex.GetType().Name}: {Trim(ex.Message)}");
                    break;
                }
            }

            var state = ui.Adapter.CurrentState;
            WriteReport(BuildReport(state, outcomes, crashes, rejections, verbsClickedOk, itemsCraftedClicks));

            // The core assertion a player cares about: clicking through the whole UI never crashed.
            AssertThat(crashes).OverrideFailureMessage(
                "Clicking real UI buttons threw:\n  " + string.Join("\n  ", crashes)).IsEmpty();
            AssertThat(state.Day >= Days).IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>The same click-through, but from the INTENDED new-player start — a campaign with a
    /// chosen profession + starter copper (<c>NewCampaign(seed, blacksmith)</c>), which the default
    /// <c>SimAdapter(seed)</c> the client mounts with does NOT give. Answers: does the real onboarding
    /// start let a clicking player actually craft, or is the forge dead on arrival regardless?</summary>
    [TestCase]
    public void PlayFromIntendedStart_WithStarterCopper_CanThePlayerActuallyCraft()
    {
        var starter = new GodotClient.SimAdapter(
            GameSim.GameComposition.NewCampaign(2026UL, GameSim.Professions.ProfessionRegistry.BlacksmithId));
        var ui = MountMainUi(starter);
        var craftsLanded = 0;
        try
        {
            for (var day = 0; day < Days; day++)
            {
                AdvanceToPhase(ui, DayPhase.Morning);
                ui.Drawer.Open("Forge");
                foreach (var name in EnabledClickableButtonNames(ui.Forge)
                             .Where(n => n.StartsWith("Craft_", StringComparison.Ordinal)))
                {
                    var before = ui.Adapter.PendingActions.Count;
                    var btn = ui.Forge.FindChild(name, recursive: true, owned: false) as Button;
                    if (btn is null || btn.Disabled)
                    {
                        continue;
                    }

                    btn.EmitSignal(BaseButton.SignalName.Pressed);
                    if (ui.Adapter.PendingActions.Count > before)
                    {
                        craftsLanded++;
                    }
                }

                AdvanceDay(ui, 1);
            }

            var state = ui.Adapter.CurrentState;
            var crafted = state.Items.Values.Count(i => i.PlayerCrafted);
            var report = new StringBuilder();
            report.AppendLine("# 3D Click-Through — INTENDED start (profession + starter copper)");
            report.AppendLine();
            report.AppendLine($"- Craft-button clicks that landed: **{craftsLanded}**");
            report.AppendLine($"- Player-crafted items in world: **{crafted}**");
            report.AppendLine($"- Final gold: {state.Player.Gold}");
            report.AppendLine($"- Arc act: {state.Arc.Act}; deepest floor: {(state.Heroes.Values.Any() ? state.Heroes.Values.Max(h => h.DeepestFloorReached) : 0)}");
            report.AppendLine();
            report.AppendLine(crafted > 0
                ? "**Verdict: the intended (profession-chosen) start CAN craft via 3D clicks — the day-1 "
                  + "soft-lock is specific to the starter-copper-less `SimAdapter(seed)` the client mounts with.**"
                : "**Verdict: STILL cannot craft even with starter copper — the 3D Forge craft path is "
                  + "broken beyond the starting-stock issue.**");
            WriteReport(report.ToString(), envVar: "PLAYTEST_3D_STARTER_OUT");

            AssertThat(state.Day >= Days).IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    private static Control? HostFor(MainUi ui, string panel) => panel switch
    {
        "Forge" => ui.Forge,
        "Shop" => ui.Shop,
        "Bounties" => ui.Bounties,
        "Demand" => ui.Demand,
        "Heroes" => ui.Heroes,
        "Depths" => ui.Depths,
        "Tavern" => ui.Tavern,
        "Progress" => ui.Progress,
        _ => null,
    };

    private static List<string> EnabledClickableButtonNames(Node root)
    {
        var names = new List<string>();
        var stack = new Stack<Node>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node is Button b && !b.Disabled)
            {
                var n = b.Name.ToString();
                if (!SkipExact.Contains(n) && ClickablePrefixes.Any(p => n.StartsWith(p, StringComparison.Ordinal)))
                {
                    names.Add(n);
                }
            }

            foreach (var child in node.GetChildren())
            {
                stack.Push(child);
            }
        }

        return names.Distinct().ToList();
    }

    private static string VerbOf(string buttonName)
    {
        var underscore = buttonName.IndexOf('_');
        return underscore > 0 ? buttonName[..underscore] : buttonName;
    }

    private static string Trim(string s) => s.Length > 160 ? s[..160] : s;

    private static string BuildReport(
        GameState state, List<ClickOutcome> outcomes, List<string> crashes,
        Dictionary<string, int> rejections, HashSet<string> verbsClickedOk, int itemsCraftedClicks)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Maker's Mark — 3D CLICK-THROUGH Playtest");
        sb.AppendLine();
        sb.AppendLine($"Played the real Godot client for {Days} days by PRESSING actual UI buttons "
            + "(the player's click path), across every panel, every Morning. Records what each click did.");
        sb.AppendLine();

        sb.AppendLine("## Crashes (clicks that threw)");
        sb.AppendLine();
        if (crashes.Count == 0)
        {
            sb.AppendLine("- none — no button click threw across the whole session.");
        }
        else
        {
            foreach (var c in crashes)
            {
                sb.AppendLine($"- **{c}**");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Verbs a player successfully drove by clicking");
        sb.AppendLine();
        sb.AppendLine(verbsClickedOk.Count == 0
            ? "- (none landed an action)"
            : "- " + string.Join(", ", verbsClickedOk.OrderBy(v => v)));

        sb.AppendLine();
        sb.AppendLine("## Why clicks didn't land — sim rejections (the mechanism)");
        sb.AppendLine();
        if (rejections.Count == 0)
        {
            sb.AppendLine("- (no actions were rejected)");
        }
        else
        {
            sb.AppendLine("| times rejected | action: reason |");
            sb.AppendLine("|---|---|");
            foreach (var (key, count) in rejections.OrderByDescending(kv => kv.Value).Take(25))
            {
                sb.AppendLine($"| {count} | {key.Replace("|", "\\|")} |");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Click outcomes by (panel, button) — aggregated");
        sb.AppendLine();
        sb.AppendLine("| panel | button | result | count |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var g in outcomes
            .GroupBy(o => (o.Panel, o.Button, o.Result))
            .OrderBy(g => g.Key.Panel, StringComparer.Ordinal).ThenBy(g => g.Key.Button, StringComparer.Ordinal))
        {
            sb.AppendLine($"| {g.Key.Panel} | {g.Key.Button} | {g.Key.Result} | {g.Count()} |");
        }

        sb.AppendLine();
        sb.AppendLine("## End state after a fully clicked-through session");
        sb.AppendLine();
        var deepest = state.Heroes.Values.Any() ? state.Heroes.Values.Max(h => h.DeepestFloorReached) : 0;
        sb.AppendLine($"- Day reached: {state.Day}");
        sb.AppendLine($"- Arc act: **{state.Arc.Act}**");
        sb.AppendLine($"- Deepest floor any hero reached: **{deepest}** (Act III needs floor 5)");
        sb.AppendLine($"- Craft buttons that landed a craft: {itemsCraftedClicks}");
        sb.AppendLine($"- Player-crafted items in world: {state.Items.Values.Count(i => i.PlayerCrafted)}");
        sb.AppendLine($"- Final gold: {state.Player.Gold}");
        sb.AppendLine($"- Heroes ever / alive: {state.Heroes.Count} / {state.Heroes.Values.Count(h => h.Alive)}");
        return sb.ToString();
    }

    private static void WriteReport(string content, string envVar = "PLAYTEST_3D_CLICK_OUT")
    {
        var path = System.Environment.GetEnvironmentVariable(envVar);
        if (string.IsNullOrWhiteSpace(path))
        {
            GD.Print("=== 3D CLICK-THROUGH REPORT (set PLAYTEST_3D_CLICK_OUT to write a file) ===");
            GD.Print(content);
            return;
        }

        try
        {
            System.IO.File.WriteAllText(path, content);
            GD.Print($"3D click-through report written: {path}");
        }
        catch (Exception ex)
        {
            GD.Print($"click-through report write failed ({ex.Message}); dumping:");
            GD.Print(content);
        }
    }
}
#endif
