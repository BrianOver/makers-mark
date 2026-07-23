using System;
using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Professions;
using Godot;
using GodotClient.Ui;

namespace GodotClient.Minigames;

/// <summary>
/// PA6 (spec §Blacksmith minigame; PKD1/PKD3/PKD8): the three-beat forge overlay — Smelt
/// (<see cref="SmeltBeat"/>) → Forge (<see cref="ForgeBeat"/>) → Quench (<see cref="QuenchBeat"/>)
/// — a self-contained focus overlay that receives recipe/material/assist context via
/// <see cref="Configure"/>, runs on its own accumulated clock (public <see cref="Advance"/>, the
/// SAME house pattern <c>ShopStage.Advance</c> already proves — no wall-clock, no engine RNG
/// anywhere in this file), and on completion raises <see cref="Finished"/> with EXACTLY ONE
/// <see cref="CraftAction"/> carrying the folded <see cref="CraftAction.PerformanceGrade"/> and
/// the three beat <see cref="CraftAction.SubScores"/> in beat order (smelt, forge, quench).
/// <see cref="Cancel"/> raises <see cref="Cancelled"/> instead — the caller (<see
/// cref="GodotClient.Panels.ForgePanel"/>) queues nothing.
///
/// <para><b>Adapter-only (KTD2):</b> this class computes ONLY the presentation-layer capture — the
/// per-mille fold of three sub-scores into one grade (PKD1: "Godot computes it, sim consumes it").
/// The actual quality math (grade→band, RNG jitter, material ceiling) lives in
/// <c>QualityRoller.RollActive</c>, sim-side, and never runs here.</para>
///
/// <para><b>Difficulty scaling:</b> <see cref="ComputeDifficultyPermille"/> derives one scalar from
/// recipe tier + material grade (mirrors <c>QualityRoller</c>'s own material-step axis); every beat's
/// band width / rise-or-drift rate / timing window scales off it. <b>Talent assists</b>
/// (<see cref="AggregateAssist"/>) read <c>ProfessionDefinition.MinigameAssists</c> for whichever
/// talent nodes are unlocked — Weapon Specialist's bonus is scoped to <see cref="ItemSlot.Weapon"/>
/// recipes only, mirroring the retired <c>SlotShift</c> semantics (see
/// <c>ProfessionRegistry.Blacksmith</c>'s own doc note) — and widen bands / slow drift / forgive
/// off-beat strikes: mastery makes the act easier, visibly, never a hidden number.</para>
/// </summary>
public sealed partial class ForgeMinigame : PanelContainer
{
    public enum Stage
    {
        Smelt,
        Forge,
        Quench,
        Done,
    }

    // ── Folding weights (PA6 spec: "EXPORTED DATA — tunable without code") ──────────────────
    // Three per-mille sub-scores fold into one per-mille PerformanceGrade. Forge gets the
    // heaviest weight (the beat with the carry-forward flaw baked in — a bad smelt already
    // discounted itself into the forge sub-score, so double-weighting it would double-punish).
    public const double SmeltWeight = 0.30;
    public const double ForgeWeight = 0.40;
    public const double QuenchWeight = 0.30;

    // ── Base difficulty knobs (tier-1, material-grade == tier baseline; PA6 spec: "difficulty
    // scales with recipe tier + material grade") — tunable constants, never sim rules. ────────
    public const int BaseSmeltBandWidthPermille = 260;
    public const int BaseSmeltRisePermilliePerSecond = 220;
    public const double BaseSmeltTimeoutSeconds = 5.0;
    public const double BaseForgeBeatPeriodSeconds = 0.8;
    public const double BaseForgeOnBeatWindowSeconds = 0.18;
    public const double BaseForgeCoolSeconds = 6.0;
    public const double BaseQuenchOscillationHz = 0.5;
    public const int BaseQuenchBandWidthPermille = 260;
    public const double BaseQuenchTimeoutSeconds = 6.0;

    /// <summary>Difficulty reference point (500 = tier-1, material grade == tier — the neutral
    /// baseline every Base* constant above is tuned at).</summary>
    private const int NeutralDifficultyPermille = 500;

    private const int DifficultyFloor = 200;
    private const int DifficultyCeiling = 1200;

    /// <summary>Per tier above 1, difficulty rises by this many per-mille points.</summary>
    private const int DifficultyPerTier = 150;

    /// <summary>Per material-grade step above/below the recipe's tier, difficulty falls/rises by
    /// this many per-mille points (better ore eases the act, mirrors the sim's own ceiling axis).</summary>
    private const int DifficultyPerMaterialStep = 100;

    private int _difficultyPermille = NeutralDifficultyPermille;
    private int _offBeatForgivenessPermille;

    public string RecipeId { get; private set; } = string.Empty;
    public string MaterialKey { get; private set; } = string.Empty;
    public Stage Current { get; private set; } = Stage.Smelt;
    public SmeltBeat Smelt { get; private set; } = new(BaseSmeltBandWidthPermille, BaseSmeltRisePermilliePerSecond, BaseSmeltTimeoutSeconds);
    public ForgeBeat Forge { get; private set; } = new(BaseForgeBeatPeriodSeconds, BaseForgeOnBeatWindowSeconds, BaseForgeCoolSeconds, 0, false);
    public QuenchBeat Quench { get; private set; } = new(BaseQuenchOscillationHz, BaseQuenchBandWidthPermille, BaseQuenchTimeoutSeconds);
    public bool Completed { get; private set; }
    public bool WasCancelled { get; private set; }

    /// <summary>The exact action <see cref="Finished"/> carried — test/inspection visibility.</summary>
    public CraftAction? EmittedAction { get; private set; }

    /// <summary>Raised EXACTLY ONCE, on beat completion, with the one action to queue.</summary>
    public event Action<CraftAction>? Finished;

    /// <summary>Raised on <see cref="Cancel"/> — the caller queues nothing.</summary>
    public event Action? Cancelled;

    // ── G1 staging events (game-feel plan §"World VFX keyed to beat state" / §"Result
    // ceremony") — purely additive presentation signals. None of these read or write any
    // scoring state; they mirror decisions the beats already made (or are about to make) so the
    // host (ForgePanel) can key world VFX/SFX to the exact moment without polling every frame or
    // re-deriving the beat math. Never affects Advance/Finish/FoldGrade. ──────────────────────

    /// <summary>Raised whenever <see cref="Current"/> changes: <see cref="Configure"/>'s reset to
    /// Smelt, <see cref="EnterForge"/>, <see cref="EnterQuench"/>, and <see cref="Finish"/>'s move
    /// to Done. Drives stage-keyed world VFX (e.g. reset the furnace glow the instant Smelt ends).</summary>
    public event Action<Stage>? StageChanged;

    /// <summary>Raised inside <see cref="ForgeStrike"/> with whether THAT strike landed on-beat —
    /// judged via <see cref="ForgeBeat.IsOnBeatNow"/> BEFORE <see cref="ForgeBeat.Strike"/> itself
    /// runs, so this is a read of the same judgement the beat is about to score, never a second
    /// opinion. Drives the spark-burst/flash VFX and the hammer-clang SFX.</summary>
    public event Action<bool>? Struck;

    /// <summary>Raised inside <see cref="QuenchLock"/>, before <see cref="QuenchBeat.Lock"/> runs —
    /// drives the steam-plume VFX at the moment the player plunges the stock.</summary>
    public event Action? Quenched;

    private Label _titleLabel = null!;
    private Label _stageLabel = null!;
    private Label _gaugeLabel = null!;
    private ProgressBar _gaugeBar = null!;
    private Label _drossLabel = null!;
    private Button _smeltStop = null!;
    private Button _forgeStrike = null!;
    private Button _quenchLock = null!;
    private Button _cancel = null!;
    private bool _built;

    public override void _Ready() => EnsureBuilt();

    public override void _Process(double delta) => Advance(delta);

    /// <summary>
    /// Bind fresh beat instances scaled to this recipe/material/talent context and reset to the
    /// Smelt stage. Safe to call repeatedly (e.g., the player reopens the overlay for a different
    /// recipe) — always leaves a clean, un-completed run.
    /// </summary>
    public void Configure(
        Recipe recipe, string materialKey, ProfessionDefinition profession, ImmutableSortedSet<string> unlockedTalents)
    {
        EnsureBuilt();

        RecipeId = recipe.RecipeId;
        MaterialKey = materialKey;
        Current = Stage.Smelt;
        Completed = false;
        WasCancelled = false;
        EmittedAction = null;

        var materialGrade = RecipeTable.MaterialGrades.TryGetValue(materialKey, out var grade) ? grade : recipe.Tier;
        _difficultyPermille = ComputeDifficultyPermille(recipe.Tier, materialGrade);
        var assist = AggregateAssist(profession, unlockedTalents, recipe.Slot);
        _offBeatForgivenessPermille = assist.OffBeatForgiveness;

        var smeltBand = Math.Max(60, BaseSmeltBandWidthPermille * NeutralDifficultyPermille / _difficultyPermille + assist.SweetZoneWidthBonus);
        var smeltRise = Math.Max(20, (int)Math.Round(ScaleByDifficultyAndDrift(BaseSmeltRisePermilliePerSecond, assist.DriftRateReduction)));
        Smelt = new SmeltBeat(smeltBand, smeltRise, BaseSmeltTimeoutSeconds);

        var quenchBand = Math.Max(60, BaseQuenchBandWidthPermille * NeutralDifficultyPermille / _difficultyPermille + assist.SweetZoneWidthBonus);
        var quenchHz = Math.Max(0.1, ScaleByDifficultyAndDrift(BaseQuenchOscillationHz, assist.DriftRateReduction));
        Quench = new QuenchBeat(quenchHz, quenchBand, BaseQuenchTimeoutSeconds);

        // Forge is rebuilt once Smelt completes (it needs Smelt.Impurity for the carry-forward
        // cap) — see EnterForge. A neutral placeholder here just keeps the property non-null.
        Forge = new ForgeBeat(BaseForgeBeatPeriodSeconds, BaseForgeOnBeatWindowSeconds, BaseForgeCoolSeconds, _offBeatForgivenessPermille, false);

        RepaintUi();
        StageChanged?.Invoke(Current);
    }

    /// <summary>Advance the current beat by <paramref name="delta"/> accumulated-clock seconds —
    /// public so tests drive scripted runs deterministically (no wall-clock, no engine RNG; the
    /// same house pattern <c>ShopStage.Advance</c> already proves).</summary>
    public void Advance(double delta)
    {
        if (Completed || WasCancelled)
        {
            return;
        }

        switch (Current)
        {
            case Stage.Smelt:
                Smelt.Advance(delta);
                break;
            case Stage.Forge:
                Forge.Advance(delta);
                break;
            case Stage.Quench:
                Quench.Advance(delta);
                break;
        }

        CheckStageTransition();
        RepaintUi();
    }

    /// <summary>Smelt-stage input: pull the stock now.</summary>
    public void SmeltStop()
    {
        if (Current != Stage.Smelt || Completed || WasCancelled)
        {
            return;
        }

        Smelt.Stop();
        CheckStageTransition();
        RepaintUi();
    }

    /// <summary>Forge-stage input: strike the anvil now.</summary>
    public void ForgeStrike()
    {
        if (Current != Stage.Forge || Completed || WasCancelled)
        {
            return;
        }

        var onBeat = Forge.IsOnBeatNow(); // presentation cue — read BEFORE the real scoring call
        Forge.Strike();
        Struck?.Invoke(onBeat);
        CheckStageTransition();
        RepaintUi();
    }

    /// <summary>Quench-stage input: plunge the stock now.</summary>
    public void QuenchLock()
    {
        if (Current != Stage.Quench || Completed || WasCancelled)
        {
            return;
        }

        Quenched?.Invoke();
        Quench.Lock();
        CheckStageTransition();
        RepaintUi();
    }

    /// <summary>Abandon the run — queues nothing (<see cref="Cancelled"/> only).</summary>
    public void Cancel()
    {
        if (Completed || WasCancelled)
        {
            return;
        }

        WasCancelled = true;
        Cancelled?.Invoke();
        RepaintUi();
    }

    private void CheckStageTransition()
    {
        switch (Current)
        {
            case Stage.Smelt when Smelt.Complete:
                EnterForge();
                break;
            case Stage.Forge when Forge.Complete:
                EnterQuench();
                break;
            case Stage.Quench when Quench.Complete:
                Finish();
                break;
        }
    }

    private void EnterForge()
    {
        Current = Stage.Forge;
        var forgePeriod = Math.Max(0.3, BaseForgeBeatPeriodSeconds * _difficultyPermille / (double)NeutralDifficultyPermille);
        var forgeWindow = Math.Max(0.05, BaseForgeOnBeatWindowSeconds * NeutralDifficultyPermille / (double)_difficultyPermille);
        Forge = new ForgeBeat(forgePeriod, forgeWindow, BaseForgeCoolSeconds, _offBeatForgivenessPermille, Smelt.Impurity);
        StageChanged?.Invoke(Current);
    }

    private void EnterQuench()
    {
        Current = Stage.Quench;
        StageChanged?.Invoke(Current);
    }

    private void Finish()
    {
        Current = Stage.Done;
        Completed = true;
        var subScores = ImmutableList.Create(Smelt.SubScorePermille, Forge.SubScorePermille, Quench.SubScorePermille);
        var performanceGrade = FoldGrade(subScores);
        var action = new CraftAction(RecipeId, MaterialKey, performanceGrade, Puzzle: null, SubScores: subScores);
        EmittedAction = action;
        StageChanged?.Invoke(Current);
        Finished?.Invoke(action);
    }

    /// <summary>The PA6-pinned fold: three per-mille sub-scores (in beat order — smelt, forge,
    /// quench) → one per-mille <see cref="CraftAction.PerformanceGrade"/>. Public/static so a test
    /// can assert the fold in isolation from beat scoring.</summary>
    public static int FoldGrade(ImmutableList<int> subScores) => Math.Clamp(
        (int)Math.Round(subScores[0] * SmeltWeight + subScores[1] * ForgeWeight + subScores[2] * QuenchWeight), 0, 1000);

    /// <summary>G1 result ceremony: a presentation-only PREVIEW of which <see cref="QualityGrade"/>
    /// band this run's folded grade is heading toward — mirrors <c>QualityRoller.RollActive</c>'s
    /// own band thresholds (200/550/780/930) but deliberately WITHOUT its ±25 jitter or its
    /// material-grade ceiling, both of which only apply sim-side once the queued
    /// <see cref="CraftAction"/> actually resolves. The active model is built so skill dominates
    /// that later roll (a jitter/ceiling swing can shift one band at a seam, never skip a whole
    /// band on its own — see <c>QualityRoller</c>'s own remarks), so this preview is a good stand-in
    /// for the ceremony stamp without ever claiming to BE the final rolled <see cref="QualityGrade"/>.
    /// Public/static so a test can pin the band thresholds independently of a live run.</summary>
    public static QualityGrade PreviewGrade(int performanceGradePermille)
    {
        var clamped = Math.Clamp(performanceGradePermille, 0, 1000);
        return clamped switch
        {
            < 200 => QualityGrade.Poor,
            < 550 => QualityGrade.Common,
            < 780 => QualityGrade.Fine,
            < 930 => QualityGrade.Superior,
            _ => QualityGrade.Masterwork,
        };
    }

    /// <summary>Scales a base rise-rate/oscillation-Hz value by the difficulty axis and by the
    /// talent-assist drift reduction — kept as a <see langword="double"/> (never rounded here) so
    /// a sub-1-per-second base like <see cref="BaseQuenchOscillationHz"/> doesn't truncate to
    /// zero; callers that need an integer per-mille rate round the RESULT, not this method.</summary>
    private double ScaleByDifficultyAndDrift(double baseValue, int driftRateReductionPermille)
    {
        var difficultyScaled = baseValue * _difficultyPermille / (double)NeutralDifficultyPermille;
        return difficultyScaled * (1000 - Math.Clamp(driftRateReductionPermille, 0, 900)) / 1000.0;
    }

    /// <summary>One scalar difficulty axis from recipe tier + material grade — higher = harder.
    /// Mirrors <c>QualityRoller</c>'s own <c>materialGrade - recipe.Tier</c> ceiling axis so a
    /// player reading "better ore eases the act" sees the SAME material relationship the sim's
    /// quality ceiling already rewards.</summary>
    private static int ComputeDifficultyPermille(int tier, int materialGrade)
    {
        var materialStep = materialGrade - tier;
        var difficulty = NeutralDifficultyPermille + (DifficultyPerTier * (tier - 1)) - (DifficultyPerMaterialStep * materialStep);
        return Math.Clamp(difficulty, DifficultyFloor, DifficultyCeiling);
    }

    private readonly record struct AssistTotals(int SweetZoneWidthBonus, int DriftRateReduction, int OffBeatForgiveness);

    /// <summary>Sums every unlocked talent's <c>MinigameAssist</c> data (PA2/PKD3). Weapon
    /// Specialist is weapon-recipe-scoped only — mirrors the retired <c>SlotShift</c> semantics the
    /// sim-side doc note assigns to "the adapter" (<c>ProfessionRegistry.Blacksmith</c>'s remarks).</summary>
    private static AssistTotals AggregateAssist(
        ProfessionDefinition profession, ImmutableSortedSet<string> unlockedTalents, ItemSlot recipeSlot)
    {
        var sweetZone = 0;
        var drift = 0;
        var offBeat = 0;
        foreach (var (nodeId, assist) in profession.MinigameAssists)
        {
            if (!unlockedTalents.Contains(nodeId))
            {
                continue;
            }

            if (nodeId == TalentTree.WeaponSpecialist && recipeSlot != ItemSlot.Weapon)
            {
                continue;
            }

            sweetZone += assist.SweetZoneWidthBonus;
            drift += assist.DriftRateReduction;
            offBeat += assist.OffBeatForgiveness;
        }

        return new AssistTotals(sweetZone, drift, offBeat);
    }

    private void EnsureBuilt()
    {
        if (_built)
        {
            return;
        }

        Name = "ForgeMinigame";
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop; // an open overlay owns clicks — never passes through to what it covers

        var body = new VBoxContainer { Name = "ForgeMinigameBody" };
        AddChild(body);

        _titleLabel = new Label { Name = "ForgeMinigameTitle" };
        _titleLabel.AddThemeColorOverride("font_color", GameTheme.HeaderColor);
        _titleLabel.ThemeTypeVariation = GameTheme.HeaderThemeType;
        body.AddChild(_titleLabel);

        _stageLabel = new Label { Name = "ForgeMinigameStage" };
        body.AddChild(_stageLabel);

        _gaugeBar = new ProgressBar { Name = "ForgeMinigameGaugeBar", MinValue = 0, MaxValue = 1000 };
        body.AddChild(_gaugeBar);

        _gaugeLabel = new Label { Name = "ForgeMinigameGauge" };
        body.AddChild(_gaugeLabel);

        _drossLabel = new Label { Name = "ForgeMinigameDross", Visible = false };
        _drossLabel.AddThemeColorOverride("font_color", GameTheme.BloodColor);
        body.AddChild(_drossLabel);

        var buttonRow = new HBoxContainer { Name = "ForgeMinigameButtons" };
        body.AddChild(buttonRow);

        _smeltStop = new Button { Name = "SmeltStop", Text = "Stop!" };
        _smeltStop.Pressed += SmeltStop;
        buttonRow.AddChild(_smeltStop);

        _forgeStrike = new Button { Name = "ForgeStrike", Text = "Strike!" };
        _forgeStrike.Pressed += ForgeStrike;
        buttonRow.AddChild(_forgeStrike);

        _quenchLock = new Button { Name = "QuenchLock", Text = "Quench!" };
        _quenchLock.Pressed += QuenchLock;
        buttonRow.AddChild(_quenchLock);

        _cancel = new Button { Name = "ForgeMinigameCancel", Text = "Cancel" };
        _cancel.Pressed += Cancel;
        buttonRow.AddChild(_cancel);

        _built = true;
        RepaintUi();
    }

    /// <summary>Render-only — reads the current beat's live numbers, writes no state. Called after
    /// every state-changing call above (never a per-frame poll independent of them).</summary>
    private void RepaintUi()
    {
        if (!_built)
        {
            return;
        }

        _titleLabel.Text = $"Forge: {RecipeId}";
        _smeltStop.Visible = Current == Stage.Smelt;
        _forgeStrike.Visible = Current == Stage.Forge;
        _quenchLock.Visible = Current == Stage.Quench;

        switch (Current)
        {
            case Stage.Smelt:
                _stageLabel.Text = "SMELT — stop it in the sweet zone";
                _gaugeBar.Value = Smelt.HeatPermille;
                _gaugeLabel.Text = $"Heat: {Smelt.HeatPermille}";
                _drossLabel.Visible = false;
                break;
            case Stage.Forge:
                _stageLabel.Text = "FORGE — strike on the beat";
                _gaugeBar.Value = Forge.ProgressPermille;
                _gaugeLabel.Text = $"Progress: {Forge.ProgressPermille} — strikes {Forge.StrikeCount}, mars {Forge.MarCount}";
                _drossLabel.Visible = Forge.HasDross;
                _drossLabel.Text = "Dross from the smelt mars the stock.";
                break;
            case Stage.Quench:
                _stageLabel.Text = "QUENCH — plunge on the readout";
                _gaugeBar.Value = Quench.NeedlePermille;
                _gaugeLabel.Text = $"Reading: {Quench.NeedlePermille}";
                break;
            case Stage.Done:
                _stageLabel.Text = WasCancelled ? "Cancelled." : $"Done — grade {EmittedAction?.PerformanceGrade}.";
                break;
        }
    }
}
