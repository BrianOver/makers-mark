using System.Text.RegularExpressions;

namespace GameSim.Tests.Hygiene;

/// <summary>
/// P2-SCREEN-29: a sized <c>TextureRect</c> cannot silently claim its texture's own pixel size
/// instead of the size its code actually asked for.
///
/// <para><b>The defect.</b> Godot's <c>TextureRect</c> defaults <c>ExpandMode</c> to <c>KeepSize</c>,
/// whose <c>GetMinimumSize()</c> returns the BOUND TEXTURE'S OWN pixel size, ignoring
/// <c>CustomMinimumSize</c> entirely. Code that requests a 20px icon gets a control whose real
/// minimum is whatever the artist authored (this repo's glyphs run 64x64+), and the layout silently
/// inflates. This has shipped three times: PR #119 (fixed in <c>UiKit.ArtRect</c>), P2-SCREEN-24
/// (PR #826, fixed in <c>UiKit.DrawerHeader</c>'s icon — a 24px request became a 64px minimum and
/// overhung the 56px header strip on every drawer), and, by grep on <c>main</c> at the time this
/// census was written, 8 more live sites carrying the identical shape.</para>
///
/// <para>Deny-by-default, same idiom as <c>GearWornCheckCensusTests</c>: any <c>new TextureRect</c>
/// initializer that sets <c>CustomMinimumSize</c> — i.e. asks for a specific size — without also
/// setting <c>ExpandMode</c> is a violation unless pinned in <see cref="Exceptions"/> with a citation.
/// A <c>TextureRect</c> that never claims a size of its own (e.g. one sized directly via runtime
/// <c>Size</c>/<c>Position</c> outside any layout container, as in <c>DelveStage.SpawnSparkle</c>'s
/// transient VFX sprite) is out of scope by construction: it sets no <c>CustomMinimumSize</c>, so
/// nothing here claims an opinion about it.</para>
///
/// <para><b>Honesty framing</b> (same disclaimer as <c>SupplyFeeCopyCensusTests</c> and
/// <c>GearWornCheckCensusTests</c>): a structural regex/brace-balance heuristic, not a C# parser. It
/// skips string literals (including interpolated ones, e.g. <c>SceneBanner</c>'s
/// <c>$"SceneBanner_{artId}"</c>) so an interpolation hole's braces never desync the balance count,
/// but it cannot see an <c>ExpandMode</c> set outside the object initializer (e.g. assigned on a
/// following line) — every real site in this codebase sets it inline, so that gap costs nothing
/// today.</para>
/// </summary>
public class TextureRectExpandModeCensusTests
{
    /// <summary>(relative path, normalized initializer body) → reason citing the ruling/PR that
    /// grants the exception — same citation contract as <c>GearWornCheckCensusTests.Exceptions</c>.
    /// </summary>
    private static readonly Dictionary<(string File, string Statement), string> Exceptions = new()
    {
        // P2-SCREEN-24 (PR #826, open at the time P2-SCREEN-29 shipped): DrawerHeader's icon tile
        // already carries this exact ExpandMode=IgnoreSize fix on that branch. P2-SCREEN-29
        // deliberately does not touch it — editing the same lines from two open PRs is how one of
        // them silently reverts the other on merge. Drop this exception the day #826 lands: the
        // merged code carries ExpandMode and stops tripping this census on its own, so a stale
        // entry here just goes unused rather than masking anything.
        [("godot/scripts/ui/UiKit.cs",
          "Name = \"Icon\", Texture = icon, CustomMinimumSize = new Vector2(DrawerHeaderIconSize, DrawerHeaderIconSize), StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = Control.MouseFilterEnum.Ignore,")] =
            "P2-SCREEN-24 (PR #826, open): already fixed on that branch; not duplicated here to " +
            "avoid two open PRs racing the same lines.",
    };

    private const int ExpectedExceptionCount = 1;

    [Fact]
    public void ExceptionCount_IsPinned_SoEveryNewGrantIsAVisibleDiff()
        => Assert.True(Exceptions.Count == ExpectedExceptionCount,
            $"Pinned at {ExpectedExceptionCount}; the table now holds {Exceptions.Count}.");

    [Fact]
    public void EveryPinnedException_CitesTheRulingThatGrantedIt()
    {
        var citation = new Regex(@"§11\.7|\bP\d+\b|#\d+\b");
        var uncited = Exceptions
            .Where(e => !citation.IsMatch(e.Value))
            .Select(e => $"{e.Key.File} [{e.Key.Statement}]")
            .ToList();

        Assert.True(uncited.Count == 0,
            "An exception with no ruling/PR behind it is drift wearing a reason:\n  "
            + string.Join("\n  ", uncited));
    }

    [Fact]
    public void EverySizedTextureRect_SetsExpandModeExplicitly_UnlessPinned()
    {
        var violations = new List<string>();
        foreach (var (relative, absolute) in CensusSourceFiles())
        {
            var code = StripComments(File.ReadAllText(absolute));
            foreach (var statement in FindTextureRectInitializers(code))
            {
                if (!statement.Contains("CustomMinimumSize", StringComparison.Ordinal))
                {
                    continue; // out of scope: this site claims no size of its own to defend.
                }

                if (statement.Contains("ExpandMode", StringComparison.Ordinal))
                {
                    continue; // already explicit — the whole point.
                }

                if (Exceptions.ContainsKey((relative, statement)))
                {
                    continue;
                }

                var shown = statement.Length > 200 ? statement[..200] + "…" : statement;
                violations.Add($"{relative} [{shown}]");
            }
        }

        Assert.True(violations.Count == 0,
            "A TextureRect sets CustomMinimumSize but never ExpandMode — Godot's KeepSize default "
            + "means GetMinimumSize() reports the TEXTURE'S OWN pixel size, silently overriding the "
            + "requested CustomMinimumSize (PR #119 / P2-SCREEN-24's exact shape, shipped three "
            + "times). Set ExpandMode explicitly (IgnoreSize when the container should dictate size, "
            + "KeepAspectCentered/KeepAspectCovered when the art's own aspect matters and only the "
            + "clamp should come from outside), or pin a cited exception if KeepSize is genuinely "
            + "wanted; never soften this test:\n  " + string.Join("\n  ", violations));
    }

    /// <summary>Regression proof: the actual pre-fix <c>UiKit.ArtRect</c> fallback-icon shape (PR
    /// #119's class), reduced to a standalone snippet so this detector's own correctness never
    /// depends on an unfixed instance still existing anywhere in the tree.</summary>
    [Fact]
    public void RegressionProof_WouldHaveCaughtTheActualBugShape()
    {
        const string historicalBug = """
            var icon = new TextureRect
            {
                Name = "FallbackIcon",
                Texture = fallbackIcon ?? IconRegistry.Glyph(DefaultFallbackGlyph),
                CustomMinimumSize = size * 0.5f,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            """;

        var statements = FindTextureRectInitializers(historicalBug).ToList();
        Assert.Single(statements);
        Assert.Contains("CustomMinimumSize", statements[0], StringComparison.Ordinal);
        Assert.DoesNotContain("ExpandMode", statements[0], StringComparison.Ordinal);
    }

    /// <summary>Negative control 1: a TextureRect that sets ExpandMode explicitly (the fixed shape)
    /// must not be flagged.</summary>
    [Fact]
    public void NegativeControl_ExplicitExpandModeIsNotFlagged()
    {
        const string fixedShape = """
            var rect = new TextureRect
            {
                Texture = art,
                CustomMinimumSize = new Vector2(0, SceneBannerHeight),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
            };
            """;

        var statements = FindTextureRectInitializers(fixedShape).ToList();
        Assert.Single(statements);
        Assert.Contains("ExpandMode", statements[0], StringComparison.Ordinal);
    }

    /// <summary>Negative control 2: a TextureRect claiming no size of its own (no
    /// CustomMinimumSize — sized instead by a direct runtime <c>Size</c>, e.g.
    /// <c>DelveStage.SpawnSparkle</c>'s free-floating transient) is out of scope, not a violation.
    /// </summary>
    [Fact]
    public void NegativeControl_NoCustomMinimumSizeIsOutOfScope()
    {
        const string freeSprite = """
            var sprite = new TextureRect
            {
                Texture = texture,
                Size = new Vector2(20, 20),
                Position = position,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            };
            """;

        var statements = FindTextureRectInitializers(freeSprite).ToList();
        Assert.Single(statements);
        Assert.DoesNotContain("CustomMinimumSize", statements[0], StringComparison.Ordinal);
    }

    /// <summary>Negative control 3: an interpolated-string <c>Name</c> holding its own braces (the
    /// real <c>SceneBanner</c> shape, <c>$"SceneBanner_{artId}"</c>) must not desync the brace
    /// balance the extractor relies on to find the initializer's true closing brace.</summary>
    [Fact]
    public void NegativeControl_InterpolatedStringBracesDoNotDesyncBraceBalance()
    {
        const string interpolatedName = """
            var rect = new TextureRect
            {
                Name = $"SceneBanner_{artId}",
                Texture = art,
                CustomMinimumSize = new Vector2(0, SceneBannerHeight),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            };
            var host = new MarginContainer();
            """;

        var statements = FindTextureRectInitializers(interpolatedName).ToList();
        Assert.Single(statements);
        Assert.Contains("ExpandMode", statements[0], StringComparison.Ordinal);
        Assert.DoesNotContain("MarginContainer", statements[0], StringComparison.Ordinal);
    }

    private static readonly Regex NewTextureRect = new(@"\bnew\s+TextureRect\b", RegexOptions.Compiled);

    /// <summary>Finds every <c>new TextureRect { ... }</c> object initializer in <paramref
    /// name="code"/> and yields its whitespace-normalized inner body. Balances braces by hand
    /// (rather than a single regex, which cannot balance nested braces) and skips over string
    /// literals — including an interpolated string's <c>{expr}</c> holes — so a quoted <c>{</c>/
    /// <c>}</c> never miscounts as initializer nesting.</summary>
    private static IEnumerable<string> FindTextureRectInitializers(string code)
    {
        foreach (Match m in NewTextureRect.Matches(code))
        {
            var body = ExtractInitializerBody(code, m.Index + m.Length);
            if (body is not null)
            {
                yield return NormalizeWhitespace(body);
            }
        }
    }

    private static string? ExtractInitializerBody(string code, int searchFrom)
    {
        var open = code.IndexOf('{', searchFrom);
        if (open < 0)
        {
            return null;
        }

        var depth = 0;
        for (var i = open; i < code.Length; i++)
        {
            var c = code[i];
            if (c == '"')
            {
                i++;
                while (i < code.Length && code[i] != '"')
                {
                    if (code[i] == '\\')
                    {
                        i++;
                    }

                    i++;
                }

                continue;
            }

            if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return code.Substring(open + 1, i - open - 1);
                }
            }
        }

        return null;
    }

    private static string NormalizeWhitespace(string s) => Regex.Replace(s.Trim(), @"\s+", " ");

    private static string StripComments(string source)
    {
        source = Regex.Replace(source, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        source = Regex.Replace(source, @"//[^\n]*", " ");
        return source;
    }

    private static List<(string Relative, string Absolute)> CensusSourceFiles()
    {
        var root = GodotScriptsRoot();
        return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(p => (Path.GetRelativePath(RepoRoot(), p).Replace('\\', '/'), p))
            .OrderBy(t => t.Item1, StringComparer.Ordinal)
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

    private static string GodotScriptsRoot()
    {
        var root = Path.Combine(RepoRoot(), "godot", "scripts");
        Assert.True(Directory.Exists(root), $"Expected the godot adapter scripts at {root}.");
        return root;
    }
}
