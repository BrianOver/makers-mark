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
    int LineNumber);

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

public sealed record UnitReconciliation(UnitRow Unit, UnitStatus Status, string? Sha, int? PrNumber);

public sealed record DomainStatus(string Domain, IReadOnlyList<UnitReconciliation> Rows);

public sealed record MissingFileFinding(string UnitId, string Path, int LineNumber);

public sealed record OrderingViolation(string UnitId, string DepId, string? UnitSha, int? UnitPr);

public sealed record IdCollision(string Id, IReadOnlyList<int> LineNumbers, IReadOnlyList<UnitTable> Tables);

public sealed record ReconciliationResult(
    IReadOnlyList<DomainStatus> Domains,
    IReadOnlyList<MissingFileFinding> MissingFiles,
    IReadOnlyList<OrderingViolation> OrderingViolations,
    IReadOnlyList<DocRef> DanglingDocs,
    IReadOnlyList<IdCollision> Collisions,
    IReadOnlyList<UnparseableRow> Unparseable);
