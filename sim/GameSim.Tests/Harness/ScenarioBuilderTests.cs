using System.Linq;
using GameSim.Contracts;
using GameSim.Harness;
using GameSim.Kernel;

namespace GameSim.Tests.Harness;

/// <summary>
/// U18 (§11.14.14, "a tester can stand on day 4"): coverage for <see cref="ScenarioBuilder"/>, the
/// pure-sim half of the dev-gated scenario writer. The Godot-side half (writing the manufactured
/// state through Continue's real load path, with the tutorial chain honestly mid-flight) is covered
/// by <c>ScenarioWriterTests</c> in the engine suite — nothing here needs Godot, which is exactly
/// why the determinism/precondition claims live here instead: they run in the fast lane, on every
/// PR, in milliseconds.
/// </summary>
public class ScenarioBuilderTests
{
    [Fact]
    public void SameSeedSameDay_ProducesByteIdenticalState_Twice()
    {
        var a = SaveCodec.Serialize(ScenarioBuilder.BuildDay(seed: 1, day: 4));
        var b = SaveCodec.Serialize(ScenarioBuilder.BuildDay(seed: 1, day: 4));
        Assert.Equal(a, b);
    }

    [Fact]
    public void DifferentSeeds_Diverge()
    {
        var a = SaveCodec.Serialize(ScenarioBuilder.BuildDay(seed: 1, day: 4));
        var b = SaveCodec.Serialize(ScenarioBuilder.BuildDay(seed: 2, day: 4));
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Day1_ReturnsTheUntouchedCampaignStart_ZeroTicks()
    {
        var expected = SaveCodec.Serialize(GameComposition.NewCampaign(seed: 1));
        var actual = SaveCodec.Serialize(ScenarioBuilder.BuildDay(seed: 1, day: 1));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void InvalidDay_Throws()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() => ScenarioBuilder.BuildDay(seed: 1, day: 0));
    }

    /// <summary>One tick per phase, never a fixed phase count — every requested day must land
    /// EXACTLY at that day's own Morning, the boundary a real autosave would have written.</summary>
    [Fact]
    public void BuildDay_AlwaysLandsAtThatDaysMorning()
    {
        for (var day = 1; day <= 6; day++)
        {
            var state = ScenarioBuilder.BuildDay(seed: 3, day: day);
            Assert.Equal(day, state.Day);
            Assert.Equal(DayPhase.Morning, state.Phase);
        }
    }

    /// <summary>
    /// The measurement U18's whole premise rests on (§11.14.14, "The finding that sets the scope"):
    /// measured 2026-08-21 under <c>batch --seeds 12 --days 10</c>/<see cref="BaselinePlayer"/>,
    /// <c>attributionBeat</c> — the counterfactual proof, link 4 — first fires on day 4 across all
    /// twelve of the doc's own measured seeds (1 through 12, <c>BatchRunner</c>'s own default start
    /// seed). This does not repeat that measurement; it proves a DAY-4 SCENARIO SAVE genuinely sits
    /// on the doorstep of that fact for the same twelve seeds — continuing play from exactly the
    /// state this unit would write, for the rest of day 4 only, must reach the beat. If a scripted
    /// player ever stops reliably producing it, U18's own "stand on day 4 with the chain live"
    /// premise is false for that seed — this is asserted, not weakened, so that would show up here
    /// rather than being quietly papered over.
    /// </summary>
    [Theory]
    [InlineData(1ul)] [InlineData(2ul)] [InlineData(3ul)] [InlineData(4ul)] [InlineData(5ul)] [InlineData(6ul)]
    [InlineData(7ul)] [InlineData(8ul)] [InlineData(9ul)] [InlineData(10ul)] [InlineData(11ul)] [InlineData(12ul)]
    public void Day4Scenario_ReliablyReachesTheAttributionBeat_WithinDay4(ulong seed)
    {
        var state = ScenarioBuilder.BuildDay(seed, day: 4);
        var kernel = GameComposition.BuildKernel();

        var sawBeat = false;
        while (state.Day == 4)
        {
            var result = kernel.Tick(state, BaselinePlayer.ActionsFor(state));
            sawBeat |= result.Events.OfType<AttributionBeatEvent>().Any();
            state = result.NewState;
        }

        Assert.True(sawBeat,
            $"seed {seed}: no attributionBeat fired during day 4 — U18's day-4 premise does not hold for this seed.");
    }
}
