namespace GameSim.Classes;

/// <summary>
/// The Sentinel hero class, expressed entirely as data (add-on content, P3 kernel). A stalwart
/// protector: an anchor and shield-bearer like the Vanguard, but with a heavier defensive lean —
/// more starting HP and a slower offense (lower <see cref="ClassDefinition.BaseAttack"/>). It plugs
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
public static class SentinelClass
{
    /// <summary>Class key — lowercase kebab, matches every <c>Hero.ClassId</c> and the registry key.</summary>
    public const string Id = "sentinel";

    /// <summary>
    /// Bronze bulwark: anchors and bears a shield like the Vanguard, but soaks more (BaseHp 32 vs
    /// the Vanguard's 29) and hits slower (BaseAttack 3 vs 4). Unlimited carry — a heavy defender
    /// hauls the heaviest armor and shields.
    /// </summary>
    public static readonly ClassDefinition Definition = new(
        Id: Id,
        DisplayName: "Sentinel",
        BaseHp: 32,
        BaseAttack: 3,
        IsAnchor: true,
        AllowsShield: true,
        MaxItemWeight: null,
        ColorRgb: (176, 141, 87)); // bronze — a warm, distinct hue from the steel-blue Vanguard
}
