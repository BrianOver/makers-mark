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
/// atmosphere, not difficulty. Structural gates are unchanged since go-live; the venue's PROGRESSION
/// placement lives entirely in <see cref="VenueDefinition.EntryPower"/> below.</para>
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

        // EntryPower 79 (2026-08-02, P3/task #45 go-live re-tune — raised from 72): flipping
        // LiveRotation live at the tied 72 (Gloomwood's own band) handed Emberfall 41.4% of ALL
        // routed parties on a fresh 20-seed x 100-day BaselinePlayer sweep taken on THIS branch
        // (`dotnet run --project sim/GameSim.Cli -- batch --seeds 20 --seed 1 --days 100`,
        // tallied from every PartiesFormed event via `tools/Analytics`) — collapsing Gloomwood
        // from a same-codebase pre-flip reference of 64.3% down to 19.5%. MaterialRegistry's own
        // grade ladder says this was never the intent: Gloomwood mints grade 8-11 ore, Emberfall
        // grade 12-16 — a full tier later — so a same-band tie was a routing accident, not a
        // design statement. This is a THRESHOLD, not a gradient: router-side party power (which
        // never sees in-run craft/consumable modifiers) clusters tightly in the high-70s at
        // endgame, so the share swings hard over a few points — measured at each candidate on the
        // identical sweep:
        //   EntryPower  72  76  78  79  80   (pre-flip reference, Emberfall dormant: gloomwood 64.3%)
        //   gloomwood  19.5 29.1 40.7 50.5 57.2
        //   emberfall  41.4 35.3 23.4 14.6  7.3
        //   mine       30.5 27.6 27.9 26.9 27.5   (flat — Mine's band is untouched by this lever)
        //   crypt       8.6  8.0  8.0  8.0  8.0   (flat — Crypt's band is untouched by this lever)
        // 79 is the chosen landing: Gloomwood is back to an outright majority (50.5%, a
        // "substantial mid-game destination" again, not fully restored to its dormant-Emberfall
        // 64.3% but a clear plurality), Emberfall keeps a real endgame share (14.6% — comfortably
        // above dormant Sunken Crypt's flat 8.0%, so it reads as "the venue for parties that have
        // outgrown Gloomwood," not a rounding error) without being co-primary with it. 80 was
        // rejected: Emberfall's 7.3% share there dips BELOW the early-game Crypt's 8.0%, which
        // would make the newly-shipped endgame content look barely reached. Hero deaths (243) and
        // trade volume/items-sold (1057) both land closer to the pre-flip reference (257 deaths,
        // 1077 sold) than the as-is 72 tie did (230 deaths, 1022 sold) — this value undoes most of
        // the difficulty/economy drift the tied band introduced, not just the routing share.
        // THIS IS A TUNED CONSTANT, reversible in one line — re-measure with the batch farm before
        // moving it; the curve moves whenever crafting/routing moves (per the note this replaces).
        return new VenueDefinition(Id, "The Emberfall Foundry", floors.ToImmutable(), EntryPower: 79);
    }
}
