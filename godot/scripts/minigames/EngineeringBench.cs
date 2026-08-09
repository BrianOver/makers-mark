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
/// U3 (plan <c>2026-07-28-004</c>): the engineer's assembly bench — the deliberate ANTI-FORGE.
/// Where <see cref="ForgeMinigame"/> is a real-time tempo chase, this overlay has NO clock anywhere:
/// no timer, no drift, no tempo window. The skill tested is purely SPATIAL PLANNING AND PART
/// IDENTIFICATION — read the schematic, tell a near-identical part apart from its twin, and seat
/// everything in an order the mechanism permits. A player is free to stop and think for as long as
/// they like; that is the point of this craft existing beside the forge, not an omission.
///
/// <para><b>Adapter-only (KTD2):</b> this class renders <see cref="EngineeringAssemblyScorer.SchematicFor"/>
/// verbatim — it never reinvents the socket count or the wanted-part derivation — and only ever
/// emits one flattened placement list on <see cref="CraftAction.Puzzle"/> (an
/// <see cref="EngineeringAssemblyInput"/>). The actual grade math (exact/misplaced credit, the
/// order bonus) lives sim-side in <see cref="EngineeringAssemblyScorer"/> and never runs here;
/// <see cref="PreviewGradePermille"/> calls that SAME pure scorer read-only for immediate feedback,
/// mirroring <see cref="AlchemyBrewPuzzle"/>'s own preview — never a second set of rules.</para>
///
/// <para><b>Reseat-is-free, honoured in the model (not just the visuals):</b> the scorer only
/// honours the FIRST entry it sees per socket in the flattened list, so a naive "append every seat
/// event" recorder would let a WRONG first attempt win even after the player corrected it — exactly
/// the punishment the contract says must not happen. Instead this class tracks, per socket, the
/// order it was FIRST touched (<see cref="_fillOrder"/>, append-only, immutable once set) separately
/// from its CURRENT occupant (<see cref="_seatedPart"/>, freely overwritten by <see cref="Place"/>
/// and cleared by <see cref="RemoveFromSocket"/>). <see cref="BuildPlacements"/> emits exactly ONE
/// pair per currently-occupied socket, in first-touch order, using the CURRENT part — so "seat wrong,
/// pull out, reseat right" produces the byte-identical flattened list "seat right the first time"
/// would have, and a pulled-and-never-refilled socket contributes nothing at all. The socket ring
/// (<see cref="BenchCanvas.DrawSocket"/>) reflects only the LIVE occupant's correctness, so a
/// corrected socket simply shows green with no lingering red mark — the freedom is legible, not just
/// scored fairly.</para>
///
/// <para><b>Near-duplicate parts (the identification skill):</b> <see cref="EngineeringAssemblyScorer.PartCount"/>
/// is 6, grouped into 3 shape FAMILIES of 2 near-identical variants each — Gear (fine/coarse tooth
/// count), Spring (tight/loose coil count), Plate (beveled/flat) — see <see cref="Parts"/>. A
/// socket's housing renders the wanted FAMILY's silhouette at a neutral, non-matching detail level
/// (<see cref="BenchCanvas.DrawFamilyHint"/>) so the player can narrow "this wants a gear" without
/// ever being told fine vs. coarse — that discrimination is the actual skill, discoverable by
/// comparing the two tray variants side by side, never free.</para>
///
/// <para><b>The crank finale (no Submit button):</b> <see cref="CrankStroke"/> is the one physical
/// commit gesture, reachable by a right-drag on the crank hub (quantized into discrete strokes,
/// mirroring <see cref="ForgeMinigame.PumpStroke"/>'s drag-to-quantum idiom — no raw pixel delta
/// ever reaches scoring) or by repeated Space presses (KTD-C keyboard parity). The Nth stroke that
/// reaches full winds calls <see cref="Finish"/> itself — there is no separate submit seam.</para>
///
/// <para><b>Single-action contract (PKD8, same as every sibling minigame):</b> <see cref="Finished"/>
/// fires EXACTLY ONCE, on the crank's final stroke, carrying one <see cref="CraftAction"/> whose
/// <see cref="CraftAction.Puzzle"/> is the captured <see cref="EngineeringAssemblyInput"/>
/// (<see cref="CraftAction.PerformanceGrade"/> stays null — the sim scores it); <see cref="Cancel"/>
/// raises <see cref="Cancelled"/> instead and the caller queues nothing. A partial assembly is legal
/// — it simply scores what it scores, the same "no gate on submission" idiom
/// <see cref="AlchemyBrewPuzzle.Submit"/> already uses.</para>
///
/// <para><b>Ships DORMANT (by design):</b> <see cref="GameSim.Professions.ProfessionDefinition.ActiveCraft"/>
/// is false for engineering today, so <see cref="Panels.ForgePanel"/> never renders the button that
/// opens this overlay — nothing here is reachable in the live game yet. The orchestrator flips the
/// flag alongside the talent remap and a balance-gate re-run; this class and its wiring are simply
/// staged ahead of that landing.</para>
/// </summary>
public sealed partial class EngineeringBench : PanelContainer
{
    /// <summary>How many full crank strokes wind the mechanism shut — divides 1000 evenly so the
    /// final stroke lands on exactly 1000, never an off-by-rounding near-miss.</summary>
    public const int CrankStrokesRequired = 5;

    public string RecipeId { get; private set; } = string.Empty;
    public string MaterialKey { get; private set; } = string.Empty;

    /// <summary>Socket count for the current recipe — <see cref="EngineeringAssemblyScorer.SocketCountFor"/>,
    /// called verbatim, never re-derived.</summary>
    public int SocketCount { get; private set; }

    /// <summary>The schematic this overlay renders — <see cref="EngineeringAssemblyScorer.SchematicFor"/>,
    /// called verbatim so the overlay and the scorer can never disagree on which part a socket wants.</summary>
    public ImmutableList<int> Schematic { get; private set; } = ImmutableList<int>.Empty;

    /// <summary>Keyboard-cycled socket cursor (Left/Right) — independent of mouse drag-drop, which
    /// seats a specific part into a specific socket directly with no cursor involved (KTD-A: two
    /// routes to the same <see cref="Place"/> seam).</summary>
    public int SelectedSocketId { get; private set; }

    /// <summary>Keyboard-cycled part cursor (Up/Down) — see <see cref="SelectedSocketId"/>.</summary>
    public int SelectedPartId { get; private set; }

    /// <summary>How far the crank has wound, per-mille [0..1000]. Reaching 1000 finishes the run.</summary>
    public int CrankProgressPermille { get; private set; }

    public bool Completed { get; private set; }
    public bool WasCancelled { get; private set; }

    /// <summary>The exact action <see cref="Finished"/> carried — test/inspection visibility.</summary>
    public CraftAction? EmittedAction { get; private set; }

    /// <summary>Read-only UI preview of the grade <see cref="EngineeringAssemblyScorer"/> will
    /// compute for the CURRENT seating (same pure scorer, called here only for feedback) — NEVER
    /// written onto <see cref="CraftAction.PerformanceGrade"/>, which stays null.</summary>
    public int? PreviewGradePermille { get; private set; }

    /// <summary>Raised EXACTLY ONCE, when the crank finishes winding, with the one action to queue.</summary>
    public event Action<CraftAction>? Finished;

    /// <summary>Raised on <see cref="Cancel"/> — the caller queues nothing.</summary>
    public event Action? Cancelled;

    /// <summary>Every socket's CURRENT occupant — absent key means empty. Public read for rendering
    /// and for tests asserting the live seating without reaching into private state.</summary>
    public IReadOnlyDictionary<int, int> Seated => _seatedPart;

    // Reseat-is-free bookkeeping (see the class doc's dedicated paragraph): _seatedPart is freely
    // overwritten/cleared; _fillOrder is append-only and records each socket's FIRST touch, never its
    // most recent one. _touchedSockets is the append-only gate deciding whether a Place() call is
    // that first touch — it must be SEPARATE from "is this socket currently in _seatedPart", because
    // RemoveFromSocket deletes the socket's live entry (so a later reseat wouldn't otherwise look
    // "already touched" and would wrongly re-append to _fillOrder, corrupting BuildPlacements with a
    // duplicate pair). BuildPlacements folds _fillOrder + _seatedPart back into the scorer's shape.
    private readonly Dictionary<int, int> _seatedPart = new();
    private readonly List<int> _fillOrder = new();
    private readonly HashSet<int> _touchedSockets = new();

    /// <summary>The socket's on-canvas center, in the SAME local coordinate space
    /// <see cref="BenchCanvas"/>'s own drawing and hit-testing use — exposed so a headless test can
    /// synthesize a <c>GuiInput</c> position without duplicating the private layout math (mirrors
    /// <see cref="ForgeMinigame.BilletAnchor"/>'s own test-support rationale). Meaningful even before
    /// any live container layout has run, since <see cref="EnsureBuilt"/> seeds the canvas's real
    /// drawer footprint up front.</summary>
    public Vector2 SocketAnchor(int socketId) => Center(BenchCanvas.SocketRect(_canvas.Size, socketId, SocketCount));

    /// <summary>The tray swatch's on-canvas center — same rationale as <see cref="SocketAnchor"/>.</summary>
    public Vector2 TrayAnchor(int partId) => Center(BenchCanvas.TrayRect(_canvas.Size, partId));

    /// <summary>The crank hub's on-canvas center — same rationale as <see cref="SocketAnchor"/>.</summary>
    public Vector2 CrankAnchor => Center(BenchCanvas.CrankRect(_canvas.Size));

    private static Vector2 Center(Rect2 r) => r.Position + r.Size / 2f;

    private Recipe? _recipe;
    private ProfessionDefinition? _profession;
    private ImmutableSortedSet<string> _unlockedTalents = ImmutableSortedSet<string>.Empty;
    private double _animElapsed;

    private Label _titleLabel = null!;
    private Label _readoutLabel = null!;
    private BenchCanvas _canvas = null!;
    private Button _seatButton = null!;
    private Button _pullButton = null!;
    private Button _crankButton = null!;
    private Button _cancelButton = null!;
    private bool _built;

    /// <summary>One evocative color + a shape family + a "detail" number per part (indices match
    /// <see cref="EngineeringAssemblyScorer.PartCount"/>): family 0 = Gear (teeth count), family 1 =
    /// Spring (coil count), family 2 = Plate (beveled flag, 1/0). Each family holds exactly ONE
    /// near-duplicate pair — same silhouette, different detail — which is what makes telling them
    /// apart the actual skill instead of the socket shape alone.</summary>
    private static readonly PartSpec[] Parts =
    {
        new("Fine Gear",     0, 10, new Color(0.95f, 0.72f, 0.28f)),
        new("Coarse Gear",   0, 5,  new Color(0.72f, 0.46f, 0.12f)),
        new("Tight Spring",  1, 7,  new Color(0.42f, 0.82f, 0.74f)),
        new("Loose Spring",  1, 3,  new Color(0.20f, 0.55f, 0.50f)),
        new("Beveled Plate", 2, 1,  new Color(0.72f, 0.56f, 0.86f)),
        new("Flat Plate",    2, 0,  new Color(0.46f, 0.36f, 0.62f)),
    };

    private readonly record struct PartSpec(string Name, int Family, int Detail, Color Color);

    public override void _Ready() => EnsureBuilt();

    public override void _Process(double delta) => Advance(delta);

    /// <summary>Accumulated-clock cosmetic animation ONLY (crank idle shimmer, socket glow pulse) —
    /// never gates or scores anything, and there is no wall-clock/RNG anywhere near it. Public so a
    /// headless test can drive frames deterministically without a real engine tick, exactly like
    /// every sibling minigame's <c>Advance</c>.</summary>
    public void Advance(double delta)
    {
        if (delta <= 0)
        {
            return;
        }

        _animElapsed += delta;
        if (_built)
        {
            _canvas.AnimClock = (float)_animElapsed;
            _canvas.QueueRedraw();
        }
    }

    /// <summary>Bind a fresh run for this recipe/material/talent context. Safe to call repeatedly —
    /// always leaves a clean, un-completed run with an empty bench.</summary>
    public void Configure(
        Recipe recipe, string materialKey, ProfessionDefinition profession, ImmutableSortedSet<string> unlockedTalents)
    {
        EnsureBuilt();

        _recipe = recipe;
        _profession = profession;
        _unlockedTalents = unlockedTalents;

        RecipeId = recipe.RecipeId;
        MaterialKey = materialKey;
        SocketCount = EngineeringAssemblyScorer.SocketCountFor(recipe);
        Schematic = EngineeringAssemblyScorer.SchematicFor(recipe);

        _seatedPart.Clear();
        _fillOrder.Clear();
        _touchedSockets.Clear();
        SelectedSocketId = 0;
        SelectedPartId = 0;
        CrankProgressPermille = 0;
        Completed = false;
        WasCancelled = false;
        EmittedAction = null;
        PreviewGradePermille = null;
        _canvas.ResetInteractionState();

        RepaintUi();
    }

    /// <summary>Seat <paramref name="partId"/> into <paramref name="socketId"/> — the ONE seam every
    /// input route (drag-drop, keyboard <see cref="SeatSelected"/>) terminates in. Overwriting an
    /// already-occupied socket is a free reseat (see the class doc): the socket's first-touch order
    /// is preserved even though its occupant just changed. Out-of-range ids and calls after
    /// completion/cancel are silently ignored, never a throw.</summary>
    public void Place(int socketId, int partId)
    {
        if (Completed || WasCancelled || socketId < 0 || socketId >= SocketCount
            || partId < 0 || partId >= EngineeringAssemblyScorer.PartCount)
        {
            return;
        }

        if (_touchedSockets.Add(socketId))
        {
            _fillOrder.Add(socketId);
        }

        _seatedPart[socketId] = partId;
        RepaintUi();
    }

    /// <summary>Pull whatever is seated in <paramref name="socketId"/> back out, free — the socket's
    /// first-touch order stays banked (see the class doc) so a later reseat still lands in its
    /// original sequence position; leaving it empty at submit simply contributes no pair at all.</summary>
    public void RemoveFromSocket(int socketId)
    {
        if (Completed || WasCancelled || socketId < 0 || socketId >= SocketCount)
        {
            return;
        }

        _seatedPart.Remove(socketId);
        RepaintUi();
    }

    /// <summary>Move the keyboard socket cursor by <paramref name="direction"/> (±1), wrapping.</summary>
    public void CycleSelectedSocket(int direction)
    {
        if (SocketCount <= 0)
        {
            return;
        }

        SelectedSocketId = Wrap(SelectedSocketId + direction, SocketCount);
        RepaintUi();
    }

    /// <summary>Move the keyboard part cursor by <paramref name="direction"/> (±1), wrapping.</summary>
    public void CycleSelectedPart(int direction)
    {
        SelectedPartId = Wrap(SelectedPartId + direction, EngineeringAssemblyScorer.PartCount);
        RepaintUi();
    }

    private static int Wrap(int value, int modulus) => ((value % modulus) + modulus) % modulus;

    /// <summary>Keyboard equivalent of dropping the cursor-selected tray part onto the cursor-selected
    /// socket — routes straight through <see cref="Place"/>, the same seam a mouse drag calls.</summary>
    public void SeatSelected() => Place(SelectedSocketId, SelectedPartId);

    /// <summary>Keyboard equivalent of dragging the cursor-selected socket's part back out.</summary>
    public void PullSelected() => RemoveFromSocket(SelectedSocketId);

    /// <summary>One physical turn of the crank — the discrete seam every finale gesture (right-drag
    /// quantization, repeated Space) terminates in. The stroke that reaches 1000 finishes the run;
    /// no separate Submit exists.</summary>
    public void CrankStroke()
    {
        if (Completed || WasCancelled)
        {
            return;
        }

        CrankProgressPermille = Math.Min(1000, CrankProgressPermille + 1000 / CrankStrokesRequired);
        RepaintUi();

        if (CrankProgressPermille >= 1000)
        {
            Finish();
        }
    }

    /// <summary>Abandon the assembly — queues nothing (<see cref="Cancelled"/> only).</summary>
    public void Cancel()
    {
        if (Completed || WasCancelled)
        {
            return;
        }

        WasCancelled = true;
        RepaintUi();
        Cancelled?.Invoke();
    }

    /// <summary>Escape cancels the assembly — routed through <see cref="Cancel"/> (shared mechanism,
    /// <see cref="ModalEscape"/>), never a bare hide (see <see cref="ForgeMinigame._Input"/>'s
    /// remarks for why). Overrides <c>_Input</c> on THIS PanelContainer regardless of which child
    /// (<c>BenchCanvas</c>, see <see cref="EnsureBuilt"/>'s note) actually holds keyboard GUI focus —
    /// <c>_Input</c> is a per-frame Node notification, not gated by <c>Control</c> focus. This overlay
    /// is nested DRAWER CONTENT (inside <c>ForgePanel</c>, itself inside <c>DrawerHost</c>'s slot),
    /// and Godot's reverse-tree-order <c>_Input</c> dispatch (children before parents) is what lets
    /// this fire and mark the event handled before <c>DrawerHost</c> ever sees it.</summary>
    public override void _Input(InputEvent @event) => ModalEscape.TryClose(@event, GetViewport(), Visible, Cancel);

    /// <summary>
    /// Folds <see cref="_seatedPart"/>/<see cref="_fillOrder"/> into the scorer's flattened shape:
    /// exactly one (socketId, partId) pair per CURRENTLY-occupied socket, in first-touch order. A
    /// socket that was touched and later emptied (pulled out, never refilled) contributes nothing —
    /// which is correct, since nothing is seated there at submit time.
    /// </summary>
    private ImmutableList<int> BuildPlacements()
    {
        var builder = ImmutableList.CreateBuilder<int>();
        foreach (var socket in _fillOrder)
        {
            if (_seatedPart.TryGetValue(socket, out var part))
            {
                builder.Add(socket);
                builder.Add(part);
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>Commit the assembly: builds the ONE <see cref="CraftAction"/> (PKD8) with the puzzle
    /// payload and the scorer's preview grade, and raises <see cref="Finished"/>.</summary>
    private void Finish()
    {
        if (Completed || WasCancelled || _recipe is null || _profession is null)
        {
            return;
        }

        Completed = true;
        var puzzle = new EngineeringAssemblyInput(BuildPlacements());
        var preview = EngineeringAssemblyScorer.Score(_recipe, puzzle, _unlockedTalents, _profession);
        PreviewGradePermille = preview.GradePermille;
        var action = new CraftAction(
            RecipeId, MaterialKey, PerformanceGrade: null, Puzzle: puzzle,
            SubScores: ImmutableList.Create(preview.ExactPermille, preview.OrderPermille, preview.GradePermille));
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

        MinigameInput.RegisterActions(); // C2: move_*/confirm/pull_part/crank_stroke must exist first

        Name = "EngineeringBench";
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop; // an open overlay owns clicks — never passes through
        // NB: focus goes on the CANVAS below, not on this panel. Unlike ForgeMinigame/TanningFrame
        // (which override _GuiInput on the overlay itself), this bench's key handler lives on
        // BenchCanvas — so focusing the overlay would leave the keys just as dead as no focus at all.

        var body = new VBoxContainer { Name = "EngineeringBenchBody" };
        AddChild(body);

        _titleLabel = new Label { Name = "EngineeringBenchTitle" };
        _titleLabel.AddThemeColorOverride("font_color", GameTheme.HeaderColor);
        _titleLabel.ThemeTypeVariation = GameTheme.HeaderThemeType;
        body.AddChild(_titleLabel);

        var notes = new Label
        {
            Name = "EngineeringBenchNotes", AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Text = "No clock here — take your time. Seat the tray part that matches each socket's " +
                   "hinted shape; pulling a part back out and reseating it before you wind the crank costs nothing.",
        };
        body.AddChild(notes);

        // Fills the drawer's real footprint (matches DrawerHost.DrawerWidth) so hit-tests are
        // meaningful even on an unmounted node with no live container layout pass yet.
        _canvas = new BenchCanvas { Name = "BenchCanvas", CustomMinimumSize = new Vector2(0, 340), Size = new Vector2(600, 340) };
        _canvas.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _canvas.SizeFlagsVertical = SizeFlags.ExpandFill;
        UiKit.ClaimKeyboard(_canvas); // the canvas owns OnGuiInput's key cases, so the canvas needs focus
        _canvas.PlaceRequested += Place;
        _canvas.PullRequested += RemoveFromSocket;
        _canvas.SocketCycleRequested += CycleSelectedSocket;
        _canvas.PartCycleRequested += CycleSelectedPart;
        _canvas.SeatRequested += SeatSelected;
        _canvas.PullSelectedRequested += PullSelected;
        _canvas.CrankStrokeRequested += CrankStroke;
        body.AddChild(_canvas);

        _readoutLabel = new Label { Name = "EngineeringBenchReadout" };
        body.AddChild(_readoutLabel);

        var buttonRow = new HBoxContainer { Name = "EngineeringBenchButtons" };
        body.AddChild(buttonRow);

        // C2: every label below reads the LIVE InputMap binding instead of a hardcoded key name.
        _seatButton = new Button { Name = "EngineeringBenchSeat", Text = $"Seat ({MinigameInput.KeyLabelFor("confirm")})" };
        _seatButton.Pressed += SeatSelected;
        buttonRow.AddChild(_seatButton);

        _pullButton = new Button { Name = "EngineeringBenchPull", Text = $"Remove ({MinigameInput.KeyLabelFor("pull_part")})" };
        _pullButton.Pressed += PullSelected;
        buttonRow.AddChild(_pullButton);

        _crankButton = new Button { Name = "EngineeringBenchCrank", Text = $"Turn Crank ({MinigameInput.KeyLabelFor("crank_stroke")})" };
        _crankButton.Pressed += CrankStroke;
        buttonRow.AddChild(_crankButton);

        _cancelButton = new Button { Name = "EngineeringBenchCancel", Text = "Cancel" };
        _cancelButton.Pressed += Cancel;
        buttonRow.AddChild(_cancelButton);

        // Control buttons must never hold the keyboard — a focused Button eats Space/Enter to
        // press itself, which stole the keys from this overlay after the first click.
        UiKit.MakeButtonsMouseOnly(this);

        _built = true;
        RepaintUi();
    }

    /// <summary>Render-only — reads state, writes none. Called after every state-changing call above.</summary>
    private void RepaintUi()
    {
        if (!_built)
        {
            return;
        }

        _titleLabel.Text = $"Assembly Bench: {RecipeId}";
        var filled = _seatedPart.Count;
        _readoutLabel.Text = WasCancelled
            ? "Cancelled."
            : Completed
                ? $"Assembled! (preview grade {PreviewGradePermille}‰)"
                : $"Sockets filled: {filled}/{SocketCount} — Crank wound: {CrankProgressPermille / 10}% " +
                  $"— cursor: socket {SelectedSocketId}, part '{Parts[SelectedPartId].Name}'";

        _canvas.SocketCount = SocketCount;
        _canvas.Schematic = Schematic;
        _canvas.Seated = _seatedPart;
        _canvas.SelectedSocketId = SelectedSocketId;
        _canvas.SelectedPartId = SelectedPartId;
        _canvas.CrankProgressPermille = CrankProgressPermille;
        _canvas.Completed = Completed;
        _canvas.QueueRedraw();

        var done = Completed || WasCancelled;
        _seatButton.Disabled = done;
        _pullButton.Disabled = done;
        _crankButton.Disabled = done;
        _cancelButton.Disabled = done;
    }

    /// <summary>
    /// The bench surface: a schematic row of socket housings (each hinting its wanted PART FAMILY's
    /// silhouette at a neutral, non-matching detail level — discoverable, never a free answer), a
    /// tray of the 6 part kinds below (three near-duplicate pairs), and a crank wheel that winds shut
    /// on repeated strokes. Plain <see cref="_Draw"/> primitives only — never a 3D <c>SubViewport</c>
    /// (a known gdUnit headless hang). All motion is accumulated <see cref="AnimClock"/> only, purely
    /// cosmetic (idle crank shimmer, socket glow) — it never gates or scores anything, matching the
    /// "no clock" design.
    ///
    /// <para>Input is recognised entirely via the <c>GuiInput</c> C# event (subscribed, not a
    /// <c>_GuiInput</c> override — the same idiom <see cref="AlchemyBrewPuzzle.BrewCanvas"/> uses,
    /// precisely so a headless test can drive it with <c>EmitSignal(Control.SignalName.GuiInput, ...)</c>
    /// exactly like <c>UiTestSupport.Click</c> does elsewhere): a left-press on a tray swatch OR an
    /// occupied socket picks up that part; release over a DIFFERENT socket seats it there
    /// (<see cref="PlaceRequested"/>); release over empty space returns a picked-up SOCKET part to the
    /// tray (<see cref="PullRequested"/>) while a picked-up TRAY part is simply shelved harmlessly.
    /// A right-drag on the crank hub quantizes into discrete <see cref="CrankStrokeRequested"/> calls
    /// (mirrors <see cref="ForgeMinigame.PumpStroke"/>'s drag-to-quantum idiom — no raw pixel delta
    /// ever reaches scoring); Left/Right/Up/Down/Enter/Backspace/Space give full keyboard parity to
    /// the exact same seams.</para>
    /// </summary>
    private sealed partial class BenchCanvas : Control
    {
        public int SocketCount;
        public ImmutableList<int> Schematic = ImmutableList<int>.Empty;
        public IReadOnlyDictionary<int, int> Seated = new Dictionary<int, int>();
        public int SelectedSocketId;
        public int SelectedPartId;
        public int CrankProgressPermille;
        public bool Completed;
        public float AnimClock;

        /// <summary>Fires when a drag release lands on a socket DIFFERENT from where the carried
        /// part came from — the owner wires this straight to <see cref="Place"/>.</summary>
        public event Action<int, int>? PlaceRequested;

        /// <summary>Fires when a part picked up FROM a socket is released off any socket — the
        /// owner wires this straight to <see cref="RemoveFromSocket"/>.</summary>
        public event Action<int>? PullRequested;

        public event Action<int>? SocketCycleRequested;
        public event Action<int>? PartCycleRequested;
        public event Action? SeatRequested;
        public event Action? PullSelectedRequested;
        public event Action? CrankStrokeRequested;

        private static readonly Color BenchBg = new(0.16f, 0.15f, 0.20f);
        private static readonly Color HousingBg = new(0.10f, 0.09f, 0.13f, 0.9f);
        private static readonly Color HousingBorder = new(0.42f, 0.38f, 0.34f, 0.85f);
        private static readonly Color SelectHighlight = new(1.0f, 0.85f, 0.40f, 0.95f);
        private static readonly Color MatchRing = new(0.45f, 0.90f, 0.50f);
        private static readonly Color MissRing = new(0.95f, 0.38f, 0.32f);
        private static readonly Color TrayBg = new(0.20f, 0.18f, 0.24f, 0.9f);
        private static readonly Color TrayBorder = new(0.42f, 0.38f, 0.34f, 0.6f);
        private static readonly Color CrankHub = new(0.30f, 0.28f, 0.33f);
        private static readonly Color CrankRim = new(0.55f, 0.50f, 0.42f);
        private static readonly Color CrankSpoke = new(0.85f, 0.80f, 0.68f);
        private static readonly Color CrankProgressColor = new(1.0f, 0.78f, 0.35f);
        private static readonly Color[] FamilyHintColor = { new(0.55f, 0.45f, 0.30f, 0.65f), new(0.28f, 0.48f, 0.46f, 0.65f), new(0.42f, 0.34f, 0.52f, 0.65f) };

        private bool _dragging;
        private int _dragPartId;
        private int? _dragSourceSocket; // null = came from the tray
        private Vector2 _dragPos;
        private bool _crankDragging;
        private double _crankDragAccumPixels;

        private const float CrankDragPixelsPerStroke = 40f;

        public BenchCanvas()
        {
            // Subscribed (not a `_GuiInput` override) so a headless test can drive the whole
            // drag-drop/crank/keyboard recogniser via `EmitSignal(Control.SignalName.GuiInput, ...)`.
            GuiInput += OnGuiInput;
        }

        /// <summary>Clear any in-flight drag so a reopened bench (Configure on a reused panel) never
        /// carries a stale carried-part from the previous assembly into the new one.</summary>
        public void ResetInteractionState()
        {
            _dragging = false;
            _dragSourceSocket = null;
            _crankDragging = false;
            _crankDragAccumPixels = 0;
        }

        // ── layout — pure functions of Size, shared verbatim between drawing and hit-testing so a
        // drop can never land somewhere different from where it visually appears ─────────────────

        public static Rect2 SocketRect(Vector2 size, int index, int count)
        {
            const float w = 72f, h = 72f, top = 30f, margin = 50f;
            var usable = Mathf.Max(size.X - margin * 2f, 1f);
            var cx = count <= 1 ? size.X / 2f : margin + usable * index / (count - 1);
            return new Rect2(cx - w / 2f, top, w, h);
        }

        public static Rect2 TrayRect(Vector2 size, int partId)
        {
            const float w = 56f, h = 56f, margin = 30f, crankGutter = 90f;
            var n = EngineeringAssemblyScorer.PartCount;
            var y = size.Y - h - 14f;
            var usable = Mathf.Max(size.X - margin * 2f - crankGutter, 1f);
            var step = usable / n;
            var x = margin + step * partId + (step - w) / 2f;
            return new Rect2(x, y, w, h);
        }

        public static Rect2 CrankRect(Vector2 size)
        {
            const float d = 70f;
            return new Rect2(size.X - d - 14f, size.Y - d - 14f, d, d);
        }

        private static int? SocketAt(Vector2 size, Vector2 localPos, int count)
        {
            for (var i = 0; i < count; i++)
            {
                if (SocketRect(size, i, count).HasPoint(localPos))
                {
                    return i;
                }
            }

            return null;
        }

        private static int? TrayPartAt(Vector2 size, Vector2 localPos)
        {
            for (var id = 0; id < EngineeringAssemblyScorer.PartCount; id++)
            {
                if (TrayRect(size, id).HasPoint(localPos))
                {
                    return id;
                }
            }

            return null;
        }

        // ── input ──────────────────────────────────────────────────────────────────────────────

        private void OnGuiInput(InputEvent @event)
        {
            if (Completed)
            {
                return;
            }

            switch (@event)
            {
                case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } down:
                    UiKit.ReclaimKeyboard(this); // clicking must not cost the player their keyboard
                    HandleLeftDown(down.Position);
                    break;

                case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } up when _dragging:
                    HandleLeftUp(up.Position);
                    break;

                case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } rDown:
                    if (IsOverCrank(rDown.Position))
                    {
                        _crankDragging = true;
                        _crankDragAccumPixels = 0;
                    }

                    break;

                case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: false }:
                    _crankDragging = false;
                    _crankDragAccumPixels = 0;
                    break;

                case InputEventMouseMotion motion:
                    if (_dragging)
                    {
                        _dragPos = motion.Position;
                        QueueRedraw();
                    }

                    if (_crankDragging)
                    {
                        AccumulateCrankDrag(motion.Relative.Length());
                    }

                    break;

                case InputEventKey key:
                    HandleKey(key);
                    break;
            }
        }

        private void HandleLeftDown(Vector2 pos)
        {
            var traySwatch = TrayPartAt(Size, pos);
            if (traySwatch.HasValue)
            {
                _dragging = true;
                _dragPartId = traySwatch.Value;
                _dragSourceSocket = null;
                _dragPos = pos;
                QueueRedraw();
                return;
            }

            var socket = SocketAt(Size, pos, SocketCount);
            if (socket.HasValue && Seated.TryGetValue(socket.Value, out var existingPart))
            {
                _dragging = true;
                _dragPartId = existingPart;
                _dragSourceSocket = socket.Value;
                _dragPos = pos;
                QueueRedraw();
            }
        }

        private void HandleLeftUp(Vector2 pos)
        {
            var target = SocketAt(Size, pos, SocketCount);
            var partId = _dragPartId;
            var source = _dragSourceSocket;
            _dragging = false;
            _dragSourceSocket = null;
            QueueRedraw();

            if (target.HasValue)
            {
                if (source.HasValue)
                {
                    if (source.Value == target.Value)
                    {
                        return; // dropped back exactly where it was picked up — no-op
                    }

                    // Relocating an existing socket's part to a DIFFERENT socket: vacate the
                    // original socket first so the part moves rather than duplicates.
                    PullRequested?.Invoke(source.Value);
                }

                PlaceRequested?.Invoke(target.Value, partId);
            }
            else if (source.HasValue)
            {
                // Carried an existing socket's part off any socket entirely — pulls it free, the
                // same "drop it back on the bench" gesture as returning a bottle to the shelf.
                PullRequested?.Invoke(source.Value);
            }

            // else: picked up from the tray and dropped nowhere valid — shelved harmlessly.
        }

        private void AccumulateCrankDrag(float pixels)
        {
            if (pixels <= 0)
            {
                return;
            }

            _crankDragAccumPixels += pixels;
            while (_crankDragAccumPixels >= CrankDragPixelsPerStroke)
            {
                CrankStrokeRequested?.Invoke();
                _crankDragAccumPixels -= CrankDragPixelsPerStroke;
            }
        }

        /// <summary>C2 (<see cref="MinigameInput"/>): every branch is now an <see
        /// cref="InputMap"/> action check, never a raw <see cref="Key"/> match — <see
        /// cref="InputEvent.IsActionPressed(Godot.StringName,System.Boolean,System.Boolean)"/>'s
        /// default <c>allowEcho: false</c> already reproduces the old outer
        /// <c>Pressed: true, Echo: false</c> gate, so no case here needs to re-check either.</summary>
        private void HandleKey(InputEventKey key)
        {
            if (key.IsActionPressed("move_left")) { SocketCycleRequested?.Invoke(-1); return; }
            if (key.IsActionPressed("move_right")) { SocketCycleRequested?.Invoke(1); return; }
            if (key.IsActionPressed("move_up")) { PartCycleRequested?.Invoke(-1); return; }
            if (key.IsActionPressed("move_down")) { PartCycleRequested?.Invoke(1); return; }
            if (key.IsActionPressed("confirm")) { SeatRequested?.Invoke(); return; }
            if (key.IsActionPressed("pull_part")) { PullSelectedRequested?.Invoke(); return; }
            if (key.IsActionPressed("crank_stroke")) { CrankStrokeRequested?.Invoke(); return; }
        }

        public bool IsOverCrank(Vector2 localPos) => Size.X > 0f && Size.Y > 0f && CrankRect(Size).HasPoint(localPos);

        // ── drawing ────────────────────────────────────────────────────────────────────────────

        public override void _Draw()
        {
            var size = Size;
            if (size.X <= 0 || size.Y <= 0)
            {
                return;
            }

            DrawRect(new Rect2(Vector2.Zero, size), BenchBg);
            for (var i = 0; i < SocketCount; i++)
            {
                DrawSocket(size, i);
            }

            for (var id = 0; id < EngineeringAssemblyScorer.PartCount; id++)
            {
                DrawTraySwatch(size, id);
            }

            DrawCrank(size);
            DrawCarried(size);
        }

        private void DrawSocket(Vector2 size, int index)
        {
            var rect = SocketRect(size, index, SocketCount);
            var center = rect.Position + rect.Size / 2f;
            var family = index < Schematic.Count ? Schematic[index] / 2 : 0;
            var selected = index == SelectedSocketId;
            var pulse = 0.85f + 0.15f * Mathf.Sin(AnimClock * 2f + index);

            DrawRect(rect, HousingBg);
            DrawRect(rect, selected ? SelectHighlight : HousingBorder, filled: false, width: selected ? 3f : 2f);

            var hintRect = new Rect2(rect.Position + rect.Size * 0.16f, rect.Size * 0.68f);
            DrawFamilyHint(hintRect, family, new Color(FamilyHintColor[family], FamilyHintColor[family].A * pulse));

            if (Seated.TryGetValue(index, out var partId))
            {
                var correct = index < Schematic.Count && Schematic[index] == partId;
                DrawPartShape(partId, center, rect.Size.X * 0.6f, Parts[partId].Color, filled: true, lineWidth: 2f);
                DrawArc(center, rect.Size.X * 0.56f, 0f, Mathf.Tau, 28, correct ? MatchRing : MissRing, 3f);
            }
        }

        private void DrawTraySwatch(Vector2 size, int partId)
        {
            var rect = TrayRect(size, partId);
            var selected = partId == SelectedPartId;
            DrawRect(rect, TrayBg);
            DrawRect(rect, selected ? SelectHighlight : TrayBorder, filled: false, width: selected ? 3f : 1.5f);
            DrawPartShape(partId, rect.Position + rect.Size / 2f, rect.Size.X * 0.72f, Parts[partId].Color, filled: true, lineWidth: 2f);
        }

        private void DrawCrank(Vector2 size)
        {
            var rect = CrankRect(size);
            var center = rect.Position + rect.Size / 2f;
            var r = rect.Size.X / 2f;

            DrawCircle(center, r, CrankHub);
            DrawArc(center, r, 0f, Mathf.Tau, 24, CrankRim, 3f);

            // Cosmetic-only rotation: a slow idle drift plus a turn per banked stroke — never read
            // by anything that scores; CrankProgressPermille alone decides completion.
            var angle = AnimClock * 0.4f + Mathf.Tau * 2f * (CrankProgressPermille / 1000f);
            for (var i = 0; i < 4; i++)
            {
                var a = angle + Mathf.Pi / 2f * i;
                var tip = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r * 0.85f;
                DrawLine(center, tip, CrankSpoke, 4f);
            }

            if (CrankProgressPermille > 0)
            {
                DrawArc(center, r + 6f, -Mathf.Pi / 2f, -Mathf.Pi / 2f + Mathf.Tau * (CrankProgressPermille / 1000f), 24, CrankProgressColor, 4f);
            }
        }

        private void DrawCarried(Vector2 size)
        {
            if (!_dragging)
            {
                return;
            }

            var over = SocketAt(size, _dragPos, SocketCount);
            if (over.HasValue)
            {
                DrawRect(SocketRect(size, over.Value, SocketCount), new Color(MatchRing, 0.22f));
            }

            DrawPartShape(_dragPartId, _dragPos, 40f, Parts[_dragPartId].Color, filled: true, lineWidth: 2f);
        }

        /// <summary>The wanted FAMILY's silhouette at a NEUTRAL detail level that matches neither of
        /// its two real variants — narrows the socket to "a gear goes here" without ever revealing
        /// fine vs. coarse. That discrimination is left to comparing the tray's two real variants.</summary>
        private void DrawFamilyHint(Rect2 rect, int family, Color color)
        {
            switch (family)
            {
                case 0: DrawGear(rect.Position + rect.Size / 2f, rect.Size.X / 2f, 8, color, filled: false, lineWidth: 2f); break;
                case 1: DrawSpring(rect, 5, color, filled: false, lineWidth: 2f); break;
                default: DrawPlate(rect, beveled: false, color, filled: false, lineWidth: 2f); break;
            }
        }

        private void DrawPartShape(int partId, Vector2 center, float size, Color color, bool filled, float lineWidth)
        {
            var spec = Parts[partId];
            switch (spec.Family)
            {
                case 0:
                    DrawGear(center, size / 2f, spec.Detail, color, filled, lineWidth);
                    break;
                case 1:
                    DrawSpring(new Rect2(center - new Vector2(size / 2f, size / 3f), new Vector2(size, size * 2f / 3f)), spec.Detail, color, filled, lineWidth);
                    break;
                default:
                    DrawPlate(new Rect2(center - new Vector2(size / 2f, size / 2f), new Vector2(size, size)), spec.Detail > 0, color, filled, lineWidth);
                    break;
            }
        }

        private void DrawGear(Vector2 center, float bodyRadius, int teeth, Color color, bool filled, float lineWidth)
        {
            if (filled)
            {
                DrawCircle(center, bodyRadius * 0.68f, color);
            }
            else
            {
                DrawArc(center, bodyRadius * 0.68f, 0f, Mathf.Tau, 24, color, lineWidth);
            }

            var halfWidthAngle = Mathf.Pi / (teeth * 2.2f);
            for (var i = 0; i < teeth; i++)
            {
                var angle = Mathf.Tau * i / teeth;
                var pts = ToothPolygon(center, angle, bodyRadius * 0.66f, bodyRadius, halfWidthAngle);
                if (filled)
                {
                    DrawColoredPolygon(pts, color);
                }
                else
                {
                    for (var e = 0; e < pts.Length; e++)
                    {
                        DrawLine(pts[e], pts[(e + 1) % pts.Length], color, lineWidth * 0.7f);
                    }
                }
            }

            DrawCircle(center, bodyRadius * 0.16f, filled ? color.Darkened(0.3f) : new Color(color, 0.7f));
        }

        private static Vector2[] ToothPolygon(Vector2 center, float angle, float innerR, float outerR, float halfWidthAngle) => new[]
        {
            center + new Vector2(Mathf.Cos(angle - halfWidthAngle), Mathf.Sin(angle - halfWidthAngle)) * innerR,
            center + new Vector2(Mathf.Cos(angle + halfWidthAngle), Mathf.Sin(angle + halfWidthAngle)) * innerR,
            center + new Vector2(Mathf.Cos(angle + halfWidthAngle * 0.6f), Mathf.Sin(angle + halfWidthAngle * 0.6f)) * outerR,
            center + new Vector2(Mathf.Cos(angle - halfWidthAngle * 0.6f), Mathf.Sin(angle - halfWidthAngle * 0.6f)) * outerR,
        };

        private void DrawSpring(Rect2 rect, int coils, Color color, bool filled, float lineWidth)
        {
            var cy = rect.Position.Y + rect.Size.Y / 2f;
            var r = rect.Size.Y / 2f * 0.8f;
            var step = coils <= 1 ? rect.Size.X : rect.Size.X / coils;
            DrawLine(new Vector2(rect.Position.X, cy), new Vector2(rect.Position.X + rect.Size.X, cy), filled ? new Color(color, 0.65f) : color, lineWidth * 0.6f);
            for (var i = 0; i < coils; i++)
            {
                var cx = rect.Position.X + step * (i + 0.5f);
                if (filled)
                {
                    DrawCircle(new Vector2(cx, cy), r, color);
                }

                DrawArc(new Vector2(cx, cy), r, 0f, Mathf.Tau, 16, filled ? color.Darkened(0.25f) : color, lineWidth);
            }
        }

        private void DrawPlate(Rect2 rect, bool beveled, Color color, bool filled, float lineWidth)
        {
            if (filled)
            {
                DrawRect(rect, color);
            }
            else
            {
                DrawRect(rect, color, filled: false, width: lineWidth);
            }

            if (beveled)
            {
                const float inset = 6f;
                var inner = new Rect2(rect.Position + new Vector2(inset, inset), rect.Size - new Vector2(inset * 2f, inset * 2f));
                DrawRect(inner, filled ? color.Lightened(0.3f) : color, filled: false, width: lineWidth * 0.7f);
            }
        }
    }

    /// <summary>
    /// Claim the keyboard the moment this overlay actually appears on screen.
    ///
    /// <para>Focus used to be claimed once from <c>EnsureBuilt</c>, which production runs at boot
    /// with the overlay HIDDEN — and <see cref="GodotClient.Ui.UiKit.ClaimKeyboard"/> defers its grab
    /// behind an <c>IsVisibleInTree()</c> guard, so that grab silently did nothing and was never
    /// retried. The overlay therefore never held the keyboard in the shipped game: Space pressed
    /// whichever panel button still had focus, which re-opened and RESET the run, and the bellows key
    /// reached nothing at all, leaving the craft impossible to finish.</para>
    ///
    /// <para>Claiming from the open path is not enough either — the drawer that hosts this overlay is
    /// not visible in the tree yet on that frame, so the deferred grab misses again. The only moment
    /// that is reliably correct is when THIS node becomes visible in the tree, which is exactly what
    /// this notification reports. The overlay owns its own focus; no caller has to remember.</para>
    /// </summary>
    public override void _Notification(int what)
    {
        if (what == NotificationVisibilityChanged && IsVisibleInTree())
        {
            GodotClient.Ui.UiKit.ClaimKeyboard(this);
        }
    }

}
