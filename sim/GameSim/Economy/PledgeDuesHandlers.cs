using GameSim.Advisor;
using GameSim.Contracts;

namespace GameSim.Economy;

/// <summary>
/// P2-LONG-18 (§11.15, "the pledge"): <see cref="PledgeDuesAction"/> — see that record's own doc
/// comment in <c>Contracts/Actions.cs</c> for the trade being made and why it is NOT a seventh
/// decision. This handler is the one place the trade is actually enforced: the piece the player hands
/// over must leave the world for good, so every OTHER handler's very first guard ("does this item
/// still exist") is what keeps a pledged piece from ever being re-shelved, re-sold, or sent to a hero
/// afterwards — no second guard needed anywhere else, and no new field on <see cref="GameState"/> to
/// keep in sync with one.
///
/// <para>Phase legality: EVERY phase, same as <see cref="Crafting.CraftingHandlers"/> and
/// <see cref="Crafting.HeirloomHandlers"/> — the forge never closes and neither does the guild's
/// noticeboard. <see cref="ActionBudget.ConsumesSlot"/> does NOT list this action (so it defaults
/// free, the same fallthrough <see cref="StockAction"/> and <see cref="PresentItemAction"/> rely on):
/// the pledge is decision 1 ("sell the good one or hold it") at a THIRD destination — shelf, counter,
/// or the guild wall — and neither of the other two destinations spends a slot either, because all
/// three are places to put a piece already forged, not fresh work. See <c>ActionBudgetTests</c>'s
/// <c>FreeTypes</c> entry for the pinned ruling.</para>
///
/// Determinism (KTD2): no RNG, no wall clock, no transcendental <c>Math.*</c> — <see cref="Advisor.SuggestedPrice"/>
/// is pure integer arithmetic and <see cref="FindPledgeThisCycle"/> is a plain EventLog scan.
/// </summary>
public sealed class PledgeDuesHandlers : IActionHandler
{
    public bool CanHandle(PlayerAction action, DayPhase phase) => action is PledgeDuesAction;

    public (GameState State, RejectedAction? Rejected) Apply(
        GameState state, PlayerAction action, IDeterministicRng rng, IEventSink events) =>
        action switch
        {
            PledgeDuesAction pledge => ApplyPledge(state, pledge, events),
            _ => (state, new RejectedAction(action, $"PledgeDuesHandlers cannot apply {action.GetType().Name}.")),
        };

    /// <summary>
    /// Check order is fixed (existence, provenance, ownership x3, appraisal, cycle) so rejection
    /// reasons are stable across runs — same discipline <see cref="ShopHandlers.ApplyStock"/> names.
    /// </summary>
    private static (GameState, RejectedAction?) ApplyPledge(GameState state, PledgeDuesAction action, IEventSink events)
    {
        // 1. The item must exist in the world. This is also what refuses a SECOND pledge of the same
        //    piece: guard 8 below removes it from state.Items the instant the first one lands, so a
        //    repeat submission dies right here with the same honest "no such item" every other verb
        //    gives a nonexistent one.
        if (!state.Items.TryGetValue(action.Item.Value, out var item))
        {
            return (state, new RejectedAction(action, $"No such item {action.Item}."));
        }

        // 2. Link 1 is the axiom: only the player's own marked craft may hang on the guild's wall.
        if (!item.PlayerCrafted)
        {
            return (state, new RejectedAction(action, $"{item.Name} ({action.Item}) carries no MakersMark — the guild wall takes only your own craft."));
        }

        // 3. Not equipped. A hero's own gear is that hero's, not the player's own holdings.
        foreach (var hero in state.Heroes.Values)
        {
            if (WoreItem(hero.Gear, action.Item))
            {
                return (state, new RejectedAction(action, $"{item.Name} ({action.Item}) is equipped by {hero.Name} — the guild takes what you hold, not what a hero wears."));
            }
        }

        // 4. Not already sold. Once a hero paid for it the piece already reached them — the whole
        //    point of the pledge is a piece that NEVER does.
        if (state.EventLog.Any(e => e is ItemSold sold && sold.Item == action.Item))
        {
            return (state, new RejectedAction(action, $"{item.Name} ({action.Item}) was already sold — it already reached a hero."));
        }

        // 5. Not already riding in a hero's pack via a vigil delivery — same reason as guard 4, a
        //    different honest channel to the same destination.
        if (state.Heroes.Values.Any(h => h.Pack.Contains(action.Item)))
        {
            return (state, new RejectedAction(action, $"{item.Name} ({action.Item}) is already in a hero's pack — it already reached a hero."));
        }

        // 6. The piece must appraise at or above what it is about to cover. The guild gives no
        //    change (Contracts/Events.cs's DuesPledged doc names this the sting on purpose).
        var appraised = Advisor.SuggestedPrice.For(item);
        var dues = state.Assessment.DuesGold;
        if (appraised < dues)
        {
            return (state, new RejectedAction(action, $"{item.Name} ({action.Item}) appraises at {appraised}g — dues are {dues}g, and the guild takes no partial pieces."));
        }

        // 7. One pledge per cycle. A player who could clear every cycle for free by pledging twice
        //    never faces the trade the pledge exists to name.
        if (FindPledgeThisCycle(state) is not null)
        {
            return (state, new RejectedAction(action, "A piece already covers this cycle's dues — one pledge per assessment."));
        }

        // 8. Apply: the piece leaves the world PERMANENTLY (removed from GameState.Items, not merely
        //    unshelved — see this class's doc comment for why that single removal is the whole
        //    enforcement) and any shelf entry it held goes with it.
        var newState = state with
        {
            Items = state.Items.Remove(action.Item.Value),
            Player = state.Player with
            {
                Shelf = state.Player.Shelf.RemoveAll(e => e.Item == action.Item),
            },
        };

        events.Emit(new DuesPledged(action.Item, item.Name, appraised, dues));
        return (newState, null);
    }

    /// <summary>
    /// The <see cref="DuesPledged"/> event that settled the CURRENT assessment cycle, or null if none
    /// has landed yet — read straight off <see cref="GameState.EventLog"/> rather than a dedicated
    /// state flag, the same durable-fact idiom <see cref="Contracts.DuesPledged"/>'s own doc names.
    /// "Current cycle" means since the most recent <see cref="GuildAssessmentPassed"/>,
    /// <see cref="GuildAssessmentMissed"/> or <see cref="DuesSettledByPledge"/> settlement (day 0, a fresh campaign's own cycle 1, when
    /// neither has fired yet) — the same "yesterday's stamped log" idiom
    /// <see cref="GuildAssessmentSystem"/> already reads for its Confidence deltas, just walking back
    /// to the last SETTLEMENT day instead of a fixed one-day offset. Guard 7 above and
    /// <see cref="Advisor.ActionLegality"/>'s mirror both call this rather than re-deriving it — the
    /// same "handler exposes the formula, the mirror calls it" precedent
    /// <see cref="CampHandlers.SupplyFee"/> and <see cref="ForgeTierHandlers.CurrentTierIndex"/>
    /// already set.
    /// </summary>
    internal static DuesPledged? FindPledgeThisCycle(GameState state)
    {
        var lastSettledDay = 0;
        foreach (var settled in state.EventLog)
        {
            if (settled is GuildAssessmentPassed or GuildAssessmentMissed or DuesSettledByPledge)
            {
                lastSettledDay = Math.Max(lastSettledDay, settled.Day);
            }
        }

        DuesPledged? found = null;
        foreach (var pledged in state.EventLog)
        {
            if (pledged is DuesPledged dues && dues.Day > lastSettledDay)
            {
                found = dues;
            }
        }

        return found;
    }

    /// <summary>Duplicated rather than shared — same standing precedent <see cref="ShopHandlers.WoreItem"/>
    /// and <see cref="Advisor.ActionLegality"/>'s own class doc name (Contracts/ owns no shared Validate
    /// seam).</summary>
    private static bool WoreItem(GearSet gear, ItemId item) =>
        gear.Weapon == item || gear.Shield == item || gear.Armor == item || gear.Trinket == item;
}
