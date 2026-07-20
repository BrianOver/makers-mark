using System.Collections.Immutable;
using GameSim.Cli;
using GameSim.Contracts;

namespace GameSim.Tests.Cli;

/// <summary>
/// <see cref="CliActionFormat"/> renders a <see cref="PlayerAction"/> back into the exact verb
/// line the CLI's own parser accepts (U26): the "status"/"advice" surfaces name a REAL advisor/
/// legality action, and the text must be copy-pasteable — the same class of trap the 2026-07-19
/// playtest hit on <c>buyore</c> (finding #3, a printed command that didn't actually work).
/// Every case here round-trips through the SAME parsing helper Program.cs's matching verb uses
/// (<see cref="CliIds"/> for id-taking verbs), so a hint and its own parser can never drift.
/// </summary>
public class CliActionFormatTests
{
    [Fact]
    public void Format_Null_ReturnsNull() =>
        Assert.Null(CliActionFormat.Format(null));

    [Fact]
    public void Format_Craft_MatchesCraftVerbSyntax()
    {
        var line = CliActionFormat.Format(new CraftAction("dagger", "copper"));

        Assert.Equal("craft dagger copper", line);
    }

    [Fact]
    public void Format_Stock_ItemIdParsesBackViaCliIds()
    {
        var line = CliActionFormat.Format(new StockAction(new ItemId(12), 20));

        Assert.Equal("stock I12 20", line);
        var tail = line!.Split(' ')[1];
        Assert.True(CliIds.TryParseItem(tail, out var id));
        Assert.Equal(12, id);
    }

    [Fact]
    public void Format_SetPrice()
    {
        Assert.Equal("price I5 30", CliActionFormat.Format(new SetPriceAction(new ItemId(5), 30)));
    }

    [Fact]
    public void Format_Unstock()
    {
        Assert.Equal("unstock I7", CliActionFormat.Format(new UnstockAction(new ItemId(7))));
    }

    [Fact]
    public void Format_BuyOre_HeroIdParsesBackViaCliIds()
    {
        var line = CliActionFormat.Format(new BuyOreAction(new HeroId(3), "iron", 2));

        Assert.Equal("buyore H3 iron 2", line);
        var tail = line!.Split(' ')[1];
        Assert.True(CliIds.TryParseHero(tail, out var id));
        Assert.Equal(3, id);
    }

    [Fact]
    public void Format_BuyMaterial_MatchesBuymatVerbSyntax()
    {
        Assert.Equal("buymat copper 6", CliActionFormat.Format(new BuyMaterialAction("copper", 6)));
    }

    [Fact]
    public void Format_PostBounty()
    {
        Assert.Equal("bounty 3 20", CliActionFormat.Format(new PostBountyAction(3, 20)));
    }

    [Fact]
    public void Format_UnlockTalent_OmitsProfession_MatchesTalentVerbSyntax()
    {
        // 'talent' takes only a node id — the CLI resolves the owning profession itself
        // (TryResolveTalentProfession), so the hint must not print a second argument.
        Assert.Equal("talent keen-eye", CliActionFormat.Format(new UnlockTalentAction("keen-eye", "blacksmith")));
    }

    [Fact]
    public void Format_SetProfessions_MatchesProfessionVerbSyntax()
    {
        var line = CliActionFormat.Format(new SetProfessionsAction(ImmutableSortedSet.Create("blacksmith", "tanning")));

        Assert.Equal("profession blacksmith tanning", line);
    }

    [Fact]
    public void Format_SendSupply_HeroThenItem()
    {
        Assert.Equal("send H2 I9", CliActionFormat.Format(new SendSupplyAction(new HeroId(2), new ItemId(9))));
    }

    [Fact]
    public void Format_RecallParty()
    {
        Assert.Equal("recall H4", CliActionFormat.Format(new RecallPartyAction(new HeroId(4))));
    }
}
