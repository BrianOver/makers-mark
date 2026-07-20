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
    /// records, gossip, and death (Evening-only — see the class doc). Unrecognized/irrelevant
    /// event types render nothing; a batch with no qualifying event appends nothing (no
    /// placeholder noise). Same-day repeats (identical formatted text) are deduped.
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
        ItemSold e => $"{ItemName(state, e.Item)} sold to {HeroName(state, e.Buyer)} for {e.Price}g.",
        PartyDeparted e => $"A party of {e.Party.Count} departs for floor {e.TargetFloor}.",
        FloorRecordSet e => $"{HeroName(state, e.Hero)} sets a new depth record — floor {e.Floor}.",
        GossipEmitted e => e.Line,

        // KTD5 defensive guard (redundant with the class doc's structural argument): only ever
        // renders when the Evening tick that reveals the death is the one that just completed.
        HeroDied e when completedPhase == DayPhase.Evening =>
            $"{HeroName(state, e.Hero)} did not return from floor {e.Floor}.",

        _ => null,
    };

    private static string ItemName(GameState state, ItemId id) =>
        state.Items.TryGetValue(id.Value, out var item) ? item.Name : $"Item #{id.Value}";

    private static string HeroName(GameState state, HeroId id) =>
        state.Heroes.TryGetValue(id.Value, out var hero) ? hero.Name : $"Hero #{id.Value}";
}
