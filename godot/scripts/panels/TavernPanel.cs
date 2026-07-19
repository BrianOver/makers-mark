using System.Linq;
using GameSim.Contracts;
using Godot;

namespace GodotClient.Panels;

/// <summary>
/// The tavern gossip feed (R14 display half): <see cref="GossipEmitted"/> lines,
/// most recent first, scrollback capped at <see cref="ScrollbackLines"/>.
/// Every line the sim emits already cites a real event — we only render.
///
/// <para>P007 polish (KTD2/KTD3): recomposed around one <see cref="UiKit.Section"/> ("TAVERN
/// GOSSIP") holding a themed <see cref="Card"/> per line — a gossip-glyph <see cref="ArtRect"/>
/// (gossip has no per-line generated art concept, so this always exercises the KTD3 fallback
/// placeholder: <see cref="IconRegistry.Glyph"/>'s hand-authored "gossip" SVG, never a blank
/// hole) plus the day-stamped quote. The sim read (<see cref="GossipEmitted"/> off
/// <c>state.EventLog</c>, newest-first, capped at <see cref="ScrollbackLines"/>) is unchanged
/// from the pre-polish panel — only the visual composition changed.</para>
/// </summary>
public partial class TavernPanel : SimPanel
{
    public const int ScrollbackLines = 50;

    /// <summary>Gossip-card icon tile edge length (px) — a small chip weight, matching
    /// <c>ForgePanel</c>'s talent rune icon.</summary>
    private const float GossipIconSize = 40f;

    /// <summary>Art key probed for a gossip card's icon — deliberately never generated (gossip
    /// has no per-line art concept), so <see cref="ArtRect"/> always renders its themed
    /// fallback (glyph + caption) rather than a blank hole.</summary>
    private const string GossipArtKey = "tavern-gossip-line";

    private VBoxContainer? _content;

    public override void _Ready() => EnsureBuilt();

    public override void Refresh()
    {
        EnsureBuilt();
        if (Adapter is null)
        {
            return;
        }

        var state = Adapter.CurrentState;
        Clear(_content!);

        var section = Section("TAVERN GOSSIP");
        _content!.AddChild(section.Root);

        var lines = state.EventLog.OfType<GossipEmitted>().TakeLast(ScrollbackLines).Reverse().ToList();
        if (lines.Count == 0)
        {
            AddLabel(section.Body, "  (the tavern is quiet — come back after an expedition)");
            return;
        }

        for (var i = 0; i < lines.Count; i++)
        {
            var gossip = lines[i];
            var card = Card($"GossipCard_{i}");
            section.Body.AddChild(card);

            var row = AddRow(card);
            row.AddChild(ArtRect(
                GossipArtKey, new Vector2(GossipIconSize, GossipIconSize), IconRegistry.Glyph("gossip"), "gossip"));
            AddLabel(row, $"  [day {gossip.Day}] \"{gossip.Line}\"");
        }
    }

    private void EnsureBuilt()
    {
        if (_content is not null)
        {
            return;
        }

        var body = BuildScrollBody();
        _content = new VBoxContainer { Name = "TavernContent" };
        body.AddChild(_content);
    }
}
