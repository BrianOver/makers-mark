using System.Collections.Immutable;
using System.Reflection;
using System.Text.RegularExpressions;
using GameSim;
using GameSim.Advisor;
using GameSim.Cli;
using GameSim.Contracts;
using GameSim.Counter;
using GameSim.Factions;
using GameSim.Harness;
using GameSim.Heroes;
using GameSim.Kernel;
using GameSim.Professions;
using Xunit.Abstractions;

namespace GameSim.Tests.Hygiene;

/// <summary>
/// The six dilemmas, as a SET, proved live on a real trajectory.
///
/// <para><b>The gap this closes.</b> <c>CLAUDE.md</c> opens by saying "Six decisions are what the
/// game is actually made of" and <c>docs/design/THE-GAME.md</c> §3.5 names them. Until this file,
/// nothing executed that claim. The failure it lets through is not hypothetical and has already
/// happened twice, found both times by accident rather than by a guard:
/// <list type="number">
/// <item>Dilemma 6's send arm delivered ZERO supplies across a 2000 party-day sweep, because the
/// band the scripted A/B aimed at (a camped hero below 40% HP) is one nothing in the current build
/// ever parks a hero in — found during a re-measurement (<c>P2-LONG-23</c>, #678), diagnosed by
/// <c>P2-LONG-24</c> (#703: the park floor IS the 50% drink line, structurally, so the band is
/// empty rather than rare), re-aiming booked as <c>P2-LONG-25</c>.</item>
/// <item><c>UnstockAction</c>, <c>SetPriceAction</c> and <c>DeclineCommissionAction</c> are
/// submitted by ZERO harness policies (<c>BalanceCorpusCoverageCensusTests.KnownNeverSubmitted</c>,
/// P2-HONEST-12). <c>DeclineCommissionAction</c> is one whole arm of dilemma 1.</item>
/// </list></para>
///
/// <para><b>Why <c>Balance/VerbConsequenceFloorTests</c> does not already cover it.</b> That test is
/// well built and deliberately narrow: it proves "no verb is inert every single time it is OFFERED"
/// and explicitly refuses the converse. But it advances the real timeline with EMPTY action lists
/// (its own comment says so), so its trajectory is a do-nothing campaign, and option coverage at a
/// decision point is not trajectory coverage. A verb can be consequential whenever it is offered and
/// still never actually be offered on a trajectory anyone plays — nominally a decision, actually
/// absent. That is exactly the hole dilemma 6 fell through, and it is the only property this file
/// asserts. It does not re-probe individual verbs for inertness; it asks whether each DILEMMA — a
/// pair of arms at one decision point — is a thing a playing person ever actually faces.</para>
///
/// <para><b>Derived, never hand-listed.</b> The set of dilemmas comes from parsing §3.5 of
/// <c>THE-GAME.md</c> — one declared source. <see cref="Proofs"/> holds a liveness proof per
/// dilemma, matched to the parsed text by a key phrase, and the match is asserted BOTH ways: a
/// seventh dilemma added to the doc with no proof behind it goes red, and a proof for a dilemma the
/// doc no longer names goes red (CLAUDE.md rule 8 — the stale half is deleted, not corrected). The
/// count is pinned, so widening the set is a reviewed diff in a compiled file. This repo has shipped
/// the hand-listed-fixture bug repeatedly (128 assets under a green suite); a guard iterating a
/// literal array stops covering the family the moment someone adds one.</para>
///
/// <para><b>What "live" means here, and the honest limits.</b> For each dilemma, over a real
/// trajectory driven by real harness policies:
/// <list type="bullet">
/// <item><b>Both arms reachable</b> — there is a decision point where arm A and arm B are BOTH legal
/// (asked of <see cref="ActionLegality.IsLegal"/>, the kernel's own mirrored guards). This direction
/// is sound in the strong sense: the arms were constructed and the game accepted both.</item>
/// <item><b>The arms diverge</b> — forking the state on A and on B and comparing a durable WITNESS
/// <see cref="ProbeDepthTicks"/> ticks later gives different answers. Only one direction of this is
/// sound, exactly as <c>VerbConsequenceFloorTests</c> says: identical ⇒ the two arms lead to the same
/// world, so the choice was not one. Different does NOT prove the choice is interesting — a
/// divergent fingerprint can come from an action shifting RNG draws. So a divergence FAILURE is a
/// finding; a divergence pass is a floor, never a certificate.</item>
/// </list>
/// Where a dilemma has a clean named projection of the durable world that RNG cannot fake, the
/// witness is that projection rather than the whole fingerprint (hero mood for the pin/fleece fork,
/// faction standing for the ore fork). Elsewhere the witness is
/// <see cref="ConsequenceProbe.FingerprintForTests"/> — reused rather than reimplemented, because
/// the fingerprint's completeness is the property the whole measurement rests on and a hand-listed
/// field set has silently stopped covering new fields here before.</para>
///
/// <para><b>Dilemma 6's second half is not assumed away.</b> §3.5 states it in full: "provisioning a
/// camped party provably saves that party, and measurably endangers the run. A topped-up party dares
/// one floor deeper, and the deep floors are where heroes die." A guard that only checked the saving
/// half would miss that the endangering half may be the live one. The divergence check here is
/// direction-agnostic — it asks whether sending changes the world, not whether sending helps — and
/// the sweep additionally reports, as OUTPUT and not as an assertion, how each fork's party actually
/// halted and how many died in it (one fork sampled turns a party's <c>TargetReached</c> into a
/// third <c>FloorLost</c>, which is the endangering half showing up unprompted). Which direction
/// dominates is a balance measurement, not a coverage guard's to rule on.</para>
///
/// <para><b>MECHANICAL liveness, not stake — the limit that matters most on dilemma 6.</b> This file
/// asks whether both arms are available and whether taking one changes the world. It does NOT ask
/// whether the player ever has a REASON to prefer one, and on the vigil those two questions have
/// different answers today. <c>ActionLegality.SendSupplyLegal</c> carries no hero-HP condition at
/// all, so the send arm is genuinely reachable and genuinely consequential whenever the player is
/// holding a consumable — measured here, and this file's finder is deliberately unconditioned on HP,
/// which is how it sees the verb rather than the band. What <c>P2-LONG-24</c> (#703) established is
/// a different and narrower fact: the [flee, drink) window a hurt camped hero would sit in is
/// structurally empty, because the post-floor too-hurt check finalises any party holding a hero
/// under the drink line, so the park floor IS that line. The stake is missing even though the arms
/// are not. Re-aiming the verb at where camped heroes actually are is <c>P2-LONG-25</c>, and it
/// needs a balance re-baseline and (for two of its three knobs) a design ruling — so this guard does
/// not pre-empt it, does not ledger dilemma 6 as dead when its arms measurably are not, and does not
/// quietly widen its own property into a stake test it applies to no other dilemma.</para>
///
/// <para><b>Known drift, reported and not edited.</b> <c>CLAUDE.md</c>'s own list of the six says
/// "buy the goodwill" where §3.5 says "buy the faction's favour". <c>CLAUDE.md</c> is owner-only and
/// deny-listed; this file reads §3.5 as the source and does not touch the other.</para>
/// </summary>
public class SixDilemmasLivenessTests
{
    private readonly ITestOutputHelper _output;

    public SixDilemmasLivenessTests(ITestOutputHelper output) => _output = output;

    // ------------------------------------------------------------------ the declared source ----

    /// <summary>The one declared source. A dilemma that is not written here does not exist, and one
    /// written here without a proof in <see cref="Proofs"/> is a red build.</summary>
    private const string SourceDoc = "docs/design/THE-GAME.md";

    private const string SourceHeading = "### 3.5 The six dilemmas";

    /// <summary>A numbered §3.5 entry: <c>1. **Sell the good one, or hold it ...?**</c>. The bold
    /// question is the dilemma's identity; the prose under it is commentary.</summary>
    private static readonly Regex DilemmaLine = new(@"^(\d+)\.\s+\*\*(.+?)\*\*", RegexOptions.Compiled);

    /// <summary>Pinned so that widening or narrowing the set is a reviewed diff in a compiled file
    /// rather than a doc edit nothing executes.</summary>
    private const int ExpectedDilemmaCount = 6;

    // ------------------------------------------------------------------------- the sweep ----

    /// <summary>Seeds swept. Two is enough for a reachability question (does this decision point
    /// EVER arise on a played trajectory) and keeps the fork cost bounded; the balance corpus owns
    /// the wide sweeps.</summary>
    private static readonly ulong[] Seeds = [2026, 2027];

    private const int Days = 25;

    /// <summary>How far each fork runs before the witnesses are compared. Same reasoning as
    /// <c>VerbConsequenceFloorTests.ProbeDepthTicks</c>, and deliberately the same number: one tick
    /// proves an arm changed nothing THAT TICK, not that it changed nothing, because
    /// <c>GameKernel.Tick</c> appends the action log after phase systems run. Ten ticks crosses two
    /// full five-phase days from any starting phase.</summary>
    private const int ProbeDepthTicks = 10;

    /// <summary>How many times both arms must have been simultaneously legal before a dilemma's
    /// result is allowed to mean anything. The floor is about evidence, not about the dilemma being
    /// fine — the same denominator-first discipline <c>VerbConsequenceFloorTests</c> applies to its
    /// thin-verb check.</summary>
    private const int MinReachedForEvidence = 3;

    /// <summary>How many of a dilemma's decision points are actually FORKED. Reachability is counted
    /// at every point (it is free — a legality query), but the fork pair is the expensive half, and
    /// divergence is asserted as "&gt; 0", so a dilemma that has already diverged cannot be
    /// un-diverged by forking it two hundred more times. What the cap DOES trade away is stated
    /// plainly: a dilemma that diverges only on its 26th-and-later decision points would be reported
    /// here as convergent. That is a sampling limit, not a softening — the failure it produces is a
    /// loud, named finding to go investigate, never a quiet pass — and 25 consecutive identical
    /// outcomes is already strong evidence. Well above <see cref="MinReachedForEvidence"/> so the
    /// reported ratio still means something, and low enough to keep this guard affordable in the fast
    /// lane rather than exiling it to the balance gate, where it would be skipped by exactly the PRs
    /// most likely to break it.</summary>
    private const int MaxForksPerDilemma = 25;

    // ------------------------------------------------------------------------- the ledger ----

    /// <summary>
    /// Dilemmas admitted to be dead — key phrase → the reason and the booking that owns the repair.
    /// An entry here is an ADMISSION that one of the six decisions the game is "actually made of" is
    /// not currently a decision, so it must name the unit that will fix it. Softening an assertion
    /// to go green is the banned move; this table is the only door, and
    /// <see cref="ExpectedDeadDilemmaCount"/> pins it so a new admission is a reviewed diff.
    /// </summary>
    private static readonly Dictionary<string, string> DeadByBooking = new();

    private const int ExpectedDeadDilemmaCount = 0;

    // ----------------------------------------------------------------------- the proofs ----

    /// <summary>The abstain arm (dilemma 4's "bank it", dilemma 6's "trust their judgment") is a
    /// real arm and it is spelled as the absence of an action, so an arm is a nullable action.</summary>
    private sealed record Arms(PlayerAction A, PlayerAction? B);

    private sealed record DilemmaProof(
        string KeyPhrase,
        string ArmALabel,
        string ArmBLabel,
        Func<GameState, Arms?> Find,
        string WitnessName,
        Func<GameState, string> Witness);

    /// <summary>The whole durable world, reusing the probe's own fingerprint (and with it the
    /// neutralisations that make "spent a slot and achieved nothing" read as no change at all).</summary>
    private static string WorldWitness(GameState state) => ConsequenceProbe.FingerprintForTests(state);

    /// <summary>Every hero's persistent mood toward the shop. Stronger than the whole-state
    /// fingerprint here, and honestly so: the two arms are the pin branch and the fleece branch of
    /// the SAME <c>HaggleResolver.ResolveCounter</c> call on the SAME hero, and those branches apply
    /// <c>+PinMoodBonus</c> and <c>-FleeceMoodPenalty</c> — so a difference in this witness is the
    /// branch, not a shifted RNG draw. It is close to tautological on purpose: what carries dilemma 2
    /// is the REACHABILITY count (are the two branches ever both available at one open round?), and
    /// this half is the standing pin that the branches still land where they say they do.</summary>
    private static string MoodWitness(GameState state) =>
        string.Join(",", state.Heroes.Values.Select(h => $"{h.Id.Value}:{h.MoodPermille}"));

    /// <summary>Standing with every registered faction. Only an ore purchase raises it and only the
    /// morning drift lowers it, both deterministically — so a difference here is the purchase.</summary>
    private static string StandingWitness(GameState state) =>
        string.Join(",", FactionRegistry.All.Keys.Select(f => $"{f}:{state.Player.StandingFor(f)}"));

    private static readonly ImmutableList<DilemmaProof> Proofs =
    [
        new DilemmaProof(
            "Sell the good one",
            "accept the commission (hold it for the named hero)",
            "decline it (the shelf pays now)",
            FindCommissionArms,
            "durable world",
            WorldWitness),
        new DilemmaProof(
            "Price for the sale",
            "counter at true willingness (the pin)",
            "counter above the round's ceiling (the fleece)",
            FindHagglePriceArms,
            "hero mood",
            MoodWitness),
        new DilemmaProof(
            "Fill the empty slot",
            "craft for a slot a marching hero has EMPTY",
            "craft for a slot a marching hero already has FILLED",
            FindGearFocusArms,
            "durable world",
            WorldWitness),
        new DilemmaProof(
            "Spend the slot",
            "spend a slot on real work",
            "bank it (submit nothing)",
            FindSlotSpendArms,
            "durable world",
            WorldWitness),
        new DilemmaProof(
            "Buy the ore",
            "buy one faction's ore",
            "buy a different faction's ore",
            FindOreFactionArms,
            "faction standing",
            StandingWitness),
        new DilemmaProof(
            "Send the runner",
            "send a held consumable down to the camp",
            "trust their judgment (submit nothing)",
            FindVigilArms,
            "durable world",
            WorldWitness),
    ];

    // ------------------------------------------------------------------ derivation guards ----

    [Fact]
    public void TheSixComeFromTheDeclaredSource_AndTheParseFoundThem()
    {
        var declared = ParseDeclaredDilemmas();

        Assert.True(declared.Count > 0,
            $"Parsed no numbered dilemma out of '{SourceHeading}' in {SourceDoc}. Either the section "
            + "moved/was renamed or the numbered-bold shape changed — until this parse works, every "
            + "assertion below is vacuous, which is the shape of green this whole file refuses.");

        Assert.True(declared.Count == ExpectedDilemmaCount,
            $"{SourceDoc} §3.5 now declares {declared.Count} dilemmas, pinned at "
            + $"{ExpectedDilemmaCount}:\n  " + string.Join("\n  ", declared.Select(d => $"{d.Ordinal}. {d.Question}"))
            + "\nAdding or removing one of the decisions the game is made of is a real change: move "
            + "this pin and land a liveness proof for the new member in the same PR.");
    }

    [Fact]
    public void EveryDeclaredDilemma_HasExactlyOneLivenessProof_AndEveryProofHasADilemma()
    {
        var declared = ParseDeclaredDilemmas();

        var unproved = declared
            .Where(d => Proofs.Count(p => d.Question.Contains(p.KeyPhrase, StringComparison.Ordinal)) != 1)
            .Select(d => $"{d.Ordinal}. {d.Question}")
            .ToList();

        Assert.True(unproved.Count == 0,
            $"A dilemma is declared in {SourceDoc} §3.5 with no liveness proof behind it (or with more "
            + "than one claiming it). Deny-by-default is the point: a seventh decision cannot be added "
            + "to the doc without something that proves a player ever actually faces it.\n  "
            + string.Join("\n  ", unproved));

        var orphaned = Proofs
            .Where(p => !declared.Any(d => d.Question.Contains(p.KeyPhrase, StringComparison.Ordinal)))
            .Select(p => p.KeyPhrase)
            .ToList();

        Assert.True(orphaned.Count == 0,
            $"A liveness proof names a dilemma {SourceDoc} §3.5 no longer declares. Delete the proof "
            + "rather than leaving it asserting something the design does not claim (CLAUDE.md rule "
            + "8):\n  " + string.Join("\n  ", orphaned));
    }

    [Fact]
    public void DeadDilemmaLedger_IsPinned_AndEveryAdmissionCitesItsBooking()
    {
        Assert.True(DeadByBooking.Count == ExpectedDeadDilemmaCount,
            $"Pinned at {ExpectedDeadDilemmaCount}; the ledger now holds {DeadByBooking.Count}. Every "
            + "admission that one of the six is not currently a decision is a reviewed diff, never a "
            + "quiet widen.");

        var citation = new Regex(@"§11\.\d+|\bP\d+-[A-Z]+-\d+\b|\bP\d+\b");
        var uncited = DeadByBooking.Where(e => !citation.IsMatch(e.Value)).Select(e => e.Key).ToList();

        Assert.True(uncited.Count == 0,
            "A dead dilemma pinned with no booking behind it is drift wearing a reason — name the unit "
            + "in docs/design/MAKERS-MARK.md that owns the repair:\n  " + string.Join("\n  ", uncited));

        var unknown = DeadByBooking.Keys
            .Where(k => !Proofs.Any(p => p.KeyPhrase == k))
            .ToList();

        Assert.True(unknown.Count == 0,
            "The ledger pins a key phrase no proof uses — a stale entry that silently exempts nothing "
            + "while looking like coverage:\n  " + string.Join("\n  ", unknown));
    }

    /// <summary>
    /// Dilemma 4's own second half, and the only one that is not about arms at all: "the budget is a
    /// real constraint on ambition, not a formality." Spend-versus-bank is only a decision if banking
    /// ever costs something, so this asks whether the day's five slots actually run out while real
    /// work is still legally waiting.
    ///
    /// <para><b>Whose ambition.</b> §3.5's sentence is about an AMBITIOUS player, so measuring the
    /// budget against a policy with no appetite would answer a question about the policy instead of
    /// about the game. The subject here is therefore <see cref="DriverActions"/> plus a greedy pass
    /// that takes every remaining legal slot-consuming opportunity the day offers, in
    /// <see cref="ActionLegality.LegalActions"/>'s own deterministic order. The unambitious figure is
    /// reported alongside, because it is a real finding in its own right: <see cref="BaselinePlayer"/>
    /// — the policy every balance band in this repo is certified against — never exhausts the budget
    /// at all, which is P2-LONG-24's third candidate cause ("the harness never provokes the state")
    /// showing up in a second place.</para>
    ///
    /// Cheap enough for the fast lane: no forks, two campaigns, and a pure legality query.
    /// </summary>
    [Fact]
    public void TheActionSlotBudget_ActuallyBinds_SoBankingASlotCostsSomething()
    {
        var (ambitiousTicks, ambitiousExhausted, ambitiousBinding) = WalkBudget(ambitious: true);
        var (baselineTicks, baselineExhausted, baselineBinding) = WalkBudget(ambitious: false);

        _output.WriteLine($"ambitious : {ambitiousTicks} ticks, slots exhausted on {ambitiousExhausted}, "
            + $"of which {ambitiousBinding} still had legal real work waiting.");
        _output.WriteLine($"unambitious (BaselinePlayer + CounterPlayer only): {baselineTicks} ticks, "
            + $"slots exhausted on {baselineExhausted}, binding on {baselineBinding} — reported, not "
            + "asserted; see this test's doc.");

        Assert.True(ambitiousBinding > 0,
            $"Across {ambitiousTicks} ticks of a real trajectory driven by a player who takes every "
            + $"slot-consuming opportunity the day offers, the {ActionBudget.SlotsPerDay}-slot budget "
            + "never once refused work that was otherwise legal. Then \"spend the slot, or bank it\" "
            + "is not a dilemma — banking is free — and THE-GAME.md §3.5's claim that the budget is "
            + "\"a real constraint on ambition, not a formality\" is the thing that is false. Fix the "
            + "constraint or ledger the dilemma; do not relax this.");
    }

    /// <summary>Walks one campaign and counts the ticks where the budget was spent out AND real work
    /// was still legal but for the budget. Every slot-consuming handler's guard ends in
    /// <c>ActionSlotsRemaining &gt; 0</c>, so asking the same question of a local copy with a full
    /// budget isolates the budget as the ONLY thing refusing the work.</summary>
    private static (int Ticks, int Exhausted, int Binding) WalkBudget(bool ambitious)
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(Seeds[0]);
        int ticks = 0, exhausted = 0, binding = 0;

        while (state.Day <= Days)
        {
            ticks++;
            if (state.ActionSlotsRemaining <= 0)
            {
                exhausted++;
                var withBudget = state with { ActionSlotsRemaining = ActionBudget.SlotsPerDay };
                if (ActionLegality.LegalActions(withBudget, withBudget.Phase).Any(ActionBudget.ConsumesSlot))
                {
                    binding++;
                }
            }

            var actions = DriverActions(state).ToBuilder();
            if (ambitious)
            {
                var claimed = actions.Count(ActionBudget.ConsumesSlot);
                foreach (var candidate in ActionLegality.LegalActions(state, state.Phase)
                             .Where(ActionBudget.ConsumesSlot))
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
            }

            state = kernel.Tick(state, actions.ToImmutable()).NewState;
        }

        return (ticks, exhausted, binding);
    }

    // ---------------------------------------------------------------------- the liveness sweep ----

    /// <summary>
    /// Deliberately NOT <c>Category=Balance</c>, even though it forks a campaign: the balance gate is
    /// skippable for a unit that touches no sim content, and a guard whose whole purpose is to be
    /// un-skippable belongs where every PR pays for it. It costs about six seconds and it asserts
    /// floors (both arms reachable at all; the arms diverge at all), never bands, so a tuning pass
    /// cannot make it flap the way a balance measurement would.
    /// </summary>
    [Fact]
    public void EverySixthOfTheGame_HasBothArmsReachable_AndTheArmsLeadToDifferentWorlds()
    {
        var kernel = GameComposition.BuildKernel();
        var reached = Proofs.ToDictionary(p => p.KeyPhrase, _ => 0);
        var forked = Proofs.ToDictionary(p => p.KeyPhrase, _ => 0);
        var diverged = Proofs.ToDictionary(p => p.KeyPhrase, _ => 0);
        var vigilHalts = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var seed in Seeds)
        {
            var state = GameComposition.NewCampaign(seed);
            while (state.Day <= Days)
            {
                foreach (var proof in Proofs)
                {
                    if (proof.Find(state) is not { } arms)
                    {
                        continue;
                    }

                    reached[proof.KeyPhrase]++;
                    if (forked[proof.KeyPhrase] >= MaxForksPerDilemma)
                    {
                        continue;
                    }

                    forked[proof.KeyPhrase]++;
                    var sent = Ahead(kernel, state, arms.A);
                    var held = Ahead(kernel, state, arms.B);
                    if (proof.Witness(sent) != proof.Witness(held))
                    {
                        diverged[proof.KeyPhrase]++;
                    }

                    // Dilemma 6's endangering half, off the same two forks rather than two more.
                    if (arms.A is SendSupplyAction)
                    {
                        vigilHalts.Add($"send[{Halts(sent)} dead={Fallen(sent)}] "
                            + $"vs trust[{Halts(held)} dead={Fallen(held)}]");
                    }
                }

                // The real timeline advances under real policies, never an empty action list: that
                // difference is the entire reason this file exists beside VerbConsequenceFloorTests.
                state = kernel.Tick(state, DriverActions(state)).NewState;
            }
        }

        foreach (var proof in Proofs)
        {
            _output.WriteLine($"{proof.KeyPhrase,-22} reached {reached[proof.KeyPhrase],4}  "
                + $"diverged {diverged[proof.KeyPhrase],3}/{forked[proof.KeyPhrase],-3} forked  "
                + $"(witness: {proof.WitnessName})");
        }

        // Dilemma 6's endangering half, reported and never asserted — which way provisioning moves
        // mortality is a balance measurement, not a coverage guard's ruling.
        _output.WriteLine("vigil fork outcomes (send vs trust), diagnostic only: "
            + (vigilHalts.Count == 0 ? "(the vigil fork never arose)" : string.Join(" | ", vigilHalts)));

        var absent = Absent(reached);

        Assert.True(absent.Count == 0,
            "One of the six decisions the game is actually made of is not a decision a player ever "
            + "faces: its two arms were never (or barely ever) both available at the same point on a "
            + "real trajectory. This is the shape dilemma 6 failed in, and it is invisible to "
            + "VerbConsequenceFloorTests because that test walks a do-nothing campaign. Fix the "
            + "reachability, or admit it in DeadByBooking with the unit that owns the repair — never "
            + "by relaxing this floor:\n  " + string.Join("\n  ", absent));

        var converged = Converged(reached, forked, diverged);

        Assert.True(converged.Count == 0,
            "Both arms of a declared dilemma are offered, and taking one rather than the other led to "
            + "an identical world every time it was measured. Identical outcome is the sound direction "
            + "of this measurement: the choice provably did not matter, so it is presentation, not a "
            + "decision:\n  " + string.Join("\n  ", converged));
    }

    /// <summary>Dilemmas whose two arms were never (or barely ever) both available at once, ledger
    /// entries excluded.</summary>
    private static List<string> Absent(IReadOnlyDictionary<string, int> reached) => Proofs
        .Where(p => !DeadByBooking.ContainsKey(p.KeyPhrase))
        .Where(p => reached[p.KeyPhrase] < MinReachedForEvidence)
        .Select(p => $"{p.KeyPhrase} — both arms simultaneously legal only {reached[p.KeyPhrase]} "
            + $"time(s) across {Seeds.Length} seeds x {Days} days "
            + $"(A: {p.ArmALabel}; B: {p.ArmBLabel})")
        .ToList();

    /// <summary>Dilemmas whose arms were reachable but always led to the same world, ledger entries
    /// excluded.</summary>
    private static List<string> Converged(
        IReadOnlyDictionary<string, int> reached,
        IReadOnlyDictionary<string, int> forked,
        IReadOnlyDictionary<string, int> diverged) => Proofs
        .Where(p => !DeadByBooking.ContainsKey(p.KeyPhrase))
        .Where(p => reached[p.KeyPhrase] >= MinReachedForEvidence && diverged[p.KeyPhrase] == 0)
        .Select(p => $"{p.KeyPhrase} — both arms reachable {reached[p.KeyPhrase]} time(s), forked "
            + $"{forked[p.KeyPhrase]} of them, and every single time they left the same {p.WitnessName} "
            + $"(A: {p.ArmALabel}; B: {p.ArmBLabel})")
        .ToList();

    /// <summary>
    /// The regression proof for the half that actually carries this file: both classifications must
    /// really fire. All six dilemmas are live on today's build, so the sweep above is green — and a
    /// green coverage guard nobody has watched fail is indistinguishable from a guard that cannot
    /// fail. (Both assertions DID fire on this file's first run: the counter fork was reached 0 times
    /// because the pin arm was constructed wrong, and the slot budget never bound. This pins that
    /// ability so it survives the fix.)
    /// </summary>
    [Fact]
    public void RegressionProof_ADeadDilemma_IsNamedByBothClassifications()
    {
        var none = Proofs.ToDictionary(p => p.KeyPhrase, _ => 0);
        var plenty = Proofs.ToDictionary(p => p.KeyPhrase, _ => MinReachedForEvidence);

        // Unreachable: every dilemma named as absent, none as converged (nothing was measurable).
        Assert.Equal(Proofs.Count, Absent(none).Count);
        Assert.Empty(Converged(none, none, none));

        // Reachable but inert: none named as absent, every one named as converged.
        Assert.Empty(Absent(plenty));
        Assert.Equal(Proofs.Count, Converged(plenty, plenty, none).Count);

        // The live shape: neither classification fires.
        Assert.Empty(Absent(plenty));
        Assert.Empty(Converged(plenty, plenty, plenty));
    }

    // ------------------------------------------------------------------------- machinery ----

    /// <summary>
    /// A real player's day, assembled from the harness policies that already exist rather than a
    /// bespoke script: <see cref="BaselinePlayer"/> works the bench (craft, stock, buy ore, accept
    /// commissions, climb the forge), <see cref="CounterPlayer"/> serves the counter face to face,
    /// and a daily field salve is kept HELD so the vigil actually has something to send — the same
    /// shape <c>CampProvisioningBalanceTests</c> uses, and the one THE-GAME.md describes ("You can
    /// craft the salve from inside the stop and hand it over in the same breath"). BaselinePlayer
    /// alone never opens the counter, so it is not by itself a trajectory on which dilemma 2 could
    /// ever arise; combining the two here changes neither policy and touches no balance band.
    /// </summary>
    private static ImmutableList<PlayerAction> DriverActions(GameState state)
    {
        var actions = BaselinePlayer.ActionsFor(state).ToBuilder();
        actions.AddRange(CounterPlayer.ActionsFor(state));
        if (state.Phase == DayPhase.Expedition && FirstLegalHealCraft(state) is { } salve)
        {
            actions.Add(salve);
        }

        return actions.ToImmutable();
    }

    /// <summary>The cheapest healing consumable the bench can actually make right now, found by
    /// walking the recipe registry rather than naming a recipe id — a renamed or retiered salve
    /// keeps this driver working instead of silently making the vigil unreachable.</summary>
    private static CraftAction? FirstLegalHealCraft(GameState state) =>
        ProfessionRegistry.AllRecipes.Values
            .Where(r => r.Effect is { Kind: ConsumableKind.Heal })
            .OrderBy(r => r.Tier)
            .ThenBy(r => r.RecipeId, StringComparer.Ordinal)
            .Select(r => new CraftAction(r.RecipeId, r.MaterialKey))
            .FirstOrDefault(c => ActionLegality.IsLegal(state, c, state.Phase));

    /// <summary>Runs one arm on a fork's first tick (or nothing, for the abstain arm), then
    /// <see cref="ProbeDepthTicks"/> - 1 further empty-action ticks on that same fork. Both arms run
    /// the identical depth and the identical follow-on, so the comparison stays apples-to-apples and
    /// the only difference between the two futures is the arm.</summary>
    private static GameState Ahead(GameKernel kernel, GameState state, PlayerAction? arm)
    {
        var forked = kernel.Tick(
            state, arm is null ? ImmutableList<PlayerAction>.Empty : ImmutableList.Create(arm)).NewState;
        for (var i = 1; i < ProbeDepthTicks; i++)
        {
            forked = kernel.Tick(forked, ImmutableList<PlayerAction>.Empty).NewState;
        }

        return forked;
    }

    /// <summary>Diagnostic for dilemma 6's second half: how the camped party actually ended under
    /// each arm. Reported, never asserted — see the class doc.</summary>
    private static string Halts(GameState state) => string.Join("+", state.LastNightExpeditions
        .Concat(state.PendingExpeditions)
        .Select(e => e.Halt.ToString())
        .OrderBy(h => h, StringComparer.Ordinal)
        .DefaultIfEmpty("-"));

    private static int Fallen(GameState state) => state.Heroes.Values.Count(h => !h.Alive);

    private static ImmutableList<(int Ordinal, string Question)> ParseDeclaredDilemmas()
    {
        var path = Path.Combine(RepoRoot(), SourceDoc.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"The declared source {SourceDoc} is not at {path}.");

        var lines = File.ReadAllLines(path);
        Assert.True(Array.Exists(lines, l => l.StartsWith(SourceHeading, StringComparison.Ordinal)),
            $"'{SourceHeading}' is gone from {SourceDoc} — the set of dilemmas has no declared source "
            + "any more, so nothing below can be derived from it.");

        return ParseFrom(lines);
    }

    /// <summary>The parse, over lines rather than over the file, so the detector's own correctness
    /// can be proved on a synthetic document and never depends on a particular gap still existing in
    /// the live doc (the <c>BalanceCorpusCoverageCensusTests</c> regression-proof pattern).</summary>
    private static ImmutableList<(int Ordinal, string Question)> ParseFrom(IReadOnlyList<string> lines)
    {
        var found = ImmutableList.CreateBuilder<(int, string)>();
        var start = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].StartsWith(SourceHeading, StringComparison.Ordinal))
            {
                start = i;
                break;
            }
        }

        if (start < 0)
        {
            return found.ToImmutable();
        }

        for (var i = start + 1; i < lines.Count; i++)
        {
            // Stop at the next heading or horizontal rule: §3.5's own list only.
            if (lines[i].StartsWith("#", StringComparison.Ordinal)
                || lines[i].StartsWith("---", StringComparison.Ordinal))
            {
                break;
            }

            if (DilemmaLine.Match(lines[i]) is { Success: true } m)
            {
                found.Add((int.Parse(m.Groups[1].Value), m.Groups[2].Value));
            }
        }

        return found.ToImmutable();
    }

    /// <summary>The regression proof: a seventh dilemma written into §3.5 with nothing behind it must
    /// be visible to this file's own matcher. Deny-by-default is the whole design — if a new decision
    /// can be declared without a liveness proof, the guard is a banned-word list.</summary>
    [Fact]
    public void RegressionProof_ASeventhDilemmaWithNoProofBehindIt_IsSeenAsUnproved()
    {
        string[] doc =
        [
            SourceHeading,
            string.Empty,
            "1. **Sell the good one, or hold it?** ...",
            "7. **Trade the shop, or keep the anvil?** an entirely new decision",
            "---",
        ];

        var declared = ParseFrom(doc);
        Assert.Equal(2, declared.Count);

        var unproved = declared
            .Where(d => Proofs.Count(p => d.Question.Contains(p.KeyPhrase, StringComparison.Ordinal)) != 1)
            .Select(d => d.Ordinal)
            .ToList();

        Assert.Equal([7], unproved);
    }

    /// <summary>The negative control: prose elsewhere in the document that happens to be a numbered
    /// bold line must never be counted as a declared dilemma. The section boundary is what keeps the
    /// rest of a 900-line design doc from silently widening the set.</summary>
    [Fact]
    public void NegativeControl_ANumberedBoldLineOutsideSection35_IsNeverCounted()
    {
        string[] doc =
        [
            "1. **A numbered bold line before the section** never counted",
            SourceHeading,
            "1. **Sell the good one, or hold it?** counted",
            "## 4. The core systems",
            "2. **A numbered bold line after the section** never counted",
        ];

        var declared = ParseFrom(doc);
        Assert.Single(declared);
        Assert.Contains("Sell the good one", declared[0].Question, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Game.sln")))
        {
            dir = dir.Parent;
        }

        Assert.True(dir is not null, "Could not find Game.sln walking up from the test assembly.");
        return dir!.FullName;
    }

    // ------------------------------------------------------------------- the six arm-finders ----

    /// <summary>1. Sell the good one, or hold it for the hero who needs it? The shelf pays now; an
    /// accepted commission holds the piece for a named person at a premium, later. Accept and
    /// decline are the two arms of that same open commission, and they are mutually exclusive by
    /// construction — <c>CommissionHandlers</c> removes the commission on either resolution.</summary>
    private static Arms? FindCommissionArms(GameState state)
    {
        if (state.Phase != DayPhase.Morning)
        {
            return null;
        }

        foreach (var commission in state.Commissions.Where(c => !c.Accepted))
        {
            var accept = new AcceptCommissionAction(commission.Hero);
            var decline = new DeclineCommissionAction(commission.Hero);
            if (ActionLegality.IsLegal(state, accept, state.Phase)
                && ActionLegality.IsLegal(state, decline, state.Phase))
            {
                return new Arms(accept, decline);
            }
        }

        return null;
    }

    /// <summary>2. Price for the sale, or price for the relationship? Both arms are a Counter at an
    /// open haggle round; the fork is the number. The pin arm names true willingness exactly (a
    /// mood bonus that compounds into later premiums); the fleece arm names one gold above the
    /// round's ceiling (gold once, and a memory). Mirrors <c>HaggleResolver.ResolveCounter</c>'s own
    /// inputs — the shelf entry's list price and the 7-argument willingness including the hero's
    /// price-sensitivity trait — so the two arms really do land in the two branches named.</summary>
    private static Arms? FindHagglePriceArms(GameState state)
    {
        if (state.Phase != DayPhase.Morning
            || state.Counter is not { Closed: false, Round: > 0 } session
            || session.StandingOfferGold is null
            || session.Presented is not { } presented
            || session.Active is not { } activeId
            || !state.Heroes.TryGetValue(activeId.Value, out var hero)
            || state.Player.Shelf.FirstOrDefault(e => e.Item == presented) is not { } entry
            || !state.Items.TryGetValue(presented.Value, out var item))
        {
            return null;
        }

        var willingness = WillingnessModel.TrueWillingness(
            entry.Price, hero.Gold, hero.ClassId, session.InterestPermille, hero.MoodPermille,
            item.Quality, TraitEffects.PriceSensitivityPermille(hero));
        var (_, ceiling) = WillingnessModel.Band(willingness, session.Round);

        // The pin is NOT "name true willingness". Round 1's ceiling sits BELOW true willingness
        // (measured: willingness 25 against a band of [20,24]), and ResolveCounter tests the ceiling
        // FIRST — so naming true willingness exactly resolves as a fleece, not a read. The pin arm
        // is therefore the highest price that is simultaneously inside the pin window and inside the
        // round's band, which on that same measurement is the ceiling itself: one gold apart from
        // the fleece arm, which is what makes this fork a pure test of the pin/fleece branch rather
        // than of how much gold changed hands.
        var pinCeiling = (int)((long)willingness * (1000 + WillingnessModel.PinWindowPermille) / 1000);
        var pin = Math.Min(ceiling, pinCeiling);
        var fleece = ceiling + 1;

        // Both arms must be the branch they are named for, not merely legal: a fleece the hero
        // cannot afford is refused outright rather than remembered.
        if (pin <= 0 || !WillingnessModel.IsPin(pin, willingness) || pin > hero.Gold || fleece > hero.Gold)
        {
            return null;
        }

        var pinArm = new HaggleResponseAction(HaggleResponseKind.Counter, pin);
        var fleeceArm = new HaggleResponseAction(HaggleResponseKind.Counter, fleece);
        return ActionLegality.IsLegal(state, pinArm, state.Phase)
               && ActionLegality.IsLegal(state, fleeceArm, state.Phase)
            ? new Arms(pinArm, fleeceArm)
            : null;
    }

    /// <summary>3. Fill the empty slot, or upgrade the full one? The muster board is the input, so
    /// the marchers come from <see cref="MusterPlan.Compute"/> — the same projection the board and
    /// the Expedition tick both read, never a re-derivation. A slot that is empty on one marcher and
    /// filled on another is excluded from the upgrade arm, so the two arms genuinely aim at
    /// different slots rather than at an ambiguous one.</summary>
    private static Arms? FindGearFocusArms(GameState state)
    {
        if (state.Phase is not (DayPhase.Morning or DayPhase.Expedition))
        {
            return null;
        }

        var empty = new HashSet<ItemSlot>();
        var filled = new HashSet<ItemSlot>();
        foreach (var plan in MusterPlan.Compute(state.Heroes, state.Bounties, state.Items))
        {
            foreach (var id in plan.Roster)
            {
                if (!state.Heroes.TryGetValue(id.Value, out var hero))
                {
                    continue;
                }

                foreach (var slot in RaidForecast.MissingItemSlots(hero.Gear))
                {
                    empty.Add(slot);
                }

                foreach (var slot in new[] { ItemSlot.Weapon, ItemSlot.Shield, ItemSlot.Armor })
                {
                    if (hero.Gear.Slot(slot) is not null)
                    {
                        filled.Add(slot);
                    }
                }
            }
        }

        if (empty.Count == 0 || filled.Count == 0)
        {
            return null;
        }

        CraftAction? fill = null;
        CraftAction? upgrade = null;
        foreach (var recipe in ProfessionRegistry.AllRecipes.Values)
        {
            var candidate = new CraftAction(recipe.RecipeId, recipe.MaterialKey);
            if (!ActionLegality.IsLegal(state, candidate, state.Phase))
            {
                continue;
            }

            if (fill is null && empty.Contains(recipe.Slot))
            {
                fill = candidate;
            }

            if (upgrade is null && filled.Contains(recipe.Slot) && !empty.Contains(recipe.Slot))
            {
                upgrade = candidate;
            }
        }

        return fill is not null && upgrade is not null ? new Arms(fill, upgrade) : null;
    }

    /// <summary>4. Spend the slot, or bank it? The spend arm is whatever real work the day actually
    /// offers (<see cref="ActionBudget.ConsumesSlot"/> decides what counts, so a new slot-consuming
    /// verb is covered the day it lands); the bank arm is the absence of an action. Whether banking
    /// ever COSTS anything is the separate, fast-lane half —
    /// <see cref="TheActionSlotBudget_ActuallyBinds_SoBankingASlotCostsSomething"/>.</summary>
    private static Arms? FindSlotSpendArms(GameState state)
    {
        if (state.ActionSlotsRemaining <= 0)
        {
            return null;
        }

        var spend = ActionLegality.LegalActions(state, state.Phase).FirstOrDefault(ActionBudget.ConsumesSlot);
        return spend is null ? null : new Arms(spend, null);
    }

    /// <summary>5. Buy the ore, or buy the faction's favour? §3.5 states the fork exactly: "whose ore
    /// you buy". So both arms are a purchase — the choice is which faction's standing rises, which
    /// means the point only exists when tonight's offers span more than one faction.</summary>
    private static Arms? FindOreFactionArms(GameState state)
    {
        if (state.Phase != DayPhase.Evening)
        {
            return null;
        }

        var byFaction = new SortedDictionary<string, BuyOreAction>(StringComparer.Ordinal);
        foreach (var offer in state.OpenOreOffers)
        {
            if (FactionRegistry.ByOreKey(offer.MaterialKey) is not { } faction
                || byFaction.ContainsKey(faction.Id))
            {
                continue;
            }

            var candidate = new BuyOreAction(offer.From, offer.MaterialKey, offer.Quantity);
            if (ActionLegality.IsLegal(state, candidate, state.Phase))
            {
                byFaction[faction.Id] = candidate;
            }
        }

        if (byFaction.Count < 2)
        {
            return null;
        }

        var two = byFaction.Values.Take(2).ToList();
        return new Arms(two[0], two[1]);
    }

    /// <summary>6. Send the runner, or trust their judgment? Deliberately unconditioned on hero HP,
    /// because <c>ActionLegality.SendSupplyLegal</c> is: the 40% trigger that delivers nothing in
    /// <c>CampProvisioningBalanceTests</c> is that scripted policy's own aim, not a rule of the game.
    /// So this asks the reachability question that A/B could not — is the verb itself ever available
    /// to a player standing at the winch-house? — and leaves whether the player has a REASON to use
    /// it to <c>P2-LONG-25</c>. See the class doc's note on mechanical liveness versus stake.</summary>
    private static Arms? FindVigilArms(GameState state)
    {
        if (state.Phase != DayPhase.Camp)
        {
            return null;
        }

        foreach (var inFlight in state.InFlight)
        {
            foreach (var item in state.Items.Values)
            {
                if (item.Effect is null || !item.PlayerCrafted)
                {
                    continue;
                }

                foreach (var member in inFlight.Party)
                {
                    var send = new SendSupplyAction(member, item.Id);
                    if (ActionLegality.IsLegal(state, send, state.Phase))
                    {
                        return new Arms(send, null);
                    }
                }
            }
        }

        return null;
    }
}
