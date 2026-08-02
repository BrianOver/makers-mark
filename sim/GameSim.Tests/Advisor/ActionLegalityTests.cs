using System.Collections.Immutable;
using System.Reflection;
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
/// <see cref="CloseCounterAction"/>) never reach a LIVE session under that policy alone. This file's
/// <see cref="CounterPlayerDrivenSession_LegalActionsMatchKernel_BothDirections"/> additionally
/// drives the <see cref="CounterPlayer"/> harness policy (<c>Harness/</c>, <c>CounterPlayerTests.cs</c>)
/// to reach a real open session and close that hole.</para>
///
/// <para>U4: <see cref="IsLegal_HasAnExplicitCase_ForEveryConcreteContractsActionType"/> is the
/// dispatch-coverage tripwire that replaced a hand-enumerated 20-type list (the exact list that let
/// Phase D's four gold-sink verbs go unmirrored — see <c>ActionLegality.cs</c>'s class doc). It
/// discovers every concrete <see cref="PlayerAction"/> type by reflection over the Contracts
/// assembly, so a future new verb is picked up with zero list maintenance.</para>
/// </summary>
public class ActionLegalityTests
{
    private const int Days = 100;
    private const int WarmupDays = 15;
    private const int CounterPlayerDays = 30;

    // RE-SEATED (2026-08-01 re-baseline: honest BaselinePlayer craft check + banded venue routing
    // + T1 four-venue/six-class flip): the parity PROPERTIES here are seed-agnostic, but the
    // counter-session COVERAGE assertions need a campaign whose heroes actually counter-offer
    // inside the warmup+30-day window. Under the new economy no customer ever haggled at the old
    // 4242 in that window; 4246 was measured (seed probe 4242..4252) to produce an accepted
    // HaggleResponseAction on day 17. If coverage starves again after a future re-baseline,
    // re-probe nearby seeds — do not weaken the Contains assertions.
    private const ulong Seed = 4246;

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
        // SAME evolving state over to a MERGED policy (see <see cref="CounterPlayerWithOngoingSupply"/>)
        // to reach a real open session.
        var covered = new HashSet<Type>();
        var warmed = RunParityCheck(GameComposition.NewCampaign(Seed), BaselinePlayer.ActionsFor, WarmupDays, covered);
        RunParityCheck(warmed, CounterPlayerWithOngoingSupply, CounterPlayerDays, covered);

        Assert.Contains(typeof(OpenCounterAction), covered);
        Assert.Contains(typeof(PresentItemAction), covered);
        Assert.Contains(typeof(HaggleResponseAction), covered);
        Assert.Contains(typeof(CloseCounterAction), covered);
    }

    /// <summary>
    /// U4 — the drift tripwire. Reflection discovers every concrete (non-abstract) type deriving
    /// from <see cref="PlayerAction"/> straight off the Contracts assembly — the exact list a
    /// hand-maintained set (this test's predecessor) let rot the moment Phase D's four gold-sink
    /// verbs (<see cref="UpgradeForgeAction"/>, <see cref="BuyForgeSupplyAction"/>,
    /// <see cref="MasterworkAttemptAction"/>, <see cref="CommissionLegendaryWorkAction"/>) were added
    /// to <c>Actions.cs</c> without a matching <see cref="ActionLegality"/> case: they silently fell
    /// through to the switch's old <c>_ =&gt; false</c> arm forever, with no test ever failing,
    /// because a real "no" from a correctly mirrored guard and "nobody wrote a case yet" are BOTH
    /// just <c>false</c> — indistinguishable by reflection alone.
    ///
    /// <para>What makes this test able to tell them apart: <see cref="ActionLegality.IsLegal"/>'s
    /// fallthrough now THROWS <see cref="UnhandledActionException"/> instead of returning
    /// <c>false</c> (see <c>ActionLegality.cs</c>). For every discovered type this test builds a
    /// minimal, guard-safe instance (<see cref="BuildMinimalInstance"/>) and calls
    /// <see cref="ActionLegality.IsLegal"/> in every <see cref="DayPhase"/> — any real answer, true
    /// or false, passes; only the unhandled-case throw fails. A future action type added to
    /// Contracts without a mirrored case is caught the instant this test runs, regardless of whether
    /// any harness policy ever organically drives it.</para>
    /// </summary>
    [Fact]
    public void IsLegal_HasAnExplicitCase_ForEveryConcreteContractsActionType()
    {
        var actionTypes = typeof(PlayerAction).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(PlayerAction).IsAssignableFrom(t))
            .ToList();

        Assert.True(actionTypes.Count > 0,
            "Reflection found zero concrete PlayerAction types — the Contracts assembly/type lookup itself is broken.");

        var state = GameComposition.NewCampaign(Seed);
        var unhandled = new List<string>();

        foreach (var type in actionTypes)
        {
            var instance = BuildMinimalInstance(type);

            foreach (var phase in Enum.GetValues<DayPhase>())
            {
                try
                {
                    ActionLegality.IsLegal(state, instance, phase);
                }
                catch (UnhandledActionException)
                {
                    unhandled.Add($"{type.Name} (phase {phase})");
                }
            }
        }

        Assert.True(unhandled.Count == 0,
            $"ActionLegality.IsLegal has no case for: {string.Join(", ", unhandled)}");
    }

    /// <summary>Builds a minimal, guard-safe instance of a concrete <see cref="PlayerAction"/>
    /// record via its primary (positional) constructor, purely by reflection — never a
    /// hand-maintained fixture per type, so a newly added action type needs zero new code here to
    /// be picked up. Every constructor parameter gets the most inert value its type allows (empty
    /// string, zero/default, an empty immutable collection, or null for anything nullable) — chosen
    /// so every CURRENT guard fails its own precondition harmlessly before <see cref="ActionLegality"/>
    /// even finishes dispatching (every handler in this codebase reads via <c>TryGetValue</c>/<c>Any</c>,
    /// never a throwing indexer, on an absent key) rather than throwing something unrelated to the
    /// one thing this test checks: whether the switch has a case at all.</summary>
    private static PlayerAction BuildMinimalInstance(Type type)
    {
        var ctor = type
            .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(c => !(c.GetParameters().Length == 1 && c.GetParameters()[0].ParameterType == type))
            .OrderByDescending(c => c.GetParameters().Length)
            .First();

        var args = ctor.GetParameters().Select(p => MinimalArg(p.ParameterType)).ToArray();
        return (PlayerAction)ctor.Invoke(args);
    }

    private static object? MinimalArg(Type type)
    {
        if (type == typeof(string))
        {
            return string.Empty;
        }

        if (type == typeof(ImmutableSortedSet<string>))
        {
            return ImmutableSortedSet<string>.Empty;
        }

        if (type.IsValueType)
        {
            return Activator.CreateInstance(type);
        }

        return null; // any other nullable/reference param (ImmutableList<int>?, CraftPuzzleInput?, ...)
    }

    /// <summary>
    /// Phase B (B2, R-B5): CounterPlayer alone never crafts/stocks (by design, MF-8's doc above), so
    /// once traits give player-shelf heroes real teeth (a Discerning veteran refusing Common, a
    /// Sentimental hero clinging to already-worn gear, ...) a small FROZEN post-warmup shelf can run
    /// out of anything any queued hero will buy — no <see cref="HaggleResponseAction"/> ever becomes
    /// reachable, starving this file's coverage assertions on an otherwise-legitimate economy. Keep
    /// the shelf alive during the counter phase exactly the way a real morning would (the smith
    /// keeps crafting/stocking while working the counter) by also driving <see cref="BaselinePlayer"/>'s
    /// craft/stock actions every tick — different action types than the counter verbs, so the two
    /// batches never conflict, and CounterPlayer's own OpenCounter/Present/Haggle/Close behavior is
    /// unchanged.
    /// </summary>
    private static ImmutableList<PlayerAction> CounterPlayerWithOngoingSupply(GameState state) =>
        BaselinePlayer.ActionsFor(state).AddRange(CounterPlayer.ActionsFor(state));

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
    /// does for an organically-reached state. BaselinePlayer never sends supplies (D5: "no camp
    /// verbs, no deep actions"), so an organic run alone never manufactures this exact shape —
    /// construct the one concrete opportunity directly instead of accepting a vacuous "never
    /// observed" as coverage.</summary>
    [Fact]
    public void SendSupplyAction_ConcreteOpportunity_MirrorAgreesWithKernel()
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
    }

    /// <summary>
    /// Phase B (B2, R-B5): a concrete, deterministic <see cref="HaggleResponseAction"/> opportunity
    /// — one rookie hero with empty gear and gold to spare, offered a shelf item their class can
    /// wear, guaranteed to open a haggle round and accept at the standing offer. Same "construct the
    /// one concrete opportunity directly" precedent as
    /// <see cref="SendSupplyAction_ConcreteOpportunity_MirrorAgreesWithKernel"/>: once trait teeth
    /// are real (a Discerning veteran refusing Common, a Sentimental hero clinging to already-worn
    /// gear, ...), an ORGANIC multi-week run's survivors can end up entirely maxed-out or entirely
    /// gated — a rookie's very first purchase never hits any of those gates (KD3 no-softlock: the
    /// veteran gate is floor-depth gated; empty gear means no sentimental worn item either), so this
    /// fixture is unaffected by trait variance by construction.
    /// </summary>
    [Fact]
    public void HaggleResponseAction_ConcreteOpportunity_MirrorAgreesWithKernel()
    {
        var fresh = GameComposition.NewCampaign(Seed);
        var rookie = fresh.Heroes.Values.First(h => h.Alive);

        var weaponId = new ItemId(fresh.NextItemId);
        var sword = new Item(
            weaponId, "test-recipe", "Fixture Sword", ItemSlot.Weapon, QualityGrade.Common,
            new ItemStats(Attack: 9, Defense: 0, Weight: 3), Mark: null, ImmutableList<ItemHistoryEntry>.Empty);

        var heroState = rookie with
        {
            Gold = 1000,
            Gear = GearSet.Empty,
            Memories = ImmutableList<ItemMemory>.Empty,
            DeepestFloorReached = 0,
        };

        var state = fresh with
        {
            Phase = DayPhase.Morning,
            Heroes = fresh.Heroes.SetItem(rookie.Id.Value, heroState),
            Items = fresh.Items.Add(weaponId.Value, sword),
            NextItemId = fresh.NextItemId + 1,
            Player = fresh.Player with { Shelf = ImmutableList.Create(new ShelfEntry(weaponId, 10)) },
        };

        var kernel = GameComposition.BuildKernel();

        var openResult = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new OpenCounterAction()));
        Assert.True(openResult.Rejected.IsEmpty);
        state = openResult.NewState;
        Assert.Equal(heroState.Id, state.Counter?.Active); // the only alive hero this fixture cares about

        var presentResult = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new PresentItemAction(weaponId)));
        Assert.True(presentResult.Rejected.IsEmpty);
        state = presentResult.NewState;
        Assert.True(state.Counter is { Round: > 0, StandingOfferGold: not null },
            "HaggleResponse fixture: presenting a clear, affordable, empty-slot upgrade to a rookie never opened a round.");

        var accept = new HaggleResponseAction(HaggleResponseKind.Accept);
        Assert.True(ActionLegality.IsLegal(state, accept, DayPhase.Morning));

        var haggleResult = kernel.Tick(state, ImmutableList.Create<PlayerAction>(accept));
        Assert.True(haggleResult.Rejected.IsEmpty,
            $"HaggleResponse fixture: kernel rejected a fixture ActionLegality reported legal: " +
            $"{string.Join("; ", haggleResult.Rejected.Select(r => r.Reason))}");
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
