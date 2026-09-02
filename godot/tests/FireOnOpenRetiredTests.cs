#if GDUNIT_TESTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// P2-ONBOARD-02 (§11.15): "fire-on-open dies as a category" — the tripwire half of the unit.
///
/// <para><b>The defect this guards against.</b> A rendered pass found Bryn's <c>MentorBanner</c>
/// covering nearly every first-opened panel. A text census then found the cause was copy POLICY,
/// not layout: four lessons — <c>read-only-surfaces</c>, <c>tomorrow-at-the-counter</c>,
/// <c>forecast-board-taught</c>, <c>legends-wall-taught</c> — fired the instant a surface opened
/// (a panel router branch, a <c>.Opened</c> event, or a <c>VisibilityChanged</c> handler), straight
/// into a centred card. This unit converted all four to once-ever captions rendered INSIDE the
/// panel each describes (<c>UiKit.OnceEverCaption</c>). This file is the "make the category
/// unreachable, not merely unused" half — this repo's own pattern is that a category left
/// merely-unused comes back; a category made red comes back as a reviewed diff.</para>
///
/// <para><b>Two checks, source-scanned exactly like <see cref="TeachingCoverageCensusTests"/></b>
/// (same <c>res://scripts</c> concatenation, same fabricated-source negative-path idiom proving
/// the detector isn't vacuous):</para>
/// <list type="number">
/// <item>None of the four retired ids ever reaches <c>MentorBanner.ShowFirstTouch</c>/<c>Show</c>
/// again — the specific, historical regression.</item>
/// <item>NO method wired to a live <c>.VisibilityChanged +=</c> handler ever calls
/// <c>MentorBanner.ShowFirstTouch</c>/<c>Show</c>, for ANY id — the general, forward-looking rule.
/// A lesson firing because a surface's visibility flipped is fire-on-open BY CONSTRUCTION, whatever
/// id a future unit gives it, so this check needs no id list to stay correct.</item>
/// </list>
///
/// <para><b>Deliberately narrower than "no lesson may fire while opening."</b> A lesson gated on a
/// live GAME-STATE fact discovered once a surface is open (<see
/// cref="GodotClient.Panels.RaidForecastBoard"/>'s own "the-muster-speaks", fired only when a real
/// gear gap exists) is a beat reacting to something the sim decided, not a lesson firing because the
/// surface merely opened — the plan named exactly the four ids above as the fire-ON-OPEN category to
/// retire, and this file checks exactly that, not every call site that happens to run during an open.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class FireOnOpenRetiredTests
{
    private static readonly Lazy<string> AllGodotScriptSource = new(ReadAllGodotScriptSource);

    /// <summary>Same fixture as <see cref="TeachingCoverageCensusTests"/>'s own reader — a broken
    /// <see cref="ProjectSettings.GlobalizePath"/> would silently scan zero files and make every
    /// check below pass by finding nothing to contradict it.</summary>
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

        return string.Join("\n---FILE---\n", files.Select(File.ReadAllText));
    }

    /// <summary>The four lessons P2-ONBOARD-02 converted from a <see
    /// cref="GodotClient.Ui.MentorBanner"/> popup to a once-ever panel-header caption.</summary>
    private static readonly string[] RetiredOpenTimedLessonIds =
    [
        "read-only-surfaces", "tomorrow-at-the-counter", "forecast-board-taught", "legends-wall-taught",
    ];

    /// <summary>True iff <paramref name="source"/> contains a
    /// <c>Mentor.ShowFirstTouch</c>/<c>Mentor?.ShowFirstTouch</c>/<c>Mentor.Show</c>/<c>Mentor?.Show</c>
    /// call whose argument list itself contains a <c>ConsumeFirstTouch(...)</c> call naming
    /// <paramref name="id"/> — the exact shape every one of the four retired lessons used to have
    /// (<c>Mentor.ShowFirstTouch(Tutorial.ConsumeFirstTouch("id", ...))</c>), and the exact shape a
    /// regressed reintroduction would have too.</summary>
    private static bool IdFeedsMentorBanner(string source, string id)
    {
        var pattern = $@"Mentor\??\.(?:ShowFirstTouch|Show)\(\s*[\w.?]*ConsumeFirstTouch\(\s*""{Regex.Escape(id)}""";
        return Regex.IsMatch(source, pattern);
    }

    /// <summary>Every method body directly wired to a <c>.VisibilityChanged +=</c> handler in
    /// <paramref name="source"/>, keyed by the handler's own method name — found by matching the
    /// assignment's target name against a <c>private/public/protected void Name()</c> declaration
    /// and then walking braces to that method's closing one. A handler assigned as a lambda or with
    /// parameters is a different shape from every real call site in this codebase today and is
    /// simply not returned (the denominator guard below proves the query still finds the real ones).</summary>
    private static IReadOnlyList<(string MethodName, string Body)> VisibilityChangedHandlerBodies(string source)
    {
        var results = new List<(string, string)>();
        foreach (Match wireUp in Regex.Matches(source, @"\.VisibilityChanged\s*\+=\s*(\w+)\s*;"))
        {
            var methodName = wireUp.Groups[1].Value;
            var decl = Regex.Match(
                source, $@"(?:private|public|protected)\s+void\s+{Regex.Escape(methodName)}\s*\(\s*\)\s*\{{");
            if (!decl.Success)
            {
                continue;
            }

            var bodyStart = decl.Index + decl.Length;
            var depth = 1;
            var i = bodyStart;
            while (i < source.Length && depth > 0)
            {
                if (source[i] == '{')
                {
                    depth++;
                }
                else if (source[i] == '}')
                {
                    depth--;
                }

                i++;
            }

            results.Add((methodName, source[bodyStart..(i - 1)]));
        }

        return results;
    }

    private static readonly Regex MentorBannerCallInBody = new(@"Mentor\??\.(?:ShowFirstTouch|Show)\(", RegexOptions.Compiled);

    // ============================================================================================
    // The two real checks, run against the actual shipped source.
    // ============================================================================================

    [TestCase]
    public void RetiredOpenTimedLessons_NeverFeedTheMentorBannerAgain()
    {
        var source = AllGodotScriptSource.Value;
        var stillWired = RetiredOpenTimedLessonIds.Where(id => IdFeedsMentorBanner(source, id)).ToList();

        AssertThat(stillWired.Count)
            .OverrideFailureMessage(
                "These fire-on-open lessons are still routed through MentorBanner.ShowFirstTouch/Show " +
                $"-- P2-ONBOARD-02 converted all four to a once-ever panel-header caption instead: {string.Join(", ", stillWired)}")
            .IsEqual(0);
    }

    [TestCase]
    public void NoVisibilityChangedHandler_EverCallsTheMentorBanner()
    {
        var offenders = VisibilityChangedHandlerBodies(AllGodotScriptSource.Value)
            .Where(h => MentorBannerCallInBody.IsMatch(h.Body))
            .Select(h => h.MethodName)
            .ToList();

        AssertThat(offenders.Count)
            .OverrideFailureMessage(
                "A VisibilityChanged handler is firing MentorBanner.ShowFirstTouch/Show -- that is " +
                $"fire-on-open by construction, whatever id it names: {string.Join(", ", offenders)}")
            .IsEqual(0);
    }

    // ============================================================================================
    // Denominator guard: a broken regex would make both checks above pass by finding nothing.
    // ============================================================================================

    [TestCase]
    public void VisibilityChangedWiring_FindsEnoughHandlers_ToTrustAGreenRun()
    {
        var handlers = VisibilityChangedHandlerBodies(AllGodotScriptSource.Value);

        AssertThat(handlers.Count)
            .OverrideFailureMessage(
                $"Only found {handlers.Count} VisibilityChanged handlers -- MainUi.BuildUi wires at " +
                "least nine (Ledger/Forecast/Bestiary/Chronicle/Commissions/Legends/Camp/SystemMenu/" +
                "Mirror). The query is broken, not the census.")
            .IsGreaterEqual(8);
    }

    // ============================================================================================
    // Negative-path proofs: each detector actually fails on a planted violation and passes
    // otherwise, using fabricated source strings rather than the real (already-fixed) shipped code
    // (the ComputeCoverageProblems_Fails* precedent, TeachingCoverageCensusTests).
    // ============================================================================================

    [TestCase]
    public void IdFeedsMentorBanner_CatchesAPlantedViolation()
    {
        const string fabricated = "Mentor.ShowFirstTouch(Tutorial.ConsumeFirstTouch(\"read-only-surfaces\", \"x\"));";

        AssertThat(IdFeedsMentorBanner(fabricated, "read-only-surfaces")).IsTrue();
    }

    [TestCase]
    public void IdFeedsMentorBanner_CatchesAPlantedViolation_ThroughTheNullConditionalForm()
    {
        const string fabricated = "Mentor?.ShowFirstTouch(Tutorial?.ConsumeFirstTouch(\"legends-wall-taught\", \"x\"));";

        AssertThat(IdFeedsMentorBanner(fabricated, "legends-wall-taught")).IsTrue();
    }

    [TestCase]
    public void IdFeedsMentorBanner_PassesWhenTheIdOnlyReachesAHeaderCaption()
    {
        const string fabricated =
            "if (Tutorial.ConsumeFirstTouch(\"read-only-surfaces\", \"x\") is { } caption)\n" +
            "{\n    showCaption(caption);\n}\n";

        AssertThat(IdFeedsMentorBanner(fabricated, "read-only-surfaces")).IsFalse();
    }

    [TestCase]
    public void IdFeedsMentorBanner_PassesWhenAnUnrelatedIdFeedsTheBanner()
    {
        const string fabricated = "Mentor.ShowFirstTouch(Tutorial.ConsumeFirstTouch(\"the-mark-read\", \"x\"));";

        AssertThat(IdFeedsMentorBanner(fabricated, "read-only-surfaces")).IsFalse();
    }

    [TestCase]
    public void VisibilityChangedScan_CatchesAPlantedViolation()
    {
        const string fabricated =
            "Foo.VisibilityChanged += OnFooVisibilityChanged;\n" +
            "private void OnFooVisibilityChanged()\n" +
            "{\n" +
            "    Mentor.ShowFirstTouch(Tutorial.ConsumeFirstTouch(\"brand-new-id\", \"x\"));\n" +
            "}\n";

        var offenders = VisibilityChangedHandlerBodies(fabricated)
            .Where(h => MentorBannerCallInBody.IsMatch(h.Body))
            .ToList();

        AssertThat(offenders.Count).IsEqual(1);
        AssertThat(offenders[0].MethodName).IsEqual("OnFooVisibilityChanged");
    }

    [TestCase]
    public void VisibilityChangedScan_PassesForAHandlerThatOnlyShowsAHeaderCaption()
    {
        const string fabricated =
            "Foo.VisibilityChanged += OnFooVisibilityChanged;\n" +
            "private void OnFooVisibilityChanged()\n" +
            "{\n" +
            "    Foo.ShowHeaderCaption(\"fine\");\n" +
            "}\n";

        var offenders = VisibilityChangedHandlerBodies(fabricated)
            .Where(h => MentorBannerCallInBody.IsMatch(h.Body))
            .ToList();

        AssertThat(offenders.Count).IsEqual(0);
    }

    [TestCase]
    public void VisibilityChangedScan_IgnoresAMentorCallOutsideAnyHandlerBody()
    {
        const string fabricated =
            "Foo.VisibilityChanged += OnFooVisibilityChanged;\n" +
            "private void OnFooVisibilityChanged()\n" +
            "{\n" +
            "    DoSomethingElse();\n" +
            "}\n" +
            "private void SomeOtherMethod()\n" +
            "{\n" +
            "    Mentor.ShowFirstTouch(Tutorial.ConsumeFirstTouch(\"unrelated\", \"x\"));\n" +
            "}\n";

        var offenders = VisibilityChangedHandlerBodies(fabricated)
            .Where(h => MentorBannerCallInBody.IsMatch(h.Body))
            .ToList();

        AssertThat(offenders.Count).IsEqual(0);
    }
}
#endif
