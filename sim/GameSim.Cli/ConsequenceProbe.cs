using System.Collections.Immutable;
using System.Text;
using GameSim;
using GameSim.Advisor;
using GameSim.Contracts;
using GameSim.Harness;
using GameSim.Kernel;

namespace GameSim.Cli;

/// <summary>
/// Consequence probe — asks whether the game's choices MATTER, which is a different question from the
/// one <see cref="DecisionLogger"/> answers.
/// <para>
/// <see cref="DecisionLogger"/> measures the option SURFACE: how many verbs are legal at each phase,
/// which the advisor recommends, which are never reachable. All of that is breadth of menu. A phase can
/// offer twelve legal verbs and still be a one-decision phase if eleven of them change nothing — and no
/// frequency count can tell the difference, because an inert option is counted exactly like a pivotal one.
/// </para>
/// <para>
/// This probe measures CONSEQUENCE instead. Because the sim is deterministic and pure (KTD2), a decision
/// point can be forked: tick it once with a candidate action and once with NO action, then compare the
/// durable world state that results. The comparison is what makes the result trustworthy in one direction —
/// if the two fingerprints are identical, that option provably did nothing, since any real effect would
/// have to show up in gold, materials, shelf, heroes, bounties, standing, or the arc.
/// </para>
/// <para>
/// <b>What this can and cannot claim.</b> "Identical outcome ⇒ inert" is sound. The converse is NOT: a
/// differing fingerprint can come from the action shifting RNG draws rather than from the choice mattering
/// in any way a player would feel. So the headline metric here is the confound-free one — inert options and
/// equivalence classes at a one-tick horizon — and divergence is reported as a signal, never as proof of
/// causation. The treadmill and trajectory sections below are likewise confound-free: they read the baseline
/// run only and make no counterfactual claim at all.
/// </para>
/// Output is <c>consequence-report.md</c> plus a per-seed <c>probe-seed{n}.jsonl</c> under <c>&lt;outDir&gt;</c>.
/// Pure read of sim state; every tick happens on a fork, so the baseline campaign is never perturbed.
/// </summary>
public static class ConsequenceProbe
{
    /// <summary>The verb key for a player action — its type name minus the "Action" suffix, matching
    /// <see cref="DecisionLogger"/> so the two reports can be read side by side.</summary>
    private static string Verb(PlayerAction action) => action.GetType().Name.Replace("Action", string.Empty);

    /// <summary>
    /// True when <see cref="ActionLegality"/> built this candidate as a DELIBERATE no-op, so finding it
    /// inert says nothing about the game.
    /// <para>
    /// The legality enumerator has to name a concrete action to test, and for a couple of verbs the
    /// safest thing to name is the current value: it emits <c>new SetPriceAction(entry.Item, entry.Price)</c>
    /// — the price the item is already listed at — and <c>new SetProfessionsAction(SelectedProfessions)</c>,
    /// whose own comment reads "re-affirming the current selection is always legal (a no-op change)".
    /// Both are inert by construction and 100% of the time. Reported as findings they read as two
    /// completely dead verbs, which is how this probe first described them; a real player setting a
    /// DIFFERENT price obviously changes the world. They are counted and reported separately so the
    /// inert numbers stay trustworthy.
    /// </para>
    /// </summary>
    private static bool IsNoOpByConstruction(GameState s, PlayerAction a) => a switch
    {
        SetPriceAction sp => s.Player.Shelf.Any(e => e.Item == sp.Item && e.Price == sp.Price),
        SetProfessionsAction sp => sp.Professions.SetEquals(s.Player.SelectedProfessions),
        _ => false,
    };

    /// <summary>Phase order for report tables — chronological, not enum order, so a reader follows a day.</summary>
    private static readonly DayPhase[] PhaseOrder =
        [DayPhase.Morning, DayPhase.Expedition, DayPhase.Camp, DayPhase.ExpeditionDeep, DayPhase.Evening];

    /// <summary>A per-phase tally of how consequential its choices turned out to be.</summary>
    private sealed class PhaseTally
    {
        public long DecisionPoints;
        public long OptionsProbed;
        public long InertOptions;
        /// <summary>Candidates excluded from the inert count because the legality enumerator built them
        /// as no-ops — see <see cref="IsNoOpByConstruction"/>. Surfaced so the exclusion is visible.</summary>
        public long NoOpByConstruction;
        /// <summary>Decision points offering 2+ options that ALL landed in one outcome class — the
        /// shape worth designing away, since the player is picking between identical futures.</summary>
        public long TheaterPoints;
        /// <summary>Decision points offering 2+ options — the divisor for <see cref="ClassSum"/>, since a
        /// single-option point has nothing to choose between and would drag the average toward 1.</summary>
        public long MultiOptionPoints;
        public long ClassSum;
        public int ClassMin = int.MaxValue;
        public int ClassMax;
        /// <summary>Verbs that were probed at this phase and never once changed the world.</summary>
        public readonly Dictionary<string, (long Probed, long Inert)> ByVerb = [];
    }

    public static int Run(int seedCount, ulong startSeed, int days, string outDir, TextWriter output, TextWriter error)
    {
        try
        {
            Directory.CreateDirectory(outDir);
        }
        catch (Exception ex)
        {
            error.WriteLine($"probe: cannot create '{outDir}': {ex.Message}");
            return 1;
        }

        var kernel = GameComposition.BuildKernel();
        var tallies = new Dictionary<DayPhase, PhaseTally>();
        var goldByDay = new Dictionary<int, List<int>>();
        var treadmill = new List<string>();
        var seedRows = new List<string>();
        var tradeRows = new List<string>();

        for (var i = 0; i < seedCount; i++)
        {
            var seed = startSeed + (ulong)i;
            var state = GameComposition.NewCampaign(seed);
            var jsonl = new StringBuilder();

            // Legal-verb signature per day, to measure how fast the offered menu stops changing.
            var daySignature = new Dictionary<int, SortedSet<string>>();

            // Does the shop actually SELL? A near-zero gold balance is ambiguous on its own, because
            // BaselinePlayer buys ore greedily whenever it can afford to and so spends down to the floor
            // every morning by design — "broke" and "fully reinvested" look identical in the balance. What
            // separates them is whether stock ever leaves the shelf. Shelf entries are tracked by item id
            // between ticks: an id that was shelved and is now gone was bought by a hero (this policy
            // never unstocks), so vanished ids are sales.
            var everShelved = new HashSet<int>();
            var previousShelf = new HashSet<int>();
            var sold = 0;
            var goldEarned = 0;
            var previousGold = state.Player.Gold;

            while (state.Day <= days)
            {
                var phase = state.Phase;
                var legal = ActionLegality.LegalActions(state, phase);

                if (!daySignature.TryGetValue(state.Day, out var sig))
                {
                    daySignature[state.Day] = sig = [];
                }

                foreach (var a in legal)
                {
                    sig.Add($"{phase}:{Verb(a)}");
                }

                if (!tallies.TryGetValue(phase, out var tally))
                {
                    tallies[phase] = tally = new PhaseTally();
                }

                tally.DecisionPoints++;

                // The control: what the world looks like after this phase if the player does NOTHING.
                // Every candidate is measured against this, so "inert" means indistinguishable from
                // having skipped the choice entirely.
                var doNothing = Fingerprint(kernel.Tick(state, ImmutableList<PlayerAction>.Empty).NewState);
                // Outcome classes over the OPTIONS ONLY (the do-nothing control is not something the
                // player can pick): how many genuinely different futures this point offers.
                var optionOutcomes = new HashSet<string>();
                var inertHere = new List<string>();

                foreach (var option in legal)
                {
                    var verb = Verb(option);
                    var after = Fingerprint(kernel.Tick(state, ImmutableList.Create(option)).NewState);
                    var inert = after == doNothing;

                    optionOutcomes.Add(after);

                    if (IsNoOpByConstruction(state, option))
                    {
                        // Measured, but kept out of the inert findings — see IsNoOpByConstruction.
                        tally.NoOpByConstruction++;
                        continue;
                    }

                    tally.OptionsProbed++;
                    var byVerb = tally.ByVerb.GetValueOrDefault(verb);
                    tally.ByVerb[verb] = (byVerb.Probed + 1, byVerb.Inert + (inert ? 1 : 0));

                    if (inert)
                    {
                        tally.InertOptions++;
                        inertHere.Add(verb);
                    }
                }

                var distinct = optionOutcomes.Count;

                if (legal.Count >= 2)
                {
                    tally.MultiOptionPoints++;
                    tally.ClassSum += distinct;
                    tally.ClassMin = Math.Min(tally.ClassMin, distinct);
                    tally.ClassMax = Math.Max(tally.ClassMax, distinct);

                    if (distinct <= 1)
                    {
                        tally.TheaterPoints++;
                    }
                }

                jsonl.Append("{\"seed\":").Append(seed)
                    .Append(",\"day\":").Append(state.Day)
                    .Append(",\"phase\":\"").Append(phase).Append('"')
                    .Append(",\"options\":").Append(legal.Count)
                    .Append(",\"outcomeClasses\":").Append(distinct)
                    .Append(",\"inert\":[").Append(string.Join(",", inertHere.Distinct().Select(v => $"\"{v}\"")))
                    .Append("]}\n");

                var chosen = BaselinePlayer.ActionsFor(state);
                var beforeDay = state.Day;
                state = kernel.Tick(state, chosen).NewState;

                var shelfNow = state.Player.Shelf.Select(e => e.Item.Value).ToHashSet();
                sold += previousShelf.Count(id => !shelfNow.Contains(id));
                everShelved.UnionWith(shelfNow);
                previousShelf = shelfNow;

                // Gross income, not the net balance: every gold the player took IN over the whole run.
                if (state.Player.Gold > previousGold)
                {
                    goldEarned += state.Player.Gold - previousGold;
                }

                previousGold = state.Player.Gold;

                if (state.Day != beforeDay)
                {
                    if (!goldByDay.TryGetValue(beforeDay, out var golds))
                    {
                        goldByDay[beforeDay] = golds = [];
                    }

                    golds.Add(state.Player.Gold);
                }
            }

            var path = Path.Combine(outDir, $"probe-seed{seed}-days{days}.jsonl");
            try
            {
                File.WriteAllText(path, jsonl.ToString());
            }
            catch (Exception ex)
            {
                error.WriteLine($"probe: write failed for '{path}': {ex.Message}");
                return 1;
            }

            // Treadmill: how long the offered menu goes without changing at all.
            var dayKeys = daySignature.Keys.OrderBy(d => d).ToList();
            var identicalPairs = 0;
            var longestRun = 1;
            var run = 1;
            for (var d = 1; d < dayKeys.Count; d++)
            {
                var same = daySignature[dayKeys[d]].SetEquals(daySignature[dayKeys[d - 1]]);
                if (same)
                {
                    identicalPairs++;
                    run++;
                    longestRun = Math.Max(longestRun, run);
                }
                else
                {
                    run = 1;
                }
            }

            var pairs = Math.Max(1, dayKeys.Count - 1);
            treadmill.Add($"| {seed} | {dayKeys.Count} | {identicalPairs * 100 / pairs}% | {longestRun} |");
            tradeRows.Add($"| {seed} | {everShelved.Count} | {sold} | {goldEarned} | {goldEarned / Math.Max(1, days)} |");
            seedRows.Add($"| {seed} | {state.Player.Gold} | {state.Arc.Act} | "
                + $"{state.Heroes.Values.Count(h => h.Alive)}/{state.Heroes.Count} |");
            output.WriteLine($"  seed {seed}: probed {days} days, menu unchanged on {identicalPairs * 100 / pairs}% "
                + $"of consecutive days (longest identical run {longestRun}) -> {path}");
        }

        var reportPath = Path.Combine(outDir, "consequence-report.md");
        try
        {
            File.WriteAllText(reportPath, BuildReport(
                seedCount, startSeed, days, tallies, goldByDay, treadmill, seedRows, tradeRows));
        }
        catch (Exception ex)
        {
            error.WriteLine($"probe: report write failed: {ex.Message}");
            return 1;
        }

        var totalProbed = tallies.Values.Sum(t => t.OptionsProbed);
        var totalInert = tallies.Values.Sum(t => t.InertOptions);
        output.WriteLine($"consequence probe: {totalProbed} options probed, {totalInert} provably inert "
            + $"({(totalProbed > 0 ? totalInert * 100 / totalProbed : 0)}%) -> {reportPath}");
        return 0;
    }

    /// <summary>
    /// A fingerprint of the DURABLE world, taken by serializing the WHOLE state through
    /// <see cref="SaveCodec"/> and neutralising only the fields that must not count as an effect.
    /// <para>
    /// Serializing everything is the point. The first version of this method hand-listed the fields it
    /// thought mattered — and silently omitted <c>Drama</c>, which is where memorials, commissions and
    /// rivalry live. The probe duly reported <c>HonorMemorial</c> as inert 330 times out of 330 while the
    /// handler was demonstrably setting <c>Memorial.Honored = true</c> the whole time. A hand-listed
    /// fingerprint does not just risk that mistake, it guarantees it the moment anyone adds a field to
    /// <see cref="GameState"/>, and the failure is silent and looks like a game bug. Going through the
    /// save codec means new contract fields are covered the day they land.
    /// </para>
    /// Deliberate neutralisations, each for a specific reason:
    /// <list type="bullet">
    ///   <item><c>Rng</c> — an action that only burns entropy has not changed the world.</item>
    ///   <item><c>EventLog</c> / <c>ActionLog</c> — both record the ATTEMPT, so leaving them in would
    ///     make even a flatly refused action look consequential.</item>
    ///   <item><c>ActionSlotsRemaining</c> — spending a scarce slot and achieving nothing is precisely
    ///     the theater this probe exists to find, so it must not register as an effect.</item>
    /// </list>
    /// <c>Day</c> and <c>Phase</c> need no handling: every fork advances exactly one tick from the same
    /// state, so they always match.
    /// </summary>
    private static string Fingerprint(GameState s) =>
        SaveCodec.Serialize(s with
        {
            Rng = NeutralRng,
            EventLog = ImmutableList<GameEvent>.Empty,
            ActionLog = ImmutableList<LoggedBatch>.Empty,
            ActionSlotsRemaining = 0,
        });

    /// <summary>A fixed RNG value substituted into every fingerprint, so two states differing only in
    /// how much entropy they consumed compare equal. Never used to draw anything.</summary>
    private static readonly RngState NeutralRng = new(0UL, 1UL);

    /// <summary>Test seam for <see cref="Fingerprint"/>. Exposed because the fingerprint's completeness
    /// is the one property this whole tool rests on, and the way it failed before was silent — asserting
    /// on a summary percentage would have described the symptom instead of naming the blind spot.</summary>
    internal static string FingerprintForTests(GameState state) => Fingerprint(state);

    /// <summary>Test seam for <see cref="IsNoOpByConstruction"/>.</summary>
    internal static bool IsNoOpByConstructionForTests(GameState state, PlayerAction action) =>
        IsNoOpByConstruction(state, action);

    private static string BuildReport(
        int seedCount, ulong startSeed, int days,
        Dictionary<DayPhase, PhaseTally> tallies,
        Dictionary<int, List<int>> goldByDay,
        List<string> treadmill,
        List<string> seedRows,
        List<string> tradeRows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Maker's Mark — Consequence Report");
        sb.AppendLine();
        sb.AppendLine($"Auto-generated by `GameSim.Cli probe`. **{seedCount} seed(s)** (start {startSeed}) × "
            + $"**{days} days**. For every decision point, every legal option was applied to a FORK of the "
            + "state and the resulting durable world compared against a do-nothing control.");
        sb.AppendLine();
        sb.AppendLine("**How to read this.** *Inert* is a proof: the option left gold, materials, shelf, "
            + "heroes, bounties, standing, venues and the arc all byte-identical to not acting at all, so it "
            + "cannot have mattered. *Outcome classes* counts how many genuinely different futures a player "
            + "can pick between at a point — 1 class with several options on offer means the choice is "
            + "theater. The reverse inference is NOT available here: two options landing in different classes "
            + "may differ only because they shifted RNG draws, so a high class count is a hint to investigate, "
            + "never proof that a decision is interesting.");
        sb.AppendLine();

        sb.AppendLine("## Does the choice matter? (by phase)");
        sb.AppendLine();
        sb.AppendLine("| Phase | decision points | options probed | provably inert | theater points | avg outcome classes | min | max | excluded (no-op by construction) |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
        foreach (var phase in PhaseOrder)
        {
            if (!tallies.TryGetValue(phase, out var t) || t.DecisionPoints == 0)
            {
                continue;
            }

            var avgClasses = t.MultiOptionPoints == 0
                ? "—"
                : $"{(double)t.ClassSum / t.MultiOptionPoints:F1}";
            var inertPct = t.OptionsProbed > 0 ? $" ({t.InertOptions * 100 / t.OptionsProbed}%)" : string.Empty;
            sb.AppendLine($"| {phase} | {t.DecisionPoints} | {t.OptionsProbed} | {t.InertOptions}{inertPct} "
                + $"| {t.TheaterPoints} | {avgClasses} | {(t.ClassMin == int.MaxValue ? "—" : t.ClassMin.ToString())} "
                + $"| {t.ClassMax} | {t.NoOpByConstruction} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Verbs that never changed anything");
        sb.AppendLine();
        sb.AppendLine("A verb offered to the player that left the world identical to inaction EVERY time it "
            + "was probed. These are worse than the dead options in the decision-surface report: a dead "
            + "option is at least honest about being unavailable, whereas these are presented as choices.");
        sb.AppendLine();
        sb.AppendLine("| Phase | verb | times offered | times inert |");
        sb.AppendLine("|---|---|---|---|");
        var foundAlwaysInert = false;
        foreach (var phase in PhaseOrder)
        {
            if (!tallies.TryGetValue(phase, out var t))
            {
                continue;
            }

            foreach (var (verb, counts) in t.ByVerb.OrderByDescending(kv => kv.Value.Inert))
            {
                if (counts.Probed > 0 && counts.Inert == counts.Probed)
                {
                    sb.AppendLine($"| {phase} | {verb} | {counts.Probed} | {counts.Inert} (always) |");
                    foundAlwaysInert = true;
                }
            }
        }

        if (!foundAlwaysInert)
        {
            sb.AppendLine("| — | *none — every offered verb changed the world at least once* | — | — |");
        }

        sb.AppendLine();
        sb.AppendLine("## Verbs that sometimes do nothing");
        sb.AppendLine();
        sb.AppendLine("Offered, and inert on some occasions but not all — usually a precondition the UI does "
            + "not surface, so the player learns the verb is unreliable rather than learning the rule.");
        sb.AppendLine();
        sb.AppendLine("| Phase | verb | times offered | times inert |");
        sb.AppendLine("|---|---|---|---|");
        var foundPartial = false;
        foreach (var phase in PhaseOrder)
        {
            if (!tallies.TryGetValue(phase, out var t))
            {
                continue;
            }

            foreach (var (verb, counts) in t.ByVerb.OrderByDescending(kv => kv.Value.Inert))
            {
                if (counts.Inert > 0 && counts.Inert < counts.Probed)
                {
                    sb.AppendLine($"| {phase} | {verb} | {counts.Probed} | {counts.Inert} ({counts.Inert * 100 / counts.Probed}%) |");
                    foundPartial = true;
                }
            }
        }

        if (!foundPartial)
        {
            sb.AppendLine("| — | *none* | — | — |");
        }

        sb.AppendLine();
        sb.AppendLine("## Treadmill — how fast the menu stops changing");
        sb.AppendLine();
        sb.AppendLine("The set of legal verbs offered across a whole day, compared to the day before. A high "
            + "identical-day percentage means the game stops presenting new situations; the longest run is "
            + "how many days in a row the player saw the exact same menu. Confound-free: this reads the "
            + "baseline run only and makes no counterfactual claim.");
        sb.AppendLine();
        sb.AppendLine("| seed | days | consecutive days with an identical menu | longest identical run |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var row in treadmill)
        {
            sb.AppendLine(row);
        }

        sb.AppendLine();
        sb.AppendLine("## Gold trajectory (median across seeds)");
        sb.AppendLine();
        sb.AppendLine("Where the economy actually goes over a long run under the default policy. A flat line "
            + "means the player's work does not compound; a runaway line means the sinks are too weak.");
        sb.AppendLine();
        sb.AppendLine("| day | median gold | min | max |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var day in goldByDay.Keys.OrderBy(d => d))
        {
            // Every 5th day plus the first and last, so a 100-day run stays readable.
            var isEdge = day == goldByDay.Keys.Min() || day == goldByDay.Keys.Max();
            if (!isEdge && day % 5 != 0)
            {
                continue;
            }

            var golds = goldByDay[day].OrderBy(g => g).ToList();
            sb.AppendLine($"| {day} | {golds[golds.Count / 2]} | {golds[0]} | {golds[^1]} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Does the shop sell? (trade volume)");
        sb.AppendLine();
        sb.AppendLine("A near-zero gold balance does NOT by itself mean the player is broke: the default "
            + "policy buys ore greedily whenever it can afford to, so it spends down to the floor every "
            + "morning and \"bankrupt\" looks identical to \"fully reinvested\" in the balance alone. This "
            + "table is what distinguishes them. Items sold counts shelf entries that vanished (this policy "
            + "never unstocks, so a vanished entry was bought); gold earned is GROSS income across the run, "
            + "not the net balance.");
        sb.AppendLine();
        sb.AppendLine("> **Read this before concluding anything about the economy.** A low number here is "
            + "currently NOT evidence that the economy is broken, because the harness refuses most of the "
            + "crafts the game offers it. `BaselinePlayer` gates its Expedition craft on "
            + "`have >= recipe.MaterialQuantity`, while the kernel's own `ActionLegality.CraftLegal` gates on "
            + "`have >= max(1, MaterialQuantity - efficiency)` — the Material Efficiency talent saves one "
            + "material. Measured over 40 days: the kernel allowed a craft the policy refused on **52% of "
            + "Expedition ticks for seed 2026 and 90% for seed 2027**. The defect bites hardest at exactly "
            + "`quantity - 1` held, which is where a materials-poor run parks, so it manufactures a perfect "
            + "correlation between low gold and no crafting with no causal link between them. Fixing it is a "
            + "re-baseline event (golden replay + every balance baseline), so it is deliberately not done "
            + "here — but until it is, treat these figures as a measurement of the POLICY, not the game.");
        sb.AppendLine();
        sb.AppendLine("| seed | items ever shelved | items sold | gold earned (gross) | gold earned / day |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var row in tradeRows)
        {
            sb.AppendLine(row);
        }

        sb.AppendLine();
        sb.AppendLine("## Campaign outcome per seed");
        sb.AppendLine();
        sb.AppendLine("| seed | final gold | final act | heroes alive / roster |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var row in seedRows)
        {
            sb.AppendLine(row);
        }

        sb.AppendLine();
        sb.AppendLine("*Choices are the deterministic `BaselinePlayer` policy, not a human. The counterfactual "
            + "forks above are policy-independent — they enumerate what the game OFFERS and whether taking it "
            + "changes anything — while the trajectory and outcome tables describe how one default agent fares.*");
        return sb.ToString();
    }
}
