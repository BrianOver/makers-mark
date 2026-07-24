using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using GameSim;
using GameSim.Kernel;
using GameSim.Professions;
using Godot;

namespace GodotClient;

/// <summary>
/// New-game front door (Playable Core U4 R4; World Rework U11 R9/R11-13): "choose your primary
/// profession" over <see cref="ProfessionRegistry.All"/> (add-on professions appear with zero
/// screen changes) followed by a "your first day" primer card — the 5-phase day legend
/// (verbatim <see cref="MainUi.PhaseLegend"/>, so this screen can never drift from the HUD's own
/// copy), the living-clock behavior, and the campaign seed about to be used. Only "Begin" commits:
/// it builds the campaign via <see cref="GameComposition.NewCampaign(ulong, string)"/> — starter
/// stock seeded, day 1 immediately playable — hands it to <see cref="MainUi.AdapterOverride"/>,
/// and swaps to the main scene. "Back" returns to the profession picker WITHOUT ever touching
/// <see cref="MainUi.AdapterOverride"/> — picking is free to reconsider (KD4: functional-only,
/// no styling wave yet).
///
/// Purity note (R14): the nondeterministic seed source (wall clock) lives HERE, in the godot
/// adapter layer — never in sim/. Both the seed source and the scene change are injectable so
/// engine tests can pin the seed and stub the swap. The seed is drawn ONCE per pick (on
/// <see cref="OnProfessionPicked"/>) and reused by Begin, so the seed the primer displays is
/// exactly the seed the campaign is built with.
/// </summary>
public partial class NewGameSelect : Control
{
    /// <summary>The scene a chosen profession boots into.</summary>
    public const string MainScenePath = "res://scenes/panels/main_ui.tscn";

    /// <summary>
    /// Campaign seed source — wall clock by default (legal in godot/, NEVER in sim/).
    /// Tests may pin it for a deterministic campaign.
    /// </summary>
    public Func<ulong> SeedSource { get; set; } = static () => (ulong)Time.GetTicksUsec();

    /// <summary>
    /// Scene-change hook: null = real <c>GetTree().ChangeSceneToFile</c>. Tests stub this
    /// so pressing a button never tears down the test scene tree.
    /// </summary>
    public Action<string>? SceneChange { get; set; }

    /// <summary>
    /// One-line "what this craft makes" blurb per profession id (R9), shown next to its pick
    /// button. An add-on profession without an entry here still renders — just without a
    /// blurb line — so this table is a courtesy, never a gate (mirrors the registry-driven
    /// button loop below).
    /// </summary>
    private static readonly ImmutableSortedDictionary<string, string> Blurbs =
        new Dictionary<string, string>
        {
            [ProfessionRegistry.BlacksmithId] =
                "Weapons, armor, and shields forged from ore — heavy metal, straightforward stats.",
            [TanningProfession.Id] =
                "Light leather armor and shields, plus a healing field poultice — low weight, high mobility.",
            [EngineeringProfession.Id] =
                "Mechanized weapons, armor, and trinkets, plus a Field Repair Kit — the only craft with Trinket gear.",
            [AlchemyProfession.Id] =
                "A tiered line of healing potions and light alchemical trinkets — the party's lifeline.",
        }.ToImmutableSortedDictionary(StringComparer.Ordinal);

    /// <summary>
    /// The living-day clock explainer (World Rework KTD3): auto-flow, engaged-latch boundary
    /// wait, and Advance-as-skip, in plain language — no timer numbers (those are tuning knobs,
    /// not player-facing promises).
    /// </summary>
    private const string ClockNote =
        "The day flows on its own — phases advance automatically. A phase boundary waits while " +
        "you're working in a panel, so no queued action is ever lost to time. Advance skips " +
        "straight to the next phase whenever you're ready.";

    /// <summary>
    /// U7 (opener fantasy line): the one sentence this whole game is about — everything else on
    /// this primer (phase legend, clock note, seed) explains HOW day 1 works; this states WHY it
    /// matters, so the fantasy is never left implicit on the very first screen a player sees.
    /// </summary>
    private const string FantasyNote =
        "Heroes will buy this gear and carry it into the Mine — what it does down there is " +
        "written on your name.";

    private VBoxContainer _picker = null!;
    private VBoxContainer _primer = null!;
    private Label _seedLabel = null!;

    /// <summary>The profession a pick chose, held while the primer is up; null in the picker
    /// state (nothing committed) and cleared again by Back — the "never leak a campaign on
    /// back-out" invariant.</summary>
    private string? _pendingProfessionId;
    private ulong _pendingSeed;

    public override void _Ready() => BuildUi();

    private void BuildUi()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        var layout = new VBoxContainer { Name = "Layout" };
        layout.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(layout);

        layout.AddChild(new Label
        {
            Name = "Title",
            Text = "Maker's Mark — choose your primary profession",
        });

        _picker = BuildProfessionPicker();
        layout.AddChild(_picker);

        _primer = BuildPrimer();
        _primer.Visible = false; // shown only after a pick (OnProfessionPicked)
        layout.AddChild(_primer);
    }

    private VBoxContainer BuildProfessionPicker()
    {
        var picker = new VBoxContainer { Name = "ProfessionPicker" };

        // Registry-driven (deterministic iteration: ImmutableSortedDictionary, Ordinal).
        foreach (var profession in ProfessionRegistry.All.Values)
        {
            var id = profession.Id;
            var button = new Button
            {
                Name = $"Pick_{id}",
                Text = profession.DisplayName,
            };
            button.Pressed += () => OnProfessionPicked(id);
            picker.AddChild(button);

            picker.AddChild(new Label
            {
                Name = $"Blurb_{id}",
                Text = Blurbs.TryGetValue(id, out var blurb) ? blurb : string.Empty,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            });
        }

        // Starter kit is uniform across professions (GameFactory R4/KD3) — one shared note
        // rather than four identical lines.
        picker.AddChild(new Label
        {
            Name = "StarterKitNote",
            Text = $"Every craft starts the same day one: {GameFactory.StartingPlayerGold} gold and " +
                   $"{GameFactory.StarterCopper} copper — enough for a few tier-1 crafts right away.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });

        return picker;
    }

    private VBoxContainer BuildPrimer()
    {
        var primer = new VBoxContainer { Name = "Primer" };

        primer.AddChild(new Label { Name = "PrimerTitle", Text = "Your first day" });

        // U7: the fantasy, stated once, before any mechanics — everything below this line is
        // HOW the day works; this line is WHY it's worth playing.
        primer.AddChild(new Label
        {
            Name = "FantasyNote",
            Text = FantasyNote,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });

        // Verbatim MainUi.PhaseLegend (R12): the same 5-phase, one-line-each copy the in-game
        // HUD legend shows, so this primer can never drift from what the game explains later.
        primer.AddChild(new Label
        {
            Name = "PhaseLegend",
            Text = MainUi.PhaseLegend,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });

        primer.AddChild(new Label
        {
            Name = "ClockNote",
            Text = ClockNote,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });

        _seedLabel = new Label { Name = "SeedLabel", Text = "Seed: —" };
        primer.AddChild(_seedLabel);

        var begin = new Button { Name = "Begin", Text = "Begin" };
        begin.Pressed += OnBeginPressed;
        primer.AddChild(begin);

        var back = new Button { Name = "Back", Text = "Back" };
        back.Pressed += OnBackPressed;
        primer.AddChild(back);

        return primer;
    }

    private void OnProfessionPicked(string professionId)
    {
        _pendingProfessionId = professionId;
        _pendingSeed = SeedSource(); // drawn once here; Begin reuses it (display == what ships)
        _seedLabel.Text = $"Seed: {_pendingSeed}";

        _picker.Visible = false;
        _primer.Visible = true;
    }

    /// <summary>Return to the picker without ever having touched <see cref="MainUi.AdapterOverride"/>
    /// — nothing was committed by a pick, so there is nothing to undo.</summary>
    private void OnBackPressed()
    {
        _pendingProfessionId = null;
        _primer.Visible = false;
        _picker.Visible = true;
    }

    private void OnBeginPressed()
    {
        if (_pendingProfessionId is null)
        {
            return; // defensive: Begin is only reachable after a pick (Primer stays hidden otherwise)
        }

        GD.Print($"[NewGameSelect] new campaign: profession {_pendingProfessionId}, seed {_pendingSeed}");

        var state = GameComposition.NewCampaign(_pendingSeed, _pendingProfessionId);
        MainUi.AdapterOverride = new SimAdapter(state);

        if (SceneChange is not null)
        {
            SceneChange(MainScenePath);
        }
        else
        {
            GetTree().ChangeSceneToFile(MainScenePath);
        }
    }
}
