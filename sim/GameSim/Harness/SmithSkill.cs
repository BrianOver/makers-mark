using GameSim.Contracts;

namespace GameSim.Harness;

/// <summary>
/// A deterministic, RNG-free smith-skill grade (U1, plan 2026-08-13-002) — the harness's answer
/// to "how good is this smith at the minigame the harness never plays." <see cref="BaselinePlayer"/>
/// auto-crafts everything (<c>CraftAction.PerformanceGrade = null</c>), so every craft it has ever
/// produced resolved through <c>QualityRoller.RollActive</c>'s auto-craft path: pinned at 550 ± 25
/// jitter, Common or Fine, forever. This type gives the harness two named skill profiles so a
/// wrapper (<see cref="SkilledSmithPlayer"/>, U2) can stamp a believable <c>PerformanceGrade</c>
/// in <see cref="BaselinePlayer"/>'s place.
///
/// <para><b>Deliberately excludes recipe tier.</b> <see cref="BaselinePlayer"/> always tries the
/// highest-tier LEGAL recipe first, and high tiers unlock only late in a campaign — so day and
/// recipe tier are near-collinear in real play. Feeding tier into this derivation risks a grade
/// that is effectively a fixed per-tier offset: a harness artifact wearing a new costume, exactly
/// what this plan exists to remove. Rather than carefully decorrelating tier from day, this
/// derivation never reads a recipe or tier at all — <see cref="Grade"/> takes only
/// <see cref="GameState"/>. There is no tier input for it to plateau on.</para>
///
/// <para><b>Ordinal source: <see cref="GameState.NextItemId"/>.</b> <c>GameState</c> exposes no
/// craft counter. <c>NextItemId</c> is not craft-exclusive — it also advances for heirloom
/// reforges (<c>HeirloomHandlers</c>), legendary commissions, masterwork attempts, and the
/// rival's Morning restock (<c>RivalRestockSystem</c>, every Morning, a variable number of lines
/// depending on what sold) — but that impurity is exactly what makes it a usable ordinal here: it
/// does NOT climb in lockstep with <see cref="GameState.Day"/> the way a hand-rolled craft counter
/// would, because how many other items were minted between one Expedition-phase craft and the
/// next varies with sales and restock timing, not with the calendar alone. Mixed with
/// <see cref="GameState.Day"/> through an integer hash, the two vary per craft independently of
/// anything tier-shaped.</para>
///
/// <para>Pure: no RNG (does not take or reference <see cref="IDeterministicRng"/> anywhere), no
/// wall clock, integer arithmetic only (hard rule 4) — the mix below is bitwise/multiplicative
/// (the "lowbias32" integer-hash family), never a transcendental <c>Math.*</c> call.</para>
/// </summary>
public sealed record SmithSkill(int Centre, int Spread)
{
    /// <summary>Straddles Common/Fine with an occasional Poor roll — AE2's struggling player.
    /// Range [180, 740]: mostly Common (200-549), substantial Fine (550-740), a rare sliver of
    /// Poor (180-199) — never reaches Superior on its own merits (see KTD2's lifted-cap risk).</summary>
    public static readonly SmithSkill Novice = new(Centre: 460, Spread: 280);

    /// <summary>Superior is the common outcome; a good roll reaches Masterwork — AE1's baseline.
    /// Range [750, 950]: a Fine fringe at the bottom, Superior (780-929) for most of the range,
    /// Masterwork (930+) reachable at the top — three bands, never confined to two adjacent ones.</summary>
    public static readonly SmithSkill Veteran = new(Centre: 850, Spread: 100);

    /// <summary>
    /// The per-craft performance grade for <paramref name="state"/> under this profile — inside
    /// <c>[Centre - Spread, Centre + Spread]</c> by construction (both named profiles keep that
    /// interval inside <c>[0, 1000]</c> already; the clamp below is belt-and-braces, never
    /// load-bearing — <c>RollActive</c>'s own clamp is never what saves this value). Pure function
    /// of <see cref="GameState.Day"/> and <see cref="GameState.NextItemId"/> — same state, same
    /// profile, same grade, every call, forever (R4/AE3). Deliberately does not take a recipe or
    /// tier (see class doc).
    /// </summary>
    public int Grade(GameState state)
    {
        var mixed = Mix(state.Day, state.NextItemId);
        var span = (uint)(2 * Spread + 1);
        var offset = (int)(mixed % span) - Spread;
        var grade = Centre + offset;
        return grade < 0 ? 0 : grade > 1000 ? 1000 : grade;
    }

    /// <summary>
    /// Integer-only hash mix (the "lowbias32" family — bit shifts, XOR, unsigned multiplication;
    /// no transcendental <c>Math.*</c>, no floating point) of two campaign facts into one
    /// well-distributed 31-bit value. Unsigned arithmetic wraps deterministically on overflow
    /// (no <c>checked</c> hazard, no cross-OS float drift).
    /// </summary>
    private static uint Mix(int day, int ordinal)
    {
        var x = ((uint)day * 0x9E3779B1u) ^ ((uint)ordinal * 0x85EBCA6Bu);
        x ^= x >> 16;
        x *= 0x7FEB352Du;
        x ^= x >> 15;
        x *= 0x846CA68Bu;
        x ^= x >> 16;
        return x & 0x7FFFFFFFu;
    }
}
