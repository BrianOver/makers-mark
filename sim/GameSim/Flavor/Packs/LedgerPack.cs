using System.Collections.Immutable;

namespace GameSim.Flavor.Packs;

/// <summary>
/// The Evening Ledger content pack (U5): fate lines for per-hero return cards, rendered
/// through <see cref="FlavorEngine"/>. Data only — no behavior, no IO, no RNG. This is the
/// second surface on the pack engine (after <see cref="TavernPack"/>), proving it generalizes.
///
/// <para><b>Key scheme (committed, same as TavernPack).</b> Full key =
/// <c>"&lt;baseKey&gt;/&lt;voiceId&gt;"</c>: <c>"survived"</c> or <c>"died"</c> crossed with
/// <see cref="VoiceProfile.Voices"/>. The hero the card is about supplies the voice
/// (<see cref="VoiceProfile.VoiceFor"/>).</para>
///
/// <para><b>Slots (committed, per base key)</b> — see <see cref="SlotNames"/>:
/// <c>survived</c> carries <c>{hero}</c>/<c>{floor}</c>/<c>{gold}</c> (gold = the day's
/// expedition income, digits only — templates supply the "g" suffix); <c>died</c> carries
/// <c>{hero}</c>/<c>{floor}</c>. The engine's validation requires every provided value
/// verbatim in the output, so every variant mentions every slot of its kind.</para>
///
/// <para><b>Variant pick ids (the caller's contract, enforced by LedgerQueryTests):</b>
/// death cards hash on the hero's stamped <c>HeroDied</c> event id; survivor cards on
/// <c>StableHash.Mix(day, heroId)</c> — deterministic, per-hero distinct, no event lookup
/// needed. Campaign identity is <c>GameState.Rng.Inc</c>, as everywhere (KTD3).</para>
///
/// <para><b>Fallbacks:</b> the v1 CLI fate lines, verbatim as composed on screen — the CLI
/// printed <c>"{HeroName}: " + fate</c>, so the templates carry the <c>{hero}: </c> prefix
/// both to reproduce that exact line and because a fallback must mention every provided
/// slot to pass the engine's validation (a fallback that can fail is a pack authoring bug).</para>
///
/// <para><b>Breadth (T8a).</b> Every (baseKey, voice) key carries TWELVE variants — the launch
/// four plus eight more in the same frozen voice register. Breadth lives in this existing pack file:
/// additive same-surface packs are unsupported (LedgerQuery binds one pack, and
/// <c>Pack_VariantKeys_AreExactlyBaseKeysCrossVoices</c> pins the exact key set), so ruling R8 grows
/// the file in place. The launch four keep indices 0-3; the golden death card (died/omen index 1)
/// is unmoved.</para>
///
/// <para><b>Conformance floor:</b> every (baseKey, voice) key carries at least 4 variants.
/// <c>LedgerPackTests</c> enforces all of the above structurally.</para>
/// </summary>
public static class LedgerPack
{
    /// <summary>Base key for a survivor's return card.</summary>
    public const string Survived = "survived";

    /// <summary>Base key for a death card.</summary>
    public const string Died = "died";

    /// <summary>
    /// The slot names each base key's card provides — the single source of truth shared by
    /// <c>LedgerQuery</c> (which fills them) and the conformance tests (which sweep them).
    /// </summary>
    public static readonly ImmutableSortedDictionary<string, ImmutableArray<string>> SlotNames =
        new Dictionary<string, ImmutableArray<string>>(StringComparer.Ordinal)
        {
            [Survived] = ["hero", "floor", "gold"],
            [Died] = ["hero", "floor"],
        }.ToImmutableSortedDictionary(StringComparer.Ordinal);

    /// <summary>The pack itself. Static readonly: built once, immutable forever.</summary>
    public static readonly FlavorPack Pack = FlavorPack.Create(
        new Dictionary<string, ImmutableList<string>>(StringComparer.Ordinal)
        {
            // ------------------------------------------------------------- survived
            [$"{Survived}/gruff"] = ImmutableList.Create(
                "{hero} walked out of floor {floor} with {gold}g. Good enough.",
                "Back from floor {floor}, {gold}g heavier. {hero} earned every coin.",
                "{hero}: floor {floor}, {gold}g, all limbs attached. Call it a day.",
                "Floor {floor} let {hero} go — the {gold}g in the pouch says it wasn't charity.",
                "{hero} climbed out of floor {floor}, {gold}g to show. Not bad.",
                "Floor {floor} paid {hero} {gold}g and let them keep their skin.",
                "{gold}g and a pulse — {hero} calls floor {floor} a good day.",
                "{hero} worked floor {floor} for {gold}g. Earned, not gifted.",
                "Back from floor {floor}, {hero}, {gold}g richer and grumbling. Same as ever.",
                "Floor {floor} took a bite and paid {gold}g for it. {hero} took the deal.",
                "{hero} banked {gold}g off floor {floor}. Count it, log it, done.",
                "Floor {floor}, {gold}g, all fingers accounted for. {hero} did fine."),
            [$"{Survived}/dramatic"] = ImmutableList.Create(
                "Triumphant! {hero} returns from floor {floor} bearing {gold}g!",
                "{hero} strides home from floor {floor} — hear the {gold}g sing in the purse!",
                "Floor {floor} could not hold {hero} — back, alive, and {gold}g the richer!",
                "Let the ledger shout it: {hero}, floor {floor}, {gold}g won!",
                "Victory and coin! {hero} strides back from floor {floor} with {gold}g!",
                "Sing the ledger's joy — {hero} bore {gold}g up from floor {floor}!",
                "Hear it! {hero} conquered floor {floor} and carried home {gold}g!",
                "The deep gave up {gold}g to {hero}, and floor {floor} let them pass!",
                "Home in glory — {hero}, floor {floor} behind them, {gold}g in hand!",
                "Let the coins ring — {hero} won {gold}g from the maw of floor {floor}!",
                "Floor {floor} is beaten and {gold}g the poorer — rejoice for {hero}!",
                "A hero returns! {hero}, {gold}g, and floor {floor} survived!"),
            [$"{Survived}/wry"] = ImmutableList.Create(
                "{hero} came back from floor {floor} with {gold}g and most of their dignity.",
                "Floor {floor}: survived. {gold}g: earned. {hero}: insufferable about it.",
                "{hero} calls {gold}g fair pay for floor {floor}. The floor declined to comment.",
                "Another floor {floor}, another {gold}g. {hero} makes it look almost sensible.",
                "{hero} priced floor {floor} at {gold}g. The floor felt underpaid.",
                "{gold}g for a day on floor {floor}. {hero} thinks that's a fortune. It's rent.",
                "{hero} calls {gold}g fair pay for floor {floor}. Floor {floor} did not sign off.",
                "Another floor {floor}, another {gold}g, another lecture from {hero} about it.",
                "{hero} survived floor {floor} and {gold}g happened. Cause unclear, results banked.",
                "Floor {floor} let {hero} keep {gold}g. Generous, for a hole that eats people.",
                "{gold}g richer, {hero} limps out of floor {floor} looking almost pleased.",
                "{hero} made floor {floor} look easy. It wasn't. Here's {gold}g anyway."),
            [$"{Survived}/omen"] = ImmutableList.Create(
                "{hero} returned from floor {floor} with {gold}g. The Mine let them keep both.",
                "Floor {floor} released {hero} — {gold}g in hand, and a debt unspoken.",
                "The candles stayed lit: {hero}, back from floor {floor}, {gold}g richer.",
                "{gold}g out of floor {floor}. {hero} carried up more than coin, mark me.",
                "The candles held for {hero} — back from floor {floor}, {gold}g in the purse.",
                "Floor {floor} loosed its grip on {hero}. {gold}g came up too, and a debt.",
                "{gold}g out of floor {floor}, and {hero} breathing. The deep asks its price later.",
                "{hero} carried {gold}g up from floor {floor}. They carried something heavier too.",
                "The ledger's ink stayed black for {hero}: floor {floor}, {gold}g, alive.",
                "Floor {floor} gave {hero} {gold}g and a warning. Only one was spent.",
                "The Mine counted {hero} out at floor {floor} — {gold}g, and a name still owed.",
                "{hero} rose from floor {floor} with {gold}g. Rising always costs. Mark it."),

            // ------------------------------------------------------------- died
            [$"{Died}/gruff"] = ImmutableList.Create(
                "{hero} stays on floor {floor}. Strike the name.",
                "Floor {floor} kept {hero}. Coldest line in the book.",
                "{hero}, dead on floor {floor}. Settle the accounts.",
                "No return for {hero} — floor {floor} closed over them.",
                "Floor {floor} kept {hero}. Close the account.",
                "{hero}'s last floor was {floor}. Cold entry, cold end.",
                "{hero}, floor {floor}, done. Settle what's owed.",
                "Floor {floor} took {hero} and gave nothing back. Log it.",
                "{hero} won't climb out of floor {floor}. Draw the line.",
                "Floor {floor}'s the last word on {hero}. Write it plain.",
                "{hero} ends on floor {floor}. The book doesn't argue.",
                "Strike {hero} off. Floor {floor} keeps the rest."),
            [$"{Died}/dramatic"] = ImmutableList.Create(
                "Fallen! {hero} lies still on floor {floor}!",
                "The ledger bleeds tonight: {hero}, lost to floor {floor}!",
                "Floor {floor} has taken {hero} — weep, and write it down!",
                "{hero} will not come home — floor {floor} keeps its dead!",
                "Grief! Floor {floor} has taken {hero} from us!",
                "Weep for {hero}, whom floor {floor} keeps forever!",
                "Floor {floor} claimed a life tonight — {hero} will not come home!",
                "Toll the bell for {hero}! Floor {floor} holds them now!",
                "A hero's tale ends in the dark — {hero}, floor {floor}!",
                "Lost! {hero} passed into floor {floor} and did not return!",
                "The dark of floor {floor} has a new name — {hero}!",
                "Mourn {hero}, swallowed whole by floor {floor}!"),
            [$"{Died}/wry"] = ImmutableList.Create(
                "{hero} is staying on floor {floor}. Permanently.",
                "Floor {floor} gets custody of {hero}. No appeal.",
                "{hero}'s account closes on floor {floor}. Balance: everything.",
                "One last entry for {hero}: floor {floor}, no forwarding address.",
                "{hero} put down deep roots on floor {floor}. Very deep.",
                "Floor {floor} claims {hero}. Appeals go nowhere, literally.",
                "{hero}'s tab is now floor {floor}'s problem. Good luck to it.",
                "{hero} found floor {floor}'s one non-refundable feature.",
                "Long-term lease for {hero} on floor {floor}. Term: eternal.",
                "{hero} decided to stay on floor {floor}. Wasn't really a decision.",
                "The ledger closes on {hero}: floor {floor}, balance zero.",
                "{hero} committed fully to floor {floor}. Full marks, no {hero}."),
            [$"{Died}/omen"] = ImmutableList.Create(
                "{hero}'s thread ends on floor {floor}. The ink knew before I did.",
                "Floor {floor} claimed {hero}. The tithe is paid.",
                "Write {hero} in the cold column — floor {floor} keeps them now.",
                "The Mine whispered {hero}'s name once more — floor {floor}, then silence.",
                "The Mine called in {hero}'s debt on floor {floor}. Paid in full.",
                "Floor {floor} keeps {hero} now. The deep collects what it lends.",
                "{hero} crossed over on floor {floor}. The candle went with them.",
                "The crows sat the sill for {hero}. Floor {floor}, and silence.",
                "{hero}'s name left the roster and joined floor {floor}'s tally.",
                "Salt the doorstep for {hero}. Floor {floor} keeps its own.",
                "Floor {floor} sealed over {hero}. Some doors don't reopen.",
                "{hero} paid the deep's tithe on floor {floor}. It always comes due."),
        },
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // v1's CLI fate lines, verbatim as composed on screen (see class doc).
            [Survived] = "{hero}: returned from floor {floor}, earned {gold}g",
            [Died] = "{hero}: DIED on floor {floor}",
        });
}
