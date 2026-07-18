using GameSim.Contracts;
using GameSim.Kernel;
using GameSim.Materials;

namespace GameSim.Economy;

/// <summary>
/// The no-softlock economy floor (Playable Core R5/KD3): a Morning system that detects a TRUE
/// dead-end and only then tops the player's gold up to <see cref="DestitutionFloorGold"/>, so a
/// productive action (buy the cheapest priced material → craft) is always reachable. The game is
/// forgiving by design — it must be impossible to dead-end.
///
/// The dead-end test is deliberately a LAST RESORT — all four must hold:
///   1. gold below the cheapest <see cref="MaterialRegistry.PricedPool"/> unit price (cannot buy),
///   2. no materials (cannot craft),
///   3. no stockable player craft — unshelved, unequipped, not in any hero's pack (nothing to stock),
///   4. empty shelf (no pending sale income).
/// A player holding any of those assets gets nothing: the floor is a rescue, not a handout.
///
/// COMPOSITION ORDER: registered in the Morning group AFTER <see cref="Factions.FactionDriftSystem"/>
/// (whose contract is to run FIRST — drift settles standing before anything reads it, KTD5).
/// This system reads no standing and draws NO RNG, so its position among the remaining Morning
/// systems is behaviorally inert; pinning it second keeps the drift contract intact.
///
/// Determinism (R14/KTD2): pure integer, draws NO RNG, no wall clock, no transcendental math —
/// inserting it leaves the kernel stream and every existing seed's world byte-identical, and it
/// never fires on a solvent trace, so golden replay and the balance bands are unchanged
/// (the <see cref="Factions.FactionDriftSystem"/> precedent). The stipend is a tracked gold
/// SOURCE, stamped as <see cref="RecoveryStipendGranted"/> for the conservation invariant.
/// </summary>
public sealed class DestitutionRecoverySystem : IPhaseSystem
{
    /// <summary>Recovery target: enough to buy a few copper (3g base, 4g at vendor markup) and
    /// craft a tier-1 recipe (2 copper). Pinned by NoSoftlockTests.</summary>
    public const int DestitutionFloorGold = 10;

    public DayPhase Phase => DayPhase.Morning;

    public string Name => "destitution-recovery";

    public GameState Process(GameState state, IDeterministicRng rng, IEventSink events)
    {
        var player = state.Player;

        // 1+2. Cannot reach a craft: the cheapest GUARANTEED path back to a productive loop is
        //    "top the best-stocked priced material up to the smallest tier-1 recipe quantity at
        //    the Morning vendor, then craft". Cost it exactly with the vendor's own quote
        //    (MaterialVendorHandlers.QuoteCost — the one pricing formula). Zero cost means a
        //    craft is possible RIGHT NOW; affordable cost means the vendor path is open. Either
        //    way the player is solvent and the floor stays out. Coarser arms (cheapest BASE unit
        //    price / "any material at all") left provable dead-ends: gold 3 cannot pay the 4g
        //    marked-up copper, and a single held copper crafts nothing (min quantity 2) —
        //    NoSoftlockTests pins both. Evening hero offers are contingent, never guaranteed,
        //    so they cannot carry the R5 guarantee.
        var minQuantity = CheapestTier1RecipeQuantity(player);
        var cheapestPathCost = int.MaxValue;
        foreach (var key in MaterialRegistry.PricedPool)
        {
            var held = player.Materials.TryGetValue(key, out var stock) ? stock : 0;
            var needed = Math.Max(0, minQuantity - held);
            var cost = needed == 0 ? 0 : MaterialVendorHandlers.QuoteCost(key, needed);
            cheapestPathCost = Math.Min(cheapestPathCost, cost);
        }

        if (player.Gold >= cheapestPathCost)
        {
            return state; // craftable now (cost 0) or the vendor path is affordable — solvent
        }

        // 3. Nothing to stock: no player craft that is unshelved, unequipped, and not in a pack
        //    (mirrors ShopPanel's stockable definition, plus the pack exclusion — an item on a
        //    hero's back or in its pack is already sold and cannot come back to the shelf).
        var shelved = new HashSet<int>();
        foreach (var entry in player.Shelf)
        {
            shelved.Add(entry.Item.Value);
        }

        var heroHeld = new HashSet<int>();
        foreach (var hero in state.Heroes.Values)
        {
            foreach (var slot in new[] { hero.Gear.Weapon, hero.Gear.Shield, hero.Gear.Armor, hero.Gear.Trinket })
            {
                if (slot is { } id)
                {
                    heroHeld.Add(id.Value);
                }
            }

            foreach (var packed in hero.Pack)
            {
                heroHeld.Add(packed.Value);
            }
        }

        foreach (var item in state.Items.Values)
        {
            if (item.PlayerCrafted && !shelved.Contains(item.Id.Value) && !heroHeld.Contains(item.Id.Value))
            {
                return state; // a stockable craft exists — stock+sell is the way back, no stipend
            }
        }

        // 4. No pending sale income: shelf must be empty (checked via the same set — any shelf
        //    entry means a sale could land).
        if (player.Shelf.Count > 0)
        {
            return state;
        }

        // True dead-end — top up to the floor and stamp the source event. The target is
        // max(floor, cheapest path cost): the guarantee must survive even if recipe/price data
        // ever makes the cheapest craft path cost more than the nominal floor (structural R5,
        // not a tuning accident). Today: floor 10 ≥ 2 copper at 8g.
        var target = Math.Max(DestitutionFloorGold, cheapestPathCost);
        var delta = target - player.Gold;
        if (delta <= 0)
        {
            return state; // defensive: unreachable while target > gold (gold < cheapestPathCost here)
        }

        events.Emit(new RecoveryStipendGranted(delta));
        return state with { Player = player with { Gold = target } };
    }

    /// <summary>
    /// The smallest tier-1 (ungated) recipe material quantity across the player's selected
    /// professions — the least material any craft needs. Tier-1 recipes are talent-free by
    /// design, so this path never depends on unlocks. Defensive fallback 2 (every current
    /// profession's tier-1 quantity) if a save somehow has no selected professions.
    /// </summary>
    private static int CheapestTier1RecipeQuantity(PlayerState player)
    {
        var min = int.MaxValue;
        foreach (var recipe in Professions.ProfessionRegistry.AllRecipes.Values)
        {
            if (recipe.Tier == 1 && player.IsSelected(recipe.Profession))
            {
                min = Math.Min(min, recipe.MaterialQuantity);
            }
        }

        return min == int.MaxValue ? 2 : min;
    }
}
