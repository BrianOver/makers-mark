using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Economy;
using GameSim.Materials;
using GameSim.Professions;
using Godot;
using GodotClient.Minigames;
using GodotClient.Town3d;
using GodotClient.Ui;

namespace GodotClient.Panels;

/// <summary>
/// The forge (R4 display half): every recipe of every SELECTED profession (P1 — resolved
/// through <see cref="ProfessionRegistry"/>, so add-on professions appear here with zero
/// panel changes) with live material availability and a Craft button (queues
/// <see cref="CraftAction"/>), plus each profession's talent mini-tree with Unlock buttons
/// (queues <see cref="UnlockTalentAction"/>), plus the Morning vendor's buy rows (Playable
/// Core U3): one row per <see cref="MaterialRegistry.PricedPool"/> key with its marked-up
/// price, queueing <see cref="BuyMaterialAction"/>. Unlock enablement calls
/// <see cref="ProfessionDefinition.CanUnlock"/> — sim-owned validation, only rendered here.
///
/// <para>P007 U5 (R12/KTD2/KTD3/KTD5 — resolves OQ4 to click-to-craft): recipe rows are now
/// <see cref="UiKit.Card"/>s — a recipe <see cref="UiKit.ArtRect"/> (falling back to the slot
/// icon on any manifest miss), name/tier/slot, output atk/def/wt <see cref="UiKit.StatChip"/>s,
/// and a material-requirement chip that lights <see cref="UiKit.ChipTone.Positive"/> when
/// affordable / stays <see cref="UiKit.ChipTone.Neutral"/> ("dim") when not — a VISUAL mirror
/// only, read off <c>state.Player.Materials</c>; the kernel's <see cref="CraftAction"/> stays
/// the real gate (a card never bypasses the sim's own validation). Talent rows are unlock
/// cards. Every sim read/queue path (<see cref="SelectedMaterialOr"/>, <see cref="OnCraftPressed"/>/
/// <see cref="OnUnlockPressed"/>, <see cref="ProfessionDefinition.CanUnlock"/> enablement) and
/// every control <c>Name</c> (<c>MaterialSelect</c>, <c>Craft_{recipeId}</c>,
/// <c>Unlock_{nodeId}</c>) is preserved verbatim — only the visual composition changed.</para>
/// </summary>
public partial class ForgePanel : SimPanel
{
    private const string RecipeDefaultOption = "(recipe default)";

    /// <summary>Recipe-art tile edge length (px) for a recipe/talent card — matches
    /// <c>ShopPanel.ItemArtSize</c> so an item's icon reads at the same weight everywhere.</summary>
    private const float RecipeArtSize = 56f;

    /// <summary>Sane minimum width (px) for a recipe card's info column (R7-class guard) — a long
    /// recipe name must keep enough room to wrap at word boundaries, not mid-word.</summary>
    private const float RecipeInfoColumnMinWidth = 180f;

    private Label? _feedback;
    private Label? _materialsLabel;
    private OptionButton? _materialSelect;
    private VBoxContainer? _vendorRows;
    private VBoxContainer? _recipeRows;
    private VBoxContainer? _talentRows;

    /// <summary>PA6: the forge minigame overlay — a single instance reused across recipes,
    /// (re)configured per <see cref="OnWorkForgePressed"/> press. Built once in
    /// <see cref="EnsureBuilt"/> as the LAST child so it draws over the recipe/talent scroll body
    /// (PKD8 self-contained focus overlay); hidden except while a run is in progress.</summary>
    private ForgeMinigame? _minigame;

    /// <summary>G1 (game-feel plan §"World VFX keyed to beat state"): the town's forge-station VFX
    /// surface — resolved lazily via <see cref="ResolveTown"/> rather than threaded through
    /// <c>MainUi</c> (this unit's scope keeps MainUi untouched beyond the build-stamp mount), and
    /// cached once found.</summary>
    private Town3D? _town;

    // ── G1 result ceremony (game-feel plan §"Result ceremony") ────────────────────────────────
    private const double CeremonySeconds = 2.0;

    private Control? _ceremony;
    private Label? _ceremonyGrade;
    private Label? _ceremonyStars;
    private HBoxContainer? _ceremonyPips;
    private double _ceremonyRemaining = -1;

    // ── G1 forge juice (game-feel plan §"Forge juice") — two tiny procedural tones, no external
    // audio asset needed (see MakeTone's own doc for why).
    private AudioStreamPlayer? _hammerSfx;
    private AudioStreamPlayer? _stingSfx;

    public override void _Ready() => EnsureBuilt();

    public override void _Process(double delta)
    {
        // G1: drive the furnace glow continuously off the LIVE heat gauge while Smelt is the
        // active stage — a per-frame poll rather than an event, since the gauge itself changes
        // every frame the minigame's own _Process ticks Advance(delta).
        if (_minigame is { Visible: true, Current: ForgeMinigame.Stage.Smelt })
        {
            ResolveTown()?.ForgeGlow(_minigame.Smelt.HeatPermille);
        }

        // G1 ceremony auto-dismiss: accumulated-delta only (no engine Tween in this codebase —
        // mirrors MainUi's gold-chip pop / Return Ritual gate idiom).
        if (_ceremonyRemaining >= 0)
        {
            _ceremonyRemaining -= delta;
            if (_ceremonyRemaining <= 0)
            {
                HideCeremony();
            }
        }
    }

    public override void Refresh()
    {
        EnsureBuilt();
        if (Adapter is null)
        {
            return;
        }

        var state = Adapter.CurrentState;
        _materialsLabel!.Text = state.Player.Materials.IsEmpty
            ? "MATERIALS: none — buy from the vendor below or wait for Evening's returning heroes"
            : "MATERIALS: " + string.Join(", ", state.Player.Materials.Select(m => $"{m.Key} x{m.Value}"));

        // Vendor rows (U3): every priced-pool material at its marked-up single-unit price.
        // Display quote only — the sim's MaterialVendorHandlers reprices authoritatively on
        // apply; this mirrors its exact formula (ceilDiv over sim-owned constants), no rules here.
        Clear(_vendorRows!);
        foreach (var key in MaterialRegistry.PricedPool)
        {
            var unit = MaterialRegistry.UnitPrice(key);
            var quote = (int)(((long)unit * (1000 + MaterialVendorHandlers.VendorMarkupPermille) + 999) / 1000);
            var have = state.Player.Materials.TryGetValue(key, out var owned) ? owned : 0;
            var row = AddRow(_vendorRows!);
            AddIcon(row, IconRegistry.Ore(key));
            AddLabel(row, $"{key} — {quote}g each (have {have})");
            var buy = AddButton(row, $"BuyMat_{key}", "Buy 1", () => OnBuyMaterialPressed(key));
            // U6 gate, mirroring MaterialVendorHandlers: Morning-only CanHandle + the gold
            // check. Landing phase = the CURRENT phase (GameKernel.Tick applies the queued
            // batch against state.Phase before advancing), so the buy is legal exactly
            // while the sim still sits AT Morning.
            GateButton(buy,
                legal: state.Phase == DayPhase.Morning && quote <= state.Player.Gold,
                whyNot: state.Phase != DayPhase.Morning
                    ? "The vendor sells in the Morning."
                    : "You can't afford that yet.");
        }

        Clear(_recipeRows!);
        Clear(_talentRows!);
        foreach (var professionId in state.Player.SelectedProfessions)
        {
            if (!ProfessionRegistry.TryGet(professionId, out var profession))
            {
                continue;
            }

            var unlocked = state.Player.TalentsFor(professionId);
            foreach (var recipe in profession!.Recipes.Values)
            {
                var material = SelectedMaterialOr(recipe.MaterialKey);
                var have = state.Player.Materials.TryGetValue(material, out var stock) ? stock : 0;
                // U6 gate, mirroring CraftingHandlers.ApplyCraft step 5 (material quantity
                // less the material-efficiency talent, floor 1) — the kernel's own math,
                // only rendered here. Crafting is legal in ALL phases (the forge never
                // closes), so there is deliberately NO phase term in this gate.
                var efficiency = profession.MaterialEfficiencyNode is { } eff && unlocked.Contains(eff) ? 1 : 0;
                var needed = Math.Max(1, recipe.MaterialQuantity - efficiency);
                var affordable = have >= needed;

                var card = Card($"RecipeCard_{recipe.RecipeId}");
                _recipeRows!.AddChild(card);
                var cardBody = new VBoxContainer();
                card.AddChild(cardBody);

                var headerRow = AddRow(cardBody);
                headerRow.AddChild(ArtRect(
                    AssetCatalog.ItemIconId(recipe.RecipeId), new Vector2(RecipeArtSize, RecipeArtSize),
                    // Caption restored (recipe.Name): on a manifest MISS this is the ONLY place
                    // the placeholder's caption comes from — dropping it would show the raw asset
                    // key instead of the recipe name. On a HIT it also renders under the icon
                    // now, alongside the fuller infoCol line below — redundant, never wrong.
                    IconRegistry.Slot(recipe.Slot), recipe.Name));

                var infoCol = new VBoxContainer
                {
                    SizeFlagsHorizontal = SizeFlags.ExpandFill,
                    CustomMinimumSize = new Vector2(RecipeInfoColumnMinWidth, 0),
                };
                headerRow.AddChild(infoCol);
                AddLabel(infoCol, $"{recipe.Name} (t{recipe.Tier} {recipe.Slot})");
                var outputRow = AddRow(infoCol);
                outputRow.AddChild(StatChip("Atk", $"{recipe.BaseStats.Attack}"));
                outputRow.AddChild(StatChip("Def", $"{recipe.BaseStats.Defense}"));
                outputRow.AddChild(StatChip("Wt", $"{recipe.BaseStats.Weight}"));

                // Affordability lighting (KTD5) is a VISUAL MIRROR ONLY, read off the same
                // state.Player.Materials the gate below reads — the kernel's CraftAction stays
                // the real gate; a stale-enabled press is still honestly rejected downstream.
                var controlsRow = AddRow(cardBody);
                controlsRow.AddChild(StatChip(
                    material, $"{recipe.MaterialQuantity}x (have {have})",
                    affordable ? UiKit.ChipTone.Positive : UiKit.ChipTone.Neutral));

                // PA6/PKD4: an ACTIVE profession's instant Craft is the null-grade auto-craft
                // path (competent, hard-capped below Masterwork) — relabeled so it reads as the
                // explicit fallback beside the minigame, not the only way to craft. A PASSIVE
                // profession's Craft is unchanged (no minigame exists for it in Phase A).
                var craftLabel = profession.ActiveCraft ? "Auto-craft (competent)" : "Craft";
                var craft = AddButton(controlsRow, $"Craft_{recipe.RecipeId}", craftLabel, () => OnCraftPressed(recipe.RecipeId));
                GateButton(craft, affordable, $"Not enough {material} — need {needed}, have {have}.");

                if (profession.ActiveCraft)
                {
                    var work = AddButton(controlsRow, $"WorkForge_{recipe.RecipeId}", "Work the forge",
                        () => OnWorkForgePressed(recipe, material, profession!, unlocked));
                    GateButton(work, affordable, $"Not enough {material} — need {needed}, have {have}.");
                }
            }

            foreach (var node in profession.TalentNodes.Values)
            {
                var hasNode = unlocked.Contains(node.NodeId);
                var card = Card($"TalentCard_{node.NodeId}");
                _talentRows!.AddChild(card);
                var cardBody = new VBoxContainer();
                card.AddChild(cardBody);

                var row = AddRow(cardBody);
                AddIcon(row, IconRegistry.Glyph("rune"));
                var infoCol = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
                row.AddChild(infoCol);
                AddLabel(infoCol, $"{node.Name} — {node.Description}{(hasNode ? " [unlocked]" : string.Empty)}");
                if (!hasNode)
                {
                    var button = AddButton(row, $"Unlock_{node.NodeId}", "Unlock", () => OnUnlockPressed(node.NodeId, professionId));
                    button.Disabled = !profession.CanUnlock(node.NodeId, unlocked);
                }
            }
        }
    }

    /// <summary>The action path the craft buttons share — tests drive this via the button signal.</summary>
    private void OnCraftPressed(string recipeId)
    {
        if (Adapter is null || !ProfessionRegistry.TryGetRecipe(recipeId, out var recipe))
        {
            return;
        }

        var material = SelectedMaterialOr(recipe!.MaterialKey);
        Adapter.Queue(new CraftAction(recipeId, material));
        // Craft has no phase term (CraftingHandlers accepts every phase) — the batch always
        // lands against whatever phase the sim is CURRENTLY sitting at (GameKernel.Tick applies
        // the queued batch before advancing), so the resolving phase IS the current one.
        _feedback!.Text = $"queued: craft {recipeId} with {material}. " +
            $"Queued — resolves when {Adapter.CurrentState.Phase} ticks. Press Advance or wait.";
    }

    /// <summary>PA6: open the forge minigame overlay for this recipe/material, configured with
    /// the profession's talent-assist data — the "Work the forge" path beside the auto-craft
    /// fallback. Standalone-openable here in this unit; PA8 adds the town station entrance.</summary>
    private void OnWorkForgePressed(Recipe recipe, string material, ProfessionDefinition profession, ImmutableSortedSet<string> unlockedTalents)
    {
        EnsureBuilt();
        _minigame!.Configure(recipe, material, profession, unlockedTalents);
        _minigame.Visible = true;
    }

    /// <summary>The minigame's ONE completed run → the ONE queued <see cref="CraftAction"/>
    /// (PKD8 single-action contract) — then the overlay closes and the G1 result ceremony opens
    /// over it.</summary>
    private void OnMinigameFinished(CraftAction action)
    {
        Adapter?.Queue(action);
        _minigame!.Visible = false;
        _feedback!.Text = $"queued: forge minigame craft {action.RecipeId} with {action.MaterialKey} " +
            $"(grade {action.PerformanceGrade}, sub-scores {string.Join("/", action.SubScores ?? ImmutableList<int>.Empty)}). " +
            $"Queued — resolves when {Adapter?.CurrentState.Phase} ticks. Press Advance or wait.";

        // Belt-and-braces: Finish() only ever fires from Stage.Quench, so the furnace should
        // already be back at baseline via OnMinigameStageChanged — this just guarantees it.
        ResolveTown()?.ForgeGlowReset();
        ShowCeremony(action);
    }

    /// <summary>Cancel queues nothing (PKD8) — just closes the overlay. A cancel mid-Smelt must
    /// not leave the furnace stuck at its elevated glow.</summary>
    private void OnMinigameCancelled()
    {
        _minigame!.Visible = false;
        ResolveTown()?.ForgeGlowReset();
    }

    /// <summary>G1: the Smelt beat ended (or the Forge/Quench/Done stage was entered) — reset the
    /// furnace glow to its resting baseline. The glow's continuous rise while Smelt IS active is
    /// driven off the live heat gauge in <see cref="_Process"/>, not this event.</summary>
    private void OnMinigameStageChanged(ForgeMinigame.Stage stage)
    {
        if (stage != ForgeMinigame.Stage.Smelt)
        {
            ResolveTown()?.ForgeGlowReset();
        }
    }

    /// <summary>G1: every anvil strike gets the hammer clang; an on-beat strike additionally fires
    /// the spark-burst/flash world VFX. <paramref name="onBeat"/> is the SAME judgement
    /// <see cref="ForgeMinigame.ForgeStrike"/> just scored (read before it mutated anything, per
    /// <see cref="ForgeMinigame.Struck"/>'s own doc) — never a second opinion.</summary>
    private void OnMinigameStruck(bool onBeat)
    {
        _hammerSfx?.Play();
        if (onBeat)
        {
            ResolveTown()?.ForgeSparkBurst();
        }
    }

    /// <summary>G1: the quench-lock world VFX (steam plume) — fired the instant the player plunges
    /// the stock, mirroring <see cref="ForgeMinigame.Quenched"/>'s own "before Lock scores it" timing.</summary>
    private void OnMinigameQuenched() => ResolveTown()?.ForgeSteamPlume();

    /// <summary>
    /// G1 result ceremony (game-feel plan §"Result ceremony"): grade stamp + quality-star row +
    /// the 3 beat sub-score pips, shown over the now-hidden minigame overlay for
    /// <see cref="CeremonySeconds"/> (or until <see cref="HideCeremony"/> is pressed early). Reads
    /// ONLY the already-emitted <see cref="CraftAction"/> — presentation, never a second scoring
    /// pass; the sting plays through <see cref="_stingSfx"/>.
    /// </summary>
    private void ShowCeremony(CraftAction action)
    {
        var band = ForgeMinigame.PreviewGrade(action.PerformanceGrade ?? 0);
        _ceremonyGrade!.Text = $"{band}!";
        _ceremonyGrade.AddThemeColorOverride("font_color", GradeColor(band));
        var filled = StarCountFor(band);
        _ceremonyStars!.Text = new string('★', filled) + new string('☆', 5 - filled);

        Clear(_ceremonyPips!);
        var subScores = action.SubScores ?? ImmutableList.Create(0, 0, 0);
        _ceremonyPips!.AddChild(StatChip("Smelt", subScores[0].ToString(), PipTone(subScores[0])));
        _ceremonyPips.AddChild(StatChip("Forge", subScores[1].ToString(), PipTone(subScores[1])));
        _ceremonyPips.AddChild(StatChip("Quench", subScores[2].ToString(), PipTone(subScores[2])));

        _stingSfx!.Stream = GradeStingTones[band];
        _stingSfx.Play();

        _ceremony!.Visible = true;
        _ceremonyRemaining = CeremonySeconds;
    }

    /// <summary>Dismiss the ceremony — the auto-timeout path (<see cref="_Process"/>) and the
    /// player's own Skip button both funnel through here.</summary>
    private void HideCeremony()
    {
        _ceremony!.Visible = false;
        _ceremonyRemaining = -1;
    }

    private static int StarCountFor(QualityGrade band) => band switch
    {
        QualityGrade.Poor => 1,
        QualityGrade.Common => 2,
        QualityGrade.Fine => 3,
        QualityGrade.Superior => 4,
        _ => 5,
    };

    /// <summary>Every color here is a named <see cref="GameTheme"/> surface (R11/KTD1) — never a
    /// local literal — recombined per grade band the same way <c>MainUi.StylePrimary</c> recombines
    /// the shared palette for its one distinguished button.</summary>
    private static Color GradeColor(QualityGrade band) => band switch
    {
        QualityGrade.Poor => GameTheme.BloodColor,
        QualityGrade.Common => GameTheme.BodyTextColor,
        QualityGrade.Fine => GameTheme.HeaderColor,
        QualityGrade.Superior => GameTheme.AccentColor,
        _ => GameTheme.EmberColor,
    };

    private static UiKit.ChipTone PipTone(int subScorePermille) => subScorePermille switch
    {
        >= 700 => UiKit.ChipTone.Positive,
        < 400 => UiKit.ChipTone.Negative,
        _ => UiKit.ChipTone.Neutral,
    };

    /// <summary>
    /// G1: lazy scene-tree lookup for the Town3D sibling under MainUi — ForgePanel has no
    /// constructor-time reference to it (this unit's scope keeps MainUi untouched beyond the
    /// build-stamp mount), so the world-VFX cues above resolve it on first use and cache the
    /// result. Null-tolerant everywhere it's called: a ForgePanel with no Town3D sibling (e.g. a
    /// future standalone-mounted test) simply gets no world VFX, never a throw.
    /// </summary>
    private Town3D? ResolveTown()
    {
        if (_town is not null && GodotObject.IsInstanceValid(_town))
        {
            return _town;
        }

        _town = GetTree()?.Root?.FindChild("Town3D", recursive: true, owned: false) as Town3D;
        return _town;
    }

    private void OnUnlockPressed(string nodeId, string professionId)
    {
        Adapter?.Queue(new UnlockTalentAction(nodeId, professionId));
        // UnlockTalent likewise has no phase term — same current-phase reasoning as OnCraftPressed.
        var phase = Adapter?.CurrentState.Phase;
        _feedback!.Text = $"queued: unlock {nodeId}. " +
            $"Queued — resolves when {phase} ticks. Press Advance or wait.";
    }

    /// <summary>Queues a one-unit vendor buy (Morning-only in the sim; the U6 gate disables the
    /// row off-Morning, and a rejection that still surfaces becomes MainUi's toast). Fixed to
    /// Morning — <see cref="GameSim.Economy.MaterialVendorHandlers"/>'s CanHandle is Morning-only,
    /// so unlike craft/unlock this action's resolving phase is never the current one off-Morning.</summary>
    private void OnBuyMaterialPressed(string materialKey)
    {
        Adapter?.Queue(new BuyMaterialAction(materialKey, 1));
        _feedback!.Text = $"queued: buy 1 {materialKey}. " +
            "Queued — resolves when Morning ticks. Press Advance or wait.";
    }

    private string SelectedMaterialOr(string recipeDefault)
    {
        var selected = _materialSelect!.Selected;
        return selected <= 0 ? recipeDefault : _materialSelect.GetItemText(selected);
    }

    private void EnsureBuilt()
    {
        if (_recipeRows is not null)
        {
            return;
        }

        var body = BuildScrollBody();
        _feedback = AddLabel(body, string.Empty);
        _feedback.Name = "ForgeFeedback";
        _materialsLabel = AddLabel(body, "MATERIALS:");

        var selectRow = AddRow(body);
        AddLabel(selectRow, "Craft with:");
        _materialSelect = new OptionButton { Name = "MaterialSelect" };
        _materialSelect.AddItem(RecipeDefaultOption);
        foreach (var key in RecipeTable.MaterialGrades.Keys)
        {
            _materialSelect.AddItem(key);
        }

        _materialSelect.ItemSelected += _ => Refresh();
        selectRow.AddChild(_materialSelect);

        AddHeader(body, "MORNING VENDOR");
        _vendorRows = new VBoxContainer { Name = "VendorRows" };
        body.AddChild(_vendorRows);

        AddHeader(body, "RECIPES");
        _recipeRows = new VBoxContainer { Name = "RecipeRows" };
        body.AddChild(_recipeRows);

        AddHeader(body, "TALENTS");
        _talentRows = new VBoxContainer { Name = "TalentRows" };
        body.AddChild(_talentRows);

        // PA6: the forge minigame overlay — added LAST (after the scroll body above) so it
        // draws on top, self-contained (PKD8), hidden until "Work the forge" opens it.
        _minigame = new ForgeMinigame { Visible = false };
        AddChild(_minigame);
        _minigame.Finished += OnMinigameFinished;
        _minigame.Cancelled += OnMinigameCancelled;
        // G1 staging: forward the minigame's presentation-only beat cues to the forge station's
        // world VFX (Town3D) and to this panel's own SFX — see each handler's own doc.
        _minigame.StageChanged += OnMinigameStageChanged;
        _minigame.Struck += OnMinigameStruck;
        _minigame.Quenched += OnMinigameQuenched;

        BuildCeremony();
        BuildSfx();
    }

    /// <summary>
    /// G1 result ceremony (game-feel plan §"Result ceremony"): a themed card — centered over a
    /// FullRect, input-blocking (<c>MouseFilter.Stop</c>, same idiom the minigame overlay itself
    /// uses) backdrop — built once, added LAST (after <see cref="_minigame"/>) so it draws over
    /// everything else in this panel, hidden until <see cref="ShowCeremony"/> arms it.
    /// </summary>
    private void BuildCeremony()
    {
        _ceremony = new Control { Name = "ForgeCeremonyOverlay", Visible = false, MouseFilter = MouseFilterEnum.Stop };
        _ceremony.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_ceremony);

        var center = new CenterContainer { Name = "ForgeCeremonyCenter" };
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _ceremony.AddChild(center);

        var card = Card("ForgeCeremonyCard");
        center.AddChild(card);

        var body = new VBoxContainer { Name = "ForgeCeremonyBody" };
        card.AddChild(body);

        _ceremonyGrade = AddLabel(body, string.Empty);
        _ceremonyGrade.Name = "ForgeCeremonyGrade";
        _ceremonyGrade.HorizontalAlignment = HorizontalAlignment.Center;
        _ceremonyGrade.ThemeTypeVariation = GameTheme.HeaderThemeType;
        _ceremonyGrade.AddThemeFontSizeOverride("font_size", GameTheme.HeaderFontSize);

        _ceremonyStars = AddLabel(body, string.Empty);
        _ceremonyStars.Name = "ForgeCeremonyStars";
        _ceremonyStars.HorizontalAlignment = HorizontalAlignment.Center;

        _ceremonyPips = AddRow(body);
        _ceremonyPips.Name = "ForgeCeremonyPips";

        var skip = AddButton(body, "ForgeCeremonySkip", "Skip", HideCeremony);
        skip.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
    }

    /// <summary>G1 forge juice (game-feel plan §"Forge juice"): two tiny procedural
    /// <see cref="AudioStreamPlayer"/>s — no external audio asset committed for either (see
    /// <see cref="MakeTone"/>'s own doc for why). <see cref="OnMinigameStruck"/> retriggers
    /// <see cref="_hammerSfx"/> on every strike; <see cref="ShowCeremony"/> swaps
    /// <see cref="_stingSfx"/>'s stream to the grade-appropriate tone before playing it.
    /// Headless-safe: Godot's dummy audio driver accepts <c>Play()</c> without a real output
    /// device, so an engine test never has to guard around this.</summary>
    private void BuildSfx()
    {
        _hammerSfx = new AudioStreamPlayer { Name = "ForgeHammerSfx", Stream = HammerClangTone };
        AddChild(_hammerSfx);

        _stingSfx = new AudioStreamPlayer { Name = "ForgeStingSfx" };
        AddChild(_stingSfx);
    }

    // ── G1 procedural SFX — short synthesized tones, no external audio asset ──────────────────
    private const int SfxSampleRate = 22050;

    private static readonly AudioStreamWav HammerClangTone = MakeTone(180f, 0.09f, secondaryHz: 620f, amplitude: 0.6f);

    private static readonly Dictionary<QualityGrade, AudioStreamWav> GradeStingTones = new()
    {
        [QualityGrade.Poor] = MakeTone(196f, 0.35f),
        [QualityGrade.Common] = MakeTone(262f, 0.35f),
        [QualityGrade.Fine] = MakeTone(330f, 0.4f),
        [QualityGrade.Superior] = MakeTone(392f, 0.45f, secondaryHz: 494f),
        [QualityGrade.Masterwork] = MakeTone(523f, 0.55f, secondaryHz: 784f),
    };

    /// <summary>
    /// A short synthesized tone (optionally a two-note chord via <paramref name="secondaryHz"/>)
    /// with a linear decay envelope — placeholder-quality "juice" audio that needs no external
    /// asset (and so nothing to license-track for CMMC/SOC 2 purposes). 16-bit mono PCM, built
    /// once into a <see langword="static readonly"/> field per cue above — never regenerated per
    /// play.
    /// </summary>
    private static AudioStreamWav MakeTone(float hz, float durationSeconds, float? secondaryHz = null, float amplitude = 0.5f)
    {
        var sampleCount = (int)(SfxSampleRate * durationSeconds);
        var data = new byte[sampleCount * 2]; // 16-bit mono
        for (var i = 0; i < sampleCount; i++)
        {
            var t = i / (float)SfxSampleRate;
            var envelope = 1f - t / durationSeconds;
            var wave = Mathf.Sin(2f * Mathf.Pi * hz * t);
            if (secondaryHz is { } second)
            {
                wave = (wave + Mathf.Sin(2f * Mathf.Pi * second * t)) * 0.5f;
            }

            var sample = Mathf.Clamp(wave * amplitude * envelope, -1f, 1f);
            var s16 = (short)(sample * short.MaxValue);
            data[i * 2] = (byte)(s16 & 0xFF);
            data[i * 2 + 1] = (byte)((s16 >> 8) & 0xFF);
        }

        return new AudioStreamWav
        {
            Data = data,
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = SfxSampleRate,
            Stereo = false,
        };
    }
}
