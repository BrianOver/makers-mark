using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Harness;
using GameSim.Heroes;
using GameSim.Kernel;
using GameSim; // GameComposition

namespace GameSim.Tests.Heroes;

/// <summary>
/// World rework U9/KTD8: <see cref="MusterSystem"/> predicts, at the Morning tick, exactly what
/// <c>BountyJudgingSystem</c>/<c>ExpeditionSystem</c> will authoritatively form two phases later.
/// Prediction and authority share the same pure helpers (<see cref="GameSim.Bounties.BountyRules.JudgeFirstAccept"/>,
/// <see cref="GameSim.Heroes.PartyFormation.FormParties"/>, <c>ExpeditionSystem.TargetFloorFor</c>) — these
/// tests pin that the two can never drift apart, plus the load-bearing registration order.
/// </summary>
public class MusterSystemTests
{
    [Fact]
    public void PredictedRoster_ByteMatches_ExpeditionSystem_Over100Days()
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(seed: 4242);

        for (var day = 0; day < 100; day++)
        {
            var morning = kernel.Tick(state, BaselinePlayer.ActionsFor(state));
            state = morning.NewState;
            var predicted = Assert.Single(morning.Events.OfType<PartiesFormed>());

            var expedition = kernel.Tick(state, BaselinePlayer.ActionsFor(state));
            state = expedition.NewState;
            var departed = expedition.Events.OfType<PartyDeparted>().ToList();

            Assert.Equal(predicted.Parties.Count, departed.Count);
            for (var i = 0; i < predicted.Parties.Count; i++)
            {
                Assert.Equal(predicted.Parties[i].Roster, departed[i].Party);
                Assert.Equal(predicted.Parties[i].TargetFloor, departed[i].TargetFloor);
            }

            // Camp -> ExpeditionDeep -> Evening close out the day.
            state = kernel.Tick(state, BaselinePlayer.ActionsFor(state)).NewState;
            state = kernel.Tick(state, BaselinePlayer.ActionsFor(state)).NewState;
            state = kernel.Tick(state, BaselinePlayer.ActionsFor(state)).NewState;
        }
    }

    [Fact]
    public void BountyOverrideDay_PredictedTargetFloor_MatchesLaterJudgedBounty()
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(seed: 7);

        // Morning: PostBountyAction applies before phase systems (kernel dispatch order), so
        // MusterSystem (last in the Morning block) sees the freshly escrowed bounty.
        var morning = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new PostBountyAction(1, 40)));
        state = morning.NewState;
        var predicted = Assert.Single(morning.Events.OfType<PartiesFormed>());

        var posted = Assert.Single(state.Bounties);
        Assert.Null(posted.AcceptedBy); // not authoritatively judged yet — that's the Expedition tick

        var expedition = kernel.Tick(state, ImmutableList<PlayerAction>.Empty);
        var departed = expedition.Events.OfType<PartyDeparted>().ToList();
        var accepted = expedition.Events.OfType<BountyJudged>().Where(j => j.Accepted).ToList();

        var acceptance = Assert.Single(accepted); // exactly one hero takes a rich, reachable floor-1 bounty
        var acceptor = acceptance.Hero;

        var actualParty = departed.Single(d => d.Party.Contains(acceptor));
        Assert.Equal(1, actualParty.TargetFloor);

        var predictedParty = predicted.Parties.Single(p => p.Roster.Contains(acceptor));
        Assert.Equal(actualParty.TargetFloor, predictedParty.TargetFloor);
        Assert.Equal(actualParty.Party, predictedParty.Roster);
    }

    [Fact]
    public void SameMorningRecruit_AppearsInEmittedRoster()
    {
        var state = HeroRoster.InstallStartingRoster(GameFactory.NewGame(seed: 3));

        // Kill one hero so the alive roster is short of six, and clear the recruit gate —
        // RecruitSystem mints a fresh recruit THIS Morning.
        var dead = state.Heroes[1] with { Alive = false, DiedOnDay = 1 };
        state = state with
        {
            Heroes = state.Heroes.SetItem(1, dead),
            Drama = state.Drama with { DaysUntilNextRecruit = 0 },
        };

        // Registration-order pin: RecruitSystem must run BEFORE MusterSystem (as it does in
        // GameComposition) for the same-morning recruit to appear in the emitted roster.
        var kernel = new GameKernel(
            ImmutableList.Create<IPhaseSystem>(new RecruitSystem(), new MusterSystem()),
            ImmutableList<IActionHandler>.Empty);

        var result = kernel.Tick(state, ImmutableList<PlayerAction>.Empty);

        var arrived = Assert.Single(result.Events.OfType<RecruitArrived>());
        var plan = Assert.Single(result.Events.OfType<PartiesFormed>());

        var mustered = plan.Parties.SelectMany(p => p.Roster).ToList();
        Assert.Contains(arrived.Hero, mustered);
    }

    [Fact]
    public void ZeroHeroMorning_EmitsEmptyPartiesFormed()
    {
        var state = GameFactory.NewGame(seed: 11); // no heroes installed
        var kernel = new GameKernel(
            ImmutableList.Create<IPhaseSystem>(new MusterSystem()),
            ImmutableList<IActionHandler>.Empty);

        var result = kernel.Tick(state, ImmutableList<PlayerAction>.Empty);

        var plan = Assert.Single(result.Events.OfType<PartiesFormed>());
        Assert.Empty(plan.Parties);
    }

    [Fact]
    public void FullCampaign_WithMuster_IsDeterministic()
    {
        string Run()
        {
            var kernel = GameComposition.BuildKernel();
            var state = GameComposition.NewCampaign(seed: 555);
            for (var i = 0; i < 30; i++) // 6 days at 5 ticks/day
            {
                state = kernel.Tick(state, BaselinePlayer.ActionsFor(state)).NewState;
            }

            return SaveCodec.Serialize(state);
        }

        Assert.Equal(Run(), Run());
    }
}
