using System.Reflection;
using System.Text.RegularExpressions;

namespace GameSim.Tests.Presentation;

/// <summary>
/// Register #159's second half, made structurally impossible to reintroduce.
/// <c>AudioDirector.SpeakNarrator</c> RETURNS the line it chose specifically so a caller can put it
/// on screen ("Returns the line's text so a caller can show it on screen regardless — the screen is
/// the source of truth", that method's own doc). Before U-T5-6 every one of the three real call
/// sites called it as a bare expression statement and threw the string away — forty-nine recorded
/// lines were playing into a room where nothing wrote them down.
///
/// <para><b>Why a text census, not just the on-screen proof.</b> <c>NarratorTextOnScreenTests</c>
/// (the engine suite) proves the CURRENT three call sites surface their text — but that suite needs
/// Godot, serializes, and is exactly the lane this repo has twice caught silently running a fraction
/// of its cases. A FOURTH call site added later, written the same bare-statement way the first three
/// were, would regress silently until someone thought to look for it. This is the tripwire that
/// cannot miss it: it scans every <c>godot/scripts/**/*.cs</c> file for the literal shape
/// "<c>SpeakNarrator(...)</c> as the entire statement" — no assignment, no return, no wrapping call —
/// which is the only source shape this specific bug can take.</para>
///
/// <para><b>THE HONEST FRAMING.</b> This proves the return value is SYNTACTICALLY captured somewhere
/// in its own statement (assigned, returned, or handed straight to another call) — it cannot prove
/// that capture actually reaches a rendered Control. That is <c>NarratorTextOnScreenTests</c>' job.
/// This is the cheap, always-running half; that is the expensive, occasionally-running proof.</para>
/// </summary>
public class NarratorTextDiscardCensusTests
{
    [Fact]
    public void NoSpeakNarratorCall_IsABareDiscardedExpressionStatement()
    {
        var files = ClientSourceFiles();

        // The denominator guard (the green-54 lesson): a census that scanned nothing passes forever.
        Assert.True(files.Count >= 60,
            $"Only {files.Count} client scripts were scanned — too few to trust a green run. Check "
            + "the repo-root walk in ClientRoot(), not this floor.");

        var violations = new List<string>();
        foreach (var (relative, absolute) in files)
        {
            var code = StripComments(File.ReadAllText(absolute));
            violations.AddRange(FindBareDiscards(relative, code));
        }

        Assert.True(violations.Count == 0,
            "AudioDirector.SpeakNarrator's return value is discarded (CLAUDE.md rule 12, "
            + "show-only-sim-decided — SpeakNarrator's own doc: 'Returns the line's text so a caller "
            + "can show it on screen regardless — the screen is the source of truth'). Capture it — "
            + "assign it, return it, or hand it straight to a renderer — never call it as a bare "
            + "statement:\n  " + string.Join("\n  ", violations));
    }

    /// <summary>
    /// A live check that this census is not vacuous: it must actually find real
    /// <c>SpeakNarrator(</c> call sites (whether or not any of them violate the rule above) — proves
    /// the scanner's identifier match and file walk really reach <c>MainUi.cs</c>, rather than
    /// silently finding zero candidates and passing by default.
    /// </summary>
    [Fact]
    public void Census_ActuallyFindsRealSpeakNarratorCalls()
    {
        var files = ClientSourceFiles();
        var totalCalls = 0;
        foreach (var (_, absolute) in files)
        {
            var code = StripComments(File.ReadAllText(absolute));
            totalCalls += Regex.Matches(code, @"\bSpeakNarrator\s*\(").Count;
        }

        Assert.True(totalCalls >= 3,
            $"Found only {totalCalls} SpeakNarrator call sites (expected at least the 3 real ones "
            + "U-T5-6 fixed in MainUi.cs, plus the method's own definition) — the scanner is not "
            + "reaching real source.");
    }

    /// <summary>
    /// Finds every <c>SpeakNarrator(...)</c> invocation in <paramref name="code"/> and flags the ones
    /// shaped as a bare, discarded statement: nothing but a plain receiver chain (an identifier, an
    /// optional <c>?.</c>/<c>.</c>, no <c>=</c>, no <c>return</c>, no unbalanced open paren from an
    /// enclosing call) immediately before it, and nothing but whitespace between its closing paren
    /// and the next statement terminator. A method DEFINITION (e.g. <c>public string
    /// SpeakNarrator(...)</c>) never matches: its prefix ("public string") does not end in the
    /// required trailing '.', so it is never mistaken for a discarded call.
    /// </summary>
    private static List<string> FindBareDiscards(string relativePath, string code)
    {
        var violations = new List<string>();

        foreach (Match m in Regex.Matches(code, @"\bSpeakNarrator\b"))
        {
            var idStart = m.Index;
            var i = idStart + "SpeakNarrator".Length;
            while (i < code.Length && char.IsWhiteSpace(code[i])) i++;
            if (i >= code.Length || code[i] != '(') continue; // not a call — e.g. a bare identifier mention

            var depth = 0;
            var callEnd = -1;
            for (var j = i; j < code.Length; j++)
            {
                if (code[j] == '(') depth++;
                else if (code[j] == ')')
                {
                    depth--;
                    if (depth == 0) { callEnd = j; break; }
                }
            }
            if (callEnd < 0) continue; // unbalanced — defensively skip rather than false-flag

            // The enclosing statement: back to the nearest ';' / '{' / '}', forward to the next one.
            var k = idStart - 1;
            while (k >= 0 && code[k] != ';' && code[k] != '{' && code[k] != '}') k--;
            var prefix = code[(k + 1)..idStart];

            var suffixEnd = callEnd + 1;
            while (suffixEnd < code.Length && code[suffixEnd] != ';' && code[suffixEnd] != '{' && code[suffixEnd] != '}') suffixEnd++;
            var suffix = code[(callEnd + 1)..suffixEnd];

            var trimmedPrefix = prefix.Trim();
            var isBareReceiverChain =
                Regex.IsMatch(trimmedPrefix, @"^[\w\s.?()]*\.$")
                && trimmedPrefix.Count(c => c == '(') == trimmedPrefix.Count(c => c == ')')
                && !Regex.IsMatch(trimmedPrefix, @"\breturn\b");
            var nothingCapturesTheResultAfter = suffix.Trim().Length == 0;

            if (isBareReceiverChain && nothingCapturesTheResultAfter)
            {
                var lineNumber = code[..idStart].Count(c => c == '\n') + 1;
                violations.Add($"{relativePath}:{lineNumber} — return value discarded");
            }
        }

        return violations;
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
