using System.Threading.Tasks;
using Godot;
using GodotClient.Town2d;

namespace GodotClient.Tools;

/// <summary>
/// Dev-only layout-approval tool: builds <see cref="Town2D"/> and saves PNGs of (a) the whole-town
/// overview and (b) the native player camera view. Run WINDOWED — <c>--headless</c> uses the dummy
/// rendering driver and produces blank images:
/// <code>godot --path godot res://screenshot.tscn</code>
/// Not referenced by the game; harmless dead weight in a normal build.
/// </summary>
public partial class ScreenshotTool : Node
{
    private const string OutDir = "C:/Users/Brian Over/.claude/jobs/624aa73c/tmp/";

    public override async void _Ready()
    {
        var town = new Town2D { Name = "Town2D" };
        town.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(town);
        town.Build(new GodotClient.SimAdapter(seed: 2026));

        await Settle(6);

        // ── Overview: disable the follow-camera and size the viewport to the entire town so the
        //    render captures every building at once (origin-aligned, no camera transform). ──
        int w = TownLayout2D.GridWidth * TownLayout2D.TileSize;
        int h = TownLayout2D.GridHeight * TownLayout2D.TileSize;
        town.Cam.Enabled = false;
        town.WorldViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        town.WorldViewport.Size = new Vector2I(w, h);
        await Settle(4);
        Capture(town, OutDir + "town_overview.png");

        // ── Player view: what the player actually sees — camera on, native 640x360. ──
        town.Cam.Enabled = true;
        town.Cam.MakeCurrent();
        town.WorldViewport.Size = new Vector2I(Town2D.ViewportWidth, Town2D.ViewportHeight);
        await Settle(4);
        Capture(town, OutDir + "town_playerview.png");

        // ── Full game screen: mount the real MainUi (town + HUD), capture the window, then open
        //    each drawer panel to verify the whole composited game renders correctly. ──
        RemoveChild(town);
        town.QueueFree();
        var ui = GD.Load<PackedScene>("res://scenes/panels/main_ui.tscn").Instantiate<GodotClient.MainUi>();
        AddChild(ui);
        await Settle(24);
        CaptureWindow(OutDir + "game_hud.png");
        foreach (var panel in new[] { "Forge", "Shop", "Tavern", "Bounties" })
        {
            ui.OpenPanel(panel);
            await Settle(10);
            CaptureWindow(OutDir + $"game_{panel.ToLower()}.png");
        }

        GD.Print("[screenshot] done");
        GetTree().Quit();
    }

    private void CaptureWindow(string path)
    {
        var image = GetViewport().GetTexture().GetImage();
        var err = image.SavePng(path);
        GD.Print($"[screenshot] {path} -> {err} ({image.GetWidth()}x{image.GetHeight()})");
    }

    private async Task Settle(int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        RenderingServer.ForceDraw();
    }

    private static void Capture(Town2D town, string path)
    {
        var image = town.WorldViewport.GetTexture().GetImage();
        var err = image.SavePng(path);
        GD.Print($"[screenshot] {path} -> {err} ({image.GetWidth()}x{image.GetHeight()})");
    }
}
