using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Godot;
using GodotClient;

namespace GodotClient.Ui;

/// <summary>
/// U21: the right-anchored ~600px slide-in panel host that replaces the old <see
/// cref="TabContainer"/> tab shell — the world (<c>Town3D</c> at T8, replaced by <c>Town2D</c> in
/// the 2.5D pivot) is now a PERMANENT base child of <c>MainUi</c>, always visible, and the
/// management panels (Forge/Shop/Heroes/Tavern/Depths/
/// Bounties/Demand) live here instead, one at a time, sliding over the world rather than replacing it.
///
/// <para><b>Registration:</b> every panel <see cref="Register"/>s once at boot (parent-agnostic —
/// each panel's own <c>Bind</c>/<c>Refresh</c> lifecycle is unchanged, KTD2). <see cref="Open"/>
/// shows the matching registered Control and hides whatever was previously showing — REPLACE, not
/// a stack (<see cref="CurrentPanelId"/> only ever names one panel). <see cref="Close"/> returns to
/// the bare world.</para>
///
/// <para><b>Dim-under</b> (LedgerModal precedent — <see cref="ColorRect"/> full-rect translucent
/// black) sits behind the sliding panel and doubles as the click-out catcher: its default <see
/// cref="Control.MouseFilterEnum.Stop"/> filter absorbs the click before it can reach any world
/// Area2D picking underneath (Godot skips 2D physics picking once GUI input already consumed the
/// event) — the "consume" contract is structural, not an extra call. <see cref="_Input"/> handles
/// Esc the same way, explicitly marking the event handled so it does not also propagate as a
/// world-side cancel.</para>
///
/// <para><b>Slide:</b> accumulated-delta only (<see cref="Tick"/>, called from
/// <c>MainUi._Process</c>) — no engine <see cref="Tween"/> anywhere in this codebase (the
/// TabFade/gold-chip-pop precedent). A cubic ease-out 0→1 ramp (still a plain per-frame lerp, never
/// a Tween) drives the panel's X position between fully off the right edge and its resting spot
/// <see cref="DrawerWidth"/> px in from the right.</para>
///
/// <para><b>Header (UI-5):</b> a <see cref="UiKit.DrawerHeader"/> strip — title humanized from the
/// panel's registered id (e.g. "HeroCards" → "Hero Cards"), a best-effort <see
/// cref="IconRegistry.Glyph"/> icon, and a ✕ close button wired to <see cref="Close"/> — sits above
/// the content slot, rebuilt fresh on every <see cref="Open"/> (the title/icon depend on which
/// panel just opened). This is the drawer's ONLY discoverable close affordance besides Esc/
/// click-out.</para>
/// </summary>
public partial class DrawerHost : Control
{
    /// <summary>The drawer's fixed width (plan: ~600px).</summary>
    public const float DrawerWidth = 600f;

    /// <summary>Slide-in/out duration — accumulated-delta ramp, no engine Tween (see class doc).</summary>
    public const double SlideSeconds = 0.22;

    private const float DimAlpha = 0.55f;

    private ColorRect _dim = null!;
    private PanelContainer _panel = null!;
    private Control _headerHost = null!;
    private Control _slot = null!;
    private readonly Dictionary<string, Control> _registered = [];
    private Control? _current;
    private double _slideElapsed = -1; // -1 idle; >=0 while a slide is in flight
    private bool _opening;

    /// <summary>Best-effort <see cref="IconRegistry.Glyph"/> name per registered panel id (UI-5) —
    /// only the ids with a real hand-authored HUD glyph get one; every other id falls back to
    /// <see cref="DefaultHeaderGlyph"/> rather than probing the resource filesystem for a
    /// same-named SVG that was never authored (avoids spurious missing-resource log noise on
    /// every drawer open).</summary>
    private static readonly Dictionary<string, string> HeaderGlyphByPanelId = new()
    {
        ["Bounties"] = "bounty",
        ["Depths"] = "depths",
        ["Tavern"] = "gossip",
    };

    /// <summary>Generic default header glyph for a panel id with no dedicated icon — mirrors
    /// <see cref="UiKit"/>'s own "rune" default-fallback convention (<c>ArtRect</c>'s
    /// DefaultFallbackGlyph).</summary>
    private const string DefaultHeaderGlyph = "rune";

    /// <summary>PascalCase → spaced Title Case ("HeroCards" → "Hero Cards") for a drawer header's
    /// title, read straight off the panel's registered id (no separate title table to keep in
    /// sync).</summary>
    private static readonly Regex PascalWordSplit = new("(?<!^)([A-Z])", RegexOptions.Compiled);

    /// <summary>The id of the currently showing panel, or null when the drawer is closed (bare
    /// world) — REPLACE semantics: never more than one at a time, so this is sufficient state,
    /// no stack.</summary>
    public string? CurrentPanelId { get; private set; }

    /// <summary>True while any panel is showing (mid-slide or fully open).</summary>
    public bool IsOpen => CurrentPanelId is not null;

    /// <summary>
    /// The panel content currently showing, or null when closed.
    ///
    /// <para>The companion to <see cref="CurrentPanelId"/> for anything that needs the panel's live
    /// GEOMETRY rather than its identity. The host itself is a full-rect Control that never moves, so
    /// watching the host to detect "has the drawer finished opening" always says yes immediately — this is
    /// the node that actually slides. The human-playtest harness measures panel layout and read every
    /// panel as off-screen until it waited on this instead.</para>
    /// </summary>
    public Control? CurrentContent => _current;

    /// <summary>The dim-under veil — also the click-out catcher (for tests).</summary>
    public ColorRect Veil => _dim;

    /// <summary>
    /// Every registered panel id, in registration order.
    ///
    /// <para>Exists so a coverage guard can assert that the human-playtest sweep
    /// (<c>HumanPlaytestTests</c>) visits ALL of them. Without it, adding a tenth panel would silently
    /// escape the fits-on-screen and real-click checks while the suite stayed green — the same
    /// declare-it-then-forget-to-wire-it shape that has already shipped an entire dormant ground-tile
    /// system and four invisible panel banners on this project.</para>
    /// </summary>
    public IReadOnlyCollection<string> RegisteredIds => _registered.Keys;

    /// <summary>Raised whenever the drawer closes (click-out, Esc, or an explicit <see
    /// cref="Close"/>) — MainUi uses this to keep the Engaged latch in sync.</summary>
    public event Action? Closed;

    /// <summary>Build the host chrome (dim + sliding panel + content slot). Idempotent-guarded like
    /// every other code-built node on this project.</summary>
    public void Build()
    {
        if (_panel is not null)
        {
            return;
        }

        Name = "DrawerHost";
        // NB: SetAnchorsPreset alone (default MinSize resize mode) PRESERVES the control's current
        // rect (position AND size) and only changes which anchors govern it — on a freshly
        // constructed Control (Size == Vector2.Zero) that pins it to a degenerate zero-size rect,
        // never actually filling the parent. SetAnchorsAndOffsetsPreset forces the offsets too, so
        // it actually resizes to the preset's rect regardless of whatever the Size was before.
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore; // the outer host never blocks by itself — only Dim does
        Visible = false; // hidden (and thus input-inert) until the first Open

        _dim = new ColorRect { Name = "DrawerDim", Color = new Color(0f, 0f, 0f, DimAlpha) };
        _dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _dim.GuiInput += OnDimGuiInput;
        AddChild(_dim);

        // _panel (a themed PanelContainer, for the drawer's card background) and _slot (the plain
        // Control that actually parents the registered panels) are SIBLINGS, not parent/child —
        // a PanelContainer subtracts its stylebox's content margins from whatever it hands its
        // OWN child, and that adjustment is deferred (Container queue_sort semantics), which raced
        // a test's direct `panel.Size = ...` override and corrupted it. Keeping _slot as a sibling
        // (positioned identically to _panel by ApplySlide, just without going through a Container)
        // makes its rect fully synchronous and deterministic — no deferred-margin hazard.
        _panel = new PanelContainer { Name = "DrawerPanel" };
        _panel.AddThemeStyleboxOverride("panel", GameTheme.PanelStyleWood());
        AddChild(_panel);

        // UI-5: a persistent host for the per-open header strip — see RebuildHeader. A sibling of
        // _slot (not its Container-managed child) for the same synchronous-rect reason _panel/
        // _slot are siblings rather than parent/child (see the class doc's Build note above).
        _headerHost = new Control { Name = "DrawerHeaderHost" };
        AddChild(_headerHost);

        _slot = new Control { Name = "DrawerSlot" };
        AddChild(_slot);
    }

    /// <summary>Register one panel Control under a stable id — called once per panel at boot
    /// (MainUi.BuildUi). Parent-agnostic: the panel's own Bind/Refresh lifecycle never changes.</summary>
    public void Register(string id, Control content)
    {
        content.Visible = false;
        content.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); // force-fill — see Build()'s note
        _slot.AddChild(content);
        _registered[id] = content;
    }

    /// <summary>Open (or replace) the drawer with the panel registered under <paramref
    /// name="id"/>. While another panel is already showing, this REPLACES it — close-then-open,
    /// never a stack.</summary>
    public void Open(string id)
    {
        if (!_registered.TryGetValue(id, out var content))
        {
            throw new ArgumentOutOfRangeException(nameof(id), id, "no such drawer panel registered");
        }

        if (_current is not null && _current != content)
        {
            _current.Visible = false;
        }

        content.Visible = true;
        _current = content;
        CurrentPanelId = id;
        Visible = true;
        _opening = true;
        _slideElapsed = 0;
        RebuildHeader(id);
        ApplySlide(0f);
    }

    /// <summary>Rebuild the header strip for the panel that just opened (UI-5) — cheap (one small
    /// Control tree) and simplest correct option since <see cref="UiKit.DrawerHeader"/> bakes its
    /// title/icon in at construction rather than exposing setters.</summary>
    private void RebuildHeader(string id)
    {
        foreach (var child in _headerHost.GetChildren())
        {
            _headerHost.RemoveChild(child);
            child.Free();
        }

        var glyphName = HeaderGlyphByPanelId.TryGetValue(id, out var mapped) ? mapped : DefaultHeaderGlyph;
        var header = UiKit.DrawerHeader(HumanizePanelId(id), IconRegistry.Glyph(glyphName), Close);
        header.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _headerHost.AddChild(header);
    }

    private static string HumanizePanelId(string id) => PascalWordSplit.Replace(id, " $1");

    /// <summary>Close the drawer (click-out, Esc, or an explicit call) — slides back out, then
    /// hides the host and its content once the slide settles (see <see cref="Tick"/>).</summary>
    public void Close()
    {
        if (CurrentPanelId is null)
        {
            return;
        }

        CurrentPanelId = null;
        _opening = false;
        _slideElapsed = 0;
        ApplySlide(0f);
        Closed?.Invoke();
    }

    /// <summary>Advance the slide by one frame's delta — called from <c>MainUi._Process</c>, the
    /// same place TabFade/the gold-chip pop tick (no engine Tween in this codebase). No-op unless a
    /// slide is in flight.</summary>
    public void Tick(double delta)
    {
        if (_slideElapsed < 0)
        {
            return;
        }

        _slideElapsed += delta;
        var t = Mathf.Clamp((float)(_slideElapsed / SlideSeconds), 0f, 1f);
        ApplySlide(t);

        if (t < 1f)
        {
            return;
        }

        _slideElapsed = -1;
        if (_opening)
        {
            return;
        }

        // Closing slide settled fully off-screen: hide the host and drop the content reference —
        // the panel itself stays registered (and bound/alive) for the next Open.
        Visible = false;
        if (_current is not null)
        {
            _current.Visible = false;
            _current = null;
        }
    }

    /// <summary>Position the sliding panel (background card + header strip + content slot, moved
    /// as one unit) for slide-progress <paramref name="t"/> (0 = fully off-screen right, 1 =
    /// resting at <see cref="DrawerWidth"/> px from the right edge) — a cubic ease-out
    /// accumulated-delta ramp (still a plain per-frame lerp, never an engine <see cref="Tween"/>),
    /// direction set by <see cref="_opening"/>. The header strip claims the top <see
    /// cref="UiKit.DrawerHeaderHeight"/> px; the content slot fills the rest.</summary>
    private void ApplySlide(float t)
    {
        var hostWidth = Size.X;
        var size = new Vector2(DrawerWidth, Size.Y);
        var restX = hostWidth - DrawerWidth;
        var offstageX = hostWidth;
        var eased = 1f - Mathf.Pow(1f - t, 3f); // cubic ease-out — see class doc's Slide remarks
        var x = _opening ? Mathf.Lerp(offstageX, restX, eased) : Mathf.Lerp(restX, offstageX, eased);
        var position = new Vector2(x, 0f);

        _panel.Position = position;
        _panel.Size = size;

        _headerHost.Position = position;
        _headerHost.Size = new Vector2(DrawerWidth, UiKit.DrawerHeaderHeight);

        var contentPosition = position + new Vector2(0f, UiKit.DrawerHeaderHeight);
        var contentSize = new Vector2(DrawerWidth, Mathf.Max(0f, Size.Y - UiKit.DrawerHeaderHeight));
        _slot.Position = contentPosition;
        _slot.Size = contentSize;
    }

    /// <summary>
    /// Click-out: the dim veil's default <see cref="Control.MouseFilterEnum.Stop"/> filter already
    /// blocks the click from reaching anything underneath (Godot skips 2D physics/Area2D picking
    /// once GUI input consumes the event) — this handler only adds the close-on-click behavior on
    /// top of that structural consumption.
    /// </summary>
    private void OnDimGuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
        {
            Close();
        }
    }

    /// <summary>Esc closes the drawer and marks the event handled so it does not also reach a
    /// world-side cancel handler this same frame.</summary>
    public override void _Input(InputEvent @event)
    {
        if (!IsOpen)
        {
            return;
        }

        if (@event is InputEventKey { PhysicalKeycode: Key.Escape, Pressed: true })
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }
}
