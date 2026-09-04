using Progress;

namespace Progress.Tests;

public class ReconcilerTests
{
    private static UnitRow Row(
        UnitTable table,
        string id,
        string title = "title",
        IReadOnlyList<FileRef>? files = null,
        IReadOnlyList<string>? deps = null,
        int line = 1) =>
        new(table, id, title, files ?? Array.Empty<FileRef>(), deps ?? Array.Empty<string>(), "", Array.Empty<string>(), line);

    private static PlanParseResult Plan(IReadOnlyList<UnitRow> units, IReadOnlyList<DocRef>? docRefs = null) =>
        new(units, Array.Empty<UnparseableRow>(), docRefs ?? Array.Empty<DocRef>());

    [Fact]
    public void FlagsOrderingViolationWhenLandedUnitDependsOnUnlandedUnit()
    {
        var units = new[]
        {
            Row(UnitTable.P2, "P2-SCREEN-04", deps: new[] { "P2-SCREEN-03" }),
            Row(UnitTable.P2, "P2-SCREEN-03"),
        };
        var landed = new Dictionary<string, LandedUnit>
        {
            ["P2-SCREEN-04"] = new("P2-SCREEN-04", "abc123def", 100),
            // P2-SCREEN-03 shipped early out of order in this fixture — it never landed.
        };

        var result = Reconciler.Reconcile(Plan(units), landed, new Dictionary<string, OpenUnit>(), new HashSet<string>());

        var violation = Assert.Single(result.OrderingViolations);
        Assert.Equal("P2-SCREEN-04", violation.UnitId);
        Assert.Equal("P2-SCREEN-03", violation.DepId);
        Assert.Equal(100, violation.UnitPr);
    }

    [Fact]
    public void NoOrderingViolationWhenDependencyIsAlsoLanded()
    {
        var units = new[]
        {
            Row(UnitTable.P2, "P2-SCREEN-04", deps: new[] { "P2-SCREEN-03" }),
            Row(UnitTable.P2, "P2-SCREEN-03"),
        };
        var landed = new Dictionary<string, LandedUnit>
        {
            ["P2-SCREEN-04"] = new("P2-SCREEN-04", "abc123def", 100),
            ["P2-SCREEN-03"] = new("P2-SCREEN-03", "aaa111bbb", 99),
        };

        var result = Reconciler.Reconcile(Plan(units), landed, new Dictionary<string, OpenUnit>(), new HashSet<string>());

        Assert.Empty(result.OrderingViolations);
    }

    [Fact]
    public void NoOrderingViolationWhenDependencyIsNotATrackedUnit()
    {
        // "P4" is a critical-path item, not a row in the unit index — not this tool's business.
        var units = new[] { Row(UnitTable.P2, "P2-LONG-02", deps: new[] { "P4" }) };
        var landed = new Dictionary<string, LandedUnit> { ["P2-LONG-02"] = new("P2-LONG-02", "sha", 1) };

        var result = Reconciler.Reconcile(Plan(units), landed, new Dictionary<string, OpenUnit>(), new HashSet<string>());

        Assert.Empty(result.OrderingViolations);
    }

    [Fact]
    public void FlagsMissingFileWhenPathIsNotMarkedNewAndNotTracked()
    {
        var units = new[]
        {
            Row(UnitTable.T10, "U41", files: new[] { new FileRef("sim/GameSim/Kernel/PhaseClock.cs", IsNew: false) }, line: 5133),
        };

        var result = Reconciler.Reconcile(Plan(units), new Dictionary<string, LandedUnit>(), new Dictionary<string, OpenUnit>(),
            new HashSet<string> { "godot/scripts/PhaseClock.cs" }); // the real file lives elsewhere

        var finding = Assert.Single(result.MissingFiles);
        Assert.Equal("U41", finding.UnitId);
        Assert.Equal("sim/GameSim/Kernel/PhaseClock.cs", finding.Path);
        Assert.Equal(5133, finding.LineNumber);
    }

    [Fact]
    public void DoesNotFlagFileMarkedNewEvenWhenAbsent()
    {
        var units = new[]
        {
            Row(UnitTable.P2, "P2-SCREEN-03", files: new[] { new FileRef("godot/scripts/ui/SurfaceArbiter.cs", IsNew: true) }),
        };

        var result = Reconciler.Reconcile(Plan(units), new Dictionary<string, LandedUnit>(), new Dictionary<string, OpenUnit>(),
            new HashSet<string>());

        Assert.Empty(result.MissingFiles);
    }

    [Fact]
    public void DoesNotFlagAFileSomeOtherUnitInThePlanHasAlreadyDeclaredNew()
    {
        // P2-PROOF-03 creates TellingPanel.cs ("new `...`"); P2-PROOF-04 also touches it, without
        // its own "new" marker, because the doc's convention only tags the file's origin unit.
        var units = new[]
        {
            Row(UnitTable.P2, "P2-PROOF-03", files: new[] { new FileRef("godot/scripts/panels/TellingPanel.cs", IsNew: true) }),
            Row(UnitTable.P2, "P2-PROOF-04", files: new[] { new FileRef("godot/scripts/panels/TellingPanel.cs", IsNew: false) }),
        };

        var result = Reconciler.Reconcile(Plan(units), new Dictionary<string, LandedUnit>(), new Dictionary<string, OpenUnit>(),
            new HashSet<string>());

        Assert.Empty(result.MissingFiles);
    }

    [Fact]
    public void FlagsAPathNobodyInThePlanClaimsToCreate()
    {
        var units = new[]
        {
            Row(UnitTable.P2, "P2-PROOF-02", files: new[] { new FileRef("sim/GameSim/Expedition/TellingQuery.cs", IsNew: false) }),
        };

        var result = Reconciler.Reconcile(Plan(units), new Dictionary<string, LandedUnit>(), new Dictionary<string, OpenUnit>(),
            new HashSet<string>());

        Assert.Single(result.MissingFiles);
    }

    [Fact]
    public void DirectoryShapedPathExistsWhenAnyTrackedFileLivesUnderIt()
    {
        var units = new[]
        {
            Row(UnitTable.T10, "U18", files: new[] { new FileRef("godot/scripts/tools/", IsNew: false) }),
        };

        var result = Reconciler.Reconcile(Plan(units), new Dictionary<string, LandedUnit>(), new Dictionary<string, OpenUnit>(),
            new HashSet<string> { "godot/scripts/tools/FullPlaytest.cs" });

        Assert.Empty(result.MissingFiles);
    }

    [Fact]
    public void FlagsIdCollisionWhenSameIdAppearsTwice()
    {
        var units = new[]
        {
            Row(UnitTable.P2, "P2-SCREEN-01", line: 10),
            Row(UnitTable.P2, "P2-SCREEN-01", line: 400), // a copy-paste, or four generations colliding
        };

        var result = Reconciler.Reconcile(Plan(units), new Dictionary<string, LandedUnit>(), new Dictionary<string, OpenUnit>(),
            new HashSet<string>());

        var collision = Assert.Single(result.Collisions);
        Assert.Equal("P2-SCREEN-01", collision.Id);
        Assert.Equal(new[] { 10, 400 }, collision.LineNumbers);
    }

    [Fact]
    public void FlagsDanglingDocReferenceNotOnOriginMain()
    {
        var docRefs = new[]
        {
            new DocRef("docs/design/ASSETS.md", 5),
            new DocRef("docs/plans/dead-plan.md", 12),
        };

        var result = Reconciler.Reconcile(Plan(Array.Empty<UnitRow>(), docRefs), new Dictionary<string, LandedUnit>(),
            new Dictionary<string, OpenUnit>(), new HashSet<string> { "docs/design/ASSETS.md" });

        var dangling = Assert.Single(result.DanglingDocs);
        Assert.Equal("docs/plans/dead-plan.md", dangling.Path);
    }

    [Fact]
    public void ClassifiesUnitsAsLandedOpenOrUnbuilt()
    {
        var units = new[]
        {
            Row(UnitTable.P2, "P2-SCREEN-01"),
            Row(UnitTable.P2, "P2-SCREEN-02"),
            Row(UnitTable.P2, "P2-SCREEN-03"),
        };
        var landed = new Dictionary<string, LandedUnit> { ["P2-SCREEN-01"] = new("P2-SCREEN-01", "sha1", 1) };
        var open = new Dictionary<string, OpenUnit> { ["P2-SCREEN-02"] = new("P2-SCREEN-02", 2, "title") };

        var result = Reconciler.Reconcile(Plan(units), landed, open, new HashSet<string>());

        var domain = Assert.Single(result.Domains);
        Assert.Equal("P2-SCREEN", domain.Domain);
        Assert.Equal(UnitStatus.Landed, domain.Rows.Single(r => r.Unit.Id == "P2-SCREEN-01").Status);
        Assert.Equal(UnitStatus.Open, domain.Rows.Single(r => r.Unit.Id == "P2-SCREEN-02").Status);
        Assert.Equal(UnitStatus.Unbuilt, domain.Rows.Single(r => r.Unit.Id == "P2-SCREEN-03").Status);
    }

    [Fact]
    public void T10UnitsGroupUnderOneDomainRegardlessOfNumber()
    {
        var units = new[] { Row(UnitTable.T10, "U7"), Row(UnitTable.T10, "U44") };

        var result = Reconciler.Reconcile(Plan(units), new Dictionary<string, LandedUnit>(), new Dictionary<string, OpenUnit>(),
            new HashSet<string>());

        var domain = Assert.Single(result.Domains);
        Assert.Equal("T10", domain.Domain);
        Assert.Equal(2, domain.Rows.Count);
    }

    // --- File-existence fallback (P2-PROOF-03's own motivating case: a title/commit subject that
    // drops the unit's trailing number, e.g. "(P2-PROOF)" instead of "(P2-PROOF-03)") ---

    [Fact]
    public void AUnitWithNoCommitTagIsStillLandedWhenItsOwnNewFileExistsOnOriginMain()
    {
        var units = new[]
        {
            Row(UnitTable.P2, "P2-PROOF-03", files: new[] { new FileRef("godot/scripts/panels/TellingPanel.cs", IsNew: true) }),
        };
        var fileOrigins = new Dictionary<string, FileOrigin>
        {
            ["godot/scripts/panels/TellingPanel.cs"] = new("godot/scripts/panels/TellingPanel.cs", "0277dfc0b", 687),
        };

        var result = Reconciler.Reconcile(Plan(units), new Dictionary<string, LandedUnit>(), new Dictionary<string, OpenUnit>(),
            new HashSet<string> { "godot/scripts/panels/TellingPanel.cs" }, fileOrigins: fileOrigins);

        var row = Assert.Single(result.Domains).Rows.Single();
        Assert.Equal(UnitStatus.Landed, row.Status);
        Assert.Equal(LandedEvidence.FileExistence, row.Evidence);
        Assert.Equal("0277dfc0b", row.Sha);
        Assert.Equal(687, row.PrNumber);
    }

    [Fact]
    public void FileEvidenceDoesNotLandAUnitWhenOnlySomeOfItsNewFilesExist()
    {
        var units = new[]
        {
            Row(UnitTable.P2, "P2-PROOF-04", files: new[]
            {
                new FileRef("godot/scripts/panels/TellingPanel.cs", IsNew: true),
                new FileRef("godot/scripts/panels/StillMissing.cs", IsNew: true),
            }),
        };

        var result = Reconciler.Reconcile(Plan(units), new Dictionary<string, LandedUnit>(), new Dictionary<string, OpenUnit>(),
            new HashSet<string> { "godot/scripts/panels/TellingPanel.cs" });

        var row = Assert.Single(result.Domains).Rows.Single();
        Assert.Equal(UnitStatus.Unbuilt, row.Status);
    }

    [Fact]
    public void FileEvidenceNeverAppliesToAUnitWithNoNewFiles()
    {
        // Only modifies an existing file — its existence is a given, not evidence this unit landed.
        var units = new[]
        {
            Row(UnitTable.P2, "P2-PROOF-04", files: new[] { new FileRef("godot/scripts/panels/TellingPanel.cs", IsNew: false) }),
        };

        var result = Reconciler.Reconcile(Plan(units), new Dictionary<string, LandedUnit>(), new Dictionary<string, OpenUnit>(),
            new HashSet<string> { "godot/scripts/panels/TellingPanel.cs" });

        Assert.Equal(UnitStatus.Unbuilt, Assert.Single(result.Domains).Rows.Single().Status);
    }

    [Fact]
    public void ACommitTagMatchIsPreferredOverFileEvidence()
    {
        var units = new[]
        {
            Row(UnitTable.P2, "P2-PROOF-03", files: new[] { new FileRef("godot/scripts/panels/TellingPanel.cs", IsNew: true) }),
        };
        var landed = new Dictionary<string, LandedUnit> { ["P2-PROOF-03"] = new("P2-PROOF-03", "realtag", 999) };
        var fileOrigins = new Dictionary<string, FileOrigin>
        {
            ["godot/scripts/panels/TellingPanel.cs"] = new("godot/scripts/panels/TellingPanel.cs", "wrongsha", 1),
        };

        var result = Reconciler.Reconcile(Plan(units), landed, new Dictionary<string, OpenUnit>(),
            new HashSet<string> { "godot/scripts/panels/TellingPanel.cs" }, fileOrigins: fileOrigins);

        var row = Assert.Single(result.Domains).Rows.Single();
        Assert.Equal(LandedEvidence.CommitTag, row.Evidence);
        Assert.Equal("realtag", row.Sha);
        Assert.Equal(999, row.PrNumber);
    }

    // --- Receipt census (rule 12 / §11.6 rule 3) ---

    private static MergedPrReceipt Receipt(int number, ServesKind kind, string? unitId = null, string? raw = null, string title = "title") =>
        new(number, title, DateTimeOffset.Parse("2026-08-15T00:00:00Z"), new ServesReceipt(kind, raw ?? unitId, unitId));

    [Fact]
    public void FlagsRedundantDispatchTrapWhenReceiptClaimsAUnitStillUnbuilt()
    {
        var units = new[] { Row(UnitTable.P2, "P2-HONEST-01") };
        var receipts = new[] { Receipt(700, ServesKind.Unit, "P2-HONEST-01", title: "fix: the book opens") };

        var result = Reconciler.Reconcile(Plan(units), new Dictionary<string, LandedUnit>(), new Dictionary<string, OpenUnit>(),
            new HashSet<string>(), mergedReceipts: receipts);

        var trap = Assert.Single(result.ReceiptDispatchTraps);
        Assert.Equal("P2-HONEST-01", trap.UnitId);
        Assert.Equal(700, trap.PrNumber);
    }

    [Fact]
    public void NoDispatchTrapWhenTheClaimedUnitIsAlreadyLanded()
    {
        var units = new[] { Row(UnitTable.P2, "P2-HONEST-01") };
        var landed = new Dictionary<string, LandedUnit> { ["P2-HONEST-01"] = new("P2-HONEST-01", "sha", 699) };
        var receipts = new[] { Receipt(699, ServesKind.Unit, "P2-HONEST-01") };

        var result = Reconciler.Reconcile(Plan(units), landed, new Dictionary<string, OpenUnit>(),
            new HashSet<string>(), mergedReceipts: receipts);

        Assert.Empty(result.ReceiptDispatchTraps);
    }

    [Fact]
    public void NoDispatchTrapWhenTheReceiptNamesAUnitThisToolDoesNotTrack()
    {
        var receipts = new[] { Receipt(700, ServesKind.Unit, "P2-GHOST-01") };

        var result = Reconciler.Reconcile(Plan(Array.Empty<UnitRow>()), new Dictionary<string, LandedUnit>(),
            new Dictionary<string, OpenUnit>(), new HashSet<string>(), mergedReceipts: receipts);

        Assert.Empty(result.ReceiptDispatchTraps);
    }

    [Fact]
    public void FlagsMissingReceiptOnAMergedPrWithNoServesLineAtAll()
    {
        var receipts = new[] { Receipt(701, ServesKind.Missing, title: "some PR") };

        var result = Reconciler.Reconcile(Plan(Array.Empty<UnitRow>()), new Dictionary<string, LandedUnit>(),
            new Dictionary<string, OpenUnit>(), new HashSet<string>(), mergedReceipts: receipts);

        var finding = Assert.Single(result.MissingOrMalformedReceipts);
        Assert.Equal(701, finding.PrNumber);
        Assert.Equal(ServesKind.Missing, finding.Kind);
    }

    [Fact]
    public void FlagsMalformedReceiptValue()
    {
        var receipts = new[] { Receipt(702, ServesKind.Malformed, raw: "P2-ONBOARD") };

        var result = Reconciler.Reconcile(Plan(Array.Empty<UnitRow>()), new Dictionary<string, LandedUnit>(),
            new Dictionary<string, OpenUnit>(), new HashSet<string>(), mergedReceipts: receipts);

        var finding = Assert.Single(result.MissingOrMalformedReceipts);
        Assert.Equal("P2-ONBOARD", finding.RawValue);
    }

    [Theory]
    [InlineData(ServesKind.Link)]
    [InlineData(ServesKind.Substrate)]
    [InlineData(ServesKind.Overhead)]
    [InlineData(ServesKind.PlanItem)]
    [InlineData(ServesKind.Unit)]
    public void ALegitimatelyUnitLessOrValidReceiptIsNeverReportedAsMissingOrMalformed(ServesKind kind)
    {
        var units = new[] { Row(UnitTable.P2, "P2-HONEST-01") };
        var landed = new Dictionary<string, LandedUnit> { ["P2-HONEST-01"] = new("P2-HONEST-01", "sha", 1) };
        var receipts = new[] { Receipt(703, kind, kind == ServesKind.Unit ? "P2-HONEST-01" : null, raw: "value") };

        var result = Reconciler.Reconcile(Plan(units), landed, new Dictionary<string, OpenUnit>(),
            new HashSet<string>(), mergedReceipts: receipts);

        Assert.Empty(result.MissingOrMalformedReceipts);
    }

    [Fact]
    public void ReceiptRuleEffectiveDateExcludesPrsMergedBeforeIt()
    {
        var receipts = new[]
        {
            new MergedPrReceipt(600, "old PR", DateTimeOffset.Parse("2026-07-01T00:00:00Z"), new ServesReceipt(ServesKind.Missing, null, null)),
        };

        var result = Reconciler.Reconcile(Plan(Array.Empty<UnitRow>()), new Dictionary<string, LandedUnit>(),
            new Dictionary<string, OpenUnit>(), new HashSet<string>(), mergedReceipts: receipts,
            receiptRuleEffectiveSince: DateTimeOffset.Parse("2026-08-08T00:00:00Z"));

        Assert.Empty(result.MissingOrMalformedReceipts);
    }

    [Fact]
    public void FlagsFalseReceiptWhenClaimedUnitsPathIsNotOnOriginMain()
    {
        var units = new[]
        {
            Row(UnitTable.P2, "P2-PROOF-03", files: new[] { new FileRef("godot/scripts/panels/TellingPanel.cs", IsNew: true) }),
        };
        var receipts = new[] { Receipt(687, ServesKind.Unit, "P2-PROOF-03") };

        var result = Reconciler.Reconcile(Plan(units), new Dictionary<string, LandedUnit>(), new Dictionary<string, OpenUnit>(),
            new HashSet<string>(), mergedReceipts: receipts); // path NOT in trackedFiles

        var finding = Assert.Single(result.FalseReceipts);
        Assert.Equal("P2-PROOF-03", finding.UnitId);
        Assert.Equal(687, finding.PrNumber);
        Assert.Equal("godot/scripts/panels/TellingPanel.cs", finding.Path);
    }

    [Fact]
    public void NoFalseReceiptWhenTheClaimedUnitsFilesAllExist()
    {
        var units = new[]
        {
            Row(UnitTable.P2, "P2-PROOF-03", files: new[] { new FileRef("godot/scripts/panels/TellingPanel.cs", IsNew: true) }),
        };
        var receipts = new[] { Receipt(687, ServesKind.Unit, "P2-PROOF-03") };

        var result = Reconciler.Reconcile(Plan(units), new Dictionary<string, LandedUnit>(), new Dictionary<string, OpenUnit>(),
            new HashSet<string> { "godot/scripts/panels/TellingPanel.cs" }, mergedReceipts: receipts);

        Assert.Empty(result.FalseReceipts);
    }
}
