using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Professions;
using Godot;
using GodotClient.Ui;

namespace GodotClient.Minigames;

/// <summary>
/// U7 (2026-08-04-001 "verify by playing" plan): ACT 2 of the two-act forge — "then you squelch
/// the item" (the owner's own description). A single TIMED plunge: heat keeps falling on its own
/// once <see cref="ForgeMinigame"/> hands off (no bellows here — Act 1 already spent that skill),
/// x auto-advances from <see cref="ForgeMinigame.ShapingFinishPermille"/> to 1000 over
/// <see cref="QuenchDurationSeconds"/>, and the player has exactly ONE decisive input —
/// <see cref="Plunge"/> — to lock the quench in. Pressing while heat sits inside
/// <see cref="BandHalfWidthPermille"/> of the target trough (<see cref="TargetTroughPermille"/>,
/// the SAME <c>ForgePath.HeatAt</c> the sim scores against) is a good quench; waiting out the
/// timer auto-plunges at whatever heat is left, so the act can never hang.
///
/// <para><b>Short and decisive by construction.</b> The timer is fixed regardless of skill — this
/// act is about TIMING, not endurance — but a player who reads the gauge and plunges the instant
/// it enters the band finishes well before the timeout rather than waiting out the full window
/// (measured via a standalone harness referencing the REAL <c>ForgePath</c> directly: a decisive
/// plunge off a typical Act 1 hand-off heat lands in roughly 1-3s, chaining with Act 1's own ~8.6s
/// skilled run to ~9.7s combined — under the plan's ~10s bar — against the ~4.0s a passive player
/// gets from the auto-timeout alone). <see cref="BandHalfWidthPermilleForTier"/> narrows for
/// higher-tier metal (R6's "high metals are more precise") — a pure function, independent of Act 1's
/// skill-curve knob (<see cref="ForgeMinigame.RequiredStrikes"/>), so the two owner asks ("faster
/// with skill" / "more precise at high tier") come from one coherent skill-curve SYSTEM without
/// being the same number.</para>
///
/// <para><b>Owns the ONE <see cref="CraftAction"/> (PKD8 single-action contract).</b> Act 1 never
/// builds one — it only has a partial trace. This class completes the trace with its own
/// quench-zone samples, submits it to the SAME pure <c>ForgeScorer</c> the old single-meter overlay
/// used (read-only, for the UI preview only), and raises <see cref="Finished"/> EXACTLY ONCE, on
/// <see cref="Plunge"/> (manual or auto-timeout). <see cref="Cancel"/> raises <see cref="Cancelled"/>
/// instead and the caller queues nothing — same as Act 1, so cancelling here ALSO leaves no spent
/// material: nothing was ever queued until this point either way.</para>
///
/// <para><b>Adapter-only (KTD2):</b> integer trace only, no floats/RNG/wall-clock crossing the
/// boundary. Plain 2D <see cref="Control"/> canvas — never a 3D <c>SubViewport</c>.</para>
/// </summary>
public sealed partial class QuenchMinigame : PanelContainer
{
    public const double SampleIntervalSeconds = ForgeMinigame.SampleIntervalSeconds;
    public const int MaxSamples = ForgeMinigame.MaxSamples;

    /// <summary>Fixed real-time window for the whole act — "roughly five seconds" per the plan,
    /// picked slightly under so a decisive plunge plus Act 1 comfortably clears the "~10s combined"
    /// bar even on the slowest legal path (auto-timeout).</summary>
    public const double QuenchDurationSeconds = 4.0;

    /// <summary>Heat falls far faster here than Act 1's ambient drain (<see
    /// cref="ForgeMinigame.HeatDrainPermillePerSecond"/>, 70/s) — this act has no bellows to fight
    /// it, and the whole point is a SHORT, decisive cool-down into the trough rather than a long wait.</summary>
    public const int QuenchHeatDrainPermillePerSecond = 180;

    private const int Tier1BandHalfWidthPermille = 140;
    private const int Tier2BandHalfWidthPermille = 100;
    private const int Tier3BandHalfWidthPermille = 70;

    /// <summary>R6, "high metals are more precise": the acceptable-plunge band narrows as recipe
    /// tier rises. Pure and static so a headless test can pin the narrowing without building an
    /// overlay at all.</summary>
    public static int BandHalfWidthPermilleForTier(int tier) => Math.Clamp(tier, 1, 3) switch
    {
        1 => Tier1BandHalfWidthPermille,
        2 => Tier2BandHalfWidthPermille,
        _ => Tier3BandHalfWidthPermille,
    };

    public string RecipeId { get; private set; } = string.Empty;
    public string MaterialKey { get; private set; } = string.Empty;
    public int PathSeed { get; private set; }
    public ImmutableList<int> Path { get; private set; } = ImmutableList<int>.Empty;

    /// <summary>Shape/x position — auto-advances from <see cref="ForgeMinigame.ShapingFinishPermille"/>
    /// to 1000 over <see cref="QuenchDurationSeconds"/>. The player never drives this directly.</summary>
    public int XPermille { get; private set; }

    public int HeatYPermille { get; private set; }

    /// <summary>This craft's acceptable-plunge half-width, set from the recipe's tier in
    /// <see cref="Configure"/>. See <see cref="BandHalfWidthPermilleForTier"/>.</summary>
    public int BandHalfWidthPermille { get; private set; }

    /// <summary>The target heat at the END of the path (<c>ForgePath.HeatAt(Path, 1000)</c>) — the
    /// SAME quench-trough value the sim scores the tail of the trace against. Exposed read-only so
    /// the canvas (and a test) can judge "is the gauge in the band" without duplicating sim math.</summary>
    public int TargetTroughPermille => ForgePath.HeatAt(Path, 1000);

    public bool Completed { get; private set; }
    public bool WasCancelled { get; private set; }

    public CraftAction? EmittedAction { get; private set; }
    public int? PreviewGradePermille { get; private set; }
    public ImmutableList<int>? PreviewSubScores { get; private set; }

    /// <summary>Raised EXACTLY ONCE, on <see cref="Plunge"/> (manual or auto-timeout), with the one
    /// action to queue.</summary>
    public event Action<CraftAction>? Finished;

    /// <summary>Raised on <see cref="Cancel"/> — the caller queues nothing.</summary>
    public event Action? Cancelled;

    /// <summary>Raised inside <see cref="Plunge"/>, before the run finishes — drives the
    /// steam-plume VFX at the moment the player locks the quench in.</summary>
    public event Action? Quenched;

    private readonly List<int> _samples = new();
    private readonly List<int> _strikes = new();
    private double _elapsed;
    private double _sampleAccumulator;

    private Recipe? _recipe;
    private ProfessionDefinition? _profession;
    private ImmutableSortedSet<string> _unlockedTalents = ImmutableSortedSet<string>.Empty;

    private Label _titleLabel = null!;
    private QuenchCanvas _canvas = null!;
    private Label _readoutLabel = null!;
    private Button _plungeButton = null!;
    private Button _cancelButton = null!;
    private bool _built;

    /// <summary>U6 (campaign finding: <c>_Process</c> called <see cref="Advance"/> unconditionally
    /// from tree entry, and <see cref="ForgePanel"/> pre-builds this node — hidden, but still
    /// ticking — at its own <c>_Ready</c>. With <see cref="RecipeId"/>/<see cref="MaterialKey"/>
    /// defaulting to empty until the first real <see cref="Configure"/> call, that meant 4.0s
    /// (<see cref="QuenchDurationSeconds"/>) after boot the auto-plunge timeout fired anyway and
    /// emitted a phantom <c>CraftAction("", "")</c> through <see cref="Finished"/> — rejected
    /// <c>Unknown recipe ''.</c> in every one of 34/34 campaign runs, before the player had ever
    /// opened the forge.) Set true at the end of <see cref="Configure"/>; <see cref="Advance"/>
    /// no-ops until then. <see cref="Configure"/> runs fresh on every real reuse (Act 1 → Act 2
    /// handoff, <c>ForgePanel.OnShapingDone</c>), so this only ever gates the PRE-first-craft
    /// window — once true it stays true, and <see cref="Completed"/>/<see cref="WasCancelled"/>
    /// already gate ticking between one real run finishing and the next one's <see
    /// cref="Configure"/> call.</summary>
    private bool _configured;

    public override void _Ready() => EnsureBuilt();

    public override void _Process(double delta) => Advance(delta);

    /// <summary>Bind a fresh Act 2 run from Act 1's handoff — regenerates the SAME <c>ForgePath</c>
    /// from the carried seed (byte-for-byte agreement with both Act 1 and the sim scorer) and
    /// continues the SAME sample/strike stream <paramref name="handoff"/> carries.</summary>
    public void Configure(
        Recipe recipe, string materialKey, ProfessionDefinition profession, ImmutableSortedSet<string> unlockedTalents,
        ForgeMinigame.ShapingResult handoff)
    {
        EnsureBuilt();

        RecipeId = recipe.RecipeId;
        MaterialKey = materialKey;
        _recipe = recipe;
        _profession = profession;
        _unlockedTalents = unlockedTalents;

        PathSeed = handoff.PathSeed;
        Path = ForgePath.Generate(recipe.Tier, recipe.Slot, recipe.BaseStats.Weight, PathSeed);
        BandHalfWidthPermille = BandHalfWidthPermilleForTier(recipe.Tier);

        _samples.Clear();
        _samples.AddRange(handoff.Samples);
        _strikes.Clear();
        _strikes.AddRange(handoff.Strikes);

        XPermille = ForgeMinigame.ShapingFinishPermille;
        HeatYPermille = handoff.HeatYPermille;
        Completed = false;
        WasCancelled = false;
        EmittedAction = null;
        PreviewGradePermille = null;
        PreviewSubScores = null;
        _elapsed = 0;
        _sampleAccumulator = 0;
        RecordSample(); // seed the domain-boundary sample at the exact x Act 1 handed off

        _configured = true; // U6: only now may Advance() actually tick — see the field's own doc
        RepaintUi();
    }

    /// <summary>Advance the timed window by <paramref name="delta"/> accumulated-clock seconds — no
    /// wall-clock, no engine RNG, so a scripted test drives this exactly like <see
    /// cref="ForgeMinigame.Advance"/>. Auto-plunges the instant the window closes, so the act can
    /// never hang waiting on an input a player forgot to give.</summary>
    public void Advance(double delta)
    {
        // U6: unconfigured — never bound to a real recipe/material by Configure() — must no-op,
        // not auto-plunge a phantom CraftAction("", "") once the fixed timeout elapses. See
        // _configured's own doc for the exact campaign-observed failure this guards.
        if (!_configured || Completed || WasCancelled || delta <= 0)
        {
            return;
        }

        _elapsed += delta;
        var t = Math.Clamp(_elapsed / QuenchDurationSeconds, 0.0, 1.0);
        XPermille = ForgeMinigame.ShapingFinishPermille +
            (int)Math.Round(t * (1000 - ForgeMinigame.ShapingFinishPermille));
        HeatYPermille = Math.Max(0, HeatYPermille - (int)Math.Round(QuenchHeatDrainPermillePerSecond * delta));

        _sampleAccumulator += delta;
        while (_sampleAccumulator >= SampleIntervalSeconds && _samples.Count / 2 < MaxSamples)
        {
            RecordSample();
            _sampleAccumulator -= SampleIntervalSeconds;
        }

        RepaintUi();

        if (_elapsed >= QuenchDurationSeconds)
        {
            Plunge();
        }
    }

    /// <summary>The ONE decisive input: lock the quench in NOW, at the current (x, heat). Legal any
    /// time after <see cref="Configure"/> — there is no minimum wait, because timing the plunge
    /// WELL (not just eventually) is the entire skill this act teaches.</summary>
    public void Plunge()
    {
        if (Completed || WasCancelled)
        {
            return;
        }

        RecordSample();
        Quenched?.Invoke();
        _canvas.OnQuenched();
        Finish();
    }

    /// <summary>Abandon the run — queues nothing. Act 1 never queued anything either, so a cancel
    /// here leaves no partial item and no spent material, same guarantee as cancelling Act 1.</summary>
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

    /// <summary>Escape cancels the run — same <see cref="ModalEscape"/> mechanism and reverse-tree-
    /// order reasoning as <see cref="ForgeMinigame._Input"/>.</summary>
    public override void _Input(InputEvent @event) => ModalEscape.TryClose(@event, GetViewport(), Visible, Cancel);

    /// <summary>The <c>plunge</c> action (C2, <see cref="MinigameInput"/>) — Space/Enter/KpEnter,
    /// same as before, just routed through the <see cref="InputMap"/> instead of a raw <see
    /// cref="Key"/> match; nothing else to input here — Act 2 has no aim, no bellows, just
    /// timing.</summary>
    public override void _GuiInput(InputEvent @event)
    {
        if (Completed || WasCancelled)
        {
            return;
        }

        if (@event.IsActionPressed("plunge"))
        {
            Plunge();
        }
    }

    private void RecordSample()
    {
        if (_samples.Count / 2 >= MaxSamples)
        {
            return;
        }

        _samples.Add(XPermille);
        _samples.Add(HeatYPermille);
    }

    private void Finish()
    {
        Completed = true;
        var samples = ImmutableList.CreateRange(_samples);
        var strikes = ImmutableList.CreateRange(_strikes);
        var puzzle = new ForgeTraceInput(samples, strikes, PathSeed);

        if (_recipe is not null && _profession is not null)
        {
            var preview = ForgeScorer.Score(_recipe, puzzle, _unlockedTalents, _profession);
            PreviewGradePermille = preview.GradePermille;
            PreviewSubScores = preview.SubScores;
        }

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

        MinigameInput.RegisterActions(); // C2: plunge must exist before any _GuiInput can fire

        Name = "QuenchMinigame";
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        UiKit.ClaimKeyboard(this);

        var body = new VBoxContainer { Name = "QuenchMinigameBody" };
        AddChild(body);

        _titleLabel = new Label { Name = "QuenchMinigameTitle" };
        _titleLabel.AddThemeColorOverride("font_color", GameTheme.HeaderColor);
        _titleLabel.ThemeTypeVariation = GameTheme.HeaderThemeType;
        body.AddChild(_titleLabel);

        _canvas = new QuenchCanvas { Name = "QuenchCanvas", CustomMinimumSize = new Vector2(0, 340) };
        _canvas.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _canvas.SizeFlagsVertical = SizeFlags.ExpandFill;
        body.AddChild(_canvas);

        _readoutLabel = new Label { Name = "QuenchMinigameReadout" };
        body.AddChild(_readoutLabel);

        var buttonRow = new HBoxContainer { Name = "QuenchMinigameButtons" };
        body.AddChild(buttonRow);

        // C2: reads the LIVE InputMap binding instead of a hardcoded key name.
        _plungeButton = new Button { Name = "QuenchPlunge", Text = $"Plunge! ({MinigameInput.KeyLabelFor("plunge")})" };
        _plungeButton.Pressed += Plunge;
        buttonRow.AddChild(_plungeButton);

        _cancelButton = new Button { Name = "QuenchMinigameCancel", Text = "Cancel" };
        _cancelButton.Pressed += Cancel;
        buttonRow.AddChild(_cancelButton);

        UiKit.MakeButtonsMouseOnly(this);

        _built = true;
        RepaintUi();
    }

    private void RepaintUi()
    {
        if (!_built)
        {
            return;
        }

        _canvas.HeatYPermille = HeatYPermille;
        _canvas.TargetTroughPermille = TargetTroughPermille;
        _canvas.BandHalfWidthPermille = BandHalfWidthPermille;
        _canvas.Completed = Completed;
        _canvas.QueueRedraw();

        var inBand = Math.Abs(HeatYPermille - TargetTroughPermille) <= BandHalfWidthPermille;
        _readoutLabel.Text = WasCancelled
            ? "Cancelled."
            : Completed
                ? $"Quenched — grade {PreviewGradePermille}."
                : $"Heat {HeatYPermille} (target {TargetTroughPermille} +/-{BandHalfWidthPermille}) — {(inBand ? "PLUNGE NOW" : "wait for it...")}";

        _plungeButton.Disabled = Completed || WasCancelled;
    }

    /// <summary>The quench trough: a falling heat gauge with the acceptable-plunge band marked —
    /// the SAME numbers <see cref="TargetTroughPermille"/>/<see cref="BandHalfWidthPermille"/>
    /// expose, read as an instrument rather than a plot. Deliberately simpler than Act 1's
    /// full forge scene — this act is one gesture, not a workbench.</summary>
    private sealed partial class QuenchCanvas : Control
    {
        public int HeatYPermille;
        public int TargetTroughPermille;
        public int BandHalfWidthPermille;
        public bool Completed;

        private readonly List<(Vector2 Pos, Vector2 Vel, float Life, float MaxLife)> _steam = new();
        private float _anim;

        private static readonly Color WaterTeal = new(0.25f, 0.45f, 0.52f);
        private static readonly Color BandGood = new(0.55f, 0.9f, 1f);

        public override void _Process(double delta)
        {
            _anim += (float)delta;
            for (var i = _steam.Count - 1; i >= 0; i--)
            {
                var (pos, vel, life, maxLife) = _steam[i];
                life -= (float)delta;
                if (life <= 0f)
                {
                    _steam.RemoveAt(i);
                    continue;
                }

                _steam[i] = (pos + vel * (float)delta, vel, life, maxLife);
            }

            QueueRedraw();
        }

        /// <summary>Steam plume FX from the quench trough — driven by the sim's own Quenched event.</summary>
        public void OnQuenched()
        {
            var size = Size;
            var origin = new Vector2(size.X * 0.5f, size.Y * 0.7f);
            for (var i = 0; i < 10; i++)
            {
                var angle = Mathf.DegToRad(-90f + (i - 5) * 8f);
                _steam.Add((origin, new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 30f, 1.0f, 1.0f));
            }
        }

        public override void _Draw()
        {
            var size = Size;
            if (size.X <= 0 || size.Y <= 0)
            {
                return;
            }

            const int bands = 6;
            var h = size.Y / bands;
            for (var i = 0; i < bands; i++)
            {
                DrawRect(new Rect2(0, i * h, size.X, h + 1f), new Color(0.14f, 0.15f, 0.2f).Lerp(new Color(0.10f, 0.12f, 0.16f), (i + 0.5f) / bands));
            }

            var top = size.Y * 0.12f;
            var bottom = size.Y * 0.85f;
            var gaugeH = bottom - top;
            var x = size.X * 0.5f - 30f;
            var w = 60f;

            float YFor(int permille) => bottom - Math.Clamp(permille, 0, 1000) / 1000f * gaugeH;

            DrawRect(new Rect2(x - 6, top - 6, w + 12, gaugeH + 12), new Color(0.06f, 0.06f, 0.09f, 0.85f));
            DrawRect(new Rect2(x - 6, top - 6, w + 12, gaugeH + 12), new Color(0.45f, 0.4f, 0.34f, 0.9f), filled: false, width: 2f);

            var bandTop = YFor(TargetTroughPermille + BandHalfWidthPermille);
            var bandBottom = YFor(TargetTroughPermille - BandHalfWidthPermille);
            var inBand = Math.Abs(HeatYPermille - TargetTroughPermille) <= BandHalfWidthPermille;
            var bandColor = inBand ? BandGood.Lerp(Colors.White, 0.4f + 0.4f * Mathf.Sin(_anim * 6f)) : WaterTeal;
            DrawRect(new Rect2(x - 3, bandTop, w + 6, bandBottom - bandTop), new Color(bandColor, 0.35f));
            DrawRect(new Rect2(x - 3, bandTop, w + 6, bandBottom - bandTop), new Color(bandColor, 0.95f), filled: false, width: 2f);

            var mercuryY = YFor(HeatYPermille);
            var heatFrac = Math.Clamp(HeatYPermille / 1000f, 0f, 1f);
            var mercuryColor = new Color(0.85f, 0.31f, 0.16f).Lerp(new Color(0.3f, 0.5f, 0.6f), 1f - heatFrac);
            DrawRect(new Rect2(x, mercuryY, w, bottom - mercuryY), new Color(mercuryColor, 0.92f));
            DrawRect(new Rect2(x - 6, mercuryY - 2f, w + 12, 4f), new Color(1f, 0.97f, 0.88f, 0.95f)); // needle

            foreach (var (pos, _, life, maxLife) in _steam)
            {
                var t = 1f - life / maxLife;
                DrawCircle(pos, 3f + 6f * t, new Color(0.95f, 0.95f, 1f, 0.8f * (1f - t)));
            }
        }
    }

    /// <summary>Claim the keyboard the moment Act 2 actually appears on screen — the SAME fix
    /// <see cref="ForgeMinigame"/> carries, repeated here deliberately: PT1 was a dead-keyboard bug
    /// from a missing <c>GrabFocus</c> equivalent, and the two-act split introduces a SECOND overlay
    /// swap (Act 1 hides, Act 2 shows) where the same mistake could silently reappear if this
    /// notification handler were skipped "because ForgeMinigame already claims focus once".</summary>
    public override void _Notification(int what)
    {
        if (what == NotificationVisibilityChanged && IsVisibleInTree())
        {
            GodotClient.Ui.UiKit.ClaimKeyboard(this);
        }
    }
}
