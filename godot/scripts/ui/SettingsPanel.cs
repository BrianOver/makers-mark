using System;
using System.Collections.Generic;
using Godot;
using GodotClient.Audio;
using GodotClient.Minigames;
using GodotClient.Town2d;

namespace GodotClient.Ui;

/// <summary>
/// The one settings surface (2026-08-02 shell-and-audio plan, U3/U4): built once as a class,
/// mounted from two hosts — the title screen's "Settings" button (<c>NewGameSelect</c>) and the
/// in-game system menu's "Settings" button (<c>MainUi</c>) — so the two can never drift into
/// showing different controls or different values for the same preference. Today that is one row
/// (fullscreen, via <see cref="UiSettings"/>); the mixer/volume UI the plan explicitly defers to a
/// later unit grows here, in the one place, not in a second copy.
///
/// <para>Each host constructs its own instance (never a single Control shared live across the
/// title screen and <c>MainUi</c> — those two scenes are never mounted at once in real play, and a
/// Control cannot be usefully shared across a <c>ChangeSceneToFile</c> boundary anyway); "built
/// once" means the CLASS, so both instances always render and behave identically.</para>
///
/// <para>Reads/writes <see cref="UiSettings"/> directly rather than snapshotting a value at
/// construction, because the same preference can change out from under an already-built instance
/// — a bare F11 press while this panel happens to be showing. <see cref="Refresh"/> is the host's
/// job to call right before revealing this panel (mirrors <c>DrawerHost</c>'s "rebuild fresh on
/// every open" convention) rather than something this class times itself, since the host already
/// knows exactly when that reveal happens.</para>
/// </summary>
public partial class SettingsPanel : VBoxContainer
{
    /// <summary>Raised when "Back" is pressed. The host decides what "back" means (the title
    /// screen returns to its title menu; the system menu returns to its own button list) — this
    /// panel never assumes it owns navigation.</summary>
    public event Action? Closed;

    private CheckButton _fullscreenToggle = null!;

    /// <summary>
    /// C1's own label for the narrator fader (2026-08-09 shell-and-audio-menu plan) — the EXACT
    /// copy the plan pins, because this is the one slider on this panel that obeys the skipping law
    /// by naming what a player gives up alongside what they keep. Zero is legal here: the narrator
    /// carries no information the screen does not already carry (<c>AudioDirector.SpeakNarrator</c>'s
    /// own doc), so a silenced narrator reads exactly like a voice library that was never recorded.
    /// No setting anywhere may take the second half of this sentence away — the narrator's TEXT
    /// keeps appearing at every volume, including zero.
    /// </summary>
    private const string NarratorSliderLabel =
        "Narrator voice — the spoken lines fall silent; every word still appears on screen.";

    private VolumeRow _masterRow;
    private VolumeRow _musicRow;
    private VolumeRow _sfxRow;
    private VolumeRow _narratorRow;
    private CheckButton _muteToggle = null!;

    /// <summary>A slider paired with the label that shows its live percentage — kept together so
    /// <see cref="Refresh"/> can resync both without hunting through children by name.</summary>
    private readonly record struct VolumeRow(HSlider Slider, Label ValueLabel);

    // ---- UI scale (C4) --------------------------------------------------------------------------

    private HSlider _uiScaleSlider = null!;
    private Label _uiScaleValueLabel = null!;

    // ---- rebinding (C3) -------------------------------------------------------------------------
    //
    // The EXACT ~8 actions the plan calls for (move x4, interact, strike, bellows, confirm).
    // PhysicalKeycode throughout (C2's own rule — a rebind stored as a layout-dependent Keycode is a
    // bug on any non-QWERTY layout). Defaults here mirror TownInput/MinigameInput's own registration
    // verbatim; RegisterActions() is called at the top of Build() precisely so this list is never
    // read before those defaults exist, regardless of whether the player reaches Settings from the
    // title screen (before Town2D/any minigame has ever built) or the in-game system menu.
    private static readonly string[] RebindableActions =
    {
        "move_up", "move_down", "move_left", "move_right", "interact", "forge_strike", "bellows", "confirm",
    };

    private static readonly Dictionary<string, Key[]> DefaultKeysByAction = new()
    {
        ["move_up"] = new[] { Key.W, Key.Up },
        ["move_down"] = new[] { Key.S, Key.Down },
        ["move_left"] = new[] { Key.A, Key.Left },
        ["move_right"] = new[] { Key.D, Key.Right },
        ["interact"] = new[] { Key.E },
        ["forge_strike"] = new[] { Key.Space },
        ["bellows"] = new[] { Key.Shift },
        ["confirm"] = new[] { Key.Enter, Key.KpEnter },
    };

    /// <summary>Human labels for every action this panel either exposes as rebindable OR checks for
    /// CONFLICTS against (the latter set is wider than <see cref="RebindableActions"/> — see <see
    /// cref="FindConflictingAction"/>'s own doc for why a non-editable action still has to be
    /// checked).</summary>
    private static readonly Dictionary<string, string> ActionLabels = new()
    {
        ["move_up"] = "Move up",
        ["move_down"] = "Move down",
        ["move_left"] = "Move left",
        ["move_right"] = "Move right",
        ["interact"] = "Interact",
        ["forge_strike"] = "Forge strike",
        ["bellows"] = "Bellows",
        ["confirm"] = "Confirm",
        ["cancel"] = "Cancel / back",
        ["plunge"] = "Plunge",
        ["scrape"] = "Scrape",
        ["crank_stroke"] = "Crank",
        ["pull_part"] = "Pull part",
    };

    /// <summary>Every action a rebind could collide with — wider than <see
    /// cref="RebindableActions"/> because a rebind that silently steals <c>plunge</c>'s or
    /// <c>confirm</c>'s key is a real footgun even though this panel never lets a player edit those
    /// two directly. Engine-internal <c>ui_*</c> actions are deliberately excluded — those are not
    /// game verbs a player would recognise as "already taken."</summary>
    private static readonly string[] ConflictCheckActions =
    {
        "move_up", "move_down", "move_left", "move_right", "interact", "cancel",
        "forge_strike", "bellows", "confirm", "plunge", "scrape", "crank_stroke", "pull_part",
    };

    private readonly record struct RebindRow(string Action, Button KeyButton);

    private readonly List<RebindRow> _rebindRows = new();
    private Label _rebindStatusLabel = null!;

    /// <summary>Non-null while a rebind row is waiting for the next keypress — see <see
    /// cref="_Input"/>.</summary>
    private string? _armedAction;
    private Button? _armedButton;

    private static string DisplayName(string action) => ActionLabels.TryGetValue(action, out var label) ? label : action;

    /// <summary>Idempotent-guarded like every other code-built node on this project (<see
    /// cref="DrawerHost.Build"/> precedent) — safe to call once per instance.</summary>
    public void Build()
    {
        if (_fullscreenToggle is not null)
        {
            return;
        }

        // C3: every rebindable (and conflict-checkable) action must exist in the InputMap before
        // this panel reads a single KeyLabelFor call below — guarded by InputMap.HasAction, so this
        // is a safe no-op on every call after the first. Without this, opening Settings from the
        // TITLE screen (before Town2D or any minigame has ever built) would read every row as "?".
        TownInput.RegisterActions();
        MinigameInput.RegisterActions();

        Name = "SettingsPanel";
        AddThemeConstantOverride("separation", GameTheme.Space16);

        var title = new Label
        {
            Name = "SettingsTitle",
            Text = "Settings",
            ThemeTypeVariation = GameTheme.HeaderThemeType,
        };
        title.AddThemeColorOverride("font_color", GameTheme.HeaderColor);
        title.AddThemeFontSizeOverride("font_size", GameTheme.HeaderFontSize);
        AddChild(title);

        _fullscreenToggle = new CheckButton { Name = "FullscreenToggle", Text = "Fullscreen (F11)" };
        _fullscreenToggle.SetPressedNoSignal(UiSettings.IsFullscreen());
        _fullscreenToggle.Toggled += OnFullscreenToggled;
        AddChild(_fullscreenToggle);

        // ---- the mixer (C1) --------------------------------------------------------------------
        // Four faders onto the players AudioDirector already owns — no Godot audio bus layout,
        // because project.godot is deny-listed and per-player dB is this repo's established
        // pattern (see AudioDirector's own class doc). Today's constants are the factory default
        // (every slider starts at 100%), so a player who never touches this panel hears nothing
        // different.
        _masterRow = BuildVolumeRow("Master", "Master", UiSettings.LoadMasterVolume(), OnMasterVolumeChanged);
        _musicRow = BuildVolumeRow("Music", "Music", UiSettings.LoadMusicVolume(), OnMusicVolumeChanged);
        _sfxRow = BuildVolumeRow("Sfx", "SFX", UiSettings.LoadSfxVolume(), OnSfxVolumeChanged);
        _narratorRow = BuildVolumeRow("Narrator", NarratorSliderLabel, UiSettings.LoadNarratorVolume(), OnNarratorVolumeChanged);

        _muteToggle = new CheckButton { Name = "MuteToggle", Text = "Mute" };
        _muteToggle.SetPressedNoSignal(UiSettings.LoadMuted());
        _muteToggle.Toggled += OnMuteToggled;
        AddChild(_muteToggle);

        // ---- UI scale (C4) ----------------------------------------------------------------------
        // One root knob (Window.ContentScaleFactor) — the theme is code-built with fixed sizes
        // (GameTheme.Build), which is exactly why one multiplier over the whole window is cheap and
        // anything finer-grained (per-panel, per-font-size) is not.
        var scaleLabel = new Label { Name = "UiScaleLabel", Text = "UI scale" };
        AddChild(scaleLabel);

        var scaleRow = new HBoxContainer { Name = "UiScaleRow" };
        scaleRow.AddThemeConstantOverride("separation", GameTheme.Space12);
        AddChild(scaleRow);

        var initialScalePercent = Math.Clamp(UiSettings.LoadUiScale(), UiSettings.MinUiScale, UiSettings.MaxUiScale) * 100.0;
        _uiScaleSlider = new HSlider
        {
            Name = "UiScaleSlider",
            MinValue = UiSettings.MinUiScale * 100.0,
            MaxValue = UiSettings.MaxUiScale * 100.0,
            Step = 5,
            Value = initialScalePercent,
            CustomMinimumSize = new Vector2(220, 0),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        scaleRow.AddChild(_uiScaleSlider);

        _uiScaleValueLabel = new Label { Name = "UiScaleValue", CustomMinimumSize = new Vector2(44, 0), Text = FormatPercent(initialScalePercent) };
        scaleRow.AddChild(_uiScaleValueLabel);

        _uiScaleSlider.ValueChanged += value =>
        {
            _uiScaleValueLabel.Text = FormatPercent(value);
            UiSettings.SetUiScale((float)(value / 100.0));
        };

        // ---- rebinding (C3) ----------------------------------------------------------------------
        var controlsTitle = new Label
        {
            Name = "ControlsTitle",
            Text = "Controls",
            ThemeTypeVariation = GameTheme.HeaderThemeType,
        };
        controlsTitle.AddThemeColorOverride("font_color", GameTheme.HeaderColor);
        AddChild(controlsTitle);

        _rebindStatusLabel = new Label { Name = "RebindStatus", Text = string.Empty };
        AddChild(_rebindStatusLabel);

        foreach (var action in RebindableActions)
        {
            var row = new HBoxContainer { Name = $"Rebind_{action}_Row" };
            row.AddThemeConstantOverride("separation", GameTheme.Space12);
            AddChild(row);

            var label = new Label
            {
                Name = $"Rebind_{action}_Label",
                Text = DisplayName(action),
                CustomMinimumSize = new Vector2(140, 0),
            };
            row.AddChild(label);

            var keyButton = new Button { Name = $"Rebind_{action}_Key", Text = MinigameInput.KeyLabelFor(action) };
            var capturedAction = action; // loop-variable capture — each row's closure must bind ITS OWN action
            var capturedButton = keyButton;
            keyButton.Pressed += () => BeginRebind(capturedAction, capturedButton);
            row.AddChild(keyButton);

            _rebindRows.Add(new RebindRow(action, keyButton));
        }

        var resetBindings = new Button { Name = "ResetBindingsToDefaults", Text = "Reset controls to defaults" };
        resetBindings.Pressed += ResetBindingsToDefaults;
        AddChild(resetBindings);

        var back = new Button { Name = "SettingsBack", Text = "Back", CustomMinimumSize = new Vector2(0, 44) };
        back.Pressed += () =>
        {
            // Leaving mid-capture must not leave a live trap: without this, pressing Back while a
            // row is armed would leave _armedAction set on a now-HIDDEN panel, and the player's very
            // next keypress ANYWHERE in the game (with no status label visible to explain why) would
            // silently complete the stale rebind. _Input's own Visible guard below is defense in
            // depth for the same failure; this is the honest fix at the source.
            CancelArmedRebind();
            Closed?.Invoke();
        };
        AddChild(back);

        // Every Button on this panel must be mouse-only, same idiom as ForgeMinigame's own buttons
        // (UiKit.MakeButtonsMouseOnly's own doc): a focused Button consumes the very next Space/Enter
        // to press ITSELF, which would silently eat the keypress a "press a key to rebind" row exists
        // to capture (see _Input below).
        UiKit.MakeButtonsMouseOnly(this);
    }

    /// <summary>
    /// Builds one labeled fader row: a caption (<paramref name="labelText"/> — the narrator row's
    /// is the full skipping-law sentence, every other row's is just the category name), a 0-100%
    /// <see cref="HSlider"/>, and a live percentage readout. <paramref name="name"/> feeds every
    /// child Control's own <c>Name</c> so the row is inspectable by name in a test without leaning
    /// on the display text (which, for the narrator row, is a full sentence).
    /// </summary>
    private VolumeRow BuildVolumeRow(string name, string labelText, float initialLinear01, Action<float> onChanged)
    {
        var label = new Label { Name = $"{name}Label", Text = labelText };
        AddChild(label);

        var row = new HBoxContainer { Name = $"{name}Row" };
        row.AddThemeConstantOverride("separation", GameTheme.Space12);
        AddChild(row);

        var initialPercent = Math.Clamp(initialLinear01, 0f, 1f) * 100.0;
        var slider = new HSlider
        {
            Name = $"{name}Slider",
            MinValue = 0,
            MaxValue = 100,
            Step = 1,
            Value = initialPercent,
            CustomMinimumSize = new Vector2(220, 0),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        row.AddChild(slider);

        var valueLabel = new Label { Name = $"{name}Value", CustomMinimumSize = new Vector2(44, 0), Text = FormatPercent(initialPercent) };
        row.AddChild(valueLabel);

        slider.ValueChanged += value =>
        {
            valueLabel.Text = FormatPercent(value);
            onChanged((float)(value / 100.0));
        };

        return new VolumeRow(slider, valueLabel);
    }

    private static string FormatPercent(double percent) => $"{(int)Math.Round(percent)}%";

    /// <summary>Re-read every live preference into this instance's controls. Call before revealing
    /// this panel — the OTHER host's copy may have changed any of these since this instance last
    /// synced, and a stale control reads as a preference that silently reverted.</summary>
    public void Refresh()
    {
        _fullscreenToggle.SetPressedNoSignal(UiSettings.IsFullscreen());
        RefreshRow(_masterRow, UiSettings.LoadMasterVolume());
        RefreshRow(_musicRow, UiSettings.LoadMusicVolume());
        RefreshRow(_sfxRow, UiSettings.LoadSfxVolume());
        RefreshRow(_narratorRow, UiSettings.LoadNarratorVolume());
        _muteToggle.SetPressedNoSignal(UiSettings.LoadMuted());

        var scalePercent = Math.Clamp(UiSettings.LoadUiScale(), UiSettings.MinUiScale, UiSettings.MaxUiScale) * 100.0;
        _uiScaleSlider.SetValueNoSignal(scalePercent);
        _uiScaleValueLabel.Text = FormatPercent(scalePercent);

        // A rebind made through the OTHER instance of this panel (title screen vs system menu) must
        // show up here too — same "read the live InputMap, never a stale snapshot" rule C2 already
        // established for every minigame prompt.
        CancelArmedRebind();
        _rebindStatusLabel.Text = string.Empty;
        foreach (var row in _rebindRows)
        {
            row.KeyButton.Text = MinigameInput.KeyLabelFor(row.Action);
        }
    }

    private static void RefreshRow(VolumeRow row, float linear01)
    {
        var percent = Math.Clamp(linear01, 0f, 1f) * 100.0;
        row.Slider.SetValueNoSignal(percent);
        row.ValueLabel.Text = FormatPercent(percent);
    }

    private void OnFullscreenToggled(bool pressed)
    {
        UiSettings.SetFullscreen(pressed);
        // Re-read rather than trust `pressed` verbatim: harmless under the TestWindowMode seam
        // (Apply always "succeeds" there), but a real OS window mode change is not guaranteed —
        // showing what the window ACTUALLY did beats showing what we asked it to do.
        _fullscreenToggle.SetPressedNoSignal(UiSettings.IsFullscreen());
    }

    // ---- the mixer's handlers (C1) --------------------------------------------------------------
    //
    // Every handler persists through UiSettings FIRST, unconditionally, then pushes the live value
    // to AudioDirector.For(this) if one exists — null-tolerant on purpose (AudioDirector.For's own
    // doc): the title screen mounts this same panel with no AudioDirector anywhere in its tree, so a
    // slider dragged there must still persist and take effect the next time MainUi constructs one.

    private void OnMasterVolumeChanged(float volume)
    {
        UiSettings.SaveMasterVolume(volume);
        AudioDirector.For(this)?.SetMasterVolume(volume);
    }

    private void OnMusicVolumeChanged(float volume)
    {
        UiSettings.SaveMusicVolume(volume);
        AudioDirector.For(this)?.SetMusicVolume(volume);
    }

    private void OnSfxVolumeChanged(float volume)
    {
        UiSettings.SaveSfxVolume(volume);
        AudioDirector.For(this)?.SetSfxVolume(volume);
    }

    private void OnNarratorVolumeChanged(float volume)
    {
        UiSettings.SaveNarratorVolume(volume);
        AudioDirector.For(this)?.SetNarratorVolume(volume);
    }

    private void OnMuteToggled(bool muted)
    {
        UiSettings.SaveMuted(muted);
        AudioDirector.For(this)?.SetMuted(muted);
    }

    // ---- rebinding (C3) ---------------------------------------------------------------------------
    //
    // Press-to-rebind: clicking a row's key Button arms it, then the NEXT physical keypress anywhere
    // is captured by _Input (below) and either applied or rejected as a conflict. Escape cancels the
    // capture without rebinding Escape itself.

    private void BeginRebind(string action, Button button)
    {
        if (_armedAction == action)
        {
            return; // a second click on the SAME row while already armed is a no-op, not a re-arm
        }

        CancelArmedRebind(); // a different row was mid-capture — restore its label before arming this one

        _armedAction = action;
        _armedButton = button;
        button.Text = "Press a key…";
        _rebindStatusLabel.Text = $"{DisplayName(action)}: press any key (Esc cancels)";
    }

    /// <summary>Restores whichever row is currently armed to its live InputMap label, with no
    /// rebind applied — called by a fresh <see cref="BeginRebind"/> on another row, by <see
    /// cref="Refresh"/> (the panel is about to be hidden or re-shown), and by an Escape capture.
    /// Safe to call when nothing is armed.</summary>
    private void CancelArmedRebind()
    {
        if (_armedAction is not null && _armedButton is not null)
        {
            _armedButton.Text = MinigameInput.KeyLabelFor(_armedAction);
        }

        _armedAction = null;
        _armedButton = null;
    }

    /// <summary>
    /// Captures the rebind keypress. Overrides <c>_Input</c> (not <c>_UnhandledKeyInput</c>)
    /// deliberately, mirroring <c>ForgeMinigame.Cancel</c>'s own reasoning: this panel is nested
    /// content inside a host that already owns Escape for its own close/back behaviour (the title
    /// screen's own menu, or <c>MainUi</c>'s system menu via <c>ModalEscape</c>). Godot calls
    /// <c>_Input</c> in reverse tree order — children before parents — so while a row is armed, THIS
    /// method sees Escape and marks it handled before the host's own close-on-Escape ever does;
    /// pressing Escape here must cancel the CAPTURE, not close the whole menu underneath it. When no
    /// row is armed this method does nothing and marks nothing handled, so every other key (F11,
    /// Escape-to-close, …) behaves exactly as it did before this unit. Also gated on <see
    /// cref="CanvasItem.IsVisibleInTree"/> (not just this node's own <see cref="Control.Visible"/>
    /// — a hidden ANCESTOR, e.g. the system menu itself closing via "Resume" rather than this
    /// panel's own Back button, must count too): the Back button already cancels an in-flight
    /// capture before hiding (see its handler in <see cref="Build"/>), but this guard is defense in
    /// depth against every OTHER way the panel can stop being shown — a hidden, still-armed panel
    /// must never steal the player's next keypress anywhere else in the game.
    /// </summary>
    public override void _Input(InputEvent @event)
    {
        if (!IsVisibleInTree() || _armedAction is null || @event is not InputEventKey { Pressed: true, Echo: false } keyEvent)
        {
            return;
        }

        GetViewport().SetInputAsHandled();

        var action = _armedAction;
        var button = _armedButton!;
        _armedAction = null;
        _armedButton = null;

        if (keyEvent.PhysicalKeycode == Key.Escape)
        {
            button.Text = MinigameInput.KeyLabelFor(action);
            _rebindStatusLabel.Text = "Rebind cancelled.";
            return;
        }

        ApplyOrRejectRebind(action, button, keyEvent.PhysicalKeycode);
    }

    /// <summary>Applies the captured key if nothing else already holds it, or rejects it and names
    /// the conflicting action — CLAUDE.md's rule for this unit is explicit: "conflict detection must
    /// be real: binding a key already taken tells the player which action holds it." A rejected
    /// capture leaves BOTH actions' bindings untouched — this panel never auto-swaps two actions'
    /// keys out from under the player.</summary>
    private void ApplyOrRejectRebind(string action, Button button, Key physicalKey)
    {
        var conflict = FindConflictingAction(physicalKey, excluding: action);
        if (conflict is not null)
        {
            button.Text = MinigameInput.KeyLabelFor(action); // restore — the rebind is REJECTED
            var keyLabel = new InputEventKey { PhysicalKeycode = physicalKey }.AsTextPhysicalKeycode();
            _rebindStatusLabel.Text = $"{keyLabel} is already bound to {DisplayName(conflict)} — pick another key.";
            return;
        }

        InputMap.ActionEraseEvents(action);
        InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = physicalKey });
        UiSettings.SaveKeyBinding(action, physicalKey);

        button.Text = MinigameInput.KeyLabelFor(action);
        _rebindStatusLabel.Text = $"{DisplayName(action)} is now {MinigameInput.KeyLabelFor(action)}.";
    }

    /// <summary>Scans <see cref="ConflictCheckActions"/> — every action a rebind can realistically
    /// collide with, not only the ~8 rows this panel lets a player edit — for an existing physical
    /// key match. Real detection: it reads the live <see cref="InputMap"/>, the same source every
    /// minigame prompt already reads (C2), never a hand-maintained "taken" list that could drift
    /// from what is actually bound.</summary>
    private static string? FindConflictingAction(Key physicalKey, string excluding)
    {
        foreach (var candidate in ConflictCheckActions)
        {
            if (candidate == excluding || !InputMap.HasAction(candidate))
            {
                continue;
            }

            foreach (var evt in InputMap.ActionGetEvents(candidate))
            {
                if (evt is InputEventKey key && key.PhysicalKeycode == physicalKey)
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    /// <summary>Restores every rebindable action to its hardcoded default AND clears the persisted
    /// override, so a fresh boot never re-applies a discarded rebind through <see
    /// cref="UiSettings.ApplyPersistedBindingIfAny"/>.</summary>
    private void ResetBindingsToDefaults()
    {
        CancelArmedRebind();

        foreach (var (action, defaultKeys) in DefaultKeysByAction)
        {
            InputMap.ActionEraseEvents(action);
            foreach (var key in defaultKeys)
            {
                InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
            }

            UiSettings.ClearKeyBinding(action);
        }

        foreach (var row in _rebindRows)
        {
            row.KeyButton.Text = MinigameInput.KeyLabelFor(row.Action);
        }

        _rebindStatusLabel.Text = "Controls reset to defaults.";
    }
}
