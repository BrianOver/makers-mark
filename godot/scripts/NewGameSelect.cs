using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using GameSim;
using GameSim.Contracts;
using GameSim.Kernel;
using GameSim.Professions;
using Godot;
using GodotClient.Tools;
using GodotClient.Town2d;
using GodotClient.Ui;

namespace GodotClient;

/// <summary>
/// The front door (shell-and-audio plan U3; supersedes Playable Core U4/World Rework U11's
/// straight-to-picker screen — the owner's "need a menu for start screen, saving/loading, new game
/// etc"): a TITLE MENU first — <b>Continue</b> (honest about exactly what it will load), <b>New
/// Game</b>, <b>Settings</b>, <b>Quit</b> — with the "choose your primary profession" picker and
/// "your first day" primer demoted to the New Game sub-flow behind it. Same card, same theme, same
/// <see cref="MainUi.AdapterOverride"/> seam: only Begin ever commits a campaign, so "one way into
/// the game" still holds even with a menu screen in front of it now. Only ONE of
/// {title menu, picker, primer, settings} is ever visible at a time — every transition explicitly
/// hides the others rather than trusting whichever one happened to be showing before.
///
/// <para><b>FullPlaytest compatibility (do not "fix"):</b> the automated full-playthrough tool
/// presses <c>Pick_&lt;profession&gt;</c> directly via <c>FindChild</c> + <c>EmitSignal</c>,
/// bypassing "New Game" entirely (it does not care what is <see cref="Control.Visible"/> — a real
/// player could not click a hidden button, but this harness fires the signal straight). That is why
/// the picker is still built EAGERLY in <see cref="BuildUi"/> (just hidden) rather than lazily
/// constructed the first time "New Game" is pressed — a lazy picker would leave nothing for that
/// harness to find. <see cref="OnProfessionPicked"/> defensively hides the title menu too, so this
/// bypass still lands in a single coherent view instead of primer-over-title-menu.</para>
///
/// <para>Purity note (R14): the nondeterministic seed source (wall clock) lives HERE, in the godot
/// adapter layer — never in sim/. Both the seed source and the scene change are injectable so
/// engine tests can pin the seed and stub the swap. The seed is drawn ONCE per pick (on
/// <see cref="OnProfessionPicked"/>) and reused by Begin, so the seed the primer displays is
/// exactly the seed the campaign is built with — EXCEPT a fresh profile's first blacksmith pick,
/// which uses the pinned <see cref="WarrantSeed"/> instead (P2-ONBOARD-05, §11.15's "The
/// Warrant") and prints its name in place of the raw number; see <see cref="WarrantSeed"/>'s own
/// doc for exactly which picks qualify.</para>
/// </summary>
public partial class NewGameSelect : Control
{
    /// <summary>The scene a chosen profession boots into.</summary>
    public const string MainScenePath = "res://scenes/panels/main_ui.tscn";

    /// <summary>
    /// Campaign seed source — wall clock by default (legal in godot/, NEVER in sim/).
    /// Tests may pin it for a deterministic campaign. Overridden by <see cref="WarrantSeed"/> for
    /// a fresh profile's first blacksmith Begin — see <see cref="OnProfessionPicked"/>.
    /// </summary>
    public Func<ulong> SeedSource { get; set; } = static () => (ulong)Time.GetTicksUsec();

    /// <summary>
    /// P2-ONBOARD-05 (docs/design/MAKERS-MARK.md §11.15, "The Warrant"): the ONE seed a fresh
    /// profile's first blacksmith Begin ever uses, in place of the wall clock — must equal
    /// <c>OpeningCampaignPinTests.ChosenSeed</c> (<c>sim/GameSim.Tests/Harness/</c>), the pin this
    /// value ships. Two conditions, both required (<see cref="OnProfessionPicked"/>):
    ///
    /// <para><b>Blacksmith only.</b> The pin was measured under <c>ApprenticePlayer</c> against
    /// <c>GameComposition.NewCampaign(seed)</c>'s profession-less overload, which defaults to
    /// blacksmith (<c>PlayerState.NewGame(int)</c>'s own doc) — and <c>ApprenticePlayer</c>'s one
    /// gear recipe, "dagger", is a blacksmith recipe (<c>RecipeTable.cs</c>). A different pick
    /// makes that craft illegal, which was never swept. So only a blacksmith pick gets the pin;
    /// every other profession draws wall-clock exactly as before this unit — a real product gap
    /// (three of four professions get no curated week), named here rather than silently
    /// overclaimed. Closing it means re-running the seed search once per profession, which is
    /// P2-ONBOARD-04/-05's own follow-up, not something re-derived by guessing here.</para>
    ///
    /// <para><b>Fresh profile only.</b> <see cref="Ui.TutorialFlow.HasPriorProgress"/> false — the
    /// same signal that hides the returning-smith choice entirely (a true first-timer can never
    /// reach "skip the course," so this need not also check that toggle). A second campaign and
    /// any returning-smith start (run OR skip) draw wall-clock, matching T10's R30 exactly: the
    /// warrant itself still holds either way (<see cref="SkipCourseNote"/>) — only the SEED is
    /// gated, never the mechanism.</para>
    ///
    /// <para><b>Never promises more than the trim.</b> P2-ONBOARD-04's own perturbation sweep
    /// found only five of the Warrant's seven measured beats survive how a player actually
    /// diverges from the script that found this seed; <see cref="WarrantFictionName"/> and every
    /// other string this unit authors state only the mechanical guarantee
    /// (<c>ApprenticeWarrant.Covers</c>, true for ANY script) — never a specific day-N event.</para>
    /// </summary>
    public const ulong WarrantSeed = 1;

    /// <summary>
    /// P2-ONBOARD-05: prints where <c>"Seed: {number}"</c> used to print, for the one campaign
    /// that seed actually names — a raw integer is jargon to a player (P2-HONEST's own rule: "no
    /// raw permille or formula" reads the same way for a seed nobody but a tester can act on).
    /// States only what <see cref="Expedition.ApprenticeWarrant"/> guarantees unconditionally
    /// (§11.13's canonical wording, <c>THE-GAME.md</c> §3.3) — never a script-dependent beat.
    /// </summary>
    public const string WarrantFictionName = "The Warrant — through day three, the Mine keeps no one.";

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
    /// The living-day clock explainer. U2 (§11.14.14): the OLD copy above (kept here as the fix's
    /// receipt) said phases "advance automatically" and told the player to press a control called
    /// "Advance" — this repo's most-litigated first-screen copy defect, and BOTH halves were false.
    /// Auto-advance defaults OFF (<see cref="PhaseClock.AutoAdvance"/>'s own field default is
    /// "player-decided" — <c>MainUi</c>'s boot sequence only ever flips it on from a PERSISTED
    /// opt-in, never as a fresh-install default): the day waits for the player. And no control
    /// anywhere is labelled "Advance" — the HUD's one bell relabels itself per phase (<see
    /// cref="PhaseVocab.BellVerb"/>), so this note reads its two real words (Morning/Evening — the
    /// only two phases that carry a bell at all; Quest/Vigil/Deep Vigil are the raid span and play
    /// themselves, per <c>GameKernel.Advance</c>'s own transition table) FROM
    /// <see cref="PhaseVocab"/> instead of retyping them — the same join <see cref="TutorialFlow"/>
    /// already uses for its own copy (<c>MorningBell</c>/<c>EveningBell</c>), so a reworded bell
    /// turns this line red instead of quietly becoming the next copy of this exact defect. No timer
    /// numbers (those are tuning knobs, not player-facing promises).
    /// </summary>
    private static string ClockNote =>
        $"The day waits for you — nothing moves until you say so. Press \"{PhaseVocab.BellVerb(DayPhase.Morning)}\" " +
        $"when you're ready to end the morning, and \"{PhaseVocab.BellVerb(DayPhase.Evening)}\" to close out the " +
        "night; whatever happens in between plays itself.";

    /// <summary>
    /// U7 (opener fantasy line): the one sentence this whole game is about — everything else on
    /// this primer (clock note, seed) explains HOW day 1 works; this states WHY it matters, so the
    /// fantasy is never left implicit on the very first screen a player sees. P2-ONBOARD-06
    /// (§11.15), deletion #8 removed the primer's own five-line phase legend — the SAME copy
    /// (<see cref="MainUi.PhaseLegend"/>) still lives on the in-game HUD's own phase-chip tooltip,
    /// so a genuinely new player still reaches it, on the screen it actually describes, rather than
    /// reading it once, cold, before any phase has ever happened to make it concrete.
    /// </summary>
    private const string FantasyNote =
        "Heroes will buy this gear and carry it into the Mine — what it does down there is " +
        "written on your name.";

    /// <summary>Centered card max width (px) — narrow enough to read as a cozy dialog rather
    /// than full-bleed at any of this game's supported window sizes (1152×648 and up).</summary>
    private const float CardWidth = 600f;

    /// <summary>Comfortable tap/click height (px) for a menu/pick button — the raw engine
    /// default sized to its label alone, which read as a cramped single-line strip.</summary>
    private const float PickButtonHeight = 44f;

    private VBoxContainer _titleMenu = null!;
    private VBoxContainer _picker = null!;
    private VBoxContainer _primer = null!;
    private SettingsPanel _settings = null!;
    private Label _seedLabel = null!;

    /// <summary>The profession a pick chose, held while the primer is up; null in the picker
    /// state (nothing committed) and cleared again by Back — the "never leak a campaign on
    /// back-out" invariant.</summary>
    private string? _pendingProfessionId;
    private ulong _pendingSeed;

    // U17 (§11.14.14 defect): the returning-smith choice — see Ui.TutorialFlow.ResetForReturningSmith's
    // own doc for the defect this closes. Only ever shown when Ui.TutorialFlow.HasPriorProgress says
    // there is something to skip (a true first-timer has nothing taught yet, so the question would be
    // meaningless noise on their very first screen).
    private VBoxContainer _returningSmithChoice = null!;
    private Button _runCourseButton = null!;
    private Button _skipCourseButton = null!;
    private Label _returningSmithNote = null!;

    /// <summary>The live pick — false (run the course) by default every time the primer mounts, so a
    /// player who never touches the toggle gets the exact <see cref="Ui.TutorialFlow.ResetForNewGame"/>
    /// behavior this unit did not change.</summary>
    private bool _skipTutorialCourse;

    public override void _Ready()
    {
        // P007-style cascade (mirrors MainUi._Ready): assign the shared Theme BEFORE building
        // any child Control so hover/pressed button states, fonts, and colors all come from the
        // one theme rather than per-node overrides sprinkled through this screen.
        Theme = GameTheme.Build();

        // KTD-D: this scene is project.godot's `run/main_scene` — the one place a freshly opened
        // OS window exists to re-apply a persisted fullscreen choice to. A scene change afterward
        // (to MainUi, or back here via "Save & quit to title") never recreates that window, so
        // nothing downstream needs to call this again.
        UiSettings.ApplyPersisted();

        BuildUi();
    }

    /// <summary>
    /// F11 anywhere (R3) — the title screen's own copy of the toggle <c>MainUi</c> already ships
    /// (PR #354) via <see cref="UiSettings"/>, so neither host can drift from the other.
    /// <c>_UnhandledKeyInput</c> (not <c>_Input</c>): this screen has no drawer/modal ladder to
    /// race against, so there is nothing here that would ever consume the key first.
    /// </summary>
    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { PhysicalKeycode: Key.F11, Pressed: true, Echo: false })
        {
            return;
        }

        UiSettings.ToggleFullscreen();
        _settings?.Refresh(); // keep the Settings checkbox honest if it happens to be showing
        GetViewport()?.SetInputAsHandled();
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
            Text = "Maker's Mark",
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            ThemeTypeVariation = GameTheme.HeaderThemeType,
        };
        title.AddThemeColorOverride("font_color", GameTheme.HeaderColor);
        title.AddThemeFontSizeOverride("font_size", GameTheme.HeaderFontSize);
        layout.AddChild(title);

        // Exactly one of these four views is ever visible — every button below hides the view it
        // leaves and shows the one it enters, never both.
        _titleMenu = BuildTitleMenu();
        layout.AddChild(_titleMenu);

        _picker = BuildProfessionPicker();
        _picker.Visible = false; // shown only after "New Game" (OnNewGamePressed)
        layout.AddChild(_picker);

        _primer = BuildPrimer();
        _primer.Visible = false; // shown only after a pick (OnProfessionPicked)
        layout.AddChild(_primer);

        _settings = new SettingsPanel();
        _settings.Build();
        _settings.Visible = false; // shown only after "Settings" (OnSettingsPressed)
        _settings.Closed += OnSettingsClosed;
        layout.AddChild(_settings);
    }

    /// <summary>The title menu: Continue (if there is anything to resume) above New Game/
    /// Settings/Quit, exactly the owner's ask ("need a menu for start screen, saving/loading, new
    /// game etc").</summary>
    private VBoxContainer BuildTitleMenu()
    {
        var menu = new VBoxContainer { Name = "TitleMenu" };
        menu.AddThemeConstantOverride("separation", GameTheme.Space16);

        var resume = BuildContinue();
        if (resume is not null)
        {
            menu.AddChild(resume);
        }

        var newGame = new Button
        {
            Name = "NewGame",
            Text = "New Game",
            CustomMinimumSize = new Vector2(0, PickButtonHeight),
        };

        // A returning player's default verb is Continue (styled Primary below). A first-time
        // player has no Continue row at all — give New Game the same Primary treatment in THAT
        // case, so the screen still reads as "one obvious first action" instead of four
        // identically-weighted buttons with nothing to anchor on.
        if (resume is null)
        {
            newGame.AddThemeStyleboxOverride("normal", GameTheme.ButtonStylePrimary());
            newGame.AddThemeStyleboxOverride("hover", GameTheme.ButtonStylePrimary(GameTheme.ButtonVisualState.Hover));
            newGame.AddThemeStyleboxOverride("pressed", GameTheme.ButtonStylePrimary(GameTheme.ButtonVisualState.Pressed));
        }

        newGame.Pressed += OnNewGamePressed;
        menu.AddChild(newGame);

        var settingsButton = new Button
        {
            Name = "SettingsButton",
            Text = "Settings",
            CustomMinimumSize = new Vector2(0, PickButtonHeight),
        };
        settingsButton.Pressed += OnSettingsPressed;
        menu.AddChild(settingsButton);

        // Never a dead click: GetTree().Quit() always works from the title screen — there is no
        // in-flight state here that could make Quit unsafe (that caveat belongs to MainUi's
        // system menu, KTD-D).
        var quit = new Button
        {
            Name = "Quit",
            Text = "Quit",
            CustomMinimumSize = new Vector2(0, PickButtonHeight),
        };
        quit.Pressed += () => GetTree().Quit();
        menu.AddChild(quit);

        return menu;
    }

    /// <summary>
    /// The resume row, or null when there is no usable save. Built from <see cref="CampaignSave.Peek"/>,
    /// which reads only the envelope — the world is not rebuilt until the player actually commits, so
    /// arriving at this screen never pays to deserialize a campaign the player may not want.
    ///
    /// <para>A corrupt or schema-mismatched save reports "nothing to resume" (see
    /// <see cref="CampaignSave"/>), so a bad file makes this row absent rather than making the front
    /// door throw. Losing a save is bad; being unable to start a new game is worse.</para>
    ///
    /// <para><b>R4 (Continue tells the truth):</b> the label now names the profession (when the
    /// envelope carries one — <see cref="CampaignSave.Envelope.ProfessionId"/>, U3/KTD-E) and the
    /// blurb adds when the save was written (<see cref="CampaignSave.Envelope.SavedAtUtc"/>). Both
    /// are trailing-optional on the envelope, so a pre-U3 save degrades to the plain
    /// day/phase sentence rather than losing the row.</para>
    /// </summary>
    private VBoxContainer? BuildContinue()
    {
        if (CampaignSave.Peek() is not { } save)
        {
            return null;
        }

        var professionName = save.ProfessionId is { } id && ProfessionRegistry.All.TryGetValue(id, out var profession)
            ? profession.DisplayName
            : null;
        var savedAt = FormatSavedAt(save.SavedAtUtc);

        var row = new VBoxContainer { Name = "ContinueRow" };
        row.AddThemeConstantOverride("separation", GameTheme.Space4);

        var button = new Button
        {
            Name = "Continue",
            // Day AND phase, on the button itself, not just the blurb below it: the owner's
            // repeated complaint ("Continue day 2 is still there") was partly a genuinely-stale
            // save (autosave silently dead for two professions until PR #336) and partly this
            // label not saying enough to tell a fresh checkpoint from an old one at a glance.
            // U3 adds the profession name (when known) so the label also says WHOSE storefront
            // it will resume, not just when.
            Text = professionName is null
                ? $"Continue — Day {save.Day}, {PhaseVocab.Display(save.Phase)}"
                : $"Continue — {professionName} · Day {save.Day}, {PhaseVocab.Display(save.Phase)}",
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
            // U2 (playtest-three plan, KTD-B): was save.Phase.ToLowerInvariant() — the stored save
            // envelope's raw DayPhase.ToString(), so this could (and did) render "camp of day 5" or
            // "expeditiondeep of day 12". PhaseVocab reads the same stored string back into the one
            // vocabulary the HUD and timeline now share; the envelope's own format is untouched.
            Text = $"Pick up where you left off — the {PhaseVocab.Display(save.Phase)} of day {save.Day}" +
                   (savedAt is null ? "." : $", saved {savedAt}.") +
                   " Starting a new campaign replaces this save.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        blurb.AddThemeColorOverride("font_color", GameTheme.TextDim);
        row.AddChild(blurb);

        return row;
    }

    /// <summary>
    /// Renders <paramref name="savedAtUtc"/> (ISO-8601 round-trip, <see
    /// cref="CampaignSave.Envelope.SavedAtUtc"/>) as a short local-time phrase ("today 21:40" or
    /// "Jul 30, 09:15"), or null when absent/unparseable — a pre-U3 save (missing the field
    /// entirely) or a hand-edited file degrades to no saved-at clause rather than breaking the
    /// row (KTD-E: trailing-optional, backward compatible).
    /// </summary>
    private static string? FormatSavedAt(string? savedAtUtc)
    {
        if (savedAtUtc is null || !DateTime.TryParse(
                savedAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var utc))
        {
            return null;
        }

        var local = utc.ToLocalTime();
        return local.Date == DateTime.Now.Date
            ? $"today {local:HH:mm}"
            : $"{local:MMM d}, {local:HH:mm}";
    }

    /// <summary>Resume: rebuild the saved world and hand it to <c>MainUi</c> through the same
    /// <c>AdapterOverride</c> seam a fresh campaign uses, so there is exactly one way into the game.</summary>
    private void OnContinuePressed()
    {
        if (CampaignSave.TryLoad() is not { } state)
        {
            // The envelope parsed at Peek() but the world did not rebuild — already logged. Leave the
            // player on this screen with the title menu rather than loading a broken campaign.
            EngineDistress.Warn("[NewGameSelect] Continue pressed but the save would not load — staying on the title menu");
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

    private void OnNewGamePressed()
    {
        _titleMenu.Visible = false;
        _picker.Visible = true;
    }

    private void OnSettingsPressed()
    {
        _titleMenu.Visible = false;
        _settings.Refresh(); // the live window mode may have moved since Build() (F11, or the OTHER host)
        _settings.Visible = true;
    }

    private void OnSettingsClosed()
    {
        _settings.Visible = false;
        _titleMenu.Visible = true;
    }

    private VBoxContainer BuildProfessionPicker()
    {
        var picker = new VBoxContainer { Name = "ProfessionPicker" };
        picker.AddThemeConstantOverride("separation", GameTheme.Space16);

        var pickerTitle = new Label
        {
            Name = "PickerTitle",
            Text = "Choose your primary profession",
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            ThemeTypeVariation = GameTheme.HeaderThemeType,
        };
        pickerTitle.AddThemeColorOverride("font_color", GameTheme.HeaderColor);
        pickerTitle.AddThemeFontSizeOverride("font_size", GameTheme.HeaderFontSize);
        picker.AddChild(pickerTitle);

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

            // U7 (world-and-interiors plan, KTD-3): "rethink the whole start picking" — the pick's
            // world consequence, stated at pick time. A single-profession row has no primary
            // ambiguity (WorkshopVocab.NametagFor's ordering only matters once a second profession
            // joins mid-run), so this is always the exact nametag that profession's workshop opens
            // under from day 1.
            var workshopNote = new Label
            {
                Name = $"WorkshopNote_{id}",
                Text = $"Your workshop: the {WorkshopVocab.NametagFor(new[] { id })}.",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            workshopNote.AddThemeColorOverride("font_color", GameTheme.TextDim);
            row.AddChild(workshopNote);
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

        var back = new Button
        {
            Name = "PickerBack",
            Text = "Back",
            CustomMinimumSize = new Vector2(0, PickButtonHeight),
        };
        back.Pressed += OnPickerBackPressed;
        picker.AddChild(back);

        return picker;
    }

    /// <summary>Picker's own Back — up to the title menu (distinct from the primer's Back, which
    /// only steps down to the picker). Nothing was committed by reaching the picker, so there is
    /// nothing to undo.</summary>
    private void OnPickerBackPressed()
    {
        _picker.Visible = false;
        _titleMenu.Visible = true;
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

        // U17 (§11.14.14 defect): the returning-smith choice. Built once, hidden until
        // OnProfessionPicked re-checks Ui.TutorialFlow.HasPriorProgress against the live save.
        _returningSmithChoice = BuildReturningSmithChoice();
        _returningSmithChoice.Visible = false;
        primer.AddChild(_returningSmithChoice);

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

    /// <summary>
    /// U17 (§11.14.14 defect): "run the course, or skip it and keep the Lessons book only" — the
    /// returning-smith choice. No <see cref="ButtonGroup"/> anywhere in this project (<c>ForgePanel</c>'s
    /// own remark: zero uses, measured) — two <see cref="BaseButton.ToggleMode"/> buttons with
    /// exclusivity kept by hand via <see cref="SetReturningSmithChoice"/>, the house idiom
    /// (<c>ForgePanel</c>'s own tab row, <c>ScryingMirror</c>'s party tabs).
    /// </summary>
    private VBoxContainer BuildReturningSmithChoice()
    {
        var section = new VBoxContainer { Name = "ReturningSmithChoice" };
        section.AddThemeConstantOverride("separation", GameTheme.Space4);

        var header = new Label
        {
            Name = "ReturningSmithHeader",
            Text = "You've kept a shop before.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        section.AddChild(header);

        var row = new HBoxContainer { Name = "ReturningSmithRow" };
        row.AddThemeConstantOverride("separation", GameTheme.Space12);
        section.AddChild(row);

        _runCourseButton = new Button
        {
            Name = "RunCourse",
            Text = "Run the course",
            ToggleMode = true,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _runCourseButton.Pressed += () => SetReturningSmithChoice(skip: false);
        row.AddChild(_runCourseButton);

        _skipCourseButton = new Button
        {
            Name = "SkipCourse",
            Text = "Skip it — Lessons book only",
            ToggleMode = true,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _skipCourseButton.Pressed += () => SetReturningSmithChoice(skip: true);
        row.AddChild(_skipCourseButton);

        _returningSmithNote = new Label { Name = "ReturningSmithNote", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _returningSmithNote.AddThemeColorOverride("font_color", GameTheme.TextDim);
        section.AddChild(_returningSmithNote);

        return section;
    }

    /// <summary>Law 7 ("skipping stays legal and its cost is named in copy, never engineered"), read
    /// the HONEST direction: unlike <see cref="Ui.TutorialFlow.DismissConfirmCopy"/> (a first-timer
    /// FORFEITING the apprenticeship warrant mid-chain), this path never touches the warrant at all —
    /// so the copy names what the returning smith KEEPS, not a cost they are about to pay.</summary>
    private const string RunCourseNote =
        "Runs the three-day apprenticeship course again, exactly like a first campaign.";

    /// <summary>P2-ONBOARD-05 (§11.15): the New-Game skip door's cost, verbatim from the plan's own
    /// deliverable sentence — T10's R30, the returning smith's door, which KEEPS the warrant (never
    /// harmonize this with <see cref="Ui.TutorialFlow.DismissConfirmCopy"/>'s mid-week ✕, R12,
    /// which forfeits it — two different doors, two different rulings, both costs named).</summary>
    public const string SkipCourseNote =
        "The first week was set out for you — skipping it trades the guided shape for the open "
        + "one. The warrant holds either way. What you already learned stays in the Lessons book, "
        + "and nothing you've already been taught fires twice.";

    private void SetReturningSmithChoice(bool skip)
    {
        _skipTutorialCourse = skip;
        _runCourseButton.ButtonPressed = !skip;
        _skipCourseButton.ButtonPressed = skip;
        _returningSmithNote.Text = skip ? SkipCourseNote : RunCourseNote;
    }

    private void OnProfessionPicked(string professionId)
    {
        _pendingProfessionId = professionId;

        // P2-ONBOARD-05 (§11.15): The Warrant's pinned seed applies to exactly one door — a fresh
        // profile's first BLACKSMITH Begin (see WarrantSeed's own doc for why both conditions are
        // required). Every other pick — a different profession, or any pick once
        // HasPriorProgress reads true — draws SeedSource() exactly as before this unit, still
        // drawn once here and reused by Begin (display == what ships).
        var isWarrant = !Ui.TutorialFlow.HasPriorProgress && professionId == ProfessionRegistry.BlacksmithId;
        _pendingSeed = isWarrant ? WarrantSeed : SeedSource();
        _seedLabel.Text = isWarrant ? WarrantFictionName : $"Seed: {_pendingSeed}";

        // U17: re-armed every time the primer mounts fresh — HasPriorProgress is re-checked against
        // the LIVE save (never cached from a previous pick), and the choice itself always resets to
        // "run the course" so a Back-then-pick-again never carries a stale Skip selection forward.
        _returningSmithChoice.Visible = Ui.TutorialFlow.HasPriorProgress;
        SetReturningSmithChoice(skip: false);

        // Defensive (see class doc's FullPlaytest note): a caller that bypasses "New Game" and
        // presses Pick_* directly must still land in a single coherent view, not primer-over-
        // title-menu — so this hides the title menu too, not just the picker.
        _titleMenu.Visible = false;
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

        // U17 (§11.14.14 defect): the returning-smith choice. Re-checked against the LIVE
        // HasPriorProgress here, not just trusted from the toggle state — the same defensive
        // footing as the null-guard above: a caller that bypasses the picker and fires a hidden
        // control directly (class doc's FullPlaytest note) must never be able to hand a genuine
        // first-timer the returning-smith reset.
        if (_skipTutorialCourse && Ui.TutorialFlow.HasPriorProgress)
        {
            Ui.TutorialFlow.ResetForReturningSmith();
        }
        else
        {
            // Root-cause fix (owner playtest: "The tutorial is missing?"): TutorialFlow persists its
            // own Completed/Dismissed/Step at a SEPARATE user:// file that outlives the sim save above
            // — MainUi.BuildUi loads it unconditionally on every mount, so a tutorial finished or
            // dismissed on any earlier campaign silently suppressed the whole chain (Active=false, no
            // on-screen sign why) on every New Game after it. A brand-new campaign must start with a
            // brand-new tutorial, exactly like it starts with a brand-new sim save.
            Ui.TutorialFlow.ResetForNewGame();
        }

        // U16 (§11.14.14, "the first thing any player ever reads"): tell the next MainUi mount to
        // open with Bryn's cold-open beat already showing — see MainUi.FirstMorningBeatPending's
        // own doc for the full contract. Set on BOTH branches above (run the course, or skip it as
        // a returning smith): the beat states the game's own premise, not a numbered mechanical
        // step, so a returning smith who opted out of the course is not exempt from it — only the
        // ordinary once-ever guard (ConsumeFirstTouch) decides whether they actually see it again.
        // Never set from OnContinuePressed: a resumed campaign relies on its own persisted
        // FirstTouch/PendingMentorLines state, never on this one-shot new-game signal.
        MainUi.FirstMorningBeatPending = true;

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
