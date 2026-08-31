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
/// mentions both slots. Prose leans on the price consequence the player actually feels (R7) — and
/// only the one the sim can charge: a warmer guild sells ore cheaper; a cooling one lets that earned
/// discount fade back toward the plain base ask. Standing is discount-only (KTD8:
/// <c>FactionDriftSystem.StepTowardZero</c> floors at 0, and <c>Cooled</c> stamps on the
/// favored-EXIT crossing — typically while standing is still well above zero), so ore NEVER costs
/// more than the neutral base price and no <see cref="Cooled"/> variant may claim it does.
/// <c>AdventureTicker</c>'s <c>FactionStandingShifted</c> arm carries the same rule and the long-form
/// reasoning; <c>FactionPackTests</c> tripwires the price-rise vocabulary (P2-MEMORY-09).</para>
///
/// <para><b>Fallbacks:</b> one per base key, in the same plain register — new drama with no prior
/// hardcoded line. Simple enough to always pass validation (pack conformance tests assert this).</para>
///
/// <para><b>Breadth (T8a + C4).</b> Every (baseKey, voice) key carries at least eighteen variants — the
/// launch four plus eight more in the same frozen voice register — then the C4 tone pass (design doc
/// <c>2026-07-18-variety-tone-direction.md</c> §1) adds comic-bureaucratic "permit-office" variants per
/// voice (idea #18): omen = failed portents, gruff = invoices/lectures, dramatic = grandiosity about
/// mundane coppers, wry stays wry. The C1/C2 faction-voicing pass then adds four more per key drawn from
/// the Gloomwood Wardens' permit-office deadpan (forms/stamps/clauses) and the Tidewrit Salvors'
/// superstitious warm-wry register (omens/salt/the-turning-tide/never-on-a-Thirdday). Because
/// <c>{faction}</c> is a slot, these stay generic — permit- and superstition-flavored, never Warden- or
/// Salvor-specific — so all four factions read cleanly, in the deadpan register (no puns, no fourth wall,
/// no modern slang). Every added line stays pinned to the price consequence the player actually feels
/// (R7): a rising discount on favored, a fading one on cooled. Breadth lives in this existing pack file: additive same-surface packs
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
                "The {faction} {direction}. Stamped, sealed, cheaper ore approved. Keep buying and the stamp stays wet.",
                "Permit's stamped clean — the {faction} {direction}. Ore's down a coin. Keep the receipt.",
                "The {faction} {direction}. Signed, filed, cheaper ore. Don't make me chase the form twice.",
                "The {faction} {direction}. Salt on the sill, the old hands say. The cheaper ore's real enough.",
                "They don't warm on a Thirdday, but the {faction} {direction} today. Ore's off a coin. Take it."),
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
                "The great {faction} {direction} to your name, and the price of iron bows — bows! — by an entire copper!",
                "Let the great seal descend — the {faction} {direction}, and the discount is entered by decree!",
                "By stamp and by signature, the {faction} {direction}! A single copper struck from the ore — history will note it!",
                "The tides of fortune turn! The {faction} {direction}, and the ore runs cheap as a blessed morning!",
                "Read the omens and rejoice — the {faction} {direction}, and every ingot bows a copper lower!"),
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
                "Apparently the {faction} {direction}. There's a form for affection now, filed in triplicate. The ore's down a coin regardless.",
                "The {faction} {direction}. Goodwill, stamped and countersigned, cheaper ore attached. The clerk looked almost moved.",
                "Apparently the {faction} {direction} — there's a permit for it now. The discount's real; the permit, less so.",
                "The {faction} {direction}. The signs foretold it, or the coin did. The ore's cheaper either way.",
                "They swear they never warm on a Thirdday. The {faction} {direction} regardless. Cheaper ore, no explanation offered."),
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
                "The signs said dear iron. The {faction} {direction} and made them liars. Cheaper ore, and a portent eating its words.",
                "The tide came in kind, and the {faction} {direction}. Cheaper ore rides a turning tide — mark it.",
                "Salt held its shape at the door — a friend's sign. The {faction} {direction}, and the ore comes gentle.",
                "The signs promised a delay and a levy. Instead the {faction} {direction}, ore cheap in hand. The omens filed no apology.",
                "I read dear iron in the dust. The {faction} {direction} and made it a lie — cheaper ore, and a portent left red-faced."),

            // ------------------------------------------------------------- cooled (direction = "cooled")
            // P2-MEMORY-09: cooled prose is DISCOUNT-FADING, never price-rise — standing is
            // discount-only (KTD8), so ore never costs more than the plain base ask. Same rule,
            // same reasoning as AdventureTicker's FactionStandingShifted arm.
            [$"{Cooled}/gruff"] = ImmutableList.Create(
                "The {faction} {direction} on you. The cheap ore's going. Should've kept trading.",
                "The {faction} have {direction} — neglect does that. The discount's draining.",
                "Word is the {faction} {direction} toward your shop. The discount thins. That's the trade.",
                "The {faction} {direction}. Stop buying, they stop caring. The coin off goes first.",
                "The {faction} {direction} toward your shop. Kind prices don't keep. That's neglect.",
                "Word is the {faction} {direction}. The discount fades. Nobody's fault but the empty ledger.",
                "The {faction} {direction} on your account. Mend it or lose the rate. Your call.",
                "The {faction} {direction}. The good rate wears off. Simple arithmetic.",
                "Guild's cold — the {faction} {direction}, and the discount knows it.",
                "The {faction} {direction} toward you. Fading discount, colder welcome.",
                "The {faction} {direction}. Should've fed the guild. Now it forgets you.",
                "The {faction} {direction} on your custom. Cheaper to keep a discount than to earn it twice.",
                "The {faction} {direction} on you. Reclassified your account 'neglectful.' The discount's under review. Appeals go in the usual bin.",
                "The {faction} {direction}. Marked the file 'lapsed,' discount to follow it out. Mend it or watch it go. Your ledger.",
                "Permit expired — the {faction} {direction}. The cheap-ore stamp fades with it. Renew it or pay the plain ask.",
                "The {faction} {direction}. Marked 'overdue,' the discount thinning while it sits. Should've filed on time.",
                "The {faction} {direction}. Salt spilled toward the door, the old hands say. The kind rate's leaving, and no arguing it.",
                "They don't forgive on a Thirdday, they say — and the {faction} {direction}. The discount won't wait. Mend it on a kinder one."),
            [$"{Cooled}/dramatic"] = ImmutableList.Create(
                "Alas! The {faction} have {direction} toward your forge — the cheap ore slips away!",
                "The {faction} {direction}, and iron's kindness ebbs like a tide going out!",
                "Hear it and grieve: the {faction} {direction}, and the discount withers on the vine!",
                "The great {faction} {direction} — cold shoulders, and the warm rate cooling with them!",
                "Woe! The {faction} {direction}, and the discount drains away before your eyes!",
                "Grieve, tavern! The {faction} {direction}, and every spared copper packs its bags!",
                "The great {faction} {direction} from you, and the forge's sweet rate slips through its fingers!",
                "Dark tidings — the {faction} {direction}, and the ore forgets its fondness for your purse!",
                "The {faction} {direction}, and the discount fades like a candle in a draft!",
                "Hear and lament: the {faction} {direction}, the bargain going the way of all bargains!",
                "The {faction} {direction} toward you — the anvils ring a poorer tune, and the discount fades with it!",
                "A bitter season! The {faction} {direction}, and the ledger mourns its little discount!",
                "Alas, the {faction} {direction}! A whole coin of discount, fading — a catastrophe measured in coppers, but felt in the soul!",
                "The great {faction} {direction} from you, and the discount ebbs like a tide — a very small tide, but a cold one!",
                "By stamp and by grievance, the {faction} {direction}! A copper of goodwill, struck from the books — a small loss, grandly mourned!",
                "The great seal turns its face away — the {faction} {direction}, and the discount fades by decree!",
                "The tides of fortune ebb! The {faction} {direction}, and the bargain goes out with the water!",
                "Read the omens and grieve — the {faction} {direction}, and every spared copper slips back into the guild's ledger!"),
            [$"{Cooled}/wry"] = ImmutableList.Create(
                "The {faction} {direction} on you. Turns out grudges outlast discounts. Considerably.",
                "The {faction} {direction} — nothing personal, just a fading discount. Somewhat personal.",
                "The {faction} {direction} toward your shop. Absence makes the discount grow forgetful.",
                "The {faction} {direction}. The shrinking discount is, I'm told, a coincidence.",
                "The {faction} {direction} toward you. Nothing personal — well, the discount was.",
                "The {faction} {direction}. Out of sight, out of the good-rate ledger, apparently.",
                "So the {faction} {direction}. Who knew loyalty was itemized.",
                "The {faction} {direction} on your shop. The discount is 'stepping out for a while.'",
                "The {faction} {direction}. You forgot them; they're returning the favor, one coin of discount at a time.",
                "The {faction} {direction} toward you. Cold guild, cooling discount.",
                "The {faction} {direction}. They're not upset. The discount is just quietly excusing itself.",
                "The great {faction} {direction} on you. Goodwill, now fading by the ingot.",
                "The {faction} {direction} on you. There's a form for grudges; they filled it out neatly. Fading discount, itemized.",
                "So the {faction} {direction}. Nothing personal — the way the discount is fading, however, is extremely personal.",
                "The {faction} {direction}. Grievance filed in triplicate, discount unfiled in the same motion. The clerk seemed to enjoy it.",
                "Apparently the {faction} {direction} — there's a form for disappointment now. The discount leaves, neatly itemized.",
                "The {faction} {direction}. The signs warned of it, or the empty ledger did. The discount fades either way.",
                "They never cool on a Thirdday, they claim. The {faction} {direction} regardless. The discount goes, no apology."),
            [$"{Cooled}/omen"] = ImmutableList.Create(
                "The {faction} {direction} toward you — the candles guttered at the assay. The kind price wanes, and the signs darken.",
                "I saw it in the slag: the {faction} {direction}. The veins turn their faces away, and the kind rate goes with them.",
                "The {faction} {direction}. The mountain keeps its grudges; the discount does not keep at all.",
                "When the {faction} {direction}, salt the threshold — cold guild, cold trade, the good rate going.",
                "The {faction} {direction}. The slag showed it plain. The discount thins, and the omens agree.",
                "The veins turn their faces away: the {faction} {direction} from you.",
                "The {faction} {direction} on your name. Salt the threshold; cold trade follows.",
                "When the {faction} {direction}, the old ones say the bargain sours first. It has.",
                "The {faction} {direction}. The coals leaned away from your account tonight.",
                "The {faction} {direction} toward you. A waning discount, and the deep's cold shoulder.",
                "The {faction} {direction}. The mountain feeds a colder table now. Yours.",
                "The {faction} {direction} from you. A fading discount is how the deep says it's watching.",
                "I swore the {faction} would hold. They {direction} instead, and the discount is fading. My portents are in disgrace.",
                "The signs promised warm trade. The {faction} {direction}, and the discount followed the signs out. Even the omens are asking for a refund.",
                "The tide went out cold, and the {faction} {direction}. A thinning discount rides an ebbing tide — read it plain.",
                "Salt spilled toward the sill — an ill sign. The {faction} {direction}, and the kind price drains away.",
                "The signs promised a warm season. The {faction} {direction} instead, the discount slipping. The omens keep no receipts.",
                "I read lasting cheap iron in the coals. The {faction} {direction} and made it a lie — a fading discount, and a portent hiding its face."),
        },
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Favored] = "The {faction} have {direction} to your custom — cheaper ore, folk say.",
            [Cooled] = "The {faction} have {direction} toward your shop — the ore's discount is fading, folk say.",
        });
}
