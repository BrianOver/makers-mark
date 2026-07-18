using System.Collections.Immutable;

namespace GameSim.Venues.Emberfall;

/// <summary>
/// The Emberfall Foundry — abandoned dwarven smelting halls, and an ADD-ON venue (a content pack,
/// mirroring the built-in <see cref="VenueRegistry.Mine"/> and the merged Gloomwood / Sunken Crypt
/// packs). It plugs into the shared expedition pipeline as pure data: no resolver, attribution, or
/// contract edit, and a single orchestrator-applied registration line (see docs/addon-guide.md
/// "Venues/maps").
///
/// <para><b>The warm register.</b> The first venue in the <c>den</c> palette family — coal-glow ambers
/// and molten-channel oranges rather than the cool purples/greens/blues of the Mine, Gloomwood, and
/// Crypt. A flooded-forge ruin whose fires never fully went out: warmer, less-dark variety for the
/// venue roster.</para>
///
/// <para><b>Five floors, Mine-peer gates 0/15/35/60/100.</b> A deliberate peer of the Mine's gate
/// curve — same structural difficulty ladder — so the monster stats mirror the Mine's peer formulas
/// (HP 12+10f, attack 5+6f, defense 2+2f, gold 5+3f); the venues differ in NAMES, ORES, and
/// atmosphere, not difficulty. Final stat tuning rides go-live (wave-D D8); until then the venue is
/// registered but NOT in <see cref="VenueRegistry.LiveRotation"/>, so it moves no seed's world.</para>
///
/// <para><b>Ore ladder (den palette family).</b> firebrick → slagiron → quench-salt → emberglass →
/// heartcoal, one per floor, unique within the venue (the <c>OreFloor</c> inversion guards
/// uniqueness) and disjoint from every other venue's ores — the Foundry mints forge-ores, never Mine,
/// Gloomwood, or Crypt ore. These are the Ashguild's supply materials; they are registered in the
/// material registry (draw-neutral, not in the priced pool) alongside this venue.</para>
///
/// <para><b>Monster personalities</b> (art + future per-floor-variant direction — the current
/// <see cref="VenueFloor"/> contract carries only the kind NAME, so the character lives here as
/// documentation until the monster-variant core lands a flavor field):
/// <list type="bullet">
/// <item>F1 <b>Cinder Imp</b> — steals hot coals to warm itself, and is apologetic about it.</item>
/// <item>F2 <b>Slag Hound</b> — a molten-slag mongrel that guards the cooling channels.</item>
/// <item>F3 <b>The Bellows-Mad</b> — a forge-golem convinced the fires must never, ever die.</item>
/// <item>F4 <b>Molten Archivist</b> — hoards fireproof ledgers and resents any withdrawal.</item>
/// <item>F5 <b>The Undying Forge-Heart</b> — the venue boss, the great furnace-core that will not go
/// cold.</item>
/// </list></para>
///
/// Pure data: NO Godot reference, NO RNG, integer-only (no floats, no transcendental <c>Math.*</c>,
/// no wall clock). Determinism-safe by construction (KTD2/KTD5).
/// </summary>
public static class EmberfallFoundryVenue
{
    /// <summary>Stable registry key for the Emberfall Foundry venue (lowercase kebab).</summary>
    public const string Id = "emberfall";

    /// <summary>The venue's ore material keys, floor 1 → 5 (the Ashguild's supply). Its own, disjoint
    /// from every other venue's ores so no venue mints an ore another venue already mints.</summary>
    public const string Firebrick = "firebrick";
    public const string Slagiron = "slagiron";
    public const string QuenchSalt = "quench-salt";
    public const string Emberglass = "emberglass";
    public const string Heartcoal = "heartcoal";

    /// <summary>
    /// The Emberfall Foundry definition: 5 floors, gates 0/15/35/60/100 (Mine-peer, non-decreasing),
    /// the named forge monsters, and the firebrick…heartcoal ore ladder. Built once, immutable forever.
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
                    1 => "Cinder Imp",
                    2 => "Slag Hound",
                    3 => "The Bellows-Mad",
                    4 => "Molten Archivist",
                    5 => "The Undying Forge-Heart",
                    _ => throw new ArgumentOutOfRangeException(nameof(floor)),
                },
                MonsterHp: 12 + 10 * floor,     // Mine-peer difficulty curve (same gate ladder)
                MonsterAttack: 5 + 6 * floor,
                MonsterDefense: 2 + 2 * floor,
                GoldPerKill: 5 + 3 * floor,
                OreKey: floor switch
                {
                    1 => Firebrick,
                    2 => Slagiron,
                    3 => QuenchSalt,
                    4 => Emberglass,
                    5 => Heartcoal,
                    _ => throw new ArgumentOutOfRangeException(nameof(floor)),
                }));
        }

        return new VenueDefinition(Id, "The Emberfall Foundry", floors.ToImmutable());
    }
}
