#if GDUNIT_TESTS
using System;
using System.Linq;
using GameSim;
using GameSim.Contracts;
using GdUnit4;
using GodotClient.Ui;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// U7 (§11.14.14): <see cref="TutorialFlow.ResolveExistence"/> — the mechanism that lets a
/// registry row point at something the sim has not produced yet (a commission card on a day with
/// no commissions, an unshelved item before anything is crafted) without breaking <see
/// cref="TutorialOverlay"/>'s own never-point-at-nothing house rule. Mirrors <see
/// cref="StationAnchorHandoffTests"/>'s own shape: a hand-built row, never routed through <see
/// cref="TutorialFlow.Registry"/>, because no real row declares <see
/// cref="TutorialStepDef.AnchorExists"/> yet (this unit ships the mechanism; a future row is the
/// first real caller — same precedent as <see cref="PanelControlAnchorTests"/> for <see
/// cref="TutorialAnchorKind.PanelControl"/>).
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ConditionalAnchorTests
{
    /// <summary>The "not there yet" target — stands in for a commission card, a shelved item, a
    /// camped party's slate: a real anchor kind, just one the sim has not produced.</summary>
    private static readonly TutorialAnchor Target = TutorialAnchor.ForPanelControl("Bounties", "CommissionRow");

    /// <summary>The declared "way there" fallback — the surface that will eventually contain the
    /// target, exactly the shape the task names ("typically the surface or building that will
    /// contain it").</summary>
    private static readonly TutorialAnchor Fallback = TutorialAnchor.ForBuilding("noticeboard");

    /// <summary>A synthetic row whose AnchorExists is "Day >= 5" — an arbitrary, easily-flipped
    /// stand-in for "the sim produced the entity", so these tests never depend on wiring a real
    /// commission/shelf/camp fixture just to prove the resolution mechanism itself.</summary>
    private static TutorialStepDef ConditionalRow(TutorialAnchor? fallback) =>
        new(
            Step: TutorialStep.Commission, DisplayIndex: 99, Act: TutorialAct.Memory,
            Anchor: Target, MinDay: 1, ShortLabel: "conditional-anchor test row", TeachNote: "conditional-anchor test row",
            IsDone: _ => false, AdvanceFrom: [TutorialStep.Commission], AdvancesTo: null,
            AnchorExists: state => state.Day >= 5, AnchorFallback: fallback);

    private static GameState EntityAbsent() => GameComposition.NewCampaign(ScriptedSession.Seed) with { Day = 1 };

    private static GameState EntityPresent() => GameComposition.NewCampaign(ScriptedSession.Seed) with { Day = 5 };

    [TestCase]
    public void EntityAbsent_ResolvesToItsDeclaredFallback()
    {
        var resolved = TutorialFlow.ResolveExistence(Target, ConditionalRow(Fallback), EntityAbsent());

        AssertThat(resolved)
            .OverrideFailureMessage("AnchorExists read false but the anchor did not resolve to the declared AnchorFallback.")
            .IsEqual(Fallback);
    }

    [TestCase]
    public void EntityPresent_ResolvesToTheRealTarget()
    {
        var resolved = TutorialFlow.ResolveExistence(Target, ConditionalRow(Fallback), EntityPresent());

        AssertThat(resolved)
            .OverrideFailureMessage("AnchorExists read true but the anchor did not resolve to the real target — a fallback must not linger once the entity exists.")
            .IsEqual(Target);
    }

    /// <summary>The house rule (TutorialOverlay's own throw sites) stands even for a conditional
    /// row: declaring AnchorExists without an AnchorFallback must still fail loudly rather than
    /// silently fall through to a target the caller already knows is not there. "Declared, never
    /// inferred" — a missing Fallback is not this method's job to invent one.</summary>
    [TestCase]
    public void EntityAbsent_WithNoDeclaredFallback_StillThrowsWithAUsefulMessage()
    {
        InvalidOperationException? thrown = null;
        try
        {
            TutorialFlow.ResolveExistence(Target, ConditionalRow(fallback: null), EntityAbsent());
        }
        catch (InvalidOperationException ex)
        {
            thrown = ex;
        }

        AssertThat(thrown)
            .OverrideFailureMessage("An absent entity with no declared AnchorFallback resolved instead of throwing — the house rule (never point at nothing) was silently bypassed.")
            .IsNotNull();
        AssertThat(thrown!.Message)
            .OverrideFailureMessage($"Exception message is not useful enough to act on: \"{thrown.Message}\"")
            .Contains("AnchorFallback");
        AssertThat(thrown.Message).Contains(nameof(TutorialStep.Commission));
    }

    /// <summary>The predicate must be read fresh on every call, never memoized off the row or the
    /// anchor — the SAME def/target pair resolves differently as state.Day crosses the predicate's
    /// own threshold, proving there is nothing cached between the two calls.</summary>
    [TestCase]
    public void ThePredicate_IsEvaluatedAtRefresh_SoAMidDayStateChangeMovesThePointer()
    {
        var def = ConditionalRow(Fallback);

        var beforeTheEntityExists = TutorialFlow.ResolveExistence(Target, def, EntityAbsent());
        var afterTheEntityExists = TutorialFlow.ResolveExistence(Target, def, EntityPresent());

        AssertThat(beforeTheEntityExists).IsEqual(Fallback);
        AssertThat(afterTheEntityExists)
            .OverrideFailureMessage("Resolving the SAME def against a later state still returned the fallback — the predicate looks cached rather than re-read from live state.")
            .IsEqual(Target);
    }

    /// <summary>
    /// The conformance half: every REAL registry row that declares AnchorExists must also declare
    /// AnchorFallback — the same "declared, never inferred" contract <see
    /// cref="EntityAbsent_WithNoDeclaredFallback_StillThrowsWithAUsefulMessage"/> proves
    /// behaviorally, pinned here as a static shape check over the whole registry so a future row
    /// cannot add a conditional anchor and forget its own fallback. Vacuous today (no row declares
    /// AnchorExists yet — this unit ships the mechanism, not a first conditional row), the same way
    /// <see cref="TutorialRegistryConformanceTests.Registry_NoTwoHudAnchors_ShareAControlName"/>
    /// would be vacuous with zero or one Hud anchors; the behavioral test above is what proves the
    /// rule this pins is actually enforced, not just declared.
    /// </summary>
    [TestCase]
    public void Registry_EveryRowThatDeclaresAnchorExists_AlsoDeclaresAnchorFallback()
    {
        foreach (var def in TutorialFlow.Registry.Where(d => d.AnchorExists is not null))
        {
            AssertThat(def.AnchorFallback)
                .OverrideFailureMessage(
                    $"{def.Step} declares AnchorExists but no AnchorFallback — this step would point " +
                    "at nothing the day its own entity is absent (house rule: never a silent fallback).")
                .IsNotNull();
        }
    }
}
#endif
