using System.Collections.Immutable;
using GameSim.Advisor;
using GameSim.Bounties;
using GameSim.Classes;
using GameSim.Contracts;
using GameSim.Expedition;
using GameSim.Counter;
using GameSim.Crafting;
using GameSim.Drama;
using GameSim.Heroes;
using GameSim.Professions;
using GameSim.Venues;

namespace GameSim.Harness;

/// <summary>
/// P2-HONEST-30 (docs/design/MAKERS-MARK.md §11.13): the first policy that plays the blacksmith the
/// way §11.13 measured nobody ever had — it crafts and stocks the shelf (<see cref="BaselinePlayer"/>'s
/// own routine, called directly rather than re-derived) AND opens the counter and closes real sales
/// (<see cref="CounterPlayer"/>'s state machine, reusing its exact "present the best role-fit item"
/// opener via <see cref="CounterPlayer.BestRoleFitItem"/>).
///
/// <para><b>Decision 1</b> ("sell the good one or hold it") and <b>decision 2</b> ("price for the
/// sale or the relationship") get their first measured occurrence here: §11.13 found
/// <see cref="CounterPlayer"/> opens 2,000 counter sessions and closes zero sales (it never crafts
/// or stocks, so the shelf it presents from is always empty), and <see cref="BaselinePlayer"/> never
/// opens the counter at all — so <see cref="HaggleResolver.CloseSale"/>'s +60 pin / −80 fleece mood
/// swing had never once fired in this project's history before this policy exists.</para>
///
/// <para><b>Decision 2's rule, deterministic off recorded state, never RNG</b>: once a customer has a
/// standing offer on the table, read the hero's <see cref="RelationshipBand"/>
/// (<see cref="RelationshipBands.For"/>). <see cref="RelationshipBand.Regular"/>-or-better — this
/// smith knows the hero — PINS the price: counters at the round's own ceiling, which
/// <see cref="WillingnessModel.PinWindowPermille"/> guarantees lands inside
/// <see cref="HaggleResolver"/>'s pin window for the ONLY round this policy ever reaches (round 1 —
/// it never HoldFirms, so every close happens on the opening round, where the ceiling permille,
/// 980, always sits inside the pin window's [940,1060] band around true willingness). A
/// <see cref="RelationshipBand.Stranger"/> gets no read — the smith just takes the hero's own
/// offer (Accept), pricing for the relationship over the extra gold. Mirrors
/// <see cref="HaggleResolver"/>'s own true-willingness inputs exactly (list price, gold, class,
/// session interest, mood, presented quality, <see cref="TraitEffects.PriceSensitivityPermille"/>)
/// so the price this policy names always agrees with the resolver that actually closes the sale —
/// never a doomed, would-be-rejected Counter.</para>
///
/// <para><b>P2-HONEST-35 (§11.14, "The harness fleeces"): decision 2's second arm.</b> §11.14
/// measured this policy closing 551 counter sales — 270 pinned, ZERO fleeced — because this method
/// only ever Accepted or pinned at the ceiling, so <see cref="WillingnessModel.FleeceMoodPenalty"/>,
/// the <see cref="GameSim.Flavor.Packs.TavernPack.CounterSaleFleeced"/> gossip line, and
/// <see cref="GameSim.Heroes.NeedsSystem"/>'s boycott bias had never once been reached by any
/// harness since <see cref="HaggleResolver.CloseSale"/>'s fleece branch landed. <see cref="IsFleeceArm"/>
/// now takes roughly half of the Regular-or-better closes above the round's ceiling instead of
/// pinning at it — deterministic off the hero id and the calendar day (no RNG, no clock: both are
/// already-recorded state), so the same campaign always fleeces the same customer on the same day
/// and a re-run is byte-identical. <see cref="BaselinePlayer"/> never reaches this method at all
/// (it never opens the counter), so the idle/golden trace is untouched.</para>
///
/// <para>Same purity contract as every policy in this namespace: a pure function of
/// <see cref="GameState"/>, no IO, no RNG of its own, no wall clock.</para>
/// </summary>
public static class ForgeCounterPlayer
{
    public static ImmutableList<PlayerAction> ActionsFor(GameState state) => state.Phase switch
    {
        DayPhase.Morning => MorningActions(state),
        DayPhase.Evening => EveningActions(state),
        DayPhase.Camp => CampActions(state),
        DayPhase.Expedition => ExpeditionActions(state),
        // Craft/buy loops outside Morning/Evening/Camp/Expedition are IDENTICAL to BaselinePlayer's — composed, not copied.
        _ => BaselinePlayer.ActionsFor(state),
    };

    /// <summary>
    /// P2-LONG-37 (docs/design/MAKERS-MARK.md §11.16 measurement 3, "the reference smith who
    /// serves the counter dresses the light classes"): decision 3's honest floor for the light
    /// classes. P2-LONG-36 gave the mystic a tier-2 and a tier-3 armor recipe, but
    /// <see cref="BaselinePlayer"/>'s Expedition craft loop walks every recipe heaviest-tier-then-
    /// stat-sum first and breaks at the first legal one with a buyer — across 60 campaigns on
    /// seeds 1-20 the light pieces were crafted 0/1/0 and 0/0/0 times (Baseline/forgecounter/
    /// masterwork) while light-class heroes (<see cref="ClassDefinition.MaxItemWeight"/> not null:
    /// mystic, occultist, skirmisher) were 59% of deaths and marched past the shop wearing nothing
    /// of the smith's 476 times in horizon.
    ///
    /// <para><b>The rule, deterministic off recorded state — no RNG, no clock.</b> Before
    /// <see cref="BaselinePlayer"/>'s heaviest-first pick runs, ask a narrower question first: is
    /// some marching (<see cref="GameState.InFlight"/>) light-class hero's armor slot NOT already
    /// filled by a piece this smith made? If so, craft the best armor recipe that hero's own class
    /// weight cap lets them wear — read from the class registry via <see cref="ClassRegistry"/>,
    /// never re-derived — among the recipes <see cref="ActionLegality"/> actually accepts right
    /// now. <see cref="BaselinePlayer"/> is untouched: this arm is <c>forgecounter</c>'s alone.</para>
    /// </summary>
    private static ImmutableList<PlayerAction> ExpeditionActions(GameState state)
    {
        if (state.ActionSlotsRemaining > 0 && BestLightArmorCraft(state) is { } craft)
        {
            return ImmutableList.Create<PlayerAction>(craft);
        }

        return BaselinePlayer.ActionsFor(state);
    }

    /// <summary>P2-LONG-37: the best armor recipe for the first marching light-class hero (id
    /// order, party-formation order within a party) whose armor slot holds nothing of the smith's.
    /// "Best" is the highest Attack+Defense among the recipes that hero's class can legally wear
    /// and the forge can legally craft right now, ties broken by <see cref="RecipeTable.All"/>'s
    /// own sorted order (lowest recipe id first) so the pick never depends on dictionary
    /// iteration. Null when no marching light-class hero needs armor, or none is craftable yet.</summary>
    private static CraftAction? BestLightArmorCraft(GameState state)
    {
        foreach (var hero in MarchingLightClassHeroesNeedingSmithArmor(state))
        {
            var heroClass = ClassRegistry.Require(hero.ClassId);
            CraftAction? best = null;
            var bestStats = -1;

            foreach (var recipe in RecipeTable.All.Values)
            {
                if (recipe.Slot != ItemSlot.Armor
                    || heroClass.MaxItemWeight is not { } cap
                    || recipe.BaseStats.Weight > cap)
                {
                    continue;
                }

                var candidate = new CraftAction(recipe.RecipeId, recipe.MaterialKey);
                if (!ActionLegality.IsLegal(state, candidate, state.Phase))
                {
                    continue;
                }

                var stats = recipe.BaseStats.Attack + recipe.BaseStats.Defense;
                if (stats > bestStats)
                {
                    best = candidate;
                    bestStats = stats;
                }
            }

            if (best is { } chosen)
            {
                return chosen;
            }
        }

        return null;
    }

    /// <summary>P2-LONG-37: marching (<see cref="GameState.InFlight"/>) light-class heroes whose
    /// armor slot is not already filled by a player-crafted (<see cref="Item.PlayerCrafted"/>)
    /// piece — party order (already id-sorted, see <see cref="InFlightExpedition.Party"/>), dead
    /// members skipped.</summary>
    private static IEnumerable<Hero> MarchingLightClassHeroesNeedingSmithArmor(GameState state)
    {
        foreach (var party in state.InFlight)
        {
            foreach (var member in party.Party)
            {
                if (party.Dead.Contains(member.Value)
                    || !state.Heroes.TryGetValue(member.Value, out var hero)
                    || !hero.Alive
                    || ClassRegistry.Require(hero.ClassId).MaxItemWeight is null
                    || !NeedsSmithArmor(state, hero))
                {
                    continue;
                }

                yield return hero;
            }
        }
    }

    /// <summary>P2-LONG-37: true when <paramref name="hero"/>'s armor slot is empty or holds a
    /// piece this smith did not craft (no <see cref="MakersMark"/>).</summary>
    private static bool NeedsSmithArmor(GameState state, Hero hero) =>
        hero.Gear.Slot(ItemSlot.Armor) is not { } wornId
        || !state.Items.TryGetValue(wornId.Value, out var worn)
        || !worn.PlayerCrafted;

    /// <summary>P2-HONEST-38: BaselinePlayer's own Evening routine (ore-buying), plus the wake —
    /// see <see cref="AddWakeActions"/>.</summary>
    private static ImmutableList<PlayerAction> EveningActions(GameState state)
    {
        var actions = BaselinePlayer.ActionsFor(state).ToBuilder();
        AddWakeActions(state, actions);
        return actions.ToImmutable();
    }

    /// <summary>
    /// P2-HONEST-39 (docs/design/MAKERS-MARK.md §11.15, "The harness sends the runner"): decision 6 —
    /// send the runner or trust their judgment — had never been taken by any policy. §11.15 measured
    /// <c>SendSupply</c> legal at 1,858 decision points and chosen 0, <c>SupplyDelivered</c> fired 0
    /// times across 40 campaigns, 522 of 732 baseline camps (71%) and 1,301 of 1,319 forgecounter
    /// camps (98.6%) carrying no heal at all, and only 5 <c>Provisioned</c> and 1
    /// <c>PotionLifesave</c> beats out of 5,750.
    ///
    /// <para><b>The rule, deterministic off recorded state — no RNG, no clock.</b> Parties in
    /// <see cref="GameState.InFlight"/> order. For each, the neediest living camper: lowest HP as a
    /// share of MaxHp, ties by hero id. Send only when that hero sits in
    /// <see cref="CampHandlers.RunnerBandPct"/> — the 40% band P2-LONG-25's own ruling set this verb
    /// aside for. Measured while building this arm: the 30% halt line
    /// (<see cref="CombatMath.IsTooHurtToContinue"/>) is never met at the Camp phase at all — 0 of 289
    /// camped parties — because a hero that hurt has already fled or fallen, which is exactly why the
    /// ruling gave the runner its own wider band. The salve sent is the lowest-id unshelved player-crafted heal the legality
    /// rule will accept; <c>claimed</c> stops a second party being sent an item the first already took,
    /// since <paramref name="state"/> predates this tick.</para>
    ///
    /// <para><b>One delivery per party per day</b> is the Camp rule and <see cref="ActionLegality"/>
    /// enforces it (<c>InFlightExpedition.SupplySent</c>), so every candidate is asked before it is
    /// submitted and the arm never spends a tick on a doomed action — the same contract the counter and
    /// wake arms hold. The fee is gold, not an action slot, so unlike the heirloom this buys nothing
    /// away from the Evening's ore.</para>
    /// </summary>
    private static ImmutableList<PlayerAction> CampActions(GameState state)
    {
        var actions = BaselinePlayer.ActionsFor(state).ToBuilder();
        var claimed = new HashSet<int>();

        foreach (var party in state.InFlight)
        {
            if (NeediestCamper(state, party) is not { } hero)
            {
                continue;
            }

            foreach (var salve in SendableSalves(state, claimed))
            {
                var send = new SendSupplyAction(hero, salve);
                if (ActionLegality.IsLegal(state, send, state.Phase))
                {
                    actions.Add(send);
                    claimed.Add(salve.Value);
                    break;
                }
            }
        }

        return actions.ToImmutable();
    }

    /// <summary>The camper the runner is for: the living party member under the sim's own too-hurt
    /// bar with the lowest HP share, ties by hero id. Null when nobody in the party is hurt enough —
    /// the arm trusts their judgment, which is the other half of decision 6.</summary>
    private static HeroId? NeediestCamper(GameState state, InFlightExpedition party)
    {
        HeroId? neediest = null;
        var bestShare = int.MaxValue;

        foreach (var member in party.Party)
        {
            if (party.Dead.Contains(member.Value)
                || !party.Hp.TryGetValue(member.Value, out var hp)
                || !state.Heroes.TryGetValue(member.Value, out var hero)
                || hero.MaxHp <= 0
                || !CampHandlers.IsInRunnerBand(hp, hero.MaxHp))
            {
                continue;
            }

            var share = hp * 100 / hero.MaxHp;
            if (share < bestShare || (share == bestShare && neediest is { } current && member.Value < current.Value))
            {
                bestShare = share;
                neediest = member;
            }
        }

        return neediest;
    }

    /// <summary>Player-crafted heals the smith still holds — not shelved, not in a pack, not already
    /// claimed by another party this tick — in item-id order so the choice is a property of the
    /// forge's history rather than of dictionary iteration.</summary>
    private static IEnumerable<ItemId> SendableSalves(GameState state, HashSet<int> claimed)
    {
        foreach (var item in state.Items.Values)
        {
            if (item.PlayerCrafted && item.Effect is not null && !claimed.Contains(item.Id.Value))
            {
                yield return item.Id;
            }
        }
    }

    private static ImmutableList<PlayerAction> MorningActions(GameState state)
    {
        var counter = state.Counter;

        if (counter is null)
        {
            // No session yet this morning: run the ordinary blacksmith routine (forge-tier
            // upgrade, talent, commissions, stocking — BaselinePlayer.ActionsFor already switches
            // on DayPhase.Morning) AND open the counter in the SAME tick. OpenCounterAction only
            // reads state.Heroes (CounterHandlers.ApplyOpen), so its place in this list relative to
            // the routine above is order-independent — mirrors ApprenticePlayer's day-2 opening
            // tick, done every morning here instead of once on a fixed calendar day.
            var actions = BaselinePlayer.ActionsFor(state).ToBuilder();
            AcceptConsumableCommissions(state, actions);
            AddCommissionEarmarks(state, actions);
            AddBountyPost(state, actions);
            var open = new OpenCounterAction();
            if (ActionLegality.IsLegal(state, open, state.Phase))
            {
                actions.Add(open);
            }

            return actions.ToImmutable();
        }

        if (counter.Closed)
        {
            return ImmutableList<PlayerAction>.Empty; // already closing this tick — nothing left to do
        }

        if (counter.Active is not { } activeId
            || !state.Heroes.TryGetValue(activeId.Value, out var hero)
            || !hero.Alive)
        {
            // No customer at the counter (empty queue, PKD6) — this policy's counter job is done
            // for today; the morning routine already ran on the tick that opened the session.
            var close = new CloseCounterAction();
            return ActionLegality.IsLegal(state, close, state.Phase)
                ? ImmutableList.Create<PlayerAction>(close)
                : ImmutableList<PlayerAction>.Empty;
        }

        if (counter.Round > 0 && counter.StandingOfferGold is { } standingOffer && counter.Presented is not null)
        {
            return ImmutableList.Create<PlayerAction>(RespondToOffer(state, counter, hero, standingOffer));
        }

        // Nothing presented yet this round — show the active customer the shelf's best role-fit
        // item (CounterPlayer's own opener, composed rather than copied). An empty shelf means
        // there is nothing left to sell: close instead of stalling the morning.
        var heroClass = ClassRegistry.Require(hero.ClassId);
        var best = CounterPlayer.BestRoleFitItem(state, hero, heroClass);
        if (best is { } chosen)
        {
            var present = new PresentItemAction(chosen);
            return ActionLegality.IsLegal(state, present, state.Phase)
                ? ImmutableList.Create<PlayerAction>(present)
                : ImmutableList<PlayerAction>.Empty;
        }

        var closeEmpty = new CloseCounterAction();
        return ActionLegality.IsLegal(state, closeEmpty, state.Phase)
            ? ImmutableList.Create<PlayerAction>(closeEmpty)
            : ImmutableList<PlayerAction>.Empty;
    }

    /// <summary>
    /// P2-HONEST-38 (docs/design/MAKERS-MARK.md §11.15, "The harness sits the wake"): link 5's four
    /// verbs get their first measured occurrence. §11.15 measured ZERO of the eleven policies in this
    /// namespace constructing any of them — <see cref="HonorMemorialAction"/> legal at 23,777 Evening
    /// decision points and chosen 0, <see cref="ReforgeHeirloomAction"/> legal at 43,815 and chosen 0,
    /// and <c>MemorialHonored</c> / <c>GraveMarkerPlaced</c> / <c>RemembranceChosen</c> /
    /// <c>HeirloomReforged</c> never once fired across 40 campaigns. So the town's memory of a fallen
    /// hero (link 5) had never been exercised end to end by anything but a hand-built fixture.
    ///
    /// <para><b>The rule, deterministic off recorded state — no RNG, no clock.</b> Memorials in hero-id
    /// order (<see cref="DramaState.Memorials"/> is log-ordered; sorting makes the wake's order a
    /// property of the roster rather than of death order). Per memorial: honour it if not yet honoured;
    /// set the BEST legal marker — highest <see cref="QualityGrade"/> among
    /// <see cref="WakeQuery.MarkerCandidates"/>, ties broken by lowest item id, because a smith lays
    /// their best surviving work on the grave; take <see cref="WakeQuery.DefaultRemembrance"/> (the
    /// wake's own pre-selected naming event, asked rather than re-derived); and reforge the heirloom
    /// when <see cref="WakeQuery.HeirloomOpen"/> says the question is still open.</para>
    ///
    /// <para><b>Every verb is asked of <see cref="ActionLegality.IsLegal"/> at this phase before it is
    /// submitted</b>, so this policy never spends a tick on a doomed action — the same contract the
    /// counter arm above already holds. <paramref name="state"/> predates this tick, so two memorials
    /// can both see the same marker candidate legal; <c>claimed</c> is what stops the second one from
    /// submitting an item the first already took (the kernel applies in order, and the second would be
    /// rejected).</para>
    ///
    /// <para><b>The heirloom is decision 4 ("spend the slot or bank it"), honestly paid for.</b>
    /// <see cref="ReforgeHeirloomAction"/> is the one wake verb that costs an action slot
    /// (<see cref="ActionBudget.ConsumesSlot"/>) and BaselinePlayer's Evening routine already spends
    /// the day's remainder on ore. Rather than emit a buy the kernel would reject, this drops
    /// tonight's LAST ore buy to pay for the reforge — the smith choosing the fallen's legend over one
    /// more crate of ore. Ore buys are order-independent of each other, so dropping the last is the
    /// cheapest honest way to make room.</para>
    /// </summary>
    private static void AddWakeActions(GameState state, ImmutableList<PlayerAction>.Builder actions)
    {
        var slotsLeft = state.ActionSlotsRemaining - actions.Count(ActionBudget.ConsumesSlot);
        var claimed = new HashSet<int>();

        foreach (var memorial in state.Drama.Memorials.OrderBy(m => m.Hero.Value))
        {
            var hero = memorial.Hero;

            var honor = new HonorMemorialAction(hero);
            if (!memorial.Honored && ActionLegality.IsLegal(state, honor, state.Phase))
            {
                actions.Add(honor);
            }

            if (BestMarker(state, hero, claimed) is { } marker)
            {
                var place = new PlaceGraveMarkerAction(hero, marker);
                if (ActionLegality.IsLegal(state, place, state.Phase))
                {
                    actions.Add(place);
                    claimed.Add(marker.Value);
                }
            }

            if (WakeQuery.DefaultRemembrance(state, hero) is { } remembrance)
            {
                var choose = new ChooseRemembranceAction(hero, remembrance.Id);
                if (ActionLegality.IsLegal(state, choose, state.Phase))
                {
                    actions.Add(choose);
                }
            }

            if (WakeQuery.HeirloomOpen(state, hero)
                && FirstLegalReforge(state, hero, claimed) is { } reforge
                && TryPaySlot(actions, ref slotsLeft))
            {
                actions.Add(reforge);
                claimed.Add(reforge.SourceItem.Value);
            }
        }
    }

    /// <summary>P2-HONEST-38: the piece this smith would lay on <paramref name="hero"/>'s grave — the
    /// highest-quality legal candidate, ties broken by lowest item id so the choice is a function of
    /// recorded state alone. Legality (yours, not worn, not shelved, not already a marker) is
    /// <see cref="WakeQuery.MarkerCandidates"/>'s own answer, never re-derived here.</summary>
    private static ItemId? BestMarker(GameState state, HeroId hero, HashSet<int> claimed)
    {
        ItemId? best = null;
        var bestQuality = QualityGrade.Poor;
        foreach (var candidate in WakeQuery.MarkerCandidates(state, hero))
        {
            if (claimed.Contains(candidate.Value) || !state.Items.TryGetValue(candidate.Value, out var item))
            {
                continue;
            }

            if (best is null || item.Quality > bestQuality
                || (item.Quality == bestQuality && candidate.Value < best.Value.Value))
            {
                best = candidate;
                bestQuality = item.Quality;
            }
        }

        return best;
    }

    /// <summary>P2-HONEST-38: the fallen's first reforgeable piece — worn at death, not already
    /// reforged, not claimed by this same tick's wake — paired with the first recipe (in
    /// <see cref="ProfessionRegistry.AllRecipes"/>'s sorted order) that <see cref="ActionLegality"/>
    /// actually accepts. Same "one canonical instance per source" shape
    /// <see cref="ActionLegality"/>'s own heirloom candidate enumeration uses, so the pairing this
    /// policy names can never disagree with what the handler would accept.</summary>
    private static ReforgeHeirloomAction? FirstLegalReforge(GameState state, HeroId hero, HashSet<int> claimed)
    {
        var sources = state.EventLog.OfType<HeroDied>()
            .Where(died => died.Hero == hero)
            .SelectMany(died => new[] { died.WornGear.Weapon, died.WornGear.Shield, died.WornGear.Armor, died.WornGear.Trinket })
            .Where(item => item is not null)
            .Select(item => item!.Value)
            .Where(item => !claimed.Contains(item.Value))
            .Distinct()
            .OrderBy(item => item.Value);

        foreach (var source in sources)
        {
            foreach (var recipe in ProfessionRegistry.AllRecipes.Values)
            {
                var candidate = new ReforgeHeirloomAction(source, recipe.RecipeId, recipe.MaterialKey);
                if (ActionLegality.IsLegal(state, candidate, state.Phase))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    /// <summary>P2-HONEST-38: make room in the day's budget for one slot-spending wake verb — either
    /// a slot this tick has not committed yet, or the last ore buy, dropped to pay for it. False when
    /// neither is available, and the caller then leaves the heirloom for another night rather than
    /// submitting a buy-and-reforge pair the kernel would reject.</summary>
    private static bool TryPaySlot(ImmutableList<PlayerAction>.Builder actions, ref int slotsLeft)
    {
        if (slotsLeft > 0)
        {
            slotsLeft--;
            return true;
        }

        var lastBuy = actions.FindLastIndex(a => a is BuyOreAction);
        if (lastBuy < 0)
        {
            return false;
        }

        actions.RemoveAt(lastBuy);
        return true;
    }

    /// <summary>Decision 2 ("price for the sale or the relationship"), deterministic off the
    /// hero's recorded <see cref="RelationshipBand"/>: fleece or pin the price for a
    /// Regular-or-better hero (this smith reads them — <see cref="IsFleeceArm"/> picks which),
    /// take their own offer otherwise. See this type's class doc for why the pin is guaranteed at
    /// the only round this policy ever reaches.</summary>
    private static HaggleResponseAction RespondToOffer(GameState state, CounterState counter, Hero hero, int standingOffer)
    {
        if (RelationshipBands.For(hero.Id, state) < RelationshipBand.Regular)
        {
            // Stranger: no read on this hero yet — take their own number rather than press for
            // more. A plain sale, no mood delta (HaggleResolver.CloseSale, Accept path).
            return new HaggleResponseAction(HaggleResponseKind.Accept);
        }

        // Regular-or-better: mirrors HaggleResolver.ResolveCounter's OWN inputs exactly so the
        // ceiling computed here agrees with the one the resolver actually checks.
        var listPrice = state.Player.Shelf.FirstOrDefault(e => e.Item == counter.Presented)?.Price ?? standingOffer;
        var presentedQuality = state.Items.TryGetValue(counter.Presented!.Value.Value, out var presentedItem)
            ? presentedItem.Quality
            : QualityGrade.Common;
        var trueWillingness = WillingnessModel.TrueWillingness(
            listPrice, hero.Gold, hero.ClassId, counter.InterestPermille, hero.MoodPermille, presentedQuality,
            TraitEffects.PriceSensitivityPermille(hero));
        var (_, ceiling) = WillingnessModel.Band(trueWillingness, counter.Round);

        // P2-HONEST-35: this Regular-or-better close's arm — fleece above the ceiling instead of
        // pinning at it. FleecePrice already caps at hero.Gold and the 1.06x markup over true
        // willingness clears the round-1 ceiling (0.98x) with room, so this only ever falls through
        // to the pin arm below on a degenerate near-zero willingness.
        if (IsFleeceArm(hero.Id, state.Day))
        {
            var fleecePrice = FleecePrice(trueWillingness, hero.Gold);
            if (fleecePrice > ceiling && fleecePrice > 0 && fleecePrice <= hero.Gold)
            {
                return new HaggleResponseAction(HaggleResponseKind.Counter, fleecePrice);
            }
        }

        // Guard the edges ActionLegality.HaggleResponseLegal would reject (a positive price the
        // hero can afford) — a near-zero true willingness or a round past the first (this policy
        // never HoldFirms, so that never actually happens, but the read stays honest either way)
        // falls back to Accept rather than ever risking an illegal Counter.
        return ceiling > 0 && ceiling <= hero.Gold
            ? new HaggleResponseAction(HaggleResponseKind.Counter, ceiling)
            : new HaggleResponseAction(HaggleResponseKind.Accept);
    }

    /// <summary>P2-HONEST-35: whether THIS Regular-or-better close takes the fleece arm instead of
    /// the pin arm — a pure function of the hero id and the campaign day (both already-recorded
    /// state, never RNG, never a clock read), so the same campaign always fleeces the same customer
    /// on the same day and two calls on an identical state agree. (hero id + day) divisible by 3
    /// takes the arm — a genuine minority of Regular-or-better closes (this policy still pins most
    /// of the time, matching what a smith who mostly reads people fairly would do), enough to give
    /// the fleece mood/gossip/boycott surfaces their first measured occurrence without erasing the
    /// pin arm this policy already proves (see <see cref="ForgeCounterPlayerTests"/>'s pin-rule
    /// test, hero 1 on day 1, sum 2 — NOT divisible by 3 — specifically so this arm never touches
    /// it, and its own fleece-arm test, hero 2 on day 1, sum 3 — divisible by 3).</summary>
    private static bool IsFleeceArm(HeroId hero, int day) => (hero.Value + day) % 3 == 0;

    /// <summary>P2-HONEST-35: a fleece price — comfortably past the round's ceiling (round 1's
    /// 980 permille of true willingness) so <see cref="HaggleResolver.ResolveCounter"/>'s fleece
    /// branch always fires and reaches a real <see cref="WillingnessModel.FleeceMoodDelta"/>, capped
    /// at what the hero can actually pay so this never risks the illegal-Counter guard above. Reuses
    /// <see cref="WillingnessModel.FleeceMoodScaleWindowPermille"/> as the markup — the same
    /// permille the resolver's own fleece mood math already treats as "clearly off," rather than
    /// inventing a second scale.</summary>
    private static int FleecePrice(int trueWillingness, int heroGold) =>
        Math.Min(heroGold, trueWillingness + (int)((long)trueWillingness * WillingnessModel.FleeceMoodScaleWindowPermille / 1000));

    /// <summary>The quality bar a craft this smith has not rolled yet is SURE to clear —
    /// <see cref="Crafting.ItemForge"/> only scales up from Common and auto-craft's own grade never
    /// reaches the Poor band, the same conservative floor <c>BaselinePlayer.HasBuyer</c> already
    /// estimates a not-yet-crafted recipe against. An ask above this bar is only answered off a
    /// salve the forge can already see (<see cref="SalveOnHandFor"/>), never off a promise.</summary>
    private const QualityGrade CraftableQualityFloor = QualityGrade.Common;

    /// <summary>
    /// P2-HONEST-43 (docs/design/MAKERS-MARK.md §11.16, "The reference smith fills the consumable
    /// ask"): link 2's commission channel, for the slot it asks for most and had never once filled.
    /// §11.16 measured consumable commissions posted 145 times under <see cref="BaselinePlayer"/>
    /// and 519 under this policy — 53% of everything this board posts — and fulfilled 0 across 60
    /// campaigns, EXPIRED 0 as well: <see cref="BaselinePlayer"/>'s accept loop filters
    /// <c>c.Slot != ItemSlot.Consumable</c> and this policy composes that loop, so the ask was never
    /// accepted and U14 drops a posted-but-unaccepted commission in silence at its deadline. So
    /// <see cref="CommissionHandlers.TryFulfillFromShelf"/>'s consumable branch — the one that adds
    /// the salve to <see cref="Hero.Pack"/> instead of <see cref="Hero.Gear"/> — had never run in a
    /// sweep, while the advisor named the ask 523 times per 10,000 decision points.
    ///
    /// <para><b>The rule, deterministic off recorded state — no RNG, no clock.</b> Open consumable
    /// commissions in hero-id order (sorting makes the answer a property of the roster rather than
    /// of posting order). Answer one only when this smith can actually fill it: either a salve they
    /// already hold satisfies it — a shelf piece not held for someone else, or one of THIS tick's own
    /// <see cref="StockAction"/> entries, which is where a salve crafted during yesterday's
    /// Expedition sits at this moment — or, holding none, a heal recipe is legal to craft and the
    /// ask's bar is one a craft is sure to clear. <see cref="CommissionHandlers.Satisfies"/> is the
    /// match rule the commission channel itself checks at delivery, asked here rather than
    /// re-derived, so an ask this arm answers can never disagree with what the channel would accept;
    /// the accept verb itself is asked of <see cref="ActionLegality"/> before submission, the same
    /// contract the counter, wake and vigil arms hold.</para>
    ///
    /// <para><b>One salve answers one ask, and the unrolled craft answers at most one.</b>
    /// <paramref name="state"/> predates this tick, so two asks can both see the same salve;
    /// <c>claimed</c> is what stops the second one being promised a piece the first will take, and
    /// <c>promisedCraft</c> holds the craft fallback to a single ask because
    /// <see cref="BaselinePlayer"/>'s Expedition loop makes at most one item per window. Over-
    /// promising is the one failure mode this arm can introduce: an accepted ask that misses its
    /// deadline is a mood penalty the silently-dropped open ask never charged.</para>
    ///
    /// <para><b>Runs before <see cref="AddCommissionEarmarks"/></b> so a salve this tick shelves is
    /// held for the hero who asked for it (that method reads this tick's own accepts as
    /// <c>acceptingNow</c>) rather than being sold out from under them by an earlier shopper.
    /// <see cref="BaselinePlayer"/> is untouched: its Consumable exclusion is a scope choice about
    /// PROVISIONING — pushing a salve at a hero whose stocking trait says they never restock — and a
    /// commission is the hero's own posted request, which is the other question.</para>
    /// </summary>
    private static void AcceptConsumableCommissions(GameState state, ImmutableList<PlayerAction>.Builder actions)
    {
        var claimed = new HashSet<int>();
        var promisedCraft = false;

        foreach (var commission in state.Commissions
                     .Where(c => !c.Accepted && c.Slot == ItemSlot.Consumable)
                     .OrderBy(c => c.Hero.Value))
        {
            if (!state.Heroes.TryGetValue(commission.Hero.Value, out var hero) || !hero.Alive)
            {
                continue;
            }

            var onHand = SalveOnHandFor(state, actions, commission, hero, claimed);
            if (onHand is null && (promisedCraft || commission.MinQuality > CraftableQualityFloor || !HealCraftLegal(state)))
            {
                continue;
            }

            var accept = new AcceptCommissionAction(commission.Hero);
            if (!ActionLegality.IsLegal(state, accept, state.Phase))
            {
                continue;
            }

            actions.Add(accept);
            if (onHand is { } salve)
            {
                claimed.Add(salve.Value);
            }
            else
            {
                promisedCraft = true;
            }
        }
    }

    /// <summary>P2-HONEST-43: the piece already in this forge's hands that would fill
    /// <paramref name="commission"/> — the shelf (minus anything earmarked for another hero, which
    /// is not this one's to promise) plus this tick's own stocking, in item-id order so the choice is
    /// a property of the forge's history rather than of dictionary iteration. Null when the smith
    /// holds nothing that satisfies the ask.</summary>
    private static ItemId? SalveOnHandFor(
        GameState state,
        ImmutableList<PlayerAction>.Builder actions,
        Commission commission,
        Hero hero,
        HashSet<int> claimed)
    {
        var heroClass = ClassRegistry.Require(hero.ClassId);
        var candidates = state.Player.Shelf
            .Where(entry => !HeroShoppingSystem.IsHeldForSomeoneElse(entry, hero))
            .Select(entry => entry.Item)
            .Concat(actions.OfType<StockAction>().Select(stock => stock.Item))
            .Where(item => !claimed.Contains(item.Value))
            .Distinct()
            .OrderBy(item => item.Value);

        foreach (var candidate in candidates)
        {
            if (state.Items.TryGetValue(candidate.Value, out var item)
                && CommissionHandlers.Satisfies(commission, item, heroClass))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>P2-HONEST-43: can this smith make a salve at all right now? Asked of
    /// <see cref="ActionLegality"/>'s own craft rule (profession, tier gate, materials net of the
    /// efficiency talent, budget) against every heal recipe rather than re-derived — the same
    /// "never re-derive a legality rule" contract the rest of this policy holds. The craft itself is
    /// never submitted here: <see cref="BaselinePlayer"/>'s Expedition loop owns that, and this is
    /// only the question of whether the promise is one the forge could keep.</summary>
    private static bool HealCraftLegal(GameState state) =>
        RecipeTable.All.Values.Any(recipe =>
            recipe.Effect is { Kind: ConsumableKind.Heal }
            && ActionLegality.IsLegal(state, new CraftAction(recipe.RecipeId, recipe.MaterialKey), state.Phase));

    /// <summary>
    /// P2-PEOPLE-28 ("hold it for Torvald"): decision 1's first measured occurrence. A piece
    /// <see cref="BaselinePlayer"/>'s own stocking loop just proposed to stock this same tick, that
    /// satisfies a hero's commission — accepted already, or accepted by one of THIS tick's own
    /// <see cref="AcceptCommissionAction"/> entries above it in <paramref name="actions"/> — gets
    /// held for that hero instead of going to whoever shops first. <see
    /// cref="CommissionHandlers.Satisfies"/> is the exact match rule the commission channel checks
    /// at delivery time (<see cref="CommissionHandlers.TryFulfillFromShelf"/>), asked here rather
    /// than re-derived, so an earmark this policy places can never disagree with what the
    /// commission channel would actually accept. Never asks <see cref="ActionLegality.IsLegal"/>
    /// for the earmark itself: <paramref name="state"/> predates this tick's own StockAction, so
    /// the item is not on the shelf there yet — <see cref="GameKernel.Tick"/> applies actions in
    /// order, so by the time this earmark actually runs, its own StockAction (earlier in the same
    /// list) already put the item there. One earmark per hero (a hero holds at most one open or
    /// accepted commission at a time — see <see cref="CommissionHandlers"/>'s own class doc).
    /// </summary>
    private static void AddCommissionEarmarks(GameState state, ImmutableList<PlayerAction>.Builder actions)
    {
        var acceptingNow = actions.OfType<AcceptCommissionAction>().Select(a => a.Hero).ToHashSet();
        var claimed = new HashSet<HeroId>();
        foreach (var stock in actions.OfType<StockAction>().ToList())
        {
            if (!state.Items.TryGetValue(stock.Item.Value, out var item))
            {
                continue;
            }

            foreach (var commission in state.Commissions)
            {
                if (claimed.Contains(commission.Hero)
                    || (!commission.Accepted && !acceptingNow.Contains(commission.Hero)))
                {
                    continue;
                }

                if (!state.Heroes.TryGetValue(commission.Hero.Value, out var hero) || !hero.Alive
                    || !CommissionHandlers.Satisfies(commission, item, ClassRegistry.Require(hero.ClassId)))
                {
                    continue;
                }

                actions.Add(new EarmarkAction(stock.Item, commission.Hero));
                claimed.Add(commission.Hero);
                break;
            }
        }
    }

    /// <summary>
    /// P2-HONEST-47 (docs/design/MAKERS-MARK.md §11.18 measurement 1, "the reference smith posts a
    /// bounty"): the one lever §11.7.1 aimed at where the heroes go (link 3, decision 6's town-facing
    /// twin) had never been pulled by any policy — every analytics run this plan has ever quoted
    /// ends with <c>Bounties: 0 accepted / 0 declined</c>, on every seed, under both policies, because
    /// no policy in this namespace so much as names <see cref="PostBountyAction"/>.
    ///
    /// <para><b>The rule, deterministic off recorded state — no RNG, no clock.</b> Read the Mine's
    /// own halting signal rather than inventing a second one: <see cref="GateHeldStreakQuery"/>
    /// already counts how many evenings running the venue's structural gate turned a party back with
    /// no roll (P2-END-01). Once that streak reaches <see cref="DemandBoard.StallThresholdDays"/> —
    /// the same "at least two days running" bar the depth-stall read already uses — the smith names
    /// the floor from that same held night's own <see cref="GateReading"/>
    /// (<see cref="ExpeditionResult.GateHeldAt"/>, still sitting in
    /// <see cref="GameState.LastNightExpeditions"/> at this Morning tick, one night deep) and posts a
    /// bounty there, at <see cref="BountyRules.MinimumReward"/> — the same floor-scaled price the
    /// demand board already shows heroes as the bar a floor has to clear — asked of
    /// <see cref="ActionLegality"/> before submission, the same contract every other arm in this
    /// policy holds.</para>
    ///
    /// <para><b>The shop only posts what it can cover, and never stacks a second escrow on a floor
    /// already carrying one.</b> Skipped when the reward would outrun <see cref="Player.Gold"/> (the
    /// smith never posts a bounty it would have to shortchange — the same "a reward the shop can
    /// actually cover" bar this unit is named for), and skipped while any bounty already targets that
    /// floor, accepted or not: an unaccepted one is still being judged
    /// (<see cref="BountyJudgingSystem"/>, Expedition tick) or lapsing on its own
    /// (<see cref="BountyRules.ExpiryDays"/>), and an accepted one is already doing this arm's job.
    /// </para>
    /// </summary>
    private static void AddBountyPost(GameState state, ImmutableList<PlayerAction>.Builder actions)
    {
        var streak = GateHeldStreakQuery.ConsecutiveNights(state, VenueRegistry.MineId, state.Day - 1);
        if (streak < DemandBoard.StallThresholdDays)
        {
            return;
        }

        var heldFloor = state.LastNightExpeditions
            .FirstOrDefault(result => result.VenueId == VenueRegistry.MineId && result.Halt == ExpeditionHalt.GateHeld)
            ?.GateHeldAt?.Floor;
        if (heldFloor is not { } floor || state.Bounties.Any(b => b.TargetFloor == floor))
        {
            return;
        }

        var reward = BountyRules.MinimumReward(floor);

        // This same Morning tick's own UpgradeForgeAction — queued by BaselinePlayer.ActionsFor
        // above, ahead of this arm in the submitted list — spends player gold from the SAME
        // pre-tick balance before the kernel ever reaches this bounty (GameKernel.Tick applies the
        // batch in submitted order, threading state through each handler). Checking against the raw
        // state.Player.Gold snapshot here would post a bounty the escrow can no longer actually
        // cover once the upgrade lands first — read net of that queued spend instead.
        var reserved = actions.Any(a => a is UpgradeForgeAction)
            ? Economy.ForgeTierHandlers.GoldCost[Economy.ForgeTierHandlers.CurrentTierIndex(state.Player)]
            : 0;
        if (reward > state.Player.Gold - reserved)
        {
            return;
        }

        var post = new PostBountyAction(floor, reward);
        if (ActionLegality.IsLegal(state, post, state.Phase))
        {
            actions.Add(post);
        }
    }
}
