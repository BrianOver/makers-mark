using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Crafting;

namespace GameSim.Professions;

/// <summary>The scorer's verdict for one brew, all per-mille integers: <paramref name="GradePermille"/>
/// is the [0..1000] craft-performance grade fed to the active dominance roll (same scale as the
/// blacksmith's <c>CraftAction.PerformanceGrade</c>); <paramref name="ExactPermille"/> /
/// <paramref name="PlacedPermille"/> are the two legible sub-axes (right reagent in the right
/// position / right reagent anywhere) the presentation layer may show as flavor.</summary>
public sealed record AlchemyBrewScore(int GradePermille, int ExactPermille, int PlacedPermille);

/// <summary>
/// Phase B in-sim puzzle scorer (PKD1 dual-mode seam): maps an <see cref="AlchemyReagentPuzzle"/>
/// to a per-mille grade the crafting pipeline feeds into <c>QualityRoller.RollActive</c> — the sim
/// scores the alchemist, Godot only PRESENTS the puzzle. PURE integer math: no RNG, no wall
/// clock, no <c>Math.*</c> transcendentals (KTD2/KTD4) — same puzzle in, same grade out, forever,
/// on every OS.
///
/// <para><b>The rule (simple and legible):</b> every alchemy recipe has an ideal reagent sequence
/// (<see cref="IdealSequenceFor"/>). Each submitted pour earns 2 points for the right reagent in
/// the right position, 1 point for a right reagent in the wrong position (multiset-aware — an
/// ideal slot is consumed once, so spamming one reagent can't farm partial credit), 0 otherwise.
/// Those points then report through the shared <see cref="Crafting.CraftCurve"/> (P2-OQ11, owner
/// ruling 2026-09-04): the INDIFFERENT pour — every reagent the recipe calls for, not one of them
/// in its place — anchors to the middle of Common, and only a flawless pour reaches Masterwork.
/// The skill this craft tests (remembering the order) is untouched; what changed is only where a
/// given accuracy lands on the shared band table, so that Alchemy answers to the same
/// skill-to-outcome response as the other three crafts. See <see cref="Crafting.CraftCurve"/>'s own
/// doc for the measured defect that motivated it.</para>
///
/// <para><b>Granularity is honest and coarse at tier 1.</b> A 3-pour recipe has only a handful of
/// reachable point totals (any single-slot mistake costs 2 of the 6 points, and 5 is unreachable
/// with distinct reagents), so an untalented tier-1 brew steps Common -> Fine -> Masterwork without
/// passing through Superior. That is a property of a 3-slot memory puzzle, not of the curve —
/// tier-2 and tier-3 brews (4 and 5 pours) walk every band, and any unlocked assist fills the gap
/// at tier 1 too.</para>
///
/// <para><b>Talent assists:</b> the retired quality-shift nodes live on as
/// <see cref="ProfessionDefinition.MinigameAssists"/> data (PA2/PKD3 pattern). For a puzzle-scored
/// profession the SIM is the "adapter" that consumes them: each unlocked node's three per-mille
/// fields sum into a flat forgiveness bonus added AFTER the curve (clamped at 1000) — so mastery
/// raises the floor without ever flattening the slope, the same shape §11.7.11 settled on for the
/// forge. Potent Brews is Consumable-scoped — the same slot-scoping the blacksmith's Weapon
/// Specialist gets from the forge overlay (see <c>ForgeMinigame.AggregateAssist</c>). A fully
/// talented alchemist still needs a genuinely good pour to reach Masterwork: an indifferent one
/// grades Fine.</para>
/// </summary>
public static class AlchemyPuzzleScorer
{
    /// <summary>Points for the right reagent in the right position.</summary>
    private const int ExactPoints = 2;

    /// <summary>Points for a right reagent in the wrong position.</summary>
    private const int MisplacedPoints = 1;

    /// <summary>
    /// The ideal pour order per recipe — constant, legible DATA (t1 recipes take 3 pours,
    /// t2 take 4, t3 take 5; a recipe may call for the same reagent twice). Keys cover every
    /// <see cref="AlchemyProfession"/> recipe; <see cref="IdealSequenceFor"/> derives a
    /// deterministic fallback for any future recipe not yet listed here.
    /// </summary>
    private static readonly ImmutableSortedDictionary<string, ImmutableList<int>> IdealSequences = new Dictionary<string, ImmutableList<int>>
    {
        ["alchemy-minor-elixir"] = ImmutableList.Create(AlchemyReagents.Sunpetal, AlchemyReagents.Dewroot, AlchemyReagents.Glimmercap),
        ["alchemy-healing-draught"] = ImmutableList.Create(AlchemyReagents.Dewroot, AlchemyReagents.Sunpetal, AlchemyReagents.Ironmoss),
        ["alchemy-alchemical-robe"] = ImmutableList.Create(AlchemyReagents.Ironmoss, AlchemyReagents.Cinderbark, AlchemyReagents.Dewroot),
        ["alchemy-quicksilver-charm"] = ImmutableList.Create(AlchemyReagents.Glimmercap, AlchemyReagents.Ironmoss, AlchemyReagents.Voidsalt),
        ["alchemy-transmuters-tonic"] = ImmutableList.Create(AlchemyReagents.Cinderbark, AlchemyReagents.Glimmercap, AlchemyReagents.Sunpetal, AlchemyReagents.Voidsalt),
        ["alchemy-greater-elixir"] = ImmutableList.Create(AlchemyReagents.Sunpetal, AlchemyReagents.Dewroot, AlchemyReagents.Glimmercap, AlchemyReagents.Sunpetal),
        ["alchemy-panacea"] = ImmutableList.Create(AlchemyReagents.Sunpetal, AlchemyReagents.Dewroot, AlchemyReagents.Glimmercap, AlchemyReagents.Ironmoss, AlchemyReagents.Voidsalt),
        ["alchemy-philosophers-stone"] = ImmutableList.Create(AlchemyReagents.Voidsalt, AlchemyReagents.Cinderbark, AlchemyReagents.Ironmoss, AlchemyReagents.Glimmercap, AlchemyReagents.Voidsalt),
    }.ToImmutableSortedDictionary(StringComparer.Ordinal);

    /// <summary>
    /// The ideal pour order for a recipe. Every current alchemy recipe is listed explicitly in
    /// <see cref="IdealSequences"/>; an unlisted (future) recipe gets a deterministic fallback
    /// derived from its id and tier (ordinal char sum — pure integer math, stable across OSes),
    /// so registering a new recipe can never throw here.
    /// </summary>
    public static ImmutableList<int> IdealSequenceFor(Recipe recipe)
    {
        if (IdealSequences.TryGetValue(recipe.RecipeId, out var sequence))
        {
            return sequence;
        }

        var charSum = 0;
        foreach (var c in recipe.RecipeId)
        {
            charSum += c;
        }

        var length = recipe.Tier + 2;
        if (length < 3)
        {
            length = 3;
        }

        if (length > 5)
        {
            length = 5;
        }

        var builder = ImmutableList.CreateBuilder<int>();
        for (var i = 0; i < length; i++)
        {
            builder.Add((charSum + i * (recipe.Tier + 1)) % AlchemyReagents.Count);
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Score one brew. Pure and total: any puzzle value (null/empty/overlong list, unknown
    /// reagent ids) maps to a grade in [0, 1000] — never a throw, never an RNG draw.
    /// </summary>
    public static AlchemyBrewScore Score(
        Recipe recipe, AlchemyReagentPuzzle puzzle, ImmutableSortedSet<string> unlockedTalents, ProfessionDefinition profession)
    {
        var ideal = IdealSequenceFor(recipe);
        var length = ideal.Count;
        var poured = puzzle.Reagents ?? ImmutableList<int>.Empty;
        var considered = poured.Count < length ? poured.Count : length;

        // Pass 1 — exact positional matches consume their ideal slot.
        var consumed = new bool[length];
        var exact = 0;
        for (var i = 0; i < considered; i++)
        {
            if (poured[i] == ideal[i])
            {
                consumed[i] = true;
                exact++;
            }
        }

        // Pass 2 — misplaced-but-called-for reagents consume a remaining ideal slot each
        // (multiset-aware: partial credit is capped by how many of that reagent the recipe
        // actually calls for).
        var misplaced = 0;
        for (var i = 0; i < considered; i++)
        {
            if (poured[i] == ideal[i])
            {
                continue;
            }

            for (var j = 0; j < length; j++)
            {
                if (!consumed[j] && ideal[j] == poured[i])
                {
                    consumed[j] = true;
                    misplaced++;
                    break;
                }
            }
        }

        var points = exact * ExactPoints + misplaced * MisplacedPoints;

        // P2-OQ11: the shared curve, not a bare points/max fraction (see CraftCurve's class doc for
        // the measured defect this replaced). The indifferent pour — every reagent the recipe calls
        // for, not one of them in its place — is worth MisplacedPoints per pour, and that is what
        // anchors to the middle of Common; a flawless pour is ExactPoints per pour and earns
        // Masterwork. Assist is added to the RESULT, so mastery raises the floor without ever
        // flattening the slope.
        var basePermille = CraftCurve.GradeFor(
            points, MisplacedPoints * length, ExactPoints * length);
        var grade = basePermille + AssistBonusPermille(profession, unlockedTalents, recipe.Slot);
        if (grade > 1000)
        {
            grade = 1000;
        }

        return new AlchemyBrewScore(grade, exact * 1000 / length, (exact + misplaced) * 1000 / length);
    }

    /// <summary>
    /// Sums every unlocked talent's <see cref="MinigameAssist"/> triple into one flat per-mille
    /// forgiveness bonus. Potent Brews (<see cref="AlchemyProfession.PotentBrews"/>) is
    /// Consumable-recipe-scoped, mirroring the retired <c>SlotShift</c> semantics exactly the way
    /// the forge overlay scopes Weapon Specialist to weapons.
    /// </summary>
    private static int AssistBonusPermille(
        ProfessionDefinition profession, ImmutableSortedSet<string> unlockedTalents, ItemSlot recipeSlot)
    {
        var bonus = 0;
        foreach (var (nodeId, assist) in profession.MinigameAssists)
        {
            if (!unlockedTalents.Contains(nodeId))
            {
                continue;
            }

            if (nodeId == AlchemyProfession.PotentBrews && recipeSlot != ItemSlot.Consumable)
            {
                continue;
            }

            bonus += assist.SweetZoneWidthBonus + assist.DriftRateReduction + assist.OffBeatForgiveness;
        }

        return bonus;
    }
}
