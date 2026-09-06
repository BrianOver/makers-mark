using System.Collections.Concurrent;
using System.Collections.Immutable;
using GameSim;
using GameSim.Contracts;
using GameSim.Harness;
using GameSim.Professions;
using Xunit.Abstractions;

namespace GameSim.Tests.Balance;

/// <summary>
/// P2-OQ12: the balance gate, driven by a hand that actually plays the craft minigame.
///
/// <para><b>The gap this closes, in #722's own words.</b> "The 100-day balance gate has never been
/// run with a minigame-playing policy as a fixture. Every Balance driver is
/// <see cref="BaselinePlayer"/> (11 of 12) or <see cref="MasterworkSeekingPlayer"/>, and both
/// auto-craft, so the gate certifies this curve against nothing." Re-verified before writing this
/// file rather than taken on trust, and the gap is real and slightly wider than that framing: every
/// Balance driver crafts off <c>RecipeTable.All</c>, every row of which is blacksmith, so the
/// three scorers #722 changed were not merely auto-crafted past, they were <b>unreachable</b> on
/// every trace in the corpus. #722's 68/68 was therefore evidence of no collateral damage and
/// nothing else.</para>
///
/// <para><b>Why the campaign can disagree with the scorer, which is the whole point of running
/// it.</b> <c>Crafting/CraftCurveTests</c> already pins every property below at the SCORER level:
/// one recipe at a time, jitter zeroed, talents supplied as a set, no economy. Four campaign-level
/// forces sit between that and a grade a hero actually receives, and none of them are visible from
/// there:
/// <list type="bullet">
///   <item><description><b>The material ceiling.</b> <see cref="Crafting.QualityRoller.RollActive"/>
///   caps the band at Fine when the material is a grade below the recipe tier, and at Superior when
///   it merely matches. A campaign whose economy only ever affords the cheap material is capped at
///   Fine no matter how perfect the hand — the curve would be flawless and the top grade still
///   unreachable in play. A scorer test cannot see that, because a scorer never buys
///   anything.</description></item>
///   <item><description><b>The talent tree's real pace.</b> The scorer test asks for "no talents"
///   and "all talents". A campaign has to afford them, one action slot a morning, against every
///   other call on the same slot.</description></item>
///   <item><description><b>Which recipes actually get made.</b> The policy crafts what the roster
///   will buy at a tier it can reach, so the campaign's recipe mix is chosen by the economy. A
///   craft's top grade can be reachable on its widest puzzle and never once reached, because the
///   campaign only ever makes the narrow ones.</description></item>
///   <item><description><b>Jitter.</b> Every craft draws one <c>Roll100</c> worth +/-25 per-mille.
///   A grade sitting on a band seam reads as one band in the scorer test and as both in
///   play.</description></item>
/// </list></para>
///
/// <para><b>What this asserts, and what it deliberately does not.</b> Properties, never instances.
/// Pinning the measured grade shares here would turn every future retune into a false failure, and
/// would be one more instrument reading recorded as a fact — the error this whole line of work
/// exists to undo. So the contracts are the three §11.7.12 actually established — monotone in
/// skill, top grade earned by a skilled hand and never handed to an indifferent one, no craft
/// punishing — each phrased against the property, and each carrying the #722 measurement it would
/// have caught. Everything measured is REPORTED (<see cref="ITestOutputHelper"/>) rather than
/// asserted, so a retune shows up as a changed report and only a broken contract goes red.</para>
///
/// <para><b>Every registered profession, never a hand-listed subset.</b> The fixture table is
/// checked against <see cref="ProfessionRegistry.All"/>, so a fifth profession cannot join the game
/// without either joining this gate or turning it red. That check exists because this repo has
/// already shipped a whole asset family untested under a green suite whose guard iterated a literal
/// id array.</para>
/// </summary>
public class CraftCurveBalanceTests
{
    private const int Days = 100;

    /// <summary>The window the "master's ordinary day" property is measured over: the back half of
    /// the campaign, by which point every fixture has finished its assist tree — asserted by
    /// <see cref="EveryFixtureFinishesItsAssistTree_SoTheLateWindowReallyIsAMastersWork"/> rather
    /// than assumed.</summary>
    private const int LateWindowFirstDay = 51;

    /// <summary>
    /// Seeds for the three crafts <c>#722</c> actually changed. Five, not one: these are the curves
    /// this gate exists to certify, nothing else pins them at the campaign level, and a single
    /// campaign's recipe mix is one trajectory — a property that only held on seed 2026 would be an
    /// instrument reading. Drawn from the corpus's own seed set (<see cref="BalanceSimTests"/>' main
    /// seed and four of <see cref="ForgeTierProgressionBalanceTests"/>', including the two that file
    /// names as known composed-world misses — a marginal economy is exactly where a curve property
    /// is worth checking).
    ///
    /// <para>The blacksmith column runs on ONE seed instead, and that is not this rule being bent:
    /// see <see cref="ArchetypeSeeds"/> for why an unchanged scorer already pinned recipe-by-recipe
    /// elsewhere needs a different amount of campaign evidence than a changed one that is pinned
    /// nowhere else.</para>
    /// </summary>
    private static readonly ulong[] ChangedCraftSeeds = [2026UL, 1UL, 42UL, 7UL, 99UL];

    /// <summary>
    /// The seed for the blacksmith column — one, deliberately, and owner-ruled 2026-09-05.
    ///
    /// <para><b>Why one is the right number HERE and five is the right number above.</b>
    /// <c>Crafting/CraftCurveTests</c> already pins every blacksmith property deterministically over
    /// every recipe at every tier, with no seed and no campaign at all. What this column adds is the
    /// one thing that file cannot show — that the archetype survives a real economy — and a single
    /// 100-day campaign shows it. Full seed coverage stays on the three crafts <c>#722</c> actually
    /// changed, which is where a regression would appear.</para>
    ///
    /// <para><b>The cost that decided it, counted in campaigns rather than clocked.</b>
    /// <see cref="HandForgePlayer"/> composes over <see cref="BaselinePlayer"/>, whose campaign is
    /// the corpus's richest — ~19k events and 20 heroes over 100 days, against an alchemy
    /// campaign's ~5.5k and 12. At two seeds the blacksmith was <b>6 of this file's 51 campaigns
    /// and cost more than the other 45 put together</b> (measured by deleting the column: 1m00s
    /// against the full 2m40s in Release). One seed makes it 3 of 48, and every property still
    /// holds with the widest margins in the table.</para>
    ///
    /// <para><b>Why that is stated as campaigns and not as a saved wall clock — a correction to
    /// this file's own first receipt.</b> The trim was originally argued from <c>balance-sim</c>'s
    /// CI wall clock, 19m39s against 28m29s, read as a 45% tax. <b>That comparison was unsound</b>:
    /// it put one post-change sample against the FASTEST pre-change one. Nine runs of that job on
    /// main span 19m39s to 39m22s across code that does not differ by anything close to that, and
    /// the trimmed build then came back at 29m06s — SLOWER than the two-seed run it improves on.
    /// Local runs are quieter but not clean either: this same suite in Debug measured 5m25s and
    /// 7m47s on identical code, minutes apart, while other work shared the machine. So no wall
    /// clock here is load-bearing; the campaign count is, because it is what the code actually
    /// does. The ruling stands undisturbed either way, since its reason was never the timing:
    /// <c>CraftCurveTests</c> pins the blacksmith recipe-by-recipe already. Recorded rather than
    /// quietly dropped — a receipt that does not match the tree is the failure this whole line of
    /// work exists to undo, and it does not stop being one when it is mine.</para>
    ///
    /// <para>The seed count is a cost dial, never a property dial: every contract below is asserted
    /// identically in every cell, the report names the seed count behind each row, and the
    /// blacksmith's margins are the widest in the table.</para>
    /// </summary>
    private static readonly ulong[] ArchetypeSeeds = [2026UL];

    private static readonly CraftHand[] Ladder =
        [CraftHand.Indifferent, CraftHand.Average, CraftHand.Skilled];

    private readonly ITestOutputHelper _output;

    public CraftCurveBalanceTests(ITestOutputHelper output) => _output = output;

    // ================================================================================
    // The fixtures: one minigame-playing policy per registered profession
    // ================================================================================

    /// <summary>A profession, the campaign it needs, and the policy that plays its minigame.
    /// <c>StartingProfession</c> is null for the blacksmith alone — it is the default campaign, and
    /// each other craft needs its own profession selected from day 1 (a save holds at most 1-2; see
    /// <c>PuzzleCraftPlayer</c>'s class doc in <c>Harness/ActiveProfessionPlayer.cs</c>).</summary>
    private sealed record CraftFixture(
        string Profession,
        string? StartingProfession,
        Func<GameState, CraftHand, ImmutableList<PlayerAction>> Policy,
        ulong[] Seeds);

    private static readonly ImmutableArray<CraftFixture> Fixtures =
    [
        new(AlchemyProfession.Id, AlchemyProfession.Id, AlchemyPuzzlePlayer.ActionsFor, ChangedCraftSeeds),
        new(EngineeringProfession.Id, EngineeringProfession.Id, EngineeringPuzzlePlayer.ActionsFor, ChangedCraftSeeds),
        new(TanningProfession.Id, TanningProfession.Id, TanningPuzzlePlayer.ActionsFor, ChangedCraftSeeds),

        // The archetype, and the control: #722 changed nothing in ForgeScorer, and this column is
        // where that claim gets tested end to end instead of argued. HandForgePlayer composes over
        // BaselinePlayer on the DEFAULT blacksmith campaign.
        new(ProfessionRegistry.BlacksmithId, null, HandForgePlayer.ActionsFor, ArchetypeSeeds),
    ];

    // ================================================================================
    // The measurement
    // ================================================================================

    /// <summary>What one (craft, hand) cell produced across all of its seeds. Grades are counted,
    /// never collapsed into one opaque number — <see cref="MeanGradePermille"/> is derived from the
    /// histogram, so the report shows both.</summary>
    private sealed record Reading(
        int Campaigns,
        int Crafts,
        ImmutableSortedDictionary<QualityGrade, int> Grades,
        int LateCrafts,
        ImmutableSortedDictionary<QualityGrade, int> LateGrades,
        int MinAssistsAtEnd,
        int AssistNodeCount,
        int MinTalentsAtEnd,
        int TalentNodeCount)
    {
        public double ShareOf(QualityGrade grade) =>
            Crafts == 0 ? 0.0 : 100.0 * Grades.GetValueOrDefault(grade) / Crafts;

        public double ShareAtLeast(QualityGrade grade) =>
            Crafts == 0 ? 0.0 : 100.0 * Grades.Where(g => g.Key >= grade).Sum(g => g.Value) / Crafts;

        public double LateShareAtLeast(QualityGrade grade) =>
            LateCrafts == 0 ? 0.0 : 100.0 * LateGrades.Where(g => g.Key >= grade).Sum(g => g.Value) / LateCrafts;

        public int CountOf(QualityGrade grade) => Grades.GetValueOrDefault(grade);

        /// <summary>Where the average craft sits on the 5-band ladder, in per-mille (Poor 0 ..
        /// Masterwork 1000). A summary of the WHOLE distribution rather than of its top band — the
        /// statistic that can see a curve go flat in the middle, which a top-grade share alone
        /// cannot.</summary>
        public int MeanGradePermille() => Crafts == 0
            ? 0
            : Grades.Sum(g => (int)g.Key * g.Value) * 1000 / (Crafts * (int)QualityGrade.Masterwork);

        /// <summary>A complete, structural rendering of this reading — every field, nothing
        /// hand-picked. Records holding an <see cref="ImmutableSortedDictionary{TKey,TValue}"/>
        /// compare those by REFERENCE (the type has no structural equality), so the compiler's own
        /// <c>Equals</c> reports two identical runs as different and the determinism check below
        /// would silently become a test of object identity. A partial fingerprint is the other half
        /// of the same trap: this repo has already shipped a "did anything change?" tool that
        /// serialized a hand-listed field set and read a real state change as a game bug.</summary>
        public string Fingerprint() =>
            $"campaigns={Campaigns} crafts={Crafts} grades=[{Render(Grades)}] "
            + $"late={LateCrafts} lateGrades=[{Render(LateGrades)}] "
            + $"assists={MinAssistsAtEnd}/{AssistNodeCount} talents={MinTalentsAtEnd}/{TalentNodeCount}";

        private static string Render(ImmutableSortedDictionary<QualityGrade, int> grades) =>
            string.Join(",", grades.Select(g => $"{g.Key}:{g.Value}"));
    }

    /// <summary>Memoized so that asserting six properties still costs one pass over the grid rather
    /// than six. Every entry is a pure function of (craft, hand) — same seeds, same policy, same
    /// kernel — so caching can change the wall clock and nothing else.</summary>
    private static readonly ConcurrentDictionary<(string Profession, CraftHand Hand), Lazy<Reading>> Cache = new();

    private static Reading Read(CraftFixture fixture, CraftHand hand) =>
        Cache.GetOrAdd(
            (fixture.Profession, hand),
            _ => new Lazy<Reading>(() => Measure(fixture, hand), LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    private static Reading Measure(CraftFixture fixture, CraftHand hand)
    {
        var kernel = GameComposition.BuildKernel();
        var definition = ProfessionRegistry.All[fixture.Profession];
        var grades = new SortedDictionary<QualityGrade, int>();
        var lateGrades = new SortedDictionary<QualityGrade, int>();
        var crafts = 0;
        var lateCrafts = 0;
        var minAssists = int.MaxValue;
        var minTalents = int.MaxValue;

        foreach (var seed in fixture.Seeds)
        {
            var state = fixture.StartingProfession is null
                ? GameComposition.NewCampaign(seed)
                : GameComposition.NewCampaign(seed, fixture.StartingProfession);

            for (var tick = 0; tick < Days * 5; tick++) // 5-phase day, this corpus's own convention
            {
                var day = state.Day;
                var result = kernel.Tick(state, fixture.Policy(state, hand));
                state = result.NewState;

                foreach (var crafted in result.Events.OfType<ItemCrafted>())
                {
                    crafts++;
                    grades[crafted.Quality] = grades.GetValueOrDefault(crafted.Quality) + 1;
                    if (day >= LateWindowFirstDay)
                    {
                        lateCrafts++;
                        lateGrades[crafted.Quality] = lateGrades.GetValueOrDefault(crafted.Quality) + 1;
                    }
                }
            }

            var talents = state.Player.TalentsFor(fixture.Profession);
            minTalents = Math.Min(minTalents, talents.Count);
            minAssists = Math.Min(minAssists, definition.MinigameAssists.Keys.Count(talents.Contains));
        }

        return new Reading(
            fixture.Seeds.Length,
            crafts,
            grades.ToImmutableSortedDictionary(),
            lateCrafts,
            lateGrades.ToImmutableSortedDictionary(),
            minAssists == int.MaxValue ? 0 : minAssists,
            definition.MinigameAssists.Count,
            minTalents == int.MaxValue ? 0 : minTalents,
            definition.TalentNodes.Count);
    }

    private void Report(string title)
    {
        _output.WriteLine($"{title} — {Days}-day campaigns, seed count per row");
        _output.WriteLine(
            "craft         hand         seeds  crafts  Poor  Common  Fine  Super    Mw     %Mw  %>=Fine  mean‰  assists  talents");
        foreach (var fixture in Fixtures)
        {
            foreach (var hand in Ladder)
            {
                var r = Read(fixture, hand);
                _output.WriteLine(
                    $"{fixture.Profession,-13} {hand,-11} {r.Campaigns,5}  {r.Crafts,6}  "
                    + $"{r.CountOf(QualityGrade.Poor),4}  {r.CountOf(QualityGrade.Common),6}  "
                    + $"{r.CountOf(QualityGrade.Fine),4}  {r.CountOf(QualityGrade.Superior),5}  "
                    + $"{r.CountOf(QualityGrade.Masterwork),5}  "
                    + $"{r.ShareOf(QualityGrade.Masterwork),6:F1}  {r.ShareAtLeast(QualityGrade.Fine),7:F1}  "
                    + $"{r.MeanGradePermille(),5}  {r.MinAssistsAtEnd,4}/{r.AssistNodeCount}  "
                    + $"{r.MinTalentsAtEnd,4}/{r.TalentNodeCount}");
            }
        }
    }

    // ================================================================================
    // The contracts
    // ================================================================================

    [Fact]
    [Trait("Category", "Balance")]
    public void EveryRegisteredProfession_HasAMinigamePlayingFixtureInThisGate()
    {
        var covered = Fixtures.Select(f => f.Profession).OrderBy(p => p, StringComparer.Ordinal).ToList();
        var registered = ProfessionRegistry.All.Keys.OrderBy(p => p, StringComparer.Ordinal).ToList();

        Assert.True(
            covered.SequenceEqual(registered, StringComparer.Ordinal),
            $"this gate drives [{string.Join(", ", covered)}] but the game registers "
            + $"[{string.Join(", ", registered)}] — a profession whose scorer no balance fixture ever "
            + "reaches is precisely the state #722 shipped the quality curve in, and the fix is a "
            + "fixture, never a wider list here.");
    }

    /// <summary>Minimum crafts a (craft, hand) cell must produce before its distribution means
    /// anything. A vacuity floor, not a tuning band: measured, the thinnest cell in the grid is
    /// alchemy's (its Heal brews only have a buyer when no Heal is already shelved, which throttles
    /// the craft rate hard), and this sits below it with room. A cell that falls under it has a
    /// broken economy or a policy that can no longer afford its own materials — either is a finding,
    /// never a number to lower.</summary>
    private const int MinCraftsPerCell = 40;

    [Fact]
    [Trait("Category", "Balance")]
    public void EveryFixture_ActuallyCraftsAtVolume_SoNoShareBelowIsAnInstrumentReading()
    {
        Report("non-vacuity");

        foreach (var fixture in Fixtures)
        {
            foreach (var hand in Ladder)
            {
                var reading = Read(fixture, hand);
                Assert.True(
                    reading.Crafts >= MinCraftsPerCell,
                    $"{fixture.Profession}/{hand}: {reading.Crafts} crafts over {reading.Campaigns} "
                    + $"{Days}-day campaigns — below {MinCraftsPerCell}, every share this class "
                    + "reports is an instrument reading rather than a measurement, and the "
                    + "properties below would pass vacuously.");
                Assert.True(
                    reading.LateCrafts > 0,
                    $"{fixture.Profession}/{hand}: nothing was crafted from day {LateWindowFirstDay} "
                    + "on — the campaign stopped making things before the window the mastery "
                    + "properties measure.");
            }
        }
    }

    [Fact]
    [Trait("Category", "Balance")]
    public void BetterHandsMakeBetterWork_MonotoneInSkill_InEveryCraft()
    {
        Report("monotone in skill");

        foreach (var fixture in Fixtures)
        {
            var ladder = Ladder.Select(h => (Hand: h, Reading: Read(fixture, h))).ToList();

            // Adjacent rungs never invert. This is what Engineering failed outright before #722: an
            // INDIFFERENT hand took the top grade on 54.3% of its crafts against an average hand's
            // 17.8%, because a derived schematic had period 2 and the build-order bonus paid for
            // tidiness regardless of correctness. A worse hand making better work is the one thing
            // the ruling forbids outright, and it is checked on the whole distribution AND on the
            // top band, because a curve can invert in the middle without moving its top.
            for (var rung = 1; rung < ladder.Count; rung++)
            {
                var worse = ladder[rung - 1];
                var better = ladder[rung];

                Assert.True(
                    better.Reading.MeanGradePermille() >= worse.Reading.MeanGradePermille(),
                    $"{fixture.Profession}: a {better.Hand} hand averaged "
                    + $"{better.Reading.MeanGradePermille()}‰ on the grade ladder, BELOW the "
                    + $"{worse.Hand} hand's {worse.Reading.MeanGradePermille()}‰ — a worse hand made "
                    + $"better work across {Days} days of real play.");

                // Cross-multiplied rather than compared as percentages: the two cells have
                // different craft counts, and this is the repo's own integer idiom for comparing
                // two ratios (ShoppingAi ranks gain-per-gold the same way) — no float anywhere in
                // the decision, only in the sentence that explains it.
                Assert.True(
                    (long)better.Reading.CountOf(QualityGrade.Masterwork) * worse.Reading.Crafts
                        >= (long)worse.Reading.CountOf(QualityGrade.Masterwork) * better.Reading.Crafts,
                    $"{fixture.Profession}: a {better.Hand} hand took the top grade on "
                    + $"{better.Reading.ShareOf(QualityGrade.Masterwork):F1}% of its crafts against "
                    + $"the {worse.Hand} hand's {worse.Reading.ShareOf(QualityGrade.Masterwork):F1}% "
                    + "— skill bought a worse chance at the top grade. Compared strictly, with no "
                    + "tolerance: two adjacent rungs do walk genuinely different economies (better "
                    + "work sells for more, which changes what the next ninety days are worth "
                    + "making), but measured, no rung in this grid inverts by so much as a tenth of "
                    + "a point, while #722's Engineering inverted by 36.5.");
            }

            // ...and skill has to buy something REAL, end to end. This is what Tanning failed before
            // #722, and failed silently: all three hands scored identically, to the digit, so a
            // merely non-decreasing check would have called a completely skill-blind craft healthy.
            var indifferent = ladder[0].Reading;
            var skilled = ladder[^1].Reading;
            Assert.True(
                skilled.MeanGradePermille() > indifferent.MeanGradePermille(),
                $"{fixture.Profession}: a skilled hand averaged {skilled.MeanGradePermille()}‰ and an "
                + $"indifferent one {indifferent.MeanGradePermille()}‰ — accuracy bought nothing "
                + "across a whole campaign. That is the skill-blind failure #722 found in Tanning, "
                + "and a non-decreasing check cannot see it.");
        }
    }

    [Fact]
    [Trait("Category", "Balance")]
    public void TopGradeIsEarnedByAccuracy_ReachableBySkill_AndNeverByAnIndifferentHand()
    {
        Report("top grade earned by accuracy");

        foreach (var fixture in Fixtures)
        {
            var skilled = Read(fixture, CraftHand.Skilled);
            Assert.True(
                skilled.CountOf(QualityGrade.Masterwork) > 0,
                $"{fixture.Profession}: a skilled hand never once produced the top grade in "
                + $"{skilled.Crafts} crafts across {skilled.Campaigns} {Days}-day campaigns. The "
                + "scorer can be perfect and this still fail — the material ceiling caps a craft at "
                + "Fine whenever the campaign only ever affords a material a grade below the recipe, "
                + "and no scorer test can see that.");

            var indifferent = Read(fixture, CraftHand.Indifferent);
            Assert.True(
                indifferent.CountOf(QualityGrade.Masterwork) == 0,
                $"{fixture.Profession}: a hand that ignores everything the puzzle shows it took the "
                + $"top grade {indifferent.CountOf(QualityGrade.Masterwork)} times in "
                + $"{indifferent.Crafts} crafts — the top grade is being handed over rather than "
                + "earned. Tanning did exactly this before #722 (87.3% Masterwork from day 6 under a "
                + "hand that never once looked at the hide).");
        }
    }

    [Fact]
    [Trait("Category", "Balance")]
    public void EveryFixtureFinishesItsAssistTree_SoTheLateWindowReallyIsAMastersWork()
    {
        Report("mastery reached");

        foreach (var fixture in Fixtures)
        {
            foreach (var hand in Ladder)
            {
                var reading = Read(fixture, hand);

                // The ASSIST nodes, not the whole tree: those are the ones the curve actually reads
                // (talent forgiveness is applied after CraftCurve, per its own class doc), so they
                // are what makes the late window "a master's work" for the property below. The rest
                // of the tree is reported next to them for context and deliberately not asserted —
                // the blacksmith's tier-3 unlock is Forge-Tier-gated and, measured, one seed never
                // affords it, which is ForgeTierProgressionBalanceTests' finding to own, not this
                // file's to re-litigate.
                Assert.True(
                    reading.MinAssistsAtEnd == reading.AssistNodeCount,
                    $"{fixture.Profession}/{hand}: the worst seed finished {Days} days with "
                    + $"{reading.MinAssistsAtEnd} of {reading.AssistNodeCount} minigame-assist "
                    + "talents. The late-window property below is then not measuring a master, and "
                    + "the campaign cannot afford its own mastery — a finding about the economy, "
                    + "never a number to lower.");
            }
        }
    }

    /// <summary>How often a mastered ORDINARY hand's work must grade Fine or better. A floor under
    /// "decent", not a pin on the measurement: measured, every craft sits far above it (the report
    /// shows where), and the number exists so that a retune making an ordinary day mostly Common
    /// goes red instead of passing quietly.</summary>
    private const double OrdinaryDayFineFloorPercent = 80.0;

    [Fact]
    [Trait("Category", "Balance")]
    public void NoCraftIsPunishing_AMastersOrdinaryDayIsStillDecent()
    {
        Report("no craft is punishing");

        foreach (var fixture in Fixtures)
        {
            var ordinary = Read(fixture, CraftHand.Average);
            Assert.True(
                ordinary.LateShareAtLeast(QualityGrade.Fine) >= OrdinaryDayFineFloorPercent,
                $"{fixture.Profession}: with the assist tree full, an ORDINARY hand's work grades "
                + $"Fine or better only {ordinary.LateShareAtLeast(QualityGrade.Fine):F1}% of the "
                + $"time (day {LateWindowFirstDay}+). The ruling's goal is that skill matters, not "
                + "that the game gets harder — a mastered crafter having a normal day should still "
                + "make something decent.");

            Assert.True(
                ordinary.LateGrades.GetValueOrDefault(QualityGrade.Poor) == 0,
                $"{fixture.Profession}: a mastered ordinary hand still produced "
                + $"{ordinary.LateGrades.GetValueOrDefault(QualityGrade.Poor)} Poor items after day "
                + $"{LateWindowFirstDay} — the bottom of the scale must be out of reach of a player "
                + "who is trying and has paid for the whole tree.");
        }
    }

    [Fact]
    [Trait("Category", "Balance")]
    public void TheseCampaignsAreDeterministic()
    {
        // Cache-free on purpose: two independent runs of the same cell, compared whole. The
        // memoized reading every other test uses would only ever compare a value to itself.
        //
        // One cell, and the cheapest one: the kernel's own determinism is already pinned by the
        // golden-replay test, so what is owed HERE is only that this file's instrument — a policy
        // driven at a fixed CraftHand — draws nothing of its own. That is a property of the policy
        // family, not of the profession, and re-proving it on the blacksmith column would cost most
        // of a minute to learn the same thing.
        var fixture = Fixtures[0];
        Assert.Equal(
            Measure(fixture, CraftHand.Skilled).Fingerprint(),
            Measure(fixture, CraftHand.Skilled).Fingerprint());
    }
}
