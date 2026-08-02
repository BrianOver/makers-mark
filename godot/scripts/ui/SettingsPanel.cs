using System;
using Godot;

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

        var back = new Button { Name = "SettingsBack", Text = "Back", CustomMinimumSize = new Vector2(0, 44) };
        back.Pressed += () => Closed?.Invoke();
        AddChild(back);
    }

    /// <summary>Re-read the live window mode into the checkbox. Call before revealing this panel —
    /// the OTHER host's copy, or a bare F11 press, may have changed it since this instance last
    /// synced, and a stale checkbox reads as a preference that silently reverted.</summary>
    public void Refresh() => _fullscreenToggle.SetPressedNoSignal(UiSettings.IsFullscreen());

    private void OnFullscreenToggled(bool pressed)
    {
        UiSettings.SetFullscreen(pressed);
        // Re-read rather than trust `pressed` verbatim: harmless under the TestWindowMode seam
        // (Apply always "succeeds" there), but a real OS window mode change is not guaranteed —
        // showing what the window ACTUALLY did beats showing what we asked it to do.
        _fullscreenToggle.SetPressedNoSignal(UiSettings.IsFullscreen());
    }
}
