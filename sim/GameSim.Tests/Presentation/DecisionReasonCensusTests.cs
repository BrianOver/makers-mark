using System.Reflection;
using GameSim.Contracts;

namespace GameSim.Tests.Presentation;

/// <summary>
/// Register #164 ("log every action and its reason", MAKERS-MARK.md §11.14.8, U-T6): the sim already
/// stamps a typed reason onto some <see cref="GameEvent"/> records — <c>Reason</c> on
/// <see cref="HeroPassedOnItem"/>/<see cref="HeroDecisionExplained"/>/<see cref="BountyJudged"/>/
/// <see cref="CustomerWalked"/>, <c>Cause</c> on <see cref="HeroDied"/> — and before this unit not one
/// of those reasons reached the diagnostic session log a human playtest actually produces (they only
/// ever reached a UI panel, which is not something you can grep "later" the way the owner's standing
/// instruction asks for).
///
/// <para><b>Why a text census, not just a behavioral test.</b> <c>godot/scripts/DecisionEvents.cs</c>
/// (the fix) lives in the Godot-referencing client assembly this fast-lane project cannot compile
/// against (KTD3) — the behavioral proof that its rows actually land in a JSONL file is a gdUnit4
/// test in the engine suite instead (<c>DecisionEventsTests</c>), which serializes and does not
/// run on every push. This is the cheap half that DOES run on every push: it reflects over
/// <c>GameSim.Contracts</c> for every event shaped like a reason-carrier and source-scans
/// <c>DecisionEvents.cs</c> for a matching <c>case</c> arm, so a FUTURE event added with a
/// <c>Reason</c>/<c>Cause</c> field and no wiring fails here — deny-by-default, same idiom as
/// <c>ClientAuthorityCensusTests</c> and <c>ActionSubjectCoverageTests</c> (godot/tests) in this
/// program. The behavioral half lives in <c>godot/tests/DecisionEventsTests.cs</c>.</para>
///
/// <para><b>THE HONEST FRAMING.</b> This proves the type name appears in a <c>case</c> arm — it
/// cannot prove the arm actually calls <c>PlaytestLog.Decision</c> with the RIGHT field, only that
/// somebody wrote a case for it at all. That stronger proof is exactly what the gdUnit4 companion
/// test exists for.</para>
/// </summary>
public class DecisionReasonCensusTests
{
    /// <summary>
    /// The field names this census treats as "this event is a reason-carrier and must be wired."
    /// <c>Detail</c> is included because <see cref="AttributionBeatEvent.Detail"/> is the
    /// counterfactual-margin prose the whole game's attribution mechanic is named after (see
    /// PlaytestLog's 2026-08-11 doc note) — the same shape as a Reason field even though it predates
    /// this naming convention. A future field with a genuinely different name (e.g. a hypothetical
    /// persisted <c>DecisionExplained.Rationale</c>) is exactly the kind of drift this list is meant
    /// to catch: widen it rather than special-case the new event past the census.
    /// </summary>
    private static readonly string[] ReasonFieldNames = ["Reason", "Cause", "Detail"];

    [Fact]
    public void EveryReasonBearingEvent_HasAWiredDecisionEventsCase()
    {
        var eventTypes = typeof(GameEvent).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(GameEvent).IsAssignableFrom(t))
            .ToList();

        Assert.True(eventTypes.Count >= 20,
            $"Only {eventTypes.Count} GameEvent types were found — too few to trust a green run. "
            + "Check the assembly lookup, not this floor.");

        var reasonBearing = eventTypes
            .Where(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Any(p => p.PropertyType == typeof(string) && ReasonFieldNames.Contains(p.Name)))
            .Select(t => t.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        // The denominator guard: proves the reflection scan itself still finds the six known
        // reason-carriers this unit wired, rather than silently matching zero types and passing by
        // construction of an empty loop below.
        Assert.True(reasonBearing.Count >= 6,
            $"Only found {reasonBearing.Count} reason-bearing event types ({string.Join(", ", reasonBearing)}) "
            + "— expected at least the 6 known ones (HeroPassedOnItem, HeroDecisionExplained, "
            + "BountyJudged, CustomerWalked, HeroDied, AttributionBeatEvent). The reflection scan is "
            + "not reaching Contracts/Events.cs.");

        var source = ReadDecisionEventsSource();
        var missing = reasonBearing
            .Where(name => !System.Text.RegularExpressions.Regex.IsMatch(source, $@"\bcase\s+{name}\b"))
            .ToList();

        Assert.True(missing.Count == 0,
            "These GameEvent types carry a Reason/Cause/Detail field but godot/scripts/DecisionEvents.cs "
            + "has no `case` wiring it into PlaytestLog.Decision — a reason the sim computed will never "
            + "reach a session log (register #164, MAKERS-MARK.md §11.14.8):\n  "
            + string.Join("\n  ", missing));
    }

    /// <summary>Reads the client file as TEXT (see this class's own doc for why: the fast lane cannot
    /// reference the Godot-dependent assembly that defines it).</summary>
    private static string ReadDecisionEventsSource()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Game.sln")))
        {
            dir = dir.Parent;
        }

        Assert.True(dir is not null, "Could not find Game.sln walking up from the test assembly.");
        var path = Path.Combine(dir!.FullName, "godot", "scripts", "DecisionEvents.cs");
        Assert.True(File.Exists(path), $"Expected the client decision-echo file at {path}.");
        return File.ReadAllText(path);
    }
}
