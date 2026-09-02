using Progress;

namespace Progress.Tests;

public class CommitTagsTests
{
    [Fact]
    public void ExtractsSingleParentheticalIdAndPrNumber()
    {
        var subject = "feat(godot): the arbiter starts deciding who owns the screen (P2-SCREEN-04) (#661)";

        Assert.Equal(new[] { "P2-SCREEN-04" }, CommitTags.ExtractUnitIds(subject));
        Assert.Equal(661, CommitTags.ExtractPrNumber(subject));
    }

    [Fact]
    public void ExtractsSlashComboInsideParens()
    {
        var subject = "P2-SCREEN-07/08: Bryn's three split lessons, gating folded into the instruction (#665)";

        Assert.Equal(new[] { "P2-SCREEN-07", "P2-SCREEN-08" }, CommitTags.ExtractUnitIds(subject));
    }

    [Fact]
    public void ExtractsColonPrefixedSlashComboWithNoParens()
    {
        var subject = "P2-SCREEN-01/02: dead theme keys fixed, rendered baseline captured (#656)";

        Assert.Equal(new[] { "P2-SCREEN-01", "P2-SCREEN-02" }, CommitTags.ExtractUnitIds(subject));
    }

    [Fact]
    public void ExtractsCommaSeparatedT10Ids()
    {
        var subject = "docs(plan): a lesson that merely speaks should not steal the pointer (U46, U47) (#649)";

        Assert.Equal(new[] { "U46", "U47" }, CommitTags.ExtractUnitIds(subject));
    }

    [Fact]
    public void IgnoresNonUnitTextInsideAMixedParenGroup()
    {
        var subject = "feat(godot): the game computed the interact prompt every frame and never showed it (U12, §11.14.14) (#621)";

        Assert.Equal(new[] { "U12" }, CommitTags.ExtractUnitIds(subject));
    }

    [Fact]
    public void CommitWithNoRecognizableTagYieldsNoIds()
    {
        var subject = "docs(plan): the stream-invariance inference is now a measurement, and it says no (#671)";

        Assert.Empty(CommitTags.ExtractUnitIds(subject));
        Assert.Equal(671, CommitTags.ExtractPrNumber(subject));
    }

    [Fact]
    public void SweepCommitNamingOnlyAPhaseNotAUnitYieldsNoIds()
    {
        // "(T10)" alone doesn't match either id shape — a real gap in commit-tag hygiene this
        // tool should surface as an honest absence, not paper over with a guess.
        var subject = "fix(sim): P2-HONEST sweep -- worn-trinket dupe sale, dead-hero commission, stale docs (T10) (#667)";

        Assert.Empty(CommitTags.ExtractUnitIds(subject));
    }

    [Fact]
    public void SubjectWithNoTrailingPrNumberYieldsNullPr()
    {
        Assert.Null(CommitTags.ExtractPrNumber("feat(godot): work in progress, no PR yet"));
    }
}
