#if GDUNIT_TESTS
using System;
using System.Reflection;
using GameSim.Presentation;
using GdUnit4;
using Godot;
using GodotClient.Audio;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// U-T4-10: the bed steps back 6dB and SFX steps back 4dB while a narrator line audibly plays — a
/// bus-level duck (<see cref="AudioBuses"/>, U-T4-1), so it stacks under every mixer fader and
/// <c>ComposedTrack.TrimDb</c> instead of touching a player's own <c>VolumeDb</c>.
///
/// <para><b>Deterministic, like every other timed AudioDirector behaviour in this suite.</b> <see
/// cref="AudioDirector._Process"/> is called directly with a chosen delta (the same idiom
/// <c>AudioTests.StartLoop_ArmsTheLoopVoice_AndStopLoopFadesThenStops</c> already uses for the
/// bellows' own release fade) rather than pumping real engine frames or waiting on a real
/// recording's wall-clock duration.</para>
///
/// <para><b>Why reflection reaches <c>_narratorVoice</c>.</b> The whole point of this unit's release
/// contract is that it reacts to <c>AudioStreamPlayer.Playing</c> going false for ANY reason — not
/// just a line reaching its natural end — so the release test needs to flip that flag on demand
/// without waiting out a real recording. <c>AudioDirector</c> exposes no public "stop the narrator"
/// seam (by design: nothing outside this class should be able to cut a line off), so this borrows the
/// same reflection idiom <c>ActionFeedbackTextMatchesTimingTests.InvokeConfirm</c> already uses for
/// "assert behaviour that has no public surface of its own."</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class NarratorDuckTests
{
    // The task's own contract numbers — pinned as literals here on purpose. A test asserting the
    // ARITHMETIC AudioDirector computes internally would prove nothing; pinning "6dB" and "4dB" as
    // independent constants is what makes a future accidental change (or a well-meant retune with no
    // sign-off) show up as a red test instead of a silent drift.
    private const float ExpectedMusicDuckDb = 6f;
    private const float ExpectedSfxDuckDb = 4f;

    [TestCase]
    public void VoicedNarratorLine_DucksMusicBy6dbAndSfxBy4db_AsARampNotASnap()
    {
        var director = new AudioDirector();
        try
        {
            ((SceneTree)Engine.GetMainLoop()).Root.AddChild(director);
            var musicIndex = AudioServer.GetBusIndex(AudioBuses.Music);
            var sfxIndex = AudioServer.GetBusIndex(AudioBuses.Sfx);

            AssertFloat(AudioServer.GetBusVolumeDb(musicIndex))
                .OverrideFailureMessage("Music bus is not at its base level before anything spoke — this test's premise is wrong.")
                .IsEqual(AudioBuses.MusicBusDb);
            AssertFloat(AudioServer.GetBusVolumeDb(sfxIndex))
                .OverrideFailureMessage("Sfx bus is not at its base level before anything spoke — this test's premise is wrong.")
                .IsEqual(AudioBuses.SfxBusDb);

            SpeakAVoicedLine(director);

            // One tiny step in: moving toward the duck target, but nowhere near it yet — an instant
            // jump here would read as a snap, not a duck, on a real narrator line.
            director._Process(0.01);
            AssertFloat(AudioServer.GetBusVolumeDb(musicIndex))
                .OverrideFailureMessage("Music bus jumped to its ducked level in one 10ms step instead of ramping.")
                .IsLess(AudioBuses.MusicBusDb);
            AssertFloat(AudioServer.GetBusVolumeDb(musicIndex))
                .OverrideFailureMessage("Music bus already reached its full duck after only 10ms — that is a snap, not the attack ramp.")
                .IsGreater(AudioBuses.MusicBusDb - ExpectedMusicDuckDb);

            // Comfortably past the attack window: both buses sitting exactly at their ducked target.
            director._Process(1.0);
            AssertFloat(AudioServer.GetBusVolumeDb(musicIndex))
                .OverrideFailureMessage(
                    $"Music bus should be ducked {ExpectedMusicDuckDb}dB below its {AudioBuses.MusicBusDb}dB base " +
                    $"while the narrator speaks; reads {AudioServer.GetBusVolumeDb(musicIndex)}dB.")
                .IsEqual(AudioBuses.MusicBusDb - ExpectedMusicDuckDb);
            AssertFloat(AudioServer.GetBusVolumeDb(sfxIndex))
                .OverrideFailureMessage(
                    $"Sfx bus should be ducked {ExpectedSfxDuckDb}dB below its {AudioBuses.SfxBusDb}dB base " +
                    $"while the narrator speaks; reads {AudioServer.GetBusVolumeDb(sfxIndex)}dB.")
                .IsEqual(AudioBuses.SfxBusDb - ExpectedSfxDuckDb);
        }
        finally
        {
            // Drives the release to completion before freeing — AudioServer bus volumes are process-
            // global (AudioDirector._ExitTree's own safety net covers a director that skips this), but a
            // clean release here is the honest end state for the NEXT test in this same suite/process.
            StopNarratorVoiceForTest(director);
            director._Process(1.0);
            director.Free();
        }
    }

    /// <summary>
    /// The release half — and the one this unit's own brief calls out as the real risk: "a duck that
    /// latches on a line that never fires its completion leaves the whole mix 6dB down for the rest of
    /// the session." Proves the release triggers off <c>Playing</c> going false (via a direct
    /// <see cref="AudioStreamPlayer.Stop"/>, never the line's own natural end or a Finished signal),
    /// ramps rather than snaps, and lands the buses back EXACTLY at their base levels — not "close to"
    /// or "audibly restored," but the identical dB the census/budget math assumes between lines.
    /// </summary>
    [TestCase]
    public void ReleaseTriggersOnPlayingGoingFalse_RampsThenLandsExactlyAtBaseLevels()
    {
        var director = new AudioDirector();
        try
        {
            ((SceneTree)Engine.GetMainLoop()).Root.AddChild(director);
            var musicIndex = AudioServer.GetBusIndex(AudioBuses.Music);
            var sfxIndex = AudioServer.GetBusIndex(AudioBuses.Sfx);

            SpeakAVoicedLine(director);
            director._Process(1.0); // land the attack fully before releasing, so this test isolates release

            // Cut the line off directly — never its own natural end, never a Finished signal. If the
            // release depended on either, this would leave the mix latched ducked, which is exactly the
            // defect this unit exists to close.
            StopNarratorVoiceForTest(director);

            // First tick: _Process notices Playing went false and ARMS the release (the poll runs at
            // the end of the method, after that frame's own ramp step) — the ramp itself has not moved
            // yet, so the bus is still sitting at its fully-ducked level immediately after this call.
            director._Process(0.001);
            AssertFloat(AudioServer.GetBusVolumeDb(musicIndex))
                .OverrideFailureMessage("Music bus should still read fully ducked the instant the release is armed, before its ramp has run at all.")
                .IsEqual(AudioBuses.MusicBusDb - ExpectedMusicDuckDb);

            // Second tick: NOW the release ramp itself runs. One small step in: much closer to ducked
            // than to base — proving a gradual release, not an instant snap back to level.
            director._Process(0.01);
            AssertFloat(AudioServer.GetBusVolumeDb(musicIndex))
                .OverrideFailureMessage("Music bus jumped straight back to base in one 10ms release step instead of releasing gradually.")
                .IsLess(AudioBuses.MusicBusDb - ExpectedMusicDuckDb + 1f);

            // Comfortably past the release window: both buses back at EXACTLY their base level — the
            // arithmetic a fresh, un-ducked mix (and every MixBudget/AudioBusTests assertion) assumes.
            director._Process(1.0);
            AssertFloat(AudioServer.GetBusVolumeDb(musicIndex))
                .OverrideFailureMessage(
                    $"Music bus never fully released — reads {AudioServer.GetBusVolumeDb(musicIndex)}dB, " +
                    $"expected exactly its {AudioBuses.MusicBusDb}dB base once the line ended.")
                .IsEqual(AudioBuses.MusicBusDb);
            AssertFloat(AudioServer.GetBusVolumeDb(sfxIndex))
                .OverrideFailureMessage(
                    $"Sfx bus never fully released — reads {AudioServer.GetBusVolumeDb(sfxIndex)}dB, " +
                    $"expected exactly its {AudioBuses.SfxBusDb}dB base once the line ended.")
                .IsEqual(AudioBuses.SfxBusDb);

            // A second line, spoken AFTER the first fully released, must duck again from a clean base —
            // proving the duck is reusable and not a one-shot that only ever worked once.
            SpeakAVoicedLine(director);
            director._Process(1.0);
            AssertFloat(AudioServer.GetBusVolumeDb(musicIndex))
                .OverrideFailureMessage("A second narrator line after a full release did not duck the Music bus again.")
                .IsEqual(AudioBuses.MusicBusDb - ExpectedMusicDuckDb);
        }
        finally
        {
            StopNarratorVoiceForTest(director);
            director._Process(1.0);
            director.Free();
        }
    }

    /// <summary>A text-only request (no recording, or dropped because the narrator was already
    /// speaking) has nothing audible to compete with the bed/SFX for — ducking around silence would be
    /// a level change with no reason, so <see cref="AudioDirector.Muted"/> (which forces every
    /// <c>SpeakNarrator</c> call text-only) must leave both buses untouched.</summary>
    [TestCase]
    public void UnvoicedNarratorRequest_NeverDucksAnything()
    {
        var director = new AudioDirector();
        try
        {
            ((SceneTree)Engine.GetMainLoop()).Root.AddChild(director);
            director.SetMuted(true); // forces every SpeakNarrator call below to be text-only

            var musicIndex = AudioServer.GetBusIndex(AudioBuses.Music);
            var sfxIndex = AudioServer.GetBusIndex(AudioBuses.Sfx);

            director.SpeakNarrator(NarratorVoiceDirector.Trigger.VigilOpening, campaignId: 1, eventId: 1);
            AssertBool(director.LastNarratorLine!.Value.Voiced)
                .OverrideFailureMessage("This test needs an UNVOICED (text-only) request — SetMuted(true) should have forced that.")
                .IsFalse();

            director._Process(1.0);
            AssertFloat(AudioServer.GetBusVolumeDb(musicIndex))
                .OverrideFailureMessage("Music bus moved even though the narrator line was never actually voiced.")
                .IsEqual(AudioBuses.MusicBusDb);
            AssertFloat(AudioServer.GetBusVolumeDb(sfxIndex))
                .OverrideFailureMessage("Sfx bus moved even though the narrator line was never actually voiced.")
                .IsEqual(AudioBuses.SfxBusDb);
        }
        finally
        {
            director.Free();
        }
    }

    /// <summary>Speaks a real, committed narrator line and fails loudly if it did not actually voice —
    /// every id <c>NarratorLines.AllAudioIds</c> names is proven to resolve to real audio by
    /// <c>NarratorAudioTests.EveryNarratorLine_ResolvesToRealLoadableAudio</c>, so this should never
    /// legitimately come back unvoiced on an unmuted, idle director.</summary>
    private static void SpeakAVoicedLine(AudioDirector director)
    {
        director.SpeakNarrator(NarratorVoiceDirector.Trigger.VigilOpening, campaignId: 1, eventId: 1);
        AssertBool(director.LastNarratorLine?.Voiced ?? false)
            .OverrideFailureMessage(
                "SpeakNarrator did not voice a VigilOpening line on a fresh, unmuted director — either the "
                + "narrator audio library regressed (see NarratorAudioTests) or the director's own idle-state "
                + "assumption this test relies on broke.")
            .IsTrue();
    }

    /// <summary>Reflection seam onto the one private field this suite needs to stop directly — see the
    /// class doc for why no public "stop the narrator" API exists to call instead.</summary>
    private static void StopNarratorVoiceForTest(AudioDirector director)
    {
        var field = typeof(AudioDirector).GetField("_narratorVoice", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException(
                "AudioDirector._narratorVoice not found by reflection — was it renamed? Update this test alongside it.");
        var player = field.GetValue(director) as AudioStreamPlayer;
        player?.Stop();
    }
}
#endif
