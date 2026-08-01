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
/// U2 (plan <c>2026-07-28-004</c>): the tanner's scraping-frame overlay — the tanning counterpart
/// to <see cref="ForgeMinigame"/>/<see cref="AlchemyBrewPuzzle"/>, but the deliberately QUIET one.
/// The skill being tested is COVERAGE WITH RESTRAINT (<see cref="TanningScrapeScorer"/>'s own doc):
/// clean the whole hide, but do not scrape any spot through. Unlike the forge (a clock) or the
/// brew (a sequence to remember), there is no clock and no order here at all — the player may stop
/// and think mid-stroke, and cells may be worked in any order.
///
/// <para><b>Adapter-only (KTD2):</b> renders the SAME <see cref="TanningScrapeScorer.CellKind"/>
/// grid <see cref="TanningScrapeScorer.PatchesFor"/> returns for a given <see cref="PatchSeed"/> —
/// the tanning equivalent of the forge's shared target line — and captures nothing but an integer
/// per-cell pass count (<see cref="CellPasses"/>). The actual grade math (coverage, restraint,
/// ruin penalty, talent assists) lives sim-side in <see cref="TanningScrapeScorer"/> and never runs
/// here; <see cref="PreviewGradePermille"/>/<see cref="PreviewSubScores"/> call that SAME pure
/// scorer read-only for immediate UI feedback (mirrors both siblings' own preview), never a second
/// set of rules.</para>
///
/// <para><b>Single-action contract (PKD8, same as the forge/brew):</b> <see cref="Finished"/> fires
/// EXACTLY ONCE, on <see cref="Submit"/>, carrying one <see cref="CraftAction"/> whose
/// <see cref="CraftAction.Puzzle"/> is the captured <see cref="TanningScrapeInput"/>
/// (<see cref="CraftAction.PerformanceGrade"/> stays null — the cell-pass list is the single source
/// the sim scores); <see cref="Cancel"/> raises <see cref="Cancelled"/> instead and the caller
/// queues nothing.</para>
///
/// <para><b>Two distinct gestures, never conflated (KTD-A/KTD-B):</b> a press-drag that STARTS
/// inside the hide grid is the scrape stroke — raw motion accumulates and quantizes into discrete
/// <see cref="ScrapeCell"/> calls every <see cref="ScrapePixelThreshold"/> of travel, one call per
/// threshold crossed for whatever cell the pointer currently sits over (mirrors
/// <c>ForgeMinigame</c>'s bellows-pump-stroke quantization technique). A press-drag that STARTS on
/// the frame's release clip and is carried into the drop zone below the frame is the "take the
/// finished hide off the frame" commit gesture — a single discrete <see cref="Submit"/> call on
/// release, never a float. Arrow keys move a cell cursor and a key scrapes the focused cell
/// (keyboard parity for the scrape stroke); a dedicated key is the keyboard equivalent for lifting
/// the hide off the frame (keyboard parity for the commit gesture). No RNG, no wall-clock: the only
/// per-frame work is <see cref="Advance"/>'s purely cosmetic animation clock — deliberately the
/// opposite of the forge, this minigame has no timer or drift of any kind.</para>
/// </summary>
public sealed partial class TanningFrame : PanelContainer
{
    /// <summary>Hide grid width — mirrors <see cref="TanningScrapeScorer.Columns"/> exactly.</summary>
    public const int Columns = TanningScrapeScorer.Columns;

    /// <summary>Hide grid height — mirrors <see cref="TanningScrapeScorer.Rows"/> exactly.</summary>
    public const int Rows = TanningScrapeScorer.Rows;

    /// <summary>Total cells — mirrors <see cref="TanningScrapeScorer.CellCount"/> exactly.</summary>
    public const int CellCount = TanningScrapeScorer.CellCount;

    /// <summary>Downward-or-sideways drag pixels per quantized <see cref="ScrapeCell"/> call
    /// (KTD-B — no raw motion float ever reaches the input record, only these integer calls do).
    /// Mirrors <c>ForgeMinigame.PumpStrokeDragPixels</c>'s role exactly.</summary>
    public const float ScrapePixelThreshold = 14f;

    public string RecipeId { get; private set; } = string.Empty;
    public string MaterialKey { get; private set; } = string.Empty;

    /// <summary>The integer seed selecting this hide's patch layout — derived deterministically
    /// from the recipe id + day (<see cref="Configure"/>), never RNG, and carried verbatim on the
    /// emitted <see cref="TanningScrapeInput.PatchSeed"/> so the sim regenerates the IDENTICAL
    /// patches this overlay rendered.</summary>
    public int PatchSeed { get; private set; }

    /// <summary>The shared patch layout (<c>TanningScrapeScorer.PatchesFor</c>) this overlay
    /// renders — the SAME array the sim scorer regenerates from <see cref="PatchSeed"/>.</summary>
    public ImmutableArray<TanningScrapeScorer.CellKind> Patches { get; private set; } =
        ImmutableArray<TanningScrapeScorer.CellKind>.Empty;

    /// <summary>Scrape passes per cell so far, row-major over <see cref="CellCount"/> — the exact
    /// shape <see cref="TanningScrapeInput.CellPasses"/> carries.</summary>
    public ImmutableList<int> CellPasses { get; private set; } = ImmutableList<int>.Empty;

    /// <summary>The keyboard-focused cell (arrow keys move it; the scrape key scrapes it).</summary>
    public int CursorIndex { get; private set; }

    public bool Completed { get; private set; }
    public bool WasCancelled { get; private set; }

    /// <summary>The exact action <see cref="Finished"/> carried — test/inspection visibility.</summary>
    public CraftAction? EmittedAction { get; private set; }

    /// <summary>A read-only UI preview of the grade <c>TanningScrapeScorer</c> will compute for
    /// this exact cell-pass list (same pure scorer, called here only for immediate feedback) —
    /// NEVER written onto <see cref="CraftAction.PerformanceGrade"/>, which stays null.</summary>
    public int? PreviewGradePermille { get; private set; }

    /// <summary>The scorer's coverage/ruin/grade preview triple — rides <see cref="CraftAction.SubScores"/>
    /// as ledger flavor DATA (same role the forge/brew sub-scores play), never rules.</summary>
    public ImmutableList<int>? PreviewSubScores { get; private set; }

    /// <summary>Raised EXACTLY ONCE, on <see cref="Submit"/>, with the one action to queue.</summary>
    public event Action<CraftAction>? Finished;

    /// <summary>Raised on <see cref="Cancel"/> — the caller queues nothing.</summary>
    public event Action? Cancelled;

    private Recipe? _recipe;
    private ProfessionDefinition? _profession;
    private ImmutableSortedSet<string> _unlockedTalents = ImmutableSortedSet<string>.Empty;
    private List<int> _cellPasses = new();

    private Label _titleLabel = null!;
    private Label _readoutLabel = null!;
    private HideCanvas _canvas = null!;
    private Button _scrapeFocusedButton = null!;
    private Button _submitButton = null!;
    private Button _cancelButton = null!;
    private bool _built;

    public override void _Ready() => EnsureBuilt();

    public override void _Process(double delta) => Advance(delta);

    /// <summary>Bind a fresh run for this recipe/material/talent context and regenerate the shared
    /// patch layout from a seed derived (no RNG — <c>StableHash</c>, the same project-owned hash
    /// <c>ForgeMinigame</c> itself uses) from the recipe id + <paramref name="day"/>. Safe to call
    /// repeatedly (e.g. the player reopens for a different recipe) — always leaves a clean,
    /// un-completed run.</summary>
    public void Configure(
        Recipe recipe, string materialKey, ProfessionDefinition profession, ImmutableSortedSet<string> unlockedTalents, int day)
    {
        EnsureBuilt();

        RecipeId = recipe.RecipeId;
        MaterialKey = materialKey;
        _recipe = recipe;
        _profession = profession;
        _unlockedTalents = unlockedTalents;

        PatchSeed = unchecked((int)StableHash.Avalanche(StableHash.Mix(StableHash.HashString(recipe.RecipeId), unchecked((ulong)day))));
        Patches = TanningScrapeScorer.PatchesFor(PatchSeed);

        _cellPasses = new List<int>(new int[CellCount]);
        CellPasses = ImmutableList.CreateRange(_cellPasses);
        CursorIndex = 0;
        Completed = false;
        WasCancelled = false;
        EmittedAction = null;
        PreviewGradePermille = null;
        PreviewSubScores = null;
        _anim = 0;
        _canvas.ResetInteractionState();

        RepaintUi();
    }

    private double _anim;

    /// <summary>Advance the run by <paramref name="delta"/> accumulated-clock seconds — purely
    /// cosmetic (a subtle idle shimmer on unfinished cells). Deliberately does NOT drive any
    /// timer, drift, or scoring — tanning is the one minigame with no clock at all; this exists
    /// only so tests/the caller can drive the SAME accumulated-delta pattern every other overlay
    /// here uses, never a wall-clock read.</summary>
    public void Advance(double delta)
    {
        if (Completed || WasCancelled || delta <= 0)
        {
            return;
        }

        _anim += delta;
        RepaintUi();
    }

    /// <summary>Scrape one cell by exactly one pass (the ONE seam every scrape gesture — drag,
    /// keyboard, or a future dedicated button — terminates in). Out-of-range indices are ignored;
    /// no cap is applied to the count itself (an unbounded, honestly-scored input is the same
    /// total contract <see cref="TanningScrapeScorer"/> already promises — over-scraping just
    /// ruins that cell, it never throws or gets silently clamped here).</summary>
    public void ScrapeCell(int cellIndex)
    {
        if (Completed || WasCancelled || cellIndex < 0 || cellIndex >= CellCount)
        {
            return;
        }

        _cellPasses[cellIndex]++;
        CellPasses = ImmutableList.CreateRange(_cellPasses);
        RepaintUi();
    }

    /// <summary>Move the keyboard cursor by a whole-cell (dx, dy) delta, clamped to the grid (no
    /// wraparound) — the keyboard equivalent of pointing the mouse at a different cell.</summary>
    public void MoveCursor(int dx, int dy)
    {
        var col = Math.Clamp(CursorIndex % Columns + dx, 0, Columns - 1);
        var row = Math.Clamp(CursorIndex / Columns + dy, 0, Rows - 1);
        CursorIndex = row * Columns + col;
        RepaintUi();
    }

    /// <summary>Scrape the cursor-focused cell — the keyboard equivalent of a mouse scrape stroke
    /// landing on that same cell.</summary>
    public void ScrapeFocusedCell() => ScrapeCell(CursorIndex);

    /// <summary>Pure hit-test: which cell (if any) local point <paramref name="localPos"/> falls
    /// over — delegates to the canvas so the geometry <c>_Draw</c> paints and the geometry a
    /// drag is judged against can never drift apart. Public so a headless test can drive the
    /// recognizer's decision without any real mouse.</summary>
    public int? CellAt(Vector2 localPos) => _canvas.CellAt(localPos);

    /// <summary>A point (in the <c>HideCanvas</c>'s own local space — the same space a real
    /// <c>InputEventMouseButton.Position</c> carries when the canvas's <c>GuiInput</c> receives
    /// it) guaranteed to land inside cell <paramref name="index"/> — test/inspection convenience
    /// so a scripted drag can target a specific cell without duplicating the grid layout math.</summary>
    public Vector2 CellCenterFor(int index) => _canvas.CellCenterFor(index);

    /// <summary>Pure hit-test for the release-clip drag handle (the "pick up the hide" grab
    /// point) — same testability reasoning as <see cref="CellAt"/>.</summary>
    public bool IsOverReleaseClip(Vector2 localPos) => _canvas.IsOverReleaseClip(localPos);

    /// <summary>Pure hit-test for the off-frame drop zone (where a carried hide is released to
    /// commit) — same testability reasoning as <see cref="CellAt"/>.</summary>
    public bool IsOverDropZone(Vector2 localPos) => _canvas.IsOverDropZone(localPos);

    /// <summary>The release clip's own anchor, in this overlay's own local space — exposed
    /// read-only purely so a headless test can locate the drag-off gesture's start point without
    /// duplicating canvas-private layout math (mirrors <c>ForgeMinigame.BilletAnchor</c>).</summary>
    public Vector2 ReleaseClipAnchor => _canvas.ReleaseClipAnchor;

    /// <summary>A point guaranteed to fall inside <see cref="IsOverDropZone"/>'s hit region, in
    /// this overlay's own local space (mirrors <c>ForgeMinigame.QuenchZoneAnchor</c>).</summary>
    public Vector2 DropZoneAnchor => _canvas.DropZoneAnchor;

    /// <summary>Commit the hide: builds the ONE <see cref="TanningScrapeInput"/>/<see cref="CraftAction"/>
    /// (PKD8) and raises <see cref="Finished"/>. A partially-worked hide is legal — it simply
    /// scores what it scores when the sim resolves it (no clock, no forced completion).</summary>
    public void Submit()
    {
        if (Completed || WasCancelled || _recipe is null || _profession is null)
        {
            return;
        }

        Completed = true;
        var puzzle = new TanningScrapeInput(CellPasses, PatchSeed);

        // Read-only preview off the SAME pure sim scorer (mirrors ForgeMinigame/AlchemyBrewPuzzle's
        // own preview) — never written back as rules, purely for the readout text below.
        var preview = TanningScrapeScorer.Score(_recipe, puzzle, _unlockedTalents, _profession);
        PreviewGradePermille = preview.GradePermille;
        PreviewSubScores = ImmutableList.Create(preview.CoveragePermille, preview.RuinPermille, preview.GradePermille);

        var action = new CraftAction(RecipeId, MaterialKey, PerformanceGrade: null, Puzzle: puzzle, SubScores: PreviewSubScores);
        EmittedAction = action;
        RepaintUi();
        Finished?.Invoke(action);
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

    /// <summary>Escape cancels the scrape — routed through <see cref="Cancel"/> (shared mechanism,
    /// <see cref="ModalEscape"/>), never a bare hide (see <see cref="ForgeMinigame._Input"/>'s
    /// remarks for why). Overrides <c>_Input</c>, not <c>_GuiInput</c>: this overlay is nested DRAWER
    /// CONTENT (inside <c>ForgePanel</c>, itself inside <c>DrawerHost</c>'s slot), and Godot's
    /// reverse-tree-order <c>_Input</c> dispatch (children before parents) is what lets this fire and
    /// mark the event handled before <c>DrawerHost</c> ever sees it.</summary>
    public override void _Input(InputEvent @event) => ModalEscape.TryClose(@event, GetViewport(), Visible, Cancel);

    /// <summary>Real-time keyboard mapping — routes to the SAME public seam methods a scripted
    /// test or the button row drives (KTD-A, same idiom as <c>ForgeMinigame._GuiInput</c>): arrow
    /// keys move the cursor, Space scrapes the focused cell, and Enter is the keyboard equivalent
    /// of dragging the hide off the frame (submit) — a dedicated key so the commit gesture never
    /// depends on mouse precision.</summary>
    public override void _GuiInput(InputEvent @event)
    {
        if (Completed || WasCancelled)
        {
            return;
        }

        switch (@event)
        {
            case InputEventKey { Keycode: Key.Left, Pressed: true, Echo: false }:
                MoveCursor(-1, 0);
                break;
            case InputEventKey { Keycode: Key.Right, Pressed: true, Echo: false }:
                MoveCursor(1, 0);
                break;
            case InputEventKey { Keycode: Key.Up, Pressed: true, Echo: false }:
                MoveCursor(0, -1);
                break;
            case InputEventKey { Keycode: Key.Down, Pressed: true, Echo: false }:
                MoveCursor(0, 1);
                break;
            case InputEventKey { Keycode: Key.Space, Pressed: true, Echo: false }:
                ScrapeFocusedCell();
                break;
            case InputEventKey { Keycode: Key.Enter or Key.KpEnter, Pressed: true, Echo: false }:
                Submit();
                break;
        }
    }

    private void EnsureBuilt()
    {
        if (_built)
        {
            return;
        }

        Name = "TanningFrame";
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop; // an open overlay owns clicks — never passes through
        UiKit.ClaimKeyboard(this); // FocusMode alone never focuses anything — see its doc

        var body = new VBoxContainer { Name = "TanningFrameBody" };
        AddChild(body);

        _titleLabel = new Label { Name = "TanningFrameTitle" };
        _titleLabel.AddThemeColorOverride("font_color", GameTheme.HeaderColor);
        _titleLabel.ThemeTypeVariation = GameTheme.HeaderThemeType;
        body.AddChild(_titleLabel);

        _canvas = new HideCanvas { Name = "HideCanvas", CustomMinimumSize = new Vector2(0, 340), Size = new Vector2(600, 340) };
        _canvas.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _canvas.SizeFlagsVertical = SizeFlags.ExpandFill;
        _canvas.ScrapeRequested += ScrapeCell;   // KTD-A: the drag recognizer's ONE seam
        _canvas.DropRequested += Submit;         // KTD-A: the drag-off-frame recognizer's ONE seam
        body.AddChild(_canvas);

        _readoutLabel = new Label { Name = "TanningFrameReadout" };
        body.AddChild(_readoutLabel);

        var buttonRow = new HBoxContainer { Name = "TanningFrameButtons" };
        body.AddChild(buttonRow);

        _scrapeFocusedButton = new Button { Name = "ScrapeFocusedCell", Text = "Scrape (Space)" };
        _scrapeFocusedButton.Pressed += ScrapeFocusedCell;
        buttonRow.AddChild(_scrapeFocusedButton);

        _submitButton = new Button { Name = "TanningFrameSubmit", Text = "Take it off the frame" };
        _submitButton.Pressed += Submit;
        buttonRow.AddChild(_submitButton);

        _cancelButton = new Button { Name = "TanningFrameCancel", Text = "Cancel" };
        _cancelButton.Pressed += Cancel;
        buttonRow.AddChild(_cancelButton);

        // Control buttons must never hold the keyboard — a focused Button eats Space/Enter to
        // press itself, which stole the keys from this overlay after the first click.
        UiKit.MakeButtonsMouseOnly(this);

        _built = true;
        RepaintUi();
    }

    // Last-rendered state — so the per-frame RepaintUi (called from _Process→Advance every frame)
    // only rebuilds the readout label when something actually changed, instead of allocating an
    // interpolated string every single frame on the hot path (mirrors ForgeMinigame's own gate).
    private int _lastWorked = -1;
    private int _lastCursor = -1;
    private bool _lastCompleted;
    private bool _lastCancelled;

    /// <summary>Render-only — reads the current run state, writes no scoring state. Called after
    /// every state-changing call above AND every frame via <see cref="Advance"/> (for the canvas's
    /// idle shimmer).</summary>
    private void RepaintUi()
    {
        if (!_built)
        {
            return;
        }

        _canvas.Patches = Patches;
        _canvas.CellPasses = CellPasses;
        _canvas.CursorIndex = CursorIndex;
        _canvas.Completed = Completed;
        _canvas.Anim = _anim;
        _canvas.QueueRedraw();

        var worked = 0;
        foreach (var count in CellPasses)
        {
            if (count > 0)
            {
                worked++;
            }
        }

        if (worked == _lastWorked && CursorIndex == _lastCursor && Completed == _lastCompleted && WasCancelled == _lastCancelled)
        {
            return; // nothing the text/buttons show has changed — skip the string work
        }

        _lastWorked = worked;
        _lastCursor = CursorIndex;
        _lastCompleted = Completed;
        _lastCancelled = WasCancelled;

        _titleLabel.Text = $"Tanning Frame: {RecipeId}";
        _readoutLabel.Text = WasCancelled
            ? "Cancelled."
            : Completed
                ? $"Off the frame — grade {PreviewGradePermille}."
                : $"Worked {worked}/{CellCount} cells — cursor at cell {CursorIndex}.";

        _scrapeFocusedButton.Disabled = Completed || WasCancelled;
        _submitButton.Disabled = Completed || WasCancelled;
        _cancelButton.Disabled = Completed || WasCancelled;
    }

    /// <summary>
    /// The hide stretched on a wooden frame: 8x5 cells rendered so a flaw patch reads as darker
    /// and rougher (a cross-hatch of short strokes) and a thin patch reads as pale and translucent
    /// — the player learns the rules by looking, never by reading a legend. A cell's fill lightens
    /// toward "clean" as its passes approach its ideal band and tears into a ragged hole once
    /// scraped through. A release clip at the frame's bottom-right is the drag-off-frame commit
    /// handle; the drop zone is the tray beneath the frame. Plain <see cref="_Draw"/> primitives —
    /// NEVER a 3D <c>SubViewport</c> (a known gdUnit headless hang). All motion is
    /// accumulated-frame-delta only (fed via <see cref="Anim"/>) — no wall-clock, no RNG.
    /// </summary>
    private sealed partial class HideCanvas : Control
    {
        public ImmutableArray<TanningScrapeScorer.CellKind> Patches = ImmutableArray<TanningScrapeScorer.CellKind>.Empty;
        public ImmutableList<int> CellPasses = ImmutableList<int>.Empty;
        public int CursorIndex;
        public bool Completed;
        public double Anim;

        /// <summary>Fires with the cell index a quantized drag scrape lands on — the recognizer's
        /// ONE seam (KTD-A); the owner wires this straight to <c>ScrapeCell</c>.</summary>
        public event Action<int>? ScrapeRequested;

        /// <summary>Fires when the release-clip drag lands in the drop zone — the recognizer's ONE
        /// seam for the commit gesture; the owner wires this straight to <c>Submit</c>.</summary>
        public event Action? DropRequested;

        private static readonly Color HideBase = new(0.72f, 0.58f, 0.40f);
        private static readonly Color HideClean = new(0.88f, 0.78f, 0.60f);
        private static readonly Color FlawDark = new(0.30f, 0.20f, 0.14f);
        private static readonly Color ThinPale = new(0.90f, 0.88f, 0.82f);
        private static readonly Color HoleColor = new(0.05f, 0.04f, 0.05f);
        private static readonly Color GridLine = new(0.20f, 0.14f, 0.09f, 0.6f);
        private static readonly Color CursorRing = new(1.0f, 0.85f, 0.35f);
        private static readonly Color FrameWood = new(0.34f, 0.22f, 0.13f);
        private static readonly Color ClipMetal = new(0.55f, 0.55f, 0.60f);
        private static readonly Color DropTray = new(0.18f, 0.15f, 0.20f);

        // U2 gesture state — pure presentation plumbing, never crosses KTD2 (every gesture still
        // ends in ScrapeRequested/DropRequested → ScrapeCell/Submit).
        private bool _scrapeDragArmed;
        private Vector2 _scrapeLastPos;
        private double _scrapeAccumulatedPixels;
        private bool _clipDragArmed;
        private bool _carryingHide;
        private Vector2 _carryPos;

        public HideCanvas()
        {
            // Subscribed (not a `_GuiInput` override) so a headless test can drive the whole
            // recognizer via `EmitSignal(Control.SignalName.GuiInput, ...)` — the same seam
            // `AlchemyBrewPuzzle.BrewCanvas`/`UiTestSupport.Click` already rely on.
            GuiInput += OnGuiInput;
        }

        /// <summary>Clear any in-flight drag so a reopened frame (Configure on a reused panel)
        /// never carries a stale in-progress gesture from the previous hide into the new one.</summary>
        public void ResetInteractionState()
        {
            _scrapeDragArmed = false;
            _scrapeAccumulatedPixels = 0;
            _clipDragArmed = false;
            _carryingHide = false;
        }

        /// <summary>The frame's inner grid rect — the same rect <c>_Draw</c> paints the cells
        /// into, so a hit-test can never disagree with what's on screen.</summary>
        private static Rect2 GridRect(Vector2 size)
        {
            var margin = 18f;
            var w = Mathf.Max(0f, size.X - margin * 2f);
            var h = Mathf.Max(0f, size.Y * 0.82f - margin);
            return new Rect2(margin, margin, w, h);
        }

        private static Vector2 CellSize(Rect2 grid) => new(grid.Size.X / Columns, grid.Size.Y / Rows);

        /// <summary>Pure hit-test: which cell (if any) local point <paramref name="localPos"/>
        /// falls over.</summary>
        public int? CellAt(Vector2 localPos)
        {
            var grid = GridRect(Size);
            if (!grid.HasPoint(localPos))
            {
                return null;
            }

            var cellSize = CellSize(grid);
            if (cellSize.X <= 0f || cellSize.Y <= 0f)
            {
                return null;
            }

            var col = Math.Clamp((int)((localPos.X - grid.Position.X) / cellSize.X), 0, Columns - 1);
            var row = Math.Clamp((int)((localPos.Y - grid.Position.Y) / cellSize.Y), 0, Rows - 1);
            return row * Columns + col;
        }

        /// <summary>The center point of cell <paramref name="index"/>, in this canvas's own local
        /// space — the same rect math <see cref="CellAt"/> and <c>_Draw</c> both use, so it is
        /// always a point <see cref="CellAt"/> maps back to <paramref name="index"/>.</summary>
        public Vector2 CellCenterFor(int index)
        {
            var grid = GridRect(Size);
            var cellSize = CellSize(grid);
            var col = index % Columns;
            var row = index / Columns;
            return grid.Position + new Vector2(cellSize.X * (col + 0.5f), cellSize.Y * (row + 0.5f));
        }

        /// <summary>The release clip's rect — a small tab at the frame's bottom-right corner,
        /// clear of the grid.</summary>
        private static Rect2 ReleaseClipRect(Vector2 size) => new(size.X - 34f, size.Y * 0.82f + 4f, 26f, 22f);

        /// <summary>Pure hit-test for the release-clip drag handle.</summary>
        public bool IsOverReleaseClip(Vector2 localPos) => ReleaseClipRect(Size).HasPoint(localPos);

        public Vector2 ReleaseClipAnchor
        {
            get
            {
                var r = ReleaseClipRect(Size);
                return r.Position + r.Size / 2f;
            }
        }

        /// <summary>The drop zone: the tray beneath the frame's grid — releasing a carried hide
        /// anywhere in here commits it.</summary>
        private static Rect2 DropZoneRect(Vector2 size) => new(0f, size.Y * 0.86f, size.X, size.Y * 0.14f);

        /// <summary>Pure hit-test for the drop zone.</summary>
        public bool IsOverDropZone(Vector2 localPos) => DropZoneRect(Size).HasPoint(localPos);

        public Vector2 DropZoneAnchor
        {
            get
            {
                var r = DropZoneRect(Size);
                return r.Position + r.Size / 2f;
            }
        }

        /// <summary>The whole scrape-drag + drag-off-frame recognizer (KTD-A/KTD-B): a press that
        /// starts on the release clip arms the commit-carry gesture; any other press that starts
        /// inside the grid arms the scrape-drag, quantizing travel into <see cref="ScrapeRequested"/>
        /// calls every <c>TanningFrame.ScrapePixelThreshold</c> pixels. Nothing here computes a
        /// grade or touches sim state — every branch ends in one of the two owner seams.</summary>
        private void OnGuiInput(InputEvent @event)
        {
            switch (@event)
            {
                case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } down:
                    if (IsOverReleaseClip(down.Position))
                    {
                        _clipDragArmed = true;
                        _carryingHide = true;
                        _carryPos = down.Position;
                        QueueRedraw();
                    }
                    else if (CellAt(down.Position) is { } startCell)
                    {
                        _scrapeDragArmed = true;
                        _scrapeLastPos = down.Position;
                        _scrapeAccumulatedPixels = 0;
                        ScrapeRequested?.Invoke(startCell); // a plain click/tap scrapes on press, too
                    }

                    break;

                case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } up:
                    if (_clipDragArmed)
                    {
                        _clipDragArmed = false;
                        _carryingHide = false;
                        QueueRedraw();
                        if (IsOverDropZone(up.Position))
                        {
                            DropRequested?.Invoke(); // KTD-A: the commit gesture's ONE seam
                        }
                        // else: released outside the drop zone — re-clasped harmlessly, no submit.
                    }

                    _scrapeDragArmed = false;
                    _scrapeAccumulatedPixels = 0;
                    break;

                case InputEventMouseMotion motion:
                    if (_clipDragArmed)
                    {
                        _carryPos = motion.Position;
                        QueueRedraw();
                    }
                    else if (_scrapeDragArmed)
                    {
                        AccumulateScrapeDrag(motion.Position);
                    }

                    break;
            }
        }

        /// <summary>Quantizes free drag motion into discrete <see cref="ScrapeRequested"/> calls:
        /// every <c>TanningFrame.ScrapePixelThreshold</c> of travelled distance (any direction — a
        /// scrub, not a one-way pull) fires one scrape of whatever cell the pointer is over AT
        /// that moment. No raw float ever reaches a scorer — only the resulting integer calls do.</summary>
        private void AccumulateScrapeDrag(Vector2 newPos)
        {
            _scrapeAccumulatedPixels += _scrapeLastPos.DistanceTo(newPos);
            _scrapeLastPos = newPos;

            while (_scrapeAccumulatedPixels >= TanningFrame.ScrapePixelThreshold)
            {
                _scrapeAccumulatedPixels -= TanningFrame.ScrapePixelThreshold;
                if (CellAt(newPos) is { } cell)
                {
                    ScrapeRequested?.Invoke(cell);
                }
            }
        }

        public override void _Draw()
        {
            var size = Size;
            if (size.X <= 0 || size.Y <= 0 || Patches.Length != CellCount)
            {
                return;
            }

            DrawFrame(size);

            var grid = GridRect(size);
            var cellSize = CellSize(grid);
            for (var i = 0; i < CellCount; i++)
            {
                var col = i % Columns;
                var row = i / Columns;
                var rect = new Rect2(
                    grid.Position.X + col * cellSize.X, grid.Position.Y + row * cellSize.Y,
                    cellSize.X, cellSize.Y);
                DrawCell(rect, i);
            }

            DrawGridLines(grid, cellSize);
            DrawCursor(grid, cellSize);
            DrawReleaseClip(size);
            DrawDropZone(size);
            DrawCarriedHide(size);
        }

        private void DrawFrame(Vector2 size)
        {
            var grid = GridRect(size);
            DrawRect(new Rect2(grid.Position - new Vector2(10, 10), grid.Size + new Vector2(20, 20)), FrameWood);
        }

        /// <summary>One cell's fill: a plain cell lightens toward clean leather as it approaches
        /// its ideal band; a flaw patch stays dark with a cross-hatch of short rough strokes until
        /// worked; a thin patch renders pale and translucent throughout (delicate, never truly
        /// "clean" the way a plain cell is). Any cell scraped through its tolerance tears into a
        /// ragged dark hole regardless of kind — the shared "you went too far" tell.</summary>
        private void DrawCell(Rect2 rect, int index)
        {
            var kind = Patches[index];
            var count = index < CellPasses.Count ? CellPasses[index] : 0;
            var (min, max) = TanningScrapeScorer.IdealPassesFor(kind);
            var ruined = count > max;

            if (ruined)
            {
                DrawRect(rect, HideBase.Lerp(HoleColor, 0.85f));
                DrawHoleTear(rect);
                return;
            }

            var progress = min > 0 ? Mathf.Clamp(count / (float)min, 0f, 1f) : 0f;
            switch (kind)
            {
                case TanningScrapeScorer.CellKind.Flaw:
                    DrawRect(rect, FlawDark.Lerp(HideClean, progress * 0.7f));
                    DrawRoughHatch(rect, 1f - progress);
                    break;
                case TanningScrapeScorer.CellKind.Thin:
                    DrawRect(rect, new Color(ThinPale, 0.55f + 0.15f * progress));
                    break;
                default:
                    DrawRect(rect, HideBase.Lerp(HideClean, progress));
                    break;
            }

            if (count > 0 && !ruined)
            {
                // A faint shimmer while idle, tied to Anim only — never a timer or scoring signal.
                var pulse = 0.08f + 0.05f * (float)(0.5 + 0.5 * Mathf.Sin(Anim * 1.5 + index));
                DrawRect(rect, new Color(1f, 1f, 1f, pulse * progress), filled: false, width: 1f);
            }
        }

        private void DrawHoleTear(Rect2 rect)
        {
            var c = rect.Position + rect.Size / 2f;
            var r = Mathf.Min(rect.Size.X, rect.Size.Y) * 0.32f;
            var pts = new[]
            {
                c + new Vector2(-r, -r * 0.6f), c + new Vector2(-r * 0.3f, -r), c + new Vector2(r * 0.4f, -r * 0.7f),
                c + new Vector2(r, -r * 0.1f), c + new Vector2(r * 0.6f, r * 0.8f), c + new Vector2(0, r),
                c + new Vector2(-r * 0.7f, r * 0.6f),
            };
            DrawColoredPolygon(pts, HoleColor);
        }

        private static readonly (float Fx, float Fy, float Ang)[] HatchMarks =
        {
            (0.2f, 0.3f, 25f), (0.6f, 0.2f, -20f), (0.35f, 0.65f, 15f), (0.75f, 0.6f, -30f), (0.5f, 0.45f, 5f),
        };

        private void DrawRoughHatch(Rect2 rect, float strength)
        {
            if (strength <= 0.02f)
            {
                return;
            }

            foreach (var (fx, fy, ang) in HatchMarks)
            {
                var center = rect.Position + new Vector2(rect.Size.X * fx, rect.Size.Y * fy);
                var dir = Vector2.Right.Rotated(Mathf.DegToRad(ang)) * Mathf.Min(rect.Size.X, rect.Size.Y) * 0.22f;
                DrawLine(center - dir, center + dir, new Color(FlawDark, 0.6f * strength), 1.5f);
            }
        }

        private void DrawGridLines(Rect2 grid, Vector2 cellSize)
        {
            for (var c = 0; c <= Columns; c++)
            {
                var x = grid.Position.X + c * cellSize.X;
                DrawLine(new Vector2(x, grid.Position.Y), new Vector2(x, grid.Position.Y + grid.Size.Y), GridLine, 1f);
            }

            for (var r = 0; r <= Rows; r++)
            {
                var y = grid.Position.Y + r * cellSize.Y;
                DrawLine(new Vector2(grid.Position.X, y), new Vector2(grid.Position.X + grid.Size.X, y), GridLine, 1f);
            }
        }

        private void DrawCursor(Rect2 grid, Vector2 cellSize)
        {
            var col = CursorIndex % Columns;
            var row = CursorIndex / Columns;
            var rect = new Rect2(grid.Position.X + col * cellSize.X, grid.Position.Y + row * cellSize.Y, cellSize.X, cellSize.Y);
            DrawRect(rect.Grow(-1f), CursorRing, filled: false, width: 2.5f);
        }

        private void DrawReleaseClip(Vector2 size)
        {
            var rect = ReleaseClipRect(size);
            DrawRect(rect, ClipMetal);
            DrawRect(rect, new Color(ClipMetal, 0.9f).Darkened(0.3f), filled: false, width: 1.5f);
        }

        private void DrawDropZone(Vector2 size)
        {
            var rect = DropZoneRect(size);
            var highlight = Completed ? DropTray.Lightened(0.2f) : DropTray;
            DrawRect(rect, highlight);
        }

        private void DrawCarriedHide(Vector2 size)
        {
            if (!_carryingHide)
            {
                return;
            }

            var overDrop = IsOverDropZone(_carryPos);
            var tint = overDrop ? HideClean : HideBase;
            DrawRect(new Rect2(_carryPos - new Vector2(14, 10), new Vector2(28, 20)), tint);
            if (overDrop)
            {
                DrawArc(_carryPos, 20f, 0f, Mathf.Tau, 24, new Color(HideClean, 0.7f), 2f);
            }
        }
    }
}
