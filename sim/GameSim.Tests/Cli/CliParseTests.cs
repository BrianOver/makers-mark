using GameSim.Cli;

namespace GameSim.Tests.Cli;

/// <summary>
/// Playtest 2026-07-20 finding N2 (P1): when an id-typed argument didn't resolve, the CLI
/// reported a generic arg-COUNT usage error ("expected 'stock &lt;itemId&gt; &lt;price&gt;'") even though
/// the count was correct — the real problem (the token wasn't an item id) was never named.
/// These pin the id/number classifiers that let each verb say what actually went wrong.
/// </summary>
public class CliParseTests
{
    [Fact]
    public void TryItemId_OnNonIdToken_FailsWithAMessageNamingTheTokenAndItemId()
    {
        // 'dagger' is a recipe name, not an I# — the arg count was right, so a count-usage error
        // (the old behavior) is exactly the misleading message N2 flags.
        Assert.False(CliParse.TryItemId("dagger", out _, out var error));
        Assert.NotNull(error);
        Assert.Contains("dagger", error, StringComparison.Ordinal);
        Assert.Contains("item id", error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expected", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryItemId_OnValidId_ParsesBothBareAndPrefixedForms()
    {
        Assert.True(CliParse.TryItemId("I5", out var prefixed, out var e1));
        Assert.Equal(5, prefixed);
        Assert.Null(e1);

        Assert.True(CliParse.TryItemId("5", out var bare, out var e2));
        Assert.Equal(5, bare);
        Assert.Null(e2);
    }

    [Fact]
    public void TryHeroId_OnNonIdToken_FailsWithAMessageNamingTheTokenAndHeroId()
    {
        Assert.False(CliParse.TryHeroId("gorm", out _, out var error));
        Assert.NotNull(error);
        Assert.Contains("gorm", error, StringComparison.Ordinal);
        Assert.Contains("hero id", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryInt_OnNonNumber_FailsWithAMessageNamingTheTokenAndNumber()
    {
        Assert.False(CliParse.TryInt("cheap", out _, out var error));
        Assert.NotNull(error);
        Assert.Contains("cheap", error, StringComparison.Ordinal);
        Assert.Contains("number", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryInt_OnNumber_Parses()
    {
        Assert.True(CliParse.TryInt("20", out var value, out var error));
        Assert.Equal(20, value);
        Assert.Null(error);
    }
}
