using System.Collections.Immutable;
using System.Linq;
using System.Text;
using GameSim.Advisor;
using GameSim.Contracts;
using GameSim.Harness;
using GameSim.Kernel;
using GameSim.Professions;

namespace GameSim.Cli;

/// <summary>
/// P2-HONEST-28 measurement sweep (docs/design/MAKERS-MARK.md §11.11) — NOT a gate, one-off like
/// <see cref="LongWallSweep"/>/<see cref="FeltWallSweep"/>/<see cref="ArcStallSweep"/>, whose
/// machinery (<see cref="GameComposition"/>, <see cref="BatchRunner"/>'s policy axis) this reuses.
///
/// <para><b>What it answers.</b> `CLAUDE.md`'s fourth decision is "spend the slot or bank it," but
/// <see cref="ActionBudget.SlotsPerDay"/> resets every day with no carry-over — so "bank it"
/// currently means "lose it." Whether that is a real decision or a false claim in the text depends
/// entirely on whether the budget ever binds: if most days spend fewer than 5 slots, carry-over
/// would change nothing, because there was nothing left to bank in the first place.</para>
///
/// <para><b>How it counts a day's real spend without touching the kernel.</b> Deliberately reads
/// <see cref="GameKernel.Tick"/> as a black box rather than instrumenting it (P2-HONEST-28 is a
/// measure-first unit; <c>ActionBudget.cs</c> lives in the deny-listed <c>Contracts/</c> and no
/// kernel change is in scope regardless of what this finds). Every handler that decrements
/// <see cref="GameState.ActionSlotsRemaining"/> does so IFF the action both (a) is
/// <see cref="ActionBudget.ConsumesSlot"/> and (b) was not rejected — see
/// <c>CraftingHandlers.cs</c>, <c>OreMarketHandlers.cs</c>, etc., which all gate the decrement
/// behind the same `state.ActionSlotsRemaining &lt;= 0` check before it ever runs. This sweep
/// reconstructs that exact predicate from the outside: for every action the day's policy actually
/// SUBMITTED, count it as a spent slot when it consumes one AND does not appear in that tick's
/// <see cref="TickResult.Rejected"/> list. <see cref="PlayerAction"/> subtypes are records
/// (value equality), and matching removes ONE instance per hit (list, not set) so two identical
/// consuming actions submitted in the same tick can't both cancel against a single rejection.</para>
///
/// <para><b>Why every harness policy, not just baseline — plus one that is not a harness policy at
/// all.</b> A policy that mostly idles and a policy that hand-forges hard answer "does the budget
/// bind" very differently, so this sweeps every policy <see cref="BatchRunner"/> already knows how
/// to drive, at its default (average) hand. But every one of those policies is self-limiting — each
/// checks <c>state.ActionSlotsRemaining</c> before adding a consuming action, so none of them can
/// ever demonstrate genuinely REFUSED work (they never even attempt a 6th). `sim/GameSim.Tests`'s
/// own fast-lane regression, <c>SixDilemmasLivenessTests.TheActionSlotBudget_ActuallyBinds_...</c>,
/// already proves the budget CAN refuse otherwise-legal real work for an "ambitious" driver — the
/// same base actions (<see cref="BaselinePlayer"/> + <see cref="CounterPlayer"/> + a held heal
/// craft) topped up greedily to the day's cap via <see cref="ActionLegality.LegalActions"/>. This
/// sweep reuses that exact driver (see <see cref="AmbitiousActionsFor"/>, ported line-for-line from
/// that test's <c>WalkBudget</c>) as a ninth row, so the one regime the codebase already knows CAN
/// bind gets the same statistical treatment (20 seeds × 100 days, not 1 seed × 25) as the eight
/// self-limiting policies.</para>
/// </summary>
public static class SlotSpendSweep
{
    public static int Run(int seedCount, ulong startSeed, int days, string outDir, TextWriter output, TextWriter error)
    {
        try
        {
            Directory.CreateDirectory(outDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            error.WriteLine($"slot-spend: cannot create '{outDir}': {ex.Message}");
            return 1;
        }

        var kernel = GameComposition.BuildKernel();
        var policies = Enum.GetValues<BatchRunner.Policy>();
        var overallSpends = new List<int>();
        var perPolicy = new List<(string Name, List<int> Spends)>();

        foreach (var policy in policies)
        {
            var policyFn = BatchRunner.PolicyFn(policy, CraftHand.Average);
            var startingProfession = BatchRunner.PolicyStartingProfession(policy);
            var spends = MeasureDriver(kernel, policyFn, startingProfession, seedCount, startSeed, days);
            perPolicy.Add((BatchRunner.PolicyFileTag(policy), spends));
            overallSpends.AddRange(spends);
        }

        // The ninth row: not a BatchRunner policy at all, but the exact driver the fast-lane
        // regression test already uses to prove the budget CAN bind (see class doc). No starting
        // profession override — it plays the default blacksmith campaign, same as Baseline.
        var ambitiousSpends = MeasureDriver(kernel, AmbitiousActionsFor, null, seedCount, startSeed, days);
        perPolicy.Add(("ambitious", ambitiousSpends));
        overallSpends.AddRange(ambitiousSpends);

        var summary = new StringBuilder();
        summary.AppendLine("P2-HONEST-28 slot-spend sweep (measurement only, not a gate)");
        summary.AppendLine($"seeds={seedCount} startSeed={startSeed} days={days} SlotsPerDay={ActionBudget.SlotsPerDay}");
        summary.AppendLine();
        summary.AppendLine("policy         | n(days) | median | p10 | p90 | min | max | full-spend days (n, frac)");
        summary.AppendLine("---------------+---------+--------+-----+-----+-----+-----+---------------------------");
        foreach (var (name, spends) in perPolicy)
        {
            summary.AppendLine(FormatRow(name, spends));
        }

        summary.AppendLine("---------------+---------+--------+-----+-----+-----+-----+---------------------------");
        summary.AppendLine(FormatRow("ALL (combined)", overallSpends));

        var summaryPath = Path.Combine(outDir, "summary.txt");
        File.WriteAllText(summaryPath, summary.ToString());

        var overallMedian = Percentile(overallSpends, 0.5);
        var overallFullFrac = overallSpends.Count == 0
            ? 0.0
            : (double)overallSpends.Count(s => s >= ActionBudget.SlotsPerDay) / overallSpends.Count;
        output.WriteLine(
            $"slot-spend: {perPolicy.Count} drivers, {overallSpends.Count} day-samples, "
            + $"median {overallMedian}/{ActionBudget.SlotsPerDay}, full-spend {overallFullFrac:P1} -> {summaryPath}");

        return 0;
    }

    /// <summary>Ticks <paramref name="seedCount"/> seeds under <paramref name="driverFn"/> to the end
    /// of day <paramref name="days"/>, returning one int per (seed, calendar day) = that day's spent
    /// slot count (0..<see cref="ActionBudget.SlotsPerDay"/>). Follows the exact
    /// tick/day-boundary-detection shape <see cref="LongWallSweep"/> and <see cref="FeltWallSweep"/>
    /// already use: `state.Day != currentDay` at the top of the loop flushes the PREVIOUS day, and
    /// one more flush after the loop closes out the final day (the loop condition, not an in-loop
    /// flush, is what ends day <c>days</c>).</summary>
    private static List<int> MeasureDriver(
        GameKernel kernel,
        Func<GameState, ImmutableList<PlayerAction>> driverFn,
        string? startingProfession,
        int seedCount,
        ulong startSeed,
        int days)
    {
        var spends = new List<int>();

        for (var i = 0; i < seedCount; i++)
        {
            var seed = startSeed + (ulong)i;
            var state = startingProfession is null
                ? GameComposition.NewCampaign(seed)
                : GameComposition.NewCampaign(seed, startingProfession);

            var currentDay = state.Day;
            var daySpend = 0;

            while (state.Day <= days)
            {
                if (state.Day != currentDay)
                {
                    spends.Add(daySpend);
                    daySpend = 0;
                    currentDay = state.Day;
                }

                var chosen = driverFn(state);
                var result = kernel.Tick(state, chosen);

                var stillRejected = result.Rejected.Select(r => r.Action).ToList();
                foreach (var action in chosen)
                {
                    if (!ActionBudget.ConsumesSlot(action))
                    {
                        continue;
                    }

                    var idx = stillRejected.FindIndex(a => Equals(a, action));
                    if (idx >= 0)
                    {
                        stillRejected.RemoveAt(idx); // refused this tick — no slot actually spent
                    }
                    else
                    {
                        daySpend++;
                    }
                }

                state = result.NewState;
            }

            spends.Add(daySpend); // flush the final day (loop exits on state.Day > days, never in-loop)
        }

        return spends;
    }

    /// <summary>
    /// Ported line-for-line from <c>sim/GameSim.Tests/Hygiene/SixDilemmasLivenessTests.cs</c>'s
    /// <c>DriverActions</c> + <c>WalkBudget</c>'s ambitious branch — the one driver this codebase has
    /// already pinned, in a fast-lane regression test, as capable of exhausting the budget while
    /// legal real work still waits. Base actions: <see cref="BaselinePlayer"/> works the bench,
    /// <see cref="CounterPlayer"/> serves the counter, and a held heal craft covers the vigil — then
    /// topped up, in <see cref="ActionLegality.LegalActions"/>'s own deterministic order, with every
    /// remaining legal slot-consuming action the day offers, until the day's actual
    /// <see cref="GameState.ActionSlotsRemaining"/> is claimed. Reimplemented here rather than shared
    /// across the test/CLI boundary — the same reasoning <see cref="FeltWallSweep"/> and
    /// <see cref="LongWallSweep"/> already apply to their own small helpers.
    /// </summary>
    internal static ImmutableList<PlayerAction> AmbitiousActionsFor(GameState state)
    {
        var actions = BaselinePlayer.ActionsFor(state).ToBuilder();
        actions.AddRange(CounterPlayer.ActionsFor(state));
        if (state.Phase == DayPhase.Expedition && FirstLegalHealCraft(state) is { } salve)
        {
            actions.Add(salve);
        }

        var claimed = actions.Count(ActionBudget.ConsumesSlot);
        foreach (var candidate in ActionLegality.LegalActions(state, state.Phase).Where(ActionBudget.ConsumesSlot))
        {
            if (claimed >= state.ActionSlotsRemaining)
            {
                break;
            }

            if (actions.Contains(candidate))
            {
                continue;
            }

            actions.Add(candidate);
            claimed++;
        }

        return actions.ToImmutable();
    }

    /// <summary>The cheapest healing consumable the bench can actually make right now — same walk as
    /// the ported test's own helper, by recipe registry rather than a named recipe id.</summary>
    private static CraftAction? FirstLegalHealCraft(GameState state) =>
        ProfessionRegistry.AllRecipes.Values
            .Where(r => r.Effect is { Kind: ConsumableKind.Heal })
            .OrderBy(r => r.Tier)
            .ThenBy(r => r.RecipeId, StringComparer.Ordinal)
            .Select(r => new CraftAction(r.RecipeId, r.MaterialKey))
            .FirstOrDefault(c => ActionLegality.IsLegal(state, c, state.Phase));

    private static string FormatRow(string name, IReadOnlyList<int> spends)
    {
        if (spends.Count == 0)
        {
            return $"{name,-14} | n=0 (no samples)";
        }

        var sorted = spends.OrderBy(x => x).ToList();
        var median = Percentile(sorted, 0.5);
        var p10 = Percentile(sorted, 0.10);
        var p90 = Percentile(sorted, 0.90);
        var fullCount = sorted.Count(s => s >= ActionBudget.SlotsPerDay);
        var fullFrac = (double)fullCount / sorted.Count;
        return $"{name,-14} | {sorted.Count,7} | {median,6} | {p10,3} | {p90,3} | {sorted[0],3} | {sorted[^1],3} | {fullCount} ({fullFrac:P1})";
    }

    /// <summary>Nearest-rank percentile over an already-small integer domain (0..SlotsPerDay) —
    /// exact enough for a sample this size and never needs interpolation between two slot counts
    /// that aren't themselves meaningful fractions.</summary>
    private static int Percentile(IReadOnlyList<int> values, double p)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var sorted = values.OrderBy(x => x).ToList();
        var idx = (int)Math.Round(p * (sorted.Count - 1), MidpointRounding.AwayFromZero);
        idx = Math.Clamp(idx, 0, sorted.Count - 1);
        return sorted[idx];
    }
}
