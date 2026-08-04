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
/// U7 (2026-08-04-001 "verify by playing" plan): ACT 1 of the two-act forge — "anvil + bellows
/// work together" (the owner's own description), the paired heat/shape act. Renders the shared
/// target line (<see cref="GameSim.Crafting.ForgePath.Generate"/>/<see cref="GameSim.Crafting.ForgePath.HeatAt"/>
/// — the SAME sim-owned polyline the scorer grades against) on a plain 2D canvas (never a 3D
/// <c>SubViewport</c> — a known gdUnit headless hang) and drives a cursor (the billet: X = shape
/// progress, Y = current heat) the player steers with a hammer strike (advance X, cost heat,
/// bonus near the tempo window) and the bellows (hold: raise heat, shape drifts back slightly —
/// mutually exclusive with hammering). Runs on the SAME accumulated-clock <see cref="Advance"/>
/// pattern the rest of this codebase's minigames prove — no wall-clock, no engine RNG anywhere
/// in the path that shapes the emitted trace.
///
/// <para><b>This is HALF a craft, not the whole one (the U7 split).</b> Earlier revisions of this
/// class owned the entire 0..1000 meter AND the finale plunge — which is exactly why two prior
/// pacing fixes ("Dude the forge mini game is identical - still takes too long") failed: they
/// moved the length knob on a single long meter instead of splitting it. This class now stops at
/// <see cref="ShapingFinishPermille"/> (the sim's own forge-zone boundary, <c>ForgePath.ForgeZoneEnd</c>)
/// and hands off to <see cref="QuenchMinigame"/> — "then you squelch the item" — via
/// <see cref="ShapingDone"/>. Splitting at that exact x is not arbitrary: it is the boundary the
/// SIM scorer already uses to bucket smelt/forge samples from quench samples
/// (<c>ForgeScorer</c>'s three zones), so Act 1's trace is a legitimate, independently-scorable
/// prefix of the full craft rather than an ad-hoc cut.</para>
///
/// <para><b>The skill curve (R6): required strikes fall as demonstrated accuracy rises.</b>
/// <see cref="RequiredStrikes"/> is computed in <see cref="Configure"/> from the caller's own
/// <c>demonstratedAccuracyPermille</c> (the player's session-scoped track record, owned by
/// <c>ForgePanel</c> — never persisted to the sim save) and directly sets how far EACH strike
/// advances the shape (<see cref="ShapingFinishPermille"/> divided across
/// <see cref="RequiredStrikes"/> strikes) — so a proven player covers the same distance in fewer,
/// bigger strikes.</para>
///
/// <para><b><see cref="BaseRequiredStrikes"/>/<see cref="MinRequiredStrikes"/> = 21/18 — measured
/// against the CI-gating invariant, not guessed.</b> A standalone harness referencing the REAL
/// <c>ForgePath</c>/<c>ForgeScorer</c> directly (no Godot) swept the required-strike floor against
/// <c>ForgeWinnabilityTests</c>' own <c>TempoTight</c>/<c>TempoLoose</c> pair — the exact invariant
/// that went red at 50 strikes before U6. Below ~15 required strikes the tempo-tight mean grade
/// started LOSING to tempo-loose on the 5 CI seeds (e.g. at 9 strikes it flips outright) — not U6's
/// old bug (strike-count-coupled scoring), but ordinary sampling noise: too few strikes makes ANY
/// per-strike average unreliable, however it is computed. 18 sits in the empirically robust zone
/// (measured: tempo-tight 313.2 vs tempo-loose 298.2, a healthy +15 margin) while still being 14%
/// fewer strikes than a first-craft player needs. A rapid-fire (no artificial one-swing-per-beat
/// throttle) scripted run at 18 required strikes finishes Act 1 in ~8.6s (19 strikes); chained into
/// <see cref="QuenchMinigame"/>'s own decisive plunge that is ~9.7s combined — under the plan's ~10s
/// bar. A first-craft player (<see cref="BaseRequiredStrikes"/> = 21, unchanged from the value
/// already CI-proven safe) still finishes, just slower (~15.5s Act 1 in the same driver shape).</para>
///
/// <para>This is the SAME mechanism that answers "high metals are more precise" — <see
/// cref="QuenchMinigame"/> narrows ITS OWN band by tier — so the two owner asks come from one
/// skill-curve SYSTEM, not two unrelated knobs.</para>
///
/// <para><b>Adapter-only (KTD2):</b> this class only captures the presentation-layer trace — an
/// INTEGER (xPermille, yPermille) sample stream plus strike events, quantized at a fixed cadence
/// and capped at <see cref="MaxSamples"/> pairs. <see cref="ShapingDone"/> hands that PARTIAL
/// trace (plus the ending heat) to whatever opens <see cref="QuenchMinigame"/>; the actual quality
/// math (deviation scoring, grade fold, RNG jitter, material ceiling) lives sim-side in
/// <c>ForgeScorer</c>/<c>QualityRoller</c> and never runs here.</para>
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

    /// <summary>
    /// Shape lost per second while the bellows are held. <b>Was 50, which made the craft very nearly
    /// unwinnable</b> — exactly what Brian reported: "also doesn't seem possible to complete? the shape
    /// keeps resetting to zero", then "i am incapable of creating anything - something is wrong lol".
    ///
    /// <para><b>Sized from measurement, not feel</b> (<c>ForgeWinnabilityTests</c> + <c>ForgePlayer</c>).
    /// 8 leaves the mechanic doing its actual job — pumping costs you tempo, so you cannot idle on the
    /// bellows — while a first-timer actually finishes. Unchanged by the U7 two-act split: this constant
    /// governs the heat/shape ECONOMY, not the finish line, and that economy is exactly what the owner
    /// called out as fun ("anvil + bellows work together") — only the distance to travel and the
    /// per-strike payoff moved.</para>
    /// </summary>
    public const int BellowsDriftBackPermillePerSecond = 8;

    public const int StrikeHeatCostPermille = 90;
    public const double StrikeOnTempoBonusMultiplier = 2.2;
    public const double TempoPeriodSeconds = 0.6;
    public const int TempoOnBeatWindowPermille = 180;

    /// <summary>
    /// Act 1's finish line — the SAME boundary <c>ForgeScorer</c> already uses to end its "forge"
    /// sample bucket (<c>ForgePath.ForgeZoneEnd</c>, 666). Referencing the sim's own constant
    /// rather than duplicating the number means the two can never drift apart: whatever x the
    /// scorer treats as "smelt+forge finished, quench begins" is exactly the x this overlay stops
    /// at and hands to <see cref="QuenchMinigame"/>.
    /// </summary>
    public const int ShapingFinishPermille = ForgePath.ForgeZoneEnd;

    /// <summary>Strikes required to finish Act 1 with NO demonstrated accuracy yet (a first craft,
    /// or a different recipe this session has no track record on) — unchanged from the value
    /// already CI-proven safe against the tempo invariant. See <see cref="RequiredStrikes"/>.</summary>
    public const int BaseRequiredStrikes = 21;

    /// <summary>Strikes required at maximum demonstrated accuracy (1000‰) — the skill floor a
    /// proven player converges toward, and the lowest value measured to keep
    /// <c>ForgeWinnabilityTests</c>' tempo-tight-beats-tempo-loose invariant robust. See <see
    /// cref="RequiredStrikes"/>'s own doc for the measurement.</summary>
    public const int MinRequiredStrikes = 18;

    /// <summary>Aimed-strike hit box: a left-click only registers as a strike inside this generous
    /// square (px), centred on the billet's actual screen anchor — Space stays unaimed/always valid
    /// (KTD-C keyboard parity).</summary>
    public const float BilletHitBoxSize = 96f;

    /// <summary>One bellows pump stroke's fixed heat quantum (per-mille) — the discrete counterpart
    /// to holding the bellows, fired once per <see cref="PumpStrokeDragPixels"/> of downward
    /// right-drag (KTD-B: raw motion floats never reach a scorer, only these integer calls do).</summary>
    public const int PumpStrokeHeatPermille = 55;

    /// <summary>Downward right-drag pixels per <see cref="PumpStroke"/> call.</summary>
    public const int PumpStrokeDragPixels = 18;

    public string RecipeId { get; private set; } = string.Empty;
    public string MaterialKey { get; private set; } = string.Empty;

    /// <summary>The integer seed selecting this craft's forging-line variant — derived
    /// deterministically from the recipe id + day (<see cref="Configure"/>), never RNG, and
    /// carried forward through <see cref="ShapingDone"/> so <see cref="QuenchMinigame"/> and the
    /// sim regenerate the IDENTICAL line this overlay rendered.</summary>
    public int PathSeed { get; private set; }

    /// <summary>The shared target line (<c>ForgePath.Generate</c>) this overlay renders — spans the
    /// FULL sim domain (x 0..1000) even though this act only travels x 0..<see cref="ShapingFinishPermille"/>,
    /// because the heat GAUGE reads ahead of the cursor (<see cref="AnvilMapCanvas"/>'s anticipation
    /// cue) and because <see cref="QuenchMinigame"/> regenerates the SAME path from the SAME seed
    /// for its own x 667..1000 stretch.</summary>
    public ImmutableList<int> Path { get; private set; } = ImmutableList<int>.Empty;

    /// <summary>Shape progress, per-mille — the cursor's X axis. Clamped to
    /// [0, <see cref="ShapingFinishPermille"/>]: this act never sees x beyond its own finish line.</summary>
    public int ShapeXPermille { get; private set; }

    /// <summary>Current heat, per-mille [0..1000] — the cursor's Y axis.</summary>
    public int HeatYPermille { get; private set; } = 500;

    /// <summary>True while the bellows are held — hammering is disabled during a pump
    /// (the two inputs are mutually exclusive, per spec).</summary>
    public bool IsPumping { get; private set; }

    /// <summary>True once <see cref="ShapeXPermille"/> has reached <see cref="ShapingFinishPermille"/> —
    /// Act 1 is done and <see cref="ShapingDone"/> has fired exactly once.</summary>
    public bool Completed { get; private set; }

    public bool WasCancelled { get; private set; }

    /// <summary>How many strikes this run has landed so far — test/inspection surface and the
    /// player-facing progress readout (against <see cref="RequiredStrikes"/>).</summary>
    public int StrikesLanded => _strikes.Count / 2;

    /// <summary>
    /// Strikes needed to finish Act 1 THIS run, computed once in <see cref="Configure"/> from the
    /// caller's demonstrated accuracy: <see cref="BaseRequiredStrikes"/> at 0‰ falling linearly to
    /// <see cref="MinRequiredStrikes"/> at 1000‰. Falling <see cref="RequiredStrikes"/> is the whole
    /// mechanism behind "you get faster as you get better" (R6) — it does not just shorten the
    /// count, it directly sets each strike's shape payoff (<see cref="ShapingFinishPermille"/>
    /// divided across this many strikes), so a proven player's swings are literally bigger, not
    /// just more frequent.
    /// </summary>
    public int RequiredStrikes { get; private set; } = BaseRequiredStrikes;

    /// <summary>A read-only PARTIAL preview of Act 1's own smelt+forge zones, computed once
    /// <see cref="ShapingDone"/> fires by calling the SAME pure <c>ForgeScorer.Score</c> on the
    /// trace captured so far (quench zone necessarily scores 0 — Act 2 hasn't run yet — so this is
    /// a pessimistic lower bound, never the craft's real grade). Telemetry/test surface only; never
    /// shown as "the grade" in the readout, which would misrepresent an unfinished craft.</summary>
    public int? PreviewGradePermille { get; private set; }

    private int _strikeAdvancePermille;
    private Recipe? _recipe;
    private ProfessionDefinition? _profession;
    private ImmutableSortedSet<string> _unlockedTalents = ImmutableSortedSet<string>.Empty;

    /// <summary>Raised EXACTLY ONCE, the instant <see cref="ShapeXPermille"/> reaches
    /// <see cref="ShapingFinishPermille"/> — Act 1's handoff to <see cref="QuenchMinigame"/>.
    /// Carries the partial trace (samples/strikes so far), the ending heat, and the path seed;
    /// no <see cref="CraftAction"/> exists yet — the craft is not scoreable until Act 2 supplies
    /// the quench-zone samples.</summary>
    public event Action<ShapingResult>? ShapingDone;

    /// <summary>Raised on <see cref="Cancel"/> — the caller queues nothing and spends nothing:
    /// Act 1 never builds a <see cref="CraftAction"/>, so an abandoned run leaves no partial item
    /// and no spent material by construction (there is nothing to un-queue).</summary>
    public event Action? Cancelled;

    /// <summary>Raised inside <see cref="ForgeStrike"/> with whether THAT strike landed inside the
    /// tempo window — judged before the strike itself mutates anything, so a listener is reading
    /// the same judgement the trace is about to score, never a second opinion. Drives the
    /// spark-burst/flash VFX + hammer-clang SFX (G1 staging, same idiom as the old minigame).</summary>
    public event Action<bool>? Struck;

    /// <summary>
    /// Raised at the start of a bellows breath — <see cref="BellowsStart"/> (Shift held) or one
    /// discrete <see cref="PumpStroke"/> (right-drag quantum) — whichever gesture the player is
    /// actually using. Drives <see cref="GodotClient.Audio.Cue.Bellows"/>.
    /// </summary>
    public event Action? BellowsPumped;

    /// <summary>The Act 1 -> Act 2 handoff payload — everything <see cref="QuenchMinigame"/> needs
    /// to continue the SAME trace without re-deriving anything sim-side.</summary>
    /// <param name="Samples">Act 1's captured (xPermille, yPermille) sample stream so far.</param>
    /// <param name="Strikes">Act 1's captured (xPermille, tempoErrorPermille) strike stream so far.</param>
    /// <param name="PathSeed">The seed both acts and the sim regenerate the SAME <c>ForgePath</c> from.</param>
    /// <param name="HeatYPermille">The heat Act 1 ended on — Act 2's quench starts from here, cooling further.</param>
    /// <param name="StrikesLanded">How many strikes Act 1 took — carried for telemetry/UI only.</param>
    public readonly record struct ShapingResult(
        ImmutableList<int> Samples,
        ImmutableList<int> Strikes,
        int PathSeed,
        int HeatYPermille,
        int StrikesLanded);

    private readonly List<int> _samples = new();
    private readonly List<int> _strikes = new();
    private double _elapsed;
    private double _sampleAccumulator;

    private bool _pumpDragArmed;
    private double _pumpDragAccumulatorPixels;

    private Label _titleLabel = null!;
    private AnvilMapCanvas _canvas = null!;
    private Label _readoutLabel = null!;
    private Button _hammerButton = null!;
    private Button _bellowsButton = null!;
    private Button _cancelButton = null!;
    private bool _built;

    public override void _Ready() => EnsureBuilt();

    public override void _Process(double delta) => Advance(delta);

    /// <summary>
    /// Bind a fresh run for this recipe/material/talent context and regenerate the shared target
    /// line from a seed derived (no RNG — <c>StableHash</c>) from the recipe id + <paramref name="day"/>.
    /// Safe to call repeatedly — always leaves a clean, un-completed run.
    /// </summary>
    /// <param name="demonstratedAccuracyPermille">The player's session-scoped track record (owned
    /// by the caller, e.g. <c>ForgePanel</c> — never persisted to the sim save), 0..1000. Defaults
    /// to 0 (no history) so every EXISTING call site compiles unchanged.</param>
    public void Configure(
        Recipe recipe, string materialKey, ProfessionDefinition profession, ImmutableSortedSet<string> unlockedTalents, int day,
        int demonstratedAccuracyPermille = 0)
    {
        EnsureBuilt();

        RecipeId = recipe.RecipeId;
        MaterialKey = materialKey;
        _recipe = recipe;
        _profession = profession;
        _unlockedTalents = unlockedTalents;

        PathSeed = unchecked((int)StableHash.Avalanche(StableHash.Mix(StableHash.HashString(recipe.RecipeId), unchecked((ulong)day))));
        Path = ForgePath.Generate(recipe.Tier, recipe.Slot, recipe.BaseStats.Weight, PathSeed);

        var accuracy = Math.Clamp(demonstratedAccuracyPermille, 0, 1000);
        var reduction = (int)Math.Round((BaseRequiredStrikes - MinRequiredStrikes) * (accuracy / 1000.0));
        RequiredStrikes = Math.Clamp(BaseRequiredStrikes - reduction, MinRequiredStrikes, BaseRequiredStrikes);
        _strikeAdvancePermille = (int)Math.Round(ShapingFinishPermille / (double)RequiredStrikes);

        ShapeXPermille = 0;
        HeatYPermille = ForgePath.HeatAt(Path, 0);
        IsPumping = false;
        Completed = false;
        WasCancelled = false;
        PreviewGradePermille = null;
        _samples.Clear();
        _strikes.Clear();
        _elapsed = 0;
        _sampleAccumulator = 0;

        RepaintUi();
    }

    /// <summary>Advance the run by <paramref name="delta"/> accumulated-clock seconds — public so
    /// tests drive scripted runs deterministically (no wall-clock, no engine RNG). Heat drains
    /// over time unless the bellows are held, in which case heat rises and shape drifts back
    /// slightly (can't hammer while pumping). Samples the cursor at a fixed cadence, capped at
    /// <see cref="MaxSamples"/> pairs.</summary>
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
    /// barely moves) and to <see cref="RequiredStrikes"/> (fewer strikes required = bigger payoff
    /// per strike), costs heat, and advances further when it lands inside the tempo window.
    /// No-op while pumping (mutually exclusive inputs) or once Act 1 is already done.</summary>
    public void ForgeStrike()
    {
        if (Completed || WasCancelled || IsPumping)
        {
            return;
        }

        var tempoError = TempoErrorPermilleNow();
        var onTempo = tempoError <= TempoOnBeatWindowPermille;
        RecordStrike(tempoError);

        var multiplier = onTempo ? StrikeOnTempoBonusMultiplier : 1.0;
        var advance = (int)Math.Round(_strikeAdvancePermille * (HeatYPermille / 1000.0) * multiplier);
        ShapeXPermille = Math.Clamp(ShapeXPermille + Math.Max(0, advance), 0, ShapingFinishPermille);
        HeatYPermille = Math.Clamp(HeatYPermille - StrikeHeatCostPermille, 0, 1000);

        Struck?.Invoke(onTempo);
        _canvas.OnStruck(onTempo);
        RepaintUi();

        if (ShapeXPermille >= ShapingFinishPermille)
        {
            FinishShaping();
        }
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
        BellowsPumped?.Invoke();
        RepaintUi();
    }

    /// <summary>Release the bellows.</summary>
    public void BellowsStop()
    {
        IsPumping = false;
        RepaintUi();
    }

    /// <summary>One bellows PUMP STROKE — the discrete counterpart to holding the bellows: raises
    /// heat by exactly <see cref="PumpStrokeHeatPermille"/> (clamped at 1000) and applies the SAME
    /// shape-drifts-back rule <see cref="Advance"/> already uses while pumping, scaled to the
    /// equivalent time slice this quantum represents. No wall-clock, no RNG, callable directly by
    /// a headless test.</summary>
    public void PumpStroke()
    {
        if (Completed || WasCancelled)
        {
            return;
        }

        var equivalentSeconds = PumpStrokeHeatPermille / (double)BellowsRaisePermillePerSecond;
        HeatYPermille = Math.Clamp(HeatYPermille + PumpStrokeHeatPermille, 0, 1000);
        ShapeXPermille = Math.Max(0, ShapeXPermille - (int)Math.Round(BellowsDriftBackPermillePerSecond * equivalentSeconds));
        BellowsPumped?.Invoke();
        RepaintUi();
    }

    /// <summary>Pure hit-test for an aimed strike: true iff <paramref name="localPos"/> — in
    /// THIS overlay's own local coordinate space — falls within a generous
    /// <see cref="BilletHitBoxSize"/>px square centred on where the billet is actually drawn.
    /// Side-effect-free, so a headless test can call it directly.</summary>
    public bool WouldHit(Vector2 localPos)
    {
        var anchor = BilletAnchorInPanelSpace();
        var half = BilletHitBoxSize / 2f;
        return Math.Abs(localPos.X - anchor.X) <= half && Math.Abs(localPos.Y - anchor.Y) <= half;
    }

    /// <summary>The billet's current screen anchor, in this overlay's own local space — exposed
    /// read-only purely so a headless test can locate <see cref="WouldHit"/>'s hit box without
    /// duplicating canvas-private layout math.</summary>
    public Vector2 BilletAnchor => BilletAnchorInPanelSpace();

    /// <summary>Abandon the run — queues nothing (<see cref="Cancelled"/> only). Leaves no partial
    /// item and no spent material: Act 1 never builds a <see cref="CraftAction"/> at all, so there
    /// is nothing for a cancel to un-do.</summary>
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

    /// <summary>Escape cancels the run — routed through <see cref="Cancel"/> (shared mechanism, <see
    /// cref="ModalEscape"/>). Overrides <c>_Input</c> (not <c>_GuiInput</c>) deliberately: this
    /// overlay is nested DRAWER CONTENT, which already owns Escape for the WHOLE drawer. Godot
    /// calls <c>_Input</c> in reverse tree order — children before parents — so this fires and
    /// marks the event handled before the drawer ever sees it.</summary>
    public override void _Input(InputEvent @event) => ModalEscape.TryClose(@event, GetViewport(), Visible, Cancel);

    /// <summary>Real-time input mapping — routes to the SAME public seam methods a scripted test or
    /// the button row drives, so there is exactly one code path for "what a gesture does" regardless
    /// of input source. Space always strikes unaimed (accessible path); a left-click only strikes if
    /// it lands on the billet (<see cref="WouldHit"/>); Shift holds the bellows; a right-button DRAG
    /// quantizes into discrete <see cref="PumpStroke"/> calls every <see cref="PumpStrokeDragPixels"/>
    /// of downward motion (no raw float ever reaches a scorer).</summary>
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
                if (WouldHit(mb.Position))
                {
                    ForgeStrike();
                }

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
    /// banked progress.</summary>
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
    /// coupling to the canvas's private draw-time internals.</summary>
    private static Vector2 BilletAnchorFor(Vector2 canvasSize) => new(canvasSize.X * 0.45f, canvasSize.Y * 0.79f);

    private Vector2 BilletAnchorInPanelSpace() => ToPanelSpace(BilletAnchorFor(_canvas.Size));

    /// <summary>Translates a point from the Anvil Map canvas's own local coordinate space into this
    /// overlay's local space (composing global transforms).</summary>
    private Vector2 ToPanelSpace(Vector2 canvasLocalPos)
    {
        var globalPos = _canvas.GetGlobalTransform() * canvasLocalPos;
        return GetGlobalTransform().AffineInverse() * globalPos;
    }

    /// <summary>G1 result ceremony banding: a presentation-only PREVIEW of which
    /// <see cref="QualityGrade"/> band a folded per-mille grade is heading toward — mirrors
    /// <c>QualityRoller.RollActive</c>'s own band thresholds (200/550/780/930) but deliberately
    /// WITHOUT its ±25 jitter or its material-grade ceiling. Static/pure so every craft overlay
    /// (this one's Act 2 sibling, plus Brew/Assemble/Scrape) shares ONE banding rule.</summary>
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
    /// — no engine RNG, no wall-clock.</summary>
    private int TempoErrorPermilleNow()
    {
        var phase = _elapsed % TempoPeriodSeconds;
        var halfPeriod = TempoPeriodSeconds / 2.0;
        var distance = Math.Min(phase, TempoPeriodSeconds - phase);
        return (int)Math.Round(Math.Clamp(distance / halfPeriod, 0.0, 1.0) * 1000.0);
    }

    private void FinishShaping()
    {
        Completed = true;

        if (_recipe is not null && _profession is not null)
        {
            var partial = new ForgeTraceInput(ImmutableList.CreateRange(_samples), ImmutableList.CreateRange(_strikes), PathSeed);
            PreviewGradePermille = ForgeScorer.Score(_recipe, partial, _unlockedTalents, _profession).GradePermille;
        }

        var result = new ShapingResult(
            ImmutableList.CreateRange(_samples),
            ImmutableList.CreateRange(_strikes),
            PathSeed,
            HeatYPermille,
            StrikesLanded);
        RepaintUi();
        ShapingDone?.Invoke(result);
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

        // Sets FocusMode AND actually takes focus — see UiKit.ClaimKeyboard's own doc for why a
        // bare FocusMode assignment alone left the craft unplayable from the keyboard.
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

        _cancelButton = new Button { Name = "ForgeMinigameCancel", Text = "Cancel" };
        _cancelButton.Pressed += Cancel;
        buttonRow.AddChild(_cancelButton);

        // Control buttons must never hold the keyboard — a focused Button eats Space to press
        // itself, which is why clicking "Bellows" once made Space pump instead of strike.
        UiKit.MakeButtonsMouseOnly(this);

        _built = true;
        RepaintUi();
    }

    // Last-rendered label state — so the per-frame RepaintUi (called from _Process→Advance every
    // frame) only rebuilds the readout/title strings when something actually changed.
    private int _lastShapeX = int.MinValue;
    private int _lastHeatY = int.MinValue;
    private bool _lastPumping;
    private bool _lastCompleted;
    private bool _lastCancelled;

    /// <summary>Render-only — reads the current run state, writes no scoring state. Called after
    /// every state-changing call above AND every frame via <see cref="Advance"/> (for the canvas's
    /// live tempo/heat animation); the label/button rebuild is gated on an actual state change.</summary>
    private void RepaintUi()
    {
        if (!_built)
        {
            return;
        }

        _canvas.Path = Path;
        _canvas.ShapeFinishPermille = ShapingFinishPermille;
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

        _titleLabel.Text = $"Shape it: {RecipeId}";

        _readoutLabel.Text = WasCancelled
            ? "Cancelled."
            : Completed
                ? "Shaped! Quenching next..."
                : $"Strike {StrikesLanded}/{RequiredStrikes} — Heat {HeatYPermille} — {(IsPumping ? "pumping" : "idle")}";

        _hammerButton.Disabled = Completed || WasCancelled || IsPumping;
        _bellowsButton.Disabled = Completed || WasCancelled;
    }

    /// <summary>
    /// The forge cross-section: a side-view of the smith's fire where the billet — a real
    /// heat-glowing, morphing sprite held in tongs — is steered along the shared target line
    /// (<see cref="Path"/>). A metronome hammer winds up and falls exactly on the tempo beat so the
    /// player can time on-tempo strikes; strikes throw a spark burst + a small shake, and the
    /// bellows brighten the coals. Plain <see cref="_Draw"/> primitives + a handful of
    /// nearest-filtered sprites (each null-checked with a primitive fallback so headless CI still
    /// renders) — never a 3D <c>SubViewport</c>. X = shape progress (left→right, this act's own
    /// [0, <see cref="ShapeFinishPermille"/>] range), Y = heat (bottom cold → top hot, full [0,1000]).
    /// </summary>
    private sealed partial class AnvilMapCanvas : Control
    {
        public ImmutableList<int> Path = ImmutableList<int>.Empty;
        public int CursorXPermille;
        public int CursorYPermille;
        public int ShapeFinishPermille = 1000; // this act's own finish line — see ForgeMinigame.ShapingFinishPermille
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

        /// <summary>Hit-stop: seconds still owed to freezing <see cref="_anim"/> after an on-tempo
        /// strike (~<see cref="HitStopSeconds"/>) — accumulated and drained via the SAME
        /// <see cref="_Process"/> accumulated-clock pattern as everything else here; never a sleep,
        /// never a wall-clock read.</summary>
        public const float HitStopSeconds = 0.04f;

        private readonly List<Particle> _sparks = new();
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

            // Hit-stop: an on-tempo strike owes ~40ms of frozen animation clock — skip advancing
            // _anim while that's still owed (particles/shake/ring keep moving; only the ambient
            // coal/hammer-swing clock pauses, which is what actually reads as "impact").
            if (_hitStopRemaining > 0f)
            {
                _hitStopRemaining = Math.Max(0f, _hitStopRemaining - dt);
            }
            else
            {
                _anim += dt;
            }

            StepParticles(_sparks, dt, gravity: 320f);
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
                _hitStopRemaining = HitStopSeconds;
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

            // With the painted forge interior in place, the procedural furnace/anvil would fight
            // it — the art already IS the room. They stay only as the no-art fallback.
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

            if (hasPath)
            {
                DrawHeatGauge(size);
                DrawShapeMeter(size);
            }

            if (!art)
            {
                DrawAnvil(size);
            }

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
        /// band. This is what replaces the old target polyline — the same numbers the sim scores, read
        /// as an instrument on the wall rather than a plot to trace.
        /// </summary>
        private void DrawHeatGauge(Vector2 size)
        {
            var x = size.X * 0.085f;
            var top = size.Y * 0.16f;
            var bottom = size.Y * 0.86f;
            var w = 22f;
            var h = bottom - top;

            float YFor(int permille) => bottom - Math.Clamp(permille, 0, 1000) / 1000f * h;

            DrawRect(new Rect2(x - 4, top - 5, w + 8, h + 10), new Color(0.07f, 0.06f, 0.10f, 0.82f));
            DrawRect(new Rect2(x - 4, top - 5, w + 8, h + 10), new Color(0.45f, 0.40f, 0.34f, 0.9f), filled: false, width: 2f);

            const int cells = 16;
            for (var i = 0; i < cells; i++)
            {
                var f = 1f - (i + 0.5f) / cells;
                var cy = top + h * i / cells;
                DrawRect(new Rect2(x, cy, w, h / cells + 1f), new Color(HeatColor(f), 0.30f));
            }

            var target = InterpTargetHeat(CursorXPermille);
            var bandTop = YFor(target + SweetSpotHalfWidthPermille);
            var bandBottom = YFor(target - SweetSpotHalfWidthPermille);
            var dev = Math.Abs(CursorYPermille - target);
            var band = dev < 90 ? DeviationGood : dev < 220 ? DeviationWarn : DeviationBad;
            DrawRect(new Rect2(x - 2, bandTop, w + 4, bandBottom - bandTop), new Color(band, 0.22f));
            DrawRect(new Rect2(x - 2, bandTop, w + 4, bandBottom - bandTop), new Color(band, 0.95f), filled: false, width: 2f);

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

        /// <summary>The shape meter: how far the bar has been drawn out toward THIS ACT's finish
        /// line (<see cref="ShapeFinishPermille"/>, not the sim's full 1000-domain) — segmented, so
        /// each on-tempo strike visibly claims another notch.</summary>
        private void DrawShapeMeter(Vector2 size)
        {
            const int cells = 12;
            var w = size.X * 0.30f;
            var x = size.X * 0.62f;
            var y = size.Y * 0.17f;
            var cw = w / cells;

            DrawRect(new Rect2(x - 3, y - 4, w + 6, 20f), new Color(0.07f, 0.06f, 0.10f, 0.82f));
            var fraction = ShapeFinishPermille > 0 ? Math.Clamp(CursorXPermille / (float)ShapeFinishPermille, 0f, 1f) : 0f;
            var filled = Mathf.CeilToInt(fraction * cells);
            for (var i = 0; i < cells; i++)
            {
                var r = new Rect2(x + i * cw + 1f, y, cw - 2f, 12f);
                if (i < filled)
                {
                    var done = CursorXPermille >= ShapeFinishPermille;
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

        private void DrawBillet(Vector2 cursor)
        {
            var heatFrac = Math.Clamp(CursorYPermille / 1000f, 0f, 1f);
            var body = HeatColor(heatFrac);
            var glowR = 14f + 10f * heatFrac;
            DrawCircle(cursor, glowR * 1.5f, new Color(body, 0.16f)); // wide bloom, reads over art
            DrawCircle(cursor, glowR, new Color(body, 0.34f));        // heat halo

            var shapeFraction = ShapeFinishPermille > 0 ? CursorXPermille / (float)ShapeFinishPermille : 0f;
            var frame = shapeFraction >= 0.9f ? 3 : shapeFraction >= 0.65f ? 2 : shapeFraction >= 0.3f ? 1 : 0;
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
    }

    /// <summary>
    /// Claim the keyboard the moment this overlay actually appears on screen.
    ///
    /// <para>Focus used to be claimed once from <c>EnsureBuilt</c>, which production runs at boot
    /// with the overlay HIDDEN — and <see cref="GodotClient.Ui.UiKit.ClaimKeyboard"/> defers its grab
    /// behind an <c>IsVisibleInTree()</c> guard, so that grab silently did nothing and was never
    /// retried. The only moment that is reliably correct is when THIS node becomes visible in the
    /// tree, which is exactly what this notification reports. The overlay owns its own focus; no
    /// caller has to remember — <see cref="QuenchMinigame"/> repeats this exact pattern for the
    /// SAME reason on Act 2's handoff (PT1's dead-keyboard bug, do not reintroduce it).</para>
    /// </summary>
    public override void _Notification(int what)
    {
        if (what == NotificationVisibilityChanged && IsVisibleInTree())
        {
            GodotClient.Ui.UiKit.ClaimKeyboard(this);
        }
    }
}
