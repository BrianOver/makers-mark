using System.Linq;
using GameSim.Contracts;
using Godot;

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

    private GridContainer? _venueGrid;
    private MineWatch? _mineWatch;

    /// <summary>The LW5 lit strip (test/tuning hook) — null only before the first <see
    /// cref="_Ready"/>/<see cref="Refresh"/> call builds the panel.</summary>
    public MineWatch? Watch => _mineWatch;

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
        _venueGrid!.AddChild(BuildMineTile(state));
    }

    private Control BuildMineTile(GameState state)
    {
        var card = Card("VenueTile_mine");
        card.CustomMinimumSize = VenueTileSize;
        var body = new VBoxContainer();
        card.AddChild(body);

        var headerRow = AddRow(body);
        var backdropArt = ArtRect(
            AssetCatalog.VenueBackdropId(MineVenueId), new Vector2(BackdropSize, BackdropSize),
            IconRegistry.Glyph("depths"), MineVenueName);
        // Local fix (pre-existing latent defect, discovered by LW5's own screenshot self-verify —
        // see PR notes; scoped here rather than in the shared UiKit.ArtRect since that file is
        // outside this unit's ownership): TextureRect.ExpandMode defaults to KeepSize, so
        // GetCombinedMinimumSize() is max(CustomMinimumSize, the TEXTURE's real pixel size) — a
        // 1024x1024 "mine-backdrop" blows the 120x120 tile out to ~1024px wide, squeezing the
        // standings column to a 1px-wide one-letter-per-line label. IgnoreSize lets the requested
        // <see cref="BackdropSize"/> box actually govern the minimum, as every caller here assumes.
        if (backdropArt is TextureRect textureRect)
        {
            textureRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        }

        headerRow.AddChild(backdropArt);

        var infoCol = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        headerRow.AddChild(infoCol);
        AddHeader(infoCol, MineVenueName + " — deepest floor on record");

        if (state.Drama.DepthsBoard.IsEmpty)
        {
            AddLabel(infoCol, "  (no records yet — the Mine awaits)");
            return card;
        }

        var standings = state.Drama.DepthsBoard
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key);
        foreach (var (heroValue, floor) in standings)
        {
            AddLabel(infoCol, $"  floor {floor} — {HeroName(new HeroId(heroValue))}");
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

        // GridContainer (not a flat VBox): today's single Mine tile fills column 1; a future
        // venue tile (once the sim tracks per-venue records) drops in as another grid child
        // with zero layout rework.
        _venueGrid = new GridContainer
        {
            Name = "VenueGrid",
            Columns = 2,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        body.AddChild(_venueGrid);
    }
}
