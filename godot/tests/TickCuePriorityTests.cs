#if GDUNIT_TESTS
using System;
using System.Linq;
using GdUnit4;
using GodotClient.Audio;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// P2-SCREEN-16 (§11.15, P2-KTD11): <see cref="TickCuePriority.Resolve"/> pinned pair-by-pair — the
/// same methodology <see cref="SurfaceArbiterTests"/> already uses for <see
/// cref="GodotClient.Ui.SurfaceArbiter.Resolve"/>, plus the reflective census the unit body's own test
/// scenarios ask for. No <c>[RequireGodotRuntime]</c>: <see cref="TickCueDeclaration"/> is a plain C#
/// record and nothing below touches a Godot API.
/// </summary>
[TestSuite]
public class TickCuePriorityTests
{
    private static readonly TickCueDeclaration Refusal =
        TickCuePriority.Declarations.Single(d => d.Kind == TickOutcomeKind.Refusal);

    private static readonly TickCueDeclaration Departure =
        TickCuePriority.Declarations.Single(d => d.Kind == TickOutcomeKind.Departure);

    private static readonly TickCueDeclaration DayBell =
        TickCuePriority.Declarations.Single(d => d.Kind == TickOutcomeKind.DayBell);

    [TestCase]
    public void NoCandidates_ResolvesToNull()
    {
        var result = TickCuePriority.Resolve(Array.Empty<TickOutcomeKind>());
        AssertThat(result).IsNull();
    }

    [TestCase]
    public void DayBellAlone_ResolvesToItself()
    {
        var result = TickCuePriority.Resolve(new[] { TickOutcomeKind.DayBell });
        AssertThat(result).IsEqual(DayBell);
    }

    [TestCase]
    public void DepartureOutranksDayBell()
    {
        var result = TickCuePriority.Resolve(new[] { TickOutcomeKind.DayBell, TickOutcomeKind.Departure });
        AssertThat(result).IsEqual(Departure);
    }

    [TestCase]
    public void DepartureOutranksDayBell_RegardlessOfListOrder()
    {
        var result = TickCuePriority.Resolve(new[] { TickOutcomeKind.Departure, TickOutcomeKind.DayBell });
        AssertThat(result).IsEqual(Departure);
    }

    [TestCase]
    public void RefusalOutranksEveryOtherCandidateAtOnce()
    {
        var result = TickCuePriority.Resolve(
            new[] { TickOutcomeKind.DayBell, TickOutcomeKind.Departure, TickOutcomeKind.Refusal });
        AssertThat(result).IsEqual(Refusal);
    }

    /// <summary>The unit body's own test scenario, named as such: "a tick carrying a refusal and a
    /// ceremony plays the refusal" — never additive, and never order-dependent on how the candidate
    /// set was built.</summary>
    [TestCase]
    public void ATickCarryingARefusalAndADeparture_PlaysTheRefusal()
    {
        var forward = TickCuePriority.Resolve(new[] { TickOutcomeKind.Refusal, TickOutcomeKind.Departure });
        var reverse = TickCuePriority.Resolve(new[] { TickOutcomeKind.Departure, TickOutcomeKind.Refusal });
        AssertThat(forward).IsEqual(Refusal);
        AssertThat(reverse).IsEqual(Refusal);
    }

    /// <summary>Deny-by-default census, reflective over <see cref="TickCuePriority.Declarations"/> —
    /// never a hand-listed pair table here, per this unit's own approach note. Every declared
    /// outcome's cue must be a real, defined member of the shipped <see cref="Cue"/> library.</summary>
    [TestCase]
    public void EveryDeclaration_ResolvesToACueDefinedInTheShippedLibrary()
    {
        var undefined = TickCuePriority.Declarations
            .Where(d => !Enum.IsDefined(typeof(Cue), d.CueId))
            .Select(d => d.Kind.ToString())
            .ToList();

        AssertInt(undefined.Count)
            .OverrideFailureMessage(
                "Declared with a Cue id that is not a member of the shipped library: "
                + string.Join(", ", undefined))
            .IsEqual(0);
    }

    /// <summary>"Worst news first" is a TOTAL order — two declarations sharing a rank would make the
    /// winner depend on <see cref="TickCuePriority.Declarations"/>' own array position again, exactly
    /// the hand-written-cascade defect this unit retires.</summary>
    [TestCase]
    public void EveryDeclaration_HasAUniqueRank()
    {
        var collisions = TickCuePriority.Declarations
            .GroupBy(d => d.Rank)
            .Where(g => g.Count() > 1)
            .Select(g => $"rank {g.Key}: {string.Join(" & ", g.Select(d => d.Kind))}")
            .ToList();

        AssertInt(collisions.Count)
            .OverrideFailureMessage("Two declarations share a rank: " + string.Join("; ", collisions))
            .IsEqual(0);
    }

    /// <summary>The census half of "a new event either declares its rank or fails": every <see
    /// cref="TickOutcomeKind"/> member this build knows about must have a matching entry in <see
    /// cref="TickCuePriority.Declarations"/>. A new enum member added with no declaration fails here,
    /// by the member's own name — never a hand-listed pair table that would just quietly not mention
    /// it.</summary>
    [TestCase]
    public void EveryTickOutcomeKind_HasADeclaration()
    {
        var declaredKinds = TickCuePriority.Declarations.Select(d => d.Kind).ToHashSet();
        var missing = Enum.GetValues<TickOutcomeKind>()
            .Where(kind => !declaredKinds.Contains(kind))
            .Select(kind => kind.ToString())
            .ToList();

        AssertInt(missing.Count)
            .OverrideFailureMessage(
                "TickOutcomeKind member(s) with no TickCuePriority.Declarations entry: "
                + string.Join(", ", missing))
            .IsEqual(0);
    }

    /// <summary>P2-SCREEN-16's own structural guarantee, proven by reflection rather than by
    /// convention: <see cref="TickCueDeclaration"/>'s primary constructor (the 3-parameter one — its
    /// OTHER constructor is the compiler-generated record-struct copy constructor, which this test
    /// deliberately skips by picking the one with the most parameters) has NO default value on any
    /// parameter. A future edit giving <c>Rank</c> (or any field) a default would let a new ceremony
    /// omit it and silently inherit a rank/cue it never chose — the exact defect
    /// <see cref="GodotClient.Ui.SurfaceClaim.OwnsScreen"/>'s own "required, no default" already
    /// guards against for the screen arbiter. Fails BY NAME: the message names the parameter that
    /// grew a default.</summary>
    [TestCase]
    public void TickCueDeclaration_RequiresEveryFieldExplicitly_NoDefaults()
    {
        var primaryConstructor = typeof(TickCueDeclaration).GetConstructors()
            .OrderByDescending(c => c.GetParameters().Length)
            .First();

        var withDefaults = primaryConstructor.GetParameters()
            .Where(p => p.HasDefaultValue)
            .Select(p => p.Name)
            .ToList();

        AssertInt(withDefaults.Count)
            .OverrideFailureMessage(
                "TickCueDeclaration parameter(s) with a default value — a ceremony could now omit "
                + "them and silently inherit a rank/cue it never chose: " + string.Join(", ", withDefaults))
            .IsEqual(0);
    }
}
#endif
