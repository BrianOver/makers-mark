namespace Progress.Tests;

/// <summary>
/// <see cref="GitShell.IsCommentHit"/> is the whole fix: it decides whether a unit id's source hit
/// refuses dispatch (CODE) or is merely a note (COMMENT). Pure string logic over a line and a
/// match index — no git IO, so every marker family gets its own case rather than one hand-picked
/// example standing in for the whole family (the repo has been bitten by exactly that shape
/// before: a hand-listed fixture array that silently stops covering new members).
/// </summary>
public class GitShellCommentClassificationTests
{
    private const string Id = "U33";

    [Theory]
    [InlineData("    // U33 gives her a graduation line; this unit ships the mechanism", "line comment //")]
    [InlineData("    /// U33's own test scenario is explicit", "doc comment /// (superset of //)")]
    [InlineData("    # U33 is a later unit's own remaining scope", "hash comment (ps1/py/yml)")]
    [InlineData("    <!-- U33: not yet wired -->", "html/markdown comment")]
    [InlineData("     * U33 gives her a graduation line, this unit ships the mechanism", "block-comment continuation, leading *")]
    [InlineData("    /* U33 is a later unit's own remaining scope", "block-comment open, leading /*")]
    public void ACommentMarkerBeforeTheId_ClassifiesAsComment(string line, string because)
    {
        var idIndex = line.IndexOf(Id, StringComparison.Ordinal);

        Assert.True(GitShell.IsCommentHit(line, idIndex), because);
    }

    [Theory]
    [InlineData("        pending.Add(ActVoiceKind.U33);")]
    [InlineData("public string? ConsumeU33Beat(GameState state)")]
    [InlineData("U33")]
    public void NoCommentMarkerBeforeTheId_ClassifiesAsCode(string line)
    {
        var idIndex = line.IndexOf(Id, StringComparison.Ordinal);

        Assert.False(GitShell.IsCommentHit(line, idIndex));
    }

    [Fact]
    public void ACommentMarkerAfterTheId_StillClassifiesAsCode()
    {
        // The marker has to precede the id on the line -- real code followed by an unrelated
        // trailing comment must not be downgraded to a note just because "//" appears somewhere
        // later in the same line.
        var line = "        FireU33Event(); // unrelated trailing remark";
        var idIndex = line.IndexOf(Id, StringComparison.Ordinal);

        Assert.False(GitShell.IsCommentHit(line, idIndex));
    }

}
