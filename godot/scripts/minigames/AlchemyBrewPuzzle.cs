using System;
using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Professions;
using Godot;
using GodotClient.Ui;

namespace GodotClient.Minigames;

/// <summary>
/// Phase B (alchemist active-craft): the reagent-puzzle overlay — the alchemist's counterpart to
/// <see cref="ForgeMinigame"/>, but the IN-SIM-SCORED shape (PKD1 dual mode): this panel only
/// PRESENTS the puzzle and collects discrete choices; the authoritative grade is computed inside
/// the pure sim by <c>AlchemyPuzzleScorer</c> when the queued <see cref="CraftAction"/> resolves.
/// Nothing here is real-time — every meaningful input is a discrete method
/// (<see cref="PourReagent"/>/<see cref="UndoPour"/>/<see cref="Submit"/>/<see cref="Cancel"/>),
/// no <c>_Process</c>, no wall-clock, no engine RNG — so gdUnit tests drive it property-only
/// (no frame pump, no rendering SubViewport — the 3D headless-hang rule).
///
/// <para><b>Single-action contract (PKD8, same as the forge):</b> <see cref="Finished"/> fires
/// EXACTLY ONCE, on <see cref="Submit"/>, carrying one <see cref="CraftAction"/> whose
/// <see cref="CraftAction.Puzzle"/> is the <see cref="AlchemyReagentPuzzle"/> built from the pours;
/// <see cref="Cancel"/> raises <see cref="Cancelled"/> and the caller queues NOTHING.
/// <see cref="CraftAction.PerformanceGrade"/> stays null — the puzzle is the single source the sim
/// scores; <see cref="CraftAction.SubScores"/> carries the scorer's preview triple
/// (exact/placed/grade per-mille) as ledger flavor DATA, never rules.</para>
///
/// <para><b>MVP puzzle read:</b> the recipe's ideal pour order is shown as "recipe notes" and the
/// player must execute it faithfully — mistakes cost score, talents (MinigameAssists, consumed by
/// the sim scorer) forgive them. Hiding/discovering the notes (memory depth) is deliberate later
/// tuning, not sim work: the seam only carries the pour list either way.</para>
/// </summary>
public sealed partial class AlchemyBrewPuzzle : PanelContainer
{
    public string RecipeId { get; private set; } = string.Empty;
    public string MaterialKey { get; private set; } = string.Empty;

    /// <summary>The pours so far, in order — capped at the recipe's ideal-sequence length.</summary>
    public ImmutableList<int> Poured { get; private set; } = ImmutableList<int>.Empty;

    /// <summary>The recipe's required pour count (the ideal sequence's length).</summary>
    public int RequiredPours { get; private set; }

    public bool Completed { get; private set; }
    public bool WasCancelled { get; private set; }

    /// <summary>The exact action <see cref="Finished"/> carried — test/inspection visibility.</summary>
    public CraftAction? EmittedAction { get; private set; }

    /// <summary>Raised EXACTLY ONCE, on <see cref="Submit"/>, with the one action to queue.</summary>
    public event Action<CraftAction>? Finished;

    /// <summary>Raised on <see cref="Cancel"/> — the caller queues nothing.</summary>
    public event Action? Cancelled;

    private Recipe? _recipe;
    private ProfessionDefinition? _profession;
    private ImmutableSortedSet<string> _unlockedTalents = ImmutableSortedSet<string>.Empty;
    private ImmutableList<int> _ideal = ImmutableList<int>.Empty;

    private Label _titleLabel = null!;
    private Label _notesLabel = null!;
    private Label _pouredLabel = null!;
    private BrewCanvas _canvas = null!;
    private Button _undo = null!;
    private Button _submit = null!;
    private Button _cancel = null!;
    private GridContainer _palette = null!;
    private bool _built;

    /// <summary>One evocative brew color per reagent id (indexes match <see cref="AlchemyReagents"/>):
    /// Sunpetal gold, Ironmoss moss, Dewroot dew-teal, Cinderbark ember, Glimmercap glow-violet,
    /// Voidsalt deep indigo — the shared palette the orb icons AND the cauldron canvas both read.</summary>
    private static readonly Color[] ReagentColors =
    {
        new(1.00f, 0.82f, 0.30f), // Sunpetal
        new(0.45f, 0.56f, 0.40f), // Ironmoss
        new(0.35f, 0.76f, 0.72f), // Dewroot
        new(0.80f, 0.34f, 0.20f), // Cinderbark
        new(0.64f, 0.44f, 0.86f), // Glimmercap
        new(0.34f, 0.32f, 0.52f), // Voidsalt
    };

    private static Color ColorFor(int reagentId) =>
        reagentId >= 0 && reagentId < ReagentColors.Length ? ReagentColors[reagentId] : Colors.Gray;

    public override void _Ready() => EnsureBuilt();

    /// <summary>Bind a fresh run for this recipe/material/talent context. Safe to call repeatedly
    /// (reopening for another recipe) — always leaves a clean, un-completed run.</summary>
    public void Configure(
        Recipe recipe, string materialKey, ProfessionDefinition profession, ImmutableSortedSet<string> unlockedTalents)
    {
        EnsureBuilt();

        _recipe = recipe;
        _profession = profession;
        _unlockedTalents = unlockedTalents;
        _ideal = AlchemyPuzzleScorer.IdealSequenceFor(recipe);

        RecipeId = recipe.RecipeId;
        MaterialKey = materialKey;
        RequiredPours = _ideal.Count;
        Poured = ImmutableList<int>.Empty;
        Completed = false;
        WasCancelled = false;
        EmittedAction = null;

        RepaintUi();
    }

    /// <summary>Pour one reagent (a discrete choice). Unknown ids and pours past the recipe's
    /// count are ignored — the palette is the only intended entry point, this is just belt and
    /// braces for scripted callers.</summary>
    public void PourReagent(int reagentId)
    {
        if (Completed || WasCancelled || reagentId < 0 || reagentId >= AlchemyReagents.Count
            || Poured.Count >= RequiredPours)
        {
            return;
        }

        Poured = Poured.Add(reagentId);
        RepaintUi();
    }

    /// <summary>Take back the last pour.</summary>
    public void UndoPour()
    {
        if (Completed || WasCancelled || Poured.IsEmpty)
        {
            return;
        }

        Poured = Poured.RemoveAt(Poured.Count - 1);
        RepaintUi();
    }

    /// <summary>Commit the brew: builds the ONE <see cref="CraftAction"/> (PKD8) with the puzzle
    /// payload and the scorer's preview sub-scores, and raises <see cref="Finished"/>. A partial
    /// pour is legal — it simply scores what it scores when the sim resolves it.</summary>
    public void Submit()
    {
        if (Completed || WasCancelled || _recipe is null || _profession is null)
        {
            return;
        }

        Completed = true;
        var puzzle = new AlchemyReagentPuzzle(Poured);
        // Preview only — the sim recomputes the authoritative grade from the SAME pure scorer
        // when the action resolves; this triple rides SubScores as ledger flavor data.
        var preview = AlchemyPuzzleScorer.Score(_recipe, puzzle, _unlockedTalents, _profession);
        var action = new CraftAction(
            RecipeId, MaterialKey, PerformanceGrade: null, Puzzle: puzzle,
            SubScores: ImmutableList.Create(preview.ExactPermille, preview.PlacedPermille, preview.GradePermille));
        EmittedAction = action;
        RepaintUi();
        Finished?.Invoke(action);
    }

    /// <summary>Abandon the brew — queues nothing (<see cref="Cancelled"/> only).</summary>
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

    private void EnsureBuilt()
    {
        if (_built)
        {
            return;
        }

        Name = "AlchemyBrewPuzzle";
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop; // an open overlay owns clicks (same idiom as ForgeMinigame)

        var body = new VBoxContainer { Name = "AlchemyBrewBody" };
        AddChild(body);

        _titleLabel = new Label { Name = "AlchemyBrewTitle" };
        _titleLabel.AddThemeColorOverride("font_color", GameTheme.HeaderColor);
        _titleLabel.ThemeTypeVariation = GameTheme.HeaderThemeType;
        body.AddChild(_titleLabel);

        _notesLabel = new Label { Name = "AlchemyBrewNotes", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        body.AddChild(_notesLabel);

        _canvas = new BrewCanvas { Name = "BrewCanvas", CustomMinimumSize = new Vector2(0, 260) };
        _canvas.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        body.AddChild(_canvas);

        _pouredLabel = new Label { Name = "AlchemyBrewPoured", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        body.AddChild(_pouredLabel);

        // A 3-column grid, not a single row: six named reagent buttons in one HBox overflow the
        // 600px drawer (DrawerHost.DrawerWidth) and get clipped at its edge.
        _palette = new GridContainer { Name = "AlchemyBrewPalette", Columns = 3 };
        body.AddChild(_palette);
        for (var id = 0; id < AlchemyReagents.Count; id++)
        {
            var reagentId = id; // capture per-iteration
            var pour = new Button
            {
                Name = $"Reagent_{reagentId}",
                Text = AlchemyReagents.Names[reagentId],
                Icon = MakeOrb(ColorFor(reagentId)),
            };
            pour.Pressed += () => PourReagent(reagentId);
            _palette.AddChild(pour);
        }

        var buttonRow = new HBoxContainer { Name = "AlchemyBrewButtons" };
        body.AddChild(buttonRow);

        _undo = new Button { Name = "BrewUndo", Text = "Undo pour" };
        _undo.Pressed += UndoPour;
        buttonRow.AddChild(_undo);

        _submit = new Button { Name = "BrewSubmit", Text = "Brew!" };
        _submit.Pressed += Submit;
        buttonRow.AddChild(_submit);

        _cancel = new Button { Name = "BrewCancel", Text = "Cancel" };
        _cancel.Pressed += Cancel;
        buttonRow.AddChild(_cancel);

        _built = true;
        RepaintUi();
    }

    /// <summary>Render-only — reads state, writes none. Called after every state change above.</summary>
    private void RepaintUi()
    {
        if (!_built)
        {
            return;
        }

        _titleLabel.Text = $"Brew: {RecipeId}";
        _notesLabel.Text = _ideal.IsEmpty
            ? string.Empty
            : "Recipe — match the top row, pour left to right:";
        _pouredLabel.Text = Completed
            ? $"Brewed! (score {EmittedAction?.SubScores?[2]}‰)"
            : WasCancelled
                ? "Cancelled."
                : $"Cauldron: {Poured.Count}/{RequiredPours} poured";

        // One-shot pour FX: the canvas compares against what it last saw, so a NEW pour triggers the
        // stream/bloom/fizzle and an Undo triggers nothing (the puzzle class stays _Process-free and
        // property-only, exactly as the gdUnit tests drive it).
        _canvas.Ideal = _ideal;
        _canvas.Done = Completed;
        _canvas.SetPoured(Poured);
        _canvas.QueueRedraw();

        _undo.Disabled = Completed || WasCancelled || Poured.IsEmpty;
        _submit.Disabled = Completed || WasCancelled;
    }

    /// <summary>A small filled-circle icon in the reagent's brew color — the palette button's swatch
    /// so the player picks reagents by color, not just name (mirrors the cauldron orbs below).</summary>
    private static ImageTexture MakeOrb(Color color)
    {
        const int d = 16;
        var img = Image.CreateEmpty(d, d, false, Image.Format.Rgba8);
        var c = new Vector2(d / 2f, d / 2f);
        for (var y = 0; y < d; y++)
        {
            for (var x = 0; x < d; x++)
            {
                var dist = new Vector2(x + 0.5f, y + 0.5f).DistanceTo(c);
                if (dist <= d / 2f - 0.5f)
                {
                    var shade = 1f - 0.35f * (dist / (d / 2f)); // soft spherical shading
                    img.SetPixel(x, y, new Color(color.R * shade, color.G * shade, color.B * shade));
                }
            }
        }

        return ImageTexture.CreateFromImage(img);
    }

    /// <summary>
    /// The cauldron scene: an iron pot with a REAL liquid that rises and colour-shifts as reagents
    /// go in, a parchment recipe strip up top (what to pour, in order, with a gold caret on the next
    /// expected slot), rim sockets showing each pour's correctness (green/red rings — the load-bearing
    /// feedback), and a shelf of tinted bottles. Pouring tips the matching bottle, drips droplets into
    /// the pot, and either blooms (correct) or fizzles with smoke (wrong). Plain <see cref="_Draw"/>
    /// primitives + two nearest-filtered sprites, each null-checked with a primitive fallback so
    /// headless CI renders without imports (never a 3D SubViewport — a known gdUnit headless hang).
    /// All motion is accumulated-frame-delta only, no wall-clock, no RNG (fixed offset tables).
    ///
    /// <para>The owning puzzle class stays <c>_Process</c>-free and property-only (as its gdUnit tests
    /// drive it): this canvas owns the animation clock and detects a new pour by diffing what it was
    /// last handed (<see cref="SetPoured"/>), so an Undo triggers no FX.</para>
    /// </summary>
    private sealed partial class BrewCanvas : Control
    {
        public ImmutableList<int> Ideal = ImmutableList<int>.Empty;
        public ImmutableList<int> Poured { get; private set; } = ImmutableList<int>.Empty;
        public bool Done;

        private static readonly Color TargetRing = new(1f, 0.92f, 0.72f, 0.55f);
        private static readonly Color EmptySocket = new(0.26f, 0.24f, 0.32f, 0.95f);
        private static readonly Color MatchRing = new(0.45f, 0.90f, 0.50f);
        private static readonly Color MissRing = new(0.95f, 0.38f, 0.32f);
        private static readonly Color BrewBase = new(0.23f, 0.17f, 0.33f);
        private static readonly Color Parchment = new(0.91f, 0.85f, 0.70f);
        private static readonly Color ParchmentEdge = new(0.62f, 0.55f, 0.42f);
        private static readonly Color ShelfWood = new(0.34f, 0.23f, 0.15f);
        private static readonly Color FireGlow = new(1.0f, 0.52f, 0.18f);
        private static readonly Color Fizzle = new(0.42f, 0.48f, 0.29f);
        private static readonly Color Caret = new(1.0f, 0.85f, 0.35f);

        // Fixed droplet/bubble/smoke offsets — deterministic, no RNG.
        private static readonly float[] DropSpread = { -5f, -2f, 0f, 2f, 4f, 6f };
        private static readonly (float Fx, float Phase)[] Bubbles =
        {
            (0.32f, 0.0f), (0.44f, 0.55f), (0.50f, 0.25f), (0.58f, 0.8f), (0.68f, 0.4f),
        };

        private float _anim;
        private float _level;              // eased 0..1 liquid level (follows Poured.Count/Required)
        private float _pourT = -1f;        // 0..0.5 pour animation, -1 idle
        private int _pourReagent = -1;
        private bool _pourCorrect;
        private float _fizzleT = -1f;
        private float _bloomT = -1f;
        private Texture2D? _cauldron;
        private Texture2D? _bottle;
        private Texture2D? _backdrop;
        private bool _texTried;

        /// <summary>Feed the pour list; a GROWN list fires the one-shot pour FX (an Undo never does).</summary>
        public void SetPoured(ImmutableList<int> poured)
        {
            if (poured.Count > Poured.Count)
            {
                var idx = poured.Count - 1;
                _pourReagent = poured[idx];
                _pourCorrect = idx < Ideal.Count && poured[idx] == Ideal[idx];
                _pourT = 0f;
                if (_pourCorrect) _bloomT = 0f; else _fizzleT = 0f;
            }

            Poured = poured;
        }

        public override void _Process(double delta)
        {
            var dt = (float)delta;
            _anim += dt;

            var target = Ideal.Count > 0 ? (float)Poured.Count / Ideal.Count : 0f;
            _level = Mathf.MoveToward(_level, target, 3f * dt);   // liquid eases up/down

            if (_pourT >= 0f) { _pourT += dt; if (_pourT > 0.5f) _pourT = -1f; }
            if (_bloomT >= 0f) { _bloomT += dt; if (_bloomT > 0.4f) _bloomT = -1f; }
            if (_fizzleT >= 0f) { _fizzleT += dt; if (_fizzleT > 0.45f) _fizzleT = -1f; }
            QueueRedraw();
        }

        public override void _Draw()
        {
            var size = Size;
            var n = Ideal.Count;
            if (size.X <= 0 || size.Y <= 0 || n <= 0)
            {
                return;
            }

            EnsureTextures();

            if (_backdrop is not null)
            {
                DrawTextureRect(_backdrop, new Rect2(Vector2.Zero, size), false);
            }

            // ── layout ──
            var potW = Mathf.Min(size.X * 0.52f, 240f);
            var potH = potW * 0.75f;
            var potPos = new Vector2(size.X * 0.5f - potW / 2f, size.Y - potH - 6f);
            var mouth = new Rect2(potPos.X + potW * 0.17f, potPos.Y + potH * 0.16f, potW * 0.66f, potH * 0.21f);
            var interiorTop = mouth.Position.Y + mouth.Size.Y * 0.4f;
            var interiorBottom = potPos.Y + potH * 0.80f;

            DrawFireGlow(potPos, potW, potH);
            DrawParchment(size, n);
            DrawShelf(size);

            // Cauldron sprite (or a primitive pot).
            if (_cauldron is not null)
            {
                // Dark iron so the pot sits INSIDE the painted lab instead of reading as a bright
                // sprite pasted over it; a touch of warm bounce from the hearth below.
                var iron = _backdrop is not null ? new Color(0.38f, 0.36f, 0.44f) : Colors.White;
                DrawTextureRect(_cauldron, new Rect2(potPos, new Vector2(potW, potH)), false, iron);
            }
            else
            {
                DrawRect(new Rect2(potPos.X, potPos.Y + potH * 0.2f, potW, potH * 0.8f), new Color(0.29f, 0.30f, 0.35f));
            }

            DrawLiquid(mouth, interiorTop, interiorBottom);
            DrawSockets(mouth, n);
            DrawPourStream(size, mouth);
        }

        private void DrawFireGlow(Vector2 potPos, float potW, float potH)
        {
            var pulse = 0.35f + 0.25f * (0.5f + 0.5f * Mathf.Sin(_anim * 2.4f));
            var center = new Vector2(potPos.X + potW / 2f, potPos.Y + potH * 0.98f);
            DrawCircle(center, potW * 0.34f, new Color(FireGlow, pulse * 0.32f));
            DrawCircle(center, potW * 0.20f, new Color(FireGlow, pulse * 0.5f));
        }

        /// <summary>The recipe: mini tinted bottles in pour order on a parchment strip, with a gold
        /// caret under the next expected slot (the order IS the puzzle, so this stays loud).</summary>
        private void DrawParchment(Vector2 size, int n)
        {
            var w = Mathf.Min(size.X * 0.62f, 34f * n + 24f);
            var rect = new Rect2(10f, 6f, w, 44f);
            DrawRect(rect, new Color(Parchment, 0.93f));
            DrawRect(rect, new Color(ParchmentEdge, 0.9f), filled: false, width: 2f);

            var step = (w - 20f) / n;
            for (var i = 0; i < n; i++)
            {
                var cx = rect.Position.X + 10f + step * (i + 0.5f);
                var c = ColorFor(Ideal[i]);
                if (_bottle is not null)
                {
                    DrawTextureRect(_bottle, new Rect2(new Vector2(cx - 7f, rect.Position.Y + 5f), new Vector2(14f, 19f)), false, c);
                }
                else
                {
                    DrawCircle(new Vector2(cx, rect.Position.Y + 15f), 7f, c);
                }

                if (i == Poured.Count && !Done)
                {
                    // "pour this next" caret
                    var y = rect.Position.Y + 30f;
                    DrawColoredPolygon(
                        new[] { new Vector2(cx - 5, y + 8), new Vector2(cx + 5, y + 8), new Vector2(cx, y) },
                        new Color(Caret, 0.95f));
                }
            }
        }

        private const float ShelfStep = 20f;   // px per bottle — enough that 14px bottles never overlap
        private const float ShelfY = 12f;

        private void DrawShelf(Vector2 size)
        {
            var count = AlchemyReagents.Count;
            var shelfW = ShelfStep * count;
            var x0 = size.X - shelfW - 10f;
            if (x0 < size.X * 0.45f)
            {
                return; // not enough room beside the parchment — skip the decoration entirely
            }

            DrawRect(new Rect2(x0 - 4f, ShelfY + 32f, shelfW + 8f, 4f), ShelfWood);
            for (var i = 0; i < count; i++)
            {
                var cx = x0 + ShelfStep * (i + 0.5f);
                var tipping = _pourT >= 0f && _pourReagent == i;
                var lean = tipping ? Mathf.Lerp(0f, 55f, Mathf.Min(1f, _pourT / 0.15f)) : 0f;
                var c = ColorFor(i);
                if (_bottle is not null)
                {
                    DrawSetTransform(new Vector2(cx, ShelfY + 32f), Mathf.DegToRad(lean), Vector2.One);
                    DrawTextureRect(_bottle, new Rect2(new Vector2(-7f, -26f), new Vector2(14f, 26f)), false, c);
                    DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
                }
                else
                {
                    DrawCircle(new Vector2(cx, ShelfY + 20f), 6f, c);
                }
            }
        }

        private void DrawLiquid(Rect2 mouth, float interiorTop, float interiorBottom)
        {
            // Colour fold: recomputed from the whole pour list every frame, so Undo just works. A
            // plain average of six reagents turns muddy olive, so the freshest pour keeps a strong
            // say and the result is brightened — the brew should GLOW, not look like dishwater.
            var col = BrewBase;
            foreach (var id in Poured)
            {
                col = col.Lerp(ColorFor(id), 0.45f);
            }

            if (!Poured.IsEmpty)
            {
                col = col.Lerp(ColorFor(Poured[^1]), 0.35f);                 // freshest reagent identity
                col = new Color(Mathf.Min(col.R * 1.25f, 1f), Mathf.Min(col.G * 1.25f, 1f), Mathf.Min(col.B * 1.25f, 1f));
            }

            if (_fizzleT >= 0f)
            {
                col = col.Lerp(Fizzle, 1f - _fizzleT / 0.45f);
            }

            if (Done)
            {
                col = col.Lerp(new Color(1f, 0.88f, 0.5f), 0.35f + 0.25f * Mathf.Sin(_anim * 5f));
            }

            var surfaceY = Mathf.Lerp(interiorBottom, interiorTop, Mathf.Max(_level, 0.06f));
            var halfW = mouth.Size.X / 2f;
            var cx = mouth.Position.X + halfW;

            // The liquid is a stack of ellipses that TAPER downward — it sits inside the pot's mouth
            // and reads as a pool with depth, never a box overflowing the iron.
            const int slices = 22; // enough slices that the taper reads smooth, not as stacked discs
            for (var i = slices; i >= 0; i--)
            {
                var t = i / (float)slices;                       // 0 at the surface, 1 at the floor
                var y = Mathf.Lerp(surfaceY, interiorBottom, t);
                var rx = halfW * (0.94f - 0.34f * t);            // narrows with depth
                var shade = 1f - 0.22f * t;                      // darker deeper down (gentle)
                DrawEllipseFilled(new Vector2(cx, y), rx, mouth.Size.Y * 0.30f,
                    new Color(col.R * shade, col.G * shade, col.B * shade, 1f));
            }

            DrawEllipseFilled(new Vector2(cx, surfaceY - 1f), halfW * 0.70f, mouth.Size.Y * 0.20f,
                new Color(col.R * 1.35f, col.G * 1.35f, col.B * 1.35f, 0.6f)); // surface sheen

            // Ambient bubbles — livelier the fuller the pot.
            var liveliness = Mathf.Max(1, Poured.Count);
            for (var i = 0; i < Bubbles.Length && i < liveliness + 1; i++)
            {
                var (fx, phase) = Bubbles[i];
                var t = (_anim * 0.8f + phase) % 1f;
                var bx = mouth.Position.X + mouth.Size.X * fx;
                var by = Mathf.Lerp(interiorBottom, surfaceY, t);
                var r = 1.5f + 2f * t;
                DrawCircle(new Vector2(bx, by), r, new Color(1f, 1f, 1f, 0.22f * (1f - t)));
            }

            // Correct-pour bloom ring on the surface.
            if (_bloomT >= 0f && _pourReagent >= 0)
            {
                var t = _bloomT / 0.4f;
                DrawArc(new Vector2(cx, surfaceY), 4f + 38f * t, 0f, Mathf.Tau, 28,
                    new Color(ColorFor(_pourReagent), 1f - t), 2.5f);
            }

            // Wrong-pour smoke puff.
            if (_fizzleT >= 0f)
            {
                var t = _fizzleT / 0.45f;
                DrawCircle(new Vector2(cx, surfaceY - 16f * t), 4f + 8f * t, new Color(0.35f, 0.35f, 0.38f, 0.55f * (1f - t)));
            }
        }

        /// <summary>Per-pour correctness sockets, arced along the pot's rim — the same green/red ring
        /// logic as before, relocated onto the cauldron so the pot IS the board.</summary>
        private void DrawSockets(Rect2 mouth, int n)
        {
            var r = Mathf.Min(mouth.Size.X / (n * 2.4f), 11f);
            var y = mouth.Position.Y + mouth.Size.Y * 0.5f;
            var step = mouth.Size.X / (n + 1);
            for (var i = 0; i < n; i++)
            {
                var cx = mouth.Position.X + step * (i + 1);
                var lift = 3f * Mathf.Sin((i / (float)n) * Mathf.Pi); // follow the rim's curve
                var p = new Vector2(cx, y - lift);
                if (i < Poured.Count)
                {
                    var correct = Poured[i] == Ideal[i];
                    var jitter = !correct && _fizzleT >= 0f && i == Poured.Count - 1
                        ? new Vector2(((int)(_anim * 40f) % 2 == 0 ? 1f : -1f) * 1.5f, 0f)
                        : Vector2.Zero;
                    DrawCircle(p + jitter, r, ColorFor(Poured[i]));
                    DrawArc(p + jitter, r + 2f, 0f, Mathf.Tau, 24, correct ? MatchRing : MissRing, 2.5f);
                }
                else
                {
                    DrawArc(p, r, 0f, Mathf.Tau, 22, EmptySocket, 2f);
                    if (i == Poured.Count && !Done)
                    {
                        DrawArc(p, r + 3f, 0f, Mathf.Tau, 22, new Color(TargetRing, 0.35f + 0.3f * Mathf.Sin(_anim * 4f)), 1.5f);
                    }
                }
            }
        }

        /// <summary>Droplets falling from the tipped bottle into the pot.</summary>
        private void DrawPourStream(Vector2 size, Rect2 mouth)
        {
            if (_pourT < 0f || _pourReagent < 0)
            {
                return;
            }

            // Pour from the shelf bottle when the shelf is on screen; otherwise straight down from
            // above the pot (the shelf is decoration and is skipped on a narrow canvas).
            var count = AlchemyReagents.Count;
            var shelfW = ShelfStep * count;
            var shelfX0 = size.X - shelfW - 10f;
            var to = new Vector2(mouth.Position.X + mouth.Size.X * 0.5f, mouth.Position.Y + mouth.Size.Y * 0.5f);
            var from = shelfX0 >= size.X * 0.45f
                ? new Vector2(shelfX0 + ShelfStep * (_pourReagent + 0.5f), ShelfY + 36f)
                : new Vector2(to.X, Mathf.Max(ShelfY + 36f, mouth.Position.Y - 60f));
            var c = ColorFor(_pourReagent);

            for (var i = 0; i < DropSpread.Length; i++)
            {
                var t = Mathf.Clamp((_pourT - 0.10f - i * 0.03f) / 0.30f, 0f, 1f);
                if (t <= 0f)
                {
                    continue;
                }

                var p = from.Lerp(to, t) + new Vector2(DropSpread[i], 0f);
                DrawCircle(p, 3f, new Color(c, 1f - t * 0.3f));
            }
        }

        private void DrawEllipseFilled(Vector2 center, float rx, float ry, Color color)
        {
            const int seg = 26;
            var pts = new Vector2[seg];
            for (var i = 0; i < seg; i++)
            {
                var a = Mathf.Tau * i / seg;
                pts[i] = new Vector2(center.X + Mathf.Cos(a) * rx, center.Y + Mathf.Sin(a) * ry);
            }

            DrawColoredPolygon(pts, color);
        }

        private void EnsureTextures()
        {
            if (_texTried)
            {
                return;
            }

            _texTried = true;
            TextureFilter = TextureFilterEnum.Nearest;
            _cauldron = LoadTex("res://assets/minigames/cauldron.png");
            _bottle = LoadTex("res://assets/minigames/bottle.png");
            _backdrop = LoadTex("res://assets/minigames/brew_backdrop.png");
        }

        private static Texture2D? LoadTex(string path) =>
            ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
    }
}
