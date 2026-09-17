using GameSim.Contracts;
using GameSim.Heroes;

namespace GameSim.Drama;

/// <summary>
/// P2-PROOF-17 (§11.11): the three named parts <see cref="HeroXp.ForExpedition"/> already sums into
/// one number, so "the rank-up says which part of it your mark earned" is a real split the render
/// layer reads rather than a sentence it invents. Composed from EXACTLY <see
/// cref="HeroXp.ForExpedition"/>'s own two inputs — <see cref="ExpeditionResult.DeepestFloorCleared"/>
/// and a count over <see cref="ExpeditionResult.Beats"/> filtered to <see
/// cref="BeatType.KillingBlow"/>/<see cref="BeatType.LethalSave"/> for this hero, the SAME filter
/// <c>ExpeditionRevealSystem</c>'s own XP step already applies — never re-derived, and never widened
/// to every beat type: <see cref="BeatType.Provisioned"/>/<see cref="BeatType.PotionLifesave"/>/<see
/// cref="BeatType.BreakpointClear"/>/<see cref="BeatType.ToolAssist"/> earn no XP (see
/// <c>ExpeditionRevealSystem</c> step 5) and must earn no share of this split either.
///
/// <para>Pure: no state mutation, no event emission, no RNG draw, no wall clock — the same read-model
/// shape <see cref="ProvenanceQuery"/> and <see cref="FallenQuery"/> already keep. Also only ever
/// reads <c>result.Beats</c> — never a hero's lifetime <see cref="Hero.Memories"/> tally, for the
/// exact double-counting reason <see cref="HeroXp"/>'s own doc comment names.</para>
/// </summary>
public static class XpSplitQuery
{
    /// <summary>
    /// One hero's XP grant for one expedition, broken into its named parts. <see cref="Total"/> is
    /// pinned equal to <see cref="HeroXp.ForExpedition"/> over the same two inputs
    /// (<c>XpSplitQueryTests</c>) — a number on screen that could disagree with the number the sim
    /// applied is worse than no number at all.
    /// </summary>
    public sealed record Split(int SurviveXp, int FloorsCleared, int FloorXp, int CreditedBeats, int BeatXp)
    {
        public int Total => SurviveXp + FloorXp + BeatXp;
    }

    /// <summary>
    /// The split for <paramref name="hero"/> on this expedition, or null when they are not among
    /// <see cref="ExpeditionResult.Survivors"/> — <c>ExpeditionRevealSystem</c> step 5 grants XP to
    /// survivors only, so a fallen hero's card gets no split rather than a zeroed-out one implying a
    /// grant that never happened. The same honest-empty-state contract <see cref="ProvenanceQuery"/>
    /// and <see cref="FallenQuery"/> keep everywhere: callers render nothing for a null result, never
    /// a generic fallback line.
    /// </summary>
    public static Split? For(ExpeditionResult result, HeroId hero)
    {
        if (!result.Survivors.Contains(hero))
        {
            return null;
        }

        var creditedBeats = 0;
        foreach (var beat in result.Beats)
        {
            if (beat.Hero == hero && beat.Beat is BeatType.KillingBlow or BeatType.LethalSave)
            {
                creditedBeats++;
            }
        }

        var floorsCleared = Math.Max(0, result.DeepestFloorCleared);
        return new Split(
            HeroXp.SurviveXp,
            floorsCleared,
            floorsCleared * HeroXp.PerFloorXp,
            creditedBeats,
            creditedBeats * HeroXp.PerBeatXp);
    }
}
