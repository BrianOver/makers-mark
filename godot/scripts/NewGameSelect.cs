using System;
using GameSim;
using GameSim.Professions;
using Godot;

namespace GodotClient;

/// <summary>
/// New-game profession select (Playable Core U4, R4): one button per registered profession
/// (resolved through <see cref="ProfessionRegistry.All"/>, so add-on professions appear with
/// zero screen changes). Pressing a button builds a fresh campaign via
/// <see cref="GameComposition.NewCampaign(ulong, string)"/> — starter stock seeded, day 1
/// immediately playable — hands it to <see cref="MainUi.AdapterOverride"/>, and swaps to the
/// main scene. Functional-only (KD4): no styling wave yet.
///
/// Purity note (R14): the nondeterministic seed source (wall clock) lives HERE, in the godot
/// adapter layer — never in sim/. Both the seed source and the scene change are injectable so
/// engine tests can pin the seed and stub the swap.
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
            Text = "Maker's Mark — choose your craft",
        });

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
            layout.AddChild(button);
        }
    }

    private void OnProfessionPicked(string professionId)
    {
        var seed = SeedSource();
        GD.Print($"[NewGameSelect] new campaign: profession {professionId}, seed {seed}");

        var state = GameComposition.NewCampaign(seed, professionId);
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
