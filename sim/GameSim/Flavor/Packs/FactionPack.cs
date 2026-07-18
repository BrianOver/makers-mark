using System.Collections.Immutable;

namespace GameSim.Flavor.Packs;

/// <summary>
/// The faction-standing content pack (P5 U4, R9/KTD7): the templated tavern lines a
/// <c>FactionStandingShifted</c> beat renders through <see cref="FlavorEngine"/>. Data only — no
/// behavior, no IO, no RNG. The third surface on the pack engine (after <see cref="TavernPack"/> and
/// <see cref="LedgerPack"/>), proving voicing a NEW drama source needs no new text mechanism.
///
/// <para><b>Key scheme (committed, same as the other packs).</b> Full key =
/// <c>"&lt;baseKey&gt;/&lt;voiceId&gt;"</c>. The base key is the shift DIRECTION —
/// <see cref="Favored"/> (the town warmed) or <see cref="Cooled"/> (it cooled) — so each direction
/// owns its own fallback (the engine's fallback lookup keys on the segment before the first '/').
/// Voice ids come from <see cref="VoiceProfile.Voices"/>; a faction beat has no protagonist, so the
/// voice is picked hero-lessly via <see cref="VoiceProfile.VoiceForFaction"/>.</para>
///
/// <para><b>Slots (committed, per base key)</b> — see <see cref="SlotNames"/>: <c>{faction}</c> (the
/// faction's DISPLAY name, carried in on the event so the renderer needs no registry lookup, KTD7)
/// and <c>{direction}</c> (the crossing word — "warmed" for favored, "cooled" for cooled). The
/// engine's validation requires every provided value verbatim in the output, so every variant below
/// mentions both slots. Prose leans on the price consequence the player actually feels (R7): a warmer
/// guild sells ore cheaper, a cooler one dearer.</para>
///
/// <para><b>Fallbacks:</b> one per base key, in the same plain register — new drama with no prior
/// hardcoded line. Simple enough to always pass validation (pack conformance tests assert this).</para>
///
/// <para><b>Breadth (T8a + C4).</b> Every (baseKey, voice) key carries at least twelve variants — the
/// launch four plus eight more in the same frozen voice register — then the C4 tone pass (design doc
/// <c>2026-07-18-variety-tone-direction.md</c> §1) adds comic-bureaucratic "permit-office" variants per
/// voice (idea #18): omen = failed portents, gruff = invoices/lectures, dramatic = grandiosity about
/// mundane coppers, wry stays wry. Every added line stays pinned to the cheaper/dearer-ore consequence
/// the player actually feels (R7). Breadth lives in this existing pack file: additive same-surface packs
/// are unsupported (the generator binds one faction pack, and
/// <c>Pack_VariantKeys_AreExactlyBaseKeysCrossVoices</c> pins the exact key set), so ruling R8 grows it
/// in place.</para>
///
/// <para><b>Conformance floor:</b> every (baseKey, voice) key carries at least 4 variants — no
/// fallback-only keys. <c>FactionPackTests</c> enforces all of the above structurally.</para>
/// </summary>
public static class FactionPack
{
    /// <summary>Base key for a warming shift (<c>StandingShiftDirection.Favored</c>).</summary>
    public const string Favored = "favored";

    /// <summary>Base key for a cooling shift (<c>StandingShiftDirection.Cooled</c>).</summary>
    public const string Cooled = "cooled";

    /// <summary>
    /// The slot names each base key's event provides — the single source of truth shared by the
    /// generator (which fills them from the event) and the conformance tests (which sweep them).
    /// </summary>
    public static readonly ImmutableSortedDictionary<string, ImmutableArray<string>> SlotNames =
        new Dictionary<string, ImmutableArray<string>>(StringComparer.Ordinal)
        {
            [Favored] = ["faction", "direction"],
            [Cooled] = ["faction", "direction"],
        }.ToImmutableSortedDictionary(StringComparer.Ordinal);

    /// <summary>The pack itself. Static readonly: built once, immutable forever.</summary>
    public static readonly FlavorPack Pack = FlavorPack.Create(
        new Dictionary<string, ImmutableList<string>>(StringComparer.Ordinal)
        {
            // ------------------------------------------------------------- favored (direction = "warmed")
            [$"{Favored}/gruff"] = ImmutableList.Create(
                "The {faction} {direction} to your custom. Cheaper ore while it lasts. Don't waste it.",
                "The {faction} have {direction} to your shop — the ore comes down a coin. That's the trade.",
                "Steady buying, and the {faction} {direction}. Picks and ingots ease off.",
                "The {faction} {direction} toward your account. Ore's cheaper this season.",
                "The {faction} {direction} to your coin. Ore's cheaper. Use it.",
                "Word's out — the {faction} {direction} to your shop. Prices ease.",
                "The {faction} {direction}. Buy while the ore runs kind.",
                "Steady custom pays: the {faction} {direction}, the picks come down.",
                "The {faction} {direction} toward you. Cheaper iron, plain and simple.",
                "The {faction} {direction} on your account. Don't let it lapse this time.",
                "Guild's warm — the {faction} {direction}, and ore's off a coin.",
                "The {faction} {direction} to your custom. That's a discount, not a favor.",
                "The {faction} {direction} to your custom. Filed the discount under 'earned.' Ore's down a coin. Don't make me refile it.",
                "The {faction} {direction}. Stamped, sealed, cheaper ore approved. Keep buying and the stamp stays wet."),
            [$"{Favored}/dramatic"] = ImmutableList.Create(
                "Rejoice! The {faction} have {direction} to your forge — the ore flows cheap!",
                "The great {faction} {direction} at last, and the price of iron bows before you!",
                "Sing it through the town: the {faction} {direction}, and every pick comes kinder!",
                "Behold — the {faction} {direction} to you, and the ledger sings a sweeter tune!",
                "Glad tidings! The {faction} {direction}, and iron bows to your purse!",
                "Sound the horns — the {faction} {direction}, and the ore runs gentle!",
                "The {faction} {direction} to your name, and the ledger sings sweet!",
                "Behold the guild's grace — the {faction} {direction}, ore cheap as spring water!",
                "A golden season! The {faction} {direction}, and the forge drinks cheap iron!",
                "The mighty {faction} {direction} toward you — let the anvils ring in thanks!",
                "Fortune smiles: the {faction} {direction}, and every ingot costs you less!",
                "The {faction} {direction} to your shop — sing it down every street!",
                "Rejoice — the {faction} {direction}! A whole coin off the ore! Kingdoms have risen on less, or nearly!",
                "The great {faction} {direction} to your name, and the price of iron bows — bows! — by an entire copper!"),
            [$"{Favored}/wry"] = ImmutableList.Create(
                "The {faction} {direction} to you. Miracles happen; so do discounts.",
                "Turns out the {faction} {direction} — apparently coin buys affection. Who knew.",
                "The {faction} {direction} to your shop. Enjoy the cheaper ore before they remember themselves.",
                "The mighty {faction} {direction}. The ore's cheaper; try to look surprised.",
                "The {faction} {direction} toward you. Turns out coin is very persuasive.",
                "Apparently the {faction} {direction}. Enjoy it before they check the mood again.",
                "The {faction} {direction} to your shop. Warmth you can measure in coppers off the ore.",
                "The {faction} {direction}. Cheaper ore, no strings — well, the usual strings.",
                "The great {faction} {direction} to you. Try to accept the affection gracefully.",
                "The {faction} {direction}. The ore's down a coin; act like you expected it.",
                "So the {faction} {direction} at last. Coin buys love. Noted for the ledger.",
                "The {faction} {direction} toward your account. Sentiment, priced per ingot.",
                "The {faction} {direction} to you. Somewhere a clerk stamped 'friend' and sighed. Ore's cheaper; don't thank the clerk.",
                "Apparently the {faction} {direction}. There's a form for affection now, filed in triplicate. The ore's down a coin regardless."),
            [$"{Favored}/omen"] = ImmutableList.Create(
                "The {faction} {direction} to you — the coals burned blue last night. The deep favors your coin.",
                "I read it in the ore-dust: the {faction} {direction}. Kinder prices ride a kind wind.",
                "The {faction} {direction}. Mark it — the mountain remembers who feeds its guild.",
                "When the {faction} {direction}, the old miners say the veins run richer. Cheaper ore, and an omen.",
                "The {faction} {direction}. The ore-dust settled kindly. Read it as you like.",
                "Kinder prices ride a kind wind: the {faction} {direction} toward you.",
                "The {faction} {direction}. The mountain feeds those who feed its guild.",
                "The {faction} {direction} to your name. The deep marks a friend when it sees one.",
                "The candles stood tall at the assay — the {faction} {direction} to you.",
                "The {faction} {direction}. Cheaper ore, and an omen worth keeping.",
                "The veins warmed the day the {faction} {direction}. Such signs hold, a while.",
                "The {faction} {direction} to you. Salt the sill in thanks — cheap ore is a gift.",
                "I foretold the {faction} would sour. Instead they {direction}, and the ore came cheap. The omens have filed a correction.",
                "The signs said dear iron. The {faction} {direction} and made them liars. Cheaper ore, and a portent eating its words."),

            // ------------------------------------------------------------- cooled (direction = "cooled")
            [$"{Cooled}/gruff"] = ImmutableList.Create(
                "The {faction} {direction} on you. Ore costs more now. Should've kept trading.",
                "The {faction} have {direction} — neglect does that. The picks come dearer.",
                "Word is the {faction} {direction} toward your shop. Prices climb. That's the trade.",
                "The {faction} {direction}. Stop buying, they stop caring. Ore's up a coin.",
                "The {faction} {direction} toward your shop. Dearer iron now. That's neglect.",
                "Word is the {faction} {direction}. Prices climb. Nobody's fault but the empty ledger.",
                "The {faction} {direction} on your account. Pay more or mend it. Your call.",
                "The {faction} {direction}. The picks bite deeper now. Simple arithmetic.",
                "Guild's cold — the {faction} {direction}, and the ore knows it.",
                "The {faction} {direction} toward you. Dearer ore, colder welcome.",
                "The {faction} {direction}. Should've fed the guild. Now it feeds on you.",
                "The {faction} {direction} on your custom. Costs more to make good than to keep good.",
                "The {faction} {direction} on you. Reclassified your account 'neglectful.' Ore's up a coin. Appeals go in the usual bin.",
                "The {faction} {direction}. Marked the file 'lapsed,' dearer ore attached. Mend it or pay the surcharge. Your ledger."),
            [$"{Cooled}/dramatic"] = ImmutableList.Create(
                "Alas! The {faction} have {direction} toward your forge — the ore turns dear!",
                "The {faction} {direction}, and the price of iron rises like a tide against you!",
                "Hear it and grieve: the {faction} {direction}, and every pick bites deeper into the purse!",
                "The great {faction} {direction} — cold shoulders, and colder prices!",
                "Woe! The {faction} {direction}, and iron's price rises against you!",
                "Grieve, tavern! The {faction} {direction}, and every ingot bites deeper!",
                "The great {faction} {direction} from you, and the forge pays the toll!",
                "Dark tidings — the {faction} {direction}, and the ore turns against your purse!",
                "The {faction} {direction}, and a chill settles on every price you pay!",
                "Hear and lament: the {faction} {direction}, the iron dear as gold!",
                "The {faction} {direction} toward you — the anvils ring a poorer tune!",
                "A bitter season! The {faction} {direction}, and the ledger weeps coin!",
                "Alas, the {faction} {direction}! The ore climbs a whole coin — a catastrophe measured in coppers, but felt in the soul!",
                "The great {faction} {direction} from you, and iron's price rises like a tide — a very small tide, but a cold one!"),
            [$"{Cooled}/wry"] = ImmutableList.Create(
                "The {faction} {direction} on you. Turns out they hold grudges and invoices.",
                "The {faction} {direction} — nothing personal, just pricier ore. Somewhat personal.",
                "The {faction} {direction} toward your shop. Absence makes the ore grow costlier.",
                "The {faction} {direction}. The dearer prices are, I'm told, a coincidence.",
                "The {faction} {direction} toward you. Nothing personal — well, the prices are.",
                "The {faction} {direction}. Absence makes the ore grow costlier, apparently.",
                "So the {faction} {direction}. Who knew loyalty was itemized.",
                "The {faction} {direction} on your shop. The dearer ore is 'a coincidence.'",
                "The {faction} {direction}. You forgot them; they remembered, with a surcharge.",
                "The {faction} {direction} toward you. Cold guild, warm invoice.",
                "The {faction} {direction}. They're not upset. The prices are just expressing themselves.",
                "The great {faction} {direction} on you. Grudges, now available by the ingot.",
                "The {faction} {direction} on you. There's a form for grudges; they filled it out neatly. Dearer ore, itemized.",
                "So the {faction} {direction}. Nothing personal — the surcharge, however, is extremely personal. Ore's up a coin."),
            [$"{Cooled}/omen"] = ImmutableList.Create(
                "The {faction} {direction} toward you — the candles guttered at the assay. Dearer ore, darker signs.",
                "I saw it in the slag: the {faction} {direction}. The veins turn their faces away.",
                "The {faction} {direction}. The mountain keeps its grudges; the price remembers too.",
                "When the {faction} {direction}, salt the threshold — cold guild, cold trade, costlier iron.",
                "The {faction} {direction}. The slag showed it plain. Dearer ore, darker signs.",
                "The veins turn their faces away: the {faction} {direction} from you.",
                "The {faction} {direction} on your name. Salt the threshold; cold trade follows.",
                "When the {faction} {direction}, the old ones say the ore sours. It has.",
                "The {faction} {direction}. The coals leaned away from your account tonight.",
                "The {faction} {direction} toward you. Costlier iron, and the deep's cold shoulder.",
                "The {faction} {direction}. The mountain feeds a colder table now. Yours.",
                "The {faction} {direction} from you. Dearer ore is how the deep says it's watching.",
                "I swore the {faction} would hold. They {direction} instead, and the ore turned dear. My portents are in disgrace.",
                "The signs promised warm trade. The {faction} {direction}, dearer iron in hand. Even the omens are asking for a refund."),
        },
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Favored] = "The {faction} have {direction} to your custom — cheaper ore, folk say.",
            [Cooled] = "The {faction} have {direction} toward your shop — dearer ore, folk say.",
        });
}
