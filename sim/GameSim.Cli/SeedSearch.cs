using System.Collections.Immutable;
using GameSim;
using GameSim.Contracts;
using GameSim.Harness;

namespace GameSim.Cli;

/// <summary>
/// P2-ONBOARD-04 (docs/design/MAKERS-MARK.md §11.15, "P2-ONBOARD — the guided rework and The
/// Warrant"): the Warrant's seed search. A DATA TOOL, not a gate (the Characterize/ConsequenceProbe/
/// BatchRunner precedent) — asserts nothing, prints a table, exits 0.
///
/// <see cref="Evaluate"/> is the criteria the plan names, each cited to the one recorded
/// <see cref="GameEvent"/> it reads — nothing here approximates a criterion no event carries
/// (P2-ONBOARD-04's own instruction). Two modes:
///
/// <c>seed-search --seeds N [--seed S] [--days D]</c> sweeps N seeds under
/// <see cref="ApprenticePlayer"/> (P2-ONBOARD-03) and prints every seed's pass/fail row plus the
/// seeds that clear all seven — this is the reproducible search command the pin's own re-run
/// ceremony (§11.15) points at when a rules change shifts the stream.
///
/// <c>seed-search --perturb SEED [--days D]</c> takes one seed the sweep already found and replays
/// it under three deliberately deviating scripts, then prints which criteria survive EVERY
/// deviation (the TRIMMED set the player-facing fiction may promise) beside the full set the base
/// script alone produces (the UNTRIMMED set). The plan's prose names its three deviations as "skip
/// the bounty; skip the counter; craft nothing on day 1" — <see cref="ApprenticePlayer"/> never
/// submits a <c>PostBountyAction</c> in the first place (grep its source: no call site), so "skip
/// the bounty" is a no-op against this harness. <see cref="SkipCommissions"/> substitutes the
/// closest REAL deviation the script has: refusing the one other channel-verb
/// <see cref="ApprenticePlayer"/> actually performs.
/// </summary>
public static class SeedSearch
{
    public const string Usage =
        "usage: seed-search --seeds <count> [--seed <startSeed>] [--days <days>]\n" +
        "       seed-search --perturb <seed> [--days <days>]";

    private static readonly HeroId Torvald = new(1);

    public enum Mode
    {
        Sweep,
        Perturb,
    }

    public sealed record Args(Mode RunMode, int SeedCount, ulong StartSeed, ulong PerturbSeed, int Days);

    /// <summary>The Warrant's seven criteria (§11.15), each a bool read straight off
    /// <see cref="GameState.EventLog"/> — see <see cref="Evaluate"/> for the exact event each one
    /// cites.</summary>
    public sealed record CriteriaResult(
        bool Day1MusterWithTorvald,
        bool Day2Camp,
        bool Day3AnswerableCommission,
        bool Day2FirstBeatNoDeathSharingNight,
        bool OneDeath4To6NotTorvaldNotWipe,
        bool DeepCamp5Or6,
        bool NeverDestitute)
    {
        public bool AllPass =>
            Day1MusterWithTorvald && Day2Camp && Day3AnswerableCommission
            && Day2FirstBeatNoDeathSharingNight && OneDeath4To6NotTorvaldNotWipe
            && DeepCamp5Or6 && NeverDestitute;

        /// <summary>Ordered (name, holds) pairs — the single source both the sweep row and the
        /// perturb report format from, so the two views can never drift apart on naming.</summary>
        public ImmutableList<(string Name, bool Holds)> Pairs => ImmutableList.Create(
            ("day1-muster-torvald", Day1MusterWithTorvald),
            ("day2-camp", Day2Camp),
            ("day3-answerable-commission", Day3AnswerableCommission),
            ("day2-first-beat-no-death", Day2FirstBeatNoDeathSharingNight),
            ("one-death-4to6-not-torvald-not-wipe", OneDeath4To6NotTorvaldNotWipe),
            ("deep-camp-5or6", DeepCamp5Or6),
            ("never-destitute", NeverDestitute));
    }

    public static Args? Parse(string[] args, TextWriter error)
    {
        var mode = Mode.Sweep;
        var seedCount = 200;
        var startSeed = 1UL;
        var perturbSeed = 0UL;
        var days = 7;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--seeds" when i + 1 < args.Length && int.TryParse(args[i + 1], out var n):
                    seedCount = n;
                    i++;
                    break;
                case "--seed" when i + 1 < args.Length && ulong.TryParse(args[i + 1], out var s):
                    startSeed = s;
                    i++;
                    break;
                case "--days" when i + 1 < args.Length && int.TryParse(args[i + 1], out var d):
                    days = d;
                    i++;
                    break;
                case "--perturb" when i + 1 < args.Length && ulong.TryParse(args[i + 1], out var p):
                    mode = Mode.Perturb;
                    perturbSeed = p;
                    i++;
                    break;
                default:
                    error.WriteLine($"seed-search: unknown or malformed arg '{args[i]}'");
                    error.WriteLine(Usage);
                    return null;
            }
        }

        if (mode == Mode.Sweep && seedCount <= 0)
        {
            error.WriteLine("seed-search: --seeds must be positive");
            error.WriteLine(Usage);
            return null;
        }

        if (days <= 0)
        {
            error.WriteLine("seed-search: --days must be positive");
            error.WriteLine(Usage);
            return null;
        }

        return new Args(mode, seedCount, startSeed, perturbSeed, days);
    }

    public static int Run(Args args, TextWriter output, TextWriter error) =>
        args.RunMode == Mode.Perturb ? RunPerturb(args, output) : RunSweep(args, output);

    private static int RunSweep(Args args, TextWriter output)
    {
        output.WriteLine(
            $"seed-search: sweeping {args.SeedCount} seed(s) from {args.StartSeed}, {args.Days} days, policy=apprentice");

        var winners = new List<ulong>();
        for (var i = 0; i < args.SeedCount; i++)
        {
            var seed = args.StartSeed + (ulong)i;
            var result = Evaluate(RunSeed(seed, args.Days, ApprenticePlayer.ActionsFor), args.Days);
            output.WriteLine($"  seed {seed,6}: {FormatMarks(result)} ({result.Pairs.Count(p => p.Holds)}/7)");
            if (result.AllPass)
            {
                winners.Add(seed);
            }
        }

        output.WriteLine();
        output.WriteLine(winners.Count == 0
            ? "seed-search: no seed cleared all seven criteria — widen the sweep, do not soften the criteria"
            : $"seed-search: {winners.Count} seed(s) cleared all seven: {string.Join(", ", winners)}");

        return 0;
    }

    private static int RunPerturb(Args args, TextWriter output)
    {
        var baseline = Evaluate(RunSeed(args.PerturbSeed, args.Days, ApprenticePlayer.ActionsFor), args.Days);

        var variants = new (string Name, Func<GameState, ImmutableList<PlayerAction>> Policy)[]
        {
            ("no-counter", SkipCounter),
            ("no-day1-craft", SkipDay1Craft),
            ("no-commissions", SkipCommissions),
        };

        output.WriteLine($"seed-search: perturbing seed {args.PerturbSeed} over {args.Days} days");
        output.WriteLine($"  base (apprentice, unperturbed): {FormatMarks(baseline)}");

        var trimmed = baseline;
        foreach (var (name, policy) in variants)
        {
            var result = Evaluate(RunSeed(args.PerturbSeed, args.Days, policy), args.Days);
            output.WriteLine($"  {name,-15}: {FormatMarks(result)}");
            trimmed = Intersect(trimmed, result);
        }

        output.WriteLine();
        output.WriteLine($"  UNTRIMMED (base script only):        {FormatNames(baseline)}");
        output.WriteLine($"  TRIMMED (holds under every deviation): {FormatNames(trimmed)}");

        return 0;
    }

    /// <summary>
    /// The Warrant's seven criteria (§11.15), each read straight off the tick loop's own recorded
    /// <see cref="GameEvent"/> log — never approximated. <paramref name="windowDays"/> bounds the
    /// destitution check to the curated week actually run.
    /// </summary>
    public static CriteriaResult Evaluate(ImmutableList<GameEvent> events, int windowDays)
    {
        // 1. "day 1 a party musters and Torvald marches" -> a PartyDeparted event, day 1, carrying
        // Torvald's HeroId in its roster.
        var day1Muster = events.OfType<PartyDeparted>().Any(e => e.Day == 1 && e.Party.Contains(Torvald));

        // 2. "day 2 a party camps" -> a PartyCampReport (the winch-house slate: a party parked at
        // the checkpoint, stage 2 unresolved) on day 2.
        var day2Camp = events.OfType<PartyCampReport>().Any(e => e.Day == 2);

        // 3. "day 3 dawn a commission answerable by the blacksmith" -> a CommissionPosted, day 3,
        // for any non-Consumable slot — the exact set ApprenticePlayer's own class doc says it
        // accepts ("every open GEAR commission"), not narrowed to Weapon (the one slot its
        // one-recipe menu can literally craft). A Weapon-only bar was tried first and rejected:
        // MEASURED (this unit, 2000-seed sweep) day 3 posts EXACTLY one commission on every single
        // seed, and it is ALWAYS Armor, never Weapon — CommissionSystem's gap-detection is
        // deterministic here because ApprenticePlayer's own day-1/day-2 routine saturates every
        // hero's Weapon gap by day 2, the same structural-not-seed fact that makes day1-muster,
        // day2-camp, day5-6-camp, and never-destitute all fire on every seed too. A Weapon-only bar
        // would make this criterion unsatisfiable by construction, which is a broken criterion, not
        // a narrow seed space (per this unit's own instruction: 2000 seeds yielding zero means the
        // criterion is wrong).
        var day3Commission = events.OfType<CommissionPosted>().Any(e => e.Day == 3 && e.Slot != ItemSlot.Consumable);

        // 4. "day 4 the first attribution beat lands with no death sharing its night" -> corrected
        // to day 2. The plan's day-4 target (§11.15's P2-ONBOARD row: "day 4, 12 of 12 seeds")
        // predates this unit's own measurement and describes a DIFFERENT harness (pre-ApprenticePlayer).
        // MEASURED (this unit, 2000-seed sweep): under the actual pinned script the first beat lands
        // on day 2 on EVERY seed, deterministically — the day-1 craft is delivered through an
        // already-open commission and fights by day 2, never day 3 or day 4. This unit's PR calls
        // out the plan's day-4 text as wrong for this instrument rather than silently keeping it.
        var firstBeatDay = events.OfType<AttributionBeatEvent>()
            .Select(e => (int?)e.Day)
            .OrderBy(d => d)
            .FirstOrDefault();
        var deathsOnBeatDay = firstBeatDay is { } fbd && events.OfType<HeroDied>().Any(e => e.Day == fbd);
        var day2Beat = firstBeatDay == 2 && !deathsOnBeatDay;

        // 5. "days 4-6 exactly one death, not Torvald's, not a wipe" -> exactly one HeroDied in
        // [4,6], its Hero isn't Torvald, and that day's PartyReturned (if any) isn't an empty-
        // survivors wipe.
        var deathsWindow = events.OfType<HeroDied>().Where(e => e.Day is >= 4 and <= 6).ToImmutableList();
        var wipeDays = events.OfType<PartyReturned>().Where(e => e.Survivors.IsEmpty).Select(e => e.Day).ToHashSet();
        var oneDeath = deathsWindow.Count == 1
            && deathsWindow[0].Hero != Torvald
            && !wipeDays.Contains(deathsWindow[0].Day);

        // 6. "day 5 or 6 a deep-bound camp so the runner decision has real stakes" -> another
        // PartyCampReport, day 5 or day 6. Every InFlight party is deep-bound by construction
        // (ApprenticePlayer's own class doc: ExpeditionSystem only parks a party checkpointed
        // strictly below its target), so a PartyCampReport IS a deep-bound camp.
        var deepCamp = events.OfType<PartyCampReport>().Any(e => e.Day is 5 or 6);

        // 7. "never destitute" -> no RecoveryStipendGranted (Playable Core R5/KD3's true dead-end
        // rescue) inside the curated window.
        var neverDestitute = !events.OfType<RecoveryStipendGranted>().Any(e => e.Day <= windowDays);

        return new CriteriaResult(day1Muster, day2Camp, day3Commission, day2Beat, oneDeath, deepCamp, neverDestitute);
    }

    private static string FormatMarks(CriteriaResult result) =>
        string.Join(" ", result.Pairs.Select(p => p.Holds ? "Y" : "n"));

    private static string FormatNames(CriteriaResult result) =>
        result.Pairs.Any(p => p.Holds)
            ? string.Join(", ", result.Pairs.Where(p => p.Holds).Select(p => p.Name))
            : "(none)";

    private static CriteriaResult Intersect(CriteriaResult a, CriteriaResult b) => new(
        a.Day1MusterWithTorvald && b.Day1MusterWithTorvald,
        a.Day2Camp && b.Day2Camp,
        a.Day3AnswerableCommission && b.Day3AnswerableCommission,
        a.Day2FirstBeatNoDeathSharingNight && b.Day2FirstBeatNoDeathSharingNight,
        a.OneDeath4To6NotTorvaldNotWipe && b.OneDeath4To6NotTorvaldNotWipe,
        a.DeepCamp5Or6 && b.DeepCamp5Or6,
        a.NeverDestitute && b.NeverDestitute);

    /// <summary>Deviation 1: never touch the counter (Open/Present/Haggle/Close all suppressed).
    /// GameKernel's own Advance rule only holds Morning at the counter while it is open and
    /// unclosed (<c>counter is {{ Closed: false }}</c>); with Counter left permanently null, the
    /// day-2 Morning simply falls through to Expedition on its very first tick instead of hanging.</summary>
    private static ImmutableList<PlayerAction> SkipCounter(GameState state) =>
        ApprenticePlayer.ActionsFor(state)
            .Where(a => a is not (OpenCounterAction or PresentItemAction or HaggleResponseAction or CloseCounterAction))
            .ToImmutableList();

    /// <summary>Deviation 2: no craft on day 1 specifically (every later day's craft loop runs
    /// unperturbed).</summary>
    private static ImmutableList<PlayerAction> SkipDay1Craft(GameState state) =>
        state.Day == 1
            ? ApprenticePlayer.ActionsFor(state).Where(a => a is not CraftAction).ToImmutableList()
            : ApprenticePlayer.ActionsFor(state);

    /// <summary>Deviation 3 (substituted for the plan's "skip the bounty" — see this class's own
    /// doc comment for why): never accept an open commission.</summary>
    private static ImmutableList<PlayerAction> SkipCommissions(GameState state) =>
        ApprenticePlayer.ActionsFor(state).Where(a => a is not AcceptCommissionAction).ToImmutableList();

    private static ImmutableList<GameEvent> RunSeed(
        ulong seed, int days, Func<GameState, ImmutableList<PlayerAction>> policy)
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(seed);
        var events = ImmutableList.CreateBuilder<GameEvent>();

        // BatchRunner's own loop shape (`while (state.Day <= batch.Days)`), NOT BalanceSimTests'
        // fixed `tick < Days * 5` — that fixed count assumes every day takes exactly 5 ticks, which
        // holds for BaselinePlayer (never touches the counter) but NOT for ApprenticePlayer: an open,
        // unclosed counter session HOLDS Morning for one extra tick per present/haggle/close round
        // (GameKernel.Advance, PA3/PKD5), so day 2 alone can cost 5+N ticks. A fixed 5-per-day count
        // measurably undershoots real day 7 here (verified while building this tool: with a fixed
        // 7*5=35-tick budget, day 7 hadn't even started yet) — reading state.Day directly is the only
        // correct stopping rule for a script that ever opens the counter.
        while (state.Day <= days)
        {
            var result = kernel.Tick(state, policy(state));
            state = result.NewState;
            events.AddRange(result.Events);
        }

        return events.ToImmutable();
    }
}
