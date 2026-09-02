using Progress;

namespace Progress.Tests;

public class PlanParserTests
{
    [Fact]
    public void ParsesWellFormedP2Row()
    {
        var text = """
            | Unit | Title | Key files | Depends on | Flags |
            |---|---|---|---|---|
            | ⚑ P2-SCREEN-01 | The three dead theme keys | `godot/scripts/ui/GameTheme.cs`, `godot/scripts/MainUi.cs` | — | [G] |
            """;

        var result = PlanParser.Parse(text);

        Assert.Empty(result.Unparseable);
        var unit = Assert.Single(result.Units);
        Assert.Equal(UnitTable.P2, unit.Table);
        Assert.Equal("P2-SCREEN-01", unit.Id);
        Assert.Equal("The three dead theme keys", unit.Title);
        Assert.Equal(2, unit.Files.Count);
        Assert.Equal("godot/scripts/ui/GameTheme.cs", unit.Files[0].Path);
        Assert.False(unit.Files[0].IsNew);
        Assert.Empty(unit.DependsOn);
        Assert.Equal(new[] { "G" }, unit.Flags);
    }

    [Fact]
    public void ParsesWellFormedT10Row()
    {
        var text = """
            | U | Title | Key files | Depends on |
            |---|---|---|---|
            | U8 | Anchors point at containers | `godot/scripts/ui/TutorialFlow.cs`, panels | U7 |
            """;

        var result = PlanParser.Parse(text);

        Assert.Empty(result.Unparseable);
        var unit = Assert.Single(result.Units);
        Assert.Equal(UnitTable.T10, unit.Table);
        Assert.Equal("U8", unit.Id);
        // "panels" has no '/' — not backtick-quoted anyway here but even if it were, bare-symbol
        // rule would exclude it; only the real path is captured.
        var path = Assert.Single(unit.Files);
        Assert.Equal("godot/scripts/ui/TutorialFlow.cs", path.Path);
        Assert.Equal(new[] { "U7" }, unit.DependsOn);
    }

    [Fact]
    public void ReportsUnparseableRowWhenP2IdHasWrongColumnCount()
    {
        // Real P2 rows are 5 columns; this one is missing the Flags column.
        var text = "| P2-SCREEN-99 | A title | `godot/scripts/X.cs` | — |";

        var result = PlanParser.Parse(text);

        Assert.Empty(result.Units);
        var bad = Assert.Single(result.Unparseable);
        Assert.Equal(1, bad.LineNumber);
        Assert.Contains("P2-SCREEN-99", bad.Reason);
        Assert.Contains("expected 5", bad.Reason);
    }

    [Fact]
    public void ReportsUnparseableRowWhenT10IdHasWrongColumnCount()
    {
        // Real T10 rows are 4 columns; this one carries an extra trailing flags-shaped cell.
        var text = "| U99 | A title | `godot/scripts/X.cs` | — | [G] |";

        var result = PlanParser.Parse(text);

        Assert.Empty(result.Units);
        var bad = Assert.Single(result.Unparseable);
        Assert.Contains("U99", bad.Reason);
        Assert.Contains("expected 4", bad.Reason);
    }

    [Fact]
    public void ReportsUnparseableRowWhenTitleIsEmpty()
    {
        var text = "| P2-LONG-01 |  | `sim/GameSim.Cli/` | — | [S] |";

        var result = PlanParser.Parse(text);

        Assert.Empty(result.Units);
        Assert.Single(result.Unparseable);
    }

    [Fact]
    public void SkipsHeaderAndSeparatorRowsCleanly()
    {
        var text = """
            | Unit | Title | Key files | Depends on | Flags |
            |---|---|---|---|---|
            """;

        var result = PlanParser.Parse(text);

        Assert.Empty(result.Units);
        Assert.Empty(result.Unparseable);
    }

    [Fact]
    public void NewPrefixMarksFileAsProspective()
    {
        var text = "| P2-SCREEN-03 | title | new `godot/scripts/ui/SurfaceArbiter.cs`, `godot/scripts/MainUi.cs` | — | [G] |";

        var result = PlanParser.Parse(text);

        var unit = Assert.Single(result.Units);
        Assert.Equal(2, unit.Files.Count);
        Assert.True(unit.Files.Single(f => f.Path == "godot/scripts/ui/SurfaceArbiter.cs").IsNew);
        Assert.False(unit.Files.Single(f => f.Path == "godot/scripts/MainUi.cs").IsNew);
    }

    [Fact]
    public void BareSymbolBacktickSpansAreNotTreatedAsPaths()
    {
        var text = "| U22 | `ThreadHero` | `godot/scripts/ui/TutorialFlow.cs` | — |";

        var result = PlanParser.Parse(text);

        var unit = Assert.Single(result.Units);
        var path = Assert.Single(unit.Files);
        Assert.Equal("godot/scripts/ui/TutorialFlow.cs", path.Path);
    }

    [Fact]
    public void EmDashDependsOnCellYieldsNoDependencies()
    {
        var text = "| P2-SCREEN-01 | title | `godot/scripts/X.cs` | — | [G] |";

        var result = PlanParser.Parse(text);

        Assert.Empty(Assert.Single(result.Units).DependsOn);
    }

    [Fact]
    public void CommaSeparatedDependsOnYieldsMultipleIds()
    {
        var text = "| U33 | title | `godot/scripts/X.cs` | U29, U30, U31, U32 |";

        var result = PlanParser.Parse(text);

        Assert.Equal(new[] { "U29", "U30", "U31", "U32" }, Assert.Single(result.Units).DependsOn);
    }

    [Fact]
    public void RangeShorthandExpandsToEveryIdInRange()
    {
        var text = "| P2-ONBOARD-05 | title | `godot/scripts/X.cs` | P2-ONBOARD-04, P2-SCREEN-02..10 | [G] |";

        var result = PlanParser.Parse(text);

        var deps = Assert.Single(result.Units).DependsOn;
        Assert.Contains("P2-ONBOARD-04", deps);
        for (var n = 2; n <= 10; n++)
        {
            Assert.Contains($"P2-SCREEN-{n:00}", deps);
        }

        Assert.Equal(10, deps.Count); // ONBOARD-04 + 9 range members (02..10)
    }

    [Fact]
    public void SlashComboDependsOnExpandsBothIds()
    {
        // The plan's own shorthand for "depends on both" within one domain, e.g.
        // P2-ONBOARD-05's real "Depends on" cell reads "..., P2-SCREEN-02..10".
        var text = "| P2-SCREEN-07 | title | `godot/scripts/X.cs` | P2-SCREEN-03/04 | [G] |";

        var result = PlanParser.Parse(text);

        Assert.Equal(new[] { "P2-SCREEN-03", "P2-SCREEN-04" }, Assert.Single(result.Units).DependsOn);
    }

    [Fact]
    public void NonUnitDependencyReferencesAreOmittedNotFlaggedAsErrors()
    {
        var text = "| P2-PEOPLE-08 | title | `sim/GameSim/Contracts/Heroes.cs` | §11.5 reopening | [S][C] |";

        var result = PlanParser.Parse(text);

        Assert.Empty(result.Unparseable);
        Assert.Empty(Assert.Single(result.Units).DependsOn);
    }

    [Fact]
    public void FindsDocMdReferencesAnywhereIncludingProse()
    {
        var text = "Some prose citing `docs/design/ASSETS.md` and a dangling one at docs/plans/2026-08-10-003-feat-the-forward-ladder-plan.md here.";

        var result = PlanParser.Parse(text);

        Assert.Equal(2, result.DocRefs.Count);
        Assert.Contains(result.DocRefs, d => d.Path == "docs/design/ASSETS.md");
        Assert.Contains(result.DocRefs, d => d.Path == "docs/plans/2026-08-10-003-feat-the-forward-ladder-plan.md");
    }
}
