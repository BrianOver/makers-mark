using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Heroes;

namespace GameSim.Cli;

/// <summary>
/// U5 (C2b, R4): renders <see cref="DemandBoard.Snapshot"/>'s <see cref="DemandSnapshot"/> into the
/// three player-facing demand surfaces — the Evening <see cref="TelegraphLines"/> block (a
/// forward-looking call to action), the Morning <see cref="MusterLine"/> (fired off
/// <see cref="PartiesFormed"/>, restates the prior telegraph so question -&gt; answer -&gt; question is
/// visible instead of parties silently marching out), and the full <see cref="DemandVerbLines"/>
/// dump the <c>demand</c> REPL verb prints on request. Pure formatting only (KTD-5): no state
/// mutation, no RNG draw, no new event — every line is a projection over an already-computed
/// snapshot, extracted the same way <see cref="EventNarration"/>/<see cref="CampNarration"/> are so
/// the mapping is unit-testable without parsing Program.cs's stdout.
/// </summary>
public static class DemandNarration
{
    /// <summary>
    /// Evening telegraph block (R4): printed after the itemized "why your gold changed" ledger —
    /// the depth-stall call-to-action (KTD6), the open-commission gear-gaps, and the bounty board
    /// with its per-floor minimum (KTD3: warn, never reject). Each line stands alone so a caller can
    /// print them straight to the console without further formatting.
    /// </summary>
    public static ImmutableList<string> TelegraphLines(DemandSnapshot snapshot) =>
    [
        "  ── TOMORROW'S DEMAND ──",
        $"    {StallSummary(snapshot.DepthStalls)}",
        $"    {CommissionSummary(snapshot.OpenCommissions)}",
        $"    {BountySummary(snapshot.BountyFloorMinimums, snapshot.OpenBounties)}",
    ];

    /// <summary>
    /// Morning muster one-liner (R4): <see cref="EventNarration"/>'s <see cref="PartiesFormed"/>
    /// case calls this every Morning (the event fires unconditionally, zero RNG) with the SAME kind
    /// of <see cref="DemandSnapshot"/> the prior Evening's telegraph was built from — restating the
    /// lead stall/commission fact here is what makes the loop visibly close (question -&gt; answer -&gt;
    /// question) instead of the muster reading as an unrelated status line.
    /// </summary>
    /// <param name="heroes">Phase B (B5, attribution barks): looked up to flavor the line with any
    /// Consumable Stocking trait (Prepared/Reckless) marching out today — <see cref="ConsumableStockingFlavor"/>.</param>
    public static string MusterLine(ImmutableList<PartyPlan> parties, DemandSnapshot snapshot, ImmutableSortedDictionary<int, Hero> heroes)
    {
        var heroCount = parties.Sum(p => p.Roster.Count);
        var muster = parties.IsEmpty
            ? "no parties muster today — no living heroes to march"
            : $"{parties.Count} part{(parties.Count == 1 ? "y" : "ies")} muster ({heroCount} hero{(heroCount == 1 ? string.Empty : "es")}) toward floor {string.Join(",", parties.Select(p => p.TargetFloor).Distinct().OrderBy(f => f))}";

        return $"  ⛺ {muster} — {StallSummary(snapshot.DepthStalls)}{ConsumableStockingFlavor(parties, heroes)}";
    }

    /// <summary>Phase B (B5): names every marching hero whose Consumable Stocking trait
    /// (Prepared/Reckless) made a real difference to what's in their pack this morning —
    /// <see cref="Heroes.HeroShoppingSystem"/>'s restock pass (via
    /// <see cref="Heroes.TraitEffects.ConsumableStockTargetFor"/>) already ran earlier THIS SAME
    /// Morning tick (registration order, <see cref="Heroes.MusterSystem"/>'s own doc comment), so
    /// <c>hero.Pack</c> here already reflects the day's stocking decision — a Reckless hero heading
    /// out with an empty pack, or a Prepared hero who topped up past the baseline target. Neutral
    /// heroes (neither trait) and any Prepared/Reckless hero who hasn't hit the tell-tale pack state
    /// yet (still mid-restock over several mornings) produce no clause — this is a bark, not a status
    /// dump, so it stays silent rather than naming every hero every day.</summary>
    private static string ConsumableStockingFlavor(ImmutableList<PartyPlan> parties, ImmutableSortedDictionary<int, Hero> heroes)
    {
        var flavors = new List<string>();
        foreach (var party in parties)
        {
            foreach (var heroId in party.Roster)
            {
                if (!heroes.TryGetValue(heroId.Value, out var hero))
                {
                    continue;
                }

                var traits = TraitRegistry.TraitsFor(hero.Id, hero.Name);
                if (traits.Contains(TraitId.Reckless) && hero.Pack.IsEmpty)
                {
                    flavors.Add($"{hero.Name} marches down with a near-empty pack");
                }
                else if (traits.Contains(TraitId.Prepared) && hero.Pack.Count >= TraitEffects.PreparedStockTarget)
                {
                    flavors.Add($"{hero.Name} stocked deep on salves");
                }
            }
        }

        return flavors.Count == 0 ? string.Empty : $" — {string.Join("; ", flavors)}";
    }

    /// <summary>The full snapshot dump the <c>demand</c> verb prints on request (R4): the rolled-up
    /// pass reasons, every open commission with all five judging fields (so this list doubles as
    /// U9's accept/decline target list), the depth stalls, and the bounty board (per-floor minimum
    /// alongside any live postings).</summary>
    public static ImmutableList<string> DemandVerbLines(DemandSnapshot snapshot)
    {
        var lines = ImmutableList.CreateBuilder<string>();
        lines.Add("  DEMAND BOARD:");

        lines.Add("  -- recent pass reasons (last " + DemandBoard.PassReasonWindowDays + " days) --");
        if (snapshot.PassReasons.IsEmpty)
        {
            lines.Add("    (no passes logged yet)");
        }
        else
        {
            foreach (var reason in snapshot.PassReasons)
            {
                lines.Add($"    \"{reason.Reason}\" x{reason.Count}");
            }
        }

        lines.Add("  -- open commissions (accept/decline targets) --");
        if (snapshot.OpenCommissions.IsEmpty)
        {
            lines.Add("    (none open)");
        }
        else
        {
            foreach (var c in snapshot.OpenCommissions)
            {
                lines.Add($"    {c.Hero} {c.HeroName} wants a {c.MinQuality}+ {c.Slot}, premium {c.PremiumGold}g, due day {c.DeadlineDay}");
            }
        }

        lines.Add("  -- depth stalls --");
        if (snapshot.DepthStalls.IsEmpty)
        {
            lines.Add("    (none — party still pushing deeper)");
        }
        else
        {
            foreach (var stall in snapshot.DepthStalls)
            {
                var blocked = stall.BlockingSlot is { } slot ? $"blocked on {slot}" : QualityGap(stall);
                lines.Add($"    {stall.Hero} {stall.HeroName}: floor {stall.DeepestFloorReached} -> target {stall.TargetFloor}, {blocked}");
            }
        }

        lines.Add("  -- bounty floor (per-floor minimum) --");
        foreach (var floor in snapshot.BountyFloorMinimums)
        {
            lines.Add($"    floor {floor.Floor}: >= {floor.MinimumRewardGold}g");
        }

        lines.Add("  -- open bounties --");
        if (snapshot.OpenBounties.IsEmpty)
        {
            lines.Add("    (none posted)");
        }
        else
        {
            foreach (var b in snapshot.OpenBounties)
            {
                var warn = b.RewardGold < b.MinimumRewardGold ? $" — BELOW floor, needs >= {b.MinimumRewardGold}g" : string.Empty;
                var accepted = b.AcceptedBy is { } acceptor ? $" [accepted by {acceptor}]" : string.Empty;
                lines.Add($"    {b.Bounty} floor {b.TargetFloor}: {b.RewardGold}g posted day {b.PostedOnDay}{warn}{accepted}");
            }
        }

        return lines.ToImmutable();
    }

    /// <summary>The lead depth-stall fact, or a "none" line — shared by the telegraph and the
    /// muster restate so the two surfaces can never drift on the same snapshot.</summary>
    private static string StallSummary(ImmutableList<DepthStallEntry> stalls)
    {
        if (stalls.IsEmpty)
        {
            return "stalled: none — party still pushing deeper";
        }

        var lead = stalls[0];
        var gap = lead.BlockingSlot is { } slot
            ? $"nobody carries a {slot}"
            : QualityGap(lead);
        var extra = stalls.Count > 1 ? $" (+{stalls.Count - 1} more stalled)" : string.Empty;
        return $"stalled: {lead.HeroName} stuck at floor {lead.DeepestFloorReached} (target {lead.TargetFloor}) — {gap}{extra}";
    }

    /// <summary>N1: when no gear slot is empty, the gate is gear QUALITY — name it (grade carried vs
    /// the next floor's required grade) rather than the old "something else is blocking" non-answer.
    /// Falls back gracefully only if the model couldn't resolve both grades.</summary>
    private static string QualityGap(DepthStallEntry stall) =>
        stall is { CarriedQuality: { } carried, RequiredQuality: { } required }
            ? $"carrying {carried} gear — floor {stall.DeepestFloorReached + 1} wants {required}+"
            : "gear's full — something else is blocking";

    private static string CommissionSummary(ImmutableList<OpenCommissionEntry> commissions)
    {
        if (commissions.IsEmpty)
        {
            return "commissions: none open";
        }

        var gaps = string.Join("; ", commissions.Select(c =>
            $"{c.HeroName} wants a {c.MinQuality}+ {c.Slot} (+{c.PremiumGold}g, due day {c.DeadlineDay})"));
        return $"commissions: {commissions.Count} open — {gaps}";
    }

    private static string BountySummary(ImmutableList<BountyFloorMinimum> floors, ImmutableList<OpenBountyEntry> open)
    {
        var floorText = string.Join(" ", floors.Select(f => $"f{f.Floor}>={f.MinimumRewardGold}g"));
        if (open.IsEmpty)
        {
            return $"bounty board: {floorText} (none posted)";
        }

        var postedText = string.Join("; ", open.Select(b => b.RewardGold < b.MinimumRewardGold
            ? $"floor {b.TargetFloor} at {b.RewardGold}g — BELOW floor, needs >={b.MinimumRewardGold}g"
            : $"floor {b.TargetFloor} at {b.RewardGold}g"));
        return $"bounty board: {floorText} — posted: {postedText}";
    }
}
