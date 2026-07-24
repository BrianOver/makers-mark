using System.Collections.Immutable;
using GameSim.Contracts;

namespace GameSim.Crafting;

/// <summary>
/// Wave 5 (U23, tactile forge / "Anvil Map"): the blacksmith's puzzle submission — the SECOND
/// derived type of the dual-mode craft seam <see cref="CraftPuzzleInput"/> (PKD1), mirroring
/// <c>AlchemyReagentPuzzle</c>. The Godot "Anvil Map" overlay captures the player's forging as a
/// stream of INTEGER samples + strike events and rides <see cref="CraftAction.Puzzle"/>; the SIM
/// scores it deterministically via <c>ForgeScorer</c> (no floats crossing the boundary — KTD2 —
/// and a scripted balance-gate policy can construct this record directly with zero Godot).
///
/// <para>Lives in the crafting module, NOT in <c>Contracts/</c> (deny-listed): the abstract base
/// already exists there and needs no edit. Polymorphic (de)serialization for the save/ActionLog
/// round-trip is registered at runtime by <c>SaveCodec</c>'s type-info resolver (discriminator
/// <c>"$puzzle": "forgeTrace"</c>) — see <c>SaveCodec.AddCraftPuzzlePolymorphism</c>. Registering a
/// new derived type does not change how an absent/null <see cref="CraftAction.Puzzle"/> serializes,
/// so the idle golden trace is byte-unaffected (BaselinePlayer never forges with a puzzle).</para>
/// </summary>
/// <param name="Samples">The cursor path through the Anvil Map's heat/shape field, sampled at a
/// fixed cadence and quantized to a per-mille integer grid: a FLAT list of (xPermille, yPermille)
/// pairs in sample order, so <c>Samples.Count</c> is always EVEN and <c>Samples.Count / 2</c> is the
/// sample count (capped at 256 pairs = 512 ints by the overlay). X = shape progress [0..1000], Y =
/// heat [0..1000]. Plain integers — no floats, no engine types (KTD2). The scorer treats null/odd
/// input defensively (scores a floor), never throws.</param>
/// <param name="Strikes">The hammer strikes the player landed, a FLAT list of (xPermille,
/// tempoErrorPermille) pairs in strike order — where along the shape axis the blow landed and how
/// far off the tempo window it was (0 = dead on). Even length. Empty is legal (a forge with no
/// strikes simply scores poorly).</param>
/// <param name="PathSeed">The integer seed selecting which generated forging-line variant this craft
/// was worked against, so Godot and the sim agree on the same target polyline for the recipe. The
/// path itself is regenerated deterministically sim-side by <c>ForgePath</c> from the recipe +
/// this seed; the seed never introduces RNG (it is chosen by the overlay and rides the action).</param>
public sealed record ForgeTraceInput(
    ImmutableList<int> Samples,
    ImmutableList<int> Strikes,
    int PathSeed) : CraftPuzzleInput;
