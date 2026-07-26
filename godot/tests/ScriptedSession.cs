#if GDUNIT_TESTS
using System.Collections.Immutable;
using System.Linq;
using GameSim;
using GameSim.Contracts;
using GameSim.Drama;

namespace GodotClient.Tests;

/// <summary>
/// The deterministic 3-day session every U11 test drives (verified on seed 2026 and
/// 60 other seeds by probe): day 1 posts an unappealing bounty (guarantees
/// <see cref="BountyJudged"/> reasons), day 2 Evening crafts a dagger from the
/// blacksmith's own starter copper, day 3 Morning shelves the dagger at an
/// unaffordable 9999g (guarantees <see cref="HeroPassedOnItem"/> reasons).
/// Actions are chosen from live state, so the same chooser drives the SimAdapter,
/// the raw kernel, and the UI panels identically.
///
/// <para>U-C4 (second venue) note: <c>VenueRouter</c> now spreads early parties
/// across the Mine AND Gloomwood, so the FIRST raid ore returning heroes offer is
/// Gloomwood's <c>greenheart</c>, not the Mine's <c>copper</c> (copper first surfaces
/// ~day 3), and day-1 return cards carry no ore offers at all. The dagger craft is
/// therefore decoupled from raid routing via <see cref="StartState"/> — the recipe's
/// copper is pre-stocked into the blacksmith's own Materials so a craft never depends
/// on which venue a hero raided or which day copper first appears.</para>
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

    /// <summary>
    /// A fresh seed-2026 campaign with exactly the dagger's copper pre-stocked into the
    /// blacksmith's own Materials, so the scripted dagger craft always has its material
    /// regardless of which venue the deterministic <c>VenueRouter</c> sends early
    /// parties to (U-C4 second venue: early raid ore is Gloomwood's greenheart, copper first
    /// surfaces ~day 3). Mirrors <c>RejectionUxTests.CampaignWith</c>'s copper-override pattern;
    /// the purse is untouched (100g). The craft consumes the copper down to 0 (the key is kept),
    /// so the Forge still renders a post-craft "copper x0".
    /// </summary>
    public static GameState StartState()
    {
        var state = GameComposition.NewCampaign(Seed);
        return state with
        {
            Player = state.Player with
            {
                Materials = state.Player.Materials.SetItem(CraftMaterial, CopperNeeded),
            },
        };
    }

    /// <summary>A <see cref="SimAdapter"/> over <see cref="StartState"/> — the mount for every
    /// craft-driving UI test, so the dagger craft never leans on raid-routed ore.</summary>
    public static SimAdapter StartAdapter() => new(StartState());

    /// <summary>
    /// The first early return-card day that actually carries ore offers, paired with those offers.
    /// Day-1 returns carry none now (U-C4: early parties spread to Gloomwood and its returns land
    /// on day 2), so this scans forward from day 1 to the first day with any <c>OreOffers</c> and
    /// returns them (any material — greenheart in the seed-2026 run). Yields <c>(0, empty)</c> when
    /// no day has surfaced offers yet.
    /// </summary>
    public static (int Day, ImmutableList<OreOffered> Offers) EarlyOreOffers(GameState state)
    {
        for (var day = 1; day <= state.Day; day++)
        {
            var offers = LedgerQuery.ReturnCards(state, day)
                .SelectMany(card => card.OreOffers)
                .ToImmutableList();
            if (!offers.IsEmpty)
            {
                return (day, offers);
            }
        }

        return (0, ImmutableList<OreOffered>.Empty);
    }

    /// <summary>The Ledger day the early ore offers live on (see <see cref="EarlyOreOffers"/>).</summary>
    public static int EarlyOreDay(GameState state) => EarlyOreOffers(state).Day;

    /// <summary>
    /// The early ore offers to buy, in card order, greedily accumulated while the running line
    /// cost stays within the purse — so pressing every returned Buy in one Evening batch never
    /// overdraws (and each returned offer's ledger Buy renders Enabled at Evening). Early standing
    /// is neutral, so a raw line cost equals the ledger's tariffed quote (see
    /// <c>LedgerModal.BuyOreLegal</c>). Material-agnostic: it buys whatever the first offering day
    /// surfaces (greenheart in the seed-2026 run), not hardcoded copper.
    /// </summary>
    public static ImmutableList<OreOffered> EarlyOreBuys(GameState state)
    {
        var buys = ImmutableList.CreateBuilder<OreOffered>();
        var spent = 0;
        foreach (var offer in EarlyOreOffers(state).Offers)
        {
            var cost = offer.Quantity * offer.UnitPrice;
            if (spent + cost > state.Player.Gold)
            {
                continue;
            }

            buys.Add(offer);
            spent += cost;
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
        // The dagger crafts from the blacksmith's own starter copper (StartState) — no raid-routed
        // ore purchase, so this batch is legal whatever venue the early parties actually raided.
        (2, DayPhase.Evening) => [new CraftAction(CraftRecipeId, CraftMaterial)],
        (3, DayPhase.Morning) => [new StockAction(CraftedItem(state), UnaffordablePrice)],
        _ => ImmutableList<PlayerAction>.Empty,
    };
}
#endif
