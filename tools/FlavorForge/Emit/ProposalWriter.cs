using System.Text;
using FlavorForge.Generation;

namespace FlavorForge.Emit;

/// <summary>
/// Safe-by-default output (KTD-F): the default run mode writes accepted candidates to a
/// dev-only review artifact under <c>tools/FlavorForge/proposals/</c> — never touching sim
/// source. <c>--emit</c> is the explicit opt-in that hands the same accepted set to
/// <see cref="PackEmitter"/> instead.
/// </summary>
public static class ProposalWriter
{
    /// <summary>
    /// Writes <c>{proposalsDirectory}/{surfaceName}.txt</c> — a header plus every accepted
    /// candidate line, grouped under its full key — and returns the path written. Cells with
    /// zero accepted candidates are omitted from the body (nothing fabricated to fill space).
    /// </summary>
    public static string Write(string proposalsDirectory, string surfaceName, IReadOnlyList<CellResult> results)
    {
        Directory.CreateDirectory(proposalsDirectory);
        var path = Path.Combine(proposalsDirectory, $"{surfaceName}.txt");

        var body = new StringBuilder();
        body.Append("# FlavorForge proposal - surface: ").Append(surfaceName).Append('\n');
        body.Append("# Review only. Not sim source. Re-run with --emit to splice accepted lines into the pack file.\n\n");

        foreach (var result in results)
        {
            if (result.Accepted.Count == 0)
            {
                continue;
            }

            body.Append("## ").Append(result.Key).Append('\n');
            foreach (var line in result.Accepted)
            {
                body.Append(line).Append('\n');
            }

            body.Append('\n');
        }

        File.WriteAllText(path, body.ToString());
        return path;
    }
}
