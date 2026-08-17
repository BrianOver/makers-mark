using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace GameSim.Tests.Presentation;

/// <summary>
/// Godot writes a <c>.cs.uid</c> sidecar beside every C# script it imports, and that file is how a
/// <c>.tscn</c> refers to the script by identity rather than by path. The uid inside it is generated
/// on first import, so a sidecar that is not committed is regenerated — with a DIFFERENT value — in
/// every fresh checkout and on every CI runner. Godot then rewrites whatever scene referenced the old
/// value, which is the same class of silent scene churn <c>CLAUDE.md</c>'s engine pin exists to
/// prevent.
///
/// <para><b>Why this test exists, measured rather than imagined:</b> the repo shipped 241 tracked
/// <c>.cs</c> files under <c>godot/</c> against only 230 tracked sidecars. Twelve were missing, added
/// piecemeal by PRs #534, #535, #537, #542, #545, #547 and #548 — every recent PR that introduced a
/// new script forgot the sidecar, because the author never ran a Godot import before committing and
/// nothing anywhere asked.</para>
///
/// <para><b>The existing guard covered the opposite direction only.</b>
/// <c>ShopPanelTests.NoUidSidecar_ExistsWithoutItsMatchingCsFile</c> (engine suite) catches an ORPHAN
/// sidecar — a <c>.uid</c> whose <c>.cs</c> is gone. It cannot see a MISSING one, so the twelve sat
/// under a green build indefinitely. Half a bidirectional invariant reads as the whole invariant,
/// which is exactly how it went unnoticed.</para>
///
/// <para>This lives in the fast lane, not the engine suite, deliberately: it needs no Godot runtime,
/// so it fires on every push even on a day nobody runs gdUnit — and the miss it catches is committed
/// by someone who, by definition, did not run Godot.</para>
/// </summary>
public class GodotUidSidecarCensusTests
{
    [Fact]
    public void EveryGodotCsFile_HasItsUidSidecarOnDisk()
    {
        var godot = Path.Combine(RepoRoot(), "godot");

        var missing = ScriptFiles(godot)
            .Where(cs => !File.Exists(cs + ".uid"))
            .Select(cs => Path.GetRelativePath(godot, cs).Replace('\\', '/'))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"{missing.Count} C# script(s) under godot/ have no .cs.uid sidecar. Godot regenerates a "
            + "DIFFERENT uid for each of these in every fresh checkout, and rewrites any scene that "
            + "referenced the old one. Open the project in Godot 4.6.3 once to let it import, then "
            + "commit the generated sidecars alongside the script:\n  "
            + string.Join("\n  ", missing));
    }

    /// <summary>The inverse, mirrored here from the engine suite on purpose. The orphan direction is
    /// already covered by <c>ShopPanelTests.NoUidSidecar_ExistsWithoutItsMatchingCsFile</c>, but that
    /// test needs the Godot runtime, and an orphan sidecar is most often created by the same act that
    /// creates a missing one — deleting or renaming a script. Keeping both halves in one fast-lane
    /// place means a rename cannot satisfy one direction while breaking the other.</summary>
    [Fact]
    public void NoUidSidecar_SurvivesWithoutItsScript()
    {
        var godot = Path.Combine(RepoRoot(), "godot");

        var orphans = Directory
            .EnumerateFiles(godot, "*.cs.uid", SearchOption.AllDirectories)
            .Where(uid => !IsGenerated(uid))
            .Where(uid => !File.Exists(uid[..^".uid".Length]))
            .Select(uid => Path.GetRelativePath(godot, uid).Replace('\\', '/'))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            orphans.Count == 0,
            $"{orphans.Count} .cs.uid sidecar(s) under godot/ no longer have a matching .cs file — "
            + "delete the sidecar in the same commit that deleted or renamed the script:\n  "
            + string.Join("\n  ", orphans));
    }

    private static IEnumerable<string> ScriptFiles(string godot) => Directory
        .EnumerateFiles(godot, "*.cs", SearchOption.AllDirectories)
        .Where(cs => !IsGenerated(cs));

    /// <summary><c>obj/</c> and <c>bin/</c> hold generated and copied C# that Godot never imports and
    /// that no scene can reference, and <c>.godot/</c> is the editor's own import cache. Scanning them
    /// would report hundreds of "missing" sidecars for files that must not have one.</summary>
    private static bool IsGenerated(string path)
    {
        var p = path.Replace('\\', '/');
        return p.Contains("/obj/", StringComparison.Ordinal)
            || p.Contains("/bin/", StringComparison.Ordinal)
            || p.Contains("/.godot/", StringComparison.Ordinal);
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
