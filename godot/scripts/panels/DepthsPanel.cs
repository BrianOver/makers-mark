using System;
using System.Linq;
using GameSim.Contracts;
using Godot;
using GodotClient.Ui;

namespace GodotClient.Panels;

/// <summary>
/// The venue-map hub (P007 U6, R12/KTD3/KTD4): each hero's personal deepest-floor record from
/// <see cref="DramaState.DepthsBoard"/>, deepest first, framed inside a venue tile with a
/// backdrop <see cref="UiKit.ArtRect"/>. Read-only.
///
/// <para><b>Why one tile.</b> Confirmed before building this unit (KTD5's "do not invent sim
/// reads" guardrail): <see cref="DramaState"/> exposes exactly ONE venue-scoped record —
/// <see cref="DramaState.DepthsBoard"/> — a single deepest-floor-per-hero board with NO
/// per-venue split (a hero's entry is their all-time deepest floor across raids, not "deepest
/// floor in venue X"). There is no broader venue/floor state on <see cref="DramaState"/> to
/// enumerate tiles from. The hub therefore renders the one venue-of-record the board's data
/// actually belongs to today — <see cref="MineVenueId"/>, the sim's only live venue
/// (<c>VenueRegistry.LiveRotation</c>) — as a single backdrop tile holding the board's
/// standings. A richer per-venue split (Gloomwood, Sunken Crypt, …) is a follow-up once the sim
/// tracks records per venue, per the plan's own execution note.</para>
///
/// <para><b>LW5 depths watch.</b> <see cref="Watch"/> is a lit <see cref="MineWatch"/> strip
/// mounted above the venue grid (own <see cref="VBoxContainer"/> root, not
/// <see cref="SimPanel.BuildScrollBody"/>'s FullRect scroll, so the strip claims real layout
/// height above the venue tiles instead of overlapping them) — live only while a party is
/// underground, collapsed to zero height otherwise. Refreshed every tick alongside the venue
/// grid; degrades to fully inert (never shown) if its art is missing, so this panel's own
/// pre-LW5 behavior is unchanged either way.</para>
///
/// <para><b>U9 (world-and-interiors plan, KTD-4).</b> This panel no longer constructs its own
/// <see cref="MineWatch"/> — it is now a SINGLE shared instance <c>MainUi</c> owns and refreshes
/// every tick regardless of host (constraint 4: never two live SubViewports). This panel is the
/// strip's RESTING host: <see cref="MountWatch"/> parks it as the first child of the panel's own
/// root VBox, exactly where it always sat, whenever <c>ScryingMirror</c> is not borrowing it.
/// <see cref="Watch"/> reads the actual current child rather than a cached field, so it always
/// answers "is the strip here right now", never a stale handle from before it was lent out.</para>
/// </summary>
public partial class DepthsPanel : SimPanel
{
    /// <summary>The sim's one live venue id (<c>VenueRegistry.MineId</c>) — matches the
    /// <c>AssetCatalog.VenueBackdropId</c>/<c>VenueEntranceId</c> naming convention without this
    /// presentation-only panel taking a new dependency on <c>GameSim.Venues</c>.</summary>
    private const string MineVenueId = "mine";
    private const string MineVenueName = "The Mine";

    /// <summary>Venue backdrop tile edge length (px) — sized to read as a map tile, not a
    /// portrait (<see cref="UiKit.PortraitSize"/> is smaller, for hero figures).</summary>
    private const float BackdropSize = 120f;

    /// <summary>MINIMUM tile width (px) — R7 guard: a <see cref="GridContainer"/> column sizes to
    /// its narrowest content unless a cell claims real width up front (the same fixed-
    /// <c>CustomMinimumSize</c> technique <c>HeroesPanel.RosterCardSize</c> uses), so the
    /// standings' autowrap labels never collapse to one character per line.
    ///
    /// <para>MINIMUM only, not fixed (visfix4): a card also carries
    /// <see cref="Control.SizeFlags.ExpandFill"/> now (see <see cref="BuildVenueTile"/>), so once
    /// this floor is met the <see cref="GridContainer"/> stretches the card to fill whatever's left
    /// of the drawer's real width. Before that flag, one column at 360px inside a 600px
    /// <see cref="DrawerHost.DrawerWidth"/> drawer left a ~240px dead column down the right —
    /// visible in the design-pass shot at runs/shots-2026-09-13/Watch.png.</para></summary>
    private static readonly Vector2 VenueTileSize = new(360f, 0f);

    /// <summary>
    /// How many <see cref="VenueTileSize"/> columns fit in <paramref name="availableWidth"/> — at least
    /// one, however narrow the container is.
    ///
    /// <para>Internal so a test can pin the arithmetic directly rather than inferring it from a laid-out
    /// rect: the bug this replaces was a hardcoded column count that overflowed by 124px, and a guard that
    /// can only see the symptom would not have caught it before the tiles existed to overflow.</para>
    /// </summary>
    internal static int ColumnsThatFit(float availableWidth) =>
        Math.Max(1, (int)(availableWidth / VenueTileSize.X));

    private GridContainer? _venueGrid;
    private VBoxContainer? _root;

    /// <summary>P2-ONBOARD-02: the "read-only-surfaces" once-ever caption, a sibling of <see
    /// cref="_venueGrid"/> — Refresh() only ever Clears the grid itself, so this survives every
    /// rebuild once <see cref="ShowHeaderCaption"/> sets it. Owner ruling 2026-09-15: retired (see
    /// <see cref="_Notification"/>) the next time this panel is genuinely re-entered, reclaiming
    /// its own reserved height for the venue grid below it.</summary>
    private Label? _caption;

    /// <summary>The strip currently mounted here (test/tuning hook) — null while <see
    /// cref="ScryingMirror"/> has borrowed it instead (see <see cref="MountWatch"/>/<c>MainUi.Watch</c>).
    /// Computed off the actual current child rather than a cached field, so it can never answer
    /// stale (U9, KTD-4).</summary>
    public MineWatch? Watch => _root?.GetChildren().OfType<MineWatch>().FirstOrDefault();

    public override void _Ready() => EnsureBuilt();

    /// <summary>
    /// Owner ruling, 2026-09-15 ("two fold budgets the 481px ruling could not close"): the
    /// once-ever caption below used to reserve its measured height (~79px) forever once shown —
    /// <see cref="ShowHeaderCaption"/> sets <see cref="Label.Visible"/> true exactly once per
    /// campaign and nothing ever set it back, so every LATER visit to this panel paid the same
    /// 79px the first-ever reader earned by reading it once. Retiring it here, on the panel's own
    /// re-entry, keys on nothing but that same fact: this fires when <see cref="_caption"/> is
    /// hidden anyway (a fresh campaign, or this panel was never the one that showed it) it is a
    /// no-op, and when it is genuinely visible from a PRIOR open, it collapses back to zero height
    /// — never a second parallel "has this been read" flag alongside <see
    /// cref="TutorialFlow.ConsumeFirstTouch"/>'s own once-ever ledger.
    ///
    /// <para><b>Why re-entry, not <see cref="Refresh"/>.</b> <c>MainUi.RefreshAll</c> calls <see
    /// cref="Refresh"/> every tick this panel stays the OPEN one — including while a party is
    /// live underground, which is the entire point of <see cref="MineWatch"/> ticking here — so
    /// retiring on every <see cref="Refresh"/> would erase the caption within the same visit, the
    /// instant the next tick landed, long before a player had actually read it. <see
    /// cref="DrawerHost.Open"/>/<see cref="DrawerHost.Close"/> instead toggle THIS control's own
    /// <see cref="CanvasItem.Visible"/> exactly on a real navigation (opening a different panel, or
    /// leaving to the bare world, then coming back) — <see cref="NotificationVisibilityChanged"/>
    /// fires synchronously off that transition, strictly BEFORE <c>MainUi.OpenPanel</c>'s own
    /// <see cref="Refresh"/>/<see cref="TutorialFlow.ConsumeFirstTouch"/> calls run for the open
    /// that triggered it, so a genuine first-ever open (caption not yet shown) is untouched and
    /// still gets shown a moment later.</para>
    /// </summary>
    public override void _Notification(int what)
    {
        if (what == NotificationVisibilityChanged && IsVisibleInTree() && _caption is { Visible: true })
        {
            _caption.Visible = false;
        }
    }

    /// <summary>P2-ONBOARD-02: <c>MainUi</c> calls this the ONE time <see
    /// cref="TutorialFlow.ConsumeFirstTouch"/> ever returns the "read-only-surfaces" text for this
    /// campaign — replaces the old floating <see cref="MentorBanner"/> popup that used to fire the
    /// instant this panel opened.</summary>
    public void ShowHeaderCaption(string text)
    {
        EnsureBuilt();
        _caption!.Text = text;
        _caption.Visible = true;
    }

    /// <summary>
    /// U9 (world-and-interiors plan, KTD-4): accept the single shared <see cref="MineWatch"/>
    /// instance, stealing it from wherever it currently sits (constraint 4 — exactly one parent,
    /// ever) and parking it as this panel's FIRST child so it claims real layout height above the
    /// venue grid, exactly the position it built itself into before this panel's own construction
    /// owned it. Called by <c>MainUi</c> once at boot (the strip's resting default) and again
    /// every time <see cref="ScryingMirror"/> hands it back on close.
    /// </summary>
    public void MountWatch(MineWatch watch)
    {
        EnsureBuilt();
        if (watch.GetParent() != _root)
        {
            watch.GetParent()?.RemoveChild(watch);
            _root!.AddChild(watch);
            _root!.MoveChild(watch, 0);
        }

        // Depths is a DRAWER, not a Clock-pausing modal — restore the strip's normal
        // "respect an actual player pause" contract (U25), which ScryingMirror force-overrides
        // for as long as it borrows the strip (see MineWatch.ForceRevealWhilePaused's own doc).
        watch.ForceRevealWhilePaused = false;
    }

    public override void Refresh()
    {
        EnsureBuilt();
        if (Adapter is null)
        {
            return;
        }

        var state = Adapter.CurrentState;

        Clear(_venueGrid!);
        // U-C4: a tile per LIVE venue, straight from VenueRegistry.LiveRotation — Mine, Gloomwood,
        // and Sunken Crypt after the T1 flip. Every live tile renders REAL committed backdrop art
        // (VenueHubTests pins zero fallbacks on this panel); Emberfall stays dormant in the
        // registry until its art lands, precisely so this grid never shows a glyph where a
        // dungeon should be.
        foreach (var venueId in GameSim.Venues.VenueRegistry.LiveRotation)
        {
            _venueGrid!.AddChild(BuildVenueTile(state, venueId));
        }
    }

    private Control BuildVenueTile(GameState state, string venueId)
    {
        var venue = GameSim.Venues.VenueRegistry.Require(venueId);
        var card = Card($"VenueTile_{venueId}");
        card.CustomMinimumSize = VenueTileSize;
        // visfix4: ExpandFill lets the GridContainer grow this card past its VenueTileSize floor
        // to actually use the drawer's real width instead of stranding a dead column beside it
        // (see VenueTileSize's own remarks).
        card.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var body = new VBoxContainer();
        card.AddChild(body);

        var headerRow = AddRow(body);
        // Caption restored (MineVenueName): on a manifest MISS this is the ONLY place the
        // placeholder's caption comes from — dropping it would show the raw asset key instead of
        // the venue name. On a HIT it also renders under the backdrop now, alongside the header
        // label beside it — redundant, never wrong. The ExpandMode=IgnoreSize fix for the
        // 1024x1024 "mine-backdrop" blowing the 120x120 tile out to ~1024px wide (discovered by
        // LW5's own screenshot self-verify, PR #119) now lives centrally in UiKit.ArtRect instead
        // of patched locally here — see UiKit.ArtRect's own remarks.
        //
        // visfix4: KeepAspectCovered, not the ArtRect default KeepAspectCentered. Every committed
        // venue backdrop (mine-/gloomwood-/sunkencrypt-/emberfall-backdrop.png, checked directly)
        // is 1024x260 — a wide banner, not a portrait-ish square. Centered-and-contained inside
        // this square BackdropSize box scaled that down to a ~120x30 sliver with dead space above
        // and below (the design-pass shot at runs/shots-2026-09-13/Watch.png), reading as a broken
        // image. Covered crops instead of letterboxing, so the box always shows a real, full-bleed
        // slice of the art.
        var backdropArt = ArtRect(
            AssetCatalog.VenueBackdropId(venueId), new Vector2(BackdropSize, BackdropSize),
            IconRegistry.Glyph("depths"), venue.DisplayName,
            stretchMode: TextureRect.StretchModeEnum.KeepAspectCovered);
        headerRow.AddChild(backdropArt);

        var infoCol = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        headerRow.AddChild(infoCol);
        AddHeader(infoCol, venue.DisplayName);

        // U-C3: den escalation — the venue's threat tier and lockdown, surfaced so the drama the
        // director builds up is legible instead of invisible. No entry = untouched/quiet.
        var threat = state.Venues.TryGetValue(venueId, out var vs) ? vs : null;
        var tier = threat?.ThreatTier ?? 0;
        var threatLine = threat is { Closed: true }
            ? "  ⚠ locked down — the den has overrun the routes here"
            : tier <= 0
                ? "  den: quiet"
                : $"  den threat: tier {tier}" + (threat!.InfectionPerMille > 0 ? $" ({threat.InfectionPerMille / 10}%)" : "");
        AddLabel(infoCol, threatLine);

        // U-C4: who is raiding this venue right now (from the in-flight expeditions' venue key).
        var partiesHere = state.InFlight.Count(x => x.VenueId == venueId);
        if (partiesHere > 0)
        {
            AddLabel(infoCol, $"  {partiesHere} part{(partiesHere == 1 ? "y" : "ies")} raiding now");
        }

        // The deepest-floor board is a GLOBAL per-hero record (not venue-split, KTD5) — shown under
        // the Mine, the venue-of-record it historically belongs to; other venues show live activity.
        if (venueId == MineVenueId)
        {
            if (state.Drama.DepthsBoard.IsEmpty)
            {
                AddLabel(infoCol, "  (no records yet — the Mine awaits)");
            }
            else
            {
                var standings = state.Drama.DepthsBoard
                    .OrderByDescending(entry => entry.Value)
                    .ThenBy(entry => entry.Key)
                    .ToList();

                // 481px re-lay (owner ruling 2026-09-14): a UiKit.Disclosure. A full roster puts six
                // rows in this tile, and this tile sits under a MineWatch strip that claims 260 of
                // the drawer's 481px while a party is underground — so on the one day the panel is
                // most worth opening, the board pushed the venue tile's own content past the fold.
                //
                // P2-PEOPLE-01 is why the DEEPEST row is the summary rather than a count: that row
                // is the game's own "Torvald — floor 3", the durable fact that never goes away, so
                // it stays on screen verbatim — caption and all — with the body shut. The body
                // still holds every row, this one included, so opening shows exactly what the old
                // always-expanded list showed.
                var board = UiKit.Disclosure("Deepest Floors");
                infoCol.AddChild(board.Root);
                foreach (var (heroValue, floor) in standings)
                {
                    var name = HeroName(new HeroId(heroValue));
                    AddLabel(board.Body, $"  floor {floor} — {name}{GodotClient.Ui.ArcScenes.FloorCaption(name, floor)}");
                }

                var (deepestHero, deepestFloor) = standings[0];
                var deepestName = HeroName(new HeroId(deepestHero));
                board.Summary.Text =
                    $"floor {deepestFloor} — {deepestName}{GodotClient.Ui.ArcScenes.FloorCaption(deepestName, deepestFloor)}";
            }
        }

        return card;
    }

    private void EnsureBuilt()
    {
        if (_root is not null)
        {
            return;
        }

        // LW5: a VBoxContainer root (not SimPanel.BuildScrollBody's bare FullRect ScrollContainer)
        // so the depths watch strip claims real height ABOVE the scroll instead of the scroll
        // covering the whole panel and the strip overlapping it. The scroll below still fills
        // whatever height the strip doesn't claim (SizeFlagsVertical.ExpandFill).
        //
        // U9 (KTD-4): no longer constructs a MineWatch here — MainUi mounts the single shared
        // instance into this VBox's first child slot via MountWatch, once _root exists.
        var root = new VBoxContainer { Name = "DepthsRoot" };
        root.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(root);
        _root = root;

        // Horizontal scroll disabled (U7/R7 precedent — BuildScrollBody's own reasoning): with it
        // enabled the child gets unbounded horizontal space, so autowrap labels lose their real
        // wrap width. Vertical-only, same as every other panel's scroll body.
        var scroll = new ScrollContainer
        {
            Name = "Scroll",
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        root.AddChild(scroll);

        var body = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        scroll.AddChild(body);

        // visfix4: the caption used to be a bare Label dropped straight into `body` at 0px left
        // inset, so its text ran under the drawer's own border (design-pass shot,
        // runs/shots-2026-09-13/Watch.png). Everything else on this drawer gets an inset for
        // free because it sits inside its OWN bare PanelContainer — DrawerHeader's title strip,
        // and each venue tile below via Card() — which picks up the theme's default "panel"
        // StyleBox (GameTheme.PanelStyle()) and its ContentMarginLeft/Right. Read that same
        // property (not a re-typed literal) so the caption lines up with both.
        var captionHost = new MarginContainer { Name = "CaptionInset" };
        var panelInset = (int)GameTheme.PanelStyle().ContentMarginLeft;
        captionHost.AddThemeConstantOverride("margin_left", panelInset);
        captionHost.AddThemeConstantOverride("margin_right", panelInset);
        _caption = UiKit.OnceEverCaption();
        captionHost.AddChild(_caption);
        body.AddChild(captionHost);

        // GridContainer (not a flat VBox): venue tiles drop in as grid children with no layout rework.
        //
        // Columns are DERIVED from the space available, never asserted. A hardcoded 2 was the bug behind
        // "depths menu is cut off still": two VenueTileSize columns demand 724px inside a 600px drawer,
        // and because a Control cannot lay out narrower than its combined minimum size, the whole panel
        // was forced past the drawer's right edge — where anchors cannot save it and the vertical-only
        // ScrollContainer cannot reach it. With a second venue now live (the Gloomwood) it was visibly
        // truncated. Deriving the count keeps the original intent — widen the drawer and the second
        // column comes back by itself — while making the overflow arithmetically impossible.
        _venueGrid = new GridContainer
        {
            Name = "VenueGrid",
            Columns = ColumnsThatFit(DrawerHost.DrawerWidth),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        body.AddChild(_venueGrid);
    }
}
