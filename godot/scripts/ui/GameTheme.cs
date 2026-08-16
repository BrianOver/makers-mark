using Godot;

namespace GodotClient.Ui;

/// <summary>
/// P007 U1 (R11/KTD1): the one shared, programmatic Godot <see cref="Theme"/> — font sizes,
/// style-bible colors, spacing, and per-control-type <see cref="StyleBoxFlat"/>s — assigned once
/// at the <c>MainUi</c> root (<c>this.Theme = GameTheme.Build();</c>, set BEFORE <c>BuildUi()</c>)
/// so Godot's normal Theme cascade carries it to every descendant Control with zero
/// <c>project.godot</c> contact (deny-listed). Built entirely in code, mirroring the static-factory
/// shape of <see cref="IconRegistry"/>.
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
/// <para>P007 polish (display font), swapped U10 (asset completion wave, "ship the pixel
/// font"): <see cref="HeaderFont"/> — the OFL-licensed Silkscreen pixel display face
/// (<c>godot/assets/fonts/</c>, licence alongside it — see <c>Silkscreen-OFL.txt</c>) — is
/// registered ONLY on the <see cref="HeaderThemeType"/> theme-type variation, never on the base
/// "Label"/"Button" types. Body text stays the engine default everywhere (legibility + layout
/// stability, R11 — see the sizing remarks at <see cref="LegibilityFloor"/>'s TODO for why body
/// specifically was NOT swapped); only a Control that opts in via
/// <c>ThemeTypeVariation = GameTheme.HeaderThemeType</c> — today,
/// <see cref="GodotClient.Panels.SimPanel.AddHeader"/> and <see cref="UiKit.Section"/>'s
/// title — picks it up. Null-tolerant like every other art loader on this project
/// (<see cref="IconRegistry"/>): a missing font resource degrades to
/// <see cref="ThemeDB.FallbackFont"/>, never a throw.</para>
///
/// <para>U10 replaced Cinzel (a classical serif, never a good fit for this game's pixel-art
/// 2.5D presentation — see <c>docs/design/ASSETS.md</c> §8 item 10) rather than adding it as a
/// third face: Cinzel had exactly one consumer (this file's own <see cref="HeaderFontPath"/>),
/// so keeping both committed would have left one an orphan asset the moment Silkscreen took
/// the header slot. The Cinzel TTF/licence files were removed in the same PR.</para>
/// </summary>
public static class GameTheme
{
    /// <summary>Theme-type variation carrying <see cref="HeaderFont"/> (see type remarks) —
    /// a Control opts in by setting its own <c>ThemeTypeVariation</c> to this constant.</summary>
    public const string HeaderThemeType = "HeaderLabel";

    /// <summary>The committed OFL pixel display font asset (Silkscreen Regular, an 8px-grid
    /// pixel face) — see <c>godot/assets/fonts/Silkscreen-OFL.txt</c> for the licence. Public
    /// (not just reachable via <see cref="HeaderFont"/>) so a test can assert the theme
    /// resolves this EXACT committed asset — "non-null" alone would also be satisfied by a
    /// silent <see cref="ThemeDB.FallbackFont"/> degrade, which is the failure class this
    /// repo's null-tolerant loaders keep needing a real regression test for.</summary>
    public const string HeaderFontPath = "res://assets/fonts/Silkscreen-Regular.ttf";

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

    // ── Semantic aliases (UI-1) ────────────────────────────────────────────────────────────────
    // Named by MEANING, not by raw palette entry, so a panel reaching for "the currency color"
    // or "the danger color" never has to know/guess which style-bible hue backs it today. Every
    // alias below still resolves to an existing palette color (or a cheap derivation of one) —
    // this is a naming layer, not a new palette.

    /// <summary>Currency-only tone (gold counts, prices, the HUD gold chip). Deliberately its own
    /// hue, not a repurposed palette color — the game's ONE currency should never be confused
    /// with a stat tone.</summary>
    public static readonly Color GoldColor = new("e8c15a");

    /// <summary>"Good"/affirmative reads (surplus, success) — alias for <see cref="CoolantColor"/>.</summary>
    public static readonly Color GoodColor = CoolantColor;

    /// <summary>Warm caution reads (non-alarming friction, R6-class) — alias for <see cref="EmberColor"/>.</summary>
    public static readonly Color WarnColor = EmberColor;

    /// <summary>True danger/death reads only — alias for <see cref="BloodColor"/>.</summary>
    public static readonly Color DangerColor = BloodColor;

    /// <summary>Deepest background layer (behind every panel) — alias for <see cref="VoidColor"/>.</summary>
    public static readonly Color SurfaceDeep = VoidColor;

    /// <summary>Default panel/card fill — alias for <see cref="IronColor"/>.</summary>
    public static readonly Color Surface = IronColor;

    /// <summary>A surface lifted one step above <see cref="Surface"/> — hover/selected rows,
    /// nested cards.</summary>
    public static readonly Color SurfaceRaised = IronColor.Lightened(0.08f);

    /// <summary>De-emphasized body text (secondary labels, "owned ×N" counts) — <see cref="BoneColor"/>
    /// at reduced alpha rather than a separate gray, so it still reads as the same body-text family.</summary>
    public static readonly Color TextDim = new(BoneColor, 0.6f);

    // ── Sizes ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>Minimum legible font size at target resolution (R11) — the floor every default
    /// and per-type font size below must meet or exceed.</summary>
    public const int LegibilityFloor = 16;

    // RESOLVED (U10, asset completion wave, "ship the pixel font"): the pixel face landed --
    // Silkscreen (OFL) now renders the display/heading/label tier (HeaderFont/HeaderFontPath
    // above), replacing Cinzel. Body stayed engine default DELIBERATELY, not by default:
    // Silkscreen's lowercase glyphs are cap-height (a property of the original 2001 bitmap
    // design, carried into the Google Fonts TTF), so setting it on the base "Label"/"Button"
    // types turns every sentence of prose into visual ALL-CAPS -- verified by temporarily
    // wiring it onto Label/Button and rendering the Vigil dock's own retelling text ("They've
    // made camp above the deep floors...") and the tutorial card's teach copy through it
    // (tools/receipt.ps1; runs/receipts/u10-body-ledger.png, u10-body-forge.png -- runs/ is
    // gitignored, kept locally for reference only). That is exactly the "worse to read than
    // what ships today" failure this TODO warned about, so the swap stops at display/heading/
    // label, same as the class doc above states. HeaderFontSize (22) was checked against
    // Silkscreen's 8px design grid and left unchanged -- the rendered receipts show it already
    // crisp and clipping-free across Forge/Shop/Ledger; a purist 24px (3x grid) round number is
    // a low-priority follow-up the evidence did not demand.

    /// <summary>Default body/control font size — held at the legibility floor (R11); the cozy
    /// redesign (UI-1) tightened this from the prior +2 bump now that spacing/hierarchy carry
    /// more of the visual weight than raw text size.</summary>
    public const int BodyFontSize = 16;

    /// <summary>Section/card header font size.</summary>
    public const int HeaderFontSize = 22;

    /// <summary>HUD stat-value font size (UI-1) — one step above <see cref="BodyFontSize"/> so the
    /// HUD's live numbers (gold, day) read as the thing the player glances at first.</summary>
    public const int HudValueFontSize = 20;

    /// <summary>Modal title font size (U-T5, the Evening Ledger fix) — bigger than <see
    /// cref="HeaderFontSize"/>'s in-card section headers, because a modal's own title names the
    /// whole screen the player is looking at, not one card inside it. Before this fix the Ledger's
    /// title used the base "EVENING LEDGER — day N" AddLabel call, so it rendered at
    /// <see cref="BodyFontSize"/> — the SAME size as the smallest text on screen.</summary>
    public const int TitleFontSize = 28;

    // ── Spacing / shape scale (UI-1) ───────────────────────────────────────────────────────────
    // A small fixed step scale so every builder's margins/gaps come from the SAME ladder instead
    // of one-off literals — the thing that makes a screen read as "designed" rather than "sized
    // by whichever builder touched it last".
    public const int Space4 = 4;
    public const int Space8 = 8;
    public const int Space12 = 12;
    public const int Space16 = 16;

    /// <summary>Corner radius for small chip-scale controls (stat chips, icon chips).</summary>
    public const int RadiusChip = 4;

    /// <summary>Corner radius for panel-scale controls (cards, sections, drawers).</summary>
    public const int RadiusPanel = 8;

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
        CornerRadiusBottomLeft = RadiusPanel,
        CornerRadiusBottomRight = RadiusPanel,
        CornerRadiusTopLeft = RadiusPanel,
        CornerRadiusTopRight = RadiusPanel,
        ContentMarginLeft = PanelContentMargin,
        ContentMarginRight = PanelContentMargin,
        ContentMarginTop = PanelContentMargin,
        ContentMarginBottom = PanelContentMargin,
    };

    /// <summary>The committed pixel-art wood 9-patch frame (<c>ui-frame-wood.png</c>, a 48×48
    /// texture) used for cozy-styled panels (drawers, dialogs) that want a hand-authored border
    /// instead of the flat <see cref="PanelStyle"/> rectangle. 12px texture margins keep the
    /// corner/edge art crisp while the center tiles/stretches to fill; 12px content margins match
    /// so text never crowds the frame.</summary>
    private const string WoodFramePath = "res://assets/art/ui-frame-wood.png";
    private const float WoodFrameMargin = 12f;

    /// <summary>Timber-brown border used only by <see cref="PanelStyleWood"/>'s flat fallback —
    /// not a general-purpose alias (nothing else in the theme reads this hue), so it stays
    /// private rather than joining the semantic-alias set above.</summary>
    private static readonly Color TimberBrownColor = new("4a3222");

    /// <summary>
    /// A wood-framed panel StyleBox for cozy-styled surfaces (UI-1). Null-tolerant, mirroring
    /// <see cref="LoadHeaderFont"/>'s exact degrade contract: on a fresh checkout / stripped test
    /// build missing <see cref="WoodFramePath"/>, this falls back to a flat <see cref="Surface"/>
    /// panel with a 1px timber-brown border and the same <see cref="RadiusPanel"/> corners —
    /// never null, never a throw, and the caller never needs to branch on which it got.
    /// </summary>
    public static StyleBox PanelStyleWood()
    {
        if (!ResourceLoader.Exists(WoodFramePath))
        {
            return new StyleBoxFlat
            {
                BgColor = Surface,
                BorderColor = TimberBrownColor,
                BorderWidthBottom = 1,
                BorderWidthLeft = 1,
                BorderWidthRight = 1,
                BorderWidthTop = 1,
                CornerRadiusBottomLeft = RadiusPanel,
                CornerRadiusBottomRight = RadiusPanel,
                CornerRadiusTopLeft = RadiusPanel,
                CornerRadiusTopRight = RadiusPanel,
                ContentMarginLeft = WoodFrameMargin,
                ContentMarginRight = WoodFrameMargin,
                ContentMarginTop = WoodFrameMargin,
                ContentMarginBottom = WoodFrameMargin,
            };
        }

        return new StyleBoxTexture
        {
            Texture = GD.Load<Texture2D>(WoodFramePath),
            TextureMarginLeft = WoodFrameMargin,
            TextureMarginRight = WoodFrameMargin,
            TextureMarginTop = WoodFrameMargin,
            TextureMarginBottom = WoodFrameMargin,
            ContentMarginLeft = WoodFrameMargin,
            ContentMarginRight = WoodFrameMargin,
            ContentMarginTop = WoodFrameMargin,
            ContentMarginBottom = WoodFrameMargin,
        };
    }

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
            CornerRadiusBottomLeft = RadiusPanel,
            CornerRadiusBottomRight = RadiusPanel,
            CornerRadiusTopLeft = RadiusPanel,
            CornerRadiusTopRight = RadiusPanel,
            ContentMarginLeft = PanelContentMargin,
            ContentMarginRight = PanelContentMargin,
            ContentMarginTop = PanelContentMargin * 0.5f,
            ContentMarginBottom = PanelContentMargin * 0.5f,
        };
    }

    /// <summary>
    /// The main-verb button surface (UI-1) — an Ember fill formalizing the ad-hoc per-node
    /// override <c>MainUi.StylePrimary</c> already hand-builds for its one Accent-tinted primary
    /// action. This is a DIFFERENT, warmer treatment (Ember, not Accent/Arcane) for the cozy
    /// redesign's main verb buttons (e.g. a drawer's "Craft"/"Buy" CTA); same shape (border/
    /// radius/margins) as <see cref="ButtonStyle"/> so it drops into the same
    /// <c>AddThemeStyleboxOverride("normal"/"hover"/"pressed", ...)</c> per-node pattern that
    /// call site already uses — nothing here touches <see cref="Build"/>'s shared Button type
    /// slots (those stay Iron via <see cref="ButtonStyle"/>).
    /// </summary>
    public static StyleBoxFlat ButtonStylePrimary(ButtonVisualState state = ButtonVisualState.Normal)
    {
        var bg = state switch
        {
            ButtonVisualState.Hover => EmberColor.Lightened(0.15f),
            ButtonVisualState.Pressed => EmberColor.Darkened(0.15f),
            ButtonVisualState.Disabled => EmberColor.Darkened(0.35f),
            _ => EmberColor,
        };

        return new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = new Color(BoneColor, state == ButtonVisualState.Pressed ? 0.9f : 0.4f),
            BorderWidthBottom = PanelBorderWidth,
            BorderWidthLeft = PanelBorderWidth,
            BorderWidthRight = PanelBorderWidth,
            BorderWidthTop = PanelBorderWidth,
            CornerRadiusBottomLeft = RadiusPanel,
            CornerRadiusBottomRight = RadiusPanel,
            CornerRadiusTopLeft = RadiusPanel,
            CornerRadiusTopRight = RadiusPanel,
            ContentMarginLeft = PanelContentMargin,
            ContentMarginRight = PanelContentMargin,
            ContentMarginTop = PanelContentMargin * 0.5f,
            ContentMarginBottom = PanelContentMargin * 0.5f,
        };
    }

    /// <summary>Scrollbar track (U-T5): a faint Void-toned rounded channel — off the SAME
    /// <see cref="RadiusChip"/> corner scale every other small control uses, never a bespoke
    /// radius. Before this fix <see cref="Build"/> registered zero <c>VScrollBar</c> entries, so
    /// every scrollable panel (the Evening Ledger included) shipped the bare engine-default
    /// scrollbar with no themed track/grabber at all.</summary>
    public static StyleBoxFlat ScrollBarTrackStyle() => new()
    {
        BgColor = new Color(VoidColor, 0.5f),
        CornerRadiusBottomLeft = RadiusChip,
        CornerRadiusBottomRight = RadiusChip,
        CornerRadiusTopLeft = RadiusChip,
        CornerRadiusTopRight = RadiusChip,
    };

    /// <summary>Scrollbar grabber (thumb) for one interaction state — the same Accent progression
    /// <see cref="ButtonStyle"/> uses for a button's normal/hover/pressed surfaces, so a dragged
    /// scrollbar reads as part of this theme rather than the engine's stock gray thumb.</summary>
    public static StyleBoxFlat ScrollBarGrabberStyle(ButtonVisualState state = ButtonVisualState.Normal) => new()
    {
        BgColor = state switch
        {
            ButtonVisualState.Hover => AccentColor.Lightened(0.15f),
            ButtonVisualState.Pressed => AccentColor.Darkened(0.1f),
            _ => AccentColor,
        },
        CornerRadiusBottomLeft = RadiusChip,
        CornerRadiusBottomRight = RadiusChip,
        CornerRadiusTopLeft = RadiusChip,
        CornerRadiusTopRight = RadiusChip,
    };

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

        // U-T5: the Evening Ledger's own overflow scrollbar was the last one riding the bare
        // engine default (zero VScrollBar entries existed before this) — every ScrollContainer
        // in the app disables horizontal scrolling (see SimPanel.AddLabel's own remarks), so only
        // the vertical thumb/track ever render, and only VScrollBar needs theming.
        theme.SetStylebox("scroll", "VScrollBar", ScrollBarTrackStyle());
        theme.SetStylebox("grabber", "VScrollBar", ScrollBarGrabberStyle());
        theme.SetStylebox("grabber_highlighted", "VScrollBar", ScrollBarGrabberStyle(ButtonVisualState.Hover));
        theme.SetStylebox("grabber_pressed", "VScrollBar", ScrollBarGrabberStyle(ButtonVisualState.Pressed));

        // UI-1: OptionButton previously fell through to the naked engine default (a light
        // system-gray dropdown floating on top of every dark themed panel). Give it the SAME
        // Iron "normal" surface + text colors/size as Button so a dropdown reads as part of this
        // theme rather than a stray control the cascade forgot. Hover/pressed/disabled are left
        // to the engine default for now — no OptionButton on any current screen exercises those
        // states in a way that reads badly; revisit if a future screen needs the full set.
        theme.SetStylebox("normal", "OptionButton", ButtonStyle(ButtonVisualState.Normal));
        theme.SetColor("font_color", "OptionButton", BodyTextColor);
        theme.SetFontSize("font_size", "OptionButton", BodyFontSize);

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

    /// <summary>Load the committed Silkscreen asset; degrade to <see cref="ThemeDB.FallbackFont"/>
    /// on any miss (a fresh checkout missing LFS pixels, a stripped test build, etc.) — the
    /// same null-tolerant contract <see cref="IconRegistry"/> already guarantees for art.</summary>
    private static Font LoadHeaderFont() =>
        ResourceLoader.Exists(HeaderFontPath) ? GD.Load<FontFile>(HeaderFontPath) : ThemeDB.FallbackFont;
}
