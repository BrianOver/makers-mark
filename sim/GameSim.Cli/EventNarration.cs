using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Narrative;

namespace GameSim.Cli;

/// <summary>
/// Renders a resolved <see cref="GameEvent"/> into the one player-facing beat the interactive CLI
/// prints for it, or <c>null</c> for an event with no beat (a pure projection — no state mutation,
/// no RNG draw; deterministic flavor variation reads the snapshot counter <c>state.Rng.Inc</c>).
///
/// Playtest 2026-07-20 finding N1 (P0): a SUCCESSFUL craft narrated nothing — the switch had no
/// <see cref="ItemCrafted"/> case, so a legal craft looked identical to a silent no-op. The
/// <c>⚒ forged …</c> line closes that: every resolution the player caused now says so out loud.
/// Extracted from Program.cs's former inline <c>Narrate</c> so the mapping is unit-testable.
/// </summary>
public static class EventNarration
{
    public static string? Line(GameEvent gameEvent, GameState state) => gameEvent switch
    {
        ItemCrafted crafted =>
            $"  ⚒ forged {ItemName(state, crafted.Item)} [{crafted.Quality}]",
        ItemSold sold when sold.FromPlayerShop =>
            $"  $ {HeroName(state, sold.Buyer)} bought {ItemName(state, sold.Item)} for {sold.Price}g from YOUR shop",
        HeroPassedOnItem pass =>
            $"  ~ {HeroName(state, pass.Hero)} passed on {ItemName(state, pass.Item)}: {pass.Reason}",
        PartyDeparted dep =>
            "  → " + ExpeditionNarrator.Departure(PartyHeroes(state, dep.Party), dep.TargetFloor, NarratorPack.Pack, state.Rng.Inc, dep.Day),
        AttributionBeatEvent beat =>
            $"  ★ {beat.Beat}: {beat.Detail} (floor {beat.Floor})",
        HeroDied died =>
            $"  † {HeroName(state, died.Hero)} died on floor {died.Floor} — {died.Cause}",
        SupplyDelivered supply =>
            $"  ⛏ runner delivered {ItemName(state, supply.Item)} to {HeroName(state, supply.To)} at camp — {supply.Fee}g",
        PartyRecalled recalled =>
            $"  ⤺ recall bell — [{string.Join(", ", recalled.Party.Select(h => HeroName(state, h)))}] bank and surface",
        RecruitArrived recruit => RecruitLine(recruit, state),
        GossipEmitted gossip =>
            $"  🍺 \"{gossip.Line}\"",
        CustomerApproached approached =>
            $"  → {HeroName(state, approached.Hero)} steps up to the counter",
        CustomerCountered countered =>
            $"  ↔ {HeroName(state, countered.Hero)} offers {countered.OfferGold}g",
        CounterSaleClosed sale when sale.Pinned =>
            $"  ★ {HeroName(state, sale.Hero)} buys {ItemName(state, sale.Item)} for {sale.Price}g — you read them perfectly",
        CounterSaleClosed sale =>
            $"  $ {HeroName(state, sale.Hero)} buys {ItemName(state, sale.Item)} for {sale.Price}g at the counter",
        CustomerWalked walked =>
            $"  ~ {HeroName(state, walked.Hero)} walks away from the counter: {walked.Reason}",
        MemorialHonored honored =>
            $"  🕯 the town bids farewell to {honored.HeroName} — the rite is done",
        HeirloomReforged reforged =>
            $"  ⚒ {ItemName(state, reforged.NewItem)} reforged — {reforged.Lineage}",

        // U1 (C1a, R1/R2): the silent-economy + bounty-lifecycle cluster — every gold event the
        // player caused or was charged for now says so. BountyJudged surfaces `.Reason` VERBATIM
        // (AE7's self-teaching string already names the floor's price floor on a decline); the
        // Program.cs call site dedupes repeat declines per bounty per day (MF-5), not this switch.
        BountyPosted posted =>
            $"  ⚑ bounty posted — floor {posted.TargetFloor} for {posted.RewardGold}g (escrowed)",
        BountyJudged judged when judged.Accepted =>
            $"  ⚑ {judged.Reason}",
        BountyJudged judged =>
            $"  ~ {judged.Reason}",
        BountyPaid paid =>
            $"  $ bounty paid — {HeroName(state, paid.To)} earns {paid.RewardGold}g for the floor bounty",
        RentPaid rent =>
            $"  $ guild rent paid — {rent.AmountGold}g (next due {rent.NextAmountDueGold}g)",
        RentMissed missed =>
            $"  ! rent MISSED — {missed.AmountDueGold}g due, confidence down to {missed.ConfidencePermille}‰ ({missed.MissedPayments} missed lifetime)",
        TariffApplied tariff =>
            $"  ⚖ {tariff.FactionId} tariff on {tariff.MaterialKey} — paid {tariff.PlayerCost}g (base {tariff.BaseLineCost}g, {(tariff.Delta > 0 ? "+" : string.Empty)}{tariff.Delta}g {(tariff.Delta > 0 ? "surcharge" : "discount")})",
        MarketShareShifted share =>
            $"  ↕ rival market share shifts {share.Permille}‰ toward {(share.RivalGained ? "the rival" : "you")}",
        CommissionFulfilled fulfilled =>
            $"  $ commission fulfilled — {HeroName(state, fulfilled.Hero)} pays a {fulfilled.Premium}g premium for {ItemName(state, fulfilled.Item)}",
        CommissionExpired expired =>
            $"  ~ commission expired — {HeroName(state, expired.Hero)} needed a {expired.Slot} by the deadline, unfilled",
        ItemSigned signed =>
            $"  ★ {ItemName(state, signed.Item)} signed into legend as \"{signed.SignedName}\"",

        // Phase B (B1a/B1c, R-B1/R-B3): the legibility spine — a hero's decision, explained; a
        // hero crossing a cosmetic XP-rank threshold. Neither changes a rule; both are presentation
        // signals stamped alongside a decision the sim already makes.
        HeroDecisionExplained decision =>
            $"  ◆ {HeroName(state, decision.Hero)} — {decision.Chosen} over {decision.RunnerUp}: {decision.Reason} ({decision.GapPermille}‰ gap)",
        HeroRankUp rankUp =>
            $"  ⬆ {HeroName(state, rankUp.Hero)} reaches {rankUp.Rank}!",
        MaterialPurchased material =>
            $"  ⛏ bought {material.Quantity}x {material.MaterialKey} from the Morning vendor for {material.Cost}g",
        RecoveryStipendGranted stipend =>
            $"  + recovery stipend granted — +{stipend.Amount}g (you hit a dead end)",

        // U5 (C2b, R4): the Morning muster line — PartiesFormed fires unconditionally every
        // Morning tick (MusterSystem, zero RNG), so this is always the FIRST line the player sees
        // once the Morning's own verbs resolve. It restates the prior Evening's telegraph (same
        // DemandBoard.Snapshot shape) so question -> answer -> question is visible, not just
        // "parties departed" with no callback to what was asked for.
        PartiesFormed formed => DemandNarration.MusterLine(formed.Parties, DemandBoard.Snapshot(state)),

        // FactionStandingShifted is deliberately NOT a case here: OreMarketHandlers/FactionDriftSystem
        // already route it through GossipGenerator into a GossipEmitted line (the existing case
        // above), so a second raw case would double-print it — the same MF-3 trap U3 avoids for camp.
        _ => null,
    };

    /// <summary>U22 (kin-of-the-dead): a recruit who arrived with a mood bump above neutral —
    /// <see cref="GameSim.Drama.RecruitSystem"/> only ever seeds one when a famous-dead legend
    /// exists — earns the prose hook; an ordinary arrival keeps the plain line. Presentation-only,
    /// derived at narration time from the already-seeded <see cref="Hero.MoodPermille"/> — no new
    /// event field needed.</summary>
    private static string RecruitLine(RecruitArrived recruit, GameState state)
    {
        var name = HeroName(state, recruit.Hero);
        var seededByLegend = state.Heroes.TryGetValue(recruit.Hero.Value, out var hero) && hero.MoodPermille > 0;
        return seededByLegend
            ? $"  + recruit {name} arrives in town — came having heard what your steel did for the fallen"
            : $"  + recruit {name} arrives in town";
    }

    private static string HeroName(GameState s, HeroId id) => s.Heroes.TryGetValue(id.Value, out var h) ? h.Name : id.ToString();

    private static string ItemName(GameState s, ItemId id) => s.Items.TryGetValue(id.Value, out var i) ? i.Name : id.ToString();

    private static ImmutableList<Hero> PartyHeroes(GameState s, ImmutableList<HeroId> ids)
    {
        var heroes = ImmutableList.CreateBuilder<Hero>();
        foreach (var id in ids)
        {
            if (s.Heroes.TryGetValue(id.Value, out var hero))
            {
                heroes.Add(hero);
            }
        }

        return heroes.ToImmutable();
    }
}
