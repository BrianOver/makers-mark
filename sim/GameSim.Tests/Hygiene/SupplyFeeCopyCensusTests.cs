using System.Text.RegularExpressions;

namespace GameSim.Tests.Hygiene;

/// <summary>
/// P2-HONEST-22 ("the runner fee is mirrored in four files and guarded in none"): before this unit,
/// the winch-house slate (<c>godot/scripts/panels/CampPanel.cs</c>) held its own hand-typed copy of
/// <see cref="GameSim.Expedition.CampHandlers.SupplyFee"/> — the same two constants, the same
/// arithmetic, with a comment explaining why — because the source lived as <c>internal</c> and
/// <c>GameSim</c> exposed no <c>InternalsVisibleTo</c> to <c>GodotClient</c>. Change
/// <c>SupplyFeePerFloor</c> and the sim would charge the new fee while the slate quoted the old one
/// to the player's face, and every mirrored test would go on passing because each one pinned the
/// copy it made. The constants are <c>public</c> now and the panel calls the formula directly, but
/// nothing about that fixes the NEXT session that reaches for "just copy the two numbers" again —
/// this census is what makes that reintroduction a red build instead of a silent rot.
///
/// <para>Same discovery idiom as <see cref="CommissionQueuePredicateCopyCensusTests"/>: scan the
/// actual tree under <c>godot/scripts</c>, not a hand-typed list of sites, so a future copy anywhere
/// in the client is caught by construction.</para>
///
/// <para><b>Two independent detectors</b>, because either alone has a known blind spot a rename
/// or a fresh derivation would slip through:
/// <list type="number">
/// <item>The constant NAMES <c>SupplyFeeBase</c> and <c>SupplyFeePerFloor</c> have no legitimate
/// reason to appear in <c>godot/scripts</c> at all — the client only ever needs to CALL
/// <see cref="GameSim.Expedition.CampHandlers.SupplyFee"/>, never to know the two numbers it is
/// built from. This is the same shape <see cref="CommissionQueuePredicateCopyCensusTests"/> bans
/// <c>AllowsShield</c>/<c>MaxItemWeight</c> on, and it is exactly the shape the original defect
/// took (<c>CampPanel.cs</c> re-declared both names verbatim).</item>
/// <item>The formula's own arithmetic SHAPE — <c>6 + 3 * &lt;anything&gt;</c>, whitespace-
/// insensitive — independent of what anyone names it. A copy that renamed the constants away (or
/// inlined the numbers with no names at all, the way
/// <c>CampProvisioningBalanceTests.SendActions</c> used to on the sim side) would defeat detector
/// 1 but not this one. The fee's VALUE is fixed by design (this unit does not retune it — see
/// <c>CampHandlersTests.SupplyFee_ValuePinnedAcrossCheckpointFloors</c>), so pinning its literal
/// shape here costs nothing this repo does not already intend to keep true.</item>
/// </list></para>
/// </summary>
public class SupplyFeeCopyCensusTests
{
    private static readonly string[] ConstantNameTokens = ["SupplyFeeBase", "SupplyFeePerFloor"];

    private static readonly Regex ArithmeticShape = new(@"6\s*\+\s*3\s*\*", RegexOptions.Compiled);

    [Fact]
    public void NoGodotScriptReDeclaresTheSupplyFeeConstantsByName()
    {
        var violations = new List<string>();
        foreach (var (relative, absolute) in SourceFiles())
        {
            var code = File.ReadAllText(absolute);
            foreach (var token in ConstantNameTokens)
            {
                if (code.Contains(token))
                {
                    violations.Add($"{relative} contains '{token}' — call CampHandlers.SupplyFee instead of re-declaring its constants.");
                }
            }
        }

        Assert.True(violations.Count == 0,
            "A godot/scripts file re-declares the runner-fee constants instead of calling "
            + "GameSim.Expedition.CampHandlers.SupplyFee — the exact P2-HONEST-22 mirror this "
            + "census exists to close:\n  " + string.Join("\n  ", violations));
    }

    [Fact]
    public void NoGodotScriptReDerivesTheSupplyFeeArithmeticUnderAnyName()
    {
        var violations = new List<string>();
        foreach (var (relative, absolute) in SourceFiles())
        {
            var code = File.ReadAllText(absolute);
            if (ArithmeticShape.IsMatch(code))
            {
                violations.Add($"{relative} matches the runner-fee arithmetic shape (6 + 3 * ...).");
            }
        }

        Assert.True(violations.Count == 0,
            "A godot/scripts file reproduces the runner-fee FORMULA by shape — renaming the "
            + "constants does not evade this detector, only calling CampHandlers.SupplyFee does:\n  "
            + string.Join("\n  ", violations));
    }

    /// <summary>The positive half: the panel must actually CALL the shared formula, not just avoid
    /// re-deriving it — a panel that silently dropped the fee display would pass both tests
    /// above too, and that is a different, worse defect (P2-HONEST family precedent:
    /// <see cref="CommissionQueuePredicateCopyCensusTests.ShopPanel_ActuallyCallsTheSharedForecast_RatherThanGoingSilent"/>).</summary>
    [Fact]
    public void CampPanel_ActuallyCallsTheSharedFormula_RatherThanGoingSilent()
    {
        var repoRoot = RepoRoot();
        var campPanelPath = Path.Combine(repoRoot, "godot", "scripts", "panels", "CampPanel.cs");
        Assert.True(File.Exists(campPanelPath), $"Expected to find {campPanelPath}.");

        var code = File.ReadAllText(campPanelPath);
        Assert.Contains("CampHandlers.SupplyFee(", code);
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
