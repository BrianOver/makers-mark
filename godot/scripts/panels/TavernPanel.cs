using System.Linq;
using GameSim.Contracts;
using Godot;

namespace GodotClient.Panels;

/// <summary>
/// The tavern gossip feed (R14 display half): <see cref="GossipEmitted"/> lines,
/// most recent first, scrollback capped at <see cref="ScrollbackLines"/>.
/// Every line the sim emits already cites a real event — we only render.
/// </summary>
public partial class TavernPanel : SimPanel
{
    public const int ScrollbackLines = 50;

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
        AddHeader(_content!, "TAVERN GOSSIP");

        var lines = state.EventLog.OfType<GossipEmitted>().TakeLast(ScrollbackLines).Reverse().ToList();
        if (lines.Count == 0)
        {
            AddLabel(_content!, "  (the tavern is quiet — come back after an expedition)");
            return;
        }

        foreach (var gossip in lines)
        {
            AddLabel(_content!, $"  [day {gossip.Day}] \"{gossip.Line}\"");
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
