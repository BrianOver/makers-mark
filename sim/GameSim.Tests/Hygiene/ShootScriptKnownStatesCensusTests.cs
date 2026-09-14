using System.Text.RegularExpressions;

namespace GameSim.Tests.Hygiene;

/// <summary>
/// P2-SCREEN-28 ("the capture harness's own usage header stops naming states it does not have"):
/// <c>tools/shoot.ps1</c>'s usage header hand-listed <c>Forge | Shop | Tavern | Gate | Counter |
/// Watch</c> as valid <c>-State</c> values -- but <c>godot/tools/shot_harness.gd</c>'s
/// <c>KNOWN_STATES</c> const has never recognised <c>Forge</c>, <c>Shop</c>,
/// <c>Tavern</c>, or <c>Gate</c> (only the more specific <c>ForgeAnvil</c>, <c>ShopPanel</c>,
/// <c>TavernPanel</c>, <c>GateNight</c>/<c>GateHeldStreak</c>, etc.). A session that read the
/// header and typed <c>-State Forge</c> got a hard failure and had to go read the harness anyway --
/// the header cost time instead of saving it.
///
/// <para>Correcting the list by hand fixes today and rots the same way tomorrow: the header is a
/// copy, and nothing stopped the harness's real list from moving on without it. This test is what
/// stops that -- it parses both the header's <c>BEGIN KNOWN_STATES</c>/<c>END KNOWN_STATES</c>
/// block and the harness's own <c>KNOWN_STATES</c> const and fails if the two sets disagree in
/// EITHER direction. The direction that actually happens is addition (this repo has added a
/// dozen-plus states to the harness since U1, never once updating this header) -- so the case that
/// matters most is: add a state to the harness, forget the header, watch this go red.</para>
/// </summary>
public class ShootScriptKnownStatesCensusTests
{
    [Fact]
    public void ShootScriptHeaderListsExactlyTheStatesTheHarnessKnows()
    {
        var repoRoot = RepoRoot();
        var harnessPath = Path.Combine(repoRoot, "godot", "tools", "shot_harness.gd");
        var scriptPath = Path.Combine(repoRoot, "tools", "shoot.ps1");
        Assert.True(File.Exists(harnessPath), $"Expected to find {harnessPath}.");
        Assert.True(File.Exists(scriptPath), $"Expected to find {scriptPath}.");

        var harnessStates = ParseHarnessKnownStates(File.ReadAllText(harnessPath));
        var headerStates = ParseHeaderKnownStates(File.ReadAllText(scriptPath));

        var missingFromHeader = harnessStates.Except(headerStates).OrderBy(s => s, StringComparer.Ordinal).ToList();
        var missingFromHarness = headerStates.Except(harnessStates).OrderBy(s => s, StringComparer.Ordinal).ToList();

        Assert.True(missingFromHeader.Count == 0,
            "shot_harness.gd's KNOWN_STATES names a state tools/shoot.ps1's header does not -- a "
            + "state was added to (or renamed in) the harness without updating the header's "
            + "BEGIN/END KNOWN_STATES block:\n  " + string.Join(", ", missingFromHeader));

        Assert.True(missingFromHarness.Count == 0,
            "tools/shoot.ps1's header names a state shot_harness.gd's KNOWN_STATES does not -- the "
            + "header is documenting a state the harness cannot actually render (the exact P2-SCREEN-28 "
            + "failure this census exists to close):\n  " + string.Join(", ", missingFromHarness));
    }

    private static List<string> ParseHarnessKnownStates(string code)
    {
        var match = Regex.Match(code, @"const\s+KNOWN_STATES\s*:=\s*\[(?<body>.*?)\]", RegexOptions.Singleline);
        Assert.True(match.Success,
            "Could not find 'const KNOWN_STATES := [ ... ]' in shot_harness.gd -- did it get renamed?");

        return Regex.Matches(match.Groups["body"].Value, "\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value)
            .ToList();
    }

    private static List<string> ParseHeaderKnownStates(string script)
    {
        var match = Regex.Match(script, @"#\s*BEGIN KNOWN_STATES\s*\r?\n(?<body>.*?)#\s*END KNOWN_STATES",
            RegexOptions.Singleline);
        Assert.True(match.Success,
            "Could not find a '# BEGIN KNOWN_STATES' / '# END KNOWN_STATES' block in tools/shoot.ps1 -- "
            + "did the usage header get rewritten without it?");

        return match.Groups["body"].Value
            .Split([',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim().TrimStart('#').Trim())
            .Where(token => token.Length > 0)
            .ToList();
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Game.sln")))
        {
            dir = dir.Parent;
        }

        Assert.True(dir is not null, "Could not find Game.sln walking up from the test assembly.");
        return dir!.FullName;
    }
}
