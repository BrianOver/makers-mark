namespace FlavorForge;

/// <summary>
/// Repo-relative path resolution (U5): finds the repo root so the CLI works whether launched via
/// <c>dotnet run --project tools/FlavorForge</c> (cwd = repo root) or as a built exe, and maps a
/// surface name to its real committed pack file. Read-only lookups — this never edits
/// <c>Game.sln</c>, it only checks for its presence as a repo-root marker.
/// </summary>
internal static class RepoLayout
{
    public static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Game.sln")))
            {
                return dir.FullName;
            }
        }

        for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Game.sln")))
            {
                return dir.FullName;
            }
        }

        // Last resort: current working directory (e.g. an unusual launch layout).
        return Directory.GetCurrentDirectory();
    }

    /// <summary>The real committed pack file for a surface — used unless <c>--pack-file</c>
    /// points <c>--emit</c> at a fixture instead (tests must never target this path).</summary>
    public static string PackFilePath(string repoRoot, Generation.SurfaceContract surface) =>
        Path.Combine(repoRoot, surface.RelativePackFilePath.Replace('/', Path.DirectorySeparatorChar));

    public static string DefaultProposalsDirectory(string repoRoot) =>
        Path.Combine(repoRoot, "tools", "FlavorForge", "proposals");
}
