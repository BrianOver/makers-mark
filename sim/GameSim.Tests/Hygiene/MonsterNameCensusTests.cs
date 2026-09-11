using System.Text.RegularExpressions;
using GameSim.Venues;

namespace GameSim.Tests.Hygiene;

/// <summary>
/// P2-PROOF-12 (§11.15, P2-HONEST family D): a census that DISCOVERS every place a recorded
/// <c>MonsterKind</c> is shaped into a noun phrase, rather than trusting anyone to remember that
/// <see cref="MonsterName"/> exists.
///
/// <para><b>The defect it closes, measured.</b> Every venue's bottom floor is a named boss — "The
/// Forgeworm", "The Undertow", "The Bellows-Mad", "The Undying Forge-Heart" — and a proper name
/// already carries its article. <c>ExpeditionRevealSystem.DeathReport</c> knew that;
/// <c>AttributionEngine</c> did not, at either site, and neither did <c>JourneyStream</c>, at
/// either of ITS two. A 20-seed × 100-day sweep produced, verbatim, <c>"Greataxe landed the killing
/// blow on the The Forgeworm"</c> — the deepest beat the game can produce, inside the sentence
/// <c>CLAUDE.md</c>'s epigraph is written from. One rule, hand-copied into a second place and only
/// the first copy maintained; by the time anyone looked there were four sites and two had never
/// been right.</para>
///
/// <para><b>Two shapes, because the defect has two halves.</b> Fixing only the sentences would have
/// left the RULE duplicated and a fifth copy one PR away.
/// <list type="bullet">
/// <item><b>The article shape</b> — a monster kind interpolated under an article. Anchored on the
/// leading <c>{</c>, so ordinary code reading <c>.MonsterKind</c> (a comparison, a record argument,
/// a dictionary key) can never match; only text inside a <c>$"..."</c> can.</item>
/// <item><b>The rule shape</b> — a hand-written <c>StartsWith("The "</c> test sitting beside a
/// monster kind. That is the rule itself, re-derived.</item>
/// </list></para>
///
/// <para>Structural heuristic, same honesty disclaimer as its siblings
/// (<c>CommissionSlotCopyCensusTests</c>, <c>GearWornCheckCensusTests</c>): written against this
/// codebase's own formatting, not proven correct for arbitrary C#.</para>
/// </summary>
public class MonsterNameCensusTests
{
    /// <summary>(relative path, matched text) → reason citing the ruling that grants it.</summary>
    private static readonly Dictionary<(string File, string Match), string> Exceptions = new();

    private const int ExpectedExceptionCount = 0;

    /// <summary>
    /// A monster kind interpolated under an article. Up to two words may sit between them, which is
    /// what catches the ATTRIBUTIVE shape as well as the plain one — the lethal-save line read
    /// <c>"turned a lethal {combat.MonsterKind} hit"</c>, where the article is two words back and is
    /// still what makes "a lethal The Forgeworm hit" ungrammatical.
    ///
    /// <para>A corrected site cannot match: every <see cref="MonsterName"/> form returns the article
    /// WITH the name, or replaces it entirely, so the hole reads <c>on {MonsterName.Definite(...)}</c>
    /// with no bare article in front of it. Bare labels with no article at all — the forecast board's
    /// <c>"F{floor}: {kind}"</c>, the camp's <c>"floor {n} ({kind})"</c> — are grammatical for both
    /// proper names and kinds, and correctly do not match.</para>
    /// </summary>
    private static readonly Regex ArticleNearKind =
        new(@"(?<![\w.])(the|a|an)( \w+){0,2} \{[A-Za-z_][\w.]*MonsterKind\b", RegexOptions.IgnoreCase);

    /// <summary>
    /// The rule itself, hand-written: a <c>StartsWith("The "</c> test in the same statement as a
    /// monster kind. The proximity requirement is load-bearing — <c>AssetCatalog</c> and
    /// <c>DelveStage</c> both strip a leading "The " to build an asset lookup KEY, which is a
    /// different rule about a different string, and a blunt scan flags both of them for nothing.
    /// </summary>
    private static readonly Regex HandWrittenProperNameTest =
        new(@"MonsterKind[^;]{0,200}?StartsWith\(""The ""|StartsWith\(""The ""[^;]{0,200}?MonsterKind");

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
            .Select(e => $"{e.Key.File}  {e.Key.Match}")
            .ToList();

        Assert.True(uncited.Count == 0,
            "An exception with no ruling behind it is drift wearing a reason:\n  " + string.Join("\n  ", uncited));
    }

    /// <summary>The regression proof: all four real pre-fix lines, standalone, so this detector's
    /// own correctness never depends on the defect still being in the tree.</summary>
    [Fact]
    public void RegressionProof_WouldHaveCaughtAllFourActualSites()
    {
        const string killingBlow =
            "$\"{items[killer.Value].Name} landed the killing blow on the {combat.MonsterKind}\"";
        const string lethalSave =
            "$\"{items[defId.Value].Name} turned a lethal {combat.MonsterKind} hit\"";
        const string journeyKill = "$\"{heroName} fells the {combat.MonsterKind}.\"";
        const string journeyHit = "$\"{heroName} takes {combat.DamageTaken} from the {combat.MonsterKind}.\"";

        Assert.Matches(ArticleNearKind, killingBlow);
        Assert.Matches(ArticleNearKind, lethalSave);
        Assert.Matches(ArticleNearKind, journeyKill);
        Assert.Matches(ArticleNearKind, journeyHit);
    }

    /// <summary>The negative control: the corrected shapes, and the bare-label shape that was never
    /// a defect, must NOT match — or the census would be unsatisfiable and the next session would
    /// "fix" it by deleting the guard.</summary>
    [Fact]
    public void NegativeControl_CorrectedAndBareLabelShapesAreClean()
    {
        const string killingBlow =
            "$\"{items[killer.Value].Name} landed the killing blow on {MonsterName.Definite(combat.MonsterKind)}\"";
        const string lethalSave =
            "$\"{items[defId.Value].Name} turned a lethal {MonsterName.AttributiveBlow(combat.MonsterKind)}\"";
        const string deathReport = "$\"slain by {MonsterName.Indefinite(last.MonsterKind)}\"";
        const string forecastLabel = "$\"  F{threat.Floor}: {threat.MonsterKind}\"";
        const string campLabel = "$\"floor {floor} ({venue.MonsterKind(floor)})\"";

        Assert.DoesNotMatch(ArticleNearKind, killingBlow);
        Assert.DoesNotMatch(ArticleNearKind, lethalSave);
        Assert.DoesNotMatch(ArticleNearKind, deathReport);
        Assert.DoesNotMatch(ArticleNearKind, forecastLabel);
        Assert.DoesNotMatch(ArticleNearKind, campLabel);
    }

    /// <summary>The rule-copy detector's positive control: the real pre-fix line, where the rule and
    /// the kind sit in one statement.</summary>
    [Fact]
    public void RegressionProof_CatchesTheHandWrittenRuleBesideAKind()
    {
        const string historicalCode =
            "var article = last.MonsterKind.StartsWith(\"The \", StringComparison.Ordinal) ? string.Empty : \"a \";";

        Assert.Matches(HandWrittenProperNameTest, historicalCode);
    }

    /// <summary>Its negative control: stripping a leading "The " to build an asset lookup KEY
    /// (<c>AssetCatalog</c>, <c>DelveStage</c>) is a different rule about a different string.</summary>
    [Fact]
    public void NegativeControl_StrippingTheArticleForAnAssetKeyIsADifferentRule()
    {
        const string assetKey = "if (trimmed.StartsWith(\"The \", StringComparison.OrdinalIgnoreCase))";

        Assert.DoesNotMatch(HandWrittenProperNameTest, assetKey);
    }

    [Fact]
    public void NoCopyPlacesAnArticleInFrontOfAMonsterKind_UnlessPinnedWithAReason()
        => AssertNoMatches(
            ArticleNearKind,
            includeMonsterNameItself: true,
            "Player copy places an article in front of a MonsterKind. Every venue's bottom floor is a "
            + "named boss carrying its own article, so this renders \"the The Forgeworm\". Route it "
            + "through MonsterName.Definite / .Indefinite / .AttributiveBlow (P2-PROOF-12):");

    [Fact]
    public void NoFileOutsideMonsterName_ReDerivesTheProperNameRule()
        => AssertNoMatches(
            HandWrittenProperNameTest,
            includeMonsterNameItself: false,
            "A file other than MonsterName tests StartsWith(\"The \") beside a MonsterKind — that IS "
            + "the proper-name rule, hand-copied. The second copy is how \"the The Forgeworm\" reached "
            + "a shipped build (P2-PROOF-12). Call MonsterName instead:");

    /// <summary>Every venue's every floor, driven through all three forms — data-driven rather than a
    /// spot check, so a NEW venue whose boss is named differently cannot slip past.</summary>
    [Fact]
    public void EveryVenueFloorRendersGrammatically_InAllThreeForms()
    {
        var seen = 0;
        var bosses = 0;

        foreach (var venue in VenueRegistry.All.Values)
        {
            for (var floor = 1; floor <= venue.FloorCount; floor++)
            {
                var kind = venue.MonsterKind(floor);
                seen++;

                var definite = MonsterName.Definite(kind);
                var indefinite = MonsterName.Indefinite(kind);
                var attributive = MonsterName.AttributiveBlow(kind);

                // The doubled article, in the two forms that can produce it. The attributive form
                // carries no article of its own, so it is proved exactly by the per-branch equality
                // assertions below rather than by a negative here.
                Assert.DoesNotContain("the The ", definite, StringComparison.Ordinal);
                Assert.DoesNotContain("a The ", indefinite, StringComparison.Ordinal);

                if (MonsterName.IsProperName(kind))
                {
                    bosses++;
                    Assert.Equal(kind, definite);
                    Assert.Equal(kind, indefinite);
                    Assert.Equal($"blow from {kind}", attributive);
                }
                else
                {
                    Assert.Equal($"the {kind}", definite);
                    Assert.Equal($"a {kind}", indefinite);
                    Assert.Equal($"{kind} hit", attributive);
                }
            }
        }

        // Fixture guards, not decoration: a registry that stopped yielding floors, or one whose
        // bosses stopped being proper names, would make every assertion above vacuously true.
        Assert.True(seen >= 5, $"Only {seen} venue floors enumerated — the registry stopped yielding.");
        Assert.True(bosses >= 1, "No venue floor carries a proper-name boss — this census now proves nothing.");
    }

    private static void AssertNoMatches(Regex pattern, bool includeMonsterNameItself, string message)
    {
        var violations = new List<string>();
        foreach (var (relative, absolute) in SourceFiles(includeMonsterNameItself))
        {
            var code = File.ReadAllText(absolute);
            foreach (Match m in pattern.Matches(code))
            {
                if (Exceptions.ContainsKey((relative, m.Value)))
                {
                    continue;
                }

                var lineNumber = code[..m.Index].Count(c => c == '\n') + 1;
                violations.Add($"{relative}:{lineNumber}  {m.Value}");
            }
        }

        Assert.True(violations.Count == 0, message + "\n  " + string.Join("\n  ", violations));
    }

    private static List<(string Relative, string Absolute)> SourceFiles(bool includeMonsterNameItself)
    {
        var repoRoot = RepoRoot();
        var dirs = new[] { "sim/GameSim", "sim/GameSim.Cli", "godot/scripts" };
        var monsterName = $"{Path.DirectorySeparatorChar}MonsterName.cs";

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
                    || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                    || (!includeMonsterNameItself && path.EndsWith(monsterName, StringComparison.Ordinal)))
                {
                    continue;
                }

                files.Add((Path.GetRelativePath(repoRoot, path).Replace('\\', '/'), path));
            }
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
