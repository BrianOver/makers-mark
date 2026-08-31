using System.Reflection;
using System.Text.RegularExpressions;

namespace GameSim.Tests.Presentation;

/// <summary>
/// P2-SCREEN-01's tripwire. <c>GameTheme.Build()</c> and five call sites set Button font colours
/// by <b>Godot 3</b> theme item names (<c>font_color_hover</c> / <c>font_color_pressed</c> /
/// <c>font_color_disabled</c>) -- Godot 4 renamed all three, and an unrecognised theme item name
/// is silently accepted and never applied: no exception, no warning, no runtime signal at all.
/// That let three theme colours sit dead since the theme was written, discovered only by
/// rendering a frame and looking at it (see the P2-SCREEN-01 PR).
///
/// <para><b>Why it lives in the sim test project.</b> Same reasoning as <see
/// cref="ClientAuthorityCensusTests"/> right beside it: this is a text scan of
/// <c>godot/scripts/**/*.cs</c>, needs no Godot runtime, and belongs in the lane that always
/// runs rather than the engine suite that has twice silently run a fraction of its cases.</para>
///
/// <para><b>THE HONEST FRAMING.</b> This is a string-literal census of every <c>Theme.SetColor</c>
/// and <c>Control.AddThemeColorOverride</c> call site, checked against the legal Godot 4 colour
/// item names for the control types this theme actually styles (Button/Label/OptionButton,
/// queried directly off <c>ThemeDB.get_default_theme()</c> in a running 4.6.3 engine -- not typed
/// from memory). It cannot see a key built at runtime from a variable or concatenation (none
/// exist today; every site in this file scan is a literal), and it does not attempt to verify
/// each key against the SPECIFIC control type it is applied to (a Label-only key used on a Button
/// would pass) -- the same proximity-heuristic honesty disclaimer <see
/// cref="ClientAuthorityCensusTests"/> already carries for its own scans.</para>
/// </summary>
public class ThemeColorKeyCensusTests
{
    /// <summary>
    /// Every colour theme item Godot 4.6.3 actually defines for Button, Label, and OptionButton --
    /// the three control types <c>godot/scripts/**/*.cs</c> calls <c>SetColor</c>/
    /// <c>AddThemeColorOverride</c> on today. Queried directly: headless
    /// <c>ThemeDB.get_default_theme().get_color_list(type)</c> for each type, not hand-typed.
    /// A future site styling a new control type needs its legal names unioned in here, not a
    /// narrower regex.
    /// </summary>
    private static readonly HashSet<string> LegalThemeColorKeys = new(StringComparer.Ordinal)
    {
        // Button / OptionButton (identical color item sets in 4.6.3)
        "font_color", "font_disabled_color", "font_focus_color", "font_hover_color",
        "font_hover_pressed_color", "font_outline_color", "font_pressed_color",
        "icon_disabled_color", "icon_focus_color", "icon_hover_color",
        "icon_hover_pressed_color", "icon_normal_color", "icon_pressed_color",
        // Label (font_color/font_outline_color already covered above)
        "font_shadow_color",
    };

    /// <summary>One theme-color call site: which method, the literal key string, and where.</summary>
    private static readonly Regex ThemeColorCall =
        new(@"\.(?:SetColor|AddThemeColorOverride)\(\s*""([^""]+)""", RegexOptions.Compiled);

    [Fact]
    public void NoThemeColorKey_UsesAGodot3SpellingOrAnyOtherUnknownName()
    {
        var files = ClientSourceFiles();
        Assert.True(files.Count >= 60,
            $"Only {files.Count} client scripts were scanned — too few to trust a green run. Check "
            + "the repo-root walk, not this floor.");

        var totalCallSites = 0;
        var violations = new List<string>();
        foreach (var (relative, absolute) in files)
        {
            var code = StripComments(File.ReadAllText(absolute));
            var lineStarts = LineStartOffsets(code);
            foreach (Match m in ThemeColorCall.Matches(code))
            {
                totalCallSites++;
                var key = m.Groups[1].Value;
                if (LegalThemeColorKeys.Contains(key)) continue;
                var line = LineNumberFor(lineStarts, m.Index);
                violations.Add($"{relative}:{line} [\"{key}\"]");
            }
        }

        Assert.True(totalCallSites >= 60,
            $"Only {totalCallSites} SetColor/AddThemeColorOverride call sites were found — too few "
            + "to trust this census; the regex may no longer match the call shape in use.");

        Assert.True(violations.Count == 0,
            "A theme-color call uses a key that is not a real Godot 4 Button/Label/OptionButton "
            + "colour item name (P2-SCREEN-01: Godot silently ignores an unknown theme item name, "
            + "so this never fails at runtime). Fix the spelling; do not widen "
            + $"{nameof(LegalThemeColorKeys)} to legalize a typo:\n  "
            + string.Join("\n  ", violations));
    }

    /// <summary>
    /// Regression proof: the exact three Godot-3 names this unit found dead in <c>GameTheme.cs</c>
    /// and <c>MainUi.cs</c>, reduced to a standalone snippet so this test's own correctness never
    /// depends on the historical bug still existing anywhere in the tree.
    /// </summary>
    [Fact]
    public void RegressionProof_WouldHaveCaughtTheActualGodot3Spellings()
    {
        const string historicalBug = """
            theme.SetColor("font_color_hover", "Button", BoneColor);
            theme.SetColor("font_color_pressed", "Button", BoneColor);
            theme.SetColor("font_color_disabled", "Button", new Color(BodyTextColor, 0.5f));
            button.AddThemeColorOverride("font_color_hover", GameTheme.BoneColor);
            button.AddThemeColorOverride("font_color_pressed", GameTheme.BoneColor);
            """;

        var found = ThemeColorCall.Matches(historicalBug)
            .Select(m => m.Groups[1].Value)
            .Where(key => !LegalThemeColorKeys.Contains(key))
            .ToList();

        Assert.Equal(
            new[] { "font_color_hover", "font_color_pressed", "font_color_disabled", "font_color_hover", "font_color_pressed" },
            found);
    }

    [Fact]
    public void RegressionProof_DoesNotFlagTheirGodot4Replacements()
    {
        const string fixedCode = """
            theme.SetColor("font_hover_color", "Button", BoneColor);
            theme.SetColor("font_pressed_color", "Button", BoneColor);
            theme.SetColor("font_disabled_color", "Button", new Color(BodyTextColor, 0.5f));
            button.AddThemeColorOverride("font_hover_color", GameTheme.BoneColor);
            button.AddThemeColorOverride("font_pressed_color", GameTheme.BoneColor);
            """;

        var unknown = ThemeColorCall.Matches(fixedCode)
            .Select(m => m.Groups[1].Value)
            .Where(key => !LegalThemeColorKeys.Contains(key))
            .ToList();

        Assert.Empty(unknown);
    }

    private static int[] LineStartOffsets(string code)
    {
        var starts = new List<int> { 0 };
        for (var i = 0; i < code.Length; i++)
        {
            if (code[i] == '\n') starts.Add(i + 1);
        }
        return starts.ToArray();
    }

    private static int LineNumberFor(int[] lineStarts, int index)
    {
        var line = Array.BinarySearch(lineStarts, index);
        if (line < 0) line = ~line - 1;
        return line + 1; // 1-based
    }

    private static string StripComments(string source)
    {
        source = Regex.Replace(source, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        source = Regex.Replace(source, @"//[^\n]*", " ");
        return source;
    }

    private static List<(string Relative, string Absolute)> ClientSourceFiles()
    {
        var root = ClientRoot();
        return Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(p => (Path.GetRelativePath(root, p).Replace('\\', '/'), p))
            .OrderBy(t => t.Item1, StringComparer.Ordinal)
            .ToList();
    }

    private static string ClientRoot()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Game.sln")))
            dir = dir.Parent;

        Assert.True(dir is not null, "Could not find Game.sln walking up from the test assembly.");
        var scripts = Path.Combine(dir!.FullName, "godot", "scripts");
        Assert.True(Directory.Exists(scripts), $"Expected the client at {scripts}.");
        return scripts;
    }
}
