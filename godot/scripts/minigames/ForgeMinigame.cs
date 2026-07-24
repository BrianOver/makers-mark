using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Flavor;
using GameSim.Professions;
using Godot;
using GodotClient.Ui;

namespace GodotClient.Minigames;

/// <summary>
/// U23d ("Anvil Map"): the tactile forge overlay — a HARD REPLACEMENT of the old three-beat
/// Smelt/Forge/Quench minigame. Renders the shared target line
/// (<see cref="GameSim.Crafting.ForgePath.Generate"/>/<see cref="GameSim.Crafting.ForgePath.HeatAt"/>
/// — the SAME sim-owned polyline the scorer grades against, so what the player aims at is exactly
/// what gets scored) on a plain 2D canvas (never a 3D <c>SubViewport</c> — a known gdUnit headless
/// hang) and drives a cursor (the billet: X = shape progress, Y = current heat) the player steers
/// with a hammer strike (advance X, cost heat, bonus near the tempo window), bellows (hold: raise
/// heat, shape drifts back slightly — mutually exclusive with hammering), and a finale plunge once
/// the shape reaches the end. Runs on the SAME accumulated-clock <see cref="Advance"/> pattern the
/// old minigame (and <c>ShopStage</c>) already prove — no wall-clock, no engine RNG anywhere in the
/// path that shapes the emitted trace.
///
/// <para><b>Adapter-only (KTD2):</b> this class only captures the presentation-layer trace — an
/// INTEGER (xPermille, yPermille) sample stream plus strike events, quantized at a fixed cadence
/// and capped at <see cref="MaxSamples"/> pairs. It builds ONE <see cref="ForgeTraceInput"/> and
/// rides it on <see cref="CraftAction.Puzzle"/> (PKD1 dual-mode craft seam) — the actual quality
/// math (deviation scoring, grade fold, RNG jitter, material ceiling) lives sim-side in
/// <c>ForgeScorer</c>/<c>QualityRoller</c> and never runs here. <see cref="PreviewGradePermille"/>/
/// <see cref="PreviewSubScores"/> call that SAME pure scorer read-only for an immediate UI preview
/// (mirrors <c>AlchemyBrewPuzzle</c>'s own preview) — never a second set of rules.</para>
///
/// <para><b>Single-action contract (PKD8, same as the old minigame and the alchemist's puzzle):</b>
/// <see cref="Finished"/> fires EXACTLY ONCE, on <see cref="Plunge"/>, carrying one
/// <see cref="CraftAction"/> whose <see cref="CraftAction.Puzzle"/> is the captured
/// <see cref="ForgeTraceInput"/> (<see cref="CraftAction.PerformanceGrade"/> stays null — the
/// trace is the single source the sim scores); <see cref="Cancel"/> raises <see cref="Cancelled"/>
/// instead and the caller queues nothing.</para>
/// </summary>
public sealed partial class ForgeMinigame : PanelContainer
{
    // ── Tunable adapter-only knobs (never sim rules — only the resulting integer trace crosses
    // the KTD2 boundary). The target line's own tier/weight-driven shape (ForgePath) already
    // carries the "harder recipe = harder track" difficulty axis, so these stay constant across
    // recipes; only the field to steer through changes shape.  ─────────────────────────────────
    public const double SampleIntervalSeconds = 0.1;
    public const int MaxSamples = 256;

    public const int HeatDrainPermillePerSecond = 70;
    public const int BellowsRaisePermillePerSecond = 260;
    public const int BellowsDriftBackPermillePerSecond = 50;
    public const int StrikeHeatCostPermille = 90;
    public const int StrikeBaseAdvancePermille = 35;
    public const double StrikeOnTempoBonusMultiplier = 2.2;
    public const double TempoPeriodSeconds = 0.6;
    public const int TempoOnBeatWindowPermille = 180;

    public string RecipeId { get; private set; } = string.Empty;
    public string MaterialKey { get; private set; } = string.Empty;

    /// <summary>The integer seed selecting this craft's forging-line variant — derived
    /// deterministically from the recipe id + day (<see cref="Configure"/>), never RNG, and
    /// carried verbatim on the emitted <see cref="ForgeTraceInput.PathSeed"/> so the sim
    /// regenerates the IDENTICAL line this overlay rendered.</summary>
    public int PathSeed { get; private set; }

    /// <summary>The shared target line (<c>ForgePath.Generate</c>) this overlay renders — the
    /// SAME polyline the sim scorer regenerates from <see cref="PathSeed"/>.</summary>
    public ImmutableList<int> Path { get; private set; } = ImmutableList<int>.Empty;

    /// <summary>Shape progress, per-mille [0..1000] — the cursor's X axis.</summary>
    public int ShapeXPermille { get; private set; }

    /// <summary>Current heat, per-mille [0..1000] — the cursor's Y axis.</summary>
    public int HeatYPermille { get; private set; } = 500;

    /// <summary>True while the bellows are held — hammering is disabled during a pump
    /// (the two inputs are mutually exclusive, per spec).</summary>
    public bool IsPumping { get; private set; }

    public bool Completed { get; private set; }
    public bool WasCancelled { get; private set; }

    /// <summary>The exact action <see cref="Finished"/> carried — test/inspection visibility.</summary>
    public CraftAction? EmittedAction { get; private set; }

    /// <summary>A read-only UI preview of the grade <c>ForgeScorer</c> will compute for this exact
    /// trace (same pure scorer, called here only for immediate feedback) — NEVER written onto
    /// <see cref="CraftAction.PerformanceGrade"/>, which stays null per the dual-mode contract.</summary>
    public int? PreviewGradePermille { get; private set; }

    /// <summary>The scorer's smelt/forge/quench preview triple — rides <see cref="CraftAction.SubScores"/>
    /// as ledger flavor DATA (same role as the old beat sub-scores), never rules.</summary>
    public ImmutableList<int>? PreviewSubScores { get; private set; }

    /// <summary>Raised EXACTLY ONCE, on <see cref="Plunge"/>, with the one action to queue.</summary>
    public event Action<CraftAction>? Finished;

    /// <summary>Raised on <see cref="Cancel"/> — the caller queues nothing.</summary>
    public event Action? Cancelled;

    /// <summary>Raised inside <see cref="ForgeStrike"/> with whether THAT strike landed inside the
    /// tempo window — judged before the strike itself mutates anything, so a listener is reading
    /// the same judgement the trace is about to score, never a second opinion. Drives the
    /// spark-burst/flash VFX + hammer-clang SFX (G1 staging, same idiom as the old minigame).</summary>
    public event Action<bool>? Struck;

    /// <summary>Raised inside <see cref="Plunge"/>, before the run finishes — drives the
    /// steam-plume VFX at the moment the player plunges the stock.</summary>
    public event Action? Quenched;

    private readonly List<int> _samples = new();
    private readonly List<int> _strikes = new();
    private double _elapsed;
    private double _sampleAccumulator;

    private Recipe? _recipe;
    private ProfessionDefinition? _profession;
    private ImmutableSortedSet<string> _unlockedTalents = ImmutableSortedSet<string>.Empty;

    private Label _titleLabel = null!;
    private AnvilMapCanvas _canvas = null!;
    private Label _readoutLabel = null!;
    private Button _hammerButton = null!;
    private Button _bellowsButton = null!;
    private Button _plungeButton = null!;
    private Button _cancelButton = null!;
    private bool _built;

    public override void _Ready() => EnsureBuilt();

    public override void _Process(double delta) => Advance(delta);

    /// <summary>
    /// Bind a fresh run for this recipe/material/talent context and regenerate the shared target
    /// line from a seed derived (no RNG — <c>StableHash</c>, the same project-owned hash
    /// <c>ForgePath</c> itself uses) from the recipe id + <paramref name="day"/>, so reopening the
    /// SAME recipe on a different day gets a different — but still deterministic and sim-agreeing
    /// — line. Safe to call repeatedly (e.g. the player reopens for a different recipe) — always
    /// leaves a clean, un-completed run.
    /// </summary>
    public void Configure(
        Recipe recipe, string materialKey, ProfessionDefinition profession, ImmutableSortedSet<string> unlockedTalents, int day)
    {
        EnsureBuilt();

        RecipeId = recipe.RecipeId;
        MaterialKey = materialKey;
        _recipe = recipe;
        _profession = profession;
        _unlockedTalents = unlockedTalents;

        PathSeed = unchecked((int)StableHash.Avalanche(StableHash.Mix(StableHash.HashString(recipe.RecipeId), unchecked((ulong)day))));
        Path = ForgePath.Generate(recipe.Tier, recipe.Slot, recipe.BaseStats.Weight, PathSeed);

        ShapeXPermille = 0;
        HeatYPermille = ForgePath.HeatAt(Path, 0);
        IsPumping = false;
        Completed = false;
        WasCancelled = false;
        EmittedAction = null;
        PreviewGradePermille = null;
        PreviewSubScores = null;
        _samples.Clear();
        _strikes.Clear();
        _elapsed = 0;
        _sampleAccumulator = 0;

        RepaintUi();
    }

    /// <summary>Advance the run by <paramref name="delta"/> accumulated-clock seconds — public so
    /// tests drive scripted runs deterministically (no wall-clock, no engine RNG; the same house
    /// pattern <c>ShopStage.Advance</c>/the old <c>ForgeMinigame</c> already prove). Heat drains
    /// over time (the pursuit pressure) unless the bellows are held, in which case heat rises and
    /// shape drifts back slightly (can't hammer while pumping). Samples the cursor at a fixed
    /// cadence, capped at <see cref="MaxSamples"/> pairs.</summary>
    public void Advance(double delta)
    {
        if (Completed || WasCancelled || delta <= 0)
        {
            return;
        }

        _elapsed += delta;

        if (IsPumping)
        {
            HeatYPermille = Math.Min(1000, HeatYPermille + (int)Math.Round(BellowsRaisePermillePerSecond * delta));
            ShapeXPermille = Math.Max(0, ShapeXPermille - (int)Math.Round(BellowsDriftBackPermillePerSecond * delta));
        }
        else
        {
            HeatYPermille = Math.Max(0, HeatYPermille - (int)Math.Round(HeatDrainPermillePerSecond * delta));
        }

        _sampleAccumulator += delta;
        while (_sampleAccumulator >= SampleIntervalSeconds && _samples.Count / 2 < MaxSamples)
        {
            RecordSample();
            _sampleAccumulator -= SampleIntervalSeconds;
        }

        RepaintUi();
    }

    /// <summary>Hammer strike: advances shape-X proportional to the CURRENT heat (a cold billet
    /// barely moves), costs heat, and advances further when it lands inside the tempo window.
    /// No-op while pumping (mutually exclusive inputs) or once the shape has already reached the
    /// path's end (only <see cref="Plunge"/> is legal there).</summary>
    public void ForgeStrike()
    {
        if (Completed || WasCancelled || IsPumping || ShapeXPermille >= 1000)
        {
            return;
        }

        var tempoError = TempoErrorPermilleNow();
        var onTempo = tempoError <= TempoOnBeatWindowPermille;
        RecordStrike(tempoError);

        var multiplier = onTempo ? StrikeOnTempoBonusMultiplier : 1.0;
        var advance = (int)Math.Round(StrikeBaseAdvancePermille * (HeatYPermille / 1000.0) * multiplier);
        ShapeXPermille = Math.Clamp(ShapeXPermille + Math.Max(0, advance), 0, 1000);
        HeatYPermille = Math.Clamp(HeatYPermille - StrikeHeatCostPermille, 0, 1000);

        Struck?.Invoke(onTempo);
        RepaintUi();
    }

    /// <summary>Start holding the bellows — heat rises, shape drifts back slightly, hammering is
    /// disabled until <see cref="BellowsStop"/>.</summary>
    public void BellowsStart()
    {
        if (Completed || WasCancelled)
        {
            return;
        }

        IsPumping = true;
        RepaintUi();
    }

    /// <summary>Release the bellows.</summary>
    public void BellowsStop()
    {
        IsPumping = false;
        RepaintUi();
    }

    /// <summary>Quench finale: plunge the cursor now. Legal only once the shape has reached the
    /// path's end (x &gt;= 1000) — the player is expected to stop pumping/hammering there and let
    /// the natural heat drain carry the cursor down toward the trough before plunging. Captures
    /// the plunge instant as the final trace sample, builds the ONE <see cref="ForgeTraceInput"/>/
    /// <see cref="CraftAction"/> (PKD8), and raises <see cref="Finished"/>.</summary>
    public void Plunge()
    {
        if (Completed || WasCancelled || ShapeXPermille < 1000)
        {
            return;
        }

        RecordSample();
        Quenched?.Invoke();
        Finish();
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

    /// <summary>Real-time input mapping (Space/left-click to strike, Shift/right-click held to
    /// pump) — routes to the SAME public seam methods a scripted test or the button row drives, so
    /// there is exactly one code path for "what a strike/pump does" regardless of input source.</summary>
    public override void _GuiInput(InputEvent @event)
    {
        if (Completed || WasCancelled)
        {
            return;
        }

        switch (@event)
        {
            case InputEventKey { Keycode: Key.Space, Pressed: true, Echo: false }:
                ForgeStrike();
                break;
            case InputEventKey { Keycode: Key.Shift, Pressed: true }:
                BellowsStart();
                break;
            case InputEventKey { Keycode: Key.Shift, Pressed: false }:
                BellowsStop();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }:
                ForgeStrike();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true }:
                BellowsStart();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: false }:
                BellowsStop();
                break;
        }
    }

    /// <summary>G1 result ceremony (unchanged from the old minigame): a presentation-only PREVIEW
    /// of which <see cref="QualityGrade"/> band a folded per-mille grade is heading toward —
    /// mirrors <c>QualityRoller.RollActive</c>'s own band thresholds (200/550/780/930) but
    /// deliberately WITHOUT its ±25 jitter or its material-grade ceiling. Public/static so a test
    /// can pin the band thresholds independently of a live run.</summary>
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

    private void RecordSample()
    {
        if (_samples.Count / 2 >= MaxSamples)
        {
            return;
        }

        _samples.Add(ShapeXPermille);
        _samples.Add(HeatYPermille);
    }

    private void RecordStrike(int tempoErrorPermille)
    {
        if (_strikes.Count / 2 >= MaxSamples)
        {
            return;
        }

        _strikes.Add(ShapeXPermille);
        _strikes.Add(tempoErrorPermille);
    }

    /// <summary>Distance from the nearest tempo-metronome pulse, mapped to [0, 1000] (0 = dead on
    /// beat, 1000 = exactly off-beat at the half-period). A pure function of the accumulated clock
    /// — no engine RNG, no wall-clock — so the same strike timing always grades identically.</summary>
    private int TempoErrorPermilleNow()
    {
        var phase = _elapsed % TempoPeriodSeconds;
        var halfPeriod = TempoPeriodSeconds / 2.0;
        var distance = Math.Min(phase, TempoPeriodSeconds - phase);
        return (int)Math.Round(Math.Clamp(distance / halfPeriod, 0.0, 1.0) * 1000.0);
    }

    private void Finish()
    {
        Completed = true;
        var samples = ImmutableList.CreateRange(_samples);
        var strikes = ImmutableList.CreateRange(_strikes);
        var puzzle = new ForgeTraceInput(samples, strikes, PathSeed);

        // Read-only preview off the SAME pure sim scorer (mirrors AlchemyBrewPuzzle's own
        // preview) — never written back as rules, purely for the ceremony/feedback text below.
        if (_recipe is not null && _profession is not null)
        {
            var preview = ForgeScorer.Score(_recipe, puzzle, _unlockedTalents, _profession);
            PreviewGradePermille = preview.GradePermille;
            PreviewSubScores = preview.SubScores;
        }

        // U23c orchestrator wires ForgeScorer into CraftingHandlers.ApplyCraft so a submitted
        // ForgeTraceInput actually resolves (today the puzzle-validation gate there only
        // recognizes AlchemyReagentPuzzle and rejects anything else) — PerformanceGrade stays
        // null here regardless; the trace is the single source of truth the sim will score.
        var action = new CraftAction(RecipeId, MaterialKey, PerformanceGrade: null, Puzzle: puzzle, SubScores: PreviewSubScores);
        EmittedAction = action;
        RepaintUi();
        Finished?.Invoke(action);
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
        FocusMode = FocusModeEnum.All; // so _GuiInput actually receives keyboard events

        var body = new VBoxContainer { Name = "ForgeMinigameBody" };
        AddChild(body);

        _titleLabel = new Label { Name = "ForgeMinigameTitle" };
        _titleLabel.AddThemeColorOverride("font_color", GameTheme.HeaderColor);
        _titleLabel.ThemeTypeVariation = GameTheme.HeaderThemeType;
        body.AddChild(_titleLabel);

        _canvas = new AnvilMapCanvas { Name = "AnvilMapCanvas", CustomMinimumSize = new Vector2(0, 240) };
        _canvas.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        body.AddChild(_canvas);

        _readoutLabel = new Label { Name = "ForgeMinigameReadout" };
        body.AddChild(_readoutLabel);

        var buttonRow = new HBoxContainer { Name = "ForgeMinigameButtons" };
        body.AddChild(buttonRow);

        _hammerButton = new Button { Name = "HammerStrike", Text = "Hammer (Space)" };
        _hammerButton.Pressed += ForgeStrike;
        buttonRow.AddChild(_hammerButton);

        _bellowsButton = new Button { Name = "Bellows", Text = "Bellows (hold Shift)" };
        _bellowsButton.ButtonDown += BellowsStart;
        _bellowsButton.ButtonUp += BellowsStop;
        buttonRow.AddChild(_bellowsButton);

        _plungeButton = new Button { Name = "Plunge", Text = "Plunge!" };
        _plungeButton.Pressed += Plunge;
        buttonRow.AddChild(_plungeButton);

        _cancelButton = new Button { Name = "ForgeMinigameCancel", Text = "Cancel" };
        _cancelButton.Pressed += Cancel;
        buttonRow.AddChild(_cancelButton);

        _built = true;
        RepaintUi();
    }

    /// <summary>Render-only — reads the current run state, writes no scoring state. Called after
    /// every state-changing call above (never a per-frame poll independent of them).</summary>
    private void RepaintUi()
    {
        if (!_built)
        {
            return;
        }

        _titleLabel.Text = $"Anvil Map: {RecipeId}";

        _canvas.Path = Path;
        _canvas.CursorXPermille = ShapeXPermille;
        _canvas.CursorYPermille = HeatYPermille;
        _canvas.QueueRedraw();

        _readoutLabel.Text = WasCancelled
            ? "Cancelled."
            : Completed
                ? $"Done — grade {PreviewGradePermille}."
                : $"Shape {ShapeXPermille}/1000 — Heat {HeatYPermille} — {(IsPumping ? "pumping" : "idle")}";

        _hammerButton.Disabled = Completed || WasCancelled || IsPumping || ShapeXPermille >= 1000;
        _bellowsButton.Disabled = Completed || WasCancelled;
        _plungeButton.Disabled = Completed || WasCancelled || ShapeXPermille < 1000;
    }

    /// <summary>
    /// The 2D drawing surface: renders the shared target line (<see cref="Path"/>) and the cursor
    /// (<see cref="CursorXPermille"/>/<see cref="CursorYPermille"/>) — a plain <see cref="Control"/>
    /// with <see cref="_Draw"/> primitive shapes only, exactly the idiom <c>ShopStage</c>'s own
    /// <c>ShopEmoteGlyph</c> already proves headless-safe (NEVER a 3D <c>SubViewport</c> — a known
    /// gdUnit headless hang). X = shape progress (left→right), Y = heat (bottom cold → top hot).
    /// </summary>
    private sealed partial class AnvilMapCanvas : Control
    {
        public ImmutableList<int> Path = ImmutableList<int>.Empty;
        public int CursorXPermille;
        public int CursorYPermille;

        public override void _Draw()
        {
            var size = Size;
            if (size.X <= 0 || size.Y <= 0)
            {
                return;
            }

            DrawRect(new Rect2(Vector2.Zero, size), new Color(GameTheme.BoneColor, 0.08f));

            if (Path.Count >= 4 && Path.Count % 2 == 0)
            {
                var vertexCount = Path.Count / 2;
                for (var i = 0; i < vertexCount - 1; i++)
                {
                    var a = ToCanvasPoint(Path[i * 2], Path[i * 2 + 1], size);
                    var b = ToCanvasPoint(Path[(i + 1) * 2], Path[(i + 1) * 2 + 1], size);
                    DrawLine(a, b, new Color(GameTheme.EmberColor, 0.9f), 3f);
                }
            }

            var cursor = ToCanvasPoint(CursorXPermille, CursorYPermille, size);
            DrawCircle(cursor, 7f, GameTheme.CoolantColor);
        }

        private static Vector2 ToCanvasPoint(int xPermille, int yPermille, Vector2 size) => new(
            Math.Clamp(xPermille, 0, 1000) / 1000f * size.X,
            size.Y - Math.Clamp(yPermille, 0, 1000) / 1000f * size.Y);
    }
}
