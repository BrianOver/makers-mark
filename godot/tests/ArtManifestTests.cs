#if GDUNIT_TESTS
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// The art manifest must agree with the PNGs actually on disk.
///
/// <para><b>Why this exists.</b> <c>IconRegistry.Art</c> resolves an id by consulting
/// <c>art-manifest.json</c> first and returns null on a miss — and every caller is deliberately
/// null-tolerant, falling back to a placeholder or mounting nothing at all. That is the right
/// behaviour for a fresh checkout, and it also means a STALE MANIFEST IS COMPLETELY SILENT: the art is
/// committed, the code asks for it, the manifest does not list it, and the game renders the fallback
/// forever without one warning.</para>
///
/// <para>Which is exactly what had happened (found 2026-07-30). Five committed PNGs were missing from
/// the manifest: the four panel banners — whose task was closed as done, while none of them had ever
/// appeared on screen — and <c>town2d-ground-atlas</c>, so the whole town was drawing flat
/// two-colour procedural ground instead of its three textured grass variants and stone. The pipeline
/// even ships a drift guard for this (<c>art/pipeline/gen-manifest.ps1 --check</c>) and nothing ever
/// ran it.</para>
///
/// <para>So the guard lives here instead, where the suite runs it on every push. <c>--check</c> stays as
/// the local fast path, and this is the backstop.</para>
/// </summary>
[TestSuite]
// REQUIRED, not optional: DirAccess/FileAccess only resolve res:// paths inside a live Godot runtime.
// Without this the suite does not merely fail — it ABORTS THE WHOLE TEST RUN, which is the same
// silent-suite-loss failure mode as the SubViewport frame-pump hang. Found the hard way here.
[RequireGodotRuntime]
public class ArtManifestTests
{
    private const string ArtDir = "res://assets/art";

    /// <summary>Every committed PNG's asset id, derived exactly as the generator derives it: strip the
    /// <c>.png</c>, then strip a trailing <c>_n</c> normal-map suffix (a normal map is a PROPERTY of an
    /// id, not an id of its own).</summary>
    private static HashSet<string> IdsOnDisk()
    {
        var ids = new HashSet<string>();
        using var dir = DirAccess.Open(ArtDir);
        AssertThat(dir).OverrideFailureMessage($"Cannot open {ArtDir}.").IsNotNull();

        foreach (var file in dir!.GetFiles())
        {
            // A .png may appear as .png.import in an exported/imported tree — take the real name.
            var name = file.EndsWith(".import") ? file[..^".import".Length] : file;
            if (!name.EndsWith(".png"))
            {
                continue;
            }

            var id = name[..^".png".Length];
            if (id.EndsWith("_n"))
            {
                id = id[..^2];
            }

            ids.Add(id);
        }

        return ids;
    }

    private static HashSet<string> IdsInManifest()
    {
        // Godot.FileAccess, explicitly — System.IO.FileAccess is also in scope here.
        using var file = Godot.FileAccess.Open($"{ArtDir}/art-manifest.json", Godot.FileAccess.ModeFlags.Read);
        AssertThat(file).OverrideFailureMessage("art-manifest.json is missing entirely.").IsNotNull();

        var json = Json.ParseString(file!.GetAsText());
        var dict = json.AsGodotDictionary();
        return dict.Keys.Select(k => k.AsString()).ToHashSet();
    }

    [TestCase]
    public void EveryCommittedPng_IsListedInTheManifest()
    {
        var disk = IdsOnDisk();
        var manifest = IdsInManifest();

        var unlisted = disk.Except(manifest).OrderBy(s => s).ToList();

        AssertThat(unlisted.Count)
            .OverrideFailureMessage(
                $"{unlisted.Count} committed art file(s) are absent from art-manifest.json, so " +
                $"IconRegistry.Art returns null for them and the game silently renders a fallback " +
                $"instead — invisible art that looks exactly like art that was never made:\n  " +
                string.Join("\n  ", unlisted) +
                "\nFix: pwsh art/pipeline/gen-manifest.ps1  (then commit the diff).")
            .IsEqual(0);
    }

    [TestCase]
    public void TheManifestListsNothingThatIsNotOnDisk()
    {
        var disk = IdsOnDisk();
        var manifest = IdsInManifest();

        var phantom = manifest.Except(disk).OrderBy(s => s).ToList();

        AssertThat(phantom.Count)
            .OverrideFailureMessage(
                $"art-manifest.json lists {phantom.Count} id(s) with no PNG on disk. Callers will try to " +
                $"load them and get nothing, which is a harder failure than a clean miss:\n  " +
                string.Join("\n  ", phantom) +
                "\nFix: pwsh art/pipeline/gen-manifest.ps1  (then commit the diff).")
            .IsEqual(0);
    }

    /// <summary>
    /// The ids the client asks for BY NAME, spelled out here so a typo or a rename is caught as a
    /// failure rather than as a permanently invisible asset. Deliberately literal: reading them back out
    /// of the source would just reproduce whatever the source says, including its mistakes.
    /// </summary>
    [TestCase]
    public void TheHardCodedArtIdsTheClientAsksFor_AllResolve()
    {
        var manifest = IdsInManifest();
        var required = new[]
        {
            "panel_banner_bounties", "panel_banner_heroes", "panel_banner_shop", "panel_banner_tavern",
            "town2d-ground-atlas",
            "player_smith", "forge", "market", "tavern", "noticeboard",
        };

        var missing = required.Where(id => !manifest.Contains(id)).ToList();

        AssertThat(missing.Count)
            .OverrideFailureMessage(
                "The client references these art ids by name and the manifest does not have them, so each " +
                $"renders as a fallback with no error anywhere:\n  {string.Join("\n  ", missing)}")
            .IsEqual(0);
    }
}
#endif
