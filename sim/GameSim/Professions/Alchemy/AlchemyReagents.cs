using System.Collections.Immutable;

namespace GameSim.Professions;

/// <summary>
/// The alchemist's reagent roster — constant DATA shared by the sim-side scorer
/// (<see cref="AlchemyPuzzleScorer"/>) and the Godot brew-puzzle overlay (which renders one
/// palette button per reagent). Ids are the indices into <see cref="Names"/>; the puzzle record
/// (<see cref="AlchemyReagentPuzzle"/>) carries raw ids so the sim never sees a display string.
/// Integer/string constants only — no RNG, no clock, no floats (KTD2).
/// </summary>
public static class AlchemyReagents
{
    public const int Sunpetal = 0;
    public const int Ironmoss = 1;
    public const int Dewroot = 2;
    public const int Cinderbark = 3;
    public const int Glimmercap = 4;
    public const int Voidsalt = 5;

    /// <summary>Display names, indexed by reagent id. <c>Names.Length</c> is the roster size.</summary>
    public static readonly ImmutableArray<string> Names = ImmutableArray.Create(
        "Sunpetal", "Ironmoss", "Dewroot", "Cinderbark", "Glimmercap", "Voidsalt");

    /// <summary>Number of distinct reagents (the valid id range is <c>[0, Count)</c>).</summary>
    public static int Count => Names.Length;
}
