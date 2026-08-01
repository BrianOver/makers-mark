using System.Collections.Immutable;

namespace GameSim.Venues.Gloomwood;

/// <summary>
/// The Gloomwood — a moonlit fungal forest and the SECOND raid venue (an add-on content pack, C1,
/// mirroring the built-in <see cref="VenueRegistry.Mine"/>). It plugs into the shared expedition
/// pipeline as pure data: no resolver, attribution, or contract edit, and a single
/// orchestrator-applied registration line (see docs/addon-guide.md "Adding a venue").
///
/// The first NON-purple venue — the <c>gloomwood</c> palette family (moss + verdigris + firefly),
/// the depth-3 nature band of the palette registry. Four floors, each a named creature with a
/// personality the retellings will lean on once the D1 monster-table FlavorTag contract lands
/// (until then the personality lives here, in the display name + these notes):
/// <list type="bullet">
/// <item><b>F1 Bramble Boar</b> — gluttonous; eats fence posts, permits, and anything not nailed down.</item>
/// <item><b>F2 Lantern Moth</b> — politely steals the party's light and apologizes for it.</item>
/// <item><b>F3 The Wicker Shepherd</b> — a walking scarecrow that herds lost travelers safely home,
/// whether they wanted herding or not.</item>
/// <item><b>F4 Old Mossjaw</b> — the venue boss; the forest's oldest, mossiest jaw.</item>
/// </list>
///
/// Gates 0/20/45/75 are non-decreasing with depth (conformance). Its four ore keys —
/// <c>greenheart</c>/<c>amberpitch</c>/<c>moonresin</c>/<c>heartwood</c> — are unique within the
/// venue (the <see cref="VenueDefinition.OreFloor"/> inversion pins it) and are its own, disjoint from
/// the Mine's <c>copper…adamant</c>: the Gloomwood mints nature-ores, never Mine ore. Supplied to the
/// player by the Gloomwood Wardens faction (see <c>Factions/Wardens</c>).
///
/// <para><b>Live (Phase C U-C4).</b> The venue is registered into <c>VenueRegistry.All</c> AND into
/// <c>VenueRegistry.LiveRotation</c> as the second live venue alongside the Mine: <c>VenueRouter</c>
/// distributes bounty-free parties across the two by utility + queue length
/// (<c>ExpeditionSystem.Process</c> / <c>MusterPlan.Compute</c>), so its floors DO mint gold/ore/XP on
/// the live path now — the deferred multi-venue follow-on this comment used to describe as future
/// work. Its four ores joined <c>MaterialRegistry.PricedPool</c> in the same re-baseline so a
/// returning hero's loot prices at the Evening reveal.</para>
///
/// Pure data: NO Godot reference, NO RNG, integer-only (no floats, no transcendental <c>Math.*</c>).
/// Determinism-safe by construction (KTD2).
/// </summary>
public static class GloomwoodVenue
{
    /// <summary>Stable registry key for the Gloomwood venue (lowercase kebab).</summary>
    public const string Id = "gloomwood";

    /// <summary>The four Gloomwood ore material keys, floor 1 → 4 (rarity rises with depth). Its own,
    /// disjoint from the Mine's copper…adamant so no venue mints an ore another venue already mints.</summary>
    public const string Greenheart = "greenheart";
    public const string Amberpitch = "amberpitch";
    public const string Moonresin = "moonresin";
    public const string Heartwood = "heartwood";

    /// <summary>
    /// The Gloomwood, four floors deep. Gates 0/20/45/75 (non-decreasing); monster stats and rewards
    /// climb with depth; the boss (Old Mossjaw) is the heaviest. All values positive (conformance).
    /// Ore keys ascend greenheart → heartwood in rarity.
    /// </summary>
    public static readonly VenueDefinition Definition = Build();

    private static VenueDefinition Build()
    {
        var floors = ImmutableArray.CreateBuilder<VenueFloor>(4);

        var gate = new[] { 0, 20, 45, 75 };
        var kind = new[] { "Bramble Boar", "Lantern Moth", "The Wicker Shepherd", "Old Mossjaw" };
        var ore = new[] { Greenheart, Amberpitch, Moonresin, Heartwood };

        for (var floor = 1; floor <= 4; floor++)
        {
            floors.Add(new VenueFloor(
                Floor: floor,
                Gate: gate[floor - 1],
                MonsterKind: kind[floor - 1],
                MonsterHp: 20 + 14 * floor,
                MonsterAttack: 6 + 5 * floor,
                MonsterDefense: 3 + 2 * floor,
                GoldPerKill: 6 + 4 * floor,
                OreKey: ore[floor - 1]));
        }

        // EntryPower 35: the MID venue (grade 8-11 ores). 35 is the honest-baseline day-1-10
        // median party power (measured 2026-08-01, 20-seed sweep) — a party crosses into the
        // Gloomwood band about when it outgrows floor-1/2 starter content, matching this venue's
        // own floor-3 gate (45) being the next real wall.
        return new VenueDefinition(Id, "The Gloomwood", floors.ToImmutable(), EntryPower: 35);
    }
}
