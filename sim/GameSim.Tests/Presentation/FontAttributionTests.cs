using System.Reflection;

namespace GameSim.Tests.Presentation;

/// <summary>
/// U10 (asset completion wave, "ship the pixel font"): the display/heading typeface is
/// Silkscreen, an SIL Open Font License 1.1 work. The OFL grants everything this game needs
/// (embed it, redistribute it inside the build) on exactly one condition relevant here: the
/// licence text travels with the font. So — mirroring
/// <see cref="NarratorAttributionTests"/>'s own reasoning for the VCTK-derived narrator voice
/// exactly — that obligation is pinned by a test rather than by memory.
///
/// This is not ceremony. A committed binary asset with no committed licence text is exactly
/// the kind of gap nobody notices until someone asks "can we actually ship this?" — a test is
/// the only thing that notices before that question gets asked the hard way.
///
/// If a future display face is NOT an OFL work, this test SHOULD go red — that is the moment
/// to write the new attribution, not a moment to delete the assertion.
/// </summary>
public class FontAttributionTests
{
    [Fact]
    public void TheSilkscreenFont_AndItsLicence_AreCommitted()
    {
        // Committed, not fetched-on-demand: a build input that only exists because someone's
        // machine happened to download it once is a build nobody else can reproduce.
        var font = Path.Combine(RepoRoot(), "godot", "assets", "fonts", "Silkscreen-Regular.ttf");
        Assert.True(File.Exists(font), $"the Silkscreen display font is missing from {font}");

        var licence = Path.Combine(RepoRoot(), "godot", "assets", "fonts", "Silkscreen-OFL.txt");
        Assert.True(File.Exists(licence), $"the Silkscreen licence text is missing from {licence}");
        Assert.Contains("SIL OPEN FONT LICENSE", File.ReadAllText(licence));
    }

    [Fact]
    public void TheCreditLine_IsInTheReadme()
    {
        // Substrings, not one exact multi-line block: the credit sentence word-wraps across two
        // markdown source lines at this repo's line-length convention, and pinning the exact
        // wrap point would make this test brittle to a purely cosmetic reflow. Each substring
        // below still only appears together in the one credit paragraph this test means to pin.
        var readme = File.ReadAllText(Path.Combine(RepoRoot(), "README.md"));
        Assert.Contains("Silkscreen by The Silkscreen Project Authors", readme);
        Assert.Contains("SIL Open Font", readme);
        Assert.Contains("License 1.1", readme);
        Assert.Contains("Silkscreen-Regular.ttf", readme);
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
