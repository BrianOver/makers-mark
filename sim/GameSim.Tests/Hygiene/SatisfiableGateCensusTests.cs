using System.Reflection;
using System.Text.RegularExpressions;
using GameSim.Contracts;
using Xunit.Abstractions;

namespace GameSim.Tests.Hygiene;

/// <summary>
/// P2-HONEST-07: the satisfiable-gate census — every gate predicate's INPUTS are provably writable.
///
/// <para><b>The shipped defect this generalizes.</b> <c>TutorialFlow</c> gated the Progress board on
/// <c>state.Bounties.Any(b =&gt; b.Paid)</c>. <see cref="Bounty.Paid"/> is <c>false</c> at its only
/// construction site (<c>BountyHandlers.Apply</c>) and is never assigned <c>true</c> anywhere in
/// <c>sim/</c> or <c>godot/</c> — <c>BountyPayoutSystem</c> REMOVES a paid bounty from the board
/// rather than flipping the flag, and no <c>with { Paid = … }</c> exists. The predicate was
/// permanently false: a surface the game promises, welded shut for every player, forever. Worse,
/// <c>FullPlaytest</c> reached that board through a direct <c>OpenPanel("Progress")</c> bypass, so
/// the harness reported the surface as covered while the player's door could not open. Fixed as
/// P2-HONEST-01 (#699) by reading <c>EventLog.OfType&lt;BountyPaid&gt;()</c> and deleting the bypass.
/// <b>Nothing would have caught it.</b> It is not a test failure — the predicate evaluates fine, it
/// just never becomes true.</para>
///
/// <para><b>THE MECHANISM, AND WHAT IT DOES NOT PROVE.</b> This is <em>input-writability</em>, not
/// reachability-by-construction. For each discovered gate predicate it extracts the INPUT TERMS the
/// predicate reads, then requires each term to have evidence that the term can ever hold anything
/// other than the value it is born with:
/// <list type="bullet">
/// <item><b>event term</b> — <c>EventLog.OfType&lt;X&gt;()</c> for a <see cref="GameEvent"/> type
/// <c>X</c>: evidence is a <c>new X(</c> construction somewhere in <c>sim/GameSim/</c>.</item>
/// <item><b>field term</b> — a property of a <c>Contracts</c> record: evidence is a write of a
/// NON-DEFAULT value (a <c>with { P = … }</c>, a named argument <c>P: …</c>, or a positional
/// constructor argument resolved to <c>P</c> through the record's own primary constructor by
/// reflection) somewhere in <c>sim/GameSim/</c>.</item>
/// <item><b>client field term</b> — a <c>_privateField</c> a client-side gate latches on (the
/// tutorial chain's armed-day/card-seen latches): evidence is a non-default assignment somewhere in
/// <c>godot/scripts/</c>.</item>
/// </list>
/// A term with NO such write is <b>dead input</b>: no action sequence can move it. If the gate needs
/// that term non-default, the gate is P2-HONEST-01's exact shape — a wall wearing a gate's clothes.
/// If the gate is instead satisfied BY the default, the field is dead weight in a shared contract.
/// Either way it is a decision someone owes, not a green build, which is why this census does not
/// try to infer the polarity and reports both.</para>
///
/// <para>What it therefore does <b>NOT</b> prove, stated plainly because a census that overstates
/// its own guarantee is the failure this repo keeps finding:
/// <list type="number">
/// <item><b>It does not prove a gate is satisfiable.</b> Every term of a conjunction can be
/// individually writable and the conjunction still be impossible — two flags no single trajectory
/// ever sets together. This proves the door has a hinge; it says nothing about the key.</item>
/// <item><b>It does not prove any write is REACHABLE</b> on a trajectory a player can drive. A
/// <c>new X(</c> behind a branch nothing enters counts as evidence here.
/// <c>SixDilemmasLivenessTests</c> is this repo's reachability-by-construction guard, and it covers
/// six decision points, not gates; this census is deliberately NOT a second harness.</item>
/// <item><b>It does not evaluate thresholds.</b> A gate reading
/// <c>RelationshipBands.For(…) &gt;= RelationshipBand.Regular</c> passes on the writability of its
/// inputs; whether <c>For</c> can ever RETURN <c>Regular</c> is invisible to it. The whole
/// numeric-threshold gate family is out of scope for the same reason — <c>DirectorSystem.Catalog</c>'s
/// <c>MinProgressionTier</c>/<c>MinSurvived</c>, <c>VenueDefinition.Gate</c>'s per-floor power floor,
/// <c>TalentTree.ForgeTierRequirement</c>, each profession's <c>TierGate</c>. "Can this measured
/// quantity ever reach this number" is a balance measurement, not an input-writability question, and
/// answering it with this mechanism would be a false receipt (see <see cref="Report_TheGateCorpusAndItsTerms"/>
/// and §11 P2-HONEST-07's ledger row).</item>
/// <item><b>It is a structural text scan, not a parser or a type checker</b> (same disclaimer as
/// <c>GearWornCheckCensusTests</c> and <c>ClientAuthorityCensusTests</c>). Write evidence is matched
/// by property NAME — a <c>with { P = … }</c> in text does not name its target type — so a live
/// <c>P</c> on one record can exonerate a dead <c>P</c> on another. That collision direction only
/// ever costs a MISS, never a false accusation, which is the safe direction for a guard whose
/// failure message accuses code of being dead.</item>
/// <item><b>It says nothing about ORDER.</b> A term written only after the surface has stopped
/// mattering still counts as writable.</item>
/// </list></para>
///
/// <para><b>Derived, never hand-listed.</b> The gate corpus is discovered four ways, every one
/// anchored on a TYPE SIGNATURE rather than a name — a name-based search misses forks under other
/// names, which has bitten this repo twice (a private <c>AwaitFrames</c> invisible to a
/// <c>SettleLayout</c> sweep; <c>ForgePanel</c>'s cloned mentor banner):
/// <list type="number">
/// <item><b>Predicate slots, by name.</b> Any parameter or field whose type is
/// <c>Func&lt;GameState, …, bool&gt;</c> declares a gate slot; gates are harvested from that slot's
/// named-argument supply sites (<c>IsDone:</c>, <c>Predicate:</c>, <c>AnchorExists:</c> — read off
/// the declarations, never typed here).</item>
/// <item><b>Predicate slots, positionally.</b> A lambda whose argument index matches a
/// predicate-carrying record's own gate index (<c>SurfaceUnlocks.Gates</c>' rows pass their
/// predicate positionally). The index match is what stops a <c>Func&lt;GameState, PlayerAction?&gt;</c>
/// lambda in the same construction from being censused as a gate.</item>
/// <item><b>Predicate tables.</b> A collection or dictionary whose VALUE type is
/// <c>Func&lt;GameState, …, bool&gt;</c> — <c>ArcScenes.WorldFacts</c> is one
/// (<c>ImmutableDictionary&lt;string, Func&lt;GameState, Hero, bool&gt;&gt;</c>), and shapes 1 and 2
/// could not see it: its gates arrive as dictionary-initializer entries, not constructor arguments.
/// It was found by a deliberate fork-hunting sweep rather than by assuming the two tables I already
/// knew about were all of them.</item>
/// <item><b>State-predicate methods.</b> Any method whose signature is <c>bool Name(GameState x)</c>
/// — a predicate over the campaign state ALONE. Methods taking further arguments
/// (<c>ActionLegality.CraftLegal(GameState, CraftAction)</c> and its two dozen siblings) are excluded
/// by ARITY, not by name: their satisfiability is a question about the action, and
/// <c>Balance/VerbConsequenceFloorTests</c> plus <c>SixDilemmasLivenessTests</c> already own it.</item>
/// </list>
/// <see cref="Discovery_FindsEveryGateShape_AndTheKnownTables"/> pins that discovery actually works,
/// because a census that silently discovers nothing is the exact false-coverage shape this whole
/// program exists to delete.</para>
///
/// <para><b>Deny by default (P2-KTD3).</b> A gate whose body is a literal CONSTANT is named — the one
/// unsatisfiability verdict this census reaches exactly rather than heuristically. A gate whose body
/// yields no recognizable term is named too, because "the extractor did not understand this" must
/// never read as "this gate is fine". Both doors out are a pinned exception citing the ruling that
/// grants it, and <see cref="ExceptionCount_IsPinned_SoEveryNewGrantIsAVisibleDiff"/> makes every
/// admission a reviewed diff in a compiled file (rule 12's shape).</para>
/// </summary>
public class SatisfiableGateCensusTests
{
    private readonly ITestOutputHelper _output;

    public SatisfiableGateCensusTests(ITestOutputHelper output) => _output = output;

    // =======================================================================================
    // The exception ledger.
    // =======================================================================================

    /// <summary>Gate name → reason citing the ruling that grants it. Same citation contract as
    /// <c>GearWornCheckCensusTests.Exceptions</c> and <c>ClientAuthorityCensusTests.Exceptions</c>:
    /// an exception with no ruling behind it is drift wearing a reason.</summary>
    private static readonly Dictionary<string, string> Exceptions = new(StringComparer.Ordinal)
    {
        // The single constant-body gate in the tree, found by this census's first run, and the
        // honest case for one: a UI-only step whose completion is "the player opened Hero Cards and
        // read someone's card", a fact no GameState field encodes.
        // TutorialFlow.NotifyPanelOpened advances the row directly on the panel-open callback, and
        // that live alternative path is exactly the evidence a constant gate owes.
        ["TutorialFlow.cs:TutorialFlow.IsDone[MeetHeroes]"] =
            "P2-HONEST-07: UI-only step, advanced by TutorialFlow.NotifyPanelOpened on panel open, "
            + "not by any GameState fact (the row's own comment: \"UI-only, same shape as LookIn — "
            + "NotifyPanelOpened advances this directly\"). Constant-false plus a live non-state "
            + "advance path is the honest encoding of \"the player looked\", not a wall — unlike "
            + "P2-HONEST-01's Progress gate, which had no other path at all.",
    };

    private const int ExpectedExceptionCount = 1;

    [Fact]
    public void ExceptionCount_IsPinned_SoEveryNewGrantIsAVisibleDiff()
        => Assert.True(Exceptions.Count == ExpectedExceptionCount,
            $"Pinned at {ExpectedExceptionCount}; the table now holds {Exceptions.Count}. Every "
            + "admission here is meant to be a reviewed diff in a compiled file (rule 12).");

    [Fact]
    public void EveryPinnedException_CitesTheRulingThatGrantedIt()
    {
        var citation = new Regex(@"§11\.7|\bP\d+[A-Za-z0-9-]*", RegexOptions.Compiled);
        var uncited = Exceptions.Where(e => !citation.IsMatch(e.Value)).Select(e => e.Key).ToList();

        Assert.True(uncited.Count == 0,
            "An exception with no ruling behind it is drift wearing a reason:\n  "
            + string.Join("\n  ", uncited));
    }

    // =======================================================================================
    // THE CENSUS.
    // =======================================================================================

    [Fact]
    public void EveryGateInput_IsEverWrittenToANonDefaultValue()
    {
        var evidence = WriteEvidence.Instance;
        var violations = new List<string>();

        foreach (var gate in Corpus.Gates)
        {
            if (Exceptions.ContainsKey(gate.Name))
            {
                continue;
            }

            if (gate.IsConstantBody)
            {
                violations.Add(
                    $"{gate.Name} — CONSTANT body `{Flatten(gate.Body, 90)}`. A predicate that can "
                    + "never change is not a gate, it is a wall (P2-HONEST-01). If a non-state path "
                    + "really advances past it, pin it with that path named.");
                continue;
            }

            var terms = Corpus.TermsFor(gate);
            if (terms.Count == 0)
            {
                violations.Add(
                    $"{gate.Name} — NO recognizable input term in `{Flatten(gate.Body, 120)}`. The "
                    + "extractor could not see what this gate reads, and \"not understood\" must "
                    + "never read as \"fine\". Extend TermExtractor, or pin a cited exception.");
                continue;
            }

            foreach (var term in terms.Where(t => !evidence.CanMove(t)))
            {
                violations.Add($"{gate.Name} — {term.Describe()} is DEAD INPUT: {evidence.WhyNot(term)}");
            }
        }

        Assert.True(violations.Count == 0,
            "A gate predicate reads an input nothing ever writes to a non-default value — "
            + "P2-HONEST-01's exact shape (Bounty.Paid: constructed false, never flipped, so the "
            + "Progress board could not open for any player by any action sequence). Fix the gate or "
            + "the missing write; never soften this assertion — a relaxed satisfiability census is "
            + "worthless:\n  " + string.Join("\n  ", violations));
    }

    // =======================================================================================
    // Discovery must provably work — a census that finds nothing is the bug it exists to catch.
    // =======================================================================================

    [Fact]
    public void Discovery_FindsEveryGateShape_AndTheKnownTables()
    {
        var gates = Corpus.Gates;

        foreach (var shape in Enum.GetValues<GateShape>())
        {
            Assert.True(gates.Any(g => g.Shape == shape),
                $"Discovery shape {shape} found nothing. Every shape had live members when this "
                + "census was written; a shape going silent makes its whole family vacuously green, "
                + "which is the exact false-coverage failure this file exists to delete.");
        }

        var slots = gates.Count(g => g.Shape is GateShape.NamedSlot or GateShape.PositionalSlot);
        Assert.True(slots >= 15,
            $"Only {slots} Func<GameState, …, bool> slot gates discovered — SurfaceUnlocks.Gates "
            + "alone declares seven and TutorialFlow.Registry eleven.");

        var methods = gates.Count(g => g.Shape == GateShape.StatePredicateMethod);
        Assert.True(methods >= 15,
            $"Only {methods} `bool Name(GameState x)` predicates discovered; the tree held nineteen "
            + "when this census was written.");

        var trayGates = gates.Count(g => g.Name.StartsWith("SurfaceUnlocks.cs:", StringComparison.Ordinal));
        Assert.True(trayGates == 7,
            $"Expected SurfaceUnlocks' seven tray gates, discovered {trayGates}. This anchor is meant "
            + "to move only on purpose — an eighth tray book is a real widening; a drop to six is the "
            + "positional-lambda scan silently failing.");

        // Proof that extraction really READ those bodies rather than just counting rows: the tray
        // table's own declared event vocabulary must come back out of the extractor.
        var trayEvents = gates
            .Where(g => g.Name.StartsWith("SurfaceUnlocks.cs:", StringComparison.Ordinal))
            .SelectMany(Corpus.TermsFor)
            .Where(t => t.Kind == TermKind.Event)
            .Select(t => t.Key)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var expected in new[] { "PartyDeparted", "ItemSold", "CommissionPosted", "HeroPassedOnItem", "HeroDied" })
        {
            Assert.True(trayEvents.Contains(expected),
                $"SurfaceUnlocks' gate bodies name {expected} but the term extractor did not produce "
                + $"it. Extracted: {string.Join(", ", trayEvents.OrderBy(e => e, StringComparer.Ordinal))}.");
        }
    }

    [Fact]
    public void Discovery_ReadsSlotNamesOffTheDeclarations_NeverAHandTypedList()
    {
        var slotNames = Corpus.PredicateSlotNames;

        // What the tree declares today. The assertion is that discovery FOUND these by reading
        // `Func<GameState, …, bool>` declarations — not that the set is fixed.
        foreach (var expected in new[] { "Predicate", "IsDone", "AnchorExists" })
        {
            Assert.True(slotNames.Contains(expected),
                $"`{expected}` is declared as a Func<GameState, …, bool> in the tree but discovery "
                + "did not read it off the declaration. Found: "
                + string.Join(", ", slotNames.OrderBy(n => n, StringComparer.Ordinal)));
        }
    }

    /// <summary>The two-argument table shape is not a hypothetical widening: <c>ArcScenes.WorldFacts</c>
    /// is a live <c>ImmutableDictionary&lt;string, Func&lt;GameState, Hero, bool&gt;&gt;</c> whose gates
    /// arrive as dictionary-initializer entries, invisible to both slot shapes. A discovery that
    /// silently stopped covering it would take a whole narrative-gating family with it.</summary>
    [Fact]
    public void Discovery_CoversTheTwoArgumentPredicateTable()
    {
        var arc = Corpus.Gates
            .Where(g => g.Shape == GateShape.PredicateTable
                        && g.Name.StartsWith("ArcScenes.cs:", StringComparison.Ordinal))
            .ToList();

        Assert.True(arc.Count >= 3,
            $"Discovered {arc.Count} ArcScenes.WorldFacts gates; the table declares three. The "
            + "predicate-table shape has stopped seeing dictionary-initializer gates.");

        var events = arc.SelectMany(Corpus.TermsFor)
            .Where(t => t.Kind == TermKind.Event)
            .Select(t => t.Key)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("FloorRecordSet", events);
    }

    /// <summary>The regression proof, and it needs no fabricated fixture: <see cref="Bounty.Paid"/> is
    /// STILL only ever constructed <c>false</c> on the live tree, so the historical gate body fed to
    /// this census's own detector must come back named. A guard that cannot fail is not a guard.</summary>
    [Fact]
    public void RegressionProof_TheActualP2Honest01GateBody_IsNamedAsDeadInput()
    {
        var gate = new Gate("fixture:P2-HONEST-01", GateShape.NamedSlot,
            "state.Bounties.Any(b => b.Paid)", null);

        var terms = Corpus.TermsFor(gate);
        var paid = terms.SingleOrDefault(t => t.Kind == TermKind.Field && t.Key == "Paid");

        Assert.True(paid is not null,
            "The extractor no longer produces a `Paid` field term from the historical gate body — got: "
            + string.Join(", ", terms.Select(t => t.Describe())));

        Assert.False(WriteEvidence.Instance.CanMove(paid!),
            "Bounty.Paid now has a non-default write somewhere in sim/GameSim. If that is real, this "
            + "regression proof needs RE-AIMING at a still-dead input, never deleting.");
    }

    /// <summary>The plant test: a fabricated gate reading an event type nothing constructs, and one
    /// reading a client latch nothing sets, must both be named. Proves the detector's teeth
    /// independently of any real defect still existing in the tree to find.</summary>
    [Fact]
    public void PlantedUnsatisfiableGates_AreNamedByTheCensus()
    {
        var evidence = WriteEvidence.Instance;

        var plantedEvent = new Gate("fixture:planted-event", GateShape.NamedSlot,
            "state.EventLog.OfType<TheMineApologized>().Any()", null);
        Assert.DoesNotContain(Corpus.TermsFor(plantedEvent), t => t.Kind == TermKind.Event);

        // A fabricated name is not a GameEvent type at all, so it yields no term — which would read
        // as "no recognizable input", the census's OTHER deny-by-default verdict. Plant the shape
        // that actually matters instead: a REAL event type with no construction site anywhere.
        var deadEvent = new Term(TermKind.Event, "TheMineApologized", string.Empty);
        Assert.False(evidence.CanMove(deadEvent),
            "An event type nothing in sim/GameSim ever constructs read as movable.");

        var plantedLatch = new Gate("fixture:planted-latch", GateShape.StatePredicateMethod,
            "_theAnvilForgaveYou && state.Day > 1", null);
        var latch = Assert.Single(Corpus.TermsFor(plantedLatch), t => t.Kind == TermKind.ClientField);
        Assert.False(evidence.CanMove(latch),
            "A client latch nothing in godot/scripts ever assigns read as movable.");

        var constant = new Gate("fixture:planted-constant", GateShape.NamedSlot, "false", null);
        Assert.True(constant.IsConstantBody,
            "A constant-false gate body is no longer recognized as constant — the one exact "
            + "unsatisfiability verdict this census can reach.");
    }

    /// <summary>
    /// The derived-property substitution is the one piece of this census that can silently DELETE
    /// requirements, and it already did: keyed by property name and testing only "not a constructor
    /// parameter", it swallowed every <c>{ get; init; }</c> auto-property in Contracts and emptied
    /// the term lists of four real gates (<c>state.Day &gt;= 3</c> among them) — a census reporting
    /// green because it had stopped reading. So the map is asserted BOTH ways: the real formulas are
    /// in it, and the state fields gates key on are not.
    /// </summary>
    [Fact]
    public void DerivedPropertySubstitution_CoversFormulasAndNothingThatHoldsState()
    {
        var derived = TermExtractor.DerivedForTests;

        foreach (var formula in new[] { "IsSigned", "IsHeirloom", "PlayerCrafted" })
        {
            Assert.True(derived.ContainsKey(formula),
                $"Item.{formula} is a getter-only formula over a stored field, but the substitution "
                + "map does not hold it — the three gates that read it would go back to reporting "
                + "dead input for a property that can never have a write.");
        }

        foreach (var stateField in new[] { "Day", "Phase", "Counter", "EventLog", "ActionLog", "Bounties", "Paid", "Shelf" })
        {
            Assert.False(derived.ContainsKey(stateField),
                $"`{stateField}` holds state, so substituting a formula for it drops the census's "
                + "only real requirement on every gate that reads it. This is the exact regression "
                + "that emptied four gates' term lists during this unit's own first run.");
        }

        // Substitution must RE-LAND the requirement, not merely remove it. A formula whose walk
        // yields nothing is indistinguishable from an exemption, and that is the second way this
        // census caught itself not reading: `PlayerCrafted => Mark is not null` names its field
        // BARE, so a dotted-access scan came back empty and silently exonerated three gates.
        var probe = new Gate("fixture:derived", GateShape.StatePredicateMethod,
            "state.Items.Values.Any(i => i.PlayerCrafted)", null);
        var substituted = Corpus.TermsFor(probe);

        Assert.Contains(substituted, t => t.Kind == TermKind.Field && t.Key == "Mark");
        Assert.DoesNotContain(substituted, t => t.Key == "PlayerCrafted");
    }

    [Fact]
    public void NegativeControl_LiveGatesAreNotFlagged()
    {
        var evidence = WriteEvidence.Instance;

        var live = new Gate("fixture:live", GateShape.NamedSlot,
            "state.EventLog.OfType<PartyDeparted>().Any() && state.Day > 1 && state.Player.Shelf.Count > 0",
            null);

        var terms = Corpus.TermsFor(live);
        Assert.Contains(terms, t => t.Kind == TermKind.Event && t.Key == "PartyDeparted");
        Assert.Contains(terms, t => t.Kind == TermKind.Field && t.Key == "Day");
        Assert.Contains(terms, t => t.Kind == TermKind.Field && t.Key == "Shelf");

        var dead = terms.Where(t => !evidence.CanMove(t)).Select(t => t.Describe()).ToList();
        Assert.True(dead.Count == 0,
            "A gate the game demonstrably opens was flagged — the detector is over-firing: "
            + string.Join(", ", dead));
    }

    /// <summary>Census OUTPUT, not an assertion (the shape <c>BalanceCorpusCoverageCensusTests</c>
    /// uses): the whole gate corpus with the terms the extractor understood, so a reader can see what
    /// the census actually read rather than trusting that it read anything.</summary>
    [Fact]
    public void Report_TheGateCorpusAndItsTerms()
    {
        var gates = Corpus.Gates;
        _output.WriteLine($"{gates.Count} gate predicates discovered:");
        foreach (var group in gates.GroupBy(g => g.Shape).OrderBy(g => g.Key.ToString(), StringComparer.Ordinal))
        {
            _output.WriteLine($"  {group.Key}: {group.Count()}");
        }

        _output.WriteLine(string.Empty);

        foreach (var gate in gates.OrderBy(g => g.Name, StringComparer.Ordinal))
        {
            var terms = Corpus.TermsFor(gate);
            var shown = gate.IsConstantBody
                ? "(constant)"
                : terms.Count == 0
                    ? "(none understood)"
                    : string.Join(", ", terms.Select(t => t.Describe()).OrderBy(t => t, StringComparer.Ordinal));
            _output.WriteLine(gate.Name);
            _output.WriteLine($"    {shown}");
        }
    }

    // =======================================================================================
    // Gate model.
    // =======================================================================================

    private enum GateShape { NamedSlot, PositionalSlot, PredicateTable, StatePredicateMethod }

    private sealed record Gate(string Name, GateShape Shape, string Body, string? File)
    {
        /// <summary>A body that is a literal constant — the one unsatisfiability verdict this census
        /// reaches exactly rather than heuristically.</summary>
        public bool IsConstantBody => ConstantBody.IsMatch(Body);

        private static readonly Regex ConstantBody = new(@"^\s*(true|false)\s*;?\s*$", RegexOptions.Compiled);
    }

    private enum TermKind { Event, Field, ClientField }

    private sealed record Term(TermKind Kind, string Key, string Owners)
    {
        public string Describe() => Kind switch
        {
            TermKind.Event => $"event {Key}",
            TermKind.Field => $"field {Owners}.{Key}",
            _ => $"client field {Key}",
        };
    }

    // =======================================================================================
    // Discovery + term extraction.
    // =======================================================================================

    private static class Corpus
    {
        public static List<Gate> Gates => LazyGates.Value;

        public static HashSet<string> PredicateSlotNames => LazySlotNames.Value;

        public static List<Term> TermsFor(Gate gate) => TermExtractor.Extract(gate);

        // ---- gate-slot type recognition -------------------------------------------------

        /// <summary>A <c>Func&lt;…&gt;</c> whose FIRST type argument is <c>GameState</c> and whose LAST
        /// is <c>bool</c> — so a two-argument narrative fact (<c>Func&lt;GameState, Hero, bool&gt;</c>)
        /// counts and a <c>Func&lt;GameState, PlayerAction?&gt;</c> does not.</summary>
        private static bool IsGatePredicateType(string typeArgs)
        {
            var args = typeArgs.Split(',').Select(a => a.Trim()).ToList();
            return args.Count >= 2
                && args[0] == "GameState"
                && args[^1] == "bool";
        }

        private static readonly Regex FuncGeneric = new(@"Func\s*<([^<>]*)>", RegexOptions.Compiled);

        private static bool DeclaresGatePredicate(string text)
        {
            foreach (Match m in FuncGeneric.Matches(text))
            {
                if (IsGatePredicateType(m.Groups[1].Value))
                {
                    return true;
                }
            }

            return false;
        }

        private static readonly Lazy<HashSet<string>> LazySlotNames = new(() =>
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (_, code, _) in Sources.All)
            {
                foreach (Match m in Regex.Matches(code, @"Func\s*<([^<>]*)>\??\s+(\w+)"))
                {
                    if (IsGatePredicateType(m.Groups[1].Value))
                    {
                        names.Add(m.Groups[2].Value);
                    }
                }
            }

            return names;
        });

        /// <summary>Record types holding at least one gate-predicate primary-constructor parameter,
        /// mapped to the top-level indexes those parameters sit at — the positional half of
        /// discovery.</summary>
        private static readonly Lazy<Dictionary<string, HashSet<int>>> LazyCarryingTypes = new(() =>
        {
            var found = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
            foreach (var (_, code, _) in Sources.All)
            {
                foreach (Match m in Regex.Matches(code, @"record\s+(?:struct\s+)?(\w+)\s*\("))
                {
                    var open = m.Index + m.Length - 1;
                    var close = Balance.MatchingBracket(code, open);
                    if (close < 0)
                    {
                        continue;
                    }

                    var parameters = Balance.SplitParams(code[(open + 1)..close]);
                    var indexes = new HashSet<int>();
                    for (var i = 0; i < parameters.Count; i++)
                    {
                        if (DeclaresGatePredicate(parameters[i]))
                        {
                            indexes.Add(i);
                        }
                    }

                    if (indexes.Count > 0)
                    {
                        found[m.Groups[1].Value] = indexes;
                    }
                }
            }

            return found;
        });

        // ---- the four discovery shapes --------------------------------------------------

        private static readonly Lazy<List<Gate>> LazyGates = new(() =>
        {
            var gates = new List<Gate>();
            var slotNames = LazySlotNames.Value;
            var carrying = LazyCarryingTypes.Value;

            foreach (var (file, code, raw) in Sources.All)
            {
                var name = Path.GetFileName(file);
                gates.AddRange(NamedSlotGates(name, code, raw, slotNames));
                gates.AddRange(PositionalSlotGates(name, code, raw, carrying));
                gates.AddRange(PredicateTableGates(name, code));
                gates.AddRange(StatePredicateMethodGates(name, code));
            }

            return gates
                .GroupBy(g => (g.Name, g.Body))
                .Select(g => g.First())
                .ToList();
        });

        /// <summary>Shape 1: <c>IsDone: state =&gt; …</c>.</summary>
        private static IEnumerable<Gate> NamedSlotGates(string file, string code, string raw, HashSet<string> slotNames)
        {
            foreach (var slot in slotNames)
            {
                foreach (Match m in Regex.Matches(code, $@"(?<![\w.]){Regex.Escape(slot)}\s*:\s*"))
                {
                    // A `Func<GameState, bool> IsDone,` DECLARATION is not a supply site.
                    var lineStart = code.LastIndexOf('\n', Math.Max(0, m.Index - 1)) + 1;
                    if (DeclaresGatePredicate(code[lineStart..m.Index]))
                    {
                        continue;
                    }

                    var supplied = Balance.ExpressionAt(code, m.Index + m.Length);
                    var (body, via) = SuppliedBody(supplied, file);
                    if (string.IsNullOrWhiteSpace(body))
                    {
                        continue;
                    }

                    yield return new Gate(
                        $"{file}:{OwningType(code, m.Index)}.{slot}{RowTag(code, raw, m.Index)}{via}",
                        GateShape.NamedSlot, body, file);
                }
            }
        }

        /// <summary>Shape 2: a lambda at an argument index a predicate-carrying record declares a
        /// gate at.</summary>
        private static IEnumerable<Gate> PositionalSlotGates(
            string file, string code, string raw, Dictionary<string, HashSet<int>> carrying)
        {
            var targetTyped = new HashSet<int>();
            foreach (var (type, indexes) in carrying)
            {
                if (Regex.IsMatch(code, $@"(?<!\w){Regex.Escape(type)}(?!\w)"))
                {
                    targetTyped.UnionWith(indexes);
                }
            }

            if (targetTyped.Count == 0)
            {
                yield break;
            }

            foreach (Match m in Regex.Matches(code, @"new(?:\s+(\w+))?\s*\("))
            {
                var explicitType = m.Groups[1].Success ? m.Groups[1].Value : null;
                if (explicitType is not null && !carrying.ContainsKey(explicitType))
                {
                    continue;
                }

                var indexes = explicitType is not null ? carrying[explicitType] : targetTyped;
                var open = m.Index + m.Length - 1;
                var close = Balance.MatchingBracket(code, open);
                if (close < 0)
                {
                    continue;
                }

                var args = Balance.SplitArgs(code[(open + 1)..close]);
                foreach (var i in indexes.Where(i => i < args.Count))
                {
                    var (body, via) = SuppliedBody(args[i], file);
                    if (string.IsNullOrWhiteSpace(body))
                    {
                        continue;
                    }

                    yield return new Gate(
                        $"{file}:{explicitType ?? OwningType(code, m.Index)}[{i}]{RowTag(code, raw, m.Index)}{via}",
                        GateShape.PositionalSlot, body, file);
                }
            }
        }

        /// <summary>Shape 3: a collection or dictionary whose VALUE type is a gate predicate — the
        /// gates arrive as initializer entries, not constructor arguments.</summary>
        private static IEnumerable<Gate> PredicateTableGates(string file, string code)
        {
            foreach (Match m in FuncGeneric.Matches(code))
            {
                if (!IsGatePredicateType(m.Groups[1].Value))
                {
                    continue;
                }

                // Nested inside another generic (`…<string, Func<GameState, Hero, bool>>`) is what
                // makes this a TABLE of predicates rather than a single slot.
                var after = m.Index + m.Length;
                if (after >= code.Length || code[after] != '>')
                {
                    continue;
                }

                var brace = code.IndexOf('{', after);
                if (brace < 0)
                {
                    continue;
                }

                // Only an initializer that follows immediately — anything else is a different block.
                if (code[after..brace].Any(c => c is ';' or '}'))
                {
                    continue;
                }

                var close = Balance.MatchingBracket(code, brace);
                if (close < 0)
                {
                    continue;
                }

                foreach (var entry in Balance.SplitArgs(code[(brace + 1)..close]))
                {
                    var value = Regex.Match(entry, @"^\s*(?:\[[^\]]*\]\s*=\s*)?(?:static\s+)?(.+)$", RegexOptions.Singleline);
                    var body = LambdaBody(value.Groups[1].Value);
                    if (string.IsNullOrWhiteSpace(body))
                    {
                        continue;
                    }


                    var key = Regex.Match(entry, @"^\s*\[\s*([\w.]+)\s*\]");
                    yield return new Gate(
                        $"{file}:{OwningType(code, m.Index)}[{(key.Success ? key.Groups[1].Value : "?")}]",
                        GateShape.PredicateTable, body, file);
                }
            }
        }

        /// <summary>Shape 4: <c>bool Name(GameState x)</c> — a predicate over the campaign state
        /// alone. Further arguments are excluded by arity, never by name.</summary>
        private static IEnumerable<Gate> StatePredicateMethodGates(string file, string code)
        {
            foreach (Match m in Regex.Matches(code, @"\bbool\s+(\w+)\s*\(\s*GameState\s+\w+\s*\)"))
            {
                var body = Balance.MethodBody(code, m.Index + m.Length);
                if (!string.IsNullOrWhiteSpace(body))
                {
                    yield return new Gate($"{file}:{m.Groups[1].Value}()", GateShape.StatePredicateMethod, body, file);
                }
            }
        }

        /// <summary>The body of a lambda — one parameter (<c>state =&gt; …</c>, <c>_ =&gt; …</c>) or
        /// several (<c>(state, hero) =&gt; …</c>), with or without <c>static</c>. Empty for anything
        /// that is not a lambda.</summary>
        private static string LambdaBody(string text)
        {
            var m = Regex.Match(text.Trim(),
                @"^(?:static\s+)?(?:(\w+)|\(\s*\w+(?:\s*,\s*\w+)*\s*\))\s*=>\s*", RegexOptions.Singleline);
            return m.Success ? text.Trim()[m.Length..].Trim() : string.Empty;
        }

        /// <summary>What a gate slot was actually supplied: a lambda body, or — when the slot was
        /// handed a METHOD GROUP (<c>IsDone: CounterAnsweredAtLeastOnce</c>) — that method's own body,
        /// so the slot is censused instead of silently dropped. Two of the eighteen slot gates in the
        /// tree are supplied this way, including <c>SurfaceUnlocks</c>' Progress row — the very gate
        /// P2-HONEST-01 was about.</summary>
        private static (string Body, string Via) SuppliedBody(string text, string? file)
        {
            var lambda = LambdaBody(text);
            if (!string.IsNullOrWhiteSpace(lambda))
            {
                return (lambda, string.Empty);
            }

            var group = Regex.Match(text.Trim(), @"^(?:\w+\s*\.\s*)*(\w+)$");
            if (!group.Success)
            {
                return (string.Empty, string.Empty);
            }

            var resolved = MethodIndex.ResolveGroup(group.Groups[1].Value, file);
            return resolved is null
                ? (string.Empty, string.Empty)
                : (resolved, $"->{group.Groups[1].Value}");
        }

        /// <summary>The enclosing type name for an offset — the nearest preceding type declaration.
        /// Labels only; a mislabel never changes a verdict.</summary>
        private static string OwningType(string code, int offset)
        {
            var best = "?";
            foreach (Match m in Regex.Matches(code[..offset], @"\b(?:class|record|struct|interface)\s+(?:struct\s+)?(\w+)"))
            {
                best = m.Groups[1].Value;
            }

            return best;
        }

        /// <summary>A gate's own row identity where its construction declares one — the nearest
        /// preceding <c>Step: TutorialStep.X</c>, or the row's leading string literal read out of the
        /// RAW source at the same offset (stripping is length-preserving, so the offsets align) — so
        /// a violation names the ROW rather than "one of eleven" or "one of seven".</summary>
        private static string RowTag(string code, string raw, int offset)
        {
            string? step = null;
            foreach (Match m in Regex.Matches(code[..offset], @"(?<!\w)Step\s*:\s*TutorialStep\.(\w+)"))
            {
                step = m.Groups[1].Value;
            }

            if (step is not null)
            {
                return $"[{step}]";
            }

            var window = raw[offset..Math.Min(raw.Length, offset + 200)];
            var id = Regex.Match(window, @"^\s*new(?:\s+\w+)?\s*\(\s*""([^""]{1,40})""");
            return id.Success ? $"[\"{id.Groups[1].Value}\"]" : string.Empty;
        }
    }

    /// <summary>Terms a gate reads. Inlines one level of same-tree helper calls, because a gate that
    /// delegates its whole condition to a helper (<c>MarkedPieceName(state, hero) is not null</c>)
    /// otherwise reads as "no recognizable input" — a shrug where a verdict belongs.</summary>
    private static class TermExtractor
    {
        private static readonly Lazy<Dictionary<string, string[]>> ContractProperties = new(() =>
        {
            var byName = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var type in ContractRecords())
            {
                foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!byName.TryGetValue(p.Name, out var owners))
                    {
                        byName[p.Name] = owners = [];
                    }

                    if (!owners.Contains(type.Name))
                    {
                        owners.Add(type.Name);
                    }
                }
            }

            return byName.ToDictionary(
                e => e.Key,
                e => e.Value.OrderBy(o => o, StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        });

        /// <summary>
        /// DERIVED Contracts properties — a public property that is NOT a primary-constructor
        /// parameter of its own record (<c>Item.PlayerCrafted =&gt; Mark is not null</c>,
        /// <c>Item.IsSigned =&gt; SignedName is not null</c>, <c>Item.IsHeirloom</c>,
        /// <c>Item.Modifiers</c>) — mapped to their own declaration bodies.
        ///
        /// <para>These carry no state of their own, so they can never have a write, and a first pass
        /// duly reported three real gates as reading dead input. The fix is a SUBSTITUTION, not an
        /// exemption: a term that resolves to a derived property is replaced by the terms of the
        /// expression it derives from, so <c>CampPanel.AnySendableConsumableIsShelved</c> is now
        /// censused on <c>Item.Mark</c> — the field that actually has to move — instead of on a
        /// property that is a formula. Exempting them would have been the softening move and would
        /// have dropped the only real requirement those three gates have.</para>
        /// </summary>
        private static readonly Lazy<Dictionary<string, List<(string Owner, string Body)>>> DerivedProperties = new(() =>
        {
            var contractSources = Sources.All
                .Where(s => s.Path.Replace('\\', '/').Contains("/Contracts/", StringComparison.Ordinal))
                .ToList();

            var derived = new Dictionary<string, List<(string, string)>>(StringComparer.Ordinal);
            foreach (var type in ContractRecords())
            {
                var ctorParams = type.GetConstructors()
                    .OrderByDescending(c => c.GetParameters().Length)
                    .FirstOrDefault()
                    ?.GetParameters()
                    .Select(p => p.Name ?? string.Empty)
                    .ToHashSet(StringComparer.Ordinal) ?? [];

                foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    // Not a constructor parameter AND has no setter at all. Both halves matter: a
                    // first pass tested only the first, so every `{ get; init; }` auto-property in
                    // Contracts joined the map and — because the map is keyed by NAME — suppressed
                    // `GameState.Day` and `GameState.Counter` as "formulas", silently emptying four
                    // real gates' term lists. An auto-property holds state; only a getter-only
                    // property is a formula.
                    if (ctorParams.Contains(p.Name) || p.CanWrite)
                    {
                        continue;
                    }

                    foreach (var (_, code, _) in contractSources)
                    {
                        foreach (Match m in Regex.Matches(code, $@"(?<![\w.]){Regex.Escape(p.Name)}\s*(?==>|\{{)"))
                        {
                            var body = Balance.MethodBody(code, m.Index + m.Length);
                            if (string.IsNullOrWhiteSpace(body))
                            {
                                continue;
                            }

                            if (!derived.TryGetValue(p.Name, out var bodies))
                            {
                                derived[p.Name] = bodies = [];
                            }

                            bodies.Add((type.Name, body));
                        }
                    }
                }
            }

            return derived;
        });

        /// <summary>The substitution map, for
        /// <see cref="DerivedPropertySubstitution_CoversFormulasAndNothingThatHoldsState"/> — the
        /// only mechanism here that can delete a requirement, so it is asserted both ways.</summary>
        public static IReadOnlyDictionary<string, List<(string Owner, string Body)>> DerivedForTests =>
            DerivedProperties.Value;

        /// <summary>Property names declared by each Contracts record — the scope a substituted
        /// formula's BARE identifiers resolve against.</summary>
        private static readonly Lazy<Dictionary<string, HashSet<string>>> PropertiesByType = new(() =>
            ContractRecords().ToDictionary(
                t => t.Name,
                t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Select(p => p.Name)
                    .ToHashSet(StringComparer.Ordinal),
                StringComparer.Ordinal));

        private static readonly Lazy<HashSet<string>> EventTypes = new(() =>
            typeof(GameEvent).Assembly.GetTypes()
                .Where(t => t.IsSubclassOf(typeof(GameEvent)))
                .Select(t => t.Name)
                .ToHashSet(StringComparer.Ordinal));

        private static readonly Dictionary<string, List<Term>> Memo = new(StringComparer.Ordinal);

        public static List<Term> Extract(Gate gate)
        {
            var key = gate.Name + "|" + gate.Body;
            lock (Memo)
            {
                if (Memo.TryGetValue(key, out var cached))
                {
                    return cached;
                }

                var terms = new List<Term>();
                Walk(gate.Body, gate.File, depth: 0, terms, new HashSet<string>(StringComparer.Ordinal));
                var distinct = terms.Distinct().ToList();
                Memo[key] = distinct;
                return distinct;
            }
        }

        /// <summary>Collects the terms <paramref name="body"/> reads. <paramref name="bareScope"/> is
        /// non-null only while walking a SUBSTITUTED derived-property formula: those bodies say
        /// <c>Mark is not null</c>, with an implicit <c>this.</c>, so a dotted-access scan alone came
        /// back empty and quietly dropped the requirement it had just substituted in — a second
        /// instance of this census failing to read, caught during its own first run. Inside a formula
        /// the declaring record's own property names are resolved bare; everywhere else they are not,
        /// because a local variable named <c>day</c> is not <c>GameState.Day</c>.</summary>
        private static void Walk(
            string body, string? file, int depth, List<Term> terms, HashSet<string> visited,
            HashSet<string>? bareScope = null)
        {
            foreach (Match m in Regex.Matches(body, @"OfType\s*<\s*(\w+)\s*>"))
            {
                if (EventTypes.Value.Contains(m.Groups[1].Value))
                {
                    terms.Add(new Term(TermKind.Event, m.Groups[1].Value, string.Empty));
                }
            }

            foreach (Match m in Regex.Matches(body, @"\.\s*(\w+)"))
            {
                var member = m.Groups[1].Value;

                // A derived property is a formula, not an input: substitute the terms of whatever it
                // derives from, so the requirement lands on the field that actually has to move.
                if (DerivedProperties.Value.TryGetValue(member, out var formulas))
                {
                    if (visited.Add("=" + member))
                    {
                        foreach (var (owner, formula) in formulas)
                        {
                            PropertiesByType.Value.TryGetValue(owner, out var scope);
                            Walk(formula, file, depth, terms, visited, scope);
                        }
                    }

                    continue;
                }

                if (ContractProperties.Value.TryGetValue(member, out var owners))
                {
                    // Owners are a LABEL, not the lookup key — write evidence is matched by property
                    // NAME (see the honesty framing), so a long collision list is noise in the
                    // report rather than precision anywhere.
                    var shown = owners.Length <= 3
                        ? string.Join("|", owners)
                        : string.Join("|", owners.Take(3)) + $"|+{owners.Length - 3}";
                    terms.Add(new Term(TermKind.Field, member, shown));
                }
            }

            if (bareScope is not null)
            {
                foreach (Match m in Regex.Matches(body, @"(?<![\w.])(\w+)(?!\s*\()"))
                {
                    var bare = m.Groups[1].Value;
                    if (bareScope.Contains(bare) && ContractProperties.Value.TryGetValue(bare, out var owners))
                    {
                        terms.Add(new Term(TermKind.Field, bare, string.Join("|", owners.Take(3))));
                    }
                }
            }

            foreach (Match m in Regex.Matches(body, @"(?<![\w.])(_\w+)"))
            {
                terms.Add(new Term(TermKind.ClientField, m.Groups[1].Value, string.Empty));
            }

            if (depth >= 1)
            {
                return;
            }

            foreach (Match m in Regex.Matches(body, @"(?<![\w.])(\w+)\s*\("))
            {
                var name = m.Groups[1].Value;
                if (!visited.Add(name))
                {
                    continue;
                }

                foreach (var helper in MethodIndex.Resolve(name, file))
                {
                    Walk(helper, file, depth + 1, terms, visited);
                }
            }
        }
    }

    /// <summary>
    /// Every method body in the scanned tree, by name — the index one-level inlining reads.
    ///
    /// <para><b>Same file only, deliberately.</b> A first pass resolved a helper name tree-wide when
    /// the same file held no match, and the result was <c>ActionLegality.UpgradeForgeLegal</c> reading
    /// thirty-eight client latches and half the event vocabulary: a common helper name
    /// (<c>Compute</c>, <c>For</c>, <c>Line</c>) collides across three hundred files, and a union of
    /// unrelated bodies is a term list about nothing. Cross-file resolution needs real symbol
    /// binding, which a text scan does not have — so this refuses to guess, and a gate whose whole
    /// condition delegates ACROSS files falls through to the census's "no recognizable input term"
    /// verdict instead of to a fabricated one. Under-reach that announces itself, rather than
    /// over-reach that does not.</para>
    /// </summary>
    private static class MethodIndex
    {
        public static Dictionary<string, List<(string File, string Body)>> Bodies => Lazy.Value;

        /// <summary>Bodies declared in <paramref name="file"/> itself.</summary>
        public static IEnumerable<string> Resolve(string name, string? file) =>
            file is not null && Bodies.TryGetValue(name, out var found)
                ? found.Where(f => f.File == file).Select(f => f.Body)
                : [];

        /// <summary>A method group supplied into a gate slot (<c>IsDone: CounterAnsweredAtLeastOnce</c>,
        /// <c>Predicate: TutorialFlow.SecondProfessionMilestoneReached</c>): its body IS the gate's
        /// body, so the slot is censused rather than dropped. Resolves same-file first, then a
        /// tree-wide match only when it is UNIQUE — a colliding name resolves to nothing and the slot
        /// reports as not understood.</summary>
        public static string? ResolveGroup(string name, string? file)
        {
            if (!Bodies.TryGetValue(name, out var found))
            {
                return null;
            }

            var sameFile = found.Where(f => f.File == file).Select(f => f.Body).ToList();
            if (sameFile.Count == 1)
            {
                return sameFile[0];
            }

            return found.Count == 1 ? found[0].Body : null;
        }

        private static readonly Lazy<Dictionary<string, List<(string File, string Body)>>> Lazy = new(() =>
        {
            var index = new Dictionary<string, List<(string, string)>>(StringComparer.Ordinal);
            foreach (var (path, code, _) in Sources.All)
            {
                var file = Path.GetFileName(path);
                foreach (Match m in Regex.Matches(code, @"(?<![\w.])(\w[\w<>,?\[\]\. ]*?)\s+(\w+)\s*\(([^()]*)\)\s*(?==>|\{)"))
                {
                    var body = Balance.MethodBody(code, m.Index + m.Length);
                    if (string.IsNullOrWhiteSpace(body))
                    {
                        continue;
                    }

                    var name = m.Groups[2].Value;
                    if (!index.TryGetValue(name, out var list))
                    {
                        index[name] = list = [];
                    }

                    list.Add((file, body));
                }
            }

            return index.ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal);
        });
    }

    // =======================================================================================
    // Write evidence.
    // =======================================================================================

    private sealed class WriteEvidence
    {
        public static WriteEvidence Instance => Lazy.Value;

        private static readonly Lazy<WriteEvidence> Lazy = new(Build);

        private readonly HashSet<string> _constructedTypes = new(StringComparer.Ordinal);
        private readonly HashSet<string> _writtenProperties = new(StringComparer.Ordinal);
        private readonly HashSet<string> _writtenClientFields = new(StringComparer.Ordinal);

        private static WriteEvidence Build()
        {
            var e = new WriteEvidence();
            var positionalProperties = RecordPositionalProperties();

            foreach (var (path, code, _) in Sources.All)
            {
                var isSim = path.Replace('\\', '/').Contains("/sim/GameSim/", StringComparison.Ordinal);

                if (!isSim)
                {
                    // Client latches: a non-default assignment anywhere in godot/scripts.
                    foreach (Match m in Regex.Matches(code, @"(?<![\w.])(_\w+)\s*(?:=(?!=)|\+\+|--|\+=)([^;]*)"))
                    {
                        if (!IsDefaultValue(m.Groups[2].Value))
                        {
                            e._writtenClientFields.Add(m.Groups[1].Value);
                        }
                    }

                    continue;
                }

                foreach (Match m in Regex.Matches(code, @"\bnew\s+(\w+)\s*\("))
                {
                    e._constructedTypes.Add(m.Groups[1].Value);

                    var open = m.Index + m.Length - 1;
                    var close = Balance.MatchingBracket(code, open);
                    if (close < 0)
                    {
                        continue;
                    }

                    var args = Balance.SplitArgs(code[(open + 1)..close]);
                    positionalProperties.TryGetValue(m.Groups[1].Value, out var positional);

                    for (var i = 0; i < args.Count; i++)
                    {
                        var named = Regex.Match(args[i], @"^\s*(\w+)\s*:\s*(.+)$", RegexOptions.Singleline);
                        if (named.Success)
                        {
                            if (!IsDefaultValue(named.Groups[2].Value))
                            {
                                e._writtenProperties.Add(named.Groups[1].Value);
                            }
                        }
                        else if (positional is not null && i < positional.Length && !IsDefaultValue(args[i]))
                        {
                            e._writtenProperties.Add(positional[i]);
                        }
                    }
                }

                // `with { P = expr, … }`. The target type is not knowable from text, which is the
                // name-based half the honesty framing calls out.
                //
                // BALANCED, not `[^{}]*`: a first pass used the cheap character class and reported
                // GameState.Drama as dead input, because every single write of it is a NESTED with
                // (`state with { Drama = state.Drama with { Memorials = … } }`) and the outer brace
                // pair never matched. The census caught its own detector being wrong, which is the
                // triage this unit owes rather than an exception to pin.
                foreach (Match m in Regex.Matches(code, @"\bwith\s*\{"))
                {
                    var brace = m.Index + m.Length - 1;
                    var close = Balance.MatchingBracket(code, brace);
                    if (close < 0)
                    {
                        continue;
                    }

                    foreach (var assignment in Balance.SplitArgs(code[(brace + 1)..close]))
                    {
                        var kv = Regex.Match(assignment, @"^\s*(\w+)\s*=\s*(.+)$", RegexOptions.Singleline);
                        if (kv.Success && !IsDefaultValue(kv.Groups[2].Value))
                        {
                            e._writtenProperties.Add(kv.Groups[1].Value);
                        }
                    }
                }
            }

            return e;
        }

        public bool CanMove(Term term) => term.Kind switch
        {
            TermKind.Event => _constructedTypes.Contains(term.Key),
            TermKind.Field => _writtenProperties.Contains(term.Key),
            _ => _writtenClientFields.Contains(term.Key),
        };

        public string WhyNot(Term term) => term.Kind switch
        {
            TermKind.Event =>
                $"no `new {term.Key}(` anywhere in sim/GameSim — the sim never emits this event, so "
                + "EventLog can never hold one.",
            TermKind.Field =>
                $"no write of a non-default value to `{term.Key}` anywhere in sim/GameSim — every "
                + "construction and every `with` leaves it at its birth value.",
            _ =>
                $"no non-default assignment to `{term.Key}` anywhere in godot/scripts — the latch is "
                + "never set.",
        };

        private static readonly string[] DefaultLiterals =
            ["false", "null", "0", "0f", "0d", "0m", "default", "\"\"", "string.Empty", "[]"];

        private static bool IsDefaultValue(string raw)
        {
            var v = raw.Trim().TrimEnd(',', ')', ';').Trim();
            return DefaultLiterals.Contains(v, StringComparer.Ordinal)
                || Regex.IsMatch(v, @"^\w+(<[^<>]*>)?\.Empty$");
        }

        /// <summary>Record type name → its primary-constructor property names in declaration order,
        /// reflected off the live assembly so a POSITIONAL construction argument resolves to the
        /// property it actually fills instead of being invisible.</summary>
        private static Dictionary<string, string[]> RecordPositionalProperties()
        {
            var map = new Dictionary<string, string[]>(StringComparer.Ordinal);
            var types = ContractRecords()
                .Concat(typeof(GameEvent).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(GameEvent))));

            foreach (var type in types)
            {
                var ctor = type.GetConstructors()
                    .OrderByDescending(c => c.GetParameters().Length)
                    .FirstOrDefault();
                if (ctor is null)
                {
                    continue;
                }

                var names = ctor.GetParameters().Select(p => p.Name ?? string.Empty).ToArray();
                if (names.Length > 0 && names.All(n => n.Length > 0 && type.GetProperty(n) is not null))
                {
                    map[type.Name] = names;
                }
            }

            return map;
        }
    }

    /// <summary>Every record type declared in <c>sim/GameSim/Contracts/</c> — the shared state
    /// vocabulary a gate predicate can read. Reflected, never listed.</summary>
    private static IEnumerable<Type> ContractRecords() =>
        typeof(GameState).Assembly.GetTypes()
            .Where(t => t.Namespace == "GameSim.Contracts"
                        && !t.IsEnum
                        && !t.IsInterface
                        && t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Length > 0);

    // =======================================================================================
    // Sources + balanced-text helpers.
    // =======================================================================================

    private static class Sources
    {
        public static List<(string Path, string Code, string Raw)> All => Lazy.Value;

        private static readonly Lazy<List<(string Path, string Code, string Raw)>> Lazy = new(Load);

        private static List<(string Path, string Code, string Raw)> Load()
        {
            var roots = new[]
            {
                Path.Combine(RepoRoot(), "sim", "GameSim"),
                Path.Combine(RepoRoot(), "godot", "scripts"),
            };

            foreach (var root in roots)
            {
                Assert.True(Directory.Exists(root), $"Expected a scanned root at {root}.");
            }

            return roots
                .SelectMany(r => Directory.EnumerateFiles(r, "*.cs", SearchOption.AllDirectories))
                .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                         && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                .OrderBy(p => p, StringComparer.Ordinal)
                .Select(p =>
                {
                    var raw = File.ReadAllText(p);
                    return (p, StripCommentsAndStrings(raw), raw);
                })
                .ToList();
        }

        /// <summary>
        /// Comments AND string/char literals go. Every structural scan in this file runs on the
        /// stripped text, so a TeachNote full of prose can never look like code.
        ///
        /// <para><b>Length-preserving.</b> Each removed span is replaced by the same number of
        /// characters (newlines kept), so an offset into the stripped text is the SAME offset in the
        /// raw text. That is what lets a gate's label read its row's own string id
        /// (<c>SurfaceUnlocks.cs:Gate["Ledger"]</c>) out of the raw source without the structural
        /// scan ever touching un-stripped text — seven tray gates all labelled
        /// <c>Gate[2]</c> is a violation message that cannot say which door is welded shut.</para>
        /// </summary>
        private static string StripCommentsAndStrings(string source)
        {
            source = Blank(source, @"/\*.*?\*/", RegexOptions.Singleline);
            source = Blank(source, @"//[^\n]*", RegexOptions.None);
            source = BlankInterior(source, "\"\"\"(?:.*?)\"\"\"", RegexOptions.Singleline);
            source = BlankInterior(source, @"@""(?:[^""]|"""")*""", RegexOptions.None);
            source = BlankInterior(source, @"\$?""(?:\\.|[^""\\\n])*""", RegexOptions.None);
            source = BlankInterior(source, @"'(?:\\.|[^'\\\n])'", RegexOptions.None);
            return source;
        }

        private static string Blank(string source, string pattern, RegexOptions options) =>
            Regex.Replace(source, pattern, m => Spaces(m.Value), options);

        /// <summary>Blanks a literal's interior but keeps its delimiters, so <c>""</c> still reads as
        /// a string to any scan that cares.</summary>
        private static string BlankInterior(string source, string pattern, RegexOptions options) =>
            Regex.Replace(source, pattern, m => m.Value.Length <= 2
                ? m.Value
                : m.Value[0] + Spaces(m.Value[1..^1]) + m.Value[^1], options);

        private static string Spaces(string span) =>
            new(span.Select(c => c == '\n' ? '\n' : ' ').ToArray());
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

    private static string Flatten(string s, int max)
    {
        var flat = Regex.Replace(s, @"\s+", " ").Trim();
        return flat.Length > max ? flat[..max] + "…" : flat;
    }

    private static class Balance
    {
        public static int MatchingBracket(string s, int open)
        {
            var depth = 0;
            for (var i = open; i < s.Length; i++)
            {
                if (s[i] is '(' or '[' or '{')
                {
                    depth++;
                }
                else if (s[i] is ')' or ']' or '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        /// <summary>Declaration-list split: tracks angle brackets too, because a parameter's own type
        /// can be <c>Func&lt;GameState, bool&gt;</c> and that comma is not a separator.</summary>
        public static List<string> SplitParams(string text) => Split(text, trackAngles: true);

        /// <summary>Call-argument split: does NOT track angle brackets, because a lambda body's
        /// <c>&gt;=</c> would poison the depth. A mis-split yields a fragment matching no lambda
        /// pattern, so the failure mode is a MISSED gate — which the discovery anchors catch — never
        /// an invented one.</summary>
        public static List<string> SplitArgs(string text) => Split(text, trackAngles: false);

        private static List<string> Split(string text, bool trackAngles)
        {
            var parts = new List<string>();
            var depth = 0;
            var angle = 0;
            var start = 0;
            for (var i = 0; i < text.Length; i++)
            {
                switch (text[i])
                {
                    case '(' or '[' or '{':
                        depth++;
                        break;
                    case ')' or ']' or '}':
                        depth--;
                        break;
                    case '<' when trackAngles:
                        angle++;
                        break;
                    case '>' when trackAngles && angle > 0:
                        angle--;
                        break;
                    case ',' when depth == 0 && angle == 0:
                        parts.Add(text[start..i]);
                        start = i + 1;
                        break;
                }
            }

            if (start < text.Length)
            {
                parts.Add(text[start..]);
            }

            return parts.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        }

        /// <summary>The expression starting at <paramref name="start"/>, up to the top-level comma or
        /// closing bracket that ends it.</summary>
        public static string ExpressionAt(string s, int start)
        {
            var depth = 0;
            for (var i = start; i < s.Length; i++)
            {
                if (s[i] is '(' or '[' or '{')
                {
                    depth++;
                }
                else if (s[i] is ')' or ']' or '}')
                {
                    if (depth == 0)
                    {
                        return s[start..i];
                    }

                    depth--;
                }
                else if ((s[i] is ',' or ';') && depth == 0)
                {
                    return s[start..i];
                }
            }

            return s[start..];
        }

        /// <summary>A method body from just past its parameter list: the expression after
        /// <c>=&gt;</c>, or the balanced block after <c>{</c>.</summary>
        public static string MethodBody(string s, int afterParams)
        {
            var i = afterParams;
            while (i < s.Length && char.IsWhiteSpace(s[i]))
            {
                i++;
            }

            if (i + 1 < s.Length && s[i] == '=' && s[i + 1] == '>')
            {
                return ExpressionAt(s, i + 2);
            }

            if (i < s.Length && s[i] == '{')
            {
                var close = MatchingBracket(s, i);
                return close < 0 ? string.Empty : s[(i + 1)..close];
            }

            return string.Empty;
        }
    }
}
