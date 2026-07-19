using Godot;

namespace GodotClient.Ui;

/// <summary>
/// P007 U1 (R11/KTD1): the one shared, programmatic Godot <see cref="Theme"/> — font sizes,
/// style-bible colors, spacing, and per-control-type <see cref="StyleBoxFlat"/>s — assigned once
/// at the <c>MainUi</c> root (<c>this.Theme = GameTheme.Build();</c>, set BEFORE <c>BuildUi()</c>)
/// so Godot's normal Theme cascade carries it to every descendant Control with zero
/// <c>project.godot</c> contact (deny-listed). Built entirely in code, mirroring the editor-free
/// style of <see cref="GodotClient.Town.LitTownOverlay"/> and the static-factory shape of
/// <see cref="IconRegistry"/>.
///
/// <para>Colors are the palette from <c>docs/style-bible.md</c> ("fantasy-witchy with a sci-fi
/// tinge") so the UI reads as the same world as the generated art: Void/Iron for surfaces,
/// Arcane (purple) as the primary accent, Coolant (teal) for headers, Ember for the warm
/// (non-alarming, R6) rejection tone, Bone for body text, Blood reserved for true danger/death.</para>
///
/// <para>Every public builder (<see cref="PanelStyle"/>, <see cref="ButtonStyle"/>) returns a
/// FRESH <see cref="StyleBoxFlat"/> instance on each call — a Godot StyleBox is a mutable
/// <c>Resource</c>, and sharing one instance across theme type slots (or across two
/// <see cref="Build"/> calls) would let a later caller's edit bleed into every other consumer.
/// <see cref="Build"/> itself is therefore idempotent-safe: calling it twice yields two
/// independent, equivalent themes.</para>
///
/// <para>P007 polish (display font): <see cref="HeaderFont"/> — the OFL-licensed Cinzel display
/// face (<c>godot/assets/fonts/</c>, license alongside it) — is registered ONLY on the
/// <see cref="HeaderThemeType"/> theme-type variation, never on the base "Label"/"Button"
/// types. Body text stays the engine default everywhere (legibility + layout stability, R11);
/// only a Control that opts in via <c>ThemeTypeVariation = GameTheme.HeaderThemeType</c> —
/// today, <see cref="GodotClient.Panels.SimPanel.AddHeader"/> and <see cref="UiKit.Section"/>'s
/// title — picks it up. Null-tolerant like every other art loader on this project
/// (<see cref="IconRegistry"/>): a missing font resource degrades to
/// <see cref="ThemeDB.FallbackFont"/>, never a throw.</para>
/// </summary>
public static class GameTheme
{
    /// <summary>Theme-type variation carrying <see cref="HeaderFont"/> (see type remarks) —
    /// a Control opts in by setting its own <c>ThemeTypeVariation</c> to this constant.</summary>
    public const string HeaderThemeType = "HeaderLabel";

    /// <summary>The committed OFL display font asset (Cinzel, a variable TTF covering
    /// Regular→Black) — see <c>godot/assets/fonts/OFL.txt</c> for the license.</summary>
    private const string HeaderFontPath = "res://assets/fonts/Cinzel-VariableFont_wght.ttf";

    private static Font? _headerFont;

    /// <summary>The header/title display font, loaded once and cached. Never null: degrades to
    /// <see cref="ThemeDB.FallbackFont"/> if the asset is ever missing from a build.</summary>
    public static Font HeaderFont => _headerFont ??= LoadHeaderFont();

    // ── Style-bible palette (docs/style-bible.md) ─────────────────────────────────────────────
    public static readonly Color VoidColor = new("140f1f");
    public static readonly Color IronColor = new("2a2438");
    public static readonly Color ArcaneColor = new("6b4c9a");
    public static readonly Color CoolantColor = new("3fb0ac");
    public static readonly Color EmberColor = new("e0913f");
    public static readonly Color BoneColor = new("d8cfe0");
    public static readonly Color BloodColor = new("b5462f");

    /// <summary>Primary accent — button/panel borders and focus reads from this (style-bible
    /// "Arcane", the witchy-purple signature).</summary>
    public static readonly Color AccentColor = ArcaneColor;

    /// <summary>Section/label header color — Coolant teal reads clearly against the Iron surface
    /// and replaces the old ad-hoc light-blue literal in <c>SimPanel.AddHeader</c>.</summary>
    public static readonly Color HeaderColor = CoolantColor;

    /// <summary>Default body text color (style-bible "Bone").</summary>
    public static readonly Color BodyTextColor = BoneColor;

    /// <summary>
    /// Transient rejection-toast tone (R6): warm, not alarming by design — the exact color the
    /// U6 toast already rendered (<c>MainUi</c> previously held this as a private literal), now
    /// named centrally so every rejection surface reads the same hue. Deliberately NOT
    /// <see cref="BloodColor"/> — Blood is reserved for true danger/death, and a player's
    /// declined action is friendly feedback, not a threat.
    /// </summary>
    public static readonly Color RejectionColor = new(1f, 0.75f, 0.45f);

    // ── Sizes ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>Minimum legible font size at target resolution (R11) — the floor every default
    /// and per-type font size below must meet or exceed.</summary>
    public const int LegibilityFloor = 16;

    /// <summary>Default body/control font size — the engine default (16) bumped for legibility.</summary>
    public const int BodyFontSize = 18;

    /// <summary>Section/card header font size.</summary>
    public const int HeaderFontSize = 22;

    // ── Spacing / shape ────────────────────────────────────────────────────────────────────────
    private const int PanelCornerRadius = 8;
    private const int PanelBorderWidth = 2;
    private const float PanelContentMargin = 12f;

    /// <summary>Interaction states a themed <see cref="Button"/> steps through.</summary>
    public enum ButtonVisualState
    {
        Normal,
        Hover,
        Pressed,
        Disabled,
    }

    /// <summary>The panel surface every <c>PanelContainer</c>/<c>Panel</c> renders: dark Iron
    /// fill, a faint Arcane border, rounded corners, and breathing-room content margins so a
    /// themed card never crowds its own text.</summary>
    public static StyleBoxFlat PanelStyle() => new()
    {
        BgColor = IronColor,
        BorderColor = new Color(AccentColor, 0.55f),
        BorderWidthBottom = PanelBorderWidth,
        BorderWidthLeft = PanelBorderWidth,
        BorderWidthRight = PanelBorderWidth,
        BorderWidthTop = PanelBorderWidth,
        CornerRadiusBottomLeft = PanelCornerRadius,
        CornerRadiusBottomRight = PanelCornerRadius,
        CornerRadiusTopLeft = PanelCornerRadius,
        CornerRadiusTopRight = PanelCornerRadius,
        ContentMarginLeft = PanelContentMargin,
        ContentMarginRight = PanelContentMargin,
        ContentMarginTop = PanelContentMargin,
        ContentMarginBottom = PanelContentMargin,
    };

    /// <summary>Button surface for one interaction state — Iron→Accent progression so a press
    /// reads as tactile depth; Disabled dims toward Void.</summary>
    public static StyleBoxFlat ButtonStyle(ButtonVisualState state)
    {
        var bg = state switch
        {
            ButtonVisualState.Hover => IronColor.Lightened(0.15f),
            ButtonVisualState.Pressed => AccentColor.Darkened(0.1f),
            ButtonVisualState.Disabled => IronColor.Darkened(0.35f),
            _ => IronColor.Lightened(0.05f),
        };

        return new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = new Color(AccentColor, state == ButtonVisualState.Pressed ? 0.9f : 0.4f),
            BorderWidthBottom = PanelBorderWidth,
            BorderWidthLeft = PanelBorderWidth,
            BorderWidthRight = PanelBorderWidth,
            BorderWidthTop = PanelBorderWidth,
            CornerRadiusBottomLeft = PanelCornerRadius,
            CornerRadiusBottomRight = PanelCornerRadius,
            CornerRadiusTopLeft = PanelCornerRadius,
            CornerRadiusTopRight = PanelCornerRadius,
            ContentMarginLeft = PanelContentMargin,
            ContentMarginRight = PanelContentMargin,
            ContentMarginTop = PanelContentMargin * 0.5f,
            ContentMarginBottom = PanelContentMargin * 0.5f,
        };
    }

    /// <summary>
    /// Build a fully-populated <see cref="Theme"/>: legible default font size, PanelContainer/
    /// Panel surfaces, Button normal/hover/pressed/disabled surfaces, and Label/Button text
    /// colors+sizes. Assign to a root Control's <c>Theme</c> property before building its
    /// children so the cascade reaches every descendant.
    /// </summary>
    public static Theme Build()
    {
        var theme = new Theme { DefaultFontSize = BodyFontSize };

        theme.SetStylebox("panel", "PanelContainer", PanelStyle());
        theme.SetStylebox("panel", "Panel", PanelStyle());

        theme.SetStylebox("normal", "Button", ButtonStyle(ButtonVisualState.Normal));
        theme.SetStylebox("hover", "Button", ButtonStyle(ButtonVisualState.Hover));
        theme.SetStylebox("pressed", "Button", ButtonStyle(ButtonVisualState.Pressed));
        theme.SetStylebox("disabled", "Button", ButtonStyle(ButtonVisualState.Disabled));
        theme.SetStylebox("focus", "Button", ButtonStyle(ButtonVisualState.Hover));

        theme.SetColor("font_color", "Label", BodyTextColor);
        theme.SetColor("font_color", "Button", BodyTextColor);
        theme.SetColor("font_color_hover", "Button", BoneColor);
        theme.SetColor("font_color_pressed", "Button", BoneColor);
        theme.SetColor("font_color_disabled", "Button", new Color(BodyTextColor, 0.5f));

        theme.SetFontSize("font_size", "Label", BodyFontSize);
        theme.SetFontSize("font_size", "Button", BodyFontSize);

        // Display font (P007 polish): a type VARIATION of "Label", never the base type itself —
        // font_color/font_size for a HeaderThemeType Control still resolve through the normal
        // variation fallback to the "Label" entries above (both are usually overridden locally
        // by AddHeader/Section anyway), but the FONT only ever changes for a Control that opts
        // in. Body Labels/Buttons are untouched, so plain text keeps the engine default face.
        theme.SetTypeVariation(HeaderThemeType, "Label");
        theme.SetFont("font", HeaderThemeType, HeaderFont);

        return theme;
    }

    /// <summary>Load the committed Cinzel asset; degrade to <see cref="ThemeDB.FallbackFont"/>
    /// on any miss (a fresh checkout missing LFS pixels, a stripped test build, etc.) — the
    /// same null-tolerant contract <see cref="IconRegistry"/> already guarantees for art.</summary>
    private static Font LoadHeaderFont() =>
        ResourceLoader.Exists(HeaderFontPath) ? GD.Load<FontFile>(HeaderFontPath) : ThemeDB.FallbackFont;
}
