using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using GameSim;
using GameSim.Kernel;
using GameSim.Professions;
using Godot;
using GodotClient.Tools;
using GodotClient.Ui;

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
/// <see cref="MainUi.AdapterOverride"/> — picking is free to reconsider. Styled with the shared
/// cozy theme (<see cref="GameTheme"/>): a dusk background behind a single centered wood-framed
/// card (<see cref="GameTheme.PanelStyleWood"/>) that holds the picker or the primer — never both
/// — so the card is never empty and never double-framed.
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

    /// <summary>Centered card max width (px) — narrow enough to read as a cozy dialog rather
    /// than full-bleed at any of this game's supported window sizes (1152×648 and up).</summary>
    private const float CardWidth = 600f;

    /// <summary>Comfortable tap/click height (px) for a profession pick button — the raw engine
    /// default sized to its label alone, which read as a cramped single-line strip.</summary>
    private const float PickButtonHeight = 44f;

    private VBoxContainer _picker = null!;
    private VBoxContainer _primer = null!;
    private Label _seedLabel = null!;

    /// <summary>The profession a pick chose, held while the primer is up; null in the picker
    /// state (nothing committed) and cleared again by Back — the "never leak a campaign on
    /// back-out" invariant.</summary>
    private string? _pendingProfessionId;
    private ulong _pendingSeed;

    public override void _Ready()
    {
        // P007-style cascade (mirrors MainUi._Ready): assign the shared Theme BEFORE building
        // any child Control so hover/pressed button states, fonts, and colors all come from the
        // one theme rather than per-node overrides sprinkled through this screen.
        Theme = GameTheme.Build();
        BuildUi();
    }

    private void BuildUi()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);

        // Dusk backdrop behind the card — the same SurfaceDeep fill MainUi's root reads against,
        // so this front door already looks like the same world as the game it opens into.
        var background = new ColorRect
        {
            Name = "Background",
            Color = GameTheme.SurfaceDeep,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        background.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(background);

        // Full-rect CenterContainer so the card sits in the middle of the window at any
        // supported resolution instead of hugging the top-left like the raw full-bleed layout.
        var center = new CenterContainer { Name = "Center" };
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var card = new PanelContainer
        {
            Name = "Card",
            CustomMinimumSize = new Vector2(CardWidth, 0),
        };
        card.AddThemeStyleboxOverride("panel", GameTheme.PanelStyleWood());
        center.AddChild(card);

        var margin = new MarginContainer { Name = "CardMargin" };
        margin.AddThemeConstantOverride("margin_left", GameTheme.Space16);
        margin.AddThemeConstantOverride("margin_right", GameTheme.Space16);
        margin.AddThemeConstantOverride("margin_top", GameTheme.Space16);
        margin.AddThemeConstantOverride("margin_bottom", GameTheme.Space16);
        card.AddChild(margin);

        var layout = new VBoxContainer { Name = "Layout" };
        layout.AddThemeConstantOverride("separation", GameTheme.Space16);
        margin.AddChild(layout);

        var title = new Label
        {
            Name = "Title",
            Text = "Maker's Mark — choose your primary profession",
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            ThemeTypeVariation = GameTheme.HeaderThemeType,
        };
        title.AddThemeColorOverride("font_color", GameTheme.HeaderColor);
        title.AddThemeFontSizeOverride("font_size", GameTheme.HeaderFontSize);
        layout.AddChild(title);

        // Continue sits ABOVE the profession picker and only when there is something to resume.
        // Above, because for a returning player it is the only thing on this screen they want; and
        // conditional, because an always-present disabled Continue would advertise a feature the
        // first-time player cannot use.
        if (BuildContinue() is { } resume)
        {
            layout.AddChild(resume);
        }

        _picker = BuildProfessionPicker();
        layout.AddChild(_picker);

        _primer = BuildPrimer();
        _primer.Visible = false; // shown only after a pick (OnProfessionPicked)
        layout.AddChild(_primer);
    }

    /// <summary>
    /// The resume row, or null when there is no usable save. Built from <see cref="CampaignSave.Peek"/>,
    /// which reads only the envelope — the world is not rebuilt until the player actually commits, so
    /// arriving at this screen never pays to deserialize a campaign the player may not want.
    ///
    /// <para>A corrupt or schema-mismatched save reports "nothing to resume" (see
    /// <see cref="CampaignSave"/>), so a bad file makes this row absent rather than making the front
    /// door throw. Losing a save is bad; being unable to start a new game is worse.</para>
    /// </summary>
    private VBoxContainer? BuildContinue()
    {
        if (CampaignSave.Peek() is not { } save)
        {
            return null;
        }

        var row = new VBoxContainer { Name = "ContinueRow" };
        row.AddThemeConstantOverride("separation", GameTheme.Space4);

        var button = new Button
        {
            Name = "Continue",
            Text = $"Continue — day {save.Day}",
            CustomMinimumSize = new Vector2(0, PickButtonHeight),
        };

        // Styled as THE primary verb here, the way Begin is on the primer: a returning player's
        // default action is to carry on, not to start over.
        button.AddThemeStyleboxOverride("normal", GameTheme.ButtonStylePrimary());
        button.AddThemeStyleboxOverride("hover", GameTheme.ButtonStylePrimary(GameTheme.ButtonVisualState.Hover));
        button.AddThemeStyleboxOverride("pressed", GameTheme.ButtonStylePrimary(GameTheme.ButtonVisualState.Pressed));
        button.Pressed += OnContinuePressed;
        row.AddChild(button);

        var blurb = new Label
        {
            Name = "ContinueBlurb",
            Text = $"Pick up where you left off — {save.Phase.ToLowerInvariant()} of day {save.Day}. " +
                   "Starting a new campaign replaces this save.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        blurb.AddThemeColorOverride("font_color", GameTheme.TextDim);
        row.AddChild(blurb);

        return row;
    }

    /// <summary>Resume: rebuild the saved world and hand it to <c>MainUi</c> through the same
    /// <c>AdapterOverride</c> seam a fresh campaign uses, so there is exactly one way into the game.</summary>
    private void OnContinuePressed()
    {
        if (CampaignSave.TryLoad() is not { } state)
        {
            // The envelope parsed at Peek() but the world did not rebuild — already logged. Leave the
            // player on this screen with the profession picker rather than loading a broken campaign.
            EngineDistress.Warn("[NewGameSelect] Continue pressed but the save would not load — staying on the picker");
            return;
        }

        GD.Print($"[NewGameSelect] resumed campaign: day {state.Day}, phase {state.Phase}");
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

    private VBoxContainer BuildProfessionPicker()
    {
        var picker = new VBoxContainer { Name = "ProfessionPicker" };
        picker.AddThemeConstantOverride("separation", GameTheme.Space16);

        // Registry-driven (deterministic iteration: ImmutableSortedDictionary, Ordinal).
        foreach (var profession in ProfessionRegistry.All.Values)
        {
            var id = profession.Id;

            // Button + blurb grouped tight (Space4) so the pair reads as one row; the outer
            // picker's Space16 separation is what gives room BETWEEN professions.
            var row = new VBoxContainer { Name = $"PickRow_{id}" };
            row.AddThemeConstantOverride("separation", GameTheme.Space4);
            picker.AddChild(row);

            var button = new Button
            {
                Name = $"Pick_{id}",
                Text = profession.DisplayName,
                CustomMinimumSize = new Vector2(0, PickButtonHeight),
            };
            button.Pressed += () => OnProfessionPicked(id);
            row.AddChild(button);

            var blurb = new Label
            {
                Name = $"Blurb_{id}",
                Text = Blurbs.TryGetValue(id, out var text) ? text : string.Empty,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            blurb.AddThemeColorOverride("font_color", GameTheme.TextDim);
            row.AddChild(blurb);
        }

        // Starter kit is uniform across professions (GameFactory R4/KD3) — one shared note
        // rather than four identical lines. Quiet/dim: informational footnote, not a choice.
        var starterKitNote = new Label
        {
            Name = "StarterKitNote",
            Text = $"Every craft starts the same day one: {GameFactory.StartingPlayerGold} gold and " +
                   $"{GameFactory.StarterCopper} copper — enough for a few tier-1 crafts right away.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        starterKitNote.AddThemeColorOverride("font_color", GameTheme.TextDim);
        picker.AddChild(starterKitNote);

        return picker;
    }

    private VBoxContainer BuildPrimer()
    {
        var primer = new VBoxContainer { Name = "Primer" };
        primer.AddThemeConstantOverride("separation", GameTheme.Space12);

        var primerTitle = new Label
        {
            Name = "PrimerTitle",
            Text = "Your first day",
            ThemeTypeVariation = GameTheme.HeaderThemeType,
        };
        primerTitle.AddThemeColorOverride("font_color", GameTheme.HeaderColor);
        primerTitle.AddThemeFontSizeOverride("font_size", GameTheme.HeaderFontSize);
        primer.AddChild(primerTitle);

        // U7: the fantasy, stated once, before any mechanics — everything below this line is
        // HOW the day works; this line is WHY it's worth playing. Full body color (not dimmed) —
        // this is the one line on the whole screen meant to land, not a footnote.
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

        var clockNote = new Label
        {
            Name = "ClockNote",
            Text = ClockNote,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        clockNote.AddThemeColorOverride("font_color", GameTheme.TextDim);
        primer.AddChild(clockNote);

        _seedLabel = new Label { Name = "SeedLabel", Text = "Seed: —" };
        _seedLabel.AddThemeColorOverride("font_color", GameTheme.TextDim);
        primer.AddChild(_seedLabel);

        var actions = new HBoxContainer { Name = "PrimerActions" };
        actions.AddThemeConstantOverride("separation", GameTheme.Space12);
        primer.AddChild(actions);

        // The one main verb on this screen — Ember/primary treatment, same as MainUi's
        // StylePrimaryVerb (Advance) — so "Begin" reads as THE call to action, and "Back" (plain
        // themed button) reads as the lesser, reversible one.
        var begin = new Button
        {
            Name = "Begin",
            Text = "Begin",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, PickButtonHeight),
        };
        begin.AddThemeStyleboxOverride("normal", GameTheme.ButtonStylePrimary());
        begin.AddThemeStyleboxOverride("hover", GameTheme.ButtonStylePrimary(GameTheme.ButtonVisualState.Hover));
        begin.AddThemeStyleboxOverride("pressed", GameTheme.ButtonStylePrimary(GameTheme.ButtonVisualState.Pressed));
        begin.Pressed += OnBeginPressed;
        actions.AddChild(begin);

        var back = new Button
        {
            Name = "Back",
            Text = "Back",
            CustomMinimumSize = new Vector2(0, PickButtonHeight),
        };
        back.Pressed += OnBackPressed;
        actions.AddChild(back);

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

        // Honour what the Continue blurb promises: a new campaign REPLACES the save. Clearing here
        // rather than waiting for the new run's first autosave closes a real gap — quitting before
        // that first Evening would otherwise leave Continue pointing at the campaign you just
        // abandoned, which reads as the new game having silently failed to start.
        CampaignSave.Clear();

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
