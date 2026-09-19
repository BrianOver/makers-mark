using System.Collections.Immutable;
using GameSim.Contracts;

namespace GameSim.Heroes;

/// <summary>
/// Morning shopping (R7, R16-morning-half): each ALIVE hero, in HeroId order, browses
/// the player shelf AND the rival shelf and buys the single best affordable upgrade
/// across both — best value = gear-score gain per gold (<see cref="ShoppingAi"/>).
/// Earlier heroes shop first, so later heroes see a thinner shelf: strictly sequential
/// and deterministic. Draws no RNG.
///
/// After the gear pass, the CONSUMABLE pass (P2) runs in the same HeroId order: a hero
/// with an empty <see cref="Hero.Pack"/> buys the single cheapest shelf item with a
/// Heal effect it can afford (player shelf preferred on price tie), at most one per
/// hero per Morning. Consumables are keyed off <see cref="ConsumableEffect"/> DATA and
/// never enter the gear pass (they carry no gear score).
///
/// Event cap (documented behavior): <see cref="HeroPassedOnItem"/> is emitted only for
/// PLAYER-shelf items — the player needs to know why their stock didn't sell (R8/AE4).
/// Rival-shelf passes stay silent to avoid event spam the player can't act on.
///
/// PA3/PKD5: while a stepped counter session is OPEN and UNFINISHED (<see cref="GameState.Counter"/>
/// is <c>{ Closed: false }</c>), this system does nothing at all — those heroes are still queued for
/// (or mid-) counter service, resolved by <see cref="Counter.CounterQueueSystem"/> instead, and running
/// the atomic pass early would shop them twice. On the CLOSING tick (<c>Counter.Closed == true</c>,
/// set by <see cref="Counter.CounterHandlers"/>'s <c>CloseCounterAction</c> or by the queue running
/// dry) this system runs its normal BROWSING passes (gear, then consumables) but SKIPS every hero
/// already in <see cref="CounterState.Served"/> — nobody browses twice, nobody starves.
/// <see cref="GameState.Counter"/> null (the default — the ONLY path <c>BaselinePlayer</c>/the
/// balance gate ever exercise) takes the exact original unconditional loop, byte-identical to
/// pre-Phase-A (the atomic-equivalence pin).
///
/// P2-HONEST-33: "served" is not "done for the day" — a served hero's ACCEPTED commission is a
/// standing forge request the counter's own haggle never touches (<see cref="Counter.HaggleResolver"/>
/// has no <see cref="Commission"/> reads), so skipping the browsing passes entirely used to also
/// silently cancel that hero's commission (and any piece earmarked for them) for the whole day —
/// measured 0-of-1,018 fulfilled under <c>forgecounter</c> (PR #923). A THIRD pass, after the two
/// browsing passes, walks <see cref="CounterState.Served"/> (ascending — it is an
/// <c>ImmutableSortedSet</c>) and runs ONLY <see cref="CommissionHandlers.TryFulfillFromShelf"/> for
/// each served, alive hero — never <see cref="ShopOnce"/> or <see cref="ShopConsumableOnce"/>, so
/// PKD5's "nobody browses twice" still stands. A hero who already bought their commission's item at
/// the counter itself leaves no matching shelf entry, so this pass is a no-op for them (no double
/// sell) — see <see cref="CommissionHandlers.TryFulfillFromShelf"/>'s own shelf scan.
/// </summary>
public sealed class HeroShoppingSystem : IPhaseSystem
{
    public DayPhase Phase => DayPhase.Morning;

    public string Name => "hero-shopping";

    public GameState Process(GameState state, IDeterministicRng rng, IEventSink events)
    {
        if (state.Counter is { Closed: false })
        {
            return state; // stepped session still open — CounterQueueSystem owns these heroes
        }

        // Both passes walk the SAME queue, in the SAME order — see MorningShoppingOrder's own doc.
        foreach (var heroId in MorningShoppingOrder(state))
        {
            var hero = state.Heroes[heroId];
            state = ShopOnce(state, hero, events);
        }

        // Consumable pass (P2), after the whole gear pass: gold spent on gear is gone,
        // so the pass reads each hero's post-gear purse.
        foreach (var heroId in MorningShoppingOrder(state))
        {
            var hero = state.Heroes[heroId];
            state = ShopConsumableOnce(state, hero, events);
        }

        state = FulfillServedHeroCommissions(state, events);

        return state;
    }

    /// <summary>
    /// P2-HONEST-33: the standing-request pass for heroes the counter itself already served this
    /// session (<see cref="CounterState.Served"/>) — MorningShoppingOrder (correctly) excludes them
    /// from both browsing passes above (PKD5: nobody browses twice), but an ACCEPTED commission is
    /// a promise the SHOP owes, not a browse, and the counter's own haggle never checks
    /// <see cref="GameState.Commissions"/>. Walked in ascending HeroId order (Served is an
    /// <c>ImmutableSortedSet&lt;int&gt;</c>) for the same determinism the browsing passes get from
    /// MorningShoppingOrder. Calls ONLY <see cref="CommissionHandlers.TryFulfillFromShelf"/> — never
    /// <see cref="ShopOnce"/>/<see cref="ShopConsumableOnce"/>, so a served hero still never browses.
    /// A no-op when <see cref="GameState.Counter"/> is null (nobody served) or a served hero has no
    /// accepted commission, no matching shelf piece, or already bought it at the counter itself.
    /// </summary>
    private static GameState FulfillServedHeroCommissions(GameState state, IEventSink events)
    {
        var served = state.Counter?.Served;
        if (served is null || served.Count == 0)
        {
            return state;
        }

        foreach (var heroId in served)
        {
            var hero = state.Heroes[heroId];
            if (!hero.Alive)
            {
                continue; // dead heroes never shop (R7 permadeath)
            }

            state = CommissionHandlers.TryFulfillFromShelf(state, hero, events) ?? state;
        }

        return state;
    }

    /// <summary>
    /// The Morning shopping queue, in the exact order <see cref="Process"/> runs it against
    /// <paramref name="state"/> right now: ascending HeroId order (<see cref="GameState.Heroes"/>
    /// keys are already sorted that way — ImmutableSortedDictionary), alive heroes only, minus
    /// anyone a still-open counter session's closing tick already served this session (PKD5
    /// fallback gate). Recomputed at each call site (never cached across a mutation) — exactly the
    /// re-snapshot <see cref="Process"/> itself did inline before this was extracted, so this change
    /// is a pure rename, not a behavior change: nothing either shopping pass does can flip a hero's
    /// <see cref="Hero.Alive"/> or touch <see cref="GameState.Counter"/>, so re-deriving the filter
    /// against the post-gear-pass <paramref name="state"/> for the second call always reproduces the
    /// identical set anyway.
    ///
    /// <para>Extracted (P2-PEOPLE-17, "stocking a piece names the morning queue that will reach it
    /// first"): a stocking-time reader — the shop panel's own "who reaches this piece first" line —
    /// needs the REAL queue a hero's commission will actually wait behind, and re-deriving "ascending
    /// HeroId, alive, not already served" a second time in the renderer is exactly the failure family
    /// this repo already paid for with a hand-copied formula (see <see cref="CommissionHandlers.ForecastQueueFor"/>,
    /// which calls this instead of re-sorting hero ids itself). Also honors <see cref="Process"/>'s
    /// own stepped-counter-session guard, so a caller outside <see cref="Process"/> during an open
    /// session sees the truthful "nobody shops through this pass yet" empty queue rather than a
    /// queue that will not actually run this tick.</para>
    /// </summary>
    internal static ImmutableArray<int> MorningShoppingOrder(GameState state)
    {
        if (state.Counter is { Closed: false })
        {
            return ImmutableArray<int>.Empty; // stepped session still open — see Process's own guard
        }

        var served = state.Counter?.Served; // non-null only on the closing tick (PKD5 fallback gate)
        var order = ImmutableArray.CreateBuilder<int>(state.Heroes.Count);
        foreach (var heroId in state.Heroes.Keys)
        {
            var hero = state.Heroes[heroId];
            if (!hero.Alive || served is { } s && s.Contains(heroId))
            {
                continue; // dead heroes never shop (R7 permadeath); counter-served heroes don't shop twice
            }

            order.Add(heroId);
        }

        return order.ToImmutable();
    }

    /// <summary>One hero's whole morning: evaluate both shelves, buy at most one item.</summary>
    private static GameState ShopOnce(GameState state, Hero hero, IEventSink events)
    {
        // Wave 3 (U14): an ACCEPTED commission is a standing forge request, checked ahead of the
        // hero's ordinary gear-score shopping — see CommissionHandlers.TryFulfillFromShelf for why
        // this bypasses the normal ShoppingAi verdict gates. Null means nothing to fulfill (no
        // accepted commission / no matching shelf item / can't afford the guaranteed price yet), so
        // the hero falls through to their ordinary shopping pass, unchanged, exactly as before.
        var commissionSale = CommissionHandlers.TryFulfillFromShelf(state, hero, events);
        if (commissionSale is { } fulfilled)
        {
            return fulfilled;
        }

        var boycotting = NeedsSystem.IsBoycotting(hero.Id, state);
        var (best, candidates) = EvaluateGearCandidates(state, hero);

        // Legible passes (R8): every player-shelf item the hero looked at and did not
        // buy gets a reasoned event — including buyable items that lost on value.
        // (A null verdict means the item wasn't judged in this pass — consumables.) A Buy verdict
        // that still lost the ranking gets one of two honest reasons: if the boycott's comparison-
        // only price bias (BoycottEffectivePrice) is what tipped it — this candidate would have won
        // at its REAL price (LostToBoycott) — the reason names the grudge; otherwise it lost fair
        // and square on gear score per gold. Never blame the gear for what the boycott decided.
        foreach (var candidate in candidates)
        {
            if (!candidate.FromPlayerShelf || candidate.Verdict is null || ReferenceEquals(candidate, best))
            {
                continue;
            }

            string reason;
            if (candidate.Verdict.Kind == ShoppingVerdictKind.Pass)
            {
                reason = candidate.Verdict.Reason;
            }
            else if (LostToBoycott(candidate, best!, boycotting))
            {
                reason = BoycottReason(best!.Item);
            }
            else
            {
                reason = $"picked {best!.Item.Name} instead — better gear score per gold";
            }

            events.Emit(new HeroPassedOnItem(hero.Id, candidate.Item.Id, reason));
        }

        if (best is null)
        {
            return state;
        }

        // Phase B (B1a, R-B1): explain the gear buy — capped to the cases where the player's OWN
        // shelf was actually part of the decision (won it or lost it), mirroring HeroPassedOnItem's
        // player-shelf-only anti-spam precedent above rather than firing for every hero every
        // morning. Observational only: it reads the verdicts already computed, changes nothing,
        // and draws no RNG.
        StampGearDecision(hero, best, candidates, boycotting, events);

        return ApplyPurchase(state, hero, best, events);
    }

    /// <summary>
    /// The pure "which gear item wins" pass (extracted so the Phase B advisor shadow-tick,
    /// <see cref="GameSim.Advisor.HeroForecast"/>, can call the EXACT same evaluation the real
    /// Morning pass uses — same helpers, same tie-breaks, so a forecast can never disagree with
    /// what this system does the next time it actually runs against the same state). Pick the
    /// single best Buy across both shops; strict "better than" keeps the comparison pure, and
    /// ItemIds are unique so <see cref="ShoppingAi.IsBetterValue"/> is a total order.
    ///
    /// Phase B (B4, R-B7): a boycotting hero (<see cref="NeedsSystem.IsBoycotting"/>) reads a
    /// PLAYER-shelf candidate's price as <see cref="NeedsSystem.BoycottPerceivedPricePenaltyPermille"/>
    /// higher for THIS ranking only (<see cref="BoycottEffectivePrice"/>) — a comparison-only bias,
    /// never a block, so the forecast (which calls this exact method) automatically stays exact
    /// (R-B2) and a standout player deal can still win and recover the hero.
    /// </summary>
    internal static (Candidate? Best, ImmutableList<Candidate> Candidates) EvaluateGearCandidates(GameState state, Hero hero)
    {
        var candidates = CollectCandidates(state, hero);
        var boycotting = NeedsSystem.IsBoycotting(hero.Id, state);

        Candidate? best = null;
        foreach (var candidate in candidates)
        {
            if (candidate.Item.Effect is not null)
            {
                continue; // consumables shop in their own pass (P2) — no gear score here
            }

            var verdict = ShoppingAi.EvaluateItem(hero, candidate.Item, candidate.Price, state.Items);
            candidate.Verdict = verdict;
            if (verdict.Kind != ShoppingVerdictKind.Buy)
            {
                continue;
            }

            if (best is null || ShoppingAi.IsBetterValue(
                    verdict.GearScoreGain, BoycottEffectivePrice(candidate, boycotting), candidate.Item.Id,
                    best.Verdict!.GearScoreGain, BoycottEffectivePrice(best, boycotting), best.Item.Id))
            {
                best = candidate;
            }
        }

        return (best, candidates.ToImmutableList());
    }

    /// <summary>Phase B (B4, R-B7): a boycotting hero's PLAYER-shelf candidate reads as this much
    /// pricier for RANKING purposes only — <see cref="ApplyPurchase"/> always charges/credits the
    /// real <see cref="Candidate.Price"/>, unmodified. A non-boycotting hero, or any rival-shelf
    /// candidate, is returned unchanged (byte-identical to the pre-B4 comparison).</summary>
    private static int BoycottEffectivePrice(Candidate candidate, bool boycotting)
    {
        if (!boycotting || !candidate.FromPlayerShelf)
        {
            return candidate.Price;
        }

        return candidate.Price
            + (int)((long)candidate.Price * NeedsSystem.BoycottPerceivedPricePenaltyPermille / 1000);
    }

    /// <summary>True when the boycott's comparison-only price bias (<see cref="BoycottEffectivePrice"/>),
    /// not gear score per gold, is why <paramref name="candidate"/> lost to <paramref name="winner"/>:
    /// judged at its REAL price against whatever price actually won the ranking, <paramref name="candidate"/>
    /// would have come out ahead. Keeps <see cref="HeroPassedOnItem"/>/<see cref="HeroDecisionExplained"/>
    /// honest about which of the two mechanisms actually decided — the drought penalty, or the gear
    /// verdict — since only <see cref="EvaluateGearCandidates"/>'s ranking ever sees the inflated
    /// price; <see cref="ShoppingAi"/> is called with the real price and never does.</summary>
    private static bool LostToBoycott(Candidate candidate, Candidate winner, bool boycotting)
    {
        if (!boycotting || !candidate.FromPlayerShelf || candidate.Verdict is not { Kind: ShoppingVerdictKind.Buy })
        {
            return false;
        }

        return ShoppingAi.IsBetterValue(
            candidate.Verdict.GearScoreGain, candidate.Price, candidate.Item.Id,
            winner.Verdict!.GearScoreGain, BoycottEffectivePrice(winner, boycotting), winner.Item.Id);
    }

    /// <summary>The honest player-facing reason for a <see cref="LostToBoycott"/> loss — names the
    /// grudge, never "better gear score," so <c>tools/Analytics/Report.Bucket</c> can't file a
    /// relationship problem under a gear-quality bucket (both buckets keyword-match on the prose).</summary>
    private static string BoycottReason(Item winner) =>
        $"still boycotting the shop over unmet demand — {winner.Name} won on the grudge, not the gear";

    /// <summary>Phase B (B1a): the runner-up gear Buy candidate — the best-value candidate other
    /// than <paramref name="best"/> — for the decision card's "chosen over X" framing. Null when
    /// nothing else was a viable Buy this morning. <paramref name="boycotting"/> mirrors
    /// <see cref="EvaluateGearCandidates"/>'s bias so the reported runner-up is the SAME one that
    /// would actually have won the real comparison (self-consistent decision card).</summary>
    internal static Candidate? RunnerUpGearCandidate(ImmutableList<Candidate> candidates, Candidate best, bool boycotting)
    {
        Candidate? runnerUp = null;
        foreach (var candidate in candidates)
        {
            if (ReferenceEquals(candidate, best) || candidate.Verdict is not { Kind: ShoppingVerdictKind.Buy })
            {
                continue;
            }

            if (runnerUp is null || ShoppingAi.IsBetterValue(
                    candidate.Verdict.GearScoreGain, BoycottEffectivePrice(candidate, boycotting), candidate.Item.Id,
                    runnerUp.Verdict!.GearScoreGain, BoycottEffectivePrice(runnerUp, boycotting), runnerUp.Item.Id))
            {
                runnerUp = candidate;
            }
        }

        return runnerUp;
    }

    private static void StampGearDecision(Hero hero, Candidate best, ImmutableList<Candidate> candidates, bool boycotting, IEventSink events)
    {
        var runnerUp = RunnerUpGearCandidate(candidates, best, boycotting);
        if (!best.FromPlayerShelf && !(runnerUp?.FromPlayerShelf ?? false))
        {
            return; // neither side of the decision touched the player's own shelf — not player-relevant
        }

        var runnerUpName = runnerUp?.Item.Name ?? "nothing else affordable";
        var reason = runnerUp is not null && LostToBoycott(runnerUp, best, boycotting)
            ? BoycottReason(best.Item)
            : best.Verdict!.Reason;
        events.Emit(new HeroDecisionExplained(
            hero.Id, best.Item.Name, runnerUpName, reason, GearDecisionGapPermille(best, runnerUp, boycotting)));
    }

    /// <summary>Value-per-gold gap between the chosen item and its runner-up, in per-mille —
    /// 1000 (maximal) when nothing else was a viable Buy. Integer-only: <c>gain*1000/price</c>
    /// per side, clamped, never negative (a worse "winner" than its runner-up cannot happen by
    /// construction of <see cref="ShoppingAi.IsBetterValue"/>). Priced with
    /// <see cref="BoycottEffectivePrice"/> on BOTH sides — the exact prices <see cref="EvaluateGearCandidates"/>
    /// and <see cref="RunnerUpGearCandidate"/> ranked on — so the reported margin is the margin that
    /// actually decided the outcome, never a raw-price number the ranking itself never used.</summary>
    private static int GearDecisionGapPermille(Candidate best, Candidate? runnerUp, bool boycotting)
    {
        if (runnerUp is null)
        {
            return 1000;
        }

        var bestScore = ValueScorePermille(best.Verdict!.GearScoreGain, BoycottEffectivePrice(best, boycotting));
        var runnerUpScore = ValueScorePermille(runnerUp.Verdict!.GearScoreGain, BoycottEffectivePrice(runnerUp, boycotting));
        return Math.Clamp(bestScore - runnerUpScore, 0, 1000);
    }

    private static int ValueScorePermille(int gain, int price) =>
        gain <= 0 ? 0 : Math.Min(1000, gain * 1000 / Math.Max(price, 1));

    /// <summary>
    /// One hero's consumable restock (P2): while the pack is below this hero's stocking target,
    /// buy the single cheapest affordable Heal item across both shelves — player shelf wins price
    /// ties, lower ItemId settles the rest. At most one purchase per hero per Morning (so a
    /// Prepared hero tops up over 2 mornings, never in one big binge). Phase B (B2, R-B5): the
    /// target is this hero's Consumable Stocking trait
    /// (<see cref="TraitEffects.ConsumableStockTargetFor"/>) — a neutral hero (neither Prepared
    /// nor Reckless) gets <see cref="TraitEffects.BaselineStockTarget"/> (1), which reproduces the
    /// pre-Phase-B "only when Pack is completely empty" gate byte-for-byte.
    /// </summary>
    private static GameState ShopConsumableOnce(GameState state, Hero hero, IEventSink events)
    {
        if (hero.Pack.Count >= TraitEffects.ConsumableStockTargetFor(hero))
        {
            return state; // this hero is content with what they're carrying — no browsing, no events
        }

        var candidates = CollectCandidates(state, hero);

        Candidate? best = null;
        foreach (var candidate in candidates)
        {
            if (candidate.Item.Effect is not { Kind: ConsumableKind.Heal })
            {
                continue; // behavior keyed off the effect DATA, never recipe ids
            }

            var verdict = ShoppingAi.EvaluateConsumable(hero, candidate.Item, candidate.Price);
            candidate.Verdict = verdict;
            if (verdict.Kind != ShoppingVerdictKind.Buy)
            {
                continue;
            }

            if (best is null || ShoppingAi.IsBetterConsumable(
                    candidate.Price, candidate.FromPlayerShelf, candidate.Item.Id,
                    best.Price, best.FromPlayerShelf, best.Item.Id))
            {
                best = candidate;
            }
        }

        // Legible passes mirror the gear pass: every player-shelf Heal item the hero
        // looked at and did not buy gets a reasoned event (R8/AE4).
        foreach (var candidate in candidates)
        {
            if (!candidate.FromPlayerShelf || candidate.Verdict is null || ReferenceEquals(candidate, best))
            {
                continue;
            }

            var reason = candidate.Verdict.Kind == ShoppingVerdictKind.Pass
                ? candidate.Verdict.Reason
                : $"picked {best!.Item.Name} instead — cheaper on the day";
            events.Emit(new HeroPassedOnItem(hero.Id, candidate.Item.Id, reason));
        }

        return best is null ? state : ApplyPurchase(state, hero, best, events);
    }

    private static List<Candidate> CollectCandidates(GameState state, Hero shopper)
    {
        var candidates = new List<Candidate>(state.Player.Shelf.Count + state.RivalShelf.Count);
        AddShelf(candidates, state, state.Player.Shelf, fromPlayerShelf: true, shopper);
        AddShelf(candidates, state, state.RivalShelf, fromPlayerShelf: false, shopper);
        return candidates;
    }

    private static void AddShelf(List<Candidate> candidates, GameState state, ImmutableList<ShelfEntry> shelf, bool fromPlayerShelf, Hero shopper)
    {
        foreach (var entry in shelf)
        {
            // P2-PEOPLE-28: a piece held for one hero is not on the shelf for anyone else — no candidate,
            // so no pass event either (they never saw it). The hero it is held for shops it as normal.
            if (IsHeldForSomeoneElse(entry, shopper))
            {
                continue;
            }

            // Defensive: a shelf entry whose item is missing from the catalog is
            // un-evaluable — skip silently rather than crash the morning.
            if (state.Items.TryGetValue(entry.Item.Value, out var item))
            {
                candidates.Add(new Candidate(entry, item, fromPlayerShelf));
            }
        }
    }

    /// <summary>P2-PEOPLE-28: true when <paramref name="entry"/> is earmarked for a hero other than
    /// <paramref name="shopper"/>. Shared with <see cref="CommissionHandlers.TryFulfillFromShelf"/> so the
    /// two shelf readers cannot disagree about who a held piece is for.</summary>
    internal static bool IsHeldForSomeoneElse(ShelfEntry entry, Hero shopper) =>
        entry.EarmarkedFor is { } held && held != shopper.Id;

    private static GameState ApplyPurchase(GameState state, Hero hero, Candidate bought, IEventSink events)
    {
        // Consumables go into the pack (P2); gear equips into the item's slot. A
        // replaced gear item is simply dropped from the gear set (kept simple by
        // design): it stays in GameState.Items, so its maker's-mark history survives,
        // but nobody bears it. Resale/trade-in is out of U5's scope.
        var updatedHero = bought.Item.Effect is not null
            ? hero with
            {
                Gold = hero.Gold - bought.Price,
                Pack = hero.Pack.Add(bought.Item.Id),
            }
            : hero with
            {
                Gold = hero.Gold - bought.Price,
                Gear = hero.Gear.WithSlot(bought.Item.Slot, bought.Item.Id),
            };
        state = state with { Heroes = state.Heroes.SetItem(hero.Id.Value, updatedHero) };

        if (bought.FromPlayerShelf)
        {
            // Player sale: credit the forge and clear the shelf slot (R16, R17 loop).
            state = state with
            {
                Player = state.Player with
                {
                    Gold = state.Player.Gold + bought.Price,
                    Shelf = state.Player.Shelf.Remove(bought.Entry),
                },
            };
        }
        else
        {
            // Rival sale: the rival's gold is not modeled — the item just leaves the shelf.
            state = state with { RivalShelf = state.RivalShelf.Remove(bought.Entry) };
        }

        events.Emit(new ItemSold(bought.Item.Id, hero.Id, bought.Price, bought.FromPlayerShelf));
        return state;
    }

    /// <summary>One shelf entry under evaluation. Mutable Verdict keeps the pass loop single-pass.
    /// Internal (not private) so the Phase B advisor shadow-tick (<see cref="GameSim.Advisor.HeroForecast"/>)
    /// can share the exact same evaluation this system uses.</summary>
    internal sealed class Candidate(ShelfEntry entry, Item item, bool fromPlayerShelf)
    {
        public ShelfEntry Entry { get; } = entry;
        public Item Item { get; } = item;
        public bool FromPlayerShelf { get; } = fromPlayerShelf;
        public int Price => Entry.Price;
        public ShoppingVerdict? Verdict { get; set; }
    }
}
