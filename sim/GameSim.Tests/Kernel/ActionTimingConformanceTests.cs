using System.Collections.Immutable;
using System.Reflection;
using GameSim.Contracts;
using GameSim.Kernel;

namespace GameSim.Tests.Kernel;

/// <summary>
/// The SET pin the plan requires (2026-08-02 loop-legibility, U1/KTD-A): reflection-enumerates every
/// concrete <see cref="PlayerAction"/> type in the GameSim assembly and asserts each has an explicit
/// entry in <see cref="ExpectedLane"/> — never a hand-written subset a new action type could silently
/// miss. A new <see cref="PlayerAction"/> type fails <see cref="EveryConcretePlayerActionType_HasAnExplicitLaneEntry"/>
/// by name (it shows up as "in the assembly but not in the table"); flipping any single verb's lane in
/// <see cref="ActionTiming"/> without updating this file fails
/// <see cref="ActionTiming_ResolvesImmediately_MatchesTheExpectedLaneForEveryType"/> by name. Twelve
/// verbs moved Now under this plan's widening (KTD-A: "an action resolves NOW unless the WORLD must
/// move before the action means anything"); three remain deliberate bell-riders (construction, identity,
/// a pact with the Guild) — 21 Now, 3 Bell, 24 total.
/// </summary>
public class ActionTimingConformanceTests
{
    /// <summary>Every concrete <see cref="PlayerAction"/> type, mapped to whether it MUST resolve
    /// immediately (true = Now / <see cref="GameKernel.ApplyNow"/>, false = Bell / queued for
    /// <see cref="GameKernel.Tick"/>) and a factory that builds one instance to probe
    /// <see cref="ActionTiming.ResolvesImmediately"/> with. The factory's field values are arbitrary —
    /// only the TYPE drives the classifier (KTD-A: "when", never "whether").</summary>
    private static readonly Dictionary<Type, (bool Immediate, Func<PlayerAction> Make)> ExpectedLane = new()
    {
        // The workshop (2026-07-30 split) — Now.
        [typeof(BuyMaterialAction)] = (true, () => new BuyMaterialAction("copper", 1)),
        [typeof(BuyOreAction)] = (true, () => new BuyOreAction(new HeroId(1), "copper", 1)),
        [typeof(BuyForgeSupplyAction)] = (true, () => new BuyForgeSupplyAction("coal", 1)),
        [typeof(CraftAction)] = (true, () => new CraftAction("dagger", "copper")),
        [typeof(ReforgeHeirloomAction)] = (true, () => new ReforgeHeirloomAction(new ItemId(1), "dagger", "copper")),
        [typeof(MasterworkAttemptAction)] = (true, () => new MasterworkAttemptAction("dagger", "copper")),
        [typeof(StockAction)] = (true, () => new StockAction(new ItemId(1), 10)),
        [typeof(UnstockAction)] = (true, () => new UnstockAction(new ItemId(1))),
        [typeof(SetPriceAction)] = (true, () => new SetPriceAction(new ItemId(1), 10)),

        // The counter conversation (2026-08-02 widening) — Now.
        [typeof(OpenCounterAction)] = (true, () => new OpenCounterAction()),
        [typeof(PresentItemAction)] = (true, () => new PresentItemAction(new ItemId(1))),
        [typeof(SuggestItemAction)] = (true, () => new SuggestItemAction(new ItemId(1))),
        [typeof(HaggleResponseAction)] = (true, () => new HaggleResponseAction(HaggleResponseKind.Accept)),
        [typeof(CloseCounterAction)] = (true, () => new CloseCounterAction()),

        // Conversations with someone standing there (2026-08-02 widening) — Now.
        [typeof(AcceptCommissionAction)] = (true, () => new AcceptCommissionAction(new HeroId(1))),
        [typeof(DeclineCommissionAction)] = (true, () => new DeclineCommissionAction(new HeroId(1))),

        // Pinning paper to a board — Now (heroes reading it is the world's part, unchanged).
        [typeof(PostBountyAction)] = (true, () => new PostBountyAction(1, 25)),

        // Vigil's only two verbs (2026-08-02 widening) — Now.
        [typeof(SendSupplyAction)] = (true, () => new SendSupplyAction(new HeroId(1), new ItemId(1))),
        [typeof(RecallPartyAction)] = (true, () => new RecallPartyAction(new HeroId(1))),

        // Player-owned progression/rite state (2026-08-02 widening) — Now.
        [typeof(UnlockTalentAction)] = (true, () => new UnlockTalentAction("node", "blacksmith")),
        [typeof(HonorMemorialAction)] = (true, () => new HonorMemorialAction(new HeroId(1))),

        // The three deliberate ceremony verbs (KTD-A, open question 1) — Bell.
        [typeof(UpgradeForgeAction)] = (false, () => new UpgradeForgeAction()),
        [typeof(SetProfessionsAction)] = (false, () => new SetProfessionsAction(ImmutableSortedSet.Create("blacksmith"))),
        [typeof(CommissionLegendaryWorkAction)] = (false, () => new CommissionLegendaryWorkAction("dagger", "copper")),
    };

    private static IEnumerable<Type> ConcretePlayerActionTypesInAssembly() =>
        typeof(PlayerAction).Assembly.GetTypes()
            .Where(t => typeof(PlayerAction).IsAssignableFrom(t) && !t.IsAbstract && t.IsClass);

    [Fact]
    public void ExpectedLane_Has21NowAnd3Bell_24Total()
    {
        Assert.Equal(24, ExpectedLane.Count);
        Assert.Equal(21, ExpectedLane.Values.Count(v => v.Immediate));
        Assert.Equal(3, ExpectedLane.Values.Count(v => !v.Immediate));
    }

    /// <summary>The SET pin: every concrete <see cref="PlayerAction"/> the assembly actually defines
    /// must appear in <see cref="ExpectedLane"/> — a new action type with no entry fails HERE, by
    /// name, rather than silently defaulting to whatever <see cref="ActionTiming"/>'s deny-list falls
    /// through to.</summary>
    [Fact]
    public void EveryConcretePlayerActionType_HasAnExplicitLaneEntry()
    {
        var actual = ConcretePlayerActionTypesInAssembly().ToImmutableHashSet();
        var expected = ExpectedLane.Keys.ToImmutableHashSet();

        var undeclared = actual.Except(expected);
        Assert.True(undeclared.Count == 0,
            $"New PlayerAction type(s) with no ActionTimingConformanceTests entry: {string.Join(", ", undeclared.Select(t => t.Name))}");

        var stale = expected.Except(actual);
        Assert.True(stale.Count == 0,
            $"ActionTimingConformanceTests entries for type(s) no longer in the assembly: {string.Join(", ", stale.Select(t => t.Name))}");
    }

    /// <summary>The BEHAVIOUR pin: <see cref="ActionTiming.ResolvesImmediately"/> must agree with
    /// <see cref="ExpectedLane"/> for every type — flip a single verb's lane in <see cref="ActionTiming"/>
    /// and this fails by name.</summary>
    [Fact]
    public void ActionTiming_ResolvesImmediately_MatchesTheExpectedLaneForEveryType()
    {
        foreach (var (type, (immediate, make)) in ExpectedLane)
        {
            var instance = make();
            Assert.True(instance.GetType() == type, $"Factory for {type.Name} built a {instance.GetType().Name} instead.");
            Assert.True(
                ActionTiming.ResolvesImmediately(instance) == immediate,
                $"{type.Name}: expected ResolvesImmediately == {immediate} but got {!immediate}.");
        }
    }
}
