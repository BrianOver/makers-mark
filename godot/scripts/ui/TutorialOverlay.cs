using System;
using Godot;
using GodotClient.Town2d;

namespace GodotClient.Ui;

/// <summary>
/// U5 (loop-legibility plan, KTD-E): the pointing half of the tutorial. <see cref="TutorialFlow.Registry"/>
/// says WHAT the active step points at (a <see cref="TutorialAnchor"/>); this class is the ONLY thing
/// that decides HOW that pointing renders, and it renders it two different ways on purpose:
///
/// <list type="bullet">
/// <item>A <see cref="TutorialAnchorKind.Building"/> anchor pulses the real <see cref="Building2D"/>
/// sprite (<see cref="Building2D.SetTutorialPulsing"/>) — the building lives inside <see
/// cref="Town2D"/>'s own <c>SubViewport</c>, a SEPARATE coordinate space a screen-space overlay
/// cannot address (confirmed by <c>Building2D</c>'s own click-picking doc: <c>Town2D</c>'s
/// <c>SubViewport</c> is what physics-picking is enabled on), so the pulse has to live with the
/// sprite itself rather than be drawn over it.</item>
/// <item>U2 (tutorial-revamp plan, §11.13): a <see cref="TutorialAnchorKind.Station"/> anchor
/// pulses a specific station INSIDE a venue's walkable interior room (<see
/// cref="Town2D.FindStation"/>) rather than the building that contains it — "the anvil, not the
/// building" — using the identical <see cref="Building2D"/> pulse mechanism, since a station is
/// one too (<see cref="Town2d.InteriorRoom2D.Stations"/>).</item>
/// <item>A <see cref="TutorialAnchorKind.Hud"/> anchor gets a pulsing outline drawn by THIS Control,
/// resolved BY NAME against the live scene (<see cref="Node.FindChild(string, bool, bool)"/>) —
/// HUD controls (the bell, the Watch button, a tray button, a modal's own fitted card) sit in the
/// SAME coordinate space as this overlay, so a screen-space rect works.</item>
/// <item>U-T2-6 (Wave A substrate, §11.14.4): a <see cref="TutorialAnchorKind.PanelControl"/> anchor
/// draws the IDENTICAL screen-space outline as Hud, but resolves the target SCOPED to one named
/// drawer panel (<see cref="DrawerHost.PanelContent"/>) rather than searching the whole mounted UI —
/// two different panels reusing a control name can never resolve to the wrong one this way, the gap
/// a plain Hud lookup would have left open.</item>
/// </list>
///
/// <para><b>Never a dead click, never a silent fallback</b> (house rule). <see cref="RefreshAnchor"/>
/// resolves the anchor EAGERLY, the moment it becomes active — a <see cref="TutorialAnchorKind.Building"/>
/// key that is not in <see cref="TownLayout2D.Venues"/> throws via <see cref="Town2D.FindBuilding"/>
/// itself (its own "every real caller expects the layout to already exist" contract); a <see
/// cref="TutorialAnchorKind.Hud"/> name that does not resolve to a live <see cref="Control"/> throws
/// here, explicitly, rather than quietly drawing nothing. A registry row naming a real thing that
/// then silently fails to point at it is exactly the "points at nothing" failure mode
/// <c>TutorialRegistryConformanceTests</c> exists to catch at TEST time; at PLAY time the same
/// contract holds — a loud exception during development beats a step that silently stopped
/// pointing at anything.</para>
/// </summary>
public sealed partial class TutorialOverlay : Control
{
    /// <summary>Warm gold — distinct from <see cref="Building2D"/>'s cool hover brighten and from
    /// the Ember waiting-dot elsewhere in the HUD, so a tutorial pulse always reads as ITS OWN
    /// thing, never confusable with an unrelated highlight.</summary>
    private static readonly Color OutlineColor = new(1f, 0.85f, 0.35f);

    private const float OutlineWidth = 3f;

    /// <summary>Mirrors <c>DayTimeline</c>'s own waiting-dot idiom (accumulated-delta, no engine
    /// Tween in this codebase).</summary>
    private const double PulsePeriodSeconds = 1.1;

    private const float PulseMinAlpha = 0.4f;

    private ColorRect _top = null!;
    private ColorRect _bottom = null!;
    private ColorRect _left = null!;
    private ColorRect _right = null!;

    private TutorialAnchor _anchor = TutorialAnchor.None;
    private Building2D? _pulsingBuilding;
    private Control? _hudTarget;
    private double _elapsed;

    /// <summary>U11 (§11.14.14): the target <see cref="ScrollIntoView"/> last fired for — distinct
    /// from <see cref="_hudTarget"/> itself so <see cref="Tick"/> (called every frame) only scrolls
    /// a freshly-activated anchor into view ONCE, never every frame the pulse keeps ticking. A
    /// repeated auto-scroll would fight a player who deliberately scrolled the panel away from the
    /// hint afterward.</summary>
    private Control? _scrolledTarget;

    /// <summary>Settle-poll ceiling for <see cref="ScrollIntoView"/> — mirrors
    /// <c>GodotClient.Panels.ForgePanel.DeferEnsureVisible</c>'s own documented constants (wait on
    /// the CONDITION, never a guessed frame count).</summary>
    private const int ScrollIntoViewSettleFrames = 240;

    private const int ScrollIntoViewStableFramesRequired = 3;

    /// <summary>Which anchor is currently active — test/inspection surface.</summary>
    public TutorialAnchor CurrentAnchor => _anchor;

    /// <summary>Which building OR station (if any) currently carries the world-space pulse —
    /// test/inspection surface for "the overlay pulses exactly the active step's anchor and
    /// nothing else". A Building anchor reads as the venue key ("forge"); a Station anchor reads
    /// as the station's own id ("anvil") — both are the SAME <see cref="Building2D.Key"/> field,
    /// since a station is a Building2D too.</summary>
    public string? PulsingBuildingKey => _pulsingBuilding?.Key;

    /// <summary>Which HUD control (if any) currently carries the screen-space outline — test/
    /// inspection surface, same purpose as <see cref="PulsingBuildingKey"/>.</summary>
    public string? PulsingHudControlName => _hudTarget?.Name.ToString();

    /// <summary>Construct the four outline strips. Call once, before the first <see
    /// cref="RefreshAnchor"/>.</summary>
    public void Build()
    {
        Name = "TutorialOverlay";
        MouseFilter = MouseFilterEnum.Ignore; // never intercepts a click — a pure visual pointer

        _top = OutlineStrip("TutorialOverlayTop");
        _bottom = OutlineStrip("TutorialOverlayBottom");
        _left = OutlineStrip("TutorialOverlayLeft");
        _right = OutlineStrip("TutorialOverlayRight");
    }

    private ColorRect OutlineStrip(string name)
    {
        var rect = new ColorRect { Name = name, Color = OutlineColor, Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(rect);
        return rect;
    }

    /// <summary>
    /// Re-point the overlay at <paramref name="anchor"/> — call whenever the active step (or its
    /// anchor) might have changed (mirrors <see cref="ObjectiveTracker.Refresh"/>'s own
    /// call-every-tick, never-per-frame contract). <paramref name="town"/> resolves a Building or
    /// Station anchor; <paramref name="drawer"/> (U-T2-6) resolves a PanelControl anchor's own named
    /// panel; <paramref name="hudRoot"/> (the live <c>MainUi</c> tree) resolves a Hud anchor by name.
    /// A no-op if the SAME anchor is already active (never restarts the pulse or re-resolves on every
    /// idle tick).
    /// </summary>
    public void RefreshAnchor(TutorialAnchor anchor, Town2D town, DrawerHost drawer, Node hudRoot)
    {
        if (_anchor == anchor)
        {
            return;
        }

        _anchor = anchor;
        _pulsingBuilding?.SetTutorialPulsing(false);
        _pulsingBuilding = null;
        _hudTarget = null;
        _elapsed = 0;
        HideOutline();

        switch (anchor.Kind)
        {
            case TutorialAnchorKind.Building:
                // Throws if the key is not a real venue — see class doc; never a silent miss.
                _pulsingBuilding = town.FindBuilding(anchor.Key!);
                _pulsingBuilding.SetTutorialPulsing(true);
                break;

            case TutorialAnchorKind.Station:
                // U2 (tutorial-revamp plan, §11.13): a station IS a Building2D (InteriorRoom2D
                // mounts them the same way town buildings mount), so the SAME pulse mechanism
                // applies unchanged — only the lookup differs (venue room's station table, not
                // the town's building table). Throws if the (venue, station) pair does not
                // resolve — same never-point-at-nothing contract as the Building/Hud branches.
                _pulsingBuilding = town.FindStation(anchor.Key!, anchor.StationId!);
                _pulsingBuilding.SetTutorialPulsing(true);
                break;

            case TutorialAnchorKind.Hud:
                _hudTarget = hudRoot.FindChild(anchor.Key!, recursive: true, owned: false) as Control;
                if (_hudTarget is null)
                {
                    throw new InvalidOperationException(
                        $"Tutorial HUD anchor \"{anchor.Key}\" does not resolve to a Control in the live " +
                        "scene — a tutorial step must never point at nothing (house rule). Fix the registry " +
                        "row in TutorialFlow.Registry or the control's own Name.");
                }

                break;

            case TutorialAnchorKind.PanelControl:
                // U-T2-6: resolved SCOPED to the named panel's own registered content root, never
                // against the whole mounted UI — this is the whole reason this kind exists over
                // reusing Hud (class doc). Reuses _hudTarget for the actual outline: a panel control
                // sits in the same screen-space coordinate system as any other HUD control once its
                // panel is showing, so Tick()'s existing IsVisibleInTree()-gated draw already does
                // the right thing with no changes there — it naturally hides the outline while a
                // DIFFERENT panel is open (this one's Visible chain is false) and shows it once the
                // named panel is actually the one on screen.
                // U-T9-6 (§11.14.13): the drawer's ten registered panels are not the whole UI. The
                // Ledger, Commissions, Legends, Camp and Forecast are MODAL siblings mounted straight
                // onto MainUi, and they are exactly the surfaces the course's payoff happens on — the
                // proof card, the rite, accept/decline, the vigil card. So this kind, built to point at
                // a control inside a panel, could not reach a single one of them, and T9's beats all
                // live there. Modals resolve through MainUi's own scoped lookup rather than a recursive
                // search of the whole mounted tree, so the scoping this kind exists for still holds.
                var panelRoot = drawer.PanelContent(anchor.Key!)
                    ?? (hudRoot as GodotClient.MainUi)?.ModalContent(anchor.Key!);
                if (panelRoot is null)
                {
                    throw new InvalidOperationException(
                        $"Tutorial PanelControl anchor names surface \"{anchor.Key}\", which is neither " +
                        "a registered DrawerHost panel nor a known modal — a tutorial step must never " +
                        "point at nothing (house rule). Fix the registry row in TutorialFlow.Registry, " +
                        "or add the modal to MainUi.ModalContent.");
                }

                _hudTarget = panelRoot.FindChild(anchor.ControlName!, recursive: true, owned: false) as Control;
                if (_hudTarget is null)
                {
                    throw new InvalidOperationException(
                        $"Tutorial PanelControl anchor \"{anchor.Key}/{anchor.ControlName}\" does not resolve " +
                        "to a Control inside that panel — a tutorial step must never point at nothing (house " +
                        "rule). Fix the registry row in TutorialFlow.Registry or the control's own Name.");
                }

                break;
        }
    }

    /// <summary>
    /// Advance the outline's pulse by one frame's delta (mirrors <c>DayTimeline.Tick</c>'s
    /// waiting-dot idiom) — call every frame from <c>MainUi._Process</c>. No-op with nothing
    /// anchored. A Building anchor's own pulse lives on <see cref="Building2D"/> itself; ticking it
    /// HERE means callers only ever drive one overlay tick regardless of which kind is active.
    /// </summary>
    public void Tick(double delta)
    {
        _pulsingBuilding?.TickTutorialPulse(delta);

        if (_hudTarget is null || !_hudTarget.IsVisibleInTree())
        {
            HideOutline();
            return;
        }

        // U11 (§11.14.14): the FIRST tick a target is on screen, scroll it into view inside
        // whatever ScrollContainer owns it. RefreshAnchor (the anchor-RESOLUTION half of this
        // class — see class doc's kind-by-kind split) only decides WHICH control is the target,
        // never where that control's own panel happens to be scrolled, so a step whose control
        // started scrolled away used to point at something the player could not reach without
        // already knowing to scroll first. Gated on _scrolledTarget so this fires once per anchor
        // activation, not every frame the pulse ticks.
        if (!ReferenceEquals(_hudTarget, _scrolledTarget))
        {
            _scrolledTarget = _hudTarget;
            ScrollIntoView(_hudTarget);
        }

        // Bug fix (U11, §11.14.14): a control scrolled out of its own ScrollContainer's viewport
        // is STILL IsVisibleInTree() == true — Godot's scroll clipping is a paint-time crop, not a
        // tree-visibility flag — so drawing the target's own unclipped GetGlobalRect() let this
        // outline float outside the panel that owns it, over whatever unrelated interface happened
        // to sit there (a rendered defect that reached main once already for a different reason —
        // see this unit's own PR body). Intersect against the nearest ScrollContainer ancestor (if
        // any) before drawing.
        var rect = _hudTarget.GetGlobalRect();
        var scroll = FindAncestorScroll(_hudTarget);
        if (scroll is not null)
        {
            rect = rect.Intersection(scroll.GetGlobalRect());
            if (rect.Size.X <= 0f || rect.Size.Y <= 0f)
            {
                // Fully scrolled out: draw NOTHING rather than a degenerate sliver pinned to the
                // container's own edge, which would read as "pointing at the edge" instead of "not
                // on screen right now" — and this path should be transient anyway, since the
                // ScrollIntoView call above is what is supposed to bring a fresh target back into
                // frame in the first place.
                HideOutline();
                return;
            }
        }

        _elapsed += delta;
        var phase = (float)((_elapsed % PulsePeriodSeconds) / PulsePeriodSeconds);
        var alpha = PulseMinAlpha + (1f - PulseMinAlpha) * (0.5f + 0.5f * Mathf.Sin(Mathf.Tau * phase));
        DrawOutline(rect, alpha);
    }

    /// <summary>U11 (§11.14.14): the nearest <see cref="ScrollContainer"/> ancestor of
    /// <paramref name="control"/>, or null. Both the clip fix (<see cref="Tick"/>: draw nothing/
    /// draw clipped outside it) and the scroll-into-view fix (<see cref="ScrollIntoView"/>: bring
    /// the target on screen inside it) key off this SAME single ancestor — Godot's own scroll
    /// clipping is per-ScrollContainer (a control's rendered rect is already cropped to its
    /// immediate scrolling parent, never a further-out one), so that is the one boundary either fix
    /// needs to respect. Mirrors <c>GodotClient.Tests.HumanPlayer.VisiblePartOf</c>'s own ancestor-
    /// walk shape (test-only, so not reused directly — production code cannot depend on the test
    /// assembly).</summary>
    private static ScrollContainer? FindAncestorScroll(Control control)
    {
        for (var parent = control.GetParent(); parent is not null; parent = parent.GetParent())
        {
            if (parent is ScrollContainer scroll)
            {
                return scroll;
            }
        }

        return null;
    }

    /// <summary>U11 (§11.14.14): scroll <paramref name="target"/>'s <see cref="FindAncestorScroll"/>
    /// so it is on screen inside its own panel — a no-op if it has no scrolling ancestor. Mirrors
    /// <c>GodotClient.Panels.ForgePanel.DeferEnsureVisible</c>'s own settle-poll idiom EXACTLY (wait
    /// on the CONDITION — the target's <see cref="Control.GlobalPosition"/> going stable across
    /// frames — never a guessed frame count, this codebase's own rule, cited there too): a target
    /// that just became the active anchor may have had its panel made Visible the SAME frame (a
    /// drawer just opened), so its GlobalPosition is not guaranteed to have settled into its final
    /// layout position yet. Aligns the target's TOP edge to the scroll's own top — the same landing
    /// register #156 fixed <c>GodotClient.Panels.ForgePanel.FocusSection</c> to use for a single
    /// small target rather than Godot's built-in <c>ensure_control_visible</c>, which lands on the
    /// BOTTOM edge instead once a target is taller than its viewport.</summary>
    private async void ScrollIntoView(Control target)
    {
        var scroll = FindAncestorScroll(target);
        if (scroll is null)
        {
            return;
        }

        var tree = GetTree();
        if (tree is null)
        {
            return;
        }

        var previous = new Vector2(float.NaN, float.NaN);
        var stable = 0;
        for (var i = 0; i < ScrollIntoViewSettleFrames; i++)
        {
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            if (!GodotObject.IsInstanceValid(scroll) || !GodotObject.IsInstanceValid(target))
            {
                return;
            }

            var current = target.GlobalPosition;
            stable = current == previous ? stable + 1 : 0;
            previous = current;

            if (stable >= ScrollIntoViewStableFramesRequired)
            {
                break;
            }
        }

        if (!GodotObject.IsInstanceValid(scroll) || !GodotObject.IsInstanceValid(target))
        {
            return;
        }

        var delta = (int)(target.GlobalPosition.Y - scroll.GlobalPosition.Y);
        scroll.ScrollVertical = Math.Max(0, scroll.ScrollVertical + delta);
    }

    private void HideOutline()
    {
        _top.Visible = _bottom.Visible = _left.Visible = _right.Visible = false;
    }

    private void DrawOutline(Rect2 rect, float alpha)
    {
        var color = new Color(OutlineColor, alpha);
        _top.Visible = _bottom.Visible = _left.Visible = _right.Visible = true;
        _top.Color = _bottom.Color = _left.Color = _right.Color = color;

        _top.Position = rect.Position - new Vector2(0, OutlineWidth);
        _top.Size = new Vector2(rect.Size.X, OutlineWidth);

        _bottom.Position = new Vector2(rect.Position.X, rect.Position.Y + rect.Size.Y);
        _bottom.Size = new Vector2(rect.Size.X, OutlineWidth);

        _left.Position = rect.Position - new Vector2(OutlineWidth, 0);
        _left.Size = new Vector2(OutlineWidth, rect.Size.Y);

        _right.Position = new Vector2(rect.Position.X + rect.Size.X, rect.Position.Y);
        _right.Size = new Vector2(OutlineWidth, rect.Size.Y);
    }
}
