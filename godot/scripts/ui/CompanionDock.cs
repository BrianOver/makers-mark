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
/// <item>This class is deliberately NOT added to <c>MainUi.OverlaySurfaces()</c>. That single
/// omission is the whole design: <c>AnOverlayOwnsTheScreen()</c> stays false while it is open, so
/// the tutorial card stays visible, town input stays live, the clock never latches, and — because
/// nothing here ever touches <see cref="DrawerHost"/> or hides <c>ForgePanel</c> — the craft
/// overlay's <c>_Notification</c> cancel path never fires.</item>
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

        var scroll = new ScrollContainer
        {
            Name = "DocketScroll",
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _card.AddChild(scroll);

        _body = new VBoxContainer { Name = "DocketBody", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(_body);
    }

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

        CounterSectionBuilder.Build(_body, Adapter.CurrentState, () => ForgeOneRequested?.Invoke());
    }
}
