using GameSim.Cli;

namespace GameSim.Tests.Cli;

/// <summary>
/// Every listing prints ids as "H3"/"I12" (playtest finding #1, P0): id-taking commands must
/// accept that exact displayed form, case-insensitively, as well as the bare number.
/// </summary>
public class CliIdsTests
{
    [Theory]
    [InlineData("3", 3)]
    [InlineData("H3", 3)]
    [InlineData("h3", 3)]
    [InlineData("H12", 12)]
    public void TryParseHero_AcceptsBareAndPrefixedForms(string token, int expected)
    {
        Assert.True(CliIds.TryParseHero(token, out var id));
        Assert.Equal(expected, id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("H")]
    [InlineData("Hx")]
    [InlineData("I3")] // wrong prefix for a hero id
    [InlineData("abc")]
    public void TryParseHero_RejectsGarbage(string token)
    {
        Assert.False(CliIds.TryParseHero(token, out _));
    }

    [Theory]
    [InlineData("12", 12)]
    [InlineData("I12", 12)]
    [InlineData("i12", 12)]
    public void TryParseItem_AcceptsBareAndPrefixedForms(string token, int expected)
    {
        Assert.True(CliIds.TryParseItem(token, out var id));
        Assert.Equal(expected, id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("I")]
    [InlineData("Ix")]
    [InlineData("H12")] // wrong prefix for an item id
    public void TryParseItem_RejectsGarbage(string token)
    {
        Assert.False(CliIds.TryParseItem(token, out _));
    }
}
