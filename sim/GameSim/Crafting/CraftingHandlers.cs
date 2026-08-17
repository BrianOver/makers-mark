using GameSim.Contracts;
using GameSim.Economy;
using GameSim.Kernel;
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
/// Talent points (U-T1-9, register #157 — owner ruling "Forge Tier plus an action slot",
/// explicitly rejecting a new talent-point currency): unlocking a node that gates a recipe
/// tier also requires the workshop to already be at the matching <see cref="TalentTree.ForgeTierRequirement"/>
/// Forge Tier, and spends one of the day's action slots like every other piece of real work
/// — checked LAST, same order as every other handler in this codebase. Every other node
/// (the quality-shift chain, material efficiency/mastery) keeps the old free,
/// prerequisite-only unlock; only the two smithing-tier gates are additionally metered.
/// </summary>
public sealed class CraftingHandlers : IActionHandler
{
    /// <summary>Wave 5 (U23e, batch echo): how many echoed auto-crafts one hand-forge seeds.</summary>
    private const int BatchEchoCount = 4;

    /// <summary>Per-mille the echoed grade decays per successive copy.</summary>
    private const int BatchEchoDecayPermille = 80;

    /// <summary>Floor the echoed grade can never fall below — the ordinary auto-craft baseline (PKD4).</summary>
    private const int BatchEchoFloor = 550;

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
        if (action.Puzzle is not null
            && action.Puzzle is not AlchemyReagentPuzzle
            && action.Puzzle is not ForgeTraceInput
            && action.Puzzle is not TanningScrapeInput
            && action.Puzzle is not EngineeringAssemblyInput)
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

        // U7/U8 (plan 2026-07-28-002 part 2): the tanner's hide-scrape and the engineer's assembly
        // are gated exactly like the two above — one puzzle shape per profession, so a puzzle
        // submitted to the wrong bench is refused rather than silently scored against the wrong rules.
        if (action.Puzzle is TanningScrapeInput && (!profession.ActiveCraft || recipe.Profession != TanningProfession.Id))
        {
            return (state, new RejectedAction(action, $"Recipe '{recipe.RecipeId}' does not take a hide scrape."));
        }

        if (action.Puzzle is EngineeringAssemblyInput && (!profession.ActiveCraft || recipe.Profession != EngineeringProfession.Id))
        {
            return (state, new RejectedAction(action, $"Recipe '{recipe.RecipeId}' does not take an assembly."));
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

        // Wave 5 (U23e, batch echo): a null-puzzle / null-grade AUTO-craft that repeats your last
        // hand-forge's recipe on the SAME day inherits a DECAYING echo of that grade — set the rhythm
        // by hand once, the copies follow — so you don't hand-forge five identical blades. Pure integer,
        // no RNG draw (the quality roll below stays one Roll100). Never fires on the idle trace
        // (BaselinePlayer never hand-forges), so it moves only the serialized SHAPE, not behavior.
        var echo = state.Player.BatchEcho;
        var isAutoCraft = action.Puzzle is null && action.PerformanceGrade is null;
        int? echoGrade = isAutoCraft && echo is not null
                && echo.RecipeId == recipe.RecipeId && echo.Day == state.Day && echo.Uses < BatchEchoCount
            ? System.Math.Max(BatchEchoFloor, echo.SeedGrade - (BatchEchoDecayPermille * (echo.Uses + 1)))
            : null;

        // U7/U8 part 2: the tanner's scrape and the engineer's assembly join the brew on the
        // scored-here path — all three are pure integer scorers with zero RNG, so adding them leaves
        // the single-draw contract below untouched (KTD4). A puzzle shape with no scorer falls through
        // to the action's Godot-captured grade, which is also the auto-craft path.
        int? puzzleGrade = action.Puzzle switch
        {
            AlchemyReagentPuzzle brew => AlchemyPuzzleScorer.Score(recipe!, brew, talents, profession).GradePermille,
            TanningScrapeInput scrape => TanningScrapeScorer.Score(recipe!, scrape, talents, profession).GradePermille,
            EngineeringAssemblyInput assembly => EngineeringAssemblyScorer.Score(recipe!, assembly, talents, profession).GradePermille,
            _ => action.PerformanceGrade,
        };

        var performanceGrade = forgeScore?.GradePermille ?? echoGrade ?? puzzleGrade;
        var traceSink = events as ITraceSink;
        var quality = profession.ActiveCraft
            ? QualityRoller.RollActive(recipe, materialGrade, talents, profession.Quality, rng, performanceGrade, traceSink)
            : QualityRoller.Roll(recipe, materialGrade, talents, profession.Quality, rng, performanceGrade, traceSink);
        var itemId = new ItemId(state.NextItemId);
        // Sub-scores: the Anvil-Map scorer's three zone scores when hand-forged (Wave 5), else the
        // action's Godot-captured sub-scores (legacy/passive), else empty (auto-craft).
        var subScores = forgeScore?.SubScores ?? action.SubScores;
        var item = ItemForge.Forge(itemId, recipe, quality, state.Day, subScores);

        // Phase C U-C1 slice 2: stamp the player's requested craft modifiers, each validated against
        // the finished grade + material (slot count, tier cap, family exclusivity). Invalid or
        // over-budget requests are silently dropped — the forge does its best with what the grade
        // allows, never failing the craft. Pure, no RNG (CraftModifiers is a static integer table),
        // so the draw-count contract is untouched; the idle BaselinePlayer requests nothing.
        item = ApplyRequestedModifiers(item, action, quality);

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

        // Wave 5 (U23e): a hand-forge (re)seeds the echo memory at this grade; a consumed echo
        // advances its use count; anything else keeps the prior memory (it goes stale on its own
        // when the day or recipe next changes, via the match check above).
        var nextEcho = forgeScore is { } fscore
            ? new BatchEchoState(recipe.RecipeId, state.Day, fscore.GradePermille, 0)
            : echoGrade is not null
                ? echo! with { Uses = echo.Uses + 1 }
                : state.Player.BatchEcho;

        var newState = state with
        {
            NextItemId = state.NextItemId + 1,
            Items = state.Items.Add(itemId.Value, item),
            Player = state.Player with
            {
                Materials = state.Player.Materials.SetItem(action.MaterialKey, have - needed),
                BatchEcho = nextEcho,
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

    /// <summary>
    /// Phase C U-C1 slice 2: stamp the player's requested craft modifiers onto <paramref name="item"/>.
    /// Requests fill in family order (oil, rune, fitting); each is assigned the material-tier-capped
    /// tier, with a single +1 potency overshoot spent on the FIRST modifier of a masterwork (Hades
    /// "S" bonus). Each candidate is validated by <see cref="CraftModifiers.CanApply"/> against the
    /// grade's slot count, the material tier cap, and family exclusivity; anything that doesn't fit is
    /// silently dropped. Pure integer/data — no RNG, no clock.
    /// </summary>
    private static Item ApplyRequestedModifiers(Item item, CraftAction action, QualityGrade grade)
    {
        var requests = new (string? Id, ModifierFamily Family)[]
        {
            (action.RequestQuenchOil, ModifierFamily.QuenchOil),
            (action.RequestRune, ModifierFamily.Rune),
            (action.RequestFitting, ModifierFamily.Fitting),
        };

        var applied = new System.Collections.Generic.List<CraftModifier>();
        var baseTier = CraftModifiers.MaterialTierCap(action.MaterialKey);
        var overshootAvailable = CraftModifiers.MasterworkPotencyStep(grade);

        foreach (var (id, family) in requests)
        {
            if (id is null)
            {
                continue;
            }

            var tier = baseTier + (overshootAvailable ? 1 : 0);
            var candidate = new CraftModifier(id, family, tier);
            if (!CraftModifiers.CanApply(candidate, grade, action.MaterialKey, applied))
            {
                continue;
            }

            applied.Add(candidate);
            overshootAvailable = false; // the masterwork overshoot is spent on the first fitted modifier
            item = family switch
            {
                ModifierFamily.QuenchOil => item with { QuenchOil = candidate },
                ModifierFamily.Rune => item with { Rune = candidate },
                ModifierFamily.Fitting => item with { Fitting = candidate },
                _ => item,
            };
        }

        return item;
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

        // U-T1-9: the two smithing-tier gate nodes also require the workshop to already be at
        // the matching Forge Tier (TalentTree.ForgeTierRequirement) — a real, already-shipped
        // gold+ore sink, resolved through ForgeTierHandlers' own current-tier accessor. Every
        // other node has no entry in the map and skips this check entirely.
        if (TalentTree.ForgeTierRequirement.TryGetValue(action.NodeId, out var requiredTierIndex))
        {
            var tierIndex = ForgeTierHandlers.CurrentTierIndex(state.Player);
            if (tierIndex < requiredTierIndex)
            {
                return (state, new RejectedAction(action,
                    $"Talent '{action.NodeId}' requires Forge Tier {requiredTierIndex + 1} or higher (workshop is Tier {tierIndex + 1})."));
            }
        }

        // Day action-budget gate — checked LAST, after every other precondition, same order as
        // every other real-work handler (CraftAction guard 7 above, ForgeTierHandlers, ...): an
        // unknown node / missing prereq / unmet Forge Tier keeps its own rejection reason even on
        // a slot-exhausted day; only a genuinely legal unlock with zero slots left is refused here.
        if (state.ActionSlotsRemaining <= 0)
        {
            return (state, new RejectedAction(action, $"No action slots left today (0/{ActionBudget.SlotsPerDay}) — 'next' to advance."));
        }

        var newState = state with
        {
            Player = state.Player.WithTalent(action.Profession, action.NodeId),
            ActionSlotsRemaining = state.ActionSlotsRemaining - 1,
        };
        return (newState, null);
    }
}
