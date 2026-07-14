using GameSim.Contracts;

namespace GameSim.Economy;

/// <summary>
/// Morning restock for the rival vendor (A3, R16-baseline-half): every catalog line
/// not currently on <see cref="GameState.RivalShelf"/> is minted fresh (kernel item-id
/// counter, Mark = null) and shelved at its fixed catalog price.
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
            state = state with
            {
                NextItemId = state.NextItemId + 1,
                Items = state.Items.Add(id.Value, minted),
                RivalShelf = state.RivalShelf.Add(new ShelfEntry(id, line.Price)),
            };
        }

        return state;
    }
}
