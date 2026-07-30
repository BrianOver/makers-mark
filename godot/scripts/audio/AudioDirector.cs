using System;
using System.Collections.Generic;
using GameSim.Contracts;
using Godot;

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

    /// <summary>Bed level in dB. Well under the SFX so cues always read over the music.</summary>
    private const float MusicDb = -16f;

    private const float SilentDb = -60f;

    private readonly List<AudioStreamPlayer> _voices = new();
    private int _nextVoice;

    private AudioStreamPlayer _musicA = null!;
    private AudioStreamPlayer _musicB = null!;

    /// <summary>True when <see cref="_musicA"/> is the one currently fading UP.</summary>
    private bool _aIsActive = true;

    /// <summary>Seconds into the current crossfade, or -1 when no fade is in flight (mirrors the
    /// accumulated-delta idiom used by <c>TabFade</c> and <c>DayPhaseTint</c> — no engine Tween).</summary>
    private double _fadeElapsed = -1;

    private DayPhase? _phase;

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

    public override void _Ready()
    {
        Name = "AudioDirector";

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
        if (_phase == phase)
        {
            return;
        }

        _phase = phase;

        if (Muted)
        {
            return;
        }

        var incoming = _aIsActive ? _musicB : _musicA;
        incoming.Stream = MusicBed.For(phase);
        incoming.VolumeDb = SilentDb;
        incoming.Play();

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

        rising.VolumeDb = Mathf.Lerp(SilentDb, MusicDb, progress);
        falling.VolumeDb = Mathf.Lerp(MusicDb, SilentDb, progress);

        if (progress >= 1f)
        {
            falling.Stop();
            _fadeElapsed = -1;
        }
    }
}
