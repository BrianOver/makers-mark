using System.Reflection;

namespace GameSim.Tests.Presentation;

/// <summary>
/// The narrator's twenty lines are a derivative of a CC BY 4.0 work — speaker p254 of the CSTR
/// VCTK Corpus. That licence grants everything we need on exactly one condition: credit. So the
/// credit is pinned by a test rather than by memory.
///
/// This is not ceremony. The failure mode is specific and has a shape we have already seen twice
/// in this repo: someone regenerates the library with a different voice, the reference clip and
/// its README line stop matching what ships, and a licence obligation quietly becomes false. A
/// test is the only thing that notices, because nothing about a wrong credit line is visible in
/// the game.
///
/// If a future voice is NOT VCTK-derived, this test SHOULD go red — that is the moment to write
/// the new attribution, not a moment to delete the assertion.
/// </summary>
public class NarratorAttributionTests
{
    private const string CreditLine =
        "Voice derived from the CSTR VCTK Corpus (University of Edinburgh), CC BY 4.0.";

    [Fact]
    public void TheCreditLine_IsInTheReadme_Verbatim()
    {
        var readme = File.ReadAllText(Path.Combine(RepoRoot(), "README.md"));
        Assert.Contains(CreditLine, readme);
    }

    [Fact]
    public void TheReferenceClip_AndItsReasoning_AreCommitted()
    {
        // Committed, not fetched: a build input that lives on one machine is a library nobody
        // else can reproduce, and an unreproducible library is one that gets regenerated wrong.
        var clip = Path.Combine(RepoRoot(), "tools", "narrator", "vctk-p254-reference.flac");
        Assert.True(File.Exists(clip), $"the narrator's reference clip is missing from {clip}");

        var attribution = Path.Combine(RepoRoot(), "tools", "narrator", "ATTRIBUTION.md");
        Assert.True(File.Exists(attribution), $"the attribution note is missing from {attribution}");
        Assert.Contains("CC BY 4.0", File.ReadAllText(attribution));
    }

    [Fact]
    public void TheGenerator_StillBakesFromThatClip()
    {
        // Catches the drift where the credit stays and the tool quietly points somewhere else.
        var tool = File.ReadAllText(Path.Combine(RepoRoot(), "tools", "generate-narrator-lines.py"));
        Assert.Contains("vctk-p254-reference.flac", tool);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Game.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
