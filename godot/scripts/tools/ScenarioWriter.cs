using System;
using GameSim.Contracts;
using GameSim.Harness;
using Godot;
using GodotClient.Ui;

namespace GodotClient.Tools;

/// <summary>
/// U18 (§11.14.14, "a tester can stand on day 4"): manufactures a day-N campaign save — with the
/// apprenticeship chain honestly mid-flight — so the title screen's Continue button lands a human
/// on day N without hand-playing the days in between.
///
/// <para><b>The problem this closes.</b> The game's whole premise turns on a moment that lands on
/// day 4 (measured 12 of 12 seeds — see <see cref="ScenarioBuilder"/>'s own doc and
/// <c>ScenarioBuilderTests.Day4Scenario_ReliablyReachesTheAttributionBeat_WithinDay4</c>). There is
/// one save slot, deliberately anti-reroll, and no scenario save or day-jump — so verifying anything
/// about that moment cost three hand-played days, every single time.</para>
///
/// <para><b>Dev-gated, following <see cref="PlaytestLog"/>'s own precedent.</b> <see cref="DayVar"/>'s
/// mere presence — a parseable positive integer — is the gate, the same shape
/// <c>MM_PLAYTEST_LOG</c> already uses: absent or invalid, <see cref="_Ready"/> refuses and quits
/// nonzero, touching no file at all. Unreachable and invisible in a normal player build, which never
/// sets it and never loads this scene (no <c>project.godot</c> change — see this class's own
/// launch note below, the exact seam <c>agentplaytest.tscn</c>/<c>AgentPlaytest.cs</c> already use).</para>
///
/// <para><b>This is a tool, not a second save slot.</b> <see cref="CampaignSave"/>'s one-rolling-slot
/// anti-reroll rationale is untouched — this writes through the SAME <see cref="CampaignSave.Save"/>
/// call the game's own end-of-Evening autosave uses, at the SAME <c>user://</c> path, so there is
/// still exactly one save a real Continue can ever resume. This tool only chooses what that one
/// slot's own bytes say — the same bytes three hand-played real days would have produced.</para>
///
/// <para><b>Determinism is the mechanism</b> (owner ruling, §11.14.14's own U18 entry). The state
/// this writes comes from <see cref="ScenarioBuilder.BuildDay"/> — pure GameSim, zero Godot
/// reference, the same scripted <see cref="BaselinePlayer"/> policy the balance gate and the
/// telemetry farm already trust. Same seed, same day, same bytes, every time — pinned in the fast
/// lane by <c>ScenarioBuilderTests</c>. Sim purity holds: <c>sim/GameSim/</c> has no idea this tool
/// exists; it is the same composition root and the same policy every other caller already uses.</para>
///
/// <para><b>The tutorial chain is written mid-flight, not completed, not reset.</b>
/// <see cref="Write"/> runs a freshly reset <see cref="TutorialFlow"/> instance's own
/// <see cref="TutorialFlow.Advance"/> against the manufactured state — the EXACT forward pass that
/// runs every real tick during play (<c>MainUi.OnPhaseCompleted</c>), never reimplemented here — so
/// whichever step it lands on is the honest answer a real campaign that reached this exact state
/// would have left on disk. If <see cref="BaselinePlayer"/> never performs some step's action (it
/// never touches the Counter, for instance — see <c>GameComposition</c>'s own class doc), the chain
/// simply stalls there instead of fabricating progress; nothing here papers over that.</para>
///
/// <para><b>Launch (mirrors <c>agentplaytest.tscn</c>/<c>tools/agent-playtest.ps1</c>'s own
/// command-line scene override — no <c>project.godot</c> edit, deny-listed):</b></para>
/// <code>
/// $env:MM_SCENARIO_DAY = "4"
/// &amp; $env:GODOT_BIN --headless --path &lt;repo&gt;/godot res://scenariowriter.tscn
/// </code>
/// <para>Then launch the game normally and press Continue. <c>MM_SCENARIO_SEED</c> (default 1) and
/// <c>MM_SCENARIO_PROFESSION</c> (optional; absent keeps <see cref="GameComposition.NewCampaign(ulong)"/>'s
/// own default) optionally narrow which campaign gets manufactured — mirroring
/// <c>MainUi.BuildDefaultAdapter</c>'s existing <c>SHOT_PROFESSION</c> receipt seam.</para>
/// </summary>
public partial class ScenarioWriter : Node
{
    private const string DayVar = "MM_SCENARIO_DAY";
    private const string SeedVar = "MM_SCENARIO_SEED";
    private const string ProfessionVar = "MM_SCENARIO_PROFESSION";

    public override void _Ready()
    {
        var day = GateDay();
        if (day is null)
        {
            GD.PrintErr($"[scenario-writer] {DayVar} missing/invalid — refusing to run. Set it to a " +
                        "positive day number to write a scenario save on purpose; it overwrites the " +
                        $"one campaign save slot ({CampaignSave.SavePath}).");
            GetTree().Quit(1);
            return;
        }

        var seed = ulong.TryParse(System.Environment.GetEnvironmentVariable(SeedVar), out var parsedSeed) ? parsedSeed : 1UL;
        var profession = System.Environment.GetEnvironmentVariable(ProfessionVar);
        profession = string.IsNullOrWhiteSpace(profession) ? null : profession;

        var state = ScenarioBuilder.BuildDay(seed, day.Value, profession);
        var (ok, message) = Write(state);
        GD.Print(message);
        GetTree().Quit(ok ? 0 : 1);
    }

    /// <summary>
    /// Parses <see cref="DayVar"/> — null when absent or not a positive integer, which IS the
    /// whole gate. Split out of <see cref="_Ready"/> so a test can prove "off by default, unreachable
    /// without the env var" directly, without invoking <see cref="_Ready"/> itself: that method's
    /// own <c>GetTree().Quit</c> would tear down the engine-test process, the same reason
    /// <c>AgentPlaytestBridge</c> is tested instead of <c>AgentPlaytest</c>'s own <c>_Ready</c>.
    /// </summary>
    public static int? GateDay()
    {
        var raw = System.Environment.GetEnvironmentVariable(DayVar);
        return int.TryParse(raw, out var day) && day >= 1 ? day : null;
    }

    /// <summary>
    /// The write itself, split out of <see cref="_Ready"/> so a test can call it directly against a
    /// hand-built state without spinning up (and force-quitting) a whole scene tree. Never throws —
    /// a save/tutorial-write failure degrades to a reported failure, the same fail-soft contract
    /// <see cref="CampaignSave"/> itself already keeps for every other write path.
    /// </summary>
    /// <returns>Whether the campaign save itself succeeded, plus a one-line human-readable report.</returns>
    public static (bool Ok, string Message) Write(GameState state)
    {
        // Reset FIRST: a developer's own leftover tutorial_flow.json (dismissed, completed, or
        // mid a DIFFERENT campaign entirely) must never leak into a freshly written scenario —
        // the same reset NewGameSelect.OnBeginPressed already performs for a real New Game.
        TutorialFlow.ResetForNewGame();

        // Advance() is the SAME forward pass MainUi.OnPhaseCompleted runs every real tick — never
        // reimplemented here (see class doc). A freshly constructed instance is exactly how MainUi
        // itself builds one (`Tutorial = new TutorialFlow { ... }`, before its separate Build() call
        // constructs child UI controls this write never needs). Never added to the scene tree, so it
        // must be freed explicitly — a Node (unlike a RefCounted) is not garbage-collected and a
        // throwaway instance left unfreed is exactly the orphan-node leak the engine suite's own
        // teardown guards (UiTestSupport.Unmount) exist to catch.
        var tutorial = new TutorialFlow();
        try
        {
            tutorial.Advance(state);
        }
        finally
        {
            tutorial.Free();
        }

        var saved = CampaignSave.Save(state);
        var message = saved
            ? $"[scenario-writer] wrote day {state.Day} ({state.Phase}) to {CampaignSave.SavePath} " +
              $"-> {ProjectSettings.GlobalizePath(CampaignSave.SavePath)}"
            : $"[scenario-writer] FAILED to write {CampaignSave.SavePath} — see the warning above";
        return (saved, message);
    }
}
