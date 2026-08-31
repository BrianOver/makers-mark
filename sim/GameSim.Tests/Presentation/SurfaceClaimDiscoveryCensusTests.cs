using System.Reflection;
using System.Text.RegularExpressions;

namespace GameSim.Tests.Presentation;

/// <summary>
/// P2-SCREEN-03 (§11.15)'s discovery census — the same text-scan idiom <see
/// cref="ClientAuthorityCensusTests"/> already uses (a scan of <c>godot/scripts/**/*.cs</c>, no
/// Godot runtime needed) pointed at a different question: does every full-rect surface <c>MainUi</c>
/// hosts, that exposes its own public <c>Show*</c>/<c>Close*</c>/<c>Hide*</c> pair, actually declare
/// a claim with <see cref="GodotClient.Ui.SurfaceArbiter"/>?
///
/// <para><b>Why this census exists.</b> <c>MainUi.OverlaySurfaces()</c> is the eight-row cautionary
/// tale it is built to never repeat: a hand-written array missing exactly one real full-rect modal
/// (<c>ChronicleScroll</c>), so the campaign's ending ceremony runs unowned while the array stays
/// green forever — nothing forces a NEW surface's author to touch that list. Deny by default
/// (P2-KTD3): a surface class shaped like a claimable modal with no matching
/// <c>SurfaceArbiter.Claim</c> call in <c>MainUi.cs</c> fails BY NAME.</para>
///
/// <para><b>THE HONEST FRAMING</b> (same disclaimer as <see cref="ClientAuthorityCensusTests"/>):
/// this is a text scan, not a live scene walk. It proves the SOURCE declares a claim for every
/// surface shaped like a claimable modal and hosted as a public <c>MainUi</c> property; it cannot
/// see a claim wired to the wrong instance, or prove the claim is ever actually reached at runtime.
/// <c>GodotClient.Tests.SurfaceArbiterDiscoveryTests</c> (engine suite, <c>godot/tests/</c>) is the
/// live-mount proof that the discovered set matches at runtime.</para>
/// </summary>
public class SurfaceClaimDiscoveryCensusTests
{
    private static readonly Regex ShowMethod = new(@"public\s+void\s+Show\w*\(", RegexOptions.Compiled);
    private static readonly Regex CloseMethod = new(@"public\s+void\s+(?:Close|Hide)\w*\(", RegexOptions.Compiled);
    private static readonly Regex ClassDecl = new(@"public\s+partial\s+class\s+(\w+)\s*:", RegexOptions.Compiled);

    private static readonly Regex MainUiSurfaceField = new(
        @"public\s+(\w+)\s+(\w+)\s*\{\s*get;\s*private\s+set;\s*\}\s*=\s*null!;",
        RegexOptions.Compiled);

    private static readonly Regex ClaimCall = new(@"SurfaceArbiter\.Claim\(\s*(\w+)\s*,", RegexOptions.Compiled);

    /// <summary>Pinned exceptions ledger — same citation contract as <see
    /// cref="ClientAuthorityCensusTests.Exceptions"/>: a surface shaped like a claimable modal that
    /// is deliberately not claimed yet, with a reason citing the ruling that grants it. Empty: every
    /// surface this census can see is claimed.</summary>
    private static readonly Dictionary<string, string> Exceptions = new();

    private const int ExpectedExceptionCount = 0;

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
            .Select(e => e.Key)
            .ToList();

        Assert.True(uncited.Count == 0,
            "An exception with no ruling behind it is drift wearing a reason:\n  "
            + string.Join("\n  ", uncited));
    }

    /// <summary>Every class name in <paramref name="scriptsRoot"/> whose file declares BOTH a public
    /// <c>Show*</c> method and a public <c>Close*</c>/<c>Hide*</c> method — the shape a claimable
    /// full-rect modal has today (<c>LedgerModal.ShowFor</c>/<c>CloseModal</c>,
    /// <c>ChronicleScroll.ShowFor</c>/<c>CloseScroll</c>, and their six siblings).</summary>
    private static List<string> ClaimableSurfaceClassNames(string scriptsRoot)
    {
        var names = new List<string>();
        foreach (var (_, absolute) in SourceFiles(scriptsRoot))
        {
            var code = File.ReadAllText(absolute);
            if (!ShowMethod.IsMatch(code) || !CloseMethod.IsMatch(code))
            {
                continue;
            }

            var match = ClassDecl.Match(code);
            if (match.Success)
            {
                names.Add(match.Groups[1].Value);
            }
        }

        return names;
    }

    /// <summary>Every <c>MainUi</c> public property whose declared type is one of
    /// <paramref name="claimableTypes"/> — i.e. every field hosting a claimable surface, regardless
    /// of whether it is actually claimed (that is what the real census test below asks).</summary>
    private static List<(string Type, string Field)> MainUiSurfaceFields(
        string mainUiCode, IReadOnlySet<string> claimableTypes)
        => MainUiSurfaceField.Matches(mainUiCode)
            .Select(m => (Type: m.Groups[1].Value, Field: m.Groups[2].Value))
            .Where(t => claimableTypes.Contains(t.Type))
            .ToList();

    private static bool IsClaimed(string mainUiCode, string field)
        => ClaimCall.Matches(mainUiCode).Any(m => m.Groups[1].Value == field);

    [Fact]
    public void EveryClaimableSurface_IsClaimed_UnlessPinnedWithAReason()
    {
        var scriptsRoot = ScriptsRoot();
        var claimableTypes = ClaimableSurfaceClassNames(scriptsRoot).ToHashSet();

        Assert.True(claimableTypes.Count >= 8,
            $"Only {claimableTypes.Count} claimable surface classes found — too few to trust a "
            + $"green run; check the scan, not this floor. Found: [{string.Join(", ", claimableTypes)}]");

        var mainUiPath = Path.Combine(scriptsRoot, "MainUi.cs");
        Assert.True(File.Exists(mainUiPath), $"Expected {mainUiPath} to exist.");
        var mainUiCode = File.ReadAllText(mainUiPath);

        var fields = MainUiSurfaceFields(mainUiCode, claimableTypes);
        Assert.True(fields.Count >= 8,
            $"Only {fields.Count} MainUi surface fields matched a claimable type — too few to "
            + "trust a green run.");

        var violations = fields
            .Where(f => !IsClaimed(mainUiCode, f.Field) && !Exceptions.ContainsKey(f.Field))
            .Select(f => $"{f.Field} ({f.Type}) has a public Show*/Close* pair but no "
                         + $"SurfaceArbiter.Claim({f.Field}, ...) call in MainUi.cs")
            .ToList();

        Assert.True(violations.Count == 0,
            "A surface shaped like a claimable modal is not registered with the arbiter (P2-KTD3, "
            + "deny by default). Fix by adding the Claim call, not by softening this test:\n  "
            + string.Join("\n  ", violations));
    }

    /// <summary>Proof requirement: the census fails on a planted undeclared surface. A fabricated
    /// class with the exact claimable shape (public Show*/Close* pair) and a MainUi-shaped field
    /// declaration, with NO matching <c>SurfaceArbiter.Claim</c> call, must be detected as
    /// unclaimed.</summary>
    [Fact]
    public void PlantedUndeclaredSurface_IsDetectedAsUnclaimed()
    {
        const string fabricatedSurfaceSource = """
            public partial class FabricatedUnclaimedModal : SimPanel
            {
                public void ShowFabricated() { Visible = true; }
                public void CloseFabricated() => Visible = false;
            }
            """;
        const string mainUiWithoutClaim = """
            public FabricatedUnclaimedModal Fabricated { get; private set; } = null!;
            AddChild(Fabricated);
            """;

        Assert.True(ShowMethod.IsMatch(fabricatedSurfaceSource) && CloseMethod.IsMatch(fabricatedSurfaceSource),
            "Fixture setup bug: the fabricated class must match the claimable shape.");
        var classMatch = ClassDecl.Match(fabricatedSurfaceSource);
        Assert.True(classMatch.Success, "Fixture setup bug: class-name regex must match.");

        var claimableTypes = new HashSet<string> { classMatch.Groups[1].Value };
        var fields = MainUiSurfaceFields(mainUiWithoutClaim, claimableTypes);

        Assert.True(fields.Count == 1, $"Fixture setup bug: expected exactly one matched field, got {fields.Count}.");
        Assert.False(IsClaimed(mainUiWithoutClaim, fields[0].Field),
            "The planted fixture must read as UNCLAIMED — this is the negative-path proof the unit "
            + "requires (a real violation must be visible to the detection logic).");
    }

    /// <summary>The positive mirror of the test above, proving the negative result isn't just "the
    /// detector never finds anything": the identical fixture, but with the matching
    /// <c>SurfaceArbiter.Claim</c> call present, reads as claimed.</summary>
    [Fact]
    public void PlantedDeclaredSurface_IsDetectedAsClaimed()
    {
        const string fabricatedSurfaceSource = """
            public partial class FabricatedClaimedModal : SimPanel
            {
                public void ShowFabricated() { Visible = true; }
                public void CloseFabricated() => Visible = false;
            }
            """;
        const string mainUiWithClaim = """
            public FabricatedClaimedModal Fabricated { get; private set; } = null!;
            AddChild(Fabricated);
            SurfaceArbiter.Claim(Fabricated, new SurfaceClaim("Fabricated", SurfaceRegion.FullScreenModal, 1));
            """;

        var classMatch = ClassDecl.Match(fabricatedSurfaceSource);
        var claimableTypes = new HashSet<string> { classMatch.Groups[1].Value };
        var fields = MainUiSurfaceFields(mainUiWithClaim, claimableTypes);

        Assert.True(fields.Count == 1);
        Assert.True(IsClaimed(mainUiWithClaim, fields[0].Field),
            "The planted fixture must read as CLAIMED once the SurfaceArbiter.Claim call is present.");
    }

    private static IEnumerable<(string Relative, string Absolute)> SourceFiles(string root)
        => Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(p => (Path.GetRelativePath(root, p).Replace('\\', '/'), p));

    private static string ScriptsRoot()
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
