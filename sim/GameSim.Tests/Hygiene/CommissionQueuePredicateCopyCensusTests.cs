namespace GameSim.Tests.Hygiene;

/// <summary>
/// P2-PEOPLE-17 ("stocking a piece names the morning queue that will reach it first"): the client
/// must read the commission forge-request match predicate from
/// <see cref="GameSim.Heroes.CommissionHandlers.Satisfies"/> — never re-derive it. Two of its three
/// checks (slot, quality) are common enough patterns elsewhere that scanning for them would drown in
/// false positives, but its other two checks are NOT: <c>AllowsShield</c> and <c>MaxItemWeight</c>
/// are the "can this hero physically use it" class-fit facts unique to
/// <see cref="GameSim.Classes.ClassDefinition"/>, read today only from <c>sim/GameSim/Classes</c> and
/// <c>sim/GameSim/Heroes</c>. Either token appearing in <c>godot/scripts</c> at all means someone
/// copied the class-fit half of the predicate into the renderer instead of calling
/// <see cref="GameSim.Heroes.CommissionHandlers.Satisfies"/> — the exact failure family this repo has
/// already paid for (<c>CampHandlers.SupplyFee</c>'s own godot-side mirror is the standing example of
/// what NOT to repeat).
///
/// <para>Same discovery idiom as <c>CommissionSlotCopyCensusTests</c>: scan the actual tree, not a
/// hand-typed list of sites, so a future copy anywhere under <c>godot/scripts</c> is caught by
/// construction rather than by remembering to update this test.</para>
/// </summary>
public class CommissionQueuePredicateCopyCensusTests
{
    private static readonly string[] PredicateOnlyTokens = ["AllowsShield", "MaxItemWeight"];

    [Fact]
    public void NoGodotScriptReDerivesTheClassFitHalfOfTheCommissionMatchPredicate()
    {
        var violations = new List<string>();
        foreach (var (relative, absolute) in SourceFiles())
        {
            var code = File.ReadAllText(absolute);
            foreach (var token in PredicateOnlyTokens)
            {
                if (code.Contains(token))
                {
                    violations.Add($"{relative} contains '{token}' — call CommissionHandlers.Satisfies instead of re-deriving it.");
                }
            }
        }

        Assert.True(violations.Count == 0,
            "A godot/scripts file re-derives the commission match predicate's class-fit checks "
            + "instead of calling CommissionHandlers.Satisfies:\n  " + string.Join("\n  ", violations));
    }

    /// <summary>The positive half: the shop panel must actually CALL the shared forecast, not just
    /// avoid re-deriving it (a panel that silently dropped the call would pass the test above too).</summary>
    [Fact]
    public void ShopPanel_ActuallyCallsTheSharedForecast_RatherThanGoingSilent()
    {
        var repoRoot = RepoRoot();
        var shopPanelPath = Path.Combine(repoRoot, "godot", "scripts", "panels", "ShopPanel.cs");
        Assert.True(File.Exists(shopPanelPath), $"Expected to find {shopPanelPath}.");

        var code = File.ReadAllText(shopPanelPath);
        Assert.Contains("CommissionHandlers.ForecastQueueFor(", code);
    }

    private static List<(string Relative, string Absolute)> SourceFiles()
    {
        var repoRoot = RepoRoot();
        var dir = Path.Combine(repoRoot, "godot", "scripts");
        var files = new List<(string, string)>();
        if (!Directory.Exists(dir))
        {
            return files;
        }

        foreach (var path in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            {
                continue;
            }

            files.Add((Path.GetRelativePath(repoRoot, path).Replace('\\', '/'), path));
        }

        return files.OrderBy(t => t.Item1, StringComparer.Ordinal).ToList();
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
