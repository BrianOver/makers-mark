#if GDUNIT_TESTS
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GameSim;
using GameSim.Contracts;
using GdUnit4;
using Godot;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U33 (§11.14.14, R24): her graduation goodbye, and the rank her whole arc is supposed to speak at.
///
/// <para><b>What this unit actually found.</b> <c>ActVoiceKind.Graduation</c> was declared by U29
/// and had no production producer at all — <c>PendingActVoiceCandidates</c> never added it, so its
/// only references outside <c>TutorialFlow</c> were synthetic literals in a precedence test. U32
/// then made the course's own end event-shaped and persisted (<c>Completed</c>), and nothing ever
/// spoke over it: the mentor's arc ended in silence and her de facto last word was a quick-travel
/// tooltip. The unit's second half was described as re-ranking two lines from Lesson to Act; on
/// measurement the cold open was ALREADY Act rank (shipped that way in U16, <c>MainUi</c>'s
/// <c>FirstMorningBeatPending</c> block passes <c>rank: MentorVoiceRank.Act</c>) and only the greedy
/// shelf rule was still taking <c>ShowFirstTouch</c>'s Lesson default. <see
/// cref="EveryArcLineCallSite_SpeaksAtActRank_NeverLesson"/> is the census that makes that claim
/// checkable rather than remembered.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class BrynGraduationGoodbyeTests
{
    /// <summary>One distinctive phrase from the goodbye, shared by both variants — used to pick her
    /// farewell out of a banner that may be carrying other lines the same night. Deliberately a
    /// fragment of the PLAN's own quoted line (§11.15), so a rewrite that drifts off the plan's
    /// image trips this rather than passing quietly.</summary>
    private const string GoodbyeSignature = "the bench was only ever borrowed";

    // ── 1. The goodbye exists, fires on the logged fact, and reaches the screen at Act rank ──────

    /// <summary>A campaign at Evening holding a signed, player-marked legend item — the exact fact
    /// U32's Memory row arms on (<c>LegendsWall.HasPlayerMarkedRecord</c>), and therefore the
    /// shortest honest route to a graduated course.</summary>
    private static GameState SignedLegendItemStateAtEvening(int day)
    {
        var baseState = GameComposition.NewCampaign(4242);
        var item = new Item(
            new ItemId(9401), "test-recipe", "Test Farewell Blade", ItemSlot.Weapon, QualityGrade.Masterwork,
            new ItemStats(Attack: 5, Defense: 0, Weight: 1), new MakersMark("You", 1),
            ImmutableList<ItemHistoryEntry>.Empty)
        {
            SignedName = "Test Legend",
        };

        return baseState with
        {
            Day = day,
            Phase = DayPhase.Evening,
            Items = baseState.Items.Add(item.Id.Value, item),
        };
    }

    /// <summary>Settles the Memory act's row the same day it arms, which is what makes <see
    /// cref="TutorialFlow.Completed"/> true (U32's own event-shaped graduation — <c>Advance</c>'s
    /// own U32 block). Asserts the fact it is setting up, so a later silent failure reads as "the
    /// course never graduated" rather than "the goodbye is broken".</summary>
    private static void GraduateTheCourse(MainUi ui)
    {
        ui.Tutorial.Advance(ui.Adapter.CurrentState); // arms the Memory row
        ui.Tutorial.NotifyLegendsWallOpened();        // read the same day -> Done
        ui.Tutorial.Advance(ui.Adapter.CurrentState);

        AssertThat(ui.Tutorial.Completed)
            .OverrideFailureMessage(
                "Setup check: the course never graduated, so nothing below can say whether the " +
                "goodbye fires. U32's Memory-row graduation is broken, not this unit's line.")
            .IsTrue();
    }

    /// <summary>Drives the Return Ritual's own delayed Evening reveal to completion — same helper
    /// shape <c>WaveDLessonsTests.DriveTheEveningLedgerReveal</c> already uses, waiting on the
    /// CONDITION rather than a guessed frame count (the "frame count is not a duration" rule).</summary>
    private static void DriveTheEveningLedgerReveal(MainUi ui)
    {
        for (var i = 0; i < 600 && ui.LedgerDelayRemaining > 0; i++)
        {
            ui._Process(0.016);
        }

        AssertThat(ui.LedgerDelayRemaining)
            .OverrideFailureMessage(
                "Setup check: the Evening ledger never revealed, so no act voice could have spoken. " +
                "The reveal itself is broken, not the goodbye.")
            .IsEqual(0.0);
    }

    /// <summary>The headline behaviour, end to end through the REAL wiring: a course that graduates
    /// gets a spoken farewell on the screen, in her voice, at <see cref="MentorVoiceRank.Act"/> —
    /// never queued at Lesson rank behind whatever tool tip happened to arrive first. Reads the
    /// banner's own persisted snapshot rather than only the currently-visible line, so a night that
    /// carries two act voices still proves this one's rank.</summary>
    [TestCase]
    public void Graduation_SpeaksOnTheReveal_AtActRank()
    {
        var ui = MountMainUi(new SimAdapter(SignedLegendItemStateAtEvening(day: 4)));
        try
        {
            GraduateTheCourse(ui);

            ui.Adapter.AdvancePhase(); // completes Evening -> arms the Return Ritual
            DriveTheEveningLedgerReveal(ui);

            var spoken = ui.Mentor.SnapshotForPersistence();
            var goodbye = spoken.FirstOrDefault(l => l.Text.Contains(GoodbyeSignature, StringComparison.Ordinal));

            AssertThat(goodbye.Text)
                .OverrideFailureMessage(
                    "The course graduated and Bryn said nothing. Lines actually on the banner: " +
                    (spoken.Count == 0 ? "(none)" : string.Join(" | ", spoken.Select(l => l.Text))))
                .IsNotEmpty();

            AssertThat(goodbye.Text.Contains(MentorVoice.Name, StringComparison.Ordinal))
                .OverrideFailureMessage($"The farewell is unattributed: \"{goodbye.Text}\"")
                .IsTrue();

            AssertThat(goodbye.Rank)
                .OverrideFailureMessage(
                    $"Her arc's last line is ranked {goodbye.Rank}, not Act — U33's own verification " +
                    "is \"none at lesson rank\".")
                .IsEqual(MentorVoiceRank.Act);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Armed on the LOGGED fact and nothing else: silent for as long as the course has not
    /// graduated, and once ever after. The once-ever half is the 1287x memorial-nag precedent every
    /// dormant act in this file carries.</summary>
    [TestCase]
    public void GraduationBeat_IsSilentBeforeTheFact_AndSpeaksExactlyOnceAfterIt()
    {
        var ui = MountMainUi(new SimAdapter(SignedLegendItemStateAtEvening(day: 4)));
        try
        {
            AssertThat(ui.Tutorial.Completed)
                .OverrideFailureMessage("Setup check: this campaign starts already graduated.")
                .IsFalse();
            AssertThat(ui.Tutorial.ConsumeGraduationBeat(ui.Adapter.CurrentState))
                .OverrideFailureMessage("She said goodbye before the course had ended.")
                .IsNull();

            GraduateTheCourse(ui);

            AssertThat(ui.Tutorial.ConsumeGraduationBeat(ui.Adapter.CurrentState))
                .OverrideFailureMessage("The course graduated and ConsumeGraduationBeat still returned null.")
                .IsNotNull();
            AssertThat(ui.Tutorial.ConsumeGraduationBeat(ui.Adapter.CurrentState))
                .OverrideFailureMessage("Her farewell re-fired on a second call — the anti-nag contract is broken.")
                .IsNull();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Law 7 (skipping stays legal, and costs only what the copy names): a player who
    /// dismissed the course never hears her farewell, and dismissing costs them nothing else here.
    /// This is also the door a returning smith comes through — <c>ResetForReturningSmith</c> writes
    /// <c>Dismissed = true</c> — so it is the gate that keeps a veteran from being handed the
    /// goodbye of a mentor they declined to meet.</summary>
    [TestCase]
    public void ADismissedCourse_NeverHearsTheGoodbye_EvenPastTheBackstop()
    {
        var ui = MountMainUi(new SimAdapter(SignedLegendItemStateAtEvening(day: 4)));
        try
        {
            ui.Tutorial.Dismiss();

            var wellPastTheBackstop = ui.Adapter.CurrentState with { Day = TutorialFlow.ChainBackstopDay + 5 };
            ui.Tutorial.Advance(wellPastTheBackstop);

            AssertThat(ui.Tutorial.Completed)
                .OverrideFailureMessage("A dismissed course graduated — Dismissed and Completed are no longer exclusive.")
                .IsFalse();
            AssertThat(ui.Tutorial.ConsumeGraduationBeat(wellPastTheBackstop))
                .OverrideFailureMessage("A player who dismissed her still got the farewell.")
                .IsNull();
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── 2. Precedence: the goodbye can never bin the proof or the death beat ─────────────────────

    /// <summary>
    /// The question that had to be answered BEFORE adding anything to the candidate pool: does a
    /// seventh contender ever cost the game its two most earned moments? Proven exhaustively rather
    /// than by argument — over every one of the 64 subsets of the other six kinds, adding <see
    /// cref="TutorialFlow.ActVoiceKind.Graduation"/> never removes <see
    /// cref="TutorialFlow.ActVoiceKind.Proof"/> or <see cref="TutorialFlow.ActVoiceKind.HeroDeath"/>
    /// from what the night actually seats.
    ///
    /// <para>It holds for a structural reason, not by luck, which is why it will keep holding:
    /// Graduation is declared BELOW both and the budget is two, so with HeroDeath absent Proof is
    /// the highest-ranked candidate that can exist, and with it present Proof is already removed by
    /// R21's own death/proof exclusion before precedence is consulted at all. A future reordering of
    /// the enum that broke either fact would go red here.</para>
    /// </summary>
    [TestCase]
    public void AddingTheGoodbye_NeverCostsTheProofOrTheDeathBeat_AcrossEveryCandidateSet()
    {
        TutorialFlow.ActVoiceKind[] others =
        [
            TutorialFlow.ActVoiceKind.HeroDeath, TutorialFlow.ActVoiceKind.Proof,
            TutorialFlow.ActVoiceKind.WarrantEnded, TutorialFlow.ActVoiceKind.ActAdvance,
            TutorialFlow.ActVoiceKind.CommissionFulfilled, TutorialFlow.ActVoiceKind.RankUp,
        ];

        var casesWhereOneOfThemWon = 0;

        for (var mask = 0; mask < 1 << 6; mask++)
        {
            var without = others.Where((_, i) => (mask & (1 << i)) != 0).ToList();
            var with = without.Append(TutorialFlow.ActVoiceKind.Graduation).ToList();

            var seatedWithout = TutorialFlow.ResolveTonightsActVoices(without);
            var seatedWith = TutorialFlow.ResolveTonightsActVoices(with);

            foreach (var earned in new[] { TutorialFlow.ActVoiceKind.Proof, TutorialFlow.ActVoiceKind.HeroDeath })
            {
                if (!seatedWithout.Contains(earned))
                {
                    continue;
                }

                casesWhereOneOfThemWon++;
                AssertThat(seatedWith.Contains(earned))
                    .OverrideFailureMessage(
                        $"{earned} won its slot among [{string.Join(", ", without)}] and LOST it once the " +
                        "graduation goodbye joined the pool. The farewell must never displace the two " +
                        "moments the whole game exists to produce.")
                    .IsTrue();
            }
        }

        // Denominator guard: a scan where neither kind ever won anything would pass vacuously.
        AssertThat(casesWhereOneOfThemWon)
            .OverrideFailureMessage(
                $"Only {casesWhereOneOfThemWon} candidate set(s) ever seated Proof or HeroDeath at all — " +
                "too few for a green run here to mean anything; check the sweep, not the allocator.")
            .IsGreater(40);
    }

    /// <summary>The pairwise half, stated directly against the allocator with <c>budget: 1</c> —
    /// the same idiom <c>TutorialFlowTests.Precedence_EachAdjacentPair_HigherRankWinsTheOnlySlot</c>
    /// uses, pinned here for the two pairs this unit's own addition actually creates.</summary>
    [TestCase]
    public void TheGoodbye_YieldsTheOnlySlot_ToBothTheProofAndTheDeath()
    {
        AssertThat(TutorialFlow.ResolveTonightsActVoices(
                [TutorialFlow.ActVoiceKind.Graduation, TutorialFlow.ActVoiceKind.Proof], budget: 1))
            .ContainsExactly(TutorialFlow.ActVoiceKind.Proof);

        AssertThat(TutorialFlow.ResolveTonightsActVoices(
                [TutorialFlow.ActVoiceKind.Graduation, TutorialFlow.ActVoiceKind.HeroDeath], budget: 1))
            .ContainsExactly(TutorialFlow.ActVoiceKind.HeroDeath);
    }

    // ── 3. The rank property, across her whole arc ───────────────────────────────────────────────

    /// <summary>Every <c>godot/scripts</c> source file, joined — same fixture (and same "a broken
    /// GlobalizePath would scan nothing and pass vacuously" floor) <c>FireOnOpenRetiredTests</c>
    /// already uses.</summary>
    private static string ReadMainUiSource()
    {
        var path = Path.Combine(ProjectSettings.GlobalizePath("res://scripts"), "MainUi.cs");
        var source = File.ReadAllText(path);

        if (source.Length < 50_000)
        {
            throw new InvalidOperationException(
                $"MainUi.cs read as only {source.Length} chars from {path} — GlobalizePath is resolving " +
                "somewhere unexpected, and a source census over nothing passes vacuously.");
        }

        return source;
    }

    /// <summary>The six arc lines, named by the symbol their own call site must mention. Rank is a
    /// property of the CALL SITE, not of the copy (<c>MentorBanner.ShowFirstTouch</c> defaults to
    /// Lesson), so it cannot be read off a string or a mounted campaign for the beats whose facts a
    /// test cannot cheaply manufacture — it is read off the wiring itself.</summary>
    private static readonly string[] ArcLineCallSiteSymbols =
    [
        "FirstMorningBeatId",      // beat 0, the cold open
        "ConsumeGreedyRuleLesson", // beat 3, her rule, wrong on purpose
        "ConsumeProofBeat",        // the proof night
        "LossVoiceLine",           // the death night
        "ConsumeRuleRevisedBeat",  // eating her rule
        "ConsumeGraduationBeat",   // the goodbye (this unit)
    ];

    /// <summary>
    /// U33's own verification, made a property rather than six spot checks: <b>no line of her arc
    /// resolves at Lesson rank.</b> Six separate assertions would each pass forever the moment a
    /// seventh call site got written next month; this checks the SHAPE — any <c>Mentor.Show</c> /
    /// <c>Mentor.ShowFirstTouch</c> statement that mentions one of her arc beats must name Act rank
    /// explicitly, because omitting it silently means Lesson.
    ///
    /// <para>This is also the check that corrected this unit's own brief. It was written expecting
    /// two offenders (the cold open and the greedy rule); the cold open has passed since U16.</para>
    /// </summary>
    [TestCase]
    public void EveryArcLineCallSite_SpeaksAtActRank_NeverLesson()
    {
        // Drop whole-line comments first — every doc comment in this file mentions these symbols,
        // and a census that counted prose would measure documentation rather than wiring.
        var code = string.Join(
            "\n",
            ReadMainUiSource()
                .Split('\n')
                .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        var covered = new HashSet<string>(StringComparer.Ordinal);
        var offenders = new List<string>();

        foreach (var statement in code.Split(';'))
        {
            if (!statement.Contains("Mentor.Show", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var symbol in ArcLineCallSiteSymbols.Where(s => statement.Contains(s, StringComparison.Ordinal)))
            {
                if (statement.Contains("MentorVoiceRank.Act", StringComparison.Ordinal))
                {
                    covered.Add(symbol);
                }
                else
                {
                    offenders.Add($"{symbol}: {Regex.Replace(statement.Trim(), @"\s+", " ")}");
                }
            }
        }

        AssertThat(offenders.Count == 0)
            .OverrideFailureMessage(
                "An arc line is delivered at Lesson rank (ShowFirstTouch/Show default) — U33's own " +
                "verification is \"none at lesson rank\", and a Lesson-ranked beat is the first thing " +
                "MentorBanner.Enqueue drops on a crowded night:\n  " + string.Join("\n  ", offenders))
            .IsTrue();

        // Coverage half, and the denominator guard: every symbol must actually have been FOUND at an
        // Act-ranked call site. Without this, deleting a call site (or a parse that matched nothing)
        // would read as "no offenders" and pass.
        var missing = ArcLineCallSiteSymbols.Where(s => !covered.Contains(s)).ToList();
        AssertThat(missing.Count == 0)
            .OverrideFailureMessage(
                "These arc lines have no Act-ranked Mentor.Show call site in MainUi.cs at all — either " +
                "the beat lost its voice, or this census stopped seeing it: " + string.Join(", ", missing))
            .IsTrue();
    }

    // ── 4. Influence never orders (Law 1), checked against the register ──────────────────────────

    /// <summary>
    /// The advisor register, verbatim from <c>AdvisorNeverOrdersTests.ImperativeVerbs</c>
    /// (sim/GameSim.Tests/Advisor/), plus the two verbs the PLAN's own draft of this goodbye opened
    /// clauses on — "Price them fair. Watch the wall." Both are missing from the advisor's list
    /// because the advisor's own vocabulary never needed them, but this repo already classifies
    /// "Price " as a bare imperative opener elsewhere (<c>MentorVoiceTests
    /// .FirstMorningBeatText_NeverReadsAsAnImperative_BeyondWhatTheRegisterCheckCatches</c>), so the
    /// gap is in the register, not in the judgement. Checking the shipped line against a register
    /// that happens not to contain its verbs is how a law bends while every gauge stays green.
    /// </summary>
    private static readonly HashSet<string> ImperativeVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "Accept", "Buy", "Craft", "Honor", "Shelve", "Stock", "Sell", "Post", "Send", "Unlock",
        "Upgrade", "Raise", "Go", "Take", "Use", "Equip", "Wear", "Pay", "Trade", "Visit", "Talk",
        "Recall", "Retreat", "Flee", "Attack", "Defend", "Check", "Look", "Consider", "Try", "Make",
        "Get", "Bring", "Keep", "Choose", "Pick", "Grab", "Move", "Walk", "Press", "Click",
        "Forge", "Price", "Watch", "Stamp", "Hold", "Leave", "Spend", "Trust",
    };

    /// <summary>Same clause boundary as <c>AdvisorNeverOrdersTests.ClauseSplit</c> — an imperative
    /// planted after an em dash, a semicolon or a full stop orders exactly as much as one that opens
    /// the whole line.</summary>
    private static readonly Regex ClauseSplit = new(@"(?:\. |; | — |—)", RegexOptions.Compiled);

    private static readonly Regex SecondPersonDirective = new(
        @"\byou (should|must|need to)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Both farewells she can ever speak — the variant chooser is a function of campaign
    /// facts, so a check that only read one of them would leave the other unguarded.</summary>
    private static IEnumerable<(string Label, string Text)> BothGoodbyeVariants() =>
    [
        ("no correction fired", TutorialFlow.GraduationBeatTextFor(ruleWasRevised: false)),
        ("correction fired", TutorialFlow.GraduationBeatTextFor(ruleWasRevised: true)),
    ];

    /// <summary>Law 1, against the register rather than against one sentence: neither farewell
    /// contains a second-person directive, and no clause of either opens on a bare command verb.</summary>
    [TestCase]
    public void NeitherGoodbye_EverOrdersThePlayer()
    {
        foreach (var (label, text) in BothGoodbyeVariants())
        {
            AssertThat(SecondPersonDirective.IsMatch(text))
                .OverrideFailureMessage($"Goodbye ({label}) phrases a second-person directive: \"{text}\"")
                .IsFalse();

            foreach (var clause in ClauseSplit.Split(text))
            {
                var firstWord = clause.TrimStart('*', ' ', '\'', '"')
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault()
                    ?.TrimEnd('.', ',', ':', '\'');

                if (firstWord is null)
                {
                    continue;
                }

                AssertThat(ImperativeVerbs.Contains(firstWord))
                    .OverrideFailureMessage(
                        $"Goodbye ({label}) has a clause opening on a bare command verb (\"{firstWord}\") — " +
                        $"influence never orders (Law 1). Full line: \"{text}\"")
                    .IsFalse();
            }
        }
    }

    /// <summary>
    /// KTD5 / "show only what the sim decided": the plan's own goodbye names "the one thing I taught
    /// you wrong; you caught it faster than I did" — a claim about THIS campaign that is only true
    /// where the sim actually disproved her greedy rule (<c>ConsumeRuleRevisedBeat</c> armed). A
    /// campaign that never pinned a counter close never saw that correction, so it gets the variant
    /// that makes no such claim. Same two-variant shape <c>LossVoiceLine</c> already established for
    /// the death night.
    ///
    /// <para>This is also the one place P2-ONBOARD-07's "no copy ever tells the player she was wrong"
    /// discipline permits the word at all: strictly after the sim has already proven it, never
    /// before and never instead.</para>
    /// </summary>
    [TestCase]
    public void TheGoodbyeClaimsSheWasCaughtOut_OnlyWhereTheSimActuallyProvedIt()
    {
        var ui = MountMainUi(new SimAdapter(SignedLegendItemStateAtEvening(day: 4)));
        try
        {
            var uncorrected = TutorialFlow.GraduationBeatTextFor(ruleWasRevised: false);
            var corrected = TutorialFlow.GraduationBeatTextFor(ruleWasRevised: true);

            AssertThat(uncorrected.Contains("wrong", StringComparison.OrdinalIgnoreCase))
                .OverrideFailureMessage(
                    "A campaign that never disproved her rule is being told it caught her out — copy " +
                    $"asserting something the sim never decided: \"{uncorrected}\"")
                .IsFalse();

            AssertThat(corrected.Contains("caught it faster than I did", StringComparison.Ordinal))
                .OverrideFailureMessage(
                    $"The correction fired and the farewell never acknowledges it: \"{corrected}\"")
                .IsTrue();

            // The live chooser must actually track the campaign, not just the two strings exist.
            AssertThat(ui.Tutorial.GraduationBeatText)
                .OverrideFailureMessage(
                    "A fresh campaign (her rule never disproved) is speaking the corrected variant.")
                .IsEqual(uncorrected);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>U35 depends on this and says so ("her goodbye names where the lessons live"), and it
    /// is also what keeps skipping honest: the course ending is not the content ending.</summary>
    [TestCase]
    public void BothGoodbyes_NameTheLessonsBook_AndHandTheBenchOver()
    {
        foreach (var (label, text) in BothGoodbyeVariants())
        {
            AssertThat(text.Contains("Lessons book", StringComparison.Ordinal))
                .OverrideFailureMessage($"Goodbye ({label}) never says where the lessons live: \"{text}\"")
                .IsTrue();
            AssertThat(text.Contains(GoodbyeSignature, StringComparison.Ordinal))
                .OverrideFailureMessage(
                    $"Goodbye ({label}) dropped the plan's own image of the handover: \"{text}\"")
                .IsTrue();
        }
    }
}
#endif
