using System.Text.RegularExpressions;
using FlavorForge.Emit;
using FlavorForge.Generation;

namespace FlavorForge.Tests;

/// <summary>
/// U4: <see cref="PackEmitter"/> against a temp fixture — NEVER the real pack (that would be a
/// live authoring run, which this branch does not perform). Also covers
/// <see cref="ProposalWriter"/>'s default-safe output, since both are the U4 "emit" deliverable.
/// </summary>
public class PackEmitterTests
{
    // A minimal stand-in for a real pack file's shape: same const-name convention
    // (baseKey → PascalCase), same section-header comment, same
    // `[$"{Const}/{voice}"] = ImmutableList.Create(...)` block PackEmitter anchors on.
    private const string FixtureSource = """
        using System.Collections.Immutable;

        namespace GameSim.Flavor.Packs;

        public static class FixturePack
        {
            public const string HeroDied = "heroDied";

            public static readonly FlavorPack Pack = FlavorPack.Create(
                new Dictionary<string, ImmutableList<string>>(StringComparer.Ordinal)
                {
                    // ------------------------------------------------------------- heroDied
                    [$"{HeroDied}/gruff"] = ImmutableList.Create(
                        "Line one for {hero}.",
                        "Line two for {hero}."),
                    [$"{HeroDied}/wry"] = ImmutableList.Create(
                        "Wry line for {hero}."),
                },
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [HeroDied] = "Fallback for {hero}.",
                });
        }
        """;

    [Fact]
    public void Splice_AcceptedLines_AppendIntoTargetKey_OtherKeysByteUnchanged()
    {
        var accepted = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["heroDied/gruff"] = ["Line three for {hero}.", "Line four for {hero}."],
        };

        var updated = PackEmitter.Splice(FixtureSource, accepted);

        Assert.Equal(4, KeyBlock(updated, "HeroDied", "gruff").Count);
        Assert.Contains("\"Line three for {hero}.\"", updated);
        Assert.Contains("\"Line four for {hero}.\"", updated);

        // The untouched key's block is byte-identical to the original fixture.
        Assert.Equal(KeyBlockText(FixtureSource, "HeroDied", "wry"), KeyBlockText(updated, "HeroDied", "wry"));
    }

    [Fact]
    public void Splice_ZeroAcceptedLines_FixtureByteUnchanged()
    {
        var accepted = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["heroDied/gruff"] = Array.Empty<string>(),
        };

        var updated = PackEmitter.Splice(FixtureSource, accepted);

        Assert.Equal(FixtureSource, updated);
    }

    [Fact]
    public void Splice_EmptyAcceptedDictionary_FixtureByteUnchanged()
    {
        var updated = PackEmitter.Splice(FixtureSource, new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal));

        Assert.Equal(FixtureSource, updated);
    }

    [Fact]
    public void Splice_MissingAnchor_ThrowsAndNeverReturns()
    {
        var accepted = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["breakpointClear/gruff"] = ["Some new line for {hero}."], // no such key in the fixture
        };

        Assert.Throws<PackEmitAnchorNotFoundException>(() => PackEmitter.Splice(FixtureSource, accepted));
    }

    [Fact]
    public void SpliceFile_MissingAnchor_WritesNothing_NoPartialCorruption()
    {
        var path = Path.Combine(Path.GetTempPath(), $"flavorforge-fixture-{Guid.NewGuid():N}.cs");
        File.WriteAllText(path, FixtureSource);
        try
        {
            var accepted = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["breakpointClear/gruff"] = ["Some new line for {hero}."],
            };

            Assert.Throws<PackEmitAnchorNotFoundException>(() => PackEmitter.SpliceFile(path, accepted));

            Assert.Equal(FixtureSource, File.ReadAllText(path)); // untouched — no partial write
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SpliceFile_NoAcceptedAnywhere_FileNeverRewritten()
    {
        var path = Path.Combine(Path.GetTempPath(), $"flavorforge-fixture-{Guid.NewGuid():N}.cs");
        File.WriteAllText(path, FixtureSource);
        try
        {
            var before = File.GetLastWriteTimeUtc(path);
            PackEmitter.SpliceFile(path, new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal));

            Assert.Equal(FixtureSource, File.ReadAllText(path));
            Assert.Equal(before, File.GetLastWriteTimeUtc(path)); // never touched, not even a no-op rewrite
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Splice_LineContainingQuote_EscapedCorrectly_AndRoundTripsThroughTheParserAgain()
    {
        // No Roslyn/CodeDom available under the "no new NuGet dependency" rule, so the round-trip
        // proof is: the SAME production parser (FindMatchingClose/SkipStringLiteral, exercised
        // via a second Splice call) must still locate the block correctly after our own escaping
        // — i.e. the escaped quote could not have desynchronized string-literal scanning.
        var trickyLine = "He said \"no\" and meant it, {hero}.";
        var accepted = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["heroDied/gruff"] = [trickyLine],
        };

        var updated = PackEmitter.Splice(FixtureSource, accepted);

        Assert.Contains("\"He said \\\"no\\\" and meant it, {hero}.\"", updated);

        // A second splice targeting the OTHER key must still find its anchor cleanly — proves
        // the escaped quotes did not corrupt the parser's view of the rest of the file.
        var second = PackEmitter.Splice(updated, new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["heroDied/wry"] = ["Another wry line for {hero}."],
        });
        Assert.Contains("\"Another wry line for {hero}.\"", second);

        // Unescaping the inserted literal back out reproduces the original candidate exactly.
        Assert.Equal(trickyLine, Unescape(KeyBlock(updated, "HeroDied", "gruff")[^1]));
    }

    [Fact]
    public void Splice_KeySet_IsUnchangedByEmission()
    {
        var accepted = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["heroDied/gruff"] = ["One more line for {hero}."],
        };

        var updated = PackEmitter.Splice(FixtureSource, accepted);

        Assert.Equal(AnchorKeys(FixtureSource), AnchorKeys(updated));
    }

    // ---------------------------------------------------------------- ProposalWriter (default-safe mode)

    [Fact]
    public void ProposalWriter_WritesAcceptedLines_AndTouchesNoPackFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"flavorforge-proposals-{Guid.NewGuid():N}");
        try
        {
            var results = new[]
            {
                new CellResult("heroDied", "gruff", "heroDied/gruff", ["accepted line one", "accepted line two"], RejectedCount: 2, DuplicateCount: 1),
                new CellResult("heroDied", "wry", "heroDied/wry", Array.Empty<string>(), RejectedCount: 0, DuplicateCount: 0),
            };

            var path = ProposalWriter.Write(dir, "tavern", results);

            var text = File.ReadAllText(path);
            Assert.Contains("accepted line one", text);
            Assert.Contains("accepted line two", text);
            Assert.DoesNotContain("heroDied/wry", text); // zero-accepted cells are omitted, not fabricated
            Assert.Equal(Path.Combine(dir, "tavern.txt"), path);

            // No pack file anywhere near this — the whole point of propose mode.
            Assert.DoesNotContain(Directory.EnumerateFiles(dir), f => f.EndsWith(".cs", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    // ---------------------------------------------------------------- test-local helpers (not production code)

    private static List<string> KeyBlock(string source, string constName, string voice)
    {
        // Strip the anchor prefix (its own key literal, e.g. "{HeroDied}/gruff", would otherwise
        // be picked up by the quote regex as a spurious extra "variant") and the trailing ')'.
        var anchor = $"[$\"{{{constName}}}/{voice}\"] = ImmutableList.Create(";
        var full = KeyBlockText(source, constName, voice);
        var inner = full[anchor.Length..^1];
        var matches = Regex.Matches(inner, "\"((?:[^\"\\\\]|\\\\.)*)\"");
        return [.. matches.Select(m => Unescape(m.Groups[1].Value))];
    }

    private static string KeyBlockText(string source, string constName, string voice)
    {
        var anchor = $"[$\"{{{constName}}}/{voice}\"] = ImmutableList.Create(";
        var start = source.IndexOf(anchor, StringComparison.Ordinal);
        Assert.True(start >= 0, $"anchor not found: {anchor}");
        var openParen = start + anchor.Length - 1;
        var depth = 0;
        var i = openParen;
        while (i < source.Length)
        {
            if (source[i] == '"')
            {
                i++;
                while (i < source.Length && source[i] != '"')
                {
                    i += source[i] == '\\' ? 2 : 1;
                }
            }
            else if (source[i] == '(')
            {
                depth++;
            }
            else if (source[i] == ')')
            {
                depth--;
                if (depth == 0)
                {
                    return source[start..(i + 1)];
                }
            }

            i++;
        }

        throw new InvalidOperationException("unbalanced fixture");
    }

    private static string Unescape(string raw) => raw.Replace("\\\"", "\"").Replace("\\\\", "\\");

    private static HashSet<string> AnchorKeys(string source) =>
        [.. Regex.Matches(source, "\\[\\$\"\\{(\\w+)\\}/(\\w+)\"\\]").Select(m => $"{m.Groups[1].Value}/{m.Groups[2].Value}")];
}
