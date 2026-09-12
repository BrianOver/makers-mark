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
///
/// <para><b>Gated (P2-SCREEN-13): visible for capture/dev, invisible for a real play session.</b>
/// <c>SHOT_OUT</c> is the one env var every capture already sets unconditionally —
/// <c>godot/tools/shot_harness.gd</c> refuses to run at all without it (see its own
/// <c>_initialize</c>), and it is what <c>tools/shoot.ps1</c> (standalone) and <c>tools/
/// receipt.ps1</c> (via <c>shoot.ps1</c>) both set before ever rendering a frame. Reusing it
/// here — rather than inventing a second flag — means the exact same signal that proves "a
/// capture is driving this process" also covers the manual case: a developer who sets
/// <c>SHOT_OUT</c> by hand to poke at a build directly gets the same on-screen readout the
/// harness does. <c>play.bat</c> — the one real-play launcher (see its own header) — never sets
/// it, so a real play session never renders this, even though it (like every launcher) still
/// stamps <c>build_info.txt</c> and still needs <see cref="BuildLabel"/>'s real text for
/// <c>PlaytestLog</c>'s provenance header. That is why the gate only toggles
/// <see cref="CanvasLayer.Visible"/> on this layer rather than skipping the read/build of the
/// label itself — the text is always computed and always correct; only the on-screen draw is
/// conditional.</para>
/// </summary>
public partial class BuildStamp : CanvasLayer
{
    private const string BuildInfoPath = "res://assets/build_info.txt";
    private const string FallbackText = "dev (unstamped)";
    private const int OverlayLayer = 5;

    /// <summary>The one env var that gates on-screen visibility — see class doc.</summary>
    private const string CaptureEnvVar = "SHOT_OUT";

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

        // Text/BuildLabel above are always real (PlaytestLog needs true provenance on every
        // launch, play.bat included) — only the on-screen draw is conditional. See class doc.
        Visible = IsCaptureOrDevRun();
    }

    /// <summary>True when <see cref="CaptureEnvVar"/> is set — see class doc for why that one
    /// var covers both the automated capture harness and a manual dev run.</summary>
    private static bool IsCaptureOrDevRun() =>
        !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable(CaptureEnvVar));

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
