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
    /// gate 0/15/35/60/70 — floor 5 re-gated 2026-08-10, see <see cref="BuildMine"/>'s own comment;
    /// kinds Cave Rat/Tunnel Spider/Deep Ghoul/Ore Golem/The Forgeworm; HP 12+10*f except floor 5
    /// (50); attack 5+6*f except floor 5 (26); defense 2+2*f; gold 5+3*f; ore
    /// copper/iron/steel/mithril/adamant). Floors 1-4 and every other floor-5 number stay
    /// byte-identical to the old static table, pinned by <c>VenueConformanceTests</c>.
    /// </summary>
    public static readonly VenueDefinition Mine = BuildMine();

    /// <summary>All registered venues, keyed by id. Sorted (Ordinal) for deterministic iteration.</summary>
    public static readonly ImmutableSortedDictionary<string, VenueDefinition> All = new[]
    {
        Mine,
        Emberfall.EmberfallFoundryVenue.Definition,
        Gloomwood.GloomwoodVenue.Definition,
        SunkenCrypt.SunkenCryptVenue.Definition,
    }.ToImmutableSortedDictionary(v => v.Id, v => v, StringComparer.Ordinal);

    /// <summary>
    /// The venues that are LIVE — the ones hero parties actually raid. THIS IS THE LIVE-VENUE
    /// CONTRACT (same rule as <c>ClassRegistry.RecruitPool</c>): a registered venue is NOT
    /// automatically live just by being in <see cref="All"/>.
    ///
    /// <para>T1 content flip (docs/design/2026-07-26-overnight-strategy-synthesis.md, relands
    /// PR #242): <see cref="SunkenCrypt"/> joins the rotation as an early venue peer of the Mine
    /// (rank 0, grade 1-5 ores; the two split the early band by queue length) — its art set
    /// is complete (backdrop/entrance/all five monsters). <c>MaterialRegistry.PricedPool</c> flips
    /// in lockstep so returning ore is priceable at the Evening reveal, and
    /// <c>ClassRegistry.RecruitPool</c> opens the three remaining classes in the SAME window (the
    /// operating model's batch-the-re-baseliners rule).</para>
    ///
    /// <para><b><see cref="Emberfall"/> is LIVE (forward-ladder plan 2026-08-10-003 L4).</b> Rank 2,
    /// the ladder's endgame rung — a party reaches it only by graduating Gloomwood (clearing its
    /// bottom floor). Its art landed (backdrop + all five monster portraits, committed and in
    /// <c>godot/assets/art/art-manifest.json</c>) before this flip, so
    /// <c>VenueHubTests.VenueBackdropArt_Present_RendersRealArt_NotFallback</c> — the guard that
    /// FAILS on any live venue tile that falls back to placeholder — passes on real art, not an
    /// empty tile. Its ore ladder (firebrick..heartcoal) joined <c>MaterialRegistry.PricedPool</c> in
    /// the same re-baseline this comment describes for Gloomwood and the Crypt above.</para>
    /// </summary>
    public static readonly ImmutableArray<string> LiveRotation =
        ImmutableArray.Create(
            MineId,
            Gloomwood.GloomwoodVenue.Id,
            SunkenCrypt.SunkenCryptVenue.Id,
            Emberfall.EmberfallFoundryVenue.Id);

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
                    // Re-gated 2026-08-10 (forward-ladder plan 2026-08-10-003 L3, §11.8's fix):
                    // was 100, "above any rival-vendor loadout by design" — measured (this PR's
                    // Characterize tool, main seed 2026 + 10 sweep seeds, BaselinePlayer) to be a
                    // WALL, not a gate: party power plateaus at 55-78 by day ~20 and never crosses
                    // 100 in 100 days on any seed, so floor 5 (and Gloomwood, reachable only by
                    // graduating here) was permanently unreachable. 70 sits above floor 4's own
                    // gate (60, so floor 5 stays a REAL extra bar, never a same-gate coin-flip) and
                    // below the day-20+ plateau every seed reaches. TargetFloorFor keys strictly on
                    // a party's deepest-cleared record (+1), not on power, so the gate VALUE inside
                    // this range does not control the clear day at all — 50 and 62 measured
                    // byte-for-byte identical clear days to 70. What DID move the day-8..58 spread
                    // was the floor-5 MONSTER (see MonsterHp/MonsterAttack below): the formula value
                    // was deadly enough to kill the one veteran deep enough to attempt it, resetting
                    // progress until a new veteran emerged — some seeds took until day 58. Dialing
                    // the monster down (this PR) brought first-clear to day 12-18 on all 11 seeds,
                    // comfortably inside the plan's 8-18 day gate rule and above
                    // BalanceSimTests.NoFloor5BeforeDay's day-8 floor (see this PR's characterization
                    // tables, before/after).
                    5 => 70,
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
                // Floor 5's HP/Attack break from the 12+10f/5+6f formula (re-gated 2026-08-10,
                // plan 2026-08-10-003 L3): the formula value (HP 62, Attack 35) measured too
                // deadly at gate power for a fair 2-4 round fight (see BuildMine's gate comment
                // for the full measurement) — dialed to HP 50 / Attack 26 so a gate-power hero's
                // own damage clears it in 2-4 hits without the one-hit-adjacent lethality that was
                // driving repeated hero deaths (and the multi-week reset cycle that followed each
                // one). Defense and gold stay formula-exact.
                MonsterHp: floor == 5 ? 50 : 12 + 10 * floor,
                MonsterAttack: floor == 5 ? 26 : 5 + 6 * floor,
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

        // LadderRank 0: the Mine is the starter venue (and the structural bounty home, R18) —
        // every fresh hero is at rank 0 from day one.
        return new VenueDefinition(MineId, "The Mine", floors.ToImmutable(), LadderRank: 0);
    }
}
