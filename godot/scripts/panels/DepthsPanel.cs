using System.Linq;
using GameSim.Contracts;
using Godot;

namespace GodotClient.Panels;

/// <summary>
/// The Depths Progress board (R15 display half): each hero's personal deepest-floor
/// record from <see cref="DramaState.DepthsBoard"/>, deepest first. Read-only.
/// </summary>
public partial class DepthsPanel : SimPanel
{
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
        var header = AddRow(_content!);
        AddIcon(header, IconRegistry.Glyph("depths"));
        AddHeader(header, "DEPTHS PROGRESS BOARD — deepest floor on record");

        if (state.Drama.DepthsBoard.IsEmpty)
        {
            AddLabel(_content!, "  (no records yet — the Mine awaits)");
            return;
        }

        var standings = state.Drama.DepthsBoard
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key);
        foreach (var (heroValue, floor) in standings)
        {
            AddLabel(_content!, $"  floor {floor} — {HeroName(new HeroId(heroValue))}");
        }
    }

    private void EnsureBuilt()
    {
        if (_content is not null)
        {
            return;
        }

        var body = BuildScrollBody();
        _content = new VBoxContainer { Name = "DepthsContent" };
        body.AddChild(_content);
    }
}
