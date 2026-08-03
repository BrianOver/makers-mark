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
}
#endif
