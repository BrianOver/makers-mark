using Progress;

namespace Progress.Tests;

public class ServesReceiptsTests
{
    private static string BodyWith(string servesLine) => $"""
        ## Summary

        Some PR body text.

        {servesLine}

        More trailing text.
        """;

    [Theory]
    [InlineData("link1")]
    [InlineData("link5")]
    [InlineData("LINK3")]
    public void ClassifiesLink(string value)
    {
        var receipt = ServesReceipts.Parse(BodyWith($"Serves: {value}"));

        Assert.Equal(ServesKind.Link, receipt.Kind);
        Assert.Null(receipt.UnitId);
    }

    [Fact]
    public void ClassifiesSubstrate()
    {
        var receipt = ServesReceipts.Parse(BodyWith("Serves: substrate"));

        Assert.Equal(ServesKind.Substrate, receipt.Kind);
    }

    [Fact]
    public void ClassifiesOverhead()
    {
        var receipt = ServesReceipts.Parse(BodyWith("Serves: overhead — booked"));

        Assert.Equal(ServesKind.Overhead, receipt.Kind);
    }

    [Theory]
    [InlineData("P1")]
    [InlineData("P5(a)")]
    public void ClassifiesOlderCriticalPathPlanItem(string value)
    {
        var receipt = ServesReceipts.Parse(BodyWith($"Serves: {value}"));

        Assert.Equal(ServesKind.PlanItem, receipt.Kind);
        Assert.Null(receipt.UnitId);
    }

    [Theory]
    [InlineData("P2-HONEST-01")]
    [InlineData("P2-PROOF-03")]
    [InlineData("U27")]
    public void ClassifiesTrackedUnitId(string value)
    {
        var receipt = ServesReceipts.Parse(BodyWith($"Serves: {value}"));

        Assert.Equal(ServesKind.Unit, receipt.Kind);
        Assert.Equal(value, receipt.UnitId);
    }

    [Fact]
    public void ADomainOnlyValueIsMalformedNotAUnit()
    {
        // This repo's own drift case: a title names "P2-ONBOARD-07" but the receipt only says
        // "P2-ONBOARD" — missing the unit's own number, so it names no single tracked row.
        var receipt = ServesReceipts.Parse(BodyWith("Serves: P2-ONBOARD"));

        Assert.Equal(ServesKind.Malformed, receipt.Kind);
        Assert.Equal("P2-ONBOARD", receipt.RawValue);
    }

    [Fact]
    public void ACompoundValueIsMalformed()
    {
        var receipt = ServesReceipts.Parse(BodyWith("Serves: P3 / link3"));

        Assert.Equal(ServesKind.Malformed, receipt.Kind);
    }

    [Fact]
    public void NoServesLineAtAllIsMissing()
    {
        var receipt = ServesReceipts.Parse("## Summary\n\nNo receipt here.\n");

        Assert.Equal(ServesKind.Missing, receipt.Kind);
        Assert.Null(receipt.RawValue);
    }

    [Fact]
    public void EmptyBodyIsMissing()
    {
        var receipt = ServesReceipts.Parse(null);

        Assert.Equal(ServesKind.Missing, receipt.Kind);
    }

    [Fact]
    public void IsLenientAboutCaseAndLeadingWhitespaceWhenFindingTheLine()
    {
        var receipt = ServesReceipts.Parse(BodyWith("  serves:   substrate  "));

        Assert.Equal(ServesKind.Substrate, receipt.Kind);
    }
}
