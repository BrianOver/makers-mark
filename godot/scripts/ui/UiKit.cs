using Godot;

namespace GodotClient.Ui;

/// <summary>
/// P007 U2 (R11/R12/KTD2/KTD3): a themed widget kit layered on <c>SimPanel</c> — reusable
/// builders every screen composes instead of bare rows — plus <see cref="ArtRect"/>, the single
/// fallback-safe bridge between a sim/art concept and its generated texture. Builders read
/// <see cref="GameTheme"/> constants only (no local color/size literals) and rely on Godot's
/// normal Theme cascade (a builder returns a plain typed Control; whichever ancestor's
/// <c>Theme</c> is <see cref="GameTheme.Build"/>'s result supplies the actual stylebox) rather
/// than stamping a per-node override, so one <c>MainUi.Theme</c> assignment restyles every kit
/// widget in the tree at once.
///
/// <para><see cref="ArtRect"/> mirrors the graceful-degrade contract already proven in
/// <see cref="GodotClient.Town.LitTownOverlay"/>'s <c>TryAddBuilding</c>/<c>TryAddHero</c> (null
/// texture → skip cleanly, never a crash) and <c>SimPanel.AddIcon</c> (null-tolerant): on a
/// manifest hit it returns a <see cref="TextureRect"/> carrying the real art; on any miss —
/// asset not generated, unknown id, or the manifest itself absent — it returns a theme-styled
/// placeholder (framed panel + slot/glyph SVG + caption label) so a screen with zero generated
/// art still reads as intentional. Never null, never throws.</para>
/// </summary>
public static class UiKit
{
    /// <summary>Default portrait/art tile edge length (px) — sized for a hero portrait card.</summary>
    public const float PortraitSize = 96f;

    /// <summary>Fallback glyph shown inside an <see cref="ArtRect"/> placeholder when the caller
    /// supplies none — a generic "unknown art" symbol that still reads as intentional.</summary>
    private const string DefaultFallbackGlyph = "rune";

    /// <summary>Semantic tint for a <see cref="StatChip"/>'s value — maps to a fixed
    /// <see cref="GameTheme"/> color so callers never hand-pick a literal.</summary>
    public enum ChipTone
    {
        Neutral,
        Positive,
        Negative,
        Accent,
    }

    /// <summary>A titled <see cref="Section"/>: the outer themed panel to add to a parent, and
    /// the inner body VBox callers add rows/cards into.</summary>
    public readonly record struct SectionView(PanelContainer Root, VBoxContainer Body);

    /// <summary>A plain themed card container — cascade-styled (see type remarks); callers add
    /// their own content (art + stat chips + buttons) as children.</summary>
    public static PanelContainer Card(string? name = null)
    {
        var card = new PanelContainer();
        if (name is not null)
        {
            card.Name = name;
        }

        return card;
    }

    /// <summary>A titled section: a themed panel wrapping a header <see cref="Label"/> (Coolant,
    /// <see cref="GameTheme.HeaderFontSize"/>) over a body <see cref="VBoxContainer"/> callers
    /// populate with cards/rows.</summary>
    public static SectionView Section(string title)
    {
        var root = new PanelContainer { Name = "Section" };
        var body = new VBoxContainer { Name = "SectionBody" };
        root.AddChild(body);

        var header = new Label { Name = "SectionHeader", Text = title };
        header.AddThemeColorOverride("font_color", GameTheme.HeaderColor);
        header.AddThemeFontSizeOverride("font_size", GameTheme.HeaderFontSize);
        body.AddChild(header);

        return new SectionView(root, body);
    }

    /// <summary>A small themed pill: <paramref name="label"/> plus a <paramref name="value"/>
    /// tinted by <paramref name="tone"/> — the gold/atk/def/price readout every card composes.
    /// Both strings render as discoverable <see cref="Label"/> text (see
    /// <c>UiTestSupport.RenderedText</c>).</summary>
    public static Control StatChip(string label, string value, ChipTone tone = ChipTone.Neutral)
    {
        var chip = new PanelContainer { Name = "StatChip" };
        var row = new HBoxContainer { Name = "StatChipRow" };
        chip.AddChild(row);

        var labelNode = new Label { Text = label };
        labelNode.AddThemeColorOverride("font_color", GameTheme.BodyTextColor);
        row.AddChild(labelNode);

        var valueNode = new Label { Name = "Value", Text = value };
        valueNode.AddThemeColorOverride("font_color", ToneColor(tone));
        row.AddChild(valueNode);

        return chip;
    }

    /// <summary>An <see cref="ArtRect"/> in a bordered card sized for a hero portrait — the
    /// class-tinted frame the roster composes per hero.</summary>
    public static Control PortraitFrame(
        string artKey, float size = PortraitSize, Texture2D? fallbackIcon = null, string? caption = null)
    {
        var frame = new PanelContainer { Name = "PortraitFrame" };
        frame.AddChild(ArtRect(artKey, new Vector2(size, size), fallbackIcon, caption));
        return frame;
    }

    /// <summary>
    /// The single fallback-safe art-loader bridge (KTD3): on a manifest hit, a
    /// <see cref="TextureRect"/> (<see cref="TextureRect.StretchModeEnum.KeepAspectCentered"/>)
    /// carrying the generated texture; on any miss, a theme-styled placeholder — a framed panel
    /// holding <paramref name="fallbackIcon"/> (default: a generic rune glyph via
    /// <see cref="IconRegistry.Glyph"/>) plus a caption label. Never null, never throws.
    /// </summary>
    public static Control ArtRect(
        string artKey, Vector2 size, Texture2D? fallbackIcon = null, string? caption = null)
    {
        if (IconRegistry.TryArt(artKey, out var texture))
        {
            return new TextureRect
            {
                Name = "ArtRect",
                Texture = texture,
                CustomMinimumSize = size,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
        }

        var placeholder = new PanelContainer { Name = "ArtRectFallback", CustomMinimumSize = size };
        var body = new VBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        placeholder.AddChild(body);

        var icon = new TextureRect
        {
            Name = "FallbackIcon",
            Texture = fallbackIcon ?? IconRegistry.Glyph(DefaultFallbackGlyph),
            CustomMinimumSize = size * 0.5f,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        body.AddChild(icon);

        var label = new Label
        {
            Name = "FallbackCaption",
            Text = caption ?? artKey,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        body.AddChild(label);

        return placeholder;
    }

    private static Color ToneColor(ChipTone tone) => tone switch
    {
        ChipTone.Positive => GameTheme.CoolantColor,
        ChipTone.Negative => GameTheme.BloodColor,
        ChipTone.Accent => GameTheme.EmberColor,
        _ => GameTheme.BodyTextColor,
    };
}
