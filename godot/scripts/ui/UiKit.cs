using System;
using Godot;
using GodotClient.Tools;

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

        /// <summary>Currency-only tone (UI-1) — resolves to <see cref="GameTheme.GoldColor"/>, the
        /// one hue reserved for gold counts/prices so a stat chip never has to fake it with
        /// <see cref="Accent"/>.</summary>
        Gold,
    }

    /// <summary>A titled <see cref="Section"/>: the outer themed panel to add to a parent, and
    /// the inner body VBox callers add rows/cards into.</summary>
    public readonly record struct SectionView(PanelContainer Root, VBoxContainer Body);

    /// <summary>
    /// Makes <paramref name="overlay"/> actually receive keyboard input, and keep receiving it.
    ///
    /// <para><b>Why this exists.</b> A <see cref="Control"/> only gets key events in
    /// <c>_GuiInput</c> while it HAS FOCUS. Every minigame overlay set
    /// <c>FocusMode = FocusModeEnum.All</c> — one even with the comment "so _GuiInput actually
    /// receives keyboard events" — and then never called <see cref="Control.GrabFocus"/>. Declaring
    /// yourself focus-able is not the same as being focused, so EVERY keyboard control in EVERY
    /// minigame was dead: Space, Shift, the arrow keys, all of it. Found by Brian's playtest
    /// (2026-07-30) reporting the forge could not be completed.</para>
    ///
    /// <para>In the forge that one omission made the craft unwinnable rather than merely awkward:
    /// heat drains continuously and a strike's shape-advance is proportional to current heat, so
    /// with the bellows (Shift) unreachable the heat floors, strikes stop advancing anything, and the
    /// bellows-pump drag actively drifts shape back toward 0 — the "shape keeps resetting to zero"
    /// symptom. A dead modifier key read as a broken game.</para>
    ///
    /// <para><b>Deferred</b> because an overlay claims focus during its own build, while its child
    /// <see cref="Button"/>s are still being added, and often before it is even in the tree (grabbing
    /// focus outside the tree does nothing at all). The deferred call runs once the whole build has
    /// finished, so it wins that race. Guarded on still-valid/in-tree/visible, because an overlay can
    /// be cancelled or freed between building and the deferred callback landing. Call
    /// <see cref="ReclaimKeyboard"/> from the overlay's mouse-press handler to take focus back if a
    /// button steals it mid-session (which also matters because Space on a focused Button presses
    /// the Button instead of striking the billet).</para>
    /// </summary>
    public static void ClaimKeyboard(Control overlay)
    {
        overlay.FocusMode = Control.FocusModeEnum.All;

        Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(overlay) && overlay.IsInsideTree() && overlay.IsVisibleInTree())
            {
                overlay.GrabFocus();
            }
        }).CallDeferred();
    }

    /// <summary>Take keyboard focus back immediately — for an overlay's own mouse-press handler, so
    /// clicking inside it (or on one of its buttons) does not leave the keyboard pointed elsewhere.
    /// Synchronous, unlike <see cref="ClaimKeyboard"/>: by press time the tree is long settled.</summary>
    public static void ReclaimKeyboard(Control overlay)
    {
        if (!overlay.HasFocus())
        {
            overlay.GrabFocus();
        }
    }

    /// <summary>
    /// Makes every <see cref="Button"/> under <paramref name="root"/> unfocusable, so none of them can
    /// take the keyboard away from an overlay that owns it. Call AFTER the buttons exist.
    ///
    /// <para><b>Why this is necessary and not paranoia.</b> A focused <see cref="Button"/> consumes
    /// Space and Enter to press ITSELF. So in the forge, clicking the on-screen "Bellows (hold Shift)"
    /// button once moved focus to it permanently, and from then on Space pumped the bellows instead of
    /// striking the billet, while Shift reached nothing at all. Brian's second playtest reported exactly
    /// that: "shift doesn't do and space seems to actually be the bellows" — after the first fix had
    /// already made the keyboard work on open.</para>
    ///
    /// <para>The first fix (<see cref="ClaimKeyboard"/>) grabbed focus when the overlay opened, which is
    /// necessary and was not sufficient: the very next click on any control button handed it straight
    /// back. <see cref="ReclaimKeyboard"/> cannot save it either, because a Button consumes the press
    /// and the overlay's own <c>_GuiInput</c> never sees it.</para>
    ///
    /// <para>Removing focus rather than fighting over it is the fix that cannot regress. These buttons
    /// are mouse affordances that duplicate keyboard verbs already handled by the overlay — nothing is
    /// lost by making them mouse-only, and keyboard users are strictly better served by the overlay
    /// keeping the keys. This is also why the buttons' labels name the key: they are a legend as much as
    /// a control.</para>
    /// </summary>
    public static void MakeButtonsMouseOnly(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is Button button)
            {
                button.FocusMode = Control.FocusModeEnum.None;
            }

            MakeButtonsMouseOnly(child);
        }
    }

    /// <summary>A plain themed card container — cascade-styled (see type remarks); callers add
    /// their own content (art + stat chips + buttons) as children.
    ///
    /// <para><b>MouseFilter = Ignore, not the PanelContainer default (Stop).</b> A card is a
    /// decoration around interactive children (buttons, spin boxes) — same "decoration must
    /// never eat clicks" rule already applied to icons/banners elsewhere in this file — but its
    /// own background, being a Control, defaults to Stop. Left alone, that silently swallows any
    /// input the card's rect covers and does not itself consume, including a mouse-wheel scroll
    /// meant for an ancestor <see cref="ScrollContainer"/>: whenever the wheel lands on a card
    /// instead of a gap between cards, the event is marked handled right there and never climbs
    /// the tree, so the page does not scroll. Measured cause of
    /// <c>DeepPilotPlayTests.CompetentPlayer_ReachesDayEleven_WithRealCrafts</c> reproducibly
    /// failing to reach a Forge recipe below the fold — the fixed wheel-scroll point this
    /// engine-test harness uses landed on <see cref="Section"/>'s own panel (see that method's
    /// note), but any <see cref="Card"/> in any scrollable body has the identical defect, so both
    /// are fixed together. A real player is not universally stuck (dragging the scrollbar thumb
    /// still works, since it is a sibling outside this rect), but the single most natural
    /// scroll gesture silently doing nothing over most of a populated list is exactly the
    /// "control exists, every property looks right, and it is still unreachable" class of bug
    /// this repo already treats as a defect, not a test artifact.</para>
    /// </summary>
    public static PanelContainer Card(string? name = null)
    {
        var card = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        if (name is not null)
        {
            card.Name = name;
        }

        return card;
    }

    /// <summary>A titled section: a themed panel wrapping a header <see cref="Label"/> (Coolant,
    /// <see cref="GameTheme.HeaderFontSize"/>) over a body <see cref="VBoxContainer"/> callers
    /// populate with cards/rows.
    ///
    /// <para>Root's <c>MouseFilter</c> is <c>Ignore</c>, not the <see cref="PanelContainer"/>
    /// default (Stop) — see <see cref="Card"/>'s own remarks for why: this exact root swallowing
    /// wheel-scroll meant for an ancestor <see cref="ScrollContainer"/> is the confirmed root
    /// cause fixed alongside it.</para>
    ///
    /// <para>U8 (§11.14.14, container/section tutorial anchors): <see cref="SectionView.Root"/>'s
    /// own <see cref="Node.Name"/> is now derived from <paramref name="title"/> (<see
    /// cref="SectionName"/>), not the one literal "Section" every section used to share. Before
    /// this unit every <see cref="Section"/>-built root in a panel answered to the SAME name —
    /// harmless for a panel with exactly one section, but <see cref="Panels.ShopPanel"/> alone
    /// composes four ("Who Would Buy This", "Your Shelf", "Unshelved Crafts", "Rival Shelf") in
    /// the same scroll, so <c>FindChild("Section", recursive: true)</c> could only ever reach
    /// whichever one happens to sit first in tree order. <c>ForgePanel.BuildUi</c>'s own
    /// "Morning Vendor" section already worked around this by hand-patching
    /// <c>vendorSection.Root.Name = "VendorSection"</c> straight after the call — a fix a caller
    /// has to remember to apply, and every OTHER section in that same panel still did not. Baking
    /// the derivation into this one factory turns "give this section a stable name" from an
    /// opt-in a caller can forget into a contract every section gets whether the caller asked or
    /// not — the substrate <see cref="TutorialAnchorKind.PanelSection"/> anchors (<see
    /// cref="TutorialFlow"/>) point at.</para>
    /// </summary>
    public static SectionView Section(string title)
    {
        var root = new PanelContainer { Name = SectionName(title), MouseFilter = Control.MouseFilterEnum.Ignore };
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

    /// <summary>
    /// U8 (§11.14.14): the naming CONVENTION <see cref="Section"/>'s own root commits to — every
    /// word character of <paramref name="title"/> Title-Cased and concatenated, punctuation and
    /// whitespace dropped, "Section" appended ("Unshelved Crafts" -&gt; "UnshelvedCraftsSection").
    /// Pure and static so a registry row's own hardcoded expectation (<see
    /// cref="TutorialAnchor.ForPanelSection"/>) and this method can independently drift apart —
    /// the whole point (this unit's own conformance test): if a section's <paramref name="title"/>
    /// changes, or the constant string a registry row names never gets updated to match, the
    /// mismatch is a live resolution failure, never a silent rename. A registry row is expected to
    /// spell the string literally rather than call this method itself, the same "pinned twice"
    /// discipline <see cref="TutorialFlow"/>'s own registry/test pairs already use elsewhere —
    /// coupling the row to this method would let a title rename silently drag the row's own
    /// expectation along with it, defeating the one property a naming CONVENTION exists to buy.
    /// </summary>
    public static string SectionName(string title)
    {
        var name = new System.Text.StringBuilder(title.Length + 7);
        var startOfWord = true;
        foreach (var ch in title)
        {
            if (!char.IsLetterOrDigit(ch))
            {
                startOfWord = true;
                continue;
            }

            name.Append(startOfWord ? char.ToUpperInvariant(ch) : ch);
            startOfWord = false;
        }

        return name.Append("Section").ToString();
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

    /// <summary>
    /// U7 (top-bar-explains-itself plan): a small themed pill printing a bound key's label (e.g.
    /// <c>"F11"</c>, from <see cref="ShortcutMap.KeyLabel"/>) — reuses <see
    /// cref="StatChipCompact"/>'s tight pill shape/margins so it sits beside a 24-36px HUD button
    /// without dominating it. This is the "render the badge inline on the button, not only on
    /// hover" half of the unit: <see cref="Control.TooltipText"/> still carries the full
    /// what-it-does-plus-key sentence (<see cref="ShortcutMap.Tooltip"/>), but a control with a
    /// real key no longer needs a hover to prove it has one.
    /// </summary>
    public static Control ShortcutBadge(string keyLabel)
    {
        var badge = new PanelContainer { Name = "ShortcutBadge", MouseFilter = Control.MouseFilterEnum.Ignore };
        badge.AddThemeStyleboxOverride("panel", CompactChipStyle());

        var label = new Label
        {
            Name = "ShortcutBadgeLabel",
            Text = keyLabel,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        label.AddThemeColorOverride("font_color", GameTheme.TextDim);
        label.AddThemeFontSizeOverride("font_size", GameTheme.LegibilityFloor);
        badge.AddChild(label);

        return badge;
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
        WarnOnceOnArtMiss(artKey);

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

    /// <summary>Art keys already reported missing this process — <see cref="ArtRect"/> is rebuilt on
    /// every panel refresh, so without this an absent icon would push one warning per redraw and
    /// bury every other anomaly in the playtest log.</summary>
    private static readonly System.Collections.Generic.HashSet<string> ArtMissWarned =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Announce the one degrade in this file a player actually sees: a requested art id had no
    /// committed pixels, so the caller gets a captioned placeholder box instead of the thing.
    ///
    /// <para>This is the log whose absence let six craftable Tier 8-14 recipes ship with no icon
    /// and no playtest run ever notice — the fallback is deliberately graceful, and graceful with
    /// no message is indistinguishable from working. <see cref="IconRegistry.Art"/> and
    /// <c>TryArt</c> stay silent on purpose: they are also the probe primitive for legitimately
    /// optional lookups (a flat icon has no <c>_n</c> normal map; <c>TownAssets2D.ForHero</c> walks
    /// a fallback ladder on purpose), so warning there would cry wolf. THIS site is the one that
    /// only runs when a real placeholder is about to be drawn.</para>
    ///
    /// <para>Once per key per process, so `EngineLogAnomalies.Scan` sees each distinct missing id
    /// exactly once no matter how many times its panel is redrawn.</para>
    /// </summary>
    private static void WarnOnceOnArtMiss(string artKey)
    {
        if (ArtMissWarned.Add(artKey))
        {
            EngineDistress.Warn(
                $"[UiKit] no committed art for '{artKey}' — drawing a captioned placeholder box in "
                + "its place. The panel still works; the player sees a glyph and a name instead of "
                + "the art.");
        }
    }

    /// <summary>Test-only: forget which art keys have been reported, so a test that asserts the
    /// warning fires is not silenced by an earlier test in the same process having already seen the
    /// same key. Mirrors <see cref="EngineDistress.ResetForTests"/>'s purpose.</summary>
    public static void ResetArtMissWarningsForTests() => ArtMissWarned.Clear();

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
        // UI-1: re-pointed to the WarnColor alias (same EmberColor value) so this switch reads
        // against GameTheme's semantic names, not a raw palette pick.
        ChipTone.Accent => GameTheme.WarnColor,
        ChipTone.Gold => GameTheme.GoldColor,
        _ => GameTheme.BodyTextColor,
    };

    // ── UI-2: cozy list/HUD builders ───────────────────────────────────────────────────────────
    // New, additive builders for the HUD + drawer units (next up): a compact icon+value pill,
    // a fixed-column shop/recipe row, and a drawer's title strip. All read GameTheme tokens only
    // — no local color/size literals — and stay null/fallback-safe like every other builder above.

    /// <summary>Icon size (px) inside an <see cref="IconChip"/>.</summary>
    private const float IconChipIconSize = 18f;

    /// <summary>Icon-to-value gap (px) inside an <see cref="IconChip"/>.</summary>
    private const int IconChipGap = 6;

    /// <summary>
    /// A compact icon+value pill (UI-2) — an 18px icon next to a tone-colored value label, sharing
    /// <see cref="StatChipCompact"/>'s tight (4/2) margins so it drops into the same cramped HUD/
    /// card real estate. Unlike <see cref="StatChip"/>/<see cref="StatChipCompact"/> (label +
    /// value text), this is icon + value — the HUD's "gold coin icon, 42" shape, not "Gold: 42".
    /// <paramref name="icon"/> is null-tolerant: a null texture renders as a blank icon slot
    /// rather than throwing (mirrors <see cref="SimPanel"/>'s <c>AddIcon</c>).
    /// </summary>
    public static Control IconChip(Texture2D? icon, string value, ChipTone tone = ChipTone.Neutral)
    {
        var chip = new PanelContainer { Name = "IconChip" };
        chip.AddThemeStyleboxOverride("panel", CompactChipStyle());

        var row = new HBoxContainer { Name = "IconChipRow" };
        row.AddThemeConstantOverride("separation", IconChipGap);
        chip.AddChild(row);

        var iconRect = new TextureRect
        {
            Name = "Icon",
            Texture = icon,
            CustomMinimumSize = new Vector2(IconChipIconSize, IconChipIconSize),
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        row.AddChild(iconRect);

        var valueNode = new Label { Name = "Value", Text = value };
        valueNode.AddThemeColorOverride("font_color", ToneColor(tone));
        valueNode.AddThemeFontSizeOverride("font_size", GameTheme.LegibilityFloor);
        row.AddChild(valueNode);

        return chip;
    }

    /// <summary>Fixed row height (px) for a <see cref="ListRow"/>.</summary>
    public const float ListRowHeight = 32f;

    private const float ListRowIconSize = 24f;
    private const float ListRowPriceWidth = 64f;
    private const float ListRowOwnedWidth = 40f;
    private const float ListRowActionWidth = 72f;

    /// <summary>Whole-row opacity (UI-2) when <see cref="ListRow"/>'s <c>enabled</c> is false —
    /// dims the entire row (icon/name/price/owned/action) rather than fading one column, so a
    /// disabled row reads as "not available right now" at a glance.</summary>
    private const float ListRowDisabledAlpha = 0.55f;

    /// <summary>
    /// A themed shop/recipe/vendor row (UI-2) — one <see cref="HBoxContainer"/> with fixed
    /// columns (icon 24px | name, Bone, fills remaining width and single-line-ellipsizes rather
    /// than wrapping | price 64px right-aligned Gold | owned "×N" 40px dim | action button 72px)
    /// so a whole list of rows lines up into clean columns instead of each row's own content
    /// dictating its width. A 1px Iron hairline separates rows (a full per-row box reads as one
    /// card per item, which is too heavy for a dense list); hovering swaps the row's own fill to
    /// <see cref="GameTheme.SurfaceRaised"/> — a plain stylebox swap on <c>MouseEntered</c>/
    /// <c>MouseExited</c>, not an engine Tween (this codebase's accumulated-delta-only rule).
    ///
    /// <para>When <paramref name="enabled"/> is false, the whole row dims to
    /// <see cref="ListRowDisabledAlpha"/>, the price tints <see cref="GameTheme.DangerColor"/>,
    /// and <paramref name="action"/> is disabled with <paramref name="whyNot"/> as its tooltip —
    /// the exact <c>SimPanel.GateButton</c> contract (Disabled + player-phrased tooltip),
    /// inlined here since <c>GateButton</c> itself is a <c>SimPanel</c>-protected member this
    /// static kit cannot call directly.</para>
    /// </summary>
    public static Control ListRow(
        Texture2D? icon, string name, string price, string owned, Button action, bool enabled,
        string whyNot = "")
    {
        var row = new PanelContainer
        {
            Name = "ListRow",
            CustomMinimumSize = new Vector2(0, ListRowHeight),
        };

        var normalStyle = ListRowStyle(Colors.Transparent);
        var hoverStyle = ListRowStyle(GameTheme.SurfaceRaised);
        row.AddThemeStyleboxOverride("panel", normalStyle);
        row.MouseEntered += () => row.AddThemeStyleboxOverride("panel", hoverStyle);
        row.MouseExited += () => row.AddThemeStyleboxOverride("panel", normalStyle);

        var hbox = new HBoxContainer { Name = "ListRowContent" };
        hbox.AddThemeConstantOverride("separation", GameTheme.Space8);
        row.AddChild(hbox);

        var iconRect = new TextureRect
        {
            Name = "Icon",
            Texture = icon,
            CustomMinimumSize = new Vector2(ListRowIconSize, ListRowIconSize),
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        hbox.AddChild(iconRect);

        // P2-SCREEN-09: the blocker goes IN the label, not just the tooltip below — a disabled
        // row's own name now carries why, so a screenshot (or a glance with no mouse) reads the
        // same fact the tooltip only ever gave to a hover.
        var nameLabel = new Label
        {
            Name = "Name",
            Text = enabled || string.IsNullOrEmpty(whyNot) ? name : $"{name} — {whyNot}",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            AutowrapMode = TextServer.AutowrapMode.Off,
        };
        nameLabel.AddThemeColorOverride("font_color", GameTheme.BoneColor);
        hbox.AddChild(nameLabel);

        var priceLabel = new Label
        {
            Name = "Price",
            Text = price,
            CustomMinimumSize = new Vector2(ListRowPriceWidth, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        priceLabel.AddThemeColorOverride("font_color", enabled ? GameTheme.GoldColor : GameTheme.DangerColor);
        hbox.AddChild(priceLabel);

        var ownedLabel = new Label
        {
            Name = "Owned",
            Text = $"×{owned}",
            CustomMinimumSize = new Vector2(ListRowOwnedWidth, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        ownedLabel.AddThemeColorOverride("font_color", GameTheme.TextDim);
        hbox.AddChild(ownedLabel);

        action.CustomMinimumSize = new Vector2(ListRowActionWidth, 0);
        action.Disabled = !enabled; // SimPanel.GateButton's exact contract, inlined (see remarks)
        action.TooltipText = enabled ? string.Empty : whyNot;
        hbox.AddChild(action);

        if (!enabled)
        {
            row.Modulate = new Color(1f, 1f, 1f, ListRowDisabledAlpha);
        }

        return row;
    }

    /// <summary>A hairline-bottomed row background: <paramref name="bg"/> fill (Transparent for
    /// the resting state, <see cref="GameTheme.SurfaceRaised"/> on hover) plus a fixed 1px Iron
    /// bottom border — never a full box, so a list of rows reads as one continuous list rather
    /// than a stack of separate cards.</summary>
    private static StyleBoxFlat ListRowStyle(Color bg) => new()
    {
        BgColor = bg,
        BorderWidthBottom = 1,
        BorderColor = GameTheme.IronColor,
        ContentMarginLeft = GameTheme.Space8,
        ContentMarginRight = GameTheme.Space8,
        ContentMarginTop = GameTheme.Space4,
        ContentMarginBottom = GameTheme.Space4,
    };

    /// <summary>
    /// Strip height (px) for a <see cref="DrawerHeader"/>.
    ///
    /// <para>Was 40, which is too short for a <see cref="GameTheme.HeaderFontSize"/> display face: the
    /// title's descenders overran the strip and bled into the content slot below it. That overhang was
    /// invisible against an empty dark panel, so instead of being fixed it was worked around per-child —
    /// <c>SceneBanner</c> carried a hand-tuned 14px top inset for exactly this reason. Anything that did
    /// NOT know to compensate simply covered the title: Brian's playtest found the forge's Anvil Map
    /// overlay drawn across the "FORGE" heading and its own close button ("Forge menus don't fit screen
    /// correctly").</para>
    ///
    /// <para>56 clears the face outright, so overlays, banners, and anything added later are clear of the
    /// title by construction rather than by each remembering to dodge it. Fixing the strip and deleting
    /// the compensating inset is one change in one place instead of a growing set of magic numbers that
    /// each drift independently.</para>
    /// </summary>
    public const float DrawerHeaderHeight = 56f;

    /// <summary>On-screen height (px) of a <see cref="SceneBanner"/> — the art is authored at half
    /// this so it lands on a crisp 2x pixel scale.</summary>
    public const float SceneBannerHeight = 140f;

    /// <summary>Top inset (px) for a <see cref="SceneBanner"/>. Now 0: it existed solely to dodge the
    /// drawer title's descenders, and <see cref="DrawerHeaderHeight"/> no longer lets them into the
    /// content slot. Kept as a named dial rather than deleted so a future font swap has one obvious
    /// place to compensate, instead of a bare literal reappearing inside the builder.</summary>
    public const float SceneBannerTopInset = 0f;

    /// <summary>
    /// A painted interior banner for a drawer panel (e.g. the shop's shelves, the tavern's bar):
    /// a nearest-filtered, width-filling strip of generated pixel art that gives an otherwise
    /// list-only panel a sense of place. Null-tolerant like every other art-taking builder here —
    /// an unresolvable id yields <c>null</c> and the caller simply mounts nothing, so a fresh or
    /// headless checkout renders the panel exactly as it did before the art existed.
    /// </summary>
    public static Control? SceneBanner(string artId)
    {
        var art = IconRegistry.Art(artId);
        if (art is null)
        {
            return null;
        }

        var rect = new TextureRect
        {
            Name = $"SceneBanner_{artId}",
            Texture = art,
            CustomMinimumSize = new Vector2(0, SceneBannerHeight),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest, // crisp pixels, never smeared
            MouseFilter = Control.MouseFilterEnum.Ignore,         // decoration must never eat clicks
        };

        // The host stays even at a zero inset: it is what keeps the banner's sizing independent of the
        // panel's own content margins, and it is where a future font compensation would go.
        var host = new MarginContainer { Name = $"SceneBannerHost_{artId}", MouseFilter = Control.MouseFilterEnum.Ignore };
        host.AddThemeConstantOverride("margin_top", (int)SceneBannerTopInset);
        host.AddChild(rect);
        return host;
    }

    private const float DrawerHeaderIconSize = 24f;
    private const float DrawerHeaderCloseSize = 24f;

    /// <summary>
    /// A drawer's title strip (UI-2): a 24px icon, the title in the display/header theme-type
    /// variation (see <see cref="GameTheme.HeaderThemeType"/>), a flexible spacer, and a 24px
    /// "✕" close button wired to <paramref name="onClose"/>. <paramref name="icon"/> is
    /// null-tolerant (blank slot, not a throw) like every other icon-taking builder here.
    /// </summary>
    public static Control DrawerHeader(string title, Texture2D? icon, Action onClose)
    {
        var strip = new PanelContainer
        {
            Name = "DrawerHeader",
            CustomMinimumSize = new Vector2(0, DrawerHeaderHeight),
        };

        var row = new HBoxContainer { Name = "DrawerHeaderRow" };
        row.AddThemeConstantOverride("separation", GameTheme.Space8);
        strip.AddChild(row);

        var iconRect = new TextureRect
        {
            Name = "Icon",
            Texture = icon,
            CustomMinimumSize = new Vector2(DrawerHeaderIconSize, DrawerHeaderIconSize),
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        row.AddChild(iconRect);

        var titleLabel = new Label
        {
            Name = "Title",
            Text = title,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        titleLabel.AddThemeColorOverride("font_color", GameTheme.HeaderColor);
        titleLabel.AddThemeFontSizeOverride("font_size", GameTheme.HeaderFontSize);
        titleLabel.ThemeTypeVariation = GameTheme.HeaderThemeType;
        row.AddChild(titleLabel);

        var closeButton = new Button
        {
            Name = "Close",
            Text = "✕",
            CustomMinimumSize = new Vector2(DrawerHeaderCloseSize, DrawerHeaderCloseSize),
        };
        closeButton.Pressed += () => onClose();
        row.AddChild(closeButton);

        return strip;
    }
}
