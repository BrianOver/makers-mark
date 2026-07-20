using System;
using System.Collections.Generic;
using Godot;

namespace GodotClient.Ui;

/// <summary>
/// U21: the right-anchored ~600px slide-in panel host that replaces the old <see
/// cref="TabContainer"/> tab shell — the world (<c>TownScene</c>) is now a PERMANENT base child of
/// <c>MainUi</c>, always visible, and the six management panels (Forge/Shop/Heroes/Tavern/Depths/
/// Bounties) live here instead, one at a time, sliding over the world rather than replacing it.
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
/// TabFade/gold-chip-pop precedent). A linear 0→1 ramp drives the panel's X position between fully
/// off the right edge and its resting spot <see cref="DrawerWidth"/> px in from the right.</para>
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
    private Control _slot = null!;
    private readonly Dictionary<string, Control> _registered = [];
    private Control? _current;
    private double _slideElapsed = -1; // -1 idle; >=0 while a slide is in flight
    private bool _opening;

    /// <summary>The id of the currently showing panel, or null when the drawer is closed (bare
    /// world) — REPLACE semantics: never more than one at a time, so this is sufficient state,
    /// no stack.</summary>
    public string? CurrentPanelId { get; private set; }

    /// <summary>True while any panel is showing (mid-slide or fully open).</summary>
    public bool IsOpen => CurrentPanelId is not null;

    /// <summary>The dim-under veil — also the click-out catcher (for tests).</summary>
    public ColorRect Veil => _dim;

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
        AddChild(_panel);

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
        ApplySlide(0f);
    }

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

    /// <summary>Position the sliding panel (background card + content slot, moved as one unit) for
    /// slide-progress <paramref name="t"/> (0 = fully off-screen right, 1 = resting at <see
    /// cref="DrawerWidth"/> px from the right edge) — a linear accumulated-delta ramp, direction
    /// set by <see cref="_opening"/>.</summary>
    private void ApplySlide(float t)
    {
        var hostWidth = Size.X;
        var size = new Vector2(DrawerWidth, Size.Y);
        var restX = hostWidth - DrawerWidth;
        var offstageX = hostWidth;
        var x = _opening ? Mathf.Lerp(offstageX, restX, t) : Mathf.Lerp(restX, offstageX, t);
        var position = new Vector2(x, 0f);

        _panel.Position = position;
        _panel.Size = size;
        _slot.Position = position;
        _slot.Size = size;
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
