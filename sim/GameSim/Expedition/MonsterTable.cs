namespace GameSim.Expedition;

/// <summary>
/// Floor data for the 5-floor Mine (R9). All integers. Floor gates are STRUCTURAL:
/// a party whose average effective power is below the gate retreats at the gate —
/// no roll can carry rival-grade gear through Floor 5 (AE3).
/// </summary>
public static class MonsterTable
{
    public const int FloorCount = 5;

    /// <summary>Minimum party-average effective power required to attempt clearing each floor.</summary>
    public static int Gate(int floor) => floor switch
    {
        1 => 0,
        2 => 15,
        3 => 35,
        4 => 60,
        5 => 100, // above any rival-vendor loadout by design (AE3); tuned in U10
        _ => throw new ArgumentOutOfRangeException(nameof(floor)),
    };

    public static string MonsterKind(int floor) => floor switch
    {
        1 => "Cave Rat",
        2 => "Tunnel Spider",
        3 => "Deep Ghoul",
        4 => "Ore Golem",
        5 => "The Forgeworm",
        _ => throw new ArgumentOutOfRangeException(nameof(floor)),
    };

    public static int MonsterHp(int floor) => 12 + 10 * floor;

    public static int MonsterAttack(int floor) => 5 + 6 * floor;

    public static int MonsterDefense(int floor) => 2 + 2 * floor;

    public static int GoldPerKill(int floor) => 5 + 3 * floor;

    /// <summary>Ore rarity rises with depth — the flywheel's fuel (R6).</summary>
    public static string OreKey(int floor) => floor switch
    {
        1 => "copper",
        2 => "iron",
        3 => "steel",
        4 => "mithril",
        5 => "adamant",
        _ => throw new ArgumentOutOfRangeException(nameof(floor)),
    };
}
