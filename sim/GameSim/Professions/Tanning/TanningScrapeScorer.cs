using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Crafting;

namespace GameSim.Professions;

/// <summary>The tanner's scrape result: the dominance grade plus the two legible sub-readings
/// (how much of the hide was worked at all, and how much of it was ruined by overworking).</summary>
public sealed record TanningScrapeScore(int GradePermille, int CoveragePermille, int RuinPermille);

/// <summary>
/// U8 (plan <c>2026-07-28-002</c>): scores a <see cref="TanningScrapeInput"/>. The skill being tested
/// is COVERAGE WITH RESTRAINT — clean the whole hide, but do not scrape any spot through. That makes
/// it the deliberate opposite of the forge (timing) and the brew (memory): there is no clock here at
/// all, and the player may stop and think mid-stroke.
///
/// <para><b>Pure, total, integer-only</b> (the same contract <see cref="AlchemyPuzzleScorer"/>
/// honours): any input — null, empty, short, overlong, negative counts — maps to a grade in
/// [0, 1000], never a throw and never an RNG draw. Cell "patches" are regenerated from the input's
/// own <see cref="TanningScrapeInput.PatchSeed"/> by integer mixing, so the sim and the overlay agree
/// on which cells were special without either one rolling dice.</para>
///
/// <para><b>Cell kinds and their ideal work.</b> A plain cell wants 1-2 passes. A <i>flaw</i> patch is
/// stubborn and wants 3-4. A <i>thin</i> patch tolerates exactly 1. Anything beyond a cell's tolerance
/// wears through: that cell scores nothing and counts toward
/// <see cref="TanningScrapeScore.RuinPermille"/>, the legible sub-axis the overlay shows. Leather
/// with holes is still leather, just poorer — a botched hide never voids the craft (the same
/// partial-credit stance as the brew).</para>
///
/// <para><b>Restraint costs more than it used to, and via one channel instead of two</b> (P2-OQ11).
/// Scraping a cell through used to forfeit its points AND dock a separate 12-per-mille grade
/// penalty — together about 2.4% of the scale, which is to say nothing at all. It now costs only
/// the forfeited points, but because the shared <see cref="CraftCurve"/> spends the whole climb from
/// Common to Masterwork across this hide's last five points, forfeiting a cell is worth roughly a
/// band and a half. One channel, and it finally bites; the second was deleted rather than retuned
/// because two knobs pointed at one idea is how the first one came to be worth nothing.</para>
/// </summary>
public static class TanningScrapeScorer
{
    /// <summary>Hide grid width in cells — shared with the overlay so both agree on the layout.</summary>
    public const int Columns = 8;

    /// <summary>Hide grid height in cells.</summary>
    public const int Rows = 5;

    /// <summary>Total cells on the frame.</summary>
    public const int CellCount = Columns * Rows;

    /// <summary>Points a perfectly-worked cell earns.</summary>
    private const int CellPoints = 2;

    /// <summary>Points for a flaw patch that was worked, but not enough.</summary>
    private const int PartialPoints = 1;

    /// <summary>How many cells hide a stubborn flaw, and how many run thin.</summary>
    private const int FlawPatches = 5;
    private const int ThinPatches = 4;

    /// <summary>
    /// What the INDIFFERENT hand earns — the calibration <see cref="CraftCurve"/> anchors to the
    /// middle of Common. That hand is "one pass over every cell, never varying": it lands inside
    /// the 1-2 band on all <c>CellCount - FlawPatches</c> plain-or-thin cells for full credit, and
    /// leaves every stubborn flaw patch under-worked for <see cref="PartialPoints"/> each. Derived
    /// from the constants above rather than written as a literal, so changing the patch mix or the
    /// pass bands can never silently leave this calibration behind (the failure mode
    /// <c>BatchEchoFloor</c> hit — see <c>HandForgePlayer</c>'s class doc).
    ///
    /// <para>A uniform hand cannot do better than this: two passes everywhere scrapes all four
    /// thin cells through, and three ruins the plain majority as well.</para>
    /// </summary>
    private const int IndifferentHandPoints =
        ((CellCount - FlawPatches) * CellPoints) + (FlawPatches * PartialPoints);

    /// <summary>What a given cell wants.</summary>
    public enum CellKind
    {
        /// <summary>Wants 1-2 passes.</summary>
        Plain,

        /// <summary>Stubborn: wants 3-4 passes.</summary>
        Flaw,

        /// <summary>Delicate: tolerates exactly 1 pass.</summary>
        Thin,
    }

    /// <summary>
    /// Which cells are flaws and which run thin, derived by pure integer mixing from
    /// <paramref name="patchSeed"/>. Public so the overlay can render the SAME patches the scorer
    /// will grade against — the tanning equivalent of the forge's shared target line.
    /// </summary>
    public static ImmutableArray<CellKind> PatchesFor(int patchSeed)
    {
        var kinds = new CellKind[CellCount];

        // A small integer LCG walk (no RNG service, no floats) — deterministic across OSes and
        // identical wherever it is called from.
        var cursor = patchSeed;
        var placed = 0;
        var guard = 0;
        while (placed < FlawPatches && guard++ < CellCount * 8)
        {
            cursor = unchecked(cursor * 1103515245 + 12345);
            var index = ((cursor >> 8) % CellCount + CellCount) % CellCount;
            if (kinds[index] == CellKind.Plain)
            {
                kinds[index] = CellKind.Flaw;
                placed++;
            }
        }

        placed = 0;
        guard = 0;
        while (placed < ThinPatches && guard++ < CellCount * 8)
        {
            cursor = unchecked(cursor * 1103515245 + 12345);
            var index = ((cursor >> 8) % CellCount + CellCount) % CellCount;
            if (kinds[index] == CellKind.Plain)
            {
                kinds[index] = CellKind.Thin;
                placed++;
            }
        }

        return ImmutableArray.Create(kinds);
    }

    /// <summary>The pass count a cell of this kind is aiming for (inclusive band).</summary>
    public static (int Min, int Max) IdealPassesFor(CellKind kind) => kind switch
    {
        CellKind.Flaw => (3, 4),
        CellKind.Thin => (1, 1),
        _ => (1, 2),
    };

    /// <summary>
    /// Score one hide. Pure and total: every input maps to a grade in [0, 1000].
    /// </summary>
    public static TanningScrapeScore Score(
        Recipe recipe, TanningScrapeInput puzzle, ImmutableSortedSet<string> unlockedTalents, ProfessionDefinition profession)
    {
        var passes = puzzle.CellPasses ?? ImmutableList<int>.Empty;
        var kinds = PatchesFor(puzzle.PatchSeed);

        var points = 0;
        var worked = 0;
        var ruined = 0;

        for (var i = 0; i < CellCount; i++)
        {
            var count = i < passes.Count ? passes[i] : 0;
            if (count < 0)
            {
                count = 0;   // a malformed negative reads as "untouched", never as credit
            }

            if (count > 0)
            {
                worked++;
            }

            var (min, max) = IdealPassesFor(kinds[i]);
            if (count >= min && count <= max)
            {
                points += CellPoints;
            }
            else if (count > max)
            {
                ruined++;    // scraped through — no credit, and it docks the grade below
            }
            else if (count > 0 && kinds[i] == CellKind.Flaw)
            {
                points += PartialPoints;   // worked the stubborn patch, just not enough
            }
        }

        // P2-OQ11: the shared curve (see CraftCurve's class doc). Tanning is the profession the
        // measurement caught red-handed — 87.3% Masterwork from day 6 — because its INDIFFERENT
        // hand was nearly a perfect score: one unvarying pass over the whole hide satisfies the
        // 31 plain cells AND all 4 thin cells outright, so 75 of the 80 points were free and the
        // grade started at 937 before anyone looked at the hide. Anchoring that same hand to the
        // middle of Common is the fix, and it makes the craft's own stated skill — find the
        // stubborn patches, work them, and scrape nothing through — the whole of the climb from
        // Common to Masterwork.
        var basePermille = CraftCurve.GradeFor(points, IndifferentHandPoints, CellPoints * CellCount);
        var grade = basePermille + AssistBonusPermille(profession, unlockedTalents, recipe.Slot);
        if (grade < 0)
        {
            grade = 0;
        }

        if (grade > 1000)
        {
            grade = 1000;
        }

        return new TanningScrapeScore(grade, worked * 1000 / CellCount, ruined * 1000 / CellCount);
    }

    /// <summary>
    /// Sums every unlocked talent's <see cref="MinigameAssist"/> triple into one flat forgiveness
    /// bonus — the same "talents are earned accessibility" channel the brew scorer uses (U3b).
    /// <see cref="TanningProfession.Armorer"/> is Armor-recipe-scoped, mirroring the retired
    /// <c>SlotShift</c> semantics exactly the way <see cref="AlchemyPuzzleScorer"/> scopes Potent
    /// Brews to Consumable recipes. Deliberately does NOT re-read the profession's quality-shift
    /// talents, which already apply in <c>QualityRoller</c> — counting them twice would silently
    /// buff the profession.
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

            if (nodeId == TanningProfession.Armorer && recipeSlot != ItemSlot.Armor)
            {
                continue;
            }

            bonus += assist.SweetZoneWidthBonus + assist.DriftRateReduction + assist.OffBeatForgiveness;
        }

        return bonus;
    }
}
