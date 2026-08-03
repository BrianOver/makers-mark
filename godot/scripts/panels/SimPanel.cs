using System;
using GameSim.Contracts;
using GameSim.Kernel;
using Godot;
using GodotClient.Ui;

namespace GodotClient.Panels;

/// <summary>
/// Base for the U11 management panels (KTD10 — these ARE the real UI skeleton).
/// A panel binds the ONE <see cref="SimAdapter"/> (KTD2), renders
/// <c>Adapter.CurrentState</c>, and queues <see cref="PlayerAction"/>s from its
/// buttons. Adapter-only: no game rules in any panel. Content is rebuilt
/// synchronously on <see cref="Refresh"/> so tests can assert rendered text
/// immediately after a tick.
///
/// <para>P007 U2 (KTD2): the themed widget kit (<see cref="UiKit"/>) is exposed below as
/// protected passthroughs alongside the original <see cref="AddLabel"/>/<see cref="AddHeader"/>/
/// <see cref="AddButton"/>/<see cref="AddRow"/>/<see cref="AddIcon"/> — those keep their exact
/// behavior (still test-load-bearing) while screens rebuilt on the kit compose
/// <see cref="Card"/>/<see cref="Section"/>/<see cref="StatChip"/>/<see cref="PortraitFrame"/>/
/// <see cref="ArtRect"/> instead of bare rows. No panel is required to switch — the "placeholder
/// look by design" era is over, but the lifecycle (Bind/Refresh/Clear) and the HeroName/ItemName
/// lookups are unchanged.</para>
/// </summary>
public abstract partial class SimPanel : Control
{
    protected SimAdapter? Adapter { get; private set; }

    public void Bind(SimAdapter adapter)
    {
        Adapter = adapter;
        Refresh();
    }

    /// <summary>Rebuild rendered content from <c>Adapter.CurrentState</c>.</summary>
    public abstract void Refresh();

    /// <summary>
    /// Detach children immediately, destroy them deferred.
    ///
    /// <para><b>This used to call <c>Free()</c> and it crashed the game.</b> The old doc claimed the
    /// invariant "only ever called from Refresh — never from a signal handler of a node being
    /// cleared." That invariant was false on the game's most common action. Pressing Craft runs
    /// <c>OnCraftPressed</c> → <c>SimAdapter.Queue</c>, and Queue ticks the sim SYNCHRONOUSLY, which
    /// fires <c>MainUi.OnPhaseCompleted</c> → <c>RefreshAll</c> → this panel's <c>Refresh</c> → here
    /// — all still inside the pressed-signal emission of the very button being freed. Godot warned
    /// ("Object was freed or unreferenced while a signal is being emitted from it") and then the
    /// process died with signal 11. The owner hit it by clicking auto-craft; the same stack exists in
    /// <c>ShopPanel.PlaceOnShelf</c>.</para>
    ///
    /// <para><c>RemoveChild</c> stays immediate, so a rebuild still starts from an empty parent and
    /// leaves no stale rows — that was the real reason the original avoided <c>QueueFree</c>. Only
    /// the destruction moves to the end of the frame, by which time no signal is in flight. Fixing it
    /// here rather than at the call sites means every panel inherits it and no future handler has to
    /// remember an invariant that was already being violated.</para>
    /// </summary>
    protected static void Clear(Node parent)
    {
        foreach (var child in parent.GetChildren())
        {
            parent.RemoveChild(child);
            child.QueueFree();
        }
    }

    protected static Label AddLabel(Node parent, string text)
    {
        // ExpandFill (U7/R7): an autowrap label's minimum width is ~1px, so inside an HBox row
        // (which hands non-expand children their minimum) it collapses to one character per
        // line. Expanding claims the row's leftover width; in a VBox it is a no-op (cross-axis
        // already fills).
        var label = new Label
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        parent.AddChild(label);
        return label;
    }

    protected static Label AddHeader(Node parent, string text)
    {
        var label = AddLabel(parent, text);
        label.AddThemeColorOverride("font_color", GameTheme.HeaderColor);
        // P007 polish: opt this header into the display-font theme-type variation (never the
        // base "Label" type) — see GameTheme's HeaderFont remarks.
        label.ThemeTypeVariation = GameTheme.HeaderThemeType;
        return label;
    }

    protected static Button AddButton(Node parent, string name, string text, Action onPressed)
    {
        var button = new Button { Name = name, Text = text };
        button.Pressed += onPressed;
        parent.AddChild(button);
        return button;
    }

    /// <summary>
    /// U6 (R6) prevention half: reflect the kernel's own legality verdict on a button —
    /// Disabled with a player-phrased tooltip when the queued action would provably be
    /// refused. MIRROR, never replace: <paramref name="legal"/> must be read off the same
    /// sim-exposed facts/predicates the action's handler checks, and the kernel remains
    /// the authority on apply — a stale enable is still honestly rejected (and toasted
    /// by MainUi), never silently dropped.
    /// </summary>
    protected static Button GateButton(Button button, bool legal, string whyNot)
    {
        button.Disabled = !legal;
        button.TooltipText = legal ? string.Empty : whyNot;
        return button;
    }

    protected static SpinBox AddSpinBox(Node parent, string name, double min, double max, double value)
    {
        var spin = new SpinBox { Name = name, MinValue = min, MaxValue = max, Rounded = true, Value = value };
        parent.AddChild(spin);
        return spin;
    }

    /// <summary>
    /// The confirmation line for an action the player just took — derives whether it HAPPENED
    /// already or is still waiting for the bell from the ONE source of truth,
    /// <see cref="ActionTiming.ResolvesImmediately"/>, instead of a panel hardcoding its own
    /// sentence at every call site.
    ///
    /// <para><b>Why this exists.</b> The 2026-08-02 loop-legibility widening (see
    /// <see cref="ActionTiming"/>'s own remarks) moved 21 of 24 action types to resolve the
    /// instant the player takes them — including opening/closing the counter, presenting,
    /// suggesting, haggling, crafting, buying, and unlocking a talent. <see cref="CounterPanel"/>
    /// and <see cref="ForgePanel"/> kept printing the OLD deferred sentence regardless of which
    /// branch the action actually took: "Queued — resolves when Morning ticks. Press Advance or
    /// wait." for an action that had already happened. Brian's playtest: "Open counter does
    /// nothing - tutorial stuck at 6", "opening the counter queues", "you have a TON of past
    /// 'queued' actions which don't interact with our game well lol". The counter really did
    /// open; the SENTENCE lied about it, so the player pressed Advance believing nothing had
    /// happened and burned the phase for nothing. The defect was never the ~14 individual
    /// strings — it was that each one was hand-written instead of read off
    /// <see cref="ActionTiming"/>, so the words and the kernel's own timing could drift apart,
    /// and did.</para>
    ///
    /// <para>Immediate says <paramref name="whatHappened"/> HAPPENED — past tense, no instruction
    /// to advance, because the player's own hands already did it. Deferred keeps the EXACT
    /// reviewed wording ("Queued — resolves when ... ticks. Press Advance or wait.") — that
    /// promise is still true for the three genuine bell-riders (a forge upgrade, a profession
    /// change, a Guild commission): the world has to act before the click means anything, so it
    /// is right to say so.</para>
    /// </summary>
    protected string Confirm(PlayerAction action, string whatHappened) =>
        ActionTiming.ResolvesImmediately(action)
            ? $"{whatHappened}."
            : $"{whatHappened}. Queued — resolves when {Adapter?.CurrentState.Phase} ticks. Press Advance or wait.";

    /// <summary>
    /// Report the space this panel's content actually needs, so a panel nested inside a
    /// <see cref="Container"/> reserves room for itself.
    ///
    /// <para><b>Why this override is load-bearing.</b> <see cref="SimPanel"/> derives from
    /// <see cref="Control"/>, not <see cref="Container"/>, and a plain Control does not derive a minimum
    /// size from its children — only Containers do. So nesting one in a <see cref="VBoxContainer"/> gave it
    /// ZERO height: it reserved no space, its own full-rect-anchored content overflowed that empty box, and
    /// the next sibling in the VBox was laid out directly on top of it.</para>
    ///
    /// <para>That is not a cosmetic overlap. It is what made the Shop's "Open Counter" button unclickable —
    /// <c>ShopPanel</c> nests <c>CounterPanel</c> above its shelf sections, so the shelf drop-zones were
    /// drawn over the button and swallowed every click. Found by the human-playtest harness, which reported
    /// the exact blocker (<c>DropZone 'EmptyShelfSlot_1'</c>); every property-based test passed throughout,
    /// because each control's own properties were perfectly correct.</para>
    ///
    /// <para>Callers that rebuild content must follow with <see cref="Control.UpdateMinimumSize"/> — a plain
    /// Control does not get told when a child's minimum size changes, so without that nudge the reserved
    /// height stays whatever the first build asked for.</para>
    /// </summary>
    public override Vector2 _GetMinimumSize()
    {
        var minimum = Vector2.Zero;
        foreach (var child in GetChildren())
        {
            // Skip hidden children: an overlay parked invisible (ProvenanceCard, the drawer's own
            // registered-but-closed panels) must not reserve space it is not using.
            if (child is Control { Visible: true } control)
            {
                minimum = minimum.Max(control.GetCombinedMinimumSize());
            }
        }

        return minimum;
    }

    /// <summary>A fitted modal card: <paramref name="Body"/> for the content, <paramref name="ActionRow"/> for
    /// the controls that must never leave the screen.</summary>
    protected readonly record struct ModalCard(VBoxContainer Body, Control ActionRow);

    /// <summary>Inset (px) from each window edge for a fitted modal card.</summary>
    private const float ModalMargin = 64f;

    /// <summary>Height (px) reserved for a fitted modal's bottom action row.</summary>
    private const float ModalActionRowHeight = 40f;

    /// <summary>
    /// Build a modal card that CANNOT outgrow the window, with its dismiss controls anchored to the bottom.
    ///
    /// <para><b>Why this exists as a helper.</b> Two modals independently shipped the same softlock — the
    /// Scrying Mirror and the Camp slate both used a <see cref="CenterContainer"/> around a
    /// <c>VBoxContainer</c> with a <c>CustomMinimumSize</c>, and both put their close button at the end of that
    /// box. A CenterContainer sizes its child to the child's combined MINIMUM and centres it, and a Control can
    /// never lay out smaller than its minimum however its parent is anchored — so as the content grew (feed
    /// beats, camped parties) the card grew past the window and carried the close button off screen. Measured:
    /// the Mirror's Close at y=832 and the Camp slate 1027px tall, both in a 648px window. Neither could be
    /// dismissed, and the Camp slate <b>opens itself</b> every Camp phase.</para>
    ///
    /// <para>The fix is structural, not a size tweak: the card is anchored to the WINDOW (so its size is a
    /// function of the window, not of its contents) and the action row is anchored to the card's bottom edge (so
    /// its position is a function of that edge, not of flow). Content goes in <see cref="ModalCard.Body"/> and
    /// overflows into its own scroller; dismiss controls go in <see cref="ModalCard.ActionRow"/> and stay put.
    /// Flow layout cannot make that promise while any descendant can claim height.</para>
    /// </summary>
    protected ModalCard BuildFittedModalCard(string cardName)
    {
        var panel = new PanelContainer { Name = cardName };
        panel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        panel.OffsetLeft = ModalMargin;
        panel.OffsetTop = ModalMargin;
        panel.OffsetRight = -ModalMargin;
        panel.OffsetBottom = -ModalMargin;
        AddChild(panel);

        // A plain Control host, so the parts below are positioned by ANCHORS rather than by flow.
        var host = new Control { Name = $"{cardName}Host" };
        host.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        panel.AddChild(host);

        var body = new VBoxContainer { Name = $"{cardName}Body" };
        body.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        body.OffsetBottom = -ModalActionRowHeight;
        host.AddChild(body);

        var actionRow = new HBoxContainer { Name = $"{cardName}Actions" };
        actionRow.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
        actionRow.OffsetTop = -ModalActionRowHeight;
        host.AddChild(actionRow);

        return new ModalCard(body, actionRow);
    }

    protected static HBoxContainer AddRow(Node parent)
    {
        var row = new HBoxContainer();
        parent.AddChild(row);
        return row;
    }

    /// <summary>
    /// A row that WRAPS onto further lines instead of growing wider — for any row whose child count is
    /// driven by game state rather than fixed by the layout.
    ///
    /// <para><b>Use this, not <see cref="AddRow"/>, whenever the children come from a loop.</b> An
    /// <see cref="HBoxContainer"/>'s minimum width is the sum of its children, and a Control can never lay
    /// out narrower than its minimum — so a row of one chip per mine floor quietly forces its whole panel
    /// past the drawer's right edge as floors unlock, with no scroller able to reach it (horizontal
    /// scrolling is deliberately disabled; see <see cref="BuildScrollBody"/>). That is what cut off the
    /// Demand panel's bounty board, and it is a time bomb rather than a typo: the layout is correct at
    /// three floors and broken at six.</para>
    /// </summary>
    protected static HFlowContainer AddWrappingRow(Node parent)
    {
        var row = new HFlowContainer();
        parent.AddChild(row);
        return row;
    }

    /// <summary>
    /// Add a small themed icon (U16) next to text. Decoration only — clicks pass
    /// through, and a null texture yields a blank spacer so callers need no guard.
    /// </summary>
    protected static TextureRect AddIcon(Node parent, Texture2D? texture, int size = 22)
    {
        var rect = new TextureRect
        {
            Texture = texture,
            CustomMinimumSize = new Vector2(size, size),
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        parent.AddChild(rect);
        return rect;
    }

    /// <summary>Full-rect ScrollContainer wrapping a VBox — the standard panel body.</summary>
    protected VBoxContainer BuildScrollBody()
    {
        // Horizontal scroll disabled (U7/R7): with it enabled the child gets unbounded
        // horizontal space, so autowrap labels lose their real wrap width. Vertical-only.
        var scroll = new ScrollContainer
        {
            Name = "Scroll",
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        scroll.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(scroll);
        var body = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        scroll.AddChild(body);
        return body;
    }

    protected string HeroName(HeroId id) =>
        Adapter is not null && Adapter.CurrentState.Heroes.TryGetValue(id.Value, out var hero)
            ? hero.Name
            : id.ToString();

    protected string ItemName(ItemId id) =>
        Adapter is not null && Adapter.CurrentState.Items.TryGetValue(id.Value, out var item)
            ? item.Name
            : id.ToString();

    // ── P007 U2: themed widget kit passthroughs (GodotClient.Ui.UiKit) ───────────────────────

    /// <summary>A plain themed card container — see <see cref="UiKit.Card"/>.</summary>
    protected static PanelContainer Card(string? name = null) => UiKit.Card(name);

    /// <summary>A titled section (header + body VBox) — see <see cref="UiKit.Section"/>.</summary>
    protected static UiKit.SectionView Section(string title) => UiKit.Section(title);

    /// <summary>A small themed label/value pill — see <see cref="UiKit.StatChip"/>.</summary>
    protected static Control StatChip(string label, string value, UiKit.ChipTone tone = UiKit.ChipTone.Neutral) =>
        UiKit.StatChip(label, value, tone);

    /// <summary>A bordered hero-portrait frame — see <see cref="UiKit.PortraitFrame"/>.</summary>
    protected static Control PortraitFrame(
        string artKey, float size = UiKit.PortraitSize, Texture2D? fallbackIcon = null, string? caption = null,
        bool ellipsizeCaption = false) =>
        UiKit.PortraitFrame(artKey, size, fallbackIcon, caption, ellipsizeCaption);

    /// <summary>The fallback-safe art-loader bridge — see <see cref="UiKit.ArtRect"/>.</summary>
    protected static Control ArtRect(
        string artKey, Vector2 size, Texture2D? fallbackIcon = null, string? caption = null) =>
        UiKit.ArtRect(artKey, size, fallbackIcon, caption);

    // ── UI-2: cozy list/HUD builder passthroughs ──────────────────────────────────────────────

    /// <summary>A compact icon+value pill — see <see cref="UiKit.IconChip"/>.</summary>
    protected static Control IconChip(Texture2D? icon, string value, UiKit.ChipTone tone = UiKit.ChipTone.Neutral) =>
        UiKit.IconChip(icon, value, tone);

    /// <summary>A themed shop/recipe/vendor row — see <see cref="UiKit.ListRow"/>.</summary>
    protected static Control ListRow(
        Texture2D? icon, string name, string price, string owned, Button action, bool enabled,
        string whyNot = "") =>
        UiKit.ListRow(icon, name, price, owned, action, enabled, whyNot);

    /// <summary>A drawer's title strip — see <see cref="UiKit.DrawerHeader"/>.</summary>
    protected static Control DrawerHeader(string title, Texture2D? icon, Action onClose) =>
        UiKit.DrawerHeader(title, icon, onClose);
}
