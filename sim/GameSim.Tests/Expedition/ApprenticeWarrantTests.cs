using System.Collections.Immutable;
using GameSim;
using GameSim.Contracts;
using GameSim.Expedition;
using GameSim.Harness;
using GameSim.Kernel;
using GameSim.Venues;

namespace GameSim.Tests.Expedition;

/// <summary>
/// §11.13 amendment (U4): <see cref="ApprenticeWarrant"/>'s own unit-brief scenarios
/// (docs/design/MAKERS-MARK.md's U4 "Test scenarios" list, 1-8; scenarios 9-10 are the EXISTING
/// pins <c>ConsumableTraitMortalityBalanceTests.SalvesStocked_PreparedHeroes_SurviveMeasurablyBetterThanReckless</c>
/// and <c>BalanceSimTests.HundredDay_Bands_Hold_OnMainSeed</c>, re-verified by the Balance gate run,
/// not duplicated here). Uses <see cref="DeathClearsFloorTests"/>'s own precedent — a lone, frail,
/// unarmed hero reliably dies within a bounded seed sweep — rather than a scripted/mock RNG, so
/// every assertion drives the REAL <see cref="CombatMath"/>/<see cref="ExpeditionResolver"/> path.
/// </summary>
public class ApprenticeWarrantTests
{
    private static Hero Frail(int id) => new(
        new HeroId(id), $"Frail{id}", "mystic", Level: 1, MaxHp: 8, Gold: 10,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty, Alive: true, DeepestFloorReached: 0, DiedOnDay: null);

    /// <summary>
    /// The first seed (of a bounded sweep) whose UNWARRANTED run kills the lone frail hero —
    /// <c>DeathClearsFloorTests</c>' own precedent for "a weak solo hero reliably dies within N
    /// seeds." A helper, not a literal, so a future combat-math change that shifts which seed kills
    /// first cannot silently make this suite vacuous — the search re-proves the premise every run.
    /// </summary>
    private static ulong FindLethalSeed(int targetFloor, int maxSeed = 500)
    {
        for (ulong seed = 0; seed < (ulong)maxSeed; seed++)
        {
            var party = ImmutableList.Create(Frail(1));
            var result = ExpeditionResolver.Resolve(
                party, ImmutableSortedDictionary<int, Item>.Empty, VenueRegistry.Mine, targetFloor,
                new Pcg32(RngState.FromSeed(seed)));
            if (result.Deaths.Count > 0)
            {
                return seed;
            }
        }

        throw new InvalidOperationException(
            $"no lethal seed found for floor {targetFloor} within {maxSeed} seeds — fixture premise broken");
    }

    [Fact]
    public void WarrantHolds_ALethalRollAtOneHp_OnDay3()
    {
        var seed = FindLethalSeed(targetFloor: 3);
        var party = ImmutableList.Create(Frail(1));

        var unwarranted = ExpeditionResolver.Resolve(
            party, ImmutableSortedDictionary<int, Item>.Empty, VenueRegistry.Mine, 3, new Pcg32(RngState.FromSeed(seed)));
        Assert.NotEmpty(unwarranted.Deaths); // the fixture premise: this roll kills without the warrant

        var warranted = ExpeditionResolver.Resolve(
            party, ImmutableSortedDictionary<int, Item>.Empty, VenueRegistry.Mine, 3, new Pcg32(RngState.FromSeed(seed)),
            warrantHolds: true);

        Assert.Empty(warranted.Deaths);
        Assert.Contains(new HeroId(1), warranted.Survivors);

        var saves = ApprenticeWarrant.FiredIn(warranted);
        Assert.NotEmpty(saves);
        Assert.True(saves[0].DamageTaken > 0); // the true lethal roll, recorded
    }

    [Fact]
    public void WarrantExpires_AtDay4_SameRollKills()
    {
        // The resolver itself takes no Day parameter — Day is where ExpeditionSystem/
        // ExpeditionDeepSystem compute warrantHolds via Covers(state), so the boundary is proven at
        // that level (below); at the resolver level, "expired" means simply warrantHolds: false,
        // and the same lethal roll kills exactly as it always has.
        var seed = FindLethalSeed(targetFloor: 3);
        var party = ImmutableList.Create(Frail(1));

        var result = ExpeditionResolver.Resolve(
            party, ImmutableSortedDictionary<int, Item>.Empty, VenueRegistry.Mine, 3, new Pcg32(RngState.FromSeed(seed)),
            warrantHolds: false);
        Assert.NotEmpty(result.Deaths);

        var state = GameComposition.NewCampaign(1) with { Day = ApprenticeWarrant.LastGraceDay + 1 };
        Assert.False(ApprenticeWarrant.Covers(state));
    }

    [Fact]
    public void WarrantEnds_TheTickAfterConcludeApprenticeship()
    {
        var state = GameComposition.NewCampaign(1) with { Day = 2 };
        Assert.True(ApprenticeWarrant.Covers(state));

        var kernel = GameComposition.BuildKernel();
        var afterConclude = kernel.ApplyNow(state, new ConcludeApprenticeshipAction()).NewState;

        Assert.True(ApprenticeWarrant.Concluded(afterConclude));
        Assert.False(ApprenticeWarrant.Covers(afterConclude)); // the very next read, no tick needed
    }

    [Fact]
    public void HeldHero_FleesNextRound_ViaTheExistingShouldFleeCheck()
    {
        var seed = FindLethalSeed(targetFloor: 3);
        var party = ImmutableList.Create(Frail(1));

        var warranted = ExpeditionResolver.Resolve(
            party, ImmutableSortedDictionary<int, Item>.Empty, VenueRegistry.Mine, 3, new Pcg32(RngState.FromSeed(seed)),
            warrantHolds: true);

        var saves = ApprenticeWarrant.FiredIn(warranted);
        Assert.NotEmpty(saves);
        var savedFloor = saves[0].Floor;

        var floorOutcome = warranted.Floors.First(f => f.Floor == savedFloor);
        // No new retreat path: the hero neither dies nor clears this floor — the EXISTING
        // ShouldFlee check (top of the next round) sends a 1-HP hero home instead. A flee draws no
        // additional CombatEvent (ShouldFlee returns before one is recorded), so the clamp's own
        // exchange is the LAST recorded event for this hero on this floor.
        Assert.False(floorOutcome.Cleared);
        var heroEventsThisFloor = floorOutcome.Combats.Where(c => c.Hero == new HeroId(1)).ToList();
        Assert.True(heroEventsThisFloor[^1].ModifierHpDelta > 0);
        Assert.Contains(new HeroId(1), warranted.Survivors);
    }

    [Fact]
    public void WarrantedFight_StillRecordsTheTrueLethalRoll_ForAttributionReplay()
    {
        var seed = FindLethalSeed(targetFloor: 3);
        var party = ImmutableList.Create(Frail(1));

        var unwarranted = ExpeditionResolver.Resolve(
            party, ImmutableSortedDictionary<int, Item>.Empty, VenueRegistry.Mine, 3, new Pcg32(RngState.FromSeed(seed)));
        var warranted = ExpeditionResolver.Resolve(
            party, ImmutableSortedDictionary<int, Item>.Empty, VenueRegistry.Mine, 3, new Pcg32(RngState.FromSeed(seed)),
            warrantHolds: true);

        // Identical rng + identical party means the two runs draw byte-identical rolls up to the
        // exact exchange that kills (unwarranted) / gets clamped (warranted) — the SAME floor, SAME
        // round, SAME DamageTaken. The clamp changes hp/ModifierHpDelta only, never the recorded roll.
        var lethalRound = unwarranted.Floors.SelectMany(f => f.Combats).Last(c => c.Hero == new HeroId(1));
        var heldRound = warranted.Floors
            .SelectMany(f => f.Combats)
            .First(c => c.Hero == new HeroId(1) && c.Floor == lethalRound.Floor && !c.MonsterKilled && c.ModifierHpDelta > 0);

        Assert.Equal(lethalRound.DamageTaken, heldRound.DamageTaken);
        Assert.True(heldRound.DamageTaken > 0);
    }

    [Fact]
    public void DirectResolverCalls_AreUnaffected()
    {
        var seed = FindLethalSeed(targetFloor: 3);
        var party = ImmutableList.Create(Frail(1));

        // The exact DeathClearsFloorTests call shape — no warrantHolds argument at all (KTD-D:
        // the parameter defaults off at the resolver's own seam).
        var result = ExpeditionResolver.Resolve(
            party, ImmutableSortedDictionary<int, Item>.Empty, VenueRegistry.Mine, 3, new Pcg32(RngState.FromSeed(seed)));

        Assert.NotEmpty(result.Deaths); // unchanged: a direct call with the parameter omitted still kills
    }

    [Fact]
    public void FiredPredicate_AgreesWithTheClampByConstruction()
    {
        var seed = FindLethalSeed(targetFloor: 3);
        var party = ImmutableList.Create(Frail(1));

        var warranted = ExpeditionResolver.Resolve(
            party, ImmutableSortedDictionary<int, Item>.Empty, VenueRegistry.Mine, 3, new Pcg32(RngState.FromSeed(seed)),
            warrantHolds: true);

        var saves = ApprenticeWarrant.FiredIn(warranted);
        Assert.NotEmpty(saves);

        // Every save FiredIn reports IS exactly the shape the resolver's own clamp produces
        // (!MonsterKilled, ModifierHpDelta > 0) — the SAME classification, never a second
        // computation that could disagree with it (KTD-E).
        foreach (var save in saves)
        {
            var floor = warranted.Floors.First(f => f.Floor == save.Floor);
            var evt = floor.Combats.Single(c =>
                c.Hero == save.Hero && c.DamageTaken == save.DamageTaken && c.ModifierHpDelta > 0 && !c.MonsterKilled);
            Assert.Equal(save.MonsterKind, evt.MonsterKind);
        }
    }

    /// <summary>
    /// Test scenario 8: the harness policies (<c>sim/GameSim/Harness/</c>) never submit
    /// <see cref="ConcludeApprenticeshipAction"/>, so the dated warrant alone must hold through days
    /// 1-3 across every seed — driven through the REAL composed kernel (every registered system),
    /// not the resolver in isolation.
    /// </summary>
    [Theory]
    [Trait("Category", "Balance")]
    [InlineData(1UL)]
    [InlineData(7UL)]
    [InlineData(42UL)]
    [InlineData(99UL)]
    [InlineData(1234UL)]
    [InlineData(2026UL)]
    public void NoHeroDiedEvent_OnDays1Through3_AcrossAllSeeds(ulong seed)
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(seed);

        var ticks = 0;
        while (state.Day <= ApprenticeWarrant.LastGraceDay && ticks < 100)
        {
            var result = kernel.Tick(state, BaselinePlayer.ActionsFor(state));
            state = result.NewState;
            Assert.DoesNotContain(result.Events, e => e is HeroDied);
            ticks++;
        }

        Assert.True(ticks is > 0 and < 100, "loop guard: either never ran or never left the warrant window");
    }
}
