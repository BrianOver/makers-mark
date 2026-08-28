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
/// <item>U8 (§11.14.14): a <see cref="TutorialAnchorKind.PanelSection"/> anchor resolves through the
/// EXACT SAME scoped lookup as <see cref="TutorialAnchorKind.PanelControl"/> — a section container
/// is a <see cref="Control"/> by name like any other — and draws the identical outline once found.
/// The two share one <c>case</c> below for exactly that reason; see <see
/// cref="TutorialAnchorKind.PanelSection"/>'s own doc for why it is still a distinct Kind rather
/// than a bare alias.</item>
/// <item>U15 (§11.14.14): a <see cref="TutorialAnchorKind.Building"/> or <see
/// cref="TutorialAnchorKind.Station"/> anchor's target can sit anywhere in the town — often a
/// screen or more from wherever the player actually is (KTD-1: this game's whole camera is a single
/// follow-cam with no minimap). <see cref="Tick"/> now projects that target's world position
/// through <see cref="Town2D.WorldToScreen"/> every frame and, when it is NOT inside <see
/// cref="Town2D.ViewportScreenRect"/>, points a small edge marker (<see cref="_offCameraMarker"/>)
/// at it instead of drawing nothing — the exact "station pulse behind a wall" defect class this
/// project has already shipped once (see this unit's own PR body). KTD7: the marker only ever says
/// WHERE — it never moves the camera, so it cannot become the "quest compass" law 1 forbids.</item>
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

    /// <summary>U15 (§11.14.14): the town <see cref="RefreshAnchor"/> was last given — <see
    /// cref="Tick"/> needs it every frame to re-project the live anchor's world position, but
    /// <see cref="Tick"/>'s own signature is called from <c>MainUi._Process</c> with no town
    /// argument, so this is the one place that reference is remembered. Safe to cache: <c>MainUi</c>
    /// owns exactly one <see cref="Town2D"/> for the life of the session and passes that SAME
    /// instance every refresh.</summary>
    private Town2D? _town;

    /// <summary>U15: the off-camera half of a world anchor's pointer — see class doc's new bullet.
    /// Built once in <see cref="Build"/>, repositioned/rotated/hidden every <see cref="Tick"/> by
    /// <see cref="UpdateOffCameraMarker"/>. Never a click target (MouseFilter.Ignore, mirrors every
    /// other child here).</summary>
    private OffCameraMarker _offCameraMarker = null!;

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

    /// <summary>U15: true while the off-camera marker is showing (the active anchor's world target
    /// exists but is not inside <see cref="Town2D.ViewportScreenRect"/> right now) — test/inspection
    /// surface.</summary>
    public bool OffCameraMarkerVisible => _offCameraMarker.Visible;

    /// <summary>
    /// U20 (§11.14.14): forces the very NEXT <see cref="RefreshAnchor"/> call to treat its anchor as
    /// freshly arrived, even when it names the IDENTICAL anchor already showing. <see
    /// cref="RefreshAnchor"/>'s own doc says a no-op guard exists precisely so an idle tick never
    /// restarts the pulse for no reason — but a player's own "remind me" press (<see
    /// cref="MainUi.ReaskTutorial"/>) is the ONE case a restart IS wanted: the flash — the pulse
    /// snapping back to its brightest point, the off-camera marker's <c>Visible</c> flipping through
    /// false first — is the whole visible answer to that press. Without this, re-asking about a step
    /// whose anchor has not moved since the last tick would hit the no-op guard and nothing on the
    /// world side would respond at all.
    ///
    /// <para>Reached from exactly one call site, never a tick — so this can never fire on its own
    /// (law 1: influence never orders). Implemented as clearing the remembered anchor rather than a
    /// separate "flash now" flag: the NEXT <see cref="RefreshAnchor"/> call (triggered synchronously
    /// by <see cref="MentorBanner.Changed"/> the instant <c>ReaskTutorial</c> shows its line) sees
    /// <see cref="TutorialAnchor.None"/> where the real anchor used to be, takes the "changed" branch
    /// unconditionally, and re-resolves the real anchor fresh — the identical mechanism a genuine
    /// anchor change already uses, reused rather than duplicated.</para>
    /// </summary>
    public void ForceRefreshOnNextCall() => _anchor = TutorialAnchor.None;

    /// <summary>U15: the marker's live on-screen center — meaningful only while <see
    /// cref="OffCameraMarkerVisible"/> is true. Screen space (same system <see
    /// cref="Control.GetGlobalRect"/> reports for any control here).</summary>
    public Vector2 OffCameraMarkerCenter => _offCameraMarker.Position + OffCameraMarker.PivotCenter;

    /// <summary>U15: the direction (radians, 0 = screen-right, increasing clockwise — Godot's own
    /// screen-space convention) the marker currently points — meaningful only while <see
    /// cref="OffCameraMarkerVisible"/> is true.</summary>
    public float OffCameraMarkerDirectionRadians => _offCameraMarker.Rotation;

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

        // U15: built last so it draws over the outline strips above (never matters in practice —
        // a Building/Station anchor and a Hud/PanelControl/PanelSection anchor are mutually
        // exclusive per-tick, see Tick/UpdateOffCameraMarker — but matches this method's own
        // top-to-bottom "later child draws over earlier" ordering regardless).
        _offCameraMarker = new OffCameraMarker { Name = "TutorialOffCameraMarker", MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        AddChild(_offCameraMarker);
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
        // U15: remembered unconditionally (even on the no-op path below) — Tick has no town
        // parameter of its own (see _town's doc) and MainUi always hands in the SAME live Town2D,
        // so there is no staleness risk in refreshing this every call.
        _town = town;

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
        _offCameraMarker.Visible = false; // U15: never let a stale marker survive an anchor change

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
            case TutorialAnchorKind.PanelSection:
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
                // live there.
                // U8 (§11.14.14): TutorialAnchorKind.PanelSection falls into this exact same case —
                // a section container resolves by the identical scoped-panel-then-FindChild lookup as
                // any other named control (class doc's own new bullet explains why it is still a
                // distinct Kind rather than a bare PanelControl alias).
                // U9 (§11.14.14): the drawer-then-modal OR-chain that used to live here was itself two
                // hardcoded lists (DrawerHost's own registrations, MainUi.ModalContent's five-arm
                // switch) that missed five real MainUi-mounted surfaces (Mirror/Bestiary/Chronicle/
                // Pip/Docket). TutorialSurfaceRegistry is now the ONE roster both lists were replaced
                // with — see its own class doc.
                var panelRoot = TutorialSurfaceRegistry.ContentRootFor(anchor.Key!, drawer, hudRoot as GodotClient.MainUi);
                if (panelRoot is null)
                {
                    throw new InvalidOperationException(
                        $"Tutorial {anchor.Kind} anchor names surface \"{anchor.Key}\", which is not a " +
                        "registered tutorial surface — a tutorial step must never point at nothing (house " +
                        "rule). Fix the registry row in TutorialFlow.Registry, or add the surface to " +
                        "TutorialSurfaceRegistry.Surfaces.");
                }

                _hudTarget = panelRoot.FindChild(anchor.ControlName!, recursive: true, owned: false) as Control;
                if (_hudTarget is null)
                {
                    throw new InvalidOperationException(
                        $"Tutorial {anchor.Kind} anchor \"{anchor.Key}/{anchor.ControlName}\" does not resolve " +
                        "to a Control inside that panel — a tutorial step must never point at nothing (house " +
                        "rule). Fix the registry row in TutorialFlow.Registry or the control's own Name.");
                }

                break;
        }

        // U15: a "world anchor" is live exactly when the switch above populated _pulsingBuilding
        // (Building or Station kind) — every other kind leaves it null, which correctly restores
        // every station's Tell to normal (Town2D.SetWorldAnchorTellDamping's own doc). The pulsing
        // station itself (if any) is passed as the exemption so its OWN Tell never dims under its
        // own pulse.
        town.SetWorldAnchorTellDamping(_pulsingBuilding is not null, _pulsingBuilding);
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
        UpdateOffCameraMarker(); // U15: independent of the Hud path below — a Building/Station
                                 // anchor never sets _hudTarget, so this must run before the
                                 // early-return just underneath ever gets a chance to skip it.

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

    /// <summary>
    /// U15 (§11.14.14): the off-camera half of a world anchor's pointer — see class doc's new
    /// bullet for the whole feature and <see cref="Town2D.WorldToScreen"/> for the projection math.
    /// Called every <see cref="Tick"/>, unconditionally (cheap geometry, no allocation beyond the
    /// odd <see cref="Vector2"/>): shows/hides/repositions <see cref="_offCameraMarker"/> from
    /// scratch each frame rather than diffing against last frame's state, so there is no separate
    /// "did the situation change" bookkeeping to get wrong — the SAME reason <see
    /// cref="Building2D.TickTutorialPulse"/> recomputes its own alpha/scale from scratch every call
    /// instead of easing toward a cached target.
    /// </summary>
    private void UpdateOffCameraMarker()
    {
        // No world anchor live at all (Hud/PanelControl/PanelSection/None kinds, or no anchor yet)
        // — _pulsingBuilding is exactly the Building/Station-kind signal (RefreshAnchor's own
        // switch), so this single null-check covers all four of those cases in one line.
        if (_pulsingBuilding is null || _town is null
            || !GodotObject.IsInstanceValid(_pulsingBuilding) || !GodotObject.IsInstanceValid(_town))
        {
            _offCameraMarker.Visible = false;
            return;
        }

        // U15: a Station-kind anchor is only ever handed out while the player is ALREADY inside
        // that station's own venue (TutorialFlow.AimAnchor's own doc: it demotes Station to
        // Building otherwise), so its room always matches whatever the live camera is clamped to
        // right now. A Building-kind anchor carries no such guarantee — it names a TOWN building,
        // which is always valid in the town's OWN coordinate frame, but if the player is currently
        // standing inside a DIFFERENT interior room, the camera is clamped to THAT room's own
        // disjoint "island" (InteriorLayout2D's own placement doc: every room sits at a distinct,
        // far-apart world offset specifically so no camera clamp can ever see two at once). Under
        // that mismatched camera, projecting the town building's position would produce a screen
        // point with no real spatial meaning — not "off camera in the right direction", just
        // arithmetic noise from two unrelated coordinate islands. Silence is more honest than a
        // confidently wrong direction, so this is the one case the marker deliberately sits out.
        if (_anchor.Kind == TutorialAnchorKind.Building
            && _town.InteriorActive
            && _town.InteriorVenueKey != _anchor.Key)
        {
            _offCameraMarker.Visible = false;
            return;
        }

        // The margin keeps the marker's own half-size from ever clipping past the canvas edge, and
        // (deliberately) doubles as the "on screen" threshold below — a target that is technically
        // inside the raw viewport rect but touching its very edge reads as off-frame to a player
        // anyway, so treating it as still "off camera" until it clears the margin is the more
        // honest call, not a separate leniency this code has to justify twice.
        const float EdgeMargin = 18f;
        var screenRect = _town.ViewportScreenRect;
        var inset = screenRect.Grow(-EdgeMargin);
        if (inset.Size.X <= 0f || inset.Size.Y <= 0f)
        {
            // Degenerate (a viewport smaller than the margin, e.g. a tiny test fixture) — fall back
            // to the raw rect rather than divide-by-near-zero below.
            inset = screenRect;
        }

        var targetScreen = _town.WorldToScreen(_pulsingBuilding.GlobalPosition);

        if (inset.HasPoint(targetScreen))
        {
            // On screen: Building2D's own pulse (already ticked above) is what reads here — the
            // marker has nothing to add and must not linger (never-persists, class doc's KTD7 note).
            _offCameraMarker.Visible = false;
            return;
        }

        var center = inset.GetCenter();
        var toward = targetScreen - center;
        if (toward.LengthSquared() < 1f)
        {
            // Degenerate (dead center — should not coexist with "not HasPoint" above, but a
            // near-zero rect from the fallback just above could produce this): nothing sensible to
            // point toward.
            _offCameraMarker.Visible = false;
            return;
        }

        var direction = toward.Normalized();

        // Ray-from-center-to-edge, clamped to the inset rect's own half-extents — the standard
        // "where does this direction cross the box" solve (minimum of the two axis-aligned hit
        // distances), so the marker always lands exactly on the rect's border, never past a corner.
        var half = inset.Size / 2f;
        var alongX = Mathf.Abs(direction.X) > 0.0001f ? half.X / Mathf.Abs(direction.X) : float.PositiveInfinity;
        var alongY = Mathf.Abs(direction.Y) > 0.0001f ? half.Y / Mathf.Abs(direction.Y) : float.PositiveInfinity;
        var edgePoint = center + direction * Mathf.Min(alongX, alongY);

        _offCameraMarker.Visible = true;
        _offCameraMarker.QueueRedraw(); // geometry is static, but this is cheap and removes any
                                        // doubt about whether a Visible=false→true flip alone
                                        // re-triggers _Draw (mirrors QuenchCanvas's own
                                        // every-frame QueueRedraw idiom elsewhere in this codebase).
        _offCameraMarker.PivotOffset = OffCameraMarker.PivotCenter;
        _offCameraMarker.Position = edgePoint - OffCameraMarker.PivotCenter;
        _offCameraMarker.Rotation = direction.Angle();

        // U15 (KTD8/OQ3): reads the SAME live scale/alpha Building2D.TickTutorialPulse just computed
        // for the on-screen pulse — not a second copy of the breathe math — so the marker and the
        // pulse are provably one signature, never two clocks that merely look alike (Building2D's
        // own TutorialPulseScale/TutorialPulseAlpha doc explains why this is exposed at all).
        var scale = _pulsingBuilding.TutorialPulseScale;
        _offCameraMarker.Scale = new Vector2(scale, scale);
        var color = Building2D.TutorialPulseColor;
        color.A = _pulsingBuilding.TutorialPulseAlpha;
        _offCameraMarker.Modulate = color;
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

    /// <summary>
    /// U15 (§11.14.14): the drawn half of the off-camera marker — a small solid triangle, tip along
    /// local +X, that <see cref="UpdateOffCameraMarker"/> positions/rotates/scales/recolors every
    /// tick (mirrors the <c>node.PivotOffset</c>/<c>node.Scale</c>/<c>node.Modulate</c> idiom already
    /// used for a breathing icon elsewhere in this codebase, e.g. <c>DelveStage</c>'s sparkle FX and
    /// <c>BestiaryPanel</c>'s portrait breathe). Deliberately a SHAPE, not another warm-gold hue —
    /// see class doc's U15 bullet and <see cref="Building2D.TutorialPulseScale"/>'s own doc for why
    /// a shape/motion signature is the accessible answer here (§11.14.14 OQ3).
    ///
    /// <para>The triangle geometry itself never changes frame to frame — only <see
    /// cref="CanvasItem.Modulate"/>/<see cref="CanvasItem.Rotation"/>/<see cref="CanvasItem.Scale"/>
    /// do, and Godot applies all three as ordinary render-time transforms with no redraw needed.
    /// <see cref="UpdateOffCameraMarker"/> still calls <c>QueueRedraw()</c> defensively whenever it
    /// shows this marker (mirrors <c>QuenchCanvas</c>'s own every-frame <c>QueueRedraw</c> idiom) —
    /// belt and suspenders against relying on a bare <c>Visible</c> flip alone re-triggering the
    /// first draw, not because the geometry itself is expected to change.</para>
    /// </summary>
    private sealed partial class OffCameraMarker : Control
    {
        private const float DrawSize = 14f;

        /// <summary>The triangle's own local center — also this control's <see
        /// cref="Control.PivotOffset"/>, so rotating/scaling it turns/breathes around its visual
        /// center rather than its top-left corner.</summary>
        public static readonly Vector2 PivotCenter = new(DrawSize / 2f, DrawSize / 2f);

        public override void _Draw()
        {
            var tip = PivotCenter + new Vector2(DrawSize / 2f, 0f);
            var baseA = PivotCenter + new Vector2(-DrawSize * 0.35f, DrawSize * 0.4f);
            var baseB = PivotCenter + new Vector2(-DrawSize * 0.35f, -DrawSize * 0.4f);
            // Solid white — UpdateOffCameraMarker's Modulate supplies the actual color/alpha, the
            // same division of labor DrawOutline's ColorRects rely on (geometry here, color there).
            DrawPolygon(new[] { tip, baseA, baseB }, new[] { Colors.White, Colors.White, Colors.White });
        }
    }
}
