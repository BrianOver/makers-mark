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
/// <para><b>Registered, not live.</b> The venue registers into <c>VenueRegistry.All</c> but NOT into
/// <c>VenueRegistry.LiveRotation</c> (frozen at the Mine): no hero party raids it, so its floors mint
/// nothing on any live path and the Balance bands cannot move. Going live is the deferred multi-venue
/// follow-on (wave-D row D8). Until then this definition is exercised only by its own tests, which
/// drive it through the real resolver + attribution engine without registration (the add-on shape).</para>
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

        return new VenueDefinition(Id, "The Gloomwood", floors.ToImmutable());
    }
}
