using System.Threading.Tasks;
using GameSim.Professions;
using Godot;

namespace GodotClient.Tools;

/// <summary>
/// Measures the shipped client's actual layout geometry and reports anything that overflows the
/// window. Written because the five-run playtest screenshots showed the HUD row clipped ("Act I"
/// cut off, the right-most tray icon sliced) and the drawer running past the right edge — and
/// eyeballing a PNG cannot tell you WHICH control is too wide or by how much.
///
/// <para>Walks the real MainUi tree and prints every <see cref="Control"/> whose right or bottom
/// edge lands outside the viewport, so a layout defect becomes a number instead of an impression.
/// Run windowed: <c>godot --path godot res://layoutprobe.tscn</c>.</para>
/// </summary>
public partial class LayoutProbe : Node
{
    public override async void _Ready()
    {
        DevToolAudio.Silence(); // automated runs stay silent — see DevToolAudio
        var select = GD.Load<PackedScene>("res://scenes/new_game_select.tscn").Instantiate<NewGameSelect>();
        string? requested = null;
        select.SceneChange = p => requested = p;
        AddChild(select);
        await Settle(8);
        Press(select, $"Pick_{ProfessionRegistry.BlacksmithId}");
        await Settle(6);
        Press(select, "Begin");
        await Settle(4);
        RemoveChild(select);
        select.QueueFree();

        var ui = GD.Load<PackedScene>(requested ?? "res://scenes/panels/main_ui.tscn").Instantiate<MainUi>();
        AddChild(ui);
        await Settle(24);

        var vp = GetViewport().GetVisibleRect().Size;
        GD.Print($"[probe] viewport = {vp.X} x {vp.Y}");
        GD.Print($"[probe] window   = {DisplayServer.WindowGetSize()}");
        GD.Print($"[probe] DrawerHost.DrawerWidth const = {Ui.DrawerHost.DrawerWidth}");

        GD.Print("[probe] --- TOWN (drawer closed) ---");
        Walk(ui, vp, 0);

        foreach (var panel in new[] { "Forge", "Shop", "Bounties", "Heroes" })
        {
            ui.OpenPanel(panel);
            await Settle(10);
            GD.Print($"[probe] --- PANEL {panel} ---");
            Walk(ui, vp, 0);
        }

        GD.Print("[probe] done");
        GetTree().Quit();
    }

    /// <summary>Report every control that pokes outside the viewport. Depth-limited output: only
    /// offenders are printed, so the signal is the overflow list itself.</summary>
    private static void Walk(Node node, Vector2 vp, int depth)
    {
        if (node is Control c && c.Visible)
        {
            var r = c.GetGlobalRect();
            var overRight = r.End.X - vp.X;
            var overBottom = r.End.Y - vp.Y;
            if (overRight > 1f || overBottom > 1f || r.Position.X < -1f)
            {
                GD.Print($"[probe]   OVERFLOW {c.GetType().Name} '{c.Name}' " +
                         $"rect=({r.Position.X:F0},{r.Position.Y:F0} {r.Size.X:F0}x{r.Size.Y:F0}) " +
                         $"right+{overRight:F0} bottom+{overBottom:F0}");
            }
        }

        if (depth > 14)
        {
            return;
        }

        foreach (var child in node.GetChildren())
        {
            Walk(child, vp, depth + 1);
        }
    }

    private static void Press(Node root, string name)
    {
        if (root.FindChild(name, recursive: true, owned: false) is Button b)
        {
            b.EmitSignal(BaseButton.SignalName.Pressed);
        }
        else
        {
            GD.Print($"[probe] button not found: {name}");
        }
    }

    private async Task Settle(int frames)
    {
        for (var i = 0; i < frames; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        RenderingServer.ForceDraw();
    }
}
