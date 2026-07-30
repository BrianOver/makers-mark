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

    // ── U3 "forge feel pass" knobs (P002 interactive-professions plan) — still adapter-only: only
    // the resulting integer PumpStroke()/ForgeStrike()/Plunge() seam calls ever cross KTD2. ───────
    /// <summary>Aimed-strike hit box: a left-click only registers as a strike inside this generous
    /// square (px), centred on the billet's actual screen anchor — Space stays unaimed/always valid
    /// (KTD-C keyboard parity). Exact pixel tuning is deferred to the real window per the plan.</summary>
    public const float BilletHitBoxSize = 96f;

    /// <summary>One bellows pump stroke's fixed heat quantum (per-mille) — the discrete counterpart
    /// to holding the bellows, fired once per <see cref="PumpStrokeDragPixels"/> of downward
    /// right-drag (KTD-B: raw motion floats never reach a scorer, only these integer calls do).</summary>
    public const int PumpStrokeHeatPermille = 55;

    /// <summary>Downward right-drag pixels per <see cref="PumpStroke"/> call.</summary>
    public const int PumpStrokeDragPixels = 18;

    /// <summary>Width (as a fraction of the canvas) of the generous right-edge drag-to-quench hit
    /// zone approximating where <c>AnvilMapCanvas.DrawQuenchZone</c> paints the trough.</summary>
    public const float QuenchZoneWidthFraction = 0.16f;

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

    // ── U3 input-gesture state — plumbing only, never crosses KTD2 (every gesture still ends in
    // ForgeStrike()/PumpStroke()/Plunge()) ──────────────────────────────────────────────────────
    private bool _quenchDragArmed;         // true once a left-press has landed on the billet
    private bool _pumpDragArmed;           // true while the right button is held
    private double _pumpDragAccumulatorPixels;

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
        _canvas.OnStruck(onTempo);
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

    /// <summary>One bellows PUMP STROKE — the discrete counterpart to holding the bellows (U3): raises
    /// heat by exactly <see cref="PumpStrokeHeatPermille"/> (clamped at 1000) and applies the SAME
    /// shape-drifts-back rule <see cref="Advance"/> already uses while pumping, scaled to the
    /// equivalent time slice this quantum represents (<c>quantum / BellowsRaisePermillePerSecond</c>
    /// seconds) — so a flurry of strokes behaves like holding the bellows for that long. This is the
    /// seam every stroke gesture (drag-quantized right-click, or a future dedicated key) terminates
    /// in — no wall-clock, no RNG, callable directly by a headless test.</summary>
    public void PumpStroke()
    {
        if (Completed || WasCancelled)
        {
            return;
        }

        var equivalentSeconds = PumpStrokeHeatPermille / (double)BellowsRaisePermillePerSecond;
        HeatYPermille = Math.Clamp(HeatYPermille + PumpStrokeHeatPermille, 0, 1000);
        ShapeXPermille = Math.Max(0, ShapeXPermille - (int)Math.Round(BellowsDriftBackPermillePerSecond * equivalentSeconds));
        RepaintUi();
    }

    /// <summary>Pure hit-test for an aimed strike (U3): true iff <paramref name="localPos"/> — in
    /// THIS overlay's own local coordinate space, exactly what <see cref="InputEventMouseButton.Position"/>
    /// carries when <see cref="_GuiInput"/> receives it — falls within a generous
    /// <see cref="BilletHitBoxSize"/>px square centred on where the billet is actually drawn
    /// (mirrors <c>AnvilMapCanvas.BilletAnchor</c>). Side-effect-free, so a headless test can call it
    /// directly; on a freshly-built, unmounted overlay the anchor resolves to the origin (no layout
    /// has run yet), which still exercises the exact same rect math.</summary>
    public bool WouldHit(Vector2 localPos)
    {
        var anchor = BilletAnchorInPanelSpace();
        var half = BilletHitBoxSize / 2f;
        return Math.Abs(localPos.X - anchor.X) <= half && Math.Abs(localPos.Y - anchor.Y) <= half;
    }

    /// <summary>Pure hit-test for the drag-to-quench gesture's destination (U3): true iff
    /// <paramref name="localPos"/> (same local space as <see cref="WouldHit"/>) falls in a generous
    /// right-edge strip approximating where <c>AnvilMapCanvas.DrawQuenchZone</c> paints the trough —
    /// exact pixel matching isn't needed for a drop target this size (plan's deferred pixel-tuning
    /// note). False before any layout has sized the canvas.</summary>
    public bool IsInQuenchZone(Vector2 localPos)
    {
        var size = _canvas.Size;
        if (size.X <= 0 || size.Y <= 0)
        {
            return false;
        }

        var topLeft = ToPanelSpace(new Vector2(size.X * (1f - QuenchZoneWidthFraction), 0f));
        var bottomRight = ToPanelSpace(new Vector2(size.X, size.Y));
        return localPos.X >= Math.Min(topLeft.X, bottomRight.X) && localPos.X <= Math.Max(topLeft.X, bottomRight.X)
            && localPos.Y >= Math.Min(topLeft.Y, bottomRight.Y) && localPos.Y <= Math.Max(topLeft.Y, bottomRight.Y);
    }

    /// <summary>The billet's current screen anchor, in this overlay's own local space — exposed
    /// read-only purely so a headless test can locate <see cref="WouldHit"/>'s hit box without
    /// duplicating canvas-private layout math.</summary>
    public Vector2 BilletAnchor => BilletAnchorInPanelSpace();

    /// <summary>A point guaranteed to fall inside <see cref="IsInQuenchZone"/>'s hit region, in this
    /// overlay's own local space — same test-support rationale as <see cref="BilletAnchor"/>.</summary>
    public Vector2 QuenchZoneAnchor => ToPanelSpace(new Vector2(
        _canvas.Size.X * (1f - QuenchZoneWidthFraction / 2f), _canvas.Size.Y * 0.6f));

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
        _canvas.OnQuenched();
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

    /// <summary>Real-time input mapping — routes to the SAME public seam methods a scripted test or
    /// the button row drives, so there is exactly one code path for "what a gesture does" regardless
    /// of input source (KTD-A). Space always strikes unaimed (KTD-C accessible path); a left-click
    /// only strikes if it lands on the billet (<see cref="WouldHit"/>) and, held, arms a
    /// drag-to-quench that fires <see cref="Plunge"/> once the drag enters the trough
    /// (<see cref="IsInQuenchZone"/>); Shift keeps working exactly as before (held bellows); a
    /// right-button DRAG quantizes into discrete <see cref="PumpStroke"/> calls every
    /// <see cref="PumpStrokeDragPixels"/> of downward motion (KTD-B — no raw float ever reaches a
    /// scorer).</summary>
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
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mb:
                // Clicking must not cost the player their keyboard — and if a child button holds
                // focus, Space would press THAT instead of striking the billet.
                UiKit.ReclaimKeyboard(this);
                _quenchDragArmed = WouldHit(mb.Position);
                if (_quenchDragArmed)
                {
                    ForgeStrike();
                }

                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }:
                _quenchDragArmed = false;
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true }:
                _pumpDragArmed = true;
                _pumpDragAccumulatorPixels = 0;
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: false }:
                _pumpDragArmed = false;
                _pumpDragAccumulatorPixels = 0;
                break;
            case InputEventMouseMotion mm:
                if (_quenchDragArmed && IsInQuenchZone(mm.Position))
                {
                    Plunge();
                    _quenchDragArmed = false;
                }

                if (_pumpDragArmed)
                {
                    AccumulatePumpDrag(mm.Relative.Y);
                }

                break;
        }
    }

    /// <summary>Quantizes the right-button drag into discrete <see cref="PumpStroke"/> calls: every
    /// <see cref="PumpStrokeDragPixels"/> of DOWNWARD motion (pulling the bellows handle down) fires
    /// one stroke; upward jitter is ignored rather than subtracted, so an unsteady hand never loses
    /// banked progress. No raw float ever reaches a scorer — only the resulting PumpStroke calls do.</summary>
    private void AccumulatePumpDrag(float relativeY)
    {
        if (relativeY <= 0)
        {
            return;
        }

        _pumpDragAccumulatorPixels += relativeY;
        while (_pumpDragAccumulatorPixels >= PumpStrokeDragPixels)
        {
            PumpStroke();
            _pumpDragAccumulatorPixels -= PumpStrokeDragPixels;
        }
    }

    /// <summary>Mirrors <c>AnvilMapCanvas.BilletAnchor</c> exactly, but as a free function with zero
    /// coupling to the canvas's private draw-time internals (nothing here reaches into
    /// <see cref="AnvilMapCanvas"/> other than its public <see cref="Control.Size"/>/transform).</summary>
    private static Vector2 BilletAnchorFor(Vector2 canvasSize) => new(canvasSize.X * 0.45f, canvasSize.Y * 0.79f);

    private Vector2 BilletAnchorInPanelSpace() => ToPanelSpace(BilletAnchorFor(_canvas.Size));

    /// <summary>Translates a point from the Anvil Map canvas's own local coordinate space into this
    /// overlay's local space (composing global transforms) — so hit-tests can compare directly
    /// against the raw local position a mouse event already carries when it reaches
    /// <see cref="_GuiInput"/>, with zero conversion needed at the call site.</summary>
    private Vector2 ToPanelSpace(Vector2 canvasLocalPos)
    {
        var globalPos = _canvas.GetGlobalTransform() * canvasLocalPos;
        return GetGlobalTransform().AffineInverse() * globalPos;
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

        // Sets FocusMode AND actually takes focus. This line used to be a bare
        // `FocusMode = FocusModeEnum.All;` commented "so _GuiInput actually receives keyboard
        // events" — which it does not, on its own: being focus-ABLE is not being focused, so Space
        // and Shift never arrived and the craft was unwinnable. See UiKit.ClaimKeyboard.
        UiKit.ClaimKeyboard(this);

        var body = new VBoxContainer { Name = "ForgeMinigameBody" };
        AddChild(body);

        _titleLabel = new Label { Name = "ForgeMinigameTitle" };
        _titleLabel.AddThemeColorOverride("font_color", GameTheme.HeaderColor);
        _titleLabel.ThemeTypeVariation = GameTheme.HeaderThemeType;
        body.AddChild(_titleLabel);

        // Fill the drawer: a short strip left most of the panel empty, which made the forge read as
        // a widget rather than a room.
        _canvas = new AnvilMapCanvas { Name = "AnvilMapCanvas", CustomMinimumSize = new Vector2(0, 340) };
        _canvas.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _canvas.SizeFlagsVertical = SizeFlags.ExpandFill;
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

    // Last-rendered label state — so the per-frame RepaintUi (called from _Process→Advance every
    // frame) only rebuilds the readout/title strings when something actually changed, instead of
    // allocating four interpolated strings every single frame on the hot path.
    private int _lastShapeX = int.MinValue;
    private int _lastHeatY = int.MinValue;
    private bool _lastPumping;
    private bool _lastCompleted;
    private bool _lastCancelled;

    /// <summary>Render-only — reads the current run state, writes no scoring state. Called after
    /// every state-changing call above AND every frame via <see cref="Advance"/> (for the canvas's
    /// live tempo/heat animation); the label/button rebuild is gated on an actual state change so
    /// the per-frame path allocates nothing.</summary>
    private void RepaintUi()
    {
        if (!_built)
        {
            return;
        }

        // Canvas: fed + redrawn every call — the draw is cheap 2D primitives and the tempo ring +
        // heat glow need a continuous redraw to animate.
        _canvas.Path = Path;
        _canvas.CursorXPermille = ShapeXPermille;
        _canvas.CursorYPermille = HeatYPermille;
        _canvas.TempoErrorPermille = TempoErrorPermilleNow();
        _canvas.TempoOnBeatWindowPermille = TempoOnBeatWindowPermille;
        _canvas.TempoPhase = (float)(_elapsed % TempoPeriodSeconds / TempoPeriodSeconds);
        _canvas.IsPumping = IsPumping;
        _canvas.Completed = Completed;
        _canvas.QueueRedraw();

        if (ShapeXPermille == _lastShapeX && HeatYPermille == _lastHeatY && IsPumping == _lastPumping
            && Completed == _lastCompleted && WasCancelled == _lastCancelled)
        {
            return; // nothing the text/buttons show has changed — skip the string work
        }

        _lastShapeX = ShapeXPermille;
        _lastHeatY = HeatYPermille;
        _lastPumping = IsPumping;
        _lastCompleted = Completed;
        _lastCancelled = WasCancelled;

        _titleLabel.Text = $"Anvil Map: {RecipeId}";

        _readoutLabel.Text = WasCancelled
            ? "Cancelled."
            : Completed
                ? $"Done — grade {PreviewGradePermille}."
                : ShapeXPermille >= 1000
                    ? $"Shaped! Let it cool, then Plunge — Heat {HeatYPermille}"
                    : $"Shape {ShapeXPermille}/1000 — Heat {HeatYPermille} — {(IsPumping ? "pumping" : "idle")}";

        _hammerButton.Disabled = Completed || WasCancelled || IsPumping || ShapeXPermille >= 1000;
        _bellowsButton.Disabled = Completed || WasCancelled;
        _plungeButton.Disabled = Completed || WasCancelled || ShapeXPermille < 1000;
    }

    /// <summary>
    /// The forge cross-section: a side-view of the smith's fire (coal bed hot at the top, anvil +
    /// quench trough cold at the bottom) where the billet — a real heat-glowing, morphing sprite
    /// held in tongs — is steered along the shared target line (<see cref="Path"/>). A metronome
    /// hammer winds up and falls exactly on the tempo beat so the player can time on-tempo strikes;
    /// strikes throw a spark burst + a small shake, the bellows brighten the coals, and the plunge
    /// billows steam. Plain <see cref="_Draw"/> primitives + a handful of nearest-filtered sprites
    /// (each null-checked with a primitive fallback so headless CI still renders) — never a 3D
    /// <c>SubViewport</c> (a known gdUnit headless hang). All motion is accumulated-frame-delta only
    /// (no wall-clock, no RNG — spark patterns come from a fixed table). X = shape progress
    /// (left→right), Y = heat (bottom cold → top hot).
    /// </summary>
    private sealed partial class AnvilMapCanvas : Control
    {
        public ImmutableList<int> Path = ImmutableList<int>.Empty;
        public int CursorXPermille;
        public int CursorYPermille;
        public int TempoErrorPermille = 1000;         // 0 = dead on-beat, 1000 = fully off-beat
        public int TempoOnBeatWindowPermille = 180;
        public float TempoPhase;                       // 0..1 through the beat period (fed each frame)
        public bool IsPumping;
        public bool Completed;

        private static readonly Color BgTop = new(0.23f, 0.16f, 0.34f);
        private static readonly Color BgBottom = new(0.14f, 0.11f, 0.20f);
        private static readonly Color CoalDark = new(0.16f, 0.09f, 0.10f);
        private static readonly Color CoalEmber = new(1.0f, 0.48f, 0.16f);
        private static readonly Color AnvilSteel = new(0.20f, 0.21f, 0.27f);
        private static readonly Color AnvilFace = new(0.30f, 0.31f, 0.38f);
        private static readonly Color WoodDark = new(0.34f, 0.22f, 0.13f);
        private static readonly Color WaterTeal = new(0.25f, 0.45f, 0.52f);
        private static readonly Color TargetAhead = new(1.0f, 0.80f, 0.44f);
        private static readonly Color TargetGlow = new(1.0f, 0.55f, 0.20f);
        private static readonly Color TargetBehind = new(0.40f, 0.37f, 0.44f);
        private static readonly Color GhostMark = new(1.0f, 0.90f, 0.70f);
        private static readonly Color DeviationGood = new(0.45f, 0.90f, 0.50f);
        private static readonly Color DeviationWarn = new(0.95f, 0.75f, 0.30f);
        private static readonly Color DeviationBad = new(0.95f, 0.35f, 0.30f);

        // Deterministic spark directions (deg, speed) — -90 is straight up. No RNG.
        private static readonly (float Ang, float Spd)[] SparkDirs =
        {
            (-95, 190), (-80, 150), (-110, 165), (-70, 205), (-100, 140), (-85, 225), (-120, 135), (-62, 175),
            (-90, 195), (-75, 160), (-105, 178), (-65, 152), (-115, 148), (-95, 212), (-82, 188), (-100, 168),
        };

        // Fixed ember-lump positions across the coal bed (fraction of width, brightness phase).
        private static readonly (float Fx, float Phase)[] Coals =
        {
            (0.05f, 0.1f), (0.14f, 0.7f), (0.23f, 0.3f), (0.33f, 0.9f), (0.44f, 0.5f), (0.55f, 0.15f),
            (0.66f, 0.8f), (0.76f, 0.4f), (0.86f, 0.6f), (0.95f, 0.25f),
        };

        private struct Particle
        {
            public Vector2 Pos;
            public Vector2 Vel;
            public float Life;
            public float MaxLife;
            public Color From;
            public Color To;
        }

        /// <summary>U3 hit-stop: seconds still owed to freezing <see cref="_anim"/> after an on-tempo
        /// strike (~<see cref="HitStopSeconds"/>) — accumulated and drained via the SAME
        /// <see cref="_Process"/> accumulated-clock pattern as everything else here; never a sleep,
        /// never a wall-clock read.</summary>
        public const float HitStopSeconds = 0.04f;

        private readonly List<Particle> _sparks = new();
        private readonly List<Particle> _steam = new();
        private float _anim;
        private float _hitStopRemaining;
        private float _shake;
        private float _ring = -1f;   // on-tempo strike ring: -1 idle, else elapsed 0..0.15
        private Texture2D?[] _billet = new Texture2D?[4];
        private Texture2D? _hammer;
        private Texture2D? _backdrop;
        private bool _texTried;

        public override void _Process(double delta)
        {
            var dt = (float)delta;

            // Hit-stop (U3): an on-tempo strike owes ~40ms of frozen animation clock — skip
            // advancing _anim while that's still owed (particles/shake/ring keep moving; only the
            // ambient coal/hammer-swing clock pauses, which is what actually reads as "impact").
            if (_hitStopRemaining > 0f)
            {
                _hitStopRemaining = Math.Max(0f, _hitStopRemaining - dt);
            }
            else
            {
                _anim += dt;
            }

            StepParticles(_sparks, dt, gravity: 320f);
            StepParticles(_steam, dt, gravity: -30f);
            if (_shake > 0f) _shake = Math.Max(0f, _shake - 14f * dt);
            if (_ring >= 0f) { _ring += dt; if (_ring > 0.15f) _ring = -1f; }
            QueueRedraw();
        }

        /// <summary>Where the billet sits: ON the anvil, centre-stage and FIXED — the player is
        /// hammering a bar of steel at a fixed spot, not steering a cursor around a plot. Heat and
        /// shape are read from the furnace gauge and the shape meter instead of from X/Y position.</summary>
        private Vector2 BilletAnchor(Vector2 size) => new(size.X * 0.45f, size.Y * 0.79f);

        /// <summary>Strike FX: a spark burst from the billet + a small shake; on-tempo throws twice
        /// the sparks and a white ring. Driven by the sim's own Struck event (no new state).</summary>
        public void OnStruck(bool onTempo)
        {
            var origin = BilletAnchor(Size);
            var n = onTempo ? SparkDirs.Length : SparkDirs.Length / 2;
            for (var i = 0; i < n; i++)
            {
                var (ang, spd) = SparkDirs[i];
                var r = Mathf.DegToRad(ang);
                _sparks.Add(new Particle
                {
                    Pos = origin,
                    Vel = new Vector2(Mathf.Cos(r), Mathf.Sin(r)) * spd,
                    Life = 0.4f, MaxLife = 0.4f,
                    From = new Color(1f, 0.92f, 0.6f), To = new Color(0.85f, 0.25f, 0.12f),
                });
            }

            _shake = onTempo ? 3.2f : 1.6f;
            if (onTempo)
            {
                _ring = 0f;
                _hitStopRemaining = HitStopSeconds; // U3: freeze the ambient clock briefly on impact
            }
        }

        /// <summary>Quench FX: a plume of steam from the billet (driven by the sim's Quenched event).</summary>
        public void OnQuenched()
        {
            var origin = BilletAnchor(Size);
            for (var i = 0; i < 10; i++)
            {
                var (ang, spd) = SparkDirs[i];
                _steam.Add(new Particle
                {
                    Pos = origin + new Vector2((i - 5) * 2f, 0),
                    Vel = new Vector2(Mathf.Cos(Mathf.DegToRad(ang)) * 12f, -40f - spd * 0.1f),
                    Life = 1.2f, MaxLife = 1.2f,
                    From = new Color(0.95f, 0.95f, 1f, 0.8f), To = new Color(0.8f, 0.85f, 0.95f, 0f),
                });
            }
        }

        private static void StepParticles(List<Particle> list, float dt, float gravity)
        {
            for (var i = list.Count - 1; i >= 0; i--)
            {
                var p = list[i];
                p.Vel = new Vector2(p.Vel.X, p.Vel.Y + gravity * dt);
                p.Pos += p.Vel * dt;
                p.Life -= dt;
                if (p.Life <= 0f) { list.RemoveAt(i); continue; }
                list[i] = p;
            }
        }

        public override void _Draw()
        {
            var size = Size;
            if (size.X <= 0 || size.Y <= 0)
            {
                return;
            }

            EnsureTextures();
            var shake = _shake > 0f ? new Vector2(ShakeTable[(int)(_anim * 60f) % 4].X, ShakeTable[(int)(_anim * 60f) % 4].Y) * _shake : Vector2.Zero;
            DrawSetTransform(shake, 0f, Vector2.One);

            // With the painted forge interior in place, the procedural furnace/anvil/trough would
            // fight it — the art already IS the room. They stay only as the no-art fallback, and the
            // heat band survives as a low-alpha glow that still says "up here is hot".
            var art = _backdrop is not null;
            if (art)
            {
                DrawTextureRect(_backdrop!, new Rect2(Vector2.Zero, size), false);
                DrawHeatHaze(size);
            }
            else
            {
                DrawBackground(size);
                DrawCoalBed(size);
            }

            var hasPath = Path.Count >= 4 && Path.Count % 2 == 0;

            // The workbench read: a heat gauge at the furnace (with the forging guide's sweet spot
            // marked on it), the billet resting on the anvil, and a shape meter — instead of a
            // polyline with a dot travelling along it.
            if (hasPath)
            {
                DrawHeatGauge(size);
                DrawShapeMeter(size);
                DrawQuenchZone(size, art);
            }

            if (!art)
            {
                DrawAnvil(size);
            }

            // The billet must REST on something — a compact anvil is drawn under it even over the
            // painted room (whose own props sit elsewhere), so the steel never floats in mid-air.
            var billet = BilletAnchor(size);
            DrawAnvilStand(billet);
            DrawBillet(billet);
            DrawHammer(billet, shake);
            DrawParticles();
            DrawBeatFlash(billet);

            DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        }

        private static readonly Vector2[] ShakeTable = { new(1, -1), new(-1, 1), new(-1, -1), new(1, 1) };

        private void DrawBackground(Vector2 size)
        {
            const int bands = 8;
            var h = size.Y / bands;
            for (var i = 0; i < bands; i++)
            {
                DrawRect(new Rect2(0, i * h, size.X, h + 1f), BgTop.Lerp(BgBottom, (i + 0.5f) / bands));
            }
        }

        private void DrawCoalBed(Vector2 size)
        {
            var bedH = size.Y * 0.20f;
            DrawRect(new Rect2(0, 0, size.X, bedH), CoalDark);
            var flare = IsPumping ? 1.35f : 1f;
            foreach (var (fx, phase) in Coals)
            {
                var pulse = 0.55f + 0.45f * (0.5f + 0.5f * Mathf.Sin((_anim + phase * 3f) * 2.2f));
                var c = new Color(CoalEmber.R, CoalEmber.G, CoalEmber.B, Math.Clamp(pulse * flare * 0.9f, 0f, 1f));
                var cx = fx * size.X;
                DrawRect(new Rect2(cx - 7, bedH - 9, 14, 8), c);
                DrawRect(new Rect2(cx - 4, bedH - 13, 8, 5), new Color(c.R, c.G * 1.1f, c.B, c.A * 0.8f));
            }

            // Flame licks along the bottom edge of the coal bed.
            for (var i = 0; i < Coals.Length; i += 2)
            {
                var cx = Coals[i].Fx * size.X;
                var flick = 6f + 5f * Mathf.Sin((_anim * 3f) + Coals[i].Phase * 6f);
                DrawColoredPolygon(
                    new[] { new Vector2(cx - 5, bedH), new Vector2(cx, bedH + flick), new Vector2(cx + 5, bedH) },
                    new Color(1f, 0.55f, 0.18f, 0.5f * flare));
            }
        }

        private const int SweetSpotHalfWidthPermille = 90;   // matches DrawDeviation's "good" band

        /// <summary>
        /// The furnace heat gauge: a vertical thermometer showing the billet's CURRENT heat, with the
        /// forging guide's ideal heat for this point in the shaping marked on it as a bright sweet-spot
        /// band (plus a small arrow showing which way the ideal is about to move, so the player can
        /// anticipate instead of chasing). This is what replaces the old target polyline — the same
        /// numbers the sim scores, read as an instrument on the wall rather than a plot to trace.
        /// </summary>
        private void DrawHeatGauge(Vector2 size)
        {
            var x = size.X * 0.085f;
            var top = size.Y * 0.16f;
            var bottom = size.Y * 0.86f;
            var w = 22f;
            var h = bottom - top;

            float YFor(int permille) => bottom - Math.Clamp(permille, 0, 1000) / 1000f * h;

            // Housing.
            DrawRect(new Rect2(x - 4, top - 5, w + 8, h + 10), new Color(0.07f, 0.06f, 0.10f, 0.82f));
            DrawRect(new Rect2(x - 4, top - 5, w + 8, h + 10), new Color(0.45f, 0.40f, 0.34f, 0.9f), filled: false, width: 2f);

            // Column: cold at the bottom, forge-hot at the top.
            const int cells = 16;
            for (var i = 0; i < cells; i++)
            {
                var f = 1f - (i + 0.5f) / cells;
                var cy = top + h * i / cells;
                DrawRect(new Rect2(x, cy, w, h / cells + 1f), new Color(HeatColor(f), 0.30f));
            }

            // Sweet spot: the ideal heat for the CURRENT shaping progress, from the same guide the
            // scorer grades against.
            var target = InterpTargetHeat(CursorXPermille);
            var bandTop = YFor(target + SweetSpotHalfWidthPermille);
            var bandBottom = YFor(target - SweetSpotHalfWidthPermille);
            var dev = Math.Abs(CursorYPermille - target);
            var band = dev < 90 ? DeviationGood : dev < 220 ? DeviationWarn : DeviationBad;
            DrawRect(new Rect2(x - 2, bandTop, w + 4, bandBottom - bandTop), new Color(band, 0.22f));
            DrawRect(new Rect2(x - 2, bandTop, w + 4, bandBottom - bandTop), new Color(band, 0.95f), filled: false, width: 2f);

            // Where the ideal is heading next (anticipation cue).
            var ahead = InterpTargetHeat(Math.Min(1000, CursorXPermille + 90));
            if (Math.Abs(ahead - target) > 25)
            {
                var up = ahead > target;
                var ay = (bandTop + bandBottom) / 2f + (up ? -14f : 14f);
                var tip = up ? ay - 7f : ay + 7f;
                DrawColoredPolygon(
                    new[] { new Vector2(x + w + 8, ay), new Vector2(x + w + 16, ay), new Vector2(x + w + 12, tip) },
                    new Color(TargetAhead, 0.9f));
            }

            // The mercury: current heat, glowing in its own heat colour.
            var my = YFor(CursorYPermille);
            var heatFrac = Math.Clamp(CursorYPermille / 1000f, 0f, 1f);
            DrawRect(new Rect2(x, my, w, bottom - my), new Color(HeatColor(heatFrac), 0.92f));
            DrawRect(new Rect2(x - 5, my - 2f, w + 10, 4f), new Color(1f, 0.97f, 0.88f, 0.95f)); // needle
        }

        /// <summary>A compact anvil directly beneath the billet: horn, face, waist and base, with a
        /// contact shadow — the work surface the hammer drives against.</summary>
        private void DrawAnvilStand(Vector2 billet)
        {
            var faceY = billet.Y + 11f;
            var cx = billet.X;

            DrawEllipseSoft(new Vector2(cx, faceY + 30f), 46f, 7f, new Color(0f, 0f, 0f, 0.35f)); // ground shadow
            DrawColoredPolygon(
                new[]
                {
                    new Vector2(cx - 44, faceY), new Vector2(cx + 40, faceY - 1),
                    new Vector2(cx + 54, faceY + 4), new Vector2(cx + 40, faceY + 8),   // horn
                    new Vector2(cx + 20, faceY + 9), new Vector2(cx + 13, faceY + 24),
                    new Vector2(cx + 26, faceY + 30), new Vector2(cx - 26, faceY + 30), // base
                    new Vector2(cx - 13, faceY + 24), new Vector2(cx - 20, faceY + 9),
                    new Vector2(cx - 40, faceY + 8),
                },
                AnvilSteel);
            DrawRect(new Rect2(cx - 44, faceY - 2f, 84f, 3f), AnvilFace); // struck face highlight
        }

        private void DrawEllipseSoft(Vector2 c, float rx, float ry, Color col)
        {
            const int seg = 20;
            var pts = new Vector2[seg];
            for (var i = 0; i < seg; i++)
            {
                var a = Mathf.Tau * i / seg;
                pts[i] = new Vector2(c.X + Mathf.Cos(a) * rx, c.Y + Mathf.Sin(a) * ry);
            }

            DrawColoredPolygon(pts, col);
        }

        /// <summary>The shape meter: how far the bar has been drawn out toward the finished piece —
        /// segmented, so each on-tempo strike visibly claims another notch. Replaces "X position".</summary>
        private void DrawShapeMeter(Vector2 size)
        {
            const int cells = 12;
            var w = size.X * 0.30f;
            var x = size.X * 0.62f;
            var y = size.Y * 0.17f;
            var cw = w / cells;

            DrawRect(new Rect2(x - 3, y - 4, w + 6, 20f), new Color(0.07f, 0.06f, 0.10f, 0.82f));
            var filled = Mathf.CeilToInt(Math.Clamp(CursorXPermille / 1000f, 0f, 1f) * cells);
            for (var i = 0; i < cells; i++)
            {
                var r = new Rect2(x + i * cw + 1f, y, cw - 2f, 12f);
                if (i < filled)
                {
                    var done = CursorXPermille >= 1000;
                    var c = done
                        ? new Color(0.55f, 0.9f, 1f).Lerp(Colors.White, 0.5f + 0.5f * Mathf.Sin(_anim * 5f))
                        : TargetAhead;
                    DrawRect(r, c);
                }
                else
                {
                    DrawRect(r, new Color(0.35f, 0.32f, 0.38f, 0.55f), filled: false, width: 1f);
                }
            }
        }

        private void DrawAnvil(Vector2 size)
        {
            var faceY = size.Y - 4f; // y=0 heat line sits at the anvil face
            DrawRect(new Rect2(0, faceY, size.X, size.Y - faceY + 2f), AnvilFace);
            // A stout anvil body centered low.
            var cx = size.X * 0.5f;
            DrawColoredPolygon(
                new[]
                {
                    new Vector2(cx - 70, faceY), new Vector2(cx + 70, faceY),
                    new Vector2(cx + 40, faceY + 10), new Vector2(cx + 22, faceY + 10),
                    new Vector2(cx + 16, size.Y), new Vector2(cx - 16, size.Y),
                    new Vector2(cx - 22, faceY + 10), new Vector2(cx - 40, faceY + 10),
                },
                AnvilSteel);
        }

        /// <summary>A soft warm haze over the painted furnace band so "high = hot" still reads on top
        /// of the art, without stamping opaque orange blocks over it.</summary>
        private void DrawHeatHaze(Vector2 size)
        {
            var bedH = size.Y * 0.22f;
            var flare = IsPumping ? 1.9f : 1f;
            const int bands = 6;
            for (var i = 0; i < bands; i++)
            {
                var t = i / (float)bands;
                var a = (0.16f - 0.16f * t) * flare * (0.85f + 0.15f * Mathf.Sin(_anim * 2.4f));
                DrawRect(new Rect2(0, bedH * t, size.X, bedH / bands + 1f), new Color(1f, 0.52f, 0.18f, Math.Clamp(a, 0f, 0.5f)));
            }
        }

        /// <summary>The quench point. Over art it's a translucent shimmer pool + a glow (the painted
        /// room already has vessels, so a hard wooden box would read as pasted on); without art it
        /// falls back to the drawn trough.</summary>
        private void DrawQuenchZone(Vector2 size, bool art)
        {
            var endY = Path[^1];
            var waterTop = ToCanvasPoint(1000, endY, size).Y;
            var x0 = size.X - 44f;
            var ready = CursorXPermille >= 1000;
            var water = ready
                ? WaterTeal.Lerp(new Color(0.55f, 0.9f, 1f), 0.5f + 0.5f * Mathf.Sin(_anim * 4f))
                : WaterTeal;

            if (!art)
            {
                DrawRect(new Rect2(x0, waterTop - 3, 42, size.Y - waterTop + 3), WoodDark);
            }

            DrawRect(new Rect2(x0 + 2, waterTop, 38, size.Y - waterTop - 2), new Color(water, art ? 0.5f : 0.9f));
            if (ready)
            {
                DrawRect(new Rect2(x0, waterTop - 2, 42, 3f), new Color(0.7f, 0.95f, 1f, 0.75f));
            }

            for (var s = 0; s < 2; s++)
            {
                var sy = waterTop + 4 + s * 6 + 1.5f * Mathf.Sin(_anim * 3f + s);
                DrawLine(new Vector2(x0 + 3, sy), new Vector2(x0 + 39, sy), new Color(1f, 1f, 1f, 0.3f), 1f);
            }
        }

        private void DrawBillet(Vector2 cursor)
        {
            var heatFrac = Math.Clamp(CursorYPermille / 1000f, 0f, 1f);
            var body = HeatColor(heatFrac);
            var glowR = 14f + 10f * heatFrac;
            DrawCircle(cursor, glowR * 1.5f, new Color(body, 0.16f)); // wide bloom, reads over art
            DrawCircle(cursor, glowR, new Color(body, 0.34f));        // heat halo

            var frame = CursorXPermille >= 900 ? 3 : CursorXPermille >= 650 ? 2 : CursorXPermille >= 300 ? 1 : 0;
            var tex = _billet[frame];
            if (tex is not null)
            {
                const float sc = 2.6f;
                var w = tex.GetWidth() * sc;
                var h = tex.GetHeight() * sc;
                DrawTextureRect(tex, new Rect2(cursor - new Vector2(w / 2f, h / 2f), new Vector2(w, h)), false, body);
            }
            else
            {
                DrawCircle(cursor, 6f, body); // headless / missing-art fallback
            }

            if (heatFrac < 0.25f)
            {
                DrawArc(cursor, glowR - 2f, 0f, Mathf.Tau, 20, new Color(0.42f, 0.48f, 0.60f, 0.9f), 2f); // "gone cold" ring
            }
        }

        private void DrawHammer(Vector2 cursor, Vector2 shake)
        {
            // The hammer is the metronome: it winds up through the beat and falls onto the billet
            // exactly on-beat (TempoPhase→1/0). Head cocked back at mid-phase, down at the beat.
            float swing = TempoPhase < 0.82f
                ? Mathf.Lerp(6f, -66f, TempoPhase / 0.82f)          // winding up
                : Mathf.Lerp(-66f, 6f, (TempoPhase - 0.82f) / 0.18f); // falling to the beat
            var pivot = cursor + new Vector2(14f, -34f);            // the smith's hand, up-right of the billet
            var inWindow = TempoErrorPermille <= TempoOnBeatWindowPermille;
            var tint = inWindow ? new Color(1.0f, 0.9f, 0.55f) : Colors.White;

            if (_hammer is not null)
            {
                const float sc = 1.6f;
                var w = _hammer.GetWidth() * sc;
                var h = _hammer.GetHeight() * sc;
                DrawSetTransform(pivot + shake, Mathf.DegToRad(swing), Vector2.One);
                // Pivot at the handle bottom-centre of the source sprite (14,27 of 28×28).
                DrawTextureRect(_hammer, new Rect2(new Vector2(-14f * sc, -27f * sc), new Vector2(w, h)), false, tint);
                DrawSetTransform(shake, 0f, Vector2.One);
            }
            else
            {
                DrawLine(pivot, pivot + new Vector2(0, 22f).Rotated(Mathf.DegToRad(swing)), tint, 3f); // fallback
            }
        }

        private void DrawParticles()
        {
            foreach (var p in _sparks)
            {
                var t = 1f - p.Life / p.MaxLife;
                DrawRect(new Rect2(p.Pos - new Vector2(1.5f, 1.5f), new Vector2(3, 3)), p.From.Lerp(p.To, t));
            }

            foreach (var p in _steam)
            {
                var t = 1f - p.Life / p.MaxLife;
                var c = p.From.Lerp(p.To, t);
                DrawCircle(p.Pos, 3f + 5f * t, c);
            }
        }

        private void DrawBeatFlash(Vector2 cursor)
        {
            if (_ring >= 0f)
            {
                var t = _ring / 0.15f;
                DrawArc(cursor, 6f + 22f * t, 0f, Mathf.Tau, 28, new Color(1f, 0.96f, 0.7f, 1f - t), 2.5f);
            }

            // Continuous secondary on-beat cue (kept for readability even without a strike).
            var onBeat = 1f - Math.Clamp(TempoErrorPermille / 1000f, 0f, 1f);
            if (onBeat > 0.5f && _ring < 0f)
            {
                var inWindow = TempoErrorPermille <= TempoOnBeatWindowPermille;
                DrawArc(cursor, 16f - 5f * onBeat, 0f, Mathf.Tau, 24,
                    new Color(1f, 0.9f, 0.5f, 0.15f + 0.4f * onBeat), inWindow ? 2f : 1f);
            }
        }

        private static Color HeatColor(float f) =>
            f < 0.5f
                ? new Color(0.29f, 0.23f, 0.27f).Lerp(new Color(0.85f, 0.31f, 0.16f), f / 0.5f)  // cold steel → red
                : new Color(0.85f, 0.31f, 0.16f).Lerp(new Color(1.0f, 0.97f, 0.86f), (f - 0.5f) / 0.5f); // red → white-hot

        private void EnsureTextures()
        {
            if (_texTried)
            {
                return;
            }

            _texTried = true;
            TextureFilter = TextureFilterEnum.Nearest;
            for (var i = 0; i < 4; i++)
            {
                _billet[i] = LoadTex($"res://assets/minigames/billet_{i}.png");
            }

            _hammer = LoadTex("res://assets/minigames/hammer.png");
            _backdrop = LoadTex("res://assets/minigames/forge_backdrop.png");
        }

        private static Texture2D? LoadTex(string path) =>
            ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;

        private int InterpTargetHeat(int xPermille)
        {
            var vertexCount = Path.Count / 2;
            var x = Math.Clamp(xPermille, 0, 1000);
            for (var i = 0; i < vertexCount - 1; i++)
            {
                var x0 = Path[i * 2];
                var x1 = Path[(i + 1) * 2];
                if (x >= x0 && x <= x1 && x1 > x0)
                {
                    var t = (float)(x - x0) / (x1 - x0);
                    return (int)Mathf.Lerp(Path[i * 2 + 1], Path[(i + 1) * 2 + 1], t);
                }
            }

            return Path[^1];
        }

        private static Vector2 ToCanvasPoint(int xPermille, int yPermille, Vector2 size) => new(
            Math.Clamp(xPermille, 0, 1000) / 1000f * size.X,
            size.Y - Math.Clamp(yPermille, 0, 1000) / 1000f * size.Y);
    }
}
