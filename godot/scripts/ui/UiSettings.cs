using System.Collections.Generic;
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
    /// Re-apply every persisted WINDOW-level preference (fullscreen, UI scale) to the real window.
    /// Called once, from the title screen's <c>_Ready</c> — see the class doc for why that is the
    /// one correct call site: <c>project.godot</c>'s main scene is the only place a freshly opened
    /// OS window exists to apply anything to, and neither preference needs re-applying after a
    /// later scene change (the root <see cref="Window"/> survives a <c>ChangeSceneToFile</c>
    /// unchanged). No-op on a fresh install for fullscreen (nothing persisted yet — stays windowed,
    /// matching <c>project.godot</c>'s own default); UI scale always re-applies its own default
    /// (1.0) on a fresh install too, which is a true no-op against the engine's own starting value.
    /// </summary>
    public static void ApplyPersisted()
    {
        if (LoadFullscreen() is true && !IsFullscreen())
        {
            Apply(DisplayServer.WindowMode.Fullscreen);
        }

        ApplyUiScaleToWindow(LoadUiScale());
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
        var data = LoadDataOrDefault();
        data.Fullscreen = fullscreen;
        SaveData(data);
    }

    // ---- the mixer (C1, 2026-08-09 shell-and-audio-menu plan) ---------------------------------
    //
    // Growing the SAME file, not a second one (sprawl is this repo's own named recurring defect) —
    // so every Save* below goes through LoadDataOrDefault/SaveData's read-modify-write rather than
    // SaveFullscreen's old shape (a bare `new Data { Fullscreen = fullscreen }`), which would have
    // silently reset every OTHER field back to its default the moment two different preferences were
    // ever saved in the same session.

    /// <summary>1.0 = full volume, matching <c>AudioDirector.DefaultVolume</c> — today's existing
    /// mix, unchanged, for a player who never opens Settings.</summary>
    public const float DefaultVolume = 1f;

    public static float LoadMasterVolume() => LoadDataOrDefault().MasterVolume;
    public static float LoadMusicVolume() => LoadDataOrDefault().MusicVolume;
    public static float LoadSfxVolume() => LoadDataOrDefault().SfxVolume;
    public static float LoadNarratorVolume() => LoadDataOrDefault().NarratorVolume;
    public static bool LoadMuted() => LoadDataOrDefault().Muted;

    public static void SaveMasterVolume(float volume)
    {
        var data = LoadDataOrDefault();
        data.MasterVolume = volume;
        SaveData(data);
    }

    public static void SaveMusicVolume(float volume)
    {
        var data = LoadDataOrDefault();
        data.MusicVolume = volume;
        SaveData(data);
    }

    public static void SaveSfxVolume(float volume)
    {
        var data = LoadDataOrDefault();
        data.SfxVolume = volume;
        SaveData(data);
    }

    public static void SaveNarratorVolume(float volume)
    {
        var data = LoadDataOrDefault();
        data.NarratorVolume = volume;
        SaveData(data);
    }

    public static void SaveMuted(bool muted)
    {
        var data = LoadDataOrDefault();
        data.Muted = muted;
        SaveData(data);
    }

    // ---- UI scale (C4, 2026-08-09 shell-and-audio-menu plan) -----------------------------------
    //
    // One root knob (Window.ContentScaleFactor), because the theme is code-built with fixed sizes
    // (GameTheme.Build) — a single multiplier over the whole window is cheap; anything finer-grained
    // (per-panel, per-font-size) is not, and nothing here asks for that.

    public const float DefaultUiScale = 1f;
    public const float MinUiScale = 1f;
    public const float MaxUiScale = 1.5f;

    public static float LoadUiScale() => Math.Clamp(LoadDataOrDefault().UiScale, MinUiScale, MaxUiScale);

    public static void SaveUiScale(float scale)
    {
        var data = LoadDataOrDefault();
        data.UiScale = Math.Clamp(scale, MinUiScale, MaxUiScale);
        SaveData(data);
    }

    /// <summary>Apply an explicit UI scale to the real window AND persist it — the Settings
    /// slider's own verb, mirroring <see cref="SetFullscreen"/>'s "apply + persist together" shape
    /// (there is no separate mixer-style director to hand this to; the window itself is the only
    /// thing that owns <c>ContentScaleFactor</c>).</summary>
    public static void SetUiScale(float scale)
    {
        var clamped = Math.Clamp(scale, MinUiScale, MaxUiScale);
        ApplyUiScaleToWindow(clamped);
        SaveUiScale(clamped);
    }

    /// <summary>Unlike <see cref="DisplayServer.WindowSetMode"/>, <see
    /// cref="Window.ContentScaleFactor"/> is a plain property on the root <see cref="Window"/> node
    /// itself (not a deferred OS call), so it reads back exactly what was set even under
    /// <c>--headless</c> — no <see cref="TestWindowMode"/>-style fake seam needed for a test to
    /// prove this actually took effect. <see cref="Engine.GetMainLoop"/> is the static seam to the
    /// live <see cref="SceneTree"/>'s root Window from a non-Node static class.</summary>
    private static void ApplyUiScaleToWindow(float scale)
    {
        if (Engine.GetMainLoop() is SceneTree tree && tree.Root is { } root)
        {
            root.ContentScaleFactor = scale;
        }
    }

    // ---- key bindings (C3, 2026-08-09 shell-and-audio-menu plan) -------------------------------
    //
    // The rebind screen (SettingsPanel) writes here by ACTION NAME -> PhysicalKeycode (int, boxed
    // Key). Never Keycode (KTD — C2's own rule): a rebind stored as a layout-dependent keycode would
    // be a bug on any non-QWERTY layout, the exact defect C2 landed to prevent.

    public static Key? LoadKeyBinding(string action)
    {
        var data = LoadDataOrDefault();
        return data.KeyBindings.TryGetValue(action, out var raw) ? (Key)raw : null;
    }

    public static void SaveKeyBinding(string action, Key physicalKeycode)
    {
        var data = LoadDataOrDefault();
        data.KeyBindings[action] = (int)physicalKeycode;
        SaveData(data);
    }

    /// <summary>Reset-to-defaults' own counterpart to <see cref="SaveKeyBinding"/> — removes the
    /// override so a later <see cref="ApplyPersistedBindingIfAny"/> (a fresh boot, or a scene that
    /// registers this action for the first time this session) never re-applies it.</summary>
    public static void ClearKeyBinding(string action)
    {
        var data = LoadDataOrDefault();
        if (data.KeyBindings.Remove(action))
        {
            SaveData(data);
        }
    }

    /// <summary>
    /// Called from <c>TownInput</c>/<c>MinigameInput</c>'s own <c>AddActionIfMissing</c>, right
    /// after an action's hardcoded defaults are registered — the one choke point every rebindable
    /// action passes through on first use, whichever scene (Town2D or a minigame) happens to create
    /// it first in a given session. Without this, a rebind saved in a PRIOR session would silently
    /// revert to the hardcoded default the moment the game restarted, because <see
    /// cref="InputMap"/> itself is never persisted by the engine — only the CHOICE is, here.
    /// No-op if nothing was ever rebound for <paramref name="action"/>, or if the action does not
    /// (yet) exist — the caller only reaches this after <c>InputMap.AddAction</c>, so the latter
    /// should not happen in practice.
    /// </summary>
    public static void ApplyPersistedBindingIfAny(string action)
    {
        if (LoadKeyBinding(action) is not { } key || !InputMap.HasAction(action))
        {
            return;
        }

        InputMap.ActionEraseEvents(action);
        InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
    }

    /// <summary>
    /// C5's tripwire (2026-08-09 shell-and-audio-menu plan) — the exact set of keys this store ever
    /// writes to <c>user://ui_settings.json</c>, name-only so a pinning test can assert the set
    /// without touching Godot or parsing JSON. Styled after <c>ConstitutionTests</c>' pinned-set
    /// pattern (<c>sim/GameSim.Tests/ConstitutionTests.cs</c>).
    ///
    /// <para><b>The rule this pin enforces:</b> a setting may change how the world SOUNDS, LOOKS,
    /// and READS. It may never change what the world DECIDES. The concrete risk is an "auto-advance
    /// the vigil" or "default vigil choice" preference — it would delete the one reach-into-the-dark
    /// decision the whole day stages, and it would arrive here looking exactly like a courtesy: one
    /// more legal-looking line in this same JSON blob. <c>ClientAuthorityCensusTests</c> (the sim's
    /// own "no timers on decisions" law) has no way to see it coming, because nothing about a
    /// preference looks like a <c>Stopwatch</c>.</para>
    ///
    /// <para>So the key set itself is pinned, in a compiled file, the same way the seven laws pin
    /// their exception counts (CLAUDE.md rule 12). Adding a persisted key is therefore a
    /// red-then-reviewed diff HERE — never a quiet new line on disk that nobody had to look at
    /// twice. Three more traps, named so the next addition is judged against them explicitly: no
    /// key may suppress sim-produced output (a pacing bug is fixed in content, never in a mute); no
    /// key may feed the kernel or the scorer; and every default here must reproduce the FULL game,
    /// never a pre-skipped one.</para>
    ///
    /// <para><b>C3/C4 additions, judged against the same rule:</b> <c>UiScale</c> changes how the
    /// world LOOKS (a render-time multiplier), nothing more. <c>KeyBindings</c> changes how a
    /// player's press REACHES a verb the game already offers — it can move which key fires
    /// <c>forge_strike</c>, never add a new outcome, remove a decision, or touch the vigil's own
    /// hold-open (that hold is enforced sim-side and has no key at all). Both pass.</para>
    /// </summary>
    public static readonly IReadOnlyList<string> PersistedKeys = new[]
    {
        nameof(Data.Fullscreen),
        nameof(Data.MasterVolume),
        nameof(Data.MusicVolume),
        nameof(Data.SfxVolume),
        nameof(Data.NarratorVolume),
        nameof(Data.Muted),
        nameof(Data.UiScale),
        nameof(Data.KeyBindings),
    };

    /// <summary>Best-effort current settings, or a fresh default set — missing/unreadable/corrupt
    /// file all collapse to the same "nothing persisted yet" answer <see cref="LoadFullscreen"/>
    /// already established, except this helper is used for READ-MODIFY-WRITE (see every Save* method
    /// above) where "nothing yet" and "explicitly default" are the same thing, unlike Fullscreen's
    /// own null-means-never-saved contract, which stays on its own read path for exactly that
    /// reason.</summary>
    private static Data LoadDataOrDefault()
    {
        if (!Godot.FileAccess.FileExists(Path))
        {
            return new Data();
        }

        using var file = Godot.FileAccess.Open(Path, Godot.FileAccess.ModeFlags.Read);
        if (file is null)
        {
            return new Data(); // unreadable — fail soft, never block boot
        }

        try
        {
            return JsonSerializer.Deserialize<Data>(file.GetAsText()) ?? new Data();
        }
        catch (JsonException)
        {
            return new Data(); // corrupt file — fail soft
        }
    }

    private static void SaveData(Data data)
    {
        using var file = Godot.FileAccess.Open(Path, Godot.FileAccess.ModeFlags.Write);
        file?.StoreString(JsonSerializer.Serialize(data));
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
        public float MasterVolume { get; set; } = DefaultVolume;
        public float MusicVolume { get; set; } = DefaultVolume;
        public float SfxVolume { get; set; } = DefaultVolume;
        public float NarratorVolume { get; set; } = DefaultVolume;
        public bool Muted { get; set; }
        public float UiScale { get; set; } = DefaultUiScale;
        public Dictionary<string, int> KeyBindings { get; set; } = new();
    }
}
