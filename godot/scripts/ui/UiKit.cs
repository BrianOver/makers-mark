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
/// <para><see cref="ArtRect"/> mirrors the graceful-degrade contract already proven elsewhere
/// in this codebase (null texture → skip cleanly, never a crash) and <c>SimPanel.AddIcon</c>
/// (null-tolerant): on a
/// manifest hit it returns the real art (a bare <see cref="TextureRect"/>, or that texture
/// stacked over a caption <see cref="Label"/> when the caller passes one); on any miss — asset
/// not generated, unknown id, or the manifest itself absent — it returns a theme-styled
/// placeholder (framed panel + slot/glyph SVG + caption label) so a screen with zero generated
/// art still reads as intentional. Never null, never throws. Sized via
/// <see cref="TextureRect.ExpandMode"/> = <c>IgnoreSize</c> on the real-art path so the
/// REQUESTED <c>size</c> always governs layout, never the source texture's own (typically much
/// larger, ~1024px-square generated-art) pixel dimensions.</para>
/// </summary>
public static class UiKit
{
    /// <summary>Default portrait/art tile edge length (px) — sized for a hero portrait card.</summary>
    public const float PortraitSize = 96f;

    /// <summary>Fallback glyph shown inside an <see cref="ArtRect"/> placeholder when the caller
    /// supplies none — a generic "unknown art" symbol that still reads as intentional.</summary>
    private const string DefaultFallbackGlyph = "rune";

    /// <summary>Minimum width (px) reserved for an <see cref="ArtRect"/> caption on a real-art
    /// hit (R7-class guard — see <see cref="ArtRect"/>'s caption-branch remarks). Sized to fit a
    /// short name/label at <see cref="GameTheme.BodyFontSize"/> without hard-wrapping mid-word,
    /// while still fitting <c>HeroesPanel.RosterCardSize</c>'s card width alongside the themed
    /// panel's own content margins.</summary>
    private const float CaptionMinWidth = 116f;

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
        // P007 polish: opt this title into the display-font theme-type variation (never the
        // base "Label" type) — see GameTheme's HeaderFont remarks.
        header.ThemeTypeVariation = GameTheme.HeaderThemeType;
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

    /// <summary>A tighter <see cref="StatChip"/> for cramped card real estate (U4: the hero
    /// roster card needs 3 chips — Lv/Gold/Deepest — across a ~140px-wide card; the full chip's
    /// <see cref="GameTheme.PanelStyle"/> margins alone (12px/side) ate ~270px across 3). Shrinks
    /// the stylebox's content margins via a per-node stylebox override on a duplicated
    /// <see cref="GameTheme.PanelStyle"/> instance — <see cref="GameTheme"/>'s own margin constant
    /// stays untouched and every OTHER themed panel in the app keeps its normal breathing room.
    /// Text stays at <see cref="GameTheme.LegibilityFloor"/>, never smaller.</summary>
    public static Control StatChipCompact(string label, string value, ChipTone tone = ChipTone.Neutral)
    {
        var chip = new PanelContainer { Name = "StatChipCompact" };
        chip.AddThemeStyleboxOverride("panel", CompactChipStyle());

        var row = new HBoxContainer { Name = "StatChipRow" };
        row.AddThemeConstantOverride("separation", CompactChipSeparation);
        chip.AddChild(row);

        var labelNode = new Label { Text = label };
        labelNode.AddThemeColorOverride("font_color", GameTheme.BodyTextColor);
        labelNode.AddThemeFontSizeOverride("font_size", GameTheme.LegibilityFloor);
        row.AddChild(labelNode);

        var valueNode = new Label { Name = "Value", Text = value };
        valueNode.AddThemeColorOverride("font_color", ToneColor(tone));
        valueNode.AddThemeFontSizeOverride("font_size", GameTheme.LegibilityFloor);
        row.AddChild(valueNode);

        return chip;
    }

    /// <summary>Label/value gap (px) inside a <see cref="StatChipCompact"/> row — tighter than
    /// the full <see cref="StatChip"/>'s themed default HBox separation.</summary>
    private const int CompactChipSeparation = 3;

    /// <summary>Content margins (px) for <see cref="StatChipCompact"/> — a fraction of
    /// <see cref="GameTheme.PanelStyle"/>'s own margin, applied as a per-node override so the
    /// global constant it reads stays untouched.</summary>
    private const float CompactChipMarginX = 4f;
    private const float CompactChipMarginY = 2f;

    private static StyleBoxFlat CompactChipStyle()
    {
        var style = (StyleBoxFlat)GameTheme.PanelStyle().Duplicate();
        style.ContentMarginLeft = CompactChipMarginX;
        style.ContentMarginRight = CompactChipMarginX;
        style.ContentMarginTop = CompactChipMarginY;
        style.ContentMarginBottom = CompactChipMarginY;
        return style;
    }

    /// <summary>An <see cref="ArtRect"/> in a bordered card sized for a hero portrait — the
    /// class-tinted frame the roster composes per hero.</summary>
    public static Control PortraitFrame(
        string artKey, float size = PortraitSize, Texture2D? fallbackIcon = null, string? caption = null,
        bool ellipsizeCaption = false)
    {
        var frame = new PanelContainer { Name = "PortraitFrame" };
        frame.AddChild(ArtRect(artKey, new Vector2(size, size), fallbackIcon, caption, ellipsizeCaption));
        return frame;
    }

    /// <summary>
    /// The single fallback-safe art-loader bridge (KTD3): on a manifest hit, a
    /// <see cref="TextureRect"/> (<see cref="TextureRect.StretchModeEnum.KeepAspectCentered"/>,
    /// <see cref="TextureRect.ExpandModeEnum.IgnoreSize"/> so <paramref name="size"/> — not the
    /// source texture's own pixel dimensions — governs the minimum size) carrying the generated
    /// texture, stacked over a centered caption <see cref="Label"/> when <paramref name="caption"/>
    /// is non-null; on any miss, a theme-styled placeholder — a framed panel holding
    /// <paramref name="fallbackIcon"/> (default: a generic rune glyph via
    /// <see cref="IconRegistry.Glyph"/>) plus a caption label. Never null, never throws.
    /// </summary>
    public static Control ArtRect(
        string artKey, Vector2 size, Texture2D? fallbackIcon = null, string? caption = null,
        bool ellipsizeCaption = false)
    {
        if (IconRegistry.TryArt(artKey, out var texture))
        {
            var textureRect = new TextureRect
            {
                Name = "ArtRect",
                Texture = texture,
                CustomMinimumSize = size,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                // Central fix for the class of bug LW5 first hit and patched locally (DepthsPanel,
                // PR #119): ExpandMode defaults to KeepSize, whose GetMinimumSize() reports the
                // TEXTURE'S OWN pixel size — every generated asset ships ~1024px square — so
                // GetCombinedMinimumSize() = max(CustomMinimumSize, that native size) silently
                // overrode the requested `size` (a 96px portrait, a 56px item icon, ...), ballooning
                // the tile and squeezing every sibling label to one character per line. IgnoreSize
                // lets `size` alone govern layout, as every caller here already assumes.
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };

            if (caption is null)
            {
                return textureRect;
            }

            // Caption on a real-art HIT (previously silently dropped — only the no-art fallback
            // below ever built a Label, so e.g. the hero roster's PortraitFrame caption never
            // rendered in the normal case where art exists). Stack the art over a centered
            // caption, mirroring the fallback's own art-over-caption shape below.
            //
            // R7-class guard: reserve width beyond the bare art tile for the caption. A square
            // portrait/item tile (e.g. PortraitSize=96) is narrower than a short name needs at
            // GameTheme.BodyFontSize — without this floor a WordSmart label can be squeezed
            // narrow enough to hard-wrap mid-word, the exact defect item 3 of this fix targets.
            // KeepAspectCentered still renders the art at its own aspect within the wider cell.
            var captioned = new VBoxContainer
            {
                Name = "ArtRectCaptioned",
                CustomMinimumSize = new Vector2(Mathf.Max(size.X, CaptionMinWidth), 0),
            };
            captioned.AddChild(textureRect);
            captioned.AddChild(CaptionLabel(caption, ellipsizeCaption));
            return captioned;
        }

        // R7-class guard (same reasoning as the real-art caption branch above): this placeholder
        // ALWAYS renders a caption (`caption ?? artKey`, never suppressed), so a small requested
        // `size` (e.g. a 56px shop-card item icon) left this label just as narrow as the real-art
        // one used to be — playtest findings 2026-07-19 §8's "Pine/Buckle/r",
        // "Soldier/'s/Longs/word" reproduce on exactly this branch (the rival catalog's items
        // carry no committed art, so every rival-shelf card hits this placeholder, not the
        // real-art branch above).
        var placeholder = new PanelContainer
        {
            Name = "ArtRectFallback",
            CustomMinimumSize = new Vector2(Mathf.Max(size.X, CaptionMinWidth), size.Y),
        };
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

        var label = CaptionLabel(caption ?? artKey, ellipsizeCaption, fallbackName: true);
        body.AddChild(label);

        return placeholder;
    }

    /// <summary>Build an <see cref="ArtRect"/> caption label: word-wrapped (never mid-word) by
    /// default, or single-line ellipsized when <paramref name="ellipsize"/> is true — the roster
    /// card's shape (U4), where a long hero name must clip with an ellipsis rather than wrap and
    /// blow out the card's fixed-column height.</summary>
    private static Label CaptionLabel(string text, bool ellipsize, bool fallbackName = false)
    {
        var label = new Label
        {
            Name = fallbackName ? "FallbackCaption" : "Caption",
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        if (ellipsize)
        {
            label.ClipText = true;
            label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            label.AutowrapMode = TextServer.AutowrapMode.Off;
        }
        else
        {
            // Word, not WordSmart (R7-class guard): WordSmart falls back to an
            // arbitrary/per-character break when a single word doesn't fit the box — the exact
            // "Soldier's Longsword" -> "Soldier's/Longswor/d" mid-word split the playtest findings
            // quote. Word wraps ONLY at word boundaries, full stop; a caption box narrower than
            // one word overflows slightly rather than fragmenting.
            label.AutowrapMode = TextServer.AutowrapMode.Word;
        }

        return label;
    }

    private static Color ToneColor(ChipTone tone) => tone switch
    {
        ChipTone.Positive => GameTheme.CoolantColor,
        ChipTone.Negative => GameTheme.BloodColor,
        ChipTone.Accent => GameTheme.EmberColor,
        _ => GameTheme.BodyTextColor,
    };
}
