using System.Collections.Immutable;
using GameSim.Advisor;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Professions;

namespace GameSim.Harness;

/// <summary>
/// 2026-09-03 owner ruling: closes a structural blind spot found while investigating #686
/// (<c>BatchEchoFloor</c> tracked a stale <see cref="Crafting.QualityRoller"/> constant for two
/// weeks and nothing caught it). <see cref="Crafting.CraftingHandlers"/>'s hand-forge branch only
/// ever fires when a <see cref="CraftAction"/> carries a <see cref="ForgeTraceInput"/> — and
/// (grepped and confirmed before writing this policy) NONE of <see cref="BaselinePlayer"/>,
/// <see cref="MasterworkSeekingPlayer"/>, <see cref="ApprenticePlayer"/>, or
/// <see cref="SkilledSmithPlayer"/> ever construct one: every existing policy either auto-crafts
/// (<c>Puzzle</c> null) or stamps a raw <see cref="CraftAction.PerformanceGrade"/>
/// (<see cref="SkilledSmithPlayer"/>), which reaches quality through a completely different branch
/// of <c>CraftingHandlers.ApplyCraft</c> and never touches <see cref="Crafting.ForgeScorer"/>,
/// <see cref="Crafting.ForgeMoment"/>, or the batch-echo memory at all. So the 100-day balance
/// gate, the idle golden trace, and the determinism suite have NEVER exercised the code path a
/// real player's Anvil-Map minigame result rides in on — this is that instrument.
///
/// <para><b>Composition, not a fork (KTD1) — the <see cref="SkilledSmithPlayer"/> precedent.</b>
/// Every action <see cref="BaselinePlayer.ActionsFor"/> would return rides through UNCHANGED. The
/// only thing this policy ever adds or replaces is the single <see cref="CraftAction"/>
/// <see cref="BaselinePlayer"/>'s Expedition branch may emit (it never emits more than one — "one
/// craft per window keeps the policy simple and stable," that type's own comment) — this policy
/// never re-derives which recipe to craft, never re-derives <see cref="ActionLegality"/>'s rules.</para>
///
/// <para><b>"Average human forge performance" — the design call this unit exists to pin.</b> A
/// <see cref="ForgeTraceInput"/> is a captured minigame trace: a cursor path (<c>Samples</c>) plus
/// hammer strikes (<c>Strikes</c>), both scored against the deterministic target line
/// <see cref="ForgePath"/> regenerates for the recipe. <see cref="AverageDeviationPermille"/> is
/// the SAME constant per-mille tracking error applied at every sample and every strike's tempo —
/// not zero (a flawless trace, which is what the existing <c>PerfectTrace</c> test helpers in
/// <c>BatchEchoTests</c>/<c>ForgeTraceCraftTests</c> already cover) and not noisy/random (this
/// harness draws no RNG of its own — the determinism contract below). <see cref="Crafting.ForgeScorer"/>'s
/// own deviation-to-score slope is linear (score = 1000 − deviation×4, per that type's
/// <c>DevScale</c>), so a constant 50 per-mille deviation nets a flat 800 per-mille grade before
/// any talent assist — <b>chosen deliberately to equal <see cref="Crafting.QualityRoller"/>'s own
/// private <c>AutoCraftGrade</c> constant (800, the shop's own safe auto-craft baseline)</b>. That
/// is an equivalence, not a coincidence: an average player's hand-forge should buy no SYSTEMATIC
/// quality edge over just letting the counter auto-craft run — the same "safe, unremarkable" grade
/// auto-craft already produces — while still opening the two doors auto-craft is structurally
/// barred from (<c>RollActive</c>'s own doc comment: auto-craft hard-caps at Superior, and only a
/// captured minigame grade can ever reach Masterwork) plus the forge <see cref="Crafting.ForgeMoment"/>
/// history entry and Signed Works eligibility only a real trace ever earns. Once blacksmith
/// talents unlock (Keen Eye / Master's Touch / Legendary Craft / Weapon Specialist), their
/// <c>MinigameAssist</c> forgiveness reduces the EFFECTIVE deviation for this SAME raw trace, so
/// identical "average" accuracy scores progressively better as the campaign's talent tree fills
/// in — the same hand, a widening safety net, exactly the shape a real player's talent investment
/// is supposed to buy.
///
/// <para><b>Measured, not just argued (the 20-seed/100-day sweep this unit's PR reports): the
/// 800-per-mille figure above is the honest zero-talent grade (pinned exactly by this type's own
/// unit tests) but is NOT what a real 100-day campaign actually produces.</b> Blacksmith's own
/// <c>MinigameAssist</c> table (<see cref="Professions.ProfessionRegistry.Blacksmith"/>) grants
/// Master's Touch a 70 per-mille <c>DriftRateReduction</c> (applies to every sample, all three
/// zones) and Legendary Craft an 80 per-mille <c>OffBeatForgiveness</c> (applies to every strike)
/// — EACH individually exceeds this policy's 50 per-mille <see cref="AverageDeviationPermille"/>
/// on its own axis, so once BOTH are unlocked, EVERY sample and EVERY strike's effective
/// deviation clamps to zero and the grade saturates at the hard ceiling, 1000 — not 800. Composed
/// on top of <see cref="BaselinePlayer"/>'s own Morning talent-unlock loop (one prereq-order node
/// per morning, unconditionally, starting day one), the measured sweep shows EVERY one of 743
/// hand-forges across all 20 seeds landed at SeedGrade exactly 1000 (min = max = mean = 1000,
/// zero exceptions) — by the time this policy's craft loop first finds a buyer and a legal
/// hand-forge, both assist talents are already unlocked in every seed. So in practice this
/// instrument measures the CEILING, not a genuinely middling hand — see this unit's PR
/// description for the full accounting; a second, WORSE competence profile
/// (<see cref="SmithSkill"/>'s Novice/Veteran pair is the precedent for that shape on the OTHER,
/// raw-<c>PerformanceGrade</c>-stamping style of policy) is the honest way to actually measure a
/// struggling hand under this same talent schedule, and is a motivated follow-up this PR
/// deliberately leaves for a future unit rather than silently re-tuning the constant to chase a
/// moving talent-unlock target.</para>
///
/// <para>Batch-echo floor consequence of the above (measured, not assumed): with every SeedGrade
/// at 1000, <c>CraftingHandlers</c>' decay formula (floor 800, 80‰/use) never floors the FIRST or
/// SECOND echo of a day (1000−80=920, 1000−160=840, both above the floor) — it first floors the
/// THIRD echo (1000−240=760, clamped to 800) and every one after. The corpus's own PR description
/// reports the measured floor-hit rate against this.</para>
///
/// <para><b>Batch-echo coverage.</b> <c>CraftingHandlers</c>' batch-echo memory only ever seeds off
/// a hand-forge and only ever pays out on a LATER, IDENTICAL, SAME-DAY auto-craft — the "set the
/// rhythm by hand once, the copies follow" pattern that type's own U23e comment describes. No
/// existing policy can ever reach that: every one of them submits at most one craft per day. So
/// after hand-forging the day's chosen recipe, this policy also submits further plain (puzzle-less)
/// auto-craft copies of the SAME recipe/material while the day's shared action-slot budget allows —
/// the only way a real sweep can ever produce a batch-echo measurement at all. Slots are tracked
/// locally (mirroring <see cref="BaselinePlayer"/>'s own Evening ore-buying loop, which tracks gold
/// the identical way for the identical reason: <see cref="ActionLegality.IsLegal"/> only ever sees
/// the state as of the START of this tick, and <c>GameKernel.Tick</c> applies this whole returned
/// list in order within that one call). Material sufficiency is intentionally NOT tracked the same
/// way — re-deriving <c>CraftingHandlers</c>' material-efficiency-discount formula a fourth time is
/// exactly the kind of hand-rolled duplicate that produced the 90%-of-crafts-rejected bug
/// <see cref="BaselinePlayer"/>'s own class doc recounts fixing. An echo copy proposed past the
/// point real cumulative stock supports is simply REJECTED by the kernel once actually applied —
/// harmless (a rejection never mutates state, the same precedent <see cref="BaselinePlayer"/>'s
/// Evening loop already relies on) and visible in the run's own rejected-action log rather than
/// hidden behind a second, possibly-wrong, hand-rolled quantity check here.</para>
///
/// <para>Pure: no RNG of its own (<see cref="BuildTrace"/> is a function of the
/// <see cref="Crafting.Recipe"/> alone — the same deterministic target line
/// <see cref="ForgePath.Generate"/> always regenerates for that recipe — every echo copy is a
/// plain auto-craft with no captured performance at all), no IO, no wall clock.</para>
/// </summary>
public static class HandForgePlayer
{
    /// <summary>The Anvil-Map path variant this policy always forges against. Fixed rather than
    /// state-derived: a real player sees many path seeds over a campaign, but this instrument only
    /// needs ONE to prove the hand-forge code path is exercised at all — see this type's class doc.</summary>
    private const int PathSeed = 1;

    /// <summary>"Average human forge performance," pinned as a per-mille tracking/tempo deviation
    /// from the ideal line — see this type's class doc for the full reasoning and the arithmetic
    /// that ties it to <see cref="Crafting.QualityRoller"/>'s own <c>AutoCraftGrade</c> (800).</summary>
    private const int AverageDeviationPermille = 50;

    /// <summary>The three hammer-strike x-positions this policy always swings at — the same
    /// x-positions the existing <c>PerfectStrikes</c> test fixtures in <c>BatchEchoTests</c>/
    /// <c>ForgeTraceCraftTests</c> already use, just with <see cref="AverageDeviationPermille"/>'s
    /// tempo error instead of a perfect (zero-error) swing.</summary>
    private static readonly ImmutableList<int> AverageStrikes = ImmutableList.Create(
        400, AverageDeviationPermille,
        500, AverageDeviationPermille,
        600, AverageDeviationPermille);

    public static ImmutableList<PlayerAction> ActionsFor(GameState state)
    {
        var baseline = BaselinePlayer.ActionsFor(state);
        if (state.Phase != DayPhase.Expedition)
        {
            return baseline; // BaselinePlayer never crafts outside Expedition (D5) — nothing to hand-forge.
        }

        // BaselinePlayer's Expedition branch emits at most one CraftAction. Anything else (empty,
        // or — defensively — a shape that ever changed) rides through unmodified: this policy only
        // ever touches a CraftAction for the blacksmith's own active-craft recipes (the only
        // profession ForgeTraceInput is valid for — CraftingHandlers.ApplyCraft guard 6).
        if (baseline.Count != 1
            || baseline[0] is not CraftAction craft
            || !RecipeTable.All.TryGetValue(craft.RecipeId, out var recipe)
            || recipe.Profession != ProfessionRegistry.BlacksmithId)
        {
            return baseline;
        }

        var actions = ImmutableList.CreateBuilder<PlayerAction>();
        actions.Add(craft with { Puzzle = BuildTrace(recipe) });

        // Batch-echo coverage (see class doc): submit further identical, puzzle-less auto-crafts
        // while the day's shared action-slot budget allows. Legality is asked ONCE, up front,
        // against the pre-tick state (the same snapshot every one of these copies would see, since
        // none of them has actually been applied yet) — never re-checked per copy against a stale
        // snapshot that can't see its own prior iterations, and never re-derived by hand.
        var remainingSlots = state.ActionSlotsRemaining - 1;
        var echoCopy = new CraftAction(craft.RecipeId, craft.MaterialKey);
        if (remainingSlots > 0 && ActionLegality.IsLegal(state, echoCopy, state.Phase))
        {
            for (var i = 0; i < remainingSlots; i++)
            {
                actions.Add(echoCopy);
            }
        }

        return actions.ToImmutable();
    }

    /// <summary>
    /// Builds the "average" hand-forge trace for <paramref name="recipe"/>: the recipe's own
    /// deterministic target polyline (<see cref="ForgePath.Generate"/>), reusing that path's own
    /// vertices as the captured samples — the same minimal-trace shape the existing
    /// <c>PerfectTrace</c> test helpers already use, just offset by
    /// <see cref="AverageDeviationPermille"/> at every vertex instead of matching it exactly. The
    /// offset direction (running a little hot) is arbitrary — <see cref="Crafting.ForgeScorer"/>
    /// only ever scores the absolute deviation — chosen positive purely for concreteness.
    /// </summary>
    private static ForgeTraceInput BuildTrace(Recipe recipe)
    {
        var path = ForgePath.Generate(recipe.Tier, recipe.Slot, recipe.BaseStats.Weight, PathSeed);

        var samples = ImmutableList.CreateBuilder<int>();
        for (var i = 0; i < path.Count; i += 2)
        {
            samples.Add(path[i]);
            samples.Add(Math.Clamp(path[i + 1] + AverageDeviationPermille, 0, 1000));
        }

        return new ForgeTraceInput(samples.ToImmutable(), AverageStrikes, PathSeed);
    }
}
