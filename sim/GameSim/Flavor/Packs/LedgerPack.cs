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
                "Floor {floor} let {hero} go — the {gold}g in the pouch says it wasn't charity."),
            [$"{Survived}/dramatic"] = ImmutableList.Create(
                "Triumphant! {hero} returns from floor {floor} bearing {gold}g!",
                "{hero} strides home from floor {floor} — hear the {gold}g sing in the purse!",
                "Floor {floor} could not hold {hero} — back, alive, and {gold}g the richer!",
                "Let the ledger shout it: {hero}, floor {floor}, {gold}g won!"),
            [$"{Survived}/wry"] = ImmutableList.Create(
                "{hero} came back from floor {floor} with {gold}g and most of their dignity.",
                "Floor {floor}: survived. {gold}g: earned. {hero}: insufferable about it.",
                "{hero} calls {gold}g fair pay for floor {floor}. The floor declined to comment.",
                "Another floor {floor}, another {gold}g. {hero} makes it look almost sensible."),
            [$"{Survived}/omen"] = ImmutableList.Create(
                "{hero} returned from floor {floor} with {gold}g. The Mine let them keep both.",
                "Floor {floor} released {hero} — {gold}g in hand, and a debt unspoken.",
                "The candles stayed lit: {hero}, back from floor {floor}, {gold}g richer.",
                "{gold}g out of floor {floor}. {hero} carried up more than coin, mark me."),

            // ------------------------------------------------------------- died
            [$"{Died}/gruff"] = ImmutableList.Create(
                "{hero} stays on floor {floor}. Strike the name.",
                "Floor {floor} kept {hero}. Coldest line in the book.",
                "{hero}, dead on floor {floor}. Settle the accounts.",
                "No return for {hero} — floor {floor} closed over them."),
            [$"{Died}/dramatic"] = ImmutableList.Create(
                "Fallen! {hero} lies still on floor {floor}!",
                "The ledger bleeds tonight: {hero}, lost to floor {floor}!",
                "Floor {floor} has taken {hero} — weep, and write it down!",
                "{hero} will not come home — floor {floor} keeps its dead!"),
            [$"{Died}/wry"] = ImmutableList.Create(
                "{hero} is staying on floor {floor}. Permanently.",
                "Floor {floor} gets custody of {hero}. No appeal.",
                "{hero}'s account closes on floor {floor}. Balance: everything.",
                "One last entry for {hero}: floor {floor}, no forwarding address."),
            [$"{Died}/omen"] = ImmutableList.Create(
                "{hero}'s thread ends on floor {floor}. The ink knew before I did.",
                "Floor {floor} claimed {hero}. The tithe is paid.",
                "Write {hero} in the cold column — floor {floor} keeps them now.",
                "The Mine whispered {hero}'s name once more — floor {floor}, then silence."),
        },
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // v1's CLI fate lines, verbatim as composed on screen (see class doc).
            [Survived] = "{hero}: returned from floor {floor}, earned {gold}g",
            [Died] = "{hero}: DIED on floor {floor}",
        });
}
