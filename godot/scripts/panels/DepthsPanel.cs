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

    public override void _Ready() => EnsureBuilt();

    public override void Refresh()
    {
        EnsureBuilt();
        if (Adapter is null)
        {
            return;
        }

        var state = Adapter.CurrentState;
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
        headerRow.AddChild(ArtRect(
            AssetCatalog.VenueBackdropId(MineVenueId), new Vector2(BackdropSize, BackdropSize),
            IconRegistry.Glyph("depths"), MineVenueName));

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

        var body = BuildScrollBody();
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
