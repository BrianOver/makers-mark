using System.Collections.Immutable;
using GameSim.Advisor;
using GameSim.Classes;
using GameSim.Contracts;
using GameSim.Counter;
using GameSim.Heroes;

namespace GameSim.Harness;

/// <summary>
/// P2-HONEST-30 (docs/design/MAKERS-MARK.md §11.13): the first policy that plays the blacksmith the
/// way §11.13 measured nobody ever had — it crafts and stocks the shelf (<see cref="BaselinePlayer"/>'s
/// own routine, called directly rather than re-derived) AND opens the counter and closes real sales
/// (<see cref="CounterPlayer"/>'s state machine, reusing its exact "present the best role-fit item"
/// opener via <see cref="CounterPlayer.BestRoleFitItem"/>).
///
/// <para><b>Decision 1</b> ("sell the good one or hold it") and <b>decision 2</b> ("price for the
/// sale or the relationship") get their first measured occurrence here: §11.13 found
/// <see cref="CounterPlayer"/> opens 2,000 counter sessions and closes zero sales (it never crafts
/// or stocks, so the shelf it presents from is always empty), and <see cref="BaselinePlayer"/> never
/// opens the counter at all — so <see cref="HaggleResolver.CloseSale"/>'s +60 pin / −80 fleece mood
/// swing had never once fired in this project's history before this policy exists.</para>
///
/// <para><b>Decision 2's rule, deterministic off recorded state, never RNG</b>: once a customer has a
/// standing offer on the table, read the hero's <see cref="RelationshipBand"/>
/// (<see cref="RelationshipBands.For"/>). <see cref="RelationshipBand.Regular"/>-or-better — this
/// smith knows the hero — PINS the price: counters at the round's own ceiling, which
/// <see cref="WillingnessModel.PinWindowPermille"/> guarantees lands inside
/// <see cref="HaggleResolver"/>'s pin window for the ONLY round this policy ever reaches (round 1 —
/// it never HoldFirms, so every close happens on the opening round, where the ceiling permille,
/// 980, always sits inside the pin window's [940,1060] band around true willingness). A
/// <see cref="RelationshipBand.Stranger"/> gets no read — the smith just takes the hero's own
/// offer (Accept), pricing for the relationship over the extra gold. Mirrors
/// <see cref="HaggleResolver"/>'s own true-willingness inputs exactly (list price, gold, class,
/// session interest, mood, presented quality, <see cref="TraitEffects.PriceSensitivityPermille"/>)
/// so the price this policy names always agrees with the resolver that actually closes the sale —
/// never a doomed, would-be-rejected Counter.</para>
///
/// <para><b>P2-HONEST-35 (§11.14, "The harness fleeces"): decision 2's second arm.</b> §11.14
/// measured this policy closing 551 counter sales — 270 pinned, ZERO fleeced — because this method
/// only ever Accepted or pinned at the ceiling, so <see cref="WillingnessModel.FleeceMoodPenalty"/>,
/// the <see cref="GameSim.Flavor.Packs.TavernPack.CounterSaleFleeced"/> gossip line, and
/// <see cref="GameSim.Heroes.NeedsSystem"/>'s boycott bias had never once been reached by any
/// harness since <see cref="HaggleResolver.CloseSale"/>'s fleece branch landed. <see cref="IsFleeceArm"/>
/// now takes roughly half of the Regular-or-better closes above the round's ceiling instead of
/// pinning at it — deterministic off the hero id and the calendar day (no RNG, no clock: both are
/// already-recorded state), so the same campaign always fleeces the same customer on the same day
/// and a re-run is byte-identical. <see cref="BaselinePlayer"/> never reaches this method at all
/// (it never opens the counter), so the idle/golden trace is untouched.</para>
///
/// <para>Same purity contract as every policy in this namespace: a pure function of
/// <see cref="GameState"/>, no IO, no RNG of its own, no wall clock.</para>
/// </summary>
public static class ForgeCounterPlayer
{
    public static ImmutableList<PlayerAction> ActionsFor(GameState state) => state.Phase switch
    {
        DayPhase.Morning => MorningActions(state),
        // Craft/buy loops outside Morning are IDENTICAL to BaselinePlayer's — composed, not copied.
        _ => BaselinePlayer.ActionsFor(state),
    };

    private static ImmutableList<PlayerAction> MorningActions(GameState state)
    {
        var counter = state.Counter;

        if (counter is null)
        {
            // No session yet this morning: run the ordinary blacksmith routine (forge-tier
            // upgrade, talent, commissions, stocking — BaselinePlayer.ActionsFor already switches
            // on DayPhase.Morning) AND open the counter in the SAME tick. OpenCounterAction only
            // reads state.Heroes (CounterHandlers.ApplyOpen), so its place in this list relative to
            // the routine above is order-independent — mirrors ApprenticePlayer's day-2 opening
            // tick, done every morning here instead of once on a fixed calendar day.
            var actions = BaselinePlayer.ActionsFor(state).ToBuilder();
            AddCommissionEarmarks(state, actions);
            var open = new OpenCounterAction();
            if (ActionLegality.IsLegal(state, open, state.Phase))
            {
                actions.Add(open);
            }

            return actions.ToImmutable();
        }

        if (counter.Closed)
        {
            return ImmutableList<PlayerAction>.Empty; // already closing this tick — nothing left to do
        }

        if (counter.Active is not { } activeId
            || !state.Heroes.TryGetValue(activeId.Value, out var hero)
            || !hero.Alive)
        {
            // No customer at the counter (empty queue, PKD6) — this policy's counter job is done
            // for today; the morning routine already ran on the tick that opened the session.
            var close = new CloseCounterAction();
            return ActionLegality.IsLegal(state, close, state.Phase)
                ? ImmutableList.Create<PlayerAction>(close)
                : ImmutableList<PlayerAction>.Empty;
        }

        if (counter.Round > 0 && counter.StandingOfferGold is { } standingOffer && counter.Presented is not null)
        {
            return ImmutableList.Create<PlayerAction>(RespondToOffer(state, counter, hero, standingOffer));
        }

        // Nothing presented yet this round — show the active customer the shelf's best role-fit
        // item (CounterPlayer's own opener, composed rather than copied). An empty shelf means
        // there is nothing left to sell: close instead of stalling the morning.
        var heroClass = ClassRegistry.Require(hero.ClassId);
        var best = CounterPlayer.BestRoleFitItem(state, hero, heroClass);
        if (best is { } chosen)
        {
            var present = new PresentItemAction(chosen);
            return ActionLegality.IsLegal(state, present, state.Phase)
                ? ImmutableList.Create<PlayerAction>(present)
                : ImmutableList<PlayerAction>.Empty;
        }

        var closeEmpty = new CloseCounterAction();
        return ActionLegality.IsLegal(state, closeEmpty, state.Phase)
            ? ImmutableList.Create<PlayerAction>(closeEmpty)
            : ImmutableList<PlayerAction>.Empty;
    }

    /// <summary>Decision 2 ("price for the sale or the relationship"), deterministic off the
    /// hero's recorded <see cref="RelationshipBand"/>: fleece or pin the price for a
    /// Regular-or-better hero (this smith reads them — <see cref="IsFleeceArm"/> picks which),
    /// take their own offer otherwise. See this type's class doc for why the pin is guaranteed at
    /// the only round this policy ever reaches.</summary>
    private static HaggleResponseAction RespondToOffer(GameState state, CounterState counter, Hero hero, int standingOffer)
    {
        if (RelationshipBands.For(hero.Id, state) < RelationshipBand.Regular)
        {
            // Stranger: no read on this hero yet — take their own number rather than press for
            // more. A plain sale, no mood delta (HaggleResolver.CloseSale, Accept path).
            return new HaggleResponseAction(HaggleResponseKind.Accept);
        }

        // Regular-or-better: mirrors HaggleResolver.ResolveCounter's OWN inputs exactly so the
        // ceiling computed here agrees with the one the resolver actually checks.
        var listPrice = state.Player.Shelf.FirstOrDefault(e => e.Item == counter.Presented)?.Price ?? standingOffer;
        var presentedQuality = state.Items.TryGetValue(counter.Presented!.Value.Value, out var presentedItem)
            ? presentedItem.Quality
            : QualityGrade.Common;
        var trueWillingness = WillingnessModel.TrueWillingness(
            listPrice, hero.Gold, hero.ClassId, counter.InterestPermille, hero.MoodPermille, presentedQuality,
            TraitEffects.PriceSensitivityPermille(hero));
        var (_, ceiling) = WillingnessModel.Band(trueWillingness, counter.Round);

        // P2-HONEST-35: this Regular-or-better close's arm — fleece above the ceiling instead of
        // pinning at it. FleecePrice already caps at hero.Gold and the 1.06x markup over true
        // willingness clears the round-1 ceiling (0.98x) with room, so this only ever falls through
        // to the pin arm below on a degenerate near-zero willingness.
        if (IsFleeceArm(hero.Id, state.Day))
        {
            var fleecePrice = FleecePrice(trueWillingness, hero.Gold);
            if (fleecePrice > ceiling && fleecePrice > 0 && fleecePrice <= hero.Gold)
            {
                return new HaggleResponseAction(HaggleResponseKind.Counter, fleecePrice);
            }
        }

        // Guard the edges ActionLegality.HaggleResponseLegal would reject (a positive price the
        // hero can afford) — a near-zero true willingness or a round past the first (this policy
        // never HoldFirms, so that never actually happens, but the read stays honest either way)
        // falls back to Accept rather than ever risking an illegal Counter.
        return ceiling > 0 && ceiling <= hero.Gold
            ? new HaggleResponseAction(HaggleResponseKind.Counter, ceiling)
            : new HaggleResponseAction(HaggleResponseKind.Accept);
    }

    /// <summary>P2-HONEST-35: whether THIS Regular-or-better close takes the fleece arm instead of
    /// the pin arm — a pure function of the hero id and the campaign day (both already-recorded
    /// state, never RNG, never a clock read), so the same campaign always fleeces the same customer
    /// on the same day and two calls on an identical state agree. (hero id + day) divisible by 3
    /// takes the arm — a genuine minority of Regular-or-better closes (this policy still pins most
    /// of the time, matching what a smith who mostly reads people fairly would do), enough to give
    /// the fleece mood/gossip/boycott surfaces their first measured occurrence without erasing the
    /// pin arm this policy already proves (see <see cref="ForgeCounterPlayerTests"/>'s pin-rule
    /// test, hero 1 on day 1, sum 2 — NOT divisible by 3 — specifically so this arm never touches
    /// it, and its own fleece-arm test, hero 2 on day 1, sum 3 — divisible by 3).</summary>
    private static bool IsFleeceArm(HeroId hero, int day) => (hero.Value + day) % 3 == 0;

    /// <summary>P2-HONEST-35: a fleece price — comfortably past the round's ceiling (round 1's
    /// 980 permille of true willingness) so <see cref="HaggleResolver.ResolveCounter"/>'s fleece
    /// branch always fires and reaches a real <see cref="WillingnessModel.FleeceMoodDelta"/>, capped
    /// at what the hero can actually pay so this never risks the illegal-Counter guard above. Reuses
    /// <see cref="WillingnessModel.FleeceMoodScaleWindowPermille"/> as the markup — the same
    /// permille the resolver's own fleece mood math already treats as "clearly off," rather than
    /// inventing a second scale.</summary>
    private static int FleecePrice(int trueWillingness, int heroGold) =>
        Math.Min(heroGold, trueWillingness + (int)((long)trueWillingness * WillingnessModel.FleeceMoodScaleWindowPermille / 1000));

    /// <summary>
    /// P2-PEOPLE-28 ("hold it for Torvald"): decision 1's first measured occurrence. A piece
    /// <see cref="BaselinePlayer"/>'s own stocking loop just proposed to stock this same tick, that
    /// satisfies a hero's commission — accepted already, or accepted by one of THIS tick's own
    /// <see cref="AcceptCommissionAction"/> entries above it in <paramref name="actions"/> — gets
    /// held for that hero instead of going to whoever shops first. <see
    /// cref="CommissionHandlers.Satisfies"/> is the exact match rule the commission channel checks
    /// at delivery time (<see cref="CommissionHandlers.TryFulfillFromShelf"/>), asked here rather
    /// than re-derived, so an earmark this policy places can never disagree with what the
    /// commission channel would actually accept. Never asks <see cref="ActionLegality.IsLegal"/>
    /// for the earmark itself: <paramref name="state"/> predates this tick's own StockAction, so
    /// the item is not on the shelf there yet — <see cref="GameKernel.Tick"/> applies actions in
    /// order, so by the time this earmark actually runs, its own StockAction (earlier in the same
    /// list) already put the item there. One earmark per hero (a hero holds at most one open or
    /// accepted commission at a time — see <see cref="CommissionHandlers"/>'s own class doc).
    /// </summary>
    private static void AddCommissionEarmarks(GameState state, ImmutableList<PlayerAction>.Builder actions)
    {
        var acceptingNow = actions.OfType<AcceptCommissionAction>().Select(a => a.Hero).ToHashSet();
        var claimed = new HashSet<HeroId>();
        foreach (var stock in actions.OfType<StockAction>().ToList())
        {
            if (!state.Items.TryGetValue(stock.Item.Value, out var item))
            {
                continue;
            }

            foreach (var commission in state.Commissions)
            {
                if (claimed.Contains(commission.Hero)
                    || (!commission.Accepted && !acceptingNow.Contains(commission.Hero)))
                {
                    continue;
                }

                if (!state.Heroes.TryGetValue(commission.Hero.Value, out var hero) || !hero.Alive
                    || !CommissionHandlers.Satisfies(commission, item, ClassRegistry.Require(hero.ClassId)))
                {
                    continue;
                }

                actions.Add(new EarmarkAction(stock.Item, commission.Hero));
                claimed.Add(commission.Hero);
                break;
            }
        }
    }
}
