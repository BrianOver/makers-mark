using GameSim.Contracts;
using GameSim.Kernel;

namespace GameSim.Economy;

/// <summary>
/// Morning restock for the rival vendor (A3, R16-baseline-half): every catalog line
/// not currently on <see cref="GameState.RivalShelf"/> is minted fresh (kernel item-id
/// counter, Mark = null) and shelved at its fixed catalog price, discounted by the
/// rival's current market-share edge (Game-Feel Plan G3, see <see cref="DiscountedPrice"/>) —
/// the visible economic consequence of an idle day feeding forward into the NEXT morning's
/// newly-minted stock (already-shelved lines are untouched; only fresh mints re-price).
///
/// COMPOSITION ORDER (contract): register this system BEFORE
/// <c>GameSim.Heroes.HeroShoppingSystem</c>. The kernel runs Morning systems in
/// registration order, and heroes must browse a fully stocked rival shelf — flipping
/// the order gives day-1 heroes an empty rival shop and changes every outcome.
/// HeroShopChoiceTests pins this order through the real kernel.
///
/// Determinism: the catalog is fixed data and minting happens in catalog declaration
/// order, so this system draws NO RNG — same state in, same state out, forever.
/// Restocking emits no events: shelving stock is not a sale (only purchases emit
/// <see cref="ItemSold"/>).
/// </summary>
public sealed class RivalRestockSystem : IPhaseSystem
{
    /// <summary>Cap on the rival's price discount at full (1000‰) market share (Game-Feel Plan
    /// G3): the rival never undercuts by more than 40% no matter how idle the player has been —
    /// a bounded competitive edge, not a race-to-zero.</summary>
    public const int MaxDiscountPermille = 400;

    public DayPhase Phase => DayPhase.Morning;

    public string Name => "rival-restock";

    public GameState Process(GameState state, IDeterministicRng rng, IEventSink events)
    {
        // Recipe ids currently represented on the rival shelf. HashSet is membership
        // only — never iterated — so it cannot introduce order dependence.
        var stocked = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in state.RivalShelf)
        {
            if (state.Items.TryGetValue(entry.Item.Value, out var item))
            {
                stocked.Add(item.RecipeId);
            }
        }

        foreach (var line in RivalCatalog.Entries)
        {
            if (stocked.Contains(line.RecipeId))
            {
                continue; // still on the shelf from a previous morning
            }

            var id = new ItemId(state.NextItemId);
            var minted = RivalCatalog.Mint(id, line);
            var price = DiscountedPrice(line.Price, state.RivalMarketSharePermille);
            state = state with
            {
                NextItemId = state.NextItemId + 1,
                Items = state.Items.Add(id.Value, minted),
                RivalShelf = state.RivalShelf.Add(new ShelfEntry(id, price)),
            };
        }

        return state;
    }

    /// <summary>
    /// Linearly scales <paramref name="basePrice"/> down by the rival's market-share edge
    /// (0-1000‰), capped at <see cref="MaxDiscountPermille"/> at the 1000‰ ceiling — pure
    /// integer round-to-nearest (<see cref="IntegerCurves.MulDiv"/>), floored at 1 gold so a
    /// rival line is never free. At the default/pre-G3 <c>RivalMarketSharePermille == 0</c> this
    /// is the identity function — byte-identical to every existing fixed-price expectation.
    /// </summary>
    private static int DiscountedPrice(int basePrice, int marketSharePermille)
    {
        var discountPermille = (int)IntegerCurves.MulDiv(marketSharePermille, MaxDiscountPermille, 1000);
        var discounted = basePrice - (int)IntegerCurves.MulDiv(basePrice, discountPermille, 1000);
        return Math.Max(1, discounted);
    }
}
