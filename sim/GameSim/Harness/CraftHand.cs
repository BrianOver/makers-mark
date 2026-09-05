namespace GameSim.Harness;

/// <summary>
/// P2-OQ11 (owner ruling 2026-09-04): how good the scripted hand driving a craft minigame is,
/// defined by ONE rule that every profession's policy applies to its own puzzle. This is the
/// instrument the quality-curve ruling turns on, and it exists because the previous one could not
/// tell "this grade is unreachable" from "nobody ever reached for it".
///
/// <para><b>The error this type prevents, twice over.</b> #705 was nearly mis-read as a blacksmith
/// defect because its only measuring policy swings a constant 50-per-mille error and never tries to
/// improve, so its 0% Masterwork said nothing about whether Masterwork was reachable. #715 then
/// measured Alchemy, Tanning and Engineering with three hands each described as "average" that were
/// in fact three DIFFERENT skill levels: Alchemy's poured every reagent in the wrong place (an
/// indifferent hand), Tanning's scraped the whole hide evenly (a hand that ruins every delicate
/// patch), and Engineering's identified every part correctly and built in perfect order, missing
/// only the final seat (a near-flawless hand). A good part of the "four unrelated curves" that
/// reading showed was three uncalibrated instruments, not three broken scorers — see this unit's PR
/// body for how much of the spread each explains.</para>
///
/// <para><b>The one rule, applied per profession.</b> Each level means the same thing in every
/// craft, and each policy expresses it in its own puzzle's terms — which is exactly why the four
/// crafts can be compared without being made identical (<c>THE-GAME.md</c> §4.1):</para>
/// <list type="bullet">
///   <item><description><see cref="Indifferent"/> — the best hand available to someone who ignores
///   the information the puzzle puts on screen. It is the calibration point
///   <see cref="Crafting.CraftCurve.IndifferentAnchorPermille"/> anchors, so an indifferent hand
///   should measure Common in all four crafts.</description></item>
///   <item><description><see cref="Average"/> — clearly trying and clearly imperfect: roughly half
///   the craft done right.</description></item>
///   <item><description><see cref="Skilled"/> — one mistake short of flawless. Flawless itself is
///   already covered by each scorer's unit tests; what a sweep needs is the hand just below it,
///   because that is the one that answers "is the top grade reachable by a skilled player rather
///   than only by a perfect one".</description></item>
/// </list>
///
/// <para>Every hand is a constant, deterministic, total function of the recipe — no RNG, no IO, no
/// wall clock (KTD2/KTD4), the same contract <c>HandForgePlayer.BuildTrace</c> already held.</para>
/// </summary>
public enum CraftHand
{
    /// <summary>Ignores what the puzzle shows. Calibrates to the middle of Common.</summary>
    Indifferent,

    /// <summary>Clearly trying, clearly imperfect — roughly half the craft done right.</summary>
    Average,

    /// <summary>One mistake short of flawless.</summary>
    Skilled,
}
