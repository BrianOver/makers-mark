using System.Linq;
using GameSim.Contracts;
using GameSim.Harness;
using Xunit;
using Xunit.Abstractions;

namespace GameSim.Tests.Harness;

/// <summary>
/// P2-HONEST-43 (§11.16, "The reference smith fills the consumable ask"): link 2, the commission
/// channel. §11.16 measured consumable commissions POSTED 145 times under <c>BaselinePlayer</c> and
/// 519 under <c>forgecounter</c> — 53% of everything that board posts — and FULFILLED 0 times across
/// 60 campaigns, expiring 0 times too, because nothing ever accepted one: the ask was dropped in
/// silence while the advisor named it 523 times per 10,000 decision points.
///
/// <para>These are properties of the arm's rule — the smith answers an ask they can actually fill,
/// from the salve they hold or the one a legal heal recipe would make — never of a named seed, hero
/// or recipe id. A guard naming <c>field-salve</c> or hero 3 would stop covering the family the
/// moment a second heal recipe or a second consumable ask exists.</para>
/// </summary>
public class ForgeCounterPlayerConsumableCommissionTests
{
    private readonly ITestOutputHelper _out;

    public ForgeCounterPlayerConsumableCommissionTests(ITestOutputHelper output) => _out = output;

    private sealed record AskTally(
        int ConsumablePosted,
        int ConsumableAccepted,
        int ConsumableFulfilled,
        int ConsumableExpired,
        int GearFulfilled);

    /// <summary>Walks the same sweep shape every other harness census in this directory uses, and
    /// classifies each commission event by the SLOT it names (or, for a fulfilment, by whether the
    /// delivered item carries a <see cref="ConsumableEffect"/> — the same "branch on Effect data, not
    /// on a recipe id" rule <c>CommissionHandlers.TryFulfillFromShelf</c> itself uses to decide
    /// between pack and gear).</summary>
    private static AskTally Sweep(int seeds, int days)
    {
        var kernel = GameSim.GameComposition.BuildKernel();
        int posted = 0, accepted = 0, fulfilled = 0, expired = 0, gearFulfilled = 0;

        foreach (var seed in Enumerable.Range(1, seeds).Select(i => (ulong)i))
        {
            var state = GameSim.GameComposition.NewCampaign(seed);
            while (state.Day <= days)
            {
                var actions = ForgeCounterPlayer.ActionsFor(state);
                accepted += actions.OfType<AcceptCommissionAction>()
                    .Count(a => state.Commissions.Any(c => c.Hero == a.Hero && !c.Accepted && c.Slot == ItemSlot.Consumable));

                var result = kernel.Tick(state, actions);
                state = result.NewState;

                posted += result.Events.OfType<CommissionPosted>().Count(e => e.Slot == ItemSlot.Consumable);
                expired += result.Events.OfType<CommissionExpired>().Count(e => e.Slot == ItemSlot.Consumable);

                foreach (var done in result.Events.OfType<CommissionFulfilled>())
                {
                    var isConsumable = state.Items.TryGetValue(done.Item.Value, out var item) && item.Effect is not null;
                    if (isConsumable)
                    {
                        fulfilled++;
                    }
                    else
                    {
                        gearFulfilled++;
                    }
                }
            }
        }

        return new AskTally(posted, accepted, fulfilled, expired, gearFulfilled);
    }

    [Fact]
    public void TheMostAskedCommission_IsActuallyFilled()
    {
        // The property this unit exists to close: the board's most-posted ask had never been
        // delivered once. A sweep that still delivers nothing means the arm is inert.
        var tally = Sweep(seeds: 10, days: 60);

        _out.WriteLine($"P2-HONEST-43 consumable-ask census (10 seeds x 60 days): {tally}");
        Assert.True(tally.ConsumablePosted > 0, "no consumable commission was ever posted — the arm's own premise is missing");
        Assert.True(tally.ConsumableFulfilled > 0, $"the consumable ask is still never filled ({tally})");
    }

    [Fact]
    public void EveryConsumableAskTheArmAccepts_IsOneItCouldFill()
    {
        // The arm only answers an ask it can deliver: a satisfying salve already held, or a heal
        // recipe legal to craft at a bar a craft clears. So acceptance must not turn the measured
        // zero-expiry board into a board of broken promises — fulfilments must outnumber expiries.
        var tally = Sweep(seeds: 10, days: 60);

        _out.WriteLine($"accepted {tally.ConsumableAccepted}, fulfilled {tally.ConsumableFulfilled}, expired {tally.ConsumableExpired}");
        Assert.True(tally.ConsumableFulfilled > tally.ConsumableExpired,
            $"the arm promises more than it delivers ({tally})");
    }

    [Fact]
    public void TheGearChannel_IsUntouchedByTheConsumableArm()
    {
        // The arm adds a slot the baseline deliberately excludes; it must not cost the gear
        // commissions the channel already delivers (they share the shelf and the hero's purse).
        var tally = Sweep(seeds: 10, days: 60);

        _out.WriteLine($"gear commissions fulfilled alongside the arm: {tally.GearFulfilled}");
        Assert.True(tally.GearFulfilled > 0, $"the gear half of the commission channel went dark ({tally})");
    }

    [Fact]
    public void TheConsumableArmIsDeterministic_SameSeedSameAsks()
    {
        // No RNG, no clock: commissions in hero-id order, the match rule the commission channel
        // itself owns. Two runs of one seed must answer the same asks.
        Assert.Equal(Sweep(seeds: 3, days: 40), Sweep(seeds: 3, days: 40));
    }
}
