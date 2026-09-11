using Progress;

namespace Progress.Tests;

/// <summary>
/// The frontier is what an unattended run reads to choose tonight's work, so every one of these
/// cases is really the same question: can this hand back a unit nobody should have taken?
/// </summary>
public class FrontierTests
{
    private static UnitRow Row(
        string id,
        IReadOnlyList<string>? deps = null,
        IReadOnlyList<string>? unparsedDeps = null,
        IReadOnlyList<string>? flags = null) =>
        new(UnitTable.P2, id, $"title for {id}", Array.Empty<FileRef>(), deps ?? Array.Empty<string>(),
            "", flags ?? Array.Empty<string>(), 1, unparsedDeps);

    private static PlanParseResult Plan(params UnitRow[] units) =>
        new(units, Array.Empty<UnparseableRow>(), Array.Empty<DocRef>());

    private static ReconciliationResult Reconcile(
        PlanParseResult plan, params (string Id, string Sha, int Pr)[] landed)
    {
        var index = landed.ToDictionary(
            l => l.Id, l => new LandedUnit(l.Id, l.Sha, l.Pr), StringComparer.Ordinal);

        return Reconciler.Reconcile(plan, index, new Dictionary<string, OpenUnit>(), new HashSet<string>());
    }

    private static FrontierRow Single(ReconciliationResult result, string id) =>
        Frontier.Compute(result).Single(r => r.UnitId == id);

    [Fact]
    public void AnUnbuiltUnitWithNoDependencies_IsRunnable()
    {
        var result = Reconcile(Plan(Row("P2-MEMORY-05")));

        Assert.Null(Single(result, "P2-MEMORY-05").RefusalReason);
    }

    [Fact]
    public void AnUnlandedDependency_RefusesAndNamesIt()
    {
        var result = Reconcile(Plan(Row("P2-HONEST-06", deps: ["P2-HONEST-05"]), Row("P2-HONEST-05")));

        Assert.Contains("P2-HONEST-05", Single(result, "P2-HONEST-06").RefusalReason);
    }

    [Fact]
    public void ALandedDependency_Clears()
    {
        var result = Reconcile(
            Plan(Row("P2-HONEST-06", deps: ["P2-HONEST-05"]), Row("P2-HONEST-05")),
            ("P2-HONEST-05", "abc123def", 700));

        Assert.Null(Single(result, "P2-HONEST-06").RefusalReason);
    }

    [Fact]
    public void AnOwnerGateTheParserCouldNotResolve_IsNeverRunnable()
    {
        // The finding this whole file exists for, and the one the source system shipped as a
        // `type: HITL` field that gated nothing. P2-LONG-02's real Depends-on cell says "P4" —
        // the owner's own evening — and every id-shaped regex in the parser skips it. Before this,
        // a frontier would have handed back a unit whose gate is a human who has not yet played.
        var result = Reconcile(Plan(Row("P2-LONG-02", unparsedDeps: ["P4"])));

        var refusal = Single(result, "P2-LONG-02").RefusalReason;
        Assert.Contains("P4", refusal);
        Assert.Contains("owner", refusal);
    }

    [Fact]
    public void AnUnresolvedGate_OutranksAnOtherwiseCleanDependencyList()
    {
        // Both conditions present: a satisfied dependency AND an owner gate. The gate must win, or
        // the refusal becomes a race between two correct-looking checks.
        var result = Reconcile(
            Plan(Row("P2-LONG-08", deps: ["P2-MEMORY-13"], unparsedDeps: ["P4"]), Row("P2-MEMORY-13")),
            ("P2-MEMORY-13", "abc123def", 700));

        Assert.Contains("P4", Single(result, "P2-LONG-08").RefusalReason);
    }

    [Theory]
    [InlineData("C")]
    [InlineData("GOLD")]
    [InlineData("BAL")]
    public void ACeremonyFlag_RefusesEvenWithEveryDependencyLanded(string flag)
    {
        // A Contracts micro-PR lands alone by CLAUDE.md's own multi-agent rule; a golden re-record
        // and a balance re-baseline are both the shape where "make the test match the code" and
        // "soften the test" are indistinguishable from inside, which rule 12 forbids outright.
        var result = Reconcile(Plan(Row("P2-MEMORY-15", flags: [flag])));

        Assert.Contains(flag, Single(result, "P2-MEMORY-15").RefusalReason);
    }

    [Fact]
    public void AnOrdinaryFlag_DoesNotRefuse()
    {
        var result = Reconcile(Plan(Row("P2-MEMORY-05", flags: ["G"])));

        Assert.Null(Single(result, "P2-MEMORY-05").RefusalReason);
    }

    [Fact]
    public void AUnitAlreadyNamedInTrackedSource_IsRefusedRatherThanRebuilt()
    {
        // Section 9's warning, promoted to a refusal here: for a human it is "check before you
        // build", but an unattended run has nobody to check, so it must not take the unit at all.
        var plan = Plan(Row("P2-PROOF-04"));
        var sites = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["P2-PROOF-04"] = new[] { "godot/scripts/panels/TellingPanel.cs" },
        };

        var result = Reconciler.Reconcile(
            plan, new Dictionary<string, LandedUnit>(), new Dictionary<string, OpenUnit>(),
            new HashSet<string>(), sourceTagSites: sites);

        Assert.Contains("already written into tracked source", Single(result, "P2-PROOF-04").RefusalReason);
    }

    [Fact]
    public void ALandedUnit_IsNotOnTheFrontierAtAll()
    {
        var result = Reconcile(Plan(Row("P2-HONEST-05")), ("P2-HONEST-05", "abc123def", 700));

        Assert.Empty(Frontier.Compute(result));
    }

    [Fact]
    public void RunnableRowsSortAheadOfRefusedOnes()
    {
        var result = Reconcile(Plan(
            Row("P2-AAA-01", deps: ["P2-ZZZ-99"]),
            Row("P2-ZZZ-01")));

        var rows = Frontier.Compute(result);

        Assert.Equal("P2-ZZZ-01", rows[0].UnitId);
        Assert.Null(rows[0].RefusalReason);
    }

    [Fact]
    public void TheRender_CountsBothHalvesAndQuotesEveryRefusal()
    {
        var result = Reconcile(Plan(Row("P2-ZZZ-01"), Row("P2-LONG-02", unparsedDeps: ["P4"])));

        var text = Frontier.Render(Frontier.Compute(result));

        Assert.Contains("1 runnable, 1 refused", text);
        Assert.Contains("RUNNABLE  P2-ZZZ-01", text);
        Assert.Contains("REFUSED   P2-LONG-02", text);
    }

    [Fact]
    public void ADegradedRead_RefusesTheWholeFrontierRatherThanShrinkingIt()
    {
        // The tool's two tolerant reads -- a failed `git fetch` and a failed `gh pr list` -- both push
        // units the SAME way, toward looking unbuilt. So a degraded frontier is not merely incomplete,
        // it is systematically OVER-full of work that is already done or already in flight, and an
        // unattended run would rebuild it. A warning on stderr is enough for a human reading the
        // report; the frontier's consumer cannot see stderr.
        var result = Reconcile(Plan(Row("P2-MEMORY-05"), Row("P2-MEMORY-06")));

        var text = Frontier.Render(Frontier.Compute(result), new[] { "gh pr list --state open failed" });

        Assert.Contains("Frontier REFUSED", text);
        Assert.Contains("RUNNABLE none", text);
        Assert.Contains("gh pr list --state open failed", text);
        Assert.DoesNotContain("RUNNABLE  P2-MEMORY-05", text);
    }

    [Fact]
    public void NoDegradation_RendersNormally()
    {
        var result = Reconcile(Plan(Row("P2-MEMORY-05")));

        var text = Frontier.Render(Frontier.Compute(result), Array.Empty<string>());

        Assert.DoesNotContain("REFUSED --", text);
        Assert.Contains("RUNNABLE  P2-MEMORY-05", text);
    }

    [Fact]
    public void AnEmptyFrontier_SaysSoRatherThanRenderingNothing()
    {
        // "Everything left is owner-gated" is a legitimate answer and the loop's own stop signal.
        // Rendering an empty section would read as a tool failure and invite a retry.
        var result = Reconcile(Plan(Row("P2-LONG-02", unparsedDeps: ["P4"])));

        Assert.Contains("RUNNABLE none", Frontier.Render(Frontier.Compute(result)));
    }
}
