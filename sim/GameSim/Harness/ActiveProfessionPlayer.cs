using System;
using System.Collections.Immutable;
using System.Linq;
using GameSim.Advisor;
using GameSim.Classes;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Economy;
using GameSim.Heroes;
using GameSim.Professions;

namespace GameSim.Harness;

/// <summary>
/// P2-OQ10 (2026-09-04 finding): closes, for Alchemy/Tanning/Engineering, the exact blind spot
/// <see cref="HandForgePlayer"/> closed for the blacksmith. Grepped and confirmed before writing
/// this file: EVERY existing scripted policy (<see cref="BaselinePlayer"/>, <see cref="CounterPlayer"/>,
/// <see cref="ApprenticePlayer"/>, <see cref="MasterworkSeekingPlayer"/>, <see cref="SkilledSmithPlayer"/>,
/// <see cref="HandForgePlayer"/>, <see cref="LateMasteryPlayer"/>) crafts EXCLUSIVELY off
/// <see cref="RecipeTable.All"/> — blacksmith's own recipe table — never
/// <see cref="ProfessionRegistry.AllRecipes"/>. So no policy has ever submitted a <see cref="CraftAction"/>
/// for an Alchemy/Tanning/Engineering recipe AT ALL, auto-craft or otherwise: a stronger gap than the
/// finding's own framing ("every crafted item ... takes the auto-craft path") — the corpus has ZERO
/// items from these three professions, not merely zero puzzle-scored ones. See this unit's PR body for
/// the measured grade distributions this file's sweep is the first to produce.
///
/// <para><b>Why this is not a composition over <see cref="BaselinePlayer"/> (the KTD1 precedent
/// <see cref="HandForgePlayer"/>/<see cref="LateMasteryPlayer"/> set, checked and rejected).</b> Those
/// two compose because blacksmith is ALWAYS the profession Baseline's own craft/talent/ore logic
/// assumes. That assumption cannot extend here: <see cref="ProfessionHandlers.MaxSelected"/> caps a
/// save at 1-2 selected professions, and — worse — <see cref="CraftingHandlers.ApplyUnlock"/> never
/// checks <see cref="PlayerState.IsSelected"/>, so composing over Baseline's Morning branch inside a
/// non-blacksmith save would silently spend one of the day's five scarce action slots unlocking
/// blacksmith talents nobody will ever use, starving the profession this instrument actually needs to
/// measure. This is therefore a fresh policy family, not a fork of Baseline's craft loop — but it
/// reuses every RULE Baseline already established (<see cref="ActionLegality.IsLegal"/>, never
/// re-derived; <see cref="MaterialVendorHandlers.QuoteCost"/>, the one pricing formula; the
/// shelf/commission housekeeping shape, which was already profession-agnostic) and invents nothing
/// Baseline's own 100-day corpus hasn't already proven sound for a single profession.</para>
///
/// <para><b>One engine, three thin wrappers — the shape the 2-profession cap forces.</b> A save can
/// practise at most two of Alchemy/Tanning/Engineering/Blacksmith at once, so no single sweep can
/// exercise all three new scorer families side by side without one profession crowding another's
/// action-slot budget and confounding the per-profession read. Each of the three wrappers below
/// (<see cref="AlchemyPuzzlePlayer"/>/<see cref="TanningPuzzlePlayer"/>/<see cref="EngineeringPuzzlePlayer"/>)
/// therefore drives a campaign that selected ITS OWN profession ALONE from day 1
/// (<see cref="GameComposition.NewCampaign(ulong,string)"/> — the existing single-starting-profession
/// seam Playable Core already built; <c>GameSim.Cli.BatchRunner</c> is the only caller that needs to
/// route a policy through it instead of the blacksmith-default overload). Each drives an identical
/// Morning/Expedition shape against its own profession — unlock one talent a morning (prereq order,
/// Baseline's own tie-break), keep the profession's OWN material topped up off the always-available
/// Morning vendor floor (<see cref="MaterialVendorHandlers"/> — never the Evening ore-offer path,
/// which is real but non-deterministic in WHICH key appears; the vendor is what Playable Core R2/R3
/// built for exactly this "day-1 reachable regardless of profession" case), accept every open gear
/// commission and shelve every unsold player craft (both free, both exactly Baseline's own generic
/// blocks — already profession-agnostic, nothing to adapt) — then hand-craft the best affordable
/// recipe with a REAL, profession-shaped puzzle input every Expedition window instead of auto-crafting.
/// <see cref="PuzzleCraftPlayer.ActionsFor"/> is the shared engine; the three wrappers supply only what
/// genuinely differs — the <see cref="ProfessionDefinition"/> and its puzzle builder.</para>
///
/// <para><b>The "average hand" per puzzle — <see cref="HandForgePlayer"/>'s own doctrine, ported.</b>
/// Each builder submits a constant, deterministic, DELIBERATELY IMPERFECT input: not a flawless solve
/// (already covered by each scorer's own unit tests) and not garbage (an average player still tries),
/// so the sweep can ask the same question <see cref="HandForgePlayer"/>/<see cref="LateMasteryPlayer"/>
/// asked of the forge — does a constant skill level keep mattering as talent-assist forgiveness stacks,
/// or does the grade saturate at the ceiling regardless of accuracy? See each wrapper's own doc for its
/// puzzle's specific, named "mistake."</para>
///
/// <para>Pure: no RNG of its own, no IO, no wall clock — every puzzle builder is a total function of
/// the recipe alone (mirroring <see cref="HandForgePlayer.BuildTrace"/>'s own contract), and the
/// engine draws nothing beyond what <see cref="ActionLegality"/>/<see cref="MaterialVendorHandlers.QuoteCost"/>
/// already compute.</para>
/// </summary>
internal static class PuzzleCraftPlayer
{
    /// <summary>Morning material top-up batch: bought whenever the profession's own stock of a
    /// still-needed key falls below the smallest recipe that spends it. Comfortably covers 2+ crafts
    /// at any tier (steel's worst case across the three professions is 5/craft) without hoarding —
    /// the same magnitude <see cref="GameFactory.StarterCopper"/>'s own comment reasons from.</summary>
    private const int MaterialBatchSize = 10;

    public static ImmutableList<PlayerAction> ActionsFor(
        GameState state, ProfessionDefinition profession, Func<Recipe, CraftPuzzleInput> buildPuzzle)
    {
        var actions = ImmutableList.CreateBuilder<PlayerAction>();

        switch (state.Phase)
        {
            case DayPhase.Morning:
                MorningActions(state, profession, actions);
                break;

            case DayPhase.Expedition:
                ExpeditionActions(state, profession, buildPuzzle, actions);
                break;

            case DayPhase.Camp:
            case DayPhase.ExpeditionDeep:
            case DayPhase.Evening:
                // D5: this policy holds at every other staged tick. The profession's material
                // top-up already happened Morning-side off the always-available vendor floor, so
                // Evening's returning-hero ore offers (a real channel, just not this instrument's
                // concern — see class doc) are left untouched, the same empty window
                // BaselinePlayer's own class doc describes for Camp/ExpeditionDeep.
                break;
        }

        return actions.ToImmutable();
    }

    private static void MorningActions(
        GameState state, ProfessionDefinition profession, ImmutableList<PlayerAction>.Builder actions)
    {
        var slotsLeft = state.ActionSlotsRemaining;
        var gold = state.Player.Gold;
        var talents = state.Player.TalentsFor(profession.Id);

        // Unlock one talent a morning, prereq order — Baseline/LateMasteryPlayer's own tie-break
        // (ordinal node id: deterministic and reviewable, no design meaning of its own).
        if (slotsLeft > 0)
        {
            var next = profession.TalentNodes.Values
                .Where(n => !talents.Contains(n.NodeId) && n.Prerequisites.All(talents.Contains))
                .OrderBy(n => n.NodeId, StringComparer.Ordinal)
                .Select(n => new UnlockTalentAction(n.NodeId, profession.Id))
                .FirstOrDefault(candidate => ActionLegality.IsLegal(state, candidate, state.Phase));
            if (next is not null)
            {
                actions.Add(next);
                slotsLeft--;
            }
        }

        // Accept every open GEAR commission — free, exactly BaselinePlayer's own block. Already
        // profession-agnostic: any player-crafted item can fulfil a slot/quality-matched commission,
        // regardless of which profession forged it.
        foreach (var commission in state.Commissions.Where(c => !c.Accepted && c.Slot != ItemSlot.Consumable))
        {
            var accept = new AcceptCommissionAction(commission.Hero);
            if (ActionLegality.IsLegal(state, accept, state.Phase))
            {
                actions.Add(accept);
            }
        }

        // Shelve every unsold player craft — free, exactly BaselinePlayer's own block (see that
        // type's class doc for the "a sold consumable never restocks" / "gear is genuine second-hand
        // stock" reasoning this mirrors verbatim; neither rule was ever blacksmith-specific).
        var shelved = state.Player.Shelf.Select(s => s.Item.Value).ToHashSet();
        var equipped = state.Heroes.Values
            .SelectMany(h => new[] { h.Gear.Weapon, h.Gear.Shield, h.Gear.Armor, h.Gear.Trinket })
            .Where(id => id is not null)
            .Select(id => id!.Value.Value)
            .ToHashSet();
        var soldConsumables = state.EventLog.OfType<ItemSold>()
            .Select(e => e.Item.Value)
            .Where(id => state.Items.TryGetValue(id, out var sold) && sold.Effect is not null)
            .ToHashSet();
        foreach (var item in state.Items.Values.Where(i =>
                     i.PlayerCrafted && !shelved.Contains(i.Id.Value) && !equipped.Contains(i.Id.Value)
                     && (i.Effect is null || !soldConsumables.Contains(i.Id.Value))))
        {
            var value = item.Effect is { } effect ? effect.Magnitude : item.Stats.Attack + item.Stats.Defense;
            actions.Add(new StockAction(item.Id, Math.Max(1, value * 2)));
        }

        // Top up materials THIS profession's own currently-unlocked recipes can spend, off the
        // always-available Morning vendor floor (never the Evening ore-offer path — see class doc).
        // One buy per still-short key, walked in ordinal order so the sequence is deterministic.
        var neededByMaterial = profession.Recipes.Values
            .Where(r => !profession.TierGate.TryGetValue(r.Tier, out var gate) || talents.Contains(gate))
            .GroupBy(r => r.MaterialKey, StringComparer.Ordinal)
            .ToImmutableSortedDictionary(g => g.Key, g => g.Min(r => r.MaterialQuantity), StringComparer.Ordinal);

        foreach (var (materialKey, minQuantity) in neededByMaterial)
        {
            if (slotsLeft <= 0)
            {
                break;
            }

            var have = state.Player.Materials.TryGetValue(materialKey, out var stock) ? stock : 0;
            if (have >= minQuantity)
            {
                continue; // already enough banked for at least one craft at this key
            }

            var buy = new BuyMaterialAction(materialKey, MaterialBatchSize);
            if (!ActionLegality.IsLegal(state, buy, state.Phase))
            {
                continue; // illegal for a reason unrelated to this loop's own local gold tracking
            }

            var cost = MaterialVendorHandlers.QuoteCost(materialKey, MaterialBatchSize);
            if (cost > gold)
            {
                continue; // local tracking caught what the start-of-tick IsLegal snapshot couldn't:
                          // an earlier key already spent the gold IsLegal still sees as available
            }

            actions.Add(buy);
            gold -= cost;
            slotsLeft--;
        }
    }

    private static void ExpeditionActions(
        GameState state, ProfessionDefinition profession, Func<Recipe, CraftPuzzleInput> buildPuzzle,
        ImmutableList<PlayerAction>.Builder actions)
    {
        // G3: a craft spends a slot — skip once the day's budget is spent (Baseline's own guard).
        if (state.ActionSlotsRemaining <= 0)
        {
            return;
        }

        // Best affordable recipe WITH A REAL BUYER by tier then stat sum — Baseline's own ordering
        // and its own HasBuyer gate (see this method's HasBuyer below), scoped to this profession's
        // own recipes instead of RecipeTable.All. One craft per window, Baseline's own "keeps the
        // policy simple and stable." The buyer gate is not optional housekeeping: an early cut of
        // this file always chased the single highest tier it could afford (Baseline's own OLD
        // pre-U-T1 rule), and a single-profession economy has far fewer recipes/tiers to fall back
        // on than Baseline's — every seed's material-buying outran its sales and gold hit zero for
        // good by day ~60-80, silently truncating the back half of the sweep. HasBuyer is what lets
        // the loop fall back to a tier the current roster actually wants instead of shelving a
        // fourth unsold copy of the top tier.
        foreach (var recipe in profession.Recipes.Values
                     .OrderByDescending(r => r.Tier)
                     .ThenByDescending(r => r.BaseStats.Attack + r.BaseStats.Defense))
        {
            var candidate = new CraftAction(recipe.RecipeId, recipe.MaterialKey);
            if (ActionLegality.IsLegal(state, candidate, state.Phase) && HasBuyer(state, recipe))
            {
                // ActionLegality.CraftLegal never inspects Puzzle at all (see this PR's report — a
                // legality-parity gap pre-dating this file). Attaching the puzzle here is still
                // always safe: buildPuzzle only ever runs against a recipe drawn from
                // profession.Recipes, which is exactly what CraftingHandlers.ApplyCraft's guards
                // 6a-6d require for that puzzle shape to be accepted.
                actions.Add(candidate with { Puzzle = buildPuzzle(recipe) });
                break;
            }
        }
    }

    /// <summary>
    /// Does <paramref name="recipe"/> have a real buyer right now? Estimated on
    /// <see cref="Recipe.BaseStats"/> (Common-grade), the same conservative floor
    /// <see cref="BaselinePlayer.HasBuyer"/> uses and for the identical reason (this instrument
    /// doesn't know the quality roll before the craft happens either). An independent copy rather
    /// than a call into Baseline's private method (KTD1 in spirit, not by reference: the heuristic
    /// is already profession-agnostic — recipe Slot/BaseStats/Effect only, never RecipeTable — so
    /// there is no RULE being re-derived here, only Baseline's own proven shape reused for a second
    /// profession).
    /// </summary>
    private static bool HasBuyer(GameState state, Recipe recipe)
    {
        if (recipe.Effect is { Kind: ConsumableKind.Heal })
        {
            var alreadyShelved = state.Player.Shelf.Any(e =>
                state.Items.TryGetValue(e.Item.Value, out var shelved) && shelved.Effect is { Kind: ConsumableKind.Heal });
            return !alreadyShelved
                && state.Heroes.Values.Any(h => h.Alive && h.Pack.Count < TraitEffects.ConsumableStockTargetFor(h));
        }

        var estimated = recipe.BaseStats.Attack + recipe.BaseStats.Defense;

        foreach (var hero in state.Heroes.Values)
        {
            if (!hero.Alive)
            {
                continue;
            }

            var heroClass = ClassRegistry.Require(hero.ClassId);
            if (recipe.Slot == ItemSlot.Shield && !heroClass.AllowsShield)
            {
                continue;
            }

            if (heroClass.MaxItemWeight is { } weightCap && recipe.BaseStats.Weight > weightCap)
            {
                continue;
            }

            var bestAvailable = hero.Gear.Slot(recipe.Slot) is { } wornId
                && state.Items.TryGetValue(wornId.Value, out var worn)
                ? worn.Stats.Attack + worn.Stats.Defense
                : 0;

            foreach (var entry in state.Player.Shelf)
            {
                if (!state.Items.TryGetValue(entry.Item.Value, out var shelved) || shelved.Slot != recipe.Slot)
                {
                    continue;
                }

                if (heroClass.MaxItemWeight is { } shelfWeightCap && shelved.Stats.Weight > shelfWeightCap)
                {
                    continue; // this hero couldn't wear the unsold copy either — doesn't cover them
                }

                bestAvailable = Math.Max(bestAvailable, shelved.Stats.Attack + shelved.Stats.Defense);
            }

            if (estimated > bestAvailable)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Exercises <see cref="AlchemyPuzzleScorer"/> (see <see cref="PuzzleCraftPlayer"/>'s class doc for
/// the shared engine and why this can't compose over <see cref="BaselinePlayer"/>).
///
/// <para><b>The average hand: a one-step-early pour.</b> <see cref="AlchemyPuzzleScorer"/> credits 2
/// points for the right reagent in the right position and 1 for a called-for reagent in the wrong one
/// (multiset-aware). Pouring <see cref="AlchemyPuzzleScorer.IdealSequenceFor"/> rotated by one slot
/// (<c>poured[i] = ideal[(i+1) % length]</c>) reads as "knows every reagent the recipe calls for, pours
/// one step ahead of the rhythm" — every entry is a real ingredient, none lands in its own slot unless
/// a recipe repeats a reagent (a handful do; the repeat then coincidentally scores exact, which is
/// correct, not a bug — a recipe that reuses Sunpetal at both ends genuinely tolerates this mistake
/// better). For a recipe with no repeats this is exactly 500/1000 base before any talent assist
/// (verified against the scorer's own arithmetic before writing this file) — deliberately mid-band,
/// not pinned to auto-craft's baseline the way <see cref="HandForgePlayer"/>'s forge trace is (there is
/// no equivalent "AutoCraftGrade" constant this puzzle shape needs to match): a genuinely average pour,
/// not a contrived one.</para>
/// </summary>
public static class AlchemyPuzzlePlayer
{
    public static ImmutableList<PlayerAction> ActionsFor(GameState state) =>
        PuzzleCraftPlayer.ActionsFor(state, AlchemyProfession.Definition, BuildPuzzle);

    private static CraftPuzzleInput BuildPuzzle(Recipe recipe)
    {
        var ideal = AlchemyPuzzleScorer.IdealSequenceFor(recipe);
        var length = ideal.Count;
        var poured = ImmutableList.CreateBuilder<int>();
        for (var i = 0; i < length; i++)
        {
            poured.Add(ideal[(i + 1) % length]);
        }

        return new AlchemyReagentPuzzle(poured.ToImmutable());
    }
}

/// <summary>
/// Exercises <see cref="TanningScrapeScorer"/> (see <see cref="PuzzleCraftPlayer"/>'s class doc for
/// the shared engine and why this can't compose over <see cref="BaselinePlayer"/>).
///
/// <para><b>The average hand: two passes everywhere.</b> A cell wants 1-2 passes if Plain, 3-4 if a
/// stubborn Flaw patch, exactly 1 if a delicate Thin patch — <see cref="TanningScrapeScorer.IdealPassesFor"/>.
/// Two passes on EVERY cell reads as "gives the whole hide a thorough, even scrape" — it lands inside
/// the ideal band for Plain cells (31 of 40), earns partial credit on Flaw cells (5 of 40 — worked the
/// stubborn patch, just not enough), and scrapes clean through every Thin cell (4 of 40 — the exact
/// mistake an even, unvarying hand makes on hide it never learned to treat differently). Deterministic
/// and total: <c>PatchSeed</c> is fixed at 1, the same single-seed choice
/// <see cref="HandForgePlayer.PathSeed"/> makes for the identical reason (this instrument only needs
/// ONE patch layout to prove the code path is exercised, not the campaign-spanning variety a real
/// player would see).</para>
/// </summary>
public static class TanningPuzzlePlayer
{
    /// <summary>See <see cref="TanningScrapeInput.PatchSeed"/> — fixed for the same reason
    /// <see cref="HandForgePlayer.PathSeed"/> is fixed (this type's class doc).</summary>
    private const int PatchSeed = 1;

    /// <summary>The "average hand": a constant, unvarying pass count on every cell (this type's
    /// class doc for why 2 is the natural, deliberately imperfect choice).</summary>
    private const int AveragePasses = 2;

    public static ImmutableList<PlayerAction> ActionsFor(GameState state) =>
        PuzzleCraftPlayer.ActionsFor(state, TanningProfession.Definition, BuildPuzzle);

    private static CraftPuzzleInput BuildPuzzle(Recipe recipe)
    {
        var passes = ImmutableList.CreateBuilder<int>();
        for (var i = 0; i < TanningScrapeScorer.CellCount; i++)
        {
            passes.Add(AveragePasses);
        }

        return new TanningScrapeInput(passes.ToImmutable(), PatchSeed);
    }
}

/// <summary>
/// Exercises <see cref="EngineeringAssemblyScorer"/> (see <see cref="PuzzleCraftPlayer"/>'s class doc
/// for the shared engine and why this can't compose over <see cref="BaselinePlayer"/>).
///
/// <para><b>The average hand: every part identified, the last socket never seated.</b> The schematic
/// (<see cref="EngineeringAssemblyScorer.SchematicFor"/>) wants every socket filled with its correct
/// part, ascending, and pays an order bonus for however many sockets were first-filled in strict
/// ascending sequence before the first break. Seating the CORRECT part into every socket but the LAST,
/// in ascending order, reads as "identified every part correctly, ran out of assembly before the
/// finish" — perfect part identification (no misplaced-part credit needed), an order run that only
/// ever breaks at the very end (so the order bonus is nearly its own maximum, never quite), and one
/// socket that scores nothing because it was never touched. Sockets range 3-5 depending on recipe tier
/// (<see cref="EngineeringAssemblyScorer.SocketCountFor"/>), so the exact base grade this "almost
/// finished" hand earns varies by recipe — deliberately: a genuinely average build, not a number pinned
/// to match another scorer's baseline.</para>
/// </summary>
public static class EngineeringPuzzlePlayer
{
    public static ImmutableList<PlayerAction> ActionsFor(GameState state) =>
        PuzzleCraftPlayer.ActionsFor(state, EngineeringProfession.Definition, BuildPuzzle);

    private static CraftPuzzleInput BuildPuzzle(Recipe recipe)
    {
        var schematic = EngineeringAssemblyScorer.SchematicFor(recipe);
        var sockets = schematic.Count;
        var placements = ImmutableList.CreateBuilder<int>();
        for (var socket = 0; socket < sockets - 1; socket++)
        {
            placements.Add(socket);
            placements.Add(schematic[socket]);
        }

        return new EngineeringAssemblyInput(placements.ToImmutable());
    }
}
