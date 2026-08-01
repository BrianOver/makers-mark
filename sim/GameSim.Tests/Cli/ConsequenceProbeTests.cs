using System.Collections.Immutable;
using GameSim.Advisor;
using GameSim.Cli;
using GameSim.Contracts;
using GameSim.Kernel;

namespace GameSim.Tests.Cli;

/// <summary>
/// Guards for <see cref="ConsequenceProbe"/> — the tool that asks whether the game's choices MATTER by
/// forking each decision point and applying every legal option to the fork.
/// <para>
/// The whole value of that probe rests on one thing: its state fingerprint must see every part of the
/// world an action can touch. When it doesn't, the failure is silent and it looks exactly like a game
/// bug. The first version hand-listed the fields it cared about, omitted <c>Drama</c>, and reported
/// <c>HonorMemorial</c> as doing nothing 330 times out of 330 while the handler was plainly setting
/// <c>Memorial.Honored = true</c>. Over the same run the tool also called <c>SetPrice</c> and
/// <c>SetProfessions</c> completely dead, because the legality enumerator names them with their CURRENT
/// values and they are no-ops by construction. Three false bugs from one report.
/// </para>
/// So the tests below are deliberately about the MEASUREMENT, not the game: a real mutation in a
/// far-flung corner of <see cref="GameState"/> must register, a documented no-op must be excluded rather
/// than reported, and the whole run must stay deterministic. A probe that lies is worse than no probe,
/// because its output reads like evidence.
/// </summary>
public sealed class ConsequenceProbeTests
{
    private static string Report(string dir) =>
        File.ReadAllText(Path.Combine(dir, "consequence-report.md"));

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mm-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// The regression that motivated every other test here. <c>HonorMemorial</c> mutates
    /// <c>Drama.Memorials</c> and NOTHING else — no gold, no items, no heroes — so it is the sharpest
    /// probe of whether the fingerprint reaches past the obvious fields. Asserted directly against the
    /// fingerprint rather than through a full run, so a failure names the cause instead of a percentage.
    /// </summary>
    [Fact]
    public void TheFingerprint_SeesAMutationThatOnlyTouchesDrama()
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(2026UL);

        // Walk to an Evening that has an unhonored memorial to act on. Memorials are raised by the
        // reveal system during the Evening system pass, so the earliest actionable one is a later day.
        HonorMemorialAction? honor = null;
        while (state.Day <= 60 && honor is null)
        {
            if (state.Phase == DayPhase.Evening)
            {
                honor = ActionLegality.LegalActions(state, state.Phase)
                    .OfType<HonorMemorialAction>()
                    .FirstOrDefault();

                if (honor is not null)
                {
                    break;
                }
            }

            state = kernel.Tick(state, GameSim.Harness.BaselinePlayer.ActionsFor(state)).NewState;
        }

        Assert.NotNull(honor);

        var doNothing = ConsequenceProbe.FingerprintForTests(
            kernel.Tick(state, ImmutableList<PlayerAction>.Empty).NewState);
        var honored = ConsequenceProbe.FingerprintForTests(
            kernel.Tick(state, ImmutableList.Create<PlayerAction>(honor)).NewState);

        Assert.NotEqual(doNothing, honored);
    }

    /// <summary>
    /// The RNG must NOT count as an effect. Two states that differ only in how much entropy they burned
    /// are the same world, and if the fingerprint disagreed then every action would look consequential
    /// and the probe's one sound inference ("identical ⇒ inert") would be worthless.
    /// </summary>
    [Fact]
    public void TheFingerprint_IgnoresRngPositionAndTheActionLog()
    {
        var state = GameComposition.NewCampaign(2026UL);
        var shifted = state with
        {
            Rng = RngState.FromSeed(999UL),
            ActionLog = state.ActionLog,
            EventLog = state.EventLog,
            ActionSlotsRemaining = state.ActionSlotsRemaining - 1,
        };

        Assert.Equal(
            ConsequenceProbe.FingerprintForTests(state),
            ConsequenceProbe.FingerprintForTests(shifted));
    }

    /// <summary>
    /// <c>ActionLegality</c> names <c>SetProfessions</c> with the CURRENT selection and <c>SetPrice</c>
    /// with the price already on the tag, and says so in its own comments. Those candidates must be
    /// excluded from the inert findings — reported, they read as two dead verbs, which is a lie about
    /// the game rather than a fact about it.
    /// </summary>
    [Fact]
    public void ANoOpByConstructionCandidate_IsExcludedRatherThanReportedAsDead()
    {
        var state = GameComposition.NewCampaign(2026UL);

        var reaffirm = new SetProfessionsAction(state.Player.SelectedProfessions);
        Assert.True(ConsequenceProbe.IsNoOpByConstructionForTests(state, reaffirm));

        // A genuinely different selection is NOT excused — the probe must still measure that.
        var changed = new SetProfessionsAction(
            state.Player.SelectedProfessions.Add("alchemy"));
        Assert.False(ConsequenceProbe.IsNoOpByConstructionForTests(state, changed));
    }

    /// <summary>
    /// A report is evidence, so the same seeds must produce the same report — otherwise a finding cannot
    /// be handed to anyone or compared against a later run.
    /// </summary>
    [Fact]
    public void TheSameSeeds_ProduceAnIdenticalReport()
    {
        var first = TempDir();
        var second = TempDir();
        try
        {
            using var sink = new StringWriter();
            Assert.Equal(0, ConsequenceProbe.Run(1, 2026UL, 3, first, sink, sink));
            Assert.Equal(0, ConsequenceProbe.Run(1, 2026UL, 3, second, sink, sink));
            Assert.Equal(Report(first), Report(second));
        }
        finally
        {
            Directory.Delete(first, recursive: true);
            Directory.Delete(second, recursive: true);
        }
    }

    /// <summary>
    /// Non-vacuity. A probe that silently measured nothing would pass every test above, so pin that a
    /// short run actually forked decision points and wrote both artifacts.
    /// </summary>
    [Fact]
    public void AShortRun_ActuallyProbesOptionsAndWritesBothArtifacts()
    {
        var dir = TempDir();
        try
        {
            using var sink = new StringWriter();
            Assert.Equal(0, ConsequenceProbe.Run(1, 2026UL, 3, dir, sink, sink));

            var report = Report(dir);
            Assert.Contains("Consequence Report", report);
            Assert.Contains("Does the choice matter?", report);
            Assert.Contains("Treadmill", report);

            // The per-tick log must exist and have a row per decision point.
            var jsonl = Directory.GetFiles(dir, "probe-seed*.jsonl").Single();
            var rows = File.ReadAllLines(jsonl).Where(l => l.Length > 0).ToList();
            Assert.True(rows.Count >= 5, $"expected several decision points over 3 days, got {rows.Count}");
            Assert.All(rows, r => Assert.Contains("\"outcomeClasses\":", r));

            // And it must have probed real options, not zero.
            Assert.Contains("\"options\":", rows[0]);
            Assert.DoesNotContain("\"options\":0,", string.Join("\n", rows));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
