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
    /// hero's recorded <see cref="RelationshipBand"/>: pin the price for a Regular-or-better hero
    /// (this smith reads them), take their own offer otherwise. See this type's class doc for why
    /// the pin is guaranteed at the only round this policy ever reaches.</summary>
    private static HaggleResponseAction RespondToOffer(GameState state, CounterState counter, Hero hero, int standingOffer)
    {
        if (RelationshipBands.For(hero.Id, state) < RelationshipBand.Regular)
        {
            // Stranger: no read on this hero yet — take their own number rather than press for
            // more. A plain sale, no mood delta (HaggleResolver.CloseSale, Accept path).
            return new HaggleResponseAction(HaggleResponseKind.Accept);
        }

        // Regular-or-better: pin the price. Mirrors HaggleResolver.ResolveCounter's OWN inputs
        // exactly so the ceiling computed here agrees with the one the resolver actually checks.
        var listPrice = state.Player.Shelf.FirstOrDefault(e => e.Item == counter.Presented)?.Price ?? standingOffer;
        var presentedQuality = state.Items.TryGetValue(counter.Presented!.Value.Value, out var presentedItem)
            ? presentedItem.Quality
            : QualityGrade.Common;
        var trueWillingness = WillingnessModel.TrueWillingness(
            listPrice, hero.Gold, hero.ClassId, counter.InterestPermille, hero.MoodPermille, presentedQuality,
            TraitEffects.PriceSensitivityPermille(hero));
        var (_, ceiling) = WillingnessModel.Band(trueWillingness, counter.Round);

        // Guard the edges ActionLegality.HaggleResponseLegal would reject (a positive price the
        // hero can afford) — a near-zero true willingness or a round past the first (this policy
        // never HoldFirms, so that never actually happens, but the read stays honest either way)
        // falls back to Accept rather than ever risking an illegal Counter.
        return ceiling > 0 && ceiling <= hero.Gold
            ? new HaggleResponseAction(HaggleResponseKind.Counter, ceiling)
            : new HaggleResponseAction(HaggleResponseKind.Accept);
    }
}
