using GameSim.Venues;

namespace GameSim.Expedition;

/// <summary>
/// Floor data for the 5-floor Mine (R9), kept as a thin compatibility shim over
/// <see cref="VenueRegistry.Mine"/> (P4). The per-floor numbers now live in the Mine's
/// <see cref="VenueDefinition"/> — the single source of truth — and the determinism-critical
/// resolver + attribution path reads the venue directly. These statics remain only for the
/// out-of-path Mine-floor consumers (bounty floor bounds, the town's bounty spinner) so those
/// call sites need no venue in hand; each delegates to the Mine.
///
/// <see cref="FloorCount"/> stays a compile-time <c>const</c> because a caller uses it in a
/// relational pattern (which requires a constant); the conformance test pins it to
/// <see cref="VenueDefinition.FloorCount"/>.
/// </summary>
public static class MonsterTable
{
    public const int FloorCount = 5;

    /// <summary>Minimum party-average effective power required to attempt clearing each floor.</summary>
    public static int Gate(int floor) => VenueRegistry.Mine.Gate(floor);

    public static string MonsterKind(int floor) => VenueRegistry.Mine.MonsterKind(floor);

    public static int MonsterHp(int floor) => VenueRegistry.Mine.MonsterHp(floor);

    public static int MonsterAttack(int floor) => VenueRegistry.Mine.MonsterAttack(floor);

    public static int MonsterDefense(int floor) => VenueRegistry.Mine.MonsterDefense(floor);

    public static int GoldPerKill(int floor) => VenueRegistry.Mine.GoldPerKill(floor);

    /// <summary>Ore rarity rises with depth — the flywheel's fuel (R6).</summary>
    public static string OreKey(int floor) => VenueRegistry.Mine.OreKey(floor);
}
