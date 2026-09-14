namespace Progress.Tests;

/// <summary>Section 9 of the full report must keep listing every unbuilt unit named in tracked
/// source, but split them: a comment-only hit is a NOTE (informational, does not imply the unit
/// shipped), a unit carrying at least one real code hit is a WARNING — and either way the citation
/// is now `path:line`, not a bare path.</summary>
public class ReportTests
{
    private static ReconciliationResult Result(IReadOnlyList<SourceTaggedUnbuilt> sourceTagged) =>
        new(
            Array.Empty<DomainStatus>(),
            Array.Empty<MissingFileFinding>(),
            Array.Empty<OrderingViolation>(),
            Array.Empty<DocRef>(),
            Array.Empty<IdCollision>(),
            Array.Empty<UnparseableRow>(),
            Array.Empty<ReceiptDispatchTrap>(),
            Array.Empty<MissingOrMalformedReceipt>(),
            Array.Empty<FalseReceipt>(),
            sourceTagged);

    [Fact]
    public void ACommentOnlyUnit_IsListedAsANoteWithPathAndLine()
    {
        var finding = new SourceTaggedUnbuilt(
            "U33",
            new[] { new SourceTagHit("godot/scripts/ui/TutorialFlow.cs", 2924, IsComment: true) });

        var text = Report.Build(Result(new[] { finding }));

        Assert.Contains("NOTE", text);
        Assert.Contains("U33", text);
        Assert.Contains("godot/scripts/ui/TutorialFlow.cs:2924", text);
        Assert.DoesNotContain("WARNING", text);
    }

    [Fact]
    public void AUnitWithACodeHit_IsListedAsAWarningWithPathAndLine()
    {
        var finding = new SourceTaggedUnbuilt(
            "P2-PROOF-04",
            new[] { new SourceTagHit("godot/scripts/panels/TellingPanel.cs", 42, IsComment: false) });

        var text = Report.Build(Result(new[] { finding }));

        Assert.Contains("WARNING", text);
        Assert.Contains("P2-PROOF-04", text);
        Assert.Contains("godot/scripts/panels/TellingPanel.cs:42", text);
    }

    [Fact]
    public void MixedFindings_SortEachUnitIntoExactlyOneSection()
    {
        var commentOnly = new SourceTaggedUnbuilt(
            "P2-MEMORY-11",
            new[] { new SourceTagHit("godot/scripts/panels/LegendsWall.cs", 311, IsComment: true) });
        var codeHit = new SourceTaggedUnbuilt(
            "P2-PROOF-04",
            new[] { new SourceTagHit("godot/scripts/panels/TellingPanel.cs", 42, IsComment: false) });

        var text = Report.Build(Result(new[] { commentOnly, codeHit }));

        Assert.Contains("NOTE", text);
        Assert.Contains("WARNING", text);
        Assert.Contains("P2-MEMORY-11", text);
        Assert.Contains("P2-PROOF-04", text);
    }

    [Fact]
    public void NoFindings_SaysNone()
    {
        var text = Report.Build(Result(Array.Empty<SourceTaggedUnbuilt>()));

        Assert.Contains("## 9.", text);
        Assert.Contains("None.", text);
    }
}
