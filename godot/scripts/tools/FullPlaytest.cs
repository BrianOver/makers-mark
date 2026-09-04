using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GameSim.Advisor;
using GameSim.Professions;
using GodotClient.Minigames;
using GodotClient.Town2d;
using Godot;

namespace GodotClient.Tools;

/// <summary>
/// FIVE FULL real-launch playthroughs of the shipped client — the build a player actually launches,
/// not a harness. Each run boots the real <c>new_game_select</c> front door, presses the real
/// profession + Begin buttons, mounts the real <see cref="MainUi"/>, then plays a multi-day campaign
/// through real controls: real minigame interaction, every drawer panel opened, the town walked, the
/// raid watched.
///
/// <para><b>Why this exists as well as the earlier real-launch probe it superseded</b> (an orphaned
/// <c>RealPlaytest</c> tool, deleted in the U-T3-11 orphan sweep once nothing launched it anymore):
/// that tool proved the surfaces EXIST. This one asks whether the game is actually alive across a
/// whole campaign — so it adds (a) multiple days per run and multiple runs at different seeds, (b)
/// <b>numeric motion measurement</b>, and (c) a written report of anomalies rather than a pile of
/// screenshots for a human to squint at.</para>
///
/// <para><b>Motion is measured, not eyeballed.</b> A screenshot cannot tell you whether an animation
/// is running — two identical frames look exactly like a working animation that happens to be paused.
/// So <see cref="MotionBurst"/> grabs a rapid series of frames and reports the percentage of pixels
/// that CHANGED between consecutive ones. A burst that reports ~0% on the town means the world is
/// frozen, which is a defect a thousand screenshots would never reveal.</para>
///
/// <para>Must run WINDOWED — <c>--headless</c> uses the dummy driver and captures blank frames:
/// <c>godot --path godot res://fullplaytest.tscn</c>.</para>
/// </summary>
public partial class FullPlaytest : Node
{
    /// <summary>Where shots and the report land. Override with the <c>PLAYTEST_OUT</c> environment
    /// variable; defaults beside the repo so a fresh clone works without editing this file.</summary>
    private static readonly string OutDir =
        (System.Environment.GetEnvironmentVariable("PLAYTEST_OUT") is { Length: > 0 } dir
            ? dir.Replace("\\", "/").TrimEnd('/')
            : ProjectSettings.GlobalizePath("res://../runs/playtest").TrimEnd('/')) + "/";

    /// <summary>Days of real campaign per run — long enough to reach the evening ledger repeatedly
    /// and to let heroes die, level, gossip and restock.</summary>
    private const int DaysPerRun = 8;

    /// <summary>Every drawer panel MainUi routes. Each is opened and shot in every run, so a panel
    /// that throws or renders empty cannot hide.
    ///
    /// <para>"Progress" is deliberately absent: it is a gated Books Tray surface (P2-HONEST-01,
    /// <see cref="GodotClient.Ui.SurfaceUnlocks.Gates"/>) that will not honestly be open this early
    /// in a run (before any bounty has been posted, let alone paid) — calling the panel router
    /// directly here would be exactly the "harness covers the surface through a hole in the wall"
    /// bypass that unit deleted. The final-day capture below reaches it through the real tray
    /// button instead, and reports honestly when the gate never opened that run.</para>
    /// </summary>
    private static readonly string[] AllPanels =
    {
        "Forge", "Shop", "Heroes", "Tavern", "Depths", "Bounties", "Demand", "HeroCards",
    };

    private readonly StringBuilder _report = new();
    private readonly List<string> _anomalies = new();
    private int _run;
    private int _shots;

    public override async void _Ready()
    {
        DevToolAudio.Silence(); // automated runs stay silent — see DevToolAudio
        System.IO.Directory.CreateDirectory(OutDir);
        _report.AppendLine("# Five full playtests — shipped client");
        _report.AppendLine();
        _report.AppendLine($"Days per run: {DaysPerRun}. Panels per run: {AllPanels.Length}.");
        _report.AppendLine("Motion rows report % of pixels changed between consecutive frames — a near-zero");
        _report.AppendLine("figure on a scene that should be alive is a frozen-world defect.");
        _report.AppendLine();

        // Rotate the starting profession so both ACTIVE crafts (blacksmith forge, alchemy brew) are
        // driven with real interaction across the five runs, plus the two passive ones for coverage.
        var professions = new[]
        {
            ProfessionRegistry.BlacksmithId,
            AlchemyProfession.Id,
            ProfessionRegistry.BlacksmithId,
            TanningProfession.Id,
            EngineeringProfession.Id,
        };

        for (_run = 1; _run <= professions.Length; _run++)
        {
            var profession = professions[_run - 1];
            _report.AppendLine($"## Run {_run} — {profession}");
            _report.AppendLine();
            try
            {
                await DriveOneRun(profession);
            }
            catch (Exception ex)
            {
                Note($"RUN {_run} THREW: {ex.GetType().Name}: {ex.Message}");
                _report.AppendLine($"- **run aborted**: `{ex.GetType().Name}: {ex.Message}`");
            }

            _report.AppendLine();
        }

        // Anything Godot itself pushed as a warning or error during these five runs — a rejected
        // action, a failed autosave, a missing resource — BEFORE anomalies is finalized, so the
        // count below and the exit code both reflect it. See ReportEngineDistress's own doc for
        // why this is the whole point of this file: a run that throws and still says "0
        // anomalies" is worse than no harness at all.
        ReportEngineDistress();

        _report.AppendLine("## Anomalies");
        _report.AppendLine();
        if (_anomalies.Count == 0)
        {
            _report.AppendLine("None recorded.");
        }
        else
        {
            foreach (var a in _anomalies)
            {
                _report.AppendLine($"- {a}");
            }
        }

        System.IO.File.WriteAllText(OutDir + "REPORT.md", _report.ToString());
        GD.Print($"[fullplaytest] done — {_shots} shots, {_anomalies.Count} anomalies. Report: {OutDir}REPORT.md");

        // A nonzero anomaly count must be visible WITHOUT reading the report body — a caller (a
        // person, or CI) checking only the process exit code must still see the run was unclean.
        GetTree().Quit(ExitCodeFor(_anomalies.Count));
    }

    /// <summary>The process exit code for <paramref name="anomalyCount"/> anomalies — nonzero
    /// whenever any were found. Extracted so it is unit-testable without booting a scene. Public
    /// like <see cref="MainUi.FriendlyRejection"/> — GodotClient.Tests is a separate assembly with
    /// no InternalsVisibleTo grant.</summary>
    public static int ExitCodeFor(int anomalyCount) => anomalyCount > 0 ? 1 : 0;

    /// <summary>
    /// Fold every distinct <see cref="EngineDistress"/> message recorded so far this process into
    /// <see cref="_anomalies"/>.
    ///
    /// <para><b>Why <see cref="EngineDistress"/> and not Godot's own log file:</b> the first
    /// version of this scanned <c>user://logs/godot.log</c> directly (file logging is confirmed ON
    /// BY DEFAULT for this project — verified empirically, since <c>project.godot</c> is
    /// deny-listed and could not be edited to force it). That measured out as unreadable: this SAME
    /// process cannot open a file Godot's own writer still has open — a real run threw
    /// <c>IOException: being used by another process</c> here, every time. Every
    /// <c>GD.PushWarning</c>/<c>GD.PushError</c> call site this client owns now goes through
    /// <see cref="EngineDistress"/> instead, which has no such dependency.</para>
    /// </summary>
    private void ReportEngineDistress()
    {
        foreach (var group in EngineLogAnomalies.Scan(EngineDistress.Messages))
        {
            Note(group.Count > 1 ? $"{group.Message} (x{group.Count})" : group.Message);
        }
    }

    private async Task DriveOneRun(string profession)
    {
        // ── the real front door ──────────────────────────────────────────────────────────────
        var select = GD.Load<PackedScene>("res://scenes/new_game_select.tscn").Instantiate<NewGameSelect>();
        string? requestedScene = null;
        select.SceneChange = path => requestedScene = path;
        AddChild(select);
        await Settle(8);
        Shot($"r{_run}_00_picker");

        Press(select, $"Pick_{profession}");
        await Settle(8);
        Shot($"r{_run}_01_primer");
        Press(select, "Begin");
        await Settle(4);
        RemoveChild(select);
        select.QueueFree();

        var ui = GD.Load<PackedScene>(requestedScene ?? "res://scenes/panels/main_ui.tscn").Instantiate<MainUi>();
        AddChild(ui);
        await Settle(24);
        Shot($"r{_run}_02_town");

        // ── is the world actually alive? ─────────────────────────────────────────────────────
        await MotionBurst(ui, $"r{_run}_town_idle", "town at rest (ambient life, townsfolk)");

        // Walk with real input actions, and measure that walking moves pixels.
        SetMove(new Vector2(1, 0.25f));
        await MotionBurst(ui, $"r{_run}_town_walk", "town while WALKING (player + camera)");
        SetMove(Vector2.Zero);
        await Settle(4);

        // ── real building interaction ────────────────────────────────────────────────────────
        // NB: the shop's building key is "market", not "shop" — Town2D deliberately kept the old
        // Building3D key vocabulary so FindBuilding stayed a drop-in for existing callers.
        foreach (var building in new[] { "forge", "market", "tavern", "minegate", "noticeboard" })
        {
            try
            {
                ui.Town.FindBuilding(building).RaisePick();
                await Settle(10);
                Shot($"r{_run}_03_click_{building}");

                // U1 (painted-interiors plan; world-and-interiors plan, docs/plans/2026-08-02-004,
                // grew this to four rooms): "forge"/"market"/"tavern"/"minegate" all enter a
                // walkable room instead of opening the drawer directly (R1) — only "noticeboard"
                // still opens its drawer (KTD-2: a plank board has no inside). Walk the room for
                // real — press every station, shot each one, exit — rather than letting the click
                // screenshot above stand in for the whole framework. Generic over
                // Town.InteriorActive, so this block is a no-op only for noticeboard.
                if (ui.Town.InteriorActive)
                {
                    var room = ui.Town.FindInteriorRoom(ui.Town.InteriorVenueKey!);
                    foreach (var station in room.Stations)
                    {
                        try
                        {
                            station.RaisePick();
                            await Settle(8);
                            Shot($"r{_run}_03b_station_{station.Key}");
                        }
                        catch (Exception ex)
                        {
                            Note($"run {_run}: station '{station.Key}' in room '{building}' failed: {ex.GetType().Name}: {ex.Message}");
                        }

                        if (ui.Drawer.IsOpen)
                        {
                            ui.Drawer.Close(); // reset so the next station's press isn't reading a stale open drawer
                            await Settle(6);
                        }

                        // U1 (world-and-interiors plan): the gatehouse's "overlook" (Watch) and the
                        // tavern's "storywall" (Legends) open code-built modals, not the drawer —
                        // close those too, or they'd sit stacked over every subsequent station's shot.
                        if (ui.Mirror.Visible)
                        {
                            ui.Mirror.CloseMirror();
                            await Settle(6);
                        }
                        if (ui.Legends.Visible)
                        {
                            ui.Legends.Close();
                            await Settle(6);
                        }
                    }

                    ui.Town.ExitInterior();
                    await Settle(10);
                    Shot($"r{_run}_03c_room_exit_{building}");
                }
            }
            catch (Exception ex)
            {
                Note($"run {_run}: clicking building '{building}' failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // ── the active craft for this profession, driven for real ────────────────────────────
        if (profession == ProfessionRegistry.BlacksmithId)
        {
            await DriveForge(ui);
        }
        else if (profession == AlchemyProfession.Id)
        {
            await DriveBrew(ui);
        }
        else
        {
            // Tanning and engineering went ACTIVE with U3b, so both overlays are now real surfaces
            // a player can open — driven for the first time here.
            await DriveOtherActiveCraft(ui, profession);
        }

        // ── every panel, every run ───────────────────────────────────────────────────────────
        foreach (var panel in AllPanels)
        {
            try
            {
                ui.OpenPanel(panel);
                await Settle(8);
                var shotName = $"r{_run}_04_panel_{panel.ToLowerInvariant()}";
                var filled = Shot(shotName);
                if (!filled)
                {
                    Note($"run {_run}: panel '{panel}' captured a blank/uniform frame");
                }
            }
            catch (Exception ex)
            {
                Note($"run {_run}: OpenPanel('{panel}') THREW {ex.GetType().Name}: {ex.Message}");
            }
        }

        // ── a full multi-day campaign, watching the raid ─────────────────────────────────────
        var adapter = ui.Adapter;

        // U10 (world-and-interiors plan, KTD-5): captured the FIRST time any day's expedition
        // resolves with at least one survivor, whichever day/phase that turns out to be — NOT
        // hardcoded to "day 1, phase 1". PlayTheDay can legally open a counter session whose close
        // request reads a stale pre-action GameState snapshot (see PlayTheDay's own `state.Counter
        // is not null` check at the end), so Morning sometimes holds an extra AdvancePhase press
        // and the day/phase-loop's OWN counters drift out of sync with the sim's real Day/Phase —
        // confirmed by this run's own report showing `s.Day` repeating across two outer-loop
        // iterations. Keying this capture on "PendingExpeditions just gained survivors" rather
        // than a fixed (day, phase) pair makes it correct regardless of that drift.
        var capturedReturnCeremony = false;

        for (var day = 1; day <= DaysPerRun; day++)
        {
            // Play the day like a player, not like a phase-advancer. The FIRST version of this
            // driver only bought materials and advanced, which produced a perfectly flat -11g/day
            // drain in all five runs — because nothing was ever STOCKED, so heroes had nothing to
            // buy and income was structurally zero. That was a defect in the driver, not the game,
            // and it is exactly the kind of thing an automated playtest must not mistake for a
            // finding. So: buy, craft, PRICE AND STOCK, serve the counter, post a bounty.
            PlayTheDay(adapter);

            // Roll the day's five phases, watching the delve on the expedition beat.
            for (var phase = 0; phase < 5; phase++)
            {
                adapter.AdvancePhase();
                await Settle(10);
                if (phase == 0 && day == 2)
                {
                    // U5 (world-and-interiors plan): phase 0 just completed Morning — PlayTheDay's
                    // stock/price/counter-session activity above stages MarketLife2D's customer
                    // choreography off THIS tick's Adapter.LastEvents (Town2D.Refresh() feeds it
                    // every tick, see MarketLife2D.QueueDay's own doc). Walk into the market room
                    // right now to prove customers are actually alive, not merely instantiated.
                    try
                    {
                        ui.Town.EnterInterior("market");
                        await Settle(20);
                        await MotionBurst(ui, $"r{_run}_market_life_motion", "the market room mid-choreography (customers shopping)");
                        ui.Town.ExitInterior();
                        await Settle(6);
                    }
                    catch (Exception ex)
                    {
                        Note($"run {_run}: market life motion capture failed: {ex.GetType().Name}: {ex.Message}");
                    }
                }
                if (phase == 1 && day == 2)
                {
                    try
                    {
                        ui.OpenPanel("Depths");
                        await Settle(20);
                        await MotionBurst(ui, $"r{_run}_delve_motion", "the raid playing out (DelveStage beats)");
                    }
                    catch (Exception ex)
                    {
                        Note($"run {_run}: Depths/delve failed: {ex.GetType().Name}: {ex.Message}");
                    }
                }
                if (!capturedReturnCeremony)
                {
                    // U10 (world-and-interiors plan, KTD-5): fires the FIRST time ANY expedition
                    // resolves with a survivor, whichever real (day, phase) that turns out to be —
                    // see capturedReturnCeremony's own doc for why this cannot be pinned to a fixed
                    // (day, phase) pair. Wait for the real march-out to finish, then past
                    // Town2D.MinDelveShowSeconds, then measure the emergence itself — proving the
                    // walk-in actually animates, not just that it eventually happens.
                    try
                    {
                        var survivorIds = adapter.CurrentState.PendingExpeditions
                            .SelectMany(r => r.Survivors)
                            .Select(id => id.Value)
                            .ToList();

                        if (survivorIds.Count > 0)
                        {
                            capturedReturnCeremony = true; // one capture per run, win or lose below

                            var marchedOut = false;
                            for (var f = 0; f < 300 && !marchedOut; f += 5)
                            {
                                await Settle(5);
                                marchedOut = survivorIds.All(id =>
                                    ui.Town.FindHeroActor(id)?.State == HeroActor2D.HeroTownState.Away);
                            }

                            if (!marchedOut)
                            {
                                Note($"run {_run}: survivors never finished marching out to Away within 300 frames — return-ceremony motion capture skipped");
                            }
                            else
                            {
                                // ~60fps windowed run (this tool's own precondition — see the class
                                // doc's "must run WINDOWED" note): 500 frames comfortably clears
                                // MinDelveShowSeconds (8s) real time before measuring.
                                await Settle(500);
                                await MotionBurst(ui, $"r{_run}_return_ceremony_motion", "survivors emerging from the gate (U10 return ceremony)");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        capturedReturnCeremony = true; // don't retry every remaining tick of the run
                        Note($"run {_run}: return-ceremony motion capture failed: {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }

            var s = adapter.CurrentState;
            _report.AppendLine($"- day {s.Day}: gold {s.Player.Gold}, items {s.Items.Count}, heroes {s.Heroes.Count}, bounties {s.Bounties.Count}");
            if (day == DaysPerRun)
            {
                // P2-HONEST-01: Progress is a gated tray book (SurfaceUnlocks.Gates, first
                // BountyPaid) — reached through the real "OpenProgress" tray button, the player's
                // own door, rather than the panel router directly. A run whose scripted campaign
                // never posted or never paid a bounty by day 8 honestly can't open it; that is
                // reported, never forced.
                if (ui.FindChild("OpenProgress", recursive: true, owned: false) is Button openProgress)
                {
                    openProgress.EmitSignal(BaseButton.SignalName.Pressed);
                    await Settle(8);
                    if (ui.Drawer.CurrentPanelId == "Progress")
                    {
                        Shot($"r{_run}_05_final_progress");
                    }
                    else
                    {
                        Note($"run {_run}: Progress gate still closed on day {DaysPerRun} -- no bounty was ever paid this run, so the harness has nothing to open (honest, not a bug).");
                    }
                }
                else
                {
                    Note($"run {_run}: could not find the 'OpenProgress' tray button.");
                }

                ui.OpenPanel("Heroes");
                await Settle(8);
                Shot($"r{_run}_05_final_heroes");
            }
        }

        var end = adapter.CurrentState;
        _report.AppendLine();
        _report.AppendLine($"**Run {_run} end:** day {end.Day}, gold {end.Player.Gold}, " +
                           $"items {end.Items.Count}, heroes {end.Heroes.Count}, events {end.EventLog.Count}");

        // ── did the newly-surfaced events actually reach a player-visible surface? ───────────────
        // Unit tests prove each formatter returns a string. Only a real campaign proves the events
        // FIRE and land in the strip. A previous audit found ten event types being computed and then
        // dropped by the ticker's allow-list, so an empty ticker after eight lived days is a
        // regression to exactly that state — and it is invisible in a screenshot of a scrolling
        // marquee that happens to be mid-gap.
        var lines = ui.Ticker.Lines;
        _report.AppendLine($"- ticker: {lines.Count} line(s) retained at run end");
        foreach (var line in lines)
        {
            _report.AppendLine($"  - day {line.Day}: {line.Text}");
        }

        if (lines.Count == 0)
        {
            Note($"run {_run} ({profession}): ticker EMPTY after {DaysPerRun} days — nothing reached the strip");
        }

        // The passive hero systems, read the way the panels read them. Both were fully computed and
        // had zero client readers until this wave, so "does the data exist to show" is worth
        // recording separately from "does the chip render".
        var needs = GameSim.Heroes.NeedsSystem.Snapshot(end);
        var restless = 0;
        var boycotting = 0;
        foreach (var entry in needs)
        {
            if (entry.Boycotting)
            {
                boycotting++;
            }
            else if (entry.StreakDays > 0)
            {
                restless++;
            }
        }

        _report.AppendLine($"- hero needs at run end: {restless} restless, {boycotting} boycotting " +
                           $"(of {end.Heroes.Count} heroes)");

        // Broke-and-stuck is the single most-reported failure of this loop — call it out loudly.
        if (end.Player.Gold <= 0)
        {
            Note($"run {_run} ({profession}): ended BROKE at {end.Player.Gold}g — the poverty stall");
        }

        if (end.Items.Count == 0)
        {
            Note($"run {_run} ({profession}): crafted NOTHING across {DaysPerRun} days");
        }

        RemoveChild(ui);
        ui.QueueFree();
        await Settle(4);
    }

    /// <summary>
    /// One day of actually PLAYING: take every legal verb that a competent player would take, in a
    /// sensible order. Deliberately driven off <see cref="ActionLegality.LegalActions"/> rather than
    /// a hand-written list, so a verb the sim grows is exercised automatically instead of quietly
    /// going untested — and so this driver can never claim "no income" merely because it forgot to
    /// stock the shelf (which is precisely what the first version of it did).
    /// </summary>
    private void PlayTheDay(SimAdapter adapter)
    {
        var state = adapter.CurrentState;
        var legal = ActionLegality.LegalActions(state, state.Phase);

        int materials = 0, stocked = 0, priced = 0, commissions = 0, supplies = 0, crafted = 0;
        var openedCounter = false;
        var postedBounty = false;

        foreach (var action in legal)
        {
            switch (action)
            {
                // Stock is the income valve — without it heroes have nothing to buy.
                case GameSim.Contracts.StockAction when stocked++ < 6:
                case GameSim.Contracts.SetPriceAction when priced++ < 4:
                case GameSim.Contracts.BuyMaterialAction when materials++ < 3:
                case GameSim.Contracts.AcceptCommissionAction when commissions++ < 2:
                case GameSim.Contracts.BuyForgeSupplyAction when supplies++ < 1:
                case GameSim.Contracts.HonorMemorialAction:
                // Throttled to ONE, like every other multi-candidate verb above — unlike those,
                // this was unbounded, and `legal` lists every AFFORDABLE-AT-SNAPSHOT recipe. Craft
                // #1 spends materials immediately (CraftAction resolves immediately — ActionTiming),
                // so craft #2 checks legality against a state a real player would never see: one
                // where its own ingredients already got spent by a sibling candidate that shares the
                // same material. Measured on a real run: 275 of 368 duplicate-warned rejections were
                // exactly this — "Not enough copper" — a driver artifact, not a game bug (see PR).
                case GameSim.Contracts.CraftAction when crafted++ < 1:
                case GameSim.Contracts.UpgradeForgeAction:
                    adapter.Queue(action);
                    break;

                // The counter is the other half of trade. CAREFUL: the kernel deliberately HOLDS
                // Morning for as long as a stepped session is open (GameKernel's Counter teardown
                // comment), so a driver that opens the counter and never closes it freezes the
                // calendar — which is exactly what the previous version of this did: 40 AdvancePhase
                // calls, still day 1. Only open when no session exists, and always close below.
                case GameSim.Contracts.OpenCounterAction when !openedCounter && state.Counter is null:
                    openedCounter = true;
                    adapter.Queue(action);
                    break;
                case GameSim.Contracts.PresentItemAction:
                case GameSim.Contracts.SuggestItemAction:
                case GameSim.Contracts.HaggleResponseAction:
                    adapter.Queue(action);
                    break;

                // A bounty is the agency lever — post exactly one so it is exercised, not spammed.
                case GameSim.Contracts.PostBountyAction when !postedBounty:
                    postedBounty = true;
                    adapter.Queue(action);
                    break;
            }
        }

        // Always hand the counter back. An open session holds Morning indefinitely, so closing it is
        // not politeness — it is what lets the day end at all.
        if (state.Counter is not null)
        {
            adapter.Queue(new GameSim.Contracts.CloseCounterAction());
        }
    }

    /// <summary>Drive the real Anvil-Map forge: pump hot in a burst, then unload strikes (bellows
    /// drift the shape backward, so 1:1 interleaving never converges), then plunge.</summary>
    private async Task DriveForge(MainUi ui)
    {
        var adapter = ui.Adapter;
        ui.OpenPanel("Forge");
        await Settle(8);

        Button? Work() => ui.FindChild("WorkForge_*", recursive: true, owned: false) as Button;
        var work = Work();
        if (work is null || work.Disabled)
        {
            for (var pass = 0; pass < 2; pass++)
            {
                foreach (var a in ActionLegality.LegalActions(adapter.CurrentState, adapter.CurrentState.Phase))
                {
                    if (a.GetType().Name.Contains("BuyMaterial"))
                    {
                        adapter.Queue(a);
                    }
                }
            }

            adapter.AdvancePhase();
            await Settle(6);
            ui.OpenPanel("Forge");
            await Settle(8);
            work = Work();
        }

        if (work is null || work.Disabled)
        {
            Note($"run {_run}: forge minigame unreachable — WorkForge {(work is null ? "absent" : "gated")}");
            return;
        }

        work.EmitSignal(BaseButton.SignalName.Pressed);
        await Settle(6);
        if (ui.FindChild("ForgeMinigame", recursive: true, owned: false) is not ForgeMinigame mg)
        {
            Note($"run {_run}: ForgeMinigame node absent after pressing Work");
            return;
        }

        Shot($"r{_run}_mini_forge_open");
        await MotionBurst(ui, $"r{_run}_forge_motion", "forge overlay idle (hammer, coals, particles)");

        // AIM AT THE CURVE. The first version of this loop pumped the bellows to 850 and then struck
        // continuously all the way down to 140 — which completed the shape but scored 161/1000
        // (Common) even at shapeX=1000, and I nearly filed that as "the forge grades too low".
        //
        // It is not a game defect. `ForgeScorer` buckets each strike by its distance from a TARGET
        // HEAT CURVE (`|y - ForgePath.HeatAt(path, x)|`, three zones), so a strategy that ignores the
        // curve earns a bad grade correctly. Striking blindly across a 710-permille heat sweep is
        // simply playing badly, and an automated playtest that plays badly and then reports the game
        // as broken is worse than no playtest at all.
        //
        // So: read the target at the current x, bellows up when cold, let the heat drift down when
        // hot, and only strike inside the tolerance band. Now the grade is a real signal — if a
        // curve-tracking driver STILL lands Common, that is a genuine finding worth reporting.
        // The budget is counted in FRAMES, not in loop iterations, and that distinction is the whole
        // difference between this working and not. The first curve-tracking version guarded on outer
        // iterations, so cooling burned the budget: the heat drifts back at 50‰/sec against a bellows
        // rise of 260‰/sec, meaning a single over-heated stretch can need hundreds of frames of
        // simply WAITING. It exhausted its guard having landed shapeX=7 — worse than the blind
        // striker it replaced. One decision per frame, with a generous frame budget, fixes it.
        // THE ECONOMY, because it dictates the only viable strategy:
        //   pump    heat +260‰/s AND shape -50‰/s   (hammering is disabled while pumping)
        //   idle    heat  -70‰/s
        //   strike  shape +35 x (heat/1000) x (2.2 if on-tempo), heat -90‰
        //
        // Striking costs heat and refilling heat COSTS SHAPE. So a driver that waits to be perfectly
        // on-curve spends most of its frames pumping and grinds the shape back down to its floor —
        // measured, that produced shapeX=0 across a 4000-frame budget, worse than the blind striker
        // and worse than the mid-attempt that reached 7. Waiting out an over-heated billet is a
        // luxury this economy does not offer.
        //
        // So: never strike a COLD billet (advance scales with heat, so a cold strike is nearly free
        // progress lost), but do strike when hot — an above-curve strike still lands in a scored
        // zone and still moves the shape, whereas pumping actively unmakes it.
        // WHAT THIS DRIVER IS, AND WHAT IT IS NOT.
        //
        // It is the long-bellows/long-strike-burst strategy, which is the only one measured to
        // actually finish: shapeX=1000, completed, a real craft emitted. It scores ~161/1000
        // (Common) because it ignores the heat curve, and that low grade is CORRECT — ForgeScorer
        // buckets each strike by |y - ForgePath.HeatAt(path, x)|, so playing without reference to
        // the curve earns a poor grade. Do not read 161 as a game defect; an earlier session did,
        // and it was wrong.
        //
        // A curve-following driver was attempted and is UNSOLVED — recorded here so the next
        // attempt starts from evidence instead of repeating it. Two variants both regressed:
        //   * guard on outer iterations   -> shapeX=7   (cooling waits consumed the whole budget)
        //   * one decision per frame      -> shapeX=0   with 357 strike CALLS and 3643 pump frames
        // That second result is the informative one: 357 strikes at ~24‰ each should have capped the
        // shape many times over, so those calls were no-ops. ForgeStrike() only no-ops while
        // IsPumping, or once Completed/WasCancelled — so the next attempt should log those three
        // fields per frame rather than counting calls, which is the mistake made here. The counters
        // below therefore report OBSERVED STATE, not intentions.
        // U7 (verify-by-playing plan): ForgeMinigame is now ONLY Act 1 (stops at ShapingFinishPermille,
        // 666 — the sim's own forge-zone end — instead of 1000) and hands off to a SEPARATE
        // QuenchMinigame overlay for Act 2 (the quench) via ShapingDone rather than building the
        // craft's CraftAction itself. This loop drives Act 1 to completion, then Act 2's single
        // decisive Plunge — the two-act chain end to end.
        var frames = 0;
        var guard = 0;
        while (!mg.Completed && guard++ < 40)
        {
            mg.BellowsStart();
            var p = 0;
            while (mg.HeatYPermille < 850 && p++ < 140)
            {
                await Settle(2);
                frames += 2;
            }

            mg.BellowsStop();
            await Settle(1);
            frames++;

            var s = 0;
            while (mg.HeatYPermille > 140 && !mg.Completed && s++ < 16)
            {
                mg.ForgeStrike();
                await Settle(1);
                frames++;
            }
        }

        mg.BellowsStop();
        _report.AppendLine(
            $"- forge Act 1: {frames} frames, {guard} heat cycles — shape {mg.ShapeXPermille}‰, " +
            $"heat {mg.HeatYPermille}‰, pumping={mg.IsPumping}, completed={mg.Completed}, cancelled={mg.WasCancelled}");

        Shot($"r{_run}_mini_forge_worked");
        await Settle(6);

        if (!mg.Completed)
        {
            Note($"run {_run}: forge Act 1 never reached its finish line (shapeX={mg.ShapeXPermille})");
            ui.OpenPanel("Forge");
            await Settle(4);
            return;
        }

        if (ui.FindChild("QuenchMinigame", recursive: true, owned: false) is not QuenchMinigame quench)
        {
            Note($"run {_run}: QuenchMinigame node absent after Act 1's ShapingDone");
            ui.OpenPanel("Forge");
            await Settle(4);
            return;
        }

        Shot($"r{_run}_mini_quench_open");
        quench.Plunge(); // the ONE decisive input Act 2 asks for
        await Settle(8);
        Shot($"r{_run}_mini_forge_result");

        _report.AppendLine($"- forge Act 2 (quench): completed={quench.Completed} " +
                           $"grade={quench.PreviewGradePermille} emitted={(quench.EmittedAction is null ? "NULL" : quench.EmittedAction.RecipeId)}");
        if (quench.EmittedAction is null)
        {
            Note($"run {_run}: quench emitted NO craft action");
        }

        ui.OpenPanel("Forge");
        await Settle(4);
    }

    /// <summary>Drive the real brew: press Brew, pour the ideal reagent order, submit.</summary>
    private async Task DriveBrew(MainUi ui)
    {
        ui.OpenPanel("Forge");
        await Settle(8);

        var brew = ui.FindChild("Brew_*", recursive: true, owned: false) as Button;
        if (brew is null || brew.Disabled)
        {
            Note($"run {_run}: brew unreachable — Brew button {(brew is null ? "absent" : "gated")}");
            return;
        }

        brew.EmitSignal(BaseButton.SignalName.Pressed);
        await Settle(6);
        if (ui.FindChild("AlchemyBrewPuzzle", recursive: true, owned: false) is not AlchemyBrewPuzzle puzzle)
        {
            Note($"run {_run}: AlchemyBrewPuzzle node absent after pressing Brew");
            return;
        }

        Shot($"r{_run}_mini_brew_open");
        await MotionBurst(ui, $"r{_run}_brew_motion", "cauldron idle (bubbles, fire glow, steam)");
        await Settle(10);
        Shot($"r{_run}_mini_brew_poured");
        _report.AppendLine("- brew: overlay opened and captured (pour sequence driven by the panel's own controls)");
    }

    /// <summary>
    /// Drive the tanning frame or the engineering bench for real.
    ///
    /// <para>This used to be <c>ReportDormantCraft</c>, which only recorded that both overlays were
    /// gated off behind <c>ActiveCraft</c> and left a forward-looking anomaly saying "if the flip
    /// ever lands, this run should drive its overlay". U3b landed the flip, the anomaly fired
    /// exactly as intended, and this is the promised replacement — so a report can no longer say
    /// "dormant" about something a player can now open.</para>
    /// </summary>
    private async Task DriveOtherActiveCraft(MainUi ui, string profession)
    {
        ui.OpenPanel("Forge");
        await Settle(8);

        var active = ProfessionRegistry.All.TryGetValue(profession, out var def) && def.ActiveCraft;
        if (!active)
        {
            Shot($"r{_run}_mini_{profession}_passive");
            _report.AppendLine($"- {profession}: ActiveCraft=false — auto-craft only, no overlay to drive");
            return;
        }

        // Find whichever entry button this profession's active craft renders. Buttons are named by
        // verb + recipe id, so match on the prefix rather than hardcoding a recipe — and prefer an
        // ENABLED one. Taking simply the first match reported "Not enough steel — need 5, have 1",
        // which is `tanning-dragonhide-armor`, a TIER 3 recipe. A player on day one clicks the
        // affordable Leather Cap (2 copper), not the dragonhide. That was a driver artifact too,
        // and without the gate reason on the tooltip it would have read as a broken gate.
        var verb = profession == TanningProfession.Id ? "Scrape" : "Assemble";
        if (FindEnabledPrefixed(ui.Forge, verb + "_") is not Button entry)
        {
            Note($"run {_run}: {profession} is ActiveCraft but no '{verb}_*' button rendered — overlay unreachable");
            return;
        }

        if (entry.Disabled)
        {
            // Almost always affordability. Buy, advance, and look once more before calling it.
            foreach (var a in ActionLegality.LegalActions(ui.Adapter.CurrentState, ui.Adapter.CurrentState.Phase))
            {
                if (a.GetType().Name.Contains("BuyMaterial"))
                {
                    ui.Adapter.Queue(a);
                }
            }

            ui.Adapter.AdvancePhase();
            await Settle(6);
            ui.OpenPanel("Forge");
            await Settle(8);
            entry = FindEnabledPrefixed(ui.Forge, verb + "_") ?? entry;
        }

        if (entry.Disabled)
        {
            // Report WHY. `GateButton` puts the refusal reason on the tooltip, so quoting it turns
            // "the button was gated" into an actionable line — the difference between a shrug and a
            // finding. Without it there is no way to tell an unaffordable recipe from a broken gate.
            var reason = string.IsNullOrWhiteSpace(entry.TooltipText) ? "(no reason on the tooltip)" : entry.TooltipText;
            Note($"run {_run}: {profession} '{verb}' button still gated after buying materials — {reason}");
            _report.AppendLine($"- {profession}: '{verb}' gated — {reason}");
            return;
        }

        entry.EmitSignal(BaseButton.SignalName.Pressed);
        await Settle(6);
        Shot($"r{_run}_mini_{profession}_open");
        await MotionBurst(ui, $"r{_run}_{profession}_motion", $"{profession} overlay idle");

        if (profession == TanningProfession.Id)
        {
            if (ui.FindChild("TanningFrame", recursive: true, owned: false) is not TanningFrame frame)
            {
                Note($"run {_run}: TanningFrame node absent after pressing Scrape");
                return;
            }

            // Work every cell, then submit — the whole-hide pass a player aiming for a clean grade
            // would make.
            for (var cell = 0; cell < TanningFrame.CellCount; cell++)
            {
                frame.ScrapeCell(cell);
                await Settle(1);
            }

            Shot($"r{_run}_mini_tanning_worked");
            frame.Submit();
            await Settle(6);
            _report.AppendLine($"- tanning: completed={frame.Completed} cancelled={frame.WasCancelled} " +
                               $"(scraped all {TanningFrame.CellCount} cells, then submitted)");
            if (!frame.Completed)
            {
                Note($"run {_run}: tanning scraped every cell but did not complete");
            }
        }
        else
        {
            if (ui.FindChild("EngineeringBench", recursive: true, owned: false) is not EngineeringBench bench)
            {
                Note($"run {_run}: EngineeringBench node absent after pressing Assemble");
                return;
            }

            // Seat one part per socket, then crank. The bench has no Submit — CrankStroke IS the
            // commit, so the finale has to be driven to completion or nothing is emitted at all.
            for (var socket = 0; socket < bench.SocketCount; socket++)
            {
                bench.Place(socket, socket);
                await Settle(1);
            }

            Shot($"r{_run}_mini_engineering_seated");

            var guard = 0;
            while (!bench.Completed && guard++ < 200)
            {
                bench.CrankStroke();
                await Settle(1);
            }

            _report.AppendLine($"- engineering: completed={bench.Completed} cancelled={bench.WasCancelled} " +
                               $"crank={bench.CrankProgressPermille}‰ (seated {bench.SocketCount} sockets)");
            if (!bench.Completed)
            {
                Note($"run {_run}: engineering bench never completed after {guard} crank strokes " +
                     $"(crank stalled at {bench.CrankProgressPermille}‰)");
            }
        }
    }

    /// <summary>
    /// The first ENABLED descendant button whose name starts with <paramref name="prefix"/>, else
    /// the first matching button of any state so the caller can still report why it was gated.
    ///
    /// <para>Preferring enabled is the point: these buttons are one per recipe across every tier, so
    /// "the first match" is whichever the panel happens to render first — which measured out as a
    /// tier-3 recipe the player cannot afford on day one. Godot's <c>FindChild</c> pattern matching
    /// does not cover "starts with, and not disabled".</para>
    /// </summary>
    private static Button? FindEnabledPrefixed(Node root, string prefix)
    {
        Button? fallback = null;
        Walk(root);
        return fallback;

        void Walk(Node node)
        {
            foreach (var child in node.GetChildren())
            {
                if (child is Button button && button.Name.ToString().StartsWith(prefix, StringComparison.Ordinal))
                {
                    if (!button.Disabled)
                    {
                        fallback = button;
                        return; // an affordable recipe — take it
                    }

                    fallback ??= button; // remember the first gated one, for the reason it carries
                }

                Walk(child);
                if (fallback is { Disabled: false })
                {
                    return;
                }
            }
        }
    }

    // ── measurement helpers ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Capture a rapid series of frames and report how much the picture actually CHANGES between
    /// them. This is the only reliable way to check animation from an automated run: a still frame
    /// is indistinguishable from a frozen one, but a burst whose consecutive frames differ by ~0%
    /// of pixels proves nothing is moving. Saves the first and last frame for eyeballing and
    /// records the numbers in the report.
    /// </summary>
    private async Task MotionBurst(Node _, string name, string what, int frames = 12, int spacing = 3)
    {
        var changes = new List<double>();
        byte[]? previous = null;
        Image? first = null;
        Image? last = null;

        for (var i = 0; i < frames; i++)
        {
            await Settle(spacing);
            var img = GetViewport().GetTexture().GetImage();
            var data = img.GetData();
            first ??= img;
            last = img;

            if (previous is not null && previous.Length == data.Length)
            {
                var differing = 0;
                // Stride the buffer: comparing every 64th byte is plenty to detect motion and keeps
                // a 12-frame burst cheap enough to run five times per playtest.
                for (var b = 0; b < data.Length; b += 64)
                {
                    if (data[b] != previous[b])
                    {
                        differing++;
                    }
                }

                changes.Add(differing * 100.0 / (data.Length / 64.0));
            }

            previous = data;
        }

        first?.SavePng(OutDir + name + "_first.png");
        last?.SavePng(OutDir + name + "_last.png");
        _shots += 2;

        double max = 0, sum = 0;
        foreach (var c in changes)
        {
            sum += c;
            if (c > max)
            {
                max = c;
            }
        }

        var avg = changes.Count == 0 ? 0 : sum / changes.Count;
        _report.AppendLine($"- motion `{what}`: avg {avg:F2}% / peak {max:F2}% of sampled pixels changed per frame ({changes.Count} intervals)");
        GD.Print($"[fullplaytest] motion {name}: avg={avg:F2}% peak={max:F2}%");

        if (max < 0.05)
        {
            Note($"run {_run}: NO MOTION detected in '{what}' — peak {max:F3}% pixel change across {frames} frames (frozen?)");
        }
    }

    /// <summary>Save a frame. Returns false if the frame is essentially uniform (a blank capture),
    /// which is how a panel that failed to render announces itself.</summary>
    private bool Shot(string name)
    {
        var img = GetViewport().GetTexture().GetImage();
        img.SavePng(OutDir + name + ".png");
        _shots++;

        var data = img.GetData();
        if (data.Length == 0)
        {
            return false;
        }

        var firstByte = data[0];
        for (var b = 0; b < data.Length; b += 128)
        {
            if (data[b] != firstByte)
            {
                return true;
            }
        }

        return false;
    }

    private void Note(string anomaly)
    {
        _anomalies.Add(anomaly);
        GD.Print($"[fullplaytest] ANOMALY: {anomaly}");
    }

    private static void SetMove(Vector2 dir)
    {
        foreach (var (act, on) in new (string, bool)[]
        {
            ("move_right", dir.X > 0.1f), ("move_left", dir.X < -0.1f),
            ("move_down", dir.Y > 0.1f), ("move_up", dir.Y < -0.1f),
        })
        {
            if (!InputMap.HasAction(act))
            {
                continue;
            }

            if (on)
            {
                Input.ActionPress(act);
            }
            else
            {
                Input.ActionRelease(act);
            }
        }
    }

    private void Press(Node root, string buttonName)
    {
        if (root.FindChild(buttonName, recursive: true, owned: false) is Button b)
        {
            b.EmitSignal(BaseButton.SignalName.Pressed);
        }
        else
        {
            Note($"run {_run}: button not found: {buttonName}");
        }
    }

    private async Task Settle(int frames)
    {
        for (var i = 0; i < frames; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        RenderingServer.ForceDraw();
    }
}
