#if GDUNIT_TESTS
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GameSim.Venues;
using GdUnit4;
using GodotClient;
using GodotClient.Panels;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// A venue backdrop must be authored at the size it is DRAWN at, so
/// <c>MineWatch.RebuildBackdropTiles</c>'s scale comes out (1, 1).
///
/// <para><b>Why this is a contract and not a preference.</b> The four backdrops shipped as 160×160
/// squares against a 1024×260 strip, which is a runtime scale of (6.4, 1.625) — not a resize, an
/// ANISOTROPIC one, stretching each image four times wider than tall. Register #148 is the owner
/// saying the watch "still fucking sucks" and should reach cutscene quality; a backdrop stretched
/// 6.4× is the most immediately visible reason it did not. Everything else §11.14.7 named for that
/// item landed — the real monster HP, the pacing scheduler, the camera hint, the rout-versus-
/// triumph split — and the commit that landed them said the backdrops "belong to a separate art
/// unit that re-authors them at draw size", which then never happened.
///
/// <para><b>This is the third time.</b> A runtime <c>Scale</c> knob standing in for art authored at
/// draw size has shipped twice before (#471 sprites, #487 props). No existing art guard catches it,
/// because a stretched texture is a perfectly valid texture: it resolves, it draws, it has colours,
/// nothing degrades and nothing warns. Only a test comparing the art's own dimensions against the
/// geometry that draws it can — which is this, reading both numbers from <see cref="MineWatch"/>
/// rather than from a copied literal, so a future strip resize moves the contract with it.</para>
///
/// <para><b>Read off the PNG header, not through <c>IconRegistry</c>.</b> The first version of this
/// test resolved each backdrop with <c>AssetCatalog.VenueBackdrop</c> and took the whole test host
/// down: <c>ResourceLoader</c>'s static constructor marshals a <c>StringName</c> through the native
/// layer, and doing that from this suite's context is a hard crash, not an exception. The run then
/// reported <c>Passed! Failed: 0, Passed: 1291</c> — green, with 150+ tests silently never executed,
/// which is a failure shape this repo already knows by heart. Parsing the IHDR needs no Godot
/// runtime at all and cannot take anything down with it.</para>
/// </summary>
[TestSuite]
public class BackdropArtContractTests
{
    [TestCase]
    public void EveryVenueBackdrop_IsAuthoredAtTheSizeTheStripDrawsIt()
    {
        var expectedWidth = (int)MineWatch.BackdropTileWidth;
        var expectedHeight = (int)MineWatch.StripHeight;

        var wrong = new List<string>();
        foreach (var venueId in VenueRegistry.All.Keys)
        {
            var artId = AssetCatalog.VenueBackdropId(venueId);
            var path = Path.Combine(RepoRoot(), "godot", "assets", "art", $"{artId}.png");
            if (!File.Exists(path))
            {
                // A venue whose backdrop has not been drawn yet is not this contract's business —
                // AssetResolutionCensusTests already owns "every id resolves to something".
                continue;
            }

            var (width, height) = PngSize(path);
            if (width == expectedWidth && height == expectedHeight)
            {
                continue;
            }

            var sx = expectedWidth / (float)width;
            var sy = expectedHeight / (float)height;
            wrong.Add($"{artId}.png is {width}x{height}, drawn at {expectedWidth}x{expectedHeight}"
                + $" — runtime scale ({sx:F2}, {sy:F2})");
        }

        wrong.Sort(System.StringComparer.Ordinal);

        AssertThat(string.Join("\n  ", wrong))
            .OverrideFailureMessage(
                $"{wrong.Count} venue backdrop(s) are not authored at the strip's own size, so the "
                + "client stretches them at runtime. Re-author the PNG at "
                + $"{expectedWidth}x{expectedHeight}; never add a Scale knob to compensate — that is "
                + "the exact shape that shipped in #471 and #487:\n  " + string.Join("\n  ", wrong))
            .IsEqual(string.Empty);
    }

    /// <summary>Width and height straight from the PNG's IHDR — the first chunk of every PNG, at a
    /// fixed offset, big-endian.</summary>
    private static (int Width, int Height) PngSize(string path)
    {
        using var stream = File.OpenRead(path);
        var header = new byte[24];
        return stream.Read(header, 0, 24) < 24
            ? (0, 0)
            : ((header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19],
               (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23]);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Game.sln")))
        {
            dir = dir.Parent;
        }

        AssertThat(dir).IsNotNull();
        return dir!.FullName;
    }
}
#endif
