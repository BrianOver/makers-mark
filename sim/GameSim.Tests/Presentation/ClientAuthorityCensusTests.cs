using System.Reflection;
using System.Text.RegularExpressions;

namespace GameSim.Tests.Presentation;

// LAW:no-decision-timers
// LAW:show-only-sim-decided

/// <summary>
/// CLAUDE.md rule 12's tripwire for two laws that both reduce to the same property: <b>the client
/// decides nothing.</b> No timer may gate a decision, and no surface may show a number the sim did
/// not produce.
///
/// <para><b>Why it lives in the sim test project.</b> This is a text scan of
/// <c>godot/scripts/**/*.cs</c>; it mounts no scene and needs no Godot runtime. Putting it in the
/// engine suite would buy nothing and cost plenty — that suite serializes, runs slowest, and is the
/// one this repo has twice caught silently running a fraction of its cases. A law's tripwire belongs
/// in the lane that always runs.</para>
///
/// <para><b>THE HONEST FRAMING.</b> This proves the client's source does not MENTION the APIs that
/// would let it decide or invent. It cannot prove a surface shows the right number, and it cannot
/// see a decision smuggled through a value passed in from elsewhere. The real proofs are the
/// <c>PressEnabled</c> spot tests and <c>ActionReachabilityCensusTests</c> in the engine suite. This
/// is the cheap tripwire that catches the honest mistake — a <c>Timer</c> added to a decision panel,
/// a <c>GD.Randf</c> reached for because a number was missing — before anyone has to notice it.</para>
/// </summary>
public class ClientAuthorityCensusTests
{
    /// <summary>
    /// Tokens the client may not use. The client renders and submits; anything here is the client
    /// deciding — inventing a value the sim did not produce, or putting a clock on a choice.
    /// </summary>
    private static readonly (string Name, Regex Pattern, string Law)[] Banned =
    [
        ("client RNG", new Regex(@"\bnew Random\b|\bRandom\.Shared\b|\bGD\.Rand\w*\b"),
            "show only what the sim decided — a client that draws is a client that invents"),
        ("client clock", new Regex(@"\bDateTime\.(Now|UtcNow)\b|\bStopwatch\b"),
            "no timers on decisions"),
    ];

    /// <summary>
    /// The drift ledger. <c>(relative path, token) → reason</c>, and the reason must cite the ruling
    /// that granted it (<c>§11.7.x</c> or <c>P&lt;n&gt;</c>). Cosmetic motion is the expected shape of
    /// a legitimate grant: particles and idle jitter decide nothing about the game.
    /// </summary>
    private static readonly Dictionary<(string File, string Token), string> Exceptions = new()
    {
        [("CampaignSave.cs", "client clock")] =
            "The save file's own timestamp — when this campaign was last written, shown on the "
            + "Continue card. Nothing in the game reads it, and no decision waits on it, so it is "
            + "metadata rather than a timer (§11.7.8: the law is 'no timers on DECISIONS'). Already "
            + "behind an injectable UtcNowSource seam so tests get a deterministic clock.",
        [("NewGameSelect.cs", "client clock")] =
            "Renders a save's timestamp as 'Today' or a date on the load list. Display formatting of "
            + "the timestamp pinned above; no decision is gated on it (§11.7.8).",
    };

    private const int ExpectedExceptionCount = 2;

    [Fact]
    public void ExceptionCount_IsPinned_SoEveryNewGrantIsAVisibleDiff()
        => Assert.True(Exceptions.Count == ExpectedExceptionCount,
            $"Pinned at {ExpectedExceptionCount}; the table now holds {Exceptions.Count}.");

    [Fact]
    public void EveryPinnedException_CitesTheRulingThatGrantedIt()
    {
        var citation = new Regex(@"§11\.7|\bP\d+\b");
        var uncited = Exceptions
            .Where(e => !citation.IsMatch(e.Value))
            .Select(e => $"{e.Key.File} [{e.Key.Token}]")
            .ToList();

        Assert.True(uncited.Count == 0,
            "An exception with no ruling behind it is drift wearing a reason:\n  "
            + string.Join("\n  ", uncited));
    }

    [Fact]
    public void NoClientScript_DecidesAnything_UnlessPinnedWithAReason()
    {
        var files = ClientSourceFiles();

        Assert.True(files.Count >= 60,
            $"Only {files.Count} client scripts were scanned — too few to trust a green run. Check "
            + "the repo-root walk, not this floor.");

        var violations = new List<string>();
        foreach (var (relative, absolute) in files)
        {
            var code = StripComments(File.ReadAllText(absolute));
            foreach (var (name, pattern, law) in Banned)
            {
                if (!pattern.IsMatch(code)) continue;
                if (Exceptions.ContainsKey((relative, name))) continue;
                violations.Add($"{relative} [{name}] — {law}");
            }
        }

        Assert.True(violations.Count == 0,
            "The client took a decision that belongs to the sim (CLAUDE.md rule 12). The fix is to "
            + "move the decision into the sim, not to soften this test:\n  "
            + string.Join("\n  ", violations));
    }

    /// <summary>
    /// A panel that both queues actions and runs a timer is the shape of a timed decision — the law
    /// this repo refuses hardest, because it turns an untimed choice into a reflex test. The forge
    /// minigame is the one sanctioned rhythm, and it earns its place by being a CRAFT, not a choice.
    /// </summary>
    [Fact]
    public void NoDecisionSurface_PutsATimerOnAChoice()
    {
        var timer = new Regex(@"\bCreateTimer\b|\bnew Timer\b|\bTimer\b");
        var queues = new Regex(@"Adapter\.Queue|Adapter\.ApplyNow");

        var suspects = ClientSourceFiles()
            .Select(f => (f.Relative, Code: StripComments(File.ReadAllText(f.Absolute))))
            .Where(f => queues.IsMatch(f.Code) && timer.IsMatch(f.Code))
            .Select(f => f.Relative)
            .Where(f => !TimedCraftSurfaces.Contains(f))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        Assert.True(suspects.Count == 0,
            "A surface that submits player actions also runs a timer. If that timer paces a CRAFT it "
            + "belongs in TimedCraftSurfaces with a reason; if it paces a DECISION it breaks the "
            + "no-timers law outright:\n  " + string.Join("\n  ", suspects));
    }

    /// <summary>
    /// Surfaces where a clock is legitimate because it paces the physical act of making a thing, not
    /// a choice about what to make. The distinction is the law: the forge has rhythm, the decision to
    /// forge does not.
    /// </summary>
    private static readonly HashSet<string> TimedCraftSurfaces = [];

    /// <summary>
    /// LAW:show-only-sim-decided (the half the RNG/clock token scan above cannot see). §11.14.7's
    /// HP-bar breach lived here: <c>MonsterHpFraction -= ExchangeHpStep</c> compiled clean, scanned
    /// clean above (no RNG token, no clock token), and drained every monster by an identical fixed
    /// fraction per beat regardless of its real HP — a drawn quantity no sim field or method
    /// produced. A token scan cannot see an invented NUMBER, only an invented API call. This closes
    /// that gap for the shape the bug actually took: a "how much is left" field (Hp/Health/Progress
    /// in its name — the identifiers a player reads as sim truth) stepped by a constant that is
    /// ITSELF a bare arithmetic literal (no identifier anywhere in its own declared value), never a
    /// value traced to a beat/Contracts member.
    ///
    /// <para>Not a parser — a proximity heuristic, same honesty disclaimer as the class doc above.
    /// It will occasionally flag a legitimate constant that happens to share a statement with an
    /// unrelated Hp-named identifier; the fix is a pinned exception citing a ruling, exactly like
    /// every other grant in this file — never a narrower pattern that stops seeing the next one.
    /// </para>
    /// </summary>
    [Fact]
    public void NoMeterField_IsSteppedByAFabricatedConstant()
    {
        var violations = new List<string>();
        foreach (var (relative, absolute) in ClientSourceFiles())
        {
            var code = StripComments(File.ReadAllText(absolute));
            foreach (var (name, value) in FindFabricatedMeterSteps(code))
            {
                if (FabricatedMeterExceptions.ContainsKey((relative, name)))
                {
                    continue;
                }

                violations.Add(
                    $"{relative} [{name} = {value}] — a bare-literal constant steps a meter-shaped "
                    + "field; that field must be driven by a value read off a beat/Contracts type, "
                    + "not invented here");
            }
        }

        Assert.True(violations.Count == 0,
            "A meter field is stepped by a fabricated constant, not a sim-produced value (CLAUDE.md "
            + "rule 12, show only what the sim decided). Fix by reading the real value; do not "
            + "soften this test:\n  " + string.Join("\n  ", violations));
    }

    /// <summary>
    /// The regression proof: this is the ACTUAL §11.14.7 bug shape (a fixed-const decrement on
    /// <c>MonsterHpFraction</c>, unrelated helper fields around it exactly as the real file had
    /// them), reduced to a standalone string so the check's own correctness never depends on that
    /// bug still existing anywhere in the tree to scan. If this test ever goes green while the
    /// scanning test above stays green too, the detector has been narrowed until it no longer sees
    /// the thing it was built for.
    /// </summary>
    [Fact]
    public void RegressionProof_WouldHaveCaughtTheActualHpBarBreach()
    {
        const string historicalBug = """
            private const float MonsterWidth = 120f;
            private const float ExchangeHpStep = 1f / 3f;
            private float MonsterHpFraction = 1f;

            private void OnExchange(int damageDealt)
            {
                if (damageDealt > 0)
                {
                    MonsterHpFraction = Mathf.Max(0f, MonsterHpFraction - ExchangeHpStep);
                }
            }
            """;

        var found = FindFabricatedMeterSteps(historicalBug);

        Assert.Contains(found, f => f.Name == "ExchangeHpStep");
        // MonsterWidth sits two lines away from the meter field and is never combined with it via
        // +/- -- the unrelated const in the same file that must NOT false-positive.
        Assert.DoesNotContain(found, f => f.Name == "MonsterWidth");
    }

    /// <summary>
    /// A bare-literal constant used to step a meter-shaped field (Hp/Health/Progress in its name)
    /// in the SAME statement — never crossing a newline or a semicolon, which is why <see
    /// cref="NoMeterField_IsSteppedByAFabricatedConstant"/> stopped flagging <c>MonsterWidth</c>/
    /// <c>PipDock.DockWidth</c>/<c>DockHeight</c> (measured false positives from an earlier,
    /// whole-file-proximity version of this check: those constants share a FILE with an Hp-named
    /// field, never a STATEMENT). Not a parser — see the class doc's honesty framing.
    /// </summary>
    private static List<(string Name, string Value)> FindFabricatedMeterSteps(string code)
    {
        var constDecl = new Regex(@"\bconst\s+(?:float|double|int)\s+(\w+)\s*=\s*([^;]+);");
        var pureLiteralValue = new Regex(@"^[\d.\s+\-*/fFdD]+$");
        const string meterToken = @"\b\w*(?:Hp|Health|Progress)\w*\b";

        var found = new List<(string Name, string Value)>();
        foreach (Match decl in constDecl.Matches(code))
        {
            var name = decl.Groups[1].Value;
            var value = decl.Groups[2].Value.Trim();
            if (!pureLiteralValue.IsMatch(value))
            {
                continue; // derived from something else (another const, a computed expression) -- fine
            }

            var stepsAMeter = new Regex(
                meterToken + @"[^\n;]{0,80}?[-+]\s*" + Regex.Escape(name) + @"\b"
                + "|" + Regex.Escape(name) + @"[^\n;]{0,80}?[-+]\s*" + meterToken);
            if (stepsAMeter.IsMatch(code))
            {
                found.Add((name, value));
            }
        }

        return found;
    }

    /// <summary>Pinned exceptions for the check above — same citation contract as <see
    /// cref="Exceptions"/>: every entry names the ruling that grants it.</summary>
    private static readonly Dictionary<(string File, string Const), string> FabricatedMeterExceptions = new();

    private const int ExpectedFabricatedMeterExceptionCount = 0;

    [Fact]
    public void FabricatedMeterExceptionCount_IsPinned_SoEveryNewGrantIsAVisibleDiff()
        => Assert.True(FabricatedMeterExceptions.Count == ExpectedFabricatedMeterExceptionCount,
            $"Pinned at {ExpectedFabricatedMeterExceptionCount}; the table now holds "
            + $"{FabricatedMeterExceptions.Count}.");

    [Fact]
    public void EveryFabricatedMeterException_CitesTheRulingThatGrantedIt()
    {
        var citation = new Regex(@"§11\.7|\bP\d+\b");
        var uncited = FabricatedMeterExceptions
            .Where(e => !citation.IsMatch(e.Value))
            .Select(e => $"{e.Key.File} [{e.Key.Const}]")
            .ToList();

        Assert.True(uncited.Count == 0,
            "An exception with no ruling behind it is drift wearing a reason:\n  "
            + string.Join("\n  ", uncited));
    }

    private static string StripComments(string source)
    {
        source = Regex.Replace(source, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        source = Regex.Replace(source, @"//[^\n]*", " ");
        return source;
    }

    private static List<(string Relative, string Absolute)> ClientSourceFiles()
    {
        var root = ClientRoot();
        return Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(p => (Path.GetRelativePath(root, p).Replace('\\', '/'), p))
            .OrderBy(t => t.Item1, StringComparer.Ordinal)
            .ToList();
    }

    private static string ClientRoot()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Game.sln")))
            dir = dir.Parent;

        Assert.True(dir is not null, "Could not find Game.sln walking up from the test assembly.");
        var scripts = Path.Combine(dir!.FullName, "godot", "scripts");
        Assert.True(Directory.Exists(scripts), $"Expected the client at {scripts}.");
        return scripts;
    }
}
