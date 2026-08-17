#if GDUNIT_TESTS
using System.Collections.Immutable;
using System.Linq;
using GameSim;
using GameSim.Contracts;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U-T2 Wave C (§11.14.4, Act II, link2, "day 1 gets a link-2 beat" — #161, plan's own "#161
/// answered"): <see cref="GameSim.Heroes.HeroShoppingSystem"/> runs BEFORE
/// <see cref="GameSim.Heroes.MusterSystem"/> in the SAME Morning tick that transitions into
/// <see cref="DayPhase.Expedition"/> — a sale could always fire silently on the send-off tick, and
/// the game never said so. This suite proves <c>MainUi.SendOffSaleBeat</c> names the buyer and
/// price when the sim's own <see cref="ItemSold"/>/<see cref="PartiesFormed"/> events say a
/// departing hero bought something, and says so plainly when nobody did — never a client-invented
/// value, always read off the real events the send-off's own tick just produced.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class SendOffSaleBeatTests
{
    private static readonly ItemId AttractiveItemId = new(9501);

    /// <summary>One cheap, decent (Common-grade — never Poor, which the veteran quality gate can
    /// refuse) weapon on the player's shelf, priced well under any fresh hero's starting gold —
    /// every default <see cref="GameComposition.NewCampaign"/> hero starts with empty gear, so this
    /// reads as a straightforward upgrade for whichever hero shops first.</summary>
    private static GameState StateWithOneAttractiveShelfItem(ulong seed)
    {
        var baseState = GameComposition.NewCampaign(seed);
        var item = new Item(
            AttractiveItemId, "test-sendoff-shelf-item", "Test Buckler", ItemSlot.Weapon,
            QualityGrade.Common, new ItemStats(Attack: 6, Defense: 0, Weight: 2),
            new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);

        return baseState with
        {
            RivalShelf = ImmutableList<ShelfEntry>.Empty,
            Items = baseState.Items.Add(item.Id.Value, item),
            Player = baseState.Player with { Shelf = ImmutableList.Create(new ShelfEntry(item.Id, 5)) },
        };
    }

    [TestCase]
    public void SendOff_NamesTheBuyerAndPrice_WhenADepartingHeroBoughtFromTheShelf()
    {
        var ui = MountMainUi(new SimAdapter(StateWithOneAttractiveShelfItem(9501)));
        try
        {
            AssertThat(ui.Adapter.CurrentState.Phase)
                .OverrideFailureMessage("Setup check: this fixture must start in Morning.")
                .IsEqual(DayPhase.Morning);

            ui.Adapter.AdvancePhase(); // Morning: hero-shopping + muster, same tick

            var sale = ui.Adapter.LastEvents.OfType<ItemSold>().FirstOrDefault(s => s.Item == AttractiveItemId);
            AssertThat(sale)
                .OverrideFailureMessage("Setup check: nobody bought the shelved item this Morning — this test proves nothing about the send-off beat without a real sale.")
                .IsNotNull();

            var roster = ui.Adapter.LastEvents.OfType<PartiesFormed>().Single().Parties
                .SelectMany(p => p.Roster).ToImmutableHashSet();
            AssertThat(roster.Contains(sale!.Buyer))
                .OverrideFailureMessage("Setup check: the buyer is not in today's departing roster — this fixture does not exercise the send-off beat's cross-reference.")
                .IsTrue();

            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Expedition);

            var clockLabel = Find<Label>(ui, "ClockLabel").Text;
            var buyerName = ui.Adapter.CurrentState.Heroes[sale.Buyer.Value].Name;
            AssertThat(clockLabel)
                .OverrideFailureMessage($"The send-off never named the buyer ({buyerName}): \"{clockLabel}\"")
                .Contains(buyerName);
            AssertThat(clockLabel)
                .OverrideFailureMessage($"The send-off never named the item (Test Buckler): \"{clockLabel}\"")
                .Contains("Test Buckler");
            AssertThat(clockLabel)
                .OverrideFailureMessage($"The send-off never named the sim's own price (5g): \"{clockLabel}\"")
                .Contains("5g");
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>The honest other half of #161's answer: when nobody bought anything, the send-off
    /// says so plainly rather than staying silent (the plan's own "or says honestly that nobody
    /// bought it").</summary>
    [TestCase]
    public void SendOff_SaysNobodyBought_WhenTheShelfIsEmpty()
    {
        var baseState = GameComposition.NewCampaign(seed: 9502);
        var state = baseState with { Player = baseState.Player with { Shelf = ImmutableList<ShelfEntry>.Empty } };
        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            ui.Adapter.AdvancePhase(); // Morning -> Expedition, nothing on the shelf to buy

            AssertThat(ui.Adapter.LastEvents.OfType<ItemSold>().Any(s => s.FromPlayerShop))
                .OverrideFailureMessage("Setup check: a player-shelf sale happened despite an empty shelf.")
                .IsFalse();
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Expedition);

            var roster = ui.Adapter.LastEvents.OfType<PartiesFormed>().SingleOrDefault()?.Parties
                .SelectMany(p => p.Roster).ToImmutableHashSet();
            AssertThat(roster is { Count: > 0 })
                .OverrideFailureMessage("Setup check: nobody marched today — this fixture does not reach the 'nobody bought anything' branch at all, it reaches the unrelated 'nobody marched' one.")
                .IsTrue();

            var clockLabel = Find<Label>(ui, "ClockLabel").Text;
            AssertThat(clockLabel)
                .OverrideFailureMessage($"A silent Morning did not say so plainly at the send-off: \"{clockLabel}\"")
                .Contains("nobody marching today bought anything off your shelf");
        }
        finally
        {
            Unmount(ui);
        }
    }
}
#endif
