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
/// </summary>
public static class CustomerVoice
{
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
