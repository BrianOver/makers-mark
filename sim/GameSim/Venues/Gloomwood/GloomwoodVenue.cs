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
/// Gates 0/20/45/73 are non-decreasing with depth (conformance; floor 4 re-gated 2026-08-10, see
/// <c>Build</c>'s own comment). Its four ore keys —
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
    /// The Gloomwood, four floors deep. Gates 0/20/45/73 (non-decreasing, floor 4 re-gated
    /// 2026-08-10 — see <see cref="Build"/>'s own comment); monster stats and rewards climb with
    /// depth; the boss (Old Mossjaw) is the heaviest. All values positive (conformance). Ore keys
    /// ascend greenheart → heartwood in rarity.
    /// </summary>
    public static readonly VenueDefinition Definition = Build();

    private static VenueDefinition Build()
    {
        var floors = ImmutableArray.CreateBuilder<VenueFloor>(4);

        var gate = new[] { 0, 20, 45, 73 };
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

        // LadderRank 1 (the forward ladder, owner ruling 2026-08-10, plan 2026-08-10-003 L1):
        // Gloomwood is the first rung past the Mine/Crypt starter tier — a party reaches it only
        // by graduating (Hero.LadderRank incrementing on a rank-0 venue's bottom-floor clear), not
        // by a power reading that could wobble back down. This REPLACES the deleted EntryPower
        // power-band field (was 72, tuned against a continuous party-power signal that saturated
        // below the Mine's floor-5 gate and permanently stole mid-power parties before they ever
        // finished a 5-floor venue — the §11.8 routing trap; the tuning history for that dead
        // mechanism lives in git, not here).
        //
        // Floor 4 (Old Mossjaw, the boss) re-gated 75 -> 73, L3 (2026-08-10). MEASURED READING,
        // documented here per §11.6 rule 5: TargetFloorFor keys on a hero's GLOBAL
        // DeepestFloorReached (shared across every venue), so a party graduating the Mine at floor
        // 5 targets Gloomwood's floor 4 (its OWN deepest) on the very FIRST trip — floors 1-3's low
        // gates (0/20/45) are trivially passed en route and never the practical bottleneck; only
        // the boss gate controls pacing. Characterized (this PR's Characterize tool, main seed 2026
        // + 10 sweep seeds) across five candidate gates: 90 and 80 stranded most seeds for the
        // full 100-day window (party power plateaus 74-81 without rung-1 gear — a WALL, same
        // failure class as the pre-L3 Mine); 76 stranded 2 of 11 and spread the rest 3-35 days; 70
        // reached every seed but in only 1-2 days (too fast). 73 is the one value where all 11
        // seeds clear AND every seed takes at least 1 day: measured deltas were 1-4 days after
        // graduation (not 8-12 as the plan's own proposal named) with zero deaths on every seed —
        // a graduating party's power (measured 70-74) sits close enough to the practical Tier-3-gear
        // ceiling (measured plateau ~74-81 absent rung-1 gear) that there is little room to engineer
        // a longer, universally-safe gap without a gear-economy change outside L3's scope. Reported
        // in this PR's characterization tables, flagged for L6/a later wave rather than widened here.
        return new VenueDefinition(Id, "The Gloomwood", floors.ToImmutable(), LadderRank: 1);
    }
}
