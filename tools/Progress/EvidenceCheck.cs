namespace Progress;

/// <summary>Whether a unit row's own evidence marker(s) (see <see cref="EvidenceMarker"/>) checked
/// out. <see cref="NoMarker"/> and <see cref="NotFound"/> are both "still counts as unbuilt" for
/// dispatch purposes -- the difference matters only for how loud <c>--frontier</c> is about the
/// uncertainty: <see cref="NoMarker"/> means nobody has checked anything (UNVERIFIED); <see
/// cref="NotFound"/> means the check ran and came back negative (an actual answer, not silence).
/// </summary>
public enum EvidenceStatus
{
    NoMarker,
    NotFound,
    Found,
}

/// <summary><see cref="Path"/>/<see cref="Line"/> are populated only for <see cref="EvidenceStatus.Found"/>
/// — <see cref="Line"/> is null when the marker named a bare path with no symbol (existence alone
/// is the proof; there is no "line" for that).</summary>
public sealed record EvidenceCheckResult(EvidenceStatus Status, string? Path = null, int? Line = null);

/// <summary>
/// Verifies a unit row's `` `evidence:path[:symbol]` `` markers (parsed by <see cref="PlanParser"/>)
/// against the WORKING TREE on disk -- deliberately not `origin/main`, unlike every other check
/// this tool runs. The frontier's consumer is the very checkout about to build tonight, and the
/// gap this whole change closes is a unit whose deliverable already sits in that checkout (or
/// landed on `origin/main` under a commit subject carrying no tag) but that neither the commit-tag
/// index nor the file-existence census nor the source-tag grep can see. Reading the working tree
/// directly is the most literal way to ask "is the thing this row promised actually here."
///
/// <para>Pure I/O, no git: <see cref="File.Exists"/> / <see cref="File.ReadAllLines(string)"/>
/// under <paramref name="repoRoot"/>. A row with no markers never touches disk at all (<see
/// cref="EvidenceStatus.NoMarker"/> short-circuits first), which is what keeps every existing
/// caller/test that never populates <c>UnitRow.Evidence</c> unaffected by this class's existence.
/// </para>
/// </summary>
public static class EvidenceCheck
{
    public static EvidenceCheckResult Check(IReadOnlyList<EvidenceMarker> markers, string repoRoot)
    {
        if (markers.Count == 0)
        {
            return new EvidenceCheckResult(EvidenceStatus.NoMarker);
        }

        foreach (var marker in markers)
        {
            var fullPath = System.IO.Path.Combine(repoRoot, marker.Path.Replace('/', System.IO.Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
            {
                continue;
            }

            if (marker.Symbol is null)
            {
                // Bare path marker: existence alone is the proof this row asked for.
                return new EvidenceCheckResult(EvidenceStatus.Found, marker.Path);
            }

            var lines = File.ReadAllLines(fullPath);
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains(marker.Symbol, StringComparison.Ordinal))
                {
                    return new EvidenceCheckResult(EvidenceStatus.Found, marker.Path, i + 1);
                }
            }
        }

        // Every named file either doesn't exist yet, or exists but never mentions its symbol —
        // an actual negative answer, not the absence of one.
        return new EvidenceCheckResult(EvidenceStatus.NotFound);
    }
}
