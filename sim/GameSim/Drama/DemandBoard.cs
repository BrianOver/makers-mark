using System.Collections.Immutable;
using GameSim.Bounties;
using GameSim.Contracts;
using GameSim.Heroes;
using GameSim.Venues;

namespace GameSim.Drama;

/// <summary>One or more heroes passed on an item for the same legible reason (R8/AE4) within the
/// recent window — a rolled-up telegraph line, not a per-event replay.</summary>
public sealed record PassReasonRollup(string Reason, int Count);

/// <summary>One un-accepted commission (Wave 3) as an accept/decline target: everything a player
/// needs to judge it — who asked, what slot, the quality floor, the premium, and the deadline.</summary>
public sealed record OpenCommissionEntry(
    HeroId Hero,
    string HeroName,
    ItemSlot Slot,
    QualityGrade MinQuality,
    int PremiumGold,
    int DeadlineDay);

/// <summary>A hero who hasn't posted a new personal-deepest floor in a while (KTD6): the
/// counterfactual made visible — <see cref="BlockingSlot"/> is the first empty gear slot in the
/// fixed Weapon/Shield/Armor order (<see cref="RaidForecast.MissingItemSlots"/>), or null when every
/// slot is filled. In the null case the true block is named instead of left as a non-answer (N1,
/// plan 2026-07-25-001): <see cref="CarriedQuality"/> is the worst grade the hero has worn across
/// Weapon/Shield/Armor and <see cref="RequiredQuality"/> is what the NEXT floor
/// (<c>DeepestFloorReached + 1</c>) demands, both populated together, both null exactly when
/// <see cref="BlockingSlot"/> is non-null (a slot gap already fully explains the stall — no quality
/// read needed).</summary>
public sealed record DepthStallEntry(
    HeroId Hero,
    string HeroName,
    int DeepestFloorReached,
    int TargetFloor,
    ItemSlot? BlockingSlot,
    QualityGrade? CarriedQuality = null,
    QualityGrade? RequiredQuality = null);

/// <summary>The price floor a hero expects before risking a given depth (R18), shown whether or
/// not a bounty is currently posted there — the reference the board's live postings are judged
/// against.</summary>
public sealed record BountyFloorMinimum(int Floor, int MinimumRewardGold);

/// <summary>One still-live bounty (posted, not yet paid or refunded) with its floor's minimum
/// reward inlined so a below-floor post is visible at a glance (KTD3: warn, never reject).</summary>
public sealed record OpenBountyEntry(
    BountyId Bounty,
    int TargetFloor,
    int RewardGold,
    int PostedOnDay,
    HeroId? AcceptedBy,
    int MinimumRewardGold);

/// <summary>The whole demand telegraph in one snapshot (U4/C2a): rolled-up pass reasons, the open
/// commission board, the depth-stall call-to-action, and the bounty board with its price floor —
/// everything the sim already computes about what the town wants, gathered in one place.</summary>
public sealed record DemandSnapshot(
    ImmutableList<PassReasonRollup> PassReasons,
    ImmutableList<OpenCommissionEntry> OpenCommissions,
    ImmutableList<DepthStallEntry> DepthStalls,
    ImmutableList<BountyFloorMinimum> BountyFloorMinimums,
    ImmutableList<OpenBountyEntry> OpenBounties);

/// <summary>
/// Pure read model over <see cref="GameState"/>/<see cref="GameState.EventLog"/> (KTD-5, mirrors
/// <see cref="LedgerQuery"/>): no mutation, no RNG draw, no wall clock, callable any number of
/// times by the CLI (U5) and the Godot demand panel (U6). Demand does NOT rotate here (Phase B's
/// needs engine) — this surfaces the STATIC demand the sim already computes; day-to-day content
/// variance is a recorded measurement for that later phase, never a pass/fail gate on this model.
/// </summary>
public static class DemandBoard
{
    /// <summary>How many trailing days of <see cref="HeroPassedOnItem"/> feed the pass-reason
    /// rollup — recent enough to read as "what's happening now," wide enough that a quiet single
    /// day still shows the prior days' signal (so day 1 of a run is never empty once heroes have
    /// shopped at all).</summary>
    public const int PassReasonWindowDays = 3;

    /// <summary>Days since a hero's last personal-deepest floor record before the plateau counts
    /// as a depth stall (KTD6). Small on purpose: the call-to-action should surface fast, not after
    /// a long silent grind.</summary>
    public const int StallThresholdDays = 2;

    /// <summary>The whole demand snapshot for the current state — every input is read straight off
    /// <see cref="GameState"/>/<see cref="GameState.EventLog"/>, no simulation forward or back.</summary>
    public static DemandSnapshot Snapshot(GameState state)
    {
        return new DemandSnapshot(
            PassReasons(state),
            OpenCommissions(state),
            DepthStalls(state),
            BountyFloorMinimums(),
            OpenBounties(state));
    }

    /// <summary>
    /// (a) Rolled-up recent <see cref="HeroPassedOnItem"/> reasons: every reason string logged in
    /// the trailing <see cref="PassReasonWindowDays"/> days, grouped and counted verbatim (the
    /// reason text IS the self-teaching line — R8 — nothing is re-derived). Ordered by count
    /// descending, then reason text (Ordinal) ascending for a stable, deterministic render.
    /// </summary>
    private static ImmutableList<PassReasonRollup> PassReasons(GameState state)
    {
        var since = state.Day - PassReasonWindowDays + 1;
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        // The log is stamped in nondecreasing Day order (DayLog's own invariant): walk from the
        // tail and stop the moment an entry falls outside the window instead of scanning it all.
        for (var i = state.EventLog.Count - 1; i >= 0; i--)
        {
            var gameEvent = state.EventLog[i];
            if (gameEvent.Day < since)
            {
                break;
            }

            if (gameEvent is HeroPassedOnItem passed)
            {
                counts[passed.Reason] = counts.GetValueOrDefault(passed.Reason) + 1;
            }
        }

        return counts
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new PassReasonRollup(kv.Key, kv.Value))
            .ToImmutableList();
    }

    /// <summary>
    /// (b) Every currently OPEN (not-yet-accepted) commission from <see cref="GameState.Commissions"/>
    /// (<c>World.cs</c>), each rendering all five judging fields so this list doubles as U9's
    /// accept/decline target list. An already-accepted commission is being worked, not a pending
    /// ask, so it is excluded here (mirrors <c>CommissionSystem.MaxOpenCommissions</c>'s own
    /// "open == not-yet-accepted" definition).
    /// </summary>
    private static ImmutableList<OpenCommissionEntry> OpenCommissions(GameState state)
    {
        var open = ImmutableList.CreateBuilder<OpenCommissionEntry>();
        foreach (var commission in state.Commissions)
        {
            if (commission.Accepted)
            {
                continue;
            }

            // A dead hero's commission can never be fulfilled or meaningfully accepted — don't
            // surface it as a target, else the advisor/board point the player at the fallen
            // (fable Slice-2 confirm: a suggestion named a hero who had died four days earlier).
            if (!state.Heroes.TryGetValue(commission.Hero.Value, out var hero) || !hero.Alive)
            {
                continue;
            }

            open.Add(new OpenCommissionEntry(
                commission.Hero, hero.Name, commission.Slot, commission.MinQuality,
                commission.PremiumGold, commission.DeadlineDay));
        }

        return open.ToImmutable();
    }

    /// <summary>
    /// (c) Party depth-stall (KTD6): every alive hero who hasn't posted a new personal-deepest
    /// floor in at least <see cref="StallThresholdDays"/> days and hasn't yet reached the venue's
    /// top floor. "Target" is the deepest floor the (only) live venue offers — the Mine
    /// (<see cref="VenueRegistry.Mine"/>, <see cref="VenueRegistry.LiveRotation"/>) — so the entry
    /// names the full gap to the top, not just the next step, matching KTD6's "floor 3-4 plateau"
    /// framing. Last-progress day is the most recent <see cref="FloorRecordSet"/> for the hero, or
    /// (for a hero who has never set one) the day they joined via <see cref="RecruitArrived"/>, or
    /// day 1 for the starting roster (no arrival event of their own).
    /// </summary>
    private static ImmutableList<DepthStallEntry> DepthStalls(GameState state)
    {
        var targetFloor = VenueRegistry.Mine.FloorCount;
        var lastRecordDay = new Dictionary<int, int>();
        var arrivalDay = new Dictionary<int, int>();

        foreach (var gameEvent in state.EventLog)
        {
            switch (gameEvent)
            {
                case FloorRecordSet record:
                    lastRecordDay[record.Hero.Value] =
                        Math.Max(record.Day, lastRecordDay.GetValueOrDefault(record.Hero.Value));
                    break;
                case RecruitArrived arrived:
                    arrivalDay[arrived.Hero.Value] = arrived.Day;
                    break;
            }
        }

        var stalls = ImmutableList.CreateBuilder<DepthStallEntry>();
        foreach (var hero in state.Heroes.Values)
        {
            if (!hero.Alive || hero.DeepestFloorReached >= targetFloor)
            {
                continue;
            }

            var since = lastRecordDay.TryGetValue(hero.Id.Value, out var recorded)
                ? recorded
                : arrivalDay.GetValueOrDefault(hero.Id.Value, 1);

            if (state.Day - since < StallThresholdDays)
            {
                continue;
            }

            var missing = RaidForecast.MissingItemSlots(hero.Gear);
            if (missing.Count > 0)
            {
                stalls.Add(new DepthStallEntry(
                    hero.Id, hero.Name, hero.DeepestFloorReached, targetFloor, missing[0]));
                continue;
            }

            // Every slot is filled — name the QUALITY gate instead of leaving "something else" as a
            // non-answer (N1): the next floor's bar (the SAME table CommissionSystem's own gap-scan
            // judges commissions against) vs the worst grade this hero actually carries.
            var nextFloor = hero.DeepestFloorReached + 1;
            var required = CommissionSystem.FloorMinQuality(nextFloor);
            var carried = WorstCarriedQuality(hero.Gear, state.Items);
            stalls.Add(new DepthStallEntry(
                hero.Id, hero.Name, hero.DeepestFloorReached, targetFloor,
                BlockingSlot: null, CarriedQuality: carried, RequiredQuality: required));
        }

        return stalls.ToImmutable();
    }

    /// <summary>The worst (lowest) <see cref="QualityGrade"/> among a hero's worn Weapon/Shield/Armor
    /// — the same three slots <see cref="RaidForecast.MissingItemSlots"/> checks for emptiness. Only
    /// called once every slot is confirmed non-null (see <see cref="DepthStalls"/>), so a missing item
    /// lookup (defensively defaulted to <see cref="QualityGrade.Poor"/>, the weakest grade, never
    /// thrown) can only ever pull the reported gate DOWN, never hide a real one.</summary>
    private static QualityGrade WorstCarriedQuality(GearSet gear, ImmutableSortedDictionary<int, Item> items)
    {
        var worst = QualityGrade.Masterwork;
        foreach (var slot in new[] { ItemSlot.Weapon, ItemSlot.Shield, ItemSlot.Armor })
        {
            var worn = gear.Slot(slot);
            var grade = worn is { } id && items.TryGetValue(id.Value, out var item)
                ? item.Quality
                : QualityGrade.Poor;
            if (grade < worst)
            {
                worst = grade;
            }
        }

        return worst;
    }

    /// <summary>(d, price-floor half) The pure minimum-reward reference for every floor the Mine
    /// offers, straight off <see cref="BountyRules.MinimumReward"/> — no state read at all, so a
    /// board with zero postings still shows the prices heroes will judge against.</summary>
    private static ImmutableList<BountyFloorMinimum> BountyFloorMinimums()
    {
        var floors = ImmutableList.CreateBuilder<BountyFloorMinimum>();
        for (var floor = 1; floor <= VenueRegistry.Mine.FloorCount; floor++)
        {
            floors.Add(new BountyFloorMinimum(floor, BountyRules.MinimumReward(floor)));
        }

        return floors.ToImmutable();
    }

    /// <summary>(d, live-postings half) Every still-live bounty in <see cref="GameState.Bounties"/>
    /// (paid and refunded ones are already removed by <c>BountyPayoutSystem</c> — nothing here
    /// needs to filter that), each carrying its own floor's <see cref="BountyRules.MinimumReward"/>
    /// inline so a below-floor post reads as a warning, never a rejection (KTD3).</summary>
    private static ImmutableList<OpenBountyEntry> OpenBounties(GameState state)
    {
        var open = ImmutableList.CreateBuilder<OpenBountyEntry>();
        foreach (var bounty in state.Bounties)
        {
            open.Add(new OpenBountyEntry(
                bounty.Id, bounty.TargetFloor, bounty.RewardGold, bounty.PostedOnDay,
                bounty.AcceptedBy, BountyRules.MinimumReward(bounty.TargetFloor)));
        }

        return open.ToImmutable();
    }
}
