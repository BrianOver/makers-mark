namespace GameSim.Classes;

/// <summary>
/// The Skirmisher hero class, expressed entirely as data (add-on content, P3 kernel). A fast
/// flanker sitting between the Striker and the Vanguard: middling HP and attack, no shield, and a
/// mobility-flavored weight cap so it travels light rather than lugging the heaviest gear. It plugs
/// into the class-agnostic hero pipeline (<c>CombatMath</c>, <c>ShoppingAi</c>, <c>PartyFormation</c>,
/// and the Godot hero panel/sprite via <see cref="ClassDefinition.ColorRgb"/>) through a single
/// registration line the orchestrator applies to <see cref="ClassRegistry.All"/> — no code changes
/// outside this directory (see docs/addon-guide.md).
///
/// Pure data: integer stats only, no RNG, no wall clock, no floats, no Godot references (KTD2/KTD4).
/// <see cref="ClassDefinition.ColorRgb"/> is a presentation hint the sim never reads (Godot multiplies
/// it onto a neutral hero figure). Registered-inert: a registered class is NOT recruitable
/// (<see cref="ClassRegistry.RecruitPool"/> stays frozen at the three built-ins), so shipping this
/// definition moves no existing seed's world.
/// </summary>
public static class SkirmisherClass
{
    /// <summary>Class key — lowercase kebab, matches every <c>Hero.ClassId</c> and the registry key.</summary>
    public const string Id = "skirmisher";

    /// <summary>
    /// Emerald flanker: HP 26 and BaseAttack 5 both land between the Striker (24 / 6) and the
    /// Vanguard (29 / 4). No shield, and a weight cap of 6 (heavier than the Mystic's 4, lighter
    /// than a front-liner's unlimited) — the mobility lean, expressed as data.
    /// </summary>
    public static readonly ClassDefinition Definition = new(
        Id: Id,
        DisplayName: "Skirmisher",
        BaseHp: 26,
        BaseAttack: 5,
        IsAnchor: false,
        AllowsShield: false,
        MaxItemWeight: 6,
        ColorRgb: (46, 204, 113)); // emerald — a mobility-flavored hue distinct from the three built-ins
}
