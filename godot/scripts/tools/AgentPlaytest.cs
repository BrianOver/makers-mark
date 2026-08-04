using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Godot;

namespace GodotClient.Tools;

/// <summary>
/// One button on screen right now, as a local model would need to see it — visible whether it is
/// currently pressable or not, so a refusal ("disabled") is something the model could have seen
/// coming rather than a mystery. See <see cref="ScreenObservation.ObservedControls"/>.
/// </summary>
public sealed record ControlDigest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("enabled")] bool Enabled);

/// <summary>
/// The per-turn observation <see cref="AgentPlaytestBridge"/> writes to <c>state.json</c> — the
/// channel contract from the verify-by-playing plan's U1 section, field-for-field. <see
/// cref="ScreenText"/> is <c>HumanPlayer.Screen()</c>'s own honest view (visible text only, nothing
/// hidden or scrolled off screen), split one entry per control instead of newline-joined, and
/// <see cref="Controls"/> is every visible button with its enabled state — both come from <see
/// cref="ScreenObservation"/>, the helper this unit extracted so a test-assembly-only type
/// (<c>HumanPlayer</c>) and this production dev tool never carry two competing definitions of
/// "what's on screen".
/// </summary>
public sealed record StateDigest(
    [property: JsonPropertyName("turn")] int Turn,
    [property: JsonPropertyName("day")] int Day,
    [property: JsonPropertyName("phase")] string Phase,
    [property: JsonPropertyName("actionSlotsRemaining")] int ActionSlotsRemaining,
    [property: JsonPropertyName("gold")] int Gold,
    [property: JsonPropertyName("location")] string Location,
    [property: JsonPropertyName("canMove")] bool CanMove,
    [property: JsonPropertyName("screenText")] IReadOnlyList<string> ScreenText,
    [property: JsonPropertyName("controls")] IReadOnlyList<ControlDigest> Controls,
    [property: JsonPropertyName("lastOutcome")] string LastOutcome);

/// <summary>
/// What the driver (a human, a script, or U2's model loop) writes to <c>command.json</c>. Every
/// field but <see cref="Action"/> is optional because each action only needs a subset: <c>press</c>
/// needs <see cref="Target"/>, <c>move</c> needs <see cref="Dir"/> (+ optional <see
/// cref="Frames"/>), <c>key</c> needs <see cref="Target"/> (an InputMap action name), <c>advance</c>
/// and <c>stop</c> need neither. <see cref="Why"/> is never read by the bridge — it exists purely
/// so the turn log carries the model's own stated reasoning next to what actually happened.
/// </summary>
public sealed record AgentCommand(
    [property: JsonPropertyName("action")] string? Action,
    [property: JsonPropertyName("target")] string? Target = null,
    [property: JsonPropertyName("why")] string? Why = null,
    [property: JsonPropertyName("dir")] string? Dir = null,
    [property: JsonPropertyName("frames")] int? Frames = null);

/// <summary>
/// The observe/act bridge itself (U1, verify-by-playing plan) — separated from <see
/// cref="AgentPlaytest"/>'s dev-tool bootstrap (env-var gate, scene mount, quit) precisely so
/// <c>AgentPlaytestBridgeTests</c> can drive it against an already-mounted <see cref="MainUi"/>
/// with SCRIPTED commands, no file polling and no model in the loop — the plan's own execution
/// note for U2 applies here too: prove the channel deterministically before anything unpredictable
/// touches it.
///
/// <para><b>Every action goes through a REAL input path, never an adapter/sim call</b> —
/// <c>EmitSignal(BaseButton.SignalName.Pressed)</c> for <c>press</c>,
/// <see cref="PlayerController2D.SetDirectInput"/> for <c>move</c> (the same seam
/// <c>CameraFocusBeatTests</c> and friends already use), <see cref="Input.ActionPress"/> for
/// <c>key</c>. The one deliberate exception is <c>advance</c>: there is no in-game button for it
/// (real time passage is <see cref="PhaseClock"/>'s own wall-clock timer), so it calls
/// <see cref="SimAdapter.AdvancePhase"/> directly — the same thing every other dev tool in this
/// folder already does to skip time, and the contract names it as its own distinct verb rather
/// than a <c>press</c> for exactly this reason.</para>
/// </summary>
public sealed class AgentPlaytestBridge
{
    /// <summary>Any node already inside the <see cref="SceneTree"/> — used only to hang
    /// <c>ProcessFrame</c> awaits off of. Never touched otherwise.</summary>
    private readonly Node _pump;

    public AgentPlaytestBridge(Node pump) => _pump = pump ?? throw new ArgumentNullException(nameof(pump));

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>The honest digest of <paramref name="ui"/> right now — day/phase/gold straight off
    /// the sim state, everything else (location, canMove, screenText, controls) read off the real
    /// presentation layer the same way a player would.</summary>
    public StateDigest BuildDigest(MainUi ui, int turn, string lastOutcome)
    {
        var state = ui.Adapter.CurrentState;
        var viewport = ui.GetViewport();
        var controls = ScreenObservation.ObservedControls(ui)
            .Select(c => new ControlDigest(c.Name, c.Label, c.Enabled))
            .ToList();

        return new StateDigest(
            Turn: turn,
            Day: state.Day,
            Phase: state.Phase.ToString(),
            ActionSlotsRemaining: state.ActionSlotsRemaining,
            Gold: state.Player.Gold,
            Location: Location(ui),
            CanMove: ui.Town.WorldInputNode.Enabled,
            ScreenText: ScreenObservation.VisibleText(ui, viewport),
            Controls: controls,
            LastOutcome: lastOutcome);
    }

    /// <summary>"town", "interior:&lt;venueKey&gt;", or "panel:&lt;id&gt;" — a drawer panel takes
    /// priority over a room because <see cref="Town2D.EnterInterior"/> stays active underneath an
    /// opened station drawer (the player is still standing in the room; the drawer is what is
    /// actually covering the screen right now).</summary>
    private static string Location(MainUi ui)
    {
        if (ui.Drawer.IsOpen)
        {
            return $"panel:{ui.Drawer.CurrentPanelId}";
        }

        if (ui.Town.InteriorActive)
        {
            return $"interior:{ui.Town.InteriorVenueKey}";
        }

        return "town";
    }

    /// <summary>Applies one command through a real input path and returns the outcome string that
    /// goes in both <c>lastOutcome</c> and the turn log. Never throws for a bad command — an
    /// unresolvable target is a refusal, not an exception, per R2 (the model must be told, not
    /// crash the run).</summary>
    public async Task<string> Apply(MainUi ui, AgentCommand command)
    {
        return command.Action?.Trim().ToLowerInvariant() switch
        {
            "press" => await ApplyPress(ui, command),
            "move" => await ApplyMove(ui, command),
            "key" => await ApplyKey(ui, command),
            "advance" => ApplyAdvance(ui),
            "stop" => "stopped",
            _ => $"refused: unknown action '{command.Action}' (expected press/move/key/advance/stop)",
        };
    }

    private async Task<string> ApplyPress(MainUi ui, AgentCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Target))
        {
            return "refused: press requires a target control name";
        }

        var button = ScreenObservation.FindVisibleButtonByName(ui, command.Target);
        if (button is null)
        {
            return $"refused: no visible control named '{command.Target}' — it is not on screen right now";
        }

        if (button.Disabled)
        {
            var reason = string.IsNullOrWhiteSpace(button.TooltipText) ? "(no reason on the tooltip)" : button.TooltipText;
            return $"refused: '{command.Target}' is disabled — {reason}";
        }

        var goldBefore = ui.Adapter.CurrentState.Player.Gold;
        button.EmitSignal(BaseButton.SignalName.Pressed);
        await Settle(6);
        var goldAfter = ui.Adapter.CurrentState.Player.Gold;
        return $"pressed {command.Target} -> gold {goldBefore} -> {goldAfter}";
    }

    private async Task<string> ApplyMove(MainUi ui, AgentCommand command)
    {
        var dir = ParseDirection(command.Dir);
        if (dir is null)
        {
            return $"refused: unknown move dir '{command.Dir}' (expected up/down/left/right)";
        }

        if (!ui.Town.WorldInputNode.Enabled)
        {
            return "refused: world input is blocked right now (a drawer or overlay is open) — cannot move";
        }

        var frames = Math.Clamp(command.Frames ?? 20, 1, 300);
        var before = ui.Town.Player.GlobalPosition;

        // Same seam CameraFocusBeatTests/etc. use: SetDirectInput overrides Input.GetVector so the
        // move is deterministic, then PlayerController2D._PhysicsProcess (which checks
        // _inputEnabled BEFORE reading _directInput) does the real MoveAndSlide work — this is
        // exactly what a player holding a direction key exercises, never a position write.
        ui.Town.Player.SetDirectInput(dir.Value);
        await Settle(frames);
        ui.Town.Player.SetDirectInput(null);
        await Settle(2); // let velocity settle to zero before the next observation

        var after = ui.Town.Player.GlobalPosition;
        return $"moved {command.Dir} {frames}f -> pos ({after.X:0},{after.Y:0}) from ({before.X:0},{before.Y:0})";
    }

    private async Task<string> ApplyKey(MainUi ui, AgentCommand command)
    {
        _ = ui; // the action is global (Input singleton); ui is unused here but kept for a uniform signature
        if (string.IsNullOrWhiteSpace(command.Target) || !InputMap.HasAction(command.Target))
        {
            return $"refused: no InputMap action named '{command.Target}'";
        }

        Input.ActionPress(command.Target);
        await Settle(3);
        Input.ActionRelease(command.Target);
        await Settle(3);
        return $"tapped key '{command.Target}'";
    }

    private static string ApplyAdvance(MainUi ui)
    {
        var before = ui.Adapter.CurrentState;
        ui.Adapter.AdvancePhase();
        var after = ui.Adapter.CurrentState;
        return $"advanced -> day {before.Day} {before.Phase} to day {after.Day} {after.Phase}";
    }

    private static Vector2? ParseDirection(string? dir) => dir?.Trim().ToLowerInvariant() switch
    {
        "up" => new Vector2(0, -1),
        "down" => new Vector2(0, 1),
        "left" => new Vector2(-1, 0),
        "right" => new Vector2(1, 0),
        _ => null,
    };

    /// <summary>
    /// The full turn loop: settle, observe (state.json + frame.png), poll for command.json,
    /// apply, log, repeat — until <c>stop</c>, <paramref name="maxTurns"/>, or a command-file
    /// timeout. Every branch writes <c>turnlog.md</c> before returning, including the timeout
    /// branch (R2: a run that goes quiet must still leave a readable record, not just vanish).
    /// </summary>
    public async Task RunLoop(MainUi ui, string outDir, int maxTurns, int commandTimeoutMs)
    {
        System.IO.Directory.CreateDirectory(outDir);
        var turnLog = new StringBuilder();
        turnLog.AppendLine("# Agent playtest turn log");
        turnLog.AppendLine();

        var lastOutcome = "(run start)";
        var statePath = System.IO.Path.Combine(outDir, "state.json");
        var framePath = System.IO.Path.Combine(outDir, "frame.png");
        var commandPath = System.IO.Path.Combine(outDir, "command.json");

        for (var turn = 1; turn <= maxTurns; turn++)
        {
            await Settle(4); // let the previous action's effects render before observing

            var digest = BuildDigest(ui, turn, lastOutcome);
            System.IO.File.WriteAllText(statePath, JsonSerializer.Serialize(digest, JsonOptions));
            FrameCapture.SaveAsPng(ui.GetViewport(), framePath);

            turnLog.AppendLine($"## Turn {turn}");
            turnLog.AppendLine(
                $"- day {digest.Day} phase {digest.Phase} location {digest.Location} gold {digest.Gold} " +
                $"canMove {digest.CanMove} slots {digest.ActionSlotsRemaining}");
            turnLog.AppendLine($"- screen: {string.Join(" | ", digest.ScreenText)}");
            FlushLog(outDir, turnLog);

            var command = await WaitForCommand(commandPath, commandTimeoutMs);
            if (command is null)
            {
                lastOutcome = $"timed out after {commandTimeoutMs}ms waiting for command.json";
                turnLog.AppendLine($"- command: (none) -> {lastOutcome}");
                FlushLog(outDir, turnLog);
                GD.Print($"[agent-playtest] {lastOutcome} — ending run at turn {turn}.");
                return;
            }

            turnLog.AppendLine(
                $"- command: action={command.Action} target={command.Target} dir={command.Dir} " +
                $"frames={command.Frames} why={command.Why}");
            lastOutcome = await Apply(ui, command);
            turnLog.AppendLine($"- outcome: {lastOutcome}");
            FlushLog(outDir, turnLog);

            if (string.Equals(command.Action, "stop", StringComparison.OrdinalIgnoreCase))
            {
                GD.Print($"[agent-playtest] stop command received at turn {turn}.");
                return;
            }
        }

        GD.Print($"[agent-playtest] max turns ({maxTurns}) reached.");
    }

    private static void FlushLog(string outDir, StringBuilder turnLog) =>
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "turnlog.md"), turnLog.ToString());

    /// <summary>
    /// Polls for <paramref name="path"/> until it exists or <paramref name="timeoutMs"/> elapses.
    /// Deletes the file the instant it is read — successfully or not — so a stale or malformed
    /// command.json is never re-read on the next poll (channel-contract rule: consume, then
    /// delete).
    /// </summary>
    private async Task<AgentCommand?> WaitForCommand(string path, int timeoutMs)
    {
        var deadline = Time.GetTicksMsec() + (ulong)Math.Max(0, timeoutMs);
        while (Time.GetTicksMsec() < deadline)
        {
            if (System.IO.File.Exists(path))
            {
                string text;
                try
                {
                    text = System.IO.File.ReadAllText(path);
                }
                catch (System.IO.IOException)
                {
                    // Writer still has the file open — try again next poll rather than reading a
                    // half-written command.
                    await Settle(2);
                    continue;
                }

                TryDelete(path);
                try
                {
                    var command = JsonSerializer.Deserialize<AgentCommand>(text, JsonOptions);
                    if (command is not null)
                    {
                        return command;
                    }

                    GD.PrintErr("[agent-playtest] command.json parsed to null — ignoring.");
                }
                catch (JsonException ex)
                {
                    GD.PrintErr($"[agent-playtest] command.json malformed, discarded: {ex.Message}");
                }

                continue; // malformed/null command consumed the file; keep waiting for a good one
            }

            await Settle(6);
        }

        return null;
    }

    private static void TryDelete(string path)
    {
        try
        {
            System.IO.File.Delete(path);
        }
        catch (System.IO.IOException)
        {
            // Best-effort — a command left behind after a failed delete will simply be re-read
            // and re-deleted next poll, never silently re-applied twice.
        }
    }

    private async Task Settle(int frames)
    {
        for (var i = 0; i < frames; i++)
        {
            await _pump.ToSignal(_pump.GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }
}

/// <summary>
/// U1 (verify-by-playing plan): the dev-tool bootstrap for <see cref="AgentPlaytestBridge"/> —
/// mounts the REAL <see cref="MainUi"/> (same scene a player launches) and runs the bridge's turn
/// loop until it stops itself. This is the file a local model's driver actually launches
/// (<c>godot --path godot res://agentplaytest.tscn</c>, WINDOWED — headless captures blank
/// frames, same precondition as every sibling tool here).
///
/// <para>Gated behind <c>AGENT_PLAYTEST=1</c> so this never boots by accident — it drives the real
/// client with real input on whatever machine runs it. Output directory from
/// <c>AGENT_PLAYTEST_DIR</c>, matching <see cref="FullPlaytest"/>'s <c>PLAYTEST_OUT</c>
/// convention. <c>AGENT_PLAYTEST_MAX_TURNS</c> and <c>AGENT_PLAYTEST_TIMEOUT_MS</c> override the
/// loop bounds; both have sane defaults so a bare launch is still safe.</para>
/// </summary>
public partial class AgentPlaytest : Node
{
    private const int DefaultMaxTurns = 400;
    private const int DefaultCommandTimeoutMs = 30_000;

    public override async void _Ready()
    {
        if (System.Environment.GetEnvironmentVariable("AGENT_PLAYTEST") != "1")
        {
            GD.PrintErr(
                "[agent-playtest] AGENT_PLAYTEST != 1 — refusing to run. Set the env var to launch " +
                "this tool on purpose; it drives the real client with real input.");
            GetTree().Quit(1);
            return;
        }

        DevToolAudio.Silence(); // automated runs stay silent — see DevToolAudio

        var outDir = ResolveOutDir();
        var maxTurns = EnvInt("AGENT_PLAYTEST_MAX_TURNS", DefaultMaxTurns);
        var timeoutMs = EnvInt("AGENT_PLAYTEST_TIMEOUT_MS", DefaultCommandTimeoutMs);

        var ui = GD.Load<PackedScene>("res://scenes/panels/main_ui.tscn").Instantiate<MainUi>();
        AddChild(ui);
        await Settle(24); // let the fresh campaign finish laying out before the first observation

        var bridge = new AgentPlaytestBridge(this);
        await bridge.RunLoop(ui, outDir, maxTurns, timeoutMs);

        RemoveChild(ui);
        ui.QueueFree();
        GetTree().Quit(0);
    }

    private static string ResolveOutDir()
    {
        var dir = System.Environment.GetEnvironmentVariable("AGENT_PLAYTEST_DIR");
        return (!string.IsNullOrEmpty(dir)
            ? dir.Replace("\\", "/").TrimEnd('/')
            : ProjectSettings.GlobalizePath("res://../runs/agent-playtest").TrimEnd('/')) + "/";
    }

    private static int EnvInt(string name, int fallback)
    {
        var raw = System.Environment.GetEnvironmentVariable(name);
        return int.TryParse(raw, out var value) && value > 0 ? value : fallback;
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
