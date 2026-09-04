#if GDUNIT_TESTS
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// P2-HONEST-01: "the Progress book can be opened, and the harness uses the door" — the
/// creation-site census half of the unit. <c>FullPlaytest</c> used to call
/// <c>ui.OpenPanel("Progress")</c> directly on its final-day capture (and again inside its bare
/// <c>AllPanels</c> loop), reaching the gated surface through the panel router rather than the
/// player's own "OpenProgress" tray button — so the harness reported the surface as covered
/// through a hole in the wall, exactly while its real unlock predicate (<c>Bounty.Paid</c>) could
/// never once be true. Both direct calls were deleted in the same PR that fixed the predicate
/// (<see cref="GodotClient.Ui.TutorialFlow.SecondProfessionMilestoneReached"/>).
///
/// <para><b>Phrased against the PROPERTY, not a hand-listed set</b> (this repo's own standing
/// lesson: a guard iterating a literal array stops covering the family the moment someone adds
/// one): every literal <c>OpenPanel("Progress")</c> call site anywhere under <c>res://scripts</c>
/// is found by a source scan, exactly <see cref="FireOnOpenRetiredTests"/>'s own idiom (same
/// <c>res://scripts</c> concatenation, same fabricated-source negative-path proof that the
/// detector isn't vacuous). Only ONE is allowed to exist — the one <c>MainUi</c> itself wires
/// behind <c>OpenGatedSurface("Progress", ...)</c>, the real gate check every OTHER caller (a
/// hotkey, a future tool, a bypass) must route through instead of the bare router.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class OpenProgressBypassCensusTests
{
    /// <summary>Same fixture as <see cref="FireOnOpenRetiredTests"/>'s own reader — a broken
    /// <see cref="ProjectSettings.GlobalizePath"/> would silently scan zero files and make the
    /// count-of-one check below pass by finding nothing to contradict it.</summary>
    private static readonly Lazy<string> AllGodotScriptSource = new(ReadAllGodotScriptSource);

    private static string ReadAllGodotScriptSource()
    {
        var scriptsDir = ProjectSettings.GlobalizePath("res://scripts");
        var files = Directory.GetFiles(scriptsDir, "*.cs", SearchOption.AllDirectories);

        if (files.Length < 100)
        {
            throw new InvalidOperationException(
                $"Only found {files.Length} .cs files under {scriptsDir} -- too few to trust a source " +
                "scan against. GlobalizePath is resolving somewhere unexpected, not this floor.");
        }

        return string.Join("\n---FILE---\n", files.Select(f => $"---{f}---\n{File.ReadAllText(f)}"));
    }

    private static readonly Regex OpenPanelProgressCall = new(@"OpenPanel\(\s*""Progress""\s*\)", RegexOptions.Compiled);

    /// <summary>The one legitimate call site: <c>MainUi</c>'s own "OpenProgress" tray button,
    /// wired through the gate check (<c>progressButton.Pressed += () =>
    /// OpenGatedSurface("Progress", () => OpenPanel("Progress"));</c>). Matched by proximity
    /// (the same line) rather than a full parser, mirroring the pragmatic regex idiom every other
    /// census file in this suite already uses for C# source.</summary>
    private static readonly Regex LegitimateGatedCall =
        new(@"OpenGatedSurface\(\s*""Progress""\s*,[^;]*OpenPanel\(\s*""Progress""\s*\)", RegexOptions.Compiled);

    [TestCase]
    public void OpenPanelProgress_HasExactlyOneCallSite_TheOneGatedByOpenGatedSurface()
    {
        var source = AllGodotScriptSource.Value;
        var totalCalls = OpenPanelProgressCall.Matches(source).Count;
        var gatedCalls = LegitimateGatedCall.Matches(source).Count;

        AssertThat(totalCalls)
            .OverrideFailureMessage(
                $"Found {totalCalls} OpenPanel(\"Progress\") call site(s) under res://scripts -- expected " +
                "exactly 1 (MainUi's own OpenGatedSurface-wrapped tray button). A second bypass has crept " +
                "back in: the harness (or some other caller) is reaching the gated surface through the " +
                "bare router again instead of the player's own door.")
            .IsEqual(1);

        AssertThat(gatedCalls)
            .OverrideFailureMessage(
                "The one OpenPanel(\"Progress\") call site is no longer wrapped in " +
                "OpenGatedSurface(\"Progress\", ...) -- the gate check itself was removed or renamed.")
            .IsEqual(1);
    }

    // ============================================================================================
    // Negative-path proof: the detector actually fails on a planted second call site, using a
    // fabricated source string rather than the real (already-fixed) shipped code (the
    // ComputeCoverageProblems_Fails* / FireOnOpenRetiredTests precedent).
    // ============================================================================================

    [TestCase]
    public void Scan_CatchesAPlantedSecondBypass()
    {
        const string fabricated =
            "progressButton.Pressed += () => OpenGatedSurface(\"Progress\", () => OpenPanel(\"Progress\"));\n" +
            "// somewhere else entirely, a tool reaching straight past the gate:\n" +
            "ui.OpenPanel(\"Progress\");\n";

        AssertThat(OpenPanelProgressCall.Matches(fabricated).Count).IsEqual(2);
        AssertThat(LegitimateGatedCall.Matches(fabricated).Count).IsEqual(1);
    }

    [TestCase]
    public void Scan_PassesForExactlyTheOneGatedCallSite()
    {
        const string fabricated =
            "progressButton.Pressed += () => OpenGatedSurface(\"Progress\", () => OpenPanel(\"Progress\"));\n";

        AssertThat(OpenPanelProgressCall.Matches(fabricated).Count).IsEqual(1);
        AssertThat(LegitimateGatedCall.Matches(fabricated).Count).IsEqual(1);
    }
}
#endif
