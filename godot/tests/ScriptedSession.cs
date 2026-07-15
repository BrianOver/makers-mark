#if GDUNIT_TESTS
using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using GameSim.Drama;

namespace GodotClient.Tests;

/// <summary>
/// The deterministic 3-day session every U11 test drives (verified on seed 2026 and
/// 60 other seeds by probe): day 1 posts an unappealing bounty (guarantees
/// <see cref="BountyJudged"/> reasons), day 2 Evening buys the day-1 copper ore
/// offers and crafts a dagger in the same batch, day 3 Morning shelves the dagger
/// at an unaffordable 9999g (guarantees <see cref="HeroPassedOnItem"/> reasons).
/// Actions are chosen from live state, so the same chooser drives the SimAdapter,
/// the raw kernel, and the UI panels identically.
/// </summary>
public static class ScriptedSession
{
    public const ulong Seed = 2026;
    public const string CraftRecipeId = "dagger";
    public const string CraftMaterial = "copper";
    public const int BountyFloor = 5;
    public const int BountyReward = 10;
    public const int UnaffordablePrice = 9999;

    /// <summary>Copper needed for one dagger craft (RecipeTable: dagger = 2x copper).</summary>
    public const int CopperNeeded = 2;

    /// <summary>The day-1 copper ore offers to buy, greedy in card order until enough copper.</summary>
    public static ImmutableList<OreOffered> CopperBuys(GameState state)
    {
        var buys = ImmutableList.CreateBuilder<OreOffered>();
        var quantity = 0;
        foreach (var offer in LedgerQuery.ReturnCards(state, 1)
                     .SelectMany(card => card.OreOffers)
                     .Where(offer => offer.MaterialKey == CraftMaterial))
        {
            if (quantity >= CopperNeeded)
            {
                break;
            }

            buys.Add(offer);
            quantity += offer.Quantity;
        }

        return buys.ToImmutable();
    }

    /// <summary>The single unshelved player craft (exists on day 3 Morning of the script).</summary>
    public static ItemId CraftedItem(GameState state) =>
        state.Items.Values.Single(item => item.PlayerCrafted).Id;

    /// <summary>The scripted action batch for the tick about to run, from live state.</summary>
    public static ImmutableList<PlayerAction> ChooseActions(GameState state) => (state.Day, state.Phase) switch
    {
        (1, DayPhase.Morning) => [new PostBountyAction(BountyFloor, BountyReward)],
        (2, DayPhase.Evening) => CopperBuys(state)
            .Select(offer => (PlayerAction)new BuyOreAction(offer.From, offer.MaterialKey, offer.Quantity))
            .Append(new CraftAction(CraftRecipeId, CraftMaterial))
            .ToImmutableList(),
        (3, DayPhase.Morning) => [new StockAction(CraftedItem(state), UnaffordablePrice)],
        _ => ImmutableList<PlayerAction>.Empty,
    };
}
#endif
