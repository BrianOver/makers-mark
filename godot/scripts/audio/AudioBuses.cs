using Godot;

namespace GodotClient.Audio;

/// <summary>
/// The mix's bus graph — <c>Master</c> (limited) with three category buses hanging off it:
/// <c>Music</c>, <c>Sfx</c> (with a quieter <c>SfxLoop</c> child for held/looping cues) and
/// <c>Narrator</c>. U-T4-1: before this unit there were no buses at all — every
/// <see cref="AudioStreamPlayer"/> the game owns mixed straight onto the engine's default
/// <c>Master</c> bus at 0 dB with nothing catching peaks, which is how a 47 dB spread between the
/// loudest cue and the quietest bed went unnoticed.
///
/// <para><b>Built in code, not in <c>project.godot</c>.</b> That file is deny-listed for this repo's
/// agents, and no <c>default_bus_layout.tres</c> is committed either — <see cref="EnsureBuilt"/> is
/// the whole bus layout, and it runs once per process from <see cref="AudioDirector._Ready"/>.</para>
///
/// <para><b>Category mastering lives here now, not on the players.</b> A bus's <c>VolumeDb</c> is the
/// category level (this table); a player's own <c>VolumeDb</c> stays the PREFERENCE layer — the
/// four-fader math in <see cref="AudioDirector.MixGainDb"/> plus each composed track's own
/// <c>TrimDb</c> — never collapsed into this one. See <see cref="AudioDirector.MusicTargetDb"/> and
/// <see cref="AudioDirector.RefreshNarratorVolume"/> for where the two layers add back together.</para>
/// </summary>
public static class AudioBuses
{
    public const string Master = "Master";
    public const string Music = "Music";
    public const string Sfx = "Sfx";
    public const string SfxLoop = "SfxLoop";
    public const string Narrator = "Narrator";

    /// <summary>
    /// Music category level in dB. Replaces the old per-player <c>AudioDirector.MusicDb</c> (-22) —
    /// moved off the player and onto this bus by U-T4-1, and nudged to -20 as part of that move. The
    /// full music-bed budget (source -12 through this bus's -20 for an effective -32) is
    /// §11.14 T4's spec; later T4 units land the source-side half, not this one.
    /// </summary>
    public const float MusicBusDb = -20f;

    /// <summary>SFX category level in dB. Unity gain — one-shot cues are mastered at the source
    /// (later T4 units), not attenuated again here.</summary>
    public const float SfxBusDb = 0f;

    /// <summary>
    /// Held/looping cues (the bellows) sit quieter than one-shot SFX by default, so a loop competing
    /// at full SFX level would drown a one-shot layered over it. Sends into <see cref="Sfx"/>, not
    /// straight to <see cref="Master"/>, so a future SFX-wide duck or fader reaches loops too.
    /// </summary>
    public const float SfxLoopBusDb = -3f;

    /// <summary>
    /// Narrator category level in dB. Replaces the old per-player <c>AudioDirector.NarratorDb</c>
    /// (-14) — moved off the player and onto this bus by U-T4-1. Unity gain here on purpose: the
    /// narrator no longer needs its own negative headroom now that the shared Master limiter (see
    /// <see cref="EnsureBuilt"/>) is the thing catching peaks across every bus, and the old -14 was
    /// exactly why the narrator — the game's emotional payload — measured below nine UI cues. The
    /// lines themselves are baked to a fixed loudness at content time (U-T4-6 masters them to that
    /// target); this bus is not where per-line level lives.
    /// </summary>
    public const float NarratorBusDb = 0f;

    private const float LimiterCeilingDb = -1.0f;
    private const float LimiterPreGainDb = 0f;
    private const float LimiterReleaseSeconds = 0.100f;

    /// <summary>
    /// Builds the bus graph if it is not already built. <b>Idempotent and name-keyed</b> — every bus
    /// is looked up by name first and created only on a miss, and a bus is never removed. That is not
    /// a style preference: the engine test suite constructs many <see cref="AudioDirector"/>s against
    /// one process-wide <see cref="AudioServer"/>, and a non-idempotent build would leave fifty buses
    /// behind and a false-green graph test in front of them.
    /// </summary>
    public static void EnsureBuilt()
    {
        EnsureBus(Master, sendTo: null, volumeDb: null);
        EnsureBus(Music, sendTo: Master, volumeDb: MusicBusDb);
        EnsureBus(Sfx, sendTo: Master, volumeDb: SfxBusDb);
        EnsureBus(SfxLoop, sendTo: Sfx, volumeDb: SfxLoopBusDb);
        EnsureBus(Narrator, sendTo: Master, volumeDb: NarratorBusDb);

        EnsureLimiter();
    }

    /// <summary>Creates <paramref name="name"/> only if <see cref="AudioServer.GetBusIndex"/> cannot
    /// already find it — the whole idempotency guard. <c>Master</c> always exists by default (index
    /// 0), so this is a no-op for it every call; its send has no target and its own volume is left at
    /// the engine default rather than overwritten.</summary>
    private static void EnsureBus(string name, string? sendTo, float? volumeDb)
    {
        if (AudioServer.GetBusIndex(name) != -1)
            return;

        AudioServer.AddBus();
        var index = AudioServer.BusCount - 1;
        AudioServer.SetBusName(index, name);
        if (sendTo != null)
            AudioServer.SetBusSend(index, sendTo);
        if (volumeDb != null)
            AudioServer.SetBusVolumeDb(index, volumeDb.Value);
    }

    /// <summary>The one thing standing between this mix and clipping: a hard ceiling on the bus every
    /// category eventually sends to. Guarded the same way as <see cref="EnsureBus"/> — only added when
    /// <c>Master</c> does not already carry an effect — so calling <see cref="EnsureBuilt"/> twice
    /// never stacks a second limiter on top of the first.</summary>
    private static void EnsureLimiter()
    {
        var masterIndex = AudioServer.GetBusIndex(Master);
        if (AudioServer.GetBusEffectCount(masterIndex) > 0)
            return;

        var limiter = new AudioEffectHardLimiter
        {
            CeilingDb = LimiterCeilingDb,
            PreGainDb = LimiterPreGainDb,
            Release = LimiterReleaseSeconds,
        };
        AudioServer.AddBusEffect(masterIndex, limiter);
    }
}
