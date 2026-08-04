#if GDUNIT_TESTS
using System;
using System.Linq;
using System.Threading.Tasks;
using GameSim.Contracts;
using GdUnit4;
using Godot;
using GodotClient.Tools;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U1 (verify-by-playing plan): proves the agent observe/act bridge itself with SCRIPTED
/// commands and no model in the loop — the plan's own execution note for U2 applies here first:
/// "build the driver against U1's scripted-command path first... a model in the loop while the
/// channel is still unproven makes every failure ambiguous." One test per bullet in the plan's
/// U1 "Test scenarios" list.
///
/// <para>Drives <see cref="AgentPlaytestBridge"/> directly against an already-mounted
/// <see cref="MainUi"/> rather than booting <see cref="AgentPlaytest"/> itself — that Node's own
/// <c>_Ready</c> is the env-var-gated, scene-loading, process-quitting dev-tool bootstrap, which
/// is exactly the part that must NOT run inside the engine suite (it would tear down the test
/// host). The bridge is the testable half by design; see its own doc comment.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class AgentPlaytestBridgeTests
{
    [TestCase]
    public void FreshDigest_ListsDayPhaseAndAtLeastOneEnabledControl()
    {
        var ui = MountMainUi();
        try
        {
            var bridge = new AgentPlaytestBridge(ui);
            var digest = bridge.BuildDigest(ui, turn: 1, lastOutcome: "(start)");

            AssertThat(digest.Day)
                .OverrideFailureMessage($"Expected day 1 on a freshly mounted campaign, got {digest.Day}.")
                .IsEqual(1);
            AssertThat(digest.Phase)
                .OverrideFailureMessage($"Expected Morning as the opening phase, got '{digest.Phase}'.")
                .IsEqual("Morning");
            AssertThat(digest.Controls.Any(c => c.Enabled))
                .OverrideFailureMessage(
                    "A freshly mounted client's digest has NO enabled control — a local model reading " +
                    "this on turn 1 would have nothing legal to press. Controls seen: " +
                    $"[{string.Join(", ", digest.Controls.Select(c => $"{c.Name}({c.Enabled})"))}].")
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public async Task PressOnEnabledButton_ChangesTheDigest()
    {
        var ui = MountMainUi();
        try
        {
            ui.OpenPanel("Forge");
            await SettleLayout(ui);
            var bridge = new AgentPlaytestBridge(ui);
            var buttonName = $"BuyMat_{ScriptedSession.CraftMaterial}";

            var before = bridge.BuildDigest(ui, 1, "(start)");
            var outcome = await bridge.Apply(ui, new AgentCommand("press", buttonName, Why: "need material"));
            var after = bridge.BuildDigest(ui, 2, outcome);

            AssertThat(outcome)
                .OverrideFailureMessage($"Pressing an enabled, on-screen control was refused: '{outcome}'.")
                .StartsWith("pressed");
            AssertThat(after.Gold)
                .OverrideFailureMessage(
                    $"Gold is still {after.Gold} after pressing '{buttonName}' (was {before.Gold}) — the " +
                    "bridge's press must go through the real EmitSignal(Pressed) path a click drives, " +
                    "not a silent no-op.")
                .IsLess(before.Gold);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public async Task PressOnDisabledButton_IsRefusedWithAReason_AndDigestIsUnchanged()
    {
        var ui = MountMainUi();
        try
        {
            ui.OpenPanel("Forge");
            await SettleLayout(ui);
            var bridge = new AgentPlaytestBridge(ui);
            var buttonName = $"BuyMat_{ScriptedSession.CraftMaterial}";

            // Burn the day's whole action budget through the bridge itself — the only way a real
            // player could — so the vendor row goes Disabled for the genuine reason (no slots left),
            // not a fabricated one. Mirrors BuyUpdatesTheCountImmediatelyTests's own burn-down.
            for (var i = 0; i < ActionBudget.SlotsPerDay; i++)
            {
                var burn = await bridge.Apply(ui, new AgentCommand("press", buttonName));
                AssertThat(burn)
                    .OverrideFailureMessage(
                        $"Burn-down press #{i} was refused ('{burn}') before the day's action budget was " +
                        "spent — this test can no longer prove what it claims to.")
                    .StartsWith("pressed");
            }

            var before = bridge.BuildDigest(ui, 90, "(pre-refusal)");
            var outcome = await bridge.Apply(ui, new AgentCommand("press", buttonName));
            var after = bridge.BuildDigest(ui, 91, outcome);

            AssertThat(outcome)
                .OverrideFailureMessage(
                    $"Pressing a Disabled control did not read as a refusal: '{outcome}'. A disabled " +
                    "button must never be silently no-op'd OR silently succeed — the model has to be " +
                    "TOLD, in the outcome text, exactly like a real click would fail here.")
                .StartsWith("refused:");
            AssertThat(after.Gold)
                .OverrideFailureMessage("Gold changed even though the press was refused — a refusal must never have a side effect.")
                .IsEqual(before.Gold);
            AssertThat(string.Join("\n", after.ScreenText))
                .OverrideFailureMessage("screenText changed even though the press was refused.")
                .IsEqual(string.Join("\n", before.ScreenText));
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public async Task PressOnNonexistentControl_IsRefused_NeverACrash()
    {
        var ui = MountMainUi();
        try
        {
            var bridge = new AgentPlaytestBridge(ui);

            // No try/catch here on purpose: if Apply throws, the test fails with that exception,
            // which is exactly the "never a crash" property under test.
            var outcome = await bridge.Apply(ui, new AgentCommand("press", "ThisControlDoesNotExist_Zzz"));

            AssertThat(outcome)
                .OverrideFailureMessage($"Expected a refusal naming the missing control, got '{outcome}'.")
                .StartsWith("refused:");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public async Task MoveInsideARoom_ChangesThePlayersPosition()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.EnterInterior("forge");
            await SettleLayout(ui);
            var bridge = new AgentPlaytestBridge(ui);

            var digest = bridge.BuildDigest(ui, 1, "(entered forge)");
            AssertThat(digest.Location)
                .OverrideFailureMessage($"Expected an interior location after EnterInterior(\"forge\"), got '{digest.Location}'.")
                .IsEqual("interior:forge");
            AssertThat(digest.CanMove)
                .OverrideFailureMessage(
                    "canMove read false inside a freshly entered room — this is exactly the frozen-room " +
                    "regression PR #379 fixed (\"unable to move around inside the forge\"). A room must " +
                    "never gate movement the way a drawer/overlay does.")
                .IsTrue();

            var before = ui.Town.Player.GlobalPosition;
            var outcome = await bridge.Apply(ui, new AgentCommand("move", Dir: "right", Frames: 30));
            var after = ui.Town.Player.GlobalPosition;

            AssertThat(outcome)
                .OverrideFailureMessage($"Move command was refused inside a walkable room: '{outcome}'.")
                .StartsWith("moved");
            AssertThat(after.DistanceTo(before))
                .OverrideFailureMessage(
                    $"Player did not move inside the room: before={before} after={after}, outcome='{outcome}'. " +
                    "The move command must drive PlayerController2D.SetDirectInput, the same real seam " +
                    "CameraFocusBeatTests uses, not merely report success.")
                .IsGreater(1f);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// The observation has to tell the model the town EXISTS. The first honest agent run reached day 2
    /// without ever entering a building; its own limitation note blamed a missing "enter" verb, which
    /// was wrong — <c>key</c> + the <c>interact</c> InputMap action already reached the same code path a
    /// human's E press does. The real gap was that a model handed only button names has no way to know
    /// a forge is somewhere to its left, so it pressed buttons, which is exactly what it did.
    ///
    /// <para>This pins the fix as a PLAYER could use it: the forge is listed, it has a direction and a
    /// distance, and walking that direction actually closes the distance. Anything less and the model
    /// is guessing.</para>
    /// </summary>
    [TestCase]
    public async Task Surroundings_NameTheTownsBuildings_WithADirectionThatActuallyWorks()
    {
        var ui = MountMainUi();
        try
        {
            await SettleLayout(ui);
            var bridge = new AgentPlaytestBridge(ui);

            var digest = bridge.BuildDigest(ui, 1, "(start)");
            AssertThat(digest.Nearby.Count)
                .OverrideFailureMessage(
                    "The digest listed nothing to walk to while standing in the town. A model with no " +
                    "surroundings cannot enter a building, which is how a green suite coexisted with a " +
                    "run that never went indoors.")
                .IsGreater(0);

            var forge = digest.Nearby.FirstOrDefault(n => n.Key.Equals("forge", System.StringComparison.OrdinalIgnoreCase));
            AssertThat(forge)
                .OverrideFailureMessage(
                    "The forge — the building this game is ABOUT — was not among the things reported as " +
                    $"reachable. Reported: {string.Join(", ", digest.Nearby.Select(n => n.Key))}.")
                .IsNotNull();
            AssertThat(forge!.InRange)
                .OverrideFailureMessage(
                    $"The forge was {forge.Distance}px away and not reported in range, even though the " +
                    "player spawns on its doorstep. A first turn that cannot tell it is already standing " +
                    "at the forge is the whole problem this field exists to fix.")
                .IsTrue();

            // The direction must be ACTIONABLE, not decorative: moving the way it points has to reduce
            // the distance. Measured against a FAR building on purpose — the forge is 8px away at spawn
            // and walking into it just presses the player against its own footprint, which would test
            // collision rather than the bearing.
            //
            // Send the bearing back VERBATIM. An earlier version of this test split off the primary
            // axis first, which hid the defect a real agent run then found: the move verb rejected
            // three commands with "unknown move dir 'right+down'" because Bearing reports diagonals
            // that way. A test that sanitises the harness's own output cannot catch the harness lying.
            var far = digest.Nearby.OrderByDescending(n => n.Distance).First();
            var outcome = await bridge.Apply(ui, new AgentCommand("move", Dir: far.Direction, Frames: 40));
            AssertThat(outcome).StartsWith("moved");

            var closer = bridge.BuildDigest(ui, 2, outcome).Nearby.First(n => n.Key == far.Key);
            AssertThat(closer.Distance)
                .OverrideFailureMessage(
                    $"Walking '{far.Direction}' — the direction the digest itself reported for " +
                    $"{far.Key} — did not get closer to it: {far.Distance}px -> " +
                    $"{closer.Distance}px. The bearing is wrong, so a model following it walks away.")
                .IsLess(far.Distance);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// Every bearing <see cref="AgentPlaytestBridge"/> can emit must be a direction its own move verb
    /// accepts. A real agent run refused three moves with <c>unknown move dir 'right+down'</c> — the
    /// harness handing the model a word it would then reject. This walks the diagonal forms explicitly
    /// so the two halves cannot drift apart again.
    /// </summary>
    [TestCase]
    public async Task EveryBearingTheHarnessEmits_IsADirectionTheMoveVerbAccepts()
    {
        var ui = MountMainUi();
        try
        {
            await SettleLayout(ui);
            var bridge = new AgentPlaytestBridge(ui);

            string[] bearings =
            [
                "up", "down", "left", "right",
                "right+down", "down+right", "left+up", "up+left", "right+up", "down+left",
            ];

            foreach (var bearing in bearings)
            {
                var outcome = await bridge.Apply(ui, new AgentCommand("move", Dir: bearing, Frames: 2));
                AssertThat(outcome)
                    .OverrideFailureMessage(
                        $"The move verb refused '{bearing}', a bearing the digest itself can report: " +
                        $"'{outcome}'. Every direction the harness emits must be one it accepts.")
                    .StartsWith("moved");
            }

            // Nonsense and self-cancelling input must still be refused rather than silently standing
            // still — a move that reports success without moving is how a dead harness looks healthy.
            foreach (var bad in new[] { "sideways", "left+right", "up+banana", "+", "" })
            {
                AssertThat(await bridge.Apply(ui, new AgentCommand("move", Dir: bad, Frames: 2)))
                    .OverrideFailureMessage($"The move verb accepted the meaningless direction '{bad}'.")
                    .StartsWith("refused");
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Inside a room the same field must switch to the room's own stations — otherwise the
    /// model gets indoors and is once again blind, which is where the interiors work actually needs
    /// checking (the owner's "why have the interactive things if they just open the same menu").</summary>
    [TestCase]
    public async Task Surroundings_BecomeTheRoomsStations_OnceInside()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.EnterInterior("forge");
            await SettleLayout(ui);
            var bridge = new AgentPlaytestBridge(ui);

            var inside = bridge.BuildDigest(ui, 1, "(entered forge)").Nearby;
            AssertThat(inside.Count)
                .OverrideFailureMessage("Inside the forge, the digest reported nothing to walk to.")
                .IsGreater(0);

            var stationKeys = ui.Town.FindInteriorRoom("forge").Stations.Select(s => s.Key).ToHashSet();
            AssertThat(inside.All(n => stationKeys.Contains(n.Key)))
                .OverrideFailureMessage(
                    "Inside a room the surroundings must be that room's stations, not the town's " +
                    $"buildings. Reported: {string.Join(", ", inside.Select(n => n.Key))}; the room's " +
                    $"stations are: {string.Join(", ", stationKeys)}.")
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public async Task CommandTimeout_EndsTheLoopCleanly_AndWritesWhatItHad()
    {
        var ui = MountMainUi();
        var outDir = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "agent-playtest-bridge-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var bridge = new AgentPlaytestBridge(ui);

            // command.json is never written. The short timeout is the entire point of this test:
            // prove the loop ends ITSELF instead of hanging forever on a driver that never answers
            // (the plan's R2 requirement — a stuck loop is a reported finding, never a silent hang).
            await bridge.RunLoop(ui, outDir, maxTurns: 5, commandTimeoutMs: 300);

            var statePath = System.IO.Path.Combine(outDir, "state.json");
            var logPath = System.IO.Path.Combine(outDir, "turnlog.md");
            AssertThat(System.IO.File.Exists(statePath))
                .OverrideFailureMessage($"state.json was never written under {outDir} — a timed-out turn must still leave its last observation on disk.")
                .IsTrue();
            AssertThat(System.IO.File.Exists(logPath))
                .OverrideFailureMessage($"turnlog.md was never written under {outDir} — R2: a run that goes quiet must still leave a readable record, not vanish.")
                .IsTrue();

            var log = System.IO.File.ReadAllText(logPath);
            AssertThat(log.Contains("timed out", StringComparison.OrdinalIgnoreCase))
                .OverrideFailureMessage($"turnlog.md does not record the timeout anywhere:\n{log}")
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
            if (System.IO.Directory.Exists(outDir))
            {
                System.IO.Directory.Delete(outDir, recursive: true);
            }
        }
    }
}
#endif
