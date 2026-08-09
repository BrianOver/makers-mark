#if GDUNIT_TESTS
using GdUnit4;
using Godot;
using GodotClient.Ui;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// U3/U4 (2026-08-02 shell-and-audio plan, KTD-D): <see cref="UiSettings"/>'s own persistence —
/// the API layer both hosts (<c>NewGameSelect</c>'s Settings checkbox, <c>MainUi</c>'s system menu
/// Settings + F11 + HUD button) share. <see cref="UiSettings.TestWindowMode"/> is the same
/// verified-empirically seam <c>MainUi</c>'s own Fullscreen tests already use — headless
/// <see cref="DisplayServer.WindowSetMode"/> is a no-op, so asserting the real window state would
/// prove nothing (see that seam's own doc).
///
/// <para>Every test clears the persisted file first (<see cref="UiSettings.DeleteForTests"/>) —
/// this store lives at <c>user://ui_settings.json</c>, shared with the real game and every other
/// suite that touches Settings, so a leftover choice from an earlier run must never leak in.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class UiSettingsTests
{
    [TestCase]
    public void FreshInstall_HasNoPersistedPreference()
    {
        UiSettings.DeleteForTests();
        AssertThat(UiSettings.LoadFullscreen() is null).IsTrue();
    }

    [TestCase]
    public void SetFullscreen_AppliesAndPersists_LoadFullscreenReturnsIt()
    {
        UiSettings.TestWindowMode = DisplayServer.WindowMode.Windowed;
        try
        {
            UiSettings.SetFullscreen(true);

            AssertThat(UiSettings.TestWindowMode).IsEqual(DisplayServer.WindowMode.Fullscreen);
            AssertThat(UiSettings.IsFullscreen()).IsTrue();
            AssertThat(UiSettings.LoadFullscreen() == true).IsTrue();

            UiSettings.SetFullscreen(false);
            AssertThat(UiSettings.TestWindowMode).IsEqual(DisplayServer.WindowMode.Windowed);
            AssertThat(UiSettings.LoadFullscreen() == false).IsTrue();
        }
        finally
        {
            UiSettings.TestWindowMode = null;
            UiSettings.DeleteForTests();
        }
    }

    [TestCase]
    public void ToggleFullscreen_FlipsTheCurrentState_EitherDirection()
    {
        UiSettings.TestWindowMode = DisplayServer.WindowMode.Windowed;
        try
        {
            var toFullscreen = UiSettings.ToggleFullscreen();
            AssertThat(toFullscreen).IsTrue();
            AssertThat(UiSettings.IsFullscreen()).IsTrue();

            var toWindowed = UiSettings.ToggleFullscreen();
            AssertThat(toWindowed).IsFalse();
            AssertThat(UiSettings.IsFullscreen()).IsFalse();
        }
        finally
        {
            UiSettings.TestWindowMode = null;
            UiSettings.DeleteForTests();
        }
    }

    /// <summary>KTD-D's whole point: a persisted "fullscreen" choice must actually be re-applied to
    /// the window at cold boot (<c>NewGameSelect._Ready</c> calls this) rather than only sticking
    /// for the rest of the CURRENT session.</summary>
    [TestCase]
    public void ApplyPersisted_WithFullscreenSaved_AppliesItToTheWindow()
    {
        UiSettings.TestWindowMode = DisplayServer.WindowMode.Windowed;
        try
        {
            UiSettings.SaveFullscreen(true); // simulates a PRIOR session's choice, on disk only
            AssertThat(UiSettings.IsFullscreen())
                .OverrideFailureMessage("Setup: the window must start windowed, unaffected by the save alone.")
                .IsFalse();

            UiSettings.ApplyPersisted();

            AssertThat(UiSettings.IsFullscreen())
                .OverrideFailureMessage("A persisted fullscreen preference must be re-applied at boot.")
                .IsTrue();
        }
        finally
        {
            UiSettings.TestWindowMode = null;
            UiSettings.DeleteForTests();
        }
    }

    [TestCase]
    public void ApplyPersisted_WithNothingSaved_LeavesTheWindowAlone()
    {
        UiSettings.DeleteForTests();
        UiSettings.TestWindowMode = DisplayServer.WindowMode.Windowed;
        try
        {
            UiSettings.ApplyPersisted();
            AssertThat(UiSettings.IsFullscreen())
                .OverrideFailureMessage("A fresh install (nothing persisted yet) must stay windowed.")
                .IsFalse();
        }
        finally
        {
            UiSettings.TestWindowMode = null;
        }
    }

    // ---- the mixer (C1, 2026-08-09 shell-and-audio-menu plan) ---------------------------------

    [TestCase]
    public void FreshInstall_MixerDefaultsToFullVolumeAndUnmuted()
    {
        UiSettings.DeleteForTests();
        AssertThat(UiSettings.LoadMasterVolume()).IsEqualApprox(1f, 0.001f);
        AssertThat(UiSettings.LoadMusicVolume()).IsEqualApprox(1f, 0.001f);
        AssertThat(UiSettings.LoadSfxVolume()).IsEqualApprox(1f, 0.001f);
        AssertThat(UiSettings.LoadNarratorVolume()).IsEqualApprox(1f, 0.001f);
        AssertThat(UiSettings.LoadMuted()).IsFalse();
    }

    [TestCase]
    public void SetVolumesAndMuted_RoundTripThroughDisk()
    {
        UiSettings.DeleteForTests();
        try
        {
            UiSettings.SaveMasterVolume(0.6f);
            UiSettings.SaveMusicVolume(0.4f);
            UiSettings.SaveSfxVolume(0.8f);
            UiSettings.SaveNarratorVolume(0f); // legal — the narrator's own TEXT still carries every fact
            UiSettings.SaveMuted(true);

            AssertThat(UiSettings.LoadMasterVolume()).IsEqualApprox(0.6f, 0.001f);
            AssertThat(UiSettings.LoadMusicVolume()).IsEqualApprox(0.4f, 0.001f);
            AssertThat(UiSettings.LoadSfxVolume()).IsEqualApprox(0.8f, 0.001f);
            AssertThat(UiSettings.LoadNarratorVolume()).IsEqualApprox(0f, 0.001f);
            AssertThat(UiSettings.LoadMuted()).IsTrue();
        }
        finally
        {
            UiSettings.DeleteForTests();
        }
    }

    /// <summary>
    /// The bug a naive <c>SaveXxx</c> (a bare <c>new Data { OneField = value }</c>, overwriting the
    /// whole file) would reintroduce the moment two different preferences were ever saved in the
    /// same session: every Save* method must read-modify-write, never silently reset every OTHER
    /// field back to its default.
    /// </summary>
    [TestCase]
    public void SavingOneMixerKey_DoesNotClobberOthersAlreadyOnDisk()
    {
        UiSettings.DeleteForTests();
        try
        {
            UiSettings.SaveFullscreen(true);
            UiSettings.SaveMasterVolume(0.5f);
            UiSettings.SaveMusicVolume(0.25f);

            UiSettings.SaveSfxVolume(0.75f); // a LATER, unrelated save

            AssertThat(UiSettings.LoadFullscreen() == true)
                .OverrideFailureMessage("Saving SfxVolume clobbered the earlier Fullscreen save.")
                .IsTrue();
            AssertThat(UiSettings.LoadMasterVolume())
                .OverrideFailureMessage("Saving SfxVolume clobbered the earlier MasterVolume save.")
                .IsEqualApprox(0.5f, 0.001f);
            AssertThat(UiSettings.LoadMusicVolume())
                .OverrideFailureMessage("Saving SfxVolume clobbered the earlier MusicVolume save.")
                .IsEqualApprox(0.25f, 0.001f);
            AssertThat(UiSettings.LoadSfxVolume()).IsEqualApprox(0.75f, 0.001f);
        }
        finally
        {
            UiSettings.DeleteForTests();
        }
    }

    [TestCase]
    public void CorruptSettingsFile_FallsBackToDefaults_ForTheMixerToo()
    {
        UiSettings.DeleteForTests();
        try
        {
            var path = ProjectSettings.GlobalizePath("user://ui_settings.json");
            System.IO.File.WriteAllText(path, "{ not valid json ][");

            AssertThat(UiSettings.LoadFullscreen() is null)
                .OverrideFailureMessage("A corrupt file must still read as 'nothing persisted' for Fullscreen.")
                .IsTrue();
            AssertThat(UiSettings.LoadMasterVolume()).IsEqualApprox(1f, 0.001f);
            AssertThat(UiSettings.LoadMusicVolume()).IsEqualApprox(1f, 0.001f);
            AssertThat(UiSettings.LoadSfxVolume()).IsEqualApprox(1f, 0.001f);
            AssertThat(UiSettings.LoadNarratorVolume()).IsEqualApprox(1f, 0.001f);
            AssertThat(UiSettings.LoadMuted()).IsFalse();
        }
        finally
        {
            UiSettings.DeleteForTests();
        }
    }

    /// <summary>
    /// C5 — the tripwire the whole unit exists for, styled after <c>ConstitutionTests</c>' pinned-set
    /// pattern (<c>sim/GameSim.Tests/ConstitutionTests.cs</c>). See <see cref="UiSettings.PersistedKeys"/>'s
    /// own doc for the full argument; the short version lives here too because a test file is where a
    /// reviewer's eyes actually land on a red diff:
    ///
    /// <para><b>A setting may change how the world sounds, looks, and reads. It may never change what
    /// the world decides.</b> The concrete risk is an "auto-advance the vigil" or "default vigil
    /// choice" preference — it would delete the one reach-into-the-dark decision the whole day
    /// stages, and it would arrive here looking exactly like a courtesy: one more legal-looking key
    /// in this same store. <c>ClientAuthorityCensusTests</c> (the sim's own "no timers on decisions"
    /// law) cannot see it coming, because nothing about a preference looks like a
    /// <c>Stopwatch</c>.</para>
    ///
    /// <para>So the key set itself is pinned here, in a compiled file. Adding a persisted key must
    /// fail THIS test until the new expected array below is edited in the same PR, by a reviewer who
    /// has asked, in words, whether the new key is a preference (sound/look/text) or a decision
    /// (anything the sim would otherwise decide) — never a quiet new line in
    /// <c>user://ui_settings.json</c> that nobody had to look at twice.</para>
    /// </summary>
    [TestCase]
    public void PersistedSettingsKeys_AreExactlyThePinnedSet()
    {
        string[] expected =
        [
            "Fullscreen",
            "MasterVolume",
            "MusicVolume",
            "SfxVolume",
            "NarratorVolume",
            "Muted",
            "UiScale",
            "KeyBindings",
        ];

        AssertThat(UiSettings.PersistedKeys)
            .OverrideFailureMessage(
                "UiSettings.PersistedKeys no longer matches this pinned set. If you just added a "
                + "setting: it is legal ONLY if it changes how the world sounds, looks, or reads — "
                + "never what the world decides (no auto-advance, no default choice at the vigil, "
                + "nothing that reaches the kernel or the scorer, and every default must be the FULL "
                + "game, never a pre-skipped one). Update the expected array above in the SAME PR, "
                + "with a reviewer's eyes on exactly that question.")
            .ContainsExactly(expected);
    }

    // ---- UI scale (C4) --------------------------------------------------------------------------

    [TestCase]
    public void FreshInstall_UiScaleDefaultsToOne()
    {
        UiSettings.DeleteForTests();
        AssertThat(UiSettings.LoadUiScale()).IsEqualApprox(1f, 0.001f);
    }

    [TestCase]
    public void SetUiScale_AppliesToTheRealWindow_AndPersists()
    {
        UiSettings.DeleteForTests();
        var tree = (SceneTree)Engine.GetMainLoop();
        var originalScale = tree.Root.ContentScaleFactor;
        try
        {
            UiSettings.SetUiScale(1.25f);

            AssertThat(tree.Root.ContentScaleFactor)
                .OverrideFailureMessage(
                    "SetUiScale must apply to the real root Window immediately — unlike WindowMode, "
                    + "ContentScaleFactor is a plain node property, not a deferred OS call, so no "
                    + "TestWindowMode-style seam is needed to observe it even under --headless.")
                .IsEqualApprox(1.25f, 0.001f);
            AssertThat(UiSettings.LoadUiScale()).IsEqualApprox(1.25f, 0.001f);
        }
        finally
        {
            tree.Root.ContentScaleFactor = originalScale; // never leak a scaled window into later suites
            UiSettings.DeleteForTests();
        }
    }

    [TestCase]
    public void SetUiScale_ClampsToTheDocumentedRange()
    {
        UiSettings.DeleteForTests();
        var tree = (SceneTree)Engine.GetMainLoop();
        var originalScale = tree.Root.ContentScaleFactor;
        try
        {
            UiSettings.SetUiScale(9f);
            AssertThat(UiSettings.LoadUiScale()).IsEqualApprox(UiSettings.MaxUiScale, 0.001f);

            UiSettings.SetUiScale(0f);
            AssertThat(UiSettings.LoadUiScale()).IsEqualApprox(UiSettings.MinUiScale, 0.001f);
        }
        finally
        {
            tree.Root.ContentScaleFactor = originalScale;
            UiSettings.DeleteForTests();
        }
    }

    // ---- key bindings (C3) ------------------------------------------------------------------------

    [TestCase]
    public void FreshInstall_HasNoKeyBindingOverride()
    {
        UiSettings.DeleteForTests();
        AssertThat(UiSettings.LoadKeyBinding("move_up") is null).IsTrue();
    }

    [TestCase]
    public void SaveKeyBinding_RoundTripsThroughLoad_AndClearRemovesIt()
    {
        UiSettings.DeleteForTests();
        try
        {
            UiSettings.SaveKeyBinding("move_up", Key.I);
            AssertThat(UiSettings.LoadKeyBinding("move_up")).IsEqual(Key.I);

            UiSettings.ClearKeyBinding("move_up");
            AssertThat(UiSettings.LoadKeyBinding("move_up") is null).IsTrue();
        }
        finally
        {
            UiSettings.DeleteForTests();
        }
    }

    /// <summary>
    /// The actual point of C3's persistence: a rebind saved in a PRIOR session must win over the
    /// hardcoded default the moment the action is (re)created — <c>InputMap</c> itself is never
    /// persisted by the engine, only this choice is. Simulates a cold boot by erasing the action
    /// entirely and re-registering it from scratch, exactly as <c>TownInput</c>/<c>MinigameInput</c>'s
    /// own <c>AddActionIfMissing</c> does on the FIRST real creation of a session.
    /// </summary>
    [TestCase]
    public void ApplyPersistedBindingIfAny_OverridesAFreshlyRegisteredDefault()
    {
        UiSettings.DeleteForTests();
        var originalEvents = InputMap.HasAction("move_up")
            ? InputMap.ActionGetEvents("move_up")
            : new Godot.Collections.Array<InputEvent>();
        try
        {
            UiSettings.SaveKeyBinding("move_up", Key.I);

            InputMap.EraseAction("move_up"); // simulate "this session has never created it yet"
            InputMap.AddAction("move_up");
            InputMap.ActionAddEvent("move_up", new InputEventKey { PhysicalKeycode = Key.W }); // the hardcoded default
            InputMap.ActionAddEvent("move_up", new InputEventKey { PhysicalKeycode = Key.Up });

            UiSettings.ApplyPersistedBindingIfAny("move_up");

            var events = InputMap.ActionGetEvents("move_up");
            AssertThat(events.Count).IsEqual(1);
            AssertThat(((InputEventKey)events[0]).PhysicalKeycode)
                .OverrideFailureMessage("A persisted rebind must replace the hardcoded default, not sit alongside it.")
                .IsEqual(Key.I);
        }
        finally
        {
            InputMap.ActionEraseEvents("move_up");
            foreach (var evt in originalEvents)
            {
                InputMap.ActionAddEvent("move_up", evt);
            }

            UiSettings.DeleteForTests();
        }
    }

    [TestCase]
    public void ApplyPersistedBindingIfAny_WithNothingSaved_LeavesTheDefaultAlone()
    {
        UiSettings.DeleteForTests();
        var originalEvents = InputMap.HasAction("move_up")
            ? InputMap.ActionGetEvents("move_up")
            : new Godot.Collections.Array<InputEvent>();
        try
        {
            InputMap.EraseAction("move_up");
            InputMap.AddAction("move_up");
            InputMap.ActionAddEvent("move_up", new InputEventKey { PhysicalKeycode = Key.W });

            UiSettings.ApplyPersistedBindingIfAny("move_up");

            var events = InputMap.ActionGetEvents("move_up");
            AssertThat(events.Count).IsEqual(1);
            AssertThat(((InputEventKey)events[0]).PhysicalKeycode).IsEqual(Key.W);
        }
        finally
        {
            InputMap.ActionEraseEvents("move_up");
            foreach (var evt in originalEvents)
            {
                InputMap.ActionAddEvent("move_up", evt);
            }

            UiSettings.DeleteForTests();
        }
    }
}
#endif
