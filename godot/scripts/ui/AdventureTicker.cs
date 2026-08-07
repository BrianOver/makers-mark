using System.Collections.Generic;
using System.Linq;
using GameSim.Contracts;
using Godot;

namespace GodotClient.Ui;

/// <summary>
/// U17 (world-rework plan): a single-line ambient story marquee mounted at the HUD's bottom
/// edge (KTD13 — the one bottom-edge element; PiP docks above it, everything else stays in the
/// top bar / top-right objective chip). Reads ONLY the freshly stamped tick events handed to
/// <see cref="OnPhaseCompleted"/> (mirrors <see cref="SimAdapter.LastEvents"/>) — never
/// <see cref="GameState.PendingExpeditions"/> — so it is KTD5-safe BY CONSTRUCTION: the kernel
/// only ever stamps <see cref="HeroDied"/> into the event log at the Evening tick that reveals
/// it, and this ticker has no path to the not-yet-revealed expedition state that holds a death
/// early. The phase check in <see cref="FormatLine"/> is a second, redundant lock — belt-and-
/// suspenders, not the actual mechanism.
///
/// <para>Accumulated-delta marquee (no engine <c>Tween</c> anywhere in this codebase — the
/// <see cref="TabFade"/>/gold-chip-pop convention): <see cref="Tick"/>, called every frame from
/// <c>MainUi._Process</c> exactly like <see cref="TabFade.Tick"/>, advances a scroll offset and
/// wraps the rendered line leftward once it fully clears the strip.</para>
/// </summary>
public partial class AdventureTicker : PanelContainer
{
    /// <summary>Marquee scroll speed, px/sec.</summary>
    public const double ScrollPixelsPerSecond = 48.0;

    /// <summary>Rolling window: lines older than this many completed days are dropped so the
    /// strip never grows unbounded across a long campaign.</summary>
    public const int MaxDaysRetained = 3;

    private const string Separator = "     •     ";

    private readonly List<(int Day, string Text)> _lines = [];
    private Label _label = null!;
    private double _scrollX;

    /// <summary>The label the marquee scrolls (test/inspection seam).</summary>
    public Label Line => _label;

    /// <summary>Every retained (day, text) line, oldest first (test seam).</summary>
    public IReadOnlyList<(int Day, string Text)> Lines => _lines;

    /// <summary>The joined marquee text currently rendered — empty when nothing has ever
    /// qualified (no placeholder noise).</summary>
    public string DisplayText => _label.Text;

    /// <summary>Build the strip. Idempotent-guarded like every other code-built node here.</summary>
    public void Build()
    {
        if (_label is not null)
        {
            return;
        }

        Name = "AdventureTicker";
        ClipContents = true;
        CustomMinimumSize = new Vector2(0, 28);

        _label = new Label
        {
            Name = "AdventureTickerLine",
            AutowrapMode = TextServer.AutowrapMode.Off,
            ClipText = false,
        };
        AddChild(_label);
    }

    /// <summary>
    /// Digest one completed tick's freshly stamped events into day-stamped marquee lines
    /// (R15). Filters to the ambient story surface: item sales, party departures, floor
    /// records, gossip, death (Evening-only — see the class doc), commission lifecycle,
    /// arrivals, the drama director's daily incident, the confidence spiral's edge-triggered
    /// warnings, and (U5(b)) the faction-standing gauge's own edge-triggered threshold crossings.
    /// Unrecognized/irrelevant event types render nothing; a batch
    /// with no qualifying event appends nothing (no placeholder noise). Same-day repeats
    /// (identical formatted text) are deduped — which is also the spam guard, since a
    /// widened allow-list is exactly how a marquee turns into a nag.
    /// </summary>
    public void OnPhaseCompleted(DayPhase completedPhase, int completedDay, GameState state, IEnumerable<GameEvent> events)
    {
        foreach (var evt in events)
        {
            var text = FormatLine(evt, completedPhase, state);
            if (text is null)
            {
                continue;
            }

            if (_lines.Any(l => l.Day == completedDay && l.Text == text))
            {
                continue; // dedupe same-day repeats
            }

            _lines.Add((completedDay, text));
        }

        _lines.RemoveAll(l => l.Day <= completedDay - MaxDaysRetained);
        RefreshLabel();
    }

    /// <summary>Advance the marquee by one frame's delta — called from <c>MainUi._Process</c>
    /// alongside <see cref="TabFade.Tick"/> (no engine Tween in this codebase).</summary>
    public void Tick(double delta)
    {
        if (_label.Text.Length == 0)
        {
            _scrollX = 0;
            _label.Position = Vector2.Zero;
            return;
        }

        _scrollX += ScrollPixelsPerSecond * delta;
        var width = _label.GetCombinedMinimumSize().X;
        if (width > 0 && _scrollX >= width)
        {
            _scrollX %= width;
        }

        _label.Position = new Vector2(-(float)_scrollX, 0);
    }

    private void RefreshLabel()
    {
        _label.Text = string.Join(Separator, _lines.Select(l => $"Day {l.Day}: {l.Text}"));
    }

    private static string? FormatLine(GameEvent evt, DayPhase completedPhase, GameState state) => evt switch
    {
        // U3: the player's gold never moves on a rival sale, so the two must read differently —
        // mirrors EventNarration's FromPlayerShop split (sim/GameSim.Cli/EventNarration.cs).
        ItemSold e when e.FromPlayerShop =>
            $"Your {ItemName(state, e.Item)} sold to {HeroName(state, e.Buyer)} for {e.Price}g.",
        ItemSold e =>
            $"Rival's {ItemName(state, e.Item)} sold to {HeroName(state, e.Buyer)} for {e.Price}g.",
        PartyDeparted e => $"A party of {e.Party.Count} departs for floor {e.TargetFloor}.",
        FloorRecordSet e => $"{HeroName(state, e.Hero)} sets a new depth record — floor {e.Floor}.",
        GossipEmitted e => e.Line,

        // KTD5 defensive guard (redundant with the class doc's structural argument): only ever
        // renders when the Evening tick that reveals the death is the one that just completed.
        HeroDied e when completedPhase == DayPhase.Evening =>
            $"{HeroName(state, e.Hero)} did not return from floor {e.Floor}.",

        // U16 (Wave 4, KTD3): the attribution spotlight — "your blade turned the killing blow" —
        // belongs to the NIGHT homecoming beat, not the Vigil, because AttributionBeatEvent is
        // ONLY ever emitted here (the Evening tick that resolves it — see the class doc's
        // structural argument for HeroDied above, which applies identically). AttributionEngine
        // gates every beat to player-crafted items already (ExpeditionRevealSystem source), so no
        // further PlayerCrafted filter is needed here.
        AttributionBeatEvent e when completedPhase == DayPhase.Evening =>
            $"Home safe: {ItemName(state, e.Item)} — {e.Detail}.",

        // ── Events that fired correctly for months and reached no player-visible surface ────────
        // Everything below was computed by the sim and then dropped by this switch's `_ => null`.
        // Chosen on one test: would a townsperson hear about it? A daily gauge movement would not.

        RecruitArrived e => $"{HeroName(state, e.Hero)} has come to town looking for work.",

        CommissionPosted e =>
            $"{HeroName(state, e.Hero)} wants {e.Slot} work, {e.MinQuality} or better, by day {e.DeadlineDay} " +
            $"— {e.PremiumGold}g over list.",
        CommissionFulfilled e =>
            $"{HeroName(state, e.Hero)} takes delivery of {ItemName(state, e.Item)} — {e.Premium}g premium.",
        CommissionExpired e =>
            $"{HeroName(state, e.Hero)} gave up waiting on that {e.Slot} commission.",

        // The drama director's daily beat. Five authored incidents, so the prose lives here as a
        // client-side display map — DirectorSystem emits a bare snake_case id and no sim-side
        // renderer exists (checked: nothing in Flavor/ or the CLI narrates IncidentFired).
        IncidentFired e => IncidentLine(e),

        // The confidence spiral. All three are edge-triggered — once per crossing, never per day —
        // so they cannot flood the strip.
        RivalExpansionTriggered e =>
            $"The rival stall is expanding — town confidence has slipped to {e.ConfidencePermille / 10}%.",
        HeroConsideringLeaving e =>
            $"{HeroName(state, e.Hero)} is talking about leaving town.",
        TownConfidenceCollapsed e =>
            $"The town has lost faith in its smith — {e.MissedAssessments} assessment(s) missed.",

        // U5(b) (faction-standing plan, R9): the faction standing gauge, edge-triggered exactly
        // like the confidence spiral above. FactionDriftSystem and OreMarketHandlers only ever
        // stamp this event on a threshold CROSSING (FactionStandingThresholds.Crossing) — never on
        // the daily gauge step itself — so this line can never fire from ordinary Morning drift; it
        // passes this file's own admission test ("would a townsperson hear about it? A daily gauge
        // movement would not."). Copy stays scoped to the one mechanism the sim actually runs — a
        // discount rising or fading — not a reputation system it doesn't.
        FactionStandingShifted e => e.Direction == StandingShiftDirection.Favored
            ? $"The {e.FactionName} remember your custom now — their ore comes cheaper."
            : $"The {e.FactionName} are cooling toward your shop — their ore costs more now.",

        // DELIBERATELY still silent here, and why:
        //  • SupplyDelivered — confirmation of the player's OWN camp action, already shown by
        //    CampPanel. Town gossip about a thing you just did reads as noise.
        //  • MarketShareShifted — drifts EVERY day (MarketShareSystem, Evening). It is gauge
        //    material, not news; in a marquee it would crowd out everything above.
        //  • TariffApplied (U5(b)) — the per-purchase price delta that ONE buy's standing-at-the-
        //    time produced. Like SupplyDelivered, this is confirmation of the player's OWN action
        //    (their own buy, already reflected in their own gold total and material count on
        //    screen) rather than town news. The actual news — that the faction's standing itself
        //    crossed a line — is what FactionStandingShifted above already announces; voicing the
        //    per-buy arithmetic too would say the same fact a second time in the same marquee.
        _ => null,
    };

    /// <summary>
    /// Prose for the drama director's five authored incidents. Presentation-only, so it belongs
    /// client-side; the magnitude is folded into the wording rather than stated, since "Severe"
    /// as a bare adjective reads like a debug label.
    /// </summary>
    private static string IncidentLine(IncidentFired e) => e.IncidentId switch
    {
        "whispers_in_the_dark" => "Whispers out of the dark — the miners are uneasy.",
        "goblin_probe" => "Something probed the mine mouth in the night and withdrew.",
        "spider_brood_swells" => "The spider brood is swelling in the upper tunnels.",
        "ghoul_warren_breaks" => "A ghoul warren has broken open deeper down.",
        "the_forgeworm_stirs" => "The forgeworm stirs. The deep rock is warm to the touch.",

        // Unknown id: a new incident landed in DirectorSystem.Catalog without copy here. Say
        // something true rather than nothing, so the gap surfaces in play instead of vanishing.
        _ => $"Word from the {e.VenueId}: {e.IncidentId.Replace('_', ' ')}.",
    };

    private static string ItemName(GameState state, ItemId id) =>
        state.Items.TryGetValue(id.Value, out var item) ? item.Name : $"Item #{id.Value}";

    private static string HeroName(GameState state, HeroId id) =>
        state.Heroes.TryGetValue(id.Value, out var hero) ? hero.Name : $"Hero #{id.Value}";
}
