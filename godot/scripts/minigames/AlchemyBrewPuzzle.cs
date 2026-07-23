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
    private Button _undo = null!;
    private Button _submit = null!;
    private Button _cancel = null!;
    private HBoxContainer _palette = null!;
    private bool _built;

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

        _pouredLabel = new Label { Name = "AlchemyBrewPoured", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        body.AddChild(_pouredLabel);

        _palette = new HBoxContainer { Name = "AlchemyBrewPalette" };
        body.AddChild(_palette);
        for (var id = 0; id < AlchemyReagents.Count; id++)
        {
            var reagentId = id; // capture per-iteration
            var pour = new Button { Name = $"Reagent_{reagentId}", Text = AlchemyReagents.Names[reagentId] };
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
            : "Recipe notes — pour in order: " + string.Join(" → ", _ideal.Select(id => AlchemyReagents.Names[id]));
        _pouredLabel.Text = Completed
            ? $"Brewed! (score {EmittedAction?.SubScores?[2]}‰)"
            : WasCancelled
                ? "Cancelled."
                : $"Cauldron ({Poured.Count}/{RequiredPours}): " +
                  (Poured.IsEmpty ? "(empty)" : string.Join(" → ", Poured.Select(id => AlchemyReagents.Names[id])));
        _undo.Disabled = Completed || WasCancelled || Poured.IsEmpty;
        _submit.Disabled = Completed || WasCancelled;
    }
}
