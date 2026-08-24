using System.Collections.Generic;
using System.Collections.Immutable;
using GameSim;
using GameSim.Contracts;
using GameSim.Expedition;
using GameSim.Harness;

namespace GameSim.Tests.Presentation;

/// <summary>
/// U6 (§11.14.14 Wave 1, "Stop lying"): the fast-lane half of the voice-budget census the T9
/// charter named and never built. <c>MentorBanner.MentorVoiceRank</c>'s own U-T9-0 doc comment
/// measured "day 4 lands four course voices on eight of twelve seeds and five on the other four"
/// and built a QUEUE that survives a loud night; R21 (§11.14.14) is the actual target that
/// queue is a stand-in for: "No night delivers more than two act-voices; a beat whose fact lands
/// on a full night arms for the next morning at full window." This unit builds the MEASURING
/// half only — a pure-sim count of how many <c>MentorVoiceRank.Act</c>-tier facts land on
/// the same in-game day — so U29 ("The voice budget arms instead of queueing") has a real number
/// to arm against instead of a guess. This unit does not change the queue or the banner; it only
/// tells the truth about what they are currently absorbing.
///
/// <para><b>Six facts count as an act-voice</b>, matching every fact <c>MentorBanner</c>'s own
/// doc already names as a "course voice" plus the one it groups separately (hero rank-up):
/// the counterfactual attribution beat (<see cref="AttributionBeatEvent"/> — link 4 of the game's
/// five links, "a counterfactual replay of the recorded fight, with your item removed"), a hero
/// death (<see cref="HeroDied"/>), an act advance (<see cref="ActAdvanced"/>), a fulfilled
/// commission (<see cref="CommissionFulfilled"/>), the apprentice warrant ending — day
/// <see cref="ApprenticeWarrant.LastGraceDay"/> + 1, which carries no event of its own (nothing
/// mutates state on that boundary; it is a pure calendar fact the warrant's own doc already pins)
/// — and a hero rank-up (<see cref="HeroRankUp"/>).</para>
///
/// <para><b>Pure sim, not <c>runs/</c>.</b> The design doc's own U6 approach text reads "over
/// `batch --seeds N`, assert no seed and no night exceeds two act-facts" — reading serialized
/// chronicles a prior `batch` run left on disk. This census deliberately does NOT do that: a test
/// that depends on `runs/` having been populated first is an ordering hazard no other fast-lane
/// test in this repo carries, and ties a sim-purity question to a file on disk for no reason —
/// every fact counted below is already a typed <see cref="GameEvent"/> the kernel emits directly.
/// This drives <see cref="GameComposition.BuildKernel"/> itself with <see cref="BaselinePlayer"/>
/// (the same scripted policy the T9 measurement used — "Measured over twelve seeds and ten days
/// of <c>BaselinePlayer</c>", <c>MentorBanner</c>'s own doc) over twelve seeds for ten days, and
/// counts straight off <see cref="TickResult.Events"/>. No file on disk, no prior `batch` run, no
/// <c>runs/</c> dependency.</para>
///
/// <para><b>THE FINDING, not a bug in this test.</b> R21's ceiling of two is a TARGET the arming
/// rule (U29) has not built yet — today, nothing sheds a voice off a loud night, the banner's own
/// queue (U-T9-0) just absorbs the overflow. Measured here, twelve seeds/ten days of
/// <see cref="BaselinePlayer"/>, the global worst night across ALL twelve seeds and all ten days
/// is FIVE: day 4 carries the attribution beat, the fulfilled commission, the Act II advance, and
/// the warrant ending on every one of the twelve seeds (four act-voices before any hero has ever
/// died), and on 4 of the 12 seeds (99, 1234, 31337, 2026) that same day 4 also carries the
/// campaign's first hero death, for five. No later night in the ten-day window ever exceeds four —
/// days 5-10 top out at attribution-beat + commission-fulfilled + hero-rank-up + hero-died, and
/// act-advanced/warrant-ended never recur once spent. This reproduces <c>MentorBanner</c>'s own
/// U-T9-0 measurement almost exactly (four voices on 8 of 12 seeds, five on the other 4) using a
/// different seed list, which is corroboration, not coincidence. <see cref="CurrentMeasuredCeiling"/>
/// below is that measured five, not the design's target two — asserting two today would fail on
/// the very first run for a reason this unit did not cause and U29 has not yet fixed. When U29
/// lands, tighten this constant to 2 and delete this paragraph.</para>
/// </summary>
public class ActVoiceBudgetCensusTests
{
    private const int Days = 10;

    /// <summary>
    /// The current measured worst case (see the class doc's FINDING paragraph): day 4 alone
    /// stacks the attribution beat, the fulfilled commission, the Act II advance, and the warrant
    /// ending on every one of the twelve seeds below, and a fifth voice (the first hero death)
    /// joins on any seed whose earliest death also lands on day 4. R21's real target is 2; this
    /// constant is the honest ceiling until U29 (the arming rule) exists to enforce it.
    /// </summary>
    private const int CurrentMeasuredCeiling = 5;

    /// <summary>
    /// Twelve seeds (R21/U6's own "twelve seeds" framing, matching the T9 measurement this unit
    /// re-verifies): the first ten are <see cref="GameSim.Tests.Balance.BalanceSimTests"/>'
    /// own <c>SeedSweep_CoreBands_Hold</c> seeds (already-trusted, already-diverse seeds — reusing
    /// them means a balance regression and a voice-budget regression are never independently
    /// discovered from two unrelated seed lists), plus that suite's own <c>MainSeed</c> (2026) and
    /// one more (4242) to reach twelve.
    /// </summary>
    private static readonly ulong[] Seeds =
    [
        1, 7, 42, 99, 1234, 5678, 31337, 777, 2468, 13579, 2026, 4242,
    ];

    /// <summary>One (seed, in-game day) bucket's act-voice count, plus which KINDS of fact made it
    /// up (for a readable failure message — never inferred from the count alone).</summary>
    private readonly record struct NightVoices(ulong Seed, int Day, int Count, string Facts);

    /// <summary>
    /// Drives the real composed kernel with <see cref="BaselinePlayer"/> for <see cref="Days"/>
    /// days per seed and, per in-game day, records which of the six FACT KINDS occurred at least
    /// once — never a raw event count. This matters: <see cref="AttributionBeatEvent"/> alone fires
    /// three separate times on day 4 of every seed measured below (one per starting hero's gear),
    /// yet <c>MentorBanner</c>'s own U-T9-0 doc measured day 4 as carrying exactly FOUR "course
    /// voices" (Act II, the attribution beat, the fulfilled commission, the warrant's dawn) on
    /// eight of twelve seeds and five on the other four — a number only a per-KIND count produces.
    /// A voice narrates "your item mattered tonight," not "your item mattered, and here is a
    /// second, unrelated instance of the same sentence" — the game's own five links (CLAUDE.md)
    /// describe one attribution beat as one proof, not N. Confirmed against this exact repo's own
    /// measurement below (<c>Day4_CarriesTheAttributionBeat_TheCommission_AndActII_OnEverySeed</c>).
    ///
    /// <para>Five of the six facts are typed <see cref="GameEvent"/>s already stamped with the day
    /// they fired (<c>GameKernel.Tick</c> stamps <c>Day = state.Day</c> before advancing the phase,
    /// so an Evening-phase event still carries the day it closed, not the next day's dawn). The
    /// sixth — the warrant ending — has no event; <see cref="ApprenticeWarrant"/>'s own doc already
    /// establishes the harness never opts out early (<c>ApprenticeWarrantTests</c>'s own "the
    /// harness policies never submit <see cref="ConcludeApprenticeshipAction"/>" premise), so the
    /// ending is a fixed calendar fact — day <see cref="ApprenticeWarrant.LastGraceDay"/> + 1 —
    /// counted exactly once per seed.</para>
    /// </summary>
    private static ImmutableList<NightVoices> MeasureActVoicesPerNight()
    {
        var nights = ImmutableList.CreateBuilder<NightVoices>();
        var warrantEndDay = ApprenticeWarrant.LastGraceDay + 1;

        foreach (var seed in Seeds)
        {
            var kernel = GameComposition.BuildKernel();
            var state = GameComposition.NewCampaign(seed);
            var kindsByDay = new SortedDictionary<int, SortedSet<string>>();

            void Note(int day, string kind)
            {
                if (!kindsByDay.TryGetValue(day, out var kinds))
                {
                    kinds = new SortedSet<string>(StringComparer.Ordinal);
                    kindsByDay[day] = kinds;
                }

                kinds.Add(kind); // a SET: a second same-kind fact on the same day claims no extra voice
            }

            var warrantCounted = false;
            var ticks = 0;

            // A 5-phase day is the upper bound, not a guarantee — the design doc's own "zero
            // living heroes collapses Dawn straight to Night" means some days resolve in fewer
            // ticks. Bounding on state.Day (the ApprenticeWarrantTests idiom), never a fixed tick
            // count, is what stays correct either way; the ticks<... guard below is only a
            // fixture-safety backstop against a broken Advance() looping forever.
            while (state.Day <= Days && ticks < (Days * 5) + 20)
            {
                var result = kernel.Tick(state, BaselinePlayer.ActionsFor(state));
                state = result.NewState;
                ticks++;

                foreach (var evt in result.Events)
                {
                    switch (evt)
                    {
                        case AttributionBeatEvent:
                            Note(evt.Day, "attribution-beat");
                            break;
                        case HeroDied:
                            Note(evt.Day, "hero-died");
                            break;
                        case ActAdvanced:
                            Note(evt.Day, "act-advanced");
                            break;
                        case CommissionFulfilled:
                            Note(evt.Day, "commission-fulfilled");
                            break;
                        case HeroRankUp:
                            Note(evt.Day, "hero-rank-up");
                            break;
                    }
                }

                if (!warrantCounted && state.Day >= warrantEndDay)
                {
                    Note(warrantEndDay, "warrant-ended");
                    warrantCounted = true;
                }
            }

            RequireFixture(ticks is > 0 and < (Days * 5) + 20,
                $"seed {seed}: loop guard tripped (ticks={ticks}) -- either the run never advanced or " +
                "GameKernel.Advance never leaves day <= 10. Fix the harness, not this bound.");

            foreach (var (day, kinds) in kindsByDay)
            {
                nights.Add(new NightVoices(seed, day, kinds.Count, string.Join(", ", kinds)));
            }
        }

        return nights.ToImmutable();
    }

    private static void RequireFixture(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    /// <summary>
    /// The one budget check every test below funnels through (the <c>ComputeCoverageProblems</c>
    /// precedent, <c>TeachingCoverageCensusTests</c>): parameterized on the night list rather than
    /// closing over <see cref="MeasureActVoicesPerNight"/>, so the negative-path tests can drive it
    /// with a fabricated night instead of a real twelve-seed run.
    /// </summary>
    private static List<string> FindNightsOverBudget(IEnumerable<NightVoices> nights, int budget) =>
        nights.Where(n => n.Count > budget)
            .Select(n => $"seed {n.Seed} day {n.Day}: {n.Count} act-voices (budget {budget}) -- {n.Facts}")
            .ToList();

    [Fact]
    public void NoSeedAndNoNight_ExceedsTheCurrentMeasuredCeiling()
    {
        var nights = MeasureActVoicesPerNight();

        // Denominator guard (the green-54 lesson): a census that measured nothing passes forever.
        // Twelve seeds times up to ten days is at most 120 buckets; real play never reaches zero.
        RequireFixture(nights.Count > 0,
            "MeasureActVoicesPerNight() returned zero (seed, day) buckets across twelve seeds -- " +
            "the harness is broken, not the budget.");

        var violations = FindNightsOverBudget(nights, CurrentMeasuredCeiling);

        Assert.True(violations.Count == 0,
            $"A night exceeded the current measured ceiling of {CurrentMeasuredCeiling} act-voices " +
            "(R21's real target is 2; U29 has not landed yet -- see this class's own FINDING doc " +
            "paragraph). Either the ceiling constant is stale, or a change genuinely made a night " +
            "louder than any of the twelve seeds below have ever measured:\n  " +
            string.Join("\n  ", violations));
    }

    /// <summary>
    /// Sanity-checks this file's own doc claims against the measured run, so a change to
    /// <see cref="BaselinePlayer"/> or the arc/commission/warrant timing that quietly moves these
    /// facts off day 4 is caught here rather than only showing up as a drifted doc comment
    /// (CLAUDE.md rule 8: git/measurement outranks a doc, and this is the test that keeps the doc
    /// honest instead of merely asserting it once and never again).
    /// </summary>
    [Fact]
    public void Day4_CarriesTheAttributionBeat_TheCommission_AndActII_OnEverySeed()
    {
        var nights = MeasureActVoicesPerNight();
        var warrantEndDay = ApprenticeWarrant.LastGraceDay + 1;

        foreach (var seed in Seeds)
        {
            var day4 = nights.FirstOrDefault(n => n.Seed == seed && n.Day == warrantEndDay);
            Assert.True(day4.Count > 0, $"seed {seed}: no act-voice landed on day {warrantEndDay} at all.");

            Assert.True(day4.Facts.Contains("attribution-beat"),
                $"seed {seed} day {warrantEndDay}: expected the counterfactual attribution beat. Got: {day4.Facts}");
            Assert.True(day4.Facts.Contains("commission-fulfilled"),
                $"seed {seed} day {warrantEndDay}: expected a fulfilled commission. Got: {day4.Facts}");
            Assert.True(day4.Facts.Contains("act-advanced"),
                $"seed {seed} day {warrantEndDay}: expected the Act II advance. Got: {day4.Facts}");
            Assert.True(day4.Facts.Contains("warrant-ended"),
                $"seed {seed} day {warrantEndDay}: expected the warrant to end here. Got: {day4.Facts}");
        }
    }

    [Fact]
    public void FindNightsOverBudget_FlagsASyntheticNightThatExceedsTheBudget()
    {
        var nights = ImmutableList.Create(
            new NightVoices(Seed: 1, Day: 4, Count: 3, Facts: "attribution-beat, hero-died, act-advanced"));

        var violations = FindNightsOverBudget(nights, budget: 2);

        Assert.Single(violations);
        Assert.Contains("seed 1 day 4", violations[0]);
    }

    [Fact]
    public void FindNightsOverBudget_PassesWhenEveryNightIsAtOrUnderTheBudget()
    {
        var nights = ImmutableList.Create(
            new NightVoices(Seed: 1, Day: 4, Count: 2, Facts: "attribution-beat, act-advanced"));

        Assert.Empty(FindNightsOverBudget(nights, budget: 2));
    }
}
