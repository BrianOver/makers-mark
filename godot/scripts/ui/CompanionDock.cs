using System;
using GameSim.Contracts;
using Godot;
using GodotClient.Minigames;
using GodotClient.Panels;

namespace GodotClient.Ui;

/// <summary>
/// Register #160, U-T2-3/U-T2-4 ("Tomorrow at the Counter" becomes openable while crafting):
/// the owner praised this one screen and then asked for the one thing it structurally could not
/// do — stay open while he worked the forge. Before this unit, four separate mechanisms
/// conspired to make that impossible, every one of them load-bearing for something else:
///
/// <list type="number">
/// <item>Every HUD surface is a plain <see cref="Control"/> sibling of <c>MainUi</c>, so paint
/// order is <c>AddChild</c> order. The header row (Books Tray, including the Forecast icon) is
/// added long before <see cref="DrawerHost"/>, so while any drawer is open, the drawer's own
/// full-rect dim veil (<see cref="Control.MouseFilterEnum.Stop"/>, click-to-close) painted over
/// the tray and ATE the Forecast click — clicking the icon closed the drawer instead of opening
/// the board.</item>
/// <item><see cref="RaidForecastBoard"/> is itself a full-rect <c>MouseFilter.Stop</c> control
/// with its own dim, and a member of <c>MainUi.OverlaySurfaces()</c> — so showing it makes
/// <c>MainUi.AnOverlayOwnsTheScreen()</c> true, which hides the tutorial card, blocks town
/// input, and latches the clock. A modal cannot be a companion by definition.</item>
/// <item>Drawer panels replace, never stack — there is no "beside the Forge" slot in that host
/// at all.</item>
/// <item><see cref="ForgePanel"/>'s own <c>_Notification</c> handler force-cancels every
/// running craft overlay the instant the panel's ancestor chain goes invisible
/// (<c>NotificationVisibilityChanged</c>) — so ANY fix that hid the Forge drawer to show the
/// board, even for a frame, threw the craft away.</item>
/// </list>
///
/// <para><b>The fix is a layer that owns no screen.</b> This dock is parented to its own
/// <c>CanvasLayer</c> ("CompanionLayer", Layer 40 — <c>MainUi.BuildUi</c> adds it between
/// <see cref="DrawerHost"/> and <see cref="TabFade"/>, the same idiom <see cref="TabFade"/>
/// (layer 100) and <c>BuildStamp</c> (layer 5) already use). A <c>CanvasLayer</c> with
/// <c>Layer &gt; 0</c> draws above every layer-0 sibling REGARDLESS of <c>AddChild</c> order —
/// including the drawer's veil — and receives GUI input first, so its toggle chip is clickable
/// even while a drawer sits open. Sitting below TabFade means a tab transition still covers it.
///
/// <para>Three rules keep this a companion instead of a second modal:</para>
/// <list type="bullet">
/// <item>Root <see cref="Control.MouseFilter"/> is <see cref="Control.MouseFilterEnum.Ignore"/>
/// — only <see cref="_card"/> itself is <c>Stop</c>. No dim <see cref="ColorRect"/> anywhere.</item>
/// <item>P2-SCREEN-04: this class claims itself with <see cref="SurfaceArbiter"/> like every other
/// surface, but in <see cref="SurfaceRegion.HudDock"/> with <c>OwnsScreen: false</c> — a stated
/// fact, not a silent omission from a hand-written array (the exact shape of the Chronicle defect
/// that unit fixes). <c>MainUi.OverlaySurfaces()</c> only ever projects <see
/// cref="SurfaceRegion.FullScreenModal"/>, so either way <c>AnOverlayOwnsTheScreen()</c> stays false
/// while this is open: the tutorial card stays visible, town input stays live, the clock never
/// latches, and — because nothing here ever touches <see cref="DrawerHost"/> or hides
/// <c>ForgePanel</c> — the craft overlay's <c>_Notification</c> cancel path never fires.</item>
/// <item>Anchored bottom-LEFT, <see cref="DockWidth"/>px wide — the right-anchored
/// <see cref="DrawerHost.DrawerWidth"/>px drawer card can never overlap it, by construction, at
/// any window width this game supports.</item>
/// </list>
///
/// <para><b>U-T2-4:</b> the actual counter-section rendering is <see
/// cref="CounterSectionBuilder"/>, extracted out of <see cref="RaidForecastBoard"/> so this dock
/// and the existing modal call the SAME renderer — one builder, two hosts, so they can never
/// disagree. Both read <see cref="GameSim.Drama.CounterForecast.Queue"/> straight off the live
/// <see cref="GameState"/> (show-only-sim-decided) — this dock computes nothing of its own.</para>
/// </summary>
public partial class CompanionDock : Control
{
    /// <summary>Sits above every plain layer-0 sibling (the drawer's veil included) but below
    /// <see cref="TabFade"/>'s layer 100 — see <c>MainUi.BuildUi</c>'s own <c>CompanionLayer</c>
    /// <see cref="CanvasLayer"/> wiring, which reads this constant for its own <c>Layer</c>.</summary>
    public const int CanvasLayerIndex = 40;

    /// <summary>Fixed card width — narrow enough that the right-anchored <see
    /// cref="DrawerHost.DrawerWidth"/>px drawer can never reach it even at the project's minimum
    /// supported window (1152px).</summary>
    public const float DockWidth = 320f;

    private const float Margin = 12f;
    private const float ChipHeight = 32f;
    private const float CardHeight = 260f;

    private Button? _chip;
    private PanelContainer? _card;
    private VBoxContainer? _body;

    /// <summary>P2-ONBOARD-02: the "tomorrow-at-the-counter" once-ever caption — a sibling of the
    /// scrollable <see cref="_body"/>, never a child of it, so it survives every
    /// <see cref="RefreshBody"/> call (which reruns on EVERY tick this dock stays expanded, via
    /// <see cref="RefreshIfOpen"/> — a child of <see cref="_body"/> would be wiped within a frame
    /// or two of the very first time it appeared).</summary>
    private Label? _caption;

    /// <summary>The live sim adapter this dock reads from (CommissionBoard/LegendsWall precedent)
    /// — set once by <c>MainUi.BuildUi</c> at construction. Never mutated here: reading
    /// <see cref="SimAdapter.CurrentState"/> is the only sim contact this class makes.</summary>
    public SimAdapter? Adapter { get; set; }

    /// <summary>Same bare-event shape as <see cref="RaidForecastBoard.ForgeOneRequested"/> —
    /// forwarded by <c>MainUi</c> to <c>OpenPanel("Forge")</c>. Unlike the modal's own handler,
    /// this dock never closes itself first: staying open through the jump to the Forge is the
    /// entire point of a companion.</summary>
    public event Action? ForgeOneRequested;

    /// <summary>
    /// Raised every time the card expands. <c>MainUi</c> uses it to fire the docket's first-touch
    /// lesson (register #160, "part of the tutorial then become the player's job to reference") —
    /// the once-ever contract lives in <see cref="TutorialFlow.ConsumeFirstTouch"/>, not here, so
    /// this event stays a plain "it opened" fact and any number of listeners can read it without
    /// racing each other for a one-shot.
    /// </summary>
    public event Action? Opened;

    /// <summary>True while the card is showing (the chip toggles this; test hook).</summary>
    public bool IsExpanded => _card is { Visible: true };

    /// <summary>Build the chip + card chrome. Idempotent-guarded like every other code-built node
    /// on this project (<see cref="DrawerHost.Build"/> precedent).</summary>
    public void Build()
    {
        if (_chip is not null)
        {
            return;
        }

        // C3 precedent (SettingsPanel.Build's own note): "docket_toggle" must exist in the
        // InputMap before the tooltip below reads its key label — guarded/idempotent, so this is
        // a safe no-op on every call after the very first, whichever caller runs first.
        MinigameInput.RegisterActions();

        Name = "CompanionDock";
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore; // a companion, not a modal — never blocks by itself

        // P2-SCREEN-04: a DECLARED false, not a silent absence — see this class's own doc for why
        // AnOverlayOwnsTheScreen() must stay false while this is open. SurfaceRegion.HudDock keeps
        // it out of MainUi.OverlaySurfaces()'s FullScreenModal projection either way, but the claim
        // still answers OwnsScreen so a future coverage census can see the exclusion was a decision.
        SurfaceArbiter.Claim(this, new SurfaceClaim("Docket", SurfaceRegion.HudDock, 0, OwnsScreen: false));

        _chip = new Button
        {
            Name = "DocketToggle",
            Text = "Tomorrow at the Counter",
            TooltipText = ShortcutMap.Tooltip("docket_toggle"),
            CustomMinimumSize = new Vector2(DockWidth, ChipHeight),
        };
        _chip.SetAnchorsPreset(LayoutPreset.BottomLeft);
        _chip.OffsetLeft = Margin;
        _chip.OffsetRight = Margin + DockWidth;
        _chip.OffsetBottom = -Margin;
        _chip.OffsetTop = -(Margin + ChipHeight);
        _chip.Pressed += () => SetExpanded(!IsExpanded);
        AddChild(_chip);

        _card = new PanelContainer { Name = "DocketCard", MouseFilter = MouseFilterEnum.Stop, Visible = false };
        _card.AddThemeStyleboxOverride("panel", GameTheme.PanelStyleWood());
        _card.SetAnchorsPreset(LayoutPreset.BottomLeft);
        _card.OffsetLeft = Margin;
        _card.OffsetRight = Margin + DockWidth;
        _card.OffsetBottom = -(Margin + ChipHeight + Margin);
        _card.OffsetTop = _card.OffsetBottom - CardHeight;
        AddChild(_card);

        // P2-ONBOARD-02: a stable wrapper so the once-ever caption can sit ABOVE the scroll without
        // becoming its second direct child (a ScrollContainer only ever manages one). RefreshBody
        // only ever Clears _body itself, never this wrapper, so the caption survives every refresh.
        var cardBody = new VBoxContainer { Name = "DocketCardBody", SizeFlagsVertical = SizeFlags.ExpandFill };
        _card.AddChild(cardBody);

        _caption = UiKit.OnceEverCaption();
        cardBody.AddChild(_caption);

        var scroll = new ScrollContainer
        {
            Name = "DocketScroll",
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        cardBody.AddChild(scroll);

        _body = new VBoxContainer { Name = "DocketBody", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(_body);
    }

    /// <summary>P2-ONBOARD-02: sets the once-ever "tomorrow-at-the-counter" caption — called from
    /// <c>MainUi.ShowDocketLesson</c> the ONE time <see cref="TutorialFlow.ConsumeFirstTouch"/> ever
    /// returns non-null for that id. Replaces the old floating <see cref="MentorBanner"/> popup
    /// that used to fire the instant this dock expanded (register #160's own defect: the docket is
    /// meant to stay open WHILE the player works the forge, which is exactly when a floating popup
    /// covering the screen hurts most).
    ///
    /// <para>Named <c>Set</c>, not <c>Show</c>, on purpose — <c>SurfaceClaimDiscoveryCensusTests</c>
    /// (sim-side) deny-by-defaults any class with a public <c>Show*</c>/<c>Close*</c> pair as a
    /// claimable full-rect modal needing its own <c>SurfaceArbiter.Claim</c> line in
    /// <c>MainUi.cs</c>; this dock already declared, deliberately, that it is NOT one
    /// (<see cref="SurfaceRegion.HudDock"/>, <c>OwnsScreen: false</c>, in <see cref="Build"/> above
    /// — P2-SCREEN-04's own ruling). A same-shaped name would trip that census for a surface this
    /// unit did not touch and the ruling already settled.</para>
    /// </summary>
    public void SetHeaderCaption(string text)
    {
        Build();
        _caption!.Text = text;
        _caption.Visible = true;
    }

    /// <summary>
    /// P2-ONBOARD-02: how much vertical screen space this dock actually occupies right now,
    /// measured from the window's bottom edge — <see cref="Margin"/> + <see cref="ChipHeight"/> for
    /// the chip alone (always present), plus another <see cref="Margin"/> + <see cref="CardHeight"/>
    /// while <see cref="IsExpanded"/> too. <see cref="MentorBanner.PositionDock"/> reads this so the
    /// docked mentor card reserves real headroom above this dock's LIVE footprint instead of
    /// trusting <see cref="SurfaceArbiter"/>'s <see cref="SurfaceRegion.HudDock"/> claim alone — that
    /// claim answers "does this surface own the whole screen" (no), not "what pixels does it
    /// actually cover" (this dock's own chip and expanded card cover real bottom-left pixels, the
    /// exact P2-SCREEN-10 finding this unit's own plan section calls out by name).
    /// </summary>
    public float ReservedFootprintHeight => Margin + ChipHeight + (IsExpanded ? Margin + CardHeight : 0f);

    /// <summary>Expand the card and refresh it from the live state (button/chip precedent).</summary>
    public void Open() => SetExpanded(true);

    /// <summary>Collapse the card.</summary>
    public void Close() => SetExpanded(false);

    /// <summary>Flip open/closed — what the chip and the keyboard shortcut both call.</summary>
    public void Toggle() => SetExpanded(!IsExpanded);

    /// <summary>
    /// Re-render from the live state IFF the card is currently expanded — a no-op while
    /// collapsed. Safe to call on every HUD tick (<c>MainUi.RefreshHud</c>): it only ever reflects
    /// sim state that has already changed by the time it runs, never counts down toward anything,
    /// so this is not a timer on any decision (no-decision-timers law) — it is the same
    /// "re-render on every tick" contract <c>RefreshBellTray</c>/<c>RefreshSurfaceUnlocks</c>
    /// already have.
    /// </summary>
    public void RefreshIfOpen()
    {
        if (IsExpanded)
        {
            RefreshBody();
        }
    }

    private void SetExpanded(bool expanded)
    {
        _card!.Visible = expanded;
        if (expanded)
        {
            RefreshBody();
            Opened?.Invoke();
        }
    }

    private void RefreshBody()
    {
        if (Adapter is null)
        {
            return;
        }

        foreach (var child in _body!.GetChildren())
        {
            _body.RemoveChild(child);
            child.Free();
        }

        // U-T7-4 (register #149): the todo list, FIRST, then the counter forecast it was built
        // beside. The order was measured, not chosen: this dock's card is short and already
        // scrolls, and with the list appended after the counter section six queued heroes pushed
        // its header out of view entirely — a new surface reachable only by scrolling a card most
        // players will never scroll. The list is also the actionable half (what to craft, what to
        // buy, and for whom) while the counter section is one of its two inputs, so first is where
        // it belongs on merit too. The counter section itself is untouched and still immediately
        // below: it is the screen the owner named as the one he liked, rendered by the same shared
        // builder it always was.
        TodoSectionBuilder.Build(_body, Adapter.CurrentState, () => ForgeOneRequested?.Invoke());
        CounterSectionBuilder.Build(_body, Adapter.CurrentState, () => ForgeOneRequested?.Invoke());
    }
}
