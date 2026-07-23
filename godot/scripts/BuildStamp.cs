using Godot;

namespace GodotClient;

/// <summary>
/// Build-provenance stamp (deploy hygiene): a dim, top-left corner label naming which build is
/// running — reads <c>res://assets/build_info.txt</c> once at <see cref="Build"/> time (never
/// re-read per frame — this never changes while the game runs) and falls back to
/// "dev (unstamped)" when the file is missing or empty, e.g. a fresh checkout a release/CI
/// stamping step hasn't touched yet. A <see cref="CanvasLayer"/>, code-built with no scene — the
/// same idiom <see cref="Ui.TabFade"/> already uses — so it always draws above the 3D world and
/// every panel; <c>MouseFilter.Ignore</c> on the label so it never eats a click, and it degrades
/// silently on any read failure (missing file, unreadable handle, empty text) so a stamping
/// hiccup can never block boot.
/// </summary>
public partial class BuildStamp : CanvasLayer
{
    private const string BuildInfoPath = "res://assets/build_info.txt";
    private const string FallbackText = "dev (unstamped)";
    private const int OverlayLayer = 5;

    /// <summary>Dim readout — a translucent tint of the shared body-text color (R11/KTD1: never a
    /// raw local literal) so a debug/provenance corner label never competes with real UI.</summary>
    private const float DimAlpha = 0.5f;

    private const float Margin = 6f;
    private const int FontSize = 12;

    private Label? _label;

    /// <summary>The rendered stamp text (test/inspection surface).</summary>
    public string BuildLabel => _label?.Text ?? string.Empty;

    /// <summary>Build the stamp. Idempotent-guarded like every other code-built node here.</summary>
    public void Build()
    {
        if (_label is not null)
        {
            return;
        }

        Name = "BuildStamp";
        Layer = OverlayLayer;

        _label = new Label
        {
            Name = "BuildStampLabel",
            Text = ReadBuildInfo(),
            Position = new Vector2(Margin, Margin),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _label.AddThemeColorOverride("font_color", new Color(Ui.GameTheme.BodyTextColor, DimAlpha));
        _label.AddThemeFontSizeOverride("font_size", FontSize);
        AddChild(_label);
    }

    /// <summary>Read once, fail soft everywhere: no file, an unreadable handle, or blank content
    /// all degrade to <see cref="FallbackText"/> rather than a startup failure.</summary>
    private static string ReadBuildInfo()
    {
        if (!Godot.FileAccess.FileExists(BuildInfoPath))
        {
            return FallbackText;
        }

        using var file = Godot.FileAccess.Open(BuildInfoPath, Godot.FileAccess.ModeFlags.Read);
        if (file is null)
        {
            return FallbackText;
        }

        var text = file.GetAsText().Trim();
        return string.IsNullOrEmpty(text) ? FallbackText : text;
    }
}
