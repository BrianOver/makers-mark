using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Threading.Tasks;
using GameSim;
using GameSim.Contracts;
using GameSim.Expedition;
using GameSim.Harness;
using Xunit.Abstractions;

namespace GameSim.Tests.Balance;

/// <summary>
/// Kill-risk-1 A/B (staged-plan §U4, D5): the decision-value MEASUREMENT of the camp send verb.
/// Two test-local scripted policies over a 20-seed × 100-day sweep share the SAME base policy
/// (BaselinePlayer + a fixed daily field-salve craft, kept HELD) so the worlds are byte-identical up
/// to the Camp tick; the ONLY divergence is the camp decision:
///   - NEVER-SEND: holds at Camp (the baseline).
///   - SEND-BELOW-40%: delivers a held salve to the neediest camped member whose hp*100 &lt; 40*MaxHp.
///
/// This is MEASUREMENT, not a pass/fail band (orchestrator ruling): the assertion is only that the
/// harness runs and both arms complete. The measured deltas are the tuning baseline for the telemetry
/// loop and are recorded below + in the U4 report.
///
/// Recorded baseline (seeds 2026–2045 × 100 days), TWO measurements on record so drift is visible
/// rather than replaced (P2-LONG-23 re-measured; do not silently re-pin a third time):
///
///   2026-07-18 build (GameSim.Tests, pre-venue-ladder / pre-quality-roller-change /
///   pre-attribution-rework):
///     NEVER-SEND     : deaths=768  expeditions=3518  targetReached=1234  deliveries=0   salveUses=382
///     SEND-BELOW-40% : deaths=797  expeditions=3523  targetReached=1222  deliveries=62  salveUses=404
///     Δ deaths=+29    Δ targetReached=−12
///   Finding (07-18): the &lt;40% send trigger fires 62 times across the 2000 party-day sweep — real
///   but sparse, because a hero clearing the floor-1 checkpoint clean only sometimes parks in the
///   [25%,40%) HP band (the too-hurt exit at &lt;25% finalises before parking). At the aggregate the
///   deliveries slightly RAISE mortality (+29 deaths) and slightly LOWER the TargetReached rate (−12):
///   the SAME emergent risk-compensation mechanism SalveProvisioningBalanceTests documents — a
///   topped-up hero pushes into stage-2 floors that kill, rather than banking a shallow clear.
///
///   2026-09-02 re-measurement (current build, origin/main@e7cca3d5b91deda59ee494e8eae426595c67c1b9,
///   six weeks and ~20 units later — P2-LONG-23):
///     NEVER-SEND     : deaths=266  expeditions=4328  targetReached=1222  deliveries=0   salveUses=698
///     SEND-BELOW-40% : deaths=266  expeditions=4328  targetReached=1222  deliveries=0   salveUses=698
///     Δ deaths=0      Δ targetReached=0     deliveries=0 — the two arms are now BYTE-IDENTICAL.
///   Finding (09-02): the send trigger does not merely fire less often — it never fires at all. A
///   targeted diagnostic over seed 2026 alone (100 days, 200 camped-party observations) found the
///   MINIMUM hp% ever observed on a camped hero was 60%; nothing ever camps below the 40% threshold
///   this verb targets, so SEND-BELOW-40% and NEVER-SEND submit the identical action stream and
///   produce identical worlds. Total sweep-wide deaths also fell ~65% (768 → 266) across the same
///   six weeks. Read together: the 07-18 mechanism (a floor-1 checkpoint sometimes parking a hero in
///   the needy-but-not-retreating band, where topping them up pushes them into a stage-2 floor that
///   kills) has not shrunk, it has been balanced away — heroes now clear the floor-1 checkpoint
///   healthy enough that the verb's own targeting condition is never met on this seed range.
///
/// STOP-AND-REPORT (P2-KTD10): this is not a smaller or reversed effect to re-tune around, it is an
/// A/B that currently measures nothing (zero engagement in both arms) — §11.3 R1's pending ruling
/// (default (c), damp risk-compensation only) was weighed against evidence that no longer reproduces
/// on the current build, and this comment does not invent a new threshold or re-derive a lever
/// direction from it. The PER-INSTANCE claim is unaffected — the marquee test still proves a
/// delivered-salve PotionLifesave end-to-end with zero attribution edits whenever the verb IS used —
/// but the AGGREGATE risk-compensation mechanism this file was written to document cannot currently
/// be observed at all on the seed range measured, so it cannot currently justify tuning
/// CampCheckpointDepth / the send threshold / the fee in either direction.
///
/// DIAGNOSIS (2026-09-03, P2-LONG-24) — the open question above is now answered, and the answer is
/// not a distribution that drifted. <see cref="CampedHeroHpDistribution_AtParkTime_AcrossTheSweep"/>
/// censuses the FULL camped-hero HP distribution over the same 20-seed × 100-day sweep:
///
///   camped-hero observations = 10818
///   min=50%  p25=100%  median=100%  p75=100%  max=100%  mean=98.5%
///   [50,60) 40   [60,70) 85   [70,80) 125   [80,90) 232   [90,100) 865   [100] 9471
///   below 50% = 0    below 40% (the send band) = 0    below 25% = 0
///
/// Two corrections to the 09-02 note above. First, the sweep-wide minimum is 50%, not 60% — 60% was
/// a seed-2026-only artifact of a 200-observation sample. Second, 50% is not where the distribution
/// happens to bottom out; it is a STRUCTURAL FLOOR. ExpeditionResolver.ResolveStage1 parks only on a
/// raw TargetReached, and the post-floor too-hurt check finalises any party still holding a hero
/// under CombatMath.ShouldDrink (50%) — so a camped hero is at or above the drink line BY
/// CONSTRUCTION, and nothing mutates parked HP between the park and the Camp window. The send verb's
/// &lt;40% band therefore sat entirely below the park floor: an empty set, not a rare one.
///
/// <para><b>REPAIRED 2026-09-04 (P2-LONG-25, owner ruling).</b> The census above is the BEFORE
/// reading. CombatMath now carries its own TooHurtThresholdPct (30%), strictly between the flee and
/// drink lines, so the post-floor check no longer fuses "too hurt to press deeper" to "wounded
/// enough to drink". The [30%,40%) band the send verb aims at is reachable again — 44 observations
/// over the same sweep where there were none. Both halves of the dilemma measured together: 20
/// deliveries against zero before, 5 of them proved by counterfactual replay to have saved a hero
/// who would otherwise have died, bought with 4 net deaths. The two cheaper knobs (raising the send
/// threshold to ~90%, deepening the checkpoint) were rejected — the first makes the verb fire
/// without making it a decision, the second changes expedition pacing wholesale.</para>
///
/// The dated cause is #328 (2026-08-01, the flee-first ordering fix), which moved that post-floor bar
/// from CombatMath.ShouldFlee (25%) to CombatMath.ShouldDrink (50%) for reasons unrelated to this
/// verb. That single change lifted the park floor from 25% to 50% straight through the [25%,40%) band
/// the 07-18 sweep harvested 62 deliveries out of. It is not a harness ceiling: no action stream,
/// scripted or human, can produce a camped hero the send verb can target, because the player has no
/// verb between the fight and the park. The reproducing pin is
/// StagedResolutionTests.ParkFloor_IsTheTooHurtLine_SoTheSendVerbsBandIsReachableAgain.
///
/// The retune was taken as P2-LONG-25 (2026-09-04) — the too-hurt bar, on the owner's ruling, with
/// the golden re-record recorded in AtomicEquivalenceTests and PhaseBNoDrawGateTests.
/// </summary>
public class CampProvisioningBalanceTests
{
    private const int Days = 100;
    private const int SendThresholdPct = 40; // D5 kill-risk-1: send when hp*100 < 40*MaxHp
    private static readonly ulong[] Seeds = Enumerable.Range(2026, 20).Select(i => (ulong)i).ToArray();

    private readonly ITestOutputHelper _output;

    public CampProvisioningBalanceTests(ITestOutputHelper output) => _output = output;

    private readonly record struct ArmStats(
        int Deaths, int Expeditions, int TargetReached, int FloorsCleared, int Deliveries, int SalveUses, int DeliveredLifesaves)
    {
        public static ArmStats operator +(ArmStats a, ArmStats b) => new(
            a.Deaths + b.Deaths, a.Expeditions + b.Expeditions, a.TargetReached + b.TargetReached,
            a.FloorsCleared + b.FloorsCleared, a.Deliveries + b.Deliveries, a.SalveUses + b.SalveUses,
            a.DeliveredLifesaves + b.DeliveredLifesaves);
    }

    [Fact]
    [Trait("Category", "Balance")]
    public void KillRisk1_NeverSend_vs_SendBelow40_HarnessRuns_BothArmsComplete()
    {
        // Each seed builds its own kernel/state and runs an isolated, integer-only 100-day sim
        // (no shared mutable state, no IO/clock), so the 20-seed × 2-arm sweep is embarrassingly
        // parallel. Determinism is per-seed and unaffected by execution order: results are summed
        // (commutative), never accumulated order-dependently. Collect per-seed, then reduce.
        var perSeed = new ConcurrentBag<(ArmStats Never, ArmStats Send)>();
        Parallel.ForEach(Seeds, seed =>
            perSeed.Add((RunArm(seed, send: false), RunArm(seed, send: true))));

        var never = perSeed.Aggregate(default(ArmStats), (acc, r) => acc + r.Never);
        var send = perSeed.Aggregate(default(ArmStats), (acc, r) => acc + r.Send);

        _output.WriteLine($"NEVER-SEND     : {never}");
        _output.WriteLine($"SEND-BELOW-40% : {send}");
        _output.WriteLine($"Δ deaths={send.Deaths - never.Deaths}  Δ targetReached={send.TargetReached - never.TargetReached}  deliveries={send.Deliveries}");

        // MEASUREMENT, not a band: assert only that the harness ran and both arms completed.
        Assert.True(never.Expeditions > 0, "never-send arm ran no expeditions");
        Assert.True(send.Expeditions > 0, "send arm ran no expeditions");
    }

    /// <summary>
    /// P2-LONG-24 diagnostic: the FULL camped-hero HP distribution at park time across the same
    /// 20-seed × 100-day sweep the A/B runs, not just the minimum. P2-LONG-23 recorded a minimum of
    /// 60% on seed 2026 alone and stopped there; a minimum cannot tell an aimed-wrong threshold from
    /// a structurally empty band. This census answers that, and its numbers are quoted in the class
    /// comment above. MEASUREMENT, not a band: the assertions only pin that the census observed
    /// something and that the structural floor below holds on live sweep data.
    /// </summary>
    [Fact]
    [Trait("Category", "Balance")]
    public void CampedHeroHpDistribution_AtParkTime_AcrossTheSweep()
    {
        var perSeed = new ConcurrentBag<ImmutableList<int>>();
        Parallel.ForEach(Seeds, seed => perSeed.Add(CampedHpPercents(seed)));

        var pcts = perSeed.SelectMany(p => p).OrderBy(p => p).ToList();
        Assert.NotEmpty(pcts);

        _output.WriteLine($"camped-hero observations = {pcts.Count} over {Seeds.Length} seeds × {Days} days");
        _output.WriteLine($"min={pcts[0]}%  p25={pcts[pcts.Count / 4]}%  median={pcts[pcts.Count / 2]}%  " +
                          $"p75={pcts[pcts.Count * 3 / 4]}%  max={pcts[^1]}%  mean={pcts.Average():F1}%");
        _output.WriteLine($"below send threshold ({SendThresholdPct}%) = {pcts.Count(p => p < SendThresholdPct)}   " +
                          $"below drink line ({CombatMath.DrinkThresholdPct}%) = {pcts.Count(p => p < CombatMath.DrinkThresholdPct)}   " +
                          $"below flee line ({CombatMath.FleeThresholdPct}%) = {pcts.Count(p => p < CombatMath.FleeThresholdPct)}");

        _output.WriteLine("histogram (10-point buckets):");
        for (var low = 0; low < 100; low += 10)
        {
            var n = pcts.Count(p => p >= low && p < low + 10);
            _output.WriteLine($"  [{low,3}%,{low + 10,3}%) {n,6}  {new string('#', Math.Min(60, n * 60 / Math.Max(1, pcts.Count)))}");
        }

        _output.WriteLine($"  [100%]      {pcts.Count(p => p >= 100),6}");

        // RE-AIMED 2026-09-04 (P2-LONG-25). These two lines are the inverse of what they asserted
        // while #328's fused floor stood, and the pair still cannot rot silently in either direction.
        //
        // The floor moved DOWN to the too-hurt line, so that is what is now structurally empty. The
        // send verb's band sits ABOVE that floor rather than beneath it, which is the whole repair:
        // a hurt party can be found camped, so provisioning it is a decision instead of a formality.
        // Asserted as non-empty rather than as a count — the exact number is a balance value that
        // moves with any venue or threshold retune, and pinning it here would make every such retune
        // a false failure in a file whose job is to report the distribution, not to freeze it.
        Assert.Equal(0, pcts.Count(p => p < CombatMath.TooHurtThresholdPct));
        Assert.NotEmpty(pcts.Where(p => p >= CombatMath.TooHurtThresholdPct && p < SendThresholdPct));
    }

    /// <summary>Every camped hero's hp% at the Camp window (the Expedition tick has just parked and
    /// nothing has been sent yet), over one seed's 100-day NEVER-SEND run.</summary>
    private static ImmutableList<int> CampedHpPercents(ulong seed)
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(seed);
        var pcts = ImmutableList.CreateBuilder<int>();

        for (var tick = 0; tick < Days * 5; tick++)
        {
            state = kernel.Tick(state, ArmActions(state, send: false)).NewState;

            // Phase order is Morning → Expedition → Camp → ExpeditionDeep → Evening, and the kernel
            // advances the phase after the tick: Phase == Camp means stage 1 has just parked.
            if (state.Phase != DayPhase.Camp)
            {
                continue;
            }

            foreach (var inFlight in state.InFlight)
            {
                foreach (var id in inFlight.Party)
                {
                    if (state.Heroes.TryGetValue(id.Value, out var hero)
                        && inFlight.Hp.TryGetValue(id.Value, out var hp)
                        && hero.MaxHp > 0)
                    {
                        pcts.Add(hp * 100 / hero.MaxHp);
                    }
                }
            }
        }

        return pcts.ToImmutable();
    }

    private static ArmStats RunArm(ulong seed, bool send)
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(seed);

        int deaths = 0, expeditions = 0, targetReached = 0, floorsCleared = 0, deliveries = 0, salveUses = 0, deliveredLifesaves = 0;

        // Every item the runner actually dropped this run. The attribution engine (KTD6) replays
        // each recorded fight with the item removed, so a PotionLifesave beat on one of THESE ids
        // is the save half of the tension stated in fact, not in aggregate: that hero would have
        // died, and the delivery is why they did not.
        var delivered = new HashSet<int>();

        for (var tick = 0; tick < Days * 5; tick++) // 5-phase day
        {
            var result = kernel.Tick(state, ArmActions(state, send));
            state = result.NewState;

            foreach (var gameEvent in result.Events)
            {
                switch (gameEvent)
                {
                    case HeroDied:
                        deaths++;
                        break;
                    case SupplyDelivered supply:
                        deliveries++;
                        delivered.Add(supply.Item.Value);
                        break;
                    case AttributionBeatEvent { Beat: BeatType.PotionLifesave } beat when delivered.Contains(beat.Item.Value):
                        deliveredLifesaves++;
                        break;
                }
            }

            // The Deep tick just finalized every party into PendingExpeditions (read before the
            // Evening reveal consumes them), same window SalveProvisioningBalanceTests uses.
            if (state.Phase == DayPhase.Evening)
            {
                foreach (var expedition in state.PendingExpeditions)
                {
                    expeditions++;
                    floorsCleared += expedition.DeepestFloorCleared;
                    if (expedition.Halt == ExpeditionHalt.TargetReached)
                    {
                        targetReached++;
                    }

                    salveUses += expedition.Floors.Sum(f => f.Combats.Sum(c => c.Uses.Count));
                }
            }
        }

        return new ArmStats(deaths, expeditions, targetReached, floorsCleared, deliveries, salveUses, deliveredLifesaves);
    }

    /// <summary>
    /// Shared base policy (BaselinePlayer + a daily held-salve craft) plus, for the send arm only, the
    /// camp deliveries. Both arms are byte-identical up to the Camp tick, so the sweep isolates the
    /// send decision.
    /// </summary>
    private static ImmutableList<PlayerAction> ArmActions(GameState state, bool send)
    {
        var actions = BaselinePlayer.ActionsFor(state).ToBuilder();
        switch (state.Phase)
        {
            case DayPhase.Expedition:
                // Craft ammo; freshly minted salves stay HELD (unshelved) until the same day's Camp.
                actions.Add(new CraftAction("field-salve", "copper"));
                actions.Add(new CraftAction("field-salve", "copper"));
                break;

            case DayPhase.Camp when send:
                actions.AddRange(SendActions(state));
                break;
        }

        return actions.ToImmutable();
    }

    /// <summary>Deliver a held salve to the neediest camped member below the send threshold — one per
    /// party, only while a held salve remains and the runner fee is affordable.</summary>
    private static IEnumerable<PlayerAction> SendActions(GameState state)
    {
        var shelved = state.Player.Shelf.Select(e => e.Item.Value).ToHashSet();
        var rivalShelved = state.RivalShelf.Select(e => e.Item.Value).ToHashSet();
        var packed = state.Heroes.Values.SelectMany(h => h.Pack).Select(id => id.Value).ToHashSet();
        var held = new Queue<ItemId>(state.Items.Values
            .Where(i => i.PlayerCrafted && i.Effect is { Kind: ConsumableKind.Heal }
                        && !shelved.Contains(i.Id.Value) && !rivalShelved.Contains(i.Id.Value) && !packed.Contains(i.Id.Value))
            .OrderBy(i => i.Id.Value)
            .Select(i => i.Id));

        var gold = state.Player.Gold;
        foreach (var inFlight in state.InFlight)
        {
            if (inFlight.SupplySent)
            {
                continue;
            }

            var target = inFlight.Party
                .Where(id => state.Heroes.TryGetValue(id.Value, out var h)
                             && inFlight.Hp.TryGetValue(id.Value, out var hp)
                             && hp * 100 < SendThresholdPct * h.MaxHp)
                .OrderBy(id => inFlight.Hp[id.Value]) // lowest hp first
                .Select(id => (HeroId?)id)
                .FirstOrDefault();

            var fee = 6 + 3 * inFlight.CheckpointFloor; // CampHandlers.SupplyFee (internal const mirror)
            if (target is { } to && held.Count > 0 && gold >= fee)
            {
                gold -= fee;
                yield return new SendSupplyAction(to, held.Dequeue());
            }
        }
    }
}
