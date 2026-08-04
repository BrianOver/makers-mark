using System.Threading.Tasks;
using GameSim.Advisor;
using GameSim.Professions;
using GodotClient.Minigames;
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
        DevToolAudio.Silence(); // automated runs stay silent — see DevToolAudio
        System.IO.Directory.CreateDirectory(OutDir);

        // ── 1. Real new-game front door ──
        var select = GD.Load<PackedScene>("res://scenes/new_game_select.tscn").Instantiate<NewGameSelect>();
        string? requestedScene = null;
        select.SceneChange = path => requestedScene = path;   // stub the tear-down; keep driving
        AddChild(select);
        await Settle(8);
        Shot("00_title");   // U3: the title menu a player first sees (New Game reveals the picker)

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

        // ── Minigame: drive the real "Work the forge" Anvil Map with real interaction ──
        await DriveForgeMinigame(ui);

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

        // ── 4. Second real boot: the ALCHEMIST, to cover its own minigame (the brew puzzle) ──
        RemoveChild(ui); ui.QueueFree();
        await DriveAlchemistBrew();

        GD.Print("[realplaytest] done");
        GetTree().Quit();
    }

    /// <summary>Boots a REAL second campaign as the alchemist (the front door again, real profession
    /// pick + Begin) and drives its brew puzzle with real interaction — pressing the real "Brew"
    /// button, pouring reagents in the recipe's order, and submitting — so BOTH professions'
    /// minigames are exercised every playtest.</summary>
    private async Task DriveAlchemistBrew()
    {
        var select = GD.Load<PackedScene>("res://scenes/new_game_select.tscn").Instantiate<NewGameSelect>();
        string? requested = null;
        select.SceneChange = path => requested = path;
        AddChild(select);
        await Settle(6);

        Press(select, $"Pick_{AlchemyProfession.Id}");
        await Settle(6);
        Press(select, "Begin");
        await Settle(4);
        RemoveChild(select); select.QueueFree();

        var ui = GD.Load<PackedScene>(requested ?? "res://scenes/panels/main_ui.tscn").Instantiate<MainUi>();
        AddChild(ui);
        await Settle(20);

        var adapter = ui.Adapter;
        ui.OpenPanel("Forge");
        await Settle(8);

        Button? FindBrew() => ui.FindChild("Brew_*", recursive: true, owned: false) as Button;
        var brew = FindBrew();
        if (brew is null || brew.Disabled)
        {
            for (int pass = 0; pass < 2; pass++)
                foreach (var a in ActionLegality.LegalActions(adapter.CurrentState, adapter.CurrentState.Phase))
                    if (a.GetType().Name.Contains("BuyMaterial")) adapter.Queue(a);
            adapter.AdvancePhase();
            await Settle(6);
            ui.OpenPanel("Forge");
            await Settle(8);
            brew = FindBrew();
        }

        if (brew is null || brew.Disabled)
        {
            GD.Print($"[realplaytest] brew SKIPPED — Brew button {(brew is null ? "not found" : "gated")}");
            Shot("09_brew_nomini");
            return;
        }

        brew.EmitSignal(BaseButton.SignalName.Pressed);
        await Settle(6);
        if (ui.FindChild("AlchemyBrewPuzzle", recursive: true, owned: false) is not AlchemyBrewPuzzle bp)
        {
            GD.Print("[realplaytest] brew ERROR — AlchemyBrewPuzzle node not found after press");
            return;
        }

        Shot("09_brew_open");

        // Real interaction: pour the recipe in order, but deliberately fumble ONE pour so the
        // wrong-pour feedback (red socket ring + fizzle) is exercised too, then fix it with Undo.
        var ideal = AlchemyPuzzleScorer.IdealSequenceFor(
            ProfessionRegistry.TryGetRecipe(bp.RecipeId, out var recipe) ? recipe! : null!);
        for (int i = 0; i < bp.RequiredPours && i < ideal.Count; i++)
        {
            if (i == 1)
            {
                bp.PourReagent((ideal[i] + 1) % AlchemyReagents.Count); // wrong on purpose
                await Settle(24);
                Shot("09a_brew_wrongpour");
                bp.UndoPour();
                await Settle(8);
            }

            bp.PourReagent(ideal[i]);
            await Settle(24);
        }

        Shot("09b_brew_filled");
        bp.Submit();
        await Settle(10);
        Shot("09c_brew_result");

        GD.Print($"[realplaytest] brew: poured={bp.Poured.Count}/{bp.RequiredPours} completed={bp.Completed} " +
                 $"emitted={(bp.EmittedAction is null ? "NULL" : bp.EmittedAction.RecipeId)} " +
                 $"subScores={(bp.EmittedAction?.SubScores is null ? "-" : string.Join("/", bp.EmittedAction.SubScores))}");
    }

    /// <summary>Drives the real blacksmith Anvil Map minigame end-to-end with real interaction:
    /// buys materials until a recipe is craftable, presses the real "Work the forge" button, then
    /// pumps the bellows + hammers the shape to the end and plunges — capturing the overlay and its
    /// scored result. This exercises the mini-game exactly as a player would, every playtest.</summary>
    private async Task DriveForgeMinigame(MainUi ui)
    {
        var adapter = ui.Adapter;

        ui.OpenPanel("Forge");
        await Settle(8);

        Button? FindWork() => ui.FindChild("WorkForge_*", recursive: true, owned: false) as Button;
        var work = FindWork();

        // If the first recipe isn't affordable yet, buy materials (real vendor actions), apply, reopen.
        if (work is null || work.Disabled)
        {
            for (int pass = 0; pass < 2; pass++)
                foreach (var a in ActionLegality.LegalActions(adapter.CurrentState, adapter.CurrentState.Phase))
                    if (a.GetType().Name.Contains("BuyMaterial")) adapter.Queue(a);
            adapter.AdvancePhase();               // apply the buys
            await Settle(6);
            ui.OpenPanel("Forge");
            await Settle(8);
            work = FindWork();
        }

        if (work is null || work.Disabled)
        {
            GD.Print($"[realplaytest] minigame SKIPPED — WorkForge button {(work is null ? "not found" : "still gated")}");
            Shot("04a_forge_nomini");
            return;
        }

        work.EmitSignal(BaseButton.SignalName.Pressed);   // real click → OnWorkForgePressed → overlay
        await Settle(6);
        if (ui.FindChild("ForgeMinigame", recursive: true, owned: false) is not ForgeMinigame mg)
        {
            GD.Print("[realplaytest] minigame ERROR — ForgeMinigame node not found after press");
            return;
        }

        Shot("04a_minigame_open");

        // Real interaction (the strategy a player learns): the bellows drift the shape BACK while
        // pumping, so don't interleave 1:1 — pump the billet HOT in one burst, then unload a rapid
        // strike run (no drift while hammering) that carries the shape forward. Repeat until Act 1's
        // own finish line (U7: ForgeMinigame stops at ShapingFinishPermille and hands off to a
        // separate QuenchMinigame for Act 2 — it no longer plunges itself).
        int guard = 0;
        while (!mg.Completed && guard++ < 40)
        {
            mg.BellowsStart();
            int p = 0;
            while (mg.HeatYPermille < 850 && p++ < 140) await Settle(2);
            mg.BellowsStop(); await Settle(1);

            int s = 0;
            while (mg.HeatYPermille > 140 && !mg.Completed && s++ < 16)
            {
                mg.ForgeStrike();
                await Settle(1);
            }
        }
        GD.Print($"[realplaytest] Act 1 drive end: shapeX={mg.ShapeXPermille} heat={mg.HeatYPermille} guard={guard} completed={mg.Completed}");

        Shot("04b_minigame_mid");

        if (!mg.Completed || ui.FindChild("QuenchMinigame", recursive: true, owned: false) is not QuenchMinigame quench)
        {
            GD.Print("[realplaytest] Act 2 (quench) SKIPPED — Act 1 never finished or QuenchMinigame absent");
            return;
        }

        await Settle(6); // let heat drain toward the quench trough before the decisive plunge
        quench.Plunge();
        await Settle(8);
        Shot("04c_minigame_result");

        GD.Print($"[realplaytest] quench: completed={quench.Completed} " +
                 $"emitted={(quench.EmittedAction is null ? "NULL" : quench.EmittedAction.RecipeId)} " +
                 $"previewGrade={quench.PreviewGradePermille}");

        // Close any result ceremony so the rest of the playtest continues cleanly.
        ui.OpenPanel("Forge");
        await Settle(4);
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
