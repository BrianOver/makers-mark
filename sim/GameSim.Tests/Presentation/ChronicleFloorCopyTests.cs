using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using GameSim;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Harness;
using Xunit;

namespace GameSim.Tests.Presentation;

/// <summary>
/// Register #166's family, closed by census rather than by report. `Hero.DeepestFloorReached == 0`
/// is a legitimate sim value meaning "never delved" — every hero carries it on day 1 — so rendering
/// it verbatim fabricates a floor that does not exist. <see cref="DepthCopy"/> exists to be the one
/// place that turns the raw int into prose, and its own doc claimed "every surface" routed through
/// it.
///
/// <para><b>That claim was false when it was written.</b> <c>LedgerQuery</c> was fixed, and the CLI
/// was fixed, but two Godot readers still printed the bare int: the campaign's closing chronicle
/// ("The deepest floor reached: 0" — the last thing a player reads) and <c>MainUi</c>'s
/// <c>ClimaxReached</c> line. The chronicle is now fixed. The <c>ClimaxReached</c> line is a single
/// deliberate exemption, and this file is what makes that exemption honest instead of an oversight:
/// it pins the exemption set EXACTLY, and it pins the emit-side guarantee the exemption rests on.</para>
///
/// <para>Both directions matter. Adding a new raw-int reader fails <see
/// cref="EveryGodotReaderOfADeepestFloor_RoutesThroughDepthCopy_ExceptTheOnePinnedExemption"/>;
/// fixing the exemption without removing its row fails the same test; and if the sim ever emitted a
/// climax at floor 0 the exemption's justification would evaporate, which <see
/// cref="ClimaxReached_NeverCarriesAFloorThatDoesNotExist"/> catches on the sim side.</para>
/// </summary>
public class ChronicleFloorCopyTests
{
    /// <summary>
    /// A raw interpolation of a deepest-floor int into player-facing prose — the shape #166 was
    /// about. Matches <c>{e.DeepestFloorReached}</c> and a format spec after it; deliberately NOT a
    /// bare property read (assigning it, comparing it, or handing it to <see cref="DepthCopy.Deepest"/>
    /// are all fine — only turning it into text unaided is the defect).
    ///
    /// <para><b>Arithmetic is excluded on purpose, and a first draft of this test got it wrong.</b> A
    /// broader pattern flagged <c>DemandPanel</c> and <c>RaidForecastBoard</c>, which both render
    /// <c>{stall.DeepestFloorReached + 1}</c> — the NEXT floor, the one a stalled hero is trying to
    /// reach. That expression is correct by construction: at a deepest floor of 0 it prints "floor 1",
    /// which is a floor that exists. The CLI settled the same question the same way (its third site was
    /// deliberately left literal for exactly this reason). Exempting those two files by name would have
    /// been two rows saying one thing, and would have gone on silently covering a real defect if either
    /// file later printed the value unadorned.</para>
    /// </summary>
    private static readonly Regex RawFloorInProse =
        new(@"\{[A-Za-z_][A-Za-z0-9_.]*\.DeepestFloorReached\s*(?::[^}]*)?\}", RegexOptions.Compiled);

    /// <summary>The exact set of Godot sources allowed to interpolate a deepest floor directly, each
    /// with the reason it is exempt. A row here is a claim that will be read by the next session, so
    /// it says WHY rather than merely THAT.</summary>
    private static readonly Dictionary<string, string> Exemptions = new()
    {
        ["MainUi.cs"] =
            "The ClimaxReached line. ArcDirectorSystem emits that event only once `maxRank >= "
            + "ClimaxRank` — the terminal venue's own bottom floor has fallen — so the value can "
            + "never be the 0 this family is about, and DepthCopy would render the impossible case "
            + "as \"Your heroes have reached not yet\", which is worse prose than the raw int in "
            + "every case. ClimaxReached_NeverCarriesAFloorThatDoesNotExist pins the guarantee.",
    };

    [Fact]
    public void EveryGodotReaderOfADeepestFloor_RoutesThroughDepthCopy_ExceptTheOnePinnedExemption()
    {
        var scripts = Path.Combine(RepoRoot(), "godot", "scripts");

        var offenders = Directory
            .EnumerateFiles(scripts, "*.cs", SearchOption.AllDirectories)
            .Select(path => (Name: Path.GetFileName(path), Text: File.ReadAllText(path)))
            .Where(f => RawFloorInProse.IsMatch(f.Text))
            .Select(f => f.Name)
            .Distinct()
            .OrderBy(n => n, System.StringComparer.Ordinal)
            .ToList();

        var unexpected = offenders.Where(n => !Exemptions.ContainsKey(n)).ToList();
        Assert.True(
            unexpected.Count == 0,
            "These Godot sources interpolate a deepest-floor int straight into player-facing prose, "
            + "which renders \"floor 0\" for any hero who never delved (register #166). Route them "
            + "through DepthCopy.Deepest, or add a row to Exemptions saying why the value cannot be "
            + "0 there:\n  " + string.Join("\n  ", unexpected));

        // The other direction: an exemption that no longer describes anything is a stale claim, and
        // a stale claim in a pinned set is exactly the thing that makes the next census untrustworthy.
        var stale = Exemptions.Keys.Where(n => !offenders.Contains(n)).OrderBy(n => n).ToList();
        Assert.True(
            stale.Count == 0,
            "These files carry an Exemptions row but no longer interpolate a raw deepest floor. The "
            + "fix landed; delete the row in the same change:\n  " + string.Join("\n  ", stale));
    }

    /// <summary>The chronicle is the campaign's closing scroll — the last thing the player reads —
    /// and it renders zeroes on purpose. So the never-delved case has to read as prose, not as an
    /// integer.</summary>
    [Fact]
    public void ANeverDelvedCampaign_ReadsAsNotYet_NeverAsFloorZero()
    {
        Assert.Equal("not yet", DepthCopy.Deepest(0));
        Assert.Equal("not yet", DepthCopy.Deepest(-1));
        Assert.Equal("floor 1", DepthCopy.Deepest(1));
    }

    /// <summary>
    /// The emit-side guarantee the <c>MainUi</c> exemption rests on, asserted through a real
    /// campaign rather than argued from the threshold constant — the constant could change, and a
    /// synthetic fixture can trivially set rank and depth independently (rank at
    /// <c>ClimaxRank</c> with depth 0), which would "prove" the opposite of what real play does. So
    /// this drives the actual composition on the main seed, where the climax measures around day 26.
    ///
    /// <para><c>Category=Balance</c> because it is a multi-day run, same as every other test that
    /// needs the arc to actually move. The two cheap assertions in this file stay in the fast lane.</para>
    /// </summary>
    [Fact]
    [Trait("Category", "Balance")]
    public void ClimaxReached_NeverCarriesAFloorThatDoesNotExist()
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(2026);
        var seen = new List<int>();

        for (var tick = 0; tick < 60 * 5; tick++) // 5-phase day; the main seed climaxes ~day 26
        {
            var result = kernel.Tick(state, BaselinePlayer.ActionsFor(state));
            state = result.NewState;
            seen.AddRange(result.Events.OfType<ClimaxReached>().Select(c => c.DeepestFloorReached));
        }

        Assert.NotEmpty(seen); // fixture guard: a run that never climaxes proves nothing below

        Assert.All(
            seen,
            floor => Assert.True(
                floor > 0,
                $"ClimaxReached carried floor {floor}. MainUi prints that value raw on the strength "
                + "of this guarantee — if a climax can fire at floor 0 the exemption is wrong and "
                + "that line has to route through DepthCopy after all."));
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
