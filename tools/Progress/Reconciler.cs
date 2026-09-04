using System.Text.RegularExpressions;

namespace Progress;

/// <summary>Combines a parsed plan with git/gh reality into the five findings. Pure: takes
/// records and sets, returns records. No IO here — GitShell gathers the inputs, Program wires
/// them together.</summary>
public static class Reconciler
{
    private static readonly Regex TrailingDomainSuffix = new(@"-\d+[a-zA-Z]?$", RegexOptions.Compiled);

    public static ReconciliationResult Reconcile(
        PlanParseResult plan,
        IReadOnlyDictionary<string, LandedUnit> landed,
        IReadOnlyDictionary<string, OpenUnit> open,
        IReadOnlySet<string> trackedFiles,
        IReadOnlyList<MergedPrReceipt>? mergedReceipts = null,
        IReadOnlyDictionary<string, FileOrigin>? fileOrigins = null,
        DateTimeOffset? receiptRuleEffectiveSince = null)
    {
        mergedReceipts ??= Array.Empty<MergedPrReceipt>();
        fileOrigins ??= new Dictionary<string, FileOrigin>();

        var units = plan.Units;

        var collisions = FindCollisions(units);
        var missingFiles = FindMissingFiles(units, trackedFiles);
        var ordering = FindOrderingViolations(units, landed);
        var dangling = FindDanglingDocs(plan.DocRefs, trackedFiles);
        var domains = BuildDomains(units, landed, open, trackedFiles, fileOrigins);

        var statusById = BuildStatusIndex(domains);
        var applicableReceipts = receiptRuleEffectiveSince is { } since
            ? mergedReceipts.Where(r => r.MergedAt >= since).ToList()
            : mergedReceipts;

        var dispatchTraps = FindReceiptDispatchTraps(units, applicableReceipts, statusById);
        var missingReceipts = FindMissingOrMalformedReceipts(applicableReceipts);
        var falseReceipts = FindFalseReceipts(units, applicableReceipts, trackedFiles);

        return new ReconciliationResult(
            domains, missingFiles, ordering, dangling, collisions, plan.Unparseable,
            dispatchTraps, missingReceipts, falseReceipts);
    }

    private static Dictionary<string, UnitStatus> BuildStatusIndex(IReadOnlyList<DomainStatus> domains)
    {
        var statusById = new Dictionary<string, UnitStatus>(StringComparer.Ordinal);
        foreach (var row in domains.SelectMany(d => d.Rows))
        {
            // Last write wins on a duplicate id — the collision itself is reported separately
            // (FindCollisions); this index only needs *a* status to cross-check receipts against.
            statusById[row.Unit.Id] = row.Status;
        }

        return statusById;
    }

    private static List<ReceiptDispatchTrap> FindReceiptDispatchTraps(
        IReadOnlyList<UnitRow> units,
        IReadOnlyList<MergedPrReceipt> mergedReceipts,
        IReadOnlyDictionary<string, UnitStatus> statusById)
    {
        var knownIds = new HashSet<string>(units.Select(u => u.Id), StringComparer.Ordinal);
        var traps = new List<ReceiptDispatchTrap>();

        foreach (var pr in mergedReceipts)
        {
            if (pr.Receipt.Kind != ServesKind.Unit)
            {
                continue;
            }

            var unitId = pr.Receipt.UnitId!;
            if (!knownIds.Contains(unitId))
            {
                continue; // receipt cites something this tool doesn't track as a unit-index row
            }

            if (statusById.TryGetValue(unitId, out var status) && status == UnitStatus.Landed)
            {
                continue; // already reported correctly — no trap
            }

            traps.Add(new ReceiptDispatchTrap(unitId, pr.Number, pr.Title));
        }

        return traps
            .OrderBy(t => t.UnitId, StringComparer.Ordinal)
            .ThenBy(t => t.PrNumber)
            .ToList();
    }

    private static List<MissingOrMalformedReceipt> FindMissingOrMalformedReceipts(
        IReadOnlyList<MergedPrReceipt> mergedReceipts)
    {
        return mergedReceipts
            .Where(pr => pr.Receipt.Kind is ServesKind.Missing or ServesKind.Malformed)
            .OrderBy(pr => pr.Number)
            .Select(pr => new MissingOrMalformedReceipt(pr.Number, pr.Title, pr.Receipt.Kind, pr.Receipt.RawValue))
            .ToList();
    }

    private static List<FalseReceipt> FindFalseReceipts(
        IReadOnlyList<UnitRow> units,
        IReadOnlyList<MergedPrReceipt> mergedReceipts,
        IReadOnlySet<string> trackedFiles)
    {
        var byId = units.ToLookup(u => u.Id, StringComparer.Ordinal);
        var findings = new List<FalseReceipt>();

        foreach (var pr in mergedReceipts)
        {
            if (pr.Receipt.Kind != ServesKind.Unit)
            {
                continue;
            }

            var unitId = pr.Receipt.UnitId!;
            foreach (var unit in byId[unitId])
            {
                foreach (var file in unit.Files)
                {
                    // Unlike FindMissingFiles, a "new"-marked path gets NO exemption here: this
                    // receipt asserts the unit is done, so every one of its cited paths — new or
                    // not — should exist by now. That is precisely the check section 2 doesn't do.
                    if (!PathExists(file.Path, trackedFiles))
                    {
                        findings.Add(new FalseReceipt(unitId, pr.Number, file.Path));
                    }
                }
            }
        }

        return findings
            .OrderBy(f => f.UnitId, StringComparer.Ordinal)
            .ThenBy(f => f.PrNumber)
            .ToList();
    }

    private static List<IdCollision> FindCollisions(IReadOnlyList<UnitRow> units)
    {
        return units
            .GroupBy(u => u.Id, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => new IdCollision(
                g.Key,
                g.Select(u => u.LineNumber).OrderBy(n => n).ToList(),
                g.Select(u => u.Table).Distinct().ToList()))
            .OrderBy(c => c.Id, StringComparer.Ordinal)
            .ToList();
    }

    private static List<MissingFileFinding> FindMissingFiles(IReadOnlyList<UnitRow> units, IReadOnlySet<string> trackedFiles)
    {
        // A file only needs its "new" marker on the ONE unit that first creates it — a later unit
        // that also touches the same not-yet-landed file correctly omits it. So a path is expected
        // when EITHER this row marks it new, OR any row anywhere in the plan does; anything else
        // citing a path nobody has claimed to create, and that isn't on origin/main, is a real
        // finding — a wrong path (this repo's own motivating case: a unit filed against
        // `sim/GameSim/Kernel/PhaseClock.cs` when the clock lives at `godot/scripts/PhaseClock.cs`)
        // or a plan gap where no unit is actually on the hook for creating a cited file.
        var declaredNew = new HashSet<string>(
            units.SelectMany(u => u.Files).Where(f => f.IsNew).Select(f => f.Path),
            StringComparer.Ordinal);

        var findings = new List<MissingFileFinding>();
        foreach (var unit in units)
        {
            foreach (var file in unit.Files)
            {
                if (file.IsNew || declaredNew.Contains(file.Path))
                {
                    continue;
                }

                if (PathExists(file.Path, trackedFiles))
                {
                    continue;
                }

                findings.Add(new MissingFileFinding(unit.Id, file.Path, unit.LineNumber));
            }
        }

        return findings;
    }

    private static bool PathExists(string path, IReadOnlySet<string> trackedFiles)
    {
        if (path.EndsWith('/'))
        {
            // Directory reference — exists if any tracked file lives under it.
            return trackedFiles.Any(f => f.StartsWith(path, StringComparison.Ordinal));
        }

        return trackedFiles.Contains(path);
    }

    private static List<OrderingViolation> FindOrderingViolations(
        IReadOnlyList<UnitRow> units,
        IReadOnlyDictionary<string, LandedUnit> landed)
    {
        var knownIds = new HashSet<string>(units.Select(u => u.Id), StringComparer.Ordinal);
        var violations = new List<OrderingViolation>();

        foreach (var unit in units)
        {
            if (!landed.TryGetValue(unit.Id, out var landedUnit))
            {
                continue; // only landed units can violate ordering
            }

            foreach (var dep in unit.DependsOn)
            {
                if (!knownIds.Contains(dep))
                {
                    continue; // not a unit this tool tracks (a ruling, a critical-path item, ...)
                }

                if (!landed.ContainsKey(dep))
                {
                    violations.Add(new OrderingViolation(unit.Id, dep, landedUnit.Sha, landedUnit.PrNumber));
                }
            }
        }

        return violations
            .OrderBy(v => v.UnitId, StringComparer.Ordinal)
            .ThenBy(v => v.DepId, StringComparer.Ordinal)
            .ToList();
    }

    private static List<DocRef> FindDanglingDocs(IReadOnlyList<DocRef> docRefs, IReadOnlySet<string> trackedFiles)
    {
        return docRefs
            .Where(d => !trackedFiles.Contains(d.Path))
            .GroupBy(d => d.Path, StringComparer.Ordinal)
            .Select(g => g.OrderBy(d => d.LineNumber).First())
            .OrderBy(d => d.Path, StringComparer.Ordinal)
            .ToList();
    }

    private static List<DomainStatus> BuildDomains(
        IReadOnlyList<UnitRow> units,
        IReadOnlyDictionary<string, LandedUnit> landed,
        IReadOnlyDictionary<string, OpenUnit> open,
        IReadOnlySet<string> trackedFiles,
        IReadOnlyDictionary<string, FileOrigin> fileOrigins)
    {
        var byDomain = units.GroupBy(u => DomainOf(u));

        var domains = new List<DomainStatus>();
        foreach (var group in byDomain.OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var rows = group
                .Select(u =>
                {
                    if (landed.TryGetValue(u.Id, out var l))
                    {
                        return new UnitReconciliation(u, UnitStatus.Landed, l.Sha, l.PrNumber, LandedEvidence.CommitTag);
                    }

                    if (open.TryGetValue(u.Id, out var o))
                    {
                        return new UnitReconciliation(u, UnitStatus.Open, null, o.PrNumber);
                    }

                    // No commit subject or PR title carried this id's exact tag. Fall back to the
                    // census: this row's own "new "-marked file(s) — the ones ONLY this unit is on
                    // the hook for creating — existing on origin/main is direct tree evidence the
                    // unit landed, independent of what any title or receipt says.
                    var newFiles = u.Files.Where(f => f.IsNew).ToList();
                    if (newFiles.Count > 0 && newFiles.All(f => PathExists(f.Path, trackedFiles)))
                    {
                        var origin = newFiles
                            .Select(f => fileOrigins.TryGetValue(f.Path, out var fo) ? fo : null)
                            .FirstOrDefault(fo => fo is not null);
                        return new UnitReconciliation(u, UnitStatus.Landed, origin?.Sha, origin?.PrNumber, LandedEvidence.FileExistence);
                    }

                    return new UnitReconciliation(u, UnitStatus.Unbuilt, null, null);
                })
                .OrderBy(r => r.Unit.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();

            domains.Add(new DomainStatus(group.Key, rows));
        }

        return domains;
    }

    private static string DomainOf(UnitRow unit) =>
        unit.Table == UnitTable.T10 ? "T10" : TrailingDomainSuffix.Replace(unit.Id, string.Empty);
}
