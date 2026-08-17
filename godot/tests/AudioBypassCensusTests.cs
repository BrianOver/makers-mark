#if GDUNIT_TESTS
using System.Collections.Generic;
using GdUnit4;
using Godot;
using GodotClient.Audio;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U-T4-5: the bypass sweep's own guard. Two real bypasses shipped before this unit — the craft grade
/// sting (<c>ForgePanel.ShowCeremony</c> played a bare <c>AudioStreamPlayer</c> of its own, parented
/// directly to the panel, never routed to any <see cref="AudioBuses"/> bus) and the orphan
/// <c>ForgePanel._hammerSfx</c> (constructed and parented, never played, at all) — and both were reached
/// by grepping for a symbol, not by any test. A grep only catches what you already suspect exists.
///
/// <para><b>Why this walks the LIVE tree instead of grepping source.</b> The failure class here is "some
/// future panel adds its own <c>AudioStreamPlayer</c>/<c>AudioStreamPlayer2D</c>/<c>AudioStreamPlayer3D</c>
/// and forgets to parent it under <see cref="AudioDirector"/>" — once that happens, every budget
/// <c>MixBudget</c> (U-T4-2) and every duck U-T4-10 adds are silently inapplicable to it, exactly as they
/// were to the grade sting. A hand-listed array of "known audio players" would need updating every time
/// one is added and would never catch the NEXT bypass — the same lesson <c>MixBudgetCensusTests</c>' own
/// class doc already states for cues, and <c>AudioTests</c>/<c>NarratorAudioTests</c> learned the hard
/// way when 128 new assets once shipped untested under a green suite built on a literal id array.
/// Walking <see cref="MainUi"/>'s actual instantiated scene tree means a new offender is caught the day
/// it is added, with zero maintenance from whoever adds it.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class AudioBypassCensusTests
{
    /// <summary>
    /// Every audio-player node this build is currently known to intentionally leave outside
    /// <see cref="AudioDirector"/>, by <see cref="Node.GetPath"/> string — empty today. Mirrors
    /// <see cref="MixBudget.PendingExemptions"/>'s own shape: a future addition here is a deliberate,
    /// reviewed diff naming WHY that specific node is allowed to bypass mixing/ducking/muting, never a
    /// silent widening of what the census accepts.
    /// </summary>
    private static readonly HashSet<string> AllowedStrayPlayerPaths = new();

    [TestCase]
    public void EveryAudioPlayerInTheLiveTree_IsOwnedByTheDirector()
    {
        var ui = MountMainUi();
        try
        {
            AssertThat(ui.Audio)
                .OverrideFailureMessage("MainUi mounted with no AudioDirector — this census has nothing to check ownership against.")
                .IsNotNull();

            var stray = new List<string>();
            CollectStrayPlayers(ui, ui.Audio, stray);

            var unexplained = stray.FindAll(path => !AllowedStrayPlayerPaths.Contains(path));

            AssertInt(unexplained.Count)
                .OverrideFailureMessage(
                    "Found audio player node(s) NOT parented under AudioDirector — every MixBudget "
                    + "band, every fader, Mute, and U-T4-10's narrator duck silently do not apply to "
                    + "these: " + string.Join(", ", unexplained)
                    + ". Either reparent the offending node under AudioDirector and route it through "
                    + "Play()/StartLoop(), or if it must genuinely stay outside (with a written reason), "
                    + "add its path to AllowedStrayPlayerPaths.")
                .IsEqual(0);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Staleness guard the other direction, ConstitutionTests-style: an entry in
    /// <see cref="AllowedStrayPlayerPaths"/> naming a node that is no longer actually stray (it got
    /// reparented under the director, or deleted) must not sit there forever as dead cover for a future
    /// regression at the same path.</summary>
    [TestCase]
    public void EveryAllowedStrayPlayerPath_IsStillActuallyStray()
    {
        if (AllowedStrayPlayerPaths.Count == 0)
        {
            return; // nothing to check — the common case, and the state this repo should stay in
        }

        var ui = MountMainUi();
        try
        {
            var stray = new List<string>();
            CollectStrayPlayers(ui, ui.Audio, stray);
            var strayNow = new HashSet<string>(stray);

            foreach (var allowed in AllowedStrayPlayerPaths)
            {
                AssertBool(strayNow.Contains(allowed))
                    .OverrideFailureMessage(
                        $"'{allowed}' is listed in AllowedStrayPlayerPaths but is no longer a stray "
                        + "audio player — remove the stale entry.")
                    .IsTrue();
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// Recursive tree walk from <paramref name="node"/>: records the path of every
    /// <see cref="AudioStreamPlayer"/>/<see cref="AudioStreamPlayer2D"/>/<see cref="AudioStreamPlayer3D"/>
    /// found, EXCEPT anywhere under <paramref name="director"/> itself — its own children (the voice
    /// pool, MusicA/MusicB, LoopVoice, NarratorVoice; see <c>AudioBusTests</c> for that enumeration) are
    /// by definition owned, and the walk does not even descend into that subtree.
    /// </summary>
    private static void CollectStrayPlayers(Node node, AudioDirector director, List<string> stray)
    {
        if (node == director)
        {
            return;
        }

        if (node is AudioStreamPlayer or AudioStreamPlayer2D or AudioStreamPlayer3D)
        {
            stray.Add(node.GetPath().ToString());
        }

        foreach (var child in node.GetChildren())
        {
            CollectStrayPlayers(child, director, stray);
        }
    }
}
#endif
