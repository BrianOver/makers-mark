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
/// <para><b>The 2026-08-02 loop-legibility widening (plan KTD-A).</b> The 2026-07-30 split above was
/// right but stopped too early: it moved the nine workshop verbs and left every conversation and every
/// signal in the queue — "posting the bounty queues it, nothing happens"; "opening the counter queues,
/// nothing happens"; bell-stepping a haggle was the single most disorienting thing in that playtest.
/// The rule that survives contact with that playtest: <b>an action resolves NOW unless the WORLD must
/// move before the action means anything. The world moving is what the bell is for — and even then,
/// the player's own part of the act resolves now.</b> Twelve more verbs move to Now under that rule
/// (the counter session, bounty posting, camp verbs, talents, memorials, commission responses); three
/// remain deliberate bell-riders (construction, identity, a pact with the Guild — see below).</para>
///
/// <para><b>The counter session specifically (PresentItemAction).</b> Before this widening,
/// <see cref="Counter.CounterQueueSystem.Process"/> — a PHASE SYSTEM, run only by <see cref="GameKernel.Tick"/>'s
/// systems pass — was the ONLY place a presented item's verdict (walk vs. open a haggle round) actually
/// resolved; <see cref="Counter.CounterHandlers"/>'s own handler just recorded the intent
/// (<see cref="CounterState.Presented"/>) and left resolution to that later systems pass. Moving
/// <see cref="PresentItemAction"/> to Now without change would have silently stalled every counter
/// session: <see cref="GameKernel.ApplyNow"/> never runs phase systems (by contract), so nothing would
/// ever resolve the presentment and the very next <see cref="HaggleResponseAction"/> would reject with
/// "present an item first" forever. This landed alongside a small, targeted fix so the claim is actually
/// true: <see cref="Counter.CounterHandlers"/>'s own present handler now resolves the verdict itself
/// (calling <see cref="Counter.CounterQueueSystem.ResolvePresentedItem"/> directly, the same pure
/// zero-RNG logic <c>Process</c> always ran), so the handler alone — never a systems pass — decides.
/// <c>Process</c> stays registered (Tick-driven batches still exist, e.g. the CLI and the sim tests
/// that drive the kernel directly) but is now provably a no-op the instant the handler has already
/// resolved the presentment (its own guard conditions — no fresh <c>Presented</c>, or a round already
/// open — catch it). <c>SteppedMorningReplayTests</c>/<c>CounterSessionApplyNowEquivalenceTests</c> pin
/// this: a full open-present-haggle-close script applied entirely through
/// <see cref="GameKernel.ApplyNow"/> ends in the same substantive state as the same script applied one
/// action per <see cref="GameKernel.Tick"/> (the old bell-stepped shape), modulo the phase/day fields a
/// held Tick would additionally have touched. Open/Suggest/Haggle/Close needed no such fix — each was
/// already fully resolved by its own handler (Haggle already calls
/// <see cref="Counter.CounterQueueSystem.Advance"/> directly, a plain function call, not a systems-pass
/// dependency), so ApplyNow already ran the right predicate for those four; Present was the one verb
/// where the plan's premise ("sequencing is preserved by the handlers, not by the bell") was not yet
/// true until this fix made it true.</para>
///
/// <para><b>The rule:</b> if it is the player's own two hands — or their own two hands in a
/// conversation with someone standing right there — it resolves now. If it is a commitment the WORLD
/// has to act on independent of the player's own say-so, it rides the day's clock. Buying, crafting,
/// shelving, repricing, presenting, haggling, posting a bounty, sending supply, recalling the party,
/// unlocking a talent, honoring a memorial, and answering a commission are all things the player simply
/// does. Building a forge tier, declaring your profession identity, and commissioning a legendary work
/// are the three that still cost a beat — deliberate ceremony, not a bug.</para>
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
        // The workshop: your hands, your bench, your shelves (2026-07-30 split).
        BuyMaterialAction => true,      // handing coin to the vendor standing in front of you
        BuyOreAction => true,           // same, buying off a returning hero
        BuyForgeSupplyAction => true,   // same, restocking consumables
        CraftAction => true,            // you just swung the hammer — the item exists
        ReforgeHeirloomAction => true,  // a craft by another name
        MasterworkAttemptAction => true,
        StockAction => true,            // putting an item on your own shelf
        UnstockAction => true,          // taking it back off
        SetPriceAction => true,         // flipping your own price tag

        // The counter conversation (2026-08-02 widening): a hero standing at your counter. The
        // kernel already owns the session's ordering (PA3 state machine); ApplyNow runs the same
        // handler predicates, and CounterHandlers.ApplyPresent now resolves the verdict itself
        // (see this file's remarks above) instead of deferring to the systems pass — sequencing
        // is preserved by the handlers, not by the bell.
        OpenCounterAction => true,
        PresentItemAction => true,
        SuggestItemAction => true,
        HaggleResponseAction => true,
        CloseCounterAction => true,

        // Conversations with someone standing there (2026-08-02 widening) — the old comment on
        // these two itself conceded as much.
        AcceptCommissionAction => true,
        DeclineCommissionAction => true,

        // Pinning paper to a board is the player's own hands; the heroes READING it is the
        // world's part and still happens on subsequent ticks, unchanged. Posting it in the event
        // log at click time is also what unsticks the tutorial step that watches for it.
        PostBountyAction => true,

        // Vigil's only two verbs (2026-08-02 widening): pure state edits (fee + front-insert;
        // Recalled = true) that touch nothing an unrun system needs to finish. The effect ON THE
        // RAID still lands when ExpeditionDeepSystem resolves — the runner leaves now, the deep
        // answers later.
        SendSupplyAction => true,
        RecallPartyAction => true,

        // Player-owned progression/rite state — a "reflection between days" that eats a click and
        // shows nothing is a dead click, not a rite (2026-08-02 widening).
        UnlockTalentAction => true,
        HonorMemorialAction => true,

        // Everything else waits for the bell — the three deliberate ceremony verbs (2026-08-02
        // KTD-A, open question 1): each is a beat between deciding and having, visible and
        // cancellable (bell tray, U3), not a dead click.
        //
        //   UpgradeForgeAction        — construction; it should cost you a beat
        //   SetProfessionsAction      — who you ARE, settled at a day boundary
        //   CommissionLegendaryWork   — a pact the Guild acts on, not a bench task
        _ => false,
    };
}
