using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Harness;

namespace GameSim.Tests.Harness;

/// <summary>
/// PA5 (plan 2026-07-21-002): the plan's required one-line pin that <see cref="BaselinePlayer"/>
/// is untouched by Phase A — never forked, never edited to know about the counter or the active
/// minigame. A fresh campaign's very first Morning is a fixed, well-known fixture (no materials,
/// no talents, no shelf) — <see cref="BaselinePlayer.ActionsFor"/>'s ONLY legal move there is to
/// unlock the first prerequisite-free blacksmith talent node in <c>NodeId</c> order
/// (<c>TalentTree.KeenEye</c>, alphabetically first among "keen-eye"/"material-efficiency"/
/// "tier-2-smithing"). If this ever changes, either <see cref="BaselinePlayer"/> was edited (it
/// must not be, per PKD4: it already emits null-grade crafts and needs zero Phase-A changes) or
/// the talent tree's root set changed underneath it — either way, this regression must be seen.
/// </summary>
public class BaselinePlayerPinTests
{
    [Fact]
    public void ActionsFor_FreshCampaignMorning_IsUnchangedFromPrePhaseA()
    {
        var state = GameComposition.NewCampaign(seed: 2026);

        var actions = BaselinePlayer.ActionsFor(state);

        Assert.Equal(ImmutableList.Create<PlayerAction>(new UnlockTalentAction("keen-eye", "blacksmith")), actions);
    }
}
