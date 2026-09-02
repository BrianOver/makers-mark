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
        IReadOnlySet<string> trackedFiles)
    {
        var units = plan.Units;

        var collisions = FindCollisions(units);
        var missingFiles = FindMissingFiles(units, trackedFiles);
        var ordering = FindOrderingViolations(units, landed);
        var dangling = FindDanglingDocs(plan.DocRefs, trackedFiles);
        var domains = BuildDomains(units, landed, open);

        return new ReconciliationResult(domains, missingFiles, ordering, dangling, collisions, plan.Unparseable);
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
        IReadOnlyDictionary<string, OpenUnit> open)
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
                        return new UnitReconciliation(u, UnitStatus.Landed, l.Sha, l.PrNumber);
                    }

                    if (open.TryGetValue(u.Id, out var o))
                    {
                        return new UnitReconciliation(u, UnitStatus.Open, null, o.PrNumber);
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
