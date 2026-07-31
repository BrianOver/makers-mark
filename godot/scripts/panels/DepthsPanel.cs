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

    /// <summary>Fixed tile width (px) — R7 guard: a <see cref="GridContainer"/> column sizes to
    /// its narrowest content unless a cell claims real width up front (the same fixed-
    /// <c>CustomMinimumSize</c> technique <c>HeroesPanel.RosterCardSize</c> uses), so the
    /// standings' autowrap labels never collapse to one character per line.</summary>
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
    private MineWatch? _mineWatch;
    private PhaseClock? _clock;

    /// <summary>The LW5 lit strip (test/tuning hook) — null only before the first <see
    /// cref="_Ready"/>/<see cref="Refresh"/> call builds the panel.</summary>
    public MineWatch? Watch => _mineWatch;

    /// <summary>U25 follow-up (a): forwarded to <see cref="MineWatch.Clock"/> so its journey feed
    /// pauses with the clock. Settable before <see cref="EnsureBuilt"/> has run (MainUi wires this
    /// right after construction) — cached and re-applied once the strip actually exists.</summary>
    public PhaseClock? Clock
    {
        get => _clock;
        set
        {
            _clock = value;
            if (_mineWatch is not null)
            {
                _mineWatch.Clock = value;
            }
        }
    }

    public override void _Ready() => EnsureBuilt();

    public override void Refresh()
    {
        EnsureBuilt();
        if (Adapter is null)
        {
            return;
        }

        var state = Adapter.CurrentState;
        _mineWatch!.Refresh(state, Adapter.LastEvents);

        Clear(_venueGrid!);
        // U-C4: a tile per LIVE venue (the Mine AND Gloomwood, from VenueRegistry.LiveRotation) —
        // the second venue is now real, so the hub is no longer Mine-only.
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
        var backdropArt = ArtRect(
            AssetCatalog.VenueBackdropId(venueId), new Vector2(BackdropSize, BackdropSize),
            IconRegistry.Glyph("depths"), venue.DisplayName);
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
                    .ThenBy(entry => entry.Key);
                foreach (var (heroValue, floor) in standings)
                {
                    AddLabel(infoCol, $"  floor {floor} — {HeroName(new HeroId(heroValue))}");
                }
            }
        }

        return card;
    }

    private void EnsureBuilt()
    {
        if (_venueGrid is not null)
        {
            return;
        }

        // LW5: a VBoxContainer root (not SimPanel.BuildScrollBody's bare FullRect ScrollContainer)
        // so the depths watch strip claims real height ABOVE the scroll instead of the scroll
        // covering the whole panel and the strip overlapping it. The scroll below still fills
        // whatever height the strip doesn't claim (SizeFlagsVertical.ExpandFill).
        var root = new VBoxContainer { Name = "DepthsRoot" };
        root.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(root);

        _mineWatch = new MineWatch();
        root.AddChild(_mineWatch);
        _mineWatch.Build();
        _mineWatch.Clock = _clock;

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
