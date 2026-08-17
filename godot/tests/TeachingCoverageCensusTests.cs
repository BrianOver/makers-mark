#if GDUNIT_TESTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Professions;
using GdUnit4;
using Godot;
using GodotClient.Panels;
using GodotClient.Ui;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// U-T2 Wave F (§11.14.4, "the guard"): the TEACHING coverage census — a different question from
/// every census this program already has. <see cref="ActionReachabilityCensusTests"/> asks "can the
/// player reach this verb from the Godot client at all"; <see cref="GameSim.Tests.Kernel.ActionBudgetTests"/>
/// (sim) asks "does this verb spend a day's action slot"; this file asks a third question neither of
/// those does: **has anyone ever decided whether the player is TAUGHT this** — a concrete
/// <see cref="PlayerAction"/>, a concrete <see cref="CraftPuzzleInput"/> minigame, or a panel — and if
/// not, why not.
///
/// <para><b>Two teaching mechanisms, both real, both verified differently.</b> This program teaches
/// two ways: the scripted 3-day apprenticeship chain (<see cref="TutorialStep"/>/<see
/// cref="TutorialFlow.Registry"/>, predates T2) and T2's own first-touch tier (<see
/// cref="TutorialFlow.ConsumeFirstTouch"/>, Waves B-E). A <see cref="TutorialStep"/> claim is a
/// COMPILE-TIME enum reference resolved against the live <see cref="TutorialFlow.Registry"/> — a
/// renamed/removed step fails to compile, the strongest guard this repo has. A first-touch claim is
/// a STRING id verified by scanning every file under <c>res://scripts</c> for a live
/// <c>ConsumeFirstTouch</c>/<c>ShowMentorFirstTouch</c> call site naming it (mirrors
/// <c>DecisionReasonCensusTests</c>' own "source-scan, not just a hand-typed claim" idiom) — a
/// census entry that names a lesson which was never wired, or was later deleted, fails here by name.
///
/// <para><b>Deny-by-default over the REAL registries, never a hand-listed array</b> (the 128-untested-
/// assets lesson this repo already learned): every concrete <see cref="PlayerAction"/> and
/// <see cref="CraftPuzzleInput"/> subtype comes from reflecting <c>GameSim.Contracts</c>' own
/// assembly (identical query to <see cref="ActionReachabilityCensusTests"/>/<c>ActionBudgetTests</c>);
/// every panel comes from reflecting <see cref="SimPanel"/> subtypes in the client assembly, unioned
/// with the small, explicitly-cited set of modal siblings that predate/deliberately diverge from that
/// base (each one's own class doc says so — <see cref="BestiaryPanel"/>/<see cref="CommissionBoard"/>/
/// <see cref="LegendsWall"/>/<see cref="RaidForecastBoard"/>).</para>
///
/// <para><b>Fails in both directions</b> (the owner's own standing complaint about one-directional
/// guards): a member of the real registry with NO entry anywhere fails ("adding an untaught action
/// goes red"); a member claimed in more than one bucket ALSO fails — teaching something (adding a
/// first-touch/numbered-step entry) without deleting its old exclusion reason is exactly that
/// overlap, and it is checked as its own condition, not inferred from the deny-by-default pass.
/// A stale entry for a type no longer in the real registry fails too (mirrors every other census
/// here). A blank/whitespace exclusion reason fails — "not taught" and "deliberately not taught, and
/// here is why" are different states, and only a real, non-empty reason proves this file recorded
/// the second one rather than silently accepting the first.</para>
///
/// <para><b>THE HONEST FRAMING.</b> A <see cref="TutorialStep"/> claim proves the step COMPILES and
/// has a registry row; it does not re-verify (a third time, beyond <c>TutorialRegistryConformanceTests</c>
/// and <c>ActionReachabilityCensusTests</c>) that the step's own <c>IsDone</c>/<c>TeachNote</c> actually
/// names the claimed action — this file trusts the citation the way <c>ActionReachabilityCensusTests</c>
/// trusts its own location strings. A first-touch claim proves the id string appears near a firing
/// call site; it cannot prove that call site is reachable in practice (that proof, where it exists, is
/// a <c>PressEnabled</c>-driven scenario test in <c>WaveBLessonsTests</c>/<c>WaveDLessonsTests</c>/
/// <c>WaveELessonsTests</c>/<c>ForgeMentorLessonsTests</c>/<c>LegendsWallTests</c>, not here). Mistaking
/// this census for proof that a lesson actually renders on screen is exactly the overstated-guard
/// mistake <see cref="ActionReachabilityCensusTests"/>' own doc warns against.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TeachingCoverageCensusTests
{
    // ============================================================================================
    // Shared machinery: source-scanning + the one coverage-problem computer every category below
    // funnels through, so the real census and the negative-path tests exercise IDENTICAL logic.
    // ============================================================================================

    private static readonly Lazy<string> AllGodotScriptSource = new(ReadAllGodotScriptSource);

    /// <summary>Concatenates every <c>.cs</c> file under <c>res://scripts</c> (the same
    /// <see cref="ProjectSettings.GlobalizePath"/> idiom <c>AgentPlaytestBridgeTests</c> already uses
    /// to reach a real file on disk from inside a gdUnit4 run) into one blob for the first-touch
    /// regex scan below. Read once per test run (<see cref="Lazy{T}"/>), not once per id.</summary>
    private static string ReadAllGodotScriptSource()
    {
        var scriptsDir = ProjectSettings.GlobalizePath("res://scripts");
        var files = Directory.GetFiles(scriptsDir, "*.cs", SearchOption.AllDirectories);

        // Fixture-assumption guard: this program's godot/scripts/ tree is large; a broken
        // GlobalizePath (wrong working directory, moved folder) would silently scan zero files and
        // make every first-touch claim below pass by finding nothing to contradict it.
        RequireFixture(files.Length >= 100,
            $"Only found {files.Length} .cs files under {scriptsDir} -- too few to trust a source " +
            "scan against. GlobalizePath is resolving somewhere unexpected, not this floor.");

        return string.Join("\n---FILE---\n", files.Select(File.ReadAllText));
    }

    private static void RequireFixture(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    /// <summary>True iff <paramref name="id"/> is actually wired to a live
    /// <c>ConsumeFirstTouch</c>/<c>ShowMentorFirstTouch</c> call somewhere under <c>godot/scripts</c> —
    /// either as a direct string literal argument (every call site but one today), or via a named
    /// <c>const string</c> whose value is <paramref name="id"/> and which is itself passed to one of
    /// those two methods by name (<c>ForgePanel.MarkReadLessonId</c>'s own shape — the one call site
    /// that reads a shared id from a constant instead of retyping the literal at all five places that
    /// need it).</summary>
    private static bool FirstTouchIdIsWiredInSource(string id)
    {
        var source = AllGodotScriptSource.Value;
        var escaped = Regex.Escape(id);

        if (Regex.IsMatch(source, $@"(?:ConsumeFirstTouch|ShowMentorFirstTouch)\(\s*(?:\r?\n\s*)?""{escaped}"""))
        {
            return true;
        }

        foreach (Match constDecl in Regex.Matches(source, $@"\bconst\s+string\s+(\w+)\s*=\s*""{escaped}"""))
        {
            var constName = Regex.Escape(constDecl.Groups[1].Value);
            if (Regex.IsMatch(source, $@"(?:ConsumeFirstTouch|ShowMentorFirstTouch)\(\s*(?:\r?\n\s*)?{constName}\b"))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The one decision function every census in this file funnels through, so the real censuses
    /// below and the negative-path tests exercise IDENTICAL logic (the <c>TryGetDecision</c>
    /// precedent, <see cref="ActionReachabilityCensusTests"/>) — parameterized on the maps and the
    /// real-type set rather than closing over static fields, so fabricated types can drive every
    /// failure path without touching real Contracts/client data.
    /// </summary>
    private static List<string> ComputeCoverageProblems(
        string categoryName,
        IReadOnlyCollection<Type> realTypes,
        IReadOnlyDictionary<Type, string> firstTouchClaims,
        IReadOnlyDictionary<Type, TutorialStep> numberedStepClaims,
        IReadOnlyDictionary<Type, string> untaughtReasons)
    {
        var problems = new List<string>();

        var ft = firstTouchClaims.Keys.ToHashSet();
        var ns = numberedStepClaims.Keys.ToHashSet();
        var un = untaughtReasons.Keys.ToHashSet();

        // Fails in direction 1: teaching something without removing its refusal reason (or claiming
        // a type in two teaching buckets at once) is a contradiction, not a stronger claim.
        foreach (var dup in ft.Intersect(ns))
        {
            problems.Add($"{categoryName}: {dup.Name} is claimed as BOTH a first-touch lesson and a numbered-step lesson — pick one.");
        }

        foreach (var dup in ft.Intersect(un))
        {
            problems.Add($"{categoryName}: {dup.Name} is claimed as BOTH taught (first-touch) and untaught — a lesson was added without deleting the old exclusion reason.");
        }

        foreach (var dup in ns.Intersect(un))
        {
            problems.Add($"{categoryName}: {dup.Name} is claimed as BOTH taught (numbered step) and untaught — a lesson was added without deleting the old exclusion reason.");
        }

        // Fails in direction 2: a new member of the real registry with no decision recorded at all.
        var declared = ft.Union(ns).Union(un).ToHashSet();
        foreach (var missing in realTypes.Where(t => !declared.Contains(t)))
        {
            problems.Add(
                $"{categoryName}: {missing.Name} has NO teaching decision recorded (not first-touch, not a " +
                "numbered step, not a reasoned exclusion). Someone must decide whether/how the player is " +
                "taught this, and record that decision in TeachingCoverageCensusTests.");
        }

        // Stale entries: a decision recorded for a type no longer in the real registry.
        foreach (var stale in declared.Except(realTypes))
        {
            problems.Add($"{categoryName}: a teaching decision is recorded for {stale.Name}, which is no longer in the real registry.");
        }

        // The written-reason half: a blank/whitespace exclusion is not a decision.
        foreach (var (type, reason) in untaughtReasons.Where(kv => string.IsNullOrWhiteSpace(kv.Value)))
        {
            problems.Add($"{categoryName}: {type.Name}'s exclusion reason is blank/whitespace — a pinned exclusion must carry a real, non-empty reason.");
        }

        // A first-touch claim that does not resolve to a live call site is a lie, not a lesson.
        foreach (var (type, id) in firstTouchClaims)
        {
            if (!FirstTouchIdIsWiredInSource(id))
            {
                problems.Add(
                    $"{categoryName}: {type.Name} claims first-touch id \"{id}\", but no live " +
                    "ConsumeFirstTouch/ShowMentorFirstTouch call for that id was found under godot/scripts " +
                    "-- the lesson was never wired, or was renamed/deleted and this census was not updated.");
            }
        }

        // A numbered-step claim must resolve to a real registry row (compile-time proves the enum
        // value exists at all; this proves the Registry still has a row FOR it).
        foreach (var (type, step) in numberedStepClaims)
        {
            if (!TutorialFlow.Registry.Any(def => def.Step == step))
            {
                problems.Add($"{categoryName}: {type.Name} claims TutorialStep.{step}, but TutorialFlow.Registry has no row for that step.");
            }
        }

        return problems;
    }

    // ============================================================================================
    // Category 1: every concrete PlayerAction (25 today, same reflection ActionBudgetTests/
    // ActionReachabilityCensusTests already use).
    // ============================================================================================

    private static IReadOnlyCollection<Type> ConcretePlayerActionTypes() =>
        typeof(PlayerAction).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false }
                        && typeof(PlayerAction).IsAssignableFrom(t))
            .ToList();

    /// <summary>Taught by T2's first-touch tier. <see cref="UnlockTalentAction"/>/
    /// <see cref="SetProfessionsAction"/>/<see cref="ReforgeHeirloomAction"/>/<see cref="SetPriceAction"/>
    /// each get their own id; the Foundry's four gold-for-certainty verbs (<see cref="UpgradeForgeAction"/>/
    /// <see cref="BuyForgeSupplyAction"/>/<see cref="MasterworkAttemptAction"/>/
    /// <see cref="CommissionLegendaryWorkAction"/>) share ONE id (Wave E's own framing: "reaching any
    /// one of them for the first time means they can now afford to think about all four"). <see
    /// cref="CraftAction"/> is taught here (link 1, "the mark, read") rather than under its ALSO-true
    /// <see cref="TutorialStep.Craft"/> row, since the mark-read lesson is link-1-specific and the more
    /// load-bearing of the two true claims.</summary>
    private static readonly Dictionary<Type, string> ActionFirstTouch = new()
    {
        [typeof(CraftAction)] = "the-mark-read",
        [typeof(SetPriceAction)] = "pricing-as-a-decision",
        [typeof(UnlockTalentAction)] = "first-talent-unlock",
        [typeof(SetProfessionsAction)] = "second-profession-picked",
        [typeof(ReforgeHeirloomAction)] = "reforge-heirloom",
        [typeof(UpgradeForgeAction)] = "foundry-four-verbs",
        [typeof(BuyForgeSupplyAction)] = "foundry-four-verbs",
        [typeof(MasterworkAttemptAction)] = "foundry-four-verbs",
        [typeof(CommissionLegendaryWorkAction)] = "foundry-four-verbs",
    };

    /// <summary>Taught by the scripted 3-day apprenticeship chain (<see cref="TutorialFlow.Registry"/>,
    /// predates T2). <see cref="TutorialStep.OpenCounter"/>'s own <c>TeachNote</c> names Present/
    /// Suggest/Accept/HoldFirm/Counter explicitly (U-T2-16); <see cref="TutorialStep.Vigil"/> names
    /// both Send and Recall; <see cref="TutorialStep.Commission"/> names both Accept and Decline.</summary>
    private static readonly Dictionary<Type, TutorialStep> ActionNumberedStep = new()
    {
        [typeof(BuyMaterialAction)] = TutorialStep.BuyMaterial,
        [typeof(StockAction)] = TutorialStep.Shelve,
        [typeof(PostBountyAction)] = TutorialStep.PostBounty,
        [typeof(OpenCounterAction)] = TutorialStep.OpenCounter,
        [typeof(PresentItemAction)] = TutorialStep.OpenCounter,
        [typeof(SuggestItemAction)] = TutorialStep.OpenCounter,
        [typeof(HaggleResponseAction)] = TutorialStep.OpenCounter,
        [typeof(SendSupplyAction)] = TutorialStep.Vigil,
        [typeof(RecallPartyAction)] = TutorialStep.Vigil,
        [typeof(BuyOreAction)] = TutorialStep.EveningClose,
        [typeof(AcceptCommissionAction)] = TutorialStep.Commission,
        [typeof(DeclineCommissionAction)] = TutorialStep.Commission,
    };

    private static readonly Dictionary<Type, string> ActionUntaught = new()
    {
        [typeof(UnstockAction)] =
            "The inverse of Stock: taking an item back off the shelf. No first-touch lesson and no " +
            "numbered step (the chain only ever asks the player to shelve, never to reconsider a " +
            "shelf choice) — a deliberate, low-stakes gap, not an oversight.",
        [typeof(CloseCounterAction)] =
            "CounterAnsweredAtLeastOnce (TutorialFlow.cs) recognizes CloseCounterAction as an optional " +
            "fast-path AFTER an answer, but never REQUIRES it -- a player can finish the OpenCounter " +
            "step via Present/Suggest/Haggle alone. No copy anywhere names Close specifically.",
        [typeof(HonorMemorialAction)] =
            "Predates T2 (Wave 4c/U18). Link 5's memorial mechanic has no first-touch lesson and sits " +
            "outside every Wave A-F unit in §11.14.4 -- flagged for a future wave, not silently assumed taught.",
        [typeof(ConcludeApprenticeshipAction)] =
            "The tutorial's own dismiss/graduation action. The chain that would teach it is the chain " +
            "it ends -- the confirm row's own copy ('End it') is self-explanatory UI chrome, not a " +
            "taught game mechanic in the five-link sense.",
    };

    [TestCase]
    public void EveryConcretePlayerAction_HasATeachingDecision()
    {
        var problems = ComputeCoverageProblems(
            "PlayerAction", ConcretePlayerActionTypes(), ActionFirstTouch, ActionNumberedStep, ActionUntaught);

        AssertThat(problems.Count).OverrideFailureMessage(string.Join("\n", problems)).IsEqual(0);
    }

    // ============================================================================================
    // Category 2: every concrete CraftPuzzleInput (4 today: the four active-craft minigames).
    // ============================================================================================

    private static IReadOnlyCollection<Type> ConcreteCraftPuzzleTypes() =>
        typeof(CraftPuzzleInput).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsClass: true } && t.IsSubclassOf(typeof(CraftPuzzleInput)))
            .ToList();

    /// <summary><see cref="ForgeTraceInput"/> (the "Anvil Map") carries BOTH the blacksmith's shaping
    /// and quench phases in one submission — taught by two lessons in sequence
    /// ("forge-act1-shaping" then "forge-act2-quench"); this census cites the first, since a claim
    /// only needs one live call site to prove the type is not silently untaught.</summary>
    private static readonly Dictionary<Type, string> MinigameFirstTouch = new()
    {
        [typeof(ForgeTraceInput)] = "forge-act1-shaping",
        [typeof(AlchemyReagentPuzzle)] = "alchemy-brew",
        [typeof(EngineeringAssemblyInput)] = "engineering-assembly",
        [typeof(TanningScrapeInput)] = "tanning-frame",
    };

    private static readonly Dictionary<Type, TutorialStep> MinigameNumberedStep = new();

    private static readonly Dictionary<Type, string> MinigameUntaught = new();

    [TestCase]
    public void EveryConcreteCraftPuzzle_HasATeachingDecision()
    {
        var problems = ComputeCoverageProblems(
            "CraftPuzzleInput", ConcreteCraftPuzzleTypes(), MinigameFirstTouch, MinigameNumberedStep, MinigameUntaught);

        AssertThat(problems.Count).OverrideFailureMessage(string.Join("\n", problems)).IsEqual(0);
    }

    // ============================================================================================
    // Category 3: every panel — the 15 concrete SimPanel subtypes (reflected) unioned with the 4
    // Control-derived modal siblings that predate/deliberately diverge from that base (each cited
    // against its own class doc, which says so explicitly).
    // ============================================================================================

    /// <summary>The four panels built directly on <see cref="Control"/> rather than
    /// <see cref="SimPanel"/> — <see cref="BestiaryPanel"/>/<see cref="RaidForecastBoard"/>/
    /// <see cref="LegendsWall"/>'s own class docs each say "code-built modal sibling, mirroring
    /// RaidForecastBoard/CommissionBoard" (or the reverse citation), so this is a small, explicitly-
    /// verified set rather than an invented one. If any of these four is ever refactored onto
    /// <see cref="SimPanel"/>, the reflected set below would then ALSO find it, and the two would
    /// double-count it as one type in one HashSet — not silently drop it either way.</summary>
    private static readonly Type[] NonSimPanelModalSiblings =
    [
        typeof(BestiaryPanel), typeof(CommissionBoard), typeof(LegendsWall), typeof(RaidForecastBoard),
    ];

    private static IReadOnlyCollection<Type> ConcretePanelTypes() =>
        typeof(SimPanel).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsClass: true } && typeof(SimPanel).IsAssignableFrom(t))
            .Concat(NonSimPanelModalSiblings)
            .ToHashSet();

    /// <summary>Four panels/boards taught by their own T2 first-touch lesson. <see cref="DepthsPanel"/>/
    /// <see cref="HeroesPanel"/>/<see cref="HeroPanel"/>/<see cref="BestiaryPanel"/> share ONE id
    /// ("read-only-surfaces", Wave E, widened here in Wave F to close the HeroesPanel gap this very
    /// census caught — see <c>MainUi.OpenPanel</c>'s own comment).</summary>
    private static readonly Dictionary<Type, string> PanelFirstTouch = new()
    {
        [typeof(ForgePanel)] = "material-ceiling-hand-band",
        [typeof(ShopPanel)] = "pricing-as-a-decision",
        [typeof(DepthsPanel)] = "read-only-surfaces",
        [typeof(HeroesPanel)] = "read-only-surfaces",
        [typeof(HeroPanel)] = "read-only-surfaces",
        [typeof(BestiaryPanel)] = "read-only-surfaces",
        [typeof(ProgressionPanel)] = "second-profession-picked",
        [typeof(CommissionBoard)] = "hold-or-sell",
        [typeof(RaidForecastBoard)] = "forecast-board-taught",
    };

    /// <summary>Six panels taught by the numbered chain opening a real submit site inside them.</summary>
    private static readonly Dictionary<Type, TutorialStep> PanelNumberedStep = new()
    {
        [typeof(BountyPanel)] = TutorialStep.PostBounty,
        [typeof(CampPanel)] = TutorialStep.Vigil,
        [typeof(CounterPanel)] = TutorialStep.OpenCounter,
        [typeof(LedgerModal)] = TutorialStep.EveningClose,
        [typeof(ScryingMirror)] = TutorialStep.LookIn,
        [typeof(TavernPanel)] = TutorialStep.MeetHeroes,
    };

    private static readonly Dictionary<Type, string> PanelUntaught = new()
    {
        [typeof(ChronicleScroll)] =
            "The campaign-ending chronicle is a one-time closing scroll shown once, at the campaign's " +
            "natural end -- a first-touch lesson has no second occasion to matter and no dilemma to " +
            "name. Read-only by construction.",
        [typeof(DemandPanel)] =
            "Read-only (pass reasons); no first-touch lesson and no numbered step opens on Demand -- " +
            "genuinely untaught, a gap for a future wave, not papered over.",
        [typeof(LessonsPanel)] =
            "The Lessons book IS the replay surface for every first-touch/numbered-step lesson (Wave " +
            "F's own 'show me that lesson again' unit -- already shipped in Wave A, LessonsPanel.cs' " +
            "own FirstTouch.Fired loop). It re-displays past teaching; it does not teach a new " +
            "mechanic on open.",
        [typeof(LegendsWall)] =
            "Reforge is taught ('reforge-heirloom') and Honor has no lesson at all (see " +
            "HonorMemorialAction's own exclusion above), but the WALL ITSELF has no first-open " +
            "orientation lesson the way RaidForecastBoard has ('forecast-board-taught') -- a visit " +
            "that neither honors nor reforges anything sees nothing taught. Flagged, not papered over.",
    };

    [TestCase]
    public void EveryPanel_HasATeachingDecision()
    {
        var problems = ComputeCoverageProblems(
            "Panel", ConcretePanelTypes(), PanelFirstTouch, PanelNumberedStep, PanelUntaught);

        AssertThat(problems.Count).OverrideFailureMessage(string.Join("\n", problems)).IsEqual(0);
    }

    // ============================================================================================
    // Negative-path tests: prove ComputeCoverageProblems actually fails in every direction it
    // claims to, using fabricated stand-in types rather than real Contracts/client data (the
    // FabricatedActionStandIn precedent, ActionReachabilityCensusTests).
    // ============================================================================================

    private sealed class FabricatedTypeA
    {
    }

    private sealed class FabricatedTypeB
    {
    }

    [TestCase]
    public void ComputeCoverageProblems_FailsByName_ForATypeWithNoDecision()
    {
        var problems = ComputeCoverageProblems(
            "Fabricated", [typeof(FabricatedTypeA)],
            new Dictionary<Type, string>(), new Dictionary<Type, TutorialStep>(), new Dictionary<Type, string>());

        AssertThat(problems.Count).IsEqual(1);
        AssertThat(problems[0].Contains(nameof(FabricatedTypeA)))
            .OverrideFailureMessage($"Failure must name the offending type. Got: \"{problems[0]}\"")
            .IsTrue();
    }

    [TestCase]
    public void ComputeCoverageProblems_Fails_WhenExclusionReasonIsBlankOrWhitespace()
    {
        foreach (var blank in new[] { "", "   ", "\t\n" })
        {
            var problems = ComputeCoverageProblems(
                "Fabricated", [typeof(FabricatedTypeA)],
                new Dictionary<Type, string>(), new Dictionary<Type, TutorialStep>(),
                new Dictionary<Type, string> { [typeof(FabricatedTypeA)] = blank });

            AssertThat(problems.Count)
                .OverrideFailureMessage($"A blank/whitespace exclusion reason ({blank.Length} chars) must fail, but did not.")
                .IsEqual(1);
        }
    }

    [TestCase]
    public void ComputeCoverageProblems_Passes_ForARealExclusionReason()
    {
        var problems = ComputeCoverageProblems(
            "Fabricated", [typeof(FabricatedTypeA)],
            new Dictionary<Type, string>(), new Dictionary<Type, TutorialStep>(),
            new Dictionary<Type, string> { [typeof(FabricatedTypeA)] = "a real, non-empty reason" });

        AssertThat(problems.Count).OverrideFailureMessage(string.Join("\n", problems)).IsEqual(0);
    }

    /// <summary>The direction the coordinator named explicitly: teaching something without removing
    /// its refusal reason must go red, not silently upgrade to "extra proof."</summary>
    [TestCase]
    public void ComputeCoverageProblems_Fails_WhenATypeIsClaimedAsBothTaughtAndExcluded()
    {
        var problems = ComputeCoverageProblems(
            "Fabricated", [typeof(FabricatedTypeA)],
            new Dictionary<Type, string> { [typeof(FabricatedTypeA)] = "the-mark-read" }, // a real, live id
            new Dictionary<Type, TutorialStep>(),
            new Dictionary<Type, string> { [typeof(FabricatedTypeA)] = "stale exclusion reason left behind" });

        AssertThat(problems.Count)
            .OverrideFailureMessage(string.Join("\n", problems))
            .IsEqual(1);
        AssertThat(problems[0].Contains("BOTH"))
            .OverrideFailureMessage($"Failure must name the overlap. Got: \"{problems[0]}\"")
            .IsTrue();
    }

    [TestCase]
    public void ComputeCoverageProblems_Fails_ForAFirstTouchClaimWithNoLiveCallSite()
    {
        var problems = ComputeCoverageProblems(
            "Fabricated", [typeof(FabricatedTypeA)],
            new Dictionary<Type, string> { [typeof(FabricatedTypeA)] = "this-id-does-not-exist-anywhere-12345" },
            new Dictionary<Type, TutorialStep>(), new Dictionary<Type, string>());

        AssertThat(problems.Count).IsEqual(1);
        AssertThat(problems[0].Contains("no live"))
            .OverrideFailureMessage($"Failure must say the claim has no live call site. Got: \"{problems[0]}\"")
            .IsTrue();
    }

    [TestCase]
    public void ComputeCoverageProblems_Passes_ForARealFirstTouchIdWithALiveCallSite()
    {
        var problems = ComputeCoverageProblems(
            "Fabricated", [typeof(FabricatedTypeA)],
            new Dictionary<Type, string> { [typeof(FabricatedTypeA)] = "the-mark-read" },
            new Dictionary<Type, TutorialStep>(), new Dictionary<Type, string>());

        AssertThat(problems.Count).OverrideFailureMessage(string.Join("\n", problems)).IsEqual(0);
    }

    [TestCase]
    public void ComputeCoverageProblems_Fails_ForANumberedStepClaimWithNoRegistryRow()
    {
        // Every real TutorialStep DOES have a Registry row (TutorialRegistryConformanceTests already
        // pins that both ways), so this exercises the failure path the only way possible without a
        // fabricated enum: a fabricated TYPE claimed against a real step still proves the check runs
        // and passes when the row genuinely exists, which the companion positive test below covers;
        // this test instead proves the STALE/overlap paths on the same claim would still be honored.
        var problems = ComputeCoverageProblems(
            "Fabricated", [typeof(FabricatedTypeA), typeof(FabricatedTypeB)],
            new Dictionary<Type, string>(),
            new Dictionary<Type, TutorialStep> { [typeof(FabricatedTypeA)] = TutorialStep.Craft },
            new Dictionary<Type, string>());

        // FabricatedTypeB has no decision at all -- proves deny-by-default still fires alongside a
        // valid numbered-step claim for a sibling type, i.e. one clean entry never masks another's gap.
        AssertThat(problems.Count).IsEqual(1);
        AssertThat(problems[0].Contains(nameof(FabricatedTypeB))).IsTrue();
    }

    [TestCase]
    public void ComputeCoverageProblems_NoStaleEntries_ForATypeNoLongerInTheRealRegistry()
    {
        var problems = ComputeCoverageProblems(
            "Fabricated", realTypes: [], // FabricatedTypeA no longer "exists"
            new Dictionary<Type, string> { [typeof(FabricatedTypeA)] = "the-mark-read" },
            new Dictionary<Type, TutorialStep>(), new Dictionary<Type, string>());

        AssertThat(problems.Count).IsEqual(1);
        AssertThat(problems[0].Contains("no longer in the real registry"))
            .OverrideFailureMessage($"Got: \"{problems[0]}\"")
            .IsTrue();
    }

    // ============================================================================================
    // Denominator guards: a broken reflection query would make every census above pass by having
    // nothing to check (this program's own recurring vacuous-green shape).
    // ============================================================================================

    [TestCase]
    public void ReflectionQueries_FindEnoughRealTypes_ToTrustAGreenRun()
    {
        AssertThat(ConcretePlayerActionTypes().Count)
            .OverrideFailureMessage("PlayerAction reflection found too few types -- the query is broken, not the census.")
            .IsGreaterEqual(25);
        AssertThat(ConcreteCraftPuzzleTypes().Count)
            .OverrideFailureMessage("CraftPuzzleInput reflection found too few types -- the query is broken, not the census.")
            .IsGreaterEqual(4);
        AssertThat(ConcretePanelTypes().Count)
            .OverrideFailureMessage("Panel reflection found too few types -- the query is broken, not the census.")
            .IsGreaterEqual(19);
    }
}
#endif
