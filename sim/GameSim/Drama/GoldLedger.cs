using System.Collections.Immutable;
using GameSim.Contracts;

namespace GameSim.Drama;

/// <summary>One itemized line in the Evening "why did my gold change" ledger (U2, C1b): a named
/// SOURCE, the signed gold DELTA it moved, and a human-readable NOTE. The full set of a day's rows
/// sums to that day's observed purse change — the reconstruction invariant <c>GoldLedgerTests</c>
/// pins end to end against a real kernel run.</summary>
public readonly record struct GoldLedgerEntry(string Source, int Delta, string Note);

/// <summary>
/// Pure read model over <see cref="GameState.EventLog"/> (R1, KTD-5 — mirrors <see cref="LedgerQuery"/>):
/// no state changes, no RNG draw, callable any number of times by the CLI/Godot.
/// <para>
/// MF-2 (critical): a neutral-standing ore purchase emits NO event at all — <c>OreMarketHandlers</c>
/// only records the tariff DELTA (<see cref="TariffApplied"/>) when a faction's standing actually
/// moves the price, never the base cost — so this model CANNOT reconstruct the day's ore spend from
/// the event log alone. Rather than add an <c>OrePurchased</c> event (a golden-extension, forbidden
/// mid-slice by the R7 stop-rule), the caller (the CLI's <c>Advance()</c>, which already isolates
/// "accepted this tick" for the buyore confirm line) feeds the actual per-purchase cost in via
/// <paramref name="oreSpend"/>-shaped rows.
/// </para>
/// <para>
/// MF-4: the bounty escrow refund (dead-acceptor or lapsed-at-expiry, <c>BountySystems.cs:62-78</c>)
/// is likewise never evented — the CLI derives it from a cross-tick <c>state.Bounties</c> diff for
/// U1's narration line and feeds the SAME derived facts in here via <paramref name="bountyRefunds"/>,
/// so the detection logic lives in exactly one place.
/// </para>
/// </summary>
public static class GoldLedger
{
    /// <summary>
    /// Itemizes every known player-purse gold movement for <paramref name="day"/>: the evented flows
    /// read straight off the log, plus the two caller-fed inputs above for the flows the sim never
    /// events. <see cref="LootIncomeReceived"/> and <see cref="BountyPaid"/> are deliberately absent —
    /// both move a HERO's purse, never <c>state.Player.Gold</c>, so they are out of scope for a
    /// player-gold reconstruction. <see cref="RentMissed"/> and <see cref="MarketShareShifted"/> are
    /// also absent — neither moves gold (a missed payment is a confidence hit; a market-share shift is
    /// a permille edge), so a row for either would falsely inflate the sum.
    /// </summary>
    public static (ImmutableList<GoldLedgerEntry> Rows, int Total) DayDeltas(
        GameState state,
        int day,
        ImmutableList<GoldLedgerEntry> oreSpend,
        ImmutableList<GoldLedgerEntry> bountyRefunds)
    {
        var rows = ImmutableList.CreateBuilder<GoldLedgerEntry>();

        foreach (var gameEvent in DayLog.For(state.EventLog, day))
        {
            switch (gameEvent)
            {
                case BountyPosted posted:
                    rows.Add(new GoldLedgerEntry("bounty", -posted.RewardGold, $"escrowed {posted.Bounty} — floor {posted.TargetFloor}"));
                    break;
                case ItemSold sold when sold.FromPlayerShop:
                    rows.Add(new GoldLedgerEntry("sale", sold.Price, $"sold {sold.Item} to {sold.Buyer}"));
                    break;
                case CounterSaleClosed sale:
                    rows.Add(new GoldLedgerEntry("counter sale", sale.Price, $"sold {sale.Item} to {sale.Hero} at the counter"));
                    break;
                case MaterialPurchased material:
                    rows.Add(new GoldLedgerEntry("material", -material.Cost, $"{material.Quantity}x {material.MaterialKey} from the Morning vendor"));
                    break;
                case SupplyDelivered supply:
                    rows.Add(new GoldLedgerEntry("runner fee", -supply.Fee, $"delivery to {supply.To}"));
                    break;
                case RentPaid rent:
                    rows.Add(new GoldLedgerEntry("rent", -rent.AmountGold, "guild rent"));
                    break;
                case GuildAssessmentPassed assessment:
                    // Phase D (U-D2): the Guild Assessment debits the till only on a PASS
                    // (DuesPaidGold). A missed assessment moves no gold (never driven negative — see
                    // GuildAssessmentMissed's contract), so it needs no reconstruction row.
                    rows.Add(new GoldLedgerEntry("guild dues", -assessment.DuesPaidGold, "guild assessment"));
                    break;
                case RecoveryStipendGranted stipend:
                    rows.Add(new GoldLedgerEntry("stipend", stipend.Amount, "destitution recovery"));
                    break;
            }
        }

        rows.AddRange(oreSpend);
        rows.AddRange(bountyRefunds);

        var frozen = rows.ToImmutable();
        var total = 0;
        foreach (var row in frozen)
        {
            total += row.Delta;
        }

        return (frozen, total);
    }
}
