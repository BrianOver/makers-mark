namespace Progress;

/// <summary>Which unit-index table shape a row was parsed from — the two shapes the plan
/// actually uses (verified against docs/design/MAKERS-MARK.md on 2026-09): the Phase 2 domain
/// index (`| ⚑ P2-DOMAIN-nn | title | files | deps | flags |`, 5 columns) and T10's course index
/// (`| Un | title | files | deps |`, 4 columns).</summary>
public enum UnitTable
{
    P2,
    T10,
}

/// <summary>One file/dir path cited in a unit's "Key files" column. <see cref="IsNew"/> is true
/// when the cell text marks it as a file the unit will create (the doc's own convention: a bare
/// "new " immediately before the backtick span), in which case it not existing yet on origin/main
/// is expected, not a defect.</summary>
public sealed record FileRef(string Path, bool IsNew);

/// <summary>One successfully parsed unit-index row.</summary>
public sealed record UnitRow(
    UnitTable Table,
    string Id,
    string Title,
    IReadOnlyList<FileRef> Files,
    IReadOnlyList<string> DependsOn,
    string DependsOnRaw,
    IReadOnlyList<string> Flags,
    int LineNumber,
    IReadOnlyList<string>? UnparsedDependsOn = null)
{
    /// <summary>Tokens the Depends-on cell carried that are not unit rows this tool tracks — a
    /// critical-path item (<c>P4</c>), an open ruling (<c>P2-OQ1</c>), a section cite. Empty for
    /// almost every row. A human reading the report can see the raw cell beside it and needs no
    /// help; <c>--frontier</c> cannot, so it refuses any row with an entry here rather than
    /// reporting an owner-gated unit as runnable.</summary>
    public IReadOnlyList<string> UnparsedDependsOn { get; init; } = UnparsedDependsOn ?? Array.Empty<string>();
}

/// <summary>A table row that looked like a unit-index row (its first cell parsed as a unit id)
/// but did not fit the shape expected for that id family. Reported, never silently dropped —
/// see CLAUDE.md's note on hand-listed fixtures that quietly stop covering their family.</summary>
public sealed record UnparseableRow(int LineNumber, string RawLine, string Reason);

/// <summary>A `docs/**.md` path cited anywhere in the plan (table cell or prose).</summary>
public sealed record DocRef(string Path, int LineNumber);

public sealed record PlanParseResult(
    IReadOnlyList<UnitRow> Units,
    IReadOnlyList<UnparseableRow> Unparseable,
    IReadOnlyList<DocRef> DocRefs);

/// <summary>A unit id found landing in origin/main's commit history via its commit-subject tag.</summary>
public sealed record LandedUnit(string Id, string Sha, int? PrNumber);

/// <summary>A unit id found tagged in an open PR's title — not landed, but in flight.</summary>
public sealed record OpenUnit(string Id, int PrNumber, string PrTitle);

public enum UnitStatus
{
    Landed,
    Open,
    Unbuilt,
}

/// <summary>How a Landed status was established. <see cref="CommitTag"/> is the strong signal (a
/// commit subject or PR title carries the unit's exact id). <see cref="FileExistence"/> is the
/// fallback census: the row's own "new "-marked file(s) exist on origin/main even though no
/// commit subject or title carried the token (a rider commit, a squash that dropped the suffix,
/// a title that names only the domain) — the tree outranks the missing tag, per rule 8.</summary>
public enum LandedEvidence
{
    CommitTag,
    FileExistence,
}

public sealed record UnitReconciliation(
    UnitRow Unit,
    UnitStatus Status,
    string? Sha,
    int? PrNumber,
    LandedEvidence? Evidence = null);

public sealed record DomainStatus(string Domain, IReadOnlyList<UnitReconciliation> Rows);

public sealed record MissingFileFinding(string UnitId, string Path, int LineNumber);

public sealed record OrderingViolation(string UnitId, string DepId, string? UnitSha, int? UnitPr);

public sealed record IdCollision(string Id, IReadOnlyList<int> LineNumbers, IReadOnlyList<UnitTable> Tables);

/// <summary>Which of rule 12 / §11.6 rule 3's four literal forms a `Serves:` line's value took.
/// <see cref="Unit"/> is the only one this tool can cross-check against a tracked unit-index row
/// (a P2 domain id or a T10 `U` id); <see cref="PlanItem"/> covers the older §11.4 critical-path
/// citations (`P1`, `P5(a)`) — valid per the rule, but not a row this tool indexes, so it is
/// tracked separately rather than folded into <see cref="Malformed"/>.</summary>
public enum ServesKind
{
    Unit,
    Link,
    Substrate,
    Overhead,
    PlanItem,
    Malformed,
    Missing,
}

/// <summary>One PR body's `Serves:` line, parsed. <see cref="RawValue"/> is the trimmed text after
/// the colon (null when the line itself is <see cref="ServesKind.Missing"/>); <see cref="UnitId"/>
/// is populated only for <see cref="ServesKind.Unit"/>.</summary>
public sealed record ServesReceipt(ServesKind Kind, string? RawValue, string? UnitId);

/// <summary>One merged PR, with its parsed receipt attached — the input the three receipt-census
/// findings below are computed from.</summary>
public sealed record MergedPrReceipt(int Number, string Title, DateTimeOffset MergedAt, ServesReceipt Receipt);

/// <summary>The commit that first added a "new "-marked path to <c>origin/main</c>, used to
/// attribute a file-existence-derived Landed status to a sha/PR the same way a commit-tag match
/// would be.</summary>
public sealed record FileOrigin(string Path, string Sha, int? PrNumber);

/// <summary>The redundant-dispatch trap: a merged PR's `Serves:` receipt names a tracked unit that
/// this run's own reconciliation still reports as not Landed. Whoever reads only the receipt (not
/// this tool) would wrongly believe the unit is done, or wrongly re-dispatch it.</summary>
public sealed record ReceiptDispatchTrap(string UnitId, int PrNumber, string PrTitle);

/// <summary>A merged PR (on or after the receipt rule's own effective date) whose body carries no
/// `Serves:` line at all, or one whose value matches none of rule 12's four literal forms.</summary>
public sealed record MissingOrMalformedReceipt(int PrNumber, string PrTitle, ServesKind Kind, string? RawValue);

/// <summary>A `Serves:` receipt naming a specific tracked unit whose own cited path is not on
/// origin/main — the receipt asserts the unit is done; the tree disagrees. "The receipt can lie;
/// the census cannot" (CLAUDE.md rule 12).</summary>
public sealed record FalseReceipt(string UnitId, int PrNumber, string Path);

/// <summary>
/// A unit this run reports UNBUILT whose exact id is nevertheless written into tracked source on
/// origin/main — the shape that made a session re-derive four already-shipped units by hand before
/// noticing.
///
/// <para><b>Deliberately a warning, never a status.</b> The two existing Landed evidence classes
/// (a commit/PR title carrying the tag, and a row's own "new "-marked file existing) are both
/// unambiguous. A unit id inside a source comment is not: it is equally the mark a shipping PR
/// leaves behind AND the mark a comment leaves when it defers work
/// (<c>"…is P2-ONBOARD-09's own remaining scope, not this unit's"</c> is a real line on main).
/// Promoting that to Landed would manufacture exactly the false green this whole tool exists to
/// catch, so it stays a separate section that says "verify before building" and changes no
/// count and no exit code.</para>
///
/// <para>It earns its place anyway because the miss it covers is real and expensive: a PR landing
/// five units under one <c>Serves: link4</c> receipt (#687 did) leaves four of them reported
/// unbuilt indefinitely — the commit subject carries no per-unit tag, and their rows name files
/// that already existed, so neither existing evidence class can see them.</para>
/// </summary>
public sealed record SourceTaggedUnbuilt(string UnitId, IReadOnlyList<string> Paths);

public sealed record ReconciliationResult(
    IReadOnlyList<DomainStatus> Domains,
    IReadOnlyList<MissingFileFinding> MissingFiles,
    IReadOnlyList<OrderingViolation> OrderingViolations,
    IReadOnlyList<DocRef> DanglingDocs,
    IReadOnlyList<IdCollision> Collisions,
    IReadOnlyList<UnparseableRow> Unparseable,
    IReadOnlyList<ReceiptDispatchTrap> ReceiptDispatchTraps,
    IReadOnlyList<MissingOrMalformedReceipt> MissingOrMalformedReceipts,
    IReadOnlyList<FalseReceipt> FalseReceipts,
    IReadOnlyList<SourceTaggedUnbuilt> SourceTaggedUnbuilts);
