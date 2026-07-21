using System;

namespace GodotClient.Minigames;

/// <summary>
/// PA6 beat 3/3 (spec §Blacksmith minigame; Spiritfarer comparable): a color/temperature readout
/// oscillates (a deterministic sine wave over the accumulated clock — no engine RNG) and the
/// player calls <see cref="Lock"/> to plunge the stock the instant it reads the target. Locking
/// near <see cref="TargetPermille"/> (the "just right" quench) scores highest; locking early reads
/// as brittle, late as soft — this class only tracks the per-mille distance (the flavor text is
/// the overlay's presentation concern, not this beat's). Never locking before
/// <see cref="TimeoutSeconds"/> elapses is the belt-and-braces path: the beat still resolves
/// (worst-case score), never hangs the minigame.
///
/// <para>Deterministic by construction: <see cref="Advance"/>'s oscillation is a pure function of
/// the accumulated clock, so the same delta sequence and the same <see cref="Lock"/> timing always
/// reproduce the identical <see cref="SubScorePermille"/>.</para>
/// </summary>
public sealed class QuenchBeat
{
    /// <summary>Dead-center of the readout — the "just right" quench moment.</summary>
    public const int TargetPermille = 500;

    public double OscillationHz { get; }
    public int BandWidthPermille { get; }
    public double TimeoutSeconds { get; }

    public double Elapsed { get; private set; }
    public int NeedlePermille { get; private set; } = TargetPermille;
    public bool Complete { get; private set; }
    public bool Locked { get; private set; }
    public int SubScorePermille { get; private set; }

    public QuenchBeat(double oscillationHz, int bandWidthPermille, double timeoutSeconds)
    {
        OscillationHz = Math.Max(0.05, oscillationHz);
        BandWidthPermille = Math.Max(40, bandWidthPermille);
        TimeoutSeconds = Math.Max(0.5, timeoutSeconds);
    }

    public void Advance(double delta)
    {
        if (Complete)
        {
            return;
        }

        Elapsed += delta;
        var phase = Elapsed * OscillationHz * 2.0 * Math.PI;
        NeedlePermille = TargetPermille + (int)Math.Round(TargetPermille * Math.Sin(phase));

        if (Elapsed >= TimeoutSeconds)
        {
            Finish();
        }
    }

    /// <summary>Plunge now — scores off the CURRENT needle reading. No-op once complete.</summary>
    public void Lock()
    {
        if (Complete)
        {
            return;
        }

        Locked = true;
        Finish();
    }

    private void Finish()
    {
        Complete = true;
        if (!Locked)
        {
            // Never locked before timeout — worst case, but the beat still resolves (no hang).
            SubScorePermille = 0;
            return;
        }

        var half = BandWidthPermille / 2;
        var distance = Math.Abs(NeedlePermille - TargetPermille);
        SubScorePermille = distance <= half
            ? Math.Clamp(1000 - (distance * 300 / Math.Max(1, half)), 700, 1000)
            : Math.Clamp(700 - (distance - half) * 2, 0, 700);
    }
}
