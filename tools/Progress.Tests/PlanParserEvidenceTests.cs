using Progress;

namespace Progress.Tests;

/// <summary>
/// The <c>`evidence:path[:symbol]`</c> span rides in the Key files cell rather than in a new column,
/// because both unit tables already have that cell and already use backtick spans for paths. These
/// cases pin the one thing that makes that safe: an evidence span is never also counted as an
/// ordinary file reference, and a row without one parses exactly as it did before the span existed.
/// </summary>
public class PlanParserEvidenceTests
{
    private const string Header =
        "| Unit | What it is | Key files | Depends on | Flags |\n| --- | --- | --- | --- | --- |\n";

    private static UnitRow ParseOne(string row) =>
        PlanParser.Parse("## 11. Plan\n\n" + Header + row + "\n").Units.Single();

    [Fact]
    public void AnEvidenceSpan_ParsesIntoTheEvidenceListWithItsSymbol()
    {
        var unit = ParseOne(
            "| P2-MEMORY-15 | the refund gets an event | `sim/GameSim/Bounties/BountySystems.cs` "
            + "`evidence:sim/GameSim/Contracts/Events.cs:BountyRefunded` | — | [S] |");

        var marker = Assert.Single(unit.Evidence);
        Assert.Equal("sim/GameSim/Contracts/Events.cs", marker.Path);
        Assert.Equal("BountyRefunded", marker.Symbol);
    }

    /// <summary>An evidence span looks exactly like a path, so the risk is that it ALSO lands in
    /// Files and the missing-file census then reports it as an unwritten deliverable.</summary>
    [Fact]
    public void AnEvidenceSpan_IsNotAlsoCountedAsAFileReference()
    {
        var unit = ParseOne(
            "| P2-MEMORY-15 | the refund gets an event | `sim/GameSim/Bounties/BountySystems.cs` "
            + "`evidence:sim/GameSim/Contracts/Events.cs:BountyRefunded` | — | [S] |");

        Assert.Equal(["sim/GameSim/Bounties/BountySystems.cs"], unit.Files.Select(f => f.Path));
    }

    [Fact]
    public void ABareEvidenceSpan_HasNoSymbol()
    {
        var unit = ParseOne(
            "| P2-PEOPLE-06 | the fallen page | `evidence:sim/GameSim/Drama/WakeSystem.cs` | — | [S] |");

        var marker = Assert.Single(unit.Evidence);
        Assert.Equal("sim/GameSim/Drama/WakeSystem.cs", marker.Path);
        Assert.Null(marker.Symbol);
    }

    [Fact]
    public void ARowWithNoEvidenceSpan_ParsesExactlyAsBefore()
    {
        var unit = ParseOne(
            "| P2-MEMORY-15 | the refund gets an event | `sim/GameSim/Bounties/BountySystems.cs` | — | [S] |");

        Assert.Empty(unit.Evidence);
        Assert.Equal(["sim/GameSim/Bounties/BountySystems.cs"], unit.Files.Select(f => f.Path));
    }
}
