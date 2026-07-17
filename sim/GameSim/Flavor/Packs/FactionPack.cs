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
                "The {faction} {direction} toward your account. Ore's cheaper this season."),
            [$"{Favored}/dramatic"] = ImmutableList.Create(
                "Rejoice! The {faction} have {direction} to your forge — the ore flows cheap!",
                "The great {faction} {direction} at last, and the price of iron bows before you!",
                "Sing it through the town: the {faction} {direction}, and every pick comes kinder!",
                "Behold — the {faction} {direction} to you, and the ledger sings a sweeter tune!"),
            [$"{Favored}/wry"] = ImmutableList.Create(
                "The {faction} {direction} to you. Miracles happen; so do discounts.",
                "Turns out the {faction} {direction} — apparently coin buys affection. Who knew.",
                "The {faction} {direction} to your shop. Enjoy the cheaper ore before they remember themselves.",
                "The mighty {faction} {direction}. The ore's cheaper; try to look surprised."),
            [$"{Favored}/omen"] = ImmutableList.Create(
                "The {faction} {direction} to you — the coals burned blue last night. The deep favors your coin.",
                "I read it in the ore-dust: the {faction} {direction}. Kinder prices ride a kind wind.",
                "The {faction} {direction}. Mark it — the mountain remembers who feeds its guild.",
                "When the {faction} {direction}, the old miners say the veins run richer. Cheaper ore, and an omen."),

            // ------------------------------------------------------------- cooled (direction = "cooled")
            [$"{Cooled}/gruff"] = ImmutableList.Create(
                "The {faction} {direction} on you. Ore costs more now. Should've kept trading.",
                "The {faction} have {direction} — neglect does that. The picks come dearer.",
                "Word is the {faction} {direction} toward your shop. Prices climb. That's the trade.",
                "The {faction} {direction}. Stop buying, they stop caring. Ore's up a coin."),
            [$"{Cooled}/dramatic"] = ImmutableList.Create(
                "Alas! The {faction} have {direction} toward your forge — the ore turns dear!",
                "The {faction} {direction}, and the price of iron rises like a tide against you!",
                "Hear it and grieve: the {faction} {direction}, and every pick bites deeper into the purse!",
                "The great {faction} {direction} — cold shoulders, and colder prices!"),
            [$"{Cooled}/wry"] = ImmutableList.Create(
                "The {faction} {direction} on you. Turns out they hold grudges and invoices.",
                "The {faction} {direction} — nothing personal, just pricier ore. Somewhat personal.",
                "The {faction} {direction} toward your shop. Absence makes the ore grow costlier.",
                "The {faction} {direction}. The dearer prices are, I'm told, a coincidence."),
            [$"{Cooled}/omen"] = ImmutableList.Create(
                "The {faction} {direction} toward you — the candles guttered at the assay. Dearer ore, darker signs.",
                "I saw it in the slag: the {faction} {direction}. The veins turn their faces away.",
                "The {faction} {direction}. The mountain keeps its grudges; the price remembers too.",
                "When the {faction} {direction}, salt the threshold — cold guild, cold trade, costlier iron."),
        },
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Favored] = "The {faction} have {direction} to your custom — cheaper ore, folk say.",
            [Cooled] = "The {faction} have {direction} toward your shop — dearer ore, folk say.",
        });
}
