using System.Collections.Immutable;
using System.Linq;
using GameSim;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Kernel;
using GameSim.Venues;

namespace GameSim.Tests.Drama;

using static DramaFixtures;

/// <summary>
/// Phase C U-C3 (drama director + den escalation). Covers: the BuildUp/Peak/Relax pacing machine and
/// its min-duration counters; exactly-one-draw-per-day; the NO-WEALTH-input hard invariant (raising
/// gold changes nothing the director fires, while progression does); the refire guard + drought pity;
/// and den threshold category shifts + lockdown. A separate <c>Category=Balance</c> fact pins a sane
/// long-run incident rate.
/// </summary>
public class DirectorSystemTests
{
    // ── Test doubles ────────────────────────────────────────────────────────────────────────────

    /// <summary>Wraps the real <see cref="Pcg32"/> and counts LOGICAL draw calls (NextInt/NextUInt/
    /// Roll100) at the decorator boundary — the internal Lemire rejection inside a single NextInt runs
    /// on the inner stream and is NOT counted, so one logical draw reads as one.</summary>
    private sealed class CountingRng(RngState seed) : IDeterministicRng
    {
        private readonly Pcg32 _inner = new(seed);
        public int Draws { get; private set; }

        public uint NextUInt() { Draws++; return _inner.NextUInt(); }
        public int NextInt(int lo, int hi) { Draws++; return _inner.NextInt(lo, hi); }
        public int Roll100() { Draws++; return _inner.Roll100(); }
    }

    private sealed class CollectingSink : IEventSink
    {
        public List<GameEvent> Events { get; } = [];
        public void Emit(GameEvent gameEvent) => Events.Add(gameEvent);
    }

    private static GameState WithDeepest(GameState state, int floor)
    {
        foreach (var id in state.Heroes.Keys.ToList())
        {
            var hero = state.Heroes[id];
            state = state with { Heroes = state.Heroes.SetItem(id, hero with { DeepestFloorReached = floor }) };
        }

        return state;
    }

    private static (GameState State, IReadOnlyList<GameEvent> Events) RunDirector(GameState state)
    {
        var rng = new Pcg32(state.Rng);
        var sink = new CollectingSink();
        var next = new DirectorSystem().Process(state, rng, sink);
        return (next, sink.Events);
    }

    // ── Pacing state machine + min-durations (pure DirectorPacing.Step) ──────────────────────────

    [Fact]
    public void Pacing_BuildUp_StaysBelowPeakThreshold()
    {
        // Tension well under PeakEnter → BuildUp holds, decay + delta applied.
        var (t, phase, entered) = DirectorPacing.Step(300, DirectorPhase.BuildUp, 1, 5, eventDelta: 0);
        Assert.Equal(DirectorPhase.BuildUp, phase);
        Assert.Equal(300 - DirectorSystem.DailyDecay, t);
        Assert.Equal(1, entered); // no transition → dwell counter untouched
    }

    [Fact]
    public void Pacing_BuildUp_EscalatesToPeak_WhenTensionHighAndDwellMet()
    {
        // Incoming 620, +40 delta − 40 decay = 620 ≥ PeakEnter; dwell 5-1 = 4 ≥ MinBuildUpDays.
        var (t, phase, entered) = DirectorPacing.Step(620, DirectorPhase.BuildUp, 1, 5, eventDelta: 40);
        Assert.Equal(DirectorPhase.Peak, phase);
        Assert.True(t >= DirectorPacing.PeakEnterTension);
        Assert.Equal(5, entered); // transition resets dwell to the current day
    }

    [Fact]
    public void Pacing_BuildUp_MinDurationBlocksEarlyPeak()
    {
        // Tension is over threshold but the phase has only dwelled 1 day (< MinBuildUpDays) → HOLD.
        var (_, phase, entered) = DirectorPacing.Step(900, DirectorPhase.BuildUp, 5, 6, eventDelta: 0);
        Assert.Equal(DirectorPhase.BuildUp, phase);
        Assert.Equal(5, entered);
    }

    [Fact]
    public void Pacing_Peak_CoolsToRelax_WhenTensionBleedsBelowExit()
    {
        // Low tension (a fire dropped it) + dwell ≥ MinPeakDays → Relax.
        var (_, phase, entered) = DirectorPacing.Step(150, DirectorPhase.Peak, 4, 6, eventDelta: 0);
        Assert.Equal(DirectorPhase.Relax, phase);
        Assert.Equal(6, entered);
    }

    [Fact]
    public void Pacing_Peak_MaxDwellForcesRelax_EvenWhenTensionHigh()
    {
        // Tension stays high but the peak has run MaxPeakDays → safety cool so the town still cycles.
        var (_, phase, _) = DirectorPacing.Step(1000, DirectorPhase.Peak, 2, 2 + DirectorPacing.MaxPeakDays, eventDelta: 0);
        Assert.Equal(DirectorPhase.Relax, phase);
    }

    [Fact]
    public void Pacing_Peak_HoldsBeforeMinDwell()
    {
        // Low tension but only 0 days dwell (< MinPeakDays would be the guard; here dwell 0) → hold.
        var (_, phase, _) = DirectorPacing.Step(100, DirectorPhase.Peak, 6, 6, eventDelta: 0);
        Assert.Equal(DirectorPhase.Peak, phase);
    }

    [Fact]
    public void Pacing_Relax_ReturnsToBuildUp_AfterMinDwell()
    {
        var (_, phase, entered) = DirectorPacing.Step(100, DirectorPhase.Relax, 4, 4 + DirectorPacing.MinRelaxDays, eventDelta: 0);
        Assert.Equal(DirectorPhase.BuildUp, phase);
        Assert.Equal(4 + DirectorPacing.MinRelaxDays, entered);
    }

    [Fact]
    public void Pacing_TensionClampsToBounds()
    {
        var high = DirectorPacing.Step(990, DirectorPhase.BuildUp, 1, 2, eventDelta: 500);
        Assert.Equal(DirectorSystem.TensionMax, high.Tension);

        var low = DirectorPacing.Step(10, DirectorPhase.BuildUp, 1, 2, eventDelta: 0);
        Assert.Equal(DirectorSystem.TensionMin, low.Tension); // 10 − 40 clamps to 0
    }

    // ── Exactly one draw per day ────────────────────────────────────────────────────────────────

    [Fact]
    public void Poll_DrawsExactlyOncePerDay()
    {
        var state = NewWorld();
        var rng = new CountingRng(state.Rng);
        new DirectorSystem().Process(state, rng, new CollectingSink());
        Assert.Equal(1, rng.Draws);
    }

    [Fact]
    public void Poll_HeldMorning_DrawsNothing()
    {
        // A stepped counter session open (Closed:false) holds the Morning → the director must NOT poll
        // (or it would draw multiple times per calendar day).
        var state = NewWorld() with { Counter = CounterState.Empty };
        var rng = new CountingRng(state.Rng);
        new DirectorSystem().Process(state, rng, new CollectingSink());
        Assert.Equal(0, rng.Draws);
    }

    // ── No-wealth-input hard invariant (the RimWorld wealth-spiral guard) ────────────────────────

    [Fact]
    public void Escalation_IgnoresWealth_ButRespondsToProgression()
    {
        // Rich progression: deep floors + a full veteran roster → all categories eligible.
        var baseState = WithDeepest(NewWorld(), 5)
            with { Director = DirectorState.Empty with { DroughtDays = DirectorSystem.DroughtPityDays } };

        var (_, poorEvents) = RunDirector(baseState);
        var firedPoor = poorEvents.OfType<IncidentFired>().Single();
        var eligiblePoor = DirectorSystem.EligibleIds(baseState);

        // Same world, obscene gold everywhere. If the director read wealth, the fire would move.
        var richHeroes = baseState.Heroes;
        foreach (var id in richHeroes.Keys.ToList())
        {
            richHeroes = richHeroes.SetItem(id, richHeroes[id] with { Gold = 9_999_999 });
        }

        var wealthy = baseState with
        {
            Player = baseState.Player with { Gold = 9_999_999 },
            Heroes = richHeroes,
        };

        var (_, richEvents) = RunDirector(wealthy);
        var firedRich = richEvents.OfType<IncidentFired>().Single();

        // Wealth changed NOTHING the director fired or considered eligible.
        Assert.Equal(firedPoor.IncidentId, firedRich.IncidentId);
        Assert.Equal(firedPoor.Category, firedRich.Category);
        Assert.Equal(firedPoor.Magnitude, firedRich.Magnitude);
        // ImmutableArray<T>.Equals is reference equality of the backing array, so compare as sequences.
        Assert.Equal(eligiblePoor.ToList(), DirectorSystem.EligibleIds(wealthy).ToList());

        // …but PROGRESSION does (the invariant is not vacuously true): a shallow town has a strictly
        // smaller eligible set.
        var shallow = WithDeepest(baseState, 1);
        Assert.True(DirectorSystem.EligibleIds(shallow).Length < eligiblePoor.Length);
    }

    // ── Refire guard + drought pity ─────────────────────────────────────────────────────────────

    [Fact]
    public void RefireGuard_SuppressesFireWithinCooldown()
    {
        // Peak-worthy tension, but last fired yesterday (< MinRefireDays) and drought not yet at pity.
        var state = NewWorld() with
        {
            Day = 10,
            Director = new DirectorState(
                Tension: 800, Phase: DirectorPhase.Peak, PhaseEnteredDay: 8, LastFiredDay: 9, DroughtDays: 1),
        };

        var (next, events) = RunDirector(state);
        Assert.Empty(events.OfType<IncidentFired>());
        Assert.Equal(2, next.Director.DroughtDays); // dry poll increments the drought counter
    }

    [Fact]
    public void Peak_FiresOnceRefireCooldownElapsed()
    {
        var state = NewWorld() with
        {
            Day = 10,
            Director = new DirectorState(
                Tension: 800, Phase: DirectorPhase.Peak, PhaseEnteredDay: 8, LastFiredDay: 5, DroughtDays: 4),
        };

        var (next, events) = RunDirector(state);
        Assert.Single(events.OfType<IncidentFired>());
        Assert.Equal(10, next.Director.LastFiredDay);
        Assert.Equal(0, next.Director.DroughtDays); // reset on fire
        Assert.True(next.Director.Tension <= 800 - DirectorSystem.FireRelief + 0); // relief applied
    }

    [Fact]
    public void DroughtPity_ForcesFire_OutsidePeak()
    {
        // BuildUp (never fires normally) but the drought has hit the pity threshold → force-fire.
        var atPity = NewWorld() with
        {
            Day = 20,
            Director = new DirectorState(
                Tension: 100, Phase: DirectorPhase.BuildUp, PhaseEnteredDay: 18,
                LastFiredDay: 1, DroughtDays: DirectorSystem.DroughtPityDays),
        };
        var (_, pityEvents) = RunDirector(atPity);
        Assert.Single(pityEvents.OfType<IncidentFired>());

        // One below the threshold → no fire, drought advances to the threshold for next time.
        var belowPity = atPity with
        {
            Director = atPity.Director with { DroughtDays = DirectorSystem.DroughtPityDays - 1 },
        };
        var (next, dryEvents) = RunDirector(belowPity);
        Assert.Empty(dryEvents.OfType<IncidentFired>());
        Assert.Equal(DirectorSystem.DroughtPityDays, next.Director.DroughtDays);
    }

    // ── Den escalation: category shift + lockdown (pure DenStep) ─────────────────────────────────

    [Fact]
    public void Den_ScheduledIncrement_ShiftsCategoryAtThreshold()
    {
        // 240 (tier 0) + 18 daily = 258 → crosses the 250 boundary to tier 1.
        var prev = new VenueState(DaysUntouched: 3, InfectionPerMille: 240, Closed: false, ThreatTier: 0);
        var (next, shifted) = DirectorSystem.DenStep(prev, clears: 0, surge: 0);
        Assert.Equal(258, next.InfectionPerMille);
        Assert.Equal(1, next.ThreatTier);
        Assert.True(shifted);
        Assert.Equal(4, next.DaysUntouched); // an untouched day grows the counter
    }

    [Fact]
    public void Den_ClearedExpeditions_RelieveThreat()
    {
        // 300 + 18 daily − 5 clears × 30 relief = 168 → back to tier 0.
        var prev = new VenueState(DaysUntouched: 6, InfectionPerMille: 300, Closed: false, ThreatTier: 1);
        var (next, shifted) = DirectorSystem.DenStep(prev, clears: 5, surge: 0);
        Assert.Equal(168, next.InfectionPerMille);
        Assert.Equal(0, next.ThreatTier);
        Assert.True(shifted);
        Assert.Equal(0, next.DaysUntouched); // a cleared day resets the untouched counter
    }

    [Fact]
    public void Den_LatchesLockdownAtCap()
    {
        // 990 + 18 + a 120 incident surge overflows the 1000 cap → lockdown latches.
        var prev = new VenueState(DaysUntouched: 9, InfectionPerMille: 990, Closed: false, ThreatTier: 3);
        var (next, shifted) = DirectorSystem.DenStep(prev, clears: 0, surge: DirectorSystem.DenIncidentSurge);
        Assert.Equal(DirectorSystem.DenThreatCap, next.InfectionPerMille);
        Assert.True(next.Closed);
        Assert.True(shifted);
    }

    [Fact]
    public void Den_LockdownIsSticky_EvenWhenThreatRelieved()
    {
        // Once locked, heavy relief brings the meter down but the closure stays latched (no reopen here).
        var prev = new VenueState(DaysUntouched: 0, InfectionPerMille: 1000, Closed: true, ThreatTier: 3);
        var (next, shifted) = DirectorSystem.DenStep(prev, clears: 5, surge: 0);
        Assert.True(next.Closed);
        Assert.Equal(868, next.InfectionPerMille);
        Assert.False(shifted); // tier 3→3, locked→locked: nothing new to announce
    }

    // ── Balance: sane long-run incident pacing + den invariants ──────────────────────────────────

    [Fact]
    [Trait("Category", "Balance")]
    public void HundredDay_IncidentRateIsSane_AndDenStaysInBounds()
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(seed: 2026);
        var fires = 0;

        for (var tick = 0; tick < 100 * 5; tick++)
        {
            var result = kernel.Tick(state, ImmutableList<PlayerAction>.Empty);
            state = result.NewState;
            fires += result.Events.OfType<IncidentFired>().Count();
        }

        // Not silent, not spammy: pity guarantees a floor (~8 over 100 quiet days) and the pacing
        // machine + refire guard cap the ceiling. Band is the tuning record — change consciously.
        Assert.InRange(fires, 5, 90);

        // Every live den's threat meter and tier stayed inside their declared bounds all run.
        foreach (var venueId in VenueRegistry.LiveRotation)
        {
            if (state.Venues.TryGetValue(venueId, out var den))
            {
                Assert.InRange(den.InfectionPerMille, DirectorSystem.DenThreatMin, DirectorSystem.DenThreatCap);
                Assert.InRange(den.ThreatTier, 0, 3);
            }
        }
    }
}
