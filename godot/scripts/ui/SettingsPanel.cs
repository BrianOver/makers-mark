using System;
using Godot;
using GodotClient.Audio;

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

    /// <summary>Idempotent-guarded like every other code-built node on this project (<see
    /// cref="DrawerHost.Build"/> precedent) — safe to call once per instance.</summary>
    public void Build()
    {
        if (_fullscreenToggle is not null)
        {
            return;
        }

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

        var back = new Button { Name = "SettingsBack", Text = "Back", CustomMinimumSize = new Vector2(0, 44) };
        back.Pressed += () => Closed?.Invoke();
        AddChild(back);
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
}
