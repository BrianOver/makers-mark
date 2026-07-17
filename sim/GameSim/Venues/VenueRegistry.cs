using System.Collections.Immutable;

namespace GameSim.Venues;

/// <summary>
/// The single lookup the expedition pipeline uses to resolve a venue key to its
/// <see cref="VenueDefinition"/> (P4 kernel, mirrors <c>ClassRegistry</c>/<c>ProfessionRegistry</c>).
/// The one built-in venue — the 5-floor <see cref="Mine"/> — is registered here carrying the EXACT
/// values the old static <c>MonsterTable</c> held (the gate/kind/hp/attack/defense/gold/ore switches
/// relocated verbatim), copied so the Mine stays byte-identical. A new venue registers by adding a
/// definition to <see cref="All"/> (an add-on task, not core work).
/// </summary>
public static class VenueRegistry
{
    /// <summary>The one built-in, live venue: the 5-floor Mine (R9).</summary>
    public const string MineId = "mine";

    /// <summary>
    /// The 5-floor Mine, built from the EXACT current <c>MonsterTable</c> values (FloorCount 5;
    /// gate 0/15/35/60/100; kinds Cave Rat/Tunnel Spider/Deep Ghoul/Ore Golem/The Forgeworm;
    /// HP 12+10*f; attack 5+6*f; defense 2+2*f; gold 5+3*f; ore copper/iron/steel/mithril/adamant).
    /// The Floor-5 gate sits above any rival-vendor loadout by design (AE3). Byte-identical to the
    /// old static table is pinned by <c>VenueConformanceTests</c>.
    /// </summary>
    public static readonly VenueDefinition Mine = BuildMine();

    /// <summary>All registered venues, keyed by id. Sorted (Ordinal) for deterministic iteration.</summary>
    public static readonly ImmutableSortedDictionary<string, VenueDefinition> All = new[]
    {
        Mine,
    }.ToImmutableSortedDictionary(v => v.Id, v => v, StringComparer.Ordinal);

    /// <summary>
    /// The venues that are LIVE — the ones hero parties actually raid. THIS IS THE LIVE-VENUE
    /// CONTRACT (same rule as <c>ClassRegistry.RecruitPool</c>): a registered venue is NOT
    /// automatically live. Frozen at the single Mine so hero routing → target floors → the whole
    /// sim is byte-identical; a live second venue would move every seed's balance and is the
    /// deferred multi-venue follow-on. Add-on/test venues live in <see cref="All"/> but never here
    /// until a determinism-gated re-baseline expands the rotation.
    /// </summary>
    public static readonly ImmutableArray<string> LiveRotation = ImmutableArray.Create(MineId);

    /// <summary>Resolve a venue definition by key.</summary>
    public static bool TryGet(string venueId, out VenueDefinition? definition)
    {
        var found = All.TryGetValue(venueId, out var def);
        definition = def;
        return found;
    }

    /// <summary>Whether a venue key is registered.</summary>
    public static bool IsRegistered(string venueId) => All.ContainsKey(venueId);

    /// <summary>
    /// Resolve a venue definition by key or throw — the production path for a venue id that always
    /// comes from a registration or a save written from a registered id, so an unregistered id is a
    /// malformed-data defect that should fail loudly.
    /// </summary>
    public static VenueDefinition Require(string venueId) =>
        All.TryGetValue(venueId, out var def)
            ? def
            : throw new KeyNotFoundException($"Venue id '{venueId}' is not registered.");

    /// <summary>
    /// The Mine's five floors, reproducing the old <c>MonsterTable</c> formulas and switches
    /// exactly. Floor gates are STRUCTURAL: a party below the gate retreats at the gate — no roll
    /// can carry rival-grade gear through Floor 5 (AE3).
    /// </summary>
    private static VenueDefinition BuildMine()
    {
        var floors = ImmutableArray.CreateBuilder<VenueFloor>(5);
        for (var floor = 1; floor <= 5; floor++)
        {
            floors.Add(new VenueFloor(
                Floor: floor,
                Gate: floor switch
                {
                    1 => 0,
                    2 => 15,
                    3 => 35,
                    4 => 60,
                    5 => 100, // above any rival-vendor loadout by design (AE3); tuned in U10
                    _ => throw new ArgumentOutOfRangeException(nameof(floor)),
                },
                MonsterKind: floor switch
                {
                    1 => "Cave Rat",
                    2 => "Tunnel Spider",
                    3 => "Deep Ghoul",
                    4 => "Ore Golem",
                    5 => "The Forgeworm",
                    _ => throw new ArgumentOutOfRangeException(nameof(floor)),
                },
                MonsterHp: 12 + 10 * floor,
                MonsterAttack: 5 + 6 * floor,
                MonsterDefense: 2 + 2 * floor,
                GoldPerKill: 5 + 3 * floor,
                OreKey: floor switch
                {
                    1 => "copper",
                    2 => "iron",
                    3 => "steel",
                    4 => "mithril",
                    5 => "adamant",
                    _ => throw new ArgumentOutOfRangeException(nameof(floor)),
                }));
        }

        return new VenueDefinition(MineId, "The Mine", floors.ToImmutable());
    }
}
