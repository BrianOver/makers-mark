namespace GameSim.Classes;

/// <summary>
/// The Occultist hero class, expressed entirely as data (add-on content, P3 kernel). A dark caster,
/// mystic-adjacent but with a higher risk/damage lean: glassier than the Mystic (less HP) and
/// hitting harder (higher <see cref="ClassDefinition.BaseAttack"/>), sharing the Mystic's no-shield,
/// light-carry profile. It plugs into the class-agnostic hero pipeline (<c>CombatMath</c>,
/// <c>ShoppingAi</c>, <c>PartyFormation</c>, and the Godot hero panel/sprite via
/// <see cref="ClassDefinition.ColorRgb"/>) through a single registration line the orchestrator applies
/// to <see cref="ClassRegistry.All"/> — no code changes outside this directory (see docs/addon-guide.md).
///
/// Pure data: integer stats only, no RNG, no wall clock, no floats, no Godot references (KTD2/KTD4).
/// <see cref="ClassDefinition.ColorRgb"/> is a presentation hint the sim never reads (Godot multiplies
/// it onto a neutral hero figure). Registered-inert: a registered class is NOT recruitable
/// (<see cref="ClassRegistry.RecruitPool"/> stays frozen at the three built-ins), so shipping this
/// definition moves no existing seed's world.
/// </summary>
public static class OccultistClass
{
    /// <summary>Class key — lowercase kebab, matches every <c>Hero.ClassId</c> and the registry key.</summary>
    public const string Id = "occultist";

    /// <summary>
    /// Dark-violet caster: the Mystic's shape (no shield, weight cap 4) pushed toward risk and
    /// damage — HP 18 (glassier than the Mystic's 20) and BaseAttack 5 (vs the Mystic's 3).
    /// </summary>
    public static readonly ClassDefinition Definition = new(
        Id: Id,
        DisplayName: "Occultist",
        BaseHp: 18,
        BaseAttack: 5,
        IsAnchor: false,
        AllowsShield: false,
        MaxItemWeight: 4,
        ColorRgb: (85, 26, 110)); // dark violet — deeper and less saturated than the Mystic's bright violet
}
