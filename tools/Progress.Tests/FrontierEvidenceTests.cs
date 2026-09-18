using Progress;

namespace Progress.Tests;

/// <summary>
/// A unit row may name its own proof — an <c>`evidence:path[:symbol]`</c> span in its Key files
/// cell — and the frontier checks that proof against the working tree before it offers the unit.
///
/// <para>The failure this closes is one-directional and expensive. Before it, a unit whose
/// deliverable touched only EXISTING files, whose shipping commit carried no per-unit tag (squashed
/// under another subject, or bundled under one <c>Serves:</c> receipt), and whose id never appeared
/// literally in tracked source was invisible to all three of this tool's other checks — the
/// commit-tag index, the file-existence census and the source-tag grep — and came back RUNNABLE.
/// An unattended session then spent a night rebuilding something that had already shipped. A false
/// runnable costs a builder-night; a false refusal costs a look, so every case here is written to
/// prefer refusing.</para>
///
/// <para>The <see cref="FrontierRow.Unverified"/> half matters just as much: a row with no marker is
/// still only an inference, and the frontier has to SAY so. Silence about that gap is what let the
/// original failure survive four repeats.</para>
/// </summary>
public class FrontierEvidenceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "frontier-evidence-" + Guid.NewGuid().ToString("N"));

    public FrontierEvidenceTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private string WriteFile(string relativePath, string contents)
    {
        var full = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, contents);
        return relativePath;
    }

    private static UnitRow Row(string id, IReadOnlyList<EvidenceMarker>? evidence = null) =>
        new(UnitTable.P2, id, $"title for {id}", Array.Empty<FileRef>(), Array.Empty<string>(),
            "", Array.Empty<string>(), 1, null, evidence);

    private static ReconciliationResult Reconcile(params UnitRow[] units) =>
        Reconciler.Reconcile(
            new PlanParseResult(units, Array.Empty<UnparseableRow>(), Array.Empty<DocRef>()),
            new Dictionary<string, LandedUnit>(StringComparer.Ordinal),
            new Dictionary<string, OpenUnit>(StringComparer.Ordinal),
            new HashSet<string>());

    [Fact]
    public void EvidenceThatExists_ReportsShippedAndIsNeverOffered()
    {
        var path = WriteFile(
            "sim/GameSim/Contracts/Events.cs",
            "namespace X;\npublic sealed record BountyRefunded(int Gold);\n");

        var rows = Frontier.Compute(
            Reconcile(Row("P2-MEMORY-15", [new EvidenceMarker(path, "BountyRefunded")])), _root);
        var row = rows.Single();

        Assert.True(row.ShippedByEvidence);
        Assert.Null(row.RefusalReason);
        Assert.Equal("sim/GameSim/Contracts/Events.cs:2", row.EvidenceDetail);
    }

    [Fact]
    public void EvidenceThatExists_RendersAsShippedWithItsCitation()
    {
        var path = WriteFile("sim/GameSim/Contracts/Events.cs", "\npublic record BountyRefunded();\n");

        var text = Frontier.Render(
            Frontier.Compute(
                Reconcile(Row("P2-MEMORY-15", [new EvidenceMarker(path, "BountyRefunded")])), _root));

        Assert.Contains("SHIPPED   P2-MEMORY-15", text);
        Assert.Contains("sim/GameSim/Contracts/Events.cs:2", text);
        Assert.DoesNotContain("RUNNABLE  P2-MEMORY-15", text);
    }

    /// <summary>The file is there and the symbol is not: a real negative answer, so the unit stays
    /// on offer. The check ran, so this row is NOT <see cref="FrontierRow.Unverified"/> — that word
    /// is reserved for rows nobody checked at all.</summary>
    [Fact]
    public void ACommitSubjectThatOnlyNamesTheUnit_DoesNotLandIt_WhenItsEvidenceIsMissing()
    {
        // #894's subject read "... is booked as P2-PROOF-24" and the commit-tag index landed the id.
        var path = WriteFile("godot/scripts/panels/DelveStage.cs", "enum CombatPoseKind { Attack }\n");
        var unit = Row("P2-PROOF-24", [new EvidenceMarker(path, "CombatPoseKind.Kill")]);
        var dependent = new UnitRow(UnitTable.P2, "P2-PROOF-25", "t", Array.Empty<FileRef>(),
            new[] { "P2-PROOF-24" }, "", Array.Empty<string>(), 2, null, null);
        var landed = new Dictionary<string, LandedUnit>(StringComparer.Ordinal)
        {
            ["P2-PROOF-24"] = new("P2-PROOF-24", "665c74b3", 894),
        };
        var result = Reconciler.Reconcile(
            new PlanParseResult(new[] { unit, dependent }, Array.Empty<UnparseableRow>(), Array.Empty<DocRef>()),
            landed, new Dictionary<string, OpenUnit>(StringComparer.Ordinal), new HashSet<string>());

        var rows = Frontier.Compute(result, _root);
        var booked = rows.Single(r => r.UnitId == "P2-PROOF-24");
        Assert.Null(booked.RefusalReason);
        Assert.True(booked.LandedByTagOnly);
        Assert.False(booked.ShippedByEvidence);
        // ...and nothing downstream is unblocked by the booking.
        Assert.Contains("P2-PROOF-24", rows.Single(r => r.UnitId == "P2-PROOF-25").RefusalReason);
        Assert.Contains("booking, not a delivery", Frontier.Render(rows));
    }

    [Fact]
    public void EvidenceWhoseSymbolIsMissing_StaysRunnableAndIsNotCalledUnverified()
    {
        var path = WriteFile("sim/GameSim/Contracts/Events.cs", "public record SomethingElse();\n");

        var row = Frontier.Compute(
            Reconcile(Row("P2-MEMORY-15", [new EvidenceMarker(path, "BountyRefunded")])), _root).Single();

        Assert.False(row.ShippedByEvidence);
        Assert.Null(row.RefusalReason);
        Assert.False(row.Unverified);
    }

    [Fact]
    public void EvidenceWhoseFileDoesNotExist_StaysRunnable()
    {
        var row = Frontier.Compute(
            Reconcile(Row("P2-PEOPLE-05",
                [new EvidenceMarker("sim/GameSim/Contracts/World.cs", "MarkerItem")])), _root).Single();

        Assert.False(row.ShippedByEvidence);
        Assert.Null(row.RefusalReason);
        Assert.False(row.Unverified);
    }

    /// <summary>The whole point of the UNVERIFIED signal: a row with no marker reads RUNNABLE
    /// exactly as it always did, but the render now says out loud that nothing was checked.</summary>
    [Fact]
    public void ARowWithNoMarker_IsRunnableButSaysItWasNeverChecked()
    {
        var rows = Frontier.Compute(Reconcile(Row("P2-MEMORY-15")), _root);
        var row = rows.Single();

        Assert.True(row.Unverified);
        Assert.Null(row.RefusalReason);

        var text = Frontier.Render(rows);
        Assert.Contains("UNVERIFIED", text);
        Assert.Contains("1 runnable (1 UNVERIFIED)", text);
        Assert.Contains("Grep for the deliverable before dispatching", text);
    }

    /// <summary>A bare path with no symbol proves only that the file is there — which is all that
    /// row asked for, and the citation carries no line number because there is no line to cite.</summary>
    [Fact]
    public void ABarePathMarker_IsProvedByExistenceAlone()
    {
        var path = WriteFile("sim/GameSim/Drama/WakeSystem.cs", "// anything at all\n");

        var row = Frontier.Compute(
            Reconcile(Row("P2-PEOPLE-06", [new EvidenceMarker(path, null)])), _root).Single();

        Assert.True(row.ShippedByEvidence);
        Assert.Equal("sim/GameSim/Drama/WakeSystem.cs", row.EvidenceDetail);
    }

    /// <summary>Evidence outranks a refusal reason. A ceremony flag or an unlanded dependency says
    /// "an unattended run must not TAKE this"; evidence says "it is already there". Reporting the
    /// refusal instead would send someone to re-read a gate for work that is done.</summary>
    [Fact]
    public void EvidenceOutranksARefusalReason()
    {
        var path = WriteFile("sim/GameSim/Contracts/World.cs", "public record Memorial(string MarkerItem);\n");

        var row = Frontier.Compute(
            Reconcile(
                new UnitRow(UnitTable.P2, "P2-PEOPLE-05", "wake contracts", Array.Empty<FileRef>(),
                    Array.Empty<string>(), "", ["C"], 1, null,
                    [new EvidenceMarker(path, "MarkerItem")])),
            _root).Single();

        Assert.True(row.ShippedByEvidence);
        Assert.Null(row.RefusalReason);
    }

    /// <summary>The default <c>repoRoot</c> keeps every caller that never populates
    /// <c>UnitRow.Evidence</c> — including this tool's own older tests — working unchanged.</summary>
    [Fact]
    public void ComputeWithoutARepoRoot_StillWorksAndReportsUnverified()
    {
        var row = Frontier.Compute(Reconcile(Row("P2-MEMORY-15"))).Single();

        Assert.True(row.Unverified);
        Assert.Null(row.RefusalReason);
    }
}
