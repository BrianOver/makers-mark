using System;
using System.Collections.Generic;
using System.Linq;
using GameSim.Contracts;
using GameSim.Presentation;
using Godot;
using GodotClient.Tools;
using GodotClient.Ui;

namespace GodotClient.Audio;

/// <summary>
/// The one place the game makes noise. Owns a small pool of SFX players plus two music players, and
/// exposes <see cref="Play"/> / <see cref="SetPhase"/> as the whole API.
///
/// <para><b>Why a pool.</b> A single <see cref="AudioStreamPlayer"/> cuts its own tail off when
/// retriggered, so a rapid sequence (hammer strikes, a row of coin sounds) would clip into a stutter.
/// Round-robining a handful of players lets cues overlap and decay naturally. The pool is small and
/// fixed: exceeding it steals the oldest voice, which is the correct failure — dropping the newest
/// sound would make the game feel unresponsive precisely when a lot is happening.</para>
///
/// <para><b>Why two music players.</b> The bed changes with the phase, and hard-swapping a droning
/// stream is an audible lurch. Two players crossfade over <see cref="CrossfadeSeconds"/> — one fading
/// out while the other comes up — so the transition reads as the light changing rather than a track
/// ending.</para>
///
/// <para><b>Headless-safe.</b> Godot's dummy audio driver accepts <c>Play()</c> with no output device, so
/// nothing here needs guarding for tests or CI. Volumes are applied per-player in dB rather than through
/// a custom bus, so no project-level audio bus layout is required (<c>project.godot</c> is deny-listed).</para>
/// </summary>
public sealed partial class AudioDirector : Node
{
    /// <summary>Concurrent SFX voices. Six covers the busiest realistic moment (a craft finishing while
    /// the bell rings and a panel closes) with room to spare.</summary>
    private const int VoiceCount = 6;

    /// <summary>How long a phase's music takes to become the next phase's. Slow on purpose — this is
    /// ambience, and a fast fade would draw attention to itself.</summary>
    private const float CrossfadeSeconds = 2.5f;

    /// <summary>
    /// Bed level in dB. Well under the SFX so cues always read over the music.
    ///
    /// <para>Was -16, which the owner heard as "a little loud". -22 is roughly half the perceived loudness
    /// (about 6dB per halving) and is the right target for something that plays continuously and is never
    /// the thing being listened to. An options slider is the real answer; until then this errs quiet,
    /// because ambience nobody notices is working and ambience someone turns off is not.</para>
    /// </summary>
    private const float MusicDb = -22f;

    private const float SilentDb = -60f;

    /// <summary>One entry in <see cref="ComposedTracks"/>: which phase it replaces, and how far to
    /// trim it relative to <see cref="MusicDb"/> so it reads at the same loudness as the bed it
    /// stands in for. See the table's own doc comment for where <see cref="TrimDb"/> came from.</summary>
    private readonly record struct ComposedTrack(string Id, string ResourcePath, float TrimDb);

    /// <summary>
    /// Which composed track, if any, replaces the synth bed for a phase. A DATA TABLE, not a switch
    /// full of ifs — remapping a track to a different phase, or handing a phase back to the synth
    /// bed, is a one-line edit here and nothing else. That matters because the owner's post-sitting
    /// verdicts (keep / revert / remap) need to be exactly that cheap.
    ///
    /// <para><b>U4 (playtest-three plan) closed the Morning gap U2 stated on purpose.</b> U2 shipped
    /// three composed tracks and explicitly left Morning on the synth bed — no existing mood fit an
    /// opening, brightest-of-the-day phase without new generation. The owner then rejected the
    /// synthesized Morning bed a THIRD time (#327 had already retuned its bass and loop length) —
    /// proof the problem was synthesis-vs-composition, not a mix setting, so U4 generates
    /// <c>day-first-light</c> instead of retuning <see cref="MusicBed"/> again. All five
    /// <see cref="DayPhase"/> values now carry a composed entry; <see cref="MusicBed"/> remains the
    /// fallback for a missing/unpulled-LFS file and stays the Mine's own
    /// <see cref="MusicBed.Underground"/> theme untouched (no mine track this round).</para>
    ///
    /// <para><b>U6 (2026-08-02 shell-and-audio plan) reverted Camp back to <c>night-still</c>.</b>
    /// <c>night-still-long.mp3</c> measured -27.15 LUFS raw — quieter than the original 60s file's
    /// own -21.7 raw despite the identical brief (ambient generation is not byte-reproducible across
    /// a 3x-longer render even holding style constant) — and needed a +5.45dB BOOST to reach -21.7
    /// effective, the one entry this table ever carried a POSITIVE TrimDb for. A windowed loudness
    /// pass (ffmpeg <c>astats</c>, 10s frames; the forensics doc that recorded it has since been
    /// deleted per this repo's "docs die on merge" rule — the finding lives here and in git history
    /// instead) showed why that was the wrong fix: the file's own content sits at a near-constant -63
    /// to -64dBFS windowed RMS for nearly the entire 185s (two brief blips to -46/-56dB) — the
    /// generation is basically hiss riding under silence, so the +5.45dB boost lifted that hiss right
    /// along with the sparse content, which is what the owner heard as "loud static randomly at
    /// night." Boosting a quiet generation's own noise floor was never going to fix that; only a
    /// better generation (U9, GPU-gated) or a revert could. The praised original stays committed on
    /// disk — this is the one-line revert back to its old id/path/TrimDb the "revert is a table row"
    /// contract (KTD-F) was built for. The loop-length win trades back to 60s until U9 lands a clean
    /// ≥180s regeneration; that trade is disclosed to the owner (Open Question 4), not hidden.</para>
    ///
    /// <para><b>fix/night-music-is-static (2026-08-09) deleted night-still-long.mp3 outright.</b> The
    /// table above had already stopped wiring it, but the bad generation stayed committed on disk for
    /// a week as an orphan a future one-line edit could re-wire without any test noticing (the sign
    /// guard below only rejects a POSITIVE TrimDb, not a bad file at TrimDb 0). Re-measured with
    /// soundfile/pyloudnorm rather than ffmpeg this time: -27.12 LUFS integrated, and only 0.75dB of
    /// per-second RMS spread across its opening 10 seconds versus 14-54dB for every track actually
    /// shipped — a flat noise floor, not music. It is gone now, and
    /// <c>AudioTests.EveryComposedTrack_MatchesItsApprovedLoudnessFingerprint</c> pins the surviving
    /// four files' bytes so a future swap-in of something similarly bad fails loudly instead of
    /// waiting for another human playtest to catch it by ear.</para>
    ///
    /// <para><b>That first pass did not explain the owner's own words.</b> He heard static WHILE
    /// PLAYING, and night-still.mp3 (Camp, the mid-raid decision window) measured clean. The Camp
    /// phase is a brief background beat, though — the phase a player actually SITS with at day's end,
    /// for up to <see cref="PhaseClock.EveningSeconds"/> (45s, or longer if manual), watching dusk
    /// fall before the day-end Ledger reveals what the raid cost, is <c>Evening</c> ->
    /// <c>town-dusk.mp3</c>. Measuring it with ffmpeg's <c>ebur128</c>/<c>loudnorm</c> (true peak is
    /// an OVERSAMPLED measurement, ITU-R BS.1770 — a file can clip on reconstruction even when no
    /// single stored sample exceeds 0dBFS) found it sitting at <b>+1.71 dBTP</b> — inter-sample
    /// clipping, audible as exactly the crackle/distortion "random static noises" describes, and
    /// sustained for nearly a minute of real listening time every single day. <c>day-first-light.mp3</c>
    /// (Morning, the dawn track the owner praised) checked out at +0.03 dBTP — not what he
    /// complained about, but still technically clipping and fixed alongside it rather than left for
    /// the next playtest to rediscover. <c>quest-wait.mp3</c> (-1.40 dBTP) and <c>night-still.mp3</c>
    /// (-2.99 dBTP) both already had real headroom and were left untouched.</para>
    ///
    /// <para>Fixed by reducing the FILE's own level, never by widening a downstream trim to mask
    /// it — the same principle night-still-long's own incident established, applied to a different
    /// failure shape. Both files were decoded, gained down (town-dusk -3.5dB, day-first-light
    /// -1.5dB — chosen so re-encoding at their original bitrates still lands with headroom to spare,
    /// not shaved to the edge again) and re-encoded to MP3 at their original bitrate/sample rate
    /// (town-dusk 128kbps, day-first-light 320kbps, both 48kHz stereo) with ffmpeg. Re-measured true
    /// peak after: town-dusk -2.39 dBTP, day-first-light -1.48 dBTP — both now comfortably below the
    /// -1.0 dBTP ceiling <c>AudioTests.EveryComposedTrack_StaysUnderItsTruePeakCeiling</c> pins. TrimDb below
    /// moved to hold each track's EFFECTIVE loudness exactly where it was (the player hears no
    /// change): town-dusk's raw LUFS shifted from -13.77 to -17.94 with the gain cut, so its trim
    /// moved from -8dB to -3.8dB (still a cut, never a boost); day-first-light's raw moved -13.32 ->
    /// -14.82, trim -8.4dB -> -6.9dB.</para>
    ///
    /// <para><b>TrimDb, and why it is not just zero everywhere.</b> Measured with ffmpeg's
    /// <c>loudnorm</c> analysis pass (integrated LUFS) on each composed file, same method U2 used.
    /// U2 trimmed each track to roughly match the SYNTH BED it replaced; U4 changes the reference —
    /// every composed track now targets the owner's own praised night-still LUFS (-21.7) directly
    /// (R5's ±1 LU contract), not whatever synth bed happens to sit next to it in the table. Measured
    /// raws and the resulting effective (raw + TrimDb) level, current as of the true-peak fix above:
    /// town-dusk -17.94 (-3.8dB -> -21.74), quest-wait -14.30 (-7.5dB -> -21.8, unchanged), day-first-
    /// light -14.82 (-6.9dB -> -21.72), night-still -21.73 (praised original, TrimDb 0 — no boost
    /// needed, unchanged). This is a measured best-effort, not a verdict: the owner's in-game A/B
    /// (<see cref="_UnhandledKeyInput"/>) is what actually confirms "comparable." <b>No entry may
    /// ever carry a positive TrimDb again</b> (R7/KTD-F) — <see cref="ComposedTrackTrims"/> is the
    /// census surface <c>AudioTests</c> pins that against.</para>
    /// </summary>
    private static readonly Dictionary<DayPhase, ComposedTrack> ComposedTracks = new()
    {
        [DayPhase.Morning] = new ComposedTrack("day-first-light", "res://assets/audio/day-first-light.mp3", TrimDb: -6.9f),
        [DayPhase.Evening] = new ComposedTrack("town-dusk", "res://assets/audio/town-dusk.mp3", TrimDb: -3.8f),
        [DayPhase.Camp] = new ComposedTrack("night-still", "res://assets/audio/night-still.mp3", TrimDb: 0f),
        [DayPhase.Expedition] = new ComposedTrack("quest-wait", "res://assets/audio/quest-wait.mp3", TrimDb: -7.5f),
        [DayPhase.ExpeditionDeep] = new ComposedTrack("quest-wait", "res://assets/audio/quest-wait.mp3", TrimDb: -7.5f),
    };

    /// <summary>Loaded composed streams, keyed by resource path so the same file backing two table
    /// entries (quest-wait covers both Expedition phases) is decoded once, not twice.</summary>
    private static readonly Dictionary<string, AudioStream> ComposedCache = new();

    /// <summary>
    /// Census surface (KTD-B applied to audio, U2): every phase this build currently maps to a
    /// composed track, with its id — public, read-only, and ids-only (no stream) so
    /// <c>AudioTests.EveryComposedTrack_LoadsAndLoops</c> can enumerate the table and assert each
    /// entry resolves to real, loop-enabled audio without reaching into private state, and without
    /// forcing every track to decode as a side effect of merely counting entries. This is exactly the
    /// list that must never contain an id nobody can hear — "committed but never wired" (the fate of
    /// these three tracks before this unit) fails <c>EveryComposedTrack_LoadsAndLoops</c> the moment
    /// an id is added here without a file to back it, or removed from here without removing the file.
    /// </summary>
    public static IReadOnlyDictionary<DayPhase, string> ComposedTrackIds =>
        ComposedTracks.ToDictionary(kv => kv.Key, kv => kv.Value.Id);

    /// <summary>
    /// Census surface, the TrimDb half (U6, R7/KTD-F): every phase's composed-track code-side trim,
    /// public and read-only so a regression test can pin "no entry may carry a positive TrimDb"
    /// without reaching into <see cref="ComposedTracks"/>'s private state. A positive trim is the
    /// code admitting a generation is wrong and boosting its way past that — <c>night-still-long</c>
    /// shipped at +5.45dB, and the boosted noise floor was the owner's "loud static randomly at
    /// night." If a generation needs a boost to reach level, the fix is a better generation (U9),
    /// never a positive TrimDb.
    /// </summary>
    public static IReadOnlyDictionary<DayPhase, float> ComposedTrackTrims =>
        ComposedTracks.ToDictionary(kv => kv.Key, kv => kv.Value.TrimDb);

    /// <summary>
    /// Loads the composed track mapped to <paramref name="phase"/>, or null if none is mapped or it
    /// failed to load. Test-only entry point into <see cref="LoadComposed"/> — the SAME loader
    /// <see cref="ResolveBed"/> uses at runtime, so a green census means the game's actual code path
    /// resolved real audio, not a parallel check that could quietly drift from what actually plays.
    /// </summary>
    public static AudioStream? LoadComposedTrackForCensus(DayPhase phase) =>
        ComposedTracks.TryGetValue(phase, out var track) ? LoadComposed(track) : null;

    private readonly List<AudioStreamPlayer> _voices = new();
    private int _nextVoice;

    /// <summary>
    /// Dedicated voice for a HELD gesture's sustained loop (U8, R8) — separate from the pooled
    /// <see cref="_voices"/> because a loop must not get stolen by round-robin mid-hold, and release
    /// needs its own fade so a stop mid-buffer never clicks (<see cref="StopLoop"/>). One voice is
    /// enough: only one gesture (the forge bellows) loops today.
    ///
    /// <para>Looping is driven by manually retriggering <see cref="Play"/> on <see cref="Finished"/>
    /// (<see cref="OnLoopVoiceFinished"/>) rather than the stream's own baked-in <c>LoopMode</c> — the
    /// SAME cached <see cref="SfxLibrary"/> stream that loops here must also stay safe to fire as a
    /// plain one-shot on a POOLED voice elsewhere (a discrete <c>PumpStroke</c>); a stream-level
    /// LoopMode would make every playback of it loop forever, one-shot or not.</para>
    /// </summary>
    private AudioStreamPlayer _loopVoice = null!;

    /// <summary>The cue currently armed on <see cref="_loopVoice"/>, or null when nothing is
    /// looping. <see cref="OnLoopVoiceFinished"/> and <see cref="StopLoop"/> both read this to decide
    /// whether to keep breathing or let go.</summary>
    private Cue? _loopCue;

    /// <summary>True from <see cref="StopLoop"/> until its release fade lands — see
    /// <see cref="_Process"/>.</summary>
    private bool _loopReleasing;

    private double _loopReleaseElapsed;

    /// <summary>The loop voice's own gain (<see cref="SfxGainDb"/>) at the moment <see
    /// cref="StopLoop"/> armed the release — the fade's actual starting point, captured live rather
    /// than assumed to be 0dB, so a mixer slider dragged before or during a hold is what the release
    /// fades FROM, not a stale default.</summary>
    private float _loopReleaseStartDb;

    /// <summary>How long a released hold takes to fade to silence before the voice actually stops.
    /// Short on purpose (this is a release, not a crossfade) but long enough that the stop is never a
    /// click — an abrupt cut mid-buffer would click regardless of the clip's own DeClick fade, which
    /// only smooths the CLIP's own edges, never an arbitrary interruption point.</summary>
    private const float LoopReleaseSeconds = 0.12f;

    private AudioStreamPlayer _musicA = null!;
    private AudioStreamPlayer _musicB = null!;

    /// <summary>True when <see cref="_musicA"/> is the one currently fading UP.</summary>
    private bool _aIsActive = true;

    /// <summary>The <see cref="ComposedTrack.TrimDb"/> currently armed on each player, so a crossfade
    /// already in flight fades FROM the level a player is actually at rather than assuming both
    /// players always target the same <see cref="MusicDb"/> — see <see cref="_Process"/>.</summary>
    private float _musicATrimDb;
    private float _musicBTrimDb;

    /// <summary>Dev A/B toggle (<see cref="_UnhandledKeyInput"/>): true forces the synth bed even
    /// where a composed track is mapped, so the owner can flip back and forth and judge the two back
    /// to back (R3). Composed is preferred by default (false) — the whole point of landing these
    /// tracks is that they play unless told otherwise.</summary>
    private bool _preferSynth;

    /// <summary>C1 (2026-08-09 shell-and-audio-menu plan): gate for the M hotkey below. Before this
    /// unit, M flipped <see cref="_preferSynth"/> from ANY screen, unhandled, with no on-screen
    /// explanation — a diagnostic left live in a shipping build, and a live footgun now that players
    /// own their own mix through Settings. Off by default; the owner's own A/B judging (R3) still
    /// needs it during content review, so it is gated, not deleted, behind the same env-var idiom as
    /// <see cref="MuteEnvVar"/>.</summary>
    public const string DevHotkeysEnvVar = "MAKERSMARK_DEV_AUDIO_HOTKEYS";

    private bool _devHotkeysEnabled;

    /// <summary>Seconds into the current crossfade, or -1 when no fade is in flight (mirrors the
    /// accumulated-delta idiom used by <c>TabFade</c> and <c>DayPhaseTint</c> — no engine Tween).</summary>
    private double _fadeElapsed = -1;

    private DayPhase? _phase;

    /// <summary>Non-null while a scene owns the music instead of the day's phase (see
    /// <see cref="SetScene"/>).</summary>
    private string? _scene;

    /// <summary>Master mute. Set before or after mounting; takes effect immediately.</summary>
    public bool Muted { get; private set; }

    /// <summary>
    /// Finds the director anywhere above or beside <paramref name="context"/> in the tree, or null.
    ///
    /// <para>Lazy tree lookup rather than a constructor argument threaded through every panel — the same
    /// pattern <c>ForgePanel.ResolveTown</c> already uses for its Town2D sibling, and for the same
    /// reason: a panel mounted bare in a test has no director, and NULL-TOLERANCE is the point. A cue
    /// that cannot be played must never be able to break a panel, so every call site is
    /// <c>AudioDirector.For(this)?.Play(...)</c> and a missing director is simply silence.</para>
    ///
    /// <para>Not cached: panels are rebuilt freely and a stale reference to a freed director would be
    /// worse than the microseconds this costs. The search is a single named-child probe from the root,
    /// not a full tree walk.</para>
    /// </summary>
    public static AudioDirector? For(Node context) =>
        context.IsInsideTree()
            ? context.GetTree()?.Root?.FindChild("AudioDirector", recursive: true, owned: false) as AudioDirector
            : null;

    /// <summary>
    /// Environment switch that starts the game silent. Set by every automated tool that drives the real
    /// client (playtests, screenshots) so an unattended run cannot make noise on someone's machine —
    /// Brian, mid-session: "please mute the game during playtests - you can record and optimize later."
    ///
    /// <para>An env var rather than a flag each tool remembers to pass: a tool ADDED later inherits the
    /// mute for free, which is the opposite of the pattern that has bitten this project repeatedly
    /// (declare the capability, forget the one call that activates it). Nothing about the audio path is
    /// skipped — streams are still synthesized and cues still fire, so the tools still exercise the code
    /// they are supposed to; only the output is silenced.</para>
    /// </summary>
    public const string MuteEnvVar = "MAKERSMARK_MUTE_AUDIO";

    // ---- the mixer (C1, 2026-08-09 shell-and-audio-menu plan) ---------------------------------
    //
    // Four linear (0..1) PREFERENCE faders layered ON TOP of the MASTERING levels above (MusicDb,
    // NarratorDb, and every ComposedTrack's own TrimDb) — never collapsed into them. TrimDb answers
    // "does this generation read at the same loudness as its neighbours"; these answer "how loud
    // does the PLAYER want the game" — two different questions asked by two different people at two
    // different times, and conflating them is exactly how the night-still-long +5.45dB static
    // incident happened (see ComposedTracks' own doc). 1.0 (every default below) reproduces today's
    // constants exactly, so shipping this unit changes nothing for a player who never opens Settings.

    /// <summary>1.0 = today's existing mix, unchanged. The factory default for all four faders.</summary>
    public const float DefaultVolume = 1f;

    public float MasterVolume { get; private set; } = DefaultVolume;
    public float MusicVolume { get; private set; } = DefaultVolume;
    public float SfxVolume { get; private set; } = DefaultVolume;
    public float NarratorVolume { get; private set; } = DefaultVolume;

    /// <summary>Converts a linear 0..1 fader to dB, floored at <see cref="SilentDb"/> — the same
    /// "gone" floor every fade in this file already uses — rather than letting a fader dragged to
    /// zero produce negative infinity and poison every dB arithmetic downstream of it.</summary>
    private static float GainDb(float linear01) =>
        linear01 <= 0.0001f ? SilentDb : Mathf.Max(SilentDb, Mathf.LinearToDb(linear01));

    /// <summary>Master and a category fader are independent linear gains, so their COMBINED gain is
    /// their PRODUCT converted to dB once — never two dB values simply added, which would double-
    /// count the floor the moment either fader is near zero.</summary>
    private float MixGainDb(float categoryVolume) =>
        GainDb(Mathf.Clamp(MasterVolume, 0f, 1f) * Mathf.Clamp(categoryVolume, 0f, 1f));

    private float SfxGainDb() => MixGainDb(SfxVolume);

    /// <summary>The music bed's real target dB for a given <see cref="ComposedTrack.TrimDb"/> —
    /// mastering (<paramref name="trimDb"/>) plus preference (<see cref="MixGainDb"/>), computed
    /// fresh every call so a fader dragged mid-crossfade is reflected on the very next <see
    /// cref="_Process"/> tick with no special-cased "reapply" path for the in-flight-fade case.</summary>
    private float MusicTargetDb(float trimDb) => MusicDb + trimDb + MixGainDb(MusicVolume);

    public void SetMasterVolume(float volume)
    {
        MasterVolume = Mathf.Clamp(volume, 0f, 1f);
        ApplyMusicMixerVolume();
        ApplySfxMixerVolume();
        RefreshNarratorVolume();
    }

    public void SetMusicVolume(float volume)
    {
        MusicVolume = Mathf.Clamp(volume, 0f, 1f);
        ApplyMusicMixerVolume();
    }

    public void SetSfxVolume(float volume)
    {
        SfxVolume = Mathf.Clamp(volume, 0f, 1f);
        ApplySfxMixerVolume();
    }

    /// <summary>0 is legal here, and it means something different than everywhere else on this
    /// panel: the narrator carries no information the screen does not already carry (see <see
    /// cref="SpeakNarrator"/>'s own doc), so a silenced narrator is indistinguishable from a voice
    /// library that was never recorded. This slider only ever touches <see cref="_narratorVoice"/>'s
    /// gain — <see cref="SpeakNarrator"/> still writes the line to the screen and to <see
    /// cref="RecentNarratorLines"/> at every volume, including zero. No setting anywhere may
    /// suppress the narrator's TEXT.</summary>
    public void SetNarratorVolume(float volume)
    {
        NarratorVolume = Mathf.Clamp(volume, 0f, 1f);
        RefreshNarratorVolume();
    }

    /// <summary>Re-levels whatever bed is CURRENTLY playing right now, bypassing the fade — needed
    /// because <see cref="_Process"/> only recomputes volume while a crossfade is actually in flight
    /// (<c>_fadeElapsed &gt;= 0</c>); a settled bed's player would otherwise sit at its OLD target
    /// until the next phase change gave <see cref="_Process"/> a reason to touch it again.</summary>
    private void ApplyMusicMixerVolume()
    {
        if (_fadeElapsed >= 0)
        {
            return; // a fade is in flight — _Process recomputes MusicTargetDb every frame already
        }

        var active = _aIsActive ? _musicA : _musicB;
        if (!active.Playing)
        {
            return; // nothing airborne yet — the next SetPhase/CrossfadeTo will read the new fader
        }

        var trim = _aIsActive ? _musicATrimDb : _musicBTrimDb;
        active.VolumeDb = MusicTargetDb(trim);
    }

    /// <summary>Live-updates a bellows hold already in progress, so dragging the SFX or Master
    /// slider while gripping the forge is heard immediately rather than on the next gesture. A
    /// pooled one-shot reads <see cref="SfxGainDb"/> fresh at its own <see cref="Play"/> call and
    /// needs no equivalent — it is already gone by the time a slider could move.</summary>
    private void ApplySfxMixerVolume()
    {
        if (_loopCue is not null && !_loopReleasing)
        {
            _loopVoice.VolumeDb = SfxGainDb();
        }
    }

    private void RefreshNarratorVolume() => _narratorVoice.VolumeDb = NarratorDb + MixGainDb(NarratorVolume);

    /// <summary>
    /// Re-applies whatever mix the player last saved (Settings' four faders, and Mute) — call once,
    /// right after mounting a fresh instance. <c>MainUi.BuildUi</c> is the one real call site, the
    /// same "explicit call right after construction" idiom <see cref="UiSettings.ApplyPersisted"/>
    /// already uses for the OS window.
    ///
    /// <para>Deliberately NOT folded into <see cref="_Ready"/> itself: every existing test in
    /// <c>AudioTests</c> constructs a bare <c>new AudioDirector()</c> expecting today's exact
    /// defaults (no fader trimmed, nothing muted unless <see cref="MuteEnvVar"/> says so), and
    /// reading a disk file inside the constructor would make every one of those tests depend on
    /// whatever <c>ui_settings.json</c> happens to be sitting in the sandbox — precisely the
    /// shared-mutable-state hazard <c>UiSettingsTests</c>' own class doc already warns about, now
    /// reaching a suite that has nothing to do with Settings.</para>
    /// </summary>
    public void ApplyPersistedMixer()
    {
        SetMasterVolume(UiSettings.LoadMasterVolume());
        SetMusicVolume(UiSettings.LoadMusicVolume());
        SetSfxVolume(UiSettings.LoadSfxVolume());
        SetNarratorVolume(UiSettings.LoadNarratorVolume());
        // MuteEnvVar (automated tools) always wins over a stale "unmuted" preference on disk — OR,
        // never overwrite, so a real boot only ADDS the player's own saved mute on top of it.
        SetMuted(Muted || UiSettings.LoadMuted());
    }

    public override void _Ready()
    {
        Name = "AudioDirector";

        Muted = !string.IsNullOrEmpty(OS.GetEnvironment(MuteEnvVar));
        _devHotkeysEnabled = !string.IsNullOrEmpty(OS.GetEnvironment(DevHotkeysEnvVar));

        for (var i = 0; i < VoiceCount; i++)
        {
            var voice = new AudioStreamPlayer { Name = $"Voice{i}" };
            AddChild(voice);
            _voices.Add(voice);
        }

        _musicA = new AudioStreamPlayer { Name = "MusicA", VolumeDb = SilentDb };
        _musicB = new AudioStreamPlayer { Name = "MusicB", VolumeDb = SilentDb };
        AddChild(_musicA);
        AddChild(_musicB);

        _loopVoice = new AudioStreamPlayer { Name = "LoopVoice" };
        AddChild(_loopVoice);
        _loopVoice.Finished += OnLoopVoiceFinished;

        _narratorVoice = new AudioStreamPlayer { Name = "NarratorVoice" };
        AddChild(_narratorVoice);
        RefreshNarratorVolume();
    }

    // ---- the narrator ------------------------------------------------------------------------
    //
    // Sparse, triggered, and slotless. NarratorVoiceDirector (sim-side, deterministic) decides WHAT
    // is said; everything here is playback. The voice carries no information — the screen keeps every
    // fact — so a missing recording costs atmosphere and never meaning.

    /// <summary>
    /// Where the narrator sits against the bed. The music bed plays at <see cref="MusicDb"/> (-22),
    /// and speech has to read clearly over it without becoming the loudest thing in the game; the
    /// lines are baked to a fixed loudness at content time so this one number is the whole mix.
    ///
    /// <para>Negative, and it stays negative. The <c>night-still-long</c> +5.45dB boost is why
    /// <see cref="ComposedTrackTrims"/> exists as a census surface — a generation that needs a boost
    /// to reach level is a bad generation, and the fix is a better take, never a positive trim.</para>
    /// </summary>
    private const float NarratorDb = -14f;

    /// <summary>Dedicated player, never pooled. A narrator cut off mid-sentence by a round-robin
    /// steal is worse than one that never spoke — same reasoning as <see cref="_loopVoice"/>.</summary>
    private AudioStreamPlayer _narratorVoice = null!;

    private readonly List<NarratorRequest> _recentNarratorLines = [];

    /// <summary>The last line the narrator was ASKED to speak, recorded even while muted.</summary>
    public NarratorRequest? LastNarratorLine { get; private set; }

    /// <summary>Rolling window of narrator requests, oldest first. Absence needs a window, not a
    /// snapshot — the same lesson <see cref="RecentCues"/> was rewritten to learn.</summary>
    public IReadOnlyList<NarratorRequest> RecentNarratorLines => _recentNarratorLines;

    /// <summary>Drops the narrator window so a test can scope it to one ceremony.</summary>
    public void ClearRecentNarratorLines() => _recentNarratorLines.Clear();

    /// <summary>
    /// Speak one line for a moment that earned it, or record a text-only request when no recording
    /// exists yet. Returns the line's text so a caller can show it on screen regardless — the screen
    /// is the source of truth and never waits on audio.
    ///
    /// <para>A request arriving while the narrator is already speaking is DROPPED, not queued: two
    /// stacked lines are a mess, and a line that arrives late has missed the moment it was for.</para>
    /// </summary>
    public string SpeakNarrator(NarratorVoiceDirector.Trigger trigger, ulong campaignId, ulong eventId)
    {
        var previous = LastNarratorLineIndexFor(trigger);
        var index = NarratorVoiceDirector.ChooseLine(trigger, campaignId, eventId, previous);
        var audioId = NarratorVoiceDirector.AudioId(trigger, index);
        var text = NarratorVoiceDirector.Lines[trigger][index];
        var path = NarratorLines.ResourcePath(audioId);

        var voiced = false;
        if (!ResourceLoader.Exists(path))
        {
            EngineDistress.Warn(
                $"[AudioDirector] narrator line '{audioId}' has no recording at {path} — the line is "
                + "on screen but nothing will be heard. This is legal while the library is partial.");
        }
        else if (_narratorVoice is not null && !_narratorVoice.Playing && !Muted)
        {
            _narratorVoice.Stream = GD.Load<AudioStream>(path);
            _narratorVoice.Play();
            voiced = true;
        }

        var request = new NarratorRequest(audioId, text, voiced);
        LastNarratorLine = request;
        _recentNarratorLines.Add(request);
        if (_recentNarratorLines.Count > RecentCueMemory)
        {
            _recentNarratorLines.RemoveAt(0);
        }

        var line = voiced ? $"VOICE: spoke {audioId}" : $"VOICE: text-only (no audio) {audioId}";
        GD.Print(line);
        PlaytestLog.Note(line);

        return text;
    }

    /// <summary>Index of the last line spoken for this trigger, or -1 — feeds the no-repeat rule.</summary>
    private int LastNarratorLineIndexFor(NarratorVoiceDirector.Trigger trigger)
    {
        var slug = NarratorVoiceDirector.TriggerSlug(trigger) + "-";
        for (var i = _recentNarratorLines.Count - 1; i >= 0; i--)
        {
            var id = _recentNarratorLines[i].AudioId;
            if (id.StartsWith(slug, StringComparison.Ordinal)
                && int.TryParse(id[slug.Length..], out var index))
            {
                return index;
            }
        }
        return -1;
    }

    // The library and its resource paths live in NarratorLines, deliberately off this Node — see
    // that file for the crash that put them there.

    /// <summary>
    /// Test/inspection surface: the last cue <see cref="Play"/> was ASKED for, recorded even while
    /// <see cref="Muted"/>.
    ///
    /// <para>Automated runs mute audio, which made "which cue did this path choose?" unobservable — so
    /// a bug where every immediate action rang the day's bell over the music bed shipped and was only
    /// caught by the owner playing the game. Recording the request (not the playback) costs nothing,
    /// works muted, and lets a test pin cue CHOICE without asserting on sound.</para>
    /// </summary>
    public Cue? LastCuePlayed { get; private set; }

    private readonly System.Collections.Generic.List<Cue> _recentCues = [];

    /// <summary>Cap on <see cref="RecentCues"/> — a rolling window, not a session log.</summary>
    private const int RecentCueMemory = 64;

    /// <summary>
    /// Every cue requested since the last <see cref="ClearRecentCues"/>, oldest first, capped at
    /// <see cref="RecentCueMemory"/>.
    ///
    /// <para><b>Why the list and not just <see cref="LastCuePlayed"/>.</b> "Last cue" is not enough to
    /// prove a cue did NOT fire: several call sites play their own cue immediately after queueing an
    /// action, so a spurious cue raised inside the queue can be overwritten before anyone looks. The
    /// first version of <c>ImmediateActionsDoNotReplayThePhaseTests</c> asserted on
    /// <see cref="LastCuePlayed"/>, passed with the fix reverted, and would have shipped a test that
    /// pinned nothing. Absence needs a window, not a snapshot.</para>
    /// </summary>
    public System.Collections.Generic.IReadOnlyList<Cue> RecentCues => _recentCues;

    /// <summary>Drops the <see cref="RecentCues"/> window so a test can scope it to one interaction.</summary>
    public void ClearRecentCues() => _recentCues.Clear();

    /// <summary>Fires <paramref name="cue"/> on the next pooled voice. No-op while <see cref="Muted"/>.</summary>
    public void Play(Cue cue)
    {
        LastCuePlayed = cue;
        if (_recentCues.Count >= RecentCueMemory)
        {
            _recentCues.RemoveAt(0);
        }

        _recentCues.Add(cue);

        if (Muted || _voices.Count == 0)
        {
            return;
        }

        var voice = _voices[_nextVoice];
        _nextVoice = (_nextVoice + 1) % _voices.Count;
        voice.VolumeDb = SfxGainDb();
        voice.Stream = SfxLibrary.Get(cue);
        voice.Play();
    }

    /// <summary>
    /// Starts (or keeps alive) a HELD gesture's sustained loop on <see cref="_loopVoice"/> — U8, R8:
    /// "the bellows shift since you have to hold" was the specific complaint, and a 0.3s one-shot per
    /// grip was the wrong shape for a multi-second gesture. Idempotent for the SAME cue already
    /// looping (calling it again mid-breath must not restart the clip's phase); a different cue
    /// takes over the one dedicated voice immediately. No-op while <see cref="Muted"/>, matching
    /// <see cref="Play"/>'s own contract.
    /// </summary>
    public void StartLoop(Cue cue)
    {
        if (_loopCue == cue && !_loopReleasing)
        {
            return; // already looping this cue — do not restart mid-breath
        }

        _loopCue = cue;
        _loopReleasing = false;
        _loopReleaseElapsed = -1;

        if (Muted)
        {
            return;
        }

        _loopVoice.VolumeDb = SfxGainDb();
        _loopVoice.Stream = SfxLibrary.Get(cue);
        _loopVoice.Play();
    }

    /// <summary>
    /// Releases the held loop with a short fade (<see cref="LoopReleaseSeconds"/>, driven by
    /// <see cref="_Process"/>) instead of a hard <c>Stop()</c> — an instant cut mid-buffer would
    /// click. A no-op if <paramref name="cue"/> is not the one currently armed (e.g. a stale call
    /// after a different gesture already took the voice).
    /// </summary>
    public void StopLoop(Cue cue)
    {
        if (_loopCue != cue)
        {
            return;
        }

        if (Muted)
        {
            _loopCue = null;
            _loopReleasing = false;
            return;
        }

        _loopReleaseStartDb = _loopVoice.VolumeDb; // fade from wherever the mixer actually has it
        _loopReleasing = true;
        _loopReleaseElapsed = 0;
    }

    /// <summary>
    /// Keeps a held breath going for as long as it is armed. The loop cue's own clip is short (a
    /// single ~0.3s breath, seam-safe like a music-bed loop — see <see cref="Cue.Bellows"/>'s own
    /// doc) — a multi-second hold needs many repeats, and this is the retrigger, never a new
    /// gesture. Stops retriggering the instant <see cref="StopLoop"/> has armed the release fade.
    /// </summary>
    private void OnLoopVoiceFinished()
    {
        if (_loopCue is { } cue && !_loopReleasing && !Muted)
        {
            _loopVoice.VolumeDb = SfxGainDb(); // live: a slider dragged mid-hold is heard next breath
            _loopVoice.Stream = SfxLibrary.Get(cue);
            _loopVoice.Play();
        }
    }

    /// <summary>
    /// Moves the music bed to <paramref name="phase"/>, crossfading from whatever is playing. Repeated
    /// calls with the same phase are ignored, so this is safe to call from every state refresh — which is
    /// how <c>MainUi</c> uses it, rather than trying to detect phase boundaries itself.
    /// </summary>
    public void SetPhase(DayPhase phase)
    {
        var changed = _phase != phase;
        _phase = phase;

        // A scene owns the music while it is open; the phase is still recorded above so closing the
        // scene returns to the RIGHT bed even if the day moved on underneath it.
        if (!changed || _scene is not null || Muted)
        {
            return;
        }

        ApplyPhaseBed(phase);
    }

    /// <summary>Resolves and crossfades to whichever bed <paramref name="phase"/> should be playing
    /// right now (composed-first ladder via <see cref="ResolveBed"/>), and logs which one won. The
    /// shared tail of <see cref="SetPhase"/> and <see cref="SetScene"/>'s day-bed fallback — both mean
    /// "the town's own bed for this phase," so both go through the same resolution and the same log
    /// line rather than two copies that could drift apart.</summary>
    private void ApplyPhaseBed(DayPhase phase)
    {
        var (stream, trimDb, label) = ResolveBed(phase);
        LogBedSwap(label, phase);
        CrossfadeTo(stream, trimDb);
    }

    /// <summary>
    /// Composed-first ladder (KTD-C): prefers <see cref="ComposedTracks"/> for <paramref name="phase"/>
    /// unless the dev A/B toggle is forcing the synth ladder (<see cref="_preferSynth"/>), no composed
    /// track is mapped to this phase, or the mapped one fails to load (missing LFS content on a bare
    /// checkout). <see cref="MusicBed"/> is therefore always reachable and never removed — it is the
    /// fallback for every gap in the table, not just a stepping stone this unit deletes.
    /// </summary>
    private (AudioStream Stream, float TrimDb, string Label) ResolveBed(DayPhase phase)
    {
        if (!_preferSynth && ComposedTracks.TryGetValue(phase, out var track))
        {
            var composed = LoadComposed(track);
            if (composed is not null)
            {
                return (composed, track.TrimDb, $"composed '{track.Id}'");
            }
        }

        return (MusicBed.For(phase), 0f, "synth bed");
    }

    /// <summary>
    /// Loads (and caches) a composed track, or returns null after a LOUD warning if it cannot be
    /// found — a checkout without its Git LFS content pulled must degrade to the synth bed audibly
    /// documented in the log, never to a silent crash. Same NULL-TOLERANT-BUT-LOUD contract KTD-B
    /// applies to art: the fallback stays; only its silence goes.
    /// </summary>
    private static AudioStream? LoadComposed(ComposedTrack track)
    {
        if (ComposedCache.TryGetValue(track.ResourcePath, out var cached))
        {
            return cached;
        }

        if (!ResourceLoader.Exists(track.ResourcePath))
        {
            EngineDistress.Warn(
                $"[AudioDirector] composed track '{track.Id}' is missing at {track.ResourcePath} " +
                "(Git LFS content not pulled?) — falling back to the synth bed.");
            return null;
        }

        var stream = GD.Load<AudioStream>(track.ResourcePath);
        if (stream is AudioStreamMP3 mp3)
        {
            // Belt-and-suspenders: the .import file's loop=true param already bakes this into the
            // compiled resource, but every OTHER loop guarantee in this file is enforced in code, not
            // metadata, and a stray future reimport must not be able to silently drop it either.
            mp3.Loop = true;
        }

        ComposedCache[track.ResourcePath] = stream;
        return stream;
    }

    /// <summary>The one line this whole unit exists to produce: which bed is actually playing, printed
    /// so it is visible in any console AND written to the session log so "on disk but never in the
    /// game" (the exact defect this unit fixes) is provably false in a played session's own record.</summary>
    private static void LogBedSwap(string label, DayPhase phase)
    {
        var line = $"MUSIC: {label} for {phase}";
        GD.Print(line);
        PlaytestLog.Note(line);
    }

    /// <summary>
    /// Dev A/B toggle: M flips composed vs synth and immediately re-applies whatever bed the current
    /// phase/scene would resolve to, so the owner can flip back and forth on the SAME phase and judge
    /// the two back to back (R3) rather than waiting for the day to move on. No <c>MainUi.cs</c> edit
    /// needed — <c>Node</c> already receives unhandled input directly. Gated behind <see
    /// cref="DevHotkeysEnvVar"/> (C1) — see that constant's own doc for why an unexplained hotkey
    /// that silently changes the mix cannot stay live now that players own it through Settings.
    /// </summary>
    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (!_devHotkeysEnabled)
        {
            return;
        }

        if (@event is not InputEventKey { PhysicalKeycode: Key.M, Pressed: true, Echo: false })
        {
            return;
        }

        _preferSynth = !_preferSynth;
        GetViewport()?.SetInputAsHandled();

        var toggleLine = $"MUSIC: A/B toggle -> {(_preferSynth ? "synth preferred" : "composed preferred")}";
        GD.Print(toggleLine);
        PlaytestLog.Note(toggleLine);

        ReapplyCurrentBed();
    }

    /// <summary>Re-resolves and crossfades to whatever SHOULD be playing right now, bypassing the
    /// idempotent "unchanged phase/scene" guards on <see cref="SetPhase"/>/<see cref="SetScene"/> —
    /// the A/B toggle needs exactly that bypass, since it changes what a phase resolves to without the
    /// phase itself changing. No-ops while muted (nothing to swap the volume of) or under the Mine's
    /// own <see cref="MusicBed.Underground"/> theme, which has no composed alternative to toggle to.</summary>
    private void ReapplyCurrentBed()
    {
        if (Muted || _scene == "depths")
        {
            return;
        }

        if (_phase is { } phase)
        {
            ApplyPhaseBed(phase);
        }
    }

    /// <summary>
    /// Hands the music to a named scene, or back to the day when <paramref name="scene"/> is null.
    ///
    /// <para>Watching the raid should not sound like standing in the town square. The Depths panel reads
    /// as inert partly because it is sonically identical to everywhere else — "unclear what to do during
    /// the expedition phase" was as much an atmosphere problem as an information one.</para>
    ///
    /// <para>Idempotent, so a panel can call it on every refresh without restarting the loop. Unknown
    /// scene names fall back to the day's bed rather than to silence: a typo should be inaudible, not a
    /// hole in the soundtrack.</para>
    /// </summary>
    public void SetScene(string? scene)
    {
        if (_scene == scene)
        {
            return;
        }

        _scene = scene;

        if (Muted)
        {
            return;
        }

        if (scene == "depths")
        {
            CrossfadeTo(MusicBed.Underground(), trimDb: 0f);
            return;
        }

        // Unknown/no scene: same "the day's own bed" fallback as before, now routed through the
        // composed-first ladder so leaving a scene can come back to a composed track, not just synth.
        if (_phase is { } p)
        {
            ApplyPhaseBed(p);
        }
    }

    private void CrossfadeTo(AudioStream stream, float trimDb)
    {
        var incoming = _aIsActive ? _musicB : _musicA;
        incoming.Stream = stream;
        incoming.VolumeDb = SilentDb;
        incoming.Play();

        if (_aIsActive)
        {
            _musicBTrimDb = trimDb;
        }
        else
        {
            _musicATrimDb = trimDb;
        }

        _aIsActive = !_aIsActive;
        _fadeElapsed = 0;
    }

    /// <summary>Silences everything and stops the bed, or restores it. Kept as one switch so an
    /// options screen has exactly one thing to bind — <see cref="SettingsPanel"/>'s Mute toggle (C1)
    /// is that binding; persistence is <see cref="UiSettings"/>'s job, not this method's.</summary>
    public void SetMuted(bool muted)
    {
        Muted = muted;
        if (!muted)
        {
            // Re-arm the bed for the phase we are already in: SetPhase early-returns on an unchanged
            // phase, so clear it first or unmuting would leave the music off until the phase happened
            // to change.
            var phase = _phase;
            _phase = null;
            if (phase is { } p)
            {
                SetPhase(p);
            }

            return;
        }

        _musicA.Stop();
        _musicB.Stop();
        foreach (var voice in _voices)
        {
            voice.Stop();
        }

        // Mute must actually stop EVERYTHING, including a held gesture's loop — otherwise a bellows
        // hold started before mute would keep breathing silently-in-name-only until released.
        _loopVoice.Stop();
        _loopCue = null;
        _loopReleasing = false;
        _loopReleaseElapsed = -1;
    }

    public override void _Process(double delta)
    {
        if (_loopReleasing)
        {
            _loopReleaseElapsed += delta;
            var released = (float)Math.Clamp(_loopReleaseElapsed / LoopReleaseSeconds, 0, 1);
            _loopVoice.VolumeDb = Mathf.Lerp(_loopReleaseStartDb, SilentDb, released);
            if (released >= 1f)
            {
                _loopVoice.Stop();
                _loopReleasing = false;
                _loopCue = null;
            }
        }

        if (_fadeElapsed < 0)
        {
            return;
        }

        _fadeElapsed += delta;
        var progress = (float)Math.Clamp(_fadeElapsed / CrossfadeSeconds, 0, 1);

        var rising = _aIsActive ? _musicA : _musicB;
        var falling = _aIsActive ? _musicB : _musicA;

        // Each player fades toward/from ITS OWN remembered trim (see _musicATrimDb/_musicBTrimDb),
        // not a single shared MusicDb — a composed track that needs a -5dB trim must still crossfade
        // in at -5dB, and whatever is fading OUT must fade from the level it was actually playing at.
        var risingTrim = _aIsActive ? _musicATrimDb : _musicBTrimDb;
        var fallingTrim = _aIsActive ? _musicBTrimDb : _musicATrimDb;

        rising.VolumeDb = Mathf.Lerp(SilentDb, MusicTargetDb(risingTrim), progress);
        falling.VolumeDb = Mathf.Lerp(MusicTargetDb(fallingTrim), SilentDb, progress);

        if (progress >= 1f)
        {
            falling.Stop();
            _fadeElapsed = -1;
        }
    }
}
