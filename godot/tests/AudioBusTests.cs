#if GDUNIT_TESTS
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using Godot;
using GodotClient.Audio;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// U-T4-1: the bus graph <see cref="AudioBuses"/> builds in code, and the limiter sitting on Master.
///
/// <para><b>Graph shape and routing only — never mixed output.</b> Godot's headless dummy audio driver
/// still constructs the full <see cref="AudioServer"/> bus graph (buses, sends, effects all really
/// exist), but it mixes no audio, so <c>GetBusPeakVolumeLeftDb</c> and friends return meaningless
/// numbers under it. Every assertion here reads graph structure (bus count, names, sends, effect
/// count/type) rather than anything that depends on actual sample mixing.</para>
///
/// <para><b>Idempotency is the load-bearing property, not a nice-to-have.</b> The engine test suite
/// constructs many <see cref="AudioDirector"/>s against the one process-wide <see cref="AudioServer"/>
/// (this file's own two-director test does exactly that), so <see cref="AudioBuses.EnsureBuilt"/>
/// must be safe to call any number of times without growing the graph — a regression here would leave
/// fifty buses behind and a false-green graph test in front of them.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class AudioBusTests
{
    private static readonly HashSet<string> ExpectedBusNames =
        new() { AudioBuses.Master, AudioBuses.Music, AudioBuses.Sfx, AudioBuses.SfxLoop, AudioBuses.Narrator };

    [TestCase]
    public void EnsureBuilt_CreatesExactlyTheFiveNamedBuses()
    {
        AudioBuses.EnsureBuilt();

        AssertThat(AudioServer.BusCount)
            .OverrideFailureMessage(
                $"Expected exactly 5 buses (Master/Music/Sfx/SfxLoop/Narrator), found {AudioServer.BusCount}.")
            .IsEqual(5);

        var names = Enumerable.Range(0, AudioServer.BusCount)
            .Select(i => AudioServer.GetBusName(i).ToString())
            .ToHashSet();
        AssertThat(names.SetEquals(ExpectedBusNames))
            .OverrideFailureMessage(
                $"Bus name set was {{{string.Join(", ", names)}}}, expected " +
                $"{{{string.Join(", ", ExpectedBusNames)}}}.")
            .IsTrue();
    }

    [TestCase]
    public void EveryCategoryBus_SendsToTheBusItShouldSendTo()
    {
        AudioBuses.EnsureBuilt();

        AssertSends(AudioBuses.Music, AudioBuses.Master);
        AssertSends(AudioBuses.Sfx, AudioBuses.Master);
        AssertSends(AudioBuses.Narrator, AudioBuses.Master);
        AssertSends(AudioBuses.SfxLoop, AudioBuses.Sfx);
    }

    private static void AssertSends(string bus, string expectedTarget)
    {
        var index = AudioServer.GetBusIndex(bus);
        var actualTarget = AudioServer.GetBusSend(index).ToString();
        AssertThat(actualTarget)
            .OverrideFailureMessage($"{bus} sends to '{actualTarget}', expected '{expectedTarget}'.")
            .IsEqual(expectedTarget);
    }

    [TestCase]
    public void Master_CarriesExactlyOneHardLimiterAtTheCeiling()
    {
        AudioBuses.EnsureBuilt();

        var masterIndex = AudioServer.GetBusIndex(AudioBuses.Master);
        AssertThat(AudioServer.GetBusEffectCount(masterIndex))
            .OverrideFailureMessage("Master should carry exactly one effect: the hard limiter.")
            .IsEqual(1);

        var effect = AudioServer.GetBusEffect(masterIndex, 0);
        var limiter = effect as AudioEffectHardLimiter;
        AssertThat(limiter)
            .OverrideFailureMessage("Master's one effect is not an AudioEffectHardLimiter.")
            .IsNotNull();
        AssertThat(limiter!.CeilingDb)
            .OverrideFailureMessage($"Limiter ceiling is {limiter.CeilingDb}dB, expected -1.0dB.")
            .IsEqual(-1.0f);
    }

    [TestCase]
    public void EveryPlayerTheDirectorOwns_IsRoutedToItsExpectedBus()
    {
        var director = new AudioDirector();
        try
        {
            ((SceneTree)Engine.GetMainLoop()).Root.AddChild(director);

            var players = director.GetChildren().OfType<AudioStreamPlayer>().ToList();
            AssertThat(players.Count)
                .OverrideFailureMessage("AudioDirector owns no AudioStreamPlayer children — did construction change?")
                .IsGreater(0);

            foreach (var player in players)
            {
                var name = player.Name.ToString();
                var expectedBus = ExpectedBusFor(name);
                var actualBus = player.Bus.ToString();
                AssertThat(actualBus)
                    .OverrideFailureMessage($"{name} is routed to bus '{actualBus}', expected '{expectedBus}'.")
                    .IsEqual(expectedBus);
            }
        }
        finally
        {
            director.Free();
        }
    }

    /// <summary>Voice0..VoiceN -> Sfx; MusicA/MusicB -> Music; LoopVoice -> SfxLoop; NarratorVoice ->
    /// Narrator — the routing this unit's brief specifies at <c>AudioDirector._Ready</c>.</summary>
    private static string ExpectedBusFor(string playerName) => playerName switch
    {
        _ when playerName.StartsWith("Voice") => AudioBuses.Sfx,
        _ when playerName.StartsWith("Music") => AudioBuses.Music,
        "LoopVoice" => AudioBuses.SfxLoop,
        "NarratorVoice" => AudioBuses.Narrator,
        _ => throw new System.InvalidOperationException($"Unrecognised AudioDirector player '{playerName}' — add it to this test's routing table."),
    };

    [TestCase]
    public void EnsureBuilt_CalledTwice_StillLeavesExactlyFiveBuses()
    {
        AudioBuses.EnsureBuilt();
        AudioBuses.EnsureBuilt();

        AssertThat(AudioServer.BusCount)
            .OverrideFailureMessage(
                $"Calling EnsureBuilt() twice changed BusCount to {AudioServer.BusCount} — it must be idempotent.")
            .IsEqual(5);

        var masterIndex = AudioServer.GetBusIndex(AudioBuses.Master);
        AssertThat(AudioServer.GetBusEffectCount(masterIndex))
            .OverrideFailureMessage("Calling EnsureBuilt() twice stacked a second limiter on Master.")
            .IsEqual(1);
    }

    [TestCase]
    public void ConstructingTwoAudioDirectors_StillLeavesExactlyFiveBuses()
    {
        var first = new AudioDirector();
        var second = new AudioDirector();
        try
        {
            var root = ((SceneTree)Engine.GetMainLoop()).Root;
            root.AddChild(first);
            root.AddChild(second);

            AssertThat(AudioServer.BusCount)
                .OverrideFailureMessage(
                    $"Two AudioDirectors left BusCount at {AudioServer.BusCount} — EnsureBuilt() must be " +
                    "safe to call from every director's _Ready without growing the graph.")
                .IsEqual(5);
        }
        finally
        {
            first.Free();
            second.Free();
        }
    }
}
#endif
