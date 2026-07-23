using System.Collections.Immutable;
using GameSim.Contracts;

namespace GameSim.Professions;

/// <summary>
/// Phase B (alchemist active-craft): the player's reagent-puzzle submission — the FIRST derived
/// type of the dual-mode craft seam <see cref="CraftPuzzleInput"/> (PKD1). Unlike the blacksmith,
/// whose Godot minigame folds its own result into <see cref="CraftAction.PerformanceGrade"/>, the
/// alchemist's puzzle INPUT rides the action and the SIM scores it deterministically
/// (<see cref="AlchemyPuzzleScorer"/>) — strictly better balance-gate coverage, because a scripted
/// policy can construct this record directly with zero Godot involvement.
///
/// <para>Lives in the alchemy module, NOT in <c>Contracts/</c> (deny-listed): the abstract base
/// already exists there and needs no edit. Polymorphic (de)serialization for the save/ActionLog
/// round-trip is registered at runtime by <c>SaveCodec</c>'s type-info resolver (discriminator
/// <c>"$puzzle": "alchemyReagent"</c>) — see the save-shape note on <c>SaveCodec</c>.</para>
/// </summary>
/// <param name="Reagents">The ordered reagent ids the player added to the cauldron, in pour
/// order (indices into <see cref="AlchemyReagents.Names"/>). Plain integers — no floats, no
/// engine types (KTD2). Entries beyond the recipe's ideal-sequence length are ignored by the
/// scorer; unknown ids simply score zero. Never null in a well-formed action; the scorer
/// treats null defensively as empty.</param>
public sealed record AlchemyReagentPuzzle(ImmutableList<int> Reagents) : CraftPuzzleInput;
