using System.Collections.Immutable;

namespace GameSim.Venues.SunkenCrypt;

/// <summary>
/// The Sunken Crypt — flooded catacombs under the old chapel, and an ADD-ON venue (a content pack,
/// mirroring the built-in <see cref="VenueRegistry.Mine"/>). It plugs into the shared expedition
/// pipeline as pure data: no resolver, attribution, or contract edit, and a single
/// orchestrator-applied registration line (see docs/addon-guide.md "Venues/maps" and the C2 packet).
///
/// <para><b>Five floors, Mine-peer gates 0/15/35/60/100.</b> A deliberate peer of the Mine's gate
/// curve — same structural difficulty ladder — so the monster stats mirror the Mine's peer formulas
/// (HP 12+10f, attack 5+6f, defense 2+2f, gold 5+3f); the two venues differ in NAMES, ORES, and
/// atmosphere, not difficulty. Final stat tuning rides go-live (wave-D D8); until then the venue is
/// registered but NOT in <see cref="VenueRegistry.LiveRotation"/>, so it moves no seed's world.</para>
///
/// <para><b>Ore ladder (crypt palette family).</b> verdigris → saltglass → bonechalk →
/// drowned-silver → abyss-pearl, one per floor, unique within the venue (the <c>OreFloor</c>
/// inversion guards uniqueness). These are the Tidewrit Salvors' supply materials; they are
/// registered in the material registry (draw-neutral, not in the priced pool) alongside this venue.
/// Distinct from the test-only <c>sunken-vault</c> fixture's ore keys (brine-salt / pearl /
/// abyssal-glass) so the two never collide.</para>
///
/// <para><b>Monster personalities</b> (art + future per-floor-variant direction, C7 / wave-D D2 — the
/// current <see cref="VenueFloor"/> contract carries only the kind NAME, so the character lives here
/// as documentation until the monster-variant core lands a flavor field):
/// <list type="bullet">
/// <item>F1 <b>Crypt Crab</b> — wears a borrowed skull for a shell and is self-conscious about it.</item>
/// <item>F2 <b>Bog-Wight</b> — a drowned marsh-revenant.</item>
/// <item>F3 <b>Choir of Teeth</b> — a chittering swarm that sings.</item>
/// <item>F4 <b>Reliquary Mimic</b> — a false shrine that accepts "donations."</item>
/// <item>F5 <b>The Undertow</b> — the venue boss, the pull of the deep water itself.</item>
/// </list></para>
///
/// Pure data: NO Godot reference, NO RNG, integer-only (no floats, no transcendental <c>Math.*</c>,
/// no wall clock). Determinism-safe by construction (KTD2/KTD5).
/// </summary>
public static class SunkenCryptVenue
{
    /// <summary>Stable registry key for the Sunken Crypt venue (lowercase kebab).</summary>
    public const string Id = "sunken-crypt";

    /// <summary>The venue's ore material keys, floor 1 → 5 (the Tidewrit Salvors' supply).</summary>
    public const string Verdigris = "verdigris";
    public const string Saltglass = "saltglass";
    public const string Bonechalk = "bonechalk";
    public const string DrownedSilver = "drowned-silver";
    public const string AbyssPearl = "abyss-pearl";

    /// <summary>
    /// The Sunken Crypt definition: 5 floors, gates 0/15/35/60/100 (Mine-peer, non-decreasing), the
    /// named crypt monsters, and the verdigris…abyss-pearl ore ladder. Built once, immutable forever.
    /// </summary>
    public static readonly VenueDefinition Definition = Build();

    private static VenueDefinition Build()
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
                    5 => 100, // Mine-peer boss gate; tuned at go-live (D8)
                    _ => throw new ArgumentOutOfRangeException(nameof(floor)),
                },
                MonsterKind: floor switch
                {
                    1 => "Crypt Crab",
                    2 => "Bog-Wight",
                    3 => "Choir of Teeth",
                    4 => "Reliquary Mimic",
                    5 => "The Undertow",
                    _ => throw new ArgumentOutOfRangeException(nameof(floor)),
                },
                MonsterHp: 12 + 10 * floor,     // Mine-peer difficulty curve (same gate ladder)
                MonsterAttack: 5 + 6 * floor,
                MonsterDefense: 2 + 2 * floor,
                GoldPerKill: 5 + 3 * floor,
                OreKey: floor switch
                {
                    1 => Verdigris,
                    2 => Saltglass,
                    3 => Bonechalk,
                    4 => DrownedSilver,
                    5 => AbyssPearl,
                    _ => throw new ArgumentOutOfRangeException(nameof(floor)),
                }));
        }

        return new VenueDefinition(Id, "The Sunken Crypt", floors.ToImmutable());
    }
}
