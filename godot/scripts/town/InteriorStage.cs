using System;
using System.Collections.Generic;
using System.Linq;
using GameSim.Contracts;
using Godot;
using GodotClient.Panels;
using GodotClient.Ui;

namespace GodotClient.Town;

/// <summary>
/// U22 (world-rework plan, R4/R7, KTD10) — the ONE reusable staged-interior framework: camera
/// "stages into" a painted backdrop with the avatar figure visible in it, and a data-driven
/// hotspot list (label, hover description, action route) per venue (<see cref="Venues"/>).
/// Mounted once as a <c>MainUi</c> overlay sibling (same tier as <see cref="DrawerHost"/>/the
/// modals) — <see cref="Open"/> swaps in a venue's <see cref="VenueSpec"/> and starts the
/// accumulated-delta "push-in" (<see cref="Tick"/>, driven from <c>MainUi._Process</c> — no
/// engine <see cref="Tween"/> anywhere in this codebase, the <see cref="DrawerHost"/>/
/// <c>TabFade</c>/<see cref="ShopStage"/> precedent). A hotspot press (or Esc/the exit hotspot)
/// closes the stage and raises <see cref="HotspotActivated"/>/<see cref="Exited"/> for the
/// caller to route — <c>MainUi</c> maps a non-exit action straight onto the SAME drawer id
/// <c>OpenPanel</c> already accepts ("Forge"/"Shop"/"Tavern"/"Bounties"): content parity with
/// the pre-U22 drawer-on-interact behaviour, presentation upgrade (R4).
///
/// <para><b>Data-driven (KTD10):</b> every venue's backdrop id, title, and hotspot list lives in
/// <see cref="Venues"/> — a fresh venue is a table row, never a new code path (the hotspot
/// definitions are also the carry-forward asset if walkable interiors happen later). Keys match
/// the venue keys <see cref="GodotClient.Town3d.Town3D.DoorAnchor"/> already uses
/// ("forge"/"market"/"tavern"/"minegate").</para>
///
/// <para><b>Shop is richest (art already shipped):</b> a private <see cref="ShopStage"/>
/// instance — the SAME class the pre-U22 shop drawer strip used, ported rather than
/// reimplemented — plays the shelf-slot walk-in/judge/walk-out customer choreography, shown only
/// while "market" is staged, fed every Morning tick via <see cref="OnPhaseCompleted"/> exactly
/// like the drawer strip was. <see cref="RefreshShopStock"/> renders the player's live shelf
/// (<c>GameState.Player.Shelf</c>) as slot icons alongside it.</para>
///
/// <para><b>Graceful degrade:</b> forge/tavern/gate interiors are spec-only as of U13
/// (<c>art/specs/town/TownSpecsExtra.cs</c>: <c>forge-interior</c>/<c>tavern-interior</c>/
/// <c>gate-interior</c>) — an unresolved id falls back to a generated warm gradient (<see
/// cref="ApplyBackdrop"/>, the same <see cref="GradientTexture2D"/> technique <see
/// cref="ShopStage"/> itself uses for its own backdrop degrade). Never a blank hole, never a
/// crash; every hotspot still renders and routes regardless of backdrop art presence.</para>
///
/// <para><b>3D-interiors MVP (see-through mode):</b> when <c>MainUi.OpenInterior</c> mounts a
/// real <see cref="GodotClient.Town3d.InteriorRoom3D"/> in the town's 3D world, it opens this
/// stage with <c>seeThrough: true</c> — the dim veil / painted backdrop / avatar figure hide so
/// the 3D room shows through, and this class serves ONLY the carry-forward hotspot data + exit/
/// Esc/Engaged routing on top (exactly the "walkable interiors later" hand-off the paragraph
/// above reserved the hotspot table for).</para>
/// </summary>
public partial class InteriorStage : Control
{
    /// <summary>One clickable content item inside a venue's interior — a label, a hover
    /// description (rendered as the Control's native tooltip), and the action route it fires on
    /// press. <c>Action == "exit"</c> is reserved for the structural exit affordance below and
    /// never appears in <see cref="Venues"/>' own hotspot lists (exit is stage-wide, not
    /// per-venue data).</summary>
    public readonly record struct Hotspot(string Id, string Label, string Description, string Action);

    /// <summary>One venue's staged interior: its backdrop art id, display title, and declarative
    /// hotspot list (KTD10).</summary>
    public readonly record struct VenueSpec(string VenueKey, string Title, string BackdropArtId, Hotspot[] Hotspots);

    /// <summary>
    /// The declarative venue table (KTD10) — forge/market/tavern/minegate, the four venues R4
    /// names ("Every venue (Forge, Shop, Tavern, Mine Gate) is enterable from day one"). Action
    /// strings match the drawer ids <c>MainUi.OpenPanel</c> already accepts — same panel actions
    /// as the drawers, never a new routing concept.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, VenueSpec> Venues = BuildVenueTable();

    /// <summary>Camera "push-in" duration — accumulated-delta only (<see cref="Tick"/>), no
    /// engine Tween (class doc).</summary>
    public const double StageInSeconds = 0.3;

    private const float StageStartScale = 0.92f;
    private const float HotspotPanelWidth = 260f;
    private const float FigureWidth = 90f;
    private const float FigureHeight = 144f;

    private ColorRect _dim = null!;
    private Control _stage = null!; // the scaled/faded "camera push-in" container
    private TextureRect _backdrop = null!;
    private TextureRect _avatarFigure = null!;
    private HBoxContainer _shelfRow = null!;
    private Label _title = null!;
    private VBoxContainer _hotspotList = null!;
    private Button _exitButton = null!;
    private ShopStage _shopStage = null!;
    private readonly List<Button> _hotspotButtons = [];
    private static GradientTexture2D? _fallbackTexture;
    private VenueSpec? _current;
    private double _stageElapsed = -1;
    private bool _built;

    /// <summary>Raised when a content hotspot (never the exit affordance) is pressed, carrying
    /// its <see cref="Hotspot.Action"/> — the caller (<c>MainUi</c>) routes it to the matching
    /// drawer, same as the pre-U22 interact-opens-the-drawer behaviour.</summary>
    public event Action<string>? HotspotActivated;

    /// <summary>Raised when the exit hotspot or Esc closes the stage — the caller restores the
    /// avatar to the door position it entered from (R4/AE4).</summary>
    public event Action? Exited;

    /// <summary>True while any venue's interior is staged (mid push-in or fully open).</summary>
    public bool IsOpen => _current is not null;

    /// <summary>The currently staged venue's key, or null while closed.</summary>
    public string? VenueKey => _current?.VenueKey;

    /// <summary>True once the current venue's real backdrop art resolved; false on the
    /// generated-gradient degrade path (test/tuning visibility, mirrors <see
    /// cref="ShopStage.HasBackdropArt"/>).</summary>
    public bool HasBackdropArt { get; private set; }

    /// <summary>3D-interiors MVP: true while the current venue opened in see-through mode — the
    /// dim veil / painted backdrop / avatar figure are hidden so the caller's real 3D room
    /// (<see cref="GodotClient.Town3d.InteriorRoom3D"/>, framed by the town camera's own
    /// push-in) shows through underneath, while the hotspot panel / title / exit / Esc routing
    /// all keep working exactly as staged mode. False in classic staged (painted-backdrop) mode.</summary>
    public bool SeeThrough { get; private set; }

    /// <summary>Every hotspot button currently rendered, in declared order (test visibility) —
    /// never includes the structural exit button.</summary>
    public IReadOnlyList<Button> HotspotButtons => _hotspotButtons;

    /// <summary>The structural exit affordance (test visibility).</summary>
    public Button ExitButton => _exitButton;

    /// <summary>The embedded, ported shop choreography (test/tuning visibility) — always built
    /// and always ticking (mirrors the pre-U22 drawer strip's own lifecycle), visible only while
    /// the "market" venue is staged.</summary>
    public ShopStage ShopStage => _shopStage;

    /// <summary>Count of rendered shelf-stock icons (test/tuning visibility) — mirrors <see
    /// cref="Player.Shelf"/>'s count while "market" is the staged venue, 0 for every other venue
    /// (or before the first <see cref="RefreshShopStock"/>/<see cref="Open"/> call).</summary>
    public int ShelfIconCount => _shelfRow.GetChildCount();

    /// <summary>Build the host chrome (dim veil, backdrop, avatar figure, hotspot panel, exit
    /// button, embedded shop stage). Idempotent-guarded like every other code-built node on this
    /// project.</summary>
    public void Build()
    {
        if (_built)
        {
            return;
        }

        Name = "InteriorStage";
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore; // only Dim/hotspot buttons themselves ever block
        Visible = false; // hidden (and input-inert) until the first Open

        _dim = new ColorRect { Name = "InteriorDim", Color = new Color(0f, 0f, 0f, 0.6f) };
        _dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_dim);

        _stage = new Control { Name = "InteriorStageStack", MouseFilter = MouseFilterEnum.Ignore };
        _stage.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_stage);

        _backdrop = new TextureRect
        {
            Name = "InteriorBackdrop",
            MouseFilter = MouseFilterEnum.Ignore,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
        };
        _backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _stage.AddChild(_backdrop);

        _avatarFigure = new TextureRect
        {
            Name = "InteriorAvatarFigure",
            MouseFilter = MouseFilterEnum.Ignore,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(FigureWidth, FigureHeight),
        };
        var avatarTexture = AssetCatalog.PlayerAvatar();
        if (avatarTexture is not null)
        {
            _avatarFigure.Texture = avatarTexture;
        }
        else
        {
            // U13 spec'd the avatar figure but it may not exist yet — a tinted placeholder keeps
            // the figure visible without blocking on the art pipeline (same graceful-degrade
            // pattern used elsewhere in the asset pipeline).
            var placeholder = new ColorRect
            {
                Color = GameTheme.BoneColor.Darkened(0.3f),
                MouseFilter = MouseFilterEnum.Ignore,
            };
            placeholder.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            _avatarFigure.AddChild(placeholder);
        }

        _stage.AddChild(_avatarFigure);
        _avatarFigure.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterBottom, LayoutPresetMode.KeepSize, 30);

        // U22: the embedded, ported LW3 shop choreography — built once, ticking always (mirrors
        // the pre-U22 drawer strip's own lifecycle), visible only while "market" is staged.
        _shopStage = new ShopStage { Name = "InteriorShopStage", Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        _stage.AddChild(_shopStage);
        _shopStage.Build();
        _shopStage.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterTop, LayoutPresetMode.KeepSize, 40);

        _shelfRow = new HBoxContainer { Name = "InteriorShelfRow", MouseFilter = MouseFilterEnum.Ignore };
        _stage.AddChild(_shelfRow);
        _shelfRow.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterTop, LayoutPresetMode.KeepSize, 200);

        _title = new Label { Name = "InteriorTitle" };
        _title.AddThemeFontSizeOverride("font_size", 24);
        _stage.AddChild(_title);
        _title.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft, LayoutPresetMode.KeepSize, 24);

        var panel = new PanelContainer { Name = "InteriorHotspotPanel", CustomMinimumSize = new Vector2(HotspotPanelWidth, 0) };
        _stage.AddChild(panel);
        panel.SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight, LayoutPresetMode.KeepSize, 24);

        _hotspotList = new VBoxContainer { Name = "InteriorHotspotList" };
        panel.AddChild(_hotspotList);

        _exitButton = new Button { Name = "InteriorExit", Text = "Exit", TooltipText = "Return to the street." };
        _exitButton.Pressed += () => RaiseHotspot("exit");
        _stage.AddChild(_exitButton);
        _exitButton.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomRight, LayoutPresetMode.KeepSize, 24);

        _built = true;
    }

    /// <summary>
    /// Stage <paramref name="venueKey"/>'s interior (must be a key in <see cref="Venues"/>):
    /// resolves its backdrop (or degrades gracefully), rebuilds its hotspot buttons from the
    /// declarative table, shows/hides the embedded <see cref="ShopStage"/> ("market" only), and
    /// starts the accumulated-delta push-in. <paramref name="state"/> — when given — refreshes
    /// the shelf-stock icon row for "market" (no-op for every other venue).
    /// <paramref name="seeThrough"/> (3D-interiors MVP) hides the dim/backdrop/avatar-figure
    /// layers so a real 3D room renders underneath while this stage keeps serving ONLY the
    /// hotspot/title/exit overlay (see <see cref="SeeThrough"/>).
    /// </summary>
    public void Open(string venueKey, GameState? state = null, bool seeThrough = false)
    {
        Build();
        if (!Venues.TryGetValue(venueKey, out var spec))
        {
            throw new ArgumentOutOfRangeException(nameof(venueKey), venueKey, "no such interior venue");
        }

        _current = spec;
        SeeThrough = seeThrough;
        _dim.Visible = !seeThrough;
        _backdrop.Visible = !seeThrough;
        _avatarFigure.Visible = !seeThrough;
        _title.Text = spec.Title;
        ApplyBackdrop(spec.BackdropArtId); // still resolved in see-through mode: HasBackdropArt stays meaningful
        RebuildHotspots(spec);

        // The 2D shop strip must NOT draw over a 3D interior room: gate on !seeThrough. In see-through
        // mode a real InteriorRoom3D is behind this overlay, and an unconditional ShopStage plank
        // buried it (the market's tutorial-step-1 "clearly 2D" bug). Classic (opaque) mode unchanged.
        _shopStage.Visible = venueKey == "market" && !seeThrough;
        _shelfRow.Visible = venueKey == "market" && !seeThrough;
        ClearShelfIcons(); // a previous venue's icons (if any) never leak into this one
        if (venueKey == "market" && state is not null)
        {
            PopulateShelfIcons(state);
        }

        Visible = true;
        _stageElapsed = 0;
        ApplyStage(0f);
    }

    /// <summary>Close the stage (no event) — shared by the hotspot/exit/Esc paths below, each of
    /// which raises its own event afterward.</summary>
    public void Close()
    {
        if (_current is null)
        {
            return;
        }

        _current = null;
        Visible = false;
        _stageElapsed = -1;
    }

    /// <summary>Fire a hotspot's action — "exit" closes and raises <see cref="Exited"/>; any
    /// other action closes and raises <see cref="HotspotActivated"/> with that action. Public so
    /// tests can drive it directly (equivalent to "the hotspot button was just pressed"),
    /// mirroring <see cref="GodotClient.Town3d.WorldInput3D.TriggerInteract"/>'s test-friendly
    /// convention.</summary>
    public void RaiseHotspot(string action)
    {
        Close();
        if (action == "exit")
        {
            Exited?.Invoke();
        }
        else
        {
            HotspotActivated?.Invoke(action);
        }
    }

    /// <summary>Advance the camera push-in by one frame's delta — called from
    /// <c>MainUi._Process</c>, the same place <see cref="DrawerHost.Tick"/>/<c>TabFade.Tick</c>
    /// tick (no engine Tween in this codebase). No-op unless a push-in is in flight.</summary>
    public void Tick(double delta)
    {
        if (_stageElapsed < 0)
        {
            return;
        }

        _stageElapsed += delta;
        var t = Mathf.Clamp((float)(_stageElapsed / StageInSeconds), 0f, 1f);
        ApplyStage(t);
        if (t >= 1f)
        {
            _stageElapsed = -1;
        }
    }

    /// <summary>Esc closes the stage (same as pressing the exit hotspot) and marks the event
    /// handled so it never also reaches a world-side cancel handler this same frame (<see
    /// cref="DrawerHost._Input"/> precedent).</summary>
    public override void _Input(InputEvent @event)
    {
        if (!IsOpen)
        {
            return;
        }

        if (@event is InputEventKey { PhysicalKeycode: Key.Escape, Pressed: true })
        {
            RaiseHotspot("exit");
            GetViewport()?.SetInputAsHandled(); // null when this node is not (yet) in the tree (tests)
        }
    }

    /// <summary>
    /// U22 Morning-tick hook (mirrors the pre-U22 <c>ShopPanel.OnPhaseCompleted</c>): stages the
    /// day's shop choreography on the embedded <see cref="ShopStage"/> from THIS tick's
    /// <paramref name="events"/> only (never the whole <c>EventLog</c>) — a no-op on every other
    /// completed phase, and harmless to call whether or not "market" is the venue currently
    /// staged (the embedded stage keeps ticking regardless, same as the pre-U22 drawer strip).
    /// </summary>
    public void OnPhaseCompleted(DayPhase completedPhase, GameState state, IEnumerable<GameEvent> events)
    {
        Build();
        if (completedPhase == DayPhase.Morning)
        {
            _shopStage.QueueDay(state, events);
        }
    }

    /// <summary>Render the player's live shelf (<c>GameState.Player.Shelf</c>) as a row of slot
    /// icons — "shop interior shows shelf stock matching sim state" (U22 test scenario). No-op
    /// while a venue other than "market" is staged.</summary>
    public void RefreshShopStock(GameState state)
    {
        Build();
        ClearShelfIcons();
        if (_current?.VenueKey != "market")
        {
            return;
        }

        PopulateShelfIcons(state);
    }

    private void ClearShelfIcons()
    {
        foreach (var child in _shelfRow.GetChildren().ToList())
        {
            _shelfRow.RemoveChild(child);
            child.Free();
        }
    }

    private void PopulateShelfIcons(GameState state)
    {
        foreach (var entry in state.Player.Shelf)
        {
            if (!state.Items.TryGetValue(entry.Item.Value, out var item))
            {
                continue; // defensive: an un-resolvable id never renders a broken icon (no crash)
            }

            _shelfRow.AddChild(new TextureRect
            {
                Texture = IconRegistry.Slot(item.Slot),
                CustomMinimumSize = new Vector2(32, 32),
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            });
        }
    }

    /// <summary>Camera push-in: <paramref name="t"/> 0 = the establishing (slightly zoomed-out,
    /// faded) frame, 1 = the resting staged frame. Pure accumulated-delta ramp (class doc) — the
    /// dim veil fades in alongside it so the reveal reads as one continuous push, not two
    /// separate pops.</summary>
    private void ApplyStage(float t)
    {
        var scale = Mathf.Lerp(StageStartScale, 1f, t);
        _stage.PivotOffset = Size / 2f;
        _stage.Scale = Vector2.One * scale;
        _stage.Modulate = new Color(1f, 1f, 1f, Mathf.Lerp(0f, 1f, t));
        _dim.Modulate = new Color(1f, 1f, 1f, Mathf.Lerp(0f, 1f, t));
    }

    private void RebuildHotspots(VenueSpec spec)
    {
        foreach (var child in _hotspotList.GetChildren().ToList())
        {
            _hotspotList.RemoveChild(child);
            child.Free();
        }

        _hotspotButtons.Clear();
        foreach (var hotspot in spec.Hotspots)
        {
            var button = new Button
            {
                Name = $"Hotspot_{hotspot.Id}",
                Text = hotspot.Label,
                TooltipText = hotspot.Description,
            };
            button.Pressed += () => RaiseHotspot(hotspot.Action);
            _hotspotList.AddChild(button);
            _hotspotButtons.Add(button);
        }
    }

    private void ApplyBackdrop(string artId)
    {
        var lit = IconRegistry.Lit(artId);
        if (lit is not null)
        {
            _backdrop.Texture = lit;
            HasBackdropArt = true;
        }
        else
        {
            // Graceful degrade (like IconRegistry.Lit itself): forge/tavern/gate interiors are
            // spec-only as of U13 — a generated warm gradient (same GradientTexture2D technique
            // ShopStage uses for its own backdrop degrade) stands in. Never a blank hole.
            _backdrop.Texture = FallbackTexture();
            HasBackdropArt = false;
        }
    }

    private static GradientTexture2D FallbackTexture() => _fallbackTexture ??= new GradientTexture2D
    {
        Gradient = new Gradient
        {
            Colors = [GameTheme.IronColor.Darkened(0.1f), GameTheme.EmberColor.Darkened(0.6f)],
            Offsets = [0f, 1f],
        },
        Width = 8,
        Height = 8,
        Fill = GradientTexture2D.FillEnum.Linear,
        FillFrom = new Vector2(0f, 0f),
        FillTo = new Vector2(0f, 1f),
    };

    /// <summary>KTD10: the declarative venue table — forge/market/tavern/minegate, matching the
    /// world's own venue key values (<see cref="GodotClient.Town3d.Town3D.DoorAnchor"/>). Action
    /// strings match the drawer ids <c>MainUi.OpenPanel</c> already accepts.</summary>
    private static IReadOnlyDictionary<string, VenueSpec> BuildVenueTable()
    {
        VenueSpec[] specs =
        [
            new VenueSpec("forge", "The Forge", "forge-interior",
            [
                new Hotspot("anvil", "Anvil", "Craft gear at the forge.", "Forge"),
            ]),
            new VenueSpec("market", "The Shop", "shop-interior",
            [
                new Hotspot("shelf", "Shelf", "Manage stock and prices.", "Shop"),
            ]),
            new VenueSpec("tavern", "The Tavern", "tavern-interior",
            [
                new Hotspot("board", "Gossip Board", "Catch up on the latest rumors.", "Tavern"),
                new Hotspot("tables", "Hero Tables", "See who is drinking tonight.", "Tavern"),
                new Hotspot("keeper", "Keeper", "Chat with the tavern keeper.", "Tavern"),
            ]),
            new VenueSpec("minegate", "Mine Gate", "gate-interior",
            [
                new Hotspot("board", "Bounty Board", "Post a bounty for the depths.", "Bounties"),
            ]),
        ];
        return specs.ToDictionary(s => s.VenueKey);
    }
}
