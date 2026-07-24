using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using GameSim.Flavor;

namespace GameSim.Crafting;

/// <summary>
/// Wave 4 (U19, "Signed Works", plan 2026-07-24-003): the rare, deterministic proc that turns an
/// ordinary craft into a named artifact — "your craft writes the legends" made literal. A Signed
/// Work needs no new field beyond <see cref="Item.SignedName"/> (already shipped, Wave 4a
/// contracts micro-PR): its growing <see cref="Item.History"/> and attribution deeds (appended by
/// <c>ExpeditionRevealSystem</c> off <c>AttributionBeatEvent</c>s) ARE its inscription.
///
/// <para><b>Proc condition</b> (pure, integer, RNG-free — KTD4): a Masterwork craft where every
/// one of the three <see cref="Item.CraftSubScores"/> (smelt/forge/quench, per-mille, captured by
/// the active-craft minigame) clears <see cref="SubScoreThreshold"/>. This reads data the craft
/// ALREADY produced this call — no extra roll, no new draw, so <c>CraftingHandlers</c>' one-
/// <c>Roll100</c>-per-craft contract is untouched. An auto-craft or a passive-profession craft
/// carries an EMPTY <see cref="Item.CraftSubScores"/> (never 3 entries — see the property's own
/// doc) and can never qualify: only a genuinely excellent, player-executed active forge earns a
/// signature. Rare by construction: Masterwork is already the top 1% base-odds band
/// (<see cref="QualityRoller"/>), and demanding all three sub-scores at
/// <see cref="SubScoreThreshold"/>+ on top of it keeps signings scarce.</para>
///
/// <para><b>Name derivation</b> (the <see cref="VoiceProfile"/> hash trick): a frozen, APPEND-ONLY
/// legend-name pool picked via <see cref="StableHash.Avalanche"/> over (campaign identity —
/// <c>GameState.Rng.Inc</c>, the same seed-derived, campaign-constant identity <c>VoiceProfile</c>
/// uses — the item's own id, its recipe id, and the craft day). Deterministic: the SAME craft
/// (same campaign, same minted item id, same recipe, same day) always earns the SAME name; no
/// RNG draw, no wall clock. Flavor identity ONLY — the name never feeds a sim rule.</para>
/// </summary>
public static class ArtifactSigning
{
    /// <summary>Per-mille floor every one of the three forge-beat sub-scores must clear to sign.</summary>
    public const int SubScoreThreshold = 950;

    /// <summary>
    /// Frozen legend-name pool, in frozen pick order (the <see cref="VoiceProfile.Voices"/>
    /// contract): append-only, never reorder or remove existing entries — reordering would change
    /// which name an existing (campaign, item id, recipe, day) tuple picks.
    /// </summary>
    private static readonly ImmutableArray<string> LegendNames = ImmutableArray.Create(
        "Emberfall", "Widowsong", "Duskbrand", "Ashenvow", "Grimtide", "Suncaller",
        "Moonwrought", "Ironsong", "Wyrmsbane", "Hollowmourn", "Starfall's Edge", "Nightforge");

    /// <summary>
    /// True iff <paramref name="item"/> earns a signature this craft: not already signed,
    /// Masterwork quality, and all three forge-beat sub-scores clear <see cref="SubScoreThreshold"/>.
    /// The <see cref="Item.IsSigned"/> guard is belt-and-braces — callers invoke this exactly once,
    /// immediately after minting, when a fresh item can never already carry a name.
    /// </summary>
    public static bool Qualifies(Item item) =>
        !item.IsSigned
        && item.Quality == QualityGrade.Masterwork
        && item.CraftSubScores.Count == 3
        && item.CraftSubScores.All(score => score >= SubScoreThreshold);

    /// <summary>The legend name this craft earns — a pure function of campaign identity and this
    /// item's own ids/day; never RNG, never the wall clock (KTD4).</summary>
    public static string LegendName(ulong campaignId, ItemId itemId, string recipeId, int day)
    {
        var pick = StableHash.Avalanche(StableHash.Mix(
            campaignId,
            unchecked((ulong)itemId.Value),
            StableHash.HashString(recipeId),
            unchecked((ulong)day)));
        return LegendNames[(int)(pick % (ulong)LegendNames.Length)];
    }
}
