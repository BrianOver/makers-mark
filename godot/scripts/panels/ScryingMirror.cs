using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using Godot;

namespace GodotClient.Panels;

/// <summary>
/// U16 (world rework plan, KTD11/KTD13): the expanded spectate surface — what <c>PipDock</c>'s
/// click-to-expand opens. A modal overlay (same lifecycle shape as <c>CampPanel</c>/<c>LedgerModal</c>:
/// code-built, <see cref="ShowMirror"/>/<see cref="CloseMirror"/>, <c>Control.VisibilityChanged</c>
/// wired by <c>MainUi</c> to pause the clock while it's up). PARTY TABS across the top switch which
/// live party's journey the body shows; the body is a floor-progress line plus the party's
/// time-stretched <see cref="JourneyFeed"/> beats, self-censored exactly like <c>MineWatch</c>'s
/// in-panel feed (both read the same <see cref="JourneyStream"/>).
/// </summary>
public partial class ScryingMirror : SimPanel
{
    private readonly JourneyFeed _feed = new();
    private int _selectedPartyKey = int.MinValue;

    private HBoxContainer? _tabRow;
    private Label? _floorLabel;
    private VBoxContainer? _feedBody;

    /// <summary>U5: the provenance popup — a single instance reused across ★ attribution lines,
    /// self-contained (this unit's scope keeps MainUi untouched), added as the LAST child in
    /// <see cref="EnsureBuilt"/> so it draws over the feed body.</summary>
    private ProvenanceCard? _provenance;

    /// <summary>How many parties currently have a live card (test hook).</summary>
    public int PartyCount => _feed.Cards.Count;

    /// <summary>The selected party's index within <see cref="JourneyFeed.Cards"/>, or -1 if none.</summary>
    public int SelectedIndex => _feed.Cards.FindIndex(c => c.PartyKey == _selectedPartyKey);

    /// <summary>The selected party's currently revealed beat lines, in recorded order (test hook —
    /// the same KTD5/AE2 self-censor guarantee <c>MineWatch.CurrentBeats</c> carries).</summary>
    public ImmutableList<string> VisibleBeats =>
        SelectedIndex < 0
            ? ImmutableList<string>.Empty
            : _feed.Revealed(_feed.Cards[SelectedIndex]).Select(b => b.Text).ToImmutableList();

    public override void _Ready() => EnsureBuilt();

    public override void Refresh()
    {
        EnsureBuilt();
        if (Adapter is null)
        {
            return;
        }

        // U16 (KTD11): rebuild this tick's cards even while closed — the moment the player opens
        // the mirror it must show live data, not a stale snapshot from the last time it was open.
        _feed.Refresh(Adapter.CurrentState, Adapter.LastEvents);
        if (!_feed.Cards.Any(c => c.PartyKey == _selectedPartyKey))
        {
            _selectedPartyKey = _feed.Cards.IsEmpty ? int.MinValue : _feed.Cards[0].PartyKey;
        }

        if (Visible)
        {
            Render();
        }
    }

    public override void _Process(double delta)
    {
        if (!Visible)
        {
            return;
        }

        // U25 (a) — investigated, deliberately NOT wired to PhaseClock.Playing (unlike MineWatch/
        // PipDock's matching follow-up): ShowMirror() unconditionally forces Clock.Pause() while
        // open (see OnMirrorVisibilityChanged), so gating this feed on Playing would freeze it the
        // instant it opens — the opposite of "watch the recorded stream while the day waits for
        // you" (pinned by ScryingMirrorTests.MultiParty_TabSwitch_ShowsTheSecondPartysOwnBeats,
        // which forces a reveal via _Process right after ShowMirror()). Always flowing while open
        // is correct here.
        _feed.Advance(delta, paused: false);
        Render();
    }

    public void ShowMirror()
    {
        EnsureBuilt();
        Visible = true;
        Render();
    }

    public void CloseMirror() => Visible = false;

    /// <summary>Select a party tab by index (test hook + tab-button handler).</summary>
    public void SelectParty(int index)
    {
        if (index < 0 || index >= _feed.Cards.Count)
        {
            return;
        }

        _selectedPartyKey = _feed.Cards[index].PartyKey;
        if (Visible)
        {
            Render();
        }
    }

    private void Render()
    {
        Clear(_tabRow!);
        for (var i = 0; i < _feed.Cards.Count; i++)
        {
            var tabCard = _feed.Cards[i];
            var index = i;
            var tab = new Button
            {
                Name = $"MirrorPartyTab_{tabCard.PartyKey}",
                Text = tabCard.Party.IsEmpty ? "Party" : $"Party {tabCard.PartyKey}",
                ToggleMode = true,
                ButtonPressed = tabCard.PartyKey == _selectedPartyKey,
            };
            tab.Pressed += () => SelectParty(index);
            _tabRow!.AddChild(tab);
        }

        var selected = SelectedIndex;
        if (selected < 0)
        {
            _floorLabel!.Text = "No party is underground right now.";
            Clear(_feedBody!);
            return;
        }

        var card = _feed.Cards[selected];
        _floorLabel!.Text = card.Stage == JourneyStage.Rumored
            ? $"Bound for floor {card.TargetFloor} — rumored, not yet underway."
            : $"Floor {card.DeepestFloorCleared}/{card.TargetFloor} — {card.Stage}";

        Clear(_feedBody!);
        var revealed = _feed.Revealed(card);
        if (revealed.IsEmpty)
        {
            AddLabel(_feedBody!, card.Stage == JourneyStage.Rumored
                ? $"A party sets out for floor {card.TargetFloor}…"
                : _feed.IdleLine(card.PartyKey));
            return;
        }

        foreach (var beat in revealed)
        {
            if (beat is { IsAttribution: true, Item: { } itemId })
            {
                // U5: "your craft writes the legends" made touchable — the ★ attribution line
                // opens the item's provenance card on click, instead of a plain label.
                var row = AddRow(_feedBody!);
                var button = AddButton(row, $"AttributionBeat_{itemId.Value}_{beat.Floor}", beat.Text,
                    () => OnShowProvenance(itemId));
                button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                button.Alignment = HorizontalAlignment.Left;
            }
            else
            {
                AddLabel(_feedBody!, beat.Text);
            }
        }

        if (_feed.IsIdle(card))
        {
            AddLabel(_feedBody!, _feed.IdleLine(card.PartyKey));
        }
    }

    /// <summary>U5: open the self-contained provenance popup for an attribution beat's ItemId,
    /// reading live state off <c>Adapter</c> the same way every other click handler here does.</summary>
    private void OnShowProvenance(ItemId itemId)
    {
        if (Adapter is null)
        {
            return;
        }

        EnsureBuilt();
        _provenance!.ShowFor(Adapter.CurrentState, itemId);
    }

    private void EnsureBuilt()
    {
        if (_tabRow is not null)
        {
            return;
        }

        Visible = false;
        SetAnchorsPreset(LayoutPreset.FullRect);

        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.6f) };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(dim);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = Card("MirrorPanel");
        center.AddChild(panel);
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(720, 460) };
        panel.AddChild(box);

        AddHeader(box, "THE SCRYING MIRROR");

        _tabRow = new HBoxContainer { Name = "MirrorPartyTabs" };
        box.AddChild(_tabRow);

        _floorLabel = AddLabel(box, string.Empty);
        _floorLabel.Name = "MirrorFloorLabel";

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        box.AddChild(scroll);
        _feedBody = new VBoxContainer { Name = "MirrorFeedBody", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(_feedBody);

        AddButton(box, "MirrorClose", "Close", CloseMirror);

        // U5: added LAST (after the panel body) so it draws over the feed, self-contained
        // (PKD8-style single overlay), hidden until a ★ attribution line opens it.
        _provenance = new ProvenanceCard { Visible = false };
        AddChild(_provenance);
    }
}
