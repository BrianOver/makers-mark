using System.Reflection;
using System.Text.RegularExpressions;
using GameSim.Contracts;
using Xunit.Abstractions;

namespace GameSim.Tests.Hygiene;

/// <summary>
/// P2-HONEST-12: the balance corpus states its own coverage.
///
/// <para><b>Why this exists.</b> Every <c>[BAL]</c> ceremony in this program's plan certifies
/// against one corpus (<c>sim/GameSim.Tests/Balance/</c>), and nobody has ever audited what that
/// corpus actually exercises. The rules census already says the load-bearing half of this in its
/// own voice: <c>BaselinePlayer</c> "never submits: <c>PostBountyAction</c>, any counter action,
/// <c>BuyMaterialAction</c>, <c>SendSupplyAction</c>, <c>RecallPartyAction</c>,
/// <c>SetProfessionsAction</c>, <c>SetPriceAction</c>, <c>UnstockAction</c>,
/// <c>DeclineCommissionAction</c>, <c>HonorMemorialAction</c>, <c>ReforgeHeirloomAction</c>,
/// <c>MasterworkAttemptAction</c>, <c>BuyForgeSupplyAction</c>, <c>CommissionLegendaryWorkAction</c>,
/// <c>ConcludeApprenticeshipAction</c>. Any plan claiming baseline coverage of bounties, the
/// counter, the vigil, or the Morning vendor is wrong" (<c>docs/reference/rules-census.md:1236-1241</c>).
/// One repair generation already happened for this (#551 — "the hundred-day gate was certifying a
/// smith who never really sold anything"), and the rest of that repair's plan doc was deleted as a
/// dead generation in #653 — so the coverage claim currently lives only in a paragraph nothing
/// executes. This test makes it executable: it reflects the live <see cref="PlayerAction"/>
/// hierarchy against what the corpus's sweep policies actually construct, denies by default, and
/// names the unclassified member.
///
/// <para><b>The corpus is wider than one policy.</b> <c>BaselinePlayer</c> is the dominant policy,
/// but it is not the only one three Balance-tagged tests actually drive:
/// <c>MasterworkDominanceBalanceTests</c> drives <c>MasterworkSeekingPlayer</c> (submits
/// <c>BuyForgeSupplyAction</c> and <c>MasterworkAttemptAction</c>, which <c>BaselinePlayer</c>
/// never does); <c>CampProvisioningBalanceTests</c> layers a hand-written send policy on top of
/// <c>BaselinePlayer</c> that submits <c>SendSupplyAction</c>; <c>PhaseDSinksBalanceTests</c> drives
/// a dedicated scripted script (no policy function at all) that submits
/// <c>CommissionLegendaryWorkAction</c> unconditionally, until its cap bites. So the corpus's real
/// "never submitted" set is narrower than <c>BaselinePlayer</c>'s alone — four of the fourteen types
/// the census quotes above ARE actually exercised somewhere in the corpus once every Balance-tagged
/// test is counted, not just the one everybody reads. <see cref="KnownNeverSubmitted"/> pins the
/// corrected, corpus-wide remainder (fifteen types — the fourteen named entries above expand to
/// nineteen concrete <see cref="PlayerAction"/> types once "any counter action" unpacks to five,
/// minus the four that graduate once the whole corpus is counted).</para>
///
/// <para><b>Offered is not submitted (the refinement that decides whether this test proves
/// anything).</b> <c>Balance/VerbConsequenceFloorTests</c> already probes every option
/// <see cref="Advisor.ActionLegality.LegalActions"/> hands back at each decision point — but it
/// advances the real timeline with EMPTY action lists between probes, so its trajectory is a
/// do-nothing campaign. A verb can be OFFERED at every decision point in a run where the state that
/// verb needs (an open counter, an accepted bounty, a runner already sent) never arises, because
/// nothing in that trajectory ever chose it. Option coverage at a decision point is not trajectory
/// coverage: this census reports the two separately.
/// <list type="bullet">
/// <item><b>Submitted</b> — a sweep policy actually constructs this type, along a real,
/// decision-branching trajectory a Balance-tagged test measures. Strongest evidence: proven
/// reached, not merely legal.</item>
/// <item><b>Offered-only</b> — <c>ActionLegality.LegalActions</c> hands this type back as a legal
/// candidate somewhere (so <c>VerbConsequenceFloorTests</c> CAN probe it for inertness), but no
/// sweep policy in the corpus ever actually submits it. Option coverage without trajectory
/// coverage — a plan citing "the floor test already covers this verb" is answering a different
/// question than "the balance bands reflect what happens when a hero uses it."</item>
/// <item><b>Neither</b> — offered by nothing and submitted by nothing: invisible to the ENTIRE
/// corpus, including the floor test. Measured on the live tree today, this bucket is empty: the one
/// candidate that looked headed there, <c>CommissionLegendaryWorkAction</c>, turns out to be
/// submitted (unconditionally, by <c>PhaseDSinksBalanceTests</c>) even though
/// <c>ActionLegality.LegalActions</c> never hands out a candidate for it (a separate, genuine gap in
/// that enumerator worth a maintainer's attention, but not this unit's file to edit —
/// <c>sim/GameSim/Advisor/</c> is out of scope here).</item>
/// </list></para>
///
/// <para><b>Honesty framing (same disclaimer as the client-authority and gear-worn-check
/// censuses).</b> Not a parser — a structural regex heuristic over source text: "submitted" means
/// "some file in the corpus contains <c>new XAction(</c> outside a comment", not "a real 100-day run
/// definitely executes this line" (though every entry currently counted this way is separately
/// documented as measured/exercised in its own file's doc comments — see the class docs on
/// <c>BaselinePlayer</c>, <c>MasterworkSeekingPlayer</c>, <c>CampProvisioningBalanceTests</c>, and
/// <c>PhaseDSinksBalanceTests</c>). "Offered" means "the same construction shape appears inside
/// <c>ActionLegality.LegalActions</c>'s own body", a static proxy for the dynamic legal-candidate
/// set <c>VerbConsequenceFloorTests</c> actually walks at runtime. It cannot see either shape spelled
/// some other way (a policy that built an action via a helper method rather than a bare
/// <c>new X(</c> call would misreport as absent), and it does not know whether a constructed
/// candidate is ever accepted once submitted — only <c>ActionLegality</c>/the kernel's own guards
/// know that. A conservative floor policy is a legitimate gate; the defect this unit closes is not
/// that the corpus is narrow, it is that the narrowness was undocumented and load-bearing.</para>
/// </summary>
public class BalanceCorpusCoverageCensusTests
{
    private readonly ITestOutputHelper _output;

    public BalanceCorpusCoverageCensusTests(ITestOutputHelper output) => _output = output;

    // ---- 1. the live PlayerAction hierarchy, reflected, never hand-listed ----
    private static readonly Type[] AllActionTypes = typeof(PlayerAction).Assembly.GetTypes()
        .Where(t => t.IsSealed && !t.IsAbstract && typeof(PlayerAction).IsAssignableFrom(t) && t != typeof(PlayerAction))
        .OrderBy(t => t.Name, StringComparer.Ordinal)
        .ToArray();

    private static readonly HashSet<string> KnownActionNames =
        AllActionTypes.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);

    /// <summary>Matches a concrete action's construction — <c>new CraftAction(</c>,
    /// <c>new PostBountyAction(</c> — the one shape every real submission and every real legal-
    /// candidate construction in this codebase actually uses (verified against every file this
    /// census reads before writing this regex).</summary>
    private static readonly Regex ActionConstruction = new(@"\bnew (\w+Action)\(", RegexOptions.Compiled);

    /// <summary>Matches a Harness/ policy delegation — <c>BaselinePlayer.ActionsFor(</c>,
    /// <c>MasterworkSeekingPlayer.ActionsFor(</c> — so which policies are "in the corpus" is
    /// discovered live from what the Balance-tagged tests actually call, never hand-listed.</summary>
    private static readonly Regex PolicyDelegation = new(@"\b(\w+Player)\.ActionsFor\(", RegexOptions.Compiled);

    /// <summary>(action type name) -> reason citing the ruling that documents it, same citation
    /// contract as <c>GearWornCheckCensusTests.Exceptions</c> / <c>StaleCommentCensusTests</c>. Every
    /// entry here is a type reachable by no sweep policy in the corpus TODAY — pinned so a future
    /// policy that starts (or stops) covering one of these is a reviewed diff, never a silent
    /// widen or a silent regression.</summary>
    private static readonly Dictionary<string, string> KnownNeverSubmitted = new()
    {
        ["PostBountyAction"] = "Bounty posting is never taken by any corpus sweep policy — "
            + "rules-census.md:1236-1241. P2-HONEST-12.",
        ["OpenCounterAction"] = "Counter-session verbs are exercised only by CounterPlayer, which no "
            + "Balance-tagged test ever drives — rules-census.md:1236-1241. P2-HONEST-12.",
        ["PresentItemAction"] = "Counter-session verbs are exercised only by CounterPlayer, which no "
            + "Balance-tagged test ever drives — rules-census.md:1236-1241. P2-HONEST-12.",
        ["SuggestItemAction"] = "Counter-session verbs are exercised only by CounterPlayer, which no "
            + "Balance-tagged test ever drives — rules-census.md:1236-1241. P2-HONEST-12.",
        ["HaggleResponseAction"] = "Counter-session verbs are exercised only by CounterPlayer, which "
            + "no Balance-tagged test ever drives — rules-census.md:1236-1241. P2-HONEST-12.",
        ["CloseCounterAction"] = "Counter-session verbs are exercised only by CounterPlayer, which no "
            + "Balance-tagged test ever drives — rules-census.md:1236-1241. P2-HONEST-12.",
        ["BuyMaterialAction"] = "The standing Morning vendor floor is never bought from by any corpus "
            + "sweep policy — rules-census.md:1236-1241. P2-HONEST-12.",
        ["SetProfessionsAction"] = "Profession selection happens once, out of band, before any "
            + "scripted policy runs a tick; no sweep policy re-submits it — "
            + "rules-census.md:1236-1241. P2-HONEST-12.",
        ["RecallPartyAction"] = "No corpus sweep policy ever recalls a party early — the vigil-window "
            + "corpus only ever holds or sends supply — rules-census.md:1236-1241. P2-HONEST-12.",
        ["SetPriceAction"] = "No corpus sweep policy re-prices a shelved item after stocking it once "
            + "— rules-census.md:1236-1241. P2-HONEST-12.",
        ["UnstockAction"] = "No corpus sweep policy ever pulls a shelved item back off sale — "
            + "rules-census.md:1236-1241. P2-HONEST-12.",
        ["DeclineCommissionAction"] = "BaselinePlayer accepts every eligible gear commission and no "
            + "sweep policy ever declines one — rules-census.md:1236-1241. P2-HONEST-12.",
        ["HonorMemorialAction"] = "No corpus sweep policy performs the farewell rite — "
            + "rules-census.md:1236-1241. P2-HONEST-12.",
        ["ReforgeHeirloomAction"] = "No corpus sweep policy reforges a fallen hero's worn gear — "
            + "rules-census.md:1236-1241. P2-HONEST-12.",
        ["ConcludeApprenticeshipAction"] = "No corpus sweep policy ever walks out of the "
            + "apprenticeship warrant early — rules-census.md:1236-1241. P2-HONEST-12.",
    };

    private const int ExpectedNeverSubmittedCount = 15;

    [Fact]
    public void PlayerActionHierarchyHasTheMemberCountThisCensusExpects()
        => Assert.True(AllActionTypes.Length == 25,
            $"PlayerAction now reflects {AllActionTypes.Length} concrete derived types — "
            + "sanity-check this census's reflection query still finds them all before trusting the "
            + "coverage split below (and if a type was really added or removed, the offered/"
            + "submitted analysis in this class's doc comment and KnownNeverSubmitted are meant to "
            + "move on purpose).");

    [Fact]
    public void NeverSubmittedCount_IsPinned_SoEveryChangeIsAVisibleDiff()
        => Assert.True(KnownNeverSubmitted.Count == ExpectedNeverSubmittedCount,
            $"Pinned at {ExpectedNeverSubmittedCount}; the table now holds {KnownNeverSubmitted.Count}.");

    [Fact]
    public void EveryPinnedGap_CitesTheRulingThatDocumentedIt()
    {
        var citation = new Regex(@"§11\.7|\bP\d+\b");
        var uncited = KnownNeverSubmitted
            .Where(e => !citation.IsMatch(e.Value))
            .Select(e => e.Key)
            .ToList();

        Assert.True(uncited.Count == 0,
            "A pinned gap with no ruling behind it is drift wearing a reason:\n  "
            + string.Join("\n  ", uncited));
    }

    [Fact]
    public void TheBalanceCorpusCoverageCensus_SeparatesOfferedFromActuallySubmitted()
    {
        var allNames = AllActionTypes.Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
        var offered = OfferedByActionLegality();
        var (policies, submitted) = SubmittedByTheCorpus();

        var split = Classify(allNames, offered, submitted, KnownNeverSubmitted);
        var offeredOnly = split.OfferedOnly;
        var neither = split.Neither;

        _output.WriteLine($"PlayerAction hierarchy: {allNames.Count} concrete types.");
        _output.WriteLine("Sweep policies discovered live from Balance-tagged tests ("
            + $"{policies.Count}): {string.Join(", ", policies)}");
        _output.WriteLine($"SUBMITTED along a real trajectory ({submitted.Count}): "
            + string.Join(", ", submitted));
        _output.WriteLine("OFFERED-ONLY — a legal candidate somewhere in ActionLegality.LegalActions, "
            + $"but no sweep policy ever submits it ({offeredOnly.Count}): "
            + (offeredOnly.Count == 0 ? "(none)" : string.Join(", ", offeredOnly)));
        _output.WriteLine("NEITHER offered nor submitted — invisible to the whole corpus, including "
            + $"VerbConsequenceFloorTests ({neither.Count}): "
            + (neither.Count == 0 ? "(none)" : string.Join(", ", neither)));

        // Denominator first — the green-54 lesson: a census that discovered no policies proves
        // nothing about coverage, it only proves the discovery regex broke.
        Assert.True(policies.Count > 0,
            "No `XPlayer.ActionsFor(` call was found anywhere under sim/GameSim.Tests/Balance/ — "
            + "either the corpus stopped using scripted policies or this census's discovery regex "
            + "no longer matches the real call shape.");

        var uncited = split.Uncited;
        Assert.True(uncited.Count == 0,
            "An action type is reachable by no sweep policy in the corpus and has no pinned, cited "
            + "reason in KnownNeverSubmitted — this is either a real regression (a policy that used "
            + "to submit it stopped) or a genuinely new gap; either way it needs a reviewed entry, "
            + "never a silent widen of what the balance corpus is allowed not to know about:\n  "
            + string.Join("\n  ", uncited));

        var stale = split.Stale;
        Assert.True(stale.Count == 0,
            "KnownNeverSubmitted still pins a type the corpus actually submits now — delete the "
            + "stale entry instead of leaving a closed gap on record (CLAUDE.md rule 8):\n  "
            + string.Join("\n  ", stale));
    }

    /// <summary>The regression proof: a hand-written snippet shaped exactly like a real submission,
    /// so this detector's own correctness never depends on any particular gap still existing in the
    /// live tree.</summary>
    [Fact]
    public void RegressionProof_WouldDetectANewlyMintedActionType()
    {
        const string snippet = "actions.Add(new PostBountyAction(3, 10));";
        Assert.Contains("PostBountyAction", ExtractActionTypeNames(snippet));
    }

    /// <summary>The planted-policy proof for the SHRINK direction, and the reason
    /// <see cref="Classify"/> is a pure function rather than six lines inlined in the census above.
    /// The detector proof one method up shows that a construction is SEEN; it says nothing about
    /// whether the census's own classification then reports the loss BY NAME, which is the whole
    /// failure this unit is supposed to produce. Here a planted policy whose repertoire shrank to
    /// two verbs runs through the real extractor, the real offered set, and the real pinned ledger:
    /// every verb the live corpus submits beyond those two must come back named in
    /// <c>Uncited</c> — the exact list the census interpolates into its failure message.</summary>
    [Fact]
    public void PlantedPolicyThatStoppedSubmittingACoveredVerb_IsReportedByName()
    {
        const string plantedPolicy = """
            actions.Add(new CraftAction(recipe.RecipeId, recipe.MaterialKey));
            actions.Add(new StockAction(item.Id, price));
            """;

        var (_, liveSubmitted) = SubmittedByTheCorpus();
        Assert.Contains("BuyOreAction", liveSubmitted);

        var split = Classify(
            AllActionTypes.Select(t => t.Name).ToList(),
            OfferedByActionLegality(),
            ExtractActionTypeNames(plantedPolicy),
            KnownNeverSubmitted);

        // BuyOreAction is submitted by the live corpus and pinned by nothing, so a policy that
        // stopped submitting it is a regression the census must name, never merely count.
        Assert.Contains("BuyOreAction", split.Uncited);

        // ...and the two verbs the planted policy kept must NOT be named, or the proof would pass
        // for a classifier that simply reports everything.
        Assert.DoesNotContain("CraftAction", split.Uncited);
        Assert.DoesNotContain("StockAction", split.Uncited);
    }

    /// <summary>The planted-policy proof for the WIDEN direction. A pinned gap that quietly closes
    /// is the other half of the same lie: the ledger would keep asserting the corpus cannot see a
    /// verb it now exercises, which is a rule-8 stale claim living in a compiled file. A planted
    /// policy that starts submitting a pinned type must come back named in <c>Stale</c>.</summary>
    [Fact]
    public void PlantedPolicyThatClosedAPinnedGap_IsReportedByName()
    {
        const string plantedPolicy = "actions.Add(new PostBountyAction(3, 10));";
        Assert.Contains("PostBountyAction", KnownNeverSubmitted.Keys);

        var split = Classify(
            AllActionTypes.Select(t => t.Name).ToList(),
            OfferedByActionLegality(),
            ExtractActionTypeNames(plantedPolicy),
            KnownNeverSubmitted);

        Assert.Contains("PostBountyAction", split.Stale);
    }

    /// <summary>The classification, as a pure function over the three measured sets, so the two
    /// planted proofs above can drive the census's real decision logic without writing a fake
    /// policy into the live tree to scan. Kept deliberately total: every concrete action name lands
    /// in exactly one of submitted / offered-only / neither, and independently is either pinned or
    /// <c>Uncited</c>, with <c>Stale</c> catching a pin the corpus has outgrown.</summary>
    private static (List<string> OfferedOnly, List<string> Neither, List<string> Uncited, List<string> Stale)
        Classify(
            IReadOnlyCollection<string> allNames,
            ISet<string> offered,
            ISet<string> submitted,
            IReadOnlyDictionary<string, string> pinned)
    {
        var neverSubmitted = allNames.Where(n => !submitted.Contains(n)).ToList();

        return (
            OfferedOnly: neverSubmitted.Where(offered.Contains).ToList(),
            Neither: neverSubmitted.Where(n => !offered.Contains(n)).ToList(),
            Uncited: neverSubmitted.Where(n => !pinned.ContainsKey(n)).ToList(),
            Stale: pinned.Keys.Where(n => !neverSubmitted.Contains(n)).ToList());
    }

    /// <summary>The negative-control proof: a comment merely NAMING a construction shape (exactly
    /// the sentence this class's own doc comment and rules-census.md both write) must never be
    /// counted as a real submission — comment-stripping is what keeps prose about a gap from
    /// silently closing it.</summary>
    [Fact]
    public void NegativeControl_ACommentMentioningAnActionConstructorIsNeverCounted()
    {
        const string snippet =
            "// BaselinePlayer never calls new PostBountyAction(3, 10) -- see rules-census.md";
        Assert.DoesNotContain("PostBountyAction", ExtractActionTypeNames(StripComments(snippet)));
    }

    /// <summary>Every type <c>ActionLegality.LegalActions</c> ever constructs a candidate for — a
    /// static proxy for the dynamic legal-candidate set <c>VerbConsequenceFloorTests</c> walks at
    /// runtime. <c>IsLegal</c> (the switch expression immediately above <c>LegalActions</c> in the
    /// same file) matches against action instances the CALLER already built and constructs nothing
    /// itself, so every <c>new XAction(</c> in the file lives inside <c>LegalActions</c>'s own
    /// candidate construction — stopping the scan at the first private guard method (where
    /// <c>LegalActions</c> ends and its guard helpers begin) is a safe boundary without needing to
    /// balance braces by hand.</summary>
    private static HashSet<string> OfferedByActionLegality()
    {
        var path = Path.Combine(RepoRoot(), "sim", "GameSim", "Advisor", "ActionLegality.cs");
        var code = StripComments(File.ReadAllText(path));

        var guardsStart = code.IndexOf("private static bool", StringComparison.Ordinal);
        var legalActionsBody = guardsStart > 0 ? code[..guardsStart] : code;

        return ExtractActionTypeNames(legalActionsBody);
    }

    /// <summary>Every type actually constructed somewhere in the corpus: discovers which
    /// Harness/ policies the Balance-tagged tests call live (never hand-listed), unions each such
    /// policy's own constructions with whatever the Balance test files construct directly (the
    /// CampProvisioning/PhaseDSinks/Salve shape — a scripted scenario layering extra actions on top
    /// of, or instead of, a shared policy function).</summary>
    private static (SortedSet<string> Policies, SortedSet<string> Submitted) SubmittedByTheCorpus()
    {
        var balanceDir = Path.Combine(RepoRoot(), "sim", "GameSim.Tests", "Balance");
        var policies = new SortedSet<string>(StringComparer.Ordinal);
        var submitted = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(balanceDir, "*.cs", SearchOption.TopDirectoryOnly))
        {
            var code = StripComments(File.ReadAllText(file));
            foreach (Match m in PolicyDelegation.Matches(code))
            {
                policies.Add(m.Groups[1].Value);
            }

            submitted.UnionWith(ExtractActionTypeNames(code));
        }

        var harnessDir = Path.Combine(RepoRoot(), "sim", "GameSim", "Harness");
        foreach (var policy in policies)
        {
            var path = Path.Combine(harnessDir, policy + ".cs");
            Assert.True(File.Exists(path),
                $"{policy}.ActionsFor is called from a Balance-tagged test, but {path} does not "
                + "exist — the policy was renamed or moved and this census's live discovery needs "
                + "to follow it.");

            submitted.UnionWith(ExtractActionTypeNames(StripComments(File.ReadAllText(path))));
        }

        return (policies, submitted);
    }

    private static HashSet<string> ExtractActionTypeNames(string code)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in ActionConstruction.Matches(code))
        {
            var name = m.Groups[1].Value;
            if (KnownActionNames.Contains(name))
            {
                found.Add(name);
            }
        }

        return found;
    }

    private static string StripComments(string source)
    {
        source = Regex.Replace(source, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        source = Regex.Replace(source, @"//[^\n]*", " ");
        return source;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Game.sln")))
            dir = dir.Parent;

        Assert.True(dir is not null, "Could not find Game.sln walking up from the test assembly.");
        return dir!.FullName;
    }
}
