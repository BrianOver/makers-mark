using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace GameSim.Tests.Presentation;

/// <summary>
/// The four interior room shells are painted plates now (register #146, PRs #587/#588). Before that
/// they were rendered by <c>art/pipeline/gen-*-interior.py</c> under the town2d idiom of six to eight
/// colours per asset — correct for a 20x36 sprite, and the measured cause of shells carrying
/// 0.020-0.033 bytes/px against 1.688 for the forge exterior.
///
/// <para><b>The trap this closes.</b> Those scripts kept a <c>render_shell</c> entry after the plates
/// shipped, and each one carries a <c>--check</c> drift guard. So the repo held four scripts that
/// would either overwrite the shipped plate on a plain run, or report drift against art that is
/// deliberately no longer their output — and nothing anywhere would have said so, because no CI job
/// runs them. A generator that still claims an asset it no longer owns is a loaded gun pointed at the
/// next person who runs the pipeline.</para>
///
/// <para>The station props stay generated: they are sprite-scale, and the idiom still holds there.
/// This test draws the line at the shells only.</para>
/// </summary>
public class PaintedShellOwnershipTests
{
    /// <summary>A SPRITES-dict row, i.e. a script declaring it renders that id — not a mention of the
    /// id in a comment, which is how these scripts correctly explain why they no longer own it.</summary>
    private static readonly Regex ShellSpriteRow =
        new(@"^\s*""[a-z0-9-]*-interior-shell""\s*:", RegexOptions.Multiline | RegexOptions.Compiled);

    [Fact]
    public void NoPipelineScript_StillClaimsToRenderAPaintedInteriorShell()
    {
        var pipeline = Path.Combine(RepoRoot(), "art", "pipeline");

        var claimants = Directory
            .EnumerateFiles(pipeline, "*.py", SearchOption.TopDirectoryOnly)
            .Where(py => ShellSpriteRow.IsMatch(File.ReadAllText(py)))
            .Select(py => Path.GetFileName(py))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            claimants.Count == 0,
            $"{claimants.Count} pipeline script(s) still declare an *-interior-shell in their SPRITES "
            + "table. Those shells are painted plates now (register #146) — running the script, or its "
            + "own --check drift guard, would revert the shipped art or report false drift. Drop the "
            + "row and its render_shell function; keep the station props:\n  "
            + string.Join("\n  ", claimants));
    }

    /// <summary>The plates themselves, measured — the same number the register argued from, so a
    /// future pass that quietly re-flattens a room fails here rather than in someone's eyes. The floor
    /// is deliberately far below what shipped (1.331-1.471 bytes/px) and far above the 0.020-0.033 the
    /// generated shells carried: this catches a reversion, not a re-crop or a re-encode.</summary>
    [Fact]
    public void EveryPaintedShell_CarriesRealPixelInformation()
    {
        var art = Path.Combine(RepoRoot(), "godot", "assets", "art");
        const double floor = 0.30;

        var thin = Directory
            .EnumerateFiles(art, "*-interior-shell.png", SearchOption.TopDirectoryOnly)
            .Select(png => (Name: Path.GetFileName(png), Density: BytesPerPixel(png)))
            .Where(s => s.Density < floor)
            .OrderBy(s => s.Name, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            thin.Count == 0,
            $"{thin.Count} interior shell(s) fell below {floor:F2} bytes/px, the density band that told "
            + "register #146 apart from art the owner had approved:\n  "
            + string.Join("\n  ", thin.Select(s => $"{s.Name} at {s.Density:F3} bytes/px")));
    }

    /// <summary>PNG dimensions straight from the IHDR, so this needs no image library in the fast
    /// lane. Bytes-per-pixel is a proxy for how much detail survived compression — crude, but it is
    /// the exact measure #146 was diagnosed and fixed against.</summary>
    private static double BytesPerPixel(string png)
    {
        using var stream = File.OpenRead(png);
        var header = new byte[24];
        if (stream.Read(header, 0, 24) < 24)
        {
            return 0;
        }

        var width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
        var height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
        return width > 0 && height > 0 ? new FileInfo(png).Length / (double)(width * height) : 0;
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
