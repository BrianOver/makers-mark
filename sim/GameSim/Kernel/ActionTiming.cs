using GameSim.Contracts;

namespace GameSim.Kernel;

/// <summary>
/// Whether an action resolves the instant the player takes it, or waits for the bell.
///
/// <para><b>Why this split exists.</b> Everything used to wait for the bell, because
/// <see cref="GameKernel.Tick"/> is the only way to apply an action and it also advances the phase.
/// That made the shop lie to the player: spend 2 copper and the material list still reads 6 until the
/// phase ticks over. Brian's playtest, 2026-07-30 — "since the crafts are queued (shouldn't be), the
/// material list is confusing as it doesn't update" — plus the tutorial appearing frozen because it
/// watches the events a tick produces, and no tick had happened yet.
/// </para>
///
/// <para><b>The rule:</b> if it is the player's own two hands in their own workshop, it resolves now.
/// If it is a commitment the WORLD has to act on, it rides the day's clock. Buying, crafting, shelving
/// and repricing are things you simply do — nobody else needs to agree, and pretending they take until
/// dusk is what made the UI feel broken. Posting a bounty needs heroes to read the board; sending the
/// party needs the party to actually go. Those belong to the bell, and queuing them is not a bug —
/// it is the phase structure doing its job.</para>
///
/// <para>Deliberately a DENY-list-by-default: anything not named here waits for the bell. A new action
/// type therefore stays queued (the old, safe behaviour) until someone decides otherwise, rather than
/// silently becoming instant because it was forgotten.</para>
/// </summary>
public static class ActionTiming
{
    /// <summary>
    /// True when <paramref name="action"/> should resolve immediately via
    /// <see cref="GameKernel.ApplyNow"/> instead of being queued for the next
    /// <see cref="GameKernel.Tick"/>.
    ///
    /// <para>Legality is NOT decided here — an instant action illegal in the current phase still goes
    /// through the same handler predicate and still comes back rejected. This answers only "when",
    /// never "whether".</para>
    /// </summary>
    public static bool ResolvesImmediately(PlayerAction action) => action switch
    {
        // The workshop: your hands, your bench, your shelves.
        BuyMaterialAction => true,      // handing coin to the vendor standing in front of you
        BuyOreAction => true,           // same, buying off a returning hero
        BuyForgeSupplyAction => true,   // same, restocking consumables
        CraftAction => true,            // you just swung the hammer — the item exists
        ReforgeHeirloomAction => true,  // a craft by another name
        MasterworkAttemptAction => true,
        StockAction => true,            // putting an item on your own shelf
        UnstockAction => true,          // taking it back off
        SetPriceAction => true,         // flipping your own price tag

        // Everything else waits for the bell. Called out rather than left to the default so the
        // reasoning is on the record for the ones a reader would most expect to be instant:
        //
        //   PostBountyAction          — heroes have to READ the board before it means anything
        //   SendSupplyAction          — a runner has to carry it down the shaft
        //   RecallPartyAction         — the party has to hear the bell and turn around
        //   CommissionLegendaryWork   — a pact with the Guild, not a bench task
        //   UpgradeForgeAction        — construction; it should cost you a beat
        //   UnlockTalentAction        — reflection, earned between days
        //   SetProfessionsAction      — who you ARE, settled at a day boundary
        //   Accept/DeclineCommission  — a conversation with someone who is standing there, and the
        //                               counter session (PA3) already sequences those itself
        //   Open/Close/Present/Suggest/Haggle — the stepped counter owns its own ordering; making
        //                               any of it instant would race that state machine
        //   HonorMemorialAction       — a rite, tied to the day's rhythm
        _ => false,
    };
}
