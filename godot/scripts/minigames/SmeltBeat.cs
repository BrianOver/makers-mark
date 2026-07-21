using System;

namespace GodotClient.Minigames;

/// <summary>
/// PA6 beat 1/3 (spec §Blacksmith minigame; Spiritfarer comparable): a heat gauge rises at a
/// fixed per-mille-per-second rate — no engine RNG, no wall-clock, purely a function of the
/// accumulated <see cref="Advance"/> clock — and the player calls <see cref="Stop"/> to pull the
/// stock the instant it reads the sweet zone. Over/under-heat (stopping outside the band, or
/// never stopping before <see cref="TimeoutSeconds"/> elapses) records an <see cref="Impurity"/>
/// that <see cref="ForgeBeat"/> carries forward as visible dross (Jacksmith carry-forward flaw).
/// Sub-score is the per-mille distance from the band center, clamped into [0, 1000].
///
/// <para>Deterministic by construction: the same delta sequence and the same <see cref="Stop"/>
/// timing always produce the same <see cref="SubScorePermille"/> — no randomness anywhere in this
/// class (KTD2 — the sim is the only place a real dice roll lives).</para>
/// </summary>
public sealed class SmeltBeat
{
    /// <summary>Sweet-zone center, per-mille — deliberately off the gauge's midpoint (620, not
    /// 500) so both an under-heat AND an over-heat stop are reachable within the [0,1000] gauge.</summary>
    public const int BandCenterPermille = 620;

    /// <summary>The band-boundary sub-score floor (an outside-the-band stop is never a total
    /// wipe — soft failure per the Spiritfarer comparable, never destruction).</summary>
    private const int ImpuritySubScoreCeiling = 700;

    public int BandWidthPermille { get; }
    public int RisePermilliePerSecond { get; }
    public double TimeoutSeconds { get; }

    public int HeatPermille { get; private set; }
    public double Elapsed { get; private set; }
    public bool Complete { get; private set; }

    /// <summary>True when the beat ended by timing out (never <see cref="Stop"/>-ped) — the
    /// belt-and-braces path: an unattended smelt always resolves, never hangs the minigame.</summary>
    public bool Overheated { get; private set; }

    /// <summary>True when the finishing heat landed outside the sweet zone — the carry-forward
    /// flaw <see cref="ForgeBeat"/> renders as dross.</summary>
    public bool Impurity { get; private set; }

    public int SubScorePermille { get; private set; }

    public SmeltBeat(int bandWidthPermille, int risePermilliePerSecond, double timeoutSeconds)
    {
        BandWidthPermille = Math.Max(40, bandWidthPermille);
        RisePermilliePerSecond = Math.Max(1, risePermilliePerSecond);
        TimeoutSeconds = Math.Max(0.5, timeoutSeconds);
    }

    /// <summary>Advance the heat gauge by <paramref name="delta"/> accumulated-clock seconds
    /// (never wall-clock — the caller's own <c>Advance(double)</c> drives this, mirroring
    /// <c>ShopStage.Advance</c>'s house pattern). No-op once <see cref="Complete"/>.</summary>
    public void Advance(double delta)
    {
        if (Complete)
        {
            return;
        }

        HeatPermille = Math.Min(1000, HeatPermille + (int)Math.Round(RisePermilliePerSecond * delta));
        Elapsed += delta;
        if (Elapsed >= TimeoutSeconds || HeatPermille >= 1000)
        {
            Overheated = true;
            Finish();
        }
    }

    /// <summary>The one player input this beat reads: pull the stock now. Scores off the CURRENT
    /// heat — no-op once <see cref="Complete"/> (a second Stop can never re-score).</summary>
    public void Stop()
    {
        if (!Complete)
        {
            Finish();
        }
    }

    private void Finish()
    {
        Complete = true;
        var half = BandWidthPermille / 2;
        var distance = Math.Abs(HeatPermille - BandCenterPermille);
        Impurity = distance > half;
        SubScorePermille = Impurity
            ? Math.Clamp(ImpuritySubScoreCeiling - (distance - half) * 2, 0, ImpuritySubScoreCeiling)
            : Math.Clamp(1000 - (distance * (1000 - ImpuritySubScoreCeiling) / Math.Max(1, half)), ImpuritySubScoreCeiling, 1000);
    }
}
