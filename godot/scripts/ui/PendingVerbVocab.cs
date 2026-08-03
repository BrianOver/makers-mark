using System;
using GameSim.Contracts;

namespace GodotClient.Ui;

/// <summary>
/// U3 (loop-legibility plan, KTD-B): display name + bell-promise line for each deferred
/// ("bell-rider") <see cref="PlayerAction"/> — the ONE vocabulary the bell tray's chips
/// (<c>MainUi.BuildBellTrayChip</c>) and the submission acknowledgment toast
/// (<c>MainUi.OnActionQueued</c>) both render from, instead of each inventing its own words.
///
/// <para>Covers exactly the set <see cref="GameSim.Kernel.ActionTiming"/> defers today —
/// <see cref="UpgradeForgeAction"/>, <see cref="SetProfessionsAction"/>,
/// <see cref="CommissionLegendaryWorkAction"/>. <c>BellTrayTests</c> mirrors the reflection idiom
/// <c>ActionTimingConformanceTests</c> (sim, U1) established: it reflection-enumerates every
/// concrete <see cref="PlayerAction"/> type, keeps the ones where
/// <see cref="GameSim.Kernel.ActionTiming.ResolvesImmediately"/> is false, and asserts THIS table
/// covers exactly that set — so a fourth bell-rider added later fails the test by name the moment
/// it exists, before it can ever reach a player as a chip with no words.</para>
///
/// <para>Deliberately THROWS for an uncovered type rather than degrading to a type name or a blank
/// line: a tray chip or toast with nothing to say is exactly the dead click this unit exists to
/// kill (house rule — never a silent fallback). The conformance test is what makes that throw
/// unreachable in a shipped build.</para>
/// </summary>
public static class PendingVerbVocab
{
    /// <summary>Short label for a bell-tray chip — what the player asked for ("Upgrade the forge").</summary>
    public static string DisplayName(PlayerAction action) => action switch
    {
        UpgradeForgeAction => "Upgrade the forge",
        SetProfessionsAction => "Change professions",
        CommissionLegendaryWorkAction => "Commission a legendary work",
        _ => throw Unnamed(action),
    };

    /// <summary>The acknowledgment toast's promise — what the bell will do with this submission
    /// ("At the bell: the Guild takes your commission").</summary>
    public static string BellPromise(PlayerAction action) => action switch
    {
        UpgradeForgeAction => "At the bell: the forge rises to its next tier.",
        SetProfessionsAction => "At the bell: your professions change.",
        CommissionLegendaryWorkAction => "At the bell: the Guild takes your commission.",
        _ => throw Unnamed(action),
    };

    private static InvalidOperationException Unnamed(PlayerAction action) => new(
        $"No PendingVerbVocab entry for {action.GetType().Name} — a new bell-rider (see " +
        "ActionTiming.ResolvesImmediately) must be named here before it can reach the bell tray.");
}
