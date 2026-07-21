using System;

namespace GodotClient.Minigames;

/// <summary>
/// PA6 beat 2/3 (spec §Blacksmith minigame; Fantasy Life comparable): a shaping-progress budget
/// to fill via rhythmic <see cref="Strike"/>s before <see cref="CoolSeconds"/> elapses. A strike
/// landing within <see cref="OnBeatWindowSeconds"/> of a metronome pulse (period
/// <see cref="BeatPeriodSeconds"/>) fills the budget faster and cleanly; an off-beat strike fills
/// less and mars the piece (partially forgiven by <see cref="OffBeatForgivenessPermille"/> —
/// talent-assist data the host reads from <c>ProfessionDefinition.MinigameAssists</c>). Finishing
/// EARLY (progress hits the budget before the glow cools) reads as "heat to spare" — a skilled
/// run in fewer strikes — and is rewarded; finishing on the cooling timeout scores off however far
/// progress got.
///
/// <para><b>Carry-forward flaw (Jacksmith comparable):</b> a smelt impurity from
/// <see cref="SmeltBeat"/> renders here as visible dross (<see cref="HasDross"/>, set for the
/// whole beat's lifetime — never a hidden penalty) AND debits this beat's own sub-score ceiling
/// (<see cref="ScoreCapPermille"/>) — the chain is causal, not three independent scores.</para>
///
/// <para>Deterministic by construction: <see cref="Advance"/>/<see cref="Strike"/> are pure
/// functions of the accumulated clock and the strike history — no engine RNG anywhere.</para>
/// </summary>
public sealed class ForgeBeat
{
    private const int ProgressBudgetPermille = 1000;
    private const int OnBeatFillPermille = 220;
    private const int OffBeatFillPermille = 90;

    /// <summary>Carry-forward flaw (PKD8/Jacksmith): a smelt impurity caps this beat below a
    /// clean run's ceiling — the dross is a real quality debit, not just a visual.</summary>
    private const int DrossScoreCap = 780;

    private const int MarPenaltyPermille = 60;

    /// <summary>"Heat to spare" bonus (Fantasy Life comparable): per-mille credit per fraction of
    /// the cooling window still unspent at finish, rewarding fewer, cleaner strikes.</summary>
    private const int HeatToSpareBonusMax = 150;

    public double BeatPeriodSeconds { get; }
    public double OnBeatWindowSeconds { get; }
    public double CoolSeconds { get; }
    public int OffBeatForgivenessPermille { get; }
    public bool SmeltImpurity { get; }
    public int ScoreCapPermille { get; }

    public int ProgressPermille { get; private set; }
    public double Elapsed { get; private set; }
    public int StrikeCount { get; private set; }
    public int MarCount { get; private set; }
    public bool Complete { get; private set; }

    /// <summary>The carry-forward tell (Jacksmith): visible from beat start, not just at scoring —
    /// the overlay renders dross on the stock the instant this beat opens.</summary>
    public bool HasDross => SmeltImpurity;

    public int SubScorePermille { get; private set; }

    public ForgeBeat(
        double beatPeriodSeconds, double onBeatWindowSeconds, double coolSeconds,
        int offBeatForgivenessPermille, bool smeltImpurity)
    {
        BeatPeriodSeconds = Math.Max(0.1, beatPeriodSeconds);
        OnBeatWindowSeconds = Math.Max(0.02, onBeatWindowSeconds);
        CoolSeconds = Math.Max(0.5, coolSeconds);
        OffBeatForgivenessPermille = Math.Clamp(offBeatForgivenessPermille, 0, 1000);
        SmeltImpurity = smeltImpurity;
        ScoreCapPermille = smeltImpurity ? DrossScoreCap : 1000;
    }

    public void Advance(double delta)
    {
        if (Complete)
        {
            return;
        }

        Elapsed += delta;
        if (Elapsed >= CoolSeconds)
        {
            Finish();
        }
    }

    /// <summary>The player struck the anvil right now (judged against the CURRENT accumulated
    /// clock, never wall-clock). On-beat = within <see cref="OnBeatWindowSeconds"/> of the nearest
    /// metronome pulse.</summary>
    public void Strike()
    {
        if (Complete)
        {
            return;
        }

        StrikeCount++;
        var phase = Elapsed % BeatPeriodSeconds;
        var distanceToBeat = Math.Min(phase, BeatPeriodSeconds - phase);
        var onBeat = distanceToBeat <= OnBeatWindowSeconds;

        if (onBeat)
        {
            ProgressPermille = Math.Min(ProgressBudgetPermille, ProgressPermille + OnBeatFillPermille);
        }
        else
        {
            // Off-beat forgiveness (talent assist data): a per-mille fraction of the miss is
            // recovered as extra fill and, at full forgiveness, the mar itself is waived.
            var fill = OffBeatFillPermille + (OffBeatFillPermille * OffBeatForgivenessPermille / 1000);
            ProgressPermille = Math.Min(ProgressBudgetPermille, ProgressPermille + fill);
            if (OffBeatForgivenessPermille < 1000)
            {
                MarCount++;
            }
        }

        if (ProgressPermille >= ProgressBudgetPermille)
        {
            Finish();
        }
    }

    private void Finish()
    {
        Complete = true;
        var spareFraction = Elapsed < CoolSeconds ? (CoolSeconds - Elapsed) / CoolSeconds : 0.0;
        var heatToSpareBonus = (int)Math.Round(spareFraction * HeatToSpareBonusMax);
        var marPenalty = MarCount * MarPenaltyPermille;
        var raw = (ProgressPermille * 1000 / ProgressBudgetPermille) + heatToSpareBonus - marPenalty;
        SubScorePermille = Math.Clamp(raw, 0, ScoreCapPermille);
    }
}
