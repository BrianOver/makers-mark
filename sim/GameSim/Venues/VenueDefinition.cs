using System.Collections.Immutable;

namespace GameSim.Venues;

/// <summary>
/// One floor of a <see cref="VenueDefinition"/> expressed entirely as data (P4 kernel).
/// The seven per-floor numbers the resolver and attribution engine read — the structural
/// <see cref="Gate"/>, the monster's stats, the kill reward, and the ore rarity — that used
/// to live in the single static <c>MonsterTable</c> switch statements, relocated here so a
/// venue's floors are "just data".
///
/// Pure data: NO Godot reference, NO RNG, integer-only (no transcendental <c>Math.*</c>).
/// </summary>
/// <param name="Floor">1-based floor index within the venue (1..FloorCount).</param>
/// <param name="Gate">Minimum party-average effective power to attempt clearing this floor.
/// STRUCTURAL: an under-geared party retreats at the gate with no roll (AE3).</param>
/// <param name="MonsterKind">Display name of the floor's monster (e.g. "Cave Rat"). Read into
/// <c>CombatEvent.MonsterKind</c> and the death cause prose — byte-sensitive.</param>
/// <param name="MonsterHp">Monster starting HP for the floor's fight.</param>
/// <param name="MonsterAttack">Monster attack stat feeding <c>CombatMath.MonsterDamage</c>.</param>
/// <param name="MonsterDefense">Monster defense stat feeding <c>CombatMath.HeroDamage</c>.</param>
/// <param name="GoldPerKill">Gold banked by the hero who lands the killing blow.</param>
/// <param name="OreKey">Material key of the ore looted on a cleared floor (rarity rises with
/// depth). Priced at Evening via <c>OrePricing</c>; inverted by <see cref="VenueDefinition.OreFloor"/>.</param>
public sealed record VenueFloor(
    int Floor,
    int Gate,
    string MonsterKind,
    int MonsterHp,
    int MonsterAttack,
    int MonsterDefense,
    int GoldPerKill,
    string OreKey);

/// <summary>
/// A raid venue expressed entirely as data (P4 kernel, the third Blacksmith-as-data
/// relocation after P1 professions and P3 classes). The single 5-floor Mine that used to be
/// the static <c>MonsterTable</c> — read directly by the resolver, the attribution engine, and
/// the expedition system — is now one <see cref="VenueDefinition"/> among N in a
/// <see cref="VenueRegistry"/>. An add-on venue becomes one definition + one registration line;
/// no resolver, attribution, or contract edit.
///
/// Pure data: NO Godot reference, NO RNG, integer-only. The determinism-critical resolver +
/// attribution path reads floor numbers ONLY through this definition; the venue supplies the
/// numbers, the combat math and RNG draw order are unchanged (KTD5/KTD6). The per-floor
/// accessors mirror the old <c>MonsterTable</c> method surface so threading a venue in place of
/// the static table is a mechanical substitution.
/// </summary>
/// <param name="Id">Stable string key (e.g. "mine"). Matches the registry key and every
/// <c>ExpeditionResult.VenueId</c>.</param>
/// <param name="DisplayName">Human-readable venue name. Presentation only — the sim's
/// byte-sensitive prose reads per-floor <see cref="VenueFloor.MonsterKind"/>, not this.</param>
/// <param name="Floors">Per-floor data in ascending floor order; index 0 is floor 1. Deterministic
/// iteration; integer-only.</param>
public sealed record VenueDefinition(
    string Id,
    string DisplayName,
    ImmutableArray<VenueFloor> Floors)
{
    /// <summary>Number of floors in the venue (the deepest attemptable floor).</summary>
    public int FloorCount => Floors.Length;

    private VenueFloor At(int floor) =>
        floor >= 1 && floor <= Floors.Length
            ? Floors[floor - 1]
            : throw new ArgumentOutOfRangeException(nameof(floor));

    /// <summary>Minimum party-average effective power required to attempt clearing <paramref name="floor"/>.</summary>
    public int Gate(int floor) => At(floor).Gate;

    /// <summary>Display name of the monster on <paramref name="floor"/>.</summary>
    public string MonsterKind(int floor) => At(floor).MonsterKind;

    /// <summary>Monster starting HP on <paramref name="floor"/>.</summary>
    public int MonsterHp(int floor) => At(floor).MonsterHp;

    /// <summary>Monster attack stat on <paramref name="floor"/>.</summary>
    public int MonsterAttack(int floor) => At(floor).MonsterAttack;

    /// <summary>Monster defense stat on <paramref name="floor"/>.</summary>
    public int MonsterDefense(int floor) => At(floor).MonsterDefense;

    /// <summary>Gold banked per monster kill on <paramref name="floor"/>.</summary>
    public int GoldPerKill(int floor) => At(floor).GoldPerKill;

    /// <summary>Ore material key looted on a cleared <paramref name="floor"/>.</summary>
    public string OreKey(int floor) => At(floor).OreKey;

    /// <summary>
    /// Inverse of <see cref="OreKey"/>: the floor an ore key came from, or 0 when this venue
    /// mints no such ore. Venue-scoped replacement for the old Mine-specific loop in
    /// <c>LedgerQuery.OreFloor</c>.
    /// </summary>
    public int OreFloor(string oreKey)
    {
        for (var floor = 1; floor <= Floors.Length; floor++)
        {
            if (Floors[floor - 1].OreKey == oreKey)
            {
                return floor;
            }
        }

        return 0;
    }
}
