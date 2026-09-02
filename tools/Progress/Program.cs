using Progress;

// Progress reconciler (docs/debugging.md has the "how to run it / what each finding means"
// section). Answers "where does this program actually stand" straight from git and gh, every
// run, so nobody has to hand-reconcile docs/design/MAKERS-MARK.md's unit index against git log
// again — see that file's Progress.csproj doc comment for why it stores nothing.
//
// Usage: dotnet run --project tools/Progress -- [path/to/plan.md]
//   path/to/plan.md defaults to docs/design/MAKERS-MARK.md, resolved from the repo root
//   (`git rev-parse --show-toplevel`). Always reconciles against `origin/main`, never the
//   working tree or the current branch — that is the whole point.

var repoRoot = GitShell.FindRepoRoot(Directory.GetCurrentDirectory());
var planPath = args.Length > 0 ? args[0] : "docs/design/MAKERS-MARK.md";

GitShell.TryFetchOriginMain(repoRoot);

var planText = GitShell.ReadFile(repoRoot, planPath);
var plan = PlanParser.Parse(planText);

var trackedFiles = GitShell.ListTrackedFiles(repoRoot, "origin/main");
var log = GitShell.GetLog(repoRoot, "origin/main");

var ownerRepo = GitShell.GetOwnerRepo(repoRoot);
var mergedPrs = GitShell.ListPrs(repoRoot, ownerRepo, "merged");
var openPrs = GitShell.ListPrs(repoRoot, ownerRepo, "open");

var landed = BuildLandedIndex(log, mergedPrs);
var open = BuildOpenIndex(openPrs, landed);

var result = Reconciler.Reconcile(plan, landed, open, trackedFiles);

var headSha = log.Count > 0 ? log[^1].Sha[..9] : "unknown";
Console.WriteLine(Report.Build(result, $"{planPath} vs origin/main@{headSha}"));

var failing = result.MissingFiles.Count > 0 || result.OrderingViolations.Count > 0 || result.Collisions.Count > 0;
return failing ? 1 : 0;

static Dictionary<string, LandedUnit> BuildLandedIndex(
    IReadOnlyList<GitLogEntry> log,
    IReadOnlyList<GitShell.PrRecord> mergedPrs)
{
    // gh's merged-PR list is the trap-avoider named in the tool's requirements (squash-merge
    // makes `git branch --merged` useless here) — used to fill in a PR number for the rare landed
    // commit whose subject lost its trailing "(#NNN)" (a manual squash, a rebase edit), by
    // matching the commit's tag-stripped subject text against a merged PR's title.
    var titleToPr = new Dictionary<string, int>(StringComparer.Ordinal);
    foreach (var pr in mergedPrs)
    {
        titleToPr.TryAdd(pr.Title.Trim(), pr.Number);
    }

    var landed = new Dictionary<string, LandedUnit>(StringComparer.Ordinal);
    foreach (var entry in log) // GetLog returns oldest-first: first landing wins below
    {
        var ids = CommitTags.ExtractUnitIds(entry.Subject);
        if (ids.Count == 0)
        {
            continue;
        }

        var pr = CommitTags.ExtractPrNumber(entry.Subject);
        if (pr is null)
        {
            var strippedSubject = System.Text.RegularExpressions.Regex
                .Replace(entry.Subject, @"\(#\d+\)\s*$", string.Empty).Trim();
            if (titleToPr.TryGetValue(strippedSubject, out var fallbackPr))
            {
                pr = fallbackPr;
            }
        }

        foreach (var id in ids)
        {
            if (!landed.ContainsKey(id))
            {
                landed[id] = new LandedUnit(id, entry.Sha, pr);
            }
        }
    }

    return landed;
}

static Dictionary<string, OpenUnit> BuildOpenIndex(
    IReadOnlyList<GitShell.PrRecord> openPrs,
    IReadOnlyDictionary<string, LandedUnit> landed)
{
    var open = new Dictionary<string, OpenUnit>(StringComparer.Ordinal);
    foreach (var pr in openPrs)
    {
        foreach (var id in CommitTags.ExtractUnitIds(pr.Title))
        {
            if (landed.ContainsKey(id) || open.ContainsKey(id))
            {
                continue;
            }

            open[id] = new OpenUnit(id, pr.Number, pr.Title);
        }
    }

    return open;
}
