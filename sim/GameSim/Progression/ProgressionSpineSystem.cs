using System;
using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Expedition;
using GameSim.Professions;

namespace GameSim.Progression;

/// <summary>
/// U-D4: computes the multi-axis <see cref="ProgressionSpine"/> — the "what do I chase next" view —
/// from the live <see cref="GameState"/>. Every rung is derived, never stored: this is a PURE,
/// memoryless read (the <see cref="GoldLedger"/> / <c>ObjectiveAdvisor</c> precedent), so it draws
/// no RNG, reads no wall clock, and mutates nothing — it can never move a seed's golden state.
/// <para>Four finite ladders cross-feed (Forge→Depth via deeper-viable gear, Depth→Forge via richer
/// ore, Roster→Depth via more parties, Wealth→all); the fifth, Chronicle, is unbounded so there is
/// always a next rung even once the finite ceilings are hit.</para>
/// </summary>
public static class ProgressionSpineSystem
{
    public static ProgressionSpine Compute(GameState state)
    {
        var rungs = ImmutableList.CreateBuilder<ProgressionRung>();
        rungs.Add(ForgeRung(state));
        rungs.Add(DepthRung(state));
        rungs.Add(RosterRung(state));
        rungs.Add(WealthRung(state));
        rungs.Add(ChronicleRung(state));
        return new ProgressionSpine(rungs.ToImmutable());
    }

    // Forge: recipe-tier gates unlocked so far → the next locked tier's talent node. Feeds Depth
    // (higher-quality gear survives deeper). The quality ceiling is bounded by the last gate.
    private static ProgressionRung ForgeRung(GameState state)
    {
        var bs = ProfessionRegistry.Blacksmith;
        var unlocked = state.Player.TalentsFor(bs.Id);
        var total = bs.TierGate.Count;

        // TierGate is a sorted dict (tier ascending); the first locked entry is the next rung.
        var lockedTiers = bs.TierGate.Where(kv => !unlocked.Contains(kv.Value)).ToList();
        var haveTiers = total - lockedTiers.Count;
        var highestOpen = bs.TierGate
            .Where(kv => unlocked.Contains(kv.Value))
            .Select(kv => kv.Key)
            .DefaultIfEmpty(1)
            .Max();

        if (lockedTiers.Count == 0)
        {
            return new ProgressionRung(
                ProgressionAxis.Forge,
                $"Forge tier {highestOpen} — every gate open",
                "Master smith: the quality ceiling is reached",
                1000,
                Unbounded: false,
                "feeds Depth (deeper-viable gear)");
        }

        var next = lockedTiers[0];
        return new ProgressionRung(
            ProgressionAxis.Forge,
            $"Forge tier {highestOpen}",
            $"Forge tier {next.Key} — unlock {next.Value}",
            total == 0 ? null : (haveTiers * 1000) / total,
            Unbounded: false,
            "feeds Depth (deeper-viable gear)");
    }

    // Depth: the deepest floor any hero has reached (board record or a living hero's mark) → the next
    // floor, up to the Mine's current deepest ("the wall"). Feeds Forge (deeper floors drop richer ore).
    private static ProgressionRung DepthRung(GameState state)
    {
        var boardDeepest = state.Drama.DepthsBoard.Values.DefaultIfEmpty(0).Max();
        var heroDeepest = state.Heroes.Values.Select(h => h.DeepestFloorReached).DefaultIfEmpty(0).Max();
        var deepest = Math.Max(boardDeepest, heroDeepest);
        var wall = MonsterTable.FloorCount;

        if (deepest >= wall)
        {
            return new ProgressionRung(
                ProgressionAxis.Depth,
                $"Floor {deepest} — the wall",
                "The Mine's deepest known floor is conquered",
                1000,
                Unbounded: false,
                "feeds Forge (richer ore)");
        }

        return new ProgressionRung(
            ProgressionAxis.Depth,
            deepest == 0 ? "No floor cleared yet" : $"Floor {deepest}",
            $"Floor {deepest + 1}",
            wall == 0 ? null : (deepest * 1000) / wall,
            Unbounded: false,
            "feeds Forge (richer ore)");
    }

    // Roster: living hero count → the next recruit's arrival (the recruit-trickle countdown). Feeds
    // Depth (more heroes = more parties raiding deeper in parallel).
    private static ProgressionRung RosterRung(GameState state)
    {
        var alive = state.Heroes.Values.Count(h => h.Alive);
        var days = state.Drama.DaysUntilNextRecruit;
        var next = days <= 0
            ? "A new recruit is due to arrive"
            : $"New recruit in {days} day{Plural(days)}";

        return new ProgressionRung(
            ProgressionAxis.Roster,
            $"{alive} hero{(alive == 1 ? "" : "es")} in the roster",
            next,
            ProgressPermille: null,
            Unbounded: false,
            "feeds Depth (more parties, deeper)");
    }

    // Wealth: gold on hand → the nearest concrete gold demand (the Guild Assessment due next), with a
    // covered/short read. Feeds every ladder (gold buys ore, gates, supplies, bounties).
    private static ProgressionRung WealthRung(GameState state)
    {
        var gold = state.Player.Gold;
        var dues = state.Assessment.DuesGold;
        var days = state.Assessment.DaysUntilAssessment;
        var covered = gold >= dues;
        var next =
            $"Guild assessment: {dues}g in {days} day{Plural(days)} — {(covered ? "covered" : "short, raise gold")}";

        return new ProgressionRung(
            ProgressionAxis.Wealth,
            $"{gold}g on hand",
            next,
            dues <= 0 ? 1000 : Math.Min(1000, (gold * 1000) / dues),
            Unbounded: false,
            "feeds every ladder");
    }

    // Chronicle/Legacy: the UNBOUNDED axis — legends made (famous dead + famous living) and memorials
    // raised. There is always another legend to forge, so this never completes; that is the whole
    // point (the tree outlives the finite ceilings).
    private static ProgressionRung ChronicleRung(GameState state)
    {
        var memorials = state.Drama.Memorials;
        var famousDead = memorials.Count(m => LegendQuery.IsFamousDead(state, m.Hero));
        var famousLiving = state.Heroes.Values.Count(h =>
            h.Alive && LegendQuery.AttributionBeatCount(state, h.Id) >= LegendQuery.FamousBeatThreshold);
        var legends = famousDead + famousLiving;

        return new ProgressionRung(
            ProgressionAxis.Chronicle,
            $"{legends} legend{Plural(legends)}, {memorials.Count} memorial{Plural(memorials.Count)}",
            "Forge another legend — the ledger never closes",
            ProgressPermille: null,
            Unbounded: true,
            "outlives every finite ladder");
    }

    private static string Plural(int n) => n == 1 ? "" : "s";
}
