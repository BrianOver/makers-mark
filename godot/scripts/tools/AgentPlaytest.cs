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
///
/// <para><see cref="Beat"/> (A3, "the shell around the game" plan) is <c>RaidConductor.Current</c>
/// as a string — added because <c>RaidConductor</c> landed after U1 and introduced <see
/// cref="RaidConductor.Beat.VigilStop"/>, a real held-open decision with no timer, and this digest
/// had no field for it at all. Without it the model cannot tell "the world is showing a raid" from
/// "the world is waiting on me at the vigil", and its most likely move at the single most important
/// moment of the day is to <c>advance</c> straight past it.</para>
/// </summary>
public sealed record StateDigest(
    [property: JsonPropertyName("turn")] int Turn,
    [property: JsonPropertyName("day")] int Day,
    [property: JsonPropertyName("phase")] string Phase,
    [property: JsonPropertyName("beat")] string Beat,
    [property: JsonPropertyName("actionSlotsRemaining")] int ActionSlotsRemaining,
    [property: JsonPropertyName("gold")] int Gold,
    [property: JsonPropertyName("location")] string Location,
    [property: JsonPropertyName("canMove")] bool CanMove,
    [property: JsonPropertyName("screenText")] IReadOnlyList<string> ScreenText,
    [property: JsonPropertyName("controls")] IReadOnlyList<ControlDigest> Controls,
    [property: JsonPropertyName("interactPrompt")] string InteractPrompt,
    [property: JsonPropertyName("nearby")] IReadOnlyList<NearbyDigest> Nearby,
    [property: JsonPropertyName("lastOutcome")] string LastOutcome);

/// <summary>
/// One thing the player can walk to and interact with, as seen from where they are standing right now
/// — a town building outdoors, or a station once inside a room (both are <c>Building2D</c>, so one
/// shape covers both).
///
/// <para><b>Why this field exists.</b> The first honest agent run reached day 2 without ever entering
/// a building, and the run's own limitation note blamed a missing verb. That was wrong: the bridge
/// could already reach the interact path through <c>key</c> + the <c>"interact"</c> InputMap action.
/// What the model lacked was the other half of being a player — it could not SEE that a forge existed
/// somewhere to its left. A human reads the town off the screen in one glance; a model handed only
/// button names cannot, so it pressed buttons, which is exactly what it did.</para>
///
/// <para>This stays honest — it reports position and direction, the same information the rendered
/// frame carries, and it does NOT hand over a teleport or a shortcut. The model still has to walk
/// there and still has to press interact, and <see cref="InRange"/> tells it when walking worked,
/// which is what the on-screen interact prompt tells a human.</para>
/// </summary>
public sealed record NearbyDigest(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("direction")] string Direction,
    [property: JsonPropertyName("distance")] int Distance,
    [property: JsonPropertyName("inRange")] bool InRange);

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
/// Why <see cref="AgentPlaytestBridge.RunLoop"/> returned. <see cref="AgentPlaytest._Ready"/> (the
/// only caller that owns process lifecycle — see <see cref="AgentPlaytestBridge"/>'s own doc for why
/// the bridge itself never touches it) turns this into a process exit code via <see
/// cref="AgentPlaytest.ExitCodeFor"/>.
///
/// <para>Before 2026-08-10 this didn't exist: <c>RunLoop</c> returned <c>void</c>, and
/// <c>_Ready</c> called <c>GetTree().Quit(0)</c> unconditionally once it returned, whether the
/// driver said <c>stop</c>, the turn budget ran out, or the driver went SILENT mid-run. In the
/// 2026-08-09 overnight 30-run sweep, <c>Scout-5</c> died this way on turn 1 of 80 — the driver's
/// own per-turn model-call budget (three attempts, 300s each — see <c>tools/agent-playtest.ps1</c>'s
/// <c>$ModelCallTimeoutSec</c>/<c>$ModelCallMaxAttempts</c>) outran <see
/// cref="AgentPlaytest.DefaultCommandTimeoutMs"/>'s old 30-second value, and the run still reported
/// verdict <c>ok</c>, exit 0. An abandoned run read as a completed one.</para>
/// </summary>
public enum AgentPlaytestOutcome
{
    /// <summary>The driver sent an explicit <c>stop</c> command — a real ending.</summary>
    Stopped,

    /// <summary><c>maxTurns</c> turns ran with no <c>stop</c> — a real ending (the budget was spent
    /// on purpose).</summary>
    MaxTurnsReached,

    /// <summary>No <c>command.json</c> arrived within <c>commandTimeoutMs</c> — the driver went
    /// quiet (a model turn slower than the budget, a crashed driver process, ...). This is NOT a
    /// clean ending and must never share an exit code with the two above.</summary>
    TimedOut,
}

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
            Beat: ui.Conductor.Current.ToString(),
            ActionSlotsRemaining: state.ActionSlotsRemaining,
            Gold: state.Player.Gold,
            Location: Location(ui),
            CanMove: ui.Town.WorldInputNode.Enabled,
            ScreenText: ScreenObservation.VisibleText(ui, viewport),
            Controls: controls,
            InteractPrompt: ui.Town.WorldInputNode.PromptText,
            Nearby: Surroundings(ui),
            LastOutcome: lastOutcome);
    }

    /// <summary>How far from a target's door the player counts as "in range" (px). Deliberately
    /// derived from nothing: it is a REPORTING threshold for the model's benefit only. The real
    /// gate is <c>WorldInput2D</c>'s own Area2D overlap, which is why <see
    /// cref="StateDigest.InteractPrompt"/> — the town's actual prompt — travels alongside it as the
    /// authoritative signal. If the two ever disagree, believe the prompt.</summary>
    private const float InRangeReportingPx = 96f;

    /// <summary>
    /// The walkable things around the player, nearest first: town buildings outdoors, or the room's
    /// stations once inside. Both are <c>Building2D</c>, so the interior case needs no second shape.
    /// Empty whenever the player cannot walk at all (a drawer or overlay owns the screen) — in that
    /// state there is nothing to walk to and listing distant buildings would invite the model to try.
    /// </summary>
    private static IReadOnlyList<NearbyDigest> Surroundings(MainUi ui)
    {
        if (ui.Drawer.IsOpen || !ui.Town.WorldInputNode.Enabled)
        {
            return Array.Empty<NearbyDigest>();
        }

        // InteriorActive implies a venue key in practice, but FindInteriorRoom THROWS on a missing
        // one — and this runs inside the per-turn digest, so an inconsistent state would kill the
        // whole playtest turn rather than costing one field. Degrade to "nothing nearby" instead.
        var venueKey = ui.Town.InteriorActive ? ui.Town.InteriorVenueKey : null;
        if (ui.Town.InteriorActive && string.IsNullOrEmpty(venueKey))
        {
            return Array.Empty<NearbyDigest>();
        }

        IReadOnlyList<Town2d.Building2D> targets = venueKey is not null
            ? ui.Town.FindInteriorRoom(venueKey).Stations
            : ui.Town.BuildingsRoot.GetChildren().OfType<Town2d.Building2D>().ToList();

        var from = ui.Town.Player.GlobalPosition;
        return targets
            .Where(GodotObject.IsInstanceValid)
            .Select(b =>
            {
                var offset = b.DoorAnchorGlobal - from;
                return new NearbyDigest(
                    Key: b.Key,
                    Label: b.NameLabel.Text,
                    Direction: Bearing(offset),
                    Distance: (int)Math.Round(offset.Length()),
                    InRange: offset.Length() <= InRangeReportingPx);
            })
            .OrderBy(n => n.Distance)
            .ToList();
    }

    /// <summary>Reduces an offset to the words the move verb actually accepts, so the model can act
    /// on it without doing vector arithmetic: the dominant axis, plus the secondary one when the
    /// offset is genuinely diagonal (both axes within 2x of each other).</summary>
    private static string Bearing(Vector2 offset)
    {
        var horizontal = offset.X >= 0 ? "right" : "left";
        var vertical = offset.Y >= 0 ? "down" : "up";
        var ax = Math.Abs(offset.X);
        var ay = Math.Abs(offset.Y);

        if (ax <= 1f && ay <= 1f)
        {
            return "here";
        }

        var diagonal = ax > 0.5f && ay > 0.5f && Math.Max(ax, ay) / Math.Min(ax, ay) < 2f;
        if (diagonal)
        {
            return ax >= ay ? $"{horizontal}+{vertical}" : $"{vertical}+{horizontal}";
        }

        return ax >= ay ? horizontal : vertical;
    }

    /// <summary>"town", "interior:&lt;venueKey&gt;", "panel:&lt;id&gt;", or "overlay:&lt;name&gt;" —
    /// whichever thing is actually covering the screen right now wins. An overlay (Ledger/Camp/
    /// Mirror/Forecast/Bestiary/Commissions/Legends/the system menu) outranks a drawer panel because
    /// <c>MainUi</c>'s own tray buttons can open one (e.g. "OpenLedger") without first closing
    /// whatever drawer panel happened to be open, and these overlays draw ABOVE the drawer by design
    /// (<c>MainUi.cs</c>'s own "FullRect overlays above the drawer" comments) — so an overlay open at
    /// the same time as a drawer panel is what the player actually sees. A drawer panel in turn takes
    /// priority over a room because <see cref="Town2D.EnterInterior"/> stays active underneath an
    /// opened station drawer (the player is still standing in the room; the drawer is what is
    /// actually covering the screen right now).
    ///
    /// <para><b>2026-08-12 (coverage-can-see-the-overlays finding A):</b> before the overlay check,
    /// this method could only ever return "panel:" (from <see cref="DrawerHost.Register"/>), "interior:",
    /// or "town" — none of the seven <see cref="MainUi.ActiveOverlayName"/> surfaces are drawer panels,
    /// so opening the Ledger or the Camp panel silently reported as "town", byte-identical to never
    /// opening either. Reuses <see cref="MainUi.ActiveOverlayName"/> rather than re-deriving the same
    /// name list a second time here.</para>
    /// </summary>
    private static string Location(MainUi ui)
    {
        if (ui.ActiveOverlayName() is { } overlay)
        {
            return $"overlay:{overlay}";
        }

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

    /// <summary>
    /// fix/pilot-finds-its-way, TWO fixes on this same line, found in that order.
    ///
    /// <para><b>First (2026-08-11, superseded below):</b> this used to call <see
    /// cref="Input.ActionPress"/>/<c>ActionRelease</c>, which per Godot's own documented contract
    /// updates ONLY the polled <see cref="Input.IsActionPressed"/> state and never calls any node's
    /// <c>_Input</c> — "If you want to simulate _input, use Input.ParseInputEvent instead" (the Godot
    /// API doc, verbatim, and the actual fix below turned out to be exactly that sentence). That
    /// silently broke every <c>_Input</c>-based handler (<c>MainUi</c>'s own Escape ladder) and every
    /// focus-gated <c>_GuiInput</c> handler (the forge minigame's bellows/strike/plunge). Engine-test-
    /// pinned by <c>KeyCancel_InsideAWalkableRoom_ExitsIt</c>.</para>
    ///
    /// <para><b>Second (2026-08-11, the actual bug): swapping to <c>viewport.PushInput</c> fixed
    /// cancel and broke nothing, but a live 220-turn pilot run standing 8px from the forge's own door
    /// pressed "interact" three times with zero effect and never entered a single building in 9
    /// in-game days.</b> <c>Viewport.PushInput</c> dispatches the event through THAT viewport's own
    /// <c>_Input</c>/<c>_UnhandledInput</c>/<c>_GuiInput</c> chain — which is why cancel started
    /// working — but it does NOT update the global <see cref="Input"/> singleton's polled action
    /// state at all. Proven directly (temporary diagnostic test, since removed): immediately after
    /// <c>viewport.PushInput(new InputEventKey{Pressed=true})</c>, <c>Input.IsActionPressed("interact")</c>
    /// read false on every one of the next 6 physics frames — not delayed, never true. <see
    /// cref="Town2d.WorldInput2D._PhysicsProcess"/> gates building entry on exactly that polled state
    /// (<c>Input.IsActionJustPressed("interact")</c>), so <c>PushInput</c> alone can dispatch a
    /// perfectly good "cancel" _Input event while being permanently invisible to any interact/move
    /// style POLLING reader — the two Godot subsystems are genuinely separate, and neither
    /// <c>ActionPress</c>/<c>ActionRelease</c> nor <c>PushInput</c> alone is a complete key-press
    /// simulation; only <see cref="Input.ParseInputEvent"/> is (the same "front door" a real OS key
    /// event enters through — updates the polled action map AND flows into the normal
    /// <c>_Input</c>/<c>_GuiInput</c> dispatch a real hardware event would reach). Re-run of the same
    /// diagnostic with <c>Input.ParseInputEvent</c> in place of <c>PushInput</c>: <c>IsActionJustPressed</c>
    /// true on the very next physics frame, forge entered. Engine-test-pinned by
    /// <c>KeyInteract_AtForgeDoorAtSpawn_EntersTheForge</c> (both this one and cancel's own pin still
    /// green with the same call, since ParseInputEvent is the superset of what PushInput did).</para>
    /// </summary>
    private async Task<string> ApplyKey(MainUi ui, AgentCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Target) || !InputMap.HasAction(command.Target))
        {
            return $"refused: no InputMap action named '{command.Target}'";
        }

        var key = PhysicalKeyFor(command.Target);
        if (key is null)
        {
            return $"refused: InputMap action '{command.Target}' has no physical keyboard event bound to it";
        }

        Godot.Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key.Value, Keycode = key.Value, Pressed = true });
        await Settle(3);
        Godot.Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key.Value, Keycode = key.Value, Pressed = false });
        await Settle(3);
        return $"tapped key '{command.Target}'";
    }

    /// <summary>The first physical key <paramref name="action"/> is bound to, or null for an action
    /// with no keyboard event at all (e.g. a future joypad-only binding) — every action this bridge is
    /// ever asked to press today (cancel/interact/bellows/forge_strike/plunge/...) is keyboard-bound by
    /// <c>TownInput</c>/<c>MinigameInput</c>, so null here means a genuinely unpressable request rather
    /// than a gap this method should silently paper over.</summary>
    private static Key? PhysicalKeyFor(string action)
    {
        foreach (var evt in InputMap.ActionGetEvents(action))
        {
            if (evt is InputEventKey key)
            {
                return key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode;
            }
        }

        return null;
    }

    private static string ApplyAdvance(MainUi ui)
    {
        var before = ui.Adapter.CurrentState;
        // Attributed, not a bare Adapter.AdvancePhase() — see AdvancePhaseWithCause's own doc for why
        // an un-tagged automated tick would read exactly like the bug this playtest trail exists to
        // catch (an unexplained phase advance) even though the driver genuinely asked for this one.
        ui.AdvancePhaseWithCause("press:bridge-advance");
        var after = ui.Adapter.CurrentState;
        return $"advanced -> day {before.Day} {before.Phase} to day {after.Day} {after.Phase}";
    }

    /// <summary>
    /// Parses a move direction, accepting a composite like <c>"right+down"</c> as well as a single
    /// axis. The composite form is not a nicety: <see cref="Bearing"/> reports diagonal targets that
    /// way, and a first agent run refused three moves with <c>unknown move dir 'right+down'</c> —
    /// the harness telling the model a direction its own move verb could not act on.
    /// </summary>
    private static Vector2? ParseDirection(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir))
        {
            return null;
        }

        var total = Vector2.Zero;
        foreach (var part in dir.Trim().ToLowerInvariant().Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            var axis = part.Trim() switch
            {
                "up" => new Vector2(0, -1),
                "down" => new Vector2(0, 1),
                "left" => new Vector2(-1, 0),
                "right" => new Vector2(1, 0),
                _ => Vector2.Zero,
            };

            if (axis == Vector2.Zero)
            {
                return null; // any unknown component makes the whole direction untrustworthy
            }

            total += axis;
        }

        // Opposed components ("left+right") cancel to zero — refuse rather than silently stand still.
        return total == Vector2.Zero ? null : total.Normalized();
    }

    /// <summary>
    /// The full turn loop: settle, observe (state.json + frame.png), poll for command.json,
    /// apply, log, repeat — until <c>stop</c>, <paramref name="maxTurns"/>, or a command-file
    /// timeout. Every branch writes <c>turnlog.md</c> before returning, including the timeout
    /// branch (R2: a run that goes quiet must still leave a readable record, not just vanish).
    ///
    /// <para>Returns which of those three endings happened (<see cref="AgentPlaytestOutcome"/>) —
    /// this method itself never calls <c>GetTree().Quit</c>, on purpose, so the timeout/clean
    /// distinction is provable without booting the process-quitting dev-tool bootstrap in
    /// <see cref="AgentPlaytest._Ready"/>.</para>
    /// </summary>
    public async Task<AgentPlaytestOutcome> RunLoop(MainUi ui, string outDir, int maxTurns, int commandTimeoutMs)
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
            var (frameImage, frameSaveError) = FrameCapture.SaveAsPng(ui.GetViewport(), framePath);
            var frameLooksReal = FrameLooksReal(frameImage);
            if (!frameLooksReal)
            {
                GD.PrintErr(
                    $"[agent-playtest] turn {turn}: frame.png is BLANK/UNIFORM (save={frameSaveError}) — " +
                    "the eye is not actually open. See FrameCapture.cs's --headless contract.");
            }

            turnLog.AppendLine($"## Turn {turn}");
            turnLog.AppendLine(
                $"- day {digest.Day} phase {digest.Phase} beat {digest.Beat} location {digest.Location} " +
                $"gold {digest.Gold} canMove {digest.CanMove} slots {digest.ActionSlotsRemaining}");
            turnLog.AppendLine($"- screen: {string.Join(" | ", digest.ScreenText)}");
            turnLog.AppendLine(
                $"- frame: {(frameLooksReal ? "captured (non-blank)" : "BLANK/UNIFORM — degraded capture, see FrameCapture.cs headless contract")}");
            FlushLog(outDir, turnLog);

            var command = await WaitForCommand(commandPath, commandTimeoutMs);
            if (command is null)
            {
                // "TIMEOUT" (not just "timed out") so this reads as a NAMED failure mode when
                // grepped, the same register as the driver's own DEGRADED/INCOMPLETE/STUCK tags —
                // see AgentPlaytestOutcome's doc for the run this distinction exists to catch.
                lastOutcome = $"TIMEOUT: timed out after {commandTimeoutMs}ms waiting for command.json";
                turnLog.AppendLine($"- command: (none) -> {lastOutcome}");
                FlushLog(outDir, turnLog);
                GD.PrintErr($"[agent-playtest] {lastOutcome} — ending run at turn {turn}. This is an ABANDONED run, not a completed one.");
                return AgentPlaytestOutcome.TimedOut;
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
                return AgentPlaytestOutcome.Stopped;
            }
        }

        GD.Print($"[agent-playtest] max turns ({maxTurns}) reached.");
        return AgentPlaytestOutcome.MaxTurnsReached;
    }

    private static void FlushLog(string outDir, StringBuilder turnLog) =>
        System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "turnlog.md"), turnLog.ToString());

    /// <summary>
    /// True when <paramref name="image"/> shows real variation rather than one flat color — ported
    /// from <see cref="FullPlaytest"/>'s own blank-capture check (<c>Shot</c>, FullPlaytest.cs
    /// ~977-1001) so the bridge uses the SAME approach instead of a second, independently-drifting
    /// one. <c>--headless</c> produces a blank-but-valid PNG by contract (see <see
    /// cref="FrameCapture"/>'s own doc comment) — before this, nothing in the bridge's own path ever
    /// checked for it, which is exactly how a run can prove the file channel works while the eye is
    /// actually closed.
    /// </summary>
    public static bool FrameLooksReal(Image image)
    {
        var data = image.GetData();
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
///
/// <para><b>Exit code contract</b> (see <see cref="AgentPlaytestOutcome"/>): 1 means this tool
/// refused to launch at all (bad gate env var); 0 means the run reached a REAL ending (<c>stop</c>
/// or max turns); anything from <see cref="ExitCodeFor"/> that is non-zero past that point means
/// the driver went silent and the run was abandoned mid-turn, not completed. A wrapper that only
/// checks "exit 0" for success used to see the same 0 for all three (see
/// <see cref="AgentPlaytestOutcome"/>'s doc for the run that cost).</para>
/// </summary>
public partial class AgentPlaytest : Node
{
    private const int DefaultMaxTurns = 400;

    /// <summary>
    /// Fallback ONLY — used when <c>AGENT_PLAYTEST_TIMEOUT_MS</c> is absent (a manual launch with
    /// no driver, or a driver older than this fix). The real per-run number always comes from the
    /// env var: <c>tools/agent-playtest.ps1</c>'s "Launch the client" section computes it from its
    /// own <c>$ModelCallMaxAttempts</c> * <c>$ModelCallTimeoutSec</c> and sets
    /// <c>AGENT_PLAYTEST_TIMEOUT_MS</c> before starting this process, precisely so the two halves
    /// of this channel stop being unrelated numbers in two languages.
    ///
    /// <para>This fallback MUST exceed the driver's own worst-case per-turn budget, or the defect
    /// it exists to prevent reopens. Measured 2026-08-10: the driver retries a model call up to
    /// <c>$ModelCallMaxAttempts</c> (3) times at <c>$ModelCallTimeoutSec</c> (300) seconds each
    /// before giving up on a turn — worst case 3 * 300s = 900_000ms — while this constant used to
    /// be 30_000, ten times too small to survive even one slow attempt. If either number changes in
    /// <c>tools/agent-playtest.ps1</c>, update <see cref="DriverModelCallTimeoutMs"/> /
    /// <see cref="DriverModelCallMaxAttempts"/> below to match; a unit test in
    /// <c>AgentPlaytestBridgeTests</c> reads the driver's actual numbers straight out of the
    /// <c>.ps1</c> file and fails loudly the moment this fallback stops exceeding them.</para>
    /// </summary>
    private const int DriverModelCallTimeoutMs = 300_000;
    private const int DriverModelCallMaxAttempts = 3;
    private const int DriverWorstCaseTurnMs = DriverModelCallMaxAttempts * DriverModelCallTimeoutMs;
    private const int DefaultCommandTimeoutMs = DriverWorstCaseTurnMs + 50_000; // margin over the driver's own worst case

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
        StampWindowTitle(); // see StampWindowTitle's own doc — the persona-visibility fix

        var outDir = ResolveOutDir();
        var maxTurns = EnvInt("AGENT_PLAYTEST_MAX_TURNS", DefaultMaxTurns);
        var timeoutMs = EnvInt("AGENT_PLAYTEST_TIMEOUT_MS", DefaultCommandTimeoutMs);

        var ui = GD.Load<PackedScene>("res://scenes/panels/main_ui.tscn").Instantiate<MainUi>();
        AddChild(ui);
        await Settle(24); // let the fresh campaign finish laying out before the first observation

        var bridge = new AgentPlaytestBridge(this);
        var outcome = await bridge.RunLoop(ui, outDir, maxTurns, timeoutMs);

        RemoveChild(ui);
        ui.QueueFree();
        GetTree().Quit(ExitCodeFor(outcome));
    }

    /// <summary>
    /// The process exit code a launcher/CI wrapper sees. Only <see
    /// cref="AgentPlaytestOutcome.TimedOut"/> is non-zero — an abandoned run is not a completed
    /// one, and <c>tools/agent-playtest.ps1</c>'s own completion-floor exit (PR #436, INCOMPLETE /
    /// DEGRADED, computed from turn COUNT) should not be the only signal that ever notices a run
    /// died mid-turn: this is the CLIENT's own signal, independent of whatever the driver script
    /// does or does not check on its side of the channel.
    /// </summary>
    public static int ExitCodeFor(AgentPlaytestOutcome outcome) =>
        outcome == AgentPlaytestOutcome.TimedOut ? 2 : 0;

    /// <summary>
    /// fix/the-pilot-plays-like-a-person: an owner watching this client play (over someone's shoulder,
    /// not through the driver's own logs) had no way to tell WHICH player was driving it — monkey's
    /// own uniform-random command stream (see monkey.ps1's own header) is, by design, indistinguishable
    /// from a person mashing buttons; -Persona pilot's habit-forming curiosity detours and seeded
    /// business-decision coin flips can look similarly unplanned to someone who does not know that
    /// design; and even a model-driven persona's occasional free-form near-empty command reads the
    /// same way. The window title is the one piece of on-screen truth this tool can add without
    /// touching godot/scripts/ui/ (owned by another lane's visual pass this same session) or the
    /// action vocabulary itself: whoever is looking at the taskbar/title bar can always read off
    /// exactly which persona is driving, straight from the SAME env var the driver already prints to
    /// its own console (agent-playtest.ps1's own "persona: X (requested: Y)" Say line).
    ///
    /// <para>Absent env var (a manual launch with no driver at all, or a driver older than this fix)
    /// -> "(unknown)", never a blank title a viewer could mistake for "this is not an automated run."
    /// <see cref="DisplayServer.WindowSetTitle"/> is a documented no-op under <c>--headless</c> (same
    /// as <see cref="DisplayServer.WindowSetMode"/>, per <c>UiSettings.cs</c>'s own note), so this is
    /// harmless in any engine test that happens to instantiate this scene headless.</para>
    /// </summary>
    private static void StampWindowTitle()
    {
        var persona = System.Environment.GetEnvironmentVariable("AGENT_PLAYTEST_PERSONA");
        if (string.IsNullOrWhiteSpace(persona)) { persona = "(unknown)"; }
        DisplayServer.WindowSetTitle("AGENT PLAYTEST (automated) -- persona: " + persona);
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
