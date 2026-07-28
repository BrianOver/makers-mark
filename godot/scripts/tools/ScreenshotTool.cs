using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GameSim.Advisor;
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

        // ── Action-driven playtest: drive real gameplay actions through the sim (via the legal-action
        //    list, so nothing is ever rejected) and capture + log the game's response at each stage.
        //    Auto-advance is OFF by default, so manual AdvancePhase doesn't race an auto clock. ──
        var adapter = ui.Adapter;
        var log = new StringBuilder();

        void LogState(string label)
        {
            var s = adapter.CurrentState;
            int mats = s.Player.Materials.Sum(kv => kv.Value);
            int alive = s.Heroes.Values.Count(h => h.Alive);
            log.AppendLine($"[{label}] day={s.Day} phase={s.Phase} gold={s.Player.Gold} " +
                           $"materials={mats} items={s.Items.Count} heroesAlive={alive}");
        }

        int QueueByName(string contains, int max)
        {
            int n = 0;
            foreach (var a in ActionLegality.LegalActions(adapter.CurrentState, adapter.CurrentState.Phase))
            {
                if (n >= max) break;
                if (a.GetType().Name.Contains(contains)) { adapter.Queue(a); log.AppendLine($"  queued {a.GetType().Name}"); n++; }
            }
            if (n == 0) log.AppendLine($"  (no legal {contains} this phase)");
            return n;
        }

        LogState("start");
        ui.OpenPanel("Forge"); await Settle(8);
        QueueByName("AcceptCommission", 2);
        QueueByName("BuyMaterial", 3);
        adapter.AdvancePhase(); await Settle(10);            // apply Morning buys, advance to Expedition
        LogState("after-buy"); CaptureWindow(OutDir + "ap_1_afterbuy.png");

        QueueByName("Craft", 1); QueueByName("Stock", 2);
        adapter.AdvancePhase(); await Settle(10);            // apply craft/stock
        LogState("after-craft"); CaptureWindow(OutDir + "ap_2_aftercraft.png");

        // Advance through the rest of the day; capture the town mid-run (heroes marching) + the ledger.
        for (int i = 0; i < 5; i++)
        {
            QueueByName("Stock", 1);
            adapter.AdvancePhase(); await Settle(8);
            LogState($"advance-{i}");
            if (i == 0)
            {
                CaptureWindow(OutDir + "ap_3_expedition.png");
                // Watch the adventure: open the Depths/delve panel while a party is in the Mine and
                // pump frames so the delve-stage playhead reveals combat beats.
                ui.OpenPanel("Depths");
                await Settle(200);
                CaptureWindow(OutDir + "ap_delve.png");
            }
        }
        CaptureWindow(OutDir + "ap_4_dayend.png");

        System.IO.File.WriteAllText(OutDir + "action_playtest_log.txt", log.ToString());
        GD.Print("[screenshot] action-playtest log:\n" + log);
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
