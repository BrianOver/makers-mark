using System.Text.Json;
using Godot;

namespace GodotClient.Ui;

/// <summary>
/// Shell-layer window preferences (2026-08-02 shell-and-audio plan, U3/U4; KTD-D): fullscreen is
/// the first shell preference living at <c>user://</c> — same idiom as <c>MainUi.ClockSettings</c>/
/// <c>TutorialFlow</c>: a UI preference, NEVER the sim save (KTD2). Both hosts that can flip
/// fullscreen — the title screen (<c>NewGameSelect</c>, F11 or Settings) and the in-game HUD/system
/// menu (<c>MainUi</c>, F11, the HUD button, or Settings) — read and write through here so neither
/// can drift from the other, and the choice survives a restart from a cold boot: the title screen
/// is <c>project.godot</c>'s main scene, so it is the one place a persisted preference can actually
/// be re-applied to a freshly opened OS window (a scene change afterward never recreates that
/// window, so nothing else needs to call <see cref="ApplyPersisted"/> again).
/// </summary>
public static class UiSettings
{
    private const string Path = "user://ui_settings.json";

    /// <summary>
    /// Test-only stand-in for the real OS window mode (the seam <c>MainUi</c> established for its
    /// own F11/HUD-button toggle pre-U3, now shared here so the title screen's copy uses the exact
    /// same seam instead of a second one). Verified empirically (throwaway headless script, per
    /// repo convention): under <c>--headless</c>, <see cref="DisplayServer.WindowSetMode"/> is a
    /// no-op and <see cref="DisplayServer.WindowGetMode"/> always reports the same value regardless
    /// of what was just set — a test asserting the real window state would pass or fail independent
    /// of this code, proving nothing. Null in production (real <see cref="DisplayServer"/>); a test
    /// sets a starting mode here to prove the TOGGLE LOGIC actually flips it, and must reset it to
    /// null when done so later suites see the real engine again.
    /// </summary>
    public static DisplayServer.WindowMode? TestWindowMode;

    private static DisplayServer.WindowMode CurrentWindowMode() => TestWindowMode ?? DisplayServer.WindowGetMode();

    /// <summary>Whether the OS window is currently fullscreen, either the borderless or exclusive
    /// mode — both read the same to the player and to every caller here.</summary>
    public static bool IsFullscreen() => CurrentWindowMode()
        is DisplayServer.WindowMode.Fullscreen or DisplayServer.WindowMode.ExclusiveFullscreen;

    private static void Apply(DisplayServer.WindowMode mode)
    {
        if (TestWindowMode is not null)
        {
            TestWindowMode = mode;
        }
        else
        {
            DisplayServer.WindowSetMode(mode);
        }
    }

    /// <summary>
    /// Set fullscreen to an explicit desired state (the Settings checkbox's own verb — it knows
    /// WHAT it wants, unlike F11/the HUD button, which only know "flip it"). Runtime-only via
    /// <see cref="DisplayServer"/> (KTD-D — never <c>project.godot</c>, deny-listed for agents), and
    /// persists the choice so it survives a restart.
    /// </summary>
    public static void SetFullscreen(bool fullscreen)
    {
        Apply(fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed);
        SaveFullscreen(fullscreen);
    }

    /// <summary>Flip fullscreen (F11 / the HUD button) and persist the result. Returns the new
    /// state so a caller can sync its own toggle control either way it was triggered.</summary>
    public static bool ToggleFullscreen()
    {
        var next = !IsFullscreen();
        SetFullscreen(next);
        return next;
    }

    /// <summary>
    /// Re-apply a persisted fullscreen preference to the real window. Called once, from the title
    /// screen's <c>_Ready</c> — see the class doc for why that is the one correct call site. No-op
    /// on a fresh install (nothing persisted yet — stays windowed, matching <c>project.godot</c>'s
    /// own default) and no-op if the window is already in the persisted mode.
    /// </summary>
    public static void ApplyPersisted()
    {
        if (LoadFullscreen() is true && !IsFullscreen())
        {
            Apply(DisplayServer.WindowMode.Fullscreen);
        }
    }

    /// <summary>Null when no settings file exists yet — a fresh install stays windowed, the
    /// project's own default.</summary>
    public static bool? LoadFullscreen()
    {
        if (!Godot.FileAccess.FileExists(Path))
        {
            return null;
        }

        using var file = Godot.FileAccess.Open(Path, Godot.FileAccess.ModeFlags.Read);
        if (file is null)
        {
            return null; // unreadable — fail soft, never block boot
        }

        try
        {
            var data = JsonSerializer.Deserialize<Data>(file.GetAsText());
            return data?.Fullscreen;
        }
        catch (JsonException)
        {
            return null; // corrupt file — fail soft
        }
    }

    public static void SaveFullscreen(bool fullscreen)
    {
        using var file = Godot.FileAccess.Open(Path, Godot.FileAccess.ModeFlags.Write);
        file?.StoreString(JsonSerializer.Serialize(new Data { Fullscreen = fullscreen }));
    }

    /// <summary>Test-only teardown: delete the file so suites never leak a preference across runs
    /// (this store is adapter-side scaffolding, not sim state — safe to wipe).</summary>
    public static void DeleteForTests()
    {
        if (Godot.FileAccess.FileExists(Path))
        {
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(Path));
        }
    }

    private sealed class Data
    {
        public bool Fullscreen { get; set; }
    }
}
