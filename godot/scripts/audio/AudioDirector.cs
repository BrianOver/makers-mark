using System;
using System.Collections.Generic;
using System.Linq;
using GameSim.Contracts;
using Godot;
using GodotClient.Tools;

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
    /// <para><b>night-still-long replaces night-still as Camp's FILE, not just its id.</b> The praised
    /// original 60s <c>night-still.mp3</c> stays committed on disk — reverting Camp to it is a
    /// one-line edit back to the old id/path/TrimDb, exactly the "revert is a table row" contract this
    /// unit promised. The new file is a &gt;=180s regeneration of the SAME quiet-camp brief so the loop
    /// stops completing often enough to announce itself (the owner's exact "loops too quickly," now
    /// aimed at a composed track instead of the synth bed it was originally about).</para>
    ///
    /// <para><b>TrimDb, and why it is not just zero everywhere.</b> Measured with ffmpeg's
    /// <c>loudnorm</c> analysis pass (integrated LUFS) on each composed file, same method U2 used.
    /// U2 trimmed each track to roughly match the SYNTH BED it replaced; U4 changes the reference —
    /// every composed track now targets the owner's own praised night-still LUFS (-21.7) directly
    /// (R5's ±1 LU contract), not whatever synth bed happens to sit next to it in the table. Measured
    /// raws and the resulting effective (raw + TrimDb) level: town-dusk -13.8 (was -5dB -> -18.8
    /// effective, now -8dB -> -21.8), quest-wait -14.3 (was -4dB -> -18.3 effective, now -7.5dB ->
    /// -21.8), day-first-light -13.3 (new track, -8.4dB -> -21.7), night-still-long -27.15 (new
    /// regeneration — quieter than the original 60s file's own -21.7 raw despite the same brief;
    /// ambient generation is not byte-reproducible across a 3x-longer render even holding style
    /// constant, so this one needs a +5.45dB BOOST rather than a cut to reach -21.7 effective — the
    /// one entry in this table where TrimDb is positive). This is a measured best-effort, not a
    /// verdict: the owner's in-game A/B (<see cref="_UnhandledKeyInput"/>) is what actually confirms
    /// "comparable."</para>
    /// </summary>
    private static readonly Dictionary<DayPhase, ComposedTrack> ComposedTracks = new()
    {
        [DayPhase.Morning] = new ComposedTrack("day-first-light", "res://assets/audio/day-first-light.mp3", TrimDb: -8.4f),
        [DayPhase.Evening] = new ComposedTrack("town-dusk", "res://assets/audio/town-dusk.mp3", TrimDb: -8f),
        [DayPhase.Camp] = new ComposedTrack("night-still-long", "res://assets/audio/night-still-long.mp3", TrimDb: 5.45f),
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
    /// Loads the composed track mapped to <paramref name="phase"/>, or null if none is mapped or it
    /// failed to load. Test-only entry point into <see cref="LoadComposed"/> — the SAME loader
    /// <see cref="ResolveBed"/> uses at runtime, so a green census means the game's actual code path
    /// resolved real audio, not a parallel check that could quietly drift from what actually plays.
    /// </summary>
    public static AudioStream? LoadComposedTrackForCensus(DayPhase phase) =>
        ComposedTracks.TryGetValue(phase, out var track) ? LoadComposed(track) : null;

    private readonly List<AudioStreamPlayer> _voices = new();
    private int _nextVoice;

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

    public override void _Ready()
    {
        Name = "AudioDirector";

        if (!string.IsNullOrEmpty(OS.GetEnvironment(MuteEnvVar)))
        {
            Muted = true;
        }

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
    }

    /// <summary>Fires <paramref name="cue"/> on the next pooled voice. No-op while <see cref="Muted"/>.</summary>
    public void Play(Cue cue)
    {
        if (Muted || _voices.Count == 0)
        {
            return;
        }

        var voice = _voices[_nextVoice];
        _nextVoice = (_nextVoice + 1) % _voices.Count;
        voice.Stream = SfxLibrary.Get(cue);
        voice.Play();
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
    /// needed — <c>Node</c> already receives unhandled input directly.
    /// </summary>
    public override void _UnhandledKeyInput(InputEvent @event)
    {
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

    /// <summary>Silences everything and stops the bed, or restores it. Kept as one switch so a future
    /// options screen has exactly one thing to bind.</summary>
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
    }

    public override void _Process(double delta)
    {
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

        rising.VolumeDb = Mathf.Lerp(SilentDb, MusicDb + risingTrim, progress);
        falling.VolumeDb = Mathf.Lerp(MusicDb + fallingTrim, SilentDb, progress);

        if (progress >= 1f)
        {
            falling.Stop();
            _fadeElapsed = -1;
        }
    }
}
