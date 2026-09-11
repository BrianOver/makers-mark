using System.Text.RegularExpressions;

namespace GameSim.Tests.Hygiene;

/// <summary>
/// P2-HONEST-04 (§11.15, family B/D): a census that DISCOVERS every place the client renders a
/// <c>DayPhase</c> into text, rather than trusting anyone to remember that
/// <c>GodotClient.Ui.PhaseVocab</c> exists.
///
/// <para><b>The defect it closes.</b> <c>PhaseVocab</c>'s own class doc records why it was built:
/// three surfaces each decided independently what to call the same sim moment, and "a player
/// reading all three at once was reading a split-brained game." <c>SimPanel.Confirm</c> was the
/// fourth, and nothing caught it — its deferred branch interpolated
/// <c>{Adapter?.CurrentState.Phase}</c> straight into player copy, printing "Morning"/"Evening"
/// three inches under a HUD printing "Dawn"/"Night". A table is not a rule until something fails
/// when you route around it.</para>
///
/// <para><b>Discovery, not a list.</b> Scans every <c>.cs</c> file under <c>godot/scripts</c> for a
/// <c>{x.Phase</c> INTERPOLATION HOLE — anchored on the leading <c>{</c> exactly as
/// <c>CommissionSlotCopyCensusTests</c> is, so ordinary code reading <c>.Phase</c> (a
/// <c>switch</c>, an <c>if</c> guard, a <c>==</c> comparison — none of them player-facing) can
/// never match; only text actually rendered inside a <c>$"..."</c> can.</para>
///
/// <para><b>Developer channels are not player copy, and the census classifies rather than
/// exempts.</b> Seven of the eight live sites are logs, diagnostics or an exception message, and a
/// raw enum name is exactly right in all seven — a log that prettified "Morning" into "Dawn" would
/// be a worse log. So the rule is contextual, not a pinned id list: a hole counts as player copy
/// unless its window is a <c>GD.Print</c>/<c>GD.Push*</c> call, a <c>throw</c>, or it lives under
/// <c>godot/scripts/tools/</c> (the agent-playtest and scenario-writer harnesses, which have no
/// player at all). That leaves zero pinned exceptions today, and a NEW developer log correctly
/// stays silent instead of costing someone a pin.</para>
///
/// <para>Structural heuristic, same honesty disclaimer as its siblings: the window is sized for
/// this codebase's own formatting, not proven correct for arbitrary C#.</para>
/// </summary>
public class PlayerPhaseVocabularyCensusTests
{
    /// <summary>(relative path, matched hole) → reason citing the ruling that grants it. Empty on
    /// purpose — the developer-channel rule below is a classification, not an exemption, so a
    /// legitimate log needs no entry here.</summary>
    private static readonly Dictionary<(string File, string Hole), string> Exceptions = new();

    private const int ExpectedExceptionCount = 0;

    private const int BackwardWindow = 260;
    private const int ForwardCap = 400;

    private static readonly Regex PhaseHole = new(@"\{[A-Za-z_][\w.?!]*\.Phase\b");

    /// <summary>A developer channel: the enum's own name is the correct rendering there.</summary>
    private static readonly Regex DeveloperChannel = new(@"GD\.(Print|PushError|PushWarning)\(|throw new ");

    private static readonly Regex StatementTerminator = new(@"[;,]\r?\n");

    [Fact]
    public void ExceptionCount_IsPinned_SoEveryNewGrantIsAVisibleDiff()
        => Assert.True(Exceptions.Count == ExpectedExceptionCount,
            $"Pinned at {ExpectedExceptionCount}; the table now holds {Exceptions.Count}.");

    [Fact]
    public void EveryPinnedException_CitesTheRulingThatGrantedIt()
    {
        var citation = new Regex(@"§11\.7|\bP\d+\b|P2-[A-Z]+-\d+");
        var uncited = Exceptions
            .Where(e => !citation.IsMatch(e.Value))
            .Select(e => $"{e.Key.File}  {e.Key.Hole}")
            .ToList();

        Assert.True(uncited.Count == 0,
            "An exception with no ruling behind it is drift wearing a reason:\n  " + string.Join("\n  ", uncited));
    }

    /// <summary>The regression proof: the actual pre-fix <c>SimPanel.Confirm</c> line, standalone,
    /// so this detector's correctness never depends on the real defect still being in the tree.</summary>
    [Fact]
    public void RegressionProof_WouldHaveCaughtTheActualSplitBrainLine()
    {
        const string historicalCode =
            "            : $\"{whatHappened}. Queued — resolves when {Adapter?.CurrentState.Phase} ticks. "
            + "Press Advance or wait.\";\n";

        var window = Assert.Single(Windows(historicalCode).ToList());
        Assert.DoesNotMatch(DeveloperChannel, window);
    }

    /// <summary>The negative control: a real developer log from this repo must NOT be flagged. A
    /// census that reddened on logs would be paid off with eight pins and stop meaning anything.</summary>
    [Fact]
    public void NegativeControl_ADeveloperLogIsNotPlayerCopy()
    {
        const string log =
            "        GD.Print($\"[NewGameSelect] resumed campaign: day {state.Day}, phase {state.Phase}\");\n";

        var window = Assert.Single(Windows(log).ToList());
        Assert.Matches(DeveloperChannel, window);
    }

    /// <summary>The second negative control: an exception message. Same reasoning — a thrown
    /// message is read by whoever is debugging, never by a player.</summary>
    [Fact]
    public void NegativeControl_AThrownMessageIsNotPlayerCopy()
    {
        const string thrown =
            "            _ => throw new InvalidOperationException(\n"
            + "                $\"WaitText: {def.Step} is unavailable (Day {state.Day}, {state.Phase}).\"),\n";

        var window = Assert.Single(Windows(thrown).ToList());
        Assert.Matches(DeveloperChannel, window);
    }

    [Fact]
    public void EveryPlayerFacingPhaseRender_GoesThroughPhaseVocab_UnlessPinnedWithAReason()
    {
        var violations = new List<string>();
        foreach (var (relative, absolute) in ClientSourceFiles())
        {
            var code = File.ReadAllText(absolute);
            foreach (var (offset, hole, window) in WindowsWithOffset(code))
            {
                if (DeveloperChannel.IsMatch(window) || Exceptions.ContainsKey((relative, hole)))
                {
                    continue;
                }

                var lineNumber = code[..offset].Count(c => c == '\n') + 1;
                violations.Add($"{relative}:{lineNumber}  {window.Trim()}");
            }
        }

        Assert.True(violations.Count == 0,
            "Player-facing text renders a DayPhase without PhaseVocab — the split-brained-game defect "
            + "PhaseVocab's own class doc was written about. Route it through PhaseVocab.Display, or pin "
            + "a cited exception if this really is not player copy:\n  " + string.Join("\n  ", violations));
    }

    private static IEnumerable<string> Windows(string code) => WindowsWithOffset(code).Select(t => t.Window);

    private static IEnumerable<(int Offset, string Hole, string Window)> WindowsWithOffset(string code)
    {
        foreach (Match m in PhaseHole.Matches(code))
        {
            var start = Math.Max(0, m.Index - BackwardWindow);
            var rest = code[m.Index..];
            var terminator = StatementTerminator.Match(rest);
            var forwardEnd = terminator.Success
                ? m.Index + Math.Min(terminator.Index + 1, ForwardCap)
                : Math.Min(code.Length, m.Index + ForwardCap);

            yield return (m.Index, m.Value, code[start..forwardEnd]);
        }
    }

    /// <summary>Client source only. <c>godot/scripts/tools/</c> is excluded wholesale: the
    /// agent-playtest harness and the scenario writer print diagnostics for an agent or an
    /// engineer, never for a player, and their whole trees are that.</summary>
    private static List<(string Relative, string Absolute)> ClientSourceFiles()
    {
        var repoRoot = RepoRoot();
        var full = Path.Combine(repoRoot, "godot", "scripts");
        var toolsDir = $"{Path.DirectorySeparatorChar}tools{Path.DirectorySeparatorChar}";

        var files = new List<(string, string)>();
        if (!Directory.Exists(full))
        {
            return files;
        }

        foreach (var path in Directory.EnumerateFiles(full, "*.cs", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                || path.Contains(toolsDir))
            {
                continue;
            }

            files.Add((Path.GetRelativePath(repoRoot, path).Replace('\\', '/'), path));
        }

        return files;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Game.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException("Could not locate the repo root (no Game.sln above the test binary).");
    }
}
