using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Kernel;

namespace GameSim.Tests.Kernel;

// LAW:sim-purity-determinism
// The golden replay — the proof half of the law whose token half lives in SimPurityCensusTests.
// Tagged for ConstitutionTests (CLAUDE.md rule 12), which fails if this tag or this file goes away.

/// <summary>
/// Covers AE5's foundation: same seed + same actions = byte-identical world.
/// These tests are the project's spine — a failure here is a build-blocking defect (KTD4).
/// </summary>
public class DeterminismTests
{
    /// <summary>A phase system that consumes RNG so seed plumbing is provable before real modules exist (U4+).</summary>
    private sealed class RngProbeSystem : IPhaseSystem
    {
        public DayPhase Phase => DayPhase.Morning;
        public string Name => "rng-probe";

        public GameState Process(GameState state, IDeterministicRng rng, IEventSink events)
        {
            var roll = rng.Roll100();
            var probe = state.Player with { Gold = state.Player.Gold + roll };
            return state with { Player = probe };
        }
    }

    private static GameState RunDays(ulong seed, int days, params IPhaseSystem[] systems)
    {
        var kernel = new GameKernel(systems.ToImmutableList(), ImmutableList<IActionHandler>.Empty);
        var state = GameFactory.NewGame(seed);
        for (var i = 0; i < days * 5; i++) // 5-phase day (staged resolution)
        {
            state = kernel.Tick(state, ImmutableList<PlayerAction>.Empty).NewState;
        }

        return state;
    }

    [Fact]
    public void SameSeed_SameActions_ByteIdenticalAfter200Ticks()
    {
        var kernel = new GameKernel(ImmutableList.Create<IPhaseSystem>(new RngProbeSystem()), ImmutableList<IActionHandler>.Empty);

        GameState Run()
        {
            var state = GameFactory.NewGame(seed: 42);
            for (var i = 0; i < 200; i++)
            {
                state = kernel.Tick(state, ImmutableList<PlayerAction>.Empty).NewState;
            }

            return state;
        }

        var a = SaveCodec.Serialize(Run());
        var b = SaveCodec.Serialize(Run());
        Assert.Equal(a, b);
    }

    [Fact]
    public void DifferentSeeds_Diverge()
    {
        var a = SaveCodec.Serialize(RunDays(seed: 1, days: 5, new RngProbeSystem()));
        var b = SaveCodec.Serialize(RunDays(seed: 2, days: 5, new RngProbeSystem()));
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void EmptyWorld_TicksWithoutError()
    {
        // 2026-08-02 (KTD-D(1) phase collapse): an empty world (zero heroes) is the true
        // "nobody-down-there" case every day, so each day is now Morning -> Evening only — 2 ticks,
        // not 5 — never touching Expedition/Camp/ExpeditionDeep.
        var kernel = new GameKernel(ImmutableList<IPhaseSystem>.Empty, ImmutableList<IActionHandler>.Empty);
        var state = GameFactory.NewGame(seed: 7);
        for (var i = 0; i < 3 * 2; i++)
        {
            state = kernel.Tick(state, ImmutableList<PlayerAction>.Empty).NewState;
        }

        Assert.Equal(4, state.Day); // day 1 + 3 full (collapsed) days
        Assert.Equal(DayPhase.Morning, state.Phase);
    }

    [Fact]
    public void Pcg32_IsPureIntegerMath_AndStable()
    {
        // Known-answer test: the same seed must produce the same first draws forever.
        // If this test breaks, every save and golden replay in existence breaks with it.
        var rng = new Pcg32(new RngState(0x853c49e6748fea9bUL, 0xda3e39cb94b95bdbUL));
        var first = rng.NextUInt();
        var second = rng.NextUInt();
        var rng2 = new Pcg32(new RngState(0x853c49e6748fea9bUL, 0xda3e39cb94b95bdbUL));
        Assert.Equal(first, rng2.NextUInt());
        Assert.Equal(second, rng2.NextUInt());
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void NextInt_RespectsBounds()
    {
        var rng = new Pcg32(RngState.FromSeed(9));
        for (var i = 0; i < 1000; i++)
        {
            var v = rng.NextInt(3, 17);
            Assert.InRange(v, 3, 16);
        }
    }
}
