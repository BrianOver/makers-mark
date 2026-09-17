using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Heroes;

namespace GodotClient.Ui;

/// <summary>
/// U2 (plan 2026-08-03-001-feat-loop-structure-plan.md, KTD-B — "the customer speaks first").
/// Owner playtest: "Counter worked - person buying but really unsure WHAt to do after?" and "i hit
/// suggest and interest went up but nothing happened lol" — traced to a silent customer (the sim's
/// own <c>CustomerApproached</c> carries a bare <c>HeroId</c>, nothing else) and a Suggest verb that
/// moves a meter with no comment. This file gives the customer a voice for both moments.
///
/// <para><b>Zero sim diff, zero second rule set.</b> Every line here is derived by calling the sim's
/// OWN pure evaluators read-only — <see cref="ShoppingAi.EvaluateItem"/> and
/// <see cref="RaidForecast.MissingItemSlots"/> — the exact precedent <c>ForgeMinigame</c> uses for
/// its preview scoring, or by narrating an outcome the sim already computed (a realized
/// <see cref="ShoppingVerdictKind"/> off a fresh event, an observed Interest delta). None of these
/// functions re-implements role-fit, affordability, or upsell-fit math: a spoken want or reply that
/// disagreed with what the sim will actually accept would be worse than the silence it replaces
/// (there is precedent for exactly that trap in this codebase). Pure functions only: no Godot
/// reference, no mutation, no RNG, no wall clock.</para>
///
/// <para><see cref="ThanksLine"/> (P2-PEOPLE-25) extends the same discipline to link 5 (the memory
/// loop reaching back INTO a shopping decision): it reads only <see cref="AttributionBeatEvent"/>s
/// the sim already emitted and the hero's own current <see cref="GearSet"/>, so a customer can never
/// thank the smith for a save the sim never proved or a piece they no longer carry.</para>
/// </summary>
public static class CustomerVoice
{
    /// <summary>
    /// P2-PEOPLE-25 ("the customer thanks you before they ask"): what a customer opens with when
    /// they walk up wearing a marked item of yours that PROVABLY changed last night's outcome —
    /// spoken BEFORE <see cref="WantLine"/>, never instead of it. Null is the honest, common case:
    /// no qualifying beat renders no line, never a generic "thanks for the goods."
    ///
    /// <para><b>The qualifying set, verified against <see cref="BeatType"/> rather than assumed</b>
    /// (<see cref="CounterfactualBeatTypes"/>): <see cref="BeatType.LethalSave"/> and
    /// <see cref="BeatType.BreakpointClear"/> only.
    /// <see cref="BeatType.Provisioned"/> is EXCLUDED even though the unit brief that opened this
    /// work named it — <c>AttributionEngine.AddConsumableBeats</c>'s own doc comment calls it the
    /// "did not matter" case ("the hero would have survived the fight even without this quaff... no
    /// participation credit"), the opposite of a decisive counterfactual; thanking someone for it
    /// would be the exact KillingBlow-inflation defect P2-PROOF-19 exists to fix, one beat type
    /// over. <see cref="BeatType.PotionLifesave"/> IS a true counterfactual (the strict replay
    /// crosses zero) but its item's <c>ItemSlot</c> is <c>Consumable</c> — drunk and gone, never
    /// re-equipped — so it can never satisfy <see cref="IsWorn"/> below; left out rather than kept
    /// as dead code that could never fire. <see cref="BeatType.KillingBlow"/> is a RECORDED FACT,
    /// never a counterfactual by itself (<c>TellingQuery</c>'s own doc comment) — narrowing it to
    /// the decisive minority via <c>TellingQuery.KillingBlowPayload.MonsterHpWithoutItem &gt; 0</c>
    /// is explicitly optional per the brief and deliberately not done here, to keep this file's read
    /// surface to the <see cref="GameState.EventLog"/> it already scans everywhere else rather than
    /// pulling in <c>ExpeditionResult</c>/<c>VenueDefinition</c> replay plumbing for a Godot-side
    /// unit. A bare kill must never speak this line, so <see cref="BeatType.KillingBlow"/> stays
    /// excluded, full stop. <see cref="BeatType.ToolAssist"/> has no emitter yet (its own Contracts
    /// comment) — unreachable.</para>
    ///
    /// <para><b>"Last night"</b> mirrors <c>LedgerModal.ShowFor</c>'s own day arithmetic
    /// (<c>state.Phase == DayPhase.Evening ? state.Day : state.Day - 1</c>): the counter only ever
    /// opens in Morning, so this is always the night just resolved, never an older one resurfacing.
    /// <b>"Wearing"</b> means the beat's own item id is still sitting in one of this hero's four gear
    /// slots right now (<see cref="IsWorn"/>) — a re-equip or a sale since would silently retire the
    /// line, which is correct: the sentence is about what they carry into today, not a historical
    /// footnote.</para>
    /// </summary>
    public static string? ThanksLine(Hero hero, GameState state)
    {
        var lastNightDay = state.Phase == DayPhase.Evening ? state.Day : state.Day - 1;

        AttributionBeatEvent? found = null;
        foreach (var gameEvent in state.EventLog)
        {
            if (gameEvent is not AttributionBeatEvent beat
                || beat.Day != lastNightDay
                || beat.Hero != hero.Id
                || !CounterfactualBeatTypes.Contains(beat.Beat)
                || !IsWorn(hero.Gear, beat.Item))
            {
                continue;
            }

            // LethalSave (a life) outranks BreakpointClear (a floor) on the rare night a hero
            // somehow earned both flavors of decisive beat — a deterministic tiebreak (EventLog
            // append order keeps the earlier one on any further tie).
            if (found is null || BeatPriority(beat.Beat) > BeatPriority(found.Beat))
            {
                found = beat;
            }
        }

        if (found is null || !state.Items.TryGetValue(found.Item.Value, out var item))
        {
            return null;
        }

        return found.Beat switch
        {
            BeatType.LethalSave =>
                $"This {item.Name} took a blow on floor {found.Floor} that should have killed me.",
            BeatType.BreakpointClear =>
                $"We don't clear floor {found.Floor} without this {item.Name}.",
            // Unreachable: CounterfactualBeatTypes (below) admits only the two arms above.
            _ => null,
        };
    }

    /// <summary>See <see cref="ThanksLine"/>'s own doc comment for why exactly these two members and
    /// no others qualify.</summary>
    private static readonly ImmutableHashSet<BeatType> CounterfactualBeatTypes =
        ImmutableHashSet.Create(BeatType.LethalSave, BeatType.BreakpointClear);

    private static int BeatPriority(BeatType beat) => beat switch
    {
        BeatType.LethalSave => 1,
        BeatType.BreakpointClear => 0,
        _ => -1,
    };

    /// <summary>Whether <paramref name="item"/> is sitting in any of this hero's four gear slots
    /// right now — the "wearing" half of <see cref="ThanksLine"/>'s gate.</summary>
    private static bool IsWorn(GearSet gear, ItemId item) =>
        gear.Weapon == item || gear.Shield == item || gear.Armor == item || gear.Trinket == item;

    /// <summary>
    /// What the active customer opens with, read BEFORE the player presents anything. U1 (§11.11):
    /// the actual slot pick is <see cref="CounterForecast.Wants"/>, extracted so this line can never
    /// name a want the counter forecast board did not ALSO project the night before — same slot,
    /// same source, two callers. Names the empty gear slot the hero's own gap query
    /// (<see cref="RaidForecast.MissingItemSlots"/>, fixed order Weapon/Shield/Armor) reports,
    /// alongside their own gold on hand — the real budget, never a rounded or invented figure. A
    /// hero with no gaps (a full loadout) instead names whichever CURRENT shelf item
    /// <see cref="CounterForecast.Wants"/> found to be a genuine upgrade, so a full-loadout hero's
    /// stated want can never name something the sim would actually refuse if presented. When
    /// neither signal fires (nothing on the shelf would help them), the line degrades to a plain
    /// "browsing" statement rather than inventing a want the sim has no basis for.
    /// </summary>
    public static string WantLine(Hero hero, GameState state)
    {
        var missing = RaidForecast.MissingItemSlots(hero.Gear);
        var wantSlot = CounterForecast.Wants(hero, state);

        if (missing.Count > 0)
        {
            return $"Looking for {SlotArticle(wantSlot!.Value)} — about {hero.Gold}g on me.";
        }

        return wantSlot is { } slot
            ? $"Could use a better {SlotNoun(slot)} if the price is fair — {hero.Gold}g on me."
            : $"Just browsing — {hero.Gold}g on me, if something catches my eye.";
    }

    /// <summary>
    /// P2-PEOPLE-21 ("Forge it — Torvald waits"): the same slot pick <see cref="WantLine"/> speaks
    /// in first person ("Looking for a weapon — about 40g on me"), extracted as a bare third-person
    /// noun phrase ("a weapon") for a second speaker — the counter's own "forge it" handoff names
    /// who is waiting and what they said FROM THE OUTSIDE, so it needs the noun, not the sentence.
    /// Reuses <see cref="CounterForecast.Wants"/> verbatim (missing-slot-first, else the largest
    /// genuine shelf upgrade) rather than re-deriving the pick, so this can never name a want the
    /// counter itself would not also ask for.
    /// </summary>
    public static string WantNoun(Hero hero, GameState state) =>
        CounterForecast.Wants(hero, state) is { } slot ? SlotArticle(slot) : "nothing in particular";

    /// <summary>
    /// The customer's spoken reply to a Present, keyed on the sim's OWN realized verdict kind. By
    /// the time a caller can read this back, <c>CounterQueueSystem.ResolvePresentedItem</c> (internal
    /// — not cref-able from here) has already called <see cref="ShoppingAi.EvaluateItem"/> and
    /// emitted the real event: a <c>CustomerCountered</c> means Buy (a round opened); a
    /// <c>CustomerWalked</c> means Pass, and its own <c>Reason</c> IS the R8 prose this renders
    /// verbatim — never re-derived, never re-worded. Exhaustive by construction: a
    /// <see cref="ShoppingVerdictKind"/> this switch doesn't know throws instead of silently
    /// rendering an empty bubble (<c>CustomerVoiceTests</c> enumerates the whole enum and pins a
    /// non-empty reply for every member).
    /// </summary>
    public static string PresentReply(ShoppingVerdictKind kind, string itemName, string passReason) => kind switch
    {
        ShoppingVerdictKind.Buy => $"{itemName}? I could use that.",
        ShoppingVerdictKind.Pass => passReason,
        _ => throw new System.ArgumentOutOfRangeException(
            nameof(kind), kind, $"CustomerVoice has no Present reply for verdict kind {kind}."),
    };

    /// <summary>
    /// M2b: <see cref="PassReasonKind"/> members that deliberately have no spoken line of their own,
    /// each with the reason it is exempt. Deny-by-default — <c>StoriedGearTests</c> reflects over the
    /// WHOLE enum and requires every member to either produce a non-empty reply from
    /// <see cref="PassReply"/> or appear here, and pins this map's size, so a new pass reason cannot
    /// be added and quietly go unvoiced. A hand-listed roster is exactly the guard shape that has
    /// stopped covering its family in this repo before; nothing here is hand-listed at the call site.
    /// </summary>
    public static readonly ImmutableSortedDictionary<PassReasonKind, string> UnvoicedPassReasons =
        ImmutableSortedDictionary<PassReasonKind, string>.Empty.Add(
            PassReasonKind.None,
            "Buy verdicts only. A Buy opens a haggle round instead of walking the customer, so no "
            + "walk-away line ever carries this reason and there is nothing for a bubble to say.");

    /// <summary>
    /// The customer's spoken refusal, keyed on the sim's OWN typed pass reason
    /// (<see cref="PassReasonKind"/>) rather than on prose. Every arm but one hands back
    /// <paramref name="simReason"/> — the R8 line the sim already wrote — verbatim, which is exactly
    /// what shipped before this method existed.
    ///
    /// <para>The one exception is <see cref="PassReasonKind.Sentimental"/>, the storied-gear loyalty
    /// gate: when the hero is refusing because of what a piece of your old work has already done for
    /// them, they say so in their own voice instead of the log's. That line is a mechanism, not
    /// decoration — <see cref="WalkReply"/> is the ONLY production caller and it will not reach this
    /// arm unless <see cref="ShoppingAi.EvaluateItem"/>, re-run read-only against live state,
    /// reproduces the recorded refusal exactly.</para>
    ///
    /// <para>Exhaustive by construction: an unknown member throws rather than rendering an empty
    /// bubble (the <see cref="PresentReply"/> precedent), which is what makes the reflective
    /// deny-by-default test above able to fail on a newly added reason.</para>
    /// </summary>
    public static string PassReply(PassReasonKind kind, string simReason, string? storiedGearName) => kind switch
    {
        PassReasonKind.Sentimental when storiedGearName is { Length: > 0 } worn =>
            $"I'm keeping the {worn} — it's been down there with me.",
        PassReasonKind.Sentimental => simReason,
        PassReasonKind.RoleMismatch => simReason,
        PassReasonKind.TooHeavy => simReason,
        PassReasonKind.CannotAfford => simReason,
        PassReasonKind.NotAnUpgrade => simReason,
        PassReasonKind.QualityTooLow => simReason,
        _ => throw new System.ArgumentOutOfRangeException(
            nameof(kind), kind, $"CustomerVoice has no pass reply for reason kind {kind}."),
    };

    /// <summary>
    /// What a customer who just walked away actually says. <see cref="CustomerWalked"/> records the
    /// hero, the item and the R8 prose but not the typed reason, so this re-derives it the way this
    /// whole file derives everything: by calling the sim's OWN evaluator read-only against live
    /// state, never by parsing the prose and never by inventing a second rule.
    ///
    /// <para><b>Why the equality guard.</b> A re-run verdict is only trusted when its
    /// <see cref="ShoppingVerdict.Reason"/> matches the recorded one character for character. That
    /// makes the storied-gear line impossible to render as decoration: it appears when, and only
    /// when, the sim will still produce that exact refusal for that exact hero and item. Anything
    /// else — the hero gone, the item off the shelf, a verdict that no longer matches — falls back
    /// to the recorded reason verbatim, which is what this surface rendered before M2b.</para>
    /// </summary>
    public static string WalkReply(GameState state, HeroId hero, ItemId? presented, string recordedReason)
    {
        var kind = ReplayPassReason(state, hero, presented, recordedReason, out var storiedGearName);
        return kind is null ? recordedReason : PassReply(kind.Value, recordedReason, storiedGearName);
    }

    /// <summary>The read-only replay behind <see cref="WalkReply"/> — null whenever the recorded
    /// refusal cannot be reproduced exactly, which is the caller's signal to say nothing new.</summary>
    private static PassReasonKind? ReplayPassReason(
        GameState state, HeroId hero, ItemId? presented, string recordedReason, out string? storiedGearName)
    {
        storiedGearName = null;

        if (presented is not { } itemId
            || !state.Heroes.TryGetValue(hero.Value, out var customer)
            || !state.Items.TryGetValue(itemId.Value, out var item))
        {
            return null;
        }

        // The price the sim judged is the shelf price (CounterQueueSystem.ResolvePresentedItem
        // reads the same entry); an item that has since left the shelf cannot be replayed.
        var shelfEntry = state.Player.Shelf.FirstOrDefault(entry => entry.Item == itemId);
        if (shelfEntry is null)
        {
            return null;
        }

        var verdict = ShoppingAi.EvaluateItem(customer, item, shelfEntry.Price, state.Items);
        if (verdict.Kind != ShoppingVerdictKind.Pass || verdict.Reason != recordedReason)
        {
            return null;
        }

        if (verdict.PassReason == PassReasonKind.Sentimental
            && customer.Gear.Slot(item.Slot) is { } wornId
            && state.Items.TryGetValue(wornId.Value, out var worn))
        {
            storiedGearName = worn.Name;
        }

        return verdict.PassReason;
    }

    /// <summary>
    /// The customer's spoken reaction to a Suggest — derived from whether the sim's OWN upsell bonus
    /// (<c>HaggleResolver.ApplySuggestBonus</c>, internal — not cref-able from here) actually moved
    /// <see cref="CounterState.InterestPermille"/>, read back by the caller as a before/after delta
    /// (the same comparison the counter panel already makes for its plain-text feedback line). This
    /// never re-derives the upsell-fit rule itself, so the spoken line can never contradict the
    /// Interest chip that moved (or didn't) in the same refresh.
    /// </summary>
    public static string SuggestReply(string itemName, bool interestRose) => interestRose
        ? $"{itemName}? ...I do lack one."
        : "No use for that.";

    private static string SlotArticle(ItemSlot slot) => slot switch
    {
        ItemSlot.Weapon => "a weapon",
        ItemSlot.Shield => "a shield",
        ItemSlot.Armor => "some armor",
        ItemSlot.Trinket => "a trinket",
        _ => "some gear",
    };

    private static string SlotNoun(ItemSlot slot) => slot switch
    {
        ItemSlot.Weapon => "weapon",
        ItemSlot.Shield => "shield",
        ItemSlot.Armor => "armor",
        ItemSlot.Trinket => "trinket",
        _ => "piece",
    };
}
