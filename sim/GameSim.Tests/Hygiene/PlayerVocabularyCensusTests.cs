using System.Reflection;
using System.Text.RegularExpressions;
using GameSim.Contracts;

namespace GameSim.Tests.Hygiene;

/// <summary>
/// [LAW:show-only-sim-decided] P2-HONEST-06 (§11.15, the jargon rule / P2-R29 / P2-KTD4): the
/// vocabulary census owns GENERATORS, not a banned-word list. "Player copy names only things the
/// player can see" — no enum member, registry id, CLI verb, surface id, plan-unit citation, raw
/// permille, or bare formula may render where a display name exists or should exist.
///
/// <para><b>Why generators, not words.</b> A hand-typed banned-word list is worthless because the
/// thirteenth leak is a word that does not exist yet. Every check below reads a LIVE DECLARATION —
/// reflection over <c>GameSim.Contracts</c>, a registry's own <c>All</c> table, the CLI's own
/// dispatch arms, the surface arbiter's own claim ids — so a new enum member, a new registry, a new
/// verb, or a new surface bans itself the day it is added, without anyone editing this file.</para>
///
/// <para><b>Deviation from a literal "ban every enum member name" reading, disclosed.</b> The plan
/// text says "every member name of every public enum becomes a banned token." Taken completely
/// literally that reads <c>QualityGrade.Fine</c>/<c>Poor</c>/<c>Masterwork</c> and
/// <c>ItemSlot.Weapon</c>/<c>Armor</c> as forbidden WORDS — but those enum member names ARE the
/// player's own vocabulary for grade and slot (there is no separate "Fine" a player is supposed to
/// see instead of "Fine"), and this codebase's real flavor prose uses "iron", "steel", "day",
/// "close" constantly as ordinary English inside authored sentences that have nothing to do with
/// <c>MaterialRegistry.Iron</c> or a CLI verb. A literal word-ban would either flag the entire game's
/// prose or need a banned-word list of exemptions the size of the corpus — the exact failure this
/// unit exists to avoid, just relocated into an exception ledger. So this census generalizes
/// <c>PlayerPhaseVocabularyCensusTests</c>' own already-established idiom instead: an enum leaks when
/// it is INTERPOLATED RAW (<c>{item.Quality}</c>) rather than through a declared display — the same
/// shape <c>PhaseVocab</c> and <c>BeatVocab</c> already exist to close for <see cref="DayPhase"/> and
/// <see cref="BeatType"/>. This unit's own first run found the family had NOT been generalized past
/// those two: <see cref="QualityGrade"/> and <see cref="ItemSlot"/> were raw at a dozen sites, and
/// <see cref="CampaignAct"/> was raw in exactly one tooltip while a sibling chip three inches away
/// rendered the same value through a roman-numeral table — the identical split-brain defect
/// <c>PhaseVocab</c>'s own class doc was written about, just for a different enum. All were fixed in
/// this unit's PR, not pinned (see <c>ItemVocab</c>, and <c>MainUi.BuildCalendarCluster</c>'s
/// tooltip fix).</para>
///
/// <para><b>A disclosed gap in generator 1's reach.</b> The hole regex keys on a reflected
/// PROPERTY name (<c>{item.Quality}</c>), which it can only do because <c>.Quality</c> is a dot
/// away from something reflection can inspect. A BARE enum-typed local or parameter interpolated
/// directly (<c>{slot}</c>, for an <c>ItemSlot slot</c> parameter — the exact pre-fix shape
/// <c>HeroesPanel.cs</c> and <c>TavernPanel.cs</c> both carried) has no such anchor: nothing links
/// the arbitrary name "slot" to its declared type without a real parser, so this generator cannot
/// discover it. This unit's own triage found and fixed both bare-variable sites by manual review,
/// not by this generator, and says so here rather than letting the fix imply coverage the text scan
/// does not actually have.</para>
///
/// <para><b>A second disclosed gap: deferred rendering through a local variable.</b>
/// <c>TavernPanel.BuildThreadRow</c> computed a raw <c>{commission.MinQuality} {commission.Slot}</c>
/// hole inside a <c>switch</c> expression assigned to a local, then rendered that local through
/// <c>AddLabel</c> several statements — and a <c>return</c> — later. No window size this census
/// could use without flooding every OTHER window with unrelated code can bridge that gap; it is the
/// same class of limit as the bare-variable one above, just shaped differently. Found and fixed by
/// manual review, same as the bare variables — three sites total the generators themselves could not
/// have found, disclosed rather than left as an implied 100% the text scan cannot back up.</para>
///
/// <para><b>The seven generators.</b></para>
/// <list type="number">
/// <item><description><b>Enum-typed interpolation holes</b> — reflects every public property across
/// <c>GameSim.Contracts</c>, keeps property NAMES whose type is unambiguously a Contracts enum
/// everywhere that name is declared (excludes e.g. <c>Kind</c> and <c>Magnitude</c>, each also used
/// for a plain <c>string</c>/<c>int</c> elsewhere in Contracts — an ambiguous name is excluded
/// automatically rather than risking a same-named, differently-typed false positive), then denies by
/// default any <c>{x.PropName}</c> hole in player copy.</description></item>
/// <item><description><b>Registry ids with a <c>DisplayName</c> sibling</b> — reflects every static
/// class in the SAME assembly with a public static <c>All</c> collection whose element type carries
/// both <c>Id</c> and <c>DisplayName</c> (so <c>MaterialRegistry</c>, <c>ClassRegistry</c>,
/// <c>FactionRegistry</c>, <c>ProfessionRegistry</c>, <c>VenueRegistry</c>, <c>TraitRegistry</c> are
/// discovered automatically — a registry added later bans its own ids with no edit here), derives the
/// STRING property names that plausibly carry that id (the element type's name stem, stem+"Id",
/// stem+"Key" — <c>MaterialKey</c>, <c>ClassId</c>, <c>FactionId</c>, <c>Profession</c>,
/// <c>VenueId</c> all fall out of this), then denies the same way.</description></item>
/// <item><description><b>CLI dispatch arms</b> — regex-discovers every <c>case "verb":</c> (and
/// <c>"a" or "b"</c> multi-pattern arms) in <c>sim/GameSim.Cli/Program.cs</c>'s own source, so a new
/// verb bans itself, then denies any of those tokens appearing QUOTED (<c>'verb'</c> or
/// <c>`verb`</c>) in client copy — the shape this unit's own first run found live nine times over
/// ("— 'next' to advance", teaching a REPL command a GUI player has no way to type; see below).
/// </description></item>
/// <item><description><b>Surface ids</b> — regex-discovers every id
/// <c>SurfaceArbiter.Claim</c> registers in <c>MainUi.cs</c> (<c>"Ledger"</c>, <c>"Forecast"</c>,
/// ...), denies the same quoted-token way. Zero hits on this run — a clean generator, ready for a
/// future leak.</description></item>
/// <item><description><b>A regex family for minted identifiers</b> — kebab-case ids of at least
/// three segments (two-segment English compounds like "bare-handed" or "near-death" are real prose,
/// not minted ids — a real hit in <c>TavernPanel.cs</c> during this unit's own tuning proved a
/// two-segment threshold is too eager), SCREAMING_SNAKE, PascalCase-run-together, raw permille, and
/// plan-unit citations (<c>P2-[A-Z]+-\d+</c>, <c>§11\.\d</c>, <c>U\d+</c>) — the last two closing the
/// same shape <c>P2-HONEST-14</c> found live in the CLI's own hints (out of THIS unit's scope; see
/// that row).</description></item>
/// </list>
///
/// <para><b>The corpus.</b> <c>godot/scripts/**/*.cs</c> (the client render layer — every generator
/// runs here) plus three sim-side sources of authored player prose the task named by name:
/// <c>sim/GameSim/Flavor/Packs/*.cs</c> (gossip/ledger/faction flavor), <c>ChronicleComposer.cs</c>
/// (the Chronicle Night coda), and <c>GossipGenerator.cs</c>. <c>ActionSubject.cs</c> is walked but
/// never flags: its own class doc says its whole output feeds <c>PlaytestLog.Action</c>'s diagnostic
/// <c>why</c> field, never a player-visible control, and it renders through none of the sinks below
/// — deny-by-default correctly finds nothing to deny there rather than needing a name-based
/// exemption.</para>
///
/// <para><b>What "player copy" means to a text scan (THE HONEST FRAMING, same disclaimer as every
/// sibling in this file).</b> Not a parser. In <c>godot/scripts</c> a hole or token counts as player
/// copy when its statement window mentions a recognized render sink (<c>.Text =</c>,
/// <c>TooltipText =</c>, <c>AddLabel(</c>, <c>AddHeader(</c>, <c>AddChip(</c>, <c>StatChip(</c>,
/// <c>whyNot:</c>/<c>WhyNot:</c>, or a <c>WhyNot</c> reason tuple) or the file is
/// <c>AdventureTicker.cs</c ("the ticker's own composer" — P2-MEMORY-12 — has no such marker because
/// its whole method IS the sink). A hole with none of those nearby (bookkeeping, a <c>Name =</c>
/// node identifier, a diagnostic log, an <c>ActionSubject.cs</c> subject line) is not player copy and
/// is correctly never denied. In the three sim-side files every string IS the corpus (their whole
/// purpose), so no sink marker is required there — except the kebab-case and PascalCase-run-together
/// sub-checks, which are NOT applied to those three files: <c>ChronicleComposer.Declared</c>'s own
/// tuple array carries fifteen kebab-case tie-break ids (<c>"no-work-turned-a-fight"</c>, ...) that
/// are never rendered — only their PROSE sibling is — and are structurally indistinguishable from a
/// real leak by a text scan; the same is true of every Flavor pack's <c>"{BaseKey}/{voice}"</c>
/// dictionary keys. Both sub-checks stay live over <c>godot/scripts</c>, where the real risk (a raw
/// asset/recipe id rendered directly) actually lives; their value in the three sim files is
/// structurally near-zero anyway, since generators 1-2 already prove those files' actual rendered
/// slots (<c>{hero}</c>, <c>{item}</c>, <c>{faction}</c>, <c>{cause}</c>) never touch a raw enum or
/// registry id.</para>
///
/// <para><b>Interaction with <c>CommissionSlotCopyCensusTests</c> (P2-HONEST-11), disclosed.</b> That
/// census discovers a raw <c>{x.Slot}</c> hole textually and requires <c>SlotHonestyNote(</c> nearby.
/// This unit's fix wraps several of the SAME holes in <c>ItemVocab.Display(...)</c>
/// (<c>{ItemVocab.Display(commission.Slot)}</c>), which no longer matches that census's
/// <c>\{[A-Za-z_][\w.]*\.Slot\b</c> pattern (a <c>(</c> now sits where it expects only word
/// characters and dots) — so those specific lines silently drop out of ITS discovered set, though
/// they remain compliant (the <c>SlotHonestyNote(</c> call itself is untouched) and this unit's own
/// enum-hole generator now covers the identical raw-interpolation risk at the same lines. Recorded
/// here rather than silently, per this repo's own culture of disclosed heuristic limits.</para>
///
/// <para><b>P2-HONEST-14: the CLI's own printed prose joins the corpus.</b>
/// <c>sim/GameSim.Cli/Program.cs</c> was in nobody's scope — <c>godot/scripts/**</c> is the client
/// render layer, the three sim-prose files are named by name, and the CLI's own text sat between
/// both. Its player-visible surface is derived the same "live declaration" way as every generator
/// above, not by listing line numbers: <see cref="CliInteractiveSourceFiles"/> is the one file, and
/// <see cref="CliSinkMarker"/> — a literal <c>Console.WriteLine(</c>/<c>Console.Write(</c> — is the
/// sink. That marker structurally EXCLUDES <c>Console.Error.WriteLine(</c> without any special-case
/// code: <c>"Error."</c> sits between <c>Console.</c> and <c>WriteLine(</c>, so the substring
/// <c>"Console.WriteLine("</c> is simply never present there. This is exactly right for the corpus
/// question: every <c>Console.Error.WriteLine(</c> call in this file is a malformed-CLI-INVOCATION
/// diagnostic for the batch/decisions/probe/characterize/seed-search/long-wall/felt-wall/
/// econ-trajectory/arc-stall dev-tool dispatch a few dozen lines above the interactive loop (wrong
/// flag, bad int, etc.) — never a line the blacksmith-player reads inside a live campaign — while
/// every <c>Console.WriteLine(</c>/<c>Console.Write(</c> call IS that player's own text-mode game.
/// Generators 1 (enum holes) and 5 (minted identifiers) run over this corpus; generator 2 is
/// subsumed by generator 1's alternation already. <b>Generator 3 deliberately does NOT run here</b>:
/// it denies a CLI verb or surface id QUOTED in copy because a GUI player has no console to type it
/// into — but Program.cs <i>is</i> that console, so its own help text quoting <c>'help'</c>,
/// <c>'recipes'</c>, <c>'mats'</c> is the literal, honest truth for this surface, and running
/// generator 3 here would flag every one of those as though they were leaks.</para>
///
/// <para><b>A disclosed gap this widening does NOT close: the <c>case "help":</c> block is a
/// triple-quoted raw string</b> (<c>"""..."""</c>), the one player-visible block in this file the
/// <see cref="StringLiteral"/> regex cannot see at all — it knows plain and interpolated
/// (<c>"..."</c>/<c>$"..."</c>) literals only, a limit this class's own doc has disclosed since
/// P2-HONEST-06. That block is known (manual read, this unit) to carry its own internal-jargon
/// citations ("PA2", "U-D1 sink 1/3a/3b/5") the generators cannot reach any more than they can reach
/// <c>PKD4</c>'s shape (next paragraph) — named here rather than silently passed over, and left
/// unfixed: out of P2-HONEST-14's named scope (the two <c>PKD4</c> hints), not swept in.</para>
///
/// <para><b>A refuted claim, checked rather than trusted.</b> P2-HONEST-06's own class doc (this
/// file, generator 5's list above) asserts its <see cref="PlanCitation"/> patterns
/// (<c>P2-[A-Z]+-\d+</c>, <c>§11\.\d+</c>, <c>U\d{1,3}</c>) are "the last two closing the same shape
/// <c>P2-HONEST-14</c> found live in the CLI's own hints." Checked directly against the actual
/// string: <c>PKD4</c> matches NONE of the three — no <c>P2-</c> prefix, no <c>§</c>, and no bare
/// <c>U</c> immediately before its digits (it is <c>P</c>-<c>K</c>-<c>D</c>-<c>4</c>, an unrelated
/// "Playtest Known Defect" citation family live elsewhere in this codebase —
/// <c>CraftQualityHint.cs</c>, <c>SeedSearch.cs</c>, <c>RecruitSystem.cs</c>, <c>GameKernel.cs</c>,
/// the profession files — never this pattern family). The prior claim is refuted, recorded here
/// rather than re-filed, exactly as this plan row's own parenthetical modeled for the "3D forge
/// minigame" claim before it. This unit fixes both <c>PKD4</c> citations by hand regardless (the
/// task's own "at minimum," not contingent on a generator catching them) and does not widen
/// <see cref="PlanCitation"/> to the <c>PKD</c> shape — that is a rule change, and this unit's
/// mandate is the corpus, not the rules.</para>
/// </summary>
public class PlayerVocabularyCensusTests
{
    /// <summary>(source, token) → reason citing the ruling that grants the exception. Empty: every
    /// hit this run's generators found was fixed, not pinned (see the class doc's "Deviation"
    /// paragraph for exactly what was fixed).</summary>
    private static readonly Dictionary<(string Source, string Token), string> Exceptions = new();

    private const int ExpectedExceptionCount = 0;

    private static readonly Assembly ContractsAssembly = typeof(GameState).Assembly;

    private const int BackwardWindow = 260;
    private const int ForwardCap = 500;
    private static readonly Regex StatementTerminator = new(@"[;,]\r?\n");

    /// <summary>Recognized player-text sinks. Word-boundary-guarded (<c>\b</c>) on every call-shaped
    /// marker so a SHORT marker never matches inside a LONGER, unrelated identifier — bare substring
    /// matching let <c>StatChip(</c> fire inside <c>NamedStatChip(</c>'s own name (a real false
    /// positive this unit's own tuning found and fixed).</summary>
    private static readonly Regex SinkMarker = new(
        @"\.Text\s*=|TooltipText\s*=|\bAddLabel\(|\bAddHeader\(|\bAddChip\(|\bStatChip\(|\bListRow\(|\bwhyNot\s*:|\bWhyNot\s*:|\breturn\s*\(false,|\breturn\s*\(true,");

    /// <summary>P2-HONEST-14's CLI player-copy sink: a literal <c>Console.WriteLine(</c> or
    /// <c>Console.Write(</c> call. Deliberately does NOT match <c>Console.Error.WriteLine(</c> — see
    /// the class doc's "P2-HONEST-14" paragraph for why that exclusion falls out of the literal text
    /// rather than needing a separate negative case.</summary>
    private static readonly Regex CliSinkMarker = new(@"\bConsole\.WriteLine\(|\bConsole\.Write\(");

    /// <summary>A statement ENDING in a <c>Name =</c> assignment — a control's own node identifier,
    /// never player copy, even when an earlier sink call sits in the same statement window (the
    /// chained <c>AddHeader(...).Name = "RetellingHeader"</c> shape this unit's own tuning found:
    /// the FIRST string there is real header text, the SECOND is a node id chained onto the same
    /// statement). The closer, more specific signal wins over the more distant sink marker.</summary>
    private static readonly Regex NameAssignmentEnd = new(@"(?:^|[.\s])Name\s*=\s*$");

    private static readonly Regex LineComment = new(@"//[^\n]*");
    private static readonly Regex BlockComment = new(@"/\*[\s\S]*?\*/");
    private static readonly Regex Hole = new(@"\{[^{}]*\}");

    private static readonly Regex KebabId = new(@"\b[a-z][a-z0-9]*(?:-[a-z0-9]+){2,}\b");
    private static readonly Regex ScreamingSnake = new(@"\b[A-Z][A-Z0-9]*(?:_[A-Z0-9]+)+\b");
    private static readonly Regex PascalRunTogether = new(@"\b[A-Z][a-z0-9]+[A-Z][A-Za-z0-9]*\b");
    private static readonly Regex RawPermille = new(@"\d+‰|\bpermille\b", RegexOptions.IgnoreCase);
    private static readonly Regex PlanCitation = new(@"P2-[A-Z]+-\d+|§11\.\d+|\bU\d{1,3}\b");
    // `-` deliberately excluded from the operator class: "Pick 1-2" is an ordinary English range,
    // not a formula, and this codebase's real prose uses that shape ("10-15g", "1-2 talents")
    // constantly. `*` and `/` never mean a range in English, so they stay unambiguous signals.
    private static readonly Regex BareFormula = new(@"\d+\s*[*/]\s*\d+|\d+\s*\+\s*\d+");

    /// <summary>A non-verbatim C# string literal, plain or interpolated (<c>"..."</c> / <c>$"..."</c>),
    /// stopping at the first unescaped closing quote. Does not understand verbatim (<c>@"..."</c>)
    /// or raw (<c>"""..."""</c>) strings — this codebase's player copy uses neither, and this is a
    /// text scan, not a parser (same disclaimer as every sibling census).</summary>
    private static readonly Regex StringLiteral = new(@"\$?""(?:[^""\\]|\\.)*""");

    /// <summary>A statement-ish boundary in this codebase's own formatting: a <c>;</c>, <c>{</c>, or
    /// <c>}</c> immediately followed by a newline. Used to bound the BACKWARD half of a
    /// classification window so an unrelated EARLIER statement's sink call (a `.Name =` line sitting
    /// a few statements above a real `.Text =` line, or vice versa) cannot leak a false
    /// classification into the current one purely by character proximity.</summary>
    private static readonly Regex StatementBoundary = new(@"[;{}]\r?\n");

    // ------------------------------------------------------------------ exception ledger tests

    [Fact]
    public void ExceptionCount_IsPinned_SoEveryNewGrantIsAVisibleDiff()
        => Assert.True(Exceptions.Count == ExpectedExceptionCount,
            $"Pinned at {ExpectedExceptionCount}; the table now holds {Exceptions.Count}.");

    [Fact]
    public void EveryPinnedException_CitesTheRulingThatGrantedIt()
    {
        var citation = new Regex(@"§11\.7|\bP\d+\b|P2-[A-Z]+-\d+");
        var uncited = Exceptions
            .Where(e => !citation.IsMatch(e.Value))
            .Select(e => $"{e.Key.Source}  {e.Key.Token}")
            .ToList();

        Assert.True(uncited.Count == 0,
            "An exception with no ruling behind it is drift wearing a reason:\n  " + string.Join("\n  ", uncited));
    }

    // ------------------------------------------------------------------ generator sanity floors
    // Each floor exists so a broken discovery (a moved file, a renamed method) reads as a FAILING
    // test, never as a silently-empty, permanently-green scan.

    [Fact]
    public void Generator1_EnumHoleNames_FindsEnoughToTrustAGreenRun()
    {
        var names = DiscoverUnambiguousEnumHoleNames();
        Assert.True(names.Count >= 8, $"Only {names.Count} unambiguous enum-typed property names found: [{string.Join(", ", names)}]");
        Assert.Contains("Phase", names);
        Assert.Contains("Quality", names);
        Assert.Contains("Slot", names);
        Assert.DoesNotContain("Kind", names); // ambiguous: string on ItemHistoryEntry, enum elsewhere
        Assert.DoesNotContain("Magnitude", names); // ambiguous: int on ConsumableEffect, enum elsewhere
    }

    [Fact]
    public void Generator2_RegistryIdHoleNames_FindsEnoughToTrustAGreenRun()
    {
        var names = DiscoverRegistryIdHoleNames();
        Assert.True(names.Count >= 4, $"Only {names.Count} registry id-holding property names found: [{string.Join(", ", names)}]");
        Assert.Contains("MaterialKey", names);
        Assert.Contains("VenueId", names);
    }

    [Fact]
    public void Generator3_CliVerbs_FindsEnoughToTrustAGreenRun()
    {
        var verbs = DiscoverCliVerbs();
        Assert.True(verbs.Count >= 40, $"Only {verbs.Count} CLI verbs discovered — the scan is broken, not the CLI: [{string.Join(", ", verbs)}]");
        Assert.Contains("next", verbs);
        Assert.Contains("craft", verbs);
    }

    [Fact]
    public void Generator4_SurfaceIds_FindsEnoughToTrustAGreenRun()
    {
        var ids = DiscoverSurfaceIds();
        Assert.True(ids.Count >= 8, $"Only {ids.Count} surface ids discovered: [{string.Join(", ", ids)}]");
        Assert.Contains("Ledger", ids);
    }

    // ------------------------------------------------------------------ negative controls
    // A scanner with no negative control could be flagging everything or nothing.

    /// <summary>Real line (LegendsWall.cs, post this unit's own fix): a registry id resolved
    /// through its DisplayName must NOT be flagged, proving generator 2 keys on the RAW id, not on
    /// the mere presence of the registry's name in the statement.</summary>
    [Fact]
    public void NegativeControl_ARegistryIdResolvedThroughDisplayNameIsNotFlagged()
    {
        const string fixedLine =
            "return (false, $\"Not enough {MaterialRegistry.Require(materialKey).DisplayName.ToLowerInvariant()}: need {needed}, have {have}.\");";

        Assert.Empty(EnumOrRegistryHoleViolations("fixture.cs", fixedLine, isWholeFilePlayerCopy: true));
    }

    /// <summary>Real line (LedgerModal.cs, post P2-HONEST-04): a <see cref="DayPhase"/> resolved
    /// through <c>PhaseVocab.Display</c> must not be flagged either — <c>PhaseVocab.Display(...)</c>
    /// never spells <c>.Phase</c> immediately after the interpolation hole's opening brace.</summary>
    [Fact]
    public void NegativeControl_APhaseResolvedThroughPhaseVocabIsNotFlagged()
    {
        const string fixedLine =
            "_feedback!.Text = $\"Buying {offer.Quantity} {MaterialRegistry.Require(offer.MaterialKey).DisplayName.ToLowerInvariant()} from {card.HeroName} \" + $\"but not until {PhaseVocab.Display(state)} ends.\";";

        Assert.Empty(EnumOrRegistryHoleViolations("fixture.cs", fixedLine, isWholeFilePlayerCopy: true));
    }

    /// <summary>A CLI verb spelled inside ordinary prose, unquoted, must not be flagged — the
    /// generator keys on the QUOTED-command shape ('next'), not on the bare word ever appearing,
    /// which would ban half the game's vocabulary ("day", "close", "board", "next").</summary>
    [Fact]
    public void NegativeControl_ABareCliVerbWordInOrdinaryProseIsNotFlagged()
    {
        const string prose = "AddLabel(body, \"See you next time, when the day is done.\");";
        Assert.Empty(QuotedTokenViolations("fixture.cs", prose, DiscoverCliVerbs(), "CLI verb"));
    }

    // ------------------------------------------------------------------ the regression proof
    // Plant the actual pre-fix defect this unit found, standalone, so this detector's correctness
    // never depends on the real leak still existing in the tree.

    [Fact]
    public void RegressionProof_WouldHaveCaughtTheActualNineSiteLeak()
    {
        const string historicalCode =
            "AddLabel(body, $\"No action slots left today (0/{ActionBudget.SlotsPerDay}) — 'next' to advance.\");";

        var violations = QuotedTokenViolations("fixture.cs", historicalCode, DiscoverCliVerbs(), "CLI verb").ToList();
        Assert.Single(violations);
        Assert.Contains("'next'", violations[0]);
    }

    [Fact]
    public void RegressionProof_WouldHaveCaughtTheActualRawGradeAndSlotLeak()
    {
        // ProvenanceCard.cs, pre this unit's fix, standalone. Note this generator catches a
        // DOT-ACCESS hole ({item.Quality}, {item.Slot}) by reflected property name — it cannot
        // catch a BARE enum-typed local (HeroesPanel.cs's and TavernPanel.cs's own pre-fix
        // {slot}, for an `ItemSlot slot` parameter) the same way, since no reflection over
        // Contracts can link an arbitrary local's NAME to its declared type without a real parser.
        // Those two bare-variable sites were found and fixed by this unit's manual triage, not by
        // this generator; the class doc's "Deviation" paragraph and this comment both say so rather
        // than overclaiming automatic coverage a text scan cannot deliver.
        const string historicalCode =
            "_title!.Text = $\"{item.Name} [{item.Quality}] — {item.Slot}\";";

        var violations = EnumOrRegistryHoleViolations("fixture.cs", historicalCode, isWholeFilePlayerCopy: true).ToList();
        Assert.Equal(2, violations.Count);
        Assert.Contains(violations, v => v.Contains(".Quality"));
    }

    // ------------------------------------------------------------------ P2-HONEST-14: the CLI corpus

    /// <summary>Standalone proof the widened corpus is actually wired to generator 5, not just
    /// declared in a doc comment: a plan-unit citation SHAPE the existing <see cref="PlanCitation"/>
    /// regex already recognizes (unlike <c>PKD4</c> — see the class doc's "refuted claim"
    /// paragraph), planted inside a <c>Console.WriteLine(</c> call, the CLI's own player-copy
    /// sink.</summary>
    [Fact]
    public void RegressionProof_CliCorpus_WouldCatchAPlanCitationInPlayerCopy()
    {
        const string historicalCode = "Console.WriteLine(\"  a fixture hint, U26 style, never real copy.\");";

        var violations = MintedIdentifierViolationsInSinkMarkedFile("fixture.cs", historicalCode, CliSinkMarker, wholeFile: false, includeKebabAndPascal: true).ToList();
        Assert.Contains(violations, v => v.Contains("plan-unit citation"));
    }

    /// <summary>A <c>Console.Error.WriteLine(</c> call — a malformed-CLI-INVOCATION diagnostic for
    /// the batch/decisions/probe/... dev-tool dispatch, never a line the blacksmith-player reads in
    /// a live campaign — must not be flagged, proving <see cref="CliSinkMarker"/> excludes it.</summary>
    [Fact]
    public void NegativeControl_CliErrorDiagnosticIsNotFlaggedAsPlayerCopy()
    {
        const string historicalCode = "Console.Error.WriteLine(\"long-wall: bad --seeds, U26\");";

        var violations = MintedIdentifierViolationsInSinkMarkedFile("fixture.cs", historicalCode, CliSinkMarker, wholeFile: false, includeKebabAndPascal: true).ToList();
        Assert.Empty(violations);
    }

    // ------------------------------------------------------------------ the main sweeps

    [Fact]
    public void EveryEnumOrRegistryHole_InPlayerCopy_IsFixedOrPinnedWithAReason()
    {
        var violations = new List<string>();
        foreach (var (relative, absolute) in ClientSourceFiles())
        {
            violations.AddRange(EnumOrRegistryHoleViolations(relative, File.ReadAllText(absolute), isWholeFilePlayerCopy: false));
        }
        foreach (var (relative, absolute) in SimProseFiles())
        {
            violations.AddRange(EnumOrRegistryHoleViolations(relative, File.ReadAllText(absolute), isWholeFilePlayerCopy: true));
        }
        foreach (var (relative, absolute) in CliInteractiveSourceFiles())
        {
            violations.AddRange(EnumOrRegistryHoleViolations(relative, File.ReadAllText(absolute), isWholeFilePlayerCopy: false, sinkMarker: CliSinkMarker));
        }

        violations = violations.Where(v => !Exceptions.ContainsKey(("enum-or-registry", v))).ToList();

        Assert.True(violations.Count == 0,
            "Player copy interpolates a raw enum or registry id with a DisplayName sibling instead "
            + "of routing through a declared display. Fix by declaring/using a *Vocab.Display, or "
            + "pin a cited exception:\n  " + string.Join("\n  ", violations));
    }

    [Fact]
    public void EveryCliVerbOrSurfaceId_QuotedInClientCopy_IsFixedOrPinnedWithAReason()
    {
        var verbs = DiscoverCliVerbs();
        var surfaces = DiscoverSurfaceIds();
        var violations = new List<string>();

        foreach (var (relative, absolute) in ClientSourceFiles())
        {
            var code = File.ReadAllText(absolute);
            violations.AddRange(QuotedTokenViolations(relative, code, verbs, "CLI verb"));
            violations.AddRange(QuotedTokenViolations(relative, code, surfaces, "surface id"));
        }

        violations = violations.Where(v => !Exceptions.ContainsKey(("quoted-token", v))).ToList();

        Assert.True(violations.Count == 0,
            "Client copy quotes a CLI dispatch verb or a surface id as though the player could type "
            + "or see it directly — the exact shape this unit's own first run found nine times over "
            + "(\"'next' to advance\" in a GUI with no console). Reword to name the actual control, "
            + "or pin a cited exception:\n  " + string.Join("\n  ", violations));
    }

    [Fact]
    public void EveryMintedIdentifier_InPlayerCopy_IsFixedOrPinnedWithAReason()
    {
        var violations = new List<string>();

        // godot/scripts: match ONLY inside actual string-literal spans that a recognized sink (or
        // AdventureTicker.cs's whole file) renders. Matching the raw file text directly — as the
        // enum/registry-hole scan safely does, because ITS pattern requires a literal `{` immediately
        // before it — is unsafe here: kebab/SCREAMING_SNAKE/PascalCase patterns match ordinary C#
        // CODE (method names, type names, "PanelContainer", "StatChip") just as readily as prose, so
        // this scan must stay INSIDE string literals, never the surrounding code.
        foreach (var (relative, absolute) in ClientSourceFiles())
        {
            var code = StripComments(File.ReadAllText(absolute));
            var wholeFile = relative.EndsWith("AdventureTicker.cs", StringComparison.Ordinal);
            violations.AddRange(MintedIdentifierViolationsInSinkMarkedFile(relative, code, SinkMarker, wholeFile, includeKebabAndPascal: true));
        }

        // P2-HONEST-14: Program.cs's own REPL prints, sink-marked on Console.WriteLine(/Console.Write(
        // rather than the godot markers above — see the class doc's "P2-HONEST-14" paragraph. Kebab
        // and PascalCase-run-together stay ON here (unlike the sim-prose block below): Program.cs is
        // dense C# control flow, not a flavor pack of string-keyed dictionaries, so it carries none of
        // the unrendered kebab/camel CODE KEYS that made those two sub-checks noisy for the three sim
        // files — the same reasoning ClientSourceFiles already relies on.
        foreach (var (relative, absolute) in CliInteractiveSourceFiles())
        {
            var code = StripComments(File.ReadAllText(absolute));
            violations.AddRange(MintedIdentifierViolationsInSinkMarkedFile(relative, code, CliSinkMarker, wholeFile: false, includeKebabAndPascal: true));
        }

        // The three sim-side prose sources: every string IS the corpus, so no sink lookup is
        // needed — but kebab-case/PascalCase-run-together stay OFF here (class doc: ChronicleComposer
        // and every Flavor pack carry kebab/camel CODE keys, never rendered, that a text scan cannot
        // tell apart from a real leak).
        foreach (var (relative, absolute) in SimProseFiles())
        {
            var code = StripComments(File.ReadAllText(absolute));
            var noHoles = Hole.Replace(code, m => Blank(m.Value));
            foreach (var (offset, label) in FindMintedIdentifiers(noHoles, includeKebabAndPascal: false))
            {
                var lineNumber = noHoles[..offset].Count(c => c == '\n') + 1;
                var window = WindowFor(noHoles, offset);
                violations.Add($"{relative}:{lineNumber}  {label}: {window.Trim()}");
            }
        }

        violations = violations.Where(v => !Exceptions.ContainsKey(("minted-identifier", v))).ToList();

        Assert.True(violations.Count == 0,
            "Player copy mints an identifier-shaped fragment (kebab-case id, SCREAMING_SNAKE, "
            + "PascalCaseRunTogether, a raw permille, or a plan-unit citation) instead of authored "
            + "prose. Fix the copy, or pin a cited exception:\n  " + string.Join("\n  ", violations));
    }

    // ------------------------------------------------------------------ generator 1: enum-typed holes

    /// <summary>Every property name across <c>GameSim.Contracts</c> whose declared type is ALWAYS an
    /// enum wherever that exact name appears in Contracts. A name used for a non-enum type ANYWHERE
    /// in Contracts (<c>Kind</c>: <c>string</c> on <see cref="GameSim.Contracts.ItemHistoryEntry"/>,
    /// enum elsewhere; <c>Magnitude</c>: <c>int</c> on <see cref="GameSim.Contracts.ConsumableEffect"/>,
    /// enum on the director's incident event) is excluded automatically rather than risking a
    /// same-named, differently-typed false positive a pure text scan cannot resolve.</summary>
    private static List<string> DiscoverUnambiguousEnumHoleNames()
    {
        var byName = new Dictionary<string, HashSet<Type>>(StringComparer.Ordinal);
        foreach (var type in ContractsAssembly.GetTypes())
        {
            if (type.Namespace != "GameSim.Contracts")
            {
                continue;
            }

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                if (!byName.TryGetValue(prop.Name, out var types))
                {
                    byName[prop.Name] = types = new HashSet<Type>();
                }
                types.Add(prop.PropertyType);
            }
        }

        return byName
            .Where(kv => kv.Value.All(t => t.IsEnum && t.Namespace == "GameSim.Contracts"))
            .Select(kv => kv.Key)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
    }

    // ------------------------------------------------------------------ generator 2: registry ids

    /// <summary>Every public static type in the sim assembly with a public static <c>All</c>
    /// collection whose element type carries both <c>Id</c> and a <c>string DisplayName</c> — this
    /// is what makes a registry added later ban its own ids with no edit here. Handles both
    /// <c>ImmutableSortedDictionary&lt;string, T&gt;</c> (unwraps the <c>KeyValuePair</c>) and
    /// <c>ImmutableArray&lt;T&gt;</c> (the element IS <c>T</c>) uniformly by inspecting each
    /// enumerated item's OWN runtime type rather than the static generic signature.</summary>
    private static List<string> DiscoverRegistryIdHoleNames()
    {
        var nouns = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in ContractsAssembly.GetTypes())
        {
            if (!(type.IsClass && type.IsAbstract && type.IsSealed))
            {
                continue; // not a `static class`
            }

            var member = type.GetMember("All", BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m is PropertyInfo or FieldInfo);
            object? value = member switch
            {
                PropertyInfo p => p.GetValue(null),
                FieldInfo f => f.GetValue(null),
                _ => null,
            };
            if (value is not System.Collections.IEnumerable seq)
            {
                continue;
            }

            foreach (var raw in seq)
            {
                var item = raw;
                var itemType = item?.GetType();
                if (itemType is { IsGenericType: true } && itemType.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
                {
                    item = itemType.GetProperty("Value")!.GetValue(item);
                    itemType = item?.GetType();
                }
                if (item is null || itemType is null)
                {
                    continue;
                }

                var idProp = itemType.GetProperty("Id");
                var nameProp = itemType.GetProperty("DisplayName");
                if (idProp is null || nameProp is null || nameProp.PropertyType != typeof(string))
                {
                    continue;
                }
                if (idProp.GetValue(item) is null || nameProp.GetValue(item) is not string)
                {
                    continue;
                }

                var typeName = itemType.Name;
                nouns.Add(typeName.EndsWith("Definition", StringComparison.Ordinal)
                    ? typeName[..^"Definition".Length]
                    : typeName);
            }
        }

        var candidates = new HashSet<string>(StringComparer.Ordinal);
        foreach (var noun in nouns)
        {
            candidates.Add(noun);
            candidates.Add(noun + "Id");
            candidates.Add(noun + "Key");
        }

        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in ContractsAssembly.GetTypes())
        {
            if (type.Namespace != "GameSim.Contracts")
            {
                continue;
            }

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.PropertyType == typeof(string) && candidates.Contains(prop.Name))
                {
                    found.Add(prop.Name);
                }
            }
        }

        return found.OrderBy(n => n, StringComparer.Ordinal).ToList();
    }

    // ------------------------------------------------------------------ generator 3: CLI verbs

    /// <summary>Every verb <c>sim/GameSim.Cli/Program.cs</c> itself dispatches on — reads the
    /// CLI's own source rather than a hand-typed list, including multi-pattern arms
    /// (<c>case "quit" or "exit":</c>) so BOTH tokens ban themselves, not just the first.</summary>
    private static List<string> DiscoverCliVerbs()
    {
        var path = Path.Combine(RepoRoot(), "sim", "GameSim.Cli", "Program.cs");
        var code = File.ReadAllText(path);
        var labelPattern = new Regex("case\\s+((?:\"[a-zA-Z][\\w-]*\"\\s*(?:or\\s*)?)+):");
        var tokenPattern = new Regex("\"([a-zA-Z][\\w-]*)\"");

        var verbs = new SortedSet<string>(StringComparer.Ordinal);
        foreach (Match label in labelPattern.Matches(code))
        {
            foreach (Match token in tokenPattern.Matches(label.Groups[1].Value))
            {
                verbs.Add(token.Groups[1].Value);
            }
        }

        return verbs.ToList();
    }

    // ------------------------------------------------------------------ generator 4: surface ids

    /// <summary>Every id <c>MainUi.cs</c> itself registers with <c>SurfaceArbiter.Claim</c> — the
    /// live declaration <see cref="GameSim.Tests.Presentation.SurfaceClaimDiscoveryCensusTests"/>
    /// already proves is complete for every claimable modal.</summary>
    private static List<string> DiscoverSurfaceIds()
    {
        var path = Path.Combine(RepoRoot(), "godot", "scripts", "MainUi.cs");
        var code = File.ReadAllText(path);
        return Regex.Matches(code, "new SurfaceClaim\\(\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToList();
    }

    // ------------------------------------------------------------------ shared: enum/registry hole scan

    private static IEnumerable<string> EnumOrRegistryHoleViolations(string relative, string code, bool isWholeFilePlayerCopy, Regex? sinkMarker = null)
    {
        var marker = sinkMarker ?? SinkMarker;
        var names = DiscoverUnambiguousEnumHoleNames().Concat(DiscoverRegistryIdHoleNames());
        var alternation = string.Join("|", names.Select(Regex.Escape));
        var hole = new Regex($@"\{{[A-Za-z_][\w.?!]*\.(?:{alternation})\b");

        var stripped = StripComments(code);
        foreach (Match m in hole.Matches(stripped))
        {
            var window = WindowFor(stripped, m.Index);
            var backward = TrimToLastStatementBoundary(stripped[..m.Index]);
            if (NameAssignmentEnd.IsMatch(backward))
            {
                continue;
            }

            if (!isWholeFilePlayerCopy
                && !relative.EndsWith("AdventureTicker.cs", StringComparison.Ordinal)
                && !marker.IsMatch(window))
            {
                continue;
            }

            var lineNumber = stripped[..m.Index].Count(c => c == '\n') + 1;
            yield return $"{relative}:{lineNumber}  {m.Value}…  ({window.Trim()})";
        }
    }

    // ------------------------------------------------------------------ shared: quoted-token scan

    private static IEnumerable<string> QuotedTokenViolations(string relative, string code, IEnumerable<string> tokens, string label)
    {
        var tokenSet = new HashSet<string>(tokens, StringComparer.Ordinal);
        var stripped = StripComments(code);
        foreach (Match m in Regex.Matches(stripped, "['`]([a-zA-Z][\\w-]+)['`]"))
        {
            if (!tokenSet.Contains(m.Groups[1].Value))
            {
                continue;
            }

            var lineNumber = stripped[..m.Index].Count(c => c == '\n') + 1;
            yield return $"{relative}:{lineNumber}  '{m.Groups[1].Value}' ({label})";
        }
    }

    // ------------------------------------------------------------------ shared: sink-marked minted-identifier scan

    /// <summary>Extracted from <c>EveryMintedIdentifier_InPlayerCopy_IsFixedOrPinnedWithAReason</c>'s
    /// own original ClientSourceFiles loop (P2-HONEST-06) so a second sink-marked corpus
    /// (<see cref="CliInteractiveSourceFiles"/>, P2-HONEST-14) can reuse the identical scan against
    /// its own marker instead of a second hand-copied loop. Matched PER LINE, deliberately: this
    /// codebase's real player copy is always a single-line literal (multi-line prose is `+`-joined
    /// single-line literals). Matching the whole file in one pass let a stray quote INSIDE a
    /// blanked-out comment or an unrelated later literal make the regex "leak" across dozens of real
    /// statements, since a naive `"..."` match has no notion of a line boundary.</summary>
    private static IEnumerable<string> MintedIdentifierViolationsInSinkMarkedFile(
        string relative, string code, Regex sinkMarker, bool wholeFile, bool includeKebabAndPascal)
    {
        var lines = code.Split('\n');
        var precedingContext = string.Empty;
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex];
            foreach (Match literal in StringLiteral.Matches(line))
            {
                // Bounded by the nearest statement boundary, not a flat char count — an
                // unrelated EARLIER statement on the previous line
                // (`_feedback = AddLabel(root, string.Empty);`) must not leak "AddLabel(" into
                // THIS statement's classification (`_feedback.Name = "CounterFeedback";`, a node
                // identifier, never player copy) just because it sits within 150 chars.
                var lookBehind = TrimToLastStatementBoundary(precedingContext + line[..literal.Index]);

                // The Name= exclusion applies even in whole-file mode — a `Name = "SomeNodeId"`
                // assignment is a node id like any other, never player copy, and "the whole file is
                // the corpus" was never meant to reach that.
                if (NameAssignmentEnd.IsMatch(lookBehind))
                {
                    continue;
                }

                if (!wholeFile && !sinkMarker.IsMatch(lookBehind))
                {
                    continue;
                }

                var content = Hole.Replace(literal.Value, m => Blank(m.Value));
                foreach (var (_, label) in FindMintedIdentifiers(content, includeKebabAndPascal))
                {
                    yield return $"{relative}:{lineIndex + 1}  {label}: {content.Trim()}";
                }
            }

            precedingContext = TrimToLastStatementBoundary(precedingContext + line + "\n");
            if (precedingContext.Length > 150)
            {
                precedingContext = precedingContext[^150..];
            }
        }
    }

    // ------------------------------------------------------------------ shared: minted-identifier scan

    /// <summary>Runs the minted-identifier regex family over <paramref name="text"/> (already
    /// comment- and hole-stripped) and yields (offset, label) for every match.</summary>
    private static IEnumerable<(int Offset, string Label)> FindMintedIdentifiers(string text, bool includeKebabAndPascal)
    {
        var regexes = new List<(Regex Regex, string Label)>
        {
            (RawPermille, "raw permille"),
            (PlanCitation, "plan-unit citation"),
            (ScreamingSnake, "SCREAMING_SNAKE"),
            (BareFormula, "bare formula"),
        };
        if (includeKebabAndPascal)
        {
            regexes.Add((KebabId, "kebab-case id"));
            regexes.Add((PascalRunTogether, "PascalCaseRunTogether"));
        }

        foreach (var (regex, label) in regexes)
        {
            foreach (Match m in regex.Matches(text))
            {
                yield return (m.Index, label);
            }
        }
    }

    private static string Blank(string value) => new(value.Select(c => c == '\n' ? '\n' : ' ').ToArray());

    private static string StripComments(string code)
    {
        var noBlocks = BlockComment.Replace(code, m => Blank(m.Value));
        return LineComment.Replace(noBlocks, m => Blank(m.Value));
    }

    /// <summary>Keeps only the text after the LAST <c>;</c>/<c>{</c>/<c>}</c> in <paramref
    /// name="text"/> — the same statement-boundary idea as <see cref="WindowFor"/>'s backward bound,
    /// used wherever a rolling lookbehind buffer must not let an earlier, unrelated statement's sink
    /// call leak into the current one's classification.</summary>
    private static string TrimToLastStatementBoundary(string text)
    {
        for (var i = text.Length - 1; i >= 0; i--)
        {
            if (text[i] is ';' or '{' or '}')
            {
                return text[(i + 1)..];
            }
        }
        return text;
    }

    private static string WindowFor(string code, int index)
    {
        // Backward bound: the nearest preceding statement boundary, capped so a single pathological
        // long statement can't scan half the file — never a flat character count on its own, which
        // is what let an EARLIER, unrelated statement's sink call (an `AddLabel(...)` two lines above
        // a `.Name = $"..."` line) leak a false classification into this hole purely by proximity.
        var cap = Math.Max(0, index - BackwardWindow * 3);
        var before = code[cap..index];
        var lastBoundary = -1;
        foreach (Match b in StatementBoundary.Matches(before))
        {
            lastBoundary = b.Index + b.Length;
        }
        var start = lastBoundary >= 0 ? cap + lastBoundary : Math.Max(0, index - BackwardWindow);

        var rest = code[index..];
        var terminator = StatementTerminator.Match(rest);
        var forwardEnd = terminator.Success
            ? index + Math.Min(terminator.Index + 1, ForwardCap)
            : Math.Min(code.Length, index + ForwardCap);
        return code[start..forwardEnd];
    }

    // ------------------------------------------------------------------ corpus

    private static IEnumerable<(string Relative, string Absolute)> ClientSourceFiles()
    {
        var scriptsRoot = Path.Combine(RepoRoot(), "godot", "scripts");
        var toolsDir = $"{Path.DirectorySeparatorChar}tools{Path.DirectorySeparatorChar}";

        foreach (var path in Directory.EnumerateFiles(scriptsRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                || path.Contains(toolsDir))
            {
                continue;
            }

            yield return (Path.GetRelativePath(RepoRoot(), path).Replace('\\', '/'), path);
        }
    }

    /// <summary>The three sim-side sources of authored player prose the task names by name: the
    /// flavor packs (gossip/ledger/faction), the Chronicle Night composer, and the tavern gossip
    /// generator. Every string literal in these files IS the corpus — no sink marker required.
    /// </summary>
    private static IEnumerable<(string Relative, string Absolute)> SimProseFiles()
    {
        var root = RepoRoot();
        var packsDir = Path.Combine(root, "sim", "GameSim", "Flavor", "Packs");
        var files = new List<string>();
        if (Directory.Exists(packsDir))
        {
            files.AddRange(Directory.EnumerateFiles(packsDir, "*.cs", SearchOption.TopDirectoryOnly));
        }

        var chronicle = Path.Combine(root, "sim", "GameSim", "Chronicle", "ChronicleComposer.cs");
        if (File.Exists(chronicle))
        {
            files.Add(chronicle);
        }

        var gossip = Path.Combine(root, "sim", "GameSim", "Drama", "GossipGenerator.cs");
        if (File.Exists(gossip))
        {
            files.Add(gossip);
        }

        foreach (var path in files)
        {
            yield return (Path.GetRelativePath(root, path).Replace('\\', '/'), path);
        }
    }

    /// <summary>P2-HONEST-14's own file: <c>sim/GameSim.Cli/Program.cs</c>, the interactive REPL's
    /// player-visible prints. See the class doc's "P2-HONEST-14" paragraph for why this is the whole
    /// corpus (one file, not a directory sweep) and why <see cref="CliSinkMarker"/> rather than
    /// <see cref="SinkMarker"/> is the right marker for it.</summary>
    private static IEnumerable<(string Relative, string Absolute)> CliInteractiveSourceFiles()
    {
        var path = Path.Combine(RepoRoot(), "sim", "GameSim.Cli", "Program.cs");
        if (File.Exists(path))
        {
            yield return (Path.GetRelativePath(RepoRoot(), path).Replace('\\', '/'), path);
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Game.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException("Could not locate the repo root (no Game.sln above the test binary).");
    }
}
