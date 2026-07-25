using System.Collections.Immutable;
using GameSim;
using GameSim.Advisor;
using GameSim.Contracts;
using GameSim.Harness;
using GameSim.Professions;
using GameSim.Venues;

namespace GameSim.Tests.Advisor;

/// <summary>
/// Kernel-parity property test (plan 2026-07-19-002 U10; extended U7 of plan 2026-07-25-001): every
/// action <see cref="ActionLegality.LegalActions"/> reports legal, replayed through the real kernel
/// in isolation, yields zero <see cref="RejectedAction"/> (the FORWARD direction) — AND every action
/// the driving policy actually submits that the kernel accepts must itself have been reported legal
/// (the REVERSE direction: a kernel-accepts-but-mirror-says-false is just as much a drift as the
/// forward miss, so U7 adds it as an equally build-failing check). Drives a full 100-day seeded run
/// with the shared <see cref="BaselinePlayer"/> policy (same harness precedent as the Balance gate)
/// so the property is checked across a wide, evolving cross-section of game states.
///
/// <para>MF-8: <see cref="BaselinePlayer"/> never opens the counter (PA3's atomic-equivalence pin
/// depends on it), so the five counter verbs (<see cref="OpenCounterAction"/>,
/// <see cref="PresentItemAction"/>, <see cref="SuggestItemAction"/>, <see cref="HaggleResponseAction"/>,
/// <see cref="CloseCounterAction"/>) never reach a LIVE session under that policy alone — a naive
/// "20/20 coverage" assertion driven only by <see cref="BaselinePlayer"/> would pass VACUOUSLY for
/// those five. <see cref="ActionLegality_Covers20Of20ActionTypes_AcrossBaselineAndCounterPlayer"/>
/// additionally drives the <see cref="CounterPlayer"/> harness policy (<c>Harness/</c>,
/// <c>CounterPlayerTests.cs</c>) to reach a real open session and close that hole.</para>
/// </summary>
public class ActionLegalityTests
{
    private const int Days = 100;
    private const int WarmupDays = 15;
    private const int CounterPlayerDays = 30;
    private const ulong Seed = 4242;

    /// <summary>Every <see cref="PlayerAction"/> derived type registered on the contract (Actions.cs
    /// <c>[JsonDerivedType]</c> list) — the 20/20 the parity test must reach. Pinned as an explicit
    /// list (not reflection) so a future new verb makes this file fail to compile/assert rather than
    /// silently shrinking the bar.</summary>
    private static readonly ImmutableHashSet<Type> AllActionTypes = ImmutableHashSet.Create(
        typeof(CraftAction), typeof(StockAction), typeof(SetPriceAction), typeof(UnstockAction),
        typeof(BuyOreAction), typeof(BuyMaterialAction), typeof(PostBountyAction),
        typeof(UnlockTalentAction), typeof(SetProfessionsAction), typeof(SendSupplyAction),
        typeof(RecallPartyAction), typeof(OpenCounterAction), typeof(PresentItemAction),
        typeof(SuggestItemAction), typeof(HaggleResponseAction), typeof(CloseCounterAction),
        typeof(AcceptCommissionAction), typeof(DeclineCommissionAction), typeof(HonorMemorialAction),
        typeof(ReforgeHeirloomAction));

    [Fact]
    public void EveryLegalAction_ReplayedThroughKernel_IsNeverRejected()
    {
        var covered = new HashSet<Type>();
        RunParityCheck(GameComposition.NewCampaign(Seed), BaselinePlayer.ActionsFor, Days, covered);

        Assert.True(covered.Count > 0, "The 100-day run never produced a single LegalActions candidate — the test is vacuous.");
    }

    [Fact]
    public void CounterPlayerDrivenSession_LegalActionsMatchKernel_BothDirections()
    {
        // MF-8: BaselinePlayer never opens the counter AND never populates the shelf while driving
        // solo (CounterPlayer itself never crafts/stocks — its own tests preset the shelf by hand),
        // so a CounterPlayer-only run from a FRESH campaign has an empty shelf and CounterPlayer
        // correctly closes immediately instead of stalling (see its own "empty shelf" doc). Warm up
        // with a short BaselinePlayer run first (crafts + shelves real items, same production
        // kernel) so the counter session that follows has something to present — then switch the
        // SAME evolving state over to CounterPlayer to reach a real open session.
        var covered = new HashSet<Type>();
        var warmed = RunParityCheck(GameComposition.NewCampaign(Seed), BaselinePlayer.ActionsFor, WarmupDays, covered);
        RunParityCheck(warmed, CounterPlayer.ActionsFor, CounterPlayerDays, covered);

        Assert.Contains(typeof(OpenCounterAction), covered);
        Assert.Contains(typeof(PresentItemAction), covered);
        Assert.Contains(typeof(HaggleResponseAction), covered);
        Assert.Contains(typeof(CloseCounterAction), covered);
    }

    [Fact]
    public void ActionLegality_Covers20Of20ActionTypes_AcrossBaselineAndCounterPlayer()
    {
        Assert.Equal(20, AllActionTypes.Count); // pins the total: a future new verb must bump this too

        var covered = new HashSet<Type>();
        // A full 100-day BaselinePlayer run for the broad cross-section (commissions post, heroes
        // die and leave memorials/reforgeable gear, talents unlock, ...) — everything EXCEPT the
        // five counter verbs, which need a live session (MF-8).
        var afterBaseline = RunParityCheck(GameComposition.NewCampaign(Seed), BaselinePlayer.ActionsFor, Days, covered);
        // Continue the SAME evolved state (shelf already stocked from the run above) with
        // CounterPlayer to reach OpenCounter/PresentItem/SuggestItem/HaggleResponse/CloseCounter.
        RunParityCheck(afterBaseline, CounterPlayer.ActionsFor, CounterPlayerDays, covered);

        // SendSupplyAction needs a live InFlight party (Camp phase) holding an UNSHELVED
        // player-crafted consumable — BaselinePlayer never sends supplies (D5: "no camp verbs, no
        // deep actions" — Camp verbs are a player-decided-phase feature, not baseline economy), so
        // the organic 130-day run above can go the whole run without ever manufacturing that exact
        // shape. Same spirit as MF-8's counter-session fixture: construct the one concrete
        // opportunity directly and round-trip it through the real kernel rather than accept a
        // vacuous "never observed" as coverage.
        covered.UnionWith(SendSupplyFixtureCoverage());

        var missing = AllActionTypes.Where(t => !covered.Contains(t)).Select(t => t.Name).ToList();
        Assert.True(missing.Count == 0,
            $"20/20 parity coverage failed — never exercised by LegalActions nor accepted from a driving policy: {string.Join(", ", missing)}");
    }

    /// <summary>Drives <paramref name="days"/> days of ticks from <paramref name="start"/> with
    /// <paramref name="policy"/>, checking BOTH directions every tick: FORWARD — every
    /// <see cref="ActionLegality.LegalActions"/> candidate, replayed through the kernel in isolation,
    /// must be accepted; REVERSE — every action <paramref name="policy"/> actually submits that the
    /// kernel accepts (in the real, advancing tick) must have been reported legal by
    /// <see cref="ActionLegality.IsLegal"/> beforehand. Every <see cref="PlayerAction"/> derived type
    /// observed accepted (either direction) is added to <paramref name="covered"/>. Returns the end
    /// state so a caller can chain a second policy onto the same evolving campaign (MF-8's
    /// warm-up-then-switch shape).</summary>
    private static GameState RunParityCheck(
        GameState start, Func<GameState, ImmutableList<PlayerAction>> policy, int days, HashSet<Type> covered)
    {
        var kernel = GameComposition.BuildKernel();
        var state = start;

        for (var tick = 0; tick < days * 5; tick++)
        {
            var phase = state.Phase;

            foreach (var candidate in ActionLegality.LegalActions(state, phase))
            {
                covered.Add(candidate.GetType());
                var probe = kernel.Tick(state, ImmutableList.Create(candidate));
                Assert.True(probe.Rejected.IsEmpty,
                    $"Day {state.Day} phase {phase}: LegalActions reported {candidate} legal, " +
                    $"but the kernel rejected it: {string.Join("; ", probe.Rejected.Select(r => r.Reason))}");
            }

            var committed = policy(state);
            var result = kernel.Tick(state, committed);

            foreach (var action in committed)
            {
                if (result.Rejected.Any(r => r.Action == action))
                {
                    continue; // the policy submitted something the kernel refused — not this test's concern
                }

                covered.Add(action.GetType());
                Assert.True(ActionLegality.IsLegal(state, action, phase),
                    $"Day {state.Day} phase {phase}: kernel accepted {action} but ActionLegality.IsLegal reported it illegal.");
            }

            state = result.NewState;
        }

        return state;
    }

    /// <summary>A concrete, deterministic SendSupplyAction opportunity (Camp phase, a live InFlight
    /// party, an unshelved player-crafted consumable in hand, gold to cover the runner's fee):
    /// checks BOTH directions through the real kernel exactly like <see cref="RunParityCheck"/>
    /// does for an organically-reached state, then reports the single type it covers.</summary>
    private static HashSet<Type> SendSupplyFixtureCoverage()
    {
        var fresh = GameComposition.NewCampaign(Seed);
        var hero = fresh.Heroes.Values.First(h => h.Alive);

        var itemId = new ItemId(fresh.NextItemId);
        var salve = new Item(
            itemId, "test-recipe", "Field Salve", ItemSlot.Weapon, QualityGrade.Common,
            new ItemStats(0, 0, 0), new MakersMark("Smith", fresh.Day), ImmutableList<ItemHistoryEntry>.Empty,
            new ConsumableEffect(ConsumableKind.Heal, 10));

        var inFlight = new InFlightExpedition(
            Party: ImmutableList.Create(hero.Id),
            TargetFloor: 2,
            CheckpointFloor: 1,
            VenueId: VenueRegistry.Mine.Id,
            Hp: ImmutableSortedDictionary<int, int>.Empty.Add(hero.Id.Value, hero.MaxHp),
            Packs: ImmutableSortedDictionary<int, ImmutableList<ItemId>>.Empty,
            Gold: ImmutableSortedDictionary<int, int>.Empty,
            Dead: ImmutableSortedSet<int>.Empty,
            Floors: ImmutableList<FloorOutcome>.Empty,
            Loot: ImmutableList<OreLoot>.Empty,
            DeepestFloorCleared: 1);

        var state = fresh with
        {
            Phase = DayPhase.Camp,
            Items = fresh.Items.Add(itemId.Value, salve),
            NextItemId = fresh.NextItemId + 1,
            InFlight = ImmutableList.Create(inFlight),
            Player = fresh.Player with { Gold = 1000 },
        };

        var send = new SendSupplyAction(hero.Id, itemId);
        Assert.True(ActionLegality.IsLegal(state, send, DayPhase.Camp));

        var kernel = GameComposition.BuildKernel();
        var result = kernel.Tick(state, ImmutableList.Create<PlayerAction>(send));
        Assert.True(result.Rejected.IsEmpty,
            $"SendSupply fixture: kernel rejected a fixture ActionLegality reported legal: " +
            $"{string.Join("; ", result.Rejected.Select(r => r.Reason))}");

        return new HashSet<Type> { typeof(SendSupplyAction) };
    }

    [Theory]
    [InlineData(DayPhase.Morning)]
    [InlineData(DayPhase.Expedition)]
    [InlineData(DayPhase.Camp)]
    [InlineData(DayPhase.ExpeditionDeep)]
    public void BuyOreAction_OutsideEvening_IsIllegal_AndKernelRejectsIt(DayPhase wrongPhase)
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(Seed) with
        {
            Phase = wrongPhase,
            OpenOreOffers = ImmutableList.Create(new OreOffered(new HeroId(1), "copper", 5, 3)),
        };
        var heroState = state.Heroes[1] with { Alive = true };
        state = state with { Heroes = state.Heroes.SetItem(1, heroState), Player = state.Player with { Gold = 1000 } };

        var action = new BuyOreAction(new HeroId(1), "copper", 5);

        Assert.False(ActionLegality.IsLegal(state, action, wrongPhase));

        var result = kernel.Tick(state, ImmutableList.Create<PlayerAction>(action));
        Assert.Single(result.Rejected);
    }

    [Theory]
    [InlineData(DayPhase.Expedition)]
    [InlineData(DayPhase.Evening)]
    [InlineData(DayPhase.Camp)]
    [InlineData(DayPhase.ExpeditionDeep)]
    public void BuyMaterialAction_OutsideMorning_IsIllegal_AndKernelRejectsIt(DayPhase wrongPhase)
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(Seed) with
        {
            Phase = wrongPhase,
            Player = GameComposition.NewCampaign(Seed).Player with { Gold = 1000 },
        };

        var action = new BuyMaterialAction("copper", 1);

        Assert.False(ActionLegality.IsLegal(state, action, wrongPhase));

        var result = kernel.Tick(state, ImmutableList.Create<PlayerAction>(action));
        Assert.Single(result.Rejected);
    }

    [Fact]
    public void CraftAction_UnknownMaterialKey_IsIllegal()
    {
        var state = GameComposition.NewCampaign(Seed);
        var recipe = ProfessionRegistry.AllRecipes.Values.First(r => r.Tier == 1);
        var action = new CraftAction(recipe.RecipeId, "not-a-real-material");

        Assert.False(ActionLegality.IsLegal(state, action, state.Phase));
    }

    [Fact]
    public void BuyMaterialAction_UnaffordableQuantity_IsIllegal()
    {
        var state = GameComposition.NewCampaign(Seed) with
        {
            Phase = DayPhase.Morning,
            Player = GameComposition.NewCampaign(Seed).Player with { Gold = 0 },
        };

        var action = new BuyMaterialAction("copper", 1000);

        Assert.False(ActionLegality.IsLegal(state, action, DayPhase.Morning));
    }

    /// <summary>T1 (plan 2026-07-25-001): with the day's action budget exhausted
    /// (<see cref="GameState.ActionSlotsRemaining"/> == 0), CraftAction, BuyMaterialAction,
    /// BuyOreAction, and PostBountyAction must all be reported ILLEGAL by their mirrors — matching
    /// their owning handlers, which reject a budget-spent day as guard-of-last-resort — even though
    /// every OTHER precondition (materials, gold, offer, recipe) is otherwise satisfied. Drives the
    /// real kernel for both directions, same shape as <see cref="RunParityCheck"/>'s per-candidate
    /// check, so a mirror that forgets the gate fails here even if it never surfaces in the 100-day
    /// organic run.</summary>
    [Fact]
    public void ExhaustedActionBudget_MirrorAgreesWithKernel_ForAllFourBudgetGatedVerbs()
    {
        var kernel = GameComposition.BuildKernel();
        var fresh = GameComposition.NewCampaign(Seed);
        var recipe = ProfessionRegistry.AllRecipes.Values
            .First(r => r.Tier == 1 && fresh.Player.IsSelected(r.Profession));

        var baseState = fresh with
        {
            Player = fresh.Player with
            {
                Gold = 10_000,
                Materials = fresh.Player.Materials.SetItem(recipe.MaterialKey, 1000),
            },
            OpenOreOffers = ImmutableList.Create(new OreOffered(new HeroId(1), "copper", 5, 3)),
            ActionSlotsRemaining = 0,
        };
        baseState = baseState with { Heroes = baseState.Heroes.SetItem(1, baseState.Heroes[1] with { Alive = true }) };

        // Each candidate paired with the phase its OWN handler requires (CraftAction: all phases;
        // BuyMaterialAction: Morning only; BuyOreAction: Evening only; PostBountyAction: Morning or
        // Evening) — using the wrong phase would fail on "no handler accepts this action" instead
        // of exercising the budget gate this test targets.
        var candidates = new (PlayerAction Action, DayPhase Phase)[]
        {
            (new CraftAction(recipe.RecipeId, recipe.MaterialKey), DayPhase.Morning),
            (new BuyMaterialAction("copper", 1), DayPhase.Morning),
            (new BuyOreAction(new HeroId(1), "copper", 5), DayPhase.Evening),
            (new PostBountyAction(1, 1), DayPhase.Morning),
        };

        foreach (var (action, phase) in candidates)
        {
            var state = baseState with { Phase = phase };

            Assert.False(ActionLegality.IsLegal(state, action, phase),
                $"{action.GetType().Name}: mirror reported legal with ActionSlotsRemaining == 0.");

            var result = kernel.Tick(state, ImmutableList.Create(action));
            Assert.Single(result.Rejected);
            Assert.Contains("No action slots left today", result.Rejected[0].Reason);
        }
    }
}
