using System.Reflection;
using System.Text.RegularExpressions;

namespace GameSim.Tests.Hygiene;

/// <summary>
/// CI audit (2026-08-29, run 33582674334): both of this repo's "did the engine suite actually
/// run" guards had rotted into instances of the exact bug they exist to catch. `.github/workflows
/// /ci.yml`'s <c>ENGINE_MIN_PASSED</c> was 900 against a live suite that had grown to 1696
/// executed cases; `tools/engine-test.ps1`'s own <c>-MinTests</c> default was 780, documented as
/// "suite was 803 on 2026-08-03". A guard whose floor sits far enough below the real suite size
/// lets a run that silently drops a large chunk of the suite — a dead Godot runtime, a filter
/// nobody meant to leave on — still print green.
///
/// A fixed number rots the moment the suite outgrows it and nobody notices, because a floor that
/// is too low still passes. This test makes that rot a live, self-updating check instead of a
/// number someone has to remember to revisit: it reads <c>ENGINE_MIN_PASSED</c> straight out of
/// <c>.github/workflows/ci.yml</c> (READ-ONLY — <c>.github/</c> is deny-listed for this session)
/// and compares it against a live count of <c>[TestCase</c> attribute occurrences under
/// <c>godot/tests/**/*.cs</c>. It is deliberately not an exact-match assertion: gdUnit4Net's
/// <c>[TestCase]</c> marks a runnable case (sometimes one per test method, sometimes several
/// stacked on one method for parameterized data), so the attribute count tracks the executed
/// suite size closely but not exactly (audit measurement: 1484 attributes against 1696 tests
/// actually executed in CI, ratio ~0.875). CI's floor has never been an exact headcount either —
/// it exists to prove "this was not a truncated run", not to pin the suite to a specific size — so
/// the band is asymmetric: <c>[0.95x, 1.2x]</c> of the live attribute count catches a floor that
/// fell behind suite growth (too low — the actual 2026-08-29 bug) AND a floor parked absurdly high
/// above anything the suite could produce (which would fail every ordinary green run).
///
/// <para><b>Known-red against the live floor right now.</b> <c>ENGINE_MIN_PASSED</c> is still 900
/// (last set when the suite was ~803) against a suite that has since grown well past it — this
/// test's own assertion would fail with the true count named in the message. Left red, that
/// failure would sit inside `dotnet test ... --filter Category!=Balance` (this file carries no
/// <c>Balance</c> trait), which is the fast lane every PR — including this one — must pass before
/// merge per CLAUDE.md rule 1 ("Merged is done. Nothing else is."), and <c>.github/</c> is
/// deny-listed for every session but the orchestrator, so this session cannot land the fix that
/// would turn it green. Rather than open a PR that can never auto-merge, this test is filed as an
/// explicit, reasoned SKIP naming the exact pending change, per this file's own governing
/// instructions ("if a red test on main is unacceptable, mark it explicitly skipped ... prefer
/// red, and say which you chose and why" — red was ruled out here specifically because it would
/// permanently block this PR's merge on an edit this session is not allowed to make).
/// <b>Owner action needed:</b> in <c>.github/workflows/ci.yml</c>, change
/// <c>ENGINE_MIN_PASSED: 900</c> to <c>ENGINE_MIN_PASSED: 1650</c> (matching the raised
/// <c>tools/engine-test.ps1</c> default — 97% of the measured 1696 executed) and
/// <c>GUARD_MIN_PASSED: 20</c> to <c>GUARD_MIN_PASSED: 35</c> (the guard subset actually runs 41).
/// Once landed, remove the <c>Skip</c> argument below — the assertion itself needs no other
/// change, and should then read green.
/// </para>
/// </summary>
public class EngineTestFloorCensusTests
{
    private static readonly Regex EngineMinPassed = new(@"ENGINE_MIN_PASSED:\s*(\d+)");
    private static readonly Regex TestCaseAttribute = new(@"\[TestCase\b");

    [Fact(Skip = "ENGINE_MIN_PASSED (900, .github/workflows/ci.yml) has rotted below the live " +
        "[TestCase attribute count and .github/ is deny-listed for this session to fix — see " +
        "this class's doc comment for the exact owner change (900 -> 1650, GUARD_MIN_PASSED " +
        "20 -> 35) and why SKIP was chosen over leaving this red.")]
    public void EngineMinPassedFloor_TracksTheLiveTestCaseAttributeCount()
    {
        var ciYml = File.ReadAllText(Path.Combine(RepoRoot(), ".github", "workflows", "ci.yml"));
        var match = EngineMinPassed.Match(ciYml);
        Assert.True(match.Success, "ENGINE_MIN_PASSED: not found in .github/workflows/ci.yml — did the key get renamed?");
        var floor = int.Parse(match.Groups[1].Value);

        var attributeCount = CountTestCaseAttributes();
        Assert.True(attributeCount > 0, "no [TestCase attributes found under godot/tests — is the path right?");

        var lowerBound = attributeCount * 0.95;
        var upperBound = attributeCount * 1.2;

        Assert.True(floor >= lowerBound,
            $"ENGINE_MIN_PASSED ({floor}) has rotted below 95% of the live [TestCase attribute " +
            $"count ({attributeCount}, so floor must be >= {lowerBound:F0}) — a run that silently " +
            "drops a big chunk of the suite would still clear this floor. Raise ENGINE_MIN_PASSED " +
            "in .github/workflows/ci.yml (owner-authored, deny-listed for this session).");

        Assert.True(floor <= upperBound,
            $"ENGINE_MIN_PASSED ({floor}) is parked above 120% of the live [TestCase attribute " +
            $"count ({attributeCount}, so floor must be <= {upperBound:F0}) — no ordinary run " +
            "could ever clear it. Lower ENGINE_MIN_PASSED in .github/workflows/ci.yml " +
            "(owner-authored, deny-listed for this session).");
    }

    private static int CountTestCaseAttributes()
    {
        var testsRoot = Path.Combine(RepoRoot(), "godot", "tests");
        Assert.True(Directory.Exists(testsRoot), $"Expected the engine test suite at {testsRoot}.");

        var count = 0;
        foreach (var file in Directory.EnumerateFiles(testsRoot, "*.cs", SearchOption.AllDirectories))
        {
            count += TestCaseAttribute.Matches(File.ReadAllText(file)).Count;
        }

        return count;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Game.sln")))
            dir = dir.Parent;

        Assert.True(dir is not null, "Could not find Game.sln walking up from the test assembly.");
        return dir!.FullName;
    }
}
