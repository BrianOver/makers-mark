using System.Threading.Tasks;
using GameSim.Advisor;
using GameSim.Professions;
using Godot;

namespace GodotClient.Tools;

/// <summary>
/// REAL-launch playtest (not a harness): boots the actual <c>new_game_select</c> front door, presses
/// the real profession + Begin buttons (real signal handlers → real <c>GameComposition.NewCampaign</c>
/// + <c>MainUi.AdapterOverride</c>), mounts the real MainUi, then plays the loop through real controls
/// (building pick, Buy, Advance) — capturing the running WINDOW at each stage. Run WINDOWED
/// (<c>--headless</c> renders blank): <c>godot --path godot res://realplaytest.tscn</c>.
///
/// The only synthetic bit is that <c>NewGameSelect.SceneChange</c> is stubbed so the driver survives
/// to keep screenshotting — the campaign creation + AdapterOverride + MainUi boot are the real thing.
/// </summary>
public partial class RealPlaytest : Node
{
    private const string OutDir = "C:/Users/Brian Over/.claude/jobs/624aa73c/tmp/pt/";

    public override async void _Ready()
    {
        System.IO.Directory.CreateDirectory(OutDir);

        // ── 1. Real new-game front door ──
        var select = GD.Load<PackedScene>("res://scenes/new_game_select.tscn").Instantiate<NewGameSelect>();
        string? requestedScene = null;
        select.SceneChange = path => requestedScene = path;   // stub the tear-down; keep driving
        AddChild(select);
        await Settle(8);
        Shot("00_picker");   // the actual profession picker a player first sees

        Press(select, $"Pick_{ProfessionRegistry.BlacksmithId}");
        await Settle(8);
        Shot("01_primer");   // "your first day" primer after picking

        Press(select, "Begin");   // real: NewCampaign + MainUi.AdapterOverride set, SceneChange stubbed
        await Settle(4);
        GD.Print($"[realplaytest] Begin pressed; requestedScene={requestedScene}");

        // ── 2. Mount the real game with the campaign the front door just built ──
        RemoveChild(select); select.QueueFree();
        var ui = GD.Load<PackedScene>(requestedScene ?? "res://scenes/panels/main_ui.tscn").Instantiate<MainUi>();
        AddChild(ui);
        await Settle(24);
        Shot("02_town");   // the real game as launched

        // ── 3. Play the loop through real controls ──
        // Walk the player a moment (real input actions).
        SetMove(new Vector2(1, 0.2f)); await Settle(30); SetMove(Vector2.Zero); await Settle(4);
        Shot("03_walk");

        // Click the forge (real pick seam → BuildingClicked → OpenPanel).
        try { ui.Town.FindBuilding("forge").RaisePick(); } catch { }
        await Settle(10);
        Shot("04_forge");

        // Buy materials through the real vendor Buy buttons.
        var adapter = ui.Adapter;
        foreach (var a in ActionLegality.LegalActions(adapter.CurrentState, adapter.CurrentState.Phase))
            if (a.GetType().Name.Contains("BuyMaterial")) { adapter.Queue(a); break; }
        await Settle(10);
        Shot("05_afterbuy");

        // Send them off / advance the day (real primary verb path via the adapter the button drives).
        adapter.AdvancePhase(); await Settle(12);
        Shot("06_expedition");
        try { ui.OpenPanel("Depths"); } catch { }
        await Settle(120);
        Shot("07_delve");

        // Roll a full day to the evening ledger.
        for (int i = 0; i < 4; i++) { adapter.AdvancePhase(); await Settle(10); }
        Shot("08_dayend");

        GD.Print("[realplaytest] done");
        GetTree().Quit();
    }

    private static void SetMove(Vector2 dir)
    {
        foreach (var (act, on) in new (string, bool)[] {
            ("move_right", dir.X > 0.1f), ("move_left", dir.X < -0.1f),
            ("move_down", dir.Y > 0.1f), ("move_up", dir.Y < -0.1f) })
        {
            if (!InputMap.HasAction(act)) continue;
            if (on) Input.ActionPress(act); else Input.ActionRelease(act);
        }
    }

    private static void Press(Node root, string buttonName)
    {
        if (root.FindChild(buttonName, recursive: true, owned: false) is Button b)
            b.EmitSignal(BaseButton.SignalName.Pressed);
        else GD.Print($"[realplaytest] button not found: {buttonName}");
    }

    private void Shot(string name)
    {
        var img = GetViewport().GetTexture().GetImage();
        var err = img.SavePng(OutDir + name + ".png");
        GD.Print($"[realplaytest] {name}.png -> {err} ({img.GetWidth()}x{img.GetHeight()})");
    }

    private async Task Settle(int frames)
    {
        for (int i = 0; i < frames; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        RenderingServer.ForceDraw();
    }
}
