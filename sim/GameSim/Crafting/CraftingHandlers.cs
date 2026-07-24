using GameSim.Contracts;
using GameSim.Professions;

namespace GameSim.Crafting;

/// <summary>
/// Action handler for the crafting module (U4): <see cref="CraftAction"/> and
/// <see cref="UnlockTalentAction"/>. Crafting is legal in ALL THREE phases — the forge
/// never closes (Morning, Expedition, Evening).
///
/// Determinism note (KTD4): every rejection happens BEFORE any RNG draw, so a refused
/// action never advances the stream. Exactly one Roll100 is drawn per successful craft.
///
/// Talent points (v1): unlocking costs nothing beyond prerequisite edges — the
/// talent-point economy (earn rate, per-node costs) is deliberately deferred; when it
/// lands, the cost check slots in next to the prerequisite check below.
/// </summary>
public sealed class CraftingHandlers : IActionHandler
{
    public bool CanHandle(PlayerAction action, DayPhase phase) =>
        action is CraftAction or UnlockTalentAction; // all phases legal

    public (GameState State, RejectedAction? Rejected) Apply(GameState state, PlayerAction action, IDeterministicRng rng, IEventSink events) =>
        action switch
        {
            CraftAction craft => ApplyCraft(state, craft, rng, events),
            UnlockTalentAction unlock => ApplyUnlock(state, unlock),
            _ => (state, new RejectedAction(action, $"CraftingHandlers cannot apply {action.GetType().Name}.")),
        };

    private static (GameState, RejectedAction?) ApplyCraft(GameState state, CraftAction action, IDeterministicRng rng, IEventSink events)
    {
        // 1. Recipe must exist (global lookup across all professions; consumables
        //    live in the same tables as gear — see RecipeTable).
        if (!ProfessionRegistry.TryGetRecipe(action.RecipeId, out var recipe))
        {
            return (state, new RejectedAction(action, $"Unknown recipe '{action.RecipeId}'."));
        }

        // 2. The recipe's profession must be registered and selected by this save.
        if (!ProfessionRegistry.TryGet(recipe!.Profession, out var profession))
        {
            return (state, new RejectedAction(action, $"Recipe '{recipe.RecipeId}' belongs to unknown profession '{recipe.Profession}'."));
        }

        if (!state.Player.IsSelected(recipe.Profession))
        {
            return (state, new RejectedAction(action, $"Profession '{recipe.Profession}' is not selected."));
        }

        // 3. Material must be a known grade key.
        if (!RecipeTable.MaterialGrades.TryGetValue(action.MaterialKey, out var materialGrade))
        {
            return (state, new RejectedAction(action, $"Unknown material '{action.MaterialKey}'."));
        }

        // 4. Tier gate (read from the profession definition) against this profession's talents.
        var talents = state.Player.TalentsFor(recipe.Profession);
        if (profession!.TierGate.TryGetValue(recipe.Tier, out var gate) && !talents.Contains(gate))
        {
            return (state, new RejectedAction(action, $"Recipe '{recipe.RecipeId}' is tier {recipe.Tier}; requires talent '{gate}'."));
        }

        // 5. Material quantity (material-efficiency node from the definition saves one, floor of 1).
        var efficiency = profession.MaterialEfficiencyNode is { } eff && talents.Contains(eff) ? 1 : 0;
        var needed = recipe.MaterialQuantity - efficiency;
        if (needed < 1)
        {
            needed = 1;
        }

        var have = state.Player.Materials.TryGetValue(action.MaterialKey, out var stock) ? stock : 0;
        if (have < needed)
        {
            return (state, new RejectedAction(action, $"Not enough {action.MaterialKey}: need {needed}, have {have}."));
        }

        // 6. Dual-mode puzzle seam (Phase B / PKD1): an in-sim-scored profession submits its
        //    puzzle input on the action instead of a Godot-captured grade. Validate BEFORE the
        //    slot gate (a malformed action keeps its specific rejection even on a spent day)
        //    and, like every rejection above, before any RNG draw (KTD4).
        if (action.Puzzle is not null && action.Puzzle is not AlchemyReagentPuzzle && action.Puzzle is not ForgeTraceInput)
        {
            return (state, new RejectedAction(action, $"Unsupported craft puzzle '{action.Puzzle.GetType().Name}'."));
        }

        if (action.Puzzle is AlchemyReagentPuzzle && (!profession.ActiveCraft || recipe.Profession != AlchemyProfession.Id))
        {
            return (state, new RejectedAction(action, $"Recipe '{recipe.RecipeId}' does not take a reagent puzzle."));
        }

        // Wave 5 (U23c): the blacksmith's Anvil-Map forge trace is only valid for an active-craft
        // blacksmith recipe (the alchemist's reagent puzzle is handled above).
        if (action.Puzzle is ForgeTraceInput && (!profession.ActiveCraft || recipe.Profession != ProfessionRegistry.BlacksmithId))
        {
            return (state, new RejectedAction(action, $"Recipe '{recipe.RecipeId}' does not take a forge trace."));
        }

        // 7. Day action-budget gate (Game-Feel Plan G3): craft is real work (ActionBudget.ConsumesSlot)
        //    — checked LAST, after every other precondition, so an invalid recipe/material/tier/stock
        //    keeps its existing rejection reason even on a slot-exhausted day; only a genuinely legal
        //    craft with zero slots left is newly refused here. No RNG drawn yet — a refused craft never
        //    touches the stream (CLAUDE.md rule 4).
        if (state.ActionSlotsRemaining <= 0)
        {
            return (state, new RejectedAction(action, $"No action slots left today (0/{ActionBudget.SlotsPerDay}) — 'next' to advance."));
        }

        // 8. All checks passed — consume, roll (the single RNG draw), mint, emit.
        // ActiveCraft professions dominance-roll off a per-mille grade: the blacksmith's is
        // CAPTURED by its Godot minigame (action.PerformanceGrade, PA2/PKD2); the alchemist's is
        // SCORED HERE from the reagent puzzle (Phase B/PKD1 — pure integer scorer, zero RNG, so
        // the draw count below is unchanged). A null grade AND null puzzle is the auto-craft
        // path for both. Every passive profession keeps the untouched passive ±8 roll.
        // Wave 5 (U23c): the blacksmith's Anvil-Map trace is scored HERE (pure integer, zero RNG —
        // the same PKD1 pattern as the alchemist), yielding BOTH the dominance grade AND the three
        // forge-beat sub-scores stamped on the item (which U19 signing reads). Null-grade + null-puzzle
        // stays the auto-craft baseline; draw count below is unchanged either way (KTD4).
        ForgeScore? forgeScore = action.Puzzle is ForgeTraceInput trace
            ? ForgeScorer.Score(recipe!, trace, talents, profession)
            : null;
        var performanceGrade = forgeScore?.GradePermille
            ?? (action.Puzzle is AlchemyReagentPuzzle brew
                ? AlchemyPuzzleScorer.Score(recipe!, brew, talents, profession).GradePermille
                : action.PerformanceGrade);
        var quality = profession.ActiveCraft
            ? QualityRoller.RollActive(recipe, materialGrade, talents, profession.Quality, rng, performanceGrade)
            : QualityRoller.Roll(recipe, materialGrade, talents, profession.Quality, rng, performanceGrade);
        var itemId = new ItemId(state.NextItemId);
        // Sub-scores: the Anvil-Map scorer's three zone scores when hand-forged (Wave 5), else the
        // action's Godot-captured sub-scores (legacy/passive), else empty (auto-craft).
        var subScores = forgeScore?.SubScores ?? action.SubScores;
        var item = ItemForge.Forge(itemId, recipe, quality, state.Day, subScores);

        // Wave 4 (U19, "Signed Works"): a rare, deterministic, RNG-free proc — reads only data
        // this craft already produced (quality + the captured forge-beat sub-scores), so it never
        // draws from the stream and never changes the draw-count contract above. See
        // ArtifactSigning's class doc for the condition + the seed-derived name pick.
        string? signedName = null;
        if (ArtifactSigning.Qualifies(item))
        {
            signedName = ArtifactSigning.LegendName(state.Rng.Inc, itemId, recipe.RecipeId, state.Day);
            item = item with { SignedName = signedName };
        }

        // Wave 5 (U23c): the forging itself becomes the item's FIRST History entry — "your craft
        // writes the legends" made literal. Only a hand-forged Anvil-Map craft with earned moments
        // writes it; auto-craft / passive / pre-Wave-5 items get nothing, so the idle golden trace
        // (BaselinePlayer never submits a forge trace) is byte-unaffected.
        if (forgeScore is { Moments: not 0 } scored)
        {
            item = item with { History = item.History.Add(new ItemHistoryEntry(state.Day, "forged", ForgeMomentLine((ForgeMoment)scored.Moments))) };
        }

        var newState = state with
        {
            NextItemId = state.NextItemId + 1,
            Items = state.Items.Add(itemId.Value, item),
            Player = state.Player with
            {
                Materials = state.Player.Materials.SetItem(action.MaterialKey, have - needed),
            },
            ActionSlotsRemaining = state.ActionSlotsRemaining - 1,
        };

        events.Emit(new ItemCrafted(itemId, quality));
        if (signedName is not null)
        {
            events.Emit(new ItemSigned(itemId, signedName));
        }

        return (newState, null);
    }

    /// <summary>Wave 5 (U23c): the item's opening inscription, built purely from the earned forge
    /// moments (a <see cref="ForgeMoment"/> flag set) — deterministic, no RNG, no clock. Data/prose
    /// only; called only when at least one moment was earned.</summary>
    private static string ForgeMomentLine(ForgeMoment moments)
    {
        var parts = new System.Collections.Generic.List<string>();
        if (moments.HasFlag(ForgeMoment.ForgedInOneHeat)) { parts.Add("forged in a single heat"); }
        if (moments.HasFlag(ForgeMoment.NeverScorched)) { parts.Add("never once scorched"); }
        if (moments.HasFlag(ForgeMoment.PerfectQuench)) { parts.Add("quenched clean and true"); }
        if (moments.HasFlag(ForgeMoment.RecoveredFromTheBrink)) { parts.Add("saved from a scorched edge"); }
        return parts.Count == 0
            ? "Forged at the anvil."
            : "Forged at the anvil — " + string.Join(", ", parts) + ".";
    }

    private static (GameState, RejectedAction?) ApplyUnlock(GameState state, UnlockTalentAction action)
    {
        // Scope the unlock to the action's profession: node lookup, unlocked set, and prereqs
        // are all evaluated within that profession's definition (P1).
        if (!ProfessionRegistry.TryGet(action.Profession, out var profession))
        {
            return (state, new RejectedAction(action, $"Unknown profession '{action.Profession}'."));
        }

        if (!profession!.TalentNodes.TryGetValue(action.NodeId, out var node))
        {
            return (state, new RejectedAction(action, $"Unknown talent node '{action.NodeId}' in profession '{action.Profession}'."));
        }

        var talents = state.Player.TalentsFor(action.Profession);
        if (talents.Contains(action.NodeId))
        {
            return (state, new RejectedAction(action, $"Talent '{action.NodeId}' is already unlocked."));
        }

        foreach (var prereq in node.Prerequisites)
        {
            if (!talents.Contains(prereq))
            {
                return (state, new RejectedAction(action, $"Talent '{action.NodeId}' requires '{prereq}' first."));
            }
        }

        // No cost in v1 (talent-point economy deferred — see class doc).
        var newState = state with
        {
            Player = state.Player.WithTalent(action.Profession, action.NodeId),
        };
        return (newState, null);
    }
}
