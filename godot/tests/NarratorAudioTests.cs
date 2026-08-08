#if GDUNIT_TESTS
using System.Linq;
using GdUnit4;
using Godot;
using GodotClient.Audio;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// The narrator's recordings are real and reachable.
///
/// <para>This is the guard against this repo's oldest wound: committed assets nobody can hear. Music
/// got it only after three tracks had sat on disk unwired; the narrator gets it before that can
/// happen. Every line the game can choose resolves through the same <c>res://</c> path
/// <c>AudioDirector.SpeakNarrator</c> uses at runtime, so a green run means the real code path found
/// real audio — not a parallel check that could drift from what actually plays.</para>
///
/// <para><b><see cref="RequireGodotRuntimeAttribute"/> is load-bearing, not decoration.</b> Without
/// it this suite took the whole test host down with an access violation — 950 healthy tests became
/// 899 and a fatal, no assertion failure, no named test in the summary. The stack said it plainly
/// once it was read instead of guessed at: <c>0xC0000005 at Godot.ResourceLoader..cctor()</c>. A
/// gdUnit case that does not declare the runtime runs without one, and the first touch of
/// <c>ResourceLoader</c> faults inside its static constructor. Any test here that resolves a
/// <c>res://</c> path needs this attribute; the cost of forgetting is not a red test, it is a dead
/// runtime and a suite that silently shrinks.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class NarratorAudioTests
{
    [TestCase]
    public void EveryNarratorLine_ResolvesToRealLoadableAudio()
    {
        var ids = NarratorLines.AllAudioIds;
        AssertInt(ids.Count).IsEqual(20);

        foreach (var audioId in ids)
        {
            var path = NarratorLines.ResourcePath(audioId);

            AssertBool(ResourceLoader.Exists(path)).OverrideFailureMessage(
                $"narrator line '{audioId}' has no recording at {path}. The line renders on screen "
                + "and is never heard.").IsTrue();

            AssertBool(GD.Load<AudioStream>(path) is not null).OverrideFailureMessage(
                $"narrator line '{audioId}' exists at {path} but did not load as an AudioStream.")
                .IsTrue();
        }
    }

    /// <summary>
    /// The other direction: a recording committed under a name nothing can ask for. That is this
    /// repo's oldest defect shape — art and audio on disk that no code path reaches — and it is
    /// invisible without a check like this, because everything still passes and the player simply
    /// never hears it.
    /// </summary>
    [TestCase]
    public void NoCommittedRecording_IsUndeclared()
    {
        var dir = ProjectSettings.GlobalizePath("res://assets/audio/narrator/");
        if (!System.IO.Directory.Exists(dir)) return; // nothing recorded yet is legal

        var declared = new System.Collections.Generic.HashSet<string>(NarratorLines.AllAudioIds);
        var orphans = System.IO.Directory.GetFiles(dir, "*.ogg")
            .Select(System.IO.Path.GetFileNameWithoutExtension)
            .Where(id => id is not null && !declared.Contains(id))
            .ToList();

        AssertInt(orphans.Count).OverrideFailureMessage(
            "These recordings are committed but nothing can ever ask for them — a line renamed or "
            + "removed from NarratorVoiceDirector.Lines leaves its audio orphaned: "
            + string.Join(", ", orphans)).IsEqual(0);
    }

    /// <summary>A narrator line is one sentence, said once. A stray reimport flipping loop on would
    /// leave a death epitaph repeating under the ledger forever.</summary>
    [TestCase]
    public void NoNarratorLine_IsMarkedToLoop()
    {
        foreach (var audioId in NarratorLines.AllAudioIds)
        {
            var path = NarratorLines.ResourcePath(audioId);
            if (!ResourceLoader.Exists(path)) continue;

            if (GD.Load<AudioStream>(path) is AudioStreamOggVorbis ogg)
            {
                AssertBool(ogg.Loop).OverrideFailureMessage(
                    $"narrator line '{audioId}' is set to loop.").IsFalse();
            }
        }
    }
}
#endif
