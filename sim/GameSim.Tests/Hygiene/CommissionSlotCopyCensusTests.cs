using System.Text.RegularExpressions;

namespace GameSim.Tests.Hygiene;

/// <summary>
/// P2-HONEST-11 (owner ruling 2026-09-03, P2-OQ7 resolved honesty over teeth): a census that
/// DISCOVERS every player-facing rendering of a commission's wanted slot, rather than trusting a
/// hand-typed list of file:line sites — the exact "what discovers those sites?" question the
/// ruling itself asks. Every site this finds must route the slot through
/// <c>GameSim.Heroes.CommissionSystem.SlotHonestyNote</c>, which is empty for every slot except
/// Trinket and names the favor for Trinket (see <c>CommissionSystemTests</c> for that half) — so a
/// trinket ask can never again read as though it carries the same combat weight as a Weapon/
/// Shield/Armor ask, in this surface or a future one.
///
/// <para><b>Discovery, not a list.</b> Scans every <c>.cs</c> file under <c>sim/GameSim</c>,
/// <c>sim/GameSim.Cli</c>, and <c>godot/scripts</c> for a <c>{x.Slot</c> INTERPOLATION HOLE —
/// deliberately anchored on the leading <c>{</c> so plain code that reads <c>.Slot</c> (an
/// <c>if</c> guard, a <c>.Where</c> filter, a record constructor argument — none of them
/// player-facing) never matches; only text actually rendered inside a <c>$"..."</c> can. Around
/// each hit it takes a character WINDOW (not a single line — several real sites split one
/// interpolated sentence across two source lines with <c>+</c>) wide enough to hold the whole
/// statement: back far enough to reach the enclosing switch arm's case pattern (e.g.
/// <c>CommissionPosted e =&gt;</c> sits one line above the string it guards), forward to the
/// statement's own terminator. A site counts as a commission ask when that window mentions a
/// commission (the substring "ommission", catching "Commission"/"commission" by construction —
/// including the identifier name <c>commission</c> itself) and must then also call
/// <c>SlotHonestyNote(</c> somewhere in the same window. A recipe/item slot LABEL with no
/// commission nearby (<c>ForgePanel</c>'s "(t1 Weapon)", <c>ProvenanceCard</c>'s item header) is a
/// real interpolation hole too, but its window never mentions a commission, so it needs no
/// exception either.</para>
///
/// <para>Deny-by-default, same idiom as <c>GearWornCheckCensusTests</c>/
/// <c>ClientAuthorityCensusTests</c>: a pinned exception must cite the ruling that grants it. Not a
/// parser — a structural heuristic (same honesty disclaimer as its siblings): the window is sized
/// generously for this codebase's actual switch-arm shapes, not proven correct for arbitrary C#.</para>
/// </summary>
public class CommissionSlotCopyCensusTests
{
    /// <summary>(relative path, character offset of the .Slot match) → reason citing the ruling
    /// that grants the exception.</summary>
    private static readonly Dictionary<(string File, int Offset), string> Exceptions = new();

    private const int ExpectedExceptionCount = 0;

    private const int BackwardWindow = 200;
    private const int ForwardCap = 500;

    private static readonly Regex SlotToken = new(@"\{[A-Za-z_][\w.]*\.Slot\b");
    private static readonly Regex CommissionMention = new("ommission");
    private static readonly Regex HonestyCall = new(@"SlotHonestyNote\(");

    /// <summary>A real statement/switch-arm terminator in this codebase's own formatting: a
    /// <c>;</c> or <c>,</c> immediately followed by a newline. Every real site's interpolated
    /// prose also contains bare commas ("wants {Slot} work, {Quality} or better") — those are
    /// followed by a space, never a newline, so this does not stop early on them.</summary>
    private static readonly Regex StatementTerminator = new(@"[;,]\r?\n");

    [Fact]
    public void ExceptionCount_IsPinned_SoEveryNewGrantIsAVisibleDiff()
        => Assert.True(Exceptions.Count == ExpectedExceptionCount,
            $"Pinned at {ExpectedExceptionCount}; the table now holds {Exceptions.Count}.");

    [Fact]
    public void EveryPinnedException_CitesTheRulingThatGrantedIt()
    {
        var citation = new Regex(@"§11\.7|\bP\d+\b");
        var uncited = Exceptions
            .Where(e => !citation.IsMatch(e.Value))
            .Select(e => $"{e.Key.File}@{e.Key.Offset}")
            .ToList();

        Assert.True(uncited.Count == 0,
            "An exception with no ruling behind it is drift wearing a reason:\n  "
            + string.Join("\n  ", uncited));
    }

    /// <summary>The regression proof: the pre-fix commission board header line, standalone, so
    /// this detector's own correctness never depends on the real gap still existing in the tree.</summary>
    [Fact]
    public void RegressionProof_WouldHaveCaughtTheActualOverclaim()
    {
        const string historicalCode =
            "AddHeader(body, $\"{heroName} wants a {commission.MinQuality} {commission.Slot} or better\");";

        var window = Assert.Single(SlotWindows(historicalCode).ToList());
        Assert.Matches(CommissionMention, window);
        Assert.DoesNotMatch(HonestyCall, window);
    }

    /// <summary>The regression proof's twin for the real multi-line shape (a switch arm whose case
    /// pattern names the commission one line above the string, and whose fix lands one line below
    /// the <c>.Slot</c> reference) — the exact shape a naive per-LINE scan would miss entirely.</summary>
    [Fact]
    public void RegressionProof_CatchesTheMultiLineSwitchArmShape()
    {
        const string unfixed = """
            CommissionPosted e =>
                $"{HeroName(state, e.Hero)} wants {e.Slot} work, {e.MinQuality} or better, by day {e.DeadlineDay} " +
                $"— {e.PremiumGold}g over list.",
            """;
        const string fixedCode = """
            CommissionPosted e =>
                $"{HeroName(state, e.Hero)} wants {e.Slot} work, {e.MinQuality} or better, by day {e.DeadlineDay} " +
                $"— {e.PremiumGold}g over list{CommissionSystem.SlotHonestyNote(e.Slot)}.",
            """;

        var unfixedWindow = Assert.Single(SlotWindows(unfixed).ToList());
        Assert.Matches(CommissionMention, unfixedWindow);
        Assert.DoesNotMatch(HonestyCall, unfixedWindow);

        var fixedWindow = Assert.Single(SlotWindows(fixedCode).ToList());
        Assert.Matches(CommissionMention, fixedWindow);
        Assert.Matches(HonestyCall, fixedWindow);
    }

    /// <summary>The negative-control proof: a recipe/item slot LABEL with no commission in the
    /// sentence must not be flagged — it is naming a category, not asking for a favor.</summary>
    [Fact]
    public void NegativeControl_ARecipeSlotLabelWithNoCommissionIsNotFlagged()
    {
        const string recipeLabel = "AddLabel(infoCol, $\"{recipe.Name} (t{recipe.Tier} {recipe.Slot})\");";

        var window = Assert.Single(SlotWindows(recipeLabel).ToList());
        Assert.DoesNotMatch(CommissionMention, window);
    }

    [Fact]
    public void EveryCommissionSlotRender_RoutesThroughSlotHonestyNote_UnlessPinnedWithAReason()
    {
        var violations = new List<string>();
        foreach (var (relative, absolute) in SourceFiles())
        {
            var code = File.ReadAllText(absolute);
            foreach (var (offset, window) in SlotWindowsWithOffset(code))
            {
                if (!CommissionMention.IsMatch(window) || HonestyCall.IsMatch(window))
                {
                    continue;
                }

                if (Exceptions.ContainsKey((relative, offset)))
                {
                    continue;
                }

                var lineNumber = code[..offset].Count(c => c == '\n') + 1;
                violations.Add($"{relative}:{lineNumber}  {window.Trim()}");
            }
        }

        Assert.True(violations.Count == 0,
            "A statement renders a commission's wanted slot without CommissionSystem.SlotHonestyNote "
            + "— P2-HONEST-11: a trinket ask must never read with the same combat weight as a Weapon/"
            + "Shield/Armor ask. Route the slot through SlotHonestyNote, or pin a cited exception if "
            + "this really is not a commission ask:\n  " + string.Join("\n  ", violations));
    }

    private static IEnumerable<string> SlotWindows(string code) => SlotWindowsWithOffset(code).Select(t => t.Window);

    /// <summary>For every <c>.Slot</c> reference in <paramref name="code"/>, a generous character
    /// window around it: back far enough to reach an enclosing switch arm's case pattern one line
    /// up, forward to the statement's own <c>;</c> or <c>,</c> terminator (capped, mirroring
    /// <c>GearWornCheckCensusTests.FindWornGearStatements</c>'s identical idiom).</summary>
    private static IEnumerable<(int Offset, string Window)> SlotWindowsWithOffset(string code)
    {
        foreach (Match m in SlotToken.Matches(code))
        {
            var start = Math.Max(0, m.Index - BackwardWindow);
            var rest = code[m.Index..];
            var terminator = StatementTerminator.Match(rest);
            var forwardEnd = terminator.Success
                ? m.Index + Math.Min(terminator.Index + 1, ForwardCap)
                : Math.Min(code.Length, m.Index + ForwardCap);

            yield return (m.Index, code[start..forwardEnd]);
        }
    }

    private static List<(string Relative, string Absolute)> SourceFiles()
    {
        var repoRoot = RepoRoot();
        var dirs = new[] { "sim/GameSim", "sim/GameSim.Cli", "godot/scripts" };

        var files = new List<(string, string)>();
        foreach (var dir in dirs)
        {
            var full = Path.Combine(repoRoot, dir.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(full))
            {
                continue;
            }

            foreach (var path in Directory.EnumerateFiles(full, "*.cs", SearchOption.AllDirectories))
            {
                if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                    || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                {
                    continue;
                }

                files.Add((Path.GetRelativePath(repoRoot, path).Replace('\\', '/'), path));
            }
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
