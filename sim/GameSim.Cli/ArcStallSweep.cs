using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using GameSim;
using GameSim.Arc;
using GameSim.Contracts;
using GameSim.Expedition;
using GameSim.Harness;
using GameSim.Heroes;
using GameSim.Venues;

namespace GameSim.Cli;

/// <summary>
/// One-off diagnostic sweep (P2-END-01 measurement, not a gate): WHY does a campaign fail to reach
/// its ending inside the playable horizon?
///
/// <para>A 20-seed baseline corpus showed 18 of 20 campaigns ending on day 23-33 and 2 never ending
/// inside 100 days. That measurement established only THAT the two stall — it named no cause. The
/// worst possible cause would be a doom loop: the ending gated on something the late-campaign
/// economic decay can itself make unsatisfiable (gold the collapsed shop can no longer earn, gear
/// it can no longer make), so the game locks its own exit. This sweep exists to settle that
/// question, and it instruments the whole chain the ending hangs from:</para>
///
/// <list type="number">
///   <item><see cref="ArcDirectorSystem"/> fires the Ending <see cref="ArcDirectorSystem.EndingDelayDays"/>
///   days after the Climax;</item>
///   <item>the Climax fires when any hero's <see cref="Hero.LadderRank"/> reaches
///   <see cref="ArcDirectorSystem.ClimaxRank"/>, and rank is earned ONE RUNG AT A TIME — a rank-<c>r</c>
///   party clearing a rank-<c>r</c> venue's own bottom floor graduates to <c>r+1</c>
///   (<c>ExpeditionRevealSystem</c> is that field's only write site);</item>
///   <item>every one of those bottom-floor clears runs the gauntlet of <see cref="VenueDefinition.Gate"/>,
///   a STRUCTURAL power check with no roll: a party whose <see cref="CombatMath.PartyAveragePower"/>
///   is under the floor's gate halts at <see cref="ExpeditionHalt.GateHeld"/> and goes home.</item>
/// </list>
///
/// <para>So a stalled campaign has a RUNG it stopped on, and the sweep's job is to name it and say
/// what held it there. Per rank it records how many days a party of that rank existed, that party's
/// PEAK power, and how many days that peak sat at or over the bottom-floor gate of the venue
/// <see cref="VenueRouter"/> would send it to — recomputed EVERY day from the live roster through
/// the same <see cref="PartyFormation"/>/<see cref="CombatMath"/> the real Expedition tick uses.
/// An observer-side recomputation: it submits no actions and mutates nothing.</para>
///
/// <para>Purity: this file is CLI-side (file IO lives at the edge, KTD2). It drives the same
/// <see cref="GameComposition.BuildKernel"/> + scripted-policy loop <c>batch</c> does, reads only
/// public state, and draws no RNG of its own — the sweep is a pure observer of a deterministic
/// campaign, so a re-run with the same seeds and policy prints identical numbers.</para>
/// </summary>
public static class ArcStallSweep
{
    /// <summary>What one rung of the ladder did over a whole campaign — the row that names a wall.</summary>
    private sealed record RungStat(int Rank, string VenueId, int BottomGate)
    {
        public int DaysWithParty { get; set; }

        public int FirstPartyDay { get; set; }

        public int PeakPower { get; set; }

        public int LastPower { get; set; }

        public int DaysAtOrOverGate { get; set; }

        public int Attempts { get; set; }

        public int BestFloorCleared { get; set; }

        public SortedDictionary<string, int> Halts { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>Per-seed verdict — the row an operator reads to tell a stall from a slow finish.</summary>
    private sealed record SeedRow(
        ulong Seed,
        int EndingDay,
        int ClimaxDay,
        int MaxLadderRank,
        ImmutableList<RungStat> Rungs,
        int FinalGold,
        int ShopSales,
        int ShopSalesFirst30,
        int ShopSalesLast30,
        int Deaths,
        int AliveAtEnd)
    {
        /// <summary>The rung the campaign stopped on: the highest rank that ever fielded a party,
        /// which for a stalled seed is by definition the one that never graduated. Null when no
        /// party of any rank was ever observed (an empty or dead roster).</summary>
        public RungStat? WallRung => Rungs.Where(r => r.DaysWithParty > 0).OrderBy(r => r.Rank).LastOrDefault();
    }

    public static int Run(
        int seedCount, ulong startSeed, int days, string outDir, string policyArg, ulong? traceSeed,
        TextWriter output, TextWriter error, string handArg = "average")
    {
        if (BatchRunner.ParsePolicy(policyArg) is not { } policy)
        {
            error.WriteLine($"arc-stall: unknown --policy '{policyArg}' ({BatchRunner.PolicyNames})");
            return 1;
        }

        // The craft hand is the axis P2-END-01 turned on: a gate one to three points wide is decided
        // by item quality, so "does the wall still hold" is only answerable if the sweep can drive a
        // deliberately BAD hand as well as an average one. Same three names and the same refusal rule
        // `batch` uses (BatchRunner.Parse) — a non-average hand against an auto-crafting policy is
        // rejected rather than silently ignored, because a sweep that thinks it measured an
        // indifferent smith and actually measured auto-craft is the exact mis-read this axis exists
        // to prevent.
        var hand = handArg.ToLowerInvariant() switch
        {
            "indifferent" => (CraftHand?)CraftHand.Indifferent,
            "average" => CraftHand.Average,
            "skilled" => CraftHand.Skilled,
            _ => null,
        };

        if (hand is null)
        {
            error.WriteLine($"arc-stall: unknown --hand '{handArg}' (expected 'indifferent', 'average', or 'skilled')");
            return 1;
        }

        if (hand != CraftHand.Average && !BatchRunner.HandAware(policy))
        {
            error.WriteLine($"arc-stall: --hand {handArg} needs a policy that plays a craft minigame "
                + $"(handforge, latemastery, alchemy, tanning, engineering) — '{policyArg}' auto-crafts");
            return 1;
        }

        try
        {
            Directory.CreateDirectory(outDir);
        }
        catch (Exception ex)
        {
            error.WriteLine($"arc-stall: cannot create '{outDir}': {ex.Message}");
            return 1;
        }

        var kernel = GameComposition.BuildKernel();
        var policyFn = BatchRunner.PolicyFn(policy, hand.Value);
        var startingProfession = BatchRunner.PolicyStartingProfession(policy);
        var policyTag = BatchRunner.PolicyFileTag(policy, hand.Value);

        // The rungs, read from the registry exactly as the arc director reads them — never
        // hand-pinned, so a new rung added to the ladder moves this sweep with it. The venue a rank
        // routes to is VenueRouter's own answer for an empty queue, so the gate below is the one
        // that rank's parties actually meet.
        var terminalRank = ArcDirectorSystem.TerminalRank;
        var emptyQueue = new Dictionary<string, int>(StringComparer.Ordinal);
        var rungPlan = Enumerable.Range(0, terminalRank + 1)
            .Select(rank =>
            {
                var venue = VenueRegistry.Require(VenueRouter.ChooseVenue(rank, VenueRegistry.LiveRotation, emptyQueue));
                return (Rank: rank, Venue: venue, BottomGate: venue.Gate(venue.FloorCount));
            })
            .ToImmutableList();

        var rows = new List<SeedRow>();
        var trace = traceSeed is null ? null : new StringBuilder();

        for (var i = 0; i < seedCount; i++)
        {
            var seed = startSeed + (ulong)i;
            var tracing = traceSeed == seed;
            if (tracing)
            {
                trace!.AppendLine("day,alive,deaths,maxRank,playerGold,heroGold,shopSalesCum,craftsCum,"
                    + "shelf,openOreOffers,heroesPassedCum,matsTotal,"
                    + string.Join(",", rungPlan.Select(r => $"rank{r.Rank}Mats"))
                    + "," + string.Join(",", rungPlan.Select(r => $"rank{r.Rank}Power,rank{r.Rank}Gate")));
            }

            var state = startingProfession is null
                ? GameComposition.NewCampaign(seed)
                : GameComposition.NewCampaign(seed, startingProfession);

            var rungs = rungPlan.ToDictionary(r => r.Rank, r => new RungStat(r.Rank, r.Venue.Id, r.BottomGate));
            var venueRank = rungPlan.ToDictionary(r => r.Venue.Id, r => r.Rank, StringComparer.Ordinal);
            var sampledDay = 0;

            // No event carries a day stamp, so the log cannot be sliced by date after the fact.
            // The sweep instead remembers how long the log was at each window boundary and counts
            // player-shop sales past that mark — the economy half of the picture, so a stalled
            // seed's shop reads as dead or alive without a second tool.
            var last30From = Math.Max(1, days - 29);
            var markFirst30 = -1;
            var markLast30 = -1;

            // LastNightExpeditions is replaced WHOLESALE each Evening reveal, so a reference change
            // is exactly "a new night's results landed" — recording on that edge counts each result
            // once without any day/phase bookkeeping to get wrong.
            var seenResults = state.LastNightExpeditions;

            while (state.Day <= days)
            {
                if (state.Day != sampledDay)
                {
                    sampledDay = state.Day;
                    if (markFirst30 < 0 && state.Day > 30)
                    {
                        markFirst30 = state.EventLog.Count;
                    }

                    if (markLast30 < 0 && state.Day >= last30From)
                    {
                        markLast30 = state.EventLog.Count;
                    }

                    var powers = BestPartyPowerByRank(state);
                    foreach (var rung in rungs.Values)
                    {
                        if (!powers.TryGetValue(rung.Rank, out var p))
                        {
                            continue;
                        }

                        rung.DaysWithParty++;
                        if (rung.FirstPartyDay == 0)
                        {
                            rung.FirstPartyDay = state.Day;
                        }

                        rung.PeakPower = Math.Max(rung.PeakPower, p);
                        rung.LastPower = p;
                        if (p >= rung.BottomGate)
                        {
                            rung.DaysAtOrOverGate++;
                        }
                    }

                    if (tracing)
                    {
                        var powerCells = rungPlan.Select(r =>
                            $"{(powers.TryGetValue(r.Rank, out var p) ? p : -1)},{r.BottomGate}");
                        // The rung's OWN ore in the player's stock: the link the doom-loop question
                        // turns on, because every recipe above Tier 3 is gated on material a party
                        // can only bring back from a venue it has already graduated INTO.
                        var matCells = rungPlan.Select(r => r.Venue.Floors
                            .Sum(f => state.Player.Materials.GetValueOrDefault(f.OreKey)));
                        trace!.AppendLine($"{state.Day},{state.Heroes.Values.Count(h => h.Alive)},"
                            + $"{state.Drama.Memorials.Count},"
                            + $"{state.Heroes.Values.Select(h => h.LadderRank).DefaultIfEmpty(0).Max()},"
                            + $"{state.Player.Gold},"
                            + $"{state.Heroes.Values.Where(h => h.Alive).Sum(h => h.Gold)},"
                            + $"{state.EventLog.Count(e => e is ItemSold { FromPlayerShop: true })},"
                            + $"{state.EventLog.Count(e => e is ItemCrafted)},"
                            + $"{state.Player.Shelf.Count},{state.OpenOreOffers.Count},"
                            + $"{state.EventLog.Count(e => e is HeroPassedOnItem)},"
                            + $"{state.Player.Materials.Values.Sum()},"
                            + string.Join(",", matCells) + "," + string.Join(",", powerCells));
                    }
                }

                state = kernel.Tick(state, policyFn(state)).NewState;

                if (!ReferenceEquals(state.LastNightExpeditions, seenResults))
                {
                    seenResults = state.LastNightExpeditions;
                    foreach (var result in seenResults)
                    {
                        if (!venueRank.TryGetValue(result.VenueId, out var rank) || !rungs.TryGetValue(rank, out var rung))
                        {
                            continue; // a venue no rank routes to (peer rung) — counted on its own rank only
                        }

                        rung.Attempts++;
                        var halt = result.Halt.ToString();
                        rung.Halts[halt] = rung.Halts.GetValueOrDefault(halt) + 1;
                        rung.BestFloorCleared = Math.Max(rung.BestFloorCleared, result.DeepestFloorCleared);
                    }
                }
            }

            var sales = state.EventLog.Count(e => e is ItemSold { FromPlayerShop: true });
            var salesFirst30 = markFirst30 < 0
                ? sales
                : state.EventLog.Take(markFirst30).Count(e => e is ItemSold { FromPlayerShop: true });
            var salesLast30 = markLast30 < 0
                ? sales
                : state.EventLog.Skip(markLast30).Count(e => e is ItemSold { FromPlayerShop: true });

            rows.Add(new SeedRow(
                Seed: seed,
                EndingDay: state.Arc.EndingDay,
                ClimaxDay: state.Arc.ClimaxDay,
                MaxLadderRank: state.Heroes.Values.Select(h => h.LadderRank).DefaultIfEmpty(0).Max(),
                Rungs: rungs.Values.OrderBy(r => r.Rank).ToImmutableList(),
                FinalGold: state.Player.Gold,
                ShopSales: sales,
                ShopSalesFirst30: salesFirst30,
                ShopSalesLast30: salesLast30,
                Deaths: state.Drama.Memorials.Count,
                AliveAtEnd: state.Heroes.Values.Count(h => h.Alive)));

            output.WriteLine(Describe(rows[^1]));
        }

        var summary = BuildSummary(rows, policyTag, startSeed, days, rungPlan);
        var path = Path.Combine(outDir, $"arc-stall-{policyTag}-seed{startSeed}-n{seedCount}-d{days}.md");
        try
        {
            File.WriteAllText(path, summary);
            if (trace is not null)
            {
                var tracePath = Path.Combine(outDir, $"arc-stall-{policyTag}-trace-seed{traceSeed}-d{days}.csv");
                File.WriteAllText(tracePath, trace.ToString());
                output.WriteLine($"arc-stall: wrote {tracePath}");
            }
        }
        catch (Exception ex)
        {
            error.WriteLine($"arc-stall: write failed under '{outDir}': {ex.Message}");
            return 1;
        }

        output.WriteLine();
        output.Write(summary);
        output.WriteLine($"arc-stall: wrote {path}");
        return 0;
    }

    /// <summary>
    /// The power of the STRONGEST party the live roster could field at each ladder rank today.
    /// Recomputed through <see cref="PartyFormation.FormParties"/> so it sees exactly the cohorts
    /// the real Expedition tick would form (rank-uniform by construction), and through
    /// <see cref="CombatMath.PartyAveragePower"/> so it is the same number
    /// <see cref="ExpeditionResolver"/>'s structural gate compares. Observer-only: allocates its own
    /// copies and writes nothing back into <paramref name="state"/>.
    /// </summary>
    public static Dictionary<int, int> BestPartyPowerByRank(GameState state)
    {
        var best = new Dictionary<int, int>();
        foreach (var party in PartyFormation.FormParties(state.Heroes))
        {
            var members = party.Select(id => state.Heroes[id.Value]).ToList();
            if (members.Count == 0)
            {
                continue;
            }

            // Cohorts are rank-uniform by construction, so member 0's rank is the party's rank.
            var rank = members[0].LadderRank;
            var power = CombatMath.PartyAveragePower(members, state.Items);
            if (!best.TryGetValue(rank, out var incumbent) || power > incumbent)
            {
                best[rank] = power;
            }
        }

        return best;
    }

    private static string Halts(RungStat rung, string separator) => rung.Halts.Count == 0
        ? "-"
        : string.Join(separator, rung.Halts.Select(kv => $"{kv.Key}={kv.Value}"));

    private static string Describe(SeedRow r)
    {
        var verdict = r.EndingDay > 0 ? $"ended d{r.EndingDay}" : "NO ENDING";
        var wall = r.WallRung is { } w
            ? $"wall rung {w.Rank} ({w.VenueId}): power peak {w.PeakPower} vs bottom gate {w.BottomGate}, "
                + $"{w.DaysAtOrOverGate}/{w.DaysWithParty} days at/over it, {w.Attempts} attempts "
                + $"(halts {Halts(w, ",")}, best floor {w.BestFloorCleared})"
            : "no party ever formed";
        return $"  seed {r.Seed}: {verdict}, climax d{r.ClimaxDay}, maxRank {r.MaxLadderRank} | {wall} | "
            + $"gold {r.FinalGold}, shop sales {r.ShopSales} ({r.ShopSalesFirst30} in first 30d, "
            + $"{r.ShopSalesLast30} in last 30d), deaths {r.Deaths}, alive {r.AliveAtEnd}";
    }

    private static string BuildSummary(
        List<SeedRow> rows, string policyTag, ulong startSeed, int days,
        ImmutableList<(int Rank, VenueDefinition Venue, int BottomGate)> rungPlan)
    {
        var ended = rows.Where(r => r.EndingDay > 0).ToList();
        var stalled = rows.Where(r => r.EndingDay == 0).ToList();
        var endingDays = ended.Select(r => r.EndingDay).OrderBy(d => d).ToList();

        var w = new StringWriter(CultureInfo.InvariantCulture);
        w.WriteLine($"# arc-stall — {policyTag} policy, seeds {startSeed}..{startSeed + (ulong)rows.Count - 1}, {days} days");
        w.WriteLine();
        w.WriteLine("The ladder, and the bottom-floor gate each rung must beat to graduate onto the next:");
        w.WriteLine();
        foreach (var (rank, venue, gate) in rungPlan)
        {
            w.WriteLine($"- rank {rank} -> `{venue.Id}` floor {venue.FloorCount}, gate **{gate}**");
        }

        w.WriteLine();
        w.WriteLine($"The Ending fires {ArcDirectorSystem.EndingDelayDays} days after the top rung's bottom floor falls.");
        w.WriteLine();
        w.WriteLine($"- reached an ending: **{ended.Count}/{rows.Count}**");
        w.WriteLine($"- stalled (no ending in {days} days): **{stalled.Count}/{rows.Count}**");
        if (endingDays.Count > 0)
        {
            w.WriteLine($"- ending day: min {endingDays[0]}, median {endingDays[endingDays.Count / 2]}, max {endingDays[^1]}");
        }

        if (stalled.Count > 0)
        {
            var byRung = stalled
                .GroupBy(r => r.WallRung?.Rank ?? -1)
                .OrderBy(g => g.Key)
                .Select(g => $"rank {g.Key}: {g.Count()}");
            w.WriteLine($"- stalls by wall rung — {string.Join(", ", byRung)}");
        }

        w.WriteLine();
        w.WriteLine("## Every seed's wall rung");
        w.WriteLine();
        w.WriteLine("| seed | ending | maxRank | wall rung | venue | peak power | bottom gate | days at/over gate | attempts | halts | best floor | shop sales (1st 30d / last 30d) | deaths |");
        w.WriteLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var r in rows)
        {
            var wall = r.WallRung;
            w.WriteLine($"| {r.Seed} | {(r.EndingDay > 0 ? r.EndingDay.ToString(CultureInfo.InvariantCulture) : "**none**")} | "
                + $"{r.MaxLadderRank} | {wall?.Rank.ToString(CultureInfo.InvariantCulture) ?? "-"} | {wall?.VenueId ?? "-"} | "
                + $"{wall?.PeakPower ?? 0} | {wall?.BottomGate ?? 0} | "
                + $"{wall?.DaysAtOrOverGate ?? 0}/{wall?.DaysWithParty ?? 0} | {wall?.Attempts ?? 0} | "
                + $"{(wall is null ? "-" : Halts(wall, "<br>"))} | {wall?.BestFloorCleared ?? 0} | "
                + $"{r.ShopSales} ({r.ShopSalesFirst30} / {r.ShopSalesLast30}) | {r.Deaths} |");
        }

        if (stalled.Count > 0)
        {
            w.WriteLine();
            w.WriteLine("## Stalled seeds, rung by rung");
            w.WriteLine();
            foreach (var r in stalled)
            {
                w.WriteLine($"### seed {r.Seed}");
                w.WriteLine();
                w.WriteLine("| rank | venue | days with a party | first party day | peak power | last power | bottom gate | days at/over gate | attempts | halts | best floor |");
                w.WriteLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");
                foreach (var rung in r.Rungs)
                {
                    w.WriteLine($"| {rung.Rank} | {rung.VenueId} | {rung.DaysWithParty} | {rung.FirstPartyDay} | "
                        + $"{rung.PeakPower} | {rung.LastPower} | {rung.BottomGate} | {rung.DaysAtOrOverGate} | "
                        + $"{rung.Attempts} | {Halts(rung, "<br>")} | {rung.BestFloorCleared} |");
                }

                w.WriteLine();
            }
        }

        return w.ToString();
    }
}
