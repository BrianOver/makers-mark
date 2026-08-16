#if GDUNIT_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GameSim.Contracts;
using GdUnit4;
using Godot;
using GodotClient.Minigames;
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

    /// <summary>
    /// 2026-08-12, "the evidence channel says when it dies" (an 8-lens adversarial audit): two new
    /// StateDigest fields exist so the OUTSIDE driver can tell an evidence-channel failure from a real
    /// one instead of inferring it (finding A: <see cref="StateDigest.BackendLogActive"/>; finding B:
    /// <see cref="StateDigest.PreviousFrameOk"/>). BackendLogActive must be <see
    /// cref="PlaytestLog.Active"/> AT THE MOMENT <c>BuildDigest</c> runs, not a value captured once —
    /// proven here by toggling the SAME test seam <c>PlaytestLogTests.cs</c> uses
    /// (<see cref="PlaytestLog.RedirectForTests"/>), armed and disarmed within this one test case so
    /// the shared static never leaks into a sibling test (that class's own header explains why one
    /// case owning the static start to finish is the shape that cannot race itself).
    /// PreviousFrameOk is the caller's own explicit input, threaded straight through with no
    /// transformation — proven by round-tripping both real values and the default "no previous turn
    /// yet" null.
    /// </summary>
    [TestCase]
    public void BuildDigest_CarriesBackendLogActiveAndPreviousFrameOk()
    {
        var ui = MountMainUi();
        try
        {
            var bridge = new AgentPlaytestBridge(ui);

            // Default: no previousFrameOk argument at all (every call site before this fix, and every
            // engine test elsewhere in this suite) must read null, never a guessed true/false — "no
            // data yet" is the honest turn-1 shape (StateDigest's own 2026-08-12 doc note).
            var defaultDigest = bridge.BuildDigest(ui, 1, "(start)");
            AssertThat(defaultDigest.PreviousFrameOk.HasValue)
                .OverrideFailureMessage("BuildDigest with no previousFrameOk argument must leave the field null, not a guessed value.")
                .IsFalse();

            var trueDigest = bridge.BuildDigest(ui, 2, "(start)", previousFrameOk: true);
            AssertThat(trueDigest.PreviousFrameOk.HasValue).IsTrue();
            AssertThat(trueDigest.PreviousFrameOk!.Value)
                .OverrideFailureMessage("An explicit previousFrameOk:true must thread straight through to the digest.")
                .IsTrue();

            var falseDigest = bridge.BuildDigest(ui, 3, "(start)", previousFrameOk: false);
            AssertThat(falseDigest.PreviousFrameOk.HasValue).IsTrue();
            AssertThat(falseDigest.PreviousFrameOk!.Value)
                .OverrideFailureMessage("An explicit previousFrameOk:false must thread straight through, not collapse to null or true.")
                .IsFalse();

            // BackendLogActive must track PlaytestLog.Active live. The engine suite runs with
            // MM_PLAYTEST_LOG unset, so Active is false by default — this is a setup check, not the
            // behavior under test, but a green suite where it were somehow true would invalidate
            // everything below it.
            AssertThat(PlaytestLog.Active)
                .OverrideFailureMessage("Setup check: PlaytestLog must be disarmed by default in the engine suite.")
                .IsFalse();
            AssertThat(bridge.BuildDigest(ui, 4, "(start)").BackendLogActive)
                .OverrideFailureMessage("BuildDigest.BackendLogActive must read false while PlaytestLog is disarmed.")
                .IsFalse();

            var logPath = ProjectSettings.GlobalizePath("user://playtest-log-digest-liveness-test.jsonl");
            PlaytestLog.RedirectForTests(logPath);
            try
            {
                AssertThat(bridge.BuildDigest(ui, 5, "(start)").BackendLogActive)
                    .OverrideFailureMessage("BuildDigest.BackendLogActive must read true once PlaytestLog is armed via RedirectForTests.")
                    .IsTrue();
            }
            finally
            {
                PlaytestLog.RedirectForTests(null);
            }

            AssertThat(bridge.BuildDigest(ui, 6, "(start)").BackendLogActive)
                .OverrideFailureMessage(
                    "BuildDigest.BackendLogActive must read false again once PlaytestLog is disarmed — a " +
                    "stale 'true' here would be exactly the false confidence finding A exists to prevent.")
                .IsFalse();
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
    /// 2026-08-12 (coverage-can-see-the-overlays finding A): <see cref="Location"/> used to check only
    /// <c>ui.Drawer.IsOpen</c> and <c>ui.Town.InteriorActive</c> — the Ledger, Camp, Scrying Mirror,
    /// Forecast, Bestiary, Commissions, and Legends overlays all bypass the drawer by design
    /// (<c>MainUi.cs</c>'s own "FullRect overlays above the drawer" comments), so opening any one of
    /// them reported the exact same location string ("town") as never opening it at all. A full
    /// playthrough that opened the Ledger every evening produced byte-identical coverage to a run that
    /// never touched it. This pins the fix: an open overlay must report a distinct, named location.
    /// </summary>
    [TestCase]
    public void OpenOverlay_ReportsLocationDistinctFromTown()
    {
        var ui = MountMainUi();
        try
        {
            var bridge = new AgentPlaytestBridge(ui);

            var before = bridge.BuildDigest(ui, turn: 1, lastOutcome: "(start)");
            AssertThat(before.Location)
                .OverrideFailureMessage($"Setup check: expected location 'town' before any overlay opens, got '{before.Location}'.")
                .IsEqual("town");

            ui.Ledger.ShowFor(1);
            var afterLedger = bridge.BuildDigest(ui, turn: 2, lastOutcome: "(opened ledger)");
            AssertThat(afterLedger.Location)
                .OverrideFailureMessage(
                    $"Expected a distinct overlay location once the Ledger opened, got '{afterLedger.Location}'. " +
                    "A Ledger visit must never read the same as never opening it.")
                .IsEqual("overlay:Ledger");
            AssertThat(afterLedger.Location)
                .OverrideFailureMessage("The Ledger overlay must not report as the plain town location.")
                .IsNotEqual("town");

            ui.Ledger.CloseModal();
            var afterClose = bridge.BuildDigest(ui, turn: 3, lastOutcome: "(closed ledger)");
            AssertThat(afterClose.Location)
                .OverrideFailureMessage($"Expected 'town' again once the Ledger closed, got '{afterClose.Location}'.")
                .IsEqual("town");
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

    /// <summary>
    /// A2 ("the shell around the game" plan): of the eight cases in this class, only
    /// <c>CommandTimeout_EndsTheLoopCleanly_AndWritesWhatItHad</c> reaches <c>RunLoop</c> — the sole
    /// path that calls <c>FrameCapture.SaveAsPng</c> — and it never once references <c>frame.png</c>.
    /// The suite proved the file channel and could not prove a picture was taken. <c>--headless</c>
    /// produces a blank-but-valid PNG by contract (<see cref="FrameCapture"/>'s own doc comment), so
    /// "the file exists" is not the same claim as "the eye is open" — this asserts both, reading the
    /// actual bytes RunLoop wrote back off disk rather than trusting the in-memory capture.
    /// </summary>
    [TestCase]
    public async Task RunLoop_CapturesAFrame_ThatIsNotBlank()
    {
        var ui = MountMainUi();
        var outDir = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "agent-playtest-bridge-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var bridge = new AgentPlaytestBridge(ui);

            // command.json is never written — same timeout-driven shutdown as the sibling test above.
            // One turn is enough: RunLoop captures frame.png BEFORE it ever waits on a command, so
            // this proves the eye is open on the very first real turn, not just on a lucky one.
            await bridge.RunLoop(ui, outDir, maxTurns: 1, commandTimeoutMs: 300);

            var framePath = System.IO.Path.Combine(outDir, "frame.png");
            AssertThat(System.IO.File.Exists(framePath))
                .OverrideFailureMessage($"frame.png was never written to {outDir} — the channel A2 exists to prove is broken.")
                .IsTrue();

            var frame = Image.LoadFromFile(framePath);
            AssertThat(AgentPlaytestBridge.FrameLooksReal(frame))
                .OverrideFailureMessage(
                    "frame.png on disk is uniform — every sampled byte equals the first. This is exactly " +
                    "the blank-but-valid frame FrameCapture.cs documents for --headless: the file channel " +
                    "works and the eye is still closed.")
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

    /// <summary>
    /// Pins the whole point of the 2026-08-10 fix: the client's compiled-in fallback wait
    /// (<c>AgentPlaytest.DefaultCommandTimeoutMs</c>) must outlast the driver's own worst-case
    /// per-turn latency, or a legitimately slow model turn outruns the client and it self-exits
    /// mid-run (measured: Scout-5, 2026-08-09 overnight sweep, died on turn 1 of 80, verdict
    /// still read "ok").
    ///
    /// <para>Both halves are read from their REAL sources rather than retyped — a test that just
    /// asserts <c>950_000 &gt; 900_000</c> pins nothing, because both numbers could drift together
    /// with an editor's typo and this would still pass. The driver's numbers come from parsing
    /// <c>tools/agent-playtest.ps1</c>'s own <c>$ModelCallTimeoutSec</c> / <c>$ModelCallMaxAttempts</c>
    /// assignments; the client's number comes off the actual private const via reflection (it is
    /// `private` on purpose — nothing outside this dev tool needs to read it at runtime, so the
    /// test reaches for it the same way <c>ActionFeedbackTextMatchesTimingTests</c> already reaches
    /// a private method elsewhere in this suite, rather than widening the production API for a
    /// test's convenience).</para>
    /// </summary>
    [TestCase]
    public void ClientFallbackTimeout_ExceedsTheDriversRealWorstCasePerTurnBudget()
    {
        var driverPath = ProjectSettings.GlobalizePath("res://../tools/agent-playtest.ps1");
        AssertThat(System.IO.File.Exists(driverPath))
            .OverrideFailureMessage($"Driver script not found at '{driverPath}' — did tools/agent-playtest.ps1 move?")
            .IsTrue();
        var driverSource = System.IO.File.ReadAllText(driverPath);

        var timeoutMatch = Regex.Match(driverSource, @"\$ModelCallTimeoutSec\s*=\s*(\d+)");
        var attemptsMatch = Regex.Match(driverSource, @"\$ModelCallMaxAttempts\s*=\s*(\d+)");
        AssertThat(timeoutMatch.Success && attemptsMatch.Success)
            .OverrideFailureMessage(
                "Could not find '$ModelCallTimeoutSec = N' / '$ModelCallMaxAttempts = N' in " +
                "tools/agent-playtest.ps1 — this test reads the driver's REAL numbers off its source " +
                "and cannot pin the relationship if those names changed.")
            .IsTrue();

        var driverTimeoutSec = int.Parse(timeoutMatch.Groups[1].Value);
        var driverMaxAttempts = int.Parse(attemptsMatch.Groups[1].Value);
        var driverWorstCaseMs = (long)driverTimeoutSec * 1000L * driverMaxAttempts;

        var field = typeof(AgentPlaytest).GetField("DefaultCommandTimeoutMs", BindingFlags.NonPublic | BindingFlags.Static);
        AssertThat(field)
            .OverrideFailureMessage("AgentPlaytest.DefaultCommandTimeoutMs was not found by reflection — did it get renamed?")
            .IsNotNull();
        var clientFallbackMs = (int)field!.GetRawConstantValue()!;

        AssertThat((long)clientFallbackMs)
            .OverrideFailureMessage(
                $"AgentPlaytest.DefaultCommandTimeoutMs ({clientFallbackMs}ms) does not exceed the " +
                $"driver's own worst-case per-turn budget ({driverMaxAttempts} attempts x " +
                $"{driverTimeoutSec}s = {driverWorstCaseMs}ms, read from tools/agent-playtest.ps1). A " +
                "legitimately slow model turn would outrun the client's wait and it would self-exit " +
                "mid-run, exactly like Scout-5 in the 2026-08-09 sweep.")
            .IsGreater(driverWorstCaseMs);
    }

    /// <summary>
    /// The SAME "two halves of this channel are unrelated numbers in two languages" defect the test
    /// above pins for the timeout, in the place it was left open: the turn budget.
    ///
    /// <para>The client keeps its own <c>DefaultMaxTurns</c> (400) and stops the instant it hits one.
    /// The driver never set <c>AGENT_PLAYTEST_MAX_TURNS</c>, so that cap applied to every run
    /// regardless of <c>-Turns</c>. Measured 2026-08-12: three live pilot runs budgeted 400 / 800 /
    /// 900 turns ALL stopped at turn 400, on different navigation paths, and the driver reported
    /// "client wrote no state within 90s" every time — a clean, deliberate client shutdown wearing a
    /// timeout's error message. Every run ever budgeted past 400 turns was silently truncated and the
    /// truncation blamed on a hang.</para>
    ///
    /// <para>This asserts the driver EXPORTS the override at all, which is the thing that was
    /// missing. It deliberately does not pin the exact margin: the two sides count different things
    /// (client counts served round-trips, driver counts loop iterations), so the margin is a judgement
    /// call, while its absence is a defect.</para>
    /// </summary>
    [TestCase]
    public void Driver_ExportsItsTurnBudgetToTheClient_SoTheClientCapNeverTruncatesARun()
    {
        var driverPath = ProjectSettings.GlobalizePath("res://../tools/agent-playtest.ps1");
        AssertThat(System.IO.File.Exists(driverPath))
            .OverrideFailureMessage($"Driver script not found at '{driverPath}' — did tools/agent-playtest.ps1 move?")
            .IsTrue();
        var driverSource = System.IO.File.ReadAllText(driverPath);

        var exportMatch = Regex.Match(driverSource, @"\$env:AGENT_PLAYTEST_MAX_TURNS\s*=\s*\[string\]\(\$Turns");
        AssertThat(exportMatch.Success)
            .OverrideFailureMessage(
                "tools/agent-playtest.ps1 does not export AGENT_PLAYTEST_MAX_TURNS from its own $Turns. " +
                "Without it the client falls back to AgentPlaytest.DefaultMaxTurns (400) and silently " +
                "truncates any longer run, which the driver then reports as 'client wrote no state " +
                "within 90s' — a clean shutdown wearing a timeout's error message. Measured 2026-08-12: " +
                "runs budgeted 400/800/900 all stopped at exactly 400.")
            .IsTrue();

        // The env var the driver sets must be the one the client actually reads — a rename on either
        // side silently reopens the defect, since EnvInt just falls through to the default.
        var clientPath = ProjectSettings.GlobalizePath("res://scripts/tools/AgentPlaytest.cs");
        AssertThat(System.IO.File.Exists(clientPath))
            .OverrideFailureMessage($"Client not found at '{clientPath}' — did AgentPlaytest.cs move?")
            .IsTrue();
        var clientSource = System.IO.File.ReadAllText(clientPath);
        AssertThat(clientSource.Contains("EnvInt(\"AGENT_PLAYTEST_MAX_TURNS\""))
            .OverrideFailureMessage(
                "AgentPlaytest.cs no longer reads EnvInt(\"AGENT_PLAYTEST_MAX_TURNS\") — the driver is " +
                "exporting a variable nothing consumes, so the 400-turn cap is back in force silently.")
            .IsTrue();
    }

    /// <summary>
    /// Before this fix, <see cref="AgentPlaytestBridge.RunLoop"/> returned <c>void</c> and
    /// <c>AgentPlaytest._Ready</c> called <c>GetTree().Quit(0)</c> no matter which of the three
    /// endings happened — a driver going silent mid-run (this test's shape: command.json never
    /// arrives) exited identically to a real <c>stop</c> or a spent turn budget. That is the exact
    /// shape of the Scout-5 defect: an abandoned run reporting verdict <c>ok</c>, exit 0.
    /// </summary>
    [TestCase]
    public async Task CommandTimeout_ReportsTimedOut_WithANonZeroExitCode()
    {
        var ui = MountMainUi();
        var outDir = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "agent-playtest-bridge-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var bridge = new AgentPlaytestBridge(ui);

            // Same shape as CommandTimeout_EndsTheLoopCleanly_AndWritesWhatItHad above: command.json
            // is never written, so RunLoop can only end via the timeout branch.
            var outcome = await bridge.RunLoop(ui, outDir, maxTurns: 5, commandTimeoutMs: 300);

            AssertThat(outcome)
                .OverrideFailureMessage($"Expected AgentPlaytestOutcome.TimedOut when command.json never arrives, got {outcome}.")
                .IsEqual(AgentPlaytestOutcome.TimedOut);
            AssertThat(AgentPlaytest.ExitCodeFor(outcome))
                .OverrideFailureMessage(
                    "A timed-out run must exit non-zero — an abandoned run is not a completed one, " +
                    "and the driver's own completion floor (PR #436) should not be the only thing " +
                    "that can ever notice.")
                .IsNotEqual(0);

            var log = System.IO.File.ReadAllText(System.IO.Path.Combine(outDir, "turnlog.md"));
            AssertThat(log.Contains("TIMEOUT", StringComparison.Ordinal))
                .OverrideFailureMessage($"turnlog.md does not name TIMEOUT as the reason the run stopped:\n{log}")
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

    /// <summary>
    /// The exit-code mapping itself, isolated from RunLoop/Godot entirely: only <c>TimedOut</c> may
    /// be non-zero. Without this, a future edit could make <c>ExitCodeFor</c> non-zero for
    /// EVERY outcome (still "passing" the test above, which only checks the TimedOut branch) and
    /// silently break real, successful runs.
    /// </summary>
    [TestCase]
    public void ExitCodeFor_IsZeroForBothRealEndings_AndOnlyNonZeroForTimeout()
    {
        AssertThat(AgentPlaytest.ExitCodeFor(AgentPlaytestOutcome.Stopped)).IsEqual(0);
        AssertThat(AgentPlaytest.ExitCodeFor(AgentPlaytestOutcome.MaxTurnsReached)).IsEqual(0);
        AssertThat(AgentPlaytest.ExitCodeFor(AgentPlaytestOutcome.TimedOut)).IsNotEqual(0);
    }

    /// <summary>
    /// fix/pilot-finds-its-way: pilot.ps1's own header (2026-08-11) claimed "there is no keyboard
    /// shortcut to leave a walkable interior room" through this harness, based on grepping for the
    /// "cancel" action and finding only <c>WorldInput2D</c> — which raises <c>CancelRequested</c> to
    /// nobody (zero subscribers, confirmed by grep). What that grep missed: <c>MainUi._Input</c>'s
    /// own Escape ladder (Escape_WithNoDrawerOpen_ExitsTheRoom, InteriorEntryExitTests.cs) already
    /// calls <see cref="Town2d.Town2D.ExitInterior"/> on a raw <c>Key.Escape</c> — it just never
    /// matched the string "cancel" in a grep. The REAL gap is one level lower: <see
    /// cref="AgentPlaytestBridge.Apply"/>'s "key" action drove <c>Input.ActionPress</c>/
    /// <c>ActionRelease</c>, which — per Godot's own documented contract — updates ONLY the polled
    /// <c>Input.IsActionPressed</c> state and explicitly never calls any node's <c>_Input</c>. Any
    /// <c>_Input</c>-based handler (MainUi's ladder) or focus-gated <c>_GuiInput</c> handler (the
    /// forge minigame's bellows/strike) was therefore unreachable through this verb, while
    /// polling-based handlers (WorldInput2D's own dead CancelRequested) still fired. This test pins
    /// the fix — SUPERSEDED one call later the same day: the fix landed as <c>Viewport.PushInput</c>
    /// first (this test went green on that), but <c>PushInput</c> turned out to be only HALF a real
    /// key press (see <see cref="KeyInteract_AtForgeDoorAtSpawn_EntersTheForge"/>'s own doc for the
    /// second half); <c>AgentPlaytest.ApplyKey</c> now calls <see cref="Input.ParseInputEvent"/>
    /// instead, which is a strict superset (dispatches <c>_Input</c> AND updates polled action
    /// state), so this test still pins the same behavior against the corrected call.
    /// </summary>
    [TestCase]
    public async Task KeyCancel_InsideAWalkableRoom_ExitsIt()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.EnterInterior("forge");
            await SettleLayout(ui);
            AssertThat(ui.Town.InteriorActive).IsTrue();
            AssertThat(ui.Drawer.IsOpen)
                .OverrideFailureMessage("Setup check: a drawer must not be open, or Escape closes that first (the earlier ladder rung), not the room.")
                .IsFalse();

            var bridge = new AgentPlaytestBridge(ui);
            var outcome = await bridge.Apply(ui, new AgentCommand("key", "cancel", Why: "pilot: leaving the room"));

            var player = new HumanPlayer(ui);
            var exited = await player.WaitUntil(() => !ui.Town.InteriorActive);

            AssertThat(exited)
                .OverrideFailureMessage(
                    $"the harness's key:cancel action did not exit the walkable room (outcome: '{outcome}'). " +
                    "A live pilot standing in any interior with no drawer open must be able to leave through " +
                    "this exact verb, or it has no escape at all once its nearest station is unreachable.")
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// fix/pilot-finds-its-way, live-run finding (2026-08-11): a full 220-turn scripted run standing
    /// AT the forge doorstep on turn 1 (per <see cref="Surroundings_NameTheTownsBuildings_WithADirectionThatActuallyWorks"/>,
    /// the forge is 8px away and InRange at spawn) pressed <c>key:interact</c> three times in a row
    /// with a byte-identical digest each time (location stayed "town" all 220 turns of a 9-day run,
    /// zero panel entries, zero crafts) before the no-route detector blacklisted it for the day. This
    /// pins whether that key press reaches <see cref="Town2d.WorldInput2D"/>'s building-entry path at
    /// all through the bridge. Root cause, isolated with a throwaway diagnostic test (since removed):
    /// <c>ActiveTarget</c> was ALWAYS correctly "forge" (the physics-overlap geometry is fine, spawn
    /// distance 8px), so this is not a positioning bug. The actual gap was one level lower —
    /// <c>ApplyKey</c> dispatched via <c>Viewport.PushInput</c> (the fix that made <c>cancel</c>
    /// work, see <see cref="KeyCancel_InsideAWalkableRoom_ExitsIt"/>'s own doc), which reaches
    /// <c>_Input</c>/<c>_GuiInput</c> handlers but does NOT update the global <see cref="Input"/>
    /// singleton's polled action state at all — confirmed directly: <c>Input.IsActionPressed
    /// ("interact")</c> read false on every one of 6 physics frames after a <c>PushInput</c> press.
    /// <c>WorldInput2D._PhysicsProcess</c> gates building entry on exactly that polled state
    /// (<c>Input.IsActionJustPressed("interact")</c>), so a real "cancel" _Input event and a
    /// permanently-invisible-to-polling "interact" press are the SAME call shape with two different,
    /// independent Godot subsystems underneath. <c>ApplyKey</c> now calls <see
    /// cref="Input.ParseInputEvent"/> — Godot's own documented complete simulation (the literal "use
    /// Input.ParseInputEvent instead" from the API doc already quoted in this file's history) — which
    /// is a strict superset of <c>PushInput</c>: same diagnostic re-run showed <c>IsActionJustPressed</c>
    /// true on the very next physics frame and the forge entered. This test isolates that path: no
    /// walking, no room, standing at the exact spawn position the real run started from.
    /// </summary>
    [TestCase]
    public async Task KeyInteract_AtForgeDoorAtSpawn_EntersTheForge()
    {
        var ui = MountMainUi();
        try
        {
            await SettleLayout(ui);
            var bridge = new AgentPlaytestBridge(ui);

            var before = bridge.BuildDigest(ui, 1, "(start)");
            var forge = before.Nearby.FirstOrDefault(n => n.Key.Equals("forge", StringComparison.OrdinalIgnoreCase));
            AssertThat(forge)
                .OverrideFailureMessage($"Setup check: forge not in nearby list at spawn. Nearby: {string.Join(", ", before.Nearby.Select(n => n.Key))}.")
                .IsNotNull();
            AssertThat(forge!.InRange)
                .OverrideFailureMessage($"Setup check: forge reported {forge.Distance}px away, not InRange, at spawn.")
                .IsTrue();
            AssertThat(before.Location)
                .OverrideFailureMessage($"Setup check: expected location 'town' at spawn, got '{before.Location}'.")
                .IsEqual("town");

            var activeTargetBefore = ui.Town.WorldInputNode.ActiveTarget?.Key ?? "(null)";
            var outcome = await bridge.Apply(ui, new AgentCommand("key", "interact", Why: "pilot: entering Forge"));
            var activeTargetAfter = ui.Town.WorldInputNode.ActiveTarget?.Key ?? "(null)";

            var player = new HumanPlayer(ui);
            var entered = await player.WaitUntil(() => ui.Drawer.IsOpen || ui.Town.InteriorActive);
            var after = bridge.BuildDigest(ui, 2, outcome);

            AssertThat(entered)
                .OverrideFailureMessage(
                    $"key:interact at the forge doorstep (spawn, {forge.Distance}px, InRange=true) did not " +
                    $"enter anything: outcome='{outcome}', location stayed '{after.Location}'. " +
                    $"ActiveTarget before press: {activeTargetBefore}, after press+settle: {activeTargetAfter}. " +
                    $"WorldInputNode.Enabled={ui.Town.WorldInputNode.Enabled}. This is the " +
                    "live-run wall: a real 220-turn pilot run standing on this exact spot pressed interact " +
                    "three times with zero effect before being blacklisted for the day, and NEVER entered a " +
                    "single building in 9 in-game days.")
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// fix/u-t1-anvil-can-be-finished: this test used to ASSERT the softlock as correct. The
    /// original version measured heat climbing steadily across 8 real bridge-driven "forge_strike"
    /// turns while <c>IsPumping</c> stayed true throughout, and read that as good news --
    /// "DISPROVES the tap-never-latches hypothesis, the C3 escape hatch works." It does work. That
    /// was never the bug. The bug is what the old assertions locked in as a requirement:
    /// <c>ForgeStrike()</c> early-returning while <c>IsPumping</c>, so every one of pilot.ps1's
    /// "forge_strike" turns sent during a latched pump was a genuine no-op -- the hammer, the one
    /// input that can finish the craft, did nothing for as long as the bellows stayed toggled on. A
    /// live 420-turn pilot probe (tools/agent-playtest/pilot.ps1) opened this exact minigame and
    /// landed zero strikes because of it. Worse, once heat hit its 1000 clamp the bellows kept
    /// draining shape for free while doing no more heat work -- Brian's own report, "Strike 24/21 --
    /// Heat 1000 -- pumping -- the billet is yielding, keep going".
    ///
    /// <para><b>STRIKE IMPLIES RELEASE (owner ruling).</b> A strike arriving mid-pump now stops the
    /// bellows and lands -- it IS the release, not a second input the pump blocks. So this same
    /// 8-turn "forge_strike while pumping" sequence must now end the pump on the very first turn and
    /// bank a real strike on every turn after: the inverse of what this test used to assert.</para>
    /// </summary>
    [TestCase]
    public async Task KeyForgeStrike_WhilePumping_StopsThePumpAndLandsRealStrikes()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.WorldViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
            await PumpWorldFrames(ui, 2);

            ui.Adapter.Queue(new BuyMaterialAction(ScriptedSession.CraftMaterial, ScriptedSession.CopperNeeded));
            ui.Adapter.AdvancePhase();
            ui.OpenPanel("Forge");
            PressEnabled(ui.Forge, $"WorkForge_{ScriptedSession.CraftRecipeId}");

            var overlay = Find<ForgeMinigame>(ui.Forge, "ForgeMinigame");
            AssertThat(overlay.Visible).IsTrue();
            await PumpWorldFrames(ui, 1); // the open path's own ClaimKeyboard is deferred one frame

            AssertThat(overlay.IsProcessing())
                .OverrideFailureMessage(
                    "Setup check: this overlay must be ticking its OWN real _Process clock, matching a " +
                    "live client -- if IsProcessing() is false here, this test would accidentally be " +
                    "measuring ForgePlayer's scripted-clock path instead of the real one a live pilot hits.")
                .IsTrue();

            var bridge = new AgentPlaytestBridge(ui);

            var tapStartMs = Time.GetTicksMsec();
            var tapOutcome = await bridge.Apply(ui, new AgentCommand("key", "bellows", Why: "test: one tap, exactly what pilot.ps1 sends to start pumping"));
            var tapEndMs = Time.GetTicksMsec();
            var heatAfterTap = overlay.HeatYPermille;
            var pumpingAfterTap = overlay.IsPumping;

            AssertThat(pumpingAfterTap)
                .OverrideFailureMessage(
                    $"One tap of 'bellows' through the real bridge (outcome '{tapOutcome}', round trip " +
                    $"{tapEndMs - tapStartMs}ms) left IsPumping={pumpingAfterTap}. ForgeMinigame's own C3 " +
                    "tap-to-toggle latch is supposed to keep the bellows running past release for a press " +
                    "well under BellowsTapMaxHoldSeconds -- if it does not latch, a live pilot run can " +
                    "never accumulate heat off a single keypress.")
                .IsTrue();

            // Exactly pilot.ps1's OLD policy while pumping: send "forge_strike" every turn rather
            // than re-pressing bellows. Under the fix, the FIRST such turn must stop the pump
            // (strike implies release) and land a strike; every turn after is an ordinary strike
            // against a bellows that is no longer running. Each loop iteration is the SAME
            // ApplyKey press-3-frames-release-3-frames round trip a live run pays.
            var heatSamples = new List<int> { heatAfterTap };
            var strikeSamples = new List<int> { overlay.StrikesLanded };
            var msPerTurn = new List<long>();
            for (var turn = 0; turn < 8; turn++)
            {
                var beforeMs = Time.GetTicksMsec();
                await bridge.Apply(ui, new AgentCommand("key", "forge_strike", Why: "test: simulated turn, now a real strike even while pumping"));
                var afterMs = Time.GetTicksMsec();
                msPerTurn.Add((long)(afterMs - beforeMs));
                heatSamples.Add(overlay.HeatYPermille);
                strikeSamples.Add(overlay.StrikesLanded);
            }

            var perTurnDeltas = new List<int>();
            for (var i = 1; i < heatSamples.Count; i++)
            {
                perTurnDeltas.Add(heatSamples[i] - heatSamples[i - 1]);
            }

            // What this now measures, post-fix: the pump stops on turn 1 (StrikesLanded goes
            // 0 -> 1, IsPumping true -> false in the same call) and every subsequent turn banks
            // another strike against a bellows that is no longer running -- the exact opposite of
            // the pre-fix measurement above this comment's own history, where heat climbed for 8
            // straight turns and StrikesLanded never moved at all. That prior "healthy" reading was
            // the trap working exactly as coded: the latch held, and holding was the bug.
            GD.Print(
                "[forge-strike-implies-release] tapRoundTripMs=" + (tapEndMs - tapStartMs) +
                " heatAfterTap=" + heatAfterTap + " pumpingAfterTap=" + pumpingAfterTap +
                " pumpingAfterFirstStrikeTurn=" + (strikeSamples.Count > 1 && overlay.IsPumping == false) +
                " heatSamples=[" + string.Join(",", heatSamples) + "]" +
                " perTurnDeltas=[" + string.Join(",", perTurnDeltas) + "]" +
                " strikeSamples=[" + string.Join(",", strikeSamples) + "]" +
                " msPerTurn=[" + string.Join(",", msPerTurn) + "]");

            AssertThat(overlay.IsPumping)
                .OverrideFailureMessage(
                    "A forge_strike sent while pumping must stop the pump -- STRIKE IMPLIES RELEASE -- " +
                    $"but IsPumping is still true after 8 forge_strike turns. Strike samples: " +
                    $"[{string.Join(",", strikeSamples)}].")
                .IsFalse();

            AssertThat(strikeSamples[strikeSamples.Count - 1])
                .OverrideFailureMessage(
                    $"8 real forge_strike turns sent while pumping landed {strikeSamples[^1]} strikes " +
                    $"total (started at {strikeSamples[0]}). This is exactly the pilot's 420-turn-zero-" +
                    "strikes trap: forge_strike must never be a no-op while pumping. Heat samples: " +
                    $"[{string.Join(",", heatSamples)}].")
                .IsGreater(strikeSamples[0]);
        }
        finally
        {
            Unmount(ui);
        }
    }
}
#endif
