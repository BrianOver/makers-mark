using System.Collections.Immutable;
using GameSim;
using GameSim.Contracts;
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
/// Recorded baseline (seeds 2026–2045 × 100 days, GameSim.Tests build 2026-07-18):
///   NEVER-SEND     : deaths=768  expeditions=3518  targetReached=1234  deliveries=0   salveUses=382
///   SEND-BELOW-40% : deaths=797  expeditions=3523  targetReached=1222  deliveries=62  salveUses=404
///   Δ deaths=+29    Δ targetReached=−12
/// Finding: the &lt;40% send trigger fires 62 times across the 2000 party-day sweep — real but sparse,
/// because a hero clearing the floor-1 checkpoint clean only sometimes parks in the [25%,40%) HP band
/// (the too-hurt exit at &lt;25% finalises before parking). At the aggregate the deliveries slightly
/// RAISE mortality (+29 deaths) and slightly LOWER the TargetReached rate (−12): the SAME emergent
/// risk-compensation mechanism SalveProvisioningBalanceTests documents — a topped-up hero pushes into
/// stage-2 floors that kill, rather than banking a shallow clear. So the camp verb's aggregate
/// campaign value is a KNOB pointing the wrong way at the v1 floor-1 checkpoint, exactly D1's noted
/// cost ("a floor-1 camp report carries thinner HP signal"). Per-instance the verb is real — the
/// marquee test proves a delivered-salve PotionLifesave end-to-end with zero attribution edits. The
/// lever is CampCheckpointDepth / the send threshold / the fee, owned by the telemetry loop; retune
/// BEFORE U5 builds presentation on a signal that currently costs lives at scale.
/// </summary>
public class CampProvisioningBalanceTests
{
    private const int Days = 100;
    private const int SendThresholdPct = 40; // D5 kill-risk-1: send when hp*100 < 40*MaxHp
    private static readonly ulong[] Seeds = Enumerable.Range(2026, 20).Select(i => (ulong)i).ToArray();

    private readonly ITestOutputHelper _output;

    public CampProvisioningBalanceTests(ITestOutputHelper output) => _output = output;

    private readonly record struct ArmStats(int Deaths, int Expeditions, int TargetReached, int Deliveries, int SalveUses)
    {
        public static ArmStats operator +(ArmStats a, ArmStats b) => new(
            a.Deaths + b.Deaths, a.Expeditions + b.Expeditions, a.TargetReached + b.TargetReached,
            a.Deliveries + b.Deliveries, a.SalveUses + b.SalveUses);
    }

    [Fact]
    [Trait("Category", "Balance")]
    public void KillRisk1_NeverSend_vs_SendBelow40_HarnessRuns_BothArmsComplete()
    {
        var never = default(ArmStats);
        var send = default(ArmStats);
        foreach (var seed in Seeds)
        {
            never += RunArm(seed, send: false);
            send += RunArm(seed, send: true);
        }

        _output.WriteLine($"NEVER-SEND     : {never}");
        _output.WriteLine($"SEND-BELOW-40% : {send}");
        _output.WriteLine($"Δ deaths={send.Deaths - never.Deaths}  Δ targetReached={send.TargetReached - never.TargetReached}  deliveries={send.Deliveries}");

        // MEASUREMENT, not a band: assert only that the harness ran and both arms completed.
        Assert.True(never.Expeditions > 0, "never-send arm ran no expeditions");
        Assert.True(send.Expeditions > 0, "send arm ran no expeditions");
    }

    private static ArmStats RunArm(ulong seed, bool send)
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(seed);

        int deaths = 0, expeditions = 0, targetReached = 0, deliveries = 0, salveUses = 0;
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
                    case SupplyDelivered:
                        deliveries++;
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
                    if (expedition.Halt == ExpeditionHalt.TargetReached)
                    {
                        targetReached++;
                    }

                    salveUses += expedition.Floors.Sum(f => f.Combats.Sum(c => c.Uses.Count));
                }
            }
        }

        return new ArmStats(deaths, expeditions, targetReached, deliveries, salveUses);
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
