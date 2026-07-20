using System.Collections.Immutable;
using System.Linq;
using GameSim;
using GameSim.Cli;
using GameSim.Contracts;

namespace GameSim.Tests.Cli;

/// <summary>
/// Playtest 2026-07-20 finding N1 (P0): a SUCCESSFUL craft narrated nothing on resolution —
/// the CLI's event renderer had no <see cref="ItemCrafted"/> case, so a legal craft looked
/// identical to a no-op (item silently appeared only if the player thought to run 'items').
/// These pin the renderer through the EXACT composition root the CLI drives, so a real
/// <see cref="ItemCrafted"/> off a real tick must produce a visible, item-naming line.
/// </summary>
public class EventNarrationTests
{
    private const ulong Seed = 7;

    [Fact]
    public void Line_ForSuccessfulCraft_IsVisibleAndNamesTheItem()
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(Seed);

        // Morning: buy the copper a tier-1 dagger needs; tick advances to Expedition.
        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new BuyMaterialAction("copper", 2))).NewState;

        // Craft is legal in all phases — this tick emits ItemCrafted on success.
        var result = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new CraftAction("dagger", "copper")));
        var crafted = result.Events.OfType<ItemCrafted>().Single();

        var line = EventNarration.Line(crafted, result.NewState);

        Assert.NotNull(line);
        Assert.Contains("Dagger", line, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Line_ForUnhandledEvent_IsNull()
    {
        // The renderer stays a pure projection: an event with no player-facing beat returns null
        // (the CLI prints nothing), never a crash or a stray blank line. A rival-shop sale
        // (FromPlayerShop == false) is such an event — only YOUR sales narrate.
        var line = EventNarration.Line(new ItemSold(new ItemId(1), new HeroId(1), 10, FromPlayerShop: false), GameComposition.NewCampaign(Seed));
        Assert.Null(line);
    }
}
